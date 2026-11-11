"""
Biblioteca comum dos scripts de modelos (Docs/modelos-blender.md). Todo lote de móveis em
Tools/Blender/lotes/ importa daqui as peças, a paleta, a UV, a exportação FBX e a foto rápida.

Espaço do móvel: X para a direita, -Y é a FRENTE, Z para cima, origem no chão no meio.
No Unity a frente (-Y) vira +Z, o forward do objeto. Medidas em metros.

Um lote é um script assim:

    import os, sys
    sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
    from moveis_base import *

    PALETA = {"Tecido_Marrom": ((120, 72, 36), 0.9, 0.0)}   # cores novas do lote

    def poltrona():
        p = Part("PoltronaX")
        p.box(...); p.cylinder(...)
        return p.to_object()

    if __name__ == "__main__":
        run_lote("meu_lote", [poltrona], PALETA)

Rodar:  blender -b --factory-startup -P Tools/Blender/lotes/meu_lote.py
Com fotos rápidas do Blender em Logs/Blender:  ... -P <script> -- --fotos
"""

import json
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Game", "Art", "Level1", "Modelos")
TEXTURES = os.path.join(ROOT, "Assets", "_Game", "Art", "Level1", "Texturas")
PHOTOS = os.path.join(ROOT, "Logs", "Blender")
TRI_BUDGET = 3000

# --------------------------------------------------------------------------- Paleta
# Nome -> (cor sRGB 0-255, rugosidade 0-1, metal 0-1[, textura]). O Unity cria um HDRP Lit
# com o mesmo nome. "textura" é o nome de um PNG em Art/Level1/Texturas (sem extensão),
# multiplicado pela cor: com textura, use cor clara (255, 255, 255) para não escurecer.
# Cores comuns a todos os lotes; cada lote acrescenta as suas em run_lote.
PALETTE = {
    "Madeira_Escura": ((88, 56, 34), 0.62, 0.0),
    "Madeira_Clara": ((170, 128, 82), 0.55, 0.0),
    "Madeira_Pintada": ((214, 206, 186), 0.5, 0.0),
    "Tecido_Azul": ((40, 52, 88), 0.92, 0.0),
    "Tecido_Vermelho": ((132, 36, 34), 0.92, 0.0),
    "Metal_Pintado": ((104, 114, 120), 0.48, 0.25),
    "Metal_Escuro": ((40, 42, 44), 0.45, 0.6),
    "Latao": ((196, 156, 64), 0.35, 0.85),
    "Plastico_Bege": ((206, 198, 176), 0.55, 0.0),
    "Plastico_Preto": ((22, 22, 24), 0.5, 0.0),
    "Vidro_Tela": ((18, 22, 24), 0.1, 0.0),
    "Papelao": ((166, 124, 78), 0.9, 0.0),
    "Fita_Adesiva": ((200, 174, 120), 0.35, 0.0),
    "Papel_Branco": ((236, 232, 218), 0.8, 0.0),
    "Papel_Vermelho": ((178, 30, 28), 0.8, 0.0),
    "Tinta_Vermelha": ((176, 26, 24), 0.6, 0.0),
    "Tinta_Laranja": ((222, 112, 28), 0.6, 0.0),
    "Tinta_Branca": ((240, 238, 230), 0.6, 0.0),
    "Livro_Verde": ((44, 78, 56), 0.7, 0.0),
    "Livro_Vinho": ((108, 30, 36), 0.7, 0.0),
    "Livro_Bege": ((196, 176, 136), 0.7, 0.0),
    "Livro_Azul": ((38, 56, 96), 0.7, 0.0),
}


# Cor média de cada textura gravada nesta rodada: a foto rápida (Workbench) mostra a cor do
# material, então um material com textura aparece com a média dela em vez de branco.
TEXTURE_AVERAGE = {}


def srgb_to_linear(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def ensure_material(name):
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    if name not in PALETTE:
        raise KeyError(f"Material '{name}' não está na paleta. Acrescente no PALETA do lote.")
    entry = PALETTE[name]
    (r, g, b), rough, metal = entry[0], entry[1], entry[2]
    mat = bpy.data.materials.new(name)
    linear = (srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), 1.0)
    if len(entry) > 3 and entry[3] in TEXTURE_AVERAGE:
        ar, ag, ab = TEXTURE_AVERAGE[entry[3]]
        linear = (srgb_to_linear(r * ar / 255.0), srgb_to_linear(g * ag / 255.0), srgb_to_linear(b * ab / 255.0), 1.0)
    mat.diffuse_color = linear
    mat.roughness = rough
    mat.metallic = metal
    try:
        mat.use_nodes = True
    except Exception:
        pass
    tree = getattr(mat, "node_tree", None)
    if tree:
        for node in tree.nodes:
            if node.type == "BSDF_PRINCIPLED":
                node.inputs["Base Color"].default_value = linear
                node.inputs["Roughness"].default_value = rough
                node.inputs["Metallic"].default_value = metal
    return mat


