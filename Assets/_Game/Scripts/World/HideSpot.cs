using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BackRooms.World
{
    /// <summary>
    /// Esconderijo num móvel do Level 1 (level1-mostruario.md, seções 3.1 e 5.4): o guarda-roupa
    /// e a mesa de jantar. Quem mira nele de perto e aperta E entra (debaixo da mesa, só
    /// agachado): o corpo vai para dentro do móvel e fica parado, olhando em volta, até apertar E
    /// de novo e sair pela frente. No guarda-roupa ninguém vê nem pega; debaixo da mesa, o
    /// inimigo vê quem estiver no cone de visão dele (<c>EntityBrain.HiddenFrom</c>).
    ///
    /// A <see cref="PropLayer"/> põe este componente no móvel. A entrada (tecla E, mira com o
    /// padrão de <c>PenTool.TryAim</c>) e o balanço das portas ficam em <see cref="HideSpotInput"/>,
    /// um só por fase. Nada vai pela rede além do esconderijo de cada jogador
    /// (<c>PlayerNetwork.Hiding</c>): as portas de cada máquina abrem quando alguém entra ou sai.
    /// </summary>
    public sealed class HideSpot : MonoBehaviour
    {
        /// <summary>Todos os esconderijos de móvel da fase.</summary>
        public static readonly List<HideSpot> All = new List<HideSpot>();

        public HideKind kind = HideKind.Wardrobe;
        [Tooltip("Onde o corpo fica, em coordenadas do móvel (pés no chão).")]
        public Vector3 inside;
        [Tooltip("Para onde sai, em coordenadas do móvel.")]
        public Vector3 exit;
        [Tooltip("Altura do olho lá dentro, em metros.")]
        public float eye = 1.55f;
        [Tooltip("Portas que abrem e fecham quando alguém entra ou sai (origem na dobradiça).")]
        public Transform[] doors;

        Quaternion[] _closed;
        float _swing;

        public Vector3 InsideWorld => transform.TransformPoint(inside);
        public Vector3 ExitWorld => transform.TransformPoint(exit);

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>Abre as portas um instante (alguém entrou ou saiu).</summary>
        public void Swing() => _swing = 1f;

        void Update()
        {
            if (_swing <= 0f || doors == null || doors.Length == 0) return;

            // A rotação fechada é lida no primeiro balanço: no Play o componente nasce (Awake)
            // antes de a PropLayer dizer quais são as portas.
            if (_closed == null || _closed.Length != doors.Length)
            {
                _closed = new Quaternion[doors.Length];
                for (int i = 0; i < doors.Length; i++) _closed[i] = doors[i] != null ? doors[i].localRotation : Quaternion.identity;
            }

            _swing = Mathf.Max(0f, _swing - Time.deltaTime / 0.9f);
            // Abre depressa e fecha devagar: sobe até 75° no primeiro terço e volta.
            float open = _swing > 0.7f ? Mathf.InverseLerp(1f, 0.7f, _swing) : Mathf.InverseLerp(0f, 0.7f, _swing);
            for (int i = 0; i < doors.Length; i++)
            {
                if (doors[i] == null) continue;
                // A frente do móvel é +Z: a porta da dobradiça da direita gira para +Y, a da esquerda para -Y.
                float side = Mathf.Sign(doors[i].localPosition.x);
                doors[i].localRotation = _closed[i] * Quaternion.Euler(0f, side * 75f * open, 0f);
            }
        }

        /// <summary>Alguém já está escondido aqui?</summary>
        public bool Occupied(PlayerNetwork except)
        {
            Vector3 at = InsideWorld;
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || player == except || !player.IsSpawned || !player.InGame) continue;
                if (player.Hiding == HideKind.None) continue;
                Vector3 d = player.transform.position - at;
                d.y = 0f;
                if (d.magnitude < 0.5f) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// A tecla E dos esconderijos de móvel, para o jogador desta máquina, e as portas que abrem
    /// quando qualquer jogador entra ou sai. Um só por fase (a <see cref="PropLayer"/> cria).
    /// </summary>
    public sealed class HideSpotInput : MonoBehaviour
    {
        [Tooltip("Até onde alcança, em metros.")]
        public float reach = 1.9f;

        /// <summary>O que a tecla E faz agora ("E - HIDE", "E - LEAVE"), para o painel. Nulo se nada.</summary>
        public static string Prompt { get; private set; }

        /// <summary>Para os testes: manda entrar/sair como se tivesse apertado E.</summary>
        public static bool PressForTest;

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        readonly Dictionary<PlayerNetwork, HideKind> _seen = new Dictionary<PlayerNetwork, HideKind>();
        InputAction _use;

        void Awake()
        {
            _use = new InputAction("Esconder", InputActionType.Button, "<Keyboard>/e");
            _use.AddBinding("<Gamepad>/buttonWest");
        }

        void OnEnable() => _use.Enable();
        void OnDisable() { _use.Disable(); Prompt = null; }
        void OnDestroy() => _use.Dispose();

        void Update()
        {
            SwingDoors();

            PlayerNetwork local = PlayerNetwork.Local;
            PlayerController body = local != null ? local.controller : null;
            Prompt = null;
            if (body == null || !local.InGame || body.IsReturning) return;

            bool pressed = (_use.WasPressedThisFrame() && !BackRooms.UI.InputGate.Blocked) || PressForTest;
            PressForTest = false;

            if (body.InFurniture)
            {
                Prompt = "E - LEAVE";
                if (pressed) body.LeaveFurniture();
                return;
            }

            HideSpot spot = Aimed(local);
            if (spot == null) return;

            if (spot.kind == HideKind.Table && !body.IsCrouching)
            {
                Prompt = "CROUCH TO HIDE";
                return;
            }
            if (spot.Occupied(local))
            {
                Prompt = "OCCUPIED";
                return;
            }

            Prompt = "E - HIDE";
            if (!pressed) return;

            float yaw = spot.transform.eulerAngles.y;
            body.EnterFurniture(spot.kind, spot.InsideWorld, yaw, spot.eye, spot.ExitWorld);
        }

        /// <summary>O esconderijo que o jogador mira de perto, ou nulo.</summary>
        public HideSpot Aimed(PlayerNetwork local)
        {
            Camera camera = local.playerCamera;
            if (camera == null) return null;

            Transform eye = camera.transform;
            int count = Physics.RaycastNonAlloc(eye.position, eye.forward, Hits, reach, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(Hits, 0, count, HitDistance);
            Transform self = local.transform.root;

            // Do mais perto ao mais longe: o próprio jogador não conta (o colisor dele acompanha o
            // corpo um passo atrasado); móvel que não é esconderijo (a cadeira diante da mesa) a
            // mira atravessa; parede e o resto da fase param a mira.
            for (int i = 0; i < count; i++)
            {
                Collider hit = Hits[i].collider;
                if (hit.transform.root == self) continue;
                HideSpot spot = hit.GetComponentInParent<HideSpot>();
                if (spot != null) return spot;
                if (!hit.transform.IsChildOf(transform)) return null;
            }
            return null;
        }

        static readonly IComparer<RaycastHit> HitDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        // Quando alguém entra ou sai de um móvel, as portas do móvel mais perto balançam.
        void SwingDoors()
        {
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned) continue;
                HideKind now = player.Hiding;
                _seen.TryGetValue(player, out HideKind before);
                _seen[player] = now;

                bool furniture(HideKind k) => k == HideKind.Wardrobe || k == HideKind.Table;
                if (furniture(now) == furniture(before)) continue;

                HideSpot closest = null;
                float best = 2.5f;
                foreach (HideSpot spot in HideSpot.All)
                {
                    float d = Vector3.Distance(spot.InsideWorld, player.transform.position);
                    if (d < best) { best = d; closest = spot; }
                }
                if (closest != null) closest.Swing();
            }
        }
    }
}
