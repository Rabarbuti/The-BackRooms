using System.Collections.Generic;
using BackRooms.Entities;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// Totem do Capitão Ancoradouro (level1-mostruario.md, seção 5.4, regra 5): a estátua de fibra
    /// de vidro do pirata da loja, do tamanho dele e na mesma pose dura, numa base preta e com a
    /// placa amarela de DISPLAY ONLY (nunca vermelha: vermelho com CLEARANCE é só das etiquetas do
    /// código da porta, <see cref="ExitLock"/>). De longe é igual ao Capitão; de perto, a base, a placa e o brilho de
    /// fibra entregam.
    ///
    /// A estátua é o próprio corpo do Capitão (<see cref="CaptainBody"/>, o modelo do Blender
    /// vestido nas juntas) na pose parada, com a animação desligada. Uma malha assada dele
    /// (SkinnedMeshRenderer.BakeMesh) ficou igual na foto e não ganhou nada.
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

        /// <summary>
        /// A estátua: o corpo do Capitão (o modelo do Blender vestido nas juntas) na pose parada,
        /// a de encaixe, com os braços estendidos, e a animação desligada (não anda nem respira).
        /// Os materiais são os de fibra de vidro (mais lisos e brilhantes), escolhidos pelo nome
        /// da parte. Devolve nulo sem modelo.
        /// </summary>
        public static GameObject CreateStatue(Transform parent, TextAsset model, Material[] materials)
        {
            if (model == null || materials == null || materials.Length == 0) return null;

            // Desligado ao montar: o Awake do corpo só roda quando ligar, já com o modelo.
            var holder = new GameObject("Estatua");
            holder.SetActive(false);
            holder.transform.SetParent(parent, false);

            var body = holder.AddComponent<CaptainBody>();
            body.model = model;
            body.modelMaterials = materials;
            body.Build();
            body.Pose(0f, 0f, 1f, 0f, 0f);
            body.enabled = false;

            foreach (SkinnedMeshRenderer skin in holder.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.updateWhenOffscreen = false;

            holder.SetActive(true);
            return holder;
        }
    }
}
