using System;
using System.Collections.Generic;
using System.IO;
using BackRooms.Audio;
using BackRooms.Entities;
using BackRooms.Items;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.UI;
using BackRooms.World;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Menu BackRooms > Montar Level 0. Gera texturas, materiais e o perfil de Volume, e
    /// monta a cena de teste com o construtor do nível e o jogador.
    /// </summary>
    public static class Level0Setup
    {
        const string ArtFolder = "Assets/_Game/Art/Level0";
        const string SettingsFolder = "Assets/_Game/Settings";
        const string PrefabFolder = "Assets/_Game/Prefabs";
        const string SceneFolder = "Assets/_Game/Scenes";
        const string ScenePath = SceneFolder + "/Level0_Test.unity";
        const string ProfilePath = SettingsFolder + "/Level0_Volume.asset";
        const string PlayerPrefabPath = PrefabFolder + "/Player.prefab";
        public const string EntityPrefabPath = PrefabFolder + "/Entity.prefab";

        static readonly string[] Phrases = { "HELP!", "RUN", "IT HEARS YOU" };

        /// <summary>
        /// Versão do conteúdo gerado (cena, texturas, materiais). Aumente sempre que mudar
        /// algo que só aparece depois de recriar tudo — texturas, materiais, objetos da cena
        /// ou valores padrão do LevelBuilder. O Level0AutoUpdate compara com o que está
        /// gravado na cena e oferece recriar.
        /// </summary>
        public const int ContentVersion = 19;

        /// <summary>Música da tela inicial (cópia do arquivo mandado pelo usuário; não é gerada).</summary>
        public const string MenuMusicPath = "Assets/_Game/Audio/Musica/Menu_Tema.mp3";

        [MenuItem("BackRooms/Montar Level 0 (sala de teste)")]
        public static void Build()
        {
            bool regenerate = false;

            if (File.Exists(ScenePath))
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Level 0",
                    "A cena Level0_Test já existe.\n\n" +
                    "• Recriar tudo: apaga e gera de novo texturas, materiais e o perfil de Volume (perde ajustes feitos à mão).\n" +
                    "• Só a cena: mantém os assets existentes e cria só o que estiver faltando.",
                    "Recriar tudo", "Cancelar", "Só a cena");

                if (choice == 1) return;
                regenerate = choice == 0;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildScene(regenerate);
        }

        /// <summary>
        /// O que muda de uma fase para outra ao montar a cena. O padrão é o Level 0; o Level 1
        /// de teste (Level1Setup) usa o mesmo montador com outra cena, mapa menor, sem buracos,
        /// sem nichos, com o Capitão no lugar da entidade e com os móveis.
        /// </summary>
        public sealed class SceneOptions
        {
            public string scenePath = ScenePath;
            public string levelName = "Level0";
            public int size = LevelBuilder.DefaultSize;
            public bool pits = true;
            public bool alcoves = true;
            public bool entity = true;
            /// <summary>Inimigo da fase no lugar da entidade (o Level 1 põe o Capitão). Nulo: a entidade.</summary>
            public GameObject enemyPrefab;
            /// <summary>A quantas células de caminhada da sala inicial alguém precisa chegar para o inimigo aparecer.</summary>
            public int enemyWakeCells = 8;
            public bool paintedArrows = true;
            /// <summary>Piscinas (Level 2): profundidades, escadas e água.</summary>
            public bool pools;
            /// <summary>Chamado com o objeto do nível antes de salvar (o Level 1 põe a camada de móveis).</summary>
            public Action<GameObject> decorate;
        }

        /// <summary>Monta a cena sem perguntar nada. Usado também pela linha de comando.</summary>
        public static void BuildScene(bool regenerateAssets)
        {
            BuildScene(regenerateAssets, new SceneOptions());
        }

        public static void BuildScene(bool regenerateAssets, SceneOptions options)
        {
            if (regenerateAssets)
            {
                // Sai da cena atual antes de apagar os assets que ela usa.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(ArtFolder);
                AssetDatabase.DeleteAsset(ProfilePath);
            }

            EnsureFolder(ArtFolder);
            EnsureFolder(SettingsFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(SceneFolder);

            // Superfícies
            Texture2D wallAlbedo = EnsureTexture("Wallpaper_Albedo", 1024, PaintWallpaper, normalFromHeight: false);
            Texture2D carpetAlbedo = EnsureTexture("Carpet_Albedo", 1024, PaintCarpet, normalFromHeight: false);
            Texture2D carpetNormal = EnsureTexture("Carpet_Normal", 1024, PaintCarpetHeight, normalFromHeight: true);
            Texture2D ceilingAlbedo = EnsureTexture("Ceiling_Albedo", 256, PaintCeiling, normalFromHeight: false);
            Texture2D ceilingNormal = EnsureTexture("Ceiling_Normal", 256, PaintCeilingHeight, normalFromHeight: true);

            Material wall = EnsureMaterial("Wallpaper", Color.white, wallAlbedo, null, 0.2f, Color.black);

            // Versões limpas, só para a sala inicial.
            Texture2D wallCleanAlbedo = EnsureTexture("Wallpaper_Clean_Albedo", 1024, PaintWallpaperClean, normalFromHeight: false);
            // O carpete gerado por IA já é só a fibra, sem sujeira: serve também para a sala inicial.
            Texture2D carpetCleanAlbedo = File.Exists(TextureSourceFolder + "/Carpet_Albedo.png")
                ? carpetAlbedo
                : EnsureTexture("Carpet_Clean_Albedo", 1024, PaintCarpetClean, normalFromHeight: false);
            Material wallClean = EnsureMaterial("Wallpaper_Clean", Color.white, wallCleanAlbedo, null, 0.2f, Color.black);
            Material floorClean = EnsureMaterial("Carpet_Clean", Color.white, carpetCleanAlbedo, carpetNormal, 0.05f, Color.black);
            Material floor = EnsureMaterial("Carpet", Color.white, carpetAlbedo, carpetNormal, 0.05f, Color.black);
            Material ceiling = EnsureMaterial("Ceiling", Color.white, ceilingAlbedo, ceilingNormal, 0.1f, Color.black);
            Material baseboard = EnsureMaterial("Baseboard", Rgb(184, 160, 64), null, null, 0.35f, Color.black);
            // Difusor de acrílico prismático com duas lâmpadas tubulares atrás: acesa, as faixas
            // das lâmpadas brilham mais; apagada, aparecem como sombra cinza através do acrílico.
            Texture2D diffuserGlow = EnsurePainting("LightPanel_Glow", 128, 256, false, rng => PaintDiffuser(true));
            Texture2D diffuserAlbedo = EnsurePainting("LightPanel_Albedo", 128, 256, false, rng => PaintDiffuser(false));
            Material panelOn = EnsureMaterial("LightPanel_On", Color.white, diffuserAlbedo, null, 0.5f, new Color(1f, 0.98f, 0.92f) * 900f, diffuserGlow);
            Material panelOff = EnsureMaterial("LightPanel_Off", Rgb(150, 148, 134), diffuserAlbedo, null, 0.5f, Color.black);
            // Chapa pintada da calha das luminárias.
            Material lightHousing = EnsureMaterial("LightHousing", Rgb(196, 192, 176), null, null, 0.45f, Color.black);

            // Concreto das paredes e do fundo dos buracos.
            Texture2D concreteAlbedo = EnsureTexture("Concrete_Albedo", 512, PaintConcrete, normalFromHeight: false);
            Material pit = EnsureMaterial("PitConcrete", Color.white, concreteAlbedo, null, 0.12f, Color.black);

            // Fumaça que tapa o fundo dos buracos, e o preto opaco por baixo dela.
            Texture2D smokeTexture = EnsureTexture("Smoke_Alpha", 256, PaintSmoke, normalFromHeight: false);
            Material pitSmoke = EnsureUnlit("PitSmoke", Color.white, smokeTexture, transparent: true);
            Material pitVoid = EnsureUnlit("PitVoid", Color.black, null, transparent: false);

            // Marcas: sangue preto, poças, palavras e seta
            var blood = new Material[LevelGenerator.BloodVariants];
            for (int i = 0; i < blood.Length; i++)
            {
                int variant = i;
                Texture2D texture = EnsurePainting("Blood_" + i, 512, 512, true, rng => PaintBlood(rng, variant));
                blood[i] = EnsureDecal("Blood_" + i, texture, 0.75f);
            }

            var pools = new Material[LevelGenerator.PoolVariants];
            for (int i = 0; i < pools.Length; i++)
            {
                Texture2D texture = EnsurePainting("Pool_" + i, 512, 512, true, PaintPool);
                pools[i] = EnsureDecal("Pool_" + i, texture, 0.8f);
            }

            var texts = new Material[Mathf.Min(LevelGenerator.TextVariants, Phrases.Length)];
            for (int i = 0; i < texts.Length; i++)
            {
                string phrase = Phrases[i];
                Texture2D texture = EnsurePainting("Text_" + i, 1024, 384, true, rng => PaintPhrase(rng, phrase));
                texts[i] = EnsureDecal("Text_" + i, texture, 0.3f);
            }

            Material arrow = EnsureDecal("Arrow", EnsurePainting("Arrow", 512, 256, true, PaintArrow), 0.3f);

            // Sujeira e desgaste. As texturas são brancas; a cor vem do material.
            // Ordem de desenho -1: ficam por baixo do sangue, das palavras e das setas.
            int s = StainPainter.Size;
            var dampTint = Rgb(40, 35, 18);
            var dirtTint = Rgb(58, 50, 28);
            var wetCarpetTint = Rgb(34, 30, 16);
            var wornCarpetTint = Rgb(208, 196, 146);

            var wallStains = new[]
            {
                EnsureDecal("Stain_TopDrip", EnsurePainting("Stain_TopDrip", s, s, true, StainPainter.TopDrip), 0.45f, dampTint, 0.9f, true, -1),
                EnsureDecal("Stain_RisingDamp", EnsurePainting("Stain_RisingDamp", s, s, true, StainPainter.RisingDamp), 0.5f, dampTint, 0.9f, true, -1),
                EnsureDecal("Stain_Blotch", EnsurePainting("Stain_Blotch", s, s, true, rng => StainPainter.Blotch(rng, 0.3f, 0.7f, 1f)), 0f, dirtTint, 0.75f, false, -1)
            };

            Material wallScuff = EnsureDecal("Stain_Scuff", EnsurePainting("Stain_Scuff", s, s, true, StainPainter.Scuff), 0f, Rgb(46, 40, 30), 0.8f, false, -1);

            // Carpete úmido: mais escuro e só um pouco mais liso. Com brilho alto vira espelho.
            var floorWet = new Material[LevelGenerator.FloorWetVariants];
            for (int i = 0; i < floorWet.Length; i++)
            {
                Texture2D texture = EnsurePainting("Floor_Wet_" + i, s, s, true, rng => StainPainter.Blotch(rng, 0.22f, 0.45f, 1f));
                floorWet[i] = EnsureDecal("Floor_Wet_" + i, texture, 0.38f, wetCarpetTint, 0.9f, true, -1);
            }

            Material floorWear = EnsureDecal("Floor_Wear", EnsurePainting("Floor_Wear", s, s, true, rng => StainPainter.Blotch(rng, 0f, 0.85f, 1.7f)),
                                             0f, wornCarpetTint, 0.6f, false, -1);

            // Saída
            Texture2D signTexture = EnsurePainting("ExitSign", 256, 96, false, PaintExitSign);
            Material exitSign = EnsureMaterial("ExitSign", Color.white, signTexture, null, 0.4f, Color.white * 90f, signTexture);
            Material exitDoor = EnsureMaterial("ExitDoor", Rgb(16, 14, 12), null, null, 0.3f, Color.black);
            // Porta em 3D gerada no Higgsfield (Art/Fontes/ExitDoor.glb). Sem o arquivo, fica a plana.
            ImportedModels.EnsureExitDoor(ArtFolder, out Mesh exitDoorModel, out Material exitDoorModelMaterial);
            // Grelhas de ar nas paredes (geometria em código no LevelBuilder): chapa pintada, já amarelada.
            Material ventMaterial = EnsureMaterial("Vent", Rgb(188, 182, 160), null, null, 0.35f, Color.black);

            VolumeProfile profile = EnsureVolumeProfile();

            // Cena
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Ajustes feitos à mão na cena do Level 0 em 12/11/2026 e trazidos para cá; só nela,
            // para as cenas do Level 1 e do Level 2 saírem como estão.
            bool level0 = options.scenePath == ScenePath;
            // Sem o céu do Unity: o HDRP não usa este material (o céu dele vem do Volume).
            if (level0) RenderSettings.skybox = null;

            var volumeObject = new GameObject("Global Volume");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;

            var ambience = new GameObject("Ambiencia");
            ambience.AddComponent<AudioSource>().playOnAwake = false;
            ambience.AddComponent<FluorescentHum>();

            // Música do menu: um objeto na raiz de cada cena; em Play o primeiro sobrevive às
            // trocas de fase e os outros se apagam (MenuMusic).
            var music = new GameObject("MusicaDoMenu");
            music.AddComponent<AudioSource>().playOnAwake = false;
            music.AddComponent<MenuMusic>().clip = EnsureMenuMusic();

            // Tintas da caneta, na ordem das cores (PenTool.ColorNames).
            Material[] inks =
            {
                EnsureInk("Ink_Black", Rgb(14, 14, 16)),
                EnsureInk("Ink_Red", Rgb(196, 20, 20)),
                EnsureInk("Ink_Blue", Rgb(26, 58, 205))
            };

            Material hazmat = EnsureMaterial("Hazmat", Rgb(214, 186, 36), null, null, 0.35f, Color.black);
            Material visor = EnsureMaterial("Visor", Rgb(10, 10, 12), null, null, 0.85f, Color.black);
            // Borracha das luvas e botas; cinza do respirador, do cilindro de ar e do cinto.
            Material rubber = EnsureMaterial("Rubber", Rgb(18, 18, 20), null, null, 0.45f, Color.black);
            Material gear = EnsureMaterial("Gear", Rgb(96, 100, 102), null, null, 0.4f, Color.black);
            GameObject playerPrefab = CreatePlayerPrefab(hazmat, visor, rubber, gear, inks[0]);

            // Entidade: casca fosca e sulcada para os cipós do corpo, casco lustroso com estrias
            // e veias para a cabeça, dentes cor de osso e vermelho-escuro por dentro da boca.
            Material entityBark = EnsureSurface("Entity_Bark", Rgb(30, 26, 22), 0.28f, 2f, EntityPainter.BarkSize,
                                                EntityPainter.BarkColor, EntityPainter.BarkNormal);
            Material entityShell = EnsureSurface("Entity_Shell", Rgb(26, 26, 30), 0.74f, 1.5f, EntityPainter.ShellSize,
                                                 EntityPainter.ShellColor, EntityPainter.ShellNormal);
            Material entityBone = EnsureMaterial("Entity_Bone", Rgb(198, 188, 160), null, null, 0.35f, Color.black);
            Material entityMaw = EnsureMaterial("Entity_Maw", Rgb(74, 6, 8), null, null, 0.55f, new Color(1f, 0.04f, 0.04f) * 2.5f);
            GameObject entityPrefab = CreateEntityPrefab(entityBark, entityShell, entityBone, entityMaw);

            var level = new GameObject(options.levelName);
            var builder = level.AddComponent<LevelBuilder>();
            builder.width = options.size;
            builder.height = options.size;
            builder.pitsEnabled = options.pits;
            builder.alcovesEnabled = options.alcoves;
            builder.paintedArrows = options.paintedArrows;
            builder.poolsEnabled = options.pools;
            builder.wallMaterial = wall;
            builder.floorMaterial = floor;
            builder.ceilingMaterial = ceiling;
            builder.baseboardMaterial = baseboard;
            builder.panelOnMaterial = panelOn;
            builder.panelOffMaterial = panelOff;
            builder.lightHousingMaterial = lightHousing;
            builder.contentVersion = ContentVersion;
            builder.cleanWallMaterial = wallClean;
            builder.cleanFloorMaterial = floorClean;
            builder.pitMaterial = pit;
            builder.pitSmokeMaterial = pitSmoke;
            builder.pitVoidMaterial = pitVoid;
            builder.bloodDecals = blood;
            builder.poolDecals = pools;
            builder.textDecals = texts;
            builder.arrowDecal = arrow;
            builder.wallStainDecals = wallStains;
            builder.wallScuffDecal = wallScuff;
            builder.floorWetDecals = floorWet;
            builder.floorWearDecal = floorWear;
            builder.exitSignMaterial = exitSign;
            builder.exitDoorMaterial = exitDoor;
            builder.exitDoorModel = exitDoorModel;
            builder.exitDoorModelMaterial = exitDoorModelMaterial;
            builder.ventMaterial = ventMaterial;

            // Câmera do menu: mostra o mapa de fundo até o jogador local entrar.
            var menuCameraObject = new GameObject("CameraDoMenu");
            menuCameraObject.transform.position = new Vector3(0f, 1.65f, 0f);
            var menuCamera = menuCameraObject.AddComponent<Camera>();
            menuCamera.fieldOfView = 70f;
            menuCamera.nearClipPlane = 0.05f;
            menuCamera.farClipPlane = 150f;
            GetOrAdd<HDAdditionalCameraData>(menuCameraObject).antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            var menuListener = menuCameraObject.AddComponent<AudioListener>();

            // Sessão de rede. Sem gerência de cenas do Netcode: todos carregam esta mesma
            // cena por conta própria e o mapa é gerado localmente a partir da seed.
            var sessionObject = new GameObject("Sessao");
            var manager = sessionObject.AddComponent<NetworkManager>();
            var transport = sessionObject.AddComponent<UnityTransport>();
            if (level0)
            {
                // Valor de cena sem efeito: GameSession.Host define o endereço de escuta ao
                // hospedar (0.0.0.0, ou 127.0.0.1 só na sessão local) e Join o de conexão.
                var connection = transport.ConnectionData;
                connection.ServerListenAddress = "127.0.0.1";
                transport.ConnectionData = connection;
            }
            if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.PlayerPrefab = playerPrefab;
            manager.NetworkConfig.EnableSceneManagement = false;
            manager.NetworkConfig.TickRate = 30;

            var session = sessionObject.AddComponent<GameSession>();
            session.level = builder;
            session.menuCamera = menuCamera;
            session.menuListener = menuListener;

            // Quem decide quando e onde a entidade aparece. Roda só no host.
            if (options.entity)
            {
                var director = sessionObject.AddComponent<EntityDirector>();
                director.session = session;
                director.entityPrefab = options.enemyPrefab != null ? options.enemyPrefab : entityPrefab;
                director.wakeCells = options.enemyWakeCells;
            }

            // Apagão das luzes: a conta do tempo é igual em todas as máquinas.
            sessionObject.AddComponent<LightsOut>().session = session;

            // Menu (UI Toolkit) e o EventSystem que entrega o mouse a ele pelo Input System.
            var menuObject = new GameObject("Menu");
            menuObject.AddComponent<UIDocument>().panelSettings = EnsurePanelSettings();
            menuObject.AddComponent<ConnectionMenu>().session = session;

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            CreateCamcorder();

            // Onde ficam os riscos de caneta de todos os jogadores.
            var drawings = new GameObject("Rabiscos");
            drawings.AddComponent<DrawingBoard>().inkMaterials = inks;

            options.decorate?.Invoke(level);

            EditorSceneManager.SaveScene(scene, options.scenePath);
            AddSceneToBuild(options.scenePath);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = level;
            Debug.Log($"[BackRooms] {options.levelName} montado em {options.scenePath}. Aperte Play e escolha Hospedar (tecla H) ou Entrar (tecla J). WASD anda, Shift corre, Ctrl agacha, Espaço pula, F liga a lanterna.");
        }

        // Filmadora: o painel (REC, relógio, bateria) e o efeito VHS em tela cheia, que o
        // HDRP aplica depois do pós-processamento por meio de um Custom Pass.
        static void CreateCamcorder()
        {
            const string materialPath = SettingsFolder + "/CamcorderVHS.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("BackRooms/CamcorderVHS");
                if (shader == null)
                {
                    Debug.LogError("[BackRooms] Shader BackRooms/CamcorderVHS não encontrado (Assets/_Game/Shaders/CamcorderVHS.shader).");
                    return;
                }

                material = new Material(shader) { name = "CamcorderVHS" };
                AssetDatabase.CreateAsset(material, materialPath);
            }

            var camcorderObject = new GameObject("Filmadora");
            camcorderObject.AddComponent<Camcorder>();
            // Gravação de verdade em arquivo de vídeo (F9).
            camcorderObject.AddComponent<ScreenRecorder>();

            var volume = camcorderObject.AddComponent<CustomPassVolume>();
            volume.isGlobal = true;
            volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;

            var pass = volume.AddPassOfType<CamcorderPass>();
            pass.name = "VHS";
            pass.material = material;
        }

        // A música do menu é longa (2,5 min): sai do disco aos poucos (Streaming), em Vorbis,
        // estéreo e na taxa original. Decompress On Load ocuparia ~50 MB de memória.
        static AudioClip EnsureMenuMusic()
        {
            if (!File.Exists(MenuMusicPath))
            {
                Debug.LogWarning("[BackRooms] Música do menu não encontrada em " + MenuMusicPath + ": o menu fica sem música.");
                return null;
            }

            AssetDatabase.ImportAsset(MenuMusicPath);
            if (AssetImporter.GetAtPath(MenuMusicPath) is AudioImporter importer)
            {
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                bool changed = settings.loadType != AudioClipLoadType.Streaming ||
                               settings.compressionFormat != AudioCompressionFormat.Vorbis ||
                               !Mathf.Approximately(settings.quality, 0.7f) ||
                               settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate ||
                               importer.forceToMono || !importer.loadInBackground;
                if (changed)
                {
                    settings.loadType = AudioClipLoadType.Streaming;
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    settings.quality = 0.7f;
                    settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                    importer.defaultSampleSettings = settings;
                    importer.forceToMono = false;
                    importer.loadInBackground = true;
                    importer.SaveAndReimport();
                }
            }

            return AssetDatabase.LoadAssetAtPath<AudioClip>(MenuMusicPath);
        }

        static PanelSettings EnsurePanelSettings()
        {
            const string themePath = SettingsFolder + "/RuntimeTheme.tss";
            const string panelPath = SettingsFolder + "/MenuPanel.asset";

            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
            if (existing != null) return existing;

            // Tema padrão de runtime do UI Toolkit; sem um tema os controles não aparecem direito.
            if (!File.Exists(themePath))
            {
                File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(themePath, ImportAssetOptions.ForceSynchronousImport);
            }

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            AssetDatabase.CreateAsset(panel, panelPath);
            return panel;
        }

        // ---------------------------------------------------------------- Jogador

        // O prefab nasce com controle, câmera e ouvinte desligados: quem liga é o
        // PlayerNetwork, só na máquina do dono. Nas outras ele é a figura daquele jogador.
        static GameObject CreatePlayerPrefab(Material hazmat, Material visor, Material rubber, Material gear, Material ink)
        {
            var player = new GameObject("Player");

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.3f;
            controller.slopeLimit = 45f;
            controller.skinWidth = 0.03f;

            var motor = player.AddComponent<PlayerController>();
            motor.enabled = false;

            var stepSource = player.AddComponent<AudioSource>();
            stepSource.playOnAwake = false;
            var footsteps = player.AddComponent<Footsteps>();
            footsteps.controller = motor;

            var pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(player.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            pivot.tag = "MainCamera";
            motor.cameraPivot = pivot.transform;

            var camera = pivot.AddComponent<Camera>();
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.05f;
            // Em quase todo mapa há um corredor reto de 80 m (medido em 40 seeds): menos que isso
            // abriria um buraco no fim dele.
            camera.farClipPlane = 85f;
            camera.enabled = false;
            var listener = pivot.AddComponent<AudioListener>();
            // Apanha o som que sai para as caixas, para a gravação em vídeo.
            pivot.AddComponent<RecorderAudioTap>();
            listener.enabled = false;

            var cameraData = GetOrAdd<HDAdditionalCameraData>(pivot);
            cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;

            var torch = new GameObject("Lanterna");
            torch.transform.SetParent(player.transform, false);
            torch.transform.localPosition = new Vector3(0.18f, 1.5f, 0.1f);

            var torchLight = torch.AddComponent<Light>();
            torchLight.type = LightType.Spot;
            var torchData = GetOrAdd<HDAdditionalLightData>(torch);
            HDAdditionalLightData.InitDefaultHDAdditionalLightData(torchData);

            torchLight.lightUnit = LightUnit.Candela;
            torchLight.intensity = 2000f;
            torchLight.range = 25f;
            torchLight.spotAngle = 55f;
            torchLight.innerSpotAngle = 28f;
            torchLight.color = Color.white;
            torchLight.useColorTemperature = true;
            torchLight.colorTemperature = 5600f;
            torchLight.shadows = LightShadows.Soft;
            torchLight.enabled = false;

            var flashlight = torch.AddComponent<Flashlight>();
            flashlight.follow = pivot.transform;
            flashlight.inputEnabled = false;

            // Corpo visto pelos outros: explorador em traje de proteção. As peças são montadas
            // em código quando o jogador aparece (PlayerBody); o prefab leva só os materiais.
            var body = new GameObject("Corpo");
            body.transform.SetParent(player.transform, false);
            var figure = body.AddComponent<PlayerBody>();
            figure.suitMaterial = hazmat;
            figure.rubberMaterial = rubber;
            figure.visorMaterial = visor;
            figure.gearMaterial = gear;

            // Rede: o dono manda a própria posição (resposta imediata no controle).
            player.AddComponent<NetworkObject>();

            var networkTransform = player.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleZ = false;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;
            networkTransform.Interpolate = true;

            var network = player.AddComponent<PlayerNetwork>();
            network.controller = motor;
            network.flashlight = flashlight;
            network.footsteps = footsteps;
            network.playerCamera = camera;
            network.listener = listener;
            network.cameraPivot = pivot.transform;
            network.body = body;
            network.figure = figure;

            // Mão em primeira pessoa, com a caneta: fica desligada até o jogador pegar o item.
            // Também é montada em código (ItemHand). Só o dono vê.
            var handObject = new GameObject("MaoComItem");
            handObject.transform.SetParent(pivot.transform, false);
            handObject.transform.localPosition = new Vector3(0.15f, -0.165f, 0.4f);
            handObject.transform.localRotation = Quaternion.Euler(-4f, -8f, 4f);

            var itemHand = handObject.AddComponent<ItemHand>();
            itemHand.suitMaterial = hazmat;
            itemHand.gloveMaterial = rubber;
            itemHand.penMaterial = ink;
            handObject.SetActive(false);

            var pen = player.AddComponent<PenTool>();
            pen.aim = pivot.transform;
            pen.viewModel = handObject;
            pen.hand = itemHand;
            itemHand.pen = pen;
            network.pen = pen;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            Object.DestroyImmediate(player);
            StampNetworkId(prefab);
            return prefab;
        }

        // ---------------------------------------------------------------- Entidade

        // O prefab leva só os componentes: o corpo é montado em código quando ela aparece
        // (EntityBody) e o som é sintetizado (EntityAudio). O colisor impede o jogador de
        // atravessá-la; o corpo rígido cinemático faz a caneta não riscar nela.
        static GameObject CreateEntityPrefab(Material body, Material shell, Material bone, Material maw)
        {
            var entity = new GameObject("Entity");

            var capsule = entity.AddComponent<CapsuleCollider>();
            capsule.radius = 0.4f;
            capsule.height = 3.2f;
            capsule.center = new Vector3(0f, 1.6f, 0f);

            var rigid = entity.AddComponent<Rigidbody>();
            rigid.isKinematic = true;
            rigid.useGravity = false;

            var figure = entity.AddComponent<EntityBody>();
            figure.bodyMaterial = body;
            figure.shellMaterial = shell;
            figure.boneMaterial = bone;
            figure.mawMaterial = maw;
            // O corpo esculpido no Blender (CharacterModels), se já foi exportado.
            figure.model = CharacterModels.Model("Entidade");
            figure.modelMaterials = CharacterModels.Materials("Entidade");
            entity.AddComponent<EntityAudio>();

            // Rede: quem manda na posição é o host (o padrão do NetworkTransform).
            entity.AddComponent<NetworkObject>();

            var networkTransform = entity.AddComponent<NetworkTransform>();
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleZ = false;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;
            networkTransform.Interpolate = true;

            entity.AddComponent<EntityBrain>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(entity, EntityPrefabPath);
            Object.DestroyImmediate(entity);
            StampNetworkId(prefab);
            return prefab;
        }

        // O Netcode identifica cada prefab por um número calculado a partir do asset. Ele só
        // é calculado quando o editor valida o componente; num prefab criado por código isso
        // ainda não aconteceu, então o número ficaria 0 no arquivo. Aqui a validação é
        // forçada e o resultado é gravado.
        public static void StampNetworkId(GameObject prefab)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

            var networkObject = prefab.GetComponent<NetworkObject>();
            var validate = typeof(NetworkObject).GetMethod("OnValidate", flags);
            var hashField = typeof(NetworkObject).GetField("GlobalObjectIdHash", flags);

            if (validate != null) validate.Invoke(networkObject, null);

            EditorUtility.SetDirty(networkObject);
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);

            if (hashField != null && (uint)hashField.GetValue(networkObject) == 0)
                Debug.LogWarning("[BackRooms] O prefab " + prefab.name + " ficou sem identificador de rede. Abra o prefab no editor e salve para o Netcode gerar.");
        }

        // ---------------------------------------------------------------- Volume

        static VolumeProfile EnsureVolumeProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (existing != null) return existing;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            // Céu em degradê só para dar luz ambiente: simula o rebatimento amarelo das
            // superfícies, já que um mapa procedural não tem lightmap. Valores em nits.
            var environment = Add<VisualEnvironment>(profile);
            environment.skyType.Override((int)SkyType.Gradient);
            environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

            var sky = Add<GradientSky>(profile);
            sky.top.Override(new Color(2.4f, 2.3f, 1.3f));
            sky.middle.Override(new Color(3.6f, 3.5f, 2.0f));
            sky.bottom.Override(new Color(7.0f, 6.8f, 3.8f));
            sky.gradientDiffusion.Override(1f);
            sky.exposure.Override(0f);
            sky.multiplier.Override(1f);
            // Desligado em 12/11/2026 (ajuste feito à mão no editor e trazido para cá): a fase
            // fica só com a luz das luminárias, e a exposição subiu para compensar.
            sky.active = false;

            // Exposição fixa: a automática clarearia as áreas escuras.
            var exposure = Add<Exposure>(profile);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(6.5460577f);

            var fog = Add<Fog>(profile);
            fog.enabled.Override(true);
            fog.colorMode.Override(FogColorMode.ConstantColor);
            fog.color.Override(new Color(0.6f, 0.54f, 0.27f));
            fog.meanFreePath.Override(60f);
            fog.baseHeight.Override(50f);
            fog.maximumHeight.Override(100f);
            fog.enableVolumetricFog.Override(true);
            fog.albedo.Override(new Color(0.9f, 0.85f, 0.6f));
            fog.anisotropy.Override(0.2f);
            // Valor do ajuste à mão de 12/11/2026, sem override: não vale enquanto a qualidade da
            // névoa vier do perfil do HDRP. Só para o arquivo gerado sair igual ao commitado.
            fog.volumetricFogBudget = 0.5f;

            var occlusion = Add<ScreenSpaceAmbientOcclusion>(profile);
            occlusion.intensity.Override(1.2f);
            occlusion.radius.Override(1.5f);
            // Desligada em 12/11/2026, pelo mesmo ajuste à mão: o quadro caiu de 27 para cerca de
            // 20 ms junto com o céu desligado (Docs/desempenho-level0.md).
            occlusion.active = false;
            // Também do ajuste à mão, sem override e só da oclusão por traçado de raios (que o
            // jogo não usa): sem efeito, repetidos para o arquivo gerado sair igual.
            occlusion.rayLength = 3f;
            occlusion.sampleCount = 2;
            occlusion.denoiserRadius = 0.5f;

            // Pós de filmadora.
            var bloom = Add<Bloom>(profile);
            bloom.intensity.Override(0.2f);
            bloom.scatter.Override(0.7f);

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
            color.contrast.Override(8f);
            color.saturation.Override(-12f);
            color.colorFilter.Override(new Color(0.97f, 1f, 0.95f));

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(false);
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        // ---------------------------------------------------------------- Materiais

        static Material EnsureMaterial(string name, Color baseColor, Texture2D albedo, Texture2D normal, float smoothness,
                                       Color emissive, Texture2D emissiveMap = null)
        {
            string path = ArtFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                Debug.LogError("[BackRooms] Shader HDRP/Lit não encontrado. O projeto está mesmo em HDRP?");
                return null;
            }

            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);

            if (albedo != null) material.SetTexture("_BaseColorMap", albedo);
            if (normal != null)
            {
                material.SetTexture("_NormalMap", normal);
                material.SetFloat("_NormalScale", 1f);
            }

            // No HDRP a cor emissiva é em nits.
            if (emissive.maxColorComponent > 0f) material.SetColor("_EmissiveColor", emissive);
            if (emissiveMap != null) material.SetTexture("_EmissiveColorMap", emissiveMap);

            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Material EnsureDecal(string name, Texture2D texture, float smoothness)
        {
            return EnsureDecal(name, texture, smoothness, Color.white, 1f, true, 0);
        }

        // Material com cor e relevo pintados em código: grava as duas texturas em PNG (a de
        // relevo já como mapa de normais) e monta o material com elas.
        static Material EnsureSurface(string name, Color baseColor, float smoothness, float normalScale, int size,
                                      Func<Color32[]> paintColor, Func<Color32[]> paintNormal)
        {
            Texture2D albedo = EnsurePixels(name + "_Albedo", size, paintColor, false);
            Texture2D normal = EnsurePixels(name + "_Normal", size, paintNormal, true);

            Material material = EnsureMaterial(name, baseColor, albedo, normal, smoothness, Color.black);
            if (material == null) return null;

            material.SetFloat("_NormalScale", normalScale);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D EnsurePixels(string name, int size, Func<Color32[]> paint, bool normalMap)
        {
            string path = ArtFolder + "/" + name + ".png";

            if (!File.Exists(path))
            {
                WritePng(path, size, size, paint());

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 8;
                if (normalMap) importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Material sem iluminação: a cor é o próprio brilho. Transparente, mistura pelo alfa
        // da textura; a cor e a opacidade de cada objeto podem vir de um MaterialPropertyBlock.
        static Material EnsureUnlit(string name, Color color, Texture2D texture, bool transparent)
        {
            string path = ArtFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
            {
                Debug.LogError("[BackRooms] Shader HDRP/Unlit não encontrado.");
                return null;
            }

            var material = new Material(shader) { name = name };
            material.SetColor("_UnlitColor", color);
            if (texture != null) material.SetTexture("_UnlitColorMap", texture);

            if (transparent)
            {
                material.SetFloat("_SurfaceType", 1f);
                material.SetFloat("_BlendMode", 0f);
                material.SetFloat("_ZWrite", 0f);
            }

            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // Tinta da caneta: fosca e sem receber decalques, para as manchas de sujeira
        // não passarem por cima do que os jogadores riscam.
        static Material EnsureInk(string name, Color color)
        {
            Material material = EnsureMaterial(name, color, null, null, 0.3f, Color.black);
            if (material == null) return null;

            material.SetFloat("_SupportDecals", 0f);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        // Decalque que só pinta cor (e, se pedido, brilho de molhado) por cima da superfície,
        // sem mexer no relevo.
        static Material EnsureDecal(string name, Texture2D texture, float smoothness, Color tint, float blend,
                                    bool affectSmoothness, int drawOrder)
        {
            string path = ArtFolder + "/Decal_" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("HDRP/Decal");
            if (shader == null)
            {
                Debug.LogError("[BackRooms] Shader HDRP/Decal não encontrado.");
                return null;
            }

            var material = new Material(shader) { name = "Decal_" + name };
            material.SetColor("_BaseColor", tint);
            material.SetTexture("_BaseColorMap", texture);
            material.SetFloat("_DecalBlend", blend);
            material.SetFloat("_AffectAlbedo", 1f);
            material.SetFloat("_AffectNormal", 0f);
            material.SetFloat("_AffectMetal", 0f);
            material.SetFloat("_AffectAO", 0f);
            material.SetFloat("_AffectSmoothness", affectSmoothness ? 1f : 0f);
            material.SetFloat("_Smoothness", smoothness);
            // A opacidade do brilho vem do contorno da mancha (alfa da cor). O padrão do HDRP a
            // tira do mapa de máscara; sem esse mapa ela vale 1 no retângulo inteiro do projetor,
            // e aparecia um quadrado liso e refletindo em volta de cada mancha.
            material.SetFloat("_MaskBlendSrc", 0f);
            material.SetInt("_DrawOrder", drawOrder);

            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ---------------------------------------------------------------- Texturas de superfície

        delegate Color32 Painter(int x, int y, int size, System.Random rng);

        // Texturas geradas por IA (Higgsfield), já emendáveis, com o mesmo nome da pintada em
        // código. Ficam fora de Art/Level0 para o "Recriar tudo" não apagá-las. Sem o arquivo,
        // volta a pintura em código.
        const string TextureSourceFolder = ImportedModels.SourceFolder + "/Texturas";

        static Texture2D EnsureTexture(string name, int size, Painter painter, bool normalFromHeight)
        {
            string source = TextureSourceFolder + "/" + name + ".png";
            if (File.Exists(source))
            {
                var sourceImporter = (TextureImporter)AssetImporter.GetAtPath(source);
                bool changed = sourceImporter.wrapMode != TextureWrapMode.Repeat || sourceImporter.anisoLevel != 8 ||
                               (normalFromHeight && sourceImporter.textureType != TextureImporterType.NormalMap);
                if (changed)
                {
                    sourceImporter.wrapMode = TextureWrapMode.Repeat;
                    sourceImporter.anisoLevel = 8;
                    if (normalFromHeight)
                    {
                        // O arquivo é um mapa de altura; o importador o converte em normais.
                        sourceImporter.textureType = TextureImporterType.NormalMap;
                        sourceImporter.convertToNormalmap = true;
                        sourceImporter.heightmapScale = 0.03f;
                    }
                    sourceImporter.SaveAndReimport();
                }

                return AssetDatabase.LoadAssetAtPath<Texture2D>(source);
            }

            string path = ArtFolder + "/" + name + ".png";

            if (!File.Exists(path))
            {
                var rng = new System.Random(name.Length * 7919 + size);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        pixels[y * size + x] = painter(x, y, size, rng);

                WritePng(path, size, size, pixels);

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 8;
                if (normalFromHeight)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.convertToNormalmap = true;
                    importer.heightmapScale = 0.03f;
                }
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void WritePng(string path, int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        // Variação suave que fecha nas bordas (usa só senos com período inteiro).
        static float Blotch(int x, int y, int size)
        {
            float u = x / (float)size * Mathf.PI * 2f;
            float v = y / (float)size * Mathf.PI * 2f;
            return (Mathf.Sin(u + 1.3f) * Mathf.Sin(v * 2f + 0.4f) + Mathf.Sin(u * 3f + v + 2.1f) * 0.5f) / 1.5f;
        }

        static float Noise(System.Random rng)
        {
            return (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        static float Range(System.Random rng, float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }

        // Fumaça: branca, com o alfa em nuvens largas e fiapos. Fecha nas bordas, para deslizar sem emenda.
        static Color32 PaintSmoke(int x, int y, int size, System.Random rng)
        {
            float u = x / (float)size;
            float v = y / (float)size;
            float clouds = StainPainter.TileableFbm(u, v, 2, 2, 91);
            float wisps = StainPainter.TileableFbm(u, v, 6, 6, 57, 3);
            float density = Mathf.SmoothStep(0.28f, 0.72f, clouds * 0.75f + wisps * 0.25f);

            return new Color32(255, 255, 255, (byte)Mathf.Clamp(40f + density * 215f, 0f, 255f));
        }

        // Concreto dos buracos: cinza com manchas largas de umidade e granulado fino. Não pode
        // ser escuro demais: é a parede do poço clareando perto da borda e sumindo no fundo
        // que faz o buraco parecer fundo, e não um tapete preto.
        static Color32 PaintConcrete(int x, int y, int size, System.Random rng)
        {
            var baseColor = new Color32(160, 155, 146, 255);
            float u = x / (float)size;
            float v = y / (float)size;
            float damp = StainPainter.TileableFbm(u, v, 3, 3, 29);
            float grain = StainPainter.TileableFbm(u, v, 24, 24, 41, 2);

            return Shade(baseColor, Noise(rng) * 0.06f + (damp - 0.5f) * 0.45f + (grain - 0.5f) * 0.15f);
        }

        static Color32 Shade(Color32 c, float amount)
        {
            float f = 1f + amount;
            return new Color32(
                (byte)Mathf.Clamp(c.r * f, 0f, 255f),
                (byte)Mathf.Clamp(c.g * f, 0f, 255f),
                (byte)Mathf.Clamp(c.b * f, 0f, 255f), 255);
        }

        static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        // Papel de parede: losangos vazados em fileiras alternadas, 8 repetições por metro.
        static Color32 PaintWallpaper(int x, int y, int size, System.Random rng)
        {
            return WallpaperPixel(x, y, size, rng, true);
        }

        // Papel de parede da sala inicial: o mesmo desenho, sem nenhum encardido.
        static Color32 PaintWallpaperClean(int x, int y, int size, System.Random rng)
        {
            return WallpaperPixel(x, y, size, rng, false);
        }

        static Color32 WallpaperPixel(int x, int y, int size, System.Random rng, bool grime)
        {
            var baseColor = new Color32(200, 181, 96, 255);
            var patternColor = new Color32(183, 164, 80, 255);

            const int cell = 64;
            int row = y / cell;
            int u = (x + (row % 2 == 1 ? cell / 2 : 0)) % cell;
            int v = y % cell;

            int diamond = Mathf.Abs(u - cell / 2) + Mathf.Abs(v - cell / 2);
            bool motif = (diamond < 16 && diamond >= 10) || diamond < 4;
            bool stripe = u == 0 || u == cell - 1;

            Color32 c = motif ? patternColor : baseColor;
            float shade = Noise(rng) * 0.03f + Blotch(x, y, size) * 0.02f;
            if (stripe) shade -= 0.03f;
            if (!grime) return Shade(c, shade);

            // Encardido embutido no papel: nuvens largas de sujeira e veios verticais de
            // umidade antiga. Os dois ruídos fecham nas bordas da textura.
            float tu = x / (float)size;
            float tv = y / (float)size;
            float clouds = StainPainter.TileableFbm(tu, tv, 3, 3, 11);
            float veins = StainPainter.TileableFbm(tu, tv, 22, 2, 23);
            float speckle = StainPainter.TileableFbm(tu, tv, 40, 40, 37, 2);

            shade -= Mathf.Max(0f, clouds - 0.48f) * 0.16f;
            shade -= Mathf.Max(0f, veins - 0.56f) * 0.12f;
            shade -= Mathf.Max(0f, speckle - 0.7f) * 0.12f;

            return Shade(c, shade);
        }

        static Color32 PaintCarpet(int x, int y, int size, System.Random rng)
        {
            return CarpetPixel(x, y, size, rng, true);
        }

        // Carpete da sala inicial: só a fibra, sem manchas.
        static Color32 PaintCarpetClean(int x, int y, int size, System.Random rng)
        {
            return CarpetPixel(x, y, size, rng, false);
        }

        static Color32 CarpetPixel(int x, int y, int size, System.Random rng, bool grime)
        {
            var baseColor = new Color32(166, 149, 90, 255);
            // Fibra: ruído fino. A variação larga é fraca de propósito — quem dá as
            // diferenças de tom no chão são os decalques de umidade e desgaste.
            float shade = Noise(rng) * 0.16f + Blotch(x, y, size) * 0.03f;
            if (!grime) return Shade(baseColor, shade);

            // Carpete velho: manchas escuras largas, pontos de sujeira e trechos pisados
            // mais claros. Tudo fecha nas bordas da textura.
            float u = x / (float)size;
            float v = y / (float)size;
            float dirt = StainPainter.TileableFbm(u, v, 4, 4, 51);
            float spots = StainPainter.TileableFbm(u, v, 18, 18, 67, 3);
            float worn = StainPainter.TileableFbm(u, v, 3, 5, 83);

            shade -= Mathf.Max(0f, dirt - 0.47f) * 0.18f;
            shade -= Mathf.Max(0f, spots - 0.66f) * 0.18f;
            shade += Mathf.Max(0f, worn - 0.58f) * 0.1f;

            return Shade(baseColor, shade);
        }

        static Color32 PaintCarpetHeight(int x, int y, int size, System.Random rng)
        {
            byte h = (byte)(128 + Noise(rng) * 110f);
            return new Color32(h, h, h, 255);
        }

        // Placa de forro de 0,6 m: perfil metálico claro nas bordas e furinhos no miolo.
        static Color32 PaintCeiling(int x, int y, int size, System.Random rng)
        {
            var tile = new Color32(207, 199, 154, 255);
            var bar = new Color32(226, 221, 188, 255);

            // As duas texturas do forro usam a mesma seed e consomem o gerador na mesma
            // ordem, para os furinhos da cor coincidirem com os do relevo.
            int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
            float roll = (float)rng.NextDouble();
            float noise = Noise(rng);

            if (edge < 4) return Shade(bar, noise * 0.015f);
            if (edge == 4) return Shade(tile, -0.25f);
            if (roll < 0.035f) return Shade(tile, -0.22f);
            return Shade(tile, noise * 0.03f);
        }

        static Color32 PaintCeilingHeight(int x, int y, int size, System.Random rng)
        {
            int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
            float roll = (float)rng.NextDouble();
            float noise = Noise(rng);

            byte h;
            if (edge < 4) h = 235;
            else if (edge == 4) h = 90;
            else if (roll < 0.035f) h = 60;
            else h = (byte)(150 + noise * 14f);

            return new Color32(h, h, h, 255);
        }

        // Leva o alfa a zero perto das bordas (rampa suave de <paramref name="margin"/> pixels).
        static Color32[] FadeBorders(Color32[] pixels, int width, int height, int margin)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int edge = Mathf.Min(Mathf.Min(x, width - 1 - x), Mathf.Min(y, height - 1 - y));
                    if (edge >= margin) continue;
                    float t = Mathf.SmoothStep(0f, 1f, edge / (float)margin);
                    int i = y * width + x;
                    pixels[i].a = (byte)Mathf.RoundToInt(pixels[i].a * t);
                }
            }
            return pixels;
        }

        // ---------------------------------------------------------------- Difusor das luminárias

        // U atravessa o painel (0,6 m), V corre ao longo dele (1,2 m). Duas lâmpadas tubulares
        // a 30% e 70% da largura, pontas mais escuras, borda sombreada pela calha e o
        // quadriculado fino do acrílico prismático. Aceso vira mapa de emissão; apagado, a cor.
        static Color32[] PaintDiffuser(bool lit)
        {
            const int width = 128, height = 256;
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float tube = Mathf.Exp(-Mathf.Pow((u - 0.3f) / 0.085f, 2f)) + Mathf.Exp(-Mathf.Pow((u - 0.7f) / 0.085f, 2f));
                    // As lâmpadas não chegam às pontas do painel.
                    float along = Mathf.Clamp01(Mathf.Min(v, 1f - v) / 0.06f);
                    tube *= along;

                    float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(u, 1f - u) * 0.6f, Mathf.Min(v, 1f - v) * 1.2f) / 0.025f);
                    float prism = (x + y) % 4 == 0 || (x - y + 1024) % 4 == 0 ? 0.94f : 1f;

                    float value = lit
                        ? (0.5f + 0.5f * Mathf.Clamp01(tube)) * Mathf.Lerp(0.7f, 1f, edge)
                        : (0.95f - 0.22f * Mathf.Clamp01(tube)) * Mathf.Lerp(0.8f, 1f, edge);
                    value *= prism;

                    byte c = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
                    pixels[y * width + x] = new Color32(c, c, c, 255);
                }
            }

            return pixels;
        }

        // ---------------------------------------------------------------- Pinturas (decalques)

        static Texture2D EnsurePainting(string name, int width, int height, bool transparent, Func<System.Random, Color32[]> paint)
        {
            string path = ArtFolder + "/" + name + ".png";

            if (!File.Exists(path))
            {
                int seed = 17;
                foreach (char c in name) seed = seed * 31 + c;

                WritePng(path, width, height, paint(new System.Random(seed)));

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = transparent;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static readonly Color32 BlackBlood = new Color32(9, 6, 7, 255);
        static readonly Color32 BlackPaint = new Color32(14, 12, 11, 255);

        // Sangue preto: mancha de impacto no alto, respingos em volta e gotas escorrendo.
        static Color32[] PaintBlood(System.Random rng, int variant)
        {
            var canvas = new PaintCanvas(512, 512);
            float cx = 256f + Range(rng, -30f, 30f);
            float cy = 372f + Range(rng, -20f, 20f);

            // Miolo da mancha: poucos discos grandes.
            int blobs = 14 + rng.Next(8);
            for (int i = 0; i < blobs; i++)
                canvas.Disc(cx + Range(rng, -95f, 95f), cy + Range(rng, -55f, 55f), Range(rng, 22f, 58f));

            // Borda irregular: muitos discos pequenos em volta, para não parecer uma nuvem.
            int fringe = 70 + rng.Next(40);
            for (int i = 0; i < fringe; i++)
            {
                float angle = Range(rng, 0f, Mathf.PI * 2f);
                float distance = Range(rng, 70f, 150f);
                canvas.Disc(cx + Mathf.Cos(angle) * distance, cy + Mathf.Sin(angle) * distance * 0.6f, Range(rng, 4f, 17f));
            }

            // Respingos de impacto: riscos curtos saindo do centro.
            int streaks = 10 + rng.Next(8);
            for (int i = 0; i < streaks; i++)
            {
                float angle = Range(rng, 0f, Mathf.PI * 2f);
                float near = Range(rng, 90f, 140f);
                float far = near + Range(rng, 25f, 90f);
                canvas.Stroke(cx + Mathf.Cos(angle) * near, cy + Mathf.Sin(angle) * near * 0.7f,
                              cx + Mathf.Cos(angle) * far, cy + Mathf.Sin(angle) * far * 0.7f,
                              Range(rng, 5f, 9f), Range(rng, 1.5f, 3f), rng, 4f);
            }

            int droplets = 90 + rng.Next(50);
            for (int i = 0; i < droplets; i++)
            {
                float angle = Range(rng, 0f, Mathf.PI * 2f);
                float distance = 90f + Mathf.Pow((float)rng.NextDouble(), 1.6f) * 160f;
                canvas.Disc(cx + Mathf.Cos(angle) * distance, cy + Mathf.Sin(angle) * distance * 0.7f, Range(rng, 1.4f, 7f));
            }

            int drips = 8 + rng.Next(6);
            for (int i = 0; i < drips; i++)
            {
                float x = cx + Range(rng, -120f, 120f);
                float y = cy - Range(rng, 10f, 60f);
                canvas.Drip(x, y, Range(rng, 90f, 400f), Range(rng, 8f, 15f), Range(rng, 3f, 6f), rng);
            }

            if (variant == 2)
            {
                // Arrastado: como se uma mão tivesse escorregado pela parede.
                for (int i = 0; i < 5; i++)
                {
                    float y0 = cy - 20f + i * 13f + Range(rng, -4f, 4f);
                    canvas.Stroke(cx - 20f, y0, cx + Range(rng, 150f, 215f), y0 - Range(rng, 60f, 130f),
                                  Range(rng, 9f, 15f), Range(rng, 2f, 5f), rng, 10f, 0.8f);
                }
            }

            // Escorridos longos chegam à beirada da textura: apaga as bordas para o decalque não
            // terminar num corte reto.
            return FadeBorders(canvas.ToAlphaPixels(BlackBlood, rng, 0.12f), 512, 512, 24);
        }

        static Color32[] PaintPool(System.Random rng)
        {
            var canvas = new PaintCanvas(512, 512);

            int blobs = 34 + rng.Next(14);
            for (int i = 0; i < blobs; i++)
            {
                float angle = Range(rng, 0f, Mathf.PI * 2f);
                float distance = Mathf.Sqrt((float)rng.NextDouble()) * 118f;
                canvas.Disc(256f + Mathf.Cos(angle) * distance, 256f + Mathf.Sin(angle) * distance * 0.8f, Range(rng, 22f, 66f));
            }

            int droplets = 36 + rng.Next(20);
            for (int i = 0; i < droplets; i++)
            {
                float angle = Range(rng, 0f, Mathf.PI * 2f);
                float distance = Range(rng, 150f, 240f);
                canvas.Disc(256f + Mathf.Cos(angle) * distance, 256f + Mathf.Sin(angle) * distance, Range(rng, 2f, 9f));
            }

            return canvas.ToAlphaPixels(BlackBlood, rng, 0.08f);
        }

        // Palavra escrita com o dedo: letras tortas e algumas gotas escorrendo da base.
        static Color32[] PaintPhrase(System.Random rng, string phrase)
        {
            var canvas = new PaintCanvas(1024, 384);

            float letterHeight = 205f;
            float maxWidth = canvas.Width - 70f;
            if (StrokeFont.MeasureWidth(phrase, letterHeight) > maxWidth)
                letterHeight *= maxWidth / StrokeFont.MeasureWidth(phrase, letterHeight);

            float textWidth = StrokeFont.MeasureWidth(phrase, letterHeight);
            float x = (canvas.Width - textWidth) * 0.5f;
            float y = canvas.Height - letterHeight - 34f;
            float stroke = Mathf.Max(11f, letterHeight * 0.11f);

            StrokeFont.Draw(canvas, phrase, x, y, letterHeight, stroke, rng, 1f);

            int drips = 3 + rng.Next(4);
            for (int i = 0; i < drips; i++)
                canvas.Drip(x + Range(rng, 0f, textWidth), y + Range(rng, 0f, letterHeight * 0.3f),
                            Range(rng, 40f, 130f), stroke * 0.6f, stroke * 0.3f, rng);

            return canvas.ToAlphaPixels(BlackPaint, rng, 0.18f);
        }

        // Seta rabiscada apontando para a direita, passada duas vezes como quem reforça o traço.
        static Color32[] PaintArrow(System.Random rng)
        {
            var canvas = new PaintCanvas(512, 256);

            for (int pass = 0; pass < 2; pass++)
            {
                float lift = Range(rng, -5f, 5f);
                canvas.Stroke(46f, 128f + lift, 462f, 128f - lift, 20f, 16f, rng, 12f);
                canvas.Stroke(462f, 128f - lift, 352f + Range(rng, -10f, 10f), 206f + Range(rng, -8f, 8f), 18f, 12f, rng, 9f);
                canvas.Stroke(462f, 128f - lift, 352f + Range(rng, -10f, 10f), 50f + Range(rng, -8f, 8f), 18f, 12f, rng, 9f);
            }

            return canvas.ToAlphaPixels(BlackPaint, rng, 0.18f);
        }

        // Placa de saída: letras vermelhas limpas sobre fundo quase preto.
        static Color32[] PaintExitSign(System.Random rng)
        {
            var canvas = new PaintCanvas(256, 96);

            const float letterHeight = 58f;
            float width = StrokeFont.MeasureWidth("EXIT", letterHeight);
            StrokeFont.Draw(canvas, "EXIT", (canvas.Width - width) * 0.5f, (canvas.Height - letterHeight) * 0.5f,
                            letterHeight, 9f, rng, 0f);

            return canvas.ToOpaquePixels(new Color32(14, 2, 2, 255), new Color32(255, 46, 34, 255));
        }

        // ---------------------------------------------------------------- Utilidades

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // Põe a cena na lista da build. O Level 0 fica sempre em primeiro (é a cena que a build
        // abre); as outras fases (o Level 1 de teste) entram depois dele.
        static void AddSceneToBuild(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(entry => entry.path == path);

            var scene = new EditorBuildSettingsScene(path, true);
            if (path == ScenePath) scenes.Insert(0, scene);
            else scenes.Add(scene);

            // Corrige listas antigas em que outra fase tinha ficado na frente do Level 0.
            int level0 = scenes.FindIndex(entry => entry.path == ScenePath);
            if (level0 > 0)
            {
                EditorBuildSettingsScene first = scenes[level0];
                scenes.RemoveAt(level0);
                scenes.Insert(0, first);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
