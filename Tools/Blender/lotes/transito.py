"""
Lote "transito": placas de trânsito do Level 1 (jogadas no chão ou pregadas num pilar, e em pé
num poste). Cada placa tem duas versões: só a chapa (sem poste, com a borda de baixo em z = 0)
e com poste (base do poste em z = 0, centro da placa a 2,2 m).

    blender -b --factory-startup -P Tools/Blender/lotes/transito.py [-- --fotos]

Construção: chapa de metal de 3 mm com a frente para -Y; o desenho é feito de camadas de 1 mm
empilhadas na frente (cada uma menor que a anterior); o verso fica cinza liso.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from moveis_base import Part, main_guard, run_lote  # noqa: E402

PALETA = {
    "Placa_Vermelha": ((188, 24, 28), 0.45, 0.0),
    "Placa_Amarela": ((238, 188, 18), 0.45, 0.0),
    "Placa_Branca": ((232, 232, 224), 0.45, 0.0),
    "Placa_Preta": ((18, 18, 20), 0.5, 0.0),
    "Metal_Galvanizado": ((150, 156, 158), 0.38, 0.75),
}

ESP = 0.003        # espessura da chapa
CAMADA = 0.001     # espessura de cada camada do desenho
ALTURA_POSTE = 2.2  # do chão ao centro da placa


# --------------------------------------------------------------------------- Peças comuns

def lamina(p, contorno, y0, y1, material, zc):
    """Contorno 2D (x, z) extrudado entre y0 e y1 (y0 < y1), de pé, centrado em z = zc.
    O prism extruda em z e rot=(90,0,0) leva o z do prisma para -Y."""
    p.prism(contorno, -y1, -y0, material, rot=(90.0, 0.0, 0.0), offset=(0.0, 0.0, zc))


def octogono(apotema):
    """Octógono regular com lado plano em cima (apótema = meia largura de lado a lado)."""
    r = apotema / math.cos(math.radians(22.5))
    return [(r * math.cos(math.radians(22.5 + 45 * k)), r * math.sin(math.radians(22.5 + 45 * k))) for k in range(8)]


def retangulo(w, h):
    return [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]


def losango(apotema):
    """Quadrado de lado 2*apotema girado 45 graus (ponta para cima)."""
    d = apotema * math.sqrt(2.0)
    return [(0, -d), (d, 0), (0, d), (-d, 0)]


def furo_de_bala(p, x, z, y_frente, zc, raio=0.008):
    """Furo de bala: disco escuro raso, assentado na frente (y_frente) da placa."""
    p.cylinder((x, y_frente, zc + z), 0.0008, raio, "Metal_Escuro", segments=8, rot=(90.0, 0.0, 0.0))
    p.cylinder((x, y_frente - 0.0008, zc + z), 0.0004, raio * 1.7, "Metal_Escuro", segments=8,
               radius_top=raio * 1.1, rot=(90.0, 0.0, 0.0))


def montar_placa(nome, poste, desenho, chapa, altura_meia, furos=()):
    """Monta chapa + desenho (+ poste e abraçadeiras). `desenho(p, zc, y)` empilha as camadas a
    partir de y (face da frente da chapa) e devolve o y da camada mais da frente."""
    p = Part(nome)
    zc = ALTURA_POSTE if poste else altura_meia
    lamina(p, chapa, -ESP / 2, ESP / 2, "Metal_Pintado", zc)
    y_ponta = desenho(p, zc, -ESP / 2)
    for (fx, fz) in furos:
        furo_de_bala(p, fx, fz, y_ponta, zc)
    if poste:
        r = 0.025
        y_poste = ESP / 2 + 0.004 + r  # o poste fica atrás da chapa
        topo = zc + min(altura_meia * 0.8, 0.22)
        p.cylinder((0, y_poste, 0.0), topo - 0.0, r, "Metal_Galvanizado", segments=12)
        p.cylinder((0, y_poste, topo), 0.012, r * 1.05, "Metal_Galvanizado", segments=12)  # tampa
        # Duas abraçadeiras: cinta em volta do poste, calço até a chapa e parafuso na frente.
        for dz in (-0.12, 0.12):
            z = zc + dz
            p.cylinder((0, y_poste, z - 0.016), 0.032, r + 0.004, "Metal_Galvanizado", segments=12)
            p.box((0, (ESP / 2 + y_poste) / 2 - 0.002, z), (0.045, y_poste - ESP / 2 - 0.004, 0.028),
                  "Metal_Galvanizado", bevel=0.002)
    return p.to_object()


# --------------------------------------------------------------------------- 1. PARE

def placa_pare(poste=False, texto="PARE", base="PlacaPare"):
    """Octógono de parada. texto="STOP", base="PlacaStop" faz a versão em inglês."""
    nome = base + ("Poste" if poste else "")
    a = 0.30  # apótema: 0,60 m de lado a lado

    def desenho(p, zc, y):
        lamina(p, octogono(a), y - CAMADA, y, "Placa_Branca", zc)
        y -= CAMADA
        lamina(p, octogono(a - 0.022), y - CAMADA, y, "Placa_Vermelha", zc)
        y -= CAMADA
        # Letras brancas em relevo de 1 mm (extrude é para os dois lados do centro).
        # Resolução 4: as letras grandes e curvas (S, O, R) ficavam facetadas com 2.
        p.text(texto, (0.0, y - CAMADA / 2, zc), 0.165, "Placa_Branca", depth=CAMADA / 2, resolution=4)
        return y - CAMADA

    # Dois furos de bala só na versão sem poste (a que cai no chão).
    furos = () if poste else ((0.17, 0.105), (-0.2, -0.13))
    return montar_placa(nome, poste, desenho, octogono(a), a, furos)


# --------------------------------------------------------------------------- 2. Setas

def seta_chevron(x0, meia_altura=0.135, fundo=0.115, traco=0.065):
    """Contorno de uma seta em ">" (chevron) com a base esquerda em x0."""
    h, d, s = meia_altura, fundo, traco
    return [(x0, -h), (x0 + s, -h), (x0 + d + s, 0.0), (x0 + s, h), (x0, h), (x0 + d, 0.0)]


def placa_setas(poste=False):
    nome = "PlacaSetasPoste" if poste else "PlacaSetas"
    w, h = 0.75, 0.45

    def desenho(p, zc, y):
        lamina(p, retangulo(w, h), y - CAMADA, y, "Placa_Amarela", zc)
        y -= CAMADA
        lamina(p, retangulo(w - 0.024, h - 0.024), y - CAMADA, y, "Placa_Preta", zc)
        y -= CAMADA
        lamina(p, retangulo(w - 0.07, h - 0.07), y - CAMADA, y, "Placa_Amarela", zc)
        y -= CAMADA
        passo = 0.17
        total = 2 * passo + 0.115 + 0.065
        for i in range(3):
            lamina(p, seta_chevron(-total / 2 + i * passo), y - CAMADA, y, "Placa_Preta", zc)
        return y - CAMADA

    return montar_placa(nome, poste, desenho, retangulo(w, h), h / 2)


# --------------------------------------------------------------------------- 3. Animal (cervo)

# Cervo de perfil olhando para a direita, em cm, x para a frente e z para cima, do rabo ao focinho.
# Contorno fechado, no sentido horário: dorso, pescoço, cabeça (com orelha), peito, pernas da
# frente, barriga, pernas de trás e garupa.
CERVO = [
    (-21, 6), (-17, 6.8), (-6, 7.2), (3, 6.8), (7, 9.5), (10.5, 15.5), (12.5, 17.5),   # rabo, dorso, pescoço
    (12.2, 21), (15.5, 19), (19, 17.2), (24, 12.5), (25.5, 10.5), (23.5, 9.2),          # orelha, testa, focinho
    (19.5, 12), (14.5, 6.5), (12, 1), (11.8, -3),                                        # queixo, garganta, peito
    (12, -8), (12, -15), (12.3, -16.5), (9, -16.5), (9, -15), (8.8, -8), (7.8, -5.5),   # perna da frente 1
    (6.8, -7), (7, -15), (7.3, -16.5), (4, -16.5), (4, -15), (3.8, -7), (2.5, -5.5),    # perna da frente 2
    (-6, -5), (-9, -5.5),                                                                # barriga
    (-9.5, -9), (-9.8, -15), (-9.5, -16.5), (-12.8, -16.5), (-12.8, -15),               # perna de trás 1
    (-13.4, -12), (-14, -8.5), (-15.5, -8.5), (-15.8, -12),                              # vão entre as pernas
    (-16.3, -15), (-16, -16.5), (-19.3, -16.5), (-19.3, -15), (-19.5, -12),             # perna de trás 2
    (-20.5, -3), (-21.5, 2),                                                             # garupa
]

# Chifres: cada galho é uma lista de pontos (cm); largura do traço em cm. Dois chifres, o
# da frente e o de trás (um pouco mais baixo e atrás, como num cervo de perfil).
CHIFRES = [
    [(14, 18.5), (12.5, 22.5), (13.5, 27)],
    [(12.5, 22.5), (17.5, 24.5)],
    [(13.2, 26), (9.5, 29.5)],
    [(13.5, 27), (17.5, 29.5)],
    [(13, 18.5), (8.5, 21.5), (7.8, 25.5), (9, 29.5)],
    [(8.5, 21.5), (13, 22.5)],
    [(7.8, 25.5), (4, 27.5)],
]
LARGURA_CHIFRE = 2.0


def cervo_ajustado(raio_max):
    """Centraliza e reduz o cervo (contorno e chifres) para caber no losango: o ponto mais
    longe, medido por |x| + |z|, fica em raio_max. Devolve (contorno, galhos, largura) em metros."""
    pontos = list(CERVO) + [q for g in CHIFRES for q in g]
    melhor = None
    # Procura o deslocamento que menos estoura o losango (grade grossa, determinística).
    for sx in range(-8, 9):
        for sz in range(-12, 13):
            m = max(abs(x - sx) + abs(z - sz) for x, z in pontos)
            if melhor is None or m < melhor[0]:
                melhor = (m, sx, sz)
    m, sx, sz = melhor
    k = raio_max / m  # cm do desenho -> metros na placa
    f = lambda q: ((q[0] - sx) * k, (q[1] - sz) * k)  # noqa: E731
    contorno = [f(q) for q in CERVO]
    galhos = [[f(q) for q in g] for g in CHIFRES]
    return contorno, galhos, LARGURA_CHIFRE * k


def area_assinada(c):
    return 0.5 * sum(c[i][0] * c[(i + 1) % len(c)][1] - c[(i + 1) % len(c)][0] * c[i][1] for i in range(len(c)))


def traco(p, a, b, largura, y0, y1, material, zc):
    """Traço reto de a até b (x, z) com a largura dada, como retângulo fino de pé."""
    dx, dz = b[0] - a[0], b[1] - a[1]
    comp = math.hypot(dx, dz) + largura * 0.5
    ang = math.degrees(math.atan2(dz, dx))
    centro = ((a[0] + b[0]) / 2, (y0 + y1) / 2, zc + (a[1] + b[1]) / 2)
    p.box(centro, (comp, y1 - y0, largura), material, bevel=0.0, rot=(0.0, -ang, 0.0))


def placa_animal(poste=False):
    nome = "PlacaAnimalPoste" if poste else "PlacaAnimal"
    a = 0.30  # lado de 0,60 m

    def desenho(p, zc, y):
        lamina(p, losango(a), y - CAMADA, y, "Placa_Amarela", zc)
        y -= CAMADA
        lamina(p, losango(a - 0.012), y - CAMADA, y, "Placa_Preta", zc)
        y -= CAMADA
        lamina(p, losango(a - 0.042), y - CAMADA, y, "Placa_Amarela", zc)
        y -= CAMADA
        contorno, galhos, largura = cervo_ajustado((a - 0.042) * math.sqrt(2.0) * 0.97)
        if area_assinada(contorno) < 0:
            contorno.reverse()
        lamina(p, contorno, y - CAMADA, y, "Placa_Preta", zc)
        for g in galhos:
            for q0, q1 in zip(g[:-1], g[1:]):
                traco(p, q0, q1, largura, y - CAMADA, y, "Placa_Preta", zc)
        return y - CAMADA

    furos = () if poste else ((-0.09, 0.07),)
    return montar_placa(nome, poste, desenho, losango(a), a * math.sqrt(2.0), furos)


# --------------------------------------------------------------------------- 4. Curva (seta dobrada)

def seta_curva(direita=True, passos=6):
    """Seta preta da placa de curva: haste que sobe, dobra com a quina de fora arredondada e
    segue na horizontal até a ponta de flecha. Medidas tiradas da placa de referência (losango
    de 0,60 m de lado). Contorno em (x, z), com a seta virando para a direita; a esquerda é o
    espelho em x."""
    x0, x1 = -0.18, -0.085          # laterais da haste
    base = -0.19                    # pé da haste
    baixo, cima = 0.048, 0.140      # faixa horizontal
    cabeca_x, ponta_x = 0.045, 0.245
    cabeca_baixo, cabeca_cima = -0.017, 0.207
    r = 0.07                        # raio da quina de fora
    pontos = [(x0, base), (x1, base), (x1, baixo), (cabeca_x, baixo), (cabeca_x, cabeca_baixo),
              (ponta_x, (baixo + cima) / 2), (cabeca_x, cabeca_cima), (cabeca_x, cima)]
    # Quina arredondada: de (x0 + r, cima) até (x0, cima - r), em volta do centro (x0 + r, cima - r).
    for i in range(passos + 1):
        ang = math.radians(90 + 90 * i / passos)
        pontos.append((x0 + r + r * math.cos(ang), cima - r + r * math.sin(ang)))
    if not direita:
        pontos = [(-x, z) for x, z in reversed(pontos)]
    return pontos


def placa_curva(direita=True, poste=False):
    """Placa amarela de advertência em losango, borda preta, seta de curva preta (como a placa
    de "curva à direita"). No Level 1 vai pregada na parede no lugar das setas pintadas do Level 0,
    apontando o caminho até a saída; a versão de poste aparece solta pela fase."""
    nome = "PlacaCurva" + ("Direita" if direita else "Esquerda") + ("Poste" if poste else "")
    a = 0.30

    def desenho(p, zc, y):
        lamina(p, losango(a), y - CAMADA, y, "Placa_Amarela", zc)
        y -= CAMADA
        lamina(p, losango(a - 0.012), y - CAMADA, y, "Placa_Preta", zc)
        y -= CAMADA
        lamina(p, losango(a - 0.03), y - CAMADA, y, "Placa_Amarela", zc)
        y -= CAMADA
        lamina(p, seta_curva(direita), y - CAMADA, y, "Placa_Preta", zc)
        return y - CAMADA

    return montar_placa(nome, poste, desenho, losango(a), a * math.sqrt(2.0))


# --------------------------------------------------------------------------- Lote

BUILDERS = [
    lambda: placa_pare(False),
    lambda: placa_pare(True),
    lambda: placa_setas(False),
    lambda: placa_setas(True),
    lambda: placa_animal(False),
    lambda: placa_animal(True),
    lambda: placa_pare(False, "STOP", "PlacaStop"),
    lambda: placa_pare(True, "STOP", "PlacaStop"),
    lambda: placa_curva(True),
    lambda: placa_curva(False),
    lambda: placa_curva(True, poste=True),
    lambda: placa_curva(False, poste=True),
]

if __name__ == "__main__":
    main_guard(lambda: run_lote("transito", BUILDERS, PALETA))
