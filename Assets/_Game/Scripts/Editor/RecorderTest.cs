using System.Collections.Generic;
using System.IO;
using BackRooms.Entities;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Teste em Play da gravação de verdade (F9, <see cref="ScreenRecorder"/>) e do painel da filmadora
    /// (<see cref="Camcorder"/>), no Level 0, com teclado virtual: parado, o canto mostra STBY e a dica
    /// do F9; F9 (a tecla, não o método) começa a gravar e o canto vira REC verde com o tempo do clipe;
    /// durante a gravação o arquivo existe e o cabeçalho diz "em curso"; F9 de novo para, o canto diz
    /// SAVED e o nome do arquivo e, uns segundos depois, volta a STBY. O arquivo é conferido por dentro:
    /// RIFF/AVI, Motion JPEG (todo quadro começa com FFD8), quadros e duração batendo com o tempo
    /// gravado, som PCM com a mesma duração e não mudo, índice com uma entrada por pedaço. A bateria
    /// aparece em porcentagem e, abaixo de 20%, pisca em vermelho.
    ///
    /// Fotos do painel em <c>Logs/Previews/filmadora/</c>: parado (bateria cheia), gravando, salvo e
    /// bateria baixa. A galeria das gravações é conferida pelo <see cref="PauseMenuTest"/>.
    ///
    /// Sem janela o Unity não desenha a câmera do jogador: em lote, a câmera desenha numa textura
    /// (como no teste de fumaça). Rodando no editor com janela, desenha na tela, como o jogador vê.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.RecorderTest.BatchRun. Sai com 0 se
    /// passou, 3 se falhou. Linha "[BackRooms] Teste da gravação".
    /// </summary>
    public static class RecorderTest
    {
        const string ActiveKey = "BackRooms.RecorderTest.Active";
        const string BatchKey = "BackRooms.RecorderTest.Batch";
        const string ExitCodeKey = "BackRooms.RecorderTest.ExitCode";
        const string Level0Scene = "Assets/_Game/Scenes/Level0_Test.unity";
        const string PhotoFolder = "Logs/Previews/filmadora";
        const string ClipFolder = "Logs/Gravacoes/teste";
        const double Timeout = 120.0;
        const float RecordSeconds = 4f;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage, _stageTicks;
        static double _began;
        static float _stageStart, _startedAt, _clipSeconds;
        static bool _finished;
        static Keyboard _keyboard;
        static InputSettings.BackgroundBehavior _backgroundBefore;
        static InputSettings.EditorInputBehaviorInPlayMode _editorInputBefore;
        static PlayerNetwork _local;
        static RenderTexture _target;
        static string _file;

        [MenuItem("BackRooms/Testar gravação (F9) e painel")]
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

        static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));

        // O painel pisca duas vezes por segundo (REC e bateria fraca): a foto espera a metade acesa.
        static bool BlinkOn
        {
            get
            {
                float half = Time.unscaledTime * 2f;
                float frac = half - Mathf.Floor(half);
                return Mathf.FloorToInt(half) % 2 == 0 && frac > 0.3f && frac < 0.7f;
            }
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || _finished) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;
            if (_began <= 0.0) _began = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - _began > Timeout) { Finish(false, $"tempo esgotado na etapa {_stage}"); return; }
            _stageTicks++;

            ScreenRecorder recorder = ScreenRecorder.Instance;
            Camcorder camcorder = Camcorder.Instance;
            // O tempo do clipe pelo relógio do próprio gravador, até o último quadro em que gravava.
            if (recorder != null && recorder.IsRecording) _clipSeconds = recorder.RecordedSeconds;

            switch (_stage)
            {
                case 0:
                {
                    EntityDirector.Paused = true;
                    CreateDevice();
                    string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ClipFolder));
                    Directory.CreateDirectory(folder);
                    foreach (string old in Directory.GetFiles(folder, "*.avi")) File.Delete(old);
                    ScreenRecorder.FolderOverride = folder;
                    session.fixedSeed = 1998;
                    session.Host(localOnly: true);
                    Next();
                    break;
                }

                case 1:
                {
                    if (session.Phase != SessionPhase.Playing) { _stageStart = Now; break; }
                    _local = PlayerNetwork.Local;
                    if (_local == null || InStage < 1.5f) break;
                    if (recorder == null) { Finish(false, "a cena não tem o gravador (ScreenRecorder)"); return; }
                    if (camcorder == null) { Finish(false, "a cena não tem o painel (Camcorder)"); return; }
                    if (Application.isBatchMode && _target == null)
                    {
                        _target = new RenderTexture(1600, 900, 24);
                        _local.playerCamera.targetTexture = _target;
                    }
                    if (InStage < 2.5f) break;
                    if (recorder.IsRecording) { Finish(false, "o gravador começou gravando sem F9"); return; }
                    if (camcorder.RecIndicator != "STBY") { Finish(false, $"parado, o canto mostra {camcorder.RecIndicator} (devia ser STBY)"); return; }
                    if (camcorder.BatteryText != "100%") { Finish(false, $"com a bateria cheia o painel mostra \"{camcorder.BatteryText}\""); return; }
                    if (!BlinkOn) break;
                    if (!Photo("parado_bateria_cheia.png")) return;
                    Next("parado: STBY, bateria 100%");
                    break;
                }

                case 2:
                    // F9: aperta num quadro, solta no seguinte.
                    if (_stageTicks == 1) { Keys(Key.F9); break; }
                    if (_stageTicks == 3) Keys();
                    if (!recorder.IsRecording) { if (InStage > 2f) Finish(false, "F9 (teclado) não começou a gravar: " + recorder.LastError); break; }
                    if (_stageTicks < 4) break;
                    _file = recorder.CurrentFile;
                    _startedAt = Now - recorder.RecordedSeconds;
                    if (!File.Exists(_file)) { Finish(false, $"gravando, o arquivo não existe: {_file}"); return; }
                    // Anda e liga a lanterna: imagem mudando e som (passos, clique).
                    Keys(Key.W);
                    Next($"F9 começou a gravar em {Path.GetFileName(_file)}");
                    break;

                case 3:
                {
                    if (_stageTicks == 10) Keys(Key.W, Key.F);
                    if (_stageTicks == 12) Keys(Key.W);
                    if (InStage < 1.5f) break;
                    if (camcorder.RecIndicator != "REC") { Finish(false, $"gravando, o canto mostra {camcorder.RecIndicator} (devia ser REC verde)"); return; }
                    AviWriter.Info during = AviWriter.ReadInfo(_file);
                    if (during.Finished) { Finish(false, "durante a gravação o cabeçalho já se diz fechado (a galeria não saberia que está em curso)"); return; }
                    if (!BlinkOn) break;
                    if (!Photo("gravando.png")) return;
                    Next("gravando: REC verde");
                    break;
                }

                case 4:
                    if (Now - _startedAt < RecordSeconds) break;
                    Keys();
                    Next();
                    break;

                case 5:
                    // F9 de novo: para.
                    if (_stageTicks == 2) { Keys(Key.F9); break; }
                    if (_stageTicks == 4) Keys();
                    if (_stageTicks < 4) break;
                    if (recorder.IsRecording) { if (InStage > 2f) Finish(false, "F9 de novo não parou a gravação"); break; }
                    if (recorder.LastFile != _file) { Finish(false, $"parou e salvou outro arquivo: {recorder.LastFile}"); return; }
                    if (!string.IsNullOrEmpty(recorder.LastError)) { Finish(false, "a gravação deu erro: " + recorder.LastError); return; }
                    Next();
                    break;

                case 6:
                    if (_stageTicks < 4) break;
                    if (camcorder.RecIndicator != "SAVED") { Finish(false, $"parado depois de gravar, o canto mostra {camcorder.RecIndicator} (devia ser SAVED)"); return; }
                    if (!CheckFile(_file, _clipSeconds)) return;
                    if (!Photo("salvo.png")) return;
                    Next("parou: SAVED e o nome do arquivo");
                    break;

                case 7:
                    // O aviso some sozinho e volta a STBY.
                    if (camcorder.RecIndicator != "STBY") { if (InStage > 6f) Finish(false, $"o aviso SAVED não saiu da tela ({camcorder.RecIndicator})"); break; }
                    Next($"SAVED sumiu em {InStage:F1} s");
                    break;

                case 8:
                {
                    // Bateria baixa: porcentagem certa e piscando em vermelho.
                    Flashlight torch = _local.flashlight;
                    if (_stageTicks == 1)
                    {
                        if (torch.infiniteBattery) { Finish(false, "a lanterna está com bateria infinita"); return; }
                        torch.Recharge(-torch.batterySeconds);
                        torch.Recharge(torch.batterySeconds * 0.12f);
                        torch.SetOn(false);
                        break;
                    }
                    if (_stageTicks < 5) break;
                    if (camcorder.BatteryText != "12%" || !camcorder.BatteryLow) { if (InStage > 2f) Finish(false, $"com 12% de bateria o painel mostra \"{camcorder.BatteryText}\" (fraca={camcorder.BatteryLow})"); break; }
                    if (!BlinkOn) break;
                    if (!Photo("bateria_baixa.png")) return;
                    torch.Recharge(torch.batterySeconds * 0.1f);
                    Next("bateria a 12%: \"12%\" piscando em vermelho");
                    break;
                }

                case 9:
                    if (_stageTicks < 5) break;
                    if (camcorder.BatteryText != "22%" || camcorder.BatteryLow) { if (InStage > 2f) Finish(false, $"com 22% de bateria o painel mostra \"{camcorder.BatteryText}\" (fraca={camcorder.BatteryLow})"); break; }
                    Finish(true, string.Join("; ", Notes) + "; a 22% deixa de piscar");
                    break;
            }
        }

        // ---------------------------------------------------------------- Arquivo

        // Lê o AVI inteiro: cabeçalho, pedaços do movi e índice.
        static bool CheckFile(string path, float recorded)
        {
            AviWriter.Info info = AviWriter.ReadInfo(path);
            if (!info.Valid || !info.Finished) { Finish(false, $"cabeçalho inválido ou não fechado (válido={info.Valid}, fechado={info.Finished})"); return false; }
            if (Mathf.Abs(info.Seconds - recorded) > 0.6f) { Finish(false, $"o vídeo diz {info.Seconds:F2} s e a gravação durou {recorded:F2} s"); return false; }
            if (!info.HasAudio || info.AudioRate <= 0) { Finish(false, "o vídeo não tem faixa de som"); return false; }

            byte[] bytes = File.ReadAllBytes(path);
            int moviStart = -1, idx = -1;
            for (int i = 12; i + 12 <= bytes.Length;)
            {
                string tag = Tag(bytes, i);
                int size = System.BitConverter.ToInt32(bytes, i + 4);
                if (tag == "LIST")
                {
                    if (Tag(bytes, i + 8) == "movi") { moviStart = i + 12; i = i + 8 + size; continue; }
                    i += 12;
                    continue;
                }
                if (tag == "idx1") { idx = i; break; }
                i += 8 + size + (size & 1);
            }
            if (moviStart < 0 || idx < 0) { Finish(false, $"sem a lista movi ({moviStart}) ou sem índice ({idx})"); return false; }

            int video = 0, jpegs = 0, badJpeg = 0, chunks = 0;
            long audioBytes = 0;
            double sum = 0;
            long samples = 0;
            for (int i = moviStart; i + 8 <= idx;)
            {
                string tag = Tag(bytes, i);
                int size = System.BitConverter.ToInt32(bytes, i + 4);
                int data = i + 8;
                chunks++;
                if (tag == "00dc")
                {
                    video++;
                    if (size > 0)
                    {
                        jpegs++;
                        if (bytes[data] != 0xFF || bytes[data + 1] != 0xD8) badJpeg++;
                    }
                }
                else if (tag == "01wb")
                {
                    audioBytes += size;
                    for (int s = 0; s + 1 < size; s += 2)
                    {
                        float v = System.BitConverter.ToInt16(bytes, data + s) / 32768f;
                        sum += v * v;
                        samples++;
                    }
                }
                else { Finish(false, $"pedaço estranho no movi: {tag}"); return false; }
                i = data + size + (size & 1);
            }

            int indexEntries = System.BitConverter.ToInt32(bytes, idx + 4) / 16;
            float audioSeconds = audioBytes / (float)(info.AudioRate * info.AudioChannels * 2);
            float rms = samples > 0 ? Mathf.Sqrt((float)(sum / samples)) : 0f;
            float db = 20f * Mathf.Log10(Mathf.Max(rms, 1e-9f));

            if (video != info.Frames) { Finish(false, $"o cabeçalho diz {info.Frames} quadros e o movi tem {video}"); return false; }
            if (jpegs < info.Frames / 3) { Finish(false, $"só {jpegs} de {video} quadros têm imagem (o resto é repetição)"); return false; }
            if (badJpeg > 0) { Finish(false, $"{badJpeg} quadros não começam como JPEG"); return false; }
            if (indexEntries != chunks) { Finish(false, $"o índice tem {indexEntries} entradas e o movi {chunks} pedaços"); return false; }
            if (Mathf.Abs(audioSeconds - info.Seconds) > 0.5f) { Finish(false, $"o som dura {audioSeconds:F2} s e o vídeo {info.Seconds:F2} s"); return false; }
            if (db < -70f) { Finish(false, $"o som está mudo ({db:F0} dB)"); return false; }

            Notes.Add($"arquivo: {bytes.Length / 1024} KB, {info.Width}x{info.Height}, {info.Frames} quadros a {info.FramesPerSecond:F0}/s ({info.Seconds:F2} s; gravou {recorded:F2} s), {jpegs} JPEG e {video - jpegs} repetidos, " +
                      $"som PCM {info.AudioRate} Hz x{info.AudioChannels} de {audioSeconds:F2} s a {db:F0} dB RMS, índice com {indexEntries} entradas; {path}");
            return true;
        }

        static string Tag(byte[] bytes, int at) => System.Text.Encoding.ASCII.GetString(bytes, at, 4);

        // ---------------------------------------------------------------- Fotos

        // Em lote, a câmera desenha na textura: a foto é ela. Com janela, a tela.
        static bool Photo(string name)
        {
            Directory.CreateDirectory(PhotoFolder);
            string path = Path.Combine(PhotoFolder, name);
            if (_target == null)
            {
                ScreenCapture.CaptureScreenshot(path);
                Notes.Add("foto " + path);
                return true;
            }

            var texture = new Texture2D(_target.width, _target.height, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _target;
            texture.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
            return true;
        }

        // ---------------------------------------------------------------- Ajuda

        static void CreateDevice()
        {
            if (_keyboard != null) return;
            _backgroundBefore = InputSystem.settings.backgroundBehavior;
            _editorInputBefore = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;
            EntityDirector.Paused = false;
            if (ScreenRecorder.Instance != null) ScreenRecorder.Instance.Stop();
            ScreenRecorder.FolderOverride = null;
            if (_local != null && _local.playerCamera != null) _local.playerCamera.targetTexture = null;
            if (_target != null) { _target.Release(); Object.Destroy(_target); _target = null; }
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                InputSystem.settings.backgroundBehavior = _backgroundBefore;
                InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBefore;
                _keyboard = null;
            }
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }
            Debug.Log("[BackRooms] Teste da gravação " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
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
