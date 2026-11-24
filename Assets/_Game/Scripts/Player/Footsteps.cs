using System;
using BackRooms.World;
using UnityEngine;

namespace BackRooms.Player
{
    /// <summary>O que o jogador está pisando (ou onde está nadando), para escolher o som do passo.</summary>
    public enum StepSurface
    {
        /// <summary>Carpete do Level 0 e do Level 1 (fase sem água).</summary>
        Carpet,
        /// <summary>Azulejo seco das Piscinas.</summary>
        Tile,
        /// <summary>Andando com água nas pernas (vadeando), de pé no fundo.</summary>
        Wade,
        /// <summary>Nadando à tona, sem pé, com a cabeça fora d'água.</summary>
        Swim,
        /// <summary>Com a cabeça debaixo d'água.</summary>
        Underwater,
        /// <summary>Laminado de madeira das vitrines do Level 1: oco, um toc de tábua.</summary>
        Laminate,
        /// <summary>Piso vinílico do estoque do Level 1: estalo seco e baixo, sem rangido.</summary>
        Vinyl
    }

    /// <summary>
    /// Passos sintetizados na inicialização (sem arquivos de áudio), escolhidos pela superfície.
    ///
    /// Passo no chão seco (refeito em 23/11/2026; antes eram senos afinados que soavam como bip,
    /// e o vinílico tinha um guincho de 1,4–1,9 kHz subindo de tom): duas batidas coladas, o
    /// calcanhar (baque grave de ruído e pulso por um passa-baixas, sem altura definida) e, 20 a
    /// 110 ms depois, a planta do pé, mais fraca. Por cima, um corpo de ruído na faixa de 200 a
    /// 900 Hz (o que se ouve em caixa pequena) e o estalo do contato, com a cor de cada piso:
    /// carpete abafado (quase só grave e um roçar), laminado com modos de tábua curtos e
    /// amortecidos, vinílico seco e baixo, azulejo com estalo duro e um rabicho de sala (ruído que
    /// segue a energia do passo, sem reflexões esparsas, que soam metálicas) e o piso duro do
    /// Escritório, como o azulejo mais seco. Nada é seno sustentado. Há três conjuntos por piso,
    /// escolhidos pela força do passo: agachado (planta do pé longe e um arrasto), andando e
    /// correndo (as duas batidas quase juntas e o baque mais forte). Pé esquerdo e direito
    /// alternam com uma pequena diferença de altura e de volume.
    ///
    /// Nas Piscinas (<see cref="PoolWater.Current"/> existe) o piso é azulejo e a água decide:
    /// azulejo seco, vadeando (chapinhar), nadando à tona (braçadas, sem batida de pé) e debaixo
    /// d'água (borbulhar grave e baixo). Cruzar a linha d'água depressa — cair de uma borda, pular,
    /// entrar correndo — dá um espirro maior, uma vez. No Escritório a superfície é
    /// <see cref="StepSurface.Tile"/> (os testes contam com isso), com o som do piso duro. Os
    /// clipes são criados só na primeira vez que se precisa deles e ficam compartilhados entre
    /// todos os jogadores.
    ///
    /// Passos no chão (carpete, azulejo, vadeando) seguem o <see cref="PlayerController.Stepped"/>
    /// no dono; nadando e debaixo d'água não há chão (o evento não dispara), e a cadência vem da
    /// velocidade, como nos jogadores dos outros: o controle deles não roda nesta máquina, então
    /// a superfície deles se calcula aqui, pela altura em que estão e a água desta fase.
    ///
    /// Cada clipe é normalizado a um pico próprio da superfície (debaixo d'água é baixo já no
    /// clipe); o volume final é o campo <see cref="volume"/> vezes a força do passo.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class Footsteps : MonoBehaviour
    {
        public PlayerController controller;
        [Range(0f, 1f)] public float volume = 0.55f;

        const int Variations = 6;
        const int SplashVariations = 3;

        /// <summary>Água acima dos pés a partir da qual o passo já é na água (vadeando).</summary>
        public const float WetDepth = 0.05f;

        /// <summary>Barulho de um espirro de entrada ou saída da água, de 0 a 1.</summary>
        public const float SplashNoise = 1f;

        /// <summary>
        /// Quanto barulho cada superfície faz, de 0 a 1, para quem ouvir (o ralo das Piscinas,
        /// por exemplo): vadeando é alto, nadando é médio, debaixo d'água e no azulejo seco é
        /// baixo. O carpete é a referência (1): é o que a entidade do Level 0 já ouve, pela
        /// velocidade (<c>EntityBrain.Loudness</c>), sem passar por aqui.
        /// </summary>
        public static float NoiseOf(StepSurface surface)
        {
            switch (surface)
            {
                case StepSurface.Tile: return 0.35f;
                case StepSurface.Wade: return 1f;
                case StepSurface.Swim: return 0.6f;
                case StepSurface.Underwater: return 0.15f;
                default: return 1f;
            }
        }

        /// <summary>Superfície sob o jogador agora (atualizada a cada quadro, também parado).</summary>
        public StepSurface Surface { get; private set; } = StepSurface.Carpet;

        /// <summary>Superfície do último som de passo que tocou.</summary>
        public StepSurface LastPlayed { get; private set; } = StepSurface.Carpet;

        /// <summary>Barulho do último som (força do passo vezes <see cref="NoiseOf"/>), de 0 a 1.</summary>
        public float LastNoise { get; private set; }

        /// <summary>Quantos espirros de entrada ou saída da água tocaram.</summary>
        public int Splashes { get; private set; }

        /// <summary>
        /// Disparado a cada som de passo, braçada ou espirro: onde e quanto barulho (0 a 1). É o
        /// gancho para um inimigo que ouve a água.
        /// </summary>
        public event Action<Vector3, float> MadeNoise;

        /// <summary>Quantos sons já tocaram em cada superfície (índice = <see cref="StepSurface"/>).</summary>
        public int PlayedOn(StepSurface surface) => _played[(int)surface];

        AudioSource _source;
        // Os clipes do carpete (os três conjuntos, agachado, andando e correndo, em fila). O teste de
        // fumaça lê este campo por reflexão e confere o pico.
        AudioClip[] _clips;
        readonly int[] _played = new int[7];
        readonly int[] _lastBySurface = { -1, -1, -1, -1, -1, -1, -1 };
        readonly int[] _lastBySet = new int[Floors * Gaits];
        bool _rightFoot;
        bool _hardFloor;

        // Clipes da água, dos pisos secos (piso x passada) e do espirro, um conjunto para todos os
        // jogadores.
        static AudioClip[][] _water;
        static AudioClip[][] _ground;
        static AudioClip[] _splash;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            for (int i = 0; i < _lastBySet.Length; i++) _lastBySet[i] = -1;
            EnsureGround(Floor.Carpet);
            _clips = new AudioClip[Gaits * Variations];
            for (int g = 0; g < Gaits; g++)
                Array.Copy(_ground[(int)Floor.Carpet * Gaits + g], 0, _clips, g * Variations, Variations);

            if (controller == null) controller = GetComponent<PlayerController>();
            _lastPosition = transform.position;
        }

