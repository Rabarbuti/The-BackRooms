using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BackRooms.World
{
    /// <summary>
    /// Muitas cópias de peças pequenas numa malha só (um objeto para a placa de vídeo em vez de um
    /// por peça), em que cada peça ainda pode sumir e voltar: escondida, os vértices dela são
    /// recolhidos num ponto só e os triângulos ficam sem área. Usado nas luminárias do Escritório
    /// (uma malha das acesas e outra das apagadas) e nas portas de armário que batem (a porta sai
    /// daqui enquanto gira).
    ///
    /// Uso: <see cref="Add"/> para cada peça durante a montagem, <see cref="Finish"/> uma vez no
    /// fim, e <see cref="SetShown"/> quando quiser (antes do <see cref="Finish"/> também vale).
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public sealed class FusedMesh : MonoBehaviour
    {
        readonly List<CombineInstance> _parts = new List<CombineInstance>();
        readonly List<bool> _shown = new List<bool>();
        int[] _start;
        Vector3[] _full;
        Vector3[] _now;
        Mesh _mesh;
        bool _dirty;

        /// <summary>Quantas peças a malha tem.</summary>
        public int Count => _parts.Count;

        /// <summary>Verdadeiro se a peça está à mostra.</summary>
        public bool IsShown(int item) => item >= 0 && item < _shown.Count && _shown[item];

        /// <summary>Acrescenta uma peça (no espaço deste objeto) e devolve o número dela.</summary>
        public int Add(Mesh mesh, Matrix4x4 matrix)
        {
            _parts.Add(new CombineInstance { mesh = mesh, transform = matrix });
            _shown.Add(true);
            return _parts.Count - 1;
        }

        /// <summary>Junta tudo. Sem nenhuma peça, o objeto é desligado.</summary>
        public void Finish()
        {
            if (_parts.Count == 0)
            {
                gameObject.SetActive(false);
                return;
            }

            _mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            _mesh.CombineMeshes(_parts.ToArray(), true, true);
            _mesh.MarkDynamic();

            // CombineMeshes mantém a ordem das peças e os vértices de cada uma.
            _start = new int[_parts.Count + 1];
            for (int i = 0; i < _parts.Count; i++) _start[i + 1] = _start[i] + _parts[i].mesh.vertexCount;

            _full = _mesh.vertices;
            _now = (Vector3[])_full.Clone();
            Bounds bounds = _mesh.bounds;

            bool hidden = false;
            for (int i = 0; i < _shown.Count; i++)
            {
                if (_shown[i]) continue;
                Write(i);
                hidden = true;
            }
            if (hidden) _mesh.SetVertices(_now);
            // Os limites são os de todas as peças à mostra: não encolhem quando alguma some.
            _mesh.bounds = bounds;

            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        /// <summary>Mostra ou esconde uma peça.</summary>
        public void SetShown(int item, bool shown)
        {
            if (item < 0 || item >= _shown.Count || _shown[item] == shown) return;
            _shown[item] = shown;
            if (_mesh == null) return;

            Write(item);
            _dirty = true;
            // Fora do Play não há LateUpdate (montagem no editor, fotos, conferências).
            if (!Application.isPlaying) Upload();
        }

        void Write(int item)
        {
            int from = _start[item], to = _start[item + 1];
            if (_shown[item])
            {
                System.Array.Copy(_full, from, _now, from, to - from);
                return;
            }

            Vector3 point = _full[from];
            for (int i = from; i < to; i++) _now[i] = point;
        }

        // Várias peças podem mudar no mesmo quadro (o aviso de apagão): sobe tudo de uma vez.
        void LateUpdate()
        {
            if (_dirty) Upload();
        }

        void Upload()
        {
            _dirty = false;
            if (_mesh == null) return;
            Bounds bounds = _mesh.bounds;
            _mesh.SetVertices(_now);
            _mesh.bounds = bounds;
        }

        void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }
    }
}
