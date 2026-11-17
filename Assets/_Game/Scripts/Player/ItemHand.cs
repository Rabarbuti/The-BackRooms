using System.Collections.Generic;
using BackRooms.Items;
using UnityEngine;

namespace BackRooms.Player
{
    /// <summary>
    /// A mão do jogador em primeira pessoa, segurando o item em uso (por enquanto, a caneta):
    /// luva de borracha preta, punho e manga amarela do traje entrando pelo canto de baixo da
    /// tela. Montada em código (ver <see cref="FigureKit"/>). Só o dono vê.
    ///
    /// Fica num objeto filho da câmera, que a caneta liga e desliga. Ao ser ligada a mão sobe
    /// de fora da tela; parada, balança de leve; riscando, avança e treme um pouco.
    ///
    /// Perto de uma superfície (parede, chão, teto, móvel) o braço encolhe: a mão fica no canto
    /// de baixo, à direita, e a ponta da caneta vai ao ponto mirado (encostada riscando, um
    /// pouco antes parada). Colada na parede, a mão desce para fora do caminho. Por último,
    /// a cada quadro, nenhuma peça passa da superfície: se passaria, a mão inteira recua.
    /// Sem isso a mão, que fica a 0,4 m da câmera com a ponta a quase 0,6 m, entrava na parede
    /// sempre que o jogador encostava nela (o olho fica a 0,33 m da parede, raio mais pele).
    /// </summary>
    public sealed class ItemHand : MonoBehaviour
    {
        public Material suitMaterial;
        public Material gloveMaterial;
        [Tooltip("Material inicial da caneta. A cor muda com a tinta escolhida.")]
        public Material penMaterial;
        [Tooltip("De quem ler se o jogador está riscando.")]
        public PenTool pen;

        /// <summary>O corpo da caneta, para trocar a cor conforme a tinta.</summary>
        public Renderer PenRenderer { get; private set; }

        /// <summary>A ponta da caneta, no mundo (para os testes e a prévia).</summary>
        public Vector3 TipPosition => transform.TransformPoint(_tip);

        // Distâncias da câmera à superfície mirada, em metros.
        const float FarFrom = 0.95f;  // daqui para longe, o braço estendido de sempre
        const float TouchAt = 0.72f;  // daqui para perto, a ponta da caneta vai ao ponto mirado
        const float BentAt = 0.33f;   // colado na parede (raio do jogador e a pele): cotovelo todo dobrado
        const float GoneAt = 0.12f;   // mais perto que isso a mão já desceu para fora do caminho
        const float Margin = 0.003f;  // nenhuma peça chega mais perto da superfície que isso

        // Cotovelo dobrado: a caneta vem de baixo e da direita, apontando para o meio da tela.
        static readonly Vector3 BentAxis = new Vector3(-0.50f, 0.52f, 0.69f).normalized;
        // Colada na parede: a caneta em pé, quase paralela a ela, subindo do canto de baixo.
        static readonly Vector3 TuckedAxis = new Vector3(-0.25f, 0.95f, 0.20f).normalized;

        static readonly RaycastHit[] Hits = new RaycastHit[16];
        static readonly Dictionary<Mesh, Vector3[]> VertexCache = new Dictionary<Mesh, Vector3[]>();

        Vector3 _restPosition;
        Quaternion _restRotation;
        float _raise;
        float _press;
        bool _built;
        bool _hasRest;

        Vector3 _tip;       // ponta da caneta, no espaço deste objeto
        Vector3 _penAxis;   // do punho para a ponta, no espaço deste objeto
        Vector3[] _hull;    // pontos extremos de cada peça, no espaço deste objeto

        // O que a câmera tem na frente, medido a cada quadro.
        float _aimDistance = float.PositiveInfinity;
        Vector3 _aimNormal = Vector3.back;
        readonly Vector3[] _planePoint = new Vector3[2];
        readonly Vector3[] _planeNormal = new Vector3[2];
        int _planes;

        float _distance;    // distância suavizada que decide a pose
        bool _hasDistance;

        void Awake()
        {
            Build();
        }

        void OnEnable()
        {
            if (!_hasRest)
            {
                _restPosition = transform.localPosition;
                _restRotation = transform.localRotation;
                _hasRest = true;
            }

            // Começa abaixada, fora da tela, e sobe.
            _raise = 0f;
            _hasDistance = false;
            Sense();
            Follow(0f);
            Apply(0f);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            _raise = Mathf.MoveTowards(_raise, 1f, dt / 0.2f);
            _press = Mathf.MoveTowards(_press, pen != null && pen.IsDrawing ? 1f : 0f, dt * 9f);

            // Depois do PlayerController, que move a câmera no Update: mede com a câmera de agora.
            Sense();
            Follow(dt);
            Apply(Time.time);
        }

