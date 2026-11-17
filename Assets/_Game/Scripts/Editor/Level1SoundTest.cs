using System.Collections.Generic;
using BackRooms.Audio;
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
    /// Teste em Play do som do Level 1 (level1-mostruario.md, seção 7), com teclado virtual:
    /// andando (W) no laminado, no vinílico e no carpete, a superfície dos passos é a do piso e
    /// o som dela toca; a música da loja toca e se cala antes de o aviso do apagão começar; o
    /// jingle do Capitão é alto e agudo de perto, baixo e grave de longe, e mudo com ele
    /// congelado. Sem placa de som no modo em lote, confere o estado dos componentes (o que
    /// tocou, volume e tom), não o áudio de verdade: ouvir é com gente (os WAV saem em
    /// EntitySounds).
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level1SoundTest.BatchRun (sai com 0 se
    /// passou, 3 se falhou). Menu: BackRooms > Testar som do Level 1. Linha
    /// "[BackRooms] Teste de som do Level 1".
    /// </summary>
    public static class Level1SoundTest
    {
        const string ActiveKey = "BackRooms.Level1SoundTest.Active";
        const string BatchKey = "BackRooms.Level1SoundTest.Batch";
        const string ExitCodeKey = "BackRooms.Level1SoundTest.ExitCode";
        const double Timeout = 150.0;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks, _played;
        static double _began;
        static float _stageStart;
        static bool _finished;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static Footsteps _feet;
        static ShopMusic _music;
        static Keyboard _keyboard;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;
        static readonly (string name, StepSurface surface)[] Floors =
        {
            ("PisoLaminado", StepSurface.Laminate), ("PisoVinilico", StepSurface.Vinyl),
        };
        static int _floor;

        [MenuItem("BackRooms/Testar som do Level 1")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false);
        }

        public static void BatchRun() => Run(true);

        static void Run(bool batch)
        {
            EditorSceneManager.OpenScene(Level1Setup.ScenePath, OpenSceneMode.Single);
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
            if (EditorApplication.timeSinceStartup - _began > Timeout)
            {
                Finish(false, $"tempo esgotado na etapa {_stage}");
                return;
            }

            _stageTicks++;
            PlayerController body = _local != null ? _local.controller : null;

            switch (_stage)
            {
                case 0:
                    EntityDirector.Paused = true;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    Next();
                    break;

                case 1:
                    if (session.Phase != SessionPhase.Playing) break;
                    _local = PlayerNetwork.Local;
                    _level = session.level;
                    if (_local == null || _level == null || _level.Data == null || InStage < 1.5f) break;
                    _feet = _local.GetComponent<Footsteps>();
                    _music = Object.FindFirstObjectByType<ShopMusic>();
                    if (_music == null) { Finish(false, "o Level 1 não tem a música da loja"); return; }
                    if (LightsOut.Instance != null) LightsOut.Instance.firstDarkAt = 1e6f;

                    _backgroundBefore = InputSystem.settings.backgroundBehavior;
                    _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    _keyboard = InputSystem.AddDevice<Keyboard>();
                    Press();
                    _floor = 0;
                    Next($"{FloorZones.Count} células de laminado ou vinílico");
                    break;

                case 2:
                {
                    // Cada piso: começa na borda sul da célula, olhando para o norte, e anda pelo meio
                    // (a cruz livre) por 0,8 s.
                    if (_floor >= Floors.Length) { Press(); Next(); break; }
                    Transform tile = _level.transform.Find("Moveis/" + Floors[_floor].name);
                    if (tile == null) { Finish(false, $"sem {Floors[_floor].name} na fase"); return; }
                    if (_stageTicks == 1)
                    {
                        body.Teleport(tile.position - Vector3.forward * 1.5f + Vector3.up * 0.05f, 0f);
                        _played = _feet.PlayedOn(Floors[_floor].surface);
                        break;
                    }
                    Press(Key.W);
                    if (InStage < 1.2f) break;
                    Press();
                    StepSurface want = Floors[_floor].surface;
                    if (_feet.Surface != want) { Finish(false, $"no {Floors[_floor].name} a superfície dos passos foi {_feet.Surface}"); return; }
                    int played = _feet.PlayedOn(want) - _played;
                    if (played < 1) { Finish(false, $"andou no {Floors[_floor].name} e nenhum passo de {want} tocou"); return; }
                    Notes.Add($"no {Floors[_floor].name}: superfície {want}, {played} passo(s)");
                    _floor++;
                    _stageStart = Now;
                    _stageTicks = 0;
                    break;
                }

                case 3:
                    // E no carpete (a sala inicial).
                    if (_stageTicks == 1) { body.Teleport(_level.SpawnCenter, 0f); break; }
                    if (InStage < 0.5f) break;
                    if (_feet.Surface != StepSurface.Carpet) { Finish(false, $"na sala inicial a superfície dos passos foi {_feet.Surface}"); return; }
                    Next("na sala inicial: carpete");
                    break;

                case 4:
                    // A música da loja toca.
                    if (_music.Level < 0.95f)
                    {
                        if (InStage > 6f) Finish(false, $"a música da loja não chegou ao volume cheio em 6 s ({_music.Level:F2})");
                        break;
                    }
                    // Apagão daqui a 16 s: o aviso começa em 6 s, e a música tem de se calar antes.
                    LightsOut.Instance.firstDarkAt = (float)session.SessionSeconds + LightsOut.Instance.warningSeconds + 6f;
                    Next("a música da loja toca");
                    break;

                case 5:
                {
                    PowerPhase phase = LightsOut.Instance.Phase;
                    if (phase == PowerPhase.Steady)
                    {
                        if (InStage > 12f) Finish(false, "o aviso do apagão não começou");
                        break;
                    }
                    if (_music.Level > 0.01f) { Finish(false, $"o aviso do apagão começou com a música ainda a {_music.Level:F2}"); return; }
                    LightsOut.Instance.firstDarkAt = 1e6f;
                    Next($"a música se calou antes do aviso do apagão ({InStage:F1} s depois de marcado)");
                    break;
                }

                case 6:
                {
                    // O jingle do Capitão, de perto (3 m) e de longe (14 m).
                    EntityDirector director = EntityDirector.Instance;
                    Vector3 me = body.transform.position;
                    if (_stageTicks == 1)
                    {
                        body.Teleport(FarFrom(me, 3f, out Vector3 at), 0f);
                        CaptainBrain near = director.SpawnAt(at, body.transform.position - at) as CaptainBrain;
                        // De costas, para não congelá-lo.
                        body.transform.rotation = Quaternion.LookRotation(Flat(body.transform.position - at), Vector3.up);
                        break;
                    }
                    var audio = director.Entity != null ? director.Entity.GetComponent<CaptainAudio>() : null;
                    if (audio == null) { Finish(false, "o Capitão não tem CaptainAudio"); return; }
                    if (InStage < 0.5f) break;
                    if (audio.JingleLevel < 0.5f || audio.JinglePitch < 1.1f) { Finish(false, $"a 3 m o jingle ficou com volume {audio.JingleLevel:F2} e tom {audio.JinglePitch:F2}"); return; }
                    Notes.Add($"a 3 m o jingle: volume {audio.JingleLevel:F2}, tom {audio.JinglePitch:F2}");
                    Vector3 far = director.Entity.transform.position + Flat(director.Entity.transform.position - body.transform.position).normalized * 11f;
                    director.SpawnAt(far, Vector3.forward);
                    Next();
                    break;
                }

                case 7:
                {
                    var audio = EntityDirector.Instance.Entity.GetComponent<CaptainAudio>();
                    if (InStage < 0.5f) break;
                    float distance = Vector3.Distance(audio.transform.position, body.transform.position);
                    if (audio.JingleLevel > 0.35f || audio.JinglePitch > 1.05f) { Finish(false, $"a {distance:F0} m o jingle ficou com volume {audio.JingleLevel:F2} e tom {audio.JinglePitch:F2}"); return; }
                    Finish(true, string.Join("; ", Notes) + $"; a {distance:F0} m o jingle: volume {audio.JingleLevel:F2}, tom {audio.JinglePitch:F2}");
                    break;
                }
            }
        }

        // Um ponto no chão, a "meters" de algum lugar onde o Capitão possa ficar, na mesma linha
        // reta e sem parede no meio: devolve onde o jogador fica e onde fica o Capitão.
        static Vector3 FarFrom(Vector3 player, float meters, out Vector3 captain)
        {
            var nav = new LevelNav(_level);
            for (int z = 0; z < _level.Data.Height; z++)
                for (int x = 0; x < _level.Data.Width; x++)
                {
                    if (!nav.Standable(x, z)) continue;
                    Vector3 c = nav.CellCenter(x, z);
                    foreach (Vector3 dir in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
                    {
                        Vector3 p = c + dir * meters;
                        if (!nav.WorldToCell(p, out int px, out int pz) || _level.Data.InRoom(px, pz)) continue;
                        if (Physics.Raycast(c + Vector3.up * 1.2f, dir, meters)) continue;
                        captain = c;
                        return p;
                    }
                }
            captain = player + Vector3.forward * meters;
            return player;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
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

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;

            ReleaseKeyboard();
            EntityDirector.Paused = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log("[BackRooms] Teste de som do Level 1 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

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
