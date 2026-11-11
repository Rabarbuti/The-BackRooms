using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// A voz da entidade, sintetizada na inicialização (sem arquivos de áudio), em 3D:
    /// • um ronco grave contínuo, que denuncia que ela está por perto antes de ser vista;
    /// • um baque a cada passo, mais forte quanto mais rápido ela anda;
    /// • um guincho quando começa a perseguir alguém;
    /// • um rugido rouco e entrecortado, que despenca de tom, quando ataca e pega um jogador.
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
        AudioSource _attack;
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

            // O rugido tem fonte própria: o tom dos passos muda a cada passo e mexeria nele.
            _attack = CreateSource();
            _attack.clip = CreateRoar();
            _attack.minDistance = 4f;
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

            // Feed é o instante em que ela pega alguém: ruge.
            if (state == EntityState.Feed)
            {
                _attack.pitch = Random.Range(0.94f, 1.05f);
                _attack.volume = volume;
                _attack.Play();
                return;
            }

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
        public static AudioClip CreateDrone()
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
        public static AudioClip CreateStep()
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
        public static AudioClip CreateScreech()
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

        // Rugido de ataque: uma voz grave e rouca que sobe num tranco e despenca, passando por
        // duas "gargantas" (filtros ressonantes que se movem, como uma boca abrindo e fechando),
        // com um gemido agudo e trêmulo por cima que falha aos soluços — a parte que soa
        // errada, como uma gravação estragada. Tudo saturado, e com um sopro de ar no final.
        public static AudioClip CreateRoar()
        {
            int rate = Rate;
            const float seconds = 1.9f;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            var rng = new System.Random(8191);

            float phase = 0f, phaseHigh = 0f, phaseWail = 0f;
            float drift = 0f, driftTarget = 0f;
            float gate = 1f, gateTarget = 1f;
            int nextDrift = 0, nextGate = 0;

            // Filtros ressonantes (variável de estado): duas gargantas e o sopro.
            float low1 = 0f, band1 = 0f, low2 = 0f, band2 = 0f, lowAir = 0f, bandAir = 0f;
            float body = 0f;
            float bodyCut = 1f - Mathf.Exp(-2f * Mathf.PI * 260f / rate);

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float u = t / seconds;

                // Tom irregular: a cada ~25 ms a voz escorrega para um lado.
                if (i >= nextDrift)
                {
                    driftTarget = (float)(rng.NextDouble() * 2.0 - 1.0);
                    nextDrift = i + (int)(rate * (0.015f + (float)rng.NextDouble() * 0.03f));
                }
                drift += (driftTarget - drift) * 0.002f;

                // Sobe de 70 a 118 Hz no primeiro décimo e desce até 44 Hz.
                float pitch = u < 0.1f ? Mathf.Lerp(70f, 118f, u / 0.1f) : Mathf.Lerp(118f, 44f, Mathf.Pow((u - 0.1f) / 0.9f, 0.8f));
                pitch *= 1f + drift * 0.07f;

                phase += pitch / rate;
                phaseHigh += pitch * 1.5f * (1f + drift * 0.02f) / rate;

                // Voz: dente de serra, uma quinta acima e ar, com a aspereza de 30 batidas por segundo.
                float saw = (phase - Mathf.Floor(phase)) * 2f - 1f;
                float sawHigh = (phaseHigh - Mathf.Floor(phaseHigh)) * 2f - 1f;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float rough = 1f - 0.5f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 30f * t + drift * 2f));
                float voice = (saw + sawHigh * 0.5f + noise * 0.35f) * rough;

                // Gargantas: a primeira abre (420 → 820 Hz) e fecha (→ 330 Hz); a segunda acompanha mais acima.
                float open = Mathf.Sin(Mathf.Clamp01(u * 1.25f) * Mathf.PI);
                float f1 = 2f * Mathf.Sin(Mathf.PI * Mathf.Lerp(330f, 820f, open) / rate);
                float f2 = 2f * Mathf.Sin(Mathf.PI * Mathf.Lerp(950f, 1650f, open) / rate);

                low1 += f1 * band1;
                band1 += f1 * (voice - low1 - 0.22f * band1);
                low2 += f2 * band2;
                band2 += f2 * (voice - low2 - 0.2f * band2);
                body += (voice - body) * bodyCut;

                // Gemido agudo: desce de 620 a 250 Hz tremendo, e falha aos soluços até dois terços do som.
                if (i >= nextGate)
                {
                    gateTarget = u < 0.68f && rng.NextDouble() < 0.42 ? 0.05f : 1f;
                    nextGate = i + (int)(rate * (0.025f + (float)rng.NextDouble() * 0.055f));
                }
                gate += (gateTarget - gate) * 0.01f;

                float wailPitch = Mathf.Lerp(620f, 250f, Mathf.Pow(u, 0.6f)) * (1f + 0.05f * Mathf.Sin(2f * Mathf.PI * 19f * t) + drift * 0.04f);
                phaseWail += wailPitch / rate;
                float wail = Mathf.Sin(2f * Mathf.PI * phaseWail) * Mathf.Sin(2f * Mathf.PI * 87f * t);
                wail *= gate * Mathf.Clamp01(u / 0.05f) * Mathf.Pow(1f - u, 0.8f);

                // Sopro: ar passando, mais forte no fim.
                float fAir = 2f * Mathf.Sin(Mathf.PI * 2100f / rate);
                lowAir += fAir * bandAir;
                bandAir += fAir * (noise - lowAir - 0.7f * bandAir);
                float air = bandAir * (0.12f + 0.3f * u * u);

                // As gargantas e o gemido mandam; o grave puro entra só como peso. Com o grave na
                // frente o rugido virava um ronco contínuo, sem nada de voz.
                float mix = band1 * 0.85f + band2 * 0.6f + body * 0.6f + wail * 0.95f + air;

                // A voz inteira falha junto com o gemido, como uma gravação que engasga.
                mix *= 0.3f + 0.7f * gate;

                // Saturação: achata os picos e engrossa o som. O volume é aplicado depois dela,
                // senão ela nivelaria tudo: cresce no começo, sustenta e some no último terço.
                float driven = mix * 2f;
                float swell = 0.72f + 0.28f * Mathf.Sin(Mathf.Clamp01(u * 1.6f) * Mathf.PI);
                float envelope = Mathf.Clamp01(t / 0.035f) * swell * Mathf.Pow(Mathf.Clamp01((seconds - t) / 0.7f), 1.3f);
                samples[i] = driven / (1f + Mathf.Abs(driven)) * envelope;
            }

            return ToClip("Entidade_Rugido", samples, rate, 0.95f);
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
