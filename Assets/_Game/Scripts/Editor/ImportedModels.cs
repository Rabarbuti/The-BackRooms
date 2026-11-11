using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Modelos 3D vindos de fora (gerados por IA no Higgsfield). O GLB original fica em
    /// <see cref="SourceFolder"/>, fora de Art/Level0 — que o "Recriar tudo" apaga —, e o
    /// <see cref="Level0Setup"/> assa a partir dele uma malha pronta para o jogo (escala, giro,
    /// origem, recortes) e as texturas do HDRP em Art/Level0. Sem o GLB, nada é criado e o
    /// LevelBuilder volta à peça antiga feita em código: a troca é reversível apagando o arquivo.
    /// </summary>
    public static class ImportedModels
    {
        public const string SourceFolder = "Assets/_Game/Art/Fontes";

        /// <summary>Como encaixar um GLB no jogo.</summary>
        public sealed class Fit
        {
            /// <summary>Giro em Y (graus) que leva a frente do modelo para -Z.</summary>
            public float Yaw;
            /// <summary>Altura final em metros; a escala é a mesma nos três eixos, exceto a profundidade.</summary>
            public float Height = 1f;
            /// <summary>Fator extra só na profundidade (Z), para achatar o que veio grosso demais.</summary>
            public float DepthScale = 1f;
            /// <summary>Quanto a face de trás entra além de Z = 0 (para dentro da parede).</summary>
            public float BackInset;
            /// <summary>Descarta o triângulo se devolver verdadeiro (vértices já no lugar final).</summary>
            public Func<Vector3, Vector3, Vector3, bool> Trim;
            /// <summary>Multiplica a cor do modelo (encardido geral para combinar com o andar).</summary>
            public Color Tint = Color.white;
            public int AlbedoSize = 1024;
            public int DetailSize = 512;
        }

        /// <summary>
        /// Porta corta-fogo da saída, gerada no Higgsfield (Tripo, texto para 3D). O modelo veio
        /// com a frente em +X do glTF, 1,16 x 2 x 0,56 unidades, uma chapa de soleira avançando
        /// 0,5 m pelo chão, a folha com 10 cm de espessura e a barra antipânico a 36 cm da
        /// parede na escala final (1,25 x 2,15 m). A chapa é recortada e a profundidade cai a
        /// 40%: a folha fica 4 cm à frente da parede e a barra, 14 cm.
        /// </summary>
        public static bool EnsureExitDoor(string artFolder, out Mesh mesh, out Material material)
        {
            var fit = new Fit
            {
                Yaw = -90f,
                Height = 2.15f,
                DepthScale = 0.4f,
                BackInset = 0.01f,
                // A tinta clara estourava sob a lanterna; um pouco mais suja e amarelada.
                Tint = new Color(0.8f, 0.77f, 0.68f),
                // Soleira: rente ao chão e à frente da folha.
                Trim = (a, b, c) => a.y < 0.02f && b.y < 0.02f && c.y < 0.02f && (a.z + b.z + c.z) / 3f < -0.045f
            };

            return Ensure("ExitDoor", artFolder, fit, out mesh, out material);
        }

        static bool Ensure(string name, string artFolder, Fit fit, out Mesh mesh, out Material material)
        {
            string meshPath = $"{artFolder}/{name}_Model.asset";
            string materialPath = $"{artFolder}/{name}_Model.mat";
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mesh != null && material != null) return true;

            string source = $"{SourceFolder}/{name}.glb";
            if (!File.Exists(source))
            {
                Debug.Log($"[BackRooms] {source} não existe: {name} fica com a peça feita em código.");
                return false;
            }

            GlbReader.Model model = GlbReader.Read(source);
            mesh = BakeMesh(model, name + "_Model", fit);
            AssetDatabase.CreateAsset(mesh, meshPath);

            GlbReader.MaterialInfo info = model.Materials.Count > 0 ? model.Materials[0] : new GlbReader.MaterialInfo();
            material = BakeMaterial(info, name, artFolder, materialPath, fit);

            Debug.Log($"[BackRooms] {name}: {mesh.vertexCount} vértices, {mesh.triangles.Length / 3} triângulos, " +
                      $"{mesh.bounds.size.x:F2} x {mesh.bounds.size.y:F2} x {mesh.bounds.size.z:F2} m (gerador: {model.Generator}).");
            return true;
        }

        // ---------------------------------------------------------------- Malha

        static Mesh BakeMesh(GlbReader.Model model, string meshName, Fit fit)
        {
            Mesh raw = GlbReader.ToMesh(model, meshName, out _);
            Vector3[] positions = raw.vertices;
            Vector3[] normals = raw.normals;
            Vector2[] uvs = raw.uv;
            int[] triangles = raw.triangles;
            Object.DestroyImmediate(raw);

            // Gira, mede e escala: altura certa, centro em X, pé em Y = 0, fundo em Z = BackInset.
            Quaternion yaw = Quaternion.Euler(0f, fit.Yaw, 0f);
            var bounds = new Bounds(yaw * positions[0], Vector3.zero);
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = yaw * positions[i];
                normals[i] = yaw * normals[i];
                bounds.Encapsulate(positions[i]);
            }

            float s = fit.Height / bounds.size.y;
            var scale = new Vector3(s, s, s * fit.DepthScale);
            var offset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.max.z);

            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = Vector3.Scale(positions[i] + offset, scale) + new Vector3(0f, 0f, fit.BackInset);
                // Escala desigual: a normal segue a inversa da escala.
                normals[i] = new Vector3(normals[i].x / scale.x, normals[i].y / scale.y, normals[i].z / scale.z).normalized;
            }

            // Recorta e compacta (os vértices que sobrarem sem triângulo saem).
            var kept = new List<int>(triangles.Length);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                if (fit.Trim != null && fit.Trim(positions[triangles[i]], positions[triangles[i + 1]], positions[triangles[i + 2]])) continue;
                kept.Add(triangles[i]); kept.Add(triangles[i + 1]); kept.Add(triangles[i + 2]);
            }

            var remap = new Dictionary<int, int>();
            var outPositions = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outUvs = new List<Vector2>();
            for (int i = 0; i < kept.Count; i++)
            {
                if (!remap.TryGetValue(kept[i], out int index))
                {
                    index = outPositions.Count;
                    remap[kept[i]] = index;
                    outPositions.Add(positions[kept[i]]);
                    outNormals.Add(normals[kept[i]]);
                    outUvs.Add(uvs[kept[i]]);
                }
                kept[i] = index;
            }

            var mesh = new Mesh { name = meshName };
            mesh.indexFormat = outPositions.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(outPositions);
            mesh.SetNormals(outNormals);
            mesh.SetUVs(0, outUvs);
            mesh.SetTriangles(kept, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(false);
            return mesh;
        }

        // ---------------------------------------------------------------- Material e texturas

        static Material BakeMaterial(GlbReader.MaterialInfo info, string name, string artFolder, string materialPath, Fit fit)
        {
            Texture2D albedo = SaveTexture(info.BaseColor, $"{artFolder}/{name}_Albedo.png", fit.AlbedoSize, TextureKind.Color, info);
            Texture2D mask = SaveTexture(info.MetallicRoughness, $"{artFolder}/{name}_Mask.png", fit.DetailSize, TextureKind.Mask, info);
            Texture2D normal = SaveTexture(info.Normal, $"{artFolder}/{name}_Normal.png", fit.DetailSize, TextureKind.Normal, info);

            var material = new Material(Shader.Find("HDRP/Lit")) { name = name + "_Model" };
            material.SetColor("_BaseColor", new Color(info.BaseColorFactor.r * fit.Tint.r, info.BaseColorFactor.g * fit.Tint.g,
                                                      info.BaseColorFactor.b * fit.Tint.b, 1f));
            if (albedo != null) material.SetTexture("_BaseColorMap", albedo);

            if (mask != null)
            {
                // Com mapa de máscara o HDRP lê metal e lisura dele, entre os limites de remapeamento.
                material.SetTexture("_MaskMap", mask);
                material.SetFloat("_MetallicRemapMin", 0f);
                material.SetFloat("_MetallicRemapMax", 1f);
                material.SetFloat("_SmoothnessRemapMin", 0f);
                material.SetFloat("_SmoothnessRemapMax", 1f);
            }
            else
            {
                material.SetFloat("_Metallic", info.MetallicFactor);
                material.SetFloat("_Smoothness", 1f - info.RoughnessFactor);
            }

            if (normal != null)
            {
                material.SetTexture("_NormalMap", normal);
                material.SetFloat("_NormalScale", 1f);
            }

            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, materialPath);
            return material;
        }

        enum TextureKind { Color, Mask, Normal }

        static Texture2D SaveTexture(byte[] encoded, string path, int size, TextureKind kind, GlbReader.MaterialInfo info)
        {
            if (encoded == null) return null;

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, kind != TextureKind.Color);
            if (!source.LoadImage(encoded)) throw new InvalidDataException("Imagem do GLB ilegível: " + path);

            Color32[] pixels = Downsample(source.GetPixels32(), source.width, source.height, size, out int width, out int height);
            Object.DestroyImmediate(source);

            if (kind == TextureKind.Mask)
            {
                // glTF: G = aspereza, B = metal (e a Tripo põe oclusão no R).
                // HDRP: R = metal, G = oclusão, B = máscara de detalhe, A = lisura.
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 p = pixels[i];
                    float metal = p.b / 255f * info.MetallicFactor;
                    float rough = p.g / 255f * info.RoughnessFactor;
                    pixels[i] = new Color32((byte)Mathf.RoundToInt(metal * 255f), p.r, 0, (byte)Mathf.RoundToInt((1f - rough) * 255f));
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 4;
            importer.maxTextureSize = Mathf.Max(width, height);
            if (kind == TextureKind.Normal) importer.textureType = TextureImporterType.NormalMap;
            if (kind == TextureKind.Mask) importer.sRGBTexture = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Reduz por média de blocos (fator inteiro) até caber em maxSize.
        static Color32[] Downsample(Color32[] pixels, int width, int height, int maxSize, out int outWidth, out int outHeight)
        {
            int factor = 1;
            while (Mathf.Max(width, height) / factor > maxSize) factor *= 2;

            outWidth = width / factor;
            outHeight = height / factor;
            if (factor == 1) return pixels;

            var result = new Color32[outWidth * outHeight];
            int area = factor * factor;

            for (int y = 0; y < outHeight; y++)
            {
                for (int x = 0; x < outWidth; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int dy = 0; dy < factor; dy++)
                    {
                        int row = (y * factor + dy) * width + x * factor;
                        for (int dx = 0; dx < factor; dx++)
                        {
                            Color32 p = pixels[row + dx];
                            r += p.r; g += p.g; b += p.b; a += p.a;
                        }
                    }
                    result[y * outWidth + x] = new Color32((byte)(r / area), (byte)(g / area), (byte)(b / area), (byte)(a / area));
                }
            }

            return result;
        }
    }
}
