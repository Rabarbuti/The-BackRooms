"""
Lote "loja": o salão vazio de uma loja/biblioteca abandonada do Level 1 (Docs/level1-mostruario.md).
Estantes de madeira cor de mel, torre expositora, balcão curvo com monitor de tubo, mesa simples
e as placas grandes penduradas do teto.

    blender -b --factory-startup -P Tools/Blender/lotes/loja.py [-- --fotos]

As peças, a paleta comum e a exportação vêm de Tools/Blender/moveis_base.py.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from moveis_base import Part, main_guard, run_lote, write_texture  # noqa: E402

# --------------------------------------------------------------------------- Paleta e textura

MEL = "Madeira_Mel"
ESCURA = "Madeira_Escura"  # rodapés (da paleta comum)

PALETA = {
    # Cor branca: quem pinta é a textura de veio (gravada por textura_mel).
    MEL: ((255, 255, 255), 0.55, 0.0, "Madeira_Mel_Veio"),
}

_textura_pronta = False


def textura_mel():
    """Veio de madeira cor de mel: listras finas e irregulares ao longo de u (repete a cada metro).
    Determinística: as fases saem de random.Random(5)."""
    global _textura_pronta
    if _textura_pronta:
        return
    rng = random.Random(5)
    # Frequências inteiras em u para a textura emendar; cada uma com fase própria.
    ondas = [(rng.randint(6, 40), rng.uniform(0, 6.283), rng.uniform(0.3, 1.0)) for _ in range(9)]
    # Fases em v desalinham as listras para o veio ondular de leve.
    deriva = [(rng.randint(1, 3), rng.uniform(0, 6.283)) for _ in range(len(ondas))]
    base = (176, 120, 62)

    def pixel(u, v):
        s = 0.0
        total = 0.0
        for (f, fase, peso), (fv, fv_fase) in zip(ondas, deriva):
            s += peso * math.sin(2 * math.pi * (f * u) + fase + 1.2 * math.sin(2 * math.pi * fv * v + fv_fase))
            total += peso
        s = s / total  # -1..1
        k = 1.0 + 0.16 * s
        # Linhas escuras estreitas de vez em quando.
        if math.sin(2 * math.pi * (23 * u + 0.8 * math.sin(2 * math.pi * v))) > 0.93:
            k *= 0.88
        return tuple(max(0, min(255, c * k)) for c in base)

    write_texture("Madeira_Mel_Veio", 128, pixel)
    _textura_pronta = True


# --------------------------------------------------------------------------- Móveis
# Espaço do móvel: X para a direita, -Y é a frente, Z para cima, origem no chão no meio.

def estante_aberta(name="EstanteAbertaMadeira"):
    """Estante larga e baixa, vazia: 1,6 x 0,4 x 1,5 m."""
    textura_mel()
    p = Part(name)
    w, d, h = 1.6, 0.4, 1.5
    side = 0.04      # espessura das laterais
    top = 0.03       # tampo
    plinth = 0.1     # altura do rodapé
    # Laterais grossas, do chão ao tampo.
    for sx in (-1, 1):
        x0 = w / 2 - side if sx > 0 else -w / 2
        p.box_span((x0, -d / 2, 0), (x0 + side, d / 2, h - top), MEL)
    # Tampo com beiral de 1,5 cm.
    p.box((0, 0, h - top / 2), (w + 0.03, d + 0.03, top), MEL, bevel=0.007)
    # Rodapé escuro recuado na frente e fundo fechado.
    p.box((0, -d / 2 + 0.03, plinth / 2), (w - 2 * side, 0.02, plinth), ESCURA)
    p.box((0, d / 2 - 0.006, (h - top) / 2), (w - 2 * side, 0.012, h - top), MEL, bevel=0.002)
    # Quatro prateleiras e um divisor central.
    shelves = [plinth + 0.012, 0.50, 0.88, 1.26]
    for z in shelves:
        p.box((0, 0.004, z), (w - 2 * side, d - 0.03, 0.025), MEL)
    p.box((0, 0.004, (plinth + h - top) / 2), (0.025, d - 0.03, h - top - plinth), MEL)
    return p.to_object()


def torre_expositora(name="TorreExpositora"):
    """Torre de livraria em cunha: 0,6 de largura, fundo de 0,55 na base e 0,25 no topo, 1,8 m."""
    textura_mel()
    p = Part(name)
    w, h = 0.6, 1.8
    base_hd, top_hd = 0.275, 0.125  # meio fundo na base e no topo
    wall = 0.025

    def meio_fundo(z):
        return base_hd + (top_hd - base_hd) * z / h

    # Laterais em trapézio (contorno em y, z; extrudado em x).
    contorno = [(-base_hd, 0.0), (base_hd, 0.0), (top_hd, h), (-top_hd, h)]
    for sx in (-1, 1):
        x0 = w / 2 - wall if sx > 0 else -w / 2
        p.prism(contorno, x0, x0 + wall, MEL, bevel=0.004, rot=(90, 0, 90))
    inner = w - 2 * wall
    # Divisória central e tampo.
    p.box((0, 0, h / 2), (inner, 0.022, h - 0.03), MEL)
    p.box((0, 0, h - 0.0125), (w + 0.02, top_hd * 2 + 0.02, 0.025), MEL, bevel=0.006)
    # Prateleiras inclinadas nas duas faces, com um batente baixo na ponta.
    tilt = 8
    for s in (-1, 1):  # -1 = frente
        for z in (0.07, 0.40, 0.73, 1.06, 1.39):
            hd = meio_fundo(z)
            length = hd - 0.025
            p.box((0, s * (0.011 + length / 2), z), (inner, length, 0.02), MEL, rot=(s * tilt, 0, 0))
            tip = s * (hd - 0.016)
            p.box((0, tip, z + 0.03), (inner, 0.014, 0.05), MEL, rot=(s * tilt, 0, 0))
    return p.to_object()


def contorno_balcao(inset=0.0, passos=10):
    """Contorno do balcão visto de cima (anti-horário): frente reta de 2,0 m e curva de 90 graus
    na ponta direita, que continua em braço reto para trás. inset > 0 encolhe, < 0 alarga."""
    cx, cy, r = 1.0, 0.4, 0.7 - inset  # centro e raio externo da curva
    pts = [(-1.0 + inset, -0.3 + inset)]
    for i in range(passos + 1):
        a = math.radians(-90 + 90 * i / passos)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    pts += [(1.7 - inset, 1.0 - inset), (1.1 + inset, 1.0 - inset),
            (1.1 + inset, 0.3 - inset), (-1.0 + inset, 0.3 - inset)]
    return pts


def balcao_de_caixa(name="BalcaoDeCaixa"):
    """Balcão em L com a ponta direita curva: 2,7 x 1,3 x 1,17 m (tampo a 1,05 m)."""
    textura_mel()
    p = Part(name)
    plinth, panel_top, slab = 0.1, 1.02, 0.03
    # Rodapé recuado escuro, painel de madeira e tampo com beiral de 3 cm.
    p.prism(contorno_balcao(0.06), 0.0, plinth, ESCURA)
    p.prism(contorno_balcao(0.0), plinth, panel_top, MEL)
    p.prism(contorno_balcao(-0.03), panel_top, panel_top + slab, MEL, bevel=0.005)
    # Faixas verticais em leve relevo no painel da frente (reta e curva).
    faixa, passo = 0.05, 0.13
    z_mid, z_h = (plinth + panel_top) / 2, panel_top - plinth - 0.1
    x = -0.9
    while x <= 1.0:
        p.box((x, -0.3 - 0.004, z_mid), (faixa, 0.008, z_h), MEL, bevel=0.0)
        x += passo
    for i in range(1, 6):
        ang = -90 + 15 * i
        a = math.radians(ang)
        p.box((1.0 + 0.7 * math.cos(a), 0.4 + 0.7 * math.sin(a), z_mid), (faixa, 0.008, z_h), MEL,
              bevel=0.0, rot=(0, 0, ang + 90))
    # Degrau mais alto atrás (lado do atendente): tampo de apoio sobre a borda de trás.
    p.box((-0.0, 0.2, panel_top + slab + 0.06), (2.1, 0.2, 0.12), MEL, bevel=0.005)
    p.box((0.0, 0.2, panel_top + slab + 0.125), (2.14, 0.23, 0.03), MEL, bevel=0.005)
    return p.to_object()


def monitor_crt(name="MonitorCRT"):
    """Monitor de tubo bege dos anos 90: 0,40 x 0,40 x 0,42 m; origem embaixo do pé."""
    p = Part(name)
    bege = "Plastico_Bege"
    # Pezinho: base achatada e pescoço.
    p.box((0, 0, 0.015), (0.22, 0.22, 0.03), bege, bevel=0.006)
    p.box((0, 0, 0.045), (0.12, 0.12, 0.03), bege, bevel=0.005)
    z0, h = 0.06, 0.36
    zc = z0 + h / 2
    front = -0.2
    # Moldura da frente com abertura 0,31 x 0,26; a tela fica funda, 1,2 cm para dentro.
    p.box((-0.1775, front + 0.09, zc), (0.045, 0.18, h), bege, bevel=0.006)
    p.box((0.1775, front + 0.09, zc), (0.045, 0.18, h), bege, bevel=0.006)
    p.box((0, front + 0.09, z0 + h - 0.05), (0.31, 0.18, 0.1), bege, bevel=0.006)
    p.box((0, front + 0.09, z0 + 0.05), (0.31, 0.18, 0.1), bege, bevel=0.006)
    p.box((0, front + 0.016, zc), (0.31, 0.012, 0.26), "Vidro_Tela", bevel=0.004)
    # Botões e luz na borda de baixo.
    for i in range(3):
        p.box((0.09 + i * 0.03, front - 0.002, z0 + 0.03), (0.02, 0.006, 0.012), "Plastico_Preto", bevel=0.0)
    # Traseira do tubo: caixa mais estreita e uma segunda ainda menor.
    p.box((0, 0.0, zc), (0.34, 0.18, 0.31), bege, bevel=0.012)
    p.box((0, 0.13, zc - 0.01), (0.22, 0.14, 0.2), bege, bevel=0.02)
    return p.to_object()


def mesa_simples(name="MesaSimples"):
    """Mesa de trabalho: tampo claro 1,2 x 0,6 a 0,75 m, quatro pernas quadradas finas."""
    textura_mel()
    p = Part(name)
    w, d, h, t = 1.2, 0.6, 0.75, 0.035
    p.box((0, 0, h - t / 2), (w, d, t), MEL, bevel=0.006)
    leg = 0.045
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (w / 2 - 0.04 - leg / 2), sy * (d / 2 - 0.04 - leg / 2)
            p.box((x, y, (h - t) / 2), (leg, leg, h - t), MEL, bevel=0.003)
    return p.to_object()


def texto_placa(p, corpo, centro, tam, material, rot, lado):
    """Letras em relevo como Part.text, mas com curvas mais redondas e sem a face que fica
    encostada no cartão (escondida), para gastar menos triângulos. lado: -1 frente, 1 verso."""
    import bmesh as _bmesh
    import bpy
    from mathutils import Vector
    from moveis_base import euler_matrix, Matrix
    curva = bpy.data.curves.new("Texto", type="FONT")
    curva.body = corpo
    curva.align_x = curva.align_y = "CENTER"
    curva.size = tam
    curva.extrude = 0.002
    curva.resolution_u = 2
    obj = bpy.data.objects.new("Texto", curva)
    bpy.context.scene.collection.objects.link(obj)
    malha = bpy.data.meshes.new_from_object(obj.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    malha.transform(Matrix.Translation(Vector(centro)) @ euler_matrix(rot))
    antes = len(p.bm.faces)
    p.bm.from_mesh(malha)
    p.bm.faces.ensure_lookup_table()
    idx = p.mat(material)
    plano = centro[1] - lado * 0.002
    for fc in p.bm.faces[antes:]:
        fc.material_index = idx
        fc.smooth = False
    bpy.data.objects.remove(obj)
    bpy.data.curves.remove(curva)
    bpy.data.meshes.remove(malha)
    return plano


def tirar_tampas(p, planos):
    """Apaga a tampa de cada letra que fica encostada no cartão (nunca aparece)."""
    import bmesh as _bmesh
    letras = {p.mat("Tinta_Vermelha"), p.mat("Tinta_Laranja")}
    ruins = [fc for fc in p.bm.faces
             if fc.material_index in letras and any(all(abs(v.co.y - pl) < 1e-5 for v in fc.verts) for pl in planos)]
    _bmesh.ops.delete(p.bm, geom=ruins, context="FACES_ONLY")


def corrigir_letras(obj, nomes):
    """As letras perderam a tampa de trás, então o recálculo de normais do to_object pode virar uma
    letra para dentro. Aqui cada letra (ilha de faces) é conferida pela tampa da frente: ela tem
    de olhar para longe do cartão (para o lado do seu próprio y)."""
    import bmesh as _bmesh
    bm = _bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    ids = {i for i, m in enumerate(obj.data.materials) if m and m.name in nomes}
    vistas = set()
    for f0 in bm.faces:
        if f0.material_index not in ids or f0.index in vistas:
            continue
        ilha, pilha = [], [f0]
        vistas.add(f0.index)
        while pilha:
            f = pilha.pop()
            ilha.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g.index not in vistas and g.material_index in ids:
                        vistas.add(g.index)
                        pilha.append(g)
        tampa = max(ilha, key=lambda q: abs(q.normal.y))
        fora = 1.0 if tampa.calc_center_median().y > 0 else -1.0
        if tampa.normal.y * fora < 0:
            _bmesh.ops.reverse_faces(bm, faces=ilha)
    bm.to_mesh(obj.data)
    bm.free()


def placa_de_teto(name, linha1, linha2, tam1, tam2):
    """Placa grande de teto: cartão branco 1,0 x 0,8 m (1,5 cm), letras laranja com sombra vermelha
    dos dois lados e dois arames de 0,6 m. Base do cartão em z = 0."""
    p = Part(name)
    w, h, e = 1.0, 0.8, 0.015
    p.box((0, 0, h / 2), (w, e, h), "Papel_Branco", bevel=0.003)
    # Arames subindo do topo, convergindo um pouco para o gancho.
    for sx in (-1, 1):
        p.tube([(sx * 0.35, 0, h - 0.005), (sx * 0.2, 0, h + 0.6)], 0.003, "Metal_Escuro", segments=6)
    z1, z2 = 0.60, 0.29
    planos = []
    for lado in (-1, 1):  # -1 = frente (-Y), 1 = verso
        rot = (90, 0, 0) if lado < 0 else (90, 0, 180)
        # No verso o eixo X da leitura está invertido: a sombra vai para o outro lado.
        dx = 0.014 * (-lado)
        y_s = lado * (e / 2 + 0.0005)  # sombra vermelha, meio encaixada no cartão
        y_l = lado * (e / 2 + 0.0035)  # letras laranja, à frente da sombra
        for texto, z, tam in ((linha1, z1, tam1), (linha2, z2, tam2)):
            planos.append(texto_placa(p, texto, (dx, y_s, z - 0.014), tam * 1.06, "Tinta_Vermelha", rot, lado))
            planos.append(texto_placa(p, texto, (0, y_l, z), tam, "Tinta_Laranja", rot, lado))
    tirar_tampas(p, planos)
    obj = p.to_object()
    corrigir_letras(obj, ["Tinta_Vermelha", "Tinta_Laranja"])
    return obj


# --------------------------------------------------------------------------- Lote

BUILDERS = [
    estante_aberta,
    torre_expositora,
    balcao_de_caixa,
    monitor_crt,
    mesa_simples,
    lambda: placa_de_teto("PlacaPenduradaOferta", "TUDO EM", "OFERTA", 0.15, 0.22),
    lambda: placa_de_teto("PlacaPenduradaLiquida", "LIQUIDA", "TUDO!", 0.17, 0.28),
]

if __name__ == "__main__":
    main_guard(lambda: run_lote("loja", BUILDERS, PALETA))
