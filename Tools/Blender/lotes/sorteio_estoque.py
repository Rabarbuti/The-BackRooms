"""
Lote "sorteio_estoque": objetos das zonas Sorteio, Lavanderia e Estoque do Level 1
(Docs/level1-mostruario.md, seções 3.1 e 3.2). O carro do sorteio com a faixa "CONCORRA!" e o
poste de fila; o cesto e a roupa largada da lavanderia; o palete e o carrinho do estoque.

    blender -b --factory-startup -P Tools/Blender/lotes/sorteio_estoque.py [-- --fotos]

As peças, a paleta comum e a exportação vêm de Tools/Blender/moveis_base.py. Aqui ficam só três
peças a mais, montadas sobre o mesmo Part: fita (faixa achatada por uma curva: laço, cordão,
manga), pano (lençol com relevo: roupa) e casca (anéis de contorno ligados: cesto afunilado).
"""

import math
import os
import random
import sys

from mathutils import Vector

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from moveis_base import Part, main_guard, run_lote, write_texture  # noqa: E402

# Cores novas deste lote: nome -> (cor sRGB, rugosidade, metal[, textura]).
PALETA = {
    # Sorteio: carro, faixa, poste de fila.
    "Sorteio_Pintura_Vermelha": ((158, 20, 24), 0.32, 0.0),
    "Sorteio_Laco": ((214, 30, 52), 0.38, 0.0),
    "Sorteio_Vidro_Escuro": ((26, 30, 34), 0.06, 0.0),
    "Sorteio_Pneu": ((20, 20, 21), 0.88, 0.0),
    "Sorteio_Calota": ((178, 182, 186), 0.3, 0.8),
    "Sorteio_Plastico_Preto": ((30, 30, 32), 0.6, 0.0),
    "Sorteio_Chassi": ((14, 14, 15), 0.9, 0.0),
    "Sorteio_Farol": ((226, 228, 214), 0.12, 0.0),
    "Sorteio_Lanterna": ((150, 18, 20), 0.15, 0.0),
    "Sorteio_Pisca": ((226, 128, 30), 0.15, 0.0),
    "Sorteio_Placa": ((228, 226, 212), 0.5, 0.0),
    "Sorteio_Tecido_Amarelo": ((238, 196, 36), 0.9, 0.0),
    "Sorteio_Letra_Vermelha": ((200, 22, 26), 0.7, 0.0),
    "Sorteio_Cromado": ((206, 210, 214), 0.14, 1.0),
    "Sorteio_Veludo_Vermelho": ((128, 14, 24), 0.95, 0.0),
    # Lavanderia: cesto e roupas, em cores apagadas.
    "Lavanderia_Vime": ((255, 255, 255), 0.85, 0.0, "Lavanderia_Vime_Trama"),
    "Lavanderia_Vime_Escuro": ((168, 128, 78), 0.85, 0.0),
    "Lavanderia_Roupa_Azul": ((84, 100, 126), 0.95, 0.0),
    "Lavanderia_Roupa_Bege": ((178, 166, 138), 0.95, 0.0),
    "Lavanderia_Roupa_Verde": ((98, 114, 92), 0.95, 0.0),
    "Lavanderia_Roupa_Rosa": ((166, 124, 124), 0.95, 0.0),
    "Lavanderia_Roupa_Cinza": ((124, 124, 122), 0.95, 0.0),
    "Lavanderia_Roupa_Branca": ((206, 202, 190), 0.95, 0.0),
    # Estoque: palete e carrinho.
    "Estoque_Madeira_Palete": ((182, 150, 106), 0.85, 0.0),
    "Estoque_Madeira_Palete_Escura": ((148, 116, 78), 0.88, 0.0),
    "Estoque_Chapa_Cinza": ((118, 122, 124), 0.5, 0.5),
    "Estoque_Azul_Alca": ((30, 72, 160), 0.4, 0.3),
    "Estoque_Rodizio": ((22, 22, 24), 0.8, 0.0),
    "Estoque_Zincado": ((168, 170, 168), 0.35, 0.8),
}

ROUPAS = ["Lavanderia_Roupa_Azul", "Lavanderia_Roupa_Bege", "Lavanderia_Roupa_Verde",
          "Lavanderia_Roupa_Rosa", "Lavanderia_Roupa_Cinza", "Lavanderia_Roupa_Branca"]


# --------------------------------------------------------------------------- Peças a mais

