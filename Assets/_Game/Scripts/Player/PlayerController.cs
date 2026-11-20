using System;
using BackRooms.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BackRooms.Player
{
    /// <summary>Onde o jogador está escondido. Vai pela rede (PlayerNetwork) e o inimigo lê.</summary>
    public enum HideKind : byte
    {
        /// <summary>À vista.</summary>
        None,
        /// <summary>No nicho de uma parede grossa (Level 0): ninguém vê nem pega.</summary>
        Niche,
        /// <summary>Dentro de um guarda-roupa (Level 1): ninguém vê nem pega.</summary>
        Wardrobe,
        /// <summary>Debaixo de uma mesa (Level 1): some só para quem não o tem no cone de visão.</summary>
        Table
    }

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

        [Header("Queda em buraco")]
        [Tooltip("Tempo entre passar do ponto sem volta e reaparecer na sala inicial, em segundos.")]
        public float pitRespawnDelay = 0.8f;

        [Header("Captura pela entidade")]
        [Tooltip("Tempo entre ser pego e reaparecer na sala inicial, em segundos.")]
        public float captureRespawnDelay = 1.6f;

        [Header("Fôlego (segundos)")]
        public float staminaSeconds = 6f;
        public float staminaRegenDelay = 2f;
        public float staminaRegenSeconds = 4f;

        [Header("Água (Piscinas)")]
        [Tooltip("Segundos de fôlego com a cabeça debaixo d'água.")]
        public float airSeconds = 20f;
        [Tooltip("Segundos para recuperar o fôlego inteiro fora d'água.")]
        public float airRefillSeconds = 3f;
        [Tooltip("Velocidade (fração da caminhada) com água na canela e com água no peito (nos degraus que entram na água funda).")]
        public float wadeShallow = 0.85f;
        public float wadeDeep = 0.55f;
        [Tooltip("Velocidade nadando, fração da caminhada.")]
        public float swimSpeed = 0.45f;
        [Tooltip("Com mais água que isto acima dos pés, o jogador nada em vez de andar (m).")]
        public float swimDepth = 1.3f;
        [Tooltip("Segurando Espaço, os pés ficam esta distância abaixo da superfície, com a cabeça fora d'água (m).")]
        public float floatDepth = 1.4f;
        [Tooltip("Subindo com Espaço ou mergulhando com Ctrl/C (m/s).")]
        public float swimVerticalSpeed = 1.6f;
        [Tooltip("Sem tecla, onde não dá pé o jogador afunda a esta velocidade (m/s).")]
        public float sinkSpeed = 0.5f;
        [Tooltip("Quanto a velocidade vertical na água muda por segundo (m/s²): a inércia do nado. Uma queda rápida a água freia três vezes mais forte.")]
        public float swimAcceleration = 3f;

        [Header("Subir na borda nadando")]
        [Tooltip("Nadando à tona, sobe numa borda cujo topo fica até esta altura acima da superfície (m). Andando, borda não se sobe.")]
        public float climbMaxAboveSurface = 0.45f;
        [Tooltip("Até onde, além do corpo, a borda pode estar para o jogador subir empurrando contra ela (m).")]
        public float climbReach = 0.3f;
        [Tooltip("Duração da subida: sai da água e avança até ficar de pé no piso de cima (s).")]
        public float climbSeconds = 0.55f;

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

        /// <summary>Quantas vezes este jogador caiu num buraco nesta sessão.</summary>
        public int Falls { get; private set; }

        /// <summary>Verdadeiro entre cair num buraco e reaparecer.</summary>
        public bool IsFalling { get; private set; }

        /// <summary>Quantas vezes a entidade pegou este jogador nesta sessão.</summary>
        public int Captures { get; private set; }

        /// <summary>Verdadeiro entre ser pego pela entidade e reaparecer.</summary>
        public bool IsCaught { get; private set; }

        /// <summary>Caindo ou pego: sem controle, a caminho da sala inicial.</summary>
        public bool IsReturning => IsFalling || IsCaught;

        /// <summary>
        /// Onde reaparece quem cai num buraco. Quem preenche é o PlayerNetwork, que conhece
        /// o mapa; sem isso o jogador volta ao último ponto firme.
        /// </summary>
        public Func<Vector3> RespawnPoint;

        /// <summary>Disparado logo depois de o jogador ser recolocado por causa de uma queda.</summary>
        public event Action Respawned;

        /// <summary>
        /// Chamado pelo gatilho do fundo do buraco. O jogador perde o controle, continua
        /// caindo e, passado <see cref="pitRespawnDelay"/>, volta para a sala inicial.
        /// </summary>
        public void FallIntoPit()
        {
            if (IsReturning || !enabled) return;

            IsFalling = true;
            _fallTimer = pitRespawnDelay;
            Falls++;
        }

        /// <summary>
        /// Chamado quando a entidade pega este jogador. Ele trava onde está e, passado
        /// <see cref="captureRespawnDelay"/>, reaparece na sala inicial. Não há morte.
        /// </summary>
        public void GetCaught()
        {
            if (IsReturning || !enabled) return;

            IsCaught = true;
            _furniture = HideKind.None;
            _fallTimer = captureRespawnDelay;
            _horizontalVelocity = Vector3.zero;
            Captures++;
        }

        /// <summary>Água acima dos pés, em metros (0 fora d'água ou em nível sem água).</summary>
        public float Submersion { get; private set; }

        /// <summary>Verdadeiro nadando: sem chão ao alcance dos pés.</summary>
        public bool IsSwimming { get; private set; }

        /// <summary>Verdadeiro com a câmera (a cabeça) debaixo d'água.</summary>
        public bool IsUnderwater { get; private set; }

        /// <summary>Há quanto tempo a cabeça está debaixo d'água sem voltar à tona (0 fora d'água).</summary>
        public float UnderwaterSeconds { get; private set; }

        /// <summary>Fôlego restante debaixo d'água, de 0 a 1.</summary>
        public float Air01 => airSeconds <= 0f ? 1f : _air / airSeconds;

        /// <summary>Enche o fôlego (ao trocar <see cref="airSeconds"/>, por exemplo).</summary>
        public void RefillAir() => _air = airSeconds;

        /// <summary>Quantas vezes este jogador se afogou nesta sessão (também contam em <see cref="Falls"/>).</summary>
        public int Drownings { get; private set; }

        /// <summary>
        /// Corrente que arrasta quem está nadando, em m/s (o ralo das Piscinas na vazante,
        /// <c>PoolTide</c>). Zero fora d'água.
        /// </summary>
        public Vector3 WaterCurrent { get; set; }

        /// <summary>Afogado sem ser pelo fôlego (puxado até a grade do ralo): volta ao início.</summary>
        public void Drown()
        {
            if (IsReturning || !enabled) return;
            Drownings++;
            FallIntoPit();
        }

        /// <summary>Verdadeiro durante a subida numa borda, saindo da água (uns 0,5 s).</summary>
        public bool IsClimbing { get; private set; }

        /// <summary>Quantas vezes este jogador subiu numa borda nadando nesta sessão.</summary>
        public int Climbs { get; private set; }

        /// <summary>Nadando à tona, de frente para uma borda em que dá para subir (pista do painel).</summary>
        public bool CanClimb { get; private set; }

        /// <summary>Verdadeiro enquanto o jogador está dentro de um esconderijo.</summary>
        public bool IsHidden => Hiding != HideKind.None;

        /// <summary>Que esconderijo: o móvel em que entrou ou o nicho em que está.</summary>
        public HideKind Hiding => _furniture != HideKind.None ? _furniture : _hidingSpots > 0 ? HideKind.Niche : HideKind.None;

        /// <summary>Dentro de um móvel (guarda-roupa, debaixo da mesa): parado, só olha em volta.</summary>
        public bool InFurniture => _furniture != HideKind.None;

        int _hidingSpots;
        HideKind _furniture;
        Vector3 _furnitureExit;
        float _furnitureEye;

        /// <summary>
        /// Entra num móvel: o corpo vai para <paramref name="inside"/>, virado para
        /// <paramref name="yaw"/>, com o olho a <paramref name="eye"/> do chão, e fica parado até
        /// <see cref="LeaveFurniture"/>, que o devolve a <paramref name="exit"/>.
        /// </summary>
        public void EnterFurniture(HideKind kind, Vector3 inside, float yaw, float eye, Vector3 exit)
        {
            if (IsReturning || !enabled || kind == HideKind.None || kind == HideKind.Niche) return;

            _furniture = kind;
            _furnitureExit = exit;
            _furnitureEye = eye;
            Teleport(inside, yaw);
            _pitch = 0f;
            _eyeHeight = eye;
        }

        /// <summary>Sai do móvel pela frente dele.</summary>
        public void LeaveFurniture()
        {
            if (_furniture == HideKind.None) return;

            _furniture = HideKind.None;
            Teleport(_furnitureExit, transform.eulerAngles.y);
            IsCrouching = false;
            _eyeHeight = standingEyeHeight;
        }

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
        float _fallTimer;
        bool _lockedCursor;
        float _air;
        Vector3 _climbFrom, _climbTo;
        float _climbTime, _climbRetryAt;
        static readonly RaycastHit[] RayHits = new RaycastHit[16];
        static readonly Collider[] Overlaps = new Collider[16];

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            // Com o valor padrão (1 mm), movimentos muito pequenos em taxas de quadro altas
            // são descartados e o controlador deixa de se reportar como apoiado no chão.
            _controller.minMoveDistance = 0f;
            _stamina = staminaSeconds;
            _air = airSeconds;
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

            // Quem some debaixo d'água não pode deixar a tela verde para trás.
            if (PoolWater.Current != null) PoolWater.Current.LocalUnderwater = false;
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

        /// <summary>Para os testes: inclina a câmera (positivo = olhando para baixo).</summary>
        public void SetPitch(float pitch) => _pitch = Mathf.Clamp(pitch, -85f, 85f);

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
            IsClimbing = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            UpdateCursor();
            UpdateLook(dt);

            // Dentro de um móvel: parado, sem gravidade nem colisão (o corpo está dentro da caixa
            // do móvel), só olhando em volta.
            if (InFurniture)
            {
                _eyeHeight = _furnitureEye;
                _horizontalVelocity = Vector3.zero;
                _verticalVelocity = 0f;
                IsSprinting = false;
                UpdateCamera(dt);
                UpdateBreath(dt);
                return;
            }

            UpdateCrouch(dt);
            UpdateMovement(dt);
            UpdateFall(dt);
            UpdateCamera(dt);
            UpdateBreath(dt);
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
            Vector2 look = _lookMouse.ReadValue<Vector2>() * (mouseSensitivity * GameOptions.MouseScale)
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
            // Caindo num buraco ou nas mãos da entidade não há o que comandar.
            Vector2 input = IsReturning ? Vector2.zero : Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
            bool grounded = _controller.isGrounded;
            bool movingForward = input.y > 0.1f;

            // Água (só nas Piscinas): no raso e nos degraus, quanto mais funda, mais lento; na água
            // funda (piscinas, raias, canais: 2,05 m) e no poço não dá pé, e o jogador nada.
            PoolWater water = PoolWater.Current;
            Submersion = water != null ? Mathf.Max(0f, water.SurfaceY - transform.position.y) : 0f;
            IsSwimming = water != null && Submersion > swimDepth;
            CanClimb = false;

            // Subindo numa borda: o movimento é só a subida, até ficar de pé no piso de cima.
            if (IsClimbing)
            {
                IsSprinting = false;
                UpdateStamina(dt);
                UpdateClimb(dt);
                UpdateSteps(dt, false);
                return;
            }

            IsSprinting = _sprint.IsPressed() && movingForward && !IsCrouching && !_exhausted && _stamina > 0f && Submersion < 0.8f;
            UpdateStamina(dt);

            float speed = IsCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;
            if (IsSwimming) speed = walkSpeed * swimSpeed;
            else if (Submersion > 0f) speed *= Mathf.Lerp(wadeShallow, wadeDeep, Mathf.InverseLerp(0.4f, 1.2f, Submersion));

            Vector3 target = (transform.right * input.x + transform.forward * input.y) * speed;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, target, acceleration * dt);

            if (IsSwimming)
            {
                // Onde não dá pé: sem tecla, afunda devagar (na funda, até ficar de pé no fundo,
                // com a cabeça debaixo d'água, e dá para andar por ele até uma escada); segurando
                // Espaço, sobe e fica com a cabeça fora d'água (os pés a floatDepth da superfície);
                // agachar mergulha mais rápido. Quem está voltando ao início (afogado) só afunda. A
                // velocidade muda aos poucos (inércia); uma queda rápida a água freia mais forte.
                float floatY = water.SurfaceY - floatDepth;
                float vertical = -sinkSpeed;
                if (!IsReturning && _crouch.IsPressed()) vertical = -swimVerticalSpeed;
                else if (!IsReturning && _jump.IsPressed()) vertical = Mathf.Clamp((floatY - transform.position.y) * 3f, -swimVerticalSpeed, swimVerticalSpeed);
                // Quem chega andando (descendo a escada para a água funda) traz o empurrão de -2 m/s
                // que o prende ao chão: não é queda, então não vira mergulho ao sair do degrau.
                if (grounded && _verticalVelocity < -sinkSpeed) _verticalVelocity = -sinkSpeed;
                float rate = Mathf.Abs(_verticalVelocity) > swimVerticalSpeed ? swimAcceleration * 3f : swimAcceleration;
                _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, vertical, rate * dt);

                // À tona, empurrando contra uma borda baixa (ou de frente para ela, encostado, com
                // Espaço): sobe nela. Andando, borda não se sobe; do fundo, primeiro sobe à tona.
                if (TryStartClimb(water, input, floatY))
                {
                    UpdateSteps(dt, false);
                    return;
                }
            }
            else if (grounded)
            {
                // Um empurrão constante para baixo mantém o controlador colado ao chão.
                _verticalVelocity = -2f;
                if (_jump.WasPressedThisFrame() && !IsCrouching && !IsReturning)
                    _verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            }
            else
            {
                _verticalVelocity -= gravity * dt;
            }

            Vector3 current = IsSwimming ? WaterCurrent : Vector3.zero;
            CollisionFlags flags = _controller.Move((_horizontalVelocity + Vector3.up * _verticalVelocity + current) * dt);

            // Bateu a cabeça no teto: para de subir.
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;

            UpdateSteps(dt, grounded);
        }

        // ---------------------------------------------------------------- Subir na borda nadando

        bool TryStartClimb(PoolWater water, Vector2 input, float floatY)
        {
            // Só à tona: a cabeça fora d'água e os pés perto de onde boia; não mergulhando nem voltando.
            if (IsReturning || IsUnderwater || _crouch.IsPressed() || transform.position.y < floatY - 0.25f) return false;
            // Uma subida barrada no caminho só se tenta de novo depois de um instante.
            if (Time.time < _climbRetryAt) return false;

            Vector3 look = transform.forward;
            look.y = 0f;
            look.Normalize();
            Vector3 push = transform.right * input.x + transform.forward * input.y;
            push.y = 0f;
            bool pushing = push.magnitude > 0.3f;
            Vector3 direction = pushing ? push.normalized : look;

            if (!FindLedge(water, direction, out Vector3 landing, out float gap, out Vector3 inward)) return false;
            CanClimb = true;

            // Empurrando contra a borda (o direcional) ou, só com Espaço, encostado nela e de frente.
            bool start = pushing || _jump.IsPressed() && gap <= 0.1f && Vector3.Dot(look, inward) >= 0.7f;
            if (!start) return false;

            IsClimbing = true;
            Climbs++;
            _climbFrom = transform.position;
            _climbTo = landing;
            _climbTime = 0f;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            return true;
        }

        /// <summary>
        /// Procura, na direção dada, uma borda em que quem nada à tona pode subir: uma face logo à
        /// frente (até <see cref="climbReach"/> além do corpo), com topo plano entre o alcance do
        /// degrau e <see cref="climbMaxAboveSurface"/> acima da água, sem parede em cima dela e com
        /// lugar para ficar de pé (a cápsula do CharacterController) no topo, acima de onde está e
        /// no meio do caminho. Os raios descartam o próprio jogador (ver PenTool.TryAim).
        /// </summary>
        bool FindLedge(PoolWater water, Vector3 direction, out Vector3 landing, out float gap, out Vector3 inward)
        {
            landing = inward = Vector3.zero;
            gap = 0f;
            Vector3 feet = transform.position;
            float radius = _controller.radius, step = _controller.stepOffset;

            // A face: um pouco acima do que o degrau já sobe sozinho.
            if (!CastIgnoringSelf(feet + Vector3.up * (step + 0.03f), direction, radius + climbReach, out RaycastHit face)) return false;
            inward = -face.normal;
            inward.y = 0f;
            if (inward.sqrMagnitude < 0.5f) return false;
            inward.Normalize();
            if (Vector3.Dot(inward, direction) < 0.6f) return false;
            gap = face.distance - radius;

            // O topo: plano, logo além da face, nem alto demais (parede) nem ao alcance do degrau.
            float maxTop = water.SurfaceY + climbMaxAboveSurface;
            Vector3 probe = face.point + inward * 0.3f;
            probe.y = maxTop + 0.4f;
            if (!CastIgnoringSelf(probe, Vector3.down, probe.y - (feet.y + step), out RaycastHit top) || top.normal.y < 0.7f) return false;
            float topY = top.point.y;
            if (topY > maxTop || topY < feet.y + step) return false;

            // Nada logo acima da borda (o topo de uma parede baixa com parede por cima não conta).
            if (CastIgnoringSelf(new Vector3(feet.x, topY + 0.15f, feet.z), inward, face.distance + radius + 0.2f, out _)) return false;

            landing = face.point + inward * (radius + 0.12f);
            landing.y = topY + 0.02f;
            Vector3 raised = new Vector3(feet.x, topY + 0.04f, feet.z);
            return CapsuleFree(landing) && CapsuleFree(raised) && CapsuleFree((raised + landing) * 0.5f);
        }

        // Sai da água (sobe rente à face) e avança até ficar de pé no piso de cima, pelo
        // CharacterController: se algo barrar a subida, desiste e volta a nadar.
        void UpdateClimb(float dt)
        {
            if (IsReturning)
            {
                IsClimbing = false;
                return;
            }

            _climbTime += dt;
            float t = _climbTime / Mathf.Max(0.1f, climbSeconds);
            float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.6f));
            float forward = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.4f) / 0.6f));
            Vector3 desired = new Vector3(Mathf.Lerp(_climbFrom.x, _climbTo.x, forward), Mathf.Lerp(_climbFrom.y, _climbTo.y, up),
                                          Mathf.Lerp(_climbFrom.z, _climbTo.z, forward));
            _controller.Move(desired - transform.position);

            Vector3 now = transform.position;
            bool blocked = desired.y - now.y > 0.3f;
            Vector2 left = new Vector2(_climbTo.x - now.x, _climbTo.z - now.z);
            if (blocked || t >= 1f && (left.magnitude < 0.15f || _climbTime > climbSeconds + 0.25f))
            {
                IsClimbing = false;
                _horizontalVelocity = Vector3.zero;
                _verticalVelocity = blocked ? 0f : -2f;
                if (blocked) _climbRetryAt = Time.time + 0.5f;
            }
        }

        bool CastIgnoringSelf(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.RaycastNonAlloc(origin, direction, RayHits, distance, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Transform self = transform.root;
            for (int i = 0; i < count; i++)
            {
                if (RayHits[i].collider.transform.root == self || RayHits[i].distance >= best) continue;
                best = RayHits[i].distance;
                nearest = RayHits[i];
            }
            return best < float.MaxValue;
        }

        // Cabe o jogador de pé com os pés em <paramref name="feet"/>? (Descarta o próprio colisor.)
        bool CapsuleFree(Vector3 feet)
        {
            float radius = _controller.radius * 0.95f;
            Vector3 bottom = feet + Vector3.up * (radius + 0.03f), top = feet + Vector3.up * (standingHeight - radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, Overlaps, ~0, QueryTriggerInteraction.Ignore);
            Transform self = transform.root;
            for (int i = 0; i < count; i++)
                if (Overlaps[i].transform.root != self) return false;
            return true;
        }

        void UpdateFall(float dt)
        {
            if (!IsReturning) return;

            _fallTimer -= dt;
            if (_fallTimer > 0f) return;

            IsFalling = false;
            IsCaught = false;
            _pitch = 0f;
            Teleport(RespawnPoint != null ? RespawnPoint() : _lastSafePosition, transform.eulerAngles.y);
            Respawned?.Invoke();
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

        // Fôlego: só gasta com a cabeça debaixo d'água; acabou, o jogador se afoga — a mesma
        // volta à sala inicial da queda no buraco (tela preta, NO SIGNAL).
        void UpdateBreath(float dt)
        {
            PoolWater water = PoolWater.Current;
            if (water == null)
            {
                IsUnderwater = false;
                UnderwaterSeconds = 0f;
                _air = airSeconds;
                return;
            }

            float eye = cameraPivot != null ? cameraPivot.position.y : transform.position.y + _eyeHeight;
            IsUnderwater = eye < water.SurfaceY - 0.05f;
            UnderwaterSeconds = IsUnderwater ? UnderwaterSeconds + dt : 0f;
            water.LocalUnderwater = IsUnderwater && enabled;

            if (IsReturning) return;

            if (IsUnderwater)
            {
                _air -= dt;
                if (_air > 0f) return;

                _air = airSeconds;
                Drownings++;
                FallIntoPit();
                return;
            }

            _air = Mathf.Min(airSeconds, _air + dt * airSeconds / Mathf.Max(0.1f, airRefillSeconds));
        }

        // Rede de segurança: quem cair para fora do mapa volta ao último ponto firme.
        void UpdateSafety(float dt)
        {
            // O fundo de um buraco é chão, mas não é lugar para onde voltar.
            if (_controller.isGrounded && !IsReturning)
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
