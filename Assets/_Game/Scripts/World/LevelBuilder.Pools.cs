using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    /// <summary>
    /// Geometria das Piscinas (Level 2), a partir da planta do <see cref="PoolGenerator"/>: salas
    /// de azulejo claro (piso, parede e teto), paredes de 0,4 m com arcos, vãos, vigias e janelas
    /// recortados sem moldura, desníveis com escada ou borda, colunas lisas, poço redondo,
    /// claraboias com luz de caixa inclinada, sanca no encontro da parede com o teto, água e
    /// cáustica.
    ///
    /// Colisores: caixas para o que é caixa (pisos, paredes retas, pilaretes, degraus, peitoris,
    /// vergas); o que é curvo (arcos, vigias, colunas redondas, poço redondo) e o teto ficam em
    /// malhas com MeshCollider da MESMA malha. Toda face de caixa que um raio alcança tem a face
    /// visível no mesmo lugar (Level0ColliderCheck).
    ///
    /// Medidas em metros; célula de 4 m; pisos na grade de 20 cm a partir de Y = 0 e água 5 cm
    /// acima (<see cref="LevelData.WaterLevel"/>). Tudo depende só de <see cref="Data"/>.
    /// </summary>
    public sealed partial class LevelBuilder
    {
        /// <summary>Espessura das paredes das Piscinas, centradas na linha da grade.</summary>
        public const float PoolWall = 0.4f;
        /// <summary>Nas Piscinas, paredes, pilaretes e colisores de piso descem até aqui (abaixo do poço).</summary>
        public const float PoolBase = -7f;

        const float HalfWall = PoolWall * 0.5f;
        // Trecho de parede entre dois pilaretes de canto: de -1,8 a 1,8 a partir do meio da divisa.
        const float SegmentHalf = Cell * 0.5f - HalfWall;
        const float StepRun = 0.3f;
        const float CoveRadius = 0.4f;
        const int CoveSegments = 8;
        const float CopingWidth = 0.3f;
        // Comprimento de uma repetição da textura da capa ao longo da borda (a imagem é quadrada e
        // vira 0,3 m de largura por 0,6 m de comprimento: a pedra não tem desenho que mostre o esticão).
        const float CopingRepeat = 0.6f;
        const float RoundWellRadius = 1.6f;
        // O deque do poço redondo é o piso da água funda em volta dele.
        static float RoundWellDeck => LevelData.DepthFloor(PoolDepth.Deep);
        const float ShaftHeight = 1f;
        const float GlassRecess = 0.15f;
        // Pontos por meia volta nas aberturas redondas (a volta inteira tem o dobro).
        const int HalfCircleSegments = 16;
        const int ArchSegments = 24;
        const int ColumnSides = 24;
        const int VaultSegments = 24;
        const float CausticsLift = 0.01f;
        const float WaterY = LevelData.WaterLevel;
        /// <summary>Base da fiada de azulejo (20 cm) que a linha d'água corta.</summary>
        static float WaterlineCourse => Mathf.Floor(WaterY / LevelData.StepRise) * LevelData.StepRise;
        // Manchas: no máximo uma a cada tantas células com piso, e um teto para o mapa inteiro.
        const int CellsPerStain = 10;
        const int MaxStains = 10;
        const int MaxRust = 3;
        // Mofo no rejunte (Stain_GroutMold.png): as linhas da textura ficam a cada 0,209 da largura;
        // a do meio está 0,045 acima e 0,0215 à direita do centro. O decalque tem o tamanho que
        // põe uma linha por junta do azulejo.
        float MouldSize => poolTileMeters / 6f / 0.209f;
        const float MouldLineUp = 0.045f;
        const float MouldLineRight = 0.0215f;

        /// <summary>Resumo da última montagem das Piscinas (objetos, vértices, luzes), para as ferramentas de teste.</summary>
        public string PoolStats { get; private set; }
        /// <summary>Onde a última montagem pôs cada mancha e marcação (tipo, célula, lado), para as ferramentas de teste.</summary>
        public string PoolDecalLog { get; private set; }

        enum Surface : byte { Wall, Floor, Bottom, Coping, Accent }

        /// <summary>Como uma face é desenhada: sala que ela olha, faixa azul, malha com colisor, material.</summary>
        struct Look
        {
            /// <summary>Sala para onde a face olha; -1 = virada para o maciço (ninguém vê).</summary>
            public int Room;
            public bool Bands;
            public bool Curved;
            public bool NoCaustics;
            public Surface Kind;
        }

        static readonly Look VoidLook = new Look { Room = -1, NoCaustics = true };

        sealed class Hole
        {
            /// <summary>0 = retângulo, 1 = arco (topo em arco de círculo), 2 = círculo.</summary>
            public int Kind;
            public float U0, U1, Yb, Yt;
            // Arco e círculo: centro (em U = 0) e raio. Arco: altura do arranque.
            public float Cy, R, Ys;
        }

        sealed class Segment
        {
            public bool AlongX;
            public int I, J;
            public Vector3 Origin, Along, Across;
            public float Top;
            public int Ax, Az, Bx, Bz;
            public bool OpenA, OpenB;
            public List<Hole> Holes = new List<Hole>();
            /// <summary>0 = aberturas atravessam a parede; -1 / +1 = janela acesa recuada do lado A / B.</summary>
            public int Lit;
        }

        struct FacePlane
        {
            public float D;
            public Vector3 Normal;
            public Look Look;
            public float Bottom;
        }

        sealed class GlassPiece
        {
            public Vector3 A, B, C, D;    // perímetro: baixo-esquerda, cima-esquerda, cima-direita, baixo-direita
            public bool Disc;
            public Vector3 Center, Right;
            public float Radius;
        }

        sealed class LitOpening
        {
            public PoolEdge Type;
            public bool AlongX;
            public int Line, Index, Side;
            public Vector3 Normal, Center;
            public List<GlassPiece> Glass = new List<GlassPiece>();
        }

        sealed class PoolKit
        {
            public MeshBuilder Wall, Floor, Worn, Bottom, Accent, Coping;
            public MeshBuilder CurvedWall, CurvedAccent, CurvedBottom, Ceiling;
            public MeshBuilder Cove, Caustics, Rails, Lamps, Grate, Water;
            public bool[] WornRoom, AccentRoom;
            public float[] Band;
            public bool[,] RoundWell;
            public bool[,] WallS, WallW;
            public float[,] TopS, TopW;
            public readonly Dictionary<int, Segment> Segments = new Dictionary<int, Segment>();
            public readonly List<LitOpening> Lit = new List<LitOpening>();
            public GameObject Colliders;
            public int Lights, ShadowLights, Objects;
            /// <summary>Caixas dos decalques já postos (manchas e marcações), para um não cair em cima do outro.</summary>
            public readonly List<Bounds> Decals = new List<Bounds>();
            /// <summary>Faces de parede (célula * 4 + lado) que já têm mancha ou marcação.</summary>
            public readonly HashSet<int> DecalFaces = new HashSet<int>();
            /// <summary>Onde ficou cada mancha e marcação, para o registro das ferramentas de teste.</summary>
            public readonly List<string> DecalLog = new List<string>();
            public readonly int[] StainCount = new int[5];
            public readonly int[] MarkCount = new int[4];
        }

        PoolKit _kit;

        // ---------------------------------------------------------------- Montagem

        void BuildPools()
        {
            int w = Data.Width, h = Data.Height;
            _kit = new PoolKit
            {
                Wall = new MeshBuilder(poolTileMeters),
                Floor = new MeshBuilder(poolTileMeters),
                Worn = new MeshBuilder(poolTileMeters),
                Bottom = new MeshBuilder(poolMosaicMeters),
                Accent = new MeshBuilder(poolTileMeters),
                // A capa passa a UV pronta (AddCoping), já na escala da textura.
                Coping = new MeshBuilder(1f),
                CurvedWall = new MeshBuilder(poolTileMeters),
                CurvedAccent = new MeshBuilder(poolTileMeters),
                CurvedBottom = new MeshBuilder(poolMosaicMeters),
                Ceiling = new MeshBuilder(poolTileMeters),
                Cove = new MeshBuilder(poolTileMeters),
                Caustics = new MeshBuilder(4f),
                Rails = new MeshBuilder(1f),
                Lamps = new MeshBuilder(1f),
                Grate = new MeshBuilder(1f),
                Water = new MeshBuilder(4f),
                RoundWell = new bool[w, h],
                WallS = new bool[w, h + 1],
                WallW = new bool[w + 1, h],
                TopS = new float[w, h + 1],
                TopW = new float[w + 1, h],
                Colliders = CreateChild("Colisores"),
            };

            int rooms = Data.Rooms.Count;
            _kit.WornRoom = new bool[rooms];
            _kit.AccentRoom = new bool[rooms];
            _kit.Band = new float[rooms];
            for (int r = 0; r < rooms; r++)
            {
                PoolRoom room = Data.Rooms[r];
                int cx = Mathf.Clamp(room.X0 + room.Width / 2, 0, w - 1), cz = Mathf.Clamp(room.Z0 + room.Height / 2, 0, h - 1);
                _kit.WornRoom[r] = poolWornTileMaterial != null && LevelGenerator.WearAt(Data, cx, cz) > 0.6f;
                _kit.AccentRoom[r] = room.Accent && poolAccentMaterial != null;

                // A primeira fiada azul é a da linha d'água (a fiada de azulejo que a água corta,
                // como numa piscina de verdade); em sala com piso seco, rente a ele.
                bool dry = false;
                for (int z = room.Z0; z < room.Z0 + room.Height; z++)
                    for (int x = room.X0; x < room.X0 + room.Width; x++)
                        if (OpenCell(x, z) && Data.Depth[x, z] == PoolDepth.Dry) dry = true;
                _kit.Band[r] = dry ? LevelData.DepthFloor(PoolDepth.Dry) : WaterlineCourse;
            }

            FindRoundWells();
            BuildPoolEdges();
            BuildPoolPosts();
            BuildPoolFloors();
            BuildPoolStairs();
            BuildPoolCeilings();
            BuildCoves();
            BuildColumns();
            BuildPoolWater();

            CreatePoolRenderers();
            CountShadowLights(out int shadowed);
            int resolution = ResolvePoolShadowResolution(shadowed);
            BuildSkylights(resolution);
            BuildLitOpenings(resolution);
            BuildLamps();
            BuildPoolStains();
            BuildPoolMarks();
            BuildExit();

            int vertices = 0;
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh != null) vertices += filter.sharedMesh.vertexCount;
            int renderers = GetComponentsInChildren<MeshRenderer>().Length;
            int boxes = _kit.Colliders.GetComponents<BoxCollider>().Length;
            int[] st = _kit.StainCount, mk = _kit.MarkCount;
            PoolStats = $"{renderers} malhas desenhadas, {vertices} vértices, {boxes} caixas de colisão, " +
                        $"{_kit.Lights} luzes ({_kit.ShadowLights} com sombra de uma face em cache, resolução {resolution}); " +
                        $"manchas {st[0] + st[1] + st[2] + st[3] + st[4]} (limo {st[0]}, calcário {st[1]}, mofo {st[2]}, mofo no teto {st[3]}, ferrugem {st[4]}), " +
                        $"marcações {mk[0] + mk[1] + mk[2] + mk[3]} (2.0 M {mk[0]}, NO DIVING {mk[1]}, DEEP END {mk[2]}, T de raia {mk[3]})";
            PoolDecalLog = string.Join("; ", _kit.DecalLog);
            _kit = null;
        }

        // ---------------------------------------------------------------- Ajudantes de célula

        bool OpenCell(int x, int z)
        {
            return x >= 0 && z >= 0 && x < Data.Width && z < Data.Height && !Data.IsSolid(x, z) && Data.Room[x, z] >= 0;
        }

        /// <summary>Piso onde se pisa: no poço redondo, o deque em volta do furo.</summary>
        float FloorTop(int x, int z) => _kit.RoundWell[x, z] ? RoundWellDeck : Data.FloorHeight(x, z);

        PoolRoom RoomAt(int x, int z) => Data.Rooms[Data.Room[x, z]];

        static float VaultRadius(PoolRoom room)
        {
            return ((room.VaultAlongX ? room.Height : room.Width) * Cell - PoolWall) * 0.5f;
        }

        /// <summary>Altura livre na parede daquele lado da célula: o teto ou, na abóbada, o arranque (laterais) ou o fecho (topos).</summary>
        float ClearHeight(int x, int z, WallSide side)
        {
            PoolRoom room = RoomAt(x, z);
            float ceiling = Data.Ceiling[x, z];
            if (!room.Vault) return ceiling;
            bool end = room.VaultAlongX ? side == WallSide.West || side == WallSide.East : side == WallSide.South || side == WallSide.North;
            return end ? ceiling + VaultRadius(room) : ceiling;
        }

        Look WallLook(int x, int z) => new Look { Room = Data.Room[x, z], Bands = true };

        static Vector3 SideDir(WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return Vector3.back;
                case WallSide.North: return Vector3.forward;
                case WallSide.West: return Vector3.left;
                default: return Vector3.right;
            }
        }

        static readonly WallSide[] PoolSides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

        /// <summary>Há caixa de parede (cheia ou com abertura) naquele lado da célula?</summary>
        bool HasWallBox(int x, int z, WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return _kit.WallS[x, z];
                case WallSide.North: return _kit.WallS[x, z + 1];
                case WallSide.West: return _kit.WallW[x, z];
                default: return _kit.WallW[x + 1, z];
            }
        }

        /// <summary>Retângulo do piso e do teto da célula: até a face das paredes, ou até a linha da grade onde não há parede.</summary>
        void CellRect(int x, int z, out float x0, out float x1, out float z0, out float z1)
        {
            x0 = x * Cell + (HasWallBox(x, z, WallSide.West) ? HalfWall : 0f);
            x1 = (x + 1) * Cell - (HasWallBox(x, z, WallSide.East) ? HalfWall : 0f);
            z0 = z * Cell + (HasWallBox(x, z, WallSide.South) ? HalfWall : 0f);
            z1 = (z + 1) * Cell - (HasWallBox(x, z, WallSide.North) ? HalfWall : 0f);
        }

        // Poço redondo: célula de poço sem escada cujas vizinhas abertas são todas fundas.
        void FindRoundWells()
        {
            for (int z = 0; z < Data.Height; z++)
                for (int x = 0; x < Data.Width; x++)
                    _kit.RoundWell[x, z] = IsRoundWell(Data, x, z);
        }

        /// <summary>A célula vira poço redondo? Poço sem escada cujas vizinhas abertas são todas fundas.</summary>
        public static bool IsRoundWell(LevelData d, int x, int z)
        {
            bool Open(int cx, int cz) => cx >= 0 && cz >= 0 && cx < d.Width && cz < d.Height && !d.IsSolid(cx, cz) && d.Room[cx, cz] >= 0;
            if (!Open(x, z) || d.Depth[x, z] != PoolDepth.Well) return false;
            foreach (WallSide side in PoolSides)
            {
                if (d.StairAt(x, z, side, out _)) return false;
                if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz) || !Open(nx, nz)) continue;
                if (d.Depth[nx, nz] != PoolDepth.Deep) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- Emissão de faces

        MeshBuilder Target(Look look)
        {
            switch (look.Kind)
            {
                case Surface.Bottom: return look.Curved ? _kit.CurvedBottom : _kit.Bottom;
                case Surface.Coping: return _kit.Coping;
                case Surface.Accent: return look.Curved ? _kit.CurvedAccent : _kit.Accent;
                case Surface.Floor:
                    if (look.Curved) return _kit.CurvedWall;
                    return look.Room >= 0 && _kit.WornRoom[look.Room] ? _kit.Worn : _kit.Floor;
                default:
                    if (look.Curved) return _kit.CurvedWall;
                    return look.Room >= 0 && _kit.WornRoom[look.Room] ? _kit.Worn : _kit.Wall;
            }
        }

        /// <summary>
        /// Polígono plano convexo: vai para a malha do material, recortado nas faixas azuis (se a
        /// sala tem) e copiado para a cáustica no trecho abaixo da água.
        /// </summary>
        void Emit(List<Vector3> poly, Vector3 normal, Look look)
        {
            if (poly.Count < 3) return;
            MeshBuilder main = Target(look);

            if (look.Bands && look.Room >= 0 && _kit.AccentRoom[look.Room])
            {
                float b = _kit.Band[look.Room];
                float[] cuts = { float.NegativeInfinity, b, b + 0.2f, 2f, 2.2f, float.PositiveInfinity };
                for (int k = 0; k + 1 < cuts.Length; k++)
                {
                    List<Vector3> piece = ClipY(poly, cuts[k], cuts[k + 1]);
                    if (piece.Count < 3) continue;
                    bool accent = k == 1 || k == 3;
                    (accent ? (look.Curved ? _kit.CurvedAccent : _kit.Accent) : main).AddPolygon(piece, normal);
                }
            }
            else main.AddPolygon(poly, normal);

            if (look.NoCaustics || look.Room < 0 || poolCausticsMaterial == null) return;
            float lowest = float.MaxValue;
            foreach (Vector3 p in poly) lowest = Mathf.Min(lowest, p.y);
            if (lowest >= WaterY - 0.001f) return;

            List<Vector3> wet = ClipY(poly, float.NegativeInfinity, WaterY);
            if (wet.Count < 3) return;
            for (int k = 0; k < wet.Count; k++) wet[k] += normal * CausticsLift;
            _kit.Caustics.AddPolygon(wet, normal);
        }

        static List<Vector3> Poly(params Vector3[] points) => new List<Vector3>(points);

        static List<Vector3> ClipY(List<Vector3> poly, float lo, float hi)
        {
            List<Vector3> result = poly;
            if (!float.IsNegativeInfinity(lo)) result = ClipHalf(result, lo, true);
            if (!float.IsPositiveInfinity(hi)) result = ClipHalf(result, hi, false);
            return result;
        }

        static List<Vector3> ClipHalf(List<Vector3> poly, float level, bool keepAbove)
        {
            var output = new List<Vector3>(poly.Count + 2);
            for (int k = 0; k < poly.Count; k++)
            {
                Vector3 a = poly[k], b = poly[(k + 1) % poly.Count];
                bool aIn = keepAbove ? a.y >= level : a.y <= level;
                bool bIn = keepAbove ? b.y >= level : b.y <= level;
                if (aIn) AddDistinct(output, a);
                if (aIn != bIn) AddDistinct(output, Vector3.Lerp(a, b, (level - a.y) / (b.y - a.y)));
            }
            if (output.Count > 1 && (output[0] - output[output.Count - 1]).sqrMagnitude < 1e-10f) output.RemoveAt(output.Count - 1);
            return output;
        }

        static void AddDistinct(List<Vector3> list, Vector3 p)
        {
            if (list.Count > 0 && (list[list.Count - 1] - p).sqrMagnitude < 1e-10f) return;
            list.Add(p);
        }

        // ---------------------------------------------------------------- Paredes

        void BuildPoolEdges()
        {
            int w = Data.Width, h = Data.Height;
            for (int j = 0; j <= h; j++)
                for (int i = 0; i < w; i++)
                    BuildPoolEdge(true, i, j);
            for (int i = 0; i <= w; i++)
                for (int j = 0; j < h; j++)
                    BuildPoolEdge(false, i, j);
        }

        int SegmentKey(bool alongX, int i, int j) => (alongX ? 0 : 1) + 2 * (i + (Data.Width + 1) * j);

        void BuildPoolEdge(bool alongX, int i, int j)
        {
            var s = new Segment { AlongX = alongX, I = i, J = j };
            WallSide sideA, sideB;
            if (alongX)
            {
                s.Ax = i; s.Az = j - 1; s.Bx = i; s.Bz = j;
                sideA = WallSide.North; sideB = WallSide.South;
                s.Origin = new Vector3(i * Cell + Cell * 0.5f, 0f, j * Cell);
                s.Along = Vector3.right; s.Across = Vector3.forward;
            }
            else
            {
                s.Ax = i - 1; s.Az = j; s.Bx = i; s.Bz = j;
                sideA = WallSide.East; sideB = WallSide.West;
                s.Origin = new Vector3(i * Cell, 0f, j * Cell + Cell * 0.5f);
                s.Along = Vector3.forward; s.Across = Vector3.right;
            }

            s.OpenA = OpenCell(s.Ax, s.Az);
            s.OpenB = OpenCell(s.Bx, s.Bz);
            if (!s.OpenA && !s.OpenB) return;

            PoolEdge type = alongX ? Data.EdgeSouth[i, j] : Data.EdgeWest[i, j];
            if (type == PoolEdge.Open && s.OpenA && s.OpenB) return;
            // Divisa aberta contra o maciço ou a borda do mapa: fecha com parede.
            if (type == PoolEdge.Open) type = PoolEdge.Wall;

            float top = PoolBase, clear = float.MaxValue, upper = float.MinValue;
            // Topo de qualquer vão de passagem: no máximo 0,6 m abaixo do teto mais baixo dos dois lados (longe da sanca).
            if (s.OpenA)
            {
                top = Mathf.Max(top, ClearHeight(s.Ax, s.Az, sideA));
                clear = Mathf.Min(clear, ClearHeight(s.Ax, s.Az, sideA));
                upper = Mathf.Max(upper, FloorTop(s.Ax, s.Az));
            }
            if (s.OpenB)
            {
                top = Mathf.Max(top, ClearHeight(s.Bx, s.Bz, sideB));
                clear = Mathf.Min(clear, ClearHeight(s.Bx, s.Bz, sideB));
                upper = Mathf.Max(upper, FloorTop(s.Bx, s.Bz));
            }
            s.Top = top;

            // Janelas acesas: do lado da sala (se as duas forem salas, do lado B).
            int lit = s.OpenB ? 1 : -1;
            int lx = lit > 0 ? s.Bx : s.Ax, lz = lit > 0 ? s.Bz : s.Az;
            WallSide litSide = lit > 0 ? sideB : sideA;
            float litFloor = FloorTop(lx, lz);
            float litClear = ClearHeight(lx, lz, litSide);
            // Abertura acesa nunca chega à sanca.
            float litCap = RoomAt(lx, lz).Vault ? litClear - 0.05f : litClear - CoveRadius - 0.05f;

            switch (type)
            {
                case PoolEdge.Doorway:
                    AddHole(s, new Hole { Kind = 0, U0 = -0.7f, U1 = 0.7f, Yb = upper, Yt = Mathf.Min(upper + 2.4f, clear - 0.6f) });
                    break;
                case PoolEdge.Arch:
                {
                    float crown = Mathf.Min(upper + 3.2f, clear - 0.6f);
                    AddHole(s, new Hole { Kind = 1, U0 = -1.2f, U1 = 1.2f, Yb = upper, Yt = crown, R = 1.2f, Cy = crown - 1.2f, Ys = crown - 1.2f });
                    break;
                }
                case PoolEdge.WideArch:
                {
                    // Arco abatido: corda de 3,4 m, flecha de 0,8 m.
                    const float half = 1.7f, rise = 0.8f;
                    float radius = (half * half + rise * rise) / (2f * rise);
                    float crown = Mathf.Min(upper + 3.0f, clear - 0.6f);
                    AddHole(s, new Hole { Kind = 1, U0 = -half, U1 = half, Yb = upper, Yt = crown, R = radius, Cy = crown - radius, Ys = crown - rise });
                    break;
                }
                case PoolEdge.Porthole:
                {
                    float center = Mathf.Min(upper + 1.9f, clear - 1.6f);
                    AddHole(s, new Hole { Kind = 2, U0 = -1f, U1 = 1f, Yb = center - 1f, Yt = center + 1f, R = 1f, Cy = center });
                    break;
                }
                case PoolEdge.Window:
                    s.Lit = lit;
                    AddHole(s, new Hole { Kind = 0, U0 = -1.6f, U1 = 1.6f, Yb = litFloor + 1f, Yt = Mathf.Min(litClear - 0.6f, litCap) });
                    break;
                case PoolEdge.GlassBlock:
                    s.Lit = lit;
                    AddHole(s, new Hole { Kind = 0, U0 = -1.6f, U1 = 1.6f, Yb = litFloor + 0.4f, Yt = Mathf.Min(litClear - 0.4f, litCap) });
                    break;
                case PoolEdge.Slits:
                {
                    s.Lit = lit;
                    float yt = Mathf.Min(litFloor + 3f, litCap);
                    AddHole(s, new Hole { Kind = 0, U0 = -1.15f, U1 = -0.65f, Yb = litFloor + 0.6f, Yt = yt });
                    AddHole(s, new Hole { Kind = 0, U0 = 0.65f, U1 = 1.15f, Yb = litFloor + 0.6f, Yt = yt });
                    break;
                }
                case PoolEdge.RoundWindow:
                {
                    s.Lit = lit;
                    float center = Mathf.Min(litFloor + 2f, litCap - 0.9f);
                    AddHole(s, new Hole { Kind = 2, U0 = -0.9f, U1 = 0.9f, Yb = center - 0.9f, Yt = center + 0.9f, R = 0.9f, Cy = center });
                    break;
                }
            }
            if (s.Holes.Count == 0) s.Lit = 0;

            if (alongX) { _kit.WallS[i, j] = true; _kit.TopS[i, j] = top; }
            else { _kit.WallW[i, j] = true; _kit.TopW[i, j] = top; }
            _kit.Segments[SegmentKey(alongX, i, j)] = s;

            BuildWallSegment(s);

            if (s.Lit != 0)
            {
                var opening = new LitOpening
                {
                    Type = type,
                    AlongX = alongX,
                    Line = alongX ? j : i,
                    Index = alongX ? i : j,
                    Side = s.Lit,
                    Normal = s.Across * s.Lit,
                };
                float glass = s.Lit * (HalfWall - GlassRecess);
                foreach (Hole hole in s.Holes)
                {
                    if (hole.Kind == 2)
                        opening.Glass.Add(new GlassPiece { Disc = true, Center = SegPoint(s, 0f, hole.Cy, glass), Right = s.Along, Radius = hole.R });
                    else
                        opening.Glass.Add(new GlassPiece
                        {
                            A = SegPoint(s, hole.U0, hole.Yb, glass), B = SegPoint(s, hole.U0, hole.Yt, glass),
                            C = SegPoint(s, hole.U1, hole.Yt, glass), D = SegPoint(s, hole.U1, hole.Yb, glass),
                        });
                }
                Vector3 sum = Vector3.zero;
                foreach (Hole hole in s.Holes) sum += SegPoint(s, (hole.U0 + hole.U1) * 0.5f, (hole.Yb + hole.Yt) * 0.5f, glass);
                opening.Center = sum / s.Holes.Count;
                _kit.Lit.Add(opening);
            }
        }

        // Abertura que não cabe (pé-direito baixo demais) vira parede cheia.
        static void AddHole(Segment s, Hole hole)
        {
            if (hole.Yt - hole.Yb < 0.5f) return;
            s.Holes.Add(hole);
        }

        static Vector3 SegPoint(Segment s, float u, float y, float d) => s.Origin + s.Along * u + Vector3.up * y + s.Across * d;

        // Coordenada de mundo ao longo da espessura da parede: a mesma que a UV do mundo usa nas
        // faces de dentro do vão (jambas), para o rejunte continuar do vão para o arco.
        static float DepthCoord(Segment s, float d) => s.AlongX ? s.Origin.z + d : s.Origin.x + d;

        FacePlane PlaneOf(Segment s, int side)
        {
            bool open = side < 0 ? s.OpenA : s.OpenB;
            int x = side < 0 ? s.Ax : s.Bx, z = side < 0 ? s.Az : s.Bz;
            return new FacePlane
            {
                D = side * HalfWall,
                Normal = s.Across * side,
                Look = open ? WallLook(x, z) : VoidLook,
                Bottom = open ? FloorTop(x, z) : PoolBase,
            };
        }

        void SegBox(Segment s, float u0, float u1, float y0, float y1, float d0, float d1)
        {
            if (u1 - u0 < 1e-4f || y1 - y0 < 1e-4f || d1 - d0 < 1e-4f) return;
            Vector3 center = SegPoint(s, (u0 + u1) * 0.5f, (y0 + y1) * 0.5f, (d0 + d1) * 0.5f);
            Vector3 size = s.Along * (u1 - u0) + Vector3.up * (y1 - y0) + s.Across * (d1 - d0);
            AddCollider(_kit.Colliders, center, size);
        }

        void PlaneRect(Segment s, FacePlane p, float u0, float u1, float y0, float y1)
        {
            if (u1 - u0 < 1e-4f || y1 - y0 < 1e-4f) return;
            Emit(Poly(SegPoint(s, u0, y0, p.D), SegPoint(s, u0, y1, p.D), SegPoint(s, u1, y1, p.D), SegPoint(s, u1, y0, p.D)), p.Normal, p.Look);
        }

        /// <summary>
        /// Um trecho de parede entre dois pilaretes, com as aberturas. Caixas: ombreiras (entre
        /// as aberturas), peitoril e verga de cada abertura e, na janela recuada, a parte de trás.
        /// O miolo curvo (arco, círculo) é malha com colisor próprio.
        /// </summary>
        void BuildWallSegment(Segment s)
        {
            float top = s.Top;
            FacePlane pa = PlaneOf(s, -1), pb = PlaneOf(s, 1);
            float d0 = -HalfWall, d1 = HalfWall;
            var exposed = new List<FacePlane>();
            var holes = s.Holes;
            holes.Sort((a, b) => a.U0.CompareTo(b.U0));

            if (s.Lit == 0)
            {
                exposed.Add(pa);
                exposed.Add(pb);
            }
            else
            {
                // Janela acesa: o vidro fica 15 cm para dentro da face da sala; atrás dele, parede cheia.
                FacePlane front = s.Lit > 0 ? pb : pa, back = s.Lit > 0 ? pa : pb;
                float glass = s.Lit * (HalfWall - GlassRecess);
                if (s.Lit > 0) { SegBox(s, -SegmentHalf, SegmentHalf, PoolBase, top, -HalfWall, glass); d0 = glass; }
                else { SegBox(s, -SegmentHalf, SegmentHalf, PoolBase, top, glass, HalfWall); d1 = glass; }
                PlaneRect(s, back, -SegmentHalf, SegmentHalf, back.Bottom, top);

                // No plano do vidro, em volta da vigia redonda: o fundo dos cantos do recorte.
                var glassPlane = new FacePlane { D = glass, Normal = front.Normal, Look = front.Look, Bottom = front.Bottom };
                glassPlane.Look.Bands = false;
                glassPlane.Look.NoCaustics = true;
                foreach (Hole hole in holes)
                    if (hole.Kind == 2) CircleStrips(s, glassPlane, hole, curved: false);

                exposed.Add(front);
            }

            Look reveal = RevealLook(s);

            // Ombreiras: o que sobra entre as aberturas, de alto a baixo.
            float u = -SegmentHalf;
            foreach (Hole hole in holes)
            {
                Jamb(s, exposed, u, hole.U0, top, d0, d1);
                u = hole.U1;
            }
            Jamb(s, exposed, u, SegmentHalf, top, d0, d1);

            foreach (Hole hole in holes)
            {
                // Peitoril (ou soleira) e a face de cima dele.
                if (hole.Yb > PoolBase)
                {
                    SegBox(s, hole.U0, hole.U1, PoolBase, hole.Yb, d0, d1);
                    foreach (FacePlane p in exposed) PlaneRect(s, p, hole.U0, hole.U1, p.Bottom, hole.Yb);
                    Emit(Poly(SegPoint(s, hole.U0, hole.Yb, d0), SegPoint(s, hole.U0, hole.Yb, d1),
                              SegPoint(s, hole.U1, hole.Yb, d1), SegPoint(s, hole.U1, hole.Yb, d0)), Vector3.up, reveal);
                }

                // Verga e a face de baixo dela.
                if (hole.Yt < top)
                {
                    SegBox(s, hole.U0, hole.U1, hole.Yt, top, d0, d1);
                    foreach (FacePlane p in exposed) PlaneRect(s, p, hole.U0, hole.U1, Mathf.Max(hole.Yt, p.Bottom), top);
                    Emit(Poly(SegPoint(s, hole.U0, hole.Yt, d0), SegPoint(s, hole.U0, hole.Yt, d1),
                              SegPoint(s, hole.U1, hole.Yt, d1), SegPoint(s, hole.U1, hole.Yt, d0)), Vector3.down, reveal);
                }

                // Faces de dentro das ombreiras (no arco e no círculo, a parte de cima fica escondida no miolo).
                Emit(Poly(SegPoint(s, hole.U0, hole.Yb, d0), SegPoint(s, hole.U0, hole.Yt, d0),
                          SegPoint(s, hole.U0, hole.Yt, d1), SegPoint(s, hole.U0, hole.Yb, d1)), s.Along, reveal);
                Emit(Poly(SegPoint(s, hole.U1, hole.Yb, d0), SegPoint(s, hole.U1, hole.Yt, d0),
                          SegPoint(s, hole.U1, hole.Yt, d1), SegPoint(s, hole.U1, hole.Yb, d1)), -s.Along, reveal);

                if (hole.Kind == 1)
                {
                    foreach (FacePlane p in exposed) ArchStrips(s, p, hole);
                    ArchSoffit(s, hole, d0, d1);
                }
                else if (hole.Kind == 2)
                {
                    foreach (FacePlane p in exposed) CircleStrips(s, p, hole, curved: true);
                    CircleBore(s, hole, d0, d1);
                }
            }
        }

        // O vão é de quem fica no piso mais alto (é por onde se passa).
        Look RevealLook(Segment s)
        {
            Look look;
            if (s.OpenA && (!s.OpenB || FloorTop(s.Ax, s.Az) >= FloorTop(s.Bx, s.Bz))) look = WallLook(s.Ax, s.Az);
            else look = WallLook(s.Bx, s.Bz);
            look.Bands = false;
            return look;
        }

        void Jamb(Segment s, List<FacePlane> exposed, float u0, float u1, float top, float d0, float d1)
        {
            if (u1 - u0 < 1e-4f) return;
            SegBox(s, u0, u1, PoolBase, top, d0, d1);
            foreach (FacePlane p in exposed) PlaneRect(s, p, u0, u1, p.Bottom, top);
        }

        static float ArchAngle(Hole hole, int k)
        {
            float start = Mathf.Acos(Mathf.Clamp(hole.U1 / hole.R, -1f, 1f));
            float a = Mathf.PI - start, b = start;
            return Mathf.Lerp(a, b, k / (float)ArchSegments);
        }

        // Face da parede acima do arco, até a verga: tiras verticais entre os pontos da curva.
        void ArchStrips(Segment s, FacePlane p, Hole hole)
        {
            Look look = p.Look;
            look.Curved = true;
            for (int k = 0; k < ArchSegments; k++)
            {
                float t0 = ArchAngle(hole, k), t1 = ArchAngle(hole, k + 1);
                float u0 = hole.R * Mathf.Cos(t0), y0 = hole.Cy + hole.R * Mathf.Sin(t0);
                float u1 = hole.R * Mathf.Cos(t1), y1 = hole.Cy + hole.R * Mathf.Sin(t1);
                Emit(Poly(SegPoint(s, u0, y0, p.D), SegPoint(s, u0, hole.Yt, p.D), SegPoint(s, u1, hole.Yt, p.D), SegPoint(s, u1, y1, p.D)), p.Normal, look);
            }
        }

        // Intradorso do arco: a espessura da parede aparecendo, com o rejunte continuando o das
        // ombreiras (V = altura do arranque + comprimento do arco a partir do arranque mais perto).
        void ArchSoffit(Segment s, Hole hole, float d0, float d1)
        {
            MeshBuilder mesh = _kit.CurvedWall;
            float length = hole.R * (ArchAngle(hole, 0) - ArchAngle(hole, ArchSegments));
            int first = mesh.VertexCount;
            for (int k = 0; k <= ArchSegments; k++)
            {
                float t = ArchAngle(hole, k);
                float u = hole.R * Mathf.Cos(t), y = hole.Cy + hole.R * Mathf.Sin(t);
                Vector3 normal = -(s.Along * Mathf.Cos(t) + Vector3.up * Mathf.Sin(t));
                float arc = hole.R * (ArchAngle(hole, 0) - t);
                float v = hole.Ys + Mathf.Min(arc, length - arc);
                mesh.AddVertex(SegPoint(s, u, y, d0), normal, new Vector2(DepthCoord(s, d0), v));
                mesh.AddVertex(SegPoint(s, u, y, d1), normal, new Vector2(DepthCoord(s, d1), v));
            }
            for (int k = 0; k < ArchSegments; k++)
            {
                int a = first + k * 2;
                Vector3 facing = -(s.Along * Mathf.Cos(ArchAngle(hole, k)) + Vector3.up * Mathf.Sin(ArchAngle(hole, k)));
                mesh.AddTriangleFacing(a, a + 1, a + 3, facing);
                mesh.AddTriangleFacing(a, a + 3, a + 2, facing);
            }
        }

        // Face da parede em volta de um furo redondo, dentro do quadrado que o contém.
        void CircleStrips(Segment s, FacePlane p, Hole hole, bool curved)
        {
            Look look = p.Look;
            look.Curved = curved;
            for (int k = 0; k < HalfCircleSegments; k++)
            {
                float t0 = Mathf.PI * k / HalfCircleSegments, t1 = Mathf.PI * (k + 1) / HalfCircleSegments;
                float u0 = hole.R * Mathf.Cos(t0), u1 = hole.R * Mathf.Cos(t1);
                float h0 = hole.R * Mathf.Sin(t0), h1 = hole.R * Mathf.Sin(t1);
                Emit(Poly(SegPoint(s, u0, hole.Cy + h0, p.D), SegPoint(s, u0, hole.Yt, p.D),
                          SegPoint(s, u1, hole.Yt, p.D), SegPoint(s, u1, hole.Cy + h1, p.D)), p.Normal, look);
                Emit(Poly(SegPoint(s, u0, hole.Yb, p.D), SegPoint(s, u0, hole.Cy - h0, p.D),
                          SegPoint(s, u1, hole.Cy - h1, p.D), SegPoint(s, u1, hole.Yb, p.D)), p.Normal, look);
            }
        }

        // O cilindro do furo redondo, de um plano ao outro, virado para o centro.
        void CircleBore(Segment s, Hole hole, float d0, float d1)
        {
            MeshBuilder mesh = _kit.CurvedWall;
            int sides = HalfCircleSegments * 2;
            int first = mesh.VertexCount;
            for (int k = 0; k <= sides; k++)
            {
                float t = Mathf.PI * 2f * k / sides;
                Vector3 radial = s.Along * Mathf.Cos(t) + Vector3.up * Mathf.Sin(t);
                float u = hole.R * Mathf.Cos(t), y = hole.Cy + hole.R * Mathf.Sin(t);
                float arc = hole.R * t;
                mesh.AddVertex(SegPoint(s, u, y, d0), -radial, new Vector2(DepthCoord(s, d0), arc));
                mesh.AddVertex(SegPoint(s, u, y, d1), -radial, new Vector2(DepthCoord(s, d1), arc));
            }
            for (int k = 0; k < sides; k++)
            {
                int a = first + k * 2;
                float t = Mathf.PI * 2f * (k + 0.5f) / sides;
                Vector3 facing = -(s.Along * Mathf.Cos(t) + Vector3.up * Mathf.Sin(t));
                mesh.AddTriangleFacing(a, a + 1, a + 3, facing);
                mesh.AddTriangleFacing(a, a + 3, a + 2, facing);
            }
        }

        // ---------------------------------------------------------------- Pilaretes de canto

        /// <summary>
        /// Em cada vértice da grade onde chega parede, um pilarete de 0,4 x 0,4 fecha o canto. As
        /// faces dele aparecem para os lados sem parede (e acima da parede mais baixa).
        /// </summary>
        void BuildPoolPosts()
        {
            int w = Data.Width, h = Data.Height;
            for (int j = 0; j <= h; j++)
                for (int i = 0; i <= w; i++)
                {
                    bool east = i < w && _kit.WallS[i, j], west = i > 0 && _kit.WallS[i - 1, j];
                    bool north = j < h && _kit.WallW[i, j], south = j > 0 && _kit.WallW[i, j - 1];
                    if (!east && !west && !north && !south) continue;

                    float te = east ? _kit.TopS[i, j] : PoolBase, tw = west ? _kit.TopS[i - 1, j] : PoolBase;
                    float tn = north ? _kit.TopW[i, j] : PoolBase, ts = south ? _kit.TopW[i, j - 1] : PoolBase;
                    float top = Mathf.Max(Mathf.Max(te, tw), Mathf.Max(tn, ts));

                    var corner = new Vector3(i * Cell, 0f, j * Cell);
                    AddCollider(_kit.Colliders, corner + Vector3.up * ((PoolBase + top) * 0.5f), new Vector3(PoolWall, top - PoolBase, PoolWall));

                    PostFace(corner, Vector3.right, east, te, top, i, j - 1, i, j);
                    PostFace(corner, Vector3.left, west, tw, top, i - 1, j - 1, i - 1, j);
                    PostFace(corner, Vector3.forward, north, tn, top, i - 1, j, i, j);
                    PostFace(corner, Vector3.back, south, ts, top, i - 1, j - 1, i, j - 1);
                }
        }

        void PostFace(Vector3 corner, Vector3 dir, bool wall, float wallTop, float top, int ax, int az, int bx, int bz)
        {
            Vector3 center = corner + dir * HalfWall;
            Vector3 tangent = Mathf.Abs(dir.x) > 0.5f ? Vector3.forward : Vector3.right;
            float bottom;
            Look look;

            if (wall)
            {
                if (top <= wallTop + 1e-3f) return;
                bottom = wallTop;
                look = VoidLook;
            }
            else
            {
                bool oa = OpenCell(ax, az), ob = OpenCell(bx, bz);
                if (oa || ob)
                {
                    look = oa ? WallLook(ax, az) : WallLook(bx, bz);
                    bottom = Mathf.Min(oa ? FloorTop(ax, az) : float.MaxValue, ob ? FloorTop(bx, bz) : float.MaxValue);
                }
                else
                {
                    look = VoidLook;
                    bottom = PoolBase;
                }
            }

            Vector3 a = center - tangent * HalfWall, b = center + tangent * HalfWall;
            Emit(Poly(a + Vector3.up * bottom, a + Vector3.up * top, b + Vector3.up * top, b + Vector3.up * bottom), dir, look);
        }

        // ---------------------------------------------------------------- Pisos e bordas

        float BoxTop(int x, int z) => _kit.RoundWell[x, z] ? LevelData.DepthFloor(PoolDepth.Well) : Data.FloorHeight(x, z);

        void BuildPoolFloors()
        {
            int w = Data.Width, h = Data.Height;

            // Colisor: uma caixa por trecho de células abertas da mesma altura em cada fileira,
            // do piso até PoolBase (fecha também a face vertical das bordas).
            for (int z = 0; z < h; z++)
            {
                int runStart = -1;
                for (int x = 0; x <= w; x++)
                {
                    bool open = x < w && OpenCell(x, z);
                    if (runStart >= 0 && (!open || !Mathf.Approximately(BoxTop(x, z), BoxTop(runStart, z))))
                    {
                        float y = BoxTop(runStart, z), length = (x - runStart) * Cell;
                        AddCollider(_kit.Colliders, new Vector3(runStart * Cell + length * 0.5f, (y + PoolBase) * 0.5f, (z + 0.5f) * Cell),
                                    new Vector3(length, y - PoolBase, Cell));
                        runStart = -1;
                    }
                    if (open && runStart < 0) runStart = x;
                }
            }

            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    if (!OpenCell(x, z)) continue;
                    CellRect(x, z, out float x0, out float x1, out float z0, out float z1);

                    if (_kit.RoundWell[x, z]) BuildRoundWell(x, z, x0, x1, z0, z1);
                    else BuildFloorSurface(x, z, x0, x1, z0, z1);

                    BuildBorders(x, z);
                }
        }

        // Lados na ordem de PoolSides (0 = sul, 1 = norte, 2 = oeste, 3 = leste). Cantos da célula:
        // 0 = sudoeste, 1 = sudeste, 2 = noroeste, 3 = nordeste; cada um junta um lado sul/norte
        // (CornerZ) e um oeste/leste (CornerX).
        static int CornerZ(int corner) => corner < 2 ? 0 : 1;
        static int CornerX(int corner) => corner % 2 == 0 ? 2 : 3;

        /// <summary>
        /// Aquele lado da célula tem borda com capa? Piso seco que dá, sem parede, para um piso
        /// aberto mais baixo. A escada corta a capa só no trecho dela (<see cref="StairSpan"/>).
        /// </summary>
        bool HasCoping(int x, int z, WallSide side)
        {
            if (!OpenCell(x, z) || Data.Depth[x, z] != PoolDepth.Dry || HasWallBox(x, z, side)) return false;
            if (!Data.Neighbor(x, z, side, out int nx, out int nz) || !OpenCell(nx, nz)) return false;
            return FloorTop(nx, nz) < FloorTop(x, z) - 1e-3f;
        }

        /// <summary>Trecho (na coordenada ao longo do lado) que a escada daquele lado ocupa.</summary>
        bool StairSpan(int x, int z, WallSide side, out float from, out float to)
        {
            from = to = float.NaN;
            if (!Data.StairAt(x, z, side, out PoolStair stair)) return false;
            float middle = side == WallSide.South || side == WallSide.North ? (x + 0.5f) * Cell : (z + 0.5f) * Cell;
            from = middle - stair.Width * 0.5f;
            to = middle + stair.Width * 0.5f;
            return true;
        }

        /// <summary>Tem capa naquele lado, no ponto <paramref name="along"/> dele (fora da escada)?</summary>
        bool HasCopingAt(int x, int z, WallSide side, float along)
        {
            if (!HasCoping(x, z, side)) return false;
            return !StairSpan(x, z, side, out float from, out float to) || along < from - 1e-3f || along > to + 1e-3f;
        }

        void BuildFloorSurface(int x, int z, float x0, float x1, float z0, float z1)
        {
            float y = FloorTop(x, z);
            PoolDepth depth = Data.Depth[x, z];
            var look = new Look { Room = Data.Room[x, z], Kind = depth == PoolDepth.Deep || depth == PoolDepth.Well ? Surface.Bottom : Surface.Floor };

            // Capa (rente ao piso) nas bordas do seco que dão para um piso mais baixo, fora da escada.
            var coping = new bool[4];
            var stairFrom = new float[4];
            var stairTo = new float[4];
            // Canto da piscina: a água faz a ponta na diagonal e as capas das duas vizinhas se
            // encontram no canto desta célula; ele leva a peça de canto (CopingWidth x CopingWidth).
            var wrap = new bool[4];
            if (depth == PoolDepth.Dry)
            {
                for (int k = 0; k < 4; k++)
                {
                    coping[k] = HasCoping(x, z, PoolSides[k]);
                    if (!coping[k] || !StairSpan(x, z, PoolSides[k], out stairFrom[k], out stairTo[k])) stairFrom[k] = stairTo[k] = float.NaN;
                }
                for (int c = 0; c < 4; c++)
                {
                    int kz = CornerZ(c), kx = CornerX(c);
                    WallSide sz = PoolSides[kz], sx = PoolSides[kx];
                    if (coping[kz] || coping[kx] || HasWallBox(x, z, sz) || HasWallBox(x, z, sx)) continue;
                    if (!Data.Neighbor(x, z, sx, out int ax, out int az) || !Data.Neighbor(x, z, sz, out int bx, out int bz)) continue;
                    float cornerX = kx == 2 ? x * Cell : (x + 1) * Cell, cornerZ = kz == 0 ? z * Cell : (z + 1) * Cell;
                    wrap[c] = HasCopingAt(ax, az, sz, cornerX) && HasCopingAt(bx, bz, sx, cornerZ);
                }
            }

            var xs = new List<float> { x0, x1 };
            var zs = new List<float> { z0, z1 };
            if (coping[2] || wrap[0] || wrap[2]) xs.Add(x0 + CopingWidth);
            if (coping[3] || wrap[1] || wrap[3]) xs.Add(x1 - CopingWidth);
            if (coping[0] || wrap[0] || wrap[1]) zs.Add(z0 + CopingWidth);
            if (coping[1] || wrap[2] || wrap[3]) zs.Add(z1 - CopingWidth);
            for (int k = 0; k < 4; k++)
            {
                if (!coping[k] || float.IsNaN(stairFrom[k])) continue;
                List<float> cuts = k < 2 ? xs : zs;
                cuts.Add(stairFrom[k]);
                cuts.Add(stairTo[k]);
            }
            Clean(xs, x0, x1);
            Clean(zs, z0, z1);

            var sides = new List<int>(2);
            for (int a = 0; a + 1 < xs.Count; a++)
                for (int b = 0; b + 1 < zs.Count; b++)
                {
                    float mx = (xs[a] + xs[a + 1]) * 0.5f, mz = (zs[b] + zs[b + 1]) * 0.5f;
                    List<Vector3> poly = Poly(new Vector3(xs[a], y, zs[b]), new Vector3(xs[a], y, zs[b + 1]),
                                              new Vector3(xs[a + 1], y, zs[b + 1]), new Vector3(xs[a + 1], y, zs[b]));
                    sides.Clear();
                    for (int k = 0; k < 4; k++)
                    {
                        if (!coping[k] || EdgeDistance(k, mx, mz, x0, x1, z0, z1) > CopingWidth) continue;
                        float along = k < 2 ? mx : mz;
                        if (!float.IsNaN(stairFrom[k]) && along > stairFrom[k] && along < stairTo[k]) continue;
                        sides.Add(k);
                    }

                    int corner = -1;
                    for (int c = 0; c < 4 && sides.Count == 0; c++)
                        if (wrap[c] && EdgeDistance(CornerZ(c), mx, mz, x0, x1, z0, z1) < CopingWidth && EdgeDistance(CornerX(c), mx, mz, x0, x1, z0, z1) < CopingWidth)
                            corner = c;

                    if (sides.Count == 1) AddCoping(poly, sides[0], x0, x1, z0, z1);
                    else if (sides.Count == 2) Miter(poly, sides[0], sides[1], false, x0, x1, z0, z1);
                    else if (corner >= 0) Miter(poly, CornerZ(corner), CornerX(corner), true, x0, x1, z0, z1);
                    else Emit(poly, Vector3.up, look);
                }
        }

        /// <summary>Distância do ponto até o lado <paramref name="k"/> do retângulo do piso.</summary>
        static float EdgeDistance(int k, float px, float pz, float x0, float x1, float z0, float z1)
        {
            switch (k)
            {
                case 0: return pz - z0;
                case 1: return z1 - pz;
                case 2: return px - x0;
                default: return x1 - px;
            }
        }

        /// <summary>
        /// Canto de duas capas em meia-esquadria, cortado na diagonal que sai do canto do piso. Na
        /// ponta do seco (capa nos dois lados), cada metade é da capa do lado mais perto; no canto
        /// da piscina (<paramref name="wrap"/>, as capas vêm das vizinhas), da capa que chega pelo
        /// outro lado (a que encosta na metade).
        /// </summary>
        void Miter(List<Vector3> poly, int kz, int kx, bool wrap, float x0, float x1, float z0, float z1)
        {
            float sz = kz == 0 ? 1f : -1f, sx = kx == 2 ? 1f : -1f;
            float ez = kz == 0 ? z0 : z1, ex = kx == 2 ? x0 : x1;
            // dz = sz * (z - ez) e dx = sx * (x - ex): distâncias até os dois lados.
            List<Vector3> nearZ = ClipLinear(poly, sx, -sz, sz * ez - sx * ex);   // dx - dz >= 0
            List<Vector3> nearX = ClipLinear(poly, -sx, sz, sx * ex - sz * ez);   // dz - dx >= 0
            AddCoping(nearZ, wrap ? kx : kz, x0, x1, z0, z1);
            AddCoping(nearX, wrap ? kz : kx, x0, x1, z0, z1);
        }

        /// <summary>Parte do polígono (no plano XZ) onde a * x + b * z + c >= 0.</summary>
        static List<Vector3> ClipLinear(List<Vector3> poly, float a, float b, float c)
        {
            var output = new List<Vector3>(poly.Count + 2);
            for (int k = 0; k < poly.Count; k++)
            {
                Vector3 p = poly[k], q = poly[(k + 1) % poly.Count];
                float fp = a * p.x + b * p.z + c, fq = a * q.x + b * q.z + c;
                if (fp >= 0f) AddDistinct(output, p);
                if ((fp >= 0f) != (fq >= 0f)) AddDistinct(output, Vector3.Lerp(p, q, fp / (fp - fq)));
            }
            if (output.Count > 1 && (output[0] - output[output.Count - 1]).sqrMagnitude < 1e-10f) output.RemoveAt(output.Count - 1);
            return output;
        }

        /// <summary>
        /// Pedaço de capa com a UV da faixa do lado <paramref name="k"/>: U ao longo da borda (em
        /// metros, contínuo de uma célula para a outra) e V atravessando a faixa, de 0 no lado da
        /// água (onde a textura tem a aresta arredondada, embaixo da imagem) a 1 no lado do piso.
        /// Olhando de cima, (U, V) gira junto com a borda: a textura nunca fica espelhada.
        /// </summary>
        void AddCoping(List<Vector3> poly, int k, float x0, float x1, float z0, float z1)
        {
            if (poly.Count < 3) return;
            MeshBuilder mesh = _kit.Coping;
            int first = mesh.VertexCount;
            foreach (Vector3 p in poly)
            {
                float u, v;
                switch (k)
                {
                    case 0: u = p.x; v = p.z - z0; break;
                    case 1: u = -p.x; v = z1 - p.z; break;
                    case 2: u = -p.z; v = p.x - x0; break;
                    default: u = p.z; v = x1 - p.x; break;
                }
                // Um centímetro de folga em cada ponta de V: a amostra bilinear não pega a outra borda da imagem.
                v = Mathf.Lerp(0.01f, 0.99f, Mathf.Clamp01(v / CopingWidth));
                mesh.AddVertex(p, Vector3.up, new Vector2(u / CopingRepeat, v));
            }
            for (int i = 1; i + 1 < poly.Count; i++) mesh.AddTriangleFacing(first, first + i, first + i + 1, Vector3.up);
        }

        static void Clean(List<float> cuts, float min, float max)
        {
            for (int k = 0; k < cuts.Count; k++) cuts[k] = Mathf.Clamp(cuts[k], min, max);
            cuts.Sort();
            for (int k = cuts.Count - 1; k > 0; k--)
                if (cuts[k] - cuts[k - 1] < 1e-4f) cuts.RemoveAt(k);
        }

        // Face vertical da borda: onde a vizinha aberta (sem parede) é mais baixa.
        void BuildBorders(int x, int z)
        {
            float y = FloorTop(x, z);
            foreach (WallSide side in PoolSides)
            {
                if (HasWallBox(x, z, side) || !Data.Neighbor(x, z, side, out int nx, out int nz) || !OpenCell(nx, nz)) continue;
                float lower = FloorTop(nx, nz);
                if (lower >= y - 1e-3f) continue;

                Vector3 dir = SideDir(side);
                Vector3 center = new Vector3((x + 0.5f) * Cell, 0f, (z + 0.5f) * Cell) + dir * (Cell * 0.5f);
                Vector3 tangent = Mathf.Abs(dir.x) > 0.5f ? Vector3.forward : Vector3.right;
                Vector3 a = center - tangent * (Cell * 0.5f), b = center + tangent * (Cell * 0.5f);
                var look = new Look { Room = Data.Room[nx, nz] };
                Emit(Poly(a + Vector3.up * lower, a + Vector3.up * y, b + Vector3.up * y, b + Vector3.up * lower), dir, look);
            }
        }

        /// <summary>
        /// Poço redondo: deque de piso fundo (-2,0 m) com um furo de raio 1,6 m no meio, que desce
        /// até o fundo do poço (-6 m), escuro, com uma grade de ralo.
        /// </summary>
        void BuildRoundWell(int x, int z, float x0, float x1, float z0, float z1)
        {
            float cx = (x + 0.5f) * Cell, cz = (z + 0.5f) * Cell;
            float deck = RoundWellDeck, bottom = LevelData.DepthFloor(PoolDepth.Well);
            int room = Data.Room[x, z];

            // Ângulos do círculo e dos quatro cantos do retângulo: o deque e o cilindro usam os mesmos.
            var angles = new List<float>();
            const int sides = HalfCircleSegments * 2;
            for (int k = 0; k < sides; k++) angles.Add(Mathf.PI * 2f * k / sides);
            foreach (Vector2 corner in new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) })
            {
                float a = Mathf.Atan2(corner.y - cz, corner.x - cx);
                if (a < 0f) a += Mathf.PI * 2f;
                angles.Add(a);
            }
            angles.Sort();
            for (int k = angles.Count - 1; k > 0; k--)
                if (angles[k] - angles[k - 1] < 1e-4f) angles.RemoveAt(k);

            var deckLook = new Look { Room = room, Kind = Surface.Bottom, Curved = true };
            for (int k = 0; k < angles.Count; k++)
            {
                float a0 = angles[k], a1 = angles[(k + 1) % angles.Count];
                Vector3 i0 = new Vector3(cx + Mathf.Cos(a0) * RoundWellRadius, deck, cz + Mathf.Sin(a0) * RoundWellRadius);
                Vector3 i1 = new Vector3(cx + Mathf.Cos(a1) * RoundWellRadius, deck, cz + Mathf.Sin(a1) * RoundWellRadius);
                Vector3 o0 = RectPoint(cx, cz, a0, x0, x1, z0, z1, deck), o1 = RectPoint(cx, cz, a1, x0, x1, z0, z1, deck);
                Emit(Poly(i0, o0, o1, i1), Vector3.up, deckLook);
            }

            // Parede do furo, virada para dentro.
            bool accent = poolAccentMaterial != null;
            MeshBuilder wall = accent ? _kit.CurvedAccent : _kit.CurvedBottom;
            int first = wall.VertexCount;
            for (int k = 0; k <= angles.Count; k++)
            {
                float a = k < angles.Count ? angles[k] : angles[0] + Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = new Vector3(cx, 0f, cz) + radial * RoundWellRadius;
                float arc = a * RoundWellRadius;
                wall.AddVertex(p + Vector3.up * bottom, -radial, new Vector2(arc, bottom));
                wall.AddVertex(p + Vector3.up * deck, -radial, new Vector2(arc, deck));
            }
            for (int k = 0; k < angles.Count; k++)
            {
                int a = first + k * 2;
                float mid = (angles[k] + (k + 1 < angles.Count ? angles[k + 1] : angles[0] + Mathf.PI * 2f)) * 0.5f;
                var facing = -new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                wall.AddTriangleFacing(a, a + 1, a + 3, facing);
                wall.AddTriangleFacing(a, a + 3, a + 2, facing);

                if (poolCausticsMaterial == null) continue;
                float b0 = angles[k], b1 = k + 1 < angles.Count ? angles[k + 1] : angles[0];
                Vector3 n0 = -new Vector3(Mathf.Cos(b0), 0f, Mathf.Sin(b0)), n1 = -new Vector3(Mathf.Cos(b1), 0f, Mathf.Sin(b1));
                Vector3 p0 = new Vector3(cx, 0f, cz) - n0 * (RoundWellRadius - CausticsLift), p1 = new Vector3(cx, 0f, cz) - n1 * (RoundWellRadius - CausticsLift);
                _kit.Caustics.AddPolygon(Poly(p0 + Vector3.up * bottom, p0 + Vector3.up * deck, p1 + Vector3.up * deck, p1 + Vector3.up * bottom), facing);
            }

            // Fundo do furo (o colisor é a caixa do piso, em -6 m).
            var disc = new List<Vector3>();
            foreach (float a in angles) disc.Add(new Vector3(cx + Mathf.Cos(a) * RoundWellRadius, bottom, cz + Mathf.Sin(a) * RoundWellRadius));
            Emit(disc, Vector3.up, new Look { Room = room, Kind = accent ? Surface.Accent : Surface.Bottom });

            // Grade do ralo: barras de inox deitadas no fundo (enfeite, sem colisor).
            if (poolRailMaterial != null)
            {
                for (int k = -3; k <= 3; k++)
                    _kit.Grate.AddBox(new Vector3(cx + k * 0.1f, bottom + 0.012f, cz), new Vector3(0.03f, 0.02f, 0.7f), caps: true);
                _kit.Grate.AddBox(new Vector3(cx, bottom + 0.012f, cz - 0.35f), new Vector3(0.7f, 0.02f, 0.03f), caps: true);
                _kit.Grate.AddBox(new Vector3(cx, bottom + 0.012f, cz + 0.35f), new Vector3(0.7f, 0.02f, 0.03f), caps: true);
            }
        }

        // Ponto da borda do retângulo na direção do ângulo, a partir do centro.
        static Vector3 RectPoint(float cx, float cz, float angle, float x0, float x1, float z0, float z1, float y)
        {
            float dx = Mathf.Cos(angle), dz = Mathf.Sin(angle);
            float t = float.MaxValue;
            if (dx > 1e-6f) t = Mathf.Min(t, (x1 - cx) / dx);
            if (dx < -1e-6f) t = Mathf.Min(t, (x0 - cx) / dx);
            if (dz > 1e-6f) t = Mathf.Min(t, (z1 - cz) / dz);
            if (dz < -1e-6f) t = Mathf.Min(t, (z0 - cz) / dz);
            return new Vector3(cx + dx * t, y, cz + dz * t);
        }

        // ---------------------------------------------------------------- Escadas

        void BuildPoolStairs()
        {
            foreach (PoolStair stair in Data.Stairs)
            {
                if (!OpenCell(stair.X, stair.Z) || !Data.Neighbor(stair.X, stair.Z, stair.Side, out int ux, out int uz) || !OpenCell(ux, uz)) continue;

                bool well = Data.Depth[stair.X, stair.Z] == PoolDepth.Well;
                float upper = FloorTop(ux, uz);
                float foot = well ? LevelData.WellStairFoot : FloorTop(stair.X, stair.Z);
                int steps = Mathf.Max(1, Mathf.RoundToInt((upper - foot) / LevelData.StepRise));
                int boxes = well ? steps : steps - 1;
                // Na escada que entra no poço, um bloco maciço sob os degraus até 40 cm abaixo do último.
                float under = well ? foot - 0.4f : FloorTop(stair.X, stair.Z);

                Vector3 toUpper = SideDir(stair.Side);
                Vector3 across = Abs(Vector3.Cross(Vector3.up, toUpper));
                // O primeiro degrau começa na face da parede (com arco ou vão) ou na linha da grade.
                float start = HasWallBox(stair.X, stair.Z, stair.Side) ? HalfWall : 0f;
                Vector3 edge = new Vector3((stair.X + 0.5f) * Cell, 0f, (stair.Z + 0.5f) * Cell) + toUpper * (Cell * 0.5f - start);
                float halfWidth = Mathf.Min(stair.Width, Cell) * 0.5f;
                var look = new Look { Room = Data.Room[stair.X, stair.Z], Kind = Surface.Floor };

                for (int k = 1; k <= boxes; k++)
                {
                    float top = upper - k * LevelData.StepRise;
                    float s0 = StepRun * (k - 1), s1 = StepRun * k;
                    Vector3 back = edge - toUpper * s0, front = edge - toUpper * s1;
                    Vector3 center = (back + front) * 0.5f + Vector3.up * ((top + under) * 0.5f);
                    AddCollider(_kit.Colliders, center, across * (halfWidth * 2f) + Abs(toUpper) * StepRun + Vector3.up * (top - under));

                    Vector3 bl = back - across * halfWidth, br = back + across * halfWidth;
                    Vector3 fl = front - across * halfWidth, fr = front + across * halfWidth;
                    Vector3 up = Vector3.up;

                    // Piso do degrau.
                    Emit(Poly(bl + up * top, br + up * top, fr + up * top, fl + up * top), Vector3.up, look);
                    // Espelho: só o que o degrau seguinte não cobre.
                    float riserBottom = k < boxes ? top - LevelData.StepRise : under;
                    Emit(Poly(fl + up * riserBottom, fl + up * top, fr + up * top, fr + up * riserBottom), -toUpper, look);
                    // Laterais.
                    Emit(Poly(bl + up * under, bl + up * top, fl + up * top, fl + up * under), -across, look);
                    Emit(Poly(br + up * under, br + up * top, fr + up * top, fr + up * under), across, look);
                    // Embaixo, visto por quem nada no poço.
                    if (well) Emit(Poly(bl + up * under, br + up * under, fr + up * under, fl + up * under), Vector3.down, look);
                }

                PoolDepth lowerDepth = Data.Depth[stair.X, stair.Z];
                if (poolRailMaterial != null && (lowerDepth == PoolDepth.Deep || lowerDepth == PoolDepth.Well))
                    AddRails(edge, -toUpper, across, halfWidth, upper, foot, boxes, well ? foot - 0.4f : foot);
            }
        }

        // Corrimão de inox dos dois lados da escada: tubo de 5 cm a 0,9 m acima dos degraus,
        // continuando para dentro d'água, com um pé em cada ponta.
        void AddRails(Vector3 edge, Vector3 down, Vector3 across, float halfWidth, float upper, float foot, int boxes, float floorBelow)
        {
            const float radius = 0.025f, height = 0.9f;
            float slope = LevelData.StepRise / StepRun;
            float end = StepRun * boxes + 0.45f;
            for (int sideSign = -1; sideSign <= 1; sideSign += 2)
            {
                Vector3 offset = across * (sideSign * (halfWidth - 0.12f));
                Vector3 a = edge + offset + Vector3.up * (upper + height);
                Vector3 b = edge + offset + down * end + Vector3.up * (upper + height - end * slope);
                _kit.Rails.AddTube(a, b, radius, 8);

                Vector3 topPost = edge + offset + down * 0.12f;
                _kit.Rails.AddTube(topPost + Vector3.up * (upper - LevelData.StepRise), topPost + Vector3.up * (upper + height - 0.12f * slope), radius, 8);
                Vector3 lowPost = edge + offset + down * (end - 0.02f);
                float postBottom = end - 0.02f > StepRun * boxes ? floorBelow : foot;
                _kit.Rails.AddTube(lowPost + Vector3.up * postBottom, lowPost + Vector3.up * (upper + height - (end - 0.02f) * slope), radius, 8);
            }
        }

        // ---------------------------------------------------------------- Tetos

        void BuildPoolCeilings()
        {
            MeshBuilder mesh = _kit.Ceiling;
            for (int z = 0; z < Data.Height; z++)
                for (int x = 0; x < Data.Width; x++)
                {
                    if (!OpenCell(x, z) || RoomAt(x, z).Vault) continue;
                    float c = Data.Ceiling[x, z];
                    CellRect(x, z, out float x0, out float x1, out float z0, out float z1);
                    float cx = (x + 0.5f) * Cell, cz = (z + 0.5f) * Cell;

                    switch (Data.Sky[x, z])
                    {
                        case PoolSky.Full:
                            break; // o vidro é o teto (BuildSkylights)
                        case PoolSky.Round:
                            CeilingWithRoundHole(x0, x1, z0, z1, cx, cz, c);
                            break;
                        case PoolSky.None:
                            mesh.AddPolygon(Poly(new Vector3(x0, c, z0), new Vector3(x0, c, z1), new Vector3(x1, c, z1), new Vector3(x1, c, z0)), Vector3.down);
                            break;
                        default:
                            SkyHole(Data.Sky[x, z], cx, cz, x0, x1, z0, z1, out float hx0, out float hx1, out float hz0, out float hz1);
                            CeilingRect(x0, hx0, z0, z1, c);
                            CeilingRect(hx1, x1, z0, z1, c);
                            CeilingRect(hx0, hx1, z0, hz0, c);
                            CeilingRect(hx0, hx1, hz1, z1, c);
                            // Poço de luz: laterais de azulejo até o vidro, 1 m acima.
                            float t = c + ShaftHeight;
                            mesh.AddPolygon(Poly(new Vector3(hx0, c, hz0), new Vector3(hx0, t, hz0), new Vector3(hx0, t, hz1), new Vector3(hx0, c, hz1)), Vector3.right);
                            mesh.AddPolygon(Poly(new Vector3(hx1, c, hz0), new Vector3(hx1, t, hz0), new Vector3(hx1, t, hz1), new Vector3(hx1, c, hz1)), Vector3.left);
                            mesh.AddPolygon(Poly(new Vector3(hx0, c, hz0), new Vector3(hx0, t, hz0), new Vector3(hx1, t, hz0), new Vector3(hx1, c, hz0)), Vector3.forward);
                            mesh.AddPolygon(Poly(new Vector3(hx0, c, hz1), new Vector3(hx0, t, hz1), new Vector3(hx1, t, hz1), new Vector3(hx1, c, hz1)), Vector3.back);
                            break;
                    }

                    // Degrau do teto onde a sala vizinha (sem parede no meio) tem o teto mais baixo.
                    foreach (WallSide side in PoolSides)
                    {
                        if (HasWallBox(x, z, side) || !Data.Neighbor(x, z, side, out int nx, out int nz) || !OpenCell(nx, nz)) continue;
                        float other = RoomAt(nx, nz).Vault ? ClearHeight(nx, nz, LevelData.Opposite(side)) : Data.Ceiling[nx, nz];
                        if (other >= c - 1e-3f) continue;
                        Vector3 dir = SideDir(side);
                        Vector3 center = new Vector3(cx, 0f, cz) + dir * (Cell * 0.5f);
                        Vector3 tangent = Mathf.Abs(dir.x) > 0.5f ? Vector3.forward : Vector3.right;
                        Vector3 a = center - tangent * (Cell * 0.5f), b = center + tangent * (Cell * 0.5f);
                        mesh.AddPolygon(Poly(a + Vector3.up * other, a + Vector3.up * c, b + Vector3.up * c, b + Vector3.up * other), -dir);
                    }
                }

            foreach (PoolRoom room in Data.Rooms)
                if (room.Vault) BuildVault(room);
        }

        void CeilingRect(float x0, float x1, float z0, float z1, float y)
        {
            if (x1 - x0 < 1e-4f || z1 - z0 < 1e-4f) return;
            _kit.Ceiling.AddPolygon(Poly(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0)), Vector3.down);
        }

        static void SkyHole(PoolSky sky, float cx, float cz, float x0, float x1, float z0, float z1,
                            out float hx0, out float hx1, out float hz0, out float hz1)
        {
            float hx, hz;
            switch (sky)
            {
                case PoolSky.SlotX: hx = 1.8f; hz = 0.6f; break;
                case PoolSky.SlotZ: hx = 0.6f; hz = 1.8f; break;
                case PoolSky.Round: hx = hz = 1.4f; break;
                default: hx = hz = 1.2f; break;
            }
            hx0 = Mathf.Max(x0, cx - hx); hx1 = Mathf.Min(x1, cx + hx);
            hz0 = Mathf.Max(z0, cz - hz); hz1 = Mathf.Min(z1, cz + hz);
        }

        void CeilingWithRoundHole(float x0, float x1, float z0, float z1, float cx, float cz, float c)
        {
            const float radius = 1.4f;
            var angles = new List<float>();
            const int sides = HalfCircleSegments * 2;
            for (int k = 0; k < sides; k++) angles.Add(Mathf.PI * 2f * k / sides);
            foreach (Vector2 corner in new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) })
            {
                float a = Mathf.Atan2(corner.y - cz, corner.x - cx);
                if (a < 0f) a += Mathf.PI * 2f;
                angles.Add(a);
            }
            angles.Sort();
            for (int k = angles.Count - 1; k > 0; k--)
                if (angles[k] - angles[k - 1] < 1e-4f) angles.RemoveAt(k);

            MeshBuilder mesh = _kit.Ceiling;
            for (int k = 0; k < angles.Count; k++)
            {
                float a0 = angles[k], a1 = angles[(k + 1) % angles.Count];
                Vector3 i0 = new Vector3(cx + Mathf.Cos(a0) * radius, c, cz + Mathf.Sin(a0) * radius);
                Vector3 i1 = new Vector3(cx + Mathf.Cos(a1) * radius, c, cz + Mathf.Sin(a1) * radius);
                mesh.AddPolygon(Poly(i0, RectPoint(cx, cz, a0, x0, x1, z0, z1, c), RectPoint(cx, cz, a1, x0, x1, z0, z1, c), i1), Vector3.down);
            }

            // Poço de luz redondo.
            int first = mesh.VertexCount;
            float top = c + ShaftHeight;
            for (int k = 0; k <= angles.Count; k++)
            {
                float a = k < angles.Count ? angles[k] : angles[0] + Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = new Vector3(cx, 0f, cz) + radial * radius;
                mesh.AddVertex(p + Vector3.up * c, -radial, new Vector2(a * radius, c));
                mesh.AddVertex(p + Vector3.up * top, -radial, new Vector2(a * radius, top));
            }
            for (int k = 0; k < angles.Count; k++)
            {
                int i = first + k * 2;
                float mid = (angles[k] + (k + 1 < angles.Count ? angles[k + 1] : angles[0] + Mathf.PI * 2f)) * 0.5f;
                var facing = -new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                mesh.AddTriangleFacing(i, i + 1, i + 3, facing);
                mesh.AddTriangleFacing(i, i + 3, i + 2, facing);
            }
        }

        /// <summary>Abóbada de berço: meio cilindro na largura livre da sala, arrancando do teto da sala.</summary>
        void BuildVault(PoolRoom room)
        {
            bool alongX = room.VaultAlongX;
            float a0 = (alongX ? room.X0 : room.Z0) * Cell + HalfWall;
            float a1 = (alongX ? room.X0 + room.Width : room.Z0 + room.Height) * Cell - HalfWall;
            float c0 = (alongX ? room.Z0 : room.X0) * Cell + HalfWall;
            float c1 = (alongX ? room.Z0 + room.Height : room.X0 + room.Width) * Cell - HalfWall;
            float radius = (c1 - c0) * 0.5f, middle = (c0 + c1) * 0.5f, spring = room.Ceiling;
            float length = Mathf.PI * radius;

            Vector3 Point(float axis, float cross, float y) => alongX ? new Vector3(axis, y, cross) : new Vector3(cross, y, axis);
            Vector3 crossAxis = alongX ? Vector3.forward : Vector3.right;

            MeshBuilder mesh = _kit.Ceiling;
            int first = mesh.VertexCount;
            for (int k = 0; k <= VaultSegments; k++)
            {
                float t = Mathf.PI * k / VaultSegments;
                float cross = middle + radius * Mathf.Cos(t), y = spring + radius * Mathf.Sin(t);
                Vector3 normal = -(crossAxis * Mathf.Cos(t) + Vector3.up * Mathf.Sin(t));
                float arc = radius * t;
                float v = spring + Mathf.Min(arc, length - arc);
                mesh.AddVertex(Point(a0, cross, y), normal, new Vector2(a0, v));
                mesh.AddVertex(Point(a1, cross, y), normal, new Vector2(a1, v));
            }
            for (int k = 0; k < VaultSegments; k++)
            {
                int i = first + k * 2;
                float t = Mathf.PI * (k + 0.5f) / VaultSegments;
                Vector3 facing = -(crossAxis * Mathf.Cos(t) + Vector3.up * Mathf.Sin(t));
                mesh.AddTriangleFacing(i, i + 1, i + 3, facing);
                mesh.AddTriangleFacing(i, i + 3, i + 2, facing);
            }
        }

        // ---------------------------------------------------------------- Sanca

        /// <summary>
        /// Quarto de círculo de 0,4 m no encontro da parede com o teto plano, em volta de cada sala.
        /// Nos cantos de dentro as duas tiras se cruzam; onde a parede acaba (passagem para outra
        /// sala) ou um vão sobe até a sanca, a tira termina com uma tampa.
        /// </summary>
        void BuildCoves()
        {
            foreach (PoolRoom room in Data.Rooms)
            {
                if (room.Vault || !OpenCell(room.X0, room.Z0)) continue;
                for (int k = 0; k < 4; k++) CoveSide(room, PoolSides[k]);
            }
        }

        void CoveSide(PoolRoom room, WallSide side)
        {
            bool alongX = side == WallSide.South || side == WallSide.North;
            int count = alongX ? room.Width : room.Height;
            float c = Data.Ceiling[room.X0, room.Z0];
            float origin = (alongX ? room.X0 : room.Z0) * Cell;

            // Linha da grade da parede e para onde fica a sala.
            int line = side == WallSide.South ? room.Z0 : side == WallSide.North ? room.Z0 + room.Height
                     : side == WallSide.West ? room.X0 : room.X0 + room.Width;
            float sign = side == WallSide.South || side == WallSide.West ? 1f : -1f;
            float plane = line * Cell + sign * HalfWall;

            bool Walled(int t)
            {
                if (alongX) return t >= 0 && t < Data.Width && _kit.WallS[t, line];
                return t >= 0 && t < Data.Height && _kit.WallW[line, t];
            }

            // A parede perpendicular no canto (lado oeste/sul da sala no começo, leste/norte no fim).
            bool CornerWalled(bool start)
            {
                if (alongX)
                {
                    int x = start ? room.X0 : room.X0 + room.Width - 1;
                    int z = side == WallSide.South ? room.Z0 : room.Z0 + room.Height - 1;
                    return start ? _kit.WallW[x, z] : _kit.WallW[x + 1, z];
                }
                else
                {
                    int z = start ? room.Z0 : room.Z0 + room.Height - 1;
                    int x = side == WallSide.West ? room.X0 : room.X0 + room.Width - 1;
                    return start ? _kit.WallS[x, z] : _kit.WallS[x, z + 1];
                }
            }

            int first0 = alongX ? room.X0 : room.Z0;
            int t0 = 0;
            while (t0 < count)
            {
                if (!Walled(first0 + t0)) { t0++; continue; }
                int t1 = t0;
                while (t1 + 1 < count && Walled(first0 + t1 + 1)) t1++;

                float a = origin + t0 * Cell, b = origin + (t1 + 1) * Cell;
                bool capA = true, capB = true;
                if (t0 == 0 && CornerWalled(true)) { a += HalfWall; capA = false; }
                else if (t0 == 0 && Walled(first0 - 1)) { }
                else a -= HalfWall;
                if (t1 == count - 1 && CornerWalled(false)) { b -= HalfWall; capB = false; }
                else if (t1 == count - 1 && Walled(first0 + count)) { }
                else b += HalfWall;

                // Vãos que sobem até a sanca a interrompem.
                var cuts = new List<Vector2>();
                for (int t = t0; t <= t1; t++)
                {
                    int index = first0 + t;
                    Segment s = alongX ? FindSegment(true, index, line) : FindSegment(false, line, index);
                    if (s == null) continue;
                    // De que lado da parede fica esta sala: A (sul/oeste da linha) ou B.
                    int roomSide = sign > 0 ? 1 : -1;
                    float centerAlong = origin + t * Cell + Cell * 0.5f;
                    foreach (Hole hole in s.Holes)
                    {
                        if (s.Lit != 0 && s.Lit != roomSide) continue;
                        if (hole.Yt <= c - CoveRadius - 0.01f) continue;
                        cuts.Add(new Vector2(centerAlong + hole.U0, centerAlong + hole.U1));
                    }
                }
                cuts.Sort((p, q) => p.x.CompareTo(q.x));

                float from = a;
                bool capFrom = capA;
                foreach (Vector2 cut in cuts)
                {
                    CoveStrip(alongX, plane, sign, from, cut.x, c, capFrom, true);
                    from = cut.y;
                    capFrom = true;
                }
                CoveStrip(alongX, plane, sign, from, b, c, capFrom, capB);

                t0 = t1 + 1;
            }
        }

        Segment FindSegment(bool alongX, int i, int j)
        {
            return _kit.Segments.TryGetValue(SegmentKey(alongX, i, j), out Segment s) ? s : null;
        }

        void CoveStrip(bool alongX, float plane, float sign, float a, float b, float c, bool capA, bool capB)
        {
            if (b - a < 1e-3f) return;
            Vector3 alongAxis = alongX ? Vector3.right : Vector3.forward;
            Vector3 inward = (alongX ? Vector3.forward : Vector3.right) * sign;
            Vector3 Base(float along) => alongX ? new Vector3(along, 0f, plane) : new Vector3(plane, 0f, along);

            MeshBuilder mesh = _kit.Cove;
            int first = mesh.VertexCount;
            for (int k = 0; k <= CoveSegments; k++)
            {
                float t = Mathf.PI * 0.5f * k / CoveSegments;
                float w = CoveRadius * (1f - Mathf.Cos(t)), y = c - CoveRadius + CoveRadius * Mathf.Sin(t);
                Vector3 normal = inward * Mathf.Cos(t) - Vector3.up * Mathf.Sin(t);
                float v = c - CoveRadius + CoveRadius * t;
                mesh.AddVertex(Base(a) + inward * w + Vector3.up * y, normal, new Vector2(a, v));
                mesh.AddVertex(Base(b) + inward * w + Vector3.up * y, normal, new Vector2(b, v));
            }
            for (int k = 0; k < CoveSegments; k++)
            {
                int i = first + k * 2;
                float t = Mathf.PI * 0.5f * (k + 0.5f) / CoveSegments;
                Vector3 facing = inward * Mathf.Cos(t) - Vector3.up * Mathf.Sin(t);
                mesh.AddTriangleFacing(i, i + 1, i + 3, facing);
                mesh.AddTriangleFacing(i, i + 3, i + 2, facing);
            }

            if (capA) CoveCap(Base(a), inward, -alongAxis, c);
            if (capB) CoveCap(Base(b), inward, alongAxis, c);
        }

        void CoveCap(Vector3 basePoint, Vector3 inward, Vector3 normal, float c)
        {
            var points = new List<Vector3> { basePoint + Vector3.up * c };
            for (int k = 0; k <= CoveSegments; k++)
            {
                float t = Mathf.PI * 0.5f * k / CoveSegments;
                points.Add(basePoint + inward * (CoveRadius * (1f - Mathf.Cos(t))) + Vector3.up * (c - CoveRadius + CoveRadius * Mathf.Sin(t)));
            }
            _kit.Cove.AddPolygon(points, normal);
        }

        // ---------------------------------------------------------------- Colunas

        void BuildColumns()
        {
            foreach (PoolColumn column in Data.Columns)
            {
                float bottom = float.MaxValue, top = float.MinValue;
                int room = -1;
                int cx0 = Mathf.FloorToInt((column.X - column.Radius) / Cell), cx1 = Mathf.FloorToInt((column.X + column.Radius - 1e-4f) / Cell);
                int cz0 = Mathf.FloorToInt((column.Z - column.Radius) / Cell), cz1 = Mathf.FloorToInt((column.Z + column.Radius - 1e-4f) / Cell);
                for (int z = cz0; z <= cz1; z++)
                    for (int x = cx0; x <= cx1; x++)
                    {
                        if (!OpenCell(x, z)) continue;
                        bottom = Mathf.Min(bottom, FloorTop(x, z));
                        top = Mathf.Max(top, Data.Ceiling[x, z]);
                        if (room < 0) room = Data.Room[x, z];
                    }
                if (room < 0 || top <= bottom) continue;

                var center = new Vector3(column.X, 0f, column.Z);
                if (!column.Round)
                {
                    float r = column.Radius;
                    var look = new Look { Room = room };
                    AddCollider(_kit.Colliders, center + Vector3.up * ((bottom + top) * 0.5f), new Vector3(r * 2f, top - bottom, r * 2f));
                    Vector3[] corners = { new Vector3(-r, 0f, -r), new Vector3(r, 0f, -r), new Vector3(r, 0f, r), new Vector3(-r, 0f, r) };
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 p0 = center + corners[k], p1 = center + corners[(k + 1) % 4];
                        Vector3 normal = Vector3.Cross(p1 - p0, Vector3.up).normalized * -1f;
                        if (Vector3.Dot(normal, (p0 + p1) * 0.5f - center) < 0f) normal = -normal;
                        Emit(Poly(p0 + Vector3.up * bottom, p0 + Vector3.up * top, p1 + Vector3.up * top, p1 + Vector3.up * bottom), normal, look);
                    }
                    continue;
                }

                // Coluna redonda: malha com colisor dela mesma.
                MeshBuilder mesh = _kit.CurvedWall;
                int first = mesh.VertexCount;
                float around = 2f * Mathf.PI * column.Radius;
                for (int k = 0; k <= ColumnSides; k++)
                {
                    float t = Mathf.PI * 2f * k / ColumnSides;
                    var radial = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
                    Vector3 p = center + radial * column.Radius;
                    float u = around * k / ColumnSides;
                    mesh.AddVertex(p + Vector3.up * bottom, radial, new Vector2(u, bottom));
                    mesh.AddVertex(p + Vector3.up * top, radial, new Vector2(u, top));
                }
                for (int k = 0; k < ColumnSides; k++)
                {
                    int i = first + k * 2;
                    float t = Mathf.PI * 2f * (k + 0.5f) / ColumnSides;
                    var facing = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
                    mesh.AddTriangleFacing(i, i + 1, i + 3, facing);
                    mesh.AddTriangleFacing(i, i + 3, i + 2, facing);

                    if (poolCausticsMaterial == null || bottom >= WaterY) continue;
                    float ta = Mathf.PI * 2f * k / ColumnSides, tb = Mathf.PI * 2f * (k + 1) / ColumnSides;
                    float rr = column.Radius + CausticsLift;
                    Vector3 pa = center + new Vector3(Mathf.Cos(ta), 0f, Mathf.Sin(ta)) * rr, pb = center + new Vector3(Mathf.Cos(tb), 0f, Mathf.Sin(tb)) * rr;
                    float wet = Mathf.Min(WaterY, top);
                    _kit.Caustics.AddPolygon(Poly(pa + Vector3.up * bottom, pa + Vector3.up * wet, pb + Vector3.up * wet, pb + Vector3.up * bottom), facing);
                }
            }
        }

        // ---------------------------------------------------------------- Água

        // Lâmina vista de cima e de baixo sobre todas as células com piso. A malha fica em Y = 0 do
        // objeto "Agua", que sobe até LevelData.WaterLevel (CreatePoolRenderers): a altura da
        // superfície é a do objeto (PoolWater.SurfaceY), e a maré (PoolTide) só o move. Sobre o seco
        // a lâmina fica escondida sob o piso enquanto a água está baixa e aparece na maré alta.
        // Sem colisor: o jogador decide sozinho se anda, vadeia ou nada (PlayerController).
        void BuildPoolWater()
        {
            for (int z = 0; z < Data.Height; z++)
            {
                int runStart = -1;
                for (int x = 0; x <= Data.Width; x++)
                {
                    bool wet = x < Data.Width && OpenCell(x, z);
                    if (wet && runStart < 0) runStart = x;
                    if (wet || runStart < 0) continue;

                    float x0 = runStart * Cell, x1 = x * Cell, z0 = z * Cell, z1 = (z + 1) * Cell;
                    var quad = Poly(new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z0));
                    _kit.Water.AddPolygon(quad, Vector3.up);
                    _kit.Water.AddPolygon(quad, Vector3.down);
                    runStart = -1;
                }
            }
        }

        // ---------------------------------------------------------------- Malhas

        void CreatePoolRenderers()
        {
            Material wall = wallMaterial;
            Material floorMat = floorMaterial != null ? floorMaterial : wall;
            Material ceilingMat = ceilingMaterial != null ? ceilingMaterial : wall;
            Material bottom = poolBottomMaterial != null ? poolBottomMaterial : floorMat;
            Material accent = poolAccentMaterial != null ? poolAccentMaterial : bottom;

            PoolRenderer("Piscinas_Paredes", _kit.Wall, wall, false);
            PoolRenderer("Piscinas_Piso", _kit.Floor, floorMat, false);
            PoolRenderer("Piscinas_AzulejoGasto", _kit.Worn, poolWornTileMaterial != null ? poolWornTileMaterial : wall, false);
            PoolRenderer("Piscinas_Fundo", _kit.Bottom, bottom, false);
            PoolRenderer("Piscinas_Faixa", _kit.Accent, accent, false);
            PoolRenderer("Piscinas_Borda", _kit.Coping, copingMaterial != null ? copingMaterial : floorMat, false);
            PoolRenderer("Piscinas_Curvas", _kit.CurvedWall, wall, true);
            PoolRenderer("Piscinas_CurvasFaixa", _kit.CurvedAccent, accent, true);
            PoolRenderer("Piscinas_CurvasFundo", _kit.CurvedBottom, bottom, true);
            PoolRenderer("Piscinas_Teto", _kit.Ceiling, ceilingMat, true);
            PoolRenderer("Sanca", _kit.Cove, ceilingMat, false);

            if (poolCausticsMaterial != null && !_kit.Caustics.IsEmpty)
                CreateRenderer("Causticas", _kit.Caustics.ToMesh("Causticas"), poolCausticsMaterial, ShadowCastingMode.Off);
            if (poolRailMaterial != null && !_kit.Rails.IsEmpty)
                CreateRenderer("Corrimaos", _kit.Rails.ToMesh("Corrimaos"), poolRailMaterial, ShadowCastingMode.On);
            if (poolRailMaterial != null && !_kit.Grate.IsEmpty)
                CreateRenderer("Ralo", _kit.Grate.ToMesh("Ralo"), poolRailMaterial, ShadowCastingMode.Off);

            if (!_kit.Water.IsEmpty)
            {
                MeshRenderer water = CreateRenderer("Agua", _kit.Water.ToMesh("Agua"), waterMaterial, ShadowCastingMode.Off);
                water.transform.localPosition = new Vector3(0f, WaterY, 0f);
                water.gameObject.AddComponent<PoolWater>();
                // A maré, o ralo e a drenagem (só em Play).
                if (Application.isPlaying) water.gameObject.AddComponent<PoolTide>();
            }
        }

        // Peças da estrutura: sombra dos dois lados (o teto e as paredes só têm a face de dentro,
        // e as luzes das claraboias ficam acima do teto). Sombras em cache: custa uma vez só.
        void PoolRenderer(string objectName, MeshBuilder builder, Material material, bool meshCollider)
        {
            if (builder.IsEmpty) return;
            Mesh mesh = builder.ToMesh(objectName);
            MeshRenderer renderer = CreateRenderer(objectName, mesh, material, ShadowCastingMode.TwoSided);
            if (meshCollider) renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        // ---------------------------------------------------------------- Manchas

        /// <summary>
        /// Poucas manchas (decalques), só longe do início e nunca na sala inicial nem na da saída:
        /// limo na linha d'água, escorrido de calcário do alto da parede, mofo no rejunte (salas
        /// escuras, cantos sem claraboia) e ferrugem embaixo das lâmpadas submersas. No máximo uma
        /// a cada <see cref="CellsPerStain"/> células com piso e <see cref="MaxStains"/> no mapa (as
        /// referências quase não têm sujeira). Só em parede cheia (nada sobre janela, vão ou vigia),
        /// longe de escada, coluna e lâmpada.
        /// </summary>
        void BuildPoolStains()
        {
            if (poolStainDecals == null || poolStainDecals.Length == 0) return;
            var rng = new System.Random(Data.Seed * 7919 + 37);
            Material Stain(int index) => index < poolStainDecals.Length ? poolStainDecals[index] : null;
            List<Bounds> obstacles = DecalObstacles();

            var roomSky = new bool[Data.Rooms.Count];
            int floorCells = 0;
            for (int z = 0; z < Data.Height; z++)
                for (int x = 0; x < Data.Width; x++)
                {
                    if (!OpenCell(x, z)) continue;
                    floorCells++;
                    if (Data.Sky[x, z] != PoolSky.None) roomSky[Data.Room[x, z]] = true;
                }
            int budget = Mathf.Min(floorCells / CellsPerStain, MaxStains);
            if (budget <= 0) return;

            var slime = new List<(int x, int z, WallSide side)>();
            var lime = new List<(int x, int z, WallSide side)>();
            var mould = new List<(int x, int z, WallSide side)>();
            var mouldCeiling = new List<Vector2Int>();
            for (int z = 0; z < Data.Height; z++)
                for (int x = 0; x < Data.Width; x++)
                {
                    if (!StainAllowed(x, z)) continue;
                    PoolRoom room = RoomAt(x, z);
                    bool shaded = room.Dark || Data.Sky[x, z] == PoolSky.None;
                    foreach (WallSide side in PoolSides)
                    {
                        if (!PlainWall(x, z, side)) continue;
                        if (FloorTop(x, z) < WaterY && Data.Depth[x, z] != PoolDepth.Well) slime.Add((x, z, side));
                        if (!room.Vault && roomSky[room.Id]) lime.Add((x, z, side));
                        if (shaded) mould.Add((x, z, side));
                    }
                    if (shaded && !room.Vault && Data.Sky[x, z] == PoolSky.None) mouldCeiling.Add(new Vector2Int(x, z));
                }

            Transform root = null;
            Transform Root() => root != null ? root : (root = CreateChild("Manchas").transform);

            // Ferrugem: embaixo de algumas lâmpadas submersas, escorrendo da borda do disco até no
            // máximo o piso.
            if (Stain(3) != null)
                foreach (PoolLamp lamp in Data.Lamps)
                {
                    bool roll = rng.NextDouble() < 0.6;
                    if (budget <= 0 || !roll || _kit.StainCount[4] >= MaxRust || !StainAllowed(lamp.X, lamp.Z)) continue;
                    Segment s = SideSegment(lamp.X, lamp.Z, lamp.Side);
                    if (s != null && s.Holes.Count > 0) continue;
                    if (_kit.DecalFaces.Contains(FaceKey(lamp.X, lamp.Z, lamp.Side))) continue;
                    float inset = HasWallBox(lamp.X, lamp.Z, lamp.Side) ? HalfWall : 0f;
                    // O topo fica logo abaixo da caixa da lâmpada (0,16 m) mais a folga do PlaceDecal.
                    float top = lamp.Y - 0.22f, bottom = Mathf.Max(top - 0.9f, FloorTop(lamp.X, lamp.Z) + 0.02f);
                    if (top - bottom < 0.3f) continue;
                    if (WallDecal(Root(), Stain(3), lamp.X, lamp.Z, lamp.Side, inset, 0f, (top + bottom) * 0.5f, new Vector2(0.6f, top - bottom), false, obstacles, "ferrugem"))
                    {
                        _kit.StainCount[4]++;
                        budget--;
                    }
                }

            // Os outros três tipos em rodízio, cada um sorteando entre os seus lugares.
            for (int turn = 0; budget > 0 && turn < 300; turn++)
            {
                int type = turn % 4;
                bool flip = rng.Next(2) == 0;
                float r0 = (float)rng.NextDouble(), r1 = (float)rng.NextDouble();

                if (type == 3)
                {
                    if (Stain(2) == null || mouldCeiling.Count == 0) continue;
                    int pick = rng.Next(mouldCeiling.Count);
                    Vector2Int cell = mouldCeiling[pick];
                    mouldCeiling.RemoveAt(pick);
                    float c = Data.Ceiling[cell.x, cell.y];
                    // Linhas do mofo sobre o rejunte do teto. O decalque olha para cima com o topo
                    // da textura em +Z, então a direita dela é -X (ou +X, espelhado).
                    float size = MouldSize, tile = poolTileMeters / 6f;
                    float lineX = Mathf.Round(((cell.x + 0.5f) * Cell + Mathf.Lerp(-1.2f, 1.2f, r0)) / tile) * tile;
                    float lineZ = Mathf.Round(((cell.y + 0.5f) * Cell + Mathf.Lerp(-1.2f, 1.2f, r1)) / tile) * tile;
                    var position = new Vector3(lineX + MouldLineRight * size * (flip ? -1f : 1f), c - 0.06f, lineZ - MouldLineUp * size);
                    if (PlaceDecal(Root(), Stain(2), position, Quaternion.LookRotation(Vector3.up, Vector3.forward), new Vector3(size, size, 0.16f),
                                   flip, obstacles, $"mofo no teto ({cell.x},{cell.y})"))
                    {
                        _kit.StainCount[3]++;
                        budget--;
                    }
                    continue;
                }

                List<(int x, int z, WallSide side)> list = type == 0 ? slime : type == 1 ? lime : mould;
                Material material = Stain(type);
                if (material == null || list.Count == 0)
                {
                    if (slime.Count + lime.Count + mould.Count + mouldCeiling.Count == 0) break;
                    continue;
                }

                int index = rng.Next(list.Count);
                var (x, z, side) = list[index];
                list.RemoveAt(index);
                if (_kit.DecalFaces.Contains(FaceKey(x, z, side))) continue;

                bool placed;
                switch (type)
                {
                    case 0:
                        // Faixa de limo: a linha escura da textura (62% abaixo do topo) na linha d'água.
                        placed = WallDecal(Root(), material, x, z, side, HalfWall, Mathf.Round(Mathf.Lerp(-0.6f, 0.6f, r0) / 0.2f) * 0.2f,
                                           WaterY + 0.62f * 0.5f - 0.25f, new Vector2(2.4f, 0.5f), flip, obstacles, "limo");
                        break;
                    case 1:
                        // Calcário: o topo da textura (que não esmaece) colado no começo da sanca,
                        // escorrendo para baixo. Acima dele a sanca curva, onde o decalque esticaria.
                        placed = WallDecal(Root(), material, x, z, side, HalfWall, Mathf.Lerp(-1f, 1f, r0),
                                           Data.Ceiling[x, z] - CoveRadius - 0.8f, new Vector2(1.6f, 1.6f), flip, obstacles, "calcário");
                        break;
                    default:
                    {
                        // Mofo: as linhas da textura sobre o rejunte do azulejo (UV do mundo, um
                        // azulejo = poolTileMeters / 6). O meio da face cai numa junta (2 m = 15 azulejos).
                        float size = MouldSize, tile = poolTileMeters / 6f;
                        Vector3 dir = SideDir(side);
                        Vector3 tangent = Mathf.Abs(dir.x) > 0.5f ? Vector3.forward : Vector3.right;
                        float rightSign = Mathf.Sign(Vector3.Dot(Vector3.Cross(Vector3.up, dir), tangent)) * (flip ? -1f : 1f);
                        float lineY = Mathf.Round((Mathf.Max(FloorTop(x, z), WaterY) + 0.8f + r1 * 1.2f) / tile) * tile;
                        float lineAlong = Mathf.Round(Mathf.Lerp(-1.2f, 1.2f, r0) / tile) * tile;
                        placed = WallDecal(Root(), material, x, z, side, HalfWall, lineAlong - rightSign * MouldLineRight * size,
                                           lineY - MouldLineUp * size, new Vector2(size, size), flip, obstacles, "mofo");
                        break;
                    }
                }
                if (!placed) continue;
                _kit.StainCount[type]++;
                budget--;
            }
        }

        bool StainAllowed(int x, int z)
        {
            if (!OpenCell(x, z) || LevelGenerator.WearAt(Data, x, z) < 0.5f) return false;
            PoolRoomKind kind = RoomAt(x, z).Kind;
            return kind != PoolRoomKind.Start && kind != PoolRoomKind.Exit;
        }

        Segment SideSegment(int x, int z, WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return FindSegment(true, x, z);
                case WallSide.North: return FindSegment(true, x, z + 1);
                case WallSide.West: return FindSegment(false, x, z);
                default: return FindSegment(false, x + 1, z);
            }
        }

        // Parede cheia (sem abertura nenhuma) daquele lado da célula, e não é a da porta de saída.
        bool PlainWall(int x, int z, WallSide side)
        {
            Segment s = SideSegment(x, z, side);
            if (s == null || s.Holes.Count > 0) return false;
            return !(x == Data.ExitX && z == Data.ExitZ && side == Data.ExitSide);
        }

        int FaceKey(int x, int z, WallSide side) => (x + z * Data.Width) * 4 + (int)side;

        /// <summary>
        /// Decalque na face da parede daquele lado da célula, olhando para dentro dela. Recusa (e
        /// devolve falso) se a face já tem decalque, se ele sairia do piso à sanca (ou ao arranque
        /// da abóbada) ou se encosta numa escada, coluna, lâmpada ou outro decalque.
        /// </summary>
        bool WallDecal(Transform root, Material material, int x, int z, WallSide side, float inset, float along, float centerY, Vector2 size, bool flip,
                       List<Bounds> obstacles, string label)
        {
            int key = FaceKey(x, z, side);
            if (_kit.DecalFaces.Contains(key)) return false;
            float top = centerY + size.y * 0.5f, bottom = centerY - size.y * 0.5f;
            float roof = ClearHeight(x, z, side) - (RoomAt(x, z).Vault ? 0f : CoveRadius);
            if (top > roof + 1e-3f || bottom < FloorTop(x, z) - 1e-3f) return false;
            if (Mathf.Abs(along) + size.x * 0.5f > SegmentHalf + 1e-3f) return false;

            Vector3 dir = SideDir(side);
            Vector3 normal = -dir;
            Vector3 tangent = Mathf.Abs(dir.x) > 0.5f ? Vector3.forward : Vector3.right;
            Vector3 face = new Vector3((x + 0.5f) * Cell, 0f, (z + 0.5f) * Cell) + dir * (Cell * 0.5f - inset);
            Vector3 position = face + normal * 0.06f + tangent * along + Vector3.up * centerY;
            if (!PlaceDecal(root, material, position, Quaternion.LookRotation(-normal, Vector3.up), new Vector3(size.x, size.y, 0.16f), flip, obstacles,
                            $"{label} ({x},{z},{side})"))
                return false;
            _kit.DecalFaces.Add(key);
            return true;
        }

        /// <summary>Põe o decalque se a caixa dele (com 5 cm de folga) não encosta em nenhum obstáculo; ele vira obstáculo.</summary>
        bool PlaceDecal(Transform root, Material material, Vector3 position, Quaternion rotation, Vector3 size, bool flip, List<Bounds> obstacles, string label)
        {
            if (material == null) return false;
            Bounds box = DecalBox(position, rotation, size);
            Bounds padded = box;
            padded.Expand(0.1f);
            foreach (Bounds other in obstacles)
                if (padded.Intersects(other)) return false;
            foreach (Bounds other in _kit.Decals)
                if (padded.Intersects(other)) return false;

            GameObject decal = AddDecal(root, material, position, rotation, size, decalDrawDistance, flip);
            decal.name = material.name;
            _kit.Decals.Add(box);
            _kit.DecalLog.Add(label);
            return true;
        }

        // A caixa do DecalProjector no espaço do nível (o pivô fica na face da frente: a caixa vai para trás).
        static Bounds DecalBox(Vector3 position, Quaternion rotation, Vector3 size)
        {
            Vector3 center = position + rotation * new Vector3(0f, 0f, size.z * 0.5f);
            var box = new Bounds(center, Vector3.zero);
            for (int k = 0; k < 8; k++)
            {
                var corner = new Vector3((k & 1) == 0 ? -0.5f : 0.5f, (k & 2) == 0 ? -0.5f : 0.5f, (k & 4) == 0 ? -0.5f : 0.5f);
                box.Encapsulate(center + rotation * Vector3.Scale(corner, size));
            }
            return box;
        }

        /// <summary>
        /// O que um decalque não pode cobrir: escadas (do pé dos degraus e dos corrimãos até 0,8 m
        /// para dentro do patamar de cima), colunas e lâmpadas submersas. Na ordem de Data.
        /// </summary>
        List<Bounds> DecalObstacles()
        {
            var list = new List<Bounds>();
            foreach (PoolStair stair in Data.Stairs)
            {
                if (!OpenCell(stair.X, stair.Z) || !Data.Neighbor(stair.X, stair.Z, stair.Side, out int ux, out int uz) || !OpenCell(ux, uz)) continue;
                bool well = Data.Depth[stair.X, stair.Z] == PoolDepth.Well;
                float upper = FloorTop(ux, uz);
                float foot = well ? LevelData.WellStairFoot : FloorTop(stair.X, stair.Z);
                int steps = Mathf.Max(1, Mathf.RoundToInt((upper - foot) / LevelData.StepRise));
                int boxes = well ? steps : steps - 1;
                Vector3 toUpper = SideDir(stair.Side);
                Vector3 across = Abs(Vector3.Cross(Vector3.up, toUpper));
                float start = HasWallBox(stair.X, stair.Z, stair.Side) ? HalfWall : 0f;
                Vector3 edge = new Vector3((stair.X + 0.5f) * Cell, 0f, (stair.Z + 0.5f) * Cell) + toUpper * (Cell * 0.5f - start);
                float halfWidth = Mathf.Min(stair.Width, Cell) * 0.5f;
                Vector3 far = edge - toUpper * (StepRun * boxes + 0.5f), near = edge + toUpper * 0.8f;
                var box = new Bounds((far + near) * 0.5f + Vector3.up * ((foot - 0.5f + upper + 1.2f) * 0.5f), Vector3.zero);
                box.size = across * (halfWidth * 2f + 0.1f) + Abs(near - far) + Vector3.up * (upper + 1.2f - foot + 0.5f);
                list.Add(box);
            }
            foreach (PoolColumn column in Data.Columns)
                list.Add(new Bounds(new Vector3(column.X, 0f, column.Z), new Vector3(column.Radius * 2f + 0.1f, 40f, column.Radius * 2f + 0.1f)));
            foreach (PoolLamp lamp in Data.Lamps)
            {
                if (!OpenCell(lamp.X, lamp.Z)) continue;
                Vector3 dir = SideDir(lamp.Side);
                float inset = HasWallBox(lamp.X, lamp.Z, lamp.Side) ? HalfWall : 0f;
                Vector3 face = new Vector3((lamp.X + 0.5f) * Cell, lamp.Y, (lamp.Z + 0.5f) * Cell) + dir * (Cell * 0.5f - inset);
                list.Add(new Bounds(face, new Vector3(0.32f, 0.32f, 0.32f)));
            }
            return list;
        }

        // ---------------------------------------------------------------- Marcações

        // Largura de cada marcação, em metros, e a proporção largura/altura do desenho
        // (Art/Fontes/Level2/Mark_*.png; a de profundidade, "2.0 M", é Art/Level2/Mark_Depth_2m.png,
        // 923 x 480, remontada pelo Level2Setup com letras da mesma altura da "1.2 M" da fonte):
        // 0 profundidade, 1 proibido mergulhar, 2 DEEP END, 3 T de raia.
        static readonly float[] MarkWidth = { 0.76f, 0.7f, 1.1f, 1.0f };
        static readonly float[] MarkAspect = { 1.923f, 0.928f, 2.482f, 0.704f };
        static readonly string[] MarkLabel = { "2.0 M", "NO DIVING", "DEEP END", "T de raia" };
        const int MaxDepthMarks = 6;
        const int MaxNoDivingMarks = 4;
        // Distância do pé dos desenhos de piso à borda (2.0 M: logo depois da capa) e do T à ponta da raia.
        const float DepthMarkGap = CopingWidth + 0.05f;
        const float LaneMarkGap = 0.6f;

        static float MarkHeight(int kind) => MarkWidth[kind] / MarkAspect[kind];

        /// <summary>
        /// Marcações pintadas, poucas e onde uma piscina de verdade as tem: "2.0 M" no deque seco
        /// rente à área funda (ou na parede logo acima da água de uma área funda), NO DIVING em
        /// parede cega de sala rasa com deque ou escada, DEEP END em parede cega de sala com poço e
        /// o T azul no fundo, nas duas pontas de uma raia funda. Nada sobre abertura, escada,
        /// coluna, lâmpada ou mancha (ver <see cref="PlaceDecal"/>). Só System.Random da seed.
        /// </summary>
        void BuildPoolMarks()
        {
            if (poolMarkDecals == null || poolMarkDecals.Length == 0) return;
            var rng = new System.Random(Data.Seed * 6151 + 11);
            Material Mark(int index) => index < poolMarkDecals.Length ? poolMarkDecals[index] : null;
            List<Bounds> obstacles = DecalObstacles();
            Transform root = null;
            Transform Root() => root != null ? root : (root = CreateChild("Marcacoes").transform);

            foreach (PoolRoom room in Data.Rooms)
            {
                if (!OpenCell(room.X0, room.Z0)) continue;
                PoolRoomKind kind = room.Kind;

                // Profundidade: 1 ou 2 por piscina, raia ou canal.
                if ((kind == PoolRoomKind.Basin || kind == PoolRoomKind.Lane || kind == PoolRoomKind.Canal) && Mark(0) != null)
                {
                    int want = 1 + rng.Next(2);
                    List<(int x, int z, WallSide side, bool deck)> spots = DepthMarkSpots(room);
                    Shuffle(spots, rng);
                    foreach (var spot in spots)
                    {
                        if (want <= 0 || _kit.MarkCount[0] >= MaxDepthMarks) break;
                        if (spot.deck ? DeckDepthMark(Root(), Mark(0), spot.x, spot.z, spot.side, obstacles)
                                      : WallDepthMark(Root(), Mark(0), spot.x, spot.z, spot.side, obstacles))
                        {
                            _kit.MarkCount[0]++;
                            want--;
                        }
                    }
                }

                // T de fim de raia, no fundo, nas duas pontas.
                if ((kind == PoolRoomKind.Basin || kind == PoolRoomKind.Lane || kind == PoolRoomKind.Canal) && Mark(3) != null)
                    LaneMarks(Root(), Mark(3), room, obstacles);

                // Proibido mergulhar: sala com água rasa junto de deque ou escada.
                bool noDivingKind = kind == PoolRoomKind.Start || kind == PoolRoomKind.Terrace || kind == PoolRoomKind.Hall || kind == PoolRoomKind.Basin;
                if (noDivingKind && Mark(1) != null && _kit.MarkCount[1] < MaxNoDivingMarks && ShallowBesideDeckOrStair(room))
                {
                    bool roll = kind == PoolRoomKind.Start || rng.NextDouble() < 0.7;
                    var spots = new List<(int x, int z, WallSide side)>();
                    ForWalls(room, (x, z, side) =>
                    {
                        if (Data.Depth[x, z] == PoolDepth.Dry || Data.Depth[x, z] == PoolDepth.Shallow) spots.Add((x, z, side));
                    });
                    Shuffle(spots, rng);
                    if (roll)
                        foreach (var (x, z, side) in spots)
                            if (WallMark(Root(), Mark(1), 1, x, z, side, Mathf.Max(FloorTop(x, z), WaterY) + 1.6f, obstacles))
                                break;
                }

                // DEEP END: parede cega de sala com poço, a mais perto do poço (entre as três mais perto).
                if (Mark(2) != null)
                {
                    var wells = new List<Vector2>();
                    for (int z = room.Z0; z < room.Z0 + room.Height; z++)
                        for (int x = room.X0; x < room.X0 + room.Width; x++)
                            if (OpenCell(x, z) && Data.Depth[x, z] == PoolDepth.Well) wells.Add(new Vector2(x, z));
                    if (wells.Count > 0)
                    {
                        var spots = new List<(int x, int z, WallSide side, float distance, int order)>();
                        ForWalls(room, (x, z, side) =>
                        {
                            float best = float.MaxValue;
                            foreach (Vector2 w in wells) best = Mathf.Min(best, (new Vector2(x, z) - w).sqrMagnitude);
                            spots.Add((x, z, side, best, spots.Count));
                        });
                        spots.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : a.order.CompareTo(b.order));
                        int first = spots.Count > 0 ? rng.Next(Mathf.Min(3, spots.Count)) : 0;
                        for (int k = 0; k < spots.Count; k++)
                        {
                            var (x, z, side, _, _) = spots[(first + k) % spots.Count];
                            if (WallMark(Root(), Mark(2), 2, x, z, side, Mathf.Max(FloorTop(x, z), WaterY) + 1.5f, obstacles)) break;
                        }
                    }
                }
            }
        }

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // Cada lado de parede cheia (sem abertura) das células da sala, na ordem z, x, lado.
        void ForWalls(PoolRoom room, System.Action<int, int, WallSide> action)
        {
            for (int z = room.Z0; z < room.Z0 + room.Height; z++)
                for (int x = room.X0; x < room.X0 + room.Width; x++)
                {
                    if (!OpenCell(x, z) || Data.Room[x, z] != room.Id) continue;
                    foreach (WallSide side in PoolSides)
                        if (HasWallBox(x, z, side) && PlainWall(x, z, side)) action(x, z, side);
                }
        }

        bool ShallowBesideDeckOrStair(PoolRoom room)
        {
            for (int z = room.Z0; z < room.Z0 + room.Height; z++)
                for (int x = room.X0; x < room.X0 + room.Width; x++)
                {
                    if (!OpenCell(x, z) || Data.Depth[x, z] != PoolDepth.Shallow) continue;
                    foreach (WallSide side in PoolSides)
                    {
                        if (Data.StairAt(x, z, side, out _)) return true;
                        if (Data.IsOpen(x, z, side) && Data.Neighbor(x, z, side, out int nx, out int nz) && OpenCell(nx, nz) &&
                            Data.Depth[nx, nz] == PoolDepth.Dry)
                            return true;
                    }
                }
            return false;
        }

        // Lugares da marca de profundidade: deque seco com área funda ao lado (sem parede entre
        // eles) ou parede cheia de uma célula funda.
        List<(int x, int z, WallSide side, bool deck)> DepthMarkSpots(PoolRoom room)
        {
            var spots = new List<(int x, int z, WallSide side, bool deck)>();
            for (int z = room.Z0; z < room.Z0 + room.Height; z++)
                for (int x = room.X0; x < room.X0 + room.Width; x++)
                {
                    if (!OpenCell(x, z) || Data.Room[x, z] != room.Id) continue;
                    PoolDepth depth = Data.Depth[x, z];
                    foreach (WallSide side in PoolSides)
                    {
                        if (depth == PoolDepth.Dry)
                        {
                            if (HasWallBox(x, z, side) || !Data.Neighbor(x, z, side, out int nx, out int nz) || !OpenCell(nx, nz)) continue;
                            if (Data.Depth[nx, nz] == PoolDepth.Deep && !_kit.RoundWell[nx, nz]) spots.Add((x, z, side, true));
                        }
                        else if (depth == PoolDepth.Deep && HasWallBox(x, z, side) && PlainWall(x, z, side))
                            spots.Add((x, z, side, false));
                    }
                }
            return spots;
        }

        // "2.0 M" no deque: lido por quem está no deque olhando para a água (o topo do desenho para a água).
        bool DeckDepthMark(Transform root, Material material, int x, int z, WallSide side, List<Bounds> obstacles)
        {
            Vector3 toWater = SideDir(side);
            float width = MarkWidth[0], height = MarkHeight(0);
            float y = FloorTop(x, z);
            Vector3 border = new Vector3((x + 0.5f) * Cell, y, (z + 0.5f) * Cell) + toWater * (Cell * 0.5f);
            Vector3 center = border - toWater * (DepthMarkGap + height * 0.5f);
            if (!FloorRectInside(center, toWater, width, height, (cx, cz) => Data.Depth[cx, cz] == PoolDepth.Dry && Mathf.Approximately(FloorTop(cx, cz), y))) return false;
            return PlaceDecal(root, material, center + Vector3.up * 0.08f, Quaternion.LookRotation(Vector3.down, toWater), new Vector3(width, height, 0.16f),
                              false, obstacles, $"{MarkLabel[0]} no deque ({x},{z},{side})");
        }

        // "2.0 M" na parede de uma célula funda, logo acima da água (e da faixa azul, se a sala tem).
        bool WallDepthMark(Transform root, Material material, int x, int z, WallSide side, List<Bounds> obstacles)
        {
            int room = Data.Room[x, z];
            float bottom = (_kit.AccentRoom[room] ? _kit.Band[room] + 0.2f : WaterY) + 0.12f;
            return WallMark(root, material, 0, x, z, side, bottom + MarkHeight(0) * 0.5f, obstacles);
        }

        // Marcação na face de parede cheia, no meio do trecho; fora das faixas azuis e acima da água.
        bool WallMark(Transform root, Material material, int kind, int x, int z, WallSide side, float centerY, List<Bounds> obstacles)
        {
            float width = MarkWidth[kind], height = MarkHeight(kind);
            int room = Data.Room[x, z];
            if (_kit.AccentRoom[room])
            {
                float band = _kit.Band[room];
                if (centerY + height * 0.5f > 2f - 0.05f && centerY - height * 0.5f < 2.2f + 0.05f) centerY = 2f - 0.05f - height * 0.5f;
                if (centerY - height * 0.5f < band + 0.2f + 0.05f && centerY + height * 0.5f > band - 0.05f) centerY = band + 0.25f + height * 0.5f;
            }
            if (centerY - height * 0.5f < Mathf.Max(FloorTop(x, z), WaterY) + 0.05f) return false;
            if (!WallDecal(root, material, x, z, side, HalfWall, 0f, centerY, new Vector2(width, height), false, obstacles, MarkLabel[kind])) return false;
            _kit.MarkCount[kind] += kind == 0 ? 0 : 1;
            return true;
        }

        // O retângulo de piso (centro, para onde aponta o topo do desenho, largura e altura) cabe
        // inteiro em células que aceitam a marca, sem entrar nas paredes?
        bool FloorRectInside(Vector3 center, Vector3 up, float width, float height, System.Func<int, int, bool> accept)
        {
            Vector3 right = Vector3.Cross(Vector3.up, up);
            for (int k = 0; k < 4; k++)
            {
                Vector3 p = center + right * ((k & 1) == 0 ? -0.5f : 0.5f) * width + up * ((k & 2) == 0 ? -0.5f : 0.5f) * height;
                int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
                if (!OpenCell(cx, cz) || _kit.RoundWell[cx, cz] || !accept(cx, cz)) return false;
                CellRect(cx, cz, out float x0, out float x1, out float z0, out float z1);
                if (p.x < x0 + 0.05f || p.x > x1 - 0.05f || p.z < z0 + 0.05f || p.z > z1 - 0.05f) return false;
            }
            return true;
        }

        /// <summary>
        /// T azul no fundo, nas duas pontas de uma raia: a trave do T a 0,6 m da parede ou da borda
        /// da ponta e a haste para o meio da raia. Raia = fileira de células fundas ao longo do lado
        /// maior da sala (na piscina, a do meio; no canal e na raia longa, a funda). Se a escada
        /// ocupa a ponta, o T vai para depois do pé dela (até 3,6 m); se não couber, fica sem.
        /// </summary>
        void LaneMarks(Transform root, Material material, PoolRoom room, List<Bounds> obstacles)
        {
            bool alongX = room.Width > room.Height;
            int length = alongX ? room.Width : room.Height, span = alongX ? room.Height : room.Width;
            int Cx(int a, int c) => alongX ? room.X0 + a : room.X0 + c;
            int Cz(int a, int c) => alongX ? room.Z0 + c : room.Z0 + a;
            bool DeepAt(int a, int c) => OpenCell(Cx(a, c), Cz(a, c)) && Data.Depth[Cx(a, c), Cz(a, c)] == PoolDepth.Deep;

            // A fileira mais funda e mais perto do meio: a sequência de fundas mais longa.
            int bestC = -1, bestStart = 0, bestEnd = -1;
            float bestScore = float.MinValue;
            for (int c = 0; c < span; c++)
            {
                int a = 0;
                while (a < length)
                {
                    if (!DeepAt(a, c)) { a++; continue; }
                    int s = a;
                    while (a + 1 < length && DeepAt(a + 1, c)) a++;
                    float score = (a - s + 1) * 10f - Mathf.Abs(c - (span - 1) * 0.5f);
                    if (score > bestScore) { bestScore = score; bestC = c; bestStart = s; bestEnd = a; }
                    a++;
                }
            }
            if (bestC < 0 || bestEnd - bestStart + 1 < 2) return;

            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            float cross = ((alongX ? room.Z0 : room.X0) + bestC + 0.5f) * Cell;
            float y = LevelData.DepthFloor(PoolDepth.Deep);
            WallSide startSide = alongX ? WallSide.West : WallSide.South, endSide = alongX ? WallSide.East : WallSide.North;
            int sx = Cx(bestStart, bestC), sz = Cz(bestStart, bestC), ex = Cx(bestEnd, bestC), ez = Cz(bestEnd, bestC);
            float startLine = ((alongX ? room.X0 : room.Z0) + bestStart) * Cell + (HasWallBox(sx, sz, startSide) ? HalfWall : 0f);
            float endLine = ((alongX ? room.X0 : room.Z0) + bestEnd + 1) * Cell - (HasWallBox(ex, ez, endSide) ? HalfWall : 0f);
            float width = MarkWidth[3], height = MarkHeight(3);
            float middle = (startLine + endLine) * 0.5f;

            for (int end = 0; end < 2; end++)
            {
                float line = end == 0 ? startLine : endLine;
                Vector3 toEnd = end == 0 ? -axis : axis;
                // Até 3,6 m da ponta: passa do pé de uma escada de 11 degraus (3 m) que ocupe a ponta.
                for (float gap = LaneMarkGap; gap <= 3.6f + 1e-3f; gap += 0.3f)
                {
                    float along = line - (end == 0 ? -1f : 1f) * (gap + height * 0.5f);
                    if (end == 0 ? along + height * 0.5f > middle : along - height * 0.5f < middle) break;
                    Vector3 center = alongX ? new Vector3(along, y, cross) : new Vector3(cross, y, along);
                    if (!FloorRectInside(center, toEnd, width, height, (cx, cz) => Data.Depth[cx, cz] == PoolDepth.Deep)) break;
                    if (PlaceDecal(root, material, center + Vector3.up * 0.08f, Quaternion.LookRotation(Vector3.down, toEnd), new Vector3(width, height, 0.16f),
                                   false, obstacles, $"{MarkLabel[3]} a {gap:0.0} m da ponta ({room.Kind} {room.Id})"))
                    {
                        _kit.MarkCount[3]++;
                        break;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- Luz

        void CountShadowLights(out int count)
        {
            count = SkyItems().Count + GroupLitOpenings().Count;
        }

        // Luzes de caixa e spots: uma face de sombra cada. A maior resolução que cabe no atlas.
        int ResolvePoolShadowResolution(int lights)
        {
            int atlas = 2048, maxRequests = 128;
            if (GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset asset)
            {
                HDShadowInitParameters shadows = asset.currentPlatformRenderPipelineSettings.hdShadowInitParams;
                atlas = shadows.cachedPunctualLightShadowAtlas;
                maxRequests = shadows.maxShadowRequests;
            }

            if (ceilingShadows && lights > maxRequests)
                Debug.LogWarning($"[BackRooms] As Piscinas pedem {lights} sombras, mas o asset do HDRP permite {maxRequests}.");

            // Folga larga: atlas em cache cheio faz o HDRP refazer sombras a cada quadro.
            long budget = (long)atlas * atlas * 3 / 10;
            int resolution = Mathf.Max(64, Mathf.ClosestPowerOfTwo(Mathf.Max(maxShadowResolution, 256)));
            while (resolution > 64 && (long)resolution * resolution * Mathf.Max(1, lights) > budget) resolution /= 2;
            return resolution;
        }

        void SetupShadow(HDAdditionalLightData hd, Light source, int resolution)
        {
            if (!ceilingShadows)
            {
                source.shadows = LightShadows.None;
                return;
            }
            source.shadows = LightShadows.Soft;
            hd.shadowUpdateMode = ShadowUpdateMode.OnEnable;
            hd.SetShadowResolutionOverride(true);
            hd.SetShadowResolution(resolution);
        }

        // Vidro de noite (DaylightPanel): a cor e o brilho do material de claraboia apagada, o
        // azul-noite (#223048 com emissão fraca). Vale também para as janelas.
        void NightGlass(out Color baseColor, out Color emissive)
        {
            Material night = panelOffMaterial;
            baseColor = night != null && night.HasProperty("_BaseColor") ? night.GetColor("_BaseColor") : new Color(0.13f, 0.19f, 0.28f);
            emissive = night != null && night.HasProperty("_EmissiveColor") ? night.GetColor("_EmissiveColor") : new Color(0.25f, 0.45f, 1f) * 16f;
        }

        sealed class SkyItem
        {
            public PoolSky Sky;
            public int X0, X1, Z0, Z1;  // células X0..X1 x Z0..Z1 (Full: uma faixa; as outras: uma célula)
        }

        // Claraboias: uma por célula. As Full vizinhas da mesma sala formam um teto de vidro; ele
        // vira faixas no sentido mais comprido dele (uma luz cada): a faixa de vidro de uma raia
        // inteira é uma luz só, corra ela em X ou em Z.
        List<SkyItem> SkyItems()
        {
            int w = Data.Width, h = Data.Height;
            // Sentido de cada teto de vidro: o mais comprido da caixa da sala inteira de Full.
            var alongZ = new bool[w, h];
            var seen = new bool[w, h];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    if (seen[x, z] || !IsFullSky(x, z)) continue;
                    var cells = new List<Vector2Int>();
                    var queue = new Queue<Vector2Int>();
                    seen[x, z] = true;
                    queue.Enqueue(new Vector2Int(x, z));
                    int bx0 = x, bx1 = x, bz0 = z, bz1 = z;
                    while (queue.Count > 0)
                    {
                        Vector2Int cell = queue.Dequeue();
                        cells.Add(cell);
                        bx0 = Mathf.Min(bx0, cell.x); bx1 = Mathf.Max(bx1, cell.x);
                        bz0 = Mathf.Min(bz0, cell.y); bz1 = Mathf.Max(bz1, cell.y);
                        foreach (WallSide side in PoolSides)
                        {
                            if (HasWallBox(cell.x, cell.y, side) || !Data.Neighbor(cell.x, cell.y, side, out int nx, out int nz)) continue;
                            if (seen[nx, nz] || !IsFullSky(nx, nz) || Data.Room[nx, nz] != Data.Room[cell.x, cell.y]) continue;
                            seen[nx, nz] = true;
                            queue.Enqueue(new Vector2Int(nx, nz));
                        }
                    }
                    bool longZ = bz1 - bz0 > bx1 - bx0;
                    foreach (Vector2Int cell in cells) alongZ[cell.x, cell.y] = longZ;
                }

            bool SameStrip(int x, int z, int nx, int nz) =>
                nx >= 0 && nz >= 0 && IsFullSky(nx, nz) && Data.Room[nx, nz] == Data.Room[x, z] && alongZ[nx, nz] == alongZ[x, z];

            var items = new List<SkyItem>();
            var stripAt = new SkyItem[w, h];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    PoolSky sky = Data.Sky[x, z];
                    if (sky == PoolSky.None || !OpenCell(x, z) || RoomAt(x, z).Vault) continue;
                    if (sky == PoolSky.Full)
                    {
                        // Continua a faixa da célula anterior (a oeste ou ao sul, conforme o sentido).
                        int px = alongZ[x, z] ? x : x - 1, pz = alongZ[x, z] ? z - 1 : z;
                        if (SameStrip(x, z, px, pz) && stripAt[px, pz] != null && !HasWallBox(x, z, alongZ[x, z] ? WallSide.South : WallSide.West))
                        {
                            SkyItem strip = stripAt[px, pz];
                            strip.X1 = Mathf.Max(strip.X1, x);
                            strip.Z1 = Mathf.Max(strip.Z1, z);
                            stripAt[x, z] = strip;
                            continue;
                        }
                    }
                    var item = new SkyItem { Sky = sky, X0 = x, X1 = x, Z0 = z, Z1 = z };
                    if (sky == PoolSky.Full) stripAt[x, z] = item;
                    items.Add(item);
                }
            return items;
        }

        /// <summary>
        /// Vidro fosco aceso em cada claraboia e luzes de caixa do HDRP: raios paralelos inclinados,
        /// o mesmo "sol" no mapa todo. As células Full vizinhas da mesma sala formam um teto de vidro
        /// só (uma malha, UV contínua, sem costura), com uma luz por faixa de células numa fileira.
        /// Toda claraboia nasce acesa (nada de claraboia "queimada": canto escuro é sala sem
        /// claraboia); o <see cref="DaylightPanel"/> leva o vidro e as luzes pelo anoitecer.
        /// </summary>
        void BuildSkylights(int resolution)
        {
            List<SkyItem> items = SkyItems();
            if (items.Count == 0) return;
            Transform root = CreateChild("Claraboias").transform;

            Material on = panelOnMaterial != null ? panelOnMaterial : ceilingMaterial;
            NightGlass(out Color nightBase, out Color nightEmissive);

            // Teto de vidro: componentes ligadas de células Full da mesma sala.
            int w = Data.Width, h = Data.Height;
            var component = new int[w, h];
            var cells = new List<List<Vector2Int>>();
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    component[x, z] = -1;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    if (component[x, z] >= 0 || !IsFullSky(x, z)) continue;
                    var list = new List<Vector2Int>();
                    var queue = new Queue<Vector2Int>();
                    component[x, z] = cells.Count;
                    queue.Enqueue(new Vector2Int(x, z));
                    while (queue.Count > 0)
                    {
                        Vector2Int cell = queue.Dequeue();
                        list.Add(cell);
                        foreach (WallSide side in PoolSides)
                        {
                            if (HasWallBox(cell.x, cell.y, side) || !Data.Neighbor(cell.x, cell.y, side, out int nx, out int nz)) continue;
                            if (component[nx, nz] >= 0 || !IsFullSky(nx, nz) || Data.Room[nx, nz] != Data.Room[cell.x, cell.y]) continue;
                            component[nx, nz] = cells.Count;
                            queue.Enqueue(new Vector2Int(nx, nz));
                        }
                    }
                    cells.Add(list);
                }

            for (int k = 0; k < cells.Count; k++)
            {
                List<Vector2Int> list = cells[k];
                float c = Data.Ceiling[list[0].x, list[0].y];
                float bx0 = float.MaxValue, bx1 = float.MinValue, bz0 = float.MaxValue, bz1 = float.MinValue;
                foreach (Vector2Int cell in list)
                {
                    CellRect(cell.x, cell.y, out float x0, out float x1, out float z0, out float z1);
                    bx0 = Mathf.Min(bx0, x0); bx1 = Mathf.Max(bx1, x1); bz0 = Mathf.Min(bz0, z0); bz1 = Mathf.Max(bz1, z1);
                }

                var glass = new MeshBuilder(1f);
                foreach (Vector2Int cell in list)
                {
                    CellRect(cell.x, cell.y, out float x0, out float x1, out float z0, out float z1);
                    GlassQuad(glass, x0, x1, z0, z1, c, bx0, bx1, bz0, bz1);
                }

                string name = $"Claraboia_{list[0].x}_{list[0].y}_teto";
                Mesh mesh = glass.ToMesh(name);
                MeshRenderer panel = CreateRenderer(name, mesh, on, ShadowCastingMode.Off, root);
                panel.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                _kit.Objects++;

                var lights = new List<HDAdditionalLightData>();
                foreach (SkyItem item in items)
                {
                    if (item.Sky != PoolSky.Full || component[item.X0, item.Z0] != k) continue;
                    var corners = new List<Vector3>();
                    for (int z = item.Z0; z <= item.Z1; z++)
                        for (int x = item.X0; x <= item.X1; x++)
                        {
                            CellRect(x, z, out float x0, out float x1, out float z0, out float z1);
                            corners.Add(new Vector3(x0, c, z0)); corners.Add(new Vector3(x1, c, z1));
                            corners.Add(new Vector3(x0, c, z1)); corners.Add(new Vector3(x1, c, z0));
                        }
                    lights.Add(CreateSun(panel.transform, corners, c, item.X0, item.Z0, resolution));
                }

                panel.gameObject.AddComponent<DaylightPanel>().Init(lights.ToArray(), panel, on, nightBase, nightEmissive);
            }

            // Claraboias recortadas (quadrada, redonda, rasgos): vidro no alto do poço de luz.
            foreach (SkyItem item in items)
            {
                if (item.Sky == PoolSky.Full) continue;
                int x = item.X0, z = item.Z0;
                float c = Data.Ceiling[x, z];
                float glassY = c + ShaftHeight;
                var glass = new MeshBuilder(1f);
                CellRect(x, z, out float x0, out float x1, out float z0, out float z1);
                float cx = (x + 0.5f) * Cell, cz = (z + 0.5f) * Cell;
                SkyHole(item.Sky, cx, cz, x0, x1, z0, z1, out float hx0, out float hx1, out float hz0, out float hz1);
                if (item.Sky == PoolSky.Round)
                    // Um centímetro a mais que o poço de luz: a borda do vidro fica por cima da parede dele, sem fresta.
                    glass.AddDisc(new Vector3(cx, glassY, cz), Vector3.down, Vector3.right, 1.41f, HalfCircleSegments * 2);
                else
                    GlassQuad(glass, hx0, hx1, hz0, hz1, glassY, hx0, hx1, hz0, hz1);
                var corners = new List<Vector3>
                {
                    new Vector3(hx0, glassY, hz0), new Vector3(hx1, glassY, hz1), new Vector3(hx0, glassY, hz1), new Vector3(hx1, glassY, hz0)
                };

                string name = $"Claraboia_{x}_{z}";
                Mesh mesh = glass.ToMesh(name);
                MeshRenderer panel = CreateRenderer(name, mesh, on, ShadowCastingMode.Off, root);
                panel.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                _kit.Objects++;

                var lights = new[] { CreateSun(panel.transform, corners, glassY, x, z, resolution) };
                panel.gameObject.AddComponent<DaylightPanel>().Init(lights, panel, on, nightBase, nightEmissive);
            }
        }

        bool IsFullSky(int x, int z) => OpenCell(x, z) && Data.Sky[x, z] == PoolSky.Full && !RoomAt(x, z).Vault;

        // Luz de caixa sob o vidro: a caixa é o vidro projetado no plano perpendicular aos raios.
        HDAdditionalLightData CreateSun(Transform parent, List<Vector3> corners, float glassY, int cellX, int cellZ, int resolution)
        {
            float tilt = poolSunTilt * Mathf.Deg2Rad, heading = poolSunHeading * Mathf.Deg2Rad;
            var horizontal = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
            Vector3 sun = (horizontal * Mathf.Sin(tilt) + Vector3.down * Mathf.Cos(tilt)).normalized;
            Quaternion rotation = Quaternion.LookRotation(sun, horizontal);
            Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up;

            Vector3 middle = Vector3.zero;
            foreach (Vector3 p in corners) middle += p;
            middle /= corners.Count;

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 p in corners)
            {
                float px = Vector3.Dot(p - middle, right), py = Vector3.Dot(p - middle, up);
                minX = Mathf.Min(minX, px); maxX = Mathf.Max(maxX, px);
                minY = Mathf.Min(minY, py); maxY = Mathf.Max(maxY, py);
            }

            // Alcance: até o fundo mais baixo da sala.
            float lowest = glassY;
            PoolRoom room = RoomAt(cellX, cellZ);
            for (int rz = room.Z0; rz < room.Z0 + room.Height; rz++)
                for (int rx = room.X0; rx < room.X0 + room.Width; rx++)
                    if (OpenCell(rx, rz)) lowest = Mathf.Min(lowest, Data.FloorHeight(rx, rz));

            HDAdditionalLightData hd = CreateLightObject("Sol", parent, LightType.Box, middle + Vector3.down * 0.03f, out Light source);
            hd.transform.rotation = transform.rotation * rotation;
            source.lightUnit = LightUnit.Lux;
            source.intensity = poolSkyLux;
            source.colorTemperature = poolSkyTemperature;
            source.areaSize = new Vector2(maxX - minX + 0.05f, maxY - minY + 0.05f);
            source.range = (glassY + 0.03f - lowest) / Mathf.Cos(tilt) + 2f;
            // A sombra vai 20% além da luz: luz acesa sem sombra atravessa as paredes (como no Level 0).
            hd.fadeDistance = lightFadeDistance;
            hd.shadowFadeDistance = lightFadeDistance * 1.2f;
            hd.affectsVolumetric = poolSkyBeams;
            hd.shadowNearPlane = 0.05f;
            if (poolSkyCookie != null && poolSkyCookie.dimension == TextureDimension.Tex2D) hd.SetCookie(poolSkyCookie);
            SetupShadow(hd, source, resolution);
            _kit.Lights++;
            if (ceilingShadows) _kit.ShadowLights++;
            return hd;
        }

        // Vidro horizontal virado para baixo; a UV vai de 0 a 1 no retângulo (bx0..bx1, bz0..bz1),
        // que é o teto de vidro inteiro: células vizinhas continuam a textura sem costura.
        static void GlassQuad(MeshBuilder glass, float x0, float x1, float z0, float z1, float y, float bx0, float bx1, float bz0, float bz1)
        {
            Vector2 Uv(float x, float z) => new Vector2((x - bx0) / Mathf.Max(0.01f, bx1 - bx0), (z - bz0) / Mathf.Max(0.01f, bz1 - bz0));
            int a = glass.AddVertex(new Vector3(x0, y, z0), Vector3.down, Uv(x0, z0));
            int b = glass.AddVertex(new Vector3(x1, y, z0), Vector3.down, Uv(x1, z0));
            int c = glass.AddVertex(new Vector3(x1, y, z1), Vector3.down, Uv(x1, z1));
            int d = glass.AddVertex(new Vector3(x0, y, z1), Vector3.down, Uv(x0, z1));
            glass.AddTriangleFacing(a, b, c, Vector3.down);
            glass.AddTriangleFacing(a, c, d, Vector3.down);
        }

        // Janelas acesas vizinhas, do mesmo tipo e na mesma parede, dividem uma luz (até 3).
        List<List<LitOpening>> GroupLitOpenings()
        {
            var groups = new List<List<LitOpening>>();
            List<LitOpening> current = null;
            foreach (LitOpening opening in _kit.Lit)
            {
                LitOpening last = current != null ? current[current.Count - 1] : null;
                bool join = last != null && current.Count < 3 && last.Type == opening.Type && last.AlongX == opening.AlongX &&
                            last.Line == opening.Line && last.Side == opening.Side && last.Index == opening.Index - 1;
                if (join) current.Add(opening);
                else
                {
                    current = new List<LitOpening> { opening };
                    groups.Add(current);
                }
            }
            return groups;
        }

        /// <summary>
        /// Janelão, frestas, vigia acesa e tijolo de vidro: o vidro aceso e um spot com sombra
        /// entrando na sala; no anoitecer os dois seguem a mesma rampa das claraboias
        /// (<see cref="DaylightPanel"/>), e o vidro termina no mesmo azul-noite.
        /// </summary>
        void BuildLitOpenings(int resolution)
        {
            List<List<LitOpening>> groups = GroupLitOpenings();
            if (groups.Count == 0) return;
            Transform root = CreateChild("Janelas").transform;

            Material fallback = poolWindowMaterial != null ? poolWindowMaterial : panelOnMaterial != null ? panelOnMaterial : wallMaterial;
            NightGlass(out Color nightBase, out Color nightEmissive);

            foreach (List<LitOpening> group in groups)
            {
                LitOpening first = group[0];
                bool blocks = first.Type == PoolEdge.GlassBlock;
                Material on = blocks && poolGlassBlockMaterial != null ? poolGlassBlockMaterial : fallback;

                // Tijolo de vidro: 1,2 m por repetição, pela UV do mundo. Os outros: 0 a 1 em cada vidro.
                var glass = new MeshBuilder(blocks ? 1.2f : 1f);
                Vector3 center = Vector3.zero;
                foreach (LitOpening opening in group)
                {
                    center += opening.Center;
                    foreach (GlassPiece piece in opening.Glass)
                    {
                        if (piece.Disc)
                        {
                            glass.AddDisc(piece.Center, opening.Normal, piece.Right, piece.Radius, HalfCircleSegments * 2);
                            continue;
                        }
                        if (blocks)
                        {
                            glass.AddPolygon(Poly(piece.A, piece.B, piece.C, piece.D), opening.Normal);
                            continue;
                        }
                        int a = glass.AddVertex(piece.A, opening.Normal, new Vector2(0f, 0f));
                        int b = glass.AddVertex(piece.B, opening.Normal, new Vector2(0f, 1f));
                        int c = glass.AddVertex(piece.C, opening.Normal, new Vector2(1f, 1f));
                        int d = glass.AddVertex(piece.D, opening.Normal, new Vector2(1f, 0f));
                        glass.AddTriangleFacing(a, b, c, opening.Normal);
                        glass.AddTriangleFacing(a, c, d, opening.Normal);
                    }
                }
                center /= group.Count;

                string name = $"Janela_{(first.AlongX ? "X" : "Z")}{first.Line}_{first.Index}";
                MeshRenderer panel = CreateRenderer(name, glass.ToMesh(name), on, ShadowCastingMode.Off, root);
                _kit.Objects++;

                // A luz entra pela janela, um pouco para baixo.
                Vector3 inward = first.Normal;
                Vector3 aim = (inward * Mathf.Cos(15f * Mathf.Deg2Rad) + Vector3.down * Mathf.Sin(15f * Mathf.Deg2Rad)).normalized;
                HDAdditionalLightData hd = CreateLightObject("Luz", panel.transform, LightType.Spot, center + inward * 0.35f, out Light source);
                hd.transform.rotation = transform.rotation * Quaternion.LookRotation(aim, Vector3.up);
                source.intensity = poolWindowCandela;
                source.colorTemperature = poolSkyTemperature;
                source.spotAngle = 130f;
                source.innerSpotAngle = 50f;
                source.range = 16f;
                hd.fadeDistance = lightFadeDistance;
                hd.shadowFadeDistance = lightFadeDistance * 1.2f;
                hd.affectsVolumetric = false;
                SetupShadow(hd, source, resolution);
                _kit.Lights++;
                if (ceilingShadows) _kit.ShadowLights++;

                panel.gameObject.AddComponent<DaylightPanel>().Init(new[] { hd }, panel, on, nightBase, nightEmissive);
            }
        }

        /// <summary>Lâmpadas de dentro d'água: disco aceso na parede ou na borda e uma luz pontual sem sombra.</summary>
        void BuildLamps()
        {
            if (Data.Lamps.Count == 0) return;
            Transform root = CreateChild("Lampadas").transform;

            foreach (PoolLamp lamp in Data.Lamps)
            {
                if (!OpenCell(lamp.X, lamp.Z)) continue;
                Vector3 dir = SideDir(lamp.Side);
                float inset = HasWallBox(lamp.X, lamp.Z, lamp.Side) ? HalfWall : 0f;
                Vector3 face = new Vector3((lamp.X + 0.5f) * Cell, lamp.Y, (lamp.Z + 0.5f) * Cell) + dir * (Cell * 0.5f - inset);
                Vector3 normal = -dir;
                Vector3 tangent = Mathf.Abs(dir.x) > 0.5f ? Vector3.forward : Vector3.right;

                if (poolLampMaterial != null) _kit.Lamps.AddDisc(face + normal * 0.005f, normal, tangent, 0.15f, 16);

                HDAdditionalLightData hd = CreateLightObject("Lampada", root, LightType.Point, face + normal * 0.2f, out Light source);
                source.useColorTemperature = false;
                source.color = poolLampColor;
                source.intensity = poolLampCandela;
                source.range = 7f;
                source.shadows = LightShadows.None;
                hd.affectsVolumetric = false;
                hd.fadeDistance = 60f;
                _kit.Lights++;
            }

            if (!_kit.Lamps.IsEmpty)
                CreateRenderer("LampadasSubmersas", _kit.Lamps.ToMesh("LampadasSubmersas"), poolLampMaterial, ShadowCastingMode.Off, root);
        }
    }
}
