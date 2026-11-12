"""A entidade do Level 0, refeita no Blender (o desenho aprovado: Docs/historico.md, "Entidade
redesenhada: cipós e cabeça comprida").

Figura de 3,3 m, curvada, feita de cipós pretos torcidos que sobem em espiral em volta de cada
membro, fechados nas juntas e abertos no meio (dá para ver através dela), com nós de madeira,
espinhos, dedos de galho, raízes no lugar dos pés e nove cipós soltos nas costas. A cabeça é um
casco comprido, sem olhos, esticado mais de 1 m para trás e para cima, com crista, sulcos,
têmporas cavadas e placas sobrepostas; a boca fica só na frente, entreaberta, com dentes em
cone tortos.

Por enquanto só para ver (fotos em Logs/Blender/personagens/entidade_*.png).

    blender -b --factory-startup -P Tools/Blender/personagens/entidade.py [-- --exportar]

--exportar: esculpe sobre o esqueleto do jogo (esqueleto_entidade.json, gravado pelo Unity em
    BackRooms > Personagens > Exportar esqueletos), na pose parada do EntityBody, e grava o
    modelo do jogo em Assets/_Game/Art/Personagens/Entidade (ver personagem_export.py).
"""

import math
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import personagem_base as pb  # noqa: E402
from personagem_base import (v3, rot, Frame, sphere, ellipsoid, capsule, smin, smax,  # noqa: E402
                             polyline, catmull, fbm, vnoise, length, norm)

RNG = np.random.default_rng(977)
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
EXPORT = "--exportar" in ARGS
SK = None
TENDRILS = []          # (junta, pontos) dos cipós soltos que o jogo balança
VINE_POINTS = 14 if EXPORT else 22

# ------------------------------------------------------------------ esqueleto (curvada, à espreita)

J = dict(
    pelvis=v3(0.0, 0.02, 1.74),
    hipR=v3(-0.13, 0.02, 1.72), hipL=v3(0.13, 0.02, 1.72),
    kneeR=v3(-0.19, -0.30, 0.90), ankleR=v3(-0.22, 0.04, 0.06),
    kneeL=v3(0.17, -0.18, 0.88), ankleL=v3(0.23, 0.16, 0.05),
    chest=v3(0.0, -0.26, 2.52),
    shoulderR=v3(-0.30, -0.34, 2.70), shoulderL=v3(0.29, -0.33, 2.64),
    neck=v3(0.0, -0.42, 2.76), head=v3(0.0, -0.78, 2.73),
    elbowR=v3(-0.45, -0.46, 1.78), wristR=v3(-0.50, -0.76, 0.78),
    elbowL=v3(0.48, -0.40, 1.74), wristL=v3(0.56, -0.62, 0.70),
)

HEAD = Frame(J["head"], rot(x=6.0, y=-4.0, z=0.0), 1.0)
TAIL_TARGET = None    # ponto das costas onde a cauda do crânio afunda (no jogo)

# ------------------------------------------------------------------ cabeça: casco comprido

T_KNOTS = np.array((-0.47, -0.38, -0.25, -0.12, 0.0, 0.12, 0.25, 0.38))
RX = np.array((0.035, 0.07, 0.105, 0.14, 0.165, 0.17, 0.16, 0.135))
RZ = np.array((0.042, 0.075, 0.11, 0.145, 0.175, 0.185, 0.175, 0.15))
ZC = np.array((-0.06, -0.045, -0.025, 0.0, 0.02, 0.05, 0.085, 0.11))
CREST_RZ = np.array((0.0, 0.0, 0.004, 0.018, 0.03, 0.04, 0.04, 0.03))

# A cauda do crânio: sobe um pouco atrás da cabeça e se curva para baixo, em gomos, até
# afundar nas costas, onde os cipós a agarram (o fim fica dentro do feixe do tronco).
TAIL_PATH = catmull([(0.0, 0.3, 0.1), (0.0, 0.48, 0.135), (0.0, 0.63, 0.11), (0.0, 0.74, 0.03),
                     (0.0, 0.79, -0.08), (0.0, 0.77, -0.19), (0.0, 0.71, -0.29)], 3)
TAIL_R = np.linspace(0.135, 0.032, len(TAIL_PATH)) ** 1.0


