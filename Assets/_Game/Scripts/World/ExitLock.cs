using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BackRooms.World
{
    /// <summary>
    /// O objetivo do Level 1, "fechar o caixa" (level1-mostruario.md, seção 6): a porta de saída
    /// (a da doca) fica trancada por um teclado na parede, ao lado dela. O código tem três
    /// dígitos, sai da seed e está nas três etiquetas vermelhas de CLEARANCE espalhadas pela
    /// loja, cada uma com um dígito e a sua ordem ("1/3", "2/3", "3/3"). Vermelho com CLEARANCE é
    /// só delas: as outras placas da loja são de outra cor e dizem outra coisa. Até 21/11/2026 um
    /// bilhete na sala inicial trazia os três dígitos fora de ordem; saiu (ficava fácil demais).
    ///
    /// Mira no teclado da parede + E (o padrão de mira de <c>PenTool.TryAim</c>) abre o painel do
    /// código na tela (<see cref="CodePanel"/>), onde se digita pelo teclado ou pelo mouse; até
    /// 21/11/2026 era mirando tecla por tecla. O Enter manda o código para o host, que confere e, se
    /// estiver certo, destranca e avisa todos (<see cref="GameSession"/>, mensagens nomeadas); quem
    /// entra depois recebe o aviso junto com a seed. O cliente também confere o código pela própria
    /// seed, só para o visor responder na hora (vermelho ou aceito); quem destranca é o host.
    /// Trancada, a porta de saída não deixa esperar nela (<see cref="LevelExit"/>).
    /// </summary>
    public sealed class ExitLock : MonoBehaviour, ITaskSource, IAimSource
    {
        // Textos das tarefas do menu de pausa (a língua do jogo é o inglês).
        const string TagsTask = "Find the 3 red CLEARANCE tags (the door code)";
        const string TagsDetail = "Each tag has one digit and its place in the code (1/3, 2/3, 3/3).";
        const string KeypadTask = "Type the code on the keypad next to the exit door";
        const string KeypadDetail = "Aim at the keypad + E, type the digits and press Enter.";
        const string ExitTask = "Leave through the exit door";
        const string ExitLocked = "Locked until the code is in.";
        const string KeypadKey = "Aim + E";
        const string KeypadAction = "use the door keypad";
        // O aviso da mira no painel da filmadora.
        const string KeypadPrompt = "E - KEYPAD";
        const string OpenPrompt = "OPEN";

        // Altura do meio do teclado: perto da altura dos olhos (1,65 m em pé), fácil de mirar.
        const float KeypadHeight = 1.42f;
        // Texto pintado na etiqueta (PixelFont: só A-Z, 0-9 e : . , / - = ( )): até 10 letras.
        const string TagWord = "CLEARANCE";

        public static ExitLock Instance { get; private set; }

        /// <summary>Os três dígitos, na ordem.</summary>
        public int[] Digits { get; private set; } = new int[3];

        /// <summary>O código como número (dígitos na ordem).</summary>
        public int Code => Digits[0] * 100 + Digits[1] * 10 + Digits[2];

        public bool Unlocked { get; private set; }

        /// <summary>O que está digitado agora (para os testes).</summary>
        public string Typed => _typed;

        /// <summary>Teclas especiais de <see cref="Press"/>: apaga o último dígito e confirma.</summary>
        public const string EraseKey = "BS", EnterKey = "OK";

        /// <summary>O que o visor mostra: o aviso (ERR, OK, OPEN) ou os dígitos, com traço no que falta.</summary>
        public string Display => _status ?? _typed.PadRight(3, '-');

        /// <summary>O visor está no aviso de código errado.</summary>
        public bool ShowingError => _status == ErrorStatus;

        /// <summary>O código certo foi digitado aqui (esperando o host) ou a porta já destrancou.</summary>
        public bool Accepted => Unlocked || _sentRight;

        /// <summary>O que a tecla E faz agora no teclado, para o painel. Nulo se nada.</summary>
        public static string Prompt { get; private set; }

        /// <summary>Para os testes: aperta E mirando o teclado (abre o painel).</summary>
        public static bool PressForTest;

        /// <summary>
        /// Nesta partida a porta já foi destrancada (o aviso do host chegou). Vale para todo
        /// teclado montado depois: o cliente pode remontar a fase pela seed logo depois do aviso.
        /// O <c>GameSession</c> apaga ao receber uma seed nova ou ao hospedar.
        /// </summary>
        public static bool PendingUnlock;

        /// <summary>Quantas vezes o host recebeu um código errado.</summary>
        public int WrongCodes { get; private set; }

        public float reach = 2f;

        // O desenho da face (as teclas da parede são só enfeite: digita-se no painel da tela).
        static readonly string[] Keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "X", "0", "OK" };
        const string ErrorStatus = "ERR", SentStatus = "OK", OpenStatus = "OPEN";

        // Face do teclado: 64 x 96 pontos.
        const int FaceW = 64, FaceH = 96;

        Transform _keypad;
        Collider _keypadCollider;
        bool _sentRight;
        Texture2D _face;
        Color32[] _pixels;
        string _typed = "";
        string _status;
        float _statusUntil;
        InputAction _use;
        AudioSource _audio;
        AudioClip _beep, _buzz, _clunk, _accept;

        /// <summary>Monta o código pela seed. Chamado pela PropLayer, que põe as etiquetas.</summary>
        public void Init(int seed)
        {
            var rng = new System.Random(seed ^ 0x43414958); // "CAIX"
            for (int i = 0; i < 3; i++) Digits[i] = rng.Next(10);
        }

        void Awake()
        {
            Instance = this;
            if (Application.isPlaying) LevelTasks.Register(this);
            _use = new InputAction("Teclado", InputActionType.Button, "<Keyboard>/e");
            _use.AddBinding("<Gamepad>/buttonWest");
        }

        void OnEnable()
        {
            _use.Enable();
            if (Application.isPlaying) InteractAim.Register(this);
        }

        void OnDisable()
        {
            _use.Disable();
            InteractAim.Unregister(this);
            Prompt = null;
        }

        void OnDestroy()
        {
            _use.Dispose();
            LevelTasks.Unregister(this);
            if (Instance == this) Instance = null;
            if (_face != null) Destroy(_face);
        }

        // ---------------------------------------------------------------- Tarefas

        /// <summary>
        /// As tarefas do Level 1 para o menu de pausa. Ninguém registra as etiquetas achadas, então a
        /// das etiquetas só se marca feita com a porta destrancada.
        /// </summary>
        public void AddTasks(List<LevelTask> tasks)
        {
            tasks.Add(new LevelTask(TagsTask, Unlocked, Unlocked ? null : TagsDetail));
            tasks.Add(new LevelTask(KeypadTask, Unlocked, Unlocked ? null : KeypadDetail));
            tasks.Add(LevelTasks.ExitTask(ExitTask, !Unlocked, ExitLocked));
        }

        public void AddKeys(List<(string key, string action)> keys) => keys.Add((KeypadKey, KeypadAction));

        // ---------------------------------------------------------------- Teclado

        /// <summary>Prega o teclado na parede, à direita da porta de saída (no espaço da saída, -Z é a sala).</summary>
        public void BuildKeypad(Transform exitRoot, Material template)
        {
            var keypad = new GameObject("Teclado");
            keypad.transform.SetParent(exitRoot, false);
            keypad.transform.localPosition = new Vector3(0.95f, KeypadHeight, -0.03f);
            _keypad = keypad.transform;

            // A caixa cinza-escura, com a face desenhada. O colisor dela é o que a mira procura.
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Caixa";
            _keypadCollider = box.GetComponent<Collider>();
            box.transform.SetParent(keypad.transform, false);
            box.transform.localScale = new Vector3(0.2f, 0.3f, 0.05f);
            box.GetComponent<MeshRenderer>().sharedMaterial = Tinted(template, new Color(0.12f, 0.12f, 0.13f));

            _face = new Texture2D(FaceW, FaceH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "TecladoFace" };
            _pixels = new Color32[FaceW * FaceH];
            var front = GameObject.CreatePrimitive(PrimitiveType.Quad);
            front.name = "Face";
            DestroyCollider(front);
            front.transform.SetParent(keypad.transform, false);
            front.transform.localPosition = new Vector3(0f, 0f, -0.026f);
            front.transform.localScale = new Vector3(0.18f, 0.27f, 1f);
            var faceMaterial = new Material(template) { name = "TecladoFace" };
            faceMaterial.SetTexture("_BaseColorMap", _face);
            faceMaterial.SetColor("_BaseColor", Color.white);
            front.GetComponent<MeshRenderer>().sharedMaterial = faceMaterial;

            _audio = keypad.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f;
            _audio.minDistance = 1f;
            _audio.maxDistance = 14f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.playOnAwake = false;
            _beep = Tone("Bipe", 1320f, 0.07f, 0f);
            _buzz = Tone("Erro", 180f, 0.35f, 0.5f);
            _clunk = Tone("Destrava", 90f, 0.4f, 0.15f);
            _accept = Tone("Aceito", 1760f, 0.16f, 0f);

            Redraw();
            if (PendingUnlock) SetUnlocked();
        }

        // Centro de cada tecla na face, em pontos (origem em cima, à esquerda).
        static Vector2 KeyCenter(int col, int row) => new Vector2(12 + col * 20, 38 + row * 15);

        void Update()
        {
            if (_status != null && Time.time > _statusUntil)
            {
                _status = null;
                Redraw();
            }

            Prompt = null;
            PlayerNetwork local = PlayerNetwork.Local;
            if (_keypad == null || local == null || !local.InGame || local.controller.IsReturning || local.controller.InFurniture) return;
            if (CodePanel.IsOpen) return;

            bool pressed = (_use.WasPressedThisFrame() && !InputGate.Blocked) || PressForTest;
            PressForTest = false;

            if (InteractAim.Winner(this) < 0) return;
            if (Unlocked)
            {
                Prompt = OpenPrompt;
                return;
            }

            Prompt = KeypadPrompt;
            if (pressed)
            {
                Prompt = null;
                CodePanel.Open(this);
            }
        }

        // O teclado na mira (mira comum: InteractAim). Destrancada, ele não disputa a mira: o E
        // fica para a porta, ao lado.
        public bool AimCandidate(in InteractAim.View view, out int index, out float score)
        {
            index = -1;
            score = float.PositiveInfinity;
            PlayerController body = view.Local != null ? view.Local.controller : null;
            if (_keypad == null || Unlocked || CodePanel.IsOpen || body == null || body.IsReturning || body.InFurniture) return false;
            score = InteractAim.Score(view, _keypad, _keypad.position, reach);
            if (float.IsPositiveInfinity(score)) return false;
            index = 0;
            return true;
        }

        /// <summary>
        /// Aperta uma tecla do painel: "0" a "9", <see cref="EraseKey"/> apaga o último, "X" apaga
        /// tudo, <see cref="EnterKey"/> manda (só com os três dígitos). Público para os testes.
        /// </summary>
        public void Press(string key)
        {
            if (Accepted) return;
            // Digitar de novo tira o aviso de erro do visor.
            if (_status == ErrorStatus) _status = null;

            if (key == "X") { _typed = ""; Play(_beep); }
            else if (key == EraseKey) { if (_typed.Length > 0) _typed = _typed.Substring(0, _typed.Length - 1); Play(_beep); }
            else if (key == EnterKey)
            {
                if (_typed.Length < 3) { Play(_beep); Redraw(); return; }
                int code = int.Parse(_typed);
                _typed = "";
                GameSession session = GameSession.Instance;
                if (session != null && !session.IsHosting)
                {
                    // O host confere e destranca; aqui só o visor responde na hora.
                    session.SendExitCode(code);
                    if (code == Code)
                    {
                        _sentRight = true;
                        Play(_accept);
                        ShowStatus(SentStatus, 1e9f);
                    }
                    else WrongCode();
                }
                else
                {
                    if (code == Code) Play(_accept);
                    Submit(code);
                }
            }
            else if (_typed.Length < 3) { _typed += key; Play(_beep); }

            Redraw();
        }

        void WrongCode()
        {
            ShowStatus(ErrorStatus);
            Play(_buzz);
        }

        /// <summary>Host: confere um código digitado (aqui ou num cliente). Certo, destranca para todos.</summary>
        public void Submit(int code)
        {
            if (Unlocked) return;
            if (code != Code)
            {
                WrongCodes++;
                WrongCode();
                return;
            }

            SetUnlocked();
            GameSession.Instance?.BroadcastExitUnlocked();
        }

        /// <summary>Em todas as máquinas: a porta destrancou (o host conferiu o código).</summary>
        public void SetUnlocked()
        {
            if (Unlocked) return;
            Unlocked = true;
            _typed = "";
            Play(_clunk);
            ShowStatus(OpenStatus, 1e9f);
        }

        /// <summary>Host, só nos testes: destranca sem digitar.</summary>
        public void UnlockForTest() => Submit(Code);

        void ShowStatus(string status, float seconds = 1.5f)
        {
            _status = status;
            _statusUntil = Time.time + seconds;
            Redraw();
        }

        void Play(AudioClip clip)
        {
            if (_audio != null && clip != null && _audio.isActiveAndEnabled) _audio.PlayOneShot(clip, 0.8f);
        }

        void Redraw()
        {
            if (_face == null) return;
            var body = new Color32(58, 60, 64, 255);
            var key = new Color32(150, 152, 150, 255);
            var ink = new Color32(20, 20, 22, 255);
            var screen = new Color32(26, 40, 28, 255);
            var lit = Unlocked ? new Color32(110, 255, 120, 255) : new Color32(170, 230, 150, 255);
            var red = new Color32(255, 70, 50, 255);

            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = body;
            Fill(6, 6, FaceW - 12, 20, screen);
            string shown = Display;
            int scale = 2;
            PixelFont.Draw(_pixels, FaceW, FaceH, (FaceW - PixelFont.Measure(shown, scale)) / 2, 9, shown, scale,
                           ShowingError ? red : lit);

            for (int i = 0; i < Keys.Length; i++)
            {
                Vector2 c = KeyCenter(i % 3, i / 3);
                Fill((int)c.x - 8, (int)c.y - 6, 16, 12, key);
                string label = Keys[i];
                PixelFont.Draw(_pixels, FaceW, FaceH, (int)c.x - PixelFont.Measure(label, 1) / 2, (int)c.y - 3, label, 1, ink);
            }

            _face.SetPixels32(_pixels);
            _face.Apply(false);
        }

        // Retângulo com origem em cima, à esquerda (o vetor de pixels começa embaixo).
        void Fill(int x, int y, int w, int h, Color32 color)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    if (xx >= 0 && xx < FaceW && yy >= 0 && yy < FaceH)
                        _pixels[(FaceH - 1 - yy) * FaceW + xx] = color;
        }

        // ---------------------------------------------------------------- Etiquetas

        /// <summary>Textura de uma etiqueta vermelha de CLEARANCE: o dígito grande e a ordem.</summary>
        public static Texture2D TagTexture(int digit, int order)
        {
            const int w = 64, h = 40;
            var pixels = new Color32[w * h];
            var red = new Color32(196, 26, 22, 255);
            var white = new Color32(250, 246, 236, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = red;
            for (int x = 0; x < w; x++) { pixels[x] = white; pixels[(h - 1) * w + x] = white; }
            for (int y = 0; y < h; y++) { pixels[y * w] = white; pixels[y * w + w - 1] = white; }

            PixelFont.Draw(pixels, w, h, (w - PixelFont.Measure(TagWord, 1)) / 2, 2, TagWord, 1, white);
            string d = digit.ToString();
            PixelFont.Draw(pixels, w, h, 8, 13, d, 3, white);
            string o = $"{order}/3";
            PixelFont.Draw(pixels, w, h, w - 6 - PixelFont.Measure(o, 1), 28, o, 1, white);

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Etiqueta_" + order };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        // ---------------------------------------------------------------- Ajuda

        static Material Tinted(Material template, Color color)
        {
            var material = new Material(template);
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseColorMap", null);
            return material;
        }

        static void DestroyCollider(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c == null) return;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }

        static AudioClip Tone(string name, float frequency, float seconds, float roughness)
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int length = (int)(rate * seconds);
            var samples = new float[length];
            var rng = new System.Random(name.Length * 31 + (int)frequency);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Clamp01(t / 0.004f) * Mathf.Clamp01((seconds - t) / 0.03f);
                float wave = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * frequency * t)) * 0.4f + Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.4f;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * roughness;
                samples[i] = (wave + noise * 0.5f) * env * 0.6f;
            }
            var clip = AudioClip.Create(name, length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
