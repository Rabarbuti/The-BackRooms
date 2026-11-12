"""Base para esculpir personagens por script no Blender (sem janela).

A forma de cada peça é uma função de distância com sinal (SDF): negativa dentro, positiva fora.
Peças simples (elipsoide, cápsula, caixa) se juntam com união suave (`smin`) e se cavam com
subtração suave (`smax(d, -outra)`), como argila: é assim que saem crânio, nariz, vincos e chapéu
sem cara de balão. A função é amostrada numa grade e vira malha por "surface nets"
(`sdf_mesh`). As cores (pele, hematoma, sangue, desgaste) são pintadas nos vértices por outra
função da posição.

Coordenadas do Blender: Z para cima, frente do personagem em -Y, esquerda dele em +X.

Uso: um script por personagem em `Tools/Blender/personagens/`, que importa este módulo.
"""

import math
import os
import sys
import time
from itertools import product

import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PHOTO_DIR = os.path.join(ROOT, "Logs", "Blender", "personagens")


# ------------------------------------------------------------------ vetores

def v3(x, y, z):
    return np.array((x, y, z), dtype=np.float64)


def norm(v):
    v = np.asarray(v, dtype=np.float64)
    return v / max(1e-12, float(np.linalg.norm(v)))


def length(v):
    return np.sqrt((v * v).sum(-1))


def rot(x=0.0, y=0.0, z=0.0):
    """Matriz 3x3 (colunas = eixos locais no mundo): gira X, depois Y, depois Z (graus)."""
    ax, ay, az = (math.radians(a) for a in (x, y, z))
    rx = np.array(((1, 0, 0), (0, math.cos(ax), -math.sin(ax)), (0, math.sin(ax), math.cos(ax))))
    ry = np.array(((math.cos(ay), 0, math.sin(ay)), (0, 1, 0), (-math.sin(ay), 0, math.cos(ay))))
    rz = np.array(((math.cos(az), -math.sin(az), 0), (math.sin(az), math.cos(az), 0), (0, 0, 1)))
    return rz @ ry @ rx


def frame_from(u, hint):
    """Base ortonormal com o primeiro eixo em `u` e o segundo o mais perto possível de `hint`."""
    u = norm(u)
    s = norm(np.asarray(hint) - u * float(np.dot(hint, u)))
    n = np.cross(u, s)
    return np.stack((u, s, n), axis=1)


class Frame:
    """Sistema local: origem, rotação (colunas = eixos) e escala. Converte pontos do mundo."""

    def __init__(self, origin, rotation=None, scale=1.0):
        self.o = np.asarray(origin, dtype=np.float64)
        self.r = np.eye(3) if rotation is None else np.asarray(rotation, dtype=np.float64)
        self.s = float(scale)

    def local(self, P):
        return ((P - self.o) @ self.r) / self.s

    def world(self, q):
        return self.o + (np.asarray(q, dtype=np.float64) * self.s) @ self.r.T

    def wrap(self, fn):
        """Função de distância escrita em coordenadas locais, avaliada com pontos do mundo."""
        return lambda P: fn(self.local(P)) * self.s


# ------------------------------------------------------------------ ruído

def _hash3(ix, iy, iz, seed):
    h = (ix * 374761393 + iy * 668265263 + iz * 1442695041 + seed * 144665) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h = h ^ (h >> 16)
    return (h & 0xFFFFFF).astype(np.float64) / float(0xFFFFFF)


def vnoise(P, freq, seed=0):
    """Ruído de valor 3D, de -1 a 1, suave."""
    Q = P * freq
    i = np.floor(Q).astype(np.int64)
    f = Q - i
    u = f * f * (3.0 - 2.0 * f)
    out = np.zeros(len(P))
    for dx, dy, dz in product((0, 1), repeat=3):
        w = (u[:, 0] if dx else 1 - u[:, 0]) * (u[:, 1] if dy else 1 - u[:, 1]) * (u[:, 2] if dz else 1 - u[:, 2])
        out += w * _hash3(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)
    return out * 2.0 - 1.0


