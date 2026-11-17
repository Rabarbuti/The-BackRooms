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
    /// Teste em Play do objetivo do Level 1, "fechar o caixa" (level1-mostruario.md, seção 6):
    /// três etiquetas vermelhas em blocos diferentes e longe umas das outras, com os dígitos do
    /// código na ordem; o bilhete na sala inicial; a porta trancada não abre; mirando uma tecla do
    /// teclado o aviso diz qual é; um código errado não destranca; o certo, digitado tecla por
    /// tecla com a mira e o E, destranca; e a porta abre.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level1ObjectiveTest.BatchRun (sai com
    /// 0 se passou, 3 se falhou). Menu: BackRooms > Testar objetivo do Level 1. Linha
    /// "[BackRooms] Teste do objetivo do Level 1". Dois editores: <see cref="BatchHost"/> destranca
    /// e espera um cliente; <see cref="BatchJoin"/> entra depois e tem de achar a porta destrancada.
    /// </summary>
    public static class Level1ObjectiveTest
    {
        const string ActiveKey = "BackRooms.Level1ObjectiveTest.Active";
        const string BatchKey = "BackRooms.Level1ObjectiveTest.Batch";
        const string ExitCodeKey = "BackRooms.Level1ObjectiveTest.ExitCode";
        const string RoleKey = "BackRooms.Level1ObjectiveTest.Role";
        const string SoloRole = "solo", HostRole = "host", JoinRole = "join";
        const double Timeout = 120.0;
        const double NetTimeout = 300.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks, _typedIndex;
        static double _began, _lastTry;
        static float _stageStart;
        static bool _finished, _waitingForKey;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static ExitLock _lock;
        static string[] _sequence;

        [MenuItem("BackRooms/Testar objetivo do Level 1")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false, SoloRole);
        }

        public static void BatchRun() { Run(true, SoloRole); }
        public static void BatchHost() { Run(true, HostRole); }
        public static void BatchJoin() { Run(true, JoinRole); }

        static void Run(bool batch, string role)
        {
            EditorSceneManager.OpenScene(Level1Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(RoleKey, role);
            EditorApplication.EnterPlaymode();
        }

        static string Role => SessionState.GetString(RoleKey, SoloRole);

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

            if (Role == JoinRole)
            {
                TickJoin(session);
                return;
            }

            _stageTicks++;
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
                    if (session.Phase != SessionPhase.Playing) break;
                    _local = PlayerNetwork.Local;
                    _level = session.level;
                    _lock = ExitLock.Instance;
                    if (_local == null || _level == null || _level.Data == null) break;
                    if (InStage < 1.5f) break;
                    if (_lock == null) { Finish(false, "o Level 1 não tem a porta trancada (ExitLock)"); return; }

                    var props = _level.GetComponent<PropLayer>();
                    if (props.Tags != 3) { Finish(false, $"{props.Tags} etiquetas vermelhas (eram para ser 3)"); return; }
                    for (int i = 0; i < 3; i++)
                        for (int j = i + 1; j < 3; j++)
                        {
                            Vector2Int a = props.TagCells[i], b = props.TagCells[j];
                            if (new Vector2Int(a.x / 2, a.y / 2) == new Vector2Int(b.x / 2, b.y / 2)) { Finish(false, $"etiquetas {i + 1} e {j + 1} no mesmo bloco"); return; }
                            if (Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)) < 3) { Finish(false, $"etiquetas {i + 1} e {j + 1} a menos de 3 células"); return; }
                        }
                    for (int i = 1; i <= 3; i++)
                    {
                        Transform tag = _level.transform.Find("Moveis/Etiqueta_" + i);
                        if (tag == null) { Finish(false, $"a etiqueta {i} não está na fase"); return; }
                        var texture = tag.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_BaseColorMap");
                        if (texture == null) { Finish(false, $"a etiqueta {i} está sem desenho"); return; }
                    }
                    if (_level.transform.Find("Moveis/Bilhete") == null) { Finish(false, "sem o bilhete na sala inicial"); return; }

                    // De frente para a porta, perto: trancada, E não abre.
                    LevelExit exit = LevelExit.Instance;
                    Vector3 front = exit.DoorPosition - exit.door.forward * 1.3f;
                    body.Teleport(new Vector3(front.x, exit.DoorPosition.y, front.z), Yaw(exit.door.forward));
                    body.SetPitch(0f);
                    Next($"3 etiquetas nas células {string.Join(", ", props.TagCells)}, código {_lock.Code:000}, bilhete na sala inicial");
                    break;
                }

                case 2:
                {
                    LevelExit exit = LevelExit.Instance;
                    if (_stageTicks < 3) break;
                    if (!exit.LocalInReach) { if (InStage > 2f) Finish(false, "de frente para a porta, ela não ficou ao alcance"); break; }
                    if (exit.Interact()) { Finish(false, "a porta trancada aceitou o E"); return; }

                    // Mira a tecla 9 do teclado.
                    AimAt("9");
                    Next("trancada, a porta não abre com E");
                    break;
                }

                case 3:
                    AimAt("9");
                    if (ExitLock.Prompt != "E - KEY 9")
                    {
                        if (InStage > 2f) Finish(false, $"mirando a tecla 9, o aviso foi \"{ExitLock.Prompt}\"");
                        break;
                    }
                    // Código errado, apertando direto: não destranca.
                    int wrong = (_lock.Code + 1) % 1000;
                    foreach (char c in wrong.ToString("000")) _lock.Press(c.ToString());
                    _lock.Press("OK");
                    if (_lock.Unlocked || _lock.WrongCodes != 1) { Finish(false, $"código errado: destrancou={_lock.Unlocked}, erros contados={_lock.WrongCodes}"); return; }

                    // O certo, com a mira e o E, tecla por tecla.
                    var keys = new List<string>();
                    foreach (char c in _lock.Code.ToString("000")) keys.Add(c.ToString());
                    keys.Add("OK");
                    _sequence = keys.ToArray();
                    _typedIndex = 0;
                    _waitingForKey = false;
                    Next("mirando a tecla 9, o aviso é E - KEY 9; código errado não destrancou");
                    break;

                case 4:
                {
                    if (_typedIndex >= _sequence.Length)
                    {
                        if (!_lock.Unlocked) { if (InStage > 2f) Finish(false, $"digitou o código certo ({_lock.Code:000}) com a mira e não destrancou: no teclado \"{_lock.Typed}\", códigos errados {_lock.WrongCodes}"); break; }
                        Next("o código certo, digitado tecla por tecla com a mira e o E, destrancou");
                        break;
                    }
                    string key = _sequence[_typedIndex];
                    AimAt(key);
                    if (!_waitingForKey)
                    {
                        if (ExitLock.Prompt == null || !ExitLock.Prompt.Contains(key == "OK" ? "ENTER" : key)) { if (InStage > 3f) Finish(false, $"mirando a tecla {key}, o aviso foi \"{ExitLock.Prompt}\""); break; }
                        ExitLock.PressForTest = true;
                        _waitingForKey = true;
                        break;
                    }
                    if (!ExitLock.PressForTest)
                    {
                        _typedIndex++;
                        _waitingForKey = false;
                        _stageStart = Now;
                    }
                    break;
                }

                case 5:
                {
                    LevelExit exit = LevelExit.Instance;
                    LevelExit door = exit;
                    Vector3 front = door.DoorPosition - door.door.forward * 1.3f;
                    body.Teleport(new Vector3(front.x, door.DoorPosition.y, front.z), Yaw(door.door.forward));
                    body.SetPitch(0f);
                    if (Role == HostRole)
                    {
                        Next();
                        break;
                    }
                    if (_stageTicks < 3) break;
                    if (!door.Interact()) { if (InStage > 2f) Finish(false, "destrancada, a porta não aceitou o E"); break; }
                    Finish(true, string.Join("; ", Notes) + "; destrancada, a porta abriu");
                    break;
                }

                case 6:
                    // Dois editores: espera o cliente entrar (depois de destrancada) e conferir.
                    if (InStage < 25f) break;
                    Finish(true, string.Join("; ", Notes) + "; esperou o cliente 25 s");
                    break;
            }
        }

        static void TickJoin(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    // Entra só depois de o host destrancar (uns 15 s): é o caso de quem chega tarde.
                    if (EditorApplication.timeSinceStartup - _began < 20.0) break;
                    if (session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null) { Next(); break; }
                    if (session.Phase == SessionPhase.Menu && EditorApplication.timeSinceStartup - _lastTry > 4.0)
                    {
                        _lastTry = EditorApplication.timeSinceStartup;
                        session.Join("127.0.0.1");
                    }
                    break;

                case 1:
                    if (ExitLock.Instance != null && ExitLock.Instance.Unlocked)
                    {
                        Finish(true, $"entrou depois e achou a porta destrancada (código {ExitLock.Instance.Code:000})");
                        return;
                    }
                    if (InStage > 15f) Finish(false, "entrou depois e a porta continuou trancada para ele");
                    break;
            }
        }

        // Vira o jogador para a tecla e inclina a câmera até ela.
        static void AimAt(string key)
        {
            Transform keypad = _level.transform.Find("Saida/Teclado");
            Transform target = keypad != null ? keypad.Find("Tecla_" + key) : null;
            if (target == null) return;
            Transform eye = _local.playerCamera.transform;
            Vector3 to = target.position - eye.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            _local.transform.rotation = Quaternion.Euler(0f, Yaw(flat), 0f);
            _local.controller.SetPitch(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
        }

        static float Yaw(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;

            EntityDirector.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log("[BackRooms] Teste do objetivo do Level 1 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

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
