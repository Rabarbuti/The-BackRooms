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
    /// Teste em Play da maré e do ralo das Piscinas (<see cref="PoolTide"/>, level2-piscinas.md,
    /// seções 4.3 e 5), com o relógio da maré forçado (<see cref="PoolTide.ForceSeconds"/>):
    /// parada, a água está na altura de sempre; na maré alta sobe 0,9 m, o seco fica com água na
    /// canela-joelho (vadeia-se, não nada) e, de pé no raso, a cabeça fica fora d'água; na vazante,
    /// quem nada sobre um poço é puxado até a grade e se afoga em poucos segundos; com a maré parada,
    /// o mesmo lugar só afunda devagar; na água funda longe dos poços o ralo não puxa.
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
        const double Timeout = 150.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks, _drownings;
        static double _began;
        static float _stageStart;
        static bool _finished;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static Vector2Int _dry, _shallow, _well, _farDeep;
        static bool _hasFar;

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

        static double Peak => PoolTide.FirstRise + PoolTide.RiseSeconds;
        static double Still => PoolTide.FirstRise - 30.0;
        static double Ebb => PoolTide.FirstRise + PoolTide.RiseSeconds + 4.0;

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
                    if (PoolTide.Current == null) { Finish(false, "a água das Piscinas não tem maré (PoolTide)"); return; }
                    if (!FindCells()) return;
                    float rest = water.SurfaceY;
                    if (Mathf.Abs(rest - LevelData.WaterLevel - _level.transform.position.y) > 0.01f) { Finish(false, $"com a maré parada a água está em {rest:F2} m (era {LevelData.WaterLevel})"); return; }
                    PoolTide.ForceSeconds = Peak;
                    Stand(_dry);
                    Next($"parada, água em {rest:F2} m; seco {_dry}, raso {_shallow}, poço {_well}" + (_hasFar ? $", funda longe {_farDeep}" : ""));
                    break;
                }

                case 2:
                {
                    if (InStage < 2.5f) break;
                    float rise = water.Offset;
                    if (Mathf.Abs(rise - PoolTide.Amplitude) > 0.02f) { Finish(false, $"na maré alta a água subiu {rise:F2} m (eram {PoolTide.Amplitude})"); return; }
                    if (body.Submersion < 0.5f || body.IsSwimming) { Finish(false, $"na maré alta, de pé no seco: {body.Submersion:F2} m de água, nadando={body.IsSwimming}"); return; }
                    float dry = body.Submersion;
                    Stand(_shallow);
                    Next($"maré alta: +{rise:F2} m; no seco, {dry:F2} m de água sem nadar");
                    break;
                }

                case 3:
                    if (InStage < 4f) break;
                    if (body.IsUnderwater) { Finish(false, $"na maré alta, no raso, a cabeça ficou debaixo d'água ({body.Submersion:F2} m de água)"); return; }
                    if (body.IsReturning) { Finish(false, "na maré alta, no raso, o jogador se afogou"); return; }
                    Notes.Add($"no raso, {body.Submersion:F2} m de água, cabeça fora d'água (nadando={body.IsSwimming})");
                    // A vazante: o ralo puxa.
                    PoolTide.ForceSeconds = Ebb;
                    _drownings = body.Drownings;
                    Float(_well);
                    Next();
                    break;

                case 4:
                {
                    PoolTide.ForceSeconds = Ebb + Mathf.Min(InStage, 8f);
                    if (!PoolTide.Current.Pulling) { Finish(false, "na vazante o ralo não está puxando"); return; }
                    if (body.Drownings <= _drownings)
                    {
                        if (InStage > 8f) Finish(false, $"na vazante, sobre o poço, não foi puxado até a grade em 8 s (pés a {body.transform.position.y:F2} m, puxão {PoolTide.Current.LocalPull:F2})");
                        break;
                    }
                    float took = InStage;
                    PoolTide.ForceSeconds = Still;
                    Next($"na vazante, sobre o poço, foi puxado até a grade e se afogou em {took:F1} s");
                    break;
                }

                case 5:
                    // Esperando voltar ao início antes de testar de novo.
                    if (body.IsReturning || InStage < 2f) break;
                    _drownings = body.Drownings;
                    Float(_well);
                    Next();
                    break;

                case 6:
                    if (body.Drownings > _drownings) { Finish(false, $"com a maré parada, sobre o poço, se afogou em {InStage:F1} s (só devia afundar devagar)"); return; }
                    if (PoolTide.Current.LocalPull > 0f) { Finish(false, "com a maré parada o ralo puxou"); return; }
                    if (InStage < 6f) break;
                    if (!_hasFar)
                    {
                        Finish(true, string.Join("; ", Notes) + "; parada, sobre o poço, só afundou (sem funda longe dos poços nesta seed)");
                        return;
                    }
                    PoolTide.ForceSeconds = Ebb;
                    Float(_farDeep);
                    Next("com a maré parada, sobre o poço, só afundou devagar");
                    break;

                case 7:
                    if (PoolTide.Current.LocalPull > 0f) { Finish(false, $"na vazante, na funda longe dos poços ({_farDeep}), o ralo puxou"); return; }
                    if (InStage < 3f) break;
                    Finish(true, string.Join("; ", Notes) + $"; na vazante, na funda longe dos poços, não puxou");
                    break;
            }
        }

        static bool FindCells()
        {
            LevelData d = _level.Data;
            var wells = new List<Vector2Int>();
            bool dry = false, shallow = false;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.IsSolid(x, z) || d.InRoom(x, z)) continue;
                    PoolDepth depth = d.Depth[x, z];
                    if (depth == PoolDepth.Well) wells.Add(new Vector2Int(x, z));
                    if (depth == PoolDepth.Dry && !dry && Flat(d, x, z)) { _dry = new Vector2Int(x, z); dry = true; }
                    if (depth == PoolDepth.Shallow && !shallow && Flat(d, x, z)) { _shallow = new Vector2Int(x, z); shallow = true; }
                }
            if (!dry || !shallow || wells.Count == 0) { Finish(false, $"a seed não tem seco ({dry}), raso ({shallow}) ou poço ({wells.Count})"); return false; }
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
            PoolTide.ForceSeconds = -1.0;
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
