using System;
using System.Collections;
using BackRooms.Items;
using BackRooms.World;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BackRooms.Net
{
    public enum SessionPhase
    {
        Menu,
        Connecting,
        Playing
    }

    /// <summary>
    /// Dona da sessão de rede: hospeda ou entra por IP, sincroniza a seed do mapa e manda
    /// construir o nível. O mapa em si não trafega — o host envia só a seed (e a fase) e cada
    /// máquina gera o mesmo labirinto.
    /// Cada fase é uma cena com a sua própria sessão. Trocar de fase destrói esta sessão e
    /// carrega a cena da outra; quem entra numa sessão de outra fase troca sozinho e reconecta.
    /// Linha de comando: -brHost hospeda ao abrir; -brJoin &lt;endereço&gt; entra ao abrir.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    [RequireComponent(typeof(UnityTransport))]
    public sealed class GameSession : MonoBehaviour
    {
        const string SeedMessage = "BackRooms.Seed";

        public static GameSession Instance { get; private set; }

        [Header("Cena")]
        public LevelBuilder level;
        [Tooltip("Câmera que mostra o mapa de fundo enquanto o menu está aberto.")]
        public Camera menuCamera;
        public AudioListener menuListener;

        [Header("Rede")]
        public ushort port = 7777;

        [Header("Mapa")]
        [Tooltip("0 = o host sorteia uma seed nova a cada sessão.")]
        public int fixedSeed;
        [Tooltip("Seed do mapa que aparece atrás do menu.")]
        public int menuSeed = 1998;
        [Tooltip("Giro da câmera do menu, em graus por segundo.")]
        public float menuSpin = 3f;

        public SessionPhase Phase { get; private set; } = SessionPhase.Menu;
        public string Status { get; private set; } = "";
        public string Address { get; private set; } = "";
        public int Seed { get; private set; }

        /// <summary>Índice desta fase no <see cref="LevelCatalog"/> (pela cena em que o nível está).</summary>
        public int LevelIndex { get; private set; }

        /// <summary>Verdadeiro quando o nível desta sessão já foi construído nesta máquina.</summary>
        public bool LevelReady { get; private set; }

        /// <summary>
        /// Segundos desde que o host abriu a sessão, pelo relógio do servidor: o mesmo valor em
        /// todas as máquinas. Serve para eventos que precisam acontecer juntos para todos sem
        /// trafegar na rede (o apagão das luzes). -1 fora de partida.
        /// </summary>
        public double SessionSeconds
        {
            get
            {
                if (_net == null || !_net.IsListening || Phase != SessionPhase.Playing) return -1.0;
                return System.Math.Max(0.0, _net.ServerTime.Time - _epoch);
            }
        }

        public bool IsHosting => _net != null && _net.IsListening && _net.IsServer;

        /// <summary>Número de jogadores conectados. Só o host sabe; nos clientes devolve -1.</summary>
        public int PlayerCount => IsHosting ? _net.ConnectedClientsIds.Count : -1;

        public event Action PhaseChanged;

        NetworkManager _net;
        UnityTransport _transport;
        Coroutine _building;
        double _epoch;
        bool _clearedThisSession;
        // Fase pedida pelo host a um cliente que está em outra; a troca acontece no Update.
        int _switchTo = -1;

        // Sobrevivem à troca de cena: o endereço para reconectar depois de trocar de fase e a
        // marca de que a linha de comando já foi lida (para não hospedar de novo a cada fase).
        static string _pendingJoin;
        static bool _argsHandled;

        void Awake()
        {
            Instance = this;
            _net = GetComponent<NetworkManager>();
            _transport = GetComponent<UnityTransport>();

            // O NetworkManager leva este objeto para fora da cena; o nível fica nela.
            string scene = level != null ? level.gameObject.scene.name : SceneManager.GetActiveScene().name;
            LevelIndex = Mathf.Max(0, LevelCatalog.IndexOfScene(scene));
        }

        void OnEnable()
        {
            LevelExit.Reached += OnExitReached;
        }

        void OnDisable()
        {
            LevelExit.Reached -= OnExitReached;
        }

        void Start()
        {
            ShowBackdrop();

            if (_pendingJoin != null)
            {
                string address = _pendingJoin;
                _pendingJoin = null;
                Join(address);
                return;
            }

            if (_argsHandled) return;
            _argsHandled = true;

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-brHost") { Host(); break; }
                if (args[i] == "-brJoin" && i + 1 < args.Length) { Join(args[i + 1]); break; }
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Unsubscribe();
        }

        void Update()
        {
            if (Phase != SessionPhase.Playing && menuCamera != null && menuCamera.enabled)
                menuCamera.transform.Rotate(0f, menuSpin * Time.deltaTime, 0f, Space.World);

            if (_switchTo >= 0)
            {
                // O host está em outra fase: sai, troca de cena e entra de novo no mesmo endereço.
                int target = _switchTo;
                _switchTo = -1;
                string address = Address;
                EndSession("");
                LoadLevel(target, address);
            }
        }

        // ---------------------------------------------------------------- Fases

        /// <summary>
        /// Troca para outra fase pelo menu. Só fora de partida e só para fase liberada
        /// (<see cref="LevelProgress"/>). Esta sessão é destruída junto com a cena atual.
        /// </summary>
        public bool SwitchLevel(int index)
        {
            if (Phase != SessionPhase.Menu || index == LevelIndex || !LevelProgress.IsUnlocked(index)) return false;
            LoadLevel(index, null);
            return true;
        }

        void LoadLevel(int index, string joinAddress)
        {
            LevelInfo info = LevelCatalog.Get(index);
            if (info == null) return;

            Debug.Log($"[BackRooms] Trocando para {info.Name} ({info.Scene})" + (joinAddress != null ? $", depois entra em {joinAddress}." : "."));
            _pendingJoin = joinAddress;

            // O objeto da sessão não pertence à cena (o NetworkManager o tirou dela): sem
            // destruí-lo à mão, a fase nova ficaria com duas sessões.
            Unsubscribe();
            if (_net != null && _net.IsListening) _net.Shutdown();
            Destroy(gameObject);
            SceneManager.LoadScene(info.Scene, LoadSceneMode.Single);
        }

        // Quem chega à saída na própria máquina zera a fase (uma vez por sessão).
        void OnExitReached(BackRooms.Player.PlayerController player)
        {
            if (Phase != SessionPhase.Playing || _clearedThisSession || player == null) return;
            if (!player.TryGetComponent(out PlayerNetwork network) || !network.IsOwner) return;

            _clearedThisSession = true;
            LevelProgress.MarkCleared(LevelIndex);
        }

        // ---------------------------------------------------------------- Entrar e sair

        /// <summary>
        /// Hospeda uma sessão (também é o modo de jogar sozinho).
        /// Com <paramref name="localOnly"/> só aceita conexões da própria máquina.
        /// </summary>
        public bool Host(bool localOnly = false)
        {
            if (Phase != SessionPhase.Menu) return false;

            _transport.SetConnectionData("127.0.0.1", port, localOnly ? "127.0.0.1" : "0.0.0.0");
            Subscribe();
            LevelReady = false;
            _clearedThisSession = false;
            ClearDrawings();
            SetPhase(SessionPhase.Connecting, "Abrindo a sessão...");

            if (!_net.StartHost())
            {
                Fail("Não foi possível hospedar. A porta " + port + " pode estar em uso.");
                return false;
            }

            Address = "esta máquina";
            _epoch = _net.ServerTime.Time;
            int seed = fixedSeed != 0 ? fixedSeed : new System.Random().Next(1, int.MaxValue);
            BuildLevel(seed);
            return true;
        }

        /// <summary>Entra na sessão hospedada em <paramref name="address"/> (IP ou nome da máquina).</summary>
        public bool Join(string address)
        {
            if (Phase != SessionPhase.Menu) return false;

            address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();

            _transport.SetConnectionData(address, port);
            Subscribe();
            LevelReady = false;
            _clearedThisSession = false;
            ClearDrawings();
            Address = address;
            SetPhase(SessionPhase.Connecting, "Conectando a " + address + ":" + port + "...");

            if (!_net.StartClient())
            {
                Fail("Endereço inválido: " + address);
                return false;
            }

            // O host manda a seed assim que a conexão é aceita.
            _net.CustomMessagingManager.RegisterNamedMessageHandler(SeedMessage, OnSeedMessage);
            // E, em seguida, os rabiscos que já existirem nas paredes.
            _net.CustomMessagingManager.RegisterNamedMessageHandler(DrawingBoard.StrokeMessage, OnStrokeMessage);
            return true;
        }

        public void Leave()
        {
            if (Phase == SessionPhase.Menu) return;
            EndSession("");
        }

        /// <summary>Chamado pelo jogador local quando já está posicionado e com a câmera ligada.</summary>
        public void NotifyLocalPlayerReady()
        {
            if (menuCamera != null) menuCamera.enabled = false;
            if (menuListener != null) menuListener.enabled = false;
            SetPhase(SessionPhase.Playing, "");
        }

        public Vector3 SpawnPoint(ulong clientId)
        {
            return level != null ? level.SpawnPosition((int)(clientId % 4)) : Vector3.zero;
        }

        // ---------------------------------------------------------------- Eventos do Netcode

        void Subscribe()
        {
            Unsubscribe();
            _net.OnClientConnectedCallback += OnClientConnected;
            _net.OnClientStopped += OnClientStopped;
        }

        void Unsubscribe()
        {
            if (_net == null) return;
            _net.OnClientConnectedCallback -= OnClientConnected;
            _net.OnClientStopped -= OnClientStopped;
        }

        void OnClientConnected(ulong clientId)
        {
            if (!_net.IsServer || clientId == _net.LocalClientId) return;

            // A seed do mapa, o instante em que a sessão começou, no relógio do servidor, e a fase.
            using (var writer = new FastBufferWriter(sizeof(int) * 2 + sizeof(double), Allocator.Temp))
            {
                writer.WriteValueSafe(Seed);
                writer.WriteValueSafe(_epoch);
                writer.WriteValueSafe(LevelIndex);
                _net.CustomMessagingManager.SendNamedMessage(SeedMessage, clientId, writer);
            }

            if (DrawingBoard.Instance != null) DrawingBoard.Instance.SendAll(_net, clientId);
        }

        void OnStrokeMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (DrawingBoard.Instance != null) DrawingBoard.Instance.Receive(senderClientId, reader);
        }

        // Os rabiscos pertencem à sessão: somem quando ela começa e quando termina.
        static void ClearDrawings()
        {
            if (DrawingBoard.Instance != null) DrawingBoard.Instance.Clear();
        }

        void OnSeedMessage(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int seed);
            reader.ReadValueSafe(out _epoch);
            reader.ReadValueSafe(out int levelIndex);

            if (levelIndex != LevelIndex && LevelCatalog.IsValid(levelIndex))
            {
                // A troca destrói esta sessão: não dá para fazer de dentro do tratamento da mensagem.
                Status = "O host está no " + LevelCatalog.Get(levelIndex).Name + ". Trocando de fase...";
                _switchTo = levelIndex;
                return;
            }

            BuildLevel(seed);
        }

        // Dispara quando a conexão cai, é recusada ou a sessão é encerrada (o host também é cliente).
        void OnClientStopped(bool wasHost)
        {
            if (Phase == SessionPhase.Menu) return;

            string reason = _net != null ? _net.DisconnectReason : "";
            if (string.IsNullOrEmpty(reason))
                reason = Phase == SessionPhase.Connecting ? "Não foi possível conectar a " + Address + "." : "A sessão foi encerrada.";

            EndSession(reason);
        }

        // ---------------------------------------------------------------- Estado

        void Fail(string message)
        {
            EndSession(message);
        }

        void EndSession(string message)
        {
            Unsubscribe();
            if (_net != null && _net.IsListening) _net.Shutdown();

            if (_building != null) StopCoroutine(_building);
            _building = null;
            LevelReady = false;
            ClearDrawings();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            SetPhase(SessionPhase.Menu, message);
            ShowBackdrop();
        }

        void SetPhase(SessionPhase phase, string status)
        {
            Phase = phase;
            Status = status;
            PhaseChanged?.Invoke();
        }

        // Mapa de fundo do menu: o mesmo gerador, com uma seed fixa e uma câmera girando.
        void ShowBackdrop()
        {
            if (menuCamera != null)
            {
                menuCamera.enabled = true;
                if (menuListener != null) menuListener.enabled = true;
            }

            if (level == null) return;
            if (_building != null) StopCoroutine(_building);
            _building = StartCoroutine(BuildRoutine(menuSeed, false));
        }

        void BuildLevel(int seed)
        {
            Seed = seed;
            if (level == null) { LevelReady = true; return; }

            if (_building != null) StopCoroutine(_building);
            _building = StartCoroutine(BuildRoutine(seed, true));
        }

        IEnumerator BuildRoutine(int seed, bool forSession)
        {
            // Apaga o mapa antigo e espera um quadro antes de criar o novo: assim as luzes
            // antigas já liberaram seu espaço no atlas de sombras quando as novas pedem o delas.
            level.Clear();
            yield return null;

            level.seed = seed;
            level.Rebuild();

            if (menuCamera != null)
            {
                Vector3 eye = level.ScenicPosition();
                menuCamera.transform.position = new Vector3(eye.x, 1.65f, eye.z);
            }

            _building = null;
            if (forSession) LevelReady = true;
        }
    }
}