def fita(p, pontos, largura, espessura, material, larg_dir=(0.0, 1.0, 0.0), fechada=False,
         deitada=False, lados=8):
    """Faixa achatada ao longo dos pontos (seção elíptica: largura x espessura), com sombra lisa.
    largura: número ou lista (um por ponto). larg_dir: para onde fica a largura; deitada=True
    põe a largura na horizontal, perpendicular ao caminho (fita deitada no teto, cordão)."""
    pts = [Vector(c) for c in pontos]
    n = len(pts)
    larguras = largura if isinstance(largura, (list, tuple)) else [largura] * n
    aneis = []
    for i in range(n):
        if fechada:
            a, b = pts[i - 1], pts[(i + 1) % n]
        else:
            a, b = pts[max(i - 1, 0)], pts[min(i + 1, n - 1)]
        t = (b - a).normalized()
        if deitada:
            w = Vector((0.0, 0.0, 1.0)).cross(t)
            if w.length < 1e-4:
                w = Vector((1.0, 0.0, 0.0))
        else:
            w = Vector(larg_dir)
            w = w - t * t.dot(w)
        w.normalize()
        nn = t.cross(w).normalized()
        anel = []
        for k in range(lados):
            ang = 2 * math.pi * k / lados
            off = w * (math.cos(ang) * larguras[i] / 2) + nn * (math.sin(ang) * espessura / 2)
            anel.append(p.bm.verts.new(pts[i] + off))
        aneis.append(anel)
    pares = list(zip(aneis, aneis[1:] + aneis[:1])) if fechada else list(zip(aneis[:-1], aneis[1:]))
    for r0, r1 in pares:
        for k in range(lados):
            j = (k + 1) % lados
            p.bm.faces.new((r0[k], r0[j], r1[j], r1[k]))
    if not fechada:
        p.bm.faces.new(list(reversed(aneis[0])))
        p.bm.faces.new(aneis[-1])
    p._finish_piece([v for anel in aneis for v in anel], material, 0.0, smooth=True)


def pano(p, centro, sx, sy, altura, material, giro=0.0, nx=10, ny=8, espessura=0.012):
    """Pano com relevo: grade de nx x ny com a face de cima em z = altura(u, v) (u, v de -0,5 a
    0,5) e a de baixo uma espessura abaixo, sem passar do chão. Girado giro graus em Z."""
    c, s = math.cos(math.radians(giro)), math.sin(math.radians(giro))
    cima, baixo = [], []
    for j in range(ny + 1):
        for i in range(nx + 1):
            u, v = i / nx - 0.5, j / ny - 0.5
            lx, ly = u * sx, v * sy
            x = centro[0] + lx * c - ly * s
            y = centro[1] + lx * s + ly * c
            z = max(altura(u, v), espessura + 0.002)
            cima.append(p.bm.verts.new((x, y, z)))
            baixo.append(p.bm.verts.new((x, y, max(z - espessura, 0.0005))))

    def at(lista, i, j):
        return lista[j * (nx + 1) + i]

    for j in range(ny):
        for i in range(nx):
            p.bm.faces.new((at(cima, i, j), at(cima, i + 1, j), at(cima, i + 1, j + 1), at(cima, i, j + 1)))
            p.bm.faces.new((at(baixo, i, j + 1), at(baixo, i + 1, j + 1), at(baixo, i + 1, j), at(baixo, i, j)))
    borda = ([(i, 0) for i in range(nx)] + [(nx, j) for j in range(ny)]
             + [(i, ny) for i in range(nx, 0, -1)] + [(0, j) for j in range(ny, 0, -1)])
    for k, (i, j) in enumerate(borda):
        i2, j2 = borda[(k + 1) % len(borda)]
        p.bm.faces.new((at(cima, i, j), at(baixo, i, j), at(baixo, i2, j2), at(cima, i2, j2)))
    p._finish_piece(cima + baixo, material, 0.0, smooth=True)


def casca(p, aneis, material, tampa_baixo=True, tampa_cima=True):
    """Liga anéis de contorno [(z, [(x, y), ...]), ...] (mesmo número de pontos, anti-horário)
    numa peça fechada: cesto afunilado, borda, aro."""
    vs = [[p.bm.verts.new((x, y, z)) for x, y in contorno] for z, contorno in aneis]
    n = len(vs[0])
    for r0, r1 in zip(vs[:-1], vs[1:]):
        for k in range(n):
            j = (k + 1) % n
            p.bm.faces.new((r0[k], r0[j], r1[j], r1[k]))
    if tampa_baixo:
        p.bm.faces.new(list(reversed(vs[0])))
    if tampa_cima:
        p.bm.faces.new(vs[-1])
    p._finish_piece([v for r in vs for v in r], material, 0.0)


def retangulo_redondo(larg, fundo, raio, seg=4):
    """Contorno anti-horário de um retângulo de cantos redondos, centrado na origem."""
    pts = []
    cantos = [(larg / 2 - raio, fundo / 2 - raio, 0), (-larg / 2 + raio, fundo / 2 - raio, 90),
              (-larg / 2 + raio, -fundo / 2 + raio, 180), (larg / 2 - raio, -fundo / 2 + raio, 270)]
    for cx, cy, a0 in cantos:
        for k in range(seg + 1):
            a = math.radians(a0 + 90 * k / seg)
            pts.append((cx + raio * math.cos(a), cy + raio * math.sin(a)))
    return pts


