using System.Collections;
using System.Collections.Generic;
using BackRooms.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace BackRooms.Net
{
    /// <summary>
    /// Liga o prefab do jogador à rede. Na máquina do dono, habilita controle, câmera e
    /// lanterna. Nas outras, o prefab vira só a representação daquele jogador: corpo visível,
    /// posição pelo NetworkTransform e, por aqui, inclinação da cabeça, agachar e lanterna.
    /// No prefab, controle, câmera e ouvinte de áudio ficam desligados; quem liga é este script.
    /// </summary>
    public sealed class PlayerNetwork : NetworkBehaviour
    {
        [Header("Referências")]
        public PlayerController controller;
        public Flashlight flashlight;
        public Footsteps footsteps;
        public Camera playerCamera;
        public AudioListener listener;
        public Transform cameraPivot;
        [Tooltip("Corpo visto pelos outros jogadores. O dono não vê o próprio.")]
        public GameObject body;
        [Tooltip("A figura animada dentro do corpo: recebe agachar, olhar, item na mão e lanterna.")]
        public PlayerBody figure;
        [Tooltip("Caneta do jogador. O painel da filmadora lê daqui se ela está na mão.")]
        public BackRooms.Items.PenTool pen;

        const float SendInterval = 1f / 15f;

        readonly NetworkVariable<float> _pitch = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<bool> _flashlightOn = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<bool> _crouching = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Esconderijo (HideKind): nicho, guarda-roupa ou debaixo da mesa.
        readonly NetworkVariable<byte> _hidden = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<bool> _holding = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<bool> _atExit = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Índice da tinta da caneta na mão (ordem de PenTool.ColorNames), para o corpo que os
        // outros veem segurar a caneta da mesma cor. Quem entra depois recebe o valor atual.
        // Segurando E num ponto do objetivo comum: faz barulho (o inimigo ouve).
        readonly NetworkVariable<bool> _working = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<byte> _penColor = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Cadeira em que está sentado (ClassroomSeats, Escritório), ou -1. Quem entra depois recebe o valor atual.
        readonly NetworkVariable<short> _seat = new NetworkVariable<short>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        float _nextSend;
        float _shownPitch;
        float _shownEye;
        bool _shownSeated;
        bool _ready;

        /// <summary>O jogador desta máquina, quando já está no mapa. Nulo no menu.</summary>
        public static PlayerNetwork Local { get; private set; }

        /// <summary>Todos os jogadores da sessão que existem nesta máquina. A entidade lê daqui.</summary>
        public static readonly List<PlayerNetwork> All = new List<PlayerNetwork>();

        /// <summary>Verdadeiro quando este jogador já está posicionado no mapa.</summary>
        public bool InGame => _ready;

        /// <summary>Agachado? Vale em qualquer máquina: a do dono lê o controle, as outras a rede.</summary>
        public bool Crouching => IsOwner ? controller.IsCrouching : _crouching.Value;

        /// <summary>Dentro de um esconderijo? Vale em qualquer máquina, como <see cref="Crouching"/>.</summary>
        public bool Hidden => Hiding != HideKind.None;

        /// <summary>Que esconderijo. Vale em qualquer máquina, como <see cref="Crouching"/>.</summary>
        public HideKind Hiding => IsOwner ? controller.Hiding : (HideKind)_hidden.Value;

        /// <summary>Com a mão na porta de saída, esperando os outros? Vale em qualquer máquina.</summary>
        public bool AtExit => _atExit.Value;

        /// <summary>
        /// Segurando E num disjuntor, válvula ou alarme (<see cref="BackRooms.World.SwitchObjective"/>):
        /// faz barulho de corrida. Vale em qualquer máquina.
        /// </summary>
        public bool Working => IsOwner ? BackRooms.World.SwitchObjective.LocalWorking : _working.Value;

        /// <summary>Cor da caneta publicada na rede (índice da tinta). Vale em qualquer máquina.</summary>
        public int PenColor => _penColor.Value;

        /// <summary>
        /// Em que cadeira está sentado (índice em <see cref="BackRooms.World.ClassroomSeats"/>), ou -1.
        /// Vale em qualquer máquina, como <see cref="Crouching"/>.
        /// </summary>
        public int Seat => IsOwner ? controller.Seat : _seat.Value;

        /// <summary>Sentado numa cadeira? Vale em qualquer máquina.</summary>
        public bool Seated => Seat >= 0;

        /// <summary>
        /// Do dono para o host: quer sentar na cadeira <paramref name="seat"/>. O host decide (uma
        /// pessoa por cadeira) e responde ao dono por <see cref="SeatAnswerRpc"/>.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void RequestSeatRpc(short seat)
        {
            BackRooms.World.ClassroomSeats seats = BackRooms.World.ClassroomSeats.Instance;
            bool granted = seats != null && seats.ServerClaim(OwnerClientId, seat);
            SeatAnswerRpc(seat, granted);
        }

        /// <summary>Do host para o dono: pode sentar (ou a cadeira já tem dono).</summary>
        [Rpc(SendTo.Owner)]
        void SeatAnswerRpc(short seat, bool granted)
        {
            BackRooms.World.ClassroomSeats seats = BackRooms.World.ClassroomSeats.Instance;
            if (seats != null) seats.Answer(this, seat, granted);
        }

        /// <summary>
        /// Quantos jogadores da sessão estão esperando na porta de saída e quantos estão no
        /// mapa. Vale em qualquer máquina.
        /// </summary>
        public static void CountAtExit(out int waiting, out int total)
        {
            waiting = 0;
            total = 0;

            foreach (PlayerNetwork player in All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                total++;
                if (player.AtExit) waiting++;
            }
        }

        /// <summary>
        /// Do host para o dono do jogador: a entidade pegou você. A queda de sinal e a volta
        /// à sala inicial acontecem na máquina do dono, que é quem manda na própria posição.
        /// </summary>
        [Rpc(SendTo.Owner)]
        public void GetCaughtRpc()
        {
            controller.GetCaught();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            _shownEye = controller.standingEyeHeight;

            if (IsOwner) StartCoroutine(BeginLocal());
            else BeginRemote();
        }

        public override void OnNetworkDespawn()
        {
            _ready = false;
            All.Remove(this);
            if (Local == this) Local = null;

            controller.Respawned -= OnRespawned;
            controller.RespawnPoint = null;
        }

        IEnumerator BeginLocal()
        {
            if (body != null) body.SetActive(false);

            // O mapa é gerado nesta máquina a partir da seed; espera ele existir para ter chão.
            GameSession session = GameSession.Instance;
            while (session != null && !session.LevelReady) yield return null;

            Vector3 spawn = session != null ? session.SpawnPoint(OwnerClientId) : transform.position;
            controller.Teleport(spawn, 0f);

            // Quem cai num buraco volta para o mesmo ponto em que começou.
            controller.RespawnPoint = RespawnPoint;
            controller.Respawned += OnRespawned;

            playerCamera.enabled = true;
            listener.enabled = true;
            flashlight.inputEnabled = true;
            controller.enabled = true;
            _ready = true;
            Local = this;

            if (session != null) session.NotifyLocalPlayerReady();
        }

        Vector3 RespawnPoint()
        {
            GameSession session = GameSession.Instance;
            return session != null ? session.SpawnPoint(OwnerClientId) : transform.position;
        }

        // Avisa a rede de que a mudança de lugar foi um salto. Sem isso os outros jogadores
        // veriam este deslizar do fundo do buraco até a sala inicial, atravessando paredes.
        void OnRespawned() => SnapNetwork();

        /// <summary>
        /// Dono: a mudança de lugar de agora foi um salto (sentar, levantar, voltar ao início); os
        /// outros não o veem deslizar até lá.
        /// </summary>
        public void SnapNetwork()
        {
            if (IsSpawned && TryGetComponent(out NetworkTransform networkTransform))
                networkTransform.Teleport(transform.position, transform.rotation, transform.localScale);
        }

        void BeginRemote()
        {
            controller.enabled = false;
            playerCamera.enabled = false;
            listener.enabled = false;
            flashlight.inputEnabled = false;

            // Quem move este objeto é o NetworkTransform; o CharacterController atrapalharia.
            if (TryGetComponent(out CharacterController characterController)) characterController.enabled = false;

            if (footsteps != null) footsteps.SetRemote();
            if (body != null) body.SetActive(true);
            _ready = true;
        }

        void Update()
        {
            if (!IsSpawned || !_ready) return;

            if (IsOwner) SendState();
            else ShowState();
        }

        // Dono: publica o que os outros precisam ver. A posição vai pelo NetworkTransform.
        void SendState()
        {
            if (_flashlightOn.Value != flashlight.IsOn) _flashlightOn.Value = flashlight.IsOn;
            if (_crouching.Value != controller.IsCrouching) _crouching.Value = controller.IsCrouching;
            byte hiding = (byte)controller.Hiding;
            if (_hidden.Value != hiding) _hidden.Value = hiding;

            bool holding = pen != null && pen.Equipped;
            if (_holding.Value != holding) _holding.Value = holding;

            byte penColor = pen != null ? (byte)pen.ColorIndex : (byte)0;
            if (_penColor.Value != penColor) _penColor.Value = penColor;

            bool atExit = BackRooms.World.LevelExit.Instance != null && BackRooms.World.LevelExit.Instance.LocalWaiting;
            if (_atExit.Value != atExit) _atExit.Value = atExit;

            bool working = BackRooms.World.SwitchObjective.LocalWorking;
            if (_working.Value != working) _working.Value = working;

            short seat = (short)controller.Seat;
            if (_seat.Value != seat) _seat.Value = seat;

            if (Time.unscaledTime < _nextSend) return;
            _nextSend = Time.unscaledTime + SendInterval;

            // Arredonda para graus inteiros: evita mandar atualização por tremidas do mouse.
            float pitch = Mathf.Round(controller.Pitch);
            if (Mathf.Abs(pitch - _pitch.Value) >= 1f) _pitch.Value = pitch;
        }

        // Demais máquinas: aplica o estado recebido, suavizando.
        void ShowState()
        {
            float dt = Time.deltaTime;
            bool crouching = _crouching.Value;

            // Sentado numa cadeira (Escritório): olho na altura de quem senta, corpo virado para a
            // frente da cadeira (a cabeça é que gira) e a figura na pose sentada.
            BackRooms.World.ClassroomSeats seats = BackRooms.World.ClassroomSeats.Instance;
            int seat = _seat.Value;
            bool seated = seat >= 0 && seats != null && seat < seats.Count;

            _shownPitch = Mathf.Lerp(_shownPitch, _pitch.Value, 1f - Mathf.Exp(-12f * dt));
            float eye = seated ? BackRooms.World.ClassroomSeats.SeatEye : crouching ? controller.crouchingEyeHeight : controller.standingEyeHeight;
            // Ao sentar e ao levantar a pessoa salta de lugar: o olho vai junto, sem deslizar.
            if (seated != _shownSeated) _shownEye = eye;
            _shownSeated = seated;
            _shownEye = Mathf.MoveTowards(_shownEye, eye, 4f * dt);

            cameraPivot.localPosition = new Vector3(0f, _shownEye, 0f);
            cameraPivot.localRotation = Quaternion.Euler(_shownPitch, 0f, 0f);

            flashlight.SetOn(_flashlightOn.Value);

            // Dentro de um guarda-roupa ou debaixo de uma mesa, o corpo não aparece (ficaria
            // atravessando o móvel).
            if (body != null)
            {
                HideKind hiding = (HideKind)_hidden.Value;
                bool inside = hiding == HideKind.Wardrobe || hiding == HideKind.Table;
                if (body.activeSelf == inside) body.SetActive(!inside);
            }

            if (figure != null)
            {
                // A figura se agacha de verdade (dobra as pernas), olha para onde o jogador
                // olha e mostra o que ele tem nas mãos.
                float crouch = seated ? 0f : Mathf.InverseLerp(controller.standingEyeHeight, controller.crouchingEyeHeight, _shownEye);
                figure.SetState(crouch, _shownPitch, _holding.Value, _flashlightOn.Value);

                if (seated)
                {
                    float front = seats.YawWorld(seat);
                    figure.transform.rotation = Quaternion.Euler(0f, front, 0f);
                    figure.SetSeated(true, Mathf.DeltaAngle(front, transform.eulerAngles.y));
                }
                else
                {
                    if (figure.transform.localRotation != Quaternion.identity) figure.transform.localRotation = Quaternion.identity;
                    figure.SetSeated(false, 0f);
                }

                // A caneta na mão do traje leva a tinta escolhida.
                BackRooms.Items.DrawingBoard board = BackRooms.Items.DrawingBoard.Instance;
                if (board != null && board.inkMaterials != null && board.inkMaterials.Length > 0)
                    figure.SetPenInk(board.inkMaterials[Mathf.Min(_penColor.Value, board.inkMaterials.Length - 1)]);
            }
            else if (body != null)
            {
                float height = _shownEye / controller.standingEyeHeight;
                body.transform.localScale = new Vector3(1f, height, 1f);
            }
        }
    }
}
