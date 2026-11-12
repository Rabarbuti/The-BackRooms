using System;
using System.Collections.Generic;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O corpo da entidade, montado em código: uma figura de 3,3 m feita de galhos e cipós
    /// pretos torcidos uns nos outros — dá para ver através do tronco e dos membros —, com
    /// raízes no lugar dos pés, dedos de galho e cipós soltos pendurados nas costas.
    ///
    /// A cabeça é comprida, sem olhos, esticada mais de 1 m para trás: crânio e mandíbula são
    /// malhas esculpidas (<see cref="EntityHeadMesh"/>), com dentes em cone. A boca fica quase
    /// fechada quando ela está parada, entreabre quando caça e abre no bote.
    ///
    /// São centenas de peças; depois de montadas, as de cada junta são fundidas numa malha só
    /// (<see cref="FigureKit.Bake"/>). A animação é em código e sai da velocidade com que o
    /// objeto se desloca, então funciona igual no host e nos clientes, sem sincronizar nada
    /// além da posição e do estado.
    /// </summary>
    public sealed class EntityBody : MonoBehaviour
    {
        [Tooltip("Casca dos cipós: fosca, com sulcos.")]
        public Material bodyMaterial;
        [Tooltip("Casco da cabeça: lustroso, com estrias e veias.")]
        public Material shellMaterial;
        [Tooltip("Dentes.")]
        public Material boneMaterial;
        [Tooltip("Língua e garganta.")]
        public Material mawMaterial;

        [Tooltip("Metros andados por ciclo completo de passada (dois passos).")]
        public float strideLength = 2.8f;

        [Header("Modelo do Blender")]
        [Tooltip("Malha esculpida no Blender (Tools/Blender/personagens), vestida sobre as juntas. Vazio: o corpo antigo, montado em código.")]
        public TextAsset model;
        [Tooltip("Materiais do modelo, um por parte (o nome termina com o nome da parte).")]
        public Material[] modelMaterials;

        /// <summary>
        /// Só para a ferramenta que exporta o esqueleto para o Blender: monta as juntas na pose de
        /// encaixe e não funde nem tira nenhuma peça.
        /// </summary>
        public static bool KeepAllJoints;

        /// <summary>Disparado a cada pé que bate no chão, com a velocidade do momento.</summary>
        public event Action<float> Stepped;

        const float HipHeight = 1.74f;
        const float ThighLength = 0.90f;
        const float ShinLength = 0.92f;
        const float UpperArmLength = 0.95f;
        const float ForearmLength = 1.05f;
        const float AttackSeconds = 1.9f;
        const int Tendrils = 9;

        static Mesh _skullMesh, _jawMesh, _fangMesh;

        readonly System.Random _rng = new System.Random(977);
        readonly HashSet<Transform> _live = new HashSet<Transform>();

        Transform _rig, _spineLow, _spineHigh, _neck, _head, _jaw;
        readonly Transform[] _thigh = new Transform[2];
        readonly Transform[] _shin = new Transform[2];
        readonly Transform[] _foot = new Transform[2];
        readonly Transform[] _upperArm = new Transform[2];
        readonly Transform[] _forearm = new Transform[2];
        readonly Transform[] _tendril = new Transform[Tendrils];
        readonly float[] _tendrilSeed = new float[Tendrils];

        Vector3 _lastPosition;
        float _speed;
        float _phase;
        float _hunt;
        float _attack;
        bool _hunting;
        Quaternion _twitch = Quaternion.identity;
        Quaternion _twitchTarget = Quaternion.identity;
        float _nextTwitch;
        bool _built;

        void Awake()
        {
            Build();
            _lastPosition = transform.position;
        }

        void OnEnable()
        {
            if (TryGetComponent(out EntityBrain brain)) brain.StateChanged += OnStateChanged;
        }

        void OnDisable()
        {
            if (TryGetComponent(out EntityBrain brain)) brain.StateChanged -= OnStateChanged;
        }

        void OnStateChanged(EntityState state)
        {
            _hunting = state == EntityState.Chase || state == EntityState.Investigate;
            if (state == EntityState.Feed) _attack = 1f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 moved = transform.position - _lastPosition;
            _lastPosition = transform.position;
            moved.y = 0f;

            // Um salto grande é a rede recolocando o objeto, não corrida.
            float speed = moved.magnitude / dt;
            if (speed > 14f) speed = 0f;
            _speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-8f * dt));
            _hunt = Mathf.MoveTowards(_hunt, _hunting ? 1f : 0f, dt * 2.5f);
            _attack = Mathf.MoveTowards(_attack, 0f, dt / AttackSeconds);

            float before = _phase;
            _phase += _speed / Mathf.Max(0.5f, strideLength) * dt;
            if (_speed > 0.5f && Mathf.FloorToInt(_phase * 2f) != Mathf.FloorToInt(before * 2f)) Stepped?.Invoke(_speed);
            if (_phase > 1000f) _phase -= 1000f;

            // Tique da cabeça: de tempos em tempos ela pula para um ângulo torto.
            if (Time.time >= _nextTwitch)
            {
                float wide = Mathf.Lerp(6f, 18f, _hunt);
                _twitchTarget = Quaternion.Euler(UnityEngine.Random.Range(-wide, wide) * 0.5f, UnityEngine.Random.Range(-wide, wide), UnityEngine.Random.Range(-wide, wide));
                _nextTwitch = Time.time + UnityEngine.Random.Range(0.08f, Mathf.Lerp(1.4f, 0.35f, _hunt));
            }
            _twitch = Quaternion.Slerp(_twitch, _twitchTarget, 1f - Mathf.Exp(-30f * dt));

            Pose(_phase, _speed, _hunt, Time.time, _attack);
        }

        // ---------------------------------------------------------------- Pose

        /// <summary>
        /// Coloca o corpo numa pose. <paramref name="phase"/> é a passada (1 = ciclo completo),
        /// <paramref name="hunt"/> vai de 0 (vagando) a 1 (caçando) e <paramref name="attack"/>
        /// de 0 a 1 (no auge do bote). Público para a ferramenta de prévia, que fotografa
        /// fora do Play.
        /// </summary>
        public void Pose(float phase, float speed, float hunt, float time, float attack = 0f)
        {
            if (!_built) Build();

            float run = Mathf.Clamp01(speed / 4.6f);
            float cycle = phase * Mathf.PI * 2f;
            float swing = Mathf.Sin(cycle);
            float lift = Mathf.Cos(cycle);
            float breathe = Mathf.Sin(time * 1.3f);
            float shake = Mathf.Sin(time * 41f) * attack;

            // Corpo: sobe e desce a cada passo, inclina para a frente correndo.
            float bob = -Mathf.Abs(Mathf.Sin(cycle)) * 0.07f * run;
            _rig.localPosition = new Vector3(0f, bob, 0f);
            _rig.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(cycle) * 2.5f * run + shake * 1.5f);

            // O quanto o tronco está curvado para a frente. Braços, pescoço e cipós soltos
            // nascem dos ombros e descontam esse ângulo, para pender em relação ao chão e não
            // ao tronco. No bote ela se ergue e joga o peito para a frente.
            float leanLow = 18f + 12f * run + breathe * 1.2f - 10f * attack;
            float leanHigh = 22f + 8f * hunt + breathe * 1.5f + 6f * attack;
            float lean = leanLow + leanHigh;

            _spineLow.localRotation = Quaternion.Euler(leanLow, swing * 5f * run, 0f);
            _spineHigh.localRotation = Quaternion.Euler(leanHigh, -swing * 7f * run, 4f);

            // O pescoço vai para a frente e a cabeça pende: o crânio comprido fica quase
            // deitado sobre as costas. No bote ela ergue o rosto.
            _neck.localRotation = Quaternion.Euler(-lean + 34f - 6f * hunt - 14f * attack, 0f, -4f);
            _head.localRotation = _twitch * Quaternion.Euler(-12f - 4f * hunt - 26f * attack, shake * 4f, 6f);

            // Boca quase fechada parada (os dentes se cruzam), entreaberta caçando, aberta no bote.
            _jaw.localRotation = Quaternion.Euler(2.5f + breathe * 1.2f + 10f * hunt + 22f * attack + shake * 3f, 0f, 0f);

            // Pernas: coxa para a frente, canela dobrada para trás. A que avança levanta o joelho.
            float legSwing = Mathf.Lerp(4f, 40f, run);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                float forward = swing * side;
                float knee = Mathf.Max(0f, lift * side);

                _thigh[i].localRotation = Quaternion.Euler(-(22f + forward * legSwing + knee * 14f * run), 0f, side * 3f);
                _shin[i].localRotation = Quaternion.Euler(44f + knee * 46f * run + Mathf.Max(0f, -forward) * 10f * run, 0f, 0f);
                _foot[i].localRotation = Quaternion.Euler(-22f - knee * 20f * run, 0f, 0f);
            }

            // Braços: pendurados e balançando ao contrário das pernas. Caçando, os dois vão
            // para a frente e balançam menos; no bote, sobem abertos para agarrar.
            float armSwing = Mathf.Lerp(3f, 24f, run);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                float forward = -swing * side;
                float sway = Mathf.Sin(time * 0.9f + i * 2.1f) * 3f;

                float reach = 6f + 30f * hunt + 48f * attack + forward * armSwing * (1f - 0.45f * hunt) * (1f - attack);
                _upperArm[i].localRotation = Quaternion.Euler(-lean - reach + sway, 0f, side * (7f + 4f * hunt + 16f * attack));
                _forearm[i].localRotation = Quaternion.Euler(-(12f + 16f * hunt + 26f * attack + Mathf.Max(0f, forward) * 10f * run), 0f, -side * 6f);
            }

            // Cipós soltos: balançam sozinhos e ficam para trás quando ela corre.
            for (int i = 0; i < Tendrils; i++)
            {
                float seed = _tendrilSeed[i];
                float waveX = Mathf.Sin(time * (0.7f + seed * 0.9f) + seed * 17f) * 9f;
                float waveZ = Mathf.Sin(time * (0.5f + seed * 1.1f) + seed * 31f) * 9f;
                _tendril[i].localRotation = Quaternion.Euler(-lean + 10f + waveX + 55f * run, 0f, waveZ);
            }
        }

        // ---------------------------------------------------------------- Montagem

        float Rnd() { return (float)_rng.NextDouble(); }

        Transform Live(Transform joint)
        {
            _live.Add(joint);
            return joint;
        }

        /// <summary>Monta as peças. Pode ser chamado mais de uma vez; só a primeira constrói.</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;

            if (shellMaterial == null) shellMaterial = bodyMaterial;
            if (boneMaterial == null) boneMaterial = bodyMaterial;
            if (mawMaterial == null) mawMaterial = bodyMaterial;

            _rig = Live(FigureKit.Joint(transform, "Corpo", Vector3.zero));

            Transform pelvis = Live(FigureKit.Joint(_rig, "Bacia", new Vector3(0f, HipHeight, 0f)));
            Knot(pelvis, Vector3.zero, 0.17f);

            // Pernas: cipós grossos torcidos em cada trecho, e raízes no lugar dos pés.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "E" : "D";

                _thigh[i] = Live(FigureKit.Joint(pelvis, "Coxa_" + tag, new Vector3(side * 0.13f, 0f, 0f)));
                _shin[i] = Live(Bundle(_thigh[i], "Canela_" + tag, ThighLength, 0.085f, 4, 0.036f, 0.8f, true, 0.3f));
                Knot(_shin[i], Vector3.zero, 0.10f);
                _foot[i] = Live(Bundle(_shin[i], "Pe_" + tag, ShinLength, 0.06f, 3, 0.03f, 1.1f, true, 0.2f));

                for (int root = 0; root < 4; root++)
                {
                    float spread = (root - 1.5f) * 34f + side * 8f;
                    Vector3 direction = Quaternion.Euler(0f, spread, 0f) * new Vector3(0f, -0.18f, 1f).normalized;
                    Twig(_foot[i], new Vector3(0f, 0.02f, 0f), direction, 0.20f + Rnd() * 0.16f, 0.018f, 2);
                }
            }

            // Tronco: um feixe largo de cipós, estreito na cintura e aberto no peito, com vãos.
            _spineLow = Live(FigureKit.Joint(pelvis, "Coluna_Baixa", new Vector3(0f, 0.05f, 0f)));
            _spineHigh = Live(Bundle(_spineLow, "Coluna_Alta", 0.62f, 0.135f, 7, 0.03f, 0.55f, false, 0.9f));
            Transform shoulders = Live(Bundle(_spineHigh, "Ombros", 0.62f, 0.24f, 11, 0.028f, 0.5f, false, 1.0f));

            // Um cerne mais fino por dentro, para o tronco não ficar oco demais.
            Bundle(_spineLow, "Cerne_Baixo", 0.62f, 0.035f, 2, 0.03f, 1.4f, false, 0f);
            Bundle(_spineHigh, "Cerne_Alto", 0.62f, 0.05f, 3, 0.028f, 1.2f, false, 0f);

            Knot(shoulders, Vector3.zero, 0.15f);

            BuildHead(shoulders);

            // Braços: cipós torcidos, bem compridos — o esquerdo mais comprido e com o ombro
            // mais alto —, terminando em dedos de galho.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "E" : "D";
                float stretch = i == 0 ? 1.1f : 1f;
                float raise = i == 0 ? 0.04f : -0.06f;

                Knot(shoulders, new Vector3(side * 0.27f, raise, 0f), i == 0 ? 0.15f : 0.12f);
                _upperArm[i] = Live(FigureKit.Joint(shoulders, "Braco_" + tag, new Vector3(side * 0.30f, raise, 0f)));
                _forearm[i] = Live(Bundle(_upperArm[i], "Antebraco_" + tag, UpperArmLength * stretch, 0.05f, 3, 0.026f, 1.0f, true, 0.25f));
                Knot(_forearm[i], Vector3.zero, 0.075f);
                Transform hand = Bundle(_forearm[i], "Mao_" + tag, ForearmLength * stretch, 0.032f, 2, 0.019f, 1.3f, true, 0.15f);

                // Dedos: galhos finos e tortos, de tamanhos diferentes, um deles bifurcado.
                for (int finger = 0; finger < 4; finger++)
                {
                    Vector3 direction = Quaternion.Euler(-10f + finger * 6f, 0f, (finger - 1.5f) * 16f) * Vector3.down;
                    Twig(hand, new Vector3((finger - 1.5f) * 0.02f, 0f, 0f), direction, 0.30f + Rnd() * 0.24f, 0.012f, finger == 1 ? 3 : 2);
                }
            }

            // Cipós soltos pendurados dos ombros e das costas.
            for (int i = 0; i < Tendrils; i++)
            {
                _tendrilSeed[i] = Rnd();
                Transform anchor = i < 6 ? shoulders : _spineHigh;
                var offset = new Vector3((Rnd() - 0.5f) * 0.46f, -Rnd() * 0.3f, -0.10f - Rnd() * 0.12f);

                _tendril[i] = Live(FigureKit.Joint(anchor, "Cipo_Solto_" + i, offset));
                Transform middle = FigureKit.Limb(_tendril[i], "Cipo_Solto_Meio_" + i, 0.35f + Rnd() * 0.4f, 0.012f, true, bodyMaterial);
                middle.localRotation = Quaternion.Euler((Rnd() - 0.5f) * 30f, 0f, (Rnd() - 0.5f) * 30f);
                FigureKit.Limb(middle, "Cipo_Solto_Ponta_" + i, 0.3f + Rnd() * 0.45f, 0.008f, true, bodyMaterial);
            }

            // Centenas de hastes soltas viram uma malha por junta e por material.
            // Com modelo, as hastes de código saem e fica só o esqueleto; sem, as centenas de
            // hastes soltas viram uma malha por junta e por material.
            bool dressed = model != null && !KeepAllJoints;
            if (!KeepAllJoints)
                foreach (Transform joint in new List<Transform>(_live))
                    if (dressed) FigureKit.Strip(joint, _live); else FigureKit.Bake(joint, _live);

            // A pose parada é a de encaixe: foi nela que o Blender esculpiu o modelo.
            Pose(0f, 0f, 0f, 0f);
            if (dressed) CharacterModel.Attach(transform, model, modelMaterials);
        }

        // Cabeça comprida, esticada para trás, sem olhos: crânio e mandíbula esculpidos como
        // malha, dentes em cone e cipós agarrados ao casco. No espaço da cabeça, +Y é para
        // cima e +Z é para onde o rosto aponta.
        void BuildHead(Transform shoulders)
        {
            if (_skullMesh == null) _skullMesh = EntityHeadMesh.BuildSkull();
            if (_jawMesh == null) _jawMesh = EntityHeadMesh.BuildJaw();
            if (_fangMesh == null) _fangMesh = EntityHeadMesh.BuildFang();

            // Pescoço grosso e fechado: um cerne maciço com os cipós enrolados em volta, sem vãos.
            _neck = Live(FigureKit.Joint(shoulders, "Pescoco", new Vector3(0f, 0.02f, 0.02f)));
            FigureKit.Limb(_neck, "Pescoco_Cerne", 0.40f, 0.062f, false, bodyMaterial);
            _head = Live(Bundle(_neck, "Cabeca", 0.38f, 0.06f, 7, 0.024f, 1.3f, false, 0.1f));

            FigureKit.Piece(_head, "Cranio", _skullMesh, shellMaterial);

            // Nuca: o pescoço continua para dentro da base do crânio, atrás da mandíbula, com
            // cipós em volta. Sem isso a cabeça ficava solta, um palmo acima do pescoço.
            Transform nape = FigureKit.Joint(_head, "Nuca", new Vector3(0f, -0.04f, -0.04f), new Vector3(-8f, 0f, 0f));
            FigureKit.Limb(nape, "Nuca_Cerne", 0.22f, 0.072f, false, bodyMaterial);
            Bundle(nape, "Nuca_Fim", 0.2f, 0.085f, 7, 0.02f, 0.7f, false, 0f);

            // Dentes de cima: seguem a beirada do focinho em ferradura, desiguais, alguns enormes.
            const int upper = 15;
            for (int tooth = 0; tooth < upper; tooth++)
            {
                float across = Mathf.Lerp(-1f, 1f, tooth / (float)(upper - 1));
                // A fileira acaba onde a boca acaba: daí para trás o rosto é fechado.
                Vector3 gum = EntityHeadMesh.UpperGum(0.012f + 0.095f * across * across, across);
                float length = 0.03f + Rnd() * 0.05f + (tooth == 4 || tooth == 10 ? 0.06f : 0f);
                Fang(_head, gum, length, 0.009f + Rnd() * 0.004f, false, across);
            }

            // Mandíbula: encaixada por baixo do crânio, sem fresta, girando no fundo da boca.
            // Só o terço da frente se abre; as bochechas e o fundo ficam sempre tapados.
            _jaw = Live(FigureKit.Joint(_head, "Mandibula", EntityHeadMesh.JawHinge));
            FigureKit.Piece(_jaw, "Mandibula_Peca", _jawMesh, shellMaterial);

            // Língua e garganta: o vermelho-escuro fica só lá dentro, visto pela frente.
            Transform tongue = FigureKit.Ball(_jaw, "Lingua", EntityHeadMesh.JawTop(0.8f) + new Vector3(0f, 0.004f, 0f), new Vector3(0.05f, 0.014f, 0.15f), mawMaterial, false);
            tongue.localRotation = Quaternion.Euler(-9f, 0f, 0f);
            FigureKit.Ball(_jaw, "Garganta", EntityHeadMesh.JawTop(0.64f) + new Vector3(0f, -0.012f, 0f), new Vector3(0.075f, 0.05f, 0.03f), mawMaterial, false);

            const int lower = 13;
            for (int tooth = 0; tooth < lower; tooth++)
            {
                float across = Mathf.Lerp(-1f, 1f, tooth / (float)(lower - 1));
                Vector3 gum = EntityHeadMesh.LowerGum(0.975f - 0.27f * across * across, across);
                float length = 0.025f + Rnd() * 0.04f + (tooth == 3 || tooth == 9 ? 0.05f : 0f);
                Fang(_jaw, gum, length, 0.008f + Rnd() * 0.004f, true, across);
            }

            // Fios de baba pendurados nos cantos da boca.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                Transform thread = FigureKit.Joint(_head, "Fio_Da_Boca_" + i, EntityHeadMesh.UpperGum(0.085f, side * 0.85f));
                thread.localRotation = Quaternion.Euler(-10f, 0f, side * 6f);
                FigureKit.Limb(thread, "Fio_Da_Boca_Fim_" + i, 0.04f, 0.0035f, true, boneMaterial, false);
            }

            // Cipós que sobem do pescoço e correm por cima do casco até a ponta de trás.
            for (int vine = 0; vine < 6; vine++)
            {
                float side = vine % 2 == 0 ? 1f : -1f;
                float from = -62f - Rnd() * 30f;
                float to = 18f + Rnd() * 58f;
                float reach = 0.62f + Rnd() * 0.33f;
                float thick = 0.016f - vine * 0.0012f;
                const int steps = 10;

                Vector3 previous = Vector3.zero;
                for (int k = 0; k <= steps; k++)
                {
                    float f = k / (float)steps;
                    float s = Mathf.Lerp(0.2f, reach, f);
                    float degrees = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, f)) + Mathf.Sin(f * 9f + vine) * 7f;
                    if (side < 0f) degrees = 180f - degrees;

                    Vector3 point = EntityHeadMesh.SurfacePoint(s, degrees, thick * 0.5f);
                    if (k > 0) Stretch(_head, previous, point, thick * (1f - 0.55f * f));
                    previous = point;
                }
            }

            // Espinhos curtos de galho ao longo da crista, da metade para trás.
            for (int thorn = 0; thorn < 7; thorn++)
            {
                float s = 0.36f + thorn * 0.085f;
                Vector3 baseOf = EntityHeadMesh.SurfacePoint(s, 90f + (Rnd() - 0.5f) * 16f, -0.004f);
                Vector3 direction = new Vector3((Rnd() - 0.5f) * 0.35f, 0.75f, -0.65f).normalized;
                Twig(_head, baseOf, direction, 0.05f + Rnd() * 0.07f, 0.009f, 1);
            }
        }

        // Um dente: cone curvo esticado, cravado na gengiva e um pouco torto.
        void Fang(Transform parent, Vector3 gum, float length, float radius, bool up, float across)
        {
            Transform tooth = FigureKit.Piece(parent, "Dente", _fangMesh, boneMaterial, false);
            tooth.localPosition = gum;
            tooth.localScale = new Vector3(radius, length, radius);

            // Os dos lados abrem para fora; todos entortam um pouco. O de baixo é o mesmo cone de ponta-cabeça.
            Quaternion lean = Quaternion.Euler((Rnd() - 0.5f) * 20f + (up ? 10f : -10f), Rnd() * 360f, across * -16f + (Rnd() - 0.5f) * 14f);
            tooth.localRotation = (up ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity) * lean;
        }

        // Um membro feito de cipós: várias hastes finas que sobem em espiral em volta do eixo
        // do membro, juntas nas pontas e abertas no meio. Devolve a junta da ponta.
        Transform Bundle(Transform joint, string endName, float length, float radius, int strands, float thickness,
                         float turns, bool down, float twigs)
        {
            float direction = down ? -1f : 1f;
            int segments = Mathf.Max(3, Mathf.CeilToInt(length / 0.17f));

            for (int strand = 0; strand < strands; strand++)
            {
                float phase = Mathf.PI * 2f * strand / strands + Rnd() * 0.7f;
                float wide = 0.7f + Rnd() * 0.6f;
                float thick = thickness * (0.75f + Rnd() * 0.5f);
                float spin = turns * (0.8f + Rnd() * 0.4f) * (strand % 2 == 0 ? 1f : -0.6f);
                Vector3 previous = Vector3.zero;

                for (int k = 0; k <= segments; k++)
                {
                    float t = k / (float)segments;
                    // Fechado nas pontas, aberto no meio, com um pouco de irregularidade.
                    float open = radius * wide * (0.35f + 0.65f * Mathf.Sin(Mathf.PI * t)) * (0.85f + Rnd() * 0.3f);
                    float angle = phase + spin * Mathf.PI * 2f * t;
                    var point = new Vector3(Mathf.Cos(angle) * open, direction * length * t, Mathf.Sin(angle) * open);

                    if (k > 0)
                    {
                        Stretch(joint, previous, point, thick);

                        // Raminhos: saem para fora do feixe, curtos e pontudos.
                        if (Rnd() < twigs * 0.22f)
                        {
                            Vector3 outward = new Vector3(point.x, 0f, point.z).normalized + new Vector3(0f, (Rnd() - 0.3f) * 0.9f, 0f);
                            Twig(joint, point, outward.normalized, 0.07f + Rnd() * 0.16f, thick * 0.45f, 1);
                        }
                    }

                    previous = point;
                }
            }

            return FigureKit.Joint(joint, endName, new Vector3(0f, direction * length, 0f));
        }

        // Uma haste reta entre dois pontos do espaço da junta.
        void Stretch(Transform parent, Vector3 from, Vector3 to, float radius)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.00001f) return;

            Transform root = FigureKit.Joint(parent, "Haste", from);
            root.localRotation = Quaternion.FromToRotation(Vector3.down, delta.normalized);
            FigureKit.Limb(root, "Haste_Fim", delta.magnitude, radius, true, bodyMaterial);
        }

        // Um galho: sai de um ponto numa direção, entorta a cada trecho e afina. Com mais de
        // dois trechos, solta um ramo para o lado.
        void Twig(Transform parent, Vector3 from, Vector3 direction, float length, float radius, int pieces)
        {
            Transform root = FigureKit.Joint(parent, "Galho", from);
            root.localRotation = Quaternion.FromToRotation(Vector3.down, direction.normalized);

            Transform tip = root;
            for (int piece = 0; piece < pieces; piece++)
            {
                float part = length / pieces * (1f - piece * 0.15f);
                Transform next = FigureKit.Limb(tip, "Galho_" + piece, part, radius * (1f - piece * 0.28f), true, bodyMaterial);
                next.localRotation = Quaternion.Euler((Rnd() - 0.5f) * 44f, 0f, (Rnd() - 0.5f) * 44f);

                if (piece == 1 && pieces > 2)
                {
                    Transform fork = FigureKit.Joint(next, "Galho_Ramo", Vector3.zero, new Vector3(38f, 0f, 30f));
                    FigureKit.Limb(fork, "Galho_Ramo_Fim", part * 0.7f, radius * 0.5f, true, bodyMaterial);
                }

                tip = next;
            }
        }

        // Nó de madeira onde os cipós se juntam.
        void Knot(Transform parent, Vector3 position, float size)
        {
            FigureKit.Ball(parent, "No", position, new Vector3(size, size * 0.85f, size * 0.9f), bodyMaterial);
            FigureKit.Ball(parent, "No_Menor", position + new Vector3(size * 0.25f, size * 0.2f, -size * 0.2f), Vector3.one * size * 0.6f, bodyMaterial);
        }
    }
}
