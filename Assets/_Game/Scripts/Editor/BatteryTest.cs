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
    /// Teste em Play da lanterna com bateria e das pilhas no chão (<see cref="BatteryPickups"/>,
    /// <c>P0.3</c>): a bateria não é infinita, dura 4 min com carga cheia (o valor que o jogador do
    /// prefab tem de fato), cai com a lanterna acesa e o painel a mostra em porcentagem; há pilhas longe umas das
    /// outras; mirando uma, o aviso diz o que fazer; E pega, a pilha some e a lanterna volta cheia;
    /// a mesma pilha não se pega duas vezes.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.BatteryTest.BatchRun (Level 0). Sai com
    /// 0 se passou, 3 se falhou. Linha "[BackRooms] Teste das pilhas".
    /// </summary>
    public static class BatteryTest
    {
        const string ActiveKey = "BackRooms.BatteryTest.Active";
        const string BatchKey = "BackRooms.BatteryTest.Batch";
        const string ExitCodeKey = "BackRooms.BatteryTest.ExitCode";
        const string Level0Scene = "Assets/_Game/Scenes/Level0_Test.unity";
        const double Timeout = 90.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks;
        static double _began;
        static float _stageStart, _before;
        static bool _finished;
        static PlayerNetwork _local;
        static BatteryPickups _pickups;

        [MenuItem("BackRooms/Testar pilhas da lanterna")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false);
        }

        public static void BatchRun() { Run(true); }

        static void Run(bool batch)
        {
            EditorSceneManager.OpenScene(Level0Scene, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
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
            if (EditorApplication.timeSinceStartup - _began > Timeout) { Finish(false, $"tempo esgotado na etapa {_stage}"); return; }
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
                    _local = PlayerNetwork.Local;
                    _pickups = BatteryPickups.Instance;
                    if (_local == null || InStage < 1.5f) break;
                    if (_pickups == null || _pickups.Count < 4) { Finish(false, $"{(_pickups == null ? 0 : _pickups.Count)} pilhas na fase (eram para ser 4 ou mais)"); return; }
                    Flashlight torch = _local.flashlight;
                    if (torch.infiniteBattery) { Finish(false, "a lanterna continua com bateria infinita"); return; }
                    // O valor efetivo (o do prefab do jogador), não só o padrão do código.
                    if (Mathf.Abs(torch.batterySeconds - Flashlight.DefaultBatterySeconds) > 0.5f)
                    { Finish(false, $"a lanterna do jogador dura {torch.batterySeconds:F0} s (devia ser {Flashlight.DefaultBatterySeconds:F0}; recrie o Level 0)"); return; }
                    torch.SetOn(true);
                    _before = torch.Battery01;
                    float minutes = torch.batterySeconds / 60f;
                    Next($"{_pickups.Count} pilhas no mapa; carga cheia {minutes:F0} min, luz total na fase pegando todas: {(1 + _pickups.Count) * minutes:F0} min");
                    break;
                }

                case 2:
                {
                    if (InStage < 3f) break;
                    Flashlight torch = _local.flashlight;
                    float spent = _before - torch.Battery01;
                    if (spent <= 0f) { Finish(false, "com a lanterna acesa a bateria não caiu"); return; }
                    // O painel mostra a porcentagem (arredondada para cima), não barras.
                    string shown = BackRooms.UI.Camcorder.Instance != null ? BackRooms.UI.Camcorder.Instance.BatteryText : null;
                    int expected = BackRooms.UI.Camcorder.BatteryPercent(torch.Battery01);
                    if (shown != expected + "%" && shown != (expected + 1) + "%")
                    { Finish(false, $"com {torch.Battery01:P1} de bateria o painel mostra \"{shown}\" (devia ser {expected}%)"); return; }
                    // Gasta mais da metade de uma vez (o teste não espera 5 min), para a recarga aparecer.
                    torch.Recharge(-torch.batterySeconds * 0.6f);
                    _before = torch.Battery01;
                    GoTo(0);
                    Next($"acesa, a bateria caiu {spent * torch.batterySeconds:F1} s em 3 s; o painel mostrou {shown}");
                    break;
                }

                case 3:
                    Aim(0);
                    if (_stageTicks < 3) break;
                    if (BatteryPickups.Prompt != "E - BATTERIES") { if (InStage > 2f) Finish(false, $"mirando a pilha, o aviso foi \"{BatteryPickups.Prompt}\""); break; }
                    BatteryPickups.PressForTest = true;
                    Next("mirando a pilha, o aviso é E - BATTERIES");
                    break;

                case 4:
                {
                    if ((_pickups.Taken & 1UL) == 0) { if (InStage > 2f) Finish(false, "apertou E na pilha e ela não sumiu"); break; }
                    Flashlight torch = _local.flashlight;
                    if (_pickups.LocalPicked != 1) { Finish(false, $"{_pickups.LocalPicked} pilhas contadas para o jogador"); return; }
                    if (torch.Battery01 < 0.99f) { Finish(false, $"pegou a pilha e a bateria ficou em {torch.Battery01:P0} (estava em {_before:P0})"); return; }
                    if (_pickups.transform.Find("Pilhas_0").gameObject.activeSelf) { Finish(false, "a pilha pega continuou no chão"); return; }
                    if (_stageTicks < 4) break;
                    string full = BackRooms.UI.Camcorder.Instance != null ? BackRooms.UI.Camcorder.Instance.BatteryText : null;
                    if (full != "100%" && full != "99%") { Finish(false, $"recarregada, o painel mostra \"{full}\""); return; }
                    // Mira o lugar de novo: nada a pegar.
                    Aim(0);
                    Next($"pegou a pilha: sumiu e a bateria foi de {_before:P0} a {torch.Battery01:P0}");
                    break;
                }

                case 5:
                    Aim(0);
                    if (_stageTicks < 5) break;
                    if (BatteryPickups.Prompt != null) { Finish(false, $"a pilha já pega ainda mostra \"{BatteryPickups.Prompt}\""); return; }
                    Finish(true, string.Join("; ", Notes) + "; a mesma pilha não se pega de novo");
                    break;
            }
        }

        static void GoTo(int index)
        {
            Vector3 at = _pickups.PickupPosition(index);
            Vector3 center = at;
            // De pé a 0,9 m da pilha, do lado de fora da parede.
            Vector3 away = Vector3.zero;
            foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                if (!Physics.Raycast(at + Vector3.up * 0.5f, dir, 1.2f, ~0, QueryTriggerInteraction.Ignore)) { away = dir; break; }
            Vector3 stand = center + away * 0.9f;
            _local.controller.Teleport(new Vector3(stand.x, at.y + 0.05f, stand.z), 0f);
            Aim(index);
        }

        static void Aim(int index)
        {
            Vector3 target = _pickups.PickupPosition(index) + Vector3.up * 0.02f;
            Transform eye = _local.playerCamera.transform;
            Vector3 to = target - eye.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude < 0.0001f) return;
            _local.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 0f);
            _local.controller.SetPitch(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
        }

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;
            BatteryPickups.PressForTest = false;
            EntityDirector.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }
            Debug.Log("[BackRooms] Teste das pilhas " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
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
