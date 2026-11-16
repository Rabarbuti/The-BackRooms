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
    /// confere que o jogador nasce no seco da sala inicial, anda o caminho a pé até a saída sem
    /// nadar (descendo e subindo escadas, passando pelo raso), depois entra num poço: segurando
    /// Espaço (teclado virtual), fica com a cabeça fora d'água sem perder fôlego; soltando, afunda
    /// sozinho até acabar o fôlego (encurtado), afoga e volta à sala inicial; de volta ao poço,
    /// debaixo d'água, sobe segurando Espaço e sai nadando para a área funda vizinha, à tona (lá
    /// também não dá pé). Depois, na água funda: desce por uma escada segurando Espaço, passa a
    /// nadar e atravessa a área funda à tona; solta e afunda até o fundo (2 m); sobe; volta à tona
    /// e sai subindo a escada; e, do outro jeito, sem Espaço afunda, anda pelo fundo até a escada
    /// e sobe os degraus andando. Por fim, passa por um anoitecer encurtado: a luz das claraboias e
    /// janelas desce sem voltar a subir até zero, as lâmpadas submersas continuam acesas e a luz
    /// volta ao valor do dia.
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
                    PickStair();
                    break;

                case 9:
                    DownTheStair(dt);
                    break;

                case 10:
                    SinkToBottom();
                    break;

                case 11:
                    RiseAgain();
                    break;

                case 12:
                    SwimUpTheStair(dt);
                    break;

                case 13:
                    WalkUpFromBottom(dt);
                    break;

                case 14:
                    ClimbEdge(PoolDepth.Dry);
                    break;

                case 15:
                    ClimbEdge(PoolDepth.Shallow);
                    break;

                case 16:
                    NoClimb();
                    break;

                case 17:
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
            if (_path.Count < 2) { Finish(false, "não há caminho a pé (sem nadar) até a saída"); return; }

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

            if (_controller.IsSwimming) { Finish(false, $"começou a nadar no caminho a pé (que não passa pela água funda), na célula {CellOf(position)}"); return; }

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

        // Segurando Espaço, nada do poço para a área funda vizinha: lá também não dá pé, então
        // continua nadando à tona (cabeça fora d'água).
        static void SwimOut(float dt)
        {
            Vector3 position = _local.transform.position;
            Vector2Int cell = CellOf(position);
            Vector3 goal = _level.CellCenter(_deepX, _deepZ);
            float surface = PoolWater.Current.SurfaceY;

            if (cell.x == _deepX && cell.y == _deepZ && Flat(goal - position).magnitude < 1f && _controller.IsSwimming && !_controller.IsUnderwater)
            {
                Notes.Add($"debaixo d'água no poço, subiu segurando Espaço e saiu nadando para a área funda vizinha, à tona (pés a {position.y - surface:F2} m da superfície, " +
                          $"piso a {goal.y:F1} m) em {InStage:F1} s");
                Next();
                return;
            }

            if (InStage > 15.0)
            {
                Finish(false, $"não conseguiu sair do poço nadando para a área funda: célula {cell}, pés a {position.y:F2} m, nadando={_controller.IsSwimming}, " +
                              $"cabeça debaixo d'água={_controller.IsUnderwater}");
                return;
            }

            // Primeiro sobe (até a cabeça sair da água); depois nada para a funda, ainda com Espaço.
            if (InStage < 1.0 || _controller.IsUnderwater && cell.x == _wellX && cell.y == _wellZ) return;
            Vector3 toGoal = Flat(goal - position);
            if (toGoal.magnitude > 0.2f) _body.Move(toGoal.normalized * 1.5f * dt);
        }

        // ---------------------------------------------------------------- Água funda e escada

        static PoolStair _stair;
        static int _upperX, _upperZ;
        static List<Vector2Int> _swim;          // células fundas, da célula da escada até a mais longe
        static List<Vector3> _route;
        static int _routeIndex;
        static bool _wasSwimming;
        static float _deepestFeet;

        // Escolhe uma escada que sobe da água funda para o seco ou o raso, com água funda sem escada
        // em volta para atravessar nadando (a mais longa, de 11 degraus, se houver; entre as de mesmo
        // tamanho, a da travessia mais longa), e põe o jogador no piso de cima, segurando Espaço.
        static void PickStair()
        {
            LevelData d = _level.Data;
            WallSide[] sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };
            var withStair = new HashSet<int>();
            foreach (PoolStair s in d.Stairs) withStair.Add(s.X + s.Z * d.Width);

            List<Vector2Int> best = null;
            foreach (PoolStair s in d.Stairs)
            {
                if (d.Depth[s.X, s.Z] != PoolDepth.Deep || !d.Neighbor(s.X, s.Z, s.Side, out int ux, out int uz)) continue;
                if (d.Depth[ux, uz] != PoolDepth.Dry && d.Depth[ux, uz] != PoolDepth.Shallow) continue;
                // Caminho mais longo (em células) pela água funda sem outra escada, a partir da célula da escada.
                var previous = new Dictionary<int, int> { [s.X + s.Z * d.Width] = -1 };
                var queue = new Queue<int>();
                queue.Enqueue(s.X + s.Z * d.Width);
                int last = s.X + s.Z * d.Width;
                while (queue.Count > 0)
                {
                    int i = queue.Dequeue(), x = i % d.Width, z = i / d.Width;
                    last = i;
                    foreach (WallSide side in sides)
                    {
                        if (!d.CanStep(x, z, side)) continue;
                        d.Neighbor(x, z, side, out int nx, out int nz);
                        int n = nx + nz * d.Width;
                        if (d.Depth[nx, nz] != PoolDepth.Deep || withStair.Contains(n) || previous.ContainsKey(n)) continue;
                        previous[n] = i;
                        queue.Enqueue(n);
                    }
                }
                var path = new List<Vector2Int>();
                for (int i = last; i != -1; i = previous[i]) path.Add(new Vector2Int(i % d.Width, i / d.Width));
                path.Reverse();
                if (path.Count > 5) path.RemoveRange(5, path.Count - 5);   // travessia de até 4 células
                if (path.Count >= 2 && (best == null || s.Steps > _stair.Steps || s.Steps == _stair.Steps && path.Count > best.Count))
                {
                    best = path;
                    _stair = s;
                    _upperX = ux;
                    _upperZ = uz;
                }
            }
            if (best == null) { Finish(false, $"a seed {SessionState.GetInt(SeedKey, 1998)} não tem escada da água funda com outra célula funda para atravessar"); return; }
            _swim = best;

            _controller.airSeconds = 20f;
            _controller.RefillAir();
            Vector3 top = _level.CellCenter(_upperX, _upperZ);
            _controller.Teleport(top, 0f);
            // Desce pelo eixo da escada até passar do último degrau (sair pelo lado é cair da escada).
            _route = new List<Vector3> { top, _level.CellCenter(_stair.X, _stair.Z) };
            _route.AddRange(FootToNext());
            for (int k = 2; k < _swim.Count; k++) _route.Add(_level.CellCenter(_swim[k].x, _swim[k].y));
            _routeIndex = 0;
            _best = float.MaxValue;
            _lastProgress = Now;
            _wasSwimming = false;
            Press(Key.Space);
            Next();
        }

        // Meio metro além do pé da escada, no eixo dela, dentro da célula de baixo.
        static Vector3 StairFoot()
        {
            Vector3 toUpper = _stair.Side == WallSide.South ? Vector3.back : _stair.Side == WallSide.North ? Vector3.forward
                            : _stair.Side == WallSide.West ? Vector3.left : Vector3.right;
            Vector3 edge = _level.CellCenter(_stair.X, _stair.Z) + toUpper * (LevelBuilder.Cell * 0.5f);
            float run = (_stair.Steps - 1) * 0.3f;   // 30 cm de piso por degrau
            return edge - toUpper * Mathf.Min(run + 0.5f, LevelBuilder.Cell - 0.3f);
        }

        // Do pé da escada até a célula seguinte da travessia: se ela fica ao lado (e não em frente),
        // primeiro em linha reta para o lado, na altura do pé, para não passar por cima dos degraus.
        static List<Vector3> FootToNext()
        {
            Vector3 foot = StairFoot();
            var points = new List<Vector3> { foot };
            Vector3 next = _level.CellCenter(_swim[1].x, _swim[1].y), here = _level.CellCenter(_stair.X, _stair.Z);
            Vector3 toNext = Flat(next - here), toFoot = Flat(foot - here);
            if (Mathf.Abs(Vector3.Dot(toNext.normalized, toFoot.normalized)) < 0.5f) points.Add(next + toFoot);
            points.Add(next);
            return points;
        }

        // Segue a rota (centros de célula) a 1,5 m/s; verdadeiro ao chegar ao fim. Falha se parar de avançar.
        static bool FollowRoute(float dt, string what)
        {
            Vector3 position = _local.transform.position;
            while (_routeIndex < _route.Count)
            {
                Vector3 toGoal = Flat(_route[_routeIndex] - position);
                float distance = toGoal.magnitude;
                if (distance < Reach)
                {
                    _routeIndex++;
                    _best = float.MaxValue;
                    _lastProgress = Now;
                    continue;
                }
                if (distance < _best - 0.05f) { _best = distance; _lastProgress = Now; }
                else if (Now - _lastProgress > 4.0)
                {
                    Vector2Int cell = CellOf(position);
                    Finish(false, $"{what}: travou na célula {cell}, a {distance:F2} m do ponto {_routeIndex} da rota, pés a {position.y:F2} m, nadando={_controller.IsSwimming}");
                    return false;
                }
                _body.Move(toGoal.normalized * Mathf.Min(1.5f * dt, distance));
                return false;
            }
            return true;
        }

        // Desce a escada segurando Espaço: onde deixa de dar pé passa a nadar, e atravessa a água
        // funda à tona até a célula mais longe, sempre com a cabeça fora d'água e o fôlego cheio.
        static void DownTheStair(float dt)
        {
            if (InStage < 0.5) return;
            float surface = PoolWater.Current.SurfaceY;
            if (_controller.IsSwimming) _wasSwimming = true;
            if (_wasSwimming && InStage > 1.0 && (_controller.IsUnderwater || _controller.Air01 < 0.999f))
            {
                Finish(false, $"nadando à tona na água funda com Espaço, a cabeça foi para debaixo d'água (pés a {_local.transform.position.y - surface:F2} m da superfície, " +
                              $"célula {CellOf(_local.transform.position)})");
                return;
            }
            if (InStage > 40.0) { Finish(false, "a travessia da água funda demorou mais de 40 s"); return; }
            if (!FollowRoute(dt, "descendo a escada e nadando na água funda")) return;
            if (InStage < 2.0) return;

            float feet = _local.transform.position.y;
            if (!_controller.IsSwimming || Mathf.Abs(feet - (surface - _controller.floatDepth)) > 0.3f)
            {
                Finish(false, $"no fim da travessia não estava boiando: nadando={_controller.IsSwimming}, pés a {feet - surface:F2} m da superfície");
                return;
            }
            Notes.Add($"desceu a escada de {_stair.Steps} degraus ({_stair.X},{_stair.Z}) segurando Espaço, passou a nadar e atravessou {_swim.Count - 1} célula(s) de água funda à tona " +
                      $"(pés a {feet - surface:F2} m da superfície, cabeça fora, fôlego cheio) em {InStage:F1} s");
            Press();
            _deepestFeet = feet;
            Next();
        }

        // Sem tecla: afunda até ficar de pé no fundo (2 m), com a cabeça debaixo d'água.
        static void SinkToBottom()
        {
            float feet = _local.transform.position.y;
            _deepestFeet = Mathf.Min(_deepestFeet, feet);
            float floor = LevelData.DepthFloor(PoolDepth.Deep);
            if (_body.isGrounded && Mathf.Abs(feet - floor) < 0.1f && InStage > 0.5)
            {
                if (!_controller.IsUnderwater || !_controller.IsSwimming)
                {
                    Finish(false, $"no fundo da água funda: cabeça debaixo d'água={_controller.IsUnderwater}, nadando={_controller.IsSwimming}");
                    return;
                }
                Notes.Add($"sem tecla afundou até o fundo da água funda (pés a {feet:F2} m, cabeça debaixo d'água) em {InStage:F1} s");
                Press(Key.Space);
                Next();
                return;
            }
            if (InStage > 10.0) Finish(false, $"sem tecla não chegou ao fundo da água funda em 10 s: pés a {feet:F2} m (piso a {floor:F1}), no chão={_body.isGrounded}");
        }

        // Segurando Espaço, volta à tona.
        static void RiseAgain()
        {
            float surface = PoolWater.Current.SurfaceY;
            float feet = _local.transform.position.y;
            if (!_controller.IsUnderwater && Mathf.Abs(feet - (surface - _controller.floatDepth)) < 0.15f)
            {
                Notes.Add($"segurando Espaço voltou à tona do fundo em {InStage:F1} s");
                // Volta pela mesma travessia até a escada e sobe por ela, à tona.
                _route = new List<Vector3>();
                for (int k = _swim.Count - 1; k >= 2; k--) _route.Add(_level.CellCenter(_swim[k].x, _swim[k].y));
                List<Vector3> back = FootToNext();
                back.Reverse();
                _route.AddRange(back);
                _route.Add(_level.CellCenter(_stair.X, _stair.Z));
                _route.Add(_level.CellCenter(_upperX, _upperZ));
                _routeIndex = 0;
                _best = float.MaxValue;
                _lastProgress = Now;
                Next();
                return;
            }
            if (InStage > 8.0) Finish(false, $"segurando Espaço não voltou à tona do fundo em 8 s: pés a {feet - surface:F2} m da superfície");
        }

        // Nadando à tona (Espaço), volta até a escada e sobe por ela: o primeiro degrau ao alcance
        // pega o pé de quem boia.
        static bool OnUpperFloor()
        {
            Vector3 position = _local.transform.position;
            Vector2Int cell = CellOf(position);
            return cell.x == _upperX && cell.y == _upperZ && !_controller.IsSwimming && _body.isGrounded &&
                   Mathf.Abs(position.y - _level.Data.FloorHeight(_upperX, _upperZ)) < 0.15f;
        }

        static void SwimUpTheStair(float dt)
        {
            if (InStage > 40.0) { Finish(false, "nadando à tona não saiu pela escada em 40 s"); return; }
            if (!FollowRoute(dt, "nadando à tona de volta e subindo a escada")) return;
            if (!OnUpperFloor())
            {
                if (InStage > 30.0) Finish(false, $"chegou ao fim da rota mas não está de pé no piso de cima da escada (pés a {_local.transform.position.y:F2} m, nadando={_controller.IsSwimming})");
                return;
            }
            Notes.Add($"nadando à tona voltou e saiu da água funda subindo a escada de {_stair.Steps} degraus em {InStage:F1} s");

            // Agora o outro jeito: do fundo, sem Espaço, andando pelo fundo e subindo os degraus.
            Vector2Int start = _swim[1];
            Vector3 center = _level.CellCenter(start.x, start.y);
            _controller.RefillAir();
            _controller.Teleport(new Vector3(center.x, PoolWater.Current.SurfaceY - _controller.floatDepth, center.z), 0f);
            Press();
            _route = FootToNext();
            _route.RemoveAt(_route.Count - 1);   // já está na célula seguinte
            _route.Reverse();
            _route.Add(_level.CellCenter(_swim[0].x, _swim[0].y));
            _route.Add(_level.CellCenter(_upperX, _upperZ));
            _routeIndex = 0;
            _best = float.MaxValue;
            _lastProgress = Now;
            Next();
        }

        // Sem tecla: afunda até o fundo e anda por ele até a escada; os degraus pegam o pé e ele
        // sobe andando até o piso de cima, com fôlego de sobra.
        static void WalkUpFromBottom(float dt)
        {
            if (InStage > 30.0) { Finish(false, "andando pelo fundo não saiu pela escada em 30 s"); return; }
            float floor = LevelData.DepthFloor(PoolDepth.Deep);
            if (_routeIndex == 0 && _best == float.MaxValue)
            {
                // Primeiro afunda até o fundo, sem andar.
                if (!(_body.isGrounded && Mathf.Abs(_local.transform.position.y - floor) < 0.1f))
                {
                    if (InStage > 10.0) Finish(false, $"sem tecla não chegou ao fundo em 10 s (pés a {_local.transform.position.y:F2} m)");
                    return;
                }
                _lastProgress = Now;
            }
            if (_controller.IsReturning) { Finish(false, "andando pelo fundo até a escada, afogou"); return; }
            if (!FollowRoute(dt, "andando pelo fundo até a escada e subindo")) return;
            if (!OnUpperFloor())
            {
                if (InStage > 25.0) Finish(false, $"andou pelo fundo até a escada mas não ficou de pé no piso de cima (pés a {_local.transform.position.y:F2} m, nadando={_controller.IsSwimming})");
                return;
            }
            Notes.Add($"sem Espaço afundou até o fundo, andou por ele até a escada e subiu os degraus andando em {InStage:F1} s (fôlego em {_controller.Air01:P0})");
            Press();
            _edgeStep = 0;
            Next();
        }

        // ---------------------------------------------------------------- Subir na borda nadando

        static int _edgeStep, _climbsBefore;
        static Vector2Int _edgeLow, _edgeHigh;
        static Vector3 _edgeFace;
        static double _climbBegan, _climbEnded;

        // Divisa aberta, sem escada, de uma célula de profundidade low para uma vizinha de profundidade high.
        static bool FindEdge(PoolDepth low, PoolDepth high, out Vector2Int lower, out Vector2Int upper)
        {
            LevelData d = _level.Data;
            lower = upper = default;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.IsSolid(x, z) || d.Depth[x, z] != low || LevelBuilder.IsRoundWell(d, x, z)) continue;
                    foreach (WallSide side in new[] { WallSide.South, WallSide.North, WallSide.West, WallSide.East })
                    {
                        if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz) || d.IsSolid(nx, nz) || d.Depth[nx, nz] != high) continue;
                        if (d.StairAt(x, z, side, out _)) continue;
                        lower = new Vector2Int(x, z);
                        upper = new Vector2Int(nx, nz);
                        return true;
                    }
                }
            return false;
        }

        static Vector3 EdgeMiddle(Vector2Int lower, Vector2Int upper)
        {
            Vector3 a = _level.CellCenter(lower.x, lower.y), b = _level.CellCenter(upper.x, upper.y);
            return Flat((a + b) * 0.5f);
        }

        static void Face(Vector3 target)
        {
            Vector3 to = Flat(target - _local.transform.position);
            if (to.sqrMagnitude > 1e-4f) _local.transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        // Na água funda, encostado numa borda sem escada (deque seco ou piso raso): do fundo, só com
        // W, não sobe (a cabeça está debaixo d'água); segurando Espaço e W, sobe à tona e, à tona,
        // sobe na borda num movimento curto e fica de pé no piso de cima. Encostado numa parede, à
        // tona, não sobe.
        static void ClimbEdge(PoolDepth high)
        {
            string name = high == PoolDepth.Dry ? "deque seco" : "piso raso";
            LevelData d = _level.Data;
            Vector3 position = _local.transform.position;
            switch (_edgeStep)
            {
                case 0:
                {
                    if (!FindEdge(PoolDepth.Deep, high, out _edgeLow, out _edgeHigh))
                    {
                        Finish(false, $"a seed {SessionState.GetInt(SeedKey, 1998)} não tem borda sem escada da água funda para {name}");
                        return;
                    }
                    _edgeFace = EdgeMiddle(_edgeLow, _edgeHigh);
                    Vector3 inward = Flat(_level.CellCenter(_edgeHigh.x, _edgeHigh.y) - _level.CellCenter(_edgeLow.x, _edgeLow.y)).normalized;
                    Vector3 start = _edgeFace - inward * 1.1f;
                    start.y = LevelData.DepthFloor(PoolDepth.Deep) + 0.05f;
                    _controller.RefillAir();
                    _controller.Teleport(start, Quaternion.LookRotation(inward).eulerAngles.y);
                    _climbsBefore = _controller.Climbs;
                    Press(Key.W);
                    _edgeStep = 1;
                    _stageStart = Now;
                    return;
                }
                case 1:
                    // Do fundo, empurrando a borda sem Espaço: não sobe.
                    Face(_level.CellCenter(_edgeHigh.x, _edgeHigh.y));
                    if (_controller.Climbs != _climbsBefore || _controller.IsClimbing)
                    {
                        Finish(false, $"no fundo da água funda, com a cabeça debaixo d'água, empurrando a borda para o {name}, subiu nela (pés a {position.y:F2} m)");
                        return;
                    }
                    if (InStage < 2.5) return;
                    if (CellOf(position) != _edgeLow) { Finish(false, $"do fundo, só com W, saiu da célula funda {_edgeLow} para {CellOf(position)}"); return; }
                    Press(Key.Space, Key.W);
                    _climbBegan = _climbEnded = -1;
                    _edgeStep = 2;
                    _stageStart = Now;
                    return;
                case 2:
                {
                    Face(_level.CellCenter(_edgeHigh.x, _edgeHigh.y));
                    if (_controller.IsClimbing && _climbBegan < 0) _climbBegan = Now;
                    if (_climbBegan >= 0 && !_controller.IsClimbing && _climbEnded < 0) _climbEnded = Now;
                    float floor = d.FloorHeight(_edgeHigh.x, _edgeHigh.y);
                    if (CellOf(position) == _edgeHigh && !_controller.IsSwimming && _body.isGrounded && Mathf.Abs(position.y - floor) < 0.1f && _climbEnded >= 0)
                    {
                        double climb = _climbEnded - _climbBegan;
                        if (_controller.Climbs != _climbsBefore + 1) { Finish(false, $"subiu no {name} com {_controller.Climbs - _climbsBefore} subidas na borda (esperada 1)"); return; }
                        if (climb < 0.3 || climb > 1.2) { Finish(false, $"a subida no {name} levou {climb:F2} s (esperado entre 0,3 e 1,2)"); return; }
                        Notes.Add($"do fundo, só com W, não subiu na borda para o {name}; segurando Espaço e W subiu à tona e subiu na borda ({climb:F2} s) até ficar de pé no piso a {floor:F1} m em {InStage:F1} s");
                        if (high == PoolDepth.Shallow) { _edgeStep = 3; _stageStart = Now; return; }
                        _edgeStep = 0;
                        Next();
                        return;
                    }
                    if (InStage > 10.0)
                        Finish(false, $"segurando Espaço e W contra a borda para o {name} não subiu em 10 s: pés a {position.y:F2} m, célula {CellOf(position)}, nadando={_controller.IsSwimming}, " +
                                      $"subindo={_controller.IsClimbing}, subidas={_controller.Climbs - _climbsBefore}");
                    return;
                }
                case 3:
                {
                    // À tona, encostado numa parede da água funda: não sobe.
                    bool found = false;
                    for (int z = 0; z < d.Height && !found; z++)
                        for (int x = 0; x < d.Width && !found; x++)
                        {
                            if (d.IsSolid(x, z) || d.Depth[x, z] != PoolDepth.Deep || LevelBuilder.IsRoundWell(d, x, z)) continue;
                            foreach (WallSide side in new[] { WallSide.South, WallSide.North, WallSide.West, WallSide.East })
                            {
                                if (d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                                _edgeLow = new Vector2Int(x, z);
                                _edgeHigh = new Vector2Int(nx, nz);
                                found = true;
                                break;
                            }
                        }
                    if (!found) { Finish(false, "não há parede na água funda para conferir que nela não se sobe"); return; }
                    _edgeFace = EdgeMiddle(_edgeLow, _edgeHigh);
                    Vector3 inward = Flat(_edgeFace - _level.CellCenter(_edgeLow.x, _edgeLow.y)).normalized;
                    Vector3 start = _edgeFace - inward * 1.0f;
                    start.y = PoolWater.Current.SurfaceY - _controller.floatDepth;
                    _controller.Teleport(start, Quaternion.LookRotation(inward).eulerAngles.y);
                    _climbsBefore = _controller.Climbs;
                    Press(Key.Space, Key.W);
                    _edgeStep = 4;
                    _stageStart = Now;
                    return;
                }
                case 4:
                    Face(_edgeFace);
                    if (_controller.Climbs != _climbsBefore || _controller.IsClimbing)
                    {
                        Finish(false, $"à tona, empurrando a parede da célula funda {_edgeLow}, subiu nela (pés a {position.y:F2} m)");
                        return;
                    }
                    if (InStage < 3.0) return;
                    if (!_controller.IsSwimming || _controller.IsUnderwater) { Finish(false, $"à tona contra a parede, deixou de boiar (nadando={_controller.IsSwimming}, debaixo d'água={_controller.IsUnderwater})"); return; }
                    Notes.Add("à tona, empurrando uma parede da água funda com Espaço e W, não subiu");
                    _edgeStep = 0;
                    Next();
                    return;
            }
        }

        // Andando no raso contra uma borda sem escada para o seco: não sobe (desce-se, não se sobe).
        static void NoClimb()
        {
            Vector3 position = _local.transform.position;
            if (_edgeStep == 0)
            {
                if (!FindEdge(PoolDepth.Shallow, PoolDepth.Dry, out _edgeLow, out _edgeHigh))
                {
                    Notes.Add("a seed não tem borda sem escada do raso para o seco (conferência de andar contra a borda pulada)");
                    ReleaseKeyboard();
                    Next();
                    return;
                }
                _edgeFace = EdgeMiddle(_edgeLow, _edgeHigh);
                Vector3 inward = Flat(_level.CellCenter(_edgeHigh.x, _edgeHigh.y) - _level.CellCenter(_edgeLow.x, _edgeLow.y)).normalized;
                Vector3 start = _edgeFace - inward * 1.2f;
                start.y = LevelData.DepthFloor(PoolDepth.Shallow) + 0.05f;
                _controller.Teleport(start, Quaternion.LookRotation(inward).eulerAngles.y);
                _climbsBefore = _controller.Climbs;
                Press(Key.W);
                _edgeStep = 1;
                _stageStart = Now;
                return;
            }

            Face(_level.CellCenter(_edgeHigh.x, _edgeHigh.y));
            if (_controller.Climbs != _climbsBefore || _controller.IsClimbing || CellOf(position) == _edgeHigh)
            {
                Finish(false, $"andando no raso contra a borda sem escada, subiu no seco (célula {CellOf(position)}, pés a {position.y:F2} m)");
                return;
            }
            if (InStage < 3.0) return;
            Notes.Add($"andando no raso contra uma borda sem escada, não subiu no seco (pés a {position.y:F2} m)");
            ReleaseKeyboard();
            _edgeStep = 0;
            Next();
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
