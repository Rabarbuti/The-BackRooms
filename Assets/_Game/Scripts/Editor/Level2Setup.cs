using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// O Level 2 de teste, As Piscinas (Docs/level2-piscinas.md): a planta do Level 0 em 16 x 16
    /// células, sem buracos, nichos nem entidade, com profundidade por célula (seco, raso, fundo,
    /// poço), escadas e uma lâmina d'água. Tudo de azulejo branco; claraboias no lugar das
    /// luminárias.
    ///
    /// Texturas: as da Folha A gerada no Higgsfield (Art/Fontes/Level2: azulejo, pastilha, borda);
    /// a ondulação da água e o vidro fosco das claraboias são pintados aqui em código até as
    /// folhas B, C e D ficarem prontas. Os materiais saem em Art/Level2.
    ///
    /// Menu BackRooms > Montar Level 2 (teste) e Conferir Level 2 (Level2Setup.BatchCheck pela
    /// linha de comando: confere várias seeds e tira fotos em Logs/Previews/level2_*.png).
    /// </summary>
    public static class Level2Setup
    {
        public const string ScenePath = "Assets/_Game/Scenes/Level2_Test.unity";
        const string SourceFolder = "Assets/_Game/Art/Fontes/Level2";
        const string ArtFolder = "Assets/_Game/Art/Level2";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        const int Size = 16;
        static readonly int[] CheckSeeds = { 1998, 7, 4242, 31337, 90210 };

        [MenuItem("BackRooms/Abrir Level 2 (teste)", priority = 0)]
        public static void OpenFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) BuildScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[BackRooms] Level 2 aberto. Aperte Play e depois H para hospedar.");
        }

        [MenuItem("BackRooms/Montar Level 2 (teste)")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        public static void BuildScene()
        {
            PoolMaterials materials = EnsureMaterials();

            Level0Setup.BuildScene(false, new Level0Setup.SceneOptions
            {
                scenePath = ScenePath,
                levelName = "Level2",
                size = Size,
                pits = false,
                alcoves = false,
                entity = false,
                paintedArrows = false,
                pools = true,
                decorate = level => Apply(level.GetComponent<LevelBuilder>(), materials),
            });
            Debug.Log($"[BackRooms] Level 2 de teste montado em {ScenePath}: abra a cena, aperte Play e hospede (H).");
        }

        static void Apply(LevelBuilder builder, PoolMaterials m)
        {
            builder.wallMaterial = m.Tile;
            builder.floorMaterial = m.Tile;
            builder.ceilingMaterial = m.Tile;
            builder.cleanWallMaterial = m.Tile;
            builder.cleanFloorMaterial = m.Tile;
            builder.baseboardMaterial = m.Coping;
            builder.poolBottomMaterial = m.Mosaic;
            builder.copingMaterial = m.Coping;
            builder.waterMaterial = m.Water;

            // Claraboias de vidro fosco, sem calha, com luz quente de fim de tarde.
            builder.panelOnMaterial = m.SkylightOn;
            builder.panelOffMaterial = m.SkylightOff;
            builder.lightHousingMaterial = null;
            builder.panelScale = 2f;
            builder.lightTemperature = 4300f;
            builder.lightCandela = 3200f;
            builder.lightRange = 18f;
            builder.ventMaterial = null;
        }

        // ---------------------------------------------------------------- Conferência

        [MenuItem("BackRooms/Conferir Level 2")]
        public static void CheckFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            bool ok = Check(out string report);
            Debug.Log("[BackRooms] Conferência do Level 2 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
            EditorUtility.DisplayDialog("Level 2", (ok ? "PASSOU\n\n" : "FALHOU\n\n") + report, "OK");
        }

        /// <summary>Linha de comando: monta, confere e fotografa. Sai com 0 se passou, 3 se falhou.</summary>
        public static void BatchCheck()
        {
            try
            {
                bool ok = Check(out string report);
                Debug.Log("[BackRooms] Conferência do Level 2 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
                Level1Setup.ExitWithLevel0Open(ok ? 0 : 3);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Level1Setup.ExitWithLevel0Open(1);
            }
        }

        static bool Check(out string report)
        {
            BuildScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var builder = Object.FindFirstObjectByType<LevelBuilder>();
            if (builder == null || !builder.poolsEnabled)
            {
                report = "a cena não tem LevelBuilder com as Piscinas ligadas";
                return false;
            }

            var problems = new List<string>();
            var lines = new List<string>();

            try
            {
                foreach (int seed in CheckSeeds)
                {
                    LevelData a = LevelGenerator.Generate(seed, Size, Size, false, false, true);
                    LevelData b = LevelGenerator.Generate(seed, Size, Size, false, false, true);
                    if (DepthHash(a) != DepthHash(b)) problems.Add($"seed {seed}: a mesma seed deu profundidades ou escadas diferentes");

                    CheckLayout(a, seed, problems, out string summary);
                    lines.Add($"seed {seed}: {summary} hash={DepthHash(a):X8}");
                }

                builder.seed = CheckSeeds[0];
                builder.Rebuild();
                lines.Add("fotos: " + Capture(builder));
            }
            finally
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            report = string.Join("\n", lines) + (problems.Count > 0 ? "\nPROBLEMAS:\n" + string.Join("\n", problems) : "");
            return problems.Count == 0;
        }

        // Regras da planta: saída e sala inicial secas, seco nunca encosta em fundo, poço só
        // encosta em fundo ou poço, toda célula sem poço alcançável a pé, uma escada (de 3 ou 4
        // degraus) em cada passagem entre alturas diferentes que se anda.
        static void CheckLayout(LevelData d, int seed, List<string> problems, out string summary)
        {
            var counts = new int[4];
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    counts[(int)d.Depth[x, z]]++;

            if (d.Depth[d.ExitX, d.ExitZ] != PoolDepth.Dry) problems.Add($"seed {seed}: a saída não está seca");
            if (d.Depth[d.SpawnX, d.SpawnZ] != PoolDepth.Dry) problems.Add($"seed {seed}: a sala inicial não está seca");
            if (!LevelGenerator.AllReachableOnFoot(d)) problems.Add($"seed {seed}: há célula sem poço que não se alcança a pé");
            if (LevelGenerator.FootPath(d).Count < 2) problems.Add($"seed {seed}: não há caminho a pé até a saída");

            int expectedStairs = 0;
            WallSide[] once = { WallSide.South, WallSide.West };
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    foreach (WallSide side in once)
                    {
                        if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                        PoolDepth p = d.Depth[x, z], q = d.Depth[nx, nz];
                        bool dryDeep = (p == PoolDepth.Dry && q == PoolDepth.Deep) || (p == PoolDepth.Deep && q == PoolDepth.Dry);
                        if (dryDeep) problems.Add($"seed {seed}: seco encosta em fundo em ({x},{z})-({nx},{nz})");
                        bool well = p == PoolDepth.Well || q == PoolDepth.Well;
                        if (well && p != q && p != PoolDepth.Deep && q != PoolDepth.Deep)
                            problems.Add($"seed {seed}: poço encosta em {(p == PoolDepth.Well ? q : p)} em ({x},{z})-({nx},{nz})");
                        if (!well && p != q) expectedStairs++;
                    }

            if (d.Stairs.Count != expectedStairs) problems.Add($"seed {seed}: {d.Stairs.Count} escadas para {expectedStairs} desníveis");
            foreach (PoolStair stair in d.Stairs)
                if (stair.Steps < 3 || stair.Steps > 4) problems.Add($"seed {seed}: escada de {stair.Steps} degraus em ({stair.X},{stair.Z})");
            if (counts[(int)PoolDepth.Deep] + counts[(int)PoolDepth.Well] == 0) problems.Add($"seed {seed}: nenhuma área funda");

            summary = $"seco {counts[0]}, raso {counts[1]}, fundo {counts[2]}, poço {counts[3]}, {d.Stairs.Count} escadas, caminho a pé de {LevelGenerator.FootPath(d).Count} células";
        }

        /// <summary>Resumo das profundidades e escadas, para comparar duas gerações (ou duas máquinas).</summary>
        public static uint DepthHash(LevelData d)
        {
            uint hash = 2166136261;
            void Mix(int value) { unchecked { hash = (hash ^ (uint)value) * 16777619u; } }

            foreach (PoolDepth depth in d.Depth) Mix((int)depth);
            foreach (PoolStair stair in d.Stairs) { Mix(stair.X); Mix(stair.Z); Mix((int)stair.Side); Mix(stair.Steps); }
            Mix(d.ExitX); Mix(d.ExitZ);
            return hash;
        }

        // ---------------------------------------------------------------- Fotos

        static string Capture(LevelBuilder builder)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) return "sem o prefab do jogador";

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews"));
            Directory.CreateDirectory(folder);
            GameObject rig = Level0Preview.CreateCameraRig(prefab, out Camera camera);

            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            int shots = 0;

            try
            {
                var camcorder = Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();
                if (camcorder != null) camcorder.Draw(true, 61.4f, 61.4f, 0.95f, false);
                camera.targetTexture = target;

                LevelData d = builder.Data;
                WallSide[] sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

                void Shot(string name, Vector3 eye, Vector3 look, float pitch)
                {
                    look.y = 0f;
                    rig.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look.normalized, Vector3.up));
                    camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, name));
                    shots++;
                }

                // Da sala inicial para fora, nas quatro direções.
                for (int i = 0; i < 4; i++)
                    Shot($"level2_inicio_{i * 90}.png", builder.SpawnCenter, Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward, 5f);

                // Uma escada de cada tipo (seco-raso e raso-fundo), vista de cima, da célula de cima.
                var seen = new HashSet<int>();
                foreach (PoolStair stair in d.Stairs)
                {
                    if (!seen.Add(stair.Steps)) continue;
                    d.Neighbor(stair.X, stair.Z, stair.Side, out int ux, out int uz);
                    Vector3 from = builder.CellCenter(ux, uz);
                    Vector3 to = builder.CellCenter(stair.X, stair.Z);
                    Shot($"level2_escada_{stair.Steps}_degraus.png", from, to - from, 22f);
                }

                // A primeira área funda e o primeiro poço, vistos de uma vizinha aberta.
                foreach (PoolDepth kind in new[] { PoolDepth.Deep, PoolDepth.Well })
                {
                    bool done = false;
                    for (int z = 0; z < d.Height && !done; z++)
                        for (int x = 0; x < d.Width && !done; x++)
                        {
                            if (d.Depth[x, z] != kind) continue;
                            foreach (WallSide side in sides)
                            {
                                if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                                if (kind == PoolDepth.Deep && d.Depth[nx, nz] == PoolDepth.Deep) continue;
                                Vector3 from = builder.CellCenter(nx, nz);
                                if (d.Depth[nx, nz] == PoolDepth.Deep) from.y = 0f; // de pé no fundo, olhos fora d'água
                                Vector3 to = builder.CellCenter(x, z);
                                Shot(kind == PoolDepth.Deep ? "level2_area_funda.png" : "level2_poco.png", from, to - from, kind == PoolDepth.Well ? 35f : 15f);
                                done = true;
                                break;
                            }
                        }
                }

                // Debaixo d'água, num poço, olhando para o lado.
                for (int z = 0; z < d.Height; z++)
                {
                    int x = Enumerable.Range(0, d.Width).FirstOrDefault(i => d.Depth[i, z] == PoolDepth.Well);
                    if (d.Depth[x, z] != PoolDepth.Well) continue;
                    var water = builder.GetComponentInChildren<PoolWater>();
                    Vector3 eye = builder.CellCenter(x, z) + Vector3.up * 3.2f; // olhos a ~1,2 m abaixo da superfície
                    if (camcorder != null) camcorder.Draw(true, 61.4f, 61.4f, 0.95f, false, null, false, null, true, 0.4f);
                    Shot("level2_debaixo_dagua.png", eye, Vector3.forward, 10f);
                    if (camcorder != null) camcorder.Draw(true, 61.4f, 61.4f, 0.95f, false);
                    break;
                }

                // Corredor raso qualquer, longe do início.
                List<int> path = LevelGenerator.FootPath(d);
                if (path.Count > 6)
                {
                    int i = path.Count * 2 / 3;
                    Vector3 from = builder.CellCenter(path[i] % d.Width, path[i] / d.Width);
                    Vector3 to = builder.CellCenter(path[i + 1] % d.Width, path[i + 1] / d.Width);
                    Shot("level2_corredor.png", from, to - from, 3f);
                }
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(rig);
                ShaderUtil.allowAsyncCompilation = asyncBefore;
            }

            return $"{shots} em {folder}";
        }

        // ---------------------------------------------------------------- Materiais

        sealed class PoolMaterials
        {
            public Material Tile, Mosaic, Coping, Water, SkylightOn, SkylightOff;
        }

        static PoolMaterials EnsureMaterials()
        {
            EnsureFolder(ArtFolder);

            Texture2D tile = Source("Tile_White");
            Texture2D mosaic = Source("Mosaic_Pool");
            Texture2D coping = Source("Coping");
            Texture2D ripple = Painted("Water_Height", 512, PaintRipples, normalMap: true);
            Texture2D frosted = Painted("Skylight_Glow", 256, PaintFrosted, normalMap: false);

            var m = new PoolMaterials
            {
                Tile = Lit("Tile_White", Color.white, tile, null, 0.72f),
                Mosaic = Lit("Mosaic_Pool", Color.white, mosaic, null, 0.72f),
                Coping = Lit("Coping", Color.white, coping, null, 0.3f),
                SkylightOn = Lit("Skylight_On", Color.white, frosted, null, 0.4f, new Color(1f, 0.93f, 0.8f) * 1400f, frosted),
                SkylightOff = Lit("Skylight_Off", new Color(0.42f, 0.47f, 0.52f), frosted, null, 0.4f),
            };

            m.Water = Lit("Water", new Color(0.36f, 0.78f, 0.74f, 0.3f), null, ripple, 0.95f);
            if (m.Water != null)
            {
                // Transparente, misturando pela opacidade da cor; a ondulação é o mapa de normais,
                // e a textura cobre 4 m (o mesmo tamanho da célula).
                m.Water.SetFloat("_SurfaceType", 1f);
                m.Water.SetFloat("_BlendMode", 0f);
                m.Water.SetFloat("_ZWrite", 0f);
                m.Water.SetFloat("_NormalScale", 0.6f);
                m.Water.SetFloat("_EnableFogOnTransparent", 1f);
                HDMaterial.ValidateMaterial(m.Water);
                EditorUtility.SetDirty(m.Water);
            }

            AssetDatabase.SaveAssets();
            return m;
        }

        static Texture2D Source(string name)
        {
            string path = $"{SourceFolder}/{name}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[BackRooms] Textura {path} não encontrada.");
                return null;
            }

            if (importer.wrapMode != TextureWrapMode.Repeat || importer.anisoLevel != 8)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 8;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Lit(string name, Color baseColor, Texture2D albedo, Texture2D normal, float smoothness,
                            Color emissive = default, Texture2D emissiveMap = null)
        {
            string path = $"{ArtFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created)
            {
                Shader shader = Shader.Find("HDRP/Lit");
                if (shader == null) { Debug.LogError("[BackRooms] Shader HDRP/Lit não encontrado."); return null; }
                material = new Material(shader) { name = name };
            }

            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetTexture("_BaseColorMap", albedo);
            material.SetTexture("_NormalMap", normal);
            if (normal != null) material.SetFloat("_NormalScale", 1f);
            material.SetColor("_EmissiveColor", emissive);
            material.SetTexture("_EmissiveColorMap", emissiveMap);

            HDMaterial.ValidateMaterial(material);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        // Textura pintada em código (provisória, até a folha do Higgsfield). Fica em Art/Level2.
        static Texture2D Painted(string name, int size, Func<int, int, int, Color32> paint, bool normalMap)
        {
            string path = $"{ArtFolder}/{name}.png";
            if (!File.Exists(path))
            {
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        pixels[y * size + x] = paint(x, y, size);

                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 4;
                if (normalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.convertToNormalmap = true;
                    importer.heightmapScale = 0.05f;
                }
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Ondulação: soma de ondas com número inteiro de voltas na textura, para emendar sozinha.
        static Color32 PaintRipples(int x, int y, int size)
        {
            float u = x / (float)size * Mathf.PI * 2f, v = y / (float)size * Mathf.PI * 2f;
            float h = Mathf.Sin(u * 3f + v * 2f) * 0.35f + Mathf.Sin(u * -2f + v * 5f + 1.3f) * 0.25f
                    + Mathf.Sin(u * 7f + v * 3f + 2.1f) * 0.15f + Mathf.Sin(u * -5f + v * -8f + 0.4f) * 0.1f
                    + Mathf.Sin(u * 11f - v * 6f + 2.8f) * 0.08f;
            byte c = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f + h * 0.5f) * 255f);
            return new Color32(c, c, c, 255);
        }

        // Vidro fosco da claraboia: claro, mais claro no meio, com o caixilho fino nas bordas.
        static Color32 PaintFrosted(int x, int y, int size)
        {
            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
            int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
            if (edge < 5) return new Color32(70, 70, 68, 255);
            if (edge < 8) return new Color32(150, 150, 146, 255);
            float center = 1f - Mathf.Clamp01(Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 1.2f);
            float mullion = Mathf.Abs(u - 0.5f) < 0.008f || Mathf.Abs(v - 0.5f) < 0.008f ? 0.55f : 1f;
            byte c = (byte)Mathf.RoundToInt(Mathf.Lerp(200f, 255f, center) * mullion);
            return new Color32(c, c, (byte)Mathf.Min(255, c - 6), 255);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
