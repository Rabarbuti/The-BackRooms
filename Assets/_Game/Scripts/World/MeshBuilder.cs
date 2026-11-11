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
