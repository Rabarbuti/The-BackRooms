"""O Zelador (The Janitor), o inimigo do escuro do Level 3, esculpido no Blender.

Desenho original: um homem magro e alto demais (2,25 m esticado), curvado, de macacão de serviço
cinza-esverdeado encharcado da canela para baixo, botas de borracha pretas e um gorro de lã puxado
até o nariz, que tapa os olhos. A cabeça pende para a frente, abaixo da linha dos ombros, torta para
um lado; o queixo é comprido e a boca, larga demais (passa das bochechas), fica entreaberta com uma
fileira de dentes finos. Braços que passam do joelho, mãos de dedos compridos. Na direita, o cabo do
esfregão arrastado, a cabeça de tiras encharcadas no chão; no cinto, à esquerda, o molho de chaves.
Pele cinza-pálida, molhada, com veias escuras.

Esculpe sempre sobre o esqueleto do jogo (esqueleto_zelador.json, gravado pelo Unity em BackRooms >
Personagens > Exportar esqueletos), na pose parada do JanitorBody: as fotos mostram a pose de encaixe.

    blender -b --factory-startup -P Tools/Blender/personagens/zelador.py [-- --exportar]

Sem --exportar: fotos em Logs/Blender/personagens/zelador_*.png.
--exportar: grava o modelo do jogo em Assets/_Game/Art/Personagens/Zelador (ver personagem_export.py).
"""

import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import personagem_base as pb  # noqa: E402
import personagem_export as pe  # noqa: E402
from personagem_base import (v3, Frame, sphere, ellipsoid, capsule, box, smin, smax,  # noqa: E402
                             polyline, catmull, fbm, vnoise, length, norm)

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
EXPORT = "--exportar" in ARGS
H_BODY = 0.0048
H_HEAD = 0.0022
RNG = np.random.default_rng(4471)

SK = pe.Skeleton("zelador")
B = SK.pos


def F(name):
    """Sistema local da junta: X para a esquerda do personagem, Y para trás, Z para cima."""
    return Frame(B(name), SK.frame(name), 1.0)


HEAD = F("Cabeca")
JAW = F("Mandibula")

# ------------------------------------------------------------------ cores (sRGB)

CLOTH = np.array((0.30, 0.34, 0.29))
CLOTH_WET = np.array((0.11, 0.13, 0.11))
PATCH = np.array((0.62, 0.62, 0.56))
SKIN = np.array((0.50, 0.50, 0.45))
SKIN_DARK = np.array((0.30, 0.31, 0.29))
VEIN = np.array((0.22, 0.24, 0.30))
LIP = np.array((0.32, 0.15, 0.15))
MAW = np.array((0.07, 0.02, 0.02))
KNIT = np.array((0.20, 0.17, 0.15))
RUBBER = np.array((0.06, 0.06, 0.055))
METAL = np.array((0.62, 0.58, 0.45))
HANDLE = np.array((0.55, 0.55, 0.52))
MOP = np.array((0.30, 0.29, 0.25))
MOP_WET = np.array((0.12, 0.115, 0.10))
TOOTH = np.array((0.70, 0.66, 0.52))


def mix(a, b, w):
    w = np.clip(w, 0.0, 1.0)[:, None]
    return a * (1 - w) + b * w


