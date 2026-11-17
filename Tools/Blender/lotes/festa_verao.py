"""
Lote "festa_verao": as vitrines de Festa e de Verão do Level 1 (Docs/level1-mostruario.md,
seções 3.1 e 3.2). Mesa de bolos, cacho de balões e bandeirinhas; espreguiçadeira, guarda-sol e
bola de praia.

    blender -b --factory-startup -P Tools/Blender/lotes/festa_verao.py [-- --fotos]

As peças, a paleta comum e a exportação vêm de Tools/Blender/moveis_base.py. Esferas, a cobertura
do guarda-sol e os gomos coloridos são feitos aqui direto no bmesh do Part, mas cada peça passa
por Part._finish_piece (faces para fora acertadas na própria peça, regra do projeto).

As Bandeirinhas são a exceção à regra "origem no chão": a origem fica no meio do cordão, na
altura das pontas (o Unity pendura no alto do vão), e o resto cai abaixo de z = 0. O run_lote
reclama disso (sai com 3); o fim deste script refaz a conferência aceitando só esse caso
(cordão com as pontas em z = 0) e sai com 0 se o resto estiver certo.
"""

import math
import os
import random
import sys

import bmesh
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from moveis_base import TRI_BUDGET, Part, main_guard, run_lote, size_of, triangles  # noqa: E402

import bpy  # noqa: E402

# Cores novas deste lote: nome -> (cor sRGB, rugosidade, metal). Cores de festa um pouco gastas.
PALETA = {
    # Festa
    "Festa_Toalha_Branca": ((226, 222, 210), 0.85, 0.0),
    "Festa_Glace_Rosa": ((222, 146, 166), 0.45, 0.0),
    "Festa_Glace_Branca": ((240, 234, 222), 0.45, 0.0),
    "Festa_Prato_Papel": ((234, 230, 220), 0.8, 0.0),
    "Festa_Prato_Rosa": ((214, 130, 156), 0.8, 0.0),
    "Festa_Copo_Vermelho": ((184, 44, 40), 0.5, 0.0),
    "Festa_Copo_Azul": ((44, 92, 172), 0.5, 0.0),
    "Festa_Copo_Amarelo": ((222, 186, 52), 0.5, 0.0),
    "Festa_Copo_Verde": ((62, 146, 74), 0.5, 0.0),
    "Festa_Vela": ((236, 214, 120), 0.5, 0.0),
    "Festa_Chama": ((250, 170, 60), 0.3, 0.0),
    "Festa_Balao_Vermelho": ((176, 38, 40), 0.32, 0.0),
    "Festa_Balao_Amarelo": ((214, 176, 44), 0.32, 0.0),
    "Festa_Balao_Azul": ((38, 80, 164), 0.32, 0.0),
    "Festa_Balao_Verde": ((50, 136, 66), 0.32, 0.0),
    "Festa_Fita": ((218, 208, 190), 0.5, 0.0),
    "Festa_Peso_Prata": ((176, 176, 182), 0.3, 0.8),
    "Festa_Barbante": ((206, 192, 160), 0.9, 0.0),
    "Festa_Papel_Rosa": ((218, 120, 160), 0.85, 0.0),
    "Festa_Papel_Amarelo": ((226, 196, 60), 0.85, 0.0),
    "Festa_Papel_Azul": ((60, 118, 196), 0.85, 0.0),
    "Festa_Papel_Verde": ((72, 160, 84), 0.85, 0.0),
    "Festa_Papel_Laranja": ((230, 128, 44), 0.85, 0.0),
    # Verão
    "Verao_Aluminio": ((186, 190, 194), 0.35, 0.85),
    "Verao_Lona_Azul": ((34, 82, 158), 0.85, 0.0),
    "Verao_Lona_Branca": ((230, 230, 222), 0.85, 0.0),
    "Verao_Borracha": ((30, 30, 32), 0.85, 0.0),
    "Verao_Concreto": ((150, 146, 138), 0.92, 0.0),
    "Verao_Mastro_Branco": ((232, 232, 226), 0.4, 0.0),
    "Verao_Tecido_Vermelho": ((182, 38, 36), 0.8, 0.0),
    "Verao_Tecido_Branco": ((234, 230, 220), 0.8, 0.0),
    "Verao_Bola_Branca": ((238, 236, 230), 0.3, 0.0),
    "Verao_Bola_Vermelha": ((196, 40, 36), 0.3, 0.0),
    "Verao_Bola_Amarela": ((236, 196, 40), 0.3, 0.0),
    "Verao_Bola_Azul": ((36, 92, 186), 0.3, 0.0),
}