def load_skeleton():
    """Troca a pose das fotos pela pose parada do jogo (o esqueleto gravado pelo Unity). No Unity
    o lado "_E" fica em +X, que no Blender é -X: o lado "R" destes scripts."""
    global J, HEAD, SK, TAIL_PATH, TAIL_R, TAIL_TARGET
    import personagem_export as pe
    SK = pe.Skeleton("entidade")
    B = SK.pos
    J = dict(
        pelvis=B("Bacia"), hipR=B("Coxa_E"), hipL=B("Coxa_D"),
        kneeR=B("Canela_E"), ankleR=B("Pe_E"), kneeL=B("Canela_D"), ankleL=B("Pe_D"),
        chest=B("Coluna_Alta"), shoulderR=B("Braco_E"), shoulderL=B("Braco_D"),
        neck=B("Ombros"), head=B("Cabeca"),
        elbowR=B("Antebraco_E"), wristR=B("Mao_E"), elbowL=B("Antebraco_D"), wristL=B("Mao_D"),
    )
    Rh = SK.frame("Cabeca")
    HEAD = Frame(B("Mandibula") - Rh @ JAW_HINGE, Rh, 1.0)
    # A cauda: sai de trás do crânio, faz a curva para trás e para baixo e afunda nas costas,
    # entre o peito e os ombros.
    back = SK.frame("Coluna_Alta")[:, 1]
    TAIL_TARGET = (B("Coluna_Alta") + B("Ombros")) * 0.5 + back * 0.1
    t = HEAD.local(TAIL_TARGET[None])[0]
    p0, p1 = v3(0.0, 0.3, 0.1), v3(0.0, 0.48, 0.14)
    d = t - p1
    perp = norm(v3(0.0, d[2], -d[1]))
    if perp[1] < 0:
        perp = -perp
    mid = (p1 + t) * 0.5 + perp * 0.16
    TAIL_PATH = catmull([p0, p1, mid, t], 3)
    TAIL_R = np.linspace(0.135, 0.035, len(TAIL_PATH))
    for i in range(9):
        pts = [B("Cipo_Solto_%d" % i), B("Cipo_Solto_Meio_%d" % i), B("Cipo_Solto_Ponta_%d" % i)]
        TENDRILS.append(("Cipo_Solto_%d" % i, pts))


def section(q, rx_tab, rz_tab, zc_tab, x0=0.0):
    t = q[:, 1]
    tc = np.clip(t, T_KNOTS[0], T_KNOTS[-1])
    rx = np.maximum(np.interp(tc, T_KNOTS, rx_tab), 1e-3)
    rz = np.maximum(np.interp(tc, T_KNOTS, rz_tab), 1e-3)
    zc = np.interp(tc, T_KNOTS, zc_tab)
    ex = (q[:, 0] - x0) / rx
    ez = (q[:, 2] - zc) / rz
    k = np.sqrt(ex * ex + ez * ez)
    dsec = (k - 1.0) * np.minimum(rx, rz) * 0.85
    dax = np.maximum(np.maximum(t - T_KNOTS[-1], T_KNOTS[0] - t), 0.0)
    d = np.where(dax > 0, np.sqrt(np.maximum(dsec, 0) ** 2 + dax ** 2), dsec)
    return d, t, ex, ez, zc, rz


def skull(q):
    d, t, ex, ez, zc, rz = section(q, RX, RZ, ZC)
    # irregular, torto: caroços e um lado mais cheio que o outro
    d -= 0.009 * fbm(q, 5.0, 3, 2) + 0.004 * fbm(q, 22.0, 2, 4)
    d -= 0.012 * np.exp(-((q[:, 0] - 0.12) ** 2 / 0.004 + (t - 0.25) ** 2 / 0.03 + (q[:, 2] - zc) ** 2 / 0.01))
    # placas sobrepostas do alto do crânio para trás (borda virada para trás)
    s = (t * 7.0 + 0.25 * vnoise(q, 6.0, 3)) % 1.0
    top = np.clip(ez + 0.2, 0.0, 1.0)
    d -= 0.011 * s ** 1.6 * smooth(-0.05, 0.08, t) * top
    # sulcos ao longo dos lados
    th = np.arctan2(ez, np.abs(ex))
    for tg, depth in ((-0.55, 0.006), (-0.1, 0.007), (0.35, 0.006), (0.75, 0.005)):
        d += depth * np.exp(-((th - tg) / 0.07) ** 2) * smooth(-0.32, -0.15, t) * smooth(1.0, 0.8, t)
    # crista
    crest_z = ZC + RZ * 0.8
    cd, *_ = section(q, np.full_like(RX, 0.016), CREST_RZ + 1e-3, crest_z)
    cd -= 0.008 * np.abs(np.sin(t * 34.0)) * smooth(-0.1, 0.05, t)   # nós de osso
    d = smin(d, cd, 0.025)
    d = smin(d, tail(q), 0.05)
    # têmporas cavadas
    for s_ in (-1, 1):
        d = smax(d, -ellipsoid(q, (s_ * 0.165, -0.12, 0.0), (0.045, 0.1, 0.07)), 0.04)
    # boca: corta a parte de baixo da frente (o céu da boca), onde a mandíbula encaixa
    mouth = np.maximum(q[:, 2] - (zc - 0.45 * rz), t + 0.06)
    d = smax(d, -mouth, 0.012)
    return d


