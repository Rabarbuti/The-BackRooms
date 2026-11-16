using BackRooms.Net;
using UnityEngine;

namespace BackRooms.Audio
{
    /// <summary>
    /// Música da tela inicial (Audio/Musica/Menu_Tema.mp3). Toca em laço enquanto o menu está na
    /// tela, entra e sai suave, e some quando a partida começa. Só observa a sessão
    /// (<see cref="GameSession.Phase"/>): no menu, toca; jogando, silencia; conectando, ou entre
    /// duas cenas (sem sessão), mantém o que estava fazendo. Assim, ao hospedar, a música vai
    /// sumindo até o jogador aparecer no mapa, e na passagem pela porta de saída a fase seguinte
    /// já abre conectando e ela continua calada.
    ///
    /// Um dono só: cada cena (Level 0, 1 e 2) traz o seu objeto, mas o primeiro que acorda
    /// sobrevive à troca de cena e os das cenas seguintes se apagam ao nascer. Trocar de fase pela
    /// lista do menu não reinicia nem duplica a música. Calada, ela fica em pausa e, de volta ao
    /// menu, continua do mesmo ponto.
    ///
    /// Som 2D, fora de reverberação e de efeitos do ouvinte. Sem dispositivo de áudio (testes em
    /// lote) o estado continua sendo calculado, só não se ouve nada.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MenuMusic : MonoBehaviour
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.4f;
        [Tooltip("Segundos para entrar (do silêncio ao volume cheio).")]
        public float fadeInSeconds = 1.5f;
        [Tooltip("Segundos para sair ao começar a jogar.")]
        public float fadeOutSeconds = 1.5f;

        /// <summary>O dono da música (o que sobreviveu às trocas de cena). Nulo fora do Play.</summary>
        public static MenuMusic Instance { get; private set; }

        /// <summary>Quantos donos existem agora. Tem de ser 1 em Play (os testes conferem).</summary>
        public static int Alive { get; private set; }

        /// <summary>Verdadeiro quando o menu está na tela e a música deve tocar.</summary>
        public bool Wanted { get; private set; } = true;

        /// <summary>Nível do fade, de 0 (calada) a 1 (volume cheio).</summary>
        public float Level { get; private set; }

        /// <summary>Quantas vezes a música começou do início. Fica em 1: trocar de fase não reinicia.</summary>
        public int Starts { get; private set; }

        /// <summary>Segundos de música já tocados (pela própria conta, vale também sem áudio).</summary>
        public float Played { get; private set; }

        /// <summary>Verdadeiro com a música em pausa (calada durante a partida).</summary>
        public bool Paused => _paused;

        AudioSource _source;
        bool _counted;

        // Sem recarregar o domínio ao entrar em Play, os estáticos sobrariam do Play anterior.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Alive = 0;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Já existe um dono, vindo de outra cena: este se apaga antes de tocar.
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _counted = true;
            Alive++;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);

            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.clip = clip;
            _source.loop = true;
            _source.spatialBlend = 0f;
            _source.priority = 0;
            _source.bypassEffects = true;
            _source.bypassListenerEffects = true;
            _source.bypassReverbZones = true;
            _source.dopplerLevel = 0f;
            _source.volume = 0f;
        }

        void OnDestroy()
        {
            if (_counted) Alive--;
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            GameSession session = GameSession.Instance;
            if (session != null)
            {
                if (session.Phase == SessionPhase.Menu) Wanted = true;
                else if (session.Phase == SessionPhase.Playing) Wanted = false;
                // Conectando: mantém. Hospedar já tira o menu da tela, mas a música só termina de
                // sair quando o jogador aparece; uma conexão que falha volta ao menu sem corte.
            }

            float dt = Time.unscaledDeltaTime;
            float rate = Wanted ? dt / Mathf.Max(0.01f, fadeInSeconds) : -dt / Mathf.Max(0.01f, fadeOutSeconds);
            Level = Mathf.Clamp01(Level + rate);

            if (clip == null || _source == null) return;

            _source.volume = volume * Level * Level;

            // O estado é a conta deste componente, não o isPlaying da fonte: sem dispositivo de
            // áudio a fonte nunca diz que está tocando, e a música recomeçaria a cada quadro.
            if (Level > 0f)
            {
                Played += dt;
                if (!_started)
                {
                    _started = true;
                    Starts++;
                    _source.Play();
                }
                else if (_paused)
                {
                    _paused = false;
                    _source.UnPause();
                }
            }
            else if (_started && !_paused)
            {
                // Calada: pausa (não para), para voltar do mesmo ponto.
                _paused = true;
                _source.Pause();
            }
        }

        bool _started, _paused;
    }
}
