using System;
using BackRooms.Entities;
using BackRooms.Net;
using BackRooms.Player;
using UnityEngine;

namespace BackRooms.UI
{
    /// <summary>
    /// Painel de filmadora (OSD): REC piscando, contador da fita, bateria, hora e data.
    /// É desenhado com a PixelFont (matriz 5x7) numa textura pequena, que o shader
    /// BackRooms/CamcorderVHS sobrepõe à imagem antes de aplicar os defeitos de fita —
    /// por isso o texto treme e borra junto com o resto, como numa gravação de verdade.
    /// No menu mostra STBY (em espera); ao entrar no mapa, começa a gravar.
    /// </summary>
    public sealed class Camcorder : MonoBehaviour
    {
        public static Camcorder Instance { get; private set; }

        [Header("Relógio que aparece na fita")]
        public int startYear = 1998;
        [Range(1, 12)] public int startMonth = 11;
        [Range(1, 28)] public int startDay = 10;
        [Range(0, 23)] public int startHour = 2;
        [Range(0, 59)] public int startMinute = 13;

        [Header("Interferência")]
        [Tooltip("Duração do chuvisco quando a gravação começa, em segundos.")]
        public float startGlitchSeconds = 0.9f;
        [Tooltip("Quanto a tela continua preta depois de o jogador reaparecer de uma queda, em segundos.")]
        public float noSignalHold = 0.35f;
        [Tooltip("A entidade interfere na imagem a partir desta distância, em metros.")]
        public float entityNoiseRange = 16f;
        [Tooltip("Interferência máxima com a entidade colada no jogador (0 a 1).")]
        [Range(0f, 1f)] public float entityNoiseMax = 0.6f;

        const int Width = 480;
        const int Height = 270;
        const int Margin = 20;

        static readonly int OsdId = Shader.PropertyToID("_BackRoomsOsd");
        static readonly int OsdOnId = Shader.PropertyToID("_BackRoomsOsdOn");
        static readonly int GlitchId = Shader.PropertyToID("_BackRoomsGlitch");

        static readonly Color32 White = new Color32(236, 236, 226, 255);
        static readonly Color32 Red = new Color32(232, 38, 28, 255);
        static readonly Color32 Shadow = new Color32(0, 0, 0, 190);
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        static readonly Color32 Black = new Color32(0, 0, 0, 255);

        static readonly string[] Months = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

        Texture2D _osd;
        Color32[] _pixels;
        float _recordSeconds;
        float _glitch;
        float _glitchFade;
        bool _wasRecording;
        int _lastFrame = -1;
        int _seenFalls;
        float _noSignal;

        void OnEnable()
        {
            Instance = this;
            EnsureTexture();
            Shader.SetGlobalFloat(OsdOnId, 1f);
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            Shader.SetGlobalFloat(OsdOnId, 0f);
            Shader.SetGlobalFloat(GlitchId, 0f);

            if (_osd != null)
            {
                if (Application.isPlaying) Destroy(_osd);
                else DestroyImmediate(_osd);
                _osd = null;
            }
        }

        /// <summary>Interferência momentânea na imagem (0 a 1), que some em <paramref name="seconds"/>.</summary>
        public void Glitch(float amount, float seconds)
        {
            _glitch = Mathf.Max(_glitch, Mathf.Clamp01(amount));
            _glitchFade = _glitch / Mathf.Max(0.05f, seconds);
        }

