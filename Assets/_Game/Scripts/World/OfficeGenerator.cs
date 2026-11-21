using System;
using System.Collections.Generic;

namespace BackRooms.World
{
    /// <summary>
    /// Planta do Escritório (Level 3, Docs/level3-escritorio.md, seção 2): um andar de prédio
    /// com um anel de corredores em volta de um miolo, um ou dois corredores transversais, e as
    /// sobras divididas em salas. No miolo ficam o saguão dos elevadores (com a cabine onde os
    /// jogadores nascem) e salas sem janela; na faixa entre o anel e a fachada, salas com janela,
    /// a cafeteria, a área de estar e o vão da escada, onde está a saída.
    ///
    /// C# puro e só <see cref="Random"/> com a seed: cada máquina monta a mesma planta. Se a
    /// planta sorteada reprova em <see cref="Validate"/>, sorteia de novo (com a mesma seed e o
    /// número da tentativa), até 40 vezes.
    /// </summary>
    public static class OfficeGenerator
    {
        public const int DefaultWidth = 14;
        public const int DefaultHeight = 12;

        const int Attempts = 40;
        /// <summary>Células, no mínimo, do caminho entre a cabine e a escada.</summary>
        public const int MinPathCells = 10;
        /// <summary>Distância mínima (em células, pela grade) entre o saguão e o vão da escada.</summary>
        public const int MinAtriumDistance = 6;
        /// <summary>A placa pendurada da última curva só entra se a curva fica a até tantas células da escada.</summary>
        const int MaxExitSignCells = 3;
        /// <summary>
        /// A entrada da escada fica a pé (só corredor e saguão) a pelo menos esta parte do corredor
        /// mais longe da cabine, medida também em passos (21/11/2026).
        /// </summary>
        public const float MinExitShare = 0.9f;

        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

        public static LevelData Generate(int seed, int width, int height)
        {
            width = Math.Max(12, width);
            height = Math.Max(10, height);

            LevelData last = null;
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                var rng = new Random(unchecked(seed * 31 + attempt * 7919));
                LevelData d = TryBuild(seed, width, height, rng);
                if (d == null) continue;
                last = d;
                if (Validate(d).Count == 0) return d;
            }

            if (last == null) throw new InvalidOperationException($"OfficeGenerator: nenhuma planta possível para {width} x {height} (seed {seed}).");
            return last;
        }

        // ---------------------------------------------------------------- Montagem

