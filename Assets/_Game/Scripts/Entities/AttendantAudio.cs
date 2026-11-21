using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O som do Ascensorista, sintetizado na inicialização, em 3D. É a principal pista dele:
    ///
    /// • a <b>bengala</b>: um "tac" seco de ponteira no piso duro a cada passo, que se ouve de longe
    ///   (<see cref="range"/>); correndo, vira um arrasto raspando o chão;
    /// • os <b>estalos</b> da boca quando ele para para escutar: cliques secos em série, como quem se
    ///   orienta pelo eco (é aí que ele está ouvindo, e é a hora de ficar quieto);
    /// • a <b>respiração</b> chiada, só de perto (<see cref="breathRange"/>).
    ///
    /// O plim e a porta do elevador são das portas (<c>ElevatorDoors</c>). Roda em todas as máquinas: os
    /// passos vêm do <see cref="AttendantBody"/> (pela posição da rede), a escuta do cérebro.
    /// </summary>
    [RequireComponent(typeof(AttendantBody))]
    public sealed class AttendantAudio : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.9f;
        [Tooltip("Até onde se ouve a bengala e os estalos, em metros.")]
        public float range = 34f;
        [Tooltip("Até onde se ouve a respiração, em metros.")]
        public float breathRange = 7f;

        const int Rate = 44100;

        /// <summary>Os sons, para exportar em WAV (EntitySounds) e ouvir sem jogar.</summary>
        public static AudioClip Tap => _tap != null ? _tap : (_tap = Make("Ascensorista_Bengala", 0.35f, TapSample()));
        public static AudioClip Scrape => _scrape != null ? _scrape : (_scrape = Make("Ascensorista_Arrasto", 0.5f, ScrapeSample()));
        public static AudioClip Clicks => _clicks != null ? _clicks : (_clicks = Make("Ascensorista_Estalos", 1.6f, ClickSample()));
        public static AudioClip Breath => _breath != null ? _breath : (_breath = Make("Ascensorista_Respiracao", 3.2f, BreathSample()));

        static AudioClip _tap, _scrape, _clicks, _breath;

        /// <summary>Quantas batidas de bengala tocaram (para os testes).</summary>
        public int Taps { get; private set; }

        /// <summary>Quantas séries de estalos tocaram (para os testes).</summary>
        public int ClickBursts { get; private set; }

        AudioSource _steps, _mouth, _breathing;
        AttendantBody _body;
        AttendantBrain _brain;
        float _nextClick;

        void Awake()
        {
            _body = GetComponent<AttendantBody>();
            _brain = GetComponent<AttendantBrain>();
            _steps = Source(range, 1.5f);
            _mouth = Source(range, 2f);
            _breathing = Source(breathRange, 1f);
            _breathing.clip = Breath;
            _breathing.loop = true;
            _breathing.volume = 0f;
            _breathing.Play();
            _body.Stepped += OnStep;
        }

        void OnDestroy()
        {
            if (_body != null) _body.Stepped -= OnStep;
        }

        AudioSource Source(float max, float min)
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

        void OnStep(float speed)
        {
            Taps++;
            if (speed > 2.2f)
            {
                _steps.pitch = Random.Range(0.9f, 1.1f);
                _steps.PlayOneShot(Scrape, volume * 0.8f);
            }
            else
            {
                _steps.pitch = Random.Range(0.94f, 1.06f);
                _steps.PlayOneShot(Tap, volume);
            }
        }

        void Update()
        {
            bool shown = _body.Shown;
            _breathing.volume = Mathf.MoveTowards(_breathing.volume, shown ? volume * 0.7f : 0f, Time.deltaTime);

            bool listening = shown && _brain != null && _brain.IsSpawned && _brain.Listening;
            if (!listening || Time.time < _nextClick) return;
            _nextClick = Time.time + Random.Range(1.6f, 2.4f);
            _mouth.pitch = Random.Range(0.92f, 1.08f);
            _mouth.PlayOneShot(Clicks, volume);
            ClickBursts++;
        }

        // ---------------------------------------------------------------- Síntese

        static AudioClip Make(string name, float seconds, System.Func<float, float> sample)
        {
            int count = (int)(seconds * Rate);
            var data = new float[count];
            float peak = 0f;
            for (int i = 0; i < count; i++)
            {
                data[i] = sample(i / (float)Rate);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            if (peak > 0f) for (int i = 0; i < count; i++) data[i] *= 0.85f / peak;
            for (int i = 0; i < Mathf.Min(count, 300); i++) data[count - 1 - i] *= i / 300f;

            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        // Ponteira de borracha dura no porcelanato: um clique agudo, um "toc" oco de alumínio e o
        // zumbido curto do tubo da bengala.
        static System.Func<float, float> TapSample()
        {
            var rng = new System.Random(31);
            return t =>
            {
                float click = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t / 0.0025f);
                float knock = Mathf.Sin(2f * Mathf.PI * 820f * t) * Mathf.Exp(-t / 0.018f);
                float ring = Mathf.Sin(2f * Mathf.PI * 2350f * t) * Mathf.Exp(-t / 0.06f) * 0.35f
                           + Mathf.Sin(2f * Mathf.PI * 3710f * t) * Mathf.Exp(-t / 0.04f) * 0.2f;
                return click * 0.9f + knock * 0.8f + ring;
            };
        }

        // A bengala arrastada no piso: ruído áspero que treme, como um risco comprido.
        static System.Func<float, float> ScrapeSample()
        {
            var rng = new System.Random(37);
            float band = 0f, low = 0f;
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * 2400f / Rate);
            float b = 1f - Mathf.Exp(-2f * Mathf.PI * 600f / Rate);
            return t =>
            {
                float n = (float)rng.NextDouble() * 2f - 1f;
                band += (n - band) * a;
                low += (band - low) * b;
                float grain = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 37f * t) * Mathf.Sin(2f * Mathf.PI * 11f * t);
                float env = Mathf.Min(1f, t / 0.03f) * Mathf.Min(1f, (0.5f - t) / 0.15f);
                return (band - low) * grain * env * 3f;
            };
        }

        // Estalos da língua, em série, cada vez mais juntos: tk… tk… tk-tk-tk.
        static System.Func<float, float> ClickSample()
        {
            var rng = new System.Random(43);
            float[] at = { 0.0f, 0.42f, 0.78f, 1.05f, 1.2f, 1.33f };
            return t =>
            {
                float s = 0f;
                for (int k = 0; k < at.Length; k++)
                {
                    float u = t - at[k];
                    if (u < 0f || u > 0.05f) continue;
                    float f = 1900f + 260f * k;
                    s += (Mathf.Sin(2f * Mathf.PI * f * u) * 0.8f + ((float)rng.NextDouble() * 2f - 1f) * 0.5f) * Mathf.Exp(-u / 0.0045f);
                }
                return s;
            };
        }

        // Respiração chiada e lenta pela boca: inspira curto, solta comprido, com um chiado molhado.
        static System.Func<float, float> BreathSample()
        {
            var rng = new System.Random(47);
            float band = 0f, low = 0f;
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * 1500f / Rate);
            float b = 1f - Mathf.Exp(-2f * Mathf.PI * 300f / Rate);
            return t =>
            {
                float n = (float)rng.NextDouble() * 2f - 1f;
                band += (n - band) * a;
                low += (band - low) * b;
                float cycle = t % 3.2f;
                float inhale = cycle < 0.9f ? Mathf.Sin(Mathf.PI * cycle / 0.9f) * 0.6f : 0f;
                float exhale = cycle >= 1.2f ? Mathf.Sin(Mathf.PI * (cycle - 1.2f) / 2.0f) : 0f;
                float rattle = 1f + 0.5f * Mathf.Sin(2f * Mathf.PI * 23f * t);
                return (band - low) * (inhale + exhale * rattle) + low * 0.3f * exhale;
            };
        }
    }
}
