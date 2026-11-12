using System.Collections.Generic;
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
    /// fica bloqueado; no modo desenvolvedor troca para ele, hospeda, leva o jogador até a
    /// saída e confere que a fase foi zerada; sai, volta para o Level 0 e hospeda de novo. Em
    /// cada fase confere que só existe uma sessão (um NetworkManager). O progresso salvo nesta
    /// máquina é restaurado no fim.
    ///
    /// Dois editores (o segundo numa cópia do projeto): <see cref="BatchHostLevel1"/> troca para
    /// o Level 1 e hospeda; <see cref="BatchJoinFromLevel0"/> entra a partir do Level 0 e tem de
    /// trocar sozinho para o Level 1 e reconectar. Os dois escrevem seed e mobília no log.
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

                    var exit = Object.FindFirstObjectByType<LevelExit>();
                    if (exit == null) { Finish(false, "o Level 1 não tem saída"); return; }
                    Vector3 spot = exit.transform.position;
                    PlayerNetwork.Local.controller.Teleport(new Vector3(spot.x, 0.05f, spot.z), 0f);
                    Next();
                    break;

                case 3:
                    if (!LevelProgress.IsCleared(1)) { if (StageTimedOut(10)) Finish(false, "chegou à saída do Level 1 e a fase não foi marcada como zerada"); return; }
                    Notes.Add($"saída zerou o Level 1 (níveis zerados: {LevelProgress.ClearedCount})");
                    session.Leave();
                    Next();
                    break;

                case 4:
                    if (!InMenu(session, 1)) { if (StageTimedOut(15)) Finish(false, "não voltou ao menu depois de sair"); return; }
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
                    if (PoolWater.Current != null) { Finish(false, "o Level 0 tem água"); return; }
                    Notes.Add("voltou ao Level 0 e hospedou de novo");
                    session.Leave();
                    Next();
                    break;

                // Ida e volta às Piscinas: a água não pode sobrar no Level 0.
                case 7:
                    if (!InMenu(session, 0)) { if (StageTimedOut(15)) Finish(false, "não voltou ao menu no Level 0"); return; }
                    if (!session.SwitchLevel(2)) { Finish(false, "não trocou para o Level 2"); return; }
                    _previous = session;
                    Next();
                    break;

                case 8:
                    if (!Switched(session, 2)) { if (StageTimedOut(30)) Finish(false, "a troca para o Level 2 não terminou"); return; }
                    if (!OneSession(out string pools)) { Finish(false, "duas sessões no Level 2: " + pools); return; }
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 9:
                {
                    if (!Playing(session)) { if (StageTimedOut(40)) Finish(false, "não começou a partida no Level 2"); return; }
                    if (PoolWater.Current == null) { Finish(false, "o Level 2 começou sem água"); return; }

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

                case 10:
                    if (!StageTimedOut(2.5)) return;
                    if (!PlayerNetwork.Local.controller.IsSwimming) { Finish(false, "no poço do Level 2 o jogador não nadou"); return; }
                    Notes.Add("foi ao Level 2 e nadou num poço");
                    session.Leave();
                    Next();
                    break;

                case 11:
                    if (!InMenu(session, 2)) { if (StageTimedOut(15)) Finish(false, "não voltou ao menu no Level 2"); return; }
                    if (!session.SwitchLevel(0)) { Finish(false, "não trocou do Level 2 para o Level 0"); return; }
                    _previous = session;
                    Next();
                    break;

                case 12:
                    if (!Switched(session, 0)) { if (StageTimedOut(30)) Finish(false, "a volta do Level 2 para o Level 0 não terminou"); return; }
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 13:
                {
                    if (!Playing(session)) { if (StageTimedOut(40)) Finish(false, "não começou a partida no Level 0 depois das Piscinas"); return; }
                    if (!StageTimedOut(1.5)) return;

                    PlayerController controller = PlayerNetwork.Local.controller;
                    var leftovers = new List<string>();
                    if (PoolWater.Current != null) leftovers.Add("água");
                    if (controller.Submersion != 0f || controller.IsSwimming || controller.IsUnderwater) leftovers.Add("jogador ainda na água");
                    foreach (UnityEngine.Rendering.Volume volume in Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
                        if (volume.name == "DebaixoDagua" || volume.name == "LuzRebatida") leftovers.Add("volume " + volume.name);
                    if (leftovers.Count > 0) { Finish(false, "voltou ao Level 0 com restos das Piscinas: " + string.Join(", ", leftovers)); return; }

                    Notes.Add("voltou ao Level 0 sem nada das Piscinas (água, nado, volumes)");
                    Finish(true, string.Join("; ", Notes));
                    break;
                }
            }
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
