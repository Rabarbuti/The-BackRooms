using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.Entities
{
    /// <summary>
    /// Onde é escuro no Escritório, célula por célula: o chão do Zelador (<see cref="JanitorBrain"/>).
    ///
    /// Uma célula é escura se tem luminária e nenhuma das dela está acesa de verdade: queimada
    /// (<see cref="CeilingLight.Works"/> falso), apagada pela onda (<see cref="CeilingLight.ForcedDark"/>)
    /// ou, depois do alarme de incêndio, apagada pelo corte de energia (menos as células com luz de
    /// emergência e os abrigos). Célula sem luminária (o vão da escada, os poços) nunca é escura: lá
    /// ele não pisa. Não usa <see cref="CeilingLight.IsLit"/>: a luminária que pisca sorteia as
    /// piscadas em cada máquina, e o chão dele ficaria trocando a cada quadro.
    /// </summary>
    public static class JanitorDark
    {
        static LevelBuilder _level;
        static LevelData _data;
        static int _lampCount = -1;
        static List<CeilingLight>[] _lamps;
        static bool[] _emergency;

        /// <summary>Corte de energia do alarme de incêndio valendo (posto pelo <see cref="JanitorDirector"/>).</summary>
        public static bool Blackout { get; private set; }

        /// <summary>A fase cujo escuro está mapeado.</summary>
        public static LevelBuilder Level => _level;

        /// <summary>Monta (ou confere) o mapa das luminárias por célula da fase dada.</summary>
        public static bool Bind(LevelBuilder level)
        {
            if (level == null || level.Data == null) return false;
            if (_level == level && _data == level.Data && _lampCount == CeilingLight.All.Count) return true;

            _level = level;
            _data = level.Data;
            _lampCount = CeilingLight.All.Count;
            int w = _data.Width, h = _data.Height;
            _lamps = new List<CeilingLight>[w * h];
            if (_emergency == null || _emergency.Length != w * h)
            {
                _emergency = new bool[w * h];
                Blackout = false;
            }

            Transform root = level.transform;
            foreach (CeilingLight lamp in CeilingLight.All)
            {
                if (lamp == null || !lamp.transform.IsChildOf(root)) continue;
                if (!Cell(lamp.transform.position, out int x, out int z)) continue;
                int i = x + z * w;
                (_lamps[i] ?? (_lamps[i] = new List<CeilingLight>())).Add(lamp);
            }
            return true;
        }

        /// <summary>A célula do ponto (no mundo). Falso fora do mapa.</summary>
        public static bool Cell(Vector3 world, out int x, out int z)
        {
            x = z = -1;
            if (_level == null || _data == null) return false;
            Vector3 local = _level.transform.InverseTransformPoint(world);
            x = Mathf.FloorToInt(local.x / LevelBuilder.Cell);
            z = Mathf.FloorToInt(local.z / LevelBuilder.Cell);
            return x >= 0 && z >= 0 && x < _data.Width && z < _data.Height;
        }

        /// <summary>A célula tem luminária?</summary>
        public static bool HasLamp(int x, int z) => _lamps != null && InGrid(x, z) && _lamps[x + z * _data.Width] != null;

        /// <summary>As luminárias da célula (vazio se não tem).</summary>
        public static IReadOnlyList<CeilingLight> LampsAt(int x, int z) =>
            _lamps != null && InGrid(x, z) && _lamps[x + z * _data.Width] != null ? _lamps[x + z * _data.Width] : (IReadOnlyList<CeilingLight>)System.Array.Empty<CeilingLight>();

        /// <summary>A célula tem luz de emergência (só vale no corte de energia)?</summary>
        public static bool Emergency(int x, int z) => _emergency != null && InGrid(x, z) && _emergency[x + z * _data.Width];

        /// <summary>A célula está escura agora (é chão do Zelador, se não for abrigo)?</summary>
        public static bool IsDark(int x, int z)
        {
            if (_lamps == null || !InGrid(x, z)) return false;
            List<CeilingLight> lamps = _lamps[x + z * _data.Width];
            if (lamps == null) return false;

            if (Blackout) return !_emergency[x + z * _data.Width] && !_data.IsShelter(x, z);

            // Piscando no aviso da onda ainda conta como acesa (é o tempo de o jogador sair).
            OfficeLightWave wave = OfficeLightWave.Instance;
            foreach (CeilingLight lamp in lamps)
                if (lamp != null && lamp.Works && (!lamp.ForcedDark || (wave != null && wave.InWarning(lamp)))) return false;
            return true;
        }

        /// <summary>O ponto (no mundo) está numa célula escura?</summary>
        public static bool IsDarkAt(Vector3 world) => Cell(world, out int x, out int z) && IsDark(x, z);

        /// <summary>Quantas células escuras há agora (para os testes e o diagnóstico).</summary>
        public static int DarkCells()
        {
            if (_data == null) return 0;
            int n = 0;
            for (int z = 0; z < _data.Height; z++)
                for (int x = 0; x < _data.Width; x++)
                    if (IsDark(x, z)) n++;
            return n;
        }

        static bool InGrid(int x, int z) => _data != null && x >= 0 && z >= 0 && x < _data.Width && z < _data.Height;

        internal static void SetBlackout(bool on, bool[] emergency)
        {
            Blackout = on;
            if (emergency != null) _emergency = emergency;
        }

        // ---------------------------------------------------------------- Rotas de fuga

        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

        /// <summary>
        /// Rota a pé (pelas divisas abertas) de uma célula até a entrada da escada, a mais curta, na
        /// mesma ordem em todas as máquinas. Vazia se não há.
        /// </summary>
        public static List<Vector2Int> RouteToStairs(LevelData data, int fromX, int fromZ)
        {
            var route = new List<Vector2Int>();
            if (data == null || !data.HasOffice) return route;
            int w = data.Width, h = data.Height;
            int hx = data.Atrium.HeadX, hz = data.Atrium.HeadZ;
            var distance = new int[w * h];
            for (int i = 0; i < distance.Length; i++) distance[i] = -1;
            var queue = new Queue<int>();
            distance[hx + hz * w] = 0;
            queue.Enqueue(hx + hz * w);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int x = c % w, z = c / w;
                foreach (WallSide side in Sides)
                {
                    if (!data.CanWalk(x, z, side, false) || !data.Neighbor(x, z, side, out int nx, out int nz)) continue;
                    int n = nx + nz * w;
                    if (distance[n] >= 0) continue;
                    distance[n] = distance[c] + 1;
                    queue.Enqueue(n);
                }
            }

            if (fromX < 0 || fromZ < 0 || fromX >= w || fromZ >= h || distance[fromX + fromZ * w] < 0) return route;
            int cx = fromX, cz = fromZ;
            route.Add(new Vector2Int(cx, cz));
            while (distance[cx + cz * w] > 0)
            {
                bool moved = false;
                foreach (WallSide side in Sides)
                {
                    if (!data.CanWalk(cx, cz, side, false) || !data.Neighbor(cx, cz, side, out int nx, out int nz)) continue;
                    if (distance[nx + nz * w] != distance[cx + cz * w] - 1) continue;
                    cx = nx;
                    cz = nz;
                    route.Add(new Vector2Int(cx, cz));
                    moved = true;
                    break;
                }
                if (!moved) break;
            }
            return route;
        }

        /// <summary>
        /// As células com luz de emergência no corte de energia do alarme: ao longo da rota de cada
        /// acionador até a escada e do caminho da cabine até a escada, uma célula acesa a cada
        /// <paramref name="every"/> (a do acionador e a da entrada da escada sempre). Só depende da
        /// planta e dos acionadores, que saem da seed: igual em todas as máquinas.
        /// </summary>
        public static bool[] EmergencyCells(LevelData data, IList<Vector2Int> starts, int every)
        {
            int w = data.Width, h = data.Height;
            var lit = new bool[w * h];
            void Mark(List<Vector2Int> route)
            {
                for (int i = 0; i < route.Count; i++)
                    if (i % every == 0 || i == route.Count - 1) lit[route[i].x + route[i].y * w] = true;
            }

            foreach (Vector2Int start in starts) Mark(RouteToStairs(data, start.x, start.y));
            if (data.PathCells != null && data.PathCells.Count > 0)
                Mark(RouteToStairs(data, data.PathCells[0] % w, data.PathCells[0] / w));
            return lit;
        }
    }

    /// <summary>
    /// O Zelador no Level 3. Em todas as máquinas: o corte de energia do alarme de incêndio (o
    /// objetivo cumprido apaga as luzes do andar, menos os abrigos, e acende a luz de emergência
    /// fraca ao longo das rotas até a escada). Só no host: põe o Zelador na sessão (escondido;
    /// quem decide aparecer no escuro é o <see cref="JanitorBrain"/>). Não usa o
    /// <see cref="EntityDirector"/>, que fica com o inimigo do elevador.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public sealed class JanitorDirector : MonoBehaviour
    {
        public static JanitorDirector Instance { get; private set; }

        public GameSession session;
        public GameObject janitorPrefab;

        [Tooltip("Desligado, a fase fica sem Zelador (o corte de energia do alarme continua).")]
        public bool janitorEnabled = true;

        [Tooltip("Luz de emergência a cada tantas células das rotas até a escada.")]
        public int emergencyEvery = 3;
        [Tooltip("Luz de emergência, em candela.")]
        public float emergencyCandela = 90f;
        public Color emergencyColor = new Color(1f, 0.32f, 0.16f);

        /// <summary>O Zelador desta sessão (só no host, depois de posto).</summary>
        public JanitorBrain Janitor { get; private set; }

        readonly List<GameObject> _fixtures = new List<GameObject>();

        /// <summary>Quantas luzes de emergência estão acesas (no corte de energia do alarme).</summary>
        public int EmergencyLights => _fixtures.Count;
        readonly List<Vector2Int> _starts = new List<Vector2Int>();
        LevelBuilder _blackoutLevel;
        LightSwitches _switches;
        LevelData _switchesLevel;
        bool _blackout;

        void Awake()
        {
            Instance = this;
            if (session == null) session = GetComponent<GameSession>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            EndBlackout();
        }

        void Update()
        {
            LevelBuilder level = session != null ? session.level : null;
            bool playing = session != null && session.Phase == SessionPhase.Playing && session.LevelReady && level != null && level.Data != null;
            if (!playing)
            {
                EndBlackout();
                Janitor = null;
                return;
            }

            JanitorDark.Bind(level);
            UpdateBlackout(level);

            // Os interruptores de luz, um por trecho da onda (em cada máquina, no mesmo lugar).
            if (level.Data.HasOffice && (_switches == null || _switchesLevel != level.Data) && OfficeLightWave.Instance != null)
            {
                if (_switches != null) Destroy(_switches.gameObject);
                _switches = LightSwitches.Build(level, OfficeLightWave.Instance);
                _switchesLevel = level.Data;
            }

            if (!session.IsHosting || !janitorEnabled || janitorPrefab == null || Janitor != null) return;
            Spawn(level);
        }

        void Spawn(LevelBuilder level)
        {
            // Nasce escondido, fora do mapa; aparece sozinho no escuro.
            Vector3 park = JanitorBrain.ParkPosition(level);
            NetworkObject spawned = NetworkObject.InstantiateAndSpawn(janitorPrefab, GetComponent<NetworkManager>(),
                NetworkManager.ServerClientId, destroyWithScene: true, position: park, rotation: Quaternion.identity);
            Janitor = spawned != null ? spawned.GetComponent<JanitorBrain>() : null;
        }

        // ---------------------------------------------------------------- Corte de energia

        void UpdateBlackout(LevelBuilder level)
        {
            SwitchObjective objective = SwitchObjective.Instance;
            bool want = level.Data.HasOffice && objective != null && objective.Kind == SwitchKind.FireAlarm && objective.Complete;
            if (want && (!_blackout || _blackoutLevel != level)) StartBlackout(level, objective);
            else if (!want && _blackout) EndBlackout();
        }

        void StartBlackout(LevelBuilder level, SwitchObjective objective)
        {
            EndBlackout();
            _blackout = true;
            _blackoutLevel = level;
            LevelData data = level.Data;

            _starts.Clear();
            for (int i = 0; i < objective.Count; i++) _starts.Add(objective.PointCell(i));
            bool[] emergency = JanitorDark.EmergencyCells(data, _starts, Mathf.Max(1, emergencyEvery));
            JanitorDark.SetBlackout(true, emergency);

            // Apaga pela fase da energia (o LightsOut fica parado no Escritório), por baixo da onda:
            // quando a onda devolve uma luminária, ela continua sem energia.
            Transform root = level.transform;
            foreach (CeilingLight lamp in CeilingLight.All)
            {
                if (lamp == null || !lamp.transform.IsChildOf(root)) continue;
                if (JanitorDark.Cell(lamp.transform.position, out int x, out int z) && data.IsShelter(x, z)) continue;
                lamp.SetPower(PowerPhase.Dark);
            }

            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++)
                    if (emergency[x + z * data.Width] && JanitorDark.HasLamp(x, z) && !data.IsShelter(x, z))
                        _fixtures.Add(EmergencyFixture(level, x, z));
        }

        void EndBlackout()
        {
            foreach (GameObject fixture in _fixtures)
                if (fixture != null) Destroy(fixture);
            _fixtures.Clear();

            if (_blackout && _blackoutLevel != null)
            {
                PowerPhase phase = LightsOut.Instance != null ? LightsOut.Instance.Phase : PowerPhase.Steady;
                Transform root = _blackoutLevel.transform;
                foreach (CeilingLight lamp in CeilingLight.All)
                    if (lamp != null && lamp.transform.IsChildOf(root)) lamp.SetPower(phase);
            }

            _blackout = false;
            _blackoutLevel = null;
            JanitorDark.SetBlackout(false, null);
        }

        // A luz de emergência: caixinha escura de dois faróis na parede de cima, acesa em vermelho
        // fraco, e um spot largo sem sombra para baixo.
        GameObject EmergencyFixture(LevelBuilder level, int x, int z)
        {
            Vector3 center = level.CellCenter(x, z);
            var root = new GameObject($"LuzDeEmergencia_{x}_{z}");
            root.transform.SetParent(level.transform, true);
            root.transform.position = center + Vector3.up * 2.55f + Vector3.right * 0.55f;

            Material body = level.office != null ? level.office.darkMetal : null;
            Material head = level.office != null ? level.office.ledRed : null;
            Box(root.transform, "Caixa", Vector3.zero, new Vector3(0.36f, 0.12f, 0.1f), body);
            Box(root.transform, "Farol_0", new Vector3(-0.12f, -0.08f, 0f), new Vector3(0.07f, 0.05f, 0.07f), head);
            Box(root.transform, "Farol_1", new Vector3(0.12f, -0.08f, 0f), new Vector3(0.07f, 0.05f, 0.07f), head);

            var go = new GameObject("Luz");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.12f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var source = go.AddComponent<Light>();
            source.type = LightType.Spot;
            if (!go.TryGetComponent(out HDAdditionalLightData hd)) hd = go.AddComponent<HDAdditionalLightData>();
            HDAdditionalLightData.InitDefaultHDAdditionalLightData(hd);
            source.lightUnit = LightUnit.Candela;
            source.useColorTemperature = false;
            source.color = emergencyColor;
            source.intensity = emergencyCandela;
            source.range = 7f;
            source.spotAngle = 140f;
            source.innerSpotAngle = 60f;
            source.shadows = LightShadows.None;
            hd.fadeDistance = 40f;
            return root;
        }

        static void Box(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            var renderer = box.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (material != null) renderer.sharedMaterial = material;
        }
    }
}
