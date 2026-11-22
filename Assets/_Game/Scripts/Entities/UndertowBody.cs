using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.World;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O corpo do Undertow, o inimigo da água funda das Piscinas (<see cref="UndertowBrain"/>).
    /// Um afogado que a água esticou: 3,4 m da cabeça à ponta da cauda, nadando de bruços. Tronco
    /// de gente, magro, de costelas e escápulas marcadas; no lugar das pernas, uma cauda comprida
    /// de vértebras, achatada na ponta; braços compridos demais (1,5 m) de dedos longos. A cabeça
    /// é careca, inchada, de olhos leitosos fundos e boca sem lábios. As costas são escuras e
    /// manchadas (de cima, através da água, ele é uma sombra comprida com dois braços abertos); a
    /// barriga, o rosto e as mãos são brancos, inchados de água (de baixo, contra a claraboia, uma
    /// silhueta clara).
    ///
    /// A pose sai de <see cref="Pose"/> (a ferramenta de prévia fotografa fora do Play): onda de
    /// lado ao longo da cauda (mais forte e mais rápida quanto mais depressa nada), os braços
    /// arrastados para trás ao deslizar, braçadas de peito ao caçar, os braços para a frente e as
    /// mãos fechando ao pegar, e o corpo enrolado no fundo, dormindo. A pose de encaixe (onde o
    /// <see cref="Build"/> termina e onde o Blender esculpe) é o corpo esticado de bruços, os braços
    /// abertos a 65° para a frente.
    ///
    /// O corpo inclina (nariz para cima ou para baixo) pela velocidade vertical, calculada aqui pela
    /// posição que chega da rede. Some nos canos (estado Travel, depois de afundar).
    ///
    /// Encaixe na fase (<see cref="Fit"/>, em todas as máquinas, a cada quadro): os braços abertos
    /// medem ~2,9 m de ponta a ponta e a braçada desce as mãos ~0,7 m; onde um braço bateria em
    /// parede, escada, coluna ou piso (um vão, o seco alagado de 1,12 m), ele se recolhe ao longo do
    /// corpo, na hora (e solta devagar); num vão de porta, cola-se ao flanco (espremido). A
    /// inclinação e a onda da cauda também cedem onde a ponta bateria, e a ponta da cauda se curva
    /// para o lado livre numa curva de corredor. O meio do corpo e a cabeça quem guarda é o
    /// <see cref="UndertowBrain"/>.
    /// </summary>
    public sealed class UndertowBody : MonoBehaviour
    {
        [Tooltip("Pele inchada (barriga, rosto, mãos): o corpo de código, sem modelo.")]
        public Material paleMaterial;
        [Tooltip("Costas escuras e manchadas: o corpo de código.")]
        public Material backMaterial;
        [Tooltip("Bolhas que sobem dele.")]
        public Material bubbleMaterial;

        [Header("Modelo do Blender")]
        [Tooltip("Malha esculpida no Blender (Tools/Blender/personagens/afogado.py), vestida sobre as juntas. Vazio: o corpo de código.")]
        public TextAsset model;
        public Material[] modelMaterials;

        /// <summary>Só para a ferramenta que exporta o esqueleto: monta na pose de encaixe e não tira peça nenhuma.</summary>
        public static bool KeepAllJoints;

        /// <summary>Velocidade no plano agora (pela posição), em m/s.</summary>
        public float Speed => _speed;

        /// <summary>Verdadeiro quando o corpo está à vista (fora dos canos).</summary>
        public bool Visible { get; private set; } = true;

        /// <summary>Quantas bolhas já subiram (para os testes).</summary>
        public int BubblesReleased { get; private set; }

        // Medidas (m), da bacia para a frente e para trás.
        const float PelvisZ = -0.30f;
        const float LowerBack = 0.38f, Chest = 0.40f, Shoulders = 0.30f, Neck = 0.14f, HeadUp = 0.18f;
        static readonly float[] TailSegments = { 0.32f, 0.38f, 0.38f, 0.34f };
        const float UpperArm = 0.62f, Forearm = 0.56f, Palm = 0.13f;
        const float ShoulderWidth = 0.26f;
        /// <summary>Ângulo dos braços na pose de encaixe, para a frente e para fora.</summary>
        const float RestArmYaw = 65f, RestArmPitch = 10f;
        /// <summary>Braço espremido (recolhido além de 1): quase reto para trás, colado ao flanco.</summary>
        const float SqueezeArmYaw = 177f;
        /// <summary>Curva de cada junta da cauda (graus) quando a ponta bateria numa parede (numa curva de corredor).</summary>
        const float TailCurl = 18f;

        readonly HashSet<Transform> _live = new HashSet<Transform>();
        readonly Dictionary<Transform, Quaternion> _rest = new Dictionary<Transform, Quaternion>();

        Transform _rig, _pelvis, _lowerBack, _chest, _shoulders, _neck, _head, _jaw;
        readonly Transform[] _tail = new Transform[4];
        readonly Transform[] _arm = new Transform[2];
        readonly Transform[] _forearm = new Transform[2];
        readonly Transform[] _hand = new Transform[2];
        readonly Transform[] _fingers = new Transform[2];

        // O último braço que a pose pediu (antes de recolher) e quanto cada um está recolhido (0 a 1).
        readonly float[] _armYaw = new float[2], _armPitch = new float[2], _armElbow = new float[2];
        readonly float[] _handX = new float[2], _handY = new float[2], _fingersX = new float[2];
        readonly float[] _tuck = new float[2];
        // Para que lado a cauda se curvou da última vez (tenta o mesmo antes: não fica trocando).
        float _curlSide = 1f;
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>Quanto cada braço está recolhido agora (0 aberto, 1 ao longo do corpo, 2 colado ao flanco, espremido).</summary>
        public float Tuck(int arm) => _tuck[arm];

        UndertowBrain _brain;
        Renderer[] _renderers;
        bool _built;
        Vector3 _lastPosition;
        float _speed, _vertical, _phase, _stroke;
        float _sleep, _grab, _hunt, _jawOpen;
        float _travelSince = -1f;

        // Bolhas: poucas esferas reaproveitadas, que sobem até a superfície e somem.
        const int BubbleCount = 24;
        Transform[] _bubbles;
        Vector3[] _bubbleVelocity;
        float[] _bubbleLife;
        float _bubbleDebt;
        int _bubbleNext;

        void Awake()
        {
            Build();
            _brain = GetComponent<UndertowBrain>();
            _lastPosition = transform.position;
            _renderers = GetComponentsInChildren<Renderer>(true);
            BuildBubbles();
        }

        void OnEnable()
        {
            if (_brain == null) _brain = GetComponent<UndertowBrain>();
            if (_brain != null) _brain.StateChanged += OnStateChanged;
        }

        void OnDisable()
        {
            if (_brain != null) _brain.StateChanged -= OnStateChanged;
        }

        void OnStateChanged(UndertowState state)
        {
            _travelSince = state == UndertowState.Travel ? Time.time : -1f;
            // Acordando ou surgindo de um cano: uma nuvem de bolhas.
            if (state == UndertowState.Waking) Burst(14);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 delta = transform.position - _lastPosition;
            _lastPosition = transform.position;
            bool jump = delta.magnitude > 3f;
            float vertical = jump ? 0f : delta.y / dt;
            delta.y = 0f;
            float speed = jump ? 0f : delta.magnitude / dt;
            _speed = Mathf.Lerp(_speed, Mathf.Min(speed, 8f), 1f - Mathf.Exp(-6f * dt));
            _vertical = Mathf.Lerp(_vertical, Mathf.Clamp(vertical, -3f, 3f), 1f - Mathf.Exp(-4f * dt));

            UndertowState state = _brain != null && _brain.IsSpawned ? _brain.State : UndertowState.Investigate;
            float diveSeconds = _brain != null ? _brain.diveSeconds : 1.4f;
            bool hidden = state == UndertowState.Travel && _travelSince >= 0f && Time.time - _travelSince > diveSeconds;
            SetVisible(!hidden);

            float rate = 1f - Mathf.Exp(-3f * dt);
            _sleep = Mathf.Lerp(_sleep, state == UndertowState.Asleep ? 1f : 0f, 1f - Mathf.Exp(-1.2f * dt));
            _grab = Mathf.Lerp(_grab, state == UndertowState.Feed ? 1f : 0f, 1f - Mathf.Exp(-8f * dt));
            _hunt = Mathf.Lerp(_hunt, state == UndertowState.Hunt ? 1f : 0f, rate);
            float jawTarget = state == UndertowState.Feed || state == UndertowState.Hunt ? 1f : state == UndertowState.Waking ? 0.6f : 0.15f;
            _jawOpen = Mathf.Lerp(_jawOpen, jawTarget, rate);

            // A onda corre mais depressa quanto mais rápido ele nada; parado, quase nada.
            _phase += (0.25f + _speed * 0.55f) * dt;
            _stroke += (0.35f + _speed * 0.25f) * dt;
            if (_phase > 1000f) _phase -= 1000f;
            if (_stroke > 1000f) _stroke -= 1000f;

            Pose(_phase, _speed, _sleep, _hunt, _grab, _jawOpen, _stroke);

            // Inclina pela subida e descida (o NetworkTransform só manda o giro de lado).
            float pitch = -Mathf.Atan2(_vertical, Mathf.Max(0.6f, _speed)) * Mathf.Rad2Deg;
            _rig.localRotation = Quaternion.Euler(Mathf.Clamp(pitch, -35f, 35f) * (1f - _sleep), 0f, 0f);

            GameSession session = GameSession.Instance;
            if (session != null && session.level != null && !hidden) Fit(session.level, dt);

            UpdateBubbles(dt, state, hidden);
        }

        void SetVisible(bool visible)
        {
            if (Visible == visible) return;
            Visible = visible;
            foreach (Renderer r in _renderers)
                if (r != null) r.enabled = visible;
        }

        // ---------------------------------------------------------------- Pose

        /// <summary>
        /// Põe o corpo numa pose. <paramref name="phase"/> e <paramref name="stroke"/> são ciclos
        /// (a onda da cauda e a braçada); <paramref name="speed"/> em m/s; o resto de 0 a 1:
        /// dormindo (enrolado no fundo), caçando (braçadas fortes), pegando (braços para a frente,
        /// mãos fechadas) e a boca aberta.
        /// </summary>
        public void Pose(float phase, float speed, float sleep, float hunt, float grab, float jaw, float stroke)
        {
            Build();
            float swim = Mathf.Clamp01(speed / 2.5f);
            float wave = phase * Mathf.PI * 2f;

            // A onda de lado: fraca no peito, forte na ponta da cauda; enrolado, cada junta dobra
            // para o mesmo lado (um C no fundo da piscina).
            float amp = Mathf.Lerp(0.35f, 1f, swim) * (1f - sleep);
            Turn(_pelvis, 0f, 5f * amp * Mathf.Sin(wave) + 6f * sleep, 0f);
            Turn(_lowerBack, 0f, -4f * amp * Mathf.Sin(wave + 0.9f) - 10f * sleep, 0f);
            Turn(_chest, 0f, -3f * amp * Mathf.Sin(wave + 1.8f) - 10f * sleep, 4f * sleep);
            Turn(_shoulders, 0f, 2f * amp * Mathf.Sin(wave + 2.7f) - 6f * sleep, 0f);
            Turn(_neck, 4f * hunt - 8f * sleep, 3f * amp * Mathf.Sin(wave + 3.6f) - 14f * sleep, 0f);
            Turn(_head, -6f * hunt + 10f * sleep, 0f, 0f);
            float[] tailAmp = { 9f, 13f, 17f, 20f };
            for (int i = 0; i < _tail.Length; i++)
                Turn(_tail[i], 2f * Mathf.Sin(wave - 1.1f * (i + 1)) * (1f - sleep), tailAmp[i] * amp * Mathf.Sin(wave - 0.9f * (i + 1)) + 22f * sleep, 0f);

            Turn(_jaw, Mathf.Lerp(4f, 38f, jaw), 0f, 0f);

            // Braços: deslizando, arrastados para trás; caçando, braçadas de peito; pegando, para
            // a frente, fechando; dormindo, dobrados junto do corpo.
            float strokeWave = Mathf.Sin(stroke * Mathf.PI * 2f);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                float glideYaw = 130f + 6f * Mathf.Sin(wave + i * 1.3f);
                float strokeYaw = Mathf.Lerp(25f, 110f, strokeWave * 0.5f + 0.5f);
                float yaw = Mathf.Lerp(glideYaw, strokeYaw, hunt);
                yaw = Mathf.Lerp(yaw, 18f, grab);
                yaw = Mathf.Lerp(yaw, 150f, sleep);
                float pitch = Mathf.Lerp(8f, 18f + 14f * strokeWave, hunt);
                pitch = Mathf.Lerp(pitch, -10f, grab);
                pitch = Mathf.Lerp(pitch, 25f, sleep);
                float elbow = Mathf.Lerp(12f, 25f + 20f * Mathf.Max(0f, -strokeWave), hunt);
                elbow = Mathf.Lerp(elbow, 55f, grab);
                elbow = Mathf.Lerp(elbow, 70f, sleep);

                _armYaw[i] = yaw;
                _armPitch[i] = pitch;
                _armElbow[i] = elbow;
                _handX[i] = 10f * (1f - grab);
                _handY[i] = -side * 10f * grab;
                _fingersX[i] = Mathf.Lerp(12f, 70f, Mathf.Max(grab, sleep * 0.6f));
                PoseArm(i, _tuck[i]);
            }
        }

        // O braço na pose pedida, recolhido pela fração dada (0 a 1): ao longo do corpo, para trás e
        // um pouco para cima, cotovelo quase reto, dedos fechados. De 1 a 2, espremido: colado ao
        // flanco, reto para trás (a ponta dos dedos a ~0,33 m do meio), para passar num vão de porta.
        void PoseArm(int i, float tuck)
        {
            float side = i == 0 ? 1f : -1f;
            float t = Mathf.Clamp01(tuck), squeeze = Mathf.Clamp01(tuck - 1f);
            float yaw = Mathf.Lerp(Mathf.Lerp(_armYaw[i], 168f, t), SqueezeArmYaw, squeeze);
            float pitch = Mathf.Lerp(Mathf.Lerp(_armPitch[i], -4f, t), -2f, squeeze);
            float elbow = Mathf.Lerp(Mathf.Lerp(_armElbow[i], 4f, t), 0f, squeeze);
            _arm[i].localRotation = Quaternion.Euler(pitch, side * yaw, 0f);
            _forearm[i].localRotation = Quaternion.Euler(0f, -side * elbow, 0f);
            Turn(_hand[i], Mathf.Lerp(_handX[i], 0f, t), _handY[i] * (1f - t), 0f);
            Turn(_fingers[i], Mathf.Lerp(_fingersX[i], 20f, t), 0f, 0f);
        }

        // ---------------------------------------------------------------- Encaixe na fase

        /// <summary>
        /// Encaixa a pose atual na fase: cada braço que bateria (ombro, cotovelo, pulso ou ponta dos
        /// dedos em colisor da fase) se recolhe na hora até caber, e solta devagar (<paramref name="dt"/>;
        /// 0 = sem pressa, para as fotos); a inclinação cede se a ponta do focinho ou da cauda bateria; a
        /// cauda fica reta se a onda a levaria para dentro de uma parede.
        /// </summary>
        public void Fit(LevelBuilder level, float dt = 0f)
        {
            if (level == null || _rig == null) return;
            Transform root = level.transform;

            // A inclinação: metade, e depois nenhuma, se o focinho ou a cauda bateria.
            for (int k = 0; k < 2 && (Blocked(root, SpineHead(), 0f) || Blocked(root, SpineTail(), 0f)); k++)
                _rig.localRotation = Quaternion.Slerp(Quaternion.identity, _rig.localRotation, k == 0 ? 0.5f : 0f);

            // A cauda: sem a onda, reta atrás do corpo (e enrolada, dormindo, como estava). Se ainda
            // bateria (virando no meio de uma célula de corredor, a ponta passa da parede), curva-se
            // para o lado que cabe (o cérebro só guarda a cauda até 1,3 m atrás no corpo espremido).
            if (Blocked(root, SpineTail(), 0f))
            {
                CurlTail(0f);
                if (Blocked(root, SpineTail(), 0f))
                {
                    bool done = false;
                    foreach (float k in new[] { 0.5f, 1f })
                    {
                        foreach (float sign in new[] { _curlSide, -_curlSide })
                        {
                            CurlTail(sign * k);
                            if (Blocked(root, SpineTail(), 0f)) continue;
                            _curlSide = sign;
                            done = true;
                            break;
                        }
                        if (done) break;
                    }
                    if (!done) CurlTail(0f);
                }
            }

            for (int i = 0; i < 2; i++)
            {
                float relaxed = dt > 0f ? Mathf.Max(0f, _tuck[i] - dt * 0.8f) : 0f;
                float chosen = 2f;
                foreach (float t in new[] { relaxed, _tuck[i], Mathf.Max(_tuck[i], 0.5f), 0.8f, 1f, 1.5f })
                {
                    PoseArm(i, t);
                    if (Blocked(root, ArmChain(i), 0f)) continue;
                    chosen = t;
                    break;
                }
                _tuck[i] = chosen;
                PoseArm(i, chosen);
            }
        }

        // A cauda sem a onda: reta (0) ou curvada para um lado (-1 a 1), mais o enrolado de dormindo.
        void CurlTail(float curl)
        {
            for (int i = 0; i < _tail.Length; i++)
                if (_tail[i] != null) _tail[i].localRotation = _rest[_tail[i]] * Quaternion.Euler(0f, 22f * _sleep + TailCurl * curl, 0f);
        }

        Vector3[] SpineHead() => new[] { transform.position, _shoulders.position, _head.position, HeadTip() };
        Vector3[] SpineTail() => new[] { transform.position, _pelvis.position, _tail[0].position, _tail[1].position, _tail[2].position, _tail[3].position, TailTip() };
        Vector3[] ArmChain(int i) => new[] { _arm[i].position, _forearm[i].position, _hand[i].position, _fingers[i].position, FingerTip(i) };

        Vector3 HeadTip() => _head.position + _head.forward * 0.26f;
        Vector3 TailTip() => _tail[3].position - _tail[3].forward * 0.42f;
        Vector3 FingerTip(int i) => _fingers[i].position + _fingers[i].forward * 0.24f;

        // A corrente de pontos encosta em colisor da fase? Cada trecho por raio, sem os últimos
        // <paramref name="tolerance"/> metros do fim.
        static bool Blocked(Transform root, Vector3[] chain, float tolerance)
        {
            for (int k = 0; k + 1 < chain.Length; k++)
                if (SegmentHits(root, chain[k], chain[k + 1], tolerance)) return true;
            return false;
        }

        static bool SegmentHits(Transform root, Vector3 a, Vector3 b, float tolerance)
        {
            Vector3 d = b - a;
            float length = d.magnitude - tolerance;
            if (length <= 0.001f) return false;
            int count = Physics.RaycastNonAlloc(a, d.normalized, Hits, length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Hits[i].collider.transform.IsChildOf(root)) return true;
            // E de trás para a frente: o raio não acha a face de quem começa dentro de um colisor.
            count = Physics.RaycastNonAlloc(b - d.normalized * tolerance, -d.normalized, Hits, length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Hits[i].collider.transform.IsChildOf(root)) return true;
            return false;
        }

        /// <summary>
        /// Para os testes: as partes do corpo (cabeça, mãos e ponta dos dedos, ponta da cauda) que
        /// entram em colisor fixo da fase, com a tolerância dada (metros do fim de cada trecho, da
        /// espinha para a ponta). Vazio se o corpo inteiro está fora de parede, escada, coluna e piso.
        /// </summary>
        public void Penetrations(LevelBuilder level, float tolerance, List<string> where)
        {
            where.Clear();
            if (level == null || _rig == null) return;
            Transform root = level.transform;
            if (Blocked(root, SpineHead(), tolerance)) where.Add($"cabeça {HeadTip()}");
            if (Blocked(root, SpineTail(), tolerance)) where.Add($"cauda {TailTip()}");
            for (int i = 0; i < 2; i++)
                if (Blocked(root, ArmChain(i), tolerance)) where.Add($"mão {(i == 0 ? "E" : "D")} {FingerTip(i)}");
        }

        void Turn(Transform joint, float x, float y, float z)
        {
            if (joint == null) return;
            joint.localRotation = _rest[joint] * Quaternion.Euler(x, y, z);
        }

        // ---------------------------------------------------------------- Montagem

        Transform Live(Transform joint)
        {
            _live.Add(joint);
            return joint;
        }

        /// <summary>Monta as juntas (e o corpo de código, sem modelo). Só a primeira chamada constrói.</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;

            Material pale = paleMaterial;
            Material back = backMaterial != null ? backMaterial : pale;

            _rig = Live(FigureKit.Joint(transform, "Afogado", Vector3.zero));
            _pelvis = Live(FigureKit.Joint(_rig, "Quadril", new Vector3(0f, 0f, PelvisZ)));
            FigureKit.Ball(_pelvis, "Quadril_Volume", new Vector3(0f, 0f, 0f), new Vector3(0.38f, 0.24f, 0.42f), pale);

            _lowerBack = Live(FigureKit.Joint(_pelvis, "Lombo", new Vector3(0f, 0f, LowerBack)));
            FigureKit.Ball(_lowerBack, "Lombo_Volume", new Vector3(0f, 0f, -0.17f), new Vector3(0.34f, 0.22f, 0.5f), pale);
            FigureKit.Ball(_lowerBack, "Lombo_Costas", new Vector3(0f, 0.06f, -0.17f), new Vector3(0.3f, 0.14f, 0.48f), back);

            _chest = Live(FigureKit.Joint(_lowerBack, "Peito", new Vector3(0f, 0f, Chest)));
            FigureKit.Ball(_chest, "Peito_Volume", new Vector3(0f, 0f, -0.16f), new Vector3(0.46f, 0.28f, 0.56f), pale);
            FigureKit.Ball(_chest, "Peito_Costas", new Vector3(0f, 0.08f, -0.14f), new Vector3(0.42f, 0.17f, 0.52f), back);

            _shoulders = Live(FigureKit.Joint(_chest, "Ombros", new Vector3(0f, 0f, Shoulders)));
            FigureKit.Ball(_shoulders, "Ombros_Volume", new Vector3(0f, 0.02f, -0.1f), new Vector3(0.58f, 0.22f, 0.3f), back);

            _neck = Live(FigureKit.Joint(_shoulders, "Pescoco", new Vector3(0f, 0.02f, Neck)));
            FigureKit.Ball(_neck, "Pescoco_Volume", new Vector3(0f, 0f, 0.06f), new Vector3(0.14f, 0.13f, 0.24f), pale);

            _head = Live(FigureKit.Joint(_neck, "Cabeca", new Vector3(0f, 0.02f, HeadUp)));
            FigureKit.Ball(_head, "Cabeca_Volume", new Vector3(0f, 0.02f, 0.1f), new Vector3(0.24f, 0.22f, 0.3f), pale);
            FigureKit.Ball(_head, "Cabeca_Topo", new Vector3(0f, 0.07f, 0.06f), new Vector3(0.2f, 0.12f, 0.26f), back);

            _jaw = Live(FigureKit.Joint(_head, "Mandibula", new Vector3(0f, -0.07f, 0.04f)));
            FigureKit.Ball(_jaw, "Mandibula_Volume", new Vector3(0f, -0.02f, 0.1f), new Vector3(0.17f, 0.06f, 0.2f), pale);

            Transform parent = _pelvis;
            float[] widths = { 0.3f, 0.24f, 0.17f, 0.12f };
            for (int i = 0; i < _tail.Length; i++)
            {
                float length = TailSegments[i];
                _tail[i] = Live(FigureKit.Joint(parent, "Cauda_" + (i + 1), new Vector3(0f, 0f, i == 0 ? -0.16f : -TailSegments[i - 1])));
                FigureKit.Ball(_tail[i], "Cauda_Volume_" + (i + 1), new Vector3(0f, 0f, -length * 0.5f),
                               new Vector3(widths[i], widths[i] * 0.7f, length * 1.15f), back);
                parent = _tail[i];
            }
            // A ponta achatada (nadadeira de pele).
            FigureKit.Ball(_tail[3], "Cauda_Ponta", new Vector3(0f, 0f, -0.42f), new Vector3(0.34f, 0.04f, 0.32f), back);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "E" : "D";
                _arm[i] = Live(FigureKit.Joint(_shoulders, "Braco_" + tag, new Vector3(side * ShoulderWidth, -0.02f, -0.04f),
                                               new Vector3(RestArmPitch, side * RestArmYaw, 0f)));
                FigureKit.Ball(_arm[i], "Braco_Volume_" + tag, new Vector3(0f, 0f, UpperArm * 0.5f), new Vector3(0.1f, 0.1f, UpperArm * 1.1f), pale);
                _forearm[i] = Live(FigureKit.Joint(_arm[i], "Antebraco_" + tag, new Vector3(0f, 0f, UpperArm)));
                FigureKit.Ball(_forearm[i], "Antebraco_Volume_" + tag, new Vector3(0f, 0f, Forearm * 0.5f), new Vector3(0.08f, 0.08f, Forearm * 1.1f), pale);
                _hand[i] = Live(FigureKit.Joint(_forearm[i], "Mao_" + tag, new Vector3(0f, 0f, Forearm)));
                FigureKit.Ball(_hand[i], "Palma_" + tag, new Vector3(0f, -0.01f, Palm * 0.5f), new Vector3(0.12f, 0.035f, Palm * 1.3f), pale);
                _fingers[i] = Live(FigureKit.Joint(_hand[i], "Dedos_" + tag, new Vector3(0f, -0.01f, Palm)));
                for (int f = 0; f < 4; f++)
                {
                    float x = (f - 1.5f) * 0.028f;
                    FigureKit.Ball(_fingers[i], "Dedo_" + tag + f, new Vector3(x, 0f, 0.11f), new Vector3(0.022f, 0.022f, 0.24f), pale);
                }
            }

            foreach (Transform joint in _live) _rest[joint] = joint.localRotation;

            bool dressed = model != null && !KeepAllJoints;
            if (!KeepAllJoints)
                foreach (Transform joint in new List<Transform>(_live))
                    if (dressed) FigureKit.Strip(joint, _live); else FigureKit.Bake(joint, _live);

            // A pose de encaixe: esticado, braços abertos (as rotações de descanso). Foi nela que o
            // Blender esculpiu o modelo.
            foreach (KeyValuePair<Transform, Quaternion> rest in _rest) rest.Key.localRotation = rest.Value;
            if (dressed) CharacterModel.Attach(transform, model, modelMaterials);
        }

        // ---------------------------------------------------------------- Bolhas

        void BuildBubbles()
        {
            if (KeepAllJoints) return;
            _bubbles = new Transform[BubbleCount];
            _bubbleVelocity = new Vector3[BubbleCount];
            _bubbleLife = new float[BubbleCount];
            var holder = new GameObject("Bolhas").transform;
            holder.SetParent(transform, false);
            for (int i = 0; i < BubbleCount; i++)
            {
                Transform bubble = FigureKit.Ball(holder, "Bolha_" + i, Vector3.zero, Vector3.one * 0.05f, bubbleMaterial != null ? bubbleMaterial : paleMaterial, false);
                bubble.gameObject.SetActive(false);
                _bubbles[i] = bubble;
            }
        }

        /// <summary>Solta algumas bolhas de uma vez (acordando, surgindo, pegando).</summary>
        public void Burst(int count)
        {
            for (int i = 0; i < count; i++) Release(1.6f);
        }

        void UpdateBubbles(float dt, UndertowState state, bool hidden)
        {
            if (_bubbles == null) return;
            PoolWater water = PoolWater.Current;
            float surface = water != null ? water.SurfaceY : float.MaxValue;

            // Nadando, uma trilha; mais depressa, mais bolhas. Dormindo, uma de vez em quando.
            if (!hidden && transform.position.y < surface - 0.2f)
            {
                float rate = state == UndertowState.Asleep ? 0.25f : 1.2f + _speed * 2.2f;
                if (state == UndertowState.Feed) rate = 14f;
                _bubbleDebt += rate * dt;
                while (_bubbleDebt >= 1f)
                {
                    _bubbleDebt -= 1f;
                    Release(1f);
                }
            }

            for (int i = 0; i < _bubbles.Length; i++)
            {
                if (_bubbleLife[i] <= 0f) continue;
                _bubbleLife[i] -= dt;
                Transform b = _bubbles[i];
                Vector3 v = _bubbleVelocity[i];
                v.x += Mathf.Sin(Time.time * 7f + i) * 0.4f * dt;
                _bubbleVelocity[i] = v;
                Vector3 p = b.position + v * dt;
                // Na superfície estoura (a água "mexe" acima dele).
                if (p.y >= surface - 0.02f || _bubbleLife[i] <= 0f)
                {
                    _bubbleLife[i] = 0f;
                    b.gameObject.SetActive(false);
                    continue;
                }
                b.position = p;
            }
        }

        void Release(float spread)
        {
            if (_bubbles == null) return;
            Transform b = _bubbles[_bubbleNext];
            _bubbleNext = (_bubbleNext + 1) % _bubbles.Length;
            Vector3 head = transform.position + transform.forward * 1.1f;
            Vector3 from = Random.value < 0.6f ? head : transform.position + transform.forward * Random.Range(-1.2f, 0.6f);
            b.position = from + Random.insideUnitSphere * 0.25f * spread;
            float size = Random.Range(0.025f, 0.075f);
            b.localScale = Vector3.one * size;
            b.gameObject.SetActive(true);
            int i = System.Array.IndexOf(_bubbles, b);
            _bubbleVelocity[i] = new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(0.7f, 1.2f), Random.Range(-0.1f, 0.1f));
            _bubbleLife[i] = 6f;
            BubblesReleased++;
        }
    }
}
