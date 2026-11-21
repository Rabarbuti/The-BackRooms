using UnityEngine;

namespace BackRooms.Player
{
    /// <summary>
    /// O corpo que os outros jogadores veem: um explorador em traje de proteção amarelo, com
    /// capuz, visor escuro, respirador com dois filtros, cilindro de ar nas costas, luvas e
    /// botas pretas. Montado em código (ver <see cref="FigureKit"/>), como a entidade.
    ///
    /// Também é animado em código: a passada sai do deslocamento do objeto, e o resto
    /// (agachar, inclinação da cabeça, item na mão, lanterna) chega por <see cref="SetState"/>,
    /// que o PlayerNetwork alimenta com o que veio pela rede. O dono não vê o próprio corpo.
    /// </summary>
    public sealed class PlayerBody : MonoBehaviour
    {
        public Material suitMaterial;
        [Tooltip("Luvas, botas e moldura do visor.")]
        public Material rubberMaterial;
        public Material visorMaterial;
        [Tooltip("Respirador, cilindro de ar e cinto.")]
        public Material gearMaterial;

        [Tooltip("Metros andados por ciclo completo de passada (dois passos).")]
        public float strideLength = 1.45f;

        const float ThighLength = 0.44f;
        const float ShinLength = 0.44f;
        const float FootHeight = 0.06f;
        /// <summary>Altura da bacia sentado: o assento das cadeiras do Escritório fica a 0,5 m do chão.</summary>
        const float SeatedHip = 0.58f;

        /// <summary>Verdadeiro se, pelo que chegou, este jogador está com um item na mão.</summary>
        public bool Holding => _holding;

        /// <summary>O corpo da caneta na mão direita, com a tinta escolhida pelo jogador.</summary>
        public Renderer PenRenderer => _penBody;

        Transform _rig, _pelvis, _spine, _head;
        readonly Transform[] _thigh = new Transform[2];
        readonly Transform[] _shin = new Transform[2];
        readonly Transform[] _foot = new Transform[2];
        readonly Transform[] _upperArm = new Transform[2];
        readonly Transform[] _forearm = new Transform[2];
        readonly Transform[] _hand = new Transform[2];
        readonly Transform[] _grip = new Transform[2];
        readonly float[] _tuck = new float[2];
        readonly bool[] _itemShown = new bool[2];
        GameObject _pen;
        Renderer _penBody;
        GameObject _torch;

        const float WallReach = 1.2f;
        const float WallGap = 0.02f;
        static readonly RaycastHit[] WallHits = new RaycastHit[16];
        bool _hasWall;
        Vector3 _wallPoint;
        Vector3 _wallNormal;

        Vector3 _lastPosition;
        float _speed;
        float _phase;
        float _crouch;
        float _pitch;
        float _hold;
        float _aim;
        bool _holding;
        bool _lightOn;
        bool _built;
        bool _seated;
        float _sit;
        float _headYaw;

        void Awake()
        {
            Build();
            _lastPosition = transform.position;
        }

        /// <summary>O que este jogador está fazendo, visto de fora. Chamado a cada quadro pelo PlayerNetwork.</summary>
        public void SetState(float crouch, float pitch, bool holding, bool lightOn)
        {
            _crouch = Mathf.Clamp01(crouch);
            _pitch = pitch;
            _holding = holding;
            _lightOn = lightOn;
        }

        /// <summary>
        /// Sentado numa cadeira (<c>ClassroomSeats</c>): a figura senta, virada para a frente da cadeira
        /// (quem vira o corpo é o PlayerNetwork), e a cabeça gira <paramref name="headYaw"/> graus para
        /// onde o jogador olha. Chamado a cada quadro pelo PlayerNetwork.
        /// </summary>
        public void SetSeated(bool seated, float headYaw)
        {
            // Ao sentar e ao levantar a pessoa salta de lugar: a pose vai junto, sem transição.
            if (seated != _seated) _sit = seated ? 1f : 0f;
            _seated = seated;
            _headYaw = headYaw;
        }

        /// <summary>Verdadeiro se, pelo que chegou, este jogador está sentado.</summary>
        public bool Seated => _seated;

