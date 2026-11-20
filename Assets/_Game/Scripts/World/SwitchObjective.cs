using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.UI;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace BackRooms.World
{
    /// <summary>Tipo dos pontos do objetivo comum: muda a aparência, o som, os tempos e os textos.</summary>
    public enum SwitchKind : byte
    {
        None,
        /// <summary>Level 0: caixas de disjuntor que zumbem de longe.</summary>
        Breaker,
        /// <summary>Level 2: válvulas das casas de máquinas; cumprido, drena as Piscinas.</summary>
        Valve,
        /// <summary>Level 3: acionadores manuais do alarme de incêndio; cumprido, destrava a porta corta-fogo.</summary>
        FireAlarm
    }

    /// <summary>
    /// O objetivo comum das fases (<c>PC.1</c> de mecanicas.md): <b>N pontos distantes armados dentro
    /// de uma janela de tempo</b>. Disjuntores no Level 0, válvulas no Level 2 e acionadores do alarme
    /// de incêndio no Level 3; muda só o <see cref="SwitchKind"/>.
    ///
    /// - <b>Postos pela seed sem mudar o mapa</b>: um <c>System.Random</c> próprio escolhe, depois de
    ///   tudo, paredes livres longe do início e longe umas das outras (distância andando pela grade),
    ///   para uma pessoa sozinha não armar dois pontos dentro da janela correndo entre eles.
    /// - <b>Armar</b>: mirar (o padrão de <c>PenTool.TryAim</c>: o raio descarta o próprio jogador) e
    ///   segurar E por <see cref="HoldSeconds"/>. Enquanto segura, o jogador faz barulho de corrida
    ///   (<see cref="LocalWorking"/> → <c>PlayerNetwork.Working</c> → o inimigo ouve).
    /// - <b>A janela</b>: o host guarda a hora em que cada ponto foi armado
    ///   (<c>GameSession.SessionSeconds</c>); cada um vale por <see cref="WindowSeconds"/>. O objetivo
    ///   se cumpre quando há ao mesmo tempo <see cref="Required"/> pontos valendo (2 com até dois
    ///   jogadores, 3 com três ou quatro). Sozinho, a janela some: ponto armado fica armado.
    /// - <b>Rede</b>: o cliente avisa o host ao terminar de segurar (<c>GameSession.SendSwitchArm</c>);
    ///   o host confere a distância, arma e manda o estado inteiro a todos, e a quem entra depois junto
    ///   com a seed (<see cref="Pending"/> guarda o estado que chega antes de a fase ser montada).
    /// - A porta de saída fica trancada até o objetivo se cumprir (<see cref="Locked"/>, lido pela
    ///   <see cref="LevelExit"/>); a filmadora mostra <see cref="Prompt"/>, <see cref="LockedText"/> e
    ///   <see cref="Footer"/>.
    /// </summary>
    public sealed class SwitchObjective : MonoBehaviour
    {
        /// <summary>O objetivo da fase aberta; nulo onde a fase não tem (Level 1 usa o <see cref="ExitLock"/>).</summary>
        public static SwitchObjective Instance { get; private set; }

        /// <summary>Hora de armado de um ponto que não expira (jogando sozinho).</summary>
        public const double Permanent = 1e12;

        /// <summary>O que a tecla E faz agora num ponto, para o painel. Nulo se o jogador não mira nenhum.</summary>
        public static string Prompt { get; private set; }

        /// <summary>O jogador desta máquina está segurando E num ponto (faz barulho).</summary>
        public static bool LocalWorking { get; private set; }

        /// <summary>Para os testes: segura E (vale enquanto estiver ligado).</summary>
        public static bool HoldForTest;

        /// <summary>Para os testes: quantos jogadores o host conta ao armar (0 = os de verdade).</summary>
        public static int PlayersForTest;

        /// <summary>Estado recebido do host antes de esta máquina montar a fase (ou entre montagens).</summary>
        public static State Pending;

        /// <summary>A porta de saída está trancada por este objetivo.</summary>
        public static bool Locked => Instance != null && Instance.Count > 0 && !Instance.Complete;

        public struct State
        {
            public bool Complete;
            public double CompletedAt;
            public int Required;
            public double[] ArmedAt;
        }

        public SwitchKind Kind { get; private set; }
        public int Count => _points.Count;
        public bool Complete { get; private set; }
        /// <summary>Hora da sessão em que o objetivo se cumpriu (-1 se não).</summary>
        public double CompletedAt { get; private set; } = -1.0;
        /// <summary>Quantos pontos precisam valer ao mesmo tempo.</summary>
        public int Required { get; private set; } = 2;
        public float HoldSeconds { get; private set; } = 3f;
        public float WindowSeconds { get; private set; } = 10f;
        /// <summary>Altura do centro da peça acima do piso da célula, em metros.</summary>
        public float MountHeight { get; private set; } = 1.35f;

        /// <summary>Quantas vezes o host armou um ponto (testes).</summary>
        public int Arms { get; private set; }

        public float reach = 2.2f;

        /// <summary>Posição (no mundo) da frente de cada ponto, para os testes.</summary>
        public Vector3 PointPosition(int i) => _points[i].Root.position;
        /// <summary>Para onde a frente do ponto aponta (para fora da parede).</summary>
        public Vector3 PointFacing(int i) => _points[i].Root.forward;
        /// <summary>Célula de cada ponto.</summary>
        public Vector2Int PointCell(int i) => _points[i].Cell;
        /// <summary>Hora da sessão em que o ponto foi armado (-1 se não; <see cref="Permanent"/> se não expira).</summary>
        public double PointArmedAt(int i) => _points[i].ArmedAt;

        static readonly RaycastHit[] Hits = new RaycastHit[16];
        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };
        static readonly int UnlitColorId = Shader.PropertyToID("_UnlitColor");

        sealed class Point
        {
            public Transform Root;
            public Transform Lever;
            public Renderer Lamp;
            public AudioSource Hum;
            public AudioSource Voice;
            public Vector2Int Cell;
            public double ArmedAt = -1.0;
            public float SentAt = -10f;
            public bool WasActive;
        }

        readonly List<Point> _points = new List<Point>();
        InputAction _use;
        MaterialPropertyBlock _block;
        AudioClip _humClip, _clickClip, _doneClip, _alarmClip;
        int _held = -1;
        float _hold;
        float _nextClick;
        bool _completeShown;

        void Awake()
        {
            Instance = this;
            _use = new InputAction("Objetivo", InputActionType.Button, "<Keyboard>/e");
            _use.AddBinding("<Gamepad>/buttonWest");
        }

        void OnEnable() => _use?.Enable();

        void OnDisable()
        {
            _use?.Disable();
            Prompt = null;
            LocalWorking = false;
        }

        void OnDestroy()
        {
            _use?.Dispose();
            if (Instance == this)
            {
                Instance = null;
                Prompt = null;
                LocalWorking = false;
            }
        }

        // ---------------------------------------------------------------- Montagem

        /// <summary>
        /// Escolhe as paredes e monta os pontos. Usa só a planta (<see cref="LevelBuilder.Data"/>) e
        /// um <c>System.Random</c> próprio, depois de tudo: o mapa das seeds que já existem não muda.
        /// </summary>
        public void Place(LevelBuilder level, SwitchKind kind, Material bodyTemplate, Material lampTemplate)
        {
            Kind = kind;
            if (Application.isPlaying) Instance = this;
            LevelData data = level.Data;
            int count, spacing, fromStart;
            float height;
            switch (kind)
            {
                case SwitchKind.Valve:
                    count = 2; spacing = 8; fromStart = 4; height = 1.1f;
                    HoldSeconds = 6f; WindowSeconds = 5f;
                    break;
                case SwitchKind.FireAlarm:
                    count = 3; spacing = 6; fromStart = 4; height = 1.35f;
                    HoldSeconds = 2f; WindowSeconds = 8f;
                    break;
                default:
                    count = 4; spacing = 9; fromStart = 5; height = 1.35f;
                    HoldSeconds = 3f; WindowSeconds = 10f;
                    break;
            }
            MountHeight = height;

            Physics.SyncTransforms();
            int[,] start = Distances(data, data.SpawnX, data.SpawnZ);

            // Candidatas em ordem fixa (z, x, lado): célula boa, parede lisa e livre na frente.
            var candidates = new List<(int x, int z, Vector3 point, Vector3 normal)>();
            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++)
                {
                    if (start[x, z] < fromStart || !GoodCell(data, x, z)) continue;
                    for (int s = 0; s < 4; s++)
                        if (FindFace(level, data, x, z, Sides[s], height, out Vector3 point, out Vector3 normal))
                        {
                            candidates.Add((x, z, point, normal));
                            break;
                        }
                }

            var rng = new System.Random(level.seed ^ 0x53574954); // "SWIT"
            var chosen = new List<(int x, int z, Vector3 point, Vector3 normal)>();
            var reach = new List<int[,]>();
            for (int gap = spacing; gap >= 2 && chosen.Count < count; gap--)
            {
                chosen.Clear();
                reach.Clear();
                var pool = new List<int>();
                for (int i = 0; i < candidates.Count; i++) pool.Add(i);
                while (chosen.Count < count && pool.Count > 0)
                {
                    int k = rng.Next(pool.Count);
                    var c = candidates[pool[k]];
                    pool.RemoveAt(k);
                    bool apart = true;
                    foreach (int[,] other in reach)
                        if (other[c.x, c.z] >= 0 && other[c.x, c.z] < gap) apart = false;
                    if (!apart) continue;
                    chosen.Add(c);
                    reach.Add(Distances(data, c.x, c.z));
                }
            }

            BuildClips();
            for (int i = 0; i < chosen.Count; i++)
                _points.Add(Mount(i, chosen[i].x, chosen[i].z, chosen[i].point, chosen[i].normal, bodyTemplate, lampTemplate));

            if (_points.Count < count)
                Debug.LogWarning($"[BackRooms] Objetivo ({kind}): só {_points.Count} de {count} pontos couberam na seed {level.seed}.");

            Required = Mathf.Min(2, _points.Count);
            if (Pending.ArmedAt != null) Apply(Pending);
            PinNote(level, bodyTemplate);
        }

        /// <summary>O bilhete de ensino da sala inicial (Etapa 6 do plano), ou nulo se não coube.</summary>
        public Transform Note { get; private set; }

        // As linhas do bilhete: o que fazer nesta fase, em poucas palavras (até 21 letras por linha).
        string[] NoteLines()
        {
            switch (Kind)
            {
                case SwitchKind.Valve:
                    return new[] { "PARA DRENAR:", "ABRA 2 VALVULAS", "AO MESMO TEMPO", "CUIDADO COM OS RALOS", "MARQUE ONDE DA PE" };
                case SwitchKind.FireAlarm:
                    return new[] { "PUXE 2 ALARMES", "AO MESMO TEMPO", "ABRIGO: SALINHA", "COM A LUZ ACESA", "AGACHADO ELA NAO OUVE" };
                default:
                    return new[] { "SEM ENERGIA:", "LIGUE 2 DISJUNTORES", "AO MESMO TEMPO", "SIGA O ZUMBIDO", "NAO CORRA PERTO DELA" };
            }
        }

        // Prega o bilhete na primeira parede lisa em volta do meio da sala inicial (ordem fixa: igual
        // em todas as máquinas). Sem colisor: "Placa" é enfeite para o Level0ColliderCheck.
        void PinNote(LevelBuilder level, Material template)
        {
            LevelData data = level.Data;
            // Uma parede fechada de alguma célula da sala inicial, em ordem fixa (z, x, lado).
            for (int z = data.RoomZ0; z < data.RoomZ0 + Mathf.Max(1, data.RoomHeight); z++)
            for (int x = data.RoomX0; x < data.RoomX0 + Mathf.Max(1, data.RoomWidth); x++)
            foreach (WallSide wall in Sides)
            {
                if (x < 0 || z < 0 || x >= data.Width || z >= data.Height || !data.HasWall(x, z, wall)) continue;
                Vector3 dir = level.transform.TransformDirection(SideDirection(wall));
                Vector3 center = level.CellCenter(x, z) + Vector3.up * 1.4f;
                if (!Physics.Raycast(center, dir, out RaycastHit hit, LevelBuilder.Cell * 0.5f + 0.4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Vector3.Dot(hit.normal, -dir) < 0.97f) continue;
                Vector3 side = Vector3.Cross(Vector3.up, hit.normal).normalized;
                bool flat = true;
                for (int s = -1; s <= 1; s += 2)
                    if (!Physics.Raycast(center + side * (0.3f * s), dir, out RaycastHit other, hit.distance + 0.3f, ~0, QueryTriggerInteraction.Ignore) ||
                        Mathf.Abs(other.distance - hit.distance) > 0.03f) flat = false;
                if (!flat) continue;

                string[] lines = NoteLines();
                const int w = 128, h = 64;
                var pixels = new Color32[w * h];
                var paper = new Color32(236, 228, 196, 255);
                var ink = new Color32(30, 34, 90, 255);
                for (int i = 0; i < pixels.Length; i++) pixels[i] = paper;
                for (int i = 0; i < lines.Length; i++)
                    PixelFont.Draw(pixels, w, h, (w - PixelFont.Measure(lines[i], 1)) / 2, 5 + i * 11, lines[i], 1, ink);
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "BilheteEnsino" };
                texture.SetPixels32(pixels);
                texture.Apply(false);

                var root = new GameObject("Bilhete");
                root.transform.SetParent(transform, false);
                root.transform.SetPositionAndRotation(hit.point + hit.normal * 0.012f, Quaternion.LookRotation(-hit.normal, Vector3.up));
                var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
                card.name = "Placa";
                Collider c = card.GetComponent<Collider>();
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
                card.transform.SetParent(root.transform, false);
                card.transform.localScale = new Vector3(0.48f, 0.24f, 1f);
                Material material = Tinted(template, Color.white, 0.2f);
                material.name = "BilheteEnsino";
                material.SetTexture("_BaseColorMap", texture);
                var renderer = card.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                Note = root.transform;
                return;
            }
        }

        /// <summary>Um número que muda se qualquer ponto mudar de lugar (para comparar máquinas).</summary>
        public int Hash()
        {
            unchecked
            {
                int h = (int)Kind * 7919;
                foreach (Point p in _points) h = h * 31 + p.Cell.x * 131 + p.Cell.y * 7 + Mathf.RoundToInt(p.Root.eulerAngles.y);
                return h;
            }
        }

        static bool GoodCell(LevelData data, int x, int z)
        {
            if (data.InRoom(x, z) || data.IsBarricade(x, z) || (data.Pit != null && data.Pit[x, z])) return false;
            if (x == data.ExitX && z == data.ExitZ) return false;
            if (data.HasPools)
                return !data.IsSolid(x, z) && data.Depth[x, z] == PoolDepth.Dry;
            if (data.HasOffice)
                return data.Office[x, z] == OfficeCell.Corridor;
            return true;
        }

        // Uma parede lisa daquele lado: o raio do meio da célula bate de frente nela perto da linha
        // da grade, e dois raios 35 cm para os lados também (a peça cabe); nada na frente dela.
        static bool FindFace(LevelBuilder level, LevelData data, int x, int z, WallSide side, float height, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            if (!data.HasWall(x, z, side)) return false;
            if (data.HasPools && data.EdgeAt(x, z, side) != PoolEdge.Wall) return false;
            if (data.HasOffice)
            {
                OfficeEdge edge = data.OfficeEdgeAt(x, z, side);
                if (edge != OfficeEdge.Wall) return false;
                if (data.LockersSouth != null && HasLockers(data, x, z, side)) return false;
            }
            if (!data.HasPools && !data.HasOffice && (data.IsAlcove(x, z, side) || data.IsRoomWall(x, z, side))) return false;
            foreach (Decoration d in data.Decorations)
                if (d.Kind == DecorationKind.Arrow && d.X == x && d.Z == z && d.Side == side) return false;

            Vector3 dir = SideDirection(side);
            Vector3 dirWorld = level.transform.TransformDirection(dir);
            Vector3 origin = level.CellCenter(x, z) + Vector3.up * height;
            Vector3 lateral = Vector3.Cross(Vector3.up, dirWorld).normalized;

            if (!Physics.Raycast(origin, dirWorld, out RaycastHit hit, LevelBuilder.Cell * 0.5f + 0.3f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.distance < LevelBuilder.Cell * 0.5f - 0.45f || Vector3.Dot(hit.normal, -dirWorld) < 0.97f) return false;
            for (int s = -1; s <= 1; s += 2)
            {
                if (!Physics.Raycast(origin + lateral * (0.35f * s), dirWorld, out RaycastHit side2, LevelBuilder.Cell * 0.5f + 0.3f, ~0, QueryTriggerInteraction.Ignore)) return false;
                if (Mathf.Abs(side2.distance - hit.distance) > 0.03f || Vector3.Dot(side2.normal, hit.normal) < 0.99f) return false;
            }
            // De cima a baixo da peça, também lisa (nada encostado nem cortando a parede).
            for (int s = -1; s <= 1; s += 2)
            {
                if (!Physics.Raycast(origin + Vector3.up * (0.4f * s), dirWorld, out RaycastHit vertical, LevelBuilder.Cell * 0.5f + 0.3f, ~0, QueryTriggerInteraction.Ignore)) return false;
                if (Mathf.Abs(vertical.distance - hit.distance) > 0.03f) return false;
            }

            normal = hit.normal;
            point = hit.point;
            Vector3 front = point + normal * 0.45f;
            Quaternion turn = Quaternion.LookRotation(normal, Vector3.up);
            if (Physics.CheckBox(front, new Vector3(0.45f, 0.5f, 0.35f), turn, ~0, QueryTriggerInteraction.Ignore)) return false;
            return true;
        }

        static bool HasLockers(LevelData data, int x, int z, WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return data.LockersSouth[x, z] != 0;
                case WallSide.North: return data.LockersSouth[x, z + 1] != 0;
                case WallSide.West: return data.LockersWest[x, z] != 0;
                default: return data.LockersWest[x + 1, z] != 0;
            }
        }

        static Vector3 SideDirection(WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return Vector3.back;
                case WallSide.North: return Vector3.forward;
                case WallSide.West: return Vector3.left;
                default: return Vector3.right;
            }
        }

        static bool Step(LevelData data, int x, int z, WallSide side)
        {
            if (data.HasPools) return data.CanStep(x, z, side);
            if (!data.CanWalk(x, z, side, data.PitStrips != null)) return false;
            data.Neighbor(x, z, side, out int nx, out int nz);
            return !data.IsBarricade(nx, nz);
        }

        /// <summary>Passos (em células) andando de (fx, fz) até cada célula; -1 onde não se chega.</summary>
        static int[,] Distances(LevelData data, int fx, int fz)
        {
            var distance = new int[data.Width, data.Height];
            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++) distance[x, z] = -1;
            var queue = new Queue<int>();
            distance[fx, fz] = 0;
            queue.Enqueue(fx + fz * data.Width);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int x = cell % data.Width, z = cell / data.Width;
                foreach (WallSide side in Sides)
                {
                    if (!Step(data, x, z, side)) continue;
                    data.Neighbor(x, z, side, out int nx, out int nz);
                    if (distance[nx, nz] >= 0) continue;
                    distance[nx, nz] = distance[x, z] + 1;
                    queue.Enqueue(nx + nz * data.Width);
                }
            }
            return distance;
        }

        // ---------------------------------------------------------------- Peças

        Point Mount(int index, int x, int z, Vector3 point, Vector3 normal, Material bodyTemplate, Material lampTemplate)
        {
            var root = new GameObject("Ponto_" + index);
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal, Vector3.up));
            var p = new Point { Root = root.transform, Cell = new Vector2Int(x, z) };

            Material grey = Tinted(bodyTemplate, new Color(0.42f, 0.44f, 0.45f), 0.55f);
            Material dark = Tinted(bodyTemplate, new Color(0.12f, 0.12f, 0.13f), 0.3f);
            Material red = Tinted(bodyTemplate, new Color(0.62f, 0.06f, 0.05f), 0.45f);
            Material white = Tinted(bodyTemplate, new Color(0.85f, 0.85f, 0.82f), 0.4f);
            Material yellow = Tinted(bodyTemplate, new Color(0.85f, 0.62f, 0.08f), 0.4f);

            switch (Kind)
            {
                case SwitchKind.Valve:
                {
                    // Cano saindo da parede, flange e a roda vermelha de manobra (sem cilindros: o
                    // colisor de caixa bate com a malha, ver Level0ColliderCheck).
                    Box(root.transform, "Cano", new Vector3(0f, 0f, 0.2f), new Vector3(0.16f, 0.16f, 0.4f), grey);
                    Box(root.transform, "CanoParede", new Vector3(0f, -0.55f, 0.1f), new Vector3(0.14f, 0.9f, 0.14f), grey);
                    Box(root.transform, "Flange", new Vector3(0f, 0f, 0.05f), new Vector3(0.3f, 0.3f, 0.06f), dark);
                    var wheel = new GameObject("Roda").transform;
                    wheel.SetParent(root.transform, false);
                    wheel.localPosition = new Vector3(0f, 0f, 0.42f);
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * 45f;
                        var rim = Box(wheel, "Aro", Quaternion.Euler(0f, 0f, a) * new Vector3(0f, 0.24f, 0f), new Vector3(0.2f, 0.04f, 0.04f), red);
                        rim.localRotation = Quaternion.Euler(0f, 0f, a);
                    }
                    Box(wheel, "Raio", Vector3.zero, new Vector3(0.46f, 0.035f, 0.03f), red);
                    Box(wheel, "Raio", Vector3.zero, new Vector3(0.035f, 0.46f, 0.03f), red);
                    p.Lever = wheel;
                    p.Lamp = Box(root.transform, "Lampada", new Vector3(0.32f, 0.3f, 0.04f), new Vector3(0.06f, 0.06f, 0.06f), null).GetComponent<Renderer>();
                    break;
                }
                case SwitchKind.FireAlarm:
                {
                    // Acionador manual: caixa vermelha, a alavanca branca e a lâmpada em cima.
                    Box(root.transform, "Caixa", new Vector3(0f, 0f, 0.05f), new Vector3(0.14f, 0.18f, 0.1f), red);
                    var lever = new GameObject("Alavanca").transform;
                    lever.SetParent(root.transform, false);
                    lever.localPosition = new Vector3(0f, 0.04f, 0.1f);
                    Box(lever, "Puxador", new Vector3(0f, -0.03f, 0.015f), new Vector3(0.08f, 0.06f, 0.03f), white);
                    p.Lever = lever;
                    p.Lamp = Box(root.transform, "Lampada", new Vector3(0f, 0.15f, 0.04f), new Vector3(0.05f, 0.05f, 0.05f), null).GetComponent<Renderer>();
                    break;
                }
                default:
                {
                    // Caixa de disjuntor cinza, faixa amarela, a alavanca e a lâmpada em cima.
                    Box(root.transform, "Caixa", new Vector3(0f, 0f, 0.07f), new Vector3(0.44f, 0.6f, 0.14f), grey);
                    Box(root.transform, "Faixa", new Vector3(0f, 0.22f, 0.142f), new Vector3(0.36f, 0.06f, 0.004f), yellow);
                    Box(root.transform, "Conduite", new Vector3(0.12f, 0.95f, 0.04f), new Vector3(0.06f, 1.3f, 0.06f), grey);
                    var lever = new GameObject("Alavanca").transform;
                    lever.SetParent(root.transform, false);
                    lever.localPosition = new Vector3(0f, -0.02f, 0.15f);
                    Box(lever, "Haste", new Vector3(0f, 0.07f, 0.02f), new Vector3(0.05f, 0.16f, 0.04f), dark);
                    Box(lever, "Cabo", new Vector3(0f, 0.15f, 0.05f), new Vector3(0.1f, 0.04f, 0.06f), red);
                    p.Lever = lever;
                    p.Lamp = Box(root.transform, "Lampada", new Vector3(0.15f, 0.22f, 0.15f), new Vector3(0.05f, 0.05f, 0.03f), null).GetComponent<Renderer>();
                    break;
                }
            }

            if (lampTemplate != null) p.Lamp.sharedMaterial = lampTemplate;
            p.Lamp.shadowCastingMode = ShadowCastingMode.Off;

            p.Hum = Source(root.transform, 2f, Kind == SwitchKind.FireAlarm ? 14f : 26f);
            p.Hum.clip = _humClip;
            p.Hum.loop = true;
            p.Hum.volume = Kind == SwitchKind.FireAlarm ? 0.25f : 0.45f;
            if (Application.isPlaying) p.Hum.timeSamples = (index * 7919) % Mathf.Max(1, _humClip.samples);
            if (Application.isPlaying) p.Hum.Play();
            p.Voice = Source(root.transform, 1.5f, 30f);

            SetLever(p, 0f);
            Paint(p, LampColor(p, 0.0, false));
            return p;
        }

        static Transform Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            // A caixa primitiva: colisor e malha do mesmo tamanho (nascem juntos).
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            var renderer = box.GetComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return box.transform;
        }

        static Material Tinted(Material template, Color color, float smoothness)
        {
            Material material = template != null ? new Material(template) : new Material(Shader.Find("HDRP/Lit"));
            material.name = "Objetivo";
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", null);
            if (material.HasProperty("_NormalMap")) material.SetTexture("_NormalMap", null);
            if (material.HasProperty("_MaskMap")) material.SetTexture("_MaskMap", null);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        static AudioSource Source(Transform parent, float min, float max)
        {
            var source = parent.gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.minDistance = min;
            source.maxDistance = max;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.dopplerLevel = 0f;
            source.playOnAwake = false;
            return source;
        }

        // ---------------------------------------------------------------- Jogo

        void Update()
        {
            double now = Now;
            UpdateLocal(now);

            // Cumprido agora (aqui ou pelo aviso do host): estalo em cada ponto e na porta.
            if (Complete && !_completeShown)
            {
                _completeShown = true;
                foreach (Point p in _points) Play(p, _doneClip, 1f);
                if (LevelExit.Instance != null) AudioSource.PlayClipAtPoint(_doneClip, LevelExit.Instance.DoorPosition + Vector3.up, 1f);
                if (Kind == SwitchKind.FireAlarm)
                    foreach (Point p in _points)
                    {
                        p.Hum.clip = _alarmClip;
                        p.Hum.maxDistance = 40f;
                        p.Hum.volume = 0.8f;
                        p.Hum.Play();
                    }
            }

            for (int i = 0; i < _points.Count; i++)
            {
                Point p = _points[i];
                bool active = IsActive(p, now);
                bool arming = _held == i && _hold > 0f;
                float lever = Complete || active ? 1f : arming ? Mathf.Clamp01(_hold / HoldSeconds) : 0f;
                if (Kind == SwitchKind.Valve && p.Lever != null)
                {
                    // A roda gira enquanto alguém a manobra e fica parada aberta depois.
                    p.Lever.localRotation = Quaternion.Euler(0f, 0f, -lever * 720f);
                }
                else SetLever(p, lever);
                Paint(p, LampColor(p, now, arming));

                if (active != p.WasActive)
                {
                    p.WasActive = active;
                    if (active && !Complete) Play(p, _clickClip, 0.9f);
                }
                if (!Complete && Kind != SwitchKind.FireAlarm) p.Hum.pitch = active ? 1.35f : 1f;
            }
        }

        void UpdateLocal(double now)
        {
            Prompt = null;
            bool working = false;
            PlayerNetwork local = PlayerNetwork.Local;
            if (local == null || !local.InGame || local.controller.IsReturning || local.controller.InFurniture)
            {
                _held = -1;
                _hold = 0f;
                LocalWorking = false;
                return;
            }

            int aimed = Aimed(local);
            bool holding = _use.IsPressed() || HoldForTest;
            if (aimed < 0)
            {
                _held = -1;
                _hold = 0f;
                LocalWorking = false;
                return;
            }

            Point p = _points[aimed];
            if (Complete)
            {
                Prompt = CompleteText;
            }
            else if (IsActive(p, now))
            {
                ActiveCount(now, out int active);
                Prompt = p.ArmedAt >= Permanent ? $"{ArmedWord}\n{active}/{Required}" : $"{ArmedWord} {Remaining(p, now)}s\n{active}/{Required}";
                _held = -1;
                _hold = 0f;
            }
            else if (Time.time - p.SentAt < 1.5f)
            {
                Prompt = ArmedWord;
            }
            else if (holding)
            {
                if (_held != aimed) { _held = aimed; _hold = 0f; }
                _hold += Time.deltaTime;
                working = true;
                if (Time.time >= _nextClick)
                {
                    _nextClick = Time.time + (Kind == SwitchKind.Valve ? 0.45f : 0.22f);
                    Play(p, _clickClip, 0.35f);
                }
                int bars = Mathf.Clamp(Mathf.FloorToInt(_hold / HoldSeconds * 5f), 0, 5);
                Prompt = WorkWord + "\n" + new string('#', bars) + new string('-', 5 - bars);
                if (_hold >= HoldSeconds)
                {
                    _hold = 0f;
                    _held = -1;
                    p.SentAt = Time.time;
                    RequestArm(aimed);
                }
            }
            else
            {
                _held = -1;
                _hold = 0f;
                Prompt = "E - HOLD\n" + PointWord;
            }
            LocalWorking = working;
        }

        int Aimed(PlayerNetwork local)
        {
            Camera camera = local.playerCamera;
            if (camera == null) return -1;
            Transform eye = camera.transform;
            int count = Physics.RaycastNonAlloc(eye.position, eye.forward, Hits, reach, ~0, QueryTriggerInteraction.Ignore);
            Transform self = local.transform.root;
            float best = float.MaxValue;
            Collider nearest = null;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.transform.root == self || Hits[i].distance >= best) continue;
                best = Hits[i].distance;
                nearest = Hits[i].collider;
            }
            if (nearest == null) return -1;
            for (Transform t = nearest.transform; t != null && t != transform; t = t.parent)
                if (t.parent == transform)
                    for (int i = 0; i < _points.Count; i++)
                        if (_points[i].Root == t) return i;
            return -1;
        }

        void RequestArm(int index)
        {
            GameSession session = GameSession.Instance;
            if (session != null && !session.IsHosting) session.SendSwitchArm(index);
            else HostArm(index, PlayerNetwork.Local != null ? PlayerNetwork.Local.transform.position : (Vector3?)null);
        }

        /// <summary>
        /// Host: arma o ponto (um cliente terminou de segurar E, ou o próprio host). Confere que
        /// quem pediu está perto; arma pela hora da sessão (ou para sempre, sozinho) e, se há pontos
        /// valendo bastante ao mesmo tempo, cumpre o objetivo. Manda o estado a todos.
        /// </summary>
        public void HostArm(int index, Vector3? who)
        {
            if (Complete || index < 0 || index >= _points.Count) return;
            Point p = _points[index];
            if (who.HasValue && (who.Value - p.Root.position).magnitude > reach + 2.5f) return;

            double now = Now;
            GameSession session = GameSession.Instance;
            int players = PlayersForTest > 0 ? PlayersForTest : session != null && session.IsHosting ? Mathf.Max(1, session.PlayerCount) : 1;
            Required = Mathf.Min(_points.Count, players >= 3 ? 3 : 2);
            p.ArmedAt = players <= 1 ? Permanent : now;
            Arms++;

            ActiveCount(now, out int active);
            if (active >= Required)
            {
                Complete = true;
                CompletedAt = now;
                Debug.Log($"[BackRooms] Objetivo ({Kind}) cumprido: {active} de {_points.Count} pontos valendo ao mesmo tempo, {players} jogador(es).");
                // O estrondo (disjuntores ligando, a sirene) chama o inimigo para o último ponto.
                BackRooms.Entities.EntityBrain.Instance?.HearNoise(p.Root.position + p.Root.forward * 1.2f);
            }
            session?.BroadcastSwitchState();
        }

        /// <summary>Host, só nos testes: desarma todos os pontos.</summary>
        public void ResetForTest()
        {
            foreach (Point p in _points) p.ArmedAt = -1.0;
            GameSession.Instance?.BroadcastSwitchState();
        }

        /// <summary>Host, só nos testes: cumpre o objetivo sem andar.</summary>
        public void CompleteForTest()
        {
            if (Complete) return;
            Complete = true;
            CompletedAt = Now;
            Required = Mathf.Min(2, _points.Count);
            GameSession.Instance?.BroadcastSwitchState();
        }

        // ---------------------------------------------------------------- Estado

        public State Snapshot()
        {
            var state = new State { Complete = Complete, CompletedAt = CompletedAt, Required = Required, ArmedAt = new double[_points.Count] };
            for (int i = 0; i < _points.Count; i++) state.ArmedAt[i] = _points[i].ArmedAt;
            return state;
        }

        /// <summary>Em todas as máquinas: o estado que o host mandou.</summary>
        public void Apply(State state)
        {
            if (state.ArmedAt == null) return;
            Required = Mathf.Max(1, state.Required);
            for (int i = 0; i < _points.Count && i < state.ArmedAt.Length; i++) _points[i].ArmedAt = state.ArmedAt[i];
            if (state.Complete && !Complete)
            {
                Complete = true;
                CompletedAt = state.CompletedAt;
            }
        }

        public static void Write(FastBufferWriter writer, State state)
        {
            writer.WriteValueSafe(state.Complete);
            writer.WriteValueSafe(state.CompletedAt);
            writer.WriteValueSafe(state.Required);
            int n = state.ArmedAt != null ? state.ArmedAt.Length : 0;
            writer.WriteValueSafe(n);
            for (int i = 0; i < n; i++) writer.WriteValueSafe(state.ArmedAt[i]);
        }

        public static int Size(State state) => sizeof(bool) + sizeof(double) + sizeof(int) * 2 + sizeof(double) * (state.ArmedAt != null ? state.ArmedAt.Length : 0);

        public static State Read(FastBufferReader reader)
        {
            var state = new State();
            reader.ReadValueSafe(out state.Complete);
            reader.ReadValueSafe(out state.CompletedAt);
            reader.ReadValueSafe(out state.Required);
            reader.ReadValueSafe(out int n);
            state.ArmedAt = new double[Mathf.Clamp(n, 0, 32)];
            for (int i = 0; i < state.ArmedAt.Length; i++) reader.ReadValueSafe(out state.ArmedAt[i]);
            return state;
        }

        // ---------------------------------------------------------------- Ajuda

        static double Now
        {
            get
            {
                GameSession session = GameSession.Instance;
                double seconds = session != null ? session.SessionSeconds : -1.0;
                return seconds >= 0.0 ? seconds : Time.timeAsDouble;
            }
        }

        bool IsActive(Point p, double now) => p.ArmedAt >= 0.0 && (p.ArmedAt >= Permanent || now - p.ArmedAt <= WindowSeconds);

        int Remaining(Point p, double now) => Mathf.Max(0, Mathf.CeilToInt((float)(WindowSeconds - (now - p.ArmedAt))));

        /// <summary>Pontos valendo agora.</summary>
        void ActiveCount(double now, out int active)
        {
            active = 0;
            foreach (Point p in _points) if (IsActive(p, now)) active++;
        }

        /// <summary>Pontos valendo agora, pelo relógio da sessão.</summary>
        public int ActiveNow
        {
            get
            {
                ActiveCount(Now, out int active);
                return active;
            }
        }

        string PointWord => Kind == SwitchKind.Valve ? "OPEN VALVE" : Kind == SwitchKind.FireAlarm ? "PULL ALARM" : "ARM BREAKER";
        string WorkWord => Kind == SwitchKind.Valve ? "TURNING" : Kind == SwitchKind.FireAlarm ? "PULLING" : "ARMING";
        string ArmedWord => Kind == SwitchKind.Valve ? "VALVE OPEN" : Kind == SwitchKind.FireAlarm ? "ALARM SET" : "BREAKER ON";
        string CompleteText => Kind == SwitchKind.Valve ? "DRAINING" : Kind == SwitchKind.FireAlarm ? "FIRE ALARM" : "POWER ON";

        /// <summary>O que a porta trancada diz, mirada.</summary>
        public string LockedText
        {
            get
            {
                int active = ActiveNow;
                switch (Kind)
                {
                    case SwitchKind.Valve: return $"FLOODED\nVALVES {active}/{Required}";
                    case SwitchKind.FireAlarm: return $"FIRE DOOR LOCKED\nALARMS {active}/{Required}";
                    default: return $"NO POWER\nBREAKERS {active}/{Required}";
                }
            }
        }

        /// <summary>Rodapé do painel enquanto algum ponto vale e o objetivo não se cumpriu. Nulo se nada.</summary>
        public string Footer
        {
            get
            {
                if (Complete || _points.Count == 0) return null;
                double now = Now;
                int active = 0, left = int.MaxValue;
                foreach (Point p in _points)
                {
                    if (!IsActive(p, now)) continue;
                    active++;
                    if (p.ArmedAt < Permanent) left = Mathf.Min(left, Remaining(p, now));
                }
                if (active == 0) return null;
                string word = Kind == SwitchKind.Valve ? "VALVES" : Kind == SwitchKind.FireAlarm ? "ALARMS" : "BREAKERS";
                return left == int.MaxValue ? $"{word} {active}/{Required}" : $"{word} {active}/{Required} 0:{left:00}";
            }
        }

        Color LampColor(Point p, double now, bool arming)
        {
            if (Complete) return new Color(0.3f, 1f, 0.4f);
            if (IsActive(p, now))
            {
                bool ending = p.ArmedAt < Permanent && WindowSeconds - (now - p.ArmedAt) < 3.0;
                bool blink = ending && Mathf.FloorToInt(Time.time * 6f) % 2 == 0;
                return blink ? new Color(0.05f, 0.2f, 0.06f) : new Color(0.2f, 1f, 0.3f);
            }
            if (arming) return new Color(1f, 0.6f, 0.1f);
            bool pulse = Kind == SwitchKind.FireAlarm && Mathf.FloorToInt(Time.time * 1.2f) % 2 == 0;
            return pulse ? new Color(0.9f, 0.05f, 0.03f) : new Color(0.38f, 0.03f, 0.02f);
        }

        void Paint(Point p, Color color)
        {
            if (p.Lamp == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            p.Lamp.GetPropertyBlock(_block);
            _block.SetColor(UnlitColorId, color);
            _block.SetColor("_BaseColor", color);
            p.Lamp.SetPropertyBlock(_block);
        }

        static void SetLever(Point p, float t)
        {
            if (p.Lever == null) return;
            // Alavanca para cima (desligado) e para baixo (ligado); o puxador do alarme desce.
            p.Lever.localRotation = Quaternion.Euler(Mathf.Lerp(-35f, 35f, t), 0f, 0f);
        }

        void Play(Point p, AudioClip clip, float volume)
        {
            if (p.Voice != null && clip != null && p.Voice.isActiveAndEnabled) p.Voice.PlayOneShot(clip, volume);
        }

        // ---------------------------------------------------------------- Sons (sintetizados)

        void BuildClips()
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            var rng = new System.Random(4242 + (int)Kind);

            // Zumbido em laço (2 s): disjuntor elétrico com estalos; válvula, ronco de água no cano;
            // alarme, um bipe curto a cada 2 s.
            int n = rate * 2;
            var hum = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float v;
                if (Kind == SwitchKind.Valve)
                {
                    v = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.25f + Mathf.Sin(2f * Mathf.PI * 82.5f * t) * 0.12f;
                    v += (float)(rng.NextDouble() * 2.0 - 1.0) * 0.18f * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 1.5f * t));
                }
                else if (Kind == SwitchKind.FireAlarm)
                {
                    float beep = t % 2f < 0.06f ? Mathf.Sin(2f * Mathf.PI * 2400f * t) * 0.3f : 0f;
                    v = beep + Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.03f;
                }
                else
                {
                    v = Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.3f + Mathf.Sin(2f * Mathf.PI * 200f * t) * 0.15f +
                        Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 100f * t)) * 0.06f;
                    if (rng.NextDouble() < 0.0006) v += 0.8f;
                }
                hum[i] = v * 0.7f;
            }
            _humClip = Clip("Zumbido_" + Kind, hum, rate);

            // Clique da alavanca (ou rangido da roda).
            int c = (int)(rate * (Kind == SwitchKind.Valve ? 0.35f : 0.06f));
            var click = new float[c];
            for (int i = 0; i < c; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * (Kind == SwitchKind.Valve ? 9f : 70f));
                float tone = Kind == SwitchKind.Valve ? Mathf.Sin(2f * Mathf.PI * (520f + 180f * Mathf.Sin(t * 30f)) * t) : Mathf.Sin(2f * Mathf.PI * 1800f * t);
                click[i] = (tone * 0.5f + (float)(rng.NextDouble() * 2.0 - 1.0) * 0.5f) * env * 0.8f;
            }
            _clickClip = Clip("Clique_" + Kind, click, rate);

            // Cumprido: um estalo grave e um zumbido subindo.
            int d = (int)(rate * 1.4f);
            var done = new float[d];
            for (int i = 0; i < d; i++)
            {
                float t = i / (float)rate;
                float thump = Mathf.Sin(2f * Mathf.PI * 60f * t) * Mathf.Exp(-t * 8f);
                float rise = Mathf.Sin(2f * Mathf.PI * (80f + 160f * t) * t) * Mathf.Clamp01(t * 3f) * Mathf.Clamp01((1.4f - t) * 2f) * 0.35f;
                done[i] = (thump + rise + (float)(rng.NextDouble() * 2.0 - 1.0) * 0.3f * Mathf.Exp(-t * 20f)) * 0.8f;
            }
            _doneClip = Clip("Cumprido_" + Kind, done, rate);

            if (Kind == SwitchKind.FireAlarm)
            {
                // Sirene do alarme: dois tons alternando, em laço (1 s).
                var siren = new float[rate];
                for (int i = 0; i < siren.Length; i++)
                {
                    float t = i / (float)rate;
                    float f = t < 0.5f ? 950f : 720f;
                    siren[i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * t)) * 0.25f + Mathf.Sin(2f * Mathf.PI * f * t) * 0.25f;
                }
                _alarmClip = Clip("Sirene", siren, rate);
            }
        }

        static AudioClip Clip(string name, float[] samples, int rate)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
