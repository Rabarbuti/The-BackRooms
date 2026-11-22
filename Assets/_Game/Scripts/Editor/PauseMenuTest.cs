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
    /// Teste em Play do menu de pausa (<see cref="PauseMenu"/>) nas quatro fases, com teclado virtual:
    /// antes de abrir, W anda; Esc abre; com o menu aberto o jogador não anda, não gira, não liga a
    /// lanterna; as tarefas da fase aparecem desenhadas (sozinho e com dois jogadores contados, com o
    /// prazo da janela) e mudam quando um ponto é armado (ou a porta do Level 1 destranca); o cartaz da
    /// sala inicial virou dicas (sem "ao mesmo tempo"); Esc fecha e W volta a andar; "Quit to menu"
    /// volta ao menu e, hospedando de novo, o jogador anda (o menu não fica preso aberto). Uma foto do
    /// menu aberto por fase em <c>Logs/Previews/pausa/pausa_&lt;cena&gt;.png</c> (1920 x 1080).
    /// No Level 0, a galeria das gravações (<see cref="RecordingsScreen"/>): pelo menu inicial, vazia
    /// (diz como gravar); pelo menu de pausa, com um clipe gravado no teste (Play e Open folder
    /// entregam o endereço certo, sem abrir nada), Delete pede confirmação e apaga, a lista volta a
    /// ficar vazia e o Esc fecha só a galeria. Tudo em inglês; fotos em <c>Logs/Previews/filmadora</c>.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.PauseMenuTest.BatchRun (as quatro fases)
    /// ou BatchRunLevel0. Sai com 0 se passou, 3 se falhou. Linhas "[BackRooms] Teste do menu de
    /// pausa".
    /// </summary>
    public static class PauseMenuTest
    {
        const string ActiveKey = "BackRooms.PauseMenuTest.Active";
        const string BatchKey = "BackRooms.PauseMenuTest.Batch";
        const string ExitCodeKey = "BackRooms.PauseMenuTest.ExitCode";
        const string ScenesKey = "BackRooms.PauseMenuTest.Scenes";
        const string IndexKey = "BackRooms.PauseMenuTest.Index";
        const string FailedKey = "BackRooms.PauseMenuTest.Failed";
        const string Level0Scene = "Assets/_Game/Scenes/Level0_Test.unity";
        const double Timeout = 150.0;
        const string PhotoFolder = "Logs/Previews/pausa";
        const string EnglishFolder = "Logs/Previews/ingles";
        const string EndingFolder = "Logs/Previews/fecho";

        // Textos que nunca podem aparecer para o jogador: os pontos não precisam ser ao mesmo tempo.
        static readonly string[] Forbidden = { "MESMO TEMPO", "SAME TIME", "AT ONCE", "SIMULTANE" };
        // A língua do jogo é o inglês: nenhum destes pode aparecer em texto visível das telas
        // fotografadas (letras acentuadas do português e palavras que sobraram da versão antiga).
        const string PortugueseLetters = "ãõçáéíóúâêôàÃÕÇÁÉÍÓÚÂÊÔÀ";
        static readonly string[] PortugueseWords =
        {
            "fase", "sair", "voltar", "continuar", "opções", "opcoes", "hospedar", "entrar", "aqui", "zerada",
            "zerado", "liberada", "liberado", "tarefas", "teclas", "lanterna", "porta", "saída", "saida", "jogador",
            "jogadores", "endereço", "endereco", "conectar", "conectando", "piscinas", "escritório", "escritorio",
            "mostruário", "mostruario", "corredores", "amarelos", "qualidade", "fita", "fim", "menu inicial",
            "teste", "ou", "de", "da", "para", "com", "não", "nao", "sozinho", "andar", "correr", "agachar",
            "pular", "achar", "ache", "arme", "gire", "puxe", "etiquetas", "código", "codigo", "bilhete", "dica"
        };

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks;
        static double _began;
        static float _stageStart;
        static bool _finished;
        static Keyboard _keyboard;
        static int _panelStep, _panelTicks;
        static Mouse _mouse;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;
        static PlayerNetwork _local;
        static Vector3 _from, _walked;
        static float _walkedYaw;
        static float _yawFrom, _pitchFrom;
        static bool _lightFrom;
        static string _tasksBefore;
        static bool _rehosted;
        static PanelSettings _photoPanel, _photoOriginal;
        static UIDocument _photoDocument;
        static int _menuShot, _shotTicks;
        static bool _menuChecked;
        static RenderTexture _photoUi;
        // Galeria das gravações (só no Level 0).
        const string FilmFolder = "Logs/Previews/filmadora";
        const string GalleryFolder = "Logs/Gravacoes/pausa";
        const string EmptyFolder = "Logs/Gravacoes/vazia";
        static int _galleryStep;
        static float _galleryAt;
        static string _galleryClip;
        static readonly List<string> Opened = new List<string>();
        static RenderTexture _galleryTarget;

        [MenuItem("BackRooms/Testar menu de pausa (quatro fases)")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Start(false, AllScenes());
        }

        public static void BatchRun() => Start(true, AllScenes());
        public static void BatchRunLevel0() => Start(true, Level0Scene);

        static string AllScenes() => string.Join(";", Level0Scene, Level1Setup.ScenePath, Level2Setup.ScenePath, Level3Setup.ScenePath);

        static void Start(bool batch, string scenes)
        {
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(ScenesKey, scenes);
            SessionState.SetInt(IndexKey, 0);
            SessionState.SetBool(FailedKey, false);
            OpenAndPlay(0);
        }

        static string[] Scenes => SessionState.GetString(ScenesKey, Level0Scene).Split(';');
        static int Index => SessionState.GetInt(IndexKey, 0);
        static string Scene => Scenes[Mathf.Clamp(Index, 0, Scenes.Length - 1)];
        static string SceneName => Path.GetFileNameWithoutExtension(Scene);

        static void OpenAndPlay(int index)
        {
            SessionState.SetInt(IndexKey, index);
            EditorSceneManager.OpenScene(Scenes[index], OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (SessionState.GetInt(ExitCodeKey, -1) >= 0)
            {
                EditorApplication.playModeStateChanged -= AfterPlay;
                EditorApplication.playModeStateChanged += AfterPlay;
                if (!EditorApplication.isPlayingOrWillChangePlaymode) AfterPlay(PlayModeStateChange.EnteredEditMode);
            }

            if (!SessionState.GetBool(ActiveKey, false)) return;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick;
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

        static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));

        static void Tick()
        {
            if (!EditorApplication.isPlaying || _finished) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - _began > Timeout)
            {
                Finish(false, $"tempo esgotado na etapa {_stage}");
                return;
            }

            _stageTicks++;
            PauseMenu menu = PauseMenu.Instance;
            PlayerController body = _local != null ? _local.controller : null;

            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    PoolTide.Paused = true;
                    SwitchObjective.PlayersForTest = 0;
                    PlayerController.AssumeMouseCaptured = true;
                    CreateDevices();
                    // No menu inicial, com a fase montada atrás: só a música se ouve.
                    if (InStage < 1.5f) break;
                    if (!_menuChecked)
                    {
                        if (!CheckMenuSilence("no menu")) return;
                        _menuChecked = true;
                    }
                    if (!MenuShots(session)) break;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                    // Pronto e assentado no chão.
                    if (!Ready(session)) break;
                    if (menu == null) { Finish(false, "a cena não tem o menu de pausa (PauseMenu)"); return; }
                    if (PauseMenu.IsOpen) { Finish(false, "o menu de pausa começou aberto"); return; }
                    if (!CheckNote()) return;
                    // Level 1: o painel do código, em inglês, e o Esc que o fecha não abre a pausa.
                    if (ExitLock.Instance != null && !PanelCheck()) break;
                    if (GameOptions.MenuSilence || !Mathf.Approximately(AudioListener.volume, GameOptions.Volume))
                    { Finish(false, $"jogando, o som da fase não voltou (ouvinte em {AudioListener.volume:F2}, volume geral {GameOptions.Volume:F2})"); return; }
                    _from = body.transform.position;
                    Keys(Key.W);
                    Next();
                    break;

                case 2:
                    if (InStage < 0.7f) break;
                    Keys();
                    if (Flat(body.transform.position - _from) < 0.3f) { Finish(false, $"antes de abrir o menu, W não andou ({Flat(body.transform.position - _from):F2} m)"); return; }
                    // Onde o W chegou: daqui, para trás (S), o caminho está livre (volta-se por onde veio).
                    _walked = body.transform.position;
                    _walkedYaw = body.transform.eulerAngles.y;
                    Next();
                    break;

                case 3:
                    // Esc: aperta num quadro, solta no seguinte.
                    if (_stageTicks == 1) { Keys(Key.Escape); break; }
                    if (_stageTicks == 3) Keys();
                    if (!PauseMenu.IsOpen) { if (InStage > 1.5f) Finish(false, "Esc não abriu o menu de pausa"); break; }
                    // Espera o embalo do W de antes acabar (o jogo não para: o corpo ainda desliza um pouco).
                    if (InStage < 1.2f) break;
                    if (!menu.Shown) { Finish(false, "o menu abriu (IsOpen) mas o painel não aparece"); return; }
                    if (PlayerController.MouseCaptured) { Finish(false, "com o menu aberto, o jogo ainda conta o mouse como preso"); return; }
                    _from = body.transform.position;
                    _yawFrom = body.transform.eulerAngles.y;
                    _pitchFrom = body.Pitch;
                    _lightFrom = _local.flashlight != null && _local.flashlight.IsOn;
                    Next("Esc abriu o menu");
                    break;

                case 4:
                    // Com o menu aberto: W, Shift, Espaço, F e o mouse girando não fazem nada.
                    Keys(Key.W, Key.LeftShift, Key.Space, Key.F);
                    InputSystem.QueueStateEvent(_mouse, new MouseState { delta = new Vector2(25f, 10f) });
                    if (InStage < 1.0f) break;
                    Keys();
                    InputSystem.QueueStateEvent(_mouse, new MouseState());
                    float moved = Flat(body.transform.position - _from);
                    float turned = Mathf.Abs(Mathf.DeltaAngle(_yawFrom, body.transform.eulerAngles.y)) + Mathf.Abs(body.Pitch - _pitchFrom);
                    bool light = _local.flashlight != null && _local.flashlight.IsOn;
                    if (moved > 0.05f) { Finish(false, $"com o menu aberto o jogador andou {moved:F2} m"); return; }
                    if (turned > 0.5f) { Finish(false, $"com o menu aberto a câmera girou {turned:F1}°"); return; }
                    if (light != _lightFrom) { Finish(false, "com o menu aberto o F mudou a lanterna"); return; }
                    if (!PauseMenu.IsOpen) { Finish(false, "o menu fechou sozinho"); return; }
                    Next($"aberto, não anda ({moved:F2} m), não gira ({turned:F1}°) nem liga a lanterna");
                    break;

                case 5:
                {
                    if (_stageTicks < 3) break;
                    string tasks = menu.ShownTasks();
                    string keys = menu.ShownKeys();
                    if (string.IsNullOrWhiteSpace(tasks)) { Finish(false, "o menu aberto não mostra tarefas"); return; }
                    if (!keys.Contains("Esc") || !keys.Contains("WASD")) { Finish(false, $"o menu não mostra as teclas: {keys}"); return; }
                    if (!Clean(tasks, "as tarefas")) return;
                    SwitchObjective objective = SwitchObjective.Instance;
                    if (objective != null)
                    {
                        if (!tasks.Contains("(0/2)")) { Finish(false, $"sozinho, a tarefa não mostra 0/2: {OneLine(tasks)}"); return; }
                        if (!tasks.Contains("Alone")) { Finish(false, $"sozinho, a tarefa não diz que o ponto fica armado: {OneLine(tasks)}"); return; }
                        // Com dois jogadores contados: o prazo da janela, um depois do outro.
                        SwitchObjective.PlayersForTest = 2;
                    }
                    else if (ExitLock.Instance == null) { Finish(false, "a fase não tem objetivo (nem SwitchObjective nem ExitLock)"); return; }
                    _tasksBefore = tasks;
                    Notes.Add("tarefas: " + OneLine(tasks));
                    Next();
                    break;
                }

                case 6:
                {
                    if (_stageTicks < 3) break;
                    SwitchObjective objective = SwitchObjective.Instance;
                    if (objective != null)
                    {
                        string tasks = menu.ShownTasks();
                        string window = $"the second within {objective.WindowSeconds:0} s of the first";
                        if (!tasks.Contains(window)) { Finish(false, $"com dois jogadores a tarefa não diz \"{window}\": {OneLine(tasks)}"); return; }
                        if (!Clean(tasks, "as tarefas com dois jogadores")) return;
                        _tasksBefore = tasks;
                        objective.HostArm(0, null);
                    }
                    else ExitLock.Instance.UnlockForTest();
                    Next();
                    break;
                }

                case 7:
                {
                    if (_stageTicks < 3) break;
                    string tasks = menu.ShownTasks();
                    SwitchObjective objective = SwitchObjective.Instance;
                    if (objective != null)
                    {
                        if (!tasks.Contains("(1/2)")) { if (InStage > 2f) Finish(false, $"armado um ponto, a tarefa não passou a 1/2: {OneLine(tasks)}"); break; }
                        if (!tasks.Contains("Time left")) { Finish(false, $"com um ponto valendo em grupo, a tarefa não diz quanto falta: {OneLine(tasks)}"); return; }
                        Notes.Add("armado um ponto (dois jogadores): " + OneLine(tasks));
                    }
                    else
                    {
                        if (tasks == _tasksBefore || tasks.Contains("type the digits")) { if (InStage > 2f) Finish(false, $"destrancada a porta, as tarefas não mudaram: {OneLine(tasks)}"); break; }
                        Notes.Add("destrancada a porta: " + OneLine(tasks));
                    }
                    BeginPhoto();
                    Next();
                    break;
                }

                case 8:
                    // A foto: alguns quadros para o painel se desenhar na textura.
                    if (_stageTicks < 8) break;
                    if (_galleryStep == 0)
                    {
                        if (!TakePhoto(body)) return;
                        if (Index != 0) { Next(); break; }
                        _galleryStep = 1;
                        break;
                    }
                    // No Level 0, a galeria das gravações, aberta por este menu.
                    if (!Gallery(menu)) break;
                    Next();
                    break;

                case 9:
                    // Esc fecha; W volta a andar.
                    if (_stageTicks == 1) { Keys(Key.Escape); break; }
                    if (_stageTicks == 3) Keys();
                    if (PauseMenu.IsOpen) { if (InStage > 1.5f) Finish(false, "Esc não fechou o menu"); break; }
                    if (_stageTicks < 5) break;
                    if (!PlayerController.MouseCaptured) { Finish(false, "fechado o menu, o mouse não voltou a valer"); return; }
                    body.Teleport(_walked, _walkedYaw);
                    _from = _walked;
                    Keys(Key.S);
                    Next();
                    break;

                case 10:
                    if (InStage < 0.7f) break;
                    Keys();
                    if (Flat(body.transform.position - _from) < 0.3f) { Finish(false, $"fechado o menu, S não andou ({Flat(body.transform.position - _from):F2} m)"); return; }
                    Next("Esc fechou e o jogador voltou a andar");
                    break;

                case 11:
                    // Abre de novo e aperta "Quit to menu".
                    if (_stageTicks == 1) { Keys(Key.Escape); break; }
                    if (_stageTicks == 3) Keys();
                    if (_stageTicks < 6) break;
                    if (!PauseMenu.IsOpen) { Finish(false, "Esc não abriu o menu de novo"); return; }
                    if (!ClickQuit(menu)) { Finish(false, "o botão de sair para o menu não existe"); return; }
                    Next();
                    break;

                case 12:
                    if (session.Phase != SessionPhase.Menu) { if (InStage > 5f) Finish(false, "\"Quit to menu\" não voltou ao menu"); break; }
                    if (PauseMenu.IsOpen) { Finish(false, "de volta ao menu, o menu de pausa continuou aberto"); return; }
                    if (InStage < 1f) break;
                    if (!CheckMenuSilence("de volta ao menu")) return;
                    Notes.Add("\"Quit to menu\" voltou ao menu");
                    // Só no Level 0: hospeda de novo e confere que o jogador anda (nada ficou preso).
                    if (Index != 0 || _rehosted) { Finish(true, string.Join("; ", Notes)); return; }
                    _rehosted = true;
                    _local = null;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 13:
                    if (!Ready(session)) break;
                    if (PauseMenu.IsOpen) { Finish(false, "hospedando de novo, o menu de pausa começou aberto"); return; }
                    _from = _local.controller.transform.position;
                    Keys(Key.W);
                    Next();
                    break;

                case 14:
                    if (InStage < 0.7f) break;
                    Keys();
                    if (Flat(_local.controller.transform.position - _from) < 0.3f) { Finish(false, "hospedando de novo depois de sair pelo menu, W não andou"); return; }
                    Finish(true, string.Join("; ", Notes) + "; hospedando de novo, o jogador anda");
                    break;
            }
        }

        static bool Ready(GameSession session)
        {
            if (session.Phase != SessionPhase.Playing) { _stageStart = Now; return false; }
            _local = PlayerNetwork.Local;
            if (_local == null || !_local.InGame || session.level == null) { _stageStart = Now; return false; }
            CharacterController character = _local.controller.GetComponent<CharacterController>();
            return InStage >= 2f && (character == null || character.isGrounded);
        }

        // O painel do código do Level 1 (CodePanel): abre, todo texto visível em inglês, e o Esc
        // fecha o painel sem abrir o menu de pausa. Devolve true quando terminou.
        static bool PanelCheck()
        {
            _panelTicks++;
            switch (_panelStep)
            {
                case 0:
                    CodePanel.Open(ExitLock.Instance);
                    _panelStep = 1;
                    _panelTicks = 0;
                    return false;
                case 1:
                    if (_panelTicks < 6) return false;
                    if (!CodePanel.IsOpen || CodePanel.Instance == null || !CodePanel.Instance.Shown) { Finish(false, "o painel do código não abriu"); return false; }
                    if (!EnglishOnly(CodePanel.Instance.Document, "o painel do código")) return false;
                    Keys(Key.Escape);
                    _panelStep = 2;
                    _panelTicks = 0;
                    return false;
                case 2:
                    if (_panelTicks == 2) Keys();
                    if (_panelTicks < 6) return false;
                    if (CodePanel.IsOpen) { Finish(false, "Esc não fechou o painel do código"); return false; }
                    if (PauseMenu.IsOpen) { Finish(false, "o Esc que fechou o painel do código abriu o menu de pausa"); return false; }
                    Notes.Add("painel do código: Esc fecha sem abrir a pausa");
                    _panelStep = 3;
                    return true;
                default:
                    return true;
            }
        }

        // O cartaz da sala inicial: dicas, a lembrança do Esc e nada de "ao mesmo tempo".
        static bool CheckNote()
        {
            SwitchObjective objective = SwitchObjective.Instance;
            if (objective == null) return true;
            if (objective.Note == null) { Finish(false, "sem o cartaz de dicas na sala inicial"); return false; }
            string note = string.Join(" / ", objective.NoteText);
            if (!Clean(note, "o cartaz")) return false;
            if (!note.Contains("ESC: TASKS")) { Finish(false, $"o cartaz não lembra do Esc: {note}"); return false; }
            foreach (string line in objective.NoteText)
                if (line.Length > 21) { Finish(false, $"linha do cartaz com mais de 21 letras: {line}"); return false; }
            Notes.Add("cartaz: " + note);
            return true;
        }

        // No menu o ouvinte fica mudo e só a música do menu passa por cima dele. Lista também as
        // fontes da fase que estariam tocando (o que se ouvia antes): sem dispositivo de áudio, em
        // lote, isPlaying não vale, então conta a fonte ligada, com som e volume.
        static bool CheckMenuSilence(string where)
        {
            if (!GameOptions.MenuSilence || AudioListener.volume > 0f)
            { Finish(false, $"{where}, o ouvinte não ficou mudo (volume {AudioListener.volume:F2})"); return false; }
            var loud = new List<string>();
            foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude))
            {
                if (!source.isActiveAndEnabled || source.clip == null || source.volume <= 0f) continue;
                bool music = source.GetComponent<BackRooms.Audio.MenuMusic>() != null;
                if (source.ignoreListenerVolume && !music)
                { Finish(false, $"{where}, a fonte {source.name} passa por cima do ouvinte mudo"); return false; }
                if (!music && (source.isPlaying || source.loop)) loud.Add(source.name + "(" + source.clip.name + ")");
            }
            var counts = new Dictionary<string, int>();
            foreach (string name in loud) counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;
            var listed = new List<string>();
            foreach (var pair in counts) listed.Add(pair.Value > 1 ? pair.Key + " x" + pair.Value : pair.Key);
            listed.Sort(System.StringComparer.Ordinal);
            Notes.Add($"{where}: ouvinte mudo, só a música passa; caladas: {(listed.Count > 0 ? string.Join(", ", listed) : "nenhuma")}");
            return true;
        }

        static bool Clean(string text, string what)
        {
            foreach (string word in Forbidden)
                if (text.ToUpperInvariant().Contains(word)) { Finish(false, $"{what} ainda dizem \"{word}\": {OneLine(text)}"); return false; }
            return true;
        }

        /// <summary>
        /// Confere que nenhum texto visível de <paramref name="document"/> está em português (letra
        /// acentuada ou palavra da lista). Falha o teste com o texto achado.
        /// </summary>
        static bool EnglishOnly(UIDocument document, string what)
        {
            var shown = new List<string>();
            document.rootVisualElement.Query<TextElement>().ForEach(t =>
            {
                if (string.IsNullOrWhiteSpace(t.text)) return;
                for (VisualElement e = t; e != null; e = e.parent)
                    if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden) return;
                shown.Add(t.text);
            });
            if (shown.Count == 0) { Finish(false, $"{what}: nenhum texto visível para conferir a língua"); return false; }
            foreach (string text in shown)
            {
                foreach (char c in text)
                    if (PortugueseLetters.IndexOf(c) >= 0) { Finish(false, $"{what} ainda tem português (\"{c}\"): {OneLine(text)}"); return false; }
                string[] words = System.Text.RegularExpressions.Regex.Split(text.ToLowerInvariant(), @"[^a-z0-9]+");
                foreach (string word in PortugueseWords)
                {
                    bool found = word.Contains(" ")
                        ? text.ToLowerInvariant().Contains(word)
                        : System.Array.IndexOf(words, word) >= 0;
                    if (found) { Finish(false, $"{what} ainda tem português (\"{word}\"): {OneLine(text)}"); return false; }
                }
            }
            Notes.Add($"{what}: {shown.Count} textos visíveis, todos em inglês");
            return true;
        }

        static bool ClickQuit(PauseMenu menu)
        {
            Button quit = null;
            menu.Document.rootVisualElement.Query<Button>().ForEach(b => { if (b.text == "Quit to menu") quit = b; });
            if (quit == null) return false;
            using (NavigationSubmitEvent e = NavigationSubmitEvent.GetPooled())
            {
                e.target = quit;
                quit.SendEvent(e);
            }
            return true;
        }

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

        static void BeginPhoto() => BeginPhoto(PauseMenu.Instance.Document);

        static bool TakePhoto(PlayerController body)
        {
            string path = Path.Combine(PhotoFolder, "pausa_" + SceneName + ".png");
            if (!EnglishOnly(PauseMenu.Instance.Document, "o menu de pausa")) return false;
            if (!TakePhoto(_local.playerCamera, path, 0.2f, "o menu de pausa")) return false;
            // Cópia para as fotos da tradução.
            Directory.CreateDirectory(EnglishFolder);
            File.Copy(path, Path.Combine(EnglishFolder, "pausa_" + SceneName + ".png"), true);
            return true;
        }

        // Junta a imagem da câmera e a do documento e grava; confere que o documento cobre pelo
        // menos <paramref name="minCover"/> da tela (senão não se desenhou).
        static bool TakePhoto(Camera camera, string path, float minCover, string what)
        {
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

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, back.EncodeToPNG());

            _photoDocument.panelSettings = _photoOriginal;
            Object.Destroy(_photoPanel);
            Object.Destroy(back);
            Object.Destroy(front);
            scene.Release();
            _photoUi.Release();
            _photoPanel = null;
            _photoUi = null;
            _photoDocument = null;

            float cover = opaque / (float)a.Length;
            if (cover < minCover) { Finish(false, $"na foto {what} quase não aparece ({cover:P0} da tela); {path}"); return false; }
            Notes.Add($"foto em {path} ({what} cobre {cover:P0})");
            return true;
        }

        // Só no Level 0, antes de hospedar: o menu inicial e a tela de fim da fita, em inglês.
        static bool MenuShots(GameSession session)
        {
            ConnectionMenu menu = Object.FindFirstObjectByType<ConnectionMenu>();
            if (menu == null) { Finish(false, "a cena não tem o menu inicial (ConnectionMenu)"); return false; }
            UIDocument document = menu.GetComponent<UIDocument>();
            Camera camera = session.menuCamera;
            switch (_menuShot)
            {
                case 0:
                    BeginPhoto(document);
                    _shotTicks = 0;
                    _menuShot = 1;
                    return false;
                case 1:
                    if (++_shotTicks < 8) return false;
                    if (!EnglishOnly(document, "o menu inicial")) return false;
                    if (!TakePhoto(camera, Path.Combine(EnglishFolder, "menu_inicial_" + SceneName + ".png"), 0.08f, "o menu inicial")) return false;
                    // A tela de fim da fita é uma só: fotografa-se na primeira fase.
                    if (Index != 0) { _menuShot = 4; return true; }
                    typeof(ConnectionMenu).GetMethod("ShowEnding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(menu, null);
                    BeginPhoto(document);
                    _shotTicks = 0;
                    _menuShot = 2;
                    return false;
                case 2:
                    if (++_shotTicks < 8) return false;
                    if (!EnglishOnly(document, "a tela de fim da fita")) return false;
                    if (!TakePhoto(camera, Path.Combine(EnglishFolder, "fim_da_fita.png"), 0.5f, "a tela de fim da fita")) return false;
                    // O painel do menu some enquanto o fim da fita está na tela (antes aparecia
                    // apagado por trás, com o texto por cima de END OF TAPE).
                    if (MenuPanelShown(menu)) { Finish(false, "com o fim da fita na tela, o painel do menu continua aparecendo por trás"); return false; }
                    Directory.CreateDirectory(EndingFolder);
                    File.Copy(Path.Combine(EnglishFolder, "fim_da_fita.png"), Path.Combine(EndingFolder, "fim_da_fita.png"), true);
                    Button back = null;
                    document.rootVisualElement.Query<Button>().ForEach(b => { if (b.text == "Back to menu") back = b; });
                    if (back == null) { Finish(false, "a tela de fim da fita não tem o botão \"Back to menu\""); return false; }
                    using (NavigationSubmitEvent e = NavigationSubmitEvent.GetPooled())
                    {
                        e.target = back;
                        back.SendEvent(e);
                    }
                    _menuShot = 3;
                    return false;
                case 3:
                case 4:
                {
                    if (ConnectionMenu.EndingShown) { Finish(false, "\"Back to menu\" não fechou a tela de fim da fita"); return false; }
                    if (Index == 0 && !MenuPanelShown(menu)) { Finish(false, "depois de \"Back to menu\" o painel do menu não voltou"); return false; }
                    if (Index != 0 || _menuShot == 4) return true;
                    // A galeria pelo menu inicial, com a pasta vazia: diz como gravar.
                    string empty = Path.GetFullPath(EmptyFolder);
                    if (Directory.Exists(empty)) Directory.Delete(empty, true);
                    ScreenRecorder.FolderOverride = empty;
                    if (!Click(document, RecordingsScreen.ButtonText)) { Finish(false, "o menu inicial não tem o botão \"Recordings\""); return false; }
                    _shotTicks = 0;
                    _menuShot = 5;
                    return false;
                }
                case 5:
                {
                    if (++_shotTicks < 4) return false;
                    if (menu.Recordings == null || !menu.Recordings.IsOpen) { Finish(false, "\"Recordings\" no menu inicial não abriu a galeria"); return false; }
                    string rows = Rows(document);
                    if (menu.Recordings.Clips.Count != 0 || !rows.Contains("F9")) { Finish(false, $"com a pasta vazia, a galeria não diz como gravar: {OneLine(rows)}"); return false; }
                    if (!EnglishOnly(document, "a galeria vazia (menu inicial)")) return false;
                    if (!Click(document, "Back", true)) { Finish(false, "a galeria não tem o botão Back"); return false; }
                    _shotTicks = 0;
                    _menuShot = 6;
                    return false;
                }
                default:
                    if (++_shotTicks < 3) return false;
                    if (menu.Recordings.IsOpen) { Finish(false, "Back não fechou a galeria do menu inicial"); return false; }
                    if (!MenuPanelShown(menu)) { Finish(false, "fechada a galeria, o painel do menu não está à mostra"); return false; }
                    Notes.Add("menu inicial: Recordings abre a galeria vazia, que diz como gravar (F9), e Back fecha");
                    ScreenRecorder.FolderOverride = null;
                    return true;
            }
        }

        // ---------------------------------------------------------------- Galeria das gravações

        // Pelo menu de pausa aberto: grava um clipe curto, abre a galeria, Play e Open folder, apaga
        // com confirmação, confere a lista vazia e fecha com Esc (o menu de pausa continua aberto).
        // Devolve verdadeiro quando terminou; falhando, chama Finish.
        static bool Gallery(PauseMenu menu)
        {
            ScreenRecorder recorder = ScreenRecorder.Instance;
            RecordingsScreen screen = menu.Recordings;
            UIDocument document = menu.Document;
            _shotTicks++;
            switch (_galleryStep)
            {
                case 1:
                {
                    if (recorder == null) { Finish(false, "a cena não tem o gravador (ScreenRecorder)"); return false; }
                    string folder = Path.GetFullPath(GalleryFolder);
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                    ScreenRecorder.FolderOverride = folder;
                    RecordingsScreen.OpenOverride = url => Opened.Add(url);
                    // Sem janela a câmera só desenha numa textura (os quadros do vídeo saem dela).
                    if (Application.isBatchMode)
                    {
                        _galleryTarget = new RenderTexture(1280, 720, 24);
                        _local.playerCamera.targetTexture = _galleryTarget;
                    }
                    if (!recorder.Begin()) { Finish(false, "o gravador não começou: " + recorder.LastError); return false; }
                    _galleryAt = Now;
                    _galleryStep = 2;
                    return false;
                }
                case 2:
                    if (Now - _galleryAt < 1.6f) return false;
                    recorder.Stop();
                    _local.playerCamera.targetTexture = null;
                    if (_galleryTarget != null) { _galleryTarget.Release(); Object.Destroy(_galleryTarget); _galleryTarget = null; }
                    _galleryClip = recorder.LastFile;
                    if (!File.Exists(_galleryClip)) { Finish(false, "o clipe do teste não foi salvo: " + recorder.LastError); return false; }
                    if (!Click(document, RecordingsScreen.ButtonText)) { Finish(false, "o menu de pausa não tem o botão \"Recordings\""); return false; }
                    _shotTicks = 0;
                    _galleryStep = 3;
                    return false;
                case 3:
                {
                    if (_shotTicks < 4) return false;
                    if (screen == null || !screen.IsOpen) { Finish(false, "\"Recordings\" no menu de pausa não abriu a galeria"); return false; }
                    if (!PauseMenu.IsOpen) { Finish(false, "aberta a galeria, o menu de pausa fechou"); return false; }
                    if (screen.Clips.Count != 1) { Finish(false, $"a galeria lista {screen.Clips.Count} clipes (gravado 1)"); return false; }
                    RecordingsScreen.Clip clip = screen.Clips[0];
                    string length = RecordingsScreen.Duration(clip.Seconds);
                    string rows = Rows(document);
                    if (!clip.Finished || clip.Seconds < 1f || !rows.Contains(length) || !rows.Contains(RecordingsScreen.Size(clip.Bytes)))
                    { Finish(false, $"a linha do clipe está errada (fechado={clip.Finished}, {clip.Seconds:F2} s): {OneLine(rows)}"); return false; }
                    if (!EnglishOnly(document, "a galeria com um clipe")) return false;
                    Opened.Clear();
                    if (!Click(document, "Play")) { Finish(false, "a linha do clipe não tem Play"); return false; }
                    if (!Click(document, "Open folder")) { Finish(false, "a galeria não tem Open folder"); return false; }
                    string expected = new System.Uri(_galleryClip).AbsoluteUri;
                    if (Opened.Count != 2 || Opened[0] != expected || !Opened[1].StartsWith("file:///") || !expected.StartsWith(Opened[1]))
                    { Finish(false, $"Play/Open folder abriram {string.Join(" e ", Opened)} (esperado {expected} e a pasta)"); return false; }
                    Notes.Add($"galeria: 1 clipe ({OneLine(rows)}); Play abre {expected}; Open folder abre {Opened[1]}");
                    BeginPhoto(document);
                    _shotTicks = 0;
                    _galleryStep = 4;
                    return false;
                }
                case 4:
                    if (_shotTicks < 8) return false;
                    if (!TakePhoto(_local.playerCamera, Path.Combine(FilmFolder, "gravacoes_um_clipe.png"), 0.2f, "a galeria com um clipe")) return false;
                    if (!Click(document, "Delete")) { Finish(false, "a linha do clipe não tem Delete"); return false; }
                    _shotTicks = 0;
                    _galleryStep = 5;
                    return false;
                case 5:
                    if (_shotTicks < 3) return false;
                    if (!screen.Confirming(_galleryClip) || !Rows(document).Contains("Delete this clip?")) { Finish(false, "Delete não pediu confirmação"); return false; }
                    if (!File.Exists(_galleryClip)) { Finish(false, "Delete apagou antes de confirmar"); return false; }
                    BeginPhoto(document);
                    _shotTicks = 0;
                    _galleryStep = 6;
                    return false;
                case 6:
                    if (_shotTicks < 8) return false;
                    if (!TakePhoto(_local.playerCamera, Path.Combine(FilmFolder, "gravacoes_confirmar.png"), 0.2f, "a confirmação de apagar")) return false;
                    if (!Click(document, "Yes, delete")) { Finish(false, "a confirmação não tem \"Yes, delete\""); return false; }
                    _shotTicks = 0;
                    _galleryStep = 7;
                    return false;
                case 7:
                {
                    if (_shotTicks < 3) return false;
                    if (File.Exists(_galleryClip)) { Finish(false, "confirmado, o clipe não foi apagado"); return false; }
                    string rows = Rows(document);
                    if (screen.Clips.Count != 0 || !rows.Contains("F9")) { Finish(false, $"apagado o clipe, a galeria não ficou vazia: {OneLine(rows)}"); return false; }
                    if (!EnglishOnly(document, "a galeria vazia")) return false;
                    BeginPhoto(document);
                    _shotTicks = 0;
                    _galleryStep = 8;
                    return false;
                }
                case 8:
                    if (_shotTicks < 8) return false;
                    if (!TakePhoto(_local.playerCamera, Path.Combine(FilmFolder, "gravacoes_vazia.png"), 0.2f, "a galeria vazia")) return false;
                    _shotTicks = 0;
                    _galleryStep = 9;
                    return false;
                default:
                    // Esc fecha só a galeria; o menu de pausa continua aberto.
                    if (_shotTicks == 1) { Keys(Key.Escape); return false; }
                    if (_shotTicks == 3) Keys();
                    if (_shotTicks < 6) return false;
                    if (screen.IsOpen) { Finish(false, "Esc não fechou a galeria"); return false; }
                    if (!PauseMenu.IsOpen) { Finish(false, "o Esc na galeria fechou também o menu de pausa"); return false; }
                    Notes.Add("Delete pediu confirmação e apagou; a galeria vazia diz como gravar; Esc fechou só a galeria");
                    ScreenRecorder.FolderOverride = null;
                    RecordingsScreen.OpenOverride = null;
                    return true;
            }
        }

        // O texto das linhas da galeria como está desenhado agora (inclusive a confirmação).
        static string Rows(UIDocument document)
        {
            var text = new System.Text.StringBuilder();
            document.rootVisualElement.Query<Label>(className: RecordingsScreen.RowClass).ForEach(l => text.Append(l.text).Append('\n'));
            return text.ToString();
        }

        // Aperta o botão visível com este texto (ou que começa com ele).
        static bool Click(UIDocument document, string text, bool prefix = false)
        {
            Button found = null;
            document.rootVisualElement.Query<Button>().ForEach(b =>
            {
                if (found != null || b.text == null) return;
                if (prefix ? !b.text.StartsWith(text) : b.text != text) return;
                for (VisualElement e = b; e != null; e = e.parent)
                    if (e.resolvedStyle.display == DisplayStyle.None) return;
                found = b;
            });
            if (found == null) return false;
            using (NavigationSubmitEvent e = NavigationSubmitEvent.GetPooled())
            {
                e.target = found;
                found.SendEvent(e);
            }
            return true;
        }

        // O painel do menu inicial (campo privado _panel do ConnectionMenu) está à mostra?
        static bool MenuPanelShown(ConnectionMenu menu)
        {
            var panel = typeof(ConnectionMenu).GetField("_panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(menu) as VisualElement;
            return panel != null && panel.style.display.value != DisplayStyle.None;
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

        // ---------------------------------------------------------------- Ajuda

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        static string OneLine(string text) => text.Replace('\n', ' ').Trim();

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

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;

            if (_photoPanel != null && _photoDocument != null) _photoDocument.panelSettings = _photoOriginal;
            if (ScreenRecorder.Instance != null) ScreenRecorder.Instance.Stop();
            ScreenRecorder.FolderOverride = null;
            RecordingsScreen.OpenOverride = null;
            if (_galleryTarget != null)
            {
                if (_local != null && _local.playerCamera != null) _local.playerCamera.targetTexture = null;
                _galleryTarget.Release();
                Object.Destroy(_galleryTarget);
                _galleryTarget = null;
            }
            SwitchObjective.PlayersForTest = 0;
            EntityDirector.Paused = false;
            PoolTide.Paused = false;
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

            Debug.Log($"[BackRooms] Teste do menu de pausa ({SceneName}) " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
            if (!ok) SessionState.SetBool(FailedKey, true);

            // Mais uma fase na lista: sai do Play e abre a próxima (AfterPlay).
            SessionState.SetInt(ExitCodeKey, 0);
            EditorApplication.playModeStateChanged -= AfterPlay;
            EditorApplication.playModeStateChanged += AfterPlay;
            EditorApplication.ExitPlaymode();
        }

        static void AfterPlay(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= AfterPlay;
            SessionState.EraseInt(ExitCodeKey);

            // Os estáticos podem não ter sido recarregados entre uma fase e outra.
            Errors.Clear();
            Notes.Clear();
            _stage = 0;
            _stageTicks = 0;
            _began = 0.0;
            _finished = false;
            _local = null;
            _rehosted = false;
            _menuShot = 0;
            _menuChecked = false;
            _galleryStep = 0;
            _panelStep = 0;
            _panelTicks = 0;

            int next = Index + 1;
            if (next < Scenes.Length)
            {
                EditorApplication.delayCall += () => OpenAndPlay(next);
                return;
            }

            bool failed = SessionState.GetBool(FailedKey, false);
            Debug.Log("[BackRooms] Teste do menu de pausa (todas) " + (failed ? "FALHOU" : "PASSOU") + $": {Scenes.Length} fase(s).");
            if (SessionState.GetBool(BatchKey, false)) Level1Setup.ExitWithLevel0Open(failed ? 3 : 0);
        }
    }
}
