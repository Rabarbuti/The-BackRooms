using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O som do Undertow, sintetizado na inicialização (sem arquivos), em 3D. É o principal aviso:
    ///
    /// • <b>Gemido</b> (acordando ou surgindo de um cano): um gemido grave, abafado pela água,
    ///   que desce de tom, com borbulhar. Ouve-se de longe (45 m): é a deixa para sair da água.
    /// • <b>Latejar</b> (nadando): um pulso grave, como um coração debaixo d'água, com ronco de
    ///   água por baixo. Cresce de perto; caçando, fica mais alto e mais rápido.
    /// • <b>Puxão</b> (pegando alguém): um tranco de água e um gorgolejo descendo.
    ///
    /// Dormindo, nada (só uma bolha de vez em quando, no corpo). Nos canos, silêncio.
    /// Roda em todas as máquinas, pelo estado que vem da rede.
    /// </summary>
    [RequireComponent(typeof(UndertowBrain))]
    public sealed class UndertowAudio : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.95f;
        [Tooltip("Até onde se ouve o latejar, em metros.")]
        public float throbRange = 30f;
        [Tooltip("Até onde se ouve o gemido, em metros.")]
        public float groanRange = 45f;

        /// <summary>Volume do latejar agora, de 0 a 1 (para os testes).</summary>
        public float ThrobLevel { get; private set; }
        /// <summary>Quantas vezes gemeu (acordando ou surgindo).</summary>
        public int Groans { get; private set; }

        UndertowBrain _brain;
        UndertowBody _body;
        AudioSource _throb, _shots;
        AudioClip _groan, _grab;

        void Awake()
        {
            _brain = GetComponent<UndertowBrain>();
            _body = GetComponent<UndertowBody>();

            _throb = gameObject.AddComponent<AudioSource>();
            _throb.clip = CreateThrob();
            _throb.loop = true;
            _throb.playOnAwake = false;
            Spatial(_throb, throbRange);
            _throb.volume = 0f;
            _throb.Play();

            _shots = gameObject.AddComponent<AudioSource>();
            _shots.playOnAwake = false;
            Spatial(_shots, groanRange);

            _groan = CreateGroan();
            _grab = CreateGrab();
        }

        static void Spatial(AudioSource source, float range)
        {
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3f;
            source.maxDistance = range;
            source.dopplerLevel = 0f;
        }

        void OnEnable() { if (_brain != null) _brain.StateChanged += OnState; }
        void OnDisable() { if (_brain != null) _brain.StateChanged -= OnState; }

        void OnState(UndertowState state)
        {
            if (state == UndertowState.Waking)
            {
                Groans++;
                _shots.pitch = Random.Range(0.92f, 1.04f);
                _shots.PlayOneShot(_groan, volume);
            }
            else if (state == UndertowState.Feed)
            {
                _shots.pitch = 1f;
                _shots.PlayOneShot(_grab, volume);
            }
        }

        void Update()
        {
            if (_brain == null || !_brain.IsSpawned) return;
            UndertowState state = _brain.State;
            bool visible = _body == null || _body.Visible;
            float target;
            switch (state)
            {
                case UndertowState.Hunt: target = 1f; break;
                case UndertowState.Investigate: target = 0.75f; break;
                case UndertowState.Circle:
                case UndertowState.Return: target = 0.5f; break;
                case UndertowState.Feed: target = 0.8f; break;
                case UndertowState.Waking: target = 0.4f; break;
                case UndertowState.Travel: target = visible ? 0.4f : 0f; break;
                default: target = 0f; break;
            }
            ThrobLevel = Mathf.MoveTowards(ThrobLevel, target, Time.deltaTime * 0.8f);
            _throb.volume = volume * ThrobLevel;
            _throb.pitch = state == UndertowState.Hunt ? 1.35f : 1f;
        }

        // ---------------------------------------------------------------- Síntese

        static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        /// <summary>O latejar: dois pulsos graves por batida (como um coração), ~1,1 batida/s, em laço de 1,8 s.</summary>
        public static AudioClip CreateThrob()
        {
            int rate = Rate;
            int n = (int)(rate * 1.8f);
            var data = new float[n];
            var rng = new System.Random(4401);
            float low = 0f, lower = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float v = 0f;
                // Duas batidas por laço (0,9 s cada), cada uma "tum-tum".
                for (int beat = 0; beat < 2; beat++)
                    for (int k = 0; k < 2; k++)
                    {
                        float start = beat * 0.9f + k * 0.22f;
                        float u = t - start;
                        if (u < 0f) continue;
                        float f = 52f - 14f * u;
                        v += Mathf.Sin(2f * Mathf.PI * f * u) * Mathf.Exp(-u * 9f) * (k == 0 ? 1f : 0.7f);
                    }
                // Ronco de água: ruído muito filtrado.
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.02f;
                lower += (low - lower) * 0.05f;
                data[i] = v * 0.75f + lower * 2.5f;
            }
            Loop(data, rate / 20);
            return Clip("Undertow_Latejar", data, rate);
        }

        /// <summary>O gemido: grave, abafado, descendo de tom, com borbulhar. 3,2 s.</summary>
        public static AudioClip CreateGroan()
        {
            int rate = Rate;
            int n = (int)(rate * 3.2f);
            var data = new float[n];
            var rng = new System.Random(4402);
            float phase = 0f, low = 0f, smooth = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Clamp01(t / 0.5f) * Mathf.Clamp01((3.2f - t) / 1.1f);
                // A voz: dente-de-serra grave (com tremor) passada num filtro passa-baixa: abafada.
                float f = Mathf.Lerp(78f, 46f, t / 3.2f) * (1f + 0.03f * Mathf.Sin(t * 31f) + 0.02f * Mathf.Sin(t * 7.3f));
                phase += f / rate;
                float saw = 2f * (phase - Mathf.Floor(phase)) - 1f;
                smooth += (saw - smooth) * 0.035f;
                float voice = smooth * 1.6f + Mathf.Sin(phase * Mathf.PI * 2f) * 0.5f;
                // Borbulhar por cima.
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.06f;
                float bubble = 0f;
                if (rng.NextDouble() < 0.0016) bubble = 1f;
                data[i] = voice * env * 0.8f + low * 0.6f * env + bubble * 0.4f;
            }
            Bubbles(data, rate, 22, 4403, 0.35f);
            return Clip("Undertow_Gemido", data, rate);
        }

        /// <summary>O puxão: um tranco grave de água e um gorgolejo descendo. 1,6 s.</summary>
        public static AudioClip CreateGrab()
        {
            int rate = Rate;
            int n = (int)(rate * 1.6f);
            var data = new float[n];
            var rng = new System.Random(4404);
            float low = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * Mathf.Lerp(0.3f, 0.03f, Mathf.Clamp01(t * 2f));
                float hit = Mathf.Sin(2f * Mathf.PI * (60f - 25f * t) * t) * Mathf.Exp(-t * 3f);
                data[i] = (low * 1.4f * Mathf.Exp(-t * 2.2f) + hit) * Mathf.Clamp01(t * 60f);
            }
            Bubbles(data, rate, 30, 4405, 0.5f);
            return Clip("Undertow_Puxao", data, rate);
        }

        // Bolhas: pios curtos subindo de tom.
        static void Bubbles(float[] data, int rate, int count, int seed, float gain)
        {
            var rng = new System.Random(seed);
            for (int b = 0; b < count; b++)
            {
                int start = rng.Next(0, data.Length - rate / 10);
                float f0 = 180f + (float)rng.NextDouble() * 260f;
                float amp = gain * (0.4f + (float)rng.NextDouble() * 0.6f);
                int len = rate / 25;
                for (int k = 0; k < len && start + k < data.Length; k++)
                {
                    float u = k / (float)rate;
                    data[start + k] += Mathf.Sin(2f * Mathf.PI * f0 * (1f + 6f * u) * u) * Mathf.Exp(-u * 70f) * amp;
                }
            }
        }

        static void Loop(float[] data, int fade)
        {
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                int j = data.Length - fade + i;
                float mixed = data[i] * w + data[j] * (1f - w);
                data[i] = mixed;
                data[j] *= 1f - w;
            }
        }

        static AudioClip Clip(string name, float[] data, int rate)
        {
            float peak = 0f;
            foreach (float s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
            if (peak > 0f)
                for (int i = 0; i < data.Length; i++) data[i] *= 0.85f / peak;
            AudioClip clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
