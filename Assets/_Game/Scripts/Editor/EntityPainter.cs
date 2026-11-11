using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Pinta as texturas da entidade: a cor e o relevo do casco da cabeça e da casca dos
    /// cipós. O relevo sai como mapa de normais, calculado aqui a partir de uma altura.
    /// Usado pelo Level0Setup, que grava o resultado em PNG.
    /// </summary>
    public static class EntityPainter
    {
        public const int ShellSize = 512;
        public const int BarkSize = 256;

        /// <summary>Casco: quase preto, mais claro nas veias, com manchas largas.</summary>
        public static Color32[] ShellColor()
        {
            var pixels = new Color32[ShellSize * ShellSize];

            for (int y = 0; y < ShellSize; y++)
            {
                for (int x = 0; x < ShellSize; x++)
                {
                    float u = x / (float)ShellSize, v = y / (float)ShellSize;
                    float grain = StainPainter.TileableFbm(u, v, 5, 46, 11);
                    float veins = 1f - Mathf.Abs(StainPainter.TileableFbm(u, v, 9, 11, 23) * 2f - 1f);
                    float blotch = StainPainter.TileableFbm(u, v, 3, 3, 53);

                    byte tone = (byte)Mathf.Clamp(120f + grain * 70f + Mathf.Pow(veins, 5f) * 90f - blotch * 70f, 40f, 255f);
                    pixels[y * ShellSize + x] = new Color32(tone, tone, (byte)Mathf.Min(255, tone + 8), 255);
                }
            }

            return pixels;
        }

        /// <summary>Casco: estrias ao comprido da cabeça, veias em relevo e poros.</summary>
        public static Color32[] ShellNormal()
        {
            var height = new float[ShellSize, ShellSize];

            for (int y = 0; y < ShellSize; y++)
            {
                for (int x = 0; x < ShellSize; x++)
                {
                    // u corre ao comprido da cabeça; v dá a volta nela.
                    float u = x / (float)ShellSize, v = y / (float)ShellSize;
                    float grain = StainPainter.TileableFbm(u, v, 5, 46, 11);
                    float veins = 1f - Mathf.Abs(StainPainter.TileableFbm(u, v, 9, 11, 23) * 2f - 1f);
                    float pores = StainPainter.TileableFbm(u, v, 70, 70, 37, 2);

                    height[x, y] = grain * 0.55f + Mathf.Pow(veins, 5f) * 0.6f - Mathf.Max(0f, pores - 0.62f) * 1.6f;
                }
            }

            return Normals(height, ShellSize, 3.2f);
        }

        /// <summary>Casca: marrom quase preta, mais clara nas cristas dos sulcos.</summary>
        public static Color32[] BarkColor()
        {
            var pixels = new Color32[BarkSize * BarkSize];

            for (int y = 0; y < BarkSize; y++)
            {
                for (int x = 0; x < BarkSize; x++)
                {
                    float grooves = StainPainter.TileableFbm(x / (float)BarkSize, y / (float)BarkSize, 14, 2, 71);
                    byte tone = (byte)Mathf.Clamp(90f + grooves * 150f, 40f, 255f);
                    pixels[y * BarkSize + x] = new Color32(tone, (byte)(tone * 0.93f), (byte)(tone * 0.84f), 255);
                }
            }

            return pixels;
        }

        /// <summary>Casca: sulcos fundos ao comprido de cada haste e alguns nós.</summary>
        public static Color32[] BarkNormal()
        {
            var height = new float[BarkSize, BarkSize];

            for (int y = 0; y < BarkSize; y++)
            {
                for (int x = 0; x < BarkSize; x++)
                {
                    // Na cápsula, u dá a volta e v corre ao comprido: sulcos ao longo de v.
                    float u = x / (float)BarkSize, v = y / (float)BarkSize;
                    float grooves = StainPainter.TileableFbm(u, v, 14, 2, 71);
                    float knots = StainPainter.TileableFbm(u, v, 5, 6, 83);

                    height[x, y] = grooves + Mathf.Max(0f, knots - 0.6f) * 1.5f;
                }
            }

            return Normals(height, BarkSize, 4.5f);
        }

        // Mapa de normais no formato comum (X no vermelho, Y no verde, Z no azul), que emenda nas bordas.
        static Color32[] Normals(float[,] height, int size, float strength)
        {
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = height[(x + 1) % size, y] - height[(x + size - 1) % size, y];
                    float dy = height[x, (y + 1) % size] - height[x, (y + size - 1) % size];
                    Vector3 normal = new Vector3(-dx * strength, -dy * strength, 1f).normalized;

                    pixels[y * size + x] = new Color32((byte)(normal.x * 127.5f + 127.5f), (byte)(normal.y * 127.5f + 127.5f),
                                                       (byte)(normal.z * 127.5f + 127.5f), 255);
                }
            }

            return pixels;
        }
    }
}