def tail(q):
    """Gomos (elipsoides achatados ao longo do caminho), cada um encaixado no anterior, com um
    nó de osso em cima de cada gomo."""
    d = None
    pts = TAIL_PATH
    for i in range(len(pts) - 1):
        a, b = np.asarray(pts[i]), np.asarray(pts[i + 1])
        u = norm(b - a)
        side = v3(1.0, 0.0, 0.0)
        up = np.cross(u, side)
        Rm = np.stack((side, u, up), axis=1)
        L = float(np.linalg.norm(b - a))
        r_ = float(TAIL_R[i])
        di = ellipsoid(q, (a + b) * 0.5, (r_ * 1.05, L * 0.95 + 0.012, r_ * 0.95), Rm)
        di = smin(di, sphere(q, (a + b) * 0.5 - up * (r_ * 0.95), r_ * 0.22), 0.015)
        d = di if d is None else smin(d, di, 0.008)
    return d - 0.005 * fbm(q, 18.0, 2, 19)


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


# mandíbula: a dobradiça fica embaixo, atrás da boca; entreaberta
JAW_HINGE = v3(0.0, -0.06, -0.1)
JAW = Frame(JAW_HINGE, rot(x=-13.0), 1.0)   # dentro do sistema da cabeça


def jaw_profile(t):
    """Medidas da mandíbula ao longo do comprimento: fina e pontuda no queixo, larga na dobradiça."""
    tt = np.clip(t, -0.46, 0.02)
    u = (tt + 0.46) / 0.48                        # 0 no queixo, 1 na dobradiça
    rx = 0.012 + 0.13 * u ** 0.8
    rz = 0.016 + 0.05 * u ** 1.2
    zc = -0.12 - 0.035 * np.sin(math.pi * np.clip(u * 1.3, 0, 1)) + 0.05 * u ** 3
    return u, rx, rz, zc


def jaw_local(q):
    """Mandíbula comprida, em coordenadas da cabeça com a boca fechada: aberta em cima (calha)."""
    t = q[:, 1]
    u, rx, rz, zc = jaw_profile(t)
    ex = q[:, 0] / rx
    ez = (q[:, 2] - zc) / rz
    k = np.sqrt(ex * ex + ez * ez)
    d = (k - 1.0) * np.minimum(rx, rz) * 0.85
    dax = np.maximum(np.maximum(t - 0.02, 0), np.maximum(-0.46 - t, 0))
    d = np.where(dax > 0, np.sqrt(np.maximum(d, 0) ** 2 + dax ** 2), d)
    d -= 0.004 * fbm(q, 14.0, 3, 21)
    # calha por dentro: a língua e a garganta ficam ali
    inner_rx = np.maximum(rx - 0.02, 0.004)
    inner = (np.sqrt((q[:, 0] / inner_rx) ** 2 + ((q[:, 2] - zc - rz * 0.6) / rz) ** 2) - 1.0) * 0.02
    inner = np.maximum(inner, -0.4 - t)
    d = smax(d, -inner, 0.006)
    d = np.minimum(d, ellipsoid(q, (0.0, -0.2, -0.1), (0.045, 0.17, 0.014)))   # língua
    return d


def jaw(q):
    return jaw_local(JAW.local(q))


def _teeth_specs():
    specs = []
    # de cima: na borda do céu da boca, apontando para baixo e um pouco para trás
    for side in (-1, 1):
        for i, t in enumerate(np.linspace(-0.43, -0.1, 9)):
            rx = np.interp(t, T_KNOTS, RX) * 0.8
            zc = np.interp(t, T_KNOTS, ZC) - 0.45 * np.interp(t, T_KNOTS, RZ)
            base = v3(side * rx + RNG.uniform(-0.006, 0.006), t, zc + 0.01)
            L = RNG.uniform(0.035, 0.085) * (1.3 if i in (2, 5) else 1.0)
            tip_dir = norm(v3(side * RNG.uniform(-0.25, 0.1), RNG.uniform(0.1, 0.45), -1.0))
            specs.append(("cima", base, tip_dir, L, RNG.uniform(0.008, 0.013)))
    # de baixo: na borda da mandíbula, apontando para cima
    for side in (-1, 1):
        for i, t in enumerate(np.linspace(-0.41, -0.12, 7)):
            u, rx, rz, zc = jaw_profile(np.array([t]))
            base = v3(side * float(rx[0]) * 0.8, t, float(zc[0] + rz[0] * 0.7))
            L = RNG.uniform(0.03, 0.07)
            tip_dir = norm(v3(side * RNG.uniform(-0.3, 0.1), RNG.uniform(-0.1, 0.3), 1.0))
            specs.append(("baixo", base, tip_dir, L, RNG.uniform(0.007, 0.011)))
    return specs


TEETH = _teeth_specs()