        void Update()
        {
            GameSession session = GameSession.Instance;
            bool recording = session != null && session.Phase == SessionPhase.Playing;

            if (recording && !_wasRecording)
            {
                _recordSeconds = 0f;
                Glitch(1f, startGlitchSeconds);
            }
            _wasRecording = recording;

            if (recording) _recordSeconds += Time.deltaTime;

            // Queda em buraco ou captura pela entidade: a imagem some (tela preta, NO SIGNAL)
            // até o jogador reaparecer na sala inicial, e volta com chuvisco.
            PlayerController body = PlayerNetwork.Local != null ? PlayerNetwork.Local.controller : null;
            int falls = body != null ? body.Falls + body.Captures : 0;

            if (falls > _seenFalls)
            {
                _noSignal = (body.IsCaught ? body.captureRespawnDelay : body.pitRespawnDelay) + noSignalHold;
                Glitch(1f, _noSignal + 1.2f);
            }
            _seenFalls = falls;
            _noSignal = Mathf.Max(0f, _noSignal - Time.deltaTime);

            // A entidade estraga a imagem de quem está perto dela: é um aviso, junto com o som.
            EntityBrain entity = EntityBrain.Instance;
            if (entity != null && body != null && entityNoiseRange > 0f)
            {
                float distance = Vector3.Distance(entity.transform.position, body.transform.position);
                float near = Mathf.InverseLerp(entityNoiseRange, 2.5f, distance);
                if (near > 0f) Glitch(near * near * entityNoiseMax, 0.5f);
            }

            _glitch = Mathf.MoveTowards(_glitch, 0f, _glitchFade * Time.unscaledDeltaTime);
            Shader.SetGlobalFloat(GlitchId, _glitch);

            // Redesenha na cadência da fita (30 quadros por segundo), não a cada quadro do jogo.
            int frame = Mathf.FloorToInt(Time.unscaledTime * 30f);
            if (frame == _lastFrame) return;
            _lastFrame = frame;

            Flashlight torch = PlayerNetwork.Local != null ? PlayerNetwork.Local.flashlight : null;
            float battery = torch == null || torch.infiniteBattery ? 1f : torch.Battery01;
            bool lightOn = torch != null && torch.IsOn;

            // Com a caneta na mão: nome da cor no canto e um ponto de mira no centro.
            var pen = PlayerNetwork.Local != null ? PlayerNetwork.Local.pen : null;
            string tool = pen != null && pen.Equipped ? "PEN " + pen.ColorName : null;

            Draw(recording, _recordSeconds, Time.unscaledTime, battery, lightOn, tool, _noSignal > 0f);
        }

        /// <summary>
        /// Desenha um quadro do painel e o publica para o shader. Público para a ferramenta
        /// de prévia do editor, que roda fora do Play.
        /// </summary>
        public void Draw(bool recording, float recordSeconds, float clockSeconds, float battery, bool lightOn, string tool = null,
                         bool noSignal = false)
        {
            EnsureTexture();

            // Sem sinal: o painel inteiro fica opaco e tapa a imagem do jogo.
            Color32 background = noSignal ? Black : Clear;
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = background;

            if (noSignal)
            {
                const string message = "NO SIGNAL";
                Text((Width - MeasureText(message, 3)) / 2, Height / 2 - 10, message, 3, White);
            }

            DrawCorners();

            // Canto de cima, esquerda: estado da gravação.
            if (recording)
            {
                bool dotOn = Mathf.FloorToInt(clockSeconds * 2f) % 2 == 0;
                if (dotOn) Disc(Margin + 8, Margin + 9, 6, Red);
                Text(Margin + 20, Margin + 2, "REC", 2, Red);
                Text(Margin + 62, Margin + 9, "SP", 1, White);
            }
            else
            {
                Text(Margin, Margin + 2, "STBY", 2, White);
            }

            // Canto de cima, direita: bateria e contador da fita.
            DrawBattery(Width - Margin - 33, Margin + 2, battery, clockSeconds);

            int total = Mathf.FloorToInt(recordSeconds);
            int frames = Mathf.FloorToInt((recordSeconds - total) * 30f);
            string counter = $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}:{frames:00}";
            TextRight(Width - Margin, Margin + 24, counter, 1, White);

            // Canto de baixo, direita: hora e data, como nas filmadoras dos anos 90.
            DateTime now = new DateTime(startYear, startMonth, startDay, startHour, startMinute, 0).AddSeconds(clockSeconds);
            int hour12 = now.Hour % 12 == 0 ? 12 : now.Hour % 12;
            string time = $"{(now.Hour < 12 ? "AM" : "PM")} {hour12}:{now.Minute:00}:{now.Second:00}";
            string date = $"{Months[now.Month - 1]} {now.Day} {now.Year}";
            TextRight(Width - Margin, Height - Margin - 32, time, 2, White);
            TextRight(Width - Margin, Height - Margin - 14, date, 2, White);

