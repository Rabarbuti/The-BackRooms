using System;
using System.Collections.Generic;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O corpo do Ascensorista, o inimigo do elevador do Level 3: um homem alto e magro demais
    /// (2,05 m, curvado para caber na porta do elevador), de uniforme antigo de ascensorista — paletó
    /// cor de vinho de duas fileiras de botões de latão, quepe redondo, luvas brancas encardidas — que
    /// "vem de outro andar". A cabeça é enrolada em gaze suja na altura dos olhos (é cego, e isso se lê
    /// de longe), a boca larga demais fica entreaberta e ele tateia o chão com uma bengala branca
    /// comprida de ponteira vermelha, na mão direita. Parado, inclina a cabeça de lado para escutar.
    ///
    /// Juntas montadas em código (<see cref="FigureKit"/>), a animação gira as juntas e a aparência
    /// é o modelo esculpido no Blender (<see cref="model"/>, Tools/Blender/personagens/ascensorista.py);
    /// sem modelo, um corpo provisório de peças. A pose parada (<see cref="Pose"/> com still = 1) é a
    /// de encaixe do modelo.
    ///
    /// Fora do andar (<see cref="AttendantPhase.Away"/> e <see cref="AttendantPhase.Coming"/>) o corpo e
    /// o colisor somem: ele está "noutro andar".
    /// </summary>
    public sealed class AttendantBody : MonoBehaviour
    {
        [Tooltip("Paletó e calça cor de vinho.")]
        public Material uniformMaterial;
        [Tooltip("Pele cinzenta do rosto e do pescoço.")]
        public Material skinMaterial;
        [Tooltip("A gaze em volta dos olhos e as luvas.")]
        public Material gauzeMaterial;
        [Tooltip("Botões, divisa do quepe.")]
        public Material brassMaterial;
        [Tooltip("Quepe e sapatos.")]
        public Material capMaterial;
        [Tooltip("A bengala branca.")]
        public Material caneMaterial;
        [Tooltip("A ponteira vermelha e a boca.")]
        public Material redMaterial;

        [Tooltip("Metros andados por ciclo completo de passada (dois passos).")]
        public float strideLength = 1.5f;

        [Header("Modelo do Blender")]
        [Tooltip("Malha esculpida no Blender, vestida sobre as juntas. Vazio: o corpo provisório de peças.")]
        public TextAsset model;
        public Material[] modelMaterials;

        /// <summary>Só para exportar o esqueleto: monta as juntas na pose de encaixe e não funde nada.</summary>
        public static bool KeepAllJoints;

        /// <summary>A cada passo, com a velocidade do momento (a bengala bate no chão).</summary>
        public event Action<float> Stepped;

        /// <summary>O corpo está à vista (ele está no andar).</summary>
        public bool Shown { get; private set; } = true;

        /// <summary>Onde a ponta da bengala está agora (para os testes e o som).</summary>
        public Vector3 CaneTip => _caneTip != null ? _caneTip.position : transform.position;

        const float HipHeight = 0.98f;
        const float ThighLength = 0.47f;
        const float ShinLength = 0.47f;
        const float TorsoHeight = 0.58f;
        const float NeckLength = 0.13f;
        const float UpperArmLength = 0.43f;
        const float ForearmLength = 0.45f;
        const float CaneLength = 1.05f;

        readonly HashSet<Transform> _live = new HashSet<Transform>();
        readonly Transform[] _thigh = new Transform[2], _shin = new Transform[2], _foot = new Transform[2];
        readonly Transform[] _upperArm = new Transform[2], _forearm = new Transform[2], _hand = new Transform[2];
        Transform _rig, _pelvis, _spine, _neck, _head, _cane, _caneTip;
        bool _built;

        AttendantBrain _brain;
        Collider _collider;
        Renderer[] _renderers;
        Vector3 _lastPosition;
        float _phase, _speed, _listen;

        void Awake()
        {
            Build();
            _brain = GetComponent<AttendantBrain>();
            _collider = GetComponent<Collider>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _lastPosition = transform.position;
            // No jogo ele nasce escondido (num poço); o primeiro quadro não o mostra no lugar em que foi posto.
            if (_brain != null) SetShown(false);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            AttendantPhase phase = _brain == null ? AttendantPhase.Out : _brain.IsSpawned ? _brain.Phase : AttendantPhase.Away;
            SetShown(phase != AttendantPhase.Away && phase != AttendantPhase.Coming);

            Vector3 moved = transform.position - _lastPosition;
            _lastPosition = transform.position;
            moved.y = 0f;
            float speed = moved.magnitude / dt;
            if (speed > 12f) speed = 0f;
            _speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-8f * dt));

            float before = _phase;
            _phase += _speed / Mathf.Max(0.5f, strideLength) * dt;
            if (Shown && _speed > 0.25f && Mathf.FloorToInt(_phase * 2f) != Mathf.FloorToInt(before * 2f))
                Stepped?.Invoke(_speed);
            if (_phase > 1000f) _phase -= 1000f;

            bool listening = _brain != null && _brain.IsSpawned && _brain.Listening;
            _listen = Mathf.MoveTowards(_listen, listening ? 1f : 0f, dt * 2.5f);

            float still = Mathf.Clamp01(1f - _speed / 0.3f);
            Pose(_phase, _speed, still, _listen, Time.time);
        }

        void SetShown(bool shown)
        {
            if (shown == Shown) return;
            Shown = shown;
            if (_collider != null) _collider.enabled = shown;
            if (_renderers == null) return;
            foreach (Renderer r in _renderers)
                if (r != null) r.enabled = shown;
        }

        // ---------------------------------------------------------------- Pose

        /// <summary>
        /// Coloca o corpo numa pose. <paramref name="phase"/> é a passada (1 = ciclo completo),
        /// <paramref name="still"/> de 0 (andando) a 1 (parado: a pose de encaixe),
        /// <paramref name="listen"/> de 0 a 1 (cabeça inclinada de lado, escutando). Público para a
        /// ferramenta de prévia, que fotografa fora do Play.
        /// </summary>
        public void Pose(float phase, float speed, float still, float listen, float time)
        {
            if (!_built) Build();

            float walk = Mathf.Clamp01(speed / 1.2f);
            float rush = Mathf.Clamp01((speed - 1.6f) / 2.2f);
            float cycle = phase * Mathf.PI * 2f;
            float swing = Mathf.Sin(cycle);
            float lift = Mathf.Cos(cycle);
            float breathe = Mathf.Sin(time * 1.3f);

            _rig.localPosition = new Vector3(0f, -0.03f * Mathf.Abs(swing) * walk - 0.06f * rush, 0f);

            // Curvado sempre (não cabe em pé na porta do elevador); correndo, quase de quatro.
            float lean = 24f + 2f * walk + 18f * rush + breathe * 0.8f;
            _spine.localRotation = Quaternion.Euler(lean, swing * 4f * walk, 0f);
            // O pescoço sai para a frente (a cabeça vai à frente do corpo, como quem fareja).
            _neck.localRotation = Quaternion.Euler(-lean * 0.35f + 4f * still, 0f, 0f);
            // Queixo um pouco erguido, de quem não olha. Escutando: a cabeça deita de lado, o ouvido para a frente.
            _head.localRotation = Quaternion.Euler(-12f - 6f * rush + 6f * listen, 18f * listen, -28f * listen + 3f * still);

            float legSwing = Mathf.Lerp(0f, 22f, walk) + 14f * rush;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                float forward = swing * side;
                float knee = Mathf.Max(0f, lift * side) * walk;
                _thigh[i].localRotation = Quaternion.Euler(-(8f + forward * legSwing + knee * 16f + 10f * rush), 0f, side * 2f);
                _shin[i].localRotation = Quaternion.Euler(10f + knee * 38f + 12f * rush, 0f, 0f);
                _foot[i].localRotation = Quaternion.Euler(-(2f + knee * 18f), 0f, 0f);
            }

            // Braço direito (0) com a bengala à frente, varrendo o chão de um lado para o outro;
            // o esquerdo, comprido, pende e tateia o ar. Correndo, os dois vão para a frente.
            float sweep = Mathf.Sin(cycle * 0.5f) * (1f - still) * (1f - rush);
            float drift = Mathf.Sin(time * 0.7f) * 2f;
            _upperArm[0].localRotation = Quaternion.Euler(-(28f + 30f * rush), -6f + 14f * sweep, 10f + 6f * rush);
            _forearm[0].localRotation = Quaternion.Euler(-(22f + 20f * rush), 0f, 0f);
            _hand[0].localRotation = Quaternion.Euler(30f - 20f * rush, 0f, -6f + 10f * sweep);
            _upperArm[1].localRotation = Quaternion.Euler(-(10f + 10f * still + 50f * rush - swing * 10f * walk + drift), 6f * still, -(12f + 4f * listen));
            _forearm[1].localRotation = Quaternion.Euler(-(18f + 10f * still + 30f * rush), -10f * still, 0f);
            _hand[1].localRotation = Quaternion.Euler(-10f * still, -20f * still, -6f);
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

            Material uniform = uniformMaterial;
            Material skin = Or(skinMaterial, uniform);
            Material gauze = Or(gauzeMaterial, skin);
            Material brass = Or(brassMaterial, uniform);
            Material cap = Or(capMaterial, uniform);
            Material cane = Or(caneMaterial, gauze);
            Material red = Or(redMaterial, skin);

            _rig = Live(FigureKit.Joint(transform, "Ascensorista", Vector3.zero));
            _pelvis = Live(FigureKit.Joint(_rig, "Bacia", new Vector3(0f, HipHeight, 0f)));
            FigureKit.Ball(_pelvis, "Calca_Bacia", new Vector3(0f, 0.02f, 0f), new Vector3(0.34f, 0.24f, 0.24f), uniform);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "D" : "E";
                _thigh[i] = Live(FigureKit.Joint(_pelvis, "Coxa_" + tag, new Vector3(side * 0.1f, -0.02f, 0f)));
                _shin[i] = Live(FigureKit.Limb(_thigh[i], "Joelho_" + tag, ThighLength, 0.07f, true, uniform));
                _foot[i] = Live(FigureKit.Limb(_shin[i], "Pe_" + tag, ShinLength, 0.055f, true, uniform));
                FigureKit.Ball(_foot[i], "Sapato_" + tag, new Vector3(0f, -0.02f, 0.06f), new Vector3(0.1f, 0.08f, 0.27f), cap);
            }

            _spine = Live(FigureKit.Joint(_pelvis, "Coluna", new Vector3(0f, 0.1f, 0f)));
            FigureKit.Ball(_spine, "Paleto", new Vector3(0f, 0.27f, 0f), new Vector3(0.36f, 0.62f, 0.24f), uniform);
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 2; c++)
                    FigureKit.Ball(_spine, "Botao_" + r + "_" + c, new Vector3(c == 0 ? -0.055f : 0.055f, 0.12f + r * 0.1f, 0.118f), new Vector3(0.025f, 0.025f, 0.012f), brass);

            Transform shoulders = Live(FigureKit.Joint(_spine, "Ombros", new Vector3(0f, TorsoHeight - 0.06f, 0f)));
            FigureKit.Ball(shoulders, "Ombreiras", new Vector3(0f, 0f, 0f), new Vector3(0.44f, 0.12f, 0.2f), uniform);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "D" : "E";
                _upperArm[i] = Live(FigureKit.Joint(shoulders, "Braco_" + tag, new Vector3(side * 0.21f, -0.02f, 0f)));
                _forearm[i] = Live(FigureKit.Limb(_upperArm[i], "Antebraco_" + tag, UpperArmLength, 0.055f, true, uniform));
                _hand[i] = Live(FigureKit.Limb(_forearm[i], "Mao_" + tag, ForearmLength, 0.045f, true, uniform));
                FigureKit.Ball(_hand[i], "Luva_" + tag, new Vector3(0f, -0.08f, 0f), new Vector3(0.08f, 0.18f, 0.045f), gauze);
            }

            // A bengala branca na mão direita, apontando para o chão à frente.
            _cane = Live(FigureKit.Joint(_hand[0], "Bengala", new Vector3(0f, -0.07f, 0.01f), new Vector3(-20f, 0f, 0f)));
            Transform tip = FigureKit.Limb(_cane, "Bengala_Ponta", CaneLength, 0.012f, true, cane);
            FigureKit.Ball(tip, "Ponteira", new Vector3(0f, 0.05f, 0f), new Vector3(0.028f, 0.11f, 0.028f), red);
            _caneTip = Live(tip);

            _neck = Live(FigureKit.Joint(shoulders, "Pescoco", new Vector3(0f, 0.04f, 0.02f)));
            Transform top = FigureKit.Limb(_neck, "Pescoco_Ponta", NeckLength, 0.045f, false, skin);
            _head = Live(FigureKit.Joint(top, "Cabeca", new Vector3(0f, -0.01f, 0f)));
            FigureKit.Ball(_head, "Cranio", new Vector3(0f, 0.13f, 0.01f), new Vector3(0.19f, 0.25f, 0.22f), skin);
            FigureKit.Ball(_head, "Gaze", new Vector3(0f, 0.16f, 0.02f), new Vector3(0.205f, 0.075f, 0.23f), gauze);
            FigureKit.Ball(_head, "Boca", new Vector3(0f, 0.05f, 0.1f), new Vector3(0.09f, 0.035f, 0.03f), red);
            FigureKit.Ball(_head, "Quepe", new Vector3(0f, 0.27f, -0.01f), new Vector3(0.2f, 0.08f, 0.2f), cap);
            FigureKit.Ball(_head, "Quepe_Divisa", new Vector3(0f, 0.25f, 0.095f), new Vector3(0.1f, 0.025f, 0.012f), brass);

            bool dressed = model != null && !KeepAllJoints;
            if (!KeepAllJoints)
                foreach (Transform joint in new List<Transform>(_live))
                    if (dressed) FigureKit.Strip(joint, _live); else FigureKit.Bake(joint, _live);

            // A pose parada é a de encaixe: foi nela que o Blender esculpiu o modelo.
            Pose(0f, 0f, 1f, 0f, 0f);
            if (dressed) CharacterModel.Attach(transform, model, modelMaterials);
        }
    }
}
