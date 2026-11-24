"""O traje dos jogadores (o corpo que os outros veem), a partir do modelo da Tripo.

A fonte é Assets/_Game/Art/Fontes/Traje/Traje.glb (feita por Tools/Blender/tripo/traje_fonte.py a
partir da exportação da Tripo). O traje é vestido nas juntas que o PlayerBody monta e anima em
código, como os inimigos (ver modelos-blender.md, seção 9), mas aqui é o esqueleto que segue o
modelo: as pernas do traje são mais curtas que as da figura de código, e esticá-las deformaria o
desenho. As medidas abaixo (MEDIDAS) descrevem onde ficam as juntas no traje.

Uso (blender -b --factory-startup -P Tools/Blender/personagens/traje.py -- <opção>):
  --medidas   grava Tools/Blender/personagens/traje_medidas.json (as medidas, com os nomes dos
              campos do PlayerBody). Depois: no Unity, BackRooms > Personagens > Exportar
              esqueletos (grava esqueleto_traje.json, na pose de encaixe do PlayerBody).
  (nada)      fotos do traje com as juntas desenhadas por cima, de frente e de lado, em
              Logs/Blender/personagens/traje_juntas_*.png, para acertar as MEDIDAS no olho.
  --exportar  reduz, abre o UV, assa a cor (da textura da Tripo) e o relevo (da malha da fonte),
              calcula os pesos de cada junta (esqueleto_traje.json) e grava
              Assets/_Game/Art/Personagens/Traje/Traje.bytes, _Cor.png e _Normal.png.
"""

import json
import math
import os
import sys

import bpy
import mathutils
import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import personagem_base as pb  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SOURCE = os.path.join(pb.ROOT, "Assets", "_Game", "Art", "Fontes", "Traje", "Traje.glb")
HERE = os.path.dirname(os.path.abspath(__file__))

# Altura do traje, do pé ao alto do capuz, em metros. Com ela, o visor fica perto da altura do
# olho do jogador (1,65 m).
ALTURA = 1.83

# Onde ficam as juntas no traje (metros), com os nomes dos campos do PlayerBody. A bacia fica a
# thighLength + shinLength + footHeight do chão; a coluna, 8 cm acima dela; os ombros, torsoLength
# acima da coluna; cada braço sai 4 cm abaixo dos ombros, a shoulderWidth do meio, aberto
# bindArmSpread graus e inclinado bindArmForward graus para a frente (as luvas da Tripo ficam
# à frente do corpo).
MEDIDAS = {
    "footHeight": 0.10,
    "shinLength": 0.27,
    "thighLength": 0.35,
    "hipWidth": 0.14,
    "torsoLength": 0.64,
    "shoulderWidth": 0.24,
    "upperArmLength": 0.27,
    "forearmLength": 0.23,
    "bindArmSpread": 15.0,
    "bindArmForward": 18.0,
}

# Juntas que movem o modelo (os nomes do PlayerBody).
BONES = ["Traje", "Bacia", "Coluna", "Cabeca", "Coxa_D", "Coxa_E", "Canela_D", "Canela_E", "Pe_D", "Pe_E",
         "Braco_D", "Braco_E", "Antebraco_D", "Antebraco_E", "Mao_D", "Mao_E"]