def teeth_in(q, which):
    d = None
    for kind, base, tip_dir, L, r in TEETH:
        if kind != which:
            continue
        bend = norm(tip_dir + v3(0, 0.5, 0) * (1 if which == "cima" else -0.3))
        p1 = base + tip_dir * L * 0.55
        p2 = p1 + bend * L * 0.5
        di = polyline(q, [base - tip_dir * 0.01, p1, p2], [r, r * 0.6, 0.0008], 0.002)
        d = di if d is None else np.minimum(d, di)
    return d


def head_color(V):
    q = HEAD.local(V)
    d, t, ex, ez, zc, rz = section(q, RX, RZ, ZC)
    c = np.tile(np.array((0.055, 0.048, 0.042)), (len(q), 1))
    c *= (1.0 + 0.25 * fbm(q, 25.0, 4, 5))[:, None]
    stria = np.abs(np.sin(t * 160.0 + 3 * fbm(q, 8.0, 2, 6)))
    c = pb_mix(c, np.array((0.13, 0.1, 0.075)), (stria > 0.93) * 0.6)
    vein = np.abs(fbm(q, 14.0, 4, 7)) < 0.035
    c = pb_mix(c, np.array((0.16, 0.06, 0.05)), vein * 0.7)
    s = (t * 7.0 + 0.25 * vnoise(q, 6.0, 3)) % 1.0
    c = pb_mix(c, np.array((0.15, 0.13, 0.11)), (s > 0.9) * smooth(-0.05, 0.08, t) * 0.6)   # beira das placas
    mouth = (q[:, 2] < zc - 0.38 * rz) & (t < -0.03)
    c[mouth] = np.array((0.12, 0.025, 0.025))
    return np.clip(c, 0, 1)


def jaw_color(V):
    q = JAW.local(HEAD.local(V))
    c = np.tile(np.array((0.055, 0.048, 0.042)), (len(q), 1)) * (1.0 + 0.25 * fbm(q, 25.0, 4, 9))[:, None]
    inside = ellipsoid(q, (0.0, -0.2, -0.1), (0.05, 0.18, 0.02)) < 0.004
    u, rx, rz, zc = jaw_profile(q[:, 1])
    trough = (q[:, 2] > zc + rz * 0.1) & (np.abs(q[:, 0]) < rx - 0.012)
    c[inside | trough] = np.array((0.16, 0.035, 0.035))
    return np.clip(c, 0, 1)


def teeth_color(V):
    q = HEAD.local(V)
    c = np.tile(np.array((0.62, 0.56, 0.42)), (len(q), 1)) * (1.0 + 0.12 * fbm(q, 90.0, 3, 11))[:, None]
    c = pb_mix(c, np.array((0.3, 0.22, 0.12)), (fbm(q, 40.0, 2, 12) > 0.25) * 0.6)
    return np.clip(c, 0, 1)


def pb_mix(a, b, w):
    w = np.clip(np.asarray(w, dtype=np.float64), 0, 1)[:, None]
    return a * (1 - w) + b * w


# ------------------------------------------------------------------ corpo: cipós

class Vines:
    """Uma curva só com muitos fios; cada ponto leva o próprio raio (afina, engrossa)."""

    def __init__(self, name, material, resolution=2):
        self.cu = bpy.data.curves.new(name, "CURVE")
        self.cu.dimensions = "3D"
        self.cu.bevel_depth = 1.0
        self.cu.bevel_resolution = resolution
        self.cu.use_fill_caps = True
        self.cu.materials.append(material)
        self.name = name
        self.count = 0

    def add(self, pts, radii):
        sp = self.cu.splines.new("POLY")
        sp.points.add(len(pts) - 1)
        for i, (p, r) in enumerate(zip(pts, radii)):
            sp.points[i].co = (float(p[0]), float(p[1]), float(p[2]), 1.0)
            sp.points[i].radius = float(max(r, 0.0005))
        self.count += 1

    def object(self):
        o = bpy.data.objects.new(self.name, self.cu)
        bpy.context.scene.collection.objects.link(o)
        return o


def basis(a, b):
    u = norm(b - a)
    e1 = norm(np.cross(u, v3(0.3, 0.2, 1.0) if abs(u[2]) < 0.9 else v3(1.0, 0.0, 0.0)))
    e2 = np.cross(u, e1)
    return u, e1, e2


