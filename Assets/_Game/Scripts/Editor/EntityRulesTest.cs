using System.Collections.Generic;
using System.Reflection;
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
    /// Teste em Play das regras da entidade de 20/11/2026 (historico.md), no molde do
    /// <see cref="Level1CaptainTest"/>. Duas partes, uma cena cada:
    ///
    /// <b>Level 0</b>
    /// 1. Campo de visão (130°, 26 m): parada (velocidades zeradas, para não virar), com o jogador de
    ///    pé e quieto, não vê nem pega quem está colado nas costas (0,9 m) nem do lado (1 m a 80°); a
    ///    uns 6–10 m não vê a 75° da frente e vê a 55°; de frente, não vê a mais de 26 m e vê a ~20 m.
    /// 2. Cansaço na caça (<see cref="EntityBrain.chaseBurstSeconds"/>): caçando um jogador que não
    ///    a olha, corre ~4,6 m/s nos primeiros 7 s, ~2,3 nos 5 s seguintes e volta a correr; a pressa de
    ///    quem não é visto não vale na caça. Ela é posta de volta longe quando chega perto, sem sair da caça.
    /// 3. Pressa sem ser vista (<see cref="EntityBrain.unseenSpeedFactor"/>): indo atrás de um barulho,
    ///    vista por um jogador na sala inicial (abrigo: ela não o percebe), anda a ~3,4 m/s; sem ninguém
    ///    olhando, a ~5,1.
    /// 4. Por fim, de frente e colado (1 m): pega.
    ///
    /// <b>Level 3</b> (abrigo, <see cref="LevelData.IsShelter"/>), na planta, fora do Play: em várias
    /// seeds, a sala inicial e as salinhas acesas são abrigo; as apagadas e o resto, não. (A entidade
    /// do Level 0 saiu do Escritório; os inimigos novos de lá têm testes próprios.)
    ///
    /// A velocidade é lida do campo privado <c>_velocity</c> por reflexão (o teste não muda o
    /// <see cref="EntityBrain"/>).
    ///
    /// Linha de comando: <see cref="BatchRun"/> (as duas partes; <see cref="BatchRunLevel0"/> e
    /// <see cref="BatchRunShelter"/> uma só, esta sem Play). Linha "[BackRooms] Teste das regras da entidade".
    /// Dois editores: <see cref="BatchHost"/> faz a parte do Level 0 com um cliente na sala inicial
    /// (olhando o chão, para não contar como olhar) e manda a posição dela pela sonda
    /// (<see cref="NetProbe"/>); <see cref="BatchJoin"/> confere que vê a entidade andar, caçar, e que a
    /// posição dela no cliente bate com a do host no mesmo instante do relógio da sessão.
    /// </summary>
    public static class EntityRulesTest
    {
        const string ActiveKey = "BackRooms.EntityRulesTest.Active";
        const string BatchKey = "BackRooms.EntityRulesTest.Batch";
        const string ExitCodeKey = "BackRooms.EntityRulesTest.ExitCode";
        const string RoleKey = "BackRooms.EntityRulesTest.Role";
        const string PartKey = "BackRooms.EntityRulesTest.Part";
        const string BothKey = "BackRooms.EntityRulesTest.Both";
        const string FirstKey = "BackRooms.EntityRulesTest.FirstReport";
        const string FirstOkKey = "BackRooms.EntityRulesTest.FirstOk";
        const string SoloRole = "solo", HostRole = "host", JoinRole = "join";
        const string Level0Scene = "Assets/_Game/Scenes/Level0_Test.unity";
        const double Timeout = 240.0;
        const double NetTimeout = 360.0;

        static readonly int[] OfficeSeeds = { 1998, 7, 42, 2024, 31337, 555, 123, 9001 };

        static readonly FieldInfo VelocityField = typeof(EntityBrain).GetField("_velocity", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo ChaseSinceField = typeof(EntityBrain).GetField("_chaseSince", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks;
        static double _began;
        static float _stageStart;
        static bool _finished;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static LevelNav _nav;
        static EntityDirector _director;
        static EntityBrain _entity;
        static float _roam, _search, _investigate, _chase;

        // Campo de visão: as sondas, uma de cada vez.
        struct Probe
        {
            public string Label;
            public Vector3 Entity, Facing, Player;
            public bool ExpectSeen;
        }

        static readonly List<Probe> Probes = new List<Probe>();
        static int _probe;
        static int _probeStep;
        static bool _probeSeen;
        static int _capturesBefore;

        // Cansaço e pressa.
        static Vector3 _from, _to, _shelterEye;
        static float _burst1, _tiredMax, _tiredMin = float.MaxValue, _burst2, _chaseMax;
        static int _tiredSamples, _resets;
        static float _watchedMax, _unwatchedMax;
        static int _watchedSamples, _unwatchedSamples;
        static float _watchSince;
        static bool _watchWas;

        // Cliente.
        static readonly List<Vector4> ClientTrack = new List<Vector4>(); // segundos da sessão, x, z, estado
        static readonly List<Vector3> HostTrack = new List<Vector3>(); // segundos do host, x, z
        static bool _wasPlaying;
        static double _lastTry;
        static float _clientFarthest;
        static Vector3 _clientFirst;
        static bool _clientSawChase, _clientSawEntity;
        static double _probeAt;

        [MenuItem("BackRooms/Testar regras da entidade")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Start(false, SoloRole, 0, true);
        }

        public static void BatchRun() { Start(true, SoloRole, 0, true); }
        public static void BatchRunLevel0() { Start(true, SoloRole, 0, false); }
        public static void BatchRunShelter()
        {
            bool ok = CheckShelterData(out string report);
            Debug.Log("[BackRooms] Teste das regras da entidade " + (ok ? "PASSOU" : "FALHOU") + " (abrigo do Level 3): " + report);
            Level1Setup.ExitWithLevel0Open(ok ? 0 : 3);
        }
        public static void BatchHost() { Start(true, HostRole, 0, false); }
        public static void BatchJoin() { Start(true, JoinRole, 0, false); }

        static void Start(bool batch, string role, int part, bool both)
        {
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(RoleKey, role);
            SessionState.SetBool(BothKey, both);
            SessionState.EraseString(FirstKey);
            SessionState.EraseBool(FirstOkKey);
            OpenPart(part);
        }

        static void OpenPart(int part)
        {
            SessionState.SetInt(PartKey, part);
            EditorSceneManager.OpenScene(Level0Scene, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            EditorApplication.EnterPlaymode();
        }

        static string Role => SessionState.GetString(RoleKey, SoloRole);
        static int Part => SessionState.GetInt(PartKey, 0);

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (SessionState.GetInt(ExitCodeKey, -1) >= 0)
            {
                EditorApplication.playModeStateChanged -= AfterPlay;
                EditorApplication.playModeStateChanged += AfterPlay;
                if (!EditorApplication.isPlayingOrWillChangePlaymode) AfterPlay(PlayModeStateChange.EnteredEditMode);
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
            if (EditorApplication.timeSinceStartup - _began > (Role == SoloRole ? Timeout : NetTimeout))
            {
                Finish(false, $"tempo esgotado na etapa {_stage}" + (Role == JoinRole ? ClientReport() : ""));
                return;
            }

            if (Role == JoinRole) { TickJoin(session); return; }

            _stageTicks++;
            if (VelocityField == null || ChaseSinceField == null)
            {
                Finish(false, "o EntityBrain não tem mais os campos _velocity/_chaseSince que o teste lê");
                return;
            }

            // Host: a posição da entidade para o cliente comparar.
            if (Role == HostRole && _entity != null && session.SessionSeconds >= 0.0 && EditorApplication.timeSinceStartup - _probeAt > 0.2)
            {
                _probeAt = EditorApplication.timeSinceStartup;
                Vector3 p = _entity.transform.position;
                NetProbe.Send(session, p.x, p.z, (float)_entity.State);
            }

            TickLevel0(session);
        }

        // ================================================================ Level 0

        static void TickLevel0(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: Role == SoloRole);
                    Next();
                    break;

                case 1:
                {
                    if (!Ready(session)) break;
                    if (Role == HostRole && CountInGame() < 2) break; // espera o cliente
                    if (LightsOut.Instance != null) LightsOut.Instance.firstDarkAt = 1e6f;
                    if (!BuildProbes(out string why)) { Finish(false, why); return; }

                    _entity = _director.SpawnAt(Probes[0].Entity, Probes[0].Facing);
                    if (_entity == null) { Finish(false, "o diretor não pôs a entidade"); return; }
                    _roam = _entity.roamSpeed; _search = _entity.searchSpeed;
                    _investigate = _entity.investigateSpeed; _chase = _entity.chaseSpeed;
                    SetSpeeds(0f);
                    _probe = 0;
                    _probeStep = 0;
                    _capturesBefore = _local.controller.Captures;
                    Next($"{Probes.Count} sondas do campo de visão");
                    break;
                }

                case 2:
                    // 1. Campo de visão, sonda por sonda.
                    if (!RunProbe()) break;
                    _probe++;
                    _probeStep = 0;
                    // A última (de frente, colada) pega: fica para o fim, depois do cansaço e da pressa.
                    if (_probe < Probes.Count - 1) break;
                    if (_local.controller.Captures != _capturesBefore) { Finish(false, "pegou o jogador nas sondas do campo de visão (fora do cone ou parada)"); return; }
                    SetSpeeds(1f);
                    Next();
                    break;

                case 3:
                {
                    // 2. Cansaço: o jogador longe, de costas para ela; ela o vê e caça.
                    if (!PickLine(14f, 24f, out _from, out _to)) { Finish(false, "não achei uma reta de 14–24 m com visada livre para o cansaço"); return; }
                    ToShelter();
                    if (_stageTicks < 3 || _entity.State == EntityState.Chase) break;
                    PlacePlayer(_to, _to - _from, away: true);
                    _entity = _director.SpawnAt(_from, _to - _from);
                    _resets = 0;
                    Next();
                    break;
                }

                case 4:
                {
                    PlacePlayerLook(_to, away: true, _from);
                    if (_entity.State != EntityState.Chase)
                    {
                        if (InStage > 3f) { Finish(false, $"o jogador a {Flat(_to - _from).magnitude:F1} m, de pé, à vista: ela não caçou ({_entity.State})"); return; }
                        break;
                    }
                    float into = Now - (float)ChaseSinceField.GetValue(_entity);
                    float speed = Speed();
                    _chaseMax = Mathf.Max(_chaseMax, speed);
                    if (into >= 1.5f && into <= 6.5f) _burst1 = Mathf.Max(_burst1, speed);
                    if (into >= 8f && into <= 11.5f)
                    {
                        _tiredMax = Mathf.Max(_tiredMax, speed);
                        _tiredMin = Mathf.Min(_tiredMin, speed);
                        _tiredSamples++;
                    }
                    if (into >= 12.8f) _burst2 = Mathf.Max(_burst2, speed);

                    // Chegou perto: volta para o começo da reta, sem sair da caça.
                    if (Flat(_entity.transform.position - _to).magnitude < 4.5f)
                    {
                        _director.SpawnAt(_from, _to - _from);
                        _resets++;
                    }

                    if (into < 14.5f) break;
                    bool ok = _burst1 >= 4.2f && _burst1 <= 4.8f && _tiredSamples >= 3 && _tiredMax <= 2.6f && _tiredMax >= 1.8f &&
                              _burst2 >= 4.2f && _chaseMax <= 4.8f && !_entity.Watched;
                    string note = $"cansaço: corrida até {_burst1:F1} m/s (2–6 s), cansada {_tiredMin:F1}–{_tiredMax:F1} m/s (8–11 s, {_tiredSamples} amostras), " +
                                  $"de novo até {_burst2:F1} m/s (13–14 s); o mais rápido na caça sem ninguém olhando {_chaseMax:F1} m/s ({_resets} recomeços)";
                    if (!ok) { Finish(false, note); return; }
                    Next(note);
                    break;
                }

                case 5:
                {
                    // 3. Pressa: de dentro da sala inicial, o jogador a vê indo atrás de um barulho.
                    ToShelter();
                    if (_stageTicks < 3 || _entity.State == EntityState.Chase) break;
                    if (!PickShelterLine(out _shelterEye, out _from, out _to)) { Finish(false, "não achei, da sala inicial, uma reta de 12 m ou mais à vista para conferir a pressa"); return; }
                    StartWalk();
                    Next();
                    break;
                }

                case 6:
                    // Olhada: ~3,4 m/s.
                    if (!Walk(watch: true)) break;
                    StartWalk();
                    Next();
                    break;

                case 7:
                {
                    // Ninguém olhando: ~5,1 m/s.
                    if (!Walk(watch: false)) break;
                    bool ok = _watchedSamples >= 3 && _watchedMax >= 2.9f && _watchedMax <= 3.7f &&
                              _unwatchedSamples >= 3 && _unwatchedMax >= 4.6f && _unwatchedMax <= 5.4f;
                    string note = $"pressa: indo atrás de barulho, olhada até {_watchedMax:F1} m/s ({_watchedSamples} amostras), " +
                                  $"sem ninguém olhando até {_unwatchedMax:F1} m/s ({_unwatchedSamples} amostras)";
                    if (!ok) { Finish(false, note); return; }
                    Next(note);
                    break;
                }

                case 8:
                {
                    // 4. De frente e colado: pega.
                    ToShelter();
                    if (_stageTicks < 3 || _entity.State == EntityState.Chase) break;
                    Probe front = Probes[Probes.Count - 1];
                    Vector3 facing = Flat(front.Facing).normalized;
                    _entity = _director.SpawnAt(front.Entity, facing);
                    PlacePlayer(front.Entity + facing * 1.0f, -facing, away: false);
                    _capturesBefore = _local.controller.Captures;
                    Next();
                    break;
                }

                case 9:
                    if (_local.controller.Captures > _capturesBefore)
                    {
                        Notes.Add($"de frente, a 1 m, pegou em {InStage:F1} s");
                        PartDone(true, string.Join("; ", Notes));
                        return;
                    }
                    if (InStage > 4f) Finish(false, $"de frente, a 1 m, não pegou em 4 s ({_entity.State}, a {Flat(_entity.transform.position - _local.transform.position).magnitude:F1} m)");
                    break;
            }
        }

        static bool Ready(GameSession session)
        {
            if (session.Phase != SessionPhase.Playing) return false;
            _local = PlayerNetwork.Local;
            _level = session.level;
            _director = EntityDirector.Instance;
            if (_local == null || _level == null || _level.Data == null) return false;
            if (InStage < 1.5f) return false;
            if (_director == null || _director.entityPrefab == null) { Finish(false, "a cena não tem o diretor da entidade"); return false; }
            _nav = new LevelNav(_level);
            return true;
        }

        static void SetSpeeds(float scale)
        {
            _entity.roamSpeed = _roam * scale;
            _entity.searchSpeed = _search * scale;
            _entity.investigateSpeed = _investigate * scale;
            _entity.chaseSpeed = _chase * scale;
        }

        static float Speed() => ((Vector3)VelocityField.GetValue(_entity)).magnitude;

        // Uma sonda: tira o jogador da vista (sala inicial), põe os dois no lugar e olha 2,5 s.
        static bool RunProbe()
        {
            Probe probe = Probes[_probe];
            switch (_probeStep)
            {
                case 0:
                    ToShelter();
                    _probeStep = 1;
                    _stageStart = Now;
                    return false;

                case 1:
                    ToShelter();
                    if (_entity.State == EntityState.Chase && InStage < 4f) return false;
                    _entity = _director.SpawnAt(probe.Entity, probe.Facing);
                    PlacePlayer(probe.Player, probe.Entity - probe.Player, away: false);
                    _probeSeen = false;
                    _probeStep = 2;
                    _stageStart = Now;
                    return false;

                default:
                    // Nos primeiros décimos ela ainda pode ter o alvo da sonda anterior: só conta depois.
                    if (InStage > 0.4f && _entity.State == EntityState.Chase) _probeSeen = true;
                    if (_probeSeen != probe.ExpectSeen && _probeSeen)
                    {
                        Finish(false, $"{probe.Label}: viu (devia não ver)");
                        return false;
                    }
                    if (InStage < 2.5f && !_probeSeen) return false;
                    if (_probeSeen != probe.ExpectSeen)
                    {
                        Finish(false, $"{probe.Label}: não viu em 2,5 s (devia ver; {_entity.State})");
                        return false;
                    }
                    Notes.Add($"{probe.Label}: {(probe.ExpectSeen ? "viu" : "não viu")}");
                    // O que ela viu fica na sala inicial antes da próxima.
                    ToShelter();
                    return true;
            }
        }

        static bool BuildProbes(out string why)
        {
            Probes.Clear();
            why = "";
            LevelData data = _level.Data;

            // Uma célula aberta (sem parede em pelo menos um lado), longe da sala inicial, para as
            // sondas coladas: o jogador fica dentro da mesma célula.
            Vector3 near = Vector3.zero, open = Vector3.forward;
            bool found = false;
            for (int z = 0; z < data.Height && !found; z++)
                for (int x = 0; x < data.Width && !found; x++)
                {
                    if (!_nav.Standable(x, z) || DistanceToRoom(x, z) < 3) continue;
                    Vector3 c = _nav.CellCenter(x, z);
                    if (!FreeFor(c) || !FreeFor(c + Vector3.forward) || !FreeFor(c + Vector3.back) || !FreeFor(c + Vector3.left) || !FreeFor(c + Vector3.right)) continue;
                    near = c;
                    found = true;
                }
            if (!found) { why = "não achei célula livre para as sondas coladas"; return false; }

            Probes.Add(new Probe { Label = "colado nas costas (0,9 m)", Entity = near, Facing = open, Player = near - open * 0.9f, ExpectSeen = false });
            Vector3 side = Quaternion.Euler(0f, 80f, 0f) * open;
            Probes.Add(new Probe { Label = "colado do lado (1 m a 80°)", Entity = near, Facing = open, Player = near + side * 1.0f, ExpectSeen = false });

            if (!PickLine(6f, 10f, out Vector3 e, out Vector3 p)) { why = "não achei par de células a 6–10 m com visada livre"; return false; }
            Vector3 to = Flat(p - e).normalized;
            Probes.Add(new Probe { Label = $"a {Flat(p - e).magnitude:F0} m, 75° da frente", Entity = e, Facing = Quaternion.Euler(0f, 75f, 0f) * to, Player = p, ExpectSeen = false });
            Probes.Add(new Probe { Label = $"a {Flat(p - e).magnitude:F0} m, 55° da frente", Entity = e, Facing = Quaternion.Euler(0f, 55f, 0f) * to, Player = p, ExpectSeen = true });

            float range = EntityBrain.Instance != null ? EntityBrain.Instance.sightRange : 26f;
            if (PickLine(range + 1.5f, range + 8f, out e, out p))
                Probes.Add(new Probe { Label = $"de frente a {Flat(p - e).magnitude:F1} m (além de {range:F0})", Entity = e, Facing = p - e, Player = p, ExpectSeen = false });
            else
                Notes.Add($"sem reta livre de mais de {range:F0} m nesta seed: o alcance máximo não foi conferido");
            if (PickLine(range - 8f, range - 3f, out e, out p))
                Probes.Add(new Probe { Label = $"de frente a {Flat(p - e).magnitude:F1} m", Entity = e, Facing = p - e, Player = p, ExpectSeen = true });

            // A última é a colada de frente, usada no fim (pega).
            Probes.Add(new Probe { Label = "de frente a 1 m", Entity = near, Facing = open, Player = near + open, ExpectSeen = true });
            return true;
        }

        static int DistanceToRoom(int x, int z)
        {
            LevelData d = _level.Data;
            int dx = x < d.RoomX0 ? d.RoomX0 - x : x >= d.RoomX0 + d.RoomWidth ? x - (d.RoomX0 + d.RoomWidth - 1) : 0;
            int dz = z < d.RoomZ0 ? d.RoomZ0 - z : z >= d.RoomZ0 + d.RoomHeight ? z - (d.RoomZ0 + d.RoomHeight - 1) : 0;
            return dx + dz;
        }

        // Dois pontos de piso (centros de célula fora da sala inicial) a uma distância na faixa, com a
        // visada do olho dela ao peito do jogador livre e chão andável em linha reta entre eles.
        static bool PickLine(float min, float max, out Vector3 from, out Vector3 to)
        {
            from = to = Vector3.zero;
            LevelData data = _level.Data;
            var cells = new List<Vector3>();
            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++)
                    if (_nav.Standable(x, z) && DistanceToRoom(x, z) >= 2) cells.Add(_nav.CellCenter(x, z));

            float eye = _entity != null ? _entity.eyeHeight : 2.9f;
            foreach (Vector3 a in cells)
                foreach (Vector3 b in cells)
                {
                    float d = Flat(b - a).magnitude;
                    if (d < min || d > max) continue;
                    if (Blocked(a + Vector3.up * eye, b + Vector3.up * 1.3f)) continue;
                    if (Blocked(a + Vector3.up * 1.0f, b + Vector3.up * 1.0f) || !_nav.FloorClear(a, b, 0.45f)) continue;
                    if (!FreeFor(b)) continue;
                    from = a;
                    to = b;
                    return true;
                }
            return false;
        }

        // Da sala inicial (o abrigo), uma reta para ela andar (12 m ou mais, fora da sala) com a
        // visada livre do olho do jogador a todos os pontos dela.
        static bool PickShelterLine(out Vector3 eye, out Vector3 from, out Vector3 to)
        {
            eye = from = to = Vector3.zero;
            LevelData data = _level.Data;
            var inside = new List<Vector3>();
            var outside = new List<Vector3>();
            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++)
                {
                    if (data.InRoom(x, z)) inside.Add(_nav.CellCenter(x, z));
                    else if (_nav.Standable(x, z)) outside.Add(_nav.CellCenter(x, z));
                }

            foreach (Vector3 room in inside)
            {
                if (!FreeFor(room)) continue;
                Vector3 look = room + Vector3.up * 1.65f;
                foreach (Vector3 a in outside)
                {
                    if (Blocked(look, a + Vector3.up * 1.5f)) continue;
                    foreach (Vector3 b in outside)
                    {
                        if (Flat(b - a).magnitude < 12f) continue;
                        if (!_nav.FloorClear(a, b, 0.45f) || Blocked(a + Vector3.up, b + Vector3.up)) continue;
                        bool seen = true;
                        for (int i = 1; i <= 4 && seen; i++)
                            if (Blocked(look, Vector3.Lerp(a, b, i / 4f) + Vector3.up * 1.5f)) seen = false;
                        if (!seen) continue;
                        eye = room;
                        from = a;
                        to = b;
                        return true;
                    }
                }
            }
            return false;
        }

        static void StartWalk()
        {
            _entity = _director.SpawnAt(_from, _to - _from);
            _entity.Alert(_to);
            _watchSince = Now;
            _watchWas = false;
        }

        // Ela anda de _from a _to indo atrás de um barulho; o jogador, na sala inicial, a olha (ou
        // olha para o outro lado). Amostra a velocidade com o "olhada" firme há meio segundo.
        static bool Walk(bool watch)
        {
            PlacePlayerLook(_shelterEye, !watch, _entity.transform.position + Vector3.up * 1.5f);

            if (_entity.State == EntityState.Chase) { Finish(false, "na conferência da pressa ela caçou o jogador da sala inicial (abrigo)"); return false; }
            if (_entity.Watched != _watchWas) { _watchWas = _entity.Watched; _watchSince = Now; }
            bool steady = Now - _watchSince > 0.5f && InStage > 0.8f && Flat(_entity.transform.position - _to).magnitude > 2f &&
                          _entity.State == EntityState.Investigate;
            if (steady)
            {
                float speed = Speed();
                if (_entity.Watched) { _watchedMax = Mathf.Max(_watchedMax, speed); _watchedSamples++; }
                else { _unwatchedMax = Mathf.Max(_unwatchedMax, speed); _unwatchedSamples++; }
            }
            return Flat(_entity.transform.position - _to).magnitude < 2f || InStage > 9f;
        }

        // ================================================================ Level 3: abrigo (os dados)

        // No Level 3 a entidade do Level 0 não mora mais (os inimigos novos têm testes próprios):
        // confere só a regra do abrigo, na planta, fora do Play. Em várias seeds: a sala inicial é
        // abrigo; toda célula de salinha acesa é; de salinha apagada, não; nenhuma outra célula é.
        static bool CheckShelterData(out string report)
        {
            EditorSceneManager.OpenScene(Level3Setup.ScenePath, OpenSceneMode.Single);
            LevelBuilder builder = Object.FindFirstObjectByType<LevelBuilder>();
            if (builder == null) { report = "a cena do Level 3 não tem LevelBuilder"; return false; }

            int lit = 0, dark = 0, seeds = 0;
            var problems = new List<string>();
            foreach (int seed in OfficeSeeds)
            {
                LevelData data = OfficeGenerator.Generate(seed, builder.width, builder.height);
                if (!data.HasOffice) { problems.Add($"seed {seed}: sem Escritório"); continue; }
                seeds++;
                for (int i = 0; i < data.OfficeRooms.Count; i++)
                {
                    OfficeRoom room = data.OfficeRooms[i];
                    if (room.Kind != OfficeCell.Small) continue;
                    if (room.Lit) lit++; else dark++;
                }

                for (int z = 0; z < data.Height; z++)
                    for (int x = 0; x < data.Width; x++)
                    {
                        int r = data.OfficeRoomOf[x, z];
                        bool small = r >= 0 && data.OfficeRooms[r].Kind == OfficeCell.Small;
                        bool expected = data.InRoom(x, z) || (small && data.OfficeRooms[r].Lit);
                        if (data.IsShelter(x, z) == expected) continue;
                        if (problems.Count < 6)
                            problems.Add($"seed {seed}, célula ({x},{z}): abrigo={!expected}, devia {expected} " +
                                         (small ? (data.OfficeRooms[r].Lit ? "(salinha acesa)" : "(salinha apagada)") : data.InRoom(x, z) ? "(sala inicial)" : $"({data.Office[x, z]})"));
                    }
            }

            bool ok = problems.Count == 0 && lit > 0 && dark > 0;
            report = $"abrigo na planta do Escritório em {seeds} seeds: {lit} salinhas acesas (abrigo), {dark} apagadas (não)";
            if (lit == 0 || dark == 0) report += "; faltou salinha " + (lit == 0 ? "acesa" : "apagada") + " nas seeds testadas";
            if (problems.Count > 0) report += "; " + string.Join("; ", problems);
            return ok;
        }

        // ================================================================ Cliente

        static void TickJoin(GameSession session)
        {
            NetProbe.Watch(session);
            switch (_stage)
            {
                case 0:
                    if (session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null)
                    {
                        _wasPlaying = true;
                        Next();
                        break;
                    }
                    if (session.Phase == SessionPhase.Menu && EditorApplication.timeSinceStartup - _lastTry > 4.0)
                    {
                        _lastTry = EditorApplication.timeSinceStartup;
                        session.Join("127.0.0.1");
                        NetProbe.Listen(session);
                        NetProbe.OnSample = s => { if (s.Values.Length >= 2) HostTrack.Add(new Vector3((float)s.HostSeconds, s.Values[0], s.Values[1])); };
                    }
                    break;

                case 1:
                {
                    // Fica na sala inicial olhando o chão: não conta como olhar para ela.
                    PlayerNetwork local = PlayerNetwork.Local;
                    if (local != null)
                    {
                        if (_stageTicks++ == 0) local.controller.Teleport(session.level.SpawnPosition(1), 0f);
                        local.controller.SetPitch(85f);
                    }

                    EntityBrain entity = EntityBrain.Instance;
                    if (entity != null && entity.IsSpawned && session.SessionSeconds >= 0.0)
                    {
                        Vector3 p = entity.transform.position;
                        if (!_clientSawEntity) { _clientSawEntity = true; _clientFirst = p; }
                        _clientFarthest = Mathf.Max(_clientFarthest, Flat(p - _clientFirst).magnitude);
                        if (entity.State == EntityState.Chase) _clientSawChase = true;
                        ClientTrack.Add(new Vector4((float)session.SessionSeconds, p.x, p.z, (float)entity.State));
                    }

                    // O host terminou e fechou a sessão: compara.
                    if (session.Phase != SessionPhase.Playing) EvaluateClient();
                    break;
                }
            }
        }

        static void EvaluateClient()
        {
            int compared = 0, good = 0;
            float worst = 0f;
            foreach (Vector3 h in HostTrack)
            {
                float best = float.MaxValue;
                foreach (Vector4 c in ClientTrack)
                {
                    if (c.x < h.x - 0.2f || c.x > h.x + 1.2f) continue;
                    best = Mathf.Min(best, new Vector2(c.y - h.y, c.z - h.z).magnitude);
                }
                if (best == float.MaxValue) continue;
                compared++;
                if (best < 1f) good++;
                else worst = Mathf.Max(worst, best);
            }
            bool clock = NetProbe.ClockOk(out string clockReport);
            bool ok = clock && _clientSawEntity && _clientFarthest >= 5f && _clientSawChase && compared >= 20 && good >= compared * 0.85f;
            Finish(ok, $"posição da entidade no cliente igual à do host no mesmo instante em {good} de {compared} amostras (pior fora: {worst:F1} m); {clockReport}" + ClientReport());
        }

        static string ClientReport() =>
            $"; no cliente: entidade vista={_clientSawEntity}, andou até {_clientFarthest:F1} m do primeiro lugar, viu caçar={_clientSawChase}, {ClientTrack.Count} posições, {HostTrack.Count} do host";

        // ================================================================ Ajudantes

        static int CountInGame()
        {
            int count = 0;
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.InGame) count++;
            return count;
        }

        // O jogador vai para a sala inicial (abrigo: ela não o percebe nem o pega).
        static void ToShelter()
        {
            Vector3 home = _level.SpawnPosition(0);
            if (Flat(_local.transform.position - home).magnitude > 0.5f) _local.controller.Teleport(home, 0f);
        }

        static void PlacePlayer(Vector3 at, Vector3 facing, bool away)
        {
            Vector3 f = Flat(away ? -facing : facing);
            float yaw = f.sqrMagnitude > 0.001f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            _local.controller.Teleport(new Vector3(at.x, at.y + 0.05f, at.z), yaw);
            _local.controller.SetPitch(0f);
        }

        // Mantém o jogador em <paramref name="at"/> olhando para <paramref name="target"/> (ou para o outro lado).
        static void PlacePlayerLook(Vector3 at, bool away, Vector3 target)
        {
            if (Flat(_local.transform.position - at).magnitude > 0.6f) _local.controller.Teleport(new Vector3(at.x, at.y + 0.05f, at.z), 0f);
            Transform eye = _local.playerCamera.transform;
            Vector3 to = target - eye.position;
            Vector3 flat = Flat(to);
            if (flat.sqrMagnitude < 0.0001f) return;
            if (away) flat = -flat;
            _local.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 0f);
            _local.controller.SetPitch(away ? 0f : -Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
        }

        // Cabe um jogador de pé ali (nada do cenário no lugar)?
        static bool FreeFor(Vector3 at)
        {
            foreach (Collider c in Physics.OverlapCapsule(at + Vector3.up * 0.4f, at + Vector3.up * 1.6f, 0.35f, ~0, QueryTriggerInteraction.Ignore))
                if (c.transform.IsChildOf(_level.transform)) return false;
            return true;
        }

        static bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return false;
            foreach (RaycastHit hit in Physics.RaycastAll(from, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.IsChildOf(_level.transform)) return true;
            return false;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // ================================================================ Fim

        // Uma parte passou. Sozinho com as duas partes: guarda o relato da primeira e abre a segunda.
        static void PartDone(bool ok, string report)
        {
            if (SessionState.GetBool(BothKey, false))
            {
                _finished = true;
                Debug.Log("[BackRooms] Regras da entidade, Level 0 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
                SessionState.SetString(FirstKey, report);
                SessionState.SetBool(FirstOkKey, ok);
                Cleanup();
                SessionState.SetInt(ExitCodeKey, 100); // 100 = conferir o abrigo do Level 3, depois sair
                EditorApplication.playModeStateChanged -= AfterPlay;
                EditorApplication.playModeStateChanged += AfterPlay;
                EditorApplication.ExitPlaymode();
                return;
            }
            Finish(ok, report);
        }

        static void Cleanup()
        {
            EntityDirector.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
        }

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;
            if (_entity != null && _chase > 0f) SetSpeeds(1f);
            Cleanup();

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }
            if (!ok && Notes.Count > 0) report += " | antes disso: " + string.Join("; ", Notes);

            string part = Role == JoinRole ? "cliente" : "Level 0" + (Role == HostRole ? ", host" : "");

            Debug.Log("[BackRooms] Teste das regras da entidade " + (ok ? "PASSOU" : "FALHOU") + $" ({part}): " + report);

            if (SessionState.GetBool(BatchKey, false))
            {
                SessionState.SetInt(ExitCodeKey, ok ? 0 : 3);
                EditorApplication.playModeStateChanged -= AfterPlay;
                EditorApplication.playModeStateChanged += AfterPlay;
            }
            EditorApplication.ExitPlaymode();
        }

        static void AfterPlay(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= AfterPlay;
            int code = SessionState.GetInt(ExitCodeKey, 1);
            SessionState.EraseInt(ExitCodeKey);

            // Fora do Play: o abrigo do Level 3, na planta.
            if (code == 100)
            {
                bool ok = CheckShelterData(out string shelter);
                Debug.Log("[BackRooms] Regras da entidade, abrigo do Level 3 " + (ok ? "PASSOU" : "FALHOU") + ": " + shelter);
                string first = SessionState.GetString(FirstKey, "");
                ok &= SessionState.GetBool(FirstOkKey, false);
                Debug.Log("[BackRooms] Teste das regras da entidade " + (ok ? "PASSOU" : "FALHOU") + " (Level 0 e abrigo do Level 3): Level 0: " + first + " | abrigo: " + shelter);
                SessionState.EraseString(FirstKey);
                if (SessionState.GetBool(BatchKey, false)) Level1Setup.ExitWithLevel0Open(ok ? 0 : 3);
                return;
            }
            if (SessionState.GetBool(BatchKey, false)) Level1Setup.ExitWithLevel0Open(code);
        }
    }
}
