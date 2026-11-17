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
    /// Teste em Play do abrigo e do perigo do Level 1 (level1-mostruario.md, seções 4 e 5.4):
    ///
    /// 1. guarda-roupa: mirando nele e apertando E, o jogador entra, fica escondido e parado; o
    ///    Capitão, chamado para ali, não o pega, e de dentro ele não congela o Capitão; com E de
    ///    novo, sai pela frente;
    /// 2. mesa: em pé, o aviso pede para agachar; debaixo dela, com o Capitão olhando de perto,
    ///    ele vê e pega; com o Capitão longe (além de 6 m) e olhando, não vê;
    /// 3. plástico-bolha: andando devagar no chão comum, o Capitão (a 6 células) não ouve; em
    ///    cima da folha, ouve, e a folha estala;
    /// 4. barricada: andando contra ela, o jogador não entra na célula;
    /// 5. totem: num apagão forçado, com o jogador longe e sem ver, o Capitão troca de lugar com
    ///    um totem (o totem vai para onde ele estava).
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level1ShelterTest.BatchRun (sai com
    /// 0 se passou, 3 se falhou). Menu: BackRooms > Testar esconderijos do Level 1. Linha
    /// "[BackRooms] Teste de abrigo do Level 1". Dois editores: <see cref="BatchHost"/> (o mesmo
    /// teste, esperando o cliente) + <see cref="BatchJoin"/> (na cópia: confere que vê o host
    /// sumir no guarda-roupa e o totem trocado de lugar).
    /// </summary>
    public static class Level1ShelterTest
    {
        const string ActiveKey = "BackRooms.Level1ShelterTest.Active";
        const string BatchKey = "BackRooms.Level1ShelterTest.Batch";
        const string ExitCodeKey = "BackRooms.Level1ShelterTest.ExitCode";
        const string RoleKey = "BackRooms.Level1ShelterTest.Role";
        const string SoloRole = "solo", HostRole = "host", JoinRole = "join";
        const double Timeout = 200.0;
        const double NetTimeout = 320.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks;
        static double _began, _lastTry;
        static float _stageStart;
        static bool _finished;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static PropLayer _props;
        static EntityDirector _director;
        static CaptainBrain _captain;
        static HideSpot _wardrobe, _table;
        static Vector3 _mark, _wrapCenter, _captainSpot;
        static int _captures, _heard, _pops;
        static Vector3[] _totemsBefore;
        // Cliente.
        static bool _sawHostHidden, _sawTotemMoved;
        static Vector3[] _totemStart;

        [MenuItem("BackRooms/Testar esconderijos do Level 1")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false, SoloRole);
        }

        public static void BatchRun() { Run(true, SoloRole); }
        public static void BatchHost() { Run(true, HostRole); }
        public static void BatchJoin() { Run(true, JoinRole); }

        static void Run(bool batch, string role)
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
            PlayerController body = _local != null ? _local.controller : null;

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
                    if (Role == HostRole && CountInGame() < 2) break;
                    _local = PlayerNetwork.Local;
                    _level = session.level;
                    _director = EntityDirector.Instance;
                    if (_local == null || _level == null || _level.Data == null || _director == null) break;
                    if (InStage < 1.5f) break;

                    _props = _level.GetComponent<PropLayer>();
                    if (LightsOut.Instance != null) LightsOut.Instance.firstDarkAt = 1e6f;
                    foreach (HideSpot spot in HideSpot.All)
                    {
                        if (spot.kind == HideKind.Wardrobe && _wardrobe == null) _wardrobe = spot;
                        if (spot.kind == HideKind.Table && _table == null) _table = spot;
                    }
                    if (_wardrobe == null) { Finish(false, "nenhum guarda-roupa de esconder na seed 1998"); return; }
                    if (_table == null) { Finish(false, "nenhuma mesa de esconder na seed 1998"); return; }

                    // De frente para o guarda-roupa, no ponto de saída dele. (_local acabou de ser lido
                    // neste quadro: "body", do começo do Tick, ainda pode estar vazio.)
                    _local.controller.Teleport(_wardrobe.ExitWorld, YawTo(_wardrobe.ExitWorld, _wardrobe.InsideWorld));
                    Next($"{_props.Totems} totens, {_props.Barricades} barricadas, {_props.BubbleSheets} plásticos-bolha, {_props.HideSpots} esconderijos");
                    break;
                }

                case 2:
                    // 1. Guarda-roupa: E entra.
                    if (_stageTicks < 3) break;
                    if (HideSpotInput.Prompt != "E - HIDE")
                    {
                        if (InStage > 2f) Finish(false, $"de frente para o guarda-roupa, o aviso foi \"{HideSpotInput.Prompt}\" e não E - HIDE");
                        break;
                    }
                    HideSpotInput.PressForTest = true;
                    Next();
                    break;

                case 3:
                    if (body.Hiding != HideKind.Wardrobe)
                    {
                        if (InStage > 1f) Finish(false, "apertou E diante do guarda-roupa e não entrou");
                        break;
                    }
                    if (Flat(body.transform.position - _wardrobe.InsideWorld).magnitude > 0.1f) { Finish(false, "entrou, mas o corpo não foi para dentro do móvel"); return; }

                    // O Capitão aparece de frente, a 4 m, chamado para ali.
                    _captures = body.Captures;
                    Vector3 front = _wardrobe.ExitWorld + (_wardrobe.ExitWorld - _wardrobe.InsideWorld).normalized * 2.5f;
                    _captain = _director.SpawnAt(front, _wardrobe.InsideWorld - front) as CaptainBrain;
                    if (_captain == null) { Finish(false, "o diretor não pôs o Capitão"); return; }
                    _captain.Alert(_wardrobe.InsideWorld);
                    Next("E diante do guarda-roupa: entrou e ficou escondido");
                    break;

                case 4:
                    if (_captain.Frozen) { Finish(false, "o jogador dentro do guarda-roupa congelou o Capitão (de dentro não se vigia)"); return; }
                    if (body.Captures > _captures) { Finish(false, "o Capitão pegou o jogador dentro do guarda-roupa"); return; }
                    if (Flat(body.transform.position - _wardrobe.InsideWorld).magnitude > 0.05f) { Finish(false, "dentro do guarda-roupa o corpo se mexeu"); return; }
                    if (InStage < 5f) break;
                    HideSpotInput.PressForTest = true;
                    Next($"o Capitão, chamado para o guarda-roupa, ficou {InStage:F0} s ali e não pegou nem congelou");
                    break;

                case 5:
                    if (body.Hiding != HideKind.None)
                    {
                        if (InStage > 1f) Finish(false, "apertou E dentro do guarda-roupa e não saiu");
                        break;
                    }
                    if (Flat(body.transform.position - _wardrobe.ExitWorld).magnitude > 0.2f) { Finish(false, "saiu do guarda-roupa, mas não pela frente"); return; }

                    // O Capitão some do caminho; o jogador vai para a mesa, em pé.
                    _director.SpawnAt(FarCell(_table.InsideWorld, 9), Vector3.forward);
                    body.Teleport(_table.ExitWorld, YawTo(_table.ExitWorld, _table.InsideWorld));
                    // Olhando para o tampo (a 0,95 m da saída, abaixo do olho).
                    body.SetPitch(Mathf.Atan2(1.65f - 0.5f, Flat(_table.InsideWorld - _table.ExitWorld).magnitude) * Mathf.Rad2Deg);
                    Next("E dentro do guarda-roupa: saiu pela frente");
                    break;

                case 6:
                    // 2. Mesa: em pé, pede para agachar.
                    if (_stageTicks < 3) break;
                    if (HideSpotInput.Prompt != "CROUCH TO HIDE")
                    {
                        if (InStage > 2f) Finish(false, $"em pé diante da mesa, o aviso foi \"{HideSpotInput.Prompt}\" e não CROUCH TO HIDE");
                        break;
                    }
                    // Agachar pelo teclado não dá aqui: entra direto, como faria o E agachado.
                    body.EnterFurniture(HideKind.Table, _table.InsideWorld, _table.transform.eulerAngles.y, _table.eye, _table.ExitWorld);
                    // O Capitão longe (9 m), de frente para a mesa: além do alcance, não vê.
                    _captainSpot = _table.InsideWorld + (_table.ExitWorld - _table.InsideWorld).normalized * 9f;
                    _director.SpawnAt(_captainSpot, _table.InsideWorld - _captainSpot);
                    _captures = body.Captures;
                    Next("em pé diante da mesa, pede para agachar");
                    break;

                case 7:
                    // Parado a 9 m: não ouve (o jogador não se mexe) e não vê (passa dos 6 m). O
                    // jogador fica de costas para ele: olhando, com a luz acesa, o congelaria.
                    _director.SpawnAt(_captainSpot, _table.InsideWorld - _captainSpot);
                    FaceAway(body, _captainSpot);
                    if (_captain.State == EntityState.Chase) { Finish(false, "debaixo da mesa, o Capitão a 9 m o viu"); return; }
                    if (InStage < 2f) break;
                    // Agora perto (3 m), de frente: vê e pega.
                    _captainSpot = _table.InsideWorld + (_table.ExitWorld - _table.InsideWorld).normalized * 3f;
                    _director.SpawnAt(_captainSpot, _table.InsideWorld - _captainSpot);
                    Next("debaixo da mesa, o Capitão a 9 m não viu");
                    break;

                case 8:
                    if (!body.IsReturning) FaceAway(body, _captain.transform.position);
                    if (body.Captures > _captures)
                    {
                        Next($"debaixo da mesa, o Capitão a 3 m, olhando, viu e pegou em {InStage:F1} s");
                        break;
                    }
                    if (InStage > 8f) Finish(false, $"debaixo da mesa, o Capitão a 3 m e olhando não pegou em 8 s (estado {_captain.State})");
                    break;

                case 9:
                {
                    // 3. Plástico-bolha. Espera voltar da captura e acabar a trégua de quem acabou de
                    // ser pego (feedSeconds + mercySeconds: ele ignora essa pessoa nesse tempo).
                    if (body.IsReturning || InStage < _captain.feedSeconds + _captain.mercySeconds + 0.5f) break;
                    Transform wrap = _level.transform.Find("Moveis/PlasticoBolha");
                    if (wrap == null) { Finish(false, "sem plástico-bolha na fase"); return; }
                    _wrapCenter = wrap.position;
                    Vector3 cell = CellCenter(_wrapCenter);
                    Vector3 captainAt = FarCell(_wrapCenter, 6, exact: true);
                    _director.SpawnAt(captainAt, Vector3.forward);
                    _captain.Alert(captainAt);
                    // Começa fora da folha, no meio da célula vizinha mais perto.
                    body.Teleport(OffSheet(_wrapCenter), 0f);
                    _heard = _captain.NoisesHeard;
                    _pops = BubbleWrap.Pops;
                    _mark = body.transform.position;
                    Next();
                    break;
                }

                case 10:
                    // Devagar (1 m/s) no chão comum, para lá e para cá: a 6 células ele não ouve.
                    Pace(body, _mark, 1.0f);
                    if (_captain.NoisesHeard > _heard) { Finish(false, $"andando devagar no chão comum, o Capitão a 6 células ouviu"); return; }
                    if (InStage < 2.5f) break;
                    body.Teleport(new Vector3(_wrapCenter.x, _mark.y, _wrapCenter.z) + Vector3.right * 0.3f, 0f);
                    _mark = body.transform.position;
                    Next("andando devagar no chão comum, o Capitão a 6 células não ouviu");
                    break;

                case 11:
                    // Na folha, mesma velocidade: estala e ele ouve.
                    Pace(body, _mark, 1.0f, 0.4f);
                    if (_captain.NoisesHeard > _heard && BubbleWrap.Pops > _pops)
                    {
                        Next($"no plástico-bolha, na mesma velocidade: estalou {BubbleWrap.Pops - _pops} vez(es) e o Capitão ouviu");
                        break;
                    }
                    if (InStage > 4f) Finish(false, $"no plástico-bolha: estalos {BubbleWrap.Pops - _pops}, o Capitão ouviu {_captain.NoisesHeard - _heard} vez(es)");
                    break;

                case 12:
                {
                    // 4. Barricada: anda contra ela e não entra.
                    if (!FindBarricade(out Vector3 inside, out Vector3 from)) { Finish(false, "sem barricada com vizinha aberta"); return; }
                    body.Teleport(from, YawTo(from, inside));
                    CharacterController controller = body.GetComponent<CharacterController>();
                    for (int i = 0; i < 40; i++) controller.Move((inside - body.transform.position).normalized * 0.15f);
                    Vector3 local = _level.transform.InverseTransformPoint(body.transform.position);
                    int x = Mathf.FloorToInt(local.x / LevelBuilder.Cell), z = Mathf.FloorToInt(local.z / LevelBuilder.Cell);
                    if (_level.Data.IsBarricade(x, z)) { Finish(false, "andando contra a barricada, o jogador entrou nela"); return; }
                    Next($"andou contra a barricada e parou a {Flat(inside - body.transform.position).magnitude:F1} m do meio dela");
                    break;
                }

                case 13:
                {
                    // 5. Totem: jogador na sala inicial (longe de tudo, sem ver), apagão, troca.
                    body.Teleport(_level.SpawnCenter, 0f);
                    _local.flashlight.SetOn(false);
                    Vector3 from = FarCell(_level.SpawnCenter, 7);
                    _director.SpawnAt(from, Vector3.forward);
                    _captain.ForceNextSwap = true;
                    _totemsBefore = new Vector3[Totem.All.Count];
                    for (int i = 0; i < _totemsBefore.Length; i++) _totemsBefore[i] = Totem.All[i].transform.position;
                    LightsOut power = LightsOut.Instance;
                    power.darkSeconds = 600f;
                    power.firstDarkAt = (float)session.SessionSeconds - 0.05f;
                    Next();
                    break;
                }

                case 14:
                    if (_captain.Swaps > 0)
                    {
                        // O totem que saiu do lugar foi para o meio de uma célula, e o Capitão está
                        // onde o totem estava.
                        int moved = -1;
                        for (int i = 0; i < _totemsBefore.Length; i++)
                            if (Flat(Totem.All[i].transform.position - _totemsBefore[i]).magnitude > 0.3f) moved = i;
                        if (moved < 0) { Finish(false, "o Capitão trocou, mas nenhum totem saiu do lugar"); return; }
                        Vector3 now = Totem.All[moved].transform.position;
                        if (Flat(now - CellCenter(now)).magnitude > 0.2f) { Finish(false, "o totem trocado não foi para o meio de uma célula"); return; }
                        float off = Flat(_captain.transform.position - _totemsBefore[moved]).magnitude;
                        if (off > 0.5f) { Finish(false, $"o Capitão trocou, mas está a {off:F1} m de onde o totem estava"); return; }
                        Next($"no apagão trocou de lugar com o totem {moved} em {InStage:F1} s (o totem foi para onde ele estava, ele para o canto do totem)");
                        LightsOut.Instance.firstDarkAt = 1e6f;
                        break;
                    }
                    if (InStage > 6f) Finish(false, $"no apagão forçado o Capitão não trocou com nenhum totem em 6 s ({Totem.All.Count} totens)");
                    break;

                case 15:
                    // Dois editores: dá tempo de o cliente ver a troca antes de fechar.
                    if (Role == HostRole && InStage < 4f) break;
                    Finish(true, string.Join("; ", Notes));
                    break;
            }
        }

        // ---------------------------------------------------------------- Cliente

        static void TickJoin(GameSession session)
        {
            switch (_stage)
            {
                case 0:
                    if (session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null)
                    {
                        _totemStart = new Vector3[Totem.All.Count];
                        for (int i = 0; i < _totemStart.Length; i++) _totemStart[i] = Totem.All[i].transform.position;
                        Next($"o cliente montou {Totem.All.Count} totens e {HideSpot.All.Count} esconderijos");
                        break;
                    }
                    if (session.Phase == SessionPhase.Menu && EditorApplication.timeSinceStartup - _lastTry > 4.0)
                    {
                        _lastTry = EditorApplication.timeSinceStartup;
                        session.Join("127.0.0.1");
                    }
                    break;

                case 1:
                    foreach (PlayerNetwork player in PlayerNetwork.All)
                    {
                        if (player == null || player.IsOwner || !player.IsSpawned) continue;
                        if (player.Hiding == HideKind.Wardrobe && player.body != null && !player.body.activeSelf) _sawHostHidden = true;
                    }
                    for (int i = 0; i < _totemStart.Length && i < Totem.All.Count; i++)
                        if (Totem.All[i] != null && Flat(Totem.All[i].transform.position - _totemStart[i]).magnitude > 0.5f) _sawTotemMoved = true;

                    if (_sawHostHidden && _sawTotemMoved)
                    {
                        Finish(true, string.Join("; ", Notes) + ClientReport());
                        return;
                    }
                    if (session.Phase != SessionPhase.Playing) Finish(false, "a sessão acabou antes" + ClientReport());
                    break;
            }
        }

        static string ClientReport() =>
            $"; no cliente: viu o host sumir no guarda-roupa={_sawHostHidden}, viu um totem trocar de lugar={_sawTotemMoved}";

        // ---------------------------------------------------------------- Ajuda

        static int CountInGame()
        {
            int count = 0;
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.InGame) count++;
            return count;
        }

        // Anda para lá e para cá em torno de um ponto, na velocidade dada (m/s), num vaivém de
        // <paramref name="span"/> metros ao longo de X.
        static void Pace(PlayerController body, Vector3 around, float speed, float span = 1.2f)
        {
            float phase = Mathf.PingPong(InStage * speed, span) - span * 0.5f;
            Vector3 goal = around + Vector3.right * phase;
            Vector3 delta = goal - body.transform.position;
            delta.y = 0f;
            body.GetComponent<CharacterController>().Move(delta);
        }

        static Vector3 CellCenter(Vector3 world)
        {
            Vector3 local = _level.transform.InverseTransformPoint(world);
            return _level.CellCenter(Mathf.FloorToInt(local.x / LevelBuilder.Cell), Mathf.FloorToInt(local.z / LevelBuilder.Cell));
        }

        // Uma célula onde o inimigo pode ficar, a "cells" passos de caminhada do ponto (ou o mais
        // perto disso; com exact, só exatamente).
        static Vector3 FarCell(Vector3 from, int cells, bool exact = false)
        {
            var nav = new LevelNav(_level);
            int[,] reach = nav.NewMap();
            nav.WorldToCell(from, out int fx, out int fz);
            nav.Distances(fx, fz, reach);
            Vector3 best = from;
            int bestGap = int.MaxValue;
            for (int z = 0; z < _level.Data.Height; z++)
                for (int x = 0; x < _level.Data.Width; x++)
                {
                    if (reach[x, z] < 0 || !nav.Standable(x, z)) continue;
                    int gap = Mathf.Abs(reach[x, z] - cells);
                    if (exact && gap != 0) continue;
                    if (gap < bestGap) { bestGap = gap; best = nav.CellCenter(x, z); }
                }
            return best;
        }

        // Um ponto no chão comum perto da folha (1,2 m ao lado dela, dentro da mesma célula).
        static Vector3 OffSheet(Vector3 sheet)
        {
            Vector3 center = CellCenter(sheet);
            foreach (Vector3 dir in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                Vector3 at = center + dir * 1.3f;
                if (!BubbleWrap.Under(at) && !BubbleWrap.Under(at + Vector3.right * 0.7f) && !BubbleWrap.Under(at - Vector3.right * 0.7f))
                    return new Vector3(at.x, sheet.y, at.z);
            }
            return center + Vector3.right * 1.3f;
        }

        static bool FindBarricade(out Vector3 inside, out Vector3 from)
        {
            inside = from = Vector3.zero;
            LevelData d = _level.Data;
            WallSide[] sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (!d.IsBarricade(x, z)) continue;
                    foreach (WallSide side in sides)
                    {
                        if (!d.CanWalk(x, z, side, false) || !d.Neighbor(x, z, side, out int nx, out int nz) || d.IsBarricade(nx, nz)) continue;
                        inside = _level.CellCenter(x, z);
                        from = _level.CellCenter(nx, nz);
                        return true;
                    }
                }
            return false;
        }

        static void FaceAway(PlayerController body, Vector3 from)
        {
            body.transform.rotation = Quaternion.Euler(0f, YawTo(from, body.transform.position), 0f);
        }

        static float YawTo(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
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
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log("[BackRooms] Teste de abrigo do Level 1 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

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
