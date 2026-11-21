"""O Undertow, o inimigo da água funda das Piscinas (Level 2): um afogado que a água esticou.

Desenho original (Docs/historico.md, rodada do 'afogado'): 3,4 m da cabeça à ponta da cauda,
nadando de bruços. Tronco de gente, magro, de costelas, escápulas e vértebras marcadas; no lugar
das pernas, uma cauda comprida de vértebras que termina numa nadadeira de pele rasgada; braços
compridos demais (1,5 m), de dedos longos com pele entre eles e unhas pretas. Cabeça careca,
inchada de água, de olhos leitosos fundos, sem nariz (dois furos) e boca sem lábios, cheia de
dentes finos. Costas escuras, manchadas de lodo (de cima, através da água, uma sombra comprida
de braços abertos); barriga, rosto e mãos brancos e inchados, de veias azuladas e hematomas (de
baixo, contra a claraboia, uma silhueta clara).

    blender -b --factory-startup -P Tools/Blender/personagens/afogado.py [-- --exportar]

Sem --exportar: fotos em Logs/Blender/personagens/afogado_*.png.
--exportar: esculpe sobre o esqueleto do jogo (esqueleto_afogado.json, gravado pelo Unity em
    BackRooms > Personagens > Exportar esqueletos), na pose de encaixe do UndertowBody, e grava
    o modelo em Assets/_Game/Art/Personagens/Afogado (ver personagem_export.py).
Sem o esqueleto gravado, usa as mesmas medidas do UndertowBody, calculadas aqui.
"""

import math
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import personagem_base as pb  # noqa: E402
from personagem_base import (v3, Frame, sphere, ellipsoid, capsule, smin, smax,  # noqa: E402
                             polyline, fbm, vnoise, length, norm)

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
EXPORT = "--exportar" in ARGS
RNG = np.random.default_rng(1313)

# ------------------------------------------------------------------ esqueleto


def unity(x, y, z):
    """Do Unity (Y para cima, frente +Z, direita +X) para o Blender (Z para cima, frente -Y)."""
    return v3(-x, -z, y)


def computed_skeleton():
    """As juntas do UndertowBody na pose de encaixe, pelas mesmas medidas do C#."""
    J = {}
    J["Afogado"] = unity(0, 0, 0)
    J["Quadril"] = unity(0, 0, -0.30)
    J["Lombo"] = unity(0, 0, 0.08)
    J["Peito"] = unity(0, 0, 0.48)
    J["Ombros"] = unity(0, 0, 0.78)
    J["Pescoco"] = unity(0, 0.02, 0.92)
    J["Cabeca"] = unity(0, 0.04, 1.10)
    J["Mandibula"] = unity(0, -0.03, 1.14)
    z = -0.30
    for i, seg in enumerate((0.16, 0.32, 0.38, 0.38)):
        z -= seg
        J["Cauda_%d" % (i + 1)] = unity(0, 0, z)
    for tag, side in (("E", 1.0), ("D", -1.0)):
        yaw, pitch = math.radians(65.0 * side), math.radians(10.0)
        d = np.array((math.cos(pitch) * math.sin(yaw), -math.sin(pitch), math.cos(pitch) * math.cos(yaw)))
        s = np.array((0.26 * side, -0.02, 0.74))
        a = s + d * 0.62
        m = a + d * 0.56
        f = m + d * 0.13 + np.array((0, -0.01, 0))
        J["Braco_" + tag] = unity(*s)
        J["Antebraco_" + tag] = unity(*a)
        J["Mao_" + tag] = unity(*m)
        J["Dedos_" + tag] = unity(*f)
    return J


def load_joints():
    path = os.path.join(os.path.dirname(__file__), "esqueleto_afogado.json")
    if os.path.exists(path):
        import personagem_export as pe
        sk = pe.Skeleton("afogado")
        J = {name: sk.pos(name) for name in sk.joints}
        print("[personagem] esqueleto do Unity: esqueleto_afogado.json")
        return J
    print("[personagem] sem esqueleto_afogado.json: medidas calculadas aqui")
    return computed_skeleton()


