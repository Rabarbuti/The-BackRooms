using System.Collections.Generic;
using BackRooms.Entities;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// Totem do Capitão Ancoradouro (level1-mostruario.md, seção 5.4, regra 5): a estátua de fibra
    /// de vidro do pirata da loja, do tamanho dele e na mesma pose dura, numa base preta e com a
    /// placa de OFERTA. De longe é igual ao Capitão; de perto, a base, a placa e o brilho de
    /// fibra entregam.
    ///
    /// A <see cref="PropLayer"/> põe os totens pela seed (a ordem em <see cref="All"/> é a mesma
    /// em todas as máquinas). No apagão o Capitão pode trocar de lugar com um deles
    /// (<c>CaptainBrain</c>): o host move o totem e manda a nova posição pela rede, e cada
    /// máquina chama <see cref="MoveTo"/>.
    /// </summary>
    public sealed class Totem : MonoBehaviour
    {
        /// <summary>Os totens da fase, na ordem em que a PropLayer os pôs.</summary>
        public static readonly List<Totem> All = new List<Totem>();

        /// <summary>Posição no mapa (índice em <see cref="All"/>).</summary>
        public int Index { get; private set; }

        public static void Clear() => All.Clear();

        public static Totem Register(GameObject root)
        {
            var totem = root.AddComponent<Totem>();
            totem.Index = All.Count;
            All.Add(totem);
            return totem;
        }

        void OnDestroy()
        {
            All.Remove(this);
        }

        /// <summary>Põe o totem em outro lugar (em coordenadas do mundo), virado para <paramref name="yaw"/>.</summary>
        public void MoveTo(Vector3 position, float yaw)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Physics.SyncTransforms();
        }

        // ---------------------------------------------------------------- Estátua

        static Mesh _statue;
        static Material[] _materials;
        static Vector3 _statueOffset;
        static Quaternion _statueRotation = Quaternion.identity;
        static TextAsset _builtFrom;

        /// <summary>
        /// A malha da estátua: o modelo do Capitão montado nas juntas na pose parada (a de encaixe,
        /// braços estendidos) e assado numa malha só, para não haver dez corpos animados. Feita uma
        /// vez por modelo e reaproveitada por todos os totens.
        /// </summary>
        public static bool Statue(TextAsset model, Material[] modelMaterials, out Mesh mesh, out Material[] materials,
                                  out Vector3 offset, out Quaternion rotation)
        {
            if (_statue == null || _builtFrom != model) Bake(model, modelMaterials);

            mesh = _statue;
            materials = _materials;
            offset = _statueOffset;
            rotation = _statueRotation;
            return mesh != null;
        }

        static void Bake(TextAsset model, Material[] modelMaterials)
        {
            _statue = null;
            _builtFrom = model;
            if (model == null || modelMaterials == null || modelMaterials.Length == 0) return;

            // Desligado: o Awake do corpo só roda quando ligar, e ele nunca liga (não anima nem respira).
            var holder = new GameObject("EstatuaDoTotem");
            holder.SetActive(false);
            try
            {
                var body = holder.AddComponent<CaptainBody>();
                body.model = model;
                body.modelMaterials = modelMaterials;
                body.Build();
                body.Pose(0f, 0f, 1f, 0f, 0f);

                SkinnedMeshRenderer skin = holder.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skin == null) return;

                var mesh = new Mesh { name = "Totem_Capitao" };
                skin.BakeMesh(mesh, true);
                mesh.RecalculateBounds();
                _statue = mesh;
                _statueOffset = holder.transform.InverseTransformPoint(skin.transform.position);
                _statueRotation = Quaternion.Inverse(holder.transform.rotation) * skin.transform.rotation;

                // Fibra de vidro: o mesmo desenho, mais liso e brilhante que pele e pano.
                Material[] source = skin.sharedMaterials;
                _materials = new Material[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    if (source[i] == null) continue;
                    var fiber = new Material(source[i]) { name = source[i].name + "_Fibra" };
                    if (fiber.HasProperty("_Smoothness")) fiber.SetFloat("_Smoothness", 0.78f);
                    if (fiber.HasProperty("_Metallic")) fiber.SetFloat("_Metallic", 0f);
                    _materials[i] = fiber;
                }
            }
            finally
            {
                if (Application.isPlaying) Destroy(holder); else DestroyImmediate(holder);
            }
        }
    }
}
