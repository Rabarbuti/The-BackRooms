"""
Lote "estofados": sofás e poltronas da loja de móveis do Level 1 (Docs/level1-mostruario.md).
Sofá, namoradeira e poltrona de veludo marrom listrado (anos 70/80) e duas poltronas
reclináveis de loja (bege-caramelo e azul-acinzentado).

    blender -b --factory-startup -P Tools/Blender/lotes/estofados.py [-- --fotos]

As peças, a paleta comum e a exportação vêm de Tools/Blender/moveis_base.py.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from moveis_base import Part, main_guard, run_lote, write_texture  # noqa: E402

# --------------------------------------------------------------------------- Paleta e textura

PALETA = {
    "Veludo_Marrom_Listrado": ((255, 255, 255), 0.9, 0.0, "Veludo_Listrado_Marrom"),
    "Tecido_Caramelo": ((190, 152, 104), 0.93, 0.0),
    "Tecido_Azul_Gasto": ((84, 104, 126), 0.95, 0.0),
}

LISTRAS_POR_METRO = 9
CLARO = (150, 88, 40)
ESCURO = (105, 58, 26)
_textura_pronta = False


def _hash(x, y):
    """Ruído determinístico em 0-1 (seno com fração, sem aleatório)."""
    s = math.sin(x * 127.1 + y * 311.7) * 43758.5453
    return s - math.floor(s)


def _pixel_listrado(u, v):
    # Listras só em u, com borda suave (tanh) entre o marrom claro e o escuro.
    t = 0.5 + 0.5 * math.tanh(math.sin(2 * math.pi * LISTRAS_POR_METRO * u) * 2.5)
    ruido = (_hash(u * 128, v * 128) - 0.5) * 9.0
    return tuple(max(0.0, min(255.0, e + (c - e) * t + ruido)) for c, e in zip(CLARO, ESCURO))


def garantir_textura():
    """Grava a textura uma vez, antes do primeiro móvel que a usa."""
    global _textura_pronta
    if not _textura_pronta:
        write_texture("Veludo_Listrado_Marrom", 128, _pixel_listrado)
        _textura_pronta = True


# --------------------------------------------------------------------------- Móveis
# Espaço do móvel: X para a direita, -Y é a frente, Z para cima, origem no chão no meio.

def estofado_listrado(name, seats, seat_w, back_h, d=0.95, seed=1):
    """Sofá/namoradeira/poltrona anos 70 de veludo listrado: base estofada quase até o chão,
    braços grossos com rolo na frente, encosto alto e almofadas soltas."""
    garantir_textura()
    fab = "Veludo_Marrom_Listrado"
    wood = "Madeira_Escura"
    p = Part(name)
    p.mat(fab)  # o tecido é o material 0: as faces novas do chanfro herdam o índice 0
    arm = 0.24
    w = seats * seat_w + 2 * arm
    rng = random.Random(seed)
    inner = w - 2 * arm

    # Pés curtos, quase escondidos pela base.
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.cylinder((sx * (w / 2 - 0.1), sy * (d / 2 - 0.1), 0), 0.05, 0.03, wood, segments=8)
    # Base estofada (z 0.04 a 0.30).
    p.box((0, 0, 0.17), (w, d, 0.26), fab, bevel=0.04, segments=2)
    # Encosto alto e estufado, atrás.
    p.box((0, d / 2 - 0.14, back_h / 2 + 0.1), (w, 0.28, back_h - 0.2), fab, bevel=0.08, segments=3)
    # Braços: caixa grossa + rolo deitado no eixo Y (a ponta vira o "caracol" da frente).
    for sx in (-1, 1):
        x = sx * (w / 2 - arm / 2)
        p.box((x, 0, 0.27), (arm, d, 0.46), fab, bevel=0.06, segments=3)
        p.cylinder((x, d / 2 - 0.01, 0.50), d - 0.02, arm / 2, fab, segments=14, rot=(90, 0, 0))
    # Almofadas do assento e das costas, uma por lugar, levemente desiguais.
    seat_d = d - 0.28 - 0.04
    for i in range(seats):
        x = (i - (seats - 1) / 2) * seat_w
        sag = rng.uniform(-0.012, 0.0)
        p.box((x, -d / 2 + 0.02 + seat_d / 2, 0.38 + sag), (seat_w - 0.01, seat_d, 0.16), fab,
              bevel=0.06, segments=3, rot=(rng.uniform(-1, 1), 0, rng.uniform(-1.2, 1.2)))
        bh = back_h - 0.50
        p.box((x, d / 2 - 0.28 - 0.08, 0.44 + bh / 2 + 0.02), (seat_w - 0.02, 0.2, bh), fab,
              bevel=0.07, segments=3,
              rot=(-10 + rng.uniform(-3, 2), 0, rng.uniform(-2, 2)))
    return p.to_object()


def reclinavel(name, fabric, seed=5):
    """Poltrona reclinável de loja: volumosa, braços grossos com almofada, encosto de gomos,
    apoio de pés recolhido embaixo da frente do assento e alavanca na lateral direita."""
    p = Part(name)
    p.mat(fabric)  # tecido como material 0 (faces do chanfro nascem com o índice 0)
    w, d = 0.88, 0.95
    arm = 0.22
    rng = random.Random(seed)
    inner = w - 2 * arm
    # Pés pretos curtos.
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.cylinder((sx * (w / 2 - 0.09), sy * (d / 2 - 0.09), 0), 0.07, 0.03, "Plastico_Preto", segments=8)
    # Base.
    p.box((0, 0.04, 0.19), (w, d - 0.1, 0.26), fabric, bevel=0.05, segments=3)
    # Braços grossos, com almofada de apoio por cima.
    for sx in (-1, 1):
        x = sx * (w / 2 - arm / 2)
        p.box((x, 0, 0.36), (arm, d, 0.60), fabric, bevel=0.08, segments=3)
        p.box((x, -0.02, 0.665), (arm + 0.01, d - 0.1, 0.11), fabric, bevel=0.05, segments=3)
    # Apoio de pés recolhido: painel na frente, embaixo do assento, entre os braços.
    p.box((0, -d / 2 + 0.05, 0.22), (inner - 0.01, 0.09, 0.26), fabric, bevel=0.03, segments=2)
    # Assento fofo.
    p.box((0, -0.12, 0.395), (inner, 0.64, 0.17), fabric, bevel=0.07, segments=3,
          rot=(rng.uniform(-1, 1), 0, 0))
    # Encosto: moldura atrás + três gomos horizontais estufados, levemente inclinados.
    p.box((0, d / 2 - 0.12, 0.60), (w, 0.24, 0.80), fabric, bevel=0.08, segments=3)
    for i, z in enumerate((0.58, 0.77, 0.95)):
        p.box((0, d / 2 - 0.27, z), (inner + 0.02, 0.15, 0.21), fabric, bevel=0.07, segments=3,
              rot=(-11 + rng.uniform(-1.5, 1.5), 0, rng.uniform(-1, 1)))
    # Alavanca na lateral direita (+X): haste curta e cabo, sob a almofada do braço.
    xr = w / 2
    p.cylinder((xr - 0.005, -0.08, 0.30), 0.04, 0.011, "Plastico_Preto", segments=8, rot=(0, 90, 0))
    p.box((xr + 0.04, -0.13, 0.30), (0.018, 0.12, 0.03), "Plastico_Preto", bevel=0.006)
    return p.to_object()


# --------------------------------------------------------------------------- Lote

BUILDERS = [
    lambda: estofado_listrado("SofaMarromListrado", 3, 0.54, 0.90, seed=3),
    lambda: estofado_listrado("NamoradeiraMarromListrada", 2, 0.51, 0.90, seed=4),
    lambda: estofado_listrado("PoltronaMarromListrada", 1, 0.52, 0.98, d=0.92, seed=5),
    lambda: reclinavel("PoltronaReclinavelBege", "Tecido_Caramelo", seed=6),
    lambda: reclinavel("PoltronaReclinavelAzul", "Tecido_Azul_Gasto", seed=7),
]

if __name__ == "__main__":
    main_guard(lambda: run_lote("estofados", BUILDERS, PALETA))
