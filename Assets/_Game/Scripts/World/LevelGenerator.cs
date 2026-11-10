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
        FloorWear
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

        /// <summary>Células do caminho mais curto do início até a saída, em ordem. Cada item é x + z * Width.</summary>
        public List<int> PathCells = new List<int>();

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
    /// saída distante e marcas nas paredes (sangue, palavras e setas que levam à saída).
    /// Usa apenas System.Random com a seed recebida — nunca UnityEngine.Random.
    /// </summary>
    public static class LevelGenerator
    {
        const double WallChance = 0.20;
        const double WallContinueChance = 0.55;
        const double HallPillarChance = 0.60;
        const double LoosePillarChance = 0.06;
        const double LightChance = 0.42;
        const double AlcoveChance = 0.07;
        const double ArrowChance = 0.55;

        // O entorno do ponto de início é novo; o desgaste aparece conforme se afasta dele.
        // Distâncias em células (4 m cada), em linha reta a partir da célula inicial.
        const float CleanRadius = 1.8f;

        // Sala inicial: lado em células e número de vãos de porta.
        const int SpawnRoomSize = 2;
        const int SpawnRoomDoors = 1;
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

        public static LevelData Generate(int seed, int width, int height)
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
                SpawnX = width / 2,
                SpawnZ = height / 2
            };

            // A ordem das etapas define a ordem de consumo do gerador: não reordenar,
            // senão a mesma seed passa a produzir outro mapa.
            ScatterWalls(data, rng);
            CarveHalls(data, rng);
            CarveSpawnRoom(data, rng);
            CloseBoundary(data);
            ConnectEverything(data);
            ScatterPillars(data, rng);
            ScatterLights(data, rng);
            FinishSpawnRoom(data);
            ScatterAlcoves(data, rng);

            int[,] parent = FindExit(data);
            var usedFaces = new HashSet<int> { FaceKey(data, data.ExitX, data.ExitZ, data.ExitSide) };

            PlaceArrows(data, rng, parent, usedFaces);
            PlaceWallMarks(data, rng, usedFaces);
            PlaceFloorPools(data, rng);
            PlaceGrime(data, rng);

            return data;
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

        // Busca em largura a partir do spawn. A saída fica na célula mais distante que
        // tenha uma parede comum onde encaixar a porta. Devolve, para cada célula, o
        // índice da célula anterior no caminho mais curto (-1 no spawn).
        static int[,] FindExit(LevelData d)
        {
            var distance = new int[d.Width, d.Height];
            var parent = new int[d.Width, d.Height];
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    distance[x, z] = -1;
                    parent[x, z] = -1;
                }

            var queue = new Queue<(int x, int z)>();
            distance[d.SpawnX, d.SpawnZ] = 0;
            queue.Enqueue((d.SpawnX, d.SpawnZ));

            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                int here = x + z * d.Width;

                if (x > 0 && !d.WallWest[x, z]) Step(d, distance, parent, queue, x - 1, z, here, distance[x, z]);
                if (x < d.Width - 1 && !d.WallWest[x + 1, z]) Step(d, distance, parent, queue, x + 1, z, here, distance[x, z]);
                if (z > 0 && !d.WallSouth[x, z]) Step(d, distance, parent, queue, x, z - 1, here, distance[x, z]);
                if (z < d.Height - 1 && !d.WallSouth[x, z + 1]) Step(d, distance, parent, queue, x, z + 1, here, distance[x, z]);
            }

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

            return parent;
        }

        static void Step(LevelData d, int[,] distance, int[,] parent, Queue<(int x, int z)> queue,
                         int x, int z, int from, int fromDistance)
        {
            if (distance[x, z] >= 0) return;
            distance[x, z] = fromDistance + 1;
            parent[x, z] = from;
            queue.Enqueue((x, z));
        }

        static int FaceKey(LevelData d, int x, int z, WallSide side)
        {
            return (x + z * d.Width) * 4 + (int)side;
        }

        // Setas rabiscadas ao longo do caminho mais curto do spawn até a saída. Cada seta
        // fica numa parede paralela ao sentido de marcha e aponta para o próximo passo.
        static void PlaceArrows(LevelData d, Random rng, int[,] parent, HashSet<int> usedFaces)
        {
            int cx = d.ExitX, cz = d.ExitZ;
            d.PathCells.Add(cx + cz * d.Width);

            while (parent[cx, cz] >= 0)
            {
                int px = parent[cx, cz] % d.Width;
                int pz = parent[cx, cz] / d.Width;
                d.PathCells.Add(px + pz * d.Width);
                int dirX = cx - px;
                int dirZ = cz - pz;

                // Sorteia sempre, mesmo sem parede disponível, para manter a sequência estável.
                bool roll = rng.NextDouble() < ArrowChance;
                bool preferFirst = rng.Next(2) == 0;
                float along = (float)(rng.NextDouble() * 1.2 - 0.6);
                float height = 1.25f + (float)rng.NextDouble() * 0.35f;

                WallSide a = dirX != 0 ? WallSide.South : WallSide.West;
                WallSide b = dirX != 0 ? WallSide.North : WallSide.East;
                if (!preferFirst) { WallSide t = a; a = b; b = t; }

                if (roll)
                {
                    WallSide? chosen = null;
                    if (IsFreeFace(d, px, pz, a, usedFaces)) chosen = a;
                    else if (IsFreeFace(d, px, pz, b, usedFaces)) chosen = b;

                    if (chosen.HasValue)
                    {
                        usedFaces.Add(FaceKey(d, px, pz, chosen.Value));
                        d.Decorations.Add(new Decoration
                        {
                            Kind = DecorationKind.Arrow,
                            X = px,
                            Z = pz,
                            Side = chosen.Value,
                            Along = along,
                            Height = height,
                            DirX = dirX,
                            DirZ = dirZ
                        });
                    }
                }

                cx = px;
                cz = pz;
            }

            // Foi montado da saída para o início; guarda do início para a saída.
            d.PathCells.Reverse();
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

                if (roll < wear) d.Decorations.Add(mark);
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

                if (WearAt(d, pool.X, pool.Z) >= MarkMinWear) d.Decorations.Add(pool);
            }
        }
    }
}
