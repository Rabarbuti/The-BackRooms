using System;
using System.Collections.Generic;
using System.IO;
using BackRooms.Net;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace BackRooms.UI
{
    /// <summary>
    /// A filmadora grava de verdade: F9 começa e para, e o que o jogador vê — a imagem do jogo
    /// já com o efeito de fita e o painel por cima — vai para um arquivo de vídeo, com o som.
    /// Cada jogador grava a própria tela, na própria máquina.
    ///
    /// A imagem é copiada pelo próprio passe da filmadora (<see cref="CamcorderPass"/>), já
    /// reduzida, e lida da placa de vídeo sem travar o jogo; a compressão e a escrita ficam
    /// com o <see cref="AviWriter"/>, em outra thread. O som é o que sai para as caixas,
    /// apanhado pelo <see cref="RecorderAudioTap"/>.
    ///
    /// Os vídeos vão para a pasta Vídeos do usuário, em "The BackRooms" (no editor e na build:
    /// <c>C:\Users\&lt;nome&gt;\Videos\The BackRooms</c>). AVI com imagem em Motion JPEG e som PCM: o
    /// Windows abre sem instalar nada (Media Foundation, a base do Media Player e do Filmes e TV).
    /// A galeria <see cref="RecordingsScreen"/> lista, abre e apaga os clipes dessa pasta.
    /// </summary>
    public sealed class ScreenRecorder : MonoBehaviour
    {
        public static ScreenRecorder Instance { get; private set; }

        /// <summary>Para os testes: grava nesta pasta em vez de na pasta Vídeos.</summary>
        public static string FolderOverride;

        /// <summary>Pasta onde os clipes vão parar (e de onde a galeria os lê).</summary>
        public static string Folder => !string.IsNullOrEmpty(FolderOverride) ? FolderOverride : DefaultFolder();

        /// <summary>Começo do nome de todo clipe (a galeria só lista estes).</summary>
        public const string FilePrefix = "BackRooms_";
        public const string FileExtension = ".avi";
        public const string StampFormat = "yyyy-MM-dd_HH-mm-ss";

        [Header("Vídeo")]
        public int width = 854;
        public int height = 480;
        [Tooltip("Quadros por segundo do arquivo. A fita de verdade tinha 30; 20 pesa bem menos.")]
        public int framesPerSecond = 20;
        [Range(1, 100)] public int quality = 78;
        [Tooltip("A gravação para sozinha depois deste tempo, em minutos.")]
        public float maxMinutes = 15f;

        public bool IsRecording => _writer != null;

        /// <summary>Arquivo sendo gravado agora, ou vazio.</summary>
        public string CurrentFile => _writer != null ? _writer.Path : "";

        /// <summary>Tempo da gravação em andamento, em segundos.</summary>
        public float RecordedSeconds => IsRecording ? Time.unscaledTime - _startedAt : 0f;

        /// <summary>Caminho do último vídeo salvo, quadros que ele tem e há quanto tempo foi salvo.</summary>
        public string LastFile { get; private set; } = "";
        public int LastFrames { get; private set; }
        public float SecondsSinceSaved => _savedAt < 0f ? float.MaxValue : Time.unscaledTime - _savedAt;

        /// <summary>Por que a última tentativa de gravar falhou, ou vazio.</summary>
        public string LastError { get; private set; } = "";

        static volatile AviWriter _audioWriter;

        readonly Queue<int> _dueAtRequest = new Queue<int>();
        InputAction _toggle;
        AviWriter _writer;
        RenderTexture _texture;
        RTHandle _handle;
        Action<AsyncGPUReadbackRequest> _onReadback;
        float _startedAt;
        float _savedAt = -1f;
        int _requested;
        bool _wantFrame;

        void Awake()
        {
            Instance = this;
            _onReadback = OnReadback;
            // R no lugar do F9 (21/11/2026): na máquina do usuário o F9 sozinho não chegava ao jogo
            // (só com Alt), tomado por algo de fora; o R não tem outro uso.
            _toggle = new InputAction("Gravar", InputActionType.Button, "<Keyboard>/r");
        }

        void OnEnable() { _toggle.Enable(); }
        void OnDisable() { _toggle.Disable(); }

        void OnDestroy()
        {
            Stop();
            _toggle.Dispose();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            GameSession session = GameSession.Instance;
            bool playing = session != null && session.Phase == SessionPhase.Playing && PlayerNetwork.Local != null;

            if (IsRecording)
            {
                bool tooLong = RecordedSeconds > maxMinutes * 60f;
                if (!playing || tooLong || _writer.Full || _writer.Error != null || _toggle.WasPressedThisFrame())
                {
                    Stop();
                    return;
                }

                // Um quadro novo sempre que o relógio do vídeo passa do que já foi pedido.
                int due = Mathf.FloorToInt(RecordedSeconds * framesPerSecond) + 1;
                if (_requested < due) _wantFrame = true;
            }
            else if (playing && _toggle.WasPressedThisFrame())
            {
                Begin();
            }
        }

        /// <summary>Começa a gravar. Devolve falso (e preenche <see cref="LastError"/>) se não der.</summary>
        public bool Begin()
        {
            if (IsRecording) return true;

            try
            {
                string folder = Folder;
                Directory.CreateDirectory(folder);
                string stamp = DateTime.Now.ToString(StampFormat, System.Globalization.CultureInfo.InvariantCulture);
                string path = Path.Combine(folder, FilePrefix + stamp + FileExtension);
                // Dois clipes no mesmo segundo (F9 duas vezes depressa): não sobrescreve o primeiro.
                for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, FilePrefix + stamp + "_" + n + FileExtension);

                // Largura e altura pares: o JPEG de vídeo trabalha em blocos de dois pixels.
                int w = Mathf.Max(64, width) & ~1;
                int h = Mathf.Max(64, height) & ~1;

                _texture = new RenderTexture(w, h, 0, GraphicsFormat.R8G8B8A8_SRGB) { name = "Gravacao" };
                _texture.Create();
                _handle = RTHandles.Alloc(_texture);

                int rate = AudioSettings.outputSampleRate;
                int channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;

                _writer = new AviWriter(path, w, h, framesPerSecond, quality, rate, channels);
                _audioWriter = _writer;
                _dueAtRequest.Clear();
                _requested = 0;
                _wantFrame = true;
                _startedAt = Time.unscaledTime;
                LastError = "";
                Debug.Log("[BackRooms] Gravando em " + path);
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogWarning("[BackRooms] Não foi possível começar a gravar: " + e.Message);
                Release();
                return false;
            }
        }

        /// <summary>Para de gravar e fecha o arquivo.</summary>
        public void Stop()
        {
            if (!IsRecording) return;

            AviWriter writer = _writer;
            _audioWriter = null;
            _wantFrame = false;

            // Os quadros já pedidos à placa de vídeo ainda entram.
            AsyncGPUReadback.WaitAllRequests();
            _writer = null;
            writer.Finish();

            LastFile = writer.Path;
            LastFrames = writer.Frames;
            LastError = writer.Error ?? "";
            _savedAt = Time.unscaledTime;

            if (writer.Error != null) Debug.LogWarning("[BackRooms] A gravação foi interrompida: " + writer.Error);
            else Debug.Log($"[BackRooms] Vídeo salvo em {writer.Path} ({writer.Frames} quadros).");

            Release();
        }

        void Release()
        {
            _writer = null;
            _audioWriter = null;

            if (_handle != null) _handle.Release();
            _handle = null;

            if (_texture != null) Destroy(_texture);
            _texture = null;
        }

        static string DefaultFolder()
        {
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(videos)) videos = Application.persistentDataPath;
            return Path.Combine(videos, "The BackRooms");
        }

        /// <summary>
        /// Chamado pelo passe da filmadora logo depois de desenhar o efeito na imagem da câmera.
        /// Se há um quadro a gravar e a câmera é a do jogador desta máquina, copia a imagem,
        /// reduzida, e pede a leitura dela.
        /// </summary>
        public static void Capture(Camera camera, CommandBuffer cmd, RTHandle source)
        {
            ScreenRecorder recorder = Instance;
            if (recorder == null || recorder._writer == null || !recorder._wantFrame || recorder._handle == null) return;

            PlayerNetwork local = PlayerNetwork.Local;
            if (local == null || camera != local.playerCamera) return;

            Blitter.BlitCameraTexture(cmd, source, recorder._handle, 0f, true);
            cmd.RequestAsyncReadback(recorder._texture, 0, TextureFormat.RGBA32, recorder._onReadback);
            CoreUtils.SetRenderTarget(cmd, source);

            recorder._wantFrame = false;
            recorder._requested++;
            recorder._dueAtRequest.Enqueue(Mathf.FloorToInt(recorder.RecordedSeconds * recorder.framesPerSecond) + 1);
        }

        void OnReadback(AsyncGPUReadbackRequest request)
        {
            int due = _dueAtRequest.Count > 0 ? _dueAtRequest.Dequeue() : 0;
            AviWriter writer = _writer;
            if (writer == null || request.hasError) return;

            NativeArray<byte> data = request.GetData<byte>();
            byte[] frame = writer.RentFrame();
            if (data.Length != frame.Length) return;

            data.CopyTo(frame);
            writer.AddFrame(frame);

            // O jogo rodou mais devagar que o vídeo? Repete o quadro até alcançar o relógio.
            while (writer.FramesQueued < due) writer.AddRepeat();
            _requested = Mathf.Max(_requested, writer.FramesQueued);
        }

        /// <summary>Som que está saindo para as caixas. Chamado da thread de áudio pelo <see cref="RecorderAudioTap"/>.</summary>
        public static void PushAudio(float[] samples, int channels)
        {
            AviWriter writer = _audioWriter;
            if (writer != null) writer.AddAudio(samples, channels);
        }
    }
}
