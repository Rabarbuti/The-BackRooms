using System;
using System.Collections.Generic;
using System.Text;

namespace BackRooms.World
{
    /// <summary>
    /// Gerador das Piscinas (Level 2). A planta é um conjunto de SALAS retangulares (patamar de
    /// chegada, salões de tambores, piscina com deque, átrio da claraboia, escadaria afogada, raia
    /// longa, canal, galerias, cisterna, salas de poço, saída) separadas por paredes, com maciços
    /// entre elas, ligadas por arcadas, paredes de portais e túneis (em abóbada ou fendas altas).
    ///
    /// Ordem: patamar num canto, grande poço e saída do lado oposto, salões, o resto, galerias até
    /// sobrar o vazio desejado; depois o modelo de cada sala (profundidades e escadas), ligações
    /// (árvore mínima + laços), paredes, porta da saída, janelas em série, colunas, claraboias e
    /// lâmpadas. A planta é conferida (<see cref="Validate"/>); se reprovar, tenta de novo com o
    /// MESMO System.Random, e depois de várias tentativas cai numa planta de reserva.
    ///
    /// Determinístico: usa apenas System.Random com a seed, nunca UnityEngine.Random, tempo, ordem
    /// de dicionário nem string.GetHashCode; toda ordenação desempata pelo índice.
    /// </summary>
    public static class PoolGenerator
    {
        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };
        const PoolEdge Unset = (PoolEdge)255;

        public const int MinSize = 16;
        public const int MaxSize = 24;
        const int Attempts = 60;

        const float CellSize = 4f;
        const float Tread = 0.3f;
        const float Eps = 0.01f;

        // Pés-direitos (metros acima da água). Teto baixo e salão largo é o normal; 8 e 12 só nos marcos.
        const float LowCeiling = 3.6f;
        const float MidCeiling = 4.8f;
        const float VaultSpring = 2.4f;
        const float TallCeiling = 8f;
        const float TallestCeiling = 12f;
        const int MaxTallRooms = 4;

        // Orçamentos.
        const int MaxLamps = 24;
        const int MaxWindows = 48;
        const int MinSky = 30, MaxSky = 70;
        /// <summary>Teto das luzes de claraboia (uma por recortada, uma por faixa de teto de vidro).</summary>
        const int MaxSkyLights = 48;
        const int MinWells = 3, MaxWells = 8;
        const float MinSolid = 0.15f, MaxSolid = 0.30f;
        const float MaxCorridor = 0.15f;
        const float MinRoomCells = 0.55f;
        const float MinWideFloor = 0.60f;
        const float MaxNarrowRooms = 0.40f;
        const float MinBlind = 0.70f, MaxBlind = 0.85f;
        const float FarFraction = 0.45f;
        const float MaxOpenForDecor = 0.26f;

        static readonly int[] ReserveSeeds = { 1998, 7, 4242, 31337, 90210, 2026, 777, 123457, 55, 8080 };

        /// <summary>Tentativas da última geração (0 = caiu na reserva). Só para os testes.</summary>
        internal static int LastAttempts;
        /// <summary>Exceções engolidas nas tentativas da última geração. Só para os testes.</summary>
        internal static int LastExceptions;
        internal static string LastExceptionText;
        /// <summary>Motivos de reprovação das tentativas da última geração. Só para os testes.</summary>
        internal static List<string> LastRejections = new List<string>();

        // ================================================================ Entrada

        public static LevelData Generate(int seed, int width, int height)
        {
            width = Math.Min(MaxSize, Math.Max(MinSize, width));
            height = Math.Min(MaxSize, Math.Max(MinSize, height));
            LastExceptions = 0;
            LastRejections.Clear();

            var rng = new Random(seed);
            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                LevelData d = TryBuild(seed, width, height, rng, out string why);
                if (d == null) { LastRejections.Add("montagem: " + why); continue; }
                List<string> problems = Validate(d);
                if (problems.Count == 0)
                {
                    LastAttempts = attempt;
                    return d;
                }
                foreach (string p in problems) LastRejections.Add(p);
            }