def fbm(P, freq, octaves=4, seed=0, gain=0.5):
    total, amp, norm_ = np.zeros(len(P)), 1.0, 0.0
    for o in range(octaves):
        total += amp * vnoise(P, freq * (2.0 ** o), seed + o * 17)
        norm_ += amp
        amp *= gain
    return total / norm_


# ------------------------------------------------------------------ formas (SDF)

def sphere(Q, c, r):
    return length(Q - c) - r


def ellipsoid(Q, c, r, R=None):
    p = Q - c
    if R is not None:
        p = p @ R
    r = np.asarray(r, dtype=np.float64)
    p = p / r
    k0 = length(p)
    k1 = length(p / r)
    return k0 * (k0 - 1.0) / np.maximum(k1, 1e-9)


def capsule(Q, a, b, r, r2=None):
    """Cápsula de `a` a `b`; com `r2`, o raio vai de `r` a `r2` (cone arredondado aproximado)."""
    a = np.asarray(a, dtype=np.float64)
    b = np.asarray(b, dtype=np.float64)
    pa = Q - a
    ba = b - a
    h = np.clip((pa @ ba) / max(1e-12, float(ba @ ba)), 0.0, 1.0)
    rad = r if r2 is None else r + (r2 - r) * h
    return length(pa - h[:, None] * ba) - rad


def box(Q, c, half, rounding=0.0, R=None):
    p = Q - c
    if R is not None:
        p = p @ R
    q = np.abs(p) - (np.asarray(half) - rounding)
    return length(np.maximum(q, 0.0)) + np.minimum(q.max(-1), 0.0) - rounding


def smin(a, b, k):
    if k <= 0:
        return np.minimum(a, b)
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b + (a - b) * h - k * h * (1.0 - h)


def smax(a, b, k):
    return -smin(-a, -b, k)


def polyline(Q, pts, r, k=0.0):
    """União de cápsulas por uma lista de pontos; `r` pode ser um raio por ponto."""
    radii = r if isinstance(r, (list, tuple, np.ndarray)) else [r] * len(pts)
    pts = [np.asarray(p, dtype=np.float64) for p in pts]
    # só calcula os pontos perto da linha; os outros ficam "longe" (vale para vincos e fitas,
    # que se juntam com raio de suavização pequeno)
    pad = max(radii) + k + 0.02
    A = np.array(pts)
    near = np.all((Q > A.min(0) - pad) & (Q < A.max(0) + pad), axis=1)
    out = np.full(len(Q), pad, dtype=np.float64)
    if not near.any():
        return out
    Qn = Q[near]
    d = None
    for i in range(len(pts) - 1):
        di = capsule(Qn, pts[i], pts[i + 1], radii[i], radii[i + 1])
        d = di if d is None else smin(d, di, k)
    out[near] = np.minimum(d, pad)
    return out


def catmull(points, steps=6):
    """Curva suave passando pelos pontos (Catmull-Rom), para vincos, fitas e cipós."""
    P = [np.asarray(p, dtype=np.float64) for p in points]
    P = [P[0] * 2 - P[1]] + P + [P[-1] * 2 - P[-2]]
    out = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for s in range(steps):
            t = s / steps
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(P[-2])
    return out


