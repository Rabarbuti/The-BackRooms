using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O som do Zelador, sintetizado na inicialização (sem arquivo), em 3D: um zelador de prédio
    /// vazio trabalhando no escuro, empurrando o balde de rodinhas. É a pista de onde ele está e do
    /// que faz antes de ser visto (refeito em 21/11/2026: as chaves eram tons limpos afinados e,
    /// com as gotas, soavam como um móbile de sininhos).
    ///
    /// • <b>Parado limpando:</b> o esfregão molhado arrastando em vaivém (laço) e, de vez em quando,
    ///   o tapa do pano no balde e a espremedeira rangendo, com a água escorrendo.
    /// • <b>Rondando:</b> botas pesadas de borracha, lentas, com um chiado molhado; um chacoalhar
    ///   seco das chaves, baixo, junto com alguns passos; o guincho do rodízio mal lubrificado, um a
    ///   cada <see cref="squeakEvery"/> metros (o ritmo diz a velocidade); o ronco baixo das rodas no
    ///   piso; o esfregão ainda arrastando enquanto vai devagar; e, sem alvo, um resmungo rouco.
    /// • <b>Vindo (caçando):</b> o rodízio guinchando depressa, as rodas roncando alto e o balde
    ///   batendo (pancada de plástico, espremedeira chacoalhando e a água).
    /// • Ao arrancar ou parar, a água chacoalha no balde.
    /// • Ao aparecer, um gorgolejo que sobe do chão; ao sumir, um sorvo para baixo.
    ///
    /// Tudo é ruído filtrado, pulsos irregulares ou parciais inarmônicos de décimos de segundo:
    /// nada de nota limpa sustentada. Roda em todas as máquinas: os passos vêm do
    /// <see cref="JanitorBody"/>, a velocidade da posição (que vai pela rede) e o aparecer/sumir do
    /// <see cref="JanitorBrain.Present"/>. Escondido, silêncio.
    /// </summary>
    [RequireComponent(typeof(JanitorBody))]
    public sealed class JanitorAudio : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.9f;
        [Tooltip("Até onde se ouvem as botas e as chaves, em metros.")]
        public float stepRange = 20f;
        [Tooltip("Até onde se ouvem o rodízio, as rodas e o balde, em metros.")]
        public float cartRange = 26f;
        [Tooltip("Até onde se ouve o esfregão arrastando, em metros.")]
        public float mopRange = 16f;
        [Tooltip("Até onde se ouve a voz dele (resmungo, pigarro) e o aparecer/sumir, em metros.")]
        public float voiceRange = 14f;
        [Tooltip("Metros de caminho entre um guincho do rodízio e o seguinte.")]
        public float squeakEvery = 0.9f;
        [Tooltip("Acima desta velocidade (m/s) ele está vindo: o balde bate.")]
        public float huntSpeed = 3f;

        /// <summary>Volume do arrasto do esfregão agora, de 0 a 1 (para os testes: sem placa de som não dá para ouvir).</summary>
        public float DragLevel { get; private set; }

        /// <summary>Quantas vezes as chaves chacoalharam (para os testes).</summary>
        public int Jingles { get; private set; }

        /// <summary>Quantas vezes o rodízio guinchou (para os testes).</summary>
        public int Squeaks { get; private set; }

        AudioSource _feet, _squeak, _bucket, _roll, _mop, _voice;
        AudioClip[] _boots, _keys, _squeaks, _sloshes, _bangs, _grumbles;
        AudioClip _wring, _rise, _sink;
        JanitorBody _body;
        JanitorBrain _brain;
        Vector3 _last;
        float _speed, _slowSpeed, _travel;
        float _sloshAt, _bangAt, _wringAt, _voiceAt;
        bool _wasPresent, _moving;

        void Awake()
        {
            _body = GetComponent<JanitorBody>();
            _brain = GetComponent<JanitorBrain>();

            _feet = Source(stepRange);
            _squeak = Source(cartRange);
            _bucket = Source(cartRange);
            _voice = Source(voiceRange);
            _roll = Loop(cartRange, CreateRoll());
            _mop = Loop(mopRange, CreateMop());

            _boots = new[] { CreateBoot(0), CreateBoot(1), CreateBoot(2) };
            _keys = new[] { CreateKeys(1), CreateKeys(2), CreateKeys(3), CreateKeys(4) };
            _squeaks = new[] { CreateSqueak(0), CreateSqueak(1), CreateSqueak(2), CreateSqueak(3) };
            _sloshes = new[] { CreateSlosh(0), CreateSlosh(1), CreateSlosh(2) };
            _bangs = new[] { CreateBang(0), CreateBang(1) };
            _grumbles = new[] { CreateGrumble(0), CreateGrumble(1), CreateGrumble(2) };
            _wring = CreateWring();
            _rise = CreateRise();
            _sink = CreateSink();
            _last = transform.position;
        }

        AudioSource Source(float range)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.5f;
            source.maxDistance = range;
            source.dopplerLevel = 0f;
            return source;
        }

        AudioSource Loop(float range, AudioClip clip)
        {
            AudioSource source = Source(range);
            source.clip = clip;
            source.loop = true;
            source.volume = 0f;
            source.Play();
            return source;
        }

        void OnEnable() { _body.Stepped += OnStepped; }
        void OnDisable() { _body.Stepped -= OnStepped; }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float now = Time.time;
            bool present = _brain == null || (_brain.IsSpawned && _brain.Present);

            if (present != _wasPresent && (_brain == null || _brain.IsSpawned))
            {
                _voice.pitch = 1f;
                _voice.PlayOneShot(present ? _rise : _sink, volume);
                _wasPresent = present;
                if (present)
                {
                    _wringAt = now + Random.Range(2f, 4f);
                    _voiceAt = now + Random.Range(5f, 12f);
                }
            }

            Vector3 moved = transform.position - _last;
            _last = transform.position;
            moved.y = 0f;
            float speed = moved.magnitude / dt;
            if (speed > 12f) speed = 0f;
            _speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-6f * dt));
            _slowSpeed = Mathf.Lerp(_slowSpeed, _speed, 1f - Mathf.Exp(-1.5f * dt));

            if (_body.Shown <= 0.5f)
            {
                DragLevel = 0f;
                _roll.volume = 0f;
                _mop.volume = 0f;
                _moving = false;
                _travel = 0f;
                return;
            }

            float fast = Mathf.Clamp01(_speed / 4.4f);

            // As rodas no piso duro: ronco baixo que cresce com a velocidade; parado, nada.
            _roll.volume = volume * 0.55f * Mathf.Clamp01(_speed / 2f);
            _roll.pitch = 0.8f + 0.6f * fast;

            // O esfregão arrasta parado e rondando devagar; apressado, ele o leva no balde.
            DragLevel = Mathf.Clamp01(1f - (_speed - 1f) / 1.2f);
            _mop.volume = volume * 0.6f * DragLevel;
            _mop.pitch = 0.9f + 0.15f * Mathf.Clamp01(_speed / 1.5f);

            // O rodízio guincha a cada tanto de caminho: o ritmo é a velocidade dele.
            if (_speed > 0.25f)
            {
                _travel += _speed * dt;
                if (_travel >= squeakEvery)
                {
                    _travel = Mathf.Min(_travel - squeakEvery, squeakEvery);
                    if (_speed > huntSpeed || Random.value > 0.12f)
                    {
                        _squeak.pitch = Random.Range(0.96f, 1.05f) + 0.08f * fast;
                        _squeak.PlayOneShot(_squeaks[Random.Range(0, _squeaks.Length)], volume * Mathf.Lerp(0.3f, 0.75f, fast));
                        Squeaks++;
                    }
                }
            }

            // Arrancou ou parou: a água chacoalha no balde.
            bool moving = _moving ? _speed > 0.25f : _speed > 0.7f;
            if (moving != _moving)
            {
                _moving = moving;
                if (now >= _sloshAt)
                {
                    _bucket.PlayOneShot(_sloshes[Random.Range(0, _sloshes.Length)], volume * 0.65f);
                    _sloshAt = now + 0.8f;
                }
                if (!moving) _wringAt = now + Random.Range(2.5f, 5f);
            }

            // Arrancada forte (ouviu algo e veio) ou caçando: o balde bate.
            bool lunge = _speed - _slowSpeed > 1.4f;
            if (_speed <= huntSpeed && !lunge) _bangAt = Mathf.Max(_bangAt, now + 0.4f);
            if (now >= _bangAt && now >= _sloshAt)
            {
                _bucket.PlayOneShot(_bangs[Random.Range(0, _bangs.Length)], volume * 0.8f);
                _bangAt = now + Random.Range(1.3f, 2.8f);
                _sloshAt = now + 0.8f;
            }

            // Parado: bate o esfregão no balde e torce.
            if (!_moving && now >= _wringAt)
            {
                _bucket.PlayOneShot(_wring, volume * 0.7f);
                _wringAt = now + Random.Range(7f, 13f);
            }

            // Sem alvo (parado ou rondando devagar), resmunga de vez em quando.
            if (_speed < 2.3f)
            {
                if (now >= _voiceAt)
                {
                    _voice.pitch = Random.Range(0.94f, 1.04f);
                    _voice.PlayOneShot(_grumbles[Random.Range(0, _grumbles.Length)], volume * 0.45f);
                    _voiceAt = now + Random.Range(12f, 24f);
                }
            }
            else _voiceAt = Mathf.Max(_voiceAt, now + 4f);
        }

        void OnStepped(float speed)
        {
            if (_body.Shown < 0.5f) return;
            float run = Mathf.InverseLerp(1.2f, 4.2f, speed);
            _feet.pitch = Random.Range(0.93f, 1.05f);
            _feet.PlayOneShot(_boots[Random.Range(0, _boots.Length)], volume * Mathf.Lerp(0.55f, 1f, run));
            // As chaves no cinto chacoalham junto com parte dos passos, baixo.
            if (Random.value < 0.55f + 0.45f * run)
            {
                int which = Mathf.Clamp(Mathf.FloorToInt(Random.value * 2f + run * 2f), 0, _keys.Length - 1);
                _feet.PlayOneShot(_keys[which], volume * Mathf.Lerp(0.18f, 0.3f, run));
                Jingles++;
            }
        }

        // ---------------------------------------------------------------- Síntese

        static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        /// <summary>Filtro biquad (receitas do RBJ), com estado; recalculável no meio sem estalo.</summary>
        struct Biquad
        {
            float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

            public static Biquad BandPass(float freq, float q, int rate) { var f = new Biquad(); f.SetBandPass(freq, q, rate); return f; }
            public static Biquad LowPass(float freq, float q, int rate) { var f = new Biquad(); f.SetLowPass(freq, q, rate); return f; }
            public static Biquad HighPass(float freq, float q, int rate) { var f = new Biquad(); f.SetHighPass(freq, q, rate); return f; }

            public void SetBandPass(float freq, float q, int rate)
            {
                float w = 2f * Mathf.PI * Mathf.Min(freq, rate * 0.45f) / rate, alpha = Mathf.Sin(w) / (2f * q), a0 = 1f + alpha;
                _b0 = alpha / a0; _b1 = 0f; _b2 = -alpha / a0; _a1 = -2f * Mathf.Cos(w) / a0; _a2 = (1f - alpha) / a0;
            }

            public void SetLowPass(float freq, float q, int rate)
            {
                float w = 2f * Mathf.PI * Mathf.Min(freq, rate * 0.45f) / rate, alpha = Mathf.Sin(w) / (2f * q), a0 = 1f + alpha, c = Mathf.Cos(w);
                _b0 = (1f - c) * 0.5f / a0; _b1 = (1f - c) / a0; _b2 = _b0; _a1 = -2f * c / a0; _a2 = (1f - alpha) / a0;
            }

            public void SetHighPass(float freq, float q, int rate)
            {
                float w = 2f * Mathf.PI * Mathf.Min(freq, rate * 0.45f) / rate, alpha = Mathf.Sin(w) / (2f * q), a0 = 1f + alpha, c = Mathf.Cos(w);
                _b0 = (1f + c) * 0.5f / a0; _b1 = -(1f + c) / a0; _b2 = _b0; _a1 = -2f * c / a0; _a2 = (1f - alpha) / a0;
            }

            public float Run(float x)
            {
                float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
                return y;
            }
        }

        /// <summary>
        /// Bota pesada de borracha: o calcanhar e a sola batendo (baque grave que cai de tom em
        /// dezenas de ms, mais ruído abafado) e o chiado molhado da borracha descolando do piso.
        /// </summary>
        public static AudioClip CreateBoot(int variant)
        {
            int rate = Rate;
            int length = (int)(rate * 0.4f);
            var samples = new float[length];
            var rng = new System.Random(3301 + variant * 17);
            var knockFilter = Biquad.LowPass(420f, 0.7f, rate);
            var squelchFilter = Biquad.BandPass(1250f + variant * 170f, 1.4f, rate);
            float toe = 0.075f + variant * 0.012f;
            float wet = 0.3f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float n = Noise(rng);
                float thump = 0f, knockEnv = 0f;
                for (int k = 0; k < 2; k++)
                {
                    float u = t - (k == 0 ? 0f : toe);
                    if (u < 0f) continue;
                    float amp = k == 0 ? 1f : 0.55f;
                    float phi = 2f * Mathf.PI * (46f * u + 34f * (1f - Mathf.Exp(-40f * u)) / 40f);
                    thump += amp * Mathf.Sin(phi) * Mathf.Exp(-u * 30f) * Mathf.Min(1f, u / 0.002f);
                    knockEnv += amp * Mathf.Exp(-u * 55f);
                }
                float knock = knockFilter.Run(n) * knockEnv;

                // Chiado molhado: ruído na banda média, picotado como a sola descolando.
                if (i % (rate / 400) == 0) wet = rng.NextDouble() < 0.25 ? 1f : 0.3f;
                float s = t - 0.02f;
                float squelchEnv = s > 0f ? (1f - Mathf.Exp(-s * 60f)) * Mathf.Exp(-s * 22f) : 0f;
                float squelch = squelchFilter.Run(n) * squelchEnv * wet;

                samples[i] = thump * 0.9f + knock * 1.4f + squelch * 0.6f;
            }
            return ToClip("Zelador_Bota_" + variant, samples, rate, 0.8f);
        }

        /// <summary>
        /// Chaves chacoalhando no cinto: <paramref name="hits"/> + 3 batidas secas em pouco mais de
        /// um décimo de segundo, cada uma com uma dúzia de parciais inarmônicos sorteados (1,7 a
        /// 9,7 kHz) que somem em 6 a 22 ms e um estalo de ruído. Nenhuma nota fica soando.
        /// </summary>
        public static AudioClip CreateKeys(int hits)
        {
            int rate = Rate;
            int length = (int)(rate * 0.28f);
            var samples = new float[length];
            var rng = new System.Random(4127 + hits * 31);
            const int partials = 12;
            var freq = new float[partials];
            var amp = new float[partials];
            var decay = new float[partials];
            var phase = new float[partials];

            for (int h = 0; h < hits + 3; h++)
            {
                float start = (float)rng.NextDouble() * 0.11f;
                float hitAmp = 0.4f + 0.6f * (float)rng.NextDouble();
                for (int p = 0; p < partials; p++)
                {
                    freq[p] = 1700f + (float)rng.NextDouble() * 8000f;
                    float r = (float)rng.NextDouble();
                    amp[p] = r * r;
                    decay[p] = 45f + (float)rng.NextDouble() * 120f;
                    phase[p] = (float)rng.NextDouble() * 6.283f;
                }
                int from = (int)(start * rate);
                for (int i = from; i < length; i++)
                {
                    float t = (i - from) / (float)rate;
                    if (t > 0.12f) break;
                    float sum = 0f;
                    for (int p = 0; p < partials; p++)
                        sum += amp[p] * Mathf.Exp(-t * decay[p]) * Mathf.Sin(2f * Mathf.PI * freq[p] * t + phase[p]);
                    float click = Noise(rng) * Mathf.Exp(-t * 900f);
                    samples[i] += hitAmp * (sum * 0.35f + click * 0.8f);
                }
            }
            var cut = Biquad.HighPass(1200f, 0.7f, rate);
            for (int i = 0; i < length; i++) samples[i] = cut.Run(samples[i]);
            return ToClip("Zelador_Chaves_" + hits, samples, rate, 0.5f);
        }

        /// <summary>
        /// O guincho do rodízio sem graxa: atrito que prende e solta (trem de pulsos em torno de
        /// 1,45 kHz, cada período com tamanho e força um pouco diferentes), curto (0,09 a 0,14 s) e
        /// torto no tom. A variação 3 é dupla ("ii-ic").
        /// </summary>
        public static AudioClip CreateSqueak(int variant)
        {
            int rate = Rate;
            float dur = 0.09f + 0.025f * (variant % 3);
            int length = (int)(rate * (dur + 0.02f));
            var samples = new float[length];
            var rng = new System.Random(6011 + variant * 13);
            var body = Biquad.BandPass(2600f + variant * 200f, 0.9f, rate);
            var cut = Biquad.HighPass(600f, 0.7f, rate);
            // Uma roda só: o mesmo tom em todas as variações, só um pouco torto (nada de escala, que
            // virava melodia); muda o desenho, o tamanho e a aspereza.
            float[] detune = { 1f, 1.035f, 0.97f, 1.015f };
            float f0 = 1450f * detune[variant & 3];
            float rough = 0.04f + 0.015f * (variant % 2);
            float phase = 0f, jitter = 1f, shimmer = 1f;
            bool twice = variant == 3;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float u = Mathf.Clamp01(t / dur);
                float bend = twice ? 0.2f * Mathf.Sin(2f * Mathf.PI * u) : 0.18f * Mathf.Sin(Mathf.PI * u) - 0.06f * u;
                float f = f0 * (1f + bend) * jitter;
                float before = phase;
                phase += f / rate;
                if (Mathf.Floor(phase) != Mathf.Floor(before))
                {
                    jitter = 1f + rough * Noise(rng);
                    shimmer = 0.7f + 0.3f * (float)rng.NextDouble();
                }
                float frac = phase - Mathf.Floor(phase);
                float pulse = Mathf.Exp(-frac * 7f) - 0.1427f;
                float env = t > dur ? 0f : Mathf.Min(1f, t / 0.006f) * Mathf.Min(1f, (dur - t) / 0.025f);
                if (twice) env *= 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(2f * Mathf.PI * u));
                float s = (pulse * shimmer + 0.12f * Noise(rng)) * env;
                samples[i] = cut.Run(s) + 0.6f * body.Run(s);
            }
            return ToClip("Zelador_Rodizio_" + variant, samples, rate, 0.6f);
        }

        /// <summary>
        /// As rodas no piso duro: ronco grave de ruído filtrado, com um tranco a cada emenda do piso
        /// e a alça do balde trepidando. Em laço (2 s).
        /// </summary>
        public static AudioClip CreateRoll()
        {
            int rate = Rate;
            const float seconds = 2f;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            var rng = new System.Random(8123);
            var lp1 = Biquad.LowPass(160f, 0.7f, rate);
            var lp2 = Biquad.LowPass(110f, 0.7f, rate);
            var bumpFilter = Biquad.LowPass(300f, 0.8f, rate);
            var rattleFilter = Biquad.HighPass(2500f, 0.7f, rate);
            var bumps = new float[8];
            for (int k = 0; k < bumps.Length; k++) bumps[k] = k * 0.25f + 0.04f * (float)rng.NextDouble();

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float n = Noise(rng);
                float rumble = lp2.Run(lp1.Run(n)) * 4f;
                float bumpEnv = 0f;
                foreach (float b in bumps)
                {
                    float u = t - b;
                    if (u >= 0f && u < 0.15f) bumpEnv += Mathf.Exp(-u * 50f);
                }
                float bump = bumpFilter.Run(n) * bumpEnv;
                float rattle = rattleFilter.Run(n) * bumpEnv * bumpEnv * 0.15f;
                float grit = rng.NextDouble() < 0.0015 ? Noise(rng) * 0.4f : 0f;
                samples[i] = rumble + bump * 0.9f + rattle + grit;
            }
            return ToLoop("Zelador_Rodas", samples, rate, 0.6f);
        }

        /// <summary>
        /// O esfregão encharcado no chão, em vaivém: duas passadas de 1,5 s (ida mais aguda, volta
        /// mais grave) de ruído filtrado, com o chiado das fibras molhadas. Em laço (3 s); começa e
        /// termina em silêncio, então emenda sem estalo.
        /// </summary>
        public static AudioClip CreateMop()
        {
            int rate = Rate;
            const float seconds = 3f;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            var rng = new System.Random(9907);
            var forth = Biquad.BandPass(850f, 0.6f, rate);
            var back = Biquad.BandPass(620f, 0.6f, rate);
            var low = Biquad.LowPass(300f, 0.7f, rate);
            var crackle = Biquad.BandPass(2400f, 1.2f, rate);
            float mod = 1f, crackAge = 1f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float n = Noise(rng);
                float half = t / (seconds * 0.5f);
                bool second = half >= 1f;
                float frac = half - (second ? 1f : 0f);
                float stroke = Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(frac * 1.12f - 0.06f)));
                stroke = Mathf.Pow(stroke, 1.4f);
                float a = forth.Run(n), b = back.Run(n);
                float swish = second ? b : a;
                if (i % (rate / 30) == 0) mod = 0.7f + 0.3f * (float)rng.NextDouble();
                if (rng.NextDouble() < 0.004 * stroke) crackAge = 0f;
                crackAge += 1f / rate;
                float crack = crackle.Run(n) * Mathf.Exp(-crackAge * 700f);
                samples[i] = ((swish + low.Run(n) * 0.6f) * mod + crack * 0.8f) * stroke;
            }
            return ToClip("Zelador_Esfregao", samples, rate, 0.6f);
        }

        /// <summary>
        /// A água chacoalhando no balde de plástico (ao arrancar ou parar): o baque oco do balde,
        /// a água batendo de um lado ao outro, cada vez menos, e bolhas estourando.
        /// </summary>
        public static AudioClip CreateSlosh(int variant)
        {
            int rate = Rate;
            var samples = Slosh(rate, 0.9f, 5501 + variant * 29, variant * 1.7f, true);
            return ToClip("Zelador_Balde_" + variant, samples, rate, 0.7f);
        }

        static float[] Slosh(int rate, float seconds, int seed, float offset, bool knock)
        {
            int length = (int)(rate * seconds);
            var samples = new float[length];
            var rng = new System.Random(seed);
            var water = Biquad.LowPass(520f, 0.7f, rate);
            var hollow = Biquad.BandPass(260f, 1.2f, rate);
            var shell1 = Biquad.BandPass(190f, 5f, rate);
            var shell2 = Biquad.BandPass(430f, 4f, rate);
            float bubbleFreq = 0f, bubbleAge = 1f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float n = Noise(rng);
                float fade = Mathf.Min(1f, (seconds - t) / 0.08f);
                float env = (1f - Mathf.Exp(-t * 30f)) * Mathf.Exp(-t * 3.2f) * (0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 3.6f * t + offset)) * fade;
                float wave = water.Run(n) + hollow.Run(n) * 0.6f;
                if (rng.NextDouble() < 0.0025 * env)
                {
                    bubbleFreq = 250f + (float)rng.NextDouble() * 650f;
                    bubbleAge = 0f;
                }
                bubbleAge += 1f / rate;
                float bubble = Mathf.Sin(2f * Mathf.PI * bubbleFreq * (1f + bubbleAge * 8f) * bubbleAge) * Mathf.Exp(-bubbleAge * 45f);
                float shell = knock ? (shell1.Run(n) + shell2.Run(n) * 0.7f) * Mathf.Exp(-t * 45f) * 4f : 0f;
                samples[i] = wave * env + bubble * 0.35f * fade + shell;
            }
            return samples;
        }

        /// <summary>
        /// O balde batendo (caçando ou numa arrancada): a pancada oca de plástico, o segundo tranco
        /// das rodas assentando, a espremedeira de metal chacoalhando e a água.
        /// </summary>
        public static AudioClip CreateBang(int variant)
        {
            int rate = Rate;
            int length = (int)(rate * 1f);
            var samples = new float[length];
            var rng = new System.Random(7307 + variant * 41);
            var low = Biquad.BandPass(140f, 3f, rate);
            var mid = Biquad.BandPass(310f, 4f, rate);
            float second = 0.06f + variant * 0.03f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float n = Noise(rng);
                float env = Mathf.Exp(-t * 25f) * Mathf.Min(1f, t / 0.002f);
                float u = t - second;
                if (u > 0f) env += 0.5f * Mathf.Exp(-u * 30f);
                samples[i] = (low.Run(n) * 1.2f + mid.Run(n)) * env * 3f;
            }

            // A espremedeira: batidas de metal mais graves que as chaves, inarmônicas e curtas.
            const int partials = 10;
            for (int h = 0; h < 3; h++)
            {
                int from = (int)((0.005f + (float)rng.NextDouble() * 0.08f) * rate);
                var freq = new float[partials];
                var decay = new float[partials];
                for (int p = 0; p < partials; p++)
                {
                    freq[p] = 600f + (float)rng.NextDouble() * 3400f;
                    decay[p] = 25f + (float)rng.NextDouble() * 40f;
                }
                for (int i = from; i < length; i++)
                {
                    float t = (i - from) / (float)rate;
                    if (t > 0.2f) break;
                    float sum = 0f;
                    for (int p = 0; p < partials; p++) sum += Mathf.Exp(-t * decay[p]) * Mathf.Sin(2f * Mathf.PI * freq[p] * t);
                    samples[i] += (sum * 0.06f + Noise(rng) * Mathf.Exp(-t * 600f) * 0.4f) * (0.6f + 0.4f * h / 2f);
                }
            }

            float[] slosh = Slosh(rate, 0.95f, 7311 + variant, variant * 2.1f, false);
            int at = (int)(0.02f * rate);
            for (int i = 0; i < slosh.Length && at + i < length; i++) samples[at + i] += slosh[i] * 0.8f;
            return ToClip("Zelador_Balde_Bate_" + variant, samples, rate, 0.8f);
        }

        /// <summary>
        /// Parado, ele bate o esfregão no balde e torce: o tapa molhado do pano, a espremedeira
        /// rangendo aos trancos e a água escorrendo de volta no balde.
        /// </summary>
        public static AudioClip CreateWring()
        {
            int rate = Rate;
            const float seconds = 1.4f;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            var rng = new System.Random(2609);
            var slapFilter = Biquad.LowPass(2500f, 0.7f, rate);
            var thudFilter = Biquad.BandPass(120f, 2f, rate);
            var creakFilter = Biquad.BandPass(800f, 3f, rate);
            var trickleFilter = Biquad.BandPass(1800f, 0.8f, rate);
            float tickAge = 1f, nextTick = 0.14f, trickleMod = 0.5f, dropFreq = 0f, dropAge = 1f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float n = Noise(rng);
                float slap = slapFilter.Run(n) * Mathf.Exp(-t * 55f) + thudFilter.Run(n) * Mathf.Exp(-t * 30f) * 3f;

                // A espremedeira: trancos irregulares de 25 a 45 por segundo, de 0,14 a 0,65 s.
                if (t >= nextTick && t < 0.65f)
                {
                    tickAge = 0f;
                    nextTick = t + 1f / (25f + 20f * (float)rng.NextDouble());
                }
                tickAge += 1f / rate;
                float creak = creakFilter.Run(n) * Mathf.Exp(-tickAge * 300f) * 2f;

                // A água escorrendo de volta e pingando.
                if (i % (rate / 60) == 0) trickleMod = 0.3f + 0.7f * (float)rng.NextDouble();
                float trickleEnv = t < 0.15f ? 0f : Mathf.Min(1f, (t - 0.15f) / 0.1f) * Mathf.Exp(-(t - 0.15f) * 2.2f);
                float trickle = trickleFilter.Run(n) * trickleMod * trickleEnv;
                if (rng.NextDouble() < 0.0008 * trickleEnv)
                {
                    dropFreq = 400f + (float)rng.NextDouble() * 800f;
                    dropAge = 0f;
                }
                dropAge += 1f / rate;
                float drop = Mathf.Sin(2f * Mathf.PI * dropFreq * (1f + dropAge * 10f) * dropAge) * Mathf.Exp(-dropAge * 70f);

                samples[i] = (slap + creak + trickle * 0.7f + drop * 0.2f) * Mathf.Min(1f, (seconds - t) / 0.05f);
            }
            return ToClip("Zelador_Torce", samples, rate, 0.75f);
        }

        /// <summary>
        /// A voz, grave e rouca, quando ronda sem alvo: 0, um "hmm-hrm" de boca fechada abrindo; 1,
        /// um resmungo de sílabas sem palavra; 2, um pigarro. Pulsos de glote a ~80 Hz com
        /// irregularidade, muito sopro e formantes de vogal escura que se mexem.
        /// </summary>
        public static AudioClip CreateGrumble(int variant)
        {
            int rate = Rate;
            float seconds = variant == 2 ? 0.9f : 1.4f;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            var rng = new System.Random(1187 + variant * 53);
            var f1 = new Biquad();
            var f2 = new Biquad();
            var f3 = new Biquad();
            var closed = Biquad.LowPass(400f, 0.7f, rate);
            float f0 = 80f + variant * 5f;
            float phase = 0f, jitter = 1f, shimmer = 1f, previous = 0f, rattle = 1f;
            int syllables = variant == 1 ? 4 : 2;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float u = t / seconds;

                // Sílabas: uma lombada de volume para cada uma; a boca abre e fecha junto.
                float syll = u * syllables;
                float k = syll - Mathf.Floor(syll);
                float bump = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * k)), 0.7f);
                float open = variant == 0 ? Mathf.Clamp01((u - 0.35f) * 2.5f) * bump : bump;
                float amp = bump * Mathf.Min(1f, t / 0.04f) * Mathf.Min(1f, (seconds - t) / 0.08f);
                if (variant != 2 && Mathf.FloorToInt(syll) == syllables - 1) amp *= 0.7f;

                if (i % 64 == 0)
                {
                    float vowel = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * (1.3f + variant * 0.4f) * t + variant);
                    f1.SetBandPass(Mathf.Lerp(420f, 620f, vowel * open + 0.1f), 5f, rate);
                    f2.SetBandPass(Mathf.Lerp(850f, 1250f, vowel), 7f, rate);
                    f3.SetBandPass(2450f, 10f, rate);
                }

                // Glote: pulso de Rosenberg derivado, com o período e a força tortos a cada ciclo.
                float pitch = f0 * (1f - 0.12f * u) * jitter;
                if (variant == 2) pitch *= 0.55f;
                float before = phase;
                phase += pitch / rate;
                if (Mathf.Floor(phase) != Mathf.Floor(before))
                {
                    jitter = 1f + 0.03f * Noise(rng);
                    shimmer = 0.75f + 0.25f * (float)rng.NextDouble();
                    rattle = variant == 2 ? (rng.NextDouble() < 0.5 ? 1f : 0.2f) : 1f;
                }
                float frac = phase - Mathf.Floor(phase);
                float glottis = frac < 0.4f ? 0.5f * (1f - Mathf.Cos(Mathf.PI * frac / 0.4f))
                              : frac < 0.55f ? Mathf.Cos(0.5f * Mathf.PI * (frac - 0.4f) / 0.15f) : 0f;
                float pulse = (glottis - previous) * rate / pitch * 0.1f * shimmer * rattle;
                previous = glottis;

                float breath = Noise(rng) * (frac < 0.55f ? 0.6f : 0.25f) * (variant == 2 ? 1.6f : 0.7f);
                float source = pulse * (variant == 2 ? 0.5f : 1f) + breath;
                float voiced = f1.Run(source) + 0.5f * f2.Run(source) + 0.2f * f3.Run(source);
                float hum = closed.Run(pulse + breath * 0.3f);
                samples[i] = Mathf.Lerp(hum * 0.8f, voiced * 2.5f, open) * amp;
            }
            return ToClip("Zelador_Voz_" + variant, samples, rate, 0.7f);
        }

        /// <summary>Gorgolejo que sobe: água borbulhando cada vez mais alto e mais agudo, 1,2 s.</summary>
        public static AudioClip CreateRise() => Gurgle("Zelador_Sobe", 1.2f, true, 2213);

        /// <summary>Sorvo para baixo: o gorgolejo ao contrário, descendo e sumindo, 0,8 s.</summary>
        public static AudioClip CreateSink() => Gurgle("Zelador_Afunda", 0.8f, false, 7741);

        static AudioClip Gurgle(string name, float seconds, bool rising, int seed)
        {
            int rate = Rate;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            var rng = new System.Random(seed);
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * 400f / rate);
            float low = 0f;
            float bubbleFreq = 0f, bubbleAge = 1f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float u = t / seconds;
                float k = rising ? u : 1f - u;
                low += ((float)(rng.NextDouble() * 2.0 - 1.0) - low) * a;
                if (rng.NextDouble() < (0.002 + 0.006 * k))
                {
                    bubbleFreq = 160f + 500f * k + (float)rng.NextDouble() * 200f;
                    bubbleAge = 0f;
                }
                bubbleAge += 1f / rate;
                float bubble = Mathf.Sin(2f * Mathf.PI * bubbleFreq * (1f + bubbleAge * 6f) * bubbleAge) * Mathf.Exp(-bubbleAge * 30f);
                float env = rising ? Mathf.Clamp01(u * 1.4f) * Mathf.Clamp01((1f - u) * 8f) : Mathf.Clamp01(u * 20f) * (1f - u);
                samples[i] = (low * 0.9f + bubble * 0.7f) * env;
            }
            return ToClip(name, samples, rate, 0.8f);
        }

        // Laço: funde o fim no começo para emendar sem estalo.
        static AudioClip ToLoop(string name, float[] samples, int rate, float peakLevel)
        {
            int fade = rate / 20;
            int length = samples.Length;
            for (int i = 0; i < fade; i++)
            {
                float u = i / (float)fade;
                samples[i] = samples[i] * u + samples[length - fade + i] * (1f - u);
            }
            System.Array.Resize(ref samples, length - fade);
            return ToClip(name, samples, rate, peakLevel);
        }

        static AudioClip ToClip(string name, float[] samples, int rate, float peakLevel)
        {
            float peak = 0f;
            foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            if (peak > 0f)
            {
                float gain = peakLevel / peak;
                for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
