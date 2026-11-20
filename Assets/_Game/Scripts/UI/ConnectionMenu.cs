using System.Net;
using System.Net.Sockets;
using BackRooms.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BackRooms.UI
{
    /// <summary>
    /// Menu inicial: escolher a fase, hospedar (ou jogar sozinho) e entrar por IP. Montado em
    /// código com UI Toolkit. Atalhos de teclado: H hospeda, J entra no endereço digitado.
    /// A lista de fases mostra as zeradas e a próxima (<see cref="LevelProgress"/>); no modo
    /// desenvolvedor, todas.
    /// Durante o jogo vira uma linha de estado no canto e, com o mouse solto (Esc), um
    /// botão para sair da sessão.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ConnectionMenu : MonoBehaviour
    {
        public GameSession session;

        static readonly Color Yellow = new Color(0.87f, 0.78f, 0.36f);
        static readonly Color Paper = new Color(0.93f, 0.9f, 0.78f);
        static readonly Color PanelColor = new Color(0.04f, 0.035f, 0.02f, 0.82f);
        static readonly Color ButtonColor = new Color(0.33f, 0.29f, 0.12f);
        static readonly Color CurrentColor = new Color(0.55f, 0.47f, 0.16f);
        static readonly Color LockedColor = new Color(0.16f, 0.15f, 0.12f);

        VisualElement _panel;
        TextField _address;
        Label _status;
        Label _hud;
        Button _leave;
        Label _subtitle;
        Label _devNote;
        Label _notice;
        Button[] _levelButtons;
        VisualElement _options;
        Label _volumeValue, _mouseValue, _qualityValue;
        float _noticeUntil;
        string _localAddresses;

        void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1f;
            root.pickingMode = PickingMode.Ignore;

            BuildPanel(root);
            BuildHud(root);

            _localAddresses = FindLocalAddresses();
            LevelProgress.LevelCleared += OnLevelCleared;
        }

        void OnDisable()
        {
            LevelProgress.LevelCleared -= OnLevelCleared;
        }

        void OnLevelCleared(int index)
        {
            LevelInfo next = LevelCatalog.Get(index + 1);
            _notice.text = LevelCatalog.Get(index).Name.ToUpperInvariant() + " ZERADO" +
                           (next != null ? "   •   " + next.Name + " liberado no menu" : "");
            _noticeUntil = Time.unscaledTime + 8f;
        }

        void BuildPanel(VisualElement root)
        {
            var overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0; overlay.style.right = 0; overlay.style.top = 0; overlay.style.bottom = 0;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            root.Add(overlay);

            _panel = new VisualElement();
            _panel.style.width = 520;
            _panel.style.paddingLeft = 34; _panel.style.paddingRight = 34;
            _panel.style.paddingTop = 28; _panel.style.paddingBottom = 28;
            _panel.style.backgroundColor = PanelColor;
            _panel.style.borderTopLeftRadius = 6; _panel.style.borderTopRightRadius = 6;
            _panel.style.borderBottomLeftRadius = 6; _panel.style.borderBottomRightRadius = 6;
            overlay.Add(_panel);

            var title = Text("THE BACKROOMS", 40, Yellow);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginBottom = 4;
            _panel.Add(title);

            _subtitle = Text("", 16, Paper);
            _subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _subtitle.style.marginBottom = 22;
            _subtitle.style.opacity = 0.7f;
            _panel.Add(_subtitle);

            BuildLevelList();

            _panel.Add(MenuButton("Hospedar  (ou jogar sozinho)        [H]", () => session.Host()));

            var joinLabel = Text("Endereço de quem está hospedando", 14, Paper);
            joinLabel.style.marginTop = 22;
            joinLabel.style.opacity = 0.75f;
            _panel.Add(joinLabel);

            _address = new TextField { value = "127.0.0.1" };
            _address.style.fontSize = 20;
            _address.style.height = 40;
            _address.style.marginTop = 4;
            _address.style.marginBottom = 8;
            _panel.Add(_address);

            _panel.Add(MenuButton("Entrar        [J]", () => session.Join(_address.value)));

            BuildOptions();

            _status = Text("", 15, Paper);
            _status.style.marginTop = 18;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.unityTextAlign = TextAnchor.MiddleCenter;
            _panel.Add(_status);
        }

        // Opções desta máquina (GameOptions): um botão abre as três linhas, cada uma com - e +.
        void BuildOptions()
        {
            Button toggle = MenuButton("Opções", () =>
            {
                _options.style.display = _options.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
                RefreshOptions();
            });
            toggle.style.marginTop = 14;
            toggle.style.height = 36;
            toggle.style.fontSize = 16;
            _panel.Add(toggle);

            _options = new VisualElement();
            _options.style.display = DisplayStyle.None;
            _options.style.marginTop = 6;
            _panel.Add(_options);

            _volumeValue = OptionRow("Volume", () => GameOptions.SetVolume(GameOptions.Volume - 0.1f), () => GameOptions.SetVolume(GameOptions.Volume + 0.1f));
            _mouseValue = OptionRow("Mouse", () => GameOptions.SetMouseScale(GameOptions.MouseScale - 0.25f), () => GameOptions.SetMouseScale(GameOptions.MouseScale + 0.25f));
            _qualityValue = OptionRow("Qualidade", () => GameOptions.SetQuality(GameOptions.Quality - 1), () => GameOptions.SetQuality(GameOptions.Quality + 1));
        }

        Label OptionRow(string name, System.Action less, System.Action more)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;
            var label = Text(name, 15, Paper);
            label.style.width = 120;
            row.Add(label);
            Button minus = MenuButton("-", () => { less(); RefreshOptions(); });
            minus.style.width = 40; minus.style.height = 30;
            row.Add(minus);
            var value = Text("", 15, Yellow);
            value.style.width = 150;
            value.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(value);
            Button plus = MenuButton("+", () => { more(); RefreshOptions(); });
            plus.style.width = 40; plus.style.height = 30;
            row.Add(plus);
            _options.Add(row);
            return value;
        }

        void RefreshOptions()
        {
            if (_volumeValue == null) return;
            _volumeValue.text = Mathf.RoundToInt(GameOptions.Volume * 100f) + "%";
            _mouseValue.text = GameOptions.MouseScale.ToString("0.00") + "x";
            _qualityValue.text = GameOptions.QualityName;
        }

        // Uma linha por fase: a atual em destaque, as liberadas clicáveis, as outras apagadas.
        void BuildLevelList()
        {
            var label = Text("Fase", 14, Paper);
            label.style.opacity = 0.75f;
            label.style.marginBottom = 4;
            _panel.Add(label);

            _levelButtons = new Button[LevelCatalog.Count];
            for (int i = 0; i < LevelCatalog.Count; i++)
            {
                int index = i;
                Button button = MenuButton("", () => session.SwitchLevel(index));
                button.style.height = 36;
                button.style.fontSize = 16;
                button.style.marginBottom = 4;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.paddingLeft = 12;
                _levelButtons[i] = button;
                _panel.Add(button);
            }

            _devNote = Text("Modo desenvolvedor: todas as fases liberadas.", 12, Paper);
            _devNote.style.opacity = 0.5f;
            _devNote.style.marginBottom = 18;
            _panel.Add(_devNote);
        }

        void RefreshLevels()
        {
            int current = session.LevelIndex;
            LevelInfo here = LevelCatalog.Get(current);
            _subtitle.text = here != null ? here.Name + " — " + here.Subtitle : "";
            _devNote.style.display = LevelProgress.DevMode ? DisplayStyle.Flex : DisplayStyle.None;

            for (int i = 0; i < _levelButtons.Length; i++)
            {
                LevelInfo info = LevelCatalog.Levels[i];
                bool unlocked = LevelProgress.IsUnlocked(i);
                string state = i == current ? "aqui"
                    : LevelProgress.IsCleared(i) ? "zerado"
                    : unlocked ? "liberado"
                    : "zere o " + LevelCatalog.Get(i - 1).Name;

                Button button = _levelButtons[i];
                button.text = info.Name + " — " + info.Subtitle + "   [" + state + "]";
                button.SetEnabled(unlocked && i != current);
                button.style.backgroundColor = i == current ? CurrentColor : unlocked ? ButtonColor : LockedColor;
                // Desabilitado o Unity apaga o botão; a fase atual tem de continuar legível.
                button.style.opacity = i == current || unlocked ? 1f : 0.55f;
            }
        }

        void BuildHud(VisualElement root)
        {
            _hud = Text("", 14, Paper);
            _hud.pickingMode = PickingMode.Ignore;
            _hud.style.position = Position.Absolute;
            _hud.style.left = 14;
            _hud.style.top = 10;
            _hud.style.opacity = 0.65f;
            root.Add(_hud);

            _notice = Text("", 18, Yellow);
            _notice.pickingMode = PickingMode.Ignore;
            _notice.style.position = Position.Absolute;
            _notice.style.left = 0; _notice.style.right = 0;
            _notice.style.top = 60;
            _notice.style.unityTextAlign = TextAnchor.MiddleCenter;
            _notice.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(_notice);

            _leave = MenuButton("Sair da sessão", () => session.Leave());
            _leave.style.position = Position.Absolute;
            _leave.style.right = 14;
            _leave.style.top = 10;
            _leave.style.width = 190;
            _leave.style.height = 40;
            root.Add(_leave);
        }

        static Label Text(string text, int size, Color color)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            return label;
        }

        static Button MenuButton(string text, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 48;
            button.style.fontSize = 19;
            button.style.color = Paper;
            button.style.backgroundColor = ButtonColor;
            button.style.borderTopWidth = 0; button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0; button.style.borderRightWidth = 0;
            button.style.marginLeft = 0; button.style.marginRight = 0;
            return button;
        }

        void Update()
        {
            if (session == null || _panel == null) return;

            bool inMenu = session.Phase == SessionPhase.Menu;
            bool playing = session.Phase == SessionPhase.Playing;

            _panel.style.display = playing ? DisplayStyle.None : DisplayStyle.Flex;
            _panel.SetEnabled(inMenu);
            _status.text = session.Status;

            _hud.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            _leave.style.display = playing && UnityEngine.Cursor.lockState != CursorLockMode.Locked ? DisplayStyle.Flex : DisplayStyle.None;

            if (playing) _hud.text = DescribeSession();
            _notice.style.display = Time.unscaledTime < _noticeUntil ? DisplayStyle.Flex : DisplayStyle.None;
            if (inMenu) ReadHotkeys();
            if (!playing) RefreshLevels();
        }

        string DescribeSession()
        {
            if (!session.IsHosting) return "Conectado a " + session.Address + "   •   Esc solta o mouse";

            int players = session.PlayerCount;
            return "Hospedando   •   " + _localAddresses + "   •   porta " + session.port + "   •   " +
                   players + (players == 1 ? " jogador" : " jogadores") + "   •   Esc solta o mouse";
        }

        void ReadHotkeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Enquanto se digita o endereço, H e J são só letras.
            bool typing = _address.focusController != null &&
                          _address.focusController.focusedElement is VisualElement focused &&
                          (focused == _address || _address.Contains(focused));
            if (typing)
            {
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                    session.Join(_address.value);
                return;
            }

            if (keyboard.hKey.wasPressedThisFrame) session.Host();
            else if (keyboard.jKey.wasPressedThisFrame) session.Join(_address.value);
        }

        // Endereços IPv4 desta máquina na rede local, para passar a quem vai entrar.
        static string FindLocalAddresses()
        {
            try
            {
                string found = "";
                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) continue;
                    found += (found.Length > 0 ? " ou " : "") + address;
                }

                return found.Length > 0 ? "IP local " + found : "IP local não encontrado";
            }
            catch (SocketException)
            {
                return "IP local não encontrado";
            }
        }
    }
}
