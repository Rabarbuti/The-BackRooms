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
        /// <summary>Piso vinílico do estoque do Level 1: estalo seco e um rangido de borracha.</summary>
        Vinyl
    }

    /// <summary>
    /// Passos sintetizados na inicialização (sem arquivos de áudio), escolhidos pela superfície.
    ///
    /// Carpete (fase sem água): cada passo é ruído filtrado na faixa de 170 a ~700 Hz (o corpo do
    /// som, audível até em caixa pequena) mais um baque grave, em dois apoios: calcanhar e ponta
    /// do pé. Quase nada passa de 3 kHz — é o chiado agudo que faz um passo soar como areia. É o
    /// som de sempre, com a mesma seed (o teste de fumaça mede o pico desses clipes).
    ///
    /// Nas Piscinas (<see cref="PoolWater.Current"/> existe) o piso é azulejo e a água decide:
    /// azulejo seco (estalo duro e curto com um pouco de eco de sala de azulejo), vadeando
    /// (chapinhar), nadando à tona (braçadas, sem batida de pé) e debaixo d'água (borbulhar grave e
    /// baixo). Cruzar a linha d'água depressa — cair de uma borda, pular, entrar correndo — dá um
    /// espirro maior, uma vez. Esses clipes são criados só na primeira vez que se precisa deles e
    /// ficam compartilhados entre todos os jogadores.
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
        AudioClip[] _clips;
        int _last = -1;
        readonly int[] _played = new int[7];
        readonly int[] _lastBySurface = { -1, -1, -1, -1, -1, -1, -1 };

        // Clipes das Piscinas e dos pisos do Level 1, um conjunto para todos os jogadores.
        static AudioClip[][] _water;
        static AudioClip[][] _floor;
        static AudioClip[] _splash;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            _clips = new AudioClip[Variations];
            var rng = new System.Random(7919);
            for (int i = 0; i < Variations; i++) _clips[i] = CreateStep(rng, i);

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
            if (water == null)
            {
                // Fase sem água. No Escritório o piso é duro (placas de porcelanato e madeira), então
                // os passos são os do azulejo seco. Nas outras, carpete, como sempre foi, a não ser
                // onde o Level 1 pôs laminado ou vinílico (FloorZones).
                if (LevelBuilder.HardFloorActive)
                {
                    EnsureWaterClips();
                    Surface = StepSurface.Tile;
                }
                else
                {
                    FloorKind floor = FloorZones.Count > 0 ? FloorZones.At(position) : FloorKind.Carpet;
                    Surface = floor == FloorKind.Laminate ? StepSurface.Laminate : floor == FloorKind.Vinyl ? StepSurface.Vinyl : StepSurface.Carpet;
                    if (Surface != StepSurface.Carpet) EnsureFloorClips();
                }
                _waterKnown = false;
                if (_remote) RemoteGroundSteps(dt);
                return;
            }

            EnsureWaterClips();
            float submersion = ReadWater(water, out bool swimming, out bool underwater);
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

            OnStepped(_smoothedSpeed < 2f ? 0.25f : _smoothedSpeed > 4f ? 1f : 0.55f);
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

            if (surface == StepSurface.Carpet)
            {
                // Evita repetir a mesma variação duas vezes seguidas.
                int index = UnityEngine.Random.Range(0, Variations - 1);
                if (index >= _last && _last >= 0) index++;
                _last = index;

                _source.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
                _source.PlayOneShot(_clips[index], volume * Mathf.Lerp(0.5f, 1f, loudness));
                return;
            }

            AudioClip[] set = surface == StepSurface.Laminate || surface == StepSurface.Vinyl ? _floor[(int)surface] : _water[(int)surface];
            int last = _lastBySurface[(int)surface];
            int pick = UnityEngine.Random.Range(0, set.Length - 1);
            if (pick >= last && last >= 0) pick++;
            _lastBySurface[(int)surface] = pick;

            // Correndo na água espirra bem mais que andando; o azulejo varia menos.
            float quiet = surface == StepSurface.Wade ? 0.4f : surface == StepSurface.Tile ? 0.5f : 0.6f;
            _source.pitch = UnityEngine.Random.Range(0.93f, 1.07f);
            _source.PlayOneShot(set[pick], volume * Mathf.Lerp(quiet, 1f, loudness) * UnityEngine.Random.Range(0.9f, 1f));
        }

        // Sem recarregar o domínio ao entrar em Play, o cache sobraria do Play anterior.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _water = null;
            _splash = null;
            _floor = null;
        }

        static void EnsureFloorClips()
        {
            if (_floor != null && _floor[(int)StepSurface.Vinyl] != null && _floor[(int)StepSurface.Vinyl][0] != null) return;

            _floor = new AudioClip[7][];
            foreach (StepSurface surface in new[] { StepSurface.Laminate, StepSurface.Vinyl })
            {
                _floor[(int)surface] = new AudioClip[Variations];
                for (int i = 0; i < Variations; i++) _floor[(int)surface][i] = CreateClip(surface, i);
            }
        }

        static void EnsureWaterClips()
        {
            if (_water != null && _splash != null && _splash[0] != null && _water[(int)StepSurface.Underwater][0] != null) return;

            _water = new AudioClip[5][];
            for (int s = (int)StepSurface.Tile; s <= (int)StepSurface.Underwater; s++)
            {
                _water[s] = new AudioClip[Variations];
                for (int i = 0; i < Variations; i++) _water[s][i] = CreateClip((StepSurface)s, i);
            }

            _splash = new AudioClip[SplashVariations];
            for (int i = 0; i < SplashVariations; i++) _splash[i] = CreateSplash(i);
        }

        // ---------------------------------------------------------------- Síntese

        /// <summary>
        /// Um clipe de passo da superfície pedida (variação 0 a 5). O carpete sai igual ao do jogo
        /// (mesma seed, mesma ordem). Usado também para exportar os sons em WAV.
        /// </summary>
        public static AudioClip CreateClip(StepSurface surface, int variation)
        {
            if (surface == StepSurface.Carpet)
            {
                var carpet = new System.Random(7919);
                AudioClip result = null;
                for (int i = 0; i <= variation; i++)
                {
                    if (result != null) DestroyClip(result);
                    result = CreateStep(carpet, i);
                }
                return result;
            }

            var rng = new System.Random(7919 + 101 * (int)surface + variation);
            switch (surface)
            {
                case StepSurface.Tile: return CreateTile(rng, variation);
                case StepSurface.Wade: return CreateWade(rng, variation);
                case StepSurface.Swim: return CreateSwim(rng, variation);
                case StepSurface.Laminate: return CreateLaminate(rng, variation);
                case StepSurface.Vinyl: return CreateVinyl(rng, variation);
                default: return CreateUnderwater(rng, variation);
            }
        }

        static void DestroyClip(AudioClip clip)
        {
            if (Application.isPlaying) Destroy(clip);
            else DestroyImmediate(clip);
        }

        static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        // Coeficiente de um filtro de um polo para a frequência de corte dada.
        static float Cut(float frequency, int rate) => 1f - Mathf.Exp(-2f * Mathf.PI * frequency / rate);

        static float Attack(float t, float seconds) => Mathf.Clamp01(t / seconds);

        // Laminado: o salto bate na tábua e ela soa oca (dois modos graves de madeira que somem
        // depressa), com o estalo do contato por cima.
        static AudioClip CreateLaminate(System.Random rng, int variation)
        {
            int rate = Rate;
            int length = (int)(rate * 0.22f);
            var samples = new float[length];
            float f1 = Range(rng, 170f, 210f), f2 = f1 * Range(rng, 2.6f, 2.9f);
            float toeDelay = Range(rng, 0.055f, 0.07f);
            float high = 0f, prev = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float n = Noise(rng);
                high = 0.7f * (high + n - prev);
                prev = n;
                float click = high * Mathf.Exp(-t * 300f) * 0.9f;
                float wood = Mathf.Sin(2f * Mathf.PI * f1 * t) * Mathf.Exp(-t * 28f) + 0.5f * Mathf.Sin(2f * Mathf.PI * f2 * t) * Mathf.Exp(-t * 55f);
                // O calcanhar e a ponta do pé: um segundo toque mais fraco, uns 60 ms depois.
                float t2 = t - toeDelay;
                float toe = t2 > 0f ? Mathf.Sin(2f * Mathf.PI * f1 * 1.1f * t2) * Mathf.Exp(-t2 * 40f) * 0.4f : 0f;
                samples[i] = (click + wood * 0.7f + toe) * Attack(t, 0.002f);
            }
            return Clip("Passo_Laminado_" + variation, samples, rate, 0.85f);
        }

        // Vinílico: estalo seco e curto, com um rangido agudo de borracha no fim do passo.
        static AudioClip CreateVinyl(System.Random rng, int variation)
        {
            int rate = Rate;
            int length = (int)(rate * 0.2f);
            var samples = new float[length];
            float squeak = Range(rng, 1400f, 1900f);
            bool squeaks = rng.NextDouble() < 0.7;
            float low = 0f;
            float cut = Cut(2400f, rate);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                low += (Noise(rng) - low) * cut;
                float tap = low * Mathf.Exp(-t * 90f) * 1.4f + Mathf.Sin(2f * Mathf.PI * 120f * t) * Mathf.Exp(-t * 45f) * 0.5f;
                float st = t - 0.07f;
                float rub = st > 0f && squeaks ? Mathf.Sin(2f * Mathf.PI * squeak * (1f + 0.2f * st) * st) * Mathf.Exp(-st * 35f) * 0.18f : 0f;
                samples[i] = (tap + rub) * Attack(t, 0.0015f);
            }
            return Clip("Passo_Vinilico_" + variation, samples, rate, 0.7f);
        }

        static AudioClip Clip(string name, float[] samples, int rate, float peakLevel)
        {
            float peak = 0f;
            foreach (float v in samples) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 0f)
                for (int i = 0; i < samples.Length; i++) samples[i] *= peakLevel / peak;
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static AudioClip CreateStep(System.Random rng, int index)
        {
            int rate = Rate;
            int length = (int)(rate * 0.20f);
            var samples = new float[length];

            float thumpFrequency = 105f + (float)rng.NextDouble() * 30f;
            float toeDelay = 0.060f + (float)rng.NextDouble() * 0.030f;
            float toeLevel = 0.45f + (float)rng.NextDouble() * 0.20f;

            // Passa-baixas de dois polos (620 a 820 Hz) e passa-altas de um polo (170 Hz).
            float lowCut = 1f - Mathf.Exp(-2f * Mathf.PI * (620f + (float)rng.NextDouble() * 200f) / rate);
            float highCut = 1f - Mathf.Exp(-2f * Mathf.PI * 170f / rate);
            float l1 = 0f, l2 = 0f, h = 0f;
            float peak = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float tt = t - toeDelay;

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                l1 += (noise - l1) * lowCut;
                l2 += (l1 - l2) * lowCut;
                h += (l2 - h) * highCut;
                float band = l2 - h;

                // Dois apoios: calcanhar e, um instante depois, a ponta do pé (mais fraca).
                float heelEnvelope = Mathf.Exp(-t * 42f) * Mathf.Clamp01(t / 0.005f);
                float toeEnvelope = tt < 0f ? 0f : Mathf.Exp(-tt * 55f) * toeLevel * Mathf.Clamp01(tt / 0.005f);

                // Baque grave que desce de frequência.
                float thumpPitch = thumpFrequency * (1f + 0.5f * Mathf.Exp(-t * 70f));
                float thump = Mathf.Sin(2f * Mathf.PI * thumpPitch * t) * Mathf.Exp(-t * 36f) * Mathf.Clamp01(t / 0.005f);

                float release = Mathf.Clamp01((length - i) / (rate * 0.02f));
                samples[i] = (band * 5f * (heelEnvelope + toeEnvelope) + thump * 0.4f) * release;

                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }

            if (peak > 0f)
            {
                float gain = 0.9f / peak;
                for (int i = 0; i < length; i++) samples[i] *= gain;
            }

            var clip = AudioClip.Create($"Passo_{index}", length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // Azulejo seco: estalo curto e agudo (sola dura no piso duro), um "toc" ressonante, pouco
        // grave, em dois apoios; por cima, ecos esparsos e abafados de sala de azulejo.
        static AudioClip CreateTile(System.Random rng, int index)
        {
            int rate = Rate;
            int length = (int)(rate * 0.34f);
            var dry = new float[length];

            float toeDelay = Range(rng, 0.05f, 0.08f);
            float toeLevel = Range(rng, 0.5f, 0.7f);
            float tok = Range(rng, 1100f, 1500f);
            float tok2 = tok * Range(rng, 2.2f, 2.5f);
            float thumpFrequency = Range(rng, 130f, 160f);
            float clickCut = Cut(1800f, rate), bodyLow = Cut(3200f, rate), bodyHigh = Cut(600f, rate);
            float c = 0f, b1 = 0f, b2 = 0f, bh = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float noise = Noise(rng);
                c += (noise - c) * clickCut;
                float click = noise - c; // passa-altas: só o estalo
                b1 += (noise - b1) * bodyLow;
                b2 += (b1 - b2) * bodyLow;
                bh += (b2 - bh) * bodyHigh;
                float body = b2 - bh;

                float sum = 0f;
                for (int strike = 0; strike < 2; strike++)
                {
                    float st = strike == 0 ? t : t - toeDelay;
                    if (st < 0f) continue;
                    float level = strike == 0 ? 1f : toeLevel;
                    float attack = Attack(st, 0.0015f);
                    float s = click * 1.4f * Mathf.Exp(-st * 260f)
                            + body * 2.2f * Mathf.Exp(-st * 70f)
                            + Mathf.Sin(2f * Mathf.PI * tok * st) * 0.5f * Mathf.Exp(-st * 90f)
                            + Mathf.Sin(2f * Mathf.PI * tok2 * st) * 0.2f * Mathf.Exp(-st * 130f)
                            + Mathf.Sin(2f * Mathf.PI * thumpFrequency * st) * 0.3f * Mathf.Exp(-st * 55f);
                    sum += s * attack * level;
                }
                dry[i] = sum;
            }

            // Eco de sala de azulejo: reflexões esparsas que se adensam e somem em ~0,25 s,
            // passadas por um passa-baixas (as paredes comem o agudo).
            var samples = new float[length];
            Array.Copy(dry, samples, length);
            var wet = new float[length];
            const int taps = 48;
            for (int k = 0; k < taps; k++)
            {
                float delay = Range(rng, 0.008f, 0.24f);
                float gain = 0.32f * Mathf.Exp(-delay * 14f) * (rng.NextDouble() < 0.5 ? -1f : 1f);
                int offset = (int)(delay * rate);
                for (int i = offset; i < length; i++) wet[i] += dry[i - offset] * gain;
            }
            float wl = 0f, wetCut = Cut(2600f, rate);
            for (int i = 0; i < length; i++)
            {
                wl += (wet[i] - wl) * wetCut;
                samples[i] += wl;
            }

            return Finish($"Passo_Azulejo_{index}", samples, rate, 0.85f);
        }

        // Vadeando: um chapinhar curto — o tapa do pé na água, o chiado do espirro, gotas que
        // caem logo depois (pequenas bolhas que sobem de tom) e um "tchum" grave.
        static AudioClip CreateWade(System.Random rng, int index)
        {
            int rate = Rate;
            int length = (int)(rate * 0.5f);
            var samples = new float[length];

            float hissLow = Cut(4500f, rate), hissHigh = Cut(350f, rate), washCut = Cut(900f, rate);
            float l1 = 0f, l2 = 0f, hh = 0f, w1 = 0f, w2 = 0f;
            float plunge = Range(rng, 85f, 105f);

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

                float slap = noise * 0.5f * Mathf.Exp(-t * 120f);
                float spray = hiss * 2.4f * Attack(t, 0.004f) * Mathf.Exp(-t * 13f);
                float wash = w2 * 3.5f * Attack(t, 0.03f) * Mathf.Exp(-t * 7f);
                float pitch = plunge * (1f - 0.35f * Mathf.Min(1f, t * 6f));
                float thump = Mathf.Sin(2f * Mathf.PI * pitch * t) * 0.3f * Attack(t, 0.004f) * Mathf.Exp(-t * 25f);
                samples[i] = slap + spray + wash + thump;
            }

            AddDroplets(rng, samples, rate, rng.Next(8, 15), 0.02f, 0.36f, 700f, 2600f, 0.15f, 0.45f);
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