# --------------------------------------------------------------------------- Peças extras
# Espaço do móvel: X para a direita, -Y é a frente, Z para cima, origem no chão no meio.

def piece_faces(verts):
    return list({f for v in verts for f in v.link_faces})


def sphere(p, center, radius, material, u=12, v=8, scale=(1.0, 1.0, 1.0), taper=0.0):
    """Esfera (ou elipsoide, com scale) fechada. taper afina a metade de baixo (balão).
    Devolve os vértices; as cores por gomo se acertam depois em local_paint."""
    r = bmesh.ops.create_uvsphere(p.bm, u_segments=u, v_segments=v, radius=radius,
                                  matrix=Matrix.Identity(4))
    verts = r["verts"]
    for vert in verts:
        x, y, z = vert.co
        if z < 0 and taper > 0:
            k = 1.0 - taper * (-z / radius)
            x, y = x * k, y * k
        vert.co = Vector((x * scale[0], y * scale[1], z * scale[2])) + Vector(center)
    p._finish_piece(verts, material, 0.0, smooth=True)
    return verts


def paint(p, verts, choose):
    """Troca o material de cada face da peça: choose(centro da face) -> nome do material."""
    for f in piece_faces(verts):
        f.material_index = p.mat(choose(f.calc_center_median()))


def stretch_to(p, a, b, radius, material, segments=6):
    p.tube([a, b], radius, material, segments=segments)


# --------------------------------------------------------------------------- Festa

def mesa_de_bolos(name="MesaDeBolos"):
    """Mesa de festa com toalha branca pregueada quase até o chão, bolo de dois andares rosa e
    branco, pratos de papel e copinhos: 1,60 x 0,80 x 0,75 m (mais o bolo)."""
    p = Part(name)
    rng = random.Random(7101)
    top = 0.75
    # Pés de metal aparecendo debaixo da toalha.
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.cylinder((sx * 0.72, sy * 0.32, 0.0), 0.07, 0.018, "Metal_Escuro", segments=8)
    # Toalha: um bloco só com o contorno em zigue-zague (pregas) de 4 cm do chão até o tampo.
    hw, hd = 0.80, 0.40
    corners = [(-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)]
    outline = []
    for i in range(4):
        (x0, y0), (x1, y1) = corners[i], corners[(i + 1) % 4]
        length = math.hypot(x1 - x0, y1 - y0)
        nx, ny = (y1 - y0) / length, -(x1 - x0) / length   # normal para fora (contorno anti-horário)
        n = max(2, round(length / 0.08))
        for s in range(n):
            t = s / n
            out = 0.014 if s % 2 else 0.0
            outline.append((x0 + (x1 - x0) * t + nx * out, y0 + (y1 - y0) * t + ny * out))
    p.prism(outline, 0.045, top - 0.012, "Festa_Toalha_Branca")
    # Tampo da toalha (liso, um fio mais largo que o bloco, com a quina macia).
    p.box((0, 0, top - 0.008), (2 * hw + 0.012, 2 * hd + 0.012, 0.016), "Festa_Toalha_Branca", bevel=0.006)

    # Bolo de dois andares sobre um disco: andar de baixo rosa com faixa branca, de cima branco
    # com faixa rosa, e quatro velinhas.
    cx, cy = 0.0, 0.02
    z = top
    p.cylinder((cx, cy, z), 0.012, 0.21, "Festa_Glace_Branca", segments=24)
    z += 0.012
    p.cylinder((cx, cy, z), 0.13, 0.16, "Festa_Glace_Rosa", segments=24)
    p.cylinder((cx, cy, z), 0.018, 0.165, "Festa_Glace_Branca", segments=24)
    p.cylinder((cx, cy, z + 0.12), 0.014, 0.164, "Festa_Glace_Branca", segments=24, radius_top=0.15)
    z += 0.13
    p.cylinder((cx, cy, z), 0.10, 0.11, "Festa_Glace_Branca", segments=20)
    p.cylinder((cx, cy, z), 0.016, 0.114, "Festa_Glace_Rosa", segments=20)
    p.cylinder((cx, cy, z + 0.09), 0.012, 0.113, "Festa_Glace_Rosa", segments=20, radius_top=0.10)
    z += 0.10
    for k in range(4):
        a = math.radians(45 + 90 * k)
        x, y = cx + math.cos(a) * 0.055, cy + math.sin(a) * 0.055
        p.cylinder((x, y, z), 0.065, 0.005, "Festa_Vela", segments=6)
        p.cylinder((x, y, z + 0.068), 0.018, 0.006, "Festa_Chama", segments=6, radius_top=0.0005)

    # Duas pilhas de pratos de papel, levemente tortas, à esquerda.
    for (px, py, mat, n) in ((-0.55, -0.12, "Festa_Prato_Papel", 9), (-0.52, 0.17, "Festa_Prato_Rosa", 6)):
        zz = top
        for i in range(n):
            ox, oy = rng.uniform(-0.006, 0.006), rng.uniform(-0.006, 0.006)
            p.cylinder((px + ox, py + oy, zz), 0.005, 0.085, mat, segments=16, radius_top=0.11)
            zz += 0.006
    # Copinhos coloridos à direita: alguns em pé, dois tombados.
    cups = ["Festa_Copo_Vermelho", "Festa_Copo_Azul", "Festa_Copo_Amarelo", "Festa_Copo_Verde"]
    spots = [(0.42, -0.18), (0.50, -0.06), (0.58, -0.20), (0.62, 0.05), (0.46, 0.10), (0.68, -0.10)]
    for i, (x, y) in enumerate(spots):
        mat = cups[i % 4]
        p.cylinder((x, y, top), 0.09, 0.026, mat, segments=12, radius_top=0.036)
    for (x, y, ang, mat) in ((0.30, 0.20, 30, "Festa_Copo_Verde"), (0.66, 0.24, -70, "Festa_Copo_Vermelho")):
        # Tombado: deitado no eixo -Y girado em Z; apoiado no tampo pela borda da boca.
        p.cylinder((x, y, top + 0.031), 0.09, 0.026, mat, segments=12, radius_top=0.036,
                   rot=(90 - 6, 0, ang))
    # Chapeuzinho de festa caído perto do bolo.
    p.cylinder((-0.28, -0.20, top), 0.15, 0.055, "Festa_Copo_Azul", segments=12, radius_top=0.002)
    return p.to_object()


