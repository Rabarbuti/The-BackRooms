using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace BackRooms.Items
{
    /// <summary>
    /// Guarda e desenha tudo o que os jogadores riscam com a caneta nas paredes e no chão.
    /// Cada traço é uma fita fina de malha colada à superfície, alguns milímetros à frente
    /// dela. É só apresentação: quem decide o que entra é quem chama <see cref="Append"/>
    /// (o dono da caneta, na hora, e a rede, para os outros jogadores).
    /// Não fica dentro do Level0: o LevelBuilder apaga os próprios filhos ao reconstruir.
    /// </summary>
    public sealed class DrawingBoard : MonoBehaviour
    {
        public const string StrokeMessage = "BackRooms.Stroke";
        public const int MaxPointsPerStroke = 300;

        public static DrawingBoard Instance { get; private set; }

        [Tooltip("Um material por cor da caneta, na ordem das cores.")]
        public Material[] inkMaterials;
        [Tooltip("Largura do traço, em metros.")]
        public float lineWidth = 0.024f;
        [Tooltip("Largura do traço numa lousa do Escritório (marcador), em metros.")]
        public float markerWidth = 0.036f;
        [Tooltip("Acima disso os traços mais antigos são apagados.")]
        public int maxStrokes = 1500;

        public int StrokeCount => _strokes.Count;
        public int PointCount { get; private set; }
        public int ColorCount => inkMaterials != null ? Mathf.Max(1, inkMaterials.Length) : 1;

        sealed class Stroke
        {
            public ulong Key;
            public byte Color;
            public Vector3 Normal;
            public float Lift;
            /// <summary>Riscado numa lousa (<c>ClassroomSeats</c>): traço de marcador, mais grosso.</summary>
            public bool Marker;
            public readonly List<Vector3> Points = new List<Vector3>();
            public GameObject Object;
            public Mesh Mesh;
        }

        readonly Dictionary<ulong, Stroke> _strokes = new Dictionary<ulong, Stroke>();
        readonly Queue<ulong> _order = new Queue<ulong>();
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<int> _triangles = new List<int>();
        int _created;

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        /// <summary>Identificador de um traço: quem riscou + o número do traço dessa pessoa.</summary>
        public static ulong Key(ulong clientId, int strokeIndex)
        {
            return (clientId << 24) | ((ulong)strokeIndex & 0xFFFFFF);
        }

        /// <summary>
        /// Acrescenta pontos a um traço, criando-o se for novo. Os pontos ficam sobre uma
        /// superfície plana de normal <paramref name="normal"/>.
        /// </summary>
        public void Append(ulong key, byte color, Vector3 normal, IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count == 0) return;

            if (!_strokes.TryGetValue(key, out Stroke stroke))
            {
                stroke = Create(key, color, normal);
                stroke.Marker = BackRooms.World.ClassroomSeats.IsWhiteboard(points[0]);
                _strokes.Add(key, stroke);
                _order.Enqueue(key);
                Trim();
            }

            for (int i = 0; i < points.Count && stroke.Points.Count < MaxPointsPerStroke; i++)
            {
                stroke.Points.Add(points[i]);
                PointCount++;
            }

            Rebuild(stroke);
        }

        /// <summary>
        /// Confere de novo quais traços caem numa lousa. A fase chama ao montar as lousas: quem entra
        /// depois recebe os traços (<see cref="SendAll"/>) antes de montar a fase.
        /// </summary>
        public void RefreshMarkers()
        {
            foreach (Stroke stroke in _strokes.Values)
            {
                if (stroke.Points.Count == 0) continue;
                bool marker = BackRooms.World.ClassroomSeats.IsWhiteboard(stroke.Points[0]);
                if (marker == stroke.Marker) continue;
                stroke.Marker = marker;
                Rebuild(stroke);
            }
        }

        /// <summary>Quantos traços estão numa lousa (para os testes).</summary>
        public int MarkerCount
        {
            get
            {
                int n = 0;
                foreach (Stroke stroke in _strokes.Values) if (stroke.Marker) n++;
                return n;
            }
        }

        /// <summary>
        /// Para os testes: o traço com mais pontos numa lousa, em <paramref name="points"/> (cópia), e a
        /// normal dele. Falso se não há traço em lousa.
        /// </summary>
        public bool MarkerStroke(List<Vector3> points, out Vector3 normal)
        {
            normal = Vector3.zero;
            Stroke best = null;
            foreach (Stroke stroke in _strokes.Values)
                if (stroke.Marker && (best == null || stroke.Points.Count > best.Points.Count)) best = stroke;
            if (best == null) return false;
            points.Clear();
            points.AddRange(best.Points);
            normal = best.Normal;
            return true;
        }

        public void Clear()
        {
            foreach (Stroke stroke in _strokes.Values) Discard(stroke);
            _strokes.Clear();
            _order.Clear();
            PointCount = 0;
        }

        Stroke Create(ulong key, byte color, Vector3 normal)
        {
            var stroke = new Stroke
            {
                Key = key,
                Color = color,
                Normal = normal.normalized,
                // Cada traço fica um fio de cabelo acima do anterior, para dois traços que
                // se cruzam não disputarem o mesmo lugar e piscarem.
                Lift = 0.003f + (_created++ % 12) * 0.0003f,
                Object = new GameObject("Risco"),
                Mesh = new Mesh { name = "Risco" }
            };

            stroke.Mesh.MarkDynamic();
            stroke.Object.transform.SetParent(transform, false);
            stroke.Object.AddComponent<MeshFilter>().sharedMesh = stroke.Mesh;

            var meshRenderer = stroke.Object.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            if (inkMaterials != null && inkMaterials.Length > 0)
                meshRenderer.sharedMaterial = inkMaterials[Mathf.Min(color, inkMaterials.Length - 1)];

            return stroke;
        }

        void Trim()
        {
            while (_order.Count > Mathf.Max(1, maxStrokes))
            {
                ulong oldest = _order.Dequeue();
                if (!_strokes.TryGetValue(oldest, out Stroke stroke)) continue;

                PointCount -= stroke.Points.Count;
                Discard(stroke);
                _strokes.Remove(oldest);
            }
        }

        static void Discard(Stroke stroke)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(stroke.Object);
                Object.Destroy(stroke.Mesh);
            }
            else
            {
                Object.DestroyImmediate(stroke.Object);
                Object.DestroyImmediate(stroke.Mesh);
            }
        }

        // Fita: dois vértices por ponto, um de cada lado do traço, no plano da superfície.
        void Rebuild(Stroke stroke)
        {
            _vertices.Clear();
            _normals.Clear();
            _triangles.Clear();

            List<Vector3> points = stroke.Points;
            Vector3 normal = stroke.Normal;
            Vector3 lift = normal * stroke.Lift;
            float half = (stroke.Marker ? markerWidth : lineWidth) * 0.5f;

            if (points.Count == 1)
            {
                // Um toque só: um pontinho quadrado.
                Vector3 a = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up).normalized * half;
                Vector3 b = Vector3.Cross(normal, a).normalized * half;
                Vector3 p = points[0] + lift;
                AddVertex(p - a - b, normal); AddVertex(p + a - b, normal);
                AddVertex(p - a + b, normal); AddVertex(p + a + b, normal);
                AddQuad(0, normal);
            }
            else
            {
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 before = points[Mathf.Max(0, i - 1)];
                    Vector3 after = points[Mathf.Min(points.Count - 1, i + 1)];
                    Vector3 direction = after - before;
                    if (direction.sqrMagnitude < 1e-10f) direction = Vector3.Cross(normal, Vector3.up) + Vector3.Cross(normal, Vector3.right);

                    Vector3 side = Vector3.Cross(normal, direction).normalized * half;
                    AddVertex(points[i] + lift - side, normal);
                    AddVertex(points[i] + lift + side, normal);
                    if (i > 0) AddQuad((i - 1) * 2, normal);
                }
            }

            stroke.Mesh.Clear();
            stroke.Mesh.SetVertices(_vertices);
            stroke.Mesh.SetNormals(_normals);
            stroke.Mesh.SetTriangles(_triangles, 0);
            stroke.Mesh.RecalculateBounds();
        }

        void AddVertex(Vector3 position, Vector3 normal)
        {
            _vertices.Add(position);
            _normals.Add(normal);
        }

        // Quad formado pelos vértices first..first+3 (dois de um ponto, dois do seguinte),
        // virado para o lado da normal.
        void AddQuad(int first, Vector3 normal)
        {
            Vector3 a = _vertices[first], b = _vertices[first + 1], c = _vertices[first + 2];
            bool flipped = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;

            if (!flipped)
            {
                _triangles.Add(first); _triangles.Add(first + 1); _triangles.Add(first + 2);
                _triangles.Add(first + 1); _triangles.Add(first + 3); _triangles.Add(first + 2);
            }
            else
            {
                _triangles.Add(first); _triangles.Add(first + 2); _triangles.Add(first + 1);
                _triangles.Add(first + 1); _triangles.Add(first + 2); _triangles.Add(first + 3);
            }
        }

        // ---------------------------------------------------------------- Quem entra depois

        /// <summary>Host: manda todos os traços existentes a um jogador que acabou de entrar.</summary>
        public void SendAll(NetworkManager net, ulong clientId)
        {
            foreach (Stroke stroke in _strokes.Values)
            {
                int size = sizeof(ulong) + sizeof(byte) + sizeof(float) * 3 + sizeof(ushort) + stroke.Points.Count * sizeof(float) * 3;

                using (var writer = new FastBufferWriter(size, Allocator.Temp))
                {
                    writer.WriteValueSafe(stroke.Key);
                    writer.WriteValueSafe(stroke.Color);
                    writer.WriteValueSafe(stroke.Normal);
                    writer.WriteValueSafe((ushort)stroke.Points.Count);
                    foreach (Vector3 point in stroke.Points) writer.WriteValueSafe(point);

                    net.CustomMessagingManager.SendNamedMessage(StrokeMessage, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                }
            }
        }

        /// <summary>Cliente: recebe um traço inteiro enviado por <see cref="SendAll"/>.</summary>
        public void Receive(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong key);
            reader.ReadValueSafe(out byte color);
            reader.ReadValueSafe(out Vector3 normal);
            reader.ReadValueSafe(out ushort count);

            var points = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out Vector3 point);
                points.Add(point);
            }

            // Se algum pedaço desse traço já chegou pelo caminho normal, não duplica.
            if (_strokes.ContainsKey(key)) return;
            Append(key, color, normal, points);
        }
    }
}
