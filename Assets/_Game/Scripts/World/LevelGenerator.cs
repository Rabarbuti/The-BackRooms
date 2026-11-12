using System;
using System.Collections.Generic;

namespace BackRooms.World
{
    public enum LightState : byte
    {
        None,
        On,
        Flicker,
        Dead
    }

    /// <summary>Lado de uma célula. A normal da face aponta para dentro da célula.</summary>
    public enum WallSide : byte
    {
        South,
        North,
        West,
        East,
        Floor
    }

    public enum DecorationKind : byte
    {
        Blood,
        Text,
        Arrow,
        FloorPool,

        /// <summary>Mancha de umidade ou sujeira na parede. Variante 0 = escorrido do teto, 1 = subindo do rodapé, 2 = mancha solta.</summary>
        WallStain,

        /// <summary>Riscos de atrito na parede, à altura da cintura.</summary>
        WallScuff,

        /// <summary>Trecho escuro e úmido no carpete.</summary>
        FloorWet,

        /// <summary>Trecho mais claro, gasto, no carpete.</summary>
        FloorWear,

        /// <summary>Grelha de retorno de ar na parede, perto do forro. É peça com colisor, não decalque.</summary>
        Vent
    }

    /// <summary>Uma marca na parede ou no chão: sangue, palavra rabiscada ou seta.</summary>
    public struct Decoration
    {
        public DecorationKind Kind;
        public int X;
        public int Z;
        public WallSide Side;

        /// <summary>Deslocamento em metros ao longo da parede, a partir do centro da célula.</summary>
        public float Along;

        /// <summary>No chão: segundo deslocamento. Na parede: altura do centro, em metros.</summary>
        public float Height;

        public int Variant;

        /// <summary>Só para setas: direção do mundo para onde a seta aponta.</summary>
        public int DirX;
        public int DirZ;

        /// <summary>Só para manchas no chão: rotação em graus.</summary>
        public float Yaw;

        /// <summary>Sujeira e desgaste: tamanho relativo, de 0 (menor) a 1 (maior).</summary>
        public float Scale;

        /// <summary>Sujeira e desgaste: espelha a textura, para variar.</summary>
        public bool Flip;

        /// <summary>Sujeira e desgaste: opacidade de 0 a 1. Cresce com a distância do início. Zero = usar 1.</summary>
        public float Strength;
    }

    /// <summary>Buraco no piso: um retângulo de células sem chão.</summary>
    public struct PitArea
    {
        public int X0;
        public int Z0;
        public int Width;
        public int Height;

        public bool Contains(int x, int z)
        {
            return x >= X0 && x < X0 + Width && z >= Z0 && z < Z0 + Height;
        }
    }

    /// <summary>
    /// Profundidade de uma célula nas Piscinas (Level 2). A água fica em Y = 0; o piso de cada
    /// profundidade fica na grade do azulejo de 20 cm (ver <see cref="LevelData.DepthFloor"/>).
    /// </summary>
    public enum PoolDepth : byte
    {
        /// <summary>Patamar seco, 20 cm acima da água.</summary>
        Dry,
        /// <summary>Água na canela: piso a 40 cm.</summary>
        Shallow,
        /// <summary>Água no peito: piso a 1,2 m.</summary>
        Deep,
        /// <summary>Poço: piso a 6 m, só nadando.</summary>
        Well
    }

    /// <summary>Escada da célula (X, Z), a de baixo, subindo pelo lado <see cref="Side"/> até a vizinha.</summary>
    public struct PoolStair
    {
        public int X;
        public int Z;
        public WallSide Side;
        public int Steps;
    }

    /// <summary>
    /// Descrição pura de um nível em grade. Não referencia nada do Unity, então pode ser
    /// gerada de forma idêntica em qualquer cliente a partir da mesma seed.
    /// </summary>
    public sealed class LevelData
    {
        public int Seed;
        public int Width;
        public int Height;

        /// <summary>Parede ao longo de X, na borda sul da célula (x, z). Tamanho [Width, Height + 1].</summary>
        public bool[,] WallSouth;

        /// <summary>Parede ao longo de Z, na borda oeste da célula (x, z). Tamanho [Width + 1, Height].</summary>
        public bool[,] WallWest;

        /// <summary>
        /// Parede grossa com esconderijo, no lugar da parede sul da célula (x, z).
        /// 0 = parede comum, 1 = abertura para o sul (-Z), 2 = abertura para o norte (+Z).
        /// </summary>
        public byte[,] AlcoveSouth;

        /// <summary>Idem para a parede oeste. 1 = abertura para o oeste (-X), 2 = para o leste (+X).</summary>
        public byte[,] AlcoveWest;

        /// <summary>
        /// Sala inicial: retângulo fechado de células onde os jogadores começam. As paredes
        /// dela são limpas (sem marcas) e não podem ser derrubadas nem virar esconderijo.
        /// </summary>
        public int RoomX0;
        public int RoomZ0;
        public int RoomWidth;
        public int RoomHeight;

        /// <summary>Parede que pertence à sala inicial. Mesmos índices de WallSouth / WallWest.</summary>
        public bool[,] RoomSouth;
        public bool[,] RoomWest;

        /// <summary>
        /// Vão de porta: a parede existe, mas com uma abertura. Para andar conta como
        /// passagem livre, então WallSouth / WallWest ficam falsos nesses índices.
        /// </summary>
        public bool[,] DoorSouth;
        public bool[,] DoorWest;

        /// <summary>Centro da sala inicial, em células. Pode cair num vértice da grade.</summary>
        public float SpawnCenterX => RoomX0 + RoomWidth * 0.5f;
        public float SpawnCenterZ => RoomZ0 + RoomHeight * 0.5f;

        public bool InRoom(int x, int z)
        {
            return x >= RoomX0 && x < RoomX0 + RoomWidth && z >= RoomZ0 && z < RoomZ0 + RoomHeight;
        }

        public bool IsRoomWall(int x, int z, WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return RoomSouth[x, z];
                case WallSide.North: return RoomSouth[x, z + 1];
                case WallSide.West: return RoomWest[x, z];
                case WallSide.East: return RoomWest[x + 1, z];
                default: return false;
            }
        }

        /// <summary>Pilar no vértice (x, z) da grade. Tamanho [Width + 1, Height + 1].</summary>
        public bool[,] Pillar;

        /// <summary>Luminária no teto de cada célula. Tamanho [Width, Height].</summary>
        public LightState[,] Lights;

        public int SpawnX;
        public int SpawnZ;

        /// <summary>Célula da saída: a mais distante do spawn, andando.</summary>
        public int ExitX;
        public int ExitZ;
        public WallSide ExitSide;

        public List<Decoration> Decorations = new List<Decoration>();

        /// <summary>
        /// Células do caminho mais curto do início até a saída, em ordem. Cada item é
        /// x + z * Width. É o caminho direto: atravessa os buracos pelas faixas.
        /// </summary>
        public List<int> PathCells = new List<int>();

        /// <summary>O caminho mais curto que não pisa em nenhum buraco. Sempre existe.</summary>
        public List<int> SafePathCells = new List<int>();

        /// <summary>Célula sem piso. Tamanho [Width, Height].</summary>
        public bool[,] Pit;

        /// <summary>
        /// Faixas estreitas de chão que sobraram dentro de um buraco. Em cada célula, um bit
        /// por lado (1 &lt;&lt; (int)WallSide): a faixa vai do centro da célula até o meio daquele
        /// lado. Faixas de células vizinhas se encontram na divisa.
        /// </summary>
        public byte[,] PitStrips;

        public List<PitArea> Pits = new List<PitArea>();

        // ---------------------------------------------------------------- Piscinas (Level 2)

        /// <summary>Profundidade de cada célula nas Piscinas. Nulo nos outros níveis.</summary>
        public PoolDepth[,] Depth;

        /// <summary>Escadas entre células de alturas diferentes, nas Piscinas.</summary>
        public List<PoolStair> Stairs = new List<PoolStair>();