def cacho_de_baloes(name="CachoDeBaloes"):
    """Sete balões de látex a alturas de 1,3 a 1,9 m, fitas finas até um peso no chão."""
    p = Part(name)
    rng = random.Random(7202)
    colors = ["Festa_Balao_Vermelho", "Festa_Balao_Amarelo", "Festa_Balao_Azul", "Festa_Balao_Verde",
              "Festa_Balao_Vermelho", "Festa_Balao_Azul", "Festa_Balao_Amarelo"]
    # (x, y, z do centro): espalhados num cacho de ~0,7 m, o mais alto encostando em 1,9 m.
    centers = [(0.00, 0.02, 1.72), (-0.21, -0.06, 1.55), (0.20, -0.10, 1.60), (0.04, -0.21, 1.42),
               (-0.14, 0.20, 1.66), (0.21, 0.12, 1.75), (-0.02, 0.21, 1.46)]
    radius = 0.135
    stretch = 1.17
    weight_top = (0.0, 0.0, 0.075)
    # Peso de papel-alumínio no chão: base e um nó em cima.
    p.cylinder((0, 0, 0), 0.05, 0.055, "Festa_Peso_Prata", segments=10, radius_top=0.045)
    p.cylinder((0, 0, 0.05), 0.03, 0.045, "Festa_Peso_Prata", segments=10, radius_top=0.012)
    for i, (c, mat) in enumerate(zip(centers, colors)):
        r = radius * rng.uniform(0.92, 1.05)
        sphere(p, c, r, mat, u=14, v=10, scale=(1.0, 1.0, stretch), taper=0.22)
        # Nó do balão: um biquinho embaixo.
        bottom = c[2] - r * stretch
        p.cylinder((c[0], c[1], bottom - 0.022), 0.026, 0.008, mat, segments=6, radius_top=0.02)
        # Fita: do nó até o peso, com uma leve curva.
        a = (c[0], c[1], bottom - 0.024)
        b = weight_top
        pts = []
        side = rng.uniform(-0.05, 0.05)
        for k in range(5):
            t = k / 4
            x = a[0] + (b[0] - a[0]) * t + side * math.sin(math.pi * t)
            y = a[1] + (b[1] - a[1]) * t + side * 0.6 * math.sin(math.pi * t)
            z = a[2] + (b[2] - a[2]) * t
            pts.append((x, y, z))
        p.tube(pts, 0.0025, "Festa_Fita", segments=4)
    return p.to_object()


