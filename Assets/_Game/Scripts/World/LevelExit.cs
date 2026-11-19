using BackRooms.Net;
using BackRooms.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BackRooms.World
{
    /// <summary>
    /// A porta de saída. O jogador chega perto, olha para ela e aperta E: fica com a mão na
    /// porta, esperando. Quando todos os jogadores da sessão estão esperando, o host abre a
    /// porta e todo mundo passa de fase junto (<see cref="GameSession"/> faz a troca). Sozinho,
    /// passa na hora.
    /// Quem está esperando continua no mapa: pode desistir apertando E de novo, e desiste
    /// sozinho se se afastar da porta, cair num buraco ou for pego.
    /// Este componente só cuida do jogador desta máquina; o estado de cada um trafega pelo
    /// <see cref="PlayerNetwork.AtExit"/>.
    /// </summary>
    public sealed class LevelExit : MonoBehaviour
    {
        public static LevelExit Instance { get; private set; }

        [Tooltip("A porta em si (o pé dela, no chão). Sem isso, vale a posição deste objeto.")]
        public Transform door;
        [Tooltip("Distância máxima do olho à porta para alcançá-la, em metros.")]
        public float reach = 2.4f;
        [Tooltip("Quem está esperando e se afasta mais que isto da porta desiste, em metros.")]
        public float waitRadius = 4f;

        /// <summary>O jogador desta máquina está perto e olhando para a porta.</summary>
        public bool LocalInReach { get; private set; }

        /// <summary>O jogador desta máquina está com a mão na porta, esperando os outros.</summary>
        public bool LocalWaiting { get; private set; }

        InputAction _interact;
        AudioSource _audio;
        AudioClip _rattle, _open;
        bool _wasPassing;

        /// <summary>Quantas vezes a porta chacoalhou trancada (testes).</summary>
        public int Rattles { get; private set; }

        /// <summary>A porta tocou o som de abrir ao passar de fase (testes).</summary>
        public bool OpenedSound { get; private set; }

        void OnEnable()
        {
            Instance = this;

            _interact = new InputAction("Interagir", InputActionType.Button, "<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonWest");
            _interact.Enable();

            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.spatialBlend = 1f;
                _audio.minDistance = 1.5f;
                _audio.maxDistance = 22f;
                _audio.rolloffMode = AudioRolloffMode.Linear;
                _audio.playOnAwake = false;
                BuildClips();
            }
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;

            _interact.Disable();
            _interact.Dispose();
            _interact = null;
        }

        /// <summary>O pé da porta, no chão.</summary>
        public Vector3 DoorPosition => door != null ? door.position : transform.position;

        /// <summary>
        /// Trancada pelo teclado do Level 1 (<see cref="ExitLock"/>) até alguém acertar o código, ou
        /// pelo objetivo comum (<see cref="SwitchObjective"/>: disjuntores, válvulas, alarmes) até ele
        /// se cumprir.
        /// </summary>
        public static bool Locked => ExitLock.Instance != null && !ExitLock.Instance.Unlocked || SwitchObjective.Locked;

        void Update()
        {
            PlayerNetwork local = PlayerNetwork.Local;
            GameSession session = GameSession.Instance;

            // A fase acabou: a porta abre (barra antipânico, rangido e a batida atrás do grupo).
            bool passing = session != null && session.Passing;
            if (passing && !_wasPassing && _audio != null && _open != null)
            {
                _audio.PlayOneShot(_open, 1f);
                OpenedSound = true;
            }
            _wasPassing = passing;

            if (local == null || !local.InGame || (session != null && session.Passing))
            {
                LocalInReach = false;
                if (local == null || !local.InGame) LocalWaiting = false;
                return;
            }

            PlayerController body = local.controller;
            LocalInReach = !body.IsReturning && InReach(local.playerCamera.transform);

            if (LocalWaiting && (body.IsReturning || Flat(body.transform.position - DoorPosition).magnitude > waitRadius))
                LocalWaiting = false;

            if (_interact.WasPressedThisFrame()) Interact();
        }

        /// <summary>
        /// O que a tecla E faz: põe a mão na porta, ou tira se já estava. Devolve se o jogador
        /// ficou esperando. Público para os testes.
        /// </summary>
        public bool Interact()
        {
            // Trancada: a maçaneta chacoalha (o painel diz o que falta).
            if (!LocalWaiting && LocalInReach && Locked && ExitLock.Prompt == null && SwitchObjective.Prompt == null && BatteryPickups.Prompt == null)
            {
                Rattles++;
                if (_audio != null && _rattle != null) _audio.PlayOneShot(_rattle, 0.9f);
            }
            if (LocalWaiting) LocalWaiting = false;
            else if (LocalInReach && !Locked && ExitLock.Prompt == null && SwitchObjective.Prompt == null && BatteryPickups.Prompt == null) LocalWaiting = true;
            return LocalWaiting;
        }

        // Perto o bastante e com a porta na frente. A altura do olhar não importa: mede contra
        // o ponto da porta na altura do olho.
        bool InReach(Transform eye)
        {
            Vector3 foot = DoorPosition;
            Vector3 target = new Vector3(foot.x, Mathf.Clamp(eye.position.y, foot.y + 0.3f, foot.y + 1.9f), foot.z);
            Vector3 to = target - eye.position;

            if (to.magnitude > reach) return false;
            return Vector3.Dot(Flat(eye.forward).normalized, Flat(to).normalized) > 0.6f;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // Sons sintetizados: a maçaneta chacoalhando e a porta abrindo e batendo.
        void BuildClips()
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            var rng = new System.Random(5150);

            int n = (int)(rate * 0.55f);
            var rattle = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float v = 0f;
                for (int k = 0; k < 4; k++)
                {
                    float start = k * 0.12f;
                    if (t < start) continue;
                    float u = t - start;
                    v += (Mathf.Sin(2f * Mathf.PI * 1650f * u) * 0.4f + (float)(rng.NextDouble() * 2.0 - 1.0) * 0.6f) * Mathf.Exp(-u * 60f);
                    v += Mathf.Sin(2f * Mathf.PI * 140f * u) * Mathf.Exp(-u * 25f) * 0.5f;
                }
                rattle[i] = v * 0.6f;
            }
            _rattle = AudioClip.Create("Porta_Trancada", n, 1, rate, false);
            _rattle.SetData(rattle, 0);

            int m = (int)(rate * 1.5f);
            var open = new float[m];
            for (int i = 0; i < m; i++)
            {
                float t = i / (float)rate;
                float bar = ((float)(rng.NextDouble() * 2.0 - 1.0) * 0.7f + Mathf.Sin(2f * Mathf.PI * 900f * t) * 0.3f) * Mathf.Exp(-t * 40f);
                float creak = t > 0.1f && t < 1.0f ? Mathf.Sin(2f * Mathf.PI * (210f + 120f * t + 25f * Mathf.Sin(t * 60f)) * t) * 0.25f * Mathf.Sin(Mathf.PI * (t - 0.1f) / 0.9f) : 0f;
                float slam = t > 1.15f ? (Mathf.Sin(2f * Mathf.PI * 70f * (t - 1.15f)) + (float)(rng.NextDouble() * 2.0 - 1.0) * 0.5f) * Mathf.Exp(-(t - 1.15f) * 14f) : 0f;
                open[i] = (bar + creak + slam * 0.8f) * 0.7f;
            }
            _open = AudioClip.Create("Porta_Abrindo", m, 1, rate, false);
            _open.SetData(open, 0);
        }
    }
}