def bundle(v, a, b, strands, r_end, r_mid, thick, twist, seed, extend=0.06, thorns=None):
    """Feixe de cipós de `a` a `b`: sobem em espiral, juntos nas pontas e abertos no meio."""
    u, e1, e2 = basis(a, b)
    L = float(np.linalg.norm(b - a))
    rng = np.random.default_rng(seed)
    for k in range(strands):
        phase = 2 * math.pi * k / strands + rng.uniform(-0.3, 0.3)
        tw = twist * rng.uniform(0.7, 1.3) * (1 if k % 3 else -0.6)
        open_ = r_mid * rng.uniform(0.75, 1.25)
        th = thick * rng.uniform(0.6, 1.4)
        pts, radii = [], []
        n = VINE_POINTS
        t0, t1 = -extend / L, 1 + extend / L
        for i in range(n):
            t = t0 + (t1 - t0) * i / (n - 1)
            bulge = math.sin(math.pi * min(max(t, 0), 1)) ** 0.8
            R = r_end + (open_ - r_end) * bulge
            ang = phase + tw * t * 2 * math.pi
            wob = rng.normal(0, 0.006, 3)
            p = a + u * (L * t) + (e1 * math.cos(ang) + e2 * math.sin(ang)) * R + wob
            pts.append(p)
            radii.append(th * (1.0 - 0.35 * bulge) * (1.0 + 0.15 * math.sin(i * 1.7 + k)))
        v.add(pts, radii)
        if thorns is not None:
            for _ in range(rng.integers(1, 4)):
                i = int(rng.integers(3, n - 3))
                p = pts[i]
                out = norm(p - (a + u * (np.dot(p - a, u))) + rng.normal(0, 0.3, 3) * 0.3)
                ln = rng.uniform(0.03, 0.08)
                tip = p + out * ln + u * rng.uniform(-0.02, 0.02)
                thorns.add([p - out * 0.004, p + out * ln * 0.5, tip], [radii[i] * 0.55, radii[i] * 0.25, 0.0006])


def twig(v, start, direction, length, radius, depth, rng, thorns=None, droop=0.0):
    """Galho que se divide: dedos, raízes, raminhos."""
    pts, radii = [start], [radius]
    p = start.copy()
    d = norm(direction)
    steps = 7
    for i in range(1, steps + 1):
        d = norm(d + rng.normal(0, 0.18, 3) + v3(0, 0, -droop))
        p = p + d * (length / steps)
        pts.append(p.copy())
        radii.append(radius * (1 - i / (steps + 0.6)))
    v.add(pts, radii)
    if depth > 0:
        for _ in range(int(rng.integers(1, 3))):
            i = int(rng.integers(2, steps - 1))
            nd = norm(d + rng.normal(0, 0.8, 3))
            twig(v, pts[i], nd, length * rng.uniform(0.3, 0.55), radii[i] * 0.7, depth - 1, rng, thorns, droop)
    if thorns is not None and rng.random() < 0.7:
        i = int(rng.integers(1, steps - 1))
        nd = norm(rng.normal(0, 1, 3))
        thorns.add([pts[i], pts[i] + nd * 0.03], [radii[i] * 0.5, 0.0006])


