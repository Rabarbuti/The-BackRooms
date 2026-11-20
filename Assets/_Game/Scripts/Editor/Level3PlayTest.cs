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
    /// Teste em Play do Escritório (Scenes/Level3_Test.unity): hospeda, confere que o jogador nasce
    /// dentro da cabine do elevador com a porta fechada e que ela abre sozinha; anda o caminho de
    /// corredor até a entrada da escada, desce a escada até a porta da saída (os passos têm de ser
    /// os de piso duro), espera a onda de luzes apagar um trecho de corredor e abre a porta: é a
    /// última fase, então ela tem de ser dada como zerada e a sessão acabar no menu.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level3PlayTest.BatchRun (sai com 0
    /// se passou, 3 se falhou). Menu: BackRooms > Testar Level 3 (Play). Resultado na linha
    /// "[BackRooms] Teste do Level 3 PASSOU/FALHOU" do log. Restaura o progresso salvo.
    /// </summary>
    public static class Level3PlayTest
    {
        const string ActiveKey = "BackRooms.Level3PlayTest.Active";
        const string BatchKey = "BackRooms.Level3PlayTest.Batch";
        const string ExitCodeKey = "BackRooms.Level3PlayTest.ExitCode";
        const string SavedProgressKey = "BackRooms.Level3PlayTest.Progress";
        const double Timeout = 240.0;
        const float WalkSpeed = 3.5f;
        const float Reach = 0.35f;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage;
        static double _began, _stageStart, _lastTick, _lastProgress;
        static PlayerNetwork _local;
        static CharacterController _body;
        static LevelBuilder _level;
        static List<Vector3> _route;
        static int _target;
        static float _best;
        static bool _hardSteps;

        [MenuItem("BackRooms/Testar Level 3 (Play)")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false);
        }

        public static void BatchRun() => Run(true);

        static void Run(bool batch)
        {
            if (!System.IO.File.Exists(Level3Setup.ScenePath)) Level3Setup.BuildScene();
            EditorSceneManager.OpenScene(Level3Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetInt(SavedProgressKey, PlayerPrefs.GetInt(LevelProgress.ClearedKey, 0));
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
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = _lastTick = Now;
            float dt = Mathf.Clamp((float)(Now - _lastTick), 0f, 0.1f);
            _lastTick = Now;

            if (Now - _began > Timeout)
            {
                Finish(false, $"tempo esgotado na etapa {_stage} (ponto {_target} de {(_route != null ? _route.Count : 0)})");
                return;
            }

            switch (_stage)
            {
                case 0:
                    LevelProgress.DevModeOverride = true;
                    // A entidade e o alarme têm testes próprios (fumaça e ObjectiveTest.BatchRunLevel3).
                    EntityDirector.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                {
                    if (session.Phase != SessionPhase.Playing) break;
                    _local = PlayerNetwork.Local;
                    _level = Object.FindAnyObjectByType<LevelBuilder>();
                    if (_local == null || _level == null || _level.Data == null) break;
                    if (Now - _stageStart < 1.0) break;

                    LevelData d = _level.Data;
                    if (!d.HasOffice) { Finish(false, "a fase montada não é o Escritório"); return; }
                    Vector3 local = _level.transform.InverseTransformPoint(_local.transform.position);
                    int cx = Mathf.FloorToInt(local.x / LevelBuilder.Cell), cz = Mathf.FloorToInt(local.z / LevelBuilder.Cell);
                    if (cx != d.SpawnX || cz != d.SpawnZ) { Finish(false, $"o jogador nasceu na célula ({cx},{cz}), fora da cabine ({d.SpawnX},{d.SpawnZ})"); return; }
                    Notes.Add("nasceu dentro da cabine do elevador");

                    _body = _local.GetComponent<CharacterController>();
                    _route = new List<Vector3>();
                    foreach (int cell in d.SafePathCells) _route.Add(_level.CellCenter(cell % d.Width, cell / d.Width));
                    _route.AddRange(_level.OfficeExitRoute);
                    if (_route.Count < 6) { Finish(false, "caminho até a saída curto demais"); return; }
                    Next();
                    break;
                }

                case 2:
                {
                    // A porta da cabine abre sozinha pouco depois do início.
                    ElevatorDoors cab = null;
                    if (OfficeElevators.Instance != null)
                        foreach (ElevatorDoors door in OfficeElevators.Instance.Doors)
                            if (door.IsCab) cab = door;
                    if (cab == null) { Finish(false, "a cabine não tem porta"); return; }
                    if (cab.Openness < 0.99f)
                    {
                        if (StageTimedOut(15)) Finish(false, $"a porta da cabine não abriu (abertura {cab.Openness:F2}, relógio {session.SessionSeconds:F1} s)");
                        return;
                    }
                    Notes.Add($"a porta da cabine abriu aos {session.SessionSeconds:F1} s de partida");
                    _target = 0;
                    _best = float.MaxValue;
                    _lastProgress = Now;
                    Next();
                    break;
                }

                case 3:
                    Walk(dt);
                    break;

                case 4:
                {
                    // De frente para a porta: espera a onda de luzes passar por algum corredor.
                    OfficeLightWave wave = OfficeLightWave.Instance;
                    if (wave == null || wave.RunCount == 0) { Finish(false, "a fase não tem onda de luzes"); return; }
                    if (wave.DarkCount == 0)
                    {
                        if (session.SessionSeconds > OfficeLightWave.FirstWave + 20.0) Finish(false, $"a onda de luzes não apagou nada até {session.SessionSeconds:F0} s");
                        return;
                    }
                    Notes.Add($"a onda de luzes apagou {wave.DarkCount} luminária(s) aos {session.SessionSeconds:F0} s");
                    // Trancada pelo alarme até ele disparar.
                    if (SwitchObjective.Instance == null || !LevelExit.Locked) { Finish(false, "a porta corta-fogo não começou trancada pelo alarme"); return; }
                    SwitchObjective.Instance.CompleteForTest();
                    FaceDoor();
                    Next();
                    break;
                }

                case 5:
                {
                    LevelExit exit = LevelExit.Instance;
                    if (exit == null || !exit.LocalInReach)
                    {
                        if (StageTimedOut(8)) Finish(false, "na frente da porta da saída ela não ficou ao alcance");
                        return;
                    }
                    if (!exit.Interact()) { Finish(false, "a porta da saída não aceitou a interação"); return; }
                    Next();
                    break;
                }

                case 6:
                    // Última fase: zera e a sessão acaba no menu.
                    if (!LevelProgress.IsCleared(3) || session.Phase == SessionPhase.Playing)
                    {
                        if (StageTimedOut(15)) Finish(false, $"abriu a porta e o Level 3 não terminou zerado no menu (zerada={LevelProgress.IsCleared(3)}, fase={session.Phase})");
                        return;
                    }
                    Notes.Add("a porta zerou o Level 3 (última fase) e a sessão acabou no menu");
                    // A tela de fim da fita, com o tempo da fase gravado.
                    if (!BackRooms.UI.ConnectionMenu.EndingShown) { Finish(false, "acabou a última fase e a tela de fim da fita não apareceu"); return; }
                    if (LevelProgress.LastTime(3) <= 0f) { Finish(false, "o tempo do Level 3 não foi gravado"); return; }
                    Notes.Add($"a tela de fim da fita apareceu (Level 3 em {LevelProgress.FormatTime(LevelProgress.LastTime(3))})");
                    Finish(true, string.Join("; ", Notes));
                    break;
            }
        }

        // Anda até cada ponto do caminho: centros das células e, depois, a escada.
        static void Walk(float dt)
        {
            Vector3 position = _local.transform.position;
            Vector3 goal = _route[_target];
            Vector3 toGoal = goal - position;
            toGoal.y = 0f;
            float distance = toGoal.magnitude;

            var steps = _local.GetComponent<Footsteps>();
            if (steps != null && steps.Surface == StepSurface.Tile) _hardSteps = true;

            if (distance < Reach)
            {
                _target++;
                _best = float.MaxValue;
                _lastProgress = Now;
                if (_target >= _route.Count)
                {
                    if (!_hardSteps) { Finish(false, "os passos não foram os de piso duro no Escritório"); return; }
                    float drop = _route[_route.Count - 1].y - position.y;
                    if (Mathf.Abs(drop) > 0.3f) { Finish(false, $"chegou na frente da porta {drop:F2} m fora da altura do patamar"); return; }
                    Notes.Add($"andou {_route.Count} pontos da cabine até a porta da saída, descendo a escada, com passos de piso duro");
                    FaceDoor();
                    Next();
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
                Finish(false, $"travou indo para o ponto {_target} de {_route.Count} ({goal:F1}), a {distance:F2} m; jogador em {position:F1}");
                return;
            }

            _body.Move(toGoal.normalized * Mathf.Min(WalkSpeed * dt, distance));
        }

        static void FaceDoor()
        {
            LevelExit exit = LevelExit.Instance;
            if (exit == null) return;
            Vector3 foot = exit.DoorPosition;
            Vector3 inward = exit.transform.position - foot;
            inward.y = 0f;
            inward.Normalize();
            Vector3 spot = new Vector3(foot.x, foot.y + 0.05f, foot.z) + inward * 1.1f;
            PlayerNetwork.Local.controller.Teleport(spot, Quaternion.LookRotation(-inward, Vector3.up).eulerAngles.y);
        }

        static void Finish(bool ok, string report)
        {
            EntityDirector.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
            LevelProgress.DevModeOverride = null;
            PlayerPrefs.SetInt(LevelProgress.ClearedKey, SessionState.GetInt(SavedProgressKey, 0));
            PlayerPrefs.Save();

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log("[BackRooms] Teste do Level 3 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

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