J = load_joints()
FWD = norm(J["Cabeca"] - J["Ombros"]) * np.array((1.0, 1.0, 0.0))
FWD = norm(FWD)                            # a frente (−Y no Blender)
TAIL_TIP = J["Cauda_4"] + norm(J["Cauda_4"] - J["Cauda_3"]) * 0.42
HEAD = Frame(J["Cabeca"], np.eye(3), 1.0)  # cabeça: frente −Y, cima Z
JAW = Frame(J["Mandibula"], np.eye(3), 1.0)


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def mix(a, b, w):
    w = np.clip(np.asarray(w, dtype=np.float64), 0, 1)[:, None]
    return a * (1 - w) + b * w


# ------------------------------------------------------------------ corpo: tronco e cauda

# Perfil ao longo do comprimento (Y do Blender: frente negativa), relativo ao Quadril.
Y0 = float(J["Quadril"][1])
KY = np.array((-1.28, -1.16, -1.04, -0.85, -0.65, -0.42, -0.22, 0.0, 0.2, 0.5, 0.85, 1.2, 1.5, 1.7)) + 0.30
W = np.array((0.055, 0.13, 0.235, 0.225, 0.205, 0.15, 0.135, 0.17, 0.15, 0.12, 0.09, 0.066, 0.05, 0.03))
H = np.array((0.055, 0.095, 0.115, 0.145, 0.145, 0.115, 0.1, 0.118, 0.105, 0.088, 0.07, 0.05, 0.036, 0.022))
ZC = np.array((0.02, 0.01, 0.0, -0.01, -0.015, -0.01, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0))
Z0 = float(J["Quadril"][2])


def profile(y):
    t = np.clip(y - Y0, KY[0], KY[-1])
    return np.interp(t, KY, W), np.interp(t, KY, H), np.interp(t, KY, ZC) + Z0, t


def trunk(P):
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    w, h, zc, t = profile(y)
    ex, ez = x / w, (z - zc) / h
    k = np.sqrt(ex * ex + ez * ez)
    d = (k - 1.0) * np.minimum(w, h) * 0.85
    over = np.maximum(np.maximum((y - Y0) - KY[-1], KY[0] - (y - Y0)), 0.0)
    d = np.where(over > 0, np.sqrt(np.maximum(d, 0) ** 2 + over ** 2), d)

    # Costelas nos lados do peito (magro: a pele afunda entre elas).
    ribs = smooth(-1.0, -0.85, t) * smooth(-0.2, -0.38, t)
    side = smooth(0.35, 0.8, np.abs(ex)) * smooth(0.6, -0.2, ez)
    d += 0.008 * (0.5 + 0.5 * np.sin((t + 0.03) * 2 * math.pi / 0.085)) * ribs * side
    # Barriga funda abaixo das costelas.
    d += 0.012 * np.exp(-((t + 0.32) / 0.12) ** 2) * smooth(0.0, -0.6, ez)

    # Vértebras pela linha das costas, da nuca à ponta da cauda.
    step = 0.068
    yy = np.round((y - Y0) / step) * step + Y0
    wv, hv, zcv, tv = profile(yy)
    r = np.clip(0.017 * np.interp(tv, KY, H) / 0.12, 0.006, 0.018) * (1.0 + 0.25 * vnoise(np.stack((x * 0, yy, z * 0), 1), 9.0, 11))
    vert = np.sqrt(x ** 2 + (y - yy) ** 2 + (z - (zcv + hv * 0.88)) ** 2) - r
    vert = np.where((tv > -1.05) & (tv < 1.62), vert, 1.0)
    d = smin(d, vert, 0.03)

    # Escápulas e ossos do quadril saltados.
    for s in (-1, 1):
        sc = J["Ombros"] + FWD * -0.14 + v3(0.11 * s, 0, 0.075)
        d = smin(d, ellipsoid(P, sc, (0.07, 0.1, 0.02), pb.rot(z=s * 12)), 0.07)
        hc = J["Quadril"] + v3(0.13 * s, 0, 0.055)
        d = smin(d, ellipsoid(P, hc, (0.045, 0.07, 0.035)), 0.035)

    # Nadadeira de pele na ponta da cauda, rasgada.
    fin = ellipsoid(P, TAIL_TIP - FWD * 0.05, (0.18, 0.21, 0.016))
    rag = 0.03 * (0.5 + 0.5 * vnoise(P, 18.0, 5)) * smooth(0.08, 0.2, np.abs(x))
    d = smin(d, fin + rag, 0.04)

    # Pescoço até a base do crânio.
    d = smin(d, capsule(P, J["Ombros"] + FWD * -0.05, HEAD.world(v3(0, 0.06, -0.01)), 0.07, 0.058), 0.05)

    # Pele encharcada: rugas e caroços.
    d -= 0.004 * fbm(P, 11.0, 3, 1) + 0.0015 * fbm(P, 46.0, 2, 2)
    return d


