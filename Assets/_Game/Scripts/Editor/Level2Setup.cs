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
using Random = System.Random;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// O Level 2 de teste, As Piscinas (Docs/level2-piscinas.md): salas do PoolGenerator, água,
    /// claraboias e janelões. O visual segue o "visual A" das Poolrooms (Jared Pike): azulejo
    /// marfim ao sol, meia-sombra esverdeada, teto claro, água turquesa transparente por onde se
    /// vê o piso, janelas estouradas e poucos buracos escuros.
    ///
    /// Aqui ficam o perfil de Volume próprio da fase (Settings/Level2_Volume.asset: céu, névoa,
    /// exposição, pós), os materiais e as texturas (Art/Level2) e os números de luz que o
    /// LevelBuilder usa. Tudo é regravado a cada montagem: mude aqui e monte de novo.
    ///
    /// Texturas: azulejo, pastilha e capa vêm da Folha A (Art/Fontes/Level2). A cáustica, a
    /// ondulação da água, o vidro fosco dos janelões e o tijolo de vidro são pintados aqui em
    /// código; se o arquivo-fonte existir em Art/Fontes/Level2 (Caustics.png, Water_Height.png,
    /// Window_Frosted.png, Glass_Block.png), ele é usado no lugar da pintura.
    ///
    /// Menu BackRooms > Montar Level 2 (teste) e Conferir Level 2 (Level2Setup.BatchCheck pela
    /// linha de comando: confere várias seeds e tira fotos em Logs/Previews/level2_look_*.png).
    /// </summary>
    public static class Level2Setup
    {
        public const string ScenePath = "Assets/_Game/Scenes/Level2_Test.unity";
        public const string ProfilePath = "Assets/_Game/Settings/Level2_Volume.asset";
        const string SourceFolder = "Assets/_Game/Art/Fontes/Level2";
        const string ArtFolder = "Assets/_Game/Art/Level2";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        const int Size = 16;
        static readonly int[] CheckSeeds = { 1998, 7, 4242, 31337, 90210 };

        // Exposição fixa (EV100). A luz se ajusta a ela, não o contrário: subir a exposição
        // acinzenta o branco.
        const float FixedEv = 5.5f;

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
            VolumeProfile profile = EnsureVolumeProfile();

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
                decorate = level =>
                {
                    Apply(level.GetComponent<LevelBuilder>(), materials);
                    UseOwnAtmosphere(profile);
                },
            });
            Debug.Log($"[BackRooms] Level 2 de teste montado em {ScenePath}: abra a cena, aperte Play e hospede (H).");
        }

        // O montador do Level 0 põe na cena o Volume global com o perfil dele (céu e névoa
        // amarelos, contraste) e o zumbido de lâmpada fluorescente. Nas Piscinas os dois saem:
        // o Volume passa a usar o perfil daqui e o zumbido é tirado (não há lâmpada aparente).
        static void UseOwnAtmosphere(VolumeProfile profile)
        {
            foreach (Volume volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (volume.isGlobal && volume.gameObject.name == "Global Volume")
                    volume.sharedProfile = profile;

            foreach (FluorescentHum hum in Object.FindObjectsByType<FluorescentHum>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(hum);
        }

        static void Apply(LevelBuilder builder, PoolMaterials m)
        {
            // Azulejo branco em tudo: parede, piso, teto (claro) e a sala inicial.
            builder.wallMaterial = m.Tile;
            builder.floorMaterial = m.Tile;
            builder.ceilingMaterial = m.Tile;
            builder.cleanWallMaterial = m.Tile;
            builder.cleanFloorMaterial = m.Tile;
            builder.baseboardMaterial = m.Tile;
            builder.poolBottomMaterial = m.Mosaic;
            builder.copingMaterial = m.Coping;
            builder.waterMaterial = m.Water;
            // 6 azulejos por repetição da textura: 0,8 m = azulejo de 13 cm (as referências medem 11 a 17).
            builder.poolTileMeters = 0.8f;
            builder.poolMosaicMeters = 0.6f;
            // As referências não têm sujeira: sem azulejo gasto.
            builder.poolWornTileMaterial = null;
            builder.poolAccentMaterial = m.Accent;
            builder.poolWindowMaterial = m.Window;
            builder.poolGlassBlockMaterial = m.GlassBlock;
            builder.poolLampMaterial = m.Lamp;
            builder.poolCausticsMaterial = m.Caustics;
            builder.poolRailMaterial = m.Rail;
            // Manchas (decalques), nesta ordem: limo, calcário, mofo, ferrugem. O construtor escolhe
            // onde: poucas e longe do início.
            builder.poolStainDecals = m.Stains;
            // Marcações pintadas (Folha D), nesta ordem: 1.2 M, NO DIVING, DEEP END, T de raia.
            builder.poolMarkDecals = m.Marks;

            // Claraboias de vidro fosco: acesas de dia; no apagão, vidro azul-noite.
            builder.panelOnMaterial = m.SkylightOn;
            builder.panelOffMaterial = m.SkylightOff;
            builder.lightHousingMaterial = null;
            builder.ventMaterial = null;

            // Luz do dia: sol de fim de tarde levemente quente, entrando inclinado.
            builder.poolSkyLux = 700f;
            builder.poolSkyTemperature = 4800f;
            // Sem cookie de cáustica no sol: a mancha cai também no seco, onde não há água por cima.
            // A cáustica vem do material Pool_Caustics, só no que está submerso.
            builder.poolSkyCookie = null;
            builder.poolSkyBeams = true;
            builder.poolSunTilt = 35f;
            builder.poolSunHeading = 35f;
            builder.poolWindowCandela = 900f;
            builder.poolLampCandela = 260f;
            builder.poolLampColor = new Color(0.55f, 0.95f, 1f);

            // Luminárias do construtor antigo (claraboias em grade); ficam com a mesma cor do sol.
            builder.panelScale = 2f;
            builder.lightTemperature = 4800f;
            builder.lightCandela = 3200f;
            builder.lightRange = 18f;
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
                // A planta: regras do PoolGenerator e a mesma seed dando a mesma planta.
                foreach (int seed in CheckSeeds)
                {
                    LevelData a = LevelGenerator.Generate(seed, Size, Size, false, false, true);
                    LevelData b = LevelGenerator.Generate(seed, Size, Size, false, false, true);
                    uint hash = PoolGenerator.Hash(a);
                    if (hash != PoolGenerator.Hash(b)) problems.Add($"seed {seed}: a mesma seed deu plantas diferentes");
                    foreach (string problem in PoolGenerator.Validate(a)) problems.Add($"seed {seed}: {problem}");
                    lines.Add($"seed {seed}: {Summary(a)} hash={hash:X8}");
                }

                // O visual: a cena aponta para o perfil e os materiais daqui.
                var global = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(v => v.isGlobal && v.gameObject.name == "Global Volume");
                if (global == null || global.sharedProfile == null || AssetDatabase.GetAssetPath(global.sharedProfile) != ProfilePath)
                    problems.Add("o Volume global da cena não usa " + ProfilePath);
                if (Object.FindFirstObjectByType<FluorescentHum>() != null) problems.Add("a cena ainda tem o zumbido de lâmpada fluorescente");
                if (builder.waterMaterial == null || builder.poolCausticsMaterial == null) problems.Add("falta o material da água ou da cáustica");

                if (builder.poolStainDecals == null || builder.poolStainDecals.Length != 4 || builder.poolStainDecals.Any(m => m == null))
                    problems.Add("falta algum material de mancha (Decal_Stain_*)");
                if (builder.poolMarkDecals == null || builder.poolMarkDecals.Length != 4 || builder.poolMarkDecals.Any(m => m == null))
                    problems.Add("falta algum material de marcação (Decal_Mark_*)");

                builder.seed = CheckSeeds[0];
                builder.Rebuild();
                lines.Add($"montagem da seed {builder.seed}: {builder.PoolStats}");
                lines.Add($"manchas e marcações: {builder.PoolDecalLog}");
                lines.Add("fotos: " + Capture(builder));
            }
            finally
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            report = string.Join("\n", lines) + (problems.Count > 0 ? "\nPROBLEMAS:\n" + string.Join("\n", problems) : "");
            return problems.Count == 0;
        }

        static string Summary(LevelData d)
        {
            var counts = new int[4];
            int solid = 0;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.IsSolid(x, z)) { solid++; continue; }
                    counts[(int)d.Depth[x, z]]++;
                }

            int dark = d.Rooms.Count(r => r.Dark);
            return $"{d.Rooms.Count} salas ({dark} escuras), seco {counts[0]}, raso {counts[1]}, fundo {counts[2]}, poço {counts[3]}, maciço {solid}, " +
                   $"{d.Stairs.Count} escadas, {d.Columns.Count} colunas, {d.Lamps.Count} lâmpadas, caminho a pé de {LevelGenerator.FootPath(d).Count} células";
        }

        // ---------------------------------------------------------------- Fotos

        // Cada vista sai duas vezes: com a filmadora (level2_look_*.png, como no jogo) e sem ela
        // (level2_limpa_*.png), para medir as cores contra a paleta, que vale antes da filmadora.
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
            var water = builder.GetComponentInChildren<PoolWater>();
            CustomPassVolume[] camcorderPasses = Object.FindObjectsByType<CustomPassVolume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var camcorder = Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();

            try
            {
                if (camcorder != null) camcorder.Draw(true, 61.4f, 61.4f, 0.95f, false);
                camera.targetTexture = target;
                if (water != null) water.Prepare();

                LevelData d = builder.Data;

                void Shot(string name, Vector3 feet, Vector3 look, float pitch, bool underwater = false)
                {
                    look.y = 0f;
                    if (look.sqrMagnitude < 1e-4f) look = Vector3.forward;
                    rig.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(look.normalized, Vector3.up));
                    camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                    if (water != null)
                    {
                        water.LocalUnderwater = underwater;
                        water.Snap(camera.transform.position);
                    }
                    if (camcorder != null) camcorder.Draw(true, 61.4f, 61.4f, 0.95f, false, underwater: underwater, air: underwater ? 0.4f : 1f);

                    foreach (CustomPassVolume pass in camcorderPasses) pass.enabled = true;
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, $"level2_look_{name}.png"));
                    foreach (CustomPassVolume pass in camcorderPasses) pass.enabled = false;
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, $"level2_limpa_{name}.png"));
                    foreach (CustomPassVolume pass in camcorderPasses) pass.enabled = true;
                    shots++;
                }

                // Da sala inicial para fora, nas quatro direções.
                for (int i = 0; i < 4; i++)
                    Shot($"inicio_{i * 90}", builder.SpawnCenter, Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward, 4f);

                // Uma foto de cada tipo de sala: de uma ponta, olhando ao longo do lado maior.
                var kinds = new HashSet<PoolRoomKind>();
                foreach (PoolRoom room in d.Rooms)
                {
                    if (room.Kind == PoolRoomKind.Start || !kinds.Add(room.Kind)) continue;
                    bool alongX = room.Width >= room.Height;
                    int x0 = alongX ? room.X0 : room.X0 + room.Width / 2;
                    int z0 = alongX ? room.Z0 + room.Height / 2 : room.Z0;
                    int x1 = alongX ? room.X0 + room.Width - 1 : x0;
                    int z1 = alongX ? z0 : room.Z0 + room.Height - 1;
                    Vector3 from = builder.CellCenter(x0, z0), to = builder.CellCenter(x1, z1);
                    // Na água funda, de pé na superfície (como quem olha da borda).
                    from.y = Mathf.Max(from.y, 0f);
                    // Quem está na primeira célula olha para dentro da sala; recua meia célula para pegar mais dela.
                    Vector3 back = (to - from).sqrMagnitude > 0.01f ? -(to - from).normalized * 1.6f : Vector3.zero;
                    back.y = 0f;
                    Shot("sala_" + room.Kind.ToString().ToLowerInvariant(), from + back, (to - from).sqrMagnitude > 0.01f ? to - from : Vector3.forward,
                         room.Kind == PoolRoomKind.WellRoom ? 25f : 6f);
                }

                // Debaixo d'água, na primeira célula funda ou poço, olhando para o lado.
                bool under = false;
                for (int z = 0; z < d.Height && !under; z++)
                    for (int x = 0; x < d.Width && !under; x++)
                    {
                        if (d.IsSolid(x, z) || (d.Depth[x, z] != PoolDepth.Deep && d.Depth[x, z] != PoolDepth.Well)) continue;
                        Vector3 feet = builder.CellCenter(x, z);
                        feet.y = (water != null ? water.SurfaceY : 0f) - 1.2f - 1.65f; // olhos 1,2 m abaixo da superfície
                        Shot("debaixo_dagua", feet, Vector3.forward, 5f, underwater: true);
                        under = true;
                    }

                // Um janelão e uma parede de tijolo de vidro, de frente, do meio da célula.
                foreach (PoolEdge kind in new[] { PoolEdge.Window, PoolEdge.GlassBlock })
                {
                    bool found = false;
                    for (int z = 0; z < d.Height && !found; z++)
                        for (int x = 0; x < d.Width && !found; x++)
                        {
                            if (d.IsSolid(x, z)) continue;
                            foreach (WallSide side in new[] { WallSide.South, WallSide.North, WallSide.West, WallSide.East })
                            {
                                if (d.EdgeAt(x, z, side) != kind) continue;
                                Vector3 dir = side == WallSide.South ? Vector3.back : side == WallSide.North ? Vector3.forward : side == WallSide.West ? Vector3.left : Vector3.right;
                                Vector3 feet = builder.CellCenter(x, z) - dir * 1.2f;
                                feet.y = Mathf.Max(feet.y, 0f);
                                Shot(kind == PoolEdge.Window ? "janelao" : "tijolo_de_vidro", feet, dir, -6f);
                                found = true;
                                break;
                            }
                        }
                }

                // Corredor qualquer do caminho a pé, longe do início.
                List<int> path = LevelGenerator.FootPath(d);
                if (path.Count > 6)
                {
                    int i = path.Count * 2 / 3;
                    Vector3 from = builder.CellCenter(path[i] % d.Width, path[i] / d.Width);
                    Vector3 to = builder.CellCenter(path[i + 1] % d.Width, path[i + 1] / d.Width);
                    Shot("caminho", from, to - from, 3f);
                }

                // Apagão ("anoitecer"), da sala inicial.
                var lightsOut = Object.FindFirstObjectByType<LightsOut>();
                if (lightsOut != null)
                {
                    foreach (CeilingLight lamp in CeilingLight.All) lamp.SetPower(PowerPhase.Dark);
                    lightsOut.SetDarkness(1f);
                    if (water != null) water.ForceNight = 1f;
                    Shot("apagao", builder.SpawnCenter, Vector3.forward, 4f);
                    if (water != null) water.ForceNight = -1f;
                    lightsOut.SetDarkness(0f);
                    foreach (CeilingLight lamp in CeilingLight.All) lamp.SetPower(PowerPhase.Steady);
                }
            }
            finally
            {
                foreach (CustomPassVolume pass in camcorderPasses) if (pass != null) pass.enabled = true;
                if (water != null) water.LocalUnderwater = false;
                camera.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(rig);
                ShaderUtil.allowAsyncCompilation = asyncBefore;
            }

            return $"{shots} vistas (com e sem filmadora) em {folder}";
        }

        // ---------------------------------------------------------------- Volume

        /// <summary>
        /// Perfil da fase, regravado a cada montagem. Céu em degradê (é dele que vem a luz
        /// ambiente, que não é barrada por parede: é a meia-sombra esverdeada de todo canto),
        /// névoa clara e leve, exposição fixa, bloom para as janelas estourarem e os efeitos de
        /// câmera do Level 0. Sem gradação de cor: a filmadora (CamcorderPass) já gradua.
        /// Valores de céu e névoa em nits (a exposição fixa divide por ~54).
        /// </summary>
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
                // Recria os componentes: o perfil sempre sai igual ao que está escrito aqui.
                foreach (VolumeComponent old in profile.components.ToArray())
                {
                    profile.components.Remove(old);
                    Object.DestroyImmediate(old, true);
                }
            }

            var environment = Add<VisualEnvironment>(profile);
            environment.skyType.Override((int)SkyType.Gradient);
            environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

            // Degradê: em cima creme-esverdeado (luz que chega no piso), embaixo verde-água (a
            // água e o azulejo rebatendo: é o que chega no teto). O LightsOut escurece este céu
            // no apagão, então ele tem de continuar sendo um GradientSky.
            var sky = Add<GradientSky>(profile);
            sky.top.Override(new Color(46f, 52f, 42f));
            sky.middle.Override(new Color(44f, 52f, 45f));
            sky.bottom.Override(new Color(40f, 56f, 50f));
            sky.gradientDiffusion.Override(1f);
            sky.exposure.Override(0f);
            sky.multiplier.Override(1f);

            var exposure = Add<UnityEngine.Rendering.HighDefinition.Exposure>(profile);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(FixedEv);

            // Névoa da cor do céu: o fundo dos corredores clareia em vez de escurecer, e no
            // apagão ela escurece junto com o céu. Volumétrica para os fachos de sol.
            var fog = Add<Fog>(profile);
            fog.enabled.Override(true);
            fog.colorMode.Override(FogColorMode.SkyColor);
            fog.tint.Override(Color.white);
            fog.meanFreePath.Override(300f);
            fog.baseHeight.Override(30f);
            fog.maximumHeight.Override(60f);
            fog.enableVolumetricFog.Override(true);
            fog.albedo.Override(new Color(0.92f, 0.96f, 0.93f));
            fog.anisotropy.Override(0.5f);
            // O ar é limpo: a luz ambiente quase não acende a névoa; quem acende são os fachos de sol.
            fog.globalLightProbeDimmer.Override(0.15f);
            fog.depthExtent.Override(50f);

            // O céu é uniforme e não há sonda de reflexo: sem isto todo azulejo liso espelha o
            // mesmo verde chapado (de raspão, quase 100 %), apagando o rejunte e lavando a água.
            // A refração da água, perto da borda da tela, ia se misturando ao céu (verde-água claro):
            // dava uma franja colorida em volta do quadro. Sem a transição, ela usa a própria imagem.
            var refraction = Add<ScreenSpaceRefraction>(profile);
            refraction.screenFadeDistance.Override(0.001f);

            var indirect = Add<IndirectLightingController>(profile);
            indirect.reflectionLightingMultiplier.Override(0.3f);

            var occlusion = Add<ScreenSpaceAmbientOcclusion>(profile);
            occlusion.intensity.Override(0.8f);
            occlusion.radius.Override(1.2f);

            var bloom = Add<Bloom>(profile);
            bloom.intensity.Override(0.25f);
            bloom.threshold.Override(1f);
            bloom.scatter.Override(0.55f);

            // Efeitos de câmera iguais aos do Level 0.
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

        sealed class PoolMaterials
        {
            public Material Tile, Mosaic, Accent, Coping, Water, Caustics, Window, GlassBlock, Lamp, Rail, SkylightOn, SkylightOff;
            /// <summary>Manchas, na ordem do LevelBuilder.poolStainDecals: limo, calcário, mofo, ferrugem.</summary>
            public Material[] Stains;
            /// <summary>Marcações, na ordem do LevelBuilder.poolMarkDecals: 1.2 M, NO DIVING, DEEP END, T de raia.</summary>
            public Material[] Marks;
        }

        static PoolMaterials EnsureMaterials()
        {
            EnsureFolder(ArtFolder);
            // Pintura antiga da ondulação (altura em cinza), trocada pelo mapa de normais Water_Normal.
            AssetDatabase.DeleteAsset($"{ArtFolder}/Water_Height.png");

            // Azulejo: albedo da Folha A; a máscara deixa o rejunte fosco e um pouco mais escuro.
            Texture2D tile = Derived("Tile_Albedo", "Tile_White", TileAlbedo, TextureKind.Color);
            Texture2D tileHeight = Derived("Tile_Height", "Tile_White", c => Gray(TileAmount(c)), TextureKind.HeightToNormal);
            Texture2D tileMask = Derived("Tile_Mask", "Tile_White", TileMask, TextureKind.Linear);
            Texture2D accent = Derived("Tile_Accent", "Tile_White", AccentTile, TextureKind.Color);
            Texture2D mosaic = Derived("Mosaic_Pale", "Mosaic_Pool", PaleMosaic, TextureKind.Color);
            Texture2D coping = Source("Coping");

            Texture2D waterNormal = SourceOr("Water_Height", TextureKind.HeightToNormal,
                                             () => Painted("Water_Normal", 1024, (x, y, s) => RippleNormal(x, y, s, 41, 5, 12, false), TextureKind.Normal), "Water_Normal");
            Texture2D waterDetail = Painted("Water_Detail", 512, (x, y, s) => RippleNormal(x, y, s, 77, 3, 6, true), TextureKind.Linear);

            float[] causticA = null;
            Texture2D caustics = SourceOr("Caustics", TextureKind.Color, () =>
            {
                causticA = CausticField(512, 1998, 22);
                return Painted("Caustics", 512, (x, y, s) => Gray(causticA[y * s + x]), TextureKind.Color);
            });
            float[] causticB = CausticField(512, 31337, 16);
            Texture2D causticsDetail = Painted("Caustics_Detail", 512, (x, y, s) => CausticDetail(causticB[y * s + x]), TextureKind.Linear);

            Texture2D window = SourceOr("Window_Frosted", TextureKind.Color, () => Painted("Window_Frosted", 512, PaintWindow, TextureKind.Color));
            Texture2D glassBlock = SourceOr("Glass_Block", TextureKind.Color, () => Painted("Glass_Block", 512, (x, y, s) => PaintGlassBlock(x, y, s, false), TextureKind.Color));
            // O que acende no tijolo de vidro é o vidro (verde-água), não o rejunte (cinza claro).
            bool glassSource = File.Exists($"{SourceFolder}/Glass_Block.png");
            Texture2D glassGlow = glassSource
                ? Derived("Glass_Block_Glow", "Glass_Block", GlassGlow, TextureKind.Color)
                : Painted("Glass_Block_Glow", 512, (x, y, s) => PaintGlassBlock(x, y, s, true), TextureKind.Color);

            // Manchas da Folha C (decalques, com transparência): poucas e discretas.
            Material[] stains =
            {
                Stain("Stain_Waterline", new Color(0.9f, 0.92f, 0.86f), 0.55f, 0.45f),
                Stain("Stain_Limescale", new Color(1f, 1f, 0.97f), 0.5f, 0.25f),
                Stain("Stain_GroutMold", new Color(0.9f, 0.92f, 0.88f), 0.5f, 0.3f),
                // Debaixo d'água a cáustica e a névoa apagam a ferrugem: um pouco mais forte que as outras.
                Stain("Stain_Rust", new Color(0.85f, 0.82f, 0.8f), 0.65f, 0.3f),
            };
            // Marcações da Folha D (tinta preta e o T azul-marinho, com falhas): é tinta, quase opaca.
            Material[] marks =
            {
                Stain("Mark_Depth", Color.white, 0.92f, 0.45f),
                Stain("Mark_NoDiving", Color.white, 0.92f, 0.45f),
                Stain("Mark_DeepEnd", Color.white, 0.92f, 0.45f),
                Stain("Mark_LaneEnd", Color.white, 0.95f, 0.6f),
            };
            Texture2D frosted = Painted("Skylight_Glow", 256, PaintFrosted, TextureKind.Color);

            // Albedo do azulejo: #D8D6CC (quase neutro, um pouco quente; o marfim vem da luz).
            // A textura da Folha A é levemente fria (227, 227, 231): o tom corrige.
            var tileTint = new Color(0.95f, 0.94f, 0.88f);

            var m = new PoolMaterials
            {
                Tile = Lit("Tile_White", tileTint, tile, tileHeight, 0.65f, mask: tileMask),
                Accent = Lit("Tile_Accent", Color.white, accent, tileHeight, 0.65f, mask: tileMask),
                Mosaic = Lit("Mosaic_Pool", Color.white, mosaic, null, 0.72f),
                Coping = Lit("Coping", new Color(0.97f, 0.96f, 0.92f), coping, null, 0.55f),
                Rail = Lit("Rail_Steel", new Color(0.78f, 0.78f, 0.75f), null, null, 0.8f, metallic: 1f),

                // Vidros acesos: emissivos o bastante para estourar (com o bloom) na exposição fixa.
                Window = Lit("Window_Frosted", Color.white, window, null, 0.35f, new Color(1f, 0.97f, 0.9f) * 400f, window),
                GlassBlock = Lit("Glass_Block", Color.white, glassBlock, null, 0.8f, (glassSource ? new Color(0.95f, 1f, 0.97f) : new Color(0.72f, 1f, 0.9f)) * 180f, glassGlow),
                Lamp = Lit("Pool_Lamp", new Color(0.85f, 1f, 1f), null, null, 0.9f, new Color(0.55f, 0.95f, 1f) * 400f),
                SkylightOn = Lit("Skylight_On", Color.white, frosted, null, 0.4f, new Color(1f, 0.96f, 0.88f) * 500f, frosted),
                // Apagada: vidro azul-noite (#223048), com um resto de céu noturno atrás.
                SkylightOff = Lit("Skylight_Off", new Color(0.13f, 0.19f, 0.28f), frosted, null, 0.4f, new Color(0.25f, 0.45f, 1f) * 16f, frosted),
            };

            m.Water = WaterMaterial(waterNormal, waterDetail);
            m.Caustics = CausticsMaterial(caustics, causticsDetail);
            m.Stains = stains;
            m.Marks = marks;

            AssetDatabase.SaveAssets();
            return m;
        }

        // Água: HDRP/Lit transparente com refração. A cor vem do piso branco visto através dela
        // (transmitância por absorção), não de uma película: o alfa é baixo. A transmitância
        // (#60C0A4 sobre azulejo ao sol #F5F3DA, em linear) dá a água rasa; a área funda fica mais
        // escura porque o fundo dela é de pastilha verde-água (o material é um só para o mapa).
        // Duas ondulações: o mapa de normais (2,5 m) e o de detalhe (0,7 m), cada um rolando
        // para um lado (PoolWater).
        static Material WaterMaterial(Texture2D normal, Texture2D detail)
        {
            Material water = Lit("Water", new Color(0.39f, 0.79f, 0.76f, 0.04f), null, normal, 0.96f);
            if (water == null) return null;

            water.SetFloat("_SurfaceType", 1f);
            water.SetFloat("_BlendMode", 0f);
            water.SetFloat("_ZWrite", 0f);
            water.SetFloat("_TransparentZWrite", 0f);
            water.SetFloat("_EnableFogOnTransparent", 1f);
            water.SetFloat("_EnableBlendModePreserveSpecularLighting", 1f);
            water.SetFloat("_SupportDecals", 0f);
            water.SetFloat("_ReceivesSSRTransparent", 0f);

            water.SetFloat("_RefractionModel", 1f); // plano
            water.SetFloat("_Ior", 1.33f);
            water.SetColor("_TransmittanceColor", new Color(0.45f, 0.82f, 0.78f));
            // Espessura pequena: no modelo plano o desvio cresce com ela e borra o piso.
            water.SetFloat("_ATDistance", 0.12f);
            water.SetFloat("_Thickness", 0.12f);

            water.SetFloat("_NormalScale", 0.18f);
            water.SetTextureScale("_BaseColorMap", new Vector2(3.2f, 3.2f)); // a malha da água tem 4 m por repetição: 1,25 m
            water.SetTexture("_DetailMap", detail);
            water.SetTextureScale("_DetailMap", new Vector2(8f, 8f)); // 0,5 m
            water.SetFloat("_LinkDetailsWithBase", 0f);
            water.SetFloat("_DetailAlbedoScale", 0f);
            water.SetFloat("_DetailNormalScale", 0.25f);
            water.SetFloat("_DetailSmoothnessScale", 0f);

            HDMaterial.ValidateMaterial(water);
            EditorUtility.SetDirty(water);
            return water;
        }

        // Cáustica: malha do construtor por cima do fundo e das paredes submersas, somada à
        // imagem. É HDRP/Lit (aditivo) para receber luz: forte nas manchas de sol, fraca na
        // sombra e apagada no apagão, sem precisar de shader próprio. Duas redes de linhas: a
        // textura base e o detalhe (que escurece onde a segunda rede não tem linha), rolando
        // cada uma para um lado (PoolWater). Desenhada antes da refração, para aparecer
        // através da água.
        static Material CausticsMaterial(Texture2D caustics, Texture2D detail)
        {
            Material material = Lit("Pool_Caustics", new Color(0.95f, 1f, 0.97f, 1f), caustics, null, 0f);
            if (material == null) return null;

            material.SetFloat("_SurfaceType", 1f);
            material.SetFloat("_BlendMode", 1f); // aditivo
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_TransparentZWrite", 0f);
            material.SetFloat("_EnableFogOnTransparent", 1f);
            material.SetFloat("_EnableBlendModePreserveSpecularLighting", 0f);
            material.SetFloat("_SupportDecals", 0f);
            material.SetFloat("_ReceivesSSRTransparent", 0f);
            material.SetFloat("_RefractionModel", 0f);
            material.SetFloat("_SpecularOcclusionMode", 0f);

            material.SetTextureScale("_BaseColorMap", new Vector2(2f, 2f)); // 2 m por repetição
            material.SetTexture("_DetailMap", detail);
            material.SetTextureScale("_DetailMap", new Vector2(2.6f, 2.6f));
            material.SetFloat("_LinkDetailsWithBase", 0f);
            material.SetFloat("_DetailAlbedoScale", 1f);
            material.SetFloat("_DetailNormalScale", 0f);
            material.SetFloat("_DetailSmoothnessScale", 0f);

            // Antes da refração: a água refrata a imagem já com a cáustica.
            material.renderQueue = 2750; // HDRenderQueue.Priority.PreRefraction (interno do HDRP)
            HDMaterial.ValidateMaterial(material);
            HDMaterial.SetRenderingPass(material, HDMaterial.RenderingPass.BeforeRefraction);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material Lit(string name, Color baseColor, Texture2D albedo, Texture2D normal, float smoothness,
                            Color emissive = default, Texture2D emissiveMap = null, Texture2D mask = null, float metallic = 0f)
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
                // Volta ao padrão do HDRP/Lit antes de aplicar: o material sai sempre igual ao
                // que está escrito aqui, mesmo que uma versão antiga tenha ligado outra coisa.
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
            material.SetTexture("_NormalMap", normal);
            if (normal != null) material.SetFloat("_NormalScale", 1f);
            material.SetTexture("_MaskMap", mask);
            if (mask != null)
            {
                // Máscara: metálico 0, oclusão no rejunte, brilho do azulejo (0,7) e do rejunte (0,3).
                material.SetFloat("_MetallicRemapMin", 0f);
                material.SetFloat("_MetallicRemapMax", 0f);
                material.SetFloat("_SmoothnessRemapMin", 0f);
                material.SetFloat("_SmoothnessRemapMax", 1f);
                material.SetFloat("_AORemapMin", 0f);
                material.SetFloat("_AORemapMax", 1f);
            }
            material.SetColor("_EmissiveColor", emissive);
            material.SetTexture("_EmissiveColorMap", emissiveMap);

            HDMaterial.ValidateMaterial(material);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- Texturas

        enum TextureKind
        {
            /// <summary>Cor (sRGB).</summary>
            Color,
            /// <summary>Dados lineares (máscara, mapa de detalhe).</summary>
            Linear,
            /// <summary>Mapa de normais já em RGB.</summary>
            Normal,
            /// <summary>Altura em tons de cinza, que o importador converte em normais.</summary>
            HeightToNormal
        }

        // Decalque (HDRP/Decal) com a textura de mesmo nome em Art/Fontes/Level2: manchas da Folha C
        // e marcações da Folha D. Mistura pelo alfa da textura; só cor e brilho (sem relevo).
        static Material Stain(string name, Color tint, float blend, float smoothness)
        {
            string texturePath = $"{SourceFolder}/{name}.png";
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[BackRooms] Textura {texturePath} não encontrada.");
                return null;
            }
            if (importer.wrapMode != TextureWrapMode.Clamp || !importer.alphaIsTransparency || !importer.sRGBTexture)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            string path = $"{ArtFolder}/Decal_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created)
            {
                Shader shader = Shader.Find("HDRP/Decal");
                if (shader == null) { Debug.LogError("[BackRooms] Shader HDRP/Decal não encontrado."); return null; }
                material = new Material(shader) { name = "Decal_" + name };
            }

            material.SetColor("_BaseColor", tint);
            material.SetTexture("_BaseColorMap", texture);
            material.SetFloat("_DecalBlend", blend);
            material.SetFloat("_AffectAlbedo", 1f);
            material.SetFloat("_AffectNormal", 0f);
            material.SetFloat("_AffectMetal", 0f);
            material.SetFloat("_AffectAO", 0f);
            material.SetFloat("_AffectSmoothness", 1f);
            material.SetFloat("_Smoothness", smoothness);
            // Opacidade do brilho pelo contorno da mancha (alfa da cor), como no Level 0.
            material.SetFloat("_MaskBlendSrc", 0f);
            material.SetInt("_DrawOrder", 0);

            HDMaterial.ValidateMaterial(material);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
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

        // Textura-fonte em Art/Fontes/Level2 se existir (folhas do Higgsfield); senão, a pintura em código.
        // A pintura que ficou para trás (Art/Level2/<paintedName>.png) é apagada quando a fonte existe.
        static Texture2D SourceOr(string name, TextureKind kind, Func<Texture2D> paint, string paintedName = null)
        {
            string path = $"{SourceFolder}/{name}.png";
            if (!File.Exists(path)) return paint();
            AssetDatabase.DeleteAsset($"{ArtFolder}/{paintedName ?? name}.png");

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Configure(path, kind, 8);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Textura pintada em código (provisória até a folha do Higgsfield). Fica em Art/Level2 e é
        // repintada a cada montagem; o arquivo só é regravado se mudou.
        static Texture2D Painted(string name, int size, Func<int, int, int, Color32> paint, TextureKind kind)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = paint(x, y, size);
            return Save(name, size, size, pixels, kind);
        }

        // Textura tirada de uma fonte de Art/Fontes/Level2 (recolorida, máscara). Fica em Art/Level2.
        static Texture2D Derived(string name, string sourceName, Func<Color32, Color32> map, TextureKind kind)
        {
            string sourcePath = $"{SourceFolder}/{sourceName}.png";
            if (!File.Exists(sourcePath))
            {
                Debug.LogWarning($"[BackRooms] Textura {sourcePath} não encontrada.");
                return null;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(File.ReadAllBytes(sourcePath));
            Color32[] pixels = source.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) pixels[i] = map(pixels[i]);
            int width = source.width, height = source.height;
            Object.DestroyImmediate(source);
            return Save(name, width, height, pixels, kind);
        }

        static Texture2D Save(string name, int width, int height, Color32[] pixels, TextureKind kind)
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

            Configure(path, kind, 4);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void Configure(string path, TextureKind kind, int aniso)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            TextureImporterType type = kind == TextureKind.Normal || kind == TextureKind.HeightToNormal
                ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool srgb = kind == TextureKind.Color;
            bool fromHeight = kind == TextureKind.HeightToNormal;

            if (importer.textureType == type && importer.sRGBTexture == srgb && importer.convertToNormalmap == fromHeight &&
                importer.wrapMode == TextureWrapMode.Repeat && importer.anisoLevel == aniso &&
                (!fromHeight || Mathf.Approximately(importer.heightmapScale, 0.04f)))
                return;

            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.convertToNormalmap = fromHeight;
            if (fromHeight) importer.heightmapScale = 0.04f;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = aniso;
            importer.SaveAndReimport();
        }

        static Color32 Gray(float value)
        {
            byte c = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
            return new Color32(c, c, c, 255);
        }

        static float Luma(Color32 c) => (c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) / 255f;

        // 1 no azulejo, 0 no rejunte (pela claridade da Folha A: rejunte ~170, azulejo ~232).
        static float TileAmount(Color32 c) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 0.87f, Luma(c)));

        // Máscara do HDRP: R metálico, G oclusão, B máscara de detalhe, A brilho.
        static Color32 TileMask(Color32 c)
        {
            float tile = TileAmount(c);
            return new Color(0f, Mathf.Lerp(0.6f, 1f, tile), 1f, Mathf.Lerp(0.3f, 0.68f, tile));
        }

        // Albedo do azulejo: o da Folha A com o rejunte mais escuro (~60 % do azulejo, cinza-oliva),
        // para a grade continuar lendo de longe e na sombra.
        static Color32 TileAlbedo(Color32 c)
        {
            float tile = TileAmount(c);
            Color color = c;
            Color grout = new Color(0.62f, 0.63f, 0.58f) * Mathf.Clamp(Luma(c) / 0.72f, 0.85f, 1.1f);
            Color result = Color.Lerp(grout, color, tile);
            result.a = 1f;
            return result;
        }

        // Brilho do tijolo de vidro da Folha B: só onde há cor (o vidro verde-água); o rejunte,
        // cinza, fica quase apagado.
        static Color32 GlassGlow(Color32 c)
        {
            Color color = c;
            float chroma = Mathf.Max(color.r, Mathf.Max(color.g, color.b)) - Mathf.Min(color.r, Mathf.Min(color.g, color.b));
            float glass = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.14f, chroma));
            Color result = color * Mathf.Lerp(0.12f, 1f, glass);
            result.a = 1f;
            return result;
        }

        // Azulejo das faixas e da parede do poço: verde-petróleo azulado, com o mesmo rejunte claro.
        static Color32 AccentTile(Color32 c)
        {
            float tile = TileAmount(c);
            float shade = Mathf.Clamp(Luma(c) / 0.9f, 0.85f, 1.1f);
            Color face = new Color(0.12f, 0.43f, 0.46f) * shade;
            Color grout = new Color(0.62f, 0.66f, 0.64f);
            Color result = Color.Lerp(grout, face, tile);
            result.a = 1f;
            return result;
        }

        // Pastilha do fundo: a da Folha A é azul-celeste saturada; aqui ela fica pálida e mais
        // verde (a água é quem tinge), mantendo a variação entre as pastilhas.
        static Color32 PaleMosaic(Color32 c)
        {
            Color color = c;
            float gray = color.r * 0.3f + color.g * 0.59f + color.b * 0.11f;
            Color pale = Color.Lerp(new Color(gray, gray, gray), color, 0.55f);
            pale = new Color(pale.r * 0.8f, pale.g * 0.95f, pale.b * 0.86f, 1f);
            return pale;
        }

        // Ondulação: soma de ondas com número inteiro de voltas na textura (emenda sozinha), de
        // direções e fases sorteadas, comprimentos entre 1/kMin e 1/kMax da repetição.
        // Devolve a normal: em RGB (mapa de normais) ou empacotada como mapa de detalhe do HDRP
        // (R e B neutros, normal em G e A).
        static readonly Dictionary<int, (int kx, int ky, float a, float phase)[]> Waves = new Dictionary<int, (int, int, float, float)[]>();

        static Color32 RippleNormal(int x, int y, int size, int seed, int kMin, int kMax, bool detailPacked)
        {
            if (!Waves.TryGetValue(seed, out var waves))
            {
                var rng = new Random(seed);
                var list = new List<(int, int, float, float)>();
                while (list.Count < 56)
                {
                    int kx = rng.Next(-kMax, kMax + 1), ky = rng.Next(-kMax, kMax + 1);
                    float k = Mathf.Sqrt(kx * kx + ky * ky);
                    if (k < kMin || k > kMax) continue;
                    list.Add((kx, ky, (0.5f + 0.5f * (float)rng.NextDouble()) / Mathf.Pow(k, 1.3f), (float)(rng.NextDouble() * Math.PI * 2)));
                }
                // Escala para a inclinação média ficar perto de 0,35 (o material dosa a força).
                float sum = 0f;
                foreach (var w in list) sum += 0.5f * Mathf.Pow(w.Item3 * 2f * Mathf.PI * Mathf.Sqrt(w.Item1 * w.Item1 + w.Item2 * w.Item2), 2f);
                float scale = 0.35f / Mathf.Sqrt(sum);
                waves = list.Select(w => (w.Item1, w.Item2, w.Item3 * scale, w.Item4)).ToArray();
                Waves[seed] = waves;
            }

            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
            float du = 0f, dv = 0f;
            foreach (var w in waves)
            {
                float angle = 2f * Mathf.PI * (w.kx * u + w.ky * v) + w.phase;
                float c = Mathf.Cos(angle) * w.a * 2f * Mathf.PI;
                du += c * w.kx;
                dv += c * w.ky;
            }

            Vector3 n = new Vector3(-du, -dv, 1f).normalized;
            byte nx = (byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f);
            byte ny = (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f);
            byte nz = (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f);
            return detailPacked ? new Color32(128, ny, 128, nx) : new Color32(nx, ny, nz, 255);
        }

        // Cáustica: rede de linhas claras nas divisas de células de Voronoi (distância F2 - F1),
        // com o domínio torcido por ondas para as linhas saírem curvas. Tudo periódico na
        // textura (pontos e distâncias "dando a volta"), para emendar.
        static float[] CausticField(int size, int seed, int points)
        {
            var rng = new Random(seed);
            var px = new float[points];
            var py = new float[points];
            for (int i = 0; i < points; i++) { px[i] = (float)rng.NextDouble(); py[i] = (float)rng.NextDouble(); }
            float p1 = (float)rng.NextDouble() * 6.28f, p2 = (float)rng.NextDouble() * 6.28f, p3 = (float)rng.NextDouble() * 6.28f, p4 = (float)rng.NextDouble() * 6.28f;

            var field = new float[size * size];
            float max = 0f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    const float tau = 2f * Mathf.PI;
                    float wu = u + 0.035f * Mathf.Sin(tau * (2f * v + u) + p1) + 0.018f * Mathf.Sin(tau * (5f * v - 3f * u) + p2);
                    float wv = v + 0.035f * Mathf.Sin(tau * (2f * u - v) + p3) + 0.018f * Mathf.Sin(tau * (4f * u + 5f * v) + p4);

                    float f1 = 9f, f2 = 9f;
                    for (int i = 0; i < points; i++)
                    {
                        float dx = Mathf.Repeat(wu - px[i] + 0.5f, 1f) - 0.5f;
                        float dy = Mathf.Repeat(wv - py[i] + 0.5f, 1f) - 0.5f;
                        float d = dx * dx + dy * dy;
                        if (d < f1) { f2 = f1; f1 = d; }
                        else if (d < f2) f2 = d;
                    }

                    float edge = Mathf.Sqrt(f2) - Mathf.Sqrt(f1);
                    float line = Mathf.Exp(-edge / 0.011f) + 0.35f * Mathf.Exp(-edge / 0.04f);
                    field[y * size + x] = line;
                    max = Mathf.Max(max, line);
                }

            for (int i = 0; i < field.Length; i++) field[i] = Mathf.Pow(field[i] / max, 1.2f);
            return field;
        }

        // Detalhe da cáustica: 0,5 (neutro) onde a segunda rede tem linha, mais escuro fora dela.
        // O detalhe de cor do HDRP escurece abaixo de 0,5, então as duas redes se multiplicam.
        static Color32 CausticDetail(float value)
        {
            byte r = (byte)Mathf.RoundToInt(Mathf.Lerp(0.3f, 0.5f, Mathf.Sqrt(Mathf.Clamp01(value))) * 255f);
            return new Color32(r, 128, 128, 128);
        }

        // Vidro fosco do janelão (a textura cobre o janelão inteiro): branco quente, mais claro em
        // cima, levemente manchado, com o caixilho fino nas bordas.
        static Color32 PaintWindow(int x, int y, int size)
        {
            int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
            if (edge < 6) return new Color32(150, 152, 146, 255);
            if (edge < 9) return new Color32(205, 206, 200, 255);
            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
            float mottle = 0.03f * Mathf.Sin(u * 37f + v * 11f) * Mathf.Sin(v * 29f - u * 7f);
            float c = Mathf.Clamp01(0.9f + 0.08f * v + mottle);
            return new Color(c, c * 0.995f, c * 0.97f, 1f);
        }

        // Tijolo de vidro: 6 x 6 por repetição, verde-água, com a borda do tijolo mais escura e
        // ondas concêntricas no meio. Na versão "acesa" (emissão), o rejunte não brilha.
        static Color32 PaintGlassBlock(int x, int y, int size, bool glow)
        {
            float cell = size / 6f;
            float lx = (x + 0.5f) % cell / cell, ly = (y + 0.5f) % cell / cell;
            float edge = Mathf.Min(Mathf.Min(lx, 1f - lx), Mathf.Min(ly, 1f - ly));
            if (edge < 0.035f) return glow ? new Color32(30, 34, 32, 255) : new Color32(196, 200, 194, 255);

            float bevel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.035f, 0.16f, edge));
            float r = Vector2.Distance(new Vector2(lx, ly), new Vector2(0.5f, 0.5f));
            float ripple = 0.03f * Mathf.Sin(r * 44f);
            float light = Mathf.Clamp01(0.62f + 0.33f * bevel + ripple * bevel);
            var tint = new Color(0.74f, 0.95f, 0.88f);
            return new Color(tint.r * light, tint.g * light, tint.b * light, 1f);
        }

        // Vidro fosco da claraboia: claro, mais claro no meio, com o caixilho fino nas bordas.
        static Color32 PaintFrosted(int x, int y, int size)
        {
            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
            int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
            if (edge < 4) return new Color32(150, 152, 146, 255);
            if (edge < 6) return new Color32(200, 202, 196, 255);
            float center = 1f - Mathf.Clamp01(Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 1.2f);
            byte c = (byte)Mathf.RoundToInt(Mathf.Lerp(215f, 255f, center));
            return new Color32(c, c, (byte)Mathf.Max(0, c - 6), 255);
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
