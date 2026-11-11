"""
Lote "detalhes": móveis de acabamento mais trabalhado do Level 1 (Docs/level1-mostruario.md).
Cadeira de ripas, cadeira dobrável, cômoda, cabideiro, vaso, máquina de lavar e cilindro de gás.

    blender -b --factory-startup -P Tools/Blender/lotes/detalhes.py [-- --fotos]

As peças, a paleta comum e a exportação vêm de Tools/Blender/moveis_base.py.
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from moveis_base import Part, main_guard, run_lote  # noqa: E402

# Cores novas deste lote: nome -> (cor sRGB, rugosidade, metal).
PALETA = {
    "Madeira_Mel_Lisa": ((190, 142, 78), 0.55, 0.0),
    "Tecido_Cinza_Azulado": ((112, 128, 142), 0.92, 0.0),
    "Plastico_Azul": ((24, 38, 92), 0.45, 0.0),
    "Metal_Grafite": ((66, 70, 76), 0.4, 0.7),
    "Ceramica_Cobalto": ((24, 52, 150), 0.22, 0.0),
    "Esmalte_Vermelho": ((168, 32, 28), 0.3, 0.0),
    "Metal_Cromado": ((190, 194, 198), 0.2, 0.9),
    "Esmalte_Turquesa": ((20, 176, 184), 0.25, 0.0),
    "Borracha_Preta": ((16, 16, 18), 0.85, 0.0),
}


# --------------------------------------------------------------------------- Móveis
# Espaço do móvel: X para a direita, -Y é a frente, Z para cima, origem no chão no meio.

def cadeira_encosto_ripas(name="CadeiraEncostoRipas"):
    """Cadeira de jantar cor de mel: encosto de 4 ripas horizontais curvas, 0,45 x 0,5 x 0,95 m."""
    p = Part(name)
    wood = "Madeira_Mel_Lisa"
    leg = 0.034
    hx = 0.225 - leg / 2          # centro das pernas em X
    fy, by = -0.23, 0.19          # centro das pernas da frente e de trás em Y
    seat = 0.45
    tilt = 6.0                    # inclinação dos montantes traseiros para trás
    tan = math.tan(math.radians(tilt))

    for sx in (-1, 1):
        # Pernas da frente e de trás até o assento.
        p.box((sx * hx, fy, seat / 2), (leg, leg, seat), wood)
        p.box((sx * hx, by, seat / 2), (leg, leg, seat), wood)
        # Montante traseiro: continua para cima inclinado para trás.
        length = 0.50
        p.box((sx * hx, by + math.sin(math.radians(tilt)) * length / 2,
               seat + math.cos(math.radians(tilt)) * length / 2),
              (leg, 0.03, length), wood, rot=(-tilt, 0, 0))
        # Avental lateral e travessa baixa lateral.
        p.box((sx * hx, (fy + by) / 2, 0.425), (0.02, by - fy, 0.05), wood)
        p.box((sx * hx, (fy + by) / 2, 0.16), (0.02, by - fy, 0.025), wood)
    # Aventais da frente e de trás, travessa baixa da frente e tábua do assento.
    p.box((0, fy, 0.425), (2 * hx, 0.02, 0.05), wood)
    p.box((0, by, 0.425), (2 * hx, 0.02, 0.05), wood)
    p.box((0, fy, 0.16), (2 * hx, 0.02, 0.025), wood)
    p.box((0, (fy + by) / 2, 0.443), (0.40, by - fy, 0.02), wood)
    # Almofada fina estofada.
    p.box((0, -0.04, 0.472), (0.41, 0.40, 0.032), "Tecido_Cinza_Azulado", bevel=0.012)

    # Ripas horizontais levemente curvas (arco para trás no meio), acompanhando a inclinação.
    half = 0.195
    n = 7
    for z in (0.60, 0.69, 0.78, 0.87):
        front = []
        back = []
        for i in range(n):
            x = -half + 2 * half * i / (n - 1)
            bow = 0.012 * (1 - (x / half) ** 2)
            front.append((x, bow - 0.007))
            back.append((x, bow + 0.007))
        outline = front + list(reversed(back))
        y = by + tan * (z - seat)
        p.prism(outline, z - 0.0275, z + 0.0275, wood, offset=(0, y, 0))
    return p.to_object()


def cadeira_dobravel(name="CadeiraDobravel"):
    """Cadeira dobrável de festa: tubo grafite, assento e encosto de chapa azul, 0,45 x 0,5 x 0,8 m."""
    p = Part(name)
    metal, plastic = "Metal_Grafite", "Plastico_Azul"
    r = 0.011
    zf = 0.007
    for sx in (-1, 1):
        xa, xb = sx * 0.215, sx * 0.193
        # Perna A: do pé da frente até o trilho do assento, cruzando a perna B.
        p.tube([(xa, -0.23, zf), (xa, 0.10, 0.44)], r, metal)
        # Perna B: do pé de trás até a frente do assento.
        p.tube([(xb, 0.24, zf), (xb, -0.18, 0.44)], r, metal)
        # Trilho do assento e montante do encosto, inclinado para trás.
        p.tube([(xa, -0.20, 0.44), (xa, 0.19, 0.44), (xa, 0.25, 0.80)], r, metal)
    # Travessas entre os dois lados.
    p.tube([(-0.215, -0.23, zf), (0.215, -0.23, zf)], r, metal)
    p.tube([(-0.193, 0.24, zf), (0.193, 0.24, zf)], r, metal)
    p.tube([(-0.2, -0.18, 0.425), (0.2, -0.18, 0.425)], r, metal)
    p.tube([(-0.215, 0.10, 0.425), (0.215, 0.10, 0.425)], r, metal)
    p.tube([(-0.215, 0.25, 0.80), (0.215, 0.25, 0.80)], r, metal)
    # Assento e encosto de chapa, com quinas arredondadas.
    p.box((0, -0.02, 0.463), (0.43, 0.41, 0.02), plastic, bevel=0.01)
    ang = math.degrees(math.atan2(0.06, 0.36))
    zc = 0.67
    yc = 0.19 + 0.06 * (zc - 0.44) / 0.36 + 0.001
    p.box((0, yc, zc), (0.40, 0.014, 0.20), plastic, bevel=0.006, rot=(-ang, 0, 0))
    return p.to_object()


def comoda(name="Comoda"):
    """Cômoda de madeira escura de 4 gavetas: 0,9 x 0,45 x 0,85 m."""
    p = Part(name)
    wood = "Madeira_Escura"
    # Pés torneados curtos.
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * 0.375, sy * 0.165
            p.cylinder((x, y, 0.0), 0.055, 0.022, wood, segments=10, radius_top=0.03)
            p.cylinder((x, y, 0.055), 0.05, 0.03, wood, segments=10, radius_top=0.038)
    # Rodapé entre os pés, corpo e tampo (sobra 1,5 cm em volta).
    p.box((0, 0, 0.08), (0.80, 0.37, 0.04), wood)
    p.box((0, 0, 0.46), (0.87, 0.42, 0.72), wood, bevel=0.004)
    p.box((0, 0, 0.835), (0.90, 0.45, 0.03), wood, bevel=0.007)
    # Frentes das gavetas em leve relevo, cada uma com 2 puxadores de latão.
    pitch, h = 0.1725, 0.16
    for i in range(4):
        zc = 0.115 + h / 2 + i * pitch + 0.0
        p.box((0, -0.216, zc), (0.82, 0.014, h), wood, bevel=0.004)
        for sx in (-1, 1):
            p.cylinder((sx * 0.22, -0.223, zc), 0.022, 0.012, "Latao", segments=8,
                       radius_top=0.018, rot=(90, 0, 0))
            p.cylinder((sx * 0.22, -0.245, zc), 0.008, 0.02, "Latao", segments=8,
                       radius_top=0.02, rot=(90, 0, 0))
    return p.to_object()


def cabideiro(name="CabideiroDePe"):
    """Cabideiro de pé: haste torneada de 1,8 m, base em cruz, 6 ganchos curvos."""
    p = Part(name)
    wood = "Madeira_Escura"
    # Base em cruz girada 45 graus, com um bloco no centro.
    p.box((0, 0, 0.02), (0.52, 0.055, 0.04), wood, bevel=0.01, rot=(0, 0, 45))
    p.box((0, 0, 0.02), (0.52, 0.055, 0.04), wood, bevel=0.01, rot=(0, 0, -45))
    # Haste torneada.
    p.cylinder((0, 0, 0.04), 0.07, 0.05, wood, segments=12, radius_top=0.032)
    p.cylinder((0, 0, 0.11), 0.30, 0.032, wood, segments=12, radius_top=0.022)
    p.cylinder((0, 0, 0.41), 0.03, 0.034, wood, segments=12, radius_top=0.034)
    p.cylinder((0, 0, 0.44), 1.06, 0.022, wood, segments=12, radius_top=0.02)
    p.cylinder((0, 0, 1.50), 0.04, 0.036, wood, segments=12, radius_top=0.036)
    p.cylinder((0, 0, 1.54), 0.16, 0.024, wood, segments=12, radius_top=0.024)
    p.cylinder((0, 0, 1.70), 0.05, 0.03, wood, segments=12, radius_top=0.036)
    p.cylinder((0, 0, 1.75), 0.05, 0.036, wood, segments=12, radius_top=0.01)
    # Seis ganchos curvos, alternando a altura.
    for k in range(6):
        a = math.radians(k * 60 + 15)
        z = 1.58 if k % 2 == 0 else 1.66
        pts = []
        for r, dz in ((0.015, 0.0), (0.07, 0.006), (0.115, 0.03), (0.13, 0.08)):
            pts.append((math.cos(a) * r, math.sin(a) * r, z + dz))
        p.tube(pts, 0.009, wood, segments=6)
    return p.to_object()


def vaso_alto(name="VasoAlto"):
    """Vaso de chão azul-cobalto em perfil de ânfora: 0,9 m."""
    p = Part(name)
    mat = "Ceramica_Cobalto"
    # Perfil (altura, raio): cada fatia é um tronco de cone entre dois pontos.
    perfil = [(0.00, 0.085), (0.04, 0.105), (0.08, 0.095), (0.18, 0.165), (0.32, 0.215),
              (0.46, 0.225), (0.58, 0.195), (0.68, 0.125), (0.76, 0.072), (0.80, 0.066),
              (0.84, 0.078), (0.88, 0.108), (0.90, 0.118)]
    for (z0, r0), (z1, r1) in zip(perfil[:-1], perfil[1:]):
        p.cylinder((0, 0, z0), z1 - z0, r0, mat, segments=16, radius_top=r1)
    # Boca aberta: o miolo escuro encosta no topo para parecer um buraco.
    p.cylinder((0, 0, 0.9), 0.002, 0.092, "Metal_Escuro", segments=16)
    return p.to_object()


def maquina_de_lavar(name="MaquinaDeLavar"):
    """Lavadora de abertura frontal, carcaça vermelha esmaltada: 0,6 x 0,6 x 0,85 m."""
    p = Part(name)
    red = "Esmalte_Vermelho"
    face = -0.26                  # y da frente da carcaça
    # Pezinhos e carcaça.
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.cylinder((sx * 0.24, 0.02 + sy * 0.22, 0.0), 0.02, 0.025, "Plastico_Preto", segments=8)
    p.box((0, 0.02, 0.435), (0.60, 0.56, 0.83), red, bevel=0.012, segments=2)
    # Porta redonda: aro cinza, degrau e vidro escuro levemente abaulado.
    zc = 0.37
    p.cylinder((0, face, zc), 0.045, 0.215, "Metal_Pintado", segments=24, rot=(90, 0, 0))
    p.cylinder((0, face - 0.04, zc), 0.012, 0.19, "Metal_Cromado", segments=24, rot=(90, 0, 0))
    p.cylinder((0, face - 0.05, zc), 0.025, 0.16, "Vidro_Tela", segments=24,
               radius_top=0.12, rot=(90, 0, 0))
    # Puxador da porta.
    p.box((0.2, face - 0.04, zc + 0.14), (0.025, 0.02, 0.07), "Metal_Cromado", bevel=0.004)
    # Painel de comandos em cima: gaveta de sabão, 2 botões giratórios e luz.
    p.box((0, face - 0.01, 0.775), (0.54, 0.022, 0.11), "Plastico_Bege", bevel=0.004)
    p.box((-0.17, face - 0.023, 0.775), (0.15, 0.006, 0.06), "Metal_Pintado", bevel=0.002)
    for sx in (0.04, 0.17):
        p.cylinder((sx, face - 0.021, 0.775), 0.028, 0.03, "Plastico_Preto", segments=14,
                   radius_top=0.026, rot=(90, 0, 0))
        p.box((sx, face - 0.05, 0.775 + 0.012), (0.005, 0.002, 0.016), "Tinta_Branca", bevel=0.0)
    p.cylinder((0.23, face - 0.021, 0.805), 0.006, 0.008, "Tinta_Laranja", segments=8, rot=(90, 0, 0))
    return p.to_object()


def cilindro_azul(name="CilindroAzul"):
    """Cilindro de gás/extintor alto, azul-turquesa: 0,25 x 0,25 x 1,1 m."""
    p = Part(name)
    body = "Esmalte_Turquesa"
    r = 0.125
    p.cylinder((0, 0, 0.0), 0.04, 0.13, "Borracha_Preta", segments=16)
    p.cylinder((0, 0, 0.04), 0.81, r, body, segments=16)
    # Faixa branca de identificação, um fio acima do corpo.
    p.cylinder((0, 0, 0.5), 0.12, r + 0.0012, "Tinta_Branca", segments=16)
    # Ombro arredondado em troncos de cone.
    ombro = [(0.85, r), (0.89, 0.121), (0.93, 0.108), (0.97, 0.085), (1.00, 0.055), (1.02, 0.036)]
    for (z0, r0), (z1, r1) in zip(ombro[:-1], ombro[1:]):
        p.cylinder((0, 0, z0), z1 - z0, r0, body, segments=16, radius_top=r1)
    # Válvula metálica: colar, corpo, saída lateral e volante.
    p.cylinder((0, 0, 1.02), 0.02, 0.036, "Metal_Cromado", segments=12)
    p.cylinder((0, 0, 1.04), 0.04, 0.026, "Metal_Cromado", segments=12, radius_top=0.022)
    p.cylinder((0.0, 0.0, 1.056), 0.07, 0.012, "Metal_Cromado", segments=8, rot=(0, 90, 0))
    p.cylinder((0, 0, 1.08), 0.012, 0.042, "Metal_Cromado", segments=12)
    p.cylinder((0, 0, 1.092), 0.008, 0.012, "Metal_Cromado", segments=8)
    return p.to_object()


# --------------------------------------------------------------------------- Lote

BUILDERS = [
    cadeira_encosto_ripas,
    cadeira_dobravel,
    comoda,
    cabideiro,
    vaso_alto,
    maquina_de_lavar,
    cilindro_azul,
]

if __name__ == "__main__":
    main_guard(lambda: run_lote("detalhes", BUILDERS, PALETA))
