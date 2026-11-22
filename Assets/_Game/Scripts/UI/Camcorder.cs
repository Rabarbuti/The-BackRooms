using System;
using BackRooms.Entities;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using UnityEngine;

namespace BackRooms.UI
{
    /// <summary>
    /// Painel de filmadora (OSD): estado da gravação de verdade (F9: STBY parado, REC verde piscando
    /// com o tempo do clipe, SAVED e o nome do arquivo ao parar), contador da fita, bateria da
    /// lanterna em porcentagem, hora e data.
    /// É desenhado com a PixelFont (matriz 5x7) numa textura pequena, que o shader
    /// BackRooms/CamcorderVHS sobrepõe à imagem antes de aplicar os defeitos de fita —
    /// por isso o texto treme e borra junto com o resto, como numa gravação de verdade.
    /// Até 21/11/2026 o canto mostrava sempre REC vermelho em jogo (a filmadora de mentira) e a
    /// gravação real era só uma linha pequena embaixo: o jogador não sabia dizer se gravava.
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
        // No começo de cada fase, o pé do painel lembra onde estão as tarefas (menu de pausa).
        const float PauseHintSeconds = 8f;
        const string PauseHint = "ESC: TASKS";
        // Gravação de verdade (F9), no canto de cima, esquerda. Texto que o jogador lê: inglês.
        const string StandbyText = "STBY";
        const string RecText = "REC";
        const string RecHint = "R - REC";
        const string SavedText = "SAVED";
        const string FailedText = "REC FAILED";
        // Quanto tempo o SAVED fica na tela depois de parar, em segundos.
        const float SavedNoticeSeconds = 4f;
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
        const byte Green = 5;

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

