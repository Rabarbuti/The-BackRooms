using System.Collections.Generic;
using BackRooms.Entities;
using BackRooms.Net;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Teste em Play dos botões dos elevadores do Escritório (<see cref="ElevatorControl"/>), mirando
    /// de verdade (câmera virada para o botão e E simulado):
    /// - sozinho (<see cref="BatchRun"/>): 9 botões; na cabine do início, a mira mostra o aviso e
    ///   "fechar" fecha, "abrir" abre; fechando com alguém no vão, a porta reabre e só fecha quando o vão
    ///   fica livre; com a trava <see cref="ElevatorDoors.HeldOpen"/> (o inimigo que chega de elevador)
    ///   o botão não fecha; num poço, o botão de chamada acende a cabine, a porta abre depois da
    ///   chegada e fecha sozinha; de dentro do poço fechado, "abrir" abre e "fechar" fecha.
    /// - dois editores: <see cref="BatchHost"/> fecha a cabine antes de o cliente entrar e espera;
    ///   <see cref="BatchJoin"/> entra depois, tem de achar a cabine fechada, chama um poço e vê a
    ///   porta abrir (o host decide); o host tem de ver a mesma porta abrir.
    ///
    /// Linha "[BackRooms] Teste dos elevadores (papel) PASSOU/FALHOU". Sai com 0 se passou, 3 se falhou.
    /// </summary>
    public static class Level3ElevatorTest
    {
        const string ActiveKey = "BackRooms.Level3ElevatorTest.Active";
        const string BatchKey = "BackRooms.Level3ElevatorTest.Batch";
        const string ExitCodeKey = "BackRooms.Level3ElevatorTest.ExitCode";
        const string RoleKey = "BackRooms.Level3ElevatorTest.Role";
        const string SoloRole = "solo", HostRole = "host", JoinRole = "join";
        const double SoloTimeout = 120.0, NetTimeout = 200.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage;
        static double _began, _stageStart, _lastTick, _lastTry, _pressedAt;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static ElevatorControl _control;
        static ElevatorDoors _cab, _shaft;
        static ElevatorButton _target;
        static float _minOpen;
        static int _presses;
        static bool _finished;

        [MenuItem("BackRooms/Testar elevadores do Level 3")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false, SoloRole);
        }

        public static void BatchRun() => Run(true, SoloRole);
        public static void BatchHost() => Run(true, HostRole);
        public static void BatchJoin() => Run(true, JoinRole);

        static string Role => SessionState.GetString(RoleKey, SoloRole);

        static void Run(bool batch, string role)
        {
            if (!System.IO.File.Exists(Level3Setup.ScenePath)) Level3Setup.BuildScene();
            EditorSceneManager.OpenScene(Level3Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(RoleKey, role);
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
            if (Role == JoinRole && message.Contains("Failed to connect to server")) return;
            Errors.Add(message);
        }

        static double Now => EditorApplication.timeSinceStartup;
        static double InStage => Now - _stageStart;

        static void Next(string note = null)
        {
            if (note != null) Notes.Add(note);
            _stage++;
            _stageStart = Now;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || _finished) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = _lastTick = Now;
            float dt = Mathf.Clamp((float)(Now - _lastTick), 0f, 0.1f);
            _lastTick = Now;
            if (OfficeElevators.ForceSeconds >= 0.0) OfficeElevators.ForceSeconds += dt;

            if (Now - _began > (Role == SoloRole ? SoloTimeout : NetTimeout))
            {
                Finish(false, $"tempo esgotado na etapa {_stage}");
                return;
            }

            if (Role == JoinRole) TickJoin(session);
            else if (Role == HostRole) TickHost(session);
            else TickSolo(session);
        }

        // ---------------------------------------------------------------- Sozinho

        static void TickSolo(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    LevelProgress.DevModeOverride = true;
                    EntityDirector.Paused = true;
                    OfficeLightWave.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                    if (!Ready(session)) break;
                    if (_control.Buttons.Count != 9) { Finish(false, $"{_control.Buttons.Count} botões de elevador (eram para ser 9)"); return; }
                    if (_cab.Openness < 0.99f) { if (InStage > 15) Finish(false, "a porta da cabine não abriu sozinha"); break; }
                    GoTo(Button(_cab, ElevatorButtonKind.Close));
                    Next("9 botões; a cabine do início abriu sozinha");
                    break;

                case 2:
                    if (!PressWhenAimed(ElevatorControl.CloseText)) break;
                    Next();
                    break;

                case 3:
                    if (_cab.Openness > 0.01f) { if (InStage > 4) Finish(false, $"apertou fechar e a cabine não fechou (abertura {_cab.Openness:F2})"); break; }
                    GoTo(Button(_cab, ElevatorButtonKind.Open));
                    Next("mira + E em CLOSE DOOR fechou a cabine");
                    break;

                case 4:
                    if (!PressWhenAimed(ElevatorControl.OpenText)) break;
                    Next();
                    break;

                case 5:
                    if (_cab.Openness < 0.99f) { if (InStage > 4) Finish(false, $"apertou abrir e a cabine não abriu ({_cab.Openness:F2})"); break; }
                    GoTo(Button(_cab, ElevatorButtonKind.Close));
                    Next("mira + E em OPEN DOOR abriu");
                    break;

                case 6:
                    // Fecha e, assim que o host aceita, alguém fica no vão.
                    if (!PressWhenAimed(ElevatorControl.CloseText)) break;
                    _presses = _control.HostPresses;
                    Next();
                    break;

                case 7:
                    if (_control.HostPresses == _presses) { if (InStage > 3) Finish(false, "o fechar não chegou ao host"); break; }
                    StandInDoorway(_cab);
                    _minOpen = 1f;
                    Next();
                    break;

                case 8:
                    _minOpen = Mathf.Min(_minOpen, _cab.Openness);
                    if (InStage < 3.0) break;
                    if (_minOpen < 0.5f || !_cab.WantsOpen) { Finish(false, $"fechou com o jogador no vão (abertura mínima {_minOpen:F2})"); return; }
                    GoTo(Button(_cab, ElevatorButtonKind.Open));
                    Next($"com o jogador no vão a porta reabriu (abertura mínima {_minOpen:F2})");
                    break;

                case 9:
                    if (_cab.Openness > 0.01f) { if (InStage > 4) Finish(false, $"o vão ficou livre e a porta não fechou ({_cab.Openness:F2})"); break; }
                    Next("vão livre: a porta terminou de fechar");
                    break;

                case 10:
                    if (!PressWhenAimed(ElevatorControl.OpenText)) break;
                    Next();
                    break;

                case 11:
                    if (_cab.Openness < 0.99f) { if (InStage > 4) Finish(false, "não reabriu para o teste da trava"); break; }
                    _cab.HeldOpen = true;
                    _presses = _control.HostPresses;
                    GoTo(Button(_cab, ElevatorButtonKind.Close));
                    Next();
                    break;

                case 12:
                    if (!PressWhenAimed(ElevatorControl.HeldText)) break;
                    Next();
                    break;

                case 13:
                    if (_cab.Openness < 0.99f || !_cab.WantsOpen) { Finish(false, "com a trava (inimigo fora da cabine) o botão fechou a porta"); return; }
                    if (InStage < 2.5) break;
                    if (_control.HostPresses != _presses) { Finish(false, "com a trava o host aceitou o fechar"); return; }
                    _cab.HeldOpen = false;
                    // Poço: relógio forçado fora de visita.
                    OfficeElevators.ForceSeconds = 10.0;
                    GoTo(Button(_shaft, ElevatorButtonKind.Call));
                    Next("com a trava HeldOpen o aviso é THE DOOR WON'T CLOSE e a porta fica aberta");
                    break;

                case 14:
                    if (_shaft.Openness > 0.01f || _shaft.CabLit) { Finish(false, "o poço já estava aberto ou aceso antes do chamado"); return; }
                    if (!PressWhenAimed(ElevatorControl.CallText)) break;
                    _pressedAt = Now;
                    Next();
                    break;

                case 15:
                {
                    double since = Now - _pressedAt;
                    if (since < 1.0 && _shaft.Openness > 0.05f) { Finish(false, "o poço abriu antes de o elevador chegar"); return; }
                    if (_shaft.Openness < 0.99f) { if (since > ElevatorControl.CallDelay + 4.0) Finish(false, $"chamado, o poço não abriu ({_shaft.Openness:F2})"); break; }
                    if (!_shaft.CabLit) { Finish(false, "o poço abriu com a cabine apagada"); return; }
                    if (!ElevatorControl.IsCalled(_shaft)) { Finish(false, "o botão de chamada não ficou aceso"); return; }
                    Next($"chamado, o poço acendeu e abriu {since:F1} s depois");
                    break;
                }

                case 16:
                    if (_shaft.Openness > 0.01f) { if (InStage > ElevatorControl.HoldSeconds + 4.0) Finish(false, "o poço chamado não fechou sozinho"); break; }
                    Next($"e fechou sozinho depois de {InStage:F0} s");
                    // De dentro do poço fechado: abrir.
                    GoTo(Button(_shaft, ElevatorButtonKind.Open));
                    break;

                case 17:
                    if (!PressWhenAimed(ElevatorControl.OpenText)) break;
                    Next();
                    break;

                case 18:
                    if (_shaft.Openness < 0.99f) { if (InStage > 4) Finish(false, "de dentro do poço fechado, abrir não abriu"); break; }
                    GoTo(Button(_shaft, ElevatorButtonKind.Close));
                    Next("de dentro do poço fechado, OPEN DOOR abriu (ninguém fica preso)");
                    break;

                case 19:
                    if (!PressWhenAimed(ElevatorControl.CloseText)) break;
                    Next();
                    break;

                case 20:
                    if (_shaft.Openness > 0.01f) { if (InStage > 4) Finish(false, "de dentro do poço, fechar não fechou"); break; }
                    Finish(true, string.Join("; ", Notes) + "; de dentro do poço, CLOSE DOOR fechou");
                    break;
            }
        }

        // ---------------------------------------------------------------- Dois editores

        static void TickHost(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    OfficeLightWave.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host();
                    Next();
                    break;

                case 1:
                    if (!Ready(session)) break;
                    if (_cab.Openness < 0.99f) break;
                    GoTo(Button(_cab, ElevatorButtonKind.Close));
                    Next();
                    break;

                case 2:
                    if (!PressWhenAimed(ElevatorControl.CloseText)) break;
                    Next();
                    break;

                case 3:
                    if (_cab.Openness > 0.01f) { if (InStage > 4) Finish(false, "o host não fechou a cabine"); break; }
                    // Sai da cabine (o cliente nasce nela) e espera.
                    GoTo(Button(_shaft, ElevatorButtonKind.Call));
                    _presses = _control.HostPresses;
                    Next("o host fechou a cabine antes de o cliente entrar");
                    break;

                case 4:
                    if (session.PlayerCount < 2) break;
                    Next("o cliente entrou");
                    break;

                case 5:
                    // O cliente aperta o chamado: o host aceita e o poço abre aqui também.
                    if (_control.HostPresses == _presses || _shaft.Openness < 0.99f)
                    {
                        if (InStage > 90) Finish(false, $"o chamado do cliente não abriu o poço no host (apertos {_control.HostPresses - _presses}, abertura {_shaft.Openness:F2})");
                        break;
                    }
                    Next("o chamado do cliente chegou ao host e o poço abriu nele");
                    break;

                case 6:
                    if (InStage < 6) break;
                    Finish(true, string.Join("; ", Notes));
                    break;
            }
        }

        static void TickJoin(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    if (Now - _began < 12.0) break;
                    if (session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null) { Next(); break; }
                    if (session.Phase == SessionPhase.Menu && Now - _lastTry > 4.0)
                    {
                        _lastTry = Now;
                        session.Join("127.0.0.1");
                    }
                    break;

                case 1:
                    if (!Ready(session)) break;
                    if (InStage < 3.0) break;
                    if (_control.StatesReceived < 1) { if (InStage > 10) Finish(false, "o cliente não recebeu o estado dos elevadores"); break; }
                    if (_cab.Openness > 0.01f) { Finish(false, $"quem entrou depois achou a cabine aberta ({_cab.Openness:F2}); o host a tinha fechado"); return; }
                    GoTo(Button(_shaft, ElevatorButtonKind.Call));
                    Next("quem entrou depois achou a cabine fechada pelo host");
                    break;

                case 2:
                    if (!PressWhenAimed(ElevatorControl.CallText)) break;
                    Next();
                    break;

                case 3:
                    if (_shaft.Openness < 0.99f) { if (InStage > ElevatorControl.CallDelay + 6.0) Finish(false, $"o cliente chamou e o poço não abriu nele ({_shaft.Openness:F2})"); break; }
                    Finish(true, string.Join("; ", Notes) + $"; o cliente chamou um poço e a porta abriu ({InStage:F1} s)");
                    break;
            }
        }

        // ---------------------------------------------------------------- Ajudantes

        static bool Ready(GameSession session)
        {
            if (session.Phase != SessionPhase.Playing) return false;
            _local = PlayerNetwork.Local;
            _level = Object.FindAnyObjectByType<LevelBuilder>();
            _control = ElevatorControl.Instance;
            if (_local == null || _level == null || _level.Data == null || _control == null || OfficeElevators.Instance == null) return false;
            _cab = _shaft = null;
            foreach (ElevatorDoors door in OfficeElevators.Instance.Doors)
            {
                if (door.IsCab) _cab = door;
                else if (_shaft == null) _shaft = door;
            }
            if (_cab == null || _shaft == null) { Finish(false, "faltam a cabine ou um poço"); return false; }
            return InStage >= 1.5;
        }

        static ElevatorButton Button(ElevatorDoors door, ElevatorButtonKind kind)
        {
            foreach (ElevatorButton b in _control.Buttons)
                if (b.Door == door && b.Kind == kind) return b;
            return null;
        }

        // De frente para o botão, a 0,7 m.
        static void GoTo(ElevatorButton button)
        {
            _target = button;
            if (button == null) { Finish(false, "botão não encontrado"); return; }
            Vector3 at = button.transform.position + button.transform.forward * 0.7f;
            _local.controller.Teleport(new Vector3(at.x, _level.transform.position.y + 0.05f, at.z), Yaw(-button.transform.forward));
        }

        // Mira no botão; depois de um instante confere o aviso e aperta E.
        static bool PressWhenAimed(string prompt)
        {
            if (_target == null) return false;
            Transform eye = _local.playerCamera.transform;
            Vector3 to = _target.transform.position - eye.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            _local.transform.rotation = Quaternion.Euler(0f, Yaw(flat), 0f);
            _local.controller.SetPitch(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
            if (InStage < 0.5) return false;
            if (ElevatorControl.Prompt != prompt)
            {
                if (InStage > 4) Finish(false, $"mirando o botão {_target.Kind}, o aviso foi \"{ElevatorControl.Prompt}\" e não \"{prompt}\"");
                return false;
            }
            ElevatorControl.PressForTest = true;
            return true;
        }

        static void StandInDoorway(ElevatorDoors door)
        {
            Vector3 at = door.transform.position;
            _local.controller.Teleport(new Vector3(at.x, _level.transform.position.y + 0.05f, at.z), 0f);
        }

        static float Yaw(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;
            OfficeElevators.ForceSeconds = -1.0;
            OfficeLightWave.Paused = false;
            EntityDirector.Paused = false;
            if (_cab != null) _cab.HeldOpen = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
            LevelProgress.DevModeOverride = null;

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log($"[BackRooms] Teste dos elevadores ({Role}) " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

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
