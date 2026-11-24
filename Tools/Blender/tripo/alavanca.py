"""Alavanca de energia do Level 0 a partir do modelo da Tripo (caixa de ferro com alça de puxar).

Uso (editor do Unity fechado ou não, tanto faz):
    blender -b --factory-startup -P Tools/Blender/tripo/alavanca.py -- <modelo.fbx>

<modelo.fbx> é a exportação da Tripo pela ponte do Unity (padrão:
Assets/TripoModels/emergency_exit_handle_3d_model_Clone1_Clone1/...fbx), com uma textura de cor por
parte na pasta .fbm ao lado. O script:

1. junta as 12 texturas numa folha só de 2048 (cada parte num quadrado, no tamanho em que veio;
   a moldura e o fundo descem a 1024) e leva o UV de cada parte para o quadrado dela;
2. separa a caixa (fixa) da alça (as peças que giram em volta do eixo dos dois pinos);
3. encaixa no espaço da alavanca do jogo: caixa com 0,56 m de altura, centro da caixa em Y = 0,
   fundo encostado em Z = 0 e frente para -Z (no Unity);
4. grava Assets/_Game/Art/Fontes/Alavanca/Alavanca.glb (dois materiais, "Caixa" e "Manivela", com a
   mesma folha em JPEG dentro do arquivo) e Alavanca.json (o eixo da alça, no espaço do Unity).

O Level0Setup lê os dois (ImportedModels.EnsureLever) e o LevelBuilder monta a alavanca com eles.
A exportação original da Tripo não vai para o Git: para mudar, exporte de novo e rode este script.
"""

import json
import math
import os
import sys

import bpy
import mathutils
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Game", "Art", "Fontes", "Alavanca")
DEFAULT = os.path.join(ROOT, "Assets", "TripoModels", "emergency_exit_handle_3d_model_Clone1_Clone1",
                       "emergency_exit_handle_3d_model_Clone1_Clone1.fbx")

# Partes que giram com a alça: a alça em U, as peças vermelhas em volta do eixo e os dois pinos.
HANDLE = {0, 2, 4, 5, 7, 8}
# Os pinos marcam o eixo de giro (horizontal, ao longo de X).
PINS = (7, 8)
# A moldura (parte 6) dá o tamanho da caixa.
FRAME = 6
BOX_HEIGHT = 0.56
ATLAS = 2048
# Maior tamanho de cada parte na folha (a moldura e o fundo vinham em 2048 e 1024).
CAP = 1024
MARGIN = 2


def part_index(obj):
    return int(obj.name.split("_")[-1].split(".")[0])


def texture_of(obj):
    for slot in obj.material_slots:
        if slot.material and slot.material.use_nodes:
            for node in slot.material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    return node.image
    raise RuntimeError(f"A parte {obj.name} não tem textura de cor.")


