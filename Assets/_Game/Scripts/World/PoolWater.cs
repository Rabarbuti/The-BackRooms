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
    /// - o apagão vira "anoitecer" (<see cref="DaylightAt"/>): pelo relógio do <see cref="LightsOut"/>,
    ///   sem pisca nenhum, a luz das claraboias e janelas (<see cref="DaylightPanel"/>) esquenta,
    ///   cai devagar até a noite e volta devagar; o céu azul-marinho e a cáustica seguem a mesma rampa;
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

        // Malhas da maré que o LevelBuilder monta (e este componente anima, achando-as pelo nome).
        public const string WetWallsName = "MarcaMolhada";
        public const string WetFloorName = "PisoMolhado";
        public const string TideLineName = "LinhaDaMare";
        public const string TideCausticsName = "CausticasMare";

        /// <summary>Altura da faixa que a maré pode molhar, acima da água parada (m).</summary>
        public const float WetBandHeight = PoolTide.Amplitude + 0.05f;

        // O seco fica 15 cm acima da água parada: a maré passa disso e ele molha.
        static float DryFloorAbove => LevelData.DepthFloor(PoolDepth.Dry) - LevelData.WaterLevel;

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

        [Header("Maré")]
        [Tooltip("Quanto a marca molhada baixa por segundo depois que a água desce (m/s): a parede seca de cima para baixo.")]
        public float dryRate = 0.02f;
        [Tooltip("Com a maré andando no mais rápido, a ondulação rola esta vez mais depressa e o relevo cresce metade disto.")]
        public float tideChop = 3f;

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

        [Header("Anoitecer (no lugar do apagão)")]
        [Tooltip("Aviso: segundos em que a luz esquenta e baixa um pouco, antes de começar a escurecer.")]
        public float duskWarning = 10f;
        [Tooltip("Segundos em que a luz cai até a noite; termina quando começa o escuro do LightsOut.")]
        public float duskFade = 10f;
        [Tooltip("Segundos em que a luz volta, a partir do fim do escuro do LightsOut.")]
        public float dawnFade = 10f;
        [Tooltip("Quanto sobra da luz no fim do aviso (fim de tarde).")]
        [Range(0f, 1f)] public float warningLight = 0.75f;
        [Tooltip("Temperatura da luz no fim de tarde, em kelvin (a do dia vem do LevelBuilder).")]
        public float warmTemperature = 2600f;
        [Tooltip("Tinta do brilho do vidro no fim de tarde.")]
        public Color warmGlass = new Color(1f, 0.7f, 0.42f);

        /// <summary>Altura da superfície da água no mundo.</summary>
        public float SurfaceY => transform.position.y;

        /// <summary>
        /// Quanto a água está acima da altura em que foi montada, em metros (a maré sobe, a drenagem
        /// desce; <see cref="PoolTide"/>). Move a lâmina; as sombras das salas escuras, filhas dela,
        /// ficam onde estavam.
        /// </summary>
        public float Offset
        {
            get => _offset;
            set
            {
                if (float.IsNaN(_baseY)) _baseY = transform.localPosition.y;
                if (Mathf.Approximately(value, _offset)) return;
                _offset = value;
                Vector3 p = transform.localPosition;
                p.y = _baseY + value;
                transform.localPosition = p;
                for (int i = 0; i < _shades.Count; i++)
                    if (_shades[i] != null) _shades[i].localPosition = _shadeBase[i] - Vector3.up * value;
            }
        }

        /// <summary>Quanto o mapa já drenou, de 0 a 1: a cáustica some junto com a água.</summary>
        public float Drained01 { get; set; }

        /// <summary>Quanto a superfície se agita, de 0 a 1 (a maré andando; <see cref="PoolTide"/>).</summary>
        public float Agitation { get; set; }

        /// <summary>
        /// Até onde as paredes estão molhadas, em metros acima da água parada: sobe com a água e,
        /// quando ela desce, baixa devagar (<see cref="dryRate"/>). As fotos podem pôr o valor.
        /// </summary>
        public float WetTop { get; set; }

        /// <summary>
        /// Ligado pelo jogador local enquanto a câmera dele está debaixo d'água. Acende a névoa
        /// e o filtro de cor só nesta máquina.
        /// </summary>
        public bool LocalUnderwater { get; set; }

        /// <summary>
        /// Para as fotos e os testes: força o relógio do anoitecer (segundos de sessão); negativo =
        /// segue o relógio da sessão, como no jogo.
        /// </summary>
        public double ForceSeconds { get; set; } = -1.0;

        /// <summary>A luz do dia aplicada no último quadro.</summary>
        public Daylight CurrentDaylight { get; private set; } = Daylight.Day;

        static readonly int BaseColorMapSt = Shader.PropertyToID("_BaseColorMap_ST");
        static readonly int DetailMapSt = Shader.PropertyToID("_DetailMap_ST");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int NormalScaleId = Shader.PropertyToID("_NormalScale");

        bool _prepared;
        float _offset;
        float _baseY = float.NaN;
        readonly List<Transform> _shades = new List<Transform>();
        readonly List<Vector3> _shadeBase = new List<Vector3>();
        MeshRenderer _surface;
        Vector4 _surfaceSt, _surfaceDetailSt;
        readonly List<MeshRenderer> _caustics = new List<MeshRenderer>();
        Vector4 _causticSt, _causticDetailSt;
        Color _causticColor;
        MaterialPropertyBlock _block;
        float _surfaceNormalScale = 1f;
        float _rippleTime, _lastAnimate = float.NaN;
        MeshRenderer _wetWalls, _wetFloor;
        Color _wetColor;
        readonly List<MeshRenderer> _tideCaustics = new List<MeshRenderer>();

        Volume _underwater, _night, _darkRoom;
        readonly List<Rect> _darkRects = new List<Rect>();
        LightsOut _clock;

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
                _surfaceNormalScale = m.HasProperty(NormalScaleId) ? m.GetFloat(NormalScaleId) : 1f;
            }

            // A cáustica é montada pelo LevelBuilder no mesmo nível que a água.
            Transform level = transform.parent != null ? transform.parent : transform;
            foreach (MeshRenderer renderer in level.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material m = renderer.sharedMaterial;
                if (renderer.name == WetWallsName) _wetWalls = renderer;
                if (renderer.name == WetFloorName) _wetFloor = renderer;
                if (m == null || !m.name.StartsWith(CausticsMaterialName)) continue;
                // A cáustica do seco só acende com a maré por cima (AnimateTide).
                if (renderer.name == TideCausticsName)
                {
                    _tideCaustics.Add(renderer);
                    continue;
                }
                if (_caustics.Count == 0)
                {
                    _causticSt = m.HasProperty(BaseColorMapSt) ? m.GetVector(BaseColorMapSt) : new Vector4(1f, 1f, 0f, 0f);
                    _causticDetailSt = m.HasProperty(DetailMapSt) ? m.GetVector(DetailMapSt) : new Vector4(1f, 1f, 0f, 0f);
                    _causticColor = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
                }
                _caustics.Add(renderer);
            }
            Material wet = _wetWalls != null ? _wetWalls.sharedMaterial : _wetFloor != null ? _wetFloor.sharedMaterial : null;
            _wetColor = wet != null && wet.HasProperty(BaseColorId) ? wet.GetColor(BaseColorId) : new Color(0f, 0f, 0f, 0.3f);

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
            ApplyDaylight();

            Camera viewer = Camera.main;
            if (_darkRoom != null && viewer != null) _darkRoom.weight = DarkRoomWeight(viewer.transform.position);

            // A marca molhada acompanha a água subindo e, quando ela desce, seca devagar.
            WetTop = Application.isPlaying ? Mathf.Max(_offset, WetTop - dryRate * Time.deltaTime) : Mathf.Max(_offset, WetTop);
            Animate(Application.isPlaying ? Time.time : Time.realtimeSinceStartup);
        }

        /// <summary>Para as fotos: põe todos os pesos no valor final de uma vez, vendo de <paramref name="viewer"/>.</summary>
        public void Snap(Vector3 viewer)
        {
            Prepare();
            if (_underwater != null) _underwater.weight = LocalUnderwater ? 1f : 0f;
            ApplyDaylight();
            if (_darkRoom != null) _darkRoom.weight = DarkRoomWeight(viewer);
            WetTop = Mathf.Max(_offset, WetTop);
            Animate(Time.realtimeSinceStartup);
        }

        // ---------------------------------------------------------------- Anoitecer

        /// <summary>O relógio do apagão: o LightsOut da cena (fora do Play o Instance não existe).</summary>
        LightsOut Clock()
        {
            if (LightsOut.Instance != null) return LightsOut.Instance;
            if (_clock == null) _clock = FindAnyObjectByType<LightsOut>();
            return _clock;
        }

        /// <summary>Segundos de sessão que valem agora para o anoitecer (negativo = fora de partida).</summary>
        public double DaylightSeconds()
        {
            if (ForceSeconds >= 0.0) return ForceSeconds;
            LightsOut clock = Clock();
            if (!Application.isPlaying || clock == null || clock.session == null || !clock.eventEnabled) return -1.0;
            return clock.session.SessionSeconds;
        }

        // Tudo sai da mesma conta, a cada quadro: o vidro e as luzes do dia, o céu azul-marinho
        // e a cáustica. O LightsOut continua ligando o Volume de apagão dele (priority 50) no escuro,
        // de uma vez, mas ali o "Anoitecer" (51) já está com peso 1 e cobre o céu; a névoa da fase
        // usa a cor do céu, então a névoa preta do apagão não aparece.
        void ApplyDaylight()
        {
            Daylight light = DaylightAt(Clock(), DaylightSeconds());
            CurrentDaylight = light;
            if (_night != null) _night.weight = light.Night;

            for (int i = DaylightPanel.All.Count - 1; i >= 0; i--)
            {
                DaylightPanel panel = DaylightPanel.All[i];
                if (panel == null) { DaylightPanel.All.RemoveAt(i); continue; }
                panel.Apply(light, warmTemperature, warmGlass);
            }
        }

        /// <summary>
        /// A luz do dia aos <paramref name="seconds"/> de sessão, pelo ciclo do apagão do
        /// <see cref="LightsOut"/> (mesma conta, <see cref="LightsOut.PhaseAt"/>): a noite é
        /// exatamente o escuro dele. Antes, <see cref="duskWarning"/> s de aviso (a luz esquenta e
        /// cai para <see cref="warningLight"/>) e <see cref="duskFade"/> s escurecendo; depois,
        /// <see cref="dawnFade"/> s clareando, de alaranjado a branco. Função só do relógio: quem
        /// entra no meio do anoitecer vê o mesmo que os outros, e nada vai pela rede.
        /// </summary>
        public Daylight DaylightAt(LightsOut clock, double seconds)
        {
            if (clock == null || seconds < 0.0) return Daylight.Day;
            PowerPhase phase = clock.PhaseAt(seconds, out float toChange);
            if (phase == PowerPhase.Dark) return new Daylight { Sun = 0f, Warmth = 1f, Night = 1f };
            if (toChange < 0f) return Daylight.Day;

            // Indo para a noite: aviso e depois a queda.
            float untilDark = toChange;
            float fall = Ramp(1f - untilDark / Mathf.Max(0.01f, duskFade));
            float warn = Ramp(1f - (untilDark - duskFade) / Mathf.Max(0.01f, duskWarning));

            // Voltando da noite: segundos desde o fim do último escuro.
            float rise = 0f;
            if (seconds >= clock.firstDarkAt)
            {
                float since = Mathf.Max(0.1f, clock.litSeconds) - untilDark;
                rise = Ramp(1f - since / Mathf.Max(0.01f, dawnFade));
            }

            float night = Mathf.Max(fall, rise);
            float warmth = Mathf.Max(warn, rise);
            return new Daylight
            {
                Night = night,
                Warmth = warmth,
                Sun = (1f - night) * Mathf.Lerp(1f, warningLight, warmth),
            };
        }

        // Rampa suave de 0 a 1 (sem degrau nas pontas).
        static float Ramp(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
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

            // A ondulação corre mais depressa (e com mais relevo) enquanto a maré anda.
            float step = float.IsNaN(_lastAnimate) ? 0f : time - _lastAnimate;
            if (step < 0f || step > 0.5f) step = 0f;
            _lastAnimate = time;
            float chop = Mathf.Clamp01(Agitation);
            _rippleTime += step * (1f + tideChop * chop);

            if (_surface != null)
            {
                _block.Clear();
                _block.SetVector(BaseColorMapSt, Scroll(_surfaceSt, rippleSpeed, _rippleTime));
                _block.SetVector(DetailMapSt, Scroll(_surfaceDetailSt, detailSpeed, _rippleTime));
                _block.SetFloat(NormalScaleId, _surfaceNormalScale * (1f + 0.5f * tideChop * chop));
                _surface.SetPropertyBlock(_block);
            }

            AnimateTide(time);

            if (_caustics.Count == 0) return;
            // A cáustica é luz do dia que passou pela água: cai junto com ela no anoitecer.
            float strength = CurrentDaylight.Sun * (LocalUnderwater ? causticUnderwaterBoost : 1f) * (1f - Drained01);
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

        // A marca da maré: a parede molhada achatada até onde a água chegou (a malha está em Y
        // relativo à água parada, de 0 a WetBandHeight), o seco escuro e brilhante enquanto molhado
        // e a cáustica no seco só com água por cima dele.
        void AnimateTide(float time)
        {
            float wet = Mathf.Clamp(WetTop, 0f, WetBandHeight);
            if (_wetWalls != null)
            {
                bool show = wet > 0.01f;
                _wetWalls.enabled = show;
                if (show)
                {
                    Vector3 scale = _wetWalls.transform.localScale;
                    scale.y = wet / WetBandHeight;
                    _wetWalls.transform.localScale = scale;
                }
            }
            if (_wetFloor != null)
            {
                float floorWet = Mathf.Clamp01((wet - DryFloorAbove) / 0.06f);
                _wetFloor.enabled = floorWet > 0.001f;
                Color c = _wetColor;
                c.a *= floorWet;
                _block.Clear();
                _block.SetColor(BaseColorId, c);
                _wetFloor.SetPropertyBlock(_block);
            }
            if (_tideCaustics.Count > 0)
            {
                float covered = Mathf.Clamp01((_offset - DryFloorAbove - 0.03f) / 0.2f);
                float strength = CurrentDaylight.Sun * (LocalUnderwater ? causticUnderwaterBoost : 1f) * covered;
                Color color = _causticColor * strength;
                color.a = _causticColor.a;
                _block.Clear();
                _block.SetVector(BaseColorMapSt, Scroll(_causticSt, causticSpeed, time));
                _block.SetVector(DetailMapSt, Scroll(_causticDetailSt, causticDetailSpeed, time));
                _block.SetColor(BaseColorId, color);
                foreach (MeshRenderer renderer in _tideCaustics)
                    if (renderer != null)
                    {
                        renderer.enabled = strength > 0.001f;
                        renderer.SetPropertyBlock(_block);
                    }
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
            _shades.Add(go.transform);
            _shadeBase.Add(go.transform.localPosition + Vector3.up * _offset);

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

        // "Anoitecer": acima do apagão do LightsOut (50), com o peso da noite (DaylightAt). O
        // escuro continua escuro (lanterna), mas azul-marinho como a noite das Poolrooms.
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