        /// <summary>
        /// Para a ferramenta de prévia, fora do Play (sem LateUpdate): põe a mão já erguida, na
        /// pose de agora, com <paramref name="press"/> de 0 (parada) a 1 (riscando), sem balanço.
        /// </summary>
        public void PreviewPose(float press)
        {
            Build();
            if (!_hasRest)
            {
                _restPosition = transform.localPosition;
                _restRotation = transform.localRotation;
                _hasRest = true;
            }

            _raise = 1f;
            _press = press;
            _hasDistance = false;
            Sense();
            Follow(0f);
            Apply(0f);
        }

        // Dois raios do olho: o da mira (a superfície que se risca) e o que vai até a ponta da
        // caneta na pose parada (a outra parede, num canto). Cada um que acerta vira um plano
        // que nenhuma peça pode passar.
        void Sense()
        {
            _planes = 0;
            _aimDistance = float.PositiveInfinity;

            Transform eye = transform.parent;
            if (eye == null || !_built) return;

            if (Probe(eye.position, eye.forward, FarFrom + 0.1f, out RaycastHit hit))
            {
                _aimDistance = hit.distance;
                _aimNormal = hit.normal;
                AddPlane(hit);
            }

            Vector3 toTip = eye.TransformPoint(_restPosition + _restRotation * _tip) - eye.position;
            if (toTip.sqrMagnitude > 1e-6f && Probe(eye.position, toTip.normalized, toTip.magnitude + 0.12f, out hit))
                AddPlane(hit);
        }

        void AddPlane(RaycastHit hit)
        {
            if (_planes >= _planePoint.Length) return;
            _planePoint[_planes] = hit.point;
            _planeNormal[_planes] = hit.normal;
            _planes++;
        }

        // Como o PenTool.TryAim: o raio sai da câmera e ignora o próprio jogador (o colisor dele
        // anda um passo de física atrás). Aqui vale qualquer coisa sólida, até outro jogador.
        bool Probe(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            hit = default;
            int count = Physics.RaycastNonAlloc(origin, direction, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            Transform self = transform.root;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.transform.root == self) continue;
                if (found && Hits[i].distance >= hit.distance) continue;

                hit = Hits[i];
                found = true;
            }

            return found;
        }