def ribbon(Q, pts, width_dir, half_w, half_t):
    """Fita: retângulo de meia largura `half_w` e meia espessura `half_t` varrido pelos pontos.
    A largura segue `width_dir` (um vetor, ou um por ponto)."""
    d = None
    for i in range(len(pts) - 1):
        a, b = np.asarray(pts[i]), np.asarray(pts[i + 1])
        ba = b - a
        L2 = max(1e-12, float(ba @ ba))
        h = np.clip(((Q - a) @ ba) / L2, 0.0, 1.0)
        v = Q - (a + h[:, None] * ba)
        t = ba / math.sqrt(L2)
        wd = width_dir[i] if isinstance(width_dir, list) else width_dir
        w = norm(np.asarray(wd) - t * float(np.dot(wd, t)))
        n = np.cross(t, w)
        hw = half_w[i] if isinstance(half_w, list) else half_w
        q = np.stack((np.abs(v @ w) - hw, np.abs(v @ n) - half_t), axis=1)
        # extremos do segmento: fecha pela distância ao longo de t
        along = np.maximum(-(Q - a) @ t, (Q - b) @ t)
        q2 = np.concatenate((q, along[:, None]), axis=1)
        di = length(np.maximum(q2, 0.0)) + np.minimum(q2.max(-1), 0.0)
        d = di if d is None else np.minimum(d, di)
    return d


def project(fn, p, steps=6, eps=1e-4):
    """Leva um ponto (ou uma lista) para a superfície de `fn` por passos de Newton."""
    P = np.atleast_2d(np.asarray(p, dtype=np.float64)).copy()
    for _ in range(steps):
        d = fn(P)
        g = np.stack([(fn(P + e) - d) / eps for e in np.eye(3) * eps], axis=1)
        g /= np.maximum(length(g)[:, None], 1e-9)
        P -= d[:, None] * g
    return P if np.ndim(p) > 1 else P[0]


def normal_at(fn, p, eps=1e-4):
    P = np.atleast_2d(np.asarray(p, dtype=np.float64))
    d = fn(P)
    g = np.stack([(fn(P + e) - d) / eps for e in np.eye(3) * eps], axis=1)
    g /= np.maximum(length(g)[:, None], 1e-9)
    return g if np.ndim(p) > 1 else g[0]


def on_surface(fn, pts, lift=0.0):
    """Projeta pontos na superfície e os afasta `lift` para fora (sobrancelha, vinco, botão)."""
    P = project(fn, np.asarray(pts, dtype=np.float64))
    return list(P + normal_at(fn, P) * lift)


# ------------------------------------------------------------------ da forma à malha

def _eval_points(fn, P, chunk=1_500_000):
    out = np.empty(len(P), dtype=np.float32)
    for i in range(0, len(P), chunk):
        out[i:i + chunk] = fn(P[i:i + chunk])
    return out


