using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Manchas orgânicas feitas com ruído: umidade, sujeira e desgaste. Todas saem brancas
    /// com a forma no canal alfa; a cor vem do material do decalque, então a mesma textura
    /// serve para mancha escura (molhado) ou clara (desgaste).
    /// </summary>
    internal static class StainPainter
    {
        public const int Size = 512;

        // ---------------------------------------------------------------- Ruído

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }

        /// <summary>Degrau suave de shader: 0 abaixo de a, 1 acima de b.</summary>
        static float Step(float a, float b, float x)
        {
            return Smooth(Mathf.Clamp01((x - a) / (b - a)));
        }

        static float ValueNoise(float x, float y, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = Smooth(x - x0);
            float fy = Smooth(y - y0);

            float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x0 + 1, y0, seed), fx);
            float b = Mathf.Lerp(Hash(x0, y0 + 1, seed), Hash(x0 + 1, y0 + 1, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }

        /// <summary>Ruído fractal de 0 a 1.</summary>
        static float Fbm(float x, float y, int seed, int octaves = 5)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;

            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise(x, y, seed + i * 101) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                x *= 2f;
                y *= 2f;
            }

            return sum / total;
        }

        /// <summary>
        /// Ruído fractal de 0 a 1 que se repete sem emenda: u e v vão de 0 a 1 e a grade
        /// dá a volta em <paramref name="periodX"/> x <paramref name="periodY"/> células.
        /// Serve para sujar texturas que são repetidas lado a lado.
        /// </summary>
        public static float TileableFbm(float u, float v, int periodX, int periodY, int seed, int octaves = 4)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;

            for (int i = 0; i < octaves; i++)
            {
                int px = periodX << i;
                int py = periodY << i;
                float x = u * px;
                float y = v * py;

                int x0 = Mathf.FloorToInt(x);
                int y0 = Mathf.FloorToInt(y);
                float fx = Smooth(x - x0);
                float fy = Smooth(y - y0);
                int s = seed + i * 101;

                float a = Mathf.Lerp(Hash(Wrap(x0, px), Wrap(y0, py), s), Hash(Wrap(x0 + 1, px), Wrap(y0, py), s), fx);
                float b = Mathf.Lerp(Hash(Wrap(x0, px), Wrap(y0 + 1, py), s), Hash(Wrap(x0 + 1, px), Wrap(y0 + 1, py), s), fx);

                sum += Mathf.Lerp(a, b, fy) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
            }

            return sum / total;
        }

        static int Wrap(int value, int period)
        {
            int m = value % period;
            return m < 0 ? m + period : m;
        }

        delegate float Shape(float u, float v, int seed);

        static Color32[] Render(int seed, Shape shape)
        {
            var pixels = new Color32[Size * Size];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float alpha = Mathf.Clamp01(shape((x + 0.5f) / Size, (y + 0.5f) / Size, seed));
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            return pixels;
        }

        static float EdgeFade(float u, float v, bool left = true, bool right = true, bool bottom = true, bool top = true)
        {
            float fade = 1f;
            if (left) fade *= Step(0f, 0.1f, u);
            if (right) fade *= Step(0f, 0.1f, 1f - u);
            if (bottom) fade *= Step(0f, 0.1f, v);
            if (top) fade *= Step(0f, 0.1f, 1f - v);
            return fade;
        }

        // ---------------------------------------------------------------- Formas

        /// <summary>Mancha irregular com borda mais marcada, como água que secou.</summary>
        public static Color32[] Blotch(System.Random rng, float rim, float breakup, float stretch)
        {
            return Render(rng.Next(), (u, v, seed) =>
            {
                float dx = (u - 0.5f) * 2f;
                float dy = (v - 0.5f) * 2f * stretch;
                float distance = Mathf.Sqrt(dx * dx + dy * dy) + (Fbm(u * 3f, v * 3f, seed) - 0.5f) * 0.75f;

                float body = 1f - Step(0.5f, 0.86f, distance);
                float grain = Mathf.Lerp(1f, Fbm(u * 11f, v * 11f, seed + 7), breakup);
                float tide = Mathf.Exp(-Mathf.Pow((distance - 0.7f) / 0.05f, 2f)) * rim;

                return (body * grain * 0.8f + tide) * EdgeFade(u, v);
            });
        }

        /// <summary>Escorrido que nasce no teto e desce em faixas de comprimentos diferentes.</summary>
        public static Color32[] TopDrip(System.Random rng)
        {
            return Render(rng.Next(), (u, v, seed) =>
            {
                float down = 1f - v;
                float reach = 0.2f + 0.8f * Fbm(u * 9f, 0.37f, seed, 4);
                float streak = 1f - Step(reach * 0.45f, reach, down);
                float fiber = 0.45f + 0.55f * Fbm(u * 26f, v * 2.5f, seed + 3);
                float body = Fbm(u * 2.2f, v * 1.5f, seed + 9) * 0.6f + 0.4f;

                return streak * fiber * body * EdgeFade(u, v, top: false);
            });
        }

        /// <summary>Umidade que sobe do rodapé, com a linha da maré na parte de cima.</summary>
        public static Color32[] RisingDamp(System.Random rng)
        {
            return Render(rng.Next(), (u, v, seed) =>
            {
                float level = 0.3f + 0.65f * Fbm(u * 3.5f, 1.7f, seed, 4);
                float body = 1f - Step(level * 0.6f, level, v);
                float grain = 0.5f + 0.5f * Fbm(u * 10f, v * 10f, seed + 5);
                float tide = Mathf.Exp(-Mathf.Pow((v - level * 0.82f) / 0.035f, 2f)) * 0.35f;

                return (body * grain * 0.85f + tide) * EdgeFade(u, v, bottom: false);
            });
        }

        /// <summary>Riscos horizontais de coisas que roçaram na parede.</summary>
        public static Color32[] Scuff(System.Random rng)
        {
            var canvas = new PaintCanvas(Size, Size);
            int streaks = 9 + rng.Next(7);

            for (int i = 0; i < streaks; i++)
            {
                float y = Size * (0.2f + 0.6f * (float)rng.NextDouble());
                float x0 = Size * (0.06f + 0.5f * (float)rng.NextDouble());
                float x1 = Mathf.Min(Size * 0.94f, x0 + Size * (0.15f + 0.5f * (float)rng.NextDouble()));
                float thickness = 5f + 22f * (float)rng.NextDouble();

                canvas.Stroke(x0, y, x1, y + ((float)rng.NextDouble() - 0.5f) * 34f, thickness * 0.4f, thickness,
                              rng, 9f, 0.2f + 0.45f * (float)rng.NextDouble());
            }

            return canvas.ToAlphaPixels(new Color32(255, 255, 255, 255), rng, 0.45f);
        }
    }
}
