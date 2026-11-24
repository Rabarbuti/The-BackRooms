using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace BackRooms.World
{
    /// <summary>
    /// Pilhas da lanterna no chão (<c>P0.3</c> de mecanicas.md): a lanterna gasta bateria
    /// (<see cref="Flashlight"/>, 4 min acesa) e as pilhas, poucas, ficam pelo mapa para quem chegar
    /// primeiro. Mirar e apertar E pega; a lanterna de quem pegou volta a ficar cheia.
    ///
    /// - Postas pela seed sem mudar o mapa: um <c>System.Random</c> próprio, depois de tudo, numa
    ///   célula longe do início, encostadas numa parede.
    /// - Rede: o cliente pede ao host (<c>GameSession.SendPickup</c>); o host confere que ainda está
    ///   lá, marca como pega e manda a todos quais sumiram e quem pegou (e a quem entra depois, junto
    ///   com a seed). A carga vai para a lanterna de quem pegou, na máquina dele.
    /// </summary>
    public sealed class BatteryPickups : MonoBehaviour, IAimSource
    {
        public static BatteryPickups Instance { get; private set; }

        /// <summary>O que a tecla E faz agora, para o painel. Nulo se não mira nenhuma pilha.</summary>
        public static string Prompt { get; private set; }

        /// <summary>Para os testes: aperta E na pilha mirada.</summary>
        public static bool PressForTest;

        /// <summary>Pilhas que sumiram, recebidas antes de a fase ser montada.</summary>
        public static ulong PendingTaken;

        public float reach = 2.2f;

        public int Count => _items.Count;
        public ulong Taken => _taken;
        /// <summary>Quantas pilhas o jogador desta máquina pegou nesta fase.</summary>
        public int LocalPicked { get; private set; }

        public Vector3 PickupPosition(int i) => _items[i].position;

        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

        readonly List<Transform> _items = new List<Transform>();
        ulong _taken;
        InputAction _use;
        AudioClip _click;
        float _sentAt = -10f;

        void Awake()
        {
            Instance = this;
            _use = new InputAction("Pilha", InputActionType.Button, "<Keyboard>/e");
            _use.AddBinding("<Gamepad>/buttonWest");
        }

        void OnEnable()
        {
            _use?.Enable();
            if (Application.isPlaying) InteractAim.Register(this);
        }

        void OnDisable()
        {
            _use?.Disable();
            InteractAim.Unregister(this);
            Prompt = null;
        }

        void OnDestroy()
        {
            _use?.Dispose();
            if (Instance == this) { Instance = null; Prompt = null; }
        }

        const float OfficeCellsPerBattery = 33f;

        /// <summary>Espalha as pilhas: uma a cada ~45 células com piso (no Escritório, ~33), de 4 a 9.</summary>
        public void Place(LevelBuilder level, Material template)
        {
            if (Application.isPlaying) Instance = this;
            LevelData data = level.Data;
            var cells = new List<Vector2Int>();
            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++)
                {
                    if (data.InRoom(x, z) || data.IsBarricade(x, z) || data.Pit[x, z]) continue;
                    if (data.HasPools && (data.IsSolid(x, z) || data.Depth[x, z] != PoolDepth.Dry)) continue;
                    if (data.HasOffice && data.Office[x, z] != OfficeCell.Corridor && data.Office[x, z] != OfficeCell.Classroom && data.Office[x, z] != OfficeCell.Small) continue;
                    if (Mathf.Abs(x - data.SpawnX) + Mathf.Abs(z - data.SpawnZ) < 3) continue;
                    cells.Add(new Vector2Int(x, z));
                }

            // No Escritório, uma a cada ~33 células (arredondado): no andar de 14 x 12 dava as mesmas
            // 4 de sempre; no de 16 x 14 (23/11/2026) dá 6, e a densidade de pilhas fica a mesma (com
            // a conta das outras fases ficariam 4 num andar 40% maior).
            int count = data.HasOffice
                ? Mathf.Clamp(Mathf.RoundToInt(cells.Count / OfficeCellsPerBattery), 4, 9)
                : Mathf.Clamp(cells.Count / 45, 4, 9);
            var rng = new System.Random(level.seed ^ 0x50494C48); // "PILH"
            Physics.SyncTransforms();
            Material box = Tinted(template, new Color(0.08f, 0.09f, 0.1f));
            Material label = Tinted(template, new Color(0.9f, 0.62f, 0.06f));
            var used = new List<Vector2Int>();
            int tries = 0;
            while (_items.Count < count && cells.Count > 0 && tries++ < 400)
            {
                int k = rng.Next(cells.Count);
                Vector2Int c = cells[k];
                cells.RemoveAt(k);
                bool apart = true;
                foreach (Vector2Int u in used) if (Mathf.Abs(u.x - c.x) + Mathf.Abs(u.y - c.y) < 4) apart = false;
                if (!apart) continue;
                if (!FindSpot(level, data, c, rng, out Vector3 spot, out float yaw)) continue;
                used.Add(c);
                _items.Add(Mount(_items.Count, spot, yaw, box, label));
            }

            BuildClip();
            if (PendingTaken != 0) Apply(PendingTaken, ulong.MaxValue);
        }

        // Junto de uma parede da célula, no chão livre (nada em volta: móvel, coluna, degrau).
        static bool FindSpot(LevelBuilder level, LevelData data, Vector2Int c, System.Random rng, out Vector3 spot, out float yaw)
        {
            int first = rng.Next(4);
            for (int i = 0; i < 4; i++)
            {
                WallSide side = Sides[(first + i) % 4];
                if (!data.HasWall(c.x, c.y, side)) continue;
                Vector3 dir = side == WallSide.South ? Vector3.back : side == WallSide.North ? Vector3.forward : side == WallSide.West ? Vector3.left : Vector3.right;
                dir = level.transform.TransformDirection(dir);
                Vector3 center = level.CellCenter(c.x, c.y);
                float along = (float)(rng.NextDouble() * 1.6 - 0.8);
                Vector3 p = center + dir * (LevelBuilder.Cell * 0.5f - 0.55f) + Vector3.Cross(Vector3.up, dir) * along;
                // Chão logo abaixo, plano, e nada ocupando o lugar.
                if (!Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit floor, 0.8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (floor.normal.y < 0.95f || Mathf.Abs(floor.point.y - center.y) > 0.05f) continue;
                if (Physics.CheckBox(floor.point + Vector3.up * 0.25f, new Vector3(0.3f, 0.2f, 0.3f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                spot = floor.point;
                yaw = (float)(rng.NextDouble() * 360.0);
                return true;
            }
            spot = Vector3.zero;
            yaw = 0f;
            return false;
        }

        Transform Mount(int index, Vector3 spot, float yaw, Material box, Material label)
        {
            // Um cartela de pilhas: a caixinha preta e a faixa laranja, deitada no chão.
            var root = new GameObject("Pilhas_" + index);
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));
            Part(root.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.12f, 0.04f, 0.17f), box);
            Part(root.transform, new Vector3(0f, 0.0405f, 0.03f), new Vector3(0.1f, 0.002f, 0.06f), label);
            return root.transform;
        }

        static void Part(Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = "Cartela";
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Material Tinted(Material template, Color color)
        {
            Material material = template != null ? new Material(template) : new Material(Shader.Find("HDRP/Lit"));
            material.name = "Pilhas";
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", null);
            if (material.HasProperty("_NormalMap")) material.SetTexture("_NormalMap", null);
            if (material.HasProperty("_MaskMap")) material.SetTexture("_MaskMap", null);
            return material;
        }

        void Update()
        {
            Prompt = null;
            PlayerNetwork local = PlayerNetwork.Local;
            bool pressed = (_use != null && _use.WasPressedThisFrame() && !BackRooms.UI.InputGate.Blocked) || PressForTest;
            PressForTest = false;
            if (local == null || !local.InGame || local.controller.IsReturning || local.controller.InFurniture) return;

            int aimed = InteractAim.Winner(this);
            if (aimed < 0) return;
            Prompt = "E - BATTERIES";
            if (!pressed || Time.time - _sentAt < 0.5f) return;
            _sentAt = Time.time;

            GameSession session = GameSession.Instance;
            if (session != null && !session.IsHosting) session.SendPickup(aimed);
            else HostPick(aimed, session != null ? NetworkManager.ServerClientId : 0UL, local.transform.position);
        }

        // A pilha mais perto do centro da mira (mira comum: InteractAim).
        public bool AimCandidate(in InteractAim.View view, out int index, out float score)
        {
            index = -1;
            score = float.PositiveInfinity;
            PlayerController body = view.Local != null ? view.Local.controller : null;
            if (body == null || body.IsReturning || body.InFurniture) return false;
            for (int i = 0; i < _items.Count; i++)
            {
                if ((_taken & (1UL << i)) != 0 || _items[i] == null || !_items[i].gameObject.activeSelf) continue;
                float sc = InteractAim.Score(view, _items[i], _items[i].position + Vector3.up * 0.03f, reach);
                if (sc >= score) continue;
                score = sc;
                index = i;
            }
            return index >= 0;
        }

        /// <summary>Host: alguém (<paramref name="who"/>, em <paramref name="at"/>) pediu a pilha.</summary>
        public void HostPick(int index, ulong who, Vector3 at)
        {
            if (index < 0 || index >= _items.Count || (_taken & (1UL << index)) != 0) return;
            if ((at - _items[index].position).magnitude > reach + 2.5f) return;
            ulong taken = _taken | (1UL << index);
            Apply(taken, who);
            GameSession.Instance?.BroadcastPickups(taken, who);
        }

        /// <summary>Em todas as máquinas: estas pilhas sumiram; a última foi pega por <paramref name="who"/>.</summary>
        public void Apply(ulong taken, ulong who)
        {
            ulong fresh = taken & ~_taken;
            _taken = taken;
            for (int i = 0; i < _items.Count; i++)
            {
                bool gone = (taken & (1UL << i)) != 0;
                if (_items[i].gameObject.activeSelf == gone)
                {
                    if (gone && (fresh & (1UL << i)) != 0 && _click != null)
                        AudioSource.PlayClipAtPoint(_click, _items[i].position, 0.7f);
                    _items[i].gameObject.SetActive(!gone);
                }
            }

            // A carga vai para quem pegou, na máquina dele.
            PlayerNetwork local = PlayerNetwork.Local;
            if (fresh == 0 || local == null || who == ulong.MaxValue) return;
            ulong localId = local.OwnerClientId;
            if (who != localId) return;
            Flashlight torch = local.flashlight;
            if (torch != null) torch.Recharge(torch.batterySeconds);
            LocalPicked++;
        }

        void BuildClip()
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int n = (int)(rate * 0.18f);
            var data = new float[n];
            var rng = new System.Random(77);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                data[i] = ((float)(rng.NextDouble() * 2.0 - 1.0) * 0.6f + Mathf.Sin(2f * Mathf.PI * 2200f * t) * 0.3f) * Mathf.Exp(-t * 35f);
            }
            _click = AudioClip.Create("Pilhas_Pegou", n, 1, rate, false);
            _click.SetData(data, 0);
        }
    }
}
