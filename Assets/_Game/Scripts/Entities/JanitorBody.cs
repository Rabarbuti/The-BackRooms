using System;
using System.Collections.Generic;
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
            _spine.localRotation = Quaternion.Euler(lean, swing * 6f * walk, 0f);
            _shoulders.localRotation = Quaternion.Euler(8f, 0f, -8f + sway * 2f);

            // Pescoço pendurado para a frente, abaixo dos ombros, e a cabeça erguida de volta, torta,
            // com o rosto para quem está na frente dele; correndo, o pescoço estica adiante.
            _neck.localRotation = Quaternion.Euler(42f - lean * 0.4f - 22f * rush, 0f, 0f);
            _head.localRotation = Quaternion.Euler(-32f + 8f * rush + sway * 4f, 6f * sway, 22f - 12f * rush + Mathf.Sin(time * 0.31f) * 4f * still);
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
