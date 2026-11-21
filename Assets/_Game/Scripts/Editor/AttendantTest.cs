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
    /// Teste em Play das regras do Ascensorista, o inimigo do elevador do Level 3 (<see cref="AttendantBrain"/>).
    /// Hospeda o Escritório (seed 1998) e confere, em ordem:
    ///
    /// 1. o diretor da fase põe o Ascensorista (não a entidade do Level 0), escondido num poço;
    /// 2. chega na hora marcada do relógio da sessão, pelo poço da seed: a seta do painel pisca com a
    ///    porta fechada, a porta abre, ele sai para o saguão;
    /// 3. ronda (parando para escutar) e, acabado o tempo, volta à cabine, a porta fecha, ele some;
    /// 4. agachado e parado a 4 m do caminho dele, ele passa sem ouvir;
    /// 5. correndo a uns 10 m, ele ouve e vem; agachado e parado no caminho, ele ouve a respiração e pega;
    /// 6. quem ele pega aparece dentro da cabine dele, a porta fecha, só então cai o sinal e o jogador
    ///    volta ao início; a porta reabre;
    /// 7. entra numa salinha acesa atrás do barulho e pega quem está lá (o abrigo do Zelador não vale);
    /// 8. o alarme de incêndio (dois acionadores) o chama ao acionador; fora do andar, chega mais cedo e
    ///    vai direto para lá;
    /// 9. trancado numa cabine de poço (botão de fechar), não é ouvido nem correndo do lado de fora; mas
    ///    se ele chega por aquela cabine, leva quem está dentro.
    ///
    /// "Correndo" e "andando" aqui são o jogador movido aos trancos (o host mede o barulho pelo
    /// deslocamento), não o teclado: em lugar apertado o teclado bate na parede e para.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.AttendantTest.BatchRun (0 passou, 3 falhou).
    /// Dois editores: <see cref="BatchHost"/> (o mesmo teste, esperando o cliente; no fim pega o
    /// cliente) + <see cref="BatchJoin"/> (na cópia: vê o Ascensorista chegar, a seta acesa, a porta
    /// aberta, ele andar; depois vai para longe, é pego, aparece na cabine com a porta fechando e
    /// volta ao início). Linha "[BackRooms] Teste do Ascensorista PASSOU/FALHOU".
    /// </summary>
    public static class AttendantTest
    {
        const string ActiveKey = "BackRooms.AttendantTest.Active";
        const string BatchKey = "BackRooms.AttendantTest.Batch";
        const string ExitCodeKey = "BackRooms.AttendantTest.ExitCode";
        const string RoleKey = "BackRooms.AttendantTest.Role";
        const string SoloRole = "solo", HostRole = "host", JoinRole = "join";
        const double Timeout = 420.0;
        const double NetTimeout = 600.0;
        const int Seed = 1998;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage;
        static double _began;
        static float _stageStart;
        static int _stageTicks;
        static bool _finished;
        static double _lastTry;

        static PlayerNetwork _local;
        static LevelBuilder _level;
        static EntityDirector _director;
        static AttendantBrain _brain;
        static AttendantBody _body;
        static Keyboard _keyboard;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;

        static int _expected, _other;
        static double _scheduled;
        static bool _sawBlink, _sawClosedComing, _sawListen, _sawReturning, _sawBoarding, _sawLeaving, _sawOpenWhileAway;
        static Vector3 _mark, _spot, _spotAxis;
        static float _closest, _subStart;
        static int _heardBefore, _takesBefore, _capturesBefore, _sub;
        static bool _sawTaken, _sawInCab, _sawDoorShut;
        static Vector2Int _room;
        static int _cabUsed;

        [MenuItem("BackRooms/Testar Ascensorista (Level 3)")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false);
        }

        public static void BatchRun() => Run(true, SoloRole);
        public static void BatchHost() => Run(true, HostRole);
        public static void BatchJoin() => Run(true, JoinRole);

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
        static double Clock => OfficeElevators.Clock();

        static void Next(string note = null)
        {
            if (note != null) Notes.Add(note);
            _stage++;
            _stageStart = Now;
            _stageTicks = 0;
            _sub = 0;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - _began > (Role == SoloRole ? Timeout : NetTimeout))
            {
                Finish(false, $"tempo esgotado na etapa {_stage}.{_sub}" + Status());
                return;
            }

            if (Role == JoinRole)
            {
                TickJoin(session);
                return;
            }

            _stageTicks++;
            try
            {
                Step(session);
            }
            catch (System.Exception e)
            {
                Finish(false, $"exceção na etapa {_stage}: {e.Message}");
            }
        }

        static string Status()
        {
            if (_brain == null) return "";
            return $" (fase {_brain.Phase}, estado {_brain.State}, poço {_brain.Shaft}, em {_brain.transform.position:F1}, destino {_brain.Goal:F1}, jogador em {(_local != null ? _local.transform.position.ToString("F1") : "-")})";
        }

        static void Step(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    session.fixedSeed = Seed;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                {
                    if (session.Phase != SessionPhase.Playing) break;
                    if (Role == HostRole && CountInGame() < 2) break;
                    _local = PlayerNetwork.Local;
                    _level = session.level;
                    _director = EntityDirector.Instance;
                    if (_local == null || _level == null || _level.Data == null || OfficeElevators.Instance == null) break;
                    if (InStage < 1.5f) break;

                    if (_director == null) { Finish(false, "o Escritório não tem o diretor do inimigo"); return; }
                    if (_director.entityPrefab == null || _director.entityPrefab.GetComponent<AttendantBrain>() == null)
                    {
                        Finish(false, "o inimigo do diretor do Level 3 não é o Ascensorista (a entidade do Level 0 ainda está na fase?)");
                        return;
                    }

                    _backgroundBefore = InputSystem.settings.backgroundBehavior;
                    _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    _keyboard = InputSystem.AddDevice<Keyboard>();
                    Press();

                    // Os armários que batem (outro barulho do mundo) desviariam o caminho dele no meio das
                    // conferências: aqui ficam calados (têm teste próprio).
                    if (LockerBangs.Instance != null) LockerBangs.Instance.enabled = false;

                    // Aparece num corredor qualquer e logo se esconde num poço.
                    Vector3 somewhere = _level.CellCenter(_level.Data.ExitX, _level.Data.ExitZ);
                    _brain = _director.SpawnAt(somewhere, Vector3.forward) as AttendantBrain;
                    if (_brain == null) { Finish(false, "o diretor não pôs o Ascensorista"); return; }
                    _body = _brain.GetComponent<AttendantBody>();
                    _brain.staySeconds = 600f;
                    ToStart();
                    Next();
                    break;
                }

                case 2:
                {
                    if (_stageTicks < 3) break;
                    if (_brain.Phase != AttendantPhase.Away) { Finish(false, $"posto no mapa, não foi se esconder num poço ({_brain.Phase})"); return; }
                    if (_body.Shown) { Finish(false, "escondido no poço, o corpo continua à vista"); return; }
                    if (!_brain.InCab(_brain.transform.position, _brain.Shaft)) { Finish(false, "fora do andar, não está dentro de uma cabine de poço"); return; }
                    if (_brain.Shafts.Count < 1) { Finish(false, "o saguão não tem poço"); return; }

                    _expected = AttendantBrain.ShaftFor(_level.Data.Seed, 0, _brain.Shafts.Count);
                    _other = _brain.Shafts.Count > 1 ? (_expected + 1) % _brain.Shafts.Count : _expected;
                    EntityDirector.Paused = false;
                    _scheduled = Clock + 3.0;
                    _brain.ScheduleArrival(_scheduled);
                    Next($"o diretor pôs o Ascensorista, escondido no poço {_brain.Shaft} de {_brain.Shafts.Count}; chegada marcada para {_scheduled:F1} s (poço da seed: {_expected})");
                    break;
                }

                case 3:
                {
                    // 2. Chega na hora, pelo poço da seed: seta piscando com a porta fechada, plim, sai.
                    ElevatorDoors door = _brain.Shafts[_expected];
                    AttendantPhase phase = _brain.Phase;
                    if (phase == AttendantPhase.Away)
                    {
                        if (Clock > _scheduled + 1.0) { Finish(false, $"passou da hora marcada ({_scheduled:F1} s) e não veio (relógio {Clock:F1})"); return; }
                        break;
                    }
                    if (_sub == 0)
                    {
                        if (Clock < _scheduled - 0.2) { Finish(false, $"chegou antes da hora ({Clock:F1} < {_scheduled:F1})"); return; }
                        if (_brain.Shaft != _expected) { Finish(false, $"chegou pelo poço {_brain.Shaft}, não pelo da seed ({_expected})"); return; }
                        _sub = 1;
                        _subStart = Now;
                    }
                    if (phase == AttendantPhase.Coming)
                    {
                        if (_brain.IndicatorLit(_expected)) _sawBlink = true;
                        if (door.Openness < 0.05f) _sawClosedComing = true;
                        else { Finish(false, "a porta abriu enquanto a seta ainda piscava"); return; }
                        if (_body.Shown) { Finish(false, "o corpo apareceu antes da porta abrir"); return; }
                        break;
                    }
                    if (phase == AttendantPhase.Arriving)
                    {
                        if (!_sawBlink || !_sawClosedComing) { Finish(false, $"antes do plim: seta acesa={_sawBlink}, porta fechada={_sawClosedComing}"); return; }
                        if (Now - _subStart > 12f) { Finish(false, "não saiu da cabine em 12 s"); return; }
                        break;
                    }
                    if (phase != AttendantPhase.Out) { Finish(false, $"depois de chegar, foi para {phase}"); return; }
                    if (door.Openness < 0.9f) { Finish(false, $"saiu, mas a porta da cabine não está aberta ({door.Openness:F2})"); return; }
                    if (!_body.Shown) { Finish(false, "saiu, mas o corpo não aparece"); return; }
                    float off = Flat(_brain.transform.position - _brain.FrontPoint(_expected)).magnitude;
                    if (off > 0.8f) { Finish(false, $"saiu da cabine, mas parou a {off:F1} m da frente da porta"); return; }
                    _mark = _brain.transform.position;
                    Next($"chegou na hora ({Clock - _scheduled:F1} s depois do marcado) pelo poço da seed: a seta piscou com a porta fechada, a porta abriu e ele saiu para o saguão");
                    break;
                }

                case 4:
                {
                    // 3. Ronda, parando para escutar; acabado o tempo, volta e parte.
                    if (_brain.Listening) _sawListen = true;
                    if (_brain.Shafts[_expected].Openness < 0.9f) { Finish(false, "com ele no andar, a porta da cabine dele fechou"); return; }
                    float moved = Flat(_brain.transform.position - _mark).magnitude;
                    if (moved >= 3f && _sawListen && InStage > 3f)
                    {
                        _brain.EndStayNow();
                        Next($"rondou ({moved:F1} m em {InStage:F1} s, parando para escutar)");
                        break;
                    }
                    if (InStage > 20f) Finish(false, $"no andar, andou {moved:F1} m e escutou={_sawListen} em 20 s");
                    break;
                }

                case 5:
                {
                    AttendantPhase phase = _brain.Phase;
                    if (phase == AttendantPhase.Returning) _sawReturning = true;
                    if (phase == AttendantPhase.Boarding) _sawBoarding = true;
                    if (phase == AttendantPhase.Leaving) _sawLeaving = true;
                    if (phase == AttendantPhase.Out && InStage > 2f) { Finish(false, "acabou o tempo e ele continuou rondando"); return; }
                    if (phase != AttendantPhase.Away)
                    {
                        if (InStage > 60f) Finish(false, $"acabou o tempo e não partiu em 60 s");
                        break;
                    }
                    if (_sub == 0) { _sub = 1; _subStart = Now; }
                    // O corpo some no quadro seguinte ao da mudança de fase.
                    if (Now - _subStart < 0.3f) break;
                    if (_brain.Shafts[_expected].Openness > 0.05f)
                    {
                        if (Now - _subStart > 2.5f) _sawOpenWhileAway = true;
                        else break;
                    }
                    if (_sawOpenWhileAway) { Finish(false, "partiu e a porta da cabine ficou aberta"); return; }
                    if (!_sawReturning || !_sawBoarding || !_sawLeaving) { Finish(false, $"partiu sem passar por todas as etapas (voltando={_sawReturning}, entrando={_sawBoarding}, saindo={_sawLeaving})"); return; }
                    if (_body.Shown) { Finish(false, "partiu e o corpo continua à vista"); return; }
                    if (_brain.NextArrival <= Clock) { Finish(false, "partiu sem marcar a próxima chegada"); return; }
                    Next($"acabado o tempo voltou à cabine, a porta fechou e ele sumiu (próxima chegada daqui a {_brain.NextArrival - Clock:F0} s)");
                    _brain.ForceArrival(_expected);
                    break;
                }

                case 6:
                {
                    // 4. Agachado e parado a 4 m do caminho: ele passa.
                    if (_sub == 0)
                    {
                        if (_brain.Phase != AttendantPhase.Out) { if (InStage > 25f) Finish(false, "a segunda chegada não saiu da cabine"); break; }
                        if (!Lobby(out Vector3 a, out Vector3 b, out Vector3 aside)) { Finish(false, "não achei a fileira do saguão"); return; }
                        _local.controller.Teleport(aside, 0f);
                        Press(Key.LeftCtrl);
                        _director.SpawnAt(a, b - a);
                        _brain.Alert(b);
                        _spot = b;
                        _heardBefore = _brain.NoisesHeard;
                        _takesBefore = _brain.Takes;
                        _closest = float.MaxValue;
                        _sub = 1;
                        _subStart = Now;
                        break;
                    }
                    Press(Key.LeftCtrl);
                    _closest = Mathf.Min(_closest, Flat(_brain.transform.position - _local.transform.position).magnitude);
                    if (_brain.Takes != _takesBefore || _local.controller.IsReturning) { Finish(false, $"agachado e parado a {_closest:F1} m do caminho, foi pego"); return; }
                    if (_brain.NoisesHeard != _heardBefore) { Finish(false, $"agachado e parado a {_closest:F1} m do caminho, ele ouviu"); return; }
                    if (Flat(_brain.transform.position - _spot).magnitude < 1f)
                    {
                        if (_closest > 5f || _closest < 3f) { Finish(false, $"o caminho dele passou a {_closest:F1} m do jogador, não a uns 4 m"); return; }
                        Next($"agachado e parado a {_closest:F1} m do caminho, ele passou sem ouvir");
                        break;
                    }
                    if (Now - _subStart > 12f) Finish(false, "mandado atravessar o saguão, não chegou em 12 s" + Status());
                    break;
                }

                case 7:
                {
                    // 5. Correndo a uns 10 m: ele ouve e vem. Depois, agachado parado no caminho: ouve a respiração e pega.
                    if (_sub == 0)
                    {
                        Press();
                        if (InStage < 3f) break;
                        if (!RunSpot(out Vector3 monster, out Vector3 player, out Vector3 axis)) { Finish(false, "não achei dois pontos a uns 10 m com o som livre"); return; }
                        _director.SpawnAt(monster, player - monster);
                        _brain.Alert(monster);
                        _local.controller.Teleport(player, 0f);
                        _spot = player;
                        _spotAxis = axis;
                        _mark = monster;
                        _heardBefore = _brain.NoisesHeard;
                        _sub = 1;
                        _subStart = Now;
                        break;
                    }
                    if (_sub == 1)
                    {
                        // Correndo no lugar, de um lado para o outro, a 5 m/s.
                        Jiggle(5f, 1.2f);
                        bool heard = _brain.NoisesHeard > _heardBefore && _brain.State == EntityState.Investigate && Flat(_brain.Goal - _spot).magnitude < 3f;
                        if (heard)
                        {
                            _local.controller.Teleport(_spot, 0f);
                            Press(Key.LeftCtrl);
                            _capturesBefore = _local.controller.Captures;
                            _takesBefore = _brain.Takes;
                            _sawTaken = _sawInCab = _sawDoorShut = false;
                            Notes.Add($"correndo a {Flat(_mark - _spot).magnitude:F1} m, ele ouviu em {Now - _subStart:F1} s");
                            _sub = 2;
                            _subStart = Now;
                            break;
                        }
                        if (Now - _subStart > 4f) Finish(false, $"correndo a {Flat(_mark - _spot).magnitude:F1} m, ele não ouviu em 4 s" + Status());
                        break;
                    }
                    // Agachado e parado onde ele vai: a respiração o denuncia; é levado para a cabine.
                    Press(Key.LeftCtrl);
                    if (WatchTaken(out string why))
                    {
                        Press();
                        Next($"veio até o barulho ({Flat(_mark - _spot).magnitude:F1} m) e pegou o jogador agachado e parado no caminho dele; {why}");
                        break;
                    }
                    if (why != null) { Finish(false, why); return; }
                    if (Now - _subStart > 25f) Finish(false, "ouviu a corrida, mas não pegou o jogador agachado no caminho em 25 s" + Status());
                    break;
                }

                case 8:
                {
                    // 7. Salinha acesa: entra atrás do barulho e pega.
                    if (_sub == 0)
                    {
                        if (InStage < 9f || _local.controller.IsReturning) break; // trégua de quem acabou de ser pego
                        if (!SmallRoom(out Vector3 inside, out Vector3 outside)) { Finish(false, "a seed não tem salinha acesa com porta para o corredor"); return; }
                        _director.SpawnAt(outside, inside - outside);
                        _brain.Alert(outside);
                        _local.controller.Teleport(inside, 0f);
                        _spot = inside;
                        _spotAxis = Vector3.right;
                        _mark = outside;
                        _takesBefore = _brain.Takes;
                        _capturesBefore = _local.controller.Captures;
                        _sawTaken = _sawInCab = _sawDoorShut = false;
                        _sub = 1;
                        _subStart = Now;
                        break;
                    }
                    if (_sub == 1)
                    {
                        // Andando dentro da salinha, de um lado para o outro (barulho de passo).
                        Jiggle(2.4f, 0.7f);
                        if (_level.Data.IsShelter(Cell(_brain.transform.position).x, Cell(_brain.transform.position).y))
                        {
                            Notes.Add($"entrou na salinha acesa ({_room}) atrás do barulho de passos, em {Now - _subStart:F1} s");
                            _local.controller.Teleport(_spot, 0f);
                            _sub = 2;
                            break;
                        }
                        if (Now - _subStart > 20f) Finish(false, "o jogador andava na salinha acesa e ele não entrou em 20 s" + Status());
                        break;
                    }
                    if (WatchTaken(out string why))
                    {
                        Next("pegou o jogador dentro da salinha acesa; " + why);
                        break;
                    }
                    if (why != null) { Finish(false, why); return; }
                    if (Now - _subStart > 30f) Finish(false, "entrou na salinha e não pegou o jogador" + Status());
                    break;
                }

                case 9:
                {
                    // 8. O alarme de incêndio o chama ao acionador.
                    SwitchObjective objective = SwitchObjective.Instance;
                    if (objective == null || objective.Count < 2) { Finish(false, "o Escritório não tem os acionadores do alarme"); return; }
                    if (_sub == 0)
                    {
                        if (InStage < 9f || _local.controller.IsReturning) break;
                        ToStart();
                        _director.SpawnAt(_brain.FrontPoint(_brain.Shaft), Vector3.forward);
                        _heardBefore = _brain.WorldNoisesHeard;
                        int emitted = WorldNoise.Emitted;
                        objective.HostArm(0, null);
                        objective.HostArm(1, null);
                        if (!objective.Complete) { Finish(false, "dois acionadores armados e o alarme não disparou"); return; }
                        if (WorldNoise.Emitted == emitted) { Finish(false, "o alarme disparou sem barulho no mundo (WorldNoise)"); return; }
                        _spot = WorldNoise.LastAt;
                        _mark = _brain.transform.position;
                        _sub = 1;
                        _subStart = Now;
                        break;
                    }
                    if (_sub == 1)
                    {
                        if (_brain.WorldNoisesHeard == _heardBefore) { Finish(false, "o alarme disparou e ele não ouviu" + Status()); return; }
                        if (Flat(_brain.Goal - _spot).magnitude > 4.5f) { Finish(false, $"ouviu o alarme, mas vai para outro lugar (destino a {Flat(_brain.Goal - _spot).magnitude:F1} m do acionador)"); return; }
                        float before = Flat(_mark - _spot).magnitude, now = Flat(_brain.transform.position - _spot).magnitude;
                        if (before - now >= 3f || now < 2.5f)
                        {
                            Notes.Add($"o alarme o chamou: andou {before - now:F1} m na direção do acionador");
                            _brain.EndStayNow();
                            _sub = 2;
                            _subStart = Now;
                            break;
                        }
                        if (Now - _subStart > 15f) Finish(false, "ouviu o alarme e não foi até o acionador em 15 s" + Status());
                        break;
                    }
                    if (_sub == 2)
                    {
                        // Fora do andar: a sirene o faz chegar logo e ir direto ao acionador.
                        if (_brain.Phase != AttendantPhase.Away) { if (Now - _subStart > 60f) Finish(false, "mandado embora, não partiu"); break; }
                        _brain.ScheduleArrival(Clock + 120.0);
                        _brain.HearWorldNoise(_spot, SwitchObjective.CompleteNoiseRadius);
                        if (_brain.NextArrival > Clock + 6.5) { Finish(false, $"fora do andar, a sirene não adiantou a chegada (daqui a {_brain.NextArrival - Clock:F0} s)"); return; }
                        _sub = 3;
                        _subStart = Now;
                        break;
                    }
                    if (_brain.Phase != AttendantPhase.Out) { if (Now - _subStart > 25f) Finish(false, "chamado pela sirene, não saiu da cabine" + Status()); break; }
                    if (Flat(_brain.Goal - _spot).magnitude > 4.5f) { Finish(false, "chamado pela sirene, saiu da cabine e não foi para o acionador" + Status()); return; }
                    Next("fora do andar, a sirene adiantou a chegada e ele saiu da cabine direto para o acionador");
                    break;
                }

                case 10:
                {
                    // 9. Trancado numa cabine de poço: lá fora ele não ouve; mas se chega por ela, leva.
                    if (_brain.Shafts.Count < 2) { Next("só um poço: sem o teste da cabine trancada"); break; }
                    if (_sub == 0) _cabUsed = (_brain.Shaft + 1) % _brain.Shafts.Count;
                    int cab = _cabUsed;
                    ElevatorDoors door = _brain.Shafts[cab];
                    if (_sub == 0)
                    {
                        if (_brain.Phase != AttendantPhase.Out) break;
                        ElevatorControl control = ElevatorControl.Instance;
                        if (control == null) { Finish(false, "o Escritório não tem os botões dos elevadores"); return; }
                        Vector3 inside = _brain.CabPoint(cab);
                        _local.controller.Teleport(inside, 0f);
                        int closer = -1;
                        for (int k = 0; k < control.Buttons.Count; k++)
                            if (control.Buttons[k].Door == door && control.Buttons[k].Kind == ElevatorButtonKind.Close) closer = k;
                        if (closer < 0) { Finish(false, "a cabine do poço não tem botão de fechar"); return; }
                        control.HostPress(closer, control.Buttons[closer].transform.position);
                        _spot = inside;
                        _spotAxis = Vector3.right;
                        _sub = 1;
                        _subStart = Now;
                        break;
                    }
                    if (_sub == 1)
                    {
                        if (door.Openness > 0.02f) { if (Now - _subStart > 5f) Finish(false, $"o botão de fechar não fechou a porta da cabine ({door.Openness:F2})"); break; }
                        _director.SpawnAt(_brain.FrontPoint(cab), _brain.FrontPoint(cab) - _brain.CabPoint(cab));
                        _brain.Alert(_brain.FrontPoint(cab));
                        _heardBefore = _brain.NoisesHeard;
                        _takesBefore = _brain.Takes;
                        _sub = 2;
                        _subStart = Now;
                        break;
                    }
                    if (_sub == 2)
                    {
                        Jiggle(5f, 0.5f);
                        if (_brain.NoisesHeard != _heardBefore || _brain.Takes != _takesBefore) { Finish(false, "trancado na cabine e correndo, ele ouviu (ou pegou) do lado de fora" + Status()); return; }
                        if (Now - _subStart < 6f) break;
                        _local.controller.Teleport(_spot, 0f);
                        _brain.EndStayNow();
                        Notes.Add("trancado numa cabine de poço, correndo, ele ficou 6 s na porta sem ouvir");
                        _sub = 3;
                        _subStart = Now;
                        break;
                    }
                    if (_sub == 3)
                    {
                        if (_brain.Phase != AttendantPhase.Away) { if (Now - _subStart > 60f) Finish(false, "mandado embora, não partiu" + Status()); break; }
                        if (door.Openness > 0.02f) { Finish(false, "a cabine trancada abriu sozinha"); return; }
                        _capturesBefore = _local.controller.Captures;
                        _takesBefore = _brain.Takes;
                        _sawTaken = _sawInCab = _sawDoorShut = false;
                        _brain.ForceArrival(cab);
                        if (_brain.Shaft != cab) { Finish(false, "não aceitou chegar pela cabine trancada com o jogador dentro"); return; }
                        _sub = 4;
                        _subStart = Now;
                        break;
                    }
                    if (WatchTaken(out string why))
                    {
                        Next("ao chegar pela cabine em que o jogador se trancou, levou-o; " + why);
                        break;
                    }
                    if (why != null) { Finish(false, why); return; }
                    if (Now - _subStart > 30f) Finish(false, "chegou pela cabine trancada e não levou quem estava dentro" + Status());
                    break;
                }

                case 11:
                {
                    // Depois de levar quem estava trancado, a porta reabre e ele sai para o andar.
                    if (_brain.Phase != AttendantPhase.Out)
                    {
                        if (InStage > 20f) Finish(false, "depois de levar quem estava na cabine, não saiu dela em 20 s" + Status());
                        break;
                    }
                    if (Role != HostRole)
                    {
                        Finish(true, string.Join("; ", Notes) + $"; chegadas {_brain.Arrivals}, levados {_brain.Takes}");
                        return;
                    }
                    // Dois editores: pega o cliente, que foi para longe e espera parado.
                    PlayerNetwork remote = Remote();
                    if (remote == null)
                    {
                        // Depois de pego, o cliente sai sozinho quando termina a conferência dele.
                        if (_sub == 2) Finish(true, string.Join("; ", Notes) + $"; o cliente terminou e saiu; chegadas {_brain.Arrivals}, levados {_brain.Takes}");
                        else Finish(false, "o cliente saiu da sessão");
                        return;
                    }
                    if (_sub == 0)
                    {
                        if (_brain.Phase != AttendantPhase.Out || _local.controller.IsReturning) break;
                        if (!_level.Data.InRoom(Cell(remote.transform.position).x, Cell(remote.transform.position).y) && InStage > 3f)
                        {
                            Vector3 at = remote.transform.position;
                            Vector2Int c = Cell(at);
                            Vector3 from = Neighbor(c, at);
                            _director.SpawnAt(from, at - from);
                            _brain.Alert(at);
                            _takesBefore = _brain.Takes;
                            _sub = 1;
                            _subStart = Now;
                        }
                        break;
                    }
                    if (_sub == 1 && _brain.Takes > _takesBefore)
                    {
                        Notes.Add("pegou o cliente e o levou para a cabine (host)");
                        _sub = 2;
                        _subStart = Now;
                        break;
                    }
                    if (_sub == 2)
                    {
                        // O cliente confere do lado dele (a cabine, a porta, o sinal, a volta ao início) e sai
                        // da sessão; o host acaba quando o vê de volta na cabine do início.
                        Vector2Int back = Cell(remote.transform.position);
                        if (_level.Data.InRoom(back.x, back.y) && Now - _subStart > 3f)
                        {
                            Finish(true, string.Join("; ", Notes) + $"; o cliente voltou ao início; chegadas {_brain.Arrivals}, levados {_brain.Takes}");
                            return;
                        }
                        if (Now - _subStart > 20f) Finish(false, "pegou o cliente, mas ele não voltou ao início em 20 s");
                        break;
                    }
                    if (Now - _subStart > 20f) Finish(false, "não pegou o cliente em 20 s" + Status());
                    break;
                }
            }
        }

        // Vê a captura do jogador local: aparece na cabine dele, a porta fecha, cai o sinal e volta ao início.
        static bool WatchTaken(out string why)
        {
            why = null;
            PlayerController c = _local.controller;
            int shaft = _brain.Shaft;
            if (shaft < 0 || shaft >= _brain.Shafts.Count) return false;
            if (c.IsTaken)
            {
                _sawTaken = true;
                if (_brain.InCab(_local.transform.position, shaft)) _sawInCab = true;
                if (_brain.Shafts[shaft].Openness < 0.1f) _sawDoorShut = true;
                if (c.Captures != _capturesBefore) { why = "o sinal caiu antes de terminar a ida para a cabine"; return false; }
                return false;
            }
            if (!_sawTaken)
            {
                if (c.Captures != _capturesBefore) why = "foi pego do jeito comum, sem ser levado para a cabine";
                return false;
            }
            if (!_sawInCab) { why = "levado, mas não apareceu dentro da cabine dele"; return false; }
            if (!_sawDoorShut) { why = "levado para a cabine, mas a porta não fechou com ele dentro"; return false; }
            if (c.Captures == _capturesBefore) { why = "saiu da cabine sem cair o sinal"; return false; }
            if (c.IsReturning) return false;
            Vector2Int cell = Cell(_local.transform.position);
            if (!_level.Data.InRoom(cell.x, cell.y)) { why = "depois do sinal, não voltou para a cabine do início"; return false; }
            why = "apareceu na cabine dele, a porta fechou, caiu o sinal e voltou ao início";
            return true;
        }

        // Cliente: entra, vê a chegada (seta, porta, corpo), vai para longe e espera ser pego.
        static bool _joinSawLit, _joinSawOpen, _joinSawShown, _joinChecked;
        static int _joinComings;
        static AttendantPhase _joinPhase;
        static Vector3 _joinSeen;
        static float _joinFarthest;

        static void TickJoin(GameSession session)
        {
            if (_stage > 0 && session.Phase != SessionPhase.Playing)
            {
                Finish(false, "a sessão acabou antes" + $" (seta={_joinSawLit}, porta={_joinSawOpen}, corpo={_joinSawShown}, andou {_joinFarthest:F1} m, chegadas vistas {_joinComings}, levado={_sawTaken})");
                return;
            }

            switch (_stage)
            {
                case 0:
                    if (session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null) { _local = PlayerNetwork.Local; _level = session.level; Next(); break; }
                    if (session.Phase == SessionPhase.Menu && EditorApplication.timeSinceStartup - _lastTry > 4.0)
                    {
                        _lastTry = EditorApplication.timeSinceStartup;
                        session.Join("127.0.0.1");
                    }
                    break;

                case 1:
                {
                    AttendantBrain brain = AttendantBrain.Attendant;
                    if (brain == null || !brain.IsSpawned) break;
                    if (_brain == null)
                    {
                        _brain = brain;
                        _body = brain.GetComponent<AttendantBody>();
                        Notes.Add("o Ascensorista apareceu no cliente");
                    }
                    int shaft = brain.Shaft;
                    if (brain.Phase != _joinPhase)
                    {
                        if (brain.Phase == AttendantPhase.Coming) _joinComings++;
                        _joinPhase = brain.Phase;
                    }
                    if (brain.Phase == AttendantPhase.Coming && brain.IndicatorLit(shaft)) _joinSawLit = true;
                    if (brain.Phase == AttendantPhase.Out && shaft >= 0)
                    {
                        if (brain.Shafts[shaft].Openness > 0.9f) _joinSawOpen = true;
                        if (_body.Shown)
                        {
                            if (!_joinSawShown) _joinSeen = brain.transform.position;
                            _joinSawShown = true;
                            _joinFarthest = Mathf.Max(_joinFarthest, Flat(brain.transform.position - _joinSeen).magnitude);
                        }
                    }
                    if (_joinSawLit && _joinSawOpen && _joinSawShown && _joinFarthest >= 2f && !_joinChecked)
                    {
                        _joinChecked = true;
                        Notes.Add($"no cliente: a seta acendeu, a porta abriu, ele apareceu e andou {_joinFarthest:F1} m");
                    }
                    // O cliente espera na cabine do início (abrigo) enquanto o host confere as regras dele
                    // (quatro chegadas); depois da quarta, sai para ser pego.
                    if (_joinChecked && _joinComings >= 4 && brain.Phase == AttendantPhase.Out)
                    {
                        // Vai para o lugar mais longe do saguão e espera parado.
                        _local.controller.Teleport(FarSpot(), 0f);
                        _capturesBefore = _local.controller.Captures;
                        _sawTaken = _sawInCab = _sawDoorShut = false;
                        Next();
                    }
                    break;
                }

                case 2:
                    if (WatchTaken(out string why))
                    {
                        Finish(true, string.Join("; ", Notes) + "; no cliente, pego: " + why);
                        return;
                    }
                    if (why != null) { Finish(false, "no cliente: " + why); return; }
                    break;
            }

        }

        // ---------------------------------------------------------------- Lugares

        static void ToStart()
        {
            GameSession session = GameSession.Instance;
            _local.controller.Teleport(session.SpawnPoint(_local.OwnerClientId), 0f);
        }

        static Vector2Int Cell(Vector3 world)
        {
            Vector3 local = _level.transform.InverseTransformPoint(world);
            return new Vector2Int(Mathf.FloorToInt(local.x / LevelBuilder.Cell), Mathf.FloorToInt(local.z / LevelBuilder.Cell));
        }

        // A fileira do saguão encostada nos elevadores: a frente de um poço (a), a do outro (b), e um
        // ponto na fileira de trás, no meio, a 4 m do caminho de a para b (aside).
        static bool Lobby(out Vector3 a, out Vector3 b, out Vector3 aside)
        {
            a = b = aside = Vector3.zero;
            if (_brain.Shafts.Count < 2) return false;
            a = _brain.FrontPoint(0);
            b = _brain.FrontPoint(_brain.Shafts.Count - 1);
            Vector3 mid = (a + b) * 0.5f;
            Vector3 toLobby = Flat(_brain.FrontPoint(0) - _brain.CabPoint(0)).normalized;
            aside = mid + toLobby * LevelBuilder.Cell;
            aside.y = _local.transform.position.y;
            return true;
        }

        // Dois pontos a 10–12,5 m em linha reta, o som chegando em até 3 células (paredes barram), o
        // jogador fora de qualquer abrigo e sem móvel no lugar (ele corre ali, de um lado para o outro).
        static bool RunSpot(out Vector3 monster, out Vector3 player, out Vector3 axis)
        {
            monster = player = axis = Vector3.zero;
            var nav = new LevelNav(_level) { OnlyStartRoomShelter = true };
            LevelData d = _level.Data;
            int[,] sound = nav.NewMap();
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (!nav.Standable(x, z) || d.Office[x, z] != OfficeCell.Corridor) continue;
                    nav.SoundDistances(x, z, 4, sound);
                    for (int pz = 0; pz < d.Height; pz++)
                        for (int px = 0; px < d.Width; px++)
                        {
                            if (sound[px, pz] < 2 || sound[px, pz] > 3 || !nav.Standable(px, pz) || d.Office[px, pz] != OfficeCell.Corridor) continue;
                            Vector3 m = nav.CellCenter(x, z), p = nav.CellCenter(px, pz);
                            float straight = Flat(m - p).magnitude;
                            if (straight < 10f || straight > 12.5f) continue;
                            if (!FreeLine(p, Vector3.right, 1.4f) && !FreeLine(p, Vector3.forward, 1.4f)) continue;
                            monster = m;
                            player = new Vector3(p.x, _local.transform.position.y, p.z);
                            axis = FreeLine(p, Vector3.right, 1.4f) ? Vector3.right : Vector3.forward;
                            return true;
                        }
                }
            return false;
        }

        static bool FreeLine(Vector3 center, Vector3 axis, float half)
        {
            return !Physics.CheckCapsule(center - axis * half + Vector3.up * 0.5f, center + axis * half + Vector3.up * 0.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore) &&
                   !Physics.CheckCapsule(center - axis * half + Vector3.up * 1.4f, center + axis * half + Vector3.up * 1.4f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
        }

        // Uma salinha acesa (abrigo do Zelador) com porta para um corredor: o meio dela e a célula do
        // corredor uma além da porta.
        static bool SmallRoom(out Vector3 inside, out Vector3 outside)
        {
            inside = outside = Vector3.zero;
            LevelData d = _level.Data;
            var sides = new[] { WallSide.North, WallSide.South, WallSide.East, WallSide.West };
            foreach (OfficeRoom room in d.OfficeRooms)
            {
                if (room.Kind != OfficeCell.Small || !room.Lit) continue;
                for (int z = room.Z0; z < room.Z0 + room.Height; z++)
                    for (int x = room.X0; x < room.X0 + room.Width; x++)
                        foreach (WallSide side in sides)
                        {
                            if (d.OfficeEdgeAt(x, z, side) != OfficeEdge.Door) continue;
                            if (!d.Neighbor(x, z, side, out int nx, out int nz) || d.Office[nx, nz] != OfficeCell.Corridor) continue;
                            _room = new Vector2Int(x, z);
                            Vector3 c = _level.CellCenter(x, z);
                            inside = new Vector3(c.x, _local.transform.position.y, c.z);
                            outside = _level.CellCenter(nx, nz);
                            // Uma célula mais longe pelo corredor, se houver.
                            foreach (WallSide on in sides)
                                if (d.CanWalk(nx, nz, on, false) && d.Neighbor(nx, nz, on, out int fx, out int fz) && d.Office[fx, fz] == OfficeCell.Corridor && !(fx == x && fz == z))
                                {
                                    outside = _level.CellCenter(fx, fz);
                                    break;
                                }
                            return true;
                        }
            }
            return false;
        }

        // A célula do mapa mais longe da porta da cabine, andando (onde o cliente espera).
        static Vector3 FarSpot()
        {
            var nav = new LevelNav(_level) { OnlyStartRoomShelter = true };
            int[,] reach = nav.NewMap();
            if (!nav.DoorCell(out int dx, out int dz)) return _local.transform.position;
            nav.Distances(dx, dz, reach);
            int best = -1, bx = dx, bz = dz;
            LevelData d = _level.Data;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    if (reach[x, z] > best && d.Office[x, z] == OfficeCell.Corridor) { best = reach[x, z]; bx = x; bz = z; }
            Vector3 c = nav.CellCenter(bx, bz);
            return new Vector3(c.x, _local.transform.position.y, c.z);
        }

        // Centro de uma célula vizinha (andável) de onde o Ascensorista vem até o ponto.
        static Vector3 Neighbor(Vector2Int c, Vector3 at)
        {
            var nav = new LevelNav(_level) { OnlyStartRoomShelter = true };
            foreach (WallSide side in new[] { WallSide.North, WallSide.South, WallSide.East, WallSide.West })
                if (nav.CanStep(c.x, c.y, side) && _level.Data.Neighbor(c.x, c.y, side, out int nx, out int nz))
                    return nav.CellCenter(nx, nz);
            return at + Vector3.forward * 3f;
        }

        static PlayerNetwork Remote()
        {
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.InGame && player != _local) return player;
            return null;
        }

        static int CountInGame()
        {
            int count = 0;
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.InGame) count++;
            return count;
        }

        // O jogador vai e volta em volta de _spot ao longo de _spotAxis, na velocidade dada (o host
        // mede o barulho pelo deslocamento).
        static void Jiggle(float speed, float half)
        {
            float t = (Now - _subStart) * speed / (half * 2f);
            float u = Mathf.PingPong(t, 2f) - 1f;
            _local.controller.Teleport(_spot + _spotAxis * (u * half), _local.transform.eulerAngles.y);
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
            ReleaseKeyboard();
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log("[BackRooms] Teste do Ascensorista " + (ok ? "PASSOU" : "FALHOU") + $" ({Role}): " + report);

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