def lateral(p, contorno_yz, x0, x1, material, bevel=0.0):
    """Contorno no plano YZ [(y, z), ...] extrudado em X de x0 a x1 (perfil do carro, travessa)."""
    p.prism(contorno_yz, x0, x1, material, bevel=bevel, rot=(90.0, 0.0, 90.0))


def placa_inclinada(p, a, b, x0, x1, material, recuo=0.05, espessura=0.012, saliencia=0.004):
    """Chapa fina sobre o segmento a-b do perfil (y, z), estendida em X: para-brisa, vigia."""
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    nrm = Vector((-d.y, d.x))
    if nrm.y < 0:
        nrm = -nrm
    a2, b2 = a + d * recuo, b - d * recuo
    fora, dentro = nrm * saliencia, nrm * (saliencia - espessura)
    contorno = [tuple(a2 + dentro), tuple(b2 + dentro), tuple(b2 + fora), tuple(a2 + fora)]
    lateral(p, contorno, x0, x1, material)


# --------------------------------------------------------------------------- Sorteio

def carro(name="Carro"):
    """Sedã popular dos anos 90, vermelho, de prêmio de sorteio: 1,70 x 1,40 x 3,90 m (frente em
    -Y), formas retas, vidros escuros, calotas, para-choques pretos e um laço grande no teto."""
    p = Part(name)
    red = "Sorteio_Pintura_Vermelha"
    meia = 0.83                       # meia largura da carroceria (para-choques vão a 0,85)
    yf, yt = -1.87, 1.87              # frente e traseira da lata (placas e para-choques: 3,90 m)
    zb = 0.21                         # fundo da carroceria
    rr, zr = 0.295, 0.295             # raio e altura do centro das rodas
    ye, yd = -1.25, 1.22              # eixo dianteiro e traseiro
    ra = 0.36                         # raio da caixa de roda

    def arco(yc):
        pts = [(yc + ra, zb), (yc + ra, zr)]
        for k in range(1, 8):
            a = math.pi * k / 8
            pts.append((yc + ra * math.cos(a), zr + ra * math.sin(a)))
        pts += [(yc - ra, zr), (yc - ra, zb)]
        return pts

    # Perfil de lado da carroceria baixa (capô, cintura, porta-malas), com as caixas de roda.
    corpo = [(yf, zb), (yf, 0.66), (yf + 0.10, 0.75), (-0.80, 0.83), (1.10, 0.85),
             (yt - 0.08, 0.83), (yt, 0.76), (yt, zb)]
    corpo += arco(yd) + arco(ye)
    # A ordem acima vai da frente por cima até a traseira e volta por baixo (horário visto de
    # +X); o prisma acerta as faces sozinho.
    lateral(p, corpo, -meia, meia, red, bevel=0.016)

    # Cabine: para-brisa, teto e vigia, mais estreita que a carroceria.
    A, B, C, D = (-0.78, 0.83), (-0.14, 1.385), (0.76, 1.385), (1.12, 0.85)
    cab = 0.75
    lateral(p, [A, D, C, B], -cab, cab, red, bevel=0.018)
    placa_inclinada(p, A, B, -cab + 0.07, cab - 0.07, "Sorteio_Vidro_Escuro", recuo=0.04)
    placa_inclinada(p, C, D, -cab + 0.07, cab - 0.07, "Sorteio_Vidro_Escuro", recuo=0.04)

    # Vidros laterais: porta da frente e de trás de cada lado, coluna B no meio.
    def y_em(p0, p1, z):
        return p0[0] + (z - p0[1]) / (p1[1] - p0[1]) * (p1[0] - p0[0])
    z0, z1 = 0.90, 1.33
    frente = [(y_em(A, B, z0) + 0.06, z0), (0.26, z0), (0.26, z1), (y_em(A, B, z1) + 0.06, z1)]
    tras = [(0.34, z0), (y_em(C, D, z0) - 0.06, z0), (y_em(C, D, z1) - 0.06, z1), (0.34, z1)]
    for sx in (-1, 1):
        x0, x1 = sorted((sx * (cab - 0.005), sx * (cab + 0.006)))
        lateral(p, frente, x0, x1, "Sorteio_Vidro_Escuro")
        lateral(p, tras, x0, x1, "Sorteio_Vidro_Escuro")

    # Vão das rodas por dentro: bloco escuro entre as rodas (sem ele se vê através do carro).
    for yc in (ye, yd):
        p.box((0, yc, 0.44), (2 * meia - 0.36, 2 * ra, 0.46), "Sorteio_Chassi", bevel=0.0)

    # Rodas: pneu, calota abaulada e miolo.
    for sx in (-1, 1):
        for yc in (ye, yd):
            xo = sx * (meia - 0.005)          # face de fora do pneu
            larg = 0.18
            base = (xo - larg, yc, zr) if sx > 0 else (xo, yc, zr)
            p.cylinder(base, larg, rr, "Sorteio_Pneu", segments=14, rot=(0, 90, 0))
            rot = (0, 90, 0) if sx > 0 else (0, -90, 0)
            p.cylinder((xo, yc, zr), 0.012, 0.205, "Sorteio_Calota", segments=14,
                       radius_top=0.19, rot=rot)
            p.cylinder((xo + sx * 0.012, yc, zr), 0.01, 0.07, "Sorteio_Calota", segments=6,
                       radius_top=0.05, rot=rot)

    # Para-choques pretos, com as pontas dobrando para os lados.
    for sy, yy in ((-1, yf), (1, yt)):
        p.box((0, yy + sy * 0.02, 0.33), (1.70, 0.10, 0.17), "Sorteio_Plastico_Preto", bevel=0.015)
        for sx in (-1, 1):
            p.box((sx * 0.80, yy - sy * 0.12, 0.33), (0.10, 0.26, 0.17), "Sorteio_Plastico_Preto",
                  bevel=0.015)
    # Friso preto na porta, ao longo dos dois lados.
    for sx in (-1, 1):
        p.box((sx * (meia + 0.006), -0.015, 0.48), (0.014, 1.71, 0.05), "Sorteio_Plastico_Preto",
              bevel=0.004)
        # Vãos das portas e maçanetas.
        for yy in (-0.76, 0.30, 0.84):
            p.box((sx * (meia + 0.001), yy, 0.62), (0.006, 0.008, 0.40), "Sorteio_Chassi", bevel=0.0)
        for yy in (0.16, 0.92):
            p.box((sx * (meia + 0.008), yy, 0.75), (0.016, 0.12, 0.03), "Sorteio_Plastico_Preto",
                  bevel=0.004)
        # Retrovisor.
        p.box((sx * 0.80, -0.66, 0.895), (0.10, 0.06, 0.13), "Sorteio_Plastico_Preto", bevel=0.012)

    # Frente: faróis retangulares, grade preta, piscas e placa.
    yfa = yf - 0.006
    for sx in (-1, 1):
        p.box((sx * 0.52, yfa, 0.57), (0.40, 0.016, 0.14), "Sorteio_Farol", bevel=0.004)
        p.box((sx * 0.77, yfa, 0.57), (0.10, 0.016, 0.14), "Sorteio_Pisca", bevel=0.004)
    p.box((0, yfa, 0.57), (0.60, 0.016, 0.12), "Sorteio_Plastico_Preto", bevel=0.004)
    for k in range(3):
        p.box((0, yfa - 0.006, 0.535 + k * 0.035), (0.56, 0.006, 0.008), "Sorteio_Calota", bevel=0.0)
    p.box((0, yf - 0.075, 0.33), (0.40, 0.012, 0.11), "Sorteio_Placa", bevel=0.003)

    # Traseira: lanternas vermelhas em faixa, piscas e placa.
    yta = yt + 0.006
    for sx in (-1, 1):
        p.box((sx * 0.54, yta, 0.64), (0.50, 0.016, 0.13), "Sorteio_Lanterna", bevel=0.004)
        p.box((sx * 0.70, yta + 0.002, 0.64), (0.14, 0.016, 0.13), "Sorteio_Pisca", bevel=0.004)
    p.box((0, yta, 0.64), (0.56, 0.016, 0.13), "Sorteio_Plastico_Preto", bevel=0.004)
    p.box((0, yt + 0.075, 0.33), (0.40, 0.012, 0.13), "Sorteio_Placa", bevel=0.003)

    # Laço grande no teto: nó, duas alças e duas pontas deitadas no teto.
    zt = 1.385
    yc = 0.30
    knot = Vector((0.0, yc, zt + 0.06))
    for sx in (-1, 1):
        pts, larg = [], []
        n = 20
        for k in range(n):
            t = 2 * math.pi * k / n
            pts.append(knot + Vector((sx * 0.22 * (1 - math.cos(t)),
                                      0.0,
                                      0.10 * math.sin(t) + 0.10 * (1 - math.cos(t)) / 2)))
            larg.append(0.07 + 0.09 * (1 - math.cos(t)) / 2)
        fita(p, pts, larg, 0.014, "Sorteio_Laco", larg_dir=(0, 1, 0), fechada=True, lados=6)
        rabo = [knot + Vector((sx * 0.02, 0.0, -0.01)), (sx * 0.07, yc - 0.07, zt + 0.02),
                (sx * 0.14, yc - 0.20, zt + 0.008), (sx * 0.22, yc - 0.36, zt + 0.007),
                (sx * 0.28, yc - 0.50, zt + 0.007)]
        fita(p, rabo, [0.07, 0.08, 0.09, 0.09, 0.09], 0.01, "Sorteio_Laco", deitada=True, lados=6)
    p.box(tuple(knot), (0.11, 0.13, 0.10), "Sorteio_Laco", bevel=0.035, segments=2)
    return p.to_object()


