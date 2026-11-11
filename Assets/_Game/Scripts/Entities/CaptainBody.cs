using System.Collections.Generic;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O corpo do Capitão, o inimigo do Level 1 (Docs/level1-mostruario.md, seção 5): a cópia
    /// que o Backrooms fez do dono da loja de móveis vestido com a fantasia de pirata dos
    /// comerciais. Um homem alto demais (2,6 m até o alto da cabeça, 2,8 m com o chapéu),
    /// magro, de braços compridos afastados do corpo e dedos abertos e duros.
    ///
    /// Rosto humano esculpido (<see cref="CaptainHeadMesh"/>): pele escura cheia de vincos,
    /// olhos arregalados de pupila pequena, boca aberta com os dentes de cima à mostra, barba
    /// preta curta. Chapéu de três bicos de feltro marrom gasto com uma fita branca; casaco
    /// comprido sem mangas cor de areia, de botões de latão, com uma mancha de sangue descendo
    /// pela frente; camisa verde-menta de gola pontuda; faixa lilás com a ponta pendurada e cinto
    /// preto largo de fivela de latão; calça escura, bota marrom na direita e perna de pau na
    /// esquerda.
    ///
    /// Por enquanto só aparência e pose: o cérebro dele ainda não existe. A pose sai de
    /// <see cref="Pose"/>, como a da entidade do Level 0, para a ferramenta de prévia poder
    /// fotografar fora do Play.
    /// </summary>
    public sealed class CaptainBody : MonoBehaviour
    {
        [Tooltip("Pele: rosto, pescoço e mãos.")]
        public Material skinMaterial;
        [Tooltip("Vincos do rosto e lábios: pele mais escura.")]
        public Material creaseMaterial;
        [Tooltip("Barba, cabelo e sobrancelhas.")]
        public Material hairMaterial;
        [Tooltip("Branco do olho.")]
        public Material eyeMaterial;
        public Material irisMaterial;
        public Material pupilMaterial;
        public Material toothMaterial;
        [Tooltip("O fundo da boca.")]
        public Material mawMaterial;
        [Tooltip("Chapéu de feltro marrom.")]
        public Material hatMaterial;
        [Tooltip("Fita branca do chapéu.")]
        public Material ribbonMaterial;
        [Tooltip("Casaco comprido cor de areia.")]
        public Material coatMaterial;
        [Tooltip("Camisa verde-menta.")]
        public Material shirtMaterial;
        [Tooltip("Faixa lilás da cintura.")]
        public Material sashMaterial;
        [Tooltip("Cinto de couro preto.")]
        public Material beltMaterial;
        [Tooltip("Fivela e botões.")]
        public Material brassMaterial;
        public Material pantsMaterial;
        public Material bootMaterial;
        [Tooltip("Perna de pau.")]
        public Material woodMaterial;
        [Tooltip("Mancha de sangue no casaco e os cortes do rosto.")]
        public Material bloodMaterial;
        [Tooltip("Hematomas do rosto: roxo escuro.")]
        public Material bruiseMaterial;
        [Tooltip("Remendo do chapéu: pano mais escuro.")]
        public Material patchMaterial;

        [Tooltip("Metros andados por ciclo completo de passada (dois passos).")]
        public float strideLength = 1.9f;

        // A perna de pau é a esquerda (índice 1; 0 é a direita, em +X).
        const int PegLeg = 1;

        const float HipHeight = 1.30f;
        const float ThighLength = 0.64f;
        const float ShinLength = 0.60f;
        const float UpperArmLength = 0.52f;
        const float ForearmLength = 0.47f;
        const float TorsoHeight = 0.78f;
        const float NeckLength = 0.15f;
        const float HeadScale = 1.5f;

        static Mesh _head, _beard, _hair, _maw, _lips, _browR, _browL, _hat, _hatEdge, _coatTorso, _coatSkirt;
        static List<Mesh> _creases, _bruises, _cuts;

        readonly HashSet<Transform> _live = new HashSet<Transform>();

        Transform _rig, _pelvis, _spine, _neck, _headJoint, _mouthJoint, _jaw;
        readonly Transform[] _thigh = new Transform[2];
        readonly Transform[] _shin = new Transform[2];
        readonly Transform[] _foot = new Transform[2];
        readonly Transform[] _upperArm = new Transform[2];
        readonly Transform[] _forearm = new Transform[2];
        readonly Transform[] _hand = new Transform[2];

        Vector3 _lastPosition;
        float _speed;
        float _phase;
        bool _built;

        void Awake()
        {
            Build();
            _lastPosition = transform.position;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 moved = transform.position - _lastPosition;
            _lastPosition = transform.position;
            moved.y = 0f;

            float speed = moved.magnitude / dt;
            if (speed > 14f) speed = 0f;
            _speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-8f * dt));
            _phase += _speed / Mathf.Max(0.5f, strideLength) * dt;
            if (_phase > 1000f) _phase -= 1000f;

            float still = Mathf.Clamp01(1f - _speed / 0.3f);
            Pose(_phase, _speed, still, 0f, Time.time);
        }

        // ---------------------------------------------------------------- Pose

        /// <summary>
        /// Coloca o corpo numa pose. <paramref name="phase"/> é a passada (1 = ciclo completo),
        /// <paramref name="still"/> vai de 0 (andando) a 1 (parado, duro, braços abertos e
        /// dedos esticados) e <paramref name="mouth"/> de 0 (boca entreaberta) a 1 (escancarada).
        /// Público para a ferramenta de prévia, que fotografa fora do Play.
        /// </summary>
        public void Pose(float phase, float speed, float still, float mouth, float time)
        {
            if (!_built) Build();

            float walk = Mathf.Clamp01(speed / 1.7f);
            float rush = Mathf.Clamp01((speed - 1.7f) / 3.5f);
            float cycle = phase * Mathf.PI * 2f;
            float swing = Mathf.Sin(cycle);
            float lift = Mathf.Cos(cycle);
            float breathe = Mathf.Sin(time * 0.9f);
            float pegSide = PegLeg == 0 ? 1f : -1f;

            // Manca: com a perna de pau apoiada o corpo afunda e tomba para ela.
            float pegStance = Mathf.Clamp01(swing * pegSide);
            float otherStance = Mathf.Clamp01(-swing * pegSide);
            _rig.localPosition = new Vector3(0f, (-0.06f * pegStance + 0.02f * otherStance) * walk, 0f);
            _rig.localRotation = Quaternion.Euler(0f, 0f, pegSide * (7f * pegStance - 2f * otherStance) * walk + pegSide * 2.5f * still);

            // Tronco: reto e duro parado; curvado correndo.
            float lean = 3f + 4f * walk + 18f * rush + breathe * 0.5f;
            _spine.localRotation = Quaternion.Euler(lean, swing * 5f * walk, -pegSide * 3f * pegStance * walk);

            // Cabeça: parado, um pouco para a frente e torta, olhando fixo.
            _neck.localRotation = Quaternion.Euler(-lean * 0.6f + 8f * still, 0f, 0f);
            _headJoint.localRotation = Quaternion.Euler(-4f * still - 8f * mouth - 10f * rush, 4f * still, -6f * still);

            // Boca: já entreaberta; escancara para baixo.
            _mouthJoint.localScale = new Vector3(1f + 0.12f * mouth, 1f + 1.1f * mouth, 1f);
            _jaw.localPosition = new Vector3(0f, -0.016f * mouth, -0.004f * mouth);

            float legSwing = Mathf.Lerp(0f, 26f, walk) + 12f * rush;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                float forward = swing * side;
                float knee = Mathf.Max(0f, lift * side) * walk;

                if (i == PegLeg)
                {
                    // A de pau não dobra: gira por fora para passar.
                    _thigh[i].localRotation = Quaternion.Euler(-(3f + forward * legSwing * 0.8f), 0f, side * (4f + 16f * knee + 3f * still));
                    _shin[i].localRotation = Quaternion.Euler(2f, 0f, -side * 6f * knee);
                }
                else
                {
                    _thigh[i].localRotation = Quaternion.Euler(-(5f + forward * legSwing + knee * 18f), 0f, side * (3f + 2f * still));
                    _shin[i].localRotation = Quaternion.Euler(6f + knee * 40f, 0f, 0f);
                    _foot[i].localRotation = Quaternion.Euler(-(2f + knee * 22f), 0f, 0f);
                }
            }

            // Braços: parado, abertos para os lados e um pouco para a frente, cotovelos quase
            // retos, palmas para trás e dedos esticados. Andando, balançam duros. Correndo,
            // vão para a frente, para agarrar.
            float armSwing = Mathf.Lerp(3f, 18f, walk);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                float forward = -swing * side * (1f - still);
                float drift = Mathf.Sin(time * 0.6f + i * 1.7f) * 2f;

                float shoulder = -lean * 0.5f + forward * armSwing + 10f * still + 40f * rush + drift * still;
                float spread = Mathf.Lerp(8f, 17f + 3f * i, still) + 6f * rush;
                float elbow = 8f + 6f * still + 30f * rush + Mathf.Max(0f, forward) * 8f;

                _upperArm[i].localRotation = Quaternion.Euler(-shoulder, -side * 8f * still, side * spread);
                _forearm[i].localRotation = Quaternion.Euler(-elbow, side * 12f * still, -side * 6f * still);
                _hand[i].localRotation = Quaternion.Euler(-6f * still, side * 25f * still, side * 8f * still);
            }
        }

        // ---------------------------------------------------------------- Montagem

        Transform Live(Transform joint)
        {
            _live.Add(joint);
            return joint;
        }

        static Material Or(Material material, Material fallback) => material != null ? material : fallback;

        /// <summary>Monta as peças. Pode ser chamado mais de uma vez; só a primeira constrói.</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;

            EnsureMeshes();

            Material skin = skinMaterial;
            Material coat = Or(coatMaterial, skin);
            Material shirt = Or(shirtMaterial, coat);
            Material pants = Or(pantsMaterial, coat);
            Material belt = Or(beltMaterial, pants);
            Material brass = Or(brassMaterial, coat);
            Material sash = Or(sashMaterial, coat);
            Material boot = Or(bootMaterial, belt);
            Material wood = Or(woodMaterial, boot);
            Material blood = Or(bloodMaterial, coat);

            _rig = Live(FigureKit.Joint(transform, "Capitao", Vector3.zero));
            _pelvis = Live(FigureKit.Joint(_rig, "Bacia", new Vector3(0f, HipHeight, 0f)));

            // Calça escura, faixa lilás amarrada e o cinto largo por cima.
            FigureKit.Ball(_pelvis, "Calca_Bacia", new Vector3(0f, 0.02f, 0f), new Vector3(0.40f, 0.28f, 0.28f), pants);
            FigureKit.Ball(_pelvis, "Faixa", new Vector3(0f, 0.10f, 0f), new Vector3(0.45f, 0.17f, 0.33f), sash);
            FigureKit.Ball(_pelvis, "Faixa_No", new Vector3(0.07f, 0.03f, 0.15f), new Vector3(0.12f, 0.10f, 0.07f), sash);
            FigureKit.Ball(_pelvis, "Cinto", new Vector3(0f, 0.13f, 0f), new Vector3(0.465f, 0.075f, 0.345f), belt);
            FigureKit.Ball(_pelvis, "Fivela", new Vector3(0.0f, 0.13f, 0.172f), new Vector3(0.085f, 0.075f, 0.02f), brass);
            FigureKit.Ball(_pelvis, "Fivela_Miolo", new Vector3(0.0f, 0.13f, 0.18f), new Vector3(0.05f, 0.045f, 0.012f), belt);

            // As pontas da faixa, penduradas até o joelho.
            for (int k = 0; k < 2; k++)
            {
                Transform tail = FigureKit.Joint(_pelvis, "Faixa_Ponta_" + k, new Vector3(0.08f + k * 0.04f, 0.04f, 0.16f), new Vector3(-4f, 0f, 3f + k * 4f));
                tail.localScale = new Vector3(1.5f, 1f, 0.35f);
                FigureKit.Limb(tail, "Faixa_Ponta_Fim_" + k, k == 0 ? 0.62f : 0.42f, 0.045f, true, sash);
            }

            // Saia do casaco, da cintura até abaixo do joelho, aberta na frente.
            Transform skirt = FigureKit.Piece(_pelvis, "Casaco_Saia", _coatSkirt, coat);
            skirt.localPosition = new Vector3(0f, 0.17f, -0.01f);

            BuildLegs(pants, belt, boot, wood, brass);

            // Tronco: o casaco sem mangas, a vista de botões, a mancha de sangue e as lapelas.
            _spine = Live(FigureKit.Joint(_pelvis, "Coluna", new Vector3(0f, 0.10f, 0f)));
            FigureKit.Piece(_spine, "Casaco_Tronco", _coatTorso, coat);

            Transform placket = FigureKit.Joint(_spine, "Vista", new Vector3(0.025f, 0.08f, 0.158f));
            FigureKit.Limb(placket, "Vista_Ponta", 0.56f, 0.022f, false, coat);
            for (int r = 0; r < 6; r++)
                FigureKit.Ball(_spine, "Botao_" + r, new Vector3(0.028f, 0.13f + r * 0.088f, 0.178f), new Vector3(0.022f, 0.022f, 0.012f), brass);

            // Sangue: escorre da gola pela vista, mais grosso no meio.
            FigureKit.Ball(_spine, "Sangue", new Vector3(0.0f, 0.42f, 0.172f), new Vector3(0.035f, 0.42f, 0.012f), blood);
            FigureKit.Ball(_spine, "Sangue_Gota", new Vector3(-0.012f, 0.16f, 0.168f), new Vector3(0.025f, 0.10f, 0.012f), blood);
            FigureKit.Ball(_spine, "Sangue_Respingo", new Vector3(0.09f, 0.32f, 0.162f), new Vector3(0.03f, 0.04f, 0.01f), blood);

            Transform shoulders = FigureKit.Joint(_spine, "Ombros", new Vector3(0f, TorsoHeight - 0.10f, 0f));

            for (int c = 0; c < 2; c++)
            {
                float side = c == 0 ? 1f : -1f;
                // Gola pontuda da camisa, deitada sobre o casaco, as pontas para baixo e para fora.
                Transform collar = FigureKit.Joint(shoulders, "Gola_" + c, new Vector3(side * 0.035f, 0.085f, 0.075f), new Vector3(-38f, 0f, side * 30f));
                FigureKit.Ball(collar, "Gola_Peca_" + c, new Vector3(0f, -0.045f, 0f), new Vector3(0.045f, 0.10f, 0.012f), shirt);
            }

            BuildArms(shoulders, shirt);

            // Pescoço comprido e fino, com o pomo de adão.
            _neck = Live(FigureKit.Joint(shoulders, "Pescoco", new Vector3(0f, 0.05f, 0.01f)));
            Transform top = FigureKit.Limb(_neck, "Pescoco_Ponta", NeckLength, 0.048f, false, skin);
            FigureKit.Ball(_neck, "Pomo", new Vector3(0f, 0.07f, 0.04f), new Vector3(0.025f, 0.035f, 0.025f), skin);
            FigureKit.Ball(_neck, "Gola_Camisa", new Vector3(0f, 0.0f, 0f), new Vector3(0.15f, 0.06f, 0.14f), shirt);

            _headJoint = Live(FigureKit.Joint(top, "Cabeca", new Vector3(0f, -0.02f, 0f)));
            BuildHead(_headJoint);

            // Muitas peças paradas viram uma malha por junta e por material.
            foreach (Transform joint in new List<Transform>(_live)) FigureKit.Bake(joint, _live);

            Pose(0f, 0f, 1f, 0f, 0f);
        }

        void BuildLegs(Material pants, Material belt, Material boot, Material wood, Material brass)
        {
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "D" : "E";

                _thigh[i] = Live(FigureKit.Joint(_pelvis, "Coxa_" + tag, new Vector3(side * 0.11f, -0.02f, 0f)));
                _shin[i] = Live(FigureKit.Limb(_thigh[i], "Joelho_" + tag, ThighLength, 0.085f, true, pants));

                if (i == PegLeg)
                {
                    // Perna de pau: a calça amarrada abaixo do joelho e o toco grosso, gasto.
                    FigureKit.Ball(_shin[i], "Calca_Amarrada", new Vector3(0f, -0.05f, 0f), new Vector3(0.18f, 0.17f, 0.18f), pants);
                    FigureKit.Ball(_shin[i], "Amarra", new Vector3(0f, -0.12f, 0f), new Vector3(0.14f, 0.035f, 0.14f), belt);
                    FigureKit.Ball(_shin[i], "Encaixe", new Vector3(0f, -0.19f, 0f), new Vector3(0.13f, 0.16f, 0.13f), wood);
                    Transform peg = FigureKit.Joint(_shin[i], "Perna_De_Pau", new Vector3(0f, -0.2f, 0f));
                    Transform mid = FigureKit.Limb(peg, "Perna_De_Pau_Meio", 0.24f, 0.05f, true, wood);
                    Transform end = FigureKit.Limb(mid, "Perna_De_Pau_Ponta", 0.13f, 0.038f, true, wood);
                    FigureKit.Ball(end, "Ponteira", new Vector3(0f, 0.012f, 0f), new Vector3(0.075f, 0.04f, 0.075f), brass);
                    _foot[i] = end;
                }
                else
                {
                    // Bota marrom de cano até o meio da canela.
                    _foot[i] = Live(FigureKit.Limb(_shin[i], "Pe_" + tag, ShinLength, 0.07f, true, pants));
                    FigureKit.Ball(_shin[i], "Cano_Da_Bota", new Vector3(0f, -0.43f, 0f), new Vector3(0.15f, 0.34f, 0.15f), boot);
                    FigureKit.Ball(_foot[i], "Bota", new Vector3(0f, 0.0f, 0.07f), new Vector3(0.13f, 0.11f, 0.31f), boot);
                }
            }
        }

        void BuildArms(Transform shoulders, Material shirt)
        {
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "D" : "E";

                _upperArm[i] = Live(FigureKit.Joint(shoulders, "Braco_" + tag, new Vector3(side * 0.27f, -0.02f, 0f)));
                FigureKit.Ball(_upperArm[i], "Manga_Ombro_" + tag, new Vector3(0f, -0.03f, 0f), new Vector3(0.15f, 0.17f, 0.15f), shirt);
                _forearm[i] = Live(FigureKit.Limb(_upperArm[i], "Antebraco_" + tag, UpperArmLength, 0.072f, true, shirt));
                _hand[i] = Live(FigureKit.Limb(_forearm[i], "Mao_" + tag, ForearmLength, 0.062f, true, shirt));

                // Punho da manga, franzido.
                FigureKit.Ball(_hand[i], "Punho_" + tag, new Vector3(0f, 0.05f, 0f), new Vector3(0.13f, 0.1f, 0.13f), shirt);

                BuildHand(_hand[i], side, tag);
            }
        }

        // Mão nua, comprida, de dedos abertos e esticados, um pouco curvos para trás.
        void BuildHand(Transform hand, float side, string tag)
        {
            Material skin = skinMaterial;
            FigureKit.Ball(hand, "Pulso_" + tag, new Vector3(0f, -0.01f, 0f), new Vector3(0.06f, 0.07f, 0.045f), skin);
            FigureKit.Ball(hand, "Palma_" + tag, new Vector3(0f, -0.07f, 0.004f), new Vector3(0.085f, 0.11f, 0.03f), skin);

            for (int f = 0; f < 4; f++)
            {
                float x = (f - 1.5f) * 0.021f;
                float fan = (f - 1.5f) * 9f;
                Transform root = FigureKit.Joint(hand, "Dedo_" + tag + "_" + f, new Vector3(x, -0.12f, 0.004f), new Vector3(8f, 0f, fan));
                float scale = f == 1 || f == 2 ? 1f : 0.85f;
                Transform b = FigureKit.Limb(root, "Dedo_B_" + tag + "_" + f, 0.065f * scale, 0.0095f, true, skin);
                b.localRotation = Quaternion.Euler(6f, 0f, 0f);
                Transform c = FigureKit.Limb(b, "Dedo_C_" + tag + "_" + f, 0.05f * scale, 0.0085f, true, skin);
                c.localRotation = Quaternion.Euler(5f, 0f, 0f);
                FigureKit.Limb(c, "Dedo_D_" + tag + "_" + f, 0.035f * scale, 0.0075f, true, skin);
            }

            Transform thumb = FigureKit.Joint(hand, "Polegar_" + tag, new Vector3(-side * 0.035f, -0.04f, 0.018f), new Vector3(-20f, 0f, -side * 38f));
            Transform t2 = FigureKit.Limb(thumb, "Polegar_B_" + tag, 0.05f, 0.011f, true, skin);
            FigureKit.Limb(t2, "Polegar_C_" + tag, 0.04f, 0.0095f, true, skin);
        }

        void BuildHead(Transform head)
        {
            Material skin = skinMaterial;
            Material crease = Or(creaseMaterial, skin);
            Material hair = Or(hairMaterial, crease);
            Material eye = Or(eyeMaterial, skin);
            Material iris = Or(irisMaterial, hair);
            Material pupil = Or(pupilMaterial, hair);
            Material tooth = Or(toothMaterial, eye);
            Material maw = Or(mawMaterial, hair);
            Material hat = Or(hatMaterial, hair);
            Material ribbon = Or(ribbonMaterial, eye);
            Material bruise = Or(bruiseMaterial, crease);
            Material blood = Or(bloodMaterial, crease);
            Material patch = Or(patchMaterial, hat);

            // O queixo encosta no alto do pescoço.
            Transform center = FigureKit.Joint(head, "Cabeca_Centro", new Vector3(0f, 0.095f * HeadScale, 0.02f));
            center.localScale = Vector3.one * HeadScale;

            FigureKit.Piece(center, "Pele", _head, skin);
            FigureKit.Piece(center, "Barba", _beard, hair);
            FigureKit.Piece(center, "Cabelo", _hair, hair);
            FigureKit.Piece(center, "Sobrancelha_D", _browR, hair);
            FigureKit.Piece(center, "Sobrancelha_E", _browL, hair);
            FigureKit.Piece(center, "Labios", _lips, crease);
            foreach (Mesh line in _creases) FigureKit.Piece(center, "Vinco", line, crease);
            foreach (Mesh spot in _bruises) FigureKit.Piece(center, "Hematoma", spot, bruise);
            foreach (Mesh cut in _cuts) FigureKit.Piece(center, "Corte", cut, blood);

            // Olhos arregalados: globo inteiro à mostra, íris pequena, pupila menor ainda. O
            // esquerdo é caído: mais baixo, meio coberto pela pálpebra inchada, olhando torto.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                bool droop = i == 1;
                Vector2 at = droop ? CaptainHeadMesh.EyeLeft : CaptainHeadMesh.EyeRight;
                Vector3 dir = CaptainHeadMesh.FaceDirection(at.x, at.y);
                Vector3 socket = CaptainHeadMesh.Surface(dir) - dir * (droop ? 0.005f : 0.0035f);
                FigureKit.Ball(center, "Olho_" + i, socket, Vector3.one * (droop ? 0.024f : 0.025f), eye);

                if (droop)
                {
                    // Pálpebra de cima caída e inchada, e a de baixo estufada.
                    FigureKit.Ball(center, "Palpebra", socket + new Vector3(0.0015f, 0.0105f, 0.0035f), new Vector3(0.031f, 0.022f, 0.026f), skin);
                    FigureKit.Ball(center, "Palpebra_Baixo", socket + new Vector3(0f, -0.0125f, 0.002f), new Vector3(0.03f, 0.012f, 0.024f), skin);
                }

                // A íris olha reto para a frente, um pouco para dentro: encara quem está na frente.
                // A do olho caído desvia para fora e para baixo.
                Vector3 look = droop
                    ? new Vector3(-0.16f, -0.08f, 1f).normalized
                    : (Vector3.forward - Vector3.right * side * 0.08f).normalized;
                Transform gaze = FigureKit.Joint(center, "Olhar_" + i, socket, Quaternion.LookRotation(look).eulerAngles);
                FigureKit.Ball(gaze, "Iris_" + i, new Vector3(0f, 0f, 0.0118f), new Vector3(0.0105f, 0.0105f, 0.003f), iris);
                FigureKit.Ball(gaze, "Pupila_" + i, new Vector3(0f, 0f, 0.0128f), new Vector3(0.0042f, 0.0042f, 0.002f), pupil);
            }

            // Boca aberta: o fundo escuro abre a partir da beirada de cima.
            Vector2 m = CaptainHeadMesh.Mouth;
            Vector2 size = CaptainHeadMesh.MouthSize;
            Vector3 mouthTop = CaptainHeadMesh.FacePoint(m.x, m.y + size.y * 0.5f);
            _mouthJoint = Live(FigureKit.Joint(center, "Boca", mouthTop));
            Transform mawPiece = FigureKit.Piece(_mouthJoint, "Fundo_Da_Boca", _maw, maw);
            mawPiece.localPosition = -mouthTop;

            // Dentes de cima, à mostra; os de baixo vão com o maxilar.
            for (int t = 0; t < 8; t++)
            {
                float u = Mathf.Lerp(-0.17f, 0.17f, t / 7f);
                float v = m.y + size.y * 0.62f - 0.05f * u * u;
                FigureKit.Ball(center, "Dente_Cima_" + t, CaptainHeadMesh.FacePoint(u, v, 0.0006f), new Vector3(0.0088f, 0.0115f, 0.006f), tooth, false);
            }

            _jaw = Live(FigureKit.Joint(center, "Maxilar", Vector3.zero));
            for (int t = 0; t < 7; t++)
            {
                float u = Mathf.Lerp(-0.14f, 0.14f, t / 6f);
                float v = m.y - size.y * 0.62f + 0.05f * u * u;
                FigureKit.Ball(_jaw, "Dente_Baixo_" + t, CaptainHeadMesh.FacePoint(u, v, 0.0004f), new Vector3(0.0082f, 0.0085f, 0.006f), tooth, false);
            }

            // Chapéu de três bicos, um pouco caído para trás e de lado, com a fita branca.
            // A malha do chapéu é do tamanho de uma cabeça de 0,5 m: encolhe na largura e
            // menos na altura, para a copa cobrir o alto da cabeça e as abas subirem bem.
            Transform hatJoint = FigureKit.Joint(center, "Chapeu", new Vector3(0f, 0.052f, -0.01f), new Vector3(-9f, 0f, 6f));
            hatJoint.localScale = new Vector3(0.34f, 0.50f, 0.34f);
            FigureKit.Piece(hatJoint, "Chapeu_Feltro", _hat, hat);
            FigureKit.Piece(hatJoint, "Chapeu_Borda", _hatEdge, hat);

            // Gasto: um remendo costurado de pano mais escuro do lado direito da copa e um furo
            // queimado atrás, à esquerda.
            HatSpot(hatJoint, "Remendo", 0.95f, 0.55f, new Vector3(0.10f, 0.075f, 0.012f), patch);
            HatSpot(hatJoint, "Furo", -2.3f, 0.45f, new Vector3(0.045f, 0.035f, 0.01f), Or(mawMaterial, hair));

            FigureKit.Ball(center, "Fita", new Vector3(-0.112f, 0.06f, -0.04f), new Vector3(0.02f, 0.03f, 0.012f), ribbon);
            FigureKit.Ball(center, "Fita_Ponta", new Vector3(-0.117f, 0.035f, -0.036f), new Vector3(0.013f, 0.028f, 0.01f), ribbon);
        }

        // ---------------------------------------------------------------- Malhas (uma vez só)

        static void EnsureMeshes()
        {
            if (_head != null) return;

            _head = CaptainHeadMesh.BuildHead();
            _beard = CaptainHeadMesh.BuildBeard();
            _hair = CaptainHeadMesh.BuildHair();

            Vector2 m = CaptainHeadMesh.Mouth;
            Vector2 size = CaptainHeadMesh.MouthSize;

            // O fundo da boca: um oval escuro dentro da cova da boca.
            float normalize = 1f / new Vector3(0f, m.y, 1f).magnitude;
            _maw = CaptainHeadMesh.BuildDisc("Fundo_Da_Boca", CaptainHeadMesh.FaceDirection(m.x, m.y),
                                             size.x * normalize * 0.98f, 0.0012f, size.y / size.x);

            // Lábios: um anel grosso em volta da boca.
            _lips = CaptainHeadMesh.BuildTube("Labios", t =>
            {
                float a = t * Mathf.PI * 2f;
                return CaptainHeadMesh.FacePoint(m.x + (size.x + 0.012f) * Mathf.Cos(a), m.y + (size.y + 0.012f) * Mathf.Sin(a), 0.0012f);
            }, 64, 0.0036f, true);

            // Sobrancelhas erguidas no alto da testa: cara de espanto que não passa.
            // A direita bem erguida; a esquerda, sobre o olho caído, mais baixa e quebrada no meio.
            _browR = Line("Sobrancelha_D", new[] { new Vector2(0.20f, 0.25f), new Vector2(0.40f, 0.39f), new Vector2(0.64f, 0.29f) }, 0.04f, 0.0022f);
            _browL = Line("Sobrancelha_E", new[] { new Vector2(-0.19f, 0.20f), new Vector2(-0.38f, 0.25f), new Vector2(-0.52f, 0.20f), new Vector2(-0.68f, 0.12f) }, 0.038f, 0.0022f);

            // Hematomas: em volta do olho caído e na maçã inchada, e um na têmpora direita.
            _bruises = new List<Mesh>
            {
                CaptainHeadMesh.BuildDisc("Hematoma_Olho", CaptainHeadMesh.FaceDirection(-0.47f, -0.06f), 0.20f, 0.0012f, 0.85f),
                CaptainHeadMesh.BuildDisc("Hematoma_Testa", CaptainHeadMesh.FaceDirection(0.62f, 0.48f), 0.10f, 0.0012f, 1.2f),
                CaptainHeadMesh.BuildDisc("Hematoma_Queixo", CaptainHeadMesh.FaceDirection(0.30f, -0.95f), 0.08f, 0.0012f, 0.9f),
            };

            // Cortes: um corte comprido na testa com o sangue seco escorrendo, três arranhões na
            // bochecha direita, um na ponte do nariz e o lábio partido.
            _cuts = new List<Mesh>
            {
                Line("Corte_Testa", new[] { new Vector2(0.06f, 0.70f), new Vector2(0.22f, 0.58f), new Vector2(0.40f, 0.47f) }, 0.024f, 0.0012f),
                Line("Corte_Testa_Escorrido", new[] { new Vector2(0.30f, 0.53f), new Vector2(0.31f, 0.42f), new Vector2(0.33f, 0.30f) }, 0.014f, 0.0012f),
                Line("Arranhao_1", new[] { new Vector2(0.44f, -0.20f), new Vector2(0.58f, -0.42f) }, 0.012f, 0.0012f),
                Line("Arranhao_2", new[] { new Vector2(0.50f, -0.17f), new Vector2(0.64f, -0.38f) }, 0.012f, 0.0012f),
                Line("Arranhao_3", new[] { new Vector2(0.56f, -0.15f), new Vector2(0.68f, -0.32f) }, 0.010f, 0.0012f),
                Line("Corte_Nariz", new[] { new Vector2(-0.05f, 0.13f), new Vector2(0.06f, 0.09f) }, 0.016f, 0.0016f),
                Line("Labio_Partido", new[] { new Vector2(0.13f, -0.84f), new Vector2(0.16f, -0.93f) }, 0.016f, 0.0042f),
            };

            // Vincos fundos, como madeira rachada: testa, entre as sobrancelhas, olheiras, rugas
            // descendo pelas bochechas, pés de galinha e o sulco do nariz até a boca.
            _creases = new List<Mesh>();
            float[] foreheads = { 0.47f, 0.57f, 0.67f };
            for (int k = 0; k < foreheads.Length; k++)
            {
                float v = foreheads[k];
                _creases.Add(Line("Vinco_Testa_" + k, new[]
                {
                    new Vector2(-0.48f, v - 0.03f), new Vector2(-0.2f, v + 0.02f), new Vector2(0.05f, v - 0.01f),
                    new Vector2(0.28f, v + 0.025f), new Vector2(0.5f, v - 0.02f)
                }, 0.016f, 0.0006f));
            }

            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? 1f : -1f;
                _creases.Add(Line("Vinco_Entre_" + i, new[] { new Vector2(s * 0.07f, 0.14f), new Vector2(s * 0.06f, 0.32f) }, 0.014f, 0.0006f));
                _creases.Add(Line("Olheira_" + i, new[] { new Vector2(s * 0.26f, -0.07f), new Vector2(s * 0.42f, -0.14f), new Vector2(s * 0.6f, -0.07f) }, 0.018f, 0.0006f));
                _creases.Add(Line("Olheira2_" + i, new[] { new Vector2(s * 0.3f, -0.15f), new Vector2(s * 0.44f, -0.22f), new Vector2(s * 0.58f, -0.17f) }, 0.012f, 0.0006f));
                _creases.Add(Line("Ruga_Bochecha_" + i, new[] { new Vector2(s * 0.38f, -0.18f), new Vector2(s * 0.43f, -0.34f), new Vector2(s * 0.41f, -0.5f) }, 0.014f, 0.0006f));
                _creases.Add(Line("Ruga_Bochecha2_" + i, new[] { new Vector2(s * 0.52f, -0.2f), new Vector2(s * 0.58f, -0.36f) }, 0.012f, 0.0006f));
                _creases.Add(Line("Pe_De_Galinha_" + i, new[] { new Vector2(s * 0.64f, 0.1f), new Vector2(s * 0.78f, 0.14f) }, 0.011f, 0.0006f));
                _creases.Add(Line("Pe_De_Galinha2_" + i, new[] { new Vector2(s * 0.64f, 0.0f), new Vector2(s * 0.78f, -0.04f) }, 0.011f, 0.0006f));
                _creases.Add(Line("Sulco_" + i, new[] { new Vector2(s * 0.2f, -0.36f), new Vector2(s * 0.3f, -0.5f), new Vector2(s * 0.34f, -0.6f) }, 0.016f, 0.0006f));
            }

            const float crown = 0.27f;
            _hat = CaptainHeadMesh.BuildHatShell(crown, 0.17f);
            _hatEdge = CaptainHeadMesh.BuildTube("Borda_Do_Chapeu",
                t => CaptainHeadMesh.BrimEdge(t * Mathf.PI * 2f, crown), 140, 0.012f, true);

            _coatTorso = CaptainHeadMesh.BuildCoatTorso(TorsoHeight, 0.40f, 0.54f, 0.30f);
            _coatSkirt = CaptainHeadMesh.BuildCoatSkirt(0.98f, 0.46f, 0.64f, 0.33f);
        }

        // Uma peça chata assentada na copa do chapéu (remendo, furo), no ângulo e altura dados
        // (t = 0 no topo, 1 na base). Mesmas medidas da malha do chapéu (copa de 0,27 x 0,17).
        static void HatSpot(Transform hat, string name, float angle, float t, Vector3 size, Material material)
        {
            const float crown = 0.27f, height = 0.17f;
            float r = crown * Mathf.Sin(t * Mathf.PI * 0.5f) * 1.02f;
            float y = height * Mathf.Cos(t * Mathf.PI * 0.5f) * (1f - 0.08f * t);
            var p = new Vector3(Mathf.Sin(angle) * r * 1.04f, y, Mathf.Cos(angle) * r);
            var normal = new Vector3(p.x / (crown * crown * 1.08f), p.y / (height * height), p.z / (crown * crown)).normalized;
            Transform spot = FigureKit.Joint(hat, name, p + normal * 0.004f, Quaternion.LookRotation(normal).eulerAngles);
            FigureKit.Ball(spot, name + "_Peca", Vector3.zero, size, material, false);
        }

        // Uma linha em relevo baixo sobre o rosto, passando pelos pontos (u, v) dados.
        static Mesh Line(string name, Vector2[] points, float width, float lift)
        {
            return CaptainHeadMesh.BuildBand(name, 12 * (points.Length - 1), 2,
                t =>
                {
                    float k = t * (points.Length - 1);
                    int a = Mathf.Min(Mathf.FloorToInt(k), points.Length - 2);
                    float f = Mathf.SmoothStep(0f, 1f, k - a);
                    Vector2 p = Vector2.Lerp(points[a], points[a + 1], f);
                    Vector2 along = (points[a + 1] - points[a]).normalized;
                    var side = new Vector3(-along.y, along.x, 0f);
                    return (CaptainHeadMesh.FaceDirection(p.x, p.y), side);
                },
                (t, s) => width * Mathf.Sin(Mathf.PI * Mathf.Clamp(t, 0.04f, 0.96f)),
                (t, s) => lift);
        }
    }
}
