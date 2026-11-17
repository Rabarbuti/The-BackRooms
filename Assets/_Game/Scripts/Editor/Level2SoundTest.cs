using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Teste em Play dos passos por superfície nas Piscinas (<see cref="Footsteps"/>): hospeda o
    /// Level 2 e, com um teclado virtual, anda no azulejo seco, anda na água rasa, cai de
    /// 1,2 m na água (um espirro, uma vez), nada à tona num poço e numa célula funda comum,
    /// mergulha e desce uma escada do seco para a água andando (sem espirro) e correndo (um
    /// espirro). Em cada lugar
    /// confere a superfície escolhida (<see cref="Footsteps.Surface"/>) e que tocou o som dela
    /// (<see cref="Footsteps.PlayedOn"/>), e que nada tocou o carpete.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level2SoundTest.BatchRun (-seed N,
    /// padrão 1998; sai com 0 se passou, 3 se falhou; o editor fecha com o Level 0 aberto). Menu:
    /// BackRooms > Testar passos do Level 2. Resultado na linha "[BackRooms] Teste de som do
    /// Level 2 PASSOU/FALHOU".
    /// </summary>
    public static class Level2SoundTest
    {
        const string ActiveKey = "BackRooms.Level2SoundTest.Active";
        const string BatchKey = "BackRooms.Level2SoundTest.Batch";
        const string ExitCodeKey = "BackRooms.Level2SoundTest.ExitCode";
        const string SeedKey = "BackRooms.Level2SoundTest.Seed";
        const double Timeout = 180.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage;
        static double _began, _stageStart;
        static PlayerNetwork _local;
        static PlayerController _controller;
        static Footsteps _feet;
        static LevelBuilder _level;
        static Keyboard _keyboard;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;
        static int _countBefore, _splashesBefore;
        static bool _checkedStill;

        static readonly WallSide[] Sides = { WallSide.North, WallSide.East, WallSide.South, WallSide.West };

        [MenuItem("BackRooms/Testar passos do Level 2")]
        public static void RunFromMenu() { Run(false); }

        public static void BatchRun() { Run(true); }

        static void Run(bool batch)
        {
            EditorSceneManager.OpenScene(Level2Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
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
        static double InStage => Now - _stageStart;

        static void Next()
        {
            _stage++;
            _stageStart = Now;
            _checkedStill = false;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = _stageStart = Now;
            if (Now - _began > Timeout) { Finish(false, $"tempo esgotado na etapa {_stage}"); return; }

            switch (_stage)
            {
                case 0:
                    session.fixedSeed = SessionState.GetInt(SeedKey, 1998);
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1: Begin(session); break;

                case 2: // Azulejo seco.
                    if (!_checkedStill) { if (!Place(PoolDepth.Dry, 0f)) return; }
                    Walk(StepSurface.Tile, "azulejo seco", new[] { Key.W }, 1.6);
                    break;

                case 3: // Água rasa (canela).
                    if (!_checkedStill) { if (!Place(PoolDepth.Shallow, 0f)) return; }
                    Walk(StepSurface.Wade, "água rasa", new[] { Key.W }, 1.6);
                    break;

                case 4: Drop(); break;

                case 5: // Nadando à tona num poço: Espaço segura a cabeça fora d'água, W nada.
                    if (!_checkedStill) { if (!Place(PoolDepth.Well, 0f)) return; }
                    Walk(StepSurface.Swim, "nado à tona no poço", new[] { Key.Space, Key.W }, 4.0);
                    break;

                case 6: // Nadando à tona numa célula funda comum (piscina, raia, canal: não dá pé).
                    if (!_checkedStill) { if (!Place(PoolDepth.Deep, 0f)) return; }
                    Walk(StepSurface.Swim, "nado à tona na funda", new[] { Key.Space, Key.W }, 4.0);
                    break;

                case 7: // Debaixo d'água: dois metros abaixo da posição de boiar, mergulhando.
                    if (!_checkedStill) { if (!Place(PoolDepth.Well, -2f)) return; }
                    Walk(StepSurface.Underwater, "debaixo d'água", new[] { Key.LeftCtrl, Key.W }, 3.0);
                    break;

                case 8: Stair(false); break; // descer a escada andando: sem espirro
                case 9: Stair(true); break;  // descer a escada correndo: um espirro

                case 10:
                    Summary();
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
            if (InStage < 1.5) return;

            _controller = _local.controller;
            _feet = _local.GetComponent<Footsteps>();
            if (_feet == null) { Finish(false, "o jogador não tem Footsteps"); return; }
            if (_level.Data.Depth == null) { Finish(false, "a fase não tem as Piscinas ligadas"); return; }

            // Teclado virtual (como no teste do Level 2): sem as duas configurações o Input System
            // descarta os eventos quando a janela não tem foco.
            _backgroundBefore = InputSystem.settings.backgroundBehavior;
            _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            Press();

            Notes.Add($"nasceu: superfície={_feet.Surface}");
            if (_feet.Surface != StepSurface.Tile) { Finish(false, $"o jogador nasceu no seco e a superfície ficou {_feet.Surface}"); return; }
            Next();
        }

        // Põe o jogador no centro de uma célula da profundidade pedida, de frente para a vizinha
        // aberta mais parecida (mesma profundidade antes), com os pés na altura do piso + offset
        // (no poço: a partir da posição de boiar).
        static bool Place(PoolDepth depth, float offset)
        {
            LevelData d = _level.Data;
            int bestX = -1, bestZ = -1, bestScore = -1;
            WallSide bestSide = WallSide.North;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.Depth[x, z] != depth || (d.Solid != null && d.Solid[x, z])) continue;
                    if (d.SpawnX == x && d.SpawnZ == z && depth != PoolDepth.Dry) continue;
                    foreach (WallSide side in Sides)
                    {
                        if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                        int score = d.Depth[nx, nz] == depth ? 2 : 1;
                        // Depois da vizinha, mais uma da mesma profundidade em linha: espaço para andar.
                        if (score == 2 && d.IsOpen(nx, nz, side) && d.Neighbor(nx, nz, side, out int fx, out int fz) && d.Depth[fx, fz] == depth) score = 3;
                        if (score <= bestScore) continue;
                        bestScore = score; bestX = x; bestZ = z; bestSide = side;
                    }
                }

            if (bestX < 0) { Finish(false, $"a seed {SessionState.GetInt(SeedKey, 1998)} não tem célula {depth} com vizinha aberta"); return false; }

            Vector3 center = _level.CellCenter(bestX, bestZ);
            float y = depth == PoolDepth.Well || depth == PoolDepth.Deep ? PoolWater.Current.SurfaceY - _controller.floatDepth + offset : center.y + 0.05f + offset;
            float yaw = YawOf(bestSide) + _level.transform.eulerAngles.y;
            _controller.RefillAir();
            _controller.Teleport(new Vector3(center.x, y, center.z), yaw);
            _countBefore = _feet.PlayedOn(SurfaceFor(depth, offset));
            _splashesBefore = _feet.Splashes;
            _checkedStill = true;
            _stageStart = Now;
            Notes.Add($"{depth} em ({bestX},{bestZ}) olhando para {bestSide}");
            return true;
        }

        static StepSurface SurfaceFor(PoolDepth depth, float offset)
        {
            switch (depth)
            {
                case PoolDepth.Dry: return StepSurface.Tile;
                case PoolDepth.Shallow: return StepSurface.Wade;
                default: return offset < -1f ? StepSurface.Underwater : StepSurface.Swim;
            }
        }

        static float YawOf(WallSide side)
        {
            switch (side)
            {
                case WallSide.North: return 0f;
                case WallSide.East: return 90f;
                case WallSide.South: return 180f;
                default: return 270f;
            }
        }

        // Parado logo depois de chegar, a superfície já tem de ser a esperada; depois anda (ou
        // nada) segurando as teclas e confere que o som daquela superfície tocou.
        static double _stillChecked;

        static void Walk(StepSurface expected, string where, Key[] keys, double seconds)
        {
            if (_stillChecked != _stageStart)
            {
                if (InStage < 0.25) return;
                if (_feet.Surface != expected)
                {
                    Finish(false, $"{where}: parado, a superfície ficou {_feet.Surface} (esperado {expected}; água acima dos pés {_controller.Submersion:F2} m, nadando={_controller.IsSwimming}, debaixo={_controller.IsUnderwater})");
                    return;
                }
                _stillChecked = _stageStart;
                Press(keys);
                return;
            }

            if (InStage < seconds + 0.25) return;
            Press();

            int played = _feet.PlayedOn(expected) - _countBefore;
            if (played <= 0)
            {
                Finish(false, $"{where}: andou {seconds:F1} s e não tocou nenhum som de {expected} (superfície agora {_feet.Surface}, último som {_feet.LastPlayed})");
                return;
            }
            Notes.Add($"{where}: superfície {expected}, {played} som(ns), barulho do último {_feet.LastNoise:F2}, " +
                      $"espirros {_feet.Splashes - _splashesBefore}, terminou em {_feet.Surface}");
            Next();
        }

        // Cai de 1,2 m na água rasa: os pés cruzam a linha d'água caindo, um espirro e só um.
        // Antes vai para o seco, para o salto até o alto não cruzar a água.
        static double _dropAt;
        const float DropHeight = 1.2f;

        static void Drop()
        {
            if (_dropAt == 0.0)
            {
                if (!Place(PoolDepth.Dry, 0f)) return;
                _dropAt = -1.0;
                return;
            }

            if (_dropAt < 0.0)
            {
                if (InStage < 0.5) return;
                if (!Place(PoolDepth.Shallow, 0f)) return;
                // 1,2 m acima da água: a cabeça fica abaixo de qualquer teto das Piscinas.
                Vector3 p = _local.transform.position;
                _controller.Teleport(new Vector3(p.x, PoolWater.Current.SurfaceY + DropHeight, p.z), _local.transform.eulerAngles.y);
                _splashesBefore = _feet.Splashes;
                _dropAt = Now;
                return;
            }

            if (Now - _dropAt < 2.0) return;
            int splashes = _feet.Splashes - _splashesBefore;
            if (splashes != 1)
            {
                Finish(false, $"caiu de {DropHeight:F1} m na água e tocaram {splashes} espirros (esperado 1; pés a {_local.transform.position.y:F2} m, " +
                              $"água {PoolWater.Current.SurfaceY:F2} m, superfície {_feet.Surface}); até aqui: {string.Join("; ", Notes)}");
                return;
            }
            if (_feet.Surface != StepSurface.Wade) { Finish(false, $"depois de cair na água rasa a superfície ficou {_feet.Surface}"); return; }
            Notes.Add($"caiu de {DropHeight:F1} m na água rasa: um espirro");
            Next();
        }

        // Desce uma escada do seco para a água: andando não espirra; correndo (Shift), um espirro.
        static bool _stairPlaced;

        static void Stair(bool run)
        {
            string where = run ? "descendo a escada correndo" : "descendo a escada andando";
            if (!_stairPlaced)
            {
                LevelData d = _level.Data;
                int found = -1, foundScore = -1;
                for (int k = 0; k < d.Stairs.Count; k++)
                {
                    PoolStair s = d.Stairs[k];
                    if (!d.Neighbor(s.X, s.Z, s.Side, out int ux, out int uz) || d.Depth[ux, uz] != PoolDepth.Dry) continue;
                    PoolDepth below = d.Depth[s.X, s.Z];
                    int score = below == PoolDepth.Deep ? 3 : below == PoolDepth.Shallow ? 2 : 0;
                    if (s.Width >= 3f) score++;
                    if (score > foundScore) { foundScore = score; found = k; }
                }
                if (found < 0 || foundScore < 2) { Finish(false, "a seed não tem escada do seco para a água"); return; }

                PoolStair stair = d.Stairs[found];
                d.Neighbor(stair.X, stair.Z, stair.Side, out int topX, out int topZ);
                Vector3 top = _level.CellCenter(topX, topZ);
                float yaw = YawOf(LevelData.Opposite(stair.Side)) + _level.transform.eulerAngles.y;
                _controller.RefillAir();
                _controller.Teleport(new Vector3(top.x, top.y + 0.05f, top.z), yaw);
                _splashesBefore = _feet.Splashes;
                _stairPlaced = true;
                _stageStart = Now;
                Notes.Add($"escada de ({topX},{topZ}) seco para ({stair.X},{stair.Z}) {d.Depth[stair.X, stair.Z]}");
                return;
            }

            if (InStage < 0.5) return;
            if (!_stillPressed) { Press(run ? new[] { Key.LeftShift, Key.W } : new[] { Key.W }); _stillPressed = true; return; }
            if (InStage < 3.3) return;
            Press();
            _stillPressed = false;
            _stairPlaced = false;

            int splashes = _feet.Splashes - _splashesBefore;
            if (_controller.Submersion <= Footsteps.WetDepth) { Finish(false, $"{where}: não chegou à água (pés a {_local.transform.position.y:F2} m)"); return; }
            if (splashes != (run ? 1 : 0)) { Finish(false, $"{where}: {splashes} espirro(s) (esperado {(run ? 1 : 0)}); superfície {_feet.Surface}"); return; }
            Notes.Add($"{where}: {splashes} espirro(s), terminou em {_feet.Surface}");
            Next();
        }

        static bool _stillPressed;

        static void Summary()
        {
            Press();
            if (_feet.PlayedOn(StepSurface.Carpet) > 0) { Finish(false, $"nas Piscinas tocaram {_feet.PlayedOn(StepSurface.Carpet)} passos de carpete"); return; }
            // Só a queda e a descida correndo espirram: andando (degraus, bordas) e nos
            // teletransportes do teste, não.
            if (_feet.Splashes != 2) { Finish(false, $"tocaram {_feet.Splashes} espirros no teste todo (esperado 2, a queda e a escada correndo): {string.Join("; ", Notes)}"); return; }

            bool order = Footsteps.NoiseOf(StepSurface.Wade) > Footsteps.NoiseOf(StepSurface.Swim) &&
                         Footsteps.NoiseOf(StepSurface.Swim) > Footsteps.NoiseOf(StepSurface.Tile) &&
                         Footsteps.NoiseOf(StepSurface.Tile) > Footsteps.NoiseOf(StepSurface.Underwater);
            if (!order) { Finish(false, "barulho por superfície fora da ordem (raso > nado > seco > debaixo d'água)"); return; }

            Notes.Add($"sons: azulejo={_feet.PlayedOn(StepSurface.Tile)} raso={_feet.PlayedOn(StepSurface.Wade)} nado={_feet.PlayedOn(StepSurface.Swim)} " +
                      $"debaixo={_feet.PlayedOn(StepSurface.Underwater)} espirros={_feet.Splashes} carpete=0");
            Finish(true, string.Join("; ", Notes));
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

            Debug.Log("[BackRooms] Teste de som do Level 2 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

            if (SessionState.GetBool(BatchKey, false))
            {
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