# --------------------------------------------------------------------------- Texturas

def write_texture(name, size, pixel):
    """Grava Art/Level1/Texturas/<name>.png. pixel(u, v) -> (r, g, b) em 0-255, u e v em 0-1.
    A UV dos móveis é 1 m = 1 unidade, então a textura se repete a cada metro.
    Chame antes de criar o objeto que usa a textura (a foto rápida usa a cor média dela)."""
    os.makedirs(TEXTURES, exist_ok=True)
    image = bpy.data.images.new(name, width=size, height=size, alpha=False)
    data = [0.0] * (size * size * 4)
    total = [0.0, 0.0, 0.0]
    for y in range(size):
        for x in range(size):
            r, g, b = pixel((x + 0.5) / size, (y + 0.5) / size)
            total[0] += r
            total[1] += g
            total[2] += b
            i = (y * size + x) * 4
            data[i] = r / 255.0
            data[i + 1] = g / 255.0
            data[i + 2] = b / 255.0
            data[i + 3] = 1.0
    image.pixels = data
    TEXTURE_AVERAGE[name] = tuple(t / (size * size) for t in total)
    image.filepath_raw = os.path.join(TEXTURES, name + ".png")
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


# --------------------------------------------------------------------------- Construtor de malha

def euler_matrix(rot):
    rx, ry, rz = (math.radians(a) for a in rot)
    return Matrix.Rotation(rz, 4, "Z") @ Matrix.Rotation(ry, 4, "Y") @ Matrix.Rotation(rx, 4, "X")


