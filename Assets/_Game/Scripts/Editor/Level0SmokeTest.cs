using System.Collections.Generic;
using System.Reflection;
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
    /// Testes automáticos em Play da cena do Level 0.
    /// • Sozinho (menu BackRooms > Testar Level 0): hospeda, segura W por 2 s e confere que
    ///   nada lançou erro, que o jogador anda apoiado no chão e que os passos tocam.
    /// • Host e Cliente (linha de comando, um editor para cada): o host espera o segundo
    ///   jogador e anda; o cliente confere que recebeu o mesmo mapa e que vê o host andar.
    /// </summary>
    public static class Level0SmokeTest
    {
        const string ScenePath = "Assets/_Game/Scenes/Level0_Test.unity";
        const string RoleKey = "BackRooms.SmokeTest.Role";
        const string BatchKey = "BackRooms.SmokeTest.Batch";

        const string Solo = "solo";
        const string HostRole = "host";
        const string ClientRole = "client";

        const double Timeout = 150.0;

        static readonly List<string> Errors = new List<string>();
        static string _role;
        static double _began;
        static double _phaseStart;
        static int _stage;
        static int _steps;
        static Vector3 _start;
        static Keyboard _keyboard;
        static PlayerNetwork _local;
        static PlayerNetwork _remote;
        static Vector3 _remoteStart;
        static float _remoteMoved;
        static bool _sawRemoteFlashlight;
        static int _mostPlayers;

        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;

        [MenuItem("BackRooms/Testar Level 0 (fumaça)")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(Solo, false);
        }

        public static void BatchRun() { Run(Solo, true); }
        public static void BatchHost() { Run(HostRole, true); }
        public static void BatchJoin() { Run(ClientRole, true); }

        static void Run(string role, bool batch)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetString(RoleKey, role);
            SessionState.SetBool(BatchKey, batch);
            EditorApplication.EnterPlaymode();
        }

        // Entrar em Play recarrega o domínio; o estado sobrevive pelo SessionState.
        [InitializeOnLoadMethod]
        static void Hook()
        {
            _role = SessionState.GetString(RoleKey, "");
            if (string.IsNullOrEmpty(_role)) return;

            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;

            // A indexação de busca do próprio editor pode falhar na abertura do projeto;
            // não tem relação com o jogo.
            if (stackTrace != null && stackTrace.Contains("UnityEditor.Search.")) return;

            // O assistente de IA do editor reclama quando há dois editores abertos na mesma
            // máquina (caso do teste de host e cliente). Também não é do jogo.
            if (stackTrace != null && stackTrace.Contains("Unity.AI.")) return;

            Errors.Add(message);
        }

        static double Now => EditorApplication.timeSinceStartup;

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;

            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = Now;
            if (Now - _began > Timeout)
            {
                Finish(false, "tempo esgotado na etapa " + _stage + " (fase da sessão: " + session.Phase + ", estado: " + session.Status + ")");
                return;
            }

            switch (_stage)
            {
                case 0:
                    // Abre a sessão conforme o papel. Seed fixa, para o teste ser repetível.
                    session.fixedSeed = 1998;

                    if (_role == Solo)
                    {
                        // Sozinho, a sessão é aberta pelo atalho H do menu, para testar o menu também.
                        BeginWalking();
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.H));
                        if (session.Phase != SessionPhase.Menu) Next();
                        else if (Now - _began > 10.0) Finish(false, "o atalho H do menu não abriu a sessão");
                        break;
                    }

                    if (_role == ClientRole) session.Join("127.0.0.1");
                    else session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                    // Espera o jogador local estar no mapa. O cliente tenta de novo se o host ainda não subiu.
                    if (_role == ClientRole && session.Phase == SessionPhase.Menu && Now - _phaseStart > 2.0)
                    {
                        session.Join("127.0.0.1");
                        _phaseStart = Now;
                    }

                    if (session.Phase != SessionPhase.Playing) break;
                    FindPlayers();
                    if (_local == null) break;

                    _local.controller.Stepped += OnStepped;
                    Next();
                    break;

                case 2:
                    // O host só continua quando o segundo jogador chegou.
                    if (_role == HostRole && session.PlayerCount < 2) break;
                    if (Now - _phaseStart < 1.0) break;

                    FindPlayers();
                    _start = _local.transform.position;
                    if (_remote != null) _remoteStart = _remote.transform.position;

                    if (_role != ClientRole) BeginWalking();
                    Next();
                    break;

                case 3:
                    FindPlayers();
                    Observe();

                    if (_role == ClientRole)
                    {
                        // O cliente fica parado olhando o host: passa assim que o vê andar.
                        if (_remoteMoved > 1.0f || Now - _phaseStart > 40.0) Evaluate(session);
                        break;
                    }

                    // Segura W. O host anda mais tempo, para o cliente ter o que observar.
                    double walk = _role == HostRole ? 6.0 : 2.0;
                    if (Now - _phaseStart < walk)
                    {
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W, Key.F));
                        break;
                    }

                    Evaluate(session);
                    break;
            }
        }

        static void Next()
        {
            _stage++;
            _phaseStart = Now;
        }

        static void FindPlayers()
        {
            int spawned = 0;

            foreach (PlayerNetwork player in Object.FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None))
            {
                if (!player.IsSpawned) continue;
                spawned++;
                if (player.IsOwner) _local = player;
                else _remote = player;
            }

            // O outro editor pode terminar o teste dele e sair antes; vale o máximo que se viu.
            _mostPlayers = Mathf.Max(_mostPlayers, spawned);
        }

        static void Observe()
        {
            if (_remote == null) return;

            _remoteMoved = Mathf.Max(_remoteMoved, Vector3.Distance(_remoteStart, _remote.transform.position));
            if (_remote.flashlight != null && _remote.flashlight.IsOn) _sawRemoteFlashlight = true;
        }

        static void BeginWalking()
        {
            if (_keyboard != null) return;

            // Sem isto o Input System descarta eventos quando a janela não tem foco.
            _backgroundBefore = InputSystem.settings.backgroundBehavior;
            _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        static void OnStepped(float loudness)
        {
            _steps++;
        }

        static void Evaluate(GameSession session)
        {
            var controller = _local.GetComponent<CharacterController>();
            var builder = Object.FindFirstObjectByType<LevelBuilder>();
            int lights = Object.FindObjectsByType<CeilingLight>(FindObjectsSortMode.None).Length;
            int marks = builder != null && builder.Data != null ? builder.Data.Decorations.Count : 0;
            float moved = Vector3.Distance(_start, _local.transform.position);
            float y = _local.transform.position.y;
            float stepPeak = MeasureStepPeak(_local);
            int players = _mostPlayers;

            bool grounded = controller != null && controller.isGrounded && Mathf.Abs(y) < 0.2f;
            bool built = session.LevelReady && builder != null && builder.Data != null && lights > 0;
            bool ok = Errors.Count == 0 && grounded && built && stepPeak > 0.5f;

            string report = $"papel={_role} erros={Errors.Count} apoiado={grounded} luminarias={lights} marcas={marks} " +
                            $"seed={session.Seed} mapa={LevelHash(builder):X8} jogadores_vistos={players}";

            if (_role == ClientRole)
            {
                // O cliente não anda; precisa ter visto o outro jogador se mexer.
                ok &= players >= 2 && _remoteMoved > 1.0f;
                report += $" host_andou={_remoteMoved:F2}m lanterna_do_host_vista={_sawRemoteFlashlight}";
            }
            else
            {
                ok &= moved > 0.5f && _steps > 0;
                report += $" andou={moved:F2}m passos={_steps} pico_do_clipe_de_passo={stepPeak:F2}";
                if (_role == HostRole) ok &= players >= 2;
            }

            if (Errors.Count > 0) report += "\nPrimeiro erro: " + Errors[0];

            Finish(ok, report);
        }

        // Resumo do mapa gerado: se host e cliente imprimem o mesmo valor, o labirinto é igual.
        static uint LevelHash(LevelBuilder builder)
        {
            if (builder == null || builder.Data == null) return 0;

            LevelData data = builder.Data;
            uint hash = 2166136261;

            void Mix(int value)
            {
                unchecked { hash = (hash ^ (uint)value) * 16777619u; }
            }

            foreach (bool wall in data.WallSouth) Mix(wall ? 1 : 0);
            foreach (bool wall in data.WallWest) Mix(wall ? 1 : 0);
            foreach (byte alcove in data.AlcoveSouth) Mix(alcove);
            foreach (byte alcove in data.AlcoveWest) Mix(alcove);
            foreach (bool pillar in data.Pillar) Mix(pillar ? 1 : 0);
            foreach (LightState state in data.Lights) Mix((int)state);
            foreach (Decoration mark in data.Decorations)
            {
                Mix((int)mark.Kind); Mix(mark.X); Mix(mark.Z); Mix((int)mark.Side); Mix(mark.Variant);
                Mix(Mathf.RoundToInt(mark.Along * 1000f)); Mix(Mathf.RoundToInt(mark.Height * 1000f));
            }
            Mix(data.ExitX); Mix(data.ExitZ); Mix((int)data.ExitSide);

            return hash;
        }

        // Lê os clipes sintetizados do componente Footsteps e devolve o maior valor absoluto.
        static float MeasureStepPeak(PlayerNetwork player)
        {
            var footsteps = player.GetComponent<Footsteps>();
            if (footsteps == null) return 0f;

            FieldInfo field = typeof(Footsteps).GetField("_clips", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || !(field.GetValue(footsteps) is AudioClip[] clips)) return 0f;

            float peak = 0f;
            foreach (AudioClip clip in clips)
            {
                if (clip == null) continue;
                var data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
                foreach (float sample in data) peak = Mathf.Max(peak, Mathf.Abs(sample));
            }

            return peak;
        }

        static void Finish(bool ok, string report)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetString(RoleKey, "");

            if (_local != null && _local.controller != null) _local.controller.Stepped -= OnStepped;

            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                InputSystem.settings.backgroundBehavior = _backgroundBefore;
                InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBefore;
            }

            Debug.Log("[BackRooms] Teste de fumaça " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

            if (SessionState.GetBool(BatchKey, false)) EditorApplication.Exit(ok ? 0 : 3);
            else EditorApplication.ExitPlaymode();
        }
    }
}
