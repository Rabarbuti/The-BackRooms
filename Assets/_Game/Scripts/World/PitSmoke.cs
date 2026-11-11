using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    /// <summary>
    /// Fumaça que tapa o fundo de um buraco: quem olha de cima não vê onde a queda termina.
    /// São camadas horizontais de névoa translúcida, cada uma mais escura e mais densa que a
    /// de cima, deslizando devagar em direções diferentes, sobre uma tampa preta opaca. Isso
    /// funciona em qualquer perfil gráfico. Nos perfis com névoa volumétrica, um volume de
    /// névoa do HDRP soma por cima e dá corpo à fumaça.
    /// Nada aqui tem colisor: o jogador atravessa a fumaça e cai.
    /// </summary>
    public sealed class PitSmoke : MonoBehaviour
    {
        /// <summary>Altura em que a fumaça começa e em que fica a tampa preta (o piso é 0).</summary>
        public const float Top = -1.0f;
        public const float Bottom = -3.4f;

        const int Layers = 6;
        const float MetersPerTile = 7f;

        // O material não recebe luz: a cor é o que aparece na tela (0 = preto, 1 = branco).
        // A camada de cima é um cinza escuro; a de baixo, quase preta.
        const float TopBrightness = 0.12f;
        const float BottomBrightness = 0.006f;
        const float TopOpacity = 0.42f;
        const float BottomOpacity = 0.9f;

        static readonly int ColorId = Shader.PropertyToID("_UnlitColor");
        static readonly int TilingId = Shader.PropertyToID("_UnlitColorMap_ST");

        MeshRenderer[] _layers;
        Vector2[] _drift;
        Vector2[] _offset;
        Vector2 _tiling;
        MaterialPropertyBlock _block;
        Mesh _mesh;

        /// <summary>Monta a fumaça de um buraco de <paramref name="sizeX"/> por <paramref name="sizeZ"/> metros, centrada neste objeto.</summary>
        public void Build(float sizeX, float sizeZ, Material smoke, Material lid, int variation)
        {
            _mesh = CreateSheet(sizeX, sizeZ);
            _block = new MaterialPropertyBlock();
            _tiling = new Vector2(sizeX / MetersPerTile, sizeZ / MetersPerTile);

            if (lid != null) CreateSheetObject("Tampa", Bottom, lid);

            if (smoke != null)
            {
                _layers = new MeshRenderer[Layers];
                _drift = new Vector2[Layers];
                _offset = new Vector2[Layers];
                var rng = new System.Random(4099 + variation * 131);

                for (int i = 0; i < Layers; i++)
                {
                    float t = i / (float)(Layers - 1);
                    float y = Mathf.Lerp(Top, Bottom + 0.3f, t);
                    float brightness = Mathf.Lerp(TopBrightness, BottomBrightness, Mathf.Sqrt(t));

                    _layers[i] = CreateSheetObject("Camada_" + i, y, smoke);

                    // Cada camada desliza para um lado, entre 4 e 10 cm por segundo.
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float speed = Mathf.Lerp(0.04f, 0.10f, (float)rng.NextDouble()) / MetersPerTile;
                    _drift[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                    _offset[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());

                    _block.SetColor(ColorId, new Color(brightness, brightness, brightness * 1.04f, Mathf.Lerp(TopOpacity, BottomOpacity, t)));
                    _block.SetVector(TilingId, new Vector4(_tiling.x, _tiling.y, _offset[i].x, _offset[i].y));
                    _layers[i].SetPropertyBlock(_block);
                }
            }

            AddVolumetricFog(sizeX, sizeZ);
        }

        void Update()
        {
            if (_layers == null) return;

            for (int i = 0; i < _layers.Length; i++)
            {
                _offset[i] += _drift[i] * Time.deltaTime;
                _offset[i].x -= Mathf.Floor(_offset[i].x);
                _offset[i].y -= Mathf.Floor(_offset[i].y);

                _layers[i].GetPropertyBlock(_block);
                _block.SetVector(TilingId, new Vector4(_tiling.x, _tiling.y, _offset[i].x, _offset[i].y));
                _layers[i].SetPropertyBlock(_block);
            }
        }

        void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }

        // Só existe nos perfis gráficos que têm névoa volumétrica (o Performant não tem).
        void AddVolumetricFog(float sizeX, float sizeZ)
        {
            if (!(GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset asset)) return;
            if (!asset.currentPlatformRenderPipelineSettings.supportVolumetrics) return;

            float top = Top + 0.6f;
            var fogObject = new GameObject("Nevoa");
            fogObject.transform.SetParent(transform, false);
            fogObject.transform.localPosition = new Vector3(0f, (top + Bottom) * 0.5f, 0f);

            var fog = fogObject.AddComponent<LocalVolumetricFog>();
            LocalVolumetricFogArtistParameters parameters = fog.parameters;
            parameters.albedo = new Color(0.3f, 0.3f, 0.32f);
            parameters.meanFreePath = 0.6f;
            parameters.size = new Vector3(sizeX, top - Bottom, sizeZ);
            // Some aos poucos para cima; nas laterais e embaixo acaba de uma vez (há parede e tampa).
            parameters.positiveFade = new Vector3(0f, 0.5f, 0f);
            parameters.negativeFade = Vector3.zero;
            parameters.distanceFadeStart = 45f;
            parameters.distanceFadeEnd = 55f;
            fog.parameters = parameters;
        }

        MeshRenderer CreateSheetObject(string objectName, float y, Material material)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;

            var sheet = go.AddComponent<MeshRenderer>();
            sheet.sharedMaterial = material;
            sheet.shadowCastingMode = ShadowCastingMode.Off;
            sheet.receiveShadows = false;
            return sheet;
        }

        // Folha horizontal vista de cima, com a textura cobrindo a folha inteira uma vez.
        static Mesh CreateSheet(float sizeX, float sizeZ)
        {
            float hx = sizeX * 0.5f, hz = sizeZ * 0.5f;
            var mesh = new Mesh { name = "FolhaDeFumaca" };

            mesh.SetVertices(new[] { new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0f, hz), new Vector3(hx, 0f, hz), new Vector3(hx, 0f, -hz) });
            mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