        public bool HasPools => Depth != null;

        /// <summary>Altura de um degrau: um azulejo.</summary>
        public const float StepRise = 0.2f;

        /// <summary>Altura do piso de cada profundidade, com a água em Y = 0.</summary>
        public static float DepthFloor(PoolDepth depth)
        {
            switch (depth)
            {
                case PoolDepth.Dry: return 0.2f;
                case PoolDepth.Shallow: return -0.4f;
                case PoolDepth.Deep: return -1.2f;
                default: return -6f;
            }
        }

        /// <summary>Altura do piso da célula: 0 fora das Piscinas.</summary>
        public float FloorHeight(int x, int z) => Depth == null ? 0f : DepthFloor(Depth[x, z]);

        /// <summary>Verdadeiro se dá para passar da célula para a vizinha daquele lado (sem parede; vão de porta conta).</summary>
        public bool IsOpen(int x, int z, WallSide side) => Neighbor(x, z, side, out _, out _) && !HasWall(x, z, side);

        public bool HasStrip(int x, int z, WallSide side)
        {
            return (PitStrips[x, z] & (1 << (int)side)) != 0;
        }

        public static WallSide Opposite(WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return WallSide.North;
                case WallSide.North: return WallSide.South;
                case WallSide.West: return WallSide.East;
                default: return WallSide.West;
            }
        }

        /// <summary>Célula vizinha daquele lado. Falso se cai fora do mapa.</summary>
        public bool Neighbor(int x, int z, WallSide side, out int nx, out int nz)
        {
            nx = x + (side == WallSide.East ? 1 : side == WallSide.West ? -1 : 0);
            nz = z + (side == WallSide.North ? 1 : side == WallSide.South ? -1 : 0);
            return nx >= 0 && nz >= 0 && nx < Width && nz < Height;
        }

        /// <summary>
        /// Dá para andar da célula (x, z) para a vizinha daquele lado? Parede barra sempre.
        /// Buraco barra, a não ser que <paramref name="overStrips"/> permita atravessar pelas
        /// faixas — e aí é preciso haver faixa dos dois lados da divisa.
        /// </summary>
        public bool CanWalk(int x, int z, WallSide side, bool overStrips)
        {
            if (!Neighbor(x, z, side, out int nx, out int nz) || HasWall(x, z, side)) return false;

            bool fromPit = Pit[x, z];
            bool toPit = Pit[nx, nz];
            if (!fromPit && !toPit) return true;
            if (!overStrips) return false;

            if (fromPit && !HasStrip(x, z, side)) return false;
            return !toPit || HasStrip(nx, nz, Opposite(side));
        }

        public bool HasWall(int x, int z, WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return WallSouth[x, z];
                case WallSide.North: return WallSouth[x, z + 1];
                case WallSide.West: return WallWest[x, z];
                case WallSide.East: return WallWest[x + 1, z];
                default: return false;
            }
        }

        public bool IsAlcove(int x, int z, WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return AlcoveSouth[x, z] != 0;
                case WallSide.North: return AlcoveSouth[x, z + 1] != 0;
                case WallSide.West: return AlcoveWest[x, z] != 0;
                case WallSide.East: return AlcoveWest[x + 1, z] != 0;
                default: return false;
            }
        }
    }

    /// <summary>
    /// Gerador determinístico do Level 0: paredes soltas que não fecham salas, salões com
    /// floresta de pilares, luminárias irregulares, paredes grossas com esconderijo, uma
    /// saída distante, buracos no piso com faixas estreitas para atravessar e marcas nas
    /// paredes (sangue, palavras e umas poucas setas que apontam para a saída).
    /// Usa apenas System.Random com a seed recebida — nunca UnityEngine.Random.
    /// </summary>
    public static class LevelGenerator
    {
        const double WallChance = 0.20;
        const double WallContinueChance = 0.55;
        const double HallPillarChance = 0.60;
        const double LoosePillarChance = 0.06;
        const double LightChance = 0.42;
        const double AlcoveChance = 0.12;

        // Setas: poucas e longe umas das outras. Metade no caminho direto, o resto onde o
        // mapa ainda não tem nenhuma. Distâncias em células.
        public const int MaxArrows = 6;
        const int ArrowsOnPath = 3;
        const float ArrowMinSpacing = 3f;
        const float ArrowMinFromSpawn = 2.5f;

        // Cada luminária acesa custa 6 sombras e o HDRP está configurado para 1024: este
        // teto deixa folga para as lanternas dos jogadores.
        const int MaxLitLights = 165;

        // Buracos: um a cada tantas células do mapa, afastados da sala inicial, da saída e
        // uns dos outros (em células). O desvio é contado em células a mais de caminhada.
        const int CellsPerPit = 100;
        const int PitRoomMargin = 2;
        const int PitExitMargin = 1;
        const int PitGap = 2;
        const int PitUsefulDetour = 10;
        const int PitMaxDetour = 14;
        const double PitBranchChance = 0.35;
        const int PitMaxBranches = 2;

        static readonly (int w, int h)[] PitSizes = { (2, 2), (3, 2), (2, 3), (3, 3), (4, 2), (2, 4), (4, 3), (3, 4) };

        // O entorno do ponto de início é novo; o desgaste aparece conforme se afasta dele.
        // Distâncias em células (4 m cada), em linha reta a partir da célula inicial.
        const float CleanRadius = 1.8f;

        // Sala inicial: lado em células e número de vãos de porta.
        const int SpawnRoomSize = 2;
        const int SpawnRoomDoors = 1;

        // A sala fica perto de um canto sorteado, a esta distância (em células) das duas
        // bordas mais próximas, mais um sorteio de até SpawnEdgeSpread. Como a saída é a
        // célula mais distante do início, ela vai parar do outro lado do mapa.
        const int SpawnEdgeMin = 3;
        const int SpawnEdgeSpread = 3;
        const float WornRadius = 7f;

        // Sangue, palavras e poças só aparecem onde o desgaste já passou deste valor.
        const float MarkMinWear = 0.35f;

        public const int BloodVariants = 3;
        public const int TextVariants = 3;
        public const int PoolVariants = 2;
        public const int WallStainVariants = 3;
        public const int FloorWetVariants = 2;

        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

        /// <summary>Desgaste da célula, de 0 (nova, perto do início) a 1 (totalmente gasta).</summary>
        public static float WearAt(LevelData d, int x, int z)
        {
            // Distância do centro da célula ao centro da sala inicial.
            float dx = x + 0.5f - d.SpawnCenterX;
            float dz = z + 0.5f - d.SpawnCenterZ;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            float t = Math.Min(1f, Math.Max(0f, (distance - CleanRadius) / (WornRadius - CleanRadius)));
            return t * t * (3f - 2f * t);
        }

        /// <param name="pits">Falso no Level 1: sem buracos (lá o perigo do chão são os móveis).</param>
        /// <param name="alcoves">Falso no Level 1: sem nichos nas paredes (os móveis encostam nelas).</param>
        /// <param name="pools">Verdadeiro nas Piscinas (Level 2): profundidade por célula e escadas,
        /// sorteadas na última etapa, depois de tudo (não muda o que vem antes por seed).</param>
        public static LevelData Generate(int seed, int width, int height, bool pits = true, bool alcoves = true, bool pools = false)
        {
            width = Math.Max(4, width);
            height = Math.Max(4, height);

            var rng = new Random(seed);
            var data = new LevelData
            {
                Seed = seed,
                Width = width,
                Height = height,
                WallSouth = new bool[width, height + 1],
                WallWest = new bool[width + 1, height],
                AlcoveSouth = new byte[width, height + 1],
                AlcoveWest = new byte[width + 1, height],
                RoomSouth = new bool[width, height + 1],
                RoomWest = new bool[width + 1, height],
                DoorSouth = new bool[width, height + 1],
                DoorWest = new bool[width + 1, height],
                Pillar = new bool[width + 1, height + 1],
                Lights = new LightState[width, height],
                Pit = new bool[width, height],
                PitStrips = new byte[width, height],
                SpawnX = width / 2,
                SpawnZ = height / 2
            };

            // A ordem das etapas define a ordem de consumo do gerador: não reordenar,
            // senão a mesma seed passa a produzir outro mapa.
            PlaceSpawn(data, rng);
            ScatterWalls(data, rng);
            CarveHalls(data, rng);
            CarveSpawnRoom(data, rng);
            CloseBoundary(data);
            ConnectEverything(data);
            ScatterPillars(data, rng);
            ScatterLights(data, rng);
            FinishSpawnRoom(data);
            LimitLights(data, rng);
            // Desligar nichos ou buracos muda o que o gerador consome dali em diante; com os dois
            // ligados (o padrão, o Level 0) a mesma seed continua dando o mesmo mapa.
            if (alcoves) ScatterAlcoves(data, rng);

            FindExit(data);
            if (pits) DigPits(data, rng);
            data.PathCells = ShortestPath(data, overStrips: true);
            data.SafePathCells = ShortestPath(data, overStrips: false);

            var usedFaces = new HashSet<int> { FaceKey(data, data.ExitX, data.ExitZ, data.ExitSide) };

            PlaceArrows(data, rng, usedFaces);
            PlaceWallMarks(data, rng, usedFaces);
            PlaceFloorPools(data, rng);
            PlaceGrime(data, rng);
            // Última etapa: sorteia depois de tudo, então não muda nada do que já existia por seed.
            PlaceVents(data, rng, usedFaces);
            if (pools) PlacePools(data, rng);

            return data;
        }

        // Ponto de início: fora do centro, perto de um dos quatro cantos. Em mapa pequeno
        // demais para isso, fica no meio.
        static void PlaceSpawn(LevelData d, Random rng)
        {
            bool east = rng.Next(2) == 0;
            bool north = rng.Next(2) == 0;
            int offsetX = SpawnEdgeMin + rng.Next(SpawnEdgeSpread + 1);
            int offsetZ = SpawnEdgeMin + rng.Next(SpawnEdgeSpread + 1);

            offsetX = Math.Min(offsetX, d.Width / 2);
            offsetZ = Math.Min(offsetZ, d.Height / 2);

            d.SpawnX = east ? d.Width - offsetX : offsetX;
            d.SpawnZ = north ? d.Height - offsetZ : offsetZ;
        }

        // Paredes internas com tendência a continuar em linha, para formar trechos longos.
        static void ScatterWalls(LevelData d, Random rng)
        {
            for (int z = 1; z < d.Height; z++)
            {
                for (int x = 0; x < d.Width; x++)
                {
                    bool previous = x > 0 && d.WallSouth[x - 1, z];
                    d.WallSouth[x, z] = rng.NextDouble() < (previous ? WallContinueChance : WallChance);
                }
            }

            for (int x = 1; x < d.Width; x++)
            {
                for (int z = 0; z < d.Height; z++)
                {
                    bool previous = z > 0 && d.WallWest[x, z - 1];
                    d.WallWest[x, z] = rng.NextDouble() < (previous ? WallContinueChance : WallChance);
                }
            }
        }

        // Salões abertos: retângulos sem paredes internas, com pilares nos vértices.
        static void CarveHalls(LevelData d, Random rng)
        {
            int count = Math.Max(1, d.Width * d.Height / 64);

            for (int i = 0; i < count; i++)
            {
                int w = Math.Min(d.Width - 2, 3 + rng.Next(3));
                int h = Math.Min(d.Height - 2, 3 + rng.Next(3));
                int x0 = 1 + rng.Next(Math.Max(1, d.Width - w - 1));
                int z0 = 1 + rng.Next(Math.Max(1, d.Height - h - 1));

                for (int z = z0 + 1; z < z0 + h; z++)
                    for (int x = x0; x < x0 + w; x++)
                        d.WallSouth[x, z] = false;

                for (int x = x0 + 1; x < x0 + w; x++)
                    for (int z = z0; z < z0 + h; z++)
                        d.WallWest[x, z] = false;

                for (int z = z0 + 1; z < z0 + h; z++)
                    for (int x = x0 + 1; x < x0 + w; x++)
                        d.Pillar[x, z] = rng.NextDouble() < HallPillarChance;
            }
        }

        // Sala inicial: 2 x 2 células (8 x 8 m) fechadas por paredes, com o ponto de início
        // no centro e um único vão de porta, sorteado entre os trechos do contorno.
        static void CarveSpawnRoom(LevelData d, Random rng)
        {
            d.RoomWidth = SpawnRoomSize;
            d.RoomHeight = SpawnRoomSize;
            d.RoomX0 = d.SpawnX - SpawnRoomSize / 2;
            d.RoomZ0 = d.SpawnZ - SpawnRoomSize / 2;

            int x0 = d.RoomX0, z0 = d.RoomZ0;
            int x1 = x0 + d.RoomWidth, z1 = z0 + d.RoomHeight;

            // Por dentro, nenhuma parede.
            for (int z = z0 + 1; z < z1; z++)
                for (int x = x0; x < x1; x++)
                    d.WallSouth[x, z] = false;

            for (int x = x0 + 1; x < x1; x++)
                for (int z = z0; z < z1; z++)
                    d.WallWest[x, z] = false;

            // Contorno inteiro fechado.
            var edges = new List<(bool south, int x, int z)>();
            for (int x = x0; x < x1; x++)
            {
                edges.Add((true, x, z0));
                edges.Add((true, x, z1));
            }
            for (int z = z0; z < z1; z++)
            {
                edges.Add((false, x0, z));
                edges.Add((false, x1, z));
            }

            foreach (var (south, x, z) in edges)
            {
                if (south) { d.WallSouth[x, z] = true; d.RoomSouth[x, z] = true; }
                else { d.WallWest[x, z] = true; d.RoomWest[x, z] = true; }
            }

            for (int i = 0; i < SpawnRoomDoors && edges.Count > 0; i++)
            {
                int pick = rng.Next(edges.Count);
                var (south, x, z) = edges[pick];
                edges.RemoveAt(pick);

                if (south) { d.WallSouth[x, z] = false; d.DoorSouth[x, z] = true; }
                else { d.WallWest[x, z] = false; d.DoorWest[x, z] = true; }
            }
        }

        // Depois de pilares e luzes sorteados: a sala fica sem pilares e com todas as luzes acesas.
        static void FinishSpawnRoom(LevelData d)
        {
            for (int z = d.RoomZ0; z <= d.RoomZ0 + d.RoomHeight; z++)
                for (int x = d.RoomX0; x <= d.RoomX0 + d.RoomWidth; x++)
                    d.Pillar[x, z] = false;

            for (int z = d.RoomZ0; z < d.RoomZ0 + d.RoomHeight; z++)
                for (int x = d.RoomX0; x < d.RoomX0 + d.RoomWidth; x++)
                    d.Lights[x, z] = LightState.On;
        }

        static void CloseBoundary(LevelData d)
        {
            for (int x = 0; x < d.Width; x++)
            {
                d.WallSouth[x, 0] = true;
                d.WallSouth[x, d.Height] = true;
            }

            for (int z = 0; z < d.Height; z++)
            {
                d.WallWest[0, z] = true;
                d.WallWest[d.Width, z] = true;
            }
        }

        // Garante que toda célula é alcançável a partir do spawn, derrubando paredes
        // entre a região já alcançada e as células isoladas.
        static void ConnectEverything(LevelData d)
        {
            var visited = new bool[d.Width, d.Height];
            var stack = new Stack<(int x, int z)>();

            Flood(d, visited, stack, d.SpawnX, d.SpawnZ);

            bool changed = true;
            while (changed)
            {
                changed = false;

                for (int z = 0; z < d.Height; z++)
                {
                    for (int x = 0; x < d.Width; x++)
                    {
                        if (visited[x, z]) continue;

                        // As paredes da sala inicial ficam de fora: ela só se liga pelo vão de porta.
                        if (x > 0 && visited[x - 1, z] && !d.RoomWest[x, z]) d.WallWest[x, z] = false;
                        else if (x < d.Width - 1 && visited[x + 1, z] && !d.RoomWest[x + 1, z]) d.WallWest[x + 1, z] = false;
                        else if (z > 0 && visited[x, z - 1] && !d.RoomSouth[x, z]) d.WallSouth[x, z] = false;
                        else if (z < d.Height - 1 && visited[x, z + 1] && !d.RoomSouth[x, z + 1]) d.WallSouth[x, z + 1] = false;
                        else continue;

                        Flood(d, visited, stack, x, z);
                        changed = true;
                    }
                }
            }
        }

        static void Flood(LevelData d, bool[,] visited, Stack<(int x, int z)> stack, int startX, int startZ)
        {
            visited[startX, startZ] = true;
            stack.Push((startX, startZ));

            while (stack.Count > 0)
            {
                var (x, z) = stack.Pop();

                if (x > 0 && !d.WallWest[x, z]) Visit(visited, stack, x - 1, z);
                if (x < d.Width - 1 && !d.WallWest[x + 1, z]) Visit(visited, stack, x + 1, z);
                if (z > 0 && !d.WallSouth[x, z]) Visit(visited, stack, x, z - 1);
                if (z < d.Height - 1 && !d.WallSouth[x, z + 1]) Visit(visited, stack, x, z + 1);
            }
        }

        static void Visit(bool[,] visited, Stack<(int x, int z)> stack, int x, int z)
        {
            if (visited[x, z]) return;
            visited[x, z] = true;
            stack.Push((x, z));
        }

        // Pilares soltos em vértices onde nenhuma parede encosta.
        static void ScatterPillars(LevelData d, Random rng)
        {
            for (int z = 1; z < d.Height; z++)
            {
                for (int x = 1; x < d.Width; x++)
                {
                    bool roll = rng.NextDouble() < LoosePillarChance;
                    if (d.Pillar[x, z]) continue;

                    bool touchesWall = d.WallSouth[x - 1, z] || d.WallSouth[x, z] ||
                                       d.WallWest[x, z - 1] || d.WallWest[x, z];

                    d.Pillar[x, z] = roll && !touchesWall;
                }
            }
        }

        static void ScatterLights(LevelData d, Random rng)
        {
            for (int z = 0; z < d.Height; z++)
            {
                for (int x = 0; x < d.Width; x++)
                {
                    bool hasLight = rng.NextDouble() < LightChance;
                    double kind = rng.NextDouble();

                    if (!hasLight) d.Lights[x, z] = LightState.None;
                    else if (kind < 0.80) d.Lights[x, z] = LightState.On;
                    else if (kind < 0.90) d.Lights[x, z] = LightState.Flicker;
                    else d.Lights[x, z] = LightState.Dead;
                }
            }

            d.Lights[d.SpawnX, d.SpawnZ] = LightState.On;

            // Na área nova em volta do início não há lâmpada queimada nem piscando.
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    if (d.Lights[x, z] != LightState.None && WearAt(d, x, z) <= 0f)
                        d.Lights[x, z] = LightState.On;
        }

        // Apaga luminárias sorteadas até caber no limite de sombras. Nunca as da sala inicial.
        static void LimitLights(LevelData d, Random rng)
        {
            int lit = 0;
            foreach (LightState state in d.Lights)
                if (state == LightState.On || state == LightState.Flicker) lit++;

            for (int attempt = 0; lit > MaxLitLights && attempt < 20000; attempt++)
            {
                int x = rng.Next(d.Width);
                int z = rng.Next(d.Height);
                LightState state = d.Lights[x, z];

                if ((state != LightState.On && state != LightState.Flicker) || d.InRoom(x, z)) continue;

                d.Lights[x, z] = LightState.None;
                lit--;
            }
        }

        // Algumas paredes internas viram paredes grossas com um nicho para se esconder.
        static void ScatterAlcoves(LevelData d, Random rng)
        {
            for (int z = 1; z < d.Height; z++)
            {
                for (int x = 0; x < d.Width; x++)
                {
                    bool roll = rng.NextDouble() < AlcoveChance;
                    byte opening = (byte)(1 + rng.Next(2));
                    if (roll && d.WallSouth[x, z] && !d.RoomSouth[x, z]) d.AlcoveSouth[x, z] = opening;
                }
            }

            for (int x = 1; x < d.Width; x++)
            {
                for (int z = 0; z < d.Height; z++)
                {
                    bool roll = rng.NextDouble() < AlcoveChance;
                    byte opening = (byte)(1 + rng.Next(2));
                    if (roll && d.WallWest[x, z] && !d.RoomWest[x, z]) d.AlcoveWest[x, z] = opening;
                }
            }
        }

        // A saída fica na célula mais distante do início, andando, que tenha uma parede
        // comum onde encaixar a porta. É escolhida antes de existirem buracos.
        static void FindExit(LevelData d)
        {
            int[,] distance = Distances(d, d.SpawnX, d.SpawnZ, false, null, null);

            int best = -1;
            d.ExitX = d.SpawnX;
            d.ExitZ = d.SpawnZ;
            d.ExitSide = WallSide.South;

            for (int z = 0; z < d.Height; z++)
            {
                for (int x = 0; x < d.Width; x++)
                {
                    if (distance[x, z] <= best) continue;

                    foreach (WallSide side in Sides)
                    {
                        if (!d.HasWall(x, z, side) || d.IsAlcove(x, z, side) || d.IsRoomWall(x, z, side)) continue;

                        best = distance[x, z];
                        d.ExitX = x;
                        d.ExitZ = z;
                        d.ExitSide = side;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Busca em largura: passos de (startX, startZ) até cada célula, -1 se não alcança.
        /// Se <paramref name="parent"/> for dado, recebe a célula anterior no caminho mais
        /// curto (-1 na origem). <paramref name="distance"/> permite reaproveitar a matriz.
        /// </summary>
        static int[,] Distances(LevelData d, int startX, int startZ, bool overStrips, int[,] parent, int[,] distance)
        {
            if (distance == null) distance = new int[d.Width, d.Height];

            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    distance[x, z] = -1;
                    if (parent != null) parent[x, z] = -1;
                }

            var queue = new Queue<(int x, int z)>();
            distance[startX, startZ] = 0;
            queue.Enqueue((startX, startZ));

            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();

                foreach (WallSide side in Sides)
                {
                    if (!d.CanWalk(x, z, side, overStrips)) continue;

                    d.Neighbor(x, z, side, out int nx, out int nz);
                    if (distance[nx, nz] >= 0) continue;

                    distance[nx, nz] = distance[x, z] + 1;
                    if (parent != null) parent[nx, nz] = x + z * d.Width;
                    queue.Enqueue((nx, nz));
                }
            }

            return distance;
        }

        /// <summary>Caminho mais curto do início até a saída, em ordem. Vazio se não há.</summary>
        static List<int> ShortestPath(LevelData d, bool overStrips)
        {
            var parent = new int[d.Width, d.Height];
            int[,] distance = Distances(d, d.SpawnX, d.SpawnZ, overStrips, parent, null);

            var path = new List<int>();
            if (distance[d.ExitX, d.ExitZ] < 0) return path;

            int cell = d.ExitX + d.ExitZ * d.Width;
            while (cell >= 0)
            {
                path.Add(cell);
                cell = parent[cell % d.Width, cell / d.Width];
            }

            path.Reverse();
            return path;
        }

        // ---------------------------------------------------------------- Buracos

        // Abre buracos grandes no piso, um de cada vez, de preferência em cima do caminho
        // seguro mais curto que existir naquele momento. Quem segue em frente dá de cara
        // com o buraco e escolhe: atravessar pelas faixas estreitas ou procurar outro
        // caminho. Entre os lugares possíveis, fica o que mais alonga o desvio. Quando não
        // cabe mais nenhum no caminho, o buraco vai para outro canto do mapa. Um buraco
        // nunca isola parte do mapa: toda célula com piso continua alcançável sem faixa.
        static void DigPits(LevelData d, Random rng)
        {
            int target = Math.Max(1, d.Width * d.Height / CellsPerPit);
            var distance = new int[d.Width, d.Height];
            var onPath = new bool[d.Width, d.Height];

            for (int n = 0; n < target; n++)
            {
                List<int> path = ShortestPath(d, overStrips: false);
                if (path.Count < 6) break;

                Array.Clear(onPath, 0, onPath.Length);
                foreach (int cell in path) onPath[cell % d.Width, cell / d.Width] = true;

                bool found = false;
                double bestScore = 0.0;
                PitArea best = default;

                // Primeira passada: só lugares que cortam o caminho. Segunda, se preciso: qualquer um.
                for (int pass = 0; pass < 2 && !found; pass++)
                {
                    foreach (var (w, h) in PitSizes)
                    {
                        for (int z0 = 0; z0 + h <= d.Height; z0++)
                        {
                            for (int x0 = 0; x0 + w <= d.Width; x0++)
                            {
                                // Sorteia sempre, para a sequência não depender de quem passa nos testes.
                                double jitter = rng.NextDouble();
                                var area = new PitArea { X0 = x0, Z0 = z0, Width = w, Height = h };

                                if (!PitAllowed(d, area)) continue;
                                if (pass == 0 && !Touches(area, onPath)) continue;

                                MarkPit(d, area, true);
                                int detour = SafeDetour(d, distance, path.Count);
                                // Fora do caminho, precisa de duas entradas para haver o que atravessar.
                                bool crossable = pass == 0 || Entries(d, area).Count >= 2;
                                MarkPit(d, area, false);

                                // Negativo: o buraco isolaria alguma célula. Desvio longo demais
                                // também não serve: tiraria a graça de procurar outro caminho.
                                if (detour < 0 || detour > PitMaxDetour || !crossable) continue;

                                double score = Math.Min(detour, PitUsefulDetour) + w * h * 0.25 + jitter * 2.0;
                                if (found && score <= bestScore) continue;

                                found = true;
                                bestScore = score;
                                best = area;
                            }
                        }
                    }
                }

                if (!found) break;
                OpenPit(d, rng, best, path);
            }
        }

        static bool PitAllowed(LevelData d, PitArea area)
        {
            if (Near(area, PitRoomMargin, d.RoomX0, d.RoomZ0, d.RoomWidth, d.RoomHeight)) return false;
            if (Near(area, PitExitMargin, d.ExitX, d.ExitZ, 1, 1)) return false;

            foreach (PitArea other in d.Pits)
                if (Near(area, PitGap, other.X0, other.Z0, other.Width, other.Height)) return false;

            return true;
        }

        // Verdadeiro se o retângulo dado encosta na área aumentada de 'margin' células para cada lado.
        static bool Near(PitArea area, int margin, int x0, int z0, int width, int height)
        {
            return x0 < area.X0 + area.Width + margin && x0 + width > area.X0 - margin &&
                   z0 < area.Z0 + area.Height + margin && z0 + height > area.Z0 - margin;
        }

        static bool Touches(PitArea area, bool[,] cells)
        {
            for (int z = area.Z0; z < area.Z0 + area.Height; z++)
                for (int x = area.X0; x < area.X0 + area.Width; x++)
                    if (cells[x, z]) return true;

            return false;
        }

        static void MarkPit(LevelData d, PitArea area, bool pit)
        {
            for (int z = area.Z0; z < area.Z0 + area.Height; z++)
                for (int x = area.X0; x < area.X0 + area.Width; x++)
                    d.Pit[x, z] = pit;
        }

        // Quantas células o caminho seguro até a saída ficou mais longo. -1 se alguma
        // célula com piso deixou de ser alcançável sem atravessar buraco.
        static int SafeDetour(LevelData d, int[,] distance, int lengthBefore)
        {
            Distances(d, d.SpawnX, d.SpawnZ, false, null, distance);

            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    if (!d.Pit[x, z] && distance[x, z] < 0) return -1;

            return distance[d.ExitX, d.ExitZ] + 1 - lengthBefore;
        }

        // Abre o buraco de vez: tira o que havia dentro dele e deixa as faixas de travessia.
        static void OpenPit(LevelData d, Random rng, PitArea area, List<int> path)
        {
            int x0 = area.X0, z0 = area.Z0;
            int x1 = x0 + area.Width, z1 = z0 + area.Height;

            MarkPit(d, area, true);
            d.Pits.Add(area);

            // Por dentro não sobra parede nem esconderijo.
            for (int z = z0 + 1; z < z1; z++)
                for (int x = x0; x < x1; x++)
                {
                    d.WallSouth[x, z] = false;
                    d.AlcoveSouth[x, z] = 0;
                }

            for (int x = x0 + 1; x < x1; x++)
                for (int z = z0; z < z1; z++)
                {
                    d.WallWest[x, z] = false;
                    d.AlcoveWest[x, z] = 0;
                }

            // Na beirada, parede grossa vira parede comum: o nicho ficaria em cima do vazio.
            for (int x = x0; x < x1; x++)
            {
                d.AlcoveSouth[x, z0] = 0;
                d.AlcoveSouth[x, z1] = 0;
            }

            for (int z = z0; z < z1; z++)
            {
                d.AlcoveWest[x0, z] = 0;
                d.AlcoveWest[x1, z] = 0;
            }

            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    d.Pillar[x, z] = false;

            // Faixa principal: entra por onde o caminho entrava e sai por onde ele saía.
            int first = -1, last = -1;
            for (int i = 0; i < path.Count; i++)
            {
                if (!area.Contains(path[i] % d.Width, path[i] / d.Width)) continue;
                if (first < 0) first = i;
                last = i;
            }

            bool trunkXFirst = rng.Next(2) == 0;

            if (first >= 0)
            {
                // O início e a saída ficam sempre fora dos buracos, então há célula antes e depois.
                int ax = path[first] % d.Width, az = path[first] / d.Width;
                int bx = path[last] % d.Width, bz = path[last] / d.Width;

                AddStrip(d, ax, az, SideToward(d, path[first], path[first - 1]));
                AddStrip(d, bx, bz, SideToward(d, path[last], path[last + 1]));
                LayStrip(d, trunkXFirst, ax, az, bx, bz);
            }
            else
            {
                // Buraco fora do caminho: liga as duas entradas mais distantes uma da outra.
                List<(int x, int z, WallSide side)> entries = Entries(d, area);
                int from = 0, to = 0, widest = -1;

                for (int i = 0; i < entries.Count; i++)
                    for (int j = i + 1; j < entries.Count; j++)
                    {
                        int span = Math.Abs(entries[i].x - entries[j].x) + Math.Abs(entries[i].z - entries[j].z);
                        if (span <= widest) continue;

                        widest = span;
                        from = i;
                        to = j;
                    }

                if (entries.Count > 0)
                {
                    AddStrip(d, entries[from].x, entries[from].z, entries[from].side);
                    AddStrip(d, entries[to].x, entries[to].z, entries[to].side);
                    LayStrip(d, trunkXFirst, entries[from].x, entries[from].z, entries[to].x, entries[to].z);
                }
            }

            // Ramais: algumas das outras entradas do buraco também ganham faixa, ligada à
            // faixa mais próxima. Sorteia para toda divisa, na mesma ordem, aproveite ou não.
            int branches = 0;

            for (int z = z0; z < z1; z++)
            {
                for (int x = x0; x < x1; x++)
                {
                    foreach (WallSide side in Sides)
                    {
                        if (!d.Neighbor(x, z, side, out int nx, out int nz) || d.Pit[nx, nz]) continue;

                        bool roll = rng.NextDouble() < PitBranchChance;
                        bool xFirst = rng.Next(2) == 0;

                        if (!roll || branches >= PitMaxBranches) continue;
                        if (d.HasWall(x, z, side) || d.HasStrip(x, z, side)) continue;
                        if (!NearestStrip(d, area, x, z, out int tx, out int tz)) continue;

                        AddStrip(d, x, z, side);
                        LayStrip(d, xFirst, x, z, tx, tz);
                        branches++;
                    }
                }
            }
        }

        // Por onde se chega ao buraco: divisas sem parede entre uma célula dele e uma de piso.
        static List<(int x, int z, WallSide side)> Entries(LevelData d, PitArea area)
        {
            var entries = new List<(int x, int z, WallSide side)>();

            for (int z = area.Z0; z < area.Z0 + area.Height; z++)
                for (int x = area.X0; x < area.X0 + area.Width; x++)
                    foreach (WallSide side in Sides)
                        if (d.Neighbor(x, z, side, out int nx, out int nz) && !d.Pit[nx, nz] && !d.HasWall(x, z, side))
                            entries.Add((x, z, side));

            return entries;
        }

        static WallSide SideToward(LevelData d, int from, int to)
        {
            int dx = to % d.Width - from % d.Width;
            int dz = to / d.Width - from / d.Width;

            if (dx > 0) return WallSide.East;
            if (dx < 0) return WallSide.West;
            return dz > 0 ? WallSide.North : WallSide.South;
        }

        static void AddStrip(LevelData d, int x, int z, WallSide side)
        {
            d.PitStrips[x, z] |= (byte)(1 << (int)side);
        }

        // Faixa em L de uma célula a outra, dentro do mesmo buraco.
        static void LayStrip(LevelData d, bool xFirst, int ax, int az, int bx, int bz)
        {
            int x = ax, z = az;

            for (int leg = 0; leg < 2; leg++)
            {
                bool alongX = (leg == 0) == xFirst;

                while (alongX ? x != bx : z != bz)
                {
                    WallSide side = alongX ? (bx > x ? WallSide.East : WallSide.West)
                                           : (bz > z ? WallSide.North : WallSide.South);
                    d.Neighbor(x, z, side, out int nx, out int nz);

                    AddStrip(d, x, z, side);
                    AddStrip(d, nx, nz, LevelData.Opposite(side));
                    x = nx;
                    z = nz;
                }
            }
        }

        // A célula com faixa mais próxima de (x, z) dentro do buraco, sem contar ela mesma.
        static bool NearestStrip(LevelData d, PitArea area, int x, int z, out int tx, out int tz)
        {
            tx = x;
            tz = z;
            int best = int.MaxValue;

            for (int cz = area.Z0; cz < area.Z0 + area.Height; cz++)
            {
                for (int cx = area.X0; cx < area.X0 + area.Width; cx++)
                {
                    if (d.PitStrips[cx, cz] == 0 || (cx == x && cz == z)) continue;

                    int distance = Math.Abs(cx - x) + Math.Abs(cz - z);
                    if (distance >= best) continue;

                    best = distance;
                    tx = cx;
                    tz = cz;
                }
            }

            return best != int.MaxValue;
        }

        static int FaceKey(LevelData d, int x, int z, WallSide side)
        {
            return (x + z * d.Width) * 4 + (int)side;
        }

        // Setas rabiscadas na parede: no máximo MaxArrows, longe umas das outras. As
        // primeiras ficam no caminho direto, a intervalos iguais; as demais vão para onde o
        // mapa ainda não tem nenhuma. Toda seta aponta o próximo passo do caminho mais
        // curto até a saída a partir de onde está — que pode passar por cima de um buraco.
        static void PlaceArrows(LevelData d, Random rng, HashSet<int> usedFaces)
        {
            int[,] toExit = Distances(d, d.ExitX, d.ExitZ, true, null, null);
            var placed = new List<(int x, int z)>();

            int count = d.PathCells.Count;
            int window = Math.Max(1, count / (ArrowsOnPath * 2));

            for (int i = 0; i < ArrowsOnPath && count > 2; i++)
            {
                int target = count * (2 * i + 1) / (ArrowsOnPath * 2);
                bool done = false;

                // A célula do alvo ou, se ela não servir, a mais próxima dela no caminho.
                for (int offset = 0; offset <= window && !done; offset++)
                {
                    for (int sign = 1; sign >= -1 && !done; sign -= 2)
                    {
                        int index = target + sign * offset;
                        if (index < 1 || index > count - 2 || (offset == 0 && sign < 0)) continue;

                        int x = d.PathCells[index] % d.Width, z = d.PathCells[index] / d.Width;
                        int nx = d.PathCells[index + 1] % d.Width, nz = d.PathCells[index + 1] / d.Width;

                        if (TooClose(placed, x, z, ArrowMinSpacing)) continue;
                        done = TryPlaceArrow(d, rng, usedFaces, placed, x, z, nx - x, nz - z);
                    }
                }
            }

            while (placed.Count < MaxArrows)
            {
                // A célula mais afastada de todas as setas já postas, do início e da saída.
                float bestGap = ArrowMinSpacing;
                int bestX = -1, bestZ = -1, bestDirX = 0, bestDirZ = 0;

                for (int z = 0; z < d.Height; z++)
                {
                    for (int x = 0; x < d.Width; x++)
                    {
                        if (toExit[x, z] < 2 || !StepToExit(d, toExit, x, z, out int dirX, out int dirZ)) continue;
                        if (!ArrowFits(d, usedFaces, x, z, dirX, dirZ)) continue;

                        float gap = Math.Min(Gap(x, z, d.ExitX, d.ExitZ), Gap(x, z, d.SpawnX, d.SpawnZ));
                        foreach (var (px, pz) in placed) gap = Math.Min(gap, Gap(x, z, px, pz));

                        if (gap <= bestGap) continue;

                        bestGap = gap;
                        bestX = x;
                        bestZ = z;
                        bestDirX = dirX;
                        bestDirZ = dirZ;
                    }
                }

                if (bestX < 0 || !TryPlaceArrow(d, rng, usedFaces, placed, bestX, bestZ, bestDirX, bestDirZ)) break;
            }
        }

        static float Gap(int x, int z, int otherX, int otherZ)
        {
            float dx = x - otherX, dz = z - otherZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        static bool TooClose(List<(int x, int z)> placed, int x, int z, float spacing)
        {
            foreach (var (px, pz) in placed)
                if (Gap(x, z, px, pz) < spacing) return true;

            return false;
        }

        // Para que lado se dá o próximo passo rumo à saída, a partir da célula (x, z).
        static bool StepToExit(LevelData d, int[,] toExit, int x, int z, out int dirX, out int dirZ)
        {
            dirX = 0;
            dirZ = 0;
            if (toExit[x, z] <= 0) return false;

            foreach (WallSide side in Sides)
            {
                if (!d.CanWalk(x, z, side, true)) continue;

                d.Neighbor(x, z, side, out int nx, out int nz);
                if (toExit[nx, nz] != toExit[x, z] - 1) continue;

                dirX = nx - x;
                dirZ = nz - z;
                return true;
            }

            return false;
        }

        // A seta fica numa parede paralela ao sentido de marcha. Não vai em célula de
        // buraco, na sala inicial nem colada nela.
        static bool ArrowFits(LevelData d, HashSet<int> usedFaces, int x, int z, int dirX, int dirZ)
        {
            if (d.Pit[x, z] || d.InRoom(x, z)) return false;

            float fromSpawnX = x + 0.5f - d.SpawnCenterX, fromSpawnZ = z + 0.5f - d.SpawnCenterZ;
            if (fromSpawnX * fromSpawnX + fromSpawnZ * fromSpawnZ < ArrowMinFromSpawn * ArrowMinFromSpawn) return false;

            WallSide a = dirX != 0 ? WallSide.South : WallSide.West;
            WallSide b = dirX != 0 ? WallSide.North : WallSide.East;
            return IsFreeFace(d, x, z, a, usedFaces) || IsFreeFace(d, x, z, b, usedFaces);
        }

        static bool TryPlaceArrow(LevelData d, Random rng, HashSet<int> usedFaces, List<(int x, int z)> placed,
                                  int x, int z, int dirX, int dirZ)
        {
            if (!ArrowFits(d, usedFaces, x, z, dirX, dirZ)) return false;

            bool preferFirst = rng.Next(2) == 0;
            float along = (float)(rng.NextDouble() * 1.2 - 0.6);
            float height = 1.25f + (float)rng.NextDouble() * 0.35f;

            WallSide a = dirX != 0 ? WallSide.South : WallSide.West;
            WallSide b = dirX != 0 ? WallSide.North : WallSide.East;
            if (!preferFirst) { WallSide t = a; a = b; b = t; }

            WallSide side = IsFreeFace(d, x, z, a, usedFaces) ? a : b;

            usedFaces.Add(FaceKey(d, x, z, side));
            placed.Add((x, z));
            d.Decorations.Add(new Decoration
            {
                Kind = DecorationKind.Arrow,
                X = x,
                Z = z,
                Side = side,
                Along = along,
                Height = height,
                DirX = dirX,
                DirZ = dirZ
            });
            return true;
        }

        static bool IsFreeFace(LevelData d, int x, int z, WallSide side, HashSet<int> usedFaces)
        {
            return d.HasWall(x, z, side) && !d.IsAlcove(x, z, side) && !d.IsRoomWall(x, z, side) &&
                   !usedFaces.Contains(FaceKey(d, x, z, side));
        }

        // Sangue preto escorrido e palavras rabiscadas, em paredes livres sorteadas.
        static void PlaceWallMarks(LevelData d, Random rng, HashSet<int> usedFaces)
        {
            int bloodCount = Math.Max(3, d.Width * d.Height / 14);
            int textCount = Math.Max(2, d.Width * d.Height / 45);

            PlaceRandomMarks(d, rng, usedFaces, DecorationKind.Blood, bloodCount, BloodVariants, 0.9f, 1.45f, 0.25f);
            PlaceRandomMarks(d, rng, usedFaces, DecorationKind.Text, textCount, TextVariants, 0.6f, 1.45f, 0.25f);
        }

        static void PlaceRandomMarks(LevelData d, Random rng, HashSet<int> usedFaces, DecorationKind kind,
                                     int count, int variants, float alongRange, float baseHeight, float heightRange)
        {
            int placed = 0;

            for (int attempt = 0; attempt < count * 20 && placed < count; attempt++)
            {
                int x = rng.Next(d.Width);
                int z = rng.Next(d.Height);
                WallSide side = Sides[rng.Next(Sides.Length)];
                float along = (float)(rng.NextDouble() * 2.0 - 1.0) * alongRange;
                float height = baseHeight + (float)rng.NextDouble() * heightRange;
                int variant = rng.Next(variants);

                if (WearAt(d, x, z) < MarkMinWear) continue;
                if (!IsFreeFace(d, x, z, side, usedFaces)) continue;

                usedFaces.Add(FaceKey(d, x, z, side));
                d.Decorations.Add(new Decoration
                {
                    Kind = kind,
                    X = x,
                    Z = z,
                    Side = side,
                    Along = along,
                    Height = height,
                    Variant = variant
                });
                placed++;
            }
        }

        /// <summary>Uma grelha de ar a cada tantas células, em média.</summary>
        public const int CellsPerVent = 12;

        // Grelhas de retorno de ar no alto das paredes: uma em cada CellsPerVent células, em
        // paredes livres (sem sangue, palavra, seta, nicho nem saída) fora da sala inicial,
        // nunca duas na mesma face. A posição ao longo da parede varia; a altura é fixa.
        static void PlaceVents(LevelData d, Random rng, HashSet<int> usedFaces)
        {
            int count = Math.Max(2, d.Width * d.Height / CellsPerVent);
            int placed = 0;

            for (int attempt = 0; attempt < count * 20 && placed < count; attempt++)
            {
                int x = rng.Next(d.Width);
                int z = rng.Next(d.Height);
                WallSide side = Sides[rng.Next(Sides.Length)];
                float along = (float)(rng.NextDouble() * 2.0 - 1.0) * 1.1f;

                if (d.InRoom(x, z) || !IsFreeFace(d, x, z, side, usedFaces)) continue;

                usedFaces.Add(FaceKey(d, x, z, side));
                d.Decorations.Add(new Decoration { Kind = DecorationKind.Vent, X = x, Z = z, Side = side, Along = along });
                placed++;
            }
        }

        // ---------------------------------------------------------------- Piscinas

        /// <summary>Uma área funda a cada tantas células, em média.</summary>
        public const int CellsPerBasin = 40;
        /// <summary>No máximo um poço a cada tantas células.</summary>
        public const int CellsPerWell = 32;

        static readonly WallSide[] EdgeOnce = { WallSide.South, WallSide.West };

        /// <summary>
        /// Profundidades das Piscinas. Quase tudo é raso; a sala inicial, a saída e alguns
        /// patamares são secos; áreas fundas retangulares; e poços dentro delas. Regras:
        /// seco nunca encosta em fundo por uma passagem (o desnível de 1,4 m não teria
        /// escada); poço só encosta em fundo ou poço, e só fica se toda célula que não é poço
        /// continuar alcançável a pé a partir da sala inicial (o mesmo espírito de "buraco nunca
        /// isola"). Por fim, uma escada em cada passagem entre alturas diferentes que se anda.
        /// </summary>
        static void PlacePools(LevelData d, Random rng)
        {
            int w = d.Width, h = d.Height;
            d.Depth = new PoolDepth[w, h];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    d.Depth[x, z] = d.InRoom(x, z) ? PoolDepth.Dry : PoolDepth.Shallow;
            d.Depth[d.ExitX, d.ExitZ] = PoolDepth.Dry;

            // Patamares secos soltos, de uma ou duas células.
            int patches = Math.Max(1, w * h / 24);
            for (int i = 0; i < patches; i++)
            {
                int x = rng.Next(w), z = rng.Next(h);
                d.Depth[x, z] = PoolDepth.Dry;
                WallSide side = Sides[rng.Next(Sides.Length)];
                if (rng.Next(2) == 0 && d.IsOpen(x, z, side) && d.Neighbor(x, z, side, out int nx, out int nz))
                    d.Depth[nx, nz] = PoolDepth.Dry;
            }

            // Áreas fundas: retângulos de 2 x 2 a 4 x 3 que não pegam a sala inicial nem seco.
            int basins = Math.Max(2, w * h / CellsPerBasin);
            for (int attempt = 0, placed = 0; attempt < basins * 12 && placed < basins; attempt++)
            {
                int bw = 2 + rng.Next(3), bh = 2 + rng.Next(2);
                int x0 = rng.Next(w - bw + 1), z0 = rng.Next(h - bh + 1);
                bool free = true;
                for (int z = z0; z < z0 + bh && free; z++)
                    for (int x = x0; x < x0 + bw && free; x++)
                        free = d.Depth[x, z] == PoolDepth.Shallow;
                if (!free) continue;

                for (int z = z0; z < z0 + bh; z++)
                    for (int x = x0; x < x0 + bw; x++)
                        d.Depth[x, z] = PoolDepth.Deep;
                placed++;
            }

            // Fundo que encosta em seco por uma passagem vira raso.
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    if (d.Depth[x, z] != PoolDepth.Deep) continue;
                    foreach (WallSide side in Sides)
                        if (d.IsOpen(x, z, side) && d.Neighbor(x, z, side, out int nx, out int nz) && d.Depth[nx, nz] == PoolDepth.Dry)
                            d.Depth[x, z] = PoolDepth.Shallow;
                }

            // Poços: fundo cercado só de fundo (ou parede), um de cada vez, sem isolar ninguém.
            int wells = Math.Max(1, w * h / CellsPerWell), dug = 0;
            for (int z = 0; z < h && dug < wells; z++)
                for (int x = 0; x < w && dug < wells; x++)
                {
                    if (d.Depth[x, z] != PoolDepth.Deep || rng.Next(3) != 0) continue;

                    bool enclosed = true, touchesDeep = false;
                    foreach (WallSide side in Sides)
                    {
                        if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                        PoolDepth n = d.Depth[nx, nz];
                        if (n != PoolDepth.Deep && n != PoolDepth.Well) enclosed = false;
                        if (n == PoolDepth.Deep) touchesDeep = true;
                    }
                    if (!enclosed || !touchesDeep) continue;

                    d.Depth[x, z] = PoolDepth.Well;
                    if (AllReachableOnFoot(d)) dug++;
                    else d.Depth[x, z] = PoolDepth.Deep;
                }

            // Mais claraboias que no Level 0: as Piscinas são claras, com cantos escuros só aqui e
            // ali. Uma a cada três células sem luz ganha uma acesa, até o limite de sombras.
            int lit = 0;
            foreach (LightState state in d.Lights)
                if (state == LightState.On || state == LightState.Flicker) lit++;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    if (lit >= MaxLitLights || d.Lights[x, z] != LightState.None || rng.Next(3) != 0) continue;
                    d.Lights[x, z] = LightState.On;
                    lit++;
                }

            // Escadas: uma por passagem entre alturas diferentes, na célula de baixo.
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    foreach (WallSide side in EdgeOnce)
                    {
                        if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                        PoolDepth a = d.Depth[x, z], b = d.Depth[nx, nz];
                        if (a == b || a == PoolDepth.Well || b == PoolDepth.Well) continue;

                        float ha = LevelData.DepthFloor(a), hb = LevelData.DepthFloor(b);
                        bool aLower = ha < hb;
                        d.Stairs.Add(new PoolStair
                        {
                            X = aLower ? x : nx,
                            Z = aLower ? z : nz,
                            Side = aLower ? side : LevelData.Opposite(side),
                            Steps = (int)Math.Round(Math.Abs(ha - hb) / LevelData.StepRise)
                        });
                    }
        }

        /// <summary>Verdadeiro se toda célula que não é poço é alcançável a pé a partir da sala inicial.</summary>
        public static bool AllReachableOnFoot(LevelData d)
        {
            bool[] seen = FootReach(d, d.SpawnX, d.SpawnZ);
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    if (d.Depth[x, z] != PoolDepth.Well && !seen[x + z * d.Width]) return false;
            return true;
        }

        /// <summary>Células alcançáveis a pé (sem entrar em poço) a partir de (x, z).</summary>
        public static bool[] FootReach(LevelData d, int x0, int z0)
        {
            var seen = new bool[d.Width * d.Height];
            var queue = new Queue<int>();
            seen[x0 + z0 * d.Width] = true;
            queue.Enqueue(x0 + z0 * d.Width);

            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % d.Width, z = i / d.Width;
                foreach (WallSide side in Sides)
                {
                    if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                    int n = nx + nz * d.Width;
                    if (seen[n] || d.Depth[nx, nz] == PoolDepth.Well) continue;
                    seen[n] = true;
                    queue.Enqueue(n);
                }
            }

            return seen;
        }

        /// <summary>Caminho mais curto a pé (sem poço) da sala inicial até a saída, célula por célula.</summary>
        public static List<int> FootPath(LevelData d)
        {
            int total = d.Width * d.Height, start = d.SpawnX + d.SpawnZ * d.Width, goal = d.ExitX + d.ExitZ * d.Width;
            var previous = new int[total];
            for (int i = 0; i < total; i++) previous[i] = -2;
            var queue = new Queue<int>();
            previous[start] = -1;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % d.Width, z = i / d.Width;
                if (i == goal) break;
                foreach (WallSide side in Sides)
                {
                    if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                    int n = nx + nz * d.Width;
                    if (previous[n] != -2 || d.Depth[nx, nz] == PoolDepth.Well) continue;
                    previous[n] = i;
                    queue.Enqueue(n);
                }
            }

            var path = new List<int>();
            if (previous[goal] == -2) return path;
            for (int i = goal; i != -1; i = previous[i]) path.Add(i);
            path.Reverse();
            return path;
        }

        // Sujeira, umidade e desgaste: muitas manchas discretas em paredes e no carpete.
        // Diferente do sangue e das palavras, podem se sobrepor entre si.
        static void PlaceGrime(LevelData d, Random rng)
        {
            int cells = d.Width * d.Height;

            PlaceWallGrime(d, rng, DecorationKind.WallStain, cells * 85 / 100, WallStainVariants);
            PlaceWallGrime(d, rng, DecorationKind.WallScuff, cells * 22 / 100, 1);
            PlaceFloorGrime(d, rng, DecorationKind.FloorWet, cells * 42 / 100, FloorWetVariants);
            PlaceFloorGrime(d, rng, DecorationKind.FloorWear, cells * 30 / 100, 1);
        }

        static void PlaceWallGrime(LevelData d, Random rng, DecorationKind kind, int count, int variants)
        {
            int exitFace = FaceKey(d, d.ExitX, d.ExitZ, d.ExitSide);

            // Número fixo de tentativas. Cada uma só vira mancha se cair numa parede e
            // passar no sorteio do desgaste: perto do início quase nenhuma passa.
            for (int attempt = 0; attempt < count * 2; attempt++)
            {
                int x = rng.Next(d.Width);
                int z = rng.Next(d.Height);
                WallSide side = Sides[rng.Next(Sides.Length)];
                float roll = (float)rng.NextDouble();
                float wear = WearAt(d, x, z);
                var mark = new Decoration
                {
                    Kind = kind,
                    X = x,
                    Z = z,
                    Side = side,
                    Along = (float)(rng.NextDouble() * 2.0 - 1.0),
                    Height = (float)rng.NextDouble(),
                    Scale = (float)rng.NextDouble(),
                    Variant = rng.Next(variants),
                    Flip = rng.Next(2) == 0,
                    Strength = 0.55f + 0.45f * wear
                };

                if (roll >= wear) continue;
                if (!d.HasWall(x, z, side) || d.IsAlcove(x, z, side) || d.IsRoomWall(x, z, side)) continue;
                if (FaceKey(d, x, z, side) == exitFace) continue;

                d.Decorations.Add(mark);
            }
        }

        static void PlaceFloorGrime(LevelData d, Random rng, DecorationKind kind, int count, int variants)
        {
            for (int i = 0; i < count; i++)
            {
                int x = rng.Next(d.Width);
                int z = rng.Next(d.Height);
                float roll = (float)rng.NextDouble();
                float wear = WearAt(d, x, z);
                var mark = new Decoration
                {
                    Kind = kind,
                    X = x,
                    Z = z,
                    Side = WallSide.Floor,
                    Along = (float)(rng.NextDouble() * 2.0 - 1.0),
                    Height = (float)(rng.NextDouble() * 2.0 - 1.0),
                    Scale = (float)rng.NextDouble(),
                    Variant = rng.Next(variants),
                    Yaw = (float)rng.NextDouble() * 360f,
                    Flip = rng.Next(2) == 0,
                    Strength = 0.55f + 0.4f * wear
                };

                // Em célula de buraco não há chão onde a mancha aparecer.
                if (roll < wear && !d.Pit[x, z]) d.Decorations.Add(mark);
            }
        }

        static void PlaceFloorPools(LevelData d, Random rng)
        {
            int count = Math.Max(2, d.Width * d.Height / 40);

            for (int i = 0; i < count; i++)
            {
                var pool = new Decoration
                {
                    Kind = DecorationKind.FloorPool,
                    X = rng.Next(d.Width),
                    Z = rng.Next(d.Height),
                    Side = WallSide.Floor,
                    Along = (float)(rng.NextDouble() * 2.0 - 1.0),
                    Height = (float)(rng.NextDouble() * 2.0 - 1.0),
                    Variant = rng.Next(PoolVariants),
                    Yaw = (float)rng.NextDouble() * 360f
                };

                if (WearAt(d, pool.X, pool.Z) >= MarkMinWear && !d.Pit[pool.X, pool.Z]) d.Decorations.Add(pool);
            }
        }
    }
}
