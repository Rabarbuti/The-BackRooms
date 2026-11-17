using System;
using System.Collections.Generic;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// A camada de objetos do Level 1 (Docs/level1-mostruario.md, seção 4): depois que o
    /// <see cref="LevelBuilder"/> monta paredes e luzes, mobília a mesma planta com os modelos
    /// feitos no Blender (Docs/modelos-blender.md).
    ///
    /// Regras:
    /// - Determinística: só System.Random da seed, uma sequência por bloco e por célula, então
    ///   todas as máquinas mobíliam igual e a ordem de construção não importa.
    /// - Cruz livre: em cada célula fica livre uma faixa de 1,2 m do centro até cada lado. Assim
    ///   todo caminho que existia continua existindo, para o jogador (0,6 m) e para quem andar
    ///   pela grade (LevelNav).
    /// - Móvel grande encosta numa parede fechada (sofá, estante, balcão), de frente para a
    ///   célula; o resto vai nos quatro cantos. A sala inicial e a célula da saída ficam vazias.
    /// - Cada bloco de 2 x 2 células tem um tema (jantar, sala de estar, loja, estoque, bagunça,
    ///   vazio) e um bloco do mapa é a sala dos sofás. Longe do início (desgaste), mais coisa
    ///   tombada e empilhada.
    /// - Setas para a saída: onde o gerador marcou uma seta, prega na parede uma placa amarela
    ///   de curva (losango com a seta dobrada), virando para o lado do caminho até a saída (com o
    ///   LevelBuilder.paintedArrows desligado, a seta pintada não aparece).
    ///   Nenhum móvel encosta nessa parede, para não tapar a placa.
    /// - Colisor e malha nascem juntos: cada móvel recebe uma caixa de colisão do tamanho dele.
    ///   Placa pendurada no teto não tem colisor (fica acima da cabeça).
    /// </summary>
    public sealed class PropLayer : MonoBehaviour
    {
        [Tooltip("Modelos disponíveis (os FBX de Art/Level1/Modelos), achados pelo nome. Modelo que faltar é pulado.")]
        public GameObject[] models;

        [Tooltip("Chance de um canto livre da célula receber alguma coisa.")]
        [Range(0f, 1f)] public float cornerChance = 0.6f;

        [Tooltip("Chance de uma parede fechada receber um móvel encostado.")]
        [Range(0f, 1f)] public float wallChance = 0.8f;

        [Header("Abrigo e perigo (level1-mostruario.md, seções 4 e 5.4)")]
        [Tooltip("Parte das células que vira barricada (entupida do chão ao teto). Nunca isola nada.")]
        [Range(0f, 0.1f)] public float barricadeShare = 0.025f;
        [Tooltip("Totens do pirata no mapa: entre o mínimo e o máximo, pela seed.")]
        public int totemsMin = 6;
        public int totemsMax = 10;
        [Tooltip("O modelo do Capitão (Art/Personagens/Capitao), assado como estátua nos totens.")]
        public TextAsset totemModel;
        public Material[] totemMaterials;
        [Tooltip("Base preta de fibra de vidro do totem.")]
        public Material totemBaseMaterial;
        [Tooltip("Plástico-bolha no chão. Sem material, não há plástico-bolha.")]
        public Material bubbleWrapMaterial;

        [Header("Objetivo (level1-mostruario.md, seção 6)")]
        [Tooltip("Porta de saída trancada por um teclado; o código está em três etiquetas vermelhas.")]
        public bool exitCode;
        [Tooltip("Material-base (HDRP/Lit branco) das etiquetas, do bilhete e do teclado: cada um recebe a própria textura.")]
        public Material labelMaterial;
        [Tooltip("Distância mínima entre as etiquetas, em células.")]
        public int tagSpacing = 6;

        [Header("Piso e som (level1-mostruario.md, seções 3 e 7)")]
        [Tooltip("Laminado de madeira debaixo das vitrines de jantar e de estar. Sem material, carpete.")]
        public Material laminateMaterial;
        [Tooltip("Piso vinílico cinza no estoque. Sem material, carpete.")]
        public Material vinylMaterial;
        [Tooltip("A música de loja baixinha, do teto, que some antes do apagão.")]
        public bool shopMusic;

        [Header("Placas e anomalias (level1-mostruario.md, seções 3 e 4)")]
        [Tooltip("Placas de papelão penduradas com o nome da zona e PROIBIDO ENTRAR nas barricadas.")]
        public bool zoneSigns;
        [Tooltip("Chance, nas células gastas (longe do início), de uma anomalia: mesa no teto, móvel atravessando a parede, guarda-roupa de frente para a parede, batente solto.")]
        [Range(0f, 1f)] public float anomalyChance = 0.14f;

        /// <summary>Quantos móveis foram postos na última construção.</summary>
        public int Count { get; private set; }

        /// <summary>Resumo do que foi posto (nome, posição, giro): igual em máquinas com a mesma seed.</summary>
        public int Hash { get; private set; }

        /// <summary>Células que viraram barricada na última construção.</summary>
        public int Barricades { get; private set; }

        /// <summary>Totens postos na última construção.</summary>
        public int Totems { get; private set; }

        /// <summary>Folhas de plástico-bolha postas na última construção.</summary>
        public int BubbleSheets { get; private set; }

        /// <summary>Móveis em que dá para se esconder (guarda-roupas e mesas de jantar).</summary>
        public int HideSpots { get; private set; }

        /// <summary>Anomalias postas na última construção.</summary>
        public int Anomalies { get; private set; }

        /// <summary>Placas penduradas com texto (zona e PROIBIDO ENTRAR).</summary>
        public int ZoneSigns { get; private set; }

        /// <summary>Etiquetas vermelhas de LIQUIDAÇÃO postas (o objetivo pede 3).</summary>
        public int Tags { get; private set; }

        /// <summary>Células das etiquetas, na ordem do dígito.</summary>
        public readonly List<Vector2Int> TagCells = new List<Vector2Int>();

        /// <summary>Bloco (de 2 x 2 células) que virou a sala dos sofás; (-1, -1) se nenhum coube.</summary>
        public Vector2Int SofaRoomBlock { get; private set; } = new Vector2Int(-1, -1);

        /// <summary>Meia largura da cruz livre no meio de cada célula.</summary>
        public const float Lane = 0.6f;

        const float Cell = LevelBuilder.Cell;
        const float Inner = Cell * 0.5f - Lane;   // 1,4 m: até onde vai um canto ou uma faixa de parede
        const float WallGap = 0.12f;              // da borda da célula à face da parede (meia espessura 0,1)
        const float PillarGap = 0.45f;            // pilar de 0,8 m no canto: 0,4 mais folga
        const float OpenGap = 0.05f;
        const float Overhang = 0.08f;             // o balcão pode avançar um tico na cruz
        const float ShadowMinHeight = 1.2f;       // móvel mais baixo que isto não projeta sombra

        enum Theme { Vazio, Jantar, Estar, Loja, Estoque, Bagunca, Sofas }

        static readonly (Theme, float)[] ThemeWeights =
        {
            (Theme.Jantar, 3f), (Theme.Estar, 3f), (Theme.Loja, 3f), (Theme.Estoque, 2.5f),
            (Theme.Bagunca, 2f), (Theme.Vazio, 1.2f),
        };

        readonly Dictionary<string, GameObject> _byName = new Dictionary<string, GameObject>();
        readonly Dictionary<GameObject, Bounds> _bounds = new Dictionary<GameObject, Bounds>();
        readonly HashSet<(int, int, WallSide)> _arrowFaces = new HashSet<(int, int, WallSide)>();
        LevelData _data;
        float _ceiling = LevelBuilder.DefaultWallHeight;
        Transform _root;
        readonly HashSet<int> _totemCells = new HashSet<int>();
        bool _barricading;
        int _hideSpots, _sheets, _anomalies, _signs;
        bool _anomaly;
        int _seed;
        int _count;
        int _hash;

        // ---------------------------------------------------------------- Entrada

        /// <summary>Mobília a planta já construída. Chamado pelo LevelBuilder no fim do Build.</summary>
        public void Place(LevelBuilder level)
        {
            _data = level.Data;
            _seed = _data.Seed;
            _ceiling = level.WallHeight;
            _count = 0;
            _hash = 17;

            _byName.Clear();
            if (models != null)
                foreach (GameObject model in models)
                    if (model != null) _byName[model.name] = model;

            // Filho do nível: o Clear do LevelBuilder apaga junto ao reconstruir.
            _root = new GameObject("Moveis").transform;
            _root.SetParent(level.transform, false);

            SofaRoomBlock = ChooseSofaBlock();

            // Antes de qualquer móvel: as células entupidas (o LevelNav do inimigo já as evita) e
            // onde ficam os totens. Cada etapa tem a própria sequência da seed.
            ChooseBarricades();
            ChooseTotemCells();
            Totem.Clear();
            BubbleWrap.Clear();
            FloorZones.Clear();
            _hideSpots = 0;
            _sheets = 0;
            _anomalies = 0;
            _signs = 0;

            _arrowFaces.Clear();
            foreach (Decoration mark in _data.Decorations)
                if (mark.Kind == DecorationKind.Arrow) _arrowFaces.Add((mark.X, mark.Z, mark.Side));
            MountExitSigns();

            for (int z = 0; z < _data.Height; z++)
                for (int x = 0; x < _data.Width; x++)
                    PlaceCell(x, z);

            // O objetivo: teclado na porta, etiquetas e bilhete.
            Tags = 0;
            TagCells.Clear();
            if (exitCode && labelMaterial != null) PlaceObjective(level);

            // A tecla E dos esconderijos e o estalo do plástico-bolha: um de cada por fase, só em jogo.
            if (Application.isPlaying)
            {
                if (_hideSpots > 0) _root.gameObject.AddComponent<HideSpotInput>();
                if (_sheets > 0) _root.gameObject.AddComponent<BubbleWrap>();
                if (shopMusic) _root.gameObject.AddComponent<BackRooms.Audio.ShopMusic>();
            }

            Count = _count;
            Hash = _hash;
            Totems = Totem.All.Count;
            BubbleSheets = _sheets;
            HideSpots = _hideSpots;
            Anomalies = _anomalies;
            ZoneSigns = _signs;
            int barricades = 0;
            for (int z = 0; z < _data.Height; z++)
                for (int x = 0; x < _data.Width; x++)
                    if (_data.IsBarricade(x, z)) barricades++;
            Barricades = barricades;
        }

        // ---------------------------------------------------------------- Barricadas

        // Algumas células, longe do início e fora do caminho até a saída, viram barricada. Uma
        // de cada vez, e só se tudo o que tem piso continuar alcançável a partir da sala inicial
        // (a mesma garantia dos buracos do Level 0: nada fica isolado).
        void ChooseBarricades()
        {
            _data.Barricade = new bool[_data.Width, _data.Height];
            if (barricadeShare <= 0f) return;

            var keep = new HashSet<int>(_data.SafePathCells);
            foreach (int cell in _data.PathCells) keep.Add(cell);

            var candidates = new List<int>();
            for (int z = 0; z < _data.Height; z++)
            {
                for (int x = 0; x < _data.Width; x++)
                {
                    if (!Free(x, z) || keep.Contains(x + z * _data.Width)) continue;
                    if (SofaRoomBlock == new Vector2Int(x / 2, z / 2)) continue;
                    if (LevelGenerator.WearAt(_data, x, z) < 0.25f) continue;
                    if (NextToRoom(x, z)) continue;
                    candidates.Add(x + z * _data.Width);
                }
            }

            System.Random rng = Rng(211, 0, 0);
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int swap = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = swap;
            }

            int target = Mathf.RoundToInt(_data.Width * _data.Height * barricadeShare);
            int made = 0;
            foreach (int cell in candidates)
            {
                if (made >= target) break;
                int x = cell % _data.Width, z = cell / _data.Width;
                _data.Barricade[x, z] = true;
                if (AllReachable()) made++;
                else _data.Barricade[x, z] = false;
            }
        }

        bool NextToRoom(int x, int z)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx >= 0 && nz >= 0 && nx < _data.Width && nz < _data.Height && _data.InRoom(nx, nz)) return true;
                }
            return false;
        }

        // Toda célula com piso que não é barricada se alcança da sala inicial, sem pular buraco.
        bool AllReachable()
        {
            int w = _data.Width, h = _data.Height;
            var seen = new bool[w, h];
            var queue = new Queue<int>();
            queue.Enqueue(_data.SpawnX + _data.SpawnZ * w);
            seen[_data.SpawnX, _data.SpawnZ] = true;

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int x = cell % w, z = cell / w;
                foreach (WallSide side in Sides)
                {
                    if (!_data.CanWalk(x, z, side, false)) continue;
                    _data.Neighbor(x, z, side, out int nx, out int nz);
                    if (seen[nx, nz] || _data.IsBarricade(nx, nz)) continue;
                    seen[nx, nz] = true;
                    queue.Enqueue(nx + nz * w);
                }
            }

            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    if (!_data.Pit[x, z] && !_data.IsBarricade(x, z) && !seen[x, z]) return false;
            return true;
        }

        // Entupida do chão ao teto: móveis grandes deitados e tombados embaixo, cadeiras e caixas
        // por cima. Um colisor só, do tamanho da célula, barra a passagem (os móveis soltos não
        // têm colisor próprio).
        void FillBarricade(int x, int z)
        {
            System.Random rng = Rng(223, x, z);
            var origin = new Vector3(x * Cell, 0f, z * Cell);

            var block = new GameObject("Barricada");
            block.transform.SetParent(_root, false);
            block.transform.localPosition = origin + new Vector3(Cell * 0.5f, _ceiling * 0.5f, Cell * 0.5f);
            var box = block.AddComponent<BoxCollider>();
            box.size = new Vector3(Cell - 0.24f, _ceiling, Cell - 0.24f);

            _barricading = true;
            try
            {
                string[] heavy = { "Sofa", "SofaMarromListrado", "Estante", "EstanteDeAco", "PilhaDeCaixas", "Comoda", "GuardaRoupa" };
                string[] light = { "Cadeira", "CadeiraEncostoRipas", "CadeiraDobravel", "CaixaDePapelao", "Poltrona", "CilindroAzul" };

                // Embaixo: um móvel grande em cada quarto da célula, deitado ou tombado, cobrindo
                // as quatro passagens.
                for (int q = 0; q < 4; q++)
                {
                    float qx = (q % 2 == 0 ? 0.28f : 0.72f) * Cell, qz = (q < 2 ? 0.28f : 0.72f) * Cell;
                    var at = new Vector3(origin.x + qx + Jitter(rng, 0.25f), 0f, origin.z + qz + Jitter(rng, 0.25f));
                    Quaternion tip = rng.NextDouble() < 0.5 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.Euler(0f, 0f, rng.NextDouble() < 0.5 ? 90f : -90f);
                    PutAt(heavy[rng.Next(heavy.Length)], at, rng.Next(4) * 90f + Jitter(rng, 20f), rng.NextDouble() < 0.6 ? tip : Quaternion.identity, rng, 0f);
                }

                // Por cima, até perto do teto: cadeiras e caixas jogadas umas sobre as outras.
                for (int column = 0; column < 5; column++)
                {
                    float cx = origin.x + Cell * (0.2f + 0.6f * (float)rng.NextDouble());
                    float cz = origin.z + Cell * (0.2f + 0.6f * (float)rng.NextDouble());
                    float y = 0.9f + 0.4f * (float)rng.NextDouble();
                    while (y < _ceiling - 0.7f)
                    {
                        GameObject item = PutAt(light[rng.Next(light.Length)], new Vector3(cx + Jitter(rng, 0.3f), y, cz + Jitter(rng, 0.3f)),
                                                Jitter(rng, 180f), Tipped(rng, 0.7f), rng, 0f);
                        if (item == null) break;
                        y = Mathf.Max(y + 0.35f, TopOf(item) - 0.05f);
                    }
                }
            }
            finally
            {
                _barricading = false;
            }

            // PROIBIDO ENTRAR pendurada na passagem para a primeira vizinha aberta.
            if (zoneSigns && labelMaterial != null)
                for (int s = 0; s < 4; s++)
                {
                    WallSide side = Sides[s];
                    if (!_data.CanWalk(x, z, side, false) || !_data.Neighbor(x, z, side, out int nx, out int nz)) continue;
                    if (_data.IsBarricade(nx, nz)) continue;
                    Vector3 middle = origin + new Vector3(Cell * 0.5f, 0f, Cell * 0.5f);
                    Vector3 toward = new Vector3(nx - x, 0f, nz - z);
                    HangSign(middle + toward * (Cell * 0.5f), Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg, "PROIBIDO ENTRAR",
                             new Color32(250, 246, 236, 255), new Color32(176, 28, 22, 255));
                    break;
                }

            unchecked { _hash = _hash * 31 + 977 + x * 101 + z; }
        }

        // ---------------------------------------------------------------- Totens

        // Células dos totens: longe do início, fora das barricadas e da sala dos sofás, e a pelo
        // menos 3 células uma da outra.
        void ChooseTotemCells()
        {
            _totemCells.Clear();
            if (totemModel == null || totemsMax <= 0) return;

            var candidates = new List<int>();
            for (int z = 0; z < _data.Height; z++)
                for (int x = 0; x < _data.Width; x++)
                {
                    if (!Free(x, z) || _data.IsBarricade(x, z)) continue;
                    if (SofaRoomBlock == new Vector2Int(x / 2, z / 2)) continue;
                    if (Mathf.Abs(x - _data.SpawnX) + Mathf.Abs(z - _data.SpawnZ) < 5) continue;
                    candidates.Add(x + z * _data.Width);
                }

            System.Random rng = Rng(233, 0, 0);
            int want = totemsMin + rng.Next(Mathf.Max(1, totemsMax - totemsMin + 1));
            while (_totemCells.Count < want && candidates.Count > 0)
            {
                int pick = candidates[rng.Next(candidates.Count)];
                candidates.Remove(pick);
                int px = pick % _data.Width, pz = pick / _data.Width;

                bool apart = true;
                foreach (int other in _totemCells)
                    if (Mathf.Max(Mathf.Abs(other % _data.Width - px), Mathf.Abs(other / _data.Width - pz)) < 3) apart = false;
                if (apart) _totemCells.Add(pick);
            }
        }

        // O totem num canto da célula, virado para o meio dela: base preta, a estátua do Capitão
        // em cima e a placa de OFERTA na frente. Um colisor só, da base à cabeça.
        bool PlaceTotem(Rect region, Vector2 anchor, Vector3 origin, System.Random rng)
        {
            if (!Totem.Statue(totemModel, totemMaterials, out Mesh statue, out Material[] statueMaterials,
                              out Vector3 statueOffset, out Quaternion statueRotation)) return false;

            const float baseW = 0.9f, baseH = 0.12f, baseD = 0.6f, height = 2.95f;
            Vector2 toCenter = new Vector2(Cell * 0.5f, Cell * 0.5f) - region.center;
            // Virado para o eixo da célula mais à frente (sem giro diagonal: cabe em canto com pilar).
            float yaw = Mathf.Abs(toCenter.x) > Mathf.Abs(toCenter.y) ? (toCenter.x > 0f ? 90f : 270f) : (toCenter.y > 0f ? 0f : 180f);
            Quaternion rotation = Quaternion.Euler(0f, yaw + Jitter(rng, 6f), 0f);

            var local = new Bounds(new Vector3(0f, height * 0.5f, 0f), new Vector3(baseW, height, baseD));
            Bounds rb = Rotated(local, rotation);
            float slackX = region.width - rb.size.x, slackZ = region.height - rb.size.z;
            if (slackX < 0f || slackZ < 0f) return false;

            var position = origin + new Vector3(region.x + slackX * anchor.x - rb.min.x, 0f, region.y + slackZ * anchor.y - rb.min.z);
            if (BlocksLane(position, rb)) return false;

            var root = new GameObject("Totem");
            root.transform.SetParent(_root, false);
            root.transform.localPosition = position;
            root.transform.localRotation = rotation;
            var box = root.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;

            // Base preta.
            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedestal.name = "Base";
            if (Application.isPlaying) Destroy(pedestal.GetComponent<Collider>()); else DestroyImmediate(pedestal.GetComponent<Collider>());
            pedestal.transform.SetParent(root.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, baseH * 0.5f, 0f);
            pedestal.transform.localScale = new Vector3(baseW, baseH, baseD);
            if (totemBaseMaterial != null) pedestal.GetComponent<MeshRenderer>().sharedMaterial = totemBaseMaterial;

            // A estátua, em pé na base.
            var figure = new GameObject("Estatua");
            figure.transform.SetParent(root.transform, false);
            figure.transform.localPosition = new Vector3(0f, baseH, 0f) + statueOffset;
            figure.transform.localRotation = statueRotation;
            figure.AddComponent<MeshFilter>().sharedMesh = statue;
            figure.AddComponent<MeshRenderer>().sharedMaterials = statueMaterials;

            // A placa de OFERTA encostada na frente da base.
            GameObject sign = Get("PlacaOferta");
            if (sign != null)
            {
                GameObject plate = Instantiate(sign, root.transform);
                plate.name = "PlacaOferta";
                Bounds sb = BoundsOf(sign);
                plate.transform.localPosition = new Vector3(0.18f, baseH - sb.min.y, baseD * 0.5f + 0.02f - sb.min.z);
                plate.transform.localRotation = Quaternion.Euler(-8f, Jitter(rng, 10f), 0f);
            }

            Totem.Register(root);
            _count++;
            unchecked
            {
                _hash = _hash * 31 + Fnv("Totem");
                _hash = _hash * 31 + Mathf.RoundToInt(position.x * 1000f);
                _hash = _hash * 31 + Mathf.RoundToInt(position.z * 1000f);
                _hash = _hash * 31 + Mathf.RoundToInt(yaw * 10f);
            }
            return true;
        }

        // ---------------------------------------------------------------- Objetivo

        void PlaceObjective(LevelBuilder level)
        {
            var lockObject = new GameObject("PortaTrancada");
            lockObject.transform.SetParent(_root, false);
            var exitLock = lockObject.AddComponent<ExitLock>();
            exitLock.Init(_seed);

            Transform exitRoot = level.transform.Find("Saida");
            if (exitRoot != null) exitLock.BuildKeypad(exitRoot, labelMaterial);

            Physics.SyncTransforms();
            PlaceTags(exitLock.Digits);
            PlaceNote(exitLock.Digits);
            unchecked { _hash = _hash * 31 + exitLock.Code + 7919; }
        }

        // Três células longe do início, em blocos diferentes e longe umas das outras, com uma
        // parede fechada livre (sem placa de saída nem móvel encostado) para a etiqueta.
        void PlaceTags(int[] digits)
        {
            var candidates = new List<int>();
            for (int z = 0; z < _data.Height; z++)
                for (int x = 0; x < _data.Width; x++)
                {
                    if (!Free(x, z) || _data.IsBarricade(x, z) || _totemCells.Contains(x + z * _data.Width)) continue;
                    if (LevelGenerator.WearAt(_data, x, z) < 0.3f) continue;
                    candidates.Add(x + z * _data.Width);
                }

            System.Random rng = Rng(251, 0, 0);
            for (int spacing = tagSpacing; spacing >= 3 && TagCells.Count < 3; spacing--)
            {
                TagCells.Clear();
                var pool = new List<int>(candidates);
                while (TagCells.Count < 3 && pool.Count > 0)
                {
                    int pick = pool[rng.Next(pool.Count)];
                    pool.Remove(pick);
                    int px = pick % _data.Width, pz = pick / _data.Width;

                    bool apart = true;
                    foreach (Vector2Int other in TagCells)
                        if (Mathf.Max(Mathf.Abs(other.x - px), Mathf.Abs(other.y - pz)) < spacing ||
                            new Vector2Int(other.x / 2, other.y / 2) == new Vector2Int(px / 2, pz / 2)) apart = false;
                    if (!apart || !FreeWall(px, pz, out _)) continue;
                    TagCells.Add(new Vector2Int(px, pz));
                }
            }

            for (int i = 0; i < TagCells.Count; i++)
            {
                Vector2Int cell = TagCells[i];
                FreeWall(cell.x, cell.y, out WallSide side);
                MountLabel($"Etiqueta_{i + 1}", cell.x, cell.y, side, 1.45f, new Vector2(0.6f, 0.4f), ExitLock.TagTexture(digits[i], i + 1));
                Tags++;
                unchecked { _hash = _hash * 31 + cell.x * 131 + cell.y * 7 + digits[i]; }
            }
        }

        // O bilhete na sala inicial, numa parede fechada dela.
        void PlaceNote(int[] digits)
        {
            int x = _data.SpawnX, z = _data.SpawnZ;
            for (int s = 0; s < 4; s++)
            {
                if (!Closed(x, z, s)) continue;
                MountLabel("Bilhete", x, z, Sides[s], 1.4f, new Vector2(0.32f, 0.24f), ExitLock.NoteTexture(digits, _seed));
                return;
            }
        }

        // Uma parede fechada da célula, sem placa de saída e sem móvel encostado na frente dela.
        bool FreeWall(int x, int z, out WallSide side)
        {
            for (int s = 0; s < 4; s++)
            {
                side = Sides[s];
                if (!Closed(x, z, s) || _arrowFaces.Contains((x, z, side))) continue;
                LevelBuilder.WallFace(x, z, side, out Vector3 center, out Vector3 normal);
                Vector3 front = _root.TransformPoint(center + normal * 0.35f + Vector3.up * 1.45f);
                Quaternion turn = _root.rotation * Quaternion.LookRotation(normal, Vector3.up);
                bool blocked = false;
                foreach (Collider hit in Physics.OverlapBox(front, new Vector3(0.4f, 0.4f, 0.3f), turn, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.transform.IsChildOf(_root)) blocked = true;
                if (!blocked) return true;
            }
            side = WallSide.South;
            return false;
        }

        // Um cartão plano pregado na parede, de frente para a célula, sem colisor.
        void MountLabel(string name, int x, int z, WallSide side, float height, Vector2 size, Texture2D texture)
        {
            LevelBuilder.WallFace(x, z, side, out Vector3 center, out Vector3 normal);
            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name = name;
            Collider c = card.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            card.transform.SetParent(_root, false);
            card.transform.localPosition = center + normal * 0.012f + Vector3.up * height;
            card.transform.localRotation = Quaternion.LookRotation(-normal, Vector3.up);
            card.transform.localScale = new Vector3(size.x, size.y, 1f);
            var material = new Material(labelMaterial) { name = name };
            material.SetTexture("_BaseColorMap", texture);
            material.SetColor("_BaseColor", Color.white);
            var renderer = card.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------- Placas e anomalias

        // Placa de papelão pendurada do teto por dois fios, com o texto dos dois lados, acima da
        // cabeça (a borda de baixo fica a ~2,7 m). Sem colisor, como as placas penduradas do Blender.
        void HangSign(Vector3 at, float yaw, string text, Color32 ink, Color32 paper)
        {
            const int w = 128, h = 28;
            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = paper;
            int scale = BackRooms.UI.PixelFont.Measure(text, 2) <= w - 8 ? 2 : 1;
            BackRooms.UI.PixelFont.Draw(pixels, w, h, (w - BackRooms.UI.PixelFont.Measure(text, scale)) / 2, (h - 7 * scale) / 2, text, scale, ink);
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Placa_" + text };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            var material = new Material(labelMaterial) { name = "Placa_" + text };
            material.SetTexture("_BaseColorMap", texture);
            material.SetColor("_BaseColor", Color.white);

            float width = 1.3f, height = width * h / w;
            float bottom = Mathf.Max(2.35f, _ceiling - 0.7f);
            var root = new GameObject("PlacaZona");
            root.transform.SetParent(_root, false);
            root.transform.localPosition = new Vector3(at.x, bottom + height * 0.5f, at.z);
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            for (int face = 0; face < 2; face++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                DestroyColliderOf(quad);
                quad.transform.SetParent(root.transform, false);
                quad.transform.localRotation = Quaternion.Euler(0f, face * 180f, 0f);
                quad.transform.localPosition = new Vector3(0f, 0f, face == 0 ? -0.004f : 0.004f);
                quad.transform.localScale = new Vector3(width, height, 1f);
                var renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Os dois fios até o teto.
            float rope = _ceiling - (bottom + height);
            for (int i = -1; i <= 1; i += 2)
            {
                var string_ = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyColliderOf(string_);
                string_.transform.SetParent(root.transform, false);
                string_.transform.localPosition = new Vector3(i * width * 0.4f, height * 0.5f + rope * 0.5f, 0f);
                string_.transform.localScale = new Vector3(0.006f, rope, 0.006f);
                var renderer = string_.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            _signs++;
            unchecked { _hash = _hash * 31 + Fnv(text) + Mathf.RoundToInt(at.x * 100f) * 3 + Mathf.RoundToInt(at.z * 100f); }
        }

        static void DestroyColliderOf(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c == null) return;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }

        // Uma das anomalias da seção 4, regra 4 do projeto, numa parede ou canto livre da célula.
        void PlaceAnomaly(int x, int z, Vector3 origin, bool[] closed, bool[] signWall, bool[] used, int[][] cornersOfSide,
                          bool[] pillar, float gapW, float gapE, float gapS, float gapN, System.Random rng)
        {
            int kind = rng.Next(4);
            bool placed = false;
            _anomaly = true;
            try
            {
                switch (kind)
                {
                    case 0:
                    {
                        // Mesa de jantar de cabeça para baixo, pregada no teto, no meio da célula.
                        GameObject table = Get("MesaDeJantar");
                        if (table == null) break;
                        float height = BoundsOf(table).size.y;
                        var center = new Rect(Cell * 0.5f - 1.2f, Cell * 0.5f - 1.2f, 2.4f, 2.4f);
                        placed = Put("MesaDeJantar", center, new Vector2(0.5f, 0.5f), rng.Next(2) * 90f, Quaternion.Euler(0f, 0f, 180f),
                                     origin, rng, _ceiling - height, collider: false) != null;
                        break;
                    }
                    case 1:
                    case 2:
                    {
                        // Numa parede livre: móvel atravessando a parede pela metade (1) ou
                        // guarda-roupa virado para a parede (2).
                        for (int side = 0; side < 4 && !placed; side++)
                        {
                            int[] corners = cornersOfSide[side];
                            if (!closed[side] || signWall[side] || used[corners[0]] || used[corners[1]]) continue;
                            Rect strip = WallStrip(side, pillar[corners[0]], pillar[corners[1]], gapW, gapE, gapS, gapN);
                            Vector2 anchor = WallAnchor(side, 0.5f);
                            float yaw = WallYaw(side);
                            if (kind == 2)
                            {
                                placed = Put("GuardaRoupa", strip, anchor, yaw + 180f, Quaternion.identity, origin, rng) != null;
                            }
                            else
                            {
                                placed = HalfThroughWall(x, z, side, origin, rng);
                            }
                            if (placed) used[corners[0]] = used[corners[1]] = true;
                        }
                        break;
                    }
                    default:
                    {
                        // Batente de porta solto, sem parede, num canto livre.
                        for (int c = 0; c < 4 && !placed; c++)
                        {
                            if (used[c]) continue;
                            bool west = c == 0 || c == 2, south = c == 0 || c == 1;
                            float x0 = west ? (pillar[c] ? PillarGap : gapW) : Cell - Inner;
                            float x1 = west ? Inner : Cell - (pillar[c] ? PillarGap : gapE);
                            float z0 = south ? (pillar[c] ? PillarGap : gapS) : Cell - Inner;
                            float z1 = south ? Inner : Cell - (pillar[c] ? PillarGap : gapN);
                            placed = DoorFrame(origin, new Rect(x0, z0, x1 - x0, z1 - z0), rng);
                            if (placed) used[c] = true;
                        }
                        break;
                    }
                }
            }
            finally
            {
                _anomaly = false;
            }

            if (placed)
            {
                _anomalies++;
                unchecked { _hash = _hash * 31 + 1013 + kind * 17 + x * 7 + z; }
            }
        }

        // Um móvel encostado com metade dentro da parede: a outra metade aparece na célula vizinha
        // (se ela puder ter móvel). O colisor é cortado na face da parede (só a metade de cá).
        bool HalfThroughWall(int x, int z, int side, Vector3 origin, System.Random rng)
        {
            if (!_data.Neighbor(x, z, Sides[side], out int nx, out int nz) || !Free(nx, nz) || _data.IsBarricade(nx, nz)) return false;

            string[] choices = { "Sofa", "Estante", "Comoda", "SofaMarromListrado" };
            string name = choices[rng.Next(choices.Length)];
            GameObject model = Get(name);
            if (model == null) return false;

            Bounds local = BoundsOf(model);
            LevelBuilder.WallFace(x, z, Sides[side], out Vector3 face, out Vector3 normal);
            Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up) * Quaternion.Euler(0f, Jitter(rng, 3f), 0f);
            // O meio do móvel fica na face da parede: metade de cada lado.
            Vector3 position = face - rotation * new Vector3(local.center.x, 0f, local.center.z) + normal * (Jitter(rng, 0.05f));
            position.y = -local.min.y;

            GameObject item = Instantiate(model, _root);
            item.name = model.name;
            item.transform.localPosition = position;
            item.transform.localRotation = rotation;

            // Colisor só da metade de cá (da face da parede para a frente).
            var box = item.AddComponent<BoxCollider>();
            float half = local.size.z * 0.5f;
            box.center = new Vector3(local.center.x, local.center.y, local.center.z + half * 0.5f);
            box.size = new Vector3(local.size.x, local.size.y, half);

            if (BlocksLane(position, Rotated(new Bounds(box.center, box.size), rotation)))
            {
                if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
                return false;
            }

            if (local.size.y < ShadowMinHeight)
                foreach (Renderer part in item.GetComponentsInChildren<Renderer>())
                    part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _count++;
            unchecked
            {
                _hash = _hash * 31 + Fnv(name);
                _hash = _hash * 31 + Mathf.RoundToInt(position.x * 1000f);
                _hash = _hash * 31 + Mathf.RoundToInt(position.z * 1000f);
            }
            return true;
        }

        // Batente de porta de madeira em pé, sozinho: dois montantes e a travessa, vão de 0,9 x 2,1 m.
        bool DoorFrame(Vector3 origin, Rect region, System.Random rng)
        {
            if (labelMaterial == null) return false;
            const float opening = 0.9f, tall = 2.1f, bar = 0.1f, deep = 0.15f;
            float width = opening + bar * 2f;
            Vector2 toCenter = new Vector2(Cell * 0.5f, Cell * 0.5f) - region.center;
            float yaw = Mathf.Abs(toCenter.x) > Mathf.Abs(toCenter.y) ? 0f : 90f;
            Quaternion rotation = Quaternion.Euler(0f, yaw + Jitter(rng, 10f), 0f);
            var local = new Bounds(new Vector3(0f, (tall + bar) * 0.5f, 0f), new Vector3(width, tall + bar, deep));
            Bounds rb = Rotated(local, rotation);
            if (region.width < rb.size.x || region.height < rb.size.z) return false;

            Vector3 position = origin + new Vector3(region.center.x, 0f, region.center.y);
            if (BlocksLane(position, rb)) return false;

            var root = new GameObject("BatenteSolto");
            root.transform.SetParent(_root, false);
            root.transform.localPosition = position;
            root.transform.localRotation = rotation;
            var wood = new Material(labelMaterial) { name = "Batente" };
            wood.SetColor("_BaseColor", new Color(0.62f, 0.55f, 0.42f));
            wood.SetTexture("_BaseColorMap", null);

            void Piece(Vector3 center, Vector3 size)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, false);
                cube.transform.localPosition = center;
                cube.transform.localScale = size;
                cube.GetComponent<MeshRenderer>().sharedMaterial = wood;
            }

            // Colisor e malha juntos: cada peça é um cubo com o próprio colisor.
            Piece(new Vector3(-(opening + bar) * 0.5f, tall * 0.5f, 0f), new Vector3(bar, tall, deep));
            Piece(new Vector3((opening + bar) * 0.5f, tall * 0.5f, 0f), new Vector3(bar, tall, deep));
            Piece(new Vector3(0f, tall + bar * 0.5f, 0f), new Vector3(width, bar, deep));

            _count++;
            unchecked { _hash = _hash * 31 + Fnv("BatenteSolto") + Mathf.RoundToInt(position.x * 1000f) * 3 + Mathf.RoundToInt(position.z * 1000f); }
            return true;
        }

        // ---------------------------------------------------------------- Piso

        // Uma placa de piso cobrindo a célula, rente ao carpete, sem colisor (o chão continua o
        // do LevelBuilder). Os passos sabem dela por FloorZones.
        void LayFloor(int x, int z, Material material, FloorKind kind)
        {
            if (material == null) return;

            const float inset = 0.1f;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = kind == FloorKind.Laminate ? "PisoLaminado" : "PisoVinilico";
            Collider c = floor.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            floor.transform.SetParent(_root, false);
            floor.transform.localPosition = new Vector3((x + 0.5f) * Cell, 0.006f, (z + 0.5f) * Cell);
            floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(Cell - inset * 2f, Cell - inset * 2f, 1f);
            var renderer = floor.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Vector3 world = floor.transform.position;
            float half = Cell * 0.5f;
            FloorZones.Add(new Rect(world.x - half, world.z - half, Cell, Cell), kind);
        }

        // ---------------------------------------------------------------- Plástico-bolha

        // Uma folha de 1,5 x 1,0 m no chão, atravessando a cruz livre da célula (é onde se pisa).
        // Sem colisor: o perigo é o barulho.
        void PlaceBubbleWrap(Vector3 origin, System.Random rng)
        {
            if (bubbleWrapMaterial == null) return;

            bool alongX = rng.NextDouble() < 0.5;
            float w = alongX ? 1.5f : 1.0f, d = alongX ? 1.0f : 1.5f;
            float cx = Cell * 0.5f + Jitter(rng, 0.5f), cz = Cell * 0.5f + Jitter(rng, 0.5f);

            var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sheet.name = "PlasticoBolha";
            if (Application.isPlaying) Destroy(sheet.GetComponent<Collider>()); else DestroyImmediate(sheet.GetComponent<Collider>());
            sheet.transform.SetParent(_root, false);
            sheet.transform.localPosition = origin + new Vector3(cx, 0.012f, cz);
            sheet.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sheet.transform.localScale = new Vector3(w, d, 1f);
            var renderer = sheet.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = bubbleWrapMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Vector3 world = sheet.transform.position;
            BubbleWrap.Add(new Rect(world.x - w * 0.5f, world.z - d * 0.5f, w, d));
            _sheets++;
            unchecked { _hash = _hash * 31 + 991 + Mathf.RoundToInt(cx * 100f) * 7 + Mathf.RoundToInt(cz * 100f); }
        }

        // ---------------------------------------------------------------- Temas

        System.Random Rng(int salt, int a, int b)
        {
            unchecked
            {
                return new System.Random(_seed * 73856093 ^ a * 19349663 ^ b * 83492791 ^ salt * 2654435);
            }
        }

        bool Free(int x, int z)
        {
            return !_data.InRoom(x, z) && !(x == _data.ExitX && z == _data.ExitZ) && !_data.Pit[x, z];
        }

        // Um bloco inteiro dentro do mapa, longe da sala inicial e da saída, vira a sala dos sofás.
        Vector2Int ChooseSofaBlock()
        {
            var candidates = new List<Vector2Int>();
            for (int bz = 0; bz < _data.Height / 2; bz++)
            {
                for (int bx = 0; bx < _data.Width / 2; bx++)
                {
                    bool ok = true;
                    for (int dz = 0; dz < 2 && ok; dz++)
                        for (int dx = 0; dx < 2 && ok; dx++)
                            ok = Free(bx * 2 + dx, bz * 2 + dz);

                    int fromSpawn = Mathf.Abs(bx * 2 - _data.SpawnX) + Mathf.Abs(bz * 2 - _data.SpawnZ);
                    if (ok && fromSpawn >= 4) candidates.Add(new Vector2Int(bx, bz));
                }
            }

            if (candidates.Count == 0) return new Vector2Int(-1, -1);
            return candidates[Rng(91, 0, 0).Next(candidates.Count)];
        }

        Theme ThemeOf(int x, int z)
        {
            var block = new Vector2Int(x / 2, z / 2);
            if (block == SofaRoomBlock) return Theme.Sofas;
            return Pick(Rng(37, block.x, block.y), ThemeWeights);
        }

        static T Pick<T>(System.Random rng, (T, float)[] options)
        {
            float total = 0f;
            foreach ((T _, float w) in options) total += w;
            float roll = (float)rng.NextDouble() * total;
            foreach ((T value, float w) in options)
            {
                roll -= w;
                if (roll <= 0f) return value;
            }
            return options[options.Length - 1].Item1;
        }

        // ---------------------------------------------------------------- Célula

        // Lados: 0 sul (-Z), 1 norte (+Z), 2 oeste (-X), 3 leste (+X).
        static readonly WallSide[] Sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

        bool Closed(int x, int z, int side)
        {
            if (!_data.HasWall(x, z, Sides[side])) return false;
            switch (side)
            {
                case 0: return !_data.DoorSouth[x, z];
                case 1: return !_data.DoorSouth[x, z + 1];
                case 2: return !_data.DoorWest[x, z];
                default: return !_data.DoorWest[x + 1, z];
            }
        }

        void PlaceCell(int x, int z)
        {
            if (!Free(x, z)) return;

            if (_data.IsBarricade(x, z))
            {
                FillBarricade(x, z);
                return;
            }

            Theme theme = ThemeOf(x, z);
            bool totemHere = _totemCells.Contains(x + z * _data.Width);

            // Piso da zona: laminado nas vitrines de jantar e de estar, vinílico no estoque.
            if (theme == Theme.Jantar || theme == Theme.Estar) LayFloor(x, z, laminateMaterial, FloorKind.Laminate);
            else if (theme == Theme.Estoque) LayFloor(x, z, vinylMaterial, FloorKind.Vinyl);
            if (theme == Theme.Vazio && !totemHere) return;

            System.Random rng = Rng(53, x, z);
            float wear = LevelGenerator.WearAt(_data, x, z);
            var origin = new Vector3(x * Cell, 0f, z * Cell);

            bool[] closed = new bool[4];
            for (int s = 0; s < 4; s++) closed[s] = Closed(x, z, s);

            // Pilares nos quatro cantos: SO, SE, NO, NE.
            bool[] pillar =
            {
                _data.Pillar[x, z], _data.Pillar[x + 1, z], _data.Pillar[x, z + 1], _data.Pillar[x + 1, z + 1],
            };

            float gapW = closed[2] ? WallGap : OpenGap;
            float gapE = closed[3] ? WallGap : OpenGap;
            float gapS = closed[0] ? WallGap : OpenGap;
            float gapN = closed[1] ? WallGap : OpenGap;

            // Cantos usados por um móvel de parede: 0 SO, 1 SE, 2 NO, 3 NE.
            bool[] used = new bool[4];
            int[][] cornersOfSide = { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 0, 2 }, new[] { 1, 3 } };

            // Parede com placa de saída: nada encosta nela nem nos dois cantos dela, para a placa
            // ficar à vista. A parede continua valendo como parede para a folga dos cantos.
            bool[] signWall = new bool[4];
            for (int side = 0; side < 4; side++)
            {
                signWall[side] = _arrowFaces.Contains((x, z, Sides[side]));
                if (signWall[side]) used[cornersOfSide[side][0]] = used[cornersOfSide[side][1]] = true;
            }

            float chance = theme == Theme.Sofas ? 1f : wallChance;
            for (int side = 0; side < 4; side++)
            {
                if (!closed[side] || signWall[side] || rng.NextDouble() > chance) continue;

                int[] corners = cornersOfSide[side];
                bool lowEndPillar = pillar[corners[0]], highEndPillar = pillar[corners[1]];
                Rect strip = WallStrip(side, lowEndPillar, highEndPillar, gapW, gapE, gapS, gapN);

                if (WallItem(theme, side, strip, origin, rng, wear))
                {
                    used[corners[0]] = true;
                    used[corners[1]] = true;
                }
            }

            // Totem: o primeiro canto livre em que ele couber. Sequência própria da seed, para
            // não mudar a mobília das outras células.
            if (totemHere)
            {
                System.Random totemRng = Rng(239, x, z);
                for (int c = 0; c < 4; c++)
                {
                    if (used[c]) continue;
                    bool west = c == 0 || c == 2, south = c == 0 || c == 1;
                    float x0 = west ? (pillar[c] ? PillarGap : gapW) : Cell - Inner;
                    float x1 = west ? Inner : Cell - (pillar[c] ? PillarGap : gapE);
                    float z0 = south ? (pillar[c] ? PillarGap : gapS) : Cell - Inner;
                    float z1 = south ? Inner : Cell - (pillar[c] ? PillarGap : gapN);
                    var region = new Rect(x0, z0, x1 - x0, z1 - z0);
                    if (!PlaceTotem(region, new Vector2(west ? 0.1f : 0.9f, south ? 0.1f : 0.9f), origin, totemRng)) continue;
                    used[c] = true;
                    break;
                }
            }

            // Plástico-bolha: mais no estoque, às vezes perdido longe do início.
            if (bubbleWrapMaterial != null)
            {
                System.Random wrapRng = Rng(241, x, z);
                double wrapChance = theme == Theme.Estoque ? 0.35 : theme == Theme.Vazio ? 0.0 : 0.06 * wear;
                if (wrapRng.NextDouble() < wrapChance) PlaceBubbleWrap(origin, wrapRng);
            }

            // Anomalia: longe do início, de vez em quando, numa parede ou num canto que sobrou.
            if (anomalyChance > 0f && wear >= 0.55f)
            {
                System.Random oddRng = Rng(263, x, z);
                if (oddRng.NextDouble() < anomalyChance)
                    PlaceAnomaly(x, z, origin, closed, signWall, used, cornersOfSide, pillar, gapW, gapE, gapS, gapN, oddRng);
            }

            // Placa da zona: uma por bloco de 2 x 2, pendurada no meio da primeira célula dele.
            if (zoneSigns && labelMaterial != null && x % 2 == 0 && z % 2 == 0)
            {
                string zone = theme == Theme.Jantar ? "SALA DE JANTAR" : theme == Theme.Estar ? "SALA DE ESTAR" : theme == Theme.Estoque ? "ESTOQUE" : null;
                System.Random signRng = Rng(269, x, z);
                if (zone != null && signRng.NextDouble() < 0.7)
                    HangSign(origin + new Vector3(Cell * 0.5f, 0f, Cell * 0.5f), signRng.Next(2) * 90f + Jitter(signRng, 8f), zone,
                             new Color32(84, 52, 30, 255), new Color32(222, 206, 168, 255));
            }

            if (theme == Theme.Vazio) return;

            for (int c = 0; c < 4; c++)
            {
                if (used[c]) continue;
                if (rng.NextDouble() > (theme == Theme.Sofas ? 0.95f : cornerChance)) continue;

                bool west = c == 0 || c == 2, south = c == 0 || c == 1;
                float x0 = west ? (pillar[c] ? PillarGap : gapW) : Cell - Inner;
                float x1 = west ? Inner : Cell - (pillar[c] ? PillarGap : gapE);
                float z0 = south ? (pillar[c] ? PillarGap : gapS) : Cell - Inner;
                float z1 = south ? Inner : Cell - (pillar[c] ? PillarGap : gapN);
                var region = new Rect(x0, z0, x1 - x0, z1 - z0);

                // Encostado no canto de fora, virado (mais ou menos) para o meio da célula.
                var anchor = new Vector2(west ? 0.1f : 0.9f, south ? 0.1f : 0.9f);
                Vector2 toCenter = new Vector2(Cell * 0.5f, Cell * 0.5f) - region.center;
                float facing = Mathf.Atan2(toCenter.x, toCenter.y) * Mathf.Rad2Deg;
                CornerItem(theme, region, anchor, facing, origin, rng, wear);
            }

            // Loja: placa pendurada do teto no meio da célula, acima da cabeça.
            if (theme == Theme.Loja && rng.NextDouble() < 0.4)
            {
                string sign = rng.NextDouble() < 0.5 ? "PlacaPenduradaOferta" : "PlacaPenduradaLiquida";
                GameObject model = Get(sign);
                if (model != null)
                {
                    float hang = _ceiling - BoundsOf(model).size.y;
                    var center = new Rect(Cell * 0.5f - 0.6f, Cell * 0.5f - 0.2f, 1.2f, 0.4f);
                    Put(sign, center, new Vector2(0.5f, 0.5f), rng.Next(4) * 90f + Jitter(rng, 10f), Quaternion.identity,
                        origin, rng, hang, collider: false);
                }
            }
        }

        // Placas de curva pregadas onde o gerador marcou as setas do caminho até a saída. A
        // versão (seta virando para a direita ou para a esquerda de quem olha) sai do lado para
        // onde o caminho segue, como a seta pintada do Level 0. Sem colisor: fica rente à parede.
        void MountExitSigns()
        {
            GameObject right = Get("PlacaCurvaDireita"), left = Get("PlacaCurvaEsquerda");
            if (right == null || left == null) return;

            foreach (Decoration mark in _data.Decorations)
            {
                if (mark.Kind != DecorationKind.Arrow) continue;
                // A sala inicial fica vazia: a placa da primeira curva vai do lado de fora.
                if (_data.InRoom(mark.X, mark.Z)) continue;

                LevelBuilder.WallFace(mark.X, mark.Z, mark.Side, out Vector3 center, out Vector3 normal);
                Vector3 tangent = Mathf.Abs(normal.z) > 0.5f ? Vector3.right : Vector3.forward;
                Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);

                // A frente da placa (+Z) olha para dentro da célula; a direita de quem olha é o -X dela.
                var direction = new Vector3(mark.DirX, 0f, mark.DirZ);
                GameObject model = Vector3.Dot(rotation * Vector3.left, direction) >= 0f ? right : left;

                Bounds local = BoundsOf(model);
                Vector3 at = center + normal * (local.extents.z + 0.012f) + tangent * mark.Along + Vector3.up * mark.Height;
                Spawn(model, at - rotation * local.center, rotation, local, collider: false);
            }
        }

        // Faixa ao longo de uma parede fechada: da quina (pilar ou parede) até a outra, e da
        // face da parede até a cruz livre.
        static Rect WallStrip(int side, bool lowPillar, bool highPillar, float gapW, float gapE, float gapS, float gapN)
        {
            float depth = Inner + Overhang;
            switch (side)
            {
                case 0:
                case 1:
                {
                    float a = lowPillar ? PillarGap : gapW;
                    float b = Cell - (highPillar ? PillarGap : gapE);
                    return side == 0
                        ? new Rect(a, WallGap, b - a, depth - WallGap)
                        : new Rect(a, Cell - depth, b - a, depth - WallGap);
                }
                default:
                {
                    float a = lowPillar ? PillarGap : gapS;
                    float b = Cell - (highPillar ? PillarGap : gapN);
                    return side == 2
                        ? new Rect(WallGap, a, depth - WallGap, b - a)
                        : new Rect(Cell - depth, a, depth - WallGap, b - a);
                }
            }
        }

        // Giro que deixa a frente do móvel (+Z dele) virada para dentro da célula.
        static float WallYaw(int side)
        {
            switch (side)
            {
                case 0: return 0f;
                case 1: return 180f;
                case 2: return 90f;
                default: return 270f;
            }
        }

        // Ponto da faixa encostado na parede (0 ou 1 no eixo de fundo), com folga sorteada ao longo.
        static Vector2 WallAnchor(int side, float along)
        {
            switch (side)
            {
                case 0: return new Vector2(along, 0f);
                case 1: return new Vector2(along, 1f);
                case 2: return new Vector2(0f, along);
                default: return new Vector2(1f, along);
            }
        }

        // ---------------------------------------------------------------- Móveis de parede

        bool WallItem(Theme theme, int side, Rect strip, Vector3 origin, System.Random rng, float wear)
        {
            float yaw = WallYaw(side);
            float along = (float)rng.NextDouble();
            Vector2 anchor = WallAnchor(side, along);

            string choice;
            switch (theme)
            {
                case Theme.Jantar:
                    choice = Pick(rng, new[] { ("@jantar", 4f), ("Comoda", 2f), ("GuardaRoupa", 1.5f), ("Estante", 1.5f), ("@comoda_placa", 1f) });
                    break;
                case Theme.Estar:
                    choice = Pick(rng, new[] { ("Sofa", 3f), ("Estante", 2f), ("Comoda", 1.5f), ("EstanteAbertaMadeira", 1f), ("SofaMarromListrado", 1f), ("GuardaRoupa", 1f) });
                    break;
                case Theme.Loja:
                    choice = Pick(rng, new[] { ("EstanteAbertaMadeira", 3f), ("@torres", 2f), ("@balcao", 1.5f), ("@mesa_monitor", 1f) });
                    break;
                case Theme.Estoque:
                    choice = Pick(rng, new[] { ("EstanteDeAcoCheia", 3f), ("EstanteDeAco", 1.5f), ("@lavadoras", 2f), ("@caixas", 2f) });
                    break;
                case Theme.Bagunca:
                    choice = Pick(rng, new[] { ("@caixas", 2f), ("@pilha_cadeiras", 2f), ("@placa_encostada", 2f), ("EstanteDeAco", 1f), ("GuardaRoupa", 0.5f) });
                    break;
                case Theme.Sofas:
                    choice = Pick(rng, new[] { ("@sofa_empilhado", 3f), ("NamoradeiraMarromListrada", 1f) });
                    break;
                default:
                    return false;
            }

            switch (choice)
            {
                case "@jantar":
                {
                    // Mesa encostada e cadeiras do lado de fora, viradas para ela.
                    GameObject table = Put("MesaDeJantar", strip, anchor, yaw, Quaternion.identity, origin, rng);
                    if (table == null) return false;
                    string chair = rng.NextDouble() < 0.5 ? "Cadeira" : "CadeiraEncostoRipas";
                    Vector3 front = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                    for (int i = -1; i <= 1; i++)
                    {
                        Vector3 at = table.transform.localPosition + front * 0.62f + right * (i * 0.6f + Jitter(rng, 0.05f));
                        // A cadeira fica na célula da mesa, longe das paredes: sem isso, numa mesa
                        // encostada ela podia atravessar a parede (e cair até na sala inicial).
                        if (!SameCellInside(at, table.transform.localPosition, 0.35f)) continue;
                        PutAt(chair, at, yaw + 180f + Jitter(rng, 8f), Quaternion.identity, rng, wear);
                    }
                    if (rng.NextDouble() < 0.4)
                        PutAt("PlacaOferta", table.transform.localPosition + Vector3.up * 0.76f, yaw + Jitter(rng, 15f), Quaternion.identity, rng, 0f);
                    return true;
                }
                case "@comoda_placa":
                {
                    GameObject chest = Put("Comoda", strip, anchor, yaw, Quaternion.identity, origin, rng);
                    if (chest == null) return false;
                    PutAt(rng.NextDouble() < 0.5 ? "PlacaOferta" : "PlacaLiquidacao",
                          chest.transform.localPosition + Vector3.up * 0.85f, yaw + Jitter(rng, 12f), Quaternion.identity, rng, 0f);
                    return true;
                }
                case "@balcao":
                {
                    GameObject desk = Put("BalcaoDeCaixa", strip, anchor, yaw, Quaternion.identity, origin, rng);
                    if (desk == null) return Put("EstanteAbertaMadeira", strip, anchor, yaw, Quaternion.identity, origin, rng) != null;
                    PutAt("MonitorCRT", desk.transform.localPosition + Vector3.up * 1.05f + Quaternion.Euler(0f, yaw, 0f) * new Vector3(-0.4f, 0f, -0.2f),
                          yaw + 180f + Jitter(rng, 20f), Quaternion.identity, rng, 0f);
                    return true;
                }
                case "@mesa_monitor":
                {
                    GameObject table = Put("MesaSimples", strip, anchor, yaw, Quaternion.identity, origin, rng);
                    if (table == null) return false;
                    PutAt("MonitorCRT", table.transform.localPosition + Vector3.up * 0.75f, yaw + Jitter(rng, 25f), Quaternion.identity, rng, 0f);
                    return true;
                }
                case "@torres":
                    return Row(new[] { "TorreExpositora" }, 2, strip, side, yaw, origin, rng, 0.25f);
                case "@lavadoras":
                    return Row(new[] { "MaquinaDeLavar" }, 4, strip, side, yaw, origin, rng, 0.02f);
                case "@caixas":
                    return Row(new[] { "PilhaDeCaixas", "CaixaDePapelao", "PilhaDeCaixas" }, 3, strip, side, yaw, origin, rng, 0.1f);
                case "@pilha_cadeiras":
                    return ChairPile(strip, anchor, origin, rng);
                case "@placa_encostada":
                {
                    // Placa de trânsito apoiada na parede, inclinada para trás.
                    string sign = Pick(rng, new[] { ("PlacaPare", 1f), ("PlacaStop", 1f), ("PlacaSetas", 1f), ("PlacaAnimal", 1f) });
                    return Put(sign, strip, anchor, yaw + Jitter(rng, 6f), Quaternion.Euler(-14f, 0f, Jitter(rng, 5f)), origin, rng) != null;
                }
                case "@sofa_empilhado":
                {
                    GameObject sofa = Put("SofaMarromListrado", strip, anchor, yaw + Jitter(rng, 6f), Quaternion.identity, origin, rng);
                    if (sofa == null) return false;
                    if (rng.NextDouble() < 0.55)
                    {
                        // Outro estofado por cima, torto, afundado no de baixo.
                        string top = rng.NextDouble() < 0.5 ? "PoltronaMarromListrada" : "NamoradeiraMarromListrada";
                        Vector3 at = sofa.transform.localPosition + Vector3.up * 0.72f + Quaternion.Euler(0f, yaw, 0f) * new Vector3(Jitter(rng, 0.4f), 0f, Jitter(rng, 0.1f));
                        PutAt(top, at, yaw + Jitter(rng, 50f), Quaternion.Euler(Jitter(rng, 14f), 0f, Jitter(rng, 12f)), rng, 0f);
                    }
                    return true;
                }
                default:
                    return Put(choice, strip, anchor, yaw + Jitter(rng, 3f), Quaternion.identity, origin, rng) != null;
            }
        }

        // Fileira do mesmo tipo ao longo da parede (lavadoras, torres, caixas), com falhas.
        bool Row(string[] names, int maxCount, Rect strip, int side, float yaw, Vector3 origin, System.Random rng, float spacing)
        {
            bool horizontal = side < 2;
            float length = horizontal ? strip.width : strip.height;
            float cursor = 0f;
            int placed = 0;

            for (int i = 0; i < maxCount; i++)
            {
                string name = names[i % names.Length];
                GameObject model = Get(name);
                if (model == null) continue;

                Bounds rb = Rotated(BoundsOf(model), Quaternion.Euler(0f, yaw, 0f));
                float size = horizontal ? rb.size.x : rb.size.z;
                if (cursor + size > length + 0.001f) break;

                if (rng.NextDouble() < 0.15) { cursor += size + spacing; continue; } // falha na fileira

                Rect slot = horizontal
                    ? new Rect(strip.x + cursor, strip.y, size, strip.height)
                    : new Rect(strip.x, strip.y + cursor, strip.width, size);
                if (Put(name, slot, WallAnchor(side, 0.5f), yaw + Jitter(rng, 2f), Quaternion.identity, origin, rng) != null) placed++;
                cursor += size + spacing;
            }

            return placed > 0;
        }

        // ---------------------------------------------------------------- Móveis de canto

        void CornerItem(Theme theme, Rect region, Vector2 anchor, float facing, Vector3 origin, System.Random rng, float wear)
        {
            string choice;
            switch (theme)
            {
                case Theme.Jantar:
                    choice = Pick(rng, new[] { ("@cadeira", 4f), ("VasoAlto", 1f), ("CabideiroDePe", 1f), ("@mesa_cadeiras", 1.5f) });
                    break;
                case Theme.Estar:
                    choice = Pick(rng, new[] { ("@poltrona", 4f), ("VasoAlto", 1f), ("CabideiroDePe", 1f), ("@cadeira", 1f) });
                    break;
                case Theme.Loja:
                    choice = Pick(rng, new[] { ("@reclinavel", 4f), ("TorreExpositora", 1.5f), ("CilindroAzul", 1f), ("@placa_preco", 1.5f), ("@mesa_monitor", 0.7f) });
                    break;
                case Theme.Estoque:
                    choice = Pick(rng, new[] { ("PilhaDeCaixas", 3f), ("@caixas_soltas", 2f), ("CilindroAzul", 1f), ("CadeiraDobravel", 1f) });
                    break;
                case Theme.Bagunca:
                    choice = Pick(rng, new[] { ("@pilha_cadeiras", 3f), ("@placa_poste", 2f), ("@placa_no_chao", 1.5f), ("@caixas_soltas", 1f), ("VasoAlto", 0.7f) });
                    break;
                case Theme.Sofas:
                    choice = Pick(rng, new[] { ("@poltrona_marrom", 3f), ("@sofa_em_pe", 1f) });
                    break;
                default:
                    return;
            }

            float tip = 0.06f + 0.4f * wear; // chance de estar tombado
            switch (choice)
            {
                case "@cadeira":
                {
                    string chair = Pick(rng, new[] { ("Cadeira", 1f), ("CadeiraEncostoRipas", 1f) });
                    Put(chair, region, Jit(anchor, rng), facing + Jitter(rng, 40f), Tipped(rng, tip), origin, rng);
                    break;
                }
                case "@poltrona":
                {
                    string chair = Pick(rng, new[] { ("Poltrona", 2f), ("PoltronaMarromListrada", 1f), ("PoltronaReclinavelBege", 1f) });
                    Put(chair, region, anchor, facing + Jitter(rng, 25f), Quaternion.identity, origin, rng);
                    break;
                }
                case "@reclinavel":
                    Put(rng.NextDouble() < 0.5 ? "PoltronaReclinavelBege" : "PoltronaReclinavelAzul", region, Jit(anchor, rng),
                        facing + Jitter(rng, 60f), Quaternion.identity, origin, rng);
                    break;
                case "@mesa_cadeiras":
                {
                    GameObject table = Put("MesaSimples", region, anchor, facing + Jitter(rng, 10f), Quaternion.identity, origin, rng);
                    if (table != null && rng.NextDouble() < 0.6)
                        PutAt("MonitorCRT", table.transform.localPosition + Vector3.up * 0.75f, facing + Jitter(rng, 30f), Quaternion.identity, rng, 0f);
                    break;
                }
                case "@mesa_monitor":
                {
                    GameObject table = Put("MesaSimples", region, anchor, facing, Quaternion.identity, origin, rng);
                    if (table != null)
                        PutAt("MonitorCRT", table.transform.localPosition + Vector3.up * 0.75f, facing + Jitter(rng, 30f), Quaternion.identity, rng, 0f);
                    break;
                }
                case "@placa_preco":
                    Put(rng.NextDouble() < 0.5 ? "PlacaOferta" : "PlacaLiquidacao", region, Jit(anchor, rng), facing + Jitter(rng, 30f),
                        Quaternion.identity, origin, rng);
                    break;
                case "@caixas_soltas":
                {
                    // Duas ou três caixas empilhadas tortas.
                    GameObject box = Put("CaixaDePapelao", region, Jit(anchor, rng), Jitter(rng, 180f), Quaternion.identity, origin, rng);
                    float top = 0.36f;
                    for (int i = 0; box != null && i < rng.Next(1, 3); i++, top += 0.36f)
                        PutAt("CaixaDePapelao", box.transform.localPosition + Vector3.up * top + new Vector3(Jitter(rng, 0.06f), 0f, Jitter(rng, 0.06f)),
                              Jitter(rng, 180f), Quaternion.identity, rng, 0f);
                    break;
                }
                case "@pilha_cadeiras":
                    ChairPile(region, anchor, origin, rng);
                    break;
                case "@placa_poste":
                    Put(Pick(rng, new[] { ("PlacaParePoste", 1f), ("PlacaStopPoste", 1f), ("PlacaSetasPoste", 1f), ("PlacaAnimalPoste", 1f),
                                          ("PlacaCurvaDireitaPoste", 0.5f), ("PlacaCurvaEsquerdaPoste", 0.5f) }), region, Jit(anchor, rng),
                        facing + Jitter(rng, 60f), Quaternion.Euler(Jitter(rng, 6f), 0f, Jitter(rng, 6f)), origin, rng);
                    break;
                case "@placa_no_chao":
                    // Caída de costas no chão.
                    Put(Pick(rng, new[] { ("PlacaPare", 1f), ("PlacaStop", 1f), ("PlacaSetas", 1f), ("PlacaAnimal", 1f) }), region, Jit(anchor, rng),
                        Jitter(rng, 180f), Quaternion.Euler(-88f, 0f, 0f), origin, rng);
                    break;
                case "@poltrona_marrom":
                {
                    GameObject chair = Put("PoltronaMarromListrada", region, Jit(anchor, rng), facing + Jitter(rng, 70f),
                                           rng.NextDouble() < 0.2 ? Quaternion.Euler(-75f, 0f, 0f) : Quaternion.identity, origin, rng);
                    if (chair != null && rng.NextDouble() < 0.45)
                        PutAt("PoltronaMarromListrada", chair.transform.localPosition + Vector3.up * 0.75f, facing + Jitter(rng, 90f),
                              Quaternion.Euler(Jitter(rng, 20f), 0f, Jitter(rng, 16f)), rng, 0f);
                    break;
                }
                case "@sofa_em_pe":
                    // Sofá de pé sobre o braço: a largura vira altura (cabe sob o teto de 4 m).
                    Put(rng.NextDouble() < 0.5 ? "NamoradeiraMarromListrada" : "SofaMarromListrado", region, anchor,
                        facing + Jitter(rng, 20f), Quaternion.Euler(0f, 0f, 90f), origin, rng);
                    break;
                default:
                    Put(choice, region, Jit(anchor, rng), facing + Jitter(rng, 30f), Quaternion.identity, origin, rng);
                    break;
            }
        }

        // Duas a quatro cadeiras empilhadas de qualquer jeito, cada uma assentada na de baixo.
        bool ChairPile(Rect region, Vector2 anchor, Vector3 origin, System.Random rng)
        {
            string[] kinds = { "Cadeira", "CadeiraEncostoRipas", "CadeiraDobravel" };
            GameObject below = Put(kinds[rng.Next(kinds.Length)], region, Jit(anchor, rng), Jitter(rng, 180f), Tipped(rng, 0.5f), origin, rng);
            if (below == null) return false;

            float top = TopOf(below);
            int count = rng.Next(1, 4);
            for (int i = 0; i < count; i++)
            {
                Vector3 at = new Vector3(below.transform.localPosition.x + Jitter(rng, 0.12f), top - 0.12f,
                                         below.transform.localPosition.z + Jitter(rng, 0.12f));
                GameObject chair = PutAt(kinds[rng.Next(kinds.Length)], at, Jitter(rng, 180f),
                                         Quaternion.Euler(Jitter(rng, 60f), 0f, Jitter(rng, 70f)), rng, 0f);
                if (chair == null) break;
                top = TopOf(chair);
            }
            return true;
        }

        static Quaternion Tipped(System.Random rng, float chance)
        {
            if (rng.NextDouble() >= chance) return Quaternion.identity;
            // Caído de costas ou de lado.
            return rng.NextDouble() < 0.5 ? Quaternion.Euler(-90f, 0f, Jitter(rng, 10f)) : Quaternion.Euler(Jitter(rng, 10f), 0f, 90f);
        }

        static float Jitter(System.Random rng, float amount)
        {
            return ((float)rng.NextDouble() * 2f - 1f) * amount;
        }

        static Vector2 Jit(Vector2 anchor, System.Random rng)
        {
            return new Vector2(Mathf.Clamp01(anchor.x + Jitter(rng, 0.15f)), Mathf.Clamp01(anchor.y + Jitter(rng, 0.15f)));
        }

        // ---------------------------------------------------------------- Pôr no lugar

        GameObject Get(string name)
        {
            return _byName.TryGetValue(name, out GameObject model) ? model : null;
        }

        /// <summary>
        /// Põe o modelo dentro de uma região da célula (coordenadas da célula, 0 a 4 m): gira
        /// (yaw em Y e depois tilt), confere se a caixa girada cabe e assenta no chão (ou em
        /// baseY). anchor diz onde, na folga que sobra, ele fica (0 = começo, 1 = fim).
        /// Devolve null, sem gastar nada, se não couber ou o modelo não existir.
        /// </summary>
        GameObject Put(string name, Rect region, Vector2 anchor, float yaw, Quaternion tilt, Vector3 origin, System.Random rng,
                       float baseY = 0f, bool collider = true)
        {
            GameObject model = Get(name);
            if (model == null) return null;

            Bounds local = BoundsOf(model);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f) * tilt;
            Bounds rb = Rotated(local, rotation);

            float slackX = region.width - rb.size.x;
            float slackZ = region.height - rb.size.z;
            if (slackX < -0.001f || slackZ < -0.001f) return null;

            var position = new Vector3(
                region.x + Mathf.Max(0f, slackX) * anchor.x - rb.min.x,
                baseY - rb.min.y,
                region.y + Mathf.Max(0f, slackZ) * anchor.y - rb.min.z);

            return Spawn(model, origin + position, rotation, local, collider);
        }

        /// <summary>
        /// Põe o modelo num ponto já escolhido (em coordenadas do nível): em cima de outro móvel,
        /// numa cadeira diante da mesa. A caixa girada assenta na altura do ponto.
        /// </summary>
        GameObject PutAt(string name, Vector3 at, float yaw, Quaternion tilt, System.Random rng, float wear)
        {
            GameObject model = Get(name);
            if (model == null) return null;

            Bounds local = BoundsOf(model);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f) * Tipped(rng, wear * 0.3f) * tilt;
            Bounds rb = Rotated(local, rotation);
            Vector3 position = new Vector3(at.x - rb.center.x, at.y - rb.min.y, at.z - rb.center.z);
            return Spawn(model, position, rotation, local, true);
        }

        // Guarda-roupa em pé e mesa de jantar em pé viram esconderijo (tecla E). A frente dos
        // modelos é +Z: sai-se por ali, para a cruz livre da célula.
        void AddHideSpot(GameObject item, string name, Bounds local)
        {
            bool upright = Vector3.Angle(item.transform.up, _root.up) < 5f;
            if (!upright || _anomaly) return;

            HideSpot spot;
            if (name == "GuardaRoupa")
            {
                spot = item.AddComponent<HideSpot>();
                spot.kind = BackRooms.Player.HideKind.Wardrobe;
                spot.inside = new Vector3(local.center.x, 0f, local.center.z);
                spot.exit = new Vector3(local.center.x, 0f, local.max.z + 1.0f);
                spot.eye = 1.55f;
                var doors = new List<Transform>();
                foreach (Transform child in item.GetComponentsInChildren<Transform>())
                    if (child.name.Contains("_Porta_")) doors.Add(child);
                spot.doors = doors.ToArray();
            }
            else if (name == "MesaDeJantar")
            {
                spot = item.AddComponent<HideSpot>();
                spot.kind = BackRooms.Player.HideKind.Table;
                spot.inside = new Vector3(local.center.x, 0f, local.center.z);
                spot.exit = new Vector3(local.center.x, 0f, local.max.z + 0.95f);
                spot.eye = 0.45f;
                spot.doors = new Transform[0];
            }
            else
            {
                return;
            }

            _hideSpots++;
        }

        // A caixa (encolhida 10 cm, a folga da parede) entra em alguma célula proibida ou fora do mapa?
        bool Intrudes(Vector3 position, Bounds rotated)
        {
            const float shrink = 0.1f;
            Vector3 min = position + rotated.min, max = position + rotated.max;
            int x0 = Mathf.FloorToInt((min.x + shrink) / Cell), x1 = Mathf.FloorToInt((max.x - shrink) / Cell);
            int z0 = Mathf.FloorToInt((min.z + shrink) / Cell), z1 = Mathf.FloorToInt((max.z - shrink) / Cell);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (x < 0 || z < 0 || x >= _data.Width || z >= _data.Height || !Free(x, z)) return true;
            return false;
        }

        // O ponto está na mesma célula que a referência e a mais de "margin" das bordas dela?
        static bool SameCellInside(Vector3 point, Vector3 reference, float margin)
        {
            int cx = Mathf.FloorToInt(reference.x / Cell), cz = Mathf.FloorToInt(reference.z / Cell);
            float lx = point.x - cx * Cell, lz = point.z - cz * Cell;
            return lx >= margin && lx <= Cell - margin && lz >= margin && lz <= Cell - margin;
        }

        GameObject Spawn(GameObject model, Vector3 localPosition, Quaternion rotation, Bounds local, bool collider)
        {
            // Nada invade uma célula que fica vazia (sala inicial, saída), nem sai do mapa: confere
            // a caixa inteira, girada (cadeira tombada gira pelo pé e pode atravessar a parede).
            if (Intrudes(localPosition, Rotated(local, rotation))) return null;

            // Dentro de uma barricada vale tudo: a cruz fica entupida de propósito e o colisor é
            // um só, da célula inteira.
            if (_barricading) collider = false;

            // Última garantia da cruz livre, para tudo que tem colisor: o que é posto por ponto
            // (cadeira diante da mesa, cadeira em cima da pilha, móvel tombado) pode passar da
            // região. Se a caixa invade a cruz da célula, o móvel não entra.
            if (collider && BlocksLane(localPosition, Rotated(local, rotation))) return null;

            GameObject item = Instantiate(model, _root);
            item.name = model.name;
            item.transform.localPosition = localPosition;
            item.transform.localRotation = rotation;

            if (collider)
            {
                var box = item.AddComponent<BoxCollider>();
                box.center = local.center;
                box.size = local.size;
            }

            if (collider) AddHideSpot(item, model.name, local);

            // Só móvel alto projeta sombra (level1-mostruario.md, seção 4, regra 7): são mais de
            // mil no mapa e cada luminária guarda a sombra de tudo o que alcança.
            if (local.size.y < ShadowMinHeight)
                foreach (Renderer part in item.GetComponentsInChildren<Renderer>())
                    part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _count++;
            unchecked
            {
                _hash = _hash * 31 + Fnv(model.name);
                _hash = _hash * 31 + Mathf.RoundToInt(localPosition.x * 1000f);
                _hash = _hash * 31 + Mathf.RoundToInt(localPosition.y * 1000f);
                _hash = _hash * 31 + Mathf.RoundToInt(localPosition.z * 1000f);
                _hash = _hash * 31 + Mathf.RoundToInt(rotation.eulerAngles.y * 10f);
            }
            return item;
        }

        // A cruz livre, um pouco mais estreita que a usada para planejar (o balcão pode avançar
        // Overhang nela): faixa de 2 x (Lane - Overhang) no meio da célula, nos dois sentidos.
        static bool BlocksLane(Vector3 position, Bounds rotated)
        {
            Vector3 min = position + rotated.min, max = position + rotated.max;
            Vector3 center = (min + max) * 0.5f;
            float ox = Mathf.Floor(center.x / Cell) * Cell, oz = Mathf.Floor(center.z / Cell) * Cell;
            float half = Lane - Overhang - 0.01f;
            float mid = Cell * 0.5f;

            bool crossesX = max.x > ox + mid - half && min.x < ox + mid + half; // faixa norte-sul
            bool crossesZ = max.z > oz + mid - half && min.z < oz + mid + half; // faixa leste-oeste
            bool insideZ = max.z > oz && min.z < oz + Cell;
            bool insideX = max.x > ox && min.x < ox + Cell;
            return (crossesX && insideZ) || (crossesZ && insideX);
        }

        float TopOf(GameObject item)
        {
            Bounds rb = Rotated(BoundsOf(_byName[item.name]), item.transform.localRotation);
            return item.transform.localPosition.y + rb.max.y;
        }

        /// <summary>Caixa do modelo no espaço dele (a raiz do FBX na origem, sem giro).</summary>
        Bounds BoundsOf(GameObject model)
        {
            if (_bounds.TryGetValue(model, out Bounds cached)) return cached;

            bool any = false;
            var bounds = new Bounds();
            Matrix4x4 toRoot = model.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds mb = Transformed(filter.sharedMesh.bounds, toRoot * filter.transform.localToWorldMatrix);
                if (!any) { bounds = mb; any = true; }
                else bounds.Encapsulate(mb);
            }

            if (!any) bounds = new Bounds(Vector3.up * 0.5f, Vector3.one);
            _bounds[model] = bounds;
            return bounds;
        }

        static Bounds Rotated(Bounds b, Quaternion rotation)
        {
            return Transformed(b, Matrix4x4.Rotate(rotation));
        }

        static Bounds Transformed(Bounds b, Matrix4x4 m)
        {
            Vector3 c = b.center, e = b.extents;
            var result = new Bounds(m.MultiplyPoint3x4(c + new Vector3(-e.x, -e.y, -e.z)), Vector3.zero);
            for (int i = 1; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                result.Encapsulate(m.MultiplyPoint3x4(c + corner));
            }
            return result;
        }

        static int Fnv(string text)
        {
            unchecked
            {
                int hash = (int)2166136261;
                foreach (char ch in text) hash = (hash ^ ch) * 16777619;
                return hash;
            }
        }
    }
}