def sample(fn, lo, hi, h, coarse=4):
    """Amostra `fn` na grade. Primeiro numa grade `coarse` vezes mais rala; na fina, só perto da
    superfície (o resto recebe só o sinal), o que corta o tempo por dez em peças cheias de vincos."""
    lo = np.asarray(lo, dtype=np.float64)
    n = np.maximum(2, np.ceil((np.asarray(hi) - lo) / h).astype(int) + 1)
    xs = lo[0] + np.arange(n[0]) * h
    ys = lo[1] + np.arange(n[1]) * h
    zs = lo[2] + np.arange(n[2]) * h
    if coarse > 1 and n.min() > coarse * 3:
        Fc = sample(fn, lo, lo + (np.ceil((n - 1) / coarse)) * coarse * h, h * coarse, coarse=0)
        ci = [np.clip(np.rint(np.arange(n[a]) / coarse).astype(int), 0, Fc.shape[a] - 1) for a in range(3)]
        near = Fc[np.ix_(ci[0], ci[1], ci[2])]
        margin = 1.6 * (coarse * h * 0.87 + h)
        F = np.where(near < 0, -margin, margin).astype(np.float32)
        band = np.nonzero(np.abs(near) < margin)
        del near
        P = np.stack((xs[band[0]], ys[band[1]], zs[band[2]]), axis=1)
        F[band] = _eval_points(fn, P)
        return F
    F = np.empty(tuple(n), dtype=np.float32)
    yy, zz = np.meshgrid(ys, zs, indexing="ij")
    plane = np.stack((np.zeros(yy.size), yy.ravel(), zz.ravel()), axis=1)
    slab = max(1, int(2_000_000 // plane.shape[0]))
    for i in range(0, n[0], slab):
        cnt = min(slab, n[0] - i)
        P = np.tile(plane, (cnt, 1))
        P[:, 0] = np.repeat(xs[i:i + cnt], plane.shape[0])
        F[i:i + cnt] = fn(P).reshape(cnt, n[1], n[2])
    return F


def surface_nets(F, origin, h):
    """Malha de quadrados na superfície F = 0 (dentro negativo), virada para fora."""
    nx, ny, nz = F.shape
    inside = F < 0
    count = np.zeros((nx - 1, ny - 1, nz - 1), np.uint8)
    for i, j, k in product((0, 1), repeat=3):
        count += inside[i:nx - 1 + i, j:ny - 1 + j, k:nz - 1 + k]
    active = (count > 0) & (count < 8)

    acc = np.zeros((nx - 1, ny - 1, nz - 1, 3), np.float32)
    num = np.zeros((nx - 1, ny - 1, nz - 1), np.float32)
    idx = [np.arange(nx, dtype=np.float32), np.arange(ny, dtype=np.float32), np.arange(nz, dtype=np.float32)]
    for axis in range(3):
        a = np.take(F, range(F.shape[axis] - 1), axis=axis)
        b = np.take(F, range(1, F.shape[axis]), axis=axis)
        m = (a < 0) != (b < 0)
        t = np.where(m, a / np.where(m, a - b, 1.0), 0.0).astype(np.float32)
        shape = a.shape
        coords = []
        for c in range(3):
            g = idx[c][:shape[c]].reshape([-1 if q == c else 1 for q in range(3)])
            g = np.broadcast_to(g, shape)
            coords.append(g + t if c == axis else g)
        pos = np.stack(coords, axis=-1)
        others = [q for q in range(3) if q != axis]
        for d1, d2 in product((0, 1), repeat=2):
            sl = [slice(None)] * 3
            sl[others[0]] = slice(d1, d1 + (shape[others[0]] - 1))
            sl[others[1]] = slice(d2, d2 + (shape[others[1]] - 1))
            sl = tuple(sl)
            mm = m[sl]
            acc += np.where(mm[..., None], pos[sl], 0.0)
            num += mm
    cell_id = -np.ones(active.shape, np.int64)
    cid = np.nonzero(active)
    cell_id[cid] = np.arange(len(cid[0]))
    verts = acc[cid] / np.maximum(num[cid], 1)[:, None]
    verts = np.asarray(origin) + verts.astype(np.float64) * h

    faces = []
    for axis in range(3):
        o1, o2 = (axis + 1) % 3, (axis + 2) % 3
        a = np.take(F, range(F.shape[axis] - 1), axis=axis)
        b = np.take(F, range(1, F.shape[axis]), axis=axis)
        m = (a < 0) != (b < 0)
        e = np.argwhere(m)
        keep = (e[:, o1] >= 1) & (e[:, o1] <= F.shape[o1] - 2) & (e[:, o2] >= 1) & (e[:, o2] <= F.shape[o2] - 2)
        e = e[keep]
        if len(e) == 0:
            continue
        flip = a[tuple(e.T)] >= 0
        quad = []
        for d1, d2 in ((1, 1), (0, 1), (0, 0), (1, 0)):
            c = e.copy()
            c[:, o1] -= d1
            c[:, o2] -= d2
            quad.append(cell_id[tuple(c.T)])
        q = np.stack(quad, axis=1)
        q[flip] = q[flip][:, ::-1]
        faces.append(q)
    faces = np.concatenate(faces) if faces else np.zeros((0, 4), np.int64)
    faces = faces[(faces >= 0).all(axis=1)]
    return verts, faces


def taubin(verts, faces, iterations=2, lam=0.5, mu=-0.53):
    edges = np.concatenate([faces[:, [0, 1]], faces[:, [1, 2]], faces[:, [2, 3]], faces[:, [3, 0]]])
    edges = np.concatenate([edges, edges[:, ::-1]])
    deg = np.bincount(edges[:, 0], minlength=len(verts)).astype(np.float64)
    V = verts.copy()
    for _ in range(iterations):
        for f in (lam, mu):
            s = np.zeros_like(V)
            np.add.at(s, edges[:, 0], V[edges[:, 1]])
            avg = s / np.maximum(deg, 1)[:, None]
            V += f * (avg - V) * (deg > 0)[:, None]
    return V


def srgb_to_linear(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def make_mesh(name, verts, faces, colors=None, material=None, collection=None):
    me = bpy.data.meshes.new(name)
    me.vertices.add(len(verts))
    me.vertices.foreach_set("co", verts.astype(np.float32).ravel())
    corners = faces.shape[1]
    me.loops.add(len(faces) * corners)
    me.loops.foreach_set("vertex_index", faces.astype(np.int32).ravel())
    me.polygons.add(len(faces))
    me.polygons.foreach_set("loop_start", np.arange(0, len(faces) * corners, corners, dtype=np.int32))
    me.update(calc_edges=True)
    me.validate(clean_customdata=False)
    me.shade_smooth()
    if colors is not None:
        attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
        lin = srgb_to_linear(np.clip(colors, 0, 1))
        rgba = np.concatenate((lin, np.ones((len(lin), 1))), axis=1).astype(np.float32)
        attr.data.foreach_set("color", rgba.ravel())
    if material is not None:
        me.materials.append(material)
    obj = bpy.data.objects.new(name, me)
    (collection or bpy.context.scene.collection).objects.link(obj)
    return obj


def sdf_mesh(name, fn, lo, hi, h, color=None, material=None, smooth=1, collection=None):
    """Esculpe a peça: amostra `fn` na caixa [lo, hi] com passo `h`, faz a malha, alisa e pinta
    com `color(P) -> (N, 3) sRGB`."""
    t0 = time.time()
    F = sample(fn, lo, hi, h)
    verts, faces = surface_nets(F, lo, h)
    if len(faces) == 0:
        print(f"[personagem] {name}: VAZIO (a forma não cruza a caixa)")
        return None
    if smooth:
        verts = taubin(verts, faces, smooth)
    cols = color(verts) if color is not None else None
    obj = make_mesh(name, verts, faces, cols, material, collection)
    print(f"[personagem] {name}: {len(verts)} vértices, {len(faces)} quadrados, {time.time() - t0:.1f} s")
    return obj


def sphere_mesh(name, center, radius, rows=48, cols=64, color=None, material=None, R=None, collection=None):
    """Esfera UV (olho), pintada por `color(dir_local) -> sRGB`."""
    verts, dirs = [], []
    for i in range(rows + 1):
        th = math.pi * i / rows
        for j in range(cols):
            ph = 2 * math.pi * j / cols
            d = np.array((math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th)))
            dirs.append(d)
    dirs = np.array(dirs)
    Rm = np.eye(3) if R is None else R
    verts = np.asarray(center) + (dirs * radius) @ Rm.T
    faces = []
    for i in range(rows):
        for j in range(cols):
            a = i * cols + j
            b = i * cols + (j + 1) % cols
            faces.append((a, a + cols, b + cols, b))
    faces = np.array(faces)
    cols_ = color(dirs) if color is not None else None
    return make_mesh(name, verts, faces, cols_, material, collection)


# ------------------------------------------------------------------ materiais e cena

def material(name, rough=0.6, metal=0.0, spec=0.5, sss=0.0, sheen=0.0, coat=0.0, base=None,
             bump=0.0, bump_scale=900.0):
    """Material com a cor tirada da pintura dos vértices (atributo "Col"). `bump` põe um relevo
    fino de ruído (poros, trama do pano): só nas fotos por enquanto; no Unity vira mapa de normais."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Specular IOR Level"].default_value = spec
    if sss > 0:
        bsdf.inputs["Subsurface Weight"].default_value = sss
        bsdf.inputs["Subsurface Radius"].default_value = (0.6, 0.25, 0.15)
        bsdf.inputs["Subsurface Scale"].default_value = 0.01
    if sheen > 0:
        bsdf.inputs["Sheen Weight"].default_value = sheen
    if coat > 0:
        bsdf.inputs["Coat Weight"].default_value = coat
    if base is None:
        attr = nt.nodes.new("ShaderNodeAttribute")
        attr.attribute_name = "Col"
        nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = tuple(srgb_to_linear(base)) + (1.0,)
    if bump > 0:
        tc = nt.nodes.new("ShaderNodeTexCoord")
        noise = nt.nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = bump_scale
        noise.inputs["Detail"].default_value = 3.0
        nt.links.new(tc.outputs["Object"], noise.inputs["Vector"])
        bn = nt.nodes.new("ShaderNodeBump")
        bn.inputs["Strength"].default_value = bump
        bn.inputs["Distance"].default_value = 0.0005
        nt.links.new(noise.outputs["Fac"], bn.inputs["Height"])
        nt.links.new(bn.outputs["Normal"], bsdf.inputs["Normal"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return m


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras):
        for b in list(coll):
            coll.remove(b)


def stage(wall=(0.72, 0.64, 0.38), floor=(0.42, 0.36, 0.22), ceiling=3.0, warm=True, dim=1.0):
    """Pedaço de sala dos Backrooms para as fotos: piso, duas paredes, teto e luminárias."""
    def plane(name, size, loc, rot_, color, rough=0.9):
        bpy.ops.mesh.primitive_plane_add(size=1, location=loc, rotation=rot_)
        o = bpy.context.active_object
        o.name = name
        o.scale = (size[0], size[1], 1)
        o.data.materials.append(material("Cena_" + name, rough=rough, base=color))
        return o

    plane("Piso", (14, 14), (0, 0, 0), (0, 0, 0), floor, 0.95)
    plane("Fundo", (14, ceiling), (0, 3.2, ceiling / 2), (math.radians(90), 0, 0), wall)
    plane("Lado", (14, ceiling), (-3.5, 0, ceiling / 2), (math.radians(90), 0, math.radians(90)), wall)
    plane("Teto", (14, 14), (0, 0, ceiling), (math.radians(180), 0, 0), (0.8, 0.78, 0.7))
    color = (1.0, 0.93, 0.78) if warm else (0.95, 0.97, 1.0)
    for i, (x, y) in enumerate(((0.0, -1.6), (0.0, 1.8), (-2.5, -0.5))):
        ld = bpy.data.lights.new(f"Luminaria{i}", "AREA")
        ld.shape = "RECTANGLE"
        ld.size, ld.size_y = 1.2, 0.3
        ld.energy = (260 if i == 0 else 170) * dim
        ld.color = color
        lo = bpy.data.objects.new(ld.name, ld)
        lo.location = (x, y, ceiling - 0.02)
        bpy.context.scene.collection.objects.link(lo)
    world = bpy.data.worlds.get("Mundo") or bpy.data.worlds.new("Mundo")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.05, 0.045, 0.03, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
    bpy.context.scene.world = world


def shoot(path, eye, target, lens=50, size=(1000, 1000), samples=48):
    scene = bpy.context.scene
    cd = bpy.data.cameras.new("Foto")
    cd.lens = lens
    cd.clip_start = 0.02
    cam = bpy.data.objects.new("Foto", cd)
    scene.collection.objects.link(cam)
    cam.location = Vector(eye)
    cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = samples
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    os.makedirs(os.path.dirname(path), exist_ok=True)
    scene.render.filepath = path
    t0 = time.time()
    bpy.ops.render.render(write_still=True)
    print(f"[personagem] foto {os.path.basename(path)} ({time.time() - t0:.1f} s)")
    bpy.data.objects.remove(cam)
    bpy.data.cameras.remove(cd)


def triangles(objs):
    total = 0
    for o in objs:
        if o.type == "MESH":
            total += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return total


def run(main):
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(3)
