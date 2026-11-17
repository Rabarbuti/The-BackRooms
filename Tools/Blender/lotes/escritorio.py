"""
Lote "escritorio": as zonas Escritório e Cubículos do Level 1 (Docs/level1-mostruario.md, 3.1 e
3.2). Mesa de escritório com gaveteiro embutido, cadeira giratória, arquivo de aço bege,
computador de mesa (gabinete, teclado e mouse; o monitor é o MonitorCRT do lote "loja") e o
painel de divisória de cubículo.

    blender -b --factory-startup -P Tools/Blender/lotes/escritorio.py [-- --fotos]

As peças, a paleta comum e a exportação vêm de Tools/Blender/moveis_base.py.
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from moveis_base import Part, main_guard, run_lote  # noqa: E402

# Cores novas deste lote: nome -> (cor sRGB, rugosidade, metal). Prefixo Escritorio_ para não
# esbarrar nas cores dos outros lotes.
FORMICA = "Escritorio_Formica_Cinza"
BORDA = "Escritorio_Borda_Escura"
TECIDO = "Escritorio_Tecido_Grafite"
CROMO = "Escritorio_Cromado"
ACO_BEGE = "Escritorio_Aco_Bege"
TECLA = "Escritorio_Tecla_Bege"
DIVISORIA = "Escritorio_Tecido_Divisoria"
ALUMINIO = "Escritorio_Aluminio"

PALETA = {
    FORMICA: ((192, 192, 186), 0.42, 0.0),
    BORDA: ((50, 49, 48), 0.5, 0.0),
    TECIDO: ((44, 46, 52), 0.95, 0.0),
    CROMO: ((198, 200, 204), 0.18, 0.9),
    ACO_BEGE: ((196, 184, 152), 0.45, 0.3),
    TECLA: ((178, 170, 148), 0.6, 0.0),
    DIVISORIA: ((98, 112, 130), 0.95, 0.0),
    ALUMINIO: ((172, 176, 180), 0.35, 0.8),
}

# Da paleta comum.
METAL = "Metal_Pintado"
BEGE = "Plastico_Bege"
PRETO = "Plastico_Preto"


# --------------------------------------------------------------------------- Móveis
# Espaço do móvel: X para a direita, -Y é a frente, Z para cima, origem no chão no meio.

def mesa_de_escritorio(name="MesaDeEscritorio"):
    """Mesa de escritório: 1,40 x 0,70 m, tampo a 0,75 m. Tampo de fórmica cinza-claro com
    borda escura, pé em painel de metal à esquerda e gaveteiro de 3 gavetas à direita."""
    p = Part(name)
    w, d, h = 1.40, 0.70, 0.75
    top_t = 0.03
    under = h - top_t              # face de baixo do tampo
    # Tampo: miolo escuro (a borda) e a lâmina de fórmica por cima, 3 mm para dentro.
    p.box((0, 0, under + (top_t - 0.004) / 2), (w, d, top_t - 0.004), BORDA, bevel=0.004)
    p.box((0, 0, h - 0.002), (w - 0.006, d - 0.006, 0.004), FORMICA, bevel=0.0)

    # Pé em painel à esquerda, com sapata no chão.
    x_leg = -w / 2 + 0.04
    p.box((x_leg, 0, under / 2), (0.03, d - 0.08, under), METAL, bevel=0.004)
    p.box((x_leg, 0, 0.012), (0.05, d - 0.06, 0.024), METAL, bevel=0.004)

    # Gaveteiro embutido à direita, do chão até o tampo.
    gx0, gx1 = w / 2 - 0.44, w / 2 - 0.03
    gx = (gx0 + gx1) / 2
    gw = gx1 - gx0
    gy0, gy1 = -d / 2 + 0.035, d / 2 - 0.04
    p.box_span((gx0, gy0, 0.0), (gx1, gy1, under), METAL)
    # Rodapé recuado escuro.
    p.box((gx, gy0 - 0.001, 0.025), (gw - 0.02, 0.004, 0.05), BORDA, bevel=0.0)
    # Três gavetas (rasa, média, de pastas), frentes em relevo com puxador escuro.
    fronts = [(0.06, 0.29), (0.30, 0.50), (0.51, 0.70)]
    for z0, z1 in fronts:
        zc = (z0 + z1) / 2
        p.box((gx, gy0 - 0.008, zc), (gw - 0.014, 0.016, z1 - z0), METAL, bevel=0.004)
        zh = z1 - 0.045
        p.box((gx, gy0 - 0.024, zh), (0.13, 0.016, 0.018), BORDA, bevel=0.004)
        for sx in (-1, 1):
            p.box((gx + sx * 0.055, gy0 - 0.018, zh), (0.014, 0.012, 0.016), BORDA, bevel=0.0)

    # Painel de recato atrás, entre o pé e o gaveteiro.
    p.box_span((x_leg + 0.015, d / 2 - 0.07, 0.30), (gx0, d / 2 - 0.05, under), METAL)
    return p.to_object()


def _rodinha(p, ang, r, raio=0.027):
    """Rodízio duplo de cadeira na ponta de uma pata: duas rodas pretas e o garfo."""
    a = math.radians(ang)
    cx, cy = r * math.cos(a), r * math.sin(a)
    tx, ty = -math.sin(a), math.cos(a)        # direção do eixo da roda (tangente)
    largura = 0.013
    for s in (-1, 1):
        mx, my = cx + tx * s * 0.0135, cy + ty * s * 0.0135
        base = (mx - tx * largura / 2, my - ty * largura / 2, raio)
        p.cylinder(base, largura, raio, PRETO, segments=10, rot=(0, 90, ang + 90))
    # Garfo entre as rodas, subindo até a pata.
    p.box((cx, cy, 0.047), (0.036, 0.014, 0.042), PRETO, bevel=0.004, rot=(0, 0, ang))


def cadeira_giratoria(name="CadeiraGiratoria"):
    """Cadeira giratória de escritório: 0,60 x 0,60 x 1,00 m. Base de 5 patas com rodízios,
    coluna cromada, assento e encosto de tecido grafite, braços de plástico preto."""
    p = Part(name)
    # Base estrela: 5 patas, uma apontando para a frente.
    for k in range(5):
        ang = -90 + 72 * k
        a = math.radians(ang)
        r0, r1 = 0.035, 0.262
        rm = (r0 + r1) / 2
        p.box((rm * math.cos(a), rm * math.sin(a), 0.078), (r1 - r0, 0.042, 0.032), PRETO,
              bevel=0.006, rot=(0, 0, ang))
        _rodinha(p, ang, 0.252)
    # Cubo da base, capa da coluna e coluna de gás cromada.
    p.cylinder((0, 0, 0.058), 0.05, 0.05, PRETO, segments=14, radius_top=0.044)
    p.cylinder((0, 0, 0.108), 0.13, 0.034, PRETO, segments=14, radius_top=0.03)
    p.cylinder((0, 0, 0.238), 0.15, 0.022, CROMO, segments=14)
    # Mecanismo sob o assento.
    p.box((0, 0.0, 0.405), (0.22, 0.24, 0.035), PRETO, bevel=0.006)
    p.box((0.13, -0.04, 0.40), (0.08, 0.015, 0.012), PRETO, bevel=0.0)   # alavanca
    # Assento estofado.
    p.box((0, -0.02, 0.4625), (0.48, 0.46, 0.08), TECIDO, bevel=0.03, segments=2)
    # Haste do encosto: sai de trás do mecanismo e sobe por trás do encosto.
    p.box((0, 0.155, 0.405), (0.06, 0.13, 0.03), PRETO, bevel=0.006)
    p.box((0, 0.232, 0.53), (0.06, 0.025, 0.28), PRETO, bevel=0.006, rot=(-6, 0, 0))
    # Encosto estofado, inclinado para trás.
    tilt = 8
    p.box((0, 0.195, 0.765), (0.44, 0.07, 0.45), TECIDO, bevel=0.03, segments=2, rot=(-tilt, 0, 0))
    # Braços em L: braçadeira sob o assento, montante e apoio.
    for sx in (-1, 1):
        p.box((sx * 0.19, -0.01, 0.41), (0.14, 0.05, 0.016), PRETO, bevel=0.004)
        p.box((sx * 0.262, -0.01, 0.525), (0.03, 0.05, 0.245), PRETO, bevel=0.006)
        p.box((sx * 0.2665, -0.03, 0.66), (0.067, 0.26, 0.03), PRETO, bevel=0.01)
    return p.to_object()


def gaveteiro(name="Gaveteiro"):
    """Arquivo de aço bege: 0,40 x 0,50 x 0,70 m, três gavetas com puxador e porta-etiqueta."""
    p = Part(name)
    w, d, h = 0.40, 0.50, 0.70
    front = -d / 2
    # Corpo (as frentes ocupam os 1,5 cm da frente).
    p.box((0, 0.0075, h / 2), (w, d - 0.015, h), ACO_BEGE, bevel=0.005)
    # Rodapé recuado.
    p.box((0, front + 0.013, 0.02), (w - 0.03, 0.006, 0.04), PRETO, bevel=0.0)
    # Três gavetas iguais.
    z0, gap = 0.045, 0.01
    fh = (h - 0.01 - z0 - 2 * gap) / 3
    for i in range(3):
        zb = z0 + i * (fh + gap)
        zc = zb + fh / 2
        p.box((0, front + 0.008, zc), (w - 0.016, 0.016, fh), ACO_BEGE, bevel=0.004)
        # Puxador cromado em alça, um pouco acima do meio.
        zh = zc + 0.03
        p.box((0, front - 0.012, zh), (0.13, 0.012, 0.016), CROMO, bevel=0.004)
        for sx in (-1, 1):
            p.box((sx * 0.058, front - 0.004, zh), (0.012, 0.012, 0.014), CROMO, bevel=0.0)
        # Porta-etiqueta com cartão branco, acima do puxador.
        zl = zh + 0.045
        p.box((0, front - 0.002, zl), (0.09, 0.006, 0.034), CROMO, bevel=0.002)
        p.box((0, front - 0.005, zl), (0.076, 0.002, 0.022), "Papel_Branco", bevel=0.0)
    # Fechadura no alto, à direita.
    p.cylinder((0.155, front, h - 0.03), 0.006, 0.009, CROMO, segments=10, rot=(90, 0, 0))
    return p.to_object()


def computador_de_mesa(name="ComputadorDeMesa"):
    """Computador de mesa dos anos 90, sem o monitor (o MonitorCRT vai à parte): gabinete torre
    bege de pé à direita (0,20 de largura, 0,42 de altura, 0,45 de fundo), teclado bege à frente e
    mouse. Origem embaixo, no meio da pegada (0,79 x 0,46 m); o Unity o põe sobre o tampo."""
    p = Part(name)

    # --- Gabinete torre: 0,20 de largura, 0,42 de altura e 0,45 de fundo, frente em -Y.
    tx, ty0, ty1 = 0.2925, -0.218, 0.232
    tw, th = 0.20, 0.42
    ty = (ty0 + ty1) / 2
    p.box((tx, ty + 0.006, th / 2 + 0.006), (tw, ty1 - ty0 - 0.012, th - 0.012), BEGE, bevel=0.006)
    # Pezinhos.
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((tx + sx * 0.075, ty + sy * 0.17, 0.006), (0.03, 0.03, 0.012), PRETO, bevel=0.0)
    # Painel da frente, um pouco saliente.
    f = ty0 + 0.006
    p.box((tx, f, th / 2 + 0.006), (tw - 0.008, 0.012, th - 0.016), BEGE, bevel=0.004)
    fy = ty0 - 0.001                 # plano da frente do painel
    # Duas baias de 5,25" (tampas com fresta escura) e uma de 3,5" com o drive de disquete.
    for zc in (0.375, 0.327):
        p.box((tx, fy - 0.001, zc), (0.16, 0.004, 0.042), BEGE, bevel=0.002)
        p.box((tx, fy - 0.0035, zc - 0.012), (0.12, 0.002, 0.003), PRETO, bevel=0.0)
    p.box((tx + 0.025, fy - 0.001, 0.28), (0.105, 0.004, 0.03), BEGE, bevel=0.002)
    p.box((tx + 0.025, fy - 0.0035, 0.282), (0.075, 0.002, 0.005), PRETO, bevel=0.0)
    p.box((tx + 0.06, fy - 0.0035, 0.272), (0.012, 0.002, 0.007), PRETO, bevel=0.0)
    # Botão de ligar, reset e luzes.
    p.box((tx - 0.045, fy - 0.004, 0.225), (0.04, 0.008, 0.025), "Metal_Pintado", bevel=0.003)
    p.box((tx + 0.02, fy - 0.003, 0.225), (0.012, 0.006, 0.01), "Metal_Pintado", bevel=0.0)
    p.box((tx + 0.05, fy - 0.0025, 0.23), (0.008, 0.004, 0.005), "Tinta_Laranja", bevel=0.0)
    p.box((tx + 0.065, fy - 0.0025, 0.23), (0.008, 0.004, 0.005), "Tinta_Vermelha", bevel=0.0)
    # Grade de ventilação embaixo (frisos escuros).
    for i in range(6):
        p.box((tx, fy - 0.0025, 0.05 + i * 0.018), (0.14, 0.002, 0.005), PRETO, bevel=0.0)

    # --- Teclado: 0,45 x 0,16 x 0,03 m: base de 2,2 cm e teclas de 8 mm em relevo.
    kx, ky = -0.1675, -0.145
    kw, kd = 0.45, 0.16
    p.box((kx, ky, 0.011), (kw, kd, 0.022), BEGE, bevel=0.004)

    def tecla(x, y, sx, sy):
        """Tecla em relevo sobre a base (x, y relativos ao meio do teclado)."""
        p.box((kx + x, ky + y, 0.026), (sx, sy, 0.008), TECLA, bevel=0.0)

    u = 0.0188           # passo das teclas
    k = 0.016            # tamanho de uma tecla
    x0 = -kw / 2 + 0.015
    # Linha das teclas de função, mais rasa e separada.
    yf = kd / 2 - 0.022
    for i in range(13):
        tecla(x0 + i * u + k / 2 + (0.01 if i > 0 else 0) + (0.008 if i > 4 else 0) + (0.008 if i > 8 else 0),
              yf, k, 0.011)
    # Bloco principal: 4 linhas de 14 teclas e a linha de baixo com a barra de espaço.
    for r in range(4):
        y = yf - 0.022 - r * u
        n = 14 if r < 3 else 13
        for i in range(n):
            tecla(x0 + i * u + k / 2 + (r % 2) * 0.004, y, k, k)
    yb = yf - 0.022 - 4 * u
    for i in range(3):
        tecla(x0 + i * u + k / 2, yb, k, k)
    tecla(x0 + 3 * u + 0.06, yb, 0.112, k)               # barra de espaço
    for i in range(3):
        tecla(x0 + 11 * u + i * u + k / 2, yb, k, k)
    # Setas e teclado numérico à direita.
    xs = x0 + 14 * u + 0.012
    for dx, dy in ((0, 0), (-u, 0), (u, 0), (0, u)):
        tecla(xs + u + dx + k / 2, yb + dy, k, k)
    xn = xs + 3 * u + 0.012
    for r in range(5):
        for i in range(4):
            tecla(xn + i * u + k / 2, yf - 0.022 - r * u, k, k)

    # --- Mouse: 0,06 x 0,10 x 0,035 m, à direita do teclado, com o fio até o gabinete.
    mx, my = 0.125, -0.15
    p.box((mx, my, 0.014), (0.058, 0.098, 0.028), BEGE, bevel=0.012, segments=2)
    p.box((mx, my - 0.022, 0.029), (0.05, 0.05, 0.008), BEGE, bevel=0.004)
    p.box((mx, my - 0.022, 0.0335), (0.002, 0.048, 0.002), PRETO, bevel=0.0)
    r = 0.0025
    p.tube([(mx, my + 0.05, 0.012), (mx, my + 0.08, r), (mx + 0.02, 0.0, r),
            (tx - tw / 2 - 0.001, 0.12, r)], r, PRETO, segments=6)
    # Fio do teclado, saindo por trás até o gabinete.
    yk = ky + kd / 2
    p.tube([(kx + 0.05, yk, 0.012), (kx + 0.05, yk + 0.03, r), (0.0, yk + 0.08, r),
            (tx - tw / 2 - 0.001, ty1 - 0.03, r)], r, PRETO, segments=6)
    return p.to_object()


def divisoria_cubiculo(name="DivisoriaCubiculo"):
    """Painel de divisória de cubículo: 1,40 x 0,06 x 1,60 m. Miolo de tecido cinza-azulado,
    moldura de alumínio e dois pés curtos de apoio."""
    p = Part(name)
    w, d, h = 1.40, 0.06, 1.60
    foot = 0.05                  # altura dos pés: o painel começa aqui
    frame = 0.035
    # Moldura: montantes laterais, travessa de cima e rodapé.
    for sx in (-1, 1):
        x = sx * (w / 2 - frame / 2)
        p.box((x, 0, (foot + h) / 2), (frame, d, h - foot), ALUMINIO, bevel=0.005)
    p.box((0, 0, h - frame / 2), (w - 2 * frame + 0.002, d - 0.004, frame), ALUMINIO, bevel=0.005)
    p.box((0, 0, foot + 0.035), (w - 2 * frame + 0.002, d - 0.004, 0.07), ALUMINIO, bevel=0.004)
    # Miolo de tecido, recuado da moldura.
    z0, z1 = foot + 0.07, h - frame
    p.box((0, 0, (z0 + z1) / 2), (w - 2 * frame + 0.002, d - 0.016, z1 - z0 + 0.002), DIVISORIA, bevel=0.003)
    # Frisos da moldura no rodapé (tampa de passagem de fios).
    for s in (-1, 1):
        p.box((0, s * (d / 2 - 0.001), foot + 0.035), (w - 2 * frame - 0.06, 0.002, 0.004), "Metal_Escuro", bevel=0.0)
    # Dois pés curtos com sapata preta.
    for sx in (-1, 1):
        x = sx * (w / 2 - 0.2)
        p.box((x, 0, foot / 2 + 0.006), (0.07, d - 0.01, foot - 0.012), ALUMINIO, bevel=0.004)
        p.box((x, 0, 0.006), (0.09, d, 0.012), PRETO, bevel=0.003)
    return p.to_object()


# --------------------------------------------------------------------------- Lote

BUILDERS = [
    mesa_de_escritorio,
    cadeira_giratoria,
    gaveteiro,
    computador_de_mesa,
    divisoria_cubiculo,
]

if __name__ == "__main__":
    main_guard(lambda: run_lote("escritorio", BUILDERS, PALETA))
