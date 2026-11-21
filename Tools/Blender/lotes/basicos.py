"""
Lote "basicos": a primeira leva de móveis do Level 1 (Docs/level1-mostruario.md, seção 3.2).
Cadeira, mesa de jantar, sofá, poltrona, estante, guarda-roupa, estantes de aço, caixas e
placas de preço.

    blender -b --factory-startup -P Tools/Blender/lotes/basicos.py [-- --fotos]

As peças, a paleta comum e a exportação vêm de Tools/Blender/moveis_base.py.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from moveis_base import Part, euler_matrix, main_guard, run_lote  # noqa: E402
from mathutils import Vector  # noqa: E402


# --------------------------------------------------------------------------- Móveis
# Espaço do móvel: X para a direita, -Y é a frente, Z para cima, origem no chão no meio.

def cadeira(name="Cadeira", wood="Madeira_Escura", seed=1):
    """Cadeira de jantar de madeira: 0,45 x 0,92 x 0,47 m."""
    p = Part(name)
    w, d, seat = 0.44, 0.44, 0.46
    leg = 0.036
    for sx in (-1, 1):
        # Pernas da frente até o assento; as de trás sobem e viram o encosto.
        p.box_span((sx * w / 2 - leg, -d / 2, 0), (sx * w / 2, -d / 2 + leg, seat - 0.03), wood)
        p.box_span((sx * w / 2 - leg, d / 2 - leg, 0), (sx * w / 2, d / 2, 0.92), wood)
        # Travessa baixa de cada lado.
        p.box_span((sx * w / 2 - leg * 0.8, -d / 2 + leg, 0.14), (sx * w / 2 - leg * 0.2, d / 2 - leg, 0.17), wood)
    # Assento com a borda arredondada e o avental embaixo.
    p.box((0, 0, seat), (w + 0.012, d + 0.012, 0.035), wood, bevel=0.008)
    p.box((0, -d / 2 + 0.01, seat - 0.06), (w - 2 * leg, 0.018, 0.07), wood)
    p.box((0, d / 2 - 0.01, seat - 0.06), (w - 2 * leg, 0.018, 0.07), wood)
    for sx in (-1, 1):
        p.box((sx * (w / 2 - 0.01), 0, seat - 0.06), (0.018, d - 2 * leg, 0.07), wood)
    # Encosto: travessa de cima curva (três pedaços) e três ripas.
    p.box((0, d / 2 - leg / 2, 0.87), (w - 2 * leg, 0.03, 0.085), wood, bevel=0.006)
    p.box((0, d / 2 - leg / 2, 0.55), (w - 2 * leg, 0.025, 0.04), wood)
    for i in (-1, 0, 1):
        p.box((i * 0.11, d / 2 - leg / 2, 0.71), (0.045, 0.018, 0.30), wood)
    return p.to_object()


def mesa_jantar(name="MesaDeJantar"):
    """Mesa de jantar: tampo de 2,0 x 0,9 m a 0,76 m do chão, pernas torneadas."""
    p = Part(name)
    w, d, h = 2.0, 0.9, 0.76
    top = "Madeira_Clara"
    p.box((0, 0, h - 0.02), (w, d, 0.04), top, bevel=0.01)
    # Saia do tampo.
    inset = 0.08
    p.box((0, -(d / 2 - inset), h - 0.09), (w - 2 * inset, 0.022, 0.1), top)
    p.box((0, d / 2 - inset, h - 0.09), (w - 2 * inset, 0.022, 0.1), top)
    for sx in (-1, 1):
        p.box((sx * (w / 2 - inset), 0, h - 0.09), (0.022, d - 2 * inset, 0.1), top)
    # Pernas torneadas: bloco quadrado em cima e anéis embaixo.
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (w / 2 - inset - 0.01), sy * (d / 2 - inset - 0.01)
            p.box((x, y, h - 0.12), (0.07, 0.07, 0.16), top)
            p.cylinder((x, y, 0.0), 0.06, 0.026, top, radius_top=0.03)
            p.cylinder((x, y, 0.06), 0.30, 0.03, top, radius_top=0.024)
            p.cylinder((x, y, 0.36), 0.04, 0.036, top)
            p.cylinder((x, y, 0.40), 0.24, 0.026, top, radius_top=0.03)
    return p.to_object()


def estofado(name, seats, fabric):
    """Sofá (seats = 3) ou poltrona (seats = 1): base, almofadas, braços e pés de madeira."""
    p = Part(name)
    seat_w = 0.62
    arm = 0.17
    w = seats * seat_w + 2 * arm
    d = 0.88
    # Pés.
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.cylinder((sx * (w / 2 - 0.07), sy * (d / 2 - 0.08), 0), 0.10, 0.025, "Madeira_Escura", radius_top=0.03)
    # Base e encosto de trás.
    p.box((0, 0.02, 0.21), (w, d - 0.04, 0.22), fabric, bevel=0.03)
    p.box((0, d / 2 - 0.1, 0.55), (w, 0.2, 0.62), fabric, bevel=0.05)
    # Braços.
    for sx in (-1, 1):
        p.box((sx * (w / 2 - arm / 2), 0, 0.42), (arm, d, 0.42), fabric, bevel=0.06)
    # Almofadas do assento e das costas, uma por lugar, um pouco desiguais.
    rng = random.Random(seats * 31)
    for i in range(seats):
        x = (i - (seats - 1) / 2) * seat_w
        sag = rng.uniform(-0.012, 0.0)
        p.box((x, -0.04, 0.39 + sag), (seat_w - 0.012, d - 0.24, 0.15), fabric, bevel=0.05)
        p.box((x, d / 2 - 0.26, 0.64), (seat_w - 0.02, 0.18, 0.42), fabric, bevel=0.06,
              rot=(-12 + rng.uniform(-3, 3), 0, rng.uniform(-2, 2)))
    return p.to_object()


def estante_livros(name="Estante", seed=7):
    """Estante de madeira com livros: 1,0 x 2,0 x 0,35 m."""
    p = Part(name)
    w, h, d = 1.0, 2.0, 0.35
    t = 0.025
    wood = "Madeira_Escura"
    for sx in (-1, 1):
        # Laterais: a direita vai de w/2 - t a w/2; a esquerda, de -w/2 a -w/2 + t.
        x0 = w / 2 - t if sx > 0 else -w / 2
        p.box_span((x0, -d / 2, 0), (x0 + t, d / 2, h), wood)
    p.box((0, 0, h - t / 2), (w + 0.03, d + 0.02, t), wood, bevel=0.008)
    p.box((0, -d / 2 + 0.01, 0.05), (w - 2 * t, 0.02, 0.1), wood)
    p.box((0, d / 2 - 0.005, h / 2), (w - 2 * t, 0.01, h - 0.02), wood)
    shelves = [0.1, 0.5, 0.9, 1.3, 1.68]
    for z in shelves:
        p.box((0, 0, z), (w - 2 * t, d - 0.02, 0.022), wood)
    # Livros: fileiras com alturas e cores sorteadas, alguns tombados, lacunas.
    rng = random.Random(seed)
    colors = ["Livro_Verde", "Livro_Vinho", "Livro_Bege", "Livro_Azul"]
    for z in shelves:
        x = -w / 2 + t + 0.01
        limit = w / 2 - t - 0.01
        while x < limit - 0.03:
            if rng.random() < 0.12:
                x += rng.uniform(0.05, 0.15)
                continue
            bw = rng.uniform(0.022, 0.05)
            bh = rng.uniform(0.2, 0.33)
            bd = rng.uniform(0.17, 0.24)
            if x + bw > limit:
                break
            color = rng.choice(colors)
            if rng.random() < 0.07 and x + bh < limit:
                # Tombado, deitado na prateleira.
                p.box((x + bh / 2, -d / 2 + bd / 2 + 0.03, z + 0.011 + bw / 2), (bh, bd, bw), color, bevel=0.0)
                x += bh + 0.01
                continue
            lean = rng.uniform(-4, 4) if rng.random() < 0.2 else 0.0
            p.box((x + bw / 2, -d / 2 + bd / 2 + 0.03, z + 0.011 + bh / 2), (bw, bd, bh), color, bevel=0.0, rot=(0, lean, 0))
            x += bw + rng.uniform(0.0, 0.004)
    return p.to_object()


def guarda_roupa(name="GuardaRoupa"):
    """Guarda-roupa de duas portas: 1,2 x 2,1 x 0,6 m, oco por dentro (vira esconderijo).
    As portas são objetos filhos com a origem na dobradiça, para abrir girando em Z."""
    p = Part(name)
    w, h, d = 1.2, 2.1, 0.6
    t = 0.022
    wood = "Madeira_Escura"
    plinth = 0.08
    p.box((0, 0.01, plinth / 2), (w - 0.02, d - 0.04, plinth), wood)
    for sx in (-1, 1):
        p.box((sx * (w / 2 - t / 2), 0, plinth + (h - plinth) / 2), (t, d, h - plinth), wood)
    p.box((0, 0, plinth + t / 2), (w - 2 * t, d, t), wood)
    p.box((0, 0, h - t / 2), (w, d, t), wood)
    p.box((0, d / 2 - 0.006, plinth + (h - plinth) / 2), (w - 2 * t, 0.012, h - plinth), wood)
    # Prateleira alta e cabideiro de latão por dentro.
    p.box((0, 0.02, h - 0.35), (w - 2 * t, d - 0.06, t), wood)
    p.cylinder((-(w / 2 - t), 0.02, h - 0.45), w - 2 * t, 0.012, "Latao", rot=(0, 90, 0))
    # Cornija no alto.
    p.box((0, -0.01, h + 0.03), (w + 0.06, d + 0.04, 0.06), wood, bevel=0.012)
    body = p.to_object()

    door_w = (w - 2 * t) / 2 - 0.002
    door_h = h - plinth - 2 * t - 0.004
    for side, sx in (("D", 1), ("E", -1)):
        q = Part(name + "_Porta_" + side)
        hinge_x = sx * (w / 2 - t)
        cx = hinge_x - sx * door_w / 2
        zc = plinth + t + 0.002 + door_h / 2
        q.box((cx, -d / 2 + 0.012, zc), (door_w, 0.022, door_h), wood, bevel=0.006)
        # Almofada (moldura em relevo) e puxador perto do meio.
        q.box((cx, -d / 2 + 0.0, zc + 0.35), (door_w - 0.12, 0.008, door_h * 0.42), wood, bevel=0.004)
        q.box((cx, -d / 2 + 0.0, zc - 0.5), (door_w - 0.12, 0.008, door_h * 0.32), wood, bevel=0.004)
        q.cylinder((cx - sx * (door_w / 2 - 0.06), -d / 2 - 0.004, zc - 0.05), 0.12, 0.009, "Latao", rot=(0, 0, 0))
        q.to_object(parent=body, origin=(hinge_x, -d / 2 + 0.012, 0.0))
    return body


def estante_aco(name="EstanteDeAco", filled=False, seed=3):
    """Estante de aço de depósito: 1,8 x 2,2 x 0,5 m, cinco prateleiras."""
    p = Part(name)
    w, h, d = 1.8, 2.2, 0.5
    steel = "Metal_Pintado"
    post = 0.04
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (w / 2 - post / 2), sy * (d / 2 - post / 2)
            # Cantoneira em L: duas abas finas.
            p.box((x, y - sy * (post / 2 - 0.0015), h / 2), (post, 0.003, h), steel, bevel=0.0)
            p.box((x - sx * (post / 2 - 0.0015), y, h / 2), (0.003, post, h), steel, bevel=0.0)
            p.box((x, y, 0.005), (0.06, 0.06, 0.01), steel, bevel=0.0)
    levels = [0.12, 0.6, 1.08, 1.56, 2.04]
    for z in levels:
        p.box((0, 0, z), (w - 0.004, d - 0.004, 0.012), steel, bevel=0.0)
        # Abas dobradas da prateleira, na frente e atrás.
        for sy in (-1, 1):
            p.box((0, sy * (d / 2 - 0.004), z - 0.02), (w - 0.004, 0.008, 0.04), steel, bevel=0.0)
    # Diagonais de reforço atrás.
    span = math.hypot(w - 0.1, levels[2] - levels[0])
    angle = math.degrees(math.atan2(levels[2] - levels[0], w - 0.1))
    p.box((0, d / 2 - 0.006, (levels[0] + levels[2]) / 2), (span, 0.004, 0.025), steel, bevel=0.0, rot=(0, -angle, 0))
    if filled:
        rng = random.Random(seed)
        for z in levels[:4]:
            x = -w / 2 + 0.08
            while x < w / 2 - 0.25:
                if rng.random() < 0.25:
                    x += rng.uniform(0.15, 0.35)
                    continue
                bw = rng.uniform(0.3, 0.5)
                bh = rng.uniform(0.2, 0.4)
                bd = rng.uniform(0.3, 0.42)
                if x + bw > w / 2 - 0.06:
                    break
                carton(p, (x + bw / 2, rng.uniform(-0.03, 0.03), z + 0.006), (bw, bd, bh), rng.uniform(-6, 6))
                x += bw + rng.uniform(0.02, 0.06)
    return p.to_object()


def carton(p, base, size, turn=0.0, tape=True):
    """Uma caixa de papelão fechada com fita, apoiada em base (meio da face de baixo)."""
    bw, bd, bh = size
    c = (base[0], base[1], base[2] + bh / 2)
    p.box(c, (bw, bd, bh), "Papelao", bevel=0.006, rot=(0, 0, turn))
    if tape:
        # Fita no meio da tampa, descendo um pouco pela frente e por trás.
        m = euler_matrix((0, 0, turn))
        top = Vector(c) + Vector((0, 0, bh / 2 + 0.0008))
        p.box(top, (0.05, bd + 0.002, 0.0016), "Fita_Adesiva", bevel=0.0, rot=(0, 0, turn))
        for sy in (-1, 1):
            off = m @ Vector((0, sy * (bd / 2 + 0.0008), bh / 2 - 0.05))
            p.box(Vector(c) + off, (0.05, 0.0016, 0.1), "Fita_Adesiva", bevel=0.0, rot=(0, 0, turn))


def caixa(name="CaixaDePapelao"):
    p = Part(name)
    carton(p, (0, 0, 0), (0.5, 0.4, 0.36))
    return p.to_object()


def pilha_caixas(name="PilhaDeCaixas", seed=11):
    """Quatro ou cinco caixas empilhadas tortas, como entulho de mudança."""
    p = Part(name)
    rng = random.Random(seed)
    z = 0.0
    for i in range(5):
        bw = rng.uniform(0.45, 0.62)
        bd = rng.uniform(0.36, 0.48)
        bh = rng.uniform(0.28, 0.4)
        shift = (rng.uniform(-0.06, 0.06), rng.uniform(-0.05, 0.05))
        carton(p, (shift[0], shift[1], z), (bw, bd, bh), rng.uniform(-14, 14), tape=rng.random() < 0.7)
        z += bh
        if z > 1.6:
            break
    return p.to_object()


def placa(name, title, price, card="Papel_Branco", ink="Tinta_Vermelha"):
    """Placa de preço em pé sobre um pezinho: cartão de 0,6 x 0,4 m."""
    p = Part(name)
    w, h = 0.6, 0.4
    lift = 0.05
    p.box((0, 0, lift + h / 2), (w, 0.012, h), card, bevel=0.002)
    # Pezinho em T atrás.
    p.box((0, 0.04, 0.004), (0.22, 0.12, 0.008), "Metal_Pintado", bevel=0.0)
    p.box((0, 0.01, lift / 2 + 0.02), (0.03, 0.01, lift + 0.04), "Metal_Pintado", bevel=0.0)
    # Faixa, título e preço na frente (-Y).
    p.box((0, -0.0065, lift + h - 0.07), (w - 0.04, 0.001, 0.1), ink, bevel=0.0)
    p.text(title, (0, -0.0075, lift + h - 0.07), 0.07, card)
    p.text(price, (0, -0.0075, lift + 0.15), 0.13, ink)
    return p.to_object()


# --------------------------------------------------------------------------- Lote

BUILDERS = [
    cadeira,
    mesa_jantar,
    lambda: estofado("Sofa", 3, "Tecido_Azul"),
    lambda: estofado("Poltrona", 1, "Tecido_Vermelho"),
    estante_livros,
    guarda_roupa,
    lambda: estante_aco("EstanteDeAco"),
    lambda: estante_aco("EstanteDeAcoCheia", filled=True),
    caixa,
    pilha_caixas,
    lambda: placa("PlacaOferta", "SALE", "$199"),  # textos em inglês (a língua do jogo)
    lambda: placa("PlacaLiquidacao", "CLEARANCE", "$49", card="Papel_Vermelho", ink="Tinta_Branca"),
]

if __name__ == "__main__":
    main_guard(lambda: run_lote("basicos", BUILDERS))
