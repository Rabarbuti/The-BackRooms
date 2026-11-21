using System;
using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using Unity.Netcode;
using UnityEngine;

namespace BackRooms.Entities
{
    public enum EntityState : byte
    {
        /// <summary>Vagando, sem pista de ninguém.</summary>
        Roam,
        /// <summary>Indo até onde ouviu um barulho.</summary>
        Investigate,
        /// <summary>Correndo atrás de um jogador que está vendo.</summary>
        Chase,
        /// <summary>Perdeu o jogador de vista e vasculha por perto.</summary>
        Search,
        /// <summary>Acabou de pegar alguém; fica parada um instante.</summary>
        Feed
    }

    /// <summary>
    /// A entidade do Level 0. Só o host pensa por ela; as outras máquinas recebem a posição
    /// pelo NetworkTransform e o estado por uma NetworkVariable.
    ///
    /// Como ela percebe os jogadores:
    /// • Ouvido — principal. Cada jogador faz barulho conforme anda: agachado quase nada,
    ///   andando médio, correndo muito. O som viaja pelo labirinto (parede barra) e ela vai
    ///   até onde ouviu. O host calcula o barulho pelo deslocamento de cada jogador, então
    ///   vale igual para quem hospeda e para quem entrou.
    /// • Visão — curta de lado, longa para a frente, e só com linha de visada livre. Vendo
    ///   alguém, ela corre.
    ///
    /// O que salva um jogador: ficar parado ou agachado (não faz barulho), entrar num
    /// esconderijo (não é visto nem pego), atravessar um buraco pela faixa (ela não anda em
    /// faixa e tem de dar a volta) ou voltar para a sala inicial (ela não passa na porta).
    /// Quem é pego não morre: volta para a sala inicial, como quem cai num buraco.
    ///
    /// O Capitão do Level 1 (<see cref="CaptainBrain"/>) herda tudo isto e muda três coisas
    /// pelos ganchos: fica parado quando é visto (<see cref="HoldStill"/>), anda no próprio
    /// ritmo (<see cref="SpeedNow"/>) e não ocupa o <see cref="Instance"/> (que é a entidade
    /// do Level 0, lida pela filmadora e pelos testes).
    /// </summary>
    public class EntityBrain : NetworkBehaviour
    {
        /// <summary>A entidade da sessão, em qualquer máquina. Nula enquanto ela não apareceu.</summary>
        public static EntityBrain Instance { get; private set; }

        [Header("Velocidade (m/s). O jogador anda a 2,8 e corre a 5,0.")]
        public float roamSpeed = 1.9f;
        public float searchSpeed = 2.6f;
        public float investigateSpeed = 3.4f;
        public float chaseSpeed = 4.6f;
        public float acceleration = 9f;
        [Tooltip("Caçando, corre isto em segundos e cansa (o jogador corre 6 s e depois anda: sem cansar ela sempre alcançava).")]
        public float chaseBurstSeconds = 7f;
        [Tooltip("Segundos cansada, caçando devagar, antes de correr de novo.")]
        public float chaseTiredSeconds = 5f;
        [Tooltip("Velocidade cansada, em fração de chaseSpeed. 0,5 dá 2,3 m/s: menos que o jogador andando.")]
        public float chaseTiredFactor = 0.5f;
        [Tooltip("Quando nenhum jogador a tem na tela, vagando, vasculhando ou indo atrás de um barulho anda isto vezes mais rápido. Na caça não: fugir continua possível.")]
        public float unseenSpeedFactor = 1.5f;
        [Tooltip("Graus por segundo.")]
        public float turnSpeed = 420f;

        [Header("Sentidos")]
        [Tooltip("Até onde enxerga à frente, em metros.")]
        public float sightRange = 26f;
        [Tooltip("Meio ângulo do campo de visão, em graus. Fora dele não vê nem pega, por mais perto que esteja: de costas para ela, só o barulho a denuncia.")]
        public float sightHalfAngle = 65f;
        [Tooltip("Alcance do ouvido para um jogador correndo, em células de 4 m de caminho. Andando: 25% disso. Agachado: não ouve.")]
        public float hearCells = 8f;
        [Tooltip("Quem está debaixo de uma mesa só some fora do cone de visão ou além desta distância, em metros.")]
        public float tableSightRange = 6f;

