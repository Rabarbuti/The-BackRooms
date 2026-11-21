using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>Em que ponto da visita o Ascensorista está. Vai pela rede (portas, painel, corpo e som).</summary>
    public enum AttendantPhase : byte
    {
        /// <summary>Fora do andar: escondido dentro de um poço fechado, esperando a hora de chegar.</summary>
        Away,
        /// <summary>A seta do painel em cima da porta pisca: ele está chegando por aquele poço.</summary>
        Coming,
        /// <summary>Plim, a porta abre; ele escuta parado dentro da cabine e depois sai.</summary>
        Arriving,
        /// <summary>No andar: ronda, para para escutar e corre atrás do barulho.</summary>
        Out,
        /// <summary>Acabou o tempo: anda de volta até a porta da cabine (não escuta mais ninguém).</summary>
        Returning,
        /// <summary>Entrando na cabine.</summary>
        Boarding,
        /// <summary>Dentro da cabine, a porta fecha e o elevador parte.</summary>
        Leaving,
    }

    /// <summary>
    /// O Ascensorista ("The Attendant"), o inimigo que chega de elevador no Level 3. Herda da
    /// entidade o ouvido, o caminho pela grade e a captura, e muda:
    ///
    /// • <b>Cego.</b> Não enxerga (<see cref="HasSight"/> falso): anda na luz e no escuro, mas só
    ///   caça pelo barulho. Correndo ouve-se de longe (<see cref="hearCells"/> células de caminho),
    ///   andando de perto (<see cref="walkLoudness"/>), agachado andando só na mesma célula
    ///   (<see cref="crouchLoudness"/>), parado nada. A <see cref="breathRange"/> metros, sem parede no
    ///   meio, ouve até a respiração: parado e agachado no caminho dele não salva. Puxar um alarme
    ///   é barulho de corrida; os armários que batem e a sirene chegam pelo <see cref="WorldNoise"/>.
    /// • <b>Pega quem encosta</b>, de qualquer lado (<see cref="CatchNeedsFacing"/> falso), e leva para
    ///   dentro da cabine dele: a vítima aparece lá dentro, a porta fecha, e só então cai o sinal
    ///   (a volta ao início é a de sempre).
    /// • <b>Salinha acesa não é abrigo para ele</b> (<see cref="OfficeRoomsShelter"/> falso): o abrigo
    ///   do Zelador não vale; lá dentro o que salva é o silêncio. Só a cabine do início é abrigo.
    /// • <b>Vem e vai de elevador.</b> Pelo relógio da sessão (<see cref="OfficeElevators.Clock"/>): a
    ///   primeira chegada em <see cref="firstArrival"/> s; depois de partir, volta entre
    ///   <see cref="gapMin"/> e <see cref="gapMax"/> s. O poço sai da seed (<see cref="ShaftFor"/>),
    ///   pulando o que tem jogador dentro ou na frente. Antes do plim, a seta do painel em cima da
    ///   porta pisca (<see cref="comingSeconds"/>). No andar fica <see cref="staySeconds"/> s, parando
    ///   para escutar a cada destino; acabado o tempo, volta à cabine, a porta fecha e parte.
    ///   Enquanto ele está fora, a porta daquela cabine fica aberta (<see cref="ElevatorDoors.HeldOpen"/>,
    ///   ligado em todas as máquinas a partir de <see cref="Phase"/> e <see cref="Shaft"/>): quem manda
    ///   nela é ele, nem o relógio nem o botão de fechar a fecham. Quem estiver na cabine dele quando
    ///   ele volta é levado junto.
    /// • <b>O alarme de incêndio o chama</b> para o acionador (pelo <see cref="WorldNoise"/>); se
    ///   estiver fora do andar, chega mais cedo e vai direto para lá.
    ///
    /// Só o host decide (fase, poço, caminho, captura). Cada máquina liga a porta e o painel pelo
    /// que recebe.
    /// </summary>
    public sealed class AttendantBrain : EntityBrain, INoiseListener
    {
        /// <summary>O Ascensorista da sessão, em qualquer máquina. Nulo enquanto ele não apareceu.</summary>
        public static AttendantBrain Attendant { get; private set; }

        [Header("Ascensorista: visitas (segundos do relógio da sessão)")]
        public float firstArrival = 60f;
        public float gapMin = 30f;
        public float gapMax = 50f;
        [Tooltip("A seta do painel pisca isto antes do plim.")]
        public float comingSeconds = 5f;
        [Tooltip("Parado na cabine, escutando, depois que a porta começa a abrir.")]
        public float doorwaySeconds = 2.4f;
        [Tooltip("Tempo no andar, depois de sair da cabine.")]
        public float staySeconds = 75f;
        [Tooltip("Cada destino da ronda (e a cada listenEvery segundos andando): para e escuta por isto.")]
        public float listenSeconds = 2.5f;
        [Tooltip("Rondando, para para escutar pelo menos a cada tantos segundos.")]
        public float listenEvery = 7f;
        [Tooltip("Velocidade ao sair e entrar na cabine (m/s).")]
        public float boardSpeed = 1.1f;

        [Header("Ascensorista: ouvido")]
        [Tooltip("Barulho de quem anda, de 0 a 1 (correndo = 1). O piso do Escritório é duro.")]
        public float walkLoudness = 0.4f;
        [Tooltip("Barulho de quem anda agachado: só na mesma célula.")]
        public float crouchLoudness = 0.1f;
        [Tooltip("A esta distância (m), sem parede no meio, ouve a respiração de quem está parado.")]
        public float breathRange = 1.5f;

        [Header("Ascensorista: levar para a cabine")]
        [Tooltip("A vítima fica na cabine fechada isto antes de cair o sinal (s).")]
        public float takenSeconds = 2.6f;
        [Tooltip("A porta da cabine fica fechada com a vítima dentro por isto, depois reabre (s).")]
        public float takeDoorSeconds = 5f;

        [Header("Painel em cima das portas")]
        public Material indicatorOn;
        public Material indicatorOff;

        /// <summary>Em que ponto da visita ele está, em qualquer máquina.</summary>
        public AttendantPhase Phase => (AttendantPhase)_phase.Value;

        /// <summary>Índice do poço da visita atual (ordem de <see cref="Shafts"/>), em qualquer máquina; -1 sem.</summary>
        public int Shaft => _shaft.Value;

        /// <summary>Parado, escutando (para os estalos da boca). Em qualquer máquina.</summary>
        public bool Listening => _listening.Value;

        /// <summary>A porta da cabine dele está fechada com alguém dentro (em qualquer máquina).</summary>
        public bool Taking => _taking.Value;

        /// <summary>Quantas vezes chegou (só no host). Para os testes.</summary>
        public int Arrivals { get; private set; }

        /// <summary>Quantos levou para a cabine (só no host). Para os testes.</summary>
        public int Takes { get; private set; }

        /// <summary>Quantos barulhos do mundo (armário, sirene) o chamaram (só no host).</summary>
        public int WorldNoisesHeard { get; private set; }

        /// <summary>Hora do relógio da sessão da próxima chegada (só no host).</summary>
        public double NextArrival => _nextArrival;

        const float BoardTolerance = 0.12f;
        static readonly RaycastHit[] Hits = new RaycastHit[24];

        readonly NetworkVariable<byte> _phase = new NetworkVariable<byte>((byte)AttendantPhase.Away);
        readonly NetworkVariable<int> _shaft = new NetworkVariable<int>(-1);
        readonly NetworkVariable<bool> _listening = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _taking = new NetworkVariable<bool>();

        readonly List<ElevatorDoors> _shafts = new List<ElevatorDoors>();
        readonly List<Transform> _indicators = new List<Transform>();
        readonly List<MeshRenderer> _arrows = new List<MeshRenderer>();
        OfficeElevators _lifts;
        ElevatorDoors _held;

        double _nextArrival = -1;
        float _phaseAt;
        float _leaveAt;
        float _listenUntil;
        float _nextListen;
        float _takeUntil;
        Vector3 _lastGoal;
        Vector3? _pending;
        int _visit;
        bool _cabSwept;
        AttendantPhase _seenPhase;

        protected override bool IsLevel0Entity => false;
        protected override bool HurriesUnseen => false;
        protected override bool TiresInChase => false;
        protected override bool HasSight => false;
        protected override bool CatchNeedsFacing => false;
        protected override bool OfficeRoomsShelter => false;

        /// <summary>Os poços (portas de elevador que não são a da cabine do início), na ordem do construtor.</summary>
        public IReadOnlyList<ElevatorDoors> Shafts
        {
            get
            {
                EnsureShafts();
                return _shafts;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Attendant = this;
            WorldNoise.Register(this);
            OfficeElevators.VisitsByEnemy = true;

            if (!IsServer) return;
            _nextArrival = firstArrival;
            Park(0);
        }

        public override void OnNetworkDespawn()
        {
            WorldNoise.Unregister(this);
            Release();
            OfficeElevators.VisitsByEnemy = false;
            base.OnNetworkDespawn();
            if (Attendant == this) Attendant = null;
        }

        void OnDisable()
        {
            Release();
            if (Attendant == this) OfficeElevators.VisitsByEnemy = false;
        }

        // ---------------------------------------------------------------- Poços

        bool EnsureShafts()
        {
            OfficeElevators lifts = OfficeElevators.Instance;
            if (lifts == _lifts && _shafts.Count > 0) return true;

            _lifts = lifts;
            _shafts.Clear();
            _indicators.Clear();
            _arrows.Clear();
            if (lifts == null) return false;
            foreach (ElevatorDoors door in lifts.Doors)
                if (door != null && !door.IsCab) _shafts.Add(door);
            return _shafts.Count > 0;
        }

        /// <summary>Do poço para o saguão, no plano (a folha da porta olha para o saguão).</summary>
        static Vector3 ToLobby(ElevatorDoors door)
        {
            Vector3 f = door.transform.childCount > 0 ? door.transform.GetChild(0).forward : Vector3.forward;
            f.y = 0f;
            if (Mathf.Abs(f.x) > Mathf.Abs(f.z)) return new Vector3(Mathf.Sign(f.x), 0f, 0f);
            return new Vector3(0f, 0f, Mathf.Sign(f.z));
        }

        static float FloorY(ElevatorDoors door)
        {
            GameSession session = GameSession.Instance;
            return session != null && session.level != null ? session.level.transform.position.y : door.transform.position.y;
        }

        /// <summary>Onde ele fica dentro da cabine do poço <paramref name="i"/> (o meio da cabine).</summary>
        public Vector3 CabPoint(int i)
        {
            ElevatorDoors door = Shafts[i];
            Vector3 p = door.transform.position - ToLobby(door) * (LevelBuilder.Cell * 0.5f);
            p.y = FloorY(door);
            return p;
        }

        /// <summary>O meio da célula do saguão em frente à porta do poço <paramref name="i"/>.</summary>
        public Vector3 FrontPoint(int i)
        {
            ElevatorDoors door = Shafts[i];
            Vector3 p = door.transform.position + ToLobby(door) * (LevelBuilder.Cell * 0.5f);
            p.y = FloorY(door);
            return p;
        }

        /// <summary>O poço da chegada número <paramref name="visit"/>, pela seed (antes de pular os ocupados).</summary>
        public static int ShaftFor(int seed, int visit, int count)
        {
            if (count <= 0) return -1;
            return (int)(OfficeElevators.Hash(seed + 401, visit) % (uint)count);
        }

        static int Seed()
        {
            GameSession session = GameSession.Instance;
            return session != null && session.level != null && session.level.Data != null ? session.level.Data.Seed : 0;
        }

        // Tem jogador na frente da porta do poço, ou dentro da cabine com a porta aberta? Lá ele não
        // chega. Quem se trancou na cabine (porta fechada pelo botão) não conta: o abrigo dura até a
        // vez daquele poço; a cabine ronca antes (Coming) e quem continuar lá dentro é levado.
        bool Occupied(int i)
        {
            Vector3 cab = CabPoint(i), front = FrontPoint(i);
            bool open = _shafts[i].Openness > 0.05f;
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                Vector3 at = player.transform.position;
                if (Flat(at - front).magnitude < 4.5f) return true;
                if (open && InCab(at, i)) return true;
            }
            return false;
        }

        /// <summary>O ponto está dentro da cabine do poço <paramref name="i"/>?</summary>
        public bool InCab(Vector3 at, int i)
        {
            if (i < 0 || i >= Shafts.Count) return false;
            Vector3 local = Flat(at - CabPoint(i));
            return Mathf.Abs(local.x) < 1.35f && Mathf.Abs(local.z) < 1.35f;
        }

        // Host: leva quem está dentro da cabine dele (chegou numa cabine trancada com gente dentro, ou
        // voltou e achou alguém lá).
        void TakeEveryoneInCab(int i)
        {
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                if (player.controller != null && player.controller.IsReturning) continue;
                if (InCab(player.transform.position, i)) Take(player);
            }
        }

        // Host: esconde-se dentro da cabine do poço, de frente para o saguão.
        void Park(int i)
        {
            if (!EnsureShafts()) return;
            i = Mathf.Clamp(i, 0, _shafts.Count - 1);
            _shaft.Value = i;
            Place(CabPoint(i), ToLobby(_shafts[i]));
        }

        void Place(Vector3 at, Vector3 facing)
        {
            Quaternion rotation = Quaternion.LookRotation(facing.sqrMagnitude > 0.01f ? facing : Vector3.forward, Vector3.up);
            transform.SetPositionAndRotation(at, rotation);
            if (TryGetComponent(out NetworkTransform moved) && IsSpawned)
                moved.Teleport(at, rotation, transform.localScale);
        }

        void SetPhase(AttendantPhase phase)
        {
            if (Phase == phase) return;
            _phase.Value = (byte)phase;
            _phaseAt = Time.time;
        }

        /// <summary>Host, para os testes: começa a chegar agora pelo poço dado (-1 = o da seed).</summary>
        public void ForceArrival(int shaft = -1)
        {
            if (!IsServer || !EnsureShafts()) return;
            BeginComing(shaft >= 0 ? shaft : PickShaft());
        }

        /// <summary>Host, para os testes: a próxima chegada fica para esta hora do relógio da sessão.</summary>
        public void ScheduleArrival(double clock)
        {
            if (IsServer) _nextArrival = clock;
        }

        /// <summary>Host, para os testes: o tempo no andar acaba agora (volta à cabine).</summary>
        public void EndStayNow()
        {
            if (!IsServer) return;
            _leaveAt = Time.time;
        }

        int PickShaft()
        {
            int n = _shafts.Count;
            int first = ShaftFor(Seed(), _visit, n);
            for (int k = 0; k < n; k++)
            {
                int i = (first + k) % n;
                if (!Occupied(i)) return i;
            }
            return -1;
        }

        void BeginComing(int i)
        {
            if (i < 0) return;
            _visit++;
            Arrivals++;
            _cabSwept = false;
            Park(i);
            SetPhase(AttendantPhase.Coming);
            Halt(EntityState.Roam);
        }

        // ---------------------------------------------------------------- O que o host faz a cada quadro

        protected override bool Scripted(float now)
        {
            if (!EnsureShafts()) return true;
            int i = Shaft;
            // A porta reabre (vazia) depois de levar alguém, também nas fases em que ele está na cabine.
            if (_taking.Value && now >= _takeUntil) _taking.Value = false;

            switch (Phase)
            {
                case AttendantPhase.Away:
                {
                    if (EntityDirector.Paused || _nextArrival < 0) return true;
                    if (OfficeElevators.Clock() < _nextArrival) return true;
                    int pick = PickShaft();
                    if (pick < 0)
                    {
                        _nextArrival = OfficeElevators.Clock() + 4.0;
                        return true;
                    }
                    BeginComing(pick);
                    return true;
                }

                case AttendantPhase.Coming:
                    if (now - _phaseAt >= comingSeconds) SetPhase(AttendantPhase.Arriving);
                    return true;

                case AttendantPhase.Arriving:
                {
                    // A porta acabou de abrir: quem estava trancado na cabine dele é levado.
                    if (!_cabSwept && now - _phaseAt >= 1.2f)
                    {
                        _cabSwept = true;
                        TakeEveryoneInCab(i);
                    }
                    _listening.Value = now - _phaseAt < doorwaySeconds;
                    if (now - _phaseAt < doorwaySeconds || _taking.Value) return true;
                    if (!Walk(FrontPoint(i), boardSpeed)) return true;

                    // No saguão: começa a rondar (ou vai direto para onde a sirene tocou).
                    _listening.Value = false;
                    SetPhase(AttendantPhase.Out);
                    _leaveAt = now + staySeconds;
                    ForgetPath();
                    _lastGoal = Goal;
                    _listenUntil = 0f;
                    _nextListen = now + listenEvery;
                    if (_pending.HasValue)
                    {
                        GoTo(_pending.Value, EntityState.Investigate, 25f);
                        _pending = null;
                    }
                    return true;
                }

                case AttendantPhase.Boarding:
                {
                    if (!Walk(CabPoint(i), boardSpeed)) return true;
                    // Quem estava escondido na cabine dele vai junto.
                    TakeEveryoneInCab(i);
                    Turn(ToLobby(_shafts[i]));
                    SetPhase(AttendantPhase.Leaving);
                    return true;
                }

                case AttendantPhase.Leaving:
                    Turn(ToLobby(_shafts[i]));
                    if (now - _phaseAt < 3f) return true;
                    SetPhase(AttendantPhase.Away);
                    _taking.Value = false;
                    _nextArrival = OfficeElevators.Clock() + Random.Range(gapMin, gapMax);
                    return true;
            }

            return false;
        }

        protected override bool HoldStill(float now)
        {
            // A porta reabre (vazia) depois de levar alguém.
            if (_taking.Value && now >= _takeUntil) _taking.Value = false;

            if (Phase == AttendantPhase.Out)
            {
                if (now >= _leaveAt && !_taking.Value && State != EntityState.Feed)
                {
                    _listening.Value = false;
                    SetPhase(AttendantPhase.Returning);
                    GoTo(FrontPoint(Shaft), EntityState.Investigate, 60f);
                    return false;
                }

                // Na ronda, cada destino novo começa com uma parada para escutar, e no caminho ele
                // para de novo a cada listenEvery segundos.
                if (State == EntityState.Roam && (Flat(Goal - _lastGoal).sqrMagnitude > 0.25f || now >= _nextListen))
                {
                    _lastGoal = Goal;
                    _listenUntil = now + listenSeconds;
                    _nextListen = _listenUntil + listenEvery;
                }
                if (State != EntityState.Roam)
                {
                    _listenUntil = 0f;
                    _nextListen = now + listenEvery;
                }
                bool listening = now < _listenUntil;
                _listening.Value = listening;
                return listening;
            }

            if (Phase == AttendantPhase.Returning)
            {
                Vector3 front = FrontPoint(Shaft);
                if (Flat(transform.position - front).magnitude < 0.6f || (Arrived && Flat(transform.position - front).magnitude < 2f))
                {
                    SetPhase(AttendantPhase.Boarding);
                    Halt(EntityState.Roam);
                    return true;
                }
                if (Flat(Goal - front).sqrMagnitude > 0.25f || State != EntityState.Investigate)
                    GoTo(front, EntityState.Investigate, 60f);
                // Preso no caminho (não devia): passado um minuto, aparece na porta.
                if (now - _phaseAt > 60f) Place(front, -ToLobby(_shafts[Shaft]));
            }

            return false;
        }

        // Anda em linha reta (dentro e para fora da cabine, onde a grade não deixa). Verdadeiro ao chegar.
        bool Walk(Vector3 to, float speed)
        {
            Vector3 at = transform.position;
            Vector3 delta = Flat(to - at);
            if (delta.magnitude <= BoardTolerance)
            {
                transform.position = new Vector3(to.x, at.y, to.z);
                return true;
            }
            Turn(delta);
            transform.position = at + delta.normalized * Mathf.Min(delta.magnitude, speed * Time.deltaTime);
            return false;
        }

        void Turn(Vector3 facing)
        {
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing, Vector3.up), turnSpeed * 0.5f * Time.deltaTime);
        }

        // ---------------------------------------------------------------- Ouvido

        protected override float HearMargin(PlayerNetwork player, Vector3 at, float speed, int soundCells)
        {
            if (Phase != AttendantPhase.Out) return -1f;

            // A dois passos, sem parede no meio, ouve a respiração de quem não está escondido.
            Vector3 position = transform.position;
            if (player.Hiding == HideKind.None && Flat(at - position).magnitude <= breathRange &&
                !Blocked(position + Vector3.up * 1.2f, at + Vector3.up * (player.Crouching ? 0.6f : 1.1f)))
                return hearCells;

            float loudness = player.Working ? 1f
                           : speed < 0.4f ? 0f
                           : player.Crouching ? crouchLoudness
                           : speed > 3.9f ? 1f : walkLoudness;
            if (loudness <= 0f || soundCells < 0) return -1f;
            return loudness * hearCells - soundCells;
        }

        /// <summary>Barulho do mundo (armário batendo, a sirene do alarme). Em todas as máquinas; age no host.</summary>
        public void HearWorldNoise(Vector3 at, float radius)
        {
            if (!IsSpawned || !IsServer) return;
            float distance = Flat(at - transform.position).magnitude;

            if (Phase == AttendantPhase.Out)
            {
                if (distance > radius) return;
                WorldNoisesHeard++;
                HearNoise(at);
                // A sirene o segura no andar até ele chegar lá.
                SwitchObjective objective = SwitchObjective.Instance;
                if (objective != null && objective.Complete) _leaveAt = Mathf.Max(_leaveAt, Time.time + 30f);
                return;
            }

            // Fora do andar, só o alarme disparando o chama: chega logo e vai direto para lá.
            if (Phase == AttendantPhase.Away || Phase == AttendantPhase.Coming || Phase == AttendantPhase.Arriving)
            {
                // Só a sirene (o estrondo de alcance do objetivo cumprido); um armário batendo não o chama.
                SwitchObjective objective = SwitchObjective.Instance;
                if (objective == null || !objective.Complete || radius < SwitchObjective.CompleteNoiseRadius) return;
                WorldNoisesHeard++;
                _pending = at;
                if (Phase == AttendantPhase.Away) _nextArrival = System.Math.Min(_nextArrival, OfficeElevators.Clock() + 6.0);
            }
        }

        bool Blocked(Vector3 from, Vector3 to)
        {
            GameSession session = GameSession.Instance;
            Transform root = session != null && session.level != null ? session.level.transform : null;
            if (root == null) return false;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return false;
            int count = Physics.RaycastNonAlloc(from, delta / distance, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < count; k++)
                if (Hits[k].collider.transform.IsChildOf(root)) return true;
            return false;
        }

        // ---------------------------------------------------------------- Levar para a cabine

        protected override void Catch(PlayerNetwork player) => Take(player);

        void Take(PlayerNetwork player)
        {
            if (player == null || Shaft < 0) return;
            Takes++;
            Vector3 cab = CabPoint(Shaft);
            Vector3 lobby = ToLobby(_shafts[Shaft]);
            // Um pouco para o fundo e de lado: ele mesmo pode estar parado no meio da cabine.
            Vector3 lateral = new Vector3(-lobby.z, 0f, lobby.x);
            Vector3 into = cab - lobby * 0.45f + lateral * 0.35f;
            _taking.Value = true;
            _takeUntil = Time.time + takeDoorSeconds;
            float yaw = Quaternion.LookRotation(lobby, Vector3.up).eulerAngles.y;
            TakeRpc(player.OwnerClientId, into, yaw);
        }

        [Rpc(SendTo.Everyone)]
        void TakeRpc(ulong clientId, Vector3 into, float yaw)
        {
            PlayerNetwork local = PlayerNetwork.Local;
            if (local == null || local.OwnerClientId != clientId) return;
            local.controller.GetTaken(into, yaw, takenSeconds);
        }

        // ---------------------------------------------------------------- Porta e painel (todas as máquinas)

        void LateUpdate()
        {
            if (!IsSpawned || !EnsureShafts()) return;
            AttendantPhase phase = Phase;
            int i = Shaft;

            // A porta da cabine dele: aberta da chegada até ele entrar de volta, menos enquanto leva alguém.
            bool hold = i >= 0 && i < _shafts.Count && !Taking &&
                        (phase == AttendantPhase.Arriving || phase == AttendantPhase.Out ||
                         phase == AttendantPhase.Returning || phase == AttendantPhase.Boarding);
            ElevatorDoors want = hold ? _shafts[i] : null;
            if (want != _held)
            {
                if (_held != null) _held.HeldOpen = false;
                _held = want;
            }
            if (_held != null) _held.HeldOpen = true;

            UpdateIndicators(phase, i);

            // A cabine ronca quando ele vem (é o aviso para quem se trancou dentro dela).
            if (phase != _seenPhase)
            {
                if (phase == AttendantPhase.Coming && i >= 0 && i < _shafts.Count)
                    AudioSource.PlayClipAtPoint(OfficeSounds.Rumble, CabPoint(i) + Vector3.up * 1.5f, 0.9f);
                _seenPhase = phase;
            }
        }

        void Release()
        {
            if (_held != null) _held.HeldOpen = false;
            _held = null;
        }

        // Painel em cima de cada porta de poço: uma seta para baixo, âmbar, que pisca enquanto ele vem
        // e fica acesa enquanto ele está no andar.
        void UpdateIndicators(AttendantPhase phase, int shaft)
        {
            if (indicatorOn == null || indicatorOff == null) return;
            if (_indicators.Count != _shafts.Count) BuildIndicators();

            bool blink = Mathf.Repeat(Time.time, 0.5f) < 0.3f;
            for (int k = 0; k < _arrows.Count; k++)
            {
                bool on = k == shaft && (phase == AttendantPhase.Coming ? blink
                                         : phase != AttendantPhase.Away && phase != AttendantPhase.Leaving);
                Material m = on ? indicatorOn : indicatorOff;
                if (_arrows[k] != null && _arrows[k].sharedMaterial != m) _arrows[k].sharedMaterial = m;
            }
        }

        /// <summary>A seta do painel do poço <paramref name="i"/> está acesa agora (nesta máquina).</summary>
        public bool IndicatorLit(int i) => i >= 0 && i < _arrows.Count && _arrows[i] != null && _arrows[i].sharedMaterial == indicatorOn;

        void BuildIndicators()
        {
            foreach (Transform old in _indicators)
                if (old != null) Destroy(old.gameObject);
            _indicators.Clear();
            _arrows.Clear();

            foreach (ElevatorDoors door in _shafts)
            {
                Vector3 lobby = ToLobby(door);
                var panel = new GameObject("PainelDoAscensorista").transform;
                panel.SetParent(door.transform, false);
                panel.position = new Vector3(door.transform.position.x, FloorY(door) + 2.6f, door.transform.position.z) + lobby * 0.1f;
                panel.rotation = Quaternion.LookRotation(lobby, Vector3.up);

                MeshRenderer plate = Quad(panel, "Placa", Plate(), indicatorOff);
                plate.transform.localPosition = Vector3.zero;
                plate.sharedMaterial = indicatorOff;
                // A placa apagada é o material "desligado"; a seta muda de material.
                MeshRenderer arrow = Quad(panel, "Seta", Arrow(), indicatorOff);
                arrow.transform.localPosition = new Vector3(0f, 0f, 0.004f);
                _indicators.Add(panel);
                _arrows.Add(arrow);
            }
        }

        static MeshRenderer Quad(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        static Mesh _plate, _arrow;

        // Placa de 0,42 x 0,2 m (a seta fica na frente dela). Faces para +Z (o saguão).
        static Mesh Plate()
        {
            if (_plate != null) return _plate;
            _plate = new Mesh { name = "PainelPlaca" };
            float w = 0.21f, h = 0.1f;
            _plate.vertices = new[] { new Vector3(-w, -h, 0f), new Vector3(w, -h, 0f), new Vector3(w, h, 0f), new Vector3(-w, h, 0f) };
            _plate.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _plate.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            _plate.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            _plate.RecalculateBounds();
            return _plate;
        }

        // Seta para baixo: o elevador está descendo até este andar.
        static Mesh Arrow()
        {
            if (_arrow != null) return _arrow;
            _arrow = new Mesh { name = "PainelSeta" };
            _arrow.vertices = new[]
            {
                new Vector3(-0.025f, 0.075f, 0f), new Vector3(0.025f, 0.075f, 0f), new Vector3(0.025f, 0.0f, 0f), new Vector3(-0.025f, 0.0f, 0f),
                new Vector3(-0.07f, 0.0f, 0f), new Vector3(0.07f, 0.0f, 0f), new Vector3(0f, -0.075f, 0f),
            };
            _arrow.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5 };
            _arrow.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            _arrow.uv = new Vector2[7];
            _arrow.RecalculateBounds();
            return _arrow;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
