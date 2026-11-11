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

        Vector3 _restPosition;
        Quaternion _restRotation;
        float _raise;
        float _press;
        bool _built;
        bool _hasRest;

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
            Apply(0f);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            _raise = Mathf.MoveTowards(_raise, 1f, dt / 0.2f);
            _press = Mathf.MoveTowards(_press, pen != null && pen.IsDrawing ? 1f : 0f, dt * 9f);
            Apply(Time.time);
        }

        void Apply(float time)
        {
            float ease = _raise * _raise * (3f - 2f * _raise);

            // Parada: respira. Riscando: avança 5 cm e treme, como quem faz força na parede.
            var sway = new Vector3(Mathf.Sin(time * 1.1f) * 0.003f, Mathf.Sin(time * 1.7f) * 0.004f, 0f);
            var push = new Vector3(-0.012f, 0.012f, 0.05f) * _press;
            var shake = new Vector3(Mathf.Sin(time * 37f), Mathf.Sin(time * 43f), 0f) * (0.0018f * _press);
            var lowered = new Vector3(0.05f, -0.28f, -0.06f) * (1f - ease);

            transform.localPosition = _restPosition + sway + push + shake + lowered;
            transform.localRotation = _restRotation * Quaternion.Euler(22f * (1f - ease) - 5f * _press, 0f, Mathf.Sin(time * 0.9f) * 0.8f);
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
        }
    }
}
