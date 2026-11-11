using BackRooms.Net;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    public enum PowerPhase : byte
    {
        /// <summary>Luz normal.</summary>
        Steady,
        /// <summary>Aviso: as luminárias piscam de vez em quando.</summary>
        Warning,
        /// <summary>Últimos segundos do aviso: piscam bem mais rápido.</summary>
        Hurry,
        /// <summary>Tudo apagado.</summary>
        Dark
    }

    /// <summary>
    /// Apagão: de tempos em tempos todas as luminárias do andar se apagam. Antes disso elas
    /// avisam, piscando — devagar primeiro, depressa nos últimos segundos.
    ///
    /// O relógio é o da sessão (<see cref="GameSession.SessionSeconds"/>), que vem do host e é
    /// igual em todas as máquinas: ninguém precisa mandar "apagou" pela rede, cada máquina faz
    /// a conta e todas apagam juntas. Só o desenho de cada piscada é local.
    ///
    /// No escuro some também a luz ambiente (um Volume por cima do da cena escurece o céu e a
    /// névoa); ficam as lanternas, a placa da saída e os olhos da entidade.
    /// </summary>
    public sealed class LightsOut : MonoBehaviour
    {
        public static LightsOut Instance { get; private set; }

        public GameSession session;

        [Header("Tempos (segundos)")]
        [Tooltip("Quando acontece o primeiro apagão, contado do começo da partida.")]
        public float firstDarkAt = 60f;
        [Tooltip("Quanto dura cada apagão.")]
        public float darkSeconds = 10f;
        [Tooltip("Quanto tempo de luz entre o fim de um apagão e o começo do seguinte (inclui o aviso).")]
        public float litSeconds = 30f;
        [Tooltip("Quanto tempo antes as luminárias começam a piscar.")]
        public float warningSeconds = 10f;
        [Tooltip("Nos últimos segundos do aviso elas piscam bem mais rápido.")]
        public float hurrySeconds = 2f;

        [Header("Escuro")]
        [Tooltip("Quanto da luz ambiente sobra durante o apagão (0 = breu total).")]
        [Range(0f, 0.2f)] public float ambientLeft = 0.012f;
        [Tooltip("Desligado, as luzes nunca se apagam.")]
        public bool eventEnabled = true;

        public PowerPhase Phase { get; private set; } = PowerPhase.Steady;

        /// <summary>Segundos até a próxima mudança entre claro e escuro. -1 fora de partida.</summary>
        public float SecondsToChange { get; private set; } = -1f;

        Volume _volume;
        VolumeProfile _profile;
        float _darkness;

        void Awake()
        {
            Instance = this;
            if (session == null) session = GetComponent<GameSession>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_profile != null) Destroy(_profile);
        }

        void Update()
        {
            double seconds = session != null && eventEnabled ? session.SessionSeconds : -1.0;
            PowerPhase phase = PhaseAt(seconds, out float toChange);
            SecondsToChange = toChange;

            if (phase != Phase)
            {
                Phase = phase;
                foreach (CeilingLight light in CeilingLight.All) light.SetPower(phase);
            }

            // O breu entra e sai rápido, mas não de um quadro para o outro.
            float target = phase == PowerPhase.Dark ? 1f : 0f;
            if (!Mathf.Approximately(_darkness, target)) SetDarkness(Mathf.MoveTowards(_darkness, target, Time.deltaTime * 6f));
        }

        /// <summary>Em que fase o andar está com <paramref name="seconds"/> de partida. Negativo = fora de partida.</summary>
        public PowerPhase PhaseAt(double seconds, out float toChange)
        {
            toChange = -1f;
            if (seconds < 0.0 || darkSeconds <= 0f) return PowerPhase.Steady;

            double untilDark;

            if (seconds < firstDarkAt)
            {
                untilDark = firstDarkAt - seconds;
            }
            else
            {
                double cycle = darkSeconds + Mathf.Max(0.1f, litSeconds);
                double into = (seconds - firstDarkAt) % cycle;

                if (into < darkSeconds)
                {
                    toChange = (float)(darkSeconds - into);
                    return PowerPhase.Dark;
                }

                untilDark = cycle - into;
            }

            toChange = (float)untilDark;
            if (untilDark <= hurrySeconds) return PowerPhase.Hurry;
            return untilDark <= warningSeconds ? PowerPhase.Warning : PowerPhase.Steady;
        }

        /// <summary>
        /// Luz ambiente: 0 = normal, 1 = apagão. Público para a ferramenta de prévia, que
        /// fotografa o escuro fora do Play.
        /// </summary>
        public void SetDarkness(float darkness)
        {
            _darkness = Mathf.Clamp01(darkness);

            if (_volume == null)
            {
                if (_darkness <= 0f) return;
                CreateVolume();
            }

            _volume.weight = _darkness;
            _volume.enabled = _darkness > 0f;
        }

        // Um Volume global com prioridade acima do da cena: céu quase preto (é dele que vem a
        // luz ambiente) e névoa preta. O peso do Volume faz a mistura com o normal.
        void CreateVolume()
        {
            var go = new GameObject("Apagao") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.hideFlags = HideFlags.DontSave;

            var sky = _profile.Add<GradientSky>(true);
            var faint = new Color(1f, 0.97f, 0.8f) * ambientLeft;
            sky.top.Override(faint);
            sky.middle.Override(faint);
            sky.bottom.Override(faint);

            var fog = _profile.Add<Fog>(true);
            fog.color.Override(Color.black);

            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 50f;
            _volume.sharedProfile = _profile;
        }
    }
}
