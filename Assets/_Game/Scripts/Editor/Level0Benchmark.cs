using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using BackRooms.Entities;
using BackRooms.Net;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Debug = UnityEngine.Debug;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Mede quanto custa desenhar o Level 0 em várias configurações de luz, para decidir o que
    /// cortar com número na mão e não por palpite.
    ///
    /// Roda dentro do Play, com a sessão aberta e a câmera do próprio jogador: fora do Play o
    /// HDRP trata as sombras em cache das luminárias de outro jeito e a medida não vale (uma
    /// tentativa assim deu quadros de 1 a 30 segundos, crescendo sem parar).
    ///
    /// A câmera anda pelo caminho seguro inteiro, meio metro por quadro, olhando um pouco para
    /// os lados, como quem corre pela fase. Cada quadro é desenhado em 1920 x 1080 e só é dado
    /// por terminado quando a placa de vídeo acaba (lê-se um pixel de volta), então o tempo
    /// soma processador e placa. Num jogo de verdade os dois trabalham em paralelo e o quadro
    /// sai mais rápido: os números servem para comparar uma configuração com a outra, não como
    /// taxa de quadros final.
    ///
    /// A primeira passada é medida à parte: é nela que cada luminária calcula a própria sombra,
    /// ao entrar no alcance pela primeira vez. As outras já encontram as sombras prontas.
    ///
    /// Menu BackRooms > Medir desempenho do Level 0 (ou do Level 2). Resultado em Logs/Desempenho.
    /// Linha de comando: -benchScene &lt;caminho da cena&gt; mede outra fase; nas Piscinas a
    /// caminhada segue o caminho a pé e as configurações testam o que é próprio delas.
    /// </summary>
    public static class Level0Benchmark
    {
        const string ScenePath = "Assets/_Game/Scenes/Level0_Test.unity";
        const string RunKey = "BackRooms.Benchmark.Run";
        const string BatchKey = "BackRooms.Benchmark.Batch";
        const string FolderKey = "BackRooms.Benchmark.Folder";
        const string SceneKey = "BackRooms.Benchmark.Scene";

        const int Width = 1920;
        const int Height = 1080;
        const float NearRadius = 20f;
        const float StepMeters = 0.5f;
        // -benchParts mede mais configurações, cada uma por menos tempo (o mesmo trecho do caminho).
        static double SecondsPerSetup = 150.0;
        static double Timeout = 1500.0;

        sealed class Setup
        {
            public string Name;
            public string Note;
            public bool Torch;
            /// <summary>Prepara a configuração. Devolve o que desfaz tudo.</summary>
            public Func<Action> Apply;
            /// <summary>Chamado a cada quadro, antes de desenhar. Devolve verdadeiro se trocou alguma luz.</summary>
            public Func<Vector3, bool> PerFrame;
        }

        sealed class Result
        {
            public string Name;
            public string Note;
            public readonly List<double> Frames = new List<double>();
            public readonly List<double> SwitchFrames = new List<double>();
            public int LitMin = int.MaxValue, LitMax;
            public bool CutShort;
        }

        static int _stage;
        static double _began;
        static string _folder;

        static PlayerNetwork _player;
        static Camera _camera;
        static Light _torch;
        static RenderTexture _target;
        static Texture2D _pixel;
        static Texture2D _photo;

        static readonly List<Light> Lamps = new List<Light>();
        static readonly List<HDAdditionalLightData> LampData = new List<HDAdditionalLightData>();
        static DecalProjector[] _decals;
        static List<Pose> _walk;
        static List<Setup> _setups;
        static readonly List<Result> Results = new List<Result>();

        static int _setupIndex;
        static int _frameIndex;
        static Result _current;
        static Action _undo;
        static Stopwatch _clock;
        static int _warm;
        static double _warmSum;

        [MenuItem("BackRooms/Medir desempenho do Level 0")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Start(DefaultFolder(), false, ScenePath);
        }

        [MenuItem("BackRooms/Medir desempenho do Level 2")]
        public static void RunLevel2FromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Start(DefaultFolder(), false, Level2Setup.ScenePath);
        }

        /// <summary>Uso em linha de comando. Aceita -benchOut &lt;pasta&gt;, -benchScene &lt;cena&gt; e -benchQuick (só as medidas de base).</summary>
        public static void BatchRun()
        {
            string folder = DefaultFolder();
            string scene = ScenePath;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-benchOut") folder = args[i + 1];
                if (args[i] == "-benchScene") scene = args[i + 1];
            }

            Start(folder, true, scene);
        }

        static string DefaultFolder()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Desempenho"));
        }

        static void Start(string folder, bool batch, string scene)
        {
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            SessionState.SetString(SceneKey, scene);
            SessionState.SetBool(RunKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(FolderKey, folder);
            EditorApplication.EnterPlaymode();
        }

        // Entrar em Play recarrega o domínio; o pedido sobrevive pelo SessionState.
        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (!SessionState.GetBool(RunKey, false)) return;
            _folder = SessionState.GetString(FolderKey, DefaultFolder());
            EditorApplication.update += Tick;
        }

        static double Now => EditorApplication.timeSinceStartup;

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;

            try
            {
                Step();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(1);
            }
        }

        static void Step()
        {
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = Now;
            if (Now - _began > Timeout)
            {
                Debug.Log("[BackRooms] Desempenho | tempo esgotado na etapa " + _stage);
                WriteReport();
                Finish(2);
                return;
            }

            switch (_stage)
            {
                case 0:
                    // Sem entidade e sem apagão: só a luz normal da fase, sempre a mesma seed.
                    EntityDirector.Paused = true;
                    if (LightsOut.Instance != null) LightsOut.Instance.eventEnabled = false;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    _stage = 1;
                    break;

                case 1:
                    if (session.Phase != SessionPhase.Playing || PlayerNetwork.Local == null) break;
                    if (session.level == null || session.level.Data == null) break;

                    Prepare(session.level);
                    _stage = 2;
                    break;

                case 2:
                    // Aquecimento parado no começo do caminho: os primeiros quadros compilam shaders.
                    PlaceAt(_walk[0]);
                    double frame = Draw();
                    if (_warm >= 10) _warmSum += frame;
                    if (++_warm < 40) break;

                    Debug.Log($"[BackRooms] Desempenho | aquecimento | {_warmSum / 30.0:F1} ms por quadro parado | caminhada de {_walk.Count} quadros | " +
                              $"{Lamps.Count} luminarias | {_decals.Length} marcas");
                    _stage = 3;
                    break;

                case 3:
                    RunSetups();
                    break;
            }
        }

        static void Prepare(LevelBuilder level)
        {
            Directory.CreateDirectory(_folder);

            _player = PlayerNetwork.Local;
            _camera = _player.playerCamera;
            _torch = _player.flashlight.GetComponent<Light>();

            // Quem mexe no jogador e na lanterna agora é a medição.
            _player.controller.enabled = false;
            _player.flashlight.enabled = false;
            _torch.enabled = false;
            _camera.transform.localRotation = Quaternion.identity;

            _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _photo = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            _camera.targetTexture = _target;

            Lamps.Clear();
            LampData.Clear();
            foreach (CeilingLight ceiling in level.GetComponentsInChildren<CeilingLight>())
            {
                foreach (Light light in ceiling.GetComponentsInChildren<Light>())
                {
                    Lamps.Add(light);
                    LampData.Add(light.GetComponent<HDAdditionalLightData>());
                }
            }

            _decals = level.GetComponentsInChildren<DecalProjector>();
            _walk = Walk(level);
            _setups = level.Data.HasPools ? PoolSetups(level) : Setups();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-benchParts") >= 0)
            {
                _setups = PartSetups();
                SecondsPerSetup = 60.0;
                Timeout = 4000.0;
            }

            // -benchQuick: só a primeira passada e as duas medidas de base (sem e com lanterna),
            // para comparar uma fase antes e depois de uma mudança sem esperar as outras.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-benchQuick") >= 0 && _setups.Count > 3)
                _setups.RemoveRange(3, _setups.Count - 3);
        }

        // Um punhado de quadros por passada do editor, até acabar a configuração da vez.
        static void RunSetups()
        {
            if (_current == null)
            {
                if (_setupIndex >= _setups.Count)
                {
                    WriteReport();
                    Finish(0);
                    return;
                }

                Setup next = _setups[_setupIndex];
                _current = new Result { Name = next.Name, Note = next.Note };
                _undo = next.Apply != null ? next.Apply() : null;
                _torch.enabled = next.Torch;
                _frameIndex = 0;

                PlaceAt(_walk[0]);
                if (next.PerFrame != null) next.PerFrame(_walk[0].position);
                for (int i = 0; i < 4; i++) Draw();

                _clock = Stopwatch.StartNew();
            }

            Setup setup = _setups[_setupIndex];

            for (int batch = 0; batch < 6 && _frameIndex < _walk.Count; batch++, _frameIndex++)
            {
                Pose pose = _walk[_frameIndex];
                PlaceAt(pose);
                bool switched = setup.PerFrame != null && setup.PerFrame(pose.position);

                int lit = 0;
                foreach (Light lamp in Lamps) if (lamp.enabled) lit++;
                _current.LitMin = Mathf.Min(_current.LitMin, lit);
                _current.LitMax = Mathf.Max(_current.LitMax, lit);

                double frame = Draw();
                _current.Frames.Add(frame);
                if (switched) _current.SwitchFrames.Add(frame);
            }

            bool late = _clock.Elapsed.TotalSeconds > SecondsPerSetup;
            if (_frameIndex < _walk.Count && !late) return;

            _current.CutShort = _frameIndex < _walk.Count;

            // Uma foto do mesmo lugar em toda configuração, para ver o que muda na imagem.
            Pose photoAt = _walk[_walk.Count / 3];
            PlaceAt(photoAt);
            if (setup.PerFrame != null) setup.PerFrame(photoAt.position);
            for (int i = 0; i < 6; i++) Draw();
            Save(Path.Combine(_folder, FileName(setup.Name) + ".png"));

            _undo?.Invoke();
            _undo = null;
            _torch.enabled = false;

            Result done = _current;
            Results.Add(done);
            _current = null;
            _setupIndex++;

            Debug.Log($"[BackRooms] Desempenho | {done.Name} | {done.Frames.Count} quadros{(done.CutShort ? " (interrompida)" : "")} | media {Mean(done.Frames):F1} ms | " +
                      $"mediana {Percentile(done.Frames, 0.5):F1} | p95 {Percentile(done.Frames, 0.95):F1} | p99 {Percentile(done.Frames, 0.99):F1} | pior {Percentile(done.Frames, 1.0):F0} | " +
                      $"luminarias ligadas {done.LitMin}-{done.LitMax}" +
                      (done.SwitchFrames.Count > 0 ? $" | {done.SwitchFrames.Count} quadros com troca de luz, media {Mean(done.SwitchFrames):F1} ms" : ""));
        }

        // ---------------------------------------------------------------- Configurações

        static List<Setup> Setups()
        {
            var setups = new List<Setup>();

            setups.Add(new Setup
            {
                Name = "0 Primeira passada (sombras sendo calculadas)",
                Note = "Como o jogo está hoje, na primeira vez que se anda pela fase: cada luminária calcula a sombra ao entrar no alcance. Mede os engasgos, não entra na comparação."
            });

            setups.Add(new Setup { Name = "1 Atual, sem lanterna", Note = "Como o jogo está hoje, com as sombras já prontas." });
            setups.Add(new Setup { Name = "2 Atual, com lanterna", Torch = true, Note = "Base de comparação: as outras usam a lanterna." });

            // Hipótese A: só as luminárias a até 20 m do jogador, pelo corte de distância do próprio HDRP.
            setups.Add(new Setup
            {
                Name = "3 Luminarias ate 20 m (corte do HDRP)",
                Torch = true,
                Note = "Todas continuam ligadas; o HDRP deixa de contar as que passam de 20 m.",
                Apply = () => Fade(NearRadius, null, null)
            });

            setups.Add(new Setup
            {
                Name = "4 Como era: luminarias acendendo a nevoa volumetrica",
                Torch = true,
                Note = "Como o jogo era antes de as luminárias saírem da névoa (LevelBuilder.lampsLightFog ligado).",
                Apply = () => Fade(null, true, null)
            });

            setups.Add(new Setup
            {
                Name = "4b Como era: luz ate 70 m, marcas ate 60 m, visao ate 150 m",
                Torch = true,
                Note = "As distancias de antes de 12/11/2026 (hoje: luz 45 m, marcas 25 m, visao 85 m).",
                Apply = () =>
                {
                    Action undo = Fade(70f, null, 60f);
                    float far = _camera.farClipPlane;
                    _camera.farClipPlane = 150f;
                    return () => { undo(); _camera.farClipPlane = far; };
                }
            });

            setups.Add(new Setup
            {
                Name = "5 Atual, sem nevoa volumetrica",
                Torch = true,
                Note = "A névoa volumétrica inteira desligada (é o que o perfil Performant faz).",
                Apply = () =>
                {
                    var go = new GameObject("SemNevoaVolumetrica");
                    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    profile.Add<Fog>(true).enableVolumetricFog.Override(false);

                    var volume = go.AddComponent<Volume>();
                    volume.isGlobal = true;
                    volume.priority = 100f;
                    volume.sharedProfile = profile;

                    return () =>
                    {
                        UnityEngine.Object.Destroy(go);
                        UnityEngine.Object.Destroy(profile);
                    };
                }
            });

            setups.Add(new Setup
            {
                Name = "6 Atual, sem marcas (decalques)",
                Torch = true,
                Note = "Sem sangue, palavras, setas, sujeira e desgaste.",
                Apply = () =>
                {
                    foreach (DecalProjector decal in _decals) decal.enabled = false;
                    return () => { foreach (DecalProjector decal in _decals) decal.enabled = true; };
                }
            });

            setups.Add(new Setup
            {
                Name = "7 Pacote: 20 m + marcas ate 25 m",
                Torch = true,
                Note = "As duas mudanças de poucas linhas que ainda não entraram, juntas.",
                Apply = () => Fade(NearRadius, null, 25f)
            });

            // As duas que desligam luminárias ficam por último: religar uma luminária refaz a sombra dela.
            setups.Add(new Setup
            {
                Name = "8 Luminarias ate 20 m (desligando as outras)",
                Torch = true,
                Note = "As de longe são desligadas e religadas ao chegar perto. Religar refaz a sombra: são os quadros com troca de luz.",
                Apply = () => () => { foreach (Light lamp in Lamps) lamp.enabled = true; },
                PerFrame = eye =>
                {
                    bool switched = false;
                    foreach (Light lamp in Lamps)
                    {
                        bool near = Flat(lamp.transform.position - eye).magnitude <= NearRadius;
                        if (lamp.enabled == near) continue;
                        lamp.enabled = near;
                        switched = true;
                    }
                    return switched;
                }
            });

            // Hipótese B: nenhuma luminária; só a lanterna.
            setups.Add(new Setup
            {
                Name = "9 So a lanterna",
                Torch = true,
                Note = "Nenhuma luminária acesa (os painéis aparecem, mas não iluminam). A luz ambiente do céu continua.",
                Apply = () =>
                {
                    foreach (Light lamp in Lamps) lamp.enabled = false;
                    return () => { foreach (Light lamp in Lamps) lamp.enabled = true; };
                }
            });

            return setups;
        }

        // As Piscinas (Level 2): o que é próprio delas — a lâmina d'água (com refração), a
        // cáustica, os fachos das claraboias na névoa, sombras e alcance das luzes. As luzes que
        // contam aqui são as de CeilingLight: o sol de caixa de cada claraboia e o spot de cada
        // janela (as lâmpadas submersas, sem sombra, ficam de fora).
        static List<Setup> PoolSetups(LevelBuilder level)
        {
            int skies = 0;
            foreach (Light lamp in Lamps) if (lamp.type == LightType.Box) skies++;
            string count = $"{skies} sóis de claraboia e {Lamps.Count - skies} janelas";

            var setups = new List<Setup>
            {
                new Setup { Name = "0 Primeira passada (sombras sendo calculadas)", Note = "Cada claraboia e janela calcula a sombra ao entrar no alcance. Mede os engasgos, não entra na comparação." },
                new Setup { Name = "1 Atual, sem lanterna", Note = $"Como o Level 2 está hoje, com as sombras já prontas ({count})." },
                new Setup { Name = "2 Atual, com lanterna", Torch = true, Note = "Base de comparação." }
            };

            Transform water = level.transform.Find("Agua");
            setups.Add(new Setup
            {
                Name = "3 Sem a lamina d'agua",
                Torch = true,
                Note = "A lâmina transparente do mapa inteiro escondida (some também a refração).",
                Apply = () =>
                {
                    if (water != null) water.gameObject.SetActive(false);
                    return () => { if (water != null) water.gameObject.SetActive(true); };
                }
            });

            MeshRenderer surface = water != null ? water.GetComponent<MeshRenderer>() : null;
            setups.Add(new Setup
            {
                Name = "4 Agua sem refracao",
                Torch = true,
                Note = "A mesma lâmina, com uma cópia do material sem refração (sem a pirâmide de cor da tela).",
                Apply = () =>
                {
                    if (surface == null || surface.sharedMaterial == null) return null;
                    Material original = surface.sharedMaterial;
                    var copy = new Material(original) { name = original.name + " (sem refracao)" };
                    copy.SetFloat("_RefractionModel", 0f);
                    HDMaterial.ValidateMaterial(copy);
                    surface.sharedMaterial = copy;
                    return () => { surface.sharedMaterial = original; UnityEngine.Object.Destroy(copy); };
                }
            });

            setups.Add(new Setup
            {
                Name = "5 Sem fachos na nevoa",
                Torch = true,
                Note = "Os sóis das claraboias não acendem a névoa volumétrica (poolSkyBeams desligado).",
                Apply = () => Fade(null, false, null)
            });

            Transform caustics = level.transform.Find("Causticas");
            setups.Add(new Setup
            {
                Name = "6 Sem caustica",
                Torch = true,
                Note = "A camada de cáustica (uma malha transparente sobre tudo o que está submerso) escondida.",
                Apply = () =>
                {
                    if (caustics != null) caustics.gameObject.SetActive(false);
                    return () => { if (caustics != null) caustics.gameObject.SetActive(true); };
                }
            });

            setups.Add(new Setup
            {
                Name = "7 Claraboias e janelas sem sombra",
                Torch = true,
                Note = "Mede o custo das sombras (a luz vaza pelas paredes).",
                Apply = () =>
                {
                    var shadows = new LightShadows[Lamps.Count];
                    for (int i = 0; i < Lamps.Count; i++) { shadows[i] = Lamps[i].shadows; Lamps[i].shadows = LightShadows.None; }
                    return () => { for (int i = 0; i < Lamps.Count; i++) Lamps[i].shadows = shadows[i]; };
                }
            });

            setups.Add(new Setup
            {
                Name = "8 Luz some a 45 m, sombra a 54 m",
                Torch = true,
                Note = "A regra do Level 0 (lightFadeDistance): hoje as Piscinas usam 90/70 m nos sóis e 80/60 m nas janelas, com a sombra sumindo antes da luz.",
                Apply = () =>
                {
                    Action undo = Fade(45f, null, null);
                    foreach (HDAdditionalLightData data in LampData) data.shadowFadeDistance = 54f;
                    return undo;
                }
            });

            setups.Add(new Setup
            {
                Name = "9 So a lanterna",
                Torch = true,
                Note = "Nenhuma claraboia nem janela acesa (as lâmpadas submersas continuam).",
                Apply = () =>
                {
                    foreach (Light lamp in Lamps) lamp.enabled = false;
                    return () => { foreach (Light lamp in Lamps) lamp.enabled = true; };
                }
            });

            return setups;
        }

        // -benchParts: o custo fixo do quadro, parte por parte, em qualquer fase. Cada uma desliga
        // uma coisa só na câmera do jogador (as "frame settings" do HDRP) ou muda a resolução.
        static List<Setup> PartSetups()
        {
            return new List<Setup>
            {
                new Setup { Name = "0 Primeira passada (sombras sendo calculadas)", Note = "Só para as sombras ficarem prontas." },
                new Setup { Name = "1 Atual, sem lanterna", Note = "Como o jogo está hoje." },
                new Setup { Name = "2 Atual, com lanterna", Torch = true, Note = "Base de comparação." },
                new Setup { Name = "3 STP a 67%", Torch = true, Note = "Resolução dinâmica: desenha a 67% e o STP do HDRP reconstrói 1920 x 1080.", Apply = () => DynamicResolution(67f) },
                new Setup { Name = "4 STP a 50%", Torch = true, Note = "Desenha a 50% e o STP reconstrói.", Apply = () => DynamicResolution(50f) },
                new Setup { Name = "5 Nevoa volumetrica em qualidade baixa", Torch = true, Note = "A mesma névoa, com metade do orçamento (Fog.quality = Low).", Apply = () => FogQuality(0) },
                new Setup { Name = "6 Sem nevoa volumetrica", Torch = true, Note = "Frame setting Volumetrics desligada.", Apply = () => FrameOff(FrameSettingsField.Volumetrics) },
                new Setup { Name = "7 Sem pos-processamento", Torch = true, Note = "Bloom, grão, aberração, vinheta, distorção, tons (e o TAA).", Apply = () => FrameOff(FrameSettingsField.Postprocess) },
                new Setup { Name = "8 Sem passada da filmadora", Torch = true, Note = "Custom Pass desligado (CamcorderPass).", Apply = () => FrameOff(FrameSettingsField.CustomPass) },
                new Setup { Name = "9 Sem mapas de sombra", Torch = true, Note = "Nenhuma sombra (mede ler as sombras, não só calculá-las).", Apply = () => FrameOff(FrameSettingsField.ShadowMaps) },
                new Setup { Name = "10 Sem decalques", Torch = true, Note = "Frame setting Decals desligada.", Apply = () => FrameOff(FrameSettingsField.Decals) },
                new Setup { Name = "11 Sem transparentes", Torch = true, Note = "Frame setting TransparentObjects desligada (água, vidro, painéis).", Apply = () => FrameOff(FrameSettingsField.TransparentObjects) },
                new Setup { Name = "12 Sem TAA", Torch = true, Note = "Antialiasing da câmera desligado.", Apply = () => Antialiasing(HDAdditionalCameraData.AntialiasingMode.None) },
                new Setup { Name = "13 Sombras com filtro medio", Torch = true, Note = "Filtro das sombras pontuais e direcionais em Medium (hoje High, que procura a borda da sombra antes de filtrar).", Apply = () => ShadowFilter(1) },
                new Setup
                {
                    Name = "14 Pacote: STP 67% + filtro medio + nevoa baixa",
                    Torch = true,
                    Note = "As três que quase não mudam a imagem, juntas.",
                    Apply = () =>
                    {
                        Action a = ShadowFilter(1), b = DynamicResolution(67f), c = FogQuality(0);
                        return () => { c(); b(); a(); };
                    }
                },
            };
        }

        static Action FrameOff(FrameSettingsField field)
        {
            var data = _camera.GetComponent<HDAdditionalCameraData>();
            bool custom = data.customRenderingSettings;
            FrameSettingsOverrideMask mask = data.renderingPathCustomFrameSettingsOverrideMask;
            FrameSettings settings = data.renderingPathCustomFrameSettings;

            data.customRenderingSettings = true;
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            data.renderingPathCustomFrameSettings.SetEnabled(field, false);

            return () =>
            {
                data.customRenderingSettings = custom;
                data.renderingPathCustomFrameSettingsOverrideMask = mask;
                data.renderingPathCustomFrameSettings = settings;
            };
        }

        static Action Antialiasing(HDAdditionalCameraData.AntialiasingMode mode)
        {
            var data = _camera.GetComponent<HDAdditionalCameraData>();
            HDAdditionalCameraData.AntialiasingMode old = data.antialiasing;
            data.antialiasing = mode;
            return () => data.antialiasing = old;
        }

        static Action FogQuality(int level)
        {
            var go = new GameObject("NevoaQualidade");
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Fog fog = profile.Add<Fog>(false);
            fog.quality.Override(level);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = profile;

            return () =>
            {
                UnityEngine.Object.Destroy(go);
                UnityEngine.Object.Destroy(profile);
            };
        }

        // Filtro das sombras no perfil do HDRP em uso (0 baixo, 1 médio, 2 alto). Trocar o perfil
        // recria o pipeline: as sombras em cache são refeitas nos primeiros quadros.
        static Action ShadowFilter(int quality)
        {
            var asset = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as HDRenderPipelineAsset;
            if (asset == null) return null;

            var serialized = new SerializedObject(asset);
            SerializedProperty punctual = serialized.FindProperty("m_RenderPipelineSettings.hdShadowInitParams.punctualShadowFilteringQuality");
            SerializedProperty directional = serialized.FindProperty("m_RenderPipelineSettings.hdShadowInitParams.directionalShadowFilteringQuality");
            int oldPunctual = punctual.intValue, oldDirectional = directional.intValue;
            punctual.intValue = quality;
            directional.intValue = quality;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return () =>
            {
                var back = new SerializedObject(asset);
                back.FindProperty("m_RenderPipelineSettings.hdShadowInitParams.punctualShadowFilteringQuality").intValue = oldPunctual;
                back.FindProperty("m_RenderPipelineSettings.hdShadowInitParams.directionalShadowFilteringQuality").intValue = oldDirectional;
                back.ApplyModifiedPropertiesWithoutUndo();
            };
        }

        // Liga a resolução dinâmica do perfil do HDRP em uso, forçada num percentual, com o STP
        // como reconstrutor. Mexe no asset só em memória e devolve tudo como estava.
        static Action DynamicResolution(float percent)
        {
            var asset = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as HDRenderPipelineAsset;
            if (asset == null) return null;

            var serialized = new SerializedObject(asset);
            SerializedProperty root = serialized.FindProperty("m_RenderPipelineSettings.dynamicResolutionSettings");
            string before = EditorJsonUtility.ToJson(asset);

            root.FindPropertyRelative("enabled").boolValue = true;
            root.FindPropertyRelative("dynResType").intValue = 0;
            root.FindPropertyRelative("forceResolution").boolValue = true;
            root.FindPropertyRelative("forcedPercentage").floatValue = percent;
            root.FindPropertyRelative("minPercentage").floatValue = percent;
            root.FindPropertyRelative("maxPercentage").floatValue = 100f;
            SerializedProperty upscalers = root.FindPropertyRelative("advancedUpscalersByPriority");
            upscalers.arraySize = 1;
            upscalers.GetArrayElementAtIndex(0).intValue = (int)AdvancedUpscalers.STP;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var data = _camera.GetComponent<HDAdditionalCameraData>();
            bool allow = data.allowDynamicResolution;
            data.allowDynamicResolution = true;

            return () =>
            {
                EditorJsonUtility.FromJsonOverwrite(before, asset);
                data.allowDynamicResolution = allow;
            };
        }

        // Muda o alcance, a participação na névoa e o alcance das marcas; devolve o que desfaz.
        static Action Fade(float? distance, bool? volumetric, float? decalDistance)
        {
            var fade = new float[LampData.Count];
            var shadowFade = new float[LampData.Count];
            var fog = new bool[LampData.Count];

            for (int i = 0; i < LampData.Count; i++)
            {
                fade[i] = LampData[i].fadeDistance;
                shadowFade[i] = LampData[i].shadowFadeDistance;
                fog[i] = LampData[i].affectsVolumetric;

                if (distance.HasValue)
                {
                    LampData[i].fadeDistance = distance.Value;
                    LampData[i].shadowFadeDistance = distance.Value;
                }

                if (volumetric.HasValue) LampData[i].affectsVolumetric = volumetric.Value;
            }

            var draw = new float[_decals.Length];
            for (int i = 0; i < _decals.Length; i++)
            {
                draw[i] = _decals[i].drawDistance;
                if (decalDistance.HasValue) _decals[i].drawDistance = decalDistance.Value;
            }

            return () =>
            {
                for (int i = 0; i < LampData.Count; i++)
                {
                    LampData[i].fadeDistance = fade[i];
                    LampData[i].shadowFadeDistance = shadowFade[i];
                    LampData[i].affectsVolumetric = fog[i];
                }

                for (int i = 0; i < _decals.Length; i++) _decals[i].drawDistance = draw[i];
            };
        }

        // ---------------------------------------------------------------- Câmera e medida

        // A caminhada: um ponto a cada meio metro do caminho seguro, do início à saída. A câmera
        // vira aos poucos para o lado em que se anda e balança 35 graus para cada lado.
        static List<Pose> Walk(LevelBuilder builder)
        {
            LevelData data = builder.Data;
            var walk = new List<Pose>();
            float yaw = 0f;
            bool first = true;
            int frame = 0;

            // Nas Piscinas, o caminho a pé (sem entrar em poço).
            List<int> path = data.HasPools ? LevelGenerator.FootPath(data) : data.SafePathCells;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                int a = path[i], b = path[i + 1];
                Vector3 from = builder.CellCenter(a % data.Width, a / data.Width);
                Vector3 to = builder.CellCenter(b % data.Width, b / data.Width);
                Vector3 direction = (to - from).normalized;
                float heading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

                if (first)
                {
                    yaw = heading;
                    first = false;
                }

                int steps = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(from, to) / StepMeters));
                for (int step = 0; step < steps; step++)
                {
                    yaw = Mathf.MoveTowardsAngle(yaw, heading, 7f);
                    float look = yaw + Mathf.Sin(frame * 0.045f) * 35f;
                    walk.Add(new Pose(Vector3.Lerp(from, to, step / (float)steps), Quaternion.Euler(0f, look, 0f)));
                    frame++;
                }
            }

            return walk;
        }

        static void PlaceAt(Pose pose)
        {
            _player.transform.SetPositionAndRotation(pose.position + Vector3.up * 0.05f, pose.rotation);
            _torch.transform.SetPositionAndRotation(_camera.transform.position, _camera.transform.rotation);
        }

        // Desenha um quadro e espera a placa de vídeo terminar. Devolve o tempo em milissegundos.
        static double Draw()
        {
            var watch = Stopwatch.StartNew();
            _camera.Render();
            RenderTexture.active = _target;
            _pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds;
        }

        static void Save(string path)
        {
            RenderTexture.active = _target;
            _photo.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            _photo.Apply();
            File.WriteAllBytes(path, _photo.EncodeToPNG());
        }

        // ---------------------------------------------------------------- Relatório e saída

        static void WriteReport()
        {
            if (Results.Count == 0) return;

            Result baseline = Results.Find(r => r.Name.StartsWith("2 ")) ?? Results[0];
            double baseMean = Mean(baseline.Frames);
            var text = new StringBuilder();

            text.AppendLine(System.IO.Path.GetFileNameWithoutExtension(SessionState.GetString(SceneKey, ScenePath)) + " - custo de desenhar um quadro, por configuracao (medido dentro do Play)");
            text.AppendLine($"Placa: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB) | Processador: {SystemInfo.processorType}");
            text.AppendLine($"Perfil grafico: {QualitySettings.names[QualitySettings.GetQualityLevel()]} | {Width} x {Height} | caminhada de {_walk.Count} quadros, {StepMeters} m por quadro");
            text.AppendLine($"Na fase: {Lamps.Count} luminarias acesas, {_decals.Length} marcas");
            text.AppendLine("Tempo = processador + placa somados (um atras do outro). Serve para comparar, nao e a taxa de quadros do jogo.");
            text.AppendLine();
            text.AppendLine("configuracao;quadros;media_ms;mediana_ms;p95_ms;p99_ms;pior_ms;contra_a_base;luminarias_ligadas;quadros_com_troca;media_da_troca_ms");

            foreach (Result result in Results)
            {
                double mean = Mean(result.Frames);
                text.AppendLine(string.Join(";",
                    result.Name,
                    result.Frames.Count + (result.CutShort ? " (interrompida)" : ""),
                    mean.ToString("F1"),
                    Percentile(result.Frames, 0.5).ToString("F1"),
                    Percentile(result.Frames, 0.95).ToString("F1"),
                    Percentile(result.Frames, 0.99).ToString("F1"),
                    Percentile(result.Frames, 1.0).ToString("F0"),
                    ((mean / baseMean - 1.0) * 100.0).ToString("+0;-0") + "%",
                    result.LitMin + "-" + result.LitMax,
                    result.SwitchFrames.Count.ToString(),
                    result.SwitchFrames.Count > 0 ? Mean(result.SwitchFrames).ToString("F1") : ""));
            }

            text.AppendLine();
            foreach (Result result in Results) text.AppendLine(result.Name + ": " + result.Note);

            File.WriteAllText(Path.Combine(_folder, "desempenho.txt"), text.ToString(), Encoding.UTF8);
            Debug.Log("[BackRooms] Medicao salva em " + _folder);
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(RunKey, false);
            EntityDirector.Paused = false;

            if (_camera != null) _camera.targetTexture = null;
            RenderTexture.active = null;
            if (_target != null) UnityEngine.Object.Destroy(_target);
            if (_pixel != null) UnityEngine.Object.Destroy(_pixel);
            if (_photo != null) UnityEngine.Object.Destroy(_photo);

            // Em Play o editor guarda a cena aberta antes do Play: quem mediu o Level 2 deixaria
            // o editor nele. Sai do Play e fecha com o Level 0 aberto.
            if (SessionState.GetBool(BatchKey, false))
            {
                SessionState.SetInt(ExitCodeKey, code);
                EditorApplication.playModeStateChanged -= ExitAfterPlay;
                EditorApplication.playModeStateChanged += ExitAfterPlay;
            }
            EditorApplication.ExitPlaymode();
        }

        const string ExitCodeKey = "BackRooms.Benchmark.ExitCode";

        [InitializeOnLoadMethod]
        static void HookExit()
        {
            if (SessionState.GetInt(ExitCodeKey, -1) < 0) return;
            EditorApplication.playModeStateChanged -= ExitAfterPlay;
            EditorApplication.playModeStateChanged += ExitAfterPlay;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) ExitAfterPlay(PlayModeStateChange.EnteredEditMode);
        }

        static void ExitAfterPlay(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= ExitAfterPlay;
            int code = SessionState.GetInt(ExitCodeKey, 1);
            SessionState.EraseInt(ExitCodeKey);
            Level1Setup.ExitWithLevel0Open(code);
        }

        static double Mean(List<double> values)
        {
            if (values.Count == 0) return 0.0;
            double sum = 0.0;
            foreach (double value in values) sum += value;
            return sum / values.Count;
        }

        static double Percentile(List<double> values, double fraction)
        {
            if (values.Count == 0) return 0.0;
            var sorted = new List<double>(values);
            sorted.Sort();
            return sorted[Mathf.Clamp(Mathf.RoundToInt((float)(fraction * (sorted.Count - 1))), 0, sorted.Count - 1)];
        }

        static string FileName(string name)
        {
            var clean = new StringBuilder();
            foreach (char c in name.ToLowerInvariant()) clean.Append(char.IsLetterOrDigit(c) ? c : '_');
            return clean.ToString().Trim('_');
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
