using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BackRooms.World
{
    /// <summary>
    /// Acumula quads e caixas em uma única malha. As UVs são calculadas a partir da posição
    /// em metros, então a textura mantém a mesma escala em qualquer peça, sem costura.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<int> _triangles = new List<int>();
        readonly float _metersPerTile;

        public MeshBuilder(float metersPerTile)
        {
            _metersPerTile = Mathf.Max(0.01f, metersPerTile);
        }

        public bool IsEmpty => _vertices.Count == 0;

        /// <summary>Quad com os cantos em ordem de perímetro. A face visível é a que aponta para <paramref name="normal"/>.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int i = _vertices.Count;

            Add(a, normal);
            Add(b, normal);
            Add(c, normal);
            Add(d, normal);

            // No Unity a face frontal tem normal = cross(b - a, c - a).
            bool flipped = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;

            if (!flipped)
            {
                _triangles.Add(i); _triangles.Add(i + 1); _triangles.Add(i + 2);
                _triangles.Add(i); _triangles.Add(i + 2); _triangles.Add(i + 3);
            }
            else
            {
                _triangles.Add(i); _triangles.Add(i + 2); _triangles.Add(i + 1);
                _triangles.Add(i); _triangles.Add(i + 3); _triangles.Add(i + 2);
            }
        }

        /// <summary>Caixa alinhada aos eixos. As tampas de cima e de baixo são opcionais.</summary>
        public void AddBox(Vector3 center, Vector3 size, bool caps = false)
        {
            Vector3 h = size * 0.5f;
            float x0 = center.x - h.x, x1 = center.x + h.x;
            float y0 = center.y - h.y, y1 = center.y + h.y;
            float z0 = center.z - h.z, z1 = center.z + h.z;

            AddQuad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.right);
            AddQuad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), Vector3.left);
            AddQuad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.forward);
            AddQuad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), Vector3.back);

            if (!caps) return;

            AddQuad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up);
            AddQuad(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), Vector3.down);
        }

        /// <summary>Número de vértices acumulados.</summary>
        public int VertexCount => _vertices.Count;

        /// <summary>Vértice avulso com UV explícita, em metros (dividida pela escala da textura). Devolve o índice.</summary>
        public int AddVertex(Vector3 position, Vector3 normal, Vector2 uvMeters)
        {
            _vertices.Add(position);
            _normals.Add(normal);
            _uvs.Add(uvMeters / _metersPerTile);
            return _vertices.Count - 1;
        }

        /// <summary>Vértice avulso com a UV projetada do mundo, como nos quads (eixo dominante da normal).</summary>
        public int AddVertexWorldUv(Vector3 position, Vector3 normal)
        {
            Add(position, normal);
            return _vertices.Count - 1;
        }

        /// <summary>Triângulo com vértices já criados, na ordem dada (horária vista da frente, como o Unity espera).</summary>
        public void AddTriangle(int a, int b, int c)
        {
            _triangles.Add(a); _triangles.Add(b); _triangles.Add(c);
        }

        /// <summary>
        /// Triângulo com vértices já criados, virado para <paramref name="facing"/>: a ordem é
        /// trocada se preciso.
        /// </summary>
        public void AddTriangleFacing(int a, int b, int c, Vector3 facing)
        {
            Vector3 cross = Vector3.Cross(_vertices[b] - _vertices[a], _vertices[c] - _vertices[a]);
            if (Vector3.Dot(cross, facing) < 0f) AddTriangle(a, c, b);
            else AddTriangle(a, b, c);
        }

        /// <summary>
        /// Polígono convexo plano, em ordem de perímetro, com a UV projetada do mundo. A face
        /// visível é a que aponta para <paramref name="normal"/>. Polígonos degenerados são ignorados.
        /// </summary>
        public void AddPolygon(IList<Vector3> points, Vector3 normal)
        {
            if (points == null || points.Count < 3) return;

            // Área (dobrada) do polígono na direção da normal: quase zero = degenerado.
            Vector3 area = Vector3.zero;
            for (int k = 1; k + 1 < points.Count; k++) area += Vector3.Cross(points[k] - points[0], points[k + 1] - points[0]);
            float signed = Vector3.Dot(area, normal);
            if (Mathf.Abs(signed) < 1e-6f) return;

            int first = _vertices.Count;
            foreach (Vector3 p in points) Add(p, normal);

            bool flipped = signed < 0f;
            for (int k = 1; k + 1 < points.Count; k++)
            {
                if (!flipped) AddTriangle(first, first + k, first + k + 1);
                else AddTriangle(first, first + k + 1, first + k);
            }
        }

        /// <summary>
        /// Tubo (cilindro sem tampas) de <paramref name="from"/> a <paramref name="to"/>, com
        /// normais suaves para fora. UV: U = volta em metros, V = comprimento em metros.
        /// </summary>
        public void AddTube(Vector3 from, Vector3 to, float radius, int sides)
        {
            Vector3 axis = to - from;
            float length = axis.magnitude;
            if (length < 1e-5f || sides < 3) return;
            axis /= length;

            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 other = Vector3.Cross(axis, side);
            float around = 2f * Mathf.PI * radius;
            int first = _vertices.Count;

            for (int i = 0; i <= sides; i++)
            {
                float angle = i / (float)sides * Mathf.PI * 2f;
                Vector3 normal = side * Mathf.Cos(angle) + other * Mathf.Sin(angle);
                float u = around * i / sides;
                AddVertex(from + normal * radius, normal, new Vector2(u, 0f));
                AddVertex(to + normal * radius, normal, new Vector2(u, length));
            }

            for (int i = 0; i < sides; i++)
            {
                int a = first + i * 2;
                AddTriangleFacing(a, a + 1, a + 3, _normals[a] + _normals[a + 2]);
                AddTriangleFacing(a, a + 3, a + 2, _normals[a] + _normals[a + 2]);
            }
        }

        /// <summary>
        /// Disco plano de raio <paramref name="radius"/> em volta de <paramref name="center"/>,
        /// virado para <paramref name="normal"/>. UV de 0 a 1 no quadrado que contém o disco
        /// (não depende da escala da textura).
        /// </summary>
        public void AddDisc(Vector3 center, Vector3 normal, Vector3 right, float radius, int sides)
        {
            normal = normal.normalized;
            right = (right - normal * Vector3.Dot(right, normal)).normalized;
            Vector3 up = Vector3.Cross(normal, right);
            int middle = AddVertex(center, normal, new Vector2(0.5f, 0.5f) * _metersPerTile);
            int first = _vertices.Count;

            for (int i = 0; i <= sides; i++)
            {
                float angle = i / (float)sides * Mathf.PI * 2f;
                float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                AddVertex(center + (right * c + up * s) * radius, normal, new Vector2(0.5f + c * 0.5f, 0.5f + s * 0.5f) * _metersPerTile);
            }

            for (int i = 0; i < sides; i++) AddTriangleFacing(middle, first + i, first + i + 1, normal);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        void Add(Vector3 position, Vector3 normal)
        {
            _vertices.Add(position);
            _normals.Add(normal);
            _uvs.Add(WorldUv(position, normal));
        }

        Vector2 WorldUv(Vector3 p, Vector3 n)
        {
            Vector2 uv;
            if (Mathf.Abs(n.y) > 0.5f) uv = new Vector2(p.x, p.z);
            else if (Mathf.Abs(n.x) > 0.5f) uv = new Vector2(p.z, p.y);
            else uv = new Vector2(p.x, p.y);

            return uv / _metersPerTile;
        }
    }
}
