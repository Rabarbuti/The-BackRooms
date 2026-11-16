using System;
using BackRooms.Entities;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using UnityEngine;

namespace BackRooms.UI
{
    /// <summary>
    /// Painel de filmadora (OSD): REC piscando, contador da fita, bateria, hora e data.
    /// É desenhado com a PixelFont (matriz 5x7) numa textura pequena, que o shader
    /// BackRooms/CamcorderVHS sobrepõe à imagem antes de aplicar os defeitos de fita —
    /// por isso o texto treme e borra junto com o resto, como numa gravação de verdade.
    /// No menu mostra STBY (em espera); ao entrar no mapa, começa a gravar.
    /// A grade é de 1280 x 720: fina o bastante para ter três tamanhos de letra próximos um do
    /// outro (na grade antiga, de 480 x 270, só dava para dobrar ou reduzir à metade).
    /// Mandar uma textura desse tamanho para a placa de vídeo é o que custa, então: ela guarda
    /// um byte por ponto (índice de cor), o painel só é redesenhado quando alguma coisa nele muda
    /// (umas duas vezes por segundo), e o contador da fita, que muda 30 vezes por segundo, fica
    /// numa textura pequena à parte.
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
        [Tooltip("Chuvisco com a cabeça debaixo d'água (0 a 1).")]
        [Range(0f, 1f)] public float underwaterNoise = 0.45f;
        [Tooltip("Interferência máxima com a entidade colada no jogador (0 a 1).")]
        [Range(0f, 1f)] public float entityNoiseMax = 0.6f;

        const int Width = 1280;
        const int Height = 720;
        // A dica HOLD SPACE aparece nestes primeiros segundos de cada mergulho onde não dá pé.
        const float SwimHintSeconds = 4f;
        const int Margin = 54;

        // Tamanhos de letra, em pontos da grade por ponto da fonte 5x7. Numa tela de 1080
        // linhas dão 21, 32, 42 e 63 pixels de altura.
        const int Small = 2;
        const int Medium = 3;
        const int Large = 4;
        const int Huge = 6;

        // A textura do contador da fita e onde o canto de cima, esquerda dela fica na grade.
        const int FastWidth = 160;
        const int FastHeight = 16;
        const int FastX = Width - Margin - (FastWidth - 2);
        const int FastY = Margin + 42;

        static readonly int OsdId = Shader.PropertyToID("_BackRoomsOsd");
        static readonly int OsdOnId = Shader.PropertyToID("_BackRoomsOsdOn");
        static readonly int OsdFastId = Shader.PropertyToID("_BackRoomsOsdFast");
        static readonly int OsdFastRectId = Shader.PropertyToID("_BackRoomsOsdFastRect");
        static readonly int GlitchId = Shader.PropertyToID("_BackRoomsGlitch");

        // As texturas do painel guardam um índice de cor por ponto (um byte), não a cor: são
        // quatro vezes menos dados para mandar à placa de vídeo. As cores em si estão no shader
        // (OsdColor, em CamcorderVHS.shader), na mesma ordem.
        const byte White = 1;
        const byte Red = 2;
        const byte Shadow = 3;
        const byte Black = 4;

        static readonly string[] Months = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

        Texture2D _osd;
        byte[] _pixels;
        Texture2D _fast;
        byte[] _fastPixels;
        string _drawn;
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
            Shader.SetGlobalVector(OsdFastRectId, Vector4.zero);

            foreach (Texture2D texture in new[] { _osd, _fast })
            {
                if (texture == null) continue;
                if (Application.isPlaying) Destroy(texture);
                else DestroyImmediate(texture);
            }

            _osd = null;
            _fast = null;
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

            // Debaixo d'água a fita sofre: chuvisco forte enquanto a cabeça estiver submersa.
            if (body != null && body.IsUnderwater) Glitch(underwaterNoise, 0.4f);

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

            // Gravação de verdade (F9): mostra a fita rodando e, ao parar, que o vídeo foi salvo.
            string tape = null;
            ScreenRecorder recorder = ScreenRecorder.Instance;
            if (recorder != null && recorder.IsRecording)
            {
                int seconds = Mathf.FloorToInt(recorder.RecordedSeconds);
                tape = $"TAPE {seconds / 60}:{seconds % 60:00}";
            }
            else if (recorder != null && recorder.SecondsSinceSaved < 3f)
            {
                tape = string.IsNullOrEmpty(recorder.LastError) ? "SAVED" : "TAPE ERROR";
            }