def bandeirinhas(name="Bandeirinhas"):
    """Cordão de 3,6 m em X com 16 bandeirinhas de papel, caindo 0,25 m no meio. Origem no meio
    do cordão, na altura das pontas: tudo fica abaixo de z = 0 (o Unity pendura no alto do vão)."""
    p = Part(name)
    rng = random.Random(7303)
    half, sag = 1.8, 0.25

    def cord_z(x):
        return -sag * (1.0 - (x / half) ** 2)

    n_pts = 17
    pts = [(-half + 2 * half * i / (n_pts - 1), 0.0, cord_z(-half + 2 * half * i / (n_pts - 1)))
           for i in range(n_pts)]
    p.tube(pts, 0.003, "Festa_Barbante", segments=5)

    colors = ["Papel_Vermelho", "Festa_Papel_Amarelo", "Festa_Papel_Azul", "Festa_Papel_Verde",
              "Festa_Papel_Rosa", "Festa_Papel_Laranja"]
    n = 16
    step = 2 * half / n
    w, h = 0.17, 0.22
    for k in range(n):
        xc = -half + step * (k + 0.5)
        xa, xb = xc - w / 2, xc + w / 2
        za, zb = cord_z(xa) - 0.002, cord_z(xb) - 0.002
        tip = (xc + rng.uniform(-0.012, 0.012), (za + zb) / 2 - h * rng.uniform(0.95, 1.03))
        # Triângulo no plano XZ (o contorno 2D vira X e Z com rot de 90 em X), 1,6 mm de papel.
        # Cada bandeirinha balança um pouco em volta do cordão.
        tilt = rng.uniform(-8, 8)
        outline = [(xa, za), (tip[0], tip[1]), (xb, zb)]
        if (outline[1][0] - outline[0][0]) * (outline[2][1] - outline[0][1]) - \
                (outline[1][1] - outline[0][1]) * (outline[2][0] - outline[0][0]) < 0:
            outline.reverse()
        # Gira em volta do cordão (não da origem): o deslocamento devolve o alto ao lugar.
        zc = (za + zb) / 2
        t = math.radians(tilt)
        p.prism(outline, -0.0008, 0.0008, colors[(k * 5) % len(colors)], rot=(90 + tilt, 0, 0),
                offset=(0, zc * math.sin(t), zc * (1 - math.cos(t))))
    return p.to_object()


# --------------------------------------------------------------------------- Verão

def espreguicadeira(name="Espreguicadeira"):
    """Espreguiçadeira de alumínio com lona listrada azul e branca: 0,60 x 1,90 m, assento a
    0,35 m e encosto levantado até 0,80 m (em +Y)."""
    p = Part(name)
    alu = "Verao_Aluminio"
    r = 0.012
    xr = 0.285                       # trilhos laterais
    zs = 0.34                        # altura do trilho do assento
    y0, yh, y1 = -0.95, 0.42, 0.94  # pé da frente, dobra do encosto, topo do encosto
    zt = 0.80 - r                    # topo do encosto
    for sx in (-1, 1):
        x = sx * xr
        # Trilho: desce na frente, corre o assento e sobe o encosto.
        p.tube([(x, y0 + 0.02, zs - 0.05), (x, y0 + 0.06, zs), (x, yh, zs), (x, y1, zt)], r, alu)
        # Pernas: um par na frente, um par atrás (sob a dobra), com sapatas de borracha.
        for (ya, yb) in ((-0.78, -0.86), (0.30, 0.38)):
            p.tube([(x, ya, zs), (x, yb, 0.025)], r, alu)
            p.cylinder((x, yb, 0.0), 0.028, 0.017, "Verao_Borracha", segments=8)
        # Braço do encosto: escora do meio do encosto até o trilho do assento.
        ym = yh + (y1 - yh) * 0.55
        zm = zs + (zt - zs) * 0.55
        p.tube([(x, ym, zm - 0.01), (x, yh - 0.12, zs - 0.012)], r * 0.8, alu)
    # Travessas.
    for (y, z) in ((y0 + 0.06, zs), (yh, zs), (y1, zt), (-0.86, 0.06), (0.38, 0.06)):
        p.tube([(-xr, y, z), (xr, y, z)], r, alu)
    # Lona: listras ao comprido, alternando azul e branco, no assento e no encosto.
    n = 7
    inner = 2 * (xr - r)
    sw = inner / n
    ang = math.degrees(math.atan2(zt - zs, y1 - yh))
    length = math.hypot(y1 - yh, zt - zs)
    for i in range(n):
        x = -inner / 2 + sw * (i + 0.5)
        mat = "Verao_Lona_Azul" if i % 2 == 0 else "Verao_Lona_Branca"
        ys0, ys1 = y0 + 0.08, yh
        p.box((x, (ys0 + ys1) / 2, zs + 0.004), (sw + 0.0004, ys1 - ys0, 0.008), mat, bevel=0.0)
        my, mz = (yh + y1) / 2, (zs + zt) / 2 + 0.004
        p.box((x, my, mz), (sw + 0.0004, length - 0.04, 0.008), mat, bevel=0.0, rot=(ang, 0, 0))
    # Almofadinha de cabeça no topo do encosto.
    ht = 0.82
    hy, hz = yh + (y1 - yh) * ht, zs + (zt - zs) * ht + 0.03
    p.box((0, hy, hz), (0.34, 0.16, 0.05), "Verao_Lona_Branca", bevel=0.02, segments=2, rot=(ang, 0, 0))
    return p.to_object()


