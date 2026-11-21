using System.Collections.Generic;
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
    /// Teste em Play das regras do Undertow, o inimigo da água funda das Piscinas (seed 1998 por
    /// padrão, -seed N). Hospeda, põe o Undertow dormindo no covil e confere, em ordem, com o teclado
    /// virtual:
    ///
    /// 1. nadando submerso a duas células dele, ele não acorda;
    /// 2. um espirro (cair na água) o acorda, com o gemido, e ele vem;
    /// 3. nadando à tona longe (se a planta tiver água para isso), não acorda;
    /// 4. quem nada até a borda e sobe escapa: ele ronda a borda e volta, sem pegar;
    /// 5. quem fica boiando é pego e puxado para o fundo (NO SIGNAL, volta ao início);
    /// 6. na maré alta, o raso vizinho vira território: ele entra lá e pega quem nada ali;
    /// 7. por um espirro noutra água, vai pelos canos (some e surge lá, com aviso);
    /// 8. drenada a fase, arrasta-se pelo fundo devagar e ainda pega quem fica parado ali.
    /// O tempo todo: ele nunca está fora do território (fora dos canos).
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.UndertowTest.BatchRun (0 passou, 3
    /// falhou); linha "[BackRooms] Teste do Undertow PASSOU/FALHOU". Menu: BackRooms > Testar
    /// Undertow (Level 2). Dois editores: <see cref="BatchHost"/> + <see cref="BatchJoin"/> (o
    /// cliente pula na água; o host tem de ouvi-lo e pegá-lo; o cliente vê o Undertow perto de si
    /// na captura, é puxado para o fundo e vê os estados mudarem).
    /// </summary>
    public static class UndertowTest
    {
        const string ActiveKey = "BackRooms.UndertowTest.Active";
        const string BatchKey = "BackRooms.UndertowTest.Batch";
        const string ExitCodeKey = "BackRooms.UndertowTest.ExitCode";
        const string RoleKey = "BackRooms.UndertowTest.Role";
        const string SeedKey = "BackRooms.UndertowTest.Seed";
        const string SoloRole = "solo", HostRole = "host", JoinRole = "join";
        const double Timeout = 400.0;
        const double NetTimeout = 360.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

        static int _stage, _step;
        static double _began, _lastTry;
        static float _stageStart, _mark;
        static bool _finished;
        static PlayerNetwork _local;
        static PlayerController _controller;
        static Footsteps _steps;
        static LevelBuilder _level;
        static UndertowDirector _director;
        static UndertowBrain _creature;
        static UndertowNav _nav;
        static Keyboard _keyboard;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;

        static Vector2Int _lairCell, _quiet, _splash, _edge, _shallow, _far, _other, _drained, _drainedFrom;
        static WallSide _edgeSide;
        static int _edgeDist;
        static bool _hasFar, _hasOther;
        static int _region;
        static int _counter, _catches, _captures;
        static float _y0, _dist0;
        static Vector3 _point;
        static int _outside;
        static string _outsideWhere;
        static readonly HashSet<UndertowState> SeenStates = new HashSet<UndertowState>();
        static readonly List<string> Trail = new List<string>();

        [MenuItem("BackRooms/Testar Undertow (Level 2)")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false, SoloRole);
        }

        public static void BatchRun() => Run(true, SoloRole);
        public static void BatchHost() => Run(true, HostRole);
        public static void BatchJoin() => Run(true, JoinRole);

        static void Run(bool batch, string role)
        {
            if (!System.IO.File.Exists(Level2Setup.ScenePath)) Level2Setup.BuildScene();
            EditorSceneManager.OpenScene(Level2Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(RoleKey, role);
            SessionState.SetInt(SeedKey, SeedArgument(1998));
            EditorApplication.EnterPlaymode();
        }

        static string Role => SessionState.GetString(RoleKey, SoloRole);
        static int Seed => SessionState.GetInt(SeedKey, 1998);

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (SessionState.GetInt(ExitCodeKey, -1) >= 0)
            {
                EditorApplication.playModeStateChanged -= ExitAfterPlay;
                EditorApplication.playModeStateChanged += ExitAfterPlay;
                if (!EditorApplication.isPlayingOrWillChangePlaymode) ExitAfterPlay(PlayModeStateChange.EnteredEditMode);
            }
            if (!SessionState.GetBool(ActiveKey, false)) return;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (stackTrace != null && (stackTrace.Contains("UnityEditor.Search.") || stackTrace.Contains("Unity.AI."))) return;
            Errors.Add(message);
        }

        static float Now => Time.time;
        static float InStage => Now - _stageStart;

        static void Next(string note = null)
        {
            if (note != null) Notes.Add(note);
            _stage++;
            _step = 0;
            _stageStart = Now;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || _finished) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - _began > (Role == SoloRole ? Timeout : NetTimeout))
            {
                Finish(false, $"tempo esgotado na etapa {_stage}.{_step} ({Describe()})");
                return;
            }

            try
            {
                if (Role == JoinRole) TickJoin(session);
                else if (Role == HostRole) TickHost(session);
                else TickSolo(session);
            }
            catch (System.Exception e)
            {
                Finish(false, $"exceção na etapa {_stage}.{_step}: {e.Message}\n{e.StackTrace}");
            }
        }

        // ---------------------------------------------------------------- Sozinho

        static void TickSolo(GameSession session)
        {
            if (_creature != null) WatchTerritory();

            switch (_stage)
            {
                case 0:
                    UndertowDirector.Paused = true;
                    PoolTide.Paused = true;
                    session.fixedSeed = Seed;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                    if (!Ready(session)) break;
                    if (!SpawnAsleep()) return;
                    Next();
                    break;

                case 2:
                    if (InStage < 0.8f || _creature.Nav == null) break;
                    if (!Plan(out string why)) { Finish(false, why); return; }
                    Next($"covil {_lairCell} (região de {RegionSize()} células), submerso {_quiet}, espirro {_splash}, borda {_edge}→{_edgeSide}, raso {_shallow}" +
                         (_hasFar ? $", longe {_far}" : ", sem célula longe") + (_hasOther ? $", outra água {_other}" : ", sem outra água"));
                    break;

                case 3:
                    // 1. Submerso, a duas células: não acorda.
                    if (_step == 0)
                    {
                        Teleport(Floor(_quiet) + Vector3.up * 0.05f, AwayFrom(_quiet, _lairCell));
                        _counter = _creature.NoisesIgnored + _creature.NoisesHeard;
                        _mark = _steps.PlayedOn(StepSurface.Underwater);
                        _step = 1;
                        break;
                    }
                    Press(InStage % 3f < 1.5f ? Key.W : Key.S, Key.LeftCtrl);
                    if (_creature.State != UndertowState.Asleep) { Finish(false, $"nadando submerso a {Dist(_quiet, _lairCell)} células, ele acordou ({_creature.State}; ouviu {_creature.LastHeardLoudness:F2} a {Flat(_creature.LastHeardAt - _creature.Head).magnitude:F1} m)"); return; }
                    if (InStage < 6f) break;
                    Press();
                    int strokes = _steps.PlayedOn(StepSurface.Underwater) - (int)_mark;
                    if (strokes < 2) { Finish(false, $"nadando submerso 6 s, só {strokes} som(ns) de debaixo d'água: o teste não fez barulho"); return; }
                    Next($"submerso a {Dist(_quiet, _lairCell)} células: {strokes} braçadas, {_creature.NoisesIgnored + _creature.NoisesHeard - _counter} barulho(s) chegaram, dormindo");
                    break;

                case 4:
                    // 2. Um espirro a acorda (gemido) e ela vem.
                    if (_step == 0)
                    {
                        Press();
                        _counter = _steps.Splashes;
                        _mark = Audio().Groans;
                        Teleport(Center(_splash) + Vector3.up * (Surface() + 2.4f - Center(_splash).y), 0f);
                        _step = 1;
                        break;
                    }
                    if (_step == 1)
                    {
                        if (_steps.Splashes > _counter)
                        {
                            _point = _local.transform.position;
                            _dist0 = Flat(_creature.Head - _point).magnitude;
                            _stageStart = Now;
                            _step = 2;
                        }
                        else if (InStage > 4f) { Finish(false, "caiu na água e não espirrou"); return; }
                        break;
                    }
                    Press(Key.Space);
                    if (_step == 2)
                    {
                        if (_creature.State == UndertowState.Asleep) { if (InStage > 1f) { Finish(false, $"o espirro a {_dist0:F1} m não a acordou (ignorados {_creature.NoisesIgnored})"); return; } break; }
                        if (Audio().Groans <= (int)_mark) { Finish(false, "acordou sem o gemido"); return; }
                        _step = 3;
                        break;
                    }
                    if (Flat(_creature.Head - _point).magnitude < _dist0 - 3f)
                    {
                        Next($"espirro a {_dist0:F1} m: acordou com o gemido e veio ({_creature.State})");
                        Reset();
                        break;
                    }
                    if (InStage > 12f) { Finish(false, $"acordou mas não veio: {_creature.State}, a {Flat(_creature.Head - _point).magnitude:F1} m"); return; }
                    break;

                case 5:
                    // 3. À tona, longe: não acorda (só se a planta tem água para isso).
                    if (!_hasFar) { Next("sem célula a 4+ células do covil na mesma água: à tona longe não conferido"); break; }
                    if (InStage < 1f) break;
                    if (_step == 0)
                    {
                        Teleport(new Vector3(Center(_far).x, Surface() - 1.4f, Center(_far).z), AwayFrom(_far, _lairCell));
                        _mark = _steps.PlayedOn(StepSurface.Swim);
                        _stageStart = Now;
                        _step = 1;
                        break;
                    }
                    Press(Key.Space, InStage % 2f < 1f ? Key.W : Key.S);
                    if (_creature.State != UndertowState.Asleep) { Finish(false, $"à tona a {Dist(_far, _lairCell)} células, acordou (ouviu {_creature.LastHeardLoudness:F2})"); return; }
                    if (InStage < 6f) break;
                    Press();
                    Next($"à tona a {Dist(_far, _lairCell)} células: {_steps.PlayedOn(StepSurface.Swim) - (int)_mark} braçadas, dormindo");
                    Reset();
                    break;

                case 6:
                    // 4. Nada até a borda e sobe: ele ronda, não pega.
                    if (InStage < 1f) break;
                    if (_step == 0)
                    {
                        Vector3 c = Center(_edge);
                        Teleport(new Vector3(c.x, Surface() - 1.4f, c.z), Yaw(Dir(_edgeSide)));
                        _catches = _creature.Catches;
                        _counter = _controller.Climbs;
                        // Como quem pula na água: o barulho a acorda (com o gemido) e o jogador foge para a borda.
                        _creature.Hear(_local.transform.position, 1f, _local);
                        _stageStart = Now;
                        _step = 1;
                        break;
                    }
                    if (_step == 1)
                    {
                        Press(Key.Space, Key.W);
                        if (_controller.IsCaught || _creature.Catches > _catches) { Finish(false, $"nadando até a borda ({Dist(_edge, _lairCell)} células do covil), foi pego antes de subir"); return; }
                        if (_controller.Climbs > _counter && !_controller.IsClimbing && !_controller.IsSwimming)
                        {
                            Press();
                            _point = _local.transform.position;
                            _mark = Now;
                            _step = 2;
                            Notes.Add($"subiu na borda em {InStage:F1} s (ele: {_creature.State}, a {Flat(_creature.Head - _point).magnitude:F1} m)");
                        }
                        else if (InStage > 10f) { Finish(false, $"não conseguiu subir na borda em 10 s (nadando={_controller.IsSwimming}, pode subir={_controller.CanClimb})"); return; }
                        break;
                    }
                    if (_step == 2)
                    {
                        if (_creature.Catches > _catches || _controller.IsCaught) { Finish(false, "pegou quem saiu da água"); return; }
                        if (_creature.State == UndertowState.Asleep && !SeenStates.Contains(UndertowState.Circle)) { Finish(false, "ele não chegou a rondar a borda"); return; }
                        if (SeenStates.Contains(UndertowState.Circle) && (_creature.State == UndertowState.Return || _creature.State == UndertowState.Asleep))
                        {
                            Next($"subiu na borda: ele rondou e voltou ({Now - _mark:F1} s depois), sem pegar");
                            Reset();
                            break;
                        }
                        if (Now - _mark > _creature.circleSeconds + 14f) { Finish(false, $"depois de 14 s + a ronda, ainda {_creature.State} (viu {string.Join(",", SeenStates)})"); return; }
                    }
                    break;

                case 7:
                    // 5. Boiando parado: é pego e puxado para o fundo.
                    if (InStage < 1f) break;
                    if (_step == 0)
                    {
                        _catches = _creature.Catches;
                        _captures = _controller.Captures;
                        Vector3 c = Center(_quiet);
                        Teleport(new Vector3(c.x, Surface() - 1.4f, c.z), 0f);
                        _creature.Hear(_local.transform.position, 1f, _local);
                        _stageStart = Now;
                        _step = 1;
                        break;
                    }
                    if (_step == 1)
                    {
                        Press(Key.Space);
                        if (_controller.IsCaught)
                        {
                            Press();
                            _y0 = _local.transform.position.y;
                            _mark = Now;
                            _step = 2;
                        }
                        else if (InStage > 16f) { Finish(false, $"boiando, não foi pego em 16 s ({Describe()}; jogador em {_local.transform.position})"); return; }
                        break;
                    }
                    if (_step == 2)
                    {
                        if (Now - _mark < 0.7f) break;
                        float fell = _y0 - _local.transform.position.y;
                        // Até o fundo da piscina (ou 1 m, onde é mais fundo, como no poço).
                        float room = _y0 - Floor(CellOf(_local.transform.position)).y;
                        if (fell < Mathf.Min(1f, room * 0.85f)) { Finish(false, $"pego, desceu só {fell:F2} m em 0,7 s de {room:F2} m até o fundo (não foi puxado para o fundo)"); return; }
                        if (_creature.Catches <= _catches) { Finish(false, "o jogador foi pego, mas não por ele"); return; }
                        Notes.Add($"boiando: pego em {_mark - _stageStart:F1} s e puxado {fell:F2} m para baixo em 0,7 s (até o fundo: {room:F2} m)");
                        _step = 3;
                        break;
                    }
                    if (!_controller.IsReturning)
                    {
                        if (_creature.Catchable(_local, _local.transform.position)) { Finish(false, "voltou ao início dentro da água dele"); return; }
                        Next("depois de pego, voltou ao início");
                        Reset();
                    }
                    break;

                case 8:
                    // 6. Maré alta: o raso vizinho vira território, ele entra e pega quem nada ali.
                    if (_step == 0)
                    {
                        PoolTide.Paused = false;
                        PoolTide.ForceSeconds = PoolTide.FirstPeak + 1.0;
                        _step = 1;
                        break;
                    }
                    if (_step == 1)
                    {
                        if (PoolTide.Current == null || PoolTide.Current.Offset < PoolTide.Amplitude * 0.95f) { if (InStage > 5f) { Finish(false, "a maré não subiu com o relógio forçado"); return; } break; }
                        if (InStage < 1.5f) break;
                        if (!_creature.Nav.Territory[_shallow.x, _shallow.y]) { Finish(false, $"na maré alta o raso {_shallow} não virou território (coluna {_creature.Nav.Column(_shallow.x, _shallow.y):F2} m)"); return; }
                        _catches = _creature.Catches;
                        Vector3 c = Center(_shallow);
                        Teleport(new Vector3(c.x, Surface() - 1.4f, c.z), 0f);
                        _creature.Hear(_local.transform.position, 1f, _local);
                        _stageStart = Now;
                        _step = 2;
                        break;
                    }
                    Press(Key.Space);
                    if (_creature.Catches > _catches)
                    {
                        Vector2Int at = CellOf(_creature.transform.position);
                        Press();
                        PoolTide.ForceSeconds = -1.0;
                        PoolTide.Paused = true;
                        if (_level.Data.Depth[at.x, at.y] != PoolDepth.Shallow && Dist2(at, _shallow) > 1)
                        { Finish(false, $"na maré alta pegou, mas de longe do raso ({at})"); return; }
                        Next($"maré alta: entrou no raso {_shallow} (estava em {at} ao pegar) e pegou");
                        Reset();
                        break;
                    }
                    if (InStage > 18f) { Finish(false, $"maré alta: não pegou quem nadava no raso em 18 s ({_creature.State}, cabeça a {Vector3.Distance(_creature.Head, _local.transform.position + Vector3.up * 0.9f):F1} m)"); return; }
                    break;

                case 9:
                    // 7. Espirro noutra água: vai pelos canos.
                    if (!_hasOther) { Next("só uma água nesta planta: os canos não conferidos"); break; }
                    if (InStage < 1.5f || _controller.IsReturning) break;
                    if (_step == 0)
                    {
                        _counter = _creature.Travels;
                        _mark = Audio().Groans;
                        Teleport(Center(_other) + Vector3.up * (Surface() + 2.4f - Center(_other).y), 0f);
                        _stageStart = Now;
                        _step = 1;
                        break;
                    }
                    Press(Key.Space);
                    if (_step == 1)
                    {
                        if (_creature.State == UndertowState.Travel && Body() != null && !Body().Visible) { _step = 2; break; }
                        if (InStage > 8f) { Finish(false, $"espirro noutra água: não foi pelos canos ({_creature.State}, viagens {_creature.Travels - _counter}, ouviu {_creature.LastHeardLoudness:F2})"); return; }
                        break;
                    }
                    if (_step == 2)
                    {
                        if (_creature.State == UndertowState.Waking || _creature.State == UndertowState.Investigate || _creature.State == UndertowState.Hunt)
                        {
                            int regionOther = _creature.Nav.Region[_other.x, _other.y];
                            if (_creature.Nav.RegionAt(_creature.transform.position) != regionOther) { Finish(false, "saiu do cano noutra água que não a do espirro"); return; }
                            if (Audio().Groans <= (int)_mark) { Finish(false, "surgiu do cano sem o gemido"); return; }
                            Next($"espirro noutra água: sumiu no cano e surgiu lá, com o gemido, em {InStage:F1} s");
                            Reset();
                            break;
                        }
                        if (InStage > 25f) { Finish(false, $"nos canos há {InStage:F0} s"); return; }
                    }
                    break;

                case 10:
                    // 8. Drenada: arrasta-se devagar e pega quem fica parado no fundo.
                    if (_step == 0)
                    {
                        SwitchObjective objective = SwitchObjective.Instance;
                        if (objective == null) { Finish(false, "a fase não tem o objetivo das válvulas"); return; }
                        objective.CompleteForTest();
                        PoolTide.Paused = false;
                        PoolTide.ForceSeconds = objective.CompletedAt + PoolTide.DrainSeconds + 2.0;
                        _step = 1;
                        break;
                    }
                    if (_step == 1)
                    {
                        if (PoolTide.Current == null || PoolTide.Current.Offset > -PoolTide.DrainDepth * 0.95f) { if (InStage > 5f) { Finish(false, "não drenou"); return; } break; }
                        if (InStage < 1.5f) break;
                        if (_controller.IsReturning) break;
                        if (!FindDrained()) { Finish(false, "drenada, não há duas células fundas vizinhas para o teste"); return; }
                        _catches = _creature.Catches;
                        Teleport(Floor(_drained) + Vector3.up * 0.05f, 0f);
                        _creature.Place(Floor(_drainedFrom) + Vector3.up * 0.3f, Quaternion.identity);
                        _creature.Hear(_local.transform.position, 1f, _local);
                        _point = _creature.transform.position;
                        _stageStart = Now;
                        _step = 2;
                        break;
                    }
                    if (_step == 2)
                    {
                        if (InStage < 3f) break;
                        float speed = Flat(_creature.transform.position - _point).magnitude / InStage;
                        if (!_creature.Crawling) { Finish(false, "drenada, no fundo seco ele não se arrasta"); return; }
                        if (speed > _creature.crawlSpeed * 1.25f) { Finish(false, $"drenada, andou a {speed:F2} m/s (arrastando: {_creature.crawlSpeed})"); return; }
                        if (speed < 0.2f) { Finish(false, $"drenada, quase parado ({speed:F2} m/s, {_creature.State})"); return; }
                        Notes.Add($"drenada: arrasta-se a {speed:F2} m/s");
                        _step = 3;
                        break;
                    }
                    if (_creature.Catches > _catches)
                    {
                        Next($"drenada: pegou quem ficou parado no fundo em {InStage:F1} s");
                        break;
                    }
                    if (InStage > 20f) { Finish(false, $"drenada, não pegou quem ficou parado no fundo ({_creature.State}, a {Flat(_creature.Head - _local.transform.position).magnitude:F1} m)"); return; }
                    break;

                case 11:
                    if (_outside > 0) { Finish(false, $"esteve fora do território {_outside} vez(es), a primeira {_outsideWhere}"); return; }
                    Finish(true, "nunca saiu da água");
                    break;
            }
        }

        // ---------------------------------------------------------------- Dois editores

        static void TickHost(GameSession session)
        {
            if (_creature != null) WatchTerritory();
            switch (_stage)
            {
                case 0:
                    UndertowDirector.Paused = true;
                    PoolTide.Paused = true;
                    session.fixedSeed = Seed;
                    session.Host(localOnly: false);
                    Next();
                    break;
                case 1:
                    if (!Ready(session) || CountInGame() < 2) break;
                    if (InStage < 2f) break;
                    if (!SpawnAsleep()) return;
                    _catches = 0;
                    Next("cliente na fase: Undertow dormindo no covil");
                    break;
                case 2:
                    // O cliente pula na água perto do covil: o host tem de ouvir e pegar.
                    if (_creature.Catches > 0)
                    {
                        Next($"pegou o cliente (ouviu {_creature.LastHeardLoudness:F2}; {_creature.NoisesHeard} barulho(s))");
                        break;
                    }
                    break;
                case 3:
                    if (InStage < 6f) break; // dá tempo ao cliente de conferir
                    if (_outside > 0) { Finish(false, $"esteve fora do território {_outside} vez(es)"); return; }
                    Finish(true, "host ouviu e pegou o cliente");
                    break;
            }
        }

        static void TickJoin(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    PoolTide.Paused = true;
                    if (session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null) { Next(); break; }
                    if (session.Phase == SessionPhase.Menu && EditorApplication.timeSinceStartup - _lastTry > 4.0)
                    {
                        _lastTry = EditorApplication.timeSinceStartup;
                        session.Join("127.0.0.1");
                    }
                    break;
                case 1:
                {
                    if (!Ready(session)) break;
                    UndertowBrain creature = UndertowBrain.Instance;
                    if (creature == null || !creature.IsSpawned) break;
                    if (_creature == null)
                    {
                        _creature = creature;
                        _creature.StateChanged += s => SeenStates.Add(s);
                    }
                    if (InStage < 2f) break;
                    // Mesma conta do host: o covil e uma célula da mesma água, a 1 ou 2 dele.
                    _nav = new UndertowNav(_level);
                    _nav.Refresh(Surface());
                    if (!UndertowDirector.FindLair(_level, out Vector3 lair)) { Finish(false, "sem covil no cliente"); return; }
                    _lairCell = CellOf(lair);
                    int[,] dist = _nav.NewMap();
                    _nav.Distances(_lairCell.x, _lairCell.y, dist);
                    if (!Pick(dist, 1, 2, out _splash, deepOnly: true)) { Finish(false, "sem água perto do covil"); return; }
                    Notes.Add($"o Undertow apareceu no cliente a {Flat(creature.transform.position - lair).magnitude:F1} m do covil calculado aqui");
                    Teleport(Center(_splash) + Vector3.up * (Surface() + 2.4f - Center(_splash).y), 0f);
                    _counter = _steps.Splashes;
                    Next();
                    break;
                }
                case 2:
                    Press(Key.Space);
                    if (_controller.IsCaught)
                    {
                        Press();
                        float d = Vector3.Distance(_creature.Head, _local.transform.position + Vector3.up * 0.9f);
                        if (d > 4f) { Finish(false, $"pego, mas aqui o Undertow está a {d:F1} m (a posição não bate)"); return; }
                        _y0 = _local.transform.position.y;
                        _mark = Now;
                        Notes.Add($"pego com o Undertow a {d:F1} m (espirrou: {_steps.Splashes > _counter})");
                        Next();
                    }
                    else if (InStage > 40f) { Finish(false, $"não foi pego em 40 s (estado aqui {_creature.State}, a {Vector3.Distance(_creature.Head, _local.transform.position):F1} m)"); return; }
                    break;
                case 3:
                    if (Now - _mark < 0.7f) break;
                    float fell = _y0 - _local.transform.position.y;
                    float room = _y0 - Floor(CellOf(_local.transform.position)).y;
                    if (fell < Mathf.Min(1f, room * 0.85f)) { Finish(false, $"pego, desceu só {fell:F2} m de {room:F2} m até o fundo (não foi puxado para o fundo)"); return; }
                    if (!SeenStates.Contains(UndertowState.Waking) || !SeenStates.Contains(UndertowState.Feed))
                    { Finish(false, $"o cliente não viu os estados (viu {string.Join(",", SeenStates)})"); return; }
                    Finish(true, $"cliente: viu o Undertow acordar, vir e pegar; puxado {fell:F1} m para o fundo (estados {string.Join(",", SeenStates)})");
                    break;
            }
        }

        // ---------------------------------------------------------------- Montagem

        static bool Ready(GameSession session)
        {
            if (session.Phase != SessionPhase.Playing || !session.LevelReady) return false;
            _local = PlayerNetwork.Local;
            _level = session.level;
            if (_local == null || _level == null || _level.Data == null || PoolWater.Current == null) return false;
            if (_controller == null)
            {
                _controller = _local.controller;
                _steps = _local.footsteps;
                _controller.airSeconds = 100000f;
                _controller.RefillAir();
                _backgroundBefore = InputSystem.settings.backgroundBehavior;
                _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                _keyboard = InputSystem.AddDevice<Keyboard>();
            }
            return true;
        }

        static bool SpawnAsleep()
        {
            _director = UndertowDirector.Instance;
            if (_director == null) { Finish(false, "a cena do Level 2 não tem o diretor do Undertow"); return false; }
            if (_director.prefab == null || _director.prefab.GetComponent<UndertowBrain>() == null) { Finish(false, "o prefab do diretor não é o Undertow"); return false; }
            if (!UndertowDirector.FindLair(_level, out Vector3 lair)) { Finish(false, "a planta não tem água funda"); return false; }
            _creature = _director.SpawnAt(lair, true);
            if (_creature == null) { Finish(false, "o diretor não pôs o Undertow"); return false; }
            _creature.StateChanged += s =>
            {
                SeenStates.Add(s);
                Trail.Add($"{Now:F1}s {s}");
                if (Trail.Count > 14) Trail.RemoveAt(0);
            };
            _lairCell = CellOf(lair);
            return true;
        }

        // Escolhe as células de cada etapa na água do covil (e uma noutra água).
        static bool Plan(out string why)
        {
            why = null;
            _nav = _creature.Nav;
            LevelData d = _level.Data;
            _region = _nav.Region[_lairCell.x, _lairCell.y];
            int[,] dist = _nav.NewMap();
            _nav.Distances(_lairCell.x, _lairCell.y, dist);

            if (!Pick(dist, 2, 3, out _quiet, deepOnly: true) && !Pick(dist, 2, 9, out _quiet, deepOnly: true)) { why = "a água do covil não tem célula funda a 2+ células dele"; return false; }
            if (!Pick(dist, 2, 5, out _splash, deepOnly: false)) _splash = _quiet;
            _hasFar = Pick(dist, 4, 99, out _far, deepOnly: false);

            // A borda: célula funda a 2–4 do covil com seco ou raso vizinho, sem escada na divisa.
            bool edge = false;
            for (int want = 2; want <= 4 && !edge; want++)
                for (int z = 0; z < d.Height && !edge; z++)
                    for (int x = 0; x < d.Width && !edge; x++)
                    {
                        if (dist[x, z] != want || d.Depth[x, z] != PoolDepth.Deep) continue;
                        foreach (WallSide side in Sides)
                        {
                            if (!d.IsOpen(x, z, side) || d.StairAt(x, z, side, out _) || !d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                            if (d.IsSolid(nx, nz) || d.Depth[nx, nz] == PoolDepth.Deep || d.Depth[nx, nz] == PoolDepth.Well) continue;
                            _edge = new Vector2Int(x, z);
                            _edgeSide = side;
                            _edgeDist = want;
                            edge = true;
                            break;
                        }
                    }
            if (!edge) { why = "a água do covil não tem borda (seco ou raso) a 2–4 células dele"; return false; }

            // O raso vizinho da água do covil (vira território na maré alta).
            bool shallow = false;
            for (int z = 0; z < d.Height && !shallow; z++)
                for (int x = 0; x < d.Width && !shallow; x++)
                {
                    if (dist[x, z] < 0) continue;
                    foreach (WallSide side in Sides)
                    {
                        if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                        if (d.IsSolid(nx, nz) || d.Depth[nx, nz] != PoolDepth.Shallow) continue;
                        _shallow = new Vector2Int(nx, nz);
                        shallow = true;
                        break;
                    }
                }
            if (!shallow) { why = "a água do covil não encosta em raso"; return false; }

            // Outra água (para os canos): uma célula funda de outra região, a mais longe do covil.
            _hasOther = false;
            float best = -1f;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (!_nav.Territory[x, z] || _nav.Region[x, z] == _region || d.Depth[x, z] != PoolDepth.Deep) continue;
                    float far = Flat(Center(new Vector2Int(x, z)) - Center(_lairCell)).magnitude;
                    if (far <= best) continue;
                    best = far;
                    _other = new Vector2Int(x, z);
                    _hasOther = true;
                }
            return true;
        }

        static bool Pick(int[,] dist, int min, int max, out Vector2Int cell, bool deepOnly)
        {
            LevelData d = _level.Data;
            for (int want = min; want <= max; want++)
                for (int z = 0; z < d.Height; z++)
                    for (int x = 0; x < d.Width; x++)
                    {
                        if (dist[x, z] != want) continue;
                        if (deepOnly && d.Depth[x, z] != PoolDepth.Deep) continue;
                        cell = new Vector2Int(x, z);
                        return true;
                    }
            cell = new Vector2Int(-1, -1);
            return false;
        }

        // Drenada: duas células fundas (não poço) vizinhas e abertas, as duas na água do covil se der.
        static bool FindDrained()
        {
            LevelData d = _level.Data;
            for (int pass = 0; pass < 2; pass++)
                for (int z = 0; z < d.Height; z++)
                    for (int x = 0; x < d.Width; x++)
                    {
                        if (d.IsSolid(x, z) || d.Depth[x, z] != PoolDepth.Deep) continue;
                        if (pass == 0 && _nav.Region[x, z] != _nav.RegionAt(_creature.transform.position)) continue;
                        foreach (WallSide side in Sides)
                        {
                            if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                            if (d.IsSolid(nx, nz) || d.Depth[nx, nz] != PoolDepth.Deep) continue;
                            _drained = new Vector2Int(x, z);
                            _drainedFrom = new Vector2Int(nx, nz);
                            return true;
                        }
                    }
            return false;
        }

        // Volta tudo ao começo de uma etapa: ele dormindo no covil, o jogador no seco da sala inicial.
        static void Reset()
        {
            Press();
            _creature.SleepAt(Center(_lairCell));
            if (_local != null) _local.controller.Teleport(StartPoint(), 0f);
            SeenStates.Clear();
        }

        static void WatchTerritory()
        {
            if (!_creature.IsServer || _creature.Nav == null || _creature.State == UndertowState.Travel) return;
            Vector2Int at = CellOf(_creature.transform.position);
            UndertowNav nav = _creature.Nav;
            bool inside = at.x >= 0 && at.y >= 0 && at.x < nav.Data.Width && at.y < nav.Data.Height && nav.Territory[at.x, at.y];
            if (inside) return;
            // Na maré descendo, o raso sai do território debaixo dele: tem um instante para sair.
            if (at.x >= 0 && at.y >= 0 && at.x < nav.Data.Width && at.y < nav.Data.Height &&
                nav.Data.Depth[at.x, at.y] == PoolDepth.Shallow && nav.Column(at.x, at.y) > 1.0f) return;
            if (_outside == 0) _outsideWhere = $"na célula {at} (etapa {_stage}.{_step}, {_creature.State})";
            _outside++;
        }

        // ---------------------------------------------------------------- Ajudas

        // O ponto de volta (a sala inicial, no seco).
        static Vector3 StartPoint()
        {
            if (_controller.RespawnPoint != null) return _controller.RespawnPoint();
            LevelData d = _level.Data;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    if (d.InRoom(x, z)) return _level.CellCenter(x, z) + Vector3.up * 0.05f;
            return _level.transform.position + Vector3.up * 0.3f;
        }

        static UndertowAudio Audio() => _creature.GetComponent<UndertowAudio>();
        static UndertowBody Body() => _creature.GetComponent<UndertowBody>();
        static float Surface() => PoolWater.Current != null ? PoolWater.Current.SurfaceY : LevelData.WaterLevel;
        static Vector3 Center(Vector2Int c) => _level.CellCenter(c.x, c.y);
        static Vector3 Floor(Vector2Int c) => _level.CellCenter(c.x, c.y);

        static void Teleport(Vector3 feet, float yaw)
        {
            _controller.Teleport(feet, yaw);
            _controller.SetPitch(0f);
        }

        static float AwayFrom(Vector2Int from, Vector2Int other) => Yaw(Center(from) - Center(other));
        static float Yaw(Vector3 v) => v.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        static Vector3 Dir(WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return Vector3.back;
                case WallSide.North: return Vector3.forward;
                case WallSide.West: return Vector3.left;
                default: return Vector3.right;
            }
        }

        static int Dist(Vector2Int a, Vector2Int b)
        {
            int[,] dist = _nav.NewMap();
            _nav.Distances(b.x, b.y, dist);
            return dist[a.x, a.y];
        }

        static int Dist2(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        static int RegionSize()
        {
            int n = 0;
            for (int z = 0; z < _nav.Data.Height; z++)
                for (int x = 0; x < _nav.Data.Width; x++)
                    if (_nav.Region[x, z] == _region) n++;
            return n;
        }

        static Vector2Int CellOf(Vector3 world)
        {
            Vector3 local = _level.transform.InverseTransformPoint(world);
            return new Vector2Int(Mathf.FloorToInt(local.x / LevelBuilder.Cell), Mathf.FloorToInt(local.z / LevelBuilder.Cell));
        }

        static int CountInGame()
        {
            int n = 0;
            foreach (PlayerNetwork p in PlayerNetwork.All)
                if (p != null && p.IsSpawned && p.InGame) n++;
            return n;
        }

        static void Press(params Key[] keys)
        {
            if (_keyboard != null) InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        static string Describe()
        {
            if (_creature == null) return "sem o Undertow";
            return $"Undertow {_creature.State}, cabeça a {(_local != null ? Vector3.Distance(_creature.Head, _local.transform.position + Vector3.up * 0.9f) : -1f):F1} m do jogador" +
                   $"; ouviu {_creature.NoisesHeard}, ignorou {_creature.NoisesIgnored}, último {_creature.LastHeardLoudness:F2} em {_creature.LastHeardAt}" +
                   $"; ele em {_creature.transform.position}, meta {_creature.Goal}; parada: {_creature.LastStop}; estados: {string.Join(", ", Trail)}";
        }

        static int SeedArgument(int fallback)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-seed" && int.TryParse(args[i + 1], out int seed)) return seed;
            return fallback;
        }

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;
            UndertowDirector.Paused = false;
            PoolTide.Paused = false;
            PoolTide.ForceSeconds = -1.0;
            Press();
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
                InputSystem.settings.backgroundBehavior = _backgroundBefore;
                InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBefore;
            }
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }
            string who = Role == SoloRole ? "" : $" ({Role})";
            Debug.Log($"[BackRooms] Teste do Undertow{who} " + (ok ? "PASSOU" : "FALHOU") + ": " + report +
                      (Notes.Count > 0 ? "\n  - " + string.Join("\n  - ", Notes) : ""));

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