            // Porta de saída: o que a tecla E faz agora, quem já está esperando e, quando ela
            // abre, a tela preta até a fase seguinte carregar.
            string prompt = null, footer = null, cover = null;
            LevelExit exit = LevelExit.Instance;

            if (session != null && session.Passing)
            {
                cover = LevelCatalog.Get(session.LevelIndex).Name + " CLEAR";
            }
            else if (recording && exit != null && PlayerNetwork.Local != null)
            {
                PlayerNetwork.CountAtExit(out int waiting, out int total);

                if (exit.LocalWaiting) prompt = total > 1 ? $"WAITING {waiting}/{total}\nE - LET GO" : "OPENING";
                else if (exit.LocalInReach) prompt = waiting > 0 ? $"E - OPEN DOOR\n{waiting}/{total} WAITING" : "E - OPEN DOOR";
                else if (waiting > 0) footer = $"EXIT {waiting}/{total}";
            }

            // Água (Piscinas): aviso e barra do fôlego.
            bool underwater = body != null && body.IsUnderwater;
            float air = body != null ? body.Air01 : 1f;

            // Onde não dá pé (poço) e com a cabeça debaixo d'água: a tecla para subir, piscando,
            // nos primeiros segundos de cada mergulho e de novo quando o fôlego está acabando.
            string swimHint = null;
            if (body != null && body.IsSwimming && underwater && !body.IsReturning && (body.UnderwaterSeconds < SwimHintSeconds || air < 0.35f))
                swimHint = "HOLD SPACE";

            // Nadando à tona de frente para uma borda baixa: dá para subir nela.
            if (prompt == null && cover == null && body != null && body.CanClimb && !body.IsClimbing && !body.IsReturning)
                prompt = "W - CLIMB";