class Part:
    """Acumula peças numa malha só, cada face com o índice do seu material.

    Peças: box, box_span, cylinder, prism (contorno 2D extrudado), tube (cano por pontos), text.
    Ângulos sempre em graus (x, y, z). Termina com to_object()."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.materials = []

    def mat(self, name):
        if name not in self.materials:
            self.materials.append(name)
        return self.materials.index(name)

    def _finish_piece(self, verts, material, bevel, smooth=False, segments=None):
        verts = list(verts)
        faces = {f for v in verts for f in v.link_faces}
        for f in faces:
            f.material_index = self.mat(material)
            f.smooth = smooth
        # Faces para fora acertadas aqui, com a peça ainda fechada e sozinha. Recalcular a malha
        # inteira no fim errava: peças que se encostam viram uma malha só e mal fechada, e
        # algumas faces saíam do avesso (o encosto das poltronas reclináveis ficava preto).
        bmesh.ops.recalc_face_normals(self.bm, faces=list(faces))
        if bevel > 0:
            # Chanfro simples nas quinas duras; arredondado nas estofadas (bevel >= 2 cm).
            if segments is None:
                segments = 2 if bevel >= 0.02 else 1
            edges = list({e for v in verts for e in v.link_edges})
            # material=: sem ele as faces novas do chanfro ficam com o material 0 da malha
            # (bordas de outra cor quando a primeira peça do móvel é de outro material).
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, segments=segments,
                            profile=0.5, affect="EDGES", clamp_overlap=True,
                            material=self.mat(material))

    def box(self, center, size, material, bevel=0.004, rot=(0.0, 0.0, 0.0), segments=None):
        """Caixa de tamanho (x, y, z) centrada em center, girada em graus (x, y, z).
        bevel grande (0,03-0,08) com segments=3 dá almofada/estofado arredondado."""
        m = (Matrix.Translation(Vector(center))
             @ euler_matrix(rot)
             @ Matrix.Diagonal((size[0], size[1], size[2], 1.0)))
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        self._finish_piece(r["verts"], material, min(bevel, min(size) * 0.45), segments=segments)

    def box_span(self, lo, hi, material, bevel=0.004):
        """Caixa pelos cantos (x0, y0, z0) e (x1, y1, z1)."""
        c = [(a + b) * 0.5 for a, b in zip(lo, hi)]
        s = [abs(b - a) for a, b in zip(lo, hi)]
        self.box(c, s, material, bevel)

    def cylinder(self, base, height, radius, material, segments=12, radius_top=None, rot=(0.0, 0.0, 0.0)):
        """Cilindro (ou tronco de cone) em pé (eixo Z), com a base no ponto dado.
        rot=(0, 90, 0) deita no eixo X; rot=(90, 0, 0) deita no eixo -Y."""
        rt = radius if radius_top is None else radius_top
        m = (Matrix.Translation(Vector(base))
             @ euler_matrix(rot)
             @ Matrix.Translation((0.0, 0.0, height * 0.5)))
        r = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segments,
                                  radius1=radius, radius2=rt, depth=height, matrix=m)
        self._finish_piece(r["verts"], material, 0.0, smooth=True)

    def prism(self, outline, z0, z1, material, bevel=0.0, rot=(0.0, 0.0, 0.0), offset=(0.0, 0.0, 0.0)):
        """Contorno 2D [(x, y), ...] (sentido anti-horário visto de cima) extrudado de z0 a z1.
        Serve para balcão curvo, torre em cunha, placa recortada. rot/offset movem a peça pronta."""
        m = Matrix.Translation(Vector(offset)) @ euler_matrix(rot)
        bottom = [self.bm.verts.new(m @ Vector((x, y, z0))) for x, y in outline]
        top = [self.bm.verts.new(m @ Vector((x, y, z1))) for x, y in outline]
        verts = bottom + top
        self.bm.faces.new(list(reversed(bottom)))
        self.bm.faces.new(top)
        n = len(outline)
        for i in range(n):
            j = (i + 1) % n
            self.bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
        self._finish_piece(verts, material, bevel)

    def tube(self, points, radius, material, segments=8):
        """Cano/cabo/aro passando pelos pontos [(x, y, z), ...] (cilindros emendados)."""
        for a, b in zip(points[:-1], points[1:]):
            a, b = Vector(a), Vector(b)
            d = b - a
            if d.length < 1e-6:
                continue
            q = d.to_track_quat("Z", "Y")
            m = Matrix.Translation(a) @ q.to_matrix().to_4x4() @ Matrix.Translation((0, 0, d.length / 2))
            r = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segments,
                                      radius1=radius, radius2=radius, depth=d.length, matrix=m)
            self._finish_piece(r["verts"], material, 0.0, smooth=True)

    def text(self, body, center, size, material, depth=0.002, rot=(90.0, 0.0, 0.0), resolution=2):
        """Letras em relevo. Por padrão de pé, viradas para a frente (-Y). resolution = pontos
        por trecho de curva: 2 basta para placa vista de longe; 4 deixa S e O redondos."""
        curve = bpy.data.curves.new("Texto", type="FONT")
        curve.body = body
        curve.align_x = "CENTER"
        curve.align_y = "CENTER"
        curve.size = size
        curve.extrude = depth
        curve.resolution_u = resolution
        obj = bpy.data.objects.new("Texto", curve)
        bpy.context.scene.collection.objects.link(obj)
        dg = bpy.context.evaluated_depsgraph_get()
        mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
        mesh.transform(Matrix.Translation(Vector(center)) @ euler_matrix(rot))
        before = len(self.bm.faces)
        self.bm.from_mesh(mesh)
        self.bm.faces.ensure_lookup_table()
        index = self.mat(material)
        for f in self.bm.faces[before:]:
            f.material_index = index
            f.smooth = False
        bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces[before:]))
        bpy.data.objects.remove(obj)
        bpy.data.curves.remove(curve)
        bpy.data.meshes.remove(mesh)

    def to_object(self, parent=None, origin=(0.0, 0.0, 0.0)):
        """Fecha a malha e cria o objeto. Com parent e origin, vira peça móvel (porta) com a
        origem no ponto dado (a dobradiça)."""
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0001)
        # Polígonos de mais de 4 lados viram triângulos: sem isso o FBX sai sem tangentes.
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        if tuple(origin) != (0.0, 0.0, 0.0):
            bmesh.ops.translate(bm, verts=bm.verts, vec=-Vector(origin))
        project_uvs(bm)
        mesh = bpy.data.meshes.new(self.name)
        bm.to_mesh(mesh)
        bm.free()
        for name in self.materials:
            mesh.materials.append(ensure_material(name))
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        if parent is not None:
            obj.parent = parent
        obj.location = Vector(origin)
        return obj


def project_uvs(bm):
    """UV por projeção na direção dominante da face: 1 m = 1 unidade de UV."""
    layer = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for loop in f.loops:
            co = loop.vert.co
            if az >= ax and az >= ay:
                loop[layer].uv = (co.x, co.y)
            elif ax >= ay:
                loop[layer].uv = (co.y, co.z)
            else:
                loop[layer].uv = (co.x, co.z)


def triangles(obj):
    count = 0
    for o in [obj] + list(obj.children_recursive):
        if o.type == "MESH":
            count += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return count


def size_of(obj):
    """Largura (X), fundo (Y) e altura (Z) do móvel com os filhos, em metros."""
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in [obj] + list(obj.children_recursive):
        if o.type != "MESH":
            continue
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return hi - lo, lo


# --------------------------------------------------------------------------- Exportação e fotos

def export(obj):
    bpy.ops.object.select_all(action="DESELECT")
    for o in [obj] + list(obj.children_recursive):
        o.select_set(True)
    bpy.context.view_layer.objects.active = obj
    path = os.path.join(OUT, obj.name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_tspace=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
        embed_textures=False,
    )
    return path


def photo(obj, path):
    """Foto rápida (Workbench) do móvel de três quartos pela frente, com as cores da paleta.
    Serve para o próprio script conferir forma e proporção; a conferência final é no Unity."""
    scene = bpy.context.scene
    size, lo = size_of(obj)
    center = lo + size * 0.5
    radius = max(size.x, size.y, size.z) * 0.5 + 0.05

    cam_data = bpy.data.cameras.new("FotoCam")
    cam_data.lens = 50
    cam = bpy.data.objects.new("FotoCam", cam_data)
    scene.collection.objects.link(cam)
    direction = Vector((0.55, -1.0, 0.45)).normalized()  # da frente (-Y), um pouco à direita e de cima
    cam.location = center + direction * radius * 4.6
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()

    scene.camera = cam
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 480
    scene.render.image_settings.file_format = "PNG"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "MATERIAL"
    shading.show_cavity = True
    shading.show_shadows = True
    # Como no Unity, a face de trás não é desenhada: face do avesso aparece como buraco na foto.
    shading.show_backface_culling = True
    scene.display.render_aa = "8"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

    bpy.data.objects.remove(cam)
    bpy.data.cameras.remove(cam_data)


def check_other_lotes(lote, paleta):
    """Recusa uma cor do lote com o mesmo nome de uma cor de OUTRO lote e definição diferente:
    no Unity só um material com aquele nome existe, e um dos lotes sairia com a cor errada."""
    if not os.path.isdir(OUT):
        return
    for file in sorted(os.listdir(OUT)):
        if not (file.startswith("materiais_") and file.endswith(".json")) or file == f"materiais_{lote}.json":
            continue
        with open(os.path.join(OUT, file), encoding="utf-8") as f:
            for item in json.load(f).get("materiais", []):
                name = item["nome"]
                if name not in paleta:
                    continue
                mine = paleta[name]
                same = (list(mine[0]) == item["cor"] and abs(mine[1] - item["rugosidade"]) < 1e-6
                        and abs(mine[2] - item["metal"]) < 1e-6
                        and (mine[3] if len(mine) > 3 else None) == item.get("textura"))
                if not same:
                    raise ValueError(f"A cor '{name}' já existe em {file} com outra definição. "
                                     "Use outro nome ou a mesma definição.")


def run_lote(lote, builders, paleta=None):
    """Constrói, confere e exporta cada móvel do lote. Grava materiais_<lote>.json com as cores
    que o lote usou. Com '-- --fotos' na linha de comando, salva Logs/Blender/<móvel>.png."""
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    with_photos = "--fotos" in args

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    if paleta:
        clash = [k for k in paleta if k in PALETTE and PALETTE[k] != paleta[k]]
        if clash:
            raise ValueError(f"Cores já existentes redefinidas pelo lote: {clash}. Use outro nome.")
        PALETTE.update(paleta)
        check_other_lotes(lote, paleta)
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(PHOTOS, exist_ok=True)

    report, used, problems = [], set(), []
    for build in builders:
        obj = build()
        tris = triangles(obj)
        size, lo = size_of(obj)
        for o in [obj] + list(obj.children_recursive):
            if o.type == "MESH":
                used.update(m.name for m in o.data.materials)
        if tris > TRI_BUDGET:
            problems.append(f"{obj.name}: {tris} triângulos (máximo {TRI_BUDGET})")
        if abs(lo.z) > 0.01:
            problems.append(f"{obj.name}: a base está em z = {lo.z:.3f}, deveria estar no chão (0)")
        export(obj)
        if with_photos:
            photo(obj, os.path.join(PHOTOS, obj.name + ".png"))
        report.append(f"{obj.name:24s} {tris:6d} tri   {size.x:.2f} x {size.y:.2f} x {size.z:.2f} m (larg x fundo x alt)")
        # Esconde o móvel pronto para não aparecer na foto do próximo.
        for o in [obj] + list(obj.children_recursive):
            o.hide_render = True

    entries = []
    for name in sorted(used):
        e = PALETTE[name]
        item = {"nome": name, "cor": list(e[0]), "rugosidade": e[1], "metal": e[2]}
        if len(e) > 3 and e[3]:
            item["textura"] = e[3]
        entries.append(item)
    with open(os.path.join(OUT, f"materiais_{lote}.json"), "w", encoding="utf-8") as f:
        json.dump({"materiais": entries}, f, ensure_ascii=False, indent=2)

    print(f"[BackRooms] Lote '{lote}':\n  " + "\n  ".join(report))
    if problems:
        print("[BackRooms] PROBLEMAS:\n  " + "\n  ".join(problems))
        sys.exit(3)


def main_guard(fn):
    """Roda fn() e sai com erro (código 1) se algo quebrar, para a linha de comando perceber."""
    try:
        fn()
    except SystemExit:
        raise
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(1)