        // A pose segue a distância suavizada: chega perto depressa e se afasta devagar, sem
        // estalar entre uma pose e outra. O que impede atravessar é o Clear, no mesmo quadro.
        void Follow(float dt)
        {
            float target = Mathf.Min(_aimDistance, FarFrom + 0.1f);
            if (!_hasDistance || dt <= 0f)
            {
                _distance = target;
                _hasDistance = true;
                return;
            }

            float rate = target < _distance ? 25f : 8f;
            _distance = Mathf.Lerp(_distance, target, 1f - Mathf.Exp(-rate * dt));
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        void Apply(float time)
        {
            float ease = Smooth(_raise);

            // 0 longe, 1 ao alcance; 0 braço estendido, 1 cotovelo dobrado; 1 mão fora do caminho.
            float reach = Smooth(Mathf.InverseLerp(FarFrom, TouchAt, _distance));
            float bend = Smooth(Mathf.InverseLerp(TouchAt, BentAt, _distance));
            float gone = Smooth(Mathf.InverseLerp(BentAt, GoneAt, _distance));

            // Parada: respira. Riscando: avança 5 cm e treme, como quem faz força na parede.
            var sway = new Vector3(Mathf.Sin(time * 1.1f) * 0.003f, Mathf.Sin(time * 1.7f) * 0.004f, 0f);
            var push = new Vector3(-0.012f, 0.012f, 0.05f) * _press;
            var shake = new Vector3(Mathf.Sin(time * 37f), Mathf.Sin(time * 43f), 0f) * (0.0018f * _press);
            var lowered = new Vector3(0.05f, -0.28f, -0.06f) * (1f - ease);

            Quaternion rotation = _restRotation * Quaternion.Euler(22f * (1f - ease) - 5f * _press * (1f - reach), 0f, Mathf.Sin(time * 0.9f) * 0.8f);

            // Cotovelo dobrando: gira a caneta para vir do canto de baixo, à direita; colada na
            // parede, deita a caneta quase paralela a ela.
            Vector3 axis = rotation * _penAxis;
            Vector3 turned = Vector3.Slerp(Vector3.Slerp(axis, BentAxis, bend), TuckedAxis, gone);
            rotation = Quaternion.FromToRotation(axis, turned) * rotation;

            // Onde fica a ponta: longe, onde sempre ficou; ao alcance, no ponto mirado (encostada
            // riscando, 3 cm antes parada).
            Vector3 restTip = _restPosition + push + rotation * _tip;
            Transform eye = transform.parent;
            Vector3 normal = eye != null && _aimDistance < float.PositiveInfinity ? eye.InverseTransformDirection(_aimNormal) : Vector3.back;
            Vector3 aimed = new Vector3(0f, 0f, _distance) + normal * Mathf.Lerp(0.03f, 0.003f, _press);
            Vector3 tip = Vector3.Lerp(restTip, aimed, reach);

            // Colada na parede a mão não cabe na frente do olho: desce pela borda de baixo da
            // tela, à direita, e fica só a caneta em pé, subindo do canto (longe da mira).
            var tucked = new Vector3(0.06f, -0.01f, Mathf.Max(0.10f, _distance - 0.03f));
            tip = Vector3.Lerp(tip, tucked, gone);

            Vector3 position = tip - rotation * _tip + sway + shake + lowered;

            transform.localPosition = Clear(position, rotation);
            transform.localRotation = rotation;
        }

        // Nenhuma peça passa dos planos medidos: se passaria, a mão inteira recua em linha reta
        // para trás (assim não anda pela tela); de uma parede quase paralela ao olhar, sai pela normal.
        Vector3 Clear(Vector3 position, Quaternion rotation)
        {
            Transform eye = transform.parent;
            if (eye == null || _planes == 0 || _hull == null) return position;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int p = 0; p < _planes; p++)
                {
                    Vector3 point = eye.InverseTransformPoint(_planePoint[p]);
                    Vector3 normal = eye.InverseTransformDirection(_planeNormal[p]);

                    float nearest = float.PositiveInfinity;
                    foreach (Vector3 corner in _hull)
                        nearest = Mathf.Min(nearest, Vector3.Dot(position + rotation * corner - point, normal));

                    if (nearest >= Margin) continue;

                    float depth = Margin - nearest;
                    if (normal.z < -0.3f) position.z -= depth / -normal.z;
                    else position += normal * depth;
                }
            }

