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
        const string PassMessage = "BackRooms.Pass";
        // Level 1: o cliente manda ao host o código digitado no teclado da porta; o host avisa
        // todos (e quem entra depois) de que a porta destrancou.
        const string ExitCodeMessage = "BackRooms.ExitCode";
        const string ExitOpenMessage = "BackRooms.ExitOpen";
        // Objetivo comum (SwitchObjective): o cliente avisa o host de que terminou de armar um
        // ponto; o host manda o estado inteiro a todos (e a quem entra depois).
        const string SwitchArmMessage = "BackRooms.SwitchArm";
        const string SwitchStateMessage = "BackRooms.SwitchState";
        // Pilhas da lanterna (BatteryPickups): o cliente pede; o host diz quais sumiram e quem pegou.
        const string PickupMessage = "BackRooms.Pickup";
        const string PickupsMessage = "BackRooms.Pickups";

        /// <summary>Do abrir da porta até a troca de cena, em segundos (a tela fica preta).</summary>
        public const float PassSeconds = 1.6f;

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

        /// <summary>
        /// Verdadeiro desde que a porta de saída abriu até a fase seguinte carregar (ou a sessão
        /// acabar, na última fase).
        /// </summary>
        public bool Passing { get; private set; }

        /// <summary>Fase para onde a porta levou: o índice no catálogo, ou -1 se era a última.</summary>
        public int PassingTo { get; private set; } = -1;

        /// <summary>Número de jogadores conectados. Só o host sabe; nos clientes devolve -1.</summary>
        public int PlayerCount => IsHosting ? _net.ConnectedClientsIds.Count : -1;

        public event Action PhaseChanged;

        NetworkManager _net;
        UnityTransport _transport;
        Coroutine _building;
        double _epoch;
        bool _clearedThisSession;
        bool _localOnly;
        Coroutine _passRoutine;
        // Quando o jogador desta máquina entrou na fase: o tempo dela vai para a tela de fim.
        float _playStart = -1f;
        // Fase pedida pelo host a um cliente que está em outra; a troca acontece no Update.
        int _switchTo = -1;

        // Sobrevivem à troca de cena: o endereço para reconectar depois de trocar de fase e a
        // marca de que a linha de comando já foi lida (para não hospedar de novo a cada fase).
        static string _pendingJoin;
        static bool _argsHandled;
        // Quem hospedava e passou de fase hospeda a seguinte sozinho, assim que a cena carrega.
        static bool _pendingHost;
        static bool _pendingHostLocalOnly;

        void Awake()
        {
            Instance = this;
            _net = GetComponent<NetworkManager>();
            _transport = GetComponent<UnityTransport>();

            // O NetworkManager leva este objeto para fora da cena; o nível fica nela.
            string scene = level != null ? level.gameObject.scene.name : SceneManager.GetActiveScene().name;
            LevelIndex = Mathf.Max(0, LevelCatalog.IndexOfScene(scene));
        }

        void Start()
        {
            ShowBackdrop();

            if (_pendingHost)
            {
                _pendingHost = false;
                Host(_pendingHostLocalOnly);
                return;
            }

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
                return;
            }

            // Porta de saída: o host abre quando todos os jogadores do mapa estão esperando nela.
            if (IsHosting && Phase == SessionPhase.Playing && !Passing)
            {
                PlayerNetwork.CountAtExit(out int waiting, out int total);
                if (total > 0 && waiting == total) OpenExit();
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

        // ---------------------------------------------------------------- Porta de saída

        // Host: avisa os clientes de que a porta abriu e para qual fase ela leva, e passa também.
        void OpenExit()
        {
            int next = LevelCatalog.IsValid(LevelIndex + 1) ? LevelIndex + 1 : -1;

            using (var writer = new FastBufferWriter(sizeof(int), Allocator.Temp))
            {
                writer.WriteValueSafe(next);
                foreach (ulong clientId in _net.ConnectedClientsIds)
                    if (clientId != _net.LocalClientId)
                        _net.CustomMessagingManager.SendNamedMessage(PassMessage, clientId, writer);
            }

            BeginPass(next);
        }

        void OnPassMessage(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int next);
            if (Phase == SessionPhase.Playing && !Passing) BeginPass(next);
        }

        // Em toda máquina: a fase está zerada para quem passou pela porta. A tela escurece e,
        // em seguida, cada um carrega a fase seguinte — o host hospeda, os clientes reconectam.
        void BeginPass(int next)
        {
            Passing = true;
            PassingTo = next;

            if (!_clearedThisSession)
            {
                if (_playStart >= 0f) LevelProgress.RecordTime(LevelIndex, Time.realtimeSinceStartup - _playStart);
                _clearedThisSession = true;
                LevelProgress.MarkCleared(LevelIndex);
            }

            Debug.Log("[BackRooms] Porta de saída aberta: " + (next >= 0 ? "indo para " + LevelCatalog.Get(next).Name : "era a última fase") + ".");
            _passRoutine = StartCoroutine(PassRoutine(next));
        }

        IEnumerator PassRoutine(int next)
        {
            bool hosting = IsHosting;
            string address = Address;
            string cleared = LevelCatalog.Get(LevelIndex).Name;

            yield return new WaitForSecondsRealtime(PassSeconds);
            _passRoutine = null;

            if (next < 0)
            {
                EndSession("Fim da fita. " + cleared + " zerado.");
                LevelProgress.EndTape();
                yield break;
            }

            _pendingHost = hosting;
            _pendingHostLocalOnly = _localOnly;
            // O host fecha a sessão antes de o cliente tentar entrar: os dois esperam o mesmo
            // tempo, e o cliente só começou a contar quando o aviso chegou.
            LoadLevel(next, hosting ? null : address);
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
            _localOnly = localOnly;
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
            ExitLock.PendingUnlock = false;
            SwitchObjective.Pending = default;
            BatteryPickups.PendingTaken = 0;
            _net.CustomMessagingManager.RegisterNamedMessageHandler(PickupMessage, OnPickupMessage);
            _net.CustomMessagingManager.RegisterNamedMessageHandler(ExitCodeMessage, OnExitCodeMessage);
            _net.CustomMessagingManager.RegisterNamedMessageHandler(SwitchArmMessage, OnSwitchArmMessage);
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
            // E, quando todos chegarem à porta de saída, para qual fase ela leva.
            _net.CustomMessagingManager.RegisterNamedMessageHandler(PassMessage, OnPassMessage);
            // E, no Level 1, que a porta destrancou.
            _net.CustomMessagingManager.RegisterNamedMessageHandler(ExitOpenMessage, OnExitOpenMessage);
            // E o estado do objetivo comum (disjuntores, válvulas, alarmes).
            _net.CustomMessagingManager.RegisterNamedMessageHandler(SwitchStateMessage, OnSwitchStateMessage);
            // E as pilhas que alguém já pegou.
            _net.CustomMessagingManager.RegisterNamedMessageHandler(PickupsMessage, OnPickupsMessage);
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
            _playStart = Time.realtimeSinceStartup;
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

            // Porta do Level 1 já destrancada: quem entra depois também a encontra aberta.
            if (ExitLock.Instance != null && ExitLock.Instance.Unlocked) SendExitOpen(clientId);

            // Objetivo comum: quem entra depois acha os pontos como estão (e a porta, se cumprido).
            if (SwitchObjective.Instance != null && SwitchObjective.Instance.Count > 0) SendSwitchState(clientId, SwitchObjective.Instance.Snapshot());

            // Pilhas que já sumiram (ninguém recarrega por isso).
            if (BatteryPickups.Instance != null && BatteryPickups.Instance.Taken != 0) SendPickups(clientId, BatteryPickups.Instance.Taken, ulong.MaxValue);
        }

        // ---------------------------------------------------------------- Pilhas da lanterna

        /// <summary>Cliente: quer pegar a pilha <paramref name="index"/>; o host confere.</summary>
        public void SendPickup(int index)
        {
            if (_net == null || !_net.IsConnectedClient) return;
            using (var writer = new FastBufferWriter(sizeof(int), Allocator.Temp))
            {
                writer.WriteValueSafe(index);
                _net.CustomMessagingManager.SendNamedMessage(PickupMessage, NetworkManager.ServerClientId, writer);
            }
        }

        /// <summary>Host: estas pilhas sumiram; a última foi pega por <paramref name="who"/>.</summary>
        public void BroadcastPickups(ulong taken, ulong who)
        {
            if (!IsHosting) return;
            foreach (ulong clientId in _net.ConnectedClientsIds)
                if (clientId != _net.LocalClientId) SendPickups(clientId, taken, who);
        }

        void SendPickups(ulong clientId, ulong taken, ulong who)
        {
            using (var writer = new FastBufferWriter(sizeof(ulong) * 2, Allocator.Temp))
            {
                writer.WriteValueSafe(taken);
                writer.WriteValueSafe(who);
                _net.CustomMessagingManager.SendNamedMessage(PickupsMessage, clientId, writer);
            }
        }

        void OnPickupMessage(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int index);
            if (BatteryPickups.Instance == null) return;
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.OwnerClientId == senderClientId)
                    BatteryPickups.Instance.HostPick(index, senderClientId, player.transform.position);
        }

        void OnPickupsMessage(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong taken);
            reader.ReadValueSafe(out ulong who);
            BatteryPickups.PendingTaken = taken;
            if (BatteryPickups.Instance != null) BatteryPickups.Instance.Apply(taken, who);
        }

        // ---------------------------------------------------------------- Objetivo comum

        /// <summary>Cliente: terminou de armar o ponto <paramref name="index"/> do objetivo; o host confere.</summary>
        public void SendSwitchArm(int index)
        {
            if (_net == null || !_net.IsConnectedClient) return;
            using (var writer = new FastBufferWriter(sizeof(int), Allocator.Temp))
            {
                writer.WriteValueSafe(index);
                _net.CustomMessagingManager.SendNamedMessage(SwitchArmMessage, NetworkManager.ServerClientId, writer);
            }
        }

        /// <summary>Host: manda a todos os clientes o estado do objetivo comum.</summary>
        public void BroadcastSwitchState()
        {
            if (!IsHosting || SwitchObjective.Instance == null) return;
            SwitchObjective.State state = SwitchObjective.Instance.Snapshot();
            foreach (ulong clientId in _net.ConnectedClientsIds)
                if (clientId != _net.LocalClientId) SendSwitchState(clientId, state);
        }

        void SendSwitchState(ulong clientId, SwitchObjective.State state)
        {
            using (var writer = new FastBufferWriter(SwitchObjective.Size(state), Allocator.Temp))
            {
                SwitchObjective.Write(writer, state);
                _net.CustomMessagingManager.SendNamedMessage(SwitchStateMessage, clientId, writer);
            }
        }

        void OnSwitchArmMessage(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int index);
            if (SwitchObjective.Instance == null) return;
            Vector3? at = null;
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.OwnerClientId == senderClientId) at = player.transform.position;
            if (at.HasValue) SwitchObjective.Instance.HostArm(index, at);
        }

        void OnSwitchStateMessage(ulong senderClientId, FastBufferReader reader)
        {
            SwitchObjective.State state = SwitchObjective.Read(reader);
            // Guarda também para a montagem que ainda vai acontecer (a seed pode ter acabado de chegar).
            SwitchObjective.Pending = state;
            if (SwitchObjective.Instance != null) SwitchObjective.Instance.Apply(state);
        }

        // ---------------------------------------------------------------- Teclado da porta (Level 1)

        /// <summary>Cliente: manda ao host o código digitado no teclado da porta.</summary>
        public void SendExitCode(int code)
        {
            if (_net == null || !_net.IsConnectedClient) return;
            using (var writer = new FastBufferWriter(sizeof(int), Allocator.Temp))
            {
                writer.WriteValueSafe(code);
                _net.CustomMessagingManager.SendNamedMessage(ExitCodeMessage, NetworkManager.ServerClientId, writer);
            }
        }

        /// <summary>Host: a porta destrancou. Avisa todos os clientes.</summary>
        public void BroadcastExitUnlocked()
        {
            if (!IsHosting) return;
            foreach (ulong clientId in _net.ConnectedClientsIds)
                if (clientId != _net.LocalClientId) SendExitOpen(clientId);
        }

        void SendExitOpen(ulong clientId)
        {
            using (var writer = new FastBufferWriter(sizeof(int), Allocator.Temp))
            {
                writer.WriteValueSafe(1);
                _net.CustomMessagingManager.SendNamedMessage(ExitOpenMessage, clientId, writer);
            }
        }

        void OnExitCodeMessage(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int code);
            if (ExitLock.Instance != null) ExitLock.Instance.Submit(code);
        }

        void OnExitOpenMessage(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int _);
            ExitLock.PendingUnlock = true;
            if (ExitLock.Instance != null) ExitLock.Instance.SetUnlocked();
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
            // Partida nova: a porta do Level 1 volta a nascer trancada (o host avisa se não estiver),
            // e os pontos do objetivo comum, desarmados.
            ExitLock.PendingUnlock = false;
            SwitchObjective.Pending = default;
            BatteryPickups.PendingTaken = 0;

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

            // Passando de fase, o host fecha a sessão de propósito: o cliente segue para a
            // fase seguinte por conta própria e reconecta lá.
            if (Passing) return;

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
            if (_passRoutine != null) StopCoroutine(_passRoutine);
            _passRoutine = null;
            Passing = false;
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
