using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.Entities
{
    /// <summary>
    /// A perna de pau do Capitão marca o carpete (level1-mostruario.md, seção 5.4, regra 5): a cada
    /// passo dela fica uma marquinha redonda e escura no chão, um decalque. As mais velhas somem
    /// (no máximo <see cref="maxMarks"/>, a mais antiga é reaproveitada). É a pista para separar
    /// o Capitão de um totem, que nunca anda.
    ///
    /// Roda em todas as máquinas: os passos vêm do <see cref="CaptainBody"/>, que anima pela
    /// posição recebida da rede. As marcas ficam fora do Capitão (num objeto próprio na cena)
    /// para não andarem com ele.
    /// </summary>
    [RequireComponent(typeof(CaptainBody))]
    public sealed class PegMarks : MonoBehaviour
    {
        [Tooltip("Decalque da marca (HDRP/Decal), gerado pelo CaptainPreview.")]
        public Material markMaterial;
        public int maxMarks = 60;
        [Tooltip("Largura da marca, em metros.")]
        public float size = 0.09f;

        /// <summary>Quantas marcas já foram postas (para os testes).</summary>
        public int Placed { get; private set; }

        readonly List<DecalProjector> _marks = new List<DecalProjector>();
        CaptainBody _body;
        Transform _holder;
        int _next;

        void Awake()
        {
            _body = GetComponent<CaptainBody>();
        }

        void OnEnable() => _body.Stepped += OnStepped;
        void OnDisable() => _body.Stepped -= OnStepped;

        void OnDestroy()
        {
            if (_holder != null) Destroy(_holder.gameObject);
        }

        void OnStepped(float speed, bool pegLeg)
        {
            if (!pegLeg || markMaterial == null) return;

            if (_holder == null) _holder = new GameObject("MarcasDaPernaDePau").transform;

            DecalProjector mark;
            if (_marks.Count < maxMarks)
            {
                var go = new GameObject("Marca");
                go.transform.SetParent(_holder, false);
                mark = go.AddComponent<DecalProjector>();
                mark.material = markMaterial;
                mark.size = new Vector3(size, size, 0.3f);
                mark.pivot = Vector3.zero;
                mark.drawDistance = 25f;
                mark.fadeScale = 0.7f;
                _marks.Add(mark);
            }
            else
            {
                mark = _marks[_next];
                _next = (_next + 1) % maxMarks;
            }

            // Projetada de cima para baixo, um pouco acima do ponto onde a ponteira bate.
            Vector3 at = _body.PegFloorPoint;
            mark.transform.SetPositionAndRotation(at + Vector3.up * 0.05f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
            mark.fadeFactor = Random.Range(0.7f, 0.95f);
            Placed++;
        }
    }
}