            // Encarar o Capitão por muito tempo: o chuvisco cresce até tapar a imagem. É o que
            // obriga alguém do grupo a desviar o olhar (CaptainBrain).
            CaptainBrain captain = CaptainBrain.Captain;
            if (captain != null && body != null)
            {
                float stare = Mathf.InverseLerp(captain.stareNoiseAfter, captain.stareBlindAfter, captain.LocalStare);
                if (stare > 0f) Glitch(Mathf.Lerp(0.2f, 1f, stare), 0.6f);
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

            // Gravação de verdade (F9): REC com o tempo do clipe e, ao parar, SAVED e o nome do arquivo.
            string tape = null;
            ScreenRecorder recorder = ScreenRecorder.Instance;
            if (recorder != null && recorder.IsRecording)
            {
                int seconds = Mathf.FloorToInt(recorder.RecordedSeconds);
                tape = $"{RecText} {seconds / 60}:{seconds % 60:00}";
            }
            else if (recorder != null && recorder.SecondsSinceSaved < SavedNoticeSeconds)
            {
                tape = string.IsNullOrEmpty(recorder.LastError)
                    ? SavedText + "\n" + System.IO.Path.GetFileName(recorder.LastFile)
                    : FailedText;
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
                else if (ExitLock.Prompt != null) prompt = ExitLock.Prompt;
                else if (SwitchObjective.Prompt != null) prompt = SwitchObjective.Prompt;
                else if (BatteryPickups.Prompt != null) prompt = BatteryPickups.Prompt;
                else if (ElevatorControl.Prompt != null) prompt = ElevatorControl.Prompt;
                else if (BackRooms.Entities.LightSwitches.Prompt != null) prompt = BackRooms.Entities.LightSwitches.Prompt;
                else if (exit.LocalInReach && SwitchObjective.Locked) prompt = SwitchObjective.Instance.LockedText;
                else if (exit.LocalInReach && LevelExit.Locked) prompt = "LOCKED\nCODE ON KEYPAD";
                else if (exit.LocalInReach) prompt = waiting > 0 ? $"E - OPEN DOOR\n{waiting}/{total} WAITING" : "E - OPEN DOOR";
                else if (waiting > 0) footer = $"EXIT {waiting}/{total}";

                // Objetivo comum com pontos valendo: quantos e quanto falta da janela.
                if (footer == null && SwitchObjective.Instance != null) footer = SwitchObjective.Instance.Footer;
                if (footer == null && _recordSeconds < PauseHintSeconds && !PauseMenu.IsOpen) footer = PauseHint;
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

            // Esconderijo em móvel (Level 1): entrar, sair, agachar para a mesa.
            if (prompt == null && cover == null) prompt = HideSpotInput.Prompt;

            // Carteiras das salas de aula (Escritório): sentar e levantar.
            if (prompt == null && cover == null) prompt = ClassroomSeats.Prompt;

            // Dentro do guarda-roupa só se vê pela fresta da porta.
            bool slit = body != null && body.Hiding == HideKind.Wardrobe && !body.IsReturning;

            // Maré das Piscinas (PoolTide): a fase no alto da tela, em vermelho, piscando se ela pede.
            string tideText = BackRooms.World.PoolTide.HudText;
            TideText = tideText != null && (!BackRooms.World.PoolTide.HudBlink || Mathf.FloorToInt(Time.unscaledTime * 2f) % 2 == 0) ? tideText : null;

            Draw(recording, _recordSeconds, Time.unscaledTime, battery, lightOn, tool, _noSignal > 0f, tape, prompt, footer, cover, underwater, air, swimHint, slit);
        }

        /// <summary>Aviso da maré desenhado no alto da tela (já piscando); nulo = nada. Público para testes e prévias.</summary>
        public string TideText { get; set; }

        /// <summary>
        /// Desenha um quadro do painel e o publica para o shader. Público para a ferramenta
        /// de prévia do editor, que roda fora do Play.
        /// <paramref name="recording"/> diz que a fase está em jogo (sem gravar, STBY com a dica do F9);
        /// <paramref name="tape"/> é a gravação de verdade: nulo (parado), "REC m:ss" (gravando),
        /// "SAVED", quebra de linha e o arquivo, ou "REC FAILED". <paramref name="prompt"/> é o aviso no meio da tela (até duas linhas, separadas por
        /// quebra de linha), <paramref name="footer"/> o do pé, e <paramref name="cover"/>,
        /// quando informado, tapa a imagem com a tela preta e esse texto. <paramref name="swimHint"/>
        /// é a dica de tecla do nado, piscando abaixo da barra do fôlego. <paramref name="slit"/> tapa
        /// a imagem menos uma fresta vertical no meio (de dentro do guarda-roupa).
        /// </summary>
        public void Draw(bool recording, float recordSeconds, float clockSeconds, float battery, bool lightOn, string tool = null,
                         bool noSignal = false, string tape = null, string prompt = null, string footer = null, string cover = null,
                         bool underwater = false, float air = 1f, string swimHint = null, bool slit = false)
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
            int percent = BatteryPercent(battery);
            int airStep = air < 0.999f ? Mathf.RoundToInt(Mathf.Clamp01(air) * 40f) : -1;
            bool airBlink = air < 0.25f && Mathf.FloorToInt(clockSeconds * 4f) % 2 == 0;
            string state = $"{recording}|{blink}|{percent}|{lightOn}|{tool}|{noSignal}|{tape}|{prompt}|{footer}|{cover}|{time}|{date}|{underwater}|{airStep}|{airBlink}|{swimHint}|{slit}|{TideText}";

            if (state != _drawn)
            {
                _drawn = state;
                DrawPanel(recording, clockSeconds, battery, lightOn, tool, noSignal, tape, prompt, footer, cover, time, date, underwater, air, airBlink, slit,
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
                       bool underwater, float air, bool airBlink, bool slit, string swimHint)
        {
            // Sem sinal: o painel inteiro fica opaco e tapa a imagem do jogo.
            bool covered = noSignal || !string.IsNullOrEmpty(cover);
            if (covered)
            {
                Array.Fill(_pixels, Black);

                string message = string.IsNullOrEmpty(cover) ? "NO SIGNAL" : cover;
                Text((Width - MeasureText(message, Huge)) / 2, Height / 2 - 21, message, Huge, White);
            }
            else if (slit)
            {
                // Escuro de dentro do móvel, com a fresta entre as portas: um filete de imagem no
                // meio, um pouco mais largo embaixo (a porta empenada).
                Array.Fill(_pixels, Black);
                for (int y = 0; y < Height; y++)
                {
                    int half = Mathf.RoundToInt(Width * Mathf.Lerp(0.03f, 0.018f, y / (float)Height));
                    int from = Width / 2 - half, to = Width / 2 + half;
                    for (int x = from; x < to; x++) _pixels[y * Width + x] = default;
                }
            }
            else
            {
                Array.Clear(_pixels, 0, _pixels.Length);
            }

            DrawCorners();

            // Canto de cima, esquerda: a gravação de verdade (F9). Tem de ser óbvia à primeira vista.
            DrawRecording(recording, tape, clockSeconds);

            // Canto de cima, direita: bateria da lanterna. O contador da fita, logo abaixo, é da outra textura.
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

            // Maré (Piscinas): abaixo do fôlego, em vermelho.
            if (!covered && !string.IsNullOrEmpty(TideText))
                Text((Width - MeasureText(TideText, Medium)) / 2, Margin + 96, TideText, Medium, Red);
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

        /// <summary>Porcentagem que o painel mostra: arredonda para cima (0% só com a bateria vazia).</summary>
        public static int BatteryPercent(float level) => Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(level) * 100f - 0.001f), 0, 100);

        /// <summary>O que o canto da gravação mostra agora: STBY, REC, SAVED ou REC FAILED (testes).</summary>
        public string RecIndicator { get; private set; } = StandbyText;

        /// <summary>A bateria como está escrita no painel agora, "87%" (testes).</summary>
        public string BatteryText { get; private set; } = "";

        /// <summary>A bateria está piscando em vermelho (abaixo de <see cref="Flashlight.LowBattery01"/>; testes).</summary>
        public bool BatteryLow { get; private set; }

        // Parado: STBY e a dica do F9. Gravando: bolinha e REC verdes piscando e o tempo do clipe.
        // Parou: SAVED em verde e o nome do arquivo, por alguns segundos.
        void DrawRecording(bool inGame, string tape, float clockSeconds)
        {
            bool blink = Mathf.FloorToInt(clockSeconds * 2f) % 2 == 0;
            string[] lines = string.IsNullOrEmpty(tape) ? null : tape.Split('\n');
            string first = lines != null ? lines[0] : "";

            if (first.StartsWith(RecText + " ") || first.StartsWith("TAPE "))
            {
                RecIndicator = RecText;
                if (blink)
                {
                    Disc(Margin + 12, Margin + 14, 11, Green);
                    Text(Margin + 32, Margin, RecText, Large, Green);
                }
                string clip = first.Substring(first.IndexOf(' ') + 1);
                Text(Margin + 48 + MeasureText(RecText, Large), Margin + 4, clip, Medium, Green);
            }
            else if (first == SavedText)
            {
                RecIndicator = SavedText;
                Text(Margin, Margin, SavedText, Large, Green);
                if (lines.Length > 1) Text(Margin, Margin + 42, lines[1], Small, White);
            }
            else if (first == FailedText)
            {
                RecIndicator = FailedText;
                Text(Margin, Margin, FailedText, Large, Red);
            }
            else
            {
                RecIndicator = StandbyText;
                Text(Margin, Margin, StandbyText, Large, White);
                if (inGame) Text(Margin, Margin + 42, RecHint, Small, White);
            }
        }

        // Pilha com a porcentagem ao lado e a carga desenhada dentro. Abaixo de 20% pisca em vermelho.
        void DrawBattery(int x, int y, float level, float clockSeconds)
        {
            int percent = BatteryPercent(level);
            bool low = level < Flashlight.LowBattery01;
            BatteryText = percent + "%";
            BatteryLow = low;
            if (low && Mathf.FloorToInt(clockSeconds * 2f) % 2 == 1) return;

            byte color = low ? Red : White;
            const int w = 60, h = 28, wall = 4;

            TextRight(x - 12, y + 4, BatteryText, Medium, color);

            Box(x, y, w, wall, color);
            Box(x, y + h - wall, w, wall, color);
            Box(x, y, wall, h, color);
            Box(x + w - wall, y, wall, h, color);
            Box(x + w, y + 8, 6, 12, color);

            int fill = Mathf.RoundToInt((w - 2 * wall - 4) * Mathf.Clamp01(level));
            if (fill > 0) Box(x + wall + 2, y + wall + 2, fill, h - 2 * wall - 4, color);
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
