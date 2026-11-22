using System.Collections.Generic;
using System.IO;
using BackRooms.Entities;
using BackRooms.Items;
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
    /// Teste em Play das lousas e das carteiras das salas de aula do Escritório (<see cref="ClassroomSeats"/>),
    /// com teclado e mouse virtuais:
    /// - sozinho (<see cref="BatchRun"/>): risca a lousa com a caneta (o traço é de marcador e fica na
    ///   face dela, não na parede atrás); mira a cadeira da primeira fila (aviso E - SIT); com a cadeira
    ///   ocupada por um jogador de mentira (<see cref="ClassroomSeats.OccupiedForTest"/>) o host recusa;
    ///   livre, senta chegando com W apertado e não anda nem levanta; o olho fica na altura de quem
    ///   senta, de frente para a lousa, e a cabeça não gira além do limite; risca a carteira sentado;
    ///   E levanta num lugar livre (cápsula sem nada, chão embaixo); senta de novo e W levanta; senta e,
    ///   pego, a cadeira fica livre. Fotos em Logs/Previews/salas.
    /// - dois editores: <see cref="BatchHost"/> risca a lousa e senta antes de o cliente entrar;
    ///   <see cref="BatchJoin"/> entra depois e tem de ver o host sentado (pose, virado para a lousa) e
    ///   o traço de marcador na lousa; o host recusa a cadeira dele ao cliente; o cliente senta noutra e
    ///   o host o vê sentado; o host levanta e o cliente vê a pose sumir.
    ///
    /// Linha "[BackRooms] Teste das carteiras (papel) PASSOU/FALHOU". Sai com 0 se passou, 3 se falhou.
    /// </summary>
    public static class ClassroomSeatsTest
    {
        const string ActiveKey = "BackRooms.ClassroomSeatsTest.Active";
        const string BatchKey = "BackRooms.ClassroomSeatsTest.Batch";
        const string ExitCodeKey = "BackRooms.ClassroomSeatsTest.ExitCode";
        const string RoleKey = "BackRooms.ClassroomSeatsTest.Role";
        const string SoloRole = "solo", HostRole = "host", JoinRole = "join";
        const double SoloTimeout = 150.0, NetTimeout = 240.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static readonly List<Vector3> StrokePoints = new List<Vector3>();
        static int _stage;
        static double _began, _stageStart, _lastTry;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static ClassroomSeats _seats;
        static Keyboard _keyboard;
        static Mouse _mouse;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;
        static int _board = -1, _seat = -1, _seat2 = -1;
        static Vector3 _boardCenter, _boardNormal, _seatFeet;
        static int _strokesBefore, _refusals;
        static Vector3 _heldAt;
        static GameObject _dummy;
        static bool _finished;
        static int _sitCount;

        [MenuItem("BackRooms/Testar lousas e carteiras (Level 3)")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false, SoloRole);
        }

        public static void BatchRun() => Run(true, SoloRole);
        public static void BatchHost() => Run(true, HostRole);
        public static void BatchJoin() => Run(true, JoinRole);

        static string Role => SessionState.GetString(RoleKey, SoloRole);

        static void Run(bool batch, string role)
        {
            if (!File.Exists(Level3Setup.ScenePath)) Level3Setup.BuildScene();
            EditorSceneManager.OpenScene(Level3Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(RoleKey, role);
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
            if (Role == JoinRole && message.Contains("Failed to connect to server")) return;
            Errors.Add(message);
        }

        static double Now => EditorApplication.timeSinceStartup;
        static double InStage => Now - _stageStart;

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

            if (_began <= 0.0)
            {
                _began = Now;
                SetUpDevices();
            }

            if (Now - _began > (Role == SoloRole ? SoloTimeout : NetTimeout))
            {
                Finish(false, $"tempo esgotado na etapa {_stage}");
                return;
            }

            if (Role == JoinRole) TickJoin(session);
            else if (Role == HostRole) TickHost(session);
            else TickSolo(session);
        }

        // ---------------------------------------------------------------- Sozinho

        static void TickSolo(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    LevelProgress.DevModeOverride = true;
                    EntityDirector.Paused = true;
                    OfficeLightWave.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                    if (!Ready(session)) break;
                    if (!PickBoardAndSeats()) return;
                    PhotoRoom();
                    GoToBoard();
                    Next($"{_seats.Count} carteiras e {_seats.Boards.Count} lousa(s) na fase");
                    break;

                // Risca a lousa.
                case 2: case 3: case 4: case 5:
                    if (!DrawOnBoard()) break;
                    Next();
                    break;

                case 6:
                    if (!CheckBoardStroke()) return;
                    Photo("lousa_riscada", _boardCenter + _boardNormal * 1.6f + Vector3.up * 0.1f, _boardCenter);
                    // Guarda a caneta e vai até a carteira.
                    Tap(Key.Digit1);
                    GoBehindSeat(_seat);
                    Next();
                    break;

                case 7:
                    Release();
                    AimAtSeat(_seat);
                    if (InStage < 0.8) break;
                    if (_local.pen.Equipped) { Finish(false, "a caneta não foi guardada"); return; }
                    if (_seats.Aimed != _seat || ClassroomSeats.Prompt != ClassroomSeats.SitText)
                    {
                        if (InStage > 4) Finish(false, $"mirando a carteira, a cadeira mirada foi {_seats.Aimed} (era {_seat}) e o aviso \"{ClassroomSeats.Prompt}\"");
                        break;
                    }
                    // Ocupada por um segundo jogador (de mentira): o host recusa.
                    ClassroomSeats.OccupiedForTest = _seat;
                    _refusals = _seats.RefusalsReceived;
                    ClassroomSeats.PressForTest = true;
                    Next("mirando a carteira o aviso é E - SIT");
                    break;

                case 8:
                    AimAtSeat(_seat);
                    if (_seats.RefusalsReceived == _refusals) { if (InStage > 3) Finish(false, "a cadeira ocupada não foi recusada"); break; }
                    if (_local.controller.IsSeated) { Finish(false, "sentou na cadeira ocupada"); return; }
                    if (ClassroomSeats.Prompt != ClassroomSeats.OccupiedText) { if (InStage > 4) Finish(false, $"recusada, o aviso foi \"{ClassroomSeats.Prompt}\""); break; }
                    ClassroomSeats.OccupiedForTest = -1;
                    Next("ocupada por outro, o host recusou (aviso SEAT TAKEN) e o jogador não sentou");
                    break;

                case 9:
                    // Chega segurando W e aperta E: senta, e o W segurado não o levanta.
                    AimAtSeat(_seat);
                    if (InStage < 1.8) break;
                    Hold(Key.W);
                    ClassroomSeats.PressForTest = true;
                    Next();
                    break;

                case 10:
                    if (!_local.controller.IsSeated) { AimAtSeat(_seat); if (InStage > 3) Finish(false, "apertou E na carteira livre e não sentou"); break; }
                    if (_heldAt == Vector3.zero) _heldAt = _local.transform.position;
                    Hold(Key.W);
                    if (InStage < 1.5) break;
                    if (!CheckSeated(_seat)) return;
                    if ((_local.transform.position - _heldAt).magnitude > 0.01f) { Finish(false, "sentado, com W apertado, andou"); return; }
                    Release();
                    Next("chegando com W apertado, sentou e não andou nem levantou; olho a " +
                         $"{_local.playerCamera.transform.position.y - _seatFeet.y:F2} m, de frente para a lousa");
                    break;

                case 11:
                {
                    // A cabeça gira até o limite.
                    float front = _seats.YawWorld(_seat);
                    if (InStage < 0.3) { _local.transform.rotation = Quaternion.Euler(0f, front + 150f, 0f); break; }
                    float off = Mathf.DeltaAngle(front, _local.transform.eulerAngles.y);
                    if (Mathf.Abs(off) > ClassroomSeats.TurnLimit + 0.5f) { Finish(false, $"sentado, a cabeça girou {off:F0}° (limite {ClassroomSeats.TurnLimit:F0}°)"); return; }
                    _local.transform.rotation = Quaternion.Euler(0f, front, 0f);
                    _local.controller.SetPitch(8f);
                    Photo("vista_sentado", null, Vector3.zero);
                    // Risca a carteira, sentado.
                    _strokesBefore = DrawingBoard.Instance.StrokeCount;
                    Tap(Key.Digit1);
                    Next($"a cabeça parou em {off:F0}° do limite de {ClassroomSeats.TurnLimit:F0}°");
                    break;
                }

                case 12:
                    Release();
                    _local.controller.SetPitch(30f);
                    if (InStage < 0.5) break;
                    if (InStage < 1.3) { _local.transform.rotation = Quaternion.Euler(0f, _seats.YawWorld(_seat) + (float)InStage * 20f, 0f); Drag(true, 0f, 0f); break; }
                    Drag(false, 0f, 0f);
                    if (DrawingBoard.Instance.StrokeCount <= _strokesBefore) { Finish(false, "sentado, a caneta não riscou a carteira"); return; }
                    if (!_local.controller.IsSeated) { Finish(false, "riscando a carteira, levantou"); return; }
                    Tap(Key.Digit1);
                    Next("sentado, riscou a carteira");
                    break;

                case 13:
                    Release();
                    if (InStage < 0.5) break;
                    ClassroomSeats.PressForTest = true;
                    Next();
                    break;

                case 14:
                    if (_local.controller.IsSeated) { if (InStage > 2) Finish(false, "E não levantou"); break; }
                    if (InStage < 0.5) break;
                    if (!CheckStandingSpot("E")) return;
                    GoBehindSeat(_seat);
                    Next("E levantou num lugar livre");
                    break;

                case 15:
                    AimAtSeat(_seat);
                    if (InStage < 0.8) break;
                    ClassroomSeats.PressForTest = true;
                    Next();
                    break;

                case 16:
                    if (!_local.controller.IsSeated) { AimAtSeat(_seat); if (InStage > 3) Finish(false, "não sentou de novo"); break; }
                    if (InStage < 0.8) break;
                    Hold(Key.W);
                    Next();
                    break;

                case 17:
                    Hold(Key.W);
                    if (_local.controller.IsSeated) { if (InStage > 2) Finish(false, "W não levantou"); break; }
                    Release();
                    Next();
                    break;

                case 18:
                    if (InStage < 0.5) break;
                    if (!CheckStandingSpot("W")) return;
                    PhotoDummy();
                    GoBehindSeat(_seat);
                    Next("W levantou num lugar livre");
                    break;

                case 19:
                    AimAtSeat(_seat);
                    if (InStage < 0.8) break;
                    ClassroomSeats.PressForTest = true;
                    Next();
                    break;

                case 20:
                    if (!_local.controller.IsSeated) { AimAtSeat(_seat); if (InStage > 3) Finish(false, "não sentou pela terceira vez"); break; }
                    if (InStage < 0.8) break;
                    _local.controller.GetCaught();
                    Next();
                    break;

                case 21:
                    if (InStage < 0.5) break;
                    if (_local.controller.IsSeated || _local.Seat >= 0) { Finish(false, "pego sentado, continuou na cadeira"); return; }
                    if (ClassroomSeats.Taken(_seat, ulong.MaxValue)) { Finish(false, "pego sentado, a cadeira continuou ocupada"); return; }
                    var keys = new List<(string key, string action)>();
                    LevelTasks.CollectKeys(keys);
                    if (!keys.Exists(k => k.action.Contains("desk chair"))) { Finish(false, "a tecla de sentar não está na lista do menu de pausa"); return; }
                    Finish(true, string.Join("; ", Notes) + "; pego sentado, a cadeira ficou livre; a tecla está no menu de pausa");
                    break;
            }
        }

        // ---------------------------------------------------------------- Dois editores

        static void TickHost(GameSession session)
        {
            PlayerNetwork other = Other();
            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    OfficeLightWave.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host();
                    Next();
                    break;

                case 1:
                    if (!Ready(session)) break;
                    if (!PickBoardAndSeats()) return;
                    GoToBoard();
                    Next();
                    break;

                case 2: case 3: case 4: case 5:
                    if (!DrawOnBoard()) break;
                    Next();
                    break;

                case 6:
                    if (!CheckBoardStroke()) return;
                    Tap(Key.Digit1);
                    GoBehindSeat(_seat);
                    Next();
                    break;

                case 7:
                    Release();
                    AimAtSeat(_seat);
                    if (InStage < 0.8) break;
                    ClassroomSeats.PressForTest = true;
                    Next();
                    break;

                case 8:
                    if (!_local.controller.IsSeated) { AimAtSeat(_seat); if (InStage > 3) Finish(false, "o host não sentou"); break; }
                    Next($"o host riscou a lousa e sentou na carteira {_seat} antes de o cliente entrar");
                    break;

                case 9:
                    // Espera o cliente entrar, ser recusado na cadeira do host e sentar noutra.
                    if (!_local.controller.IsSeated) { Finish(false, "o host levantou sozinho"); return; }
                    if (other == null || other.Seat < 0) { if (InStage > 150) Finish(false, $"o cliente não sentou (recusas dadas: {_seats.Refusals})"); break; }
                    if (other.Seat == _seat) { Finish(false, "o cliente sentou na cadeira do host"); return; }
                    if (_seats.Refusals < 1) { Finish(false, "o host não recusou nenhum pedido"); return; }
                    Next($"o host recusou a cadeira dele ao cliente ({_seats.Refusals} recusa(s)) e o cliente sentou na {other.Seat}");
                    break;

                case 10:
                    if (InStage < 1.5) break;
                    if (other == null || other.figure == null || !other.figure.Seated) { Finish(false, "o host não vê o cliente sentado (pose)"); return; }
                    Photo("rede_host_ve_cliente", _seats.FeetWorld(other.Seat) + Quaternion.Euler(0f, _seats.YawWorld(other.Seat), 0f) * new Vector3(1.6f, 1.4f, 1.2f), other.transform.position + Vector3.up * 0.8f);
                    Next("o host vê o cliente sentado");
                    break;

                case 11:
                    // Fica sentado mais um pouco (o cliente fotografa) e levanta.
                    if (InStage < 8) break;
                    ClassroomSeats.PressForTest = true;
                    Next();
                    break;

                case 12:
                    if (_local.controller.IsSeated) { if (InStage > 2) Finish(false, "o host não levantou"); break; }
                    if (InStage < 8) break;
                    Finish(true, string.Join("; ", Notes) + "; o host levantou");
                    break;
            }
        }

        static void TickJoin(GameSession session)
        {
            PlayerNetwork host = Other();
            switch (_stage)
            {
                case 0:
                    // Entra depois: o host já riscou e sentou.
                    if (Now - _began < 25.0) break;
                    if (session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null) { Next(); break; }
                    if (session.Phase == SessionPhase.Menu && Now - _lastTry > 4.0)
                    {
                        _lastTry = Now;
                        session.Join("127.0.0.1");
                    }
                    break;

                case 1:
                    if (!Ready(session)) break;
                    if (InStage < 3.0) break;
                    if (!PickBoardAndSeats()) return;
                    if (host == null || host.Seat < 0) { if (InStage > 20) Finish(false, "quem entrou depois não vê o host sentado (Seat)"); break; }
                    if (host.Seat != _seat) Notes.Add($"o host sentou na {host.Seat} (o cliente esperava a {_seat})");
                    _seat = host.Seat;
                    if (!CheckBoardStroke()) return;
                    Next("quem entrou depois recebeu o traço da lousa (marcador, na face dela)");
                    break;

                case 2:
                {
                    if (InStage < 1.0) break;
                    if (host == null) { Finish(false, "o host sumiu"); return; }
                    if (host.figure == null || !host.figure.Seated) { Finish(false, "quem entrou depois não vê o host na pose sentada"); return; }
                    float yawOff = Mathf.DeltaAngle(host.figure.transform.eulerAngles.y, _seats.YawWorld(_seat));
                    if (Mathf.Abs(yawOff) > 5f) { Finish(false, $"o corpo do host sentado está virado {yawOff:F0}° da frente da cadeira"); return; }
                    float off = Vector3.Distance(Flat(host.transform.position), Flat(_seats.FeetWorld(_seat)));
                    if (off > 0.3f) { Finish(false, $"o host sentado aparece a {off:F2} m da cadeira"); return; }
                    Photo("rede_traje_sentado", _seats.FeetWorld(_seat) + Quaternion.Euler(0f, _seats.YawWorld(_seat), 0f) * new Vector3(1.5f, 1.45f, 1.3f), host.transform.position + Vector3.up * 0.8f);
                    Photo("rede_traje_sentado_lado", _seats.FeetWorld(_seat) + Quaternion.Euler(0f, _seats.YawWorld(_seat), 0f) * new Vector3(1.9f, 1.0f, 0.1f), host.transform.position + Vector3.up * 0.7f);
                    // Tenta a cadeira do host: o aviso diz ocupada e, pedindo assim mesmo, o host recusa.
                    GoBehindSeat(_seat);
                    _refusals = _seats.RefusalsReceived;
                    Next("quem entrou depois vê o host sentado, virado para a lousa, no lugar da cadeira");
                    break;
                }

                case 3:
                    AimAtSeat(_seat);
                    if (InStage < 0.8) break;
                    if (ClassroomSeats.Prompt != ClassroomSeats.OccupiedText) { if (InStage > 4) Finish(false, $"mirando a cadeira do host, o aviso foi \"{ClassroomSeats.Prompt}\""); break; }
                    _local.RequestSeatRpc((short)_seat);
                    Next("mirando a cadeira do host o aviso é SEAT TAKEN");
                    break;

                case 4:
                    if (_seats.RefusalsReceived == _refusals) { if (InStage > 4) Finish(false, "o host não recusou a cadeira dele ao cliente"); break; }
                    if (_local.controller.IsSeated) { Finish(false, "o cliente sentou na cadeira do host"); return; }
                    _seat2 = NeighborSeat(_seat);
                    if (_seat2 < 0) { Finish(false, "sem outra carteira"); return; }
                    GoBehindSeat(_seat2);
                    Next("pedindo a cadeira do host, o host recusou");
                    break;

                case 5:
                    AimAtSeat(_seat2);
                    if (InStage < 1.8) break;
                    ClassroomSeats.PressForTest = true;
                    Next();
                    break;

                case 6:
                    if (!_local.controller.IsSeated) { AimAtSeat(_seat2); if (InStage > 4) Finish(false, "o cliente não sentou na outra carteira"); break; }
                    if (!CheckSeated(_seat2)) return;
                    Next($"o cliente sentou na carteira {_seat2}");
                    break;

                case 7:
                    // Espera o host levantar.
                    if (host == null) { Finish(false, "o host sumiu"); return; }
                    if (host.Seat >= 0 || host.figure.Seated) { if (InStage > 40) Finish(false, "o host não levantou (visto do cliente)"); break; }
                    if (InStage < 1.0) break;
                    if (host.figure.transform.localRotation != Quaternion.identity) { Finish(false, "o corpo do host não voltou a ficar reto ao levantar"); return; }
                    Finish(true, string.Join("; ", Notes) + "; o host levantou e a pose sumiu no cliente");
                    break;
            }
        }

        // ---------------------------------------------------------------- Ajudantes

        static bool Ready(GameSession session)
        {
            if (session.Phase != SessionPhase.Playing) return false;
            _local = PlayerNetwork.Local;
            _level = Object.FindAnyObjectByType<LevelBuilder>();
            _seats = ClassroomSeats.Instance;
            if (_local == null || _level == null || _level.Data == null || _seats == null || DrawingBoard.Instance == null) return false;
            return InStage >= 1.5;
        }

        static PlayerNetwork Other()
        {
            foreach (PlayerNetwork p in PlayerNetwork.All)
                if (p != null && p != PlayerNetwork.Local && p.IsSpawned && p.InGame) return p;
            return null;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // Uma lousa numa sala acesa (senão, qualquer uma) e a carteira mais perto dela, na mesma sala.
        static bool PickBoardAndSeats()
        {
            if (_seats.Count == 0 || _seats.Boards.Count == 0) { Finish(false, $"{_seats.Count} carteiras e {_seats.Boards.Count} lousas na seed (tente outra com -seed)"); return false; }

            int best = -1, bestSeat = -1;
            bool bestLit = false;
            for (int b = 0; b < _seats.Boards.Count; b++)
            {
                Bounds board = _seats.Boards[b];
                Vector3 center = _seats.transform.TransformPoint(board.center);
                if (!RoomAt(center, out OfficeRoom room)) continue;
                int seat = -1;
                float near = float.PositiveInfinity;
                for (int s = 0; s < _seats.Count; s++)
                {
                    if (!RoomAt(_seats.FeetWorld(s), out OfficeRoom other) || other.X0 != room.X0 || other.Z0 != room.Z0) continue;
                    float d = Vector3.Distance(Flat(_seats.FeetWorld(s)), Flat(center));
                    if (d < near) { near = d; seat = s; }
                }
                if (seat < 0) continue;
                if (best >= 0 && (bestLit || !room.Lit)) continue;
                best = b;
                bestSeat = seat;
                bestLit = room.Lit;
            }
            if (best < 0) { Finish(false, "nenhuma lousa com carteira na mesma sala"); return false; }

            _board = best;
            _seat = bestSeat;
            _seatFeet = _seats.FeetWorld(_seat);
            Bounds chosen = _seats.Boards[_board];
            _boardCenter = _seats.transform.TransformPoint(chosen.center);
            Vector3 axis = chosen.size.x < chosen.size.z ? Vector3.right : Vector3.forward;
            axis = _seats.transform.TransformDirection(axis);
            _boardNormal = Vector3.Dot(_seatFeet - _boardCenter, axis) > 0f ? axis : -axis;
            _boardCenter += _boardNormal * (Mathf.Min(chosen.size.x, chosen.size.z) * 0.5f);   // a face da frente
            _seat2 = NeighborSeat(_seat);
            if (!bestLit) Notes.Add("a lousa escolhida fica numa sala apagada");
            return true;
        }

        static bool RoomAt(Vector3 world, out OfficeRoom found)
        {
            Vector3 local = _level.transform.InverseTransformPoint(world);
            int x = Mathf.FloorToInt(local.x / LevelBuilder.Cell), z = Mathf.FloorToInt(local.z / LevelBuilder.Cell);
            foreach (OfficeRoom room in _level.Data.OfficeRooms)
                if (room.Contains(x, z)) { found = room; return true; }
            found = default;
            return false;
        }

        static int NeighborSeat(int seat)
        {
            int best = -1;
            float near = float.PositiveInfinity;
            Vector3 at = _seats.FeetWorld(seat);
            for (int s = 0; s < _seats.Count; s++)
            {
                if (s == seat) continue;
                float d = Vector3.Distance(_seats.FeetWorld(s), at);
                if (d < near) { near = d; best = s; }
            }
            return near < 3f ? best : -1;
        }

        // De frente para a lousa, a 1,2 m, com a caneta preta na mão.
        static void GoToBoard()
        {
            Vector3 at = _boardCenter + _boardNormal * 1.2f + Vector3.left * 0f;
            float floor = _seatFeet.y;
            Vector3 side = Vector3.Cross(Vector3.up, _boardNormal).normalized;
            _local.controller.Teleport(new Vector3(at.x, floor + 0.05f, at.z) - side * 0.4f, Yaw(-_boardNormal));
        }

        static int _drawStep;

        // Mira a lousa e risca: puxa a caneta (1), segura o botão e arrasta para o lado e para baixo.
        static bool DrawOnBoard()
        {
            switch (_drawStep)
            {
                case 0:
                    LookAt(_boardCenter + Vector3.up * 0.15f);
                    if (InStage < 0.6) return false;
                    Tap(Key.Digit1);
                    _drawStep = 1;
                    return true;
                case 1:
                    Release();
                    if (InStage < 0.5) return false;
                    if (!_local.pen.Equipped) { Finish(false, "a tecla 1 não puxou a caneta"); return false; }
                    _strokesBefore = DrawingBoard.Instance.StrokeCount;
                    _drawStep = 2;
                    return true;
                case 2:
                {
                    // Botão apertado e a mira varrendo a lousa de um lado ao outro, descendo um pouco.
                    if (InStage < 1.4)
                    {
                        float t = (float)InStage / 1.4f;
                        Vector3 side = Vector3.Cross(Vector3.up, _boardNormal).normalized;
                        LookAt(_boardCenter + side * Mathf.Lerp(-0.6f, 0.6f, t) + Vector3.up * (0.25f - 0.3f * t));
                        Drag(true, 0f, 0f);
                        return false;
                    }
                    Drag(false, 0f, 0f);
                    _drawStep = 3;
                    return true;
                }
                default:
                    if (InStage < 0.6) return false;
                    _drawStep = 0;
                    return true;
            }
        }

        static bool CheckBoardStroke()
        {
            DrawingBoard board = DrawingBoard.Instance;
            if (board.MarkerCount < 1)
            {
                string aim = _local.pen.TryAim(out RaycastHit hit)
                    ? $"a mira bate em {hit.collider.name} ({hit.collider.GetType().Name}) em {hit.point}, a {Vector3.Dot(hit.point - _boardCenter, _boardNormal) * 100f:F1} cm do plano da lousa, IsWhiteboard {ClassroomSeats.IsWhiteboard(hit.point)}"
                    : "a mira não bate em nada";
                Finish(false, $"nenhum traço de marcador na lousa ({board.StrokeCount} traços ao todo); lousa em {_boardCenter}, normal {_boardNormal}; jogador em {_local.transform.position}; {aim}");
                return false;
            }
            if (!board.MarkerStroke(StrokePoints, out Vector3 normal)) { Finish(false, "sem traço na lousa"); return false; }
            if (Vector3.Dot(normal, _boardNormal) < 0.98f) { Finish(false, $"o traço da lousa tem a normal {normal} (a lousa, {_boardNormal})"); return false; }
            float worst = 0f;
            foreach (Vector3 p in StrokePoints) worst = Mathf.Max(worst, Mathf.Abs(Vector3.Dot(p - _boardCenter, _boardNormal)));
            if (worst > 0.01f) { Finish(false, $"um ponto do traço fica a {worst * 100f:F1} cm da face da lousa"); return false; }
            Notes.Add($"traço de marcador na lousa: {StrokePoints.Count} pontos, todos a até {worst * 1000f:F1} mm da face dela");
            return true;
        }

        // Atrás da cadeira, a 0,9 m, olhando para ela.
        static void GoBehindSeat(int seat)
        {
            Vector3 feet = _seats.FeetWorld(seat);
            Vector3 back = Quaternion.Euler(0f, _seats.YawWorld(seat), 0f) * Vector3.back;
            _local.controller.Teleport(feet + back * 0.9f + Vector3.up * 0.05f, Yaw(-back));
        }

        static void AimAtSeat(int seat) => LookAt(_seats.FeetWorld(seat) + Vector3.up * 0.55f);

        static void LookAt(Vector3 target)
        {
            Transform eye = _local.playerCamera.transform;
            Vector3 to = target - eye.position;
            Vector3 flat = Flat(to);
            if (flat.sqrMagnitude > 1e-4f) _local.transform.rotation = Quaternion.Euler(0f, Yaw(flat), 0f);
            _local.controller.SetPitch(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
        }

        static bool CheckSeated(int seat)
        {
            PlayerController body = _local.controller;
            if (!body.IsSeated || body.Seat != seat) { Finish(false, $"não está sentado na carteira {seat} (Seat {body.Seat})"); return false; }
            Vector3 feet = _seats.FeetWorld(seat);
            if (Vector3.Distance(Flat(_local.transform.position), Flat(feet)) > 0.05f) { Finish(false, "sentado fora do lugar da cadeira"); return false; }
            float eye = _local.playerCamera.transform.position.y - feet.y;
            if (Mathf.Abs(eye - ClassroomSeats.SeatEye) > 0.06f) { Finish(false, $"sentado, o olho fica a {eye:F2} m do chão (devia ser {ClassroomSeats.SeatEye:F2})"); return false; }
            Vector3 forward = Flat(_local.transform.forward).normalized;
            if (Vector3.Dot(forward, -_boardNormal) < 0.9f && seat == _seat) { Finish(false, "sentado, não ficou de frente para a lousa"); return false; }
            _seatFeet = feet;
            return true;
        }

        static readonly Collider[] Overlaps = new Collider[16];

        // De pé fora da cadeira: a cápsula não toca nada, há chão embaixo e fica perto da cadeira.
        static bool CheckStandingSpot(string how)
        {
            CharacterController cc = _local.GetComponent<CharacterController>();
            Vector3 feet = _local.transform.position;
            float r = cc.radius * 0.95f;
            int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (r + 0.05f), feet + Vector3.up * (_local.controller.standingHeight - r), r, Overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Overlaps[i].transform.root != _local.transform.root) { Finish(false, $"levantou ({how}) dentro de {Overlaps[i].name}"); return false; }
            if (!Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out RaycastHit ground, 0.6f, ~0, QueryTriggerInteraction.Ignore)) { Finish(false, $"levantou ({how}) sem chão embaixo"); return false; }
            float d = Vector3.Distance(Flat(feet), Flat(_seats.FeetWorld(_seat)));
            if (d > 3.2f) { Finish(false, $"levantou ({how}) a {d:F1} m da cadeira"); return false; }
            Notes.Add($"de pé ({how}) a {d:F2} m da cadeira");
            return true;
        }

        static float Yaw(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        // ---------------------------------------------------------------- Teclado e mouse virtuais

        static void SetUpDevices()
        {
            _backgroundBefore = InputSystem.settings.backgroundBehavior;
            _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            PlayerController.AssumeMouseCaptured = true;
        }

        static void Tap(Key key) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
        static void Hold(Key key) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
        static void Release() => InputSystem.QueueStateEvent(_keyboard, new KeyboardState());

        static void Drag(bool down, float dx, float dy)
        {
            var state = new MouseState { delta = new Vector2(dx, dy) };
            InputSystem.QueueStateEvent(_mouse, down ? state.WithButton(MouseButton.Left) : state);
        }

        // ---------------------------------------------------------------- Fotos

        static string Folder
        {
            get
            {
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews", "salas"));
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        /// <summary>
        /// Foto pela câmera do jogador (com a filmadora). Com <paramref name="from"/>, a câmera sai do
        /// lugar só para a foto e volta.
        /// </summary>
        static void Photo(string name, Vector3? from, Vector3 look)
        {
            Camera camera = _local.playerCamera;
            Transform t = camera.transform;
            Vector3 position = t.localPosition;
            Quaternion rotation = t.localRotation;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                if (from.HasValue) t.SetPositionAndRotation(from.Value, Quaternion.LookRotation(look - from.Value, Vector3.up));
                camera.targetTexture = target;
                Level0Preview.Shoot(camera, target, readback, Path.Combine(Folder, $"{name}.png"));
            }
            catch (System.Exception e)
            {
                Notes.Add($"foto {name} falhou: {e.Message}");
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                t.localPosition = position;
                t.localRotation = rotation;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
            }
        }

        // A sala vista do fundo, de frente para a lousa.
        static void PhotoRoom()
        {
            Vector3 back = _boardCenter + _boardNormal * 6.5f;
            back.y = _seatFeet.y + 1.7f;
            Photo("sala", back, _boardCenter - Vector3.up * 0.4f);
        }

        // O traje sentado (uma figura avulsa na carteira vizinha), visto de lado e de frente.
        static void PhotoDummy()
        {
            if (_seat2 < 0 || _local.figure == null) return;
            _dummy = new GameObject("TrajeSentado_Teste");
            _dummy.SetActive(false);
            PlayerBody body = _dummy.AddComponent<PlayerBody>();
            body.suitMaterial = _local.figure.suitMaterial;
            body.rubberMaterial = _local.figure.rubberMaterial;
            body.visorMaterial = _local.figure.visorMaterial;
            body.gearMaterial = _local.figure.gearMaterial;
            Vector3 feet = _seats.FeetWorld(_seat2);
            Quaternion front = Quaternion.Euler(0f, _seats.YawWorld(_seat2), 0f);
            _dummy.transform.SetPositionAndRotation(feet, front);
            _dummy.SetActive(true);
            body.SetSeated(true, 25f);
            body.Pose(0f, 0f, 0f, 10f, 0f, 0f, 0f, 1f, 25f);
            Photo("traje_sentado_lado", feet + front * new Vector3(1.9f, 1.0f, 0.15f), feet + Vector3.up * 0.65f);
            Photo("traje_sentado_frente", feet + front * new Vector3(0.6f, 1.5f, 1.9f), feet + Vector3.up * 0.75f);
            Object.Destroy(_dummy);
            _dummy = null;
        }

        // ---------------------------------------------------------------- Fim

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;
            ClassroomSeats.OccupiedForTest = -1;
            OfficeLightWave.Paused = false;
            EntityDirector.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
            LevelProgress.DevModeOverride = null;
            if (_dummy != null) Object.Destroy(_dummy);

            if (_mouse != null) InputSystem.RemoveDevice(_mouse);
            PlayerController.AssumeMouseCaptured = false;
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                InputSystem.settings.backgroundBehavior = _backgroundBefore;
                InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBefore;
            }

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log($"[BackRooms] Teste das carteiras ({Role}) " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

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
