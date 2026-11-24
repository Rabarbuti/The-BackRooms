using System.Collections.Generic;
using UnityEngine;

namespace BackRooms.UI
{
    /// <summary>
    /// Fonte de matriz 5x7 desenhada direto num vetor de pixels, sem depender de arquivo
    /// de fonte. Só maiúsculas, dígitos e alguns sinais. Usada pelo painel da filmadora e
    /// pelo mapa 2D do editor.
    /// As coordenadas têm a origem no canto de cima, esquerda; o vetor segue a ordem das
    /// texturas do Unity (primeira linha = pé da imagem).
    /// </summary>
    public static class PixelFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;

        /// <summary>Passo de um caractere ao seguinte, em pontos (5 de letra + 1 de espaço).</summary>
        public const int Advance = 6;

        /// <summary>A fonte desenha este caractere? (O espaço não tem desenho, só avança.)</summary>
        public static bool CanDraw(char c) => Glyphs.ContainsKey(char.ToUpperInvariant(c));

        public static int Measure(string text, int scale)
        {
            return text.Length == 0 ? 0 : text.Length * Advance * scale - scale;
        }

        public static void Draw(Color32[] pixels, int width, int height, int x, int y, string text, int scale, Color32 color)
        {
            foreach (char raw in text)
            {
                if (Glyphs.TryGetValue(char.ToUpperInvariant(raw), out string rows))
                {
                    for (int row = 0; row < GlyphHeight; row++)
                        for (int col = 0; col < GlyphWidth; col++)
                            if (rows[row * GlyphWidth + col] == '#')
                                Fill(pixels, width, height, x + col * scale, y + row * scale, scale, scale, color);
                }

                x += Advance * scale;
            }
        }

        /// <summary>Retângulo cheio, recortado nas bordas da imagem.</summary>
        public static void Fill(Color32[] pixels, int width, int height, int x, int y, int w, int h, Color32 color)
        {
            int x0 = Mathf.Max(0, x), x1 = Mathf.Min(width, x + w);
            int y0 = Mathf.Max(0, y), y1 = Mathf.Min(height, y + h);

            for (int j = y0; j < y1; j++)
            {
                int rowStart = (height - 1 - j) * width;
                for (int i = x0; i < x1; i++) pixels[rowStart + i] = color;
            }
        }

        /// <summary>
        /// O mesmo que <see cref="Draw(Color32[],int,int,int,int,string,int,Color32)"/>, numa
        /// imagem de um byte por ponto (o painel da filmadora guarda um índice de cor).
        /// </summary>
        public static void Draw(byte[] pixels, int width, int height, int x, int y, string text, int scale, byte value)
        {
            foreach (char raw in text)
            {
                if (Glyphs.TryGetValue(char.ToUpperInvariant(raw), out string rows))
                {
                    for (int row = 0; row < GlyphHeight; row++)
                        for (int col = 0; col < GlyphWidth; col++)
                            if (rows[row * GlyphWidth + col] == '#')
                                Fill(pixels, width, height, x + col * scale, y + row * scale, scale, scale, value);
                }

                x += Advance * scale;
            }
        }

        /// <summary>Retângulo cheio numa imagem de um byte por ponto, recortado nas bordas.</summary>
        public static void Fill(byte[] pixels, int width, int height, int x, int y, int w, int h, byte value)
        {
            int x0 = Mathf.Max(0, x), x1 = Mathf.Min(width, x + w);
            int y0 = Mathf.Max(0, y), y1 = Mathf.Min(height, y + h);

            for (int j = y0; j < y1; j++)
            {
                int rowStart = (height - 1 - j) * width;
                for (int i = x0; i < x1; i++) pixels[rowStart + i] = value;
            }
        }

