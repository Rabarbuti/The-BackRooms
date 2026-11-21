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
    /// Os dois inimigos do Escritório na mesma partida, sem pausar nada (os testes de cada um pausam o
    /// outro ou o diretor): o Zelador (<see cref="JanitorBrain"/>) e o Ascensorista
    /// (<see cref="AttendantBrain"/>). Em ordem:
    ///
    /// 1. o jogador fica de pé no interruptor de um trecho da onda (seed 1998);
    /// 2. pelo relógio, a onda apaga perto dele e o Zelador sobe do chão; o Ascensorista chega de
    ///    elevador (a primeira chegada é aos 60 s) e sai para o andar;
    /// 3. o Ascensorista é posto a 15–25 m do interruptor; o trecho apaga de novo; com o Zelador lá, o
    ///    jogador aperta o interruptor: o trecho religa, o Zelador some e o estalo chega ao Ascensorista;
    /// 4. o tempo todo: o Zelador nunca fica numa célula acesa (mais de 1,5 s), os dois continuam na
    ///    rede e o console não tem erro.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level3EnemiesTest.BatchRun (0 passou, 3
    /// falhou); linha "[BackRooms] Teste dos dois inimigos do Level 3".
    /// </summary>
    public static class Level3EnemiesTest
    {
        const string ActiveKey = "BackRooms.Level3EnemiesTest.Active";
        const string BatchKey = "BackRooms.Level3EnemiesTest.Batch";
        const string ExitCodeKey = "BackRooms.Level3EnemiesTest.ExitCode";
        const double Timeout = 240.0;
        const string SwitchReadyText = "E - LIGHTS ON";

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static readonly List<Vector2Int> _cells = new List<Vector2Int>();
        static int _stage, _run = -1, _heard, _flips, _vanishes, _playerCatches;
        static double _began;
        static float _stageStart, _litSince = -1f, _pressedAt = -1f;
        static bool _finished, _janitorEmerged, _attendantOut;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static JanitorBrain _janitor;
        static AttendantBrain _attendant;

        [MenuItem("BackRooms/Testar Zelador e Ascensorista juntos (Level 3)")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false);
        }

        public static void BatchRun() => Run(true);

        static void Run(bool batch)
        {
            EditorSceneManager.OpenScene(Level3Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            EditorApplication.EnterPlaymode();
        }

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
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || _finished) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;
            if (_began <= 0.0) _began = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - _began > Timeout) { Finish(false, $"tempo esgotado na etapa {_stage}" + State()); return; }

            if (_stage >= 2 && !Watch()) return;

            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = false;
                    OfficeLightWave.Paused = false;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                {
                    if (session.Phase != SessionPhase.Playing || !session.LevelReady || InStage < 2f) break;
                    _local = PlayerNetwork.Local;
                    _level = session.level;
                    _janitor = JanitorBrain.Janitor;
                    if (_local == null || _level == null || _level.Data == null || _janitor == null || !_janitor.IsSpawned) break;
                    if (!JanitorDark.Bind(_level)) { Finish(false, "sem mapa do escuro"); return; }
                    if (!PickRun(out string why)) { Finish(false, why); return; }
                    StandAtSwitch();
                    _playerCatches = 0;
                    Next($"o jogador ficou de pé no interruptor do trecho {_run} ({_cells.Count} células), aos {session.SessionSeconds:F0} s");
                    break;
                }

                case 2:
                {
                    // Pelo relógio: a onda traz o Zelador; o Ascensorista chega de elevador e sai.
                    _attendant = AttendantBrain.Attendant;
                    if (_janitor.Emerges > 0 && !_janitorEmerged)
                    {
                        _janitorEmerged = true;
                        Notes.Add($"pelo relógio, o Zelador apareceu aos {session.SessionSeconds:F0} s ({_janitor.LastEmergeKind})");
                    }
                    if (_attendant != null && _attendant.IsSpawned && _attendant.Phase == AttendantPhase.Out && !_attendantOut)
                    {
                        _attendantOut = true;
                        Notes.Add($"pelo relógio, o Ascensorista chegou de elevador e saiu para o andar aos {session.SessionSeconds:F0} s (poço {_attendant.Shaft})");
                    }
                    // Sem Ascensorista 30 s depois da primeira chegada dele (aos 60 s): o diretor recusou
                    // (anota o porquê) e o teste o põe como o AttendantTest faz; a chegada segue pelo relógio.
                    if (_attendant == null && session.SessionSeconds > 90.0 && EntityDirector.Instance != null)
                    {
                        string why = EntityDirector.Instance.LastRefusal;
                        Vector3 somewhere = _level.CellCenter(_level.Data.ExitX, _level.Data.ExitZ);
                        _attendant = EntityDirector.Instance.SpawnAt(somewhere, Vector3.forward) as AttendantBrain;
                        if (_attendant == null) { Finish(false, $"o diretor do Level 3 não tem o Ascensorista (recusa: {why})"); return; }
                        _attendant.ForceArrival();
                        Notes.Add($"aos 90 s o diretor ainda não tinha posto o Ascensorista (recusa: {why}); o teste o pôs e ele chegou pelo elevador");
                    }
                    // Pego por um deles, volta ao interruptor.
                    if (!_local.controller.IsReturning && !_local.controller.IsCaught &&
                        Flat(_local.transform.position - SwitchFoot()).magnitude > 1.5f) StandAtSwitch();
                    if (!_janitorEmerged || !_attendantOut)
                    {
                        if (session.SessionSeconds > 170.0)
                            Finish(false, $"aos {session.SessionSeconds:F0} s: Zelador apareceu={_janitorEmerged}, Ascensorista no andar={_attendantOut} ({(_attendant != null ? _attendant.Phase.ToString() : "não existe")})" + State());
                        break;
                    }
                    if (_local.controller.IsReturning || _local.controller.IsCaught) break;
                    // Os dois no andar: o Ascensorista a 15-25 m do interruptor; o trecho apaga de novo.
                    Vector3 at = LightSwitches.Instance.PositionOf(_run);
                    if (!ListenerSpot(at, out Vector3 ear)) { Finish(false, "nenhuma célula a 15-25 m do interruptor para pôr o Ascensorista"); return; }
                    EntityDirector.Instance.SpawnAt(ear, at - ear);
                    double t = OfficeElevators.Clock();
                    if (t < _janitor.SwitchReadyAt(_run)) break; // alguém apertou antes: espera recarregar
                    _janitor.ScheduleWave(t + 1e6);
                    if (!_janitor.RunActive(_run, t)) _janitor.StartWave(_run);
                    StandAtSwitch();
                    // Os armários que batem (a partir dos ~110 s) também chegam a ele: calados daqui
                    // em diante, só o estalo do interruptor conta como barulho ouvido.
                    if (LockerBangs.Instance != null) LockerBangs.Instance.enabled = false;
                    _heard = _attendant.WorldNoisesHeard;
                    _flips = _janitor.SwitchFlips;
                    _pressedAt = -1f;
                    Next($"o Ascensorista foi posto a {Flat(ear - at).magnitude:F0} m do interruptor e o trecho {_run} apagou de novo");
                    break;
                }

                case 3:
                {
                    // Escuro: o jogador aperta o interruptor logo (parado no escuro o Zelador o pega).
                    Vector3 at = LightSwitches.Instance.PositionOf(_run);
                    if (_local.controller.IsReturning || _local.controller.IsCaught)
                    {
                        _stageStart = Now; // pego por um deles: recomeça a contar quando voltar
                        _caughtInStage = true;
                        break;
                    }
                    if (Flat(_local.transform.position - SwitchFoot()).magnitude > 1.5f) StandAtSwitch();
                    AimAt(at);
                    double t = OfficeElevators.Clock();
                    int dark = 0;
                    foreach (Vector2Int c in _cells) if (JanitorDark.IsDark(c.x, c.y)) dark++;
                    if (_janitor.SwitchFlips != _flips)
                    {
                        bool inRun = _janitor.Present && JanitorDark.Cell(_janitor.transform.position, out int jx, out int jz) && _cells.Contains(new Vector2Int(jx, jz));
                        Next($"o trecho apagou e o jogador apertou o interruptor (Zelador no trecho={inRun}{(_caughtInStage ? "; antes, o jogador foi pego e voltou" : "")})");
                        _vanishes = _janitor.Vanishes;
                        break;
                    }
                    if (!_janitor.RunActive(_run, t))
                    {
                        _janitor.StartWave(_run);
                        _darkSince = -1f;
                    }
                    if (dark < _cells.Count - 1)
                    {
                        _darkSince = -1f;
                        if (InStage > 40f)
                        {
                            string plans = "";
                            foreach (WavePlan plan in _janitor.Plans) plans += $" [trecho {plan.Run} início {plan.Start:F1} corte {(plan.Cut > 1e11 ? "nunca" : plan.Cut.ToString("F1"))}]";
                            Finish(false, $"o trecho {_run} não apagou em 40 s ({dark} de {_cells.Count} células escuras; agora {t:F1} s; na onda={_janitor.RunActive(_run, t)};{plans})" + State());
                        }
                        break;
                    }
                    if (_darkSince < 0f) _darkSince = Now;
                    if (Now - _darkSince < 0.3f) break;
                    if (_pressedAt < 0f || Now - _pressedAt > 1.5f)
                    {
                        if (LightSwitches.Prompt != SwitchReadyText)
                        {
                            if (InStage > 40f) Finish(false, $"a mira no interruptor mostra \"{LightSwitches.Prompt}\"" + State());
                            break;
                        }
                        LightSwitches.PressForTest = true;
                        _pressedAt = Now;
                    }
                    if (InStage > 60f) Finish(false, $"o interruptor não religou ({_janitor.LastSwitchRefusal})" + State());
                    break;
                }

                case 4:
                {
                    // Religou: o trecho acende, o Zelador sai dele e o estalo chega ao Ascensorista.
                    if (InStage < 1.5f) break;
                    foreach (Vector2Int c in _cells)
                        if (JanitorDark.IsDark(c.x, c.y)) { Finish(false, "o interruptor religou e o trecho continuou escuro"); return; }
                    if (_janitor.Present && JanitorDark.Cell(_janitor.transform.position, out int jx, out int jz) && _cells.Contains(new Vector2Int(jx, jz)))
                    { Finish(false, "o trecho acendeu e o Zelador continuou nele" + State()); return; }
                    Vector3 at = LightSwitches.Instance.PositionOf(_run);
                    float d = Flat(_attendant.transform.position - at).magnitude;
                    if (_attendant.WorldNoisesHeard == _heard) { Finish(false, $"o Ascensorista ({_attendant.Phase}, a {d:F0} m) não ouviu o estalo do interruptor" + State()); return; }
                    if (Flat(WorldNoise.LastAt - at).magnitude > 0.5f) { Finish(false, $"o último barulho do mundo não foi o estalo do interruptor (em {WorldNoise.LastAt})" + State()); return; }
                    Finish(true, string.Join("; ", Notes) + $"; o estalo (aos {GameSession.Instance.SessionSeconds:F0} s) chegou ao Ascensorista (a {d:F0} m, agora {_attendant.Phase}); " +
                                 $"no fim: Zelador apareceu {_janitor.Emerges} vez(es), sumiu {_janitor.Vanishes}, pegou {_janitor.Catches}; " +
                                 $"Ascensorista chegou {_attendant.Arrivals} vez(es), pegou {_attendant.Catches}, levou {_attendant.Takes}; jogador pego {_playerCatches} vez(es)");
                    break;
                }
            }
        }

        // A cada quadro, depois de montada a fase: os dois na rede, o Zelador fora da luz, capturas.
        static bool Watch()
        {
            if (_janitor == null || !_janitor.IsSpawned) { Finish(false, "o Zelador saiu da rede"); return false; }
            if (_attendant != null && !_attendant.IsSpawned) { Finish(false, "o Ascensorista saiu da rede"); return false; }
            if (_janitor.Present && !JanitorDark.IsDarkAt(_janitor.transform.position))
            {
                if (_litSince < 0f) _litSince = Now;
                else if (Now - _litSince > 1.5f) { Finish(false, "o Zelador ficou mais de 1,5 s numa célula acesa" + State()); return false; }
            }
            else _litSince = -1f;
            if (_local != null && _local.controller.IsCaught && !_wasCaught) _playerCatches++;
            _wasCaught = _local != null && _local.controller.IsCaught;
            return true;
        }

        static bool _wasCaught, _caughtInStage;
        static float _darkSince = -1f;

        // Um trecho da onda com interruptor e quatro células ou mais com luminária boa, fora de abrigo.
        static bool PickRun(out string why)
        {
            why = "";
            OfficeLightWave wave = OfficeLightWave.Instance;
            LightSwitches switches = LightSwitches.Instance;
            if (wave == null || switches == null) { why = "sem onda de luzes ou sem interruptores"; return false; }
            // O mais longe da cabine: o diretor só põe o Ascensorista quando alguém se afasta (uma
            // célula do caminho longe e fora da vista de todos).
            var nav = new LevelNav(_level);
            int[,] fromStart = nav.NewMap();
            nav.WorldToCell(_local.transform.position, out int sx, out int sz);
            nav.Distances(sx, sz, fromStart);
            int best = -1, bestCells = 0, bestFar = -1;
            for (int r = 0; r < wave.RunCount; r++)
            {
                if (!switches.Has(r)) continue;
                _janitor.RunCells(r, _cells);
                bool good = _cells.Count >= 4;
                int far = 0;
                foreach (Vector2Int c in _cells)
                {
                    if (_level.Data.IsShelter(c.x, c.y)) good = false;
                    foreach (CeilingLight lamp in JanitorDark.LampsAt(c.x, c.y)) if (lamp == null || !lamp.Works) good = false;
                    far = Mathf.Max(far, fromStart[c.x, c.y]);
                }
                if (good && far > bestFar) { best = r; bestCells = _cells.Count; bestFar = far; }
            }
            if (best < 0 || bestCells < 4) { why = $"nenhum trecho de 4 células com interruptor e luminárias boas ({switches.Count} interruptores em {wave.RunCount} trechos)"; return false; }
            _run = best;
            _janitor.RunCells(best, _cells);
            return true;
        }

        // Uma célula de corredor ou saguão, fora de abrigo e do trecho, a 15-25 m do ponto.
        static bool ListenerSpot(Vector3 point, out Vector3 spot)
        {
            spot = Vector3.zero;
            LevelData d = _level.Data;
            float best = float.MaxValue;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.IsShelter(x, z) || d.IsBarricade(x, z) || _cells.Contains(new Vector2Int(x, z))) continue;
                    if (d.Office[x, z] != OfficeCell.Corridor && d.Office[x, z] != OfficeCell.Lobby) continue;
                    Vector3 c = _level.CellCenter(x, z);
                    c.y = _level.transform.position.y;
                    float dist = Flat(c - point).magnitude;
                    if (dist < 15f || dist > 25f || Mathf.Abs(dist - 20f) >= best) continue;
                    best = Mathf.Abs(dist - 20f);
                    spot = c;
                }
            return best < float.MaxValue;
        }

        static Vector3 SwitchFoot()
        {
            Vector3 foot = LightSwitches.Instance.PositionOf(_run) + LightSwitches.Instance.NormalOf(_run) * 0.9f;
            foot.y = _level.transform.position.y;
            return foot;
        }

        static void StandAtSwitch()
        {
            Vector3 normal = LightSwitches.Instance.NormalOf(_run);
            _local.controller.Teleport(SwitchFoot(), Mathf.Atan2(-normal.x, -normal.z) * Mathf.Rad2Deg);
        }

        static void AimAt(Vector3 point)
        {
            if (_local.playerCamera == null) return;
            Vector3 to = point - _local.playerCamera.transform.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude < 0.0001f) return;
            _local.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 0f);
            _local.controller.SetPitch(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        static string State()
        {
            string j = _janitor != null ? $"Zelador presente={_janitor.Present} estado={_janitor.State} apareceu {_janitor.Emerges} sumiu {_janitor.Vanishes} pegou {_janitor.Catches} [onda: {_janitor.WaveLog}]" : "sem Zelador";
            string a = _attendant != null ? $"Ascensorista {_attendant.Phase} chegou {_attendant.Arrivals} pegou {_attendant.Catches}"
                : $"sem Ascensorista (recusa do diretor: {(EntityDirector.Instance != null ? EntityDirector.Instance.LastRefusal : "sem diretor")})";
            return $" | {j}; {a}; antes: {string.Join("; ", Notes)}";
        }

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;
            LightSwitches.PressForTest = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }
            Debug.Log("[BackRooms] Teste dos dois inimigos do Level 3 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
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
