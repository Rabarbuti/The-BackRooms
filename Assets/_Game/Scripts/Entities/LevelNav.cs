using System.Collections.Generic;
using BackRooms.World;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// Navegação da entidade sobre a grade do nível. Não usa NavMesh: o mapa já é uma grade
    /// de células de 4 m com as paredes nas divisas, então o caminho sai direto do LevelData.
    ///
    /// Onde a entidade pisa: em qualquer célula com piso, menos os abrigos (a sala inicial e,
    /// no Escritório, as salas pequenas acesas: <see cref="LevelData.IsShelter"/>) e os buracos
    /// (ela não anda nas faixas). Isso faz dos abrigos um refúgio e das faixas uma rota de fuga.
    ///
    /// O som é outra conta: atravessa buracos e portas e só é barrado por parede.
    /// </summary>
    public sealed class LevelNav
    {
        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

        readonly LevelBuilder _builder;
        readonly Queue<int> _queue = new Queue<int>();
        readonly int[,] _scratch;

        public LevelData Data { get; }

        public LevelNav(LevelBuilder builder)
        {
            _builder = builder;
            Data = builder.Data;
            _scratch = new int[Data.Width, Data.Height];
        }

        public int[,] NewMap()
        {
            return new int[Data.Width, Data.Height];
        }

        public Vector3 CellCenter(int x, int z)
        {
            return _builder.CellCenter(x, z);
        }

        /// <summary>Célula que contém o ponto. Falso se o ponto está fora do mapa.</summary>
        public bool WorldToCell(Vector3 world, out int x, out int z)
        {
            Vector3 local = _builder.transform.InverseTransformPoint(world);
            x = Mathf.FloorToInt(local.x / LevelBuilder.Cell);
            z = Mathf.FloorToInt(local.z / LevelBuilder.Cell);
            return x >= 0 && z >= 0 && x < Data.Width && z < Data.Height;
        }

        /// <summary>A entidade pode ficar nesta célula?</summary>
        public bool Standable(int x, int z)
        {
            return !Data.Pit[x, z] && !Data.IsShelter(x, z) && !Data.IsBarricade(x, z);
        }

        /// <summary>A entidade pode passar da célula (x, z) para a vizinha daquele lado?</summary>
        public bool CanStep(int x, int z, WallSide side)
        {
            if (!Standable(x, z) || !Data.CanWalk(x, z, side, false)) return false;

            Data.Neighbor(x, z, side, out int nx, out int nz);
            return Standable(nx, nz);
        }

        /// <summary>Passos da entidade de (fromX, fromZ) até cada célula; -1 onde ela não chega.</summary>
        public void Distances(int fromX, int fromZ, int[,] distance)
        {
            Fill(distance);
            if (!Standable(fromX, fromZ)) return;

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

        /// <summary>
        /// Caminho da entidade de uma célula a outra: os centros das células, sem a de partida.
        /// Falso (e lista vazia) se não há caminho.
        /// </summary>
        public bool Path(int fromX, int fromZ, int toX, int toZ, List<Vector3> waypoints)
        {
            waypoints.Clear();
            if (!Standable(toX, toZ)) return false;

            // Busca a partir do destino: seguindo sempre o vizinho mais próximo dele, sai o caminho já na ordem.
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
        /// Distância que o som percorre de (fromX, fromZ) até cada célula, em células, só até
        /// <paramref name="limit"/>. Parede barra; buraco, porta e sala inicial não.
        /// </summary>
        public void SoundDistances(int fromX, int fromZ, int limit, int[,] distance)
        {
            Fill(distance);
            distance[fromX, fromZ] = 0;
            _queue.Clear();
            _queue.Enqueue(fromX + fromZ * Data.Width);

            while (_queue.Count > 0)
            {
                int cell = _queue.Dequeue();
                int x = cell % Data.Width, z = cell / Data.Width;
                if (distance[x, z] >= limit) continue;

                foreach (WallSide side in Sides)
                {
                    if (!Data.Neighbor(x, z, side, out int nx, out int nz) || Data.HasWall(x, z, side)) continue;
                    if (distance[nx, nz] >= 0) continue;

                    distance[nx, nz] = distance[x, z] + 1;
                    _queue.Enqueue(nx + nz * Data.Width);
                }
            }
        }

        /// <summary>
        /// Entre as células que a entidade alcança (<paramref name="reach"/> ≥ 0), a mais
        /// próxima do ponto dado, em linha reta.
        /// </summary>
        public bool NearestReachable(Vector3 world, int[,] reach, out int x, out int z)
        {
            x = z = -1;
            float best = float.MaxValue;

            for (int cz = 0; cz < Data.Height; cz++)
            {
                for (int cx = 0; cx < Data.Width; cx++)
                {
                    if (reach[cx, cz] < 0) continue;

                    Vector3 center = CellCenter(cx, cz);
                    float dx = center.x - world.x, dz = center.z - world.z;
                    float distance = dx * dx + dz * dz;
                    if (distance >= best) continue;

                    best = distance;
                    x = cx;
                    z = cz;
                }
            }

            return x >= 0;
        }

        /// <summary>O ponto da célula mais próximo do alvo, sem chegar a menos de <paramref name="margin"/> da borda.</summary>
        public Vector3 ClampToCell(int x, int z, Vector3 world, float margin)
        {
            Vector3 center = CellCenter(x, z);
            float half = LevelBuilder.Cell * 0.5f - margin;

            return new Vector3(Mathf.Clamp(world.x, center.x - half, center.x + half), center.y,
                               Mathf.Clamp(world.z, center.z - half, center.z + half));
        }

        /// <summary>
        /// O chão entre dois pontos serve para a entidade? Confere, a cada meio palmo e com a
        /// largura do corpo, que ela nunca fica sobre um buraco, na sala inicial ou fora do mapa.
        /// Não olha parede: isso é conferido com a física.
        /// </summary>
        public bool FloorClear(Vector3 from, Vector3 to, float radius)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            float length = delta.magnitude;
            Vector3 forward = length > 0.001f ? delta / length : Vector3.forward;
            Vector3 side = new Vector3(-forward.z, 0f, forward.x) * radius;
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.35f));

            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = from + forward * (length * i / steps);
                if (!StandableAt(point) || !StandableAt(point + side) || !StandableAt(point - side)) return false;
            }

            return true;
        }

        bool StandableAt(Vector3 world)
        {
            return WorldToCell(world, out int x, out int z) && Standable(x, z);
        }

        /// <summary>A célula logo do lado de fora do vão de porta da sala inicial.</summary>
        public bool DoorCell(out int x, out int z)
        {
            for (z = 0; z <= Data.Height; z++)
            {
                for (x = 0; x < Data.Width; x++)
                {
                    if (!Data.DoorSouth[x, z]) continue;

                    // O vão fica entre (x, z - 1) e (x, z); uma das duas é da sala.
                    if (z < Data.Height && !Data.InRoom(x, z)) return true;
                    z--;
                    return z >= 0;
                }
            }

            for (x = 0; x <= Data.Width; x++)
            {
                for (z = 0; z < Data.Height; z++)
                {
                    if (!Data.DoorWest[x, z]) continue;

                    if (x < Data.Width && !Data.InRoom(x, z)) return true;
                    x--;
                    return x >= 0;
                }
            }

            x = z = -1;
            return false;
        }

        void Fill(int[,] map)
        {
            for (int z = 0; z < Data.Height; z++)
                for (int x = 0; x < Data.Width; x++)
                    map[x, z] = -1;
        }
    }
}