# Texto da faixa do sorteio (a língua do jogo é o inglês; era "CONCORRA!", 0,31 m).
FAIXA_TEXTO = "ENTER TO WIN!"
FAIXA_TAMANHO = 0.24


def faixa_concorra(name="FaixaConcorra"):
    """Faixa de tecido amarelo de 2,4 x 0,5 m com "CONCORRA!" em vermelho nas duas faces,
    amarrada entre dois postes finos de 2,2 m. Ocupa 2,4 x 0,1 x 2,2 m."""
    p = Part(name)
    xp = 1.2 - 0.05                   # centro dos postes (base de 5 cm de raio no limite)
    for sx in (-1, 1):
        x = sx * xp
        p.cylinder((x, 0, 0.0), 0.02, 0.05, "Sorteio_Cromado", segments=16, radius_top=0.045)
        p.cylinder((x, 0, 0.02), 2.16, 0.012, "Sorteio_Cromado", segments=10)
        p.cylinder((x, 0, 2.17), 0.03, 0.018, "Sorteio_Cromado", segments=10, radius_top=0.01)
    # Tecido entre os postes, com bainha um pouco mais escura nas pontas.
    z0, z1 = 1.55, 2.05
    xb = xp - 0.05
    p.box((0, 0, (z0 + z1) / 2), (2 * xb, 0.008, z1 - z0), "Sorteio_Tecido_Amarelo", bevel=0.002)
    # Cordinhas dos cantos até os postes.
    for sx in (-1, 1):
        for z in (z0 + 0.03, z1 - 0.03):
            p.tube([(sx * (xb - 0.01), 0, z), (sx * (xp - 0.01), 0, z + 0.01)], 0.004,
                   "Sorteio_Cromado", segments=6)
            p.cylinder((sx * xp, 0, z), 0.02, 0.016, "Sorteio_Cromado", segments=10)
    # Letras em relevo, nas duas faces (a de trás girada para ler certo por trás).
    zc = (z0 + z1) / 2 - 0.01
    p.text(FAIXA_TEXTO, (0, -0.0045, zc), FAIXA_TAMANHO, "Sorteio_Letra_Vermelha", depth=0.0015,
           resolution=3)
    p.text(FAIXA_TEXTO, (0, 0.0045, zc), FAIXA_TAMANHO, "Sorteio_Letra_Vermelha", depth=0.0015,
           rot=(90.0, 0.0, 180.0), resolution=3)
    return p.to_object()


