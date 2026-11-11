using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    /// <summary>
    /// Constrói o Level 0 em tempo de execução a partir de uma seed: malhas combinadas,
    /// colisores primitivos, luminárias, esconderijos, buracos, marcas nas paredes e a saída.
    /// O mapa só existe durante o Play.
    /// </summary>
    public sealed class LevelBuilder : MonoBehaviour
    {
        public const float Cell = 4f;
        // Pé-direito de 4,05 m: 1,5 vez o de um escritório comum (2,7 m).
        public const float WallHeight = 4.05f;
        public const float WallThickness = 0.2f;

        /// <summary>Lado do mapa em células. 20 x 20 = 80 x 80 m (era 16 x 16 até a versão 9).</summary>
        public const int DefaultSize = 20;

        // Buracos (medidas em metros). A faixa é mais estreita que o jogador (0,6 m).
        public const float PitDepth = 7f;
        public const float StripWidth = 0.4f;
        const float StripThickness = 0.25f;
        const float StripPostSize = 0.3f;
        const float FloorThickness = 0.5f;
        // Quem passa desta altura dentro de um buraco é dado como caído.
        const float PitFallLine = -2.5f;

        const float CeilingTile = 0.6f;
        const float BaseboardHeight = 0.1f;
        const float PillarSize = 0.8f;
        const float PanelWidth = 0.6f;
        const float PanelLength = 1.2f;
        // Calha da luminária: aro de chapa em volta do difusor, saindo do forro.
        const float TrofferDrop = 0.045f;
        const float TrofferRim = 0.03f;
        const float DiffuserInset = 0.005f;

        // Parede grossa com nicho (medidas em metros).
        const float AlcoveHalfThickness = 0.7f;
        const float AlcoveShell = 0.2f;
        const float AlcoveCavityHalfLength = 1.2f;
        const float AlcoveCavityHeight = 1.9f;
        const float AlcoveOpeningHalfWidth = 0.45f;
        const float AlcoveOpeningHeight = 1.25f;

        // Vão de porta da sala inicial.
        const float DoorHalfWidth = 0.7f;
        const float DoorHeight = 2.3f;

        [Header("Geração")]
        public int seed = 1998;
        [Min(4)] public int width = DefaultSize;
        [Min(4)] public int height = DefaultSize;

        [Header("Materiais")]
        public Material wallMaterial;
        public Material floorMaterial;
        public Material ceilingMaterial;
        public Material baseboardMaterial;
        public Material panelOnMaterial;
        public Material panelOffMaterial;
        [Tooltip("Chapa da calha em volta do difusor. Vazio = painel plano colado ao forro, sem calha.")]
        public Material lightHousingMaterial;
        [Tooltip("Papel de parede e carpete sem nenhum encardido, usados só na sala inicial.")]
        public Material cleanWallMaterial;
        public Material cleanFloorMaterial;
        [Tooltip("Concreto das paredes e do fundo dos buracos.")]
        public Material pitMaterial;
        [Tooltip("Névoa translúcida que cobre o fundo dos buracos.")]
        public Material pitSmokeMaterial;
        [Tooltip("Preto sem luz, por baixo da fumaça.")]
        public Material pitVoidMaterial;

        [Header("Luminárias")]
        [Tooltip("Intensidade em candela.")]
        public float lightCandela = 2300f;
        public float lightRange = 15f;
        public float lightTemperature = 6000f;
        [Tooltip("Sombra em cache (renderizada uma vez). Impede a luz de vazar pelas paredes.")]
        public bool ceilingShadows = true;
        [Tooltip("Resolução máxima de cada face da sombra. É reduzida sozinha se não couber no atlas do HDRP.")]
        public int maxShadowResolution = 256;

        [Header("Marcas (decalques)")]
        public Material[] bloodDecals;
        public Material[] textDecals;
        public Material[] poolDecals;
        public Material arrowDecal;

        [Header("Sujeira e desgaste (decalques)")]
        [Tooltip("0 = escorrido do teto, 1 = umidade subindo do rodapé, 2 = mancha solta.")]
        public Material[] wallStainDecals;
        public Material wallScuffDecal;
        public Material[] floorWetDecals;
        public Material floorWearDecal;

        [Header("Grelhas de ar")]
        [Tooltip("Chapa da grelha de retorno de ar. Vazio = sem grelhas.")]
        public Material ventMaterial;
        [Tooltip("Altura do centro da grelha, em metros.")]
        public float ventHeight = 3.45f;

        [Header("Saída")]
        public Material exitDoorMaterial;
        public Material exitSignMaterial;
        [Tooltip("Porta em 3D (folha, batente e barra antipânico), com a frente em -Z, o pé em Y = 0 e o fundo na face da parede. " +
                 "Vazio = porta plana antiga.")]
        public Mesh exitDoorModel;
        public Material exitDoorModelMaterial;

        [Tooltip("Versão do Level0Setup que gerou esta cena. O editor usa isso para avisar quando o conteúdo ficou velho.")]
        [HideInInspector] public int contentVersion;

        [Header("Uso sem rede")]
        [Tooltip("Constrói sozinho ao iniciar. Deixe desligado quando houver um GameSession na cena.")]
        public bool buildOnAwake;

        public LevelData Data { get; private set; }

        void Awake()
        {
            // Com sessão de rede, quem manda construir é o GameSession, com a seed do host.
            if (buildOnAwake) Rebuild();
        }

        /// <summary>Apaga o que foi gerado e constrói de novo com a seed atual.</summary>
        public void Rebuild()
        {
            Clear();
            Build();
        }

        /// <summary>
        /// Ponto de início do jogador de índice <paramref name="index"/>: os quatro cantos
        /// de um quadrado no centro da sala inicial, para ninguém nascer dentro de ninguém.
        /// </summary>
        public Vector3 SpawnPosition(int index)
        {
            if (Data == null) return transform.position;

            int slot = Mathf.Abs(index) % 4;
            var offset = new Vector3(slot % 2 == 0 ? -0.7f : 0.7f, 0.05f, slot < 2 ? -0.7f : 0.7f);
            return SpawnCenter + offset;
        }

        /// <summary>Centro da sala inicial, no chão.</summary>
        public Vector3 SpawnCenter
        {
            get
            {
                if (Data == null) return transform.position;
                return transform.TransformPoint(new Vector3(Data.SpawnCenterX * Cell, 0f, Data.SpawnCenterZ * Cell));
            }
        }

        /// <summary>
        /// Um ponto com vista aberta do mapa, a 60% do caminho entre o início e a saída.
        /// Serve de posição para a câmera do menu, já que a sala inicial é fechada.
        /// </summary>
        public Vector3 ScenicPosition()
        {
            if (Data == null || Data.PathCells.Count == 0) return SpawnCenter;

            // A primeira célula com piso a partir de 60% do caminho (ele passa por cima de buracos).
            int start = Mathf.Min(Data.PathCells.Count - 1, Data.PathCells.Count * 6 / 10);

            for (int i = start; i < Data.PathCells.Count; i++)
            {
                int index = Data.PathCells[i];
                if (!Data.Pit[index % Data.Width, index / Data.Width]) return CellCenter(index % Data.Width, index / Data.Width);
            }

            return SpawnCenter;
        }

        /// <summary>Remove tudo que este componente gerou.</summary>
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            Data = null;
        }

        public Vector3 CellCenter(int x, int z)
        {
            return transform.TransformPoint(new Vector3((x + 0.5f) * Cell, 0f, (z + 0.5f) * Cell));
        }

        // ---------------------------------------------------------------- Geometria

        void Build()
        {
            Data = LevelGenerator.Generate(seed, width, height);

            float sizeX = Data.Width * Cell;
            float sizeZ = Data.Height * Cell;

            // A textura do carpete cobre 2 m, para a repetição aparecer menos.
            var floor = new MeshBuilder(2f);
            var ceiling = new MeshBuilder(CeilingTile);
            // O papel de parede também cobre 2 m por repetição.
            var walls = new MeshBuilder(2f);
            var baseboards = new MeshBuilder(1f);
            // Sala inicial: mesmas peças, com materiais limpos.
            var cleanFloor = new MeshBuilder(2f);
            var cleanWalls = new MeshBuilder(2f);
            // Concreto dos buracos: paredes e fundo do poço, laterais das faixas e seus apoios.
            var pits = new MeshBuilder(2f);

            GameObject colliders = CreateChild("Colisores");
            GameObject hidingSpots = CreateChild("Esconderijos");

            // Piso: um quad por célula, para a sala inicial poder usar outro material e os
            // buracos ficarem sem nenhum. O colisor é espesso, para ninguém atravessar, e vai
            // em trechos: um por sequência de células com piso em cada fileira.
            for (int z = 0; z < Data.Height; z++)
            {
                int runStart = -1;

                for (int x = 0; x <= Data.Width; x++)
                {
                    bool solid = x < Data.Width && !Data.Pit[x, z];

                    if (solid)
                    {
                        MeshBuilder target = Data.InRoom(x, z) ? cleanFloor : floor;
                        float fx = x * Cell, fz = z * Cell;
                        target.AddQuad(new Vector3(fx, 0f, fz), new Vector3(fx, 0f, fz + Cell),
                                       new Vector3(fx + Cell, 0f, fz + Cell), new Vector3(fx + Cell, 0f, fz), Vector3.up);

                        if (runStart < 0) runStart = x;
                        continue;
                    }

                    if (runStart < 0) continue;

                    float length = (x - runStart) * Cell;
                    AddCollider(colliders, new Vector3(runStart * Cell + length * 0.5f, -FloorThickness * 0.5f, (z + 0.5f) * Cell),
                                new Vector3(length, FloorThickness, Cell));
                    runStart = -1;
                }
            }

            BuildPits(floor, pits, colliders);

            ceiling.AddQuad(new Vector3(0f, WallHeight, 0f), new Vector3(0f, WallHeight, sizeZ),
                            new Vector3(sizeX, WallHeight, sizeZ), new Vector3(sizeX, WallHeight, 0f), Vector3.down);
            AddCollider(colliders, new Vector3(sizeX * 0.5f, WallHeight + 0.25f, sizeZ * 0.5f), new Vector3(sizeX + 2f, 0.5f, sizeZ + 2f));

            // As paredes passam 10 cm de cada ponta para fechar os cantos.
            float wallLength = Cell + WallThickness;

            for (int z = 0; z <= Data.Height; z++)
            {
                for (int x = 0; x < Data.Width; x++)
                {
                    var origin = new Vector3((x + 0.5f) * Cell, 0f, z * Cell);

                    if (Data.DoorSouth[x, z])
                    {
                        AddDoorway(cleanWalls, baseboards, colliders, origin, Vector3.right, Vector3.forward);
                        continue;
                    }

                    if (!Data.WallSouth[x, z]) continue;

                    if (Data.AlcoveSouth[x, z] != 0)
                        AddAlcove(walls, baseboards, colliders, hidingSpots, origin, Vector3.right, Vector3.forward,
                                  Data.AlcoveSouth[x, z] == 1 ? -1f : 1f);
                    else
                        AddWall(Data.RoomSouth[x, z] ? cleanWalls : walls, baseboards, colliders, origin,
                                new Vector3(wallLength, WallHeight, WallThickness));
                }
            }

            for (int x = 0; x <= Data.Width; x++)
            {
                for (int z = 0; z < Data.Height; z++)
                {
                    var origin = new Vector3(x * Cell, 0f, (z + 0.5f) * Cell);

                    if (Data.DoorWest[x, z])
                    {
                        AddDoorway(cleanWalls, baseboards, colliders, origin, Vector3.forward, Vector3.right);
                        continue;
                    }

                    if (!Data.WallWest[x, z]) continue;

                    if (Data.AlcoveWest[x, z] != 0)
                        AddAlcove(walls, baseboards, colliders, hidingSpots, origin, Vector3.forward, Vector3.right,
                                  Data.AlcoveWest[x, z] == 1 ? -1f : 1f);
                    else
                        AddWall(Data.RoomWest[x, z] ? cleanWalls : walls, baseboards, colliders, origin,
                                new Vector3(WallThickness, WallHeight, wallLength));
                }
            }

            for (int z = 0; z <= Data.Height; z++)
            {
                for (int x = 0; x <= Data.Width; x++)
                {
                    if (!Data.Pillar[x, z]) continue;
                    AddWall(walls, baseboards, colliders,
                            new Vector3(x * Cell, 0f, z * Cell),
                            new Vector3(PillarSize, WallHeight, PillarSize));
                }
            }

            CreateRenderer("Piso", floor.ToMesh("Piso"), floorMaterial, ShadowCastingMode.Off);
            CreateRenderer("Teto", ceiling.ToMesh("Teto"), ceilingMaterial, ShadowCastingMode.Off);
            CreateRenderer("Paredes", walls.ToMesh("Paredes"), wallMaterial, ShadowCastingMode.On);

            if (!cleanFloor.IsEmpty)
                CreateRenderer("PisoDaSalaInicial", cleanFloor.ToMesh("PisoDaSalaInicial"),
                               cleanFloorMaterial != null ? cleanFloorMaterial : floorMaterial, ShadowCastingMode.Off);
            if (!cleanWalls.IsEmpty)
                CreateRenderer("ParedesDaSalaInicial", cleanWalls.ToMesh("ParedesDaSalaInicial"),
                               cleanWallMaterial != null ? cleanWallMaterial : wallMaterial, ShadowCastingMode.On);
            CreateRenderer("Rodapes", baseboards.ToMesh("Rodapes"), baseboardMaterial, ShadowCastingMode.Off);

            // Sombra dos dois lados: as paredes do poço só têm a face de dentro, e é pelo
            // lado de fora que a luz dos cômodos vizinhos chegaria ao buraco.
            if (!pits.IsEmpty)
                CreateRenderer("PocosDosBuracos", pits.ToMesh("PocosDosBuracos"),
                               pitMaterial != null ? pitMaterial : baseboardMaterial, ShadowCastingMode.TwoSided);

            BuildLights();
            BuildDecorations();
            BuildExit();
        }

        // ---------------------------------------------------------------- Buracos

        /// <summary>
        /// Cada buraco é um poço retangular: quatro paredes de concreto que descem do nível do
        /// piso, o fundo, a fumaça que não deixa ver esse fundo e um gatilho que dá o jogador
        /// como caído. Dentro dele ficam as faixas — vigas estreitas, com carpete em cima,
        /// apoiadas em pilaretes. Tirando a fumaça, tudo que aparece tem colisor do mesmo
        /// tamanho (ver Level0ColliderCheck).
        /// </summary>
        void BuildPits(MeshBuilder floor, MeshBuilder pits, GameObject colliders)
        {
            if (Data.Pits.Count == 0) return;

            GameObject zones = CreateChild("Quedas");
            const float shell = 0.2f;

            foreach (PitArea area in Data.Pits)
            {
                float x0 = area.X0 * Cell, x1 = (area.X0 + area.Width) * Cell;
                float z0 = area.Z0 * Cell, z1 = (area.Z0 + area.Height) * Cell;
                float midX = (x0 + x1) * 0.5f, midZ = (z0 + z1) * 0.5f;
                float bottom = -PitDepth;

                pits.AddQuad(new Vector3(x0, bottom, z0), new Vector3(x0, bottom, z1),
                             new Vector3(x1, bottom, z1), new Vector3(x1, bottom, z0), Vector3.up);
                AddCollider(colliders, new Vector3(midX, bottom - shell * 0.5f, midZ), new Vector3(x1 - x0, shell, z1 - z0));

                // Paredes do poço, viradas para dentro. O colisor fica atrás da face.
                pits.AddQuad(new Vector3(x0, bottom, z0), new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), new Vector3(x0, bottom, z1), Vector3.right);
                AddCollider(colliders, new Vector3(x0 - shell * 0.5f, bottom * 0.5f, midZ), new Vector3(shell, PitDepth, z1 - z0));

                pits.AddQuad(new Vector3(x1, bottom, z0), new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), new Vector3(x1, bottom, z1), Vector3.left);
                AddCollider(colliders, new Vector3(x1 + shell * 0.5f, bottom * 0.5f, midZ), new Vector3(shell, PitDepth, z1 - z0));

                pits.AddQuad(new Vector3(x0, bottom, z0), new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), new Vector3(x1, bottom, z0), Vector3.forward);
                AddCollider(colliders, new Vector3(midX, bottom * 0.5f, z0 - shell * 0.5f), new Vector3(x1 - x0, PitDepth, shell));

                pits.AddQuad(new Vector3(x0, bottom, z1), new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), new Vector3(x1, bottom, z1), Vector3.back);
                AddCollider(colliders, new Vector3(midX, bottom * 0.5f, z1 + shell * 0.5f), new Vector3(x1 - x0, PitDepth, shell));

                var zone = new GameObject("Queda");
                zone.transform.SetParent(zones.transform, false);
                zone.transform.localPosition = new Vector3(midX, (bottom + PitFallLine) * 0.5f, midZ);

                var trigger = zone.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = new Vector3(x1 - x0, PitFallLine - bottom, z1 - z0);
                zone.AddComponent<PitFall>();

                var smoke = new GameObject("Fumaca");
                smoke.transform.SetParent(zones.transform, false);
                smoke.transform.localPosition = new Vector3(midX, 0f, midZ);
                smoke.AddComponent<PitSmoke>().Build(x1 - x0, z1 - z0, pitSmokeMaterial, pitVoidMaterial, area.X0 + area.Z0 * Data.Width);
            }

            float half = StripWidth * 0.5f;

            for (int z = 0; z < Data.Height; z++)
            {
                for (int x = 0; x < Data.Width; x++)
                {
                    if (Data.PitStrips[x, z] == 0) continue;

                    float cx = (x + 0.5f) * Cell, cz = (z + 0.5f) * Cell;

                    // Miolo da célula, onde as faixas se encontram, e o pilarete que o sustenta.
                    AddStrip(floor, pits, colliders, cx - half, cx + half, cz - half, cz + half);

                    float postHeight = PitDepth - StripThickness;
                    var postCenter = new Vector3(cx, -StripThickness - postHeight * 0.5f, cz);
                    var postSize = new Vector3(StripPostSize, postHeight, StripPostSize);
                    pits.AddBox(postCenter, postSize);
                    AddCollider(colliders, postCenter, postSize);

                    if (Data.HasStrip(x, z, WallSide.West)) AddStrip(floor, pits, colliders, x * Cell, cx - half, cz - half, cz + half);
                    if (Data.HasStrip(x, z, WallSide.East)) AddStrip(floor, pits, colliders, cx + half, (x + 1) * Cell, cz - half, cz + half);
                    if (Data.HasStrip(x, z, WallSide.South)) AddStrip(floor, pits, colliders, cx - half, cx + half, z * Cell, cz - half);
                    if (Data.HasStrip(x, z, WallSide.North)) AddStrip(floor, pits, colliders, cx - half, cx + half, cz + half, (z + 1) * Cell);
                }
            }
        }

        // Um trecho de faixa: carpete em cima, concreto dos lados e embaixo.
        void AddStrip(MeshBuilder floor, MeshBuilder pits, GameObject colliders, float xMin, float xMax, float zMin, float zMax)
        {
            var center = new Vector3((xMin + xMax) * 0.5f, -StripThickness * 0.5f, (zMin + zMax) * 0.5f);
            var size = new Vector3(xMax - xMin, StripThickness, zMax - zMin);

            floor.AddQuad(new Vector3(xMin, 0f, zMin), new Vector3(xMin, 0f, zMax),
                          new Vector3(xMax, 0f, zMax), new Vector3(xMax, 0f, zMin), Vector3.up);
            pits.AddBox(center, size);
            pits.AddQuad(new Vector3(xMin, -StripThickness, zMin), new Vector3(xMin, -StripThickness, zMax),
                         new Vector3(xMax, -StripThickness, zMax), new Vector3(xMax, -StripThickness, zMin), Vector3.down);
            AddCollider(colliders, center, size);
        }

        void AddWall(MeshBuilder walls, MeshBuilder baseboards, GameObject colliders, Vector3 basePosition, Vector3 size)
        {
            Vector3 center = basePosition + Vector3.up * (size.y * 0.5f);
            walls.AddBox(center, size);
            AddCollider(colliders, center, size);

            // Rodapé: 2 cm mais largo que a parede, com tampa em cima.
            var baseboardSize = new Vector3(size.x + 0.04f, BaseboardHeight, size.z + 0.04f);
            baseboards.AddBox(basePosition + Vector3.up * (BaseboardHeight * 0.5f), baseboardSize, caps: true);
        }

        /// <summary>
        /// Parede comum com um vão de porta no meio (1,4 m de largura por 2,3 m de altura),
        /// com uma guarnição fina em volta. Não tem folha de porta: é só a passagem.
        /// </summary>
        void AddDoorway(MeshBuilder walls, MeshBuilder baseboards, GameObject colliders, Vector3 origin, Vector3 along, Vector3 depth)
        {
            float halfLength = Cell * 0.5f + WallThickness * 0.5f;
            float half = WallThickness * 0.5f;

            AlcoveBox(walls, baseboards, colliders, origin, along, depth, -halfLength, -DoorHalfWidth, -half, half, 0f, WallHeight);
            AlcoveBox(walls, baseboards, colliders, origin, along, depth, DoorHalfWidth, halfLength, -half, half, 0f, WallHeight);
            AlcoveBox(walls, baseboards, colliders, origin, along, depth, -DoorHalfWidth, DoorHalfWidth, -half, half, DoorHeight, WallHeight);

            // Batente: reveste o vão por dentro e sobra 7 cm em volta dele. Nenhuma face pode
            // ficar exatamente no mesmo plano de uma face da parede ou do rodapé — duas
            // superfícies no mesmo lugar ficam piscando. Por isso o batente avança 1,5 cm para
            // dentro do vão e 3 cm para fora de cada face da parede (o rodapé sai 2 cm).
            const float trim = 0.07f;
            const float inset = 0.015f;
            float trimDepth = WallThickness + 0.06f;

            float jambWidth = trim + inset;
            float jambCenter = DoorHalfWidth - inset + jambWidth * 0.5f;
            float jambHeight = DoorHeight - inset;
            Vector3 jamb = Abs(along) * jambWidth + Abs(depth) * trimDepth + Vector3.up * jambHeight;

            // A travessa de cima é um fio mais funda que as laterais, pelo mesmo motivo.
            float headHeight = trim + inset;
            Vector3 head = Abs(along) * ((DoorHalfWidth + trim) * 2f + 0.004f) + Abs(depth) * (trimDepth + 0.004f) + Vector3.up * headHeight;

            baseboards.AddBox(origin + along * -jambCenter + Vector3.up * (jambHeight * 0.5f), jamb, caps: true);
            baseboards.AddBox(origin + along * jambCenter + Vector3.up * (jambHeight * 0.5f), jamb, caps: true);
            baseboards.AddBox(origin + Vector3.up * (DoorHeight - inset + headHeight * 0.5f), head, caps: true);
        }

        /// <summary>
        /// Parede de 1,4 m de espessura com um nicho oco. A entrada é um buraco baixo
        /// (precisa agachar) de um dos lados; lá dentro dá para ficar em pé.
        /// </summary>
        /// <param name="along">Eixo ao longo da parede.</param>
        /// <param name="depth">Eixo que atravessa a parede.</param>
        /// <param name="opening">-1 ou +1: para que lado do eixo de profundidade fica a entrada.</param>
        void AddAlcove(MeshBuilder walls, MeshBuilder baseboards, GameObject colliders, GameObject hidingSpots,
                       Vector3 origin, Vector3 along, Vector3 depth, float opening)
        {
            float halfLength = Cell * 0.5f + WallThickness * 0.5f;
            float outer = AlcoveHalfThickness;
            float inner = AlcoveHalfThickness - AlcoveShell;
            float cavity = AlcoveCavityHalfLength;
            float door = AlcoveOpeningHalfWidth;

            // Blocos maciços das pontas e parede do fundo.
            AlcoveBox(walls, baseboards, colliders, origin, along, depth, -halfLength, -cavity, -outer, outer, 0f, WallHeight);
            AlcoveBox(walls, baseboards, colliders, origin, along, depth, cavity, halfLength, -outer, outer, 0f, WallHeight);
            AlcoveBox(walls, baseboards, colliders, origin, along, depth, -cavity, cavity, -opening * outer, -opening * inner, 0f, WallHeight);

            // Teto do nicho.
            AlcoveBox(walls, baseboards, colliders, origin, along, depth, -cavity, cavity, -opening * inner, opening * outer, AlcoveCavityHeight, WallHeight);

            // Parede da frente, com o buraco de entrada no meio.
            AlcoveBox(walls, baseboards, colliders, origin, along, depth, -cavity, -door, opening * inner, opening * outer, 0f, AlcoveCavityHeight);
            AlcoveBox(walls, baseboards, colliders, origin, along, depth, door, cavity, opening * inner, opening * outer, 0f, AlcoveCavityHeight);
            AlcoveBox(walls, baseboards, colliders, origin, along, depth, -door, door, opening * inner, opening * outer, AlcoveOpeningHeight, AlcoveCavityHeight);

            // Volume que marca o jogador como escondido.
            var spot = new GameObject("Esconderijo");
            spot.transform.SetParent(hidingSpots.transform, false);
            spot.transform.localPosition = origin + Vector3.up * (AlcoveCavityHeight * 0.5f);

            var trigger = spot.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = Abs(along) * (cavity * 2f - 0.2f) + Abs(depth) * (inner * 2f - 0.1f) + Vector3.up * AlcoveCavityHeight;
            spot.AddComponent<HidingSpot>();
        }

        void AlcoveBox(MeshBuilder walls, MeshBuilder baseboards, GameObject colliders, Vector3 origin, Vector3 along, Vector3 depth,
                       float a0, float a1, float d0, float d1, float y0, float y1)
        {
            float aMin = Mathf.Min(a0, a1), aMax = Mathf.Max(a0, a1);
            float dMin = Mathf.Min(d0, d1), dMax = Mathf.Max(d0, d1);

            Vector3 basePosition = origin + along * ((aMin + aMax) * 0.5f) + depth * ((dMin + dMax) * 0.5f);
            Vector3 size = Abs(along) * (aMax - aMin) + Abs(depth) * (dMax - dMin) + Vector3.up * (y1 - y0);

            if (y0 <= 0f)
            {
                AddWall(walls, baseboards, colliders, basePosition, size);
                return;
            }

            // Peça suspensa: precisa da face de baixo e não tem rodapé.
            Vector3 center = basePosition + Vector3.up * ((y0 + y1) * 0.5f);
            walls.AddBox(center, size, caps: true);
            AddCollider(colliders, center, size);
        }

        static Vector3 Abs(Vector3 v)
        {
            return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

        static void AddCollider(GameObject host, Vector3 center, Vector3 size)
        {
            var box = host.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }

        GameObject CreateChild(string objectName)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            return go;
        }

        MeshRenderer CreateRenderer(string objectName, Mesh mesh, Material material, ShadowCastingMode shadows, Transform parent = null)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = shadows;
            return meshRenderer;
        }

        // ---------------------------------------------------------------- Luminárias

        void BuildLights()
        {
            GameObject root = CreateChild("Luminarias");
            Mesh panelMesh = CreateQuadMesh("Painel", PanelWidth, PanelLength, horizontal: true);
            bool housed = lightHousingMaterial != null;
            Mesh housingMesh = housed ? CreateTrofferMesh() : null;
            // Com calha, o difusor desce junto com o aro (fica 5 mm para dentro dele).
            float panelHeight = housed ? WallHeight - TrofferDrop + DiffuserInset : WallHeight - 0.004f;

            int count = 0;
            foreach (LightState state in Data.Lights)
                if (state == LightState.On || state == LightState.Flicker) count++;

            int shadowResolution = ResolveShadowResolution(count);

            for (int z = 0; z < Data.Height; z++)
            {
                for (int x = 0; x < Data.Width; x++)
                {
                    LightState state = Data.Lights[x, z];
                    if (state == LightState.None) continue;

                    // Encaixa o painel na grade de placas do forro (0,6 m).
                    float cx = (x + 0.5f) * Cell;
                    float cz = (z + 0.5f) * Cell;
                    float px = Mathf.Round((cx - PanelWidth * 0.5f) / CeilingTile) * CeilingTile + PanelWidth * 0.5f;
                    float pz = Mathf.Round((cz - PanelLength * 0.5f) / CeilingTile) * CeilingTile + PanelLength * 0.5f;

                    MeshRenderer panel = CreateRenderer($"Luminaria_{x}_{z}", panelMesh, panelOffMaterial, ShadowCastingMode.Off, root.transform);
                    panel.transform.localPosition = new Vector3(px, panelHeight, pz);

                    if (housed)
                    {
                        // Sem sombra: a luz fica dentro da calha e não pode ser tapada por ela.
                        MeshRenderer housing = CreateRenderer("Calha", housingMesh, lightHousingMaterial, ShadowCastingMode.Off, panel.transform);
                        // Colisor do mesmo tamanho da calha fechada (aro + difusor).
                        var box = housing.gameObject.AddComponent<BoxCollider>();
                        box.center = new Vector3(0f, TrofferDrop * 0.5f - DiffuserInset, 0f);
                        box.size = new Vector3(PanelWidth + TrofferRim * 2f, TrofferDrop, PanelLength + TrofferRim * 2f);
                    }

                    HDAdditionalLightData[] lights = null;
                    if (state != LightState.Dead)
                    {
                        lights = new[] { CreateCeilingLight(panel.transform, shadowResolution) };
                        // A luz fica sempre 3,4 cm abaixo do forro, com ou sem calha, para a
                        // iluminação do andar não mudar.
                        lights[0].transform.localPosition = new Vector3(0f, WallHeight - 0.034f - panelHeight, 0f);
                    }

                    panel.gameObject.AddComponent<CeilingLight>().Init(state, lights, panel, panelOnMaterial, panelOffMaterial);
                }
            }
        }

        /// <summary>
        /// Calha de luminária de embutir: laterais de chapa do forro até o aro, o aro em volta
        /// do difusor e a borda de 5 mm até ele. No espaço do painel: difusor em Y = 0, forro em
        /// Y = TrofferDrop - DiffuserInset. 24 triângulos, a mesma malha em todas as luminárias.
        /// </summary>
        static Mesh CreateTrofferMesh()
        {
            var mesh = new MeshBuilder(1f);
            float ix = PanelWidth * 0.5f, iz = PanelLength * 0.5f;
            float ox = ix + TrofferRim, oz = iz + TrofferRim;
            float top = TrofferDrop - DiffuserInset;
            float rim = -DiffuserInset;

            // Laterais de fora, do forro até o aro.
            mesh.AddQuad(new Vector3(ox, rim, -oz), new Vector3(ox, top, -oz), new Vector3(ox, top, oz), new Vector3(ox, rim, oz), Vector3.right);
            mesh.AddQuad(new Vector3(-ox, rim, -oz), new Vector3(-ox, top, -oz), new Vector3(-ox, top, oz), new Vector3(-ox, rim, oz), Vector3.left);
            mesh.AddQuad(new Vector3(-ox, rim, oz), new Vector3(-ox, top, oz), new Vector3(ox, top, oz), new Vector3(ox, rim, oz), Vector3.forward);
            mesh.AddQuad(new Vector3(-ox, rim, -oz), new Vector3(-ox, top, -oz), new Vector3(ox, top, -oz), new Vector3(ox, rim, -oz), Vector3.back);

            // Aro, visto de baixo.
            mesh.AddQuad(new Vector3(-ox, rim, -oz), new Vector3(ox, rim, -oz), new Vector3(ox, rim, -iz), new Vector3(-ox, rim, -iz), Vector3.down);
            mesh.AddQuad(new Vector3(-ox, rim, iz), new Vector3(ox, rim, iz), new Vector3(ox, rim, oz), new Vector3(-ox, rim, oz), Vector3.down);
            mesh.AddQuad(new Vector3(-ox, rim, -iz), new Vector3(-ix, rim, -iz), new Vector3(-ix, rim, iz), new Vector3(-ox, rim, iz), Vector3.down);
            mesh.AddQuad(new Vector3(ix, rim, -iz), new Vector3(ox, rim, -iz), new Vector3(ox, rim, iz), new Vector3(ix, rim, iz), Vector3.down);

            // Borda de dentro, do aro até o difusor.
            mesh.AddQuad(new Vector3(ix, rim, -iz), new Vector3(ix, 0f, -iz), new Vector3(ix, 0f, iz), new Vector3(ix, rim, iz), Vector3.left);
            mesh.AddQuad(new Vector3(-ix, rim, -iz), new Vector3(-ix, 0f, -iz), new Vector3(-ix, 0f, iz), new Vector3(-ix, rim, iz), Vector3.right);
            mesh.AddQuad(new Vector3(-ix, rim, iz), new Vector3(-ix, 0f, iz), new Vector3(ix, 0f, iz), new Vector3(ix, rim, iz), Vector3.back);
            mesh.AddQuad(new Vector3(-ix, rim, -iz), new Vector3(-ix, 0f, -iz), new Vector3(ix, 0f, -iz), new Vector3(ix, rim, -iz), Vector3.forward);

            return mesh.ToMesh("Calha");
        }

        // Uma luz pontual projeta sombra em 6 faces (um cubo). Escolhe a maior resolução por
        // face que cabe no atlas de sombras em cache do HDRP e avisa se o limite de sombras
        // do asset do HDRP for pequeno demais para o número de luminárias.
        int ResolveShadowResolution(int lightCount)
        {
            const int faces = 6;
            int atlas = 2048;
            int maxRequests = 128;

            if (GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset asset)
            {
                HDShadowInitParameters shadows = asset.currentPlatformRenderPipelineSettings.hdShadowInitParams;
                atlas = shadows.cachedPunctualLightShadowAtlas;
                maxRequests = shadows.maxShadowRequests;
            }

            int requests = Mathf.Max(1, lightCount) * faces;

            if (ceilingShadows && requests > maxRequests)
                Debug.LogWarning($"[BackRooms] {lightCount} luminárias pedem {requests} sombras, mas o asset do HDRP permite {maxRequests} " +
                                 "(Lighting > Shadows > Maximum Shadows on Screen). As que passarem do limite vão vazar luz pelas paredes.");

            long budget = (long)atlas * atlas * 6 / 10;
            int resolution = Mathf.Max(32, Mathf.ClosestPowerOfTwo(maxShadowResolution));

            while (resolution > 32 && (long)resolution * resolution * requests > budget)
                resolution /= 2;

            return resolution;
        }

        // Luz pontual colada no forro: ilumina o chão e as paredes até o teto, como um
        // painel de verdade. A sombra em cache faz as paredes bloquearem a luz do cômodo ao lado.
        HDAdditionalLightData CreateCeilingLight(Transform panel, int shadowResolution)
        {
            HDAdditionalLightData hd = CreateLightObject("Luz", panel, LightType.Point, new Vector3(0f, -0.03f, 0f), out Light source);

            source.intensity = lightCandela;
            source.range = lightRange;

            hd.fadeDistance = 70f;
            hd.shadowFadeDistance = 50f;

            if (ceilingShadows)
            {
                source.shadows = LightShadows.Soft;
                hd.shadowUpdateMode = ShadowUpdateMode.OnEnable;
                hd.SetShadowResolutionOverride(true);
                hd.SetShadowResolution(shadowResolution);
            }
            else
            {
                source.shadows = LightShadows.None;
            }

            return hd;
        }

        HDAdditionalLightData CreateLightObject(string objectName, Transform parent, LightType type, Vector3 localPosition, out Light lightSource)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            lightSource = go.AddComponent<Light>();
            lightSource.type = type;

            if (!go.TryGetComponent(out HDAdditionalLightData hd))
                hd = go.AddComponent<HDAdditionalLightData>();
            HDAdditionalLightData.InitDefaultHDAdditionalLightData(hd);

            // No HDRP 17 a intensidade de luzes pontuais e spots é sempre em candela.
            lightSource.lightUnit = LightUnit.Candela;
            lightSource.color = Color.white;
            lightSource.useColorTemperature = true;
            lightSource.colorTemperature = lightTemperature;

            return hd;
        }

        /// <summary>
        /// Quad de 1 lado visível. Horizontal: no plano XZ, visto de baixo. Vertical: no plano
        /// XY, visível para quem olha no sentido +Z do objeto, com o U crescendo para a direita.
        /// </summary>
        static Mesh CreateQuadMesh(string meshName, float sizeA, float sizeB, bool horizontal)
        {
            float ha = sizeA * 0.5f;
            float hb = sizeB * 0.5f;

            var mesh = new Mesh { name = meshName };

            if (horizontal)
            {
                mesh.SetVertices(new[]
                {
                    new Vector3(-ha, 0f, -hb), new Vector3(ha, 0f, -hb),
                    new Vector3(ha, 0f, hb), new Vector3(-ha, 0f, hb)
                });
                mesh.SetNormals(new[] { Vector3.down, Vector3.down, Vector3.down, Vector3.down });
                mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            }
            else
            {
                mesh.SetVertices(new[]
                {
                    new Vector3(-ha, -hb, 0f), new Vector3(ha, -hb, 0f),
                    new Vector3(ha, hb, 0f), new Vector3(-ha, hb, 0f)
                });
                mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
                mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            }

            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // ---------------------------------------------------------------- Marcas e saída

        /// <summary>Centro (na altura do chão) e normal da face de parede que delimita a célula daquele lado.</summary>
        public static void WallFace(int x, int z, WallSide side, out Vector3 center, out Vector3 normal)
        {
            float cx = (x + 0.5f) * Cell;
            float cz = (z + 0.5f) * Cell;
            float half = WallThickness * 0.5f;

            switch (side)
            {
                case WallSide.South:
                    center = new Vector3(cx, 0f, z * Cell + half);
                    normal = Vector3.forward;
                    break;
                case WallSide.North:
                    center = new Vector3(cx, 0f, (z + 1) * Cell - half);
                    normal = Vector3.back;
                    break;
                case WallSide.West:
                    center = new Vector3(x * Cell + half, 0f, cz);
                    normal = Vector3.right;
                    break;
                default:
                    center = new Vector3((x + 1) * Cell - half, 0f, cz);
                    normal = Vector3.left;
                    break;
            }
        }

        void BuildDecorations()
        {
            GameObject root = CreateChild("Marcas");
            Transform vents = null;

            foreach (Decoration mark in Data.Decorations)
            {
                if (mark.Kind == DecorationKind.Vent)
                {
                    if (ventMaterial == null) continue;
                    if (vents == null) vents = CreateChild("Grelhas").transform;
                    AddVent(vents, mark);
                    continue;
                }

                if (mark.Side == WallSide.Floor)
                {
                    AddFloorMark(root.transform, mark);
                    continue;
                }

                Material material;
                Vector2 size;
                float centerHeight = mark.Height;

                switch (mark.Kind)
                {
                    case DecorationKind.Blood:
                        material = Pick(bloodDecals, mark.Variant);
                        size = new Vector2(1.4f, 1.6f);
                        break;
                    case DecorationKind.Text:
                        material = Pick(textDecals, mark.Variant);
                        size = new Vector2(1.8f, 0.675f);
                        break;
                    case DecorationKind.Arrow:
                        material = arrowDecal;
                        size = new Vector2(1.2f, 0.6f);
                        break;
                    case DecorationKind.WallScuff:
                        material = wallScuffDecal;
                        size = new Vector2(Mathf.Lerp(1.2f, 2.6f, mark.Scale), Mathf.Lerp(0.45f, 0.9f, mark.Scale));
                        centerHeight = Mathf.Lerp(0.6f, 1.2f, mark.Height);
                        break;
                    default:
                        material = Pick(wallStainDecals, mark.Variant);
                        WallStainLayout(mark, out size, out centerHeight);
                        break;
                }

                if (material == null) continue;

                WallFace(mark.X, mark.Z, mark.Side, out Vector3 center, out Vector3 normal);
                Vector3 tangent = Mathf.Abs(normal.z) > 0.5f ? Vector3.right : Vector3.forward;

                // Marcas grandes ficam mais perto do centro, para não sobrar para fora da parede.
                float slack = Mathf.Max(0f, Cell * 0.5f - size.x * 0.5f);
                bool isGrime = mark.Kind == DecorationKind.WallStain || mark.Kind == DecorationKind.WallScuff;
                float along = isGrime ? mark.Along * slack : mark.Along;

                // O projetor olha para dentro da parede. Começa 6 cm à frente da face e
                // avança 16 cm: pega o rodapé, mas não chega à face do outro lado.
                Quaternion rotation = Quaternion.LookRotation(-normal, Vector3.up);

                if (mark.Kind == DecorationKind.Arrow)
                {
                    // A textura da seta aponta para a direita de quem olha. Se o caminho
                    // segue para o outro lado, gira o decalque 180°.
                    var direction = new Vector3(mark.DirX, 0f, mark.DirZ);
                    if (Vector3.Dot(rotation * Vector3.right, direction) < 0f)
                        rotation *= Quaternion.Euler(0f, 0f, 180f);
                }

                Vector3 wallPosition = center + normal * 0.06f + tangent * along + Vector3.up * centerHeight;
                AddDecal(root.transform, material, wallPosition, rotation, new Vector3(size.x, size.y, 0.16f), isGrime && mark.Flip, mark.Strength);
            }
        }

        // Grelha de retorno de ar (medidas em metros).
        const float VentWidth = 0.6f;
        const float VentHeight = 0.36f;
        const float VentRim = 0.035f;
        const float VentDepth = 0.025f;
        const int VentSlats = 8;

        Mesh _ventMesh;
        Mesh _ventBackMesh;

        // Grelha de ar encostada na parede, no alto. A mesma malha em todas, com colisor dela
        // mesma: fica ao alcance da caneta (olho a 1,65 m + 2,2 m).
        void AddVent(Transform parent, Decoration mark)
        {
            WallFace(mark.X, mark.Z, mark.Side, out Vector3 center, out Vector3 normal);
            Vector3 tangent = Mathf.Abs(normal.z) > 0.5f ? Vector3.right : Vector3.forward;

            if (_ventMesh == null) _ventMesh = CreateVentMesh();

            MeshRenderer vent = CreateRenderer("Grelha", _ventMesh, ventMaterial, ShadowCastingMode.Off, parent);
            vent.transform.localPosition = center + tangent * mark.Along + Vector3.up * ventHeight;
            vent.transform.localRotation = Quaternion.LookRotation(-normal, Vector3.up);
            vent.gameObject.AddComponent<MeshCollider>().sharedMesh = _ventMesh;

            // Fundo escuro atrás das aletas: pelas frestas aparece o duto, não o papel de parede.
            if (pitVoidMaterial == null) return;
            if (_ventBackMesh == null)
                _ventBackMesh = CreateQuadMesh("FundoDaGrelha", VentWidth - VentRim * 2f, VentHeight - VentRim * 2f, horizontal: false);
            MeshRenderer back = CreateRenderer("FundoDaGrelha", _ventBackMesh, pitVoidMaterial, ShadowCastingMode.Off, vent.transform);
            back.transform.localPosition = new Vector3(0f, 0f, -0.002f);
        }

        /// <summary>
        /// Grelha de retorno de ar: aro de chapa saindo 2,5 cm da parede e aletas inclinadas
        /// para baixo, com as duas faces. No espaço da grelha: centro na origem, parede em Z = 0,
        /// frente para -Z. 72 triângulos.
        /// </summary>
        static Mesh CreateVentMesh()
        {
            var mesh = new MeshBuilder(1f);
            float ox = VentWidth * 0.5f, oy = VentHeight * 0.5f;
            float ix = ox - VentRim, iy = oy - VentRim;
            float front = -VentDepth;

            // Laterais de fora do aro.
            mesh.AddQuad(new Vector3(ox, -oy, 0f), new Vector3(ox, oy, 0f), new Vector3(ox, oy, front), new Vector3(ox, -oy, front), Vector3.right);
            mesh.AddQuad(new Vector3(-ox, -oy, 0f), new Vector3(-ox, oy, 0f), new Vector3(-ox, oy, front), new Vector3(-ox, -oy, front), Vector3.left);
            mesh.AddQuad(new Vector3(-ox, oy, 0f), new Vector3(ox, oy, 0f), new Vector3(ox, oy, front), new Vector3(-ox, oy, front), Vector3.up);
            mesh.AddQuad(new Vector3(-ox, -oy, 0f), new Vector3(ox, -oy, 0f), new Vector3(ox, -oy, front), new Vector3(-ox, -oy, front), Vector3.down);

            // Frente do aro.
            mesh.AddQuad(new Vector3(-ox, iy, front), new Vector3(ox, iy, front), new Vector3(ox, oy, front), new Vector3(-ox, oy, front), Vector3.back);
            mesh.AddQuad(new Vector3(-ox, -oy, front), new Vector3(ox, -oy, front), new Vector3(ox, -iy, front), new Vector3(-ox, -iy, front), Vector3.back);
            mesh.AddQuad(new Vector3(-ox, -iy, front), new Vector3(-ix, -iy, front), new Vector3(-ix, iy, front), new Vector3(-ox, iy, front), Vector3.back);
            mesh.AddQuad(new Vector3(ix, -iy, front), new Vector3(ox, -iy, front), new Vector3(ox, iy, front), new Vector3(ix, iy, front), Vector3.back);

            // Bordas de dentro do aro, da frente até a parede.
            mesh.AddQuad(new Vector3(-ix, iy, front), new Vector3(ix, iy, front), new Vector3(ix, iy, 0f), new Vector3(-ix, iy, 0f), Vector3.down);
            mesh.AddQuad(new Vector3(-ix, -iy, front), new Vector3(ix, -iy, front), new Vector3(ix, -iy, 0f), new Vector3(-ix, -iy, 0f), Vector3.up);
            mesh.AddQuad(new Vector3(-ix, -iy, front), new Vector3(-ix, iy, front), new Vector3(-ix, iy, 0f), new Vector3(-ix, -iy, 0f), Vector3.right);
            mesh.AddQuad(new Vector3(ix, -iy, front), new Vector3(ix, iy, front), new Vector3(ix, iy, 0f), new Vector3(ix, -iy, 0f), Vector3.left);

            // Aletas: descem da parede para a frente, uma encobrindo a fresta da de baixo.
            float pitch = iy * 2f / VentSlats;
            float drop = pitch * 0.75f;
            float back = -0.004f, tip = front + 0.004f;
            // Face de cima da aleta: aponta para cima e para fora da parede.
            Vector3 up = new Vector3(0f, back - tip, -drop).normalized;

            for (int i = 0; i < VentSlats; i++)
            {
                float top = iy - pitch * i;
                var a = new Vector3(-ix, top, back);
                var b = new Vector3(ix, top, back);
                var c = new Vector3(ix, top - drop, tip);
                var d = new Vector3(-ix, top - drop, tip);
                mesh.AddQuad(a, b, c, d, up);
                mesh.AddQuad(a, b, c, d, -up);
            }

            return mesh.ToMesh("Grelha");
        }

        // Tamanho e altura de cada tipo de mancha de parede.
        static void WallStainLayout(Decoration mark, out Vector2 size, out float centerHeight)
        {
            switch (mark.Variant)
            {
                case 0:
                    // Escorrido: encosta no teto e desce.
                    size = new Vector2(Mathf.Lerp(1.4f, 3.8f, mark.Scale), Mathf.Lerp(1.6f, WallHeight, mark.Height));
                    centerHeight = WallHeight - size.y * 0.5f;
                    break;
                case 1:
                    // Umidade: nasce no rodapé e sobe.
                    size = new Vector2(Mathf.Lerp(1.8f, 3.8f, mark.Scale), Mathf.Lerp(0.6f, 1.4f, mark.Height));
                    centerHeight = size.y * 0.5f;
                    break;
                default:
                    // Mancha solta, em qualquer altura.
                    float side = Mathf.Lerp(1.1f, 3.0f, mark.Scale);
                    size = new Vector2(side, side * 0.85f);
                    centerHeight = Mathf.Lerp(size.y * 0.5f, WallHeight - size.y * 0.5f, mark.Height);
                    break;
            }
        }

        void AddFloorMark(Transform root, Decoration mark)
        {
            Material material;
            float size;

            switch (mark.Kind)
            {
                case DecorationKind.FloorWet:
                    material = Pick(floorWetDecals, mark.Variant);
                    size = Mathf.Lerp(1.8f, 4.6f, mark.Scale);
                    break;
                case DecorationKind.FloorWear:
                    material = floorWearDecal;
                    size = Mathf.Lerp(2.2f, 5f, mark.Scale);
                    break;
                default:
                    material = Pick(poolDecals, mark.Variant);
                    size = 1.5f;
                    break;
            }

            if (material == null) return;

            var position = new Vector3((mark.X + 0.5f) * Cell + mark.Along * 1.2f, 0.1f, (mark.Z + 0.5f) * Cell + mark.Height * 1.2f);
            AddDecal(root, material, position, Quaternion.Euler(90f, mark.Yaw, 0f), new Vector3(size, size, 0.2f), mark.Flip, mark.Strength);
        }

        static Material Pick(Material[] materials, int variant)
        {
            if (materials == null || materials.Length == 0) return null;
            return materials[Mathf.Abs(variant) % materials.Length];
        }

        static void AddDecal(Transform parent, Material material, Vector3 position, Quaternion rotation, Vector3 size, bool flip = false, float strength = 0f)
        {
            var go = new GameObject("Marca");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;

            var projector = go.AddComponent<DecalProjector>();
            projector.material = material;
            projector.size = size;
            projector.pivot = new Vector3(0f, 0f, size.z * 0.5f);
            projector.drawDistance = 60f;
            projector.fadeScale = 0.85f;
            // Força zero significa que não foi informada: vale a opacidade cheia do material.
            projector.fadeFactor = strength > 0f ? Mathf.Clamp01(strength) : 1f;

            if (flip)
            {
                projector.uvScale = new Vector2(-1f, 1f);
                projector.uvBias = new Vector2(1f, 0f);
            }
        }

        // Porta escura com placa EXIT vermelha, na parede da célula mais distante do spawn.
        void BuildExit()
        {
            GameObject root = CreateChild("Saida");

            WallFace(Data.ExitX, Data.ExitZ, Data.ExitSide, out Vector3 center, out Vector3 normal);
            Quaternion facing = Quaternion.LookRotation(-normal, Vector3.up);
            root.transform.localPosition = center;
            root.transform.localRotation = facing;

            // No espaço local da saída, -Z aponta para dentro da célula.
            if (exitDoorModel != null && exitDoorModelMaterial != null)
            {
                // Porta em relevo: avança até 13 cm da parede, então tem colisor da mesma malha
                // (a caneta risca nela e ninguém atravessa a barra).
                MeshRenderer model = CreateRenderer("PortaModelo", exitDoorModel, exitDoorModelMaterial, ShadowCastingMode.On, root.transform);
                model.gameObject.AddComponent<MeshCollider>().sharedMesh = exitDoorModel;
            }
            else
            {
                MeshRenderer door = CreateRenderer("Porta", CreateQuadMesh("Porta", 0.95f, 2.05f, horizontal: false),
                                                   exitDoorMaterial, ShadowCastingMode.Off, root.transform);
                door.transform.localPosition = new Vector3(0f, 1.025f, -0.012f);
            }

            MeshRenderer sign = CreateRenderer("Placa", CreateQuadMesh("Placa", 0.56f, 0.21f, horizontal: false),
                                               exitSignMaterial, ShadowCastingMode.Off, root.transform);
            sign.transform.localPosition = new Vector3(0f, 2.3f, -0.02f);

            HDAdditionalLightData glow = CreateLightObject("Brilho", root.transform, LightType.Point, new Vector3(0f, 2.25f, -0.35f), out Light red);
            red.useColorTemperature = false;
            red.color = new Color(1f, 0.08f, 0.05f);
            red.intensity = 35f;
            red.range = 4f;
            red.shadows = LightShadows.None;
            glow.affectsVolumetric = false;

            var zone = new GameObject("Gatilho");
            zone.transform.SetParent(root.transform, false);
            zone.transform.localPosition = new Vector3(0f, 1.1f, -0.55f);

            var trigger = zone.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1.3f, 2.2f, 0.9f);
            zone.AddComponent<LevelExit>();
        }

    }
}