        struct Rect
        {
            public int X0, Z0, X1, Z1;
            public int Width => X1 - X0 + 1;
            public int Height => Z1 - Z0 + 1;
            public Rect(int x0, int z0, int x1, int z1) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; }
        }

        /// <summary>Faixa entre o anel e a fachada: retângulo, eixo ao longo do anel e o lado da fachada.</summary>
        struct Strip
        {
            public Rect Area;
            public bool AlongX;
            public WallSide Facade;
        }

        sealed class Plan
        {
            public LevelData D;
            public Random Rng;
            public int Rx0, Rx1, Rz0, Rz1;
            public int LobbyCx, LobbyCz;
            public readonly List<(int room, bool glass)> Fronts = new List<(int, bool)>();
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
                Office = new OfficeCell[w, h],
                OfficeSouth = new OfficeEdge[w, h + 1],
                OfficeWest = new OfficeEdge[w + 1, h],
                LockersSouth = new byte[w, h + 1],
                LockersWest = new byte[w + 1, h],
                OfficeRoomOf = new int[w, h]
            };

            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    d.OfficeRoomOf[x, z] = -1;
            return d;
        }

        static LevelData TryBuild(int seed, int w, int h, Random rng)
        {
            var p = new Plan { D = NewData(seed, w, h), Rng = rng };
            LevelData d = p.D;

            // Anel de corredores, recuado da fachada pela faixa de salas (2 ou 3 células).
            int a = 2 + rng.Next(2);
            int b = 2 + rng.Next(2);
            p.Rx0 = a;
            p.Rx1 = w - 1 - a;
            p.Rz0 = b;
            p.Rz1 = h - 1 - b;
            if (p.Rx1 - p.Rx0 < 6 || p.Rz1 - p.Rz0 < 4) return null;

            for (int x = p.Rx0; x <= p.Rx1; x++) { d.Office[x, p.Rz0] = OfficeCell.Corridor; d.Office[x, p.Rz1] = OfficeCell.Corridor; }
            for (int z = p.Rz0; z <= p.Rz1; z++) { d.Office[p.Rx0, z] = OfficeCell.Corridor; d.Office[p.Rx1, z] = OfficeCell.Corridor; }

            // Transversal norte-sul pelo miolo: dos dois lados sobram 2 células ou mais, e de um deles 3
            // (o saguão precisa de 3 de largura).
            var xs = new List<int> { p.Rx0 };
            var candidates = new List<int>();
            for (int tx = p.Rx0 + 3; tx <= p.Rx1 - 3; tx++)
            {
                int left = tx - p.Rx0 - 1, right = p.Rx1 - tx - 1;
                if (left >= 2 && right >= 2 && Math.Max(left, right) >= 3) candidates.Add(tx);
            }
            if (candidates.Count == 0) return null;
            int cross = candidates[rng.Next(candidates.Count)];
            for (int z = p.Rz0; z <= p.Rz1; z++) d.Office[cross, z] = OfficeCell.Corridor;
            xs.Add(cross);
            xs.Add(p.Rx1);

            // Transversal leste-oeste, às vezes, quando o miolo é fundo.
            var zs = new List<int> { p.Rz0 };
            int coreDepth = p.Rz1 - p.Rz0 - 1;
            if (coreDepth >= 6 && rng.NextDouble() < 0.5)
            {
                int tz = rng.Next(2) == 0 ? p.Rz0 + 4 : p.Rz1 - 4;
                for (int x = p.Rx0; x <= p.Rx1; x++) d.Office[x, tz] = OfficeCell.Corridor;
                zs.Add(tz);
            }
            zs.Add(p.Rz1);

            var blocks = new List<Rect>();
            for (int i = 0; i + 1 < xs.Count; i++)
                for (int j = 0; j + 1 < zs.Count; j++)
                {
                    var r = new Rect(xs[i] + 1, zs[j] + 1, xs[i + 1] - 1, zs[j + 1] - 1);
                    if (r.Width > 0 && r.Height > 0) blocks.Add(r);
                }

            if (!PlaceLobby(p, blocks, out Rect lobbyBlock, out List<Rect> lobbyLeftovers)) return null;

            foreach (Rect block in blocks)
            {
                if (block.X0 == lobbyBlock.X0 && block.Z0 == lobbyBlock.Z0) continue;
                if (!Partition(p, block, block.Width >= block.Height, (x, z) => true, WallSide.Floor)) return null;
            }
            foreach (Rect rest in lobbyLeftovers)
                if (!Partition(p, rest, rest.Width >= rest.Height, (x, z) => TouchesCorridor(d, x, z), WallSide.Floor)) return null;

            // Faixa da fachada: quatro tiras. As do sul e do norte vão de ponta a ponta do mapa e
            // incluem os cantos; as de oeste e leste ficam entre elas.
            var strips = new[]
            {
                new Strip { Area = new Rect(0, 0, w - 1, p.Rz0 - 1), AlongX = true, Facade = WallSide.South },
                new Strip { Area = new Rect(0, p.Rz1 + 1, w - 1, h - 1), AlongX = true, Facade = WallSide.North },
                new Strip { Area = new Rect(0, p.Rz0, p.Rx0 - 1, p.Rz1), AlongX = false, Facade = WallSide.West },
                new Strip { Area = new Rect(p.Rx1 + 1, p.Rz0, w - 1, p.Rz1), AlongX = false, Facade = WallSide.East },
            };

            var reserved = new List<Rect>();
            if (!PlaceAtrium(p, strips, reserved)) return null;
            PlaceSpecial(p, strips, reserved, OfficeCell.Cafe, 3, nearLobby: true);
            PlaceSpecial(p, strips, reserved, OfficeCell.Lounge, 2, nearLobby: false);

            foreach (Strip strip in strips)
            {
                Strip s = strip;
                int start = s.AlongX ? s.Area.X0 : s.Area.Z0;
                int end = s.AlongX ? s.Area.X1 : s.Area.Z1;
                int pos = start;
                while (pos <= end)
                {
                    // Pula o que já foi reservado; o resto até a próxima reserva vira salas.
                    int next = pos;
                    while (next <= end && !Free(d, s, next)) next++;
                    pos = next;
                    if (pos > end) break;
                    int stop = pos;
                    while (stop + 1 <= end && Free(d, s, stop + 1)) stop++;

                    Rect run = s.AlongX ? new Rect(pos, s.Area.Z0, stop, s.Area.Z1) : new Rect(s.Area.X0, pos, s.Area.X1, stop);
                    if (!Partition(p, run, s.AlongX, (x, z) => TouchesCorridor(d, x, z), s.Facade)) return null;
                    pos = stop + 1;
                }
            }

            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    if (d.Office[x, z] == OfficeCell.Solid) return null;

            AssignEdges(p);
            PlaceDoors(p);
            FinishWalls(d);
            PlaceLockers(p);
            PlaceLights(p);
            FindPath(d);
            PlaceExitSigns(d);
            return d;
        }

        static bool Free(LevelData d, Strip s, int along)
        {
            int x = s.AlongX ? along : s.Area.X0;
            int z = s.AlongX ? s.Area.Z0 : along;
            return d.Office[x, z] == OfficeCell.Solid;
        }

        static bool TouchesCorridor(LevelData d, int x, int z)
        {
            foreach (WallSide side in Sides)
                if (d.Neighbor(x, z, side, out int nx, out int nz) && d.Office[nx, nz] == OfficeCell.Corridor) return true;
            return false;
        }

        static int AddRoom(Plan p, OfficeCell kind, Rect r, WallSide window, bool lit)
        {
            LevelData d = p.D;
            int index = d.OfficeRooms.Count;
            d.OfficeRooms.Add(new OfficeRoom
            {
                Kind = kind,
                X0 = r.X0,
                Z0 = r.Z0,
                Width = r.Width,
                Height = r.Height,
                Lit = lit,
                Variant = p.Rng.Next(1 << 20),
                WindowSide = window
            });

            for (int z = r.Z0; z <= r.Z1; z++)
                for (int x = r.X0; x <= r.X1; x++)
                {
                    d.Office[x, z] = kind;
                    d.OfficeRoomOf[x, z] = index;
                }
            return index;
        }

        // Saguão de 3 x 2 encostado num corredor, com a fileira dos elevadores atrás (poço, cabine, poço).
        static bool PlaceLobby(Plan p, List<Rect> blocks, out Rect lobbyBlock, out List<Rect> leftovers)
        {
            LevelData d = p.D;
            lobbyBlock = default;
            leftovers = new List<Rect>();

            var options = new List<(Rect block, bool south, int x0)>();
            foreach (Rect block in blocks)
            {
                if (block.Width < 3 || block.Height < 3) continue;
                for (int x0 = block.X0; x0 + 2 <= block.X1; x0++)
                {
                    options.Add((block, true, x0));
                    options.Add((block, false, x0));
                }
            }
            if (options.Count == 0) return false;

            (Rect chosen, bool south, int lx0) = options[p.Rng.Next(options.Count)];
            lobbyBlock = chosen;

            int row0 = south ? chosen.Z0 : chosen.Z1 - 1;
            int elevatorRow = south ? chosen.Z0 + 2 : chosen.Z1 - 2;
            WallSide toLobby = south ? WallSide.South : WallSide.North;

            AddRoom(p, OfficeCell.Lobby, new Rect(lx0, row0, lx0 + 2, row0 + 1), WallSide.Floor, true);
            p.LobbyCx = lx0 + 1;
            p.LobbyCz = row0;

            int cab = AddRoom(p, OfficeCell.Cab, new Rect(lx0 + 1, elevatorRow, lx0 + 1, elevatorRow), WallSide.Floor, true);
            d.Office[lx0, elevatorRow] = OfficeCell.Shaft;
            d.Office[lx0 + 2, elevatorRow] = OfficeCell.Shaft;
            d.Elevators.Add(new OfficeElevator { X = lx0, Z = elevatorRow, Side = toLobby });
            d.Elevators.Add(new OfficeElevator { X = lx0 + 1, Z = elevatorRow, Side = toLobby, IsCab = true });
            d.Elevators.Add(new OfficeElevator { X = lx0 + 2, Z = elevatorRow, Side = toLobby });

            d.SpawnX = lx0 + 1;
            d.SpawnZ = elevatorRow;
            d.RoomX0 = d.SpawnX;
            d.RoomZ0 = d.SpawnZ;
            d.RoomWidth = 1;
            d.RoomHeight = 1;
            _ = cab;

            // O resto do quarteirão: as colunas dos lados (fundura inteira) e, nas colunas do
            // saguão, as fileiras além dos elevadores.
            if (lx0 > chosen.X0) leftovers.Add(new Rect(chosen.X0, chosen.Z0, lx0 - 1, chosen.Z1));
            if (lx0 + 2 < chosen.X1) leftovers.Add(new Rect(lx0 + 3, chosen.Z0, chosen.X1, chosen.Z1));
            if (south && elevatorRow < chosen.Z1) leftovers.Add(new Rect(lx0, elevatorRow + 1, lx0 + 2, chosen.Z1));
            if (!south && elevatorRow > chosen.Z0) leftovers.Add(new Rect(lx0, chosen.Z0, lx0 + 2, elevatorRow - 1));
            return true;
        }

        // Posições possíveis de um espaço de <paramref name="length"/> células na faixa da fachada,
        // com todas as células da frente encostadas no anel e sem deixar sobra que não encoste nele.
        static List<Rect> StripSlots(Plan p, Strip[] strips, int length, List<Rect> reserved)
        {
            var slots = new List<Rect>();
            foreach (Strip s in strips)
            {
                if (s.AlongX)
                {
                    for (int x0 = p.Rx0 + 1; x0 + length - 1 <= p.Rx1 - 1; x0++)
                    {
                        var r = new Rect(x0, s.Area.Z0, x0 + length - 1, s.Area.Z1);
                        if (!Overlaps(r, reserved)) slots.Add(r);
                    }
                }
                else
                {
                    for (int z0 = s.Area.Z0; z0 + length - 1 <= s.Area.Z1; z0++)
                    {
                        var r = new Rect(s.Area.X0, z0, s.Area.X1, z0 + length - 1);
                        if (!Overlaps(r, reserved)) slots.Add(r);
                    }
                }
            }
            return slots;
        }

        static bool Overlaps(Rect r, List<Rect> list)
        {
            foreach (Rect o in list)
                if (r.X0 <= o.X1 && o.X0 <= r.X1 && r.Z0 <= o.Z1 && o.Z0 <= r.Z1) return true;
            return false;
        }

        static Strip StripOf(Strip[] strips, Rect r)
        {
            foreach (Strip s in strips)
                if (r.X0 >= s.Area.X0 && r.X1 <= s.Area.X1 && r.Z0 >= s.Area.Z0 && r.Z1 <= s.Area.Z1) return s;
            return strips[0];
        }

        static int LobbyDistance(Plan p, Rect r)
        {
            int cx = (r.X0 + r.X1) / 2, cz = (r.Z0 + r.Z1) / 2;
            return Math.Abs(cx - p.LobbyCx) + Math.Abs(cz - p.LobbyCz);
        }

        // O vão da escada: 3 células ao longo do anel, a fundura inteira da faixa. A entrada da
        // escada (a célula de corredor na frente dela) fica o mais longe possível do saguão a pé, de
        // preferência fora da vista do saguão e sem um corredor desembocando de frente para a porta
        // (21/11/2026: antes era o mais longe em linha reta, e de alguns pontos via-se o vão).
        static bool PlaceAtrium(Plan p, Strip[] strips, List<Rect> reserved)
        {
            LevelData d = p.D;
            List<Rect> slots = StripSlots(p, strips, 3, reserved);
            int[] walk = CorridorWalk(d);

            var options = new List<(Rect r, OfficeAtrium info, int walk)>();
            int best = -1;
            foreach (Rect r in slots)
            {
                if (LobbyDistance(p, r) < MinAtriumDistance) continue;
                foreach (bool first in new[] { true, false })
                {
                    OfficeAtrium info = AtriumAt(r, StripOf(strips, r), first);
                    if (d.Office[info.HeadX, info.HeadZ] != OfficeCell.Corridor) continue;
                    int dist = walk[info.HeadX + info.HeadZ * d.Width];
                    if (dist < 0) continue;
                    options.Add((r, info, dist));
                    best = Math.Max(best, dist);
                }
            }
            if (options.Count == 0) return false;

            // O mais longe e escondido; senão um passo mais perto e escondido; senão o mais longe.
            bool Hidden((Rect r, OfficeAtrium info, int walk) o) => !SeenFromLobby(d, o.info.HeadX, o.info.HeadZ) && !FacesCorridor(d, o.info);
            var far = options.FindAll(o => o.walk == best && Hidden(o));
            if (far.Count == 0) far = options.FindAll(o => o.walk >= best - 1 && Hidden(o));
            if (far.Count == 0) far = options.FindAll(o => o.walk == best);
            (Rect atrium, OfficeAtrium chosen, _) = far[p.Rng.Next(far.Count)];
            Strip s = StripOf(strips, atrium);

            AddRoom(p, OfficeCell.Atrium, atrium, s.Facade, false);
            reserved.Add(atrium);
            d.Atrium = chosen;
            for (int z = atrium.Z0; z <= atrium.Z1; z++)
                for (int x = atrium.X0; x <= atrium.X1; x++)
                    d.Pit[x, z] = true;
            return true;
        }

        // A escada num retângulo da faixa: encosta na ponta oeste/sul (first) ou leste/norte.
        static OfficeAtrium AtriumAt(Rect atrium, Strip s, bool first)
        {
            var info = new OfficeAtrium
            {
                X0 = atrium.X0,
                Z0 = atrium.Z0,
                Width = atrium.Width,
                Height = atrium.Height,
                Inward = s.Facade
            };

            if (s.AlongX)
            {
                info.EndSide = first ? WallSide.West : WallSide.East;
                info.TopX = first ? atrium.X0 : atrium.X1;
                info.TopZ = s.Facade == WallSide.South ? atrium.Z1 : atrium.Z0;
                info.HeadX = info.TopX;
                info.HeadZ = s.Facade == WallSide.South ? info.TopZ + 1 : info.TopZ - 1;
            }
            else
            {
                info.EndSide = first ? WallSide.South : WallSide.North;
                info.TopZ = first ? atrium.Z0 : atrium.Z1;
                info.TopX = s.Facade == WallSide.West ? atrium.X1 : atrium.X0;
                info.HeadZ = info.TopZ;
                info.HeadX = s.Facade == WallSide.West ? info.TopX + 1 : info.TopX - 1;
            }
            return info;
        }

        /// <summary>
        /// Passos da cabine até cada célula andando só por saguão e corredor (-1 = não chega). Só
        /// olha o tipo das células, então serve antes de as divisas existirem: corredor com corredor
        /// e corredor com saguão são sempre abertos, e da cabine só se sai para o saguão.
        /// </summary>
        static int[] CorridorWalk(LevelData d)
        {
            int w = d.Width, h = d.Height;
            var dist = new int[w * h];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var queue = new Queue<int>();
            dist[d.SpawnX + d.SpawnZ * w] = 0;
            foreach (OfficeElevator e in d.Elevators)
                if (e.IsCab && d.Neighbor(e.X, e.Z, e.Side, out int lx, out int lz) && d.Office[lx, lz] == OfficeCell.Lobby)
                {
                    dist[lx + lz * w] = 1;
                    queue.Enqueue(lx + lz * w);
                }

            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int x = c % w, z = c / w;
                foreach (WallSide side in Sides)
                {
                    if (!d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                    OfficeCell kind = d.Office[nx, nz];
                    if (kind != OfficeCell.Corridor && kind != OfficeCell.Lobby) continue;
                    int n = nx + nz * w;
                    if (dist[n] >= 0) continue;
                    dist[n] = dist[c] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        /// <summary>
        /// Verdadeiro se a célula se vê do saguão: está numa linha reta de saguão e corredor que
        /// sai de uma célula do saguão ou de um corredor encostado nele.
        /// </summary>
        public static bool SeenFromLobby(LevelData d, int tx, int tz)
        {
            int w = d.Width, h = d.Height;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    OfficeCell kind = d.Office[x, z];
                    bool start = kind == OfficeCell.Lobby;
                    if (!start && kind == OfficeCell.Corridor)
                        foreach (WallSide side in Sides)
                            if (d.Neighbor(x, z, side, out int lx, out int lz) && d.Office[lx, lz] == OfficeCell.Lobby) start = true;
                    if (!start) continue;
                    if (x == tx && z == tz) return true;

                    foreach (WallSide side in Sides)
                    {
                        int cx = x, cz = z;
                        while (d.Neighbor(cx, cz, side, out int nx, out int nz))
                        {
                            OfficeCell next = d.Office[nx, nz];
                            if (next != OfficeCell.Corridor && next != OfficeCell.Lobby) break;
                            cx = nx;
                            cz = nz;
                            if (cx == tx && cz == tz) return true;
                        }
                    }
                }
            return false;
        }

        // Um corredor desemboca de frente para a entrada da escada (a porta seria vista de longe por ele).
        static bool FacesCorridor(LevelData d, OfficeAtrium info)
        {
            return d.Neighbor(info.HeadX, info.HeadZ, LevelData.Opposite(info.Inward), out int nx, out int nz) &&
                   d.Office[nx, nz] == OfficeCell.Corridor;
        }

        static void PlaceSpecial(Plan p, Strip[] strips, List<Rect> reserved, OfficeCell kind, int length, bool nearLobby)
        {
            List<Rect> slots = StripSlots(p, strips, length, reserved);
            if (slots.Count == 0) return;

            Rect chosen;
            if (nearLobby)
            {
                int best = int.MaxValue;
                foreach (Rect r in slots) best = Math.Min(best, LobbyDistance(p, r));
                var near = slots.FindAll(r => LobbyDistance(p, r) <= best + 1);
                chosen = near[p.Rng.Next(near.Count)];
            }
            else
            {
                chosen = slots[p.Rng.Next(slots.Count)];
            }

            AddRoom(p, kind, chosen, StripOf(strips, chosen).Facade, true);
            reserved.Add(chosen);
        }

        // Divide um retângulo em salas ao longo de um eixo; cada sala pega a largura inteira do
        // outro eixo e tem pelo menos uma célula encostada num corredor.
        static bool Partition(Plan p, Rect r, bool alongX, Func<int, int, bool> touch, WallSide window)
        {
            int start = alongX ? r.X0 : r.Z0;
            int end = alongX ? r.X1 : r.Z1;
            int depth = alongX ? r.Height : r.Width;

            bool SegmentTouches(int from, int to)
            {
                for (int i = from; i <= to; i++)
                    for (int k = 0; k < depth; k++)
                    {
                        int x = alongX ? i : r.X0 + k;
                        int z = alongX ? r.Z0 + k : i;
                        if (touch(x, z)) return true;
                    }
                return false;
            }

            int pos = start;
            while (pos <= end)
            {
                double roll = p.Rng.NextDouble();
                int length = roll < 0.15 ? 1 : roll < 0.45 ? 2 : roll < 0.8 ? 3 : 4;
                int last = Math.Min(end, pos + length - 1);

                while (!SegmentTouches(pos, last) && last < end) last++;
                if (last < end && !SegmentTouches(last + 1, end)) last = end;
                if (!SegmentTouches(pos, last)) return false;

                var room = alongX ? new Rect(pos, r.Z0, last, r.Z1) : new Rect(r.X0, pos, r.X1, last);
                int area = room.Width * room.Height;
                OfficeCell kind = area >= 6 ? OfficeCell.Classroom : OfficeCell.Small;

                double litChance = kind == OfficeCell.Classroom ? (window != WallSide.Floor ? 0.6 : 0.4) : 0.45;
                int index = AddRoom(p, kind, room, window, p.Rng.NextDouble() < litChance);
                p.Fronts.Add((index, p.Rng.NextDouble() < (kind == OfficeCell.Classroom ? 0.5 : 0.35)));
                pos = last + 1;
            }
            return true;
        }

        // ---------------------------------------------------------------- Divisas

        static void AssignEdges(Plan p)
        {
            LevelData d = p.D;
            int w = d.Width, h = d.Height;

            for (int z = 0; z <= h; z++)
                for (int x = 0; x < w; x++)
                    d.OfficeSouth[x, z] = EdgeBetween(p, z > 0 ? (x, z - 1) : ((int, int)?)null, z < h ? (x, z) : ((int, int)?)null, WallSide.North);

            for (int x = 0; x <= w; x++)
                for (int z = 0; z < h; z++)
                    d.OfficeWest[x, z] = EdgeBetween(p, x > 0 ? (x - 1, z) : ((int, int)?)null, x < w ? (x, z) : ((int, int)?)null, WallSide.East);
        }

        /// <param name="towardB">Direção de A para B.</param>
        static OfficeEdge EdgeBetween(Plan p, (int x, int z)? a, (int x, int z)? b, WallSide towardB)
        {
            LevelData d = p.D;
            if (a == null || b == null)
            {
                (int x, int z) inside = a ?? b.Value;
                OfficeCell kind = d.Office[inside.x, inside.z];
                return kind == OfficeCell.Corridor || kind == OfficeCell.Cab || kind == OfficeCell.Shaft ? OfficeEdge.Wall : OfficeEdge.Window;
            }

            (int ax, int az) = a.Value;
            (int bx, int bz) = b.Value;
            OfficeCell ka = d.Office[ax, az], kb = d.Office[bx, bz];

            if (ka == OfficeCell.Corridor && kb == OfficeCell.Corridor) return OfficeEdge.Open;
            int ra = d.OfficeRoomOf[ax, az], rb = d.OfficeRoomOf[bx, bz];
            if (ra >= 0 && ra == rb) return OfficeEdge.Open;

            if (ka == OfficeCell.Corridor || kb == OfficeCell.Corridor)
            {
                bool aIsCorridor = ka == OfficeCell.Corridor;
                OfficeCell other = aIsCorridor ? kb : ka;
                int ox = aIsCorridor ? bx : ax, oz = aIsCorridor ? bz : az;
                int cx = aIsCorridor ? ax : bx, cz = aIsCorridor ? az : bz;

                switch (other)
                {
                    case OfficeCell.Lobby:
                    case OfficeCell.Cafe:
                    case OfficeCell.Lounge:
                        return OfficeEdge.Open;
                    case OfficeCell.Atrium:
                        // O vão fica atrás de parede: do corredor só se vê a porta da escada (21/11/2026;
                        // antes era guarda-corpo, e o vão aceso se via de longe).
                        return ox == d.Atrium.TopX && oz == d.Atrium.TopZ && cx == d.Atrium.HeadX && cz == d.Atrium.HeadZ
                            ? OfficeEdge.StairTop
                            : OfficeEdge.Wall;
                    case OfficeCell.Classroom:
                    case OfficeCell.Small:
                    {
                        int room = d.OfficeRoomOf[ox, oz];
                        foreach ((int index, bool glass) in p.Fronts)
                            if (index == room) return glass ? OfficeEdge.Glass : OfficeEdge.Wall;
                        return OfficeEdge.Wall;
                    }
                    default:
                        return OfficeEdge.Wall;
                }
            }

            // Saguão e elevadores: a porta só na fileira de trás, de frente para o saguão.
            if (ka == OfficeCell.Lobby || kb == OfficeCell.Lobby)
            {
                bool aIsLobby = ka == OfficeCell.Lobby;
                OfficeCell other = aIsLobby ? kb : ka;
                int ox = aIsLobby ? bx : ax, oz = aIsLobby ? bz : az;
                // Do poço para o saguão: o contrário de A->B se o saguão é B.
                WallSide fromShaft = aIsLobby ? LevelData.Opposite(towardB) : towardB;
                if (other == OfficeCell.Shaft || other == OfficeCell.Cab)
                {
                    foreach (OfficeElevator e in d.Elevators)
                        if (e.X == ox && e.Z == oz && e.Side == fromShaft)
                            return e.IsCab ? OfficeEdge.CabDoor : OfficeEdge.ElevatorDoor;
                }
            }

            return OfficeEdge.Wall;
        }

        static void PlaceDoors(Plan p)
        {
            LevelData d = p.D;
            for (int i = 0; i < d.OfficeRooms.Count; i++)
            {
                OfficeRoom room = d.OfficeRooms[i];
                if (room.Kind != OfficeCell.Classroom && room.Kind != OfficeCell.Small) continue;

                var options = new List<(int x, int z, WallSide side)>();
                for (int z = room.Z0; z < room.Z0 + room.Height; z++)
                    for (int x = room.X0; x < room.X0 + room.Width; x++)
                        foreach (WallSide side in Sides)
                            if (d.Neighbor(x, z, side, out int nx, out int nz) && d.Office[nx, nz] == OfficeCell.Corridor)
                                options.Add((x, z, side));
                if (options.Count == 0) continue;

                int count = room.Width * room.Height >= 9 && options.Count >= 4 ? 2 : 1;
                var first = options[p.Rng.Next(options.Count)];
                SetDoor(d, first.x, first.z, first.side);

                if (count == 2)
                {
                    // A segunda porta, a mais longe da primeira.
                    var far = first;
                    int farthest = -1;
                    foreach (var o in options)
                    {
                        int dist = Math.Abs(o.x - first.x) + Math.Abs(o.z - first.z);
                        if (dist > farthest) { farthest = dist; far = o; }
                    }
                    if (farthest >= 2) SetDoor(d, far.x, far.z, far.side);
                }
            }
        }

        static void SetDoor(LevelData d, int x, int z, WallSide side)
        {
            OfficeEdge current = d.OfficeEdgeAt(x, z, side);
            OfficeEdge door = current == OfficeEdge.Glass ? OfficeEdge.GlassDoor : OfficeEdge.Door;
            SetEdge(d, x, z, side, door);
        }

        static void SetEdge(LevelData d, int x, int z, WallSide side, OfficeEdge edge)
        {
            switch (side)
            {
                case WallSide.South: d.OfficeSouth[x, z] = edge; break;
                case WallSide.North: d.OfficeSouth[x, z + 1] = edge; break;
                case WallSide.West: d.OfficeWest[x, z] = edge; break;
                default: d.OfficeWest[x + 1, z] = edge; break;
            }
        }

        /// <summary>Dá para passar por esta divisa (a pé)?</summary>
        public static bool Passable(OfficeEdge edge)
        {
            return edge == OfficeEdge.Open || edge == OfficeEdge.Door || edge == OfficeEdge.GlassDoor ||
                   edge == OfficeEdge.CabDoor || edge == OfficeEdge.StairTop;
        }

        static void FinishWalls(LevelData d)
        {
            int w = d.Width, h = d.Height;
            for (int z = 0; z <= h; z++)
                for (int x = 0; x < w; x++)
                {
                    OfficeEdge e = d.OfficeSouth[x, z];
                    d.WallSouth[x, z] = !Passable(e);
                    d.DoorSouth[x, z] = e == OfficeEdge.Door || e == OfficeEdge.GlassDoor || e == OfficeEdge.CabDoor;
                }

            for (int x = 0; x <= w; x++)
                for (int z = 0; z < h; z++)
                {
                    OfficeEdge e = d.OfficeWest[x, z];
                    d.WallWest[x, z] = !Passable(e);
                    d.DoorWest[x, z] = e == OfficeEdge.Door || e == OfficeEdge.GlassDoor || e == OfficeEdge.CabDoor;
                }

            // A cabine é a "sala inicial": as quatro paredes dela.
            int cx = d.RoomX0, cz = d.RoomZ0;
            d.RoomSouth[cx, cz] = true;
            d.RoomSouth[cx, cz + 1] = true;
            d.RoomWest[cx, cz] = true;
            d.RoomWest[cx + 1, cz] = true;
        }

        // ---------------------------------------------------------------- Armários e luzes

        // Armários amarelos na parede do corredor (parede lisa, sem porta nem vidro), em trechos:
        // quem tem armário ao lado tende a continuar.
        static void PlaceLockers(Plan p)
        {
            LevelData d = p.D;
            int w = d.Width, h = d.Height;

            for (int z = 1; z < h; z++)
            {
                bool previous = false;
                for (int x = 0; x < w; x++)
                {
                    byte side = LockerSide(d, x, z - 1, x, z, d.OfficeSouth[x, z]);
                    bool put = side != 0 && p.Rng.NextDouble() < (previous ? 0.8 : 0.4);
                    d.LockersSouth[x, z] = put ? side : (byte)0;
                    previous = put;
                }
            }

            for (int x = 1; x < w; x++)
            {
                bool previous = false;
                for (int z = 0; z < h; z++)
                {
                    byte side = LockerSide(d, x - 1, z, x, z, d.OfficeWest[x, z]);
                    bool put = side != 0 && p.Rng.NextDouble() < (previous ? 0.8 : 0.4);
                    d.LockersWest[x, z] = put ? side : (byte)0;
                    previous = put;
                }
            }
        }

        // 1 = armário do lado da célula A (sul/oeste), 2 = do lado de B, 0 = não cabe.
        static byte LockerSide(LevelData d, int ax, int az, int bx, int bz, OfficeEdge edge)
        {
            if (edge != OfficeEdge.Wall) return 0;
            bool aCorridor = d.Office[ax, az] == OfficeCell.Corridor;
            bool bCorridor = d.Office[bx, bz] == OfficeCell.Corridor;
            if (aCorridor == bCorridor) return 0;

            // Nada na frente da entrada da escada.
            int cx = aCorridor ? ax : bx, cz = aCorridor ? az : bz;
            if (cx == d.Atrium.HeadX && cz == d.Atrium.HeadZ) return 0;
            return aCorridor ? (byte)1 : (byte)2;
        }

        static void PlaceLights(Plan p)
        {
            LevelData d = p.D;
            int w = d.Width, h = d.Height;

            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    switch (d.Office[x, z])
                    {
                        case OfficeCell.Corridor:
                            d.Lights[x, z] = p.Rng.NextDouble() < 0.07 ? LightState.Flicker : LightState.On;
                            break;
                        case OfficeCell.Lobby:
                        case OfficeCell.Cafe:
                        case OfficeCell.Lounge:
                        case OfficeCell.Cab:
                            d.Lights[x, z] = LightState.On;
                            break;
                        case OfficeCell.Classroom:
                        case OfficeCell.Small:
                            d.Lights[x, z] = d.OfficeRooms[d.OfficeRoomOf[x, z]].Lit ? LightState.On : LightState.Dead;
                            break;
                        default:
                            d.Lights[x, z] = LightState.None;
                            break;
                    }
                }

            // Trechos escuros: 3 a 5 luminárias queimadas seguidas, longe da cabine.
            int[] distance = Distances(d, d.SpawnX, d.SpawnZ, corridorsOnly: true);
            var seeds = new List<int>();
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    if (d.Office[x, z] == OfficeCell.Corridor && distance[x + z * w] >= 8) seeds.Add(x + z * w);

            int stretches = Math.Min(seeds.Count, 1 + p.Rng.Next(2));
            for (int s = 0; s < stretches; s++)
            {
                int start = seeds[p.Rng.Next(seeds.Count)];
                int x = start % w, z = start / w;
                WallSide dir = Sides[p.Rng.Next(4)];
                int length = 3 + p.Rng.Next(3);
                for (int i = 0; i < length; i++)
                {
                    if (d.Office[x, z] != OfficeCell.Corridor) break;
                    d.Lights[x, z] = LightState.Dead;
                    if (!d.Neighbor(x, z, dir, out x, out z)) break;
                }
            }
        }

        // ---------------------------------------------------------------- Caminhos

        /// <summary>Distância (em passos de célula) de (x0, z0) a cada célula; -1 = não alcança.</summary>
        public static int[] Distances(LevelData d, int x0, int z0, bool corridorsOnly)
        {
            int w = d.Width, h = d.Height;
            var dist = new int[w * h];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var queue = new Queue<int>();
            dist[x0 + z0 * w] = 0;
            queue.Enqueue(x0 + z0 * w);

            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int x = c % w, z = c / w;
                foreach (WallSide side in Sides)
                {
                    if (!d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                    if (!Passable(d.OfficeEdgeAt(x, z, side))) continue;
                    OfficeCell kind = d.Office[nx, nz];
                    if (kind == OfficeCell.Atrium || kind == OfficeCell.Shaft || kind == OfficeCell.Solid) continue;
                    if (corridorsOnly && kind != OfficeCell.Corridor && kind != OfficeCell.Lobby && kind != OfficeCell.Cab) continue;
                    int n = nx + nz * w;
                    if (dist[n] >= 0) continue;
                    dist[n] = dist[c] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        // Caminho mais curto da cabine até a entrada da escada, só por corredor e saguão (a
        // cafeteria e a área de estar ficam como desvio).
        static void FindPath(LevelData d)
        {
            int w = d.Width;
            int[] dist = Distances(d, d.SpawnX, d.SpawnZ, corridorsOnly: true);
            int target = d.Atrium.HeadX + d.Atrium.HeadZ * w;
            d.PathCells = new List<int>();
            d.ExitX = d.Atrium.TopX;
            d.ExitZ = d.Atrium.TopZ;
            d.ExitSide = d.Atrium.EndSide;
            if (dist[target] < 0) return;

            var path = new List<int> { target };
            int current = target;
            while (dist[current] > 0)
            {
                int x = current % w, z = current / w;
                foreach (WallSide side in Sides)
                {
                    if (!d.Neighbor(x, z, side, out int nx, out int nz)) continue;
                    int n = nx + nz * w;
                    if (dist[n] == dist[current] - 1 && Passable(d.OfficeEdgeAt(x, z, side)))
                    {
                        current = n;
                        break;
                    }
                }
                path.Add(current);
            }
            path.Reverse();
            d.PathCells = path;
            d.SafePathCells = new List<int>(path);
        }

        // Placa de saída de emergência pendurada no corredor só na última curva do caminho antes da
        // escada, e só se ela fica a até MaxExitSignCells células da entrada (21/11/2026: antes havia
        // uma em cada curva e a cada 4 células desde a cabine, e bastava segui-las). A seta aponta
        // para a próxima célula. A porta da escada tem a placa dela, na parede (LevelBuilder).
        static void PlaceExitSigns(LevelData d)
        {
            int w = d.Width;
            List<int> path = d.PathCells;
            for (int i = path.Count - 2; i >= 1 && i >= path.Count - 1 - MaxExitSignCells; i--)
            {
                int x = path[i] % w, z = path[i] / w;
                if (d.Office[x, z] != OfficeCell.Corridor) continue;

                int px = path[i - 1] % w, pz = path[i - 1] / w;
                int nx = path[i + 1] % w, nz = path[i + 1] / w;
                bool turn = (nx - x) != (x - px) || (nz - z) != (z - pz);
                if (!turn) continue;

                d.Decorations.Add(new Decoration { Kind = DecorationKind.Arrow, X = x, Z = z, DirX = nx - x, DirZ = nz - z, Side = WallSide.Floor });
                return;
            }
        }

        // ---------------------------------------------------------------- Conferência

        /// <summary>Lista do que a planta desrespeita (vazia = passou). Regras da seção 2.3 do projeto.</summary>
        public static List<string> Validate(LevelData d)
        {
            var problems = new List<string>();
            if (d == null || !d.HasOffice) { problems.Add("não é planta de escritório"); return problems; }
            int w = d.Width, h = d.Height;

            int[] all = Distances(d, d.SpawnX, d.SpawnZ, corridorsOnly: false);
            int unreachable = 0;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    OfficeCell kind = d.Office[x, z];
                    if (kind == OfficeCell.Solid) problems.Add($"célula ({x},{z}) sem uso");
                    if (kind == OfficeCell.Atrium || kind == OfficeCell.Shaft || kind == OfficeCell.Solid) continue;
                    if (all[x + z * w] < 0) unreachable++;
                }
            if (unreachable > 0) problems.Add($"{unreachable} células com piso não se alcançam da cabine");

            for (int i = 0; i < d.OfficeRooms.Count; i++)
            {
                OfficeRoom room = d.OfficeRooms[i];
                if (room.Kind != OfficeCell.Classroom && room.Kind != OfficeCell.Small) continue;
                bool door = false;
                for (int z = room.Z0; z < room.Z0 + room.Height && !door; z++)
                    for (int x = room.X0; x < room.X0 + room.Width && !door; x++)
                        foreach (WallSide side in Sides)
                            if (d.Neighbor(x, z, side, out int nx, out int nz) && d.Office[nx, nz] == OfficeCell.Corridor && Passable(d.OfficeEdgeAt(x, z, side)))
                                door = true;
                if (!door) problems.Add($"sala {i} ({room.X0},{room.Z0}) sem porta para o corredor");
            }

            if (d.PathCells.Count == 0) problems.Add("não há caminho da cabine até a escada");
            else if (d.PathCells.Count < MinPathCells) problems.Add($"caminho curto demais até a escada ({d.PathCells.Count} células)");

            int lobbyToAtrium = int.MaxValue;
            foreach (OfficeRoom room in d.OfficeRooms)
                if (room.Kind == OfficeCell.Lobby)
                    lobbyToAtrium = Math.Abs(room.X0 + 1 - (d.Atrium.X0 + d.Atrium.Width / 2)) + Math.Abs(room.Z0 - (d.Atrium.Z0 + d.Atrium.Height / 2));
            if (lobbyToAtrium < MinAtriumDistance) problems.Add($"vão da escada perto demais do saguão ({lobbyToAtrium})");

            // A entrada da escada longe a pé (do lado oposto do andar, ou quase) e fora da vista do saguão.
            int[] walk = Distances(d, d.SpawnX, d.SpawnZ, corridorsOnly: true);
            int farthest = 0;
            for (int i = 0; i < walk.Length; i++)
                if (d.Office[i % w, i / w] == OfficeCell.Corridor) farthest = Math.Max(farthest, walk[i]);
            int toStairs = walk[d.Atrium.HeadX + d.Atrium.HeadZ * w];
            if (toStairs < MinExitShare * farthest) problems.Add($"escada perto demais da cabine a pé ({toStairs} passos; o corredor mais longe fica a {farthest})");
            if (SeenFromLobby(d, d.Atrium.HeadX, d.Atrium.HeadZ)) problems.Add("a entrada da escada se vê do saguão");

            if (d.OfficeEdgeAt(d.SpawnX, d.SpawnZ, d.Elevators.Find(e => e.IsCab).Side) != OfficeEdge.CabDoor)
                problems.Add("a cabine não tem porta para o saguão");

            return problems;
        }

        /// <summary>Resumo da planta, para comparar host e cliente e conferir que a mesma seed dá a mesma planta.</summary>
        public static int Hash(LevelData d)
        {
            unchecked
            {
                uint hash = 2166136261;
                void Mix(int v) { hash = (hash ^ (uint)v) * 16777619; }

                Mix(d.Width);
                Mix(d.Height);
                foreach (OfficeCell c in d.Office) Mix((int)c);
                foreach (OfficeEdge e in d.OfficeSouth) Mix((int)e);
                foreach (OfficeEdge e in d.OfficeWest) Mix((int)e);
                foreach (LightState l in d.Lights) Mix((int)l);
                foreach (byte b in d.LockersSouth) Mix(b);
                foreach (byte b in d.LockersWest) Mix(b);
                foreach (OfficeRoom r in d.OfficeRooms) { Mix(r.Variant); Mix(r.Lit ? 1 : 0); }
                foreach (Decoration m in d.Decorations) { Mix(m.X); Mix(m.Z); Mix(m.DirX); Mix(m.DirZ); }
                return (int)hash;
            }
        }
    }
}
