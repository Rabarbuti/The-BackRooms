using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O som do Zelador, sintetizado na inicialização (sem arquivo), em 3D. É a pista de que ele
    /// está no escuro antes de ser visto:
    ///
    /// • o <b>molho de chaves</b> tilintando a cada passo (mais forte e mais embolado correndo);
    /// • o <b>esfregão molhado</b> arrastando no piso, um chiado encharcado contínuo que cresce com
    ///   a velocidade e quase some parado;
    /// • parado, <b>gotas</b> pingando do esfregão;
    /// • ao aparecer, um <b>gorgolejo</b> que sobe do chão; ao sumir, um <b>sorvo</b> para baixo.
    ///
    /// Roda em todas as máquinas: os passos vêm do <see cref="JanitorBody"/> e o aparecer/sumir do
    /// <see cref="JanitorBrain.Present"/>, que vai pela rede. Escondido, silêncio.
    /// </summary>
    [RequireComponent(typeof(JanitorBody))]
    public sealed class JanitorAudio : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.9f;
        [Tooltip("Até onde se ouvem as chaves, em metros.")]
        public float keysRange = 22f;
        [Tooltip("Até onde se ouve o esfregão arrastando, em metros.")]
        public float dragRange = 16f;

        /// <summary>Volume do arrasto agora, de 0 a 1 (para os testes: sem placa de som não dá para ouvir).</summary>
        public float DragLevel { get; private set; }

        /// <summary>Quantas vezes as chaves tilintaram (para os testes).</summary>
        public int Jingles { get; private set; }

        AudioSource _keys, _drag;
        AudioClip[] _jingles;
        AudioClip _drip, _rise, _sink;
        JanitorBody _body;
        JanitorBrain _brain;
        Vector3 _last;
        float _speed, _dripAt;
        bool _wasPresent;

        void Awake()
        {
            _body = GetComponent<JanitorBody>();
            _brain = GetComponent<JanitorBrain>();

            _keys = Source(keysRange);
            _drag = Source(dragRange);
            _drag.clip = CreateDrag();
            _drag.loop = true;
            _drag.volume = 0f;
            _drag.Play();

            _jingles = new[] { CreateKeys(1), CreateKeys(2), CreateKeys(3), CreateKeys(4) };
            _drip = CreateDrip();
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

        void OnEnable() { _body.Stepped += OnStepped; }
        void OnDisable() { _body.Stepped -= OnStepped; }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            bool present = _brain == null || (_brain.IsSpawned && _brain.Present);

            if (present != _wasPresent && (_brain == null || _brain.IsSpawned))
            {
                _keys.pitch = 1f;
                _keys.PlayOneShot(present ? _rise : _sink, volume);
                _wasPresent = present;
            }

            Vector3 moved = transform.position - _last;
            _last = transform.position;
            moved.y = 0f;
            float speed = moved.magnitude / dt;
            if (speed > 12f) speed = 0f;
            _speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-6f * dt));

            bool shown = _body.Shown > 0.5f;
            DragLevel = shown ? Mathf.Clamp01(0.12f + _speed / 3.5f) : 0f;
            _drag.volume = volume * 0.7f * DragLevel;
            _drag.pitch = 0.85f + Mathf.Clamp01(_speed / 4f) * 0.35f;

            // Parado, o esfregão pinga.
            if (shown && _speed < 0.3f && Time.time >= _dripAt)
            {
                _dripAt = Time.time + Random.Range(0.5f, 1.6f);
                _keys.pitch = Random.Range(0.85f, 1.2f);
                _keys.PlayOneShot(_drip, volume * 0.5f);
            }
        }

        void OnStepped(float speed)
        {
            if (_body.Shown < 0.5f) return;
            float run = Mathf.InverseLerp(1.2f, 4.2f, speed);
            int which = Mathf.Clamp(Mathf.FloorToInt(Random.value * 2f + run * 2f), 0, _jingles.Length - 1);
            _keys.pitch = Random.Range(0.94f, 1.08f);
            _keys.PlayOneShot(_jingles[which], volume * Mathf.Lerp(0.55f, 1f, run));
            Jingles++;
        }

        // ---------------------------------------------------------------- Síntese

        static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        /// <summary>
        /// Chaves batendo umas nas outras: <paramref name="hits"/> toques metálicos em menos de meio
        /// segundo, cada um com parciais inarmônicos agudos que somem depressa e um estalo seco.
        /// </summary>
        public static AudioClip CreateKeys(int hits)
        {
            int rate = Rate;
            int length = (int)(rate * 0.6f);
            var samples = new float[length];
            var rng = new System.Random(4127 + hits * 31);

            for (int h = 0; h < hits + 2; h++)
            {
                float start = (float)rng.NextDouble() * 0.32f;
                float baseFreq = 2300f + (float)rng.NextDouble() * 2600f;
                float amp = 0.5f + (float)rng.NextDouble() * 0.5f;
                int from = (int)(start * rate);
                for (int i = from; i < length; i++)
                {
                    float t = (i - from) / (float)rate;
                    float env = Mathf.Exp(-t * (38f + h * 5f));
                    if (env < 0.001f) break;
                    float ring = Mathf.Sin(2f * Mathf.PI * baseFreq * t)
                               + 0.6f * Mathf.Sin(2f * Mathf.PI * baseFreq * 2.76f * t + 0.4f)
                               + 0.35f * Mathf.Sin(2f * Mathf.PI * baseFreq * 5.4f * t + 1.1f) * Mathf.Exp(-t * 60f);
                    float click = t < 0.002f ? (float)(rng.NextDouble() * 2.0 - 1.0) : 0f;
                    samples[i] += amp * env * (ring * 0.6f + click);
                }
            }
            return ToClip("Zelador_Chaves_" + hits, samples, rate, 0.75f);
        }

        /// <summary>
        /// O esfregão encharcado arrastando: ruído grave e médio filtrado, com o "chape" da água
        /// pegando e soltando no piso. Em laço (2 s).
        /// </summary>
        public static AudioClip CreateDrag()
        {
            int rate = Rate;
            const float seconds = 2f;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            var rng = new System.Random(9907);
            float a1 = 1f - Mathf.Exp(-2f * Mathf.PI * 900f / rate);
            float a2 = 1f - Mathf.Exp(-2f * Mathf.PI * 120f / rate);
            float low = 0f, low2 = 0f, high = 0f, squelch = 0.6f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                low += ((float)(rng.NextDouble() * 2.0 - 1.0) - low) * a1;
                low2 += (low - low2) * a1;
                high += (low2 - high) * a2;
                float band = low2 - high;
                if (i % (rate / 9) == 0) squelch = 0.45f + 0.55f * (float)rng.NextDouble();
                // Bolhas de água estourando sob o pano.
                float bubble = Mathf.Sin(2f * Mathf.PI * (180f + 120f * Mathf.Sin(t * 7f)) * t) * Mathf.Max(0f, Mathf.Sin(t * 23f)) * 0.08f;
                float wobble = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * t / seconds * 2f);
                samples[i] = (band * squelch + bubble) * wobble;
            }
            // Emenda do laço sem estalo.
            int fade = rate / 20;
            for (int i = 0; i < fade; i++)
            {
                float u = i / (float)fade;
                samples[i] = samples[i] * u + samples[length - fade + i] * (1f - u);
            }
            System.Array.Resize(ref samples, length - fade);
            return ToClip("Zelador_Esfregao", samples, rate, 0.6f);
        }

        /// <summary>Uma gota caindo numa poça: "plic" agudo que desce de tom.</summary>
        public static AudioClip CreateDrip()
        {
            int rate = Rate;
            int length = (int)(rate * 0.18f);
            var samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float f = 1500f - 900f * Mathf.Clamp01(t / 0.05f);
                samples[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 45f) * Mathf.Clamp01(t / 0.001f);
            }
            return ToClip("Zelador_Gota", samples, rate, 0.6f);
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
