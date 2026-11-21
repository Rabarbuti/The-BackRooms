using System.Collections.Generic;
using System.IO;
using System.Linq;
using BackRooms.Entities;
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
    /// A maré alta das Piscinas não prende ninguém (<see cref="PoolTide"/>): com a maré forçada no
    /// alto (<see cref="PoolTide.ForceSeconds"/>), põe o jogador DE PÉ, com o fôlego de verdade (20 s),
    /// em cada célula rasa do caminho a pé até a saída (onde a maré o pega andando), nas rasas e
    /// fundas mais longe de um seco, e, só com o teclado virtual (Espaço e W, olhando para o próximo
    /// ponto), o faz subir à tona e nadar até o seco mais perto, onde fica de pé, respirando, à espera
    /// da vazante. Passa quando chega de pé ao seco, fora d'água e sem se afogar, no tempo dado. O
    /// Undertow fica pausado: aqui a medida é de geometria e de fôlego (ele tem teste próprio). Desde
    /// 21/11/2026 o seco alagado não é mais abrigo do Undertow (na maré alta ele caça no mapa inteiro,
    /// menos a sala inicial): o seco continua sendo onde se respira, e é isso que se mede aqui.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level2HighTideTest.BatchRun, com -seed N
    /// ou -seeds A,B,C (padrão 1998). Sai com 0 se todas as tentativas chegaram, 3 se não. Cada
    /// tentativa em Logs/MareAlta/mare_alta.txt; linha final "[BackRooms] Teste da maré alta".
    /// </summary>
    public static class Level2HighTideTest
    {
        const string ActiveKey = "BackRooms.Level2HighTideTest.Active";
        const string BatchKey = "BackRooms.Level2HighTideTest.Batch";
        const string ExitCodeKey = "BackRooms.Level2HighTideTest.ExitCode";
        const string SeedsKey = "BackRooms.Level2HighTideTest.Seeds";
        const string IndexKey = "BackRooms.Level2HighTideTest.Index";
        const string BetweenKey = "BackRooms.Level2HighTideTest.Between";
        const string TotalKey = "BackRooms.Level2HighTideTest.Total";
        const string FailedKey = "BackRooms.Level2HighTideTest.Failed";
        const string ErrorsKey = "BackRooms.Level2HighTideTest.Errors";
        const string WorstKey = "BackRooms.Level2HighTideTest.Worst";
        const float Step = 1f / 30f;
        const float Reach = 0.4f;

        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };
        static readonly List<string> Errors = new List<string>();

        static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "MareAlta"));
        static string ResultFile => Path.Combine(Folder, "mare_alta.txt");

        [MenuItem("BackRooms/Testar fuga na maré alta")]
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
            File.WriteAllText(ResultFile, $"Teste da maré alta: seeds {string.Join(", ", seeds)}, maré +{PoolTide.Amplitude:F2} m\n");
            SessionState.SetString(SeedsKey, string.Join(",", seeds));
            SessionState.SetInt(IndexKey, 0);
            SessionState.SetInt(TotalKey, 0);
            SessionState.SetInt(FailedKey, 0);
            SessionState.SetInt(ErrorsKey, 0);
            SessionState.SetFloat(WorstKey, 0f);
            SessionState.SetBool(BetweenKey, false);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
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

        // ---------------------------------------------------------------- Estado

        sealed class Trial
        {
            public Vector2Int Start;
            public string Why;
            public List<Vector3> Route;
            public Vector2Int Goal;
            public float Limit;
        }

        static int _stage, _trial, _phase, _routeIndex, _drownings, _seedOk, _seedFailed;
        static PlayerNetwork _local;
        static PlayerController _controller;
        static CharacterController _body;
        static LevelBuilder _level;
        static Keyboard _keyboard;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;
        static List<Trial> _trials;
        static float _trialStart, _best, _lastProgress, _stageStart, _minAir, _underwaterTime;

        static float Now => Time.time;
        static double HighTide => PoolTide.FirstPeak + PoolTide.HighSeconds * 0.5;

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
                    PoolTide.Paused = false;
                    PoolTide.ForceSeconds = HighTide;
                    EntityDirector.Paused = true;
                    UndertowDirector.Paused = true;
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
            _local = PlayerNetwork.Local;
            _level = session.level;
            if (_local == null || _level == null || _level.Data == null || PoolWater.Current == null || PoolTide.Current == null) return;
            if (Now - _stageStart < 2f) return;
            LevelData d = _level.Data;
            if (d.Seed != CurrentSeed) { FinishSeed($"a planta montada é da seed {d.Seed}, não da {CurrentSeed}"); return; }
            float rise = PoolWater.Current.Offset;
            if (Mathf.Abs(rise - PoolTide.Amplitude) > 0.02f) { FinishSeed($"com o relógio na maré alta a água subiu {rise:F2} m (eram {PoolTide.Amplitude})"); return; }

            _controller = _local.controller;
            _body = _local.GetComponent<CharacterController>();
            _backgroundBefore = InputSystem.settings.backgroundBehavior;
            _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>();

            _trials = PlanTrials(d);
            _seedOk = _seedFailed = 0;
            Append($"\n===== seed {d.Seed}: água +{rise:F2} m, {_trials.Count} tentativas");
            _trial = -1;
            NextTrial();
            _stage = 2;
        }

        // ---------------------------------------------------------------- Planejamento

        // Na maré alta passa-se por toda divisa aberta (nadando à tona); o seco é onde se respira.
        static Dictionary<Vector2Int, int> RefugeDistances(LevelData d, out Dictionary<Vector2Int, Vector2Int> next)
        {
            var dist = new Dictionary<Vector2Int, int>();
            next = new Dictionary<Vector2Int, Vector2Int>();
            var queue = new Queue<Vector2Int>();
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    if (!d.IsSolid(x, z) && d.Depth[x, z] == PoolDepth.Dry)
                    {
                        var c = new Vector2Int(x, z);
                        dist[c] = 0;
                        queue.Enqueue(c);
                    }
            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                foreach (WallSide side in Sides)
                {
                    if (!d.IsOpen(c.x, c.y, side) || !d.Neighbor(c.x, c.y, side, out int nx, out int nz) || d.IsSolid(nx, nz)) continue;
                    var n = new Vector2Int(nx, nz);
                    if (dist.ContainsKey(n)) continue;
                    dist[n] = dist[c] + 1;
                    next[n] = c;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        static List<Trial> PlanTrials(LevelData d)
        {
            Dictionary<Vector2Int, int> dist = RefugeDistances(d, out Dictionary<Vector2Int, Vector2Int> next);
            var starts = new List<(Vector2Int cell, string why)>();
            var taken = new HashSet<Vector2Int>();

            // As rasas do caminho a pé até a saída (onde a maré pega quem anda por ele).
            foreach (int i in PoolGenerator.FootPath(d))
            {
                var c = new Vector2Int(i % d.Width, i / d.Width);
                if (d.Depth[c.x, c.y] == PoolDepth.Shallow && taken.Add(c)) starts.Add((c, "caminho"));
            }
            // As rasas e fundas mais longe de um seco.
            foreach (PoolDepth depth in new[] { PoolDepth.Shallow, PoolDepth.Deep })
            {
                var cells = new List<Vector2Int>();
                for (int z = 0; z < d.Height; z++)
                    for (int x = 0; x < d.Width; x++)
                        if (!d.IsSolid(x, z) && d.Depth[x, z] == depth && !d.InRoom(x, z)) cells.Add(new Vector2Int(x, z));
                cells.Sort((a, b) => (dist.TryGetValue(b, out int db) ? db : 999).CompareTo(dist.TryGetValue(a, out int da) ? da : 999));
                int added = 0;
                foreach (Vector2Int c in cells)
                {
                    if (added >= 3) break;
                    if (!taken.Add(c)) continue;
                    starts.Add((c, depth == PoolDepth.Shallow ? "rasa longe do seco" : "funda longe do seco"));
                    added++;
                }
            }

            var trials = new List<Trial>();
            foreach (var (cell, why) in starts)
            {
                var t = new Trial { Start = cell, Why = why, Route = new List<Vector3>() };
                if (!dist.ContainsKey(cell)) { t.Route = null; trials.Add(t); continue; }
                Vector2Int at = cell;
                while (dist[at] > 0)
                {
                    Vector2Int n = next[at];
                    Vector3 a = _level.CellCenter(at.x, at.y), b = _level.CellCenter(n.x, n.y);
                    t.Route.Add((a + b) * 0.5f);
                    t.Route.Add(b);
                    at = n;
                }
                t.Goal = at;
                t.Limit = dist[cell] * LevelBuilder.Cell / 0.9f + 15f;
                trials.Add(t);
            }
            return trials;
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
            Vector3 position = _local.transform.position;

            if (_phase == 0)
            {
                if (t.Route == null) { Record(t, false, "não há seco alcançável por divisa aberta"); return; }
                Vector3 center = _level.CellCenter(t.Start.x, t.Start.y);
                Vector3 spot = center + Vector3.up * 0.05f;
                if (!Free(spot))
                {
                    bool found = false;
                    foreach (Vector3 offset in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                        for (float r = 0.5f; r <= 1.5f && !found; r += 0.5f)
                            if (Free(spot + offset * r)) { spot += offset * r; found = true; }
                    if (!found) { Record(t, false, "não achou lugar livre na célula de partida"); return; }
                }
                _controller.RefillAir();
                Vector3 first = t.Route.Count > 0 ? t.Route[0] : center;
                _controller.Teleport(spot, Yaw(Flat(first - spot)));
                _controller.SetPitch(0f);
                _drownings = _controller.Drownings;
                _trialStart = _lastProgress = Now;
                _routeIndex = 0;
                _best = float.MaxValue;
                _minAir = 1f;
                _underwaterTime = 0f;
                _phase = 1;
                return;
            }

            if (_controller.Drownings > _drownings || _controller.IsReturning) { Record(t, false, $"afogou-se ({Stuck(t)})"); return; }
            _minAir = Mathf.Min(_minAir, _controller.Air01);
            if (_controller.IsUnderwater) _underwaterTime += Time.deltaTime;
            if (Safe(out Vector2Int at))
            {
                Record(t, true, $"de pé no seco ({at.x},{at.y}), respirando, em {Now - _trialStart:F1} s; menor fôlego {_minAir * 100f:F0}%, {_underwaterTime:F1} s com a cabeça debaixo d'água");
                return;
            }
            if (Now - _trialStart > t.Limit) { Record(t, false, $"tempo esgotado ({t.Limit:F0} s): {Stuck(t)}"); return; }

            while (_routeIndex < t.Route.Count - 1 && Flat(t.Route[_routeIndex] - position).magnitude < Reach)
            {
                _routeIndex++;
                _best = float.MaxValue;
                _lastProgress = Now;
            }
            Vector3 to = Flat(t.Route[Mathf.Min(_routeIndex, t.Route.Count - 1)] - position);
            if (to.magnitude < _best - 0.05f) { _best = to.magnitude; _lastProgress = Now; }
            else if (Now - _lastProgress > 8f) { Record(t, false, $"parou de avançar: {Stuck(t)}"); return; }
            if (to.sqrMagnitude > 0.0025f) _local.transform.rotation = Quaternion.Euler(0f, Yaw(to), 0f);
            Press(Key.Space, Key.W);
        }

        // De pé num seco, fora d'água (respirando), sem nadar: onde se respira na maré alta (não é mais
        // abrigo do Undertow, só do afogamento).
        static bool Safe(out Vector2Int cell)
        {
            cell = CellOf(_local.transform.position);
            LevelData d = _level.Data;
            if (cell.x < 0 || cell.y < 0 || cell.x >= d.Width || cell.y >= d.Height) return false;
            return d.Depth[cell.x, cell.y] == PoolDepth.Dry && !d.IsSolid(cell.x, cell.y) && !_controller.IsSwimming && !_controller.IsUnderwater && _body.isGrounded;
        }

        static string Stuck(Trial t)
        {
            Vector3 p = _local.transform.position;
            Vector2Int c = CellOf(p);
            float surface = PoolWater.Current.SurfaceY;
            int index = Mathf.Min(_routeIndex, t.Route.Count - 1);
            return $"pés em ({p.x:F2}, {p.y:F2}, {p.z:F2}) ({p.y - surface:F2} da superfície), célula ({c.x},{c.y}), ponto {index + 1}/{t.Route.Count} a {Flat(t.Route[index] - p).magnitude:F2} m, " +
                   $"nadando={_controller.IsSwimming}, no chão={_body.isGrounded}, colisão={_body.collisionFlags}, debaixo d'água={_controller.IsUnderwater}, fôlego {_controller.Air01 * 100f:F0}%";
        }

        static void Record(Trial t, bool ok, string what)
        {
            LevelData d = _level.Data;
            string line = $"  {(ok ? "CHEGOU" : "PRESO ")} partida {t.Why} ({t.Start.x},{t.Start.y}) {d.Depth[t.Start.x, t.Start.y]}, " +
                          $"seco mais perto ({t.Goal.x},{t.Goal.y}) a {t.Route?.Count / 2 ?? -1} célula(s): {what}";
            if (ok) _seedOk++;
            else
            {
                _seedFailed++;
                SessionState.SetInt(FailedKey, SessionState.GetInt(FailedKey, 0) + 1);
            }
            if (ok) SessionState.SetFloat(WorstKey, Mathf.Max(SessionState.GetFloat(WorstKey, 0f), Now - _trialStart));
            SessionState.SetInt(TotalKey, SessionState.GetInt(TotalKey, 0) + 1);
            Append(line);
            NextTrial();
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

        static Vector2Int CellOf(Vector3 world)
        {
            Vector3 local = _level.transform.InverseTransformPoint(world);
            return new Vector2Int(Mathf.FloorToInt(local.x / LevelBuilder.Cell), Mathf.FloorToInt(local.z / LevelBuilder.Cell));
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
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
            File.AppendAllText(ResultFile, line + "\n");
        }

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
            Debug.Log($"[BackRooms] Maré alta seed {seed}: {_seedOk} tentativas chegaram ao seco, {_seedFailed} presas" + (problem != null ? $" ({problem})" : ""));

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
            PoolTide.ForceSeconds = -1.0;
            EntityDirector.Paused = false;
            UndertowDirector.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            SessionState.SetBool(ActiveKey, false);

            int total = SessionState.GetInt(TotalKey, 0), failed = SessionState.GetInt(FailedKey, 0), errors = SessionState.GetInt(ErrorsKey, 0);
            bool ok = failed == 0 && errors == 0 && total > 0;
            string report = $"seeds {string.Join(", ", Seeds)}, maré +{PoolTide.Amplitude:F2} m: {total} tentativas, {total - failed} chegaram de pé ao seco " +
                            $"(a mais longa em {SessionState.GetFloat(WorstKey, 0f):F1} s), {failed} presas, {errors} erro(s) no console; detalhes em Logs/MareAlta/mare_alta.txt";
            Append(report);
            Debug.Log("[BackRooms] Teste da maré alta " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
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
