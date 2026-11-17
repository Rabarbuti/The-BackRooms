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
    /// Teste em Play das regras do Capitão no Level 1 de teste (level1-mostruario.md, seção 5.4).
    /// Hospeda, escolhe duas células com visada livre entre elas (o jogador numa, o Capitão
    /// noutra, a 8–14 m, sob uma luminária que funciona) e confere, em ordem:
    ///
    /// 1. de costas para ele, o Capitão anda até o jogador;
    /// 2. olhado com a luz acesa, congela: não anda, o corpo segura a pose;
    /// 3. no apagão, olhado sem lanterna, anda, e em arrancadas (mais de 4 m/s);
    /// 4. no apagão, com a lanterna nele, congela;
    /// 5. no apagão, sem lanterna, pega quem fica parado na frente dele;
    /// 6. com a luz de volta, encarado sem parar: depois de uns 9 s a imagem do jogador some no
    ///    chuvisco e ele volta a andar.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level1CaptainTest.BatchRun (sai com
    /// 0 se passou, 3 se falhou). Menu: BackRooms > Testar Capitão (Level 1). O resultado é a
    /// linha "[BackRooms] Teste do Capitão PASSOU/FALHOU" do log.
    ///
    /// Dois editores (o segundo numa cópia do projeto): <see cref="BatchHost"/> faz o mesmo teste
    /// esperando um cliente entrar; <see cref="BatchJoin"/> entra e confere, do lado do cliente,
    /// que o Capitão aparece, anda e congela com o corpo segurando a pose (o congelamento vem do
    /// host pela rede).
    /// </summary>
    public static class Level1CaptainTest
    {
        const string ActiveKey = "BackRooms.Level1CaptainTest.Active";
        const string BatchKey = "BackRooms.Level1CaptainTest.Batch";
        const string ExitCodeKey = "BackRooms.Level1CaptainTest.ExitCode";
        const string RoleKey = "BackRooms.Level1CaptainTest.Role";
        const string SoloRole = "solo", HostRole = "host", JoinRole = "join";
        const double Timeout = 150.0;
        const double NetTimeout = 300.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage;
        static double _began;
        static float _stageStart;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static EntityDirector _director;
        static CaptainBrain _captain;
        static Vector3 _playerSpot, _captainSpot, _mark;
        static float _lookStart;
        static int _capturesBefore;
        static bool _sideLook;
        static float _sideStart, _angleBefore;
        static double _lastTry;
        static Vector3 _seenAt;
        static float _farthest;
        static bool _sawFrozen, _sawHolding;
        static bool _finished;
        static int _stageTicks;

        [MenuItem("BackRooms/Testar Capitão (Level 1)")]
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
            EditorSceneManager.OpenScene(Level1Setup.ScenePath, OpenSceneMode.Single);
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

        // Tempo de jogo: as regras do Capitão contam no relógio do jogo, não no do editor.
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
                Finish(false, $"tempo esgotado na etapa {_stage}" + (Role == JoinRole ? ClientReport() : ""));
                return;
            }

            if (Role == JoinRole)
            {
                TickJoin(session);
                return;
            }

            _stageTicks++;

            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                {
                    if (session.Phase != SessionPhase.Playing) break;
                    if (Role == HostRole && CountInGame() < 2) break; // espera o cliente entrar
                    _local = PlayerNetwork.Local;
                    _level = session.level;
                    _director = EntityDirector.Instance;
                    if (_local == null || _level == null || _level.Data == null) break;
                    if (InStage < 1.5f) break;

                    if (_director == null) { Finish(false, "a cena do Level 1 não tem o diretor do inimigo"); return; }
                    if (_director.entityPrefab == null || _director.entityPrefab.GetComponent<CaptainBrain>() == null)
                    {
                        Finish(false, "o inimigo do diretor do Level 1 não é o Capitão");
                        return;
                    }

                    // Sem apagão até o teste pedir.
                    if (LightsOut.Instance != null) LightsOut.Instance.firstDarkAt = 1e6f;

                    if (!PickSpots(out string why)) { Finish(false, why); return; }

                    _local.controller.Teleport(_playerSpot, 0f);
                    _captain = _director.SpawnAt(_captainSpot, _playerSpot - _captainSpot) as CaptainBrain;
                    if (_captain == null) { Finish(false, "o diretor não pôs um Capitão"); return; }
                    // A troca com um totem no apagão tem teste próprio (Level1ShelterTest): aqui
                    // ela o levaria para longe no meio da conferência.
                    _captain.swapChance = 0f;

                    Face(away: true);
                    _captain.Alert(_playerSpot);
                    _mark = _captain.transform.position;
                    Next($"jogador e Capitão a {Flat(_captainSpot - _playerSpot).magnitude:F1} m");
                    break;
                }

                case 2:
                    // 1. De costas: anda até o jogador.
                    if (_captain.Frozen) { Finish(false, "congelou com o jogador de costas"); return; }
                    if (Moved() >= 1.5f) { Next($"de costas, andou {Moved():F1} m em {InStage:F1} s"); break; }
                    if (InStage > 4f) Finish(false, $"de costas, andou só {Moved():F2} m em 4 s (estado {_captain.State}, destino a {Flat(_captain.Goal - _captain.transform.position).magnitude:F1} m dele)");
                    break;

                case 3:
                    // 2. Olhado com luz: congela.
                    Face(away: false);
                    if (!_captain.Frozen)
                    {
                        if (InStage > 1f) Finish(false, $"olhado com a luz acesa, não congelou (iluminado={_captain.IsLit()}, à vista={_captain.InSight(_local)}; {SightReport()})");
                        break;
                    }
                    _mark = _captain.transform.position;
                    Next();
                    break;

                case 4:
                {
                    var body = _captain.GetComponent<CaptainBody>();
                    if (!_sideLook)
                    {
                        if (!_captain.Frozen) { Finish(false, $"descongelou olhado com luz, depois de {InStage:F1} s"); return; }
                        if (InStage < 2f) break;
                        if (Moved() > 0.05f) { Finish(false, $"congelado, mas andou {Moved():F2} m"); return; }
                        if (body == null || !body.Holding) { Finish(false, "congelado, mas o corpo não segurou a pose"); return; }
                        Notes.Add($"olhado com luz congelou, ficou {InStage:F1} s parado com a pose segura");

                        // Os olhos seguem: o jogador vai para o lado dele (uns 70° da frente), olhando.
                        if (!SideSpot(out Vector3 side)) { Finish(false, "não achei lugar ao lado do Capitão para conferir os olhos"); return; }
                        _local.controller.Teleport(side, 0f);
                        _sideLook = true;
                        _sideStart = Now;
                        _angleBefore = -1f;
                        break;
                    }

                    Face(away: false);
                    if (_angleBefore < 0f && Now - _sideStart > 0.15f) _angleBefore = body.HeadAngleTo(_local.playerCamera.transform.position);
                    if (Now - _sideStart < 3f) break;
                    if (!_captain.Frozen) { Finish(false, "olhado de lado com luz, descongelou"); return; }
                    float angle = body.HeadAngleTo(_local.playerCamera.transform.position);
                    if (angle > _angleBefore - 15f) { Finish(false, $"congelado, a cabeça não seguiu o jogador ao lado ({_angleBefore:F0}° -> {angle:F0}°)"); return; }
                    Next($"congelado, a cabeça seguiu o jogador ao lado ({_angleBefore:F0}° -> {angle:F0}°)");

                    // 3. Apagão: escuro já, por bastante tempo.
                    LightsOut power = LightsOut.Instance;
                    power.darkSeconds = 600f;
                    power.firstDarkAt = (float)session.SessionSeconds - 0.05f;
                    break;
                }

                case 5:
                    if (LightsOut.Instance.Phase != PowerPhase.Dark)
                    {
                        if (InStage > 2f) Finish(false, "o apagão forçado não veio");
                        break;
                    }
                    // Recomeça de longe, olhando para ele, sem lanterna.
                    PlaceBoth();
                    Face(away: false);
                    _local.flashlight.SetOn(false);
                    _captain.TopSpeed = 0f;
                    Next();
                    break;

                case 6:
                    // A medida de velocidade só vale depois do salto de recolocá-lo no lugar. Conta
                    // quadros do teste, não tempo: em lote um quadro passa de 0,3 s.
                    if (_stageTicks <= 3) { _captain.TopSpeed = 0f; break; }
                    if (_captain.Frozen && InStage > 0.5f) { Finish(false, "no escuro, olhado sem lanterna, congelou"); return; }
                    if (Moved() >= 2f && _captain.TopSpeed > 4f)
                    {
                        Next($"no apagão, olhado sem lanterna, andou {Moved():F1} m em {InStage:F1} s, a até {_captain.TopSpeed:F1} m/s");
                        _local.flashlight.SetOn(true);
                        break;
                    }
                    if (InStage > 4f) Finish(false, $"no apagão andou {Moved():F2} m em 4 s, a até {_captain.TopSpeed:F1} m/s");
                    break;

                case 7:
                    // 4. Lanterna nele, no escuro: congela.
                    Face(away: false);
                    _local.flashlight.SetOn(true);
                    if (!_captain.Frozen)
                    {
                        if (InStage > 1f) Finish(false, $"no apagão, com a lanterna nele, não congelou (iluminado={_captain.IsLit()}, à vista={_captain.InSight(_local)})");
                        break;
                    }
                    if (InStage < 1.5f) { _mark = _captain.transform.position; break; }
                    if (Moved() > 0.05f) { Finish(false, $"congelado pela lanterna, mas andou {Moved():F2} m"); return; }
                    Next("no apagão, a lanterna nele o congelou");
                    _local.flashlight.SetOn(false);
                    _capturesBefore = _local.controller.Captures;
                    break;

                case 8:
                    // 5. Sem lanterna, parado na frente dele: pega.
                    _local.flashlight.SetOn(false);
                    if (_local.controller.Captures > _capturesBefore)
                    {
                        Next($"no apagão, sem lanterna, pegou o jogador parado em {InStage:F1} s");
                        LightsOut.Instance.firstDarkAt = 1e6f;
                        break;
                    }
                    if (InStage > 10f) Finish(false, $"no apagão, sem lanterna, não pegou o jogador parado em 10 s (a {Flat(_captain.transform.position - _local.transform.position).magnitude:F1} m, estado {_captain.State})");
                    break;

                case 9:
                    // A luz volta; espera o jogador voltar da captura.
                    // E espera o cansaço de encarar das etapas anteriores passar de todo.
                    if (LightsOut.Instance.Phase == PowerPhase.Dark || _local.controller.IsReturning || InStage < 3f || _captain.LocalStare > 0.01f) break;
                    PlaceBoth();
                    Face(away: false);
                    _lookStart = Now;
                    Next();
                    break;

                case 10:
                {
                    // 6. Encarado sem parar: congela e, cansado o olhar, volta a andar.
                    Face(away: false);
                    float looked = Now - _lookStart;
                    if (looked < 1f) { _mark = _captain.transform.position; break; }

                    if (!_captain.Frozen)
                    {
                        if (looked < _captain.stareBlindAfter - 1f)
                        {
                            Finish(false, $"encarado com luz, descongelou cedo, com {looked:F1} s (cansaço {_captain.LocalStare:F1} s)");
                            return;
                        }
                        if (_captain.LocalStare < _captain.stareNoiseAfter)
                        {
                            Finish(false, $"descongelou com {looked:F1} s, mas o cansaço de encarar estava em {_captain.LocalStare:F1} s");
                            return;
                        }
                        Next($"encarado sem parar, voltou a andar com {looked:F1} s (chuvisco a partir de {_captain.stareNoiseAfter:F0} s)");
                        break;
                    }

                    if (looked > _captain.stareBlindAfter + 4f) Finish(false, $"encarado {looked:F1} s sem parar, continuou congelado (cansaço {_captain.LocalStare:F1} s)");
                    break;
                }

                case 11:
                {
                    // A perna de pau marcou o chão pelo caminho todo.
                    var marks = _captain.GetComponent<PegMarks>();
                    if (marks == null || marks.Placed == 0) { Finish(false, "a perna de pau não deixou nenhuma marca no chão"); return; }
                    Finish(true, string.Join("; ", Notes) + $"; congelou {_captain.Freezes} vez(es); a perna de pau deixou {marks.Placed} marca(s)");
                    break;
                }
            }
        }

        static int CountInGame()
        {
            int count = 0;
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.InGame) count++;
            return count;
        }

        // Cliente: entra na sessão do host e olha o Capitão que chega pela rede.
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
                    CaptainBrain captain = CaptainBrain.Captain;
                    if (captain == null || !captain.IsSpawned) break;
                    if (_captain == null)
                    {
                        _captain = captain;
                        _seenAt = captain.transform.position;
                        Notes.Add("o Capitão apareceu no cliente");
                    }

                    _farthest = Mathf.Max(_farthest, Flat(captain.transform.position - _seenAt).magnitude);
                    var body = captain.GetComponent<CaptainBody>();
                    if (captain.Frozen)
                    {
                        _sawFrozen = true;
                        if (body != null && body.Holding) _sawHolding = true;
                    }

                    if (_farthest >= 1f && _sawFrozen && _sawHolding)
                    {
                        Finish(true, string.Join("; ", Notes) + ClientReport());
                        return;
                    }
                    break;
                }
            }

            // O host acabou antes de o cliente ver tudo.
            if (_stage > 0 && session.Phase != SessionPhase.Playing)
                Finish(false, "a sessão acabou antes" + ClientReport());
        }

        static string ClientReport() =>
            $"; no cliente: Capitão visto={_captain != null}, andou até {_farthest:F1} m, congelado={_sawFrozen}, pose segura={_sawHolding}";

        // Duas células com piso, fora da sala inicial, a 8–14 m em linha reta, com visada livre
        // do olho do jogador a três alturas do Capitão, caminho curto entre elas e uma luminária
        // que funciona (acesa, sem piscar) iluminando a célula do Capitão.
        static bool PickSpots(out string why)
        {
            why = "";
            var nav = new LevelNav(_level);
            LevelData data = _level.Data;
            int[,] reach = nav.NewMap();
            Transform root = _level.transform;
            var cells = new List<Vector2Int>();

            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++)
                    if (nav.Standable(x, z)) cells.Add(new Vector2Int(x, z));

            int tried = 0, noLamp = 0;
            foreach (Vector2Int b in cells)
            {
                Vector3 captainAt = nav.CellCenter(b.x, b.y);
                if (!LampOver(captainAt + Vector3.up * 1.5f, root)) { noLamp++; continue; }
                nav.Distances(b.x, b.y, reach);

                foreach (Vector2Int a in cells)
                {
                    Vector3 playerAt = nav.CellCenter(a.x, a.y);
                    float straight = Flat(captainAt - playerAt).magnitude;
                    if (straight < 8f || straight > 14f) continue;
                    if (reach[a.x, a.y] < 0 || reach[a.x, a.y] > Mathf.RoundToInt(straight / LevelBuilder.Cell) + 1) continue;
                    tried++;

                    Vector3 eye = playerAt + Vector3.up * 1.65f;
                    if (Blocked(eye, captainAt + Vector3.up * 0.6f, root) ||
                        Blocked(eye, captainAt + Vector3.up * 1.5f, root) ||
                        Blocked(eye, captainAt + Vector3.up * 2.35f, root)) continue;

                    _playerSpot = playerAt;
                    _captainSpot = captainAt;
                    return true;
                }
            }

            why = $"nenhum par de células serve ({cells.Count} com piso, {noLamp} sem luminária boa em cima, {tried} pares na distância certa sem visada livre)";
            return false;
        }

        static bool LampOver(Vector3 chest, Transform root)
        {
            foreach (CeilingLight lamp in CeilingLight.All)
            {
                if (lamp == null || lamp.State != LightState.On || !lamp.IsLit) continue;
                Vector3 from = lamp.LightPosition + Vector3.down * 0.3f;
                if (Vector3.Distance(from, chest) > 5.5f) continue;
                if (!Blocked(from, chest, root)) return true;
            }
            return false;
        }

        // Um ponto a ~3,5 m do Capitão, de 60 a 85° da frente dele, com chão e visada livre até a
        // cabeça dele (para conferir que a cabeça vira).
        static bool SideSpot(out Vector3 spot)
        {
            spot = Vector3.zero;
            var nav = new LevelNav(_level);
            Transform root = _level.transform;
            Vector3 at = _captain.transform.position;
            Vector3 forward = Flat(_captain.transform.forward).normalized;
            foreach (float angle in new[] { 70f, -70f, 60f, -60f, 80f, -80f, 85f, -85f })
                foreach (float radius in new[] { 3.5f, 3f, 4f })
                {
                    Vector3 p = at + Quaternion.Euler(0f, angle, 0f) * forward * radius;
                    if (!nav.WorldToCell(p, out int x, out int z) || _level.Data.InRoom(x, z) || _level.Data.IsBarricade(x, z)) continue;
                    if (Blocked(p + Vector3.up * 1.65f, at + Vector3.up * 2.2f, root)) continue;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.35f, p + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    spot = new Vector3(p.x, _local.transform.position.y, p.z);
                    return true;
                }
            return false;
        }

        static void PlaceBoth()
        {
            _local.controller.Teleport(_playerSpot, _local.transform.eulerAngles.y);
            _director.SpawnAt(_captainSpot, _playerSpot - _captainSpot);
            _captain.Alert(_playerSpot);
            _mark = _captain.transform.position;
        }

        // Vira o jogador de frente (ou de costas) para o Capitão, no lugar.
        static void Face(bool away)
        {
            Vector3 to = Flat(_captain.transform.position - _local.transform.position);
            if (away) to = -to;
            if (to.sqrMagnitude < 0.01f) return;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            _local.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // Onde cada ponto do corpo do Capitão cai na câmera do jogador e o que tapa a visada.
        static string SightReport()
        {
            Camera camera = _local.playerCamera;
            if (camera == null) return "sem câmera";
            Transform eye = camera.transform;
            var parts = new List<string> { $"câmera em {eye.position} olhando {eye.forward}, fov {camera.fieldOfView:F0}, jogador em {_local.transform.position}" };
            foreach (float h in new[] { 2.35f, 1.5f, 0.6f })
            {
                Vector3 point = _captain.transform.position + Vector3.up * h;
                Vector3 local = eye.InverseTransformPoint(point);
                string block = "livre";
                Vector3 delta = point - eye.position;
                foreach (RaycastHit hit in Physics.RaycastAll(eye.position, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider.transform.IsChildOf(_level.transform)) { block = "tapado por " + hit.collider.name; break; }
                parts.Add($"{h:F2} m: local {local}, {block}");
            }
            return string.Join(" | ", parts);
        }

        static float Moved() => Flat(_captain.transform.position - _mark).magnitude;

        static bool Blocked(Vector3 from, Vector3 to, Transform root)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            foreach (RaycastHit hit in Physics.RaycastAll(from, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.IsChildOf(root)) return true;
            return false;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        static void Finish(bool ok, string report)
        {
            // Um resultado só: sair do Play ainda pode passar por aqui de novo.
            if (_finished) return;
            _finished = true;

            EntityDirector.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log("[BackRooms] Teste do Capitão " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

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