        /// <summary>
        /// Procura a superfície na frente do corpo, na altura dos ombros e do peito: o braço com
        /// a caneta ou a lanterna encolhe para não atravessá-la (ver <see cref="Pose"/>). O jogador
        /// encosta na parede com o centro a 0,33 m dela, e o braço erguido chega a uns 0,6 m.
        /// Chamado a cada quadro pelo Update; público para a ferramenta de prévia.
        /// </summary>
        public void ProbeWall()
        {
            if (!_built) Build();
            _hasWall = false;

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return;
            forward.Normalize();

            Transform self = transform.root;
            float best = float.PositiveInfinity;

            for (int ray = 0; ray < 2; ray++)
            {
                Vector3 origin = new Vector3(transform.position.x, _upperArm[0].position.y - ray * 0.3f, transform.position.z);
                int count = Physics.RaycastNonAlloc(origin, forward, WallHits, WallReach, ~0, QueryTriggerInteraction.Ignore);

                for (int i = 0; i < count; i++)
                {
                    // O próprio jogador não conta (como no raio da caneta).
                    if (WallHits[i].collider.transform.root == self || WallHits[i].distance >= best) continue;

                    best = WallHits[i].distance;
                    _wallPoint = WallHits[i].point;
                    _wallNormal = WallHits[i].normal;
                    _hasWall = true;
                }
            }
        }

        /// <summary>
        /// Folga, em metros, entre a superfície achada por <see cref="ProbeWall"/> e a peça dos
        /// braços que mais avança (negativo: atravessa). Infinito sem superfície. Para a prévia.
        /// </summary>
        public float ArmClearance()
        {
            if (!_hasWall) return float.PositiveInfinity;
            return Mathf.Min(Clearance(0), Clearance(1));
        }

        /// <summary>Tinta da caneta que este jogador tem na mão (chega pela rede, como o resto).</summary>
        public void SetPenInk(Material ink)
        {
            if (!_built) Build();
            if (_penBody != null && ink != null && _penBody.sharedMaterial != ink) _penBody.sharedMaterial = ink;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 moved = transform.position - _lastPosition;
            _lastPosition = transform.position;
            moved.y = 0f;

            // Um salto grande é a rede recolocando o jogador, não corrida.
            float speed = moved.magnitude / dt;
            if (speed > 12f) speed = 0f;
            _speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-10f * dt));

            _phase += _speed / Mathf.Max(0.3f, strideLength) * dt;
            if (_phase > 1000f) _phase -= 1000f;

            _hold = Mathf.MoveTowards(_hold, _holding ? 1f : 0f, dt * 5f);
            _aim = Mathf.MoveTowards(_aim, _lightOn ? 1f : 0f, dt * 5f);

            // O braço encolhe na hora em que a parede chega e volta a esticar devagar.
            ProbeWall();
            for (int i = 0; i < 2; i++) _tuck[i] = Mathf.MoveTowards(_tuck[i], 0f, dt * 2.5f);

