using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O Zelador (The Janitor), o inimigo do escuro do Level 3. Herda da entidade do Level 0 a
    /// visão, o ouvido, o caminho e a captura, e muda o principal: <b>ele só existe no escuro</b>.
    ///
    /// • Só pisa em célula escura (<see cref="JanitorDark"/>: luminária queimada, apagada pela onda
    ///   de luzes ou sem energia depois do alarme). Não percebe nem pega quem está numa célula acesa,
    ///   por mais perto que esteja: a luz salva dele (o silêncio salva do outro, o do elevador).
    /// • Aparece (<see cref="Present"/>) dentro de um trecho escuro, longe de todos e fora da vista,
    ///   e vai na direção do jogador mais perto, pelo escuro. Quando a luz volta sobre ele, some no
    ///   chão; volta noutro escuro depois de um tempo. Rondando longe de todos por muito tempo, também
    ///   some e reaparece mais perto.
    /// • A lanterna não o detém: o facho nele o faz hesitar um instante (<see cref="hesitateSeconds"/>),
    ///   no máximo uma vez a cada <see cref="hesitateCooldown"/>.
    /// • Enxerga bem no escuro e ouve mal (<see cref="EntityBrain.hearCells"/> curto): o barulho é a
    ///   arma do outro inimigo. Ouve o mundo (<see cref="INoiseListener"/>) só se o barulho cai num
    ///   escuro que ele alcança.
    ///
    /// Só o host decide; <see cref="Present"/> vai pela rede (o corpo e o som somem com ele).
    /// </summary>
    public sealed partial class JanitorBrain : EntityBrain, INoiseListener
    {
        /// <summary>O Zelador da sessão, em qualquer máquina. Nulo enquanto não foi posto.</summary>
        public static JanitorBrain Janitor { get; private set; }

        [Header("Zelador: aparecer e sumir")]
        [Tooltip("Aparece a pelo menos esta distância de caminhada de todo jogador, em células de 4 m.")]
        public int emergeMinCells = 4;
        [Tooltip("Segundos escondido, depois de sumir, antes de poder aparecer de novo.")]
        public float emergeCooldown = 5f;
        [Tooltip("Rondando sem ninguém a menos disto (células em linha reta)...")]
        public float lingerCells = 7f;
        [Tooltip("...por mais que isto (s), some e reaparece mais perto.")]
        public float lingerSeconds = 25f;

        [Header("Zelador: lanterna")]
        [Tooltip("Segundos parado quando o facho de uma lanterna o pega.")]
        public float hesitateSeconds = 0.6f;
        [Tooltip("Intervalo mínimo entre duas hesitações, em segundos.")]
        public float hesitateCooldown = 4f;

        /// <summary>Para os testes: verdadeiro impede que apareça sozinho (só por <see cref="ManifestAt"/>).</summary>
        public static bool HoldEmerge;

        /// <summary>Para o teste dele: continua valendo com os outros inimigos pausados (<see cref="EntityDirector.Paused"/>).</summary>
        public static bool RunWhilePaused;

        /// <summary>Está no mapa (no escuro)? Falso: escondido, sem corpo, sem som, não pega ninguém.</summary>
        public bool Present => _present.Value;

        /// <summary>Hesitando agora pelo facho de uma lanterna (vai pela rede, para o corpo).</summary>
        public bool Flinching => _flinch.Value;

        /// <summary>Contadores do host, para os testes.</summary>
        public int Emerges { get; private set; }
        public int Vanishes { get; private set; }
        public int Hesitations { get; private set; }
        public string LastVanish { get; private set; } = "";

        const float EmergeInterval = 0.5f;
        const float SinkSeconds = 0.9f;
        const float RiseSeconds = 1.0f;
        static readonly float[] BodyHeights = { 2.0f, 1.3f, 0.6f };
        static readonly RaycastHit[] Hits = new RaycastHit[24];

        readonly NetworkVariable<bool> _present = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _flinch = new NetworkVariable<bool>();
        readonly List<int> _candidates = new List<int>();
        readonly List<int[,]> _playerMaps = new List<int[,]>();
        readonly List<PlayerNetwork> _players = new List<PlayerNetwork>();

        LevelNav _free;
        float _nextEmerge;
        float _lonelySince = -1f;
        float _hesitateUntil;
        float _hesitateReady;
        float _parkAt = -1f;
        float _risenAt;

        protected override bool IsLevel0Entity => false;

        protected override bool CellOpen(int x, int z) => JanitorDark.IsDark(x, z);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Janitor = this;
            WorldNoise.Register(this);
            WavesSpawn();
        }

        public override void OnNetworkDespawn()
        {
            WorldNoise.Unregister(this);
            WavesDespawn();
            base.OnNetworkDespawn();
            if (Janitor == this) Janitor = null;
        }

        /// <summary>Onde ele fica escondido: longe do mapa (os sentidos e o caminho não funcionam lá).</summary>
        public static Vector3 ParkPosition(LevelBuilder level)
        {
            Vector3 origin = level != null ? level.transform.position : Vector3.zero;
            return origin + new Vector3(-400f, 0f, -400f);
        }

        // ---------------------------------------------------------------- A cada quadro (host)

        protected override bool HoldStill(float now)
        {
            GameSession session = GameSession.Instance;
            LevelBuilder level = session != null ? session.level : null;
            if (level == null || !JanitorDark.Bind(level)) return true;

            // Os testes que pausam os inimigos (EntityDirector.Paused) pausam ele também; o teste dele não.
            bool paused = EntityDirector.Paused && !RunWhilePaused;

            // A onda persegue: o host escolhe os trechos; o que acabou de apagar o chama.
            PlanWaves(level, now, paused);
            if (HandlePendingWave(level, now, paused)) return true;

            if (!Present)
            {
                if (_flinch.Value) _flinch.Value = false;
                if (_parkAt > 0f && now >= _parkAt)
                {
                    _parkAt = -1f;
                    Teleport(ParkPosition(level), transform.rotation);
                }
                if (!paused && !HoldEmerge && now >= _nextEmerge)
                {
                    _nextEmerge = now + EmergeInterval;
                    TryEmerge(level, now);
                }
                return true;
            }

            if (paused)
            {
                Vanish(level, now, "pausado");
                return true;
            }

            // A luz voltou sobre ele: some no chão.
            if (!JanitorDark.Cell(transform.position, out int cx, out int cz) || !JanitorDark.IsDark(cx, cz))
            {
                Vanish(level, now, "a luz voltou");
                return true;
            }

            // Rondando longe de todos por muito tempo: some e volta mais perto.
            if (State == EntityState.Roam && NearestPlayer(out _) > lingerCells * LevelBuilder.Cell)
            {
                if (_lonelySince < 0f) _lonelySince = now;
                else if (now - _lonelySince > lingerSeconds)
                {
                    Vanish(level, now, "longe de todos");
                    _nextEmerge = now + 1f;
                    return true;
                }
            }
            else _lonelySince = -1f;

            // O facho de uma lanterna: hesita um instante.
            if (now >= _hesitateReady && InTorchBeam(level.transform))
            {
                _hesitateUntil = now + hesitateSeconds;
                _hesitateReady = now + hesitateCooldown;
                Hesitations++;
            }
            // Subindo do chão: ainda não anda nem pega (o gorgolejo avisa).
            bool flinching = now < _hesitateUntil;
            if (now < _risenAt) return true;
            if (_flinch.Value != flinching) _flinch.Value = flinching;
            return flinching;
        }

        float NearestPlayer(out PlayerNetwork nearest)
        {
            nearest = null;
            float best = float.MaxValue;
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                Vector3 to = player.transform.position - transform.position;
                to.y = 0f;
                if (to.magnitude >= best) continue;
                best = to.magnitude;
                nearest = player;
            }
            return best;
        }

        // ---------------------------------------------------------------- Aparecer e sumir

        void Vanish(LevelBuilder level, float now, string why)
        {
            _present.Value = false;
            _flinch.Value = false;
            Vanishes++;
            LastVanish = why;
            _lonelySince = -1f;
            _nextEmerge = Mathf.Max(_nextEmerge, now + emergeCooldown);
            // Sai do mapa depois de afundar (o corpo leva sinkSeconds descendo no lugar).
            _parkAt = now + SinkSeconds;
        }

        /// <summary>
        /// Host: põe o Zelador num ponto escolhido (para os testes), olhando para
        /// <paramref name="facing"/>, sem conferir nada além de estar no escuro.
        /// </summary>
        public void ManifestAt(Vector3 position, Vector3 facing)
        {
            if (!IsServer) return;
            facing.y = 0f;
            Quaternion rotation = Quaternion.LookRotation(facing.sqrMagnitude > 0.01f ? facing : Vector3.forward, Vector3.up);
            _parkAt = -1f;
            _risenAt = Time.time + RiseSeconds;
            Teleport(position, rotation);
            ForgetPath();
            _present.Value = true;
            _lonelySince = -1f;
            Emerges++;
        }

        // Escolhe a célula escura mais perto de algum jogador entre as que ficam longe (andando) de
        // todos e fora da vista de todos. Aparece lá e vai para onde estava o mais perto.
        void TryEmerge(LevelBuilder level, float now)
        {
            LevelData data = level.Data;
            if (_free == null || _free.Data != data) _free = new LevelNav(level);

            _players.Clear();
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.InGame) _players.Add(player);
            if (_players.Count == 0) return;

            while (_playerMaps.Count < _players.Count) _playerMaps.Add(_free.NewMap());
            for (int p = 0; p < _players.Count; p++)
            {
                int[,] map = _playerMaps[p];
                if (_free.WorldToCell(_players[p].transform.position, out int px, out int pz) && WalkFrom(px, pz, map)) continue;
                for (int z = 0; z < data.Height; z++)
                    for (int x = 0; x < data.Width; x++) map[x, z] = -1;
            }

            int bestCell = -1;
            float bestScore = float.MaxValue;
            for (int z = 0; z < data.Height; z++)
            {
                for (int x = 0; x < data.Width; x++)
                {
                    if (!JanitorDark.IsDark(x, z) || data.IsShelter(x, z) || data.Pit[x, z] || data.IsBarricade(x, z)) continue;

                    Vector3 center = _free.CellCenter(x, z);
                    float nearest = float.MaxValue;
                    bool ok = true;
                    for (int p = 0; p < _players.Count && ok; p++)
                    {
                        Vector3 at = _players[p].transform.position;
                        int steps = _playerMaps[p][x, z];
                        float straight = Vector3.Distance(Flat(center), Flat(at)) / LevelBuilder.Cell;
                        float cells = steps >= 0 ? steps : straight;
                        if (cells < emergeMinCells || straight < emergeMinCells * 0.75f) ok = false;
                        else if (Seen(_players[p], center, level.transform)) ok = false;
                        nearest = Mathf.Min(nearest, cells);
                    }
                    if (!ok) continue;

                    // O mais perto de alguém (sem passar do mínimo); empate, ao acaso.
                    float score = nearest + Random.value * 0.5f;
                    if (score >= bestScore) continue;
                    bestScore = score;
                    bestCell = x + z * data.Width;
                }
            }
            if (bestCell < 0) return;

            Vector3 spot = _free.CellCenter(bestCell % data.Width, bestCell / data.Width);
            spot.y = level.transform.position.y;
            NearestFrom(spot, out PlayerNetwork lure);
            Vector3 look = lure != null ? lure.transform.position - spot : Vector3.forward;
            ManifestAt(spot, look);
            LastEmergeKind = "no escuro";
            LastEmergeCells = Mathf.RoundToInt(bestScore);
            if (lure != null) Alert(lure.transform.position);
        }

        void NearestFrom(Vector3 spot, out PlayerNetwork nearest)
        {
            nearest = null;
            float best = float.MaxValue;
            foreach (PlayerNetwork player in _players)
            {
                float d = Vector3.Distance(Flat(player.transform.position), Flat(spot));
                if (d >= best) continue;
                best = d;
                nearest = player;
            }
        }

        // Distância a pé (qualquer célula com piso; abrigo e porta valem) de uma célula a todas.
        bool WalkFrom(int fromX, int fromZ, int[,] map)
        {
            LevelData data = _free.Data;
            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++) map[x, z] = -1;
            var queue = new Queue<int>();
            map[fromX, fromZ] = 0;
            queue.Enqueue(fromX + fromZ * data.Width);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int x = c % data.Width, z = c / data.Width;
                for (int s = 0; s < 4; s++)
                {
                    WallSide side = (WallSide)s;
                    if (!data.CanWalk(x, z, side, false) || !data.Neighbor(x, z, side, out int nx, out int nz)) continue;
                    if (map[nx, nz] >= 0) continue;
                    map[nx, nz] = map[x, z] + 1;
                    queue.Enqueue(nx + nz * data.Width);
                }
            }
            return true;
        }

        // O jogador veria o ponto (o corpo de pé numa célula) agora: na frente da câmera e sem parede?
        static bool Seen(PlayerNetwork viewer, Vector3 foot, Transform levelRoot)
        {
            Camera camera = viewer.playerCamera;
            Vector3 eye = camera != null ? camera.transform.position : viewer.transform.position + Vector3.up * 1.6f;
            foreach (float height in BodyHeights)
                if (!Blocked(eye, foot + Vector3.up * height, levelRoot)) return true;
            return false;
        }

        static bool Blocked(Vector3 from, Vector3 to, Transform levelRoot)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return false;
            int count = Physics.RaycastNonAlloc(from, delta / distance, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Hits[i].collider.transform.IsChildOf(levelRoot)) return true;
            return false;
        }

        void Teleport(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (TryGetComponent(out NetworkTransform moved))
                moved.Teleport(position, rotation, transform.localScale);
        }

        // ---------------------------------------------------------------- Lanterna

        // O facho de alguma lanterna acesa o pega (cabeça, peito ou joelho), sem parede no meio?
        bool InTorchBeam(Transform levelRoot)
        {
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                Flashlight torch = player.flashlight;
                if (torch == null || !torch.IsOn || torch.Beam == null) continue;

                Light beam = torch.Beam;
                Vector3 from = beam.transform.position;
                foreach (float height in BodyHeights)
                {
                    Vector3 point = transform.position + Vector3.up * height;
                    Vector3 to = point - from;
                    if (to.magnitude > beam.range) continue;
                    if (beam.type == LightType.Spot && Vector3.Angle(beam.transform.forward, to) > beam.spotAngle * 0.5f) continue;
                    if (!Blocked(from, point, levelRoot)) return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- Ouvido do mundo

        public void HearWorldNoise(Vector3 at, float radius)
        {
            if (!IsServer || !IsSpawned || !Present) return;
            if (Vector3.Distance(at, transform.position) > radius) return;
            // Só vai se o barulho caiu num escuro (ele não sai da luz para ver).
            if (!JanitorDark.IsDarkAt(at)) return;
            HearNoise(at);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
