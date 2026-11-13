using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Teste em Play do Level 2 de teste (Scenes/Level2_Test.unity, As Piscinas): hospeda,
    /// confere que o jogador nasce no seco da sala inicial, anda o caminho a pé até a saída
    /// (descendo e subindo escadas, passando pela água), depois entra num poço: segurando Espaço
    /// (teclado virtual), fica com a cabeça fora d'água sem perder fôlego; soltando, afunda sozinho
    /// até acabar o fôlego (encurtado), afoga e volta à sala inicial; de volta ao poço, debaixo
    /// d'água, sobe segurando Espaço e nada até uma área funda vizinha e fica de pé nela; ali,
    /// agachado, põe a cabeça debaixo d'água e o fôlego corre. Por fim, passa por um anoitecer
    /// encurtado: a luz das claraboias e janelas desce sem voltar a subir até zero, as lâmpadas
    /// submersas continuam acesas e a luz volta ao valor do dia.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level2PlayTest.BatchRun (sai com 0
    /// se passou, 3 se falhou; o editor fecha com o Level 0 aberto). Menu: BackRooms > Testar
    /// Level 2 (Play). Resultado na linha "[BackRooms] Teste do Level 2 PASSOU/FALHOU".
    /// </summary>
    public static class Level2PlayTest
    {
        const string ActiveKey = "BackRooms.Level2PlayTest.Active";
        const string BatchKey = "BackRooms.Level2PlayTest.Batch";
        const string ExitCodeKey = "BackRooms.Level2PlayTest.ExitCode";
        const string SeedKey = "BackRooms.Level2PlayTest.Seed";
        const float WalkSpeed = 3f;
        const float Reach = 0.45f;
        const double Timeout = 240.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage;
        static double _began, _stageStart, _lastTick, _lastProgress;
        static PlayerNetwork _local;
        static PlayerController _controller;
        static CharacterController _body;
        static LevelBuilder _level;
        static List<int> _path;
        static int _target;
        static float _best, _walked, _lowest;
        static Vector3 _lastPosition;
        static int _wellX, _wellZ, _deepX, _deepZ;
        static int _drownedBefore;
        static Keyboard _keyboard;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;
        static float _sinkStart, _sinkLowest;
        static bool _wentUnder;
        static int _crouchStep;
        const float HoldSeconds = 4f;

        [MenuItem("BackRooms/Testar Level 2 (Play)")]
        public static void RunFromMenu() { Run(false); }

        public static void BatchRun() { Run(true); }

        static void Run(bool batch)
        {
            EditorSceneManager.OpenScene(Level2Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            // Linha de comando: -seed N escolhe a planta (padrão 1998).
            int seed = 1998;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-seed" && int.TryParse(args[i + 1], out int parsed)) seed = parsed;
            SessionState.SetInt(SeedKey, seed);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (SessionState.GetInt(ExitCodeKey, -1) >= 0)
            {
                EditorApplication.playModeStateChanged -= ExitAfterPlay;
                EditorApplication.playModeStateChanged += ExitAfterPlay;
                if (!EditorApplication.isPlayingOrWillChangePlaymode) ExitAfterPlay(PlayModeStateChange.EnteredEditMode);
            }

            if (!SessionState.GetBool(ActiveKey, false)) return;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (stackTrace != null && (stackTrace.Contains("UnityEditor.Search.") || stackTrace.Contains("Unity.AI."))) return;
            Errors.Add(message);
        }

        static double Now => EditorApplication.timeSinceStartup;

        static void Next()
        {
            _stage++;
            _stageStart = Now;
        }

        static double InStage => Now - _stageStart;

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = _lastTick = _stageStart = Now;
            float dt = Mathf.Clamp((float)(Now - _lastTick), 0f, 0.1f);
            _lastTick = Now;

            if (Now - _began > Timeout) { Finish(false, $"tempo esgotado na etapa {_stage}"); return; }

            switch (_stage)
            {
                case 0:
                    session.fixedSeed = SessionState.GetInt(SeedKey, 1998);
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                    Begin(session);
                    break;

                case 2:
                    Walk(dt);
                    break;

                case 3:
                    EnterWell();
                    break;

                case 4:
                    HoldSurface();
                    break;

                case 5:
                    Sink();
                    break;

                case 6:
                    BackToStart();
                    break;

                case 7:
                    SwimOut(dt);
                    break;

                case 8:
                    CrouchInDeep();
                    break;

                case 9:
                    Dusk(session);
                    break;
            }
        }

        static void Begin(GameSession session)
        {
            if (session.Phase != SessionPhase.Playing) return;
            foreach (PlayerNetwork player in Object.FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None))
                if (player.IsSpawned && player.IsOwner) _local = player;
            _level = Object.FindFirstObjectByType<LevelBuilder>();
            if (_local == null || _level == null || _level.Data == null || PoolWater.Current == null) return;
            if (InStage < 1.5) return; // deixa assentar no chão

            _controller = _local.controller;
            _body = _local.GetComponent<CharacterController>();
            LevelData d = _level.Data;
            if (!d.HasPools) { Finish(false, "a fase não tem as Piscinas ligadas"); return; }

            Vector2Int cell = CellOf(_local.transform.position);
            if (!d.InRoom(cell.x, cell.y)) { Finish(false, $"o jogador nasceu fora da sala inicial, na célula {cell}"); return; }
            float floor = d.FloorHeight(cell.x, cell.y);
            if (Mathf.Abs(_local.transform.position.y - floor) > 0.15f || _controller.Submersion > 0f)
            {
                Finish(false, $"o jogador nasceu a {_local.transform.position.y:F2} m, fora do piso seco ({floor:F2} m)");
                return;
            }

            _path = PoolGenerator.FootPath(d);
            if (_path.Count < 2) { Finish(false, "não há caminho a pé até a saída"); return; }

            _target = 0;
            _best = float.MaxValue;
            _lowest = float.MaxValue;
            _lastProgress = Now;
            _lastPosition = _local.transform.position;
            Notes.Add($"nasceu no seco da sala inicial (piso a {floor:F1} m)");
            Next();
        }

        // Anda até o centro de cada célula do caminho a pé, uma por vez.
        static void Walk(float dt)
        {
            Vector3 position = _local.transform.position;
            _walked += Vector3.Distance(Flat(position), Flat(_lastPosition));
            _lastPosition = position;
            _lowest = Mathf.Min(_lowest, position.y);

            if (_controller.IsSwimming) { Finish(false, $"começou a nadar no caminho a pé, na célula {CellOf(position)}"); return; }

            int cell = _path[_target];
            Vector3 goal = _level.CellCenter(cell % _level.Data.Width, cell / _level.Data.Width);
            Vector3 toGoal = Flat(goal - position);
            float distance = toGoal.magnitude;

            if (distance < Reach)
            {
                float floor = goal.y;
                if (Mathf.Abs(position.y - floor) > 0.25f)
                {
                    Finish(false, $"chegou à célula ({cell % _level.Data.Width},{cell / _level.Data.Width}) a {position.y:F2} m, longe do piso ({floor:F2} m)");
                    return;
                }

                _target++;
                _best = float.MaxValue;
                _lastProgress = Now;
                if (_target >= _path.Count)
                {
                    Notes.Add($"andou {_walked:F0} m e {_path.Count} células até a saída, descendo até {_lowest:F1} m");
                    Next();
                }
                return;
            }

            if (distance < _best - 0.05f) { _best = distance; _lastProgress = Now; }
            else if (Now - _lastProgress > 3.0)
            {
                Finish(false, $"travou indo para a célula ({cell % _level.Data.Width},{cell / _level.Data.Width}), a {distance:F2} m do centro, na altura {position.y:F2} m");
                return;
            }

            _body.Move(toGoal.normalized * Mathf.Min(WalkSpeed * dt, distance));
        }

        // Escolhe um poço com área funda vizinha e põe o jogador dentro, um metro abaixo da superfície.
        static void EnterWell()
        {
            LevelData d = _level.Data;
            bool found = false;
            for (int z = 0; z < d.Height && !found; z++)
                for (int x = 0; x < d.Width && !found; x++)
                {
                    if (d.Depth[x, z] != PoolDepth.Well) continue;
                    foreach (WallSide side in new[] { WallSide.South, WallSide.North, WallSide.West, WallSide.East })
                    {
                        if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz) || d.Depth[nx, nz] != PoolDepth.Deep) continue;
                        _wellX = x; _wellZ = z; _deepX = nx; _deepZ = nz;
                        found = true;
                        break;
                    }
                }

            if (!found) { Finish(false, $"a seed {SessionState.GetInt(SeedKey, 1998)} não tem poço com área funda vizinha"); return; }

            // Teclado virtual (como no teste de fumaça): Espaço segurado de verdade, pela ação do jogo.
            // Sem as duas configurações o Input System descarta os eventos quando a janela não tem foco.
            _backgroundBefore = InputSystem.settings.backgroundBehavior;
            _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            Press(Key.Space);
            _controller.airSeconds = 3f;
            _controller.RefillAir();
            Vector3 center = _level.CellCenter(_wellX, _wellZ);
            _controller.Teleport(new Vector3(center.x, PoolWater.Current.SurfaceY - _controller.floatDepth, center.z), 0f);
            _wentUnder = false;
            Next();
        }

        static void Press(params Key[] keys)
        {
            if (_keyboard != null) InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        }

        static void ReleaseKeyboard()
        {
            if (_keyboard == null) return;
            InputSystem.RemoveDevice(_keyboard);
            _keyboard = null;
            InputSystem.settings.backgroundBehavior = _backgroundBefore;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBefore;
        }

        // Segurando Espaço: nada, os pés uns 1,4 m abaixo da superfície, a cabeça fora d'água e o
        // fôlego (encurtado para 3 s) cheio por HoldSeconds (depois de 1 s para subir à tona).
        static void HoldSurface()
        {
            if (InStage < 1.0) return;

            float surface = PoolWater.Current.SurfaceY;
            float feet = _local.transform.position.y;
            if (!_controller.IsSwimming || _controller.IsUnderwater || _controller.Air01 < 0.999f)
            {
                Finish(false, $"no poço, segurando Espaço, não ficou com a cabeça fora: nadando={_controller.IsSwimming}, pés a {feet - surface:F2} m da superfície, " +
                              $"cabeça debaixo d'água={_controller.IsUnderwater}, fôlego={_controller.Air01:F2} ({InStage:F1} s)");
                return;
            }
            if (InStage < 1.0 + HoldSeconds) return;

            if (Mathf.Abs(feet - (surface - _controller.floatDepth)) > 0.3f)
            {
                Finish(false, $"segurando Espaço no poço os pés ficaram a {feet - surface:F2} m da superfície (esperado {-_controller.floatDepth:F2})");
                return;
            }

            Notes.Add($"segurando Espaço, ficou {HoldSeconds:F0} s à tona no poço (pés a {feet - surface:F2} m, cabeça fora, fôlego cheio)");
            Press();
            _drownedBefore = _controller.Drownings;
            _sinkStart = _sinkLowest = feet;
            Next();
        }

        // Sem tecla: afunda sozinho, põe a cabeça debaixo d'água e se afoga quando o fôlego acaba.
        static void Sink()
        {
            if (_controller.Drownings > _drownedBefore)
            {
                float sank = _sinkStart - _sinkLowest;
                if (!_wentUnder || sank < 1f)
                {
                    Finish(false, $"afogou sem ter afundado: desceu {sank:F2} m, cabeça debaixo d'água antes={_wentUnder}");
                    return;
                }
                Notes.Add($"sem tecla afundou sozinho ({sank:F1} m em {InStage:F1} s, {sank / Mathf.Max(0.1f, (float)InStage):F2} m/s) e afogou (fôlego de {_controller.airSeconds:F0} s)");
                Next();
                return;
            }

            if (!_controller.IsReturning)
            {
                _sinkLowest = Mathf.Min(_sinkLowest, _local.transform.position.y);
                if (_controller.IsUnderwater) _wentUnder = true;
            }

            if (InStage > 15.0)
            {
                Finish(false, $"sem tecla, 15 s no poço e não se afogou: desceu {_sinkStart - _sinkLowest:F2} m, debaixo d'água={_controller.IsUnderwater}, fôlego={_controller.Air01:F2}");
            }
        }

        static void BackToStart()
        {
            if (_controller.IsReturning) return;
            if (InStage < 0.5) return;

            Vector3 position = _local.transform.position;
            if (!_level.Data.InRoom(CellOf(position).x, CellOf(position).y))
            {
                if (InStage > 6.0) Finish(false, $"depois de afogar não voltou à sala inicial (está na célula {CellOf(position)})");
                return;
            }

            Notes.Add("voltou à sala inicial depois de afogar");
            _controller.airSeconds = 20f;
            _controller.RefillAir();

            // De volta ao poço, 2 m abaixo da superfície: segurando Espaço, sobe e nada para fora.
            Vector3 center = _level.CellCenter(_wellX, _wellZ);
            _controller.Teleport(new Vector3(center.x, PoolWater.Current.SurfaceY - _controller.floatDepth - 2f, center.z), 0f);
            Press(Key.Space);
            _lastProgress = Now;
            Next();
        }

        // Segurando Espaço, nada do poço para a área funda vizinha e tem de ficar de pé nela, fora do nado.
        static void SwimOut(float dt)
        {
            Vector3 position = _local.transform.position;
            Vector2Int cell = CellOf(position);
            Vector3 goal = _level.CellCenter(_deepX, _deepZ);

            if (cell.x == _deepX && cell.y == _deepZ && !_controller.IsSwimming && _body.isGrounded && Mathf.Abs(position.y - goal.y) < 0.15f)
            {
                Notes.Add($"debaixo d'água no poço, subiu segurando Espaço, saiu nadando e ficou de pé na área funda (piso a {goal.y:F1} m, água no peito) em {InStage:F1} s");
                Press();
                _crouchStep = 0;
                Next();
                return;
            }

            if (InStage > 15.0)
            {
                Finish(false, $"não conseguiu sair do poço para a área funda: célula {cell}, pés a {position.y:F2} m, nadando={_controller.IsSwimming}");
                return;
            }

            // Primeiro sobe (até a cabeça sair da água); depois anda para a funda, ainda com Espaço.
            if (InStage < 1.0 || _controller.IsUnderwater && cell.x == _wellX && cell.y == _wellZ) return;
            Vector3 toGoal = Flat(goal - position);
            if (toGoal.magnitude > 0.2f) _body.Move(toGoal.normalized * 1.5f * dt);
        }

        // Na área funda (dá pé), agachar põe a cabeça debaixo d'água e o fôlego corre; de pé, volta.
        static void CrouchInDeep()
        {
            switch (_crouchStep)
            {
                case 0:
                    // De pé: a cabeça fora d'água. Agacha.
                    if (InStage < 0.5) return;
                    if (_controller.IsUnderwater) { Finish(false, "de pé na área funda a cabeça ficou debaixo d'água"); return; }
                    _controller.RefillAir();
                    Press(Key.LeftCtrl);
                    _crouchStep = 1;
                    _stageStart = Now;
                    return;

                case 1:
                    // Agachado 2 s: cabeça debaixo d'água, sem nadar, fôlego descendo. Levanta.
                    if (InStage < 2.0) return;
                    if (!_controller.IsUnderwater || _controller.Air01 > 0.95f || _controller.IsSwimming)
                    {
                        Finish(false, $"agachado na área funda: cabeça debaixo d'água={_controller.IsUnderwater}, fôlego={_controller.Air01:F2}, nadando={_controller.IsSwimming}");
                        return;
                    }
                    Notes.Add($"agachado na área funda a cabeça foi para debaixo d'água e o fôlego desceu a {_controller.Air01:P0} em {InStage:F1} s");
                    Press();
                    _crouchStep = 2;
                    _stageStart = Now;
                    return;

                default:
                    // De pé de novo: a cabeça sai da água.
                    if (_controller.IsUnderwater)
                    {
                        if (InStage > 2.0) Finish(false, "levantou na área funda e a cabeça continuou debaixo d'água");
                        return;
                    }
                    ReleaseKeyboard();
                    Next();
                    return;
            }
        }

        // ---------------------------------------------------------------- Anoitecer

        static double _duskDark, _duskDawnEnd;
        static float _duskDay, _duskLast;
        static int _duskSamples, _duskNightSamples;
        static bool _duskStarted, _duskReachedNight;
        static List<(Light light, HDAdditionalLightData data, float intensity)> _lamps;

        // Anoitecer encurtado (mesma conta, tempos menores): adianta o próximo escuro para daqui a
        // pouco e acompanha a luz das claraboias e janelas quadro a quadro. Tem de descer sem
        // voltar a subir (sem pisca) até ~0 na noite, com as lâmpadas submersas acesas o tempo
        // todo, e voltar ao valor do dia depois.
        static void Dusk(GameSession session)
        {
            PoolWater water = PoolWater.Current;
            LightsOut clock = LightsOut.Instance;
            if (water == null || clock == null) { Finish(false, "anoitecer: sem PoolWater ou sem LightsOut na fase"); return; }
            double now = session.SessionSeconds;

            if (!_duskStarted)
            {
                if (Object.FindObjectsByType<CeilingLight>(FindObjectsInactive.Include).Length > 0)
                {
                    Finish(false, "anoitecer: há luminária (CeilingLight) nas Piscinas, que piscaria no apagão");
                    return;
                }
                if (DaylightPanel.All.Count == 0) { Finish(false, "anoitecer: nenhum vidro de luz do dia"); return; }

                water.duskWarning = 2f;
                water.duskFade = 3f;
                water.dawnFade = 3f;
                clock.darkSeconds = 2f;
                clock.firstDarkAt = (float)now + water.duskWarning + water.duskFade + 1.5f;
                _duskDark = clock.firstDarkAt;
                _duskDawnEnd = _duskDark + clock.darkSeconds + water.dawnFade;

                _lamps = new List<(Light, HDAdditionalLightData, float)>();
                Transform lamps = _level.transform.Find("Lampadas");
                if (lamps != null)
                    foreach (Light light in lamps.GetComponentsInChildren<Light>(true))
                        _lamps.Add((light, light.GetComponent<HDAdditionalLightData>(), light.intensity));
                _duskStarted = true;
                _duskDay = -1f;
                return;
            }

            float sky = SkylightIntensity();
            if (_duskDay < 0f)
            {
                // Primeiro quadro depois de adiantar o relógio: ainda é dia pleno.
                _duskDay = _duskLast = sky;
                if (sky <= 0f) { Finish(false, "anoitecer: as claraboias e janelas começaram sem luz"); return; }
                return;
            }
            _duskSamples++;
            float eps = _duskDay * 1e-4f;

            foreach (var (light, data, intensity) in _lamps)
                if (light == null || !light.enabled || !light.gameObject.activeInHierarchy || !Mathf.Approximately(light.intensity, intensity) ||
                    (data != null && data.lightDimmer < 0.999f))
                {
                    Finish(false, $"anoitecer: uma lâmpada submersa mudou ou apagou aos {now - _duskDark:F1} s do escuro");
                    return;
                }

            // A fase vem do estado que o PoolWater aplicou junto com as luzes (o relógio lido aqui
            // pode estar um quadro à frente).
            bool night = water.CurrentDaylight.Night >= 0.999f;
            if (!_duskReachedNight && !night)
            {
                if (sky > _duskLast + eps) { Finish(false, $"anoitecer: a luz voltou a subir antes da noite ({_duskLast:F0} -> {sky:F0}, {now - _duskDark:F2} s do escuro): pisca"); return; }
                if (now > _duskDark + 1.0) { Finish(false, $"anoitecer: 1 s depois do escuro do LightsOut ainda não era noite (luz em {sky / _duskDay:P1})"); return; }
            }
            else if (night)
            {
                _duskReachedNight = true;
                _duskNightSamples++;
                if (sky > _duskDay * 0.001f) { Finish(false, $"anoitecer: de noite as claraboias ainda dão {sky / _duskDay:P1} da luz do dia"); return; }
            }
            else
            {
                if (sky < _duskLast - eps) { Finish(false, $"anoitecer: a luz caiu de novo no amanhecer ({_duskLast:F0} -> {sky:F0}): pisca"); return; }
                if (now >= _duskDawnEnd + 0.5)
                {
                    if (_duskNightSamples == 0) { Finish(false, "anoitecer: nenhuma amostra da noite"); return; }
                    if (Mathf.Abs(sky - _duskDay) > _duskDay * 0.01f) { Finish(false, $"anoitecer: depois de amanhecer a luz ficou em {sky / _duskDay:P1} da do dia"); return; }
                    Notes.Add($"anoitecer: a luz das claraboias e janelas desceu sem voltar a subir até 0 ({_duskSamples} amostras, {_duskNightSamples} de noite), " +
                              $"{_lamps.Count} lâmpadas submersas acesas o tempo todo, e voltou a 100% do dia");
                    Finish(true, string.Join("; ", Notes));
                    return;
                }
            }
            _duskLast = sky;
        }

        // Luz que as claraboias e janelas dão agora: intensidade vezes dimmer, somada.
        static float SkylightIntensity()
        {
            float total = 0f;
            foreach (DaylightPanel panel in DaylightPanel.All)
            {
                if (panel == null) continue;
                foreach (HDAdditionalLightData data in panel.GetComponentsInChildren<HDAdditionalLightData>(true))
                {
                    var light = data.GetComponent<Light>();
                    if (light != null && light.enabled) total += light.intensity * data.lightDimmer;
                }
            }
            return total;
        }

        static Vector2Int CellOf(Vector3 world)
        {
            Vector3 local = _level.transform.InverseTransformPoint(world);
            return new Vector2Int(Mathf.FloorToInt(local.x / LevelBuilder.Cell), Mathf.FloorToInt(local.z / LevelBuilder.Cell));
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        static void Finish(bool ok, string report)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
            ReleaseKeyboard();

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log("[BackRooms] Teste do Level 2 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

            if (SessionState.GetBool(BatchKey, false))
            {
                // Sai do Play, reabre o Level 0 e só então fecha o editor.
                SessionState.SetInt(ExitCodeKey, ok ? 0 : 3);
                EditorApplication.playModeStateChanged -= ExitAfterPlay;
                EditorApplication.playModeStateChanged += ExitAfterPlay;
            }

            EditorApplication.ExitPlaymode();
        }

        static void ExitAfterPlay(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;

            EditorApplication.playModeStateChanged -= ExitAfterPlay;
            int code = SessionState.GetInt(ExitCodeKey, 1);
            SessionState.EraseInt(ExitCodeKey);
            Level1Setup.ExitWithLevel0Open(code);
        }
    }
}
