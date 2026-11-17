using BackRooms.World;
using UnityEngine;

namespace BackRooms.Audio
{
    /// <summary>
    /// A música da loja do Level 1 (level1-mostruario.md, seção 7): um "muzak" baixinho de piano
    /// elétrico, vindo do teto, que de vez em quando some e volta. Quando some de vez é o aviso do
    /// apagão: ela se cala uns segundos antes de as luminárias começarem a piscar, e só volta
    /// quando a luz volta. Sintetizada (sem arquivo). Toca igual em todas as máquinas pela conta do
    /// <see cref="LightsOut"/> (o relógio da sessão); as falhas curtas são só desta máquina.
    ///
    /// Também guarda as melodias: o jingle da loja (<see cref="CreateJingle"/>), que a caixinha de
    /// música do Capitão toca desafinada.
    /// </summary>
    public sealed class ShopMusic : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.14f;
        [Tooltip("Segundos antes do aviso do apagão em que a música já se calou.")]
        public float silenceBeforeWarning = 3f;
        [Tooltip("Duração do sumiço e da volta, em segundos.")]
        public float fadeSeconds = 2.5f;

        /// <summary>Volume relativo agora (0 calada, 1 cheia). Para os testes: sem placa de som isPlaying fica falso.</summary>
        public float Level { get; private set; }

        /// <summary>A música está querendo tocar (fora do aviso e do apagão).</summary>
        public bool Wanted { get; private set; }

        AudioSource _source;
        float _dropUntil, _nextDrop;

        void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.clip = CreateMuzak();
            _source.loop = true;
            _source.spatialBlend = 0f;
            _source.volume = 0f;
            _source.playOnAwake = false;
            _source.Play();
            _nextDrop = Time.time + Random.Range(30f, 60f);
        }

        void Update()
        {
            LightsOut power = LightsOut.Instance;
            bool steady = power == null || power.Phase == PowerPhase.Steady;
            // Às vésperas do aviso (que começa warningSeconds antes do escuro), já se calou.
            bool beforeWarning = power != null && steady && power.SecondsToChange >= 0f &&
                                 power.SecondsToChange < power.warningSeconds + silenceBeforeWarning;
            Wanted = steady && !beforeWarning;

            // Some e volta sozinha de tempos em tempos (só nesta máquina).
            if (Time.time >= _nextDrop)
            {
                _dropUntil = Time.time + Random.Range(2f, 4f);
                _nextDrop = Time.time + Random.Range(35f, 70f);
            }
            bool dropped = Time.time < _dropUntil;

            float target = Wanted && !dropped ? 1f : 0f;
            Level = Mathf.MoveTowards(Level, target, Time.deltaTime / Mathf.Max(0.1f, fadeSeconds));
            _source.volume = volume * Level;
        }

        static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        static float Note(float semitonesFromC4) => 261.63f * Mathf.Pow(2f, semitonesFromC4 / 12f);

        // O jingle (original): oito compassos curtos, em dó maior, saltitante.
        static readonly float[] Tune = { 4, 7, 9, 7, 12, 9, 7, 4, 2, 4, 7, 4, 2, 0, -1, 0 };

        /// <summary>
        /// O muzak: Cmaj7, Am7, Dm7 e G7 em piano elétrico macio, baixo e a melodia do jingle bem
        /// devagar, com o leve vaivém de uma fita gasta. 16 s, fecha o laço sem estalo.
        /// </summary>
        public static AudioClip CreateMuzak()
        {
            int rate = Rate;
            const float seconds = 16f;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            float[][] chords =
            {
                new float[] { 0, 4, 7, 11 }, new float[] { -3, 0, 4, 7 }, new float[] { 2, 5, 9, 12 }, new float[] { -5, 2, 5, 11 },
            };

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float wow = 1f + 0.004f * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
                int bar = (int)(t / 4f) % 4;
                float inBar = t % 4f;

                // Acorde: nota de piano elétrico (seno com um pouco de segundo harmônico), batida a
                // cada 2 tempos e decaindo.
                float hit = inBar % 2f;
                float env = Mathf.Exp(-hit * 1.6f) * Mathf.Clamp01(hit / 0.01f);
                float chord = 0f;
                foreach (float n in chords[bar])
                {
                    float f = Note(n - 12f) * wow;
                    chord += (Mathf.Sin(2f * Mathf.PI * f * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * f * t)) * 0.18f;
                }

                // Baixo: a fundamental, um tempo sim, um não.
                float beat = inBar % 1f;
                float bass = Mathf.Sin(2f * Mathf.PI * Note(chords[bar][0] - 24f) * wow * t) * Mathf.Exp(-beat * 3f) * 0.35f;

                // Melodia: uma nota do jingle por tempo (16 tempos = o laço).
                int step = (int)(t / 1f) % Tune.Length;
                float noteT = t % 1f;
                float lead = Mathf.Sin(2f * Mathf.PI * Note(Tune[step]) * wow * t) * Mathf.Exp(-noteT * 2.2f) * Mathf.Clamp01(noteT / 0.02f) * 0.22f;

                samples[i] = (chord * env + bass + lead) * 0.6f;
            }

            Normalize(samples, 0.8f);
            var clip = AudioClip.Create("Loja_Musica", length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// O jingle na caixinha de música: notas agudas de lâmina (seno com um harmônico alto que
        /// some rápido), uma a cada 0,25 s. 4 s, em laço.
        /// </summary>
        public static AudioClip CreateJingle()
        {
            int rate = Rate;
            const float step = 0.25f;
            float seconds = step * Tune.Length;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                int k = (int)(t / step) % Tune.Length;
                float nt = t - k * step;
                float f = Note(Tune[k] + 24f);
                float env = Mathf.Exp(-nt * 7f) * Mathf.Clamp01(nt / 0.003f);
                float tine = Mathf.Sin(2f * Mathf.PI * f * nt) + 0.4f * Mathf.Sin(2f * Mathf.PI * f * 5.4f * nt) * Mathf.Exp(-nt * 40f);
                // A nota anterior ainda soando por baixo.
                float prevT = nt + step;
                float pf = Note(Tune[(k + Tune.Length - 1) % Tune.Length] + 24f);
                float tail = Mathf.Sin(2f * Mathf.PI * pf * prevT) * Mathf.Exp(-prevT * 7f) * 0.6f;
                samples[i] = (tine * env + tail) * 0.5f;
            }

            Normalize(samples, 0.8f);
            var clip = AudioClip.Create("Capitao_Jingle", length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static void Normalize(float[] samples, float peakLevel)
        {
            float peak = 0f;
            foreach (float s in samples) peak = Mathf.Max(peak, Mathf.Abs(s));
            if (peak <= 0f) return;
            float gain = peakLevel / peak;
            for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
        }
    }
}
