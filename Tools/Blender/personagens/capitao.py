"""O Capitão, inimigo do Level 1 (Docs/level1-mostruario.md, seção 5), esculpido no Blender.

Homem alto demais (2,6 m até o alto da cabeça), magro, em fantasia de pirata gasta: chapéu de três
bicos de feltro marrom puído, casaco comprido sem mangas cor de areia com mancha de sangue,
camisa verde-menta de mangas largas, faixa lilás e cinto preto, calça escura, bota marrom na
direita e perna de pau na esquerda. Rosto de pele escura cheio de vincos, olhos arregalados de
pupila pequena, boca aberta com os dentes de cima à mostra, barba preta curta; o olho esquerdo
é caído e roxo, o nariz quebrado, cortes e arranhões.

Por enquanto só para ver (fotos em Logs/Blender/personagens/capitao_*.png), na pose parada:
torto para o lado da perna de pau, braços abertos e duros, dedos esticados, cabeça inclinada.

    blender -b --factory-startup -P Tools/Blender/personagens/capitao.py -- [--rosto]

--rosto: só cabeça, chapéu e mãos (mais rápido, para acertar o rosto).
--exportar: esculpe sobre o esqueleto do jogo (esqueleto_capitao.json, gravado pelo Unity em
    BackRooms > Personagens > Exportar esqueletos), na pose parada do CaptainBody, e grava o
    modelo do jogo em Assets/_Game/Art/Personagens/Capitao (ver personagem_export.py).
"""

import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import personagem_base as pb  # noqa: E402
from personagem_base import (v3, rot, Frame, sphere, ellipsoid, capsule, box, smin, smax,  # noqa: E402
                             polyline, catmull, ribbon, fbm, vnoise, length, norm, on_surface)

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ONLY_FACE = "--rosto" in ARGS
EXPORT = "--exportar" in ARGS

S = 1.55          # a cabeça é esculpida em medidas de gente comum e aumentada
H_FACE = 0.0021   # passo da grade no rosto (m, no mundo)
H_BODY = 0.0042

# ------------------------------------------------------------------ esqueleto (pose parada)

J = dict(
    pelvis=v3(0.0, 0.01, 1.30),
    hipR=v3(-0.115, 0.01, 1.28), hipL=v3(0.115, 0.01, 1.28),
    kneeR=v3(-0.13, -0.03, 0.68), ankleR=v3(-0.14, 0.0, 0.10),
    kneeL=v3(0.145, -0.03, 0.66), pegTip=v3(0.175, -0.01, 0.0),
    shoulderR=v3(-0.20, 0.03, 2.035), shoulderL=v3(0.26, 0.03, 1.985),
    neck=v3(0.03, 0.0, 2.07), head=v3(0.06, -0.085, 2.27),
    elbowR=v3(-0.36, -0.20, 1.62), wristR=v3(-0.51, -0.52, 1.34),
    elbowL=v3(0.42, -0.18, 1.57), wristL=v3(0.58, -0.48, 1.28),
)

HEAD_R = rot(x=14.0, y=-15.0, z=4.0)
HEAD = Frame(J["head"] + HEAD_R @ (v3(0.0, -0.012, 0.072) * S), HEAD_R, S)
HAT = Frame(v3(0.0, 0.006, 0.068), rot(x=-8.0, y=6.0), 1.0)   # dentro do sistema da cabeça

# Na exportação tudo é esculpido no "espaço do modelo" (o mesmo das fotos: bacia em
# (0, 0.01, 1.30), frente em -Y) e depois levado para o lugar do esqueleto do jogo por TORSO.
TORSO = Frame(v3(0.0, 0.0, 0.0))
HANDS = {}
SK = None


def load_skeleton():
    """Troca a pose das fotos pela pose parada do jogo (o esqueleto gravado pelo Unity)."""
    global J, HEAD_R, HEAD, TORSO, SK
    import personagem_export as pe
    SK = pe.Skeleton("capitao")
    Rb = SK.frame("Bacia")
    TORSO = Frame(SK.pos("Bacia") - Rb @ v3(0.0, 0.01, 1.30), Rb, 1.0)

    def L(n):
        return TORSO.local(SK.pos(n)[None])[0]

    def A(n, a):
        return Rb.T @ SK.axis(n, a)

    peg_top, peg_end = L("Perna_De_Pau"), L("Perna_De_Pau_Ponta")
    J = dict(
        pelvis=L("Bacia"), hipR=L("Coxa_D"), hipL=L("Coxa_E"),
        kneeR=L("Joelho_D"), ankleR=L("Pe_D") + v3(0.0, 0.0, 0.06),
        kneeL=L("Joelho_E"), pegTip=peg_end + norm(peg_end - peg_top) * 0.09,
        shoulderR=L("Braco_D"), shoulderL=L("Braco_E"),
        neck=L("Pescoco"), head=L("Cabeca"),
        elbowR=L("Antebraco_D"), wristR=L("Mao_D"),
        elbowL=L("Antebraco_E"), wristL=L("Mao_E"),
    )
    HEAD_R = Rb.T @ SK.frame("Cabeca")
    HEAD = Frame(J["head"] + HEAD_R @ (v3(0.0, -0.012, 0.072) * S), HEAD_R, S)
    for side, tag in ((-1, "D"), (1, "E")):
        a = -A("Mao_" + tag, "y")
        k = A("Mao_" + tag, "z")
        k = norm(k - a * float(np.dot(k, a)))
        # No Unity o polegar fica para dentro (-X na mão direita, +X na esquerda).
        t = A("Mao_" + tag, "x") * (1.0 if tag == "D" else -1.0) * -1.0
        t = norm(t - a * float(np.dot(t, a)) - k * float(np.dot(t, k)))
        wr = J["wrist" + ("R" if tag == "D" else "L")]
        HANDS[side] = Frame(wr - a * 0.02, np.stack((a, t, k), axis=1), 1.3)

# ------------------------------------------------------------------ cores (sRGB)

SKIN = np.array((0.27, 0.175, 0.125))
LIP = np.array((0.21, 0.115, 0.115))
BRUISE = np.array((0.24, 0.11, 0.17))
BRUISE_OLD = np.array((0.40, 0.33, 0.13))
BLOOD = np.array((0.33, 0.03, 0.03))
BLOOD_DRY = np.array((0.22, 0.04, 0.03))
MAW = np.array((0.14, 0.035, 0.035))
FELT = np.array((0.13, 0.095, 0.072))
COAT = np.array((0.40, 0.355, 0.28))
SHIRT = np.array((0.32, 0.38, 0.33))
SASH = np.array((0.31, 0.26, 0.32))
PANTS = np.array((0.085, 0.085, 0.09))
LEATHER = np.array((0.21, 0.135, 0.085))
WOOD = np.array((0.30, 0.21, 0.13))


def mix(a, b, w):
    w = np.clip(w, 0.0, 1.0)[:, None]
    return a * (1 - w) + b * w