def poste_de_fila(name="PosteDeFila"):
    """Poste cromado de fila de banco (0,30 x 0,95 x 0,30 m) com meio cordão de veludo vermelho
    pendurado para +X, caindo em curva."""
    p = Part(name)
    cr = "Sorteio_Cromado"
    p.cylinder((0, 0, 0.0), 0.015, 0.15, cr, segments=24, radius_top=0.148)
    p.cylinder((0, 0, 0.015), 0.02, 0.148, cr, segments=24, radius_top=0.11)
    p.cylinder((0, 0, 0.035), 0.012, 0.11, cr, segments=24, radius_top=0.04)
    p.cylinder((0, 0, 0.047), 0.85, 0.028, cr, segments=16)
    # Cabeça: anel, tampa abaulada e o gancho onde o cordão se prende.
    p.cylinder((0, 0, 0.897), 0.02, 0.034, cr, segments=16)
    p.cylinder((0, 0, 0.917), 0.02, 0.034, cr, segments=16, radius_top=0.026)
    p.cylinder((0, 0, 0.937), 0.013, 0.026, cr, segments=16, radius_top=0.008)
    gancho = (0.032, 0.0, 0.905)
    p.cylinder(gancho, 0.012, 0.012, cr, segments=10, rot=(0, 90, 0))
    # Meio cordão: sai do gancho e cai como uma corrente (catenária), até o ponto mais baixo.
    x0, z0 = 0.05, 0.895
    vao, flecha = 0.60, 0.30
    pts = []
    n = 18
    for k in range(n + 1):
        s = k / n
        x = x0 + vao * s
        # Parábola da corrente: desce depressa perto do gancho e chega deitada no fim.
        z = z0 - flecha * (1 - (1 - s) ** 2)
        pts.append((x, 0.0, z))
    p.box((0.044, 0, 0.9), (0.03, 0.03, 0.03), cr, bevel=0.006)
    fita(p, pts, 0.03, 0.03, "Sorteio_Veludo_Vermelho", deitada=True, lados=8)
    return p.to_object()


