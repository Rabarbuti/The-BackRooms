using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Teste em Play do Level 1 de teste (Scenes/Level1_Test.unity): hospeda, confere que o
    /// jogador nasce na sala inicial e que a mobília foi montada, anda o caminho seguro inteiro
    /// da sala inicial até a saída, célula por célula, e no meio do caminho anda contra um
    /// móvel para conferir que não o atravessa. Falha se travar, se atravessar um móvel ou se
    /// aparecer erro no console.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level1PlayTest.BatchRun (sai com 0
    /// se passou, 3 se falhou). Menu: BackRooms > Testar Level 1 (Play). O resultado é a linha
    /// "[BackRooms] Teste do Level 1 PASSOU/FALHOU" do log.
    /// </summary>
    public static class Level1PlayTest
    {
        const string ActiveKey = "BackRooms.Level1PlayTest.Active";
        const string BatchKey = "BackRooms.Level1PlayTest.Batch";
        // Código de saída guardado enquanto o editor sai do Play (-1 = nenhum pendente).
        const string ExitCodeKey = "BackRooms.Level1PlayTest.ExitCode";
        const double Timeout = 180.0;
        const float WalkSpeed = 3.5f;
        const float Reach = 0.35f;

        static readonly List<string> Errors = new List<string>();
        static int _stage;
        static double _began, _stageStart, _lastTick, _lastProgress;
        static PlayerNetwork _local;
        static CharacterController _body;
        static LevelBuilder _level;
        static List<int> _path;
        static int _target;
        static float _best;
        static float _walked;
        static Vector3 _lastPosition;
        static Collider _bumpTarget;
        static string _bumpNote = "não houve móvel no caminho";
        static Vector3 _bumpReturn;
        static bool _bumpStopped;
        static CollisionFlags _bumpFlags;

        [MenuItem("BackRooms/Testar Level 1 (Play)")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false);
        }

        public static void BatchRun()
        {
            Run(true);
        }

        static void Run(bool batch)
        {
            EditorSceneManager.OpenScene(Level1Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            EditorApplication.EnterPlaymode();
        }

        // Entrar em Play recarrega o domínio; o estado sobrevive pelo SessionState.
        [InitializeOnLoadMethod]
        static void Hook()
        {
            // Se sair do Play recarregar o domínio, o pedido de fechar o editor continua valendo.
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

        static double Now => EditorApplication.timeSinceStartup;

        static void Next()
        {
            _stage++;
            _stageStart = Now;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = _lastTick = Now;
            float dt = Mathf.Clamp((float)(Now - _lastTick), 0f, 0.1f);
            _lastTick = Now;

            if (Now - _began > Timeout)
            {
                Finish(false, $"tempo esgotado na etapa {_stage} (célula {_target} de {(_path != null ? _path.Count : 0)} do caminho)");
                return;
            }

            switch (_stage)
            {
                case 0:
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                {
                    if (session.Phase != SessionPhase.Playing) break;
                    foreach (PlayerNetwork player in Object.FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None))
                        if (player.IsSpawned && player.IsOwner) _local = player;
                    _level = Object.FindFirstObjectByType<LevelBuilder>();
                    if (_local == null || _level == null || _level.Data == null) break;
                    if (Now - _stageStart < 1.5) break; // deixa assentar no chão

                    _body = _local.GetComponent<CharacterController>();
                    var props = _level.GetComponent<PropLayer>();
                    if (props == null || props.Count == 0) { Finish(false, "a fase não tem móveis em Play"); return; }

                    LevelData d = _level.Data;
                    Vector3 local = _level.transform.InverseTransformPoint(_local.transform.position);
                    int cx = Mathf.FloorToInt(local.x / LevelBuilder.Cell), cz = Mathf.FloorToInt(local.z / LevelBuilder.Cell);
                    if (!d.InRoom(cx, cz)) { Finish(false, $"o jogador nasceu fora da sala inicial, na célula ({cx},{cz})"); return; }

                    _path = new List<int>(d.SafePathCells);
                    if (_path.Count < 2) { Finish(false, "caminho até a saída vazio"); return; }

                    _target = 0;
                    _best = float.MaxValue;
                    _lastProgress = Now;
                    _lastPosition = _local.transform.position;
                    Debug.Log($"[BackRooms] Level 1 em Play: {props.Count} móveis, caminho de {_path.Count} células até a saída.");
                    Next();
                    break;
                }

                case 2:
                    Walk(dt);
                    break;

                case 3:
                    Bump(dt);
                    break;
            }
        }

        // Anda até o centro de cada célula do caminho seguro, uma por vez.
        static void Walk(float dt)
        {
            Vector3 position = _local.transform.position;
            _walked += Vector3.Distance(Flat(position), Flat(_lastPosition));
            _lastPosition = position;

            int cell = _path[_target];
            Vector3 goal = _level.CellCenter(cell % _level.Data.Width, cell / _level.Data.Width);
            Vector3 toGoal = Flat(goal - position);
            float distance = toGoal.magnitude;

            if (distance < Reach)
            {
                // No meio do caminho, a primeira célula com um móvel vira o teste de esbarrão.
                if (_bumpTarget == null && _target > 2 && TryPickBump(cell))
                {
                    _bumpReturn = goal;
                    Next();
                    return;
                }

                _target++;
                _best = float.MaxValue;
                _lastProgress = Now;
                if (_target >= _path.Count)
                {
                    Finish(true, $"andou {_walked:F0} m, {_path.Count} células, da sala inicial até a saída sem travar; {_bumpNote}");
                }
                return;
            }

            if (distance < _best - 0.05f)
            {
                _best = distance;
                _lastProgress = Now;
            }
            else if (Now - _lastProgress > 3.0)
            {
                Finish(false, $"travou indo para a célula ({cell % _level.Data.Width},{cell / _level.Data.Width}), a {distance:F2} m do centro; perto de: {NearestProp(position)}");
                return;
            }

            _body.Move(toGoal.normalized * Mathf.Min(WalkSpeed * dt, distance));
        }

        // Escolhe o móvel com colisor mais perto do centro desta célula que esteja à vista: o
        // primeiro colisor no caminho reto até ele tem de ser ele mesmo.
        static bool TryPickBump(int cell)
        {
            Transform root = _level.transform.Find("Moveis");
            if (root == null) return false;

            int w = _level.Data.Width;
            Vector3 center = _level.CellCenter(cell % w, cell / w);
            float best = 2.6f;
            foreach (Transform item in root)
            {
                var box = item.GetComponent<BoxCollider>();
                if (box == null) continue;
                float d = Vector3.Distance(Flat(box.bounds.center), Flat(center));
                if (d >= best || box.bounds.size.y <= 0.4f || box.bounds.min.y >= 0.5f) continue;

                Vector3 eye = new Vector3(center.x, 0.5f, center.z);
                Vector3 aim = new Vector3(box.bounds.center.x, 0.5f, box.bounds.center.z) - eye;
                if (!Physics.Raycast(eye, aim.normalized, out RaycastHit hit, aim.magnitude + 1f) || hit.collider != box) continue;

                {
                    best = d;
                    _bumpTarget = box;
                }
            }
            return _bumpTarget != null;
        }

        // Anda contra o móvel até parar de avançar e confere que o corpo do jogador não entrou
        // nele; depois volta ao centro da célula e segue o caminho. Conta por avanço, não por
        // tempo: em modo batch o editor chama o teste poucas vezes por segundo.
        static void Bump(float dt)
        {
            Vector3 position = _local.transform.position;
            if (!_bumpStopped)
            {
                // Um movimento só, do tamanho da distância até o meio do móvel: o CharacterController
                // varre o caminho e para no contato. Medido no mesmo instante, sem depender de quadros.
                Vector3 toward = Flat(_bumpTarget.bounds.center - position);
                var spy = _local.gameObject.GetComponent<HitSpy>() ?? _local.gameObject.AddComponent<HitSpy>();
                HitSpy.Hits.Clear();
                HitSpy.Colliders.Clear();
                _bumpFlags = _body.Move(toward);
                _bumpStopped = true;
                position = _local.transform.position;
            }

            // O corpo do jogador é uma cápsula de 0,3 m de raio e 1,8 m. Atravessado num móvel, até
            // uma cápsula bem fina (0,22 m) no lugar dele toca o móvel.
            Transform root = _level.transform.Find("Moveis");
            Vector3 low = position + Vector3.up * 0.3f, high = position + Vector3.up * 1.5f;
            Collider inside = FirstProp(Physics.OverlapCapsule(low, high, 0.22f), root);
            if (inside != null)
            {
                Finish(false, $"o jogador atravessou {inside.name} (andando contra {_bumpTarget.name})");
                return;
            }

            // Não pode ter chegado ao meio do móvel nem passado dele.
            float leftToCenter = Vector3.Distance(Flat(position), Flat(_bumpTarget.bounds.center));
            float startToCenter = Vector3.Distance(Flat(_bumpReturn), Flat(_bumpTarget.bounds.center));
            if (leftToCenter < 0.15f || leftToCenter > startToCenter)
            {
                Finish(false, $"andando contra {_bumpTarget.name} o jogador chegou ao meio dele ou passou ({leftToCenter:F2} m do centro)");
                return;
            }

            // Encostou: durante o movimento o corpo do jogador bateu num móvel (qualquer um da pilha).
            Collider touching = null;
            foreach (Collider hit in HitSpy.Colliders)
                if (hit != null && hit.transform.IsChildOf(root)) { touching = hit; break; }
            if (touching == null)
            {
                Vector3 closest = _bumpTarget.ClosestPoint(position + Vector3.up * 0.9f);
                Finish(false, $"andou contra {_bumpTarget.name} até parar e não encostou em móvel nenhum: jogador em {position:F2}, " +
                              $"móvel em {_bumpTarget.bounds.center:F2} (tamanho {_bumpTarget.bounds.size:F2}), ponto mais perto dele a " +
                              $"{Vector3.Distance(closest, position + Vector3.up * 0.9f):F2} m do peito; mais perto: {NearestProp(position)}; " +
                              $"saiu de {_bumpReturn:F2}; colisão do movimento {_bumpFlags}, bateu em: {string.Join(" | ", HitSpy.Hits)}; " +
                              $"na frente dele: {Blocker(position)}; {Probe(position)}");
                return;
            }
            _bumpNote = $"andou contra {_bumpTarget.name}, parou ao bater em {touching.name} a {touching.ClosestPoint(position + Vector3.up * 0.9f).y:F2} m de altura e não atravessou";

            Vector3 back = Flat(_bumpReturn - position);
            if (back.magnitude > Reach && Now - _stageStart < 20.0)
            {
                _body.Move(back.normalized * Mathf.Min(WalkSpeed * dt, back.magnitude));
                return;
            }

            _lastPosition = _local.transform.position;
            _lastProgress = Now;
            _best = float.MaxValue;
            _stage = 2;
        }

        // O primeiro colisor (fora o próprio jogador) que uma cápsula do tamanho dele encontra
        // andando em direção ao alvo do esbarrão.
        static string Blocker(Vector3 position)
        {
            Vector3 direction = Flat(_bumpTarget.bounds.center - position).normalized;
            RaycastHit[] hits = Physics.CapsuleCastAll(position + Vector3.up * 0.3f, position + Vector3.up * 1.5f, 0.3f, direction, 2f,
                                                       Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            string best = "nada";
            float nearest = float.MaxValue;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.root == _local.transform.root) continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    Transform t = hit.collider.transform;
                    best = $"{t.name} (pai {(t.parent != null ? t.parent.name : "-")}, {hit.collider.GetType().Name}, gatilho={hit.collider.isTrigger}) a {hit.distance:F2} m";
                }
            }
            return best;
        }

        // Mede o corpo do jogador e dá um passo de 0,25 m direto contra o móvel.
        static string Probe(Vector3 position)
        {
            Vector3 direction = Flat(_bumpTarget.bounds.center - position).normalized;
            CollisionFlags flags = _body.Move(direction * 0.25f);
            float moved = Vector3.Distance(Flat(_local.transform.position), Flat(position));
            return $"corpo: raio {_body.radius:F2}, pele {_body.skinWidth:F2}, centro {_body.center:F2}, altura {_body.height:F2}, " +
                   $"escala {_local.transform.lossyScale:F2}; passo de 0,25 m andou {moved:F2} m, colisão {flags}";
        }

        static Collider FirstProp(Collider[] hits, Transform root)
        {
            foreach (Collider hit in hits)
                if (root != null && hit.transform.IsChildOf(root)) return hit;
            return null;
        }

        static string NearestProp(Vector3 position)
        {
            Transform root = _level.transform.Find("Moveis");
            if (root == null) return "nenhum móvel";
            string name = "nenhum móvel";
            float best = 3f;
            foreach (Transform item in root)
            {
                var box = item.GetComponent<BoxCollider>();
                if (box == null) continue;
                float d = Vector3.Distance(box.ClosestPoint(position), position);
                if (d < best) { best = d; name = $"{item.name} a {d:F2} m"; }
            }
            return name;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        static void Finish(bool ok, string report)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log("[BackRooms] Teste do Level 1 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

            if (SessionState.GetBool(BatchKey, false))
            {
                // Linha de comando: sai do Play, reabre o Level 0 e só então fecha o editor. Em
                // Play não dá para trocar de cena, e fechando direto o editor reabriria no Level 1.
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

    /// <summary>Anota em que o CharacterController bateu (só para o diagnóstico do teste).</summary>
    public sealed class HitSpy : MonoBehaviour
    {
        public static readonly List<string> Hits = new List<string>();
        public static readonly List<Collider> Colliders = new List<Collider>();

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Colliders.Add(hit.collider);
            Transform t = hit.collider.transform;
            Hits.Add($"{t.name} (pai {(t.parent != null ? t.parent.name : "-")}) normal {hit.normal:F2} ponto {hit.point:F2} empurrou {hit.moveLength:F2}");
        }
    }
}
