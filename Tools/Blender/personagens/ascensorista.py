"""O Ascensorista ("The Attendant"), o inimigo cego que chega de elevador no Level 3 (Escritório).

Desenho original: alguém que "vem de outro andar". Um homem alto e magro demais (uns 2 m, curvado),
de uniforme antigo de ascensorista de prédio — paletó cor de vinho, comprido, de duas fileiras de
botões de latão e gola em pé, calça com friso escuro, quepe redondo sem aba com a divisa de latão,
luvas brancas encardidas de dedos compridos demais. A cabeça é estreita e comprida, a pele cinzenta
e chupada; a faixa de gaze suja enrolada várias vezes em volta dos olhos (é cego, e isso se lê de
longe), com escorridos escuros descendo dela pelas bochechas e a ponta solta pendurada atrás; as
orelhas grandes demais, viradas para a frente (é o ouvido que caça); a boca larga demais,
entreaberta. Na mão direita, a bengala branca de cego, comprida, de ponteira vermelha.

Esculpido sobre o esqueleto do jogo (esqueleto_ascensorista.json, gravado pelo Unity em
BackRooms > Personagens > Exportar esqueletos), na pose parada do AttendantBody: as fotos e o
modelo do jogo saem da mesma pose.

    blender -b --factory-startup -P Tools/Blender/personagens/ascensorista.py -- [--exportar] [--rosto]

Sem --exportar: fotos em Logs/Blender/personagens/ascensorista_*.png. --rosto: só a cabeça.
--exportar: grava o modelo do jogo em Assets/_Game/Art/Personagens/Ascensorista (ver personagem_export.py).
"""

import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import personagem_base as pb  # noqa: E402
import personagem_export as pe  # noqa: E402
from personagem_base import (v3, Frame, sphere, ellipsoid, capsule, box, smin, smax,  # noqa: E402
                             polyline, catmull, fbm, vnoise, length, norm, on_surface)

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
EXPORT = "--exportar" in ARGS
ONLY_FACE = "--rosto" in ARGS

H_FACE = 0.0022
H_BODY = 0.0042

SK = pe.Skeleton("ascensorista")
B = SK.pos


def F(name):
    """Sistema local de uma junta (X para a esquerda do personagem, Y para trás, Z para cima da junta)."""
    return Frame(B(name), SK.frame(name), 1.0)


HEAD = F("Cabeca")
SPINE = F("Coluna")
PELVIS = F("Bacia")

# ------------------------------------------------------------------ cores (sRGB)

SKIN = np.array((0.30, 0.31, 0.27))       # cinza-esverdeado, de quem não vê o sol
SKIN_DARK = np.array((0.16, 0.16, 0.14))
LIP = np.array((0.26, 0.15, 0.15))
MAW = np.array((0.05, 0.02, 0.02))
SEEP = np.array((0.20, 0.05, 0.03))
GAUZE = np.array((0.58, 0.54, 0.45))
GAUZE_DIRTY = np.array((0.38, 0.31, 0.21))
WINE = np.array((0.32, 0.07, 0.09))
WINE_DARK = np.array((0.16, 0.04, 0.05))
STRIPE = np.array((0.09, 0.05, 0.05))
BLACK = np.array((0.06, 0.055, 0.055))
BRASS = np.array((0.62, 0.48, 0.20))
WHITE = np.array((0.88, 0.87, 0.83))
RED = np.array((0.55, 0.06, 0.05))


def mix(a, b, w):
    w = np.clip(w, 0.0, 1.0)[:, None]
    return a * (1 - w) + b * w


