using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// As lâmpadas de alarme vermelhas das Piscinas (giroflex de parede, postas pela seed por
    /// <see cref="LevelBuilder"/>, BuildTideAlarms): piscam juntas quando a maré vai subir, do aviso
    /// até ela parar de subir (<see cref="PoolTide"/> liga). O piscar sai do relógio da sessão, então é
    /// o mesmo em todas as máquinas. Fora do alarme as luzes ficam desligadas (não custam nada).
    /// A lente é HDRP/Lit com o emissivo trocado por MaterialPropertyBlock (nada vai para o .mat).
    /// </summary>
    public sealed class PoolAlarm : MonoBehaviour
    {
        public const string RootName = "AlarmeMare";
        public const string HousingName = "Alarme_Caixa";
        public const string LensName = "Alarme_Lente";

        /// <summary>Cor da luz das lâmpadas de alarme.</summary>
        public static readonly Color LightColor = new Color(1f, 0.12f, 0.06f);
        /// <summary>Intensidade da luz no pico do clarão, em candela.</summary>
        public const float PeakCandela = 700f;
        /// <summary>Clarões por segundo (o espelho do giroflex dando a volta).</summary>
        public const float FlashHz = 1.4f;
        // Emissivo da lente em nits (a exposição fixa da fase divide por ~54): acesa estoura no bloom.
        static readonly Color LensOn = new Color(1f, 0.08f, 0.04f) * 110f;
        static readonly Color LensOff = new Color(1f, 0.08f, 0.04f) * 2f;
        static readonly int EmissiveId = Shader.PropertyToID("_EmissiveColor");

        Light[] _lights = new Light[0];
        MeshRenderer _lens;
        MaterialPropertyBlock _block;

        /// <summary>O alarme está ligado agora.</summary>
        public bool On { get; private set; }

        /// <summary>Brilho do clarão agora, de 0 a 1.</summary>
        public float Flash { get; private set; }

        public int LampCount => _lights.Length;

        public void Init(Light[] lights, MeshRenderer lens)
        {
            _lights = lights ?? new Light[0];
            _lens = lens;
            Set(false, 0.0);
        }

        /// <summary>Liga ou desliga o alarme e acerta o clarão pela hora da sessão (ou do teste).</summary>
        public void Set(bool on, double seconds)
        {
            float flash = on ? FlashAt(seconds) : 0f;
            if (on == On && Mathf.Abs(flash - Flash) < 0.004f && _block != null) return;
            On = on;
            Flash = flash;

            foreach (Light light in _lights)
            {
                if (light == null) continue;
                if (light.enabled != on) light.enabled = on;
                light.intensity = PeakCandela * flash;
            }

            if (_lens == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            _lens.GetPropertyBlock(_block);
            _block.SetColor(EmissiveId, Color.Lerp(LensOff, LensOn, flash));
            _lens.SetPropertyBlock(_block);
        }

        /// <summary>Brilho do giroflex aos <paramref name="seconds"/>: um clarão curto e forte por volta, com um resto aceso.</summary>
        public static float FlashAt(double seconds)
        {
            double turn = seconds * FlashHz;
            float c = Mathf.Cos((float)((turn - System.Math.Floor(turn)) * 2.0 * System.Math.PI));
            float beam = Mathf.Pow(Mathf.Max(0f, c), 4f);
            return 0.12f + 0.88f * beam;
        }
    }
}
