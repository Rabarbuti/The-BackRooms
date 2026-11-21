using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BackRooms.Entities
{
    /// <summary>
    /// Os interruptores de luz do Escritório: um por trecho reto de corredor da onda de luzes
    /// (<see cref="OfficeLightWave"/>), na parede de dentro do trecho. Mira + E religa o trecho na hora
    /// (o Zelador dali some), com um estalo alto que chama quem ouve (<see cref="WorldNoise"/>); depois
    /// o interruptor recarrega (a luzinha fica vermelha e a mira mostra quanto falta). Com o alarme de
    /// incêndio disparado, não funcionam mais.
    ///
    /// Postos em cada máquina depois de a fase montar, com sorteio próprio pela seed (não mexem no hash
    /// da planta) e a parede achada por raio a partir do meio da célula: todos ficam no mesmo lugar.
    /// Quem decide é o host (<see cref="JanitorBrain.HostFlip"/>); o estado (hora de pronto de cada um)
    /// vai pela rede no cérebro do Zelador.
    /// </summary>
    public sealed class LightSwitches : MonoBehaviour, ITaskSource
    {
        // Textos que o jogador lê (a língua do jogo é o inglês).
        const string FlipText = "E - LIGHTS ON";
        const string LitText = "LIGHT SWITCH\nLIGHTS ARE ON";
        const string ResetText = "LIGHT SWITCH\nRESETTING {0}";
        const string NoPowerText = "LIGHT SWITCH\nNO POWER";
        const string KeyName = "Aim + E";
        const string KeyAction = "light switch: turns a dark corridor back on (loud, then it resets)";
        const string TipKey = "TIP";
        const string TipText = "when the lights flicker, get out: he only walks in the dark";

        const float MountHeight = 1.25f;
        static readonly Vector3 PlateSize = new Vector3(0.09f, 0.13f, 0.014f);

        public static LightSwitches Instance { get; private set; }

        /// <summary>O que a tecla E faz agora, para a filmadora. Nulo se não mira nenhum interruptor.</summary>
        public static string Prompt { get; private set; }

        /// <summary>Para os testes: aperta E no interruptor mirado.</summary>
        public static bool PressForTest;

        public float reach = 2.2f;

        /// <summary>Quantas vezes um interruptor estalou nesta máquina (para os testes).</summary>
        public int Clacks { get; private set; }

        /// <summary>O trecho do interruptor mirado agora pelo jogador desta máquina (-1 nenhum).</summary>
        public int Aimed { get; private set; } = -1;

        sealed class Switch
        {
            public Transform Root, Lever;
            public Renderer Led;
            public Collider Collider;
            public Vector3 Normal;
        }

        static readonly RaycastHit[] Hits = new RaycastHit[16];
        static AudioClip _clack;

        Switch[] _byRun = new Switch[0];
        Material _ledOn, _ledOff;
        InputAction _use;
        float _sentAt = -10f;

        /// <summary>Quantos trechos ganharam interruptor.</summary>
        public int Count
        {
            get
            {
                int n = 0;
                foreach (Switch s in _byRun) if (s != null) n++;
                return n;
            }
        }

        public bool Has(int run) => run >= 0 && run < _byRun.Length && _byRun[run] != null;

        /// <summary>Onde fica o interruptor do trecho (no mundo).</summary>
        public Vector3 PositionOf(int run) => Has(run) ? _byRun[run].Root.position : Vector3.zero;

        /// <summary>Para onde ele olha (para fora da parede).</summary>
        public Vector3 NormalOf(int run) => Has(run) ? _byRun[run].Normal : Vector3.forward;

        // ---------------------------------------------------------------- Montagem

        /// <summary>
        /// Põe os interruptores da fase (um por trecho da onda, quando acha parede livre). Serve também
        /// fora do Play (as fotos). Devolve o componente, filho do nível.
        /// </summary>
        public static LightSwitches Build(LevelBuilder level, OfficeLightWave wave)
        {
            if (level == null || wave == null || level.Data == null) return null;
            Physics.SyncTransforms();

            var go = new GameObject("Interruptores");
            go.transform.SetParent(level.transform, false);
            var switches = go.AddComponent<LightSwitches>();
            switches.Place(level, wave);
            return switches;
        }

        void Place(LevelBuilder level, OfficeLightWave wave)
        {
            Material plate = level.office != null ? level.office.frameWhite : null;
            Material dark = level.office != null ? level.office.darkMetal : null;
            _ledOn = level.office != null ? level.office.ledGreen : null;
            _ledOff = level.office != null ? level.office.ledRed : null;

            _byRun = new Switch[wave.RunCount];
            // Sorteio próprio, depois de tudo: não muda nada do que a planta e os outros sorteios fazem.
            var rng = new System.Random(unchecked(level.Data.Seed * 977 + 61));
            Transform root = level.transform;

            for (int r = 0; r < wave.RunCount; r++)
            {
                IReadOnlyList<CeilingLight> lamps = wave.RunLamps(r);
                if (lamps.Count < 2 || lamps[0] == null || lamps[lamps.Count - 1] == null) continue;
                Vector3 axis = lamps[lamps.Count - 1].transform.position - lamps[0].transform.position;
                axis.y = 0f;
                axis.Normalize();
                Vector3 across = Vector3.Cross(Vector3.up, axis);

                // As luminárias do meio primeiro; depois as outras, em ordem sorteada.
                var order = new List<int>();
                for (int i = 1; i < lamps.Count - 1; i++) order.Add(i);
                for (int i = order.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }

                bool sideFirst = rng.Next(2) == 0;
                foreach (int i in order)
                {
                    if (lamps[i] == null) continue;
                    Vector3 center = lamps[i].transform.position;
                    center.y = root.position.y + MountHeight;
                    Vector3 local = root.InverseTransformPoint(center);
                    int cx = Mathf.FloorToInt(local.x / LevelBuilder.Cell), cz = Mathf.FloorToInt(local.z / LevelBuilder.Cell);
                    if (TryMount(r, center, sideFirst ? across : -across, axis, root, level.Data, cx, cz, plate, dark) ||
                        TryMount(r, center, sideFirst ? -across : across, axis, root, level.Data, cx, cz, plate, dark)) break;
                }
            }
        }

        // Raio do meio da célula até a parede (lisa: a normal volta para o meio) e uma caixa livre na
        // frente dela (nada de armário, extintor, alarme ou porta em cima).
        bool TryMount(int run, Vector3 center, Vector3 side, Vector3 axis, Transform root, LevelData data, int cx, int cz,
                      Material plate, Material dark)
        {
            // Só em parede de alvenaria (com ou sem porta): nada de vidro, janela ou porta de elevador.
            Vector3 dir = root.InverseTransformDirection(side);
            WallSide wall_ = Mathf.Abs(dir.x) > Mathf.Abs(dir.z) ? (dir.x > 0f ? WallSide.East : WallSide.West)
                                                                 : (dir.z > 0f ? WallSide.North : WallSide.South);
            if (data.HasOffice && cx >= 0 && cz >= 0 && cx < data.Width && cz < data.Height)
            {
                OfficeEdge edge = data.OfficeEdgeAt(cx, cz, wall_);
                if (edge != OfficeEdge.Wall && edge != OfficeEdge.Door) return false;
            }

            foreach (float along in new[] { 0f, 0.9f, -0.9f })
            {
                Vector3 from = center + axis * along;
                int count = Physics.RaycastNonAlloc(from, side, Hits, 2.4f, ~0, QueryTriggerInteraction.Ignore);
                RaycastHit wall = default;
                float best = float.MaxValue;
                for (int h = 0; h < count; h++)
                {
                    if (!Fixed(Hits[h].collider, root) || Hits[h].distance >= best) continue;
                    best = Hits[h].distance;
                    wall = Hits[h];
                }
                if (best == float.MaxValue || Vector3.Dot(wall.normal, -side) < 0.95f) continue;

                Vector3 normal = wall.normal;
                Quaternion rotation = Quaternion.LookRotation(-normal, Vector3.up);
                Vector3 box = wall.point + normal * 0.08f;
                if (Blocked(box, new Vector3(0.12f, 0.2f, 0.06f), rotation, root)) continue;

                _byRun[run] = Create(run, wall.point + normal * (PlateSize.z * 0.5f), normal, rotation, plate, dark);
                return true;
            }
            return false;
        }

        static readonly Collider[] Overlaps = new Collider[16];

        // Alguma peça fixa da fase na caixa? Jogadores, inimigos e o que se mexe não contam.
        static bool Blocked(Vector3 center, Vector3 half, Quaternion rotation, Transform root)
        {
            int count = Physics.OverlapBoxNonAlloc(center, half, Overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Fixed(Overlaps[i], root)) return true;
            return false;
        }

        /// <summary>
        /// Colisor de geometria fixa da fase: filho do nível, sem corpo rígido e fora do que se mexe
        /// ou some (porta de elevador, porta de armário que bate, pilhas, os próprios interruptores).
        /// Assim a conta dá o mesmo em todas as máquinas, a qualquer hora.
        /// </summary>
        static bool Fixed(Collider c, Transform root)
        {
            if (c == null || c.attachedRigidbody != null || !c.transform.IsChildOf(root)) return false;
            for (Transform t = c.transform; t != null && t != root; t = t.parent)
            {
                if (t.name == "Elevadores" || t.name.StartsWith("Pilhas")) return false;
                if (t.GetComponent<LockerDoor>() != null || t.GetComponent<ElevatorDoors>() != null ||
                    t.GetComponent<BatteryPickups>() != null || t.GetComponent<LightSwitches>() != null) return false;
            }
            return true;
        }

        /// <summary>Resumo das posições (centímetros) de todos os interruptores: tem de bater entre as máquinas.</summary>
        public int Hash
        {
            get
            {
                unchecked
                {
                    int h = 17;
                    for (int r = 0; r < _byRun.Length; r++)
                    {
                        if (_byRun[r] == null)
                        {
                            h = h * 31 + r;
                            continue;
                        }
                        Vector3 p = _byRun[r].Root.localPosition;
                        h = h * 31 + Mathf.RoundToInt(p.x * 100f);
                        h = h * 31 + Mathf.RoundToInt(p.y * 100f);
                        h = h * 31 + Mathf.RoundToInt(p.z * 100f);
                    }
                    return h == 0 ? 1 : h;
                }
            }
        }

        Switch Create(int run, Vector3 at, Vector3 normal, Quaternion rotation, Material plate, Material dark)
        {
            var root = new GameObject($"Interruptor_{run}").transform;
            root.SetParent(transform, true);
            root.SetPositionAndRotation(at, rotation);

            // Espelho com o colisor do mesmo tamanho (nascem juntos), a alavanca e a luzinha.
            GameObject face = Piece(root, "Espelho", Vector3.zero, PlateSize, plate);
            var collider = face.AddComponent<BoxCollider>();
            collider.size = Vector3.one;
            Transform lever = Piece(root, "Alavanca", new Vector3(0f, 0f, -0.012f), new Vector3(0.018f, 0.04f, 0.018f), dark).transform;
            GameObject led = Piece(root, "Luz", new Vector3(0f, 0.048f, -0.009f), new Vector3(0.012f, 0.012f, 0.006f), _ledOn);

            return new Switch { Root = root, Lever = lever, Led = led.GetComponent<Renderer>(), Collider = collider, Normal = normal };
        }

        static GameObject Piece(Transform parent, string name, Vector3 local, Vector3 size, Material material)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = name;
            Object.DestroyImmediate(piece.GetComponent<Collider>());
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = local;
            piece.transform.localScale = size;
            var renderer = piece.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (material != null) renderer.sharedMaterial = material;
            return piece;
        }

        // ---------------------------------------------------------------- Em jogo

        void Awake()
        {
            _use = new InputAction("Interruptor", InputActionType.Button, "<Keyboard>/e");
            _use.AddBinding("<Gamepad>/buttonWest");
        }

        void OnEnable()
        {
            Instance = this;
            _use?.Enable();
            if (Application.isPlaying) LevelTasks.Register(this);
        }

        void OnDisable()
        {
            _use?.Disable();
            LevelTasks.Unregister(this);
            if (Instance == this)
            {
                Instance = null;
                Prompt = null;
            }
        }

        void OnDestroy() => _use?.Dispose();

        void Update()
        {
            if (!Application.isPlaying) return;
            JanitorBrain brain = JanitorBrain.Janitor;
            double t = OfficeElevators.Clock();
            bool alarm = AlarmOn();

            // A alavanca e a luzinha de cada um: verde pronto; vermelho recarregando ou sem energia.
            for (int r = 0; r < _byRun.Length; r++)
            {
                Switch s = _byRun[r];
                if (s == null) continue;
                bool resetting = brain != null && t < brain.SwitchReadyAt(r);
                if (s.Led != null) s.Led.sharedMaterial = alarm || resetting ? _ledOff : _ledOn;
                s.Lever.localRotation = Quaternion.Euler(resetting ? 25f : -25f, 0f, 0f);
            }

            PlayerNetwork local = PlayerNetwork.Local;
            Aimed = local != null && local.InGame ? Aim(local) : -1;
            Prompt = Aimed >= 0 ? PromptFor(Aimed, brain, t, alarm) : null;

            bool pressed = PressForTest || (_use != null && _use.WasPressedThisFrame());
            PressForTest = false;
            if (!pressed || Aimed < 0 || brain == null || Time.time - _sentAt < 0.3f) return;
            _sentAt = Time.time;
            brain.RequestFlip(Aimed, local.transform.position);
        }

        string PromptFor(int run, JanitorBrain brain, double t, bool alarm)
        {
            if (alarm) return NoPowerText;
            double ready = brain != null ? brain.SwitchReadyAt(run) : 0.0;
            if (t < ready) return string.Format(ResetText, Mathf.CeilToInt((float)(ready - t)));
            if (brain == null || !brain.RunActive(run, t)) return LitText;
            return FlipText;
        }

        // O interruptor na mira do jogador, ignorando o próprio jogador (o padrão de PenTool.TryAim).
        int Aim(PlayerNetwork local)
        {
            Camera camera = local.playerCamera;
            if (camera == null) return -1;
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
            if (nearest == null) return -1;
            for (int r = 0; r < _byRun.Length; r++)
                if (_byRun[r] != null && _byRun[r].Collider == nearest) return r;
            return -1;
        }

        /// <summary>Em todas as máquinas: o interruptor do trecho foi usado. Estalo alto e barulho no mundo.</summary>
        public void Flipped(int run, float radius)
        {
            if (!Has(run)) return;
            Vector3 at = PositionOf(run);
            Clacks++;
            AudioSource.PlayClipAtPoint(Clack, at, 1f);
            WorldNoise.Emit(at, radius);
        }

        static bool AlarmOn()
        {
            SwitchObjective objective = SwitchObjective.Instance;
            return objective != null && objective.Kind == SwitchKind.FireAlarm && objective.Complete;
        }

        // ---------------------------------------------------------------- Menu de pausa

        public void AddTasks(List<LevelTask> tasks) { }

        public void AddKeys(List<(string key, string action)> keys)
        {
            keys.Add((KeyName, KeyAction));
            keys.Add((TipKey, TipText));
        }

        // ---------------------------------------------------------------- Som

        /// <summary>O estalo do interruptor: um "TAC" seco e alto de plástico duro com o relé batendo.</summary>
        public static AudioClip Clack => _clack != null ? _clack : (_clack = CreateClack());

        public static AudioClip CreateClack()
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int length = (int)(rate * 0.5f);
            var samples = new float[length];
            var rng = new System.Random(77);
            float peak = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float snap = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t / 0.003f);
                float body = Mathf.Sin(2f * Mathf.PI * 1800f * t) * Mathf.Exp(-t / 0.012f) * 0.6f;
                // O relé do quadro, um instante depois, e o "tunc" das luminárias acendendo.
                float u = t - 0.09f;
                float relay = u > 0f ? Mathf.Sin(2f * Mathf.PI * 140f * u) * Mathf.Exp(-u / 0.06f) : 0f;
                samples[i] = snap + body + relay * 0.8f;
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }
            for (int i = 0; i < length; i++) samples[i] *= 0.95f / Mathf.Max(peak, 1e-5f);
            var clip = AudioClip.Create("Interruptor_Estalo", length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