# ------------------------------------------------------------------ braços e mãos

def arm_axes(tag):
    s, a, m, f = J["Braco_" + tag], J["Antebraco_" + tag], J["Mao_" + tag], J["Dedos_" + tag]
    u = norm(f - m)
    lat = norm(np.cross(u, v3(0, 0, 1)))
    down = np.cross(u, lat)
    if down[2] > 0:
        down = -down
    return s, a, m, f, u, lat, down


FINGERS = {}


def finger_paths(tag):
    if tag in FINGERS:
        return FINGERS[tag]
    s, a, m, f, u, lat, down = arm_axes(tag)
    paths = []
    for k in range(4):
        off = (k - 1.5) * 0.027
        L = (0.2, 0.25, 0.26, 0.21)[k]
        base = f + lat * off
        spread = lat * off * 0.9
        p1 = base + u * L * 0.38 + spread * 0.5 + down * 0.006
        p2 = p1 + norm(u + spread * 2 + down * 0.25) * L * 0.34
        p3 = p2 + norm(u + spread * 2.5 + down * 0.7) * L * 0.28
        paths.append((base - u * 0.02, p1, p2, p3))
    # Polegar, do lado de dentro da palma.
    inner = 1.0 if np.dot(lat, -J["Braco_" + tag] * np.array((1.0, 0.0, 0.0))) > 0 else -1.0
    t0 = m + u * 0.05 + lat * inner * 0.045
    t1 = t0 + norm(u + lat * inner * 0.8 + down * 0.2) * 0.07
    t2 = t1 + norm(u + lat * inner * 0.3 + down * 0.5) * 0.06
    paths.append((t0, t1, t2))
    FINGERS[tag] = paths
    return paths


def arm(P, tag):
    s, a, m, f, u, lat, down = arm_axes(tag)
    d = sphere(P, s - u * 0.03, 0.06)
    d = smin(d, capsule(P, s, a, 0.056, 0.043), 0.04)
    d = smin(d, ellipsoid(P, a, (0.042, 0.042, 0.05)), 0.04)      # cotovelo ossudo
    d = smin(d, capsule(P, a, m, 0.043, 0.029), 0.03)
    d = smin(d, ellipsoid(P, m + u * 0.02, (0.04, 0.04, 0.026)), 0.02)   # pulso
    # Palma comprida e chata.
    Rm = np.stack((lat, u, -down), axis=1)
    d = smin(d, ellipsoid(P, m + u * 0.075, (0.052, 0.085, 0.018), Rm), 0.03)
    # Pele entre os dedos.
    d = smin(d, ellipsoid(P, f + u * 0.05 + down * 0.004, (0.055, 0.075, 0.006), Rm), 0.01)
    for path in finger_paths(tag):
        radii = [0.0125, 0.011, 0.009, 0.005][:len(path)]
        d = smin(d, polyline(P, list(path), radii, 0.004), 0.012)
        for p in path[1:-1]:
            d = smin(d, sphere(P, p, 0.0125), 0.006)               # nós dos dedos
    d -= 0.003 * fbm(P, 14.0, 3, 3) + 0.0012 * fbm(P, 55.0, 2, 4)
    return d


def arm_bounds(tag):
    pts = [J[n + tag] for n in ("Braco_", "Antebraco_", "Mao_", "Dedos_")]
    for path in finger_paths(tag):
        pts.extend(path)
    A = np.array(pts)
    return A.min(0) - 0.1, A.max(0) + 0.1


# ------------------------------------------------------------------ cabeça

# No sistema da cabeça (origem na junta Cabeca): frente −Y, cima Z, esquerda +X.
EYES = [v3(s * 0.045, -0.176, 0.018) for s in (-1, 1)]


