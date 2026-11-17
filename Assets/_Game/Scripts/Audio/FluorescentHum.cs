using UnityEngine;

namespace BackRooms.Audio
{
    /// <summary>
    /// Zumbido de lâmpada fluorescente, sintetizado na inicialização (sem arquivos de áudio).
    /// É a soma dos harmônicos de 120 Hz (o dobro da rede elétrica de 60 Hz), o que dá o
    /// timbre áspero característico. O loop de 2 s fecha sem emenda porque todos os
    /// componentes completam um número inteiro de ciclos nesse intervalo.
    ///
    /// Só se ouve jogando: com o menu na tela fica calado (volume zero, a fonte segue tocando em
    /// laço para não estalar), entra suave quando a partida começa, cruzando com a saída da
    /// música do menu, e sai suave de volta ao menu. O critério é o da música
    /// (<see cref="MenuMusic.MenuOnScreen"/>): conectando ou entre cenas, mantém o que fazia. Cada
    /// cena tem o seu e ele nasce calado, então a fase que abre já conectando (porta de saída)
    /// também começa em silêncio. Sem dispositivo de áudio (testes em lote) o estado continua
    /// sendo calculado.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class FluorescentHum : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.22f;
        [Tooltip("Quantidade de harmônicos. Mais harmônicos = zumbido mais áspero.")]
        [Range(4, 60)] public int harmonics = 36;
        [Tooltip("Segundos para entrar ao começar a jogar (do silêncio ao volume de jogo).")]
        public float fadeInSeconds = 1.5f;
        [Tooltip("Segundos para sair ao voltar ao menu.")]
        public float fadeOutSeconds = 1.5f;

        /// <summary>Verdadeiro jogando, quando o zumbido deve soar; falso com o menu na tela.</summary>
        public bool Wanted { get; private set; }

        /// <summary>Nível do fade, de 0 (calado) a 1 (volume de jogo).</summary>
        public float Level { get; private set; }

        /// <summary>O volume que a conta pede agora (0 no menu, <see cref="volume"/> jogando).</summary>
        public float CurrentVolume => volume * Level * Level;

        AudioSource _source;

        void Start()
        {
            _source = GetComponent<AudioSource>();
            _source.clip = CreateLoop(harmonics);
            _source.loop = true;
            _source.spatialBlend = 0f;
            _source.volume = 0f;
            _source.Play();
        }

        void Update()
        {
            bool? menu = MenuMusic.MenuOnScreen();
            if (menu.HasValue) Wanted = !menu.Value;

            // Passo por quadro limitado, como na música: os quadros longos da montagem do mapa e
            // do jogador caem bem no começo do fade e, sem o limite, a entrada viraria um corte.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float rate = Wanted ? dt / Mathf.Max(0.01f, fadeInSeconds) : -dt / Mathf.Max(0.01f, fadeOutSeconds);
            Level = Mathf.Clamp01(Level + rate);

            if (_source != null) _source.volume = CurrentVolume;
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
