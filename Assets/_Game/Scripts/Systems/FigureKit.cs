using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BackRooms
{
    /// <summary>
    /// Peças para montar figuras em código, sem modelo importado: juntas (objetos vazios que
    /// giram), ossos (cápsulas que saem de uma junta) e volumes (esferas achatadas ou
    /// esticadas). A entidade, o traje dos jogadores e a mão em primeira pessoa são feitos
    /// assim. Como tudo é hierarquia de juntas, animar é só girar as juntas.
    /// </summary>
    public static class FigureKit
    {
        static Mesh _capsule;
        static Mesh _sphere;

        public static Transform Joint(Transform parent, string name, Vector3 localPosition)
        {
            var joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.localPosition = localPosition;
            return joint;
        }

        public static Transform Joint(Transform parent, string name, Vector3 localPosition, Vector3 euler)
        {
            Transform joint = Joint(parent, name, localPosition);
            joint.localRotation = Quaternion.Euler(euler);
            return joint;
        }

        /// <summary>
        /// Um osso: cápsula de <paramref name="length"/> metros que sai da junta para baixo
        /// (ou para cima). Devolve uma junta nova na ponta, para encadear o osso seguinte.
        /// </summary>
        public static Transform Limb(Transform joint, string endName, float length, float radius, bool down, Material material, bool shadows = true)
        {
            float direction = down ? -1f : 1f;

            var piece = new GameObject(joint.name + "_Peca");
            piece.transform.SetParent(joint, false);
            piece.transform.localPosition = new Vector3(0f, direction * length * 0.5f, 0f);
            // A cápsula padrão do Unity tem 2 m de altura e 0,5 m de raio.
            piece.transform.localScale = new Vector3(radius * 2f, length * 0.5f + radius * 0.5f, radius * 2f);
            AddRenderer(piece, Capsule, material, shadows);

            return Joint(joint, endName, new Vector3(0f, direction * length, 0f));
        }

        /// <summary>Um volume: esfera com o tamanho dado em cada eixo, presa a uma junta.</summary>
        public static Transform Ball(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material, bool shadows = true)
        {
            var piece = new GameObject(name);
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localScale = size;
            AddRenderer(piece, Sphere, material, shadows);
            return piece.transform;
        }

        /// <summary>Uma peça com malha própria (o crânio, um dente), presa a uma junta.</summary>
        public static Transform Piece(Transform parent, string name, Mesh mesh, Material material, bool shadows = true)
        {
            var piece = new GameObject(name);
            piece.transform.SetParent(parent, false);
            AddRenderer(piece, mesh, material, shadows);
            return piece.transform;
        }

        /// <summary>
        /// Funde numa malha só (uma por material) todas as peças paradas penduradas numa junta,
        /// e apaga as peças e as juntas de apoio que sobraram. Uma figura de centenas de
        /// cápsulas vira umas poucas dezenas de malhas, sem mudar nada na aparência.
        /// <paramref name="live"/> são as juntas que a animação gira: a fusão não entra nelas.
        /// </summary>
        public static void Bake(Transform joint, HashSet<Transform> live) => Bake(joint, live, true);

        /// <summary>
        /// Tira da junta todas as peças e juntas de apoio, deixando só o esqueleto que a animação
        /// gira. Para quando o corpo veste um modelo (<see cref="CharacterModel"/>).
        /// </summary>
        public static void Strip(Transform joint, HashSet<Transform> live) => Bake(joint, live, false);

        static void Bake(Transform joint, HashSet<Transform> live, bool combine)
        {
            var groups = new Dictionary<(Material, bool), List<CombineInstance>>();
            if (combine) Collect(joint, joint, live, groups);

            // O que não é junta viva nem leva a uma junta viva já foi copiado: pode sair.
            var leftovers = new List<GameObject>();
            foreach (Transform child in joint)
                if (!HoldsLive(child, live)) leftovers.Add(child.gameObject);
                else Prune(child, live, leftovers);

            foreach (GameObject leftover in leftovers)
            {
                if (Application.isPlaying) Object.Destroy(leftover);
                else Object.DestroyImmediate(leftover);
            }

            foreach (KeyValuePair<(Material, bool), List<CombineInstance>> group in groups)
            {
                int vertices = 0;
                foreach (CombineInstance part in group.Value) vertices += part.mesh.vertexCount;

                var mesh = new Mesh
                {
                    name = joint.name + "_Fundida",
                    indexFormat = vertices > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
                };
                mesh.CombineMeshes(group.Value.ToArray(), true, true);

                Piece(joint, joint.name + "_Malha", mesh, group.Key.Item1, group.Key.Item2);
            }
        }

        static void Collect(Transform joint, Transform node, HashSet<Transform> live, Dictionary<(Material, bool), List<CombineInstance>> groups)
        {
            foreach (Transform child in node)
            {
                if (live.Contains(child)) continue;

                if (child.TryGetComponent(out MeshFilter filter) && child.TryGetComponent(out MeshRenderer renderer) && filter.sharedMesh != null)
                {
                    var key = (renderer.sharedMaterial, renderer.shadowCastingMode != ShadowCastingMode.Off);
                    if (!groups.TryGetValue(key, out List<CombineInstance> list)) groups[key] = list = new List<CombineInstance>();

                    list.Add(new CombineInstance { mesh = filter.sharedMesh, transform = joint.worldToLocalMatrix * child.localToWorldMatrix });
                }

                Collect(joint, child, live, groups);
            }
        }

        static bool HoldsLive(Transform node, HashSet<Transform> live)
        {
            if (live.Contains(node)) return true;

            foreach (Transform child in node)
                if (HoldsLive(child, live)) return true;

            return false;
        }

        // Dentro de um ramo que leva a uma junta viva: tira as peças e os galhos mortos, deixa o caminho.
        static void Prune(Transform node, HashSet<Transform> live, List<GameObject> leftovers)
        {
            if (live.Contains(node)) return;

            if (node.TryGetComponent(out MeshRenderer renderer))
            {
                // Peça com malha no meio do caminho: a malha já foi copiada; fica só o objeto vazio.
                if (Application.isPlaying) Object.Destroy(renderer); else Object.DestroyImmediate(renderer);
                if (node.TryGetComponent(out MeshFilter filter))
                {
                    if (Application.isPlaying) Object.Destroy(filter); else Object.DestroyImmediate(filter);
                }
            }

            foreach (Transform child in node)
                if (!HoldsLive(child, live)) leftovers.Add(child.gameObject);
                else Prune(child, live, leftovers);
        }

        static void AddRenderer(GameObject piece, Mesh mesh, Material material, bool shadows)
        {
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = piece.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        static Mesh Capsule => _capsule != null ? _capsule : _capsule = BuiltinMesh(PrimitiveType.Capsule);
        static Mesh Sphere => _sphere != null ? _sphere : _sphere = BuiltinMesh(PrimitiveType.Sphere);

        // Pega a malha padrão do Unity de um primitivo temporário, apagado em seguida.
        static Mesh BuiltinMesh(PrimitiveType type)
        {
            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }
    }
}
