using System.Collections;
using BackRooms.Player;
using Unity.Netcode;
using UnityEngine;

namespace BackRooms.Net
{
    /// <summary>
    /// Liga o prefab do jogador à rede. Na máquina do dono, habilita controle, câmera e
    /// lanterna. Nas outras, o prefab vira só a representação daquele jogador: corpo visível,
    /// posição pelo NetworkTransform e, por aqui, inclinação da cabeça, agachar e lanterna.
    /// No prefab, controle, câmera e ouvinte de áudio ficam desligados; quem liga é este script.
    /// </summary>
    public sealed class PlayerNetwork : NetworkBehaviour
    {
        [Header("Referências")]
        public PlayerController controller;
        public Flashlight flashlight;
        public Footsteps footsteps;
        public Camera playerCamera;
        public AudioListener listener;
        public Transform cameraPivot;
        [Tooltip("Corpo visto pelos outros jogadores. O dono não vê o próprio.")]
        public GameObject body;

        const float SendInterval = 1f / 15f;

        readonly NetworkVariable<float> _pitch = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<bool> _flashlightOn = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<bool> _crouching = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        float _nextSend;
        float _shownPitch;
        float _shownEye;
        bool _ready;

        public override void OnNetworkSpawn()
        {
            _shownEye = controller.standingEyeHeight;

            if (IsOwner) StartCoroutine(BeginLocal());
            else BeginRemote();
        }

        public override void OnNetworkDespawn()
        {
            _ready = false;
        }

        IEnumerator BeginLocal()
        {
            if (body != null) body.SetActive(false);

            // O mapa é gerado nesta máquina a partir da seed; espera ele existir para ter chão.
            GameSession session = GameSession.Instance;
            while (session != null && !session.LevelReady) yield return null;

            Vector3 spawn = session != null ? session.SpawnPoint(OwnerClientId) : transform.position;
            controller.Teleport(spawn, 0f);

            playerCamera.enabled = true;
            listener.enabled = true;
            flashlight.inputEnabled = true;
            controller.enabled = true;
            _ready = true;

            if (session != null) session.NotifyLocalPlayerReady();
        }

        void BeginRemote()
        {
            controller.enabled = false;
            playerCamera.enabled = false;
            listener.enabled = false;
            flashlight.inputEnabled = false;

            // Quem move este objeto é o NetworkTransform; o CharacterController atrapalharia.
            if (TryGetComponent(out CharacterController characterController)) characterController.enabled = false;

            if (footsteps != null) footsteps.SetRemote();
            if (body != null) body.SetActive(true);
            _ready = true;
        }

        void Update()
        {
            if (!IsSpawned || !_ready) return;

            if (IsOwner) SendState();
            else ShowState();
        }

        // Dono: publica o que os outros precisam ver. A posição vai pelo NetworkTransform.
        void SendState()
        {
            if (_flashlightOn.Value != flashlight.IsOn) _flashlightOn.Value = flashlight.IsOn;
            if (_crouching.Value != controller.IsCrouching) _crouching.Value = controller.IsCrouching;

            if (Time.unscaledTime < _nextSend) return;
            _nextSend = Time.unscaledTime + SendInterval;

            // Arredonda para graus inteiros: evita mandar atualização por tremidas do mouse.
            float pitch = Mathf.Round(controller.Pitch);
            if (Mathf.Abs(pitch - _pitch.Value) >= 1f) _pitch.Value = pitch;
        }

        // Demais máquinas: aplica o estado recebido, suavizando.
        void ShowState()
        {
            float dt = Time.deltaTime;
            bool crouching = _crouching.Value;

            _shownPitch = Mathf.Lerp(_shownPitch, _pitch.Value, 1f - Mathf.Exp(-12f * dt));
            _shownEye = Mathf.MoveTowards(_shownEye, crouching ? controller.crouchingEyeHeight : controller.standingEyeHeight, 4f * dt);

            cameraPivot.localPosition = new Vector3(0f, _shownEye, 0f);
            cameraPivot.localRotation = Quaternion.Euler(_shownPitch, 0f, 0f);

            flashlight.SetOn(_flashlightOn.Value);

            if (body != null)
            {
                float height = _shownEye / controller.standingEyeHeight;
                body.transform.localScale = new Vector3(1f, height, 1f);
            }
        }
    }
}
