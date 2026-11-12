using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Teste em Play do Level 2 de teste (Scenes/Level2_Test.unity, As Piscinas): hospeda,
    /// confere que o jogador nasce no seco da sala inicial, anda o caminho a pé até a saída
    /// (descendo e subindo escadas, passando pela água), depois cai num poço e confere que boia
    /// com a cabeça fora, mergulha até acabar o fôlego (afoga e volta à sala inicial) e, por fim,
    /// nada para fora do poço até uma área funda vizinha e fica de pé nela.
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

        [MenuItem("BackRooms/Testar Level 2 (Play)")]
        public static void RunFromMenu() { Run(false); }

        public static void BatchRun() { Run(true); }

        static void Run(bool batch)
        {
            EditorSceneManager.OpenScene(Level2Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
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
                    session.fixedSeed = 1998;
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
                    Float();
                    break;

                case 5:
                    Dive(dt);
                    break;

                case 6:
                    BackToStart();
                    break;

                case 7:
                    SwimOut(dt);
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

            _path = LevelGenerator.FootPath(d);
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

            if (!found) { Finish(false, "a seed 1998 não tem poço com área funda vizinha"); return; }

            Vector3 center = _level.CellCenter(_wellX, _wellZ);
            _controller.Teleport(new Vector3(center.x, PoolWater.Current.SurfaceY - 1f, center.z), 0f);
            Next();
        }

        // Boiando: sem chão, os pés uns 1,4 m abaixo da superfície e a cabeça fora d'água.
        static void Float()
        {
            if (InStage < 2.5) return;

            float surface = PoolWater.Current.SurfaceY;
            float feet = _local.transform.position.y;
            bool floating = _controller.IsSwimming && Mathf.Abs(feet - (surface - _controller.floatDepth)) < 0.3f && !_controller.IsUnderwater;
            if (!floating)
            {
                Finish(false, $"no poço não boiou: nadando={_controller.IsSwimming}, pés a {feet:F2} m, cabeça debaixo d'água={_controller.IsUnderwater}");
                return;
            }

            Notes.Add($"boiou no poço com os pés a {feet - surface:F2} m e a cabeça fora d'água");
            _drownedBefore = _controller.Drownings;
            _controller.airSeconds = 3f;
            _controller.RefillAir();
            Next();
        }

        // Mergulha (empurrando para baixo) até o fôlego acabar e ele se afogar.
        static void Dive(float dt)
        {
            if (_controller.Drownings > _drownedBefore)
            {
                Notes.Add($"afogou com {InStage:F1} s de mergulho (fôlego de {_controller.airSeconds:F0} s)");
                Next();
                return;
            }

            if (InStage > 12.0)
            {
                Finish(false, $"mergulhou 12 s e não se afogou: debaixo d'água={_controller.IsUnderwater}, fôlego={_controller.Air01:F2}, pés a {_local.transform.position.y:F2} m");
                return;
            }

            _body.Move(Vector3.down * 3f * dt);
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

            Vector3 center = _level.CellCenter(_wellX, _wellZ);
            _controller.Teleport(new Vector3(center.x, PoolWater.Current.SurfaceY - _controller.floatDepth, center.z), 0f);
            _lastProgress = Now;
            Next();
        }

        // Nada do poço para a área funda vizinha e tem de ficar de pé nela, fora do nado.
        static void SwimOut(float dt)
        {
            Vector3 position = _local.transform.position;
            Vector2Int cell = CellOf(position);
            Vector3 goal = _level.CellCenter(_deepX, _deepZ);

            if (cell.x == _deepX && cell.y == _deepZ && !_controller.IsSwimming && _body.isGrounded && Mathf.Abs(position.y - goal.y) < 0.15f)
            {
                Notes.Add($"saiu do poço nadando e ficou de pé na área funda (piso a {goal.y:F1} m, água no peito)");
                Finish(true, string.Join("; ", Notes));
                return;
            }

            if (InStage > 15.0)
            {
                Finish(false, $"não conseguiu sair do poço para a área funda: célula {cell}, pés a {position.y:F2} m, nadando={_controller.IsSwimming}");
                return;
            }

            Vector3 toGoal = Flat(goal - position);
            if (toGoal.magnitude > 0.2f) _body.Move(toGoal.normalized * 1.5f * dt);
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
