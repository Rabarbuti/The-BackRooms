using System;
using System.Collections.Generic;
using System.IO;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Ponte entre os modelos feitos no Blender (Tools/Blender, ver Docs/modelos-blender.md) e
    /// o Unity, para os móveis do Level 1.
    ///
    /// - Todo .fbx em Art/Level1/Modelos entra com as mesmas configurações (escala 1, sem
    ///   animação, câmera ou luz) e com os materiais trocados pelos HDRP Lit de
    ///   Art/Level1/Materiais, achados pelo nome do material no Blender.
    /// - Os materiais saem dos Art/Level1/Modelos/materiais_<lote>.json que cada lote do
    ///   Blender grava: cor, rugosidade, metal e textura (PNG em Art/Level1/Texturas) ficam
    ///   num lugar só, o script.
    /// - Menu BackRooms > Móveis do Level 1 > Atualizar e fotografar: cria ou atualiza os
    ///   materiais, reimporta os modelos e tira fotos deles dentro do Level 0
    ///   (Logs/Previews/moveis_*.png).
    /// </summary>
    public static class Level1Models
    {
        public const string ModelFolder = "Assets/_Game/Art/Level1/Modelos";
        public const string MaterialFolder = "Assets/_Game/Art/Level1/Materiais";
        public const string TextureFolder = "Assets/_Game/Art/Level1/Texturas";
        const string ScenePath = "Assets/_Game/Scenes/Level0_Test.unity";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";

        [Serializable]
        class Palette { public List<Entry> materiais = new List<Entry>(); }

        [Serializable]
        class Entry
        {
            public string nome;
            public int[] cor;
            public float rugosidade;
            public float metal;
            public string textura;
        }

        [MenuItem("BackRooms/Móveis do Level 1/Atualizar e fotografar")]
        public static void UpdateAndCaptureFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            UpdateModels();
            string folder = Capture(DefaultFolder());
            if (folder != null) EditorUtility.RevealInFinder(folder);
        }

        /// <summary>Linha de comando: atualiza materiais e modelos, fotografa e fecha (0 = ok).</summary>
        public static void BatchUpdateAndCapture()
        {
            try
            {
                UpdateModels();
                bool ok = Capture(DefaultFolder()) != null;
                EditorApplication.Exit(ok ? 0 : 2);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Cria ou atualiza os materiais pela paleta e reimporta todos os modelos.</summary>
        public static void UpdateModels()
        {
            EnsureFolder(MaterialFolder);

            // Uma paleta por lote; um nome repetido em dois lotes tem de ter a mesma cor (o
            // moveis_base.py recusa redefinir cor), então o último lido vale.
            var palette = new Palette();
            string[] files = Directory.GetFiles(ModelFolder, "materiais_*.json");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
                palette.materiais.AddRange(JsonUtility.FromJson<Palette>(File.ReadAllText(file)).materiais);

            if (palette.materiais.Count == 0)
            {
                Debug.LogError("[BackRooms] Nenhum materiais_*.json em " + ModelFolder + ". Rode antes um lote do Blender (Tools/Blender/lotes).");
                return;
            }

            ConfigureTextures();
            foreach (Entry entry in palette.materiais) EnsureMaterial(entry);
            AssetDatabase.SaveAssets();

            // Troca cada material embutido no FBX pelo do projeto com o mesmo nome. A troca fica
            // gravada no .meta do modelo, então sobrevive a reimportações.
            var materials = new List<Material>();
            foreach (Entry entry in palette.materiais)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + entry.nome + ".mat");
                if (material != null) materials.Add(material);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ModelFolder }))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                foreach (Material material in materials)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
                importer.SaveAndReimport();
            }

            Debug.Log($"[BackRooms] {palette.materiais.Count} materiais e os modelos de {ModelFolder} atualizados.");
        }

        static void EnsureMaterial(Entry entry)
        {
            string path = MaterialFolder + "/" + entry.nome + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;

            if (created)
            {
                Shader shader = Shader.Find("HDRP/Lit");
                if (shader == null)
                {
                    Debug.LogError("[BackRooms] Shader HDRP/Lit não encontrado.");
                    return;
                }
                material = new Material(shader) { name = entry.nome };
            }

            material.SetColor("_BaseColor", new Color(entry.cor[0] / 255f, entry.cor[1] / 255f, entry.cor[2] / 255f));
            Texture2D texture = string.IsNullOrEmpty(entry.textura)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + entry.textura + ".png");
            if (!string.IsNullOrEmpty(entry.textura) && texture == null)
                Debug.LogError($"[BackRooms] Textura {entry.textura} do material {entry.nome} não encontrada em {TextureFolder}.");
            material.SetTexture("_BaseColorMap", texture);
            material.SetFloat("_Smoothness", 1f - entry.rugosidade);
            material.SetFloat("_Metallic", entry.metal);
            HDMaterial.ValidateMaterial(material);

            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
        }

        // As texturas do Blender se repetem a cada metro (UV de 1 m) e são cor (sRGB).
        static void ConfigureTextures()
        {
            if (!AssetDatabase.IsValidFolder(TextureFolder)) return;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (importer.wrapMode == TextureWrapMode.Repeat && importer.anisoLevel == 8 && importer.sRGBTexture) continue;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 8;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
        }

        // ---------------------------------------------------------------- Fotos

        static string DefaultFolder()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews"));
        }

        static GameObject Model(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + "/" + name + ".fbx");
        }

        static string Capture(string folder)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var builder = UnityEngine.Object.FindFirstObjectByType<LevelBuilder>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (builder == null || prefab == null)
            {
                Debug.LogError("[BackRooms] A cena não tem LevelBuilder ou falta o prefab do jogador.");
                return null;
            }

            GameObject rig = Level0Preview.CreateCameraRig(prefab, out Camera camera);
            Directory.CreateDirectory(folder);

            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var placed = new List<GameObject>();

            try
            {
                builder.Rebuild();
                var camcorder = UnityEngine.Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();
                if (camcorder != null) camcorder.Draw(true, 95.2f, 95.2f, 0.9f, false);

                camera.targetTexture = target;
                Transform player = rig.transform;
                Vector3 room = builder.SpawnCenter;
                room.y = 0f;

                // Um por um, no meio da sala inicial, de frente e um pouco de lado. Todos os
                // modelos da pasta, ou só os de "-moveis Nome1,Nome2" na linha de comando.
                var names = new List<string>();
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ModelFolder }))
                    names.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));
                names.Sort(StringComparer.Ordinal);
                string only = ArgumentAfter("-moveis");
                if (!string.IsNullOrEmpty(only)) names = new List<string>(only.Split(','));

                foreach (string name in names)
                {
                    GameObject model = Model(name);
                    if (model == null)
                    {
                        Debug.LogError("[BackRooms] Modelo não encontrado: " + name);
                        continue;
                    }

                    GameObject item = Place(model, room, 0f, placed);
                    Bounds bounds = BoundsOf(item);
                    Debug.Log($"[BackRooms] {name}: {bounds.size.x:F2} x {bounds.size.y:F2} x {bounds.size.z:F2} m (largura x altura x fundo)");

                    // A frente do móvel é o +Z dele; a câmera fica na frente, 25° de lado.
                    float distance = Mathf.Max(1.6f, Mathf.Max(bounds.size.x, bounds.size.y) * 1.25f + 0.8f);
                    Vector3 eye = room + Quaternion.Euler(0f, 25f, 0f) * Vector3.forward * distance;
                    Frame(player, camera, eye, bounds.center);
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "moveis_" + name + ".png"));

                    Clear(placed);
                }

                // Vitrine de jantar: mesa, seis cadeiras viradas para ela e a placa de oferta.
                Place(Model("MesaDeJantar"), room, 0f, placed);
                for (int i = 0; i < 3; i++)
                {
                    float x = (i - 1) * 0.62f;
                    Place(Model("Cadeira"), room + new Vector3(x, 0f, 0.62f), 180f + (i - 1) * 4f, placed);
                    Place(Model("Cadeira"), room + new Vector3(x + 0.03f, 0f, -0.62f), (i - 1) * -5f, placed);
                }
                Place(Model("PlacaOferta"), room + new Vector3(0.55f, 0.76f, 0.05f), 150f, placed);
                Frame(player, camera, room + new Vector3(2.4f, 0f, -2.6f), room + new Vector3(0f, 0.6f, 0f));
                Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "moveis_vitrine_jantar.png"));
                Clear(placed);

                // Sala copiada errado: sofá e poltrona, estante e guarda-roupa encostados no fundo,
                // e uma pilha jogada de cadeiras e caixas no canto (como no MECCHA CHAMELEON).
                Place(Model("Sofa"), room + new Vector3(-0.4f, 0f, 0.2f), 180f, placed);
                Place(Model("Poltrona"), room + new Vector3(1.4f, 0f, -0.5f), 230f, placed);
                Place(Model("Estante"), room + new Vector3(-1.5f, 0f, 2.6f), 180f, placed);
                Place(Model("GuardaRoupa"), room + new Vector3(0.2f, 0f, 2.55f), 180f, placed);
                Place(Model("EstanteDeAcoCheia"), room + new Vector3(2.6f, 0f, 1.6f), 270f, placed);
                Place(Model("PilhaDeCaixas"), room + new Vector3(-2.4f, 0f, 0.4f), 20f, placed);
                var rng = new System.Random(5);
                for (int i = 0; i < 4; i++)
                {
                    GameObject chair = Place(Model("Cadeira"), room + new Vector3(-2.5f + (float)rng.NextDouble() * 0.5f, 0.02f + i * 0.28f, -1.2f + (float)rng.NextDouble() * 0.4f),
                                             (float)rng.NextDouble() * 360f, placed);
                    chair.transform.rotation *= Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 50f, 0f, (float)(rng.NextDouble() - 0.5) * 70f);
                }
                Frame(player, camera, room + new Vector3(1.0f, 0f, -3.3f), room + new Vector3(-0.3f, 0.7f, 0.8f));
                Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "moveis_sala.png"));
                Clear(placed);

                // Loja em liquidação: poltronas reclináveis soltas pelo salão, placas penduradas
                // do teto, o balcão com o monitor e as torres expositoras vazias.
                if (Model("BalcaoDeCaixa") != null)
                {
                    Place(Model("BalcaoDeCaixa"), room + new Vector3(-1.4f, 0f, 2.7f), 180f, placed);
                    Place(Model("MonitorCRT"), room + new Vector3(-1.0f, 1.05f, 2.75f), 200f, placed);
                    Place(Model("TorreExpositora"), room + new Vector3(2.4f, 0f, 2.6f), 160f, placed);
                    Place(Model("TorreExpositora"), room + new Vector3(3.2f, 0f, 1.2f), 250f, placed);
                    Place(Model("EstanteAbertaMadeira"), room + new Vector3(-3.4f, 0f, 0.6f), 90f, placed);
                    Place(Model("MesaSimples"), room + new Vector3(-2.6f, 0f, -1.8f), 70f, placed);
                    Place(Model("PoltronaReclinavelBege"), room + new Vector3(0.6f, 0f, 0.4f), 200f, placed);
                    Place(Model("PoltronaReclinavelAzul"), room + new Vector3(-1.2f, 0f, -0.3f), 150f, placed);
                    Place(Model("PoltronaReclinavelBege"), room + new Vector3(2.0f, 0f, -0.9f), 235f, placed);
                    Place(Model("PoltronaReclinavelAzul"), room + new Vector3(-0.4f, 0f, 1.6f), 175f, placed);
                    Place(Model("CilindroAzul"), room + new Vector3(-3.3f, 0f, 2.3f), 0f, placed);
                    Place(Model("PlacaPenduradaOferta"), room + new Vector3(-1.6f, 2.45f, 0.9f), 180f, placed);
                    Place(Model("PlacaPenduradaLiquida"), room + new Vector3(1.8f, 2.45f, 1.6f), 200f, placed);
                    Frame(player, camera, room + new Vector3(0.6f, 0f, -3.5f), room + new Vector3(0f, 1.1f, 1.2f));
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "moveis_loja.png"));
                    Clear(placed);
                }

                // A sala dos sofás (como no filme): a sala inicial entupida de estofados marrons
                // listrados, de parede a parede, virados para todo lado, uns empilhados, uns de
                // pé sobre o braço, uns tombados. Sobra só uma passagem estreita.
                if (Model("SofaMarromListrado") != null)
                {
                    PackSofas(room, placed);
                    Frame(player, camera, room + new Vector3(0.2f, 0f, -3.55f), room + new Vector3(-0.2f, 0.75f, 1.5f));
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "moveis_sala_dos_sofas.png"));
                    Frame(player, camera, room + new Vector3(3.3f, 0f, 3.3f), room + new Vector3(-0.5f, 0.6f, -0.5f));
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "moveis_sala_dos_sofas_2.png"));
                    Clear(placed);
                }

                Debug.Log("[BackRooms] Fotos dos móveis salvas em " + folder);
                return folder;
            }
            finally
            {
                Clear(placed);
                camera.targetTexture = null;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(rig);
                ShaderUtil.allowAsyncCompilation = asyncBefore;
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        // Enche a sala inicial (8 x 8 m) de estofados marrons numa grade folgada com sorteio
        // determinístico: giro qualquer, alguns em cima de outros, alguns de pé sobre o braço,
        // alguns tombados para trás. Deixa livre um corredor da porta (em -Z) até o meio.
        static void PackSofas(Vector3 room, List<GameObject> placed)
        {
            GameObject[] kinds =
            {
                Model("SofaMarromListrado"), Model("NamoradeiraMarromListrada"), Model("PoltronaMarromListrada"),
            };
            var rng = new System.Random(1998);
            float R() => (float)rng.NextDouble();

            for (int gx = 0; gx < 4; gx++)
            {
                for (int gz = 0; gz < 4; gz++)
                {
                    float x = -2.85f + gx * 1.9f + (R() - 0.5f) * 0.35f;
                    float z = -2.7f + gz * 1.8f + (R() - 0.5f) * 0.35f;
                    if (gz == 0 && (gx == 1 || gx == 2)) continue; // passagem na frente da câmera

                    GameObject kind = kinds[rng.Next(kinds.Length)];
                    if (kind == null) continue;
                    float yaw = rng.Next(4) * 90f + (R() - 0.5f) * 40f;
                    double roll = R();

                    if (roll < 0.15)
                    {
                        // De pé sobre o braço: a largura vira altura.
                        GameObject up = Place(kind, room + new Vector3(x, 0f, z), yaw, placed);
                        Bounds b = BoundsOf(up);
                        up.transform.rotation *= Quaternion.Euler(0f, 0f, 90f);
                        up.transform.position += Vector3.up * (b.size.x * 0.5f - 0.05f);
                        continue;
                    }

                    GameObject below = Place(kind, room + new Vector3(x, 0f, z), yaw, placed);
                    if (roll < 0.3)
                    {
                        // Tombado de costas: o encosto no chão, o assento virado para cima.
                        below.transform.rotation *= Quaternion.Euler(-80f, 0f, 0f);
                        below.transform.position += Vector3.up * 0.42f;
                    }
                    else if (roll < 0.62)
                    {
                        // Outro por cima, torto, apoiado no de baixo.
                        float height = BoundsOf(below).size.y;
                        GameObject top = Place(kinds[rng.Next(kinds.Length)] ?? kind,
                                               room + new Vector3(x + (R() - 0.5f) * 0.4f, height - 0.18f, z + (R() - 0.5f) * 0.4f),
                                               yaw + (R() - 0.5f) * 70f, placed);
                        top.transform.rotation *= Quaternion.Euler((R() - 0.5f) * 24f, 0f, (R() - 0.5f) * 18f);
                    }
                }
            }
        }

        static string ArgumentAfter(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }

        // Põe o modelo no chão, virado para yaw graus (0 = frente para +Z). Modelo que não
        // existe (lote ainda não gerado) vira um objeto vazio, para a foto seguir sem ele.
        static GameObject Place(GameObject model, Vector3 position, float yaw, List<GameObject> placed)
        {
            if (model == null)
            {
                var empty = new GameObject("Faltando");
                placed.Add(empty);
                return empty;
            }

            var item = (GameObject)UnityEngine.Object.Instantiate(model);
            item.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            placed.Add(item);
            return item;
        }

        static void Clear(List<GameObject> placed)
        {
            foreach (GameObject item in placed)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            placed.Clear();
        }

        static Bounds BoundsOf(GameObject item)
        {
            var renderers = item.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(item.transform.position, Vector3.zero);
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        // Câmera (no suporte do jogador, a 1,65 m do chão) olhando para um ponto.
        static void Frame(Transform player, Camera camera, Vector3 eye, Vector3 target)
        {
            Vector3 flat = target - eye;
            flat.y = 0f;
            player.SetPositionAndRotation(new Vector3(eye.x, 0f, eye.z), Quaternion.LookRotation(flat.normalized, Vector3.up));
            Vector3 camPos = player.position + Vector3.up * camera.transform.localPosition.y;
            float pitch = -Mathf.Atan2(target.y - camPos.y, flat.magnitude) * Mathf.Rad2Deg;
            camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }

    /// <summary>
    /// Configurações fixas de importação dos modelos do Blender em Art/Level1/Modelos. Mudar
    /// algo aqui pede reimportar os modelos (Móveis do Level 1 > Atualizar e fotografar).
    /// </summary>
    public sealed class Level1ModelImporter : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Level1Models.ModelFolder + "/", StringComparison.Ordinal)) return;

            var importer = (ModelImporter)assetImporter;

            // Escala: o Blender exporta em metros (1 unidade = 1 m), nada de corrigir no Unity.
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = false;

            // Só malha: sem animação, esqueleto, câmeras ou luzes.
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;

            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.generateSecondaryUV = false;
            importer.addCollider = false;

            // Materiais: o FBX traz só o nome; Level1Models.UpdateModels troca cada um pelo
            // HDRP Lit do projeto (a troca fica gravada no .meta do modelo).
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }
    }
}
