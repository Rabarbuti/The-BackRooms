using System.Collections.Generic;
using BackRooms.Entities;
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
    /// Teste em Play da maré e do ralo das Piscinas (<see cref="PoolTide"/>, level2-piscinas.md,
    /// seções 4.3 e 5), com o relógio da maré forçado (<see cref="PoolTide.ForceSeconds"/>):
    ///
    /// - mede a rota do objetivo (início, as duas válvulas, a saída) e o caminho a pé até a saída,
    ///   em metros e segundos, para comparar com a hora da primeira maré;
    /// - parada: a água na altura de sempre, sem alarme nem aviso no painel; correndo no seco, a
    ///   velocidade de referência;
    /// - o aviso (10 s antes): o alarme liga (sirene tocando, lâmpadas vermelhas acesas), a buzina
    ///   toca e o painel diz que a maré vem;
    /// - subindo: alarme ligado, ronco de água correndo, superfície agitada, painel "subindo";
    /// - maré alta: o alarme para; o seco fica com água acima do joelho (vadeia-se, não nada), a
    ///   marca molhada sobe com a água, os passos são de água e, tentando correr, não se corre e a
    ///   velocidade cai a menos da metade; no raso a passagem a pé fecha: de pé, sem tecla, nada-se e
    ///   a cabeça fica debaixo d'água, e segurando Espaço sobe e respira à tona;
    /// - na vazante, quem nada sobre um poço é puxado até a grade e se afoga em poucos segundos (o
    ///   painel diz que o ralo puxa); com a maré parada, o mesmo lugar só afunda devagar, e a marca
    ///   molhada continua nas paredes; na água funda longe dos poços o ralo não puxa;
    /// - as válvulas abertas no meio de uma subida: a água para de subir e desce na hora, o alarme
    ///   cala e, na hora do aviso seguinte, não liga mais.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level2TideTest.BatchRun (-seed N; padrão
    /// 1998). Sai com 0 se passou, 3 se falhou. Linha "[BackRooms] Teste da maré".
    /// </summary>
    public static class Level2TideTest
    {
        const string ActiveKey = "BackRooms.Level2TideTest.Active";
        const string BatchKey = "BackRooms.Level2TideTest.Batch";
        const string ExitCodeKey = "BackRooms.Level2TideTest.ExitCode";
        const string SeedKey = "BackRooms.Level2TideTest.Seed";
        const double Timeout = 200.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks, _drownings, _warnings;
        static double _began;
        static float _stageStart;
        static bool _finished;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static Vector2Int _dry, _shallow, _well, _farDeep, _run;
        static WallSide _runSide;
        static bool _hasFar, _sprinted;
        static float _restSpeed, _drainFrom, _drainPeak;
        static Vector3 _measureFrom;
        static Keyboard _keyboard;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;

        [MenuItem("BackRooms/Testar maré do Level 2")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false);
        }

        public static void BatchRun() { Run(true); }

        static void Run(bool batch)
        {
            int seed = 1998;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-seed" && int.TryParse(args[i + 1], out int s)) seed = s;
            EditorSceneManager.OpenScene(Level2Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetInt(SeedKey, seed);
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

        static double Still => PoolTide.FirstRise - 25.0;
        static double Warning => PoolTide.FirstRise - PoolTide.WarningSeconds * 0.5;
        static double Rising => PoolTide.FirstRise + PoolTide.RiseSeconds * 0.5;
        static double High => PoolTide.FirstPeak + 2.0;
        static double Ebb => PoolTide.FirstEbb + 4.0;

        // Correndo (W + Shift) do meio da célula de corrida: mede a velocidade entre 0,4 e 1,2 s.
        const float RunWarmup = 0.4f, RunEnd = 1.2f;

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;
            if (_began <= 0.0) _began = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - _began > Timeout) { Finish(false, $"tempo esgotado na etapa {_stage}"); return; }
            _stageTicks++;
            PlayerController body = _local != null ? _local.controller : null;
            PoolWater water = PoolWater.Current;
            PoolTide tide = PoolTide.Current;

            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    PoolTide.Paused = false;
                    PoolTide.ForceSeconds = Still;
                    session.fixedSeed = SessionState.GetInt(SeedKey, 1998);
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                {
                    if (session.Phase != SessionPhase.Playing) break;
                    _local = PlayerNetwork.Local;
                    _level = session.level;
                    if (_local == null || _level == null || _level.Data == null || water == null) break;
                    if (InStage < 1.5f) break;
                    if (tide == null) { Finish(false, "a água das Piscinas não tem maré (PoolTide)"); return; }
                    if (!FindCells()) return;
                    float rest = water.SurfaceY;
                    if (Mathf.Abs(rest - LevelData.WaterLevel - _level.transform.position.y) > 0.01f) { Finish(false, $"com a maré parada a água está em {rest:F2} m (era {LevelData.WaterLevel})"); return; }
                    if (tide.AlarmOn || PoolTide.HudText != null) { Finish(false, $"com a maré parada o alarme está ligado ({tide.AlarmOn}) ou o painel avisa ({PoolTide.HudText})"); return; }
                    PoolAlarm alarm = _level.GetComponentInChildren<PoolAlarm>(true);
                    if (alarm == null || alarm.LampCount == 0) { Finish(false, "a fase não tem lâmpadas de alarme"); return; }
                    Notes.Add($"parada, água em {rest:F2} m; seco {_dry}, raso {_shallow}, poço {_well}" + (_hasFar ? $", funda longe {_farDeep}" : "") + $"; {alarm.LampCount} lâmpadas de alarme");
                    Notes.Add(RouteNote());
                    Notes.Add(DrainReachNote(tide));

                    StartKeyboard();
                    StartRun(body);
                    Next();
                    break;
                }

                case 2:
                {
                    // Correndo no seco com a água parada: a velocidade de referência.
                    float speed = MeasureRun(body);
                    if (speed < 0f) break;
                    _restSpeed = speed;
                    if (!_sprinted) { Finish(false, $"no seco, com a água parada, não correu ({speed:F2} m/s)"); return; }
                    _warnings = tide.WarningsPlayed;
                    PoolTide.ForceSeconds = Warning;
                    Next($"correndo no seco com a água parada: {speed:F2} m/s");
                    break;
                }

                case 3:
                {
                    if (InStage < 0.6f) break;
                    PoolAlarm alarm = _level.GetComponentInChildren<PoolAlarm>(true);
                    if (tide.Phase != TidePhase.Warning) { Finish(false, $"aos {Warning:F0} s a maré está em {tide.Phase} (devia ser o aviso)"); return; }
                    if (!tide.AlarmOn || !alarm.On) { Finish(false, "no aviso o alarme não ligou"); return; }
                    if (tide.WarningsPlayed <= _warnings) { Finish(false, "no aviso a buzina não tocou"); return; }
                    if (tide.SirenVolume < 0.2f) { Finish(false, $"no aviso a sirene não toca (volume {tide.SirenVolume:F2})"); return; }
                    if (PoolTide.HudText != PoolTide.TextComing) { Finish(false, $"no aviso o painel diz '{PoolTide.HudText}' (devia ser {PoolTide.TextComing})"); return; }
                    PoolTide.ForceSeconds = Rising;
                    Next($"aviso aos {Warning:F0} s: alarme ligado ({alarm.LampCount} lâmpadas, sirene {tide.SirenVolume:F2}), buzina tocou, painel '{PoolTide.TextComing}'");
                    break;
                }

                case 4:
                {
                    if (InStage < 1.5f) break;
                    if (tide.Phase != TidePhase.Rising || !tide.AlarmOn) { Finish(false, $"subindo: fase {tide.Phase}, alarme {tide.AlarmOn}"); return; }
                    if (PoolTide.HudText != PoolTide.TextRising) { Finish(false, $"subindo, o painel diz '{PoolTide.HudText}'"); return; }
                    if (tide.FlowVolume < 0.2f) { Finish(false, $"subindo, o ronco da água não toca (volume {tide.FlowVolume:F2})"); return; }
                    if (water.Agitation < 0.5f) { Finish(false, $"subindo, a superfície não se agita ({water.Agitation:F2})"); return; }
                    if (Mathf.Abs(water.WetTop - water.Offset) > 0.05f) { Finish(false, $"subindo, a marca molhada ({water.WetTop:F2} m) não acompanha a água ({water.Offset:F2} m)"); return; }
                    // Na filmadora o aviso pisca: espera um quadro aceso (até 3 s).
                    var camcorder = Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();
                    bool shown = camcorder != null && camcorder.TideText == PoolTide.TextRising;
                    if (!shown && InStage < 3f) break;
                    if (camcorder != null && !shown) { Finish(false, "subindo, a filmadora não mostrou o aviso da maré em 3 s"); return; }
                    PoolTide.ForceSeconds = High;
                    Stand(_dry);
                    Next($"subindo: +{water.Offset:F2} m, ronco {tide.FlowVolume:F2}, agitação {water.Agitation:F2}, painel '{PoolTide.TextRising}'" + (shown ? " (na filmadora)" : " (sem filmadora na cena)"));
                    break;
                }

                case 5:
                {
                    if (InStage < 2.5f) break;
                    float rise = water.Offset;
                    if (Mathf.Abs(rise - PoolTide.Amplitude) > 0.02f) { Finish(false, $"na maré alta a água subiu {rise:F2} m (eram {PoolTide.Amplitude})"); return; }
                    if (tide.AlarmOn) { Finish(false, "na maré alta (parada) o alarme continua ligado"); return; }
                    if (PoolTide.HudText != PoolTide.TextHigh) { Finish(false, $"na maré alta o painel diz '{PoolTide.HudText}'"); return; }
                    if (body.Submersion < 0.75f || body.IsSwimming) { Finish(false, $"na maré alta, de pé no seco: {body.Submersion:F2} m de água, nadando={body.IsSwimming}"); return; }
                    if (water.WetTop < PoolTide.Amplitude - 0.03f) { Finish(false, $"na maré alta a marca molhada ficou em {water.WetTop:F2} m"); return; }
                    Notes.Add($"maré alta: +{rise:F2} m, alarme desligado, painel '{PoolTide.TextHigh}'; no seco, {body.Submersion:F2} m de água sem nadar");
                    StartRun(body);
                    Next();
                    break;
                }

                case 6:
                {
                    float speed = MeasureRun(body);
                    if (speed < 0f) break;
                    if (_sprinted) { Finish(false, $"no seco alagado ({body.Submersion:F2} m de água) ainda se corre"); return; }
                    if (speed > _restSpeed * 0.5f) { Finish(false, $"no seco alagado anda a {speed:F2} m/s (correndo no seco: {_restSpeed:F2}); devia cair a menos da metade"); return; }
                    Footsteps steps = _local.footsteps;
                    if (steps != null && steps.Surface != StepSurface.Wade) { Finish(false, $"no seco alagado os passos são de {steps.Surface} (deviam ser de água)"); return; }
                    Stand(_shallow);
                    Next($"no seco alagado, tentando correr: {speed:F2} m/s ({speed / _restSpeed * 100f:F0}% de correr no seco), sem correr, passos de água");
                    break;
                }

                case 7:
                    // Na maré alta o raso fecha a passagem a pé: de pé no fundo, a cabeça fica debaixo
                    // d'água (até 21/11/2026, com 1,0 m de maré, ficava 20 cm fora e andava-se pelo fundo).
                    if (InStage < 4f) break;
                    if (body.IsReturning) { Finish(false, "na maré alta, no raso, o jogador se afogou em 4 s"); return; }
                    if (!body.IsSwimming || !body.IsUnderwater) { Finish(false, $"na maré alta, de pé no raso ({body.Submersion:F2} m de água), nadando={body.IsSwimming}, cabeça debaixo d'água={body.IsUnderwater} (devia ter de nadar)"); return; }
                    Notes.Add($"no raso, sem tecla, {body.Submersion:F2} m de água: nada e a cabeça fica debaixo d'água");
                    Press(Key.Space);
                    Next();
                    break;

                case 8:
                    // Segurando Espaço, sobe e respira à tona.
                    if (body.IsReturning) { Finish(false, "na maré alta, no raso, segurando Espaço, o jogador se afogou"); return; }
                    if (body.IsUnderwater) { if (InStage > 4f) Finish(false, $"na maré alta, no raso, segurando Espaço a cabeça não saiu da água em 4 s (pés a {body.transform.position.y:F2} m)"); break; }
                    Notes.Add($"segurando Espaço, à tona em {InStage:F1} s");
                    Press();
                    // A vazante: o ralo puxa.
                    PoolTide.ForceSeconds = Ebb;
                    _drownings = body.Drownings;
                    Float(_well);
                    Next();
                    break;

                case 9:
                {
                    PoolTide.ForceSeconds = Ebb + Mathf.Min(InStage, 8f);
                    if (!tide.Pulling) { Finish(false, "na vazante o ralo não está puxando"); return; }
                    if (tide.AlarmOn) { Finish(false, "na vazante o alarme está ligado"); return; }
                    if (tide.LocalPull > 0f && PoolTide.HudText != PoolTide.TextPull) { Finish(false, $"puxado pelo ralo, o painel diz '{PoolTide.HudText}'"); return; }
                    if (body.Drownings <= _drownings)
                    {
                        if (InStage > 8f) Finish(false, $"na vazante, sobre o poço, não foi puxado até a grade em 8 s (pés a {body.transform.position.y:F2} m, puxão {tide.LocalPull:F2})");
                        break;
                    }
                    float took = InStage;
                    PoolTide.ForceSeconds = Still;
                    Next($"na vazante, sobre o poço, foi puxado até a grade (painel '{PoolTide.TextPull}') e se afogou em {took:F1} s");
                    break;
                }

                case 10:
                    // Esperando voltar ao início antes de testar de novo.
                    if (body.IsReturning || InStage < 2f) break;
                    if (Mathf.Abs(water.Offset) > 0.01f) { Finish(false, $"com a maré parada de novo a água está em +{water.Offset:F2} m"); return; }
                    if (water.WetTop < 0.3f) { Finish(false, $"logo depois da maré a marca molhada já sumiu ({water.WetTop:F2} m)"); return; }
                    Notes.Add($"água baixa de novo: a marca molhada continua até +{water.WetTop:F2} m (seca a {water.dryRate:F2} m/s)");
                    _drownings = body.Drownings;
                    Float(_well);
                    Next();
                    break;

                case 11:
                    if (body.Drownings > _drownings) { Finish(false, $"com a maré parada, sobre o poço, se afogou em {InStage:F1} s (só devia afundar devagar)"); return; }
                    if (tide.LocalPull > 0f) { Finish(false, "com a maré parada o ralo puxou"); return; }
                    if (InStage < 6f) break;
                    Notes.Add("com a maré parada, sobre o poço, só afundou devagar");
                    if (_hasFar)
                    {
                        PoolTide.ForceSeconds = Ebb;
                        Float(_farDeep);
                    }
                    Next();
                    break;

                case 12:
                    if (_hasFar)
                    {
                        if (tide.LocalPull > 0f) { Finish(false, $"na vazante, na funda longe dos poços ({_farDeep}), o ralo puxou"); return; }
                        if (InStage < 3f) break;
                        Notes.Add("na vazante, na funda longe dos poços, não puxou");
                    }
                    else Notes.Add("sem funda longe dos poços nesta seed");
                    if (SwitchObjective.Instance == null || SwitchObjective.Instance.Kind != SwitchKind.Valve) { Finish(false, "a fase não tem o objetivo das válvulas"); return; }
                    Stand(_dry);
                    // Válvulas abertas no meio de uma subida: um segundo antes, a maré subindo com alarme.
                    PoolTide.ForceSeconds = Rising - 1.0;
                    PoolTide.ForceDrainStart = Rising;
                    SwitchObjective.Instance.CompleteForTest();
                    Next();
                    break;

                case 13:
                    if (InStage < 0.8f) break;
                    if (tide.Phase != TidePhase.Rising || !tide.AlarmOn) { Finish(false, $"um segundo antes de abrir as válvulas: fase {tide.Phase}, alarme {tide.AlarmOn}"); return; }
                    PoolTide.ForceSeconds = Rising;
                    Next();
                    break;

                case 14:
                {
                    PoolTide.ForceSeconds = Rising + InStage;
                    // Os primeiros quadros ainda podem trazer a altura de antes: a medida começa aos 0,2 s.
                    if (InStage < 0.2f) { _drainFrom = _drainPeak = water.Offset; break; }
                    _drainPeak = Mathf.Max(_drainPeak, water.Offset);
                    if (InStage > 0.3f && (tide.AlarmOn || tide.Phase != TidePhase.Draining)) { Finish(false, $"abertas as válvulas no meio da subida, o alarme continuou ({tide.AlarmOn}, fase {tide.Phase})"); return; }
                    if (InStage < 3f) break;
                    float drop = _drainFrom - water.Offset;
                    if (_drainPeak > _drainFrom + 0.01f) { Finish(false, $"abertas as válvulas no meio da subida, a água ainda subiu {_drainPeak - _drainFrom:F2} m"); return; }
                    if (drop < 0.3f) { Finish(false, $"abertas as válvulas no meio da subida, a água desceu só {drop:F2} m em 3 s"); return; }
                    Notes.Add($"válvulas abertas no meio da subida (+{_drainFrom:F2} m): a água parou de subir e desceu {drop:F2} m em 3 s, alarme calado, painel '{PoolTide.HudText}'");
                    // A próxima hora de aviso depois disso: drenada, o alarme não liga mais.
                    double at = Rising + 5.0;
                    for (int i = 0; i < 400 && PoolTide.PhaseAt(at) != TidePhase.Warning; i++) at += 0.5;
                    PoolTide.ForceSeconds = at;
                    Next();
                    break;
                }

                case 15:
                    if (InStage < 1f) break;
                    if (tide.AlarmOn || tide.SirenVolume > 0f) { Finish(false, $"drenada a fase, na hora do aviso o alarme ligou (sirene {tide.SirenVolume:F2})"); return; }
                    if (tide.Phase != TidePhase.Draining) { Finish(false, $"drenada a fase, a maré está em {tide.Phase}"); return; }
                    Finish(true, string.Join("; ", Notes) + $"; drenada, na hora do aviso o alarme não liga (água em {water.Offset:F2} m)");
                    break;
            }
        }

        // ---------------------------------------------------------------- Corrida

        static void StartKeyboard()
        {
            _backgroundBefore = InputSystem.settings.backgroundBehavior;
            _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        static void Press(params Key[] keys)
        {
            if (_keyboard != null) InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        }

        static void ReleaseKeyboard()
        {
            if (_keyboard == null) return;
            InputSystem.RemoveDevice(_keyboard);
            _keyboard = null;
            InputSystem.settings.backgroundBehavior = _backgroundBefore;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBefore;
        }

        static float Yaw(WallSide side)
        {
            switch (side)
            {
                case WallSide.North: return 0f;
                case WallSide.East: return 90f;
                case WallSide.South: return 180f;
                default: return 270f;
            }
        }

        // Do meio da célula de corrida, de frente para as duas células secas seguintes, W + Shift.
        static void StartRun(PlayerController body)
        {
            Vector3 at = _level.CellCenter(_run.x, _run.y);
            body.Teleport(at + Vector3.up * 0.05f, Yaw(_runSide));
            _sprinted = false;
            Press(Key.W, Key.LeftShift);
            _stageStart = Now;
        }

        // Velocidade horizontal média entre RunWarmup e RunEnd; negativo enquanto mede.
        static float MeasureRun(PlayerController body)
        {
            if (InStage < RunWarmup) { _measureFrom = body.transform.position; return -1f; }
            if (body.IsSprinting) _sprinted = true;
            if (InStage < RunEnd) return -1f;
            Press();
            Vector3 moved = body.transform.position - _measureFrom;
            moved.y = 0f;
            return moved.magnitude / (RunEnd - RunWarmup);
        }

        // ---------------------------------------------------------------- Células e rota

        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

        static bool FindCells()
        {
            LevelData d = _level.Data;
            var wells = new List<Vector2Int>();
            bool dry = false, shallow = false, run = false;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.IsSolid(x, z) || d.InRoom(x, z)) continue;
                    PoolDepth depth = d.Depth[x, z];
                    if (depth == PoolDepth.Well) wells.Add(new Vector2Int(x, z));
                    if (depth == PoolDepth.Dry && !dry && Flat(d, x, z)) { _dry = new Vector2Int(x, z); dry = true; }
                    if (depth == PoolDepth.Shallow && !shallow && Flat(d, x, z)) { _shallow = new Vector2Int(x, z); shallow = true; }
                    // Corrida: três células secas em linha, sem escada.
                    if (depth == PoolDepth.Dry && !run && Flat(d, x, z))
                        foreach (WallSide side in Sides)
                        {
                            if (!d.CanStep(x, z, side) || !d.Neighbor(x, z, side, out int ax, out int az) || d.Depth[ax, az] != PoolDepth.Dry || !Flat(d, ax, az)) continue;
                            if (!d.CanStep(ax, az, side) || !d.Neighbor(ax, az, side, out int bx, out int bz) || d.Depth[bx, bz] != PoolDepth.Dry) continue;
                            _run = new Vector2Int(x, z);
                            _runSide = side;
                            run = true;
                            break;
                        }
                }
            if (!dry || !shallow || wells.Count == 0 || !run) { Finish(false, $"a seed não tem seco ({dry}), raso ({shallow}), poço ({wells.Count}) ou três secos em linha ({run})"); return false; }
            _well = wells[0];
            _hasFar = false;
            for (int z = 0; z < d.Height && !_hasFar; z++)
                for (int x = 0; x < d.Width && !_hasFar; x++)
                {
                    if (d.IsSolid(x, z) || d.Depth[x, z] != PoolDepth.Deep) continue;
                    bool far = true;
                    foreach (Vector2Int w in wells) if (Mathf.Abs(w.x - x) + Mathf.Abs(w.y - z) < 5) far = false;
                    if (far) { _farDeep = new Vector2Int(x, z); _hasFar = true; }
                }
            return true;
        }

        // Distâncias a pé (seco e raso, sem nadar), em células, a partir de uma célula.
        static int[,] WalkDistances(LevelData d, Vector2Int from)
        {
            var dist = new int[d.Width, d.Height];
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++) dist[x, z] = -1;
            var queue = new Queue<Vector2Int>();
            dist[from.x, from.y] = 0;
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                foreach (WallSide side in Sides)
                {
                    if (!d.CanStep(c.x, c.y, side) || !d.Neighbor(c.x, c.y, side, out int nx, out int nz)) continue;
                    if (d.Depth[nx, nz] != PoolDepth.Dry && d.Depth[nx, nz] != PoolDepth.Shallow) continue;
                    if (dist[nx, nz] >= 0) continue;
                    dist[nx, nz] = dist[c.x, c.y] + 1;
                    queue.Enqueue(new Vector2Int(nx, nz));
                }
            }
            return dist;
        }

        // A célula a pé mais perto de um ponto do mundo (a válvula fica na parede).
        static Vector2Int WalkCellNear(LevelData d, Vector3 world)
        {
            Vector2Int best = new Vector2Int(d.SpawnX, d.SpawnZ);
            float bestDistance = float.MaxValue;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.IsSolid(x, z) || (d.Depth[x, z] != PoolDepth.Dry && d.Depth[x, z] != PoolDepth.Shallow)) continue;
                    Vector3 c = _level.CellCenter(x, z);
                    float distance = new Vector2(c.x - world.x, c.z - world.z).sqrMagnitude;
                    if (distance < bestDistance) { bestDistance = distance; best = new Vector2Int(x, z); }
                }
            return best;
        }

        /// <summary>
        /// A rota que o jogador sozinho faz: do início às duas válvulas (na melhor ordem) e delas à
        /// saída, a pé, em metros (centro a centro de célula) e em segundos andando (2,8 m/s) e
        /// correndo (5 m/s), mais os 6 s de cada válvula; e só o caminho até a saída. Para comparar
        /// com a hora da primeira maré.
        /// </summary>
        static string RouteNote()
        {
            LevelData d = _level.Data;
            var spawn = new Vector2Int(d.SpawnX, d.SpawnZ);
            var exit = new Vector2Int(d.ExitX, d.ExitZ);
            int[,] fromSpawn = WalkDistances(d, spawn);
            int straight = fromSpawn[exit.x, exit.y];
            SwitchObjective objective = SwitchObjective.Instance;
            string valves = "sem válvulas";
            if (objective != null && objective.Count >= 2)
            {
                // A célula da frente da válvula (a 1 m da parede, do lado de quem gira: o que se alcança a pé).
                Vector2Int Front(int i)
                {
                    Vector3 p = objective.PointPosition(i), f = objective.PointFacing(i);
                    Vector2Int one = WalkCellNear(d, p + f), other = WalkCellNear(d, p - f);
                    return fromSpawn[one.x, one.y] >= 0 ? one : other;
                }
                Vector2Int a = Front(0), b = Front(1);
                int[,] fromA = WalkDistances(d, a), fromB = WalkDistances(d, b);
                int ab = fromA[b.x, b.y];
                if (ab < 0 || fromSpawn[a.x, a.y] < 0 || fromSpawn[b.x, b.y] < 0 || fromA[exit.x, exit.y] < 0)
                    return $"válvulas fora do caminho a pé (células {a} e {b}); caminho a pé até a saída {straight} células";
                int one = fromSpawn[a.x, a.y] + ab + fromB[exit.x, exit.y];
                int two = fromSpawn[b.x, b.y] + ab + fromA[exit.x, exit.y];
                int cells = Mathf.Min(one, two);
                float meters = cells * LevelBuilder.Cell;
                float hold = objective.HoldSeconds * 2f;
                int toSecond = Mathf.Min(fromSpawn[a.x, a.y], fromSpawn[b.x, b.y]) + ab;
                float secondAt = toSecond * LevelBuilder.Cell / 2.8f + hold;
                valves = $"rota início-válvulas-saída {cells} células ({meters:F0} m): {meters / 2.8f + hold:F0} s andando, {meters / 5f + hold:F0} s correndo " +
                         $"(com {hold:F0} s girando); a segunda válvula (que drena e para a maré) andando aos {secondAt:F0} s";
            }
            return $"caminho a pé até a saída {straight} células ({straight * LevelBuilder.Cell:F0} m, {straight * LevelBuilder.Cell / 2.8f:F0} s andando); {valves}; " +
                   $"primeira maré: aviso aos {PoolTide.FirstRise - PoolTide.WarningSeconds:F0} s, sobe aos {PoolTide.FirstRise:F0} s, alta aos {PoolTide.FirstPeak:F0} s";
        }

        // Quantas células de água funda (e raso, que na maré alta vira nado) ficam ao alcance do ralo.
        static string DrainReachNote(PoolTide tide)
        {
            LevelData d = _level.Data;
            var wells = new List<Vector3>();
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    if (!d.IsSolid(x, z) && d.Depth[x, z] == PoolDepth.Well) wells.Add(_level.CellCenter(x, z));
            float reach = LevelBuilder.Cell * 0.5f + tide.pullReach;
            int deep = 0, deepIn = 0, shallow = 0, shallowIn = 0;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.IsSolid(x, z)) continue;
                    PoolDepth depth = d.Depth[x, z];
                    if (depth != PoolDepth.Deep && depth != PoolDepth.Shallow) continue;
                    Vector3 c = _level.CellCenter(x, z);
                    bool near = false;
                    foreach (Vector3 w in wells) if (new Vector2(w.x - c.x, w.z - c.z).magnitude <= reach) near = true;
                    if (depth == PoolDepth.Deep) { deep++; if (near) deepIn++; }
                    else { shallow++; if (near) shallowIn++; }
                }
            return $"ralo ({wells.Count} células de poço, alcance {reach:F0} m): {deepIn} de {deep} células fundas e {shallowIn} de {shallow} rasas ao alcance";
        }

        // Célula sem escada nem coluna no meio: dá para ficar de pé no centro.
        static bool Flat(LevelData d, int x, int z)
        {
            foreach (PoolStair stair in d.Stairs) if (stair.X == x && stair.Z == z) return false;
            return true;
        }

        static void Stand(Vector2Int cell)
        {
            Vector3 at = _level.CellCenter(cell.x, cell.y);
            _local.controller.Teleport(at + Vector3.up * 0.05f, 0f);
        }

        // À tona sobre a célula (os pés a 1,4 m da superfície).
        static void Float(Vector2Int cell)
        {
            Vector3 at = _level.CellCenter(cell.x, cell.y);
            float y = PoolWater.Current.SurfaceY - _local.controller.floatDepth;
            _local.controller.Teleport(new Vector3(at.x, y, at.z), 0f);
        }

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;
            ReleaseKeyboard();
            PoolTide.ForceSeconds = -1.0;
            PoolTide.ForceDrainStart = -1.0;
            EntityDirector.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }
            Debug.Log("[BackRooms] Teste da maré " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
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
