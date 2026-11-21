using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using BackRooms.Player;
using UnityEngine.InputSystem;

namespace BackRooms.Items
{
    /// <summary>
    /// Caneta: o jogador risca paredes e chão para marcar por onde passou ou o que evitar.
    /// Uma tecla por caneta: 1 preta, 2 azul, 3 vermelha. Sem caneta na mão, a tecla puxa
    /// aquela; com outra cor na mão, troca; com a mesma, guarda. Botão esquerdo do mouse risca.
    ///
    /// A ordem das cores (<see cref="ColorNames"/>, <c>DrawingBoard.inkMaterials</c> e o byte
    /// que vai na rede) continua preto, vermelho, azul; as teclas só apontam para o índice
    /// (<see cref="KeyColors"/>).
    ///
    /// Rede: quem risca vê o traço na hora. A cada décimo de segundo os pontos novos vão
    /// para o host, que os repassa aos demais. Quem entra depois recebe tudo pelo
    /// <see cref="DrawingBoard.SendAll"/>.
    /// </summary>
    public sealed class PenTool : NetworkBehaviour
    {
        public static readonly string[] ColorNames = { "BLACK", "RED", "BLUE" };

        /// <summary>Índice da cor de cada tecla: 1 preta (0), 2 azul (2), 3 vermelha (1).</summary>
        public static readonly int[] KeyColors = { 0, 2, 1 };

        [Header("Referências")]
        [Tooltip("De onde sai a mira: a câmera do jogador.")]
        public Transform aim;
        [Tooltip("O que aparece na tela de quem está usando: a mão segurando a caneta. Só o dono vê.")]
        public GameObject viewModel;
        [Tooltip("Corpo da caneta, quando ela não vem de uma ItemHand.")]
        public Renderer viewModelRenderer;
        [Tooltip("A mão em primeira pessoa. A cor da tinta vai para a caneta que ela segura.")]
        public ItemHand hand;

        [Header("Uso")]
        [Tooltip("Distância máxima para riscar, em metros.")]
        public float reach = 2.2f;
        [Tooltip("Distância mínima entre dois pontos do traço, em metros.")]
        public float pointSpacing = 0.012f;
        [Tooltip("Um salto maior que isso começa um traço novo em vez de ligar os pontos.")]
        public float maxJump = 0.25f;

        public bool Equipped { get; private set; }
        public int ColorIndex { get; private set; }

        /// <summary>Verdadeiro enquanto a ponta está de fato encostada e riscando.</summary>
        public bool IsDrawing => _active;

        /// <summary>Nome da cor atual, para o painel da filmadora.</summary>
        public string ColorName => ColorNames[ColorIndex % ColorNames.Length];

        const float FlushInterval = 0.1f;

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        InputAction _draw;
        readonly InputAction[] _keys = new InputAction[3];
        readonly List<Vector3> _pending = new List<Vector3>();
        readonly Vector3[] _single = new Vector3[1];
        bool _active;
        int _strokeIndex;
        int _pointsInStroke;
        Vector3 _normal;
        Vector3 _last;
        float _nextFlush;

        void Awake()
        {
            // Uma tecla por caneta, na ordem de KeyColors. No controle: cima preta, esquerda
            // azul, direita vermelha.
            string[] keyboard = { "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/3" };
            string[] gamepad = { "<Gamepad>/dpadUp", "<Gamepad>/dpadLeft", "<Gamepad>/dpadRight" };
            for (int i = 0; i < _keys.Length; i++)
            {
                _keys[i] = new InputAction("Pen" + ColorNames[KeyColors[i]], InputActionType.Button, keyboard[i]);
                _keys[i].AddBinding(gamepad[i]);
            }

            _draw = new InputAction("Draw", InputActionType.Button, "<Mouse>/leftButton");
            _draw.AddBinding("<Gamepad>/rightTrigger");

            if (viewModel != null) viewModel.SetActive(false);
        }

        void OnEnable() { foreach (InputAction key in _keys) key.Enable(); _draw.Enable(); }
        void OnDisable() { foreach (InputAction key in _keys) key.Disable(); _draw.Disable(); }

        public override void OnDestroy()
        {
            foreach (InputAction key in _keys) key.Dispose();
            _draw.Dispose();
            base.OnDestroy();
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            for (int i = 0; i < _keys.Length; i++)
            {
                if (!_keys[i].WasPressedThisFrame() || BackRooms.UI.PauseMenu.IsOpen) continue;
                Select(KeyColors[i]);
                break;
            }

            if (!Equipped)
            {
                EndStroke();
                return;
            }

            DrawingBoard board = DrawingBoard.Instance;
            if (board == null) return;

            // Com o mouse solto (Esc), o clique serve para voltar ao jogo, não para riscar.
            bool drawing = _draw.IsPressed() && PlayerController.MouseCaptured && aim != null;

            if (drawing && TryAim(out RaycastHit hit))
                AddSample(board, hit.point, hit.normal);
            else
                EndStroke();

            if (_pending.Count > 0 && Time.unscaledTime >= _nextFlush) Flush();
        }