        [Header("Captura")]
        public float catchRadius = 1.25f;
        [Tooltip("Tempo parada depois de pegar alguém, em segundos.")]
        public float feedSeconds = 2.5f;
        [Tooltip("Tempo em que ignora o jogador que acabou de pegar, para ele conseguir se afastar.")]
        public float mercySeconds = 8f;

        [Header("Busca")]
        [Tooltip("Tempo vasculhando depois de perder alguém de vista, em segundos.")]
        public float searchSeconds = 8f;

        [Header("Corpo (metros)")]
        public float eyeHeight = 2.9f;
        public float bodyRadius = 0.45f;

        public EntityState State => (EntityState)_state.Value;

        /// <summary>Disparado em todas as máquinas quando o estado muda.</summary>
        public event Action<EntityState> StateChanged;

        /// <summary>Quantas vezes ouviu um jogador (só no host).</summary>
        public int NoisesHeard { get; private set; }

        /// <summary>Quantos jogadores pegou (só no host).</summary>
        public int Catches { get; private set; }

        /// <summary>Para onde está indo (só no host).</summary>
        public Vector3 Goal => _goal;

        const float SenseInterval = 0.15f;

        static readonly RaycastHit[] Hits = new RaycastHit[24];

        sealed class Track
        {
            public Vector3 Last;
            public float LastTime;
            public float Speed;
            public float IgnoreUntil;
        }

        readonly NetworkVariable<byte> _state = new NetworkVariable<byte>();
        readonly Dictionary<PlayerNetwork, Track> _tracks = new Dictionary<PlayerNetwork, Track>();
        readonly List<Vector3> _path = new List<Vector3>();
        readonly List<int> _candidates = new List<int>();

        LevelNav _nav;
        Transform _levelRoot;
        int[,] _reach;
        int[,] _sound;

        Vector3 _goal;
        Vector3 _destination;
        Vector3 _searchCenter;
        Vector3 _velocity;
        bool _hasGoal;
        bool _arrived;
        bool _direct;
        int _pathIndex;
        float _repathAt;
        float _senseAt;
        float _stateUntil;
        float _waitUntil;

        PlayerNetwork _target;
        float _lastSeen;
        float _chaseSince;

        /// <summary>Cansa na caça? O Capitão não: ele manca sempre no mesmo passo.</summary>
        protected virtual bool TiresInChase => true;

        /// <summary>Apressa quando ninguém a vê? O Capitão não: ele já só anda sem ser visto.</summary>
        protected virtual bool HurriesUnseen => true;

        /// <summary>Algum jogador a tem na tela agora, sem parede no meio (só no host, a cada escuta).</summary>
        public bool Watched { get; private set; }

        /// <summary>Falso no Capitão: ele não é "a entidade" que a filmadora e os testes procuram.</summary>
        protected virtual bool IsLevel0Entity => true;

        /// <summary>
        /// Host: a célula serve para ele agora? Fora dela não pisa (o caminho e a rede de segurança
        /// do <see cref="LevelNav"/> a evitam) e não percebe nem pega quem está nela. A entidade e o
        /// Capitão aceitam todas; o Zelador do Level 3 (<see cref="JanitorBrain"/>), só as escuras.
        /// </summary>
        protected virtual bool CellOpen(int x, int z) => true;

        /// <summary>A navegação da fase atual (só no host; nula antes da fase existir).</summary>
        protected LevelNav Nav => _nav;

        // ---------------------------------------------------------------- Ganchos do Ascensorista
        // O inimigo do elevador do Level 3 (AttendantBrain) é cego e chega de elevador. Os padrões
        // abaixo deixam a entidade e o Capitão como eram.

        /// <summary>
        /// Host: verdadeiro neste quadro pula tudo (ouvir, decidir, andar, pegar): quem herda move o
        /// corpo sozinho (o Ascensorista dentro da cabine, saindo ou voltando para ela).
        /// </summary>
        protected virtual bool Scripted(float now) => false;

        /// <summary>Enxerga? Falso no Ascensorista (cego): nunca caça pela visão, só pelo barulho.</summary>
        protected virtual bool HasSight => true;

        /// <summary>Pega só quem está no campo de visão? Falso no Ascensorista: pega quem encosta nele.</summary>
        protected virtual bool CatchNeedsFacing => true;

