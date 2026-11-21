using BackRooms.Net;
using BackRooms.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BackRooms.UI
{
    /// <summary>
    /// O painel do código da porta do Level 1 (<see cref="ExitLock"/>), na tela: mira no teclado da
    /// parede + E abre; digita-se pelos números do teclado (fileira de cima e numérico) ou clicando
    /// nas teclas com o mouse; Backspace apaga, Enter confirma, Esc ou E fecha. Código errado: o visor
    /// pisca vermelho, soa o erro e limpa; certo: soa, fecha e a porta destranca (o cliente manda o
    /// código ao host, como antes).
    ///
    /// <b>O jogo não para</b> com o painel aberto (o Capitão continua vindo): o jogador só deixa de
    /// comandar, pelo mesmo ponto do menu de pausa (<see cref="InputGate"/>). Abrir solta o mouse;
    /// fechar prende de novo. Fecha sozinho se o jogador for pego, cair, a porta destrancar ou a fase
    /// trocar. Num documento próprio (UI Toolkit, peças do <see cref="MenuKit"/>), criado na primeira
    /// vez que abre, ao lado do menu de pausa e com o mesmo PanelSettings.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CodePanel : MonoBehaviour
    {
        // ---------------------------------------------------------------- Textos (a língua do jogo é o inglês)
        const string TitleText = "KEYPAD";
        const string HintText = "Type the 3-digit door code.";
        const string KeysText = "0-9 type  |  Backspace erase  |  Enter confirm  |  Esc / E close";
        const string CloseText = "Close        [Esc]";
        const string EraseLabel = "DEL";
        const string EnterLabel = "OK";

        /// <summary>Classe do visor (os testes leem o que está desenhado).</summary>
        public const string VisorClass = "codigo-visor";

        // Quanto o painel fica na tela mostrando o código aceito antes de fechar.
        const float AcceptSeconds = 0.6f;

        static readonly Color VisorBack = new Color(0.1f, 0.16f, 0.11f);
        static readonly Color VisorErrorBack = new Color(0.45f, 0.05f, 0.04f);
        static readonly Color VisorInk = new Color(0.67f, 0.9f, 0.59f);
        static readonly Color VisorErrorInk = new Color(1f, 0.35f, 0.28f);
        static readonly Color VisorOkInk = new Color(0.45f, 1f, 0.5f);

        public static CodePanel Instance { get; private set; }

        /// <summary>O painel está aberto nesta máquina: o jogador local não comanda.</summary>
        public static bool IsOpen { get; private set; }

        static int _closedFrame = -1;

        /// <summary>Aberto, ou fechou neste quadro (o Esc ou o E que fechou não vale para mais nada).</summary>
        public static bool TakesInput => IsOpen || _closedFrame == Time.frameCount;

        /// <summary>O documento (UI Toolkit) do painel, para os testes fotografarem.</summary>
        public UIDocument Document { get; private set; }

        /// <summary>O texto do visor agora e se está vermelho (erro), para os testes.</summary>
        public string VisorText => _visor != null ? _visor.text : "";
        public bool VisorError => _lock != null && _lock.ShowingError;

        ExitLock _lock;
        int _openedFrame = -1;
        float _acceptedAt = -1f;
        InputAction[] _digits;
        InputAction _erase, _enter, _close;
        VisualElement _overlay, _visorBox;
        Label _visor;

        /// <summary>Abre o painel para o teclado <paramref name="exitLock"/> (cria o documento na primeira vez).</summary>
        public static void Open(ExitLock exitLock)
        {
            CodePanel panel = Ensure();
            if (panel != null) panel.Show(exitLock);
        }

        /// <summary>Fecha, se estiver aberto. <paramref name="relock"/>: prende o mouse de novo.</summary>
        public static void CloseIfOpen(bool relock = true)
        {
            if (Instance != null && IsOpen) Instance.Close(relock);
        }

        static CodePanel Ensure()
        {
            if (Instance != null) return Instance;
            UIDocument template = PauseMenu.Instance != null ? PauseMenu.Instance.Document : null;
            if (template == null || template.panelSettings == null) { ConnectionMenu menu = FindAnyObjectByType<ConnectionMenu>(); template = menu != null ? menu.GetComponent<UIDocument>() : null; }
            if (template == null || template.panelSettings == null) return null;

            var holder = new GameObject("PainelCodigo");
            holder.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, template.gameObject.scene);
            UIDocument document = holder.AddComponent<UIDocument>();
            document.panelSettings = template.panelSettings;
            document.sortingOrder = template.sortingOrder + 5;
            CodePanel panel = holder.AddComponent<CodePanel>();
            holder.SetActive(true);
            return panel;
        }

        void Awake()
        {
            _digits = new InputAction[10];
            for (int i = 0; i < 10; i++)
            {
                _digits[i] = new InputAction("Digito" + i, InputActionType.Button, "<Keyboard>/" + i);
                _digits[i].AddBinding("<Keyboard>/numpad" + i);
            }
            _erase = new InputAction("Apagar", InputActionType.Button, "<Keyboard>/backspace");
            _erase.AddBinding("<Keyboard>/delete");
            _enter = new InputAction("Confirmar", InputActionType.Button, "<Keyboard>/enter");
            _enter.AddBinding("<Keyboard>/numpadEnter");
            _close = new InputAction("Fechar", InputActionType.Button, "<Keyboard>/escape");
            _close.AddBinding("<Keyboard>/e");
            _close.AddBinding("<Gamepad>/buttonEast");
        }

        void OnEnable()
        {
            Instance = this;
            Document = GetComponent<UIDocument>();
            Build(Document.rootVisualElement);
            foreach (InputAction a in _digits) a.Enable();
            _erase.Enable(); _enter.Enable(); _close.Enable();
            IsOpen = false;
        }

        void OnDisable()
        {
            foreach (InputAction a in _digits) a.Disable();
            _erase.Disable(); _enter.Disable(); _close.Disable();
            if (IsOpen) { IsOpen = false; _closedFrame = Time.frameCount; }
            if (Instance == this) Instance = null;
        }

        void OnDestroy()
        {
            foreach (InputAction a in _digits) a.Dispose();
            _erase.Dispose(); _enter.Dispose(); _close.Dispose();
        }

        // ---------------------------------------------------------------- Montagem

        void Build(VisualElement root)
        {
            root.Clear();
            root.style.flexGrow = 1f;
            root.pickingMode = PickingMode.Ignore;

            _overlay = new VisualElement();
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0; _overlay.style.right = 0; _overlay.style.top = 0; _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.display = DisplayStyle.None;
            root.Add(_overlay);

            var panel = new VisualElement();
            panel.style.width = 440;
            panel.style.paddingLeft = 30; panel.style.paddingRight = 30;
            panel.style.paddingTop = 24; panel.style.paddingBottom = 24;
            panel.style.backgroundColor = MenuKit.PanelColor;
            panel.style.borderTopWidth = 2; panel.style.borderBottomWidth = 2;
            panel.style.borderLeftWidth = 2; panel.style.borderRightWidth = 2;
            Color edge = MenuKit.Yellow * new Color(1f, 1f, 1f, 0.45f);
            panel.style.borderTopColor = edge; panel.style.borderBottomColor = edge;
            panel.style.borderLeftColor = edge; panel.style.borderRightColor = edge;
            panel.style.alignItems = Align.Center;
            MenuKit.Round(panel);
            _overlay.Add(panel);

            Label title = MenuKit.Text(TitleText, 30, MenuKit.Yellow);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 6;
            panel.Add(title);
            Label hint = MenuKit.Text(HintText, 15, MenuKit.Paper);
            hint.style.opacity = 0.7f;
            panel.Add(hint);

            // O visor dos três dígitos.
            _visorBox = new VisualElement();
            _visorBox.style.width = 348;
            _visorBox.style.height = 96;
            _visorBox.style.marginTop = 16; _visorBox.style.marginBottom = 18;
            _visorBox.style.backgroundColor = VisorBack;
            _visorBox.style.borderTopWidth = 3; _visorBox.style.borderBottomWidth = 3;
            _visorBox.style.borderLeftWidth = 3; _visorBox.style.borderRightWidth = 3;
            Color rim = new Color(0.02f, 0.02f, 0.02f);
            _visorBox.style.borderTopColor = rim; _visorBox.style.borderBottomColor = rim;
            _visorBox.style.borderLeftColor = rim; _visorBox.style.borderRightColor = rim;
            _visorBox.style.alignItems = Align.Center;
            _visorBox.style.justifyContent = Justify.Center;
            MenuKit.Round(_visorBox, 4f);
            panel.Add(_visorBox);
            _visor = MenuKit.Text("---", 62, VisorInk);
            _visor.style.unityFontStyleAndWeight = FontStyle.Bold;
            _visor.style.letterSpacing = 22;
            _visor.AddToClassList(VisorClass);
            _visorBox.Add(_visor);

            // As teclas: 1 a 9, DEL, 0, OK.
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.width = 354;
            panel.Add(grid);
            string[] labels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", EraseLabel, "0", EnterLabel };
            foreach (string label in labels)
            {
                string key = label == EraseLabel ? ExitLock.EraseKey : label == EnterLabel ? ExitLock.EnterKey : label;
                Button button = MenuKit.MenuButton(label, () => Click(key));
                button.style.width = 110;
                button.style.height = 66;
                button.style.fontSize = label.Length > 1 ? 22 : 30;
                button.style.marginLeft = 4; button.style.marginRight = 4;
                button.style.marginTop = 4; button.style.marginBottom = 4;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                if (label == EnterLabel) button.style.backgroundColor = MenuKit.CurrentColor;
                // Sem foco de teclado: o Enter confirma pelo painel, não "clica" de novo a última tecla.
                button.focusable = false;
                grid.Add(button);
            }

            Label keys = MenuKit.Text(KeysText, 13, MenuKit.Paper);
            keys.style.opacity = 0.65f;
            keys.style.marginTop = 14;
            keys.style.whiteSpace = WhiteSpace.Normal;
            keys.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(keys);

            Button close = MenuKit.MenuButton(CloseText, () => Close(true));
            close.style.width = 354;
            close.style.height = 40;
            close.style.fontSize = 16;
            close.style.marginTop = 12;
            close.focusable = false;
            panel.Add(close);
        }

        // ---------------------------------------------------------------- Jogo

        void Show(ExitLock exitLock)
        {
            if (exitLock == null) return;
            _lock = exitLock;
            IsOpen = true;
            _openedFrame = Time.frameCount;
            _acceptedAt = -1f;
            _overlay.style.display = DisplayStyle.Flex;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            Refresh();
        }

        void Close(bool relock)
        {
            if (!IsOpen) return;
            IsOpen = false;
            _closedFrame = Time.frameCount;
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
            if (relock)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                UnityEngine.Cursor.visible = false;
            }
        }

        void Click(string key)
        {
            if (!IsOpen || _lock == null || _lock.Accepted) return;
            _lock.Press(key);
            Refresh();
        }

        void Update()
        {
            if (!IsOpen) return;

            GameSession session = GameSession.Instance;
            PlayerNetwork local = PlayerNetwork.Local;
            bool playing = session != null && session.Phase == SessionPhase.Playing && !session.Passing && local != null && local.InGame;
            if (!playing) { Close(false); return; }
            if (_lock == null || local.controller.IsReturning) { Close(true); return; }

            // Aceito (aqui ou por outro jogador): mostra um instante e fecha.
            if (_lock.Accepted)
            {
                if (_acceptedAt < 0f) _acceptedAt = Time.unscaledTime;
                else if (Time.unscaledTime - _acceptedAt > AcceptSeconds) { Close(true); return; }
                Refresh();
                return;
            }

            // O E que abriu o painel não o fecha no mesmo quadro.
            if (Time.frameCount != _openedFrame)
            {
                if (_close.WasPressedThisFrame()) { Close(true); return; }
                for (int i = 0; i < _digits.Length; i++)
                    if (_digits[i].WasPressedThisFrame()) _lock.Press(i.ToString());
                if (_erase.WasPressedThisFrame()) _lock.Press(ExitLock.EraseKey);
                if (_enter.WasPressedThisFrame()) _lock.Press(ExitLock.EnterKey);
            }
            Refresh();
        }

        void Refresh()
        {
            if (_visor == null || _lock == null) return;
            string shown = _lock.Display;
            if (_visor.text != shown) _visor.text = shown;
            bool error = _lock.ShowingError;
            // Erro: o visor pisca vermelho enquanto o aviso dura.
            bool blink = error && Mathf.Repeat(Time.unscaledTime * 6f, 1f) < 0.5f;
            _visorBox.style.backgroundColor = blink ? VisorErrorBack : VisorBack;
            _visor.style.color = error ? VisorErrorInk : _lock.Accepted ? VisorOkInk : VisorInk;
        }

        /// <summary>O painel está visível (desenhado) agora.</summary>
        public bool Shown => _overlay != null && _overlay.resolvedStyle.display == DisplayStyle.Flex;
    }
}