def smooth01(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


# ------------------------------------------------------------------ cabeça (coordenadas da junta Cabeca)
# A junta fica no alto do pescoço; o crânio sobe dela. Frente = -Y.

def mouth_curve(q):
    """Altura do meio da boca em cada x: sobe nos cantos (um sorriso que não é sorriso)."""
    return 0.062 + 26.0 * (q[:, 0] ** 2)


def mouth_cut(q):
    zc = mouth_curve(q)
    half_h = 0.0065 * np.clip(1.0 - (q[:, 0] / 0.056) ** 2, 0.0, 1.0) + 0.0008
    d = np.maximum(np.abs(q[:, 2] - zc) - half_h, np.abs(q[:, 0]) - 0.056)
    return np.maximum(d, q[:, 1] + 0.058)     # só na frente: o fundo da boca fica a uns 4 cm da cara


def teeth(q):
    d = None
    for k in range(9):
        x = -0.032 + k * 0.008
        z = 0.062 + 26.0 * x * x
        for zz, h in ((z + 0.004, 0.0042), (z - 0.004, 0.0038)):
            t = ellipsoid(q, (x, -0.083 + abs(x) * 0.35, zz), (0.0034, 0.004, h))
            d = t if d is None else np.minimum(d, t)
    return d


def skull(q):
    d = ellipsoid(q, (0.0, 0.012, 0.15), (0.076, 0.102, 0.122))
    d = smin(d, ellipsoid(q, (0.0, -0.028, 0.078), (0.062, 0.072, 0.088)), 0.03)       # maxilar
    d = smin(d, capsule(q, (0.0, -0.055, 0.07), (0.0, -0.07, 0.0), 0.024), 0.035)      # queixo comprido
    for s in (-1, 1):
        d = smax(d, -sphere(q, (s * 0.074, -0.05, 0.08), 0.027), 0.018)                # bochecha chupada
        d = smax(d, -sphere(q, (s * 0.084, -0.025, 0.15), 0.018), 0.018)               # têmpora
    # nariz fino e comprido; a ponte some debaixo da gaze
    d = smin(d, capsule(q, (0.0, -0.09, 0.16), (0.0, -0.112, 0.118), 0.009, 0.011), 0.016)
    # boca: um rasgo largo demais, entreaberto, quase até as bochechas, subindo nos cantos
    d = smax(d, -mouth_cut(q), 0.004)
    d = smin(d, teeth(q), 0.0015)
    # orelhas grandes, de abano, viradas para a frente
    for s in (-1, 1):
        R = pb.rot(z=s * 38.0, y=s * -8.0)
        ear = ellipsoid(q, (s * 0.086, 0.018, 0.14), (0.012, 0.04, 0.064), R)
        ear = smax(ear, -ellipsoid(q, (s * 0.091, 0.012, 0.14), (0.006, 0.028, 0.048), R), 0.006)
        d = smin(d, ear, 0.012)
    # pele enrugada
    d = d - 0.0015 * fbm(q, 38.0, 3, 11) - 0.0007 * fbm(q * v3(1.0, 1.0, 4.0), 30.0, 2, 12)
    return d


def neck(P):
    a = B("Pescoco")
    b = HEAD.world(v3(0.0, 0.0, 0.03))
    d = capsule(P, a, b, 0.043, 0.038)
    for s in (-1, 1):   # tendões
        t0 = a + SPINE.r[:, 0] * s * 0.018 + SPINE.r[:, 1] * -0.025
        t1 = HEAD.world(v3(s * 0.035, -0.02, 0.06))
        d = smin(d, capsule(P, t0, t1, 0.012), 0.01)
    return d


def head_skin(P):
    return smin(HEAD.wrap(skull)(P), neck(P), 0.03)


def skin_color(V):
    q = HEAD.local(V)
    c = SKIN * (1.0 + 0.08 * fbm(q, 30.0, 4, 21))[:, None]
    c = mix(c, SKIN_DARK, smooth01(0.1, 0.6, fbm(q, 9.0, 3, 22)) * 0.5)
    c = mix(c, np.array((0.40, 0.42, 0.44)), smooth01(0.55, 0.75, np.abs(vnoise(q, 26.0, 23))) * 0.35)   # veias
    for s in (-1, 1):   # bochechas chupadas mais escuras
        c = mix(c, SKIN_DARK * 0.8, smooth01(0.045, 0.015, length(q - v3(s * 0.066, -0.055, 0.085))) * 0.6)
    # lábios, fundo da boca e dentes amarelos
    zc = mouth_curve(q)
    lip = smooth01(0.012, 0.004, np.abs(q[:, 2] - zc)) * smooth01(0.062, 0.05, np.abs(q[:, 0])) * (q[:, 1] < -0.05)
    c = mix(c, LIP, lip * 0.9)
    c = mix(c, MAW, smooth01(0.0, -0.004, mouth_cut(q)))
    c = mix(c, np.array((0.62, 0.56, 0.40)), smooth01(0.002, -0.001, teeth(q)))
    # sangue escuro que vaza da gaze e escorre pelas bochechas até a boca
    for s, sway in ((-1, 0.7), (1, 1.3)):
        x = q[:, 0] - s * (0.034 + 0.006 * np.sin(q[:, 2] * 60.0 * sway))
        w = smooth01(0.011, 0.002, np.abs(x)) * smooth01(0.13, 0.116, q[:, 2]) * smooth01(0.035, 0.07, q[:, 2])
        w *= (q[:, 1] < -0.04)
        c = mix(c, SEEP, w * (0.6 + 0.4 * fbm(q, 60.0, 2, 24)))
        c = mix(c, SEEP * 0.8, smooth01(0.03, 0.0, length(q - v3(s * 0.034, -0.085, 0.118))) * 0.7)
    return np.clip(c, 0, 1)


# A gaze: quatro voltas tortas e enrugadas por cima dos olhos (uma cruzando na diagonal) e as duas
# pontas soltas penduradas atrás. (altura, meia largura, inclinação para trás, inclinação de lado)
WRAPS = ((0.142, 0.03, 6.0, 0.0), (0.152, 0.022, -12.0, 0.0), (0.131, 0.02, 14.0, 0.0), (0.145, 0.016, 0.0, 26.0))


def gauze_local(q):
    shell = ellipsoid(q, (0.0, 0.012, 0.15), (0.082, 0.108, 0.127))
    d = None
    for z0, half, tilt, roll in WRAPS:
        zt = z0 + q[:, 1] * math.tan(math.radians(tilt)) + q[:, 0] * math.tan(math.radians(roll))
        wrinkle = 0.003 * fbm(q * v3(1.0, 1.0, 3.0), 45.0, 3, 31)
        edge = 0.004 * fbm(q, 90.0, 2, 32)
        band = smax(np.abs(shell - 0.002) - 0.0055 - wrinkle, np.abs(q[:, 2] - zt) - half - edge, 0.003)
        d = band if d is None else smin(d, band, 0.003)
    for k, pts in enumerate(([(0.012, 0.11, 0.15), (0.03, 0.13, 0.12), (0.022, 0.15, 0.07), (0.04, 0.14, 0.0)],
                             [(-0.01, 0.112, 0.14), (-0.02, 0.135, 0.11), (-0.012, 0.15, 0.05)])):
        d = np.minimum(d, pb.ribbon(q, catmull(pts, 4), v3(1.0, 0.0, 0.15), 0.016 - k * 0.004, 0.0028))
    return d


def gauze_color(V):
    q = HEAD.local(V)
    c = GAUZE * (1.0 + 0.06 * fbm(q, 90.0, 2, 33))[:, None]
    c = mix(c, GAUZE_DIRTY, smooth01(0.0, 0.5, fbm(q, 10.0, 3, 34)) * 0.6)
    c = mix(c, GAUZE_DIRTY * 0.8, smooth01(0.4, 0.8, fbm(q, 25.0, 2, 36)) * 0.4)
    for s in (-1, 1):   # o sangue que vaza onde ficam os olhos, irregular, mais escuro no meio
        e = length((q - v3(s * 0.034, -0.1, 0.14)) * v3(1.0, 1.0, 0.8)) + 0.012 * fbm(q, 35.0, 3, 35 + s)
        c = mix(c, SEEP * 1.4, smooth01(0.034, 0.012, e) * 0.75)
        c = mix(c, SEEP * 0.7, smooth01(0.016, 0.0, e))
    return np.clip(c, 0, 1)


# Quepe redondo sem aba, inclinado para trás, com a divisa de latão pequena na frente.
CAP = Frame(v3(0.0, 0.01, 0.262), pb.rot(x=-10.0), 1.0)


def cap_local(q):
    c = CAP.local(q)
    rad = length(c[:, :2])
    d = np.maximum(rad - 0.082, np.abs(c[:, 2]) - 0.036)
    d = smin(d, np.maximum(rad - 0.072, np.abs(c[:, 2] - 0.03) - 0.012), 0.01)        # tampo
    badge = box(c, (0.0, -0.083, 0.004), (0.012, 0.003, 0.008), 0.002)
    return np.minimum(d - 0.002 * fbm(c, 50.0, 2, 41), badge)


def cap_color(V):
    q = CAP.local(HEAD.local(V))
    c = BLACK * (1.0 + 0.1 * fbm(q, 40.0, 2, 42))[:, None]
    c = mix(c, WINE, smooth01(0.006, 0.0, np.abs(q[:, 2] + 0.02) - 0.006) * 0.9)     # faixa vinho
    badge = smooth01(-0.079, -0.083, q[:, 1]) * smooth01(0.016, 0.012, np.abs(q[:, 0])) * smooth01(0.016, 0.01, np.abs(q[:, 2] - 0.004))
    c = mix(c, BRASS, badge)
    return np.clip(c, 0, 1)


# ------------------------------------------------------------------ corpo (coordenadas do mundo)

def jacket_solid(P):
    q = SPINE.local(P)
    d = ellipsoid(q, (0.0, 0.0, 0.3), (0.15, 0.105, 0.34))
    d = smin(d, capsule(P, B("Braco_D"), B("Braco_E"), 0.05), 0.06)                 # ombreiras
    qp = PELVIS.local(P)
    skirt = capsule(qp * v3(1.0, 1.35, 1.0), (0.0, 0.0, 0.12), (0.0, 0.0, -0.36), 0.14, 0.18)
    return smin(d, skirt, 0.06)


def sleeves(P):
    d = None
    for t in ("D", "E"):
        s = capsule(P, B("Braco_" + t), B("Antebraco_" + t), 0.05, 0.044)
        s = smin(s, capsule(P, B("Antebraco_" + t), B("Mao_" + t), 0.044, 0.04), 0.02)
        u = norm(B("Mao_" + t) - B("Antebraco_" + t))
        s = smin(s, capsule(P, B("Mao_" + t) - u * 0.06, B("Mao_" + t) - u * 0.01, 0.046), 0.006)   # punho
        s = smax(s, (P - B("Mao_" + t)) @ u, 0.004)
        d = s if d is None else np.minimum(d, s)
    return d


def jacket(P):
    d = jacket_solid(P)
    d = smin(d, sleeves(P), 0.03)
    # gola em pé em volta do pescoço
    a = B("Pescoco") - SPINE.r[:, 2] * 0.03
    collar = np.maximum(np.abs(capsule(P, a, a + SPINE.r[:, 2] * 0.06, 0.0) - 0.055) - 0.006,
                        np.abs((P - a) @ SPINE.r[:, 2] - 0.025) - 0.03)
    d = np.minimum(d, collar)
    d = smax(d, -capsule(P, B("Pescoco") - SPINE.r[:, 2] * 0.1, HEAD.world(v3(0, 0, 0.05)), 0.047), 0.01)
    # rugas do pano
    d = d - 0.003 * fbm(P * v3(1.0, 1.0, 0.4), 18.0, 3, 51)
    # trespasse: um degrau vertical na frente, um pouco à direita
    q = SPINE.local(P)
    d = smin(d, box(q, (-0.035, -0.1, 0.25), (0.004, 0.01, 0.3), 0.003), 0.004)
    return d


def jacket_color(V):
    q = SPINE.local(V)
    c = WINE * (1.0 + 0.08 * fbm(V, 25.0, 3, 61))[:, None]
    c = mix(c, WINE_DARK, smooth01(0.1, 0.6, fbm(V, 6.0, 3, 62)) * 0.55)
    c = mix(c, np.array((0.18, 0.12, 0.10)), smooth01(0.3, 0.7, fbm(V, 3.5, 3, 63)) * 0.4)   # graxa e poeira
    c = mix(c, WINE_DARK * 0.7, smooth01(-0.05, -0.2, q[:, 2]) * 0.6)                      # barra suja
    return np.clip(c, 0, 1)


def trousers(P):
    d = None
    for t in ("D", "E"):
        leg = capsule(P, B("Coxa_" + t), B("Joelho_" + t), 0.07, 0.06)
        leg = smin(leg, capsule(P, B("Joelho_" + t), B("Pe_" + t) + SK.axis("Pe_" + t, "y") * 0.03, 0.058, 0.052), 0.02)
        d = leg if d is None else np.minimum(d, leg)
    d = smin(d, ellipsoid(PELVIS.local(P), (0.0, 0.0, -0.02), (0.12, 0.09, 0.1)), 0.04)
    return d - 0.0025 * fbm(P * v3(1, 1, 0.4), 16.0, 3, 71)


def trousers_color(V):
    c = WINE_DARK * (1.0 + 0.1 * fbm(V, 25.0, 3, 72))[:, None]
    for t in ("D", "E"):   # friso escuro do lado de fora da perna
        out = SK.axis("Coxa_" + t, "x") * (1.0 if t == "D" else -1.0) * -1.0
        side = (V - B("Coxa_" + t)) @ out
        w = smooth01(0.045, 0.055, side) * (np.abs(V[:, 0] - B("Coxa_" + t)[0]) < 0.12)
        c = mix(c, STRIPE, w)
    return np.clip(c, 0, 1)


def shoes(P):
    d = None
    for t in ("D", "E"):
        f = F("Pe_" + t)
        s = ellipsoid(f.local(P), (0.0, -0.075, -0.025), (0.05, 0.14, 0.042))
        s = smax(s, -f.local(P)[:, 2] - 0.065, 0.004)
        d = s if d is None else np.minimum(d, s)
    return d


def buttons(P):
    d = None
    surf = lambda Q: jacket_solid(Q) - 0.002   # noqa: E731
    for col in (-0.065, 0.045):
        for k in range(4):
            seed = SPINE.world(v3(col, -0.12, 0.14 + k * 0.1))
            p = on_surface(surf, [seed], 0.0)[0]
            n = pb.normal_at(surf, p)
            di = ellipsoid(P, p + n * 0.003, (0.011, 0.011, 0.011))
            d = di if d is None else np.minimum(d, di)
    return d


def brass_color(V):
    return np.clip(BRASS * (1 + 0.15 * fbm(V, 200.0, 2, 81))[:, None], 0, 1)


def hand_local(q, open_hand):
    """Luva: palma e dedos compridos demais. Fechada (a direita), segurando a bengala."""
    d = box(q, (0.0, 0.0, -0.06), (0.038, 0.014, 0.045), 0.012)
    d = smin(d, capsule(q, (0.0, 0.0, 0.0), (0.0, 0.0, -0.03), 0.03), 0.02)          # pulso
    if open_hand:
        for k, x in enumerate((-0.027, -0.009, 0.009, 0.027)):
            L = (0.085, 0.1, 0.095, 0.08)[k]
            p0 = v3(x, 0.0, -0.1)
            p1 = p0 + v3(x * 0.4, -0.012, -L)
            p2 = p1 + v3(x * 0.2, -0.03, -L * 0.7)
            p3 = p2 + v3(0.0, -0.03, -L * 0.5)
            d = smin(d, polyline(q, [p0, p1, p2, p3], [0.0095, 0.0085, 0.0075, 0.0065], 0.002), 0.008)
        d = smin(d, polyline(q, [v3(-0.035, -0.01, -0.04), v3(-0.06, -0.03, -0.08), v3(-0.07, -0.05, -0.12)], [0.011, 0.009, 0.008]), 0.01)
    else:
        d = smin(d, ellipsoid(q, (0.0, -0.025, -0.1), (0.042, 0.035, 0.04)), 0.02)   # punho fechado
        for k in range(4):
            d = smin(d, sphere(q, (-0.027 + k * 0.018, -0.05, -0.1 - k * 0.004), 0.012), 0.006)
    return d


def gloves(P):
    d = None
    for t, open_hand in (("D", False), ("E", True)):
        f = F("Mao_" + t)
        g = hand_local(f.local(P), open_hand)
        d = g if d is None else np.minimum(d, g)
    return d


def glove_color(V):
    c = WHITE * (1.0 + 0.05 * fbm(V, 80.0, 2, 91))[:, None]
    c = mix(c, GAUZE_DIRTY, smooth01(0.0, 0.5, fbm(V, 12.0, 3, 92)) * 0.7)
    return np.clip(c, 0, 1)


def cane(P):
    a, b = B("Bengala"), B("Bengala_Ponta")
    u = norm(b - a)
    d = capsule(P, a - u * 0.08, b - u * 0.11, 0.011)
    d = smin(d, capsule(P, b - u * 0.12, b + u * 0.02, 0.013, 0.012), 0.003)        # ponteira
    return d


def cane_color(V):
    a, b = B("Bengala"), B("Bengala_Ponta")
    u = norm(b - a)
    t = (V - b) @ u
    c = mix(np.tile(WHITE, (len(V), 1)), RED, smooth01(-0.13, -0.11, t))
    c = mix(c, GAUZE_DIRTY * 0.7, smooth01(-0.05, 0.0, t) * 0.8)                   # ponta gasta
    return np.clip(c * (1.0 + 0.05 * fbm(V, 60.0, 2, 95))[:, None], 0, 1)


# ------------------------------------------------------------------ montagem

def bounds(frame, lo, hi):
    corners = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
    W = frame.world(corners)
    return W.min(0), W.max(0)


def joints_box(names, pad):
    A = np.array([B(n) for n in names])
    return A.min(0) - pad, A.max(0) + pad


def build():
    pb.clear_scene()
    if not EXPORT:
        pb.stage(wall=(0.78, 0.79, 0.8), floor=(0.45, 0.46, 0.47), ceiling=2.9, warm=False, dim=0.7)
    m_skin = pb.material("Ascensorista_Pele", rough=0.55, spec=0.4, sss=0.06, bump=0.4, bump_scale=1100.0)
    m_gauze = pb.material("Ascensorista_Gaze", rough=0.95, sheen=0.2, bump=0.6, bump_scale=700.0)
    m_cap = pb.material("Ascensorista_Quepe", rough=0.5)
    objs = []

    lo, hi = bounds(HEAD, (-0.13, -0.15, -0.02), (0.13, 0.14, 0.29))
    lo = np.minimum(lo, B("Pescoco") - 0.07)
    hi = np.maximum(hi, B("Pescoco") + 0.07)
    objs.append(pb.sdf_mesh("Ascensorista_Pele", head_skin, lo, hi, H_FACE, skin_color, m_skin))
    lo, hi = bounds(HEAD, (-0.11, -0.14, -0.0), (0.11, 0.16, 0.2))
    objs.append(pb.sdf_mesh("Ascensorista_Gaze", HEAD.wrap(gauze_local), lo, hi, 0.0018, gauze_color, m_gauze))
    lo, hi = bounds(HEAD, (-0.1, -0.11, 0.2), (0.1, 0.1, 0.32))
    objs.append(pb.sdf_mesh("Ascensorista_Quepe", HEAD.wrap(cap_local), lo, hi, 0.0018, cap_color, m_cap))

    if not ONLY_FACE:
        m_wool = pb.material("Ascensorista_Uniforme", rough=0.92, sheen=0.15, bump=0.35, bump_scale=420.0)
        m_brass = pb.material("Ascensorista_Latao", rough=0.35, metal=1.0)
        m_glove = pb.material("Ascensorista_Luva", rough=0.85, sheen=0.1)
        m_cane = pb.material("Ascensorista_Bengala", rough=0.35)
        m_shoe = pb.material("Ascensorista_Sapato", rough=0.35)
        lo, hi = joints_box(["Bacia", "Coluna", "Ombros", "Pescoco", "Braco_D", "Braco_E", "Antebraco_D", "Antebraco_E", "Mao_D", "Mao_E"], 0.25)
        objs.append(pb.sdf_mesh("Ascensorista_Paleto", jacket, lo, hi, H_BODY, jacket_color, m_wool))
        lo, hi = joints_box(["Bacia", "Coxa_D", "Coxa_E", "Joelho_D", "Joelho_E", "Pe_D", "Pe_E"], 0.18)
        objs.append(pb.sdf_mesh("Ascensorista_Calca", trousers, lo, hi, H_BODY, trousers_color, m_wool))
        lo, hi = joints_box(["Pe_D", "Pe_E"], 0.22)
        objs.append(pb.sdf_mesh("Ascensorista_Sapatos", shoes, lo, hi, 0.003, None, m_shoe))
        lo, hi = joints_box(["Coluna", "Ombros"], 0.22)
        objs.append(pb.sdf_mesh("Ascensorista_Botoes", buttons, lo, hi, 0.0016, brass_color, m_brass))
        lo, hi = joints_box(["Mao_D", "Mao_E"], 0.28)
        objs.append(pb.sdf_mesh("Ascensorista_Luvas", gloves, lo, hi, 0.0018, glove_color, m_glove))
        lo, hi = joints_box(["Bengala", "Bengala_Ponta"], 0.15)
        objs.append(pb.sdf_mesh("Ascensorista_Bengala", cane, lo, hi, 0.002, cane_color, m_cane))

    objs = [o for o in objs if o is not None]
    print(f"[personagem] Ascensorista: {pb.triangles(objs)} triângulos (antes de reduzir)")
    return objs


def photos():
    out = pb.PHOTO_DIR
    face = HEAD.world(v3(0.0, -0.05, 0.13))
    fwd = -SK.axis("Ascensorista", "z") if SK.has("Ascensorista") else v3(0, -1, 0)
    pb.shoot(os.path.join(out, "ascensorista_rosto.png"), face + v3(0.05, -0.9, 0.0), face, 85, (900, 900))
    pb.shoot(os.path.join(out, "ascensorista_rosto_34.png"), face + v3(0.7, -0.75, 0.05), face, 85, (900, 900))
    pb.shoot(os.path.join(out, "ascensorista_rosto_lado.png"), face + v3(-0.9, 0.05, 0.0), face, 85, (900, 900))
    pb.shoot(os.path.join(out, "ascensorista_rosto_de_baixo.png"), v3(0.05, -0.9, 1.1), face, 55, (900, 900))
    if not ONLY_FACE:
        mid = v3(0.0, -0.1, 1.05)
        pb.shoot(os.path.join(out, "ascensorista_corpo.png"), v3(0.1, -4.2, 1.4), mid, 40, (900, 1200))
        pb.shoot(os.path.join(out, "ascensorista_corpo_34.png"), v3(2.4, -3.2, 1.5), mid, 40, (900, 1200))
        pb.shoot(os.path.join(out, "ascensorista_lado.png"), v3(3.6, -0.3, 1.3), mid, 40, (900, 1200))
        pb.shoot(os.path.join(out, "ascensorista_costas.png"), v3(-1.4, 2.9, 1.6), mid, 40, (900, 1200))
        pb.shoot(os.path.join(out, "ascensorista_jogador.png"), v3(-0.5, -2.6, 1.55), v3(0.0, -0.2, 1.4), 28, (1280, 720))


# ------------------------------------------------------------------ exportação para o jogo

BONES = ["Ascensorista", "Bacia", "Coluna", "Ombros", "Pescoco", "Cabeca", "Coxa_D", "Coxa_E", "Joelho_D", "Joelho_E",
         "Pe_D", "Pe_E", "Braco_D", "Braco_E", "Antebraco_D", "Antebraco_E", "Mao_D", "Mao_E", "Bengala"]


def export_game(objs):
    by = {o.name: o for o in objs}

    def seg(bone, a, b):
        return (bone, B(a) if isinstance(a, str) else a, B(b) if isinstance(b, str) else b)

    spine = [seg("Bacia", "Bacia", "Coluna"), seg("Coluna", "Coluna", "Ombros"), seg("Ombros", "Ombros", "Pescoco")]
    hand_end = {t: B("Mao_" + t) + norm(B("Mao_" + t) - B("Antebraco_" + t)) * 0.25 for t in ("D", "E")}
    arms = {t: [seg("Braco_" + t, "Braco_" + t, "Antebraco_" + t), seg("Antebraco_" + t, "Antebraco_" + t, "Mao_" + t),
                seg("Mao_" + t, "Mao_" + t, hand_end[t])] for t in ("D", "E")}
    toe = {t: B("Pe_" + t) - SK.axis("Pe_" + t, "z") * -0.2 for t in ("D", "E")}
    legs = {t: [seg("Coxa_" + t, "Coxa_" + t, "Joelho_" + t), seg("Joelho_" + t, "Joelho_" + t, "Pe_" + t),
                seg("Pe_" + t, "Pe_" + t, toe[t])] for t in ("D", "E")}
    head_top = HEAD.world(v3(0, 0, 0.25))
    neck_chain = pe.chain([spine[2], seg("Pescoco", "Pescoco", "Cabeca"), seg("Cabeca", "Cabeca", head_top)])
    skin_w = pe.blend(neck_chain, pe.rigid("Cabeca"), lambda V: smooth01(0.0, 0.05, HEAD.local(V)[:, 2]))
    jacket_w = pe.chain(spine + arms["D"][:2] + arms["E"][:2] + [legs["D"][0], legs["E"][0]],
                        caps={"Coxa_D": 0.35, "Coxa_E": 0.35})
    trousers_w = pe.chain([spine[0]] + legs["D"][:2] + legs["E"][:2])
    shoes_w = pe.chain(legs["D"][1:] + legs["E"][1:])
    gloves_w = pe.chain(arms["D"][1:] + arms["E"][1:])

    pe.ensure_color(by["Ascensorista_Sapatos"], tuple(BLACK))
    P = pe.Part
    parts = [
        P(by["Ascensorista_Pele"], "Pele", 7000, skin_w, 2.4),
        P(by["Ascensorista_Gaze"], "Gaze", 2600, pe.rigid("Cabeca"), 1.6),
        P(by["Ascensorista_Quepe"], "Couro", 1400, pe.rigid("Cabeca"), 1.2),
        P(by["Ascensorista_Paleto"], "Uniforme", 7000, jacket_w),
        P(by["Ascensorista_Calca"], "Uniforme", 3500, trousers_w),
        P(by["Ascensorista_Sapatos"], "Couro", 1200, shoes_w),
        P(by["Ascensorista_Botoes"], "Metal", 700, pe.chain(spine[:2])),
        P(by["Ascensorista_Luvas"], "Gaze", 3200, gloves_w, 1.4),
        P(by["Ascensorista_Bengala"], "Bengala", 500, pe.rigid("Bengala"), 0.8),
    ]
    pe.export(parts, BONES, "Ascensorista")


def main():
    objs = build()
    if EXPORT:
        export_game(objs)
        return
    photos()


pb.run(main)
