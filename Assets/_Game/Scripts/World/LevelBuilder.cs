using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    /// <summary>
    /// Constrói o Level 0 em tempo de execução a partir de uma seed: malhas combinadas,
    /// colisores primitivos, luminárias, esconderijos, marcas nas paredes e a saída.
    /// O mapa só existe durante o Play.
    /// </summary>
    public sealed class LevelBuilder : MonoBehaviour
    {
        public const float Cell = 4f;
        public const float WallHeight = 2.7f;
        public const float WallThickness = 0.2f;

        const float CeilingTile = 0.6f;
        const float BaseboardHeight = 0.1f;
        const float PillarSize = 0.8f;
        const float PanelWidth = 0.6f;
        const float PanelLength = 1.2f;

        // Parede grossa com nicho (medidas em metros).
        const float AlcoveHalfThickness = 0.7f;
        const float AlcoveShell = 0.2f;
        const float AlcoveCavityHalfLength = 1.2f;
        const float AlcoveCavityHeight = 1.9f;
        const float AlcoveOpeningHalfWidth = 0.45f;
        const float AlcoveOpeningHeight = 1.25f;

        [Header("Geração")]
        public int seed = 1998;
        [Min(4)] public int width = 16;
        [Min(4)] public int height = 16;

        [Header("Materiais")]
        public Material wallMaterial;
        public Material floorMaterial;
        public Material ceilingMaterial;
        public Material baseboardMaterial;
        public Material panelOnMaterial;
        public Material panelOffMaterial;

        [Header("Luminárias")]
        [Tooltip("Intensidade em candela.")]
        public float lightCandela = 1200f;
        public float lightRange = 12f;
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

        [Header("Saída")]
        public Material exitDoorMaterial;
        public Material exitSignMaterial;

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
        /// de um quadrado no meio da célula inicial, para ninguém nascer dentro de ninguém.
        /// </summary>
        public Vector3 SpawnPosition(int index)
        {
            if (Data == null) return transform.position;

            int slot = Mathf.Abs(index) % 4;
            var offset = new Vector3(slot % 2 == 0 ? -0.7f : 0.7f, 0.05f, slot < 2 ? -0.7f : 0.7f);
            return CellCenter(Data.SpawnX, Data.SpawnZ) + offset;
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

            GameObject colliders = CreateChild("Colisores");
            GameObject hidingSpots = CreateChild("Esconderijos");

            // Piso e teto: um quad cada, com colisor espesso para ninguém atravessar.
            floor.AddQuad(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, sizeZ),
                          new Vector3(sizeX, 0f, sizeZ), new Vector3(sizeX, 0f, 0f), Vector3.up);
            AddCollider(colliders, new Vector3(sizeX * 0.5f, -0.25f, sizeZ * 0.5f), new Vector3(sizeX + 2f, 0.5f, sizeZ + 2f));

            ceiling.AddQuad(new Vector3(0f, WallHeight, 0f), new Vector3(0f, WallHeight, sizeZ),
                            new Vector3(sizeX, WallHeight, sizeZ), new Vector3(sizeX, WallHeight, 0f), Vector3.down);
            AddCollider(colliders, new Vector3(sizeX * 0.5f, WallHeight + 0.25f, sizeZ * 0.5f), new Vector3(sizeX + 2f, 0.5f, sizeZ + 2f));

            // As paredes passam 10 cm de cada ponta para fechar os cantos.
            float wallLength = Cell + WallThickness;

            for (int z = 0; z <= Data.Height; z++)
            {
                for (int x = 0; x < Data.Width; x++)
                {
                    if (!Data.WallSouth[x, z]) continue;

                    var origin = new Vector3((x + 0.5f) * Cell, 0f, z * Cell);
                    if (Data.AlcoveSouth[x, z] != 0)
                        AddAlcove(walls, baseboards, colliders, hidingSpots, origin, Vector3.right, Vector3.forward,
                                  Data.AlcoveSouth[x, z] == 1 ? -1f : 1f);
                    else
                        AddWall(walls, baseboards, colliders, origin, new Vector3(wallLength, WallHeight, WallThickness));
                }
            }

            for (int x = 0; x <= Data.Width; x++)
            {
                for (int z = 0; z < Data.Height; z++)
                {
                    if (!Data.WallWest[x, z]) continue;

                    var origin = new Vector3(x * Cell, 0f, (z + 0.5f) * Cell);
                    if (Data.AlcoveWest[x, z] != 0)
                        AddAlcove(walls, baseboards, colliders, hidingSpots, origin, Vector3.forward, Vector3.right,
                                  Data.AlcoveWest[x, z] == 1 ? -1f : 1f);
                    else
                        AddWall(walls, baseboards, colliders, origin, new Vector3(WallThickness, WallHeight, wallLength));
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
            CreateRenderer("Rodapes", baseboards.ToMesh("Rodapes"), baseboardMaterial, ShadowCastingMode.Off);

            BuildLights();
            BuildDecorations();
            BuildExit();
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
                    panel.transform.localPosition = new Vector3(px, WallHeight - 0.004f, pz);

                    HDAdditionalLightData[] lights = null;
                    if (state != LightState.Dead)
                        lights = new[] { CreateCeilingLight(panel.transform, shadowResolution) };

                    panel.gameObject.AddComponent<CeilingLight>().Init(state, lights, panel, panelOnMaterial, panelOffMaterial);
                }
            }
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

            foreach (Decoration mark in Data.Decorations)
            {
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
                AddDecal(root.transform, material, wallPosition, rotation, new Vector3(size.x, size.y, 0.16f), isGrime && mark.Flip);
            }
        }

        // Tamanho e altura de cada tipo de mancha de parede.
        static void WallStainLayout(Decoration mark, out Vector2 size, out float centerHeight)
        {
            switch (mark.Variant)
            {
                case 0:
                    // Escorrido: encosta no teto e desce.
                    size = new Vector2(Mathf.Lerp(1.0f, 2.6f, mark.Scale), Mathf.Lerp(1.1f, 2.5f, mark.Height));
                    centerHeight = WallHeight - size.y * 0.5f;
                    break;
                case 1:
                    // Umidade: nasce no rodapé e sobe.
                    size = new Vector2(Mathf.Lerp(1.8f, 3.8f, mark.Scale), Mathf.Lerp(0.6f, 1.4f, mark.Height));
                    centerHeight = size.y * 0.5f;
                    break;
                default:
                    // Mancha solta, em qualquer altura.
                    float side = Mathf.Lerp(0.9f, 2.3f, mark.Scale);
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
            AddDecal(root, material, position, Quaternion.Euler(90f, mark.Yaw, 0f), new Vector3(size, size, 0.2f), mark.Flip);
        }

        static Material Pick(Material[] materials, int variant)
        {
            if (materials == null || materials.Length == 0) return null;
            return materials[Mathf.Abs(variant) % materials.Length];
        }

        static void AddDecal(Transform parent, Material material, Vector3 position, Quaternion rotation, Vector3 size, bool flip = false)
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
            MeshRenderer door = CreateRenderer("Porta", CreateQuadMesh("Porta", 0.95f, 2.05f, horizontal: false),
                                               exitDoorMaterial, ShadowCastingMode.Off, root.transform);
            door.transform.localPosition = new Vector3(0f, 1.025f, -0.012f);

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
