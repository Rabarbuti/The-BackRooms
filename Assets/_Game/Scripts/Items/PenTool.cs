using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using BackRooms.Player;
using UnityEngine.InputSystem;

namespace BackRooms.Items
{
    /// <summary>
    /// Caneta: o jogador risca paredes e chão para marcar por onde passou ou o que evitar.
    /// Tecla 1 pega e guarda; botão esquerdo do mouse risca; R ou 3 passa para a cor seguinte
    /// e 2 volta para a anterior (preto, vermelho, azul).
    ///
    /// Rede: quem risca vê o traço na hora. A cada décimo de segundo os pontos novos vão
    /// para o host, que os repassa aos demais. Quem entra depois recebe tudo pelo
    /// <see cref="DrawingBoard.SendAll"/>.
    /// </summary>
    public sealed class PenTool : NetworkBehaviour
    {
        public static readonly string[] ColorNames = { "BLACK", "RED", "BLUE" };

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

        InputAction _equip, _draw, _nextColor, _previousColor;
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
            _equip = new InputAction("Pen", InputActionType.Button, "<Keyboard>/1");
            _equip.AddBinding("<Gamepad>/dpadUp");

            _draw = new InputAction("Draw", InputActionType.Button, "<Mouse>/leftButton");
            _draw.AddBinding("<Gamepad>/rightTrigger");

            _nextColor = new InputAction("PenColor", InputActionType.Button, "<Keyboard>/r");
            _nextColor.AddBinding("<Keyboard>/3");
            _nextColor.AddBinding("<Gamepad>/dpadRight");

            _previousColor = new InputAction("PenColorBack", InputActionType.Button, "<Keyboard>/2");
            _previousColor.AddBinding("<Gamepad>/dpadLeft");

            if (viewModel != null) viewModel.SetActive(false);
        }

        void OnEnable() { _equip.Enable(); _draw.Enable(); _nextColor.Enable(); _previousColor.Enable(); }
        void OnDisable() { _equip.Disable(); _draw.Disable(); _nextColor.Disable(); _previousColor.Disable(); }

        public override void OnDestroy()
        {
            _equip.Dispose(); _draw.Dispose(); _nextColor.Dispose(); _previousColor.Dispose();
            base.OnDestroy();
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            if (_equip.WasPressedThisFrame()) SetEquipped(!Equipped);

            if (!Equipped)
            {
                EndStroke();
                return;
            }

            DrawingBoard board = DrawingBoard.Instance;
            if (board == null) return;

            int step = (_nextColor.WasPressedThisFrame() ? 1 : 0) - (_previousColor.WasPressedThisFrame() ? 1 : 0);
            if (step != 0) ChangeColor(board, step);

            // Com o mouse solto (Esc), o clique serve para voltar ao jogo, não para riscar.
            bool drawing = _draw.IsPressed() && PlayerController.MouseCaptured && aim != null;

            if (drawing && TryAim(out RaycastHit hit))
                AddSample(board, hit.point, hit.normal);
            else
                EndStroke();

            if (_pending.Count > 0 && Time.unscaledTime >= _nextFlush) Flush();
        }

        /// <summary>Passa <paramref name="step"/> cores para a frente (negativo volta), em ciclo.</summary>
        public void ChangeColor(DrawingBoard board, int step)
        {
            EndStroke();
            int count = Mathf.Max(1, board.ColorCount);
            ColorIndex = ((ColorIndex + step) % count + count) % count;
            ApplyViewModelColor(board);
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