def joints_from_measures(m):
    """As juntas na pose de encaixe (tudo reto, braços abertos), em coordenadas do Unity: a mesma
    conta do PlayerBody.Build + BindPose. Para as fotos; a exportação usa o esqueleto gravado."""
    J = {}
    pelvis = m["thighLength"] + m["shinLength"] + m["footHeight"]
    J["Bacia"] = np.array((0.0, pelvis, 0.0))
    J["Coluna"] = J["Bacia"] + (0, 0.08, 0)
    J["Ombros"] = J["Coluna"] + (0, m["torsoLength"], 0)
    J["Cabeca"] = J["Ombros"] + (0, 0.03, 0)
    J["Topo"] = np.array((0.0, ALTURA, 0.0))
    for side, t in ((1, "D"), (-1, "E")):
        J["Coxa_" + t] = J["Bacia"] + (side * m["hipWidth"], 0, 0)
        J["Canela_" + t] = J["Coxa_" + t] - (0, m["thighLength"], 0)
        J["Pe_" + t] = J["Canela_" + t] - (0, m["shinLength"], 0)
        J["Ponta_" + t] = J["Pe_" + t] + (0, -m["footHeight"] + 0.03, 0.14)
        # Euler(-frente, 0, lado * abertura) do Unity: gira em Z, depois em X.
        a = math.radians(side * m["bindArmSpread"])
        f = math.radians(m["bindArmForward"])
        d = np.array((math.sin(a), -math.cos(a) * math.cos(f), math.cos(a) * math.sin(f)))
        J["Braco_" + t] = J["Ombros"] + (side * m["shoulderWidth"], -0.04, 0)
        J["Antebraco_" + t] = J["Braco_" + t] + d * m["upperArmLength"]
        J["Mao_" + t] = J["Antebraco_" + t] + d * m["forearmLength"]
        J["Dedos_" + t] = J["Mao_" + t] + d * 0.17
    return J


def conv(v):
    v = np.asarray(v, dtype=np.float64)
    return np.array((-v[0], -v[2], v[1]))


