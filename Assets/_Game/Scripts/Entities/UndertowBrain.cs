using System;
using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace BackRooms.Entities
{
    public enum UndertowState : byte
    {
        /// <summary>No fundo do covil (um poço), parada. Ouve, mas leva um instante para acordar.</summary>
        Asleep,
        /// <summary>Acordando ou surgindo de um cano: sobe do fundo, com o gemido e as bolhas. Parada.</summary>
        Waking,
        /// <summary>Indo até onde ouviu barulho, pelo fundo.</summary>
        Investigate,
        /// <summary>Ouviu de perto e há pouco: arranca, subindo para quem nada.</summary>
        Hunt,
        /// <summary>Quem ela caçava saiu da água: ronda a borda por onde ele saiu.</summary>
        Circle,
        /// <summary>Voltando para o covil.</summary>
        Return,
        /// <summary>Nos canos, de uma piscina para outra: afunda, some e reaparece noutra água.</summary>
        Travel,
        /// <summary>Pegou alguém: afunda com ele.</summary>
        Feed
    }

    /// <summary>
    /// O Undertow, o inimigo da água funda das Piscinas (Level 2). Só o host pensa por ele; as
    /// outras máquinas recebem a posição pelo NetworkTransform e o estado por uma NetworkVariable.
    /// Não herda do <see cref="EntityBrain"/>: ele não anda no chão, não enxerga e não sai da água.
    ///
    /// As regras, em linguagem de jogador:
    /// • Vive na água funda (piscinas, raias, canais, poços). O seco e o raso são abrigo; na maré
    ///   alta o raso fica fundo e vira território dele (<see cref="UndertowNav"/>).
    /// • Dorme no fundo de um poço (o covil). É cego: caça pelo barulho NA ÁGUA (os passos, braçadas
    ///   e espirros que o <see cref="Footsteps"/> já toca). Pular na água espirra e se ouve de longe;
    ///   nadar à tona se ouve de perto; nadar submerso, só encostado; parado, nada.
    /// • As piscinas são ligadas por baixo, pelos canos: um espirro em outra água o chama, e ele
    ///   afunda, some e reaparece lá (na parte dessa água mais longe do barulho), com o mesmo aviso
    ///   de quando acorda.
    /// • Aviso: acordando ou surgindo, um gemido grave e bolhas; nadando, um latejar grave que
    ///   cresce de perto, a sombra passando no fundo e as bolhas subindo até a superfície.
    /// • Pega quem está nadando (ou com água acima da cintura) na água dele: puxa para o fundo
    ///   (NO SIGNAL e volta ao início, como a entidade). Quem sai da água (escada ou borda) escapa:
    ///   ele ronda a borda um tempo e volta a dormir.
    /// • Drenada a fase, a água funda fica com 5 cm: ali ele se arrasta pelo fundo, devagar, e
    ///   ainda pega quem anda no fundo das piscinas vazias; nos poços (4 m de água ainda) nada.
    /// </summary>
    public sealed class UndertowBrain : NetworkBehaviour, INoiseListener
    {
        /// <summary>O Undertow da sessão, em qualquer máquina. Nulo enquanto não apareceu.</summary>
        public static UndertowBrain Instance { get; private set; }

        /// <summary>O nome que o jogador lê (a língua do jogo pode mudar: aqui).</summary>
        public const string DisplayName = "THE UNDERTOW";

        [Header("Velocidade (m/s). O jogador nada a 1,26.")]
        public float returnSpeed = 1.4f;
        public float investigateSpeed = 2.3f;
        [Tooltip("Ouviu alguém há pouco e de perto: arranca.")]
        public float huntSpeed = 3.3f;
        [Tooltip("Arrastando-se no fundo de uma piscina drenada (o jogador anda a 2,8).")]
        public float crawlSpeed = 0.9f;
        public float acceleration = 5f;
        public float verticalSpeed = 2.2f;
        [Tooltip("Graus por segundo.")]
        public float turnSpeed = 200f;

        [Header("Ouvido")]
        [Tooltip("Alcance, em metros de água, de um barulho de força 1 (um espirro). Braçada à tona ~0,47 (11 m); submersa ~0,12 (3 m).")]
        public float hearRange = 24f;
        [Tooltip("Barulho mínimo que se ouve de outra água, pelos canos (o espirro de quem pula, correr no raso).")]
        public float pipeLoudness = 0.8f;
        [Tooltip("Ouviu alguém a menos disto, há menos de huntMemory s: caça (mais rápido).")]
        public float huntRange = 9f;
        public float huntMemory = 1.6f;
        [Tooltip("Quem fez barulho há menos disto e sai da água (escada, borda): ela vai rondar a borda por onde saiu.")]
        public float circleMemory = 8f;
        [Tooltip("Segundos indo até um barulho antes de desistir (sem ouvir mais nada).")]
        public float investigateSeconds = 12f;
        [Tooltip("Segundos parada no ponto do barulho (contados da chegada) antes de voltar.")]
        public float lingerSeconds = 4f;

        [Header("Tempos (s)")]
        [Tooltip("Acordando ou surgindo de um cano: parado, subindo, com o gemido.")]
        public float wakeSeconds = 2.2f;
        [Tooltip("Rondando a borda por onde a presa saiu.")]
        public float circleSeconds = 8f;
        [Tooltip("Nos canos: este tempo mais a distância dividida por pipeSpeed.")]
        public float pipeSeconds = 4f;
        public float pipeSpeed = 8f;
        [Tooltip("Afundando antes de sumir no cano.")]
        public float diveSeconds = 1.4f;
        public float feedSeconds = 3f;
        [Tooltip("Tempo em que ignora quem acabou de pegar.")]
        public float mercySeconds = 10f;

        [Header("Captura")]
        [Tooltip("Distância da cabeça dele ao meio do corpo do jogador, em metros.")]
        public float catchRadius = 1.4f;
        [Tooltip("Água acima dos pés do jogador a partir da qual ele pode ser pego (acima da cintura).")]
        public float catchSubmersion = 1.0f;

        [Header("Corpo (m)")]
        [Tooltip("Da origem (meio do corpo) até a cabeça, para a frente.")]
        public float headForward = 1.25f;
        public float bodyRadius = 0.45f;

        public UndertowState State => (UndertowState)_state.Value;

        /// <summary>Disparado em todas as máquinas quando o estado muda.</summary>
        public event Action<UndertowState> StateChanged;

        /// <summary>Quantos barulhos ouviu (só no host).</summary>
        public int NoisesHeard { get; private set; }
        /// <summary>Quantos jogadores pegou (só no host).</summary>
        public int Catches { get; private set; }
        /// <summary>Quantas vezes atravessou os canos (só no host).</summary>
        public int Travels { get; private set; }
        /// <summary>Para onde vai (só no host).</summary>
        public Vector3 Goal => _goal;
        /// <summary>O covil, onde dorme (só no host).</summary>
        public Vector3 Lair => _lair;
        /// <summary>A navegação (só no host).</summary>
        public UndertowNav Nav => _nav;
        /// <summary>Último barulho ouvido: onde e quanto (só no host, para os testes).</summary>
        public Vector3 LastHeardAt { get; private set; }
        public float LastHeardLoudness { get; private set; }
        /// <summary>Barulhos que chegaram mas não foram ouvidos (longe demais; só no host).</summary>
        public int NoisesIgnored { get; private set; }
        /// <summary>Está se arrastando (sem água para nadar)? Em todas as máquinas, pela posição.</summary>
        public bool Crawling { get; private set; }

        /// <summary>A cabeça, no mundo: é dela que se mede a captura.</summary>
        public Vector3 Head => transform.position + transform.forward * headForward;

        const float SenseInterval = 0.2f;
        static readonly RaycastHit[] Hits = new RaycastHit[24];

        readonly NetworkVariable<byte> _state = new NetworkVariable<byte>();
        readonly List<Vector3> _path = new List<Vector3>();
        readonly Dictionary<PlayerNetwork, float> _mercy = new Dictionary<PlayerNetwork, float>();
        readonly Dictionary<Footsteps, Action<Vector3, float>> _ears = new Dictionary<Footsteps, Action<Vector3, float>>();
        readonly List<Footsteps> _drop = new List<Footsteps>();

        UndertowNav _nav;
        LevelBuilder _level;
        int[,] _reach;

        Vector3 _goal, _destination, _lair, _velocity;
        bool _hasGoal, _arrived, _direct;
        int _pathIndex;
        bool _goalInside;
        float _repathAt, _senseAt, _stateUntil, _lastHeardTime, _arrivedAt;
        PlayerNetwork _target;
        Vector3 _circleCenter;
        int _circleLeg;

        // Os canos: para onde vai, quando chega e se já sumiu.
        Vector3 _travelTo;
        float _travelArrive;
        bool _travelHidden;
        UndertowState _afterTravel;
        Vector3 _afterTravelGoal;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _state.OnValueChanged += OnStateChanged;
            WorldNoise.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= OnStateChanged;
            WorldNoise.Unregister(this);
            foreach (KeyValuePair<Footsteps, Action<Vector3, float>> ear in _ears)
                if (ear.Key != null) ear.Key.MadeNoise -= ear.Value;
            _ears.Clear();
            if (Instance == this) Instance = null;
        }

        void OnStateChanged(byte before, byte after) => StateChanged?.Invoke((UndertowState)after);

        /// <summary>Host: o covil (onde dorme e para onde volta). Posto pelo diretor.</summary>
        public void SetLair(Vector3 lair) => _lair = lair;

        void Update()
        {
            UpdateCrawling();
            if (!IsSpawned || !IsServer || !EnsureLevel()) return;

            float now = Time.time;
            if (now >= _senseAt)
            {
                _senseAt = now + SenseInterval;
                PoolWater water = PoolWater.Current;
                _nav.Refresh(water != null ? water.SurfaceY : LevelData.WaterLevel);
                Listen();
                Watch(now);
            }

            Think(now);
            Move(Time.deltaTime, now);
            TryCatch(now);
        }

        // Em todas as máquinas: arrasta-se onde a água não dá para nadar (fase drenada).
        void UpdateCrawling()
        {
            PoolWater water = PoolWater.Current;
            GameSession session = GameSession.Instance;
            Crawling = water != null && session != null && session.level != null
                       && !UndertowNav.SwimmableAt(session.level, transform.position, water.SurfaceY);
        }

        bool EnsureLevel()
        {
            GameSession session = GameSession.Instance;
            LevelBuilder level = session != null ? session.level : null;
            if (level == null || level.Data == null || !level.Data.HasPools) return false;

            if (_nav == null || _nav.Data != level.Data)
            {
                _level = level;
                _nav = new UndertowNav(level);
                _reach = _nav.NewMap();
                PoolWater water = PoolWater.Current;
                _nav.Refresh(water != null ? water.SurfaceY : LevelData.WaterLevel);
                if (_lair == Vector3.zero) _lair = transform.position;
            }
            return true;
        }

        // ---------------------------------------------------------------- Ouvido

        // Os passos de cada jogador já tocam aqui (os dos outros também, calculados pela posição):
        // ouvir é assinar o MadeNoise de cada um.
        void Listen()
        {
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || player.footsteps == null || _ears.ContainsKey(player.footsteps)) continue;
                PlayerNetwork who = player;
                Action<Vector3, float> ear = (at, loudness) => OnPlayerNoise(who, at, loudness);
                player.footsteps.MadeNoise += ear;
                _ears[player.footsteps] = ear;
            }

            _drop.Clear();
            foreach (Footsteps steps in _ears.Keys)
                if (steps == null) _drop.Add(steps);
            foreach (Footsteps steps in _drop) _ears.Remove(steps);
        }

        void OnPlayerNoise(PlayerNetwork player, Vector3 at, float loudness)
        {
            if (!IsServer || _nav == null || player == null || !player.InGame) return;
            if (_mercy.TryGetValue(player, out float until) && Time.time < until) return;
            // Só o que soa na água (ou no fundo das piscinas drenadas, que é dele).
            if (!WetAt(at) && !_nav.InTerritory(at)) return;
            Hear(at, loudness, player);
        }

        /// <summary>Barulho do mundo (<see cref="WorldNoise"/>): conta se cai na água dele.</summary>
        public void HearWorldNoise(Vector3 at, float radius)
        {
            if (!IsServer || _nav == null) return;
            if (!WetAt(at) && !_nav.InTerritory(at)) return;
            Hear(at, 1f, null);
        }

        static bool WetAt(Vector3 at)
        {
            PoolWater water = PoolWater.Current;
            return water != null && water.SurfaceY - at.y > Footsteps.WetDepth;
        }

        /// <summary>
        /// Host: um barulho em <paramref name="at"/> de força <paramref name="loudness"/> (0 a 1).
        /// Ouve se a distância pela água até ele (em linha reta ou pelas células, o que for maior)
        /// cabe em <see cref="hearRange"/> vezes a força; de outra água, só barulho forte, pelos canos.
        /// Público para os testes.
        /// </summary>
        public bool Hear(Vector3 at, float loudness, PlayerNetwork player)
        {
            if (!IsServer || _nav == null || loudness <= 0f) return false;
            if (State == UndertowState.Feed || State == UndertowState.Travel) return false;
            if (!_nav.EntryCell(at, out int ex, out int ez)) { NoisesIgnored++; return false; }
            if (!_nav.WorldToCell(transform.position, out int cx, out int cz)) return false;

            int mine = _nav.Region[cx, cz];
            int theirs = _nav.Region[ex, ez];
            float now = Time.time;

            if (mine >= 0 && mine == theirs)
            {
                _nav.Distances(cx, cz, _reach);
                int cells = _reach[ex, ez];
                float distance = Mathf.Max(Vector3.Distance(Head, at), (cells - 1) * LevelBuilder.Cell);
                if (loudness * hearRange < distance) { NoisesIgnored++; return false; }

                Heard(at, loudness, player, now);
                if (State == UndertowState.Asleep)
                {
                    BeginWaking(now, UndertowState.Investigate, at);
                    return true;
                }
                if (State == UndertowState.Waking)
                {
                    _afterTravelGoal = at;
                    return true;
                }

                SetGoal(ClampGoal(at));
                bool close = Vector3.Distance(Head, at) < huntRange;
                SetState(close ? UndertowState.Hunt : UndertowState.Investigate);
                _stateUntil = now + investigateSeconds;
                return true;
            }

            // Outra água: só pelos canos, só barulho forte, e só se não está caçando alguém aqui.
            if (loudness < pipeLoudness || theirs < 0) { NoisesIgnored++; return false; }
            if (State == UndertowState.Hunt || State == UndertowState.Waking) { NoisesIgnored++; return false; }
            if (State == UndertowState.Investigate && now - _lastHeardTime < 4f) { NoisesIgnored++; return false; }

            Heard(at, loudness, player, now);
            BeginTravel(EmergeCell(theirs, at), UndertowState.Investigate, at, now);
            return true;
        }

        void Heard(Vector3 at, float loudness, PlayerNetwork player, float now)
        {
            NoisesHeard++;
            LastHeardAt = at;
            LastHeardLoudness = loudness;
            _lastHeardTime = now;
            if (player != null) _target = player;
        }

        // Onde surge numa água: num poço dela, se há (sobe do ralo); senão, na célula dela mais longe do barulho.
        Vector3 EmergeCell(int region, Vector3 noise)
        {
            LevelData data = _nav.Data;
            int bx = -1, bz = -1;
            float best = -1f;
            bool wellFound = false;
            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++)
                {
                    if (_nav.Region[x, z] != region) continue;
                    bool well = data.Depth[x, z] == PoolDepth.Well;
                    if (wellFound && !well) continue;
                    Vector3 c = _nav.CellCenter(x, z);
                    float d = new Vector2(c.x - noise.x, c.z - noise.z).magnitude;
                    if (well && !wellFound) { wellFound = true; best = -1f; }
                    if (d <= best) continue;
                    best = d;
                    bx = x;
                    bz = z;
                }
            return bx >= 0 ? _nav.CellCenter(bx, bz) : transform.position;
        }

        // ---------------------------------------------------------------- Decisão

        // Quem ela caça saiu da água? Ronda a borda por onde ele saiu.
        void Watch(float now)
        {
            if (State != UndertowState.Hunt && State != UndertowState.Investigate) return;
            if (_target == null || !_target.InGame) return;
            if (now - _lastHeardTime > circleMemory) return;

            Vector3 at = _target.transform.position;
            if (Catchable(_target, at)) return;
            // Fora da água dela (no seco, no raso, na escada): ela não vai, ronda.
            if (!_nav.Nearest(at, _nav.RegionAt(transform.position), out int x, out int z)) return;
            _circleCenter = _nav.ClampToCell(x, z, at, bodyRadius + 0.4f);
            _target = null;
            _circleLeg = 0;
            SetGoal(_circleCenter);
            SetState(UndertowState.Circle);
            _stateUntil = now + circleSeconds;
        }

        void Think(float now)
        {
            switch (State)
            {
                case UndertowState.Asleep:
                    break;

                case UndertowState.Waking:
                    if (now >= _stateUntil)
                    {
                        SetGoal(ClampGoal(_afterTravelGoal));
                        SetState(_afterTravel);
                        _stateUntil = now + investigateSeconds;
                    }
                    break;

                case UndertowState.Investigate:
                case UndertowState.Hunt:
                    if (State == UndertowState.Hunt && now - _lastHeardTime > huntMemory) SetState(UndertowState.Investigate);
                    if (now >= _stateUntil) { BeginReturn(now); break; }
                    // Chegou onde ouviu e nada mais: fica um instante (subindo até a altura do barulho) e volta.
                    // Conta da chegada, não do barulho: senão o tempo acordando e nadando come a espera.
                    if (_arrived && now - Mathf.Max(_lastHeardTime, _arrivedAt) > lingerSeconds) BeginReturn(now);
                    break;

                case UndertowState.Circle:
                    if (now >= _stateUntil) { BeginReturn(now); break; }
                    if (_arrived)
                    {
                        // Vai e volta ao longo da borda, a 2 m para cada lado.
                        _circleLeg++;
                        Vector3 side = Vector3.Cross(Vector3.up, transform.forward).normalized;
                        Vector3 next = _circleCenter + side * ((_circleLeg % 2 == 0 ? 1f : -1f) * 2f);
                        if (_nav.WorldToCell(_circleCenter, out int cx, out int cz)) next = _nav.ClampToCell(cx, cz, next, bodyRadius + 0.3f);
                        SetGoal(next);
                    }
                    break;

                case UndertowState.Return:
                    if (_arrived)
                    {
                        SetState(UndertowState.Asleep);
                        _velocity = Vector3.zero;
                    }
                    break;

                case UndertowState.Travel:
                    ThinkTravel(now);
                    break;

                case UndertowState.Feed:
                    if (now >= _stateUntil) BeginReturn(now);
                    break;
            }
        }

        void SetState(UndertowState state)
        {
            if (State == state) return;
            _state.Value = (byte)state;
        }

        void SetGoal(Vector3 goal)
        {
            if (!_hasGoal || Flat(goal - _goal).sqrMagnitude > 1f) _repathAt = 0f;
            _goal = goal;
            _hasGoal = true;
            _arrived = false;
        }

        Vector3 ClampGoal(Vector3 at)
        {
            if (_nav.WorldToCell(at, out int x, out int z) && _nav.Territory[x, z]) return at;
            return _nav.EntryCell(at, out x, out z) ? _nav.ClampToCell(x, z, at, bodyRadius + 0.3f) : at;
        }

        void BeginWaking(float now, UndertowState after, Vector3 goal)
        {
            _afterTravel = after;
            _afterTravelGoal = goal;
            _velocity = Vector3.zero;
            SetState(UndertowState.Waking);
            _stateUntil = now + wakeSeconds;
        }

        /// <summary>Por que parou por último (para os testes): quando, por quê e onde.</summary>
        public string LastStop { get; private set; } = "";

        void Arrive(string why)
        {
            if (!_arrived) _arrivedAt = Time.time;
            _arrived = true;
            string line = $"{Time.time:F1}s {State} {why} (ele em {transform.position}, meta {_goal}, destino {_destination}, alcançável={_goalInside}, direto={_direct}, caminho {_path.Count}/{_pathIndex})";
            LastStop = LastStop.Length > 900 ? LastStop.Substring(LastStop.Length - 600) + " || " + line : (LastStop.Length > 0 ? LastStop + " || " : "") + line;
        }

        void BeginReturn(float now)
        {
            LastStop += $" | {now:F1}s voltando de {State}";
            _target = null;
            if (!_nav.WorldToCell(transform.position, out int cx, out int cz)) return;
            int mine = _nav.Region[cx, cz];
            int lairRegion = _nav.RegionAt(_lair);

            if (lairRegion >= 0 && lairRegion != mine)
            {
                BeginTravel(_lair, UndertowState.Asleep, _lair, now);
                return;
            }
            SetGoal(_lair);
            SetState(UndertowState.Return);
        }

        /// <summary>Host: manda ir pelos canos até <paramref name="to"/>; lá surge e segue no estado dado.</summary>
        void BeginTravel(Vector3 to, UndertowState after, Vector3 goal, float now)
        {
            _travelTo = to;
            _afterTravel = after;
            _afterTravelGoal = goal;
            _travelHidden = false;
            _travelArrive = now + diveSeconds + pipeSeconds + Flat(to - transform.position).magnitude / Mathf.Max(0.1f, pipeSpeed);
            _stateUntil = now + diveSeconds;
            _velocity = Vector3.zero;
            _target = null;
            Travels++;
            SetState(UndertowState.Travel);
        }

        void ThinkTravel(float now)
        {
            if (!_travelHidden)
            {
                if (now < _stateUntil) return;
                // Sumiu no fundo: vai, escondido, para o outro lado (o corpo não aparece em Travel).
                _travelHidden = true;
                Vector3 floor = _travelTo;
                if (_nav.WorldToCell(_travelTo, out int x, out int z)) floor.y = _nav.FloorY(x, z) + 0.35f;
                Place(floor, transform.rotation);
                return;
            }

            if (now < _travelArrive) return;
            _travelHidden = false;
            if (_afterTravel == UndertowState.Asleep)
            {
                SetGoal(_lair);
                SetState(UndertowState.Return);
                return;
            }
            BeginWaking(now, _afterTravel, _afterTravelGoal);
        }

        /// <summary>Host: põe num ponto de uma vez (sem a rede interpolar o caminho).</summary>
        public void Place(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (TryGetComponent(out NetworkTransform moved)) moved.Teleport(position, rotation, transform.localScale);
            _velocity = Vector3.zero;
            _path.Clear();
            _repathAt = 0f;
        }

        /// <summary>Host, para os testes: dorme já, no ponto dado (o covil).</summary>
        public void SleepAt(Vector3 position)
        {
            _lair = position;
            if (_nav != null && _nav.WorldToCell(position, out int x, out int z)) position.y = _nav.FloorY(x, z) + 0.35f;
            Place(position, transform.rotation);
            _target = null;
            _hasGoal = false;
            _mercy.Clear();
            SetState(UndertowState.Asleep);
        }

        // ---------------------------------------------------------------- Movimento

        float SpeedNow()
        {
            float speed;
            switch (State)
            {
                case UndertowState.Hunt: speed = huntSpeed; break;
                case UndertowState.Investigate: speed = investigateSpeed; break;
                case UndertowState.Circle:
                case UndertowState.Return: speed = returnSpeed; break;
                default: return 0f;
            }
            return Crawling ? Mathf.Min(speed, crawlSpeed) : speed;
        }

        bool Moving => State == UndertowState.Investigate || State == UndertowState.Hunt
                    || State == UndertowState.Circle || State == UndertowState.Return;

        void Move(float dt, float now)
        {
            Vector3 position = transform.position;
            Vector3 desired = Vector3.zero;
            Vector3 aim = position;

            // Fora do território (a maré desceu debaixo dele): volta direto para a célula boa mais perto.
            if (_nav.WorldToCell(position, out int cx, out int cz) && !_nav.Territory[cx, cz] && State != UndertowState.Travel)
            {
                if (_nav.Nearest(position, -1, out int nx, out int nz))
                {
                    Vector3 to = Flat(_nav.CellCenter(nx, nz) - position);
                    desired = to.normalized * Mathf.Max(returnSpeed, 1.5f);
                }
            }
            else if (Moving && _hasGoal)
            {
                if (now >= _repathAt) Repath(position, now);
                if (!_arrived)
                {
                    bool last = _direct || _pathIndex >= _path.Count;
                    Vector3 next = last ? _destination : _path[_pathIndex];
                    aim = next;
                    // Indo a um barulho, o último trecho leva a CABEÇA ao ponto (é dela que se pega).
                    bool byHead = last && (State == UndertowState.Investigate || State == UndertowState.Hunt);
                    Vector3 to = Flat(next - (byHead ? Head : position));
                    if (to.magnitude < 0.35f)
                    {
                        if (!_direct && _pathIndex < _path.Count)
                        {
                            _pathIndex++;
                            Shortcut(position);
                        }
                        else Arrive($"chegou (cabeça a {to.magnitude:F2} m do destino {next})");
                    }
                    else desired = to.normalized * SpeedNow();
                }
            }

            _velocity = Vector3.MoveTowards(_velocity, desired, acceleration * dt);
            Vector3 moved = position + _velocity * dt;
            moved.y = Mathf.MoveTowards(position.y, TargetHeight(moved, aim), verticalSpeed * dt);
            transform.position = moved;

            Vector3 facing = _velocity.sqrMagnitude > 0.04f ? _velocity
                           : State == UndertowState.Hunt && _target != null ? Flat(_target.transform.position - moved)
                           : Vector3.zero;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing, Vector3.up), turnSpeed * dt);
        }

        // A altura em que nada: dormindo e indo pelo fundo; caçando, sobe para quem ouviu (vem por baixo).
        float TargetHeight(Vector3 at, Vector3 aim)
        {
            if (!_nav.WorldToCell(at, out int x, out int z)) return at.y;
            float floor = _nav.FloorY(x, z);
            // Saindo do poço para a água funda, já sobe: vale o piso mais alto, o daqui ou o de onde vai.
            if (State != UndertowState.Asleep && State != UndertowState.Travel && _nav.WorldToCell(aim, out int ax, out int az) && _nav.Territory[ax, az])
                floor = Mathf.Max(floor, _nav.FloorY(ax, az));
            float surface = _nav.SurfaceY;
            float low = floor + (Crawling ? 0.25f : 0.4f);
            float high = Mathf.Max(low, surface - 0.45f);

            switch (State)
            {
                case UndertowState.Asleep:
                case UndertowState.Travel:
                    return floor + 0.3f;
                case UndertowState.Waking:
                    return Mathf.Clamp(floor + 1.0f, low, high);
                case UndertowState.Investigate:
                {
                    // Perto do barulho, sobe até a altura dele (vem por baixo).
                    float y = Flat(_goal - Head).magnitude < 5f ? _goal.y + 0.6f : floor + 0.7f;
                    return Mathf.Clamp(y, low, high);
                }
                case UndertowState.Hunt:
                case UndertowState.Feed:
                {
                    // De perto, sobe até a altura do meio do corpo de quem caça.
                    float y = _target != null ? _target.transform.position.y + 0.6f : surface - 1.2f;
                    if (State == UndertowState.Hunt && _target != null && Flat(_target.transform.position - Head).magnitude > 5f) y = floor + 0.7f;
                    if (State == UndertowState.Feed) y = low;
                    return Mathf.Clamp(y, low, high);
                }
                default:
                    return Mathf.Clamp(floor + 0.7f, low, high);
            }
        }

        void Repath(Vector3 position, float now)
        {
            _repathAt = now + (State == UndertowState.Hunt ? 0.25f : 0.6f);
            if (!_nav.WorldToCell(position, out int cx, out int cz) || !_nav.Territory[cx, cz]) { Arrive($"fora do território em {position}"); return; }

            _nav.Distances(cx, cz, _reach);
            bool inside = _nav.WorldToCell(_goal, out int gx, out int gz) && _reach[gx, gz] >= 0;
            _goalInside = inside;
            if (!inside)
            {
                // O alvo fora do alcance: a célula alcançável mais perto dele.
                float best = float.MaxValue;
                gx = gz = -1;
                for (int z = 0; z < _nav.Data.Height; z++)
                    for (int x = 0; x < _nav.Data.Width; x++)
                    {
                        if (_reach[x, z] < 0) continue;
                        float d = Flat(_nav.CellCenter(x, z) - _goal).sqrMagnitude;
                        if (d < best) { best = d; gx = x; gz = z; }
                    }
                if (gx < 0) { Arrive("nenhuma célula alcançável"); return; }
            }

            _destination = _nav.ClampToCell(gx, gz, _goal, bodyRadius + 0.3f);
            _nav.Path(cx, cz, gx, gz, _path);
            _pathIndex = 0;
            _direct = Clear(position, _destination);
            if (!_direct) Shortcut(position);
            if (Flat(_destination - position).magnitude < 0.35f) Arrive($"já no destino {_destination}");
        }

        void Shortcut(Vector3 position)
        {
            while (_pathIndex + 1 < _path.Count && Clear(position, _path[_pathIndex + 1])) _pathIndex++;
        }

        bool Clear(Vector3 from, Vector3 to)
        {
            if (!_nav.StraightClear(from, to, bodyRadius)) return false;
            Vector3 delta = Flat(to - from);
            float distance = delta.magnitude;
            if (distance < 0.05f) return true;
            Vector3 origin = new Vector3(from.x, Mathf.Max(from.y, _nav.SurfaceY - 1.2f), from.z);
            int count = Physics.SphereCastNonAlloc(origin, bodyRadius * 0.8f, delta / distance, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Hits[i].collider.transform.IsChildOf(_level.transform)) return false;
            return true;
        }

        // ---------------------------------------------------------------- Captura

        /// <summary>
        /// O jogador está onde ela pega? Os pés numa célula do território e água acima da cintura
        /// (nadando, ou de pé no raso na maré alta); no fundo de uma piscina drenada (onde ela se
        /// arrasta), de pé mesmo. No seco, no raso de verdade e na escada fora d'água, nunca.
        /// </summary>
        public bool Catchable(PlayerNetwork player, Vector3 feet)
        {
            if (_nav == null || !_nav.WorldToCell(feet, out int x, out int z) || !_nav.Territory[x, z]) return false;
            float submersion = _nav.SurfaceY - feet.y;
            if (submersion > catchSubmersion) return true;
            return !_nav.Swimmable(x, z) && feet.y < _nav.FloorY(x, z) + 0.5f;
        }

        void TryCatch(float now)
        {
            if (State != UndertowState.Investigate && State != UndertowState.Hunt && State != UndertowState.Circle && State != UndertowState.Return) return;

            Vector3 head = Head;
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                if (_mercy.TryGetValue(player, out float until) && now < until) continue;

                Vector3 feet = player.transform.position;
                if (!Catchable(player, feet)) continue;
                Vector3 middle = feet + Vector3.up * 0.9f;
                if (Vector3.Distance(head, middle) > catchRadius) continue;

                Catches++;
                _mercy[player] = now + feedSeconds + mercySeconds;
                player.GetCaughtRpc();
                _target = player;
                _velocity = Vector3.zero;
                SetState(UndertowState.Feed);
                _stateUntil = now + feedSeconds;
                return;
            }
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
