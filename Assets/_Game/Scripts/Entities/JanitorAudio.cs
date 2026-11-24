using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O som do Zelador, sintetizado na inicialização (sem arquivo), em 3D, em duas partes:
    ///
    /// • a <b>vassoura</b> passando no piso, seca, uma passada a cada passada dele (dois passos). É a
    ///   pista de onde ele anda: o ritmo diz a velocidade (devagar rondando, aos trancos depressa
    ///   caçando) e o volume, a distância. Parado ou escondido, silêncio;
    /// • a <b>voz de caça</b>: um rosnado rouco e grave quando ele começa a correr atrás de alguém
    ///   (estado <see cref="EntityState.Chase"/>), repetido a cada 3 a 5 s enquanto a caça dura. Cala
    ///   quando ele perde o jogador de vista, pega ou some. É o aviso "ele está vindo atrás de você".
    ///
    /// Desde 23/11/2026 só esses dois. Saíram as botas, as chaves, o rodízio e as rodas do balde, o
    /// esfregão molhado, a água e as borbulhas do balde, a espremedeira, o resmungo de quando rondava e
    /// o gorgolejo de aparecer e sumir.
    ///
    /// Ruído filtrado em faixa, com o envelope antes do filtro e Q baixo: nada de tom afinado nem
    /// glissando. Roda em todas as máquinas: os passos vêm do <see cref="JanitorBody"/> (pela posição,
    /// que vai pela rede); o estado e a presença, do <see cref="JanitorBrain"/> (NetworkVariables).
    /// </summary>
    [RequireComponent(typeof(JanitorBody))]
    public sealed class JanitorAudio : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.9f;
        [Tooltip("Até onde se ouve a vassoura, em metros (as botas iam a 20 m, o rodízio a 26 m).")]
        public float range = 24f;
        [Tooltip("Velocidade (m/s) em que a passada fica no máximo: mais curta, mais forte e mais aguda.")]
        public float fullSpeed = 4.2f;

        [Tooltip("Até onde se ouve a voz de caça, em metros (um pouco além da vassoura).")]
        public float voiceRange = 30f;
        [Tooltip("Intervalo, em segundos, entre um rosnado e o seguinte enquanto ele caça.")]
        public Vector2 growlEvery = new Vector2(3f, 5f);

        /// <summary>Quantas passadas de vassoura tocaram (para os testes: sem placa de som não dá para ouvir).</summary>
        public int Sweeps { get; private set; }

        /// <summary>Quantos rosnados de caça tocaram (para os testes).</summary>
        public int Growls { get; private set; }

        /// <summary>Caçando agora, pela conta do som (estado Chase, presente e à vista).</summary>
        public bool Hunting => _hunting;

        AudioSource _broom, _voice;
        AudioClip[] _sweeps, _growls;
        JanitorBody _body;
        JanitorBrain _brain;
        bool _otherFoot, _hunting;
        float _growlAt, _lastGrowl = -100f;
        int _lastVariant = -1;

        void Awake()
        {
            _body = GetComponent<JanitorBody>();
            _brain = GetComponent<JanitorBrain>();
            _broom = Source(1.5f, range);
            _voice = Source(2f, voiceRange);
            _sweeps = new[] { CreateSweep(0), CreateSweep(1), CreateSweep(2), CreateSweep(3) };
            _growls = new[] { CreateGrowl(0), CreateGrowl(1), CreateGrowl(2), CreateGrowl(3) };
        }

        AudioSource Source(float min, float max)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = min;
            source.maxDistance = max;
            source.dopplerLevel = 0f;
            return source;
        }

        void OnEnable() { _body.Stepped += OnStepped; }
        void OnDisable() { _body.Stepped -= OnStepped; }

        void Update()
        {
            float dt = Time.deltaTime;
            // Sumindo (afundando no chão), a passada que ainda soa se apaga em ~0,15 s.
            bool shown = _body.Shown > 0.5f;
            _broom.volume = Mathf.MoveTowards(_broom.volume, shown ? 1f : 0f, dt * 7f);

            // A voz de caça: só correndo atrás de alguém (Chase), presente e à vista.
            bool alive = _brain != null && _brain.IsSpawned;
            var state = alive ? _brain.State : EntityState.Roam;
            bool hunting = shown && alive && _brain.Present && state == EntityState.Chase;
            float now = Time.time;
            if (hunting && !_hunting)
            {
                // Começou a caçar: rosna já. Despistado numa quina e achado de novo logo depois, espera
                // um pouco, para não virar metralhadora.
                _voice.volume = 1f;
                _growlAt = Mathf.Max(now, _lastGrowl + 1.8f);
            }
            _hunting = hunting;
            if (!hunting)
            {
                // Pegou ou sumiu: cala depressa; perdeu de vista (vasculhando): o rosnado morre em ~0,4 s.
                bool cut = !shown || !alive || !_brain.Present || state == EntityState.Feed;
                _voice.volume = Mathf.MoveTowards(_voice.volume, 0f, dt * (cut ? 9f : 2.5f));
                return;
            }
            if (now < _growlAt) return;

            // O primeiro da caça é o de ataque mais forte (1); os outros, sorteados sem repetir o anterior.
            bool first = now - _lastGrowl > growlEvery.y + 2f;
            int variant = first ? 1 : Random.Range(0, _growls.Length);
            if (!first && variant == _lastVariant) variant = (variant + 1 + Random.Range(0, _growls.Length - 1)) % _growls.Length;
            _lastVariant = variant;
            _voice.pitch = Random.Range(0.93f, 1.05f);
            _voice.PlayOneShot(_growls[variant], volume * (first ? 1f : 0.85f));
            _lastGrowl = now;
            _growlAt = now + Random.Range(growlEvery.x, growlEvery.y);
            Growls++;
        }

        void OnStepped(float speed)
        {
            if (_body.Shown < 0.5f) return;
            // Uma passada por passada inteira (o corpo avisa a cada pé): por pé, as passadas de ~0,5 s
            // se emendavam num chiado contínuo e o ritmo, que é a pista, sumia.
            _otherFoot = !_otherFoot;
            if (_otherFoot) return;
            float run = Mathf.InverseLerp(1.2f, fullSpeed, speed);
            // Depressa, a passada encurta (tocada mais rápida): o ritmo acompanha o passo.
            _broom.pitch = Random.Range(0.94f, 1.06f) * Mathf.Lerp(1f, 1.35f, run);
            _broom.PlayOneShot(_sweeps[Random.Range(0, _sweeps.Length)], volume * Mathf.Lerp(0.6f, 1f, run));
            Sweeps++;
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
        /// Uma passada de vassoura seca no piso duro ("xxxsh"): ruído com aspereza de cerdas (o volume
        /// treme em grãos de poucos ms), entrada suave de ~0,1 s e cauda de ~0,2 s, em duas faixas
        /// largas (a raspada aguda e o corpo médio) e um toque grave e abafado da vassoura assentando.
        /// O envelope vem antes dos filtros, todos de Q baixo. Variações 0 a 3: tamanho, cor e se é
        /// empurrada (entra mais firme) ou puxada (entra mais devagar).
        /// </summary>
        public static AudioClip CreateSweep(int variant)
        {
            int rate = Rate;
            float[] durations = { 0.46f, 0.54f, 0.42f, 0.5f };
            float dur = durations[variant & 3];
            int length = (int)(rate * (dur + 0.03f));
            var samples = new float[length];
            var rng = new System.Random(9907 + variant * 37);
            bool pull = (variant & 1) == 1;
            float attack = pull ? 0.14f : 0.07f;
            float release = 0.2f + 0.03f * (variant % 3);
            var scratchHp = Biquad.HighPass(1800f + 250f * variant, 0.6f, rate);
            var scratchLp = Biquad.LowPass(7500f - 400f * variant, 0.6f, rate);
            var bodyBand = Biquad.BandPass(1300f + 150f * (variant % 2), 0.55f, rate);
            var touch = Biquad.LowPass(380f, 0.6f, rate);
            float grain = 1f;
            int grainLeft = 0;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float n = Noise(rng);

                // Envelope: sobe em curva suave, segura e cai devagar (sem corte seco).
                float rise = Mathf.Clamp01(t / attack);
                float fall = Mathf.Clamp01((dur - t) / release);
                float env = rise * rise * (3f - 2f * rise) * fall * fall * (3f - 2f * fall);
                // A raspada perde força ao longo da passada (a vassoura alivia no fim).
                float bright = 1f - 0.35f * Mathf.Clamp01(t / dur);

                // Aspereza das cerdas: o volume muda em grãos de 2 a 8 ms, sem periodicidade.
                if (grainLeft-- <= 0)
                {
                    grain = 0.55f + 0.45f * (float)rng.NextDouble();
                    grainLeft = (int)(rate * (0.002f + 0.006f * (float)rng.NextDouble()));
                }

                float x = n * env * grain;
                float scratch = scratchLp.Run(scratchHp.Run(x * bright));
                float mid = bodyBand.Run(x);
                float tap = touch.Run(n * Mathf.Exp(-t * 40f) * Mathf.Min(1f, t / 0.004f) * (pull ? 0.5f : 1f));
                samples[i] = scratch * 1.1f + mid * 0.7f + tap * 0.5f;
            }
            return ToClip("Zelador_Vassoura_" + variant, samples, rate, 0.7f);
        }

        /// <summary>
        /// O rosnado de caça: um "hrrrrRRRhh" rouco e grave de garganta, de 0,7 a 1,3 s. É sopro (ruído)
        /// cortado em pulsos irregulares, de 33 a 50 por segundo, como uma garganta raspando: o intervalo e
        /// a força mudam a cada pulso e às vezes um falha, então não há nota, só aspereza. O envelope vem
        /// antes dos filtros: formantes largos de vogal escura (Q 1,3 a 2) e um ronco de peito abaixo de
        /// 200 Hz. A boca abre devagar ao longo do rosnado (os formantes andam umas centenas de Hz em
        /// quase um segundo, sem glissando). Variações: 0, comprido e crescendo; 1, o de ataque, que entra
        /// forte e tem dois golpes ("hrr-RRAH"); 2, o mais grave, que termina num bafo; 3, curto e áspero.
        /// </summary>
        public static AudioClip CreateGrowl(int variant)
        {
            int rate = Rate;
            variant &= 3;
            float[] durations = { 1.15f, 0.95f, 1.3f, 0.7f };
            float[] pulses = { 40f, 46f, 33f, 50f };
            float dur = durations[variant];
            int length = (int)(rate * (dur + 0.05f));
            var samples = new float[length];
            var rng = new System.Random(4421 + variant * 61);
            var chest = Biquad.LowPass(variant == 2 ? 170f : 190f, 0.7f, rate);
            var f1 = new Biquad();
            var f2 = new Biquad();
            var f3 = new Biquad();
            var dark = Biquad.LowPass(3200f, 0.6f, rate);
            var breathHp = Biquad.HighPass(900f, 0.6f, rate);
            float attack = variant == 1 ? 0.04f : variant == 3 ? 0.06f : 0.14f;
            float release = variant == 2 ? 0.35f : 0.22f;
            float pulseAge = 1f, pulseAmp = 0f, nextPulse = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float u = Mathf.Clamp01(t / dur);
                float n = Noise(rng);

                float rise = Mathf.Clamp01(t / attack);
                float fall = Mathf.Clamp01((dur - t) / release);
                float env = rise * rise * (3f - 2f * rise) * fall * fall * (3f - 2f * fall);
                float shape;
                switch (variant)
                {
                    case 0: shape = 0.55f + 0.45f * u; break;
                    case 1:
                        // Dois golpes: um curto e o segundo mais forte e comprido.
                        float a = u < 0.33f ? 0.8f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * u / 0.33f)), 0.5f) : 0f;
                        float b = u > 0.28f ? Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * (u - 0.28f) / 0.72f)), 0.4f) : 0f;
                        shape = Mathf.Max(a, b);
                        break;
                    case 2: shape = 0.45f + 0.55f * Mathf.Sin(0.5f * Mathf.PI * Mathf.Min(1f, u * 1.4f)); break;
                    default: shape = 1f - 0.35f * u; break;
                }
                float amp = env * shape;

                // A garganta: pulsos de ~4,5 ms em intervalos tortos; de vez em quando um falha.
                if (t >= nextPulse)
                {
                    nextPulse = t + 1f / (pulses[variant] * (1f + 0.3f * Noise(rng)));
                    pulseAge = 0f;
                    pulseAmp = 0.5f + 0.5f * (float)rng.NextDouble();
                    if (rng.NextDouble() < 0.12) pulseAmp *= 0.2f;
                }
                float gate = pulseAmp * Mathf.Exp(-pulseAge * 220f);
                pulseAge += 1f / rate;
                float x = n * amp * (0.3f + 0.9f * gate);

                if (i % 64 == 0)
                {
                    float open = Mathf.Clamp01(u * 1.3f) * (variant == 3 ? 1f : 0.8f) + (variant == 1 ? 0.2f : 0f);
                    f1.SetBandPass(Mathf.Lerp(330f, 520f, open), 1.3f, rate);
                    f2.SetBandPass(Mathf.Lerp(820f, 1080f, open), 1.6f, rate);
                    f3.SetBandPass(2300f, 2f, rate);
                }
                float body = chest.Run(x) * 2.2f;
                float voice = f1.Run(x) * 1.2f + f2.Run(x) * 0.6f + f3.Run(x) * 0.2f;
                // O mais grave termina soltando o ar (bafo rouco, sem a garganta).
                float breath = variant == 2 ? breathHp.Run(n * Mathf.Clamp01((u - 0.7f) / 0.15f) * fall * 0.25f) : 0f;
                samples[i] = dark.Run(body + voice) + breath;
            }
            return ToClip("Zelador_Voz_" + variant, samples, rate, 0.75f);
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
