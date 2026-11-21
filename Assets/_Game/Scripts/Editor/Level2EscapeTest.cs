using System.Collections.Generic;
using System.IO;
using System.Linq;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Teste de fuga da água funda das Piscinas (Level 2), em Play: para cada região de água funda
    /// e poço da planta (células Fundas e de Poço ligadas por divisa aberta), põe o jogador no
    /// FUNDO de algumas células (a mais longe de qualquer escada, uma do meio e uma de poço, se
    /// houver) e, só com o teclado virtual (Espaço, W e o olhar), o faz subir à tona e nadar até a
    /// escada mais próxima e, noutra tentativa, até a borda (divisa aberta para seco ou raso sem
    /// escada) mais próxima. Passa quando fica de pé, fora d'água funda, num piso seco ou raso, em
    /// menos do tempo dado. O fôlego fica sem fim: a medida é de geometria, não de ar.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level2EscapeTest.BatchRun, com
    /// -seed N ou -seeds A,B,C (padrão 1998). Sai com 0 se todas as tentativas saíram, 3 se não.
    /// Cada tentativa vai para Logs/Fuga/fuga.txt (escrita na hora) e o resumo de cada seed para
    /// o log ("[BackRooms] Fuga seed N"); fotos dos casos que não saíram em Logs/Fuga. Linha final
    /// "[BackRooms] Teste de fuga PASSOU/FALHOU". Menu: BackRooms > Testar fuga da água funda.
    /// </summary>
    public static class Level2EscapeTest
    {
        const string ActiveKey = "BackRooms.Level2EscapeTest.Active";
        const string BatchKey = "BackRooms.Level2EscapeTest.Batch";
        const string ExitCodeKey = "BackRooms.Level2EscapeTest.ExitCode";
        const string SeedsKey = "BackRooms.Level2EscapeTest.Seeds";
        const string IndexKey = "BackRooms.Level2EscapeTest.Index";
        const string BetweenKey = "BackRooms.Level2EscapeTest.Between";
        const string TotalKey = "BackRooms.Level2EscapeTest.Total";
        const string FailedKey = "BackRooms.Level2EscapeTest.Failed";
        const string StairOkKey = "BackRooms.Level2EscapeTest.StairOk";
        const string EdgeOkKey = "BackRooms.Level2EscapeTest.EdgeOk";
        const string ErrorsKey = "BackRooms.Level2EscapeTest.Errors";
        // Com -undertow: tentativas em que o Undertow pegou o jogador (não contam como saída nem como presas).
        const string CaughtKey = "BackRooms.Level2EscapeTest.Caught";
        const float Reach = 0.35f;
        const float Step = 1f / 30f;   // tempo de jogo por quadro: o teste anda mais rápido que o relógio

        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };
        static readonly List<string> Errors = new List<string>();

        static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Fuga"));
        static string ResultFile => Path.Combine(Folder, "fuga.txt");

        [MenuItem("BackRooms/Testar fuga da água funda")]
        public static void RunFromMenu() { Run(false); }

        public static void BatchRun() { Run(true); }

        static void Run(bool batch)
        {
            var seeds = new List<int>();
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-seed" && int.TryParse(args[i + 1], out int one)) seeds.Add(one);
                if (args[i] == "-seeds")
                    foreach (string part in args[i + 1].Split(','))
                        if (int.TryParse(part.Trim(), out int s)) seeds.Add(s);
            }
            if (seeds.Count == 0) seeds.Add(1998);

            Directory.CreateDirectory(Folder);
            File.WriteAllText(ResultFile, $"Teste de fuga da água funda: seeds {string.Join(", ", seeds)}\n");
            SessionState.SetString(SeedsKey, string.Join(",", seeds));
            SessionState.SetInt(IndexKey, 0);
            SessionState.SetInt(TotalKey, 0);
            SessionState.SetInt(FailedKey, 0);
            SessionState.SetInt(StairOkKey, 0);
            SessionState.SetInt(EdgeOkKey, 0);
            SessionState.SetInt(ErrorsKey, 0);
            SessionState.SetInt(CaughtKey, 0);
            SessionState.SetBool(BetweenKey, false);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            Debug.Log($"[BackRooms] Teste de fuga: seeds {string.Join(", ", seeds)}");
            EditorSceneManager.OpenScene(Level2Setup.ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static int[] Seeds => SessionState.GetString(SeedsKey, "1998").Split(',').Select(int.Parse).ToArray();
        static int CurrentSeed => Seeds[Mathf.Min(SessionState.GetInt(IndexKey, 0), Seeds.Length - 1)];

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (SessionState.GetInt(ExitCodeKey, -1) >= 0)
            {
                EditorApplication.playModeStateChanged -= ExitAfterPlay;
                EditorApplication.playModeStateChanged += ExitAfterPlay;
                if (!EditorApplication.isPlayingOrWillChangePlaymode) ExitAfterPlay(PlayModeStateChange.EnteredEditMode);
                return;
            }

            if (!SessionState.GetBool(ActiveKey, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
            if (SessionState.GetBool(BetweenKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += NextSeed;
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(BetweenKey, false))
                EditorApplication.delayCall += NextSeed;
        }

        static void NextSeed()
        {
            if (!SessionState.GetBool(BetweenKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(BetweenKey, false);
            EditorApplication.EnterPlaymode();
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (stackTrace != null && (stackTrace.Contains("UnityEditor.Search.") || stackTrace.Contains("Unity.AI."))) return;
            Errors.Add(message);
        }

        // ---------------------------------------------------------------- Estado da seed

        enum Exit { Stair, Edge }

        sealed class Trial
        {
            public int Region;
            public Vector2Int Start;
            public string StartKind;
            public Exit Mode;
            public Vector2Int ExitCell, Upper;
            public WallSide ExitSide;
            public int Steps;
            public List<Vector3> Route;
            public float Limit;
            public string Room;
        }

        static int _stage;
        static PlayerNetwork _local;
        static PlayerController _controller;
        static CharacterController _body;
        static LevelBuilder _level;
        static Keyboard _keyboard;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;
        static List<Trial> _trials;
        static int _trial, _phase, _routeIndex, _photos;
        static float _trialStart, _phaseStart, _best, _lastProgress, _stageStart;
        static int _regionCount, _climbsBefore;
        static bool _climbSeen, _caught;
        static float _climbStart, _climbEnd;
        static readonly List<string> SeedLines = new List<string>();
        static readonly List<string> _regionLines = new List<string>();
        static int _seedFailed, _seedOk;

        static float Now => Time.time;

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            switch (_stage)
            {
                case 0:
                    Time.captureDeltaTime = Step;
                    session.fixedSeed = CurrentSeed;
                    // A maré tem teste próprio (Level2TideTest): aqui a água fica parada.
                    PoolTide.Paused = true;
                    // O Undertow fica pausado, a não ser com -undertow (para medir quantas fugas ele impede).
                    BackRooms.Entities.UndertowDirector.Paused = !System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a == "-undertow");
                    session.Host(localOnly: true);
                    _stageStart = Now;
                    _stage = 1;
                    break;

                case 1:
                    Begin(session);
                    break;

                case 2:
                    RunTrial();
                    break;
            }
        }

        static void Begin(GameSession session)
        {
            if (Now - _stageStart > 60f) { FinishSeed($"a sessão não começou em 60 s (fase {session.Phase})"); return; }
            if (session.Phase != SessionPhase.Playing) return;
            foreach (PlayerNetwork player in Object.FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None))
                if (player.IsSpawned && player.IsOwner) _local = player;
            _level = Object.FindFirstObjectByType<LevelBuilder>();
            if (_local == null || _level == null || _level.Data == null || PoolWater.Current == null) return;
            if (Now - _stageStart < 1.5f) return;

            _controller = _local.controller;
            _body = _local.GetComponent<CharacterController>();
            LevelData d = _level.Data;
            if (!d.HasPools) { FinishSeed("a fase não tem as Piscinas ligadas"); return; }
            if (d.Seed != CurrentSeed) { FinishSeed($"a planta montada é da seed {d.Seed}, não da {CurrentSeed}"); return; }

            _controller.airSeconds = 100000f;
            _controller.RefillAir();

            _backgroundBefore = InputSystem.settings.backgroundBehavior;
            _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>();

            _regionLines.Clear();
            _trials = PlanTrials(d);
            SeedLines.Clear();
            _seedFailed = _seedOk = 0;
            _photos = 0;
            Append($"\n===== seed {d.Seed} (usada de fato): {_regionCount} regiões de água funda/poço, {_trials.Count} tentativas");
            foreach (string line in _regionLines) Append(line);
            _trial = -1;
            NextTrial();
            _stage = 2;
        }

        // ---------------------------------------------------------------- Planejamento

        static bool Wet(LevelData d, int x, int z) => !d.IsSolid(x, z) && (d.Depth[x, z] == PoolDepth.Deep || d.Depth[x, z] == PoolDepth.Well);
        static bool Wading(LevelData d, int x, int z) => !d.IsSolid(x, z) && (d.Depth[x, z] == PoolDepth.Dry || d.Depth[x, z] == PoolDepth.Shallow);

        static List<List<Vector2Int>> Regions(LevelData d)
        {
            var seen = new bool[d.Width, d.Height];
            var all = new List<List<Vector2Int>>();
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (!Wet(d, x, z) || seen[x, z]) continue;
                    var region = new List<Vector2Int>();
                    var queue = new Queue<Vector2Int>();
                    queue.Enqueue(new Vector2Int(x, z));
                    seen[x, z] = true;
                    while (queue.Count > 0)
                    {
                        Vector2Int c = queue.Dequeue();
                        region.Add(c);
                        foreach (WallSide side in Sides)
                        {
                            if (!d.IsOpen(c.x, c.y, side) || !d.Neighbor(c.x, c.y, side, out int nx, out int nz) || !Wet(d, nx, nz) || seen[nx, nz]) continue;
                            seen[nx, nz] = true;
                            queue.Enqueue(new Vector2Int(nx, nz));
                        }
                    }
                    all.Add(region);
                }
            return all;
        }

        // Distâncias (em células) dentro da região, a partir de várias células, e o anterior de cada uma.
        static Dictionary<Vector2Int, int> Distances(LevelData d, HashSet<Vector2Int> region, IEnumerable<Vector2Int> sources, Dictionary<Vector2Int, Vector2Int> previous = null)
        {
            var dist = new Dictionary<Vector2Int, int>();
            var queue = new Queue<Vector2Int>();
            foreach (Vector2Int s in sources)
                if (!dist.ContainsKey(s)) { dist[s] = 0; queue.Enqueue(s); }
            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                foreach (WallSide side in Sides)
                {
                    if (!d.IsOpen(c.x, c.y, side) || !d.Neighbor(c.x, c.y, side, out int nx, out int nz)) continue;
                    var n = new Vector2Int(nx, nz);
                    if (!region.Contains(n) || dist.ContainsKey(n)) continue;
                    dist[n] = dist[c] + 1;
                    if (previous != null) previous[n] = c;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        struct ExitSpot
        {
            public Exit Kind;
            public Vector2Int Cell, Upper;
            public WallSide Side;
            public int Steps;
        }

        static List<ExitSpot> ExitsOf(LevelData d, HashSet<Vector2Int> region)
        {
            var exits = new List<ExitSpot>();
            foreach (PoolStair s in d.Stairs)
            {
                var cell = new Vector2Int(s.X, s.Z);
                if (!region.Contains(cell) || !d.Neighbor(s.X, s.Z, s.Side, out int ux, out int uz) || !Wading(d, ux, uz)) continue;
                exits.Add(new ExitSpot { Kind = Exit.Stair, Cell = cell, Upper = new Vector2Int(ux, uz), Side = s.Side, Steps = s.Steps });
            }
            foreach (Vector2Int c in region)
            {
                if (d.Depth[c.x, c.y] != PoolDepth.Deep) continue;
                foreach (WallSide side in Sides)
                {
                    if (!d.IsOpen(c.x, c.y, side) || !d.Neighbor(c.x, c.y, side, out int nx, out int nz) || !Wading(d, nx, nz)) continue;
                    if (d.StairAt(c.x, c.y, side, out _)) continue;
                    exits.Add(new ExitSpot { Kind = Exit.Edge, Cell = c, Upper = new Vector2Int(nx, nz), Side = side });
                }
            }
            return exits;
        }

        static List<Trial> PlanTrials(LevelData d)
        {
            var trials = new List<Trial>();
            List<List<Vector2Int>> regions = Regions(d);
            _regionCount = regions.Count;
            for (int r = 0; r < regions.Count; r++)
            {
                var region = new HashSet<Vector2Int>(regions[r]);
                List<ExitSpot> exits = ExitsOf(d, region);
                List<ExitSpot> stairs = exits.Where(e => e.Kind == Exit.Stair).ToList();
                List<ExitSpot> edges = exits.Where(e => e.Kind == Exit.Edge).ToList();
                var fromStairs = Distances(d, region, (stairs.Count > 0 ? stairs : exits).Select(e => e.Cell));

                // Células de partida: a mais longe da escada, uma do meio e uma de poço.
                var starts = new List<(Vector2Int cell, string kind)>();
                Vector2Int far = regions[r].OrderByDescending(c => fromStairs.TryGetValue(c, out int v) ? v : 999).ThenBy(c => c.x + c.y * d.Width).First();
                starts.Add((far, "longe"));
                int maxDist = fromStairs.TryGetValue(far, out int fd) ? fd : 0;
                List<Vector2Int> middles = regions[r].Where(c => c != far && d.Depth[c.x, c.y] == PoolDepth.Deep)
                                                     .OrderBy(c => Mathf.Abs((fromStairs.TryGetValue(c, out int v) ? v : 0) - maxDist / 2f)).ThenBy(c => c.x + c.y * d.Width).ToList();
                if (middles.Count > 0) starts.Add((middles[0], "meio"));
                List<Vector2Int> wells = regions[r].Where(c => d.Depth[c.x, c.y] == PoolDepth.Well && starts.All(s => s.cell != c)).OrderBy(c => c.x + c.y * d.Width).ToList();
                if (wells.Count > 0) starts.Add((wells[0], "poço"));

                _regionLines.Add($"  região {r}: {RoomName(d, regions[r][0])}, {regions[r].Count} células " +
                                 $"({regions[r].Count(c => d.Depth[c.x, c.y] == PoolDepth.Well)} de poço), {stairs.Count} escada(s), {edges.Count} trecho(s) de borda; " +
                                 $"a célula mais longe fica a {maxDist} célula(s) da escada mais próxima");
                foreach (var (cell, kind) in starts)
                    foreach (Exit mode in new[] { Exit.Stair, Exit.Edge })
                    {
                        List<ExitSpot> pool = mode == Exit.Stair ? stairs : edges;
                        if (pool.Count == 0) continue;
                        var previous = new Dictionary<Vector2Int, Vector2Int>();
                        var dist = Distances(d, region, new[] { cell }, previous);
                        ExitSpot best = pool.OrderBy(e => dist.TryGetValue(e.Cell, out int v) ? v : 999).ThenBy(e => e.Cell.x + e.Cell.y * d.Width).ThenBy(e => (int)e.Side).First();
                        if (!dist.ContainsKey(best.Cell)) continue;

                        var cells = new List<Vector2Int>();
                        for (Vector2Int c = best.Cell; ; c = previous[c])
                        {
                            cells.Add(c);
                            if (c == cell) break;
                        }
                        cells.Reverse();
                        trials.Add(new Trial
                        {
                            Region = r, Start = cell, StartKind = kind, Mode = mode, ExitCell = best.Cell, Upper = best.Upper,
                            ExitSide = best.Side, Steps = best.Steps, Room = RoomName(d, cell),
                            Route = BuildRoute(d, cells, best),
                            Limit = 25f + cells.Count * 5f
                        });
                    }
            }
            return trials;
        }

        static string RoomName(LevelData d, Vector2Int c)
        {
            int room = d.Room[c.x, c.y];
            return room >= 0 && room < d.Rooms.Count ? d.Rooms[room].Kind.ToString() : "?";
        }

        static Vector3 Dir(WallSide side) => side == WallSide.South ? Vector3.back : side == WallSide.North ? Vector3.forward
                                         : side == WallSide.West ? Vector3.left : Vector3.right;

        // Pelos centros das células (as colunas ficam longe das linhas de centro); na célula da
        // escada, primeiro até o pé dela, no eixo, sem passar por cima dos degraus; na borda, em
        // linha reta do centro da célula funda para o centro da vizinha, atravessando a divisa.
        static List<Vector3> BuildRoute(LevelData d, List<Vector2Int> cells, ExitSpot exit)
        {
            var route = new List<Vector3>();
            for (int k = 0; k + 1 < cells.Count; k++) route.Add(_level.CellCenter(cells[k].x, cells[k].y));

            Vector3 here = _level.CellCenter(exit.Cell.x, exit.Cell.y);
            if (exit.Kind == Exit.Stair)
            {
                Vector3 toUpper = Dir(exit.Side);
                Vector3 edge = here + toUpper * (LevelBuilder.Cell * 0.5f);
                float run = (exit.Steps - 1) * 0.3f;
                Vector3 foot = edge - toUpper * Mathf.Min(run + 0.5f, LevelBuilder.Cell - 0.3f);
                // Entrando pelo lado: primeiro anda para o lado até a altura do pé, ainda na célula anterior.
                Vector3 entry = route.Count > 0 ? route[route.Count - 1] : here;
                Vector3 lateral = Flat(entry - here) - Vector3.Dot(Flat(entry - here), toUpper) * toUpper;
                if (route.Count > 0 && lateral.magnitude > 0.3f) route.Add(foot + lateral);
                route.Add(foot);
            }
            else
            {
                route.Add(here);
            }
            route.Add(_level.CellCenter(exit.Upper.x, exit.Upper.y));
            return route;
        }

        // ---------------------------------------------------------------- Tentativa

        static void NextTrial()
        {
            _trial++;
            Press();
            if (_trial >= _trials.Count) { FinishSeed(null); return; }
            _phase = 0;
        }

        static void RunTrial()
        {
            Trial t = _trials[_trial];
            LevelData d = _level.Data;
            float surface = PoolWater.Current.SurfaceY;
            Vector3 position = _local.transform.position;

            switch (_phase)
            {
                case 0:
                {
                    // No fundo da célula de partida (fora de degraus ou coluna).
                    Vector3 center = _level.CellCenter(t.Start.x, t.Start.y);
                    float bottom = d.Depth[t.Start.x, t.Start.y] == PoolDepth.Well ? LevelData.DepthFloor(PoolDepth.Well) : LevelData.DepthFloor(PoolDepth.Deep);
                    Vector3 spot = new Vector3(center.x, bottom + 0.05f, center.z);
                    if (!Free(spot))
                    {
                        bool found = false;
                        foreach (Vector3 offset in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                            for (float r = 0.5f; r <= 1.5f && !found; r += 0.5f)
                                if (Free(spot + offset * r)) { spot += offset * r; found = true; }
                        if (!found) { Record(t, false, "não achou lugar livre no fundo da célula de partida"); return; }
                    }
                    _controller.RefillAir();
                    _controller.Teleport(spot, Yaw(Flat(t.Route[0] - spot)));
                    _trialStart = _phaseStart = Now;
                    _climbsBefore = _controller.Climbs;
                    _climbSeen = false;
                    _caught = false;
                    _climbEnd = -1f;
                    _routeIndex = 0;
                    _best = float.MaxValue;
                    _lastProgress = Now;
                    Press(Key.Space);
                    _phase = 1;
                    return;
                }

                case 1:
                    // Sobe segurando Espaço até ficar à tona.
                    if (Now - _phaseStart < 0.3f) return;
                    if (!_controller.IsUnderwater && Mathf.Abs(position.y - (surface - _controller.floatDepth)) < 0.2f)
                    {
                        _phase = 2;
                        _lastProgress = Now;
                        return;
                    }
                    if (Now - _phaseStart > 12f) Record(t, false, $"não subiu à tona do fundo em 12 s (pés a {position.y:F2} m)");
                    return;

                case 2:
                    // Pego pelo Undertow (-undertow): espera a volta ao início e conta à parte.
                    if (_controller.IsCaught) _caught = true;
                    if (_controller.IsReturning) return;
                    if (_caught)
                    {
                        SessionState.SetInt(CaughtKey, SessionState.GetInt(CaughtKey, 0) + 1);
                        SessionState.SetInt(TotalKey, SessionState.GetInt(TotalKey, 0) + 1);
                        Append($"  PEGO  região {t.Region} ({t.Room}), partida {t.StartKind} ({t.Start.x},{t.Start.y}): o Undertow pegou em {Now - _trialStart:F1} s");
                        NextTrial();
                        return;
                    }
                    if (_controller.IsClimbing && !_climbSeen)
                    {
                        _climbSeen = true;
                        _climbStart = Now;
                    }
                    if (_climbSeen && !_controller.IsClimbing && _climbEnd < 0f) _climbEnd = Now;
                    if (Escaped(out Vector2Int at))
                    {
                        bool climbed = _controller.Climbs > _climbsBefore;
                        string how = climbed ? $"subindo na borda nadando (subida de {(_climbEnd >= 0f ? _climbEnd - _climbStart : Now - _climbStart):F2} s)" : "pela escada";
                        Record(t, true, $"saiu {how} em {Now - _trialStart:F1} s em {Describe(d, at)}", climbed);
                        return;
                    }
                    if (Now - _trialStart > t.Limit) { Record(t, false, Stuck("tempo esgotado", t)); return; }

                    // Segue a rota com W (e Espaço), virando o olhar para o próximo ponto.
                    while (_routeIndex < t.Route.Count - 1 && Flat(t.Route[_routeIndex] - position).magnitude < Reach)
                    {
                        _routeIndex++;
                        _best = float.MaxValue;
                        _lastProgress = Now;
                    }
                    Vector3 toGoal = Flat(t.Route[_routeIndex] - position);
                    float distance = toGoal.magnitude;
                    if (distance < _best - 0.05f) { _best = distance; _lastProgress = Now; }
                    else if (Now - _lastProgress > 6f) { Record(t, false, Stuck("parou de avançar", t)); return; }

                    if (distance > 0.05f) _local.transform.rotation = Quaternion.Euler(0f, Yaw(toGoal), 0f);
                    Press(Key.Space, Key.W);
                    return;
            }
        }

        // De pé, fora da água funda, num piso seco ou raso.
        static bool Escaped(out Vector2Int cell)
        {
            Vector3 position = _local.transform.position;
            cell = CellOf(position);
            LevelData d = _level.Data;
            if (cell.x < 0 || cell.y < 0 || cell.x >= d.Width || cell.y >= d.Height) return false;
            return !_controller.IsSwimming && _body.isGrounded && position.y > -0.45f && Wading(d, cell.x, cell.y);
        }

        static string Describe(LevelData d, Vector2Int c) => $"({c.x},{c.y}) {d.Depth[c.x, c.y]}";

        static string Stuck(string why, Trial t)
        {
            Vector3 p = _local.transform.position;
            Vector2Int c = CellOf(p);
            float surface = PoolWater.Current.SurfaceY;
            return $"{why}: pés em ({p.x:F2}, {p.y:F2}, {p.z:F2}) ({p.y - surface:F2} da superfície), célula ({c.x},{c.y}), ponto {_routeIndex + 1}/{t.Route.Count} da rota " +
                   $"a {Flat(t.Route[_routeIndex] - p).magnitude:F2} m, nadando={_controller.IsSwimming}, no chão={_body.isGrounded}, " +
                   $"colisão={_body.collisionFlags}, velocidade=({_body.velocity.x:F2}, {_body.velocity.y:F2}, {_body.velocity.z:F2}), debaixo d'água={_controller.IsUnderwater}";
        }

        static void Record(Trial t, bool ok, string what, bool climbed = false)
        {
            string target = t.Mode == Exit.Stair ? $"escada de {t.Steps} degraus em ({t.ExitCell.x},{t.ExitCell.y}) {t.ExitSide} para {Describe(_level.Data, t.Upper)}"
                                                 : $"borda em ({t.ExitCell.x},{t.ExitCell.y}) {t.ExitSide} para {Describe(_level.Data, t.Upper)}";
            string line = $"  {(ok ? "SAIU " : "PRESO")} região {t.Region} ({t.Room}), partida {t.StartKind} ({t.Start.x},{t.Start.y}) " +
                          $"{_level.Data.Depth[t.Start.x, t.Start.y]}, rumo à {target}: {what}";
            if (ok)
            {
                _seedOk++;
                string key = climbed ? EdgeOkKey : StairOkKey;
                SessionState.SetInt(key, SessionState.GetInt(key, 0) + 1);
            }
            else
            {
                _seedFailed++;
                SessionState.SetInt(FailedKey, SessionState.GetInt(FailedKey, 0) + 1);
                if (_photos < 4) line += "; fotos " + Photo($"s{_level.Data.Seed}_r{t.Region}_{t.StartKind}_{(t.Mode == Exit.Stair ? "escada" : "borda")}");
                _photos++;
            }
            SessionState.SetInt(TotalKey, SessionState.GetInt(TotalKey, 0) + 1);
            Append(line);
            NextTrial();
        }

        // ---------------------------------------------------------------- Fotos

        static string Photo(string name)
        {
            Camera camera = _local.GetComponentInChildren<Camera>(true);
            if (camera == null) return "(sem câmera)";
            var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            RenderTexture before = camera.targetTexture;
            Vector3 eyePosition = camera.transform.position;
            Quaternion eyeRotation = camera.transform.rotation;
            try
            {
                camera.targetTexture = target;
                camera.transform.rotation = Quaternion.Euler(20f, _local.transform.eulerAngles.y, 0f);
                Level0Preview.Shoot(camera, target, readback, Path.Combine(Folder, name + "_olho.png"));

                // De fora: 3 m atrás e 1,5 m acima, sem atravessar parede.
                Vector3 head = _local.transform.position + Vector3.up * 1.2f;
                Vector3 back = -_local.transform.forward * 3f + Vector3.up * 1.5f;
                float reach = back.magnitude;
                if (Physics.Raycast(head, back.normalized, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore)) reach = Mathf.Max(0.3f, hit.distance - 0.2f);
                camera.transform.position = head + back.normalized * reach;
                camera.transform.rotation = Quaternion.LookRotation(_local.transform.position + Vector3.up * 0.5f - camera.transform.position);
                Level0Preview.Shoot(camera, target, readback, Path.Combine(Folder, name + "_fora.png"));
            }
            finally
            {
                camera.targetTexture = before;
                camera.transform.SetPositionAndRotation(eyePosition, eyeRotation);
                Object.DestroyImmediate(readback);
                target.Release();
                Object.DestroyImmediate(target);
            }
            return $"Logs/Fuga/{name}_olho.png e _fora.png";
        }

        // ---------------------------------------------------------------- Apoio

        static bool Free(Vector3 feet)
        {
            float radius = _body.radius;
            Vector3 a = feet + Vector3.up * (radius + 0.05f), b = feet + Vector3.up * (_controller.standingHeight - radius);
            foreach (Collider c in Physics.OverlapCapsule(a, b, radius, ~0, QueryTriggerInteraction.Ignore))
                if (c.transform.root != _local.transform.root) return false;
            return true;
        }

        static float Yaw(Vector3 direction) => direction.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        static void Press(params Key[] keys)
        {
            if (_keyboard != null) InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        }

        static void ReleaseKeyboard()
        {
            if (_keyboard == null) return;
            InputSystem.RemoveDevice(_keyboard);
            _keyboard = null;
            InputSystem.settings.backgroundBehavior = _backgroundBefore;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBefore;
        }

        static void Append(string line)
        {
            SeedLines.Add(line);
            try { File.AppendAllText(ResultFile, line + "\n"); } catch (IOException) { }
        }

        static Vector2Int CellOf(Vector3 world)
        {
            Vector3 local = _level.transform.InverseTransformPoint(world);
            return new Vector2Int(Mathf.FloorToInt(local.x / LevelBuilder.Cell), Mathf.FloorToInt(local.z / LevelBuilder.Cell));
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // ---------------------------------------------------------------- Fim

        static void FinishSeed(string problem)
        {
            ReleaseKeyboard();
            Time.captureDeltaTime = 0f;
            int seed = CurrentSeed;
            if (problem != null)
            {
                SessionState.SetInt(FailedKey, SessionState.GetInt(FailedKey, 0) + 1);
                Append($"  seed {seed}: {problem}");
            }
            if (Errors.Count > 0)
            {
                SessionState.SetInt(ErrorsKey, SessionState.GetInt(ErrorsKey, 0) + Errors.Count);
                Append($"  {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}");
                Errors.Clear();
            }
            if (!BackRooms.Entities.UndertowDirector.Paused)
            {
                var undertow = BackRooms.Entities.UndertowBrain.Instance;
                Append(undertow == null ? "  Undertow: não apareceu"
                                        : $"  Undertow: ouviu {undertow.NoisesHeard} barulho(s), ignorou {undertow.NoisesIgnored}, pegou {undertow.Catches}, canos {undertow.Travels}, estado no fim {undertow.State}");
            }
            Debug.Log($"[BackRooms] Fuga seed {seed}: {_regionCount} regiões, {_seedOk} tentativas saíram, {_seedFailed} presas" + (problem != null ? $" ({problem})" : ""));

            int index = SessionState.GetInt(IndexKey, 0) + 1;
            SessionState.SetInt(IndexKey, index);
            _stage = 0;
            _local = null;
            _level = null;
            if (index < Seeds.Length)
            {
                SessionState.SetBool(BetweenKey, true);
                EditorApplication.ExitPlaymode();
                return;
            }
            Finish();
        }

        static void Finish()
        {
            PoolTide.Paused = false;
            BackRooms.Entities.UndertowDirector.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            SessionState.SetBool(ActiveKey, false);

            int total = SessionState.GetInt(TotalKey, 0), failed = SessionState.GetInt(FailedKey, 0), errors = SessionState.GetInt(ErrorsKey, 0);
            int stairs = SessionState.GetInt(StairOkKey, 0), edges = SessionState.GetInt(EdgeOkKey, 0);
            int caught = SessionState.GetInt(CaughtKey, 0);
            bool ok = failed == 0 && errors == 0 && total > 0 && stairs > 0 && edges > 0;
            string report = $"seeds {string.Join(", ", Seeds)}: {total} tentativas, {total - failed - caught} saíram ({stairs} pela escada, {edges} pela borda), {failed} presas, {(caught > 0 ? $"{caught} pegas pelo Undertow, " : "")}{errors} erro(s) no console; detalhes em Logs/Fuga/fuga.txt";
            Append(report);
            Debug.Log("[BackRooms] Teste de fuga " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

            if (SessionState.GetBool(BatchKey, false))
            {
                SessionState.SetInt(ExitCodeKey, ok ? 0 : 3);
                EditorApplication.playModeStateChanged -= ExitAfterPlay;
                EditorApplication.playModeStateChanged += ExitAfterPlay;
            }
            EditorApplication.ExitPlaymode();
        }

        static void ExitAfterPlay(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= ExitAfterPlay;
            int code = SessionState.GetInt(ExitCodeKey, 1);
            SessionState.EraseInt(ExitCodeKey);
            Level1Setup.ExitWithLevel0Open(code);
        }
    }
}
