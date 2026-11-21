using System.Collections.Generic;
using BackRooms.World;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// Onde o Undertow (o inimigo da água funda das Piscinas, <see cref="UndertowBrain"/>) pode
    /// ir: a grade das Piscinas, só nas células de água. Não usa o <see cref="LevelNav"/> da
    /// entidade (que não conhece água) nem o <see cref="LevelData.CanStep"/> (que recusa o poço e
    /// a troca de profundidade sem escada: nadando, ela passa por cima de tudo isso).
    ///
    /// Território dela (<see cref="Territory"/>):
    /// • a água funda e o poço, sempre (piso a −2 e −6 m);
    /// • o raso quando a maré o deixa fundo o bastante para nadar (coluna d'água de
    ///   <see cref="EnterColumn"/> m; sai dele só abaixo de <see cref="LeaveColumn"/>, para não
    ///   piscar na borda da conta enquanto a maré desce);
    /// • o seco nunca (na maré alta fica com 0,85 m: ninguém nada ali).
    /// Drenada a fase, a funda continua território, mas quase sem água: ali ela se arrasta
    /// (<see cref="Swimmable"/> falso), devagar.
    ///
    /// Passa de uma célula à vizinha pela divisa aberta (vão, arco, porta), as duas território.
    /// Regiões (<see cref="Region"/>): pedaços de território ligados; de uma para outra ela só vai
    /// pelos canos (<see cref="UndertowBrain"/>, estado Travel).
    /// </summary>
    public sealed class UndertowNav
    {
        /// <summary>Coluna d'água (m) a partir da qual o raso vira território na maré alta.</summary>
        public const float EnterColumn = 1.3f;
        /// <summary>Coluna d'água (m) abaixo da qual o raso deixa de ser território (maré descendo).</summary>
        public const float LeaveColumn = 1.15f;
        /// <summary>Coluna d'água (m) a partir da qual ela nada; abaixo disso, se arrasta.</summary>
        public const float SwimColumn = 1.2f;

        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

        readonly LevelBuilder _builder;
        readonly Queue<int> _queue = new Queue<int>();
        readonly int[,] _scratch;

        public LevelData Data { get; }

        /// <summary>Altura da superfície da água na última <see cref="Refresh"/>.</summary>
        public float SurfaceY { get; private set; }

        /// <summary>Células onde ela pode ficar agora.</summary>
        public bool[,] Territory { get; }

        /// <summary>Região de cada célula de território (−1 fora dele).</summary>
        public int[,] Region { get; }

        public int RegionCount { get; private set; }

        public UndertowNav(LevelBuilder builder)
        {
            _builder = builder;
            Data = builder.Data;
            _scratch = new int[Data.Width, Data.Height];
            Territory = new bool[Data.Width, Data.Height];
            Region = new int[Data.Width, Data.Height];
        }

        public int[,] NewMap() => new int[Data.Width, Data.Height];

        /// <summary>Recalcula o território e as regiões pela altura da água agora.</summary>
        public void Refresh(float surfaceY)
        {
            SurfaceY = surfaceY;
            for (int z = 0; z < Data.Height; z++)
                for (int x = 0; x < Data.Width; x++)
                    Territory[x, z] = IsTerritory(x, z, Territory[x, z]);

            for (int z = 0; z < Data.Height; z++)
                for (int x = 0; x < Data.Width; x++)
                    Region[x, z] = -1;

            RegionCount = 0;
            for (int z = 0; z < Data.Height; z++)
                for (int x = 0; x < Data.Width; x++)
                {
                    if (!Territory[x, z] || Region[x, z] >= 0) continue;
                    Flood(x, z, RegionCount++);
                }
        }

        bool IsTerritory(int x, int z, bool before)
        {
            if (!Data.HasPools || Data.IsSolid(x, z)) return false;
            PoolDepth depth = Data.Depth[x, z];
            if (depth == PoolDepth.Deep || depth == PoolDepth.Well) return true;
            if (depth != PoolDepth.Shallow) return false;
            float column = Column(x, z);
            return column >= EnterColumn || before && column >= LeaveColumn;
        }

        void Flood(int x0, int z0, int id)
        {
            _queue.Clear();
            Region[x0, z0] = id;
            _queue.Enqueue(x0 + z0 * Data.Width);
            while (_queue.Count > 0)
            {
                int cell = _queue.Dequeue();
                int x = cell % Data.Width, z = cell / Data.Width;
                foreach (WallSide side in Sides)
                {
                    if (!CanStep(x, z, side)) continue;
                    Data.Neighbor(x, z, side, out int nx, out int nz);
                    if (Region[nx, nz] >= 0) continue;
                    Region[nx, nz] = id;
                    _queue.Enqueue(nx + nz * Data.Width);
                }
            }
        }

        /// <summary>Altura do piso da célula, no mundo.</summary>
        public float FloorY(int x, int z) => _builder.CellCenter(x, z).y;

        /// <summary>Água acima do piso da célula agora, em metros (0 se a água está abaixo dele).</summary>
        public float Column(int x, int z) => Mathf.Max(0f, SurfaceY - FloorY(x, z));

        /// <summary>Dá para nadar ali (senão, drenada, ela se arrasta pelo fundo)?</summary>
        public bool Swimmable(int x, int z) => Column(x, z) >= SwimColumn;

        public Vector3 CellCenter(int x, int z) => _builder.CellCenter(x, z);

        /// <summary>Em qualquer máquina, sem montar a navegação: dá para nadar no ponto (coluna d'água da célula)?</summary>
        public static bool SwimmableAt(LevelBuilder level, Vector3 world, float surfaceY)
        {
            if (level == null || level.Data == null || !level.Data.HasPools) return true;
            Vector3 local = level.transform.InverseTransformPoint(world);
            int x = Mathf.FloorToInt(local.x / LevelBuilder.Cell), z = Mathf.FloorToInt(local.z / LevelBuilder.Cell);
            if (x < 0 || z < 0 || x >= level.Data.Width || z >= level.Data.Height) return true;
            return surfaceY - level.CellCenter(x, z).y >= SwimColumn;
        }

        public bool WorldToCell(Vector3 world, out int x, out int z)
        {
            Vector3 local = _builder.transform.InverseTransformPoint(world);
            x = Mathf.FloorToInt(local.x / LevelBuilder.Cell);
            z = Mathf.FloorToInt(local.z / LevelBuilder.Cell);
            return x >= 0 && z >= 0 && x < Data.Width && z < Data.Height;
        }

        public bool InTerritory(Vector3 world) => WorldToCell(world, out int x, out int z) && Territory[x, z];

        /// <summary>Região do ponto (−1 fora do território).</summary>
        public int RegionAt(Vector3 world) => WorldToCell(world, out int x, out int z) ? Region[x, z] : -1;

        /// <summary>Passa da célula para a vizinha daquele lado? Divisa aberta e as duas no território.</summary>
        public bool CanStep(int x, int z, WallSide side)
        {
            if (!Territory[x, z] || !Data.IsOpen(x, z, side)) return false;
            Data.Neighbor(x, z, side, out int nx, out int nz);
            return Territory[nx, nz];
        }

        /// <summary>Passos de (fromX, fromZ) até cada célula do território; −1 onde não chega.</summary>
        public void Distances(int fromX, int fromZ, int[,] distance)
        {
            Fill(distance);
            if (!Territory[fromX, fromZ]) return;

            distance[fromX, fromZ] = 0;
            _queue.Clear();
            _queue.Enqueue(fromX + fromZ * Data.Width);
            while (_queue.Count > 0)
            {
                int cell = _queue.Dequeue();
                int x = cell % Data.Width, z = cell / Data.Width;
                foreach (WallSide side in Sides)
                {
                    if (!CanStep(x, z, side)) continue;
                    Data.Neighbor(x, z, side, out int nx, out int nz);
                    if (distance[nx, nz] >= 0) continue;
                    distance[nx, nz] = distance[x, z] + 1;
                    _queue.Enqueue(nx + nz * Data.Width);
                }
            }
        }

        /// <summary>Caminho (centros das células, sem a de partida). Falso se não há.</summary>
        public bool Path(int fromX, int fromZ, int toX, int toZ, List<Vector3> waypoints)
        {
            waypoints.Clear();
            if (!Territory[toX, toZ]) return false;
            Distances(toX, toZ, _scratch);
            if (_scratch[fromX, fromZ] < 0) return false;

            int x = fromX, z = fromZ;
            while (_scratch[x, z] > 0)
            {
                bool moved = false;
                foreach (WallSide side in Sides)
                {
                    if (!CanStep(x, z, side)) continue;
                    Data.Neighbor(x, z, side, out int nx, out int nz);
                    if (_scratch[nx, nz] != _scratch[x, z] - 1) continue;
                    x = nx;
                    z = nz;
                    waypoints.Add(CellCenter(x, z));
                    moved = true;
                    break;
                }
                if (!moved) return false;
            }
            return true;
        }

        /// <summary>
        /// A célula do território por onde um barulho no ponto chega à água dela: a própria, se é
        /// território; senão a vizinha de território (pela divisa aberta) mais perto do ponto. Assim
        /// quem chapinha no raso ou pisa na borda da piscina é ouvido dali. Falso se não há.
        /// </summary>
        public bool EntryCell(Vector3 world, out int x, out int z)
        {
            if (!WorldToCell(world, out x, out z)) return false;
            if (Territory[x, z]) return true;

            int bx = -1, bz = -1;
            float best = float.MaxValue;
            foreach (WallSide side in Sides)
            {
                if (!Data.IsOpen(x, z, side)) continue;
                Data.Neighbor(x, z, side, out int nx, out int nz);
                if (!Territory[nx, nz]) continue;
                Vector3 c = CellCenter(nx, nz);
                float d = new Vector2(c.x - world.x, c.z - world.z).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                bx = nx;
                bz = nz;
            }
            x = bx;
            z = bz;
            return bx >= 0;
        }

        /// <summary>Entre as células de território, a mais perto do ponto em linha reta (de uma região, ou de qualquer uma com −1).</summary>
        public bool Nearest(Vector3 world, int region, out int x, out int z)
        {
            x = z = -1;
            float best = float.MaxValue;
            for (int cz = 0; cz < Data.Height; cz++)
                for (int cx = 0; cx < Data.Width; cx++)
                {
                    if (!Territory[cx, cz] || region >= 0 && Region[cx, cz] != region) continue;
                    Vector3 c = CellCenter(cx, cz);
                    float d = new Vector2(c.x - world.x, c.z - world.z).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    x = cx;
                    z = cz;
                }
            return x >= 0;
        }

        /// <summary>O ponto da célula mais perto do alvo, a pelo menos <paramref name="margin"/> da borda (Y do piso).</summary>
        public Vector3 ClampToCell(int x, int z, Vector3 world, float margin)
        {
            Vector3 center = CellCenter(x, z);
            float half = LevelBuilder.Cell * 0.5f - margin;
            return new Vector3(Mathf.Clamp(world.x, center.x - half, center.x + half), center.y,
                               Mathf.Clamp(world.z, center.z - half, center.z + half));
        }

        /// <summary>
        /// Dá para ir em linha reta, no plano, de um ponto ao outro sem sair do território? Confere a
        /// cada 35 cm, com a largura dada para os lados. Parede e coluna se conferem com a física.
        /// </summary>
        public bool StraightClear(Vector3 from, Vector3 to, float radius)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            float length = delta.magnitude;
            Vector3 forward = length > 0.001f ? delta / length : Vector3.forward;
            Vector3 side = new Vector3(-forward.z, 0f, forward.x) * radius;
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.35f));
            int lastX = -1, lastZ = -1;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = from + forward * (length * i / steps);
                foreach (Vector3 p in new[] { point, point + side, point - side })
                {
                    if (!WorldToCell(p, out int x, out int z) || !Territory[x, z]) return false;
                }
                // Entre duas células seguidas da linha, a divisa tem de estar aberta.
                if (WorldToCell(point, out int cx, out int cz))
                {
                    if (lastX >= 0 && (cx != lastX || cz != lastZ) && !Adjacent(lastX, lastZ, cx, cz)) return false;
                    lastX = cx;
                    lastZ = cz;
                }
            }
            return true;
        }

        bool Adjacent(int ax, int az, int bx, int bz)
        {
            foreach (WallSide side in Sides)
            {
                if (!Data.Neighbor(ax, az, side, out int nx, out int nz) || nx != bx || nz != bz) continue;
                return CanStep(ax, az, side);
            }
            return false; // diagonal: só passando por uma das duas vizinhas, conferidas pelos pontos
        }

        void Fill(int[,] map)
        {
            for (int z = 0; z < Data.Height; z++)
                for (int x = 0; x < Data.Width; x++)
                    map[x, z] = -1;
        }
    }
}
