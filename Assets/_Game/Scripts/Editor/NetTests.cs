using System.Collections.Generic;
using BackRooms.Entities;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Testes em dois editores das Piscinas (Level 2) e do Escritório (Level 3): o host manda pela
    /// sonda dos testes (<see cref="NetProbe"/>) a hora da sessão, os hashes da planta, dos pontos do
    /// objetivo e das pilhas, as pilhas pegas, a altura da água, a porta e a etapa; o cliente compara
    /// com o que ele mesmo montou e vê, no mesmo instante. Em ordem:
    ///
    /// 1. <b>Planta:</b> mesmo hash do gerador (<see cref="PoolGenerator.Hash"/> ou
    ///    <see cref="OfficeGenerator.Hash"/>), mesmos pontos do objetivo e mesmas pilhas, e o relógio da
    ///    sessão igual nas duas máquinas.
    /// 2. <b>Maré</b> (Level 2): espera a primeira subida, a maré alta e parte da vazante (<see cref="PoolTide.FirstEbb"/>) e confere que a
    ///    altura da água é a mesma no host e no cliente no mesmo instante.
    /// 3. <b>Pilhas:</b> o host pega a 0, o cliente a 1; cada uma some na outra máquina e só quem pegou
    ///    recarrega.
    /// 4. <b>Quem entra depois:</b> o cliente sai e entra de novo; as pilhas pegas continuam sumidas e os
    ///    hashes batem.
    /// 4b. <b>Interruptor pelo cliente</b> (Level 3): o host apaga um trecho da onda; o cliente aperta o
    ///    interruptor dele (pedido por RPC ao host), o host religa e o interruptor recarrega nas duas
    ///    máquinas. O cliente sai; com ele fora, o host apaga outro trecho; o cliente volta e acha o
    ///    interruptor recarregando (a mira diz RESETTING), o trecho religado aceso e o outro trecho com
    ///    a mesma passagem da onda que o host (e apagado, se voltou dentro da janela do escuro).
    /// 5. <b>Objetivo:</b> o host cumpre (<see cref="SwitchObjective.CompleteForTest"/>); o cliente vê a
    ///    porta destrancar e, no Level 2, a água descer (drenagem) e, no Level 3, a sirene dos alarmes.
    /// 6. <b>Quem entra depois de cumprido:</b> o cliente sai e entra de novo e acha a porta destrancada
    ///    e, no Level 2, a água já baixa.
    ///
    /// Linha de comando (fila): <c>-Metodo ...NetTests.BatchHostLevel2 -MetodoCopia ...NetTests.BatchJoinLevel2</c>
    /// (e Level3). Linha "[BackRooms] Teste de rede (Level N, host|cliente) PASSOU/FALHOU".
    /// </summary>
    public static class NetTests
    {
        const string ActiveKey = "BackRooms.NetTests.Active";
        const string ExitCodeKey = "BackRooms.NetTests.ExitCode";
        const string RoleKey = "BackRooms.NetTests.Role";
        const string LevelKey = "BackRooms.NetTests.Level";
        const string HostRole = "host", JoinRole = "join";
        const double Timeout = 480.0;

        // Etapas que o host anuncia na sonda.
        const int StagePlan = 1, StagePickups = 2, StageRejoin = 3, StageSwitch = 4, StageSwitchRejoin = 5,
                  StageComplete = 6, StageRejoinDone = 7;
        const string SwitchReadyText = "E - LIGHTS ON";

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks;
        static double _began, _probeAt;
        static float _stageStart;
        static bool _finished;
        static PlayerNetwork _local;
        static LevelBuilder _level;

        // Host.
        static int _clientsSeen;
        static bool _clientLeft;
        static int _leaves;
        static float _tideSeenMax;
        static JanitorBrain _janitor;
        static int _switchRun = -1, _otherRun = -1, _flips0, _requests0;
        static float _otherStart = -1f;
        static double _switchWaveAt;

        // Cliente.
        static int _hostStage;
        static int _hashChecks, _hashMismatch;
        static string _hashNote = "";
        static int _tideChecks;
        static float _tideWorst, _tideHostMax;
        static int _joins;
        static bool _pickedOne, _sawHostPick, _rejoinPickupsOk, _sawUnlocked, _sawSiren, _sawDrop, _afterOk;
        static float _waterLow = float.MaxValue;
        static double _lastTry;
        static float _playingSince = -1f;
        static string _afterNote = "";
        // Cliente, interruptor (Level 3).
        static int _hostSwitchRun = -1, _hostOtherRun = -1, _clacks0;
        static float _hostOtherStart = -1f, _pressedAt = -1f;
        static bool _standing, _clientFlipped, _switchRejoinOk;

        public static void BatchHostLevel2() { Run(HostRole, 2); }
        public static void BatchJoinLevel2() { Run(JoinRole, 2); }
        public static void BatchHostLevel3() { Run(HostRole, 3); }
        public static void BatchJoinLevel3() { Run(JoinRole, 3); }

        static void Run(string role, int level)
        {
            EditorSceneManager.OpenScene(level == 2 ? Level2Setup.ScenePath : Level3Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(RoleKey, role);
            SessionState.SetInt(LevelKey, level);
            EditorApplication.EnterPlaymode();
        }

        static string Role => SessionState.GetString(RoleKey, HostRole);
        static int LevelNumber => SessionState.GetInt(LevelKey, 2);

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
            _stageStart = Now;
            _stageTicks = 0;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || _finished) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;
            if (_began <= 0.0) _began = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - _began > Timeout)
            {
                Finish(false, $"tempo esgotado na etapa {_stage}" + (Role == JoinRole ? ClientReport() : HostReport()));
                return;
            }
            _stageTicks++;
            if (Role == JoinRole) TickJoin(session);
            else TickHost(session);
        }

        // ---------------------------------------------------------------- O que as duas máquinas medem

        static int PlanHash()
        {
            LevelData data = _level != null ? _level.Data : null;
            if (data == null) return -1;
            uint raw = data.HasPools ? PoolGenerator.Hash(data) : data.HasOffice ? (uint)OfficeGenerator.Hash(data) : 0u;
            return (int)(raw & 0xFFFFFF);
        }

        static int ObjectiveHash() => SwitchObjective.Instance != null ? SwitchObjective.Instance.Hash() & 0xFFFFFF : -1;

        static int PickupsHash()
        {
            BatteryPickups pickups = BatteryPickups.Instance;
            if (pickups == null) return -1;
            var points = new List<Vector3>();
            for (int i = 0; i < pickups.Count; i++) points.Add(pickups.PickupPosition(i));
            return NetProbe.PointsHash(points);
        }

        static float Water() => PoolWater.Current != null ? PoolWater.Current.Offset : 0f;

        static bool SirenOn()
        {
            if (SwitchObjective.Instance == null) return false;
            foreach (AudioSource source in SwitchObjective.Instance.GetComponentsInChildren<AudioSource>(true))
                if (source.isPlaying && source.clip != null && source.clip.name == "Sirene") return true;
            return false;
        }

        static bool PickupGone(int index)
        {
            BatteryPickups pickups = BatteryPickups.Instance;
            if (pickups == null || index >= pickups.Count) return false;
            Transform item = pickups.transform.Find("Pilhas_" + index);
            return (pickups.Taken & (1UL << index)) != 0 && item != null && !item.gameObject.activeSelf;
        }

        // ---------------------------------------------------------------- Host

        static void TickHost(GameSession session)
        {
            int inGame = CountInGame();
            if (inGame >= 2) _clientsSeen = Mathf.Max(_clientsSeen, inGame);

            // A sonda: hora, hashes, pilhas pegas, água, porta, etapa.
            if (session.IsHosting && session.SessionSeconds >= 0.0 && _level != null && EditorApplication.timeSinceStartup - _probeAt > 0.3)
            {
                _probeAt = EditorApplication.timeSinceStartup;
                ulong taken = BatteryPickups.Instance != null ? BatteryPickups.Instance.Taken : 0UL;
                NetProbe.Send(session, PlanHash(), ObjectiveHash(), PickupsHash(), (float)(taken & 0xFFFFFF), Water(),
                              LevelExit.Locked ? 1f : 0f, HostStageNumber(), _switchRun, _otherRun, _otherStart);
            }

            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                    if (!Ready(session) || inGame < 2) break;
                    if (BatteryPickups.Instance == null || BatteryPickups.Instance.Count < 2) { Finish(false, "menos de duas pilhas na fase"); return; }
                    if (SwitchObjective.Instance == null || SwitchObjective.Instance.Count == 0) { Finish(false, "fase sem o objetivo comum"); return; }
                    Next($"cliente entrou; plano {PlanHash():X6}, objetivo {ObjectiveHash():X6}, pilhas {PickupsHash():X6} ({BatteryPickups.Instance.Count})");
                    break;

                case 2:
                {
                    // Planta e relógio: o cliente confere sozinho. No Level 2, espera a maré subir.
                    _tideSeenMax = Mathf.Max(_tideSeenMax, Mathf.Abs(Water()));
                    double s = session.SessionSeconds;
                    double wait = LevelNumber == 2 ? PoolTide.FirstEbb + PoolTide.FallSeconds * 0.4 : 6.0; // subida, maré alta e parte da vazante
                    if (s < wait || InStage < 4f) break;
                    if (LevelNumber == 2 && _tideSeenMax < 0.3f) { Finish(false, $"a maré não subiu no host até os {s:F0} s (no máximo {_tideSeenMax:F2} m)"); return; }
                    // O host pega a pilha 0.
                    GoTo(0);
                    Next(LevelNumber == 2 ? $"maré subiu até {_tideSeenMax:F2} m no host" : null);
                    break;
                }

                case 3:
                    Aim(0);
                    if (_stageTicks < 4) break;
                    if (!PickupGone(0)) { BatteryPickups.PressForTest = true; if (InStage > 4f) Finish(false, "o host não conseguiu pegar a pilha 0"); break; }
                    // Espera o cliente pegar a 1.
                    if (!PickupGone(1)) { if (InStage > 40f) Finish(false, "a pilha 1 (do cliente) não sumiu no host"); break; }
                    if (BatteryPickups.Instance.LocalPicked != 1) { Finish(false, $"o host contou {BatteryPickups.Instance.LocalPicked} pilhas para si (pegou só uma)"); return; }
                    Next("a pilha do cliente sumiu no host, e só a do host recarregou a lanterna dele");
                    break;

                case 4:
                    // O cliente sai e entra de novo.
                    if (!_clientLeft && inGame < 2) { _clientLeft = true; _leaves++; }
                    if (_clientLeft && inGame >= 2 && InStage > 3f)
                    {
                        _clientLeft = false;
                        Next("o cliente saiu e entrou de novo");
                        if (LevelNumber == 3) _stage = 20;
                    }
                    else if (InStage > 60f) Finish(false, "o cliente não saiu e voltou");
                    break;

                case 5:
                    if (InStage < 4f) break;
                    SwitchObjective.Instance.CompleteForTest();
                    Next("o host cumpriu o objetivo");
                    break;

                case 6:
                {
                    // Espera a drenagem acabar (Level 2) e o cliente sair e voltar de novo.
                    if (LevelExit.Locked) { Finish(false, "cumprido, a porta continuou trancada no host"); return; }
                    // A saída do cliente conta mesmo antes de a água acabar de descer aqui.
                    if (!_clientLeft && inGame < 2) { _clientLeft = true; _leaves++; }
                    if (LevelNumber == 2 && Water() > -PoolTide.DrainDepth * 0.9f && InStage < PoolTide.DrainSeconds + 10f) break;
                    if (_clientLeft && inGame >= 2) { _clientLeft = false; Next("o cliente entrou de novo depois de cumprido"); }
                    else if (InStage > PoolTide.DrainSeconds + 70f) Finish(false, "o cliente não voltou depois de cumprido");
                    break;
                }

                case 7:
                    // O cliente termina e sai: o host encerra.
                    if (inGame < 2 && InStage > 2f) { Finish(true, string.Join("; ", Notes) + HostReport()); return; }
                    if (InStage > 60f) Finish(false, "o cliente não terminou" + HostReport());
                    break;

                case 20:
                {
                    // Level 3: apaga um trecho com interruptor para o cliente apertar.
                    if (InStage < 4f) break;
                    _janitor = JanitorBrain.Janitor;
                    OfficeLightWave wave = OfficeLightWave.Instance;
                    if (_janitor == null || !_janitor.IsSpawned || wave == null || LightSwitches.Instance == null)
                    {
                        if (InStage > 20f) Finish(false, "o Escritório não tem Zelador, onda de luzes ou interruptores");
                        break;
                    }
                    _janitor.ScheduleWave(OfficeElevators.Clock() + 1e6); // sem ondas automáticas daqui em diante
                    double t = OfficeElevators.Clock();
                    _switchRun = -1;
                    for (int r = 0; r < wave.RunCount && _switchRun < 0; r++)
                        if (LightSwitches.Instance.Has(r) && t >= _janitor.SwitchReadyAt(r) && WorkingLamps(r) >= 3) _switchRun = r;
                    if (_switchRun < 0) { Finish(false, "nenhum trecho com interruptor pronto e três luminárias boas"); return; }
                    if (!_janitor.RunActive(_switchRun, t)) _janitor.StartWave(_switchRun);
                    _switchWaveAt = t;
                    _flips0 = _janitor.SwitchFlips;
                    _requests0 = _janitor.FlipRequests;
                    Next($"o host apagou o trecho {_switchRun} para o cliente religar");
                    break;
                }

                case 21:
                {
                    // O pedido do cliente chega (RPC) e religa.
                    double t = OfficeElevators.Clock();
                    if (_janitor.SwitchFlips == _flips0)
                    {
                        // A passagem acabou sem o cliente apertar: apaga de novo.
                        if (!_janitor.RunActive(_switchRun, t) && t - _switchWaveAt > 3.0) { _janitor.StartWave(_switchRun); _switchWaveAt = t; }
                        if (InStage > 60f) Finish(false, $"o pedido do cliente não religou o trecho no host (pedidos {_janitor.FlipRequests - _requests0}, recusa: {_janitor.LastSwitchRefusal})");
                        break;
                    }
                    if (_janitor.RunActive(_switchRun, t)) { Finish(false, "o interruptor do cliente religou, mas o trecho continua na onda no host"); return; }
                    if (t >= _janitor.SwitchReadyAt(_switchRun)) { Finish(false, "o interruptor do cliente religou e não ficou recarregando no host"); return; }
                    _clientLeft = false;
                    Next($"o pedido do cliente chegou ao host ({_janitor.FlipRequests - _requests0} pedido(s)) e religou o trecho {_switchRun}");
                    break;
                }

                case 22:
                {
                    // Com o cliente fora, outro trecho apaga; ele volta e confere; o host segue.
                    if (!_clientLeft && _otherRun < 0 && inGame < 2)
                    {
                        _clientLeft = true;
                        _leaves++;
                        OfficeLightWave wave = OfficeLightWave.Instance;
                        double t = OfficeElevators.Clock();
                        for (int r = 0; r < wave.RunCount && _otherRun < 0; r++)
                            if (r != _switchRun && !_janitor.RunActive(r, t) && WorkingLamps(r) >= 3) _otherRun = r;
                        if (_otherRun < 0) { Finish(false, "nenhum segundo trecho com três luminárias boas"); return; }
                        _janitor.StartWave(_otherRun);
                        foreach (WavePlan plan in _janitor.Plans)
                            if (plan.Run == _otherRun && plan.Cut >= WavePlan.Never * 0.5) _otherStart = (float)plan.Start;
                        Notes.Add($"com o cliente fora, o host apagou o trecho {_otherRun}");
                    }
                    if (_clientLeft && inGame >= 2) { _clientLeft = false; _stageStart = Now; }
                    if (!_clientLeft && _otherRun >= 0 && inGame >= 2 && InStage > 12f)
                    {
                        Notes.Add("o cliente entrou de novo depois do interruptor");
                        _stage = 5;
                        _stageStart = Now;
                        _stageTicks = 0;
                    }
                    else if (InStage > 90f) Finish(false, "o cliente não saiu e voltou depois do interruptor");
                    break;
                }
            }
        }

        static int WorkingLamps(int run)
        {
            int n = 0;
            foreach (CeilingLight lamp in OfficeLightWave.Instance.RunLamps(run)) if (lamp != null && lamp.Works) n++;
            return n;
        }

        static float HostStageNumber()
        {
            switch (_stage)
            {
                case 0: case 1: case 2: return StagePlan;
                case 3: return StagePickups;
                case 4: return StageRejoin;
                case 20: case 21: return StageSwitch;
                case 22: return StageSwitchRejoin;
                case 5: case 6: return StageComplete;
                default: return StageRejoinDone;
            }
        }

        static string HostReport() => $"; host: água agora {Water():F2} m, porta {(LevelExit.Locked ? "trancada" : "aberta")}, cliente saiu {_leaves} vez(es)";

        // ---------------------------------------------------------------- Cliente

        static void TickJoin(GameSession session)
        {
            NetProbe.Watch(session);
            bool playing = session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null;
            if (playing && _playingSince < 0f) _playingSince = Now;
            if (!playing) _playingSince = -1f;
            _local = PlayerNetwork.Local;
            _level = session.level;

            // Entra (e reentra): de 4 em 4 s enquanto estiver no menu.
            if (session.Phase == SessionPhase.Menu && EditorApplication.timeSinceStartup - _lastTry > 4.0 && _stage != 99)
            {
                _lastTry = EditorApplication.timeSinceStartup;
                session.Join("127.0.0.1");
                NetProbe.Listen(session);
                NetProbe.OnSample = OnSample;
                _joins++;
            }

            if (!playing || Now - _playingSince < 1.5f) return;

            switch (_stage)
            {
                case 0:
                    Next($"entrou; plano {PlanHash():X6}");
                    break;

                case 1:
                    // Pilhas: quando o host anuncia, pega a 1 (e confere que a 0 sumiu).
                    if (_hostStage < StagePickups) break;
                    if (!_pickedOne)
                    {
                        if (_stageTicks % 40 == 1) GoTo(1);
                        Aim(1);
                        if (_stageTicks % 4 == 0) BatteryPickups.PressForTest = true;
                        if (PickupGone(1) && BatteryPickups.Instance.LocalPicked == 1)
                        {
                            _pickedOne = true;
                            Notes.Add("o cliente pegou a pilha 1 e a lanterna dele recarregou");
                        }
                        break;
                    }
                    if (PickupGone(0)) _sawHostPick = true;
                    if (!_sawHostPick) { if (InStage > 40f) Finish(false, "a pilha 0, pega pelo host, não sumiu no cliente"); break; }
                    if (_hostStage < StageRejoin) break;
                    Notes.Add("a pilha do host sumiu no cliente");
                    session.Leave();
                    Next();
                    break;

                case 2:
                    // Voltou: as pilhas pegas continuam sumidas.
                    if (_stageTicks < 3) break;
                    _rejoinPickupsOk = PickupGone(0) && PickupGone(1) && BatteryPickups.Instance.LocalPicked == 0;
                    if (!_rejoinPickupsOk) { if (InStage > 6f) Finish(false, $"entrando de novo, as pilhas pegas não estão sumidas (pegas={BatteryPickups.Instance.Taken})"); break; }
                    Next("entrando de novo, as duas pilhas pegas estão sumidas e ninguém recarregou");
                    if (LevelNumber == 3) _stage = 20;
                    break;

                case 3:
                {
                    // O host cumpre: a porta destranca; a água desce (2) ou a sirene toca (3).
                    if (_hostStage < StageComplete) break;
                    if (!LevelExit.Locked) _sawUnlocked = true;
                    if (SirenOn()) _sawSiren = true;
                    if (_waterLow == float.MaxValue) _waterLow = Water();
                    _waterLow = Mathf.Min(_waterLow, Water());
                    if (Water() < -PoolTide.DrainDepth * 0.85f) _sawDrop = true;
                    bool done = _sawUnlocked && (LevelNumber == 2 ? _sawDrop : _sawSiren);
                    if (!done)
                    {
                        if (InStage > PoolTide.DrainSeconds + 15f)
                            Finish(false, $"cumprido no host, no cliente: porta destrancada={_sawUnlocked}" +
                                          (LevelNumber == 2 ? $", água desceu até {_waterLow:F2} m" : $", sirene={_sawSiren}"));
                        break;
                    }
                    Notes.Add(LevelNumber == 2 ? $"cumprido: no cliente a porta destrancou e a água desceu até {_waterLow:F2} m"
                                               : "cumprido: no cliente a porta destrancou e a sirene tocou");
                    session.Leave();
                    Next();
                    break;
                }

                case 4:
                {
                    // Entrou depois de cumprido: logo de cara, porta aberta e (2) água baixa / (3) sirene.
                    if (_stageTicks < 3) break;
                    bool door = !LevelExit.Locked;
                    bool extra = LevelNumber == 2 ? Water() < -PoolTide.DrainDepth * 0.85f : SirenOn();
                    _afterNote = $"entrando depois de cumprido: porta aberta={door}, " + (LevelNumber == 2 ? $"água a {Water():F2} m" : $"sirene={SirenOn()}");
                    if (!(door && extra)) { if (InStage > 5f) Finish(false, _afterNote); break; }
                    _afterOk = true;
                    Notes.Add(_afterNote);
                    Next();
                    break;
                }

                case 20:
                {
                    // Level 3: o host apagou um trecho; o cliente aperta o interruptor dele (RPC ao host).
                    JanitorBrain janitor = JanitorBrain.Janitor;
                    if (_hostStage < StageSwitch || _hostSwitchRun < 0 || janitor == null || LightSwitches.Instance == null) break;
                    double t = OfficeElevators.Clock();
                    int run = _hostSwitchRun;
                    if (_standing && t < janitor.SwitchReadyAt(run) && !janitor.RunActive(run, t) && LightSwitches.Instance.Clacks > _clacks0)
                    {
                        _clientFlipped = true;
                        Notes.Add($"o cliente apertou o interruptor do trecho {run}: estalou, religou e ficou recarregando no cliente");
                        session.Leave();
                        _standing = false;
                        _stage = 21;
                        _stageStart = Now;
                        _stageTicks = 0;
                        break;
                    }
                    if (!janitor.RunActive(run, t)) { if (InStage > 60f) Finish(false, $"o trecho {run} não apagou no cliente"); break; }
                    if (!_standing) { StandAtSwitch(run); _standing = true; _clacks0 = LightSwitches.Instance.Clacks; _pressedAt = -1f; break; }
                    AimAt(LightSwitches.Instance.PositionOf(run));
                    if (_pressedAt < 0f || Now - _pressedAt > 2f)
                    {
                        if (LightSwitches.Prompt != SwitchReadyText)
                        {
                            if (InStage > 30f) Finish(false, $"no cliente, a mira no interruptor mostra \"{LightSwitches.Prompt}\"");
                            break;
                        }
                        LightSwitches.PressForTest = true;
                        _pressedAt = Now;
                    }
                    if (InStage > 60f) Finish(false, "o interruptor apertado pelo cliente não religou o trecho no cliente");
                    break;
                }

                case 21:
                {
                    // Voltou depois do interruptor e da outra onda: o estado chega a quem entra depois.
                    JanitorBrain janitor = JanitorBrain.Janitor;
                    if (_stageTicks < 3 || _hostStage < StageSwitchRejoin || _hostOtherRun < 0 || janitor == null || !janitor.IsSpawned || LightSwitches.Instance == null)
                    {
                        if (InStage > 60f) Finish(false, "voltando depois do interruptor, o cliente não recebeu o estado");
                        break;
                    }
                    double t = OfficeElevators.Clock();
                    int run = _hostSwitchRun, other = _hostOtherRun;
                    if (t >= janitor.SwitchReadyAt(run))
                    {
                        if (InStage > 8f) Finish(false, $"entrando depois, o interruptor do trecho {run} não está recarregando");
                        break;
                    }
                    if (janitor.RunActive(run, t)) { Finish(false, $"entrando depois, o trecho {run} religado pelo interruptor está na onda"); return; }
                    bool same = false;
                    WavePlan otherPlan = default;
                    foreach (WavePlan plan in janitor.Plans)
                        if (plan.Run == other && System.Math.Abs(plan.Start - _hostOtherStart) < 0.05) { same = true; otherPlan = plan; }
                    if (!same)
                    {
                        if (InStage > 8f) Finish(false, $"entrando depois, a passagem da onda no trecho {other} (início {_hostOtherStart:F2} s) não chegou ao cliente");
                        break;
                    }
                    if (!_standing) { StandAtSwitch(run); _standing = true; break; }
                    AimAt(LightSwitches.Instance.PositionOf(run));
                    if (_stageTicks < 8) break;
                    if (LightSwitches.Prompt == null || !LightSwitches.Prompt.Contains("RESETTING"))
                    {
                        if (InStage > 10f) Finish(false, $"entrando depois, a mira no interruptor recarregando mostra \"{LightSwitches.Prompt}\"");
                        break;
                    }
                    // As luminárias: o trecho religado aceso; o outro apagado, se ainda está no escuro firme.
                    OfficeLightWave wave = OfficeLightWave.Instance;
                    int unlit = 0;
                    // A luminária do cruzamento pode ser dos dois trechos: apagada pelo outro, que está no escuro.
                    var shared = new HashSet<CeilingLight>(wave.RunLamps(other));
                    // Conta a onda (ForcedDark), não o aceso do instante: luminária que pisca por natureza
                    // (LightState.Flicker) pode estar apagada num quadro qualquer.
                    foreach (CeilingLight lamp in wave.RunLamps(run)) if (lamp != null && lamp.Works && lamp.ForcedDark && !shared.Contains(lamp)) unlit++;
                    if (unlit > 0) { Finish(false, $"entrando depois, {unlit} luminária(s) do trecho religado {run} continuam apagadas pela onda"); return; }
                    string lamps;
                    double darkFrom = OfficeLightWave.DarkStart(otherPlan) + wave.RunLamps(other).Count * OfficeLightWave.StepSeconds + 0.5;
                    double darkTo = OfficeLightWave.DarkStart(otherPlan) + OfficeLightWave.HoldSeconds - 0.5;
                    // Voltou ainda no aviso (piscando): espera o escuro firme (o host dá 12 s antes de seguir).
                    if (t < darkFrom && InStage < 9f) break;
                    if (t >= darkFrom && t <= darkTo)
                    {
                        int dark = 0, works = 0;
                        foreach (CeilingLight lamp in wave.RunLamps(other))
                        {
                            if (lamp == null || !lamp.Works) continue;
                            works++;
                            if (lamp.ForcedDark) dark++;
                        }
                        if (dark < works) { Finish(false, $"entrando depois, no escuro firme do trecho {other} só {dark} de {works} luminárias estão apagadas"); return; }
                        lamps = $"o trecho {other} apagado ({dark} de {works} luminárias)";
                    }
                    else lamps = $"o trecho {other} fora da janela do escuro firme ({t - _hostOtherStart:F1} s depois do início), luminárias não conferidas";
                    _switchRejoinOk = true;
                    Notes.Add($"entrando depois do interruptor: recarregando (a mira diz \"{LightSwitches.Prompt.Replace("\n", " ")}\"), o trecho {run} aceso, a passagem do trecho {other} igual à do host, {lamps}");
                    _stage = 3;
                    _stageStart = Now;
                    _stageTicks = 0;
                    break;
                }

                case 5:
                {
                    // Fecha: hashes, relógio e maré.
                    if (InStage < 2f) break;
                    bool clock = NetProbe.ClockOk(out string clockReport);
                    bool hashes = _hashChecks >= 10 && _hashMismatch == 0;
                    bool tide = LevelNumber != 2 || (_tideChecks >= 10 && _tideHostMax >= 0.3f && _tideWorst <= 0.15f);
                    bool ok = clock && hashes && tide && _pickedOne && _sawHostPick && _rejoinPickupsOk && _sawUnlocked && _afterOk &&
                              (LevelNumber != 3 || (_clientFlipped && _switchRejoinOk));
                    string report = string.Join("; ", Notes) + $"; hashes iguais em {_hashChecks - _hashMismatch} de {_hashChecks} amostras{_hashNote}; {clockReport}";
                    if (LevelNumber == 2) report += $"; maré: {_tideChecks} comparações com a água subindo ou descendo (até {_tideHostMax:F2} m no host), maior diferença {_tideWorst:F3} m";
                    _stage = 99;
                    Finish(ok, report + ClientReport());
                    break;
                }
            }
        }

        // Cada amostra do host: compara com o que esta máquina vê agora.
        static void OnSample(NetProbe.Sample s)
        {
            if (s.Values.Length < 7) return;
            _hostStage = Mathf.RoundToInt(s.Values[6]);
            if (s.Values.Length >= 10)
            {
                _hostSwitchRun = Mathf.RoundToInt(s.Values[7]);
                _hostOtherRun = Mathf.RoundToInt(s.Values[8]);
                _hostOtherStart = s.Values[9];
            }
            GameSession session = GameSession.Instance;
            if (session == null || session.Phase != SessionPhase.Playing || !session.LevelReady || _level == null || _level.Data == null) return;
            if (_playingSince < 0f || Now - _playingSince < 1.5f) return;

            int plan = PlanHash(), objective = ObjectiveHash(), pickups = PickupsHash();
            _hashChecks++;
            if (plan != Mathf.RoundToInt(s.Values[0]) || objective != Mathf.RoundToInt(s.Values[1]) || pickups != Mathf.RoundToInt(s.Values[2]))
            {
                _hashMismatch++;
                if (_hashNote.Length == 0)
                    _hashNote = $" (diferente: plano {plan:X6}/{Mathf.RoundToInt(s.Values[0]):X6}, objetivo {objective:X6}/{Mathf.RoundToInt(s.Values[1]):X6}, pilhas {pickups:X6}/{Mathf.RoundToInt(s.Values[2]):X6})";
            }

            // Maré: só antes de cumprir (depois, a drenagem também é conferida, à parte).
            if (LevelNumber == 2 && _hostStage < StageComplete && Mathf.Abs(s.Values[4]) > 0.02f)
            {
                _tideChecks++;
                _tideHostMax = Mathf.Max(_tideHostMax, Mathf.Abs(s.Values[4]));
                _tideWorst = Mathf.Max(_tideWorst, Mathf.Abs(s.Values[4] - Water()));
            }
        }

        static string ClientReport() =>
            $"; cliente: entrou {_joins} vez(es), etapa do host {_hostStage}, pegou a sua={_pickedOne}, viu a do host sumir={_sawHostPick}, " +
            $"pilhas certas ao voltar={_rejoinPickupsOk}, porta destrancada={_sawUnlocked}, sirene={_sawSiren}, água mais baixa {(_waterLow == float.MaxValue ? 0f : _waterLow):F2} m" +
            (LevelNumber == 3 ? $", apertou o interruptor={_clientFlipped}, interruptor certo ao voltar={_switchRejoinOk}" : "");

        // ---------------------------------------------------------------- Ajudantes

        static bool Ready(GameSession session)
        {
            if (session.Phase != SessionPhase.Playing) return false;
            _local = PlayerNetwork.Local;
            _level = session.level;
            if (_local == null || _level == null || _level.Data == null) return false;
            return InStage >= 1.5f;
        }

        static int CountInGame()
        {
            int count = 0;
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.InGame) count++;
            return count;
        }

        // De pé a 0,9 m da pilha, do lado de fora da parede (como no BatteryTest).
        static void GoTo(int index)
        {
            BatteryPickups pickups = BatteryPickups.Instance;
            Vector3 at = pickups.PickupPosition(index);
            Vector3 away = Vector3.zero;
            foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                if (!Physics.Raycast(at + Vector3.up * 0.5f, dir, 1.2f, ~0, QueryTriggerInteraction.Ignore)) { away = dir; break; }
            Vector3 stand = at + away * 0.9f;
            _local.controller.Teleport(new Vector3(stand.x, at.y + 0.05f, stand.z), 0f);
            Aim(index);
        }

        static void Aim(int index)
        {
            BatteryPickups pickups = BatteryPickups.Instance;
            if (pickups == null || _local == null || _local.playerCamera == null) return;
            Vector3 target = pickups.PickupPosition(index) + Vector3.up * 0.02f;
            Transform eye = _local.playerCamera.transform;
            Vector3 to = target - eye.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude < 0.0001f) return;
            _local.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 0f);
            _local.controller.SetPitch(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
        }

        // De pé a 0,9 m do interruptor do trecho, de frente para ele (como no JanitorTest).
        static void StandAtSwitch(int run)
        {
            Vector3 at = LightSwitches.Instance.PositionOf(run);
            Vector3 normal = LightSwitches.Instance.NormalOf(run);
            Vector3 foot = at + normal * 0.9f;
            foot.y = _level.transform.position.y;
            _local.controller.Teleport(foot, Mathf.Atan2(-normal.x, -normal.z) * Mathf.Rad2Deg);
        }

        // Vira o jogador e a câmera para o ponto (a mira do interruptor).
        static void AimAt(Vector3 point)
        {
            if (_local == null || _local.playerCamera == null) return;
            Vector3 to = point - _local.playerCamera.transform.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude < 0.0001f) return;
            _local.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 0f);
            _local.controller.SetPitch(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
        }

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;
            BatteryPickups.PressForTest = false;
            LightSwitches.PressForTest = false;
            EntityDirector.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }
            Debug.Log($"[BackRooms] Teste de rede (Level {LevelNumber}, {(Role == JoinRole ? "cliente" : "host")}) " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
            SessionState.SetInt(ExitCodeKey, ok ? 0 : 3);
            EditorApplication.playModeStateChanged -= ExitAfterPlay;
            EditorApplication.playModeStateChanged += ExitAfterPlay;
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
