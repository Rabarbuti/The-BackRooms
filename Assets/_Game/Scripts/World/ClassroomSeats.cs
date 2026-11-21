using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BackRooms.World
{
    /// <summary>
    /// As carteiras das salas de aula do Escritório (Level 3): as cadeiras das fileiras, de frente
    /// para a lousa, em que o jogador senta como quem assiste a uma aula, e as lousas (para o traço
    /// de marcador do <c>DrawingBoard</c>).
    ///
    /// Mira na cadeira + E senta; E de novo, andar ou pular levanta, ao lado da cadeira, num ponto
    /// livre. Sentado: câmera na altura de quem senta, de frente para onde a cadeira aponta, girando
    /// até <see cref="TurnLimit"/> para cada lado; não anda; lanterna e caneta funcionam. Não é
    /// esconderijo: os inimigos veem, ouvem e pegam quem está sentado.
    ///
    /// Uma pessoa por cadeira, e quem decide é o host: o dono pede (<c>PlayerNetwork.RequestSeatRpc</c>),
    /// o host confere e responde (<c>SeatAnswerRpc</c>); só então o dono senta. O lugar de cada
    /// jogador vai pela rede (<c>PlayerNetwork.Seat</c>), e quem entra depois o recebe com o resto
    /// do estado. Ser pego, levado, cair, trocar de fase ou sair do jogo solta a cadeira (o
    /// <c>PlayerController</c> zera o assento em qualquer salto).
    ///
    /// O <c>LevelBuilder</c> (salas de aula, <c>LevelBuilder.OfficeProps.cs</c>) cria este componente
    /// com as cadeiras e as lousas, em coordenadas da fase, sem sortear nada: não muda o hash da planta
    /// nem o resto da mobília.
    /// </summary>
    public sealed class ClassroomSeats : MonoBehaviour, ITaskSource, IAimSource
    {
        // Textos que o jogador lê (a língua do jogo é o inglês).
        public const string SitText = "E - SIT";
        public const string StandText = "E - STAND UP";
        public const string OccupiedText = "SEAT TAKEN";
        const string KeyName = "Aim + E";
        const string KeyAction = "desk chair: sit and watch the board (E, move or jump to stand up)";

        /// <summary>Altura do olho de quem está sentado, em metros.</summary>
        public const float SeatEye = 1.18f;
        /// <summary>Quanto a cabeça gira, para cada lado, a partir da frente da cadeira (graus).</summary>
        public const float TurnLimit = 100f;
        /// <summary>Margem da lousa para o traço de marcador (fora do plano dela).</summary>
        const float BoardMargin = 0.03f;

        /// <summary>Uma cadeira de sentar: pés no meio do assento, virada para a lousa.</summary>
        public struct Seat
        {
            /// <summary>Onde ficam os pés de quem senta, em coordenadas da fase.</summary>
            public Vector3 Feet;
            /// <summary>Para onde a cadeira aponta, em graus (no referencial da fase).</summary>
            public float Yaw;
            /// <summary>O colisor da cadeira: é ele que a mira acha.</summary>
            public Collider Collider;
        }

        public static ClassroomSeats Instance { get; private set; }

        /// <summary>O que a tecla E faz agora, para a filmadora. Nulo se não mira cadeira nem está sentado.</summary>
        public static string Prompt { get; private set; }

        /// <summary>Para os testes: aperta E (senta na cadeira mirada ou levanta).</summary>
        public static bool PressForTest;

        /// <summary>
        /// Para os testes, só no host: a cadeira dada conta como ocupada por um segundo jogador que não
        /// existe (-1: nenhuma). Serve para conferir a recusa sem dois editores.
        /// </summary>
        public static int OccupiedForTest = -1;

        [Tooltip("Até onde a mira alcança a cadeira, em metros.")]
        public float reach = 2f;

        readonly List<Seat> _seats = new List<Seat>();
        readonly List<Bounds> _boards = new List<Bounds>();
        readonly Dictionary<Collider, int> _byCollider = new Dictionary<Collider, int>();
        // Host: cadeiras prometidas há pouco (o dono ainda não publicou que sentou).
        readonly Dictionary<int, (ulong client, float time)> _claims = new Dictionary<int, (ulong, float)>();

        static readonly RaycastHit[] Hits = new RaycastHit[16];
        static readonly Collider[] Overlaps = new Collider[16];
        static readonly IComparer<RaycastHit> HitDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        InputAction _use;
        int _asked = -1;
        float _askedAt, _refusedUntil;

        public IReadOnlyList<Seat> Seats => _seats;
        public int Count => _seats.Count;

        /// <summary>Quantas recusas o host deu (para os testes).</summary>
        public int Refusals { get; private set; }

        /// <summary>Quantas recusas chegaram a esta máquina (para os testes).</summary>
        public int RefusalsReceived { get; private set; }

        /// <summary>A cadeira mirada agora pelo jogador desta máquina (-1 nenhuma).</summary>
        public int Aimed { get; private set; } = -1;

        public void Init(List<Seat> seats, List<Bounds> boards)
        {
            _seats.Clear();
            _seats.AddRange(seats);
            _boards.Clear();
            _boards.AddRange(boards);
            _byCollider.Clear();
            for (int i = 0; i < _seats.Count; i++)
                if (_seats[i].Collider != null) _byCollider[_seats[i].Collider] = i;

            // Traços que chegaram antes da fase (quem entra depois): os que caem numa lousa engrossam.
            if (Application.isPlaying && BackRooms.Items.DrawingBoard.Instance != null) BackRooms.Items.DrawingBoard.Instance.RefreshMarkers();
        }

        void Awake()
        {
            _use = new InputAction("Sentar", InputActionType.Button, "<Keyboard>/e");
            _use.AddBinding("<Gamepad>/buttonWest");
        }

        void OnEnable()
        {
            Instance = this;
            _use?.Enable();
            LevelTasks.Register(this);
            if (Application.isPlaying) InteractAim.Register(this);
        }

        void OnDisable()
        {
            _use?.Disable();
            LevelTasks.Unregister(this);
            InteractAim.Unregister(this);
            if (Instance != this) return;
            Instance = null;
            Prompt = null;

            // A fase saiu (troca de fase, volta ao menu): ninguém fica sentado numa cadeira que não existe.
            PlayerNetwork local = PlayerNetwork.Local;
            if (local != null && local.controller != null && local.controller.IsSeated) local.controller.ClearSeat();
        }

        void OnDestroy() => _use?.Dispose();

        // ---------------------------------------------------------------- Onde fica cada coisa

        /// <summary>Os pés de quem senta na cadeira, no mundo.</summary>
        public Vector3 FeetWorld(int seat) => transform.TransformPoint(_seats[seat].Feet);

        /// <summary>Para onde a cadeira aponta, no mundo (graus).</summary>
        public float YawWorld(int seat) => _seats[seat].Yaw + transform.eulerAngles.y;

        /// <summary>O ponto está na face de uma lousa (com uma margem fora do plano dela)?</summary>
        public bool OnWhiteboard(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            foreach (Bounds board in _boards)
            {
                Bounds b = board;
                b.Expand(BoardMargin * 2f);
                if (b.Contains(local)) return true;
            }
            return false;
        }

        /// <summary>A lousa mais perto de um ponto, no mundo (centro e frente). Falso sem lousa.</summary>
        public bool NearestBoard(Vector3 world, out Bounds bounds)
        {
            bounds = default;
            float best = float.PositiveInfinity;
            Vector3 local = transform.InverseTransformPoint(world);
            foreach (Bounds board in _boards)
            {
                float d = (board.center - local).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                bounds = new Bounds(transform.TransformPoint(board.center), board.size);
            }
            return best < float.PositiveInfinity;
        }

        public IReadOnlyList<Bounds> Boards => _boards;

        /// <summary>Atalho para o <c>DrawingBoard</c>: o ponto está numa lousa da fase aberta?</summary>
        public static bool IsWhiteboard(Vector3 world) => Instance != null && Instance.OnWhiteboard(world);

        // ---------------------------------------------------------------- Quem está sentado onde

        /// <summary>Algum jogador (fora <paramref name="except"/>) está sentado nessa cadeira? Vale em qualquer máquina.</summary>
        public static bool Taken(int seat, ulong except)
        {
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame || player.OwnerClientId == except) continue;
                if (player.Seat == seat) return true;
            }
            return false;
        }

        /// <summary>
        /// Host: <paramref name="client"/> pede a cadeira. Concede se ninguém mais está nela nem a
        /// pediu há pouco; a promessa vale 2 s, até o dono publicar que sentou.
        /// </summary>
        public bool ServerClaim(ulong client, int seat)
        {
            if (seat < 0 || seat >= _seats.Count) return false;

            bool taken = Taken(seat, client) || seat == OccupiedForTest;
            if (!taken && _claims.TryGetValue(seat, out var claim) && claim.client != client && Time.unscaledTime - claim.time < 2f) taken = true;

            if (taken)
            {
                Refusals++;
                return false;
            }
            _claims[seat] = (client, Time.unscaledTime);
            return true;
        }

        /// <summary>Dono: a resposta do host ao pedido de sentar.</summary>
        public void Answer(PlayerNetwork player, int seat, bool granted)
        {
            if (player != PlayerNetwork.Local || seat != _asked) return;
            _asked = -1;

            if (!granted)
            {
                RefusalsReceived++;
                _refusedUntil = Time.unscaledTime + 1.5f;
                Prompt = OccupiedText;
                return;
            }

            PlayerController body = player.controller;
            if (body == null || body.IsReturning || body.InFurniture || body.IsSeated || seat < 0 || seat >= _seats.Count) return;
            body.SitDown(seat, FeetWorld(seat), YawWorld(seat), SeatEye, TurnLimit);
            player.SnapNetwork();
        }

        // ---------------------------------------------------------------- A tecla E

        void Update()
        {
            PlayerNetwork local = PlayerNetwork.Local;
            PlayerController body = local != null ? local.controller : null;
            Prompt = null;
            Aimed = -1;
            if (body == null || !local.InGame || body.IsReturning || body.InFurniture) { PressForTest = false; return; }

            bool pressed = (_use.WasPressedThisFrame() && !BackRooms.UI.InputGate.Blocked) || PressForTest;
            PressForTest = false;

            if (body.IsSeated)
            {
                Prompt = StandText;
                if (pressed || body.WantsToStand) StandUp(local);
                return;
            }

            int seat = InteractAim.Winner(this);
            Aimed = seat;
            if (seat < 0) return;

            if (Time.unscaledTime < _refusedUntil || Taken(seat, local.OwnerClientId))
            {
                Prompt = OccupiedText;
                return;
            }

            Prompt = SitText;
            if (!pressed) return;
            // Um pedido de cada vez (a resposta do host leva um instante).
            if (_asked >= 0 && Time.unscaledTime - _askedAt < 2f) return;
            _asked = seat;
            _askedAt = Time.unscaledTime;
            local.RequestSeatRpc((short)seat);
        }

        // A cadeira mais perto do centro da mira (mira comum: InteractAim): o raio acertando a cadeira
        // ou o meio dela dentro do cone, com visada livre. Sentado, a tecla é de levantar (sem mira).
        public bool AimCandidate(in InteractAim.View view, out int index, out float score)
        {
            index = -1;
            score = float.PositiveInfinity;
            PlayerController body = view.Local != null ? view.Local.controller : null;
            if (body == null || body.IsReturning || body.InFurniture || body.IsSeated) return false;
            for (int i = 0; i < _seats.Count; i++)
            {
                Collider c = _seats[i].Collider;
                if (c == null) continue;
                float sc = InteractAim.Score(view, c.transform, c.bounds.center, reach);
                if (sc >= score) continue;
                score = sc;
                index = i;
            }
            return index >= 0;
        }

        /// <summary>A cadeira que o jogador mira de perto (-1 nenhuma). Ignora o próprio jogador (padrão do PenTool.TryAim).</summary>
        public int AimedSeat(PlayerNetwork local)
        {
            Camera camera = local.playerCamera;
            if (camera == null) return -1;

            Transform eye = camera.transform;
            int count = Physics.RaycastNonAlloc(eye.position, eye.forward, Hits, reach, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(Hits, 0, count, HitDistance);
            Transform self = local.transform.root;

            for (int i = 0; i < count; i++)
            {
                Collider hit = Hits[i].collider;
                if (hit.transform.root == self) continue;
                return _byCollider.TryGetValue(hit, out int seat) ? seat : -1;
            }
            return -1;
        }

        /// <summary>Levanta o jogador desta máquina, ao lado da cadeira, num ponto livre. Falso se não achou lugar.</summary>
        public bool StandUp(PlayerNetwork local)
        {
            PlayerController body = local.controller;
            if (body == null || !body.IsSeated) return false;

            int seat = body.Seat;
            Vector3 feet = seat >= 0 && seat < _seats.Count ? FeetWorld(seat) : local.transform.position;
            float yaw = seat >= 0 && seat < _seats.Count ? YawWorld(seat) : local.transform.eulerAngles.y;
            CharacterController capsule = body.GetComponent<CharacterController>();
            float radius = capsule != null ? capsule.radius : 0.35f;

            if (!FindExit(feet, yaw, radius, body.standingHeight, local.transform.root, out Vector3 exit)) return false;
            body.StandUp(exit);
            local.SnapNetwork();
            return true;
        }

        /// <summary>
        /// Um ponto livre perto da cadeira para ficar de pé: cabe a cápsula do jogador (sem móvel nem
        /// parede), há chão embaixo e nada entre a cadeira e ele na altura do peito (não passa para o
        /// outro lado de uma parede). Primeiro atrás e dos lados da cadeira, depois em anéis maiores.
        /// </summary>
        public static bool FindExit(Vector3 feet, float yaw, float radius, float height, Transform self, out Vector3 exit)
        {
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            Vector3 back = turn * Vector3.back, right = turn * Vector3.right;

            // Atrás e dos lados, de perto para longe.
            foreach (float d in new[] { 0.7f, 0.85f, 1.0f, 1.2f })
            {
                if (TryExit(feet, feet + back * d, radius, height, self, out exit)) return true;
                if (TryExit(feet, feet + right * d, radius, height, self, out exit)) return true;
                if (TryExit(feet, feet - right * d, radius, height, self, out exit)) return true;
                if (TryExit(feet, feet + (back + right).normalized * d, radius, height, self, out exit)) return true;
                if (TryExit(feet, feet + (back - right).normalized * d, radius, height, self, out exit)) return true;
            }

            // Anéis maiores, 24 direções.
            for (float d = 1.4f; d <= 3.01f; d += 0.2f)
                for (int k = 0; k < 24; k++)
                {
                    Vector3 dir = Quaternion.Euler(0f, yaw + 180f + (k % 2 == 0 ? k : -k - 1) * 7.5f, 0f) * Vector3.forward;
                    if (TryExit(feet, feet + dir * d, radius, height, self, out exit)) return true;
                }

            exit = feet;
            return false;
        }

        static bool TryExit(Vector3 from, Vector3 to, float radius, float height, Transform self, out Vector3 exit)
        {
            exit = to;
            // Chão embaixo (o piso da sala), não muito abaixo nem acima dos pés.
            if (!Physics.Raycast(to + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 1f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(ground.point.y - from.y) > 0.15f) return false;
            exit = new Vector3(to.x, ground.point.y + 0.02f, to.z);

            // Nada entre a cadeira e o ponto, na altura do peito (acima das mesas e dos encostos).
            Vector3 chest = from + Vector3.up * 1.4f, there = exit + Vector3.up * 1.4f;
            Vector3 line = there - chest;
            int hits = Physics.RaycastNonAlloc(chest, line.normalized, Hits, line.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
                if (Hits[i].collider.transform.root != self) return false;

            // Cabe a cápsula de pé.
            float r = radius * 0.95f;
            Vector3 bottom = exit + Vector3.up * (r + 0.03f), top = exit + Vector3.up * (height - r);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, r, Overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Overlaps[i].transform.root != self) return false;
            return true;
        }

        // ---------------------------------------------------------------- Menu de pausa

        public void AddTasks(List<LevelTask> tasks) { }

        public void AddKeys(List<(string key, string action)> keys)
        {
            if (_seats.Count > 0) keys.Add((KeyName, KeyAction));
        }
    }
}