def guarda_sol(name="GuardaSol"):
    """Guarda-sol de praia: base redonda de concreto, mastro branco, cobertura de 8 gomos
    vermelhos e brancos, levemente cônica: Ø 2,0 m x 2,2 m."""
    p = Part(name)
    white = "Verao_Mastro_Branco"
    # Base de concreto e luva do mastro.
    p.cylinder((0, 0, 0), 0.06, 0.25, "Verao_Concreto", segments=20, radius_top=0.24)
    p.cylinder((0, 0, 0.06), 0.05, 0.24, "Verao_Concreto", segments=20, radius_top=0.13)
    p.cylinder((0, 0, 0.11), 0.10, 0.032, "Verao_Concreto", segments=10, radius_top=0.026)
    # Mastro em dois tubos com uma emenda.
    p.cylinder((0, 0, 0.10), 1.10, 0.019, white, segments=10)
    p.cylinder((0, 0, 1.18), 0.06, 0.024, white, segments=10)
    p.cylinder((0, 0, 1.20), 0.98, 0.016, white, segments=10)

    # Cobertura: casca fechada (por cima e por baixo), 32 segmentos = 8 gomos de 4. Entre as
    # varetas o tecido cede um pouco (raio menor e mais alto).
    seg = 32
    outer = [(1.00, 1.84), (0.68, 1.97), (0.34, 2.08)]
    inner = [(1.00, 1.84), (0.68, 1.945), (0.34, 2.05)]
    apex_o, apex_i = 2.15, 2.115
    sag_r = [0.0, 0.016, 0.024, 0.016]
    sag_z = [0.0, 0.010, 0.015, 0.010]
    bm = p.bm
    verts = []

    def ring(profile, k, shared=None):
        r0, z0 = profile[k]
        out = []
        for i in range(seg):
            if shared is not None:
                out.append(shared[i])
                continue
            a = 2 * math.pi * i / seg
            f = r0 / profile[0][0]
            rr = r0 * (1.0 - sag_r[i % 4] * f)
            zz = z0 + sag_z[i % 4] * f
            v = bm.verts.new((math.cos(a) * rr, math.sin(a) * rr, zz))
            verts.append(v)
            out.append(v)
        return out

    rim = ring(outer, 0)
    o_rings = [rim] + [ring(outer, k) for k in (1, 2)]
    i_rings = [rim] + [ring(inner, k) for k in (1, 2)]
    ao = bm.verts.new((0, 0, apex_o))
    ai = bm.verts.new((0, 0, apex_i))
    verts += [ao, ai]
    for rings in (o_rings, i_rings):
        for k in range(len(rings) - 1):
            a_, b_ = rings[k], rings[k + 1]
            for i in range(seg):
                j = (i + 1) % seg
                bm.faces.new((a_[i], a_[j], b_[j], b_[i]))
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new((o_rings[-1][i], o_rings[-1][j], ao))
        bm.faces.new((i_rings[-1][i], i_rings[-1][j], ai))
    p._finish_piece(verts, "Verao_Tecido_Branco", 0.0, smooth=False)

    def gore(c):
        a = math.atan2(c.y, c.x) % (2 * math.pi)
        return "Verao_Tecido_Vermelho" if int(a / (2 * math.pi / 8)) % 2 == 0 else "Verao_Tecido_Branco"
    paint(p, verts, gore)

    # Varetas por baixo do tecido e escoras até o corredor do mastro.
    p.cylinder((0, 0, 1.58), 0.07, 0.03, white, segments=10)
    for k in range(8):
        a = 2 * math.pi * k / 8
        ca, sa = math.cos(a), math.sin(a)
        rib = []
        for (rr, zz) in ((0.06, 2.10), (0.34, 2.035), (0.68, 1.93), (0.97, 1.83)):
            rib.append((ca * rr, sa * rr, zz - 0.006))
        p.tube(rib, 0.006, white, segments=5)
        stretch_to(p, (ca * 0.03, sa * 0.03, 1.62), (ca * 0.55, sa * 0.55, 1.965), 0.005, white, segments=5)
        # Ponteira da vareta.
        p.cylinder((ca * 0.975, sa * 0.975, 1.815), 0.025, 0.008, white, segments=6)
    # Ponteira de cima.
    p.cylinder((0, 0, 2.14), 0.04, 0.03, white, segments=10, radius_top=0.012)
    p.cylinder((0, 0, 2.18), 0.02, 0.012, white, segments=8, radius_top=0.004)
    return p.to_object()


