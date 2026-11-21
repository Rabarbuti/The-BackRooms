using System.Collections.Generic;
using System.Text;
using BackRooms.Net;
using BackRooms.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BackRooms.UI
{
    /// <summary>
    /// Menu de pausa (Esc): as tarefas da fase com o estado ao vivo, as teclas que valem nela e os
    /// botões Continuar, Opções e Sair para o menu. Montado em código com UI Toolkit, no mesmo visual
    /// do menu inicial (<see cref="MenuKit"/>), num documento próprio criado pelo
    /// <see cref="ConnectionMenu"/> (o dele se limpa inteiro ao ligar).
    ///
    /// É co-op em rede: <b>o jogo não para</b>. O tempo, a maré e o monstro continuam; aberto, o
    /// jogador só deixa de comandar (andar, olhar, itens, E), lendo <see cref="IsOpen"/>. Abrir solta o
    /// mouse; fechar prende de novo.
    ///
    /// As tarefas não são escritas aqui: cada objetivo da fase as publica (<see cref="LevelTasks"/>),
    /// e o menu só desenha.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PauseMenu : MonoBehaviour
    {
        // ---------------------------------------------------------------- Textos (a língua do jogo é o inglês)
        const string RunningText = "The tape keeps rolling: the game does not stop while this is open.";
        const string TasksTitle = "TASKS";
        const string KeysTitle = "KEYS";
        const string ContinueText = "Continue        [Esc]";
        const string LeaveText = "Quit to menu";
        const string DoneMark = "[X]";
        const string TodoMark = "[  ]";
        static readonly (string key, string action)[] BaseKeys =
        {
            ("WASD", "move"),
            ("Shift", "run"),
            ("Ctrl / C", "crouch"),
            ("Space", "jump"),
            ("F", "flashlight"),
            ("1  2  3", "pen: black, blue, red (again: put away)"),
            ("Left mouse", "draw with the pen"),
            ("E", "open the exit door (again: let go)"),
        };
        static readonly (string key, string action) BatteryKey = ("Aim + E", "pick up batteries");
        static readonly (string key, string action) HideKey = ("Aim + E", "hide (crouch for tables)");
        static readonly (string key, string action)[] WaterKeys =
        {
            ("Hold Space", "stay afloat in deep water"),
            ("Ctrl / C", "dive"),
            ("W at a low edge", "climb out of the water"),
        };
        static readonly (string key, string action)[] EndKeys =
        {
            ("R", "record the screen (again: stop; clips in Recordings)"),
            ("Esc", "tasks / back to the game"),
        };

        /// <summary>Classe dos rótulos de tarefa (os testes leem o que está desenhado).</summary>
        public const string TaskClass = "pausa-tarefa";
        public const string KeyClass = "pausa-tecla";

        // Largura dos quatro botões da fileira de baixo.
        const float ButtonWidth = 266f;

        public GameSession session;

        /// <summary>
        /// O menu de pausa desta máquina está aberto: o jogador local não recebe entrada de
        /// movimento, olhar nem itens. O ponto central que <c>PlayerController</c>, <c>PenTool</c>,
        /// <c>Flashlight</c> e os usos de E leem.
        /// </summary>
        public static bool IsOpen { get; private set; }

        public static PauseMenu Instance { get; private set; }

        /// <summary>O documento (UI Toolkit) do menu, para os testes fotografarem.</summary>
        public UIDocument Document { get; private set; }

        InputAction _toggle;
        VisualElement _overlay;
        Label _title;
        VisualElement _tasksBox;
        VisualElement _keysBox;
        MenuKit.OptionsPanel _options;
        RecordingsScreen _recordings;
        readonly List<LevelTask> _tasks = new List<LevelTask>();
        readonly List<(string key, string action)> _keys = new List<(string key, string action)>();
        readonly StringBuilder _sb = new StringBuilder();
        string _drawnTasks, _drawnKeys;

        void Awake()
        {
            _toggle = new InputAction("Pausa", InputActionType.Button, "<Keyboard>/escape");
            _toggle.AddBinding("<Gamepad>/start");
        }

        void OnEnable()
        {
            Instance = this;
            Document = GetComponent<UIDocument>();
            Build(Document.rootVisualElement);
            _toggle.Enable();
            IsOpen = false;
        }

        void OnDisable()
        {
            _toggle.Disable();
            IsOpen = false;
            if (Instance == this) Instance = null;
        }

        void OnDestroy() => _toggle.Dispose();

        // ---------------------------------------------------------------- Montagem

        void Build(VisualElement root)
        {
            root.Clear();
            root.style.flexGrow = 1f;
            root.pickingMode = PickingMode.Ignore;

            _overlay = new VisualElement();
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0; _overlay.style.right = 0; _overlay.style.top = 0; _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.display = DisplayStyle.None;
            root.Add(_overlay);

            var panel = new VisualElement();
            panel.style.width = 1240;
            panel.style.paddingLeft = 40; panel.style.paddingRight = 40;
            panel.style.paddingTop = 30; panel.style.paddingBottom = 30;
            panel.style.backgroundColor = MenuKit.PanelColor;
            panel.style.borderTopWidth = 2; panel.style.borderBottomWidth = 2;
            panel.style.borderLeftWidth = 2; panel.style.borderRightWidth = 2;
            Color edge = MenuKit.Yellow * new Color(1f, 1f, 1f, 0.45f);
            panel.style.borderTopColor = edge; panel.style.borderBottomColor = edge;
            panel.style.borderLeftColor = edge; panel.style.borderRightColor = edge;
            MenuKit.Round(panel);
            _overlay.Add(panel);

            _title = MenuKit.Text("", 34, MenuKit.Yellow);
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            panel.Add(_title);
            var running = MenuKit.Text(RunningText, 15, MenuKit.Paper);
            running.style.opacity = 0.65f;
            running.style.marginBottom = 22;
            panel.Add(running);

            var columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            panel.Add(columns);

            var left = new VisualElement();
            left.style.width = 660;
            left.style.marginRight = 40;
            columns.Add(left);
            left.Add(Heading(TasksTitle));
            _tasksBox = new VisualElement();
            left.Add(_tasksBox);

            var right = new VisualElement();
            right.style.flexGrow = 1f;
            columns.Add(right);
            right.Add(Heading(KeysTitle));
            _keysBox = new VisualElement();
            right.Add(_keysBox);

            // Os três botões numa fileira, do mesmo tamanho; as linhas de opções abrem embaixo dela.
            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 26;
            panel.Add(buttons);
            var optionsHolder = new VisualElement();
            panel.Add(optionsHolder);
            _options = MenuKit.AddOptions(optionsHolder);
            Button toggle = _options.ToggleButton;
            toggle.RemoveFromHierarchy();

            Button resume = MenuKit.MenuButton(ContinueText, () => SetOpen(false));
            Button leave = MenuKit.MenuButton(LeaveText, Leave);
            Button recordings = MenuKit.MenuButton(RecordingsScreen.ButtonText, () => _recordings.Show());
            Button[] row = { resume, toggle, recordings, leave };
            for (int i = 0; i < row.Length; i++)
            {
                Button b = row[i];
                b.style.width = ButtonWidth;
                b.style.height = 48;
                b.style.fontSize = 19;
                b.style.marginTop = 0; b.style.marginBottom = 0;
                b.style.marginLeft = 0; b.style.marginRight = i < row.Length - 1 ? 14 : 0;
                buttons.Add(b);
            }
            _options.Root.style.marginTop = 12;
            _options.Root.style.marginLeft = ButtonWidth + 14;

            // A galeria das gravações (F9), por cima do painel.
            _recordings = new RecordingsScreen(_overlay);
        }

        /// <summary>A galeria das gravações deste menu (testes).</summary>
        public RecordingsScreen Recordings => _recordings;

        static Label Heading(string text)
        {
            Label label = MenuKit.Text(text, 18, MenuKit.Yellow);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 10;
            label.style.letterSpacing = 3;
            return label;
        }

        // ---------------------------------------------------------------- Jogo

        void Update()
        {
            bool playing = session != null && session.Phase == SessionPhase.Playing && !session.Passing && PlayerNetwork.Local != null;
            if (!playing)
            {
                if (IsOpen) SetOpen(false, false);
                return;
            }

            // Com o painel do código na tela, o Esc é dele (fecha o painel, não abre este menu).
            // Com a galeria aberta, o Esc fecha só ela.
            if (_toggle.WasPressedThisFrame() && !CodePanel.TakesInput)
            {
                if (_recordings != null && _recordings.IsOpen) _recordings.Hide();
                else SetOpen(!IsOpen);
            }
            if (IsOpen) Refresh();
        }

        /// <summary>Abre ou fecha. Abrir solta o mouse; fechar o prende de novo (se <paramref name="relock"/>).</summary>
        public void SetOpen(bool open, bool relock = true)
        {
            IsOpen = open;
            if (!open) _recordings?.Hide();
            if (_overlay != null) _overlay.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
            {
                _options?.SetOpen(false);
                _drawnTasks = _drawnKeys = null;
                Refresh();
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }
            else if (relock)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                UnityEngine.Cursor.visible = false;
            }
        }

        void Leave()
        {
            SetOpen(false, false);
            session?.Leave();
        }

        /// <summary>Redesenha as tarefas e as teclas, só quando o texto muda.</summary>
        public void Refresh()
        {
            if (_tasksBox == null) return;
            LevelInfo level = session != null ? LevelCatalog.Get(session.LevelIndex) : null;
            _title.text = level != null ? level.Name.ToUpperInvariant() + "  —  " + level.Subtitle : "";

            LevelTasks.Collect(_tasks);
            _sb.Clear();
            foreach (LevelTask t in _tasks) _sb.Append(t.Done).Append('|').Append(t.Text).Append('|').Append(t.Detail).Append('\n');
            string tasks = _sb.ToString();
            if (tasks != _drawnTasks)
            {
                _drawnTasks = tasks;
                _tasksBox.Clear();
                foreach (LevelTask t in _tasks) _tasksBox.Add(TaskRow(t));
            }

            CollectKeys(_keys);
            _sb.Clear();
            foreach (var k in _keys) _sb.Append(k.key).Append('|').Append(k.action).Append('\n');
            string keys = _sb.ToString();
            if (keys != _drawnKeys)
            {
                _drawnKeys = keys;
                _keysBox.Clear();
                foreach (var k in _keys) _keysBox.Add(KeyRow(k.key, k.action));
            }
        }

        // As teclas que valem nesta fase: as de sempre, as do objetivo e as do que existe na fase.
        static void CollectKeys(List<(string key, string action)> keys)
        {
            keys.Clear();
            keys.AddRange(BaseKeys);
            if (BatteryPickups.Instance != null) keys.Add(BatteryKey);
            LevelTasks.CollectKeys(keys);
            if (HideSpot.All.Count > 0) keys.Add(HideKey);
            if (PoolWater.Current != null) keys.AddRange(WaterKeys);
            keys.AddRange(EndKeys);
        }

        static VisualElement TaskRow(LevelTask task)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 12;

            Label mark = MenuKit.Text(task.Done ? DoneMark : TodoMark, 20, task.Done ? MenuKit.Green : MenuKit.Yellow);
            mark.style.width = 46;
            mark.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(mark);

            var body = new VisualElement();
            body.style.flexShrink = 1f;
            body.style.flexGrow = 1f;
            row.Add(body);
            Label text = MenuKit.Text(task.Text, 21, task.Done ? MenuKit.Green : MenuKit.Paper);
            text.AddToClassList(TaskClass);
            text.style.whiteSpace = WhiteSpace.Normal;
            if (task.Done) text.style.opacity = 0.8f;
            body.Add(text);
            if (!string.IsNullOrEmpty(task.Detail))
            {
                Label detail = MenuKit.Text(task.Detail, 15, MenuKit.Paper);
                detail.AddToClassList(TaskClass);
                detail.style.whiteSpace = WhiteSpace.Normal;
                detail.style.opacity = 0.7f;
                detail.style.marginTop = 2;
                body.Add(detail);
            }
            return row;
        }

        static VisualElement KeyRow(string key, string action)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 4;
            Label k = MenuKit.Text(key, 15, MenuKit.Yellow);
            k.style.width = 170;
            k.AddToClassList(KeyClass);
            row.Add(k);
            Label a = MenuKit.Text(action, 15, MenuKit.Paper);
            a.style.whiteSpace = WhiteSpace.Normal;
            a.style.flexShrink = 1f;
            a.AddToClassList(KeyClass);
            row.Add(a);
            return row;
        }

        /// <summary>O texto das tarefas como está desenhado agora (uma linha por rótulo), para os testes.</summary>
        public string ShownTasks()
        {
            if (Document == null) return "";
            var text = new StringBuilder();
            Document.rootVisualElement.Query<Label>(className: TaskClass).ForEach(l => text.Append(l.text).Append('\n'));
            return text.ToString();
        }

        /// <summary>O texto das teclas como está desenhado agora, para os testes.</summary>
        public string ShownKeys()
        {
            if (Document == null) return "";
            var text = new StringBuilder();
            Document.rootVisualElement.Query<Label>(className: KeyClass).ForEach(l => text.Append(l.text).Append(' '));
            return text.ToString();
        }

        /// <summary>O painel está visível (desenhado) agora.</summary>
        public bool Shown => _overlay != null && _overlay.resolvedStyle.display == DisplayStyle.Flex;
    }
}
