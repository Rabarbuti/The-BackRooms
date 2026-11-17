"""
Lote "quarto": os móveis da zona Quarto do Level 1 (Docs/level1-mostruario.md, seções 3.1 e
3.2). Cama de casal, cama de solteiro, criado-mudo com abajur e tapete. O guarda-roupa da zona
já existe no lote "basicos".

    blender -b --factory-startup -P Tools/Blender/lotes/quarto.py [-- --fotos]

As peças, a paleta comum e a exportação vêm de Tools/Blender/moveis_base.py.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from moveis_base import Part, main_guard, run_lote  # noqa: E402
from mathutils import Vector  # noqa: E402

# Cores novas deste lote: nome -> (cor sRGB, rugosidade, metal).
PALETA = {
    "Quarto_Colcha_Bege": ((190, 170, 136), 0.95, 0.0),
    "Quarto_Colcha_Azul": ((98, 124, 148), 0.95, 0.0),
    "Quarto_Lencol": ((232, 228, 214), 0.9, 0.0),
    "Quarto_Colchao": ((212, 206, 188), 0.9, 0.0),
    "Quarto_Ceramica_Verde": ((106, 134, 112), 0.3, 0.0),
    "Quarto_Cupula_Creme": ((226, 212, 172), 0.92, 0.0),
    "Quarto_Tapete_Tijolo": ((146, 72, 54), 0.96, 0.0),
    "Quarto_Tapete_Bege": ((196, 176, 136), 0.96, 0.0),
    "Quarto_Franja": ((214, 202, 170), 0.95, 0.0),
}


# --------------------------------------------------------------------------- Colcha
# Espaço do móvel: X para a direita, -Y é a frente, Z para cima, origem no chão no meio.

def colcha(p, material, hw, y_foot, y_head, top, d_max, seed, thick=0.022, r=0.045, k=0.09,
           amp=0.035, amp_fold=0.026, n_center=11, n_len=12):
    """Colcha meio amarrotada jogada sobre o colchão: uma placa grossa (duas camadas fechadas
    pela borda) que cobre o topo, dobra na quina com raio r e cai d_max (medido pelo pano) nas
    duas laterais e no pé. hw e y_foot são onde a dobra começa; top é a face de cima do pano.
    Vincos: ondas suaves no topo e pregas verticais que crescem para baixo na parte que cai."""
    rng = random.Random(seed)
    arc = math.pi * r / 2
    drops = [arc * 0.5, arc] + [arc + (d_max - arc) * i / 3 for i in (1, 2, 3)]
    s_vals = ([-(hw + d) for d in reversed(drops)]
              + [-hw + 2 * hw * i / n_center for i in range(n_center + 1)]
              + [hw + d for d in drops])
    t_vals = ([y_foot - d for d in reversed(drops)]
              + [y_foot + (y_head - y_foot) * i / n_len for i in range(n_len + 1)])

    waves = []
    for _ in range(5):
        a = rng.uniform(0, math.pi)
        waves.append((math.cos(a), math.sin(a), rng.uniform(6.0, 13.0), rng.uniform(0, 2 * math.pi),
                      rng.uniform(0.5, 1.0)))
    folds = [(rng.uniform(9.0, 15.0), rng.uniform(0, 2 * math.pi)) for _ in range(2)]
    total = sum(w[4] for w in waves)

    def point(s, t, inner):
        dx = max(0.0, abs(s) - hw)
        dy = max(0.0, y_foot - t)
        bx = max(-hw, min(hw, s))
        by = max(t, y_foot)
        d = math.hypot(dx, dy)
        ux, uy = (math.copysign(dx / d, s), -dy / d) if d > 1e-9 else (0.0, 0.0)
        if d <= arc:
            a = d / r
            h, z = r * math.sin(a), -r * (1 - math.cos(a))
        else:
            a = math.pi / 2
            h, z = r + k * (d - arc), -r - (d - arc)
        n = Vector((ux * math.sin(a), uy * math.sin(a), math.cos(a)))
        # Vincos do topo, sempre para fora (o pano não entra no colchão).
        w = sum(wt * (0.5 + 0.5 * math.sin(f * (cx * s + cy * t) + ph)) for cx, cy, f, ph, wt in waves)
        off = amp * w / total
        # Pregas da parte que cai: correm ao longo da borda e abrem para baixo.
        if d > arc:
            e = t * abs(ux) + s * abs(uy)
            grow = (d - arc) / (d_max - arc)
            off += amp_fold * grow * sum(0.5 + 0.5 * math.sin(f * e + ph) for f, ph in folds) / len(folds)
        pos = Vector((bx + ux * h, by + uy * h, top + z)) + n * off
        if inner:
            pos -= n * thick
        return pos

    bm = p.bm
    outer = [[bm.verts.new(point(s, t, False)) for t in t_vals] for s in s_vals]
    inner = [[bm.verts.new(point(s, t, True)) for t in t_vals] for s in s_vals]
    ni, nj = len(s_vals), len(t_vals)
    for i in range(ni - 1):
        for j in range(nj - 1):
            bm.faces.new((outer[i][j], outer[i + 1][j], outer[i + 1][j + 1], outer[i][j + 1]))
            bm.faces.new((inner[i][j + 1], inner[i + 1][j + 1], inner[i + 1][j], inner[i][j]))
    # Borda: fecha as duas camadas pelo contorno da grade.
    ring = ([(i, 0) for i in range(ni)] + [(ni - 1, j) for j in range(1, nj)]
            + [(i, nj - 1) for i in range(ni - 2, -1, -1)] + [(0, j) for j in range(nj - 2, 0, -1)])
    for (ia, ja), (ib, jb) in zip(ring, ring[1:] + ring[:1]):
        bm.faces.new((outer[ia][ja], outer[ib][jb], inner[ib][jb], inner[ia][ja]))
    verts = [v for col in outer for v in col] + [v for col in inner for v in col]
    # Cada peça acerta as próprias faces ao nascer (a colcha é fechada, então dá certo).
    p._finish_piece(verts, material, 0.0, smooth=True)


# --------------------------------------------------------------------------- Móveis

def cama(name, width, top, length, head_h, wood, spread, pillows, seed, double):
    """Cama com cabeceira nas costas (+Y), estrado de ripas sobre a caixa de madeira, colchão,
    colcha caindo nas laterais e no pé, travesseiros e a dobra do lençol."""
    p = Part(name)
    hx, hy = width / 2, length / 2
    post = 0.07 if double else 0.06
    rail = 0.035
    mthick = 0.22 if double else 0.20
    mb = top - mthick                     # base do colchão = topo da caixa
    rail_lo = 0.13

    # Cabeceira: dois montantes até o chão e o painel entre eles.
    py = hy - post / 2
    head_front = hy - post
    for sx in (-1, 1):
        x = sx * (hx - post / 2)
        if double:
            p.box((x, py, (head_h - 0.06) / 2), (post, post, head_h - 0.06), wood)
        else:
            p.box((x, py, (head_h - 0.03) / 2), (post, post, head_h - 0.03), wood)
            p.cylinder((x, py, head_h - 0.035), 0.012, 0.026, wood, segments=10)
            p.cylinder((x, py, head_h - 0.023), 0.023, 0.03, wood, segments=10, radius_top=0.012)
    inner_w = width - 2 * post
    if double:
        # Travessa de cima mais larga, painel liso e duas almofadas em relevo.
        p.box((0, py, head_h - 0.03), (width + 0.04, post + 0.02, 0.06), wood, bevel=0.01)
        panel_lo, panel_hi = mb - 0.03, head_h - 0.06
        p.box((0, py, (panel_lo + panel_hi) / 2), (inner_w, 0.03, panel_hi - panel_lo), wood)
        for sx in (-1, 1):
            p.box((sx * inner_w / 4, py - 0.017, top + 0.02 + (panel_hi - top - 0.02) / 2),
                  (inner_w / 2 - 0.1, 0.008, panel_hi - top - 0.1), wood, bevel=0.004)
    else:
        # Cabeceira de solteiro: travessa baixa e três ripas largas.
        p.box((0, py, mb + 0.03), (inner_w, 0.03, 0.08), wood)
        for z in (0.56, 0.645, 0.73):
            p.box((0, py, z), (inner_w, 0.025, 0.06), wood, bevel=0.006)

    # Caixa da cama: laterais e travessa do pé, com pés curtos nos cantos da frente.
    foot_y = -hy
    p.box_span((-hx, foot_y, rail_lo), (-hx + rail, head_front, mb), wood)
    p.box_span((hx - rail, foot_y, rail_lo), (hx, head_front, mb), wood)
    p.box_span((-hx + rail, foot_y, rail_lo), (hx - rail, foot_y + rail, mb), wood)
    for sx in (-1, 1):
        p.box_span((sx * (hx - 0.005) - (0.055 if sx > 0 else 0), foot_y + 0.005, 0),
                   (sx * (hx - 0.005) + (0.055 if sx < 0 else 0), foot_y + 0.06, rail_lo + 0.01), wood)
    # Estrado: ripas de madeira clara atravessadas, apoiadas na caixa.
    slats = 9 if double else 8
    span = head_front - (foot_y + rail) - 0.12
    for i in range(slats):
        y = foot_y + rail + 0.06 + span * i / (slats - 1)
        p.box((0, y, mb - 0.012), (width - 2 * rail, 0.07, 0.02), "Madeira_Clara", bevel=0.0)

    # Colchão, um pouco para dentro da caixa.
    mhw = hx - 0.015
    m_foot = foot_y + 0.015
    m_head = head_front - 0.005
    p.box((0, (m_foot + m_head) / 2, mb + mthick / 2), (2 * mhw, m_head - m_foot, mthick),
          "Quarto_Colchao", bevel=0.035, segments=2)

    # Colcha: começa a dobrar pouco antes da borda do colchão, cai até perto do topo da caixa.
    thick, r = 0.022, 0.045
    top_cloth = top + thick + 0.003
    y_head = m_head - (0.55 if double else 0.5)
    d_max = (top_cloth - (mb + 0.05)) - r + math.pi * r / 2
    colcha(p, spread, mhw - r + thick + 0.005, m_foot + r - thick - 0.005, y_head, top_cloth, d_max,
           seed, thick=thick, r=r, n_center=11 if double else 7)
    # Dobra do lençol virada sobre a colcha, na cabeceira.
    p.box((0, y_head + 0.02, top_cloth - 0.004), (2 * mhw + 0.006, 0.11, 0.032), "Quarto_Lencol",
          bevel=0.014, segments=2)

    # Travesseiros encostados na cabeceira, meio tortos.
    rng = random.Random(seed + 5)
    pw = 0.6 if double else 0.62
    for i in range(pillows):
        x = (i - (pillows - 1) / 2) * (pw + 0.04)
        p.box((x, head_front - 0.21, top + 0.065), (pw, 0.38, 0.13), "Quarto_Lencol", bevel=0.045,
              segments=3, rot=(-14 + rng.uniform(-3, 3), rng.uniform(-2, 2), rng.uniform(-4, 4)))
    return p.to_object()


def criado_mudo(name="CriadoMudo"):
    """Criado-mudo de madeira escura, 0,45 x 0,55 x 0,40 m, uma gaveta com puxador de latão e
    um nicho aberto embaixo; em cima, abajur de base de cerâmica e cúpula de tecido creme."""
    p = Part(name)
    wood = "Madeira_Escura"
    w, d, h = 0.45, 0.40, 0.55
    t = 0.018
    front = -d / 2 + 0.01                # frente da caixa (o tampo sobra 1 cm)
    back = d / 2 - 0.005
    # Pés torneados curtos.
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.cylinder((sx * 0.185, sy * 0.155, 0.0), 0.07, 0.016, wood, segments=8, radius_top=0.022)
    # Caixa: fundo, laterais, costas, divisória e tampo.
    lo, hi = 0.07, h - 0.025
    p.box_span((-w / 2 + 0.01, front, lo), (w / 2 - 0.01, back, lo + 0.02), wood)
    for sx in (-1, 1):
        x0 = w / 2 - 0.01 - t if sx > 0 else -w / 2 + 0.01
        p.box_span((x0, front, lo), (x0 + t, back, hi), wood)
    p.box_span((-w / 2 + 0.01 + t, back - 0.012, lo + 0.02), (w / 2 - 0.01 - t, back, hi), wood)
    p.box_span((-w / 2 + 0.01 + t, front, 0.365), (w / 2 - 0.01 - t, back - 0.012, 0.383), wood)
    p.box((0, 0, h - 0.0125), (w, d, 0.025), wood, bevel=0.007)
    # Gaveta: frente em leve relevo com puxador de latão.
    zc = (0.383 + hi) / 2
    p.box((0, front - 0.006, zc), (w - 0.02 - 2 * t - 0.004, 0.016, hi - 0.383 - 0.006), wood, bevel=0.004)
    p.cylinder((0, front - 0.014, zc), 0.014, 0.007, "Latao", segments=8, rot=(90, 0, 0))
    p.cylinder((0, front - 0.028, zc), 0.01, 0.016, "Latao", segments=10, radius_top=0.012, rot=(90, 0, 0))
    # Prateleira do nicho, no meio da altura.
    p.box_span((-w / 2 + 0.01 + t, front + 0.02, 0.225), (w / 2 - 0.01 - t, back - 0.012, 0.24), wood)

    # Abajur (0,40 m): base de cerâmica em cabaça, haste de latão e cúpula de tecido.
    ax, ay, z0 = 0.04, 0.03, h
    perfil = [(0.0, 0.05), (0.012, 0.058), (0.04, 0.07), (0.085, 0.076), (0.125, 0.066),
              (0.155, 0.044), (0.175, 0.024), (0.185, 0.02)]
    for (za, ra), (zb, rb) in zip(perfil[:-1], perfil[1:]):
        p.cylinder((ax, ay, z0 + za), zb - za, ra, "Quarto_Ceramica_Verde", segments=16, radius_top=rb)
    p.cylinder((ax, ay, z0 + 0.185), 0.012, 0.024, "Latao", segments=12)
    p.cylinder((ax, ay, z0 + 0.197), 0.06, 0.008, "Latao", segments=8)
    p.cylinder((ax, ay, z0 + 0.25), 0.03, 0.017, "Latao", segments=10)
    p.cylinder((ax, ay, z0 + 0.22), 0.18, 0.14, "Quarto_Cupula_Creme", segments=20, radius_top=0.095)
    p.cylinder((ax, ay, z0 + 0.395), 0.005, 0.012, "Latao", segments=8)
    return p.to_object()


def tapete(name="Tapete", seed=21):
    """Tapete retangular de 1,60 x 1,10 m (com as franjas), vermelho-tijolo desbotado com borda
    bege, filete bege por dentro, losango no meio e franjas curtas nas pontas curtas (em X)."""
    p = Part(name)
    body_w, body_d = 1.52, 1.10
    p.box((0, 0, 0.006), (body_w, body_d, 0.012), "Quarto_Tapete_Bege", bevel=0.003)
    fw, fd = body_w - 0.16, body_d - 0.16
    p.box((0, 0, 0.0135), (fw, fd, 0.003), "Quarto_Tapete_Tijolo", bevel=0.0)
    # Filete bege a 6 cm da borda do campo.
    ins = 0.06
    lw = 0.014
    for sy in (-1, 1):
        p.box((0, sy * (fd / 2 - ins), 0.0152), (fw - 2 * ins + lw, lw, 0.0006), "Quarto_Tapete_Bege", bevel=0.0)
    for sx in (-1, 1):
        p.box((sx * (fw / 2 - ins), 0, 0.0152), (lw, fd - 2 * ins - lw, 0.0006), "Quarto_Tapete_Bege", bevel=0.0)
    # Losango do meio: bege por fora, tijolo por dentro.
    p.prism([(0, -0.24), (0.38, 0), (0, 0.24), (-0.38, 0)], 0.015, 0.0156, "Quarto_Tapete_Bege")
    p.prism([(0, -0.17), (0.27, 0), (0, 0.17), (-0.27, 0)], 0.0156, 0.016, "Quarto_Tapete_Tijolo")
    # Franjas: tirinhas finas saindo por baixo das pontas, levemente tortas.
    rng = random.Random(seed)
    n = 26
    for sx in (-1, 1):
        for i in range(n):
            y = -body_d / 2 + 0.04 + (body_d - 0.08) * i / (n - 1)
            ln = rng.uniform(0.032, 0.04)
            p.box((sx * (body_w / 2 + ln / 2 - 0.008), y, 0.0015), (ln + 0.016, 0.012, 0.003),
                  "Quarto_Franja", bevel=0.0, rot=(0, 0, rng.uniform(-7, 7)))
    return p.to_object()


# --------------------------------------------------------------------------- Lote

BUILDERS = [
    lambda: cama("CamaDeCasal", 1.40, 0.55, 2.00, 1.0, "Madeira_Escura", "Quarto_Colcha_Bege",
                 pillows=2, seed=3, double=True),
    lambda: cama("CamaDeSolteiro", 0.90, 0.50, 1.95, 0.8, "Madeira_Clara", "Quarto_Colcha_Azul",
                 pillows=1, seed=8, double=False),
    criado_mudo,
    tapete,
]

if __name__ == "__main__":
    main_guard(lambda: run_lote("quarto", BUILDERS, PALETA))