            if (_seated) _speed = 0f;
            Pose(_phase, _speed, _crouch, _pitch, _hold, _aim, Time.time, _sit, _headYaw);
        }

        /// <summary>
        /// Coloca o corpo numa pose. <paramref name="hold"/> e <paramref name="aim"/> vão de 0 a 1:
        /// braço direito erguido com o item e braço esquerdo apontando a lanterna.
        /// Público para a ferramenta de prévia, que fotografa fora do Play. <paramref name="sit"/> (0 a 1)
        /// senta a figura numa cadeira e <paramref name="headYaw"/> gira a cabeça para o lado (graus).
        /// </summary>
        public void Pose(float phase, float speed, float crouch, float pitch, float hold, float aim, float time, float sit = 0f, float headYaw = 0f)
        {
            if (!_built) Build();

            float run = Mathf.Clamp01(speed / 5f);
            float cycle = phase * Mathf.PI * 2f;
            float swing = Mathf.Sin(cycle);
            float lift = Mathf.Cos(cycle);
            float breathe = Mathf.Sin(time * 1.6f);

            // Pernas: em pé quase retas; agachado, coxa para a frente e canela dobrada para trás.
            // Sentado: coxa na horizontal e canela de pé, os pés no chão diante da cadeira.
            sit = Mathf.Clamp01(sit);
            float thighBase = Mathf.Lerp(Mathf.Lerp(4f, 62f, crouch), 86f, sit);
            float kneeBase = Mathf.Lerp(Mathf.Lerp(8f, 118f, crouch), 84f, sit);
            float legSwing = (Mathf.Lerp(0f, 34f, run) * (1f - 0.5f * crouch) + Mathf.Clamp01(speed / 1.4f) * 10f * (1f - run)) * (1f - sit);

            // A bacia fica na altura que deixa os pés no chão.
            float hip = ThighLength * Mathf.Cos(thighBase * Mathf.Deg2Rad) + ShinLength * Mathf.Cos((kneeBase - thighBase) * Mathf.Deg2Rad) + FootHeight;
            float bob = -Mathf.Abs(swing) * 0.035f * Mathf.Clamp01(speed / 2f);
            // Sentado, a bacia fica no assento, um pouco para trás (o encosto da cadeira).
            _pelvis.localPosition = new Vector3(0f, Mathf.Lerp(hip + bob, SeatedHip, sit), -0.08f * sit);
            _rig.localRotation = Quaternion.Euler(0f, 0f, swing * 2f * run);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                float forward = swing * side;
                float knee = Mathf.Max(0f, lift * side) * Mathf.Clamp01(speed / 1.2f);

                _thigh[i].localRotation = Quaternion.Euler(-(thighBase + forward * legSwing + knee * 10f), 0f, side * 2.5f);
                _shin[i].localRotation = Quaternion.Euler(kneeBase + knee * 34f + Mathf.Max(0f, -forward) * 8f * run, 0f, 0f);
                _foot[i].localRotation = Quaternion.Euler(-(kneeBase - thighBase) - knee * 14f, 0f, 0f);
            }

            // Tronco: um pouco curvado; mais agachado ou correndo.
            float lean = Mathf.Lerp(5f + 26f * crouch + 8f * run, 2f, sit) + breathe * 0.8f;
            _spine.localRotation = Quaternion.Euler(lean, -swing * 6f * run, 0f);

            // Cabeça: acompanha para onde o jogador olha (positivo = para baixo), descontando a curvatura do tronco.
            _head.localRotation = Quaternion.Euler(0f, Mathf.Clamp(headYaw, -75f, 75f) * sit, 0f)
                                * Quaternion.Euler(Mathf.Clamp(pitch, -60f, 60f) * 0.75f - lean * 0.8f, 0f, 0f);

            // Braços: balançam ao contrário das pernas. O direito sobe com o item; o esquerdo, com a lanterna.
            float armSwing = Mathf.Lerp(4f, 30f, run) + Mathf.Clamp01(speed / 1.4f) * 8f;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                float busy = i == 0 ? hold : aim;
                float forward = -swing * side * (1f - busy);

                // Sentado, o braço livre descansa na carteira: ombro para a frente, cotovelo dobrado.
                float shoulder = lean + forward * armSwing + busy * (i == 0 ? 52f : 60f) + crouch * 18f * (1f - busy) + sit * 28f * (1f - busy);
                float elbow = 12f + Mathf.Max(0f, forward) * 12f + busy * (i == 0 ? 58f : 28f) + crouch * 40f * (1f - busy) + sit * 55f * (1f - busy);

                _itemShown[i] = busy > 0.2f;

                // Parede na frente: o menor encolhimento que deixa o braço, a mão e o item
                // antes dela (busca em 8 passos; cada um só gira duas juntas).
                float need = 0f;
                if (_hasWall)
                {
                    SetArm(i, shoulder, elbow, side, busy, lean, 0f);
                    if (Clearance(i) < WallGap)
                    {
                        float low = 0f, high = 1f;
                        for (int step = 0; step < 8; step++)
                        {
                            float mid = (low + high) * 0.5f;
                            SetArm(i, shoulder, elbow, side, busy, lean, mid);
                            if (Clearance(i) < WallGap) low = mid; else high = mid;
                        }
                        need = high;
                    }
                }

                _tuck[i] = Mathf.Max(_tuck[i], need);
                SetArm(i, shoulder, elbow, side, busy, lean, _tuck[i]);
            }

            if (_pen != null && _pen.activeSelf != hold > 0.2f) _pen.SetActive(hold > 0.2f);
            if (_torch != null && _torch.activeSelf != aim > 0.2f) _torch.SetActive(aim > 0.2f);
        }

        // Gira as juntas de um braço. Encolhido (tuck 1), o braço desce rente ao corpo, abre um
        // pouco para o lado e dobra o cotovelo, trazendo a mão para perto do peito.
        void SetArm(int i, float shoulder, float elbow, float side, float busy, float lean, float tuck)
        {
            float s = Mathf.Lerp(shoulder, lean + 6f, tuck);
            float e = Mathf.Lerp(elbow, 145f, tuck);
            _upperArm[i].localRotation = Quaternion.Euler(-s, 0f, side * (6f - busy * 10f + 28f * tuck));
            _forearm[i].localRotation = Quaternion.Euler(-e, 0f, -side * busy * 8f);
        }

        // Folga entre a superfície da frente e as peças de um braço: cotovelo, punho, luva e a
        // ponta do item (caneta ou lanterna), cada uma com o seu raio. As peças são cápsulas e
        // esferas entre esses pontos, então nenhuma parte delas avança mais que eles.
        float Clearance(int i)
        {
            float Gap(Vector3 point, float radius) => Vector3.Dot(point - _wallPoint, _wallNormal) - radius;

            float gap = Mathf.Min(Gap(_forearm[i].position, 0.075f), Gap(_hand[i].position, 0.065f));
            gap = Mathf.Min(gap, Gap(_hand[i].TransformPoint(0f, -0.055f, 0.01f), 0.075f));

            if (_itemShown[i])
                gap = Mathf.Min(gap, i == 0 ? Gap(_grip[i].TransformPoint(0f, -0.158f, 0f), 0.01f)
                                            : Gap(_grip[i].TransformPoint(0f, -0.184f, 0f), 0.03f));
            return gap;
        }

        /// <summary>Monta as peças. Pode ser chamado mais de uma vez; só a primeira constrói.</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;

            Material suit = suitMaterial;
            Material rubber = rubberMaterial != null ? rubberMaterial : suitMaterial;
            Material visor = visorMaterial != null ? visorMaterial : rubber;
            Material gear = gearMaterial != null ? gearMaterial : rubber;

            _rig = FigureKit.Joint(transform, "Traje", Vector3.zero);
            _pelvis = FigureKit.Joint(_rig, "Bacia", new Vector3(0f, 0.94f, 0f));

            FigureKit.Ball(_pelvis, "Bacia_Peca", new Vector3(0f, 0.03f, 0f), new Vector3(0.38f, 0.26f, 0.28f), suit);
            FigureKit.Ball(_pelvis, "Cinto", new Vector3(0f, 0.11f, 0f), new Vector3(0.40f, 0.07f, 0.30f), gear);

            // Pernas folgadas do traje e botas de borracha.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "D" : "E";

                _thigh[i] = FigureKit.Joint(_pelvis, "Coxa_" + tag, new Vector3(side * 0.105f, 0f, 0f));
                _shin[i] = FigureKit.Limb(_thigh[i], "Canela_" + tag, ThighLength, 0.098f, true, suit);
                _foot[i] = FigureKit.Limb(_shin[i], "Pe_" + tag, ShinLength, 0.083f, true, suit);

                FigureKit.Ball(_foot[i], "Cano_Da_Bota_" + tag, new Vector3(0f, 0.10f, 0f), new Vector3(0.19f, 0.26f, 0.19f), rubber);
                FigureKit.Ball(_foot[i], "Bota_" + tag, new Vector3(0f, -0.005f, 0.065f), new Vector3(0.135f, 0.11f, 0.30f), rubber);
            }

            // Tronco.
            _spine = FigureKit.Joint(_pelvis, "Coluna", new Vector3(0f, 0.08f, 0f));
            Transform shoulders = FigureKit.Limb(_spine, "Ombros", 0.44f, 0.165f, false, suit);
            FigureKit.Ball(_spine, "Peito", new Vector3(0f, 0.30f, 0.01f), new Vector3(0.44f, 0.40f, 0.31f), suit);

            // Cilindro de ar nas costas, com a válvula em cima.
            Transform tank = FigureKit.Joint(_spine, "Cilindro", new Vector3(0f, 0.06f, -0.19f));
            Transform valve = FigureKit.Limb(tank, "Valvula", 0.36f, 0.078f, false, gear);
            FigureKit.Ball(valve, "Valvula_Peca", new Vector3(0f, 0.04f, 0f), new Vector3(0.07f, 0.07f, 0.07f), rubber);

            // Capuz com visor escuro e respirador.
            _head = FigureKit.Joint(shoulders, "Cabeca", new Vector3(0f, 0.03f, 0f));
            FigureKit.Ball(_head, "Gola", new Vector3(0f, 0.02f, 0f), new Vector3(0.34f, 0.17f, 0.32f), suit);
            FigureKit.Ball(_head, "Capuz", new Vector3(0f, 0.17f, 0f), new Vector3(0.30f, 0.34f, 0.32f), suit);
            FigureKit.Ball(_head, "Moldura_Do_Visor", new Vector3(0f, 0.195f, 0.118f), new Vector3(0.265f, 0.165f, 0.09f), rubber);
            FigureKit.Ball(_head, "Visor", new Vector3(0f, 0.197f, 0.132f), new Vector3(0.235f, 0.135f, 0.075f), visor);
            FigureKit.Ball(_head, "Respirador", new Vector3(0f, 0.075f, 0.145f), new Vector3(0.105f, 0.095f, 0.10f), gear);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                Transform filter = FigureKit.Joint(_head, "Filtro_" + i, new Vector3(side * 0.045f, 0.07f, 0.15f), new Vector3(0f, side * -28f, side * 105f));
                FigureKit.Limb(filter, "Filtro_Ponta_" + i, 0.065f, 0.04f, true, rubber);
            }

            // Braços e luvas. No espaço do traje, +X é a direita de quem o veste.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "D" : "E";

                FigureKit.Ball(shoulders, "Ombro_Peca_" + tag, new Vector3(side * 0.205f, -0.03f, 0f), Vector3.one * 0.19f, suit);
                _upperArm[i] = FigureKit.Joint(shoulders, "Braco_" + tag, new Vector3(side * 0.225f, -0.04f, 0f));
                _forearm[i] = FigureKit.Limb(_upperArm[i], "Antebraco_" + tag, 0.30f, 0.073f, true, suit);
                Transform hand = FigureKit.Limb(_forearm[i], "Mao_" + tag, 0.27f, 0.062f, true, suit);
                _hand[i] = hand;

                FigureKit.Ball(hand, "Punho_" + tag, new Vector3(0f, 0.02f, 0f), new Vector3(0.14f, 0.07f, 0.14f), rubber);
                FigureKit.Ball(hand, "Luva_" + tag, new Vector3(0f, -0.055f, 0.01f), new Vector3(0.10f, 0.14f, 0.11f), rubber);

                if (i == 0)
                {
                    // Caneta na mão direita, só enquanto o jogador a usa.
                    Transform grip = FigureKit.Joint(hand, "Caneta", new Vector3(0f, -0.08f, 0.03f), new Vector3(-70f, 0f, 0f));
                    FigureKit.Limb(grip, "Caneta_Ponta", 0.15f, 0.008f, true, rubber);
                    _penBody = grip.Find("Caneta_Peca").GetComponent<Renderer>();
                    _grip[i] = grip;
                    _pen = grip.gameObject;
                    _pen.SetActive(false);
                }
                else
                {
                    // Lanterna na mão esquerda, só enquanto está acesa.
                    Transform grip = FigureKit.Joint(hand, "Lanterna", new Vector3(0f, -0.07f, 0.02f), new Vector3(-80f, 0f, 0f));
                    FigureKit.Limb(grip, "Lanterna_Ponta", 0.16f, 0.024f, true, gear);
                    _grip[i] = grip;
                    _torch = grip.gameObject;
                    _torch.SetActive(false);
                }
            }

            Pose(0f, 0f, 0f, 0f, 0f, 0f, 0f);
        }
    }
}
