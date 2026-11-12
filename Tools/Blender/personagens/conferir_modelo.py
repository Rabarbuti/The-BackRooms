"""Confere um modelo exportado para o jogo: lê Art/Personagens/<Nome>/<Nome>.bytes de volta, com
a textura assada, e fotografa de quatro lados com as faces de trás escondidas (como no Unity).
Buraco, face do avesso ou cor errada no cozimento aparecem aqui sem abrir o Unity.

    blender -b --factory-startup -P Tools/Blender/personagens/conferir_modelo.py -- Capitao
"""

import math
import os
import struct
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import personagem_base as pb  # noqa: E402

NAME = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "Capitao"
FOLDER = os.path.join(pb.ROOT, "Assets", "_Game", "Art", "Personagens", NAME)


def read_names(f):
    n = struct.unpack("<i", f.read(4))[0]
    return [f.read(struct.unpack("<i", f.read(4))[0]).decode("utf-8") for _ in range(n)]


def load():
    with open(os.path.join(FOLDER, NAME + ".bytes"), "rb") as f:
        assert f.read(4) == b"BRMD"
        f.read(4)
        bones = read_names(f)
        slots = read_names(f)
        n = struct.unpack("<i", f.read(4))[0]
        P = np.frombuffer(f.read(n * 12), np.float32).reshape(-1, 3)
        f.read(n * 12)
        UV = np.frombuffer(f.read(n * 8), np.float32).reshape(-1, 2)
        f.read(n * 4)
        f.read(n * 16)
        tris = []
        for _ in slots:
            m = struct.unpack("<i", f.read(4))[0]
            tris.append(np.frombuffer(f.read(m * 4), np.int32).reshape(-1, 3))
    # Unity -> Blender, e a ordem dos triângulos volta ao normal.
    V = np.stack((-P[:, 0], -P[:, 2], P[:, 1]), axis=1)
    T = np.concatenate(tris)[:, ::-1]
    print(f"[conferir] {NAME}: {n} vértices, {len(T)} triângulos, juntas {len(bones)}, partes {slots}")
    return V, T, UV


def main():
    pb.clear_scene()
    pb.stage(ceiling=3.9)
    V, T, UV = load()
    me = bpy.data.meshes.new(NAME)
    me.vertices.add(len(V))
    me.vertices.foreach_set("co", V.astype(np.float32).ravel())
    me.loops.add(len(T) * 3)
    me.loops.foreach_set("vertex_index", T.astype(np.int32).ravel())
    me.polygons.add(len(T))
    me.polygons.foreach_set("loop_start", np.arange(0, len(T) * 3, 3, dtype=np.int32))
    me.update(calc_edges=True)
    me.shade_smooth()
    uv = me.uv_layers.new(name="UV")
    uv.data.foreach_set("uv", UV[T.ravel()].astype(np.float32).ravel())

    mat = bpy.data.materials.new("Conferir")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(FOLDER, NAME + "_Cor.png"))
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nm = nt.nodes.new("ShaderNodeTexImage")
    nm.image = bpy.data.images.load(os.path.join(FOLDER, NAME + "_Normal.png"))
    nm.image.colorspace_settings.name = "Non-Color"
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.6
    mat.use_backface_culling = True
    me.materials.append(mat)
    obj = bpy.data.objects.new(NAME, me)
    bpy.context.scene.collection.objects.link(obj)

    lo, hi = V.min(0), V.max(0)
    c = (lo + hi) * 0.5
    h = hi[2] - lo[2]
    out = pb.PHOTO_DIR
    for tag, d in (("frente", (0.25, -1.0, 0.05)), ("lado", (-1.0, -0.1, 0.05)), ("costas", (-0.3, 1.0, 0.1)),
                   ("34", (0.8, -0.8, 0.12))):
        eye = c + np.array(d) * h * 1.25
        eye[2] = min(eye[2], 3.6)
        pb.shoot(os.path.join(out, f"jogo_{NAME.lower()}_{tag}.png"), eye, c, 35, (800, 1000), samples=32)
    top = lo + (hi - lo) * np.array((0.5, 0.5, 0.86))
    pb.shoot(os.path.join(out, f"jogo_{NAME.lower()}_rosto.png"), top + np.array((0.2, -1.1, 0.0)), top, 50, (800, 800), samples=32)


pb.run(main)
