using System;
using BackRooms.World;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O som do Undertow, sintetizado na inicialização (sem arquivos), em 3D. É o principal aviso.
    /// Refeito em 21/11/2026 ("nem parece de bicho"): agora é uma garganta de verdade, com cordas
    /// vocais (pulsos glotais), rouquidão (ar soprado junto), período dobrado (o ronco gutural), a
    /// boca e a garganta como filtros (formantes) e saturação (o rasgado).
    ///
    /// • <b>Rugido</b> (ao ouvir e acordar, ao surgir de um cano atrás de alguém, ao dar o bote e
    ///   quando a maré alta chega): começa num ronco grave e rachado, sobe num grito rasgado e cai
    ///   num estertor. Alto e de longe (até 110 m, quase sem cair com a distância perto). Debaixo
    ///   d'água (quem ouve com a cabeça submersa, ou ele fundo) passa por um filtro: abafado.
    /// • <b>Rosnado</b> (nadando): um ronco grave e irregular de garganta com a água revolvendo
    ///   e um baque surdo; cresce e sobe de tom quanto mais perto de quem ouve; caçando, mais alto.
    /// • <b>Gorgolejo</b> (surgindo de um ralo sem ter ouvido nada): o ralo engolindo água, bolhas
    ///   subindo e um resmungo baixo no fim. Discreto: é a pista de que ele mudou de água.
    /// • <b>Puxão</b> (pegando alguém): um tranco de água, um rosnado curto e o gorgolejo descendo.
    ///
    /// Dormindo, nada. Nos canos, silêncio. Roda em todas as máquinas, pelo estado e pelo contador
    /// de rugidos que vêm da rede.
    /// </summary>
    [RequireComponent(typeof(UndertowBrain))]
    public sealed class UndertowAudio : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.95f;
        [Tooltip("Até onde se ouve o rosnado, em metros.")]
        public float growlRange = 36f;
        [Tooltip("Até onde se ouve o rugido, em metros.")]
        public float roarRange = 110f;
        [Tooltip("Corte do filtro (Hz) com quem ouve debaixo d'água: o rugido abafado.")]
        public float underwaterCutoff = 650f;

        /// <summary>Volume do rosnado agora, de 0 a 1 (para os testes).</summary>
        public float GrowlLevel { get; private set; }
        /// <summary>Quantas vezes rugiu nesta máquina.</summary>
        public int Roars { get; private set; }
        /// <summary>Quantas vezes gorgolejou (surgindo de um ralo sem barulho).</summary>
        public int Gurgles { get; private set; }
        /// <summary>O último rugido saiu abafado (quem ouvia estava debaixo d'água)?</summary>
        public bool LastRoarMuffled { get; private set; }

        UndertowBrain _brain;
        UndertowBody _body;
        AudioSource _growl, _shots, _roar;
        AudioLowPassFilter _roarFilter;
        AudioClip[] _roars;
        AudioClip _gurgle, _grab;
        AudioListener _listener;

        void Awake()
        {
            _brain = GetComponent<UndertowBrain>();
            _body = GetComponent<UndertowBody>();

            _growl = gameObject.AddComponent<AudioSource>();
            _growl.clip = CreateGrowl();
            _growl.loop = true;
            _growl.playOnAwake = false;
            Spatial(_growl, 2f, growlRange);
            _growl.volume = 0f;
            _growl.Play();

            _shots = gameObject.AddComponent<AudioSource>();
            _shots.playOnAwake = false;
            Spatial(_shots, 3f, 32f);

            // O rugido num filho com o próprio filtro (o filtro vale para o objeto inteiro: no
            // corpo, abafaria também o rosnado e o puxão).
            var voice = new GameObject("Rugido");
            voice.transform.SetParent(transform, false);
            voice.transform.localPosition = new Vector3(0f, 0f, 1.2f);
            _roar = voice.AddComponent<AudioSource>();
            _roar.playOnAwake = false;
            Spatial(_roar, 9f, roarRange);
            _roar.spatialBlend = 0.85f;
            _roarFilter = voice.AddComponent<AudioLowPassFilter>();
            _roarFilter.cutoffFrequency = 22000f;

            _roars = new[] { CreateRoar(5101), CreateRoar(5102) };
            _gurgle = CreateGurgle();
            _grab = CreateGrab();
        }

        static void Spatial(AudioSource source, float near, float range)
        {
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = near;
            source.maxDistance = range;
            source.dopplerLevel = 0f;
        }

        void OnEnable()
        {
            if (_brain == null) return;
            _brain.StateChanged += OnState;
            _brain.Roared += OnRoar;
        }

        void OnDisable()
        {
            if (_brain == null) return;
            _brain.StateChanged -= OnState;
            _brain.Roared -= OnRoar;
        }

        void OnState(UndertowState state)
        {
            if (state == UndertowState.Feed)
            {
                _shots.pitch = 1f;
                _shots.PlayOneShot(_grab, volume);
            }
            else if (state == UndertowState.Emerge)
            {
                Gurgles++;
                _shots.pitch = UnityEngine.Random.Range(0.92f, 1.05f);
                _shots.PlayOneShot(_gurgle, volume * 0.8f);
            }
        }

        void OnRoar()
        {
            Roars++;
            PoolWater water = PoolWater.Current;
            bool listenerUnder = water != null && water.LocalUnderwater;
            bool deep = water != null && water.SurfaceY - transform.position.y > 1.6f;
            LastRoarMuffled = listenerUnder || deep;
            _roarFilter.cutoffFrequency = listenerUnder ? underwaterCutoff : deep ? 1500f : 22000f;
            _roar.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            _roar.PlayOneShot(_roars[UnityEngine.Random.Range(0, _roars.Length)], 1f);
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
                case UndertowState.Investigate: target = 0.8f; break;
                case UndertowState.Prowl: target = 0.6f; break;
                case UndertowState.Circle:
                case UndertowState.Return: target = 0.5f; break;
                case UndertowState.Feed: target = 0.9f; break;
                case UndertowState.Waking: target = 0.5f; break;
                case UndertowState.Travel: target = visible ? 0.4f : 0f; break;
                default: target = 0f; break;
            }
            GrowlLevel = Mathf.MoveTowards(GrowlLevel, target, Time.deltaTime * 0.8f);

            // Cresce e sobe de tom com a proximidade de quem ouve.
            if (_listener == null || !_listener.isActiveAndEnabled) _listener = FindFirstObjectByType<AudioListener>();
            float closeness = 0f;
            if (_listener != null)
                closeness = 1f - Mathf.Clamp01(Vector3.Distance(_listener.transform.position, transform.position) / 25f);
            _growl.volume = volume * GrowlLevel * (0.55f + 0.45f * closeness);
            _growl.pitch = Mathf.Lerp(0.88f, 1.2f, closeness) + (state == UndertowState.Hunt ? 0.12f : 0f);
        }

        // ---------------------------------------------------------------- Síntese

        static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        /// <summary>
        /// Um filtro passa-faixa de dois polos (RBJ, pico 0 dB) cuja frequência pode mudar a cada
        /// amostra: a boca e a garganta (formantes).
        /// </summary>
        struct Band
        {
            float _x1, _x2, _y1, _y2;

            public float Run(float x, float frequency, float q, int rate)
            {
                float w = 2f * Mathf.PI * Mathf.Clamp(frequency, 20f, rate * 0.45f) / rate;
                float alpha = Mathf.Sin(w) / (2f * q);
                float a0 = 1f + alpha;
                float y = (alpha * x - alpha * _x2 + 2f * Mathf.Cos(w) * _y1 - (1f - alpha) * _y2) / a0;
                _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
                return y;
            }
        }

        /// <summary>
        /// Uma garganta: pulsos glotais na altura <paramref name="pitch"/>(t) com tremor e variação de
        /// ciclo a ciclo, período dobrado (<paramref name="growl"/>(t): o ronco gutural), ar soprado
        /// (<paramref name="breath"/>(t): a rouquidão), quatro formantes com a boca abrindo pela força
        /// (<paramref name="open"/>(t)) e saturação (<paramref name="drive"/>). Soma em <paramref name="data"/>.
        /// </summary>
        static void Throat(float[] data, int rate, int start, float seconds, int seed, Func<float, float> pitch,
                           Func<float, float> envelope, Func<float, float> growl, Func<float, float> breath,
                           Func<float, float> open, float drive, float gain)
        {
            var rng = new System.Random(seed);
            Band f1 = default, f2 = default, f3 = default, f4 = default, chest = default;
            float phase = 0f, cycleAmp = 1f, jitter = 0f, noiseLow = 0f;
            int cycle = 0;
            int n = Mathf.Min(data.Length - start, (int)(seconds * rate));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float f0 = pitch(t) * (1f + jitter) * (1f + 0.035f * Mathf.Sin(2f * Mathf.PI * 5.3f * t));
                phase += f0 / rate;
                if (phase >= 1f)
                {
                    phase -= 1f;
                    cycle++;
                    // Ciclo a ciclo: tremor de altura e de força (a voz rachada de um bicho).
                    jitter = ((float)rng.NextDouble() - 0.5f) * 0.09f;
                    float sub = growl(t);
                    cycleAmp = (0.75f + 0.5f * (float)rng.NextDouble()) * (cycle % 2 == 0 ? 1f : 1f - sub);
                }
                // Pulso glotal: abre devagar, fecha de estalo (rico em harmônicos).
                float pulse = phase < 0.62f ? 0.5f - 0.5f * Mathf.Cos(Mathf.PI * phase / 0.62f) : Mathf.Max(0f, 1f - (phase - 0.62f) / 0.08f);
                float glottal = (pulse - 0.45f) * cycleAmp;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                noiseLow += (noise - noiseLow) * 0.35f;
                float air = noiseLow * breath(t) * (0.35f + 0.65f * pulse);
                float source = glottal + air;

                float o = Mathf.Clamp01(open(t));
                float y = f1.Run(source, Mathf.Lerp(380f, 820f, o), 4.5f, rate) * 1.0f
                        + f2.Run(source, Mathf.Lerp(1000f, 1350f, o), 5f, rate) * 0.75f
                        + f3.Run(source, Mathf.Lerp(2300f, 2700f, o), 6f, rate) * 0.45f * (0.4f + o)
                        + f4.Run(source, 3500f, 7f, rate) * 0.25f * o
                        + chest.Run(source, 160f, 1.5f, rate) * 0.9f;
                y = (float)Math.Tanh(y * drive) / (float)Math.Tanh(drive);
                data[start + i] += y * envelope(t) * gain;
            }
        }

        static float Smooth(float a, float b, float t)
        {
            t = Mathf.Clamp01((t - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// O rugido, 3,1 s: ronco grave e rachado (55 Hz, período dobrado), sobe num grito rasgado
        /// (até ~165 Hz com a boca aberta e muito ar) e cai num estertor; por baixo, um baque de
        /// água e bolhas no começo.
        /// </summary>
        public static AudioClip CreateRoar(int seed = 5101)
        {
            int rate = Rate;
            const float length = 3.1f;
            var data = new float[(int)(rate * length)];
            float rise = 0.6f + (seed % 3) * 0.08f;
            Throat(data, rate, 0, length, seed,
                pitch: t => t < rise ? Mathf.Lerp(55f, 165f, Smooth(0.08f, rise, t))
                          : t < 2.1f ? 165f - 25f * Smooth(rise, 2.1f, t)
                          : Mathf.Lerp(140f, 62f, Smooth(2.1f, 3f, t)),
                envelope: t => Smooth(0f, 0.07f, t) * (0.75f + 0.25f * Smooth(0.1f, rise, t)) * (1f - Smooth(2.3f, 3.08f, t)),
                growl: t => 0.75f - 0.5f * Smooth(0.15f, rise, t) + 0.55f * Smooth(1.9f, 2.6f, t),
                breath: t => 0.35f + 0.45f * Smooth(0.2f, rise + 0.2f, t) + 0.3f * Smooth(2.2f, 3f, t),
                open: t => Smooth(0.05f, rise, t) * (1f - 0.6f * Smooth(1.9f, 2.9f, t)),
                drive: 3.2f, gain: 1f);
            // Uma segunda garganta, uma oitava abaixo e atrasada: o corpo grande por trás do grito.
            Throat(data, rate, (int)(0.04f * rate), length - 0.1f, seed + 7,
                pitch: t => t < rise ? Mathf.Lerp(28f, 80f, Smooth(0.08f, rise, t)) : Mathf.Lerp(80f, 32f, Smooth(rise, 3f, t)),
                envelope: t => Smooth(0f, 0.12f, t) * (1f - Smooth(2.2f, 3f, t)),
                growl: t => 0.6f, breath: t => 0.2f, open: t => 0.2f, drive: 2f, gain: 0.55f);
            // O tranco de água no começo e bolhas.
            var rng = new System.Random(seed + 13);
            float low = 0f;
            for (int i = 0; i < (int)(0.5f * rate); i++)
            {
                float t = i / (float)rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.08f;
                data[i] += (low * 0.9f + Mathf.Sin(2f * Mathf.PI * (48f - 20f * t) * t) * 0.7f) * Mathf.Exp(-t * 7f) * Mathf.Clamp01(t * 200f);
            }
            Bubbles(data, rate, 18, seed + 17, 0.25f);
            return Clip("Undertow_Rugido_" + seed, data, rate);
        }

        /// <summary>
        /// O rosnado de quem nada perto, em laço de 3,2 s: ronco de garganta grave e irregular (40 Hz,
        /// período dobrado, muito ar), a água revolvendo e um baque surdo duas vezes por volta.
        /// </summary>
        public static AudioClip CreateGrowl()
        {
            int rate = Rate;
            const float length = 3.2f;
            var data = new float[(int)(rate * length)];
            // Modulações que fecham a volta (frequências múltiplas de 1/3,2 s).
            float w = 2f * Mathf.PI / length;
            Throat(data, rate, 0, length, 4501,
                pitch: t => 40f + 6f * Mathf.Sin(w * t) + 3f * Mathf.Sin(3f * w * t + 1f),
                envelope: t => 0.55f + 0.3f * Mathf.Sin(2f * w * t) * Mathf.Sin(2f * w * t) + 0.15f * Mathf.Sin(5f * w * t),
                growl: t => 0.7f, breath: t => 0.6f + 0.2f * Mathf.Sin(w * t),
                open: t => 0.15f + 0.1f * Mathf.Sin(2f * w * t), drive: 2.4f, gain: 0.8f);
            var rng = new System.Random(4502);
            float low = 0f, lower = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.03f;
                lower += (low - lower) * 0.08f;
                float churn = lower * 3f * (0.6f + 0.4f * Mathf.Sin(4f * w * t));
                float thump = 0f;
                for (int b = 0; b < 2; b++)
                {
                    float u = t - b * length * 0.5f;
                    if (u >= 0f) thump += Mathf.Sin(2f * Mathf.PI * (50f - 15f * u) * u) * Mathf.Exp(-u * 8f);
                }
                data[i] += churn + thump * 0.5f;
            }
            Loop(data, rate / 10);
            return Clip("Undertow_Rosnado", data, rate);
        }

        /// <summary>O gorgolejo de quem surge de um ralo sem ter ouvido nada: o ralo, as bolhas e um resmungo baixo. 2 s.</summary>
        public static AudioClip CreateGurgle()
        {
            int rate = Rate;
            const float length = 2f;
            var data = new float[(int)(rate * length)];
            var rng = new System.Random(4601);
            Band glug = default;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                // O ralo: ruído numa faixa grave que sobe e desce, em goles (7 por segundo).
                float gulp = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 6.8f * t + Mathf.Sin(t * 13f)));
                float y = glug.Run(noise, 140f + 90f * gulp, 3f, rate) * gulp * 2.2f;
                data[i] = y * Smooth(0f, 0.15f, t) * (1f - Smooth(1.2f, 1.9f, t));
            }
            Bubbles(data, rate, 34, 4602, 0.45f);
            Throat(data, rate, (int)(1.0f * rate), 0.95f, 4603,
                pitch: t => 48f - 8f * t, envelope: t => Smooth(0f, 0.2f, t) * (1f - Smooth(0.5f, 0.92f, t)),
                growl: t => 0.8f, breath: t => 0.5f, open: t => 0.1f, drive: 2f, gain: 0.35f);
            return Clip("Undertow_Gorgolejo", data, rate);
        }

        /// <summary>O puxão: um tranco grave de água, um rosnado curto e um gorgolejo descendo. 1,6 s.</summary>
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
            Throat(data, rate, (int)(0.05f * rate), 0.9f, 4405,
                pitch: t => Mathf.Lerp(120f, 60f, t / 0.9f), envelope: t => Smooth(0f, 0.04f, t) * (1f - Smooth(0.4f, 0.88f, t)),
                growl: t => 0.6f, breath: t => 0.6f, open: t => 0.6f - 0.5f * t, drive: 3f, gain: 0.6f);
            Bubbles(data, rate, 30, 4405, 0.5f);
            return Clip("Undertow_Puxao", data, rate);
        }

        // Bolhas: pios curtos subindo de tom.
        static void Bubbles(float[] data, int rate, int count, int seed, float gain)
        {
            var rng = new System.Random(seed);
            for (int b = 0; b < count; b++)
            {
                int start = rng.Next(0, Mathf.Max(1, data.Length - rate / 10));
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
                for (int i = 0; i < data.Length; i++) data[i] *= 0.89f / peak;
            AudioClip clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
