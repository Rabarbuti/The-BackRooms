using System.Collections.Generic;
using BackRooms.Entities;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Teste em Play do objetivo comum (<see cref="SwitchObjective"/>, PC.1 de mecanicas.md) numa
    /// fase: disjuntores do Level 0, válvulas do Level 2 ou alarmes do Level 3.
    ///
    /// Sozinho: os pontos existem, longe uns dos outros e do início; a porta de saída começa trancada
    /// e não abre com E; mirando um ponto o aviso diz o que fazer; segurando E o jogador faz barulho
    /// (<c>PlayerNetwork.Working</c>) e o ponto arma; com o host contando dois jogadores
    /// (<see cref="SwitchObjective.PlayersForTest"/>) um ponto que passou da janela deixa de valer e
    /// dois armados fora da janela não cumprem; dois dentro da janela cumprem; sozinho de verdade, o
    /// ponto armado não expira; cumprido, a porta abre e a fase acaba. No Level 2, cumprido, a água
    /// desce (a drenagem).
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.ObjectiveTest.BatchRun (Level 0),
    /// BatchRunLevel2, BatchRunLevel3. Sai com 0 se passou, 3 se falhou. Linha
    /// "[BackRooms] Teste do objetivo". Dois editores (Level 0): <see cref="BatchHost"/> arma um
    /// disjuntor quando o cliente chega e <see cref="BatchJoin"/> arma outro dentro da janela; os dois
    /// têm de ver o objetivo cumprido e a porta destrancada.
    /// </summary>
    public static class ObjectiveTest
    {
        const string ActiveKey = "BackRooms.ObjectiveTest.Active";
        const string BatchKey = "BackRooms.ObjectiveTest.Batch";
        const string ExitCodeKey = "BackRooms.ObjectiveTest.ExitCode";
        const string RoleKey = "BackRooms.ObjectiveTest.Role";
        const string SceneKey = "BackRooms.ObjectiveTest.Scene";
        const string SoloRole = "solo", HostRole = "host", JoinRole = "join";
        const string Level0Scene = "Assets/_Game/Scenes/Level0_Test.unity";
        const double Timeout = 200.0;
        const double NetTimeout = 300.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks;
        static double _began, _lastTry;
        static float _stageStart, _waterBefore;
        static bool _finished, _sawWorking;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static SwitchObjective _objective;

        [MenuItem("BackRooms/Testar objetivo (Level 0)")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false, SoloRole, Level0Scene);
        }

        public static void BatchRun() { Run(true, SoloRole, Level0Scene); }
        public static void BatchRunLevel2() { Run(true, SoloRole, Level2Setup.ScenePath); }
        public static void BatchRunLevel3() { Run(true, SoloRole, Level3Setup.ScenePath); }
        public static void BatchHost() { Run(true, HostRole, Level0Scene); }
        public static void BatchJoin() { Run(true, JoinRole, Level0Scene); }

        static void Run(bool batch, string role, string scene)
        {
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(RoleKey, role);
            SessionState.SetString(SceneKey, scene);
            EditorApplication.EnterPlaymode();
        }

        static string Role => SessionState.GetString(RoleKey, SoloRole);
        static string Scene => SessionState.GetString(SceneKey, Level0Scene);

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
            // O cliente tenta entrar antes de o host abrir a sessão e tenta de novo: não é defeito.
            if (Role == JoinRole && message.Contains("Failed to connect to server")) return;
            Errors.Add(message);
        }

        static float Now => Time.time;
        static float InStage => Now - _stageStart;

        static void Next(string note = null)
        {
            if (note != null) Notes.Add(note);
            _stage++;
            _stageStart = Now;
            _stageTicks = 0;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - _began > (Role == SoloRole ? Timeout : NetTimeout))
            {
                Finish(false, $"tempo esgotado na etapa {_stage}");
                return;
            }

            _stageTicks++;
            if (Role == JoinRole) { TickJoin(session); return; }
            if (Role == HostRole) { TickHost(session); return; }

            PlayerController body = _local != null ? _local.controller : null;
            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                {
                    if (!Ready(session)) break;
                    if (_objective == null) { Finish(false, "a fase não tem objetivo (SwitchObjective)"); return; }
                    int expected = _objective.Kind == SwitchKind.Breaker ? 4 : _objective.Kind == SwitchKind.Valve ? 2 : 3;
                    if (_objective.Count != expected) { Finish(false, $"{_objective.Count} pontos de {_objective.Kind} (eram para ser {expected})"); return; }
                    var cells = new List<string>();
                    for (int i = 0; i < _objective.Count; i++)
                    {
                        Vector2Int a = _objective.PointCell(i);
                        cells.Add($"({a.x},{a.y})");
                        if (_level.Data.InRoom(a.x, a.y)) { Finish(false, $"o ponto {i} está na sala inicial"); return; }
                        for (int j = i + 1; j < _objective.Count; j++)
                        {
                            Vector2Int b = _objective.PointCell(j);
                            if (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) < 3) { Finish(false, $"os pontos {i} e {j} estão a menos de 3 células"); return; }
                        }
                    }
                    if (!SwitchObjective.Locked || !LevelExit.Locked) { Finish(false, "a porta de saída não começou trancada pelo objetivo"); return; }

                    LevelExit exit = LevelExit.Instance;
                    Vector3 front = exit.DoorPosition - exit.door.forward * 1.3f;
                    body.Teleport(new Vector3(front.x, exit.DoorPosition.y + 0.05f, front.z), Yaw(exit.door.forward));
                    body.SetPitch(0f);
                    Next($"{_objective.Count} pontos de {_objective.Kind} nas células {string.Join(" ", cells)}, hash {_objective.Hash():X8}");
                    break;
                }

                case 2:
                {
                    LevelExit exit = LevelExit.Instance;
                    if (_stageTicks < 3) break;
                    if (!exit.LocalInReach) { if (InStage > 2f) Finish(false, "de frente para a porta, ela não ficou ao alcance"); break; }
                    if (exit.Interact()) { Finish(false, "a porta trancada pelo objetivo aceitou o E"); return; }
                    string locked = _objective.LockedText;
                    // Sozinho de verdade: o último ponto, armado, não expira.
                    SwitchObjective.PlayersForTest = 0;
                    GoTo(_objective.Count - 1);
                    SwitchObjective.HoldForTest = true;
                    Next($"trancada, a porta não abre (\"{locked.Replace('\n', ' ')}\")");
                    break;
                }

                case 3:
                {
                    int last = _objective.Count - 1;
                    Aim(last);
                    if (_objective.ActiveNow < 1) { if (InStage > _objective.HoldSeconds + 3f) Finish(false, $"sozinho, segurou E no ponto {last} e ele não armou (aviso \"{SwitchObjective.Prompt}\")"); break; }
                    SwitchObjective.HoldForTest = false;
                    if (_objective.PointArmedAt(last) < SwitchObjective.Permanent) { Finish(false, "sozinho, o ponto armado ganhou janela (devia ficar armado)"); return; }
                    if (_objective.Complete) { Finish(false, "sozinho, um ponto só cumpriu o objetivo"); return; }
                    // Daqui em diante o host conta dois jogadores: vale a janela.
                    _objective.ResetForTest();
                    SwitchObjective.PlayersForTest = 2;
                    GoTo(0);
                    Next($"sozinho, o ponto {last} armado ficou armado para sempre");
                    break;
                }

                case 4:
                    // Mirando o ponto 0, sem segurar: o aviso de segurar E.
                    Aim(0);
                    if (_stageTicks < 3) break;
                    if (SwitchObjective.Prompt == null || !SwitchObjective.Prompt.StartsWith("E - HOLD"))
                    {
                        if (InStage > 2f) Finish(false, $"mirando o ponto 0, o aviso foi \"{SwitchObjective.Prompt}\"");
                        break;
                    }
                    SwitchObjective.HoldForTest = true;
                    _sawWorking = false;
                    Next($"mirando o ponto 0, o aviso é \"{SwitchObjective.Prompt.Replace('\n', ' ')}\"");
                    break;

                case 5:
                    // Segurando E: barulho e, depois do tempo, armado (com a janela).
                    Aim(0);
                    if (_local.Working) _sawWorking = true;
                    if (_objective.ActiveNow < 1)
                    {
                        if (InStage > _objective.HoldSeconds + 3f) Finish(false, $"segurou E por {InStage:F1} s no ponto 0 e ele não armou (aviso \"{SwitchObjective.Prompt}\")");
                        break;
                    }
                    SwitchObjective.HoldForTest = false;
                    if (!_sawWorking) { Finish(false, "segurando E o jogador não fez barulho (PlayerNetwork.Working)"); return; }
                    if (InStage < _objective.HoldSeconds - 0.3f) { Finish(false, $"armou em {InStage:F1} s (eram {_objective.HoldSeconds} s)"); return; }
                    if (_objective.Complete) { Finish(false, "um ponto só cumpriu o objetivo"); return; }
                    Next($"segurando E fez barulho e armou o ponto 0 em {InStage:F1} s");
                    break;

                case 6:
                    // Passou da janela: deixa de valer.
                    if (InStage < _objective.WindowSeconds + 0.6f)
                    {
                        if (_objective.ActiveNow < 1 && InStage < _objective.WindowSeconds - 0.6f) { Finish(false, $"o ponto 0 deixou de valer em {InStage:F1} s (a janela é de {_objective.WindowSeconds} s)"); return; }
                        break;
                    }
                    if (_objective.ActiveNow != 0) { Finish(false, "o ponto 0 continuou valendo depois da janela"); return; }
                    GoTo(1);
                    SwitchObjective.HoldForTest = true;
                    Next($"o ponto 0 deixou de valer depois de {_objective.WindowSeconds} s");
                    break;

                case 7:
                    Aim(1);
                    if (_objective.ActiveNow < 1) { if (InStage > _objective.HoldSeconds + 3f) Finish(false, "o ponto 1 não armou"); break; }
                    SwitchObjective.HoldForTest = false;
                    if (_objective.Complete) { Finish(false, "o ponto 0 vencido e o 1 cumpriram o objetivo (fora da janela)"); return; }
                    if (_objective.Arms != 3) { Finish(false, $"{_objective.Arms} armações contadas (eram 3: a de sozinho, a do 0 e a do 1)"); return; }
                    // O outro jogador arma o ponto 0 agora, dentro da janela do 1 (o host recebe o
                    // aviso dele como recebe o de um cliente, sem a conferência de distância).
                    _objective.HostArm(0, null);
                    Next("ponto 1 armado com o 0 vencido: não cumpriu");
                    break;

                case 8:
                    if (!_objective.Complete)
                    {
                        if (InStage > 1f) Finish(false, $"dois pontos dentro da janela não cumpriram (valendo {_objective.ActiveNow})");
                        break;
                    }
                    if (SwitchObjective.Locked || LevelExit.Locked) { Finish(false, "cumprido, a porta continuou trancada"); return; }
                    _waterBefore = PoolWater.Current != null ? PoolWater.Current.SurfaceY : 0f;
                    Next("dois pontos dentro da janela cumpriram o objetivo e destrancaram a porta");
                    break;

                case 9:
                    // Level 2: a água desce (drenagem).
                    if (PoolWater.Current != null)
                    {
                        if (InStage < 6f) break;
                        float drop = _waterBefore - PoolWater.Current.SurfaceY;
                        if (drop < 0.2f) { Finish(false, $"cumprido nas Piscinas, a água desceu só {drop:F2} m em 6 s"); return; }
                        Notes.Add($"a água desceu {drop:F2} m em 6 s");
                    }
                    {
                        LevelExit door = LevelExit.Instance;
                        Vector3 doorFront = door.DoorPosition - door.door.forward * 1.3f;
                        body.Teleport(new Vector3(doorFront.x, door.DoorPosition.y + 0.05f, doorFront.z), Yaw(door.door.forward));
                        body.SetPitch(0f);
                        Next();
                    }
                    break;

                case 10:
                {
                    LevelExit exit = LevelExit.Instance;
                    if (_stageTicks < 3) break;
                    if (!exit.Interact()) { if (InStage > 2f) Finish(false, "destrancada, a porta não aceitou o E"); break; }
                    Next("a porta aceitou o E");
                    break;
                }

                case 11:
                    if (!session.Passing) { if (InStage > 3f) Finish(false, "a porta aceitou o E e a fase não acabou"); break; }
                    Finish(true, string.Join("; ", Notes) + "; a porta abriu e a fase acabou");
                    break;
            }
        }

        static bool Ready(GameSession session)
        {
            if (session.Phase != SessionPhase.Playing) return false;
            _local = PlayerNetwork.Local;
            _level = session.level;
            _objective = SwitchObjective.Instance;
            if (_local == null || _level == null || _level.Data == null) return false;
            return InStage >= 1.5f;
        }

        static void TickHost(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host();
                    Next();
                    break;
                case 1:
                    if (!Ready(session)) break;
                    if (_objective == null || _objective.Count < 2) { Finish(false, "sem objetivo"); return; }
                    if (session.PlayerCount < 2) break;
                    Notes.Add($"cliente entrou; pontos hash {_objective.Hash():X8}");
                    GoTo(0);
                    SwitchObjective.HoldForTest = true;
                    Next();
                    break;
                case 2:
                    Aim(0);
                    if (_objective.ActiveNow < 1) { if (InStage > _objective.HoldSeconds + 3f) Finish(false, "o host não armou o ponto 0"); break; }
                    SwitchObjective.HoldForTest = false;
                    if (_objective.Complete) { Finish(false, "com dois jogadores, um ponto só cumpriu"); return; }
                    Next("host armou o ponto 0 (dois jogadores: com janela)");
                    break;
                case 3:
                    if (!_objective.Complete) { if (InStage > 40f) Finish(false, "o cliente não armou o ponto 1 a tempo"); break; }
                    if (LevelExit.Locked) { Finish(false, "cumprido, a porta continuou trancada no host"); return; }
                    Next("o cliente armou o ponto 1 dentro da janela: cumprido no host");
                    break;
                case 4:
                    if (InStage < 8f) break;
                    Finish(true, string.Join("; ", Notes));
                    break;
            }
        }

        static void TickJoin(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    if (EditorApplication.timeSinceStartup - _began < 12.0) break;
                    if (session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null) { Next(); break; }
                    if (session.Phase == SessionPhase.Menu && EditorApplication.timeSinceStartup - _lastTry > 4.0)
                    {
                        _lastTry = EditorApplication.timeSinceStartup;
                        session.Join("127.0.0.1");
                    }
                    break;
                case 1:
                    if (!Ready(session)) break;
                    if (_objective == null) { Finish(false, "o cliente não montou o objetivo"); return; }
                    Notes.Add($"pontos hash {_objective.Hash():X8}");
                    GoTo(1);
                    Next();
                    break;
                case 2:
                    // Espera o host armar o ponto 0 (o estado chega pela rede) e arma o 1.
                    Aim(1);
                    if (_objective.ActiveNow < 1) { if (InStage > 60f) Finish(false, "o cliente nunca viu o ponto 0 armado"); break; }
                    SwitchObjective.HoldForTest = true;
                    Next("o cliente viu o ponto 0 armado pelo host");
                    break;
                case 3:
                    Aim(1);
                    if (!_objective.Complete) { if (InStage > _objective.HoldSeconds + 6f) Finish(false, "o cliente segurou E no ponto 1 e o objetivo não se cumpriu"); break; }
                    SwitchObjective.HoldForTest = false;
                    if (LevelExit.Locked) { Finish(false, "cumprido, a porta continuou trancada no cliente"); return; }
                    Finish(true, string.Join("; ", Notes) + "; o cliente armou o ponto 1 e viu o objetivo cumprido e a porta destrancada");
                    break;
            }
        }

        // Põe o jogador de frente para o ponto, a 1,1 m, e mira nele.
        static void GoTo(int index)
        {
            Vector3 at = _objective.PointPosition(index);
            Vector3 facing = _objective.PointFacing(index);
            Vector3 stand = at + facing * 1.1f;
            float floor = at.y - _objective.MountHeight;
            _local.controller.Teleport(new Vector3(stand.x, floor + 0.05f, stand.z), Yaw(-facing));
            Aim(index);
        }

        static void Aim(int index)
        {
            Vector3 target = _objective.PointPosition(index) + _objective.PointFacing(index) * 0.08f;
            Transform eye = _local.playerCamera.transform;
            Vector3 to = target - eye.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude < 0.0001f) return;
            _local.transform.rotation = Quaternion.Euler(0f, Yaw(flat), 0f);
            _local.controller.SetPitch(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
        }

        static float Yaw(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;

            SwitchObjective.HoldForTest = false;
            SwitchObjective.PlayersForTest = 0;
            EntityDirector.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log($"[BackRooms] Teste do objetivo ({System.IO.Path.GetFileNameWithoutExtension(Scene)}, {Role}) " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

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
