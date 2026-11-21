using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BackRooms.Audio;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// O Level 3 de teste, O Escritório (Docs/level3-escritorio.md): planta do OfficeGenerator,
    /// geometria e mobília do LevelBuilder (LevelBuilder.Office.cs e LevelBuilder.OfficeProps.cs).
    /// Quase sem cor: cinza, inox, concreto e piso de placas; a única cor forte são os armários
    /// amarelos. Luz fria das luminárias tubulares; a onda de luzes no lugar do apagão.
    ///
    /// Aqui ficam os materiais e as texturas pintadas em código (Art/Level3), o perfil de Volume
    /// (Settings/Level3_Volume.asset) e a cena (Scenes/Level3_Test.unity). Tudo é regravado a cada
    /// montagem: mude aqui e monte de novo.
    ///
    /// Menu BackRooms > Montar Level 3 (teste), Abrir Level 3 (teste) e Conferir Level 3
    /// (Level3Setup.BatchCheck pela linha de comando: confere várias seeds e tira fotos em
    /// Logs/Previews/level3_*.png).
    /// </summary>
    public static class Level3Setup
    {
        public const string ScenePath = "Assets/_Game/Scenes/Level3_Test.unity";
        public const string ProfilePath = "Assets/_Game/Settings/Level3_Volume.asset";
        const string ArtFolder = "Assets/_Game/Art/Level3";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        public const int Width = OfficeGenerator.DefaultWidth;
        public const int Height = OfficeGenerator.DefaultHeight;
        static readonly int[] CheckSeeds = { 1998, 7, 4242, 31337, 90210, 12, 555, 2026 };

        const float FixedEv = 5.7f;

        [MenuItem("BackRooms/Abrir Level 3 (teste)", priority = 0)]
        public static void OpenFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) BuildScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[BackRooms] Level 3 aberto. Aperte Play e depois H para hospedar.");
        }

        [MenuItem("BackRooms/Montar Level 3 (teste)")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        public static void BuildScene()
        {
            LevelBuilder.OfficeLook look = EnsureMaterials();
            VolumeProfile profile = EnsureVolumeProfile();
            // O Zelador, o inimigo do escuro (prefab regravado a cada montagem, como o do Capitão).
            GameObject janitorPrefab = JanitorPreview.CreatePrefab();
            // O Ascensorista, o inimigo cego que chega de elevador: é ele que o EntityDirector põe aqui.
            GameObject attendantPrefab = AttendantPreview.CreatePrefab();

            Level0Setup.BuildScene(false, new Level0Setup.SceneOptions
            {
                scenePath = ScenePath,
                levelName = "Level3",
                size = Width,
                pits = false,
                alcoves = false,
                // O diretor de inimigos põe o Ascensorista no lugar da entidade do Level 0 (que sai
                // desta fase, 20/11/2026). Ele aparece logo que alguém sai da cabine e fica escondido
                // num poço até a hora de chegar (AttendantBrain); o Zelador tem diretor próprio.
                entity = true,
                enemyPrefab = attendantPrefab,
                enemyWakeCells = 1,
                paintedArrows = false,
                // O alarme de incêndio destrava a porta corta-fogo (decisão D7).
                objective = SwitchKind.FireAlarm,
                decorate = level =>
                {
                    var builder = level.GetComponent<LevelBuilder>();
                    builder.officeEnabled = true;
                    builder.width = Width;
                    builder.height = Height;
                    builder.office = look;
                    builder.lightFadeDistance = 30f;

                    foreach (Volume volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
                        if (volume.isGlobal && volume.gameObject.name == "Global Volume")
                            volume.sharedProfile = profile;

                    // A onda de luzes (OfficeLightWave) toma o lugar do apagão do Level 0.
                    foreach (LightsOut lightsOut in Object.FindObjectsByType<LightsOut>(FindObjectsInactive.Include))
                        lightsOut.eventEnabled = false;

                    // Sem o zumbido 2D das outras fases: aqui os reatores soam em cada luminária
                    // (OfficeAmbience, montado com a planta) e calam onde a onda apaga.
                    foreach (FluorescentHum hum in Object.FindObjectsByType<FluorescentHum>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        Object.DestroyImmediate(hum);

                    // O Zelador tem diretor próprio na sessão (só aparece no escuro e faz o corte de
                    // energia do alarme); o EntityDirector fica com o inimigo do elevador.
                    var session = Object.FindFirstObjectByType<BackRooms.Net.GameSession>();
                    if (session != null)
                    {
                        var janitor = session.gameObject.AddComponent<BackRooms.Entities.JanitorDirector>();
                        janitor.session = session;
                        janitor.janitorPrefab = janitorPrefab;
                    }
                },
            });
            Debug.Log($"[BackRooms] Level 3 de teste montado em {ScenePath}: abra a cena, aperte Play e hospede (H).");
        }

        // ---------------------------------------------------------------- Atmosfera

        static VolumeProfile EnsureVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            else
            {
                foreach (VolumeComponent old in profile.components.ToArray())
                {
                    profile.components.Remove(old);
                    Object.DestroyImmediate(old, true);
                }
            }

            // Céu frio e fraco: só a luz que rebate. As salas apagadas ficam quase pretas (lanterna).
            var environment = Add<VisualEnvironment>(profile);
            environment.skyType.Override((int)SkyType.Gradient);
            environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky = Add<GradientSky>(profile);
            sky.top.Override(new Color(1.2f, 1.25f, 1.35f));
            sky.middle.Override(new Color(2.6f, 2.7f, 2.9f));
            // Embaixo é o que acende os tetos (as luminárias só apontam para baixo).
            sky.bottom.Override(new Color(9f, 9.3f, 9.8f));
            sky.gradientDiffusion.Override(1f);
            sky.exposure.Override(0f);
            sky.multiplier.Override(1f);

            var exposure = Add<Exposure>(profile);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(FixedEv);

            // Névoa fria e leve: o fundo dos corredores compridos some num cinza azulado.
            var fog = Add<Fog>(profile);
            fog.enabled.Override(true);
            fog.colorMode.Override(FogColorMode.ConstantColor);
            fog.color.Override(new Color(0.36f, 0.39f, 0.43f));
            fog.meanFreePath.Override(75f);
            fog.baseHeight.Override(50f);
            fog.maximumHeight.Override(100f);
            fog.enableVolumetricFog.Override(true);
            fog.albedo.Override(new Color(0.85f, 0.88f, 0.92f));
            fog.anisotropy.Override(0.2f);
            fog.volumetricFogBudget = 0.5f;

            var bloom = Add<Bloom>(profile);
            bloom.intensity.Override(0.22f);
            bloom.scatter.Override(0.65f);

            var grain = Add<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.35f);
            grain.response.Override(0.8f);

            var aberration = Add<ChromaticAberration>(profile);
            aberration.intensity.Override(0.15f);

            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(0.25f);
            vignette.smoothness.Override(0.4f);

            var distortion = Add<LensDistortion>(profile);
            distortion.intensity.Override(0.08f);

            var tonemapping = Add<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);

            var color = Add<ColorAdjustments>(profile);
            color.contrast.Override(10f);
            color.saturation.Override(-18f);
            color.colorFilter.Override(new Color(0.96f, 0.99f, 1.02f));

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(false);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        // ---------------------------------------------------------------- Materiais

        static LevelBuilder.OfficeLook EnsureMaterials()
        {
            EnsureFolder(ArtFolder);

            Texture2D tile = Paint("FloorTile_Albedo", 512, 512, PaintFloorTile);
            Texture2D wood = Paint("Wood_Albedo", 512, 512, PaintWood);
            Texture2D wall = Paint("Wall_Albedo", 256, 256, (x, y) => Gray(198 + Noise(x, y, 1, 24) * 6, 1f, 1f, 0.98f));
            Texture2D ceiling = Paint("Ceiling_Albedo", 256, 256, PaintCeiling);
            Texture2D slab = Paint("Slab_Albedo", 256, 256, (x, y) => Gray(86 + Noise(x, y, 3, 18) * 10 + Noise(x, y, 4, 3) * 4, 1f, 1.01f, 1.03f));
            Texture2D concrete = Paint("Concrete_Albedo", 256, 256, (x, y) => Gray(124 + Noise(x, y, 5, 30) * 14 + Noise(x, y, 6, 4) * 6, 1f, 1f, 0.97f));
            Texture2D osb = Paint("OSB_Albedo", 256, 256, PaintOsb);
            Texture2D blinds = Paint("Window_Blinds", 256, 256, PaintBlinds);
            Texture2D galvanized = Paint("Galvanized_Albedo", 256, 256, (x, y) => Gray(150 + Noise(x, y, 7, 22) * 26 + Noise(x, y, 8, 6) * 8, 1f, 1.02f, 1.04f));
            Texture2D steel = Paint("Steel_Albedo", 256, 256, (x, y) => Gray(176 + Noise(x / 16, y, 9, 2) * 12, 1f, 1.01f, 1.02f));
            Texture2D granite = Paint("Granite_Albedo", 256, 256, PaintGranite);
            Texture2D signs = PaintSigns();

            var look = new LevelBuilder.OfficeLook
            {
                wall = Lit("Wall", Color.white, wall, 0.25f),
                floorTile = Lit("FloorTile", Color.white, tile, 0.55f),
                floorWood = Lit("FloorWood", Color.white, wood, 0.4f),
                slab = Lit("Slab", Color.white, slab, 0.1f),
                ceilingTile = Lit("CeilingTile", Color.white, ceiling, 0.05f),
                slats = Lit("Slats", Rgb(72, 73, 76), null, 0.3f),
                baseboard = Lit("Baseboard", Rgb(58, 58, 60), null, 0.4f),
                // Sem sonda de reflexo, metal puro só reflete o escuro: inox meio metálico.
                steel = Lit("Steel", Color.white, steel, 0.6f, metallic: 0.4f),
                galvanized = Lit("Galvanized", Color.white, galvanized, 0.45f, metallic: 0.35f),
                pipeRed = Lit("PipeRed", Rgb(146, 26, 22), null, 0.45f),
                frameWhite = Lit("FrameWhite", Rgb(230, 230, 226), null, 0.5f),
                glass = Glass(),
                windowDay = Lit("WindowDay", Color.white, blinds, 0.6f, new Color(0.86f, 0.91f, 1f) * 320f, blinds),
                lockerYellow = Lit("LockerYellow", Rgb(226, 168, 18), null, 0.45f),
                lockerBody = Lit("LockerBody", Rgb(176, 176, 172), null, 0.35f),
                darkMetal = Lit("DarkMetal", Rgb(46, 47, 50), null, 0.45f, metallic: 0.6f),
                blackPlastic = Lit("BlackPlastic", Rgb(22, 22, 24), null, 0.35f),
                granite = Lit("Granite", Color.white, granite, 0.55f),
                osb = Lit("OSB", Color.white, osb, 0.2f),
                fabric = Lit("Fabric", Rgb(112, 112, 110), null, 0.1f),
                plant = Lit("Plant", Rgb(52, 72, 46), null, 0.3f),
                concrete = Lit("Concrete", Color.white, concrete, 0.15f),
                tubeOn = Lit("Tube_On", Color.white, null, 0.5f, new Color(0.92f, 0.96f, 1f) * 2600f),
                tubeOff = Lit("Tube_Off", Rgb(206, 206, 202), null, 0.5f),
                panelOn = Lit("Panel_On", Color.white, null, 0.5f, new Color(0.97f, 0.98f, 1f) * 1600f),
                panelOff = Lit("Panel_Off", Rgb(214, 214, 210), null, 0.5f),
                domeOn = Lit("Dome_On", Color.white, null, 0.5f, new Color(1f, 0.95f, 0.86f) * 1300f),
                domeOff = Lit("Dome_Off", Rgb(198, 196, 188), null, 0.5f),
                signGreen = Lit("ExitSign_Green", Color.white, signs, 0.4f, Color.white * 70f, signs),
                ledRed = Lit("Led_Red", Rgb(80, 6, 6), null, 0.6f, new Color(1f, 0.05f, 0.03f) * 160f),
                ledGreen = Lit("Led_Green", Rgb(6, 60, 12), null, 0.6f, new Color(0.1f, 1f, 0.25f) * 90f),
                screenOff = Lit("ScreenOff", Rgb(12, 13, 15), null, 0.9f),
                whiteboard = Lit("Whiteboard", Rgb(238, 238, 236), null, 0.85f),
                buttonAmber = Lit("ButtonAmber", Rgb(120, 70, 8), null, 0.6f, new Color(1f, 0.6f, 0.1f) * 80f),
                skylight = Unlit("Skylight", new Color(0.93f, 0.96f, 1f)),
                voidBlack = Unlit("VoidBlack", Color.black),
                fridgeGlass = Lit("FridgeGlass", Rgb(180, 200, 210), null, 0.9f, new Color(0.85f, 0.93f, 1f) * 140f),
                exitDoor = Lit("ExitDoor", Rgb(92, 94, 96), null, 0.4f, metallic: 0.3f),
            };

            AssetDatabase.SaveAssets();
            return look;
        }

        static Material Lit(string name, Color baseColor, Texture2D albedo, float smoothness, Color emissive = default, Texture2D emissiveMap = null,
                            float metallic = 0f)
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
            else
            {
                var fresh = new Material(material.shader);
                material.CopyPropertiesFromMaterial(fresh);
                material.shaderKeywords = fresh.shaderKeywords;
                material.renderQueue = fresh.renderQueue;
                Object.DestroyImmediate(fresh);
            }

            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetTexture("_BaseColorMap", albedo);
            material.SetColor("_EmissiveColor", emissive);
            material.SetTexture("_EmissiveColorMap", emissiveMap);
            material.enableInstancing = true;

            HDMaterial.ValidateMaterial(material);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        // Vidro fosco cinza-esverdeado das divisórias: transparente, sem refração.
        static Material Glass()
        {
            Material glass = Lit("Glass_Frosted", new Color(0.78f, 0.84f, 0.82f, 0.62f), null, 0.8f);
            if (glass == null) return null;
            glass.SetFloat("_SurfaceType", 1f);
            glass.SetFloat("_BlendMode", 0f);
            glass.SetFloat("_ZWrite", 0f);
            glass.SetFloat("_TransparentZWrite", 0f);
            glass.SetFloat("_EnableFogOnTransparent", 1f);
            glass.SetFloat("_DoubleSidedEnable", 1f);
            glass.SetFloat("_RefractionModel", 0f);
            glass.SetFloat("_ReceivesSSRTransparent", 0f);
            HDMaterial.ValidateMaterial(glass);
            EditorUtility.SetDirty(glass);
            return glass;
        }

        static Material Unlit(string name, Color color)
        {
            string path = $"{ArtFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created)
            {
                Shader shader = Shader.Find("HDRP/Unlit");
                if (shader == null) { Debug.LogError("[BackRooms] Shader HDRP/Unlit não encontrado."); return null; }
                material = new Material(shader) { name = name };
            }
            material.SetColor("_UnlitColor", color);
            HDMaterial.ValidateMaterial(material);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- Texturas pintadas

        static Texture2D Paint(string name, int width, int height, Func<int, int, Color32> paint)
        {
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = paint(x, y);
            return Save(name, width, height, pixels);
        }

        static Texture2D Save(string name, int width, int height, Color32[] pixels)
        {
            string path = $"{ArtFolder}/{name}.png";
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png))
            {
                File.WriteAllBytes(path, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }

            if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                (importer.wrapMode != TextureWrapMode.Repeat || importer.anisoLevel != 4 || !importer.sRGBTexture))
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 4;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Ruído de valor suave em [-1, 1], com período que fecha na textura (a textura repete).
        static float Noise(int x, int y, int salt, int cell)
        {
            cell = Mathf.Max(1, cell);
            float fx = x / (float)cell, fy = y / (float)cell;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Hash(x0, y0, salt), b = Hash(x0 + 1, y0, salt), c = Hash(x0, y0 + 1, salt), d = Hash(x0 + 1, y0 + 1, salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty) * 2f - 1f;
        }

        static float Hash(int x, int y, int salt)
        {
            unchecked
            {
                // A grade do ruído repete a cada 64 células: com textura de 256 a 512 px, emenda.
                x &= 63;
                y &= 63;
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static Color32 Gray(float v, float r, float g, float b)
        {
            return new Color32((byte)Mathf.Clamp(v * r, 0, 255), (byte)Mathf.Clamp(v * g, 0, 255), (byte)Mathf.Clamp(v * b, 0, 255), 255);
        }

        static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        // Porcelanato cinza em placas de 0,9 m (a textura cobre 1,8 m: 2 x 2 placas de 256 px).
        static Color32 PaintFloorTile(int x, int y)
        {
            int tx = x / 256, ty = y / 256;
            int lx = x % 256, ly = y % 256;
            bool joint = lx < 2 || ly < 2;
            float shade = (Hash(tx, ty, 11) - 0.5f) * 10f;
            float v = 142 + shade + Noise(x, y, 12, 40) * 7 + Noise(x, y, 13, 6) * 3;
            if (joint) v = 108;
            return Gray(v, 1f, 1f, 0.98f);
        }

        // Tábuas de madeira de 0,2 m ao longo do eixo V (a textura cobre 2,4 m: 12 tábuas).
        static Color32 PaintWood(int x, int y)
        {
            int plank = x * 12 / 512;
            int start = plank * 512 / 12;
            int local = x - start;
            int offset = (int)(Hash(plank, 0, 21) * 512);
            int along = (y + offset) % 512;
            bool joint = local < 2 || along < 2 || (along > 255 && along < 258 && Hash(plank, 1, 22) > 0.5f);

            float tone = Hash(plank, 2, 23);
            float grain = Mathf.Sin((x * 0.9f + Noise(x, y, 24, 20) * 9f) * 0.8f) * 0.5f + 0.5f;
            float r = 128 + tone * 30 + grain * 10 + Noise(x, y, 25, 30) * 6;
            float g = r * 0.76f, b = r * 0.56f;
            if (joint) { r *= 0.55f; g *= 0.55f; b *= 0.55f; }
            return new Color32((byte)Mathf.Clamp(r, 0, 255), (byte)Mathf.Clamp(g, 0, 255), (byte)Mathf.Clamp(b, 0, 255), 255);
        }

        // Forro mineral branco: placas de 0,625 m com perfil, salpicado de furinhos.
        static Color32 PaintCeiling(int x, int y)
        {
            int lx = x % 128, ly = y % 128;
            if (lx < 2 || ly < 2) return Gray(206, 1f, 1f, 1f);
            float v = 232 + Noise(x, y, 31, 16) * 4;
            if (Hash(x, y, 32) > 0.93f) v -= 22;
            return Gray(v, 1f, 1f, 1f);
        }

        // OSB dessaturado: lascas claras e escuras orientadas ao acaso.
        static Color32 PaintOsb(int x, int y)
        {
            float flake = Noise(x * 2, y, 41, 9) + Noise(x, y * 2, 42, 7) * 0.8f;
            float v = 158 + flake * 22 + Noise(x, y, 43, 3) * 5;
            return new Color32((byte)Mathf.Clamp(v, 0, 255), (byte)Mathf.Clamp(v * 0.92f, 0, 255), (byte)Mathf.Clamp(v * 0.8f, 0, 255), 255);
        }

        // Janela com persiana: em cima as lâminas abaixadas, embaixo o claro de fora.
        static Color32 PaintBlinds(int x, int y)
        {
            if (y > 115)
            {
                bool gap = (y % 9) < 2;
                float v = gap ? 252 : 214 + Noise(x, y, 51, 30) * 4;
                return Gray(v, 1f, 1.01f, 1.03f);
            }
            float sky = 236 - y * 0.12f + Noise(x, y, 52, 60) * 3;
            return Gray(sky, 0.96f, 0.99f, 1.04f);
        }

        static Color32 PaintGranite(int x, int y)
        {
            float speck = Hash(x, y, 61);
            float v = 128 + Noise(x, y, 62, 6) * 10;
            if (speck > 0.86f) v = 70 + speck * 30;
            else if (speck < 0.12f) v = 190 + speck * 200;
            return Gray(v, 1f, 1f, 1f);
        }

        // Placa de saída de emergência: fundo verde, o boneco correndo para a porta e a seta.
        // Quatro quadros de 256 x 96 lado a lado: seta para frente, para a esquerda, para a direita, sem seta.
        static Texture2D PaintSigns()
        {
            const int cw = 256, ch = 96;
            var pixels = new Color32[cw * 4 * ch];
            var green = new Color32(16, 140, 64, 255);
            var white = new Color32(245, 250, 245, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = green;

            void Fill(int frame, Func<float, float, bool> inside)
            {
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                        if (inside(x, y)) pixels[y * cw * 4 + frame * cw + x] = white;
            }

            bool Segment(float px, float py, float ax, float ay, float bx, float by, float r)
            {
                float vx = bx - ax, vy = by - ay;
                float t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
                float dx = px - (ax + vx * t), dy = py - (ay + vy * t);
                return dx * dx + dy * dy <= r * r;
            }

            // Boneco correndo para a esquerda, entrando numa porta (contorno de retângulo).
            bool Runner(float x, float y)
            {
                bool door = (x >= 14 && x <= 46 && y >= 14 && y <= 82) && !(x >= 20 && x <= 40 && y >= 14 && y <= 76);
                bool head = (x - 76) * (x - 76) + (y - 70) * (y - 70) <= 64;
                bool body = Segment(x, y, 74, 60, 84, 38, 5);
                bool arm1 = Segment(x, y, 77, 54, 64, 50, 4) || Segment(x, y, 64, 50, 58, 58, 4);
                bool arm2 = Segment(x, y, 77, 54, 92, 52, 4) || Segment(x, y, 92, 52, 98, 44, 4);
                bool leg1 = Segment(x, y, 84, 38, 70, 30, 5) || Segment(x, y, 70, 30, 64, 16, 5);
                bool leg2 = Segment(x, y, 84, 38, 98, 28, 5) || Segment(x, y, 98, 28, 106, 18, 5);
                return door || head || body || arm1 || arm2 || leg1 || leg2;
            }

            bool ArrowRight(float x, float y) => Segment(x, y, 140, 48, 214, 48, 7) || Segment(x, y, 214, 48, 192, 70, 7) || Segment(x, y, 214, 48, 192, 26, 7);
            bool ArrowLeft(float x, float y) => Segment(x, y, 140, 48, 214, 48, 7) || Segment(x, y, 140, 48, 162, 70, 7) || Segment(x, y, 140, 48, 162, 26, 7);
            bool ArrowUp(float x, float y) => Segment(x, y, 178, 14, 178, 82, 7) || Segment(x, y, 178, 82, 156, 60, 7) || Segment(x, y, 178, 82, 200, 60, 7);

            Fill(0, (x, y) => Runner(x, y) || ArrowUp(x, y));
            Fill(1, (x, y) => Runner(x, y) || ArrowLeft(x, y));
            Fill(2, (x, y) => Runner(x, y) || ArrowRight(x, y));
            Fill(3, (x, y) => Runner(x + 60, y));

            // Borda branca fina em cada quadro.
            for (int f = 0; f < 4; f++)
                Fill(f, (x, y) => x < 4 || x > cw - 5 || y < 4 || y > ch - 5);

            Texture2D texture = Save("ExitSign_Atlas", cw * 4, ch, pixels);
            if (AssetImporter.GetAtPath($"{ArtFolder}/ExitSign_Atlas.png") is TextureImporter importer && importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtFolder}/ExitSign_Atlas.png");
            }
            return texture;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---------------------------------------------------------------- Conferência

        [MenuItem("BackRooms/Conferir Level 3")]
        public static void CheckFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            bool ok = Check(out string report);
            EditorUtility.DisplayDialog("Level 3", (ok ? "PASSOU\n\n" : "FALHOU\n\n") + report, "OK");
        }

        /// <summary>Linha de comando: monta, confere e fotografa. Sai com 0 se passou, 3 se falhou.</summary>
        public static void BatchCheck()
        {
            try
            {
                bool ok = Check(out string report);
                Debug.Log("[BackRooms] Conferência do Level 3 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
                Level1Setup.ExitWithLevel0Open(ok ? 0 : 3);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Level1Setup.ExitWithLevel0Open(1);
            }
        }

        /// <summary>
        /// Linha de comando: monta a cena e só tira as fotos (seed 1998, ou -seed N), sem conferir as
        /// outras seeds. Para ajustar a aparência mais depressa. Sai com 0.
        /// </summary>
        public static void BatchPhotos()
        {
            try
            {
                BuildScene();
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var builder = Object.FindAnyObjectByType<LevelBuilder>();
                string[] args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-seed");
                builder.seed = index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out int s) ? s : CheckSeeds[0];
                builder.Rebuild();
                Debug.Log("[BackRooms] Fotos do Level 3: " + Capture(builder));
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Level1Setup.ExitWithLevel0Open(0);
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

            var builder = Object.FindAnyObjectByType<LevelBuilder>();
            if (builder == null || !builder.officeEnabled)
            {
                report = "a cena não tem LevelBuilder do Escritório";
                return false;
            }

            var problems = new List<string>();
            var lines = new List<string>();

            try
            {
                foreach (int seed in CheckSeeds)
                {
                    builder.seed = seed;
                    builder.Rebuild();
                    LevelData d = builder.Data;
                    int hash = OfficeGenerator.Hash(d);

                    foreach (string p in OfficeGenerator.Validate(d)) problems.Add($"seed {seed}: {p}");

                    builder.Rebuild();
                    if (OfficeGenerator.Hash(builder.Data) != hash) problems.Add($"seed {seed}: a mesma seed deu outra planta");
                    d = builder.Data;

                    if (builder.OfficeExitRoute.Count != 4) problems.Add($"seed {seed}: caminho da escada com {builder.OfficeExitRoute.Count} pontos");
                    if (Object.FindObjectsByType<LevelExit>().Length != 1) problems.Add($"seed {seed}: não há uma porta de saída só");
                    if (Object.FindObjectsByType<ElevatorDoors>().Length != 3) problems.Add($"seed {seed}: o saguão não tem 3 elevadores");
                    if (Object.FindObjectsByType<ElevatorButton>().Length != 9) problems.Add($"seed {seed}: os elevadores não têm 9 botões (abrir, fechar e chamar em cada um)");
                    if (Object.FindFirstObjectByType<FluorescentHum>() != null) problems.Add($"seed {seed}: a cena tem o zumbido 2D das outras fases");
                    var ambience = Object.FindFirstObjectByType<OfficeAmbience>();
                    if (ambience == null || ambience.LightCount == 0) problems.Add($"seed {seed}: sem som ambiente (ou sem luminárias nele)");
                    var bangs = Object.FindFirstObjectByType<LockerBangs>();
                    if (bangs == null || bangs.RowCount == 0) problems.Add($"seed {seed}: nenhum corredor com armários que batem");
                    else Debug.Log($"[BackRooms] Level 3 seed {seed}: {bangs.DoorCount} portas de armário soltas em {bangs.RowCount} corredores, {ambience?.LightCount} luminárias e {ambience?.VentCount} difusores no som ambiente");
                    if (!d.InRoom(d.SpawnX, d.SpawnZ) || d.Office[d.SpawnX, d.SpawnZ] != OfficeCell.Cab) problems.Add($"seed {seed}: o início não é a cabine");

                    List<string> blocked = BlockedPath(builder);
                    if (blocked.Count > 0) problems.Add($"seed {seed}: caminho até a escada bloqueado ({string.Join(", ", blocked.Take(4))})");
                    List<string> floating = RouteOffGround(builder);
                    if (floating.Count > 0) problems.Add($"seed {seed}: pontos da escada sem chão ({string.Join(", ", floating)})");

                    var kinds = d.OfficeRooms.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count());
                    int lit = 0, lockers = 0;
                    foreach (LightState s in d.Lights) if (s == LightState.On || s == LightState.Flicker) lit++;
                    foreach (byte b in d.LockersSouth) if (b != 0) lockers++;
                    foreach (byte b in d.LockersWest) if (b != 0) lockers++;
                    var wave = builder.GetComponentInChildren<OfficeLightWave>();

                    lines.Add($"seed {seed}: hash {hash:X8}, caminho {d.PathCells.Count} células, salas de aula {Count(kinds, OfficeCell.Classroom)}, " +
                              $"pequenas {Count(kinds, OfficeCell.Small)}, cafeteria {Count(kinds, OfficeCell.Cafe)}, estar {Count(kinds, OfficeCell.Lounge)}, " +
                              $"{lit} luminárias acesas, {lockers} paredes com armário, {d.Decorations.Count} placas de saída, " +
                              $"{(wave != null ? wave.RunCount : 0)} trechos para a onda de luzes");
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

        static int Count(Dictionary<OfficeCell, int> kinds, OfficeCell kind) => kinds.TryGetValue(kind, out int n) ? n : 0;

        // Cápsula do tamanho do jogador entre os centros das células do caminho (com as portas da
        // cabine abertas): nada pode barrar.
        static List<string> BlockedPath(LevelBuilder builder)
        {
            foreach (ElevatorDoors doors in Object.FindObjectsByType<ElevatorDoors>())
                if (doors.IsCab) doors.Snap(1f);
            Physics.SyncTransforms();

            LevelData d = builder.Data;
            var blocked = new List<string>();
            var points = d.PathCells.Select(c => builder.CellCenter(c % d.Width, c / d.Width)).ToList();
            points.AddRange(builder.OfficeExitRoute);

            int stairs = d.PathCells.Count;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                // Na escada a cápsula reta desce na diagonal e raspa a quina dos degraus, coisa que o
                // jogador (que desce degrau a degrau) não faz: ali ela vai um pouco mais alta.
                float lift = i >= stairs ? 0.5f : 0.35f;
                Vector3 low = Vector3.up * lift, high = Vector3.up * 1.5f;
                Vector3 dir = b - a;
                if (dir.magnitude < 0.01f) continue;
                foreach (RaycastHit hit in Physics.CapsuleCastAll(a + low, a + high, 0.28f, dir.normalized, dir.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.distance <= 0f) continue;
                    blocked.Add($"{i}->{i + 1} por {hit.collider.name} em {hit.point:F2} (caixa {hit.collider.bounds.center:F2} x {hit.collider.bounds.size:F2}; de {a:F2} a {b:F2})");
                    break;
                }
            }

            foreach (ElevatorDoors doors in Object.FindObjectsByType<ElevatorDoors>()) doors.Snap(0f);
            return blocked;
        }

        static List<string> RouteOffGround(LevelBuilder builder)
        {
            var off = new List<string>();
            for (int i = 0; i < builder.OfficeExitRoute.Count; i++)
            {
                Vector3 p = builder.OfficeExitRoute[i];
                if (!Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                    Mathf.Abs(hit.point.y - p.y) > 0.25f)
                    off.Add($"ponto {i} em {p:F1}");
            }
            return off;
        }

        // ---------------------------------------------------------------- Fotos

        static string Capture(LevelBuilder builder)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) return "sem o prefab do jogador";

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews"));
            Directory.CreateDirectory(folder);
            foreach (string old in Directory.GetFiles(folder, "level3_*.png")) File.Delete(old);

            GameObject rig = Level0Preview.CreateCameraRig(prefab, out Camera camera);
            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var taken = new List<string>();

            foreach (ElevatorDoors doors in Object.FindObjectsByType<ElevatorDoors>())
                if (doors.IsCab) doors.Snap(1f);
            // Fora do Play a sombra em cache não é desenhada: sem isto toda luz parece tapada.
            foreach (HDAdditionalLightData light in builder.GetComponentsInChildren<HDAdditionalLightData>(true))
                light.shadowUpdateMode = ShadowUpdateMode.EveryFrame;

            try
            {
                camera.targetTexture = target;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 150f);
                LevelData d = builder.Data;

                void Shot(string name, Vector3 eye, Vector3 look, float pitch = 4f)
                {
                    Vector3 flat = look - eye;
                    flat.y = 0f;
                    if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
                    rig.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(flat.normalized, Vector3.up));
                    camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, $"level3_{name}.png"));
                    taken.Add(name);
                }

                Vector3 Center(int x, int z) => builder.CellCenter(x, z);
                OfficeElevator cab = d.Elevators.Find(e => e.IsCab);
                Vector3 toLobby = Side(cab.Side);

                Shot("inicio_cabine", Center(cab.X, cab.Z) - toLobby * 0.9f, Center(cab.X, cab.Z) + toLobby * 4f);
                Shot("saguao", Center(cab.X, cab.Z) + toLobby * 9.5f, Center(cab.X, cab.Z), 2f);

                // O trecho reto de corredor mais comprido, visto de uma ponta.
                (int x, int z, Vector3 dir, int length) best = (0, 0, Vector3.right, 0);
                for (int z = 0; z < d.Height; z++)
                    for (int x = 0; x < d.Width; x++)
                        foreach (WallSide s in new[] { WallSide.East, WallSide.North })
                        {
                            int n = 0, cx = x, cz = z;
                            while (d.Office[cx, cz] == OfficeCell.Corridor && d.OfficeEdgeAt(cx, cz, s) == OfficeEdge.Open && d.Neighbor(cx, cz, s, out int nx, out int nz) && d.Office[nx, nz] == OfficeCell.Corridor)
                            {
                                n++;
                                cx = nx;
                                cz = nz;
                            }
                            if (n > best.length) best = (x, z, Side(s), n);
                        }
                Shot("corredor", Center(best.x, best.z) - best.dir * 1.6f, Center(best.x, best.z) + best.dir * 20f);
                Shot("corredor_volta", Center(best.x, best.z) + best.dir * (best.length * 4f + 1.6f), Center(best.x, best.z));

                foreach (OfficeCell kind in new[] { OfficeCell.Classroom, OfficeCell.Small, OfficeCell.Cafe, OfficeCell.Lounge })
                {
                    int shots = 0;
                    foreach (OfficeRoom room in d.OfficeRooms)
                    {
                        if (room.Kind != kind || shots >= (kind == OfficeCell.Classroom ? 2 : 1)) continue;
                        if ((kind == OfficeCell.Classroom || kind == OfficeCell.Small) && !room.Lit) continue;
                        if (!Entrance(d, room, out int ex, out int ez, out WallSide into)) continue;
                        Vector3 door = Center(ex, ez) + Side(into) * 2f;
                        Vector3 middle = builder.CellCenter(room.X0, room.Z0) + new Vector3((room.Width - 1) * 2f, 0f, (room.Height - 1) * 2f);
                        Shot($"{kind.ToString().ToLowerInvariant()}_{shots}", door - Side(into) * 0.8f, middle, 6f);
                        shots++;
                    }
                }

                OfficeAtrium at = d.Atrium;
                Vector3 head = Center(at.HeadX, at.HeadZ);
                Shot("vao_escada", head - Side(at.Inward) * 1.2f, head + Side(at.Inward) * 6f, 18f);
                Shot("vao_para_cima", head - Side(at.Inward) * 0.5f + Side(at.EndSide) * -3f, head + Side(at.Inward) * 6f + Side(at.EndSide) * -3f, -40f);
                if (builder.OfficeExitRoute.Count == 4)
                {
                    Vector3 landing = builder.OfficeExitRoute[3];
                    Shot("saida", landing - Side(at.EndSide) * 1.2f, landing + Side(at.EndSide) * 3f, 0f);
                }

                // Uma placa de saída pendurada, vista de quem chega.
                if (d.Decorations.Count > 0)
                {
                    Decoration sign = d.Decorations[Mathf.Min(1, d.Decorations.Count - 1)];
                    int index = d.PathCells.IndexOf(sign.X + sign.Z * d.Width);
                    if (index > 1)
                    {
                        int back = d.PathCells[index - 2];
                        Shot("placa", Center(back % d.Width, back / d.Width), Center(sign.X, sign.Z) + Vector3.up * 0.8f, -6f);
                    }
                }
            }
            finally
            {
                foreach (ElevatorDoors doors in Object.FindObjectsByType<ElevatorDoors>()) doors.Snap(0f);
                camera.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(rig);
                ShaderUtil.allowAsyncCompilation = asyncBefore;
            }

            return $"{taken.Count} em {folder} ({string.Join(", ", taken)})";
        }

        // Uma célula de corredor encostada na sala por uma porta, e o lado que entra na sala.
        static bool Entrance(LevelData d, OfficeRoom room, out int x, out int z, out WallSide into)
        {
            for (int cz = room.Z0; cz < room.Z0 + room.Height; cz++)
                for (int cx = room.X0; cx < room.X0 + room.Width; cx++)
                    foreach (WallSide side in new[] { WallSide.South, WallSide.North, WallSide.West, WallSide.East })
                    {
                        if (!d.Neighbor(cx, cz, side, out int nx, out int nz) || d.Office[nx, nz] != OfficeCell.Corridor) continue;
                        if (!OfficeGenerator.Passable(d.OfficeEdgeAt(cx, cz, side))) continue;
                        x = nx;
                        z = nz;
                        into = LevelData.Opposite(side);
                        return true;
                    }
            x = z = 0;
            into = WallSide.North;
            return false;
        }

        public static Vector3 Side(WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return Vector3.back;
                case WallSide.North: return Vector3.forward;
                case WallSide.West: return Vector3.left;
                default: return Vector3.right;
            }
        }
    }
}
