using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BackRooms.Entities
{
    /// <summary>
    /// O corpo da entidade, montado em código com cápsulas e esferas: uma figura preta de
    /// 3 m, magra e curvada, com braços que quase arrastam no chão, pernas dobradas para
    /// trás, cabeça pequena sem rosto (dois pontos claros de tamanhos diferentes) e fios
    /// pendurados nas costas. Sem modelo nem animação importados.
    ///
    /// A animação também é em código e sai da velocidade com que o objeto se desloca, então
    /// funciona igual no host e nos clientes, sem sincronizar nada além da posição.
    /// </summary>
    public sealed class EntityBody : MonoBehaviour
    {
        public Material bodyMaterial;
        public Material eyeMaterial;

        [Tooltip("Metros andados por ciclo completo de passada (dois passos).")]
        public float strideLength = 2.6f;

        /// <summary>Disparado a cada pé que bate no chão, com a velocidade do momento.</summary>
        public event Action<float> Stepped;

        const float HipHeight = 1.55f;
        const float ThighLength = 0.80f;
        const float ShinLength = 0.82f;
        const float UpperArmLength = 0.85f;
        const float ForearmLength = 0.95f;
        const int Tendrils = 7;

        static Mesh _capsule;
        static Mesh _sphere;

        Transform _rig;
        Transform _spineLow, _spineHigh, _neck, _head;
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

            float before = _phase;
            _phase += _speed / Mathf.Max(0.5f, strideLength) * dt;
            if (_speed > 0.5f && Mathf.FloorToInt(_phase * 2f) != Mathf.FloorToInt(before * 2f)) Stepped?.Invoke(_speed);
            if (_phase > 1000f) _phase -= 1000f;

            // Tique da cabeça: de tempos em tempos ela pula para um ângulo torto.
            if (Time.time >= _nextTwitch)
            {
                float wide = Mathf.Lerp(9f, 26f, _hunt);
                _twitchTarget = Quaternion.Euler(UnityEngine.Random.Range(-wide, wide) * 0.5f, UnityEngine.Random.Range(-wide, wide), UnityEngine.Random.Range(-wide, wide));
                _nextTwitch = Time.time + UnityEngine.Random.Range(0.08f, Mathf.Lerp(1.4f, 0.35f, _hunt));
            }
            _twitch = Quaternion.Slerp(_twitch, _twitchTarget, 1f - Mathf.Exp(-30f * dt));

            Pose(_phase, _speed, _hunt, Time.time);
        }

        /// <summary>
        /// Coloca o corpo numa pose. <paramref name="phase"/> é a passada (1 = ciclo completo),
        /// <paramref name="hunt"/> vai de 0 (vagando) a 1 (caçando). Público para a ferramenta
        /// de prévia, que fotografa fora do Play.
        /// </summary>
        public void Pose(float phase, float speed, float hunt, float time)
        {
            if (!_built) Build();

            float run = Mathf.Clamp01(speed / 4.6f);
            float cycle = phase * Mathf.PI * 2f;
            float swing = Mathf.Sin(cycle);
            float lift = Mathf.Cos(cycle);
            float breathe = Mathf.Sin(time * 1.3f);

            // Corpo: sobe e desce a cada passo, inclina para a frente correndo.
            float bob = -Mathf.Abs(Mathf.Sin(cycle)) * 0.07f * run;
            _rig.localPosition = new Vector3(0f, bob, 0f);
            _rig.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(cycle) * 2.5f * run);

            // O quanto o tronco está curvado para a frente. Braços, pescoço e fios nascem dos
            // ombros e descontam esse ângulo, para pender em relação ao chão e não ao tronco.
            float leanLow = 16f + 12f * run + breathe * 1.2f;
            float leanHigh = 14f + 8f * hunt + breathe * 1.5f;
            float lean = leanLow + leanHigh;

            _spineLow.localRotation = Quaternion.Euler(leanLow, swing * 5f * run, 0f);
            _spineHigh.localRotation = Quaternion.Euler(leanHigh, -swing * 7f * run, 0f);
            _neck.localRotation = Quaternion.Euler(-lean + 6f - 10f * hunt, 0f, 0f);
            _head.localRotation = _twitch * Quaternion.Euler(18f, 0f, 9f);

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

            // Braços: pendurados e balançando ao contrário das pernas; caçando, estendem para a frente.
            float armSwing = Mathf.Lerp(3f, 24f, run);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                float forward = -swing * side;
                float sway = Mathf.Sin(time * 0.9f + i * 2.1f) * 3f;

                // Caçando, os dois braços vão para a frente e balançam menos: vista de frente,
                // ela vem com as mãos estendidas, e não acenando.
                float reach = 6f + 30f * hunt + forward * armSwing * (1f - 0.45f * hunt);
                _upperArm[i].localRotation = Quaternion.Euler(-lean - reach + sway, 0f, side * (7f + 4f * hunt));
                _forearm[i].localRotation = Quaternion.Euler(-(12f + 16f * hunt + Mathf.Max(0f, forward) * 10f * run), 0f, -side * 6f);
            }

            // Fios: balançam sozinhos e ficam para trás quando ela corre.
            for (int i = 0; i < Tendrils; i++)
            {
                float seed = _tendrilSeed[i];
                float waveX = Mathf.Sin(time * (0.7f + seed * 0.9f) + seed * 17f) * 9f;
                float waveZ = Mathf.Sin(time * (0.5f + seed * 1.1f) + seed * 31f) * 9f;
                _tendril[i].localRotation = Quaternion.Euler(-lean + 10f + waveX + 55f * run, 0f, waveZ);
            }
        }

        /// <summary>Monta as peças. Pode ser chamado mais de uma vez; só a primeira constrói.</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;

            EnsureMeshes();

            _rig = new GameObject("Corpo").transform;
            _rig.SetParent(transform, false);

            // Bacia e pernas.
            Transform pelvis = Joint(_rig, "Bacia", new Vector3(0f, HipHeight, 0f));
            Ball(pelvis, "Bacia_Peca", new Vector3(0f, 0.02f, 0f), new Vector3(0.34f, 0.22f, 0.25f), bodyMaterial);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "E" : "D";

                _thigh[i] = Joint(pelvis, "Coxa_" + tag, new Vector3(side * 0.13f, 0f, 0f));
                _shin[i] = Limb(_thigh[i], "Canela_" + tag, ThighLength, 0.074f, down: true);
                _foot[i] = Limb(_shin[i], "Pe_" + tag, ShinLength, 0.052f, down: true);
                Ball(_foot[i], "Pe_Peca_" + tag, new Vector3(0f, -0.03f, 0.10f), new Vector3(0.10f, 0.06f, 0.30f), bodyMaterial);
            }

            // Tronco curvado, pescoço e cabeça.
            _spineLow = Joint(pelvis, "Coluna_Baixa", new Vector3(0f, 0.05f, 0f));
            _spineHigh = Limb(_spineLow, "Coluna_Alta", 0.56f, 0.135f, down: false);
            Transform shoulders = Limb(_spineHigh, "Ombros", 0.56f, 0.185f, down: false);
            _neck = Joint(shoulders, "Pescoco", Vector3.zero);
            _head = Limb(_neck, "Cabeca", 0.34f, 0.042f, down: false);
            Ball(_head, "Cabeca_Peca", new Vector3(0f, 0.13f, 0.02f), new Vector3(0.22f, 0.36f, 0.26f), bodyMaterial);

            // Dois pontos claros no lugar dos olhos, um maior e mais alto que o outro.
            Ball(_head, "Olho_E", new Vector3(-0.055f, 0.17f, 0.118f), Vector3.one * 0.040f, eyeMaterial);
            Ball(_head, "Olho_D", new Vector3(0.048f, 0.11f, 0.122f), Vector3.one * 0.022f, eyeMaterial);

            // Braços compridos, com três dedos finos cada.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string tag = i == 0 ? "E" : "D";

                Ball(shoulders, "Ombro_Peca_" + tag, new Vector3(side * 0.24f, -0.05f, 0f), Vector3.one * 0.17f, bodyMaterial);
                _upperArm[i] = Joint(shoulders, "Braco_" + tag, new Vector3(side * 0.28f, -0.05f, 0f));
                _forearm[i] = Limb(_upperArm[i], "Antebraco_" + tag, UpperArmLength, 0.045f, down: true);
                Transform hand = Limb(_forearm[i], "Mao_" + tag, ForearmLength, 0.034f, down: true);

                for (int finger = 0; finger < 3; finger++)
                {
                    Transform knuckle = Joint(hand, "Dedo_" + tag + finger, new Vector3((finger - 1) * 0.022f, 0f, 0f));
                    knuckle.localRotation = Quaternion.Euler(-12f + finger * 9f, 0f, (finger - 1) * 15f);
                    Limb(knuckle, "Dedo_Ponta_" + tag + finger, 0.26f + finger * 0.04f, 0.012f, down: true);
                }
            }

            // Fios nas costas e nos ombros.
            var rng = new System.Random(331);
            for (int i = 0; i < Tendrils; i++)
            {
                _tendrilSeed[i] = (float)rng.NextDouble();
                Transform anchor = i < 5 ? shoulders : _spineHigh;
                var offset = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.36f, -(float)rng.NextDouble() * 0.25f, -0.08f - (float)rng.NextDouble() * 0.08f);

                _tendril[i] = Joint(anchor, "Fio_" + i, offset);
                Limb(_tendril[i], "Fio_Ponta_" + i, 0.45f + (float)rng.NextDouble() * 0.75f, 0.011f, down: true);
            }

            Pose(0f, 0f, 0f, 0f);
        }

        static Transform Joint(Transform parent, string name, Vector3 localPosition)
        {
            var joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.localPosition = localPosition;
            return joint;
        }

        // Um osso: cápsula que sai da junta para baixo (ou para cima). Devolve a junta da ponta.
        Transform Limb(Transform joint, string endName, float length, float radius, bool down)
        {
            float direction = down ? -1f : 1f;

            var piece = new GameObject(joint.name + "_Peca");
            piece.transform.SetParent(joint, false);
            piece.transform.localPosition = new Vector3(0f, direction * length * 0.5f, 0f);
            // A cápsula padrão tem 2 m de altura e 0,5 m de raio.
            piece.transform.localScale = new Vector3(radius * 2f, length * 0.5f + radius * 0.5f, radius * 2f);
            AddRenderer(piece, _capsule, bodyMaterial);

            return Joint(joint, endName, new Vector3(0f, direction * length, 0f));
        }

        static void Ball(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material)
        {
            var piece = new GameObject(name);
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localScale = size;
            AddRenderer(piece, _sphere, material);
        }

        static void AddRenderer(GameObject piece, Mesh mesh, Material material)
        {
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = piece.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }

        // Pega as malhas padrão do Unity de um primitivo temporário (que é apagado em seguida).
        static void EnsureMeshes()
        {
            if (_capsule == null) _capsule = BuiltinMesh(PrimitiveType.Capsule);
            if (_sphere == null) _sphere = BuiltinMesh(PrimitiveType.Sphere);
        }

        static Mesh BuiltinMesh(PrimitiveType type)
        {
            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(temp);
            return mesh;
        }
    }
}
