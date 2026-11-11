using System;
using System.Collections.Generic;
using BackRooms.Net;
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
    /// </summary>
    public sealed class EntityBrain : NetworkBehaviour
    {
        /// <summary>A entidade da sessão, em qualquer máquina. Nula enquanto ela não apareceu.</summary>
        public static EntityBrain Instance { get; private set; }

        [Header("Velocidade (m/s). O jogador anda a 2,8 e corre a 5,0.")]
        public float roamSpeed = 1.9f;
        public float searchSpeed = 2.6f;
        public float investigateSpeed = 3.4f;
        public float chaseSpeed = 4.6f;
        public float acceleration = 9f;
        [Tooltip("Graus por segundo.")]
        public float turnSpeed = 420f;

        [Header("Sentidos")]
        [Tooltip("Até onde enxerga à frente, em metros.")]
        public float sightRange = 26f;
        [Tooltip("Meio ângulo do campo de visão, em graus.")]
        public float sightHalfAngle = 65f;
        [Tooltip("Dentro desta distância percebe o jogador mesmo de costas (se não houver parede).")]
        public float closeSense = 3.5f;
        [Tooltip("Alcance do ouvido para um jogador correndo, em células de 4 m de caminho. Andando: 55% disso. Agachado: 25%.")]
        public float hearCells = 8f;

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
        public float eyeHeight = 2.5f;
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

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _state.OnValueChanged += OnStateChanged;
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= OnStateChanged;
            if (Instance == this) Instance = null;
        }

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

            if (now >= _senseAt)
            {
                _senseAt = now + SenseInterval;
                Sense(now);
            }

            Think(now);
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
                _nav = new LevelNav(level);
                _reach = _nav.NewMap();
                _sound = _nav.NewMap();
                _levelRoot = level.transform;
                _hasGoal = false;
            }

            return true;
        }

        // ---------------------------------------------------------------- Sentidos

        void Sense(float now)
        {
            Vector3 position = transform.position;
            if (!_nav.WorldToCell(position, out int cx, out int cz)) return;

            _nav.SoundDistances(cx, cz, Mathf.CeilToInt(hearCells), _sound);

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

                bool hidden = player.Hidden;

                if (!hidden && CanSee(position, at, player.Crouching))
                {
                    float distance = Flat(at - position).magnitude;
                    if (distance < seenDistance)
                    {
                        seenDistance = distance;
                        seen = player;
                    }
                }

                float loudness = Loudness(track.Speed, player.Crouching);
                if (loudness <= 0f || _sound[px, pz] < 0) continue;

                float margin = loudness * hearCells - _sound[px, pz];
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

        /// <summary>Barulho de um jogador, de 0 a 1, pelo jeito de andar.</summary>
        public static float Loudness(float speed, bool crouching)
        {
            if (speed < 0.4f) return 0f;
            if (crouching) return 0.25f;
            return speed > 3.9f ? 1f : 0.55f;
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
            return _nav.WorldToCell(at, out x, out z) && !_nav.Data.InRoom(x, z);
        }

        bool CanSee(Vector3 position, Vector3 playerAt, bool crouching)
        {
            Vector3 to = Flat(playerAt - position);
            float distance = to.magnitude;
            if (distance > sightRange) return false;
            if (distance > closeSense && Vector3.Angle(transform.forward, to) > sightHalfAngle) return false;

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

            if (UnityEngine.Random.value < 0.65f)
            {
                int seenPlayers = 0;
                foreach (PlayerNetwork player in PlayerNetwork.All)
                {
                    if (player == null || !player.IsSpawned || !player.InGame) continue;
                    if (!_nav.WorldToCell(player.transform.position, out int px, out int pz) || _nav.Data.InRoom(px, pz)) continue;

                    seenPlayers++;
                    if (UnityEngine.Random.Range(0, seenPlayers) == 0) lure = player;
                }
            }

            if (lure != null && PickNear(lure.transform.position, 3f, 8f)) return;
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

        float SpeedNow()
        {
            switch (State)
            {
                case EntityState.Chase: return chaseSpeed;
                case EntityState.Investigate: return investigateSpeed;
                case EntityState.Search: return searchSpeed;
                case EntityState.Feed: return 0f;
                default: return roamSpeed;
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
                if (player == null || !player.IsSpawned || !player.InGame || player.Hidden) continue;

                Track track = TrackOf(player, now);
                Vector3 at = player.transform.position;

                if (!Reachable(player, track, now, at, out _, out _)) continue;
                if (Flat(at - position).magnitude > catchRadius) continue;
                if (SightBlocked(position + Vector3.up * 1.4f, at + Vector3.up * (player.Crouching ? 0.7f : 1.2f))) continue;

                Catches++;
                track.IgnoreUntil = now + feedSeconds + mercySeconds;
                player.GetCaughtRpc();

                _target = null;
                _velocity = Vector3.zero;
                SetState(EntityState.Feed);
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