            Draw(recording, _recordSeconds, Time.unscaledTime, battery, lightOn, tool, _noSignal > 0f, tape, prompt, footer, cover, underwater, air, swimHint);
        }

        /// <summary>
        /// Desenha um quadro do painel e o publica para o shader. Público para a ferramenta
        /// de prévia do editor, que roda fora do Play.
        /// <paramref name="prompt"/> é o aviso no meio da tela (até duas linhas, separadas por
        /// quebra de linha), <paramref name="footer"/> o do pé, e <paramref name="cover"/>,
        /// quando informado, tapa a imagem com a tela preta e esse texto. <paramref name="swimHint"/>
        /// é a dica de tecla do nado, piscando abaixo da barra do fôlego.
        /// </summary>
        public void Draw(bool recording, float recordSeconds, float clockSeconds, float battery, bool lightOn, string tool = null,
                         bool noSignal = false, string tape = null, string prompt = null, string footer = null, string cover = null,
                         bool underwater = false, float air = 1f, string swimHint = null)
        {
            EnsureTexture();

            // Canto de cima, direita, abaixo da bateria: o contador da fita, na textura pequena.
            int total = Mathf.FloorToInt(recordSeconds);
            int frames = Mathf.FloorToInt((recordSeconds - total) * 30f);
            string counter = $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}:{frames:00}";

            Array.Clear(_fastPixels, 0, _fastPixels.Length);
            int counterX = FastWidth - Small - PixelFont.Measure(counter, Small);
            PixelFont.Draw(_fastPixels, FastWidth, FastHeight, counterX + Small, Small, counter, Small, Shadow);
            PixelFont.Draw(_fastPixels, FastWidth, FastHeight, counterX, 0, counter, Small, White);
            _fast.SetPixelData(_fastPixels, 0);
            _fast.Apply(false);

            // Canto de baixo, direita: hora e data, como nas filmadoras dos anos 90.
            DateTime now = new DateTime(startYear, startMonth, startDay, startHour, startMinute, 0).AddSeconds(clockSeconds);
            int hour12 = now.Hour % 12 == 0 ? 12 : now.Hour % 12;
            string time = $"{(now.Hour < 12 ? "AM" : "PM")} {hour12}:{now.Minute:00}:{now.Second:00}";
            string date = $"{Months[now.Month - 1]} {now.Day} {now.Year}";

            // O resto só é redesenhado quando muda: a bolinha do REC e a bateria fraca piscam
            // duas vezes por segundo, o relógio anda uma vez por segundo.
            bool blink = Mathf.FloorToInt(clockSeconds * 2f) % 2 == 0;
            int bars = battery > 0.66f ? 3 : battery > 0.33f ? 2 : battery > 0.05f ? 1 : 0;
            int airStep = air < 0.999f ? Mathf.RoundToInt(Mathf.Clamp01(air) * 40f) : -1;
            bool airBlink = air < 0.25f && Mathf.FloorToInt(clockSeconds * 4f) % 2 == 0;
            string state = $"{recording}|{blink}|{bars}|{battery < 0.15f}|{lightOn}|{tool}|{noSignal}|{tape}|{prompt}|{footer}|{cover}|{time}|{date}|{underwater}|{airStep}|{airBlink}|{swimHint}";

            if (state != _drawn)
            {
                _drawn = state;
                DrawPanel(recording, clockSeconds, battery, lightOn, tool, noSignal, tape, prompt, footer, cover, time, date, underwater, air, airBlink,
                          blink ? swimHint : null);
                _osd.SetPixelData(_pixels, 0);
                _osd.Apply(false);
            }

            Shader.SetGlobalTexture(OsdId, _osd);
            Shader.SetGlobalTexture(OsdFastId, _fast);
            Shader.SetGlobalVector(OsdFastRectId, new Vector4(
                FastX / (float)Width, (Height - FastY - FastHeight) / (float)Height,
                Width / (float)FastWidth, Height / (float)FastHeight));
            Shader.SetGlobalFloat(OsdOnId, 1f);
        }

        void DrawPanel(bool recording, float clockSeconds, float battery, bool lightOn, string tool, bool noSignal,
                       string tape, string prompt, string footer, string cover, string time, string date,
                       bool underwater, float air, bool airBlink, string swimHint)
        {
            // Sem sinal: o painel inteiro fica opaco e tapa a imagem do jogo.
            bool covered = noSignal || !string.IsNullOrEmpty(cover);
            if (covered)
            {
                Array.Fill(_pixels, Black);

                string message = string.IsNullOrEmpty(cover) ? "NO SIGNAL" : cover;
                Text((Width - MeasureText(message, Huge)) / 2, Height / 2 - 21, message, Huge, White);
            }
            else
            {
                Array.Clear(_pixels, 0, _pixels.Length);
            }

            DrawCorners();

            // Canto de cima, esquerda: estado da gravação.
            if (recording)
            {
                bool dotOn = Mathf.FloorToInt(clockSeconds * 2f) % 2 == 0;
                if (dotOn) Disc(Margin + 12, Margin + 14, 11, Red);
                Text(Margin + 32, Margin, "REC", Large, Red);
                Text(Margin + 44 + MeasureText("REC", Large), Margin + 14, "SP", Small, White);
            }
            else
            {
                Text(Margin, Margin, "STBY", Large, White);
            }

            // Logo abaixo: a fita de verdade. Bolinha vermelha fixa enquanto grava em arquivo.
            if (!string.IsNullOrEmpty(tape))
            {
                bool rolling = tape.StartsWith("TAPE ") && tape != "TAPE ERROR";
                if (rolling) Disc(Margin + 6, Margin + 49, 5, Red);
                Text(Margin + (rolling ? 20 : 0), Margin + 42, tape, Small, White);
            }

            // Canto de cima, direita: bateria. O contador da fita, logo abaixo, é da outra textura.
            DrawBattery(Width - Margin - 66, Margin, battery, clockSeconds);

            // Canto de baixo, direita: hora e data.
            TextRight(Width - Margin, Height - Margin - 51, time, Medium, White);
            TextRight(Width - Margin, Height - Margin - 21, date, Medium, White);

            // Canto de baixo, esquerda: aviso da lanterna e, acima dele, o item na mão.
            if (lightOn) Text(Margin, Height - Margin - 14, "LIGHT", Small, White);

            if (!string.IsNullOrEmpty(tool) && !covered)
            {
                Text(Margin, Height - Margin - 38, tool, Small, White);

                // Mira: um ponto pequeno no centro, com contorno escuro.
                Box(Width / 2 - 4, Height / 2 - 4, 8, 8, Shadow);
                Box(Width / 2 - 2, Height / 2 - 2, 4, 4, White);
            }

            // Meio da tela, abaixo da mira: o aviso da porta. Pé da tela: quem já espera nela.
            if (!string.IsNullOrEmpty(prompt) && !covered)
            {
                string[] lines = prompt.Split('\n');
                Text((Width - MeasureText(lines[0], Medium)) / 2, 440, lines[0], Medium, White);
                if (lines.Length > 1) Text((Width - MeasureText(lines[1], Small)) / 2, 472, lines[1], Small, White);
            }

            if (!string.IsNullOrEmpty(footer) && !covered)
                Text((Width - MeasureText(footer, Small)) / 2, Height - Margin - 14, footer, Small, White);

            // Água (Piscinas): WATER no meio de cima e, faltando ar, a barra do fôlego embaixo.
            if (!covered && (underwater || air < 0.999f))
            {
                const int barWidth = 200;
                int x0 = (Width - barWidth) / 2;
                if (underwater) Text((Width - MeasureText("WATER", Medium)) / 2, Margin, "WATER", Medium, White);

                byte tone = airBlink ? Red : White;
                Text(x0 - MeasureText("AIR", Small) - 12, Margin + 40, "AIR", Small, tone);
                Box(x0, Margin + 40, barWidth, 16, Shadow);
                Box(x0 + 2, Margin + 42, Mathf.RoundToInt((barWidth - 4) * Mathf.Clamp01(air)), 12, tone);

                // Logo abaixo, onde não dá pé: a tecla para subir (já chega piscando, 2 vezes por segundo).
                if (!string.IsNullOrEmpty(swimHint))
                    Text((Width - MeasureText(swimHint, Small)) / 2, Margin + 66, swimHint, Small, White);
            }
        }

        void EnsureTexture()
        {
            if (_osd != null) return;

            _osd = new Texture2D(Width, Height, TextureFormat.R8, false, true)
            {
                name = "FilmadoraOSD",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _pixels = new byte[Width * Height];

            _fast = new Texture2D(FastWidth, FastHeight, TextureFormat.R8, false, true)
            {
                name = "FilmadoraContador",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _fastPixels = new byte[FastWidth * FastHeight];
            _drawn = null;
        }

        // ---------------------------------------------------------------- Desenho

        // Coordenadas com a origem no canto de cima, esquerda.
        void Plot(int x, int y, byte color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            _pixels[(Height - 1 - y) * Width + x] = color;
        }

        void Box(int x, int y, int w, int h, byte color)
        {
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                    Plot(x + i, y + j, color);
        }

        void Disc(int cx, int cy, int radius, byte color)
        {
            for (int j = -radius; j <= radius; j++)
                for (int i = -radius; i <= radius; i++)
                    if (i * i + j * j <= radius * radius + 1) Plot(cx + i, cy + j, color);
        }

        // Cantoneiras do visor.
        void DrawCorners()
        {
            const int inset = 24, length = 44, thick = 4;

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

            byte color = low ? Red : White;
            const int w = 60, h = 28, wall = 4;

            Box(x, y, w, wall, color);
            Box(x, y + h - wall, w, wall, color);
            Box(x, y, wall, h, color);
            Box(x + w - wall, y, wall, h, color);
            Box(x + w, y + 8, 6, 12, color);

            int bars = level > 0.66f ? 3 : level > 0.33f ? 2 : level > 0.05f ? 1 : 0;
            for (int i = 0; i < bars; i++) Box(x + 8 + i * 16, y + 8, 12, 12, color);
        }

        void TextRight(int right, int y, string text, int scale, byte color)
        {
            Text(right - MeasureText(text, scale), y, text, scale, color);
        }

        static int MeasureText(string text, int scale)
        {
            return PixelFont.Measure(text, scale);
        }

        // Texto com sombra de um pixel, para ler sobre qualquer fundo.
        void Text(int x, int y, string text, int scale, byte color)
        {
            Glyphs(x + scale, y + scale, text, scale, Shadow);
            Glyphs(x, y, text, scale, color);
        }

        void Glyphs(int x, int y, string text, int scale, byte color)
        {
            PixelFont.Draw(_pixels, Width, Height, x, y, text, scale, color);
        }
    }
}
