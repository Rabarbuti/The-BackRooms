using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O som do Capitão, sintetizado na inicialização (sem arquivos de áudio), em 3D. Ele não
    /// fala: o aviso dele é o andar manco, "TOC… arrasta. TOC… arrasta." — a perna de pau
    /// batendo seco no chão e a bota arrastando no carpete. Correndo (as arrancadas do apagão),
    /// o TOC fica mais alto e o arrasto mais curto.
    ///
    /// Roda em todas as máquinas: os passos vêm do <see cref="CaptainBody"/>, que anima pela
    /// posição recebida da rede. Congelado, ele não dá passo e fica em silêncio.
    /// O jingle desafinado da loja fica para a etapa do som (level1-mostruario.md, seção 7).
    /// </summary>
    [RequireComponent(typeof(CaptainBody))]
    public sealed class CaptainAudio : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.9f;
        [Tooltip("Distância até onde se ouve, em metros.")]
        public float range = 26f;

        AudioSource _source;
        AudioClip _knock;
        AudioClip _drag;
        CaptainBody _body;

        void Awake()
        {
            _body = GetComponent<CaptainBody>();

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 2f;
            _source.maxDistance = range;
            _source.dopplerLevel = 0f;

            _knock = CreateKnock();
            _drag = CreateDrag();
        }

        void OnEnable() { _body.Stepped += OnStepped; }
        void OnDisable() { _body.Stepped -= OnStepped; }

        void OnStepped(float speed, bool pegLeg)
        {
            float run = Mathf.InverseLerp(1.7f, 5.2f, speed);
            _source.pitch = Random.Range(0.94f, 1.06f) * (pegLeg ? 1f : Mathf.Lerp(1f, 1.25f, run));

            if (pegLeg) _source.PlayOneShot(_knock, volume * Mathf.Lerp(0.75f, 1f, run));
            else _source.PlayOneShot(_drag, volume * Mathf.Lerp(0.7f, 0.45f, run));
        }

        static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        /// <summary>
        /// TOC: a perna de pau batendo no chão. Um estalo de madeira (dois tons secos que somem
        /// em poucos centésimos) sobre um baque grave curto, com o carpete abafando o agudo.
        /// </summary>
        public static AudioClip CreateKnock()
        {
            int rate = Rate;
            int length = (int)(rate * 0.22f);
            var samples = new float[length];
            var rng = new System.Random(5113);
            float cut = 1f - Mathf.Exp(-2f * Mathf.PI * 2400f / rate);
            float low = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;

                // Madeira: dois modos inarmônicos que caem rápido.
                float wood = Mathf.Sin(2f * Mathf.PI * 610f * t) * Mathf.Exp(-t * 55f) * 0.8f
                           + Mathf.Sin(2f * Mathf.PI * 1270f * t + 0.6f) * Mathf.Exp(-t * 80f) * 0.45f;

                // Baque: o peso dele no chão, descendo de tom.
                float pitch = 95f + 70f * Mathf.Exp(-t * 40f);
                float thump = Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-t * 22f) * 0.9f;

                // Estalo do contato.
                low += ((float)(rng.NextDouble() * 2.0 - 1.0) - low) * cut;
                float click = low * Mathf.Exp(-t * 160f) * 1.4f;

                samples[i] = (wood + thump + click) * Mathf.Clamp01(t / 0.0015f) * Mathf.Clamp01((length - i) / (rate * 0.02f));
            }

            return ToClip("Capitao_Toc", samples, rate, 0.9f);
        }

        /// <summary>
        /// Arrasta: a bota esfregando o carpete. Chiado abafado (ruído passado por dois filtros)
        /// que cresce e some em meio segundo, com a aspereza da fibra (o volume treme).
        /// </summary>
        public static AudioClip CreateDrag()
        {
            int rate = Rate;
            const float seconds = 0.55f;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            var rng = new System.Random(8237);
            float lowCut = 1f - Mathf.Exp(-2f * Mathf.PI * 1500f / rate);
            float highCut = 1f - Mathf.Exp(-2f * Mathf.PI * 180f / rate);
            float low = 0f, low2 = 0f, high = 0f;
            float grain = 1f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float u = t / seconds;

                low += ((float)(rng.NextDouble() * 2.0 - 1.0) - low) * lowCut;
                low2 += (low - low2) * lowCut;
                high += (low2 - high) * highCut;
                float band = low2 - high;

                // Fibra: o atrito pega e solta, uns 40 vezes por segundo.
                if (i % (rate / 40) == 0) grain = 0.55f + 0.45f * (float)rng.NextDouble();

                // Entra devagar (o pé começa a deslizar) e sai mais depressa.
                float envelope = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.7f));
                envelope = Mathf.Max(0f, envelope);

                samples[i] = band * envelope * grain;
            }

            return ToClip("Capitao_Arrasta", samples, rate, 0.6f);
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
