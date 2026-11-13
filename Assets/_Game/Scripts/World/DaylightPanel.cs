using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    /// <summary>
    /// A luz do dia num instante, nas Piscinas: o que o anoitecer faz com a fase. Sai de
    /// <see cref="PoolWater.DaylightAt"/>, que é função só do relógio da sessão.
    /// </summary>
    public struct Daylight
    {
        /// <summary>Quanto sobra da luz das claraboias e janelas: 1 = dia, 0 = noite.</summary>
        public float Sun;
        /// <summary>0 = luz branca do dia, 1 = fim de tarde alaranjado.</summary>
        public float Warmth;
        /// <summary>0 = dia, 1 = noite: vidro azul-noite, céu azul-marinho.</summary>
        public float Night;

        public static Daylight Day => new Daylight { Sun = 1f };
    }

    /// <summary>
    /// Vidro de luz do dia das Piscinas (claraboia, janelão, frestas, vigia acesa, tijolo de vidro)
    /// e as luzes que entram por ele. Não é luminária: não pisca, não queima e não apaga seco. O
    /// <see cref="PoolWater"/> leva todos juntos pela curva do anoitecer: a luz cai pelo dimmer (a
    /// sombra em cache não é refeita), a temperatura esquenta e o vidro vai do branco estourado ao
    /// azul-noite por um MaterialPropertyBlock (o material nunca troca).
    ///
    /// Fica fora de <see cref="CeilingLight.All"/>: o <see cref="LightsOut"/> nem sabe que ele existe,
    /// e o apagão do Level 0 continua igual.
    /// </summary>
    // Também no editor: as fotos da prévia são tiradas fora do Play (e a lista tem de se limpar).
    [ExecuteAlways]
    public sealed class DaylightPanel : MonoBehaviour
    {
        /// <summary>Todos os vidros de luz do dia do nível construído.</summary>
        public static readonly List<DaylightPanel> All = new List<DaylightPanel>();

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");

        HDAdditionalLightData[] _lights;
        Light[] _sources;
        float[] _dayTemperature;
        MeshRenderer _glass;
        Color _dayBase, _dayEmissive, _nightBase, _nightEmissive;
        MaterialPropertyBlock _block;
        Daylight _applied = new Daylight { Sun = -1f };

        /// <summary>Quanto sobra da luz agora (o dimmer das luzes): 1 = dia, 0 = noite.</summary>
        public float Sun => _applied.Sun;

        /// <summary>Quantas luzes entram por este vidro.</summary>
        public int LightCount => _lights != null ? _lights.Length : 0;

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDestroy()
        {
            All.Remove(this);
        }

        /// <summary>
        /// <paramref name="day"/> é o material do vidro aceso (fica no renderer o tempo todo); de
        /// noite a cor e a emissão vão até <paramref name="nightBase"/> e <paramref name="nightEmissive"/>.
        /// </summary>
        public void Init(HDAdditionalLightData[] lights, MeshRenderer glass, Material day, Color nightBase, Color nightEmissive)
        {
            _lights = lights ?? new HDAdditionalLightData[0];
            _sources = new Light[_lights.Length];
            _dayTemperature = new float[_lights.Length];
            for (int i = 0; i < _lights.Length; i++)
            {
                _sources[i] = _lights[i] != null ? _lights[i].GetComponent<Light>() : null;
                _dayTemperature[i] = _sources[i] != null ? _sources[i].colorTemperature : 6500f;
            }

            _glass = glass;
            if (_glass != null && day != null) _glass.sharedMaterial = day;
            _dayBase = day != null && day.HasProperty(BaseColorId) ? day.GetColor(BaseColorId) : Color.white;
            _dayEmissive = day != null && day.HasProperty(EmissiveColorId) ? day.GetColor(EmissiveColorId) : Color.black;
            _nightBase = nightBase;
            _nightEmissive = nightEmissive;

            if (!All.Contains(this)) All.Add(this);
            _applied = new Daylight { Sun = -1f };
            Apply(Daylight.Day, 2600f, Color.white);
        }

        /// <summary>
        /// Põe o vidro e as luzes no estado <paramref name="light"/>: luz vezes Sun, temperatura do dia
        /// até <paramref name="warmTemperature"/> e brilho do vidro tingido por <paramref name="warmTint"/>
        /// conforme Warmth, e o vidro até o azul-noite conforme Night.
        /// </summary>
        public void Apply(Daylight light, float warmTemperature, Color warmTint)
        {
            if (Same(light.Sun, _applied.Sun) && Same(light.Warmth, _applied.Warmth) && Same(light.Night, _applied.Night)) return;
            _applied = light;

            for (int i = 0; i < _lights.Length; i++)
            {
                HDAdditionalLightData data = _lights[i];
                if (data == null) continue;
                data.lightDimmer = light.Sun;
                data.volumetricDimmer = light.Sun;
                if (_sources[i] != null) _sources[i].colorTemperature = Mathf.Lerp(_dayTemperature[i], warmTemperature, light.Warmth);
            }

            if (_glass == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            Color dayEmissive = _dayEmissive * Color.Lerp(Color.white, warmTint, light.Warmth);
            _glass.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, Color.Lerp(_dayBase, _nightBase, light.Night));
            // O brilho cai em proporção (de 500 para 16 nits, em linha reta, ficava estourado até o fim).
            // SetVector, não SetColor: a emissão é HDR e já está em linear no material; o SetColor do
            // bloco converteria de novo (500 virava centenas de milhares e a tela estourava).
            _block.SetVector(EmissiveColorId, Proportional(dayEmissive, _nightEmissive, light.Night));
            _glass.SetPropertyBlock(_block);
        }

        static bool Same(float a, float b) => Mathf.Abs(a - b) < 1e-4f;

        static Color Proportional(Color a, Color b, float t)
        {
            float Channel(float x, float y) => Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(x, 1e-3f)), Mathf.Log(Mathf.Max(y, 1e-3f)), t));
            return new Color(Channel(a.r, b.r), Channel(a.g, b.g), Channel(a.b, b.b), 1f);
        }
    }
}
