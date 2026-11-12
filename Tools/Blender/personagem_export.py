"""Do personagem esculpido ao modelo do jogo (ver CharacterModels.cs no Unity).

1. O Unity grava o esqueleto do corpo em código, na pose de encaixe, em
   Tools/Blender/personagens/esqueleto_<nome>.json (BackRooms > Personagens > Exportar
   esqueletos). O script do personagem esculpe sobre ele.
2. `exportar` reduz cada peça para o jogo, abre todas num mapa UV só, assa a cor (a pintura dos
   vértices) e o relevo (a malha cheia e o ruído fino dos materiais) em duas texturas, calcula
   quais juntas movem cada vértice e grava Art/Personagens/<Nome>/<Nome>.bytes, _Cor.png e
   _Normal.png.

Coordenadas: o Blender tem Z para cima e a frente em -Y; o Unity, Y para cima e frente em +Z.
`conv` leva um vetor do Unity para o Blender e `unconv` faz o caminho de volta (o X troca de
sinal, e por isso a ordem dos triângulos também se inverte na gravação).
"""

import json
import math
import os
import struct
import time

import bmesh
import bpy
import numpy as np

import personagem_base as pb

ART = os.path.join(pb.ROOT, "Assets", "_Game", "Art", "Personagens")
SKELETONS = os.path.join(pb.ROOT, "Tools", "Blender", "personagens")


def conv(v):
    v = np.asarray(v, dtype=np.float64)
    return np.array((-v[0], -v[2], v[1]))


def unconv(B):
    B = np.atleast_2d(B)
    return np.stack((-B[:, 0], B[:, 2], -B[:, 1]), axis=1)


class Skeleton:
    """As juntas do corpo do Unity, em coordenadas do Blender."""

    def __init__(self, name):
        path = os.path.join(SKELETONS, f"esqueleto_{name}.json")
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        self.joints = {}
        for j in data["juntas"]:
            self.joints.setdefault(j["nome"], j)

    def has(self, name):
        return name in self.joints

    def pos(self, name):
        return conv(self.joints[name]["pos"])

    def axis(self, name, a):
        return pb.norm(conv(self.joints[name][a]))

    def frame(self, name):
        """Eixos no costume dos scripts: X para a esquerda do personagem, Y para trás, Z para cima."""
        X = -self.axis(name, "x")
        Y = -self.axis(name, "z")
        Z = self.axis(name, "y")
        return np.stack((X, Y, Z), axis=1)


# ------------------------------------------------------------------ pesos

def chain(segments, tau=0.035, caps=None):
    """Pesos pela distância aos ossos: cada segmento (junta, a, b) puxa os vértices perto dele, e
    perto de uma dobra os dois ossos dividem o vértice. `caps` limita quanto um osso pode pegar."""
    def weights(V, bones):
        S = len(segments)
        D = np.empty((len(V), S))
        for k, (_, a, b) in enumerate(segments):
            D[:, k] = pb.capsule(V, a, b, 0.0)
        W = np.exp(-(D - D.min(axis=1, keepdims=True)) / tau)
        per_bone = np.zeros((len(V), len(bones)))
        for k, (bone, _, _) in enumerate(segments):
            per_bone[:, bones.index(bone)] += W[:, k]
        per_bone /= per_bone.sum(axis=1, keepdims=True)
        if caps:
            for bone, cap in caps.items():
                per_bone[:, bones.index(bone)] *= cap
            per_bone /= per_bone.sum(axis=1, keepdims=True)
        return per_bone
    return weights


def rigid(bone):
    def weights(V, bones):
        W = np.zeros((len(V), len(bones)))
        W[:, bones.index(bone)] = 1.0
        return W
    return weights


def blend(fn_a, fn_b, amount):
    """Mistura dois jeitos de pesar: `amount(V)` de 0 (só a) a 1 (só b)."""
    def weights(V, bones):
        t = np.clip(amount(V), 0.0, 1.0)[:, None]
        return fn_a(V, bones) * (1 - t) + fn_b(V, bones) * t
    return weights


def top4(W):
    idx = np.argsort(-W, axis=1)[:, :4]
    w = np.take_along_axis(W, idx, axis=1)
    w[w < 0.01] = 0.0
    w /= np.maximum(w.sum(axis=1, keepdims=True), 1e-9)
    return idx.astype(np.uint8), w.astype(np.float32)


# ------------------------------------------------------------------ peças

class Part:
    """Uma peça do modelo: a malha cheia, a parte (material no Unity), quantos triângulos ela
    pode ter no jogo, como pesar os vértices e quanto espaço ela ganha na textura."""

    def __init__(self, hi, slot, tris, weights, texel=1.0):
        self.hi, self.slot, self.tris, self.weights, self.texel = hi, slot, tris, weights, texel
        self.lo = None