        /// <summary>
        /// As salinhas acesas do Escritório são abrigo dele? Falso no Ascensorista: para ele só a sala
        /// inicial (a cabine) é abrigo (<see cref="LevelNav.OnlyStartRoomShelter"/>).
        /// </summary>
        protected virtual bool OfficeRoomsShelter => true;

        /// <summary>
        /// Host: quanto sobra de alcance do ouvido para o barulho deste jogador, em células (negativo =
        /// não ouve). <paramref name="soundCells"/> é a distância do som pela grade (-1 = não chega).
        /// </summary>
        protected virtual float HearMargin(PlayerNetwork player, Vector3 at, float speed, int soundCells)
        {
            float loudness = Loudness(speed, player.Crouching);
            // Plástico-bolha estala como corrida, mesmo agachado.
            if (speed >= 0.4f && BubbleWrap.Under(at)) loudness = 1f;
            // Armar um disjuntor, girar uma válvula, puxar um alarme: barulho de corrida, parado.
            if (player.Working) loudness = 1f;
            if (loudness <= 0f || soundCells < 0) return -1f;
            return loudness * hearCells - soundCells;
        }

        /// <summary>Host: o que acontece com quem ele pega. O padrão: queda de sinal e volta ao início.</summary>
        protected virtual void Catch(PlayerNetwork player) => player.GetCaughtRpc();

        /// <summary>Chegou ao destino atual (só no host).</summary>
        protected bool Arrived => _arrived;

        /// <summary>Tem destino (só no host).</summary>
        protected bool HasGoal => _hasGoal;

        /// <summary>Host: vai até <paramref name="goal"/> no estado dado, por até <paramref name="seconds"/>.</summary>
        protected void GoTo(Vector3 goal, EntityState state, float seconds)
        {
            SetGoal(goal);
            SetState(state);
            _stateUntil = Time.time + seconds;
        }

        /// <summary>Host: fica em <paramref name="state"/> sem destino (parado), por exemplo de volta à cabine.</summary>
        protected void Halt(EntityState state)
        {
            _target = null;
            _velocity = Vector3.zero;
            _path.Clear();
            _hasGoal = false;
            _arrived = true;
            SetState(state);
        }

