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
        GameObject _pen;
        Renderer _penBody;
        GameObject _torch;

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

            Pose(_phase, _speed, _crouch, _pitch, _hold, _aim, Time.time);
        }

        /// <summary>
        /// Coloca o corpo numa pose. <paramref name="hold"/> e <paramref name="aim"/> vão de 0 a 1:
        /// braço direito erguido com o item e braço esquerdo apontando a lanterna.
        /// Público para a ferramenta de prévia, que fotografa fora do Play.
        /// </summary>
        public void Pose(float phase, float speed, float crouch, float pitch, float hold, float aim, float time)
        {
            if (!_built) Build();

            float run = Mathf.Clamp01(speed / 5f);
            float cycle = phase * Mathf.PI * 2f;
            float swing = Mathf.Sin(cycle);
            float lift = Mathf.Cos(cycle);
            float breathe = Mathf.Sin(time * 1.6f);

            // Pernas: em pé quase retas; agachado, coxa para a frente e canela dobrada para trás.
            float thighBase = Mathf.Lerp(4f, 62f, crouch);
            float kneeBase = Mathf.Lerp(8f, 118f, crouch);
            float legSwing = Mathf.Lerp(0f, 34f, run) * (1f - 0.5f * crouch) + Mathf.Clamp01(speed / 1.4f) * 10f * (1f - run);

            // A bacia fica na altura que deixa os pés no chão.
            float hip = ThighLength * Mathf.Cos(thighBase * Mathf.Deg2Rad) + ShinLength * Mathf.Cos((kneeBase - thighBase) * Mathf.Deg2Rad) + FootHeight;
            float bob = -Mathf.Abs(swing) * 0.035f * Mathf.Clamp01(speed / 2f);
            _pelvis.localPosition = new Vector3(0f, hip + bob, 0f);
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
            float lean = 5f + 26f * crouch + 8f * run + breathe * 0.8f;
            _spine.localRotation = Quaternion.Euler(lean, -swing * 6f * run, 0f);

            // Cabeça: acompanha para onde o jogador olha (positivo = para baixo), descontando a curvatura do tronco.
            _head.localRotation = Quaternion.Euler(Mathf.Clamp(pitch, -60f, 60f) * 0.75f - lean * 0.8f, 0f, 0f);

            // Braços: balançam ao contrário das pernas. O direito sobe com o item; o esquerdo, com a lanterna.
            float armSwing = Mathf.Lerp(4f, 30f, run) + Mathf.Clamp01(speed / 1.4f) * 8f;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                float busy = i == 0 ? hold : aim;
                float forward = -swing * side * (1f - busy);

                float shoulder = lean + forward * armSwing + busy * (i == 0 ? 52f : 60f) + crouch * 18f * (1f - busy);
                float elbow = 12f + Mathf.Max(0f, forward) * 12f + busy * (i == 0 ? 58f : 28f) + crouch * 40f * (1f - busy);

                _upperArm[i].localRotation = Quaternion.Euler(-shoulder, 0f, side * (6f - busy * 10f));
                _forearm[i].localRotation = Quaternion.Euler(-elbow, 0f, -side * busy * 8f);
            }

            if (_pen != null && _pen.activeSelf != hold > 0.2f) _pen.SetActive(hold > 0.2f);
            if (_torch != null && _torch.activeSelf != aim > 0.2f) _torch.SetActive(aim > 0.2f);
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

                FigureKit.Ball(hand, "Punho_" + tag, new Vector3(0f, 0.02f, 0f), new Vector3(0.14f, 0.07f, 0.14f), rubber);
                FigureKit.Ball(hand, "Luva_" + tag, new Vector3(0f, -0.055f, 0.01f), new Vector3(0.10f, 0.14f, 0.11f), rubber);

                if (i == 0)
                {
                    // Caneta na mão direita, só enquanto o jogador a usa.
                    Transform grip = FigureKit.Joint(hand, "Caneta", new Vector3(0f, -0.08f, 0.03f), new Vector3(-70f, 0f, 0f));
                    FigureKit.Limb(grip, "Caneta_Ponta", 0.15f, 0.008f, true, rubber);
                    _penBody = grip.Find("Caneta_Peca").GetComponent<Renderer>();
                    _pen = grip.gameObject;
                    _pen.SetActive(false);
                }
                else
                {
                    // Lanterna na mão esquerda, só enquanto está acesa.
                    Transform grip = FigureKit.Joint(hand, "Lanterna", new Vector3(0f, -0.07f, 0.02f), new Vector3(-80f, 0f, 0f));
                    FigureKit.Limb(grip, "Lanterna_Ponta", 0.16f, 0.024f, true, gear);
                    _torch = grip.gameObject;
                    _torch.SetActive(false);
                }
            }

            Pose(0f, 0f, 0f, 0f, 0f, 0f, 0f);
        }
    }
}