# --------------------------------------------------------------------------- Lavanderia

def vime_trama(u, v):
    """Trama de vime: fios deitados de 1 cm passando por cima e por baixo de varetas a cada 2 cm."""
    fu, fv = u * 50.0, v * 100.0
    col, row = int(fu), int(fv)
    fu, fv = fu - col, fv - row
    if (row + col) % 2 == 0:
        luz = 0.62 + 0.38 * math.sin(math.pi * fv)
        base = (228, 196, 140)
    else:
        luz = 0.6 + 0.3 * math.sin(math.pi * fu)
        base = (204, 168, 112)
    borda = min(fu, 1 - fu, fv * 0.5, (1 - fv) * 0.5)
    if borda < 0.04:
        luz *= 0.7
    return tuple(min(255, c * luz) for c in base)


def cesto_de_roupa(name="CestoDeRoupa", seed=7):
    """Cesto de vime afunilado (0,50 x 0,55 x 0,40 m com a roupa) cheio de roupa amontoada."""
    rng = random.Random(seed)
    p = Part(name)
    h = 0.40                          # altura da borda; a roupa sobe até ~0,55
    w0, d0, w1, d1 = 0.38, 0.28, 0.448, 0.348   # fundo e boca do corpo

    def contorno_em(z, sobra=0.0):
        s = z / h
        w, d = w0 + (w1 - w0) * s + sobra, d0 + (d1 - d0) * s + sobra
        return retangulo_redondo(w, d, 0.06 + 0.012 * s + sobra / 2)

    # Corpo afunilado em anéis (mais largo em cima), com a trama na textura.
    casca(p, [(h * k / 4, contorno_em(h * k / 4)) for k in range(5)], "Lavanderia_Vime")
    # Faixas de vime mais escuras em volta, e a borda grossa em cima.
    for zc in (0.06, 0.27):
        casca(p, [(zc - 0.015, contorno_em(zc, 0.007)), (zc + 0.015, contorno_em(zc, 0.007))],
              "Lavanderia_Vime_Escuro")
    fora = retangulo_redondo(0.48, 0.38, 0.086)
    casca(p, [(h - 0.03, contorno_em(h - 0.03, 0.004)), (h - 0.005, fora), (h + 0.012, fora)],
          "Lavanderia_Vime_Escuro")
    # Alças: rasgos escuros nos dois lados curtos, deitados na parede inclinada.
    zal = 0.33
    xal = (w0 + (w1 - w0) * zal / h) / 2
    ang = math.degrees(math.atan2((w1 - w0) / 2, h))
    for sx in (-1, 1):
        p.box((sx * (xal + 0.002), 0, zal), (0.012, 0.11, 0.032), "Sorteio_Chassi", bevel=0.008,
              rot=(0, sx * ang, 0))
    # Roupa: uma camada que enche a boca toda e montes por cima, cada um de uma cor.
    def cama(u, v):
        return h + 0.03 + 0.012 * math.sin(11 * u + 2) * math.cos(9 * v)

    pano(p, (0, 0, 0), 0.43, 0.33, cama, "Lavanderia_Roupa_Cinza", nx=8, ny=6, espessura=0.03)
    montes = [(-0.10, -0.05, 0.20, 0.16, 0.08, "Lavanderia_Roupa_Bege"),
              (0.09, 0.06, 0.22, 0.17, 0.10, "Lavanderia_Roupa_Rosa"),
              (0.10, -0.07, 0.18, 0.14, 0.07, "Lavanderia_Roupa_Verde"),
              (-0.07, 0.07, 0.20, 0.15, 0.11, "Lavanderia_Roupa_Branca"),
              (-0.01, -0.01, 0.17, 0.14, 0.125, "Lavanderia_Roupa_Azul")]
    for cx, cy, sx, sy, alto, cor in montes:
        fase = rng.uniform(0, 6.28)

        def alt(u, v, alto=alto, fase=fase):
            bossa = max(0.0, math.cos(math.pi * u) * math.cos(math.pi * v)) ** 0.7
            ruga = 0.25 * math.sin(10 * u + 6 * v + fase) * math.cos(7 * v - fase)
            return h + 0.02 + alto * bossa * (0.85 + ruga)

        pano(p, (cx, cy, 0), sx, sy, alt, cor, giro=rng.uniform(-30, 30), nx=8, ny=6,
             espessura=0.02)
    # Duas peças penduradas para fora da borda: manga de camisa e ponta de toalha.
    fita(p, [(0.08, -0.08, h + 0.07), (0.13, -0.16, h + 0.05), (0.155, -0.202, h + 0.025),
             (0.17, -0.207, h - 0.06), (0.16, -0.200, h - 0.15)],
         [0.07, 0.075, 0.075, 0.07, 0.06], 0.014, "Lavanderia_Roupa_Azul", larg_dir=(1, 0, 0))
    fita(p, [(-0.12, 0.02, h + 0.06), (-0.205, 0.03, h + 0.035), (-0.252, 0.035, h + 0.012),
             (-0.256, 0.04, h - 0.06), (-0.248, 0.045, h - 0.17)],
         0.12, 0.012, "Lavanderia_Roupa_Branca", larg_dir=(0, 1, 0))
    return p.to_object()