def build_vines(m_bark):
    """Os cipós, em grupos (um objeto por grupo: tronco, peito, quadril, pescoço, cauda, cada
    perna, cada braço, cada cipó solto), porque no jogo cada grupo é movido por juntas diferentes."""
    groups = {}

    def V(name):
        if name not in groups:
            groups[name] = Vines("Entidade_Cipos_" + name, m_bark, resolution=1 if EXPORT else 2)
        return groups[name]

    thorns = Vines("Entidade_Espinhos", m_bark, resolution=1)
    rng = np.random.default_rng(31)
    # tronco: da bacia ao peito e do peito aos ombros, curvado para a frente
    bundle(V("tronco"), J["pelvis"] + v3(0, 0, -0.08), J["chest"], 14, 0.09, 0.2, 0.024, 0.55, 1, 0.08, thorns)
    bundle(V("peito"), J["chest"] - v3(0, -0.04, 0.08), J["neck"], 10, 0.1, 0.17, 0.022, -0.4, 2, 0.06, thorns)
    bundle(V("peito"), J["shoulderR"], J["shoulderL"], 6, 0.05, 0.08, 0.02, 0.8, 3, 0.05, thorns)
    bundle(V("quadril"), J["hipR"], J["hipL"], 5, 0.05, 0.08, 0.022, 0.8, 4, 0.06, thorns)
    # espinha grossa, da bacia até a ponta da cauda do crânio
    tip = HEAD.world(np.asarray(TAIL_PATH[-1]))
    back = norm(np.cross(norm(J["chest"] - J["pelvis"]), v3(1, 0, 0)))
    if back[1] < 0:
        back = -back
    sp = catmull([J["pelvis"] + v3(0, 0, -0.1) + back * 0.05, (J["pelvis"] + J["chest"]) * 0.5 + back * 0.09,
                  J["chest"] + back * 0.12, tip], 5)
    V("tronco").add(sp, [0.045 - 0.01 * i / len(sp) for i in range(len(sp))])
    # raízes que agarram a ponta da cauda nas costas
    for k in range(10):
        ang = 2 * math.pi * k / 10 + rng.uniform(-0.2, 0.2)
        ring = norm(np.cross(back, v3(1, 0, 0))) * math.cos(ang) + v3(1, 0, 0) * math.sin(ang)
        mid_ = HEAD.world(np.asarray(TAIL_PATH[-3])) + ring * 0.06
        end = tip + ring * rng.uniform(0.18, 0.32) - back * rng.uniform(0.0, 0.08) + v3(0, 0, -rng.uniform(0.05, 0.3))
        pts = catmull([mid_, tip + (mid_ - tip) * 0.3 + rng.normal(0, 0.02, 3), (tip + end) * 0.5 + back * 0.03, end], 3)
        V("cauda").add(pts, list(np.linspace(0.02, 0.006, len(pts))))
    # pescoço até a base do crânio
    bundle(V("pescoco"), J["neck"], HEAD.world(v3(0.0, 0.1, -0.05)), 9, 0.07, 0.1, 0.02, 0.6, 5, 0.1, thorns)
    # pernas e braços
    seed = 10
    for tag, (a, b, c) in (("R", (J["hipR"], J["kneeR"], J["ankleR"])), ("L", (J["hipL"], J["kneeL"], J["ankleL"]))):
        bundle(V("perna" + tag), a, b, 8, 0.05, 0.1, 0.02, 0.5, seed, 0.06, thorns)
        bundle(V("perna" + tag), b, c, 7, 0.045, 0.085, 0.018, -0.45, seed + 1, 0.06, thorns)
        seed += 2
    for tag, (a, b, c) in (("R", (J["shoulderR"], J["elbowR"], J["wristR"])), ("L", (J["shoulderL"], J["elbowL"], J["wristL"]))):
        bundle(V("braco" + tag), a, b, 7, 0.045, 0.09, 0.018, 0.6, seed, 0.06, thorns)
        bundle(V("braco" + tag), b, c, 7, 0.04, 0.08, 0.016, -0.5, seed + 1, 0.06, thorns)
        seed += 2
    # dedos de galho
    for tag, side, el, wr in (("R", -1, J["elbowR"], J["wristR"]), ("L", 1, J["elbowL"], J["wristL"])):
        u = norm(wr - el)
        for f in range(5):
            spread = (f - 2) * 0.32
            d = norm(u + v3(side * 0.15, 0, 0) + np.cross(u, v3(0, 0, 1)) * spread * side + v3(0, -0.15, 0))
            twig(V("braco" + tag), wr + rng.normal(0, 0.01, 3), d, rng.uniform(0.38, 0.6) * (1.2 if f == 2 else 1.0),
                 0.016, 1, rng, thorns, droop=0.05)
    # raízes no lugar dos pés
    for tag, ank in (("R", J["ankleR"]), ("L", J["ankleL"])):
        for k in range(7):
            ang = 2 * math.pi * k / 7 + rng.uniform(-0.3, 0.3)
            d = norm(v3(math.cos(ang), math.sin(ang) - 0.4, -0.25))
            pts = [ank + v3(0, 0, 0.06)]
            p = ank + v3(0, 0, 0.06)
            dd = d
            for i in range(6):
                dd = norm(dd + rng.normal(0, 0.15, 3) + v3(0, 0, -0.3))
                p = p + dd * rng.uniform(0.05, 0.08)
                p[2] = max(p[2], 0.012)
                pts.append(p.copy())
            V("perna" + tag).add(pts, list(np.linspace(0.026, 0.004, len(pts))))
    # nove cipós soltos pendurados nas costas: no jogo, nas juntas que balançam
    if TENDRILS:
        for name, (a, b, c) in TENDRILS:
            pts = [a, a + (b - a) * 0.5 + rng.normal(0, 0.03, 3), b, b + (c - b) * 0.5 + rng.normal(0, 0.04, 3), c]
            pts = catmull(pts, 3)
            V(name).add(pts, list(np.linspace(0.016, 0.003, len(pts))))
    else:
        for k in range(9):
            start = J["chest"] + v3(rng.uniform(-0.22, 0.22), 0.12 + rng.uniform(0, 0.06), rng.uniform(-0.25, 0.15))
            pts = [start]
            p = start.copy()
            for i in range(10):
                p = p + v3(rng.normal(0, 0.03), 0.03 + rng.normal(0, 0.02), -rng.uniform(0.08, 0.14))
                pts.append(p.copy())
            V("Cipo_Solto_%d" % k).add(catmull(pts, 2), list(np.linspace(0.016, 0.003, len(catmull(pts, 2)))))
    return [g.object() for g in groups.values()] + [thorns.object()]


def knots(P):
    """Nós de madeira nas juntas: caroços irregulares."""
    d = None
    for name, size in (("shoulderR", 0.085), ("shoulderL", 0.075), ("elbowR", 0.065), ("elbowL", 0.06),
                       ("kneeR", 0.07), ("kneeL", 0.07), ("wristR", 0.05), ("wristL", 0.05),
                       ("hipR", 0.06), ("hipL", 0.06), ("neck", 0.07), ("ankleR", 0.055), ("ankleL", 0.055)):
        c = J[name]
        di = ellipsoid(P, c, (size, size * 0.85, size * 0.9), rot(z=RNG.uniform(0, 90)))
        di = smin(di, sphere(P, c + v3(size * 0.4, -size * 0.3, size * 0.3), size * 0.55), 0.02)
        d = di if d is None else np.minimum(d, di)
    return d - 0.012 * fbm(P, 18.0, 3, 41)


