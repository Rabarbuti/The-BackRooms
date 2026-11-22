using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HD = UnityEngine.Rendering.HighDefinition;
using R = UnityEngine.Rendering;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Fotos da luz do Level 0 nos lugares onde ela costuma faltar: beco ao lado de uma
    /// luminária, canto de célula acesa, canto de célula apagada vizinha de uma acesa, nicho e a
    /// célula mais longe de qualquer luz. Várias seeds e, para achar a causa de um canto escuro,
    /// variantes montadas na hora (nada é salvo): com luz ambiente, sem sombra, com a luz indo
    /// mais longe e no perfil de qualidade mais leve.
    ///
    /// Cada foto sai sem a filmadora (é nela que se mede quanto da imagem está preta) e, nas
    /// variantes de <c>-luzFilmadora</c>, também com ela. As medidas vão para o log
    /// (<c>[BackRooms] luz0 ...</c>) e para <c>medidas.txt</c> na pasta.
    ///
    /// Linha de comando: <c>-executeMethod BackRooms.EditorTools.Level0LightPreview.BatchRun</c>
    /// com <c>-luzTag antes</c> (pasta Logs/Previews/luz0/&lt;tag&gt;), <c>-luzSeeds 1998,7,4242</c>,
    /// <c>-luzVariantes base,ambiente1,...</c> e <c>-luzFilmadora base</c>. Não salva a cena.
    /// </summary>
    public static class Level0LightPreview
    {
        const string ScenePath = "Assets/_Game/Scenes/Level0_Test.unity";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";

        // Pixel "preto": luminância abaixo disto (0 a 1, depois do tonemapping), uns 9/255.
        const float BlackLevel = 0.035f;

        // Céu em degradê do perfil original do Level 0 (antes de 12/11/2026), em nits.
        static readonly Color SkyTop = new Color(2.4f, 2.3f, 1.3f);
        static readonly Color SkyMiddle = new Color(3.6f, 3.5f, 2.0f);
        static readonly Color SkyBottom = new Color(7.0f, 6.8f, 3.8f);

        sealed class Variant
        {
            public string Name;
            public float Ambient;          // multiplicador do céu de luz ambiente; 0 = como a cena está
            public float Fade = -1f;       // distância de corte das luminárias; negativo = a da cena
            public bool Shadows = true;
            public string Quality;         // nome do perfil de qualidade; nulo = o atual
            public bool Blackout;          // fotografa no apagão (luminárias e luz ambiente apagadas)
        }

        static readonly Variant[] AllVariants =
        {
            new Variant { Name = "base" },
            new Variant { Name = "ambiente1", Ambient = 1f },
            new Variant { Name = "ambiente2", Ambient = 2f },
            new Variant { Name = "ambiente4", Ambient = 4f },
            new Variant { Name = "ambiente8", Ambient = 8f },
            new Variant { Name = "fade60", Fade = 60f },
            new Variant { Name = "semsombra", Shadows = false },
            new Variant { Name = "performant", Quality = "Performant" },
            new Variant { Name = "apagao", Blackout = true },
            new Variant { Name = "apagao_ambiente2", Ambient = 2f, Blackout = true },
        };

        struct Spot
        {
            public string Name;
            public Vector3 Position;
            public Vector3 Look;
        }

        [MenuItem("BackRooms/Fotografar luz do Level 0")]
        public static void CaptureFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string folder = Path.Combine(DefaultFolder(), "editor");
            if (Capture(folder, new[] { 1998, 7, 4242 }, new[] { "base" }, new[] { "base" }))
                EditorUtility.RevealInFinder(folder);
        }

        public static void BatchRun()
        {
            try
            {
                string tag = ArgumentAfter("-luzTag") ?? "antes";
                string folder = ArgumentAfter("-luzOut") ?? Path.Combine(DefaultFolder(), tag);
                int[] seeds = ParseInts(ArgumentAfter("-luzSeeds") ?? "1998,7,4242");
                string[] variants = (ArgumentAfter("-luzVariantes") ?? "base,ambiente1,ambiente2,ambiente4,ambiente8,fade60,semsombra,performant,apagao,apagao_ambiente2").Split(',');
                string[] withCamcorder = (ArgumentAfter("-luzFilmadora") ?? "base").Split(',');
                EditorApplication.Exit(Capture(folder, seeds, variants, withCamcorder) ? 0 : 2);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static bool Capture(string folder, int[] seeds, string[] variantNames, string[] withCamcorder)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var builder = UnityEngine.Object.FindFirstObjectByType<LevelBuilder>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (builder == null || prefab == null)
            {
                Debug.LogError("[BackRooms] luz0: a cena não tem LevelBuilder ou falta o prefab do jogador.");
                return false;
            }

            var variants = new List<Variant>();
            foreach (string name in variantNames)
            {
                Variant found = Array.Find(AllVariants, v => v.Name == name.Trim());
                if (found == null) { Debug.LogError("[BackRooms] luz0: variante desconhecida " + name); return false; }
                variants.Add(found);
            }

            Directory.CreateDirectory(folder);
            GameObject rig = Level0Preview.CreateCameraRig(prefab, out Camera camera);
            Transform player = rig.transform;

            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            int qualityBefore = QualitySettings.GetQualityLevel();
            float fadeBefore = builder.lightFadeDistance;
            bool shadowsBefore = builder.ceilingShadows;

            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            camera.targetTexture = target;

            var camcorder = UnityEngine.Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();
            if (camcorder != null) camcorder.Draw(true, 207.47f, 207.47f, 0.8f, false);
            var passes = UnityEngine.Object.FindObjectsByType<HD.CustomPassVolume>(FindObjectsSortMode.None);

            var report = new StringBuilder();
            report.AppendLine($"Fotos da luz do Level 0 ({DateTime.Now:yyyy-MM-dd HH:mm}). Preto = luminância < {BlackLevel} na foto sem filmadora.");
            var totals = new Dictionary<string, (double black, double mean, int count)>();
            GameObject ambient = null;

            try
            {
                foreach (int seed in seeds)
                {
                    foreach (Variant variant in variants)
                    {
                        if (variant.Quality != null)
                        {
                            int index = Array.IndexOf(QualitySettings.names, variant.Quality);
                            if (index < 0) { Debug.LogError("[BackRooms] luz0: perfil de qualidade não existe: " + variant.Quality); return false; }
                            QualitySettings.SetQualityLevel(index, true);
                        }
                        else
                        {
                            QualitySettings.SetQualityLevel(qualityBefore, true);
                        }

                        builder.seed = seed;
                        builder.lightFadeDistance = variant.Fade > 0f ? variant.Fade : fadeBefore;
                        builder.ceilingShadows = variant.Shadows && shadowsBefore;
                        builder.Rebuild();

                        if (ambient != null) UnityEngine.Object.DestroyImmediate(ambient);
                        ambient = variant.Ambient > 0f ? CreateAmbient(variant.Ambient) : null;

                        var lightsOut = UnityEngine.Object.FindFirstObjectByType<LightsOut>();
                        CeilingLight[] lamps = builder.GetComponentsInChildren<CeilingLight>();
                        if (variant.Blackout)
                        {
                            foreach (CeilingLight lamp in lamps) lamp.SetPower(PowerPhase.Dark);
                            if (lightsOut != null) lightsOut.SetDarkness(1f);
                        }

                        List<Spot> spots = FindSpots(builder);
                        foreach (Spot spot in spots)
                        {
                            player.position = spot.Position + Vector3.up * 0.05f;
                            Vector3 flat = new Vector3(spot.Look.x, 0f, spot.Look.z);
                            player.rotation = Quaternion.LookRotation(flat.sqrMagnitude > 0.001f ? flat : Vector3.forward, Vector3.up);

                            string name = $"s{seed}_{spot.Name}_{variant.Name}";

                            foreach (var pass in passes) pass.enabled = false;
                            Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, name + "_limpa.png"));
                            Measure(readback, out float black, out float mean);
                            foreach (var pass in passes) pass.enabled = true;

                            if (Array.IndexOf(withCamcorder, variant.Name) >= 0)
                                Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, name + ".png"));

                            string line = string.Format(CultureInfo.InvariantCulture,
                                "seed={0} ponto={1} variante={2} preto={3:F1}% media={4:F3}", seed, spot.Name, variant.Name, black * 100f, mean);
                            Debug.Log("[BackRooms] luz0 " + line);
                            report.AppendLine(line);

                            totals.TryGetValue(variant.Name, out var sum);
                            totals[variant.Name] = (sum.black + black, sum.mean + mean, sum.count + 1);
                        }

                        if (variant.Blackout && lightsOut != null) lightsOut.SetDarkness(0f);
                    }
                }

                report.AppendLine();
                foreach (Variant variant in variants)
                {
                    if (!totals.TryGetValue(variant.Name, out var sum) || sum.count == 0) continue;
                    string line = string.Format(CultureInfo.InvariantCulture, "Resumo {0}: preto {1:F1}% e média {2:F3} em {3} fotos",
                                                variant.Name, sum.black / sum.count * 100.0, sum.mean / sum.count, sum.count);
                    Debug.Log("[BackRooms] luz0 " + line);
                    report.AppendLine(line);
                }

                File.WriteAllText(Path.Combine(folder, "medidas.txt"), report.ToString());
                Debug.Log("[BackRooms] luz0: fotos e medidas em " + folder);
                return true;
            }
            finally
            {
                if (ambient != null) UnityEngine.Object.DestroyImmediate(ambient);
                QualitySettings.SetQualityLevel(qualityBefore, true);
                builder.lightFadeDistance = fadeBefore;
                builder.ceilingShadows = shadowsBefore;
                camera.targetTexture = null;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(readback);
                ShaderUtil.allowAsyncCompilation = asyncBefore;
                // Reabre a cena do disco para descartar o mapa e o suporte da câmera.
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        // Volume por cima do da cena com o céu de luz ambiente ligado (só para a variante).
        static GameObject CreateAmbient(float multiplier)
        {
            var go = new GameObject("LuzAmbienteDaPrevia") { hideFlags = HideFlags.DontSave };
            var profile = ScriptableObject.CreateInstance<R.VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            var sky = profile.Add<HD.GradientSky>(true);
            // Escala as cores: na primeira rodada o multiplicador do céu não mudou a imagem.
            sky.top.Override(Scaled(SkyTop, multiplier));
            sky.middle.Override(Scaled(SkyMiddle, multiplier));
            sky.bottom.Override(Scaled(SkyBottom, multiplier));
            sky.gradientDiffusion.Override(1f);
            sky.exposure.Override(0f);
            sky.multiplier.Override(1f);

            var volume = go.AddComponent<R.Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
            return go;
        }

        static Color Scaled(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        static void Measure(Texture2D image, out float black, out float mean)
        {
            Color32[] pixels = image.GetPixels32();
            int dark = 0;
            double sum = 0;
            foreach (Color32 p in pixels)
            {
                float lum = (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) / 255f;
                if (lum < BlackLevel) dark++;
                sum += lum;
            }

            black = pixels.Length > 0 ? dark / (float)pixels.Length : 0f;
            mean = pixels.Length > 0 ? (float)(sum / pixels.Length) : 0f;
        }

        // ---------------------------------------------------------------- Onde fotografar

        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

        static bool Lit(LevelData d, int x, int z) => d.Lights[x, z] == LightState.On || d.Lights[x, z] == LightState.Flicker;

        static bool Closed(LevelData d, int x, int z, WallSide side) => !d.Neighbor(x, z, side, out _, out _) || d.HasWall(x, z, side);

        static bool Usable(LevelData d, int x, int z) => !d.InRoom(x, z) && !d.Pit[x, z] && !d.IsBarricade(x, z);

        static Vector3 Dir(WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return Vector3.back;
                case WallSide.North: return Vector3.forward;
                case WallSide.West: return Vector3.left;
                default: return Vector3.right;
            }
        }

        static int FromSpawn(LevelData d, int x, int z) => Mathf.Abs(x - d.SpawnX) + Mathf.Abs(z - d.SpawnZ);

        // Um ponto de cada tipo, o mais longe do início (onde o jogador passa mais tempo).
        static List<Spot> FindSpots(LevelBuilder builder)
        {
            LevelData d = builder.Data;
            var spots = new List<Spot>();
            Vector3 Cell(int x, int z) => builder.CellCenter(x, z);

            // Beco sem luminária ao lado de uma célula acesa: a luz deveria entrar nele.
            int best = -1; Spot pick = default;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (!Usable(d, x, z) || Lit(d, x, z)) continue;
                    int closed = 0; WallSide open = WallSide.South;
                    foreach (WallSide s in Sides) { if (Closed(d, x, z, s)) closed++; else open = s; }
                    if (closed != 3 || !d.Neighbor(x, z, open, out int nx, out int nz) || !Lit(d, nx, nz) || !Usable(d, nx, nz)) continue;
                    int score = FromSpawn(d, x, z);
                    if (score <= best) continue;
                    best = score;
                    pick = new Spot { Name = "beco", Position = Cell(nx, nz) + Dir(open) * 1.2f, Look = -Dir(open) };
                }
            if (best >= 0) spots.Add(pick);

            // Canto em L de uma célula acesa e de uma apagada vizinha de uma acesa.
            for (int pass = 0; pass < 2; pass++)
            {
                bool wantLit = pass == 0;
                best = -1;
                for (int z = 0; z < d.Height; z++)
                    for (int x = 0; x < d.Width; x++)
                    {
                        if (!Usable(d, x, z) || Lit(d, x, z) != wantLit) continue;
                        foreach (WallSide a in new[] { WallSide.South, WallSide.North })
                            foreach (WallSide b in new[] { WallSide.West, WallSide.East })
                            {
                                if (!Closed(d, x, z, a) || !Closed(d, x, z, b)) continue;
                                WallSide oa = LevelData.Opposite(a), ob = LevelData.Opposite(b);
                                if (Closed(d, x, z, oa) || Closed(d, x, z, ob)) continue;
                                if (!wantLit)
                                {
                                    bool lamp = (d.Neighbor(x, z, oa, out int ax, out int az) && Lit(d, ax, az)) ||
                                                (d.Neighbor(x, z, ob, out int bx, out int bz) && Lit(d, bx, bz));
                                    if (!lamp) continue;
                                }
                                int score = FromSpawn(d, x, z);
                                if (score <= best) continue;
                                best = score;
                                Vector3 corner = Dir(a) + Dir(b);
                                pick = new Spot { Name = wantLit ? "canto_aceso" : "canto_apagado", Position = Cell(x, z) - corner * 1.3f, Look = corner };
                            }
                    }
                if (best >= 0) spots.Add(pick);
            }

            // Nicho de esconderijo, visto de frente.
            best = -1;
            if (d.AlcoveSouth != null)
                for (int z = 0; z < d.Height; z++)
                    for (int x = 0; x < d.Width; x++)
                    {
                        if (!Usable(d, x, z)) continue;
                        foreach (WallSide s in Sides)
                        {
                            if (!d.Neighbor(x, z, s, out _, out _) || !d.IsAlcove(x, z, s)) continue;
                            int score = FromSpawn(d, x, z);
                            if (score <= best) continue;
                            best = score;
                            pick = new Spot { Name = "nicho", Position = Cell(x, z) - Dir(s) * 1.2f, Look = Dir(s) };
                        }
                    }
            if (best >= 0) spots.Add(pick);

            // A célula mais longe de qualquer luminária acesa, olhando para o lado mais aberto.
            best = -1;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (!Usable(d, x, z) || Lit(d, x, z)) continue;
                    int nearest = int.MaxValue;
                    for (int lz = 0; lz < d.Height; lz++)
                        for (int lx = 0; lx < d.Width; lx++)
                            if (Lit(d, lx, lz)) nearest = Mathf.Min(nearest, Mathf.Abs(lx - x) + Mathf.Abs(lz - z));
                    if (nearest == int.MaxValue || nearest <= best) continue;
                    best = nearest;

                    WallSide longest = WallSide.North; int run = -1;
                    foreach (WallSide s in Sides)
                    {
                        int n = 0, cx = x, cz = z;
                        while (!Closed(d, cx, cz, s) && d.Neighbor(cx, cz, s, out cx, out cz)) n++;
                        if (n > run) { run = n; longest = s; }
                    }
                    pick = new Spot { Name = "longe", Position = Cell(x, z), Look = Dir(longest) };
                }
            if (best >= 0) spots.Add(pick);

            return spots;
        }

        // ---------------------------------------------------------------- Utilidades

        static string DefaultFolder()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews", "luz0"));
        }

        static string ArgumentAfter(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }

        static int[] ParseInts(string text)
        {
            string[] parts = text.Split(',');
            var values = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) values[i] = int.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
            return values;
        }
    }
}