        void OnEnable()
        {
            if (controller != null) controller.Stepped += OnStepped;
        }

        void OnDisable()
        {
            if (controller != null) controller.Stepped -= OnStepped;
        }

        bool _remote;
        float _submersion;
        Vector3 _lastPosition;
        float _phase;
        float _smoothedSpeed;
        float _strokePhase;
        bool _wasWet;
        bool _waterKnown;
        float _splashCooldown;
        float _entryWindow;

        // Posições dos pés nos últimos ~0,3 s (uma amostra a cada 10 ms), para a velocidade
        // média do espirro: vertical e no plano.
        const int History = 32;
        const float SplashWindow = 0.12f;
        readonly float[] _historyTime = new float[History];
        readonly Vector3[] _historyPosition = new Vector3[History];
        int _historyHead, _historyCount;

        Vector3 WindowVelocity(float now, Vector3 position)
        {
            float oldestTime = now;
            Vector3 oldest = position;
            for (int k = 1; k <= _historyCount; k++)
            {
                int i = (_historyHead - k + History) % History;
                oldestTime = _historyTime[i];
                oldest = _historyPosition[i];
                if (now - oldestTime >= SplashWindow) break;
            }
            float span = now - oldestTime;
            return span >= 0.03f ? (position - oldest) / span : Vector3.zero;
        }

        /// <summary>
        /// Passos de outro jogador: o som vira 3D e a cadência é calculada pelo deslocamento,
        /// já que o controlador dele não roda nesta máquina.
        /// </summary>
        public void SetRemote()
        {
            _remote = true;
            _lastPosition = transform.position;

            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 1.5f;
            _source.maxDistance = 24f;
            _source.dopplerLevel = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 position = transform.position;
            Vector3 delta = position - _lastPosition;
            _lastPosition = position;
            // Um salto grande é teletransporte (entrada na fase, volta ao início), não corrida. Mede
            // pela distância, não pela velocidade: com quadros de duração muito diferente (em lote,
            // num engasgo) o deslocamento de um quadro dividido pelo dt do outro parece um salto.
            bool teleported = delta.magnitude > 4f;
            float verticalSpeed = delta.y / dt;
            delta.y = 0f;

            float speed = delta.magnitude / dt;
            if (teleported || speed > 12f) { speed = 0f; verticalSpeed = 0f; }
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, speed, 1f - Mathf.Exp(-10f * dt));

            PoolWater water = PoolWater.Current;
            _hardFloor = water == null && LevelBuilder.HardFloorActive;
            if (water == null)
            {
                // Fase sem água. No Escritório o piso é duro (placas de porcelanato e madeira): a
                // superfície é a do azulejo seco, com o som próprio do piso duro. Nas outras,
                // carpete, a não ser onde o Level 1 pôs laminado ou vinílico (FloorZones).
                if (_hardFloor)
                {
                    EnsureGround(Floor.Office);
                    Surface = StepSurface.Tile;
                }
                else
                {
                    FloorKind floor = FloorZones.Count > 0 ? FloorZones.At(position) : FloorKind.Carpet;
                    Surface = floor == FloorKind.Laminate ? StepSurface.Laminate : floor == FloorKind.Vinyl ? StepSurface.Vinyl : StepSurface.Carpet;
                    EnsureGround(FloorOf(Surface));
                }
                _waterKnown = false;
                if (_remote) RemoteGroundSteps(dt);
                return;
            }

            EnsureWaterClips();
            EnsureGround(Floor.Tile);
            float submersion = ReadWater(water, out bool swimming, out bool underwater);
            _submersion = submersion;
            Surface = underwater ? StepSurface.Underwater
                    : swimming ? StepSurface.Swim
                    : submersion > WetDepth ? StepSurface.Wade
                    : StepSurface.Tile;

            // A linha d'água do espirro vem da posição deste quadro (não do controle, que pode
            // estar um quadro atrasado depois de um teletransporte).
            CheckSplash(water.SurfaceY - position.y > WetDepth, teleported, position, dt);