        public override void OnNetworkSpawn()
        {
            if (IsLevel0Entity) Instance = this;
            _state.OnValueChanged += OnStateChanged;
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= OnStateChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Host, a cada quadro, depois de ouvir e decidir: verdadeiro faz o corpo ficar onde está
        /// neste quadro, sem andar, sem virar e sem pegar ninguém. A entidade nunca para.
        /// </summary>
        protected virtual bool HoldStill(float now) => false;

        void OnStateChanged(byte before, byte after)
        {
            StateChanged?.Invoke((EntityState)after);
        }

        /// <summary>Host: manda a entidade conferir um ponto, como se tivesse ouvido algo ali.</summary>
        public void Alert(Vector3 position)
        {
            if (!IsServer) return;

            SetGoal(position);
            SetState(EntityState.Investigate);
            _stateUntil = Time.time + 16f;
        }

        void Update()
        {
            if (!IsSpawned || !IsServer || !EnsureLevel()) return;

            float now = Time.time;

            if (Scripted(now))
            {
                _velocity = Vector3.zero;
                return;
            }

            if (now >= _senseAt)
            {
                _senseAt = now + SenseInterval;
                Sense(now);
            }

            Think(now);

            if (HoldStill(now))
            {
                _velocity = Vector3.zero;
                return;
            }

            Move(Time.deltaTime, now);
            TryCatch(now);
        }

        bool EnsureLevel()
        {
            GameSession session = GameSession.Instance;
            LevelBuilder level = session != null ? session.level : null;
            if (level == null || level.Data == null) return false;

            if (_nav == null || _nav.Data != level.Data)
            {
                // Fase nova: o destino era da fase velha. Na primeira vez não: quem a pôs no
                // mapa já mandou ir a algum lugar (Alert), e esquecer isso a deixava parada.
                if (_nav != null) _hasGoal = false;
                _nav = new LevelNav(level) { Filter = CellOpen, OnlyStartRoomShelter = !OfficeRoomsShelter };
                _reach = _nav.NewMap();
                _sound = _nav.NewMap();
                _levelRoot = level.transform;
            }

            return true;
        }

        // ---------------------------------------------------------------- Sentidos

        void Sense(float now)
        {
            Vector3 position = transform.position;
            if (!_nav.WorldToCell(position, out int cx, out int cz)) return;

            _nav.SoundDistances(cx, cz, Mathf.CeilToInt(hearCells), _sound);

            if (HurriesUnseen) Watched = AnyoneWatching(position);

            PlayerNetwork seen = null;
            float seenDistance = float.MaxValue;
            bool heard = false;
            float heardMargin = 0f;
            Vector3 heardAt = Vector3.zero;

            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;

                Track track = TrackOf(player, now);
                Vector3 at = player.transform.position;

                // Barulho: estimado pelo deslocamento desde a última escuta. Um salto enorme é
                // teletransporte (entrada na fase, volta ao início), não corrida.
                float elapsed = now - track.LastTime;
                if (elapsed > 0.01f)
                {
                    Vector3 moved = at - track.Last;
                    moved.y = 0f;
                    float speed = moved.magnitude / elapsed;
                    track.Speed = speed > 12f ? 0f : speed;
                    track.Last = at;
                    track.LastTime = now;
                }

                if (!Reachable(player, track, now, at, out int px, out int pz)) continue;

                HideKind hiding = player.Hiding;
                bool hidden = HiddenFrom(hiding, position, at);

                // Debaixo da mesa e no cone dele: viu o jogador se enfiar ali (a mesa tapa a
                // visada, então não se confere a linha de visão).
                if (!HasSight) { }
                else if (hiding == HideKind.Table && !hidden)
                {
                    float distance = Flat(at - position).magnitude;
                    if (distance < seenDistance)
                    {
                        seenDistance = distance;
                        seen = player;
                    }
                }
                else if (!hidden && CanSee(position, at, player.Crouching))
                {
                    float distance = Flat(at - position).magnitude;
                    if (distance < seenDistance)
                    {
                        seenDistance = distance;
                        seen = player;
                    }
                }

                float margin = HearMargin(player, at, track.Speed, _sound[px, pz]);
                if (margin < 0f) continue;

                NoisesHeard++;
                if (heard && margin <= heardMargin) continue;

                heard = true;
                heardMargin = margin;
                heardAt = at;
            }

            if (State == EntityState.Feed) return;

            if (seen != null)
            {
                _target = seen;
                _lastSeen = now;
                SetGoal(seen.transform.position);
                SetState(EntityState.Chase);
                return;
            }

            if (State == EntityState.Chase)
            {
                // Meio segundo de tolerância: uma quina não basta para despistar.
                if (now - _lastSeen < 0.6f && _target != null)
                {
                    SetGoal(_target.transform.position);
                    return;
                }

                BeginSearch(_goal, searchSeconds, now);
            }

            if (heard)
            {
                SetGoal(heardAt);
                SetState(EntityState.Investigate);
                _stateUntil = now + 14f;
            }
        }

        /// <summary>
        /// O jogador está escondido dele? Nicho e guarda-roupa escondem sempre. Debaixo da mesa,
        /// só fora do cone de visão (ou além de <see cref="tableSightRange"/>): quem se enfia na
        /// frente dele é visto.
        /// </summary>
        protected bool HiddenFrom(HideKind hiding, Vector3 position, Vector3 playerAt)
        {
            if (hiding == HideKind.None) return false;
            if (hiding != HideKind.Table) return true;

            Vector3 to = Flat(playerAt - position);
            return to.magnitude > tableSightRange || Vector3.Angle(transform.forward, to) > sightHalfAngle;
        }

        /// <summary>
        /// Host: um barulho forte em <paramref name="at"/> (o objetivo cumprido, o alarme disparando).
        /// Vai até lá investigar, se não está caçando nem comendo e se o lugar está ao alcance dele.
        /// </summary>
        public void HearNoise(Vector3 at)
        {
            if (!IsServer || State == EntityState.Chase || State == EntityState.Feed) return;
            Alert(at);
        }

        /// <summary>
        /// Barulho de um jogador, de 0 a 1, pelo jeito de andar: correndo ouve-se de longe
        /// (<see cref="hearCells"/>), andando só de perto (um quarto disso), agachado nada.
        /// </summary>
        public static float Loudness(float speed, bool crouching)
        {
            if (speed < 0.4f || crouching) return 0f;
            return speed > 3.9f ? 1f : 0.25f;
        }

        Track TrackOf(PlayerNetwork player, float now)
        {
            if (_tracks.TryGetValue(player, out Track track)) return track;

            track = new Track { Last = player.transform.position, LastTime = now };
            _tracks[player] = track;
            return track;
        }

