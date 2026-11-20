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

        void OnEnable()
        {
            Instance = this;

            _interact = new InputAction("Interagir", InputActionType.Button, "<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonWest");
            _interact.Enable();
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
            if (LocalWaiting) LocalWaiting = false;
            else if (LocalInReach && !Locked && ExitLock.Prompt == null && SwitchObjective.Prompt == null) LocalWaiting = true;
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
    }
}
