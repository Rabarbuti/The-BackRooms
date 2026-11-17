using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    /// <summary>
    /// Geometria do Escritório (Level 3, Docs/level3-escritorio.md), a partir da planta do
    /// <see cref="OfficeGenerator"/>: piso de placas cinza (madeira nas salas de aula), corredores
    /// de teto técnico aparente (laje escura, duto galvanizado, eletrocalha, conduítes e cano de
    /// incêndio) com luminárias tubulares penduradas, salas de forro branco, saguão e cafeteria
    /// de ripas escuras com pendentes, divisórias de vidro em esquadria branca, janelas com
    /// persiana na fachada, elevadores de inox, armários amarelos e o vão da escada com a saída
    /// (LevelBuilder.OfficeProps.cs).
    ///
    /// Tudo sai de <see cref="Data"/> e de um <see cref="System.Random"/> com a seed: cada máquina
    /// monta igual. As peças fixas viram uma malha por material; o que muda em jogo (difusor das
    /// luminárias, folhas das portas de elevador) fica em objeto próprio.
    /// </summary>
    public sealed partial class LevelBuilder
    {
        /// <summary>Materiais do Escritório. O <c>Level3Setup</c> cria e preenche.</summary>
        [Serializable]
        public sealed class OfficeLook
        {
            [Tooltip("Pintura cinza-clara das paredes.")] public Material wall;
            [Tooltip("Placas grandes de porcelanato cinza (corredor, saguão, cafeteria).")] public Material floorTile;
            [Tooltip("Piso de madeira das salas de aula.")] public Material floorWood;
            [Tooltip("Laje escura do teto técnico.")] public Material slab;
            [Tooltip("Forro branco das salas.")] public Material ceilingTile;
            [Tooltip("Ripas escuras do saguão e da cafeteria.")] public Material slats;
            public Material baseboard;
            [Tooltip("Inox escovado: elevador, corrimão, guarda-corpo.")] public Material steel;
            [Tooltip("Aço galvanizado do duto e da eletrocalha.")] public Material galvanized;
            [Tooltip("Cano de incêndio.")] public Material pipeRed;
            [Tooltip("Esquadria, portas e mesas brancas.")] public Material frameWhite;
            [Tooltip("Vidro fosco das divisórias.")] public Material glass;
            [Tooltip("Vidro da janela com persiana (emissivo: a luz de fora).")] public Material windowDay;
            public Material lockerYellow;
            public Material lockerBody;
            [Tooltip("Grafite: viga da escada, quadros, grelhas.")] public Material darkMetal;
            [Tooltip("Plástico preto: cadeiras, telas, pendentes.")] public Material blackPlastic;
            [Tooltip("Granito cinza: degraus e piso da cabine.")] public Material granite;
            [Tooltip("OSB dessaturado: gabinetes e balcão.")] public Material osb;
            [Tooltip("Tecido cinza: sofá e pufes.")] public Material fabric;
            public Material plant;
            [Tooltip("Concreto: borda das lajes, vasos.")] public Material concrete;
            public Material tubeOn;
            public Material tubeOff;
            [Tooltip("Luminária embutida das salas.")] public Material panelOn;
            public Material panelOff;
            [Tooltip("Pendente do saguão, por dentro.")] public Material domeOn;
            public Material domeOff;
            [Tooltip("Placas de saída: atlas de 4 quadros (seta para frente, esquerda, direita, sem seta).")] public Material signGreen;
            [Tooltip("Painel de senha, LEDs.")] public Material ledRed;
            public Material ledGreen;
            public Material screenOff;
            public Material whiteboard;
            public Material buttonAmber;
            [Tooltip("Claraboia no alto do vão da escada (sem luz).")] public Material skylight;
            [Tooltip("Preto do fundo do vão e dos andares de baixo.")] public Material voidBlack;
            [Tooltip("Vidro da geladeira, aceso.")] public Material fridgeGlass;
            public Material exitDoor;
        }

        [Header("Escritório (Level 3)")]
        [Tooltip("Monta o Escritório (OfficeGenerator) em vez do labirinto do Level 0.")]
        public bool officeEnabled;
        public OfficeLook office = new OfficeLook();
        [Tooltip("Luminária tubular do corredor, em candela.")] public float officeTubeCandela = 1900f;
        [Tooltip("Luminária embutida das salas, em candela.")] public float officeRoomCandela = 1700f;
        [Tooltip("Pendente do saguão, da cafeteria e da área de estar, em candela.")] public float officePendantCandela = 1500f;
        [Tooltip("Luz da janela entrando na sala, em candela.")] public float officeWindowCandela = 600f;
        [Tooltip("Spots da cabine do elevador, em candela.")] public float officeCabCandela = 260f;
        [Tooltip("Luz do alto do vão da escada, em candela.")] public float officeAtriumCandela = 60000f;

        /// <summary>Verdadeiro enquanto o Escritório está montado: os passos tocam o piso duro.</summary>
        public static bool HardFloorActive { get; private set; }

        /// <summary>
        /// Pontos (no mundo) do caminho da entrada da escada até a porta da saída: patamar de cima,
        /// pé da escada e frente da porta. O teste em Play anda por eles depois do caminho de células.
        /// </summary>
        public List<Vector3> OfficeExitRoute { get; } = new List<Vector3>();

        // Alturas, em metros acima do piso.
        public const float OfficeSlab = 3.9f;
        public const float OfficeRoomCeiling = 2.9f;
        const float OfficeSlatsBottom = 3.3f;
        const float OfficeCabCeiling = 2.5f;
        const float OfficeTubeHeight = 2.78f;
        const float OfficeWallThickness = 0.15f;
        const float OfficeDoorHalf = 0.5f;
        const float OfficeDoorTop = 2.1f;
        const float OfficeGlassTop = 2.75f;
        const float ElevatorHalf = 0.55f;
        const float ElevatorTop = 2.2f;
        /// <summary>Meia largura da cabine por dentro: cabe o ponto de início dos quatro jogadores.</summary>
        const float CabHalf = 1.3f;

        sealed class OfficeKit
        {
            public readonly Dictionary<Material, MeshBuilder> Meshes = new Dictionary<Material, MeshBuilder>();
            public GameObject Colliders;
            public Transform Fixtures;
            public Transform Moving;
            public System.Random Rng;
            public int ShadowResolution = 128;
            public readonly Dictionary<int, CeilingLight> CorridorLights = new Dictionary<int, CeilingLight>();
            public readonly List<ElevatorDoors> Elevators = new List<ElevatorDoors>();
            public Mesh TubeMesh, PanelMesh, DomeMesh, SpotsMesh, LeafMesh;
        }

        OfficeKit _ok;

        void BuildOffice()
        {
            HardFloorActive = true;
            OfficeExitRoute.Clear();

            _ok = new OfficeKit
            {
                Colliders = CreateChild("Colisores"),
                Fixtures = CreateChild("Luminarias").transform,
                Moving = CreateChild("Elevadores").transform,
                Rng = new System.Random(unchecked(seed * 7 + 3001))
            };

            int lit = 0;
            foreach (LightState state in Data.Lights)
                if (state == LightState.On || state == LightState.Flicker) lit++;
            _ok.ShadowResolution = OfficeShadowResolution(lit);

            _ok.TubeMesh = BoxMesh("Tubo", new Vector3(2.4f, 0.075f, 0.075f));
            _ok.LeafMesh = BoxMesh("FolhaDoElevador", new Vector3(0.56f, ElevatorTop, 0.04f));
            _ok.PanelMesh = RecessedPanelMesh();
            _ok.DomeMesh = DiscMesh("Pendente", 0.28f);
            _ok.SpotsMesh = CabSpotsMesh();

            BuildOfficeFloors();
            BuildOfficeCeilings();
            BuildOfficeEdges();
            BuildOfficeLights();
            BuildOfficeLockers();
            BuildOfficeCorridorDetails();
            BuildOfficeRooms();
            BuildOfficeElevators();
            BuildOfficeAtrium();
            BuildOfficeSigns();
            FlushOffice();
            BuildOfficeEvents();
            _ok = null;
        }

        // ---------------------------------------------------------------- Ajudantes de malha

        MeshBuilder OM(Material material)
        {
            if (material == null) material = office.wall != null ? office.wall : wallMaterial;
            if (!_ok.Meshes.TryGetValue(material, out MeshBuilder mb))
            {
                mb = new MeshBuilder(OfficeTile(material));
                _ok.Meshes[material] = mb;
            }
            return mb;
        }

        // Metros por repetição da textura de cada material (o que não tem textura não importa).
        float OfficeTile(Material m)
        {
            if (m == office.floorTile) return 1.8f;
            if (m == office.floorWood) return 2.4f;
            if (m == office.ceilingTile) return 1.25f;
            if (m == office.wall || m == office.slab || m == office.concrete) return 2f;
            if (m == office.osb) return 1.2f;
            return 1f;
        }

        void OBox(Material m, Vector3 center, Vector3 size, bool caps = true)
        {
            OM(m).AddBox(center, size, caps);
        }

        void OSolid(Material m, Vector3 center, Vector3 size, bool caps = true)
        {
            OM(m).AddBox(center, size, caps);
            AddCollider(_ok.Colliders, center, size);
        }

        void OTurned(Material m, Vector3 center, Vector3 size, Quaternion rotation, bool collider, bool caps = true)
        {
            AddOrientedBox(OM(m), center, size, rotation, caps);
            if (!collider) return;
            var go = new GameObject("Colisor");
            go.transform.SetParent(_ok.Colliders.transform, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rotation;
            go.AddComponent<BoxCollider>().size = size;
        }

        static void AddOrientedBox(MeshBuilder mb, Vector3 c, Vector3 s, Quaternion r, bool caps)
        {
            Vector3 hx = r * new Vector3(s.x * 0.5f, 0f, 0f);
            Vector3 hy = r * new Vector3(0f, s.y * 0.5f, 0f);
            Vector3 hz = r * new Vector3(0f, 0f, s.z * 0.5f);
            Vector3 nx = r * Vector3.right, ny = r * Vector3.up, nz = r * Vector3.forward;

            mb.AddQuad(c + hx - hy - hz, c + hx + hy - hz, c + hx + hy + hz, c + hx - hy + hz, nx);
            mb.AddQuad(c - hx - hy - hz, c - hx + hy - hz, c - hx + hy + hz, c - hx - hy + hz, -nx);
            mb.AddQuad(c - hx - hy + hz, c - hx + hy + hz, c + hx + hy + hz, c + hx - hy + hz, nz);
            mb.AddQuad(c - hx - hy - hz, c - hx + hy - hz, c + hx + hy - hz, c + hx - hy - hz, -nz);
            if (!caps) return;
            mb.AddQuad(c - hx + hy - hz, c - hx + hy + hz, c + hx + hy + hz, c + hx + hy - hz, ny);
            mb.AddQuad(c - hx - hy - hz, c - hx - hy + hz, c + hx - hy + hz, c + hx - hy - hz, -ny);
        }

        /// <summary>
        /// Caixa no referencial de uma divisa: <paramref name="along"/> é o eixo da divisa e
        /// <paramref name="across"/> vai da célula A para a B (os dois unitários e positivos ou
        /// negativos, tanto faz: só o valor absoluto entra no tamanho).
        /// </summary>
        void OEdgeBox(Material m, Vector3 origin, Vector3 along, Vector3 across, float a0, float a1, float d0, float d1,
                      float y0, float y1, bool collider, bool caps = true)
        {
            if (a1 < a0) (a0, a1) = (a1, a0);
            if (d1 < d0) (d0, d1) = (d1, d0);
            if (a1 - a0 < 1e-4f || d1 - d0 < 1e-4f || y1 - y0 < 1e-4f) return;

            Vector3 center = origin + along * ((a0 + a1) * 0.5f) + across * ((d0 + d1) * 0.5f) + Vector3.up * ((y0 + y1) * 0.5f);
            Vector3 size = Abs(along) * (a1 - a0) + Abs(across) * (d1 - d0) + Vector3.up * (y1 - y0);
            OM(m).AddBox(center, size, caps);
            if (collider) AddCollider(_ok.Colliders, center, size);
        }

        void OTube(Material m, Vector3 from, Vector3 to, float radius, int sides = 8)
        {
            OM(m).AddTube(from, to, radius, sides);
        }

        void OCylinder(Material m, Vector3 bottom, float height, float radius, int sides = 16, bool collider = false)
        {
            MeshBuilder mb = OM(m);
            mb.AddTube(bottom, bottom + Vector3.up * height, radius, sides);
            mb.AddDisc(bottom + Vector3.up * height, Vector3.up, Vector3.right, radius, sides);
            if (collider) AddCollider(_ok.Colliders, bottom + Vector3.up * (height * 0.5f), new Vector3(radius * 1.6f, height, radius * 1.6f));
        }

        static Mesh BoxMesh(string meshName, Vector3 size)
        {
            var mb = new MeshBuilder(1f);
            mb.AddBox(Vector3.zero, size, caps: true);
            return mb.ToMesh(meshName);
        }

        static Mesh DiscMesh(string meshName, float radius)
        {
            var mb = new MeshBuilder(1f);
            mb.AddDisc(Vector3.zero, Vector3.down, Vector3.right, radius, 20);
            return mb.ToMesh(meshName);
        }

        // Luminária embutida das salas: duas linhas de 1,2 m x 0,14 m, a 1,6 m uma da outra.
        static Mesh RecessedPanelMesh()
        {
            var mb = new MeshBuilder(1f);
            foreach (float z in new[] { -0.8f, 0.8f })
                mb.AddQuad(new Vector3(-0.6f, 0f, z - 0.07f), new Vector3(-0.6f, 0f, z + 0.07f),
                           new Vector3(0.6f, 0f, z + 0.07f), new Vector3(0.6f, 0f, z - 0.07f), Vector3.down);
            return mb.ToMesh("LuminariaEmbutida");
        }

        // Quatro spots redondos no teto da cabine.
        static Mesh CabSpotsMesh()
        {
            var mb = new MeshBuilder(1f);
            foreach (float x in new[] { -0.6f, 0.6f })
                foreach (float z in new[] { -0.6f, 0.6f })
                    mb.AddDisc(new Vector3(x, 0f, z), Vector3.down, Vector3.right, 0.07f, 16);
            return mb.ToMesh("SpotsDaCabine");
        }

        void FlushOffice()
        {
            var parent = CreateChild("Escritorio").transform;
            int index = 0;
            foreach (KeyValuePair<Material, MeshBuilder> pair in _ok.Meshes)
            {
                if (pair.Value.IsEmpty) continue;
                Material m = pair.Key;
                bool flat = m == office.floorTile || m == office.floorWood || m == office.slab || m == office.ceilingTile ||
                            m == office.granite || m == office.skylight || m == office.windowDay || m == office.voidBlack ||
                            m == office.signGreen || m == office.ledRed || m == office.ledGreen || m == office.tubeOn ||
                            m == office.fridgeGlass || m == office.glass;
                string name = $"{index:00}_{(m != null ? m.name : "sem_material")}";
                CreateRenderer(name, pair.Value.ToMesh(name), m, flat ? ShadowCastingMode.Off : ShadowCastingMode.On, parent);
                index++;
            }
        }

        // Spot = uma face de sombra. Escolhe a maior resolução que cabe no atlas em cache.
        int OfficeShadowResolution(int lightCount)
        {
            int atlas = 2048;
            if (GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset asset)
                atlas = asset.currentPlatformRenderPipelineSettings.hdShadowInitParams.cachedPunctualLightShadowAtlas;
            long budget = (long)atlas * atlas * 6 / 10;
            int resolution = Mathf.Max(64, Mathf.ClosestPowerOfTwo(maxShadowResolution));
            while (resolution > 64 && (long)resolution * resolution * Mathf.Max(1, lightCount) > budget) resolution /= 2;
            return resolution;
        }

        HDAdditionalLightData OfficeSpot(Transform parent, Vector3 localPosition, Quaternion localRotation, float candela, float range,
                                         float angle, float temperature, bool shadows)
        {
            HDAdditionalLightData hd = CreateLightObject("Luz", parent, LightType.Spot, localPosition, out Light source);
            source.transform.localRotation = localRotation;
            source.spotAngle = angle;
            source.innerSpotAngle = angle * 0.55f;
            source.intensity = candela;
            source.range = range;
            source.colorTemperature = temperature;
            hd.fadeDistance = lightFadeDistance;
            hd.shadowFadeDistance = lightFadeDistance * 1.2f;
            hd.affectsVolumetric = lampsLightFog;

            if (shadows && ceilingShadows)
            {
                source.shadows = LightShadows.Soft;
                hd.shadowUpdateMode = ShadowUpdateMode.OnEnable;
                hd.SetShadowResolutionOverride(true);
                hd.SetShadowResolution(_ok.ShadowResolution);
            }
            else
            {
                source.shadows = LightShadows.None;
            }
            return hd;
        }

        Vector3 OCellCenter(int x, int z) => new Vector3((x + 0.5f) * Cell, 0f, (z + 0.5f) * Cell);

        static Vector3 SideVector(WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return Vector3.back;
                case WallSide.North: return Vector3.forward;
                case WallSide.West: return Vector3.left;
                default: return Vector3.right;
            }
        }

        OfficeCell KindAt(int x, int z)
        {
            if (x < 0 || z < 0 || x >= Data.Width || z >= Data.Height) return OfficeCell.Solid;
            return Data.Office[x, z];
        }

        bool CorridorLink(int x, int z, WallSide side)
        {
            return Data.Neighbor(x, z, side, out int nx, out int nz) && Data.Office[nx, nz] == OfficeCell.Corridor &&
                   Data.OfficeEdgeAt(x, z, side) == OfficeEdge.Open;
        }

        // ---------------------------------------------------------------- Piso e teto

        void BuildOfficeFloors()
        {
            for (int z = 0; z < Data.Height; z++)
            {
                int runStart = -1;
                for (int x = 0; x <= Data.Width; x++)
                {
                    bool solid = x < Data.Width && Data.Office[x, z] != OfficeCell.Atrium;
                    if (solid)
                    {
                        OfficeCell kind = Data.Office[x, z];
                        Material m = kind == OfficeCell.Classroom ? office.floorWood
                                   : kind == OfficeCell.Cab || kind == OfficeCell.Shaft ? office.granite
                                   : office.floorTile;
                        float fx = x * Cell, fz = z * Cell;
                        OM(m).AddQuad(new Vector3(fx, 0f, fz), new Vector3(fx, 0f, fz + Cell), new Vector3(fx + Cell, 0f, fz + Cell),
                                      new Vector3(fx + Cell, 0f, fz), Vector3.up);
                        if (runStart < 0) runStart = x;
                        continue;
                    }

                    if (runStart < 0) continue;
                    float length = (x - runStart) * Cell;
                    AddCollider(_ok.Colliders, new Vector3(runStart * Cell + length * 0.5f, -0.25f, (z + 0.5f) * Cell), new Vector3(length, 0.5f, Cell));
                    runStart = -1;
                }
            }
        }

        void BuildOfficeCeilings()
        {
            for (int z = 0; z < Data.Height; z++)
            {
                int runStart = -1;
                for (int x = 0; x <= Data.Width; x++)
                {
                    bool covered = x < Data.Width && Data.Office[x, z] != OfficeCell.Atrium;
                    if (covered)
                    {
                        float fx = x * Cell, fz = z * Cell;
                        OM(office.slab).AddQuad(new Vector3(fx, OfficeSlab, fz), new Vector3(fx, OfficeSlab, fz + Cell),
                                                new Vector3(fx + Cell, OfficeSlab, fz + Cell), new Vector3(fx + Cell, OfficeSlab, fz), Vector3.down);
                        if (runStart < 0) runStart = x;
                        CellCeiling(x, z);
                        continue;
                    }

                    if (runStart < 0) continue;
                    float length = (x - runStart) * Cell;
                    AddCollider(_ok.Colliders, new Vector3(runStart * Cell + length * 0.5f, OfficeSlab + 0.25f, (z + 0.5f) * Cell), new Vector3(length, 0.5f, Cell));
                    runStart = -1;
                }
            }
        }

        void CellCeiling(int x, int z)
        {
            Vector3 c = OCellCenter(x, z);
            switch (Data.Office[x, z])
            {
                case OfficeCell.Classroom:
                case OfficeCell.Small:
                {
                    // Forro branco a 2,9 m, com uma grelha de ar-condicionado aqui e ali.
                    float h = OfficeRoomCeiling;
                    OM(office.ceilingTile).AddQuad(new Vector3(c.x - 2f, h, c.z - 2f), new Vector3(c.x - 2f, h, c.z + 2f),
                                                   new Vector3(c.x + 2f, h, c.z + 2f), new Vector3(c.x + 2f, h, c.z - 2f), Vector3.down);
                    AddCollider(_ok.Colliders, c + Vector3.up * (h + 0.05f), new Vector3(Cell, 0.1f, Cell));
                    if (_ok.Rng.NextDouble() < 0.4)
                    {
                        float gx = c.x + (_ok.Rng.Next(2) == 0 ? -1.2f : 1.2f);
                        OBox(office.darkMetal, new Vector3(gx, h - 0.01f, c.z + 1.2f), new Vector3(0.6f, 0.02f, 0.6f));
                    }
                    break;
                }

                case OfficeCell.Lobby:
                case OfficeCell.Cafe:
                case OfficeCell.Lounge:
                    // Teto de ripas escuras, atravessadas no eixo Z; a laje escura aparece entre elas.
                    for (int i = 0; i < 20; i++)
                    {
                        float sx = c.x - 1.9f + i * 0.2f;
                        OBox(office.slats, new Vector3(sx, OfficeSlatsBottom + 0.125f, c.z), new Vector3(0.05f, 0.25f, Cell), caps: true);
                    }
                    break;

                case OfficeCell.Corridor:
                    TechnicalCeiling(x, z, c);
                    break;
            }
        }

        // Teto técnico do corredor: em cada célula, do meio até cada lado onde o corredor continua,
        // duto galvanizado, eletrocalha, três conduítes e o cano de incêndio.
        void TechnicalCeiling(int x, int z, Vector3 c)
        {
            foreach (WallSide side in new[] { WallSide.East, WallSide.West, WallSide.North, WallSide.South })
            {
                if (!CorridorLink(x, z, side)) continue;

                Vector3 dir = SideVector(side);
                Vector3 across = Mathf.Abs(dir.x) > 0.5f ? Vector3.forward : Vector3.right;
                Vector3 mid = c + dir * 1f;
                Vector3 alongSize = Abs(dir) * 2f;

                OBox(office.galvanized, mid + across * 0.9f + Vector3.up * 3.55f, alongSize + across * 0.8f + Vector3.up * 0.4f, caps: true);
                OBox(office.galvanized, mid - across * 0.8f + Vector3.up * 3.15f, alongSize + across * 0.35f + Vector3.up * 0.06f, caps: true);
                for (int k = 0; k < 3; k++)
                {
                    Vector3 offset = -across * (1.25f + k * 0.08f) + Vector3.up * 3.48f;
                    OTube(office.galvanized, c + offset, c + dir * 2f + offset, 0.018f, 6);
                }
                Vector3 pipe = across * 1.55f + Vector3.up * 3.62f;
                OTube(office.pipeRed, c + pipe, c + dir * 2f + pipe, 0.055f, 10);

                // Tirantes da eletrocalha.
                Vector3 rod = c + dir * 1f - across * 0.8f;
                OBox(office.galvanized, rod + across * 0.15f + Vector3.up * ((3.18f + OfficeSlab) * 0.5f), new Vector3(0.012f, OfficeSlab - 3.18f, 0.012f));
                OBox(office.galvanized, rod - across * 0.15f + Vector3.up * ((3.18f + OfficeSlab) * 0.5f), new Vector3(0.012f, OfficeSlab - 3.18f, 0.012f));
            }
        }

        // ---------------------------------------------------------------- Divisas

        void BuildOfficeEdges()
        {
            int w = Data.Width, h = Data.Height;
            for (int z = 0; z <= h; z++)
                for (int x = 0; x < w; x++)
                    OfficeEdgePiece(Data.OfficeSouth[x, z], new Vector3((x + 0.5f) * Cell, 0f, z * Cell), Vector3.right, Vector3.forward,
                                    z > 0 ? KindAt(x, z - 1) : OfficeCell.Solid, z < h ? KindAt(x, z) : OfficeCell.Solid, z > 0, z < h);

            for (int x = 0; x <= w; x++)
                for (int z = 0; z < h; z++)
                    OfficeEdgePiece(Data.OfficeWest[x, z], new Vector3(x * Cell, 0f, (z + 0.5f) * Cell), Vector3.forward, Vector3.right,
                                    x > 0 ? KindAt(x - 1, z) : OfficeCell.Solid, x < w ? KindAt(x, z) : OfficeCell.Solid, x > 0, x < w);
        }

        static bool IsRoomKind(OfficeCell kind) => kind == OfficeCell.Classroom || kind == OfficeCell.Small;

        void OfficeEdgePiece(OfficeEdge edge, Vector3 o, Vector3 along, Vector3 across, OfficeCell a, OfficeCell b, bool hasA, bool hasB)
        {
            const float T = OfficeWallThickness;
            float half = Cell * 0.5f + T * 0.5f;
            float wallTop = OfficeSlab;

            switch (edge)
            {
                case OfficeEdge.Open:
                case OfficeEdge.StairTop:
                    return;

                case OfficeEdge.Wall:
                    OEdgeBox(office.wall, o, along, across, -half, half, -T * 0.5f, T * 0.5f, 0f, wallTop, true);
                    Baseboards(o, along, across, -half, half, hasA && a != OfficeCell.Atrium, hasB && b != OfficeCell.Atrium);
                    return;

                case OfficeEdge.Door:
                {
                    float roomSide = IsRoomKind(b) ? 1f : -1f;
                    OEdgeBox(office.wall, o, along, across, -half, -OfficeDoorHalf, -T * 0.5f, T * 0.5f, 0f, wallTop, true);
                    OEdgeBox(office.wall, o, along, across, OfficeDoorHalf, half, -T * 0.5f, T * 0.5f, 0f, wallTop, true);
                    OEdgeBox(office.wall, o, along, across, -OfficeDoorHalf, OfficeDoorHalf, -T * 0.5f, T * 0.5f, OfficeDoorTop, wallTop, true);
                    DoorFrame(o, along, across, T);
                    Baseboards(o, along, across, -half, -OfficeDoorHalf - 0.06f, true, true);
                    Baseboards(o, along, across, OfficeDoorHalf + 0.06f, half, true, true);
                    OpenLeaf(o, along, across, roomSide, office.frameWhite, false);
                    return;
                }

                case OfficeEdge.Glass:
                case OfficeEdge.GlassDoor:
                {
                    bool door = edge == OfficeEdge.GlassDoor;
                    float roomSide = IsRoomKind(b) ? 1f : -1f;
                    OEdgeBox(office.wall, o, along, across, -half, half, -T * 0.5f, T * 0.5f, OfficeGlassTop, wallTop, true);
                    OEdgeBox(office.frameWhite, o, along, across, -2f, 2f, -0.045f, 0.045f, OfficeGlassTop - 0.08f, OfficeGlassTop, false);

                    if (door)
                    {
                        OEdgeBox(office.frameWhite, o, along, across, -2f, -OfficeDoorHalf, -0.045f, 0.045f, 0f, 0.08f, false);
                        OEdgeBox(office.frameWhite, o, along, across, OfficeDoorHalf, 2f, -0.045f, 0.045f, 0f, 0.08f, false);
                        OEdgeBox(office.glass, o, along, across, -2f, -OfficeDoorHalf, -0.01f, 0.01f, 0.08f, OfficeGlassTop - 0.08f, false);
                        OEdgeBox(office.glass, o, along, across, OfficeDoorHalf, 2f, -0.01f, 0.01f, 0.08f, OfficeGlassTop - 0.08f, false);
                        OEdgeBox(office.glass, o, along, across, -OfficeDoorHalf, OfficeDoorHalf, -0.01f, 0.01f, OfficeDoorTop + 0.04f, OfficeGlassTop - 0.08f, false);
                        OEdgeBox(office.frameWhite, o, along, across, -OfficeDoorHalf, OfficeDoorHalf, -0.045f, 0.045f, OfficeDoorTop, OfficeDoorTop + 0.05f, false);
                        foreach (float post in new[] { -2f, -1.25f, -OfficeDoorHalf, OfficeDoorHalf, 1.25f, 2f })
                            OEdgeBox(office.frameWhite, o, along, across, post - 0.025f, post + 0.025f, -0.045f, 0.045f, 0.08f, OfficeGlassTop - 0.08f, false);

                        AddEdgeCollider(o, along, across, -half, -OfficeDoorHalf, 0f, OfficeGlassTop);
                        AddEdgeCollider(o, along, across, OfficeDoorHalf, half, 0f, OfficeGlassTop);
                        AddEdgeCollider(o, along, across, -OfficeDoorHalf, OfficeDoorHalf, OfficeDoorTop, OfficeGlassTop);
                        OpenLeaf(o, along, across, roomSide, office.glass, true);
                    }
                    else
                    {
                        OEdgeBox(office.frameWhite, o, along, across, -2f, 2f, -0.045f, 0.045f, 0f, 0.08f, false);
                        OEdgeBox(office.glass, o, along, across, -2f, 2f, -0.01f, 0.01f, 0.08f, OfficeGlassTop - 0.08f, false);
                        foreach (float post in new[] { -2f, -1f, 0f, 1f, 2f })
                            OEdgeBox(office.frameWhite, o, along, across, post - 0.025f, post + 0.025f, -0.045f, 0.045f, 0.08f, OfficeGlassTop - 0.08f, false);
                        AddEdgeCollider(o, along, across, -half, half, 0f, OfficeGlassTop);
                    }
                    return;
                }

                case OfficeEdge.Window:
                {
                    // A borda do mapa: só um dos lados existe, e é para ele que a janela olha.
                    float inside = hasB ? 1f : -1f;
                    OfficeCell room = hasB ? b : a;
                    const float sill = 0.9f, top = 2.6f, pane = 1.8f;
                    OEdgeBox(office.wall, o, along, across, -half, half, -T * 0.5f, T * 0.5f, 0f, sill, true);
                    OEdgeBox(office.wall, o, along, across, -half, half, -T * 0.5f, T * 0.5f, top, wallTop, true);
                    OEdgeBox(office.wall, o, along, across, -half, -pane, -T * 0.5f, T * 0.5f, sill, top, true);
                    OEdgeBox(office.wall, o, along, across, pane, half, -T * 0.5f, T * 0.5f, sill, top, true);
                    OEdgeBox(office.windowDay, o, along, across, -pane, pane, -0.01f, 0.01f, sill, top, true);
                    OEdgeBox(office.frameWhite, o, along, across, -0.03f, 0.03f, -0.04f, 0.04f, sill, top, false);
                    OEdgeBox(office.frameWhite, o, along, across, -pane - 0.03f, pane + 0.03f, inside * T * 0.5f, inside * 0.3f, sill - 0.03f, sill + 0.01f, false);
                    Baseboards(o, along, across, -half, half, !hasB, hasB);

                    if (room != OfficeCell.Atrium)
                    {
                        // A luz de fora entrando na sala, sem sombra (não atravessa nada além do vidro).
                        Vector3 into = across * inside;
                        var holder = new GameObject("Janela").transform;
                        holder.SetParent(_ok.Fixtures, false);
                        holder.localPosition = o + into * 0.35f + Vector3.up * 2.35f;
                        OfficeSpot(holder, Vector3.zero, Quaternion.LookRotation((into * 0.75f + Vector3.down * 0.65f).normalized),
                                   officeWindowCandela, 7.5f, 110f, 7000f, false);
                    }
                    return;
                }

                case OfficeEdge.ElevatorDoor:
                case OfficeEdge.CabDoor:
                {
                    // Parede do saguão com o vão da porta de elevador; as folhas e a cabine saem em
                    // BuildOfficeElevators. O portal de inox fica do lado do saguão.
                    float lobby = b == OfficeCell.Lobby ? 1f : -1f;
                    OEdgeBox(office.wall, o, along, across, -half, -ElevatorHalf, -T * 0.5f, T * 0.5f, 0f, wallTop, true);
                    OEdgeBox(office.wall, o, along, across, ElevatorHalf, half, -T * 0.5f, T * 0.5f, 0f, wallTop, true);
                    OEdgeBox(office.wall, o, along, across, -ElevatorHalf, ElevatorHalf, -T * 0.5f, T * 0.5f, ElevatorTop, wallTop, true);
                    float face = lobby * T * 0.5f;
                    OEdgeBox(office.steel, o, along, across, -0.72f, -ElevatorHalf, face, face + lobby * 0.025f, 0f, ElevatorTop + 0.15f, false);
                    OEdgeBox(office.steel, o, along, across, ElevatorHalf, 0.72f, face, face + lobby * 0.025f, 0f, ElevatorTop + 0.15f, false);
                    OEdgeBox(office.steel, o, along, across, -0.72f, 0.72f, face, face + lobby * 0.025f, ElevatorTop, ElevatorTop + 0.15f, false);
                    // Botão de chamada, com a luz âmbar.
                    OEdgeBox(office.steel, o, along, across, 0.92f, 1.04f, face, face + lobby * 0.015f, 1.0f, 1.25f, false);
                    OEdgeBox(office.buttonAmber, o, along, across, 0.96f, 1.0f, face, face + lobby * 0.022f, 1.1f, 1.14f, false);
                    // Laterais de inox do vão até a cabine (0,7 m de fundo), do lado do poço.
                    float shaft = -lobby;
                    float depth = Cell * 0.5f - CabHalf;
                    OEdgeBox(office.steel, o, along, across, -ElevatorHalf - 0.05f, -ElevatorHalf, shaft * T * 0.5f, shaft * (depth + 0.05f), 0f, ElevatorTop + 0.3f, true);
                    OEdgeBox(office.steel, o, along, across, ElevatorHalf, ElevatorHalf + 0.05f, shaft * T * 0.5f, shaft * (depth + 0.05f), 0f, ElevatorTop + 0.3f, true);
                    OEdgeBox(office.steel, o, along, across, -ElevatorHalf, ElevatorHalf, shaft * T * 0.5f, shaft * (depth + 0.05f), ElevatorTop, ElevatorTop + 0.3f, true);
                    Baseboards(o, along, across, -half, -0.75f, lobby < 0f, lobby > 0f);
                    Baseboards(o, along, across, 0.75f, half, lobby < 0f, lobby > 0f);
                    return;
                }

                case OfficeEdge.Railing:
                {
                    float hole = b == OfficeCell.Atrium ? 1f : -1f;
                    RailingRun(o, along, across, -2f, 2f, hole);
                    return;
                }
            }
        }

        void AddEdgeCollider(Vector3 o, Vector3 along, Vector3 across, float a0, float a1, float y0, float y1)
        {
            const float T = OfficeWallThickness;
            Vector3 center = o + along * ((a0 + a1) * 0.5f) + Vector3.up * ((y0 + y1) * 0.5f);
            Vector3 size = Abs(along) * Mathf.Abs(a1 - a0) + Abs(across) * T + Vector3.up * (y1 - y0);
            AddCollider(_ok.Colliders, center, size);
        }

        void Baseboards(Vector3 o, Vector3 along, Vector3 across, float a0, float a1, bool sideA, bool sideB)
        {
            const float T = OfficeWallThickness;
            if (sideA) OEdgeBox(office.baseboard, o, along, across, a0, a1, -T * 0.5f - 0.012f, -T * 0.5f, 0f, 0.09f, false);
            if (sideB) OEdgeBox(office.baseboard, o, along, across, a0, a1, T * 0.5f, T * 0.5f + 0.012f, 0f, 0.09f, false);
        }

        void DoorFrame(Vector3 o, Vector3 along, Vector3 across, float thickness)
        {
            float d = thickness * 0.5f + 0.02f;
            OEdgeBox(office.frameWhite, o, along, across, -OfficeDoorHalf - 0.06f, -OfficeDoorHalf, -d, d, 0f, OfficeDoorTop + 0.06f, false);
            OEdgeBox(office.frameWhite, o, along, across, OfficeDoorHalf, OfficeDoorHalf + 0.06f, -d, d, 0f, OfficeDoorTop + 0.06f, false);
            OEdgeBox(office.frameWhite, o, along, across, -OfficeDoorHalf - 0.06f, OfficeDoorHalf + 0.06f, -d, d, OfficeDoorTop, OfficeDoorTop + 0.06f, false);
        }

        // Folha aberta até encostar na parede de dentro da sala (180°): não fecha passagem nenhuma.
        void OpenLeaf(Vector3 o, Vector3 along, Vector3 across, float roomSide, Material material, bool glass)
        {
            float hinge = _ok.Rng.Next(2) == 0 ? 1f : -1f;
            float a0 = hinge * (OfficeDoorHalf + 0.03f), a1 = hinge * (OfficeDoorHalf + 0.97f);
            float d0 = roomSide * (OfficeWallThickness * 0.5f + 0.015f), d1 = roomSide * (OfficeWallThickness * 0.5f + 0.055f);
            if (glass)
            {
                OEdgeBox(office.frameWhite, o, along, across, a0, a1, d0, d1, 0.01f, 0.09f, false);
                OEdgeBox(office.frameWhite, o, along, across, a0, a1, d0, d1, 1.99f, 2.07f, false);
                OEdgeBox(office.glass, o, along, across, a0, a1, d0, d1, 0.09f, 1.99f, false);
                AddCollider(_ok.Colliders, o + along * ((a0 + a1) * 0.5f) + across * ((d0 + d1) * 0.5f) + Vector3.up * 1.04f,
                            Abs(along) * Mathf.Abs(a1 - a0) + Abs(across) * 0.04f + Vector3.up * 2.06f);
            }
            else
            {
                OEdgeBox(material, o, along, across, a0, a1, d0, d1, 0.01f, 2.07f, true);
                // Maçaneta, do lado de fora da folha aberta.
                float knob = hinge * (OfficeDoorHalf + 0.88f);
                OEdgeBox(office.steel, o, along, across, knob - 0.06f, knob + 0.06f, d1, d1 + roomSide * 0.05f, 1.0f, 1.03f, false);
            }
        }

        /// <summary>Guarda-corpo de inox: montantes, corrimão, três tubos e a tela embaixo; borda da laje à mostra do lado do vão.</summary>
        void RailingRun(Vector3 o, Vector3 along, Vector3 across, float a0, float a1, float hole)
        {
            OEdgeBox(office.concrete, o, along, across, a0, a1, hole * -0.08f, hole * 0.08f, -0.35f, 0f, false);
            for (float a = a0; a <= a1 + 1e-3f; a += 1f)
                OEdgeBox(office.steel, o, along, across, a - 0.02f, a + 0.02f, -0.02f, 0.02f, 0f, 1.1f, false);

            OTube(office.steel, o + along * a0 + Vector3.up * 1.1f, o + along * a1 + Vector3.up * 1.1f, 0.025f, 8);
            foreach (float y in new[] { 0.4f, 0.62f, 0.84f })
                OTube(office.steel, o + along * a0 + Vector3.up * y, o + along * a1 + Vector3.up * y, 0.012f, 6);
            OEdgeBox(office.darkMetal, o, along, across, a0, a1, -0.004f, 0.004f, 0.05f, 0.32f, false);

            // Colisor mais alto que o corrimão: não dá para pular para o vão.
            Vector3 center = o + along * ((a0 + a1) * 0.5f) + Vector3.up * 0.65f;
            AddCollider(_ok.Colliders, center, Abs(along) * (a1 - a0) + Abs(across) * 0.06f + Vector3.up * 1.3f);
        }

        // ---------------------------------------------------------------- Luminárias

        void BuildOfficeLights()
        {
            for (int z = 0; z < Data.Height; z++)
                for (int x = 0; x < Data.Width; x++)
                {
                    LightState state = Data.Lights[x, z];
                    if (state == LightState.None) continue;

                    switch (Data.Office[x, z])
                    {
                        case OfficeCell.Corridor: CorridorTube(x, z, state); break;
                        case OfficeCell.Classroom:
                        case OfficeCell.Small: RecessedLight(x, z, state); break;
                        case OfficeCell.Lobby:
                        case OfficeCell.Cafe:
                        case OfficeCell.Lounge: Pendant(x, z, state); break;
                    }
                }
        }

        // Luminária tubular atravessada no corredor, pendurada por dois cabos.
        void CorridorTube(int x, int z, LightState state)
        {
            Vector3 c = OCellCenter(x, z);
            bool runsAlongX = CorridorLink(x, z, WallSide.East) || CorridorLink(x, z, WallSide.West);
            Quaternion turn = runsAlongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
            Vector3 tubeAxis = turn * Vector3.right;

            MeshRenderer tube = CreateRenderer($"Tubo_{x}_{z}", _ok.TubeMesh, office.tubeOff, ShadowCastingMode.Off, _ok.Fixtures);
            tube.transform.localPosition = c + Vector3.up * OfficeTubeHeight;
            tube.transform.localRotation = turn;
            AddCollider(tube.gameObject, Vector3.zero, new Vector3(2.4f, 0.075f, 0.075f));

            foreach (float s in new[] { -0.95f, 0.95f })
                OBox(office.steel, c + tubeAxis * s + Vector3.up * ((OfficeTubeHeight + OfficeSlab) * 0.5f), new Vector3(0.006f, OfficeSlab - OfficeTubeHeight, 0.006f), caps: false);

            HDAdditionalLightData[] lights = null;
            if (state != LightState.Dead)
                // Girar o tubo em Y não muda o "para baixo" do spot.
                lights = new[] { OfficeSpot(tube.transform, new Vector3(0f, -0.06f, 0f), Quaternion.Euler(90f, 0f, 0f), officeTubeCandela, 7.5f, 150f, 5200f, true) };

            CeilingLight light = tube.gameObject.AddComponent<CeilingLight>();
            light.Init(state, lights, tube, office.tubeOn, office.tubeOff);
            _ok.CorridorLights[x + z * Data.Width] = light;
        }

        void RecessedLight(int x, int z, LightState state)
        {
            Vector3 c = OCellCenter(x, z);
            MeshRenderer panel = CreateRenderer($"Embutida_{x}_{z}", _ok.PanelMesh, office.panelOff, ShadowCastingMode.Off, _ok.Fixtures);
            panel.transform.localPosition = c + Vector3.up * (OfficeRoomCeiling - 0.004f);

            HDAdditionalLightData[] lights = null;
            if (state != LightState.Dead)
                lights = new[] { OfficeSpot(panel.transform, new Vector3(0f, -0.04f, 0f), Quaternion.Euler(90f, 0f, 0f), officeRoomCandela, 7.5f, 150f, 4300f, true) };

            panel.gameObject.AddComponent<CeilingLight>().Init(state, lights, panel, office.panelOn, office.panelOff);
        }

        // Pendente em meia-esfera preta, pendurado das ripas.
        void Pendant(int x, int z, LightState state)
        {
            Vector3 c = OCellCenter(x, z);
            float bottom = Data.Office[x, z] == OfficeCell.Lounge ? 2.55f : 2.85f;
            OTube(office.blackPlastic, c + Vector3.up * bottom, c + Vector3.up * (bottom + 0.22f), 0.3f, 20);
            OM(office.blackPlastic).AddDisc(c + Vector3.up * (bottom + 0.22f), Vector3.up, Vector3.right, 0.3f, 20);
            OBox(office.blackPlastic, c + Vector3.up * ((bottom + 0.22f + OfficeSlatsBottom) * 0.5f), new Vector3(0.008f, OfficeSlatsBottom - bottom - 0.22f, 0.008f), caps: false);

            MeshRenderer dome = CreateRenderer($"Pendente_{x}_{z}", _ok.DomeMesh, office.domeOff, ShadowCastingMode.Off, _ok.Fixtures);
            dome.transform.localPosition = c + Vector3.up * (bottom + 0.03f);

            float candela = Data.Office[x, z] == OfficeCell.Lounge ? officePendantCandela * 0.55f : officePendantCandela;
            HDAdditionalLightData[] lights = null;
            if (state != LightState.Dead)
                lights = new[] { OfficeSpot(dome.transform, new Vector3(0f, -0.02f, 0f), Quaternion.Euler(90f, 0f, 0f), candela, 7f, 115f, 3600f, true) };

            dome.gameObject.AddComponent<CeilingLight>().Init(state, lights, dome, office.domeOn, office.domeOff);
        }

        // ---------------------------------------------------------------- Eventos

        void BuildOfficeEvents()
        {
            GameObject events = CreateChild("Eventos");

            var elevators = events.AddComponent<OfficeElevators>();
            elevators.Init(Data.Seed, _ok.Elevators);

            // Trechos retos de corredor (5 células ou mais) com luminária: a onda corre por eles.
            var runs = new List<CeilingLight[]>();
            int w = Data.Width, h = Data.Height;
            for (int z = 0; z < h; z++)
                CollectRuns(runs, w, (i) => (i, z));
            for (int x = 0; x < w; x++)
                CollectRuns(runs, h, (i) => (x, i));

            events.AddComponent<OfficeLightWave>().Init(Data.Seed, runs);
        }

        void CollectRuns(List<CeilingLight[]> runs, int length, Func<int, (int x, int z)> cellAt)
        {
            var current = new List<CeilingLight>();
            for (int i = 0; i <= length; i++)
            {
                CeilingLight light = null;
                bool corridor = false;
                if (i < length)
                {
                    (int x, int z) = cellAt(i);
                    corridor = Data.Office[x, z] == OfficeCell.Corridor;
                    _ok.CorridorLights.TryGetValue(x + z * Data.Width, out light);
                    // A sequência só continua se a célula anterior se liga a esta pelo corredor.
                    if (corridor && current.Count > 0 && i > 0)
                    {
                        (int px, int pz) = cellAt(i - 1);
                        WallSide toward = px < x ? WallSide.West : px > x ? WallSide.East : pz < z ? WallSide.South : WallSide.North;
                        if (!CorridorLink(x, z, toward)) corridor = false;
                    }
                }

                if (corridor && light != null)
                {
                    current.Add(light);
                    continue;
                }

                if (current.Count >= 5) runs.Add(current.ToArray());
                current.Clear();
                if (corridor && light != null) current.Add(light);
            }
        }
    }
}
