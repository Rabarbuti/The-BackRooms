using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace BackRooms
{
    /// <summary>
    /// Personagem esculpido no Blender (Tools/Blender/personagens) vestido sobre as juntas que o
    /// corpo monta em código. O Blender grava um arquivo .bytes com a malha (já nas coordenadas
    /// do Unity, em relação ao objeto do corpo), os nomes das juntas que a movem e, para cada
    /// vértice, até quatro juntas com o peso de cada uma. Aqui a malha vira uma
    /// SkinnedMeshRenderer presa às juntas pelo nome: a animação continua sendo girar juntas,
    /// e a malha acompanha, dobrando nos cotovelos e joelhos.
    ///
    /// Formato (little-endian): "BRMD", versão; juntas (nomes); partes (nomes dos materiais);
    /// número de vértices; posições, normais, UV; índices das juntas (4 bytes por vértice) e
    /// pesos (4 floats); por parte, os índices dos triângulos.
    /// </summary>
    public static class CharacterModel
    {
        public sealed class Data
        {
            public Mesh mesh;
            public string[] bones;
            public string[] slots;
            public bool bound;
        }

        static readonly Dictionary<TextAsset, Data> _cache = new Dictionary<TextAsset, Data>();

        /// <summary>Lê só os nomes das juntas e das partes (para a ferramenta criar os materiais).</summary>
        public static void ReadHeader(byte[] bytes, out string[] bones, out string[] slots)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            ReadHeader(reader, out bones, out slots);
        }

        static void ReadHeader(BinaryReader reader, out string[] bones, out string[] slots)
        {
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "BRMD") throw new InvalidDataException("Não é um modelo de personagem (BRMD).");
            int version = reader.ReadInt32();
            if (version != 1) throw new InvalidDataException("Versão de modelo desconhecida: " + version);
            bones = ReadNames(reader);
            slots = ReadNames(reader);
        }

        static string[] ReadNames(BinaryReader reader)
        {
            var names = new string[reader.ReadInt32()];
            for (int i = 0; i < names.Length; i++) names[i] = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
            return names;
        }

        public static Data Load(TextAsset asset)
        {
            if (_cache.TryGetValue(asset, out Data cached) && cached.mesh != null) return cached;

            using var reader = new BinaryReader(new MemoryStream(asset.bytes));
            ReadHeader(reader, out string[] bones, out string[] slots);

            int count = reader.ReadInt32();
            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var uv = new Vector2[count];
            var weights = new BoneWeight[count];
            for (int i = 0; i < count; i++) positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            for (int i = 0; i < count; i++) normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            for (int i = 0; i < count; i++) uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            byte[] index = reader.ReadBytes(count * 4);
            for (int i = 0; i < count; i++)
            {
                weights[i] = new BoneWeight
                {
                    boneIndex0 = index[i * 4], boneIndex1 = index[i * 4 + 1], boneIndex2 = index[i * 4 + 2], boneIndex3 = index[i * 4 + 3],
                    weight0 = reader.ReadSingle(), weight1 = reader.ReadSingle(), weight2 = reader.ReadSingle(), weight3 = reader.ReadSingle()
                };
            }

            var mesh = new Mesh
            {
                name = asset.name,
                indexFormat = count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.boneWeights = weights;
            mesh.subMeshCount = slots.Length;
            for (int s = 0; s < slots.Length; s++)
            {
                var triangles = new int[reader.ReadInt32()];
                for (int i = 0; i < triangles.Length; i++) triangles[i] = reader.ReadInt32();
                mesh.SetTriangles(triangles, s, false);
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            var data = new Data { mesh = mesh, bones = bones, slots = slots };
            _cache[asset] = data;
            return data;
        }

        /// <summary>
        /// Veste o modelo em <paramref name="root"/>: procura as juntas pelo nome e prende a malha
        /// nelas na pose em que estão agora (a mesma em que o Blender esculpiu). Os materiais são
        /// escolhidos pelo nome da parte (o material "X_Pele" vai na parte "Pele").
        /// </summary>
        public static SkinnedMeshRenderer Attach(Transform root, TextAsset asset, Material[] materials)
        {
            Data data = Load(asset);

            var bones = new Transform[data.bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = FindDeep(root, data.bones[i]);
                if (bones[i] == null)
                {
                    Debug.LogError($"[BackRooms] O modelo {asset.name} pede a junta {data.bones[i]}, que o corpo não tem.");
                    bones[i] = root;
                }
            }

            // As poses de encaixe valem para todos os corpos iguais: calculadas uma vez.
            if (!data.bound)
            {
                var bindposes = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++) bindposes[i] = bones[i].worldToLocalMatrix * root.localToWorldMatrix;
                data.mesh.bindposes = bindposes;
                data.bound = true;
            }

            var holder = new GameObject("Modelo");
            holder.transform.SetParent(root, false);
            var renderer = holder.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = data.mesh;
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            renderer.localBounds = new Bounds(Vector3.up * 0.5f, Vector3.one * 6f);
            renderer.updateWhenOffscreen = false;

            var chosen = new Material[data.slots.Length];
            for (int s = 0; s < chosen.Length; s++)
            {
                if (materials == null) continue;
                foreach (Material material in materials)
                    if (material != null && material.name.EndsWith("_" + data.slots[s], StringComparison.Ordinal)) chosen[s] = material;
                if (chosen[s] == null && materials.Length > 0) chosen[s] = materials[0];
            }
            renderer.sharedMaterials = chosen;
            return renderer;
        }

        public static Transform FindDeep(Transform node, string name)
        {
            if (node.name == name) return node;
            foreach (Transform child in node)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