def pack(sizes):
    """Encaixa quadrados de lado potência de 2 numa folha ATLAS x ATLAS (árvore de quadrantes).
    Devolve {índice: (x, y, lado)} em pixels."""
    free = [(0, 0, ATLAS)]
    placed = {}
    for index, side in sorted(sizes.items(), key=lambda kv: -kv[1]):
        free.sort(key=lambda f: (f[2], f[1], f[0]))
        for k, (x, y, s) in enumerate(free):
            if s < side:
                continue
            free.pop(k)
            while s > side:
                s //= 2
                free += [(x + s, y, s), (x, y + s, s), (x + s, y + s, s)]
            placed[index] = (x, y, side)
            break
        else:
            raise RuntimeError("As texturas não cabem na folha.")
    return placed


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    source = os.path.abspath(args[0]) if args else DEFAULT
    os.makedirs(OUT, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=source)
    parts = {part_index(o): o for o in bpy.context.scene.objects if o.type == "MESH"}
    for o in parts.values():
        o.select_set(True)
    bpy.context.view_layer.objects.active = next(iter(parts.values()))
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # 1. Folha de texturas.
    images = {i: texture_of(o) for i, o in parts.items()}
    sizes = {i: min(CAP, max(images[i].size)) for i in parts}
    cells = pack(sizes)
    sheet = np.zeros((ATLAS, ATLAS, 4), np.float32)
    sheet[..., 3] = 1.0
    for i, image in images.items():
        x, y, side = cells[i]
        img = image.copy()
        if max(img.size) != side:
            img.scale(side, side)
        pixels = np.array(img.pixels[:], np.float32).reshape(side, side, 4)
        sheet[y:y + side, x:x + side] = pixels
        bpy.data.images.remove(img)

        # UV da parte para o quadrado dela, com uma margem para a amostragem não pegar o vizinho.
        me = parts[i].data
        uv = np.empty(len(me.loops) * 2, np.float32)
        me.uv_layers.active.data.foreach_get("uv", uv)
        uv = uv.reshape(-1, 2)
        uv = np.clip(uv, 0.0, 1.0)
        inner = (side - 2 * MARGIN) / ATLAS
        uv = uv * inner + (np.array((x, y), np.float32) + MARGIN) / ATLAS
        me.uv_layers.active.data.foreach_set("uv", uv.ravel())

    atlas = bpy.data.images.new("Alavanca_Cor", ATLAS, ATLAS, alpha=False)
    atlas.pixels.foreach_set(sheet.ravel())
    atlas.file_format = "JPEG"
    atlas.pack()

    def material(name, metal, rough):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        nt = m.node_tree
        bsdf = nt.nodes["Principled BSDF"]
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = atlas
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Metallic"].default_value = metal
        bsdf.inputs["Roughness"].default_value = rough
        return m

    box_mat = material("Caixa", 0.35, 0.8)
    handle_mat = material("Manivela", 0.45, 0.65)

    # 2. Eixo da alça: a reta entre os centros dos dois pinos (ao longo de X).
    def center(obj):
        V = np.array([v.co[:] for v in obj.data.vertices])
        return (V.min(axis=0) + V.max(axis=0)) / 2
    pivot = (center(parts[PINS[0]]) + center(parts[PINS[1]])) / 2

    V = np.array([v.co[:] for v in parts[FRAME].data.vertices])
    lo, hi = V.min(axis=0), V.max(axis=0)

    # 3. Encaixe: gira 180° em Z (a frente da Tripo é -Y; no Unity ela vira -Z), fundo em Y = 0 do
    # Blender (Z = 0 do Unity), centro da caixa na altura zero, escala pela altura da caixa.
    s = BOX_HEIGHT / (hi[2] - lo[2])
    back = hi[1]
    mid = (lo[2] + hi[2]) / 2

    def place(p):
        p = np.asarray(p, np.float64)
        q = np.array((-p[0], -(p[1] - back), p[2] - mid))
        return q * s

    for i, o in parts.items():
        me = o.data
        co = np.empty(len(me.vertices) * 3, np.float64)
        me.vertices.foreach_get("co", co)
        co = np.apply_along_axis(place, 1, co.reshape(-1, 3))
        me.vertices.foreach_set("co", co.astype(np.float32).ravel())
        me.update()
        # O giro de 180° em Z não muda a orientação das faces.
        me.materials.clear()
        me.materials.append(handle_mat if i in HANDLE else box_mat)

    # Junta numa caixa e numa alça.
    def join(indices, name):
        objs = [parts[i] for i in indices]
        for o in bpy.context.scene.objects:
            o.select_set(False)
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.join()
        objs[0].name = name
        return objs[0]

    box = join(sorted(set(parts) - HANDLE), "Caixa")
    handle = join(sorted(HANDLE), "Manivela")

    tris = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in (box, handle)}

    # 4. Gravação.
    glb = os.path.join(OUT, "Alavanca.glb")
    for o in bpy.context.scene.objects:
        o.select_set(o in (box, handle))
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=True,
                              export_image_format="JPEG", export_jpeg_quality=88,
                              export_apply=True, export_yup=True)

    # Eixo no espaço do Unity: Blender (x, y, z) -> glTF (x, z, -y) -> Unity (-x, z, -y).
    pb = place(pivot)
    info = {
        "pivo": [round(float(-pb[0]), 4), round(float(pb[2]), 4), round(float(-pb[1]), 4)],
        "eixo": "x",
        "alturaDaCaixa": BOX_HEIGHT,
        "larguraDaCaixa": round(float((hi[0] - lo[0]) * s), 4),
        "fundoDaCaixa": round(float((hi[1] - lo[1]) * s), 4),
        "triangulos": tris,
        "origem": os.path.basename(source),
    }
    with open(os.path.join(OUT, "Alavanca.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, ensure_ascii=False, indent=2)
    print("[alavanca]", glb, os.path.getsize(glb) // 1024, "KB", json.dumps(info, ensure_ascii=False))


main()