        /// <summary>
        /// O que a tecla de uma caneta faz (<paramref name="color"/> é o índice da tinta): sem
        /// caneta na mão, puxa essa; com outra cor na mão, troca; com a mesma, guarda.
        /// </summary>
        public void Select(int color)
        {            if (Equipped && ColorIndex == color)
            {
                SetEquipped(false);
                return;
            }

            // Cor antes de pôr na mão: a mão já aparece com a tinta certa no primeiro quadro.
            EndStroke();
            ColorIndex = color;

            if (!Equipped) SetEquipped(true);
            else if (DrawingBoard.Instance != null) ApplyViewModelColor(DrawingBoard.Instance);
        }

        /// <summary>
        /// Onde a caneta encosta: a primeira superfície parada na frente da câmera, dentro do
        /// alcance. Devolve falso se não há nada ou se o que está na frente é outro jogador.
        ///
        /// O raio precisa ignorar o próprio jogador. O colisor do CharacterController só
        /// acompanha o corpo no passo de física seguinte; pulando, correndo ou recuando, a
        /// câmera sai de dentro dele por um instante e um raio comum o acertava — o traço
        /// aparecia solto no ar, onde o jogador estava um instante antes.
        /// </summary>
        public bool TryAim(out RaycastHit hit)
        {
            hit = default;
            if (aim == null) return false;

            int count = Physics.RaycastNonAlloc(aim.position, aim.forward, Hits, reach, ~0, QueryTriggerInteraction.Ignore);
            Transform self = transform.root;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.transform.root == self) continue;
                if (found && Hits[i].distance >= hit.distance) continue;

                hit = Hits[i];
                found = true;
            }

            // Só se risca em coisa parada: outro jogador (ou qualquer corpo móvel) na frente
            // interrompe o traço em vez de receber tinta.
            // Nas Piscinas, a tinta não pega debaixo d'água.
            if (found && BackRooms.World.PoolWater.Current != null && hit.point.y < BackRooms.World.PoolWater.Current.SurfaceY) return false;

            return found && !(hit.collider is CharacterController) && hit.collider.attachedRigidbody == null;
        }

        void SetEquipped(bool equipped)
        {
            Equipped = equipped;
            if (!equipped) EndStroke();

            if (viewModel != null) viewModel.SetActive(equipped);
            if (equipped && hand != null) hand.Build();
            if (equipped && DrawingBoard.Instance != null) ApplyViewModelColor(DrawingBoard.Instance);
        }

        void ApplyViewModelColor(DrawingBoard board)
        {
            Renderer body = hand != null && hand.PenRenderer != null ? hand.PenRenderer : viewModelRenderer;
            if (body == null || board.inkMaterials == null || board.inkMaterials.Length == 0) return;
            body.sharedMaterial = board.inkMaterials[Mathf.Min(ColorIndex, board.inkMaterials.Length - 1)];
        }

        void AddSample(DrawingBoard board, Vector3 point, Vector3 normal)
        {
            bool sameSurface = _active && Vector3.Dot(normal, _normal) > 0.98f;
            bool near = (point - _last).sqrMagnitude <= maxJump * maxJump;

            if (!sameSurface || !near || _pointsInStroke >= DrawingBoard.MaxPointsPerStroke)
            {
                // Outra superfície, um salto ou traço cheio: fecha o atual e abre outro.
                EndStroke();
                _active = true;
                _strokeIndex++;
                _pointsInStroke = 0;
                _normal = normal;
            }
            else if ((point - _last).sqrMagnitude < pointSpacing * pointSpacing)
            {
                return;
            }

            _last = point;
            _pointsInStroke++;
            _pending.Add(point);

            // Quem risca vê na hora, sem esperar a rede.
            _single[0] = point;
            board.Append(DrawingBoard.Key(OwnerClientId, _strokeIndex), (byte)ColorIndex, _normal, _single);
        }

        void EndStroke()
        {
            if (!_active) return;
            Flush();
            _active = false;
        }

        void Flush()
        {
            _nextFlush = Time.unscaledTime + FlushInterval;
            if (_pending.Count == 0) return;

            SubmitRpc(_strokeIndex, (byte)ColorIndex, _normal, _pending.ToArray());
            _pending.Clear();
        }

        // Do dono para o host.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SubmitRpc(int strokeIndex, byte color, Vector3 normal, Vector3[] points)
        {
            if (points == null || points.Length == 0 || points.Length > DrawingBoard.MaxPointsPerStroke) return;

            // Se o host é quem riscou, o traço já está no quadro dele.
            if (!IsOwner && DrawingBoard.Instance != null)
                DrawingBoard.Instance.Append(DrawingBoard.Key(OwnerClientId, strokeIndex), color, normal, points);

            BroadcastRpc(strokeIndex, color, normal, points);
        }

        // Do host para os outros jogadores.
        [Rpc(SendTo.NotServer)]
        void BroadcastRpc(int strokeIndex, byte color, Vector3 normal, Vector3[] points)
        {
            if (IsOwner || DrawingBoard.Instance == null) return;
            DrawingBoard.Instance.Append(DrawingBoard.Key(OwnerClientId, strokeIndex), color, normal, points);
        }
    }
}