            // Canto de baixo, esquerda: aviso da lanterna e, acima dele, o item na mão.
            if (lightOn) Text(Margin, Height - Margin - 7, "LIGHT", 1, White);

            if (!string.IsNullOrEmpty(tool) && !noSignal)
            {
                Text(Margin, Height - Margin - 19, tool, 1, White);

                // Mira: um ponto pequeno no centro, com contorno escuro.
                Box(Width / 2 - 2, Height / 2 - 2, 4, 4, Shadow);
                Box(Width / 2 - 1, Height / 2 - 1, 2, 2, White);
            }

            _osd.SetPixels32(_pixels);
            _osd.Apply(false);
            Shader.SetGlobalTexture(OsdId, _osd);
            Shader.SetGlobalFloat(OsdOnId, 1f);
        }

        void EnsureTexture()
        {
            if (_osd != null) return;

            _osd = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                name = "FilmadoraOSD",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _pixels = new Color32[Width * Height];
        }

        // ---------------------------------------------------------------- Desenho

        // Coordenadas com a origem no canto de cima, esquerda.
        void Plot(int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            _pixels[(Height - 1 - y) * Width + x] = color;
        }

        void Box(int x, int y, int w, int h, Color32 color)
        {
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                    Plot(x + i, y + j, color);
        }

        void Disc(int cx, int cy, int radius, Color32 color)
        {
            for (int j = -radius; j <= radius; j++)
                for (int i = -radius; i <= radius; i++)
                    if (i * i + j * j <= radius * radius + 1) Plot(cx + i, cy + j, color);
        }

        // Cantoneiras do visor.
        void DrawCorners()
        {
            const int inset = 9, length = 18, thick = 2;

            Box(inset, inset, length, thick, White);
            Box(inset, inset, thick, length, White);
            Box(Width - inset - length, inset, length, thick, White);
            Box(Width - inset - thick, inset, thick, length, White);
            Box(inset, Height - inset - thick, length, thick, White);
            Box(inset, Height - inset - length, thick, length, White);
            Box(Width - inset - length, Height - inset - thick, length, thick, White);
            Box(Width - inset - thick, Height - inset - length, thick, length, White);
        }

        // Pilha com três barras. Abaixo de 15% pisca em vermelho.
        void DrawBattery(int x, int y, float level, float clockSeconds)
        {
            bool low = level < 0.15f;
            if (low && Mathf.FloorToInt(clockSeconds * 2f) % 2 == 1) return;

            Color32 color = low ? Red : White;
            const int w = 30, h = 14;

            Box(x, y, w, 2, color);
            Box(x, y + h - 2, w, 2, color);
            Box(x, y, 2, h, color);
            Box(x + w - 2, y, 2, h, color);
            Box(x + w, y + 4, 3, 6, color);

            int bars = level > 0.66f ? 3 : level > 0.33f ? 2 : level > 0.05f ? 1 : 0;
            for (int i = 0; i < bars; i++) Box(x + 4 + i * 8, y + 4, 6, 6, color);
        }

        void TextRight(int right, int y, string text, int scale, Color32 color)
        {
            Text(right - MeasureText(text, scale), y, text, scale, color);
        }

        static int MeasureText(string text, int scale)
        {
            return PixelFont.Measure(text, scale);
        }

        // Texto com sombra de um pixel, para ler sobre qualquer fundo.
        void Text(int x, int y, string text, int scale, Color32 color)
        {
            Glyphs(x + scale, y + scale, text, scale, Shadow);
            Glyphs(x, y, text, scale, color);
        }

        void Glyphs(int x, int y, string text, int scale, Color32 color)
        {
            PixelFont.Draw(_pixels, Width, Height, x, y, text, scale, color);
        }
    }
}
