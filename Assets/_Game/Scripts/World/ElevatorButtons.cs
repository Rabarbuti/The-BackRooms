using System.Collections.Generic;
using BackRooms.Net;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BackRooms.World
{
    public enum ElevatorButtonKind : byte
    {
        /// <summary>Dentro da cabine: abre a porta.</summary>
        Open,
        /// <summary>Dentro da cabine: fecha a porta.</summary>
        Close,
        /// <summary>No saguão, ao lado da porta: chama o elevador (abre).</summary>
        Call
    }

    /// <summary>
    /// Um botão de elevador do Escritório (malha e colisor pequenos, para a mira). Afunda e acende
    /// ao ser apertado; o de chamada fica aceso enquanto o chamado vale. Quem decide o que o botão
    /// faz é o <see cref="ElevatorControl"/>.
    /// </summary>
    public sealed class ElevatorButton : MonoBehaviour
    {
        public ElevatorDoors Door { get; private set; }
        public ElevatorButtonKind Kind { get; private set; }

        MeshRenderer _renderer;
        Material _idle, _lit;
        Vector3 _rest;
        float _pressedAt = -10f;
        bool _shownLit;

        public void Init(ElevatorDoors door, ElevatorButtonKind kind, MeshRenderer meshRenderer, Material idle, Material lit)
        {
            Door = door;
            Kind = kind;
            _renderer = meshRenderer;
            _idle = idle;
            _lit = lit;
            _rest = transform.localPosition;
            if (_renderer != null) _renderer.sharedMaterial = _idle;
        }

        /// <summary>Afunda e acende por um instante (quem apertou, na máquina dele).</summary>
        public void Flash() => _pressedAt = Time.time;

        void Update()
        {
            bool pushed = Time.time - _pressedAt < 0.3f;
            transform.localPosition = pushed ? _rest - transform.localRotation * Vector3.forward * 0.004f : _rest;

            bool lit = pushed || (Kind == ElevatorButtonKind.Call && ElevatorControl.IsCalled(Door));
            if (lit == _shownLit || _renderer == null) return;
            _shownLit = lit;
            _renderer.sharedMaterial = lit ? _lit : _idle;
        }
    }

    /// <summary>
    /// Botões dos elevadores do Escritório: dentro de cada cabine, abrir e fechar; no saguão, o de
    /// chamada. Mira + E (raio da câmera descartando o próprio jogador, como o PenTool.TryAim), aviso
    /// na filmadora (<see cref="Prompt"/>) e clique.
    ///
    /// Rede: o host decide. O cliente manda o botão apertado (BackRooms.ElevatorPress); o host muda o
    /// estado da porta e manda o estado de todas a todos (BackRooms.ElevatorState); quem entra depois
    /// pede o estado ao montar a fase (BackRooms.ElevatorHello). O estado guarda horas do relógio da
    /// sessão, então cada máquina anda a porta sozinha a partir dele.
    ///
    /// Regras: a cabine do início abre sozinha (<see cref="OfficeElevators.CabOpensAt"/>) e depois
    /// obedece aos botões (fechada fica fechada até alguém abrir ou chamar). Os poços: chamar faz o
    /// elevador "chegar" (luz da cabine na hora, porta abre <see cref="CallDelay"/> depois e fica
    /// <see cref="HoldSeconds"/> aberta); abrir por dentro abre na hora pelo mesmo tempo; fechar por
    /// dentro fecha e segura o relógio por <see cref="ClosedSeconds"/>. Nunca prende nem esmaga:
    /// enquanto a porta não está fechada de todo, alguém no vão a faz reabrir. A trava
    /// <see cref="ElevatorDoors.HeldOpen"/> (o inimigo que chega de elevador) vence o botão de fechar.
    /// </summary>
    public sealed class ElevatorControl : MonoBehaviour
    {
        // Textos que o jogador lê (a língua do jogo é o inglês).
        public const string OpenText = "E - OPEN DOOR";
        public const string CloseText = "E - CLOSE DOOR";
        public const string CallText = "E - CALL ELEVATOR";
        public const string HeldText = "THE DOOR WON'T CLOSE";

        /// <summary>Poço chamado: segundos até a porta abrir (o elevador "chegando").</summary>
        public const float CallDelay = 1.5f;
        /// <summary>Poço aberto pelo botão: segundos aberto.</summary>
        public const float HoldSeconds = 8f;
        /// <summary>Poço fechado pelo botão: segundos em que o relógio não o reabre.</summary>
        public const float ClosedSeconds = 10f;

        const string PressMessage = "BackRooms.ElevatorPress";
        const string StateMessage = "BackRooms.ElevatorState";
        const string HelloMessage = "BackRooms.ElevatorHello";

        /// <summary>Meia largura do vão da porta e meia profundidade da faixa que reabre a porta.</summary>
        const float DoorwayHalfWidth = 0.65f;
        const float DoorwayHalfDepth = 0.45f;

        public float reach = 2.2f;

        public static ElevatorControl Instance { get; private set; }

        /// <summary>O que a tecla E faz agora, para o painel. Nulo se não mira nenhum botão.</summary>
        public static string Prompt { get; private set; }

        /// <summary>Para os testes: aperta E no botão mirado.</summary>
        public static bool PressForTest;

        enum Mode : byte { Auto, Open, Closed }

        struct DoorState
        {
            public Mode Mode;
            public double From, Until;
        }

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        readonly List<ElevatorDoors> _doors = new List<ElevatorDoors>();
        readonly List<Vector3> _toLobby = new List<Vector3>();
        readonly List<ElevatorButton> _buttons = new List<ElevatorButton>();
        DoorState[] _states = new DoorState[0];
        InputAction _use;
        float _sentAt = -10f;
        NetworkManager _net;
        static AudioClip _click;

        /// <summary>Botões apertados que o host aceitou (para os testes).</summary>
        public int HostPresses { get; private set; }

        /// <summary>Estados recebidos do host (cliente; para os testes).</summary>
        public int StatesReceived { get; private set; }

        public IReadOnlyList<ElevatorButton> Buttons => _buttons;

        public void Init(List<ElevatorDoors> doors, List<Vector3> toLobby, List<ElevatorButton> buttons)
        {
            _doors.Clear();
            _doors.AddRange(doors);
            _toLobby.Clear();
            _toLobby.AddRange(toLobby);
            _buttons.Clear();
            _buttons.AddRange(buttons);
            _states = new DoorState[_doors.Count];
        }

        void Awake()
        {
            _use = new InputAction("BotaoDoElevador", InputActionType.Button, "<Keyboard>/e");
            _use.AddBinding("<Gamepad>/buttonWest");
        }

        void OnEnable()
        {
            Instance = this;
            _use?.Enable();
        }

        void OnDisable()
        {
            _use?.Disable();
            if (Instance == this) { Instance = null; Prompt = null; }
        }

        void OnDestroy()
        {
            _use?.Dispose();
            Unregister();
        }

        // ---------------------------------------------------------------- Regra da porta

        /// <summary>
        /// A porta deve estar aberta? <paramref name="auto"/> é o que o relógio pede (a cabine do
        /// início aberta, a visita do poço). Em <paramref name="called"/>, se um botão a chamou ou
        /// abriu (acende a luz da cabine e o botão de chamada).
        /// </summary>
        public static bool Resolve(ElevatorDoors door, bool auto, double t, out bool called)
        {
            called = false;
            bool wanted = auto;
            ElevatorControl control = Instance;
            int i = control != null ? control._doors.IndexOf(door) : -1;
            if (i >= 0)
            {
                DoorState s = control._states[i];
                if (s.Mode == Mode.Open && t < s.Until)
                {
                    called = true;
                    wanted = t >= s.From;
                }
                else if (s.Mode == Mode.Closed && t < s.Until) wanted = false;

                // Ninguém fica preso nem esmagado: com a porta ainda aberta, alguém no vão a reabre.
                if (!wanted && door.Openness > 0.02f && control.Doorway(i)) wanted = true;
            }
            if (door.HeldOpen) wanted = true;
            return wanted;
        }

        /// <summary>O botão de chamada desta porta está aceso?</summary>
        public static bool IsCalled(ElevatorDoors door)
        {
            ElevatorControl control = Instance;
            int i = control != null ? control._doors.IndexOf(door) : -1;
            if (i < 0) return false;
            DoorState s = control._states[i];
            return s.Mode == Mode.Open && OfficeElevators.Clock() < s.Until && !door.IsCab;
        }

        /// <summary>Algum jogador (ou o corpo dele) está no vão da porta <paramref name="i"/>?</summary>
        public bool Doorway(int i)
        {
            ElevatorDoors door = _doors[i];
            if (door == null) return false;
            Vector3 toLobby = _toLobby[i];
            Vector3 lateral = Vector3.Cross(Vector3.up, toLobby);
            Vector3 center = door.transform.position;
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned) continue;
                Vector3 d = player.transform.position - center;
                if (d.y < -1f || d.y > 2.2f) continue;
                if (Mathf.Abs(Vector3.Dot(d, lateral)) < DoorwayHalfWidth && Mathf.Abs(Vector3.Dot(d, toLobby)) < DoorwayHalfDepth) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- Mira e tecla

        void Update()
        {
            if (!Application.isPlaying) return;
            EnsureNetwork();

            Prompt = null;
            PlayerNetwork local = PlayerNetwork.Local;
            bool pressed = (_use != null && _use.WasPressedThisFrame()) || PressForTest;
            PressForTest = false;
            if (local == null || !local.InGame || local.controller == null || local.controller.IsReturning || local.controller.InFurniture) return;

            ElevatorButton aimed = Aimed(local);
            if (aimed == null) return;
            Prompt = PromptFor(aimed);
            if (!pressed || Time.time - _sentAt < 0.3f) return;
            _sentAt = Time.time;

            aimed.Flash();
            PlayClick(aimed.transform.position);
            int index = _buttons.IndexOf(aimed);
            GameSession session = GameSession.Instance;
            if (session != null && !session.IsHosting && _net != null && _net.IsConnectedClient)
            {
                using (var writer = new FastBufferWriter(sizeof(int), Allocator.Temp))
                {
                    writer.WriteValueSafe(index);
                    _net.CustomMessagingManager.SendNamedMessage(PressMessage, NetworkManager.ServerClientId, writer);
                }
            }
            else HostPress(index, local.transform.position);
        }

        static string PromptFor(ElevatorButton button)
        {
            switch (button.Kind)
            {
                case ElevatorButtonKind.Open: return OpenText;
                case ElevatorButtonKind.Close: return button.Door != null && button.Door.HeldOpen ? HeldText : CloseText;
                default: return CallText;
            }
        }

        ElevatorButton Aimed(PlayerNetwork local)
        {
            Camera camera = local.playerCamera;
            if (camera == null) return null;
            Transform eye = camera.transform;
            int count = Physics.RaycastNonAlloc(eye.position, eye.forward, Hits, reach, ~0, QueryTriggerInteraction.Ignore);
            Transform self = local.transform.root;
            float best = float.MaxValue;
            Collider nearest = null;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.transform.root == self || Hits[i].distance >= best) continue;
                best = Hits[i].distance;
                nearest = Hits[i].collider;
            }
            if (nearest == null) return null;
            var button = nearest.GetComponent<ElevatorButton>();
            return button != null && _buttons.Contains(button) ? button : null;
        }

        // ---------------------------------------------------------------- Host

        /// <summary>Host: alguém em <paramref name="at"/> apertou o botão <paramref name="index"/>.</summary>
        public void HostPress(int index, Vector3 at)
        {
            if (index < 0 || index >= _buttons.Count) return;
            ElevatorButton button = _buttons[index];
            int i = _doors.IndexOf(button.Door);
            if (i < 0) return;
            if ((at - button.transform.position).magnitude > reach + 2.5f) return;

            double t = OfficeElevators.Clock();
            ElevatorDoors door = _doors[i];
            DoorState s = _states[i];
            switch (button.Kind)
            {
                case ElevatorButtonKind.Close:
                    // A cabine em que o inimigo chegou não fecha enquanto ele está fora dela.
                    if (door.HeldOpen) return;
                    s = new DoorState { Mode = Mode.Closed, From = t, Until = door.IsCab ? double.PositiveInfinity : t + ClosedSeconds };
                    break;

                case ElevatorButtonKind.Open:
                case ElevatorButtonKind.Call:
                    if (door.IsCab) s = new DoorState { Mode = Mode.Open, From = t, Until = double.PositiveInfinity };
                    else
                    {
                        // Já aberta (ou chegando): só estende. Fechada, por fora, o elevador ainda vem.
                        bool arriving = s.Mode == Mode.Open && t < s.Until;
                        double from = button.Kind == ElevatorButtonKind.Call && !door.WantsOpen && !arriving ? t + CallDelay : t;
                        if (arriving) from = System.Math.Min(from, s.From);
                        s = new DoorState { Mode = Mode.Open, From = from, Until = System.Math.Max(from, t) + HoldSeconds };
                    }
                    break;
            }

            _states[i] = s;
            HostPresses++;
            Broadcast();
        }

        // ---------------------------------------------------------------- Rede

        void EnsureNetwork()
        {
            GameSession session = GameSession.Instance;
            NetworkManager net = session != null ? session.GetComponent<NetworkManager>() : null;
            if (net == _net && (_net == null || _net.IsListening)) return;
            if (_net != null && !_net.IsListening) Unregister();
            if (net == null || !net.IsListening || net.CustomMessagingManager == null) return;

            _net = net;
            if (net.IsServer)
            {
                net.CustomMessagingManager.RegisterNamedMessageHandler(PressMessage, OnPressMessage);
                net.CustomMessagingManager.RegisterNamedMessageHandler(HelloMessage, OnHelloMessage);
            }
            else
            {
                net.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, OnStateMessage);
                // Quem entra depois (ou monta a fase depois de conectar) pede o estado ao host.
                using (var writer = new FastBufferWriter(sizeof(int), Allocator.Temp))
                {
                    writer.WriteValueSafe(0);
                    net.CustomMessagingManager.SendNamedMessage(HelloMessage, NetworkManager.ServerClientId, writer);
                }
            }
        }

        void Unregister()
        {
            if (_net == null) return;
            try
            {
                CustomMessagingManager messages = _net.CustomMessagingManager;
                // Se outra fase já montou o seu controle, os nomes agora são dela.
                if (messages != null && (Instance == null || Instance == this))
                {
                    messages.UnregisterNamedMessageHandler(PressMessage);
                    messages.UnregisterNamedMessageHandler(HelloMessage);
                    messages.UnregisterNamedMessageHandler(StateMessage);
                }
            }
            catch (System.Exception)
            {
                // Sessão já encerrada: não há o que desfazer.
            }
            _net = null;
        }

        void OnPressMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int index);
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.OwnerClientId == sender)
                    HostPress(index, player.transform.position);
        }

        void OnHelloMessage(ulong sender, FastBufferReader reader) => SendState(sender);

        void Broadcast()
        {
            if (_net == null || !_net.IsServer) return;
            foreach (ulong clientId in _net.ConnectedClientsIds)
                if (clientId != _net.LocalClientId) SendState(clientId);
        }

        void SendState(ulong clientId)
        {
            if (_net == null || !_net.IsServer) return;
            using (var writer = new FastBufferWriter(sizeof(int) + _states.Length * (1 + sizeof(double) * 2), Allocator.Temp))
            {
                writer.WriteValueSafe(_states.Length);
                foreach (DoorState s in _states)
                {
                    writer.WriteValueSafe((byte)s.Mode);
                    writer.WriteValueSafe(s.From);
                    writer.WriteValueSafe(s.Until);
                }
                _net.CustomMessagingManager.SendNamedMessage(StateMessage, clientId, writer);
            }
        }

        void OnStateMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int count);
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out byte mode);
                reader.ReadValueSafe(out double from);
                reader.ReadValueSafe(out double until);
                if (i < _states.Length) _states[i] = new DoorState { Mode = (Mode)mode, From = from, Until = until };
            }
            StatesReceived++;
        }

        // ---------------------------------------------------------------- Som

        void PlayClick(Vector3 at)
        {
            if (_click == null) _click = CreateClick();
            AudioSource.PlayClipAtPoint(_click, at, 0.6f);
        }

        /// <summary>Clique de botão de elevador: estalo curto de plástico com um bipe agudo.</summary>
        public static AudioClip CreateClick()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.16f);
            var data = new float[n];
            var rng = new System.Random(311);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float click = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t / 0.003f);
                float beep = t > 0.02f && t < 0.13f ? Mathf.Sin(2f * Mathf.PI * 1760f * t) * 0.25f * Mathf.Min(1f, (0.13f - t) / 0.01f) : 0f;
                data[i] = click * 0.8f + beep;
            }
            var clip = AudioClip.Create("Elevador_Botao", n, 1, rate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }
    }
}