def load_source():
    """A fonte no lugar: em pé, frente para -Y, centrada em X e Y, pé em Z = 0, com ALTURA."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=SOURCE)
    obj = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    me = obj.data
    V = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get("co", V)
    V = V.reshape(-1, 3)
    lo, hi = V.min(axis=0), V.max(axis=0)
    s = ALTURA / (hi[2] - lo[2])
    V = (V - np.array(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]))) * s
    me.vertices.foreach_set("co", V.astype(np.float32).ravel())
    me.update()

    # O glTF guarda a malha cortada em toda divisa do UV: cola de volta os vértices repetidos (o UV
    # de cada canto continua o mesmo). Sem isso, a cópia reduzida vira milhares de pedaços soltos e
    # o UV do jogo, milhares de ilhas minúsculas.
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0001)
    bm.to_mesh(me)
    bm.free()
    me.update()
    obj.name = "Traje"
    return obj


def write_measures():
    path = os.path.join(HERE, "traje_medidas.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(MEDIDAS, f, indent=2)
    print("[traje] medidas em", path)


def photos(obj):
    """Fotos de frente e de lado com as juntas (esferas verdes) e os ossos (linhas) por cima."""
    J = joints_from_measures(MEDIDAS)
    for m in obj.data.materials:
        m.blend_method = "BLEND" if hasattr(m, "blend_method") else m.blend_method
        if hasattr(m, "surface_render_method"):
            m.surface_render_method = "BLENDED"
        bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf:
            bsdf.inputs["Alpha"].default_value = 0.45

    mark = bpy.data.materials.new("Junta")
    mark.use_nodes = True
    nt = mark.node_tree
    nt.nodes.clear()
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (0.1, 1.0, 0.2, 1.0)
    em.inputs["Strength"].default_value = 3.0
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], out.inputs[0])

    def sphere(p, r=0.025):
        bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=conv(p))
        bpy.context.object.data.materials.append(mark)

    def bone(a, b, r=0.008):
        a, b = conv(a), conv(b)
        d = b - a
        bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=float(np.linalg.norm(d)), location=(a + b) / 2)
        o = bpy.context.object
        o.rotation_euler = mathutils.Vector(d).to_track_quat("Z", "Y").to_euler()
        o.data.materials.append(mark)

    for name, p in J.items():
        sphere(p)
    links = [("Bacia", "Coluna"), ("Coluna", "Ombros"), ("Ombros", "Cabeca"), ("Cabeca", "Topo")]
    for t in ("D", "E"):
        links += [("Bacia", "Coxa_" + t), ("Coxa_" + t, "Canela_" + t), ("Canela_" + t, "Pe_" + t), ("Pe_" + t, "Ponta_" + t),
                  ("Ombros", "Braco_" + t), ("Braco_" + t, "Antebraco_" + t), ("Antebraco_" + t, "Mao_" + t), ("Mao_" + t, "Dedos_" + t)]
    for a, b in links:
        bone(J[a], J[b])

    scn = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    scn.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "BLENDER_EEVEE"
    scn.render.resolution_x, scn.render.resolution_y = 700, 1000
    w = bpy.data.worlds.new("w")
    scn.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[1].default_value = 1.2
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scn.collection.objects.link(cam)
    scn.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 2.0
    os.makedirs(pb.PHOTO_DIR, exist_ok=True)
    for name, loc in (("frente", (0, -4, 0.92)), ("lado", (4, 0, 0.92))):
        cam.location = loc
        cam.rotation_euler = (mathutils.Vector((0, 0, 0.92)) - mathutils.Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scn.render.filepath = os.path.join(pb.PHOTO_DIR, f"traje_juntas_{name}.png")
        bpy.ops.render.render(write_still=True)
    print("[traje] fotos em", pb.PHOTO_DIR)


def export_game(obj):
    import personagem_export as pe

    SK = pe.Skeleton("traje")
    B = SK.pos

    def seg(bone, a, b):
        return (bone, B(a) if isinstance(a, str) else a, B(b) if isinstance(b, str) else b)

    def below(name, meters):
        return B(name) - SK.axis(name, "y") * meters

    segments = [seg("Bacia", "Bacia", "Coluna"), seg("Coluna", "Coluna", "Ombros"),
                seg("Cabeca", "Cabeca", B("Cabeca") + np.array((0, 0, ALTURA - SK.pos("Cabeca")[2])))]
    for t in ("D", "E"):
        segments += [seg("Coxa_" + t, "Coxa_" + t, "Canela_" + t), seg("Canela_" + t, "Canela_" + t, "Pe_" + t),
                     seg("Pe_" + t, "Pe_" + t, below("Pe_" + t, MEDIDAS["footHeight"] * 0.7) + np.array((0, -0.12, 0))),
                     seg("Braco_" + t, "Braco_" + t, "Antebraco_" + t), seg("Antebraco_" + t, "Antebraco_" + t, "Mao_" + t),
                     seg("Mao_" + t, "Mao_" + t, below("Mao_" + t, 0.17))]
    weights = pe.chain(segments, tau=0.03)

    # A cor vem da textura da Tripo, não da pintura dos vértices: o cozimento da cor emite a imagem.
    image = None
    for m in obj.data.materials:
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image:
                image = n.image
    # A imagem da fonte também se chama Traje_Cor: com o mesmo nome, a assada viraria
    # "Traje_Cor.001" e iria para outro arquivo, e o modelo leria a textura velha.
    image.name = "Fonte_Traje_Cor"

    def emission_material(name):
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.use_nodes = True
        nt = mat.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        em = nt.nodes.new("ShaderNodeEmission")
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image
        nt.links.new(tex.outputs["Color"], em.inputs["Color"])
        nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
        return mat

    pe.emission_material = emission_material

    # A cópia reduzida herda o UV da Tripo; sem apagá-lo, o cozimento usaria esse UV e o modelo
    # gravado, o novo (atlas_uvs), e a cor sairia em manchas.
    lower = pe.lower

    def lower_without_uv(part):
        lo = lower(part)
        while lo.data.uv_layers:
            lo.data.uv_layers.remove(lo.data.uv_layers[0])
        return lo

    pe.lower = lower_without_uv
    parts = [pe.Part(obj, "Traje", 30000, weights)]
    pe.export(parts, BONES, "Traje")


def main():
    if "--medidas" in ARGS:
        write_measures()
        return
    obj = load_source()
    if "--exportar" in ARGS:
        export_game(obj)
    else:
        photos(obj)


main()