def smooth01(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def bounds(points, pad):
    P = np.array(points)
    return P.min(axis=0) - pad, P.max(axis=0) + pad


# ------------------------------------------------------------------ macacão (tronco e pernas)

LEGS = {t: (B("Coxa_" + t), B("Canela_" + t), B("Pe_" + t)) for t in ("D", "E")}
ARMS = {t: (B("Braco_" + t), B("Antebraco_" + t), B("Mao_" + t)) for t in ("D", "E")}
SPINE = F("Coluna")
PELVIS = F("Bacia")
SHOULDERS = F("Ombros")


def coverall(P):
    q = SPINE.local(P)
    chest = ellipsoid(q, v3(0.0, 0.01, 0.42), (0.185, 0.11, 0.2))
    belly = ellipsoid(q, v3(0.0, 0.005, 0.17), (0.13, 0.09, 0.2))
    # Bolsos do peito, estufados.
    for x in (-0.085, 0.085):
        chest = smin(chest, box(q, v3(x, -0.1, 0.4), (0.05, 0.015, 0.06), 0.01), 0.01)
    d = smin(chest, belly, 0.06)
    hips = ellipsoid(PELVIS.local(P), v3(0.0, 0.005, 0.0), (0.16, 0.11, 0.13))
    d = smin(d, hips, 0.06)
    s = SHOULDERS.local(P)
    yoke = capsule(s, v3(-0.17, 0.0, -0.04), v3(0.17, 0.0, -0.04), 0.065)
    d = smin(d, yoke, 0.05)
    # Gola dobrada em volta do pescoço.
    collar = capsule(s, v3(-0.07, 0.01, 0.03), v3(0.07, 0.01, 0.03), 0.04)
    d = smin(d, collar, 0.02)
    for t in ("D", "E"):
        hip, knee, ankle = LEGS[t]
        up = SK.axis("Pe_" + t, "y")
        cuff = ankle + up * 0.17
        leg = smin(capsule(P, hip, knee, 0.08, 0.066), capsule(P, knee, cuff, 0.066, 0.084), 0.03)
        # Joelho folgado: o pano embola.
        leg = smin(leg, sphere(P, knee + v3(0.0, -0.03, 0.0), 0.07), 0.04)
        d = smin(d, leg, 0.05)
    # Rugas do pano e a barra pesada de água.
    d = d + fbm(P, 5.0, 3, 11) * 0.012 + vnoise(P * v3(1.0, 1.0, 0.3), 18.0, 5) * 0.005
    return d


def sleeves(P):
    d = np.full(len(P), 1e3)
    for t in ("D", "E"):
        sh, el, wr = ARMS[t]
        end = el + norm(wr - el) * 0.09
        arm = smin(capsule(P, sh, el, 0.058, 0.052), capsule(P, el, end, 0.052, 0.05), 0.02)
        # A manga dobrada: um rolo grosso.
        arm = smin(arm, capsule(P, end - norm(wr - el) * 0.02, end + norm(wr - el) * 0.025, 0.058), 0.01)
        d = np.minimum(d, arm)
    return d + fbm(P, 9.0, 3, 23) * 0.007


def cloth_color(V):
    z = V[:, 2]
    wet = smooth01(0.62, 0.38, z + fbm(V, 4.0, 3, 7) * 0.08)
    c = mix(np.tile(CLOTH, (len(V), 1)), CLOTH_WET, wet)
    # Manchas de sujeira e de água escorrida.
    c = mix(c, CLOTH_WET * 1.2, smooth01(0.35, 0.75, fbm(V, 6.0, 4, 31)) * 0.6)
    # Etiqueta de nome em branco no peito, à esquerda (dele): +X no Blender.
    q = SPINE.local(V)
    tag = (np.abs(q[:, 0] - 0.09) < 0.045) & (np.abs(q[:, 2] - 0.46) < 0.022) & (q[:, 1] < 0)
    c[tag] = PATCH
    return c * (1.0 + fbm(V, 40.0, 2, 3)[:, None] * 0.06)


# ------------------------------------------------------------------ cabeça (pele), gorro e dentes

MOUTH_Z = 0.035


def head_local_sdf(q):
    skull = ellipsoid(q, v3(0.0, -0.015, 0.125), (0.092, 0.112, 0.13))
    jaw = ellipsoid(q, v3(0.0, -0.05, 0.01), (0.078, 0.075, 0.09))
    chin = ellipsoid(q, v3(0.0, -0.085, -0.06), (0.04, 0.036, 0.055))
    d = smin(smin(skull, jaw, 0.04), chin, 0.03)
    # Bochechas fundas.
    for x in (-0.072, 0.072):
        d = smax(d, -sphere(q, v3(x, -0.095, 0.07), 0.032), 0.02)
    # Nariz achatado saindo de baixo do gorro.
    d = smin(d, capsule(q, v3(0.0, -0.118, 0.11), v3(0.0, -0.128, 0.08), 0.012), 0.012)
    # A boca: rasgo largo demais, de bochecha a bochecha, entreaberto.
    slit = ellipsoid(q, v3(0.0, -0.115, MOUTH_Z), (0.064, 0.05, 0.024))
    d = smax(d, -slit, 0.006)
    # Orelhas pequenas e coladas.
    for x in (-0.09, 0.09):
        d = smin(d, ellipsoid(q, v3(x, 0.0, 0.1), (0.012, 0.025, 0.035)), 0.01)
    return d


def head_sdf(P):
    d = HEAD.wrap(head_local_sdf)(P)
    neck = capsule(P, B("Pescoco"), B("Cabeca") + SK.axis("Cabeca", "y") * 0.03, 0.05, 0.047)
    d = smin(d, neck, 0.03)
    return d + fbm(P, 30.0, 2, 41) * 0.0012


def skin_color(V):
    c = mix(np.tile(SKIN, (len(V), 1)), SKIN_DARK, smooth01(-0.2, 0.6, fbm(V, 14.0, 4, 13)))
    veins = np.abs(fbm(V, 24.0, 3, 17))
    c = mix(c, VEIN, smooth01(0.06, 0.0, veins) * 0.55)
    q = HEAD.local(V)
    near_mouth = smooth01(0.03, 0.0, np.abs(q[:, 2] - MOUTH_Z) - 0.01) * (q[:, 1] < -0.07) * smooth01(0.11, 0.07, np.abs(q[:, 0]))
    c = mix(c, LIP, near_mouth * 0.8)
    inside = (length(((q - v3(0.0, -0.11, MOUTH_Z)) / np.array((0.064, 0.05, 0.024)))) < 1.05)
    c[inside] = MAW
    return c


def cap_local_sdf(q):
    outer = ellipsoid(q, v3(0.0, -0.014, 0.13), (0.1, 0.12, 0.138))
    inner = ellipsoid(q, v3(0.0, -0.014, 0.13), (0.09, 0.11, 0.126))
    shell = smax(outer, -inner, 0.0)
    # Borda: mais baixa na frente (tapa os olhos, até o nariz), mais alta atrás.
    edge = 0.094 + np.clip(q[:, 1], -0.2, 0.2) * 0.15
    d = smax(shell, edge - q[:, 2], 0.004)
    # Barra dobrada, mais grossa.
    band = smax(smax(ellipsoid(q, v3(0.0, -0.014, 0.13), (0.106, 0.126, 0.144)), -inner, 0.0),
                np.abs(q[:, 2] - edge - 0.018) - 0.02, 0.004)
    d = smin(d, band, 0.004)
    # Tricô: nervuras verticais.
    ang = np.arctan2(q[:, 0], q[:, 1])
    return d - np.abs(np.sin(ang * 70.0)) * 0.001


def cap_color(V):
    q = HEAD.local(V)
    c = np.tile(KNIT, (len(V), 1)) * (0.85 + 0.3 * np.abs(np.sin(np.arctan2(q[:, 0], q[:, 1]) * 70.0)))[:, None]
    return mix(c, KNIT * 0.6, smooth01(0.2, 0.7, fbm(V, 10.0, 3, 9)))


def teeth_local(q, upper):
    d = np.full(len(q), 1e3)
    for i in range(13):
        x = -0.048 + i * 0.008
        bend = x * x * 2.5
        y = -0.118 + bend
        if upper:
            a, b = v3(x, y, MOUTH_Z + 0.022), v3(x, y - 0.002, MOUTH_Z + 0.004 - (i % 3) * 0.003)
        else:
            a, b = v3(x, y, MOUTH_Z - 0.022), v3(x, y - 0.002, MOUTH_Z - 0.008 + (i % 2) * 0.003)
        d = np.minimum(d, capsule(q, a, b, 0.0034, 0.0011))
    return d


def teeth_color(V):
    return mix(np.tile(TOOTH, (len(V), 1)), TOOTH * 0.55, smooth01(0.0, 0.6, fbm(V, 120.0, 2, 5)))


# ------------------------------------------------------------------ mãos

def hand_frame(t):
    sh, el, wr = ARMS[t]
    a = norm(wr - el)                       # ao longo do antebraço, para baixo
    k = norm(np.cross(a, SK.frame("Mao_" + t)[:, 0]))
    side = np.cross(k, a)
    return Frame(wr, np.stack((side, k, a), axis=1), 1.35)


def hand_local(q, grip):
    palm = ellipsoid(q, v3(0.0, 0.0, 0.055), (0.035, 0.016, 0.06))
    d = palm
    for i, x in enumerate((-0.024, -0.008, 0.008, 0.024)):
        length_ = (0.13, 0.155, 0.15, 0.12)[i]
        curl = 0.03 + grip * 0.05
        pts = [v3(x, 0.0, 0.1), v3(x * 1.1, -curl * 0.4, 0.1 + length_ * 0.5), v3(x * 1.15, -curl, 0.1 + length_)]
        d = smin(d, polyline(q, pts, [0.0085, 0.0065, 0.004], 0.004), 0.008)
        # Nós dos dedos.
        d = smin(d, sphere(q, pts[1], 0.0085), 0.004)
    thumb = [v3(0.03, -0.01, 0.03), v3(0.045, -0.03, 0.07), v3(0.04, -0.045 - grip * 0.02, 0.1)]
    d = smin(d, polyline(q, thumb, [0.01, 0.008, 0.005], 0.004), 0.01)
    wrist = capsule(q, v3(0.0, 0.0, -0.03), v3(0.0, 0.0, 0.02), 0.026)
    return smin(d, wrist, 0.012)


def forearm_and_hand(P, fr, grip, el, wr):
    """O antebraço nu e ossudo (a manga arregaçada), com tendões, e a mão."""
    hand = fr.wrap(lambda q: hand_local(q, grip))(P)
    start = el + norm(wr - el) * 0.06
    arm = capsule(P, start, wr, 0.036, 0.027)
    # Tendões e o osso do pulso.
    arm = arm - np.clip(vnoise(P * v3(1.0, 1.0, 0.3), 60.0, 7), 0.0, 1.0) * 0.003
    arm = smin(arm, sphere(P, wr + norm(wr - el) * -0.01, 0.03), 0.01)
    return smin(hand, arm, 0.015)


# ------------------------------------------------------------------ botas

def boots(P):
    d = np.full(len(P), 1e3)
    for t in ("D", "E"):
        foot = F("Pe_" + t)
        q = foot.local(P)
        sole = ellipsoid(q, v3(0.0, -0.075, -0.035), (0.062, 0.145, 0.05))
        shaft = capsule(q, v3(0.0, 0.0, -0.02), v3(0.0, 0.005, 0.27), 0.074, 0.08)
        d = np.minimum(d, smin(sole, shaft, 0.04))
    d = smax(d, -P[:, 2] + 0.002, 0.004)
    return d


def boots_color(V):
    return mix(np.tile(RUBBER, (len(V), 1)), RUBBER * 2.2, smooth01(0.3, 0.8, fbm(V, 8.0, 3, 19)) * 0.5)


# ------------------------------------------------------------------ chaves

KEYS = F("Chaves")


def keys_local(q):
    ring = length(np.stack((length(q[:, [0, 2]] - np.array((0.0, -0.06))) - 0.032, q[:, 1]), axis=1)) - 0.0035
    d = ring
    for i in range(7):
        a = math.radians(-60 + i * 20)
        R = np.array(((math.cos(a), 0, -math.sin(a)), (0, 1, 0), (math.sin(a), 0, math.cos(a))))
        c = v3(math.sin(a) * 0.035, 0.0, -0.06 - math.cos(a) * 0.035) + R @ v3(0.0, 0.0, -0.035)
        blade = box(q, c, (0.006, 0.0018, 0.032), 0.001, R)
        bow = sphere(q, c + R @ v3(0.0, 0.0, 0.034), 0.011)
        d = np.minimum(d, smin(blade, bow, 0.003))
    return d


# ------------------------------------------------------------------ esfregão

MOP_A, MOP_B = B("Esfregao"), B("Esfregao_Ponta")
MOP_STRANDS = []


def _strands():
    axis = norm(MOP_B - MOP_A)
    out = []
    for i in range(34):
        ang = RNG.uniform(0, 2 * math.pi)
        spread = RNG.uniform(0.04, 0.2)
        start = MOP_B + axis * 0.02 + v3(math.cos(ang), math.sin(ang), 0.0) * 0.03
        mid = start + v3(math.cos(ang), math.sin(ang), 0.0) * spread * 0.5 + v3(0, 0, -max(0.05, (start[2] - 0.01) * 0.6))
        end = v3(start[0] + math.cos(ang) * spread, start[1] + math.sin(ang) * spread + 0.08, 0.012)
        out.append(catmull([tuple(start), tuple(mid), tuple(end)], 3))
    return out


def mop(P):
    d = capsule(P, MOP_A - norm(MOP_B - MOP_A) * 0.12, MOP_B, 0.0145)
    d = np.minimum(d, capsule(P, MOP_B - norm(MOP_B - MOP_A) * 0.01, MOP_B + norm(MOP_B - MOP_A) * 0.05, 0.03))
    for pts in MOP_STRANDS:
        d = np.minimum(d, polyline(P, [np.asarray(p) for p in pts], 0.011))
    return smax(d, -P[:, 2] + 0.001, 0.003)


def mop_color(V):
    along = (V - MOP_A) @ norm(MOP_B - MOP_A)
    handle = along < length(MOP_B - MOP_A) - 0.01
    c = mix(np.tile(MOP, (len(V), 1)), MOP_WET, smooth01(0.3, 0.05, V[:, 2]) + 0.3 * fbm(V, 12.0, 3, 2))
    c[handle] = HANDLE
    return c


# ------------------------------------------------------------------ montagem

def build():
    global MOP_STRANDS
    pb.clear_scene()
    if not EXPORT:
        pb.stage(wall=(0.62, 0.64, 0.66), floor=(0.36, 0.37, 0.38), ceiling=3.6, warm=False, dim=0.6)
    MOP_STRANDS = _strands()

    m_cloth = pb.material("Zelador_Macacao", rough=0.85, sheen=0.1, bump=0.3, bump_scale=500.0)
    m_skin = pb.material("Zelador_Pele", rough=0.35, spec=0.6, sss=0.05, bump=0.4, bump_scale=900.0)
    m_cap = pb.material("Zelador_Gorro", rough=0.95, sheen=0.2)
    m_tooth = pb.material("Zelador_Dente", rough=0.4)
    m_rubber = pb.material("Zelador_Borracha", rough=0.4)
    m_metal = pb.material("Zelador_Metal", rough=0.35, metal=1.0)
    m_mop = pb.material("Zelador_Esfregao", rough=0.6)
    objs = []

    pts = [B(n) for n in ("Bacia", "Coluna", "Ombros", "Coxa_D", "Coxa_E", "Pe_D", "Pe_E")]
    lo, hi = bounds(pts, 0.28)
    objs.append(pb.sdf_mesh("Zelador_Corpo", coverall, lo, hi, H_BODY, cloth_color, m_cloth))
    lo, hi = bounds([B(n) for n in ("Braco_D", "Braco_E", "Antebraco_D", "Antebraco_E", "Mao_D", "Mao_E")], 0.1)
    objs.append(pb.sdf_mesh("Zelador_Mangas", sleeves, lo, hi, H_BODY * 0.9, cloth_color, m_cloth))

    corners = [HEAD.world(v3(x, y, z)) for x in (-0.13, 0.13) for y in (-0.16, 0.14) for z in (-0.1, 0.3)] + [B("Pescoco")]
    lo, hi = bounds(corners, 0.03)
    objs.append(pb.sdf_mesh("Zelador_Cabeca", head_sdf, lo, hi, H_HEAD, skin_color, m_skin))
    objs.append(pb.sdf_mesh("Zelador_Gorro", HEAD.wrap(cap_local_sdf), lo, hi, H_HEAD, cap_color, m_cap))
    tl = [HEAD.world(v3(x, y, z)) for x in (-0.09, 0.09) for y in (-0.13, -0.08) for z in (0.0, 0.08)]
    lo, hi = bounds(tl, 0.01)
    objs.append(pb.sdf_mesh("Zelador_DentesCima", HEAD.wrap(lambda q: teeth_local(q, True)), lo, hi, 0.0009, teeth_color, m_tooth))
    objs.append(pb.sdf_mesh("Zelador_DentesBaixo", HEAD.wrap(lambda q: teeth_local(q, False)), lo, hi, 0.0009, teeth_color, m_tooth))

    for t in ("D", "E"):
        fr = hand_frame(t)
        grip = 1.0 if t == "D" else 0.2
        corners = [fr.world(v3(x, y, z)) for x in (-0.08, 0.08) for y in (-0.1, 0.05) for z in (-0.05, 0.28)]
        lo, hi = bounds(corners, 0.01)
        sh, el, wr = ARMS[t]
        corners += [el - v3(0.06, 0.06, 0.06), el + v3(0.06, 0.06, 0.06)]
        lo, hi = bounds(corners, 0.01)
        objs.append(pb.sdf_mesh("Zelador_Mao" + t, lambda P, f=fr, g=grip, e=el, w=wr: forearm_and_hand(P, f, g, e, w),
                                lo, hi, 0.0019, skin_color, m_skin))

    lo, hi = bounds([B("Pe_D"), B("Pe_E"), B("Canela_D") * 0.4 + B("Pe_D") * 0.6, B("Canela_E") * 0.4 + B("Pe_E") * 0.6], 0.2)
    lo[2] = -0.01
    objs.append(pb.sdf_mesh("Zelador_Botas", boots, lo, hi, 0.0035, boots_color, m_rubber))

    corners = [KEYS.world(v3(x, y, z)) for x in (-0.1, 0.1) for y in (-0.03, 0.03) for z in (-0.18, 0.02)]
    lo, hi = bounds(corners, 0.01)
    objs.append(pb.sdf_mesh("Zelador_Chaves", KEYS.wrap(keys_local), lo, hi, 0.0012, lambda V: np.tile(METAL, (len(V), 1)), m_metal))

    strand_pts = [np.asarray(p) for s in MOP_STRANDS for p in s]
    lo, hi = bounds([MOP_A, MOP_B] + strand_pts, 0.04)
    lo[2] = -0.01
    objs.append(pb.sdf_mesh("Zelador_Esfregao", mop, lo, hi, 0.004, mop_color, m_mop))

    objs = [o for o in objs if o is not None]
    print(f"[personagem] Zelador: {pb.triangles(objs)} triângulos (antes de reduzir)")
    return objs


# ------------------------------------------------------------------ exportação para o jogo

BONES = ["Zelador", "Bacia", "Coluna", "Ombros", "Pescoco", "Cabeca", "Mandibula",
         "Coxa_D", "Coxa_E", "Canela_D", "Canela_E", "Pe_D", "Pe_E",
         "Braco_D", "Braco_E", "Antebraco_D", "Antebraco_E", "Mao_D", "Mao_E", "Esfregao", "Chaves"]


def export_game(objs):
    by = {o.name: o for o in objs}

    def seg(bone, a, b):
        return (bone, B(a) if isinstance(a, str) else a, B(b) if isinstance(b, str) else b)

    top = B("Ombros") + SK.axis("Ombros", "y") * 0.08
    spine = [seg("Bacia", "Bacia", "Coluna"), seg("Coluna", "Coluna", "Ombros"), seg("Ombros", "Ombros", top)]
    legs = []
    for t in ("D", "E"):
        legs += [seg("Coxa_" + t, "Coxa_" + t, "Canela_" + t), seg("Canela_" + t, "Canela_" + t, "Pe_" + t)]
    body_w = pe.chain(spine + legs, caps={"Coxa_D": 0.7, "Coxa_E": 0.7})

    arms, hand_w = [], {}
    for t in ("D", "E"):
        tip = B("Mao_" + t) + norm(B("Mao_" + t) - B("Antebraco_" + t)) * 0.25
        arm = [seg("Braco_" + t, "Braco_" + t, "Antebraco_" + t), seg("Antebraco_" + t, "Antebraco_" + t, "Mao_" + t)]
        arms += [seg("Ombros", "Ombros", "Braco_" + t)] + arm
        hand_w[t] = pe.chain([arm[1], seg("Mao_" + t, "Mao_" + t, tip)])
    sleeve_w = pe.chain(arms)

    neck = pe.chain([seg("Ombros", "Ombros", "Pescoco"), seg("Pescoco", "Pescoco", "Cabeca")])
    head_jaw = pe.blend(pe.rigid("Cabeca"), pe.rigid("Mandibula"),
                        lambda V: smooth01(MOUTH_Z + 0.004, MOUTH_Z - 0.008, HEAD.local(V)[:, 2]) * smooth01(-0.02, -0.06, HEAD.local(V)[:, 1]))
    head_w = pe.blend(neck, head_jaw, lambda V: smooth01(-0.07, -0.02, HEAD.local(V)[:, 2]))

    boot_w = {}
    feet = []
    for t in ("D", "E"):
        toe = B("Pe_" + t) - SK.axis("Pe_" + t, "z") * -0.2
        feet += [seg("Canela_" + t, "Canela_" + t, "Pe_" + t), seg("Pe_" + t, "Pe_" + t, toe)]
    boots_w = pe.chain(feet)

    P = pe.Part
    parts = [
        P(by["Zelador_Corpo"], "Macacao", 7500, body_w, 1.0),
        P(by["Zelador_Mangas"], "Macacao", 2600, sleeve_w, 0.8),
        P(by["Zelador_Cabeca"], "Pele", 7000, head_w, 2.4),
        P(by["Zelador_Gorro"], "Tecido", 2600, pe.rigid("Cabeca"), 1.2),
        P(by["Zelador_DentesCima"], "Dente", 700, pe.rigid("Cabeca"), 0.8),
        P(by["Zelador_DentesBaixo"], "Dente", 700, pe.rigid("Mandibula"), 0.8),
        P(by["Zelador_MaoD"], "Pele", 2600, hand_w["D"], 1.4),
        P(by["Zelador_MaoE"], "Pele", 2600, hand_w["E"], 1.4),
        P(by["Zelador_Botas"], "Borracha", 1600, boots_w, 0.8),
        P(by["Zelador_Chaves"], "Metal", 900, pe.rigid("Chaves"), 0.5),
        P(by["Zelador_Esfregao"], "Encharcada", 3200, pe.rigid("Esfregao"), 0.9),
    ]
    pe.export(parts, BONES, "Zelador")


def photos():
    out = pb.PHOTO_DIR
    mid = v3(0.0, 0.0, 1.15)
    face = HEAD.world(v3(0.0, -0.08, 0.06))
    pb.shoot(os.path.join(out, "zelador_frente.png"), v3(0.15, -4.6, 1.5), mid, 35, (900, 1200))
    pb.shoot(os.path.join(out, "zelador_34.png"), v3(2.8, -3.6, 1.6), mid, 35, (900, 1200))
    pb.shoot(os.path.join(out, "zelador_lado.png"), v3(4.2, -0.2, 1.4), mid, 35, (900, 1200))
    pb.shoot(os.path.join(out, "zelador_costas.png"), v3(-1.6, 3.0, 1.7), mid, 35, (900, 1200))
    pb.shoot(os.path.join(out, "zelador_rosto.png"), face + v3(0.1, -0.75, -0.05), face, 70, (900, 900))
    pb.shoot(os.path.join(out, "zelador_rosto_34.png"), face + v3(0.55, -0.6, -0.08), face, 70, (900, 900))
    pb.shoot(os.path.join(out, "zelador_de_baixo.png"), v3(0.1, -1.3, 0.9), face + v3(0, 0, 0.05), 40, (900, 1200))
    pb.shoot(os.path.join(out, "zelador_jogador.png"), v3(0.4, -3.2, 1.65), v3(0.0, -0.2, 1.4), 24, (1280, 720))


def main():
    objs = build()
    if EXPORT:
        export_game(objs)
        return
    photos()


pb.run(main)
