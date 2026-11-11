using System.Collections.Generic;
using BackRooms.Net;
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
    /// fica bloqueado; no modo desenvolvedor troca para ele, hospeda, leva o jogador até a
    /// saída e confere que a fase foi zerada; sai, volta para o Level 0 e hospeda de novo. Em
    /// cada fase confere que só existe uma sessão (um NetworkManager). O progresso salvo nesta
    /// máquina é restaurado no fim.
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
                    // Só chegar perto não zera mais: é preciso abrir a porta (tecla E).
                    LevelExit door = LevelExit.Instance;
                    if (door == null || !door.LocalInReach)
                    {
                        if (StageTimedOut(10)) Finish(false, "de frente para a porta do Level 1, ela não ficou ao alcance");
                        return;
                    }
                    if (LevelProgress.IsCleared(1)) { Finish(false, "o Level 1 foi zerado só de chegar perto da porta"); return; }
                    if (!door.Interact()) { Finish(false, "a porta do Level 1 não aceitou a interação"); return; }
                    Next();
                    break;

                case 4:
                    // É a última fase: a porta zera, a tela escurece e a sessão acaba no menu.
                    if (!LevelProgress.IsCleared(1) || !InMenu(session, 1))
                    {
                        if (StageTimedOut(15)) Finish(false, $"abriu a porta do Level 1 e não terminou no menu com a fase zerada (zerada={LevelProgress.IsCleared(1)}, fase da sessão={session?.Phase})");
                        return;
                    }
                    Notes.Add($"a porta zerou o Level 1 e a sessão acabou no menu (níveis zerados: {LevelProgress.ClearedCount})");
                    LevelProgress.DevModeOverride = false;
                    bool stillOpen = LevelProgress.IsUnlocked(0) && LevelProgress.IsUnlocked(1);
                    LevelProgress.DevModeOverride = null;
                    if (!stillOpen) { Finish(false, "depois de zerar, o jogador não tem o Level 0 e o Level 1 liberados"); return; }

                    if (!session.SwitchLevel(0)) { Finish(false, "não trocou de volta para o Level 0"); return; }
                    _previous = session;
                    Next();
                    break;

                case 5:
                    if (!Switched(session, 0)) { if (StageTimedOut(30)) Finish(false, "a troca de volta para o Level 0 não terminou"); return; }
                    if (!OneSession(out string again)) { Finish(false, "duas sessões depois de voltar ao Level 0: " + again); return; }
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 6:
                    if (!Playing(session)) { if (StageTimedOut(40)) Finish(false, "não começou a partida no Level 0 depois de voltar"); return; }
                    Notes.Add("voltou ao Level 0 e hospedou de novo");

                    // Agora a porta do Level 0, sozinho: abre na hora e leva para o Level 1.
                    PlayerPrefs.SetInt(LevelProgress.ClearedKey, 0);
                    if (!FaceExit()) { Finish(false, "o Level 0 não tem saída"); return; }
                    _previous = session;
                    Next();
                    break;

                case 7:
                    LevelExit exit0 = LevelExit.Instance;
                    if (exit0 == null || !exit0.LocalInReach)
                    {
                        if (StageTimedOut(10)) Finish(false, "de frente para a porta do Level 0, ela não ficou ao alcance");
                        return;
                    }
                    if (!exit0.Interact()) { Finish(false, "a porta do Level 0 não aceitou a interação"); return; }
                    Next();
                    break;

                case 8:
                    // Quem hospedava hospeda a fase seguinte sozinho.
                    if (session == _previous || !Playing(session) || session.LevelIndex != 1)
                    {
                        if (StageTimedOut(60)) Finish(false, "abriu a porta do Level 0 e não começou a partida no Level 1");
                        return;
                    }
                    if (!LevelProgress.IsCleared(0)) { Finish(false, "passou pela porta do Level 0 e a fase não foi zerada"); return; }
                    if (!OneSession(out string after)) { Finish(false, "duas sessões depois de passar pela porta: " + after); return; }
                    Notes.Add("a porta do Level 0 levou ao Level 1, já hospedando");
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

            Vector3 spot = new Vector3(foot.x, 0.05f, foot.z) + inward * 1.1f + Vector3.Cross(Vector3.up, inward) * side;
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
