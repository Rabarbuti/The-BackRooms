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
        /// <summary>Parado no fundo de uma água funda (qualquer uma, não só o covil). Ouve, mas leva um instante para acordar.</summary>
        Asleep,
        /// <summary>Acordando ou surgindo de um cano porque ouviu alguém: o rugido e as bolhas. Parado.</summary>
        Waking,
        /// <summary>Indo até onde ouviu barulho, pelo fundo.</summary>
        Investigate,
        /// <summary>Ouviu de perto e há pouco: arranca, subindo para quem se mexe.</summary>
        Hunt,
        /// <summary>Quem ele caçava saiu da água: ronda a borda por onde ele saiu.</summary>
        Circle,
        /// <summary>Voltando para a água funda (sem ouvir nada).</summary>
        Return,
        /// <summary>Nos canos, de uma água para outra: afunda, some e reaparece noutra água.</summary>
        Travel,
        /// <summary>Pegou alguém: afunda com ele.</summary>
        Feed,
        /// <summary>Rondando sem barulho: na maré alta, em volta dos jogadores, a uma distância; na baixa, mudando de lugar na mesma água.</summary>
        Prowl,
        /// <summary>Surgindo de um ralo sem ter ouvido nada (mudou de água pelo relógio): bolhas e um gorgolejo, sem rugido.</summary>
        Emerge
    }

    /// <summary>
    /// O Undertow, o inimigo da água das Piscinas (Level 2). Só o host pensa por ele; as outras
    /// máquinas recebem a posição pelo NetworkTransform, o estado por uma NetworkVariable e os
    /// rugidos por um contador. Não herda do <see cref="EntityBrain"/>: não anda no chão nem enxerga.
    ///
    /// As regras, em linguagem de jogador (desde 21/11/2026):
    /// • <b>Maré baixa:</b> vive em toda água em que dá para afundar (piscinas, raias, canais, poços)
    ///   e nunca sai dela; o raso e o seco são abrigo. Não tem casa: de tempos em tempos (16 a 30 s
    ///   sem ouvir nada) muda de água pelos canos, de preferência para uma a ~14 m de algum jogador,
    ///   nunca a menos de 10 m de ninguém nem onde alguém está nadando. Ao surgir, bolhas e um
    ///   gorgolejo do ralo, sem rugido: o jogador nunca sabe em qual água ele está.
    /// • <b>Ouve o barulho na água</b> (passos, braçadas e espirros do <see cref="Footsteps"/>):
    ///   na mesma água, até força × 24 m; de outra água, pelos canos, só barulho forte (espirro,
    ///   correr chapinhando): surge lá com o rugido.
    /// • <b>Maré alta:</b> com o seco alagado (1,12 m de água) ele persegue pelo mapa inteiro, menos
    ///   a sala inicial: ruge quando a água chega, ronda os jogadores a 8–18 m (nunca a menos de 7 m)
    ///   e vem pelo barulho. Fora da água funda é mais lento (vem a 1,6, caça a 2,2; quem vadeia anda
    ///   a 1,54), com um bote de 3 m/s nos últimos 2,5 m. Quando a maré começa a descer, ele some
    ///   pelos ralos e volta a uma água funda.
    /// • <b>Pega só quem ele ouve:</b> quem ele ouviu nos últimos 2,5 s (ou quem ele esbarra, a
    ///   0,6 m). Parado e quieto, ele perde a pista: chega perto de onde ouviu, para a 1,3 m,
    ///   escuta 4 s e vai embora. Mexer-se perto dele é o que mata.
    /// • Avisos: o rugido (ao ouvir e acordar, ao dar o bote e quando a maré o solta; ouve-se de
    ///   longe, abafado debaixo d'água), um rosnado que cresce com a proximidade, a sombra e as
    ///   bolhas.
    /// • Pega quem está com água acima da cintura no território dele e puxa para o fundo (NO
    ///   SIGNAL e volta ao início). Quem sai da água (escada ou borda) escapa: ele ronda a borda.
    /// • Drenada a fase, arrasta-se pelo fundo das piscinas vazias, devagar.
    /// • O corpo inteiro (não só o meio) fica fora das paredes, escadas e colunas: cada passo e
    ///   cada giro é conferido (<see cref="Fits"/>); a altura segue o piso medido por raio sob o
    ///   corpo todo, e ele não avança para onde não cabe.
    /// • Passa por todo vão que o <see cref="UndertowNav"/> dá como aberto (desde 21/11/2026, à
    ///   noite): nada para onde aponta, nunca de lado; o caminho vai pelo eixo de cada passagem (meio
    ///   da célula, meio do vão, meio da célula seguinte), ele vira só no meio das células, freando
    ///   antes, e cruza baixo; no aperto, os braços se colam ao corpo (<see cref="SqueezeProbes"/>).
    ///   Travado, recomeça pelo eixo; só depois de 4 travadas seguidas no mesmo lugar some pelo ralo.
    /// </summary>
    public sealed class UndertowBrain : NetworkBehaviour, INoiseListener
    {
        /// <summary>O Undertow da sessão, em qualquer máquina. Nulo enquanto não apareceu.</summary>
        public static UndertowBrain Instance { get; private set; }

        /// <summary>O nome que o jogador lê (a língua do jogo pode mudar: aqui).</summary>
        public const string DisplayName = "THE UNDERTOW";

        /// <summary>Para os testes: sem mudar de água pelo relógio (as etapas antigas esperam ele parado).</summary>
        public static bool RoamPaused;

        [Header("Velocidade na água funda (m/s). O jogador nada a 1,26.")]
        public float returnSpeed = 1.8f;
        public float investigateSpeed = 2.9f;
        [Tooltip("Ouviu alguém há pouco e de perto: arranca.")]
        public float huntSpeed = 4f;
        [Tooltip("Arrastando-se no fundo de uma piscina drenada (o jogador anda a 2,8).")]
        public float crawlSpeed = 0.9f;
        public float acceleration = 5f;
        public float verticalSpeed = 2.2f;
        [Tooltip("Graus por segundo. Giro suave: o corpo tem 3,4 m e não pode varrer parede.")]
        public float turnSpeed = 150f;

        [Header("Maré alta, fora da água funda (o jogador vadeia o seco alagado a 1,54 m/s): é quando ele é mais perigoso")]
        public float floodInvestigateSpeed = 2.6f;
        public float floodHuntSpeed = 3.4f;
        [Tooltip("O bote, nos últimos metros, ouvindo a presa agora.")]
        public float lungeSpeed = 4.4f;
        public float lungeRange = 2.5f;
        public float prowlSpeed = 2f;
        [Tooltip("Rondando: vai a um ponto a esta distância (m) de um jogador...")]
        public float prowlNear = 8f;
        public float prowlFar = 18f;
        [Tooltip("...nunca a menos disto de nenhum.")]
        public float prowlKeepAway = 7f;

        [Header("Ouvido")]
        [Tooltip("Alcance, em metros de água, de um barulho de força 1 (um espirro). Braçada à tona ~0,47 (11 m); vadeando andando 0,55 (13 m); agachado 0,25 (6 m); submersa ~0,12 (3 m).")]
        public float hearRange = 24f;
        [Tooltip("Barulho mínimo que se ouve de outra água, pelos canos (o espirro de quem pula, correr chapinhando).")]
        public float pipeLoudness = 0.6f;
        [Tooltip("Ouviu alguém a menos disto, há menos de huntMemory s: caça (mais rápido).")]
        public float huntRange = 9f;
        public float huntMemory = 1.6f;
        [Tooltip("Quem fez barulho há menos disto e sai da água (escada, borda): ele vai rondar a borda por onde saiu.")]
        public float circleMemory = 8f;
        [Tooltip("Segundos indo até um barulho antes de desistir (sem ouvir mais nada).")]
        public float investigateSeconds = 12f;
        [Tooltip("Segundos parado perto do ponto do barulho (contados da chegada) antes de ir embora.")]
        public float lingerSeconds = 4f;
        [Tooltip("Só pega quem ouviu há menos disto (s): parado e quieto, ele perde a pista.")]
        public float contactSeconds = 2f;
        [Tooltip("Sem ouvir de novo, para com a cabeça a esta distância (m) de onde ouviu.")]
        public float stopShort = 1.6f;
        [Tooltip("Esbarrou (cabeça a menos disto do meio do corpo): pega mesmo sem ouvir.")]
        public float bumpRadius = 0.6f;

        [Header("Presença na maré baixa")]
        [Tooltip("Parado sem ouvir nada, muda de água depois de um tempo entre estes (s).")]
        public float roamMinSeconds = 16f;
        public float roamMaxSeconds = 30f;
        [Tooltip("Chance de escolher uma água perto de um jogador (o resto, qualquer uma).")]
        [Range(0f, 1f)] public float roamNearPlayer = 0.65f;
        [Tooltip("Nunca surge a menos disto (m) de um jogador.")]
        public float roamMinDistance = 10f;
        [Tooltip("Perto de um jogador quer dizer a esta distância (m).")]
        public float roamPreferred = 14f;

        [Header("Tempos (s)")]
        [Tooltip("Acordando ou surgindo de um cano por um barulho: parado, subindo, com o rugido.")]
        public float wakeSeconds = 2.2f;
        [Tooltip("Surgindo de um ralo sem barulho (mudou de água): bolhas e gorgolejo.")]
        public float emergeSeconds = 1.6f;
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
        [Tooltip("Intervalo mínimo entre dois rugidos.")]
        public float roarCooldown = 5f;

        [Header("Captura")]
        [Tooltip("Distância da cabeça dele ao meio do corpo do jogador, em metros.")]
        public float catchRadius = 1.4f;
        [Tooltip("Água acima dos pés do jogador a partir da qual ele pode ser pego (acima da cintura). O seco alagado tem 1,12 m.")]
        public float catchSubmersion = 0.9f;

        [Header("Corpo (m)")]
        [Tooltip("Da origem (meio do corpo) até a cabeça, para a frente.")]
        public float headForward = 1.25f;
        public float bodyRadius = 0.45f;

        public UndertowState State => (UndertowState)_state.Value;

        /// <summary>Disparado em todas as máquinas quando o estado muda.</summary>
        public event Action<UndertowState> StateChanged;

        /// <summary>Disparado em todas as máquinas a cada rugido.</summary>
        public event Action Roared;

        /// <summary>Quantos barulhos ouviu (só no host).</summary>
        public int NoisesHeard { get; private set; }
        /// <summary>Quantos jogadores pegou (só no host).</summary>
        public int Catches { get; private set; }
        /// <summary>Quantas vezes atravessou os canos (só no host).</summary>
        public int Travels { get; private set; }
        /// <summary>Quantas vezes mudou de lugar pelo relógio, sem barulho (só no host).</summary>
        public int Roams { get; private set; }
        /// <summary>Quantos rugidos (em qualquer máquina, pela rede).</summary>
        public int RoarCount => _roars.Value;
        /// <summary>Quantas vezes um passo ou giro foi recusado por bater o corpo na fase (só no host).</summary>
        public int Blocked { get; private set; }
        /// <summary>Quantas vezes travou e mudou de rumo (só no host).</summary>
        public int Stucks { get; private set; }
        /// <summary>Para onde vai (só no host).</summary>
        public Vector3 Goal => _goal;
        /// <summary>Onde apareceu (só no host). Não é mais a casa dele: ele muda de água.</summary>
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
        static readonly Collider[] Overlaps = new Collider[16];

        /// <summary>
        /// Os pontos do corpo conferidos contra a fase a cada passo (no espaço dele: X para a direita,
        /// Z para a frente; W = raio): a espinha da ponta do focinho à ponta da cauda e as mãos com os
        /// braços recolhidos ao longo do corpo (o <see cref="UndertowBody"/> recolhe os braços onde
        /// eles abertos bateriam).
        /// </summary>
        public static readonly Vector4[] BodyProbes =
        {
            new Vector4(0f, 0f, 1.45f, 0.09f), new Vector4(0f, 0f, 1.2f, 0.12f), new Vector4(0f, 0f, 0.85f, 0.15f),
            new Vector4(0f, 0f, 0.45f, 0.16f), new Vector4(0f, 0f, 0f, 0.16f), new Vector4(0f, 0f, -0.4f, 0.14f),
            new Vector4(0f, 0f, -0.8f, 0.11f), new Vector4(0f, 0f, -1.2f, 0.09f), new Vector4(0f, 0f, -1.6f, 0.07f),
            new Vector4(0f, 0f, -1.9f, 0.05f),
            // Braços recolhidos: cotovelos e mãos ao longo do corpo.
            new Vector4(0.4f, 0f, 0.15f, 0.06f), new Vector4(-0.4f, 0f, 0.15f, 0.06f),
            new Vector4(0.58f, 0f, -0.8f, 0.06f), new Vector4(-0.58f, 0f, -0.8f, 0.06f),
        };

        /// <summary>
        /// O corpo espremido, conferido quando o de <see cref="BodyProbes"/> não cabe (um vão de
        /// porta, uma coluna perto da divisa, uma curva num corredor): os braços colados aos flancos
        /// (ombro, cotovelo, pulso e ponta dos dedos a até 0,38 m do meio; o <see cref="UndertowBody"/>
        /// os cola de verdade onde não cabem recolhidos) e a cauda só até 1,3 m atrás (a ponta se curva
        /// para o lado livre). Largura total 0,76 m: passa folgado no vão de porta (1,4 m), onde os
        /// braços recolhidos (1,28 m) só passavam com 6 cm de cada lado.
        /// </summary>
        public static readonly Vector4[] SqueezeProbes =
        {
            new Vector4(0f, 0f, 1.45f, 0.09f), new Vector4(0f, 0f, 1.2f, 0.12f), new Vector4(0f, 0f, 0.85f, 0.15f),
            new Vector4(0f, 0f, 0.45f, 0.16f), new Vector4(0f, 0f, 0f, 0.16f), new Vector4(0f, 0f, -0.4f, 0.14f),
            new Vector4(0f, 0f, -0.8f, 0.11f), new Vector4(0f, 0f, -1.2f, 0.09f),
            new Vector4(0.26f, 0f, 0.78f, 0.08f), new Vector4(-0.26f, 0f, 0.78f, 0.08f),
            new Vector4(0.29f, 0f, 0.16f, 0.07f), new Vector4(-0.29f, 0f, 0.16f, 0.07f),
            new Vector4(0.32f, 0f, -0.4f, 0.06f), new Vector4(-0.32f, 0f, -0.4f, 0.06f),
            new Vector4(0.33f, 0f, -0.75f, 0.05f), new Vector4(-0.33f, 0f, -0.75f, 0.05f),
        };

        // O caminho: pontos no eixo de cada passagem (o meio da divisa) e no meio de cada célula.
        // Entre dois pontos ele segue a linha (de _railFrom ao ponto), corrigindo o desvio de lado.
        const float RailLook = 0.8f;
        // Depois do último vão do caminho, um ponto no eixo, do outro lado: o corpo só vira quando
        // as mãos e o fim do corpo saíram da parede.
        const float WallGatePost = 1.6f, OpenGatePost = 1.2f;
        // Travadas seguidas no mesmo lugar (de 2 em 2 s, cada uma com nova tentativa pelo eixo)
        // antes de sumir pelo ralo: rede de segurança, não o normal numa porta.
        const int StuckLimit = 4;
        // Tipo de cada ponto do caminho: meio de célula (ou ponto solto), meio de divisa aberta
        // (dentro da sala) ou meio de um vão na parede (porta, arco, arco largo).
        const byte PointFree = 0, PointOpen = 1, PointWallGate = 2;

        readonly NetworkVariable<byte> _state = new NetworkVariable<byte>();
        readonly NetworkVariable<int> _roars = new NetworkVariable<int>();
        readonly List<Vector3> _path = new List<Vector3>();
        readonly List<Vector3> _railFrom = new List<Vector3>();
        readonly List<byte> _pointKind = new List<byte>();
        readonly List<Vector3> _cellsOnPath = new List<Vector3>();
        readonly Dictionary<PlayerNetwork, float> _mercy = new Dictionary<PlayerNetwork, float>();
        readonly Dictionary<PlayerNetwork, float> _heard = new Dictionary<PlayerNetwork, float>();
        readonly Dictionary<Footsteps, Action<Vector3, float>> _ears = new Dictionary<Footsteps, Action<Vector3, float>>();
        readonly List<Footsteps> _drop = new List<Footsteps>();
        readonly List<Vector3> _players = new List<Vector3>();
        readonly List<Vector2Int> _candidates = new List<Vector2Int>();

        UndertowNav _nav;
        LevelBuilder _level;
        int[,] _reach;

        Vector3 _goal, _destination, _lair;
        // Nada para onde o corpo aponta (como um peixe), nunca de lado: assim entra reto num vão.
        float _speed;
        // Travou: até esta hora o caminho recomeça pelo meio da célula, no eixo do vão.
        float _realignUntil;
        Vector3 _stuckPlace;
        bool _hasGoal, _arrived, _direct;
        int _pathIndex;
        bool _goalInside;
        float _repathAt, _senseAt, _stateUntil, _lastHeardTime, _arrivedAt, _roamAt, _lastRoar = -99f;
        PlayerNetwork _target;
        Vector3 _circleCenter;
        int _circleLeg;
        bool _wasFlooded;
        float _outsideSince = -1f;

        // Travado: confere de 2 em 2 s se andou.
        Vector3 _stuckFrom;
        float _stuckCheckAt;
        int _stuckRun;

        // Os canos: para onde vai, quando chega e se já sumiu.
        Vector3 _travelTo;
        Quaternion _travelRotation = Quaternion.identity;
        float _travelArrive;
        bool _travelHidden;
        UndertowState _afterTravel;
        Vector3 _afterTravelGoal;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _state.OnValueChanged += OnStateChanged;
            _roars.OnValueChanged += OnRoarsChanged;
            WorldNoise.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= OnStateChanged;
            _roars.OnValueChanged -= OnRoarsChanged;
            WorldNoise.Unregister(this);
            foreach (KeyValuePair<Footsteps, Action<Vector3, float>> ear in _ears)
                if (ear.Key != null) ear.Key.MadeNoise -= ear.Value;
            _ears.Clear();
            if (Instance == this) Instance = null;
        }

        void OnStateChanged(byte before, byte after) => StateChanged?.Invoke((UndertowState)after);

        void OnRoarsChanged(int before, int after)
        {
            if (after > before) Roared?.Invoke();
        }

        /// <summary>Host: onde apareceu. Posto pelo diretor.</summary>
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
                Tide(now);
            }

            Think(now);
            Move(Time.deltaTime, now);
            CheckStuck(now);
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
                _roamAt = Time.time + UnityEngine.Random.Range(roamMinSeconds, roamMaxSeconds);
            }
            return true;
        }

        static bool TideFalling()
        {
            PoolTide tide = PoolTide.Current;
            return tide != null && (tide.Phase == TidePhase.Falling || tide.Phase == TidePhase.Draining);
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
                if (State == UndertowState.Asleep || State == UndertowState.Emerge)
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
                if (State == UndertowState.Prowl || State == UndertowState.Return || State == UndertowState.Circle) Roar();
                SetState(close ? UndertowState.Hunt : UndertowState.Investigate);
                _stateUntil = now + investigateSeconds;
                return true;
            }

            // Outra água: só pelos canos, só barulho forte, e só se não está caçando alguém aqui.
            if (loudness < pipeLoudness || theirs < 0) { NoisesIgnored++; return false; }
            if (State == UndertowState.Hunt || State == UndertowState.Waking) { NoisesIgnored++; return false; }
            if (State == UndertowState.Investigate && now - _lastHeardTime < 4f) { NoisesIgnored++; return false; }

            Heard(at, loudness, player, now);
            Vector2Int cell = EmergeCell(theirs, at);
            if (cell.x < 0) { NoisesIgnored++; return false; }
            FreeSpot(cell.x, cell.y, at, out Vector3 spot, out Quaternion facing);
            BeginTravel(spot, facing, UndertowState.Investigate, at, now);
            return true;
        }

        void Heard(Vector3 at, float loudness, PlayerNetwork player, float now)
        {
            NoisesHeard++;
            LastHeardAt = at;
            LastHeardLoudness = loudness;
            _lastHeardTime = now;
            if (player != null)
            {
                _target = player;
                _heard[player] = now;
            }
        }

        // Onde surge numa água: num poço dela, se há (sobe do ralo); senão, na célula dela mais longe do barulho.
        Vector2Int EmergeCell(int region, Vector3 noise)
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
            return new Vector2Int(bx, bz);
        }

        // ---------------------------------------------------------------- Decisão

        // Quem ele caça saiu da água? Ronda a borda por onde ele saiu.
        void Watch(float now)
        {
            if (State != UndertowState.Hunt && State != UndertowState.Investigate) return;
            if (_target == null || !_target.InGame) return;
            if (now - _lastHeardTime > circleMemory) return;

            Vector3 at = _target.transform.position;
            if (Catchable(_target, at)) return;
            // Fora da água dele (no seco, no raso, na escada, na sala inicial): ele não vai, ronda.
            if (!_nav.Nearest(at, _nav.RegionAt(transform.position), out int x, out int z)) return;
            _circleCenter = _nav.ClampToCell(x, z, at, bodyRadius + 0.4f);
            _target = null;
            _circleLeg = 0;
            SetGoal(_circleCenter);
            SetState(UndertowState.Circle);
            _stateUntil = now + circleSeconds;
        }

        // A maré: chegando ao seco, ele ruge e passa a rondar o mapa; descendo, some pelos ralos se
        // está fora da água funda (nunca fica encalhado nem anda em linha reta através de parede).
        void Tide(float now)
        {
            bool flooded = _nav.Flooded && !TideFalling();
            if (flooded && !_wasFlooded)
            {
                if (State == UndertowState.Asleep || State == UndertowState.Prowl || State == UndertowState.Return
                    || State == UndertowState.Emerge || State == UndertowState.Circle)
                {
                    Roar();
                    BeginProwl(now);
                }
            }
            _wasFlooded = flooded;

            if (State == UndertowState.Travel || State == UndertowState.Feed) { _outsideSince = -1f; return; }
            bool inCell = _nav.WorldToCell(transform.position, out int cx, out int cz);
            bool outside = !inCell || !_nav.Territory[cx, cz];
            bool stranded = inCell && !_nav.IsDeep(cx, cz) && TideFalling();
            if (outside && _outsideSince < 0f) _outsideSince = now;
            if (!outside) _outsideSince = -1f;
            if (stranded || outside && now - _outsideSince > 0.6f)
            {
                LastStop += $" | {now:F1}s maré: some pelo ralo ({(stranded ? "descendo" : "fora do território")})";
                DiveAway(now);
            }
        }

        void Think(float now)
        {
            switch (State)
            {
                case UndertowState.Asleep:
                    if (_nav.Flooded && !TideFalling()) { Roar(); BeginProwl(now); break; }
                    if (!RoamPaused && now >= _roamAt) Roam(now);
                    break;

                case UndertowState.Waking:
                    if (now >= _stateUntil)
                    {
                        SetGoal(ClampGoal(_afterTravelGoal));
                        SetState(_afterTravel);
                        _stateUntil = now + investigateSeconds;
                    }
                    break;

                case UndertowState.Emerge:
                    if (now >= _stateUntil) GoAsleep(now);
                    break;

                case UndertowState.Investigate:
                case UndertowState.Hunt:
                    if (State == UndertowState.Hunt && now - _lastHeardTime > huntMemory) SetState(UndertowState.Investigate);
                    if (now >= _stateUntil) { BeginRest(now); break; }
                    // Chegou onde ouviu e nada mais: fica um instante, escutando, e vai embora.
                    // Conta da chegada, não do barulho: senão o tempo acordando e nadando come a espera.
                    if (_arrived && now - Mathf.Max(_lastHeardTime, _arrivedAt) > lingerSeconds) BeginRest(now);
                    break;

                case UndertowState.Circle:
                    if (now >= _stateUntil) { BeginRest(now); break; }
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

                case UndertowState.Prowl:
                    if (_arrived || now >= _stateUntil)
                    {
                        if (_nav.Flooded && !TideFalling()) NextProwl(now);
                        else GoAsleep(now);
                    }
                    break;

                case UndertowState.Return:
                    if (_arrived) GoAsleep(now);
                    break;

                case UndertowState.Travel:
                    ThinkTravel(now);
                    break;

                case UndertowState.Feed:
                    if (now >= _stateUntil) BeginRest(now);
                    break;
            }
        }

        void SetState(UndertowState state)
        {
            if (State == state) return;
            // Dar o bote (caçar de perto) é com rugido.
            if (state == UndertowState.Hunt) Roar();
            _state.Value = (byte)state;
        }

        /// <summary>Host: um rugido (todas as máquinas tocam). Com intervalo mínimo.</summary>
        void Roar()
        {
            float now = Time.time;
            if (now - _lastRoar < roarCooldown) return;
            _lastRoar = now;
            _roars.Value++;
        }

        void SetGoal(Vector3 goal)
        {
            if (!_hasGoal || Flat(goal - _goal).sqrMagnitude > 1f) _repathAt = 0f;
            _goal = goal;
            _hasGoal = true;
            _arrived = false;
            _stuckFrom = transform.position;
            _stuckCheckAt = Time.time + 2f;
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
            _speed = 0f;
            Roar();
            SetState(UndertowState.Waking);
            _stateUntil = now + wakeSeconds;
        }

        void GoAsleep(float now)
        {
            if (_nav.Flooded && !TideFalling()) { BeginProwl(now); return; }
            _target = null;
            _hasGoal = false;
            _speed = 0f;
            SetState(UndertowState.Asleep);
            _roamAt = now + UnityEngine.Random.Range(roamMinSeconds, roamMaxSeconds);
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

        // Acabou o que fazia: na maré alta, ronda; na baixa, descansa na água funda em que está (ou
        // na mais perto, pela água); sem caminho, pelos canos.
        void BeginRest(float now)
        {
            LastStop += $" | {now:F1}s descansando depois de {State}";
            _target = null;
            if (_nav.Flooded && !TideFalling()) { BeginProwl(now); return; }
            if (!_nav.WorldToCell(transform.position, out int cx, out int cz)) { DiveAway(now); return; }
            if (_nav.IsDeep(cx, cz)) { GoAsleep(now); return; }

            _nav.Distances(cx, cz, _reach);
            int bx = -1, bz = -1, best = int.MaxValue;
            for (int z = 0; z < _nav.Data.Height; z++)
                for (int x = 0; x < _nav.Data.Width; x++)
                    if (_reach[x, z] >= 0 && _nav.IsDeep(x, z) && _reach[x, z] < best) { best = _reach[x, z]; bx = x; bz = z; }
            if (bx < 0) { DiveAway(now); return; }
            SetGoal(_nav.CellCenter(bx, bz));
            SetState(UndertowState.Return);
        }

        void BeginProwl(float now)
        {
            SetState(UndertowState.Prowl);
            NextProwl(now);
        }

        // Rondando na maré alta: um ponto a 8–18 m de um jogador, longe de todos (7 m), na água dele.
        void NextProwl(float now)
        {
            _stateUntil = now + 14f;
            if (!_nav.WorldToCell(transform.position, out int cx, out int cz)) return;
            int mine = _nav.Region[cx, cz];
            CollectPlayers();
            _candidates.Clear();
            Vector3 around = _players.Count > 0 ? _players[UnityEngine.Random.Range(0, _players.Count)] : transform.position;
            for (int z = 0; z < _nav.Data.Height; z++)
                for (int x = 0; x < _nav.Data.Width; x++)
                {
                    if (!_nav.Territory[x, z] || _nav.Region[x, z] != mine || x == cx && z == cz) continue;
                    Vector3 c = _nav.CellCenter(x, z);
                    float d = Flat(c - around).magnitude;
                    if (_players.Count > 0 && (d < prowlNear || d > prowlFar)) continue;
                    if (_players.Count == 0 && Flat(c - transform.position).magnitude > 20f) continue;
                    if (NearAnyPlayer(c, prowlKeepAway)) continue;
                    _candidates.Add(new Vector2Int(x, z));
                }
            if (_candidates.Count == 0) { SetGoal(transform.position); Arrive("ronda: nenhum ponto"); return; }
            Vector2Int pick = _candidates[UnityEngine.Random.Range(0, _candidates.Count)];
            SetGoal(_nav.CellCenter(pick.x, pick.y));
        }

        // Muda de água pelo relógio (maré baixa, sem barulho).
        void Roam(float now)
        {
            if (!ChooseLurkCell(out Vector2Int cell)) { _roamAt = now + 8f; return; }
            Roams++;
            LastStop += $" | {now:F1}s muda de água para {cell}";
            if (_nav.WorldToCell(transform.position, out int cx, out int cz) && _nav.Region[cx, cz] == _nav.Region[cell.x, cell.y])
            {
                _nav.Distances(cx, cz, _reach);
                if (_reach[cell.x, cell.y] >= 0 && _reach[cell.x, cell.y] <= 5)
                {
                    SetGoal(_nav.CellCenter(cell.x, cell.y));
                    SetState(UndertowState.Prowl);
                    _stateUntil = now + 25f;
                    return;
                }
            }
            FreeSpot(cell.x, cell.y, _nav.CellCenter(cell.x, cell.y), out Vector3 spot, out Quaternion facing);
            BeginTravel(spot, facing, UndertowState.Emerge, spot, now);
        }

        /// <summary>Host, para os testes: muda de água agora (como pelo relógio).</summary>
        public void RoamNow() { if (IsServer && EnsureLevel()) Roam(Time.time); }

        // Some pelo ralo e reaparece descansando numa água funda (maré descendo, travado, encalhado).
        void DiveAway(float now)
        {
            _outsideSince = -1f;
            if (!ChooseLurkCell(out Vector2Int cell))
            {
                if (!_nav.Nearest(transform.position, -1, out int x, out int z)) return;
                cell = new Vector2Int(x, z);
            }
            FreeSpot(cell.x, cell.y, _nav.CellCenter(cell.x, cell.y), out Vector3 spot, out Quaternion facing);
            BeginTravel(spot, facing, UndertowState.Emerge, spot, now);
        }

        void CollectPlayers()
        {
            _players.Clear();
            foreach (PlayerNetwork p in PlayerNetwork.All)
                if (p != null && p.IsSpawned && p.InGame) _players.Add(p.transform.position);
        }

        bool NearAnyPlayer(Vector3 at, float distance)
        {
            foreach (Vector3 p in _players)
                if (Flat(p - at).magnitude < distance) return true;
            return false;
        }

        /// <summary>
        /// Uma água funda (ou poço) para descansar: longe de todo jogador (10 m), nunca numa água em
        /// que alguém está nadando; de preferência a ~14 m de um jogador sorteado. Só onde dá para
        /// nadar, se houver (drenada, os poços).
        /// </summary>
        bool ChooseLurkCell(out Vector2Int cell)
        {
            cell = new Vector2Int(-1, -1);
            CollectPlayers();
            var swimming = new HashSet<int>();
            foreach (PlayerNetwork p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || !p.InGame || !Catchable(p, p.transform.position)) continue;
                int r = _nav.RegionAt(p.transform.position);
                if (r >= 0) swimming.Add(r);
            }
            _nav.WorldToCell(transform.position, out int cx, out int cz);

            _candidates.Clear();
            for (int pass = 0; pass < 2 && _candidates.Count == 0; pass++)
                for (int z = 0; z < _nav.Data.Height; z++)
                    for (int x = 0; x < _nav.Data.Width; x++)
                    {
                        if (!_nav.Territory[x, z] || !_nav.IsDeep(x, z) || x == cx && z == cz) continue;
                        if (pass == 0 && !_nav.Swimmable(x, z)) continue;
                        if (swimming.Contains(_nav.Region[x, z])) continue;
                        if (NearAnyPlayer(_nav.CellCenter(x, z), roamMinDistance)) continue;
                        _candidates.Add(new Vector2Int(x, z));
                    }
            if (_candidates.Count == 0) return false;

            if (_players.Count > 0 && UnityEngine.Random.value < roamNearPlayer)
            {
                Vector3 who = _players[UnityEngine.Random.Range(0, _players.Count)];
                float best = float.MaxValue;
                foreach (Vector2Int c in _candidates)
                {
                    float score = Mathf.Abs(Flat(_nav.CellCenter(c.x, c.y) - who).magnitude - roamPreferred) + UnityEngine.Random.value * 4f;
                    if (score >= best) continue;
                    best = score;
                    cell = c;
                }
                return true;
            }
            cell = _candidates[UnityEngine.Random.Range(0, _candidates.Count)];
            return true;
        }

        /// <summary>Host: manda ir pelos canos até <paramref name="to"/>; lá surge e segue no estado dado.</summary>
        void BeginTravel(Vector3 to, Quaternion facing, UndertowState after, Vector3 goal, float now)
        {
            _travelTo = to;
            _travelRotation = facing;
            _afterTravel = after;
            _afterTravelGoal = goal;
            _travelHidden = false;
            _travelArrive = now + diveSeconds + pipeSeconds + Flat(to - transform.position).magnitude / Mathf.Max(0.1f, pipeSpeed);
            _stateUntil = now + diveSeconds;
            _speed = 0f;
            _target = null;
            _hasGoal = false;
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
                Place(_travelTo, _travelRotation);
                return;
            }

            if (now < _travelArrive) return;
            _travelHidden = false;
            if (_afterTravel == UndertowState.Emerge || _afterTravel == UndertowState.Asleep)
            {
                SetState(UndertowState.Emerge);
                _stateUntil = now + emergeSeconds;
                return;
            }
            BeginWaking(now, _afterTravel, _afterTravelGoal);
        }

        /// <summary>Host: põe num ponto de uma vez (sem a rede interpolar o caminho).</summary>
        public void Place(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (TryGetComponent(out NetworkTransform moved)) moved.Teleport(position, rotation, transform.localScale);
            _speed = 0f;
            _path.Clear();
            _repathAt = 0f;
        }

        /// <summary>Host, para os testes: dorme já, na célula do ponto dado (numa pose em que o corpo cabe).</summary>
        public void SleepAt(Vector3 position)
        {
            EnsureLevel();
            _lair = position;
            if (_nav != null && _nav.WorldToCell(position, out int x, out int z))
            {
                FreeSpot(x, z, position, out Vector3 spot, out Quaternion facing);
                Place(spot, facing);
            }
            else Place(position, transform.rotation);
            _target = null;
            _hasGoal = false;
            _mercy.Clear();
            _heard.Clear();
            SetState(UndertowState.Asleep);
            _roamAt = Time.time + UnityEngine.Random.Range(roamMinSeconds, roamMaxSeconds);
        }

        /// <summary>
        /// Uma pose em que o corpo inteiro cabe na célula, descansando no fundo: o meio da célula e
        /// pontos em volta, em oito direções, a começar pelo mais perto de <paramref name="near"/>.
        /// Sem nenhuma, o meio da célula à meia água.
        /// </summary>
        public void FreeSpot(int x, int z, Vector3 near, out Vector3 spot, out Quaternion facing)
        {
            Vector3 center = _nav.CellCenter(x, z);
            float best = float.MaxValue;
            spot = new Vector3(center.x, Mathf.Max(center.y + 0.4f, _nav.SurfaceY - 1f), center.z);
            facing = transform.rotation;
            for (int ox = -1; ox <= 1; ox++)
                for (int oz = -1; oz <= 1; oz++)
                {
                    Vector3 p = center + new Vector3(ox, 0f, oz) * 0.8f;
                    float d = Flat(p - near).sqrMagnitude;
                    if (d >= best) continue;
                    for (int k = 0; k < 8; k++)
                    {
                        Quaternion r = Quaternion.Euler(0f, k * 45f, 0f);
                        float y = RequiredY(p, r);
                        if (!Crawling && y > _nav.SurfaceY - 0.3f) continue;
                        Vector3 at = new Vector3(p.x, y + 0.02f, p.z);
                        // Descansando, o corpo inteiro (braços só recolhidos, cauda toda).
                        if (!Fits(at, r, false)) continue;
                        best = d;
                        spot = at;
                        facing = r;
                        break;
                    }
                }
        }

        // ---------------------------------------------------------------- Movimento

        bool OutOfDeep()
        {
            return _nav.WorldToCell(transform.position, out int x, out int z) && !_nav.IsDeep(x, z);
        }

        float SpeedNow()
        {
            bool flood = OutOfDeep();
            float speed;
            switch (State)
            {
                case UndertowState.Hunt:
                    speed = huntSpeed;
                    if (flood)
                    {
                        bool fresh = _target != null && Time.time - _lastHeardTime < 0.8f;
                        speed = fresh && _target != null && Vector3.Distance(Head, _target.transform.position + Vector3.up * 0.9f) < lungeRange ? lungeSpeed : floodHuntSpeed;
                    }
                    break;
                case UndertowState.Investigate: speed = flood ? floodInvestigateSpeed : investigateSpeed; break;
                case UndertowState.Prowl: speed = prowlSpeed; break;
                case UndertowState.Circle:
                case UndertowState.Return: speed = returnSpeed; break;
                default: return 0f;
            }
            return Crawling ? Mathf.Min(speed, crawlSpeed) : speed;
        }

        bool Moving => State == UndertowState.Investigate || State == UndertowState.Hunt || State == UndertowState.Prowl
                    || State == UndertowState.Circle || State == UndertowState.Return;

        void Move(float dt, float now)
        {
            if (State == UndertowState.Travel && _travelHidden) return;
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            Vector3 forward = Flat(rotation * Vector3.forward);
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            Vector3 want = Vector3.zero;
            float wantSpeed = 0f;
            Vector3 aim = position;
            // Seguindo a linha do caminho: o desvio de lado (para voltar a ela, se travar).
            bool onRail = false;
            Vector3 offRail = Vector3.zero;

            if (Moving && _hasGoal)
            {
                if (now >= _repathAt) Repath(position, now);
                if (!_arrived)
                {
                    bool last = _direct || _pathIndex >= _path.Count;
                    if (last)
                    {
                        aim = _destination;
                        // Indo a um barulho, o último trecho leva a CABEÇA ao ponto (é dela que se pega);
                        // sem ouvir de novo, para um pouco antes (escuta: quem ficou parado e quieto escapa).
                        bool byHead = State == UndertowState.Investigate || State == UndertowState.Hunt;
                        bool fresh = now - _lastHeardTime <= huntMemory;
                        float tolerance = byHead && !fresh ? stopShort : 0.35f;
                        Vector3 to = Flat(_destination - (byHead ? Head : position));
                        if (to.magnitude < tolerance) Arrive($"chegou (a {to.magnitude:F2} m do destino {_destination})");
                        else
                        {
                            want = to.normalized;
                            wantSpeed = SpeedNow();
                        }
                    }
                    else
                    {
                        // A linha do trecho: de _railFrom ao ponto. Persegue um ponto da linha um pouco
                        // à frente (o desvio de lado some em ~1 m) e, antes de uma curva, freia para
                        // virar no ponto: assim entra no próximo vão já no eixo dele.
                        Vector3 target = _path[_pathIndex];
                        Vector3 from = _railFrom[_pathIndex];
                        Vector3 seg = Flat(target - from);
                        float length = seg.magnitude;
                        Vector3 dir = length > 0.01f ? seg / length : forward;
                        float along = Vector3.Dot(Flat(position - from), dir);
                        float remaining = length - along;
                        float turn = TurnAfter(_pathIndex, dir);
                        bool gateNext = _pathIndex + 1 < _path.Count && _pointKind[_pathIndex + 1] == PointWallGate;
                        float tolerance = turn > 20f ? (gateNext ? 0.15f : 0.25f) : 0.35f;
                        aim = target;
                        // Indo a um barulho sem ouvir de novo, perto do fim do caminho (o último vão ou o
                        // ponto depois dele): para com a cabeça a stopShort, como no último trecho. Sem
                        // isso, ia até depois da porta e esbarrava em quem ficou parado e quieto ali.
                        bool listening = (State == UndertowState.Investigate || State == UndertowState.Hunt)
                                         && now - _lastHeardTime > huntMemory;
                        if (listening && _pathIndex >= _path.Count - 2 && Flat(_destination - Head).magnitude < stopShort)
                            Arrive($"parou antes do vão (a cabeça a {Flat(_destination - Head).magnitude:F2} m do destino {_destination})");
                        else if (remaining < tolerance || Flat(target - position).magnitude < tolerance) Advance(position);
                        else
                        {
                            onRail = true;
                            offRail = Flat(position - from) - dir * along;
                            float look = along + RailLook;
                            Vector3 carrot = from + dir * Mathf.Clamp(look, 0f, length);
                            // Passa do ponto para o seguinte sem curva (o meio do vão e o meio da
                            // célula seguinte estão no mesmo eixo): continua na mesma linha.
                            if (look > length && turn < 5f && _pathIndex + 1 < _path.Count)
                                carrot = target + dir * Mathf.Min(look - length, Flat(_path[_pathIndex + 1] - target).magnitude);
                            want = Flat(carrot - position);
                            want = want.sqrMagnitude > 1e-6f ? want.normalized : dir;
                            wantSpeed = SpeedNow();
                            if (turn > 20f)
                            {
                                float corner = Mathf.Lerp(wantSpeed, 0.6f, Mathf.Clamp01(turn / 90f));
                                wantSpeed = Mathf.Min(wantSpeed, Mathf.Sqrt(corner * corner + 4f * acceleration * Mathf.Max(0f, remaining)));
                            }
                        }
                    }
                }
            }

            // Vira para onde quer ir e nada para onde aponta, nunca de lado; muito torto, só vira
            // (a ~70° de diferença, parado): é o que o deixa entrar reto num vão.
            Vector3 face = want.sqrMagnitude > 0.001f ? want
                         : State == UndertowState.Hunt && _target != null ? Flat(_target.transform.position - position)
                         : Vector3.zero;
            Quaternion turned = face.sqrMagnitude > 0.001f
                ? Quaternion.RotateTowards(rotation, Quaternion.LookRotation(face, Vector3.up), turnSpeed * dt)
                : rotation;
            float align = want.sqrMagnitude > 0.001f ? Vector3.Dot(forward, want) : 1f;
            float targetSpeed = wantSpeed * Mathf.Clamp01((align - 0.34f) / 0.66f);
            _speed = Mathf.MoveTowards(_speed, targetSpeed, (targetSpeed > _speed ? acceleration : acceleration * 2f) * dt);
            Vector3 heading = Flat(turned * Vector3.forward);
            heading = heading.sqrMagnitude > 1e-6f ? heading.normalized : forward;

            // O corpo tem de caber: primeiro o passo e o giro juntos; senão reto, sem virar (acaba de
            // sair de um vão); só o giro; de lado, de volta à linha; só a altura (para cima ou para
            // baixo). Nada cabe: fica (e o CheckStuck tenta de novo pelo eixo).
            Vector3 step = position + heading * (_speed * dt);
            float need = RequiredY(step, turned);
            // Olhando à frente: para sair do poço (ou passar para um piso mais alto), sobe antes de
            // avançar; senão a cabeça bate na parede do poço e ele nunca sai do fundo.
            float rise = need;
            Vector3 ahead = Flat(aim - position);
            if (Moving && ahead.sqrMagnitude > 0.01f)
            {
                Vector3 toward = ahead.normalized;
                rise = Mathf.Max(rise, RequiredY(position + toward * Mathf.Min(1.4f, ahead.magnitude), Quaternion.LookRotation(toward, Vector3.up)));
                if (!Crawling) rise = Mathf.Min(rise, Mathf.Max(need, _nav.SurfaceY - 0.3f));
            }
            // No caminho (de vão em vão), baixo: o lintel da porta entre duas águas fundas fica a 0,4 m
            // e o arco nasce a 0,0; na maré alta, caçando, ele subia até 0,9 e batia a cabeça.
            float height = TargetHeight(aim, need);
            if (onRail) height = Mathf.Min(height, need + 0.3f);
            float y = Mathf.MoveTowards(position.y, Mathf.Max(height, rise), verticalSpeed * dt);
            if (TryPose(new Vector3(step.x, y, step.z), turned)) return;
            Blocked++;
            Vector3 straight = position + forward * (_speed * dt);
            if (_speed > 0.01f && TryPose(new Vector3(straight.x, y, straight.z), rotation)) return;
            if (TryPose(new Vector3(position.x, y, position.z), turned)) { _speed *= 0.5f; return; }
            if (onRail && offRail.sqrMagnitude > 0.0004f)
            {
                Vector3 back = position - offRail.normalized * Mathf.Min(offRail.magnitude, 0.5f * dt);
                if (TryPose(new Vector3(back.x, y, back.z), rotation)) { _speed *= 0.5f; return; }
            }
            _speed = 0f;
            // Quer voltar e não cabe virar (dentro de um vão): sai de ré, devagar.
            if (align < 0f)
            {
                Vector3 reverse = position - forward * (0.5f * dt);
                if (TryPose(new Vector3(reverse.x, y, reverse.z), rotation)) return;
            }
            if (TryPose(new Vector3(position.x, y, position.z), rotation)) return;
            float stay = Mathf.MoveTowards(position.y, Mathf.Max(RequiredY(position, rotation), position.y), verticalSpeed * dt);
            if (!TryPose(new Vector3(position.x, stay, position.z), rotation))
            {
                // Já estava preso (a maré o deixou assim, ou um teste o pôs): sai para cima, que é água.
                transform.position = new Vector3(position.x, Mathf.MoveTowards(position.y, need, verticalSpeed * dt), position.z);
            }
        }

        // Quanto o caminho vira (graus) no ponto i, vindo na direção dada: para o ponto seguinte ou,
        // no último, para o destino.
        float TurnAfter(int i, Vector3 dir)
        {
            Vector3 next = Flat((i + 1 < _path.Count ? _path[i + 1] : _destination) - _path[i]);
            if (next.sqrMagnitude < 0.04f) return 0f;
            return Vector3.Angle(dir, next);
        }

        // Chegou ao ponto do caminho: o seguinte (ou um atalho, onde a linha está livre).
        void Advance(Vector3 position)
        {
            _pathIndex++;
            _stuckRun = 0;
            Shortcut(position);
        }

        bool TryPose(Vector3 position, Quaternion rotation)
        {
            if (!Fits(position, rotation)) return false;
            transform.SetPositionAndRotation(position, rotation);
            return true;
        }

        /// <summary>
        /// O corpo cabe nesta pose? Nenhum ponto de <see cref="BodyProbes"/> (ou, com
        /// <paramref name="squeeze"/>, do corpo espremido, <see cref="SqueezeProbes"/>: vãos de porta,
        /// colunas perto da divisa, curvas) encosta em colisor da fase, e (nadando) há água para a
        /// cabeça: o piso sob o focinho fica abaixo da superfície (é isso que o segura na água funda
        /// na maré baixa).
        /// </summary>
        public bool Fits(Vector3 position, Quaternion rotation, bool squeeze = true)
        {
            if (_level == null) return true;
            if (!Crawling)
            {
                Vector3 snout = position + rotation * new Vector3(0f, 0f, BodyProbes[0].z);
                if (FloorAt(snout) > _nav.SurfaceY - 0.25f) return false;
            }
            if (ProbesClear(BodyProbes, position, rotation)) return true;
            return squeeze && ProbesClear(SqueezeProbes, position, rotation);
        }

        bool ProbesClear(Vector4[] probes, Vector3 position, Quaternion rotation)
        {
            foreach (Vector4 probe in probes)
            {
                Vector3 p = position + rotation * new Vector3(probe.x, probe.y, probe.z);
                if (Solid(p, probe.w)) return false;
            }
            return true;
        }

        bool Solid(Vector3 p, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(p, radius, Overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Overlaps[i] != null && Overlaps[i].transform.IsChildOf(_level.transform)) return true;
            return false;
        }

        /// <summary>Altura do que há embaixo do ponto (piso, degrau, capa, deque do poço), medida por raio na fase.</summary>
        public float FloorAt(Vector3 p)
        {
            float top = Crawling ? p.y + 1f : Mathf.Max(_nav.SurfaceY, p.y) + 0.3f;
            int count = Physics.RaycastNonAlloc(new Vector3(p.x, top, p.z), Vector3.down, Hits, 16f, ~0, QueryTriggerInteraction.Ignore);
            float best = -50f;
            for (int i = 0; i < count; i++)
            {
                if (!Hits[i].collider.transform.IsChildOf(_level.transform)) continue;
                if (Hits[i].point.y > best) best = Hits[i].point.y;
            }
            return best;
        }

        /// <summary>A altura mínima do meio do corpo nesta pose: o piso sob cada ponto do corpo mais a folga dele.</summary>
        public float RequiredY(Vector3 position, Quaternion rotation)
        {
            if (_level == null) return position.y;
            float need = -50f;
            for (int i = 0; i < BodyProbes.Length; i++)
            {
                Vector4 probe = BodyProbes[i];
                Vector3 p = position + rotation * new Vector3(probe.x, probe.y, probe.z);
                need = Mathf.Max(need, FloorAt(p) + probe.w + 0.14f);
            }
            return need;
        }

        // A altura em que nada, entre o mínimo que o corpo pede (need) e logo abaixo da superfície:
        // parado, no fundo; caçando, sobe para quem ouviu (vem por baixo).
        float TargetHeight(Vector3 aim, float need)
        {
            float surface = _nav.SurfaceY;
            float low = need;
            float high = Mathf.Max(low, surface - 0.45f);

            switch (State)
            {
                case UndertowState.Asleep:
                case UndertowState.Travel:
                    return low;
                case UndertowState.Waking:
                case UndertowState.Emerge:
                    return Mathf.Lerp(low, high, 0.35f);
                case UndertowState.Investigate:
                {
                    float y = Flat(_goal - Head).magnitude < 5f ? _goal.y + 0.6f : low + 0.3f;
                    return Mathf.Clamp(y, low, high);
                }
                case UndertowState.Hunt:
                case UndertowState.Feed:
                {
                    float y = _target != null ? _target.transform.position.y + 0.6f : surface - 1.2f;
                    if (State == UndertowState.Hunt && _target != null && Flat(_target.transform.position - Head).magnitude > 5f) y = low + 0.3f;
                    if (State == UndertowState.Feed) y = low;
                    return Mathf.Clamp(y, low, high);
                }
                default:
                    return Mathf.Clamp(low + 0.3f, low, high);
            }
        }

        // Travado (andando sem sair do lugar, de 2 em 2 s). Caçando ou indo a um barulho, não desiste:
        // recomeça pelo meio da célula, no eixo do vão, e tenta de novo. Só depois de StuckLimit
        // travadas seguidas no mesmo lugar (a ~2 m) some pelo ralo: rede de segurança. Chegar a um
        // ponto do caminho zera a conta.
        void CheckStuck(float now)
        {
            if (!Moving || !_hasGoal || _arrived) { _stuckCheckAt = now + 2f; _stuckFrom = transform.position; return; }
            if (now < _stuckCheckAt) return;
            Vector3 position = transform.position;
            float moved = Flat(position - _stuckFrom).magnitude;
            _stuckFrom = position;
            _stuckCheckAt = now + 2f;
            if (moved > 0.3f) return;
            Stucks++;
            if (Flat(position - _stuckPlace).magnitude > 2f) _stuckRun = 0;
            _stuckPlace = position;
            _stuckRun++;
            if (_stuckRun >= StuckLimit)
            {
                _stuckRun = 0;
                LastStop += $" | {now:F1}s travado {StuckLimit} vezes em {position}: some pelo ralo";
                DiveAway(now);
                return;
            }
            if (State == UndertowState.Hunt || State == UndertowState.Investigate)
            {
                LastStop += $" | {now:F1}s travado em {position}: de novo pelo eixo";
                _realignUntil = now + 2.5f;
                _repathAt = 0f;
                return;
            }
            Arrive("travado (o corpo não cabe no caminho)");
        }

        void Repath(Vector3 position, float now)
        {
            _repathAt = now + (State == UndertowState.Hunt ? 0.25f : 0.6f);
            // Caçando ou indo a um barulho (a meta anda a cada 0,25 s), no meio de uma travessia não
            // refaz o caminho: termina de passar pelo eixo do vão (refazendo, entrava sempre torto).
            // Rondando ou voltando, refaz (a meta nova pode estar atrás: ele sai de ré).
            bool chasing = State == UndertowState.Hunt || State == UndertowState.Investigate;
            if (chasing && now >= _realignUntil && Crossing(position)) { _repathAt = now + 0.1f; return; }
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
            _nav.Path(cx, cz, gx, gz, _cellsOnPath);
            BuildRail(cx, cz);
            _pathIndex = 0;
            bool realign = now < _realignUntil;
            _direct = !realign && Clear(position, _destination);
            if (!_direct)
            {
                Approach(position, cx, cz, realign);
                Shortcut(position);
            }
            if (Flat(_destination - position).magnitude < 0.35f) Arrive($"já no destino {_destination}");
        }

        /// <summary>
        /// O caminho por pontos: para cada troca de célula, o meio da divisa (o eixo do vão) e o meio
        /// da célula seguinte, os dois na linha que liga os meios das duas células. O corpo cruza
        /// todo vão (porta, arco, arco largo, divisa entre colunas) reto e pelo meio, e só vira no
        /// meio das células. Sem o meio da célula do alvo (dali vale a aproximação final, pela
        /// cabeça; com ele, o corpo passava por cima de quem estava parado ali e esbarrava): no
        /// lugar dele, um ponto no eixo logo depois do último vão, para não virar com o corpo dentro.
        /// </summary>
        void BuildRail(int cx, int cz)
        {
            _path.Clear();
            _railFrom.Clear();
            _pointKind.Clear();
            Vector3 previous = _nav.CellCenter(cx, cz);
            int px = cx, pz = cz;
            foreach (Vector3 c in _cellsOnPath)
            {
                _nav.WorldToCell(c, out int nx, out int nz);
                byte kind = EdgeBetween(px, pz, nx, nz) == PoolEdge.Open ? PointOpen : PointWallGate;
                AddPoint((previous + c) * 0.5f, previous, kind);
                AddPoint(c, previous, PointFree);
                previous = c;
                px = nx;
                pz = nz;
            }
            int n = _path.Count;
            if (n < 2) return;
            Vector3 gate = _path[n - 2];
            Vector3 from = _railFrom[n - 2];
            byte last = _pointKind[n - 2];
            _path.RemoveAt(n - 1);
            _railFrom.RemoveAt(n - 1);
            _pointKind.RemoveAt(n - 1);
            Vector3 axis = Flat(gate - from).normalized;
            AddPoint(gate + axis * (last == PointWallGate ? WallGatePost : OpenGatePost), from, PointFree);
        }

        void AddPoint(Vector3 point, Vector3 from, byte kind)
        {
            _path.Add(point);
            _railFrom.Add(from);
            _pointKind.Add(kind);
        }

        PoolEdge EdgeBetween(int ax, int az, int bx, int bz)
        {
            WallSide side = bx > ax ? WallSide.East : bx < ax ? WallSide.West : bz > az ? WallSide.North : WallSide.South;
            return _nav.Data.EdgeSouth == null ? PoolEdge.Open : _nav.Data.EdgeAt(ax, az, side);
        }

        /// <summary>
        /// Começando o caminho longe do eixo do primeiro vão (ou recomeçando depois de travar): vai
        /// antes ao meio da célula em que está, em linha reta, e dali segue o eixo. Já perto da
        /// parede (a cabeça dentro do vão), segue em frente.
        /// </summary>
        void Approach(Vector3 position, int cx, int cz, bool realign)
        {
            if (_path.Count == 0) return;
            Vector3 center = _nav.CellCenter(cx, cz);
            if (Flat(center - position).magnitude < 0.3f) return;
            if (!realign)
            {
                if (_pointKind[0] == PointFree) return;
                Vector3 from = _railFrom[0];
                Vector3 dir = Flat(_path[0] - from);
                float length = dir.magnitude;
                if (length < 0.01f) return;
                dir /= length;
                float along = Vector3.Dot(Flat(position - from), dir);
                float side = (Flat(position - from) - dir * along).magnitude;
                if (side <= 0.35f || along > length - 0.8f) return;
            }
            else
            {
                // Travou: volta ao meio da célula só se cabe virado para ele (dentro de um vão não
                // cabe, e ali o certo é seguir em frente pelo eixo).
                Vector3 back = Flat(center - position);
                if (Vector3.Dot(Flat(transform.forward), back) < 0f && !Fits(position, Quaternion.LookRotation(back, Vector3.up))) return;
            }
            _path.Insert(0, center);
            _railFrom.Insert(0, position);
            _pointKind.Insert(0, PointFree);
        }

        /// <summary>Entrando num vão de parede (já passou do meio da célula de antes) ou ainda saindo dele.</summary>
        bool Crossing(Vector3 position)
        {
            if (_direct || _pathIndex >= _path.Count) return false;
            int i = _pathIndex;
            if (i > 0 && _pointKind[i - 1] == PointWallGate) return true;
            if (_pointKind[i] != PointWallGate) return false;
            Vector3 dir = Flat(_path[i] - _railFrom[i]);
            return dir.sqrMagnitude > 1e-4f && Vector3.Dot(Flat(position - _railFrom[i]), dir.normalized) > 0.3f;
        }

        // Pula pontos do caminho onde a linha reta está livre, mas nunca o meio de um vão de parede
        // nem direto para ele (o vão se cruza sempre pelo eixo), nem no meio de uma travessia.
        void Shortcut(Vector3 position)
        {
            if (Crossing(position)) return;
            bool skipped = false;
            while (_pathIndex + 1 < _path.Count && _pointKind[_pathIndex] != PointWallGate
                   && _pointKind[_pathIndex + 1] != PointWallGate && Clear(position, _path[_pathIndex + 1]))
            {
                _pathIndex++;
                skipped = true;
            }
            // O atalho é a linha daqui até o ponto.
            if (skipped) _railFrom[_pathIndex] = position;
        }

        // Linha reta livre para o corpo: no território com a largura dos braços recolhidos, sem
        // cruzar vão de parede (porta, arco: esses só pelo eixo) e sem parede nem coluna na altura
        // em que nada.
        bool Clear(Vector3 from, Vector3 to)
        {
            if (!_nav.StraightClear(from, to, 0.9f)) return false;
            if (CrossesWallGate(from, to)) return false;
            Vector3 delta = Flat(to - from);
            float distance = delta.magnitude;
            if (distance < 0.05f) return true;
            int count = Physics.SphereCastNonAlloc(from, 0.7f, delta / distance, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Hits[i].collider.transform.IsChildOf(_level.transform)) return false;
            return true;
        }

        bool CrossesWallGate(Vector3 from, Vector3 to)
        {
            Vector3 delta = Flat(to - from);
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.25f));
            if (!_nav.WorldToCell(from, out int lx, out int lz)) return true;
            for (int i = 1; i <= steps; i++)
            {
                if (!_nav.WorldToCell(from + delta * ((float)i / steps), out int x, out int z)) return true;
                if (x == lx && z == lz) continue;
                if (Mathf.Abs(x - lx) + Mathf.Abs(z - lz) != 1 || EdgeBetween(lx, lz, x, z) != PoolEdge.Open) return true;
                lx = x;
                lz = z;
            }
            return false;
        }

        // ---------------------------------------------------------------- Captura

        /// <summary>
        /// O jogador está onde ele pega? Os pés numa célula do território e água acima da cintura
        /// (nadando, ou de pé no raso e no seco alagados na maré alta); no fundo de uma piscina
        /// drenada (onde ele se arrasta), de pé mesmo. Na sala inicial, no raso de verdade e na escada
        /// fora d'água, nunca.
        /// </summary>
        public bool Catchable(PlayerNetwork player, Vector3 feet)
        {
            if (_nav == null || !_nav.WorldToCell(feet, out int x, out int z) || !_nav.Territory[x, z]) return false;
            float submersion = _nav.SurfaceY - feet.y;
            if (submersion > catchSubmersion) return true;
            return _nav.IsDeep(x, z) && !_nav.Swimmable(x, z) && feet.y < _nav.FloorY(x, z) + 0.5f;
        }

        /// <summary>Ouviu este jogador há pouco (há menos de <see cref="contactSeconds"/>)? Só no host.</summary>
        public bool HasContact(PlayerNetwork player)
            => player != null && _heard.TryGetValue(player, out float at) && Time.time - at <= contactSeconds;

        void TryCatch(float now)
        {
            if (State != UndertowState.Investigate && State != UndertowState.Hunt && State != UndertowState.Circle
                && State != UndertowState.Return && State != UndertowState.Prowl) return;

            Vector3 head = Head;
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                if (_mercy.TryGetValue(player, out float until) && now < until) continue;

                Vector3 feet = player.transform.position;
                if (!Catchable(player, feet)) continue;
                Vector3 middle = feet + Vector3.up * 0.9f;
                float distance = Vector3.Distance(head, middle);
                if (distance > catchRadius) continue;
                // Cego: só pega quem ouviu há pouco, ou quem esbarra nele.
                if (!HasContact(player) && distance > bumpRadius) continue;

                Catches++;
                _mercy[player] = now + feedSeconds + mercySeconds;
                _heard.Remove(player);
                player.GetCaughtRpc();
                _target = player;
                _speed = 0f;
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
