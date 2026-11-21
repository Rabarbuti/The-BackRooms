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
    /// Teste em Play do objetivo comum (<see cref="SwitchObjective"/>, PC.1 de mecanicas.md) numa
    /// fase: disjuntores do Level 0, válvulas do Level 2 ou alarmes do Level 3.
    ///
    /// Sozinho: os pontos existem, longe uns dos outros e do início; a porta de saída começa trancada
    /// e não abre com E; mirando um ponto o aviso diz o que fazer; segurando E o jogador faz barulho
    /// (<c>PlayerNetwork.Working</c>) e o ponto arma; com o host contando dois jogadores
    /// (<see cref="SwitchObjective.PlayersForTest"/>) um ponto que passou da janela deixa de valer e
    /// dois armados fora da janela não cumprem; dois dentro da janela cumprem; sozinho de verdade, o
    /// ponto armado não expira; cumprido, a porta abre e a fase acaba. No Level 2, cumprido, a água
    /// desce (a drenagem). No Level 2 a última válvula é a da água: o jogador de teste vai até ela
    /// nadando, com o teclado virtual, da última célula a pé (W e Espaço, maré parada), e a gira à
    /// tona segurando Espaço (ou de pé, se é numa ilha); o tempo da travessia e o de girar vão para o
    /// relato.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.ObjectiveTest.BatchRun (Level 0),
    /// BatchRunLevel2, BatchRunLevel3. Sai com 0 se passou, 3 se falhou. Linha
    /// "[BackRooms] Teste do objetivo". Dois editores (Level 0): <see cref="BatchHost"/> arma um
    /// disjuntor quando o cliente chega e <see cref="BatchJoin"/> arma outro dentro da janela; os dois
    /// têm de ver o objetivo cumprido e a porta destrancada.
    /// </summary>
    public static class ObjectiveTest
    {
        const string ActiveKey = "BackRooms.ObjectiveTest.Active";
        const string BatchKey = "BackRooms.ObjectiveTest.Batch";
        const string ExitCodeKey = "BackRooms.ObjectiveTest.ExitCode";
        const string RoleKey = "BackRooms.ObjectiveTest.Role";
        const string SceneKey = "BackRooms.ObjectiveTest.Scene";
        const string SoloRole = "solo", HostRole = "host", JoinRole = "join";
        const string Level0Scene = "Assets/_Game/Scenes/Level0_Test.unity";
        const double Timeout = 200.0;
        const double NetTimeout = 300.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks;
        static double _began, _lastTry;
        static float _stageStart, _waterBefore;
        static bool _finished, _sawWorking;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static SwitchObjective _objective;
        static EntityBrain _entity;
        static Vector3 _entityFrom;
        static float _entityAt;
        static bool _entityHeard, _entityChecked;
        // A travessia a nado até a válvula da água (Level 2).
        static List<Vector3> _swimRoute;
        static int _swimIndex;
        static float _swimBest, _swimProgress, _swimStart, _swimLimit;
        static int _swimSplashes = -1;
        static Keyboard _keyboard;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;

        [MenuItem("BackRooms/Testar objetivo (Level 0)")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false, SoloRole, Level0Scene);
        }

        public static void BatchRun() { Run(true, SoloRole, Level0Scene); }
        public static void BatchRunLevel2() { Run(true, SoloRole, Level2Setup.ScenePath); }
        public static void BatchRunLevel3() { Run(true, SoloRole, Level3Setup.ScenePath); }
        public static void BatchHost() { Run(true, HostRole, Level0Scene); }
        public static void BatchJoin() { Run(true, JoinRole, Level0Scene); }

        static void Run(bool batch, string role, string scene)
        {
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(RoleKey, role);
            SessionState.SetString(SceneKey, scene);
            EditorApplication.EnterPlaymode();
        }

        static string Role => SessionState.GetString(RoleKey, SoloRole);
        static string Scene => SessionState.GetString(SceneKey, Level0Scene);

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
            // O cliente tenta entrar antes de o host abrir a sessão e tenta de novo: não é defeito.
            if (Role == JoinRole && message.Contains("Failed to connect to server")) return;
            Errors.Add(message);
        }

        static float Now => Time.time;
        static float InStage => Now - _stageStart;

        static void Next(string note = null)
        {
            if (note != null) Notes.Add(note);
            _stage++;
            _stageStart = Now;
            _stageTicks = 0;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - _began > (Role == SoloRole ? Timeout : NetTimeout))
            {
                Finish(false, $"tempo esgotado na etapa {_stage}");
                return;
            }

            _stageTicks++;
            if (Role == JoinRole) { TickJoin(session); return; }
            if (Role == HostRole) { TickHost(session); return; }

            PlayerController body = _local != null ? _local.controller : null;
            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    session.fixedSeed = SeedArg();
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                {
                    if (!Ready(session)) break;
                    if (_objective == null) { Finish(false, "a fase não tem objetivo (SwitchObjective)"); return; }
                    int expected = _objective.Kind == SwitchKind.Breaker ? 4 : _objective.Kind == SwitchKind.Valve ? 2 : 3;
                    if (_objective.Count != expected) { Finish(false, $"{_objective.Count} pontos de {_objective.Kind} (eram para ser {expected})"); return; }
                    var cells = new List<string>();
                    for (int i = 0; i < _objective.Count; i++)
                    {
                        Vector2Int a = _objective.PointCell(i);
                        cells.Add($"({a.x},{a.y})");
                        if (_level.Data.InRoom(a.x, a.y)) { Finish(false, $"o ponto {i} está na sala inicial"); return; }
                        for (int j = i + 1; j < _objective.Count; j++)
                        {
                            Vector2Int b = _objective.PointCell(j);
                            if (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) < 3) { Finish(false, $"os pontos {i} e {j} estão a menos de 3 células"); return; }
                        }
                    }
                    if (!SwitchObjective.Locked || !LevelExit.Locked) { Finish(false, "a porta de saída não começou trancada pelo objetivo"); return; }
                    if (_objective.Note == null) { Finish(false, "sem o bilhete de ensino na sala inicial"); return; }
                    Vector3 noteFrom = _level.SpawnCenter;
                    if (new Vector2(_objective.Note.position.x - noteFrom.x, _objective.Note.position.z - noteFrom.z).magnitude > 8.5f) { Finish(false, "o bilhete de ensino ficou longe do início"); return; }

                    LevelExit exit = LevelExit.Instance;
                    Vector3 front = exit.DoorPosition - exit.door.forward * 1.3f;
                    body.Teleport(new Vector3(front.x, exit.DoorPosition.y + 0.05f, front.z), Yaw(exit.door.forward));
                    body.SetPitch(0f);
                    Next($"{_objective.Count} pontos de {_objective.Kind} nas células {string.Join(" ", cells)}, hash {_objective.Hash():X8}");
                    break;
                }

                case 2:
                {
                    LevelExit exit = LevelExit.Instance;
                    if (_stageTicks < 3) break;
                    if (!exit.LocalInReach) { if (InStage > 2f) Finish(false, "de frente para a porta, ela não ficou ao alcance"); break; }
                    if (exit.Interact()) { Finish(false, "a porta trancada pelo objetivo aceitou o E"); return; }
                    if (exit.Rattles < 1) { Finish(false, "a porta trancada não chacoalhou com o E"); return; }
                    string locked = _objective.LockedText;
                    // Sozinho de verdade: o último ponto, armado, não expira.
                    // Se ele exige nadar (a válvula da água), o jogador vai até lá nadando.
                    SwitchObjective.PlayersForTest = 0;
                    if (_objective.PointSwim(_objective.Count - 1) > 0) StartSwim(_objective.Count - 1);
                    else
                    {
                        GoTo(_objective.Count - 1);
                        SwitchObjective.HoldForTest = true;
                    }
                    Next($"trancada, a porta não abre (\"{locked.Replace('\n', ' ')}\")");
                    break;
                }

                case 3:
                {
                    int last = _objective.Count - 1;
                    if (_swimRoute != null)
                    {
                        if (!SwimStep(last)) break;
                        _swimRoute = null;
                        int splashes = _local.footsteps != null && _swimSplashes >= 0 ? _local.footsteps.Splashes - _swimSplashes : -1;
                        Notes.Add($"nadou até a válvula da água ({_objective.PointSpot(last)}, {_objective.PointSwim(last)} célula(s) d'água) em {Now - _swimStart:F1} s, maré parada, {splashes} espirro(s) (o Undertow ouve espirro de longe, pelos canos)");
                        SwitchObjective.HoldForTest = true;
                        PoolTide.Paused = false; // a maré volta a valer: drenando, ela não pode tocar o alarme
                        _stageStart = Now;
                        break;
                    }
                    KeepUp(last);
                    Aim(last);
                    if (_objective.ActiveNow < 1) { if (InStage > _objective.HoldSeconds + 3f) Finish(false, $"sozinho, segurou E no ponto {last} e ele não armou (aviso \"{SwitchObjective.Prompt}\")"); break; }
                    SwitchObjective.HoldForTest = false;
                    if (_objective.PointInWater(last)) Notes.Add($"girou a válvula à tona, segurando Espaço e E, em {InStage:F1} s");
                    KeepUp(-1);
                    if (_objective.PointArmedAt(last) < SwitchObjective.Permanent) { Finish(false, "sozinho, o ponto armado ganhou janela (devia ficar armado)"); return; }
                    if (_objective.Complete) { Finish(false, "sozinho, um ponto só cumpriu o objetivo"); return; }
                    // Daqui em diante o host conta dois jogadores: vale a janela.
                    _objective.ResetForTest();
                    SwitchObjective.PlayersForTest = 2;
                    GoTo(0);
                    Next($"sozinho, o ponto {last} armado ficou armado para sempre");
                    break;
                }

                case 4:
                    // Mirando o ponto 0, sem segurar: o aviso de segurar E.
                    Aim(0);
                    if (_stageTicks < 3) break;
                    if (SwitchObjective.Prompt == null || !SwitchObjective.Prompt.StartsWith("E - HOLD"))
                    {
                        if (InStage > 2f) Finish(false, $"mirando o ponto 0, o aviso foi \"{SwitchObjective.Prompt}\"");
                        break;
                    }
                    SwitchObjective.HoldForTest = true;
                    _sawWorking = false;
                    Next($"mirando o ponto 0, o aviso é \"{SwitchObjective.Prompt.Replace('\n', ' ')}\"");
                    break;

                case 5:
                    // Segurando E: barulho e, depois do tempo, armado (com a janela).
                    Aim(0);
                    if (_local.Working) _sawWorking = true;
                    if (_objective.ActiveNow < 1)
                    {
                        if (InStage > _objective.HoldSeconds + 3f) Finish(false, $"segurou E por {InStage:F1} s no ponto 0 e ele não armou (aviso \"{SwitchObjective.Prompt}\")");
                        break;
                    }
                    SwitchObjective.HoldForTest = false;
                    if (!_sawWorking) { Finish(false, "segurando E o jogador não fez barulho (PlayerNetwork.Working)"); return; }
                    if (InStage < _objective.HoldSeconds - 0.3f) { Finish(false, $"armou em {InStage:F1} s (eram {_objective.HoldSeconds} s)"); return; }
                    if (_objective.Complete) { Finish(false, "um ponto só cumpriu o objetivo"); return; }
                    Next($"segurando E fez barulho e armou o ponto 0 em {InStage:F1} s");
                    break;

                case 6:
                    // Passou da janela: deixa de valer.
                    if (InStage < _objective.WindowSeconds + 0.6f)
                    {
                        if (_objective.ActiveNow < 1 && InStage < _objective.WindowSeconds - 0.6f) { Finish(false, $"o ponto 0 deixou de valer em {InStage:F1} s (a janela é de {_objective.WindowSeconds} s)"); return; }
                        break;
                    }
                    if (_objective.ActiveNow != 0) { Finish(false, "o ponto 0 continuou valendo depois da janela"); return; }
                    GoTo(1);
                    SwitchObjective.HoldForTest = true;
                    Next($"o ponto 0 deixou de valer depois de {_objective.WindowSeconds} s");
                    break;

                case 7:
                    KeepUp(1);
                    Aim(1);
                    if (_objective.ActiveNow < 1) { if (InStage > _objective.HoldSeconds + 3f) Finish(false, "o ponto 1 não armou"); break; }
                    SwitchObjective.HoldForTest = false;
                    KeepUp(-1);
                    if (_objective.Complete) { Finish(false, "o ponto 0 vencido e o 1 cumpriram o objetivo (fora da janela)"); return; }
                    if (_objective.Arms != 3) { Finish(false, $"{_objective.Arms} armações contadas (eram 3: a de sozinho, a do 0 e a do 1)"); return; }
                    // O outro jogador arma o ponto 0 agora, dentro da janela do 1 (o host recebe o
                    // aviso dele como recebe o de um cliente, sem a conferência de distância).
                    _objective.HostArm(0, null);
                    Next("ponto 1 armado com o 0 vencido: não cumpriu");
                    break;

                case 8:
                    if (!_objective.Complete)
                    {
                        if (InStage > 1f) Finish(false, $"dois pontos dentro da janela não cumpriram (valendo {_objective.ActiveNow})");
                        break;
                    }
                    if (SwitchObjective.Locked || LevelExit.Locked) { Finish(false, "cumprido, a porta continuou trancada"); return; }
                    _waterBefore = PoolWater.Current != null ? PoolWater.Current.SurfaceY : 0f;
                    Next("dois pontos dentro da janela cumpriram o objetivo e destrancaram a porta");
                    break;

                case 9:
                    // Level 2: a água desce (drenagem).
                    if (PoolWater.Current != null)
                    {
                        if (InStage < 6f) break;
                        float drop = _waterBefore - PoolWater.Current.SurfaceY;
                        if (drop < 0.2f) { Finish(false, $"cumprido nas Piscinas, a água desceu só {drop:F2} m em 6 s"); return; }
                        // A maré não pode continuar: drenando, o alarme dela cala (PoolTide).
                        if (PoolTide.Current != null && PoolTide.Current.AlarmOn) { Finish(false, "cumprido nas Piscinas, o alarme da maré continuou ligado"); return; }
                        Notes.Add($"a água desceu {drop:F2} m em 6 s" + (PoolTide.Current != null ? $" (maré: {PoolTide.Current.Phase})" : ""));
                    }
                    // Level 3: a entidade mora no Escritório e o alarme a chama.
                    if (_objective.Kind == SwitchKind.FireAlarm && !_entityChecked)
                    {
                        if (!CheckEntity()) break;
                        _entityChecked = true;
                    }
                    {
                        LevelExit door = LevelExit.Instance;
                        Vector3 doorFront = door.DoorPosition - door.door.forward * 1.3f;
                        body.Teleport(new Vector3(doorFront.x, door.DoorPosition.y + 0.05f, doorFront.z), Yaw(door.door.forward));
                        body.SetPitch(0f);
                        Next();
                    }
                    break;

                case 10:
                {
                    LevelExit exit = LevelExit.Instance;
                    if (_stageTicks < 3) break;
                    if (!exit.Interact()) { if (InStage > 2f) Finish(false, "destrancada, a porta não aceitou o E"); break; }
                    Next("a porta aceitou o E");
                    break;
                }

                case 11:
                    if (!session.Passing) { if (InStage > 3f) Finish(false, "a porta aceitou o E e a fase não acabou"); break; }
                    if (!LevelExit.Instance.OpenedSound) { Finish(false, "a fase acabou sem o som da porta abrindo"); return; }
                    Finish(true, string.Join("; ", Notes) + "; a porta chacoalhou trancada, abriu com som e a fase acabou");
                    break;
            }
        }

        // Põe a entidade no outro acionador, manda o barulho do alarme e confere que ela anda pela
        // grade do Escritório (o LevelNav) na direção dele. Verdadeiro quando terminou.
        static bool CheckEntity()
        {
            EntityDirector director = EntityDirector.Instance;
            if (director == null) { Finish(false, "o Escritório não tem a entidade (EntityDirector)"); return false; }
            if (_entity == null)
            {
                Vector2Int cell = _objective.PointCell(_objective.Count - 1); // longe do jogador, que está no 1
                _entityFrom = _level.CellCenter(cell.x, cell.y);
                _entity = director.SpawnAt(_entityFrom, Vector3.forward);
                _entityAt = Now;
                if (_entity == null) Finish(false, "a entidade não apareceu no Escritório");
                return false;
            }
            if (Now - _entityAt < 0.5f) return false;
            // No Escritório o inimigo é o Ascensorista (escondido num poço até chegar de elevador).
            if (_entity is AttendantBrain attendant) return CheckAttendant(attendant);
            if (!_entityHeard)
            {
                _entityHeard = true;
                _entity.HearNoise(_objective.PointPosition(0) + _objective.PointFacing(0) * 1.2f);
                if (_entity.State != EntityState.Investigate && _entity.State != EntityState.Chase)
                    Finish(false, $"o barulho do alarme não chamou a entidade (ficou em {_entity.State})");
                return false;
            }
            Vector3 moved = _entity.transform.position - _entityFrom;
            moved.y = 0f;
            if (moved.magnitude >= 2f)
            {
                Notes.Add($"a entidade apareceu no Escritório, ouviu o alarme e andou {moved.magnitude:F1} m pela grade ({_entity.State})");
                return true;
            }
            if (Now - _entityAt > 12f) Finish(false, $"a entidade não andou no Escritório ({_entity.State}, {moved.magnitude:F1} m em 12 s)");
            return false;
        }

        // O Ascensorista fora do andar: a sirene (o barulho do alarme pelo WorldNoise) o faz chegar logo
        // de elevador e ir direto ao acionador. Verdadeiro quando ele saiu da cabine e andou para lá.
        static bool CheckAttendant(AttendantBrain attendant)
        {
            Vector3 alarm = _objective.PointPosition(0) + _objective.PointFacing(0) * 1.2f;
            if (!_entityHeard)
            {
                if (attendant.Phase != AttendantPhase.Away) { Finish(false, $"posto pelo diretor, o Ascensorista não foi se esconder num poço ({attendant.Phase})"); return false; }
                EntityDirector.Paused = false; // ele só chega de elevador com o diretor ligado
                int heard = attendant.WorldNoisesHeard;
                WorldNoise.Emit(alarm, SwitchObjective.CompleteNoiseRadius);
                if (attendant.WorldNoisesHeard == heard) { Finish(false, "a sirene (WorldNoise) não chegou ao Ascensorista"); return false; }
                if (attendant.NextArrival > OfficeElevators.Clock() + 6.5) { Finish(false, $"a sirene não adiantou a chegada do Ascensorista (daqui a {attendant.NextArrival - OfficeElevators.Clock():F0} s)"); return false; }
                _entityHeard = true;
                _entityAt = Now;
                return false;
            }
            if (attendant.Phase != AttendantPhase.Out)
            {
                if (Now - _entityAt > 30f) Finish(false, $"chamado pela sirene, o Ascensorista não saiu do elevador em 30 s ({attendant.Phase})");
                return false;
            }
            if (_entityFrom == Vector3.zero || attendant.Shaft >= 0 && (_entityFrom - attendant.FrontPoint(attendant.Shaft)).sqrMagnitude > 0.01f)
                _entityFrom = attendant.FrontPoint(attendant.Shaft);
            Vector3 toAlarm = attendant.Goal - alarm;
            toAlarm.y = 0f;
            if (toAlarm.magnitude > 4.5f) { Finish(false, $"chamado pela sirene, o Ascensorista vai para outro lugar (destino a {toAlarm.magnitude:F1} m do acionador)"); return false; }
            Vector3 moved = attendant.transform.position - _entityFrom;
            moved.y = 0f;
            if (moved.magnitude >= 2f)
            {
                EntityDirector.Paused = true;
                Notes.Add($"o Ascensorista ouviu a sirene fora do andar, chegou de elevador e andou {moved.magnitude:F1} m para o acionador");
                return true;
            }
            if (Now - _entityAt > 45f) Finish(false, $"o Ascensorista saiu do elevador e não andou para o acionador ({attendant.State}, {moved.magnitude:F1} m)");
            return false;
        }

        static bool Ready(GameSession session)
        {
            if (session.Phase != SessionPhase.Playing) return false;
            _local = PlayerNetwork.Local;
            _level = session.level;
            _objective = SwitchObjective.Instance;
            if (_local == null || _level == null || _level.Data == null) return false;
            return InStage >= 1.5f;
        }

        static void TickHost(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host();
                    Next();
                    break;
                case 1:
                    if (!Ready(session)) break;
                    if (_objective == null || _objective.Count < 2) { Finish(false, "sem objetivo"); return; }
                    if (session.PlayerCount < 2) break;
                    Notes.Add($"cliente entrou; pontos hash {_objective.Hash():X8}");
                    GoTo(0);
                    SwitchObjective.HoldForTest = true;
                    Next();
                    break;
                case 2:
                    Aim(0);
                    if (_objective.ActiveNow < 1) { if (InStage > _objective.HoldSeconds + 3f) Finish(false, "o host não armou o ponto 0"); break; }
                    SwitchObjective.HoldForTest = false;
                    if (_objective.Complete) { Finish(false, "com dois jogadores, um ponto só cumpriu"); return; }
                    Next("host armou o ponto 0 (dois jogadores: com janela)");
                    break;
                case 3:
                    if (!_objective.Complete) { if (InStage > 40f) Finish(false, "o cliente não armou o ponto 1 a tempo"); break; }
                    if (LevelExit.Locked) { Finish(false, "cumprido, a porta continuou trancada no host"); return; }
                    Next("o cliente armou o ponto 1 dentro da janela: cumprido no host");
                    break;
                case 4:
                    if (InStage < 8f) break;
                    Finish(true, string.Join("; ", Notes));
                    break;
            }
        }

        static void TickJoin(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    if (EditorApplication.timeSinceStartup - _began < 12.0) break;
                    if (session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null) { Next(); break; }
                    if (session.Phase == SessionPhase.Menu && EditorApplication.timeSinceStartup - _lastTry > 4.0)
                    {
                        _lastTry = EditorApplication.timeSinceStartup;
                        session.Join("127.0.0.1");
                    }
                    break;
                case 1:
                    if (!Ready(session)) break;
                    if (_objective == null) { Finish(false, "o cliente não montou o objetivo"); return; }
                    Notes.Add($"pontos hash {_objective.Hash():X8}");
                    GoTo(1);
                    Next();
                    break;
                case 2:
                    // Espera o host armar o ponto 0 (o estado chega pela rede) e arma o 1.
                    Aim(1);
                    if (_objective.ActiveNow < 1) { if (InStage > 60f) Finish(false, "o cliente nunca viu o ponto 0 armado"); break; }
                    SwitchObjective.HoldForTest = true;
                    Next("o cliente viu o ponto 0 armado pelo host");
                    break;
                case 3:
                    Aim(1);
                    if (!_objective.Complete) { if (InStage > _objective.HoldSeconds + 6f) Finish(false, "o cliente segurou E no ponto 1 e o objetivo não se cumpriu"); break; }
                    SwitchObjective.HoldForTest = false;
                    if (LevelExit.Locked) { Finish(false, "cumprido, a porta continuou trancada no cliente"); return; }
                    Finish(true, string.Join("; ", Notes) + "; o cliente armou o ponto 1 e viu o objetivo cumprido e a porta destrancada");
                    break;
            }
        }

        // Põe o jogador de frente para o ponto, a 1,1 m (de pé), ou à tona a 0,8 m se o ponto fica
        // sobre a água funda, e mira nele.
        static void GoTo(int index)
        {
            _local.controller.Teleport(StandSpot(index), Yaw(-_objective.PointFacing(index)));
            Aim(index);
        }

        static Vector3 StandSpot(int index)
        {
            Vector3 at = _objective.PointPosition(index);
            Vector3 facing = _objective.PointFacing(index);
            if (_objective.PointInWater(index) && PoolWater.Current != null)
            {
                Vector3 spot = at + facing * 0.8f;
                return new Vector3(spot.x, PoolWater.Current.SurfaceY - _local.controller.floatDepth, spot.z);
            }
            Vector3 stand = at + facing * 1.1f;
            return new Vector3(stand.x, _objective.PointFloor(index) + 0.05f, stand.z);
        }

        // ---------------------------------------------------------------- A nado (Level 2)

        // Da última célula a pé da rota de menos nado até a válvula: pelo meio das divisas e das
        // células, e por fim até a frente dela. Maré parada (o ralo da vazante não entra na conta).
        static void StartSwim(int index)
        {
            IReadOnlyList<Vector2Int> cells = _objective.PointApproach(index);
            _swimRoute = new List<Vector3>();
            for (int k = 1; k < cells.Count; k++)
            {
                Vector3 a = _level.CellCenter(cells[k - 1].x, cells[k - 1].y), b = _level.CellCenter(cells[k].x, cells[k].y);
                _swimRoute.Add((a + b) * 0.5f);
                _swimRoute.Add(b);
            }
            _swimRoute.Add(StandSpot(index));
            PoolTide.Paused = true;
            StartKeyboard();
            Vector3 shore = _level.CellCenter(cells[0].x, cells[0].y);
            _local.controller.Teleport(shore + Vector3.up * 0.05f, Yaw(Flat(_swimRoute[0] - shore)));
            _local.controller.SetPitch(0f);
            _swimIndex = 0;
            _swimBest = float.MaxValue;
            _swimStart = _swimProgress = Now;
            _swimLimit = cells.Count * LevelBuilder.Cell / 0.9f + 20f;
            _swimSplashes = -1; // contados a partir do primeiro quadro depois do teleporte
        }

        // Um quadro da travessia: verdadeiro ao chegar à frente da válvula.
        static bool SwimStep(int index)
        {
            PlayerController body = _local.controller;
            Vector3 position = _local.transform.position;
            if (_swimSplashes < 0 && _local.footsteps != null) _swimSplashes = _local.footsteps.Splashes;
            if (body.IsReturning) { Finish(false, $"nadando até a válvula da água, o jogador voltou ao início ({position})"); return false; }
            while (_swimIndex < _swimRoute.Count - 1 && Flat(_swimRoute[_swimIndex] - position).magnitude < 0.45f)
            {
                _swimIndex++;
                _swimBest = float.MaxValue;
                _swimProgress = Now;
            }
            Vector3 to = Flat(_swimRoute[_swimIndex] - position);
            if (_swimIndex == _swimRoute.Count - 1 && to.magnitude < 0.35f)
            {
                KeepUp(index);
                return true;
            }
            if (to.magnitude < _swimBest - 0.05f) { _swimBest = to.magnitude; _swimProgress = Now; }
            Vector3 local = _level.transform.InverseTransformPoint(position);
            var cell = new Vector2Int(Mathf.FloorToInt(local.x / LevelBuilder.Cell), Mathf.FloorToInt(local.z / LevelBuilder.Cell));
            string where = $"pés em {position}, célula {cell}, ponto {_swimIndex + 1}/{_swimRoute.Count} a {to.magnitude:F2} m, nadando={body.IsSwimming}";
            if (Now - _swimProgress > 8f) { Finish(false, "nadando até a válvula da água, parou de avançar: " + where); return false; }
            if (Now - _swimStart > _swimLimit) { Finish(false, $"nadando até a válvula da água, tempo esgotado ({_swimLimit:F0} s): " + where); return false; }
            if (to.sqrMagnitude > 0.0025f) _local.transform.rotation = Quaternion.Euler(0f, Yaw(to), 0f);
            body.SetPitch(0f);
            if (body.IsSwimming) Press(Key.W, Key.Space);
            else Press(Key.W);
            return false;
        }

        // Na água funda, segura Espaço (fica à tona); fora dela (ou com -1), solta tudo.
        static void KeepUp(int index)
        {
            bool water = index >= 0 && _objective.PointInWater(index);
            if (water) StartKeyboard();
            if (water) Press(Key.Space);
            else Press();
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static void StartKeyboard()
        {
            if (_keyboard != null) return;
            _backgroundBefore = InputSystem.settings.backgroundBehavior;
            _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

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

        static void Aim(int index)
        {
            Vector3 target = _objective.PointPosition(index) + _objective.PointFacing(index) * 0.08f;
            Transform eye = _local.playerCamera.transform;
            Vector3 to = target - eye.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude < 0.0001f) return;
            _local.transform.rotation = Quaternion.Euler(0f, Yaw(flat), 0f);
            _local.controller.SetPitch(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
        }

        // -seed N na linha de comando (padrão 1998): no Level 2, para escolher ilha ou parede sobre a funda.
        static int SeedArg()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-seed" && int.TryParse(args[i + 1], out int seed)) return seed;
            return 1998;
        }

        static float Yaw(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;

            SwitchObjective.HoldForTest = false;
            SwitchObjective.PlayersForTest = 0;
            EntityDirector.Paused = false;
            PoolTide.Paused = false;
            ReleaseKeyboard();
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log($"[BackRooms] Teste do objetivo ({System.IO.Path.GetFileNameWithoutExtension(Scene)}, {Role}) " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

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