def mesh_arrays(me):
    V = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", V)
    return V.reshape(-1, 3).astype(np.float64)


def lower(part):
    """Cópia reduzida da peça, só de triângulos, já no espaço do mundo."""
    src = part.hi
    dg = bpy.context.evaluated_depsgraph_get()
    tris_now = sum(len(p.vertices) - 2 for p in src.evaluated_get(dg).to_mesh().polygons)
    src.evaluated_get(dg).to_mesh_clear()
    mod = None
    if src.type == "MESH" and tris_now > part.tris:
        mod = src.modifiers.new("Reduz", "DECIMATE")
        mod.ratio = part.tris / tris_now
        mod.use_collapse_triangulate = True
        dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(src.evaluated_get(dg))
    if mod is not None:
        src.modifiers.remove(mod)
    me.transform(src.matrix_world)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(me)
    bm.free()
    me.materials.clear()
    for poly in me.polygons:
        poly.use_smooth = True
    lo = bpy.data.objects.new(src.name + "_Jogo", me)
    bpy.context.scene.collection.objects.link(lo)
    part.lo = lo
    return lo


def ensure_color(obj, rgb):
    """Peça sem pintura (fita, cipós): pinta de uma cor só, para o cozimento da cor."""
    me = obj.data
    if "Col" in me.color_attributes:
        return
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    lin = np.append(pb.srgb_to_linear(np.asarray(rgb)), 1.0).astype(np.float32)
    attr.data.foreach_set("color", np.tile(lin, len(me.vertices)))


# ------------------------------------------------------------------ UV

