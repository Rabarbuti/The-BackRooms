using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    /// <summary>
    /// Mobília e peças do Escritório (Level 3): armários amarelos, quadros elétricos, extintores
    /// e câmeras do corredor; fileiras de mesas e cadeiras das salas de aula; salas técnicas,
    /// depósitos e laboratórios; cafeteria; área de estar; saguão; as cabines dos elevadores com
    /// as portas que abrem; o vão da escada com a saída; as placas de saída penduradas.
    /// Tudo em código, a partir da planta e de um sorteio com a seed (ver LevelBuilder.Office.cs).
    /// </summary>
    public sealed partial class LevelBuilder
    {
        // ---------------------------------------------------------------- Corredor

        bool LockerAt(int x, int z, WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return Data.LockersSouth[x, z] == 2;
                case WallSide.North: return Data.LockersSouth[x, z + 1] == 1;
                case WallSide.West: return Data.LockersWest[x, z] == 2;
                default: return Data.LockersWest[x + 1, z] == 1;
            }
        }

        static WallSide SideOf(Vector3 direction)
        {
            if (Mathf.Abs(direction.x) > 0.5f) return direction.x > 0f ? WallSide.East : WallSide.West;
            return direction.z > 0f ? WallSide.North : WallSide.South;
        }

        // Armários amarelos encostados na parede do corredor: módulos de 1,2 m com 3 x 3 portas.
        void BuildOfficeLockers()
        {
            const float face = 2f - OfficeWallThickness * 0.5f;
            for (int z = 0; z < Data.Height; z++)
                for (int x = 0; x < Data.Width; x++)
                {
                    if (Data.Office[x, z] != OfficeCell.Corridor) continue;
                    Vector3 c = OCellCenter(x, z);

                    foreach (WallSide side in new[] { WallSide.South, WallSide.North, WallSide.West, WallSide.East })
                    {
                        if (!LockerAt(x, z, side)) continue;
                        Vector3 n = SideVector(side);
                        Vector3 along = Mathf.Abs(n.x) > 0.5f ? Vector3.forward : Vector3.right;

                        // Encurta a fileira onde ela encontraria outra num canto.
                        float a0 = -1.8f, a1 = 1.8f;
                        if (LockerAt(x, z, SideOf(along))) a1 -= 0.5f;
                        if (LockerAt(x, z, SideOf(-along))) a0 += 0.5f;
                        int modules = Mathf.FloorToInt((a1 - a0) / 1.2f);
                        if (modules <= 0) continue;

                        float start = (a0 + a1) * 0.5f - modules * 0.6f;
                        Vector3 back = c + n * (face - 0.225f);
                        Vector3 front = c + n * (face - 0.45f);

                        for (int m = 0; m < modules; m++)
                        {
                            float mid = start + 0.6f + m * 1.2f;
                            OBox(office.lockerBody, back + along * mid + Vector3.up * 0.675f, Abs(along) * 1.18f + Abs(n) * 0.45f + Vector3.up * 1.35f);
                            // Uma porta por módulo fica solta, numa dobradiça, para bater (LockerBangs).
                            // A escolha é pela posição, sem sortear: não mexe no sorteio da fase.
                            int looseCol = (x * 3 + z * 5 + m + (int)side) % 3 - 1;
                            int looseRow = (x + z + m) % 2 == 0 ? 2 : 1;
                            for (int col = -1; col <= 1; col++)
                                for (int row = 0; row < 3; row++)
                                {
                                    Vector3 door = front + along * (mid + col * 0.39f) + Vector3.up * (0.275f + row * 0.43f) - n * 0.006f;
                                    if (col == looseCol && row == looseRow)
                                    {
                                        LooseLockerDoor(door, along, n, x, z);
                                        continue;
                                    }
                                    OBox(office.lockerYellow, door, Abs(along) * 0.37f + Abs(n) * 0.012f + Vector3.up * 0.41f);
                                    OBox(office.darkMetal, door + along * 0.13f + Vector3.up * 0.1f - n * 0.008f, Abs(along) * 0.03f + Abs(n) * 0.006f + Vector3.up * 0.05f);
                                }
                        }

                        AddCollider(_ok.Colliders, back + along * ((a0 + a1) * 0.5f) + Vector3.up * 0.675f,
                                    Abs(along) * (modules * 1.2f) + Abs(n) * 0.45f + Vector3.up * 1.35f);
                    }
                }
        }

        // Porta de armário solta: a dobradiça fica do lado oposto ao puxador; a folha e o puxador
        // são filhos dela. Sem colisor próprio: fechada, está dentro do colisor do armário.
        void LooseLockerDoor(Vector3 center, Vector3 along, Vector3 n, int x, int z)
        {
            if (_ok.Lockers == null) _ok.Lockers = CreateChild("PortasDeArmario").transform;

            Vector3 hinge = center - along * 0.185f;
            var root = new GameObject($"PortaDeArmario_{x}_{z}_{_ok.LockerDoors.Count}");
            root.transform.SetParent(_ok.Lockers, false);
            root.transform.localPosition = hinge;
            Quaternion turn = Quaternion.LookRotation(-n, Vector3.up);
            root.transform.localRotation = turn;
            Quaternion back = Quaternion.Inverse(turn);

            MeshRenderer leaf = CreateRenderer("Folha", _ok.LockerLeafMesh, office.lockerYellow, ShadowCastingMode.On, root.transform);
            leaf.transform.localPosition = back * (center - hinge);
            MeshRenderer handle = CreateRenderer("Puxador", _ok.LockerHandleMesh, office.darkMetal, ShadowCastingMode.Off, root.transform);
            handle.transform.localPosition = back * (center + along * 0.13f + Vector3.up * 0.1f - n * 0.008f - hinge);

            LockerDoor locker = root.AddComponent<LockerDoor>();
            locker.Init(x, z, back * (center - hinge));
            _ok.LockerDoors.Add(locker);
        }

        // Quadros elétricos, extintores e câmeras de segurança.
        void BuildOfficeCorridorDetails()
        {
            const float face = 2f - OfficeWallThickness * 0.5f;
            for (int z = 0; z < Data.Height; z++)
                for (int x = 0; x < Data.Width; x++)
                {
                    if (Data.Office[x, z] != OfficeCell.Corridor) continue;
                    Vector3 c = OCellCenter(x, z);
                    int links = 0;

                    foreach (WallSide side in new[] { WallSide.South, WallSide.North, WallSide.West, WallSide.East })
                    {
                        if (CorridorLink(x, z, side)) links++;
                        if (Data.OfficeEdgeAt(x, z, side) != OfficeEdge.Wall || LockerAt(x, z, side)) continue;

                        Vector3 n = SideVector(side);
                        Vector3 along = Mathf.Abs(n.x) > 0.5f ? Vector3.forward : Vector3.right;
                        double roll = _ok.Rng.NextDouble();
                        float offset = _ok.Rng.Next(2) == 0 ? -1f : 1f;

                        if (roll < 0.14)
                        {
                            Vector3 box = c + n * (face - 0.1f) + along * offset + Vector3.up * 1.4f;
                            OSolid(office.lockerBody, box, Abs(along) * 0.6f + Abs(n) * 0.2f + Vector3.up * 0.8f);
                            OBox(office.buttonAmber, box - n * 0.101f + along * 0.18f + Vector3.up * 0.3f, Abs(along) * 0.08f + Abs(n) * 0.004f + Vector3.up * 0.05f);
                            for (int k = 0; k < 3; k++)
                            {
                                Vector3 pipe = c + n * (face - 0.06f) + along * (offset - 0.15f + k * 0.15f);
                                OTube(office.galvanized, pipe + Vector3.up * 1.8f, pipe + Vector3.up * OfficeSlab, 0.022f, 6);
                            }
                        }
                        else if (roll < 0.22)
                        {
                            Vector3 foot = c + n * (face - 0.12f) + along * offset * 1.4f;
                            OCylinder(office.pipeRed, foot + Vector3.up * 0.32f, 0.5f, 0.085f, 12, collider: true);
                            OBox(office.darkMetal, foot + Vector3.up * 0.9f, new Vector3(0.05f, 0.08f, 0.05f));
                            OBox(office.pipeRed, c + n * (face - 0.005f) + along * offset * 1.4f + Vector3.up * 1.6f, Abs(along) * 0.22f + Abs(n) * 0.01f + Vector3.up * 0.22f);
                        }
                    }

                    // Câmera domo nos cruzamentos.
                    if (links >= 3)
                    {
                        Vector3 cam = c + new Vector3(1.2f, OfficeSlab - 0.12f, 1.2f);
                        OTube(office.blackPlastic, cam, cam + Vector3.up * 0.12f, 0.08f, 12);
                        OM(office.blackPlastic).AddDisc(cam, Vector3.down, Vector3.right, 0.08f, 12);
                    }
                }
        }

        // ---------------------------------------------------------------- Salas

        // Cadeiras de sentar e lousas das salas de aula (ClassroomSeats), em coordenadas da fase.
        List<ClassroomSeats.Seat> _officeSeats;
        List<Bounds> _officeBoards;

        void BuildOfficeRooms()
        {
            _officeSeats = new List<ClassroomSeats.Seat>();
            _officeBoards = new List<Bounds>();

            foreach (OfficeRoom room in Data.OfficeRooms)
            {
                var rng = new System.Random(room.Variant);
                switch (room.Kind)
                {
                    case OfficeCell.Classroom: Classroom(room, rng); break;
                    case OfficeCell.Small: SmallRoom(room, rng); break;
                    case OfficeCell.Cafe: Cafe(room, rng); break;
                    case OfficeCell.Lounge: Lounge(room, rng); break;
                    case OfficeCell.Lobby: LobbyProps(room, rng); break;
                }
            }

            // As carteiras e as lousas: sentar (mira + E) e o traço de marcador. Sem sorteio: não muda
            // nada do que vem depois.
            CreateChild("Carteiras").AddComponent<ClassroomSeats>().Init(_officeSeats, _officeBoards);
            _officeSeats = null;
            _officeBoards = null;
        }

        void RoomBounds(OfficeRoom room, out Vector3 min, out Vector3 max)
        {
            float inset = OfficeWallThickness * 0.5f;
            min = new Vector3(room.X0 * Cell + inset, 0f, room.Z0 * Cell + inset);
            max = new Vector3((room.X0 + room.Width) * Cell - inset, 0f, (room.Z0 + room.Height) * Cell - inset);
        }

        /// <summary>Cadeira de escritório; devolve o colisor (nulo sem colisor).</summary>
        Collider Chair(Vector3 position, Quaternion rotation, bool collider)
        {
            OTurned(office.blackPlastic, position + rotation * new Vector3(0f, 0.47f, 0f), new Vector3(0.48f, 0.07f, 0.46f), rotation, false);
            OTurned(office.blackPlastic, position + rotation * new Vector3(0f, 0.86f, -0.22f), new Vector3(0.46f, 0.5f, 0.04f), rotation, false);
            OTube(office.steel, position + Vector3.up * 0.08f, position + Vector3.up * 0.44f, 0.022f, 6);
            OTurned(office.darkMetal, position + Vector3.up * 0.05f, new Vector3(0.6f, 0.04f, 0.06f), rotation, false);
            OTurned(office.darkMetal, position + Vector3.up * 0.05f, new Vector3(0.06f, 0.04f, 0.6f), rotation, false);
            return collider ? TurnedCollider(position + Vector3.up * 0.5f, new Vector3(0.55f, 1f, 0.55f), rotation) : null;
        }

        BoxCollider TurnedCollider(Vector3 center, Vector3 size, Quaternion rotation)
        {
            var go = new GameObject("Colisor");
            go.transform.SetParent(_ok.Colliders.transform, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rotation;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        void Table(Vector3 center, Vector3 size, Material top)
        {
            OBox(top, center + Vector3.up * (size.y - 0.015f), new Vector3(size.x, 0.03f, size.z));
            float hx = size.x * 0.5f - 0.05f, hz = size.z * 0.5f - 0.05f;
            foreach (float sx in new[] { -hx, hx })
                foreach (float sz in new[] { -hz, hz })
                    OBox(office.frameWhite, center + new Vector3(sx, (size.y - 0.03f) * 0.5f, sz), new Vector3(0.04f, size.y - 0.03f, 0.04f), caps: false);
            AddCollider(_ok.Colliders, center + Vector3.up * (size.y * 0.5f), size);
        }

        // Sala de aula: fileiras de mesas brancas compridas de frente para o quadro, cadeiras de tela.
        void Classroom(OfficeRoom room, System.Random rng)
        {
            RoomBounds(room, out Vector3 min, out Vector3 max);
            bool longX = room.Width >= room.Height;
            Vector3 face = longX ? (rng.Next(2) == 0 ? Vector3.right : Vector3.left) : (rng.Next(2) == 0 ? Vector3.forward : Vector3.back);
            // A frente (quadro e tela) fica numa parede sem porta nem vidro, se houver uma.
            bool frontFree = PlainSide(room, SideOf(face));
            if (!frontFree && PlainSide(room, SideOf(-face)))
            {
                face = -face;
                frontFree = true;
            }
            Vector3 side = longX ? Vector3.forward : Vector3.right;

            float longMin = longX ? min.x : min.z, longMax = longX ? max.x : max.z;
            float shortMin = longX ? min.z : min.x, shortMax = longX ? max.z : max.x;
            float frontLong = Vector3.Dot(face, Vector3.one) > 0f ? longMax : longMin;
            float shortMid = (shortMin + shortMax) * 0.5f;
            float length = longMax - longMin, width = shortMax - shortMin;

            Vector3 Point(float fromFront, float sideways)
            {
                float l = frontLong - Vector3.Dot(face, Vector3.one) * fromFront;
                float s = shortMid + sideways;
                return longX ? new Vector3(l, 0f, s) : new Vector3(s, 0f, l);
            }

            Quaternion facing = Quaternion.LookRotation(face, Vector3.up);
            Vector3 tableSize = Abs(side) * 1.6f + Abs(face) * 0.7f + Vector3.up * 0.75f;

            for (float d = 2.6f; d < length - 2.2f; d += 1.85f)
            {
                int tables = Mathf.FloorToInt((width - 2.2f) / 1.6f);
                float first = -tables * 0.8f + 0.8f;
                for (int t = 0; t < tables; t++)
                {
                    Vector3 p = Point(d, first + t * 1.6f);
                    Table(p, tableSize, office.frameWhite);
                    foreach (float k in new[] { -0.4f, 0.4f })
                    {
                        if (rng.NextDouble() < 0.12) continue;
                        float yaw = rng.NextDouble() < 0.15 ? (float)(rng.NextDouble() * 140.0 - 70.0) : (float)(rng.NextDouble() * 16.0 - 8.0);
                        Vector3 seat = p - face * (0.7f + (float)rng.NextDouble() * 0.15f) + side * k;
                        Quaternion turn = facing * Quaternion.Euler(0f, yaw, 0f);
                        Collider chair = Chair(seat, turn, true);
                        // Carteira: senta-se nela de frente para a lousa, mesmo na cadeira torta (ClassroomSeats).
                        if (_officeSeats != null) _officeSeats.Add(new ClassroomSeats.Seat { Feet = seat, Yaw = facing.eulerAngles.y, Collider = chair });
                    }
                }
            }

            // Quadro branco, tela e projetor na parede da frente; mesa do professor.
            Vector3 wall = Point(0f, 0f);
            if (frontFree)
            {
                // A lousa tem colisor próprio: a caneta risca na face dela, não na parede 3,5 cm atrás
                // (sem ele o traço nascia atrás da lousa e ninguém via). O DrawingBoard engrossa o traço
                // que cai nela (marcador).
                Vector3 boardCenter = wall - face * 0.02f + Vector3.up * 1.5f, boardSize = Abs(side) * 3f + Abs(face) * 0.03f + Vector3.up * 1.2f;
                OSolid(office.whiteboard, boardCenter, boardSize);
                if (_officeBoards != null) _officeBoards.Add(new Bounds(boardCenter, boardSize));
                OBox(office.frameWhite, wall - face * 0.06f + Vector3.up * 0.88f, Abs(side) * 3f + Abs(face) * 0.08f + Vector3.up * 0.03f);
            }
            if (frontFree && width >= 7f)
            {
                Vector3 tv = Point(0f, 2.7f) - face * 0.04f + Vector3.up * 1.9f;
                OBox(office.blackPlastic, tv, Abs(side) * 1.25f + Abs(face) * 0.05f + Vector3.up * 0.72f);
                OBox(office.screenOff, tv - face * 0.026f, Abs(side) * 1.18f + Abs(face) * 0.004f + Vector3.up * 0.65f);
            }
            Vector3 projector = Point(Mathf.Min(3.2f, length * 0.4f), 0f) + Vector3.up * (OfficeRoomCeiling - 0.32f);
            OBox(office.frameWhite, projector, new Vector3(0.38f, 0.14f, 0.32f));
            OBox(office.steel, projector + Vector3.up * 0.25f, new Vector3(0.04f, 0.36f, 0.04f));
            Table(Point(1.3f, width * 0.25f), Abs(side) * 1.4f + Abs(face) * 0.7f + Vector3.up * 0.75f, office.frameWhite);
        }

        /// <summary>Todas as divisas daquele lado da sala são parede lisa ou janela.</summary>
        bool PlainSide(OfficeRoom room, WallSide side)
        {
            int count = side == WallSide.North || side == WallSide.South ? room.Width : room.Height;
            for (int i = 0; i < count; i++)
            {
                int x = side == WallSide.East ? room.X0 + room.Width - 1 : side == WallSide.West ? room.X0 : room.X0 + i;
                int z = side == WallSide.North ? room.Z0 + room.Height - 1 : side == WallSide.South ? room.Z0 : room.Z0 + i;
                OfficeEdge e = Data.OfficeEdgeAt(x, z, side);
                if (e != OfficeEdge.Wall && e != OfficeEdge.Window) return false;
            }
            return true;
        }

        // Lado da sala sem porta nem vidro (de preferência sem corredor): onde os móveis encostam.
        bool QuietWall(OfficeRoom room, out WallSide quiet)
        {
            quiet = WallSide.Floor;
            int best = -1;
            foreach (WallSide side in new[] { WallSide.North, WallSide.South, WallSide.East, WallSide.West })
            {
                bool ok = true;
                int score = 2;
                int count = side == WallSide.North || side == WallSide.South ? room.Width : room.Height;
                for (int i = 0; i < count && ok; i++)
                {
                    int x = side == WallSide.East ? room.X0 + room.Width - 1 : side == WallSide.West ? room.X0 : room.X0 + i;
                    int z = side == WallSide.North ? room.Z0 + room.Height - 1 : side == WallSide.South ? room.Z0 : room.Z0 + i;
                    OfficeEdge e = Data.OfficeEdgeAt(x, z, side);
                    if (e != OfficeEdge.Wall && e != OfficeEdge.Window) ok = false;
                    if (Data.Neighbor(x, z, side, out int nx, out int nz) && Data.Office[nx, nz] == OfficeCell.Corridor) score = 1;
                }
                if (ok && score > best) { best = score; quiet = side; }
            }
            return best >= 0;
        }

        // Sala pequena: técnica (racks), depósito (estantes) ou laboratório (bancada e monitores).
        void SmallRoom(OfficeRoom room, System.Random rng)
        {
            if (!QuietWall(room, out WallSide wallSide)) return;
            RoomBounds(room, out Vector3 min, out Vector3 max);
            Vector3 n = SideVector(wallSide);
            Vector3 along = Mathf.Abs(n.x) > 0.5f ? Vector3.forward : Vector3.right;
            Vector3 mid = (min + max) * 0.5f;
            float halfDepth = Mathf.Abs(Vector3.Dot(max - min, n)) * 0.5f;
            float span = Mathf.Abs(Vector3.Dot(max - min, along)) - 1.2f;
            Vector3 wall = mid + n * halfDepth;

            switch (room.Variant % 3)
            {
                case 0:
                {
                    int racks = Mathf.Max(1, Mathf.FloorToInt(span / 0.65f));
                    for (int i = 0; i < racks; i++)
                    {
                        Vector3 p = wall - n * 0.55f + along * (-racks * 0.325f + 0.325f + i * 0.65f);
                        OSolid(office.blackPlastic, p + Vector3.up * 1f, Abs(along) * 0.6f + Abs(n) * 1f + Vector3.up * 2f);
                        for (int led = 0; led < 6; led++)
                        {
                            if (rng.NextDouble() < 0.35) continue;
                            Material m = rng.NextDouble() < 0.8 ? office.ledGreen : office.buttonAmber;
                            OBox(m, p - n * 0.502f + along * ((float)rng.NextDouble() * 0.4f - 0.2f) + Vector3.up * (0.4f + led * 0.25f),
                                 Abs(along) * 0.02f + Abs(n) * 0.004f + Vector3.up * 0.015f);
                        }
                    }
                    break;
                }

                case 1:
                {
                    int shelves = Mathf.Max(1, Mathf.FloorToInt(span / 1.05f));
                    for (int i = 0; i < shelves; i++)
                    {
                        Vector3 p = wall - n * 0.25f + along * (-shelves * 0.525f + 0.525f + i * 1.05f);
                        foreach (float s in new[] { -0.48f, 0.48f })
                            OBox(office.steel, p + along * s + Vector3.up * 1f, Abs(along) * 0.03f + Abs(n) * 0.45f + Vector3.up * 2f);
                        for (int level = 0; level < 4; level++)
                        {
                            float y = 0.15f + level * 0.55f;
                            OBox(office.steel, p + Vector3.up * y, Abs(along) * 0.96f + Abs(n) * 0.45f + Vector3.up * 0.025f);
                            int boxes = rng.Next(3);
                            for (int b = 0; b < boxes; b++)
                                OBox(office.osb, p + along * (-0.3f + b * 0.32f) + Vector3.up * (y + 0.16f), Abs(along) * 0.28f + Abs(n) * 0.36f + Vector3.up * 0.28f);
                        }
                        AddCollider(_ok.Colliders, p + Vector3.up * 1f, Abs(along) * 1f + Abs(n) * 0.45f + Vector3.up * 2f);
                    }
                    break;
                }

                default:
                {
                    Vector3 bench = wall - n * 0.4f + Vector3.zero;
                    Table(bench, Abs(along) * span + Abs(n) * 0.75f + Vector3.up * 0.78f, office.frameWhite);
                    int screens = Mathf.Max(1, Mathf.FloorToInt(span / 1.3f));
                    Quaternion facing = Quaternion.LookRotation(n, Vector3.up);
                    for (int i = 0; i < screens; i++)
                    {
                        float a = -screens * 0.65f + 0.65f + i * 1.3f;
                        OBox(office.blackPlastic, bench + along * a + n * 0.2f + Vector3.up * 1.05f, Abs(along) * 0.56f + Abs(n) * 0.04f + Vector3.up * 0.34f);
                        if (rng.NextDouble() < 0.7) Chair(bench + along * a - n * 0.75f, facing * Quaternion.Euler(0f, (float)(rng.NextDouble() * 40.0 - 20.0), 0f), true);
                    }
                    break;
                }
            }
        }

        // Cafeteria: balcão de OSB com banquetas, geladeiras e o painel de senha atrás; mesas redondas.
        void Cafe(OfficeRoom room, System.Random rng)
        {
            RoomBounds(room, out Vector3 min, out Vector3 max);
            Vector3 facade = SideVector(room.WindowSide);
            Vector3 along = Mathf.Abs(facade.x) > 0.5f ? Vector3.forward : Vector3.right;
            Vector3 end = (room.Variant & 1) == 0 ? along : -along;
            Vector3 mid = (min + max) * 0.5f;
            float halfAlong = Mathf.Abs(Vector3.Dot(max - min, along)) * 0.5f;
            float depth = Mathf.Abs(Vector3.Dot(max - min, facade));

            Vector3 endWall = mid + end * halfAlong;
            float counterLength = depth - 2.6f;
            Vector3 counter = endWall - end * 1.5f + facade * 0.1f;
            OSolid(office.osb, counter + Vector3.up * 0.52f, Abs(facade) * counterLength + Abs(end) * 0.7f + Vector3.up * 1.04f);
            OBox(office.frameWhite, counter + Vector3.up * 1.06f, Abs(facade) * (counterLength + 0.06f) + Abs(end) * 0.8f + Vector3.up * 0.04f);

            Quaternion toCounter = Quaternion.LookRotation(end, Vector3.up);
            for (float s = -counterLength * 0.5f + 0.4f; s <= counterLength * 0.5f - 0.3f; s += 0.7f)
            {
                Vector3 stool = counter - end * 0.85f + facade * s;
                OTube(office.steel, stool + Vector3.up * 0.05f, stool + Vector3.up * 0.72f, 0.025f, 6);
                OCylinder(office.frameWhite, stool + Vector3.up * 0.72f, 0.05f, 0.18f, 14);
                OCylinder(office.steel, stool, 0.02f, 0.2f, 14);
                AddCollider(_ok.Colliders, stool + Vector3.up * 0.38f, new Vector3(0.38f, 0.76f, 0.38f));
            }

            // Atrás do balcão: geladeiras e o painel de senha em LED.
            int fridges = Mathf.Clamp(Mathf.FloorToInt(counterLength / 0.8f), 1, 4);
            for (int i = 0; i < fridges; i++)
            {
                Vector3 f = endWall - end * 0.38f + facade * (-fridges * 0.4f + 0.4f + i * 0.8f);
                OSolid(office.steel, f + Vector3.up * 1f, Abs(facade) * 0.74f + Abs(end) * 0.7f + Vector3.up * 2f);
                OBox(office.fridgeGlass, f - end * 0.352f + Vector3.up * 1.05f, Abs(facade) * 0.62f + Abs(end) * 0.004f + Vector3.up * 1.6f);
            }
            OBox(office.blackPlastic, endWall - end * 0.04f + Vector3.up * 2.55f + facade * 1.2f, Abs(facade) * 0.5f + Abs(end) * 0.06f + Vector3.up * 0.2f);
            OBox(office.ledRed, endWall - end * 0.072f + Vector3.up * 2.55f + facade * 1.2f, Abs(facade) * 0.42f + Abs(end) * 0.004f + Vector3.up * 0.13f);

            // Mesas redondas brancas com cadeiras de madeira, no resto do salão.
            for (int z = room.Z0; z < room.Z0 + room.Height; z++)
                for (int x = room.X0; x < room.X0 + room.Width; x++)
                {
                    Vector3 c = OCellCenter(x, z);
                    if (Mathf.Abs(Vector3.Dot(c - endWall, end)) < 3.2f) continue;
                    Vector3 table = c + new Vector3((float)rng.NextDouble() * 0.6f - 0.3f, 0f, (float)rng.NextDouble() * 0.6f - 0.3f);
                    OCylinder(office.frameWhite, table + Vector3.up * 0.72f, 0.03f, 0.42f, 20);
                    OTube(office.frameWhite, table, table + Vector3.up * 0.72f, 0.03f, 8);
                    OCylinder(office.frameWhite, table, 0.02f, 0.25f, 16);
                    AddCollider(_ok.Colliders, table + Vector3.up * 0.375f, new Vector3(0.84f, 0.75f, 0.84f));
                    float turn = (float)rng.NextDouble() * 90f;
                    for (int k = 0; k < 4; k++)
                    {
                        if (rng.NextDouble() < 0.2) continue;
                        Quaternion r = Quaternion.Euler(0f, turn + k * 90f + (float)(rng.NextDouble() * 20.0 - 10.0), 0f);
                        Vector3 seat = table - r * Vector3.forward * 0.72f;
                        OTurned(office.osb, seat + Vector3.up * 0.45f, new Vector3(0.48f, 0.05f, 0.46f), r, false);
                        OTurned(office.osb, seat + r * new Vector3(0f, 0.72f, -0.22f), new Vector3(0.5f, 0.4f, 0.05f), r, false);
                        OTurned(office.steel, seat + Vector3.up * 0.22f, new Vector3(0.42f, 0.44f, 0.4f), r, false, caps: false);
                        TurnedCollider(seat + Vector3.up * 0.47f, new Vector3(0.5f, 0.94f, 0.5f), r);
                    }
                }
        }

        // Área de estar: sofá curvo comprido, pufes grandes e plantas.
        void Lounge(OfficeRoom room, System.Random rng)
        {
            RoomBounds(room, out Vector3 min, out Vector3 max);
            Vector3 mid = (min + max) * 0.5f;
            Vector3 facade = SideVector(room.WindowSide);
            float radius = 2.1f;
            Vector3 center = mid - facade * 0.6f;
            float baseAngle = Mathf.Atan2(facade.x, facade.z) * Mathf.Rad2Deg;

            for (int i = 0; i < 9; i++)
            {
                float angle = baseAngle - 75f + i * (150f / 8f);
                Quaternion r = Quaternion.Euler(0f, angle, 0f);
                Vector3 dir = r * Vector3.forward;
                Quaternion facing = Quaternion.LookRotation(-dir, Vector3.up);
                OTurned(office.fabric, center + dir * radius + Vector3.up * 0.22f, new Vector3(0.66f, 0.44f, 0.8f), facing, true);
                OTurned(office.fabric, center + dir * (radius + 0.45f) + Vector3.up * 0.62f, new Vector3(0.7f, 0.5f, 0.2f), facing, true);
            }

            int pufes = 2 + rng.Next(2);
            for (int i = 0; i < pufes; i++)
            {
                float angle = baseAngle + (i - (pufes - 1) * 0.5f) * 55f;
                Vector3 p = center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (0.7f + (float)rng.NextDouble() * 0.3f) - facade * 0.2f;
                OCylinder(office.fabric, p, 0.42f, 0.55f, 20, collider: true);
            }

            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 along = Mathf.Abs(facade.x) > 0.5f ? Vector3.forward : Vector3.right;
                float halfAlong = Mathf.Abs(Vector3.Dot(max - min, along)) * 0.5f - 0.45f;
                float halfDeep = Mathf.Abs(Vector3.Dot(max - min, facade)) * 0.5f - 0.45f;
                Plant(mid + along * (s * halfAlong) + facade * halfDeep, rng);
            }
        }

        void Plant(Vector3 foot, System.Random rng)
        {
            OSolid(office.concrete, foot + Vector3.up * 0.3f, new Vector3(0.45f, 0.6f, 0.45f));
            MeshBuilder leaves = OM(office.plant);
            for (int k = 0; k < 4; k++)
            {
                float angle = k * 45f + (float)rng.NextDouble() * 20f;
                Vector3 d = Quaternion.Euler(0f, angle, 0f) * Vector3.right * 0.45f;
                Vector3 lo = foot + Vector3.up * 0.6f, hi = foot + Vector3.up * 1.6f;
                Vector3 n = Vector3.Cross(d, Vector3.up).normalized;
                leaves.AddQuad(lo - d * 0.3f, hi - d, hi + d, lo + d * 0.3f, n);
                leaves.AddQuad(lo - d * 0.3f, hi - d, hi + d, lo + d * 0.3f, -n);
            }
        }

        // Saguão: duas mesas com cadeiras de arame, lixeiras, totem e a placa de direções pendurada.
        void LobbyProps(OfficeRoom room, System.Random rng)
        {
            OfficeElevator cab = Data.Elevators.Find(e => e.IsCab);
            Vector3 toLobby = SideVector(cab.Side);
            Vector3 along = Mathf.Abs(toLobby.x) > 0.5f ? Vector3.forward : Vector3.right;
            Vector3 cabCenter = OCellCenter(cab.X, cab.Z);
            // A fileira do saguão de frente para o corredor: a duas células da cabine.
            Vector3 frontRow = cabCenter + toLobby * (Cell * 2f);

            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 table = frontRow + along * (s * Cell) + toLobby * 0.3f;
                // O caminho até a escada às vezes cruza o saguão de lado: mesa nenhuma nele.
                int tx = Mathf.FloorToInt(table.x / Cell), tz = Mathf.FloorToInt(table.z / Cell);
                if (Data.PathCells.Contains(tx + tz * Data.Width)) continue;
                Table(table, new Vector3(0.8f, 0.74f, 0.8f), office.frameWhite);
                for (int k = 0; k < 4; k++)
                {
                    if (rng.NextDouble() < 0.25) continue;
                    Quaternion r = Quaternion.Euler(0f, k * 90f + (float)(rng.NextDouble() * 24.0 - 12.0), 0f);
                    Vector3 seat = table - r * Vector3.forward * 0.7f;
                    OTurned(office.fabric, seat + Vector3.up * 0.45f, new Vector3(0.5f, 0.06f, 0.48f), r, false);
                    OTurned(office.darkMetal, seat + r * new Vector3(0f, 0.7f, -0.24f), new Vector3(0.5f, 0.45f, 0.02f), r, false);
                    OTurned(office.darkMetal, seat + Vector3.up * 0.21f, new Vector3(0.46f, 0.42f, 0.44f), r, false, caps: false);
                    TurnedCollider(seat + Vector3.up * 0.47f, new Vector3(0.52f, 0.94f, 0.52f), r);
                }
            }

            // Lixeiras e totem junto da parede dos elevadores, no canto.
            Vector3 corner = cabCenter + toLobby * (Cell * 0.5f + 0.45f) + along * (Cell * 1.5f - 0.5f);
            OSolid(office.darkMetal, corner + Vector3.up * 0.45f, Abs(along) * 0.38f + Abs(toLobby) * 0.38f + Vector3.up * 0.9f);
            OSolid(office.lockerBody, corner - along * 0.42f + Vector3.up * 0.45f, Abs(along) * 0.38f + Abs(toLobby) * 0.38f + Vector3.up * 0.9f);
            Vector3 totem = cabCenter + toLobby * (Cell * 0.5f + 0.2f) - along * (Cell * 1.5f - 0.6f);
            OSolid(office.blackPlastic, totem + Vector3.up * 1.05f, Abs(along) * 0.6f + Abs(toLobby) * 0.12f + Vector3.up * 2.1f);
            for (int i = 0; i < 6; i++)
                OBox(office.frameWhite, totem + toLobby * 0.062f + Vector3.up * (1.95f - i * 0.22f) - along * 0.05f,
                     Abs(along) * (0.3f + (i % 3) * 0.06f) + Abs(toLobby) * 0.004f + Vector3.up * 0.03f);

            // Placa de direções pendurada, sem texto: faixas brancas e setas abstratas.
            Vector3 sign = frontRow + toLobby * 1.2f + Vector3.up * 2.95f;
            OBox(office.blackPlastic, sign, Abs(along) * 1.8f + Abs(toLobby) * 0.06f + Vector3.up * 0.42f);
            foreach (float face in new[] { -1f, 1f })
                for (int i = 0; i < 3; i++)
                    OBox(office.frameWhite, sign + toLobby * (face * 0.031f) + along * (-0.5f + i * 0.5f) + Vector3.up * (i == 1 ? 0.06f : -0.04f),
                         Abs(along) * 0.34f + Abs(toLobby) * 0.003f + Vector3.up * 0.04f);
            foreach (float s in new[] { -0.7f, 0.7f })
                OBox(office.steel, sign + along * s + Vector3.up * ((OfficeSlatsBottom - 2.95f) * 0.5f + 0.1f), new Vector3(0.008f, OfficeSlatsBottom - 3.1f, 0.008f), caps: false);
        }

        // ---------------------------------------------------------------- Elevadores

        void BuildOfficeElevators()
        {
            foreach (OfficeElevator e in Data.Elevators)
            {
                Vector3 c = OCellCenter(e.X, e.Z);
                Vector3 toLobby = SideVector(e.Side);
                Vector3 lateral = Mathf.Abs(toLobby.x) > 0.5f ? Vector3.forward : Vector3.right;
                const float h = OfficeCabCeiling;
                float wall = CabHalf + 0.025f;

                // Cabine de inox: fundo, laterais, frente com o vão da porta e teto.
                OSolid(office.steel, c - toLobby * wall + Vector3.up * (h * 0.5f), Abs(lateral) * (CabHalf * 2f + 0.1f) + Abs(toLobby) * 0.05f + Vector3.up * h);
                foreach (float s in new[] { -1f, 1f })
                {
                    OSolid(office.steel, c + lateral * (s * wall) + Vector3.up * (h * 0.5f), Abs(lateral) * 0.05f + Abs(toLobby) * (CabHalf * 2f + 0.1f) + Vector3.up * h);
                    float a0 = s * (ElevatorHalf + 0.05f), a1 = s * (CabHalf + 0.05f);
                    OSolid(office.steel, c + toLobby * wall + lateral * ((a0 + a1) * 0.5f) + Vector3.up * (h * 0.5f),
                           Abs(lateral) * Mathf.Abs(a1 - a0) + Abs(toLobby) * 0.05f + Vector3.up * h);
                }
                OSolid(office.steel, c + toLobby * wall + Vector3.up * ((ElevatorTop + h) * 0.5f), Abs(lateral) * (ElevatorHalf * 2f + 0.1f) + Abs(toLobby) * 0.05f + Vector3.up * (h - ElevatorTop));
                OSolid(office.steel, c + Vector3.up * (h + 0.025f), Abs(lateral) * (CabHalf * 2f + 0.1f) + Abs(toLobby) * (CabHalf * 2f + 0.1f) + Vector3.up * 0.05f);

                // Corrimão nas três paredes e o painel de botões ao lado da porta.
                Vector3 rail = Vector3.up * 0.92f;
                OTube(office.steel, c - toLobby * (CabHalf - 0.06f) - lateral * 1.0f + rail, c - toLobby * (CabHalf - 0.06f) + lateral * 1.0f + rail, 0.02f, 8);
                foreach (float s in new[] { -1f, 1f })
                    OTube(office.steel, c + lateral * (s * (CabHalf - 0.06f)) - toLobby * 1.0f + rail, c + lateral * (s * (CabHalf - 0.06f)) + toLobby * 0.8f + rail, 0.02f, 8);
                Vector3 panel = c + toLobby * (CabHalf - 0.012f) + lateral * 0.9f + Vector3.up * 1.2f;
                OBox(office.steel, panel, Abs(lateral) * 0.24f + Abs(toLobby) * 0.02f + Vector3.up * 0.6f);
                // Os dois de baixo (abrir e fechar) se apertam: saem depois da porta, como objetos.
                for (int i = 0; i < 4; i++)
                    OBox(i == 2 ? office.buttonAmber : office.darkMetal, panel - toLobby * 0.012f + Vector3.up * (0.2f - i * 0.075f),
                         Abs(lateral) * 0.035f + Abs(toLobby) * 0.006f + Vector3.up * 0.035f);

                MeshRenderer spots = CreateRenderer($"SpotsDaCabine_{e.X}_{e.Z}", _ok.SpotsMesh, e.IsCab ? office.tubeOn : office.tubeOff, ShadowCastingMode.Off, _ok.Moving);
                spots.transform.localPosition = c + Vector3.up * (h - 0.003f);
                // Luz pontual curta: acende as paredes de inox da cabine (o spot só pegava o chão)
                // e se apaga antes de chegar ao piso do saguão (a 3,2 m dela), sem precisar de sombra.
                HDAdditionalLightData light = CreateLightObject("Luz", spots.transform, LightType.Point, new Vector3(0f, -0.15f, 0f), out Light cabLight);
                cabLight.intensity = officeCabCandela;
                cabLight.range = 2.5f;
                cabLight.colorTemperature = 4500f;
                cabLight.shadows = LightShadows.None;
                light.affectsVolumetric = false;

                if (e.IsCab) spots.gameObject.AddComponent<CeilingLight>().Init(LightState.On, new[] { light }, spots, office.tubeOn, office.tubeOff);
                else light.lightDimmer = 0f;

                // Porta: duas folhas que correm para dentro da parede.
                var rig = new GameObject($"Elevador_{e.X}_{e.Z}");
                rig.transform.SetParent(_ok.Moving, false);
                rig.transform.localPosition = c + toLobby * (Cell * 0.5f);
                Transform left = Leaf(rig.transform, -lateral * 0.28f, toLobby);
                Transform right = Leaf(rig.transform, lateral * 0.28f, toLobby);

                var doors = rig.AddComponent<ElevatorDoors>();
                doors.Init(left, right, lateral * 0.55f, e.IsCab, e.IsCab ? null : light, e.IsCab ? null : spots, office.tubeOn, office.tubeOff);
                _ok.Elevators.Add(doors);
                _ok.ElevatorLobby.Add(transform.TransformDirection(toLobby));

                // Botões: abrir e fechar no painel de dentro; chamar no espelho do saguão.
                Vector3 inner = panel - toLobby * 0.018f;
                ElevatorButton(doors, ElevatorButtonKind.Open, inner + Vector3.up * (0.2f - 4 * 0.075f), -toLobby, e);
                ElevatorButton(doors, ElevatorButtonKind.Close, inner + Vector3.up * (0.2f - 5 * 0.075f), -toLobby, e);
                Vector3 call = c + toLobby * (Cell * 0.5f + OfficeWallThickness * 0.5f + 0.021f) + lateral * 0.98f + Vector3.up * 1.12f;
                ElevatorButton(doors, ElevatorButtonKind.Call, call, toLobby, e);
            }
        }

        // Botão que se aperta (mira + E): malha pequena com um colisor um pouco maior, para a mira.
        void ElevatorButton(ElevatorDoors doors, ElevatorButtonKind kind, Vector3 position, Vector3 facing, OfficeElevator e)
        {
            MeshRenderer button = CreateRenderer($"Botao{kind}_{e.X}_{e.Z}", _ok.ButtonMesh, office.darkMetal, ShadowCastingMode.Off, _ok.Moving);
            button.transform.localPosition = position;
            button.transform.localRotation = Quaternion.LookRotation(facing, Vector3.up);
            button.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.07f, 0.07f, 0.03f);
            var control = button.gameObject.AddComponent<ElevatorButton>();
            control.Init(doors, kind, button, office.darkMetal, office.buttonAmber);
            _ok.ElevatorButtons.Add(control);
        }

        Transform Leaf(Transform rig, Vector3 offset, Vector3 toLobby)
        {
            MeshRenderer leaf = CreateRenderer("Folha", _ok.LeafMesh, office.steel, ShadowCastingMode.On, rig);
            leaf.transform.localPosition = offset + Vector3.up * (ElevatorTop * 0.5f);
            leaf.transform.localRotation = Quaternion.LookRotation(toLobby, Vector3.up);
            leaf.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.56f, ElevatorTop, 0.04f);
            return leaf.transform;
        }

        // ---------------------------------------------------------------- Vão da escada e saída

        const float StairWidth = 1.4f;
        const float StairRiser = 0.17f;
        const float StairTread = 0.28f;
        const int StairSteps = 10;
        const float StairTopLanding = 1f;
        const float AtriumBottom = -14f;
        const float AtriumTop = 16f;
        const float FloorPitch = 4.2f;

        Vector3 _atTop, _atEnd, _atIn;

        // Ponto no referencial do vão: u a partir da face da parede da ponta (para dentro do vão),
        // v a partir da linha do corredor (para a fachada), y a partir do piso.
        Vector3 AtPoint(float u, float v, float y)
        {
            return _atTop + _atEnd * (Cell * 0.5f - OfficeWallThickness * 0.5f) - _atEnd * u - _atIn * (Cell * 0.5f) + _atIn * v + Vector3.up * y;
        }

        void AtBox(Material m, float u0, float u1, float v0, float v1, float y0, float y1, bool collider, bool caps = true)
        {
            if (u1 < u0) (u0, u1) = (u1, u0);
            if (v1 < v0) (v0, v1) = (v1, v0);
            Vector3 center = AtPoint((u0 + u1) * 0.5f, (v0 + v1) * 0.5f, (y0 + y1) * 0.5f);
            Vector3 size = Abs(_atEnd) * (u1 - u0) + Abs(_atIn) * (v1 - v0) + Vector3.up * (y1 - y0);
            if (m != null) OM(m).AddBox(center, size, caps);
            if (collider) AddCollider(_ok.Colliders, center, size);
        }

        // Guarda-corpo reto entre dois pontos do vão (no mesmo nível ou em rampa, na escada).
        void AtRailing(float u0, float v0, float y0, float u1, float v1, float y1)
        {
            Vector3 a = AtPoint(u0, v0, y0), b = AtPoint(u1, v1, y1);
            Vector3 flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float length = flat.magnitude;
            if (length < 0.05f) return;

            OTube(office.steel, a + Vector3.up * 1.1f, b + Vector3.up * 1.1f, 0.025f, 8);
            foreach (float y in new[] { 0.4f, 0.62f, 0.84f })
                OTube(office.steel, a + Vector3.up * y, b + Vector3.up * y, 0.012f, 6);
            int posts = Mathf.Max(1, Mathf.CeilToInt(length));
            for (int i = 0; i <= posts; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)posts);
                OBox(office.steel, p + Vector3.up * 0.55f, new Vector3(0.04f, 1.1f, 0.04f));
            }

            // Colisor em degraus de meio metro, para acompanhar a rampa.
            int pieces = Mathf.Max(1, Mathf.CeilToInt(length / 0.5f));
            Quaternion r = Quaternion.LookRotation(flat.normalized, Vector3.up);
            for (int i = 0; i < pieces; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, (i + 0.5f) / pieces);
                TurnedCollider(p + Vector3.up * 0.65f, new Vector3(0.08f, 1.3f, length / pieces + 0.02f), r);
            }
        }

        void BuildOfficeAtrium()
        {
            OfficeAtrium at = Data.Atrium;
            _atTop = OCellCenter(at.TopX, at.TopZ);
            _atEnd = SideVector(at.EndSide);
            _atIn = SideVector(at.Inward);
            const float T = OfficeWallThickness;

            bool endAlongX = Mathf.Abs(_atEnd.x) > 0.5f;
            float lenU = (endAlongX ? at.Width : at.Height) * Cell - T;
            float lenV = (endAlongX ? at.Height : at.Width) * Cell;

            const float vTop = StairTopLanding;
            float vBottom = vTop + StairSteps * StairTread;
            float landingEnd = vBottom + 1.6f;
            float yBottom = -StairSteps * StairRiser;

            // Patamar de cima, degraus de granito (fita preta e ponteiras amarelas) e patamar da saída.
            AtBox(office.granite, 0f, StairWidth, 0f, vTop, -0.3f, 0f, true);
            for (int i = 0; i < StairSteps; i++)
            {
                float v0 = vTop + i * StairTread, v1 = v0 + StairTread, top = -(i + 1) * StairRiser;
                AtBox(office.granite, 0f, StairWidth, v0, v1, top - 0.22f, top, true);
                AtBox(office.blackPlastic, 0.14f, StairWidth - 0.14f, v1 - 0.1f, v1 - 0.04f, top, top + 0.004f, false);
                AtBox(office.lockerYellow, 0.02f, 0.13f, v1 - 0.1f, v1 - 0.02f, top, top + 0.006f, false);
                AtBox(office.lockerYellow, StairWidth - 0.13f, StairWidth - 0.02f, v1 - 0.1f, v1 - 0.02f, top, top + 0.006f, false);
            }
            AtBox(office.granite, 0f, 3f, vBottom, landingEnd, yBottom - 0.3f, yBottom, true);

            // Viga lateral grafite (decisão 1 do projeto: no lugar do verde-água).
            MeshBuilder beam = OM(office.darkMetal);
            foreach (float u in new[] { StairWidth, StairWidth + 0.07f })
            {
                Vector3 n = u > StairWidth ? -_atEnd : _atEnd;
                beam.AddQuad(AtPoint(u, vTop, 0.06f), AtPoint(u, vBottom, yBottom + 0.06f), AtPoint(u, vBottom, yBottom - 0.42f), AtPoint(u, vTop, -0.42f), n);
            }
            beam.AddQuad(AtPoint(StairWidth, vTop, -0.42f), AtPoint(StairWidth + 0.07f, vTop, -0.42f),
                         AtPoint(StairWidth + 0.07f, vBottom, yBottom - 0.42f), AtPoint(StairWidth, vBottom, yBottom - 0.42f), Vector3.down);

            // Guarda-corpos: patamar de cima (do lado do vão e na linha do corredor), escada e patamar da saída.
            // Na linha do corredor, só a célula da escada: nas outras o guarda-corpo é o da divisa.
            float topCellEnd = Cell - T * 0.5f;
            AtRailing(StairWidth + 0.05f, 0f, 0f, topCellEnd - 0.02f, 0f, 0f);
            AtRailing(StairWidth + 0.04f, 0.02f, 0f, StairWidth + 0.04f, vTop, 0f);
            AtRailing(StairWidth + 0.04f, vTop, 0f, StairWidth + 0.04f, vBottom, yBottom);
            AtRailing(3f, vBottom, yBottom, 3f, landingEnd, yBottom);
            AtRailing(0.02f, landingEnd, yBottom, 3f, landingEnd, yBottom);
            AtBox(office.concrete, StairWidth, topCellEnd, -0.08f, 0.08f, -0.35f, 0f, false);

            // O lance que continua para baixo, fechado por uma grade.
            for (int i = 0; i < StairSteps; i++)
            {
                float v1 = vBottom - i * StairTread, v0 = v1 - StairTread, top = yBottom - (i + 1) * StairRiser;
                AtBox(office.granite, 1.62f, 3f, v0, v1, top - 0.22f, top, false);
            }
            AtBox(office.darkMetal, 1.6f, 3f, vBottom - 0.04f, vBottom, yBottom, yBottom + 0.05f, false);
            AtBox(office.darkMetal, 1.6f, 3f, vBottom - 0.04f, vBottom, yBottom + 1.95f, yBottom + 2f, false);
            for (float u = 1.65f; u < 3f; u += 0.12f)
                AtBox(office.darkMetal, u, u + 0.02f, vBottom - 0.035f, vBottom - 0.005f, yBottom, yBottom + 2f, false);
            AtBox(null, 1.6f, 3f, vBottom - 0.04f, vBottom, yBottom, yBottom + 2f, true);

            // Paredes do vão abaixo do piso e acima da laje (o trecho entre 0 e a laje é das divisas).
            foreach (float u in new[] { -T, lenU })
            {
                AtBox(office.concrete, u, u + T, 0f, lenV, AtriumBottom, 0f, true);
                AtBox(office.wall, u, u + T, 0f, lenV, OfficeSlab, AtriumTop, false);
            }
            AtBox(office.concrete, 0f, lenU, lenV - T * 0.5f, lenV + T * 0.5f, AtriumBottom, 0f, false);
            AtBox(office.concrete, 0f, lenU, -T, 0f, AtriumBottom, -0.35f, false);
            AtBox(office.wall, 0f, lenU, -T, 0f, OfficeSlab, AtriumTop, false);

            // Fachada de vidro acima da laje, com uma faixa de concreto em cada andar.
            AtBox(office.windowDay, 0f, lenU, lenV - 0.01f, lenV + 0.01f, OfficeSlab, AtriumTop, false);
            for (float u = 0f; u <= lenU + 0.01f; u += 2f)
                AtBox(office.frameWhite, u - 0.03f, u + 0.03f, lenV - 0.06f, lenV - 0.01f, OfficeSlab, AtriumTop, false);
            for (int k = 1; k <= 3; k++)
                AtBox(office.concrete, 0f, lenU, lenV - 0.08f, lenV, k * FloorPitch - 0.35f, k * FloorPitch + 0.5f, false);

            // Os outros andares, vistos pelo vão: borda da laje, guarda-corpo e o escuro (embaixo)
            // ou a fileira de luminárias (em cima). Só cascas, sem nada atrás.
            for (int k = -3; k <= 3; k++)
            {
                if (k == 0) continue;
                float y = k * FloorPitch;
                AtBox(office.concrete, 0f, lenU, -0.02f, 0.25f, y - 0.35f, y, false);
                AtBox(k < 0 ? office.voidBlack : office.slab, 0f, lenU, -0.005f, 0f, y, y + OfficeSlab - 0.4f, false);
                foreach (float ry in new[] { 0.62f, 1.1f })
                    OTube(office.steel, AtPoint(0f, 0.2f, y + ry), AtPoint(lenU, 0.2f, y + ry), k < 0 ? 0.012f : 0.02f, 6);
                if (k > 0)
                    for (float u = 1.2f; u < lenU - 0.5f; u += Cell)
                        AtBox(office.tubeOn, u - 0.04f, u + 0.04f, -0.6f, -0.52f, y + 2.78f, y + 2.86f, false);
            }

            // Fundo escuro e a queda: quem passa do guarda-corpo volta ao início.
            AtBox(office.voidBlack, 0f, lenU, 0f, lenV, AtriumBottom - 0.1f, AtriumBottom, false);
            var fall = new GameObject("Queda");
            fall.transform.SetParent(transform, false);
            fall.transform.localPosition = AtPoint(lenU * 0.5f, lenV * 0.5f, -9.5f);
            var trigger = fall.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = Abs(_atEnd) * lenU + Abs(_atIn) * lenV + Vector3.up * 9f;
            fall.AddComponent<PitFall>();

            // Claraboia no alto e a luz fria que desce por ela.
            AtBox(office.skylight, 0f, lenU, 0f, lenV, AtriumTop, AtriumTop + 0.05f, false);
            var sky = new GameObject("LuzDoVao").transform;
            sky.SetParent(_ok.Fixtures, false);
            foreach (float f in new[] { 0.25f, 0.7f })
            {
                Vector3 p = AtPoint(lenU * f, lenV * 0.5f, AtriumTop - 0.6f);
                OfficeSpot(sky, p, Quaternion.Euler(90f, 0f, 0f), officeAtriumCandela, 34f, 60f, 6800f, false);
            }

            BuildOfficeExit(yBottom, (vBottom + landingEnd) * 0.5f);

            OfficeExitRoute.Clear();
            OfficeExitRoute.Add(transform.TransformPoint(AtPoint(0.7f, 0.4f, 0f)));
            OfficeExitRoute.Add(transform.TransformPoint(AtPoint(0.7f, vTop - 0.1f, 0f)));
            OfficeExitRoute.Add(transform.TransformPoint(AtPoint(0.7f, vBottom + 0.25f, yBottom)));
            OfficeExitRoute.Add(transform.TransformPoint(AtPoint(1.15f, (vBottom + landingEnd) * 0.5f, yBottom)));
        }

        // Porta corta-fogo na parede da ponta, no patamar de baixo, com a placa verde acesa.
        void BuildOfficeExit(float floor, float v)
        {
            GameObject root = CreateChild("Saida");
            root.transform.localPosition = AtPoint(0f, v, floor);
            // No espaço local da saída, -Z aponta para dentro do vão (como no Level 0).
            root.transform.localRotation = Quaternion.LookRotation(_atEnd, Vector3.up);

            if (exitDoorModel != null && exitDoorModelMaterial != null)
            {
                MeshRenderer model = CreateRenderer("PortaModelo", exitDoorModel, exitDoorModelMaterial, ShadowCastingMode.On, root.transform);
                model.gameObject.AddComponent<MeshCollider>().sharedMesh = exitDoorModel;
            }
            else
            {
                MeshRenderer door = CreateRenderer("Porta", CreateQuadMesh("Porta", 0.95f, 2.05f, horizontal: false),
                                                   office.exitDoor != null ? office.exitDoor : exitDoorMaterial, ShadowCastingMode.Off, root.transform);
                door.transform.localPosition = new Vector3(0f, 1.025f, -0.012f);
            }

            var signMesh = new MeshBuilder(1f);
            // Quem olha a porta olha para +Z local: a direita dele é +X.
            SignFace(signMesh, new Vector3(0f, 2.35f, -0.03f), Vector3.back, Vector3.right, 0.5f, 0.2f, 3);
            MeshRenderer sign = CreateRenderer("Placa", signMesh.ToMesh("PlacaDeSaida"), office.signGreen, ShadowCastingMode.Off, root.transform);
            sign.transform.localPosition = Vector3.zero;

            HDAdditionalLightData glow = CreateLightObject("Brilho", root.transform, LightType.Point, new Vector3(0f, 2.25f, -0.35f), out Light green);
            green.useColorTemperature = false;
            green.color = new Color(0.1f, 1f, 0.35f);
            green.intensity = 30f;
            green.range = 4.5f;
            green.shadows = LightShadows.None;
            glow.affectsVolumetric = false;

            var zone = new GameObject("Interacao");
            zone.transform.SetParent(root.transform, false);
            zone.transform.localPosition = new Vector3(0f, 1.1f, -0.55f);
            zone.AddComponent<LevelExit>().door = root.transform;
        }

        // ---------------------------------------------------------------- Placas de saída

        /// <summary>
        /// Um quadro do atlas da placa de saída (4 quadros lado a lado: seta para frente, para a
        /// esquerda, para a direita, sem seta), visto de quem olha no sentido -<paramref name="normal"/>.
        /// </summary>
        static void SignFace(MeshBuilder mb, Vector3 center, Vector3 normal, Vector3 viewerRight, float width, float height, int frame)
        {
            Vector3 r = viewerRight.normalized * (width * 0.5f), u = Vector3.up * (height * 0.5f);
            float u0 = frame * 0.25f, u1 = u0 + 0.25f;
            int a = mb.AddVertex(center - r - u, normal, new Vector2(u0, 0f));
            int b = mb.AddVertex(center + r - u, normal, new Vector2(u1, 0f));
            int c = mb.AddVertex(center + r + u, normal, new Vector2(u1, 1f));
            int d = mb.AddVertex(center - r + u, normal, new Vector2(u0, 1f));
            mb.AddTriangleFacing(a, b, c, normal);
            mb.AddTriangleFacing(a, c, d, normal);
        }

        // Placas de saída de emergência penduradas no corredor, ao longo do caminho até a escada,
        // viradas para quem vem do início: seta para frente nas retas e para o lado nas curvas.
        void BuildOfficeSigns()
        {
            int w = Data.Width;
            MeshBuilder faces = OM(office.signGreen);
            foreach (Decoration mark in Data.Decorations)
            {
                if (mark.Kind != DecorationKind.Arrow) continue;
                int index = Data.PathCells.IndexOf(mark.X + mark.Z * w);
                if (index <= 0) continue;

                int previous = Data.PathCells[index - 1];
                var incoming = new Vector3(mark.X - previous % w, 0f, mark.Z - previous / w);
                var outgoing = new Vector3(mark.DirX, 0f, mark.DirZ);
                int frame = incoming == outgoing ? 0 : Vector3.Cross(incoming, outgoing).y < 0f ? 1 : 2;

                Vector3 right = Vector3.Cross(Vector3.up, incoming);
                Vector3 center = OCellCenter(mark.X, mark.Z) - incoming * 1.1f + Vector3.up * 2.42f;
                OBox(office.blackPlastic, center, Abs(right) * 0.64f + Abs(incoming) * 0.05f + Vector3.up * 0.26f);
                SignFace(faces, center - incoming * 0.026f, -incoming, right, 0.6f, 0.22f, frame);
                SignFace(faces, center + incoming * 0.026f, incoming, -right, 0.6f, 0.22f, 3);
                foreach (float s in new[] { -0.25f, 0.25f })
                    OBox(office.steel, center + right * s + Vector3.up * ((OfficeSlab - 2.55f) * 0.5f + 0.13f), new Vector3(0.008f, OfficeSlab - 2.55f, 0.008f), caps: false);
            }
        }
    }
}
