using System.Collections.Generic;
using BackRooms.Entities;
using BackRooms.Net;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Teste em Play das regras do Zelador no Level 3 (seed 1998). A onda de luzes fica parada e o
    /// teste apaga à mão um trecho reto de corredor de cinco células (c0 a c3 escuras, c4 acesa):
    ///
    /// 1. posto em c0 e chamado para o jogador parado em c4 (acesa), anda pelo escuro até a beira e
    ///    para: nunca pisa numa célula acesa;
    /// 2. na beira, com o jogador na luz a um passo dele, não pega;
    /// 3. com o jogador no escuro (c3) na frente dele, pega;
    /// 4. a luz volta sobre ele: some (afunda no chão);
    /// 5. com o trecho apagado de novo, aparece sozinho no escuro, longe do jogador;
    /// 6. o facho da lanterna o faz hesitar;
    /// 7. o alarme de incêndio: o andar escurece (luz de emergência nas rotas, abrigos acesos), toda
    ///    rota de um acionador até a escada tem no máximo dois escuros seguidos, e o jogador vai do
    ///    acionador 0 à escada (a 4 m/s, pela rota) com o Zelador solto sem ser pego.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.JanitorTest.BatchRun (0 passou, 3
    /// falhou); linha "[BackRooms] Teste do Zelador". Dois editores: <see cref="BatchHost"/> (o mesmo,
    /// esperando o cliente, que fica na cabine) + <see cref="BatchJoin"/> (o cliente vê o Zelador
    /// aparecer, andar, sumir, o host ser pego e o andar escurecer no alarme).
    /// </summary>
    public static class JanitorTest
    {
        const string ActiveKey = "BackRooms.JanitorTest.Active";
        const string BatchKey = "BackRooms.JanitorTest.Batch";
        const string ExitCodeKey = "BackRooms.JanitorTest.ExitCode";
        const string RoleKey = "BackRooms.JanitorTest.Role";
        const string SoloRole = "solo", HostRole = "host", JoinRole = "join";
        const double Timeout = 200.0;
        const double NetTimeout = 360.0;
        const float RunSpeed = 4f;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage;
        static double _began, _lastTry;
        static float _stageStart;
        static bool _finished;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static JanitorDirector _director;
        static JanitorBrain _janitor;
        static readonly Vector2Int[] _run = new Vector2Int[5];
        static Vector3 _dir, _start;
        static int _catches, _vanishes, _hesitations, _sweeps;
        static float _farthest;
        static List<Vector2Int> _route;
        static float _routeDistance;
        static float _closest;
        static bool _wasPresent, _relit, _manifested;
        // A onda que persegue e o interruptor.
        static int _waveRun = -1, _blinks, _emerges, _flips, _clacks, _noises, _heard, _requests;
        static readonly List<Vector2Int> _waveCells = new List<Vector2Int>();
        static bool _lastLit, _pressed, _attendantAsked;
        static string _prompt;
        static AttendantBrain _attendant;

        // Cliente
        static bool _sawPresent, _sawShown, _sawVanish, _sawHostCaught, _sawBlackout, _sawWarning, _sawClack, _sawSameSwitches;
        static Vector3 _seenAt;
        static float _feedSeenAt;
        static bool _hostKnown;

        [MenuItem("BackRooms/Testar Zelador (Level 3)")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false);
        }

        public static void BatchRun() { Run(true, SoloRole); }
        public static void BatchHost() { Run(true, HostRole); }
        public static void BatchJoin() { Run(true, JoinRole); }

        static void Run(bool batch, string role = SoloRole)
        {
            EditorSceneManager.OpenScene(Level3Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(RoleKey, role);
            EditorApplication.EnterPlaymode();
        }

        static string Role => SessionState.GetString(RoleKey, SoloRole);

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

        /// <summary>Passadas de vassoura do Zelador até agora (-1 sem o <see cref="JanitorAudio"/>).</summary>
        static int Sweeps => _janitor != null && _janitor.TryGetComponent(out JanitorAudio audio) ? audio.Sweeps : -1;

        static void Goto(int stage, string note = null)
        {
            if (note != null) Notes.Add(note);
            _stage = stage;
            _stageStart = Now;
        }

        static void Next(string note = null)
        {
            if (note != null) Notes.Add(note);
            _stage++;
            _stageStart = Now;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - _began > (Role == SoloRole ? Timeout : NetTimeout))
            {
                Finish(false, $"tempo esgotado na etapa {_stage}" + (Role == JoinRole ? ClientReport() : State()));
                return;
            }

            if (Role == JoinRole)
            {
                TickJoin(session);
                return;
            }

            // Em toda etapa com ele no mapa: nunca numa célula acesa.
            if (_janitor != null && _janitor.IsSpawned && _janitor.Present && !JanitorDark.IsDarkAt(_janitor.transform.position) && _stage < 9)
            {
                // A luz acabou de voltar sobre ele (etapa 5): some no mesmo quadro de decisão; dá um respiro.
                if (_stage != 5 && _stage != 8) { Finish(false, $"o Zelador está numa célula acesa na etapa {_stage}{State()}"); return; }
            }

            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    JanitorBrain.RunWhilePaused = true;
                    JanitorBrain.HoldEmerge = true;
                    OfficeLightWave.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                {
                    if (session.Phase != SessionPhase.Playing || !session.LevelReady) break;
                    if (Role == HostRole && CountInGame() < 2) break;
                    _local = PlayerNetwork.Local;
                    _level = session.level;
                    _director = JanitorDirector.Instance;
                    if (_local == null || _level == null || _level.Data == null || InStage < 1.5f) break;
                    if (_director == null) { Finish(false, "a cena do Level 3 não tem o diretor do Zelador"); return; }
                    if (_director.janitorPrefab == null || _director.janitorPrefab.GetComponent<JanitorBrain>() == null) { Finish(false, "o diretor do Zelador não tem o prefab dele"); return; }
                    if (EntityDirector.Instance != null && EntityDirector.Instance.entityPrefab != null &&
                        EntityDirector.Instance.entityPrefab.GetComponent<EntityBrain>()?.GetType() == typeof(EntityBrain))
                        Notes.Add("aviso: a entidade do Level 0 ainda está no EntityDirector do Level 3");
                    _janitor = _director.Janitor;
                    if (_janitor == null || !_janitor.IsSpawned) break;
                    if (_janitor.Present) { Finish(false, "o Zelador nasceu à vista, sem esperar o escuro"); return; }
                    if (!JanitorDark.Bind(_level)) { Finish(false, "sem mapa do escuro"); return; }
                    _start = _local.transform.position;

                    if (!PickRun(out string why)) { Finish(false, why); return; }
                    Darken(4, true);
                    _local.controller.Teleport(CellCenter(_run[4]) - _dir * (LevelBuilder.Cell * 0.5f - 0.45f), Yaw(-_dir));
                    _janitor.ManifestAt(CellCenter(_run[0]), _dir);
                    _janitor.Alert(_local.transform.position);
                    _catches = _janitor.Catches;
                    _sweeps = Sweeps;
                    _farthest = 0f;
                    Next($"trecho escuro de 4 células e o jogador na 5ª, acesa, a {Vector3.Distance(CellCenter(_run[0]), _local.transform.position):F1} m");
                    break;
                }

                case 2:
                {
                    // 1. Anda pelo escuro até a beira da luz e para.
                    float along = Vector3.Dot(Flat(_janitor.transform.position - CellCenter(_run[0])), _dir);
                    _farthest = Mathf.Max(_farthest, along);
                    if (!_janitor.Present) { Finish(false, $"sumiu andando no escuro ({_janitor.LastVanish})"); return; }
                    if (_janitor.Catches != _catches) { Finish(false, "pegou o jogador que estava na luz"); return; }
                    if (InStage > 9f)
                    {
                        float gap = Flat(_janitor.transform.position - _local.transform.position).magnitude;
                        if (_farthest < 9f) { Finish(false, $"andou só {_farthest:F1} m pelo escuro em 9 s (estado {_janitor.State})"); return; }
                        // A vassoura toca uma passada a cada dois passos: andando, tem de ter tocado.
                        int swept = Sweeps - _sweeps;
                        if (swept < 3) { Finish(false, $"andou {_farthest:F1} m pelo escuro e a vassoura tocou só {swept} vezes"); return; }
                        Next($"andou {_farthest:F1} m pelo escuro (a vassoura passou {swept} vezes) e parou na beira, a {gap:F1} m do jogador na luz");
                        // Os dois colados na divisa: ele 0,5 m dentro do escuro, o jogador 0,4 m dentro da luz.
                        Vector3 border = CellCenter(_run[3]) + _dir * (LevelBuilder.Cell * 0.5f);
                        _janitor.ManifestAt(border - _dir * 0.5f, _dir);
                        _closest = float.MaxValue;
                        _local.controller.Teleport(border + _dir * 0.4f, Yaw(-_dir));
                        // Chamado para o jogador (como quem ouviu algo ali): vai até a beira do escuro e fica.
                        _janitor.Alert(border + _dir * 0.4f);
                    }
                    break;
                }

                case 3:
                    // 2. Na divisa, o jogador na luz a menos de 1 m, na frente dele: não pega.
                    if (_janitor.Catches != _catches) { Finish(false, "pegou o jogador que estava na luz ao lado"); return; }
                    _closest = Mathf.Min(_closest, Flat(_janitor.transform.position - _local.transform.position).magnitude);
                    if (InStage > 4f)
                    {
                        float gap = _closest;
                        if (gap > _janitor.catchRadius) { Finish(false, $"o teste não pôs o jogador ao alcance dele (o mais perto foi {gap:F2} m){State()}"); return; }
                        _janitor.ManifestAt(CellCenter(_run[1]), _dir);
                        _local.controller.Teleport(CellCenter(_run[3]), Yaw(-_dir));
                        Next($"3 s na luz a {gap:F2} m dele, na frente, sem ser pego");
                    }
                    break;

                case 4:
                    // 3. No escuro, na frente dele: pega.
                    if (_janitor.Catches != _catches)
                    {
                        // Deixa ele "comer" um instante (o estado vai pela rede: o cliente confere a captura).
                        Next($"no escuro a 8 m, pegou em {InStage:F1} s");
                        break;
                    }
                    if (InStage > 8f) Finish(false, $"o jogador no escuro na frente dele não foi pego em 8 s (estado {_janitor.State}){State()}");
                    break;

                case 5:
                    // 4. A luz volta sobre ele: some.
                    if (InStage < 1.5f) break;
                    if (!_manifested)
                    {
                        _manifested = true;
                        _janitor.ManifestAt(CellCenter(_run[1]), _dir);
                        _vanishes = _janitor.Vanishes;
                        break;
                    }
                    if (InStage < 2.2f) break;
                    if (!_relit) { _relit = true; Darken(4, false); break; }
                    if (_janitor.Vanishes > _vanishes && !_janitor.Present)
                    {
                        Next($"a luz voltou e ele sumiu ({_janitor.LastVanish}) em {InStage - 2.2f:F2} s");
                        Darken(4, true);
                        _local.controller.Teleport(_start, 0f);
                        JanitorBrain.HoldEmerge = false;
                        _hesitations = _janitor.Emerges;
                        break;
                    }
                    if (InStage > 4f) Finish(false, "a luz voltou sobre ele e ele continuou no mapa");
                    break;

                case 6:
                {
                    // 5. Aparece sozinho no escuro, longe do jogador.
                    if (_janitor.Emerges > _hesitations && _janitor.Present)
                    {
                        float straight = Flat(_janitor.transform.position - _local.transform.position).magnitude;
                        if (!JanitorDark.IsDarkAt(_janitor.transform.position)) { Finish(false, "apareceu numa célula acesa"); return; }
                        // Com o jogador na luz (aqui, na cabine) ele espreita: aparece a lurkMinCells ou mais (a pé; 3/4 disso em linha reta).
                        if (straight < LevelBuilder.Cell * _janitor.lurkMinCells * 0.75f - 0.1f) { Finish(false, $"apareceu a {straight:F1} m do jogador"); return; }
                        Next($"apareceu sozinho no escuro a {straight:F1} m do jogador em {InStage:F1} s");
                        JanitorBrain.HoldEmerge = true;
                        _janitor.ManifestAt(CellCenter(_run[0]), _dir);
                        _local.controller.Teleport(CellCenter(_run[3]), Yaw(-_dir));
                        _local.flashlight.infiniteBattery = true;
                        _local.flashlight.SetOn(true);
                        _hesitations = _janitor.Hesitations;
                        break;
                    }
                    if (InStage > 14f) Finish(false, $"não apareceu sozinho em 14 s ({JanitorDark.DarkCells()} células escuras){State()}");
                    break;
                }

                case 7:
                    // 6. A lanterna nele: hesita.
                    Face(_janitor.transform.position);
                    if (_janitor.Hesitations > _hesitations)
                    {
                        _local.flashlight.SetOn(false);
                        _local.controller.Teleport(_start, 0f);
                        Darken(4, false);
                        Goto(20, $"o facho da lanterna o fez hesitar em {InStage:F2} s");
                        break;
                    }
                    if (InStage > 3f) Finish(false, $"o facho da lanterna não o fez hesitar{State()}");
                    break;

                case 8:
                {
                    // 7. O alarme: o andar escurece, com ilhas de emergência nas rotas até a escada.
                    if (InStage < 1f) break;
                    if (!CheckBlackout(out string report)) { Finish(false, report); return; }
                    JanitorBrain.HoldEmerge = true;
                    StandAtSwitch(_waveRun);
                    _flips = _janitor.SwitchFlips;
                    _requests = _janitor.FlipRequests;
                    _pressed = false;
                    Goto(26, report);
                    break;
                }

                case 26:
                {
                    // Com o alarme, o interruptor não funciona: a fuga final é no escuro.
                    AimAt(LightSwitches.Instance.PositionOf(_waveRun));
                    if (InStage < 0.4f) break;
                    if (!_pressed)
                    {
                        if (LightSwitches.Prompt == null || !LightSwitches.Prompt.Contains("NO POWER")) { Finish(false, $"no alarme, a mira no interruptor mostra \"{LightSwitches.Prompt}\""); return; }
                        _pressed = true;
                        LightSwitches.PressForTest = true;
                        break;
                    }
                    if (InStage < 1f) break;
                    if (_janitor.SwitchFlips != _flips) { Finish(false, "o interruptor religou com o alarme disparado"); return; }
                    if (_janitor.FlipRequests == _requests) { if (InStage > 3f) Finish(false, "o pedido do interruptor no alarme não chegou ao host"); break; }
                    if (!_janitor.LastSwitchRefusal.Contains("alarme")) { Finish(false, $"no alarme o interruptor recusou por outro motivo ({_janitor.LastSwitchRefusal})"); return; }
                    // Termina qualquer volta à cabine (captura) antes de começar a fuga.
                    if (_local.controller.IsReturning) break;
                    SwitchObjective objective = SwitchObjective.Instance;
                    _route = JanitorDark.RouteToStairs(_level.Data, objective.PointCell(0).x, objective.PointCell(0).y);
                    _local.controller.Teleport(CellCenter(_route[0]), 0f);
                    JanitorBrain.HoldEmerge = false;
                    _catches = _janitor.Catches;
                    _routeDistance = 0f;
                    _closest = float.MaxValue;
                    Goto(9, $"no alarme o interruptor mostra NO POWER e recusa ({_janitor.LastSwitchRefusal})");
                    break;
                }

                case 20:
                {
                    // A onda que persegue: o Ascensorista vem ao andar (para ouvir o estalo do interruptor).
                    if (!_attendantAsked)
                    {
                        _attendantAsked = true;
                        EntityDirector director = EntityDirector.Instance;
                        if (director != null && director.entityPrefab != null && director.entityPrefab.GetComponent<AttendantBrain>() != null)
                        {
                            _attendant = director.SpawnAt(_start, Vector3.forward) as AttendantBrain;
                            if (_attendant != null) _attendant.ForceArrival();
                        }
                    }
                    if (_attendant != null && _attendant.Phase != AttendantPhase.Out)
                    {
                        if (InStage > 45f) { Finish(false, $"o Ascensorista não chegou ao andar em 45 s ({_attendant.Phase})"); return; }
                        break;
                    }
                    if (!PickWaveRun(out string why)) { Finish(false, why); return; }
                    if (_attendant != null)
                    {
                        if (!ListenerSpot(LightSwitches.Instance.PositionOf(_waveRun), out Vector3 ear)) { Finish(false, "nenhuma célula a 15-25 m do interruptor para pôr o Ascensorista"); return; }
                        EntityDirector.Instance.SpawnAt(ear, LightSwitches.Instance.PositionOf(_waveRun) - ear);
                    }
                    _janitor.ScheduleWave(OfficeElevators.Clock() + 1e6);
                    OfficeLightWave.Paused = false;
                    JanitorBrain.HoldEmerge = false;
                    _local.controller.Teleport(CellCenter(_waveCells[1]), Yaw(CellCenter(_waveCells[_waveCells.Count - 1]) - CellCenter(_waveCells[1])));
                    _blinks = 0;
                    _lastLit = true;
                    _emerges = _janitor.Emerges;
                    int run = _janitor.StartWaveNear(_local);
                    if (run != _waveRun) { Finish(false, $"a onda escolheu o trecho {run}, não o {_waveRun}, onde o jogador está"); return; }
                    Goto(21, $"a onda escolheu o trecho em que o jogador está ({_waveCells.Count} células)" +
                              (_attendant != null ? $"; o Ascensorista está no andar ({_attendant.Phase})" : "; sem Ascensorista na fase"));
                    break;
                }

                case 21:
                {
                    // Pisca antes de apagar: o trecho conta como aceso para ele, as luminárias piscam.
                    OfficeLightWave wave = OfficeLightWave.Instance;
                    CeilingLight probe = wave.RunLamps(_waveRun)[wave.RunLamps(_waveRun).Count / 2];
                    if (probe.IsLit != _lastLit) { _blinks++; _lastLit = probe.IsLit; }
                    if (InStage < OfficeLightWave.WarnSeconds - 0.3f)
                    {
                        foreach (Vector2Int c in _waveCells)
                            if (JanitorDark.IsDark(c.x, c.y)) { Finish(false, $"o trecho já contava como escuro {InStage:F1} s depois do início do aviso"); return; }
                        if (_janitor.Present && JanitorDark.Cell(_janitor.transform.position, out int wx, out int wz) && _waveCells.Contains(new Vector2Int(wx, wz)))
                        { Finish(false, "o Zelador apareceu no trecho durante o aviso"); return; }
                        break;
                    }
                    if (InStage < OfficeLightWave.WarnSeconds + 1.2f) break;
                    int dark = 0;
                    foreach (Vector2Int c in _waveCells) if (JanitorDark.IsDark(c.x, c.y)) dark++;
                    if (_blinks < 4) { Finish(false, $"as luminárias não piscaram no aviso ({_blinks} trocas)"); return; }
                    if (dark < _waveCells.Count - 1) { Finish(false, $"depois do aviso só {dark} de {_waveCells.Count} células apagaram"); return; }
                    Goto(22, $"piscou {_blinks} vezes em {OfficeLightWave.WarnSeconds:F1} s (contando como aceso) e depois apagou ({dark} células)");
                    break;
                }

                case 22:
                {
                    // Nasce na ponta mais perto do jogador que ficou dentro, a distância justa.
                    // Pode já estar noutro escuro (aparece sozinho): o trecho apagando o faz afundar lá e subir aqui.
                    if (!_janitor.Present || _janitor.Emerges == _emerges || _janitor.LastEmergeKind != "na onda")
                    {
                        if (InStage > 6f) Finish(false, $"o trecho apagou com o jogador dentro e ele não apareceu na ponta (última aparição: {_janitor.LastEmergeKind}){State()}");
                        break;
                    }
                    JanitorDark.Cell(_janitor.transform.position, out int jx, out int jz);
                    if (!_waveCells.Contains(new Vector2Int(jx, jz))) { Finish(false, $"apareceu fora do trecho ({jx}, {jz})"); return; }
                    int expected = FairestEnd();
                    if (_janitor.LastEmergeCells < _janitor.fairCells) { Finish(false, $"apareceu a só {_janitor.LastEmergeCells} células do jogador"); return; }
                    if (expected >= 0 && _janitor.LastEmergeCells != expected) { Finish(false, $"apareceu a {_janitor.LastEmergeCells} células, mas a ponta justa mais perto estava a {expected}"); return; }
                    float straight = Flat(_janitor.transform.position - _local.transform.position).magnitude;
                    // O jogador vai ao interruptor do trecho (no escuro, ele subindo do chão).
                    StandAtSwitch(_waveRun);
                    _flips = _janitor.SwitchFlips;
                    _clacks = LightSwitches.Instance.Clacks;
                    _noises = WorldNoise.Emitted;
                    _heard = _attendant != null ? _attendant.WorldNoisesHeard : 0;
                    _vanishes = _janitor.Vanishes;
                    _pressed = false;
                    Goto(23, $"apareceu na ponta do trecho a {_janitor.LastEmergeCells} células de caminhada ({straight:F1} m) do jogador, a mais perto que é justa");
                    break;
                }

                case 23:
                {
                    // O interruptor religa o trecho, ele some, o estalo sai pelo mundo e chega ao Ascensorista.
                    Vector3 at = LightSwitches.Instance.PositionOf(_waveRun);
                    AimAt(at);
                    if (InStage < 0.25f) break;
                    if (!_pressed)
                    {
                        if (LightSwitches.Prompt != "E - LIGHTS ON") { Finish(false, $"a mira no interruptor mostra \"{LightSwitches.Prompt}\" (mirado {LightSwitches.Instance.Aimed})"); return; }
                        _pressed = true;
                        LightSwitches.PressForTest = true;
                        break;
                    }
                    if (_janitor.SwitchFlips == _flips)
                    {
                        if (InStage > 2f) Finish(false, $"o interruptor não religou ({_janitor.LastSwitchRefusal})");
                        break;
                    }
                    if (InStage < 1.2f) break;
                    foreach (Vector2Int c in _waveCells)
                        if (JanitorDark.IsDark(c.x, c.y)) { Finish(false, "o interruptor religou e o trecho continuou escuro"); return; }
                    if (_janitor.Present && JanitorDark.Cell(_janitor.transform.position, out int jx, out int jz) && _waveCells.Contains(new Vector2Int(jx, jz)))
                    { Finish(false, "o trecho acendeu e o Zelador continuou nele"); return; }
                    if (LightSwitches.Instance.Clacks == _clacks) { Finish(false, "o interruptor não estalou"); return; }
                    if (WorldNoise.Emitted == _noises || Vector3.Distance(WorldNoise.LastAt, at) > 0.5f) { Finish(false, "o estalo não saiu como barulho no mundo"); return; }
                    string heard;
                    if (_attendant == null) heard = "sem Ascensorista na fase (o EntityDirector do Level 3 não o tem)";
                    else
                    {
                        float d = Flat(_attendant.transform.position - at).magnitude;
                        if (_attendant.WorldNoisesHeard == _heard) { Finish(false, $"o Ascensorista ({_attendant.Phase}, a {d:F0} m) não ouviu o estalo"); return; }
                        heard = $"o Ascensorista ouviu (a {d:F0} m)";
                        // Ouviu e vem: sai do teste aqui (a fuga do alarme é só com o Zelador).
                        if (_attendant.IsSpawned) _attendant.NetworkObject.Despawn(true);
                        _attendant = null;
                    }
                    // Recarga: o mesmo trecho apaga de novo e o interruptor ainda não funciona.
                    JanitorBrain.HoldEmerge = true;
                    _janitor.StartWave(_waveRun);
                    _flips = _janitor.SwitchFlips;
                    _requests = _janitor.FlipRequests;
                    _pressed = false;
                    Goto(24, $"o interruptor religou o trecho e ele sumiu ({_janitor.LastVanish}); o estalo ({WorldNoise.LastRadius:F0} m) saiu pelo mundo; {heard}");
                    break;
                }

                case 24:
                {
                    AimAt(LightSwitches.Instance.PositionOf(_waveRun));
                    if (InStage < OfficeLightWave.WarnSeconds + 1f) break;
                    if (!_pressed)
                    {
                        if (LightSwitches.Prompt == null || !LightSwitches.Prompt.Contains("RESETTING")) { Finish(false, $"recarregando, a mira no interruptor mostra \"{LightSwitches.Prompt}\""); return; }
                        _prompt = LightSwitches.Prompt.Replace("\n", " ");
                        _pressed = true;
                        LightSwitches.PressForTest = true;
                        break;
                    }
                    if (InStage < OfficeLightWave.WarnSeconds + 2f) break;
                    if (_janitor.SwitchFlips != _flips) { Finish(false, "o interruptor religou de novo sem recarregar"); return; }
                    if (_janitor.FlipRequests == _requests || !_janitor.LastSwitchRefusal.Contains("recarreg")) { Finish(false, $"recarregando, o pedido não chegou ao host ou foi recusado por outro motivo ({_janitor.LastSwitchRefusal})"); return; }
                    int dark = 0;
                    foreach (Vector2Int c in _waveCells) if (JanitorDark.IsDark(c.x, c.y)) dark++;
                    if (dark == 0) { Finish(false, "o trecho acendeu com o interruptor recarregando"); return; }
                    _local.controller.Teleport(_start, 0f);
                    OfficeLightWave.Paused = true;
                    if (_attendant != null && _attendant.IsSpawned) _attendant.NetworkObject.Despawn(true);
                    SwitchObjective objective = SwitchObjective.Instance;
                    if (objective == null || objective.Kind != SwitchKind.FireAlarm) { Finish(false, "a fase não tem o alarme de incêndio"); return; }
                    objective.CompleteForTest();
                    Goto(8, $"recarregando, a mira mostra \"{_prompt}\" e o interruptor recusa ({_janitor.LastSwitchRefusal})");
                    break;
                }

                case 9:
                    // Espera ele aparecer em algum escuro antes de correr.
                    if (_janitor.Present || InStage > 10f)
                    {
                        Next(_janitor.Present
                            ? $"no apagão, apareceu a {Flat(_janitor.transform.position - _local.transform.position).magnitude:F1} m do jogador no acionador"
                            : "no apagão, não apareceu em 10 s (corrida sem ele no mapa)");
                    }
                    break;

                case 10:
                {
                    // Corre a rota até a escada.
                    if (_janitor.Catches != _catches || _local.controller.IsCaught)
                    {
                        Finish(false, $"pego a {_routeDistance:F0} m do acionador, na rota de fuga do alarme{State()}");
                        return;
                    }
                    if (_janitor.Present) _closest = Mathf.Min(_closest, Flat(_janitor.transform.position - _local.transform.position).magnitude);
                    _routeDistance += RunSpeed * Time.deltaTime;
                    int cell = Mathf.FloorToInt(_routeDistance / LevelBuilder.Cell);
                    if (cell >= _route.Count - 1)
                    {
                        string close = _closest < float.MaxValue ? $"chegou a {_closest:F1} m" : "não apareceu no caminho";
                        Finish(true, string.Join("; ", Notes) + $"; correu {_route.Count} células do acionador 0 à escada sem ser pego (o Zelador {close}); apareceu {_janitor.Emerges} vez(es), sumiu {_janitor.Vanishes}");
                        return;
                    }
                    float t = _routeDistance / LevelBuilder.Cell - cell;
                    Vector3 a = CellCenter(_route[cell]), b = CellCenter(_route[cell + 1]);
                    _local.controller.Teleport(Vector3.Lerp(a, b, t), Yaw(b - a));
                    break;
                }
            }
        }

        // O corte de energia do alarme: escuro em boa parte, emergência acesa, abrigos acesos e no
        // máximo dois escuros seguidos em cada rota de acionador até a escada.
        static bool CheckBlackout(out string report)
        {
            LevelData d = _level.Data;
            if (!JanitorDark.Blackout) { report = "o alarme disparou e o corte de energia não começou"; return false; }

            int lamps = 0, dark = 0;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (!JanitorDark.HasLamp(x, z)) continue;
                    lamps++;
                    if (JanitorDark.IsDark(x, z)) dark++;
                    if (d.IsShelter(x, z) && JanitorDark.IsDark(x, z)) { report = $"o abrigo ({x}, {z}) ficou escuro no alarme"; return false; }
                }
            if (dark < lamps / 2) { report = $"no alarme só {dark} de {lamps} células com luz escureceram"; return false; }
            if (_director.EmergencyLights == 0) { report = "sem luz de emergência no alarme"; return false; }

            SwitchObjective objective = SwitchObjective.Instance;
            int worst = 0;
            for (int i = 0; i < objective.Count; i++)
            {
                List<Vector2Int> route = JanitorDark.RouteToStairs(d, objective.PointCell(i).x, objective.PointCell(i).y);
                if (route.Count == 0) { report = $"sem rota do acionador {i} até a escada"; return false; }
                int run = 0;
                foreach (Vector2Int c in route)
                {
                    run = JanitorDark.IsDark(c.x, c.y) ? run + 1 : 0;
                    worst = Mathf.Max(worst, run);
                }
            }
            if (worst > 2) { report = $"uma rota de acionador até a escada tem {worst} células escuras seguidas"; return false; }

            report = $"no alarme, {dark} de {lamps} células escureceram, {_director.EmergencyLights} luzes de emergência, no máximo {worst} escuros seguidos nas rotas dos {objective.Count} acionadores";
            return true;
        }

        // Cinco células de corredor em linha reta, ligadas, cada uma com luminária que funciona,
        // fora de abrigo e longe da cabine (as quatro primeiras serão apagadas).
        static bool PickRun(out string why)
        {
            why = "";
            LevelData d = _level.Data;
            var nav = new LevelNav(_level);
            int[,] fromStart = nav.NewMap();
            nav.WorldToCell(_start, out int sx, out int sz);
            int tried = 0;
            foreach (WallSide side in new[] { WallSide.East, WallSide.North, WallSide.West, WallSide.South })
                for (int z = 0; z < d.Height; z++)
                    for (int x = 0; x < d.Width; x++)
                    {
                        int cx = x, cz = z;
                        bool ok = true;
                        for (int i = 0; i < 5 && ok; i++)
                        {
                            if (d.Office[cx, cz] != OfficeCell.Corridor || d.IsShelter(cx, cz) || !WorkingLamp(cx, cz)) { ok = false; break; }
                            if (Mathf.Abs(cx - sx) + Mathf.Abs(cz - sz) < 3) { ok = false; break; }
                            _run[i] = new Vector2Int(cx, cz);
                            if (i < 4 && (!d.CanWalk(cx, cz, side, false) || !d.Neighbor(cx, cz, side, out cx, out cz))) ok = false;
                        }
                        tried++;
                        if (!ok) continue;
                        _dir = Level3Setup.Side(side);
                        return true;
                    }
            why = $"nenhum corredor reto de 5 células com luminária boa ({tried} pontas tentadas)";
            return false;
        }

        // Um trecho da onda com interruptor e cinco células ou mais, longe da cabine.
        static bool PickWaveRun(out string why)
        {
            why = "";
            OfficeLightWave wave = OfficeLightWave.Instance;
            LightSwitches switches = LightSwitches.Instance;
            if (wave == null || switches == null) { why = "sem onda de luzes ou sem interruptores"; return false; }
            int best = -1, bestCells = 0;
            for (int r = 0; r < wave.RunCount; r++)
            {
                if (!switches.Has(r)) continue;
                _janitor.RunCells(r, _waveCells);
                bool good = true;
                foreach (Vector2Int c in _waveCells)
                    if (!WorkingLamp(c.x, c.y) || _level.Data.IsShelter(c.x, c.y)) good = false;
                if (good && _waveCells.Count > bestCells) { best = r; bestCells = _waveCells.Count; }
            }
            if (best < 0 || bestCells < 5) { why = $"nenhum trecho de 5 células com interruptor e luminárias boas ({switches.Count} interruptores em {wave.RunCount} trechos)"; return false; }
            _waveRun = best;
            _janitor.RunCells(best, _waveCells);
            // Em ordem ao longo do corredor (as células vêm das luminárias, já em ordem).
            return true;
        }

        // A menor distância de caminhada, entre as células escuras do trecho, que ainda é justa (>= fairCells).
        static int FairestEnd()
        {
            LevelData d = _level.Data;
            var nav = new LevelNav(_level);
            var players = new List<PlayerNetwork>();
            var maps = new List<int[,]>();
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                int[,] map = nav.NewMap();
                for (int z = 0; z < d.Height; z++) for (int x = 0; x < d.Width; x++) map[x, z] = -1;
                if (nav.WorldToCell(player.transform.position, out int px, out int pz))
                {
                    var queue = new Queue<Vector2Int>();
                    map[px, pz] = 0;
                    queue.Enqueue(new Vector2Int(px, pz));
                    while (queue.Count > 0)
                    {
                        Vector2Int c = queue.Dequeue();
                        foreach (WallSide side in new[] { WallSide.South, WallSide.North, WallSide.West, WallSide.East })
                        {
                            if (!d.CanWalk(c.x, c.y, side, false) || !d.Neighbor(c.x, c.y, side, out int nx, out int nz) || map[nx, nz] >= 0) continue;
                            map[nx, nz] = map[c.x, c.y] + 1;
                            queue.Enqueue(new Vector2Int(nx, nz));
                        }
                    }
                }
                players.Add(player);
                maps.Add(map);
            }

            int best = -1;
            foreach (Vector2Int c in _waveCells)
            {
                if (!JanitorDark.IsDark(c.x, c.y)) continue;
                Vector3 center = _level.CellCenter(c.x, c.y);
                bool ok = true;
                int nearest = int.MaxValue;
                for (int i = 0; i < players.Count; i++)
                {
                    int steps = maps[i][c.x, c.y];
                    float straight = Flat(center - players[i].transform.position).magnitude / LevelBuilder.Cell;
                    int cells = steps >= 0 ? steps : Mathf.CeilToInt(straight);
                    if (cells < _janitor.fairCells || straight < _janitor.fairCells * 0.75f) ok = false;
                    if (nav.WorldToCell(players[i].transform.position, out int px, out int pz) && d.IsShelter(px, pz)) continue;
                    nearest = Mathf.Min(nearest, cells);
                }
                if (!ok || nearest == int.MaxValue) continue;
                if (best < 0 || nearest < best) best = nearest;
            }
            return best;
        }

        // Uma célula com piso, fora de abrigo e fora do trecho, a 15-25 m do ponto (o mais perto de 20 m).
        static bool ListenerSpot(Vector3 point, out Vector3 spot)
        {
            spot = Vector3.zero;
            LevelData d = _level.Data;
            float best = float.MaxValue;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.IsShelter(x, z) || d.IsBarricade(x, z) || _waveCells.Contains(new Vector2Int(x, z))) continue;
                    if (d.Office[x, z] != OfficeCell.Corridor && d.Office[x, z] != OfficeCell.Lobby) continue;
                    Vector3 c = CellCenter(new Vector2Int(x, z));
                    float dist = Flat(c - point).magnitude;
                    if (dist < 15f || dist > 25f || Mathf.Abs(dist - 20f) >= best) continue;
                    best = Mathf.Abs(dist - 20f);
                    spot = c;
                }
            return best < float.MaxValue;
        }

        // De pé a 0,9 m do interruptor do trecho, de frente para ele.
        static void StandAtSwitch(int run)
        {
            Vector3 at = LightSwitches.Instance.PositionOf(run);
            Vector3 normal = LightSwitches.Instance.NormalOf(run);
            Vector3 foot = at + normal * 0.9f;
            foot.y = _level.transform.position.y;
            _local.controller.Teleport(foot, Yaw(-normal));
        }

        // Vira o jogador e a câmera para o ponto (a mira do interruptor).
        static void AimAt(Vector3 point)
        {
            Vector3 to = Flat(point - _local.transform.position);
            if (to.sqrMagnitude > 0.01f) _local.transform.rotation = Quaternion.Euler(0f, Yaw(to), 0f);
            Camera camera = _local.playerCamera;
            if (camera == null) return;
            Vector3 eye = camera.transform.position;
            Vector3 d = point - eye;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            // O controle reescreve a inclinação da câmera a cada quadro: vai por ele.
            _local.controller.SetPitch(pitch);
        }

        static bool WorkingLamp(int x, int z)
        {
            IReadOnlyList<CeilingLight> lamps = JanitorDark.LampsAt(x, z);
            if (lamps.Count == 0) return false;
            foreach (CeilingLight lamp in lamps) if (lamp == null || !lamp.Works) return false;
            return true;
        }

        static void Darken(int count, bool dark)
        {
            for (int i = 0; i < count; i++)
                foreach (CeilingLight lamp in JanitorDark.LampsAt(_run[i].x, _run[i].y))
                    if (lamp != null) lamp.SetForcedDark(dark);
        }

        static Vector3 CellCenter(Vector2Int c)
        {
            Vector3 p = _level.CellCenter(c.x, c.y);
            p.y = _level.transform.position.y;
            return p;
        }

        static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        static void Face(Vector3 point)
        {
            Vector3 to = Flat(point - _local.transform.position);
            if (to.sqrMagnitude > 0.01f) _local.transform.rotation = Quaternion.Euler(0f, Yaw(to), 0f);
            if (_local.playerCamera != null) _local.playerCamera.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
        }

        static string State()
        {
            if (_janitor == null) return "";
            return $" [onda: {_janitor.WaveLog}] [Zelador: presente={_janitor.Present}, estado={_janitor.State}, em {_janitor.transform.position}, escuro ali={JanitorDark.IsDarkAt(_janitor.transform.position)}, " +
                   $"apareceu {_janitor.Emerges}, sumiu {_janitor.Vanishes} ({_janitor.LastVanish}), pegou {_janitor.Catches}; jogador em {(_local != null ? _local.transform.position.ToString() : "?")}]";
        }

        static int CountInGame()
        {
            int count = 0;
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.InGame) count++;
            return count;
        }

        // ---------------------------------------------------------------- Cliente

        static void TickJoin(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    if (session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null) { Next(); break; }
                    if (session.Phase == SessionPhase.Menu && EditorApplication.timeSinceStartup - _lastTry > 4.0)
                    {
                        _lastTry = EditorApplication.timeSinceStartup;
                        session.Join("127.0.0.1");
                    }
                    break;

                case 1:
                {
                    JanitorBrain janitor = JanitorBrain.Janitor;
                    var body = janitor != null ? janitor.GetComponent<JanitorBody>() : null;
                    if (janitor != null && janitor.IsSpawned)
                    {
                        if (janitor.Present)
                        {
                            if (!_sawPresent) { _sawPresent = true; _seenAt = janitor.transform.position; }
                            _farthest = Mathf.Max(_farthest, Flat(janitor.transform.position - _seenAt).magnitude);
                            if (body != null && body.Shown > 0.95f) _sawShown = true;
                            _wasPresent = true;
                        }
                        else if (_wasPresent && body != null && body.Shown <= 0f) _sawVanish = true;
                    }

                    // O host pego: o Zelador entra em "comendo" (o estado vai pela rede) longe da cabine e,
                    // nos segundos seguintes, o host reaparece na cabine, perto do cliente. (A rede
                    // suaviza o teletransporte: não dá para contar com um salto de um quadro.)
                    if (janitor != null && janitor.IsSpawned && janitor.State == EntityState.Feed && !_hostKnown &&
                        Flat(janitor.transform.position - PlayerNetwork.Local.transform.position).magnitude > 8f)
                    {
                        _hostKnown = true;
                        _feedSeenAt = Now;
                    }
                    if (_hostKnown && !_sawHostCaught)
                    {
                        foreach (PlayerNetwork player in PlayerNetwork.All)
                        {
                            if (player == null || player == PlayerNetwork.Local || !player.IsSpawned) continue;
                            if (Flat(player.transform.position - PlayerNetwork.Local.transform.position).magnitude < 6f) _sawHostCaught = true;
                        }
                        if (!_sawHostCaught && Now - _feedSeenAt > 8f) _hostKnown = false;
                    }

                    if (JanitorDark.Blackout) _sawBlackout = true;
                    if (OfficeLightWave.Instance != null && OfficeLightWave.Instance.WarningCount > 0) _sawWarning = true;
                    if (LightSwitches.Instance != null && LightSwitches.Instance.Clacks > 0) _sawClack = true;
                    if (janitor != null && janitor.IsSpawned && LightSwitches.Instance != null && janitor.HostSwitchHash != 0)
                    {
                        if (janitor.HostSwitchHash != LightSwitches.Instance.Hash)
                        {
                            Finish(false, $"os interruptores do cliente não estão no mesmo lugar que os do host ({LightSwitches.Instance.Hash} x {janitor.HostSwitchHash})" + ClientReport());
                            return;
                        }
                        _sawSameSwitches = true;
                    }

                    if (_sawPresent && _sawShown && _farthest >= 1f && _sawVanish && _sawHostCaught && _sawBlackout && _sawWarning && _sawClack && _sawSameSwitches)
                    {
                        Finish(true, "o cliente viu o Zelador" + ClientReport());
                        return;
                    }
                    break;
                }
            }

            if (_stage > 0 && session.Phase != SessionPhase.Playing)
                Finish(false, "a sessão acabou antes" + ClientReport());
        }

        static string ClientReport() =>
            $"; no cliente: apareceu={_sawPresent}, corpo de pé={_sawShown}, andou {_farthest:F1} m, sumiu (corpo escondido)={_sawVanish}, host pego={_sawHostCaught}, apagão do alarme={_sawBlackout}, aviso piscando={_sawWarning}, estalo do interruptor={_sawClack}, interruptores iguais aos do host={_sawSameSwitches} ({(LightSwitches.Instance != null ? LightSwitches.Instance.Count : 0)} interruptores)";

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;

            EntityDirector.Paused = false;
            JanitorBrain.RunWhilePaused = false;
            JanitorBrain.HoldEmerge = false;
            OfficeLightWave.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            if (!ok && Notes.Count > 0) report += " | antes: " + string.Join("; ", Notes);
            Debug.Log("[BackRooms] Teste do Zelador " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

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