        // Jogador que a entidade pode perceber e pegar: fora da sala inicial, sem estar
        // caindo num buraco e fora do tempo de trégua.
        bool Reachable(PlayerNetwork player, Track track, float now, Vector3 at, out int x, out int z)
        {
            x = z = -1;
            if (now < track.IgnoreUntil || at.y < -1f) return false;
            return _nav.WorldToCell(at, out x, out z) && !_nav.IsRefuge(x, z) && CellOpen(x, z);
        }

        static readonly float[] WatchHeights = { 2.6f, 1.5f, 0.5f };

        // Algum jogador tem a cabeça, o peito ou as pernas dela no quadro da câmera, sem parede no meio?
        bool AnyoneWatching(Vector3 position)
        {
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame || player.playerCamera == null) continue;
                if (player.Hiding == HideKind.Wardrobe) continue;

                Camera camera = player.playerCamera;
                Transform eye = camera.transform;
                float tanV = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float tanH = tanV * 16f / 9f;

                foreach (float height in WatchHeights)
                {
                    Vector3 point = position + Vector3.up * height;
                    Vector3 local = eye.InverseTransformPoint(point);
                    if (local.z < 0.3f || local.z > sightRange * 1.5f) continue;
                    if (Mathf.Abs(local.x) > local.z * tanH || Mathf.Abs(local.y) > local.z * tanV) continue;
                    if (!SightBlocked(eye.position, point)) return true;
                }
            }
            return false;
        }

        bool CanSee(Vector3 position, Vector3 playerAt, bool crouching)
        {
            Vector3 to = Flat(playerAt - position);
            float distance = to.magnitude;
            if (distance > sightRange) return false;
            if (!InCone(to)) return false;

            return !SightBlocked(position + Vector3.up * eyeHeight, playerAt + Vector3.up * (crouching ? 0.7f : 1.3f));
        }

        bool SightBlocked(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return false;

            int count = Physics.RaycastNonAlloc(from, delta / distance, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Hits[i].collider.transform.IsChildOf(_levelRoot)) return true;

            return false;
        }

        // ---------------------------------------------------------------- Decisão

        void Think(float now)
        {
            switch (State)
            {
                case EntityState.Feed:
                    if (now >= _stateUntil) BeginRoam();
                    break;

                case EntityState.Investigate:
                    if (now >= _stateUntil) { BeginRoam(); break; }
                    if (!_arrived) break;

                    // Chegou onde ouviu: fica um instante e depois vasculha em volta.
                    if (_waitUntil <= 0f) _waitUntil = now + 1.5f;
                    else if (now >= _waitUntil) BeginSearch(_goal, 5f, now);
                    break;

                case EntityState.Search:
                    if (now >= _stateUntil) BeginRoam();
                    else if (_arrived) PickNear(_searchCenter, 0f, 2.2f);
                    break;

                case EntityState.Roam:
                    if (_arrived || !_hasGoal) PickRoamGoal();
                    break;
            }
        }

        void SetState(EntityState state)
        {
            if (State == state) return;

            if (state == EntityState.Chase) _chaseSince = Time.time;
            _state.Value = (byte)state;
            _waitUntil = 0f;
        }

        void SetGoal(Vector3 goal)
        {
            // Mudança pequena não refaz o caminho antes da hora.
            if (!_hasGoal || Flat(goal - _goal).sqrMagnitude > 1f) _repathAt = 0f;

            _goal = goal;
            _hasGoal = true;
            _arrived = false;
        }

        /// <summary>
        /// Host: esquece o caminho e para (depois de ser posto noutro lugar de uma vez, como na
        /// troca do Capitão com um totem). Volta a vagar a partir de onde está.
        /// </summary>
        protected void ForgetPath()
        {
            _velocity = Vector3.zero;
            _path.Clear();
            _repathAt = 0f;
            BeginRoam();
        }

        void BeginRoam()
        {
            _target = null;
            SetState(EntityState.Roam);
            PickRoamGoal();
        }

        void BeginSearch(Vector3 center, float seconds, float now)
        {
            _target = null;
            _searchCenter = center;
            SetState(EntityState.Search);
            _stateUntil = now + seconds;
            SetGoal(center);
        }

        // Vagando, na maior parte das vezes ela ronda a região de algum jogador (sem saber
        // onde ele está exatamente); no resto, vai para um ponto qualquer do mapa.
        void PickRoamGoal()
        {
            PlayerNetwork lure = null;

            if (UnityEngine.Random.value < 0.4f)
            {
                int seenPlayers = 0;
                foreach (PlayerNetwork player in PlayerNetwork.All)
                {
                    if (player == null || !player.IsSpawned || !player.InGame) continue;
                    if (!_nav.WorldToCell(player.transform.position, out int px, out int pz) || _nav.IsRefuge(px, pz)) continue;

                    seenPlayers++;
                    if (UnityEngine.Random.Range(0, seenPlayers) == 0) lure = player;
                }
            }

            if (lure != null && PickNear(lure.transform.position, 4f, 10f)) return;
            if (PickNear(transform.position, 6f, 1000f)) return;
            PickNear(transform.position, 1f, 1000f);
        }

        // Escolhe ao acaso uma célula alcançável cuja distância ao ponto, em células, esteja na faixa dada.
        bool PickNear(Vector3 center, float minCells, float maxCells)
        {
            if (!_nav.WorldToCell(transform.position, out int cx, out int cz)) return false;

            _nav.Distances(cx, cz, _reach);
            _candidates.Clear();

            LevelData data = _nav.Data;
            for (int z = 0; z < data.Height; z++)
            {
                for (int x = 0; x < data.Width; x++)
                {
                    if (_reach[x, z] < 0) continue;

                    float cells = Flat(_nav.CellCenter(x, z) - center).magnitude / LevelBuilder.Cell;
                    if (cells >= minCells && cells <= maxCells) _candidates.Add(x + z * data.Width);
                }
            }

            if (_candidates.Count == 0)
            {
                _arrived = false;
                return false;
            }

            int pick = _candidates[UnityEngine.Random.Range(0, _candidates.Count)];
            SetGoal(_nav.CellCenter(pick % data.Width, pick / data.Width));
            _repathAt = 0f;
            return true;
        }

        // ---------------------------------------------------------------- Movimento

        float Hurry => HurriesUnseen && !Watched ? unseenSpeedFactor : 1f;

        /// <summary>Velocidade desejada agora, pelo estado. O Capitão muda no apagão.</summary>
        protected virtual float SpeedNow()
        {
            switch (State)
            {
                case EntityState.Chase:
                {
                    if (!TiresInChase || chaseTiredSeconds <= 0f) return chaseSpeed;
                    float into = (Time.time - _chaseSince) % Mathf.Max(0.1f, chaseBurstSeconds + chaseTiredSeconds);
                    return into < chaseBurstSeconds ? chaseSpeed : chaseSpeed * chaseTiredFactor;
                }
                case EntityState.Investigate: return investigateSpeed * Hurry;
                case EntityState.Search: return searchSpeed * Hurry;
                case EntityState.Feed: return 0f;
                default: return roamSpeed * Hurry;
            }
        }

        void Move(float dt, float now)
        {
            Vector3 position = transform.position;
            Vector3 desired = Vector3.zero;

            if (State != EntityState.Feed && _hasGoal)
            {
                if (now >= _repathAt) Repath(position, now);

                if (!_arrived)
                {
                    Vector3 next = _direct || _pathIndex >= _path.Count ? _destination : _path[_pathIndex];
                    Vector3 to = Flat(next - position);

                    if (to.magnitude < 0.3f)
                    {
                        if (!_direct && _pathIndex < _path.Count)
                        {
                            _pathIndex++;
                            Shortcut(position);

                            // Acabaram as células: o último trecho, até o ponto exato, só se estiver livre.
                            if (_pathIndex >= _path.Count && !Clear(position, _destination)) _arrived = true;
                        }
                        else
                        {
                            _arrived = true;
                        }
                    }
                    else
                    {
                        desired = to.normalized * SpeedNow();
                    }
                }
            }

            _velocity = Vector3.MoveTowards(_velocity, desired, acceleration * dt);

            Vector3 moved = position + _velocity * dt;
            moved.y = _levelRoot.position.y;

            // Rede de segurança: nunca dá um passo para cima de um buraco ou para dentro da sala inicial.
            if (_velocity.sqrMagnitude > 0f && !_nav.FloorClear(position, moved, bodyRadius * 0.5f))
            {
                moved = position;
                _velocity = Vector3.zero;
                _repathAt = 0f;
            }

            transform.position = moved;

            Vector3 facing = _velocity.sqrMagnitude > 0.05f ? _velocity
                           : State == EntityState.Chase && _target != null ? Flat(_target.transform.position - moved)
                           : Vector3.zero;

            if (facing.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing, Vector3.up), turnSpeed * dt);
        }

        void Repath(Vector3 position, float now)
        {
            _repathAt = now + (State == EntityState.Chase ? 0.25f : 0.6f);

            if (!_nav.WorldToCell(position, out int cx, out int cz) || !_nav.Standable(cx, cz))
            {
                // Não devia acontecer; se acontecer, volta para a célula alcançável mais próxima.
                Recover(position);
                return;
            }

            _nav.Distances(cx, cz, _reach);

            // O alvo pode estar onde ela não chega (em cima de uma faixa, na sala inicial):
            // vai até a célula alcançável mais próxima dele e, dentro dela, o mais perto possível.
            bool inside = _nav.WorldToCell(_goal, out int gx, out int gz) && _reach[gx, gz] >= 0;
            if (!inside && !_nav.NearestReachable(_goal, _reach, out gx, out gz))
            {
                _arrived = true;
                return;
            }

            _destination = _nav.ClampToCell(gx, gz, _goal, bodyRadius + 0.2f);
            _nav.Path(cx, cz, gx, gz, _path);
            _pathIndex = 0;

            _direct = Clear(position, _destination);
            if (!_direct) Shortcut(position);

            if (Flat(_destination - position).magnitude < 0.3f) _arrived = true;
        }

        // Pula as células do caminho que dá para alcançar em linha reta, para não andar em ângulo reto.
        void Shortcut(Vector3 position)
        {
            while (_pathIndex + 1 < _path.Count && Clear(position, _path[_pathIndex + 1])) _pathIndex++;
        }

        // Dá para ir em linha reta de um ponto ao outro? Chão bom o caminho todo e nenhuma parede.
        bool Clear(Vector3 from, Vector3 to)
        {
            if (!_nav.FloorClear(from, to, bodyRadius)) return false;

            Vector3 delta = Flat(to - from);
            float distance = delta.magnitude;
            if (distance < 0.05f) return true;

            int count = Physics.SphereCastNonAlloc(from + Vector3.up, bodyRadius * 0.85f, delta / distance, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Hits[i].collider.transform.IsChildOf(_levelRoot)) return false;

            return true;
        }

        void Recover(Vector3 position)
        {
            LevelData data = _nav.Data;
            float best = float.MaxValue;
            Vector3 safe = position;

            for (int z = 0; z < data.Height; z++)
            {
                for (int x = 0; x < data.Width; x++)
                {
                    if (!_nav.Standable(x, z)) continue;

                    Vector3 center = _nav.CellCenter(x, z);
                    float distance = Flat(center - position).sqrMagnitude;
                    if (distance >= best) continue;

                    best = distance;
                    safe = center;
                }
            }

            transform.position = safe;
            _velocity = Vector3.zero;
            _repathAt = 0f;
        }

        // ---------------------------------------------------------------- Captura

        void TryCatch(float now)
        {
            if (State == EntityState.Feed) return;

            Vector3 position = transform.position;

            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;

                Track track = TrackOf(player, now);
                Vector3 at = player.transform.position;
                HideKind hiding = player.Hiding;
                if (HiddenFrom(hiding, position, at)) continue;

                if (!Reachable(player, track, now, at, out _, out _)) continue;
                if (Flat(at - position).magnitude > catchRadius) continue;
                // Fora do campo de visão não pega: encostar nas costas dela não é morte.
                if (CatchNeedsFacing && !InCone(Flat(at - position))) continue;
                // Debaixo da mesa, o tampo tapa a visada: quem foi visto se enfiando ali é pego assim mesmo.
                if (hiding != HideKind.Table &&
                    SightBlocked(position + Vector3.up * 1.4f, at + Vector3.up * (player.Crouching ? 0.7f : 1.2f))) continue;

                Catches++;
                track.IgnoreUntil = now + feedSeconds + mercySeconds;
                Catch(player);

                _target = null;
                _velocity = Vector3.zero;
                SetState(EntityState.Feed);
                _stateUntil = now + feedSeconds;
                return;
            }
        }

        bool InCone(Vector3 flatTo) => Vector3.Angle(transform.forward, flatTo) <= sightHalfAngle;

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