            return position;
        }

        /// <summary>
        /// Quanto a peça da mão que mais avança passa do plano dado (ponto e normal virada para
        /// o jogador), em metros, pelos vértices das malhas: positivo atravessou, negativo ficou
        /// antes. Para os testes e a prévia.
        /// </summary>
        public float DepthPast(Vector3 point, Vector3 normal)
        {
            float deepest = float.NegativeInfinity;

            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                Vector3[] vertices = Vertices(filter.sharedMesh);
                if (vertices == null) continue;

                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
                foreach (Vector3 vertex in vertices)
                    deepest = Mathf.Max(deepest, -Vector3.Dot(toWorld.MultiplyPoint3x4(vertex) - point, normal));
            }

            return deepest;
        }

        // Vértices de uma malha (guardados); sem leitura liberada, os cantos da caixa dela.
        static Vector3[] Vertices(Mesh mesh)
        {
            if (mesh == null) return null;
            if (VertexCache.TryGetValue(mesh, out Vector3[] vertices)) return vertices;

            if (mesh.isReadable) vertices = mesh.vertices;
            else
            {
                Bounds b = mesh.bounds;
                vertices = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    vertices[i] = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            }

            VertexCache[mesh] = vertices;
            return vertices;
        }

        // Pontos extremos de cada peça em 98 direções, no espaço deste objeto: a cada quadro a
        // mão confere só esses (uns poucos milhares) contra a superfície, em vez de todos os
        // vértices. Erro de 1 a 2 mm na esfera maior.
        void BuildHull()
        {
            var directions = new HashSet<Vector3>();
            for (int x = -2; x <= 2; x++)
                for (int y = -2; y <= 2; y++)
                    for (int z = -2; z <= 2; z++)
                        if (x != 0 || y != 0 || z != 0) directions.Add(new Vector3(x, y, z).normalized);

            var hull = new HashSet<Vector3>();
            Matrix4x4 toLocal = transform.worldToLocalMatrix;

            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                Vector3[] vertices = Vertices(filter.sharedMesh);
                if (vertices == null) continue;

                Matrix4x4 toHand = toLocal * filter.transform.localToWorldMatrix;
                var local = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) local[i] = toHand.MultiplyPoint3x4(vertices[i]);

                foreach (Vector3 direction in directions)
                {
                    int best = 0;
                    float bestDot = float.NegativeInfinity;
                    for (int i = 0; i < local.Length; i++)
                    {
                        float dot = Vector3.Dot(local[i], direction);
                        if (dot > bestDot) { bestDot = dot; best = i; }
                    }
                    hull.Add(local[best]);
                }
            }

            _hull = new Vector3[hull.Count];
            hull.CopyTo(_hull);
        }

        /// <summary>Monta as peças. Pode ser chamado mais de uma vez; só a primeira constrói.</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;

            Material glove = gloveMaterial != null ? gloveMaterial : suitMaterial;

            // Neste objeto, +Z é para a frente (para longe do olho), +Y para cima e +X para a
            // direita. O punho fica na origem; a manga desce para trás e para a direita, para
            // fora da tela, de onde viria o braço.
            Transform wrist = FigureKit.Joint(transform, "Punho", Vector3.zero);
            wrist.localRotation = Quaternion.FromToRotation(Vector3.down, new Vector3(0.30f, -0.42f, -0.86f).normalized);
            FigureKit.Limb(wrist, "Manga", 0.55f, 0.052f, true, suitMaterial, false);
            FigureKit.Ball(wrist, "Punho_Da_Luva", new Vector3(0f, -0.035f, 0f), new Vector3(0.118f, 0.06f, 0.118f), glove, false);
            FigureKit.Ball(wrist, "Punho_Do_Traje", new Vector3(0f, -0.085f, 0f), new Vector3(0.122f, 0.05f, 0.122f), suitMaterial, false);

            // Mão fechada em torno da caneta, com as costas viradas para cima e para a direita.
            Transform hand = FigureKit.Joint(transform, "Mao", new Vector3(-0.005f, 0.012f, 0.03f), new Vector3(-12f, -24f, -38f));
            FigureKit.Ball(hand, "Palma", new Vector3(0f, 0f, 0.035f), new Vector3(0.088f, 0.05f, 0.105f), glove, false);

            // Quatro dedos: saem da frente da palma, descem e voltam, fechando o punho.
            for (int finger = 0; finger < 4; finger++)
            {
                float x = -0.031f + finger * 0.0205f;
                float shorter = finger == 3 ? 0.006f : 0f;

                Transform knuckle = FigureKit.Joint(hand, "Dedo_" + finger, new Vector3(x, 0.004f, 0.082f - Mathf.Abs(finger - 1.2f) * 0.004f),
                                                    new Vector3(-48f + finger * 5f, 0f, 0f));
                Transform middle = FigureKit.Limb(knuckle, "Dedo_Meio_" + finger, 0.040f - shorter, 0.0115f, true, glove, false);
                middle.localRotation = Quaternion.Euler(74f, 0f, 0f);
                Transform tip = FigureKit.Limb(middle, "Dedo_Ponta_" + finger, 0.030f - shorter, 0.0105f, true, glove, false);
                tip.localRotation = Quaternion.Euler(58f, 0f, 0f);
                FigureKit.Limb(tip, "Dedo_Fim_" + finger, 0.022f, 0.0095f, true, glove, false);
            }

            // Polegar: sai do lado de dentro da palma e encosta na caneta por cima.
            Transform thumb = FigureKit.Joint(hand, "Polegar", new Vector3(-0.042f, 0.002f, 0.022f), new Vector3(-62f, 0f, -34f));
            Transform thumbTip = FigureKit.Limb(thumb, "Polegar_Ponta", 0.042f, 0.013f, true, glove, false);
            thumbTip.localRotation = Quaternion.Euler(-18f, 0f, 30f);
            FigureKit.Limb(thumbTip, "Polegar_Fim", 0.030f, 0.0115f, true, glove, false);

            // Caneta: atravessa o punho fechado, com a ponta para a frente e um pouco para cima.
            Transform grip = FigureKit.Joint(hand, "Caneta", new Vector3(-0.022f, -0.022f, 0.012f));
            grip.localRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(-0.10f, 0.30f, 0.95f).normalized);
            Transform nib = FigureKit.Limb(grip, "Caneta_Ponta", 0.155f, 0.0058f, false, penMaterial, false);
            PenRenderer = grip.Find("Caneta_Peca").GetComponent<Renderer>();
            FigureKit.Ball(nib, "Bico", new Vector3(0f, 0.006f, 0f), new Vector3(0.006f, 0.018f, 0.006f), glove, false);

            // A ponta é o fim do bico (esfera de 18 mm de altura, 6 mm acima da junta).
            _tip = transform.InverseTransformPoint(nib.TransformPoint(0f, 0.015f, 0f));
            _penAxis = transform.InverseTransformDirection(grip.up);
            BuildHull();
        }
    }
}