def bola_de_praia(name="BolaDePraia"):
    """Bola de praia de Ø 0,40 m, seis gomos (vermelho, amarelo, azul, intercalados com branco)."""
    p = Part(name)
    r = 0.20
    r_ = bmesh.ops.create_uvsphere(p.bm, u_segments=24, v_segments=14, radius=r, matrix=Matrix.Identity(4))
    verts = r_["verts"]
    p._finish_piece(verts, "Verao_Bola_Branca", 0.0, smooth=True)
    gores = ["Verao_Bola_Vermelha", "Verao_Bola_Branca", "Verao_Bola_Amarela", "Verao_Bola_Branca",
             "Verao_Bola_Azul", "Verao_Bola_Branca"]

    def choose(c):
        if abs(c.z) > 0.96 * r:
            return "Verao_Bola_Branca"   # tampas dos polos
        a = math.atan2(c.y, c.x) % (2 * math.pi)
        return gores[int(a / (2 * math.pi / 6)) % 6]
    paint(p, verts, choose)
    # Gira a bola: o eixo dos polos tomba um pouco, como bola largada no chão.
    m = Matrix.Translation((0, 0, r)) @ Matrix.Rotation(math.radians(24), 4, "X") @ \
        Matrix.Rotation(math.radians(20), 4, "Z")
    bmesh.ops.transform(p.bm, matrix=m, verts=verts)
    # Bico de encher num gomo branco, perto do polo de cima.
    a = math.radians(90)
    d = Vector((math.cos(a) * 0.62, math.sin(a) * 0.62, 0.78)).normalized()
    base = m @ (d * (r - 0.002))
    n = m.to_3x3() @ d
    p.tube([tuple(base), tuple(base + n * 0.012)], 0.008, "Verao_Bola_Branca", segments=8)
    p.bm.normal_update()
    return p.to_object()


# --------------------------------------------------------------------------- Lote

BUILDERS = [
    mesa_de_bolos,
    cacho_de_baloes,
    bandeirinhas,
    espreguicadeira,
    guarda_sol,
    bola_de_praia,
]

HANGING = {"Bandeirinhas"}


def conferir_pendurados():
    """O run_lote saiu com 3: refaz a conferência aceitando as peças penduradas (pontas do
    cordão em z = 0, o resto abaixo). Qualquer outro problema continua sendo erro."""
    problems = []
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.parent is not None:
            continue
        tris = triangles(obj)
        size, lo = size_of(obj)
        hi_z = lo.z + size.z
        if tris > TRI_BUDGET:
            problems.append(f"{obj.name}: {tris} triângulos (máximo {TRI_BUDGET})")
        if obj.name in HANGING:
            if abs(hi_z) > 0.01:
                problems.append(f"{obj.name}: pendurado, o alto deveria estar em z = 0 (está em {hi_z:.3f})")
        elif abs(lo.z) > 0.01:
            problems.append(f"{obj.name}: a base está em z = {lo.z:.3f}, deveria estar no chão (0)")
    if problems:
        print("[BackRooms] PROBLEMAS (festa_verao):\n  " + "\n  ".join(problems))
        sys.exit(3)
    print("[BackRooms] festa_verao: o aviso de base fora do chão é das Bandeirinhas, penduradas "
          "pela origem (pontas do cordão em z = 0). Resto conferido: OK.")


def main():
    try:
        run_lote("festa_verao", BUILDERS, PALETA)
    except SystemExit as e:
        if e.code != 3:
            raise
        conferir_pendurados()


if __name__ == "__main__":
    main_guard(main)
