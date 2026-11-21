using System;
using System.Globalization;
using System.IO;
using System.Text;
using BackRooms.Entities;
using BackRooms.Player;
using UnityEditor;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Os inimigos esculpidos no Blender (Tools/Blender/personagens) e o caminho de ida e volta:
    ///
    /// 1. <see cref="DumpSkeletons"/> monta cada corpo em código e grava a posição e os eixos de
    ///    cada junta, na pose de encaixe, em Tools/Blender/personagens/esqueleto_*.json. O Blender
    ///    esculpe sobre esse esqueleto, e por isso o modelo cai certinho nas juntas.
    /// 2. O script do Blender (com --exportar) grava em Art/Personagens/&lt;Nome&gt;/ o modelo
    ///    (&lt;Nome&gt;.bytes, lido por <see cref="CharacterModel"/>) e as texturas assadas
    ///    (_Cor e _Normal).
    /// 3. <see cref="UpdateModels"/> cria um material HDRP por parte do modelo.
    ///
    /// Mudou junta, comprimento de osso ou a pose parada? Exporte o esqueleto e rode o Blender
    /// de novo. Tudo em Art/Personagens é gerado: não editar à mão.
    /// </summary>
    public static class CharacterModels
    {
        public const string ArtFolder = "Assets/_Game/Art/Personagens";

        static string SkeletonFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "Blender", "personagens"));

        // ---------------------------------------------------------------- Esqueletos

        [MenuItem("BackRooms/Personagens/Exportar esqueletos para o Blender")]
        public static void DumpSkeletons()
        {
            Dump("capitao", () => new GameObject("Capitao").AddComponent<CaptainBody>());
            Dump("entidade", () => new GameObject("Entidade").AddComponent<EntityBody>());
            Dump("traje", () =>
            {
                var suit = new GameObject("Jogador").AddComponent<PlayerBody>();
                ApplySuitMeasures(suit);
                return suit;
            });
        }

        /// <summary>
        /// As medidas do traje da Tripo (Tools/Blender/personagens/traje_medidas.json, gravado pelo
        /// traje.py): comprimentos dos ossos e a abertura dos braços na pose de encaixe, com os nomes
        /// dos campos do <see cref="PlayerBody"/>. Sem o arquivo, ficam as da figura de código.
        /// </summary>
        public static void ApplySuitMeasures(PlayerBody body)
        {
            string path = Path.Combine(SkeletonFolder, "traje_medidas.json");
            if (File.Exists(path)) JsonUtility.FromJsonOverwrite(File.ReadAllText(path), body);
        }

        /// <summary>Linha de comando: grava os esqueletos e fecha (0 = ok).</summary>
        public static void BatchDumpSkeletons()
        {
            try
            {
                DumpSkeletons();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static void Dump(string name, Func<Component> make)
        {
            CaptainBody.KeepAllJoints = true;
            EntityBody.KeepAllJoints = true;
            Component body = null;
            try
            {
                body = make();
                if (body is CaptainBody captain) captain.Build();
                if (body is EntityBody entity) entity.Build();
                if (body is PlayerBody player) player.BindPose();

                Transform root = body.transform;
                var json = new StringBuilder();
                json.Append("{\n  \"juntas\": [\n");
                bool first = true;
                Write(root, root, json, ref first);
                json.Append("\n  ]\n}\n");

                Directory.CreateDirectory(SkeletonFolder);
                string path = Path.Combine(SkeletonFolder, "esqueleto_" + name + ".json");
                File.WriteAllText(path, json.ToString());
                Debug.Log($"[BackRooms] Esqueleto de {name} gravado em {path}");
            }
            finally
            {
                CaptainBody.KeepAllJoints = false;
                EntityBody.KeepAllJoints = false;
                if (body != null) UnityEngine.Object.DestroyImmediate(body.gameObject);
            }
        }

        static void Write(Transform root, Transform node, StringBuilder json, ref bool first)
        {
            // Só juntas: objetos sem malha (as peças de código não interessam ao Blender).
            if (node != root && node.GetComponent<Renderer>() == null)
            {
                if (!first) json.Append(",\n");
                first = false;
                json.Append("    {\"nome\": \"").Append(node.name)
                    .Append("\", \"pai\": \"").Append(node.parent == root ? "" : node.parent.name)
                    .Append("\", \"pos\": ").Append(Vec(root.InverseTransformPoint(node.position)))
                    .Append(", \"x\": ").Append(Vec(root.InverseTransformDirection(node.right)))
                    .Append(", \"y\": ").Append(Vec(root.InverseTransformDirection(node.up)))
                    .Append(", \"z\": ").Append(Vec(root.InverseTransformDirection(node.forward)))
                    .Append('}');
            }
            foreach (Transform child in node) Write(root, child, json, ref first);
        }

        static string Vec(Vector3 v)
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0:0.#####}, {1:0.#####}, {2:0.#####}]", v.x, v.y, v.z);
        }

        // ---------------------------------------------------------------- Modelos e materiais

        public static TextAsset Model(string name)
        {
            return AssetDatabase.LoadAssetAtPath<TextAsset>($"{ArtFolder}/{name}/{name}.bytes");
        }

        [MenuItem("BackRooms/Personagens/Atualizar modelos")]
        public static void UpdateModels()
        {
            foreach (string name in new[] { "Capitao", "Entidade", "Traje" }) Materials(name);
            AssetDatabase.SaveAssets();
        }

        // Brilho e metal de cada parte; o resto vem das texturas assadas no Blender.
        static (float smooth, float metal) Surface(string slot)
        {
            switch (slot)
            {
                case "Pele": return (0.42f, 0f);
                case "Cabelo": return (0.22f, 0f);
                case "Olho": return (0.9f, 0f);
                case "Dente": return (0.5f, 0f);
                case "Tecido": return (0.1f, 0f);
                case "Couro": return (0.38f, 0f);
                case "Metal": return (0.55f, 0.9f);
                case "Madeira": return (0.2f, 0f);
                case "Casco": return (0.72f, 0f);
                case "Casca": return (0.12f, 0f);
                // Traje de borracha do jogador: brilho de plástico molhado, como na foto da Tripo.
                case "Traje": return (0.55f, 0f);
                default: return (0.3f, 0f);
            }
        }

        /// <summary>
        /// Os materiais do modelo, um por parte, criados ou atualizados aqui (nulo se o modelo não
        /// existe, e o corpo usa o de código).
        /// </summary>
        public static Material[] Materials(string name)
        {
            TextAsset model = Model(name);
            if (model == null) return null;

            CharacterModel.ReadHeader(model.bytes, out _, out string[] slots);
            string folder = $"{ArtFolder}/{name}";
            Texture2D color = Texture(folder + "/" + name + "_Cor.png", false);
            Texture2D normal = Texture(folder + "/" + name + "_Normal.png", true);

            var materials = new Material[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                string path = $"{folder}/{name}_{slots[i]}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool created = material == null;
                if (created) material = new Material(Shader.Find("HDRP/Lit")) { name = name + "_" + slots[i] };

                (float smooth, float metal) = Surface(slots[i]);
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BaseColorMap", color);
                material.SetTexture("_NormalMap", normal);
                material.SetFloat("_NormalScale", 1f);
                material.SetFloat("_Smoothness", smooth);
                material.SetFloat("_Metallic", metal);
                HDMaterial.ValidateMaterial(material);

                if (created) AssetDatabase.CreateAsset(material, path);
                else EditorUtility.SetDirty(material);
                materials[i] = material;
            }
            return materials;
        }

        static Texture2D Texture(string path, bool normal)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("[BackRooms] Textura do personagem não encontrada: " + path);
                return null;
            }

            TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type || importer.sRGBTexture == normal || importer.maxTextureSize != 2048)
            {
                importer.textureType = type;
                importer.sRGBTexture = !normal;
                importer.maxTextureSize = 2048;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
