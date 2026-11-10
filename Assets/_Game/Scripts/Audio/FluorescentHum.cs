using UnityEngine;

namespace BackRooms.Audio
{
    /// <summary>
    /// Zumbido de lâmpada fluorescente, sintetizado na inicialização (sem arquivos de áudio).
    /// É a soma dos harmônicos de 120 Hz (o dobro da rede elétrica de 60 Hz), o que dá o
    /// timbre áspero característico. O loop de 2 s fecha sem emenda porque todos os
    /// componentes completam um número inteiro de ciclos nesse intervalo.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class FluorescentHum : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.22f;
        [Tooltip("Quantidade de harmônicos. Mais harmônicos = zumbido mais áspero.")]
        [Range(4, 60)] public int harmonics = 36;

        void Start()
        {
            var source = GetComponent<AudioSource>();
            source.clip = CreateLoop(harmonics);
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = volume;
            source.Play();
        }

        static AudioClip CreateLoop(int harmonicCount)
        {
            const float seconds = 2f;
            const float fundamental = 120f;

            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            var rng = new System.Random(120);

            // Um pouco de 60 Hz por baixo, como o transformador do reator.
            Accumulate(samples, rate, 60f, 0.25f, 0f);

            for (int n = 1; n <= harmonicCount; n++)
            {
                float frequency = fundamental * n;
                if (frequency > rate * 0.45f) break;

                float amplitude = 1f / Mathf.Pow(n, 1.15f);
                // Realce na faixa de 2 a 4 kHz, onde o zumbido incomoda.
                if (frequency > 2000f && frequency < 4200f) amplitude *= 2.2f;

                Accumulate(samples, rate, frequency, amplitude, (float)rng.NextDouble() * Mathf.PI * 2f);
            }

            float peak = 0f;
            for (int i = 0; i < length; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            if (peak > 0f)
            {
                float gain = 0.8f / peak;
                for (int i = 0; i < length; i++) samples[i] *= gain;
            }

            var clip = AudioClip.Create("ZumbidoFluorescente", length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static void Accumulate(float[] samples, int rate, float frequency, float amplitude, float phase)
        {
            float step = 2f * Mathf.PI * frequency / rate;
            for (int i = 0; i < samples.Length; i++)
                samples[i] += Mathf.Sin(phase + step * i) * amplitude;
        }
    }
}