def smooth01(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


# ------------------------------------------------------------------ cabeça (coordenadas locais)

EYE_R = v3(-0.032, -0.071, 0.002)
EYE_L = v3(0.033, -0.0685, -0.0045)
EYE_RADIUS = 0.0134


def eye_cone(q, c, A, b_up, b_down, tilt=0.0):
    """Abertura das pálpebras: cone elíptico saindo do centro do olho (dentro negativo)."""
    v = q - c
    if tilt:
        ct, st = math.cos(tilt), math.sin(tilt)
        v = np.stack((v[:, 0] * ct + v[:, 2] * st, v[:, 1], -v[:, 0] * st + v[:, 2] * ct), axis=1)
    ax = np.arctan2(v[:, 0], -v[:, 1])
    az = np.arctan2(v[:, 2], -v[:, 1])
    B = np.where(az > 0, b_up, b_down)
    e = np.sqrt((ax / A) ** 2 + (az / B) ** 2)
    return (e - 1.0) * length(v) * 0.6


def skull(q):
    d = ellipsoid(q, (0.0, 0.012, 0.035), (0.073, 0.096, 0.088))
    d = smin(d, ellipsoid(q, (0.0, -0.03, -0.035), (0.058, 0.058, 0.085)), 0.03)
    d = smin(d, ellipsoid(q, (0.0, -0.06, -0.075), (0.034, 0.03, 0.03)), 0.02)
    # maçãs do rosto: a esquerda inchada
    d = smin(d, ellipsoid(q, (-0.05, -0.055, -0.02), (0.02, 0.02, 0.014)), 0.03)
    d = smin(d, ellipsoid(q, (0.05, -0.056, -0.022), (0.022, 0.022, 0.016)), 0.03)
    # maxilar e queixo
    for s in (-1, 1):
        d = smin(d, capsule(q, (s * 0.057, 0.005, -0.062), (s * 0.024, -0.062, -0.115), 0.013), 0.025)
    d = smin(d, ellipsoid(q, (0.002, -0.07, -0.117), (0.022, 0.018, 0.016)), 0.02)
    # bochechas fundas e têmporas cavadas
    for s in (-1, 1):
        d = smax(d, -ellipsoid(q, (s * 0.055, -0.05, -0.062), (0.019, 0.03, 0.027)), 0.02)
        d = smax(d, -ellipsoid(q, (s * 0.072, -0.045, 0.028), (0.014, 0.028, 0.026)), 0.015)
    # arco das sobrancelhas: direita alta, esquerda baixa
    d = smin(d, capsule(q, (-0.050, -0.074, 0.024), (-0.010, -0.088, 0.029), 0.0105), 0.015)
    d = smin(d, capsule(q, (0.010, -0.088, 0.022), (0.050, -0.074, 0.010), 0.0105), 0.015)
    # órbitas fundas: os olhos afundam na sombra da testa
    for e in (EYE_R, EYE_L):
        d = smax(d, -ellipsoid(q, e + v3(0.0, -0.007, 0.001), (0.022, 0.016, 0.019)), 0.007)
    # pescoço comprido, tendões e pomo de adão
    d = smin(d, capsule(q, (0.0, 0.012, -0.07), (0.0, 0.03, -0.33), 0.036, 0.041), 0.03)
    for s in (-1, 1):
        d = smin(d, capsule(q, (s * 0.05, 0.02, -0.065), (s * 0.012, -0.022, -0.27), 0.009), 0.015)
    d = smin(d, ellipsoid(q, (0.0, -0.03, -0.165), (0.007, 0.0075, 0.012)), 0.025)
    # orelhas
    for s, dz in ((-1, 0.0), (1, -0.004)):
        R = rot(x=-15.0, z=s * 22.0)
        c = v3(s * 0.073, 0.012, -0.012 + dz)
        ear = ellipsoid(q, c, (0.008, 0.02, 0.032), R)
        ear = smax(ear, -ellipsoid(q, c + v3(s * 0.007, -0.002, 0.0), (0.005, 0.013, 0.021), R), 0.003)
        d = smin(d, ear, 0.006)
    return d


def nose(q):
    d = polyline(q, [(0.0, -0.086, 0.012), (-0.003, -0.099, -0.012), (0.006, -0.115, -0.04)],
                 [0.0075, 0.0088, 0.011], 0.004)
    d = smin(d, sphere(q, (-0.004, -0.0985, -0.008), 0.0075), 0.004)  # o calombo da fratura
    d = smin(d, ellipsoid(q, (0.006, -0.112, -0.042), (0.0095, 0.0095, 0.0095)), 0.006)
    for s in (-1, 1):
        d = smin(d, ellipsoid(q, (0.006 + s * 0.0145, -0.099, -0.048), (0.0075, 0.0085, 0.007)), 0.007)
    d = smin(d, capsule(q, (0.005, -0.113, -0.05), (0.002, -0.1, -0.057), 0.005), 0.004)
    return d


UPPER_LIP = [(-0.033, -0.074, -0.077), (-0.017, -0.089, -0.071), (0.002, -0.094, -0.0695),
             (0.019, -0.089, -0.072), (0.034, -0.074, -0.079)]
LOWER_LIP = [(-0.031, -0.074, -0.092), (-0.015, -0.088, -0.101), (0.002, -0.091, -0.1035),
             (0.018, -0.088, -0.101), (0.032, -0.074, -0.093)]
MOUTH = (v3(0.001, -0.074, -0.0862), (0.03, 0.03, 0.0118))


def face_no_creases(q):
    d = smin(skull(q), nose(q), 0.006)
    # pálpebras: casca em volta do olho, aberta por um cone (o esquerdo caído e inchado)
    d = smin(d, sphere(q, EYE_R, EYE_RADIUS + 0.0021), 0.005)
    d = smin(d, sphere(q, EYE_L + v3(0.0, -0.001, 0.002), EYE_RADIUS + 0.0036), 0.006)
    cut_r = smax(eye_cone(q, EYE_R, 1.12, 0.92, 0.72), sphere(q, EYE_R, 0.021), 0.002)
    cut_l = smax(eye_cone(q, EYE_L, 1.05, 0.30, 0.62, tilt=-0.18), sphere(q, EYE_L, 0.021), 0.002)
    d = smax(d, -cut_r, 0.0015)
    d = smax(d, -cut_l, 0.0015)
    # boca aberta
    d = smin(d, polyline(q, catmull(UPPER_LIP, 4), 0.0034, 0.002), 0.007)
    d = smin(d, polyline(q, catmull(LOWER_LIP, 4), 0.0042, 0.002), 0.007)
    d = smax(d, -ellipsoid(q, MOUTH[0], MOUTH[1]), 0.003)
    d = np.minimum(d, ellipsoid(q, (0.001, -0.066, -0.099), (0.018, 0.025, 0.008)))  # língua
    # narinas
    for s in (-1, 1):
        d = smax(d, -ellipsoid(q, (0.005 + s * 0.0085, -0.104, -0.0545), (0.0045, 0.006, 0.003)), 0.002)
    return d


def _crease_lines():
    """Vincos fundos (testa, olheiras, bigode chinês, pés de galinha) e os cortes, já pousados
    na superfície do rosto. (pontos, raio)"""
    lines = []
    for z, r, wob in ((0.052, 0.0016, 0.003), (0.064, 0.0014, -0.002), (0.076, 0.0012, 0.002)):
        pts = [(x, -0.12, z + wob * math.sin(x * 90) - (0.006 if x > 0.01 else 0.0) * (x / 0.04))
               for x in np.linspace(-0.042, 0.04, 8)]
        lines.append((pts, r))
    lines.append(([(-0.008, -0.12, 0.011), (-0.006, -0.12, 0.034)], 0.0016))
    lines.append(([(0.007, -0.12, 0.009), (0.004, -0.12, 0.030)], 0.0015))
    # olheiras
    for c, drop in ((EYE_R, 0.0), (EYE_L, 0.004)):
        s = 1 if c[0] > 0 else -1
        for k in (0.002,):
            z0 = c[2] - 0.015 - drop - k
            lines.append(([(c[0] - s * 0.013, -0.12, z0 + 0.004), (c[0], -0.12, z0 - 0.002),
                           (c[0] + s * 0.014, -0.12, z0 + 0.003)], 0.0016 - k * 0.05))
        for a in (-0.4, 0.0, 0.4):   # pés de galinha
            o = c + v3(s * 0.017, 0.0, 0.0)
            lines.append(([(o[0], -0.09, o[2] + a * 0.006), (o[0] + s * 0.012, -0.08, o[2] + a * 0.016)], 0.0011))
    # bigode chinês e vincos das bochechas
    for s in (-1, 1):
        x0 = 0.006
        lines.append(([(x0 + s * 0.021, -0.12, -0.045), (x0 + s * 0.031, -0.12, -0.062),
                       (x0 + s * 0.036, -0.12, -0.08), (s * 0.036, -0.12, -0.099)], 0.0023))
        lines.append(([(s * 0.046, -0.12, -0.03), (s * 0.05, -0.12, -0.065), (s * 0.046, -0.12, -0.1)], 0.0015))
    # pescoço
    for z in (-0.215, -0.245):
        lines.append(([(x, -0.08, z + 0.004 * math.cos(x * 60)) for x in np.linspace(-0.03, 0.03, 5)], 0.0014))
    # feridas: corte na testa, corte no nariz
    lines.append(([(-0.022, -0.12, 0.060), (-0.006, -0.12, 0.066), (0.009, -0.12, 0.071)], 0.0012))
    lines.append(([(-0.006, -0.12, -0.004), (0.003, -0.12, -0.012)], 0.0010))
    out = []
    for pts, r in lines:
        pts = catmull(pts, 4) if len(pts) > 2 else pts
        out.append((on_surface(face_no_creases, pts), r))
    return out


CREASES = None


def face(q):
    d0 = face_no_creases(q)
    d = d0
    for pts, r in CREASES:
        d = smax(d, -polyline(q, pts, r), 0.002)
    d = smax(d, -sphere(q, (0.010, -0.098, -0.1005), 0.0026), 0.001)   # lábio partido
    return d


SCRATCHES = [((-0.028 + o, -0.09, -0.014 - o * 0.4), (-0.05 + o, -0.075, -0.048 - o * 0.4)) for o in (0.0, 0.006, 0.012)]
BLOOD_DRIP = [(-0.008, -0.095, 0.066), (-0.009, -0.095, 0.058), (-0.007, -0.096, 0.050),
              (-0.009, -0.096, 0.042), (-0.008, -0.096, 0.036)]


def seg_dist(q, a, b):
    return capsule(q, np.asarray(a), np.asarray(b), 0.0)


def skin_color(V, local=None):
    q = HEAD.local(V) if local is None else local
    n = fbm(q, 70.0, 4, 3)
    c = SKIN * (1.0 + 0.1 * n)[:, None]
    c = mix(c, np.array((0.25, 0.23, 0.2)), smooth01(-0.1, 0.4, fbm(q, 28.0, 3, 4)) * 0.45)   # pele cinzenta, doente
    c = mix(c, np.array((0.32, 0.16, 0.13)), 0.3 * np.exp(-length((q - (0.006, -0.11, -0.04)) / 0.02) ** 2))
    for e in (EYE_R, EYE_L):    # órbitas fundas e escuras
        w = np.exp(-length((q - (e + v3(0.0, -0.004, 0.002))) / v3(0.028, 0.03, 0.024)) ** 2)
        c = c * (1.0 - 0.55 * w)[:, None]
    # vincos: quanto mais fundo, mais escuro
    depth = np.clip((face_no_creases(q) - face(q)) / 0.0015, 0.0, 1.0)
    c = c * (1.0 - 0.5 * depth)[:, None]
    # beira das pálpebras, úmida e mais escura
    for e, rr in ((EYE_R, EYE_RADIUS + 0.0021), (EYE_L, EYE_RADIUS + 0.0036)):
        r = length(q - e)
        c = mix(c, np.array((0.36, 0.09, 0.08)), smooth01(rr + 0.0009, rr - 0.0006, r) * 0.55)
    # olheiras
    for e in (EYE_R, EYE_L):
        w = np.exp(-length((q - (e + v3(0.0, -0.01, -0.016))) / v3(0.016, 0.03, 0.009)) ** 2)
        c = mix(c, c * np.array((0.7, 0.62, 0.7)), 0.8 * w)
    # lábios e boca por dentro
    lip = np.minimum(polyline(q, UPPER_LIP, 0.0066), polyline(q, LOWER_LIP, 0.0078))
    c = mix(c, LIP, smooth01(0.002, -0.0005, lip))
    inner = (q[:, 1] > -0.093) & (ellipsoid(q, MOUTH[0], np.array(MOUTH[1]) + 0.003) < 0.0)
    c[inner] = MAW
    # hematomas: olho esquerdo, têmpora direita, queixo
    for center, rad, amt in ((EYE_L + v3(0.002, -0.005, -0.004), 0.036, 1.0),
                             (v3(-0.068, -0.05, 0.042), 0.02, 0.7),
                             (v3(0.03, -0.065, -0.112), 0.014, 0.6)):
        r = length(q - center) * (1.0 + 0.25 * fbm(q, 90.0, 3, 11))
        w = smooth01(rad, rad * 0.35, r) * amt
        halo = smooth01(rad * 1.25, rad * 0.8, r) * (1 - w) * 0.6 * amt
        c = mix(c, BRUISE_OLD * 0.75, halo * 0.45)
        c = mix(c, BRUISE, w)
    # arranhões na bochecha direita
    for a, b in SCRATCHES:
        dd = seg_dist(q, a, b)
        c = mix(c, np.array((0.5, 0.17, 0.13)), smooth01(0.0035, 0.0008, dd) * 0.6)
        c = mix(c, BLOOD, smooth01(0.0011, 0.0003, dd))
    # corte da testa e o sangue seco escorrido; corte no nariz; lábio partido
    cut = polyline(q, CREASES[-2][0], 0.0)
    c = mix(c, BLOOD_DRY, smooth01(0.004, 0.0012, cut) * 0.7)
    drip = polyline(q, BLOOD_DRIP, [0.0026, 0.0022, 0.002, 0.0016, 0.0009])
    c = mix(c, BLOOD_DRY * 1.2, smooth01(0.0006, -0.0004, drip))
    c = mix(c, BLOOD, smooth01(0.0025, 0.0005, polyline(q, CREASES[-1][0], 0.0)))
    c = mix(c, BLOOD, smooth01(0.005, 0.0015, length(q - (0.010, -0.098, -0.1005))))
    for path, w0 in ((NOSEBLEED_R, 0.0032), (NOSEBLEED_L, 0.0024), (MOUTH_DRIBBLE, 0.0026)):
        dd = polyline(q, path, list(np.linspace(w0, w0 * 0.35, len(path))))
        c = mix(c, BLOOD_DRY, smooth01(0.0008, -0.0006, dd + 0.0006 * fbm(q, 500.0, 2, 13)))
    return np.clip(c, 0, 1)


NOSEBLEED_R = [(-0.003, -0.106, -0.056), (-0.004, -0.106, -0.061), (-0.006, -0.103, -0.066), (-0.007, -0.1, -0.07)]
NOSEBLEED_L = [(0.014, -0.105, -0.056), (0.015, -0.104, -0.062), (0.016, -0.101, -0.067)]
MOUTH_DRIBBLE = [(-0.031, -0.077, -0.088), (-0.033, -0.076, -0.098), (-0.031, -0.077, -0.108), (-0.029, -0.078, -0.12)]


# ------------------------------------------------------------------ cabelo, barba, sobrancelhas

def beard_region(q):
    r = ellipsoid(q, (0.0, -0.02, -0.098), (0.088, 0.098, 0.047))
    r = smax(r, (q[:, 2] - (0.69 * q[:, 1] + 0.004)) * 0.8, 0.006)
    r = smax(r, -ellipsoid(q, (0.002, -0.092, -0.087), (0.037, 0.03, 0.0185)), 0.004)
    r = smax(r, q[:, 1] - 0.004 - 0.6 * np.maximum(0, -q[:, 2] - 0.09), 0.01)  # nada atrás da orelha
    return r


def hair_region(q):
    r = smax(-(q[:, 2] + 0.035), -(q[:, 1] + 0.02), 0.01)
    for s in (-1, 1):   # contorna a orelha
        r = smax(r, -ellipsoid(q, (s * 0.074, 0.012, -0.012), (0.03, 0.03, 0.045)), 0.004)
    return r


BROWS = None
MOUSTACHE = None


def _hair_lines():
    brow_r = [(-0.014, -0.12, 0.031), (-0.027, -0.12, 0.041), (-0.042, -0.12, 0.041), (-0.055, -0.12, 0.032)]
    brow_l1 = [(0.013, -0.12, 0.021), (0.024, -0.12, 0.019)]
    brow_l2 = [(0.031, -0.12, 0.016), (0.044, -0.12, 0.011), (0.055, -0.12, 0.004)]
    must = [(-0.031, -0.12, -0.088), (-0.022, -0.12, -0.071), (-0.008, -0.12, -0.065), (0.003, -0.12, -0.063),
            (0.014, -0.12, -0.065), (0.025, -0.12, -0.072), (0.033, -0.12, -0.089)]
    brows = [(on_surface(face_no_creases, catmull(b, 4) if len(b) > 2 else b, 0.0008), rads)
             for b, rads in ((brow_r, (0.0042, 0.0026)), (brow_l1, (0.0042, 0.0036)), (brow_l2, (0.0038, 0.0022)))]
    m = on_surface(face_no_creases, catmull(must, 3), 0.0012)
    return brows, m


def _hair_patch(base, region, thick, bump, edge=0.012):
    """Pelo como uma camada sobre a pele: grossa no meio da região, afinando até a borda."""
    w = np.clip(-region / edge, 0.0, 1.0) ** 0.6
    return np.maximum(base - (0.0007 + thick * w) - bump * w, region)


def hair_sdf(q):
    base = face_no_creases(q)
    bump = 0.0012 * fbm(q * v3(1.0, 1.0, 0.45), 260.0, 2, 21)
    d = _hair_patch(base, beard_region(q), 0.0042, bump)
    d = np.minimum(d, _hair_patch(base, hair_region(q), 0.0035, bump, 0.008))
    fuzz = 0.0005 * vnoise(q * v3(1.0, 1.0, 2.5), 700.0, 22)
    for pts, (r0, r1) in BROWS:
        dist = polyline(q, pts, 0.0)
        u = np.clip(q[:, 0] * np.sign(pts[-1][0]) / max(abs(pts[-1][0]), 1e-3), 0, 1)
        width = (r0 + (r1 - r0) * u) * 1.5
        d = np.minimum(d, _hair_patch(base, dist - width, 0.0009, fuzz, 0.0016))
    # bigode: faixa sobre o lábio de cima, das narinas ao canto da boca
    must = smax(ellipsoid(q, (0.003, -0.095, -0.064), (0.037, 0.03, 0.0095)), -(q[:, 2] + 0.0685), 0.003)
    must = smax(must, -ellipsoid(q, (0.005, -0.104, -0.053), (0.022, 0.012, 0.007)), 0.003)
    d = np.minimum(d, _hair_patch(base, must, 0.0032, bump + fuzz, 0.006))
    return d


def hair_color(V):
    q = HEAD.local(V)
    n = vnoise(q * v3(1.0, 1.0, 0.3), 400.0, 5)
    c = np.array((0.045, 0.04, 0.036)) * (1.0 + 0.35 * n)[:, None]
    c = c + np.array((0.02, 0.012, 0.006)) * (fbm(q, 60.0, 2, 8) > 0.3)[:, None]
    for path, w0 in ((NOSEBLEED_R, 0.006), (NOSEBLEED_L, 0.005), (MOUTH_DRIBBLE, 0.006)):
        c = mix(c, np.array((0.16, 0.025, 0.02)), smooth01(w0, w0 * 0.3, polyline(q, path, 0.0)) * 0.9)
    return np.clip(c, 0, 1)


# ------------------------------------------------------------------ olhos e dentes

def eye_color(gaze, veins_seed):
    def paint(dirs):
        cosang = dirs @ gaze
        ang = np.degrees(np.arccos(np.clip(cosang, -1, 1)))
        c = np.tile(np.array((0.76, 0.69, 0.52)), (len(dirs), 1))
        veins = (np.abs(vnoise(dirs, 18.0, veins_seed)) < 0.07) | (np.abs(vnoise(dirs, 40.0, veins_seed + 1)) < 0.05)
        c = np.where((veins & (ang > 24))[:, None], np.array((0.55, 0.12, 0.1)), c)
        c = mix(c, np.array((0.62, 0.25, 0.2)), smooth01(40, 85, ang) * 0.8)
        iris = ang < 19.0
        ring = fbm(dirs, 30.0, 3, veins_seed + 4)
        c[iris] = (np.array((0.17, 0.11, 0.06)) * (1 + 0.4 * ring[iris])[:, None])
        c = mix(c, np.array((0.08, 0.05, 0.03)), smooth01(16.5, 19.0, ang) * (ang < 19.0))
        c[ang < 5.5] = np.array((0.01, 0.008, 0.007))
        return c
    return paint


def teeth(q):
    d = None
    rng = np.random.default_rng(7)
    for i in range(11):
        phi = math.radians(-66 + i * 13.2)
        if i in (2, 8):
            continue    # falhas
        L = 0.0062 + rng.uniform(-0.0008, 0.0016) - (0.0025 if i in (0, 10) else 0.0)
        if i == 6:
            L -= 0.002  # dente lascado
        c = v3(0.024 * math.sin(phi) + 0.001, -0.066 - 0.0215 * math.cos(phi) + 0.0015, -0.0765 - L * 0.6)
        R = rot(x=rng.uniform(-6, 6), y=rng.uniform(-7, 7), z=-math.degrees(phi))
        di = box(q, c, (0.0037 if i in (3, 4, 5) else 0.0032, 0.0024, L), 0.0016, R)
        d = di if d is None else np.minimum(d, di)
    for i in range(8):
        phi = math.radians(-52 + i * 15)
        c = v3(0.018 * math.sin(phi) + 0.001, -0.065 - 0.018 * math.cos(phi) + 0.002, -0.0955)
        d = np.minimum(d, box(q, c, (0.0028, 0.002, 0.0035), 0.0012, rot(z=-math.degrees(phi))))
    d = np.minimum(d, ellipsoid(q, (0.001, -0.068, -0.073), (0.024, 0.02, 0.004)))  # gengiva
    return d


def teeth_color(V):
    q = HEAD.local(V)
    c = np.tile(np.array((0.5, 0.46, 0.36)), (len(q), 1)) * (1 + 0.12 * fbm(q, 600.0, 2, 9))[:, None]
    gum = q[:, 2] > -0.0755
    c[gum] = np.array((0.28, 0.08, 0.08))
    c = mix(c, np.array((0.2, 0.15, 0.1)), smooth01(-0.072, -0.078, q[:, 2]) * 0.85)      # podre na raiz
    c = mix(c, np.array((0.3, 0.25, 0.16)), smooth01(0.1, 0.4, fbm(q, 300.0, 2, 10)) * 0.6)
    return np.clip(c, 0, 1)


# ------------------------------------------------------------------ chapéu de três bicos

def hat_local(h):
    crown = ellipsoid(h, (0.0, 0.0, 0.035), (0.085, 0.104, 0.08))
    crown = smax(crown, -h[:, 2] - 0.002, 0.004)
    crown = smax(crown, h[:, 2] - 0.098, 0.015)
    crown = smax(crown, -ellipsoid(h, (0.025, -0.035, 0.112), (0.05, 0.035, 0.028)), 0.012)  # amassado
    crown = smax(crown, -capsule(h, (-0.05, 0.02, 0.1), (0.02, 0.06, 0.1), 0.004), 0.004)
    th = np.arctan2(h[:, 0], -h[:, 1])
    rn = np.sqrt((h[:, 0] / 0.085) ** 2 + (h[:, 1] / 0.104) ** 2)
    s3 = np.abs(np.sin(1.5 * th))
    back = np.maximum(0.0, -np.cos(th))
    up = (0.18 + 0.82 * s3 ** 0.7) * (1.0 - 0.6 * back ** 2)
    ext = np.maximum(rn - 1.0, 0.0)
    zb = 0.002 + ext * 0.125 * up - 0.03 * back ** 2 * ext + 0.006 * np.sin(5 * th) * ext
    ring = np.stack((np.cos(th), np.sin(th), np.zeros_like(th)), 1)
    edge = (1.5 + 0.38 * (1 - s3) ** 3 + 0.05 * vnoise(ring, 7.0, 4)
            + 0.012 * vnoise(ring, 22.0, 5) - 0.03 * np.clip(vnoise(ring, 13.0, 6) - 0.5, 0, 1))
    slope = np.sqrt(1.0 + (1.4 * up) ** 2)
    brim = np.abs(h[:, 2] - zb) / slope - 0.0027
    brim = np.maximum(brim, (rn - edge) * 0.088)
    brim = np.maximum(brim, (0.92 - rn) * 0.088)
    # rasgo em V na frente, à direita do bico
    tt = -0.62
    tear = (np.abs(th - tt) - 0.32 * np.maximum(rn - 1.18, 0.0)) * rn * 0.088
    tear = np.where(rn > 1.18, tear, 0.02)
    brim = np.maximum(brim, -tear)
    d = smin(crown, brim, 0.008)
    # furos
    d = smax(d, -sphere(h, (0.083, 0.02, 0.05), 0.0065), 0.002)
    hole = np.stack((np.sin(2.6) * 0.085 * 1.3, -np.cos(2.6) * 0.104 * 1.3, 0.0))
    d = smax(d, -capsule(h, hole + v3(0, 0, -0.05), hole + v3(0, 0, 0.08), 0.006), 0.002)
    return d, th, rn, edge


def hat_sdf_local(q):
    return hat_local(HAT.local(q))[0]


def hat_color(V):
    q = HEAD.local(V)
    h = HAT.local(q)
    _, th, rn, edge = hat_local(h)
    n = fbm(h, 45.0, 4, 31)
    c = FELT * (1.0 + 0.13 * n)[:, None]
    c = mix(c, np.array((0.44, 0.35, 0.26)), smooth01(edge - 0.12, edge - 0.01, rn) * 0.75)    # aba puída
    c = mix(c, np.array((0.33, 0.25, 0.18)), smooth01(0.08, 0.1, h[:, 2]) * (rn < 1.0) * 0.4)  # copa gasta
    c = mix(c, FELT * 0.55, smooth01(0.012, 0.0, h[:, 2]) * (rn < 1.0))                        # suor na base
    c = mix(c, FELT * 0.6, smooth01(0.25, 0.45, fbm(h, 18.0, 3, 41)) * 0.8)                    # manchas
    patch = box(h, (-0.07, -0.03, 0.055), (0.03, 0.04, 0.026), 0.008, rot(z=0.0))
    c = mix(c, np.array((0.14, 0.12, 0.11)) * (1 + 0.1 * vnoise(h, 300.0, 3))[:, None], smooth01(0.002, -0.001, patch))
    stitch = (np.abs(patch + 0.003) < 0.0012) & (np.sin(h[:, 1] * 900 + h[:, 2] * 900) > 0.2)
    c[stitch] = np.array((0.6, 0.55, 0.45))
    return np.clip(c, 0, 1)


COCKADE = None


def _cockade_place():
    """Ponto e normal na face de fora da aba levantada, do lado esquerdo do chapéu."""
    th = math.radians(72.0)
    rn = 1.22
    radial = v3(math.sin(th), -math.cos(th), 0.0)
    up = (0.18 + 0.82 * abs(math.sin(1.5 * th)) ** 0.7)
    zb = 0.002 + (rn - 1.0) * 0.125 * up
    mid = v3(math.sin(th) * 0.085 * rn, -math.cos(th) * 0.104 * rn, zb)
    out = norm(radial * (1.4 * up) - v3(0, 0, 1))
    fn = lambda h: hat_local(h)[0]
    p = pb.project(fn, mid + out * 0.012)
    n = pb.normal_at(fn, p)
    return p, n


def ribbon_sdf_local(q):
    """Fita branca do lado esquerdo: uma roseta franzida sobre a aba, com duas pontas caídas."""
    h = HAT.local(q)
    p, n = COCKADE
    R = pb.frame_from(n, v3(0, 0, 1))
    c = p + n * 0.0028
    v = (h - c) @ R
    rr = np.sqrt(v[:, 1] ** 2 + v[:, 2] ** 2)
    ang = np.arctan2(v[:, 2], v[:, 1])
    rosette = np.maximum(np.abs(v[:, 0] - 0.0018 * np.cos(ang * 9) * (rr / 0.02)) - 0.0024,
                         rr - (0.02 + 0.0022 * np.sin(ang * 11)))
    rosette = smin(rosette, sphere(h, c + n * 0.003, 0.0065), 0.002)
    side = R[:, 2]
    down = norm(-v3(0, 0, 1) + n * 0.25)
    t1 = ribbon(h, [c, c + down * 0.025 + side * 0.008, c + down * 0.05 + side * 0.014], side, 0.0048, 0.0012)
    t2 = ribbon(h, [c, c + down * 0.022 - side * 0.007, c + down * 0.042 - side * 0.016], side, 0.0045, 0.0012)
    return np.minimum(rosette, np.minimum(t1, t2))


# ------------------------------------------------------------------ corpo (mundo)

def torso(P):
    d = ellipsoid(P, (0.035, 0.025, 1.84), (0.175, 0.115, 0.24), rot(y=4.0))
    d = smin(d, capsule(P, J["shoulderR"], J["shoulderL"], 0.062), 0.06)
    d = smin(d, ellipsoid(P, (0.015, 0.02, 1.50), (0.135, 0.095, 0.2)), 0.07)
    d = smin(d, ellipsoid(P, (0.0, 0.02, 1.30), (0.165, 0.105, 0.12)), 0.07)
    return d


def coat_solid(P):
    d = torso(P) - 0.022
    Qs = P.copy()
    Qs[:, 1] = (P[:, 1] - 0.02) * 1.25 + 0.02
    skirt = capsule(Qs, (0.01, 0.02, 1.45), (0.025, 0.03, 0.72), 0.165, 0.29) * 0.85
    return smin(d, skirt, 0.06)


def coat(P):
    solid = coat_solid(P)
    d = np.abs(solid) - 0.0062
    hem = 0.745 + 0.03 * fbm(P, 9.0, 3, 51) + 0.036 * np.clip(vnoise(P, 35.0, 52), 0, 1) ** 2
    d = smax(d, hem - P[:, 2], 0.003)
    for c_, r_ in (((-0.2, -0.12, 0.95), 0.03), ((0.12, 0.2, 1.05), 0.025), ((0.24, -0.02, 0.86), 0.035)):
        d = smax(d, -(sphere(P, c_, r_) + 0.012 * fbm(P, 60.0, 2, 53)), 0.002)
    d = smax(d, -capsule(P, J["neck"] - v3(0, 0, 0.12), J["head"], 0.074), 0.01)
    vee = np.maximum(np.abs(P[:, 0] - 0.035) - (P[:, 2] - 1.80) * 0.5, np.maximum(1.80 - P[:, 2], P[:, 1] - 0.0))
    d = smax(d, -vee, 0.008)
    for side, sh in ((-1, J["shoulderR"]), (1, J["shoulderL"])):
        d = smax(d, -ellipsoid(P, sh + v3(side * 0.035, 0.0, -0.07), (0.072, 0.1, 0.135)), 0.012)
    opening = np.maximum(np.abs(P[:, 0] - 0.02) - (0.012 + np.maximum(0.0, 1.40 - P[:, 2]) * 0.3),
                         np.maximum(P[:, 2] - 1.40, P[:, 1] - 0.02))
    d = smax(d, -opening, 0.006)
    for side in (-1, 1):   # abas dos bolsos
        p = on_surface(coat_solid, [(side * 0.17, -0.2, 1.12)])[0]
        n = pb.normal_at(coat_solid, p)
        t = norm(np.cross(v3(0, 0, 1), n))
        R = np.stack((t, n, np.cross(t, n)), axis=1)
        d = np.minimum(d, box(P, p + n * 0.002, (0.075, 0.006, 0.03), 0.005, R))
    return d


BLOOD_PATH = [(0.075, -0.15, 1.99), (0.08, -0.17, 1.9), (0.072, -0.19, 1.8), (0.078, -0.2, 1.7),
              (0.07, -0.2, 1.62), (0.073, -0.2, 1.56)]


def coat_color(V):
    c = COAT * (1.0 + 0.07 * fbm(V, 14.0, 4, 61))[:, None]
    c = mix(c, COAT * 0.62, smooth01(1.0, 0.75, V[:, 2]) * (0.6 + 0.4 * fbm(V, 6.0, 2, 62)))  # barra suja
    c = mix(c, COAT * 0.72, smooth01(0.3, 0.6, fbm(V, 5.0, 3, 63)) * 0.6)
    c = mix(c, np.array((0.2, 0.17, 0.12)), smooth01(0.15, 0.5, fbm(V, 3.0, 4, 65)) * 0.5)     # umidade, graxa
    c = c * (1.0 - 0.18 * np.clip(fbm(V, 60.0, 2, 66), 0, 1))[:, None]
    near = np.abs(V[:, 0] - 0.02) < 0.012
    c[near & (V[:, 1] < 0) & (V[:, 2] > 1.4)] *= 0.85
    # mancha de sangue da gola para baixo
    path = on_surface(lambda P: coat_solid(P) - 0.0062, BLOOD_PATH, 0.0)
    dd = polyline(V, catmull(path, 4), list(np.linspace(0.05, 0.012, len(catmull(path, 4)))))
    w = smooth01(0.012, -0.012, dd + 0.012 * fbm(V, 40.0, 3, 64))
    c = mix(c, BLOOD_DRY, w * 0.9)
    c = mix(c, BLOOD, smooth01(0.01, -0.02, dd) * 0.6)
    for i, (x, z) in enumerate(((0.12, 1.75), (0.0, 1.66), (0.13, 1.6), (0.09, 1.5))):  # respingos
        r = length(V - v3(x, -0.2, z) * v3(1, 0, 1) - v3(0, 1, 0) * V[:, 1:2])
        c = mix(c, BLOOD_DRY, smooth01(0.012 - i * 0.002, 0.004, r) * (V[:, 1] < -0.05))
    return np.clip(c, 0, 1)


def buttons(P):
    d = None
    for z in np.arange(1.50, 1.95, 0.085):
        p = on_surface(lambda Q: coat_solid(Q) - 0.0062, [(0.052, -0.2, z)], 0.0)[0]
        n = pb.normal_at(coat_solid, p)
        R = pb.frame_from(n, v3(0, 0, 1))
        di = ellipsoid(P, p + n * 0.003, (0.006, 0.013, 0.013), R)
        d = di if d is None else np.minimum(d, di)
    # fivela do cinto
    p = on_surface(lambda Q: coat_solid(Q) - 0.0197, [(0.02, -0.2, 1.43)], 0.0)[0]
    n = pb.normal_at(coat_solid, p)
    R = np.stack((v3(1, 0, 0), n, v3(0, 0, 1)), axis=1)
    frame = box(P, p + n * 0.004, (0.042, 0.0045, 0.034), 0.004, R)
    frame = smax(frame, -box(P, p + n * 0.004, (0.028, 0.02, 0.021), 0.003, R), 0.002)
    frame = np.minimum(frame, capsule(P, p + n * 0.006 - v3(0.02, 0, 0), p + n * 0.006 + v3(0.012, 0, 0), 0.003))
    return np.minimum(d, frame)


def brass_color(V):
    return np.clip(np.array((0.62, 0.48, 0.22)) * (1 + 0.15 * fbm(V, 200.0, 2, 71))[:, None], 0, 1)


def arm(P, sh, el, wr, side):
    """Manga larga da camisa: do ombro ao punho, franzida e com babado no punho."""
    sh_in = sh - v3(side * 0.03, 0, 0)
    d = capsule(P, sh_in, el, 0.064, 0.058)
    d = smin(d, capsule(P, el, wr, 0.058, 0.05), 0.03)
    u = norm(wr - el)
    d = smin(d, ellipsoid(P, (sh + el) * 0.5 + v3(0, 0, -0.03), (0.068, 0.068, 0.09)), 0.05)  # bufante
    d = smin(d, capsule(P, wr - u * 0.07, wr - u * 0.01, 0.052, 0.062), 0.01)       # babado
    d = d - 0.0035 * fbm(P * v3(1, 1, 0.5), 22.0, 3, 81 + side)
    d = smax(d, ((P - wr) @ u) - 0.0, 0.004)
    return d


def shirt(P):
    d = arm(P, J["shoulderR"], J["elbowR"], J["wristR"], -1)
    d = np.minimum(d, arm(P, J["shoulderL"], J["elbowL"], J["wristL"], 1))
    chest = smax(torso(P) - 0.012, 1.62 - P[:, 2], 0.01)
    d = np.minimum(d, chest)
    # gola em pé e as duas pontas por cima do casaco
    neck_axis = capsule(P, J["neck"] - v3(0, 0, 0.05), J["head"], 0.0)
    collar = np.maximum(np.abs(neck_axis - 0.066) - 0.004, np.abs(P[:, 2] - 2.085) - 0.028)
    d = np.minimum(d, collar)
    for side in (-1, 1):
        top = J["neck"] + v3(side * 0.055, -0.06, 0.03)
        tip = J["neck"] + v3(side * 0.10, -0.15, -0.11)
        pts = [top + (tip - top) * t for t in np.linspace(0, 1, 6)]
        pts = on_surface(lambda Q: coat_solid(Q) - 0.009, pts, 0.0)
        widths = [0.03, 0.026, 0.021, 0.015, 0.009, 0.003]
        d = np.minimum(d, ribbon(P, pts, [v3(side * -1.0, 0.0, 0.5)] * 6, widths, 0.0025))
    return d


def shirt_color(V):
    c = SHIRT * (1.0 + 0.06 * fbm(V, 20.0, 3, 91))[:, None]
    fold = fbm(V * v3(1, 1, 0.4), 35.0, 3, 80)
    c = c * (1.0 - 0.15 * np.clip(-fold, 0, 1))[:, None]
    c = mix(c, SHIRT * 0.7, smooth01(0.2, 0.5, fbm(V, 7.0, 2, 92)) * 0.5)
    c = mix(c, np.array((0.42, 0.38, 0.22)), smooth01(0.1, 0.45, fbm(V, 4.0, 3, 93)) * 0.55)   # suor e encardido
    for wr in (J["wristR"], J["wristL"]):
        near = smooth01(0.16, 0.03, length(V - wr))
        c = mix(c, BLOOD_DRY, near * smooth01(-0.1, 0.35, fbm(V, 30.0, 3, 94)))
    c = mix(c, BLOOD_DRY, (fbm(V, 70.0, 2, 95) > 0.63) * 0.7)                                 # respingos
    return np.clip(c, 0, 1)


def sash(P):
    t = coat_solid(P) - 0.0062
    band = np.maximum(np.abs(t - 0.014) - 0.009 - 0.004 * fbm(P, 30.0, 2, 101),
                      np.abs(P[:, 2] - 1.33 - 0.04 * (P[:, 0] - 0.1)) - 0.045)
    knot = ellipsoid(P, (0.178, -0.115, 1.31), (0.036, 0.03, 0.034), rot(z=30))
    d = smin(band, knot, 0.01)
    e1 = catmull([(0.18, -0.125, 1.29), (0.2, -0.15, 1.15), (0.21, -0.165, 1.0), (0.205, -0.17, 0.84)], 4)
    e2 = catmull([(0.2, -0.1, 1.29), (0.235, -0.115, 1.12), (0.25, -0.118, 0.96)], 4)
    d = np.minimum(d, ribbon(P, e1, v3(1.0, -0.35, 0.0), 0.024, 0.003))
    d = np.minimum(d, ribbon(P, e2, v3(1.0, 0.2, 0.0), 0.021, 0.003))
    return d


def sash_color(V):
    c = SASH * (1.0 + 0.1 * fbm(V * v3(1, 1, 3), 25.0, 3, 111))[:, None]
    c = mix(c, SASH * 0.55, smooth01(0.9, 0.84, V[:, 2]))   # pontas encardidas
    return np.clip(c, 0, 1)


def belt(P):
    t = coat_solid(P) - 0.0062
    return np.maximum(np.abs(t - 0.008) - 0.0055, np.abs(P[:, 2] - 1.43) - 0.031)


def belt_color(V):
    return np.clip(np.array((0.06, 0.05, 0.045)) * (1 + 0.3 * fbm(V, 80.0, 3, 121))[:, None], 0, 1)


def pants(P):
    def leg(hip, knee, end, r_end):
        d = capsule(P, hip, knee, 0.087, 0.068)
        d = smin(d, capsule(P, knee, end, 0.066, r_end), 0.02)
        return d - 0.005 * fbm(P * v3(1, 1, 0.3), 25.0, 3, 131)
    shin_dir = norm(J["ankleR"] - J["kneeR"])
    d = leg(J["hipR"], J["kneeR"], J["kneeR"] + shin_dir * 0.32, 0.056)
    left_end = J["kneeL"] + v3(0.003, 0.0, -0.06)
    d = np.minimum(d, leg(J["hipL"], J["kneeL"], left_end, 0.07))
    d = smin(d, ellipsoid(P, left_end + v3(0, 0, -0.005), (0.078, 0.075, 0.028)), 0.01)   # barra enrolada
    d = smin(d, ellipsoid(P, (0.0, 0.02, 1.27), (0.17, 0.105, 0.11)), 0.05)
    return d


def pants_color(V):
    c = PANTS * (1.0 + 0.12 * fbm(V, 25.0, 3, 141))[:, None]
    c = mix(c, np.array((0.25, 0.22, 0.18)), smooth01(0.3, 0.6, fbm(V, 6.0, 2, 142)) * 0.35)  # poeira
    return np.clip(c, 0, 1)


def boot(P):
    a = J["ankleR"]
    up = norm(J["kneeR"] - J["ankleR"])
    d = capsule(P, a, a + up * 0.30, 0.062, 0.068)
    d = smin(d, capsule(P, a + up * 0.29, a + up * 0.36, 0.077, 0.079), 0.006)   # cano dobrado
    d = smax(d, (P - a) @ up - 0.37, 0.004)
    foot = box(P, a + v3(-0.006, -0.085, -0.045), (0.054, 0.135, 0.05), 0.034, rot(z=-6))
    foot = smin(foot, box(P, a + v3(-0.006, 0.02, -0.075), (0.045, 0.045, 0.024), 0.008), 0.01)   # salto
    d = smin(d, foot, 0.03)
    d = smax(d, -P[:, 2], 0.002)
    return d


def boot_color(V):
    c = LEATHER * (1.0 + 0.15 * fbm(V, 30.0, 3, 151))[:, None]
    c = mix(c, np.array((0.08, 0.06, 0.05)), smooth01(0.03, 0.02, V[:, 2]))
    c = mix(c, LEATHER * 1.6, smooth01(0.3, 0.6, fbm(V, 40.0, 2, 152)) * 0.3)  # couro gasto
    return np.clip(c, 0, 1)


def peg(P):
    k = J["kneeL"]
    tip = J["pegTip"]
    top = k + v3(0.0, 0.0, -0.08)
    d = ellipsoid(P, k + v3(0.0, 0.0, -0.1), (0.07, 0.07, 0.07))                 # concha de couro
    d = smin(d, capsule(P, top - v3(0, 0, 0.04), tip + v3(0, 0, 0.03), 0.047, 0.026), 0.02)
    axis = norm(tip - top)
    t = (P - top) @ axis
    d = d - 0.004 * np.clip(np.sin(t * 90.0), 0, 1) * (t > 0.06) * (t < 0.18)        # torneado
    d = smin(d, capsule(P, tip + axis * -0.12, tip + axis * -0.10, 0.034), 0.004)   # anel de ferro
    return d


def peg_color(V):
    k = J["kneeL"]
    axis = norm(J["pegTip"] - (k + v3(0, 0, -0.08)))
    t = (V - (k + v3(0, 0, -0.08))) @ axis
    grain = np.sin(V[:, 0] * 220 + V[:, 1] * 140 + 6 * fbm(V * v3(1, 1, 0.1), 15.0, 3, 161))
    c = WOOD * (1.0 + 0.12 * grain + 0.1 * fbm(V, 30.0, 2, 162))[:, None]
    c = mix(c, np.array((0.18, 0.11, 0.07)), smooth01(-0.02, -0.05, t))            # couro
    strap = (np.abs(t + 0.075) < 0.012)
    c[strap] = np.array((0.09, 0.07, 0.06))
    iron = (t > (np.linalg.norm(J["pegTip"] - (k + v3(0, 0, -0.08))) - 0.125)) & (t < (np.linalg.norm(J["pegTip"] - (k + v3(0, 0, -0.08))) - 0.095))
    c[iron] = np.array((0.16, 0.16, 0.17))
    c = mix(c, WOOD * 0.5, smooth01(0.1, 0.03, V[:, 2]))
    return np.clip(c, 0, 1)


# ------------------------------------------------------------------ mãos

def hand_frame(el, wr, side):
    u = norm(wr - el)
    hint = v3(-side, 0.0, 0.0)   # polegar para dentro, palma para baixo
    s = norm(hint - u * float(np.dot(hint, u)))
    n = np.cross(u, s)
    back = n if n[2] > 0 else -n
    R = np.stack((u, s, back), axis=1)   # a: dedos, t: polegar, k: costas da mão
    return Frame(wr - u * 0.02, R, 1.3)


def hand_local(q):
    d = box(q, (0.052, 0.0, 0.0), (0.05, 0.043, 0.0145), 0.013)
    d = smin(d, capsule(q, (-0.05, 0.0, 0.0), (0.012, 0.0, 0.0), 0.026), 0.015)
    d = smin(d, ellipsoid(q, (0.035, 0.03, -0.012), (0.03, 0.02, 0.014)), 0.012)  # monte do polegar
    fingers = ((0.10, 0.028, 0.115, 13.0), (0.103, 0.009, 0.13, 3.0), (0.10, -0.01, 0.121, -7.0), (0.093, -0.027, 0.097, -17.0))
    for a0, t0, L, spread in fingers:
        ang = math.radians(spread)
        dirv = v3(math.cos(ang), math.sin(ang), 0.0)
        p0 = v3(a0, t0, 0.002)
        lens = (0.45, 0.3, 0.25)
        pts = [p0]
        bend = (-0.12, -0.42, -0.5)   # curvados para a palma, como garras
        cur = dirv
        for k, f in enumerate(lens):
            cur = norm(cur + v3(0, 0, bend[k]))
            pts.append(pts[-1] + cur * L * f)
        radii = [0.0092, 0.0082, 0.0072, 0.006]
        d = smin(d, polyline(q, pts, radii, 0.002), 0.008)
        nail = [pts[-1] - cur * 0.012 + v3(0, 0, 0.004), pts[-1] + cur * 0.004 + v3(0, 0, 0.003),
                pts[-1] + norm(cur + v3(0, 0, -0.6)) * 0.02]
        d = smin(d, polyline(q, nail, [0.0058, 0.0045, 0.0008]), 0.002)
        for k in (1, 2):
            d = smin(d, sphere(q, pts[k] + v3(0, 0, 0.002), radii[k] + 0.0012), 0.003)
        d = smin(d, sphere(q, p0 + v3(-0.004, 0, 0.006), 0.0105), 0.006)   # nó do dedo
    tdir = norm(v3(math.cos(math.radians(55)), math.sin(math.radians(55)), -0.35))
    t0 = v3(0.022, 0.034, -0.008)
    t1 = t0 + tdir * 0.05
    t2 = t1 + norm(tdir + v3(0.25, 0, 0.05)) * 0.042
    d = smin(d, polyline(q, [t0, t1, t2], [0.013, 0.0105, 0.0082], 0.003), 0.01)
    # tendões nas costas da mão
    for t in (0.024, 0.008, -0.009, -0.025):
        d = smin(d, capsule(q, (0.015, t * 0.6, 0.012), (0.095, t, 0.012), 0.0032), 0.006)
    return d


def hand_color_fn(frame):
    def paint(V):
        q = frame.local(V)
        c = SKIN * (1.0 + 0.08 * fbm(q, 60.0, 3, 171))[:, None]
        c = mix(c, np.array((0.48, 0.33, 0.26)), smooth01(-0.004, -0.012, q[:, 2]) * 0.7)   # palma clara
        c = mix(c, np.array((0.25, 0.23, 0.2)), smooth01(-0.1, 0.4, fbm(q, 28.0, 3, 172)) * 0.4)
        tip = q[:, 0] > 0.13
        c = mix(c, np.array((0.2, 0.17, 0.11)), tip * smooth01(0.13, 0.17, q[:, 0]) * 0.85)    # unhas e pontas sujas
        c = mix(c, BLOOD_DRY, smooth01(0.0, 0.4, fbm(q, 45.0, 3, 173)) * 0.7)                 # sangue seco
        c = mix(c, c * 0.6, smooth01(0.3, 0.6, np.abs(np.sin(q[:, 0] * 260))) * (q[:, 2] > 0.005) * 0.25)
        return np.clip(c, 0, 1)
    return paint


# ------------------------------------------------------------------ montagem

def bounds_local(frame, lo, hi):
    corners = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
    W = frame.world(corners)
    return W.min(0), W.max(0)


def build():
    global CREASES, BROWS, MOUSTACHE, COCKADE
    pb.clear_scene()
    if not EXPORT:
        pb.stage(dim=0.65)
    CREASES = _crease_lines()
    BROWS, MOUSTACHE = _hair_lines()
    COCKADE = _cockade_place()

    m_skin = pb.material("Capitao_Pele", rough=0.6, spec=0.4, sss=0.08, bump=0.5, bump_scale=1100.0)
    m_hair = pb.material("Capitao_Cabelo", rough=0.62, spec=0.35)
    m_eye = pb.material("Capitao_Olho", rough=0.06, spec=0.8, coat=0.6)
    m_tooth = pb.material("Capitao_Dente", rough=0.35)
    m_hat = pb.material("Capitao_Chapeu", rough=0.95, sheen=0.15, bump=0.4, bump_scale=600.0)
    m_ribbon = pb.material("Capitao_Fita", rough=0.8, base=(0.86, 0.84, 0.78))
    objs = []

    lo, hi = bounds_local(HEAD, (-0.11, -0.14, -0.34), (0.11, 0.13, 0.135))
    objs.append(pb.sdf_mesh("Capitao_Cabeca", HEAD.wrap(face), lo, hi, H_FACE, skin_color, m_skin))
    lo, hi = bounds_local(HEAD, (-0.105, -0.125, -0.19), (0.105, 0.125, 0.07))
    objs.append(pb.sdf_mesh("Capitao_Barba", HEAD.wrap(hair_sdf), lo, hi, H_FACE * 0.9, hair_color, m_hair))
    lo, hi = bounds_local(HEAD, (-0.03, -0.1, -0.105), (0.03, -0.055, -0.068))
    objs.append(pb.sdf_mesh("Capitao_Dentes", HEAD.wrap(teeth), lo, hi, 0.0009, teeth_color, m_tooth, smooth=1))
    lo, hi = bounds_local(HEAD, (-0.21, -0.24, -0.02), (0.21, 0.22, 0.2))
    objs.append(pb.sdf_mesh("Capitao_Chapeu", HEAD.wrap(hat_sdf_local), lo, hi, 0.0022, hat_color, m_hat))
    lo, hi = bounds_local(HEAD, (-0.02, -0.2, -0.05), (0.24, 0.1, 0.18))
    objs.append(pb.sdf_mesh("Capitao_Fita", HEAD.wrap(ribbon_sdf_local), lo, hi, 0.0012, None, m_ribbon))

    # olhos: o direito encara a câmera; o esquerdo, caído, olha para fora
    target = v3(0.0, -2.5, 1.65)
    for name, c, seed, out in (("Capitao_OlhoD", EYE_R, 3, 0.0), ("Capitao_OlhoE", EYE_L, 5, 0.45)):
        center = HEAD.world(c)
        gaze_w = norm(norm(target - center) + HEAD.r[:, 0] * out)
        gaze_l = HEAD.r.T @ gaze_w
        objs.append(pb.sphere_mesh(name, center, EYE_RADIUS * S, 48, 64,
                                   eye_color(gaze_l, seed), m_eye, R=HEAD.r))

    for side, el, wr in ((-1, J["elbowR"], J["wristR"]), (1, J["elbowL"], J["wristL"])):
        fr = HANDS[side] if EXPORT else hand_frame(el, wr, side)
        lo, hi = bounds_local(fr, (-0.06, -0.08, -0.11), (0.27, 0.12, 0.06))
        objs.append(pb.sdf_mesh("Capitao_Mao" + ("D" if side < 0 else "E"), fr.wrap(hand_local), lo, hi,
                                0.0017, hand_color_fn(fr), m_skin))

    if not ONLY_FACE:
        m_coat = pb.material("Capitao_Casaco", rough=0.95, sheen=0.1, bump=0.35, bump_scale=420.0)
        m_shirt = pb.material("Capitao_Camisa", rough=0.9, sheen=0.1, bump=0.3, bump_scale=500.0)
        m_sash = pb.material("Capitao_Faixa", rough=0.85, sheen=0.15, bump=0.3, bump_scale=300.0)
        m_belt = pb.material("Capitao_Cinto", rough=0.45)
        m_brass = pb.material("Capitao_Latao", rough=0.35, metal=1.0)
        m_pants = pb.material("Capitao_Calca", rough=0.95, bump=0.3, bump_scale=400.0)
        m_boot = pb.material("Capitao_Bota", rough=0.5)
        m_wood = pb.material("Capitao_Madeira", rough=0.7)
        objs.append(pb.sdf_mesh("Capitao_Casaco", coat, (-0.42, -0.4, 0.70), (0.47, 0.36, 2.17), H_BODY, coat_color, m_coat))
        objs.append(pb.sdf_mesh("Capitao_Camisa", shirt, (-0.7, -0.9, 1.2), (0.7, 0.2, 2.25), H_BODY, shirt_color, m_shirt))
        objs.append(pb.sdf_mesh("Capitao_Faixa", sash, (-0.3, -0.27, 0.8), (0.34, 0.28, 1.42), 0.0032, sash_color, m_sash))
        objs.append(pb.sdf_mesh("Capitao_Cinto", belt, (-0.3, -0.27, 1.38), (0.34, 0.28, 1.48), 0.0032, belt_color, m_belt))
        objs.append(pb.sdf_mesh("Capitao_Latao", buttons, (-0.05, -0.26, 1.38), (0.12, -0.08, 1.98), 0.0016, brass_color, m_brass))
        objs.append(pb.sdf_mesh("Capitao_Calca", pants, (-0.28, -0.2, 0.3), (0.3, 0.18, 1.42), H_BODY, pants_color, m_pants))
        objs.append(pb.sdf_mesh("Capitao_Bota", boot, (-0.25, -0.3, 0.0), (-0.04, 0.12, 0.5), 0.0032, boot_color, m_boot))
        objs.append(pb.sdf_mesh("Capitao_PernaDePau", peg, (0.03, -0.14, -0.01), (0.27, 0.1, 0.66), 0.003, peg_color, m_wood))

    objs = [o for o in objs if o is not None]
    print(f"[personagem] Capitão: {pb.triangles(objs)} triângulos (antes de reduzir)")
    if EXPORT:
        from mathutils import Matrix
        M = np.eye(4)
        M[:3, :3] = TORSO.r
        M[:3, 3] = TORSO.o
        for o in objs:
            o.matrix_world = Matrix(M.tolist())
    return objs


# ------------------------------------------------------------------ exportação para o jogo

BONES = ["Capitao", "Bacia", "Coluna", "Pescoco", "Cabeca", "Coxa_D", "Coxa_E", "Joelho_D", "Joelho_E",
         "Pe_D", "Braco_D", "Braco_E", "Antebraco_D", "Antebraco_E", "Mao_D", "Mao_E"]


def export_game(objs):
    import personagem_export as pe
    by = {o.name: o for o in objs}
    B = SK.pos

    def seg(bone, a, b):
        return (bone, B(a) if isinstance(a, str) else a, B(b) if isinstance(b, str) else b)

    toe = B("Pe_D") + pb.norm(-SK.axis("Pe_D", "z") * -1.0) * 0.25
    hand_end = {t: B("Mao_" + t) - SK.axis("Mao_" + t, "y") * 0.28 for t in ("D", "E")}
    spine = [seg("Bacia", "Bacia", "Coluna"), seg("Coluna", "Coluna", "Pescoco")]
    legs = [seg("Coxa_D", "Coxa_D", "Joelho_D"), seg("Coxa_E", "Coxa_E", "Joelho_E"),
            seg("Joelho_D", "Joelho_D", "Pe_D"), seg("Joelho_E", "Joelho_E", "Perna_De_Pau_Ponta")]
    arms = [seg("Braco_" + t, "Braco_" + t, "Antebraco_" + t) for t in ("D", "E")] + \
           [seg("Antebraco_" + t, "Antebraco_" + t, "Mao_" + t) for t in ("D", "E")] + \
           [seg("Mao_" + t, "Mao_" + t, hand_end[t]) for t in ("D", "E")]

    def head_local(V):
        return HEAD.local(TORSO.local(V))

    neck = pe.chain([seg("Coluna", "Coluna", "Pescoco"), seg("Pescoco", "Pescoco", "Cabeca")])
    skin_head = pe.blend(neck, pe.rigid("Cabeca"), lambda V: smooth01(-0.21, -0.14, head_local(V)[:, 2]))
    head = pe.rigid("Cabeca")
    coat_w = pe.chain(spine + legs[:2], caps={"Coxa_D": 0.45, "Coxa_E": 0.45})
    shirt_w = pe.chain([spine[1]] + arms)
    waist = pe.chain(spine + legs[:2], caps={"Coxa_D": 0.3, "Coxa_E": 0.3})
    pants_w = pe.chain([spine[0]] + legs)
    boot_w = pe.chain([seg("Joelho_D", "Joelho_D", "Pe_D"), seg("Pe_D", "Pe_D", toe)])
    peg_w = pe.chain(legs[1::2][:1] + [legs[3]])
    hand_w = {t: pe.chain([seg("Antebraco_" + t, "Antebraco_" + t, "Mao_" + t), seg("Mao_" + t, "Mao_" + t, hand_end[t])])
              for t in ("D", "E")}

    pe.ensure_color(by["Capitao_Fita"], (0.86, 0.84, 0.78))
    P = pe.Part
    parts = [
        P(by["Capitao_Cabeca"], "Pele", 9000, skin_head, 2.6),
        P(by["Capitao_Barba"], "Cabelo", 3000, head, 1.6),
        P(by["Capitao_Dentes"], "Dente", 1200, head, 1.8),
        P(by["Capitao_OlhoD"], "Olho", 500, head, 2.2),
        P(by["Capitao_OlhoE"], "Olho", 500, head, 2.2),
        P(by["Capitao_Chapeu"], "Tecido", 3500, head, 1.2),
        P(by["Capitao_Fita"], "Tecido", 500, head),
        P(by["Capitao_MaoD"], "Pele", 2500, hand_w["D"], 1.6),
        P(by["Capitao_MaoE"], "Pele", 2500, hand_w["E"], 1.6),
        P(by["Capitao_Casaco"], "Tecido", 5000, coat_w),
        P(by["Capitao_Camisa"], "Tecido", 4500, shirt_w),
        P(by["Capitao_Faixa"], "Tecido", 1500, waist),
        P(by["Capitao_Cinto"], "Couro", 500, waist),
        P(by["Capitao_Latao"], "Metal", 800, waist),
        P(by["Capitao_Calca"], "Tecido", 2500, pants_w),
        P(by["Capitao_Bota"], "Couro", 1200, boot_w),
        P(by["Capitao_PernaDePau"], "Madeira", 1000, peg_w),
    ]
    pe.export(parts, BONES, "Capitao")


def photos():
    out = pb.PHOTO_DIR
    face_c = HEAD.world(v3(0.0, -0.06, -0.01))
    pb.shoot(os.path.join(out, "capitao_retrato.png"), face_c + v3(0.05, -1.5, -0.05), face_c + v3(0, 0, 0.06), 85, (900, 900))
    pb.shoot(os.path.join(out, "capitao_rosto.png"), face_c + v3(0.05, -0.85, -0.02), face_c, 85, (900, 900))
    pb.shoot(os.path.join(out, "capitao_rosto_34.png"), face_c + v3(0.8, -1.0, 0.0), face_c + v3(0, 0, 0.04), 85, (900, 900))
    pb.shoot(os.path.join(out, "capitao_rosto_de_baixo.png"), v3(0.1, -1.1, 1.62), face_c + v3(0, 0, 0.05), 60, (900, 900))
    pb.shoot(os.path.join(out, "capitao_rosto_lado.png"), face_c + v3(-0.85, -0.12, 0.0), face_c, 85, (900, 900))
    if not ONLY_FACE:
        mid = v3(0.03, 0.0, 1.35)
        pb.shoot(os.path.join(out, "capitao_corpo.png"), v3(0.2, -4.4, 1.6), mid, 40, (900, 1200))
        pb.shoot(os.path.join(out, "capitao_corpo_34.png"), v3(2.6, -3.4, 1.7), mid, 40, (900, 1200))
        pb.shoot(os.path.join(out, "capitao_costas.png"), v3(-1.6, 3.0, 1.7), mid, 40, (900, 1200))
        pb.shoot(os.path.join(out, "capitao_jogador.png"), v3(-0.6, -2.4, 1.65), v3(0.02, 0.0, 1.75), 28, (1280, 720))


def main():
    if EXPORT:
        load_skeleton()
        export_game(build())
        return
    build()
    photos()


pb.run(main)