            LastAttempts = 0;
            return Reserve(seed, width, height);
        }

        /// <summary>Planta de reserva: a primeira que passa, gerada a partir de seeds fixas (sempre a mesma para o mesmo tamanho).</summary>
        internal static LevelData Reserve(int seed, int width, int height)
        {
            foreach (int s in ReserveSeeds)
            {
                var rng = new Random(s);
                for (int attempt = 0; attempt < Attempts; attempt++)
                {
                    LevelData d = TryBuild(seed, width, height, rng, out _);
                    if (d != null && Validate(d).Count == 0) return d;
                }
            }
            // Não deve acontecer (o teste confere a reserva em todos os tamanhos): devolve a última
            // tentativa montada, mesmo imperfeita, para nunca lançar exceção.
            var last = new Random(seed ^ 0x5bd1e995);
            for (int attempt = 0; attempt < 400; attempt++)
            {
                LevelData d = TryBuild(seed, width, height, last, out _);
                if (d != null) return d;
            }
            return null;
        }

        static LevelData TryBuild(int seed, int width, int height, Random rng, out string why)
        {
            why = null;
            try
            {
                var c = new Ctx(seed, width, height, rng);
                why = Build(c);
                return why == null ? c.D : null;
            }
            catch (Exception e)
            {
                LastExceptions++;
                LastExceptionText = e.ToString();
                why = "exceção";
                return null;
            }
        }

        // ================================================================ Estado da montagem

        enum Tpl : byte { Start, Exit, Hall, Atrium, Basin, Canal, Lane, Gallery, WindowHall, Terrace, WellBig, WellCorner, Cistern, Tunnel, Fenda }

        /// <summary>
        /// Sistema local de uma sala: u atravessa a largura (LW), v corre o comprimento (LH). Swap
        /// troca os eixos (v vira X), FU e FV espelham. É assim que os modelos giram e espelham.
        /// </summary>
        struct Frame
        {
            public int X0, Z0, LW, LH;
            public bool Swap, FU, FV;

            public Frame(int x0, int z0, int lw, int lh, bool swap, bool fu, bool fv)
            {
                X0 = x0; Z0 = z0; LW = lw; LH = lh; Swap = swap; FU = fu; FV = fv;
            }

            public int GW => Swap ? LH : LW;
            public int GH => Swap ? LW : LH;

            public void Cell(int u, int v, out int x, out int z)
            {
                int uu = FU ? LW - 1 - u : u, vv = FV ? LH - 1 - v : v;
                if (Swap) { x = X0 + vv; z = Z0 + uu; }
                else { x = X0 + uu; z = Z0 + vv; }
            }

            public void Vertex(int a, int b, out int x, out int z)
            {
                int aa = FU ? LW - a : a, bb = FV ? LH - b : b;
                if (Swap) { x = X0 + bb; z = Z0 + aa; }
                else { x = X0 + aa; z = Z0 + bb; }
            }

            public WallSide Side(WallSide local)
            {
                int du = local == WallSide.East ? 1 : local == WallSide.West ? -1 : 0;
                int dv = local == WallSide.North ? 1 : local == WallSide.South ? -1 : 0;
                if (FU) du = -du;
                if (FV) dv = -dv;
                int dx = Swap ? dv : du, dz = Swap ? du : dv;
                return dx > 0 ? WallSide.East : dx < 0 ? WallSide.West : dz > 0 ? WallSide.North : WallSide.South;
            }
        }

        sealed class Info
        {
            public PoolRoom Room;
            public Tpl T;
            public Frame F;
            public bool HasWell;
            public int LampWish;
            public PoolSky SkyStyle;
            public PoolEdge WinType = PoolEdge.Wall;
            public readonly List<PoolColumn> Want = new List<PoolColumn>();
            public readonly List<int> Links = new List<int>();
            public int Id => Room.Id;
            public int MinDim => Math.Min(Room.Width, Room.Height);
        }

        sealed class Ctx
        {
            public readonly LevelData D;
            public readonly Random R;
            public readonly int W, H, N;
            public readonly List<Info> Rooms = new List<Info>();
            public readonly bool[,] Reserved;
            /// <summary>Claraboias do sorteio antigo (sem uso; ver <see cref="PlaceSkies"/>).</summary>
            public readonly PoolSky[,] OldSky;
            public readonly bool[,] KeepSky;
            public readonly bool[,] PrimarySky;
            public Info Start, Exit, Well;
            public double StartCX, StartCZ;
            public int Tall;

            public Ctx(int seed, int w, int h, Random r)
            {
                D = NewData(seed, w, h);
                R = r;
                W = w;
                H = h;
                N = w * h;
                Reserved = new bool[w, h];
                OldSky = new PoolSky[w, h];
                KeepSky = new bool[w, h];
                PrimarySky = new bool[w, h];
            }
        }

        sealed class Cand
        {
            public int A, B;
            public bool Tunnel;
            public int X, Z, Len;
            public WallSide Side;
            public double Weight;
            public int Index;
            public bool Done;
        }

        // ================================================================ Montagem

        /// <summary>Monta a planta; devolve null se deu certo ou o motivo de ter desistido.</summary>
        static string Build(Ctx c)
        {
            PlaceStart(c);
            if (!PlaceAnchors(c)) return "sem lugar para o grande poço ou a saída";
            PlaceBag(c);
            FillGalleries(c);
            if (FreeCount(c) > 0.36 * c.N) return "vazio demais depois das salas";
            if (!DecideWells(c)) return "poços fora do orçamento";

            foreach (Info i in c.Rooms.ToArray()) Fill(c, i);

            string link = Link(c);
            if (link != null) return link;

            ResolveEdges(c);
            if (!ChooseExitDoor(c)) return "saída sem parede contra maciço";
            Decorate(c);
            if (!WindowHallsComplete(c)) return "corredor de janelas sem a série inteira";
            PlaceColumns(c);
            PlaceSkies(c);
            if (!PlaceLamps(c)) return "sala escura sem lugar para lâmpada";
            Finish(c);
            ArrangeSkies(c);
            return null;
        }

        static LevelData NewData(int seed, int w, int h)
        {
            var d = new LevelData
            {
                Seed = seed,
                Width = w,
                Height = h,
                WallSouth = new bool[w, h + 1],
                WallWest = new bool[w + 1, h],
                AlcoveSouth = new byte[w, h + 1],
                AlcoveWest = new byte[w + 1, h],
                RoomSouth = new bool[w, h + 1],
                RoomWest = new bool[w + 1, h],
                DoorSouth = new bool[w, h + 1],
                DoorWest = new bool[w + 1, h],
                Pillar = new bool[w + 1, h + 1],
                Lights = new LightState[w, h],
                Pit = new bool[w, h],
                PitStrips = new byte[w, h],
                Depth = new PoolDepth[w, h],
                Solid = new bool[w, h],
                Room = new int[w, h],
                Ceiling = new float[w, h],
                Sky = new PoolSky[w, h],
                EdgeSouth = new PoolEdge[w, h + 1],
                EdgeWest = new PoolEdge[w + 1, h]
            };

            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    d.Solid[x, z] = true;
                    d.Room[x, z] = -1;
                    d.Ceiling[x, z] = 0f;
                }
            for (int z = 0; z <= h; z++)
                for (int x = 0; x < w; x++)
                    d.EdgeSouth[x, z] = Unset;
            for (int z = 0; z < h; z++)
                for (int x = 0; x <= w; x++)
                    d.EdgeWest[x, z] = Unset;
            return d;
        }

        static Info AddRoom(Ctx c, Tpl t, PoolRoomKind kind, Frame f, float ceiling)
        {
            LevelData d = c.D;
            var room = new PoolRoom { Id = d.Rooms.Count, Kind = kind, X0 = f.X0, Z0 = f.Z0, Width = f.GW, Height = f.GH, Ceiling = ceiling };
            d.Rooms.Add(room);
            for (int z = room.Z0; z < room.Z0 + room.Height; z++)
                for (int x = room.X0; x < room.X0 + room.Width; x++)
                {
                    d.Solid[x, z] = false;
                    d.Room[x, z] = room.Id;
                    d.Depth[x, z] = PoolDepth.Shallow;
                    d.Ceiling[x, z] = ceiling;
                }
            var info = new Info { Room = room, T = t, F = f };
            c.Rooms.Add(info);
            if (ceiling >= TallCeiling - Eps) c.Tall++;
            return info;
        }

        // ---------------------------------------------------------------- Colocação das salas

        delegate double Bias(int x0, int z0, int gw, int gh);
        delegate bool Accept(int x0, int z0, int gw, int gh, bool swap);

        static bool Free(Ctx c, int x0, int z0, int gw, int gh)
        {
            for (int z = z0; z < z0 + gh; z++)
                for (int x = x0; x < x0 + gw; x++)
                    if (c.D.Room[x, z] >= 0 || c.Reserved[x, z]) return false;
            return true;
        }

        static int FreeCount(Ctx c)
        {
            int n = 0;
            for (int z = 0; z < c.H; z++)
                for (int x = 0; x < c.W; x++)
                    if (c.D.Room[x, z] < 0) n++;
            return n;
        }

        /// <summary>
        /// Quanto a sala encosta no que já existe: encostar em sala ou na borda é bom (mapa denso,
        /// passagens diretas); deixar uma tira vazia de 1 célula entre ela e outra sala é ruim.
        /// </summary>
        static double PackScore(Ctx c, int x0, int z0, int gw, int gh)
        {
            double s = 0;
            for (int k = 0; k < 4; k++)
            {
                int len = k < 2 ? gw : gh;
                for (int t = 0; t < len; t++)
                {
                    int ox, oz, dx = 0, dz = 0;
                    switch (k)
                    {
                        case 0: ox = x0 + t; oz = z0 - 1; dz = -1; break;
                        case 1: ox = x0 + t; oz = z0 + gh; dz = 1; break;
                        case 2: ox = x0 - 1; oz = z0 + t; dx = -1; break;
                        default: ox = x0 + gw; oz = z0 + t; dx = 1; break;
                    }
                    if (!In(c, ox, oz)) { s += 0.6; continue; }
                    if (c.D.Room[ox, oz] >= 0) { s += 1.0; continue; }
                    int bx = ox + dx, bz = oz + dz;
                    if (!In(c, bx, bz) || c.D.Room[bx, bz] >= 0) s -= 0.35;
                }
            }
            return s;
        }

        static bool In(Ctx c, int x, int z) => x >= 0 && z >= 0 && x < c.W && z < c.H;

        static Info Place(Ctx c, Tpl t, PoolRoomKind kind, List<(int lw, int lh)> sizes, float ceiling, Bias bias, double jitter, Accept accept = null, bool fixedFU = false)
        {
            foreach ((int lw, int lh) in sizes)
            {
                double best = double.NegativeInfinity;
                int bx = 0, bz = 0;
                bool bswap = false, found = false;
                for (int o = 0; o < 2; o++)
                {
                    bool swap = o == 1;
                    if (swap && lw == lh) continue;
                    int gw = swap ? lh : lw, gh = swap ? lw : lh;
                    for (int z0 = 0; z0 + gh <= c.H; z0++)
                        for (int x0 = 0; x0 + gw <= c.W; x0++)
                        {
                            if (!Free(c, x0, z0, gw, gh)) continue;
                            if (accept != null && !accept(x0, z0, gw, gh, swap)) continue;
                            double s = PackScore(c, x0, z0, gw, gh) + (bias != null ? bias(x0, z0, gw, gh) : 0) + c.R.NextDouble() * jitter;
                            if (s > best)
                            {
                                best = s;
                                bx = x0;
                                bz = z0;
                                bswap = swap;
                                found = true;
                            }
                        }
                }
                if (!found) continue;
                bool fu = !fixedFU && c.R.Next(2) == 0;
                bool fv = c.R.Next(2) == 0;
                return AddRoom(c, t, kind, new Frame(bx, bz, lw, lh, bswap, fu, fv), ceiling);
            }
            return null;
        }

        static double DistFromStart(Ctx c, double cx, double cz)
        {
            double dx = cx - c.StartCX, dz = cz - c.StartCZ;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        static int Quadrant(Ctx c, double cx, double cz) => (cx >= c.W * 0.5 ? 1 : 0) + (cz >= c.H * 0.5 ? 2 : 0);

        static bool TallInQuadrant(Ctx c, int q)
        {
            foreach (Info i in c.Rooms)
                if (i.Room.Ceiling >= TallCeiling - Eps && Quadrant(c, i.Room.X0 + i.Room.Width * 0.5, i.Room.Z0 + i.Room.Height * 0.5) == q)
                    return true;
            return false;
        }

        static int KindCount(Ctx c, PoolRoomKind kind)
        {
            int n = 0;
            foreach (Info i in c.Rooms) if (i.Room.Kind == kind) n++;
            return n;
        }

        static int CountT(Ctx c, Tpl t)
        {
            int n = 0;
            foreach (Info i in c.Rooms) if (i.T == t) n++;
            return n;
        }

        static void Shuffle<T>(List<T> list, Random r)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = r.Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>Tamanho sorteado primeiro, os outros em seguida (maiores antes), para cair num menor se não couber.</summary>
        static List<(int, int)> SizeOrder(Random r, List<(int lw, int lh)> all)
        {
            var rest = new List<(int lw, int lh)>(all);
            int first = r.Next(rest.Count);
            var order = new List<(int, int)> { rest[first] };
            rest.RemoveAt(first);
            // Maiores primeiro; desempate pela posição na lista (ordenação total).
            var idx = new List<int>();
            for (int i = 0; i < rest.Count; i++) idx.Add(i);
            idx.Sort((a, b) =>
            {
                int aa = rest[a].lw * rest[a].lh, bb = rest[b].lw * rest[b].lh;
                return aa != bb ? bb.CompareTo(aa) : a.CompareTo(b);
            });
            foreach (int i in idx) order.Add(rest[i]);
            return order;
        }

        static void PlaceStart(Ctx c)
        {
            Random r = c.R;
            int corner = r.Next(4);
            bool east = (corner & 1) != 0, north = (corner & 2) != 0;
            int x0 = east ? c.W - 3 - r.Next(2) : r.Next(2);
            int z0 = north ? c.H - 3 - r.Next(2) : r.Next(2);
            bool swap = r.Next(2) == 0;
            // v = 0 é a fileira rasa, virada para dentro do mapa.
            bool fv = swap ? !east : !north;
            var f = new Frame(x0, z0, 3, 3, swap, r.Next(2) == 0, fv);
            c.Start = AddRoom(c, Tpl.Start, PoolRoomKind.Start, f, MidCeiling);
            c.StartCX = x0 + 1.5;
            c.StartCZ = z0 + 1.5;
        }

        static bool PlaceAnchors(Ctx c)
        {
            Random r = c.R;
            double diag = Math.Sqrt(c.W * c.W + c.H * c.H);
            bool big = r.NextDouble() < (Math.Min(c.W, c.H) >= 20 ? 0.6 : 0.3);
            var wellSizes = big ? new List<(int, int)> { (6, 6), (5, 5) } : new List<(int, int)> { (5, 5) };
            c.Well = Place(c, Tpl.WellBig, PoolRoomKind.WellRoom, wellSizes, TallestCeiling,
                (x, z, gw, gh) => 1.6 * DistFromStart(c, x + gw * 0.5, z + gh * 0.5), 1.0,
                (x, z, gw, gh, s) => DistFromStart(c, x + gw * 0.5, z + gh * 0.5) >= (FarFraction + 0.05) * diag);
            if (c.Well == null) return false;

            var exitSizes = r.Next(2) == 0 ? new List<(int, int)> { (2, 3), (2, 2) } : new List<(int, int)> { (2, 2), (2, 3) };
            c.Exit = Place(c, Tpl.Exit, PoolRoomKind.Exit, exitSizes, LowCeiling,
                (x, z, gw, gh) => 1.0 * DistFromStart(c, x + gw * 0.5, z + gh * 0.5) + ((x == 0 || z == 0 || x + gw == c.W || z + gh == c.H) ? 3 : 0),
                1.5,
                (x, z, gw, gh, s) => DistFromStart(c, x + gw * 0.5, z + gh * 0.5) >= (FarFraction + 0.05) * diag);
            return c.Exit != null;
        }

        static void PlaceBag(Ctx c)
        {
            Random r = c.R;
            int small = Math.Min(c.W, c.H);
            PlaceHall(c, true);
            PlaceLane(c);
            PlaceBasin(c);
            PlaceAtrium(c);
            PlaceTerrace(c);
            PlaceHall(c, false);

            var extra = new List<Tpl>();
            if (r.NextDouble() < 0.5) extra.Add(Tpl.Canal);
            if (r.NextDouble() < 0.45) extra.Add(Tpl.Cistern);
            if (r.NextDouble() < 0.45) extra.Add(Tpl.WellCorner);
            if (r.NextDouble() < 0.45) extra.Add(Tpl.WindowHall);
            if (small >= 20 && r.NextDouble() < 0.6) extra.Add(Tpl.Basin);
            if (small >= 20 && r.NextDouble() < 0.5) extra.Add(Tpl.Canal);
            if (small >= 22 && r.NextDouble() < 0.5) extra.Add(Tpl.Lane);
            Shuffle(extra, r);
            foreach (Tpl t in extra)
            {
                switch (t)
                {
                    case Tpl.Canal: PlaceCanal(c); break;
                    case Tpl.Cistern: PlaceCistern(c); break;
                    case Tpl.WellCorner: PlaceWellCorner(c); break;
                    case Tpl.WindowHall: PlaceWindowHall(c); break;
                    case Tpl.Basin: PlaceBasin(c); break;
                    case Tpl.Lane: PlaceLane(c); break;
                }
            }
        }

        static int NarrowCount(Ctx c)
        {
            int n = 0;
            foreach (Info i in c.Rooms) if (i.MinDim <= 2) n++;
            return n;
        }

        static int CorridorCells(Ctx c)
        {
            int n = 0;
            foreach (Info i in c.Rooms) if (i.MinDim == 1) n += i.Room.Width * i.Room.Height;
            return n;
        }

        static bool NarrowAllowed(Ctx c) => (NarrowCount(c) + 1) <= 0.36 * (c.Rooms.Count + 1);

        static void FillGalleries(Ctx c)
        {
            Random r = c.R;
            double target = 0.19 + r.NextDouble() * 0.07;
            for (int guard = 0; guard < 16 && FreeCount(c) > target * c.N; guard++)
            {
                Info placed = null;
                if (KindCount(c, PoolRoomKind.Hall) < 2 && r.NextDouble() < 0.6) placed = PlaceHall(c, false);
                if (placed == null && KindCount(c, PoolRoomKind.Basin) < 2 && r.NextDouble() < 0.3) placed = PlaceBasin(c);
                if (placed == null && Math.Min(c.W, c.H) >= 20 && r.NextDouble() < 0.6) placed = PlaceLane(c);
                if (placed == null && NarrowAllowed(c)) placed = PlaceGallery(c);
                if (placed == null && KindCount(c, PoolRoomKind.Canal) < 2) placed = PlaceCanal(c);
                if (placed == null && KindCount(c, PoolRoomKind.Terrace) < 2) placed = PlaceTerrace(c);
                // Mapas grandes: a raia longa só pode aparecer 2 vezes, então outras salas largas enchem.
                if (placed == null && Math.Min(c.W, c.H) >= 22) placed = PlaceCistern(c);
                if (placed == null && Math.Min(c.W, c.H) >= 22) placed = PlaceWellCorner(c);
                if (placed == null && Math.Min(c.W, c.H) >= 22) placed = PlaceAtrium(c);
                if (placed == null) break;
            }
        }

        static Info PlaceHall(Ctx c, bool first)
        {
            if (KindCount(c, PoolRoomKind.Hall) >= 2) return null;
            Random r = c.R;
            int small = Math.Min(c.W, c.H), max = small >= 22 ? 8 : small >= 20 ? 7 : 6;
            var all = new List<(int lw, int lh)>();
            for (int lw = 4; lw <= max; lw++)
                for (int lh = lw; lh <= max; lh++)
                    if (!first || lw * lh >= 25) all.Add((lw, lh));
            List<(int, int)> sizes = SizeOrder(r, all);
            if (first)
                for (int lw = 4; lw <= 5; lw++)
                    for (int lh = lw; lh <= 5; lh++)
                        if (lw * lh < 25) sizes.Add((lw, lh));
            (int a, int b) = sizes[0];
            float ceiling = a * b >= 25 && r.NextDouble() < 0.7 ? MidCeiling : LowCeiling;
            return Place(c, Tpl.Hall, PoolRoomKind.Hall, sizes, ceiling, null, 0.8);
        }

        static Info PlaceLane(Ctx c)
        {
            if (KindCount(c, PoolRoomKind.Lane) >= 2) return null;
            Random r = c.R;
            int max = Math.Max(c.W, c.H) - 2;
            var all = new List<(int, int)>();
            for (int len = 8; len <= 10 && len <= max; len++) all.Add((3, len));
            if (all.Count == 0) return null;
            float ceiling = r.NextDouble() < 0.5 ? LowCeiling : MidCeiling;
            return Place(c, Tpl.Lane, PoolRoomKind.Lane, SizeOrder(r, all), ceiling, null, 0.8);
        }

        static Info PlaceBasin(Ctx c)
        {
            if (KindCount(c, PoolRoomKind.Basin) >= 2) return null;
            var all = new List<(int, int)> { (5, 5), (5, 6) };
            if (Math.Min(c.W, c.H) >= 20) all.Add((6, 6));
            if (Math.Min(c.W, c.H) >= 22) { all.Add((6, 7)); all.Add((7, 7)); }
            return Place(c, Tpl.Basin, PoolRoomKind.Basin, SizeOrder(c.R, all), MidCeiling, null, 0.8);
        }

        static Bias TallBias(Ctx c) => (x, z, gw, gh) => TallInQuadrant(c, Quadrant(c, x + gw * 0.5, z + gh * 0.5)) ? 0 : 6;

        static Info PlaceAtrium(Ctx c)
        {
            if (KindCount(c, PoolRoomKind.Atrium) >= 2) return null;
            bool tall = c.Tall < MaxTallRooms;
            return Place(c, Tpl.Atrium, PoolRoomKind.Atrium, new List<(int, int)> { (3, 3) }, tall ? TallestCeiling : MidCeiling, tall ? TallBias(c) : null, 0.8);
        }

        static Info PlaceTerrace(Ctx c)
        {
            if (KindCount(c, PoolRoomKind.Terrace) >= 2) return null;
            Random r = c.R;
            var all = new List<(int, int)>();
            for (int lw = 2; lw <= (Math.Min(c.W, c.H) >= 22 ? 5 : 4); lw++)
            {
                all.Add((lw, 4));
                if (r.NextDouble() < 0.4) all.Add((lw, 3));
            }
            bool tall = c.Tall < 3;
            float ceiling = tall ? (r.NextDouble() < 0.5 ? TallCeiling : TallestCeiling) : MidCeiling;
            return Place(c, Tpl.Terrace, PoolRoomKind.Terrace, SizeOrder(r, all), ceiling, tall ? TallBias(c) : null, 0.8);
        }

        static Info PlaceCanal(Ctx c)
        {
            if (KindCount(c, PoolRoomKind.Canal) >= 2) return null;
            Random r = c.R;
            var all = new List<(int, int)> { (3, 4), (3, 5), (3, 6) };
            if (Math.Min(c.W, c.H) >= 22) { all.Add((3, 7)); all.Add((3, 8)); }
            float ceiling = r.NextDouble() < 0.6 ? LowCeiling : MidCeiling;
            return Place(c, Tpl.Canal, PoolRoomKind.Canal, SizeOrder(r, all), ceiling, null, 0.8);
        }

        static Info PlaceCistern(Ctx c)
        {
            if (KindCount(c, PoolRoomKind.Cistern) >= (Math.Min(c.W, c.H) >= 22 ? 2 : 1)) return null;
            return Place(c, Tpl.Cistern, PoolRoomKind.Cistern, new List<(int, int)> { (4, 4) }, LowCeiling, null, 0.8);
        }

        static Info PlaceWellCorner(Ctx c)
        {
            if (KindCount(c, PoolRoomKind.WellRoom) >= 2) return null;
            bool tall = c.Tall < MaxTallRooms && c.R.NextDouble() < 0.5;
            double diag = Math.Sqrt(c.W * c.W + c.H * c.H);
            return Place(c, Tpl.WellCorner, PoolRoomKind.WellRoom, new List<(int, int)> { (3, 3) }, tall ? TallCeiling : MidCeiling,
                (x, z, gw, gh) => 0.3 * DistFromStart(c, x + gw * 0.5, z + gh * 0.5) + (tall && !TallInQuadrant(c, Quadrant(c, x + gw * 0.5, z + gh * 0.5)) ? 4 : 0),
                0.8, (x, z, gw, gh, s) => DistFromStart(c, x + gw * 0.5, z + gh * 0.5) >= 0.3 * diag);
        }

        static Info PlaceGallery(Ctx c)
        {
            var all = new List<(int, int)> { (2, 4), (2, 5), (2, 6) };
            return Place(c, Tpl.Gallery, PoolRoomKind.Gallery, SizeOrder(c.R, all), LowCeiling, null, 0.8);
        }

        /// <summary>
        /// Corredor de janelas: o lado comprido local Oeste (FU fixo) cai na parede Sul (deitado) ou
        /// Oeste (em pé) da sala, e a tira de fora dele fica reservada como maciço (ou é a borda).
        /// </summary>
        static Info PlaceWindowHall(Ctx c)
        {
            if (!NarrowAllowed(c)) return null;
            Random r = c.R;
            var all = new List<(int lw, int lh)>();
            for (int len = 4; len <= 8; len++)
            {
                all.Add((2, len));
                if (CorridorCells(c) + len <= 0.08 * c.N) all.Add((1, len));
            }
            Accept strip = (x0, z0, gw, gh, swap) =>
            {
                if (swap)
                {
                    if (z0 == 0) return true;
                    for (int x = x0; x < x0 + gw; x++) if (c.D.Room[x, z0 - 1] >= 0) return false;
                    return true;
                }
                if (x0 == 0) return true;
                for (int z = z0; z < z0 + gh; z++) if (c.D.Room[x0 - 1, z] >= 0) return false;
                return true;
            };
            Bias border = (x0, z0, gw, gh) => (gw > gh ? z0 == 0 : x0 == 0) ? 2 : 0;
            Info i = Place(c, Tpl.WindowHall, PoolRoomKind.WindowGallery, SizeOrder(r, all), LowCeiling, border, 0.8, strip, true);
            if (i == null) return null;
            if (i.F.Swap)
            {
                if (i.Room.Z0 > 0) for (int x = i.Room.X0; x < i.Room.X0 + i.Room.Width; x++) c.Reserved[x, i.Room.Z0 - 1] = true;
            }
            else if (i.Room.X0 > 0)
                for (int z = i.Room.Z0; z < i.Room.Z0 + i.Room.Height; z++) c.Reserved[i.Room.X0 - 1, z] = true;
            return i;
        }

        static bool DecideWells(Ctx c)
        {
            Random r = c.R;
            int wells = 0;
            var options = new List<Info>();
            foreach (Info i in c.Rooms)
            {
                if (i.T == Tpl.WellBig) { i.HasWell = true; wells += i.F.LW >= 6 ? 4 : 1; }
                else if (i.T == Tpl.WellCorner) { i.HasWell = true; wells++; }
                else if (i.T == Tpl.Basin || (i.T == Tpl.Terrace && i.F.LH >= 4)) options.Add(i);
            }
            int target = MinWells + r.Next(3);
            Shuffle(options, r);
            foreach (Info o in options)
            {
                if (wells >= target) break;
                o.HasWell = true;
                wells++;
            }
            return wells >= MinWells && wells <= MaxWells;
        }

        // ---------------------------------------------------------------- Modelos das salas

        static void SetDepth(Ctx c, Info i, int u, int v, PoolDepth p)
        {
            i.F.Cell(u, v, out int x, out int z);
            c.D.Depth[x, z] = p;
        }

        static void SetAll(Ctx c, Info i, PoolDepth p)
        {
            for (int v = 0; v < i.F.LH; v++)
                for (int u = 0; u < i.F.LW; u++)
                    SetDepth(c, i, u, v, p);
        }

        static bool StairLocal(Ctx c, Info i, int u, int v, WallSide localSide, float width)
        {
            i.F.Cell(u, v, out int x, out int z);
            return TryStair(c.D, x, z, i.F.Side(localSide), width);
        }

        // Sorteio antigo das claraboias (vai para OldSky, sem uso): ver PlaceSkies.
        static void SkyLocal(Ctx c, Info i, int u, int v, PoolSky sky, bool primary = false)
        {
            i.F.Cell(u, v, out int x, out int z);
            c.OldSky[x, z] = sky;
            c.KeepSky[x, z] = true;
            if (primary) c.PrimarySky[x, z] = true;
        }

        static void ColumnLocal(Info i, int a, int b, float radius, bool round)
        {
            i.F.Vertex(a, b, out int x, out int z);
            i.Want.Add(new PoolColumn { X = x * CellSize, Z = z * CellSize, Radius = radius, Round = round });
        }

        /// <summary>Rasgo de claraboia no sentido do comprimento (v) da sala.</summary>
        static PoolSky SlotAlong(Info i) => i.F.Swap ? PoolSky.SlotX : PoolSky.SlotZ;

        static void Fill(Ctx c, Info i)
        {
            Random r = c.R;
            int lw = i.F.LW, lh = i.F.LH;
            switch (i.T)
            {
                case Tpl.Start:
                    for (int u = 0; u < 3; u++)
                    {
                        SetDepth(c, i, u, 0, PoolDepth.Shallow);
                        SetDepth(c, i, u, 1, PoolDepth.Dry);
                        SetDepth(c, i, u, 2, PoolDepth.Dry);
                    }
                    for (int u = 0; u < 3; u++) StairLocal(c, i, u, 0, WallSide.North, 4f);
                    SkyLocal(c, i, 1, 1, PoolSky.Round, true);
                    i.SkyStyle = PoolSky.Square;
                    break;

                case Tpl.Exit:
                    SetAll(c, i, PoolDepth.Dry);
                    i.SkyStyle = PoolSky.Square;
                    if (r.NextDouble() < 0.6) SkyLocal(c, i, r.Next(lw), r.Next(lh), PoolSky.Square);
                    break;

                case Tpl.Hall: FillHall(c, i); break;

                case Tpl.Atrium:
                {
                    SetAll(c, i, PoolDepth.Shallow);
                    SetDepth(c, i, 1, 1, PoolDepth.Dry);
                    var opts = new List<(int u, int v, WallSide s)> { (1, 0, WallSide.North), (1, 2, WallSide.South), (0, 1, WallSide.East), (2, 1, WallSide.West) };
                    Shuffle(opts, r);
                    int n = 2 + r.Next(3);
                    float width = r.NextDouble() < 0.6 ? 4f : 2.4f;
                    for (int k = 0; k < n; k++) StairLocal(c, i, opts[k].u, opts[k].v, opts[k].s, width);
                    SkyLocal(c, i, 1, 1, PoolSky.Round, true);
                    break;
                }

                case Tpl.Basin: FillBasin(c, i); break;

                case Tpl.Canal:
                {
                    PoolDepth side = r.NextDouble() < 0.6 ? PoolDepth.Dry : PoolDepth.Shallow;
                    SetAll(c, i, side);
                    bool both = lh >= 5 && r.NextDouble() < 0.5;
                    int vs = 1, ve = both ? lh - 2 : lh - 1;
                    for (int v = vs; v <= ve; v++) SetDepth(c, i, 1, v, PoolDepth.Deep);
                    StairLocal(c, i, 1, vs + r.Next(ve - vs + 1), r.Next(2) == 0 ? WallSide.West : WallSide.East, r.NextDouble() < 0.5 ? 4f : 2.4f);
                    i.SkyStyle = SlotAlong(i);
                    for (int v = vs; v <= ve; v += 2) SkyLocal(c, i, 1, v, SlotAlong(i));
                    i.LampWish = 1 + r.Next(2);
                    break;
                }

                case Tpl.Lane:
                {
                    SetAll(c, i, PoolDepth.Shallow);
                    for (int v = 1; v <= lh - 2; v++) SetDepth(c, i, 1, v, PoolDepth.Deep);
                    // Entrada numa ponta só (e às vezes um lado junto dela): com escadas nas duas
                    // pontas, o caminho mais curto podia correr a raia inteira dentro d'água.
                    StairLocal(c, i, 1, 1, WallSide.South, 2.4f);
                    if (r.NextDouble() < 0.5) StairLocal(c, i, 1, 2, r.Next(2) == 0 ? WallSide.West : WallSide.East, 2.4f);
                    PoolSky sky = r.NextDouble() < 0.5 ? PoolSky.Full : SlotAlong(i);
                    for (int v = 0; v < lh; v++) SkyLocal(c, i, 1, v, sky, true);   // faixa contínua: nunca se corta
                    i.SkyStyle = PoolSky.None;
                    int a = r.Next(2) == 0 ? 1 : 2;
                    bool round = r.NextDouble() < 0.5;
                    float radius = round ? 0.45f : 0.35f;
                    for (int b = 1; b < lh; b++) ColumnLocal(i, a, b, radius, round);
                    i.LampWish = 1 + r.Next(2);
                    break;
                }

                case Tpl.Gallery:
                {
                    SetAll(c, i, r.NextDouble() < 0.7 ? PoolDepth.Shallow : PoolDepth.Dry);
                    float radius = 0.3f + 0.1f * r.Next(3);
                    for (int b = 1; b < lh; b++) ColumnLocal(i, 1, b, radius, false);
                    i.SkyStyle = SlotAlong(i);
                    if (r.NextDouble() < 0.6)
                        for (int v = 1; v < lh; v += 2)
                            for (int u = 0; u < lw; u++)
                                SkyLocal(c, i, u, v, SlotAlong(i));
                    break;
                }

                case Tpl.WindowHall:
                    SetAll(c, i, PoolDepth.Shallow);
                    i.WinType = r.NextDouble() < 0.5 ? PoolEdge.Slits : PoolEdge.RoundWindow;
                    i.SkyStyle = PoolSky.None;
                    break;

                case Tpl.Terrace:
                {
                    for (int u = 0; u < lw; u++)
                    {
                        SetDepth(c, i, u, 0, PoolDepth.Dry);
                        SetDepth(c, i, u, 1, PoolDepth.Shallow);
                        SetDepth(c, i, u, 2, PoolDepth.Deep);
                        if (lh >= 4) SetDepth(c, i, u, 3, PoolDepth.Deep);
                    }
                    int wu = -1;
                    if (i.HasWell && lh >= 4)
                    {
                        wu = r.Next(lw);
                        SetDepth(c, i, wu, 3, PoolDepth.Well);
                    }
                    for (int u = 0; u < lw; u++)
                    {
                        StairLocal(c, i, u, 1, WallSide.South, 4f);
                        StairLocal(c, i, u, 2, WallSide.South, 4f);
                    }
                    if (wu >= 0) StairLocal(c, i, wu, 3, WallSide.South, 2.4f);
                    i.SkyStyle = r.NextDouble() < 0.5 ? PoolSky.Full : PoolSky.Square;
                    if (r.NextDouble() < 0.5)
                        for (int u = 0; u < lw; u++) SkyLocal(c, i, u, 0, PoolSky.Full);
                    i.LampWish = 1 + r.Next(2);
                    break;
                }

                case Tpl.WellBig: FillWellBig(c, i); break;

                case Tpl.WellCorner:
                {
                    for (int v = 0; v < 3; v++)
                    {
                        SetDepth(c, i, 0, v, PoolDepth.Shallow);
                        SetDepth(c, i, 1, v, PoolDepth.Deep);
                        SetDepth(c, i, 2, v, PoolDepth.Deep);
                    }
                    SetDepth(c, i, 2, 1, PoolDepth.Well);
                    StairLocal(c, i, 1, r.Next(3), WallSide.West, r.NextDouble() < 0.5 ? 4f : 2.4f);
                    i.Room.Dark = r.NextDouble() < 0.5;
                    if (!i.Room.Dark) SkyLocal(c, i, 2, 1, PoolSky.Round, true);
                    i.LampWish = 1 + r.Next(2);
                    break;
                }

                case Tpl.Cistern:
                {
                    SetAll(c, i, PoolDepth.Deep);
                    SetDepth(c, i, 0, 0, PoolDepth.Shallow);
                    SetDepth(c, i, 3, 0, PoolDepth.Shallow);
                    SetDepth(c, i, 0, 3, PoolDepth.Shallow);
                    SetDepth(c, i, 3, 3, PoolDepth.Shallow);
                    float radius = 0.3f + 0.1f * r.Next(2);
                    for (int a = 1; a <= 3; a++)
                        for (int b = 1; b <= 3; b++)
                            ColumnLocal(i, a, b, radius, false);
                    // Escadas de canto a canto pelas bordas: atravessar uma ponta é no máximo 2 fundas.
                    StairLocal(c, i, 1, 0, WallSide.West, 2.4f);
                    StairLocal(c, i, 2, 0, WallSide.East, 2.4f);
                    StairLocal(c, i, 1, 3, WallSide.West, 2.4f);
                    StairLocal(c, i, 2, 3, WallSide.East, 2.4f);
                    StairLocal(c, i, 0, 1, WallSide.South, 2.4f);
                    StairLocal(c, i, 0, 2, WallSide.North, 2.4f);
                    StairLocal(c, i, 3, 1, WallSide.South, 2.4f);
                    StairLocal(c, i, 3, 2, WallSide.North, 2.4f);
                    i.Room.Dark = true;
                    i.LampWish = 2 + r.Next(3);
                    break;
                }
            }
        }

        static void FillHall(Ctx c, Info i)
        {
            Random r = c.R;
            int lw = i.F.LW, lh = i.F.LH;
            SetAll(c, i, PoolDepth.Shallow);
            i.SkyStyle = r.NextDouble() < 0.5 ? PoolSky.Round : PoolSky.Square;
            i.Room.Accent = r.NextDouble() < 0.3;

            // Raia funda de 1 célula no sentido do comprimento (às vezes de parede a parede).
            if (r.NextDouble() < 0.45)
            {
                int lu = 1 + r.Next(lw - 2);
                bool full = lh < 4 || r.NextDouble() < 0.4;
                int vs = full ? 0 : 1, ve = full ? lh - 1 : lh - 2;
                for (int v = vs; v <= ve; v++) SetDepth(c, i, lu, v, PoolDepth.Deep);
                for (int v = vs; v <= ve; v++) SkyLocal(c, i, lu, v, SlotAlong(i));
                if (full)
                {
                    // Uma escada para cada lado, em células vizinhas: as duas, de 8 degraus (2,4 m),
                    // não cabem opostas na mesma célula.
                    int vc = lh / 2;
                    StairLocal(c, i, lu, vc, WallSide.West, 2.4f);
                    StairLocal(c, i, lu, vc - 1, WallSide.East, 2.4f);
                }
                else
                {
                    StairLocal(c, i, lu, vs, WallSide.South, 2.4f);
                    if (r.NextDouble() < 0.5 && vs + 1 <= ve) StairLocal(c, i, lu, vs + 1, r.Next(2) == 0 ? WallSide.West : WallSide.East, 2.4f);
                }
                i.LampWish = 1 + r.Next(2);
            }

            // Tambores lisos nos vértices: a cada 4 m, a cada 8 m (vértice sim, vértice não) ou esbeltos.
            int mode = r.Next(100);
            float radius = mode < 75 ? 0.6f + 0.1f * r.Next(4) : 0.45f;
            bool every8 = mode >= 40 && mode < 75;
            for (int a = 1; a < lw; a++)
                for (int b = 1; b < lh; b++)
                {
                    if (every8 && (a + b) % 2 != 0) continue;
                    ColumnLocal(i, a, b, radius, true);
                }
        }

        static void FillBasin(Ctx c, Info i)
        {
            Random r = c.R;
            int lw = i.F.LW, lh = i.F.LH;
            SetAll(c, i, PoolDepth.Dry);
            for (int v = 1; v <= lh - 2; v++)
                for (int u = 1; u <= lw - 2; u++)
                    SetDepth(c, i, u, v, PoolDepth.Deep);

            if (i.HasWell)
            {
                var wells = new List<(int, int)>();
                for (int v = 2; v <= lh - 3; v++)
                    for (int u = 2; u <= lw - 3; u++)
                        wells.Add((u, v));
                (int wu, int wv) = wells[r.Next(wells.Count)];
                SetDepth(c, i, wu, wv, PoolDepth.Well);
            }

            var stairs = new List<(int u, int v, WallSide s)>();
            for (int u = 2; u <= lw - 3; u++)
            {
                stairs.Add((u, 1, WallSide.South));
                stairs.Add((u, lh - 2, WallSide.North));
            }
            for (int v = 2; v <= lh - 3; v++)
            {
                stairs.Add((1, v, WallSide.West));
                stairs.Add((lw - 2, v, WallSide.East));
            }
            Shuffle(stairs, r);
            PlaceSameSideStairs(c, i, stairs, 1 + r.Next(2), r.NextDouble() < 0.5 ? 4f : 2.4f);

            int row = lh / 2;
            for (int u = 0; u < lw; u++) SkyLocal(c, i, u, row, PoolSky.Full);
            i.SkyStyle = PoolSky.Full;
            i.Room.Accent = true;
            i.LampWish = 2 + r.Next(2);
        }

        /// <summary>
        /// Escadas de entrada na água, todas do mesmo lado: escadas em lados opostos fariam o
        /// caminho mais curto atravessar a piscina por dentro.
        /// </summary>
        static void PlaceSameSideStairs(Ctx c, Info i, List<(int u, int v, WallSide s)> options, int count, float width)
        {
            if (options.Count == 0) return;
            WallSide side = options[0].s;
            int placed = 0;
            foreach (var o in options)
            {
                if (placed >= count) break;
                if (o.s != side) continue;
                if (StairLocal(c, i, o.u, o.v, o.s, width)) placed++;
            }
        }

        static void FillWellBig(Ctx c, Info i)
        {
            Random r = c.R;
            int n = i.F.LW;
            SetAll(c, i, PoolDepth.Shallow);
            for (int v = 1; v <= n - 2; v++)
                for (int u = 1; u <= n - 2; u++)
                    SetDepth(c, i, u, v, PoolDepth.Deep);
            var wells = new List<(int u, int v)>();
            if (n >= 6) { wells.Add((2, 2)); wells.Add((3, 2)); wells.Add((2, 3)); wells.Add((3, 3)); }
            else wells.Add((2, 2));
            foreach ((int u, int v) in wells) SetDepth(c, i, u, v, PoolDepth.Well);

            var stairs = new List<(int u, int v, WallSide s)>();
            for (int u = 2; u <= n - 3; u++)
            {
                stairs.Add((u, 1, WallSide.South));
                stairs.Add((u, n - 2, WallSide.North));
            }
            for (int v = 2; v <= n - 3; v++)
            {
                stairs.Add((1, v, WallSide.West));
                stairs.Add((n - 2, v, WallSide.East));
            }
            Shuffle(stairs, r);
            PlaceSameSideStairs(c, i, stairs, 1 + r.Next(2), r.NextDouble() < 0.5 ? 4f : 2.4f);

            // Escada de sair do poço nadando (os degraus somem na água escura). Com a água funda a
            // -2,0 m ela não cabe mais (o piso da funda em volta é mais baixo que o pé dela,
            // LevelData.WellStairFoot) e TryStair recusa; o sorteio continua, para não mudar o resto.
            if (r.NextDouble() < 0.5)
            {
                (int wu, int wv) = wells[r.Next(wells.Count)];
                var sides = new List<WallSide>();
                foreach (WallSide s in Sides)
                {
                    int nu = wu + (s == WallSide.East ? 1 : s == WallSide.West ? -1 : 0);
                    int nv = wv + (s == WallSide.North ? 1 : s == WallSide.South ? -1 : 0);
                    bool isWell = false;
                    foreach ((int u, int v) in wells) if (u == nu && v == nv) isWell = true;
                    if (!isWell) sides.Add(s);
                }
                StairLocal(c, i, wu, wv, sides[r.Next(sides.Count)], 2.4f);
            }

            i.Room.Dark = r.NextDouble() < 0.5;
            if (!i.Room.Dark)
            {
                (int su, int sv) = wells[r.Next(wells.Count)];
                SkyLocal(c, i, su, sv, PoolSky.Round, true);
            }
            i.LampWish = 2 + r.Next(3);
        }

        // ---------------------------------------------------------------- Escadas

        static float FootHeight(LevelData d, int x, int z) => d.Depth[x, z] == PoolDepth.Well ? LevelData.WellStairFoot : d.FloorHeight(x, z);

        static int StepsFor(LevelData d, int x, int z, int ux, int uz) => (int)Math.Round((d.FloorHeight(ux, uz) - FootHeight(d, x, z)) / LevelData.StepRise);

        static void StairRect(int x, int z, WallSide side, float width, int steps, out float x0, out float z0, out float x1, out float z1)
        {
            float depth = Math.Min(CellSize, steps * Tread);
            float cx = x * CellSize, cz = z * CellSize, half = width * 0.5f, mid = CellSize * 0.5f;
            switch (side)
            {
                case WallSide.South: x0 = cx + mid - half; x1 = cx + mid + half; z0 = cz; z1 = cz + depth; break;
                case WallSide.North: x0 = cx + mid - half; x1 = cx + mid + half; z0 = cz + CellSize - depth; z1 = cz + CellSize; break;
                case WallSide.West: x0 = cx; x1 = cx + depth; z0 = cz + mid - half; z1 = cz + mid + half; break;
                default: x0 = cx + CellSize - depth; x1 = cx + CellSize; z0 = cz + mid - half; z1 = cz + mid + half; break;
            }
        }

        static bool RectsOverlap(PoolStair a, PoolStair b)
        {
            StairRect(a.X, a.Z, a.Side, a.Width, a.Steps, out float ax0, out float az0, out float ax1, out float az1);
            StairRect(b.X, b.Z, b.Side, b.Width, b.Steps, out float bx0, out float bz0, out float bx1, out float bz1);
            return ax0 < bx1 - Eps && bx0 < ax1 - Eps && az0 < bz1 - Eps && bz0 < az1 - Eps;
        }

        /// <summary>Problema de convivência da escada com as que já existem; null se cabe.</summary>
        static string StairConflict(LevelData d, PoolStair s, int skipIndex)
        {
            d.Neighbor(s.X, s.Z, s.Side, out int nx, out int nz);
            for (int k = 0; k < d.Stairs.Count; k++)
            {
                if (k == skipIndex) continue;
                PoolStair o = d.Stairs[k];
                if (o.X == nx && o.Z == nz && o.Side == LevelData.Opposite(s.Side)) return "duas escadas na mesma divisa";
                if (o.X != s.X || o.Z != s.Z) continue;
                if (o.Side == s.Side) return "duas escadas na mesma divisa";
                if (o.Side == LevelData.Opposite(s.Side))
                {
                    if ((o.Steps - 1) * Tread + (s.Steps - 1) * Tread > 3.4f + Eps) return "escadas opostas não cabem na célula";
                }
                else if (RectsOverlap(o, s)) return "escadas se cruzam no canto da célula";
            }
            return null;
        }

        /// <summary>Escada na célula de baixo (x, z), subindo pelo lado <paramref name="side"/>, se couber.</summary>
        static bool TryStair(LevelData d, int x, int z, WallSide side, float width)
        {
            if (!d.Neighbor(x, z, side, out int ux, out int uz)) return false;
            if (d.Depth[ux, uz] == PoolDepth.Well) return false;
            int steps = StepsFor(d, x, z, ux, uz);
            if (steps < 1 || steps > 12) return false;
            var s = new PoolStair { X = x, Z = z, Side = side, Steps = steps, Width = width };
            if (StairConflict(d, s, -1) != null) return false;
            d.Stairs.Add(s);
            return true;
        }

        // ================================================================ Ligações

        static bool Passable(PoolEdge e) => e == PoolEdge.Open || e == PoolEdge.Arch || e == PoolEdge.WideArch || e == PoolEdge.Doorway;
        static bool WindowLike(PoolEdge e) => e == PoolEdge.Window || e == PoolEdge.Slits || e == PoolEdge.RoundWindow;
        static bool Glazed(PoolEdge e) => WindowLike(e) || e == PoolEdge.GlassBlock;

        /// <summary>Vão útil da divisa (largura máxima de uma escada nela).</summary>
        static float Span(PoolEdge e)
        {
            switch (e)
            {
                case PoolEdge.Open: return 4f;
                case PoolEdge.WideArch: return 3.4f;
                case PoolEdge.Arch: return 2.4f;
                case PoolEdge.Doorway: return 1.4f;
                default: return 0f;
            }
        }

        static void SetEdge(LevelData d, int x, int z, WallSide side, PoolEdge edge)
        {
            switch (side)
            {
                case WallSide.South: d.EdgeSouth[x, z] = edge; break;
                case WallSide.North: d.EdgeSouth[x, z + 1] = edge; break;
                case WallSide.West: d.EdgeWest[x, z] = edge; break;
                default: d.EdgeWest[x + 1, z] = edge; break;
            }
        }

        /// <summary>Lado comprido de sala de 1 célula de largura (túnel, fenda, corredor).</summary>
        static bool IsLongSide(PoolRoom room, WallSide side)
        {
            if (room.Width == 1 && room.Height > 1) return side == WallSide.West || side == WallSide.East;
            if (room.Height == 1 && room.Width > 1) return side == WallSide.South || side == WallSide.North;
            return false;
        }

        static bool IsCorridorKind(Tpl t) => t == Tpl.Tunnel || t == Tpl.Fenda;
        static bool IsCorridorKind(PoolRoomKind k) => k == PoolRoomKind.Tunnel || k == PoolRoomKind.Slot;

        /// <summary>Pode haver passagem por este lado da célula? Só em seco ou raso, e nunca no lado comprido de túnel ou fenda.</summary>
        static bool Doorable(Ctx c, int x, int z, WallSide side)
        {
            int id = c.D.Room[x, z];
            if (id < 0) return false;
            PoolDepth p = c.D.Depth[x, z];
            if (p != PoolDepth.Dry && p != PoolDepth.Shallow) return false;
            Info i = c.Rooms[id];
            if ((i.Room.Vault || IsCorridorKind(i.T)) && IsLongSide(i.Room, side)) return false;
            return true;
        }

        delegate void CellSide(int x, int z, WallSide side);

        /// <summary>Células da borda da sala com o lado que dá para fora, em ordem fixa (Sul, Norte, Oeste, Leste).</summary>
        static void ForPerimeter(PoolRoom room, CellSide f)
        {
            for (int x = room.X0; x < room.X0 + room.Width; x++) f(x, room.Z0, WallSide.South);
            for (int x = room.X0; x < room.X0 + room.Width; x++) f(x, room.Z0 + room.Height - 1, WallSide.North);
            for (int z = room.Z0; z < room.Z0 + room.Height; z++) f(room.X0, z, WallSide.West);
            for (int z = room.Z0; z < room.Z0 + room.Height; z++) f(room.X0 + room.Width - 1, z, WallSide.East);
        }

        static bool IsBig(Info i) =>
            i.T == Tpl.Hall || i.T == Tpl.Basin || i.T == Tpl.Atrium || i.T == Tpl.WellBig || i.T == Tpl.Terrace ||
            i.T == Tpl.Start || i.T == Tpl.Lane || i.T == Tpl.Canal;

        static bool DeadEndOk(Tpl t) => t == Tpl.Exit || t == Tpl.WellCorner || t == Tpl.Cistern;

        static void AddLink(Ctx c, int a, int b)
        {
            if (!c.Rooms[a].Links.Contains(b)) c.Rooms[a].Links.Add(b);
            if (!c.Rooms[b].Links.Contains(a)) c.Rooms[b].Links.Add(a);
        }

        static int Loops(Ctx c)
        {
            int pairs = 0;
            foreach (Info i in c.Rooms) pairs += i.Links.Count;
            return pairs / 2 - c.Rooms.Count + 1;
        }

        static int Find(List<int> uf, int a)
        {
            while (uf[a] != a)
            {
                uf[a] = uf[uf[a]];
                a = uf[a];
            }
            return a;
        }

        static void Union(List<int> uf, int a, int b) => uf[Find(uf, a)] = Find(uf, b);

        static string Link(Ctx c)
        {
            Random r = c.R;
            LevelData d = c.D;
            int n = c.Rooms.Count;
            var cands = new List<Cand>();

            // Salas que se encostam: uma candidata por par.
            var seen = new bool[n * n];
            foreach (Info a in c.Rooms)
            {
                ForPerimeter(a.Room, (x, z, side) =>
                {
                    if (!d.Neighbor(x, z, side, out int nx, out int nz)) return;
                    int b = d.Room[nx, nz];
                    if (b <= a.Id || seen[a.Id * n + b]) return;
                    seen[a.Id * n + b] = true;
                    cands.Add(new Cand { A = a.Id, B = b });
                });
            }

            // Túneis pelo maciço: 2 a 5 células em linha reta entre duas salas.
            foreach (Info a in c.Rooms)
            {
                ForPerimeter(a.Room, (x, z, side) =>
                {
                    if (!Doorable(c, x, z, side)) return;
                    int cx = x, cz = z, len = 0;
                    while (true)
                    {
                        if (!d.Neighbor(cx, cz, side, out int nx, out int nz)) return;
                        int b = d.Room[nx, nz];
                        if (b < 0)
                        {
                            if (c.Reserved[nx, nz]) return;
                            len++;
                            if (len > 5) return;
                            cx = nx;
                            cz = nz;
                            continue;
                        }
                        if (len >= 2 && b > a.Id && Doorable(c, nx, nz, LevelData.Opposite(side)))
                            cands.Add(new Cand { A = a.Id, B = b, Tunnel = true, X = x, Z = z, Side = side, Len = len });
                        return;
                    }
                });
            }

            for (int k = 0; k < cands.Count; k++)
            {
                Cand cd = cands[k];
                cd.Index = k;
                bool big = IsBig(c.Rooms[cd.A]) && IsBig(c.Rooms[cd.B]);
                cd.Weight = r.NextDouble() + (cd.Tunnel ? 0.3 : 0) - (big ? 0.2 : 0);
            }
            var order = new List<Cand>(cands);
            order.Sort((p, q) => p.Weight != q.Weight ? p.Weight.CompareTo(q.Weight) : p.Index.CompareTo(q.Index));

            var uf = new List<int>();
            for (int k = 0; k < n; k++) uf.Add(k);

            foreach (Cand k in order)
            {
                if (Find(uf, k.A) == Find(uf, k.B)) continue;
                if (Realize(c, k, uf)) Union(uf, k.A, k.B);
            }

            int root = Find(uf, c.Start.Id);
            foreach (Info i in c.Rooms)
                if (Find(uf, i.Id) != root) return "salas que não se ligam ao resto";

            // Laços: pelo menos max(2, 30% das salas) ligações além da árvore.
            int nonTunnel = 0;
            foreach (Info i in c.Rooms) if (!IsCorridorKind(i.T)) nonTunnel++;
            int target = Math.Max(2, (int)Math.Ceiling(0.3 * nonTunnel)) + r.Next(2);

            var rest = new List<Cand>();
            foreach (Cand k in order) if (!k.Done) rest.Add(k);
            foreach (Cand k in rest) k.Weight = r.NextDouble();
            rest.Sort((p, q) => p.Weight != q.Weight ? p.Weight.CompareTo(q.Weight) : p.Index.CompareTo(q.Index));

            foreach (Cand k in rest)
            {
                if (k.Done) continue;
                if (NeedsLink(c.Rooms[k.A]) || NeedsLink(c.Rooms[k.B])) Realize(c, k, uf);
            }
            foreach (Cand k in rest)
            {
                bool solidHigh = SolidCount(c) > 0.29 * c.N;
                if (Loops(c) >= target && !solidHigh) break;
                if (k.Done) continue;
                if (Loops(c) >= target && !k.Tunnel) continue;
                Realize(c, k, uf);
            }

            foreach (Info i in c.Rooms)
                if (NeedsLink(i)) return "beco sem motivo (" + i.T + ")";
            return null;
        }

        static bool NeedsLink(Info i) => i.Links.Count <= 1 && !DeadEndOk(i.T);

        static int SolidCount(Ctx c)
        {
            int n = 0;
            for (int z = 0; z < c.H; z++)
                for (int x = 0; x < c.W; x++)
                    if (c.D.Room[x, z] < 0) n++;
            return n;
        }

        static bool Realize(Ctx c, Cand k, List<int> uf)
        {
            if (k.Done) return false;
            if (!k.Tunnel)
            {
                if (!RealizeDirect(c, k)) return false;
                k.Done = true;
                return true;
            }
            Info t = RealizeTunnel(c, k);
            if (t == null) return false;
            while (uf.Count <= t.Id) uf.Add(uf.Count);
            Union(uf, t.Id, k.A);
            Union(uf, t.Id, k.B);
            k.Done = true;
            return true;
        }

        struct DoorOpt
        {
            public int Run, Start, Len, Style;
            public PoolEdge Type;
        }

        static bool RealizeDirect(Ctx c, Cand k)
        {
            LevelData d = c.D;
            Random r = c.R;
            Info A = c.Rooms[k.A], B = c.Rooms[k.B];
            var edges = new List<(int x, int z, WallSide s)>();
            ForPerimeter(A.Room, (x, z, side) =>
            {
                if (!d.Neighbor(x, z, side, out int nx, out int nz) || d.Room[nx, nz] != B.Id) return;
                if (d.EdgeAt(x, z, side) != Unset) return;
                if (!Doorable(c, x, z, side) || !Doorable(c, nx, nz, LevelData.Opposite(side))) return;
                edges.Add((x, z, side));
            });
            if (edges.Count == 0) return false;

            // Trechos seguidos da parede comum.
            var runs = new List<List<(int x, int z, WallSide s)>>();
            foreach (var e in edges)
            {
                List<(int x, int z, WallSide s)> last = runs.Count > 0 ? runs[runs.Count - 1] : null;
                bool next = false;
                if (last != null)
                {
                    var p = last[last.Count - 1];
                    bool horizontal = e.s == WallSide.South || e.s == WallSide.North;
                    next = p.s == e.s && (horizontal ? (p.z == e.z && p.x + 1 == e.x) : (p.x == e.x && p.z + 1 == e.z));
                }
                if (next) last.Add(e);
                else runs.Add(new List<(int x, int z, WallSide s)> { e });
            }

            bool big = IsBig(A) && IsBig(B);
            bool halls = A.Room.Kind == PoolRoomKind.Hall && B.Room.Kind == PoolRoomKind.Hall;
            var opts = new List<DoorOpt>();
            for (int ri = 0; ri < runs.Count; ri++)
            {
                int m = runs[ri].Count;
                if (big)
                {
                    // 0: arcada de 2 a 4 arcos; 1: parede de portais (3 a 5); 2: um arco largo só.
                    for (int len = 2; len <= Math.Min(4, m); len++)
                        for (int s = 0; s + len <= m; s++)
                        {
                            opts.Add(new DoorOpt { Run = ri, Start = s, Len = len, Style = 0, Type = PoolEdge.Arch });
                            opts.Add(new DoorOpt { Run = ri, Start = s, Len = len, Style = 0, Type = PoolEdge.WideArch });
                        }
                    for (int len = 3; len <= Math.Min(5, m); len++)
                        for (int s = 0; s + len <= m; s++)
                            opts.Add(new DoorOpt { Run = ri, Start = s, Len = len, Style = 1, Type = PoolEdge.Doorway });
                    if (!halls)
                        for (int s = 0; s < m; s++)
                            opts.Add(new DoorOpt { Run = ri, Start = s, Len = 1, Style = 2, Type = PoolEdge.WideArch });
                }
                else
                {
                    // 0: arco; 1: arco largo; 2: vão reto; 3: parede de portais.
                    for (int s = 0; s < m; s++)
                    {
                        opts.Add(new DoorOpt { Run = ri, Start = s, Len = 1, Style = 0, Type = PoolEdge.Arch });
                        opts.Add(new DoorOpt { Run = ri, Start = s, Len = 1, Style = 1, Type = PoolEdge.WideArch });
                        opts.Add(new DoorOpt { Run = ri, Start = s, Len = 1, Style = 2, Type = PoolEdge.Doorway });
                    }
                    for (int len = 3; len <= Math.Min(5, m); len++)
                        for (int s = 0; s + len <= m; s++)
                            opts.Add(new DoorOpt { Run = ri, Start = s, Len = len, Style = 3, Type = PoolEdge.Doorway });
                }
            }

            double[] weights = big ? new[] { 0.6, 0.3, 0.1, 0 } : new[] { 0.45, 0.2, 0.25, 0.1 };
            while (opts.Count > 0)
            {
                // Estilo sorteado entre os que ainda têm opção, depois uma opção dele.
                var present = new double[4];
                foreach (DoorOpt o in opts) present[o.Style] = weights[o.Style] > 0 ? weights[o.Style] : 0.05;
                double total = present[0] + present[1] + present[2] + present[3];
                double pick = r.NextDouble() * total;
                int style = 0;
                for (int s = 0; s < 4; s++)
                {
                    if (present[s] <= 0) continue;
                    style = s;
                    if (pick < present[s]) break;
                    pick -= present[s];
                }
                var ofStyle = new List<int>();
                for (int k2 = 0; k2 < opts.Count; k2++) if (opts[k2].Style == style) ofStyle.Add(k2);
                int chosen = ofStyle[r.Next(ofStyle.Count)];
                DoorOpt opt = opts[chosen];
                opts.RemoveAt(chosen);
                if (ApplyDoors(c, runs[opt.Run], opt))
                {
                    AddLink(c, A.Id, B.Id);
                    return true;
                }
            }
            return false;
        }

        static bool ApplyDoors(Ctx c, List<(int x, int z, WallSide s)> run, DoorOpt opt)
        {
            LevelData d = c.D;
            int stairsBefore = d.Stairs.Count;
            bool need = false, got = false;
            for (int j = 0; j < opt.Len; j++)
            {
                var e = run[opt.Start + j];
                SetEdge(d, e.x, e.z, e.s, opt.Type);
                d.Neighbor(e.x, e.z, e.s, out int nx, out int nz);
                PoolDepth pa = d.Depth[e.x, e.z], pb = d.Depth[nx, nz];
                if (pa == pb) continue;
                need = true;
                bool aLower = d.FloorHeight(e.x, e.z) < d.FloorHeight(nx, nz);
                bool ok = aLower ? TryStair(d, e.x, e.z, e.s, Span(opt.Type)) : TryStair(d, nx, nz, LevelData.Opposite(e.s), Span(opt.Type));
                if (ok) got = true;
            }
            if (need && !got)
            {
                for (int j = 0; j < opt.Len; j++)
                {
                    var e = run[opt.Start + j];
                    SetEdge(d, e.x, e.z, e.s, Unset);
                }
                d.Stairs.RemoveRange(stairsBefore, d.Stairs.Count - stairsBefore);
                return false;
            }
            return true;
        }

        static Info RealizeTunnel(Ctx c, Cand k)
        {
            LevelData d = c.D;
            Random r = c.R;
            int len = k.Len;
            d.Neighbor(0, 0, k.Side, out int ddx, out int ddz);
            int dx = ddx, dz = ddz;   // direção do lado (Neighbor de (0,0) dá o passo, mesmo fora do mapa)
            var cells = new List<(int x, int z)>();
            for (int s = 1; s <= len; s++) cells.Add((k.X + dx * s, k.Z + dz * s));
            int bx = k.X + dx * (len + 1), bz = k.Z + dz * (len + 1);

            foreach ((int x, int z) in cells)
            {
                if (d.Room[x, z] >= 0 || c.Reserved[x, z]) return null;
                foreach (WallSide s in Sides)
                {
                    if (!d.Neighbor(x, z, s, out int nx, out int nz)) continue;
                    int id = d.Room[nx, nz];
                    if (id >= 0 && IsCorridorKind(c.Rooms[id].T)) return null;
                }
            }
            WallSide back = LevelData.Opposite(k.Side);
            if (d.EdgeAt(k.X, k.Z, k.Side) != Unset || d.EdgeAt(bx, bz, back) != Unset) return null;
            if (!Doorable(c, k.X, k.Z, k.Side) || !Doorable(c, bx, bz, back)) return null;

            if (CorridorCells(c) + len > MaxCorridor * c.N) return null;
            if (SolidCount(c) - len < MinSolid * c.N + 0.5) return null;
            if (NarrowCount(c) + 1 > MaxNarrowRooms * (c.Rooms.Count + 1)) return null;

            bool fenda = len >= 3 && c.Tall < MaxTallRooms && CountT(c, Tpl.Fenda) == 0 && r.NextDouble() < 0.4;
            if (len == 5 && !fenda) return null;

            PoolDepth da = d.Depth[k.X, k.Z], db = d.Depth[bx, bz];
            var options = new List<PoolDepth>();
            if (da == db) options.Add(da);
            else if (r.Next(2) == 0) { options.Add(PoolDepth.Shallow); options.Add(PoolDepth.Dry); }
            else { options.Add(PoolDepth.Dry); options.Add(PoolDepth.Shallow); }

            PoolEdge endA = r.NextDouble() < 0.7 ? PoolEdge.Arch : PoolEdge.Doorway;
            PoolEdge endB = r.NextDouble() < 0.7 ? PoolEdge.Arch : PoolEdge.Doorway;

            foreach (PoolDepth td in options)
            {
                float ft = LevelData.DepthFloor(td);
                // Escada no lado da sala (quando a sala é a mais baixa) precisa caber na célula dela.
                if (td != da && LevelData.DepthFloor(da) < ft && !StairFitsHypo(d, k.X, k.Z, k.Side, Span(endA), da, td)) continue;
                if (td != db && LevelData.DepthFloor(db) < ft && !StairFitsHypo(d, bx, bz, back, Span(endB), db, td)) continue;

                int x0 = Math.Min(cells[0].x, cells[len - 1].x), z0 = Math.Min(cells[0].z, cells[len - 1].z);
                bool alongX = k.Side == WallSide.East || k.Side == WallSide.West;
                float ceiling = fenda ? (r.NextDouble() < 0.5 ? TallCeiling : TallestCeiling) : VaultSpring;
                Info t = AddRoom(c, fenda ? Tpl.Fenda : Tpl.Tunnel, fenda ? PoolRoomKind.Slot : PoolRoomKind.Tunnel, new Frame(x0, z0, 1, len, alongX, false, false), ceiling);
                t.Room.Vault = !fenda;
                t.Room.VaultAlongX = alongX;
                foreach ((int x, int z) in cells) d.Depth[x, z] = td;
                if (fenda)
                    foreach ((int x, int z) in cells)
                    {
                        c.OldSky[x, z] = alongX ? PoolSky.SlotX : PoolSky.SlotZ;
                        c.KeepSky[x, z] = true;
                        c.PrimarySky[x, z] = true;
                    }

                SetEdge(d, k.X, k.Z, k.Side, endA);
                SetEdge(d, bx, bz, back, endB);
                (int fx, int fz) = cells[0];
                (int lx, int lz) = cells[len - 1];
                if (td != da)
                {
                    if (LevelData.DepthFloor(da) < ft) TryStair(d, k.X, k.Z, k.Side, Span(endA));
                    else TryStair(d, fx, fz, back, Span(endA));
                }
                if (td != db)
                {
                    if (LevelData.DepthFloor(db) < ft) TryStair(d, bx, bz, back, Span(endB));
                    else TryStair(d, lx, lz, k.Side, Span(endB));
                }
                AddLink(c, k.A, t.Id);
                AddLink(c, k.B, t.Id);
                return t;
            }
            return null;
        }

        /// <summary>A escada caberia na célula (x, z) se a vizinha tivesse a profundidade <paramref name="upper"/>?</summary>
        static bool StairFitsHypo(LevelData d, int x, int z, WallSide side, float width, PoolDepth lower, PoolDepth upper)
        {
            float foot = lower == PoolDepth.Well ? LevelData.WellStairFoot : LevelData.DepthFloor(lower);
            int steps = (int)Math.Round((LevelData.DepthFloor(upper) - foot) / LevelData.StepRise);
            if (steps < 1 || steps > 12) return false;
            return StairConflict(d, new PoolStair { X = x, Z = z, Side = side, Steps = steps, Width = width }, -1) == null;
        }

        // ================================================================ Paredes, saída e aberturas

        static void ResolveEdges(Ctx c)
        {
            LevelData d = c.D;
            for (int z = 0; z <= c.H; z++)
                for (int x = 0; x < c.W; x++)
                {
                    if (d.EdgeSouth[x, z] != Unset) continue;
                    bool same = z > 0 && z < c.H && d.Room[x, z] >= 0 && d.Room[x, z] == d.Room[x, z - 1];
                    d.EdgeSouth[x, z] = same ? PoolEdge.Open : PoolEdge.Wall;
                }
            for (int z = 0; z < c.H; z++)
                for (int x = 0; x <= c.W; x++)
                {
                    if (d.EdgeWest[x, z] != Unset) continue;
                    bool same = x > 0 && x < c.W && d.Room[x, z] >= 0 && d.Room[x, z] == d.Room[x - 1, z];
                    d.EdgeWest[x, z] = same ? PoolEdge.Open : PoolEdge.Wall;
                }
            UpdateWalls(d);
        }

        static void UpdateWalls(LevelData d)
        {
            for (int z = 0; z <= d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    d.WallSouth[x, z] = !Passable(d.EdgeSouth[x, z]);
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x <= d.Width; x++)
                    d.WallWest[x, z] = !Passable(d.EdgeWest[x, z]);
        }

        /// <summary>-1 = borda do mapa, -2 = maciço, senão o índice da sala vizinha.</summary>
        static int Outside(LevelData d, int x, int z, WallSide side)
        {
            if (!d.Neighbor(x, z, side, out int nx, out int nz)) return -1;
            return d.Solid[nx, nz] ? -2 : d.Room[nx, nz];
        }

        static bool ChooseExitDoor(Ctx c)
        {
            LevelData d = c.D;
            var border = new List<(int x, int z, WallSide s)>();
            var solid = new List<(int x, int z, WallSide s)>();
            ForPerimeter(c.Exit.Room, (x, z, side) =>
            {
                if (d.EdgeAt(x, z, side) != PoolEdge.Wall) return;
                int o = Outside(d, x, z, side);
                if (o == -1) border.Add((x, z, side));
                else if (o == -2) solid.Add((x, z, side));
            });
            List<(int x, int z, WallSide s)> pool = border.Count > 0 && (solid.Count == 0 || c.R.NextDouble() < 0.75) ? border : solid;
            if (pool.Count == 0) return false;
            var e = pool[c.R.Next(pool.Count)];
            d.ExitX = e.x;
            d.ExitZ = e.z;
            d.ExitSide = e.s;
            return true;
        }

        static bool IsExitEdge(LevelData d, int x, int z, WallSide side) => x == d.ExitX && z == d.ExitZ && side == d.ExitSide;

        /// <summary>Divisas de um lado da sala, em ordem.</summary>
        static List<(int x, int z)> SideCells(PoolRoom room, WallSide side)
        {
            var list = new List<(int, int)>();
            switch (side)
            {
                case WallSide.South: for (int x = room.X0; x < room.X0 + room.Width; x++) list.Add((x, room.Z0)); break;
                case WallSide.North: for (int x = room.X0; x < room.X0 + room.Width; x++) list.Add((x, room.Z0 + room.Height - 1)); break;
                case WallSide.West: for (int z = room.Z0; z < room.Z0 + room.Height; z++) list.Add((room.X0, z)); break;
                default: for (int z = room.Z0; z < room.Z0 + room.Height; z++) list.Add((room.X0 + room.Width - 1, z)); break;
            }
            return list;
        }

        /// <summary>Trechos seguidos de parede cega contra maciço ou borda num lado da sala.</summary>
        static List<List<(int x, int z)>> BlindRuns(LevelData d, PoolRoom room, WallSide side)
        {
            var runs = new List<List<(int x, int z)>>();
            List<(int x, int z)> cur = null;
            foreach ((int x, int z) in SideCells(room, side))
            {
                bool ok = d.EdgeAt(x, z, side) == PoolEdge.Wall && Outside(d, x, z, side) < 0 && !IsExitEdge(d, x, z, side);
                if (!ok) { cur = null; continue; }
                if (cur == null) { cur = new List<(int, int)>(); runs.Add(cur); }
                cur.Add((x, z));
            }
            return runs;
        }

        static int CountGlazed(LevelData d)
        {
            int n = 0;
            foreach (PoolEdge e in d.EdgeSouth) if (Glazed(e)) n++;
            foreach (PoolEdge e in d.EdgeWest) if (Glazed(e)) n++;
            return n;
        }

        /// <summary>Divisas de borda de sala (contadas uma vez) e quantas delas não são parede cega.</summary>
        static void BoundaryCounts(LevelData d, out int boundary, out int open)
        {
            boundary = 0;
            open = 0;
            for (int z = 0; z <= d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    int a = z < d.Height ? d.Room[x, z] : -1, b = z > 0 ? d.Room[x, z - 1] : -1;
                    if ((a < 0 && b < 0) || a == b) continue;
                    boundary++;
                    if (d.EdgeSouth[x, z] != PoolEdge.Wall) open++;
                }
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x <= d.Width; x++)
                {
                    int a = x < d.Width ? d.Room[x, z] : -1, b = x > 0 ? d.Room[x - 1, z] : -1;
                    if ((a < 0 && b < 0) || a == b) continue;
                    boundary++;
                    if (d.EdgeWest[x, z] != PoolEdge.Wall) open++;
                }
        }

        static bool CanDecorate(Info i) =>
            !i.Room.Dark && !IsCorridorKind(i.T) && i.T != Tpl.Atrium && i.T != Tpl.WellBig && i.T != Tpl.WellCorner && i.T != Tpl.Cistern;

        /// <summary>Põe a série de aberturas no trecho (todo, ou um pedaço de 2 a 6 se for comprido).</summary>
        static int PutSeries(Ctx c, List<(int x, int z)> run, WallSide side, PoolEdge type, bool whole, int budget)
        {
            if (budget <= 0 || run.Count == 0) return 0;
            int len = run.Count, start = 0;
            if (!whole && len > 2)
            {
                int want = Math.Min(len, 2 + c.R.Next(5));
                start = c.R.Next(len - want + 1);
                len = want;
            }
            len = Math.Min(len, budget);
            for (int k = 0; k < len; k++) SetEdge(c.D, run[start + k].x, run[start + k].z, side, type);
            return len;
        }

        static List<(int x, int z)> Longest(List<List<(int x, int z)>> runs)
        {
            List<(int x, int z)> best = null;
            foreach (var run in runs) if (best == null || run.Count > best.Count) best = run;
            return best;
        }

        static void Decorate(Ctx c)
        {
            LevelData d = c.D;
            Random r = c.R;

            // Corredor de janelas primeiro: frestas ou vigias em todas as células do lado Sul ou
            // Oeste (a tira de fora foi reservada como maciço). Não entra no teto de aberturas.
            foreach (Info i in c.Rooms)
            {
                if (i.T != Tpl.WindowHall) continue;
                WallSide side = WindowSide(i);
                foreach (var run in BlindRuns(d, i.Room, side)) PutSeries(c, run, side, i.WinType, true, MaxWindows);
            }

            foreach (Info i in c.Rooms)
            {
                if (!CanDecorate(i) || i.T == Tpl.WindowHall) continue;
                int budget = MaxWindows - CountGlazed(d);
                if (budget <= 0) break;
                // Parede cega é a regra (70 a 85%): nada de janela a mais se já há aberturas demais.
                BoundaryCounts(d, out int bnd, out int opn);
                if (bnd > 0 && opn >= MaxOpenForDecor * bnd) break;

                double chance;
                switch (i.T)
                {
                    case Tpl.Hall: chance = 0.7; break;
                    case Tpl.Canal: case Tpl.Lane: chance = 0.8; break;
                    case Tpl.Start: chance = 0.6; break;
                    case Tpl.Basin: case Tpl.Terrace: chance = 0.4; break;
                    case Tpl.Gallery: chance = 0.3; break;
                    case Tpl.Exit: chance = 0.25; break;
                    default: chance = 0; break;
                }
                if (r.NextDouble() >= chance) continue;

                if (i.T == Tpl.Gallery || i.T == Tpl.Exit)
                {
                    // Tijolo de vidro: qualquer parede contra maciço.
                    var all = new List<(List<(int x, int z)> run, WallSide side)>();
                    foreach (WallSide s in Sides)
                        foreach (var run in BlindRuns(d, i.Room, s)) all.Add((run, s));
                    if (all.Count == 0) continue;
                    var pick = all[r.Next(all.Count)];
                    PutSeries(c, pick.run, pick.side, PoolEdge.GlassBlock, false, budget);
                    continue;
                }

                // Janelas só nas paredes Sul e Oeste da sala (o sol está a sudoeste).
                WallSide wside;
                if (i.T == Tpl.Canal || i.T == Tpl.Lane) wside = i.F.Swap ? WallSide.South : WallSide.West;
                else wside = r.Next(2) == 0 ? WallSide.South : WallSide.West;
                var best = Longest(BlindRuns(d, i.Room, wside));
                if (best == null)
                {
                    WallSide other = wside == WallSide.South ? WallSide.West : WallSide.South;
                    if (i.T != Tpl.Canal && i.T != Tpl.Lane) best = Longest(BlindRuns(d, i.Room, other));
                    if (best != null) wside = other;
                }
                if (best == null)
                {
                    // Canal sem parede ao sol: tijolo de vidro no outro lado comprido.
                    if ((i.T == Tpl.Canal || i.T == Tpl.Lane) && r.NextDouble() < 0.5)
                    {
                        WallSide gside = i.F.Swap ? WallSide.North : WallSide.East;
                        var g = Longest(BlindRuns(d, i.Room, gside));
                        if (g != null) PutSeries(c, g, gside, PoolEdge.GlassBlock, false, budget);
                    }
                    continue;
                }
                PoolEdge type;
                double t = r.NextDouble();
                if (i.T == Tpl.Hall) type = t < 0.5 ? PoolEdge.Window : t < 0.75 ? PoolEdge.RoundWindow : PoolEdge.Slits;
                else if (i.T == Tpl.Canal || i.T == Tpl.Lane) type = t < 0.4 ? PoolEdge.Slits : t < 0.8 ? PoolEdge.Window : PoolEdge.RoundWindow;
                else if (i.T == Tpl.Terrace) type = t < 0.5 ? PoolEdge.Window : PoolEdge.Slits;
                else type = PoolEdge.Window;
                PutSeries(c, best, wside, type, best.Count <= 6, budget);
            }

            // Aberturas redondas em série entre salas vizinhas.
            int portholes = 1 + r.Next(3);
            for (int k = 0; k < portholes; k++)
            {
                BoundaryCounts(d, out int bnd, out int opn);
                if (bnd > 0 && opn >= MaxOpenForDecor * bnd) break;
                AddPortholes(c);
            }

            // Proporção de parede cega: 70 a 85% das divisas de borda de sala.
            double want = 0.17 + r.NextDouble() * 0.05;
            for (int guard = 0; guard < 40; guard++)
            {
                BoundaryCounts(d, out int boundary, out int open);
                if (boundary == 0 || open >= want * boundary) break;
                bool added = r.NextDouble() < 0.5 ? AddPortholes(c) || AddGlassBlock(c) : AddGlassBlock(c) || AddPortholes(c);
                if (!added) break;
            }
            UpdateWalls(d);
        }

        /// <summary>Lado comprido das janelas do corredor de janelas: local Oeste (FU fixo) = Sul deitado, Oeste em pé.</summary>
        static WallSide WindowSide(Info i) => i.F.Swap ? WallSide.South : WallSide.West;

        /// <summary>Todo corredor de janelas tem a série inteira no lado do sol.</summary>
        static bool WindowHallsComplete(Ctx c)
        {
            foreach (Info i in c.Rooms)
            {
                if (i.T != Tpl.WindowHall) continue;
                WallSide side = WindowSide(i);
                foreach ((int x, int z) in SideCells(i.Room, side))
                    if (c.D.EdgeAt(x, z, side) != i.WinType) return false;
            }
            return true;
        }

        static bool AddGlassBlock(Ctx c)
        {
            LevelData d = c.D;
            int budget = MaxWindows - CountGlazed(d);
            if (budget <= 0) return false;
            var all = new List<(List<(int x, int z)> run, WallSide side)>();
            foreach (Info i in c.Rooms)
            {
                if (!CanDecorate(i)) continue;
                foreach (WallSide s in Sides)
                    foreach (var run in BlindRuns(d, i.Room, s)) all.Add((run, s));
            }
            if (all.Count == 0) return false;
            var pick = all[c.R.Next(all.Count)];
            return PutSeries(c, pick.run, pick.side, PoolEdge.GlassBlock, false, budget) > 0;
        }

        static bool AddPortholes(Ctx c)
        {
            LevelData d = c.D;
            var all = new List<(List<(int x, int z)> run, WallSide side)>();
            foreach (Info i in c.Rooms)
            {
                if (!CanDecorate(i)) continue;
                foreach (WallSide s in Sides)
                {
                    List<(int x, int z)> cur = null;
                    int curRoom = -1;
                    foreach ((int x, int z) in SideCells(i.Room, s))
                    {
                        int o = Outside(d, x, z, s);
                        bool ok = o > i.Id && d.EdgeAt(x, z, s) == PoolEdge.Wall && CanDecorate(c.Rooms[o]);
                        if (!ok || o != curRoom) cur = null;
                        if (!ok) continue;
                        if (cur == null) { cur = new List<(int, int)>(); all.Add((cur, s)); curRoom = o; }
                        cur.Add((x, z));
                    }
                }
            }
            if (all.Count == 0) return false;
            var pick = all[c.R.Next(all.Count)];
            int len = Math.Min(pick.run.Count, 1 + c.R.Next(3));
            int start = c.R.Next(pick.run.Count - len + 1);
            for (int k = 0; k < len; k++) SetEdge(d, pick.run[start + k].x, pick.run[start + k].z, pick.side, PoolEdge.Porthole);
            return true;
        }

        // ================================================================ Colunas, claraboias, lâmpadas

        static void PlaceColumns(Ctx c)
        {
            LevelData d = c.D;
            foreach (Info i in c.Rooms)
                foreach (PoolColumn col in i.Want)
                {
                    if (ColumnProblem(d, col) != null) continue;
                    bool dup = false;
                    foreach (PoolColumn o in d.Columns)
                        if (Math.Abs(o.X - col.X) < Eps && Math.Abs(o.Z - col.Z) < Eps) dup = true;
                    if (!dup) d.Columns.Add(col);
                }
        }

        /// <summary>
        /// Sorteio ANTIGO das claraboias: espalhava claraboias soltas pelas salas e lia como furos
        /// ao acaso. Continua sendo tirado do mesmo System.Random, na mesma ordem (aqui, no Fill e
        /// no Finish), só para cada seed manter a mesma planta de antes: o resultado fica em OldSky
        /// e não vai para o mapa. As claraboias de verdade saem por padrão de sala, no fim
        /// (<see cref="ArrangeSkies"/>), com um Random só delas.
        /// </summary>
        static void PlaceSkies(Ctx c)
        {
            PoolSky[,] sky = c.OldSky;
            Random r = c.R;
            int count = 0;
            foreach (PoolSky s in sky) if (s != PoolSky.None) count++;

            int target = 34 + r.Next(26);
            if (count < target)
            {
                var cells = new List<(int x, int z, PoolSky s)>();
                foreach (Info i in c.Rooms)
                {
                    if (i.SkyStyle == PoolSky.None || i.Room.Dark || i.Room.Vault) continue;
                    for (int z = i.Room.Z0; z < i.Room.Z0 + i.Room.Height; z++)
                        for (int x = i.Room.X0; x < i.Room.X0 + i.Room.Width; x++)
                            if (sky[x, z] == PoolSky.None) cells.Add((x, z, i.SkyStyle));
                }
                Shuffle(cells, r);
                for (int k = 0; k < cells.Count && count < target; k++)
                {
                    sky[cells[k].x, cells[k].z] = cells[k].s;
                    count++;
                }
            }
            // Demais: tira primeiro as de enchimento, depois as dos modelos; nunca as dos marcos.
            for (int pass = 0; pass < 2 && count > MaxSky - 4; pass++)
            {
                var removable = new List<(int x, int z)>();
                for (int z = 0; z < c.H; z++)
                    for (int x = 0; x < c.W; x++)
                        if (sky[x, z] != PoolSky.None && !c.PrimarySky[x, z] && (pass == 1 || !c.KeepSky[x, z])) removable.Add((x, z));
                Shuffle(removable, r);
                for (int k = 0; k < removable.Count && count > MaxSky - 8; k++)
                {
                    sky[removable[k].x, removable[k].z] = PoolSky.None;
                    c.KeepSky[removable[k].x, removable[k].z] = false;
                    count--;
                }
            }
        }

        // ================================================================ Claraboias por padrão de sala
        //
        // As claraboias seguem a estrutura da sala, como num projeto de arquiteto: um formato só por
        // sala, simétricas, em ritmo regular, no centro dos vãos entre colunas (as colunas ficam nos
        // vértices, então o vão é a célula). A seed escolhe QUAL padrão, nunca posições soltas. Cada
        // sala tem famílias de padrões (todas as baias, xadrez, fileiras alternadas, nave central,
        // anel de borda, faixa sobre a raia...), cada família do mais cheio ao mais ralo; o orçamento
        // do mapa (30 a 70 células) se acerta andando dentro da família, nunca tirando claraboia
        // avulsa. Usa um System.Random só dele (da seed): o principal não é tocado, e a planta
        // (salas, água, escadas, colunas, lâmpadas) fica a mesma de antes.

        /// <summary>Um padrão: um formato só e as células, no sistema local da sala (u, v).</summary>
        sealed class SkyPattern
        {
            public PoolSky Shape;
            public readonly List<(int u, int v)> Cells = new List<(int u, int v)>();

            /// <summary>
            /// Luzes com sombra que o construtor cria: uma por claraboia recortada; o teto de vidro
            /// (sempre um retângulo) leva uma por faixa no sentido mais comprido dele.
            /// </summary>
            public int Lights
            {
                get
                {
                    if (Shape != PoolSky.Full || Cells.Count == 0) return Cells.Count;
                    int u0 = int.MaxValue, u1 = int.MinValue, v0 = int.MaxValue, v1 = int.MinValue;
                    foreach ((int u, int v) in Cells)
                    {
                        u0 = Math.Min(u0, u); u1 = Math.Max(u1, u);
                        v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
                    }
                    return Math.Min(u1 - u0 + 1, v1 - v0 + 1);
                }
            }
        }

        /// <summary>Família de padrões da mesma ideia, do mais cheio ao mais ralo.</summary>
        sealed class SkyFamily
        {
            public double Weight;
            public readonly List<SkyPattern> Steps = new List<SkyPattern>();
        }

        sealed class SkyChoice
        {
            public Info Room;
            public List<SkyFamily> Families;
            public int Family, Step;
            public SkyPattern Current => Families[Family].Steps[Step];
            public int Count => Current.Cells.Count;
            public int Lights => Current.Lights;
        }

        /// <summary>
        /// O próximo padrão mais ralo: o degrau seguinte da mesma família; ou, com otherFamily, o
        /// padrão de outra família com mais células entre os que têm menos que o atual.
        /// </summary>
        static bool SparserOption(SkyChoice ch, bool otherFamily, out int family, out int step)
        {
            family = step = -1;
            if (!otherFamily)
            {
                if (ch.Step >= ch.Families[ch.Family].Steps.Count - 1) return false;
                family = ch.Family;
                step = ch.Step + 1;
                return true;
            }
            int best = -1;
            for (int f = 0; f < ch.Families.Count; f++)
            {
                if (f == ch.Family) continue;
                for (int s = 0; s < ch.Families[f].Steps.Count; s++)
                {
                    int n = ch.Families[f].Steps[s].Cells.Count;
                    if (n < ch.Count && n > best) { best = n; family = f; step = s; }
                }
            }
            return best >= 0;
        }

        /// <summary>
        /// Posições de uma fileira de n células, simétricas e em ritmo: de a até n-1-a, de s em s.
        /// Null se o ritmo não fecha nas duas pontas ((n-1-2a) precisa ser múltiplo de s).
        /// </summary>
        static List<int> Rhythm(int n, int a, int s)
        {
            int span = n - 1 - 2 * a;
            if (span < 0 || s < 1 || span % s != 0) return null;
            var list = new List<int>();
            for (int p = a; p <= n - 1 - a; p += s) list.Add(p);
            return list;
        }

        /// <summary>A fileira encostada na parede fica sem claraboia quando há 4 ou mais células naquele sentido.</summary>
        static int Margin(int n) => n >= 4 ? 1 : 0;

        /// <summary>O meio de uma fileira: a célula do meio, ou o par do meio.</summary>
        static List<int> Middle(int n) => n % 2 == 1 ? new List<int> { n / 2 } : new List<int> { n / 2 - 1, n / 2 };

        /// <summary>
        /// Ritmos de uma fileira de n células, do mais cheio ao mais ralo: toda célula (depois da
        /// margem), uma sim uma não, a cada 3 (cada um com a menor margem que fecha simétrico) e o
        /// meio. Com minCount, só os que têm pelo menos tantas posições (toda célula fica sempre).
        /// </summary>
        static List<List<int>> Rhythms(int n, int margin, int minCount = 1, bool middle = true)
        {
            var result = new List<List<int>>();
            for (int s = 1; s <= 3; s++)
                for (int a = margin; 2 * a <= n - 1; a++)
                {
                    List<int> r = Rhythm(n, a, s);
                    if (r == null) continue;
                    if (s == 1 || r.Count >= minCount) AddRhythm(result, r);
                    break;
                }
            if (middle && (Middle(n).Count >= minCount || result.Count == 0)) AddRhythm(result, Middle(n));
            result.Sort((x, y) => y.Count.CompareTo(x.Count));
            return result;
        }

        static void AddRhythm(List<List<int>> list, List<int> r)
        {
            foreach (List<int> o in list)
            {
                if (o.Count != r.Count) continue;
                bool same = true;
                for (int k = 0; k < o.Count; k++) if (o[k] != r[k]) same = false;
                if (same) return;
            }
            list.Add(r);
        }

        static List<int> Range(int from, int to)
        {
            var list = new List<int>();
            for (int p = from; p <= to; p++) list.Add(p);
            return list;
        }

        static List<int> Shift(List<int> list, int by)
        {
            var result = new List<int>();
            foreach (int p in list) result.Add(p + by);
            return result;
        }

        /// <summary>Grade: o produto das fileiras U (em u) e V (em v).</summary>
        static SkyPattern Grid(PoolSky shape, List<int> us, List<int> vs)
        {
            var p = new SkyPattern { Shape = shape };
            foreach (int v in vs)
                foreach (int u in us)
                    p.Cells.Add((u, v));
            return p;
        }

        /// <summary>Xadrez da grade U x V (cantos incluídos). Só é simétrico com as duas contagens ímpares.</summary>
        static SkyPattern Checker(PoolSky shape, List<int> us, List<int> vs)
        {
            var p = new SkyPattern { Shape = shape };
            for (int iv = 0; iv < vs.Count; iv++)
                for (int iu = 0; iu < us.Count; iu++)
                    if ((iu + iv) % 2 == 0) p.Cells.Add((us[iu], vs[iv]));
            return p;
        }

        /// <summary>Anel: só a volta de fora da grade U x V.</summary>
        static SkyPattern Ring(PoolSky shape, List<int> us, List<int> vs)
        {
            var p = new SkyPattern { Shape = shape };
            for (int iv = 0; iv < vs.Count; iv++)
                for (int iu = 0; iu < us.Count; iu++)
                    if (iu == 0 || iv == 0 || iu == us.Count - 1 || iv == vs.Count - 1) p.Cells.Add((us[iu], vs[iv]));
            return p;
        }

        static SkyFamily Family(double weight, params SkyPattern[] steps)
        {
            var f = new SkyFamily { Weight = weight };
            foreach (SkyPattern p in steps) AddStep(f, p);
            return f;
        }

        /// <summary>Acrescenta um degrau à família (sem repetir o mesmo conjunto) e mantém do mais cheio ao mais ralo.</summary>
        static void AddStep(SkyFamily f, SkyPattern p)
        {
            if (p == null) return;
            foreach (SkyPattern o in f.Steps)
            {
                if (o.Shape != p.Shape || o.Cells.Count != p.Cells.Count) continue;
                var set = new HashSet<(int, int)>(o.Cells);
                bool same = true;
                foreach (var cell in p.Cells) if (!set.Contains(cell)) same = false;
                if (same) return;
            }
            f.Steps.Add(p);
            // Ordenação estável: do mais cheio ao mais ralo, na ordem em que entraram.
            var sorted = new List<SkyPattern>(f.Steps);
            f.Steps.Clear();
            for (int k = 0; k < sorted.Count; k++)
            {
                int best = -1;
                for (int j = 0; j < sorted.Count; j++)
                    if (sorted[j] != null && (best < 0 || sorted[j].Cells.Count > sorted[best].Cells.Count)) best = j;
                f.Steps.Add(sorted[best]);
                sorted[best] = null;
            }
        }

        /// <summary>Rasgo atravessado na sala (no sentido da largura, u).</summary>
        static PoolSky SlotAcross(Info i) => i.F.Swap ? PoolSky.SlotZ : PoolSky.SlotX;

        static PoolSky RoundOrSquare(Random r) => r.Next(2) == 0 ? PoolSky.Round : PoolSky.Square;

        /// <summary>Os padrões que cabem na sala, por família. Lista vazia: sala sem claraboia.</summary>
        static List<SkyFamily> SkyOptions(Ctx c, Info i, Random r)
        {
            var families = new List<SkyFamily>();
            if (i.Room.Dark || i.Room.Vault) return families;
            int lw = i.F.LW, lh = i.F.LH;
            switch (i.T)
            {
                case Tpl.Start:
                case Tpl.Atrium:
                {
                    // Um óculo (ou uma quadrada) no meio: sobre a ilha do átrio, sobre o patamar.
                    PoolSky shape = i.T == Tpl.Atrium || r.Next(2) == 0 ? PoolSky.Round : PoolSky.Square;
                    families.Add(Family(1, Grid(shape, Middle(lw), Middle(lh))));
                    break;
                }

                case Tpl.Exit:
                {
                    // Uma no meio, ou o par (ou os quatro) do meio; ou um teto de vidro nesse meio.
                    families.Add(Family(0.35, Grid(PoolSky.Square, Middle(lw), Middle(lh))));
                    families.Add(Family(0.35, Grid(PoolSky.Round, Middle(lw), Middle(lh))));
                    families.Add(Family(0.3, Grid(PoolSky.Full, Middle(lw), Middle(lh))));
                    break;
                }

                case Tpl.Hall:
                    HallOptions(c, i, r, families);
                    break;

                case Tpl.Basin:
                {
                    // Sobre a água (o deque em volta fica com teto cheio).
                    List<int> wu = Range(1, lw - 2), wv = Range(1, lh - 2);
                    families.Add(Family(0.3, Grid(PoolSky.Full, wu, wv), Grid(PoolSky.Full, Middle(lw), wv)));
                    families.Add(Family(0.15, Grid(PoolSky.Full, wu, Middle(lh))));
                    PoolSky shape = RoundOrSquare(r);
                    var grid = new SkyFamily { Weight = 0.55 };
                    List<List<int>> us = Rhythms(lw - 2, 0), vs = Rhythms(lh - 2, 0);
                    if (wu.Count % 2 == 1 && wv.Count % 2 == 1 && wu.Count >= 3 && wv.Count >= 3) AddStep(grid, Checker(shape, wu, wv));
                    foreach (List<int> u in us)
                        foreach (List<int> v in vs)
                            AddStep(grid, Grid(shape, Shift(u, 1), Shift(v, 1)));
                    families.Add(grid);
                    break;
                }

                case Tpl.Lane:
                {
                    // Exatamente sobre a raia funda (u = 1, v = 1..lh-2), de ponta a ponta da raia:
                    // uma faixa contínua de vidro, ou rasgos em ritmo.
                    List<int> run = Range(1, lh - 2);
                    families.Add(Family(0.55, Grid(PoolSky.Full, Middle(lw), run)));
                    var slots = new SkyFamily { Weight = 0.45 };
                    foreach (List<int> v in Rhythms(run.Count, 0, 3, false)) AddStep(slots, Grid(SlotAlong(i), Middle(lw), Shift(v, 1)));
                    families.Add(slots);
                    break;
                }

                case Tpl.Canal:
                {
                    // Sobre o canal, no meio da largura: faixa de vidro ou fileira de rasgos.
                    List<int> v0 = Range(Margin(lh), lh - 1 - Margin(lh));
                    families.Add(Family(0.35, Grid(PoolSky.Full, Middle(lw), v0)));
                    var slots = new SkyFamily { Weight = 0.65 };
                    foreach (List<int> v in Rhythms(lh, Margin(lh), 3, false)) AddStep(slots, Grid(SlotAlong(i), Middle(lw), v));
                    families.Add(slots);
                    break;
                }

                case Tpl.Gallery:
                case Tpl.WindowHall:
                {
                    // Fileira de rasgos centrada, em ritmo. Com 2 células de largura o meio é a fila
                    // de pilares: um rasgo de cada lado dela, em par.
                    List<int> us = lw <= 2 ? Range(0, lw - 1) : Middle(lw);
                    var along = new SkyFamily { Weight = i.T == Tpl.Gallery ? 0.6 : 1 };
                    foreach (List<int> v in Rhythms(lh, Margin(lh), 3, false)) AddStep(along, Grid(SlotAlong(i), us, v));
                    if (i.T == Tpl.WindowHall)
                    {
                        // A luz do corredor de janelas é das janelas: claraboia só se o orçamento pedir.
                        along.Steps.Add(new SkyPattern { Shape = SlotAlong(i) });
                        families.Add(along);
                        break;
                    }
                    families.Add(along);
                    if (lw == 2)
                    {
                        var across = new SkyFamily { Weight = 0.4 };
                        foreach (List<int> v in Rhythms(lh, Margin(lh), 3, false)) AddStep(across, Grid(SlotAcross(i), us, v));
                        families.Add(across);
                    }
                    break;
                }

                case Tpl.Terrace:
                {
                    // Sobre o meio da escada, de parede a parede (ou fora a fileira da borda).
                    List<int> mid = Middle(lh);
                    families.Add(Family(0.5, Grid(PoolSky.Full, Range(Margin(lw), lw - 1 - Margin(lw)), mid)));
                    PoolSky shape = RoundOrSquare(r);
                    var row = new SkyFamily { Weight = 0.5 };
                    foreach (List<int> u in Rhythms(lw, Margin(lw))) AddStep(row, Grid(shape, u, mid));
                    families.Add(row);
                    break;
                }

                case Tpl.WellBig:
                {
                    // Um óculo sobre o poço (no meio); com o poço de 4 células, quatro óculos ou um
                    // teto de vidro sobre ele.
                    List<int> m = Middle(lw);
                    if (m.Count == 1) families.Add(Family(1, Grid(PoolSky.Round, m, m)));
                    else
                    {
                        families.Add(Family(0.5, Grid(PoolSky.Round, m, m)));
                        families.Add(Family(0.5, Grid(PoolSky.Full, m, m)));
                    }
                    break;
                }

                case Tpl.WellCorner:
                    // Um óculo sobre o poço (u = 2, no meio do comprimento).
                    families.Add(Family(1, Grid(PoolSky.Round, new List<int> { 2 }, Middle(lh))));
                    break;

                case Tpl.Fenda:
                    // O rasgo de luz no topo, de ponta a ponta.
                    families.Add(Family(1, Grid(SlotAlong(i), Range(0, lw - 1), Range(0, lh - 1))));
                    break;
            }
            families.RemoveAll(f => f.Steps.Count == 0);
            // Fora os marcos (patamar, átrio, raia, fenda, salas de poço), a sala pode ficar sem
            // claraboia quando o mapa estoura o orçamento mesmo nos padrões mais ralos.
            bool landmark = i.T == Tpl.Start || i.T == Tpl.Atrium || i.T == Tpl.Lane || i.T == Tpl.Fenda || i.T == Tpl.WellBig || i.T == Tpl.WellCorner;
            if (families.Count > 0 && !landmark) families.Add(Family(0, new SkyPattern()));
            return families;
        }

        static void HallOptions(Ctx c, Info i, Random r, List<SkyFamily> families)
        {
            int lw = i.F.LW, lh = i.F.LH;
            PoolSky shape = RoundOrSquare(r);
            List<int> iu = Range(Margin(lw), lw - 1 - Margin(lw)), iv = Range(Margin(lh), lh - 1 - Margin(lh));
            List<List<int>> us = Rhythms(lw, Margin(lw), 2, false), vs = Rhythms(lh, Margin(lh), 2, false);

            // Todas as baias (do miolo) e, mais ralo, as grades de passo maior.
            var bays = new SkyFamily { Weight = 0.3 };
            foreach (List<int> u in us)
                foreach (List<int> v in vs)
                    AddStep(bays, Grid(shape, u, v));
            families.Add(bays);

            // Xadrez: só com as duas contagens ímpares (senão não é simétrico).
            if (iu.Count % 2 == 1 && iv.Count % 2 == 1 && iu.Count >= 3 && iv.Count >= 3)
            {
                var checker = new SkyFamily { Weight = 0.15 };
                AddStep(checker, Checker(shape, iu, iv));
                foreach (List<int> u in us)
                    foreach (List<int> v in vs)
                        if (u.Count < iu.Count && v.Count < iv.Count) AddStep(checker, Grid(shape, u, v));
                families.Add(checker);
            }

            // Fileiras alternadas: atravessadas no comprimento, uma sim uma não (ou a cada 3).
            var rows = new SkyFamily { Weight = 0.2 };
            foreach (List<int> v in vs)
                if (v.Count < iv.Count)
                    foreach (List<int> u in us)
                        AddStep(rows, Grid(shape, u, v));
            if (rows.Steps.Count > 0) families.Add(rows);

            // Só a nave central: a fileira do meio, de ponta a ponta do miolo.
            var nave = new SkyFamily { Weight = 0.15 };
            foreach (List<int> v in vs) AddStep(nave, Grid(shape, Middle(lw), v));
            families.Add(nave);
            families.Add(Family(0.1, Grid(PoolSky.Full, Middle(lw), iv)));

            // Só o anel de borda: a volta das baias encostadas na parede (toda baia ou uma sim uma
            // não). Só os quatro cantos não conta: lia como sobra, não como anel.
            var ring = new SkyFamily { Weight = 0.08 };
            foreach (List<int> u in Rhythms(lw, 0, 3, false))
                foreach (List<int> v in Rhythms(lh, 0, 3, false))
                    if (u.Count >= 3 && v.Count >= 3) AddStep(ring, Ring(shape, u, v));
            if (ring.Steps.Count > 0) families.Add(ring);

            // Raia funda cortando o salão: faixa (ou rasgos em ritmo) exatamente sobre ela.
            int lane = -1, vs0 = int.MaxValue, vs1 = -1;
            for (int u = 0; u < lw; u++)
                for (int v = 0; v < lh; v++)
                {
                    i.F.Cell(u, v, out int x, out int z);
                    if (c.D.Depth[x, z] != PoolDepth.Deep) continue;
                    lane = u;
                    vs0 = Math.Min(vs0, v);
                    vs1 = Math.Max(vs1, v);
                }
            if (lane >= 0)
            {
                // A raia que vai de parede a parede perde as pontas: a fileira da borda fica livre.
                vs0 = Math.Max(vs0, Margin(lh));
                vs1 = Math.Min(vs1, lh - 1 - Margin(lh));
                var over = new SkyFamily { Weight = 0.3 };
                foreach (List<int> v in Rhythms(vs1 - vs0 + 1, 0, 3, false)) AddStep(over, Grid(SlotAlong(i), new List<int> { lane }, Shift(v, vs0)));
                families.Add(over);
                families.Add(Family(0.15, Grid(PoolSky.Full, new List<int> { lane }, Range(vs0, vs1))));
            }
        }

        /// <summary>
        /// Claraboias do mapa: um padrão por sala (a seed escolhe a família e o degrau), depois o
        /// orçamento de 30 a 70 células, andando dentro da família. Acende a luz de cada célula
        /// com claraboia.
        /// </summary>
        static void ArrangeSkies(Ctx c)
        {
            LevelData d = c.D;
            var r = new Random(unchecked(d.Seed * 1103515245 + 0x5C1A));
            var choices = new List<SkyChoice>();
            foreach (Info i in c.Rooms)
            {
                List<SkyFamily> families = SkyOptions(c, i, r);
                if (families.Count == 0) continue;
                // Família pelo peso (a de peso zero, "sem claraboia", só entra pelo orçamento).
                double total = 0;
                int fi = -1;
                foreach (SkyFamily f in families) total += f.Weight;
                double pick = r.NextDouble() * total;
                for (int k = 0; k < families.Count; k++)
                {
                    if (families[k].Weight <= 0) continue;
                    fi = k;
                    pick -= families[k].Weight;
                    if (pick < 0) break;
                }
                int si = r.Next(families[fi].Steps.Count);
                choices.Add(new SkyChoice { Room = i, Families = families, Family = fi, Step = si });
            }

            int count = 0, lights = 0;
            foreach (SkyChoice ch in choices) { count += ch.Count; lights += ch.Lights; }

            // Demais (células acima de 70 ou luzes acima do teto): a sala que mais pesa passa para o
            // degrau mais ralo da MESMA família; só quando nenhuma sala pode mais, troca de família
            // (para o padrão mais próximo, mais ralo) ou, nas salas que não são marco, fica sem
            // claraboia. Nunca abaixo de 30 células por causa das luzes.
            bool Sparsen(bool otherFamily)
            {
                bool byCells = count > MaxSky;
                SkyChoice best = null;
                int bf = -1, bs = -1;
                foreach (SkyChoice ch in choices)
                {
                    if (!SparserOption(ch, otherFamily, out int f, out int s)) continue;
                    SkyPattern next = ch.Families[f].Steps[s];
                    if (!byCells && count - ch.Count + next.Cells.Count < MinSky) continue;
                    int weight = byCells ? ch.Count : ch.Lights;
                    int bestWeight = best == null ? -1 : byCells ? best.Count : best.Lights;
                    if (weight > bestWeight) { best = ch; bf = f; bs = s; }
                }
                if (best == null) return false;
                count -= best.Count; lights -= best.Lights;
                best.Family = bf;
                best.Step = bs;
                count += best.Count; lights += best.Lights;
                return true;
            }
            while (count > MaxSky || lights > MaxSkyLights)
                if (!Sparsen(false) && !Sparsen(true)) break;
            // De menos: a sala com menos claraboias passa para o degrau mais cheio da mesma família;
            // se nenhuma pode, a que mais ganha troca para o padrão mais cheio que tem.
            while (count < MinSky)
            {
                SkyChoice best = null;
                foreach (SkyChoice ch in choices)
                    if (ch.Step > 0 && (best == null || ch.Count < best.Count)) best = ch;
                int bf = best != null ? best.Family : -1, bs = best != null ? best.Step - 1 : -1;
                if (best == null)
                {
                    int gain = 0;
                    foreach (SkyChoice ch in choices)
                        for (int f = 0; f < ch.Families.Count; f++)
                        {
                            int g = ch.Families[f].Steps[0].Cells.Count - ch.Count;
                            if (g > gain) { gain = g; best = ch; bf = f; bs = 0; }
                        }
                    if (best == null) break;
                }
                count -= best.Count; lights -= best.Lights;
                best.Family = bf;
                best.Step = bs;
                count += best.Count; lights += best.Lights;
            }

            foreach (SkyChoice ch in choices)
                foreach ((int u, int v) in ch.Current.Cells)
                {
                    ch.Room.F.Cell(u, v, out int x, out int z);
                    d.Sky[x, z] = ch.Current.Shape;
                }
            for (int z = 0; z < c.H; z++)
                for (int x = 0; x < c.W; x++)
                    d.Lights[x, z] = d.Sky[x, z] != PoolSky.None ? LightState.On : LightState.None;
        }

        /// <summary>Altura máxima do centro de uma lâmpada naquele lado, ou NaN se o lado não serve.</summary>
        static float LampTop(LevelData d, int x, int z, WallSide side)
        {
            PoolEdge e = d.EdgeAt(x, z, side);
            if (!Passable(e)) return -0.4f;
            if (e != PoolEdge.Open || !d.Neighbor(x, z, side, out int nx, out int nz)) return float.NaN;
            if (d.Solid[nx, nz] || d.Depth[nx, nz] == PoolDepth.Well) return float.NaN;
            if (d.StairAt(x, z, side, out _)) return float.NaN;
            float top = d.FloorHeight(nx, nz) - 0.3f;
            return Math.Min(-0.4f, top);
        }

        static bool PlaceLamps(Ctx c)
        {
            LevelData d = c.D;
            Random r = c.R;
            var order = new List<Info>();
            foreach (Info i in c.Rooms) if (i.Room.Dark) order.Add(i);
            foreach (Info i in c.Rooms) if (!i.Room.Dark && i.LampWish > 0) order.Add(i);

            float[] deepYs = { -0.6f, -0.7f, -0.8f, -0.9f };
            float[] wellYs = { -2f, -2.5f, -3f, -3.5f, -4.5f };
            foreach (Info i in order)
            {
                int want = Math.Max(i.Room.Dark ? 1 : 0, i.LampWish);
                want = Math.Min(want, MaxLamps - d.Lamps.Count);
                if (want <= 0)
                {
                    if (i.Room.Dark) return false;
                    continue;
                }
                var wells = new List<PoolLamp>();
                var deeps = new List<PoolLamp>();
                for (int z = i.Room.Z0; z < i.Room.Z0 + i.Room.Height; z++)
                    for (int x = i.Room.X0; x < i.Room.X0 + i.Room.Width; x++)
                    {
                        PoolDepth p = d.Depth[x, z];
                        if (p != PoolDepth.Deep && p != PoolDepth.Well) continue;
                        float bottom = d.FloorHeight(x, z) + 0.3f;
                        foreach (WallSide s in Sides)
                        {
                            float top = LampTop(d, x, z, s);
                            if (float.IsNaN(top) || top < bottom) continue;
                            var ys = new List<float>();
                            foreach (float y in p == PoolDepth.Well ? wellYs : deepYs)
                                if (y >= bottom - Eps && y <= top + Eps) ys.Add(y);
                            if (ys.Count == 0) continue;
                            var lamp = new PoolLamp { X = x, Z = z, Side = s, Y = ys[r.Next(ys.Count)] };
                            (p == PoolDepth.Well ? wells : deeps).Add(lamp);
                        }
                    }
                Shuffle(wells, r);
                Shuffle(deeps, r);
                var pool = new List<PoolLamp>(wells);
                pool.AddRange(deeps);
                if (pool.Count == 0)
                {
                    if (i.Room.Dark) return false;
                    continue;
                }
                for (int k = 0; k < want && k < pool.Count; k++) d.Lamps.Add(pool[k]);
            }
            return true;
        }

        static void Finish(Ctx c)
        {
            LevelData d = c.D;
            UpdateWalls(d);

            // Sala inicial: só o retângulo SECO do patamar (fileiras v = 1 e 2).
            Info s = c.Start;
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            for (int v = 1; v <= 2; v++)
                for (int u = 0; u < 3; u++)
                {
                    s.F.Cell(u, v, out int x, out int z);
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
                }
            d.RoomX0 = minX;
            d.RoomZ0 = minZ;
            d.RoomWidth = maxX - minX + 1;
            d.RoomHeight = maxZ - minZ + 1;
            d.SpawnX = d.RoomX0 + d.RoomWidth / 2;
            d.SpawnZ = d.RoomZ0 + d.RoomHeight / 2;

            // O sorteio que escolhia as claraboias quebradas (sobre as do sorteio antigo) continua
            // sendo tirado, sem uso, para cada seed manter a mesma planta de antes. As luzes
            // acendem em ArrangeSkies.
            var unused = new List<(int x, int z)>();
            for (int z = 0; z < c.H; z++)
                for (int x = 0; x < c.W; x++)
                    if (c.OldSky[x, z] != PoolSky.None && !c.KeepSky[x, z] && LevelGenerator.WearAt(d, x, z) >= 0.75f) unused.Add((x, z));
            Shuffle(unused, c.R);
            c.R.Next(3);

            d.PathCells = FootPath(d);
            d.SafePathCells = new List<int>(d.PathCells);
        }

        // ================================================================ Andar

        /// <summary>
        /// Verdadeiro se toda célula com piso que não é poço é alcançável a partir da sala inicial,
        /// a pé ou nadando na água funda (<see cref="FootReach"/>). Como a passagem vale nos dois
        /// sentidos, toda área funda tem uma escada que sobe dela.
        /// </summary>
        public static bool AllReachableOnFoot(LevelData d)
        {
            bool[] seen = FootReach(d, d.SpawnX, d.SpawnZ);
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    if (!d.IsSolid(x, z) && d.Depth[x, z] != PoolDepth.Well && !seen[x + z * d.Width]) return false;
            return true;
        }

        /// <summary>
        /// Células alcançáveis a partir de (x, z) por <see cref="LevelData.CanStep"/>: mesma altura ou
        /// escada, sem poço. Entre células fundas passa-se nadando (ali não dá pé).
        /// </summary>
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
                    if (!d.CanStep(x, z, side)) continue;
                    d.Neighbor(x, z, side, out int nx, out int nz);
                    int n = nx + nz * d.Width;
                    if (seen[n]) continue;
                    seen[n] = true;
                    queue.Enqueue(n);
                }
            }

            return seen;
        }

        /// <summary>
        /// Caminho mais curto a pé, SEM NADAR (só seco e raso), da sala inicial até a saída, célula
        /// por célula (x + z * Width). Vazio se a saída só se alcança passando pela água funda.
        /// </summary>
        public static List<int> FootPath(LevelData d)
        {
            bool Wading(int x, int z) => d.Depth[x, z] == PoolDepth.Dry || d.Depth[x, z] == PoolDepth.Shallow;
            if (!Wading(d.SpawnX, d.SpawnZ) || !Wading(d.ExitX, d.ExitZ)) return new List<int>();
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
                    if (!d.CanStep(x, z, side)) continue;
                    d.Neighbor(x, z, side, out int nx, out int nz);
                    if (!Wading(nx, nz)) continue;
                    int n = nx + nz * d.Width;
                    if (previous[n] != -2) continue;
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

        // ================================================================ Conferência

        static float DistToCenterLine(float v)
        {
            float m = v % CellSize;
            if (m < 0) m += CellSize;
            return Math.Abs(m - CellSize * 0.5f);
        }

        static bool OnGrid(float v, out int index)
        {
            float f = v / CellSize;
            index = (int)Math.Round(f);
            return Math.Abs(f - index) * CellSize < Eps;
        }

        static bool FloorNotWell(LevelData d, int x, int z) => x >= 0 && z >= 0 && x < d.Width && z < d.Height && !d.Solid[x, z] && d.Depth[x, z] != PoolDepth.Well;

        /// <summary>Por que a coluna não pode ficar ali; null se pode.</summary>
        static string ColumnProblem(LevelData d, PoolColumn col)
        {
            if (!(col.Radius > 0f) || col.Radius > 0.9f + Eps) return "coluna com raio fora de 0 a 0,9 m";
            if (DistToCenterLine(col.X) < col.Radius + 0.6f - Eps || DistToCenterLine(col.Z) < col.Radius + 0.6f - Eps)
                return "coluna perto demais da linha de centro de uma célula";
            bool gx = OnGrid(col.X, out int ix), gz = OnGrid(col.Z, out int iz);
            if (gx && gz)
            {
                if (ix <= 0 || iz <= 0 || ix >= d.Width || iz >= d.Height) return "coluna em vértice da borda do mapa";
                if (!FloorNotWell(d, ix - 1, iz - 1) || !FloorNotWell(d, ix, iz - 1) || !FloorNotWell(d, ix - 1, iz) || !FloorNotWell(d, ix, iz))
                    return "coluna em vértice de maciço ou poço";
                if (d.EdgeWest[ix, iz - 1] != PoolEdge.Open || d.EdgeWest[ix, iz] != PoolEdge.Open || d.EdgeSouth[ix - 1, iz] != PoolEdge.Open || d.EdgeSouth[ix, iz] != PoolEdge.Open)
                    return "coluna em vértice que toca divisa que não é aberta";
            }
            else if (gx || gz)
            {
                if (col.Radius > 0.4f + Eps) return "coluna fora do vértice com raio acima de 0,4 m";
                float along = gx ? col.Z : col.X;
                float m = along % CellSize;
                if (m < 0) m += CellSize;
                if (Math.Abs(m - 1f) > Eps && Math.Abs(m - 3f) > Eps) return "coluna sobre a linha da grade fora de 1 m do vértice";
                int cell = (int)Math.Floor(along / CellSize);
                if (gx)
                {
                    if (ix <= 0 || ix >= d.Width || cell < 0 || cell >= d.Height) return "coluna fora do mapa";
                    if (!FloorNotWell(d, ix - 1, cell) || !FloorNotWell(d, ix, cell) || d.EdgeWest[ix, cell] != PoolEdge.Open) return "coluna sobre divisa que não é aberta";
                }
                else
                {
                    if (iz <= 0 || iz >= d.Height || cell < 0 || cell >= d.Width) return "coluna fora do mapa";
                    if (!FloorNotWell(d, cell, iz - 1) || !FloorNotWell(d, cell, iz) || d.EdgeSouth[cell, iz] != PoolEdge.Open) return "coluna sobre divisa que não é aberta";
                }
            }
            else return "coluna fora da grade";

            foreach (PoolStair s in d.Stairs)
            {
                StairRect(s.X, s.Z, s.Side, s.Width, s.Steps, out float x0, out float z0, out float x1, out float z1);
                float dx = Math.Max(0f, Math.Max(x0 - col.X, col.X - x1));
                float dz = Math.Max(0f, Math.Max(z0 - col.Z, col.Z - z1));
                if (Math.Sqrt(dx * dx + dz * dz) < col.Radius + 0.1f) return "coluna em cima de escada";
            }
            return null;
        }

        /// <summary>
        /// Claraboias de uma sala como projeto, não furos ao acaso: um formato só; o conjunto
        /// espelhado em pelo menos um dos eixos da sala; e em ritmo: as colunas (em X) e as fileiras
        /// (em Z) usadas são igualmente espaçadas, e o conjunto é a grade inteira delas, o xadrez
        /// dela (cantos incluídos) ou o anel de fora dela. Null se está tudo certo.
        /// </summary>
        static string SkyPatternProblem(LevelData d, PoolRoom room)
        {
            var cells = new List<(int x, int z)>();
            PoolSky shape = PoolSky.None;
            bool mixed = false;
            for (int z = room.Z0; z < room.Z0 + room.Height; z++)
                for (int x = room.X0; x < room.X0 + room.Width; x++)
                {
                    PoolSky s = d.Sky[x, z];
                    if (s == PoolSky.None) continue;
                    if (shape == PoolSky.None) shape = s;
                    else if (s != shape) mixed = true;
                    cells.Add((x, z));
                }
            if (cells.Count == 0) return null;
            if (mixed) return "claraboias de formatos diferentes na mesma sala";

            var set = new HashSet<(int, int)>(cells);
            bool mirrorX = true, mirrorZ = true;
            var xs = new List<int>();
            var zs = new List<int>();
            foreach ((int x, int z) in cells)
            {
                if (!set.Contains((2 * room.X0 + room.Width - 1 - x, z))) mirrorX = false;
                if (!set.Contains((x, 2 * room.Z0 + room.Height - 1 - z))) mirrorZ = false;
                if (!xs.Contains(x)) xs.Add(x);
                if (!zs.Contains(z)) zs.Add(z);
            }
            if (!mirrorX && !mirrorZ) return "claraboias sem simetria na sala";

            xs.Sort();
            zs.Sort();
            bool Even(List<int> list)
            {
                for (int k = 2; k < list.Count; k++)
                    if (list[k] - list[k - 1] != list[1] - list[0]) return false;
                return true;
            }
            if (!Even(xs) || !Even(zs)) return "claraboias fora de ritmo na sala";
            bool grid = cells.Count == xs.Count * zs.Count, checker = true, ring = true;
            for (int iz = 0; iz < zs.Count; iz++)
                for (int ix = 0; ix < xs.Count; ix++)
                {
                    bool has = set.Contains((xs[ix], zs[iz]));
                    if (has != ((ix + iz) % 2 == 0)) checker = false;
                    if (has != (ix == 0 || iz == 0 || ix == xs.Count - 1 || iz == zs.Count - 1)) ring = false;
                }
            if (!grid && !checker && !ring) return "claraboias fora de ritmo na sala";
            return null;
        }

        /// <summary>Problemas da planta; lista vazia = tudo certo. Cada frase começa por um texto fixo (sem coordenadas).</summary>
        public static List<string> Validate(LevelData d)
        {
            var problems = new List<string>();
            try
            {
                ValidateCore(d, problems);
            }
            catch (Exception e)
            {
                problems.Add("exceção ao conferir a planta: " + e.GetType().Name);
            }
            return problems;
        }

        static void Add(List<string> problems, string text)
        {
            if (!problems.Contains(text)) problems.Add(text);
        }

        static void ValidateCore(LevelData d, List<string> p)
        {
            int w = d.Width, h = d.Height, n = w * h;
            if (d.Depth == null || d.Solid == null || d.Room == null || d.EdgeSouth == null || d.EdgeWest == null || d.Sky == null || d.Ceiling == null || d.Lights == null)
            {
                p.Add("faltam dados das Piscinas");
                return;
            }
            if (w < MinSize || h < MinSize) p.Add("mapa menor que 16 x 16");

            // ---------------- Salas
            int rooms = d.Rooms.Count;
            var kindCount = new int[16];
            int roomCells = 0, corridorCells = 0, wideCells = 0, narrowRooms = 0, nonTunnel = 0;
            for (int k = 0; k < rooms; k++)
            {
                PoolRoom room = d.Rooms[k];
                if (room.Id != k) Add(p, "sala com índice errado");
                if (room.X0 < 0 || room.Z0 < 0 || room.Width < 1 || room.Height < 1 || room.X0 + room.Width > w || room.Z0 + room.Height > h)
                {
                    Add(p, "sala fora do mapa");
                    continue;
                }
                kindCount[(int)room.Kind]++;
                int area = room.Width * room.Height, minDim = Math.Min(room.Width, room.Height);
                roomCells += area;
                if (minDim == 1) corridorCells += area;
                if (minDim >= 3) wideCells += area;
                if (minDim <= 2) narrowRooms++;
                if (!IsCorridorKind(room.Kind)) nonTunnel++;
                for (int z = room.Z0; z < room.Z0 + room.Height; z++)
                    for (int x = room.X0; x < room.X0 + room.Width; x++)
                    {
                        if (d.Room[x, z] != k || d.Solid[x, z]) Add(p, "célula de sala marcada com outra sala ou maciça");
                        if (Math.Abs(d.Ceiling[x, z] - room.Ceiling) > Eps) Add(p, "teto da célula diferente do da sala");
                    }
                if (room.Vault)
                {
                    if (minDim > 2) Add(p, "abóbada em sala com mais de 2 células de largura");
                    if (Math.Abs(room.Ceiling - VaultSpring) > Eps) Add(p, "abóbada sem arranque de 2,4 m");
                    if (room.VaultAlongX != (room.Width >= room.Height)) Add(p, "abóbada atravessada na sala");
                }
                else
                {
                    bool okCeiling = Math.Abs(room.Ceiling - LowCeiling) < Eps || Math.Abs(room.Ceiling - MidCeiling) < Eps ||
                                     Math.Abs(room.Ceiling - TallCeiling) < Eps || Math.Abs(room.Ceiling - TallestCeiling) < Eps;
                    if (!okCeiling) Add(p, "teto plano fora de 3,6 / 4,8 / 8 / 12 m");
                }
                if (IsCorridorKind(room.Kind))
                {
                    int len = Math.Max(room.Width, room.Height);
                    if (minDim != 1) Add(p, "túnel ou fenda com mais de 1 célula de largura");
                    if (room.Kind == PoolRoomKind.Tunnel)
                    {
                        if (!room.Vault) Add(p, "túnel sem abóbada");
                        if (len > 4) Add(p, "túnel comprido demais");
                    }
                    else
                    {
                        if (room.Vault) Add(p, "fenda com abóbada");
                        if (len > 5) Add(p, "fenda comprida demais");
                        if (room.Ceiling < TallCeiling - Eps) Add(p, "fenda sem teto alto");
                    }
                }
            }
            int floorCells = 0, solidCells = 0;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    bool solid = d.Solid[x, z];
                    if (solid) solidCells++;
                    else floorCells++;
                    if (solid != (d.Room[x, z] < 0)) Add(p, "maciço e sala não batem");
                    if (d.Room[x, z] >= rooms) Add(p, "célula aponta para sala que não existe");
                }
            if (floorCells != roomCells) Add(p, "células com piso fora das salas ou salas sobrepostas");
            for (int k = 0; k < kindCount.Length; k++)
            {
                PoolRoomKind kind = (PoolRoomKind)k;
                if (kind == PoolRoomKind.Tunnel || kind == PoolRoomKind.Slot || kind == PoolRoomKind.Gallery) continue;
                if (kindCount[k] > 2) Add(p, "tipo de sala repetido mais de 2 vezes");
            }
            if (kindCount[(int)PoolRoomKind.Start] != 1) Add(p, "não há exatamente uma sala inicial");
            if (kindCount[(int)PoolRoomKind.Exit] != 1) Add(p, "não há exatamente uma sala de saída");

            // ---------------- Orçamentos de área
            if (solidCells < MinSolid * n - Eps || solidCells > MaxSolid * n + Eps) Add(p, "maciço fora de 15 a 30% do mapa");
            if (corridorCells > MaxCorridor * n + Eps) Add(p, "corredor estreito acima de 15% das células");
            if (roomCells - corridorCells < MinRoomCells * n - Eps) Add(p, "salas abaixo de 55% do mapa");
            if (floorCells > 0 && wideCells < MinWideFloor * floorCells - Eps) Add(p, "salões e salas largas abaixo de 60% do piso");
            if (rooms > 0 && narrowRooms > Math.Ceiling(MaxNarrowRooms * rooms)) Add(p, "salas estreitas acima de 40% das salas");

            // ---------------- Sala inicial e saída
            bool startOk = d.RoomWidth > 0 && d.RoomHeight > 0 && d.RoomX0 >= 0 && d.RoomZ0 >= 0 && d.RoomX0 + d.RoomWidth <= w && d.RoomZ0 + d.RoomHeight <= h;
            if (!startOk) Add(p, "sala inicial fora do mapa");
            else
            {
                int startRoom = d.Room[d.RoomX0, d.RoomZ0];
                for (int z = d.RoomZ0; z < d.RoomZ0 + d.RoomHeight; z++)
                    for (int x = d.RoomX0; x < d.RoomX0 + d.RoomWidth; x++)
                    {
                        if (d.Solid[x, z] || d.Depth[x, z] != PoolDepth.Dry) Add(p, "a sala inicial não está seca");
                        if (d.Room[x, z] != startRoom || startRoom < 0 || d.Rooms[startRoom].Kind != PoolRoomKind.Start) Add(p, "a sala inicial não é o patamar");
                    }
                if (!d.InRoom(d.SpawnX, d.SpawnZ)) Add(p, "o ponto de início está fora da sala inicial");
            }
            bool exitIn = d.ExitX >= 0 && d.ExitZ >= 0 && d.ExitX < w && d.ExitZ < h;
            if (!exitIn) Add(p, "saída fora do mapa");
            else
            {
                if (d.Solid[d.ExitX, d.ExitZ] || d.Depth[d.ExitX, d.ExitZ] != PoolDepth.Dry) Add(p, "a saída não está seca");
                int er = d.Room[d.ExitX, d.ExitZ];
                if (er < 0 || d.Rooms[er].Kind != PoolRoomKind.Exit) Add(p, "a porta de saída não está na sala de saída");
                if ((int)d.ExitSide > 3 || d.EdgeAt(d.ExitX, d.ExitZ, d.ExitSide) != PoolEdge.Wall) Add(p, "a porta de saída não está numa parede cheia");
                else if (d.Neighbor(d.ExitX, d.ExitZ, d.ExitSide, out int ox, out int oz) && !d.Solid[ox, oz]) Add(p, "a porta de saída não dá para maciço nem para a borda");
            }

            // ---------------- Divisas
            int glazed = 0, boundary = 0, blind = 0;
            var pairEdges = new int[rooms * rooms];
            for (int pass = 0; pass < 2; pass++)
            {
                bool south = pass == 0;
                int maxX = south ? w : w + 1, maxZ = south ? h + 1 : h;
                for (int z = 0; z < maxZ; z++)
                    for (int x = 0; x < maxX; x++)
                    {
                        PoolEdge e = south ? d.EdgeSouth[x, z] : d.EdgeWest[x, z];
                        bool wall = south ? d.WallSouth[x, z] : d.WallWest[x, z];
                        // a = célula ao norte/leste (a divisa é o lado Sul/Oeste dela); b = ao sul/oeste.
                        int ax = x, az = z, bx = south ? x : x - 1, bz = south ? z - 1 : z;
                        bool aIn = ax < w && az < h, bIn = bx >= 0 && bz >= 0;
                        bool aFloor = aIn && !d.Solid[ax, az], bFloor = bIn && !d.Solid[bx, bz];
                        int ra = aFloor ? d.Room[ax, az] : -1, rb = bFloor ? d.Room[bx, bz] : -1;
                        if ((int)e > (int)PoolEdge.RoundWindow) { Add(p, "divisa com valor inválido"); continue; }
                        if (wall == Passable(e)) Add(p, "WallSouth/WallWest não batem com a divisa");
                        if (Glazed(e)) glazed++;
                        if (aFloor || bFloor)
                        {
                            if (!(aFloor && bFloor && ra == rb))
                            {
                                boundary++;
                                if (e == PoolEdge.Wall) blind++;
                            }
                        }

                        if (!aFloor && !bFloor)
                        {
                            if (e != PoolEdge.Wall) Add(p, "divisa entre maciços (ou maciço e borda) que não é parede");
                            continue;
                        }
                        if (aFloor != bFloor)
                        {
                            bool okEdge = e == PoolEdge.Wall || e == PoolEdge.GlassBlock || WindowLike(e);
                            if (!okEdge) Add(p, "passagem ou abertura para maciço ou para fora do mapa");
                            int fr = aFloor ? ra : rb;
                            if (WindowLike(e) && !aFloor) Add(p, "janela fora das paredes Sul e Oeste da sala");
                            if (Glazed(e) && d.Rooms[fr].Dark) Add(p, "janela ou tijolo de vidro em sala escura");
                            continue;
                        }
                        // As duas com piso.
                        if (ra == rb)
                        {
                            if (e != PoolEdge.Open) Add(p, "divisa dentro de uma sala que não é aberta");
                            continue;
                        }
                        if (e == PoolEdge.Open) Add(p, "divisa aberta entre salas diferentes");
                        if (Glazed(e)) Add(p, "janela ou tijolo de vidro entre duas salas");
                        if (Passable(e)) pairEdges[Math.Min(ra, rb) * rooms + Math.Max(ra, rb)]++;
                        // Lado comprido de túnel ou fenda: parede cheia (a abóbada arranca a 2,4 m).
                        WallSide sideA = south ? WallSide.South : WallSide.West, sideB = south ? WallSide.North : WallSide.East;
                        if ((IsCorridorKind(d.Rooms[ra].Kind) || d.Rooms[ra].Vault) && IsLongSide(d.Rooms[ra], sideA) && e != PoolEdge.Wall)
                            Add(p, "abertura no lado comprido de túnel ou fenda");
                        if ((IsCorridorKind(d.Rooms[rb].Kind) || d.Rooms[rb].Vault) && IsLongSide(d.Rooms[rb], sideB) && e != PoolEdge.Wall)
                            Add(p, "abertura no lado comprido de túnel ou fenda");
                    }
            }
            // Lado comprido de túnel contra maciço também é parede cheia (os laços acima só veem duas salas).
            for (int k = 0; k < rooms; k++)
            {
                PoolRoom room = d.Rooms[k];
                if (!IsCorridorKind(room.Kind) && !room.Vault) continue;
                foreach (WallSide s in Sides)
                {
                    if (!IsLongSide(room, s)) continue;
                    foreach ((int x, int z) in SideCells(room, s))
                        if (d.EdgeAt(x, z, s) != PoolEdge.Wall) Add(p, "abertura no lado comprido de túnel ou fenda");
                }
            }
            if (glazed > MaxWindows) Add(p, "mais de 48 divisas com janela ou tijolo de vidro");
            if (boundary > 0)
            {
                double ratio = (double)blind / boundary;
                if (ratio < MinBlind - 1e-6) Add(p, "parede cega abaixo de 70% das divisas de borda de sala");
                if (ratio > MaxBlind + 1e-6) Add(p, "parede cega acima de 85% das divisas de borda de sala");
            }

            // ---------------- Escadas
            for (int k = 0; k < d.Stairs.Count; k++)
            {
                PoolStair s = d.Stairs[k];
                if (s.X < 0 || s.Z < 0 || s.X >= w || s.Z >= h || (int)s.Side > 3 || !d.Neighbor(s.X, s.Z, s.Side, out int ux, out int uz))
                {
                    Add(p, "escada fora do mapa");
                    continue;
                }
                if (d.Solid[s.X, s.Z] || d.Solid[ux, uz]) { Add(p, "escada em maciço"); continue; }
                PoolEdge e = d.EdgeAt(s.X, s.Z, s.Side);
                if (!Passable(e)) { Add(p, "escada numa divisa que não se passa"); continue; }
                if (d.Depth[ux, uz] == PoolDepth.Well) { Add(p, "escada subindo para um poço"); continue; }
                float foot = FootHeight(d, s.X, s.Z), top = d.FloorHeight(ux, uz);
                if (top <= foot + Eps) { Add(p, "escada cuja célula de baixo não é mais baixa"); continue; }
                int steps = (int)Math.Round((top - foot) / LevelData.StepRise);
                if (s.Steps != steps) Add(p, "escada com número de degraus diferente do desnível");
                if (s.Steps > 12) Add(p, "escada com mais de 12 degraus");
                if (!(s.Width > 0f) || s.Width > Span(e) + Eps) Add(p, "escada mais larga que o vão da divisa");
                string conflict = StairConflict(d, s, k);
                if (conflict != null) Add(p, conflict);
            }

            // ---------------- Alcance a pé e poços
            bool reachOk = startOk && d.InRoom(d.SpawnX, d.SpawnZ);
            if (reachOk && !AllReachableOnFoot(d)) Add(p, "há célula com piso que não se alcança a pé");

            int wellCount = 0;
            bool anyDeepExit = false;
            var wellDone = new bool[n];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    if (d.Solid[x, z] || d.Depth[x, z] != PoolDepth.Well) continue;
                    wellCount++;
                    foreach (WallSide s in Sides)
                    {
                        if (!d.Neighbor(x, z, s, out int nx, out int nz) || !Passable(d.EdgeAt(x, z, s)) || d.Solid[nx, nz]) continue;
                        PoolDepth q = d.Depth[nx, nz];
                        if (q == PoolDepth.Deep) anyDeepExit = true;
                        else if (q != PoolDepth.Well) Add(p, "poço aberto para seco ou raso");
                    }
                    if (wellDone[x + z * w]) continue;
                    // Grupo de poços ligados: precisa de uma saída nadando, uma célula funda aberta
                    // para ele. A funda, por sua vez, alcança uma escada que sobe para o raso ou o
                    // seco (é o que AllReachableOnFoot garante, nos dois sentidos). Escada saindo do
                    // próprio poço não cabe mais (LevelData.WellStairFoot).
                    bool exitFound = false;
                    var queue = new Queue<int>();
                    queue.Enqueue(x + z * w);
                    wellDone[x + z * w] = true;
                    while (queue.Count > 0)
                    {
                        int i = queue.Dequeue(), cx = i % w, cz = i / w;
                        foreach (WallSide s in Sides)
                        {
                            if (!d.Neighbor(cx, cz, s, out int nx, out int nz) || !Passable(d.EdgeAt(cx, cz, s)) || d.Solid[nx, nz]) continue;
                            if (d.Depth[nx, nz] == PoolDepth.Deep) exitFound = true;
                            else if (d.Depth[nx, nz] == PoolDepth.Well && !wellDone[nx + nz * w])
                            {
                                wellDone[nx + nz * w] = true;
                                queue.Enqueue(nx + nz * w);
                            }
                        }
                    }
                    if (!exitFound) Add(p, "poço sem saída nadando");
                }
            if (wellCount < MinWells || wellCount > MaxWells) Add(p, "poços fora de 3 a 8 células");
            if (!anyDeepExit) Add(p, "nenhum poço com vizinha funda aberta");

            // ---------------- Colunas
            for (int k = 0; k < d.Columns.Count; k++)
            {
                string why = ColumnProblem(d, d.Columns[k]);
                if (why != null) Add(p, why);
                for (int j = 0; j < k; j++)
                    if (Math.Abs(d.Columns[j].X - d.Columns[k].X) < Eps && Math.Abs(d.Columns[j].Z - d.Columns[k].Z) < Eps) Add(p, "duas colunas no mesmo lugar");
            }

            // ---------------- Lâmpadas
            if (d.Lamps.Count > MaxLamps) Add(p, "mais de 24 lâmpadas");
            var darkLamps = new int[rooms];
            for (int k = 0; k < d.Lamps.Count; k++)
            {
                PoolLamp l = d.Lamps[k];
                if (l.X < 0 || l.Z < 0 || l.X >= w || l.Z >= h || (int)l.Side > 3 || d.Solid[l.X, l.Z]) { Add(p, "lâmpada fora de célula com piso"); continue; }
                PoolDepth q = d.Depth[l.X, l.Z];
                if (q != PoolDepth.Deep && q != PoolDepth.Well) Add(p, "lâmpada fora de água funda ou poço");
                if (l.Y < d.FloorHeight(l.X, l.Z) + 0.3f - Eps || l.Y > -0.4f + Eps) Add(p, "lâmpada fora da altura (piso + 0,3 até -0,4)");
                float top = LampTop(d, l.X, l.Z, l.Side);
                if (float.IsNaN(top) || l.Y > top + Eps) Add(p, "lâmpada num lado que não é parede nem borda mais alta");
                darkLamps[d.Room[l.X, l.Z]]++;
                for (int j = 0; j < k; j++)
                    if (d.Lamps[j].X == l.X && d.Lamps[j].Z == l.Z && d.Lamps[j].Side == l.Side) Add(p, "duas lâmpadas no mesmo lado da célula");
            }

            // ---------------- Claraboias, luz e salas escuras
            int skyCells = 0;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    PoolSky s = d.Sky[x, z];
                    LightState l = d.Lights[x, z];
                    if (s == PoolSky.None)
                    {
                        if (l != LightState.None) Add(p, "luz acesa em célula sem claraboia");
                        continue;
                    }
                    skyCells++;
                    if (l != LightState.On) Add(p, "claraboia sem luz acesa");
                    if (d.Solid[x, z]) { Add(p, "claraboia em maciço"); continue; }
                    PoolRoom room = d.Rooms[d.Room[x, z]];
                    if (room.Vault) Add(p, "claraboia em abóbada");
                    if (room.Dark) Add(p, "claraboia em sala escura");
                }
            if (skyCells < MinSky || skyCells > MaxSky) Add(p, "claraboias fora de 30 a 70 células");
            for (int k = 0; k < rooms; k++)
            {
                string why = SkyPatternProblem(d, d.Rooms[k]);
                if (why != null) Add(p, why);
            }
            for (int k = 0; k < rooms; k++)
                if (d.Rooms[k].Dark && darkLamps[k] == 0) Add(p, "sala escura sem lâmpada");

            // ---------------- Marcos: 3 ou 4 salas de teto alto em pelo menos 3 quadrantes
            int tall = 0;
            var quadrants = new bool[4];
            foreach (PoolRoom room in d.Rooms)
            {
                if (room.Vault || room.Ceiling < TallCeiling - Eps) continue;
                tall++;
                double cx = room.X0 + room.Width * 0.5, cz = room.Z0 + room.Height * 0.5;
                quadrants[(cx >= w * 0.5 ? 1 : 0) + (cz >= h * 0.5 ? 2 : 0)] = true;
            }
            int quadCount = (quadrants[0] ? 1 : 0) + (quadrants[1] ? 1 : 0) + (quadrants[2] ? 1 : 0) + (quadrants[3] ? 1 : 0);
            if (tall > MaxTallRooms) Add(p, "mais de 4 salas de teto alto");
            if (quadCount < 3) Add(p, "salas de teto alto em menos de 3 quadrantes");

            // ---------------- Grafo das salas: laços, becos, salões encostados
            int pairs = 0;
            var degree = new int[rooms];
            for (int a = 0; a < rooms; a++)
                for (int b = a + 1; b < rooms; b++)
                {
                    int edges = pairEdges[a * rooms + b];
                    if (edges == 0) continue;
                    pairs++;
                    degree[a]++;
                    degree[b]++;
                    if (d.Rooms[a].Kind == PoolRoomKind.Hall && d.Rooms[b].Kind == PoolRoomKind.Hall && edges < 2)
                        Add(p, "salões encostados ligados por menos de 2 vãos");
                }
            int loops = pairs - rooms + 1;
            if (loops < Math.Max(2, (int)Math.Ceiling(0.3 * nonTunnel))) Add(p, "poucos laços entre as salas");
            for (int k = 0; k < rooms; k++)
            {
                PoolRoomKind kind = d.Rooms[k].Kind;
                if (IsCorridorKind(kind) && degree[k] != 2) Add(p, "túnel ou fenda que não liga duas salas");
                if (degree[k] == 1 && kind != PoolRoomKind.Exit && kind != PoolRoomKind.WellRoom && kind != PoolRoomKind.Cistern && !IsCorridorKind(kind))
                    Add(p, "beco sem motivo");
            }
            for (int a = 0; a < rooms; a++)
            {
                if (!IsCorridorKind(d.Rooms[a].Kind)) continue;
                for (int b = 0; b < rooms; b++)
                    if (b != a && IsCorridorKind(d.Rooms[b].Kind) && pairEdges[Math.Min(a, b) * rooms + Math.Max(a, b)] > 0)
                        Add(p, "dois túneis seguidos");
            }

            // ---------------- Distâncias: grande poço e saída longe do início
            double diag = Math.Sqrt(w * w + h * h);
            if (startOk && exitIn)
            {
                double sx = d.RoomX0 + d.RoomWidth * 0.5, sz = d.RoomZ0 + d.RoomHeight * 0.5;
                double ex = d.ExitX + 0.5 - sx, ez = d.ExitZ + 0.5 - sz;
                if (Math.Sqrt(ex * ex + ez * ez) < FarFraction * diag) Add(p, "saída perto demais do início");
                PoolRoom wellRoom = null;
                foreach (PoolRoom room in d.Rooms)
                    if (room.Kind == PoolRoomKind.WellRoom && (wellRoom == null || room.Width * room.Height > wellRoom.Width * wellRoom.Height)) wellRoom = room;
                if (wellRoom == null) Add(p, "não há sala do grande poço");
                else
                {
                    double wx = wellRoom.X0 + wellRoom.Width * 0.5 - sx, wz = wellRoom.Z0 + wellRoom.Height * 0.5 - sz;
                    if (Math.Sqrt(wx * wx + wz * wz) < FarFraction * diag) Add(p, "sala do grande poço perto demais do início");
                }
            }

            // ---------------- Caminho a pé: do início à saída sem nadar (só seco e raso). A água
            // funda é atalho ou desvio, nunca obrigatória.
            if (reachOk && exitIn && FootPath(d).Count < 2) Add(p, "não há caminho sem nadar até a saída");
        }

        // ================================================================ Resumo e mapa em texto

        /// <summary>Resumo da planta, para comparar duas gerações (ou duas máquinas).</summary>
        public static uint Hash(LevelData d)
        {
            uint hash = 2166136261;
            void Mix(int value) { unchecked { hash = (hash ^ (uint)value) * 16777619u; } }

            Mix(d.Width);
            Mix(d.Height);
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    Mix(d.Solid[x, z] ? 1 : 0);
                    Mix((int)d.Depth[x, z]);
                    Mix(d.Room[x, z]);
                    Mix((int)Math.Round(d.Ceiling[x, z] * 10f));
                    Mix((int)d.Sky[x, z]);
                    Mix((int)d.Lights[x, z]);
                }
            foreach (PoolEdge edge in d.EdgeSouth) Mix((int)edge);
            foreach (PoolEdge edge in d.EdgeWest) Mix((int)edge);
            foreach (PoolRoom r in d.Rooms)
            {
                Mix((int)r.Kind); Mix(r.X0); Mix(r.Z0); Mix(r.Width); Mix(r.Height);
                Mix((int)Math.Round(r.Ceiling * 10f));
                Mix((r.Vault ? 1 : 0) | (r.VaultAlongX ? 2 : 0) | (r.Accent ? 4 : 0) | (r.Dark ? 8 : 0));
            }
            foreach (PoolStair s in d.Stairs) { Mix(s.X); Mix(s.Z); Mix((int)s.Side); Mix(s.Steps); Mix((int)Math.Round(s.Width * 10f)); }
            foreach (PoolColumn c in d.Columns) { Mix((int)Math.Round(c.X * 10f)); Mix((int)Math.Round(c.Z * 10f)); Mix((int)Math.Round(c.Radius * 100f)); Mix(c.Round ? 1 : 0); }
            foreach (PoolLamp l in d.Lamps) { Mix(l.X); Mix(l.Z); Mix((int)l.Side); Mix((int)Math.Round(l.Y * 10f)); }
            Mix(d.ExitX); Mix(d.ExitZ); Mix((int)d.ExitSide);
            Mix(d.RoomX0); Mix(d.RoomZ0); Mix(d.RoomWidth); Mix(d.RoomHeight); Mix(d.SpawnX); Mix(d.SpawnZ);
            return hash;
        }

        static char EdgeChar(PoolEdge e, bool vertical)
        {
            switch (e)
            {
                case PoolEdge.Open: return ' ';
                case PoolEdge.Wall: return vertical ? '|' : '-';
                case PoolEdge.Arch: return 'a';
                case PoolEdge.WideArch: return 'A';
                case PoolEdge.Doorway: return 'd';
                case PoolEdge.Porthole: return 'o';
                case PoolEdge.Window: return 'J';
                case PoolEdge.GlassBlock: return 'g';
                case PoolEdge.Slits: return 'f';
                case PoolEdge.RoundWindow: return 'v';
                default: return '?';
            }
        }

        static char SkyChar(PoolSky s)
        {
            switch (s)
            {
                case PoolSky.Square: return 'q';
                case PoolSky.Round: return 'o';
                case PoolSky.SlotX: return '=';
                case PoolSky.SlotZ: return '!';
                case PoolSky.Full: return '*';
                default: return ' ';
            }
        }

        static char RoomChar(int id)
        {
            if (id < 0) return '#';
            if (id < 26) return (char)('A' + id);
            if (id < 52) return (char)('a' + id - 26);
            return '?';
        }

        /// <summary>
        /// Mapa em texto para conferir de olho. Norte em cima. Cada célula tem 4 letras: sala,
        /// profundidade (S seco, R raso, F fundo, P poço; # maciço), claraboia e marca (escada que
        /// sobe para ^ v &lt; &gt;, L lâmpada, @ início, X saída).
        /// </summary>
        public static string Ascii(LevelData d)
        {
            var sb = new StringBuilder();
            int w = d.Width, h = d.Height;
            sb.AppendLine("Legenda: célula = sala, profundidade (S seco, R raso, F fundo, P poço, # maciço), claraboia (q quadrada, o redonda,");
            sb.AppendLine("  = rasgo em X, ! rasgo em Z, * teto de vidro), marca (^ v < > escada subindo, L lâmpada, @ início, X saída).");
            sb.AppendLine("  Divisas: - | parede, a arco, A arco largo, d vão reto, o abertura redonda, J janelão, f frestas, v vigia, g tijolo de vidro.");
            sb.AppendLine("  Vértices: O tambor/coluna redonda, Q pilar quadrado, + parede.");

            var marks = new char[w, h];
            for (int z = 0; z < h; z++) for (int x = 0; x < w; x++) marks[x, z] = ' ';
            foreach (PoolLamp l in d.Lamps) marks[l.X, l.Z] = 'L';
            foreach (PoolStair s in d.Stairs)
                marks[s.X, s.Z] = s.Side == WallSide.North ? '^' : s.Side == WallSide.South ? 'v' : s.Side == WallSide.West ? '<' : '>';
            if (d.SpawnX >= 0 && d.SpawnX < w && d.SpawnZ >= 0 && d.SpawnZ < h) marks[d.SpawnX, d.SpawnZ] = '@';
            if (d.ExitX >= 0 && d.ExitX < w && d.ExitZ >= 0 && d.ExitZ < h) marks[d.ExitX, d.ExitZ] = 'X';

            var columns = new char[w + 1, h + 1];
            foreach (PoolColumn c in d.Columns)
                if (OnGrid(c.X, out int ix) && OnGrid(c.Z, out int iz) && ix >= 0 && iz >= 0 && ix <= w && iz <= h)
                    columns[ix, iz] = c.Round ? 'O' : 'Q';

            for (int z = h; z >= 0; z--)
            {
                // Linha das divisas ao longo de X (EdgeSouth[x, z]).
                var line = new StringBuilder();
                for (int x = 0; x <= w; x++)
                {
                    char v = columns[x, z];
                    if (v == '\0')
                    {
                        bool any = false;
                        if (x > 0 && d.EdgeSouth[x - 1, z] != PoolEdge.Open) any = true;
                        if (x < w && d.EdgeSouth[x, z] != PoolEdge.Open) any = true;
                        if (z > 0 && d.EdgeWest[x, z - 1] != PoolEdge.Open) any = true;
                        if (z < h && d.EdgeWest[x, z] != PoolEdge.Open) any = true;
                        v = any ? '+' : ' ';
                    }
                    line.Append(v);
                    if (x == w) break;
                    bool solidBoth = (z == h || d.Solid[x, z]) && (z == 0 || d.Solid[x, z - 1]);
                    char e = solidBoth ? '#' : EdgeChar(d.EdgeSouth[x, z], false);
                    if (d.ExitZ == z && d.ExitX == x && d.ExitSide == WallSide.South) e = 'X';
                    if (d.ExitZ == z - 1 && d.ExitX == x && d.ExitSide == WallSide.North) e = 'X';
                    line.Append(e, 4);
                }
                sb.AppendLine(line.ToString());
                if (z == 0) break;

                int row = z - 1;
                line.Clear();
                for (int x = 0; x <= w; x++)
                {
                    bool solidBoth = (x == 0 || d.Solid[x - 1, row]) && (x == w || d.Solid[x, row]);
                    char e = solidBoth ? '#' : EdgeChar(d.EdgeWest[x, row], true);
                    if (d.ExitZ == row && d.ExitX == x && d.ExitSide == WallSide.West) e = 'X';
                    if (d.ExitZ == row && d.ExitX == x - 1 && d.ExitSide == WallSide.East) e = 'X';
                    line.Append(e);
                    if (x == w) break;
                    if (d.Solid[x, row]) { line.Append("####"); continue; }
                    char depth = d.Depth[x, row] == PoolDepth.Dry ? 'S' : d.Depth[x, row] == PoolDepth.Shallow ? 'R' : d.Depth[x, row] == PoolDepth.Deep ? 'F' : 'P';
                    line.Append(RoomChar(d.Room[x, row])).Append(depth).Append(SkyChar(d.Sky[x, row])).Append(marks[x, row]);
                }
                sb.AppendLine(line.ToString());
            }

            foreach (PoolRoom r in d.Rooms)
            {
                sb.Append(RoomChar(r.Id)).Append(' ').Append(r.Kind).Append(' ').Append(r.Width).Append('x').Append(r.Height)
                  .Append(" em (").Append(r.X0).Append(',').Append(r.Z0).Append(") teto ").Append(r.Ceiling.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                if (r.Vault) sb.Append(" abóbada");
                if (r.Dark) sb.Append(" escura");
                if (r.Accent) sb.Append(" faixa azul");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
