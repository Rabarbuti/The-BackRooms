using BackRooms.Net;
using BackRooms.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BackRooms.World
{
    /// <summary>
    /// O objetivo do Level 1, "fechar o caixa" (level1-mostruario.md, seção 6): a porta de saída
    /// (a da doca) fica trancada por um teclado na parede, ao lado dela. O código tem três
    /// dígitos, sai da seed e está nas três etiquetas vermelhas de LIQUIDAÇÃO espalhadas pela
    /// loja, cada uma com um dígito e a sua ordem ("1/3", "2/3", "3/3"). Um bilhete na sala
    /// inicial traz os três dígitos fora de ordem.
    ///
    /// O teclado se usa mirando e apertando E em cada tecla (o padrão de mira de
    /// <c>PenTool.TryAim</c>; as teclas 1, 2 e 3 do teclado são das canetas). O "OK" manda o
    /// código para o host, que confere e, se estiver certo, destranca e avisa todos
    /// (<see cref="GameSession"/>, mensagens nomeadas); quem entra depois recebe o aviso junto
    /// com a seed. Trancada, a porta de saída não deixa esperar nela (<see cref="LevelExit"/>).
    /// </summary>
    public sealed class ExitLock : MonoBehaviour
    {
        public static ExitLock Instance { get; private set; }

        /// <summary>Os três dígitos, na ordem.</summary>
        public int[] Digits { get; private set; } = new int[3];

        /// <summary>O código como número (dígitos na ordem).</summary>
        public int Code => Digits[0] * 100 + Digits[1] * 10 + Digits[2];

        public bool Unlocked { get; private set; }

        /// <summary>O que está digitado agora (para os testes).</summary>
        public string Typed => _typed;

        /// <summary>O que a tecla E faz agora no teclado, para o painel. Nulo se nada.</summary>
        public static string Prompt { get; private set; }

        /// <summary>Para os testes: aperta E na tecla mirada.</summary>
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

        static readonly RaycastHit[] Hits = new RaycastHit[16];
        static readonly string[] Keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "X", "0", "OK" };

        // Face do teclado: 64 x 96 pontos.
        const int FaceW = 64, FaceH = 96;

        Transform _keypad;
        Texture2D _face;
        Color32[] _pixels;
        string _typed = "";
        string _status;
        float _statusUntil;
        InputAction _use;
        AudioSource _audio;
        AudioClip _beep, _buzz, _clunk;

        /// <summary>Monta o código pela seed. Chamado pela PropLayer, que põe as etiquetas.</summary>
        public void Init(int seed)
        {
            var rng = new System.Random(seed ^ 0x43414958); // "CAIX"
            for (int i = 0; i < 3; i++) Digits[i] = rng.Next(10);
        }

        void Awake()
        {
            Instance = this;
            _use = new InputAction("Teclado", InputActionType.Button, "<Keyboard>/e");
            _use.AddBinding("<Gamepad>/buttonWest");
        }

        void OnEnable() => _use.Enable();
        void OnDisable() { _use.Disable(); Prompt = null; }

        void OnDestroy()
        {
            _use.Dispose();
            if (Instance == this) Instance = null;
            if (_face != null) Destroy(_face);
        }

        // ---------------------------------------------------------------- Teclado

        /// <summary>Prega o teclado na parede, à direita da porta de saída (no espaço da saída, -Z é a sala).</summary>
        public void BuildKeypad(Transform exitRoot, Material template)
        {
            var keypad = new GameObject("Teclado");
            keypad.transform.SetParent(exitRoot, false);
            keypad.transform.localPosition = new Vector3(0.95f, 1.3f, -0.03f);
            _keypad = keypad.transform;

            // A caixa cinza-escura, com a face desenhada.
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Caixa";
            DestroyCollider(box);
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

            // As teclas: uma caixa fina com colisor em cima de cada uma das 12 do desenho.
            for (int i = 0; i < Keys.Length; i++)
            {
                int col = i % 3, row = i / 3;
                var key = new GameObject("Tecla_" + Keys[i]);
                key.transform.SetParent(keypad.transform, false);
                Vector2 center = KeyCenter(col, row);
                key.transform.localPosition = new Vector3((center.x / FaceW - 0.5f) * 0.18f, (0.5f - center.y / FaceH) * 0.27f, -0.03f);
                var collider = key.AddComponent<BoxCollider>();
                collider.size = new Vector3(0.045f, 0.04f, 0.01f);
            }

            _audio = keypad.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f;
            _audio.minDistance = 1f;
            _audio.maxDistance = 14f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.playOnAwake = false;
            _beep = Tone("Bipe", 1320f, 0.07f, 0f);
            _buzz = Tone("Erro", 180f, 0.35f, 0.5f);
            _clunk = Tone("Destrava", 90f, 0.4f, 0.15f);

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

            bool pressed = _use.WasPressedThisFrame() || PressForTest;
            PressForTest = false;

            string key = Aimed(local);
            if (key == null) return;
            if (Unlocked)
            {
                Prompt = "OPEN";
                return;
            }

            Prompt = "E - " + (key == "OK" ? "ENTER" : key == "X" ? "CLEAR" : "KEY " + key);
            if (pressed) Press(key);
        }

        string Aimed(PlayerNetwork local)
        {
            Camera camera = local.playerCamera;
            if (camera == null) return null;
            Transform eye = camera.transform;
            int count = Physics.RaycastNonAlloc(eye.position, eye.forward, Hits, reach, ~0, QueryTriggerInteraction.Ignore);
            Transform self = local.transform.root;
            float best = float.MaxValue;
            Collider nearest = null;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.transform.root == self || Hits[i].distance >= best) continue;
                best = Hits[i].distance;
                nearest = Hits[i].collider;
            }
            if (nearest == null || nearest.transform.parent != _keypad || !nearest.name.StartsWith("Tecla_")) return null;
            return nearest.name.Substring("Tecla_".Length);
        }

        /// <summary>Aperta uma tecla ("0" a "9", "X" apaga, "OK" manda). Público para os testes.</summary>
        public void Press(string key)
        {
            if (Unlocked) return;
            Play(_beep);

            if (key == "X") _typed = "";
            else if (key == "OK")
            {
                if (_typed.Length == 3)
                {
                    int code = int.Parse(_typed);
                    GameSession session = GameSession.Instance;
                    if (session != null && !session.IsHosting) session.SendExitCode(code);
                    else Submit(code);
                }
                _typed = "";
            }
            else if (_typed.Length < 3) _typed += key;

            Redraw();
        }

        /// <summary>Host: confere um código digitado (aqui ou num cliente). Certo, destranca para todos.</summary>
        public void Submit(int code)
        {
            if (Unlocked) return;
            if (code != Code)
            {
                WrongCodes++;
                ShowStatus("ERR");
                Play(_buzz);
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
            Play(_clunk);
            ShowStatus("OPEN", 1e9f);
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
            string shown = _status ?? (_typed.PadRight(3, '-'));
            int scale = 2;
            PixelFont.Draw(_pixels, FaceW, FaceH, (FaceW - PixelFont.Measure(shown, scale)) / 2, 9, shown, scale,
                           _status == "ERR" ? red : lit);

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

        // ---------------------------------------------------------------- Etiquetas e bilhete

        /// <summary>Textura de uma etiqueta vermelha de LIQUIDAÇÃO: o dígito grande e a ordem.</summary>
        public static Texture2D TagTexture(int digit, int order)
        {
            const int w = 64, h = 40;
            var pixels = new Color32[w * h];
            var red = new Color32(196, 26, 22, 255);
            var white = new Color32(250, 246, 236, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = red;
            for (int x = 0; x < w; x++) { pixels[x] = white; pixels[(h - 1) * w + x] = white; }
            for (int y = 0; y < h; y++) { pixels[y * w] = white; pixels[y * w + w - 1] = white; }

            PixelFont.Draw(pixels, w, h, (w - PixelFont.Measure("LIQUIDACAO", 1)) / 2, 2, "LIQUIDACAO", 1, white);
            string d = digit.ToString();
            PixelFont.Draw(pixels, w, h, 8, 13, d, 3, white);
            string o = $"{order}/3";
            PixelFont.Draw(pixels, w, h, w - 6 - PixelFont.Measure(o, 1), 28, o, 1, white);

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Etiqueta_" + order };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        /// <summary>Textura do bilhete da sala inicial: os três dígitos, fora de ordem.</summary>
        public static Texture2D NoteTexture(int[] digits, int seed)
        {
            const int w = 64, h = 48;
            var pixels = new Color32[w * h];
            var paper = new Color32(236, 228, 196, 255);
            var ink = new Color32(30, 34, 90, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = paper;

            // Fora de ordem, mas sempre do mesmo jeito para a mesma seed.
            var order = new[] { 0, 1, 2 };
            var rng = new System.Random(seed ^ 0x4E4F5441); // "NOTA"
            for (int i = 2; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }

            PixelFont.Draw(pixels, w, h, 4, 4, "DOCA:", 1, ink);
            string code = $"{digits[order[0]]} {digits[order[1]]} {digits[order[2]]}";
            PixelFont.Draw(pixels, w, h, (w - PixelFont.Measure(code, 2)) / 2, 17, code, 2, ink);
            PixelFont.Draw(pixels, w, h, 4, 36, "FORA DE ORDEM", 1, ink);

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Bilhete" };
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
