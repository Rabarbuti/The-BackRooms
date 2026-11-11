using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// A voz da entidade, sintetizada na inicialização (sem arquivos de áudio), em 3D:
    /// • um ronco grave contínuo, que denuncia que ela está por perto antes de ser vista;
    /// • um baque a cada passo, mais forte quanto mais rápido ela anda;
    /// • um guincho quando começa a perseguir alguém.
    /// Roda em todas as máquinas: os passos vêm do <see cref="EntityBody"/> e o estado do
    /// <see cref="EntityBrain"/>, que já chegam pela rede.
    /// </summary>
    [RequireComponent(typeof(EntityBody))]
    public sealed class EntityAudio : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.9f;
        [Tooltip("Distância até onde se ouve, em metros.")]
        public float range = 34f;

        AudioSource _drone;
        AudioSource _shots;
        AudioClip _step;
        AudioClip _screech;
        EntityBody _body;
        EntityBrain _brain;
        float _droneLevel = 0.35f;
        float _droneTarget = 0.35f;

        void Awake()
        {
            _body = GetComponent<EntityBody>();
            _brain = GetComponent<EntityBrain>();

            _drone = CreateSource();
            _drone.clip = CreateDrone();
            _drone.loop = true;

            _shots = CreateSource();
            _step = CreateStep();
            _screech = CreateScreech();
        }

        void OnEnable()
        {
            _body.Stepped += OnStepped;
            if (_brain != null) _brain.StateChanged += OnStateChanged;

            _drone.volume = volume * _droneLevel;
            _drone.Play();
        }

        void OnDisable()
        {
            _body.Stepped -= OnStepped;
            if (_brain != null) _brain.StateChanged -= OnStateChanged;
        }

        void Start()
        {
            // Ao aparecer: é o aviso de que agora há algo no andar.
            _shots.PlayOneShot(_screech, volume * 0.7f);
        }

        void Update()
        {
            _droneLevel = Mathf.MoveTowards(_droneLevel, _droneTarget, Time.deltaTime * 0.6f);
            _drone.volume = volume * _droneLevel;
            _drone.pitch = Mathf.Lerp(0.92f, 1.12f, Mathf.InverseLerp(0.35f, 0.8f, _droneLevel));
        }

        void OnStepped(float speed)
        {
            _shots.pitch = Random.Range(0.9f, 1.08f);
            _shots.PlayOneShot(_step, volume * Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(1f, 4.6f, speed)));
        }

        void OnStateChanged(EntityState state)
        {
            _droneTarget = state == EntityState.Chase ? 0.8f : state == EntityState.Investigate ? 0.55f : 0.35f;

            if (state != EntityState.Chase) return;
            _shots.pitch = Random.Range(0.92f, 1.06f);
            _shots.PlayOneShot(_screech, volume);
        }

        AudioSource CreateSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = range;
            source.dopplerLevel = 0f;
            return source;
        }

        static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        // Ronco: dois graves desafinados entre si mais um chiado abafado que "respira". Todas
        // as frequências fecham ciclos inteiros em 4 s, para o laço não estalar.
        static AudioClip CreateDrone()
        {
            int rate = Rate;
            const float seconds = 4f;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            var rng = new System.Random(4421);

            float cut = 1f - Mathf.Exp(-2f * Mathf.PI * 260f / rate);
            float low = 0f, low2 = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float breath = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
                float wobble = 1f + 0.25f * Mathf.Sin(2f * Mathf.PI * 7.25f * t);

                float tone = Mathf.Sin(2f * Mathf.PI * 46f * t) * 0.55f
                           + Mathf.Sin(2f * Mathf.PI * 69.25f * t) * 0.32f
                           + Mathf.Sin(2f * Mathf.PI * 138.5f * t + Mathf.Sin(2f * Mathf.PI * 3f * t) * 0.8f) * 0.14f;

                low += ((float)(rng.NextDouble() * 2.0 - 1.0) - low) * cut;
                low2 += (low - low2) * cut;

                samples[i] = tone * wobble * 0.6f + low2 * 2.2f * breath;
            }

            // O ruído filtrado não fecha sozinho: emenda o fim no começo.
            int blend = rate / 10;
            for (int i = 0; i < blend; i++)
            {
                float k = i / (float)blend;
                samples[length - blend + i] = Mathf.Lerp(samples[length - blend + i], samples[i], k);
            }

            return ToClip("Entidade_Ronco", samples, rate, 0.8f);
        }

        // Passo: baque grave que desce de tom, com um estalo curto no começo.
        static AudioClip CreateStep()
        {
            int rate = Rate;
            int length = (int)(rate * 0.32f);
            var samples = new float[length];
            var rng = new System.Random(977);
            float cut = 1f - Mathf.Exp(-2f * Mathf.PI * 900f / rate);
            float low = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float pitch = 44f + 50f * Mathf.Exp(-t * 34f);
                float thump = Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-t * 14f);

                low += ((float)(rng.NextDouble() * 2.0 - 1.0) - low) * cut;
                float click = low * Mathf.Exp(-t * 70f) * 1.6f;

                samples[i] = (thump + click) * Mathf.Clamp01(t / 0.003f) * Mathf.Clamp01((length - i) / (rate * 0.03f));
            }

            return ToClip("Entidade_Passo", samples, rate, 0.9f);
        }

        // Guincho: um tom áspero que sobe e despenca, tremendo, com chiado por cima.
        static AudioClip CreateScreech()
        {
            int rate = Rate;
            int length = (int)(rate * 1.3f);
            var samples = new float[length];
            var rng = new System.Random(6151);
            float phase = 0f, phase2 = 0f;
            float cut = 1f - Mathf.Exp(-2f * Mathf.PI * 2600f / rate);
            float low = 0f, high = 0f;
            float highCut = 1f - Mathf.Exp(-2f * Mathf.PI * 500f / rate);

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float u = t / 1.3f;

                // Sobe rápido de 420 a 980 Hz e cai até 260 Hz.
                float sweep = u < 0.18f ? Mathf.Lerp(420f, 980f, u / 0.18f) : Mathf.Lerp(980f, 260f, Mathf.Pow((u - 0.18f) / 0.82f, 0.7f));
                float vibrato = 1f + 0.06f * Mathf.Sin(2f * Mathf.PI * 23f * t) + 0.03f * Mathf.Sin(2f * Mathf.PI * 61f * t);

                phase += sweep * vibrato / rate;
                phase2 += sweep * vibrato * 1.493f / rate;

                // Dois dentes de serra desafinados, saturados.
                float saw = (phase - Mathf.Floor(phase)) * 2f - 1f;
                float saw2 = (phase2 - Mathf.Floor(phase2)) * 2f - 1f;
                float voice = Mathf.Clamp((saw * 0.7f + saw2 * 0.5f) * 2.4f, -1f, 1f);

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (voice * 0.8f + noise * 0.35f - low) * cut;
                high += (low - high) * highCut;

                float envelope = Mathf.Clamp01(t / 0.04f) * Mathf.Pow(1f - u, 1.4f);
                samples[i] = (low - high) * envelope;
            }

            return ToClip("Entidade_Guincho", samples, rate, 0.9f);
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
