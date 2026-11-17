using System.Collections.Generic;
using BackRooms.Audio;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Teste em Play da troca de fase pelo menu (<see cref="GameSession.SwitchLevel"/>) e do
    /// progresso (<see cref="LevelProgress"/>).
    ///
    /// Sozinho (<see cref="BatchRun"/>): sem progresso e fora do modo desenvolvedor o Level 1
    /// fica bloqueado; no modo desenvolvedor troca para ele, hospeda e abre a porta, que zera
    /// e leva ao Level 2 já hospedando; nada num poço e abre a porta das Piscinas (a última
    /// fase), que zera e acaba no menu; volta ao Level 0, confere que nada da água sobrou e
    /// passa pela porta dele até o Level 1, onde sai da partida para o menu. Em cada fase confere
    /// que só existe uma sessão (um NetworkManager), a música do menu e o zumbido das lâmpadas
    /// (calado no menu, no volume de jogo jogando e calado de novo ao sair). O progresso salvo
    /// nesta máquina é restaurado no fim.
    ///
    /// Dois editores (o segundo numa cópia do projeto): <see cref="BatchHostLevel1"/> troca para
    /// o Level 1 e hospeda; <see cref="BatchJoinFromLevel0"/> entra a partir do Level 0 e tem de
    /// trocar sozinho para o Level 1 e reconectar. Os dois escrevem seed e mobília no log.
    ///
    /// Porta de saída com dois editores: <see cref="BatchHostDoor"/> hospeda o Level 0 e
    /// <see cref="BatchJoinDoor"/> entra. O cliente abre a porta primeiro e tem de ficar
    /// esperando; quando o host abre também, os dois passam para o Level 1 e o cliente
    /// reconecta sozinho na sessão nova.
    ///
    /// Linha de comando: sai com 0 se passou, 3 se falhou; linha "[BackRooms] Teste do seletor de
    /// fase" do log. O editor fecha com o Level 0 aberto.
    /// </summary>
    public static class LevelSelectTest
    {
        const string ActiveKey = "BackRooms.LevelSelectTest.Active";
        const string RoleKey = "BackRooms.LevelSelectTest.Role";
        const string SavedKey = "BackRooms.LevelSelectTest.SavedProgress";
        const string Level0Scene = "Assets/_Game/Scenes/Level0_Test.unity";

        const string Solo = "solo";
        const string HostRole = "host1";
        const string JoinRole = "join0";
        const string HostDoorRole = "hostporta";
        const string JoinDoorRole = "joinporta";

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static string _role;
        static int _stage;
        static double _began, _stageStart, _lastTry;
        static GameSession _previous;
        static bool _switched;
        static float _musicPlayed;
        static bool _humNoted;

        [MenuItem("BackRooms/Testar seletor de fase")]
        public static void RunFromMenu() { Run(Solo); }

        public static void BatchRun() { Run(Solo); }
        public static void BatchHostLevel1() { Run(HostRole); }
        public static void BatchJoinFromLevel0() { Run(JoinRole); }
        public static void BatchHostDoor() { Run(HostDoorRole); }
        public static void BatchJoinDoor() { Run(JoinDoorRole); }

        static void Run(string role)
        {
            EditorSceneManager.OpenScene(Level0Scene, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(RoleKey, role);
            EditorApplication.EnterPlaymode();
        }

        // Entrar em Play recarrega o domínio; o estado sobrevive pelo SessionState.
        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            _role = SessionState.GetString(RoleKey, Solo);
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (stackTrace != null && (stackTrace.Contains("UnityEditor.Search.") || stackTrace.Contains("Unity.AI."))) return;
            Errors.Add(message);
        }

        static double Now => EditorApplication.timeSinceStartup;

        static void Next()
        {
            _stage++;
            _stageStart = Now;
        }

        static bool StageTimedOut(double seconds) => Now - _stageStart > seconds;

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (_began == 0) { _began = Now; _stageStart = Now; }

            if (Now - _began > 240) { Finish(false, $"passou de 4 min na etapa {_stage}"); return; }

            switch (_role)
            {
                case HostRole: TickHost(); break;
                case JoinRole: TickJoin(); break;
                case HostDoorRole: TickHostDoor(); break;
                case JoinDoorRole: TickJoinDoor(); break;
                default: TickSolo(); break;
            }
        }

        // ---------------------------------------------------------------- Sozinho

        static void TickSolo()
        {
            GameSession session = GameSession.Instance;

            switch (_stage)
            {
                case 0:
                    if (!InMenu(session, 0)) { if (StageTimedOut(20)) Finish(false, "o Level 0 não abriu no menu"); return; }
                    if (!HumMuted(out string hum0)) { Finish(false, "zumbido das lâmpadas no menu do Level 0: " + hum0); return; }
                    Notes.Add("no menu do Level 0 o zumbido fica calado");

                    // Guarda o progresso desta máquina e começa do zero.
                    SessionState.SetInt(SavedKey, PlayerPrefs.GetInt(LevelProgress.ClearedKey, -1));
                    PlayerPrefs.SetInt(LevelProgress.ClearedKey, 0);

                    LevelProgress.DevModeOverride = false;
                    bool locked = !LevelProgress.IsUnlocked(1) && !session.SwitchLevel(1);
                    LevelProgress.DevModeOverride = null;
                    if (!locked) { Finish(false, "sem progresso e fora do modo desenvolvedor o Level 1 não ficou bloqueado"); return; }
                    Notes.Add("Level 1 bloqueado para jogador sem progresso");

                    if (!session.SwitchLevel(1)) { Finish(false, "no modo desenvolvedor o Level 1 não abriu"); return; }
                    _previous = session;
                    Next();
                    break;

                case 1:
                    if (!Switched(session, 1)) { if (StageTimedOut(30)) Finish(false, "a troca para o Level 1 não terminou"); return; }
                    if (!OneSession(out string sessions)) { Finish(false, "duas sessões depois de trocar para o Level 1: " + sessions); return; }
                    Notes.Add("trocou para o Level 1 com uma sessão só");
                    if (!MusicOk(true, out string music1)) { Finish(false, "música do menu depois de trocar para o Level 1: " + music1); return; }
                    _musicPlayed = MenuMusic.Instance.Played;
                    Notes.Add("a música do menu seguiu na troca de fase, sem recomeçar nem duplicar");
                    if (!HumMuted(out string hum1)) { Finish(false, "zumbido das lâmpadas no menu do Level 1: " + hum1); return; }
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 2:
                    if (!Playing(session)) { if (StageTimedOut(40)) Finish(false, "não começou a partida no Level 1"); return; }
                    var props = Object.FindFirstObjectByType<PropLayer>();
                    if (props == null || props.Count == 0) { Finish(false, "o Level 1 começou sem móveis"); return; }
                    Notes.Add($"Level 1 em jogo com {props.Count} móveis");

                    if (!FaceExit()) { Finish(false, "o Level 1 não tem saída"); return; }
                    Next();
                    break;

                case 3:
                {
                    // O zumbido entra em fade: espera chegar ao volume de jogo antes da porta.
                    if (!HumFull(out string humFull1))
                    {
                        if (StageTimedOut(10)) Finish(false, "o zumbido não chegou ao volume de jogo no Level 1: " + humFull1);
                        return;
                    }
                    if (!_humNoted)
                    {
                        _humNoted = true;
                        Notes.Add("jogando o Level 1 o zumbido entrou em fade até o volume de jogo");
                    }

                    // Só chegar perto não zera mais: é preciso abrir a porta (tecla E).
                    LevelExit door = LevelExit.Instance;
                    if (door == null || !door.LocalInReach)
                    {
                        if (StageTimedOut(10)) Finish(false, "de frente para a porta do Level 1, ela não ficou ao alcance");
                        return;
                    }
                    if (LevelProgress.IsCleared(1)) { Finish(false, "o Level 1 foi zerado só de chegar perto da porta"); return; }
                    _previous = session;
                    // A porta do Level 1 é trancada por teclado (o objetivo tem teste próprio,
                    // Level1ObjectiveTest): trancada, não abre; destrancada, abre.
                    if (ExitLock.Instance != null)
                    {
                        if (door.Interact()) { Finish(false, "a porta do Level 1 abriu trancada"); return; }
                        ExitLock.Instance.UnlockForTest();
                    }
                    if (!door.Interact()) { Finish(false, "a porta do Level 1 não aceitou a interação"); return; }
                    Next();
                    break;
                }

                case 4:
                {
                    // O Level 1 não é mais a última fase: a porta zera e leva às Piscinas, já hospedando.
                    if (session == _previous || !Playing(session) || session.LevelIndex != 2)
                    {
                        if (StageTimedOut(60)) Finish(false, $"abriu a porta do Level 1 e não começou a partida no Level 2 (zerada={LevelProgress.IsCleared(1)}, fase da sessão={session?.Phase})");
                        return;
                    }
                    if (!LevelProgress.IsCleared(1)) { Finish(false, "passou pela porta do Level 1 e a fase não foi zerada"); return; }
                    if (!OneSession(out string pools)) { Finish(false, "duas sessões no Level 2: " + pools); return; }
                    if (PoolWater.Current == null) { Finish(false, "o Level 2 começou sem água"); return; }
                    Notes.Add("a porta do Level 1 zerou a fase e levou ao Level 2, já hospedando");
                    if (!MusicOk(false, out string music2)) { Finish(false, "música do menu jogando o Level 2: " + music2); return; }
                    Notes.Add("em jogo a música do menu fica calada");
                    if (Object.FindObjectsByType<FluorescentHum>(FindObjectsSortMode.None).Length != 0) { Finish(false, "o Level 2 tem zumbido de lâmpada fluorescente"); return; }

                    // Mergulha num poço.
                    var builder = Object.FindFirstObjectByType<LevelBuilder>();
                    LevelData d = builder.Data;
                    bool placed = false;
                    for (int z = 0; z < d.Height && !placed; z++)
                        for (int x = 0; x < d.Width && !placed; x++)
                        {
                            if (d.Depth[x, z] != PoolDepth.Well) continue;
                            Vector3 center = builder.CellCenter(x, z);
                            PlayerNetwork.Local.controller.Teleport(new Vector3(center.x, PoolWater.Current.SurfaceY - 1f, center.z), 0f);
                            placed = true;
                        }
                    if (!placed) { Finish(false, "o Level 2 não tem poço nesta seed"); return; }
                    Next();
                    break;
                }

                case 5:
                    if (!StageTimedOut(2.5)) return;
                    if (!PlayerNetwork.Local.controller.IsSwimming) { Finish(false, "no poço do Level 2 o jogador não nadou"); return; }
                    Notes.Add("nadou num poço do Level 2");
                    if (!FaceExit()) { Finish(false, "o Level 2 não tem saída"); return; }
                    Next();
                    break;

                case 6:
                    LevelExit poolDoor = LevelExit.Instance;
                    if (poolDoor == null || !poolDoor.LocalInReach)
                    {
                        if (StageTimedOut(10)) Finish(false, "de frente para a porta do Level 2, ela não ficou ao alcance");
                        return;
                    }
                    if (!poolDoor.Interact()) { Finish(false, "a porta do Level 2 não aceitou a interação"); return; }
                    Next();
                    break;

                case 7:
                    // É a última fase: a porta zera, a tela escurece e a sessão acaba no menu.
                    if (!LevelProgress.IsCleared(2) || !InMenu(session, 2))
                    {
                        if (StageTimedOut(15)) Finish(false, $"abriu a porta do Level 2 e não terminou no menu com a fase zerada (zerada={LevelProgress.IsCleared(2)}, fase da sessão={session?.Phase})");
                        return;
                    }
                    Notes.Add($"a porta zerou o Level 2 (última fase) e a sessão acabou no menu (níveis zerados: {LevelProgress.ClearedCount})");
                    LevelProgress.DevModeOverride = false;
                    bool stillOpen = LevelProgress.IsUnlocked(0) && LevelProgress.IsUnlocked(1) && LevelProgress.IsUnlocked(2);
                    LevelProgress.DevModeOverride = null;
                    if (!stillOpen) { Finish(false, "depois de zerar tudo, o jogador não tem as três fases liberadas"); return; }

                    if (!session.SwitchLevel(0)) { Finish(false, "não trocou de volta para o Level 0"); return; }
                    _previous = session;
                    Next();
                    break;

                case 8:
                    if (!Switched(session, 0)) { if (StageTimedOut(30)) Finish(false, "a troca de volta para o Level 0 não terminou"); return; }
                    if (!OneSession(out string again)) { Finish(false, "duas sessões depois de voltar ao Level 0: " + again); return; }
                    // Zerou a última fase e voltou ao menu: a música voltou, do ponto em que parou.
                    if (!MusicOk(true, out string music0) || MenuMusic.Instance.Played <= _musicPlayed)
                    {
                        Finish(false, $"música do menu de volta ao menu e ao Level 0: {music0} (antes {_musicPlayed:F1}s)");
                        return;
                    }
                    Notes.Add("de volta ao menu a música voltou, sem recomeçar (" + music0 + ")");
                    if (!HumMuted(out string humBack)) { Finish(false, "zumbido das lâmpadas no menu de volta ao Level 0: " + humBack); return; }
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 9:
                {
                    if (!Playing(session)) { if (StageTimedOut(40)) Finish(false, "não começou a partida no Level 0 depois de voltar"); return; }
                    if (!StageTimedOut(1.5)) return;
                    FluorescentHum playingHum = Object.FindFirstObjectByType<FluorescentHum>();
                    if (playingHum == null || !playingHum.Wanted || playingHum.Level <= 0f) { Finish(false, "zumbido das lâmpadas jogando o Level 0: " + DescribeHum()); return; }

                    // A água das Piscinas não pode sobrar no Level 0.
                    PlayerController controller = PlayerNetwork.Local.controller;
                    var leftovers = new List<string>();
                    if (PoolWater.Current != null) leftovers.Add("água");
                    if (controller.Submersion != 0f || controller.IsSwimming || controller.IsUnderwater) leftovers.Add("jogador ainda na água");
                    foreach (UnityEngine.Rendering.Volume volume in Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
                        if (volume.name == "DebaixoDagua" || volume.name == "LuzRebatida" || volume.name == "Anoitecer") leftovers.Add("volume " + volume.name);
                    // Nem o anoitecer: os vidros de luz do dia e o ciclo mais longo do apagão são só das Piscinas.
                    if (DaylightPanel.All.Count > 0) leftovers.Add($"{DaylightPanel.All.Count} vidro(s) de luz do dia");
                    if (LightsOut.Instance != null && !Mathf.Approximately(LightsOut.Instance.litSeconds, 30f)) leftovers.Add($"apagão com {LightsOut.Instance.litSeconds} s de luz");
                    if (leftovers.Count > 0) { Finish(false, "voltou ao Level 0 com restos das Piscinas: " + string.Join(", ", leftovers)); return; }
                    Notes.Add("voltou ao Level 0 sem nada das Piscinas (água, nado, volumes, anoitecer)");

                    // Agora a porta do Level 0, sozinho: abre na hora e leva para o Level 1.
                    PlayerPrefs.SetInt(LevelProgress.ClearedKey, 0);
                    if (!FaceExit()) { Finish(false, "o Level 0 não tem saída"); return; }
                    _previous = session;
                    Next();
                    break;
                }

                case 10:
                    LevelExit exit0 = LevelExit.Instance;
                    if (exit0 == null || !exit0.LocalInReach)
                    {
                        if (StageTimedOut(10)) Finish(false, "de frente para a porta do Level 0, ela não ficou ao alcance");
                        return;
                    }
                    if (!exit0.Interact()) { Finish(false, "a porta do Level 0 não aceitou a interação"); return; }
                    Next();
                    break;

                case 11:
                    // Quem hospedava hospeda a fase seguinte sozinho.
                    if (session == _previous || !Playing(session) || session.LevelIndex != 1)
                    {
                        if (StageTimedOut(60)) Finish(false, "abriu a porta do Level 0 e não começou a partida no Level 1");
                        return;
                    }
                    if (!LevelProgress.IsCleared(0)) { Finish(false, "passou pela porta do Level 0 e a fase não foi zerada"); return; }
                    if (!OneSession(out string after)) { Finish(false, "duas sessões depois de passar pela porta: " + after); return; }
                    Notes.Add("a porta do Level 0 levou ao Level 1, já hospedando");
                    Next();
                    break;

                case 12:
                    // A fase aberta pela porta nasce conectando e o zumbido dela, calado: entra em
                    // fade até o volume de jogo. Depois sai da partida e confere que ele cala de novo.
                    if (!HumFull(out string humFull))
                    {
                        if (StageTimedOut(10)) Finish(false, "o zumbido não chegou ao volume de jogo no Level 1 depois da porta: " + humFull);
                        return;
                    }
                    session.Leave();
                    Next();
                    break;

                case 13:
                    if (session == null || session.Phase != SessionPhase.Menu) { Finish(false, $"sair da partida não voltou ao menu (fase da sessão={session?.Phase})"); return; }
                    if (!HumMuted(out string humLeft))
                    {
                        // Um quadro de folga para o componente ver a sessão no menu.
                        FluorescentHum leaving = Object.FindFirstObjectByType<FluorescentHum>();
                        if (StageTimedOut(0.5) && (leaving == null || leaving.Wanted)) { Finish(false, "de volta ao menu o zumbido ainda quer soar: " + humLeft); return; }
                        if (StageTimedOut(10)) Finish(false, "de volta ao menu o zumbido não calou: " + humLeft);
                        return;
                    }
                    Notes.Add($"de volta ao menu o zumbido saiu em fade e calou ({Now - _stageStart:F1}s)");
                    Finish(true, string.Join("; ", Notes));
                    break;
            }
        }

        // ---------------------------------------------------------------- Porta, dois editores

        static void TickHostDoor()
        {
            GameSession session = GameSession.Instance;

            switch (_stage)
            {
                case 0:
                    if (!InMenu(session, 0)) { if (StageTimedOut(20)) Finish(false, "o Level 0 não abriu no menu"); return; }
                    // Passar pela porta zera a fase: guarda o progresso desta máquina para devolver no fim.
                    SessionState.SetInt(SavedKey, PlayerPrefs.GetInt(LevelProgress.ClearedKey, -1));
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                    if (!Playing(session)) { if (StageTimedOut(40)) Finish(false, "não começou a partida no Level 0"); return; }
                    Next();
                    break;

                case 2:
                    // Espera o cliente entrar e pôr a mão na porta.
                    PlayerNetwork.CountAtExit(out int waiting, out int total);
                    if (total < 2 || waiting < 1)
                    {
                        if (StageTimedOut(150)) Finish(false, $"o cliente não ficou esperando na porta (esperando {waiting} de {total})");
                        return;
                    }
                    Notes.Add($"o cliente esperou na porta ({waiting}/{total})");
                    Next();
                    break;

                case 3:
                    // Com um só esperando, a porta não pode abrir.
                    if (session.Passing) { Finish(false, "a porta abriu com só um dos dois jogadores esperando"); return; }
                    if (!StageTimedOut(3)) return;
                    Notes.Add("a porta não abriu enquanto faltava o host");
                    if (!FaceExit(0.4f)) { Finish(false, "o Level 0 não tem saída"); return; }
                    _previous = session;
                    Next();
                    break;

                case 4:
                    LevelExit door = LevelExit.Instance;
                    if (door == null || !door.LocalInReach)
                    {
                        if (StageTimedOut(10)) Finish(false, "de frente para a porta, ela não ficou ao alcance do host");
                        return;
                    }
                    if (!door.Interact()) { Finish(false, "a porta não aceitou a interação do host"); return; }
                    Next();
                    break;

                case 5:
                    if (session == _previous || !Playing(session) || session.LevelIndex != 1)
                    {
                        if (StageTimedOut(60)) Finish(false, "os dois abriram a porta e o host não começou a partida no Level 1");
                        return;
                    }
                    Notes.Add("o host passou para o Level 1 e hospedou de novo");
                    Next();
                    break;

                case 6:
                    if (Object.FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None).Length < 2)
                    {
                        if (StageTimedOut(90)) Finish(false, "o cliente não reconectou no Level 1");
                        return;
                    }
                    Notes.Add("o cliente reconectou no Level 1");
                    Next();
                    break;

                case 7:
                    // Dá tempo ao cliente de terminar o teste dele antes de fechar a sessão.
                    if (!StageTimedOut(10)) return;
                    Finish(true, string.Join("; ", Notes) + "; " + Describe(session));
                    break;
            }
        }

        static void TickJoinDoor()
        {
            GameSession session = GameSession.Instance;

            switch (_stage)
            {
                case 0:
                    if (!InMenu(session, 0)) { if (StageTimedOut(20)) Finish(false, "o Level 0 não abriu no menu"); return; }
                    Next();
                    break;

                case 1:
                    // O host pode ainda não ter aberto a sessão: tenta de novo até ele aparecer.
                    if (Playing(session)) { Next(); return; }
                    if (session != null && session.Phase == SessionPhase.Menu && Now - _lastTry > 4)
                    {
                        _lastTry = Now;
                        session.Join("127.0.0.1");
                    }
                    if (StageTimedOut(150)) Finish(false, "o cliente não entrou na partida do Level 0");
                    break;

                case 2:
                    if (!FaceExit(-0.4f)) { Finish(false, "o Level 0 não tem saída"); return; }
                    _previous = session;
                    Next();
                    break;

                case 3:
                    LevelExit door = LevelExit.Instance;
                    if (door == null || !door.LocalInReach)
                    {
                        if (StageTimedOut(10)) Finish(false, "de frente para a porta, ela não ficou ao alcance do cliente");
                        return;
                    }
                    if (!door.Interact()) { Finish(false, "a porta não aceitou a interação do cliente"); return; }
                    Next();
                    break;

                case 4:
                    // O host ainda não abriu: o cliente tem de continuar no Level 0, esperando.
                    if (!StageTimedOut(2)) return;
                    LevelExit waitingAt = LevelExit.Instance;
                    if (session != _previous || session.Passing || waitingAt == null || !waitingAt.LocalWaiting)
                    {
                        Finish(false, "o cliente abriu a porta sozinho e não ficou esperando o host");
                        return;
                    }
                    Notes.Add("o cliente ficou esperando o host na porta");
                    Next();
                    break;

                case 5:
                    if (session == _previous || !Playing(session) || session.LevelIndex != 1)
                    {
                        if (StageTimedOut(150)) Finish(false, "o cliente não chegou à partida do Level 1 depois que os dois abriram a porta");
                        return;
                    }
                    if (!OneSession(out string sessions)) { Finish(false, "duas sessões no cliente depois de passar pela porta: " + sessions); return; }
                    if (!LevelProgress.IsCleared(0)) { Finish(false, "o cliente passou pela porta e o Level 0 não foi zerado para ele"); return; }
                    Notes.Add("o cliente passou para o Level 1 e reconectou");
                    Finish(true, string.Join("; ", Notes) + "; " + Describe(session));
                    break;
            }
        }

        // Põe o jogador desta máquina de frente para a porta de saída, a um passo dela.
        // Com dois jogadores, cada um fica de um lado.
        static bool FaceExit(float side = 0f)
        {
            LevelExit exit = LevelExit.Instance;
            if (exit == null || PlayerNetwork.Local == null) return false;

            Vector3 foot = exit.DoorPosition;
            Vector3 inward = exit.transform.position - foot;
            inward.y = 0f;
            inward.Normalize();

            Vector3 spot = new Vector3(foot.x, foot.y + 0.05f, foot.z) + inward * 1.1f + Vector3.Cross(Vector3.up, inward) * side;
            PlayerNetwork.Local.controller.Teleport(spot, Quaternion.LookRotation(-inward, Vector3.up).eulerAngles.y);
            return true;
        }

        // ---------------------------------------------------------------- Dois editores

        static void TickHost()
        {
            GameSession session = GameSession.Instance;

            switch (_stage)
            {
                case 0:
                    if (!InMenu(session, 0)) { if (StageTimedOut(20)) Finish(false, "o Level 0 não abriu no menu"); return; }
                    _previous = session;
                    session.SwitchLevel(1);
                    Next();
                    break;

                case 1:
                    if (!Switched(session, 1)) { if (StageTimedOut(30)) Finish(false, "a troca para o Level 1 não terminou"); return; }
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 2:
                    if (!Playing(session)) { if (StageTimedOut(40)) Finish(false, "não começou a partida no Level 1"); return; }
                    Debug.Log($"[BackRooms] Seletor (host): hospedando o Level 1, {Describe(session)}");
                    Next();
                    break;

                case 3:
                    // Espera o cliente entrar (ele começa no Level 0 e precisa trocar de fase).
                    if (Object.FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None).Length < 2)
                    {
                        if (StageTimedOut(150)) Finish(false, "nenhum cliente entrou no Level 1");
                        return;
                    }
                    Notes.Add("o cliente entrou no Level 1");
                    Next();
                    break;

                case 4:
                    // Dá tempo ao cliente de terminar o teste dele antes de fechar a sessão.
                    if (!StageTimedOut(10)) return;
                    Finish(true, "hospedou o Level 1 e o cliente entrou; " + Describe(session));
                    break;
            }
        }

        static void TickJoin()
        {
            GameSession session = GameSession.Instance;
            if (session != null && session.LevelIndex == 1) _switched = true;

            switch (_stage)
            {
                case 0:
                    if (!InMenu(session, 0)) { if (StageTimedOut(20)) Finish(false, "o Level 0 não abriu no menu"); return; }
                    Next();
                    break;

                case 1:
                    // O host pode ainda não ter aberto a sessão: tenta de novo até ele aparecer.
                    if (session != null && session.LevelIndex == 1) { Next(); return; }
                    if (session != null && session.Phase == SessionPhase.Menu && Now - _lastTry > 4)
                    {
                        _lastTry = Now;
                        session.Join("127.0.0.1");
                    }
                    if (StageTimedOut(150)) Finish(false, "o cliente não foi levado para o Level 1");
                    break;

                case 2:
                    if (!Playing(session) || session.LevelIndex != 1) { if (StageTimedOut(60)) Finish(false, "trocou para o Level 1 mas não entrou na partida"); return; }
                    if (!OneSession(out string sessions)) { Finish(false, "duas sessões no cliente depois da troca: " + sessions); return; }
                    Finish(_switched, "começou no Level 0, foi levado para o Level 1 e reconectou; " + Describe(session));
                    break;
            }
        }

        // ---------------------------------------------------------------- Conferências

        static bool InMenu(GameSession session, int level) =>
            session != null && session.Phase == SessionPhase.Menu && session.LevelIndex == level;

        static bool Switched(GameSession session, int level) =>
            session != null && session != _previous && InMenu(session, level) &&
            SceneManager.GetActiveScene().name == LevelCatalog.Get(level).Scene;

        static bool Playing(GameSession session) =>
            session != null && session.Phase == SessionPhase.Playing && session.LevelReady && PlayerNetwork.Local != null;

        static bool OneSession(out string names)
        {
            NetworkManager[] managers = Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
            GameSession[] sessions = Object.FindObjectsByType<GameSession>(FindObjectsSortMode.None);
            names = $"{managers.Length} NetworkManager, {sessions.Length} GameSession";
            return managers.Length == 1 && sessions.Length == 1;
        }

        // Música do menu (MenuMusic): um dono só, que sobreviveu às trocas de cena sem recomeçar
        // (Starts == 1); no menu quer tocar; jogando, calada e em pausa. Confere o estado do
        // componente, não o isPlaying da fonte (sem dispositivo de áudio ele nunca é verdadeiro).
        static bool MusicOk(bool menu, out string why)
        {
            MenuMusic music = MenuMusic.Instance;
            int found = Object.FindObjectsByType<MenuMusic>(FindObjectsSortMode.None).Length;
            why = music == null ? "sem dono da música"
                : $"donos={MenuMusic.Alive} achados={found} quer_tocar={music.Wanted} nivel={music.Level:F2} " +
                  $"pausa={music.Paused} comecos={music.Starts} tocado={music.Played:F1}s";
            if (music == null || music.clip == null || MenuMusic.Alive != 1 || found != 1 || music.Starts != 1) return false;
            return menu ? music.Wanted && !music.Paused : !music.Wanted && music.Level <= 0f && music.Paused;
        }

        // Zumbido das lâmpadas (FluorescentHum), um por cena nos Levels 0 e 1: no menu, calado
        // (não quer soar e o nível do fade está em zero); jogando, no volume de jogo. Confere a
        // conta do componente, não a fonte (sem dispositivo de áudio ela não toca).
        static bool HumMuted(out string why)
        {
            why = DescribeHum();
            FluorescentHum[] hums = Object.FindObjectsByType<FluorescentHum>(FindObjectsSortMode.None);
            return hums.Length == 1 && !hums[0].Wanted && hums[0].Level <= 0f && hums[0].CurrentVolume <= 0f;
        }

        static bool HumFull(out string why)
        {
            why = DescribeHum();
            FluorescentHum[] hums = Object.FindObjectsByType<FluorescentHum>(FindObjectsSortMode.None);
            return hums.Length == 1 && hums[0].Wanted && hums[0].Level >= 0.999f &&
                   Mathf.Abs(hums[0].CurrentVolume - hums[0].volume) < 0.001f;
        }

        static string DescribeHum()
        {
            FluorescentHum[] hums = Object.FindObjectsByType<FluorescentHum>(FindObjectsSortMode.None);
            if (hums.Length != 1) return $"{hums.Length} zumbido(s) na cena";
            FluorescentHum hum = hums[0];
            return $"quer_soar={hum.Wanted} nivel={hum.Level:F2} volume={hum.CurrentVolume:F3} (de jogo {hum.volume:F2})";
        }

        static string Describe(GameSession session)
        {
            var props = Object.FindFirstObjectByType<PropLayer>();
            return $"fase={session.LevelIndex} seed={session.Seed} moveis={(props != null ? props.Count : 0)} hash_moveis={(props != null ? props.Hash : 0):X8}";
        }

        static void Finish(bool ok, string report)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
            LevelProgress.DevModeOverride = null;

            // Devolve o progresso que esta máquina tinha antes do teste.
            int saved = SessionState.GetInt(SavedKey, int.MinValue);
            if (saved != int.MinValue)
            {
                if (saved < 0) PlayerPrefs.DeleteKey(LevelProgress.ClearedKey);
                else PlayerPrefs.SetInt(LevelProgress.ClearedKey, saved);
                PlayerPrefs.Save();
                SessionState.EraseInt(SavedKey);
            }

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log("[BackRooms] Teste do seletor de fase (" + _role + ") " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

            // Em Play o editor guarda como última cena a que estava aberta antes do Play: o Level 0.
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 3);
            else EditorApplication.ExitPlaymode();
        }
    }
}