def roupa_no_chao(name="RoupaNoChao", seed=21):
    """Pilha baixa de roupa largada no chão: 0,80 x 0,15 x 0,60 m."""
    rng = random.Random(seed)
    p = Part(name)
    pecas = [
        # (centro x, y, tamanho x, y, altura do topo, giro, cor)
        (-0.06, 0.02, 0.62, 0.42, 0.07, 12, "Lavanderia_Roupa_Azul"),
        (0.14, -0.06, 0.44, 0.34, 0.10, -25, "Lavanderia_Roupa_Bege"),
        (-0.16, 0.08, 0.36, 0.30, 0.13, 40, "Lavanderia_Roupa_Rosa"),
        (0.04, 0.10, 0.30, 0.26, 0.165, 70, "Lavanderia_Roupa_Cinza"),
        (0.22, 0.12, 0.26, 0.22, 0.08, 15, "Lavanderia_Roupa_Verde"),
    ]
    for cx, cy, sx, sy, topo, giro, cor in pecas:
        fase = rng.uniform(0, 6.28)
        f2 = rng.uniform(4, 8)

        def alt(u, v, topo=topo, fase=fase, f2=f2):
            # Platô de beirada caída (mais quadrado que um domo) amassado em dobras.
            borda = max(0.0, 1 - max(abs(2 * u), abs(2 * v)) ** 3)
            dobra = 0.6 + 0.4 * math.sin(f2 * u + 2 * v + fase) * math.cos(6 * v - 0.5 * fase)
            return 0.006 + (topo - 0.006) * borda * dobra

        pano(p, (cx, cy, 0), sx, sy, alt, cor, giro=giro, nx=12, ny=10, espessura=0.012)
    # Uma manga largada no chão saindo da pilha.
    fita(p, [(-0.30, -0.10, 0.03), (-0.34, -0.18, 0.015), (-0.30, -0.26, 0.012), (-0.22, -0.28, 0.012)],
         0.09, 0.02, "Lavanderia_Roupa_Branca", deitada=True)
    return p.to_object()


# --------------------------------------------------------------------------- Estoque

def palete(name="Palete"):
    """Palete de madeira 1,20 x 0,15 x 0,80 m: 7 tábuas em cima, 3 travessas com rasgos para a
    empilhadeira e 3 tábuas embaixo."""
    p = Part(name)
    clara, escura = "Estoque_Madeira_Palete", "Estoque_Madeira_Palete_Escura"
    tb = 0.022                        # espessura das tábuas
    ht = 0.15 - 2 * tb                # altura das travessas
    # Tábuas de baixo, no sentido do fundo (Y), nas pontas e no meio.
    for i, x in enumerate((-0.53, 0.0, 0.53)):
        p.box((x, 0, tb / 2), (0.14, 0.80, tb), escura if i == 1 else clara, bevel=0.003)
    # Travessas no sentido da largura (X), com dois rasgos embaixo.
    rasgo = 0.045
    for i, y in enumerate((-0.35, 0.0, 0.35)):
        cor = escura if i == 1 else clara
        p.box((0, y, tb + rasgo + (ht - rasgo) / 2), (1.20, 0.10, ht - rasgo), cor, bevel=0.003)
        for x in (-0.53, 0.0, 0.53):
            p.box((x, y, tb + rasgo / 2 + 0.001), (0.14, 0.096, rasgo + 0.002), cor, bevel=0.003)
    # Tábuas de cima, no sentido do fundo, alternando os tons.
    xs = [-0.53, -0.36, -0.18, 0.0, 0.18, 0.36, 0.53]
    for i, x in enumerate(xs):
        w = 0.14 if i in (0, 3, 6) else 0.10
        p.box((x, 0, 0.15 - tb / 2), (w, 0.80, tb), escura if i % 3 == 1 else clara, bevel=0.003)
    return p.to_object()