            if (Surface == StepSurface.Swim || Surface == StepSurface.Underwater)
                Strokes(dt, Mathf.Abs(verticalSpeed));
            else if (_remote)
                RemoteGroundSteps(dt);
        }

        // Água acima dos pés. O dono lê o próprio controle; dos outros jogadores, calcula pela
        // altura dos pés e da cabeça (o pivô da câmera acompanha o agachar recebido pela rede).
        float ReadWater(PoolWater water, out bool swimming, out bool underwater)
        {
            if (!_remote && controller != null && controller.enabled)
            {
                swimming = controller.IsSwimming;
                underwater = controller.IsUnderwater;
                return controller.Submersion;
            }

            float surface = water.SurfaceY;
            float submersion = Mathf.Max(0f, surface - transform.position.y);
            float swimDepth = controller != null ? controller.swimDepth : 1.3f;
            swimming = submersion > swimDepth;

            float eye = controller != null && controller.cameraPivot != null
                ? controller.cameraPivot.position.y
                : transform.position.y + 1.65f;
            underwater = eye < surface - 0.05f;
            return submersion;
        }

        // Os pés cruzaram a linha d'água depressa (caiu, pulou, entrou ou saiu correndo): um
        // espirro maior, uma vez. As velocidades são a média dos últimos 0,12 s (pelo relógio,
        // não pelo dt de um quadro): o degrau que o CharacterController sobe de uma vez (um
        // quadro só) não conta como queda, e descer a escada andando não espirra. Entrando, a
        // queda ainda pode ganhar velocidade depois da linha (caiu de uma borda baixa na funda):
        // a janela fica aberta por 0,3 s.
        void CheckSplash(bool wet, bool teleported, Vector3 position, float dt)
        {
            _splashCooldown -= dt;
            _entryWindow -= dt;

            if (teleported) _historyCount = 0;
            float now = Time.time;
            if (_historyCount == 0 || now - _historyTime[(_historyHead + History - 1) % History] >= 0.01f)
            {
                _historyTime[_historyHead] = now;
                _historyPosition[_historyHead] = position;
                _historyHead = (_historyHead + 1) % History;
                if (_historyCount < History) _historyCount++;
            }
            Vector3 velocity = WindowVelocity(now, position);
            float vertical = velocity.y;
            float planar = new Vector2(velocity.x, velocity.z).magnitude;

            bool crossed = _waterKnown && wet != _wasWet && !teleported;
            _wasWet = wet;
            _waterKnown = true;

            bool fast = planar > 4.5f;
            if (crossed && wet) _entryWindow = 0.3f;
            bool splash = crossed && !wet ? fast || vertical > 3f
                        : _entryWindow > 0f && wet && (fast || vertical < -3f);
            if (!splash || _splashCooldown > 0f) return;

            _entryWindow = 0f;
            _splashCooldown = 0.6f;
            Splashes++;
            float force = Mathf.Clamp01(Mathf.Max(Mathf.Abs(vertical) / 6f, planar / 6f));
            AudioClip clip = _splash[UnityEngine.Random.Range(0, _splash.Length)];
            _source.pitch = UnityEngine.Random.Range(0.92f, 1.06f);
            _source.PlayOneShot(clip, volume * Mathf.Lerp(0.75f, 1f, force) * UnityEngine.Random.Range(0.92f, 1f));
            LastNoise = SplashNoise;
            MadeNoise?.Invoke(transform.position, SplashNoise);
        }

        // Nadando e debaixo d'água não há chão: braçadas (ou bolhas) pela velocidade, bem mais
        // espaçadas que os passos.
        void Strokes(float dt, float verticalSpeed)
        {
            bool moving = _smoothedSpeed > 0.3f || verticalSpeed > 0.6f;
            if (!moving) return;

            float rate = Surface == StepSurface.Swim ? 0.45f + 0.25f * _smoothedSpeed : 0.6f + 0.3f * _smoothedSpeed;
            float before = _strokePhase;
            _strokePhase += rate * dt;
            if (_strokePhase > 1000f) _strokePhase -= 1000f;
            if (Mathf.FloorToInt(_strokePhase) == Mathf.FloorToInt(before)) return;

            PlayStep(Surface, Mathf.Clamp01(_smoothedSpeed / 1.6f));
        }

        void RemoteGroundSteps(float dt)
        {
            if (_smoothedSpeed < 0.4f) return;

            float baseRate = controller != null ? controller.stepBaseRate : 1.2f;
            float perSpeed = controller != null ? controller.stepRatePerSpeed : 0.3f;

            float before = _phase;
            _phase += (baseRate + perSpeed * _smoothedSpeed) * dt;
            if (Mathf.FloorToInt(_phase) == Mathf.FloorToInt(before)) return;
            if (_phase > 1000f) _phase -= 1000f;

            // Vadeando, o passo é mais lento para o mesmo esforço (PlayerController.WadeFactor): o
            // ritmo se mede pela velocidade em seco, senão quem anda no seco alagado da maré alta
            // (1,54 m/s) soaria como quem anda agachado para quem o ouve (o Undertow, no host).
            float pace = _smoothedSpeed;
            if (Surface == StepSurface.Wade && controller != null) pace /= Mathf.Max(0.3f, controller.WadeFactor(_submersion));
            OnStepped(pace < 2f ? 0.25f : pace > 4f ? 1f : 0.55f);
        }

        void OnStepped(float loudness)
        {
            // Nadando ou debaixo d'água, quem marca o tempo são as braçadas (Strokes).
            if (Surface == StepSurface.Swim || Surface == StepSurface.Underwater) return;
            PlayStep(Surface, loudness);
        }

        void PlayStep(StepSurface surface, float loudness)
        {
            _played[(int)surface]++;
            LastPlayed = surface;
            LastNoise = loudness * NoiseOf(surface);
            MadeNoise?.Invoke(transform.position, LastNoise);

            if (IsGround(surface))
            {
                // Piso seco: o conjunto da passada (agachado, andando, correndo), sem repetir a
                // mesma variação duas vezes seguidas. Os pés alternam: o direito um pouco mais grave
                // e mais baixo, para o passo não soar como metralhadora.
                Floor floor = surface == StepSurface.Tile && _hardFloor ? Floor.Office : FloorOf(surface);
                EnsureGround(floor);
                int key = (int)floor * Gaits + GaitOf(loudness);
                AudioClip[] clips = _ground[key];
                int previous = _lastBySet[key];
                int index = UnityEngine.Random.Range(0, clips.Length - 1);
                if (index >= previous && previous >= 0) index++;
                _lastBySet[key] = index;

                _rightFoot = !_rightFoot;
                float quietest = surface == StepSurface.Carpet || surface == StepSurface.Tile ? 0.5f : 0.6f;
                _source.pitch = UnityEngine.Random.Range(0.95f, 1.05f) * (_rightFoot ? 0.97f : 1f);
                _source.PlayOneShot(clips[index], volume * Mathf.Lerp(quietest, 1f, loudness) *
                                                  UnityEngine.Random.Range(0.9f, 1f) * (_rightFoot ? 0.93f : 1f));
                return;
            }

            AudioClip[] set = _water[(int)surface];
            int last = _lastBySurface[(int)surface];
            int pick = UnityEngine.Random.Range(0, set.Length - 1);
            if (pick >= last && last >= 0) pick++;
            _lastBySurface[(int)surface] = pick;

            // Correndo na água espirra bem mais que andando.
            float quiet = surface == StepSurface.Wade ? 0.4f : 0.6f;
            _source.pitch = UnityEngine.Random.Range(0.93f, 1.07f);
            _source.PlayOneShot(set[pick], volume * Mathf.Lerp(quiet, 1f, loudness) * UnityEngine.Random.Range(0.9f, 1f));
        }

        // Sem recarregar o domínio ao entrar em Play, o cache sobraria do Play anterior.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _water = null;
            _splash = null;
            _ground = null;
        }

        static bool IsGround(StepSurface surface) =>
            surface == StepSurface.Carpet || surface == StepSurface.Tile || surface == StepSurface.Laminate || surface == StepSurface.Vinyl;

        static Floor FloorOf(StepSurface surface) =>
            surface == StepSurface.Laminate ? Floor.Laminate
            : surface == StepSurface.Vinyl ? Floor.Vinyl
            : surface == StepSurface.Tile ? Floor.Tile
            : Floor.Carpet;

        /// <summary>A passada pela força do passo: 0,25 agachado, 0,55 andando, 1 correndo.</summary>
        static int GaitOf(float loudness) => loudness < 0.4f ? 0 : loudness < 0.8f ? 1 : 2;

        static void EnsureGround(Floor floor)
        {
            if (_ground == null) _ground = new AudioClip[Floors * Gaits][];
            int first = (int)floor * Gaits;
            bool ready = true;
            for (int g = 0; g < Gaits && ready; g++)
            {
                AudioClip[] set = _ground[first + g];
                if (set == null) { ready = false; break; }
                foreach (AudioClip clip in set)
                    if (clip == null) { ready = false; break; }
            }
            if (ready) return;

            for (int g = 0; g < Gaits; g++)
            {
                var set = new AudioClip[Variations];
                for (int i = 0; i < Variations; i++) set[i] = CreateGroundStep(floor, g, i);
                _ground[first + g] = set;
            }
        }

        static void EnsureWaterClips()
        {
            if (_water != null && _splash != null && _splash[0] != null && _water[(int)StepSurface.Underwater][0] != null) return;

            _water = new AudioClip[5][];
            for (int s = (int)StepSurface.Wade; s <= (int)StepSurface.Underwater; s++)
            {
                _water[s] = new AudioClip[Variations];
                for (int i = 0; i < Variations; i++) _water[s][i] = CreateClip((StepSurface)s, i);
            }

            _splash = new AudioClip[SplashVariations];
            for (int i = 0; i < SplashVariations; i++) _splash[i] = CreateSplash(i);
        }

        // ---------------------------------------------------------------- Síntese

        /// <summary>
        /// Um clipe de passo da superfície pedida (variação 0 a 5), o mesmo que toca no jogo. Nos
        /// pisos secos, <paramref name="loudness"/> escolhe a passada como no jogo (0,25 agachado,
        /// 0,55 andando, 1 correndo); na água não muda nada. Usado também para exportar os sons em
        /// WAV.
        /// </summary>
        public static AudioClip CreateClip(StepSurface surface, int variation, float loudness = 0.55f)
        {
            if (IsGround(surface)) return CreateGroundStep(FloorOf(surface), GaitOf(loudness), variation);

            var rng = new System.Random(7919 + 101 * (int)surface + variation);
            switch (surface)
            {
                case StepSurface.Wade: return CreateWade(rng, variation);
                case StepSurface.Swim: return CreateSwim(rng, variation);
                default: return CreateUnderwater(rng, variation);
            }
        }

        /// <summary>
        /// Um clipe de passo no piso duro do Escritório (a superfície lá é <see cref="StepSurface.Tile"/>,
        /// mas o som é este, mais seco que o azulejo das Piscinas). Para exportar em WAV.
        /// </summary>
        public static AudioClip CreateOfficeClip(int variation, float loudness = 0.55f) =>
            CreateGroundStep(Floor.Office, GaitOf(loudness), variation);

        static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        // Coeficiente de um filtro de um polo para a frequência de corte dada.
        static float Cut(float frequency, int rate) => 1f - Mathf.Exp(-2f * Mathf.PI * frequency / rate);

        static float Attack(float t, float seconds) => Mathf.Clamp01(t / seconds);

        /// <summary>
        /// Filtro de dois polos (fórmulas do "Audio EQ Cookbook"): passa-baixas, passa-altas e
        /// passa-faixa com ganho 1 no centro. Com Q baixo (até ~6) e excitado por ruído curto, um
        /// passa-faixa dá cor ao som sem cantar uma nota.
        /// </summary>
        struct Biquad
        {
            float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

            static Biquad Make(float b0, float b1, float b2, float a0, float a1, float a2) =>
                new Biquad { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0, _a1 = a1 / a0, _a2 = a2 / a0 };

            static void Prepare(float frequency, float q, int rate, out float cos, out float alpha)
            {
                float w = 2f * Mathf.PI * Mathf.Clamp(frequency, 10f, rate * 0.45f) / rate;
                cos = Mathf.Cos(w);
                alpha = Mathf.Sin(w) / (2f * Mathf.Max(0.1f, q));
            }

            public static Biquad LowPass(float frequency, float q, int rate)
            {
                Prepare(frequency, q, rate, out float c, out float a);
                return Make((1f - c) * 0.5f, 1f - c, (1f - c) * 0.5f, 1f + a, -2f * c, 1f - a);
            }

            public static Biquad HighPass(float frequency, float q, int rate)
            {
                Prepare(frequency, q, rate, out float c, out float a);
                return Make((1f + c) * 0.5f, -(1f + c), (1f + c) * 0.5f, 1f + a, -2f * c, 1f - a);
            }

            public static Biquad BandPass(float frequency, float q, int rate)
            {
                Prepare(frequency, q, rate, out float c, out float a);
                return Make(a, 0f, -a, 1f + a, -2f * c, 1f - a);
            }

            public float Process(float x)
            {
                float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x;
                _y2 = _y1; _y1 = y;
                return y;
            }
        }

        // ---------------------------------------------------------------- Passos no chão

        /// <summary>Os pisos secos, para a síntese (o Escritório é azulejo para o jogo, com som próprio).</summary>
        enum Floor { Carpet, Laminate, Vinyl, Tile, Office }

        const int Floors = 5;
        /// <summary>Passadas: 0 agachado, 1 andando, 2 correndo.</summary>
        const int Gaits = 3;

        /// <summary>A cor de cada piso. Níveis relativos: cada parte é normalizada antes da mistura.</summary>
        sealed class FloorTone
        {
            public float seconds;                        // duração depois da planta do pé
            public float peak;                           // pico do clipe: o volume relativo entre pisos (RMS
                                                         // em 50 ms igual ao dos passos de antes de 23/11/2026)
            public float lowPass;                        // passa-baixas final (o carpete come o agudo)
            public float thumpCut, thump;                // baque do calcanhar: corte do passa-baixas e nível
            public float bodyFreq, bodyQ, body, bodyDecay;    // corpo de ruído em 200–900 Hz
            public float clickFreq, clickQ, click, clickDecay; // estalo do contato
            public float[] modes; public float modeQ, wood;    // tábua oca: modos curtos e amortecidos
            public float rubFreq, rub;                   // roçar (tecido, sola arrastando)
            public float room, roomDecay, roomCut;       // rabicho de sala: nível, constante da energia, corte
        }

        static readonly FloorTone[] Tones =
        {
            // Carpete: abafado e macio, quase só o baque e um roçar de tecido; nada acima de ~2 kHz.
            new FloorTone { seconds = 0.2f, peak = 0.9f, lowPass = 1700f,
                            thumpCut = 140f, thump = 1f,
                            bodyFreq = 360f, bodyQ = 0.8f, body = 0.8f, bodyDecay = 0.032f,
                            clickFreq = 750f, clickQ = 0.9f, click = 0.25f, clickDecay = 0.007f,
                            rubFreq = 850f, rub = 0.3f },
            // Laminado: o contato excita a tábua, que soa oca (modos graves curtos, Q baixo).
            new FloorTone { seconds = 0.2f, peak = 0.95f, lowPass = 7500f,
                            thumpCut = 160f, thump = 0.75f,
                            bodyFreq = 420f, bodyQ = 1f, body = 0.4f, bodyDecay = 0.02f,
                            clickFreq = 2300f, clickQ = 1f, click = 0.3f, clickDecay = 0.004f,
                            modes = new[] { 240f, 560f, 1080f }, modeQ = 5f, wood = 1f,
                            rubFreq = 1500f, rub = 0.05f },
            // Vinílico: seco e baixo, estalo curto e abafado, sem rangido.
            new FloorTone { seconds = 0.17f, peak = 0.6f, lowPass = 4200f,
                            thumpCut = 150f, thump = 0.9f,
                            bodyFreq = 520f, bodyQ = 0.9f, body = 0.6f, bodyDecay = 0.014f,
                            clickFreq = 1400f, clickQ = 1f, click = 0.45f, clickDecay = 0.003f,
                            rubFreq = 1100f, rub = 0.08f },
            // Azulejo das Piscinas: estalo duro e curto, pouco grave, e o rabicho da sala de azulejo.
            new FloorTone { seconds = 0.3f, peak = 0.62f, lowPass = 14000f,
                            thumpCut = 170f, thump = 0.5f,
                            bodyFreq = 900f, bodyQ = 1.1f, body = 0.4f, bodyDecay = 0.011f,
                            clickFreq = 3800f, clickQ = 0.8f, click = 1f, clickDecay = 0.0025f,
                            room = 0.5f, roomDecay = 0.03f, roomCut = 3200f },
            // Piso duro do Escritório: como o azulejo, mais seco (sala menor, forro de placas).
            new FloorTone { seconds = 0.22f, peak = 0.55f, lowPass = 11000f,
                            thumpCut = 160f, thump = 0.6f,
                            bodyFreq = 800f, bodyQ = 1.1f, body = 0.45f, bodyDecay = 0.011f,
                            clickFreq = 3200f, clickQ = 0.8f, click = 0.85f, clickDecay = 0.0025f,
                            room = 0.22f, roomDecay = 0.018f, roomCut = 2600f },
        };

        // Envelope de uma batida: sobe em meio cosseno até 'attack' e cai exponencial. Começa em zero.
        static float Hit(float t, float attack, float decay)
        {
            if (t <= 0f) return 0f;
            if (t < attack) return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t / attack);
            return Mathf.Exp(-(t - attack) / decay);
        }

        static void Normalize(float[] samples)
        {
            float peak = 0f;
            foreach (float v in samples) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak <= 0f) return;
            for (int i = 0; i < samples.Length; i++) samples[i] /= peak;
        }

        /// <summary>
        /// Um passo no piso seco: calcanhar e planta do pé. O baque é um pulso curto (e um pouco de
        /// ruído) num passa-baixas de dois polos: peso sem altura definida. O corpo e o estalo são
        /// ruído em faixa com envelope curto; a tábua do laminado, o estalo passando por modos de Q
        /// baixo; o rabicho de sala, ruído que segue a energia do passo seco. Cada passada muda o
        /// atraso da planta do pé, a força do baque e o arrasto. A seed é por piso, passada e
        /// variação: o clipe exportado é o mesmo do jogo.
        /// </summary>
        static AudioClip CreateGroundStep(Floor floor, int gait, int variation)
        {
            FloorTone tone = Tones[(int)floor];
            var rng = new System.Random(7919 + 1000 * (int)floor + 100 * gait + variation);
            int rate = Rate;

            // A passada: agachado, a planta do pé vem bem depois e o pé arrasta; correndo, as duas
            // batidas quase se juntam e o calcanhar bate mais forte.
            float toeDelay, toeLevel, heelAttack, toeAttack, thumpScale, clickScale, rubScale, decayScale;
            switch (gait)
            {
                case 0:
                    toeDelay = Range(rng, 0.075f, 0.115f); toeLevel = Range(rng, 0.6f, 0.8f);
                    heelAttack = Range(rng, 0.006f, 0.009f); toeAttack = 0.008f;
                    thumpScale = 0.55f; clickScale = 0.45f; rubScale = 3f; decayScale = 1.3f;
                    break;
                case 2:
                    toeDelay = Range(rng, 0.012f, 0.025f); toeLevel = Range(rng, 0.65f, 0.85f);
                    heelAttack = Range(rng, 0.001f, 0.0015f); toeAttack = 0.0015f;
                    thumpScale = 1.4f; clickScale = 1.2f; rubScale = 0.8f; decayScale = 0.85f;
                    break;
                default:
                    toeDelay = Range(rng, 0.04f, 0.065f); toeLevel = Range(rng, 0.45f, 0.6f);
                    heelAttack = Range(rng, 0.002f, 0.003f); toeAttack = 0.003f;
                    thumpScale = 1f; clickScale = 1f; rubScale = 1f; decayScale = 1f;
                    break;
            }

            // Cada variação mexe um pouco nas cores (frequências e tempos), sem mudar o piso.
            float J(float value, float spread) => value * Range(rng, 1f - spread, 1f + spread);
            float thumpCut = J(tone.thumpCut, 0.15f);
            float bodyFreq = J(tone.bodyFreq, 0.15f), bodyDecay = J(tone.bodyDecay, 0.15f) * decayScale;
            float clickFreq = J(tone.clickFreq, 0.12f) * (gait == 0 ? 0.75f : 1f);
            float clickDecay = J(tone.clickDecay, 0.2f) * decayScale;
            float clickAttack = gait == 0 ? 0.0015f : 0.0004f;
            float toeShift = Range(rng, 1.08f, 1.25f); // a planta do pé soa um pouco mais aguda

            int length = (int)(rate * (tone.seconds + toeDelay + (gait == 0 ? 0.05f : 0f)));
            var thump = new float[length];
            var body = new float[length];
            var click = new float[length];
            var rub = new float[length];

            // Baque: pulsos de meio cosseno (o calcanhar largo, a planta mais fraca) e um sopro de
            // ruído, pelo passa-baixas e por um passa-altas em 40 Hz (sem deslocar o zero).
            float heelWidth = heelAttack * 2f + 0.002f, toeWidth = toeAttack * 2f + 0.002f;
            var thumpLow = Biquad.LowPass(thumpCut, 0.75f, rate);
            var thumpHigh = Biquad.HighPass(40f, 0.7f, rate);
            var bodyHeel = Biquad.BandPass(bodyFreq, tone.bodyQ, rate);
            var bodyToe = Biquad.BandPass(bodyFreq * toeShift, tone.bodyQ, rate);
            var clickHeel = Biquad.BandPass(clickFreq, tone.clickQ, rate);
            var clickToe = Biquad.BandPass(clickFreq * toeShift, tone.clickQ, rate);
            var rubBand = Biquad.BandPass(J(tone.rubFreq, 0.15f), 0.7f, rate);
            float rubFrom = heelAttack, rubTo = toeDelay + (gait == 0 ? 0.08f : 0.04f);

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float tt = t - toeDelay;

                float pulse = t < heelWidth ? 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * t / heelWidth) : 0f;
                if (tt > 0f && tt < toeWidth) pulse += 0.5f * toeLevel * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * tt / toeWidth));
                float puff = Noise(rng) * 0.4f * (Hit(t, heelAttack, 0.012f) + 0.5f * toeLevel * Hit(tt, toeAttack, 0.012f));
                thump[i] = thumpHigh.Process(thumpLow.Process(pulse + puff));

                // O envelope vem antes do filtro: a rajada já sai com a cor do piso (envelope depois
                // do filtro espalhava o ataque curto pelo espectro inteiro, um clique digital).
                body[i] = bodyHeel.Process(Noise(rng) * Hit(t, heelAttack, bodyDecay))
                        + bodyToe.Process(Noise(rng) * toeLevel * Hit(tt, toeAttack, bodyDecay * 0.85f));

                click[i] = clickHeel.Process(Noise(rng) * Hit(t, clickAttack, clickDecay))
                         + clickToe.Process(Noise(rng) * toeLevel * Hit(tt, clickAttack, clickDecay));

                float r = t > rubFrom && t < rubTo ? Mathf.Sin(Mathf.PI * (t - rubFrom) / (rubTo - rubFrom)) : 0f;
                rub[i] = rubBand.Process(Noise(rng) * r * r);
            }

            // Tábua oca: o estalo passa por modos de Q baixo (somem em ~30 ms, não cantam).
            float[] wood = null;
            if (tone.modes != null)
            {
                wood = new float[length];
                foreach (float mode in tone.modes)
                {
                    var resonator = Biquad.BandPass(J(mode, 0.1f), tone.modeQ, rate);
                    float weight = Range(rng, 0.6f, 1f);
                    for (int i = 0; i < length; i++) wood[i] += resonator.Process(click[i]) * weight;
                }
                Normalize(wood);
            }

            Normalize(thump);
            Normalize(body);
            Normalize(click);
            Normalize(rub);

            var samples = new float[length];
            var finalLow = Biquad.LowPass(tone.lowPass, 0.7f, rate);
            float thumpLevel = tone.thump * thumpScale, clickLevel = tone.click * clickScale;
            float woodLevel = tone.wood * clickScale, rubLevel = tone.rub * rubScale;
            for (int i = 0; i < length; i++)
            {
                float s = thump[i] * thumpLevel + body[i] * tone.body + click[i] * clickLevel + rub[i] * rubLevel;
                if (wood != null) s += wood[i] * woodLevel;
                samples[i] = finalLow.Process(s);
            }

            // Rabicho de sala: ruído (sem o grave e sem o agudo) com a amplitude de uma média móvel
            // da energia do passo seco — cresce com o passo e some devagar, sem reflexões contáveis.
            if (tone.room > 0f)
            {
                var roomLow = Biquad.LowPass(tone.roomCut, 0.7f, rate);
                var roomHigh = Biquad.HighPass(300f, 0.7f, rate);
                float keep = Mathf.Exp(-1f / (tone.roomDecay * rate));
                float energy = 0f;
                var tail = new float[length];
                for (int i = 0; i < length; i++)
                {
                    energy = energy * keep + samples[i] * samples[i] * (1f - keep);
                    tail[i] = roomHigh.Process(roomLow.Process(Noise(rng))) * Mathf.Sqrt(energy);
                }
                Normalize(tail);
                float dryPeak = 0f;
                foreach (float v in samples) dryPeak = Mathf.Max(dryPeak, Mathf.Abs(v));
                for (int i = 0; i < length; i++) samples[i] += tail[i] * tone.room * dryPeak;
            }

            // Agachado, o arrasto encorpa o clipe (RMS acima do andando): o pico desce a 0,7 para o
            // passo agachado soar mais fraco, além do volume menor que o PlayStep já dá.
            string[] gaitName = { "Agachado", "Andando", "Correndo" };
            return Finish($"Passo_{floor}_{gaitName[gait]}_{variation}", samples, rate, tone.peak * (gait == 0 ? 0.7f : 1f));
        }

        // Vadeando: um chapinhar curto — o tapa do pé na água, o chiado do espirro, o baque abafado
        // do pé no fundo e gotas que caem logo depois. Desde 23/11/2026 o baque é ruído num
        // passa-baixas (era um seno que descia de tom) e as gotas são estalinhos de ruído (eram senos
        // que subiam de tom e chilreavam).
        static AudioClip CreateWade(System.Random rng, int index)
        {
            int rate = Rate;
            int length = (int)(rate * 0.5f);
            var samples = new float[length];

            float hissLow = Cut(4500f, rate), hissHigh = Cut(350f, rate), washCut = Cut(900f, rate);
            float l1 = 0f, l2 = 0f, hh = 0f, w1 = 0f, w2 = 0f;
            var thumpLow = Biquad.LowPass(Range(rng, 100f, 130f), 0.75f, rate);
            var thumpHigh = Biquad.HighPass(40f, 0.7f, rate);
            var thumps = new float[length];
            float width = Range(rng, 0.008f, 0.012f);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float pulse = t < width ? 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * t / width) : 0f;
                thumps[i] = thumpHigh.Process(thumpLow.Process(pulse + Noise(rng) * 0.3f * Hit(t, 0.004f, 0.02f)));
            }
            Normalize(thumps);

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float noise = Noise(rng);
                l1 += (noise - l1) * hissLow;
                l2 += (l1 - l2) * hissLow;
                hh += (l2 - hh) * hissHigh;
                float hiss = l2 - hh;
                w1 += (noise - w1) * washCut;
                w2 += (w1 - w2) * washCut;

                float slap = noise * 0.5f * Attack(t, 0.001f) * Mathf.Exp(-t * 120f);
                float spray = hiss * 2.4f * Attack(t, 0.004f) * Mathf.Exp(-t * 13f);
                float wash = w2 * 3.5f * Attack(t, 0.03f) * Mathf.Exp(-t * 7f);
                samples[i] = slap + spray + wash + thumps[i] * 0.3f;
            }

            AddDrops(rng, samples, rate, rng.Next(8, 15), 0.02f, 0.36f, 900f, 3000f, 0.12f, 0.35f);
            return Finish($"Passo_Agua_{index}", samples, rate, 0.9f);
        }

        // Nadando à tona: uma braçada é água empurrada — um "slosh" mais longo, que cresce e
        // some devagar, ondulando, e umas poucas gotas no fim. Sem batida de pé.
        static AudioClip CreateSwim(System.Random rng, int index)
        {
            int rate = Rate;
            float seconds = Range(rng, 0.85f, 1f);
            int length = (int)(rate * seconds);
            var samples = new float[length];

            float low = Cut(1200f, rate), high = Cut(150f, rate), gurgleCut = Cut(300f, rate);
            float l1 = 0f, l2 = 0f, hh = 0f, g1 = 0f, g2 = 0f;
            float wobble = Range(rng, 5f, 7.5f), phase = Range(rng, 0f, 6.28f);
            float peakAt = Range(rng, 0.18f, 0.28f);

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float noise = Noise(rng);
                l1 += (noise - l1) * low;
                l2 += (l1 - l2) * low;
                hh += (l2 - hh) * high;
                g1 += (noise - g1) * gurgleCut;
                g2 += (g1 - g2) * gurgleCut;

                // Cresce até peakAt e cai devagar até o fim.
                float envelope = t < peakAt
                    ? Mathf.Sin(0.5f * Mathf.PI * t / peakAt)
                    : Mathf.Pow(Mathf.Max(0f, 1f - (t - peakAt) / (seconds - peakAt)), 1.6f);
                float slosh = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * wobble * t + phase);
                samples[i] = ((l2 - hh) * 2.6f * slosh + g2 * 4f) * envelope;
            }

            AddDroplets(rng, samples, rate, rng.Next(3, 6), seconds * 0.4f, seconds * 0.85f, 600f, 1800f, 0.06f, 0.14f);
            return Finish($"Bracada_{index}", samples, rate, 0.75f);
        }

        // Debaixo d'água: quase mudo — um ronco grave e abafado e umas bolhas graves subindo.
        static AudioClip CreateUnderwater(System.Random rng, int index)
        {
            int rate = Rate;
            const float seconds = 0.8f;
            int length = (int)(rate * seconds);
            var samples = new float[length];

            float rumbleCut = Cut(250f, rate);
            float r1 = 0f, r2 = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                r1 += (Noise(rng) - r1) * rumbleCut;
                r2 += (r1 - r2) * rumbleCut;
                samples[i] = r2 * 5f * Mathf.Sin(Mathf.PI * t / seconds);
            }

            AddDroplets(rng, samples, rate, rng.Next(4, 8), 0.05f, 0.6f, 180f, 520f, 0.3f, 0.6f, 0.04f, 0.08f, 1.6f);

            // Tudo abafado: passa-baixas de dois polos em 700 Hz.
            float muffle = Cut(700f, rate), m1 = 0f, m2 = 0f;
            for (int i = 0; i < length; i++)
            {
                m1 += (samples[i] - m1) * muffle;
                m2 += (m1 - m2) * muffle;
                samples[i] = m2;
            }

            return Finish($"Bolhas_{index}", samples, rate, 0.3f);
        }

        /// <summary>
        /// Espirro grande de entrada ou saída da água (cair de uma borda, pular, entrar correndo):
        /// o tapa na superfície, o "tchum" grave do corpo afundando, a cavidade que se fecha e uma
        /// chuva de gotas. Usado também para exportar os sons em WAV.
        /// </summary>
        public static AudioClip CreateSplash(int index)
        {
            var rng = new System.Random(4127 + index);
            int rate = Rate;
            const float seconds = 1.2f;
            int length = (int)(rate * seconds);
            var samples = new float[length];

            float slapLow = Cut(7000f, rate), slapHigh = Cut(300f, rate), bodyLow = Cut(5000f, rate), bodyHigh = Cut(250f, rate);
            float s1 = 0f, sh = 0f, b1 = 0f, b2 = 0f, bh = 0f;
            float plunge = Range(rng, 65f, 80f);
            float cavity = Range(rng, 0.1f, 0.14f);

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float noise = Noise(rng);
                s1 += (noise - s1) * slapLow;
                sh += (s1 - sh) * slapHigh;
                b1 += (noise - b1) * bodyLow;
                b2 += (b1 - b2) * bodyLow;
                bh += (b2 - bh) * bodyHigh;

                float slap = (s1 - sh) * 1.6f * Mathf.Exp(-t * 40f);
                float body = (b2 - bh) * 2.2f * Attack(t, 0.005f) * Mathf.Exp(-t * 5f);
                float pitch = plunge * (1f - 0.35f * Mathf.Min(1f, t * 3f));
                float thump = Mathf.Sin(2f * Mathf.PI * pitch * t) * 0.6f * Attack(t, 0.006f) * Mathf.Exp(-t * 12f);

                // A cavidade de ar que o corpo abriu se fecha num "blomp" que sobe de tom.
                float ct = t - cavity;
                float blomp = ct < 0f || ct > 0.12f ? 0f
                    : Mathf.Sin(2f * Mathf.PI * (180f * ct + 500f * ct * ct)) * 0.35f * Mathf.Sin(Mathf.PI * ct / 0.12f);
                samples[i] = slap + body + thump + blomp;
            }

            AddDroplets(rng, samples, rate, rng.Next(25, 36), 0.05f, 0.9f, 600f, 3500f, 0.1f, 0.4f);
            return Finish($"Espirro_{index}", samples, rate, 0.92f);
        }

        // Gotas sem tom: cada uma é um estalinho de ruído de poucos milissegundos num passa-faixa de
        // Q baixo (a cor da gota), mais densas no começo do intervalo.
        static void AddDrops(System.Random rng, float[] samples, int rate, int count, float from, float to,
                             float minFrequency, float maxFrequency, float minLevel, float maxLevel)
        {
            for (int k = 0; k < count; k++)
            {
                float u = (float)rng.NextDouble();
                float start = from + (to - from) * u * u;
                var band = Biquad.BandPass(Range(rng, minFrequency, maxFrequency), 2f, rate);
                float decay = Range(rng, 0.002f, 0.005f);
                float level = Range(rng, minLevel, maxLevel) * 3f; // o passa-faixa come uns 10 dB do ruído
                int first = (int)(start * rate);
                int count2 = (int)(0.03f * rate);
                for (int j = 0; j < count2 && first + j < samples.Length; j++)
                {
                    float t = j / (float)rate;
                    samples[first + j] += band.Process(Noise(rng) * Hit(t, 0.0005f, decay)) * level;
                }
            }
        }

        // Gotas e bolhas: senos curtos que sobem de tom e somem (o som de uma bolha que se forma
        // ao bater na água). Mais densas no começo do intervalo.
        static void AddDroplets(System.Random rng, float[] samples, int rate, int count, float from, float to,
                                float minFrequency, float maxFrequency, float minLevel, float maxLevel,
                                float minSeconds = 0.02f, float maxSeconds = 0.05f, float rise = 2f)
        {
            for (int k = 0; k < count; k++)
            {
                float u = (float)rng.NextDouble();
                float start = from + (to - from) * u * u;
                float frequency = Range(rng, minFrequency, maxFrequency);
                float duration = Range(rng, minSeconds, maxSeconds);
                float level = Range(rng, minLevel, maxLevel);
                float climb = Range(rng, rise * 0.75f, rise * 1.25f);

                int first = (int)(start * rate);
                int count2 = (int)(duration * rate);
                float phase = 0f;
                for (int j = 0; j < count2 && first + j < samples.Length; j++)
                {
                    float t = j / (float)rate;
                    float f = frequency * (1f + (climb - 1f) * t / duration);
                    phase += 2f * Mathf.PI * f / rate;
                    float envelope = Attack(t, 0.002f) * Mathf.Exp(-t * 4f / duration);
                    samples[first + j] += Mathf.Sin(phase) * level * envelope;
                }
            }
        }

        // Corta o fim com uma saída de 20 ms e normaliza para o pico pedido.
        static AudioClip Finish(string name, float[] samples, int rate, float peakTarget)
        {
            int length = samples.Length;
            float peak = 0f;
            for (int i = 0; i < length; i++)
            {
                samples[i] *= Mathf.Clamp01((length - i) / (rate * 0.02f));
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }

            if (peak > 0f)
            {
                float gain = peakTarget / peak;
                for (int i = 0; i < length; i++) samples[i] *= gain;
            }

            var clip = AudioClip.Create(name, length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