        // Cada caractere são 7 linhas de 5 pontos, de cima para baixo.
        static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['0'] = ".###." + "#...#" + "#..##" + "#.#.#" + "##..#" + "#...#" + ".###.",
            ['1'] = "..#.." + ".##.." + "..#.." + "..#.." + "..#.." + "..#.." + ".###.",
            ['2'] = ".###." + "#...#" + "....#" + "...#." + "..#.." + ".#..." + "#####",
            ['3'] = "#####" + "...#." + "..#.." + "...#." + "....#" + "#...#" + ".###.",
            ['4'] = "...#." + "..##." + ".#.#." + "#..#." + "#####" + "...#." + "...#.",
            ['5'] = "#####" + "#...." + "####." + "....#" + "....#" + "#...#" + ".###.",
            ['6'] = "..##." + ".#..." + "#...." + "####." + "#...#" + "#...#" + ".###.",
            ['7'] = "#####" + "....#" + "...#." + "..#.." + ".#..." + ".#..." + ".#...",
            ['8'] = ".###." + "#...#" + "#...#" + ".###." + "#...#" + "#...#" + ".###.",
            ['9'] = ".###." + "#...#" + "#...#" + ".####" + "....#" + "...#." + ".##..",
            ['A'] = ".###." + "#...#" + "#...#" + "#####" + "#...#" + "#...#" + "#...#",
            ['B'] = "####." + "#...#" + "#...#" + "####." + "#...#" + "#...#" + "####.",
            ['C'] = ".###." + "#...#" + "#...." + "#...." + "#...." + "#...#" + ".###.",
            ['D'] = "###.." + "#..#." + "#...#" + "#...#" + "#...#" + "#..#." + "###..",
            ['E'] = "#####" + "#...." + "#...." + "####." + "#...." + "#...." + "#####",
            ['F'] = "#####" + "#...." + "#...." + "####." + "#...." + "#...." + "#....",
            ['G'] = ".###." + "#...#" + "#...." + "#.###" + "#...#" + "#...#" + ".####",
            ['H'] = "#...#" + "#...#" + "#...#" + "#####" + "#...#" + "#...#" + "#...#",
            ['I'] = ".###." + "..#.." + "..#.." + "..#.." + "..#.." + "..#.." + ".###.",
            ['J'] = "..###" + "...#." + "...#." + "...#." + "...#." + "#..#." + ".##..",
            ['K'] = "#...#" + "#..#." + "#.#.." + "##..." + "#.#.." + "#..#." + "#...#",
            ['L'] = "#...." + "#...." + "#...." + "#...." + "#...." + "#...." + "#####",
            ['M'] = "#...#" + "##.##" + "#.#.#" + "#.#.#" + "#...#" + "#...#" + "#...#",
            ['N'] = "#...#" + "#...#" + "##..#" + "#.#.#" + "#..##" + "#...#" + "#...#",
            ['O'] = ".###." + "#...#" + "#...#" + "#...#" + "#...#" + "#...#" + ".###.",
            ['P'] = "####." + "#...#" + "#...#" + "####." + "#...." + "#...." + "#....",
            ['Q'] = ".###." + "#...#" + "#...#" + "#...#" + "#.#.#" + "#..#." + ".##.#",
            ['R'] = "####." + "#...#" + "#...#" + "####." + "#.#.." + "#..#." + "#...#",
            ['S'] = ".####" + "#...." + "#...." + ".###." + "....#" + "....#" + "####.",
            ['T'] = "#####" + "..#.." + "..#.." + "..#.." + "..#.." + "..#.." + "..#..",
            ['U'] = "#...#" + "#...#" + "#...#" + "#...#" + "#...#" + "#...#" + ".###.",
            ['V'] = "#...#" + "#...#" + "#...#" + ".#.#." + ".#.#." + "..#.." + "..#..",
            ['W'] = "#...#" + "#...#" + "#...#" + "#.#.#" + "#.#.#" + "#.#.#" + ".#.#.",
            ['X'] = "#...#" + "#...#" + ".#.#." + "..#.." + ".#.#." + "#...#" + "#...#",
            ['Y'] = "#...#" + "#...#" + "#...#" + ".#.#." + "..#.." + "..#.." + "..#..",
            ['Z'] = "#####" + "....#" + "...#." + "..#.." + ".#..." + "#...." + "#####",
            [':'] = "....." + ".##.." + ".##.." + "....." + ".##.." + ".##.." + ".....",
            ['.'] = "....." + "....." + "....." + "....." + "....." + ".##.." + ".##..",
            [','] = "....." + "....." + "....." + "....." + ".##.." + "..#.." + ".#...",
            ['/'] = "....." + "....#" + "...#." + "..#.." + ".#..." + "#...." + ".....",
            ['-'] = "....." + "....." + "....." + "#####" + "....." + "....." + ".....",
            ['='] = "....." + "....." + "#####" + "....." + "#####" + "....." + ".....",
            ['('] = "...#." + "..#.." + ".#..." + ".#..." + ".#..." + "..#.." + "...#.",
            [')'] = ".#..." + "..#.." + "...#." + "...#." + "...#." + "..#.." + ".#...",
            ['%'] = "##..." + "##..#" + "...#." + "..#.." + ".#..." + "#..##" + "...##",
            ['_'] = "....." + "....." + "....." + "....." + "....." + "....." + "#####"
        };
    }
}
