using System;
using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.World;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O corpo do Zelador (The Janitor), o inimigo do escuro do Level 3: um homem magro e alto
    /// demais (2,25 m esticado; curvado, uns 2 m), de macacão de serviço cinza-esverdeado encharcado
    /// na barra, botas de borracha, braços que passam do joelho e a cabeça pendurada para a frente,
    /// abaixo da linha dos ombros, torta para um lado. O gorro de lã puxado até o nariz tapa os olhos;
    /// a boca, larga demais, fica entreaberta. Na mão direita arrasta o esfregão molhado pelo cabo; no
    /// cinto, à esquerda, o molho de chaves que tilinta a cada passo.
    ///
    /// Aparência: o modelo esculpido no Blender (<see cref="model"/>,
    /// Tools/Blender/personagens/zelador.py), vestido sobre as juntas montadas aqui; vazio, as peças de
    /// código (cápsulas e esferas). A pose sai de <see cref="Pose"/> (pública para a prévia). Aparecer
    /// e sumir: ele sobe do chão (e afunda nele) em vez de piscar na tela; escondido, sem malha e sem
    /// colisor.
    ///
    /// Encaixe na fase (<see cref="Fit"/>, em cada máquina, a cada quadro; só visual): o esfregão
    /// arrastado atrás e de lado sai do raio que o cérebro confere (1,3 m de cabo) e entrava na parede
    /// rente a ela, virando, no vão de porta e parado na beira da luz. Onde bateria, ele passa a
    /// arrastá-lo reto atrás (no rastro por onde veio) e, se nem assim cabe, levanta-o em pé ao lado do
    /// ombro; sem pressa, com folga para começar antes de encostar, e solta devagar. O braço esquerdo
    /// esticado na caça e a cabeça pendurada para a frente se recolhem do mesmo jeito. Por onde ele anda
    /// não muda (molde do Undertow: <see cref="UndertowBody.Fit"/>).
    /// </summary>
    public sealed class JanitorBody : MonoBehaviour
    {
        public Material skinMaterial;
        [Tooltip("Macacão de serviço.")]
        public Material coverallMaterial;
        [Tooltip("Barra encharcada do macacão e o esfregão.")]
        public Material wetMaterial;
        [Tooltip("Botas de borracha e o gorro.")]
        public Material rubberMaterial;
        public Material capMaterial;
        [Tooltip("Chaves e argola.")]
        public Material metalMaterial;
        [Tooltip("Cabo do esfregão.")]
        public Material handleMaterial;
        [Tooltip("O fundo da boca.")]
        public Material mawMaterial;
        public Material toothMaterial;

        [Tooltip("Metros andados por ciclo completo de passada (dois passos).")]
        public float strideLength = 1.6f;
        [Tooltip("Segundos para subir do chão ao aparecer.")]
        public float riseSeconds = 1.1f;
        [Tooltip("Segundos para afundar ao sumir.")]
        public float sinkSeconds = 0.7f;

        [Header("Modelo do Blender")]
        [Tooltip("Malha esculpida no Blender (Tools/Blender/personagens/zelador.py), vestida sobre as juntas. Vazio: as peças de código.")]
        public TextAsset model;
        public Material[] modelMaterials;

        /// <summary>Só para a ferramenta que exporta o esqueleto: monta as juntas na pose de encaixe e não tira peça nenhuma.</summary>
        public static bool KeepAllJoints;

        /// <summary>A cada pé no chão, com a velocidade do momento.</summary>
        public event Action<float> Stepped;

        /// <summary>De 0 (escondido, debaixo do chão) a 1 (de pé).</summary>
        public float Shown => _shown;

        const float HipHeight = 1.12f;
        const float ThighLength = 0.55f;
        const float ShinLength = 0.51f;
        const float UpperArmLength = 0.5f;
        const float ForearmLength = 0.48f;
        const float SpineLength = 0.58f;
        const float NeckLength = 0.2f;
        const float Height = 2.3f;
        // O cabo do esfregão sai da junta para cima (no local da junta) e a cabeça de tiras fica além da ponta.
        const float MopLength = 1.3f;
        const float MopHeadAt = 1.38f;
        const float MopHeadHalfWidth = 0.15f;

        // Encaixe: a folga que antecipa o recolhimento, os passos tentados e as velocidades.
        const float FitMargin = 0.18f;
        const float FitCheckEvery = 0.1f;
        const int FitSteps = 8;
        const float FoldRate = 3.5f;
        const float RelaxRate = 0.7f;
        const int SpinePart = 0, ArmPart = 1, MopPart = 2;
        static readonly float[] FitMax = { 1f, 1f, 2f };
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        readonly HashSet<Transform> _live = new HashSet<Transform>();
        readonly Transform[] _thigh = new Transform[2];
        readonly Transform[] _shin = new Transform[2];
        readonly Transform[] _foot = new Transform[2];
        readonly Transform[] _upperArm = new Transform[2];
        readonly Transform[] _forearm = new Transform[2];
        readonly Transform[] _hand = new Transform[2];
        Transform _rig, _pelvis, _spine, _shoulders, _neck, _head, _jaw, _mop, _keys;

        Renderer[] _renderers;
        Collider[] _colliders;
        JanitorBrain _brain;
        Vector3 _lastPosition;
        float _speed, _phase, _shown = 1f, _flinch;
        bool _built, _visible = true;

        // Encaixe na fase: 0 = a pose animada. _upright endireita o tronco (a cabeça vem para trás),
        // _armFold baixa o braço esquerdo, _mopFold: 0 a 1 o esfregão vai para reto atrás, 1 a 2 levanta.
        float _upright, _armFold, _mopFold;
        readonly float[] _fitWant = new float[3];
        float _fitCheckAt;
        Quaternion _mopAnimated = Quaternion.identity;
        float _poseSpeed, _poseStill, _poseFlinch, _poseTime, _posePhase;
        readonly Vector3[] _chain = new Vector3[5];

        /// <summary>De 0 (a pose: atrás e de lado) a 1 (reto atrás) e 2 (levantado): quanto o esfregão está recolhido agora.</summary>
        public float MopFold => _mopFold;

        void Awake()
        {
            Build();
            _brain = GetComponent<JanitorBrain>();
            _lastPosition = transform.position;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponents<Collider>();
            if (_brain != null) SetShown(0f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Aparecer e sumir: sobe do chão e afunda nele.
            bool present = _brain == null || (_brain.IsSpawned && _brain.Present);
            float target = present ? 1f : 0f;
            if (!Mathf.Approximately(_shown, target))
            {
                float rate = present ? 1f / Mathf.Max(0.05f, riseSeconds) : 1f / Mathf.Max(0.05f, sinkSeconds);
                SetShown(Mathf.MoveTowards(_shown, target, rate * dt));
                // Ao aparecer noutro lugar não conta a distância pulada como passo.
                if (present && _shown < 0.05f) _lastPosition = transform.position;
            }
            if (_shown <= 0f) { _lastPosition = transform.position; return; }

            Vector3 moved = transform.position - _lastPosition;
            _lastPosition = transform.position;
            moved.y = 0f;
            float speed = moved.magnitude / dt;
            if (speed > 12f) speed = 0f;
            _speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-8f * dt));

            float before = _phase;
            _phase += _speed / Mathf.Max(0.5f, strideLength) * dt;
            if (_speed > 0.3f && Mathf.FloorToInt(_phase * 2f) != Mathf.FloorToInt(before * 2f)) Stepped?.Invoke(_speed);
            if (_phase > 1000f) _phase -= 1000f;

            bool flinching = _brain != null && _brain.IsSpawned && _brain.Flinching;
            _flinch = Mathf.MoveTowards(_flinch, flinching ? 1f : 0f, dt * (flinching ? 8f : 2.5f));

            float still = Mathf.Clamp01(1f - _speed / 0.3f);
            Pose(_phase, _speed, still, _flinch, Time.time);
            ApplyRise();

            // De pé, confere contra a fase (subindo ou afundando fica como está: metade do corpo está no chão).
            GameSession session = GameSession.Instance;
            if (_shown >= 0.99f && session != null && session.level != null) Fit(session.level, dt);
        }

        void SetShown(float shown)
        {
            _shown = Mathf.Clamp01(shown);
            bool visible = _shown > 0f;
            if (visible != _visible)
            {
                _visible = visible;
                if (_renderers != null) foreach (Renderer r in _renderers) if (r != null) r.enabled = visible;
            }
            if (_colliders != null) foreach (Collider c in _colliders) if (c != null) c.enabled = _shown > 0.6f;
            ApplyRise();
        }

        // Debaixo do chão na proporção do que falta aparecer (a pose não mexe na altura do rig fora disto).
        void ApplyRise()
        {
            if (_rig == null) return;
            Vector3 p = _rig.localPosition;
            float eased = _shown * _shown * (3f - 2f * _shown);
            p.y = _bob - (1f - eased) * Height;
            _rig.localPosition = p;
        }

        float _bob;

        // ---------------------------------------------------------------- Pose

        /// <summary>
        /// Coloca o corpo numa pose. <paramref name="phase"/> é a passada (1 = ciclo completo),
        /// <paramref name="still"/> de 0 (andando) a 1 (parado: a pose de encaixe, mais o balanço),
        /// <paramref name="flinch"/> de 0 a 1 (o braço esquerdo sobe na frente do rosto: a lanterna).
        /// </summary>
        public void Pose(float phase, float speed, float still, float flinch, float time)
        {
            if (!_built) Build();
            _posePhase = phase;
            _poseSpeed = speed;
            _poseStill = still;
            _poseFlinch = flinch;
            _poseTime = time;

            float walk = Mathf.Clamp01(speed / 1.6f);
            float rush = Mathf.Clamp01((speed - 1.6f) / 2.6f);
            float cycle = phase * Mathf.PI * 2f;
            float swing = Mathf.Sin(cycle);
            float lift = Mathf.Cos(cycle);
            float breathe = Mathf.Sin(time * 0.7f);
            float sway = Mathf.Sin(time * 0.45f) * (1f - walk);

            _bob = -0.025f * Mathf.Abs(lift) * walk;
            _rig.localRotation = Quaternion.Euler(0f, 0f, swing * 3f * walk + sway * 1.5f);

            // Curvado sempre; caçando, quase de quatro.
            float lean = 16f + 6f * walk + 22f * rush + breathe * 0.8f;
            // Encaixe: rente a uma parede, endireita (a cabeça pendurada não entra nela).
            lean = Mathf.Lerp(lean, 4f, _upright);
            _spine.localRotation = Quaternion.Euler(lean, swing * 6f * walk, 0f);
            _shoulders.localRotation = Quaternion.Euler(8f, 0f, -8f + sway * 2f);

            // Pescoço pendurado para a frente, abaixo dos ombros, e a cabeça erguida de volta, torta,
            // com o rosto para quem está na frente dele; correndo, o pescoço estica adiante.
            _neck.localRotation = Quaternion.Euler(Mathf.Lerp(42f - lean * 0.4f - 22f * rush, 12f, _upright), 0f, 0f);
            _head.localRotation = Quaternion.Euler(Mathf.Lerp(-32f + 8f * rush + sway * 4f, -8f, _upright), 6f * sway, 22f - 12f * rush + Mathf.Sin(time * 0.31f) * 4f * still);
            _jaw.localRotation = Quaternion.Euler(10f + 14f * rush + 4f * breathe, 0f, 0f);

            // Pernas: arrasta os pés devagar; correndo, passadas compridas e joelho alto.
            float legSwing = Mathf.Lerp(0f, 20f, walk) + 18f * rush;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                float forward = swing * side;
                float knee = Mathf.Max(0f, lift * side) * walk;
                _thigh[i].localRotation = Quaternion.Euler(-(8f + forward * legSwing + knee * (10f + 25f * rush)), 0f, side * 3f);
                _shin[i].localRotation = Quaternion.Euler(12f + knee * (25f + 35f * rush), 0f, 0f);
                _foot[i].localRotation = Quaternion.Euler(-(4f + knee * 15f), 0f, 0f);
            }

            // Braço esquerdo: pendurado, comprido, balançando solto; caçando, estica para a frente.
            // Na hesitação (lanterna), sobe na frente do rosto.
            {
                float shoulder = 6f - swing * 12f * walk + 55f * rush + 4f * breathe;
                shoulder = Mathf.Lerp(shoulder, 105f, flinch);
                float elbow = Mathf.Lerp(10f + 20f * rush, 115f, flinch);
                // Encaixe: onde a mão esticada bateria, o braço desce junto do corpo.
                shoulder = Mathf.Lerp(shoulder, 4f, _armFold);
                elbow = Mathf.Lerp(elbow, 12f, _armFold);
                _upperArm[1].localRotation = Quaternion.Euler(-shoulder, 0f, -(6f + 4f * sway) - 10f * flinch);
                _forearm[1].localRotation = Quaternion.Euler(-elbow, 0f, 20f * flinch);
                _hand[1].localRotation = Quaternion.Euler(-10f - 25f * rush, 0f, 0f);
            }

            // Braço direito: segura o cabo do esfregão, que arrasta atrás e de lado.
            {
                float shoulder = -8f + swing * 6f * walk + 10f * rush;
                _upperArm[0].localRotation = Quaternion.Euler(-shoulder, 0f, 10f);
                _forearm[0].localRotation = Quaternion.Euler(-(18f + 10f * rush), 0f, 0f);
                _hand[0].localRotation = Quaternion.Euler(0f, 0f, 0f);
                _mop.localRotation = Quaternion.Euler(-122f + 6f * swing * walk, 0f, 20f);
            }

            // As chaves balançam com o passo.
            _keys.localRotation = Quaternion.Euler(swing * 18f * walk + Mathf.Sin(time * 2.1f) * 3f, 0f, lift * 10f * walk);
        }

        // ---------------------------------------------------------------- Encaixe na fase

        /// <summary>
        /// Encaixa a pose atual na fase: a cabeça pendurada, o braço esquerdo e o esfregão que bateriam
        /// num colisor fixo da fase (parede, porta, móvel; jogador e inimigos não contam) se recolhem.
        /// No jogo (<paramref name="dt"/> > 0) começam a recolher com folga, antes de encostar, sem
        /// pressa, e soltam devagar; o que ainda bateria sobe na hora. Sem dt (as fotos), direto ao que cabe.
        /// </summary>
        public void Fit(LevelBuilder level, float dt = 0f)
        {
            if (level == null || _rig == null) return;
            Transform root = level.transform;
            bool check = dt <= 0f || Time.time >= _fitCheckAt;
            if (check) _fitCheckAt = Time.time + FitCheckEvery;

            _upright = FitPart(SpinePart, _upright, dt, check, root);
            _armFold = FitPart(ArmPart, _armFold, dt, check, root);
            _mopFold = FitPart(MopPart, _mopFold, dt, check, root);
        }

        /// <summary>Para as fotos: volta à pose animada, sem nada recolhido (o "antes").</summary>
        public void Unfit()
        {
            _upright = _armFold = _mopFold = 0f;
            for (int i = 0; i < _fitWant.Length; i++) _fitWant[i] = 0f;
            if (_built) Repose();
        }

        /// <summary>
        /// Para as fotos e os testes: as partes (cabeça, braço esquerdo, esfregão) que entram agora em
        /// colisor fixo da fase. Vazio se nada entra.
        /// </summary>
        public void Penetrations(LevelBuilder level, List<string> where)
        {
            where.Clear();
            if (level == null || _rig == null) return;
            Transform root = level.transform;
            if (PartBlocked(SpinePart, root, 0f)) where.Add("cabeça");
            if (PartBlocked(ArmPart, root, 0f)) where.Add("braço esquerdo");
            if (PartBlocked(MopPart, root, 0f)) where.Add("esfregão");
        }

        // O menor recolhimento que cabe (com folga, a cada FitCheckEvery), alcançado sem pressa; o que
        // ainda entraria num colisor sobe na hora, um passo de cada vez.
        float FitPart(int part, float current, float dt, bool check, Transform root)
        {
            float max = FitMax[part];
            if (check)
            {
                float want = max;
                for (int k = 0; k <= FitSteps; k++)
                {
                    float t = max * k / FitSteps;
                    SetFold(part, t);
                    if (PartBlocked(part, root, dt > 0f ? FitMargin : 0f)) continue;
                    want = t;
                    break;
                }
                _fitWant[part] = want;
            }

            float target = _fitWant[part];
            float next = dt > 0f ? Mathf.MoveTowards(current, target, (target > current ? FoldRate : RelaxRate) * dt) : target;
            SetFold(part, next);
            while (next < max && PartBlocked(part, root, 0f))
            {
                next = Mathf.Min(max, next + max / FitSteps);
                SetFold(part, next);
            }
            return next;
        }

        void SetFold(int part, float t)
        {
            switch (part)
            {
                case SpinePart: _upright = t; Repose(); break;
                case ArmPart: _armFold = t; Repose(); break;
                default: SetMop(t); break;
            }
        }

        // Refaz a pose com os mesmos valores (e o novo recolhimento) e guarda o esfregão animado.
        void Repose()
        {
            Pose(_posePhase, _poseSpeed, _poseStill, _poseFlinch, _poseTime);
            _mopAnimated = _mop.rotation;
        }

        // O esfregão: 0 = como a pose o deixa (atrás e de lado), 1 = reto atrás, 2 = em pé ao lado do ombro.
        void SetMop(float t)
        {
            Quaternion animated = _mopAnimated;
            if (t <= 0f)
            {
                _mop.rotation = animated;
                return;
            }
            Vector3 from = animated * Vector3.up;
            Vector3 behind = MopBehind();
            Vector3 to = t <= 1f ? Vector3.Slerp(from, behind, t) : Vector3.Slerp(behind, MopRaised(), t - 1f);
            _mop.rotation = Quaternion.FromToRotation(from, to) * animated;
        }

        // O cabo reto para trás, a cabeça de tiras no chão no meio do rastro dele (por onde veio).
        Vector3 MopBehind()
        {
            Vector3 forward = Flat(transform.forward).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 grip = _mop.position;
            float lateral = Vector3.Dot(grip - transform.position, right);
            float drop = Mathf.Max(0.05f, grip.y - transform.position.y - 0.05f);
            float reach = Mathf.Sqrt(Mathf.Max(0f, MopHeadAt * MopHeadAt - drop * drop));
            float back = Mathf.Sqrt(Mathf.Max(0.01f, reach * reach - lateral * lateral));
            return (-forward * back - right * lateral + Vector3.down * drop).normalized;
        }

        // O cabo em pé, um pouco para trás e para dentro: a cabeça de tiras acima do ombro direito.
        Vector3 MopRaised()
        {
            Vector3 forward = Flat(transform.forward).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return (Vector3.up * 0.97f - forward * 0.12f - right * 0.15f).normalized;
        }

        // A parte bateria em colisor da fase? A folga estica a ponta (começa a recolher antes de encostar).
        bool PartBlocked(int part, Transform root, float margin)
        {
            if (part == MopPart) return MopBlocked(root, margin);
            if (part == SpinePart)
            {
                _chain[0] = _pelvis.position;
                _chain[1] = _shoulders.position;
                _chain[2] = _head.position;
                _chain[3] = _head.TransformPoint(0f, 0.1f, 0.15f);
            }
            else
            {
                _chain[0] = _upperArm[1].position;
                _chain[1] = _forearm[1].position;
                _chain[2] = _hand[1].position;
                _chain[3] = _hand[1].TransformPoint(0f, -0.22f, 0f);
            }
            if (margin > 0f)
            {
                Vector3 d = _chain[3] - _chain[2];
                if (d.sqrMagnitude > 1e-6f) _chain[3] += d.normalized * margin;
            }
            for (int k = 0; k < 3; k++)
                if (SegmentHits(root, _chain[k], _chain[k + 1])) return true;
            return false;
        }

        // O cabo (da mão à cabeça de tiras) e a cabeça: a ponta e os dois lados. Arrastada no chão, a
        // cabeça é conferida um palmo acima dele (o raio não pode achar o piso).
        bool MopBlocked(Transform root, float margin)
        {
            Vector3 grip = _mop.position;
            Vector3 along = _mop.up;
            Vector3 head = grip + along * MopHeadAt;
            float floor = transform.position.y + 0.2f;
            if (head.y < floor) head.y = floor;
            Vector3 flat = Flat(along);
            bool lying = flat.sqrMagnitude > 0.01f;
            Vector3 outward = lying ? flat.normalized : along;
            Vector3 side = lying ? Vector3.Cross(Vector3.up, outward) : transform.right;
            float wide = MopHeadHalfWidth + margin;
            return SegmentHits(root, grip, head) ||
                   SegmentHits(root, head, head + outward * (0.14f + margin)) ||
                   SegmentHits(root, head, head + side * wide) ||
                   SegmentHits(root, head, head - side * wide);
        }

        // O trecho encosta em colisor da fase? Nos dois sentidos: o raio não acha a face de quem começa dentro.
        static bool SegmentHits(Transform root, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float length = d.magnitude;
            if (length <= 0.001f) return false;
            Vector3 dir = d / length;
            int count = Physics.RaycastNonAlloc(a, dir, Hits, length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Hits[i].collider.transform.IsChildOf(root)) return true;
            count = Physics.RaycastNonAlloc(b, -dir, Hits, length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Hits[i].collider.transform.IsChildOf(root)) return true;
            return false;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // ---------------------------------------------------------------- Montagem

        Transform Live(Transform joint)
        {
            _live.Add(joint);
            return joint;
        }

        static Material Or(Material material, Material fallback) => material != null ? material : fallback;

        /// <summary>Monta as juntas e as peças. Só a primeira chamada constrói.</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;

            Material skin = skinMaterial;
            Material cloth = Or(coverallMaterial, skin);
            Material wet = Or(wetMaterial, cloth);
            Material rubber = Or(rubberMaterial, cloth);
            Material cap = Or(capMaterial, rubber);
            Material metal = Or(metalMaterial, rubber);
            Material handle = Or(handleMaterial, metal);
            Material maw = Or(mawMaterial, rubber);
            Material tooth = Or(toothMaterial, skin);

            _rig = Live(FigureKit.Joint(transform, "Zelador", Vector3.zero));
            _pelvis = Live(FigureKit.Joint(_rig, "Bacia", new Vector3(0f, HipHeight, 0f)));
            FigureKit.Ball(_pelvis, "Macacao_Bacia", new Vector3(0f, 0.02f, 0f), new Vector3(0.36f, 0.26f, 0.24f), cloth);
            FigureKit.Ball(_pelvis, "Cinto", new Vector3(0f, 0.1f, 0f), new Vector3(0.38f, 0.06f, 0.26f), rubber);

            // Molho de chaves preso à esquerda do cinto.
            _keys = Live(FigureKit.Joint(_pelvis, "Chaves", new Vector3(-0.19f, 0.08f, 0.04f)));
            FigureKit.Ball(_keys, "Argola", new Vector3(0f, -0.06f, 0f), new Vector3(0.07f, 0.07f, 0.015f), metal);
            for (int k = 0; k < 7; k++)
            {
                float a = -60f + k * 20f;
                Transform key = FigureKit.Joint(_keys, "Chave_" + k, new Vector3(0f, -0.06f, 0f), new Vector3(0f, 90f, a));
                FigureKit.Ball(key, "Chave_Peca_" + k, new Vector3(0f, -0.06f, 0f), new Vector3(0.012f, 0.075f, 0.004f), metal);
            }

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "D" : "E";
                _thigh[i] = Live(FigureKit.Joint(_pelvis, "Coxa_" + tag, new Vector3(side * 0.1f, -0.02f, 0f)));
                _shin[i] = Live(FigureKit.Limb(_thigh[i], "Canela_" + tag, ThighLength, 0.075f, true, cloth));
                // A barra do macacão, encharcada, por cima da bota.
                FigureKit.Ball(_shin[i], "Barra_" + tag, new Vector3(0f, -0.3f, 0f), new Vector3(0.17f, 0.24f, 0.17f), wet);
                _foot[i] = Live(FigureKit.Limb(_shin[i], "Pe_" + tag, ShinLength, 0.07f, true, rubber));
                FigureKit.Ball(_foot[i], "Bota_" + tag, new Vector3(0f, -0.02f, 0.07f), new Vector3(0.13f, 0.1f, 0.3f), rubber);
            }

            _spine = Live(FigureKit.Joint(_pelvis, "Coluna", new Vector3(0f, 0.08f, 0f)));
            FigureKit.Ball(_spine, "Macacao_Barriga", new Vector3(0f, 0.18f, 0f), new Vector3(0.3f, 0.36f, 0.2f), cloth);
            FigureKit.Ball(_spine, "Macacao_Peito", new Vector3(0f, 0.42f, -0.01f), new Vector3(0.4f, 0.3f, 0.22f), cloth);
            _shoulders = Live(FigureKit.Joint(_spine, "Ombros", new Vector3(0f, SpineLength, 0f)));

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "D" : "E";
                _upperArm[i] = Live(FigureKit.Joint(_shoulders, "Braco_" + tag, new Vector3(side * 0.21f, -0.03f, 0f)));
                _forearm[i] = Live(FigureKit.Limb(_upperArm[i], "Antebraco_" + tag, UpperArmLength, 0.055f, true, cloth));
                _hand[i] = Live(FigureKit.Limb(_forearm[i], "Mao_" + tag, ForearmLength, 0.045f, true, skin));
                // Mão comprida, dedos finos juntos.
                FigureKit.Ball(_hand[i], "Mao_Peca_" + tag, new Vector3(0f, -0.1f, 0.01f), new Vector3(0.07f, 0.22f, 0.04f), skin);
            }

            // O esfregão: cabo comprido saindo da mão direita, a cabeça de tiras molhadas no chão.
            _mop = Live(FigureKit.Joint(_hand[0], "Esfregao", new Vector3(0f, -0.08f, 0.02f)));
            Transform mopEnd = FigureKit.Limb(_mop, "Esfregao_Ponta", 1.3f, 0.016f, false, handle);
            FigureKit.Ball(mopEnd, "Esfregao_Tiras", new Vector3(0f, 0.08f, 0f), new Vector3(0.3f, 0.2f, 0.26f), wet);

            _neck = Live(FigureKit.Joint(_shoulders, "Pescoco", new Vector3(0f, 0.04f, 0.02f)));
            Transform top = FigureKit.Limb(_neck, "Pescoco_Ponta", NeckLength, 0.045f, false, skin);
            _head = Live(FigureKit.Joint(top, "Cabeca", Vector3.zero));
            FigureKit.Ball(_head, "Cranio", new Vector3(0f, 0.12f, 0.02f), new Vector3(0.2f, 0.27f, 0.23f), skin);
            // Gorro puxado até o nariz: tapa os olhos.
            FigureKit.Ball(_head, "Gorro", new Vector3(0f, 0.17f, 0.0f), new Vector3(0.225f, 0.22f, 0.245f), cap);
            _jaw = Live(FigureKit.Joint(_head, "Mandibula", new Vector3(0f, 0.04f, 0.04f)));
            FigureKit.Ball(_jaw, "Queixo", new Vector3(0f, -0.04f, 0.04f), new Vector3(0.15f, 0.08f, 0.13f), skin);
            FigureKit.Ball(_head, "Boca", new Vector3(0f, 0.03f, 0.115f), new Vector3(0.13f, 0.035f, 0.02f), maw);
            FigureKit.Ball(_head, "Dentes", new Vector3(0f, 0.045f, 0.118f), new Vector3(0.11f, 0.012f, 0.015f), tooth);

            bool dressed = model != null && !KeepAllJoints;
            if (!KeepAllJoints)
                foreach (Transform joint in new List<Transform>(_live))
                    if (dressed) FigureKit.Strip(joint, _live); else FigureKit.Bake(joint, _live);

            // A pose parada é a de encaixe: foi nela que o Blender esculpiu o modelo.
            Pose(0f, 0f, 1f, 0f, 0f);
            ApplyRise();
            if (dressed) CharacterModel.Attach(transform, model, modelMaterials);
        }
    }
}
