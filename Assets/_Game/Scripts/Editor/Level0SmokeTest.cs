using System.Collections.Generic;
using System.Reflection;
using BackRooms.Entities;
using BackRooms.Items;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.UI;
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
    /// • Buracos: todos os papéis andam sobre uma faixa e depois se jogam no vazio, para
    ///   conferir que a faixa segura o jogador e que a queda devolve à sala inicial.
    /// • Apagão: o primeiro é adiantado para os 20 s. O teste confere que as luminárias avisam
    ///   (piscando, depois piscando depressa), apagam todas e voltam.
    /// • Gravação: grava 3 s de vídeo e confere que o arquivo saiu com quadros.
    /// • Traje e mão: o corpo do outro jogador é a figura nova, mostra o item que ele pegou,
    ///   e a mão em primeira pessoa aparece com a caneta.
    /// • Entidade: fica desligada até a hora dela. Então o jogador é posto longe da sala e o
    ///   teste confere que ela aparece entre ele e a sala, que ouve o barulho dele, que vai
    ///   atrás e que o pega (e ele volta para a sala). O cliente confere que a vê se mexer.
    /// </summary>
    public static class Level0SmokeTest
    {
        const string ScenePath = "Assets/_Game/Scenes/Level0_Test.unity";
        const string RoleKey = "BackRooms.SmokeTest.Role";
        const string BatchKey = "BackRooms.SmokeTest.Batch";
        const string LateKey = "BackRooms.SmokeTest.LateJoin";

        const string Solo = "solo";
        const string HostRole = "host";
        const string ClientRole = "client";

        const double Timeout = 210.0;

        static readonly List<string> Errors = new List<string>();
        static string _role;
        static double _began;
        static double _phaseStart;
        static int _stage;
        static int _steps;
        static Vector3 _start;
        static Keyboard _keyboard;
        static Mouse _mouse;
        static double _lastMouseEvent;
        static double _penReady;
        static int _rayChecks;
        static int _rayOffLevel;
        static int _raySelfHits;
        static string _rayFirst;
        static PlayerNetwork _local;
        static PlayerNetwork _remote;
        static Vector3 _remoteStart;
        static float _remoteMoved;
        static bool _sawRemoteFlashlight;
        static int _mostPlayers;
        static float _movedMost;
        static int _pitStep;
        static double _pitStepStart;
        static Vector3 _stripGoal;
        static int _fallsBefore;
        static bool _pitTested;
        static bool _stripOk;
        static bool _fallOk;
        static string _pitNote = "";
        static int _entityStep;
        static double _entityStepStart;
        static Vector3 _entityPost;
        static bool _entityForward = true;
        static bool _entityTested;
        static bool _entitySpawnOk;
        static bool _entityHeardOk;
        static bool _entityTrackedOk;
        static bool _entityCatchOk;
        static string _entityNote = "";
        static bool _entitySeen;
        static bool _sawEntityMove;
        static Vector3 _entityFirst;
        static bool _sawWarning, _sawHurry, _sawDark, _sawLightBack;
        static int _litBeforeDark;
        static int _litInDark;
        static int _recordStep;
        static double _recordStart;
        static bool _recordOk;
        static string _recordNote = "";
        static RenderTexture _recordTarget;
        static bool _recordWarm;
        static bool _handOk;
        static bool _sawRemoteFigure;
        static bool _sawRemoteHolding;
        static bool _sawRemoteBlue;
        static int _handSamples;
        static float _handClosest = float.PositiveInfinity;
        static float _handDeepest = float.NegativeInfinity;
        static float _tipGap;
        static int _tipOffScreen;
        static int _penKeyStep;
        static int _penKeyTicks;
        static bool _penKeyReleased;
        static double _penKeyHalfStart;
        static bool _penKeysOk;
        static string _penKeyLog = "";
        static int _baitStep;
        static double _baitStart;
        static bool _clientCaughtOk;
        static string _baitNote = "";

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

        // Variante "quem entra depois": o host risca sozinho e só então o cliente chega.
        // Confere o envio de todos os riscos já existentes na conexão.
        public static void BatchHostDrawFirst() { SessionState.SetBool(LateKey, true); Run(HostRole, true); }
        public static void BatchJoinLate() { SessionState.SetBool(LateKey, true); Run(ClientRole, true); }

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
        static bool LateJoin => SessionState.GetBool(LateKey, false);

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

            if (_stage >= 2) AuditPenRay();
            if (_stage >= 2) WatchLights();
            if (_stage >= 3 && _local != null) RecordStage();

            switch (_stage)
            {
                case 0:
                    // Abre a sessão conforme o papel. Seed fixa, para o teste ser repetível.
                    session.fixedSeed = 1998;
                    // A entidade só entra na etapa dela; antes, atrapalharia as outras.
                    EntityDirector.Paused = true;
                    // O primeiro apagão é adiantado, para caber no teste. Host e cliente fazem a mesma conta.
                    if (LightsOut.Instance != null) LightsOut.Instance.firstDarkAt = 20f;

                    if (_role == Solo)
                    {
                        // Sozinho, a sessão é aberta pelo atalho H do menu, para testar o menu também.
                        BeginWalking();
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.H));
                        if (session.Phase != SessionPhase.Menu) Next();
                        else if (Now - _began > 10.0) Finish(false, "o atalho H do menu não abriu a sessão");
                        break;
                    }

                    // Na variante de quem entra depois, o cliente espera o host acabar de riscar.
                    if (_role == ClientRole && LateJoin && Now - _began < 35.0) break;

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
                    if (_role == HostRole && !LateJoin && session.PlayerCount < 2) break;
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
                        // O cliente fica parado olhando o host: passa quando o vê andar e
                        // quando os riscos de caneta dele chegam pela rede.
                        bool sawEverything = (LateJoin || _remoteMoved > 1.0f) && BoardPoints() >= 10;

                        // Quem entrou depois sai logo (o host só o espera por 2 s); os demais
                        // ainda testam os buracos, como dono de um jogador que não é o host.
                        if (sawEverything && !LateJoin)
                        {
                            _stage = 5;
                            _phaseStart = Now;
                        }
                        else if (sawEverything || Now - _phaseStart > 60.0) Evaluate(session);
                        break;
                    }

                    _movedMost = Mathf.Max(_movedMost, Vector3.Distance(_start, _local.transform.position));

                    // Segura W. O host anda mais tempo, para o cliente ter o que observar.
                    double walk = _role == HostRole ? 6.0 : 2.0;
                    if (Now - _phaseStart < walk)
                    {
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W, Key.F));
                        break;
                    }

                    // Depois de andar: recua, e então corre, pula e recua olhando para o chão.
                    // É o pior caso para o raio da caneta, que não pode bater no próprio jogador.
                    double acrobatics = Now - _phaseStart - walk;

                    if (acrobatics < 1.5)
                    {
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.S));
                        break;
                    }

                    if (acrobatics < 4.5)
                    {
                        bool tap = (int)(acrobatics * 5.0) % 2 == 0;
                        Key move = acrobatics < 3.0 ? Key.W : Key.S;
                        InputSystem.QueueStateEvent(_keyboard, tap ? new KeyboardState(move, Key.LeftShift, Key.Space)
                                                                   : new KeyboardState(move, Key.LeftShift));
                        Look(_local.controller.Pitch < 60f ? -10f : 0f);
                        break;
                    }

                    if (acrobatics < 8.0)
                    {
                        // Volta a olhar em frente e encosta de novo na parede, para riscar.
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
                        Look(_local.controller.Pitch > 2f ? 10f : 0f);
                        break;
                    }

                    Next();
                    break;

                case 4:
                    // Já encostado na parede: pega a caneta (tecla 1) e risca arrastando o mouse.
                    FindPlayers();
                    Observe();
                    PenTool held = _local != null ? _local.pen : null;

                    // O host do teste de dois editores pega a azul (tecla 2), para o cliente conferir
                    // a cor que chega pela rede; sozinho, a preta (tecla 1).
                    Key equipKey = _role == HostRole ? Key.Digit2 : Key.Digit1;

                    // Só até a primeira vez: depois, as teclas das canetas guardam a caneta de propósito.
                    if (held != null && !held.Equipped && Now - _phaseStart < 8.0 && _penKeyLog.Length == 0)
                    {
                        // Alterna apertado e solto a cada décimo de segundo, até a caneta estar na mão.
                        bool down = (int)((Now - _phaseStart) * 10.0) % 2 == 0;
                        InputSystem.QueueStateEvent(_keyboard, down ? new KeyboardState(equipKey) : new KeyboardState());
                        _penReady = Now;
                        break;
                    }

                    // Com a caneta na mão, a mão em primeira pessoa tem de estar na tela, com a caneta.
                    if (held != null && held.Equipped && held.hand != null && held.hand.gameObject.activeInHierarchy && held.hand.PenRenderer != null)
                        _handOk = true;

                    double t = Now - _penReady;

                    // A primeira tecla puxou a caneta da cor dela (a rede publica no quadro seguinte).
                    if (held != null && t >= 0.2 && _penKeyLog.Length == 0)
                    {
                        string first = PenState(held);
                        _penKeyLog = $"{(equipKey == Key.Digit1 ? 1 : 2)}:{first}";
                        _penKeysOk = first == (equipKey == Key.Digit1 ? "BLACK" : "BLUE");
                    }

                    if (t < 0.2)
                    {
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                    }
                    else if (t < 2.0)
                    {
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                        CheckHandAtWall(held);

                        // Um evento de mouse a cada 1/60 s; mais que isso giraria rápido demais.
                        if (Now - _lastMouseEvent > 1.0 / 60.0)
                        {
                            _lastMouseEvent = Now;
                            InputSystem.QueueStateEvent(_mouse, new MouseState { delta = new Vector2(4f, 0f) }.WithButton(MouseButton.Left));
                        }
                    }
                    else if (t < 3.5)
                    {
                        // Solta tudo e dá tempo de os riscos chegarem ao outro editor.
                        InputSystem.QueueStateEvent(_mouse, new MouseState());
                    }
                    else if (_role == Solo && !PenKeys(held))
                    {
                        // Sozinho: aperta as teclas das canetas, uma de cada vez (PenKeys).
                    }
                    else
                    {
                        Next();
                    }
                    break;

                case 5:
                    // Buracos: anda sobre uma faixa e depois se joga no vazio.
                    FindPlayers();
                    Observe();
                    if (!PitStage(session)) break;
                    Next();
                    break;

                case 6:
                    FindPlayers();
                    Observe();

                    if (_role == ClientRole)
                    {
                        // O cliente não pensa pela entidade: só precisa vê-la aparecer e andar.
                        EntityBrain shown = EntityBrain.Instance;
                        if (shown != null && !_entitySeen)
                        {
                            _entitySeen = true;
                            _entityFirst = shown.transform.position;
                        }

                        if (shown != null && Vector3.Distance(shown.transform.position, _entityFirst) > 1f) _sawEntityMove = true;

                        // Viu a entidade andar: agora sai da sala e faz barulho, para ser pego.
                        // É a captura de quem não é o host, avisada pela rede.
                        if (_sawEntityMove ? ClientBait(session) : Now - _phaseStart > 70.0) Evaluate(session);
                        break;
                    }

                    if (!EntityStage(session)) break;

                    if (_role == HostRole) Next();
                    else Evaluate(session);
                    break;

                case 7:
                    FindPlayers();

                    if (LateJoin)
                    {
                        // Variante: já riscou; agora espera o cliente entrar e receber tudo.
                        // Vale ter visto o cliente uma vez: ele sai assim que recebe os riscos.
                        if (_mostPlayers < 2) { _phaseStart = Now; break; }
                        if (Now - _phaseStart > 2.0) Evaluate(session);
                        break;
                    }

                    // O host fica até o cliente terminar a parte dele (ser pego pela entidade) e sair.
                    if ((_mostPlayers >= 2 && session.PlayerCount < 2) || Now - _phaseStart > 100.0) Evaluate(session);
                    break;
            }
        }

        static void Next()
        {
            _stage++;
            _phaseStart = Now;
        }

        // Teclas das canetas, depois de riscar com a preta: 2 troca para a azul, 3 para a
        // vermelha, 3 de novo guarda, 2 puxa a azul direto e 1 volta para a preta (o resto do
        // teste segue com ela na mão). Cada tecla fica 0,15 s apertada e 0,15 s solta; então
        // confere a cor da caneta, a tinta na mão e o que foi publicado na rede.
        static readonly Key[] PenKeyOrder = { Key.Digit2, Key.Digit3, Key.Digit3, Key.Digit2, Key.Digit1 };
        static readonly string[] PenKeyExpected = { "BLUE", "RED", "guardada", "BLUE", "BLACK" };

        // Conta por passadas e por tempo desde o começo de cada metade, não pelo relógio do
        // estágio: com a gravação desenhando a câmera a cada passada, uma passada pode levar
        // mais que 0,15 s e pular a metade apertada inteira.
        static bool PenKeys(PenTool pen)
        {
            const double Half = 0.15;
            const int MinTicks = 3;
            if (_penKeyStep >= PenKeyOrder.Length) return true;

            if (_penKeyTicks == 0) _penKeyHalfStart = Now;
            _penKeyTicks++;
            bool halfDone = _penKeyTicks > MinTicks && Now - _penKeyHalfStart >= Half;

            if (!_penKeyReleased)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(PenKeyOrder[_penKeyStep]));
                if (halfDone) { _penKeyReleased = true; _penKeyTicks = 0; }
                return false;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            if (!halfDone) return false;

            string state = PenState(pen);
            _penKeyLog += $" {PenKeyOrder[_penKeyStep] - Key.Digit1 + 1}:{state}";
            _penKeysOk &= state == PenKeyExpected[_penKeyStep];
            _penKeyStep++;
            _penKeyReleased = false;
            _penKeyTicks = 0;
            return _penKeyStep >= PenKeyOrder.Length;
        }

        // Riscando encostado na parede (o olho fica a ~0,33 m dela): nenhum vértice das peças da
        // mão em primeira pessoa passa do plano da superfície riscada, e a ponta da caneta fica
        // na tela, perto do ponto riscado.
        static void CheckHandAtWall(PenTool pen)
        {
            if (pen == null || !pen.IsDrawing || pen.hand == null || !pen.hand.isActiveAndEnabled || _local.playerCamera == null) return;
            if (!pen.TryAim(out RaycastHit hit)) return;

            _handSamples++;
            _handClosest = Mathf.Min(_handClosest, hit.distance);
            _handDeepest = Mathf.Max(_handDeepest, pen.hand.DepthPast(hit.point, hit.normal));

            Vector3 tip = pen.hand.TipPosition;
            if (hit.distance < 0.6f) _tipGap = Mathf.Max(_tipGap, Vector3.Distance(tip, hit.point));

            Vector3 view = _local.playerCamera.WorldToViewportPoint(tip);
            if (view.z < _local.playerCamera.nearClipPlane || view.x < 0f || view.x > 1f || view.y < 0f || view.y > 1f) _tipOffScreen++;
        }

        // A cor na mão do dono: o nome da tinta se caneta, mão e rede concordam; senão o que diverge.
        static string PenState(PenTool pen)
        {
            if (pen == null) return "sem_caneta";
            if (!pen.Equipped) return "guardada";

            DrawingBoard board = DrawingBoard.Instance;
            Material ink = board != null && board.inkMaterials != null && pen.ColorIndex < board.inkMaterials.Length
                ? board.inkMaterials[pen.ColorIndex] : null;
            bool hand = pen.hand != null && pen.hand.PenRenderer != null && pen.hand.PenRenderer.sharedMaterial == ink;
            bool net = _local != null && _local.PenColor == pen.ColorIndex;

            string name = pen.ColorName;
            if (!hand) name += "(mao=" + (pen.hand != null && pen.hand.PenRenderer != null ? pen.hand.PenRenderer.sharedMaterial.name : "nada") + ")";
            if (!net) name += "(rede=" + (_local != null ? _local.PenColor : -1) + ")";
            return name;
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

            // O outro jogador aparece como a figura de traje, e ela mostra o item que ele pegou.
            if (_remote.figure != null && _remote.body.activeInHierarchy && _remote.figure.GetComponentsInChildren<Renderer>().Length > 20) _sawRemoteFigure = true;
            if (_remote.figure != null && _remote.figure.Holding) _sawRemoteHolding = true;

            // O host pegou a caneta azul (tecla 2): a rede diz azul e o traje a segura com a tinta azul.
            DrawingBoard board = DrawingBoard.Instance;
            if (_remote.figure != null && _remote.figure.Holding && _remote.PenColor == 2 && board != null &&
                board.inkMaterials != null && board.inkMaterials.Length > 2 && _remote.figure.PenRenderer != null &&
                _remote.figure.PenRenderer.sharedMaterial == board.inkMaterials[2])
                _sawRemoteBlue = true;
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
            _mouse = InputSystem.AddDevice<Mouse>();

            // Sem janela o cursor não fica preso; avisa o jogo para agir como se estivesse.
            PlayerController.AssumeMouseCaptured = true;
        }

        // Gira a câmera para cima ou para baixo, um evento de mouse a cada 1/60 s.
        static void Look(float deltaY)
        {
            if (Mathf.Approximately(deltaY, 0f) || Now - _lastMouseEvent < 1.0 / 60.0) return;
            _lastMouseEvent = Now;
            InputSystem.QueueStateEvent(_mouse, new MouseState { delta = new Vector2(0f, deltaY) });
        }

        // A cada quadro, pergunta à caneta onde ela encostaria e confere que é no cenário.
        // Um raio comum, sem o filtro dela, acerta o colisor do próprio jogador quando ele
        // pula, corre ou recua (o colisor anda um passo de física atrás do corpo): era o
        // defeito de "riscar no ar". Esse número também é contado, para o teste provar que
        // passou pela situação.
        static void AuditPenRay()
        {
            PenTool pen = _local != null ? _local.pen : null;
            if (pen == null || pen.aim == null) return;

            _rayChecks++;

            if (Physics.Raycast(pen.aim.position, pen.aim.forward, out RaycastHit raw, pen.reach, ~0, QueryTriggerInteraction.Ignore) &&
                raw.collider.transform.root == _local.transform.root)
                _raySelfHits++;

            if (!pen.TryAim(out RaycastHit hit)) return;
            if (hit.collider.GetComponentInParent<LevelBuilder>() != null) return;

            _rayOffLevel++;
            if (_rayFirst == null)
                _rayFirst = $"{hit.collider.name} [{hit.collider.GetType().Name}] a {hit.distance:F2} m; câmera em {pen.aim.position}, ponto {hit.point}, jogador em {_local.transform.position}";
        }

        // Devolve verdadeiro quando terminou. Passos: (0) põe o jogador na beirada, de frente
        // para uma faixa; (1) segura W até o meio da célula do buraco; (2) solta o jogador
        // sobre o vazio; (3) espera ele cair e reaparecer na sala inicial.
        static bool PitStage(GameSession session)
        {
            LevelBuilder builder = session.level;
            LevelData data = builder != null ? builder.Data : null;

            if (data == null || data.Pits.Count == 0)
            {
                _pitNote = "mapa sem buracos";
                return true;
            }

            BeginWalking();
            _pitTested = true;

            PlayerController body = _local.controller;
            var capsule = _local.GetComponent<CharacterController>();
            Vector3 position = _local.transform.position;

            switch (_pitStep)
            {
                case 0:
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState());

                    if (!FindStripEntry(data, out int x, out int z, out WallSide side))
                    {
                        _pitNote = "nenhuma faixa chega à beirada";
                        _pitStep = 2;
                        _pitStepStart = Now;
                        break;
                    }

                    data.Neighbor(x, z, side, out int nx, out int nz);
                    Vector3 outside = builder.CellCenter(nx, nz);
                    _stripGoal = builder.CellCenter(x, z);
                    Vector3 look = _stripGoal - outside;

                    body.Teleport(outside + Vector3.up * 0.05f, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg);
                    _fallsBefore = body.Falls;
                    _pitStep = 1;
                    _pitStepStart = Now;
                    break;

                case 1:
                    if (Now - _pitStepStart < 0.4) break;

                    float left = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(_stripGoal.x, _stripGoal.z));
                    bool fell = body.Falls != _fallsBefore || position.y < -0.3f;

                    if (left < 0.3f || fell || Now - _pitStepStart > 6.0)
                    {
                        _stripOk = left < 0.3f && !fell && capsule.isGrounded && Mathf.Abs(position.y) < 0.15f;
                        if (!_stripOk) _pitNote = $"na faixa: faltavam {left:F2} m, altura {position.y:F2}, apoiado={capsule.isGrounded}, caiu={fell}";

                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                        _pitStep = 2;
                        _pitStepStart = Now;
                        break;
                    }

                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
                    break;

                case 2:
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                    if (Now - _pitStepStart < 0.5 || body.IsFalling) break;

                    body.Teleport(FindVoid(builder, data) + Vector3.up * 0.2f, 0f);
                    _fallsBefore = body.Falls;
                    _pitStep = 3;
                    _pitStepStart = Now;
                    break;

                case 3:
                    Vector3 home = builder.SpawnCenter;
                    float fromHome = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(home.x, home.z));
                    bool back = body.Falls == _fallsBefore + 1 && !body.IsFalling && fromHome < 2.5f && capsule.isGrounded;

                    // Dá um segundo parado na sala, para a avaliação pegar o jogador assentado.
                    if (!back) _phaseStart = Now;
                    if (back && Now - _phaseStart > 1.0)
                    {
                        _fallOk = true;
                        return true;
                    }

                    if (Now - _pitStepStart > 8.0)
                    {
                        _pitNote += $" na queda: quedas={body.Falls - _fallsBefore}, caindo={body.IsFalling}, a {fromHome:F1} m do início, altura {position.y:F2}";
                        return true;
                    }
                    break;
            }

            return false;
        }

        // Devolve verdadeiro quando terminou. Passos: (0) põe o jogador longe da sala inicial e
        // solta a entidade; (1) espera ela aparecer e confere onde; muda o jogador de lugar,
        // para a pista que ela recebeu ao nascer ficar velha; (2) o jogador faz barulho indo e
        // vindo, e o teste espera ela ouvir, vir atrás e pegá-lo.
        static bool EntityStage(GameSession session)
        {
            EntityDirector director = EntityDirector.Instance;
            LevelBuilder builder = session.level;
            LevelData data = builder != null ? builder.Data : null;

            if (director == null || director.entityPrefab == null || data == null || data.SafePathCells.Count < 20)
            {
                _entityNote = "cena sem entidade";
                _entityTested = true;
                return true;
            }

            BeginWalking();
            _entityTested = true;

            PlayerController body = _local.controller;
            var capsule = _local.GetComponent<CharacterController>();
            Vector3 position = _local.transform.position;
            EntityBrain entity = director.Entity;

            switch (_entityStep)
            {
                case 0:
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                    if (Now - _phaseStart < 1.0 || body.IsReturning) break;

                    _entityPost = PathPoint(builder, data, 14);
                    body.Teleport(_entityPost + Vector3.up * 0.05f, 0f);

                    director.minSeconds = 0f;
                    EntityDirector.Paused = false;
                    _entityStep = 1;
                    _entityStepStart = Now;
                    break;

                case 1:
                    if (entity == null)
                    {
                        if (Now - _entityStepStart < 10.0) break;

                        _entityNote = "não apareceu em 10 s: " + director.LastRefusal;
                        return true;
                    }

                    // Onde nasceu: mais perto da sala do que o jogador, a pelo menos 4 células dele.
                    var nav = new LevelNav(builder);
                    int[,] fromDoor = nav.NewMap();
                    int[,] fromEntity = nav.NewMap();
                    nav.DoorCell(out int doorX, out int doorZ);
                    nav.Distances(doorX, doorZ, fromDoor);
                    nav.WorldToCell(entity.transform.position, out int ex, out int ez);
                    nav.WorldToCell(_entityPost, out int px, out int pz);
                    nav.Distances(ex, ez, fromEntity);

                    _entitySpawnOk = fromDoor[ex, ez] >= 0 && fromDoor[ex, ez] < fromDoor[px, pz] &&
                                     fromEntity[px, pz] >= director.minCellsFromPlayers;
                    _entityNote = $"nasceu a {fromDoor[ex, ez]} células da porta e a {fromEntity[px, pz]} do jogador, que estava a {fromDoor[px, pz]}";

                    // O jogador muda de lugar sem fazer barulho: agora só o ouvido (ou a vista) o acha.
                    _entityPost = PathPoint(builder, data, 17);
                    body.Teleport(_entityPost + Vector3.up * 0.05f, 0f);
                    _entityStep = 2;
                    _entityStepStart = Now;
                    break;

                case 2:
                    if (entity == null)
                    {
                        _entityNote += "; sumiu";
                        return true;
                    }

                    if (entity.NoisesHeard > 0) _entityHeardOk = true;

                    bool hunting = entity.State == EntityState.Investigate || entity.State == EntityState.Chase;
                    if (hunting && Vector3.Distance(entity.Goal, position) < 3f) _entityTrackedOk = true;

                    if (body.Captures > 0)
                    {
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());

                        Vector3 home = builder.SpawnCenter;
                        float fromHome = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(home.x, home.z));
                        bool back = !body.IsReturning && fromHome < 2.5f && capsule.isGrounded;

                        if (!back) _phaseStart = Now;
                        if (back && Now - _phaseStart > 1.0)
                        {
                            _entityCatchOk = true;
                            _entityNote += $"; pegou em {Now - _entityStepStart:F1} s";
                            return true;
                        }
                    }
                    else
                    {
                        MakeNoise(position);
                    }

                    if (Now - _entityStepStart > 60.0)
                    {
                        float away = Vector3.Distance(entity.transform.position, position);
                        _entityNote += $"; em 60 s não pegou: estado {entity.State}, a {away:F1} m, ouviu {entity.NoisesHeard} vezes";
                        return true;
                    }
                    break;
            }

            return false;
        }

        // Acompanha as fases da energia do andar e conta as luminárias acesas em cada uma.
        static void WatchLights()
        {
            LightsOut lightsOut = LightsOut.Instance;
            if (lightsOut == null || CeilingLight.All.Count == 0) return;

            int lit = 0;
            foreach (CeilingLight light in CeilingLight.All) if (light.IsLit) lit++;

            switch (lightsOut.Phase)
            {
                case PowerPhase.Warning:
                    _sawWarning = true;
                    break;

                case PowerPhase.Hurry:
                    _sawHurry = true;
                    break;

                case PowerPhase.Dark:
                    // O valor da fase muda num quadro e as luminárias no mesmo: conta o pior caso do apagão inteiro.
                    _sawDark = true;
                    _litInDark = Mathf.Max(_litInDark, lit);
                    break;

                default:
                    if (!_sawDark) _litBeforeDark = Mathf.Max(_litBeforeDark, lit);
                    else if (lit > 0) _sawLightBack = true;
                    break;
            }
        }

        // Grava 3 s de vídeo numa pasta do projeto e confere o arquivo.
        static void RecordStage()
        {
            ScreenRecorder recorder = ScreenRecorder.Instance;
            if (_recordStep >= 2) return;

            if (recorder == null)
            {
                _recordNote = "cena sem gravador";
                _recordStep = 2;
                return;
            }

            if (_recordStep == 0)
            {
                ScreenRecorder.FolderOverride = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Logs", "Gravacoes", _role));
                _recordStep = recorder.Begin() ? 1 : 2;
                _recordStart = Now;
                if (_recordStep == 2) _recordNote = "não começou: " + recorder.LastError;
                return;
            }

            // Sem janela (linha de comando) o Unity não desenha a câmera do jogador sozinho, e
            // é ao desenhar que a filmadora entrega a imagem ao gravador. Aqui o teste manda
            // desenhar, numa textura, a cada passada.
            if (Application.isBatchMode && _local.playerCamera != null)
            {
                if (_recordTarget == null) _recordTarget = new RenderTexture(1280, 720, 24);
                _local.playerCamera.targetTexture = _recordTarget;
                _local.playerCamera.Render();

                // O primeiro desenho pode levar muitos segundos compilando shaders; os 3 s
                // de gravação contam a partir dele.
                if (!_recordWarm)
                {
                    _recordWarm = true;
                    _recordStart = Now;
                }
            }

            if (Now - _recordStart < 3.0) return;

            recorder.Stop();
            _recordStep = 2;

            if (_recordTarget != null)
            {
                _local.playerCamera.targetTexture = null;
                Object.Destroy(_recordTarget);
                _recordTarget = null;
            }

            long bytes = System.IO.File.Exists(recorder.LastFile) ? new System.IO.FileInfo(recorder.LastFile).Length : 0;
            _recordOk = recorder.LastFrames >= 20 && bytes > 100000 && recorder.LastError.Length == 0;
            _recordNote = $"{recorder.LastFrames} quadros, {bytes / 1024} KB" + (recorder.LastError.Length > 0 ? ", erro: " + recorder.LastError : "");
        }

        // Vai e vem em torno do posto: corre para a frente, anda para trás.
        static void MakeNoise(Vector3 position)
        {
            float offset = position.z - _entityPost.z;
            if (offset > 0.9f) _entityForward = false;
            else if (offset < -0.9f) _entityForward = true;

            InputSystem.QueueStateEvent(_keyboard, _entityForward ? new KeyboardState(Key.W, Key.LeftShift) : new KeyboardState(Key.S));
        }

        // Cliente: sai da sala inicial, faz barulho e espera a entidade (que pensa no host)
        // vir pegá-lo. Devolve verdadeiro quando terminou.
        static bool ClientBait(GameSession session)
        {
            LevelBuilder builder = session.level;
            LevelData data = builder != null ? builder.Data : null;
            if (data == null || _local == null) return true;

            BeginWalking();
            PlayerController body = _local.controller;
            var capsule = _local.GetComponent<CharacterController>();
            Vector3 position = _local.transform.position;

            if (_baitStep == 0)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                if (body.IsReturning) return false;

                _entityPost = PathPoint(builder, data, 14);
                body.Teleport(_entityPost + Vector3.up * 0.05f, 0f);
                _baitStep = 1;
                _baitStart = Now;
                return false;
            }

            if (body.Captures > 0)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());

                Vector3 home = builder.SpawnCenter;
                float fromHome = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(home.x, home.z));
                bool back = !body.IsReturning && fromHome < 2.5f && capsule.isGrounded;

                if (!back) _phaseStart = Now;
                if (!back || Now - _phaseStart < 1.0) return false;

                _clientCaughtOk = true;
                _baitNote = $"pego em {Now - _baitStart:F1} s";
                return true;
            }

            MakeNoise(position);

            if (Now - _baitStart < 75.0) return false;

            EntityBrain entity = EntityBrain.Instance;
            _baitNote = entity != null
                ? $"em 75 s não foi pego: entidade em estado {entity.State}, a {Vector3.Distance(entity.transform.position, position):F1} m"
                : "em 75 s não foi pego: entidade sumiu";
            return true;
        }

        // Centro de uma célula do caminho seguro, contada a partir do início.
        static Vector3 PathPoint(LevelBuilder builder, LevelData data, int index)
        {
            int cell = data.SafePathCells[Mathf.Min(index, data.SafePathCells.Count - 3)];
            return builder.CellCenter(cell % data.Width, cell / data.Width);
        }

        // Uma célula de buraco cuja faixa chega até a beirada, e o lado por onde ela sai.
        static bool FindStripEntry(LevelData data, out int x, out int z, out WallSide side)
        {
            for (z = 0; z < data.Height; z++)
                for (x = 0; x < data.Width; x++)
                    foreach (WallSide candidate in new[] { WallSide.South, WallSide.North, WallSide.West, WallSide.East })
                    {
                        if (!data.Pit[x, z] || !data.HasStrip(x, z, candidate)) continue;
                        if (!data.Neighbor(x, z, candidate, out int nx, out int nz) || data.Pit[nx, nz]) continue;

                        side = candidate;
                        return true;
                    }

            x = z = 0;
            side = WallSide.South;
            return false;
        }

        // Um ponto de buraco sem nada embaixo: de preferência o centro de uma célula sem
        // faixa; senão um canto de célula, longe da faixa que passa pelo meio.
        static Vector3 FindVoid(LevelBuilder builder, LevelData data)
        {
            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++)
                    if (data.Pit[x, z] && data.PitStrips[x, z] == 0) return builder.CellCenter(x, z);

            PitArea area = data.Pits[0];
            return builder.CellCenter(area.X0, area.Z0) + new Vector3(1.2f, 0f, 1.2f);
        }

        static int BoardPoints()
        {
            return DrawingBoard.Instance != null ? DrawingBoard.Instance.PointCount : 0;
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
            // O teste dos buracos termina com o jogador de volta ao início: vale o mais longe que foi.
            float moved = Mathf.Max(_movedMost, Vector3.Distance(_start, _local.transform.position));
            float y = _local.transform.position.y;
            float stepPeak = MeasureStepPeak(_local);
            int players = _mostPlayers;

            bool grounded = controller != null && controller.isGrounded && Mathf.Abs(y) < 0.2f;
            bool built = session.LevelReady && builder != null && builder.Data != null && lights > 0;
            bool ok = Errors.Count == 0 && grounded && built && stepPeak > 0.5f;

            string report = $"papel={_role} erros={Errors.Count} apoiado={grounded} luminarias={lights} marcas={marks} " +
                            $"seed={session.Seed} mapa={LevelHash(builder):X8} jogadores_vistos={players}";

            int strokes = DrawingBoard.Instance != null ? DrawingBoard.Instance.StrokeCount : 0;
            int inkPoints = BoardPoints();
            PenTool pen = _local != null ? _local.pen : null;
            report += $" caneta_na_mao={(pen != null && pen.Equipped)}";
            if (pen == null) report += " (jogador sem caneta)";
            else if (!pen.Equipped) report += $" (ligada={pen.isActiveAndEnabled} na_rede={pen.IsSpawned} dono={pen.IsOwner})";

            // Uma tecla por caneta (1 preta, 2 azul, 3 vermelha; a mesma tecla guarda): a cor
            // na caneta, na mão em primeira pessoa e na rede, depois de cada tecla.
            if (_role != ClientRole)
            {
                bool colors = _penKeysOk && (_role != Solo || _penKeyStep >= PenKeyOrder.Length);
                ok &= colors;
                report += $" cores_da_caneta={colors} [{_penKeyLog}]";
            }

            ok &= _rayOffLevel == 0;
            report += $" miras_da_caneta={_rayChecks} fora_do_cenario={_rayOffLevel} raio_comum_no_proprio_jogador={_raySelfHits}";
            if (_rayFirst != null) report += $" primeiro=[{_rayFirst}]";

            // Mapa: buracos, os dois caminhos e as setas.
            LevelData level = builder != null ? builder.Data : null;
            if (level != null)
            {
                int arrows = 0, pitCells = 0;
                foreach (Decoration mark in level.Decorations) if (mark.Kind == DecorationKind.Arrow) arrows++;
                foreach (bool pit in level.Pit) if (pit) pitCells++;

                ok &= level.Pits.Count > 0 && level.SafePathCells.Count > 0 && level.PathCells.Count > 0;
                ok &= arrows > 0 && arrows <= LevelGenerator.MaxArrows;
                report += $" tamanho={level.Width}x{level.Height} buracos={level.Pits.Count} celulas_de_buraco={pitCells} " +
                          $"caminho_direto={(level.PathCells.Count - 1) * 4}m caminho_seguro={(level.SafePathCells.Count - 1) * 4}m setas={arrows}";
            }

            // Apagão. Quem entrou depois fica poucos segundos e pode não ver o ciclo inteiro.
            if (!(_role == ClientRole && LateJoin))
            {
                ok &= _sawWarning && _sawHurry && _sawDark && _sawLightBack && _litBeforeDark > 0 && _litInDark == 0;
                report += $" apagao: aviso={_sawWarning} aviso_rapido={_sawHurry} escuro={_sawDark} voltou={_sawLightBack} " +
                          $"acesas_antes={_litBeforeDark} acesas_no_escuro={_litInDark}";
            }

            if (_recordStep >= 2)
            {
                ok &= _recordOk;
                report += $" gravacao={_recordOk} [{_recordNote}]";
            }

            if (_role != ClientRole)
            {
                ok &= _handOk;
                report += $" mao_com_caneta={_handOk}";

                // A mão não atravessa a parede riscando de perto (tolerância de 1 cm).
                bool wallOk = _handSamples > 0 && _handClosest < 0.6f && _handDeepest <= 0.01f && _tipOffScreen == 0;
                ok &= wallOk;
                report += $" mao_na_parede={wallOk} [amostras={_handSamples} parede_mais_perto={_handClosest:F2}m " +
                          $"peca_mais_funda={_handDeepest * 100f:+0.0;-0.0}cm ponta_longe_do_risco={_tipGap * 100f:F1}cm ponta_fora_da_tela={_tipOffScreen}]";
            }

            if (_role == ClientRole && !LateJoin)
            {
                ok &= _sawRemoteFigure && _sawRemoteHolding && _sawRemoteBlue;
                report += $" traje_do_host={_sawRemoteFigure} viu_o_host_com_a_caneta={_sawRemoteHolding} caneta_do_host_azul={_sawRemoteBlue}";
            }

            if (_entityTested)
            {
                ok &= _entitySpawnOk && _entityHeardOk && _entityTrackedOk && _entityCatchOk;
                report += $" entidade_nasce_atras={_entitySpawnOk} ouviu={_entityHeardOk} foi_atras={_entityTrackedOk} " +
                          $"pegou_e_devolveu={_entityCatchOk} capturas={_local.controller.Captures} [{_entityNote}]";
            }

            if (_role == ClientRole && !LateJoin)
            {
                ok &= _sawEntityMove && _clientCaughtOk;
                report += $" viu_a_entidade={_entitySeen} viu_ela_andar={_sawEntityMove} foi_pego_e_voltou={_clientCaughtOk} capturas={_local.controller.Captures} [{_baitNote}]";
            }

            if (_pitTested)
            {
                ok &= _stripOk && _fallOk;
                report += $" faixa_segura={_stripOk} queda_volta_ao_inicio={_fallOk} quedas={_local.controller.Falls}";
                if (_pitNote.Length > 0) report += $" [{_pitNote.Trim()}]";
            }

            if (_role == ClientRole)
            {
                // O cliente não anda nem risca; precisa ter visto o outro jogador se mexer
                // e ter recebido os riscos dele.
                ok &= players >= 2 && (LateJoin || _remoteMoved > 1.0f) && inkPoints >= 10;
                report += $" host_andou={_remoteMoved:F2}m lanterna_do_host_vista={_sawRemoteFlashlight} riscos_recebidos={strokes} pontos={inkPoints}";
            }
            else
            {
                ok &= moved > 0.5f && _steps > 0 && inkPoints >= 5;
                report += $" andou={moved:F2}m passos={_steps} pico_do_clipe_de_passo={stepPeak:F2} riscos={strokes} pontos={inkPoints}";
                if (_role == HostRole) ok &= players >= 2;
            }

            // Censo do que é sólido durante a partida, por objeto. Ajuda a achar um colisor
            // que não devia existir (algo invisível em que a caneta consiga riscar, por exemplo).
            var census = new SortedDictionary<string, int>();
            foreach (Collider collider in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Transform parent = collider.transform.parent;
                string key = (parent != null ? parent.name + "/" : "") + collider.name + " [" + collider.GetType().Name +
                             (collider.isTrigger ? ", gatilho" : "") +
                             (collider.enabled && collider.gameObject.activeInHierarchy ? "" : ", desligado") + "]";
                census[key] = census.TryGetValue(key, out int count) ? count + 1 : 1;
            }

            if (pen != null && pen.aim != null && controller != null)
            {
                // Caso extremo: o jogador recua vários palmos num único quadro e a caneta mira
                // em seguida. O colisor dele ficou para trás, bem na frente da câmera.
                foreach (float step in new[] { 0.1f, 0.3f, 0.6f })
                {
                    controller.Move(-_local.transform.forward * step);
                    bool any = pen.TryAim(out RaycastHit probe);
                    bool self = any && probe.collider.transform.root == _local.transform.root;
                    ok &= !self;
                    report += $" recuo_{step * 100f:F0}cm={(any ? probe.collider.name : "nada")}";
                }
            }

            var lines = new List<string>();
            foreach (KeyValuePair<string, int> entry in census) lines.Add(entry.Value + " x " + entry.Key);
            Debug.Log("[BackRooms] Colisores em jogo: " + string.Join(" | ", lines));

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
            foreach (bool pit in data.Pit) Mix(pit ? 1 : 0);
            foreach (byte strips in data.PitStrips) Mix(strips);
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
            SessionState.SetBool(LateKey, false);
            EntityDirector.Paused = false;
            ScreenRecorder.FolderOverride = null;
            if (ScreenRecorder.Instance != null) ScreenRecorder.Instance.Stop();

            if (_local != null && _local.controller != null) _local.controller.Stepped -= OnStepped;

            if (_mouse != null) InputSystem.RemoveDevice(_mouse);
            PlayerController.AssumeMouseCaptured = false;

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
