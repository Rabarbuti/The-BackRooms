using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    /// <summary>
    /// A água das Piscinas (Level 2): a lâmina que o <see cref="LevelBuilder"/> põe sobre o mapa.
    /// Guarda a altura da superfície — hoje fixa em Y = 0; a maré vai movê-la — e cuida do que
    /// depende dela na tela:
    /// - a ondulação: as duas camadas de normais do material rolando para lados diferentes;
    /// - a cáustica (o objeto com o material "Pool_Caustics"): duas redes de linhas rolando,
    ///   apagada no apagão e mais forte para quem está debaixo d'água;
    /// - debaixo d'água: névoa verde-água clara (visão de ~10 m) e imagem puxada para o ciano,
    ///   só na máquina de quem está com a cabeça submersa;
    /// - o apagão vira "anoitecer": céu azul-marinho por cima do escuro do <see cref="LightsOut"/>;
    /// - salas escuras de propósito (<see cref="PoolRoom.Dark"/>): a luz ambiente cai enquanto a
    ///   câmera está dentro delas (o céu do HDRP não é barrado por parede; sem isso nenhuma sala
    ///   escurece).
    ///
    /// As animações usam MaterialPropertyBlock, nunca uma cópia do material: nada disso vai
    /// parar no arquivo da cena nem no .mat.
    ///
    /// Nos outros níveis não existe, e <see cref="Current"/> fica nulo: o jogador, a filmadora e a
    /// caneta só olham para a água quando há uma.
    /// </summary>
    // Também roda no editor: as fotos da prévia são tiradas fora do Play.
    [ExecuteAlways]
    public sealed class PoolWater : MonoBehaviour
    {
        /// <summary>A água do nível aberto, ou nulo se o nível não tem água.</summary>
        public static PoolWater Current { get; private set; }

        /// <summary>Nome do material da cáustica (Level2Setup); os objetos com ele são animados aqui.</summary>
        public const string CausticsMaterialName = "Pool_Caustics";

        [Header("Ondulação")]
        [Tooltip("Velocidade da ondulação grande (mapa de normais), em repetições por segundo.")]
        public Vector2 rippleSpeed = new Vector2(0.016f, 0.012f);
        [Tooltip("Velocidade da ondulação miúda (mapa de detalhe), em repetições por segundo, para outro lado.")]
        public Vector2 detailSpeed = new Vector2(-0.024f, 0.032f);

        [Header("Cáustica")]
        [Tooltip("Velocidade das duas redes de linhas, em repetições por segundo.")]
        public Vector2 causticSpeed = new Vector2(0.018f, 0.011f);
        public Vector2 causticDetailSpeed = new Vector2(-0.013f, -0.021f);
        [Tooltip("Quanto a cáustica cresce para quem está debaixo d'água.")]
        public float causticUnderwaterBoost = 1.8f;

        [Header("Debaixo d'água")]
        [Tooltip("Distância em que a névoa de debaixo d'água fecha a visão, em metros.")]
        public float underwaterSight = 10f;
        [Tooltip("Cor da névoa de debaixo d'água, em nits (a exposição fixa divide por ~54).")]
        public Color underwaterFog = new Color(19f, 38f, 37f);

        [Header("Apagão e salas escuras")]
        [Tooltip("Céu do apagão (azul-marinho), em nits: topo, meio e base.")]
        public Color nightTop = new Color(1.3f, 2.6f, 7f);
        public Color nightMiddle = new Color(1.1f, 2.2f, 6.4f);
        public Color nightBottom = new Color(1f, 2.4f, 5.8f);
        [Tooltip("Céu de dentro das salas escuras, em nits (o normal fica perto de 55).")]
        public Color darkRoomSky = new Color(3.5f, 5f, 4.6f);
        [Tooltip("Distância, em metros, que se anda para dentro da sala escura até ela escurecer de todo.")]
        public float darkRoomBlend = 3f;
        [Tooltip("Névoa preta (só absorve) dentro das salas escuras e dos túneis: alcance médio da vista, em metros. " +
                 "É o que faz a boca do túnel e a porta da sala escura parecerem buracos vistos de fora.")]
        public float darkRoomShade = 6f;
        public float tunnelShade = 11f;

        /// <summary>Altura da superfície da água no mundo.</summary>
        public float SurfaceY => transform.position.y;

        /// <summary>
        /// Ligado pelo jogador local enquanto a câmera dele está debaixo d'água. Acende a névoa
        /// e o filtro de cor só nesta máquina.
        /// </summary>
        public bool LocalUnderwater { get; set; }

        /// <summary>Para as fotos fora do Play: 0 a 1 força o "anoitecer"; negativo = segue o <see cref="LightsOut"/>.</summary>
        public float ForceNight { get; set; } = -1f;

        static readonly int BaseColorMapSt = Shader.PropertyToID("_BaseColorMap_ST");
        static readonly int DetailMapSt = Shader.PropertyToID("_DetailMap_ST");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        bool _prepared;
        MeshRenderer _surface;
        Vector4 _surfaceSt, _surfaceDetailSt;
        readonly List<MeshRenderer> _caustics = new List<MeshRenderer>();
        Vector4 _causticSt, _causticDetailSt;
        Color _causticColor;
        MaterialPropertyBlock _block;

        Volume _underwater, _night, _darkRoom;
        readonly List<Rect> _darkRects = new List<Rect>();
        float _nightWeight;

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
            Prepare();
        }

        void OnDestroy()
        {
            foreach (Volume volume in new[] { _underwater, _night, _darkRoom })
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

        /// <summary>
        /// Acha a superfície e a cáustica e cria os Volumes. Chamado no Start; a ferramenta de
        /// prévia chama antes de fotografar (fora do Play o Start pode não ter rodado).
        /// </summary>
        public void Prepare()
        {
            if (_prepared) return;
            _prepared = true;
            _block = new MaterialPropertyBlock();

            _surface = GetComponent<MeshRenderer>();
            if (_surface != null && _surface.sharedMaterial != null)
            {
                Material m = _surface.sharedMaterial;
                _surfaceSt = m.HasProperty(BaseColorMapSt) ? m.GetVector(BaseColorMapSt) : new Vector4(1f, 1f, 0f, 0f);
                _surfaceDetailSt = m.HasProperty(DetailMapSt) ? m.GetVector(DetailMapSt) : new Vector4(1f, 1f, 0f, 0f);
            }

            // A cáustica é montada pelo LevelBuilder no mesmo nível que a água.
            Transform level = transform.parent != null ? transform.parent : transform;
            foreach (MeshRenderer renderer in level.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material m = renderer.sharedMaterial;
                if (m == null || !m.name.StartsWith(CausticsMaterialName)) continue;
                if (_caustics.Count == 0)
                {
                    _causticSt = m.HasProperty(BaseColorMapSt) ? m.GetVector(BaseColorMapSt) : new Vector4(1f, 1f, 0f, 0f);
                    _causticDetailSt = m.HasProperty(DetailMapSt) ? m.GetVector(DetailMapSt) : new Vector4(1f, 1f, 0f, 0f);
                    _causticColor = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
                }
                _caustics.Add(renderer);
            }

            // Salas escuras e túneis, em metros no mundo.
            var builder = GetComponentInParent<LevelBuilder>();
            if (builder != null && builder.Data != null && builder.Data.HasPools)
                foreach (PoolRoom room in builder.Data.Rooms)
                {
                    bool tunnel = room.Kind == PoolRoomKind.Tunnel;
                    if (!room.Dark && !tunnel) continue;
                    Vector3 a = builder.CellCenter(room.X0, room.Z0) - new Vector3(2f, 0f, 2f);
                    Vector3 b = builder.CellCenter(room.X0 + room.Width - 1, room.Z0 + room.Height - 1) + new Vector3(2f, 0f, 2f);
                    var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
                    if (room.Dark) _darkRects.Add(rect);
                    CreateShade(rect, room.Ceiling, room.Dark ? darkRoomShade : tunnelShade);
                }

            _underwater = CreateUnderwaterVolume();
            _night = CreateNightVolume();
            if (_darkRects.Count > 0) _darkRoom = CreateDarkRoomVolume();
        }

        void Update()
        {
            if (!_prepared) Prepare();

            float rate = Time.deltaTime * 6f;
            if (_underwater != null)
                _underwater.weight = Mathf.MoveTowards(_underwater.weight, LocalUnderwater ? 1f : 0f, rate);
            _nightWeight = Mathf.MoveTowards(_nightWeight, NightTarget(), rate);
            if (_night != null) _night.weight = _nightWeight;

            Camera viewer = Camera.main;
            if (_darkRoom != null && viewer != null) _darkRoom.weight = DarkRoomWeight(viewer.transform.position);

            Animate(Application.isPlaying ? Time.time : Time.realtimeSinceStartup);
        }

        /// <summary>Para as fotos: põe todos os pesos no valor final de uma vez, vendo de <paramref name="viewer"/>.</summary>
        public void Snap(Vector3 viewer)
        {
            Prepare();
            if (_underwater != null) _underwater.weight = LocalUnderwater ? 1f : 0f;
            _nightWeight = NightTarget();
            if (_night != null) _night.weight = _nightWeight;
            if (_darkRoom != null) _darkRoom.weight = DarkRoomWeight(viewer);
            Animate(Time.realtimeSinceStartup);
        }

        float NightTarget()
        {
            if (ForceNight >= 0f) return Mathf.Clamp01(ForceNight);
            LightsOut power = LightsOut.Instance;
            return power != null && power.Phase == PowerPhase.Dark ? 1f : 0f;
        }

        float DarkRoomWeight(Vector3 viewer)
        {
            // Só escurece por dentro: a sala vizinha, do outro lado da parede, não pode apagar.
            float best = 0f;
            var point = new Vector2(viewer.x, viewer.z);
            foreach (Rect rect in _darkRects)
            {
                if (!rect.Contains(point)) continue;
                float inside = Mathf.Min(Mathf.Min(point.x - rect.xMin, rect.xMax - point.x), Mathf.Min(point.y - rect.yMin, rect.yMax - point.y));
                best = Mathf.Max(best, inside);
            }
            return Mathf.Clamp01(best / Mathf.Max(0.01f, darkRoomBlend));
        }

        void Animate(float time)
        {
            if (_block == null) return;

            if (_surface != null)
            {
                _block.Clear();
                _block.SetVector(BaseColorMapSt, Scroll(_surfaceSt, rippleSpeed, time));
                _block.SetVector(DetailMapSt, Scroll(_surfaceDetailSt, detailSpeed, time));
                _surface.SetPropertyBlock(_block);
            }

            if (_caustics.Count == 0) return;
            // A cáustica é luz do dia que passou pela água: some com o apagão.
            float strength = (1f - _nightWeight) * (LocalUnderwater ? causticUnderwaterBoost : 1f);
            Color color = _causticColor * strength;
            color.a = _causticColor.a;
            _block.Clear();
            _block.SetVector(BaseColorMapSt, Scroll(_causticSt, causticSpeed, time));
            _block.SetVector(DetailMapSt, Scroll(_causticDetailSt, causticDetailSpeed, time));
            _block.SetColor(BaseColorId, color);
            foreach (MeshRenderer renderer in _caustics)
                if (renderer != null)
                {
                    renderer.enabled = strength > 0.001f;
                    renderer.SetPropertyBlock(_block);
                }
        }

        static Vector4 Scroll(Vector4 st, Vector2 speed, float time)
        {
            return new Vector4(st.x, st.y, Mathf.Repeat(st.z + speed.x * time, 1f), Mathf.Repeat(st.w + speed.y * time, 1f));
        }

        // ---------------------------------------------------------------- Volumes

        // Névoa local de albedo preto: não clareia nada, só absorve. Vale por pixel (ao contrário
        // dos Volumes, que dependem de onde está a câmera), então escurece o que está dentro da
        // sala também para quem olha de fora. Fica na névoa volumétrica, que já está ligada.
        void CreateShade(Rect rect, float ceiling, float meanFreePath)
        {
            const float bottom = -4f;
            float top = Mathf.Max(2.5f, ceiling);
            var go = new GameObject("Sombra") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(rect.center.x, (bottom + top) * 0.5f, rect.center.y);

            var fog = go.AddComponent<LocalVolumetricFog>();
            var parameters = new LocalVolumetricFogArtistParameters(Color.black, meanFreePath, 0f)
            {
                size = new Vector3(rect.width, top - bottom, rect.height),
                // Entra devagar no primeiro metro da sala, para a borda não virar uma parede preta.
                positiveFade = new Vector3(1f / Mathf.Max(1f, rect.width), 0.02f, 1f / Mathf.Max(1f, rect.height)),
                negativeFade = new Vector3(1f / Mathf.Max(1f, rect.width), 0.02f, 1f / Mathf.Max(1f, rect.height)),
            };
            fog.parameters = parameters;
        }

        Volume CreateVolume(string name, float priority, out VolumeProfile profile)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = name;
            profile.hideFlags = HideFlags.DontSave;

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = priority;
            volume.weight = 0f;
            volume.sharedProfile = profile;
            return volume;
        }

        // Por cima do Volume da cena, abaixo do apagão (50, que tem de vencer também debaixo
        // d'água): névoa verde-água clara e a imagem puxada para o ciano.
        Volume CreateUnderwaterVolume()
        {
            Volume volume = CreateVolume("DebaixoDagua", 45f, out VolumeProfile profile);

            Fog fog = profile.Add<Fog>(true);
            fog.enabled.Override(true);
            fog.colorMode.Override(FogColorMode.ConstantColor);
            fog.color.Override(underwaterFog);
            fog.albedo.Override(new Color(0.6f, 0.95f, 0.92f));
            fog.meanFreePath.Override(Mathf.Max(1f, underwaterSight));
            fog.baseHeight.Override(SurfaceY - 20f);
            fog.maximumHeight.Override(SurfaceY + 1f);

            ColorAdjustments color = profile.Add<ColorAdjustments>(true);
            color.colorFilter.Override(new Color(0.75f, 0.97f, 0.94f));
            color.saturation.Override(-5f);
            return volume;
        }

        // "Anoitecer": acima do apagão do LightsOut (50), com o mesmo peso dele. O escuro
        // continua escuro (lanterna), mas azul-marinho como a noite das Poolrooms.
        Volume CreateNightVolume()
        {
            Volume volume = CreateVolume("Anoitecer", 51f, out VolumeProfile profile);
            GradientSky sky = profile.Add<GradientSky>(true);
            sky.top.Override(nightTop);
            sky.middle.Override(nightMiddle);
            sky.bottom.Override(nightBottom);
            return volume;
        }

        // Sala escura: a luz ambiente cai para perto de um décimo enquanto a câmera está nela.
        Volume CreateDarkRoomVolume()
        {
            Volume volume = CreateVolume("SalaEscura", 30f, out VolumeProfile profile);
            GradientSky sky = profile.Add<GradientSky>(true);
            sky.top.Override(darkRoomSky);
            sky.middle.Override(darkRoomSky);
            sky.bottom.Override(darkRoomSky);
            return volume;
        }
    }
}