# ------------------------------------------------------------------ montagem e fotos

def bark_material():
    m = pb.material("Entidade_Casca", rough=0.85, spec=0.3, base=(0.05, 0.043, 0.036), bump=0.9, bump_scale=160.0)
    return m


def build():
    pb.clear_scene()
    if not EXPORT:
        pb.stage(ceiling=3.8)
    m_bark = bark_material()
    m_shell = pb.material("Entidade_Casco", rough=0.28, spec=0.6, coat=0.5, bump=0.3, bump_scale=300.0)
    m_bone = pb.material("Entidade_Dente", rough=0.4)
    m_knot = pb.material("Entidade_No", rough=0.9, spec=0.25, base=(0.065, 0.052, 0.04), bump=1.0, bump_scale=90.0)
    objs = []

    def head_bounds(lo, hi):
        corners = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
        W = HEAD.world(corners)
        return W.min(0), W.max(0)

    lo, hi = head_bounds((-0.24, -0.52, -0.42), (0.24, 0.98, 0.42))
    objs.append(pb.sdf_mesh("Entidade_Cranio", HEAD.wrap(skull), lo, hi, 0.004, head_color, m_shell))
    lo, hi = head_bounds((-0.18, -0.52, -0.32), (0.18, 0.07, 0.0))
    objs.append(pb.sdf_mesh("Entidade_Mandibula", HEAD.wrap(jaw), lo, hi, 0.003, jaw_color, m_shell))
    lo, hi = head_bounds((-0.2, -0.5, -0.3), (0.2, 0.0, 0.05))
    objs.append(pb.sdf_mesh("Entidade_DentesCima", HEAD.wrap(lambda q: teeth_in(q, "cima")), lo, hi, 0.0018, teeth_color, m_bone))
    objs.append(pb.sdf_mesh("Entidade_DentesBaixo", HEAD.wrap(lambda q: teeth_in(JAW.local(q), "baixo")), lo, hi, 0.0018, teeth_color, m_bone))
    allj = np.array(list(J.values()))
    objs.append(pb.sdf_mesh("Entidade_Nos", knots, allj.min(0) - 0.15, allj.max(0) + 0.15, 0.006, None, m_knot))
    objs.extend(build_vines(m_bark))
    objs = [o for o in objs if o is not None]
    dg = bpy.context.evaluated_depsgraph_get()
    tris = 0
    for o in objs:
        me = o.evaluated_get(dg).to_mesh()
        tris += sum(len(p.vertices) - 2 for p in me.polygons)
        o.evaluated_get(dg).to_mesh_clear()
    print(f"[personagem] Entidade: {tris} triângulos (antes de reduzir)")
    return objs


def photos():
    out = pb.PHOTO_DIR
    mid = v3(0.0, -0.3, 1.6)
    hc = HEAD.world(v3(0.0, 0.15, 0.05))
    pb.shoot(os.path.join(out, "entidade_corpo_34.png"), v3(2.6, -4.2, 1.8), mid, 35, (900, 1200))
    pb.shoot(os.path.join(out, "entidade_lado.png"), v3(-3.3, -0.5, 1.9), mid + v3(0, -0.2, 0.15), 24, (900, 1200))
    pb.shoot(os.path.join(out, "entidade_frente.png"), v3(0.1, -5.0, 1.65), mid, 35, (900, 1200))
    pb.shoot(os.path.join(out, "entidade_costas.png"), v3(-1.8, 3.0, 2.2), mid, 35, (900, 1200))
    pb.shoot(os.path.join(out, "entidade_cabeca.png"), hc + v3(1.7, -1.3, 0.3), hc + v3(0, -0.1, -0.05), 40, (1100, 900))
    pb.shoot(os.path.join(out, "entidade_cabeca_lado.png"), hc + v3(-2.6, 0.1, 0.1), hc + v3(0, 0.0, -0.05), 40, (1100, 900))
    pb.shoot(os.path.join(out, "entidade_boca.png"), HEAD.world(v3(0.0, -0.4, -0.1)) + v3(0.35, -0.95, -0.25),
             HEAD.world(v3(0.0, -0.35, -0.08)), 50, (1100, 900))
    pb.shoot(os.path.join(out, "entidade_jogador.png"), v3(0.7, -3.4, 1.65), v3(0.0, -0.5, 2.15), 20, (1280, 720))


# ------------------------------------------------------------------ exportação para o jogo

BONES = ["Corpo", "Bacia", "Coluna_Baixa", "Coluna_Alta", "Ombros", "Pescoco", "Cabeca", "Mandibula",
         "Coxa_E", "Coxa_D", "Canela_E", "Canela_D", "Pe_E", "Pe_D", "Braco_E", "Braco_D",
         "Antebraco_E", "Antebraco_D"] + ["Cipo_Solto_%d" % i for i in range(9)]


