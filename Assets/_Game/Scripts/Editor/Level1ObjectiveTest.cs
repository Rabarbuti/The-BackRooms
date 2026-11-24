using System.Collections.Generic;
using System.IO;
using BackRooms.Entities;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.UI;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Teste em Play do objetivo do Level 1, "fechar o caixa" (level1-mostruario.md, seção 6):
    /// três etiquetas vermelhas em blocos diferentes e longe umas das outras, com os dígitos do
    /// código na ordem; nenhum bilhete do código na sala inicial (saiu em 21/11/2026), o cartaz de
    /// dicas do Capitão lá, sem dígito nem CLEARANCE, e a placa amarela nos totens; a porta trancada não abre. Depois, o painel do código (<see cref="CodePanel"/>), com
    /// teclado e mouse virtuais: mirando o teclado o aviso é E - KEYPAD e o E abre o painel; aberto,
    /// o jogador não anda nem gira e a tecla 1 digita em vez de puxar a caneta; Backspace apaga; um
    /// código errado pisca o visor, não destranca e limpa; Esc fecha (sem abrir o menu de pausa) e o
    /// jogador volta a andar; o E também fecha; o certo, um dígito clicado com o mouse, um pelo
    /// teclado numérico e um pela fileira de cima, com Enter, destranca e fecha; e a porta abre.
    /// Fotos do painel em Logs/Previews/level1_placas/painel_*.png.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level1ObjectiveTest.BatchRun (sai com
    /// 0 se passou, 3 se falhou). Menu: BackRooms > Testar objetivo do Level 1. Linha
    /// "[BackRooms] Teste do objetivo do Level 1". Dois editores: <see cref="BatchHost"/> +
    /// <see cref="BatchJoin"/> (o cliente digita no painel, primeiro errado e depois certo, e o
    /// host destranca pelo código dele) e <see cref="BatchHostLate"/> + <see cref="BatchJoinLate"/>
    /// (o host destranca e o cliente entra depois e tem de achar a porta destrancada).
    /// </summary>
    public static class Level1ObjectiveTest
    {
        const string ActiveKey = "BackRooms.Level1ObjectiveTest.Active";
        const string BatchKey = "BackRooms.Level1ObjectiveTest.Batch";
        const string ExitCodeKey = "BackRooms.Level1ObjectiveTest.ExitCode";
        const string RoleKey = "BackRooms.Level1ObjectiveTest.Role";
        const string SoloRole = "solo", HostRole = "host", JoinRole = "join", HostLateRole = "hostlate", JoinLateRole = "joinlate";
        const string KeypadPrompt = "E - KEYPAD";
        const double Timeout = 150.0;
        const double NetTimeout = 300.0;
        const float StepTimeout = 4f;
        const string PhotoFolder = "Logs/Previews/level1_placas";

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks;
        static double _began, _lastTry;
        static float _stageStart;
        static bool _finished;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static ExitLock _lock;

        // O roteiro do painel: um passo por vez, cada um chamado a cada quadro até devolver true.
        static List<(string what, System.Func<bool> act)> _steps;
        static int _step, _stepTicks;
        static float _stepStart;
        static Keyboard _keyboard;
        static Mouse _mouse;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;
        static Vector3 _from;
        static float _yawFrom, _pitchFrom;
        static int _rattlesFrom;

        // Foto do painel: o documento desenha numa textura enquanto isso.
        static UIDocument _photoDocument;
        static PanelSettings _photoOriginal, _photoPanel;
        static RenderTexture _photoUi;

        [MenuItem("BackRooms/Testar objetivo do Level 1")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false, SoloRole);
        }

        public static void BatchRun() { Run(true, SoloRole); }
        public static void BatchHost() { Run(true, HostRole); }
        public static void BatchJoin() { Run(true, JoinRole); }
        public static void BatchHostLate() { Run(true, HostLateRole); }
        public static void BatchJoinLate() { Run(true, JoinLateRole); }

        static void Run(bool batch, string role)
        {
            EditorSceneManager.OpenScene(Level1Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(RoleKey, role);
            EditorApplication.EnterPlaymode();
        }

        static string Role => SessionState.GetString(RoleKey, SoloRole);
        static bool Joining => Role == JoinRole || Role == JoinLateRole;

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
        static float InStep => Now - _stepStart;

        static void Next(string note = null)
        {
            if (note != null) Notes.Add(note);
            _stage++;
            _stageStart = Now;
            _stageTicks = 0;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || _finished) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - _began > (Role == SoloRole ? Timeout : NetTimeout))
            {
                Finish(false, $"tempo esgotado na etapa {_stage}" + (_steps != null && _step < _steps.Count ? $" (passo \"{_steps[_step].what}\")" : ""));
                return;
            }

            if (Role == JoinLateRole) { TickJoinLate(session); return; }
            if (Role == JoinRole) { TickJoin(session); return; }

            _stageTicks++;
            PlayerController body = _local != null ? _local.controller : null;

            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    PlayerController.AssumeMouseCaptured = true;
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
                    // O bilhete com os dígitos fora de ordem saiu em 21/11/2026: não pode voltar.
                    if (_level.transform.Find("Moveis/Bilhete") != null) { Finish(false, "o bilhete do código voltou à sala inicial"); return; }
                    // O cartaz de dicas do Capitão (23/11/2026) fica na sala inicial, mas sem dígito nem
                    // CLEARANCE: não pode dar o código nem parecer uma das etiquetas.
                    StartNote note = StartNote.Current;
                    if (note == null || _level.transform.Find("Cartaz/CartazDeDicas") == null) { Finish(false, "sem o cartaz de dicas do Capitão na sala inicial"); return; }
                    string noteText = string.Join(" / ", note.Lines);
                    foreach (char c in noteText)
                        if (char.IsDigit(c)) { Finish(false, $"o cartaz de dicas tem um dígito: {noteText}"); return; }
                    if (noteText.Contains("CLEARANCE")) { Finish(false, $"o cartaz de dicas parece uma etiqueta: {noteText}"); return; }
                    // Vermelho com CLEARANCE é só das etiquetas: a placa dos totens é a amarela de DISPLAY ONLY.
                    foreach (Totem totem in Totem.All)
                        if (totem.transform.Find("PlacaTotem") == null) { Finish(false, $"o totem {totem.Index} está sem a placa amarela"); return; }
                    Notes.Add($"3 etiquetas nas células {string.Join(", ", props.TagCells)}, código {_lock.Code:000}, sem bilhete, cartaz de dicas sem dígitos, {Totem.All.Count} totens com a placa amarela");

                    if (Role == HostLateRole)
                    {
                        // Dois editores, o cliente entra depois: o host destranca e espera.
                        _lock.UnlockForTest();
                        _stage = 5;
                        _stageStart = Now;
                        _stageTicks = 0;
                        break;
                    }
                    if (Role == HostRole) { _stage = 6; _stageStart = Now; _stageTicks = 0; break; }

                    // De frente para a porta, perto: trancada, E não abre.
                    LevelExit exit = LevelExit.Instance;
                    Vector3 front = exit.DoorPosition - exit.door.forward * 1.3f;
                    body = _local.controller;
                    body.Teleport(new Vector3(front.x, exit.DoorPosition.y, front.z), Yaw(exit.door.forward));
                    body.SetPitch(0f);
                    Next();
                    break;
                }

                case 2:
                {
                    LevelExit exit = LevelExit.Instance;
                    if (_stageTicks < 3) break;
                    if (!exit.LocalInReach) { if (InStage > 2f) Finish(false, "de frente para a porta, ela não ficou ao alcance"); break; }
                    if (exit.Interact()) { Finish(false, "a porta trancada aceitou o E"); return; }
                    CreateDevices();
                    BeginSteps(SoloSteps());
                    Next("trancada, a porta não abre com E");
                    break;
                }

                case 3:
                    if (!RunSteps()) break;
                    Next();
                    break;

                case 4:
                {
                    LevelExit door = LevelExit.Instance;
                    Vector3 front = door.DoorPosition - door.door.forward * 1.3f;
                    body.Teleport(new Vector3(front.x, door.DoorPosition.y, front.z), Yaw(door.door.forward));
                    body.SetPitch(0f);
                    if (_stageTicks < 3) break;
                    if (!door.Interact()) { if (InStage > 2f) Finish(false, "destrancada, a porta não aceitou o E"); break; }
                    Finish(true, string.Join("; ", Notes) + "; destrancada, a porta abriu");
                    break;
                }

                case 5:
                    // Dois editores (o cliente entra depois): espera o cliente entrar e conferir.
                    if (InStage < 25f) break;
                    Finish(true, string.Join("; ", Notes) + "; destrancou e esperou o cliente 25 s");
                    break;

                case 6:
                    // Dois editores: quem digita é o cliente; o host só confere o código que chega.
                    if (!_lock.Unlocked) break;
                    if (_lock.WrongCodes < 1) { Finish(false, "destrancou sem antes receber o código errado do cliente"); return; }
                    Next($"o cliente mandou {_lock.WrongCodes} código(s) errado(s) e o certo; destrancou");
                    break;

                case 7:
                    if (InStage < 8f) break;
                    Finish(true, string.Join("; ", Notes) + "; esperou 8 s com o cliente");
                    break;
            }
        }

        // ---------------------------------------------------------------- O painel, sozinho

        static List<(string, System.Func<bool>)> SoloSteps()
        {
            var steps = new List<(string, System.Func<bool>)>();
            string code = _lock.Code.ToString("000");
            string wrong = ((_lock.Code + 1) % 1000).ToString("000");

            steps.Add(("mirar o teclado", AimPrompt));
            steps.Add(("E abre o painel", OpenPanel));
            steps.Add(("aberto, o jogador não anda nem gira", () =>
            {
                PlayerController body = _local.controller;
                if (_stepTicks == 1)
                {
                    if (!InputGate.Blocked) return Fail("com o painel aberto, a entrada não ficou bloqueada (InputGate)");
                    if (PlayerController.MouseCaptured) return Fail("com o painel aberto, o jogo ainda conta o mouse como preso");
                    _from = body.transform.position;
                    _yawFrom = body.transform.eulerAngles.y;
                    _pitchFrom = body.Pitch;
                }
                Keys(Key.W, Key.LeftShift);
                InputSystem.QueueStateEvent(_mouse, new MouseState { delta = new Vector2(25f, 10f) });
                if (InStep < 0.8f) return false;
                Keys();
                InputSystem.QueueStateEvent(_mouse, new MouseState());
                float moved = Flat(body.transform.position - _from);
                float turned = Mathf.Abs(Mathf.DeltaAngle(_yawFrom, body.transform.eulerAngles.y)) + Mathf.Abs(body.Pitch - _pitchFrom);
                if (moved > 0.05f) return Fail($"com o painel aberto o jogador andou {moved:F2} m");
                if (turned > 0.5f) return Fail($"com o painel aberto a câmera girou {turned:F1}°");
                if (!CodePanel.IsOpen) return Fail("o painel fechou sozinho");
                Notes.Add($"painel aberto: não anda ({moved:F2} m) nem gira ({turned:F1}°)");
                return true;
            }));
            steps.Add(("a tecla 1 digita e não puxa a caneta", () =>
            {
                if (!Tap(Key.Digit1)) return false;
                if (_lock.Typed != "1") return Fail($"a tecla 1 não digitou (no visor \"{_lock.Typed}\")");
                if (_local.pen != null && _local.pen.Equipped) return Fail("com o painel aberto, a tecla 1 puxou a caneta");
                return true;
            }));
            steps.Add(("Backspace apaga", () =>
            {
                if (!Tap(Key.Backspace)) return false;
                if (_lock.Typed != "") return Fail($"Backspace não apagou (no visor \"{_lock.Typed}\")");
                return true;
            }));
            AddTyping(steps, wrong, false);
            steps.Add(("código errado: visor vermelho, não destranca, limpa", () =>
            {
                if (!Tap(Key.Enter)) return false;
                if (_lock.Unlocked) return Fail("o código errado destrancou");
                if (_lock.WrongCodes != 1) return Fail($"código errado contado {_lock.WrongCodes} vezes");
                if (!_lock.ShowingError || !CodePanel.Instance.VisorError) return Fail($"código errado: o visor não ficou vermelho (\"{CodePanel.Instance.VisorText}\")");
                if (_lock.Typed != "") return Fail($"código errado: o visor não limpou (\"{_lock.Typed}\")");
                if (!CodePanel.IsOpen) return Fail("o código errado fechou o painel");
                BeginPhoto(CodePanel.Instance.Document);
                return true;
            }));
            steps.Add(("foto do erro", () => _stepTicks >= 6 && TakePhoto("painel_erro.png")));
            steps.Add(("Esc fecha sem abrir a pausa", () =>
            {
                if (_stepTicks == 1) { Keys(Key.Escape); return false; }
                if (_stepTicks == 3) Keys();
                if (_stepTicks < 8) return false;
                if (CodePanel.IsOpen) return Fail("Esc não fechou o painel");
                if (PauseMenu.IsOpen) return Fail("o Esc que fechou o painel abriu o menu de pausa");
                if (!PlayerController.MouseCaptured) return Fail("fechado o painel, o mouse não voltou a valer");
                _from = _local.controller.transform.position;
                return true;
            }));
            steps.Add(("fechado, S anda", () =>
            {
                Keys(Key.S);
                if (InStep < 0.6f) return false;
                Keys();
                float moved = Flat(_local.controller.transform.position - _from);
                if (moved < 0.3f) return Fail($"fechado o painel, S não andou ({moved:F2} m)");
                Notes.Add($"Esc fechou o painel (sem a pausa) e S andou {moved:F2} m");
                FaceKeypad();
                return true;
            }));
            steps.Add(("mirar o teclado de novo", AimPrompt));
            steps.Add(("E abre o painel de novo", OpenPanel));
            steps.Add(("E fecha (e não mexe na porta)", () =>
            {
                if (_stepTicks == 1) { _rattlesFrom = LevelExit.Instance.Rattles; Keys(Key.E); return false; }
                if (_stepTicks == 3) Keys();
                if (_stepTicks < 8) return false;
                if (CodePanel.IsOpen) return Fail("E não fechou o painel");
                if (LevelExit.Instance.Rattles != _rattlesFrom) return Fail("o E que fechou o painel chacoalhou a porta");
                return true;
            }));
            steps.Add(("mirar o teclado pela terceira vez", AimPrompt));
            steps.Add(("E abre o painel pela terceira vez", OpenPanel));
            steps.Add(("primeiro dígito clicado com o mouse", () =>
            {
                if (_stepTicks == 1 && !ClickKey(code[0].ToString())) return Fail($"o painel não tem a tecla {code[0]}");
                if (_stepTicks < 3) return false;
                if (_lock.Typed != code.Substring(0, 1)) return Fail($"o clique na tecla {code[0]} não digitou (\"{_lock.Typed}\")");
                return true;
            }));
            steps.Add(("segundo dígito pelo teclado numérico", () =>
            {
                if (!Tap(Key.Numpad0 + (code[1] - '0'))) return false;
                if (_lock.Typed != code.Substring(0, 2)) return Fail($"o teclado numérico não digitou {code[1]} (\"{_lock.Typed}\")");
                BeginPhoto(CodePanel.Instance.Document);
                return true;
            }));
            steps.Add(("foto digitando", () => _stepTicks >= 6 && TakePhoto("painel_digitando.png")));
            steps.Add(("terceiro dígito pela fileira de cima", () =>
            {
                if (!Tap(DigitKey(code[2]))) return false;
                if (_lock.Typed != code) return Fail($"faltou o terceiro dígito (\"{_lock.Typed}\")");
                return true;
            }));
            steps.Add(("Enter: o certo destranca e fecha", () =>
            {
                if (_stepTicks <= 4 && !Tap(Key.Enter)) return false;
                if (!_lock.Unlocked) return InStep > 2f ? Fail($"o código certo ({code}) não destrancou") : false;
                if (CodePanel.IsOpen) return InStep > 3f ? Fail("destrancada a porta, o painel não fechou") : false;
                Notes.Add($"o código certo ({code}: clique, numérico e fileira de cima, Enter) destrancou e fechou o painel");
                return true;
            }));
            steps.Add(("fechado pelo código certo, o mouse volta", () =>
            {
                // No quadro em que o painel fecha a entrada ainda é dele (InputGate); espera uns quadros.
                if (_stepTicks < 6) return false;
                if (!PlayerController.MouseCaptured) return Fail("fechado o painel pelo código certo, o mouse não voltou a valer");
                return true;
            }));
            steps.Add(("destrancada, o aviso é OPEN", () =>
            {
                AimAtKeypad();
                if (_stepTicks < 4) return false;
                if (ExitLock.Prompt != "OPEN") return InStep > 2f ? Fail($"destrancada, mirando o teclado o aviso foi \"{ExitLock.Prompt}\"") : false;
                return true;
            }));
            return steps;
        }

        // Digita uma sequência pela fileira de cima, tecla por tecla.
        static void AddTyping(List<(string, System.Func<bool>)> steps, string digits, bool numpad)
        {
            for (int i = 0; i < digits.Length; i++)
            {
                char c = digits[i];
                string expected = digits.Substring(0, i + 1);
                steps.Add(($"digitar {c}", () =>
                {
                    if (!Tap(numpad ? Key.Numpad0 + (c - '0') : DigitKey(c))) return false;
                    if (_lock.Typed != expected) return Fail($"digitando {digits}, o visor ficou \"{_lock.Typed}\"");
                    return true;
                }));
            }
        }

        static bool AimPrompt()
        {
            AimAtKeypad();
            if (_stepTicks < 3) return false;
            if (ExitLock.Prompt != KeypadPrompt) return InStep > 2f ? Fail($"mirando o teclado, o aviso foi \"{ExitLock.Prompt}\"") : false;
            return true;
        }

        static bool OpenPanel()
        {
            AimAtKeypad();
            if (_stepTicks == 1) ExitLock.PressForTest = true;
            if (!CodePanel.IsOpen || CodePanel.Instance == null || !CodePanel.Instance.Shown) return false;
            if (_stepTicks < 4) return false;
            return true;
        }

        // ---------------------------------------------------------------- Dois editores

        static void TickJoin(GameSession session)
        {
            _stageTicks++;
            switch (_stage)
            {
                case 0:
                    if (session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null && ExitLock.Instance != null) { Next(); break; }
                    if (session.Phase == SessionPhase.Menu && EditorApplication.timeSinceStartup - _lastTry > 4.0)
                    {
                        _lastTry = EditorApplication.timeSinceStartup;
                        session.Join("127.0.0.1");
                    }
                    break;

                case 1:
                {
                    if (InStage < 3f) break;
                    _local = PlayerNetwork.Local;
                    _level = session.level;
                    _lock = ExitLock.Instance;
                    PlayerController.AssumeMouseCaptured = true;
                    CreateDevices();
                    FaceKeypad();
                    string code = _lock.Code.ToString("000");
                    string wrong = ((_lock.Code + 1) % 1000).ToString("000");
                    var steps = new List<(string, System.Func<bool>)>
                    {
                        ("mirar o teclado", AimPrompt),
                        ("E abre o painel", OpenPanel),
                    };
                    AddTyping(steps, wrong, false);
                    steps.Add(("código errado no cliente: visor vermelho", () =>
                    {
                        if (!Tap(Key.Enter)) return false;
                        if (_lock.Unlocked || _lock.Accepted) return Fail("o código errado foi aceito no cliente");
                        if (!_lock.ShowingError) return Fail("código errado: o visor do cliente não ficou vermelho");
                        return true;
                    }));
                    AddTyping(steps, code, true);
                    steps.Add(("Enter: o host destranca", () =>
                    {
                        if (_stepTicks <= 4 && !Tap(Key.NumpadEnter)) return false;
                        if (!_lock.Unlocked) return InStep > 10f ? Fail($"o cliente mandou o código certo ({code}) e o host não destrancou") : false;
                        if (CodePanel.IsOpen) return InStep > 12f ? Fail("destrancada a porta, o painel do cliente não fechou") : false;
                        return true;
                    }));
                    BeginSteps(steps);
                    Next();
                    break;
                }

                case 2:
                    if (!RunSteps()) break;
                    Finish(true, $"o cliente digitou no painel um código errado (visor vermelho) e o certo ({_lock.Code:000}); o host destrancou e o painel fechou");
                    break;
            }
        }

        static void TickJoinLate(GameSession session)
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

        // ---------------------------------------------------------------- Passos

        static void BeginSteps(List<(string, System.Func<bool>)> steps)
        {
            _steps = steps;
            _step = 0;
            _stepTicks = 0;
            _stepStart = Now;
        }

        // Roda o passo atual; devolve true quando todos terminaram.
        static bool RunSteps()
        {
            if (_finished) return false;
            if (_step >= _steps.Count) return true;
            _stepTicks++;
            bool done = _steps[_step].act();
            if (_finished) return false;
            if (done)
            {
                _step++;
                _stepTicks = 0;
                _stepStart = Now;
                return _step >= _steps.Count;
            }
            if (InStep > StepTimeout) Fail($"o passo \"{_steps[_step].what}\" não terminou em {StepTimeout:0} s");
            return false;
        }

        static bool Fail(string why)
        {
            Finish(false, why);
            return false;
        }

        // Aperta num quadro e solta dois depois; devolve true no quadro seguinte ao soltar.
        static bool Tap(Key key)
        {
            if (_stepTicks == 1) Keys(key);
            else if (_stepTicks == 3) Keys();
            return _stepTicks >= 5;
        }

        static Key DigitKey(char c) => c == '0' ? Key.Digit0 : Key.Digit1 + (c - '1');

        static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));

        // Clica (como o mouse) na tecla do painel com esse texto.
        static bool ClickKey(string label)
        {
            Button key = null;
            CodePanel.Instance.Document.rootVisualElement.Query<Button>().ForEach(b => { if (b.text == label) key = b; });
            if (key == null) return false;
            using (NavigationSubmitEvent e = NavigationSubmitEvent.GetPooled())
            {
                e.target = key;
                key.SendEvent(e);
            }
            return true;
        }

        static void CreateDevices()
        {
            if (_keyboard != null) return;
            _backgroundBefore = InputSystem.settings.backgroundBehavior;
            _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
        }

        // Põe o jogador de frente para o teclado, a 1 m dele.
        static void FaceKeypad()
        {
            Transform keypad = _level.transform.Find("Saida/Teclado");
            if (keypad == null) return;
            LevelExit door = LevelExit.Instance;
            Vector3 front = keypad.position - door.door.forward * 1.0f;
            _local.controller.Teleport(new Vector3(front.x, door.DoorPosition.y, front.z), Yaw(door.door.forward));
            AimAtKeypad();
        }

        // Vira o jogador para o teclado e inclina a câmera até ele.
        static void AimAtKeypad()
        {
            Transform keypad = _level.transform.Find("Saida/Teclado");
            if (keypad == null) return;
            Transform eye = _local.playerCamera.transform;
            Vector3 to = keypad.position - eye.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            _local.transform.rotation = Quaternion.Euler(0f, Yaw(flat), 0f);
            _local.controller.SetPitch(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
        }

        static float Yaw(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        // ---------------------------------------------------------------- Foto

        // O UI Toolkit não sai no camera.Render(): o documento passa a desenhar numa textura (uma
        // cópia do PanelSettings; o asset não se toca) e a foto junta as duas imagens.
        static void BeginPhoto(UIDocument document)
        {
            _photoDocument = document;
            _photoOriginal = document.panelSettings;
            _photoUi = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            _photoPanel = Object.Instantiate(_photoOriginal);
            _photoPanel.targetTexture = _photoUi;
            _photoPanel.clearColor = true;
            _photoPanel.colorClearValue = Color.clear;
            document.panelSettings = _photoPanel;
        }

        static bool TakePhoto(string name)
        {
            Camera camera = _local.playerCamera;
            var scene = new RenderTexture(1920, 1080, 24);
            camera.targetTexture = scene;
            for (int i = 0; i < 6; i++) camera.Render();
            camera.targetTexture = null;

            Texture2D back = Read(scene), front = Read(_photoUi);
            Color32[] a = back.GetPixels32(), b = front.GetPixels32();
            int opaque = 0;
            for (int i = 0; i < a.Length; i++)
            {
                float alpha = b[i].a / 255f;
                if (b[i].a > 200) opaque++;
                a[i] = Color32.Lerp(a[i], new Color32(b[i].r, b[i].g, b[i].b, 255), alpha);
            }
            back.SetPixels32(a);
            back.Apply();

            Directory.CreateDirectory(PhotoFolder);
            string path = Path.Combine(PhotoFolder, name);
            File.WriteAllBytes(path, back.EncodeToPNG());
            EndPhoto();
            Object.Destroy(back);
            Object.Destroy(front);
            scene.Release();

            float cover = opaque / (float)a.Length;
            if (cover < 0.05f) return Fail($"na foto {name} o painel quase não aparece ({cover:P0} da tela)");
            Notes.Add($"foto {path} (painel cobre {cover:P0})");
            return true;
        }

        static void EndPhoto()
        {
            if (_photoDocument != null) _photoDocument.panelSettings = _photoOriginal;
            if (_photoPanel != null) Object.Destroy(_photoPanel);
            if (_photoUi != null) _photoUi.Release();
            _photoDocument = null;
            _photoPanel = null;
            _photoUi = null;
        }

        static Texture2D Read(RenderTexture target)
        {
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            return texture;
        }

        // ---------------------------------------------------------------- Fim

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;

            EndPhoto();
            EntityDirector.Paused = false;
            PlayerController.AssumeMouseCaptured = false;
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                InputSystem.RemoveDevice(_mouse);
                InputSystem.settings.backgroundBehavior = _backgroundBefore;
                InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBefore;
                _keyboard = null;
                _mouse = null;
            }
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            string role = Role == SoloRole ? "" : $" ({Role})";
            Debug.Log($"[BackRooms] Teste do objetivo do Level 1{role} " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

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
