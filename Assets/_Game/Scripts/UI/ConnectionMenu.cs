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
    /// Durante o jogo vira uma linha de estado no canto; Esc abre o menu de pausa
    /// (<see cref="PauseMenu"/>, criado aqui num documento próprio), que tem o botão de sair.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ConnectionMenu : MonoBehaviour
    {
        public GameSession session;

        static Color Yellow => MenuKit.Yellow;
        static Color Paper => MenuKit.Paper;
        static Color PanelColor => MenuKit.PanelColor;
        static Color ButtonColor => MenuKit.ButtonColor;
        static Color CurrentColor => MenuKit.CurrentColor;
        static Color LockedColor => MenuKit.LockedColor;

        // ---------------------------------------------------------------- Textos (a língua do jogo é o inglês)
        const string PauseHint = "Esc: tasks and options";
        const string EndingTitle = "END OF TAPE";
        const string EndingLine = "You got out. For now.";
        const string BackText = "Back to menu";
        const string TotalText = "Total";
        const string ClearedNotice = " CLEARED";
        const string UnlockedNotice = " unlocked in the menu";
        // Por enquanto o jogo é só para um jogador (pedido de 21/11/2026): o menu não mostra o
        // endereço nem "Join", e a tela não fala de hospedar. A rede continua inteira por baixo
        // (os testes em dois editores entram direto pela GameSession); ligar isto traz tudo de volta.
        const bool MultiplayerMenu = false;
        const string PlayText = "Play        [H]";
        const string HostText = "Host  (or play alone)        [H]";
        const string AddressLabel = "Address of whoever is hosting";
        const string JoinText = "Join        [J]";
        const string LevelLabel = "Level";
        const string DevNote = "Developer mode: all levels unlocked.";
        const string StateCleared = "cleared";
        const string ConnectedTo = "Connected to ";
        const string Hosting = "Hosting";
        const string PortText = "port ";
        const string OnePlayer = " player";
        const string ManyPlayers = " players";
        const string LocalIp = "Local IP ";
        const string Or = " or ";
        const string NoLocalIp = "Local IP not found";

        VisualElement _panel;
        TextField _address;
        Label _status;
        Label _hud;
        PauseMenu _pause;
        Label _subtitle;
        Label _devNote;
        Label _notice;
        Button[] _levelButtons;
        MenuKit.OptionsPanel _options;
        RecordingsScreen _recordings;
        VisualElement _ending;
        Label _endingTimes;

        /// <summary>A tela de fim da fita está aberta (testes).</summary>
        public static bool EndingShown { get; private set; }
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

            BuildEnding(root);
            _recordings = new RecordingsScreen(root);
            EnsurePauseMenu();

            _localAddresses = FindLocalAddresses();
            LevelProgress.LevelCleared += OnLevelCleared;
            LevelProgress.TapeEnded += ShowEnding;
        }

        void OnDisable()
        {
            LevelProgress.LevelCleared -= OnLevelCleared;
            LevelProgress.TapeEnded -= ShowEnding;
        }

        // Fim da fita: passou pela porta da última fase. Tela preta com o tempo de cada fase.
        void BuildEnding(VisualElement root)
        {
            _ending = new VisualElement();
            _ending.style.position = Position.Absolute;
            _ending.style.left = 0; _ending.style.right = 0; _ending.style.top = 0; _ending.style.bottom = 0;
            _ending.style.backgroundColor = new Color(0f, 0f, 0f, 0.96f);
            _ending.style.alignItems = Align.Center;
            _ending.style.justifyContent = Justify.Center;
            _ending.style.display = DisplayStyle.None;
            root.Add(_ending);

            var title = Text(EndingTitle, 44, Yellow);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            _ending.Add(title);
            var line = Text(EndingLine, 18, Paper);
            line.style.opacity = 0.75f;
            line.style.marginBottom = 26;
            _ending.Add(line);
            _endingTimes = Text("", 20, Paper);
            _endingTimes.style.unityTextAlign = TextAnchor.MiddleCenter;
            _endingTimes.style.marginBottom = 30;
            _ending.Add(_endingTimes);
            Button back = MenuButton(BackText, () => { _ending.style.display = DisplayStyle.None; EndingShown = false; });
            back.style.width = 260;
            _ending.Add(back);
        }

        void ShowEnding()
        {
            if (_ending == null) return;
            string text = "";
            float total = 0f;
            for (int i = 0; i < LevelCatalog.Count; i++)
            {
                float t = LevelProgress.LastTime(i);
                total += t;
                text += LevelCatalog.Get(i).Name + "   " + LevelProgress.FormatTime(t) + "\n";
            }
            _endingTimes.text = text + "\n" + TotalText + "   " + LevelProgress.FormatTime(total);
            _ending.style.display = DisplayStyle.Flex;
            EndingShown = true;
        }

        void OnLevelCleared(int index)
        {
            LevelInfo next = LevelCatalog.Get(index + 1);
            _notice.text = LevelCatalog.Get(index).Name.ToUpperInvariant() + ClearedNotice +
                           (next != null ? "   •   " + next.Name + UnlockedNotice : "");
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

            _panel.Add(MenuButton(MultiplayerMenu ? HostText : PlayText, () => session.Host()));

            var joinLabel = Text(AddressLabel, 14, Paper);
            joinLabel.style.marginTop = 22;
            joinLabel.style.opacity = 0.75f;
            _panel.Add(joinLabel);

            _address = new TextField { value = "127.0.0.1" };
            _address.style.fontSize = 20;
            _address.style.height = 40;
            _address.style.marginTop = 4;
            _address.style.marginBottom = 8;
            _panel.Add(_address);

            Button join = MenuButton(JoinText, () => session.Join(_address.value));
            _panel.Add(join);
            if (!MultiplayerMenu)
            {
                joinLabel.style.display = DisplayStyle.None;
                _address.style.display = DisplayStyle.None;
                join.style.display = DisplayStyle.None;
            }

            // As gravações da tela (F9): a galeria abre por cima do menu.
            Button recordings = MenuButton(RecordingsScreen.ButtonText, () => _recordings.Show());
            recordings.style.marginTop = 14;
            recordings.style.height = 36;
            recordings.style.fontSize = 16;
            _panel.Add(recordings);

            BuildOptions();

            _status = Text("", 15, Paper);
            _status.style.marginTop = 18;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.unityTextAlign = TextAnchor.MiddleCenter;
            _panel.Add(_status);
        }

        // Opções desta máquina (GameOptions): as mesmas do menu de pausa (MenuKit).
        void BuildOptions() => _options = MenuKit.AddOptions(_panel);

        void OnDestroy()
        {
            if (_pause != null) Destroy(_pause.gameObject);
        }

        /// <summary>A galeria das gravações do menu inicial (testes).</summary>
        public RecordingsScreen Recordings => _recordings;

        // O menu de pausa vive num documento próprio (este se limpa inteiro ao ligar), com o mesmo
        // PanelSettings e desenhado por cima. Criado aqui para valer em toda cena que tem este menu.
        void EnsurePauseMenu()
        {
            if (_pause != null) return;
            UIDocument mine = GetComponent<UIDocument>();
            var holder = new GameObject("MenuPausa");
            holder.SetActive(false);
            // Na raiz da cena, não como filho: um UIDocument filho de outro entra na árvore do pai,
            // e este documento se limpa inteiro ao ligar.
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, gameObject.scene);
            UIDocument document = holder.AddComponent<UIDocument>();
            document.panelSettings = mine.panelSettings;
            document.sortingOrder = mine.sortingOrder + 10;
            _pause = holder.AddComponent<PauseMenu>();
            _pause.session = session;
            holder.SetActive(true);
        }

        // Uma linha por fase: a atual em destaque, as liberadas clicáveis, as outras apagadas.
        void BuildLevelList()
        {
            var label = Text(LevelLabel, 14, Paper);
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

            _devNote = Text(DevNote, 12, Paper);
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
                // Só a fase zerada ganha etiqueta (pedido de 21/11/2026): a atual se vê pela cor do
                // botão e a bloqueada, por estar apagada.
                Button button = _levelButtons[i];
                button.text = info.Name + " — " + info.Subtitle +
                    (LevelProgress.IsCleared(i) ? "   [" + StateCleared + "]" : "");
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
        }

        static Label Text(string text, int size, Color color) => MenuKit.Text(text, size, color);

        static Button MenuButton(string text, System.Action onClick) => MenuKit.MenuButton(text, onClick);

        void Update()
        {
            if (session == null || _panel == null) return;

            bool inMenu = session.Phase == SessionPhase.Menu;
            if (EndingShown && !inMenu)
            {
                _ending.style.display = DisplayStyle.None;
                EndingShown = false;
            }
            bool playing = session.Phase == SessionPhase.Playing;
            if (!inMenu && _recordings != null && _recordings.IsOpen) _recordings.Hide();
            // Fora do jogo só a música do menu se ouve: a fase montada atrás do menu fica muda.
            GameOptions.SetMenuSilence(!playing);

            // Com o fim da fita na tela, o painel do menu some (aparecia apagado por trás, com o texto
            // por cima de END OF TAPE) e volta com "Back to menu".
            _panel.style.display = playing || EndingShown ? DisplayStyle.None : DisplayStyle.Flex;
            _panel.SetEnabled(inMenu);
            _status.text = session.Status;

            _hud.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;

            if (playing) _hud.text = DescribeSession();
            _notice.style.display = Time.unscaledTime < _noticeUntil && !EndingShown ? DisplayStyle.Flex : DisplayStyle.None;
            if (inMenu && !EndingShown) ReadHotkeys();
            if (!playing) RefreshLevels();
        }

        string DescribeSession()
        {
            if (!MultiplayerMenu && session.IsHosting && session.PlayerCount <= 1) return PauseHint;
            if (!session.IsHosting) return ConnectedTo + session.Address + "   •   " + PauseHint;

            int players = session.PlayerCount;
            return Hosting + "   •   " + _localAddresses + "   •   " + PortText + session.port + "   •   " +
                   players + (players == 1 ? OnePlayer : ManyPlayers) + "   •   " + PauseHint;
        }

        void ReadHotkeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Com a galeria das gravações aberta, H e J não valem; Esc fecha a galeria.
            if (_recordings != null && _recordings.IsOpen)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) _recordings.Hide();
                return;
            }

            // Enquanto se digita o endereço, H e J são só letras.
            if (!MultiplayerMenu)
            {
                if (keyboard.hKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame ||
                    keyboard.numpadEnterKey.wasPressedThisFrame) session.Host();
                return;
            }

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
                    found += (found.Length > 0 ? Or : "") + address;
                }

                return found.Length > 0 ? LocalIp + found : NoLocalIp;
            }
            catch (SocketException)
            {
                return NoLocalIp;
            }
        }
    }
}