def export_game(objs):
    import personagem_export as pe
    by = {o.name: o for o in objs}
    B = SK.pos

    def seg(bone, a, b):
        return (bone, B(a) if isinstance(a, str) else a, B(b) if isinstance(b, str) else b)

    spine = [seg("Bacia", "Bacia", "Coluna_Baixa"), seg("Coluna_Baixa", "Coluna_Baixa", "Coluna_Alta"),
             seg("Coluna_Alta", "Coluna_Alta", "Ombros")]
    head_top = B("Cabeca") + SK.axis("Cabeca", "y") * 0.3
    neck = [spine[2], seg("Pescoco", "Pescoco", "Cabeca"), seg("Cabeca", "Cabeca", head_top)]
    legs, arms = {}, {}
    for t in ("E", "D"):
        foot_end = B("Pe_" + t) + SK.axis("Pe_" + t, "z") * 0.3
        legs[t] = [seg("Coxa_" + t, "Coxa_" + t, "Canela_" + t), seg("Canela_" + t, "Canela_" + t, "Pe_" + t),
                   seg("Pe_" + t, "Pe_" + t, foot_end)]
        hand_end = B("Mao_" + t) + norm(B("Mao_" + t) - B("Antebraco_" + t)) * 0.6
        arms[t] = [seg("Braco_" + t, "Braco_" + t, "Antebraco_" + t), seg("Antebraco_" + t, "Antebraco_" + t, hand_end)]
    every = spine + neck[1:2] + legs["E"] + legs["D"] + arms["E"] + arms["D"]

    tip = HEAD.world(np.asarray(TAIL_PATH[-1]))
    skull_w = pe.blend(pe.rigid("Cabeca"), pe.rigid("Coluna_Alta"),
                       lambda V: smooth(0.5, 0.1, pb.length(V - tip)))
    weights = {
        "Entidade_Cranio": skull_w,
        "Entidade_Mandibula": pe.rigid("Mandibula"),
        "Entidade_DentesCima": pe.rigid("Cabeca"),
        "Entidade_DentesBaixo": pe.rigid("Mandibula"),
        "Entidade_Nos": pe.chain(every),
        "Entidade_Espinhos": pe.chain(every),
        "Entidade_Cipos_tronco": pe.chain(spine),
        "Entidade_Cipos_peito": pe.chain(spine[1:] + [arms["E"][0], arms["D"][0]], caps={"Braco_E": 0.5, "Braco_D": 0.5}),
        "Entidade_Cipos_quadril": pe.chain(spine[:1] + [legs["E"][0], legs["D"][0]], caps={"Coxa_E": 0.5, "Coxa_D": 0.5}),
        "Entidade_Cipos_pescoco": pe.chain(neck),
        "Entidade_Cipos_cauda": pe.rigid("Coluna_Alta"),
        "Entidade_Cipos_pernaR": pe.chain(spine[:1] + legs["E"]),
        "Entidade_Cipos_pernaL": pe.chain(spine[:1] + legs["D"]),
        "Entidade_Cipos_bracoR": pe.chain(spine[2:] + arms["E"]),
        "Entidade_Cipos_bracoL": pe.chain(spine[2:] + arms["D"]),
    }
    for i in range(9):
        weights["Entidade_Cipos_Cipo_Solto_%d" % i] = pe.rigid("Cipo_Solto_%d" % i)

    plans = {"Entidade_Cranio": ("Casco", 7000, 1.8), "Entidade_Mandibula": ("Casco", 1800, 1.5),
             "Entidade_DentesCima": ("Dente", 1400, 1.4), "Entidade_DentesBaixo": ("Dente", 1000, 1.4),
             "Entidade_Nos": ("Casca", 2500, 1.0)}
    parts = []
    dg = bpy.context.evaluated_depsgraph_get()
    for name, fn in weights.items():
        obj = by[name]
        if obj.type == "CURVE":
            # O cipó cheio para o cozimento é a própria curva em malha, com a cor da casca.
            me = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
            hi = bpy.data.objects.new(name + "_Cheio", me)
            bpy.context.scene.collection.objects.link(hi)
            me.materials.append(obj.data.materials[0])
            pe.ensure_color(hi, (0.05, 0.043, 0.036))
            parts.append(pe.Part(hi, "Casca", 10 ** 7, fn, 0.8))
        else:
            if name == "Entidade_Nos":
                pe.ensure_color(obj, (0.065, 0.052, 0.04))
            slot, tris, texel = plans[name]
            parts.append(pe.Part(obj, slot, tris, fn, texel))
    pe.export(parts, BONES, "Entidade")


def main():
    if EXPORT:
        load_skeleton()
        export_game(build())
        return
    build()
    photos()


pb.run(main)