def skull(q):
    d = ellipsoid(q, v3(0, -0.06, 0.035), (0.108, 0.165, 0.1))
    # Crânio comprido para trás (afogado inchado, careca) e rosto estreito na frente.
    d = smin(d, ellipsoid(q, v3(0, 0.03, 0.05), (0.1, 0.13, 0.09)), 0.04)
    d = smax(d, -ellipsoid(q, v3(0, -0.29, -0.06), (0.16, 0.06, 0.07)), 0.05)
    # Inchado e torto: um lado mais cheio, caroços.
    d -= 0.012 * np.exp(-((q[:, 0] - 0.05) ** 2 / 0.004 + (q[:, 1] + 0.02) ** 2 / 0.01 + (q[:, 2] - 0.08) ** 2 / 0.004))
    d -= 0.008 * fbm(q, 7.0, 3, 6)
    # Arcada sobre os olhos e maçãs do rosto ossudas.
    d = smin(d, ellipsoid(q, v3(0, -0.198, 0.047), (0.095, 0.03, 0.022)), 0.03)
    for s in (-1, 1):
        d = smin(d, ellipsoid(q, v3(s * 0.075, -0.17, -0.02), (0.03, 0.045, 0.025)), 0.03)
        # Têmporas fundas.
        d = smax(d, -ellipsoid(q, v3(s * 0.112, -0.1, 0.02), (0.03, 0.06, 0.045)), 0.03)
        # Órbitas fundas.
        d = smax(d, -ellipsoid(q, EYES[(s + 1) // 2] + v3(0, -0.012, 0.002), (0.03, 0.026, 0.024)), 0.01)
        # Narinas: dois furos, sem nariz.
        d = smax(d, -ellipsoid(q, v3(s * 0.011, -0.232, -0.012), (0.006, 0.02, 0.01)), 0.004)
    # Boca sem lábios: fenda larga na frente, de canto a canto.
    mouth = ellipsoid(q, v3(0, -0.19, -0.055), (0.07, 0.07, 0.022))
    d = smax(d, -mouth, 0.008)
    # Corta o queixo: a mandíbula é outra peça.
    d = smax(d, -(q[:, 2] + 0.06), 0.02)
    d -= 0.002 * fbm(q, 40.0, 2, 7)
    return d


def jaw_local(q):
    # Mandíbula comprida e estreita, em U, aberta em cima.
    d = ellipsoid(q, v3(0, -0.08, -0.016), (0.062, 0.115, 0.03))
    d = smin(d, ellipsoid(q, v3(0, -0.165, -0.022), (0.022, 0.03, 0.022)), 0.025)   # queixo ossudo
    d -= 0.004 * fbm(q, 9.0, 2, 9)
    inner = ellipsoid(q, v3(0, -0.08, 0.012), (0.054, 0.105, 0.034))
    d = smax(d, -inner, 0.01)
    d -= 0.003 * fbm(q, 18.0, 2, 8)
    return d


def _teeth():
    specs = []
    for i, t in enumerate(np.linspace(0.04, 0.96, 15)):
        if RNG.random() < 0.22:
            continue                                   # faltando
        ang = (t - 0.5) * 2.5
        base = v3(math.sin(ang) * 0.062, -0.185 - math.cos(ang) * 0.03, -0.047)
        L = RNG.uniform(0.012, 0.034) * (1.6 if i in (3, 11) else 1.0)
        tilt = v3(RNG.uniform(-0.35, 0.35), RNG.uniform(-0.45, 0.15), -1.0)
        specs.append(("cima", base, norm(tilt), L, RNG.uniform(0.0025, 0.0045)))
    for i, t in enumerate(np.linspace(0.08, 0.92, 12)):
        if RNG.random() < 0.25:
            continue
        ang = (t - 0.5) * 2.4
        base = v3(math.sin(ang) * 0.052, -0.08 - math.cos(ang) * 0.1, 0.01)
        L = RNG.uniform(0.01, 0.028)
        specs.append(("baixo", base, norm(v3(RNG.uniform(-0.35, 0.35), RNG.uniform(-0.3, 0.3), 1.0)), L, RNG.uniform(0.0025, 0.004)))
    return specs


TEETH = _teeth()


def teeth(q, which):
    d = None
    for kind, base, direction, L, r in TEETH:
        if kind != which:
            continue
        di = polyline(q, [base - direction * 0.006, base + direction * L * 0.6, base + direction * L], [r, r * 0.55, 0.0006], 0.0015)
        d = di if d is None else np.minimum(d, di)
    return d


# ------------------------------------------------------------------ cores

PALE = np.array((0.6, 0.64, 0.57))
DARK = np.array((0.07, 0.085, 0.075))
SLIME = np.array((0.13, 0.17, 0.09))
VEIN = np.array((0.33, 0.38, 0.45))
BRUISE = np.array((0.36, 0.24, 0.32))


def skin(V, top):
    """Pele: `top` de 0 (barriga) a 1 (costas)."""
    c = np.tile(PALE, (len(V), 1)) * (1.0 + 0.12 * fbm(V, 6.0, 3, 21))[:, None]
    c = mix(c, VEIN, (np.abs(fbm(V, 7.5, 4, 22)) < 0.035) * 0.75 * (1 - top))
    c = mix(c, BRUISE, smooth(0.25, 0.5, fbm(V, 2.5, 3, 23)) * 0.55 * (1 - top))
    back = np.tile(DARK, (len(V), 1)) * (1.0 + 0.6 * fbm(V, 9.0, 3, 24))[:, None]
    back = mix(back, SLIME, smooth(0.1, 0.45, fbm(V, 3.5, 3, 25)))
    blotch = smooth(-0.15, 0.25, fbm(V, 4.0, 3, 26) + (top - 0.5) * 1.6)
    c = mix(c, back, blotch)
    return np.clip(c, 0, 1)


def trunk_color(V):
    w, h, zc, t = profile(V[:, 1])
    top = np.clip((V[:, 2] - zc) / h * 0.9 + 0.35, 0, 1)
    return skin(V, top)


def arm_color(tag):
    s, a, m, f, u, lat, down = arm_axes(tag)

    def color(V):
        # Em cima do braço mais escuro: pela altura relativa ao eixo do braço.
        axis_z = np.interp(np.dot(V - s, u), (0.0, 1.4), (s[2], f[2]))
        top = np.clip((V[:, 2] - axis_z) / 0.05 * 0.5 + 0.3, 0, 1) * smooth(1.25, 1.0, np.dot(V - s, u))
        c = skin(V, top * 0.85)
        # Unhas pretas nas pontas dos dedos.
        for path in finger_paths(tag):
            tip = np.asarray(path[-1])
            c = mix(c, np.array((0.04, 0.035, 0.03)), smooth(0.03, 0.012, length(V - tip)))
        return c
    return color


def head_color(V):
    q = HEAD.local(V)
    top = np.clip((q[:, 2] - 0.02) / 0.09 * 0.8 + 0.15 + 0.35 * smooth(-0.1, 0.05, q[:, 1]), 0, 1)
    c = skin(V, top * 0.9)
    for e in EYES:
        c = mix(c, np.array((0.12, 0.09, 0.09)), smooth(0.05, 0.03, length(q - e)))
    inside = ellipsoid(q, v3(0, -0.205, -0.055), (0.08, 0.065, 0.026)) < 0.004
    c[inside] = np.array((0.16, 0.04, 0.05))
    return np.clip(c, 0, 1)


def jaw_color(V):
    q = JAW.local(V)
    c = skin(V, np.zeros(len(V)))
    inner = ellipsoid(q, v3(0, -0.08, 0.012), (0.058, 0.11, 0.038)) < 0.004
    c[inner] = np.array((0.18, 0.05, 0.06))
    return np.clip(c, 0, 1)


def teeth_color(V):
    c = np.tile(np.array((0.5, 0.45, 0.33)), (len(V), 1)) * (1.0 + 0.2 * fbm(V, 80.0, 2, 31))[:, None]
    c = mix(c, np.array((0.2, 0.15, 0.08)), smooth(0.0, 0.4, fbm(V, 35.0, 2, 32)) * 0.8)
    return np.clip(c, 0, 1)


def eye_color(dirs):
    # Leitoso, sem íris clara: um disco cinzento apagado na frente (−Y).
    c = np.tile(np.array((0.5, 0.54, 0.5)), (len(dirs), 1))
    front = smooth(0.8, 0.95, -dirs[:, 1])
    c = mix(c, np.array((0.34, 0.37, 0.36)), front * 0.7)
    c = mix(c, np.array((0.55, 0.3, 0.3)), (np.abs(fbm(dirs * 3.0, 4.0, 2, 33)) < 0.04) * 0.6)
    return c


# ------------------------------------------------------------------ montagem

def build():
    pb.clear_scene()
    m_skin = pb.material("Afogado_Pele", rough=0.45, spec=0.5, sss=0.08, coat=0.12, bump=0.3, bump_scale=420.0)
    m_teeth = pb.material("Afogado_Dente", rough=0.4)
    m_eye = pb.material("Afogado_Olho", rough=0.12, spec=0.7, coat=0.8)
    objs = {}

    lo = np.array((-0.34, min(J["Cabeca"][1], J["Ombros"][1]) - 0.12, Z0 - 0.2))
    hi = np.array((0.34, TAIL_TIP[1] + 0.3, Z0 + 0.24))
    objs["Afogado_Corpo"] = pb.sdf_mesh("Afogado_Corpo", trunk, lo, hi, 0.0075 if EXPORT else 0.009, trunk_color, m_skin)
    for tag in ("E", "D"):
        a, b = arm_bounds(tag)
        objs["Afogado_Braco_" + tag] = pb.sdf_mesh("Afogado_Braco_" + tag, lambda P, t=tag: arm(P, t), a, b,
                                                    0.0042 if EXPORT else 0.005, arm_color(tag), m_skin)
    hlo, hhi = J["Cabeca"] + v3(-0.16, -0.3, -0.12), J["Cabeca"] + v3(0.16, 0.14, 0.17)
    objs["Afogado_Cabeca"] = pb.sdf_mesh("Afogado_Cabeca", HEAD.wrap(skull), hlo, hhi, 0.0026, head_color, m_skin)
    jlo, jhi = J["Mandibula"] + v3(-0.11, -0.24, -0.08), J["Mandibula"] + v3(0.11, 0.05, 0.02)
    objs["Afogado_Mandibula"] = pb.sdf_mesh("Afogado_Mandibula", JAW.wrap(jaw_local), jlo, jhi, 0.0024, jaw_color, m_skin)
    objs["Afogado_DentesCima"] = pb.sdf_mesh("Afogado_DentesCima", HEAD.wrap(lambda q: teeth(q, "cima")), hlo, hhi, 0.0016, teeth_color, m_teeth)
    objs["Afogado_DentesBaixo"] = pb.sdf_mesh("Afogado_DentesBaixo", JAW.wrap(lambda q: teeth(q, "baixo")), jlo, jhi, 0.0016, teeth_color, m_teeth)
    for i, e in enumerate(EYES):
        objs["Afogado_Olho_%d" % i] = pb.sphere_mesh("Afogado_Olho_%d" % i, HEAD.world(e), 0.0135, 24, 32, eye_color, m_eye)
    objs = {k: o for k, o in objs.items() if o is not None}
    print(f"[personagem] Afogado: {pb.triangles(objs.values())} triângulos (antes de reduzir)")
    return objs


def photos(objs):
    lift = 0.95
    for o in objs.values():
        o.location.z += lift
    pb.stage(wall=(0.55, 0.72, 0.76), floor=(0.24, 0.5, 0.6), ceiling=4.0, warm=False, dim=1.3)
    out = pb.PHOTO_DIR
    mid = v3(0.0, 0.3, Z0 + lift)
    head = J["Cabeca"] + v3(0, 0, lift)
    pb.shoot(os.path.join(out, "afogado_cima.png"), mid + v3(0.3, 0.2, 2.9), mid, 16, (1000, 1200))
    pb.shoot(os.path.join(out, "afogado_34.png"), mid + v3(2.8, -3.2, 1.9), mid, 30, (1200, 900))
    pb.shoot(os.path.join(out, "afogado_lado.png"), mid + v3(-4.4, 0.0, 0.5), mid, 28, (1400, 800))
    pb.shoot(os.path.join(out, "afogado_frente.png"), head + v3(0.25, -2.2, 0.15), head + v3(0, 0.3, -0.05), 35, (1200, 900))
    pb.shoot(os.path.join(out, "afogado_baixo.png"), mid + v3(0.6, -1.2, -0.85), mid + v3(0, -0.4, 0), 18, (1200, 900))
    pb.shoot(os.path.join(out, "afogado_rosto.png"), head + v3(0.18, -0.75, 0.02), head + v3(0, -0.15, -0.02), 50, (900, 900))
    hand = J["Mao_E"] + v3(0, 0, lift)
    pb.shoot(os.path.join(out, "afogado_mao.png"), hand + v3(-0.3, -0.5, 0.45), hand + norm(J["Dedos_E"] - J["Mao_E"]) * 0.12, 45, (900, 900))


# ------------------------------------------------------------------ exportação

BONES = ["Afogado", "Quadril", "Lombo", "Peito", "Ombros", "Pescoco", "Cabeca", "Mandibula",
         "Cauda_1", "Cauda_2", "Cauda_3", "Cauda_4",
         "Braco_E", "Braco_D", "Antebraco_E", "Antebraco_D", "Mao_E", "Mao_D", "Dedos_E", "Dedos_D"]


def export_game(objs):
    import personagem_export as pe

    def seg(bone, a, b):
        return (bone, J[a] if isinstance(a, str) else a, J[b] if isinstance(b, str) else b)

    head_end = HEAD.world(v3(0, -0.2, 0.02))
    body = [seg("Quadril", "Quadril", "Lombo"), seg("Lombo", "Lombo", "Peito"), seg("Peito", "Peito", "Ombros"),
            seg("Ombros", "Ombros", "Pescoco"), seg("Pescoco", "Pescoco", "Cabeca"), seg("Cabeca", "Cabeca", head_end),
            seg("Quadril", "Quadril", "Cauda_1"), seg("Cauda_1", "Cauda_1", "Cauda_2"), seg("Cauda_2", "Cauda_2", "Cauda_3"),
            seg("Cauda_3", "Cauda_3", "Cauda_4"), seg("Cauda_4", "Cauda_4", TAIL_TIP + norm(TAIL_TIP - J["Cauda_4"]) * 0.2)]
    weights = {"Afogado_Corpo": pe.chain(body, tau=0.05)}
    for tag in ("E", "D"):
        s, a, m, f, u, lat, down = arm_axes(tag)
        segs = [seg("Ombros", "Ombros", "Braco_" + tag), seg("Braco_" + tag, "Braco_" + tag, "Antebraco_" + tag),
                seg("Antebraco_" + tag, "Antebraco_" + tag, "Mao_" + tag), seg("Mao_" + tag, "Mao_" + tag, "Dedos_" + tag),
                seg("Dedos_" + tag, "Dedos_" + tag, f + u * 0.3)]
        weights["Afogado_Braco_" + tag] = pe.chain(segs, tau=0.03, caps={"Ombros": 0.6})
    weights["Afogado_Cabeca"] = pe.blend(pe.rigid("Cabeca"), pe.rigid("Pescoco"),
                                         lambda V: smooth(-0.02, 0.06, HEAD.local(V)[:, 1]) * 0.5)
    weights["Afogado_Mandibula"] = pe.rigid("Mandibula")
    weights["Afogado_DentesCima"] = pe.rigid("Cabeca")
    weights["Afogado_DentesBaixo"] = pe.rigid("Mandibula")
    weights["Afogado_Olho_0"] = pe.rigid("Cabeca")
    weights["Afogado_Olho_1"] = pe.rigid("Cabeca")

    plans = {"Afogado_Corpo": ("Encharcada", 15000, 1.0), "Afogado_Braco_E": ("Encharcada", 5200, 1.2),
             "Afogado_Braco_D": ("Encharcada", 5200, 1.2), "Afogado_Cabeca": ("Encharcada", 7000, 1.9),
             "Afogado_Mandibula": ("Encharcada", 1800, 1.6), "Afogado_DentesCima": ("Dente", 1600, 1.3),
             "Afogado_DentesBaixo": ("Dente", 1200, 1.3), "Afogado_Olho_0": ("Olho", 600, 1.5),
             "Afogado_Olho_1": ("Olho", 600, 1.5)}
    parts = []
    for name, fn in weights.items():
        if name not in objs:
            print(f"[personagem] AVISO: sem a peça {name}")
            continue
        slot, tris, texel = plans[name]
        parts.append(pe.Part(objs[name], slot, tris, fn, texel))
    pe.export(parts, BONES, "Afogado")


def main():
    objs = build()
    if EXPORT:
        export_game(objs)
        return
    photos(objs)


pb.run(main)
