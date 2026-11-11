using UnityEngine;

namespace BackRooms.Player
{
    /// <summary>
    /// Passos em carpete, sintetizados na inicialização (sem arquivos de áudio).
    /// Cada passo é ruído filtrado na faixa de 170 a ~700 Hz (o corpo do som, audível até
    /// em caixa pequena) mais um baque grave, em dois apoios: calcanhar e ponta do pé.
    /// Quase nada passa de 3 kHz — é o chiado agudo que faz um passo soar como areia.
    /// Cada clipe é normalizado, então o volume final depende só do campo <see cref="volume"/>.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class Footsteps : MonoBehaviour
    {
        public PlayerController controller;
        [Range(0f, 1f)] public float volume = 0.55f;

        const int Variations = 6;

        AudioSource _source;
        AudioClip[] _clips;
        int _last = -1;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            _clips = new AudioClip[Variations];
            var rng = new System.Random(7919);
            for (int i = 0; i < Variations; i++) _clips[i] = CreateStep(rng, i);

            if (controller == null) controller = GetComponent<PlayerController>();
        }

        void OnEnable()
        {
            if (controller != null) controller.Stepped += OnStepped;
        }

        void OnDisable()
        {
            if (controller != null) controller.Stepped -= OnStepped;
        }

        bool _remote;
        Vector3 _lastPosition;
        float _phase;
        float _smoothedSpeed;

        /// <summary>
        /// Passos de outro jogador: o som vira 3D e a cadência é calculada pelo deslocamento,
        /// já que o controlador dele não roda nesta máquina.
        /// </summary>
        public void SetRemote()
        {
            _remote = true;
            _lastPosition = transform.position;

            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 1.5f;
            _source.maxDistance = 24f;
            _source.dopplerLevel = 0f;
        }

        void Update()
        {
            if (!_remote || Time.deltaTime <= 0f) return;

            Vector3 delta = transform.position - _lastPosition;
            _lastPosition = transform.position;
            delta.y = 0f;

            // Um salto grande é teletransporte (entrada na fase), não corrida.
            float speed = delta.magnitude / Time.deltaTime;
            if (speed > 12f) speed = 0f;
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, speed, 1f - Mathf.Exp(-10f * Time.deltaTime));

            if (_smoothedSpeed < 0.4f) return;

            float baseRate = controller != null ? controller.stepBaseRate : 1.2f;
            float perSpeed = controller != null ? controller.stepRatePerSpeed : 0.3f;

            float before = _phase;
            _phase += (baseRate + perSpeed * _smoothedSpeed) * Time.deltaTime;
            if (Mathf.FloorToInt(_phase) == Mathf.FloorToInt(before)) return;
            if (_phase > 1000f) _phase -= 1000f;

            OnStepped(_smoothedSpeed < 2f ? 0.25f : _smoothedSpeed > 4f ? 1f : 0.55f);
        }

        void OnStepped(float loudness)
        {
            // Evita repetir a mesma variação duas vezes seguidas.
            int index = Random.Range(0, Variations - 1);
            if (index >= _last && _last >= 0) index++;
            _last = index;

            _source.pitch = Random.Range(0.94f, 1.06f);
            _source.PlayOneShot(_clips[index], volume * Mathf.Lerp(0.5f, 1f, loudness));
        }

        static AudioClip CreateStep(System.Random rng, int index)
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int length = (int)(rate * 0.20f);
            var samples = new float[length];

            float thumpFrequency = 105f + (float)rng.NextDouble() * 30f;
            float toeDelay = 0.060f + (float)rng.NextDouble() * 0.030f;
            float toeLevel = 0.45f + (float)rng.NextDouble() * 0.20f;

            // Passa-baixas de dois polos (620 a 820 Hz) e passa-altas de um polo (170 Hz).
            float lowCut = 1f - Mathf.Exp(-2f * Mathf.PI * (620f + (float)rng.NextDouble() * 200f) / rate);
            float highCut = 1f - Mathf.Exp(-2f * Mathf.PI * 170f / rate);
            float l1 = 0f, l2 = 0f, h = 0f;
            float peak = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float tt = t - toeDelay;

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                l1 += (noise - l1) * lowCut;
                l2 += (l1 - l2) * lowCut;
                h += (l2 - h) * highCut;
                float band = l2 - h;

                // Dois apoios: calcanhar e, um instante depois, a ponta do pé (mais fraca).
                float heelEnvelope = Mathf.Exp(-t * 42f) * Mathf.Clamp01(t / 0.005f);
                float toeEnvelope = tt < 0f ? 0f : Mathf.Exp(-tt * 55f) * toeLevel * Mathf.Clamp01(tt / 0.005f);

                // Baque grave que desce de frequência.
                float thumpPitch = thumpFrequency * (1f + 0.5f * Mathf.Exp(-t * 70f));
                float thump = Mathf.Sin(2f * Mathf.PI * thumpPitch * t) * Mathf.Exp(-t * 36f) * Mathf.Clamp01(t / 0.005f);

                float release = Mathf.Clamp01((length - i) / (rate * 0.02f));
                samples[i] = (band * 5f * (heelEnvelope + toeEnvelope) + thump * 0.4f) * release;

                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }

            if (peak > 0f)
            {
                float gain = 0.9f / peak;
                for (int i = 0; i < length; i++) samples[i] *= gain;
            }

            var clip = AudioClip.Create($"Passo_{index}", length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
