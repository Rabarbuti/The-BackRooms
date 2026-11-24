"""Fonte do traje dos jogadores a partir do modelo da Tripo (traje amarelo de proteção).

Uso:
    blender -b --factory-startup -P Tools/Blender/tripo/traje_fonte.py -- <modelo.fbx>

<modelo.fbx> é a exportação da Tripo pela ponte do Unity (padrão:
Assets/TripoModels/hazmat_suit_3d_model/hazmat_suit_3d_model.fbx): 2 milhões de triângulos e 65 MB,
grande demais para o Git (sem LFS). Este script faz uma cópia reduzida (FONTE_TRIS triângulos), abre
nela um UV novo e assa nele a cor do modelo original (a textura da Tripo), e grava
Assets/_Game/Art/Fontes/Traje/Traje.glb, com a cor em JPEG dentro do arquivo. É dessa fonte que
Tools/Blender/personagens/traje.py tira o modelo do jogo.

Por que assar em vez de só reduzir: a redução junta triângulos dos dois lados das divisas entre os
recortes da textura da Tripo, e esses triângulos pegam a cor do recorte vizinho (riscos claros no
visor e no traje).

A exportação original não vai para o Git: para mudar, exporte de novo da Tripo e rode este script.
"""

import math
import os
import sys

import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Game", "Art", "Fontes", "Traje")
DEFAULT = os.path.join(ROOT, "Assets", "TripoModels", "hazmat_suit_3d_model", "hazmat_suit_3d_model.fbx")
FONTE_TRIS = 150_000
TEXTURE = 2048


def select_only(*objs):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[-1]


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    source = os.path.abspath(args[0]) if args else DEFAULT
    os.makedirs(OUT, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=source)
    hi = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
    select_only(hi)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    tris = sum(len(p.vertices) - 2 for p in hi.data.polygons)

    image = None
    for slot in hi.material_slots:
        bsdf = next(n for n in slot.material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        link = bsdf.inputs["Base Color"].links[0]
        image = link.from_node.image

    # Cópia reduzida, sem o UV da Tripo. O FBX vem cortado em toda divisa do UV: a cópia é colada
    # antes de reduzir, senão sai em milhares de pedaços soltos e o UV novo, em ilhas de poucos
    # pixels cercadas de preto (o traje sai com bolinhas).
    import bmesh
    lo = hi.copy()
    lo.data = hi.data.copy()
    bm = bmesh.new()
    bm.from_mesh(lo.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.00005)
    bm.to_mesh(lo.data)
    bm.free()
    bpy.context.scene.collection.objects.link(lo)
    lo.name = "Traje"
    select_only(lo)
    mod = lo.modifiers.new("Reduz", "DECIMATE")
    mod.ratio = FONTE_TRIS / tris
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    while lo.data.uv_layers:
        lo.data.uv_layers.remove(lo.data.uv_layers[0])
    lo.data.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002, area_weight=0.0, correct_aspect=True)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")

    # Cor do original na cópia: a cor da Tripo emitida, assada de um para o outro.
    baked = bpy.data.images.new("Traje_Cor", TEXTURE, TEXTURE, alpha=False)
    emit = bpy.data.materials.new("Emite")
    emit.use_nodes = True
    nt = emit.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    nt.links.new(tex.outputs["Color"], em.inputs["Color"])
    nt.links.new(em.outputs[0], out.inputs[0])
    hi.data.materials.clear()
    hi.data.materials.append(emit)

    target = bpy.data.materials.new("Traje")
    target.use_nodes = True
    tnt = target.node_tree
    bsdf = tnt.nodes["Principled BSDF"]
    btex = tnt.nodes.new("ShaderNodeTexImage")
    btex.image = baked
    tnt.links.new(btex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.45
    tnt.nodes.active = btex
    lo.data.materials.clear()
    lo.data.materials.append(target)

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 1
    bake = scene.render.bake
    bake.use_selected_to_active = True
    bake.cage_extrusion = 0.004
    bake.max_ray_distance = 0.02
    bake.margin = 8
    bake.target = "IMAGE_TEXTURES"
    select_only(hi, lo)
    bpy.ops.object.bake(type="EMIT")
    baked.pack()

    select_only(lo)
    glb = os.path.join(OUT, "Traje.glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=True,
                              export_image_format="JPEG", export_jpeg_quality=90, export_apply=True)
    after = sum(len(p.vertices) - 2 for p in lo.data.polygons)
    print(f"[traje] {glb}: {after} triângulos (de {tris}), {os.path.getsize(glb) // 1024} KB")


main()
