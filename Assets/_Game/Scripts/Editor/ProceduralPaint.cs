using System.Collections.Generic;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>Tela de pintura simples: acumula cobertura (0 a 1) a partir de discos e traços.</summary>
    internal sealed class PaintCanvas
    {
        public readonly int Width;
        public readonly int Height;
        readonly float[] _coverage;

        public PaintCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            _coverage = new float[width * height];
        }

        public void Disc(float cx, float cy, float radius, float strength = 1f)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius - 2f));
            int x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(cx + radius + 2f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius - 2f));
            int y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(cy + radius + 2f));

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float distance = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    // Borda suave de 2,4 px. (Mathf.SmoothStep do Unity interpola entre dois
                    // valores; não é o smoothstep de shader, por isso a conta é feita à mão.)
                    float t = Mathf.Clamp01((distance - (radius - 1.2f)) / 2.4f);
                    float value = (1f - t * t * (3f - 2f * t)) * strength;
                    int i = y * Width + x;
                    if (value > _coverage[i]) _coverage[i] = value;
                }
            }
        }

        /// <summary>Traço de largura variável, com tremido opcional de mão.</summary>
        public void Stroke(float x0, float y0, float x1, float y1, float width0, float width1,
                           System.Random rng = null, float wobble = 0f, float strength = 1f)
        {
            float length = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            int steps = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(0.6f, Mathf.Min(width0, width1) * 0.3f)));
            float offsetX = 0f, offsetY = 0f;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;

                if (rng != null && wobble > 0f)
                {
                    // Passeio aleatório amortecido: desvia aos poucos, sem dentes.
                    offsetX = offsetX * 0.94f + ((float)rng.NextDouble() - 0.5f) * wobble * 0.25f;
                    offsetY = offsetY * 0.94f + ((float)rng.NextDouble() - 0.5f) * wobble * 0.25f;
                }

                // As pontas ficam presas, para as letras fecharem.
                float pin = Mathf.Sin(t * Mathf.PI);
                Disc(Mathf.Lerp(x0, x1, t) + offsetX * pin, Mathf.Lerp(y0, y1, t) + offsetY * pin,
                     Mathf.Lerp(width0, width1, t) * 0.5f, strength);
            }
        }

        /// <summary>Gota escorrendo para baixo, afinando e terminando numa bolinha.</summary>
        public void Drip(float x, float y, float length, float width0, float width1, System.Random rng)
        {
            float endY = Mathf.Max(6f, y - length);
            float endX = x + ((float)rng.NextDouble() - 0.5f) * 6f;
            Stroke(x, y, endX, endY, width0, width1, rng, 3f);
            Disc(endX, endY, width1 * 0.75f);
        }

        /// <summary>Pixels de uma cor só, com a cobertura no canal alfa.</summary>
        public Color32[] ToAlphaPixels(Color32 color, System.Random rng, float grain)
        {
            var pixels = new Color32[_coverage.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                float alpha = _coverage[i] * (1f - grain * (float)rng.NextDouble());
                pixels[i] = new Color32(color.r, color.g, color.b, (byte)(Mathf.Clamp01(alpha) * 255f));
            }
            return pixels;
        }

        /// <summary>Pixels opacos: mistura fundo e tinta conforme a cobertura.</summary>
        public Color32[] ToOpaquePixels(Color32 background, Color32 ink)
        {
            var pixels = new Color32[_coverage.Length];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color32.Lerp(background, ink, _coverage[i]);
            return pixels;
        }
    }

    /// <summary>Alfabeto de traços (maiúsculas) para escrever palavras rabiscadas sem depender de fonte.</summary>
    internal static class StrokeFont
    {
        const float GlyphWidth = 0.6f;
        const float Advance = 0.82f;

        // Cada letra é uma lista de linhas poligonais, em uma caixa de 0,6 x 1.
        static readonly Dictionary<char, float[][]> Glyphs = new Dictionary<char, float[][]>
        {
            ['A'] = new[] { new[] { 0f, 0f, 0.3f, 1f, 0.6f, 0f }, new[] { 0.13f, 0.4f, 0.47f, 0.4f } },
            ['E'] = new[] { new[] { 0.55f, 1f, 0f, 1f, 0f, 0f, 0.55f, 0f }, new[] { 0f, 0.52f, 0.42f, 0.52f } },
            ['H'] = new[] { new[] { 0f, 0f, 0f, 1f }, new[] { 0.6f, 0f, 0.6f, 1f }, new[] { 0f, 0.5f, 0.6f, 0.5f } },
            ['I'] = new[] { new[] { 0.3f, 0f, 0.3f, 1f } },
            ['L'] = new[] { new[] { 0f, 1f, 0f, 0f, 0.55f, 0f } },
            ['N'] = new[] { new[] { 0f, 0f, 0f, 1f, 0.6f, 0f, 0.6f, 1f } },
            ['O'] = new[] { new[] { 0.3f, 0f, 0.05f, 0.22f, 0.05f, 0.78f, 0.3f, 1f, 0.55f, 0.78f, 0.55f, 0.22f, 0.3f, 0f } },
            ['P'] = new[] { new[] { 0f, 0f, 0f, 1f, 0.48f, 1f, 0.6f, 0.8f, 0.48f, 0.55f, 0f, 0.55f } },
            ['R'] = new[] { new[] { 0f, 0f, 0f, 1f, 0.48f, 1f, 0.6f, 0.8f, 0.48f, 0.55f, 0f, 0.55f }, new[] { 0.26f, 0.55f, 0.6f, 0f } },
            ['S'] = new[] { new[] { 0.56f, 0.84f, 0.3f, 1f, 0.05f, 0.8f, 0.3f, 0.5f, 0.56f, 0.24f, 0.3f, 0f, 0.04f, 0.16f } },
            ['T'] = new[] { new[] { 0f, 1f, 0.6f, 1f }, new[] { 0.3f, 1f, 0.3f, 0f } },
            ['U'] = new[] { new[] { 0f, 1f, 0f, 0.2f, 0.3f, 0f, 0.6f, 0.2f, 0.6f, 1f } },
            ['X'] = new[] { new[] { 0f, 0f, 0.6f, 1f }, new[] { 0f, 1f, 0.6f, 0f } },
            ['Y'] = new[] { new[] { 0f, 1f, 0.3f, 0.5f, 0.6f, 1f }, new[] { 0.3f, 0.5f, 0.3f, 0f } },
            ['!'] = new[] { new[] { 0.3f, 1f, 0.3f, 0.3f }, new[] { 0.3f, 0.07f, 0.3f, 0f } }
        };

        public static float MeasureWidth(string text, float height)
        {
            return ((text.Length - 1) * Advance + GlyphWidth) * height;
        }

        /// <summary>
        /// Desenha o texto com a base em (x, y). <paramref name="mess"/> de 0 a 1 controla
        /// o quanto cada letra sai torta, como escrita com a mão ou com o dedo.
        /// </summary>
        public static void Draw(PaintCanvas canvas, string text, float x, float y, float height, float stroke,
                                System.Random rng, float mess)
        {
            float cursor = x;

            foreach (char raw in text)
            {
                char c = char.ToUpperInvariant(raw);

                if (Glyphs.TryGetValue(c, out float[][] lines))
                {
                    float angle = mess * ((float)rng.NextDouble() - 0.5f) * 14f * Mathf.Deg2Rad;
                    float lift = mess * ((float)rng.NextDouble() - 0.5f) * 0.1f * height;
                    float scale = 1f + mess * ((float)rng.NextDouble() - 0.5f) * 0.16f;
                    float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);

                    foreach (float[] line in lines)
                    {
                        for (int i = 0; i + 3 < line.Length; i += 2)
                        {
                            Point(line[i], line[i + 1], cursor, y + lift, height * scale, cos, sin, out float ax, out float ay);
                            Point(line[i + 2], line[i + 3], cursor, y + lift, height * scale, cos, sin, out float bx, out float by);

                            float w0 = stroke * (1f + mess * ((float)rng.NextDouble() - 0.5f) * 0.5f);
                            float w1 = stroke * (1f + mess * ((float)rng.NextDouble() - 0.5f) * 0.5f);
                            canvas.Stroke(ax, ay, bx, by, w0, w1, rng, mess * stroke * 1.2f);
                        }
                    }
                }

                cursor += Advance * height;
            }
        }

        // Converte um ponto da caixa da letra para pixels, girando em torno do centro da letra.
        static void Point(float gx, float gy, float originX, float originY, float height, float cos, float sin,
                          out float px, out float py)
        {
            float lx = (gx - GlyphWidth * 0.5f) * height;
            float ly = (gy - 0.5f) * height;

            px = originX + GlyphWidth * 0.5f * height + lx * cos - ly * sin;
            py = originY + 0.5f * height + lx * sin + ly * cos;
        }
    }
}
