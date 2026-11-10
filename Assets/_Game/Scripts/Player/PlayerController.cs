using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BackRooms.Player
{
    /// <summary>
    /// Movimento em primeira pessoa: andar, correr com fôlego, agachar, pular, olhar e
    /// balanço de câmera. As ações de input são criadas em código para não depender de asset.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Referências")]
        public Transform cameraPivot;

        [Header("Velocidade (m/s)")]
        public float walkSpeed = 2.8f;
        public float sprintSpeed = 5.0f;
        public float crouchSpeed = 1.4f;
        public float acceleration = 20f;

        [Header("Salto e gravidade")]
        public float gravity = 20f;
        public float jumpHeight = 0.8f;

        [Header("Fôlego (segundos)")]
        public float staminaSeconds = 6f;
        public float staminaRegenDelay = 2f;
        public float staminaRegenSeconds = 4f;

        [Header("Corpo (metros)")]
        public float standingHeight = 1.8f;
        public float crouchingHeight = 1.1f;
        public float standingEyeHeight = 1.65f;
        public float crouchingEyeHeight = 0.95f;

        [Header("Câmera")]
        public float mouseSensitivity = 0.1f;
        public float stickSensitivity = 150f;
        public bool headBob = true;
        public float headBobAmplitude = 0.025f;

        [Header("Passos por segundo = base + fator x velocidade")]
        [Tooltip("Com 1,2 e 0,3: agachado 1,6 | andando 2,0 | correndo 2,7 passos por segundo.")]
        public float stepBaseRate = 1.2f;
        public float stepRatePerSpeed = 0.3f;

        /// <summary>Disparado a cada passo, com a intensidade do ruído (0 a 1).</summary>
        public event Action<float> Stepped;

        /// <summary>Fôlego restante, de 0 a 1.</summary>
        public float Stamina01 => staminaSeconds <= 0f ? 1f : _stamina / staminaSeconds;
        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }

        /// <summary>
        /// Para testes automáticos sem janela: lá o cursor nunca chega a ficar preso, então
        /// o teste liga isto para o jogo se comportar como se estivesse.
        /// </summary>
        public static bool AssumeMouseCaptured;

        /// <summary>
        /// Verdadeiro quando o mouse está preso ao jogo. Com ele solto (Esc), mover o mouse
        /// não gira a câmera e clicar não usa itens.
        /// </summary>
        public static bool MouseCaptured => AssumeMouseCaptured || Cursor.lockState == CursorLockMode.Locked;

        /// <summary>Verdadeiro enquanto o jogador está dentro de um esconderijo.</summary>
        public bool IsHidden => _hidingSpots > 0;

        int _hidingSpots;

        public void SetHidden(bool inside)
        {
            _hidingSpots = Mathf.Max(0, _hidingSpots + (inside ? 1 : -1));
        }

        CharacterController _controller;
        InputAction _move, _lookMouse, _lookStick, _jump, _sprint, _crouch;

        Vector3 _horizontalVelocity;
        float _verticalVelocity;
        float _pitch;
        float _stamina;
        float _timeSinceSprint;
        bool _exhausted;
        float _eyeHeight;
        float _bobPhase;
        Vector3 _lastSafePosition;
        float _safeTimer;
        bool _lockedCursor;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            // Com o valor padrão (1 mm), movimentos muito pequenos em taxas de quadro altas
            // são descartados e o controlador deixa de se reportar como apoiado no chão.
            _controller.minMoveDistance = 0f;
            _stamina = staminaSeconds;
            _eyeHeight = standingEyeHeight;
            _lastSafePosition = transform.position;
            CreateActions();
        }

        void OnEnable()
        {
            _move.Enable(); _lookMouse.Enable(); _lookStick.Enable();
            _jump.Enable(); _sprint.Enable(); _crouch.Enable();
        }

        void OnDisable()
        {
            _move.Disable(); _lookMouse.Disable(); _lookStick.Disable();
            _jump.Disable(); _sprint.Disable(); _crouch.Disable();
        }

        void OnDestroy()
        {
            _move.Dispose(); _lookMouse.Dispose(); _lookStick.Dispose();
            _jump.Dispose(); _sprint.Dispose(); _crouch.Dispose();

            // Só o jogador local chega a travar o cursor; ao sair da sessão, devolve.
            if (_lockedCursor) SetCursorLocked(false);
        }

        void Start()
        {
            _lockedCursor = true;
            SetCursorLocked(true);
        }

        /// <summary>Inclinação atual da câmera, em graus (positivo = olhando para baixo).</summary>
        public float Pitch => _pitch;

        /// <summary>Move o jogador de uma vez, sem colisão. Usado para colocá-lo no ponto de início.</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _controller.enabled = wasEnabled;

            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _lastSafePosition = position;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            UpdateCursor();
            UpdateLook(dt);
            UpdateCrouch(dt);
            UpdateMovement(dt);
            UpdateCamera(dt);
            UpdateSafety(dt);
        }

        void CreateActions()
        {
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");

            _lookMouse = new InputAction("LookMouse", InputActionType.Value, "<Mouse>/delta");
            _lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");

            _jump = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            _sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/leftStickPress");

            _crouch = new InputAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            _crouch.AddBinding("<Keyboard>/c");
            _crouch.AddBinding("<Gamepad>/buttonEast");
        }

        void UpdateCursor()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) SetCursorLocked(true);
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void UpdateLook(float dt)
        {
            if (!MouseCaptured) return;

            // O delta do mouse já é por quadro; o analógico é uma taxa e precisa do dt.
            Vector2 look = _lookMouse.ReadValue<Vector2>() * mouseSensitivity
                         + _lookStick.ReadValue<Vector2>() * (stickSensitivity * dt);

            transform.Rotate(0f, look.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - look.y, -85f, 85f);
        }

        void UpdateCrouch(float dt)
        {
            bool wantsCrouch = _crouch.IsPressed();

            // Só levanta se houver espaço acima da cabeça.
            if (!wantsCrouch && IsCrouching && !HasHeadroom()) wantsCrouch = true;
            IsCrouching = wantsCrouch;

            float targetHeight = IsCrouching ? crouchingHeight : standingHeight;
            float newHeight = Mathf.MoveTowards(_controller.height, targetHeight, 4f * dt);
            _controller.height = newHeight;
            _controller.center = new Vector3(0f, newHeight * 0.5f, 0f);

            float targetEye = IsCrouching ? crouchingEyeHeight : standingEyeHeight;
            _eyeHeight = Mathf.MoveTowards(_eyeHeight, targetEye, 4f * dt);
        }

        bool HasHeadroom()
        {
            float radius = _controller.radius * 0.95f;
            Vector3 origin = transform.position + Vector3.up * (_controller.height - radius);
            float distance = standingHeight - _controller.height + 0.02f;
            return !Physics.SphereCast(origin, radius, Vector3.up, out _, distance, ~0, QueryTriggerInteraction.Ignore);
        }

        void UpdateMovement(float dt)
        {
            Vector2 input = Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
            bool grounded = _controller.isGrounded;
            bool movingForward = input.y > 0.1f;

            IsSprinting = _sprint.IsPressed() && movingForward && !IsCrouching && !_exhausted && _stamina > 0f;
            UpdateStamina(dt);

            float speed = IsCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;
            Vector3 target = (transform.right * input.x + transform.forward * input.y) * speed;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, target, acceleration * dt);

            if (grounded)
            {
                // Um empurrão constante para baixo mantém o controlador colado ao chão.
                _verticalVelocity = -2f;
                if (_jump.WasPressedThisFrame() && !IsCrouching)
                    _verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            }
            else
            {
                _verticalVelocity -= gravity * dt;
            }

            CollisionFlags flags = _controller.Move((_horizontalVelocity + Vector3.up * _verticalVelocity) * dt);

            // Bateu a cabeça no teto: para de subir.
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;

            UpdateSteps(dt, grounded);
        }

        void UpdateStamina(float dt)
        {
            if (IsSprinting)
            {
                _stamina = Mathf.Max(0f, _stamina - dt);
                _timeSinceSprint = 0f;
                if (_stamina <= 0f) _exhausted = true;
                return;
            }

            _timeSinceSprint += dt;
            if (_timeSinceSprint < staminaRegenDelay) return;

            float rate = staminaSeconds / Mathf.Max(0.01f, staminaRegenSeconds);
            _stamina = Mathf.Min(staminaSeconds, _stamina + rate * dt);

            // Depois de esgotar, só volta a correr com 30% do fôlego.
            if (_exhausted && _stamina >= staminaSeconds * 0.3f) _exhausted = false;
        }

        void UpdateSteps(float dt, bool grounded)
        {
            float planarSpeed = new Vector2(_controller.velocity.x, _controller.velocity.z).magnitude;

            if (!grounded || planarSpeed < 0.3f)
            {
                // Parado: volta suavemente a fase para o ponto neutro mais próximo.
                _bobPhase = Mathf.MoveTowards(_bobPhase, Mathf.Round(_bobPhase), dt * 2f);
                return;
            }

            float before = _bobPhase;
            _bobPhase += (stepBaseRate + stepRatePerSpeed * planarSpeed) * dt;

            if (Mathf.FloorToInt(_bobPhase) != Mathf.FloorToInt(before))
            {
                float loudness = IsCrouching ? 0.25f : IsSprinting ? 1f : 0.55f;
                Stepped?.Invoke(loudness);
            }

            if (_bobPhase > 1000f) _bobPhase -= 1000f;
        }

        void UpdateCamera(float dt)
        {
            if (cameraPivot == null) return;

            float bob = headBob ? -Mathf.Abs(Mathf.Sin(_bobPhase * Mathf.PI)) * headBobAmplitude : 0f;
            if (IsSprinting) bob *= 1.4f;

            cameraPivot.localPosition = new Vector3(0f, _eyeHeight + bob, 0f);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // Rede de segurança: quem cair para fora do mapa volta ao último ponto firme.
        void UpdateSafety(float dt)
        {
            if (_controller.isGrounded)
            {
                _safeTimer += dt;
                if (_safeTimer >= 0.5f)
                {
                    _safeTimer = 0f;
                    _lastSafePosition = transform.position;
                }
            }

            if (transform.position.y > -10f) return;

            _controller.enabled = false;
            transform.position = _lastSafePosition + Vector3.up * 0.1f;
            _controller.enabled = true;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }
    }
}
