using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    /// <summary>
    /// A água das Piscinas (Level 2): a lâmina que o <see cref="LevelBuilder"/> põe sobre o mapa.
    /// Guarda a altura da superfície — hoje fixa em Y = 0; a maré vai movê-la — e cuida do que
    /// depende dela na tela: a ondulação da textura e a névoa verde-água de quem está com a cabeça
    /// debaixo d'água.
    ///
    /// Nos outros níveis não existe, e <see cref="Current"/> fica nulo: o jogador, a filmadora e a
    /// caneta só olham para a água quando há uma.
    /// </summary>
    // Também roda no editor: as fotos da prévia são tiradas fora do Play e precisam da luz rebatida.
    [ExecuteAlways]
    public sealed class PoolWater : MonoBehaviour
    {
        /// <summary>A água do nível aberto, ou nulo se o nível não tem água.</summary>
        public static PoolWater Current { get; private set; }

        [Tooltip("Velocidade da ondulação da textura, em repetições por segundo.")]
        public Vector2 rippleSpeed = new Vector2(0.012f, 0.007f);
        [Tooltip("Distância em que a névoa de debaixo d'água fecha a visão, em metros.")]
        public float underwaterSight = 7f;

        /// <summary>Altura da superfície da água no mundo.</summary>
        public float SurfaceY => transform.position.y;

        /// <summary>
        /// Ligado pelo jogador local enquanto a câmera dele está debaixo d'água. Acende a névoa
        /// e o filtro de cor só nesta máquina.
        /// </summary>
        public bool LocalUnderwater { get; set; }

        Material _material;
        Volume _underwater;
        Volume _ambient;
        static readonly int BaseColorMapSt = Shader.PropertyToID("_BaseColorMap_ST");

        void OnEnable()
        {
            Current = this;
        }

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        void Start()
        {
            var surface = GetComponent<MeshRenderer>();
            if (surface != null && surface.sharedMaterial != null)
            {
                // Cópia própria: a ondulação mexe no deslocamento da textura a cada quadro.
                _material = new Material(surface.sharedMaterial) { name = surface.sharedMaterial.name + " (ondulando)" };
                surface.sharedMaterial = _material;
            }

            _underwater = CreateUnderwaterVolume();
            _ambient = CreateAmbientVolume();
        }

        void OnDestroy()
        {
            Release(_material);
            foreach (Volume volume in new[] { _underwater, _ambient })
            {
                if (volume == null) continue;
                Release(volume.sharedProfile);
                Release(volume.gameObject);
            }
        }

        static void Release(Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) Destroy(item);
            else DestroyImmediate(item);
        }

        void Update()
        {
            if (_material != null && _material.HasProperty(BaseColorMapSt))
            {
                Vector4 st = _material.GetVector(BaseColorMapSt);
                Vector2 offset = rippleSpeed * Time.time;
                _material.SetVector(BaseColorMapSt, new Vector4(st.x, st.y, offset.x % 1f, offset.y % 1f));
            }

            if (_underwater != null)
                _underwater.weight = Mathf.MoveTowards(_underwater.weight, LocalUnderwater ? 1f : 0f, Time.deltaTime * 6f);
        }

        [Tooltip("Multiplica a luz indireta: azulejo branco rebate muita luz, e os cantos não ficam breu.")]
        public float ambientBounce = 2.5f;

        // Volume global sempre ligado nas Piscinas: mais luz indireta que no Level 0.
        Volume CreateAmbientVolume()
        {
            var go = new GameObject("LuzRebatida");
            go.transform.SetParent(transform, false);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "LuzRebatida";
            IndirectLightingController indirect = profile.Add<IndirectLightingController>(true);
            indirect.indirectDiffuseLightingMultiplier.Override(ambientBounce);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 40f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
            return volume;
        }

        // Volume global por cima do da cena: névoa verde-água densa e a imagem puxada para o azul.
        Volume CreateUnderwaterVolume()
        {
            var go = new GameObject("DebaixoDagua");
            go.transform.SetParent(transform, false);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "DebaixoDagua";

            Fog fog = profile.Add<Fog>(true);
            fog.enabled.Override(true);
            fog.colorMode.Override(FogColorMode.ConstantColor);
            fog.color.Override(new Color(0.05f, 0.22f, 0.22f));
            fog.meanFreePath.Override(Mathf.Max(1f, underwaterSight));
            fog.baseHeight.Override(SurfaceY - 20f);
            fog.maximumHeight.Override(SurfaceY + 1f);

            ColorAdjustments color = profile.Add<ColorAdjustments>(true);
            color.colorFilter.Override(new Color(0.55f, 0.9f, 0.88f));
            color.saturation.Override(-20f);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;
            volume.weight = 0f;
            volume.sharedProfile = profile;
            return volume;
        }
    }
}