def caixa_papelao(p, base, size, giro=0.0):
    """Caixa de papelão com fita na tampa, apoiada em base (x, y, z)."""
    bw, bd, bh = size
    c = (base[0], base[1], base[2] + bh / 2)
    p.box(c, (bw, bd, bh), "Papelao", bevel=0.006, rot=(0, 0, giro))
    p.box((c[0], c[1], c[2] + bh / 2 + 0.0008), (0.05, bd + 0.002, 0.0016), "Fita_Adesiva",
          bevel=0.0, rot=(0, 0, giro))
    g = math.radians(giro)
    for sy in (-1, 1):
        dx, dy = -math.sin(g) * sy * (bd / 2 + 0.0008), math.cos(g) * sy * (bd / 2 + 0.0008)
        p.box((c[0] + dx, c[1] + dy, c[2] + bh / 2 - 0.05), (0.05, 0.0016, 0.1), "Fita_Adesiva",
              bevel=0.0, rot=(0, 0, giro))


def carrinho_de_estoque(name="CarrinhoDeEstoque"):
    """Carrinho de plataforma 1,00 x 0,90 x 0,60 m: chapa cinza, alça azul de um lado, quatro
    rodízios e duas caixas de papelão em cima."""
    p = Part(name)
    chapa = "Estoque_Chapa_Cinza"
    zd0, zd1 = 0.15, 0.18             # fundo e topo da plataforma
    xd0, xd1 = -0.50, 0.45
    # Plataforma com aba dobrada em volta e frisos antiderrapantes.
    p.box(((xd0 + xd1) / 2, 0, (zd0 + zd1) / 2), (xd1 - xd0, 0.60, zd1 - zd0), chapa, bevel=0.006)
    for k in range(7):
        y = -0.24 + k * 0.08
        p.box(((xd0 + xd1) / 2, y, zd1 + 0.002), (xd1 - xd0 - 0.08, 0.012, 0.004),
              "Estoque_Rodizio", bevel=0.0)
    # Rodízios: chapa de fixação, garfo, roda.
    for x in (-0.40, 0.36):
        for y in (-0.22, 0.22):
            p.box((x, y, zd0 - 0.004), (0.08, 0.08, 0.008), "Estoque_Zincado", bevel=0.002)
            p.cylinder((x, y, zd0 - 0.03), 0.026, 0.022, "Estoque_Zincado", segments=10)
            for sy in (-1, 1):
                p.box((x + 0.01, y + sy * 0.024, 0.085), (0.05, 0.006, 0.08), "Estoque_Zincado",
                      bevel=0.002)
            p.cylinder((x + 0.012, y + 0.019, 0.05), 0.038, 0.05, "Estoque_Rodizio", segments=16,
                       rot=(90, 0, 0))
            p.cylinder((x + 0.012, y + 0.021, 0.05), 0.042, 0.016, "Estoque_Zincado", segments=8,
                       rot=(90, 0, 0))
    # Alça tubular azul em U, de pé na ponta +X, com travessa no meio.
    r = 0.014
    xa = 0.485
    curva = []
    for k in range(5):
        a = math.radians(90 * k / 4)
        curva.append((xa, 0.24 - 0.06 + 0.06 * math.cos(a), 0.90 - r - 0.06 + 0.06 * math.sin(a)))
    lado = [(xa, 0.24, zd1)] + curva
    outro = [(x, -y, z) for x, y, z in reversed(curva)] + [(xa, -0.24, zd1)]
    p.tube(lado + outro, r, "Estoque_Azul_Alca", segments=10)
    p.tube([(xa, -0.24, 0.55), (xa, 0.24, 0.55)], r * 0.85, "Estoque_Azul_Alca", segments=10)
    for y in (-0.24, 0.24):
        p.box((xa - 0.02, y, zd1 + 0.02), (0.07, 0.05, 0.04), "Estoque_Azul_Alca", bevel=0.004)
    # Duas caixas de papelão em cima.
    caixa_papelao(p, (-0.24, -0.01, zd1 + 0.004), (0.46, 0.40, 0.34), giro=3)
    caixa_papelao(p, (0.22, 0.04, zd1 + 0.004), (0.34, 0.32, 0.42), giro=-7)
    return p.to_object()


# --------------------------------------------------------------------------- Lote

def cesto_com_textura():
    # A textura nasce antes do material, para a foto rápida mostrar a cor média dela.
    write_texture("Lavanderia_Vime_Trama", 512, vime_trama)
    return cesto_de_roupa()


BUILDERS = [
    carro,
    faixa_concorra,
    poste_de_fila,
    cesto_com_textura,
    roupa_no_chao,
    palete,
    carrinho_de_estoque,
]

if __name__ == "__main__":
    main_guard(lambda: run_lote("sorteio_estoque", BUILDERS, PALETA))
