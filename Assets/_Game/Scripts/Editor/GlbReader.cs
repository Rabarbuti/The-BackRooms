using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Leitor mínimo de GLB (glTF 2.0 binário), para trazer modelos gerados por IA sem
    /// depender de pacote de importação. Lê só o que um modelo estático precisa: posições,
    /// normais, UV, índices, transformação dos nós e as imagens do material PBR. Não lê
    /// animação, pele, morph, Draco nem meshopt — e recusa o arquivo se ele exigir algo disso.
    ///
    /// Converte para o Unity: glTF é destro (+Y para cima, frente em +Z) e o Unity é canhoto,
    /// então o X é espelhado e a ordem dos triângulos invertida. O V da UV vira 1 - V.
    /// </summary>
    public static class GlbReader
    {
        public sealed class Primitive
        {
            public Vector3[] Positions;
            public Vector3[] Normals;
            public Vector2[] Uvs;
            public int[] Triangles;
            public int Material = -1;
        }

        public sealed class MaterialInfo
        {
            public string Name;
            public Color BaseColorFactor = Color.white;
            public float MetallicFactor = 1f;
            public float RoughnessFactor = 1f;
            public byte[] BaseColor;
            public byte[] MetallicRoughness;
            public byte[] Normal;
            public byte[] Occlusion;
            public byte[] Emissive;
        }

        public sealed class Model
        {
            public readonly List<Primitive> Primitives = new List<Primitive>();
            public readonly List<MaterialInfo> Materials = new List<MaterialInfo>();
            public string Generator;
        }

        static readonly HashSet<string> UnsupportedExtensions = new HashSet<string>
        {
            "KHR_draco_mesh_compression", "EXT_meshopt_compression", "KHR_texture_basisu", "KHR_mesh_quantization"
        };

        public static Model Read(string path)
        {
            byte[] file = File.ReadAllBytes(path);
            if (file.Length < 20 || BitConverter.ToUInt32(file, 0) != 0x46546C67)
                throw new InvalidDataException(path + " não é um GLB.");
            if (BitConverter.ToUInt32(file, 4) != 2)
                throw new InvalidDataException(path + ": só glTF 2.0 é suportado.");

            string json = null;
            int binStart = -1;
            int offset = 12;

            while (offset + 8 <= file.Length)
            {
                int length = BitConverter.ToInt32(file, offset);
                uint type = BitConverter.ToUInt32(file, offset + 4);
                if (type == 0x4E4F534A) json = Encoding.UTF8.GetString(file, offset + 8, length);
                else if (type == 0x004E4942) binStart = offset + 8;
                offset += 8 + length;
            }

            if (json == null) throw new InvalidDataException(path + ": GLB sem bloco JSON.");

            var root = (Dictionary<string, object>)Json.Parse(json);
            var model = new Model();

            if (root.TryGetValue("asset", out object asset) && asset is Dictionary<string, object> assetInfo &&
                assetInfo.TryGetValue("generator", out object generator))
                model.Generator = generator as string;

            foreach (object required in List(root, "extensionsRequired"))
                if (UnsupportedExtensions.Contains((string)required))
                    throw new NotSupportedException(path + ": exige " + required + ", que este leitor não lê.");

            var reader = new Buffers(root, file, binStart);
            List<object> images = List(root, "images");
            List<object> textures = List(root, "textures");

            foreach (object entry in List(root, "materials"))
            {
                var m = (Dictionary<string, object>)entry;
                var info = new MaterialInfo { Name = m.TryGetValue("name", out object n) ? n as string : null };

                if (m.TryGetValue("pbrMetallicRoughness", out object pbrObject))
                {
                    var pbr = (Dictionary<string, object>)pbrObject;
                    if (pbr.TryGetValue("baseColorFactor", out object factor))
                    {
                        var f = (List<object>)factor;
                        info.BaseColorFactor = new Color(Num(f[0]), Num(f[1]), Num(f[2]), Num(f[3]));
                    }
                    if (pbr.TryGetValue("metallicFactor", out object metal)) info.MetallicFactor = Num(metal);
                    if (pbr.TryGetValue("roughnessFactor", out object rough)) info.RoughnessFactor = Num(rough);
                    info.BaseColor = Image(pbr, "baseColorTexture", textures, images, reader);
                    info.MetallicRoughness = Image(pbr, "metallicRoughnessTexture", textures, images, reader);
                }

                info.Normal = Image(m, "normalTexture", textures, images, reader);
                info.Occlusion = Image(m, "occlusionTexture", textures, images, reader);
                info.Emissive = Image(m, "emissiveTexture", textures, images, reader);
                model.Materials.Add(info);
            }

            List<object> nodes = List(root, "nodes");
            List<object> meshes = List(root, "meshes");

            // Percorre a cena a partir das raízes, acumulando a transformação de cada nó.
            int sceneIndex = root.TryGetValue("scene", out object s) ? (int)Num(s) : 0;
            List<object> scenes = List(root, "scenes");
            IEnumerable<object> roots;
            if (scenes.Count > 0) roots = List((Dictionary<string, object>)scenes[sceneIndex], "nodes");
            else
            {
                var all = new List<object>();
                for (int i = 0; i < nodes.Count; i++) all.Add((double)i);
                roots = all;
            }

            foreach (object index in roots)
                Visit(model, nodes, meshes, reader, (int)Num(index), Matrix4x4.identity);

            if (model.Primitives.Count == 0) throw new InvalidDataException(path + ": nenhuma malha encontrada.");
            return model;
        }

        static void Visit(Model model, List<object> nodes, List<object> meshes, Buffers reader, int index, Matrix4x4 parent)
        {
            var node = (Dictionary<string, object>)nodes[index];
            Matrix4x4 world = parent * LocalMatrix(node);

            if (node.TryGetValue("mesh", out object meshIndex))
            {
                var mesh = (Dictionary<string, object>)meshes[(int)Num(meshIndex)];
                foreach (object p in List(mesh, "primitives"))
                {
                    var primitive = (Dictionary<string, object>)p;
                    if (primitive.TryGetValue("mode", out object mode) && (int)Num(mode) != 4) continue; // só triângulos

                    var attributes = (Dictionary<string, object>)primitive["attributes"];
                    Vector3[] positions = reader.Vec3((int)Num(attributes["POSITION"]));
                    Vector3[] normals = attributes.TryGetValue("NORMAL", out object na) ? reader.Vec3((int)Num(na)) : null;
                    Vector2[] uvs = attributes.TryGetValue("TEXCOORD_0", out object ta) ? reader.Vec2((int)Num(ta)) : null;

                    int[] indices;
                    if (primitive.TryGetValue("indices", out object ia)) indices = reader.Indices((int)Num(ia));
                    else
                    {
                        indices = new int[positions.Length];
                        for (int i = 0; i < indices.Length; i++) indices[i] = i;
                    }

                    // glTF -> Unity: espelha X (posição e normal), inverte a ordem dos triângulos e o V.
                    Matrix4x4 normalMatrix = world.inverse.transpose;
                    for (int i = 0; i < positions.Length; i++)
                    {
                        Vector3 v = world.MultiplyPoint3x4(positions[i]);
                        positions[i] = new Vector3(-v.x, v.y, v.z);
                        if (normals != null)
                        {
                            Vector3 nrm = normalMatrix.MultiplyVector(normals[i]).normalized;
                            normals[i] = new Vector3(-nrm.x, nrm.y, nrm.z);
                        }
                    }
                    if (uvs != null)
                        for (int i = 0; i < uvs.Length; i++) uvs[i] = new Vector2(uvs[i].x, 1f - uvs[i].y);

                    // Espelhar uma vez inverte a orientação; um nó com escala negativa inverte de novo.
                    bool flip = world.determinant > 0f;
                    if (flip)
                        for (int i = 0; i + 2 < indices.Length; i += 3)
                            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);

                    model.Primitives.Add(new Primitive
                    {
                        Positions = positions,
                        Normals = normals,
                        Uvs = uvs,
                        Triangles = indices,
                        Material = primitive.TryGetValue("material", out object mat) ? (int)Num(mat) : -1
                    });
                }
            }

            foreach (object child in List(node, "children"))
                Visit(model, nodes, meshes, reader, (int)Num(child), world);
        }

        static Matrix4x4 LocalMatrix(Dictionary<string, object> node)
        {
            if (node.TryGetValue("matrix", out object m))
            {
                var values = (List<object>)m;
                var matrix = new Matrix4x4();
                // glTF guarda a matriz por colunas.
                for (int c = 0; c < 4; c++)
                    for (int r = 0; r < 4; r++)
                        matrix[r, c] = Num(values[c * 4 + r]);
                return matrix;
            }

            Vector3 t = Vector3.zero;
            Quaternion q = Quaternion.identity;
            Vector3 scale = Vector3.one;
            if (node.TryGetValue("translation", out object tr)) { var l = (List<object>)tr; t = new Vector3(Num(l[0]), Num(l[1]), Num(l[2])); }
            if (node.TryGetValue("rotation", out object ro)) { var l = (List<object>)ro; q = new Quaternion(Num(l[0]), Num(l[1]), Num(l[2]), Num(l[3])); }
            if (node.TryGetValue("scale", out object sc)) { var l = (List<object>)sc; scale = new Vector3(Num(l[0]), Num(l[1]), Num(l[2])); }
            return Matrix4x4.TRS(t, q, scale);
        }

        static byte[] Image(Dictionary<string, object> owner, string key, List<object> textures, List<object> images, Buffers reader)
        {
            if (!owner.TryGetValue(key, out object refObject)) return null;
            var textureRef = (Dictionary<string, object>)refObject;
            var texture = (Dictionary<string, object>)textures[(int)Num(textureRef["index"])];
            if (!texture.TryGetValue("source", out object source)) return null;

            var image = (Dictionary<string, object>)images[(int)Num(source)];
            if (image.TryGetValue("bufferView", out object view)) return reader.View((int)Num(view));

            if (image.TryGetValue("uri", out object uri) && uri is string text && text.StartsWith("data:"))
                return Convert.FromBase64String(text.Substring(text.IndexOf(',') + 1));

            throw new NotSupportedException("Imagem externa ao GLB não é suportada: " + uri);
        }

        static List<object> List(Dictionary<string, object> owner, string key)
        {
            return owner.TryGetValue(key, out object value) && value is List<object> list ? list : new List<object>();
        }

        static float Num(object value)
        {
            return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- Dados binários

        sealed class Buffers
        {
            readonly List<object> _views;
            readonly List<object> _accessors;
            readonly byte[] _file;
            readonly int _binStart;

            public Buffers(Dictionary<string, object> root, byte[] file, int binStart)
            {
                _views = List(root, "bufferViews");
                _accessors = List(root, "accessors");
                _file = file;
                _binStart = binStart;

                foreach (object b in List(root, "buffers"))
                    if (((Dictionary<string, object>)b).ContainsKey("uri"))
                        throw new NotSupportedException("Buffer externo ao GLB não é suportado.");
                if (_binStart < 0 && _views.Count > 0) throw new InvalidDataException("GLB sem bloco binário.");
            }

            public byte[] View(int index)
            {
                var view = (Dictionary<string, object>)_views[index];
                int start = _binStart + (view.TryGetValue("byteOffset", out object o) ? (int)Num(o) : 0);
                int length = (int)Num(view["byteLength"]);
                var bytes = new byte[length];
                Buffer.BlockCopy(_file, start, bytes, 0, length);
                return bytes;
            }

            // Início, passo entre elementos, quantidade e tipo de número de um accessor.
            void Locate(int index, int components, out int start, out int stride, out int count, out int componentType, out bool normalized)
            {
                var accessor = (Dictionary<string, object>)_accessors[index];
                if (accessor.ContainsKey("sparse")) throw new NotSupportedException("Accessor esparso não é suportado.");

                count = (int)Num(accessor["count"]);
                componentType = (int)Num(accessor["componentType"]);
                normalized = accessor.TryGetValue("normalized", out object nz) && nz is bool b && b;

                var view = (Dictionary<string, object>)_views[(int)Num(accessor["bufferView"])];
                start = _binStart + (view.TryGetValue("byteOffset", out object vo) ? (int)Num(vo) : 0) +
                        (accessor.TryGetValue("byteOffset", out object ao) ? (int)Num(ao) : 0);
                stride = view.TryGetValue("byteStride", out object st) ? (int)Num(st) : components * ComponentSize(componentType);
            }

            float Component(int position, int componentType, bool normalized)
            {
                switch (componentType)
                {
                    case 5126: return BitConverter.ToSingle(_file, position);
                    case 5121: return normalized ? _file[position] / 255f : _file[position];
                    case 5123: return normalized ? BitConverter.ToUInt16(_file, position) / 65535f : BitConverter.ToUInt16(_file, position);
                    case 5120: return normalized ? Mathf.Max((sbyte)_file[position] / 127f, -1f) : (sbyte)_file[position];
                    case 5122: return normalized ? Mathf.Max(BitConverter.ToInt16(_file, position) / 32767f, -1f) : BitConverter.ToInt16(_file, position);
                    default: throw new NotSupportedException("Tipo de componente " + componentType + " não suportado.");
                }
            }

            public Vector3[] Vec3(int index)
            {
                Locate(index, 3, out int start, out int stride, out int count, out int type, out bool normalized);
                int size = ComponentSize(type);
                var result = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    int p = start + i * stride;
                    result[i] = new Vector3(Component(p, type, normalized), Component(p + size, type, normalized), Component(p + size * 2, type, normalized));
                }
                return result;
            }

            public Vector2[] Vec2(int index)
            {
                Locate(index, 2, out int start, out int stride, out int count, out int type, out bool normalized);
                int size = ComponentSize(type);
                var result = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    int p = start + i * stride;
                    result[i] = new Vector2(Component(p, type, normalized), Component(p + size, type, normalized));
                }
                return result;
            }

            public int[] Indices(int index)
            {
                Locate(index, 1, out int start, out int stride, out int count, out int type, out _);
                var result = new int[count];
                for (int i = 0; i < count; i++)
                {
                    int p = start + i * stride;
                    switch (type)
                    {
                        case 5121: result[i] = _file[p]; break;
                        case 5123: result[i] = BitConverter.ToUInt16(_file, p); break;
                        case 5125: result[i] = (int)BitConverter.ToUInt32(_file, p); break;
                        default: throw new NotSupportedException("Índice do tipo " + type + " não suportado.");
                    }
                }
                return result;
            }

            static int ComponentSize(int componentType)
            {
                switch (componentType)
                {
                    case 5120: case 5121: return 1;
                    case 5122: case 5123: return 2;
                    default: return 4;
                }
            }
        }

        // ---------------------------------------------------------------- Malha do Unity

        /// <summary>
        /// Junta todas as primitivas numa malha só, uma submalha por material do GLB, na ordem
        /// dos materiais. Primitiva sem material vai para a submalha 0.
        /// </summary>
        public static Mesh ToMesh(Model model, string name, out int[] submeshMaterials)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var bySubmesh = new SortedDictionary<int, List<int>>();
            bool hasNormals = true;

            foreach (Primitive p in model.Primitives)
            {
                int baseIndex = vertices.Count;
                vertices.AddRange(p.Positions);
                if (p.Normals != null) normals.AddRange(p.Normals); else hasNormals = false;
                if (p.Uvs != null) uvs.AddRange(p.Uvs);
                else for (int i = 0; i < p.Positions.Length; i++) uvs.Add(Vector2.zero);

                int key = Mathf.Max(0, p.Material);
                if (!bySubmesh.TryGetValue(key, out List<int> list)) bySubmesh[key] = list = new List<int>();
                foreach (int t in p.Triangles) list.Add(baseIndex + t);
            }

            var mesh = new Mesh { name = name };
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            if (hasNormals) mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = bySubmesh.Count;

            submeshMaterials = new int[bySubmesh.Count];
            int sub = 0;
            foreach (KeyValuePair<int, List<int>> pair in bySubmesh)
            {
                mesh.SetTriangles(pair.Value, sub);
                submeshMaterials[sub] = pair.Key;
                sub++;
            }

            if (!hasNormals) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // ---------------------------------------------------------------- JSON

        /// <summary>Analisador de JSON pequeno: objetos viram Dictionary, listas viram List, números viram double.</summary>
        static class Json
        {
            public static object Parse(string text)
            {
                int i = 0;
                object value = Value(text, ref i);
                return value;
            }

            static void Skip(string s, ref int i)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            static object Value(string s, ref int i)
            {
                Skip(s, ref i);
                char c = s[i];
                if (c == '{') return Object(s, ref i);
                if (c == '[') return Array(s, ref i);
                if (c == '"') return String(s, ref i);
                if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
                if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
                if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }

                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
            }

            static Dictionary<string, object> Object(string s, ref int i)
            {
                var result = new Dictionary<string, object>();
                i++;
                Skip(s, ref i);
                if (s[i] == '}') { i++; return result; }

                while (true)
                {
                    Skip(s, ref i);
                    string key = String(s, ref i);
                    Skip(s, ref i);
                    i++; // ':'
                    result[key] = Value(s, ref i);
                    Skip(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; // '}'
                    return result;
                }
            }

            static List<object> Array(string s, ref int i)
            {
                var result = new List<object>();
                i++;
                Skip(s, ref i);
                if (s[i] == ']') { i++; return result; }

                while (true)
                {
                    result.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; // ']'
                    return result;
                }
            }

            static string String(string s, ref int i)
            {
                var builder = new StringBuilder();
                i++;
                while (s[i] != '"')
                {
                    char c = s[i++];
                    if (c != '\\') { builder.Append(c); continue; }

                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': builder.Append('\n'); break;
                        case 't': builder.Append('\t'); break;
                        case 'r': builder.Append('\r'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'u': builder.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: builder.Append(e); break;
                    }
                }
                i++;
                return builder.ToString();
            }
        }
    }
}