def atlas_uvs(parts):
    """Junta as peças reduzidas numa malha só, abre o UV de tudo junto (densidade igual, com o
    rosto e as mãos maiores) e devolve as coordenadas de cada canto de triângulo, peça a peça."""
    verts, faces, starts = [], [], []
    offset = 0
    for p in parts:
        me = p.lo.data
        V = mesh_arrays(me)
        F = np.empty(len(me.polygons) * 3, np.int32)
        me.polygons.foreach_get("vertices", F)
        verts.append(V)
        faces.append(F.reshape(-1, 3) + offset)
        starts.append(sum(len(f) for f in faces[:-1]))
        offset += len(V)
    V = np.concatenate(verts)
    F = np.concatenate(faces)
    me = bpy.data.meshes.new("Atlas")
    me.vertices.add(len(V))
    me.vertices.foreach_set("co", V.astype(np.float32).ravel())
    me.loops.add(len(F) * 3)
    me.loops.foreach_set("vertex_index", F.ravel())
    me.polygons.add(len(F))
    me.polygons.foreach_set("loop_start", np.arange(0, len(F) * 3, 3, dtype=np.int32))
    me.update(calc_edges=True)
    me.uv_layers.new(name="UV")
    obj = bpy.data.objects.new("Atlas", me)
    bpy.context.scene.collection.objects.link(obj)

    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode="OBJECT")

    # Mais textura para o que se vê de perto: cada peça cresce em volta do próprio centro.
    UV = np.empty(len(me.loops) * 2, np.float32)
    me.uv_layers["UV"].data.foreach_get("uv", UV)
    UV = UV.reshape(-1, 2)
    for p, s in zip(parts, starts):
        if p.texel != 1.0:
            n = len(p.lo.data.polygons) * 3
            block = UV[s * 3:s * 3 + n]
            c = block.mean(axis=0)
            UV[s * 3:s * 3 + n] = c + (block - c) * p.texel
    me.uv_layers["UV"].data.foreach_set("uv", UV.ravel())

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(rotate=True, margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")

    UV = np.empty(len(me.loops) * 2, np.float32)
    me.uv_layers["UV"].data.foreach_get("uv", UV)
    UV = UV.reshape(-1, 2)
    result = []
    for p, s in zip(parts, starts):
        n = len(p.lo.data.polygons) * 3
        uv = UV[s * 3:s * 3 + n]
        layer = p.lo.data.uv_layers.new(name="UV")
        layer.data.foreach_set("uv", uv.ravel())
        result.append(uv)
    bpy.data.objects.remove(obj)
    bpy.data.meshes.remove(me)
    return result


# ------------------------------------------------------------------ cozimento

def emission_material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "Col"
    nt.links.new(attr.outputs["Color"], em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    return m


def target_material(name, image):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    nt.nodes.active = tex
    return m


def bake(parts, name, size=2048):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 1
    scene.render.bake.use_selected_to_active = True
    scene.render.bake.cage_extrusion = 0.008
    scene.render.bake.max_ray_distance = 0.03
    scene.render.bake.margin = 6
    scene.render.bake.use_clear = False
    scene.render.bake.target = "IMAGE_TEXTURES"

    folder = os.path.join(ART, name)
    os.makedirs(folder, exist_ok=True)
    color = bpy.data.images.new(name + "_Cor", size, size, alpha=False)
    color.generated_color = (0.05, 0.04, 0.035, 1.0)
    normal = bpy.data.images.new(name + "_Normal", size, size, alpha=False, float_buffer=False)
    normal.generated_color = (0.5, 0.5, 1.0, 1.0)
    normal.colorspace_settings.name = "Non-Color"

    emit = emission_material("Assar_Cor")
    for image, kind in ((color, "EMIT"), (normal, "NORMAL")):
        mat = target_material("Assar_" + kind, image)
        t0 = time.time()
        for p in parts:
            originals = list(p.hi.data.materials)
            if kind == "EMIT":
                p.hi.data.materials.clear()
                p.hi.data.materials.append(emit)
            p.lo.data.materials.clear()
            p.lo.data.materials.append(mat)
            for o in bpy.context.view_layer.objects:
                o.select_set(False)
            p.hi.select_set(True)
            p.lo.select_set(True)
            bpy.context.view_layer.objects.active = p.lo
            if kind == "NORMAL":
                bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT")
            else:
                bpy.ops.object.bake(type="EMIT")
            if kind == "EMIT":
                p.hi.data.materials.clear()
                for m in originals:
                    p.hi.data.materials.append(m)
        print(f"[personagem] cozimento {kind}: {time.time() - t0:.0f} s")
        image.filepath_raw = os.path.join(folder, image.name + ".png")
        image.file_format = "PNG"
        image.save()
    return color, normal


# ------------------------------------------------------------------ gravação

def write_model(parts, bones, name):
    slots = []
    for p in parts:
        if p.slot not in slots:
            slots.append(p.slot)
    P, N, UV, BI, BW = [], [], [], [], []
    tris = {s: [] for s in slots}
    base = 0
    for p in parts:
        me = p.lo.data
        V = mesh_arrays(me)
        Nv = np.empty(len(me.vertices) * 3, np.float32)
        me.vertex_normals.foreach_get("vector", Nv)
        Nv = Nv.reshape(-1, 3)
        F = np.empty(len(me.polygons) * 3, np.int32)
        me.polygons.foreach_get("vertices", F)
        uv = np.empty(len(me.loops) * 2, np.float32)
        me.uv_layers["UV"].data.foreach_get("uv", uv)
        uv = uv.reshape(-1, 2)
        # Um vértice do Unity por par (vértice, UV): costura de UV vira vértice duplicado.
        key = np.stack((F.astype(np.int64), np.round(uv[:, 0] * 65536).astype(np.int64),
                        np.round(uv[:, 1] * 65536).astype(np.int64)), axis=1)
        uniq, first, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
        vid = F[first]
        W = p.weights(V, bones)
        bi, bw = top4(W)
        P.append(unconv(V[vid]))
        N.append(unconv(Nv[vid].astype(np.float64)))
        UV.append(uv[first])
        BI.append(bi[vid])
        BW.append(bw[vid])
        tri = inverse.reshape(-1, 3)[:, ::-1] + base     # X espelhado: ordem invertida
        tris[p.slot].append(tri)
        base += len(uniq)

    P = np.concatenate(P).astype(np.float32)
    N = np.concatenate(N).astype(np.float32)
    UV = np.concatenate(UV).astype(np.float32)
    BI = np.concatenate(BI).astype(np.uint8)
    BW = np.concatenate(BW).astype(np.float32)

    path = os.path.join(ART, name, name + ".bytes")

    def names(items):
        out = struct.pack("<i", len(items))
        for s in items:
            b = s.encode("utf-8")
            out += struct.pack("<i", len(b)) + b
        return out

    with open(path, "wb") as f:
        f.write(b"BRMD" + struct.pack("<i", 1))
        f.write(names(bones))
        f.write(names(slots))
        f.write(struct.pack("<i", len(P)))
        f.write(P.tobytes())
        f.write(N.tobytes())
        f.write(UV.tobytes())
        f.write(BI.tobytes())
        f.write(BW.tobytes())
        for s in slots:
            T = np.concatenate(tris[s]).astype(np.int32).ravel() if tris[s] else np.zeros(0, np.int32)
            f.write(struct.pack("<i", len(T)))
            f.write(T.tobytes())
    total = sum(len(np.concatenate(t)) for t in tris.values() if t)
    print(f"[personagem] {path}: {len(P)} vértices, {total} triângulos, partes {slots}")


def export(parts, bones, name):
    t0 = time.time()
    for p in parts:
        lower(p)
    print(f"[personagem] reduzido: {sum(len(p.lo.data.polygons) for p in parts)} triângulos")
    atlas_uvs(parts)
    bake(parts, name)
    write_model(parts, bones, name)
    print(f"[personagem] exportado em {time.time() - t0:.0f} s")
