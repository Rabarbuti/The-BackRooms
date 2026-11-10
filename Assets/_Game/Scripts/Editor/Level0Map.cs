using System;
using System.IO;
using BackRooms.UI;
using BackRooms.World;
using UnityEditor;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Desenha a planta baixa do Level 0 de uma seed e salva como PNG em Docs/.
    /// Usa o mesmo LevelGenerator do jogo, então o desenho é exatamente o mapa jogado.
    /// Menu BackRooms > Gerar mapa 2D do Level 0. Norte (eixo Z) para cima.
    /// </summary>
    public static class Level0Map
    {
        const int PixelsPerMeter = 10;
        const int Margin = 44;
        const int TitleHeight = 56;
        const int LegendWidth = 330;

        static readonly Color32 Background = new Color32(24, 22, 18, 255);
        static readonly Color32 CleanFloor = new Color32(222, 206, 128, 255);
        static readonly Color32 WornFloor = new Color32(112, 96, 58, 255);
        static readonly Color32 GridLine = new Color32(0, 0, 0, 255);
        static readonly Color32 Wall = new Color32(34, 28, 16, 255);
        static readonly Color32 ThickWall = new Color32(74, 62, 34, 255);
        static readonly Color32 Hideout = new Color32(168, 96, 200, 255);
        static readonly Color32 LightOn = new Color32(255, 255, 240, 255);
        static readonly Color32 LightFlicker = new Color32(255, 160, 40, 255);
        static readonly Color32 LightDead = new Color32(62, 58, 52, 255);
        static readonly Color32 Spawn = new Color32(60, 200, 90, 255);
        static readonly Color32 Exit = new Color32(230, 40, 30, 255);
        static readonly Color32 PathColor = new Color32(40, 130, 235, 255);
        static readonly Color32 Blood = new Color32(120, 0, 10, 255);
        static readonly Color32 Word = new Color32(20, 60, 170, 255);
        static readonly Color32 Arrow = new Color32(10, 10, 10, 255);
        static readonly Color32 Ink = new Color32(236, 232, 214, 255);

        static Color32[] _pixels;
        static int _width;
        static int _height;
        static int _originX;
        static int _originY;
        static float _levelDepth;

        [MenuItem("BackRooms/Gerar mapa 2D do Level 0")]
        public static void ExportFromMenu()
        {
            var builder = UnityEngine.Object.FindFirstObjectByType<LevelBuilder>();
            int seed = builder != null ? builder.seed : 1998;
            int width = builder != null ? builder.width : 16;
            int height = builder != null ? builder.height : 16;

            string path = Export(seed, width, height);
            EditorUtility.RevealInFinder(path);
        }

        /// <summary>Uso em linha de comando. Aceita -mapSeed &lt;número&gt;; sem isso usa 1998.</summary>
        public static void BatchExport()
        {
            try
            {
                int seed = 1998;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-mapSeed") int.TryParse(args[i + 1], out seed);

                Export(seed, 16, 16);
                Level0AutoUpdate.OpenGameScene();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static string Export(int seed, int cellsX, int cellsZ)
        {
            LevelData data = LevelGenerator.Generate(seed, cellsX, cellsZ);

            int levelWidth = Mathf.RoundToInt(data.Width * LevelBuilder.Cell * PixelsPerMeter);
            int levelHeight = Mathf.RoundToInt(data.Height * LevelBuilder.Cell * PixelsPerMeter);

            _width = Margin + levelWidth + Margin + LegendWidth;
            _height = TitleHeight + levelHeight + Margin;
            _originX = Margin;
            _originY = TitleHeight;
            _levelDepth = data.Height * LevelBuilder.Cell;
            _pixels = new Color32[_width * _height];
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = Background;

            string title = $"LEVEL 0   SEED {seed}   {data.Width * (int)LevelBuilder.Cell} X {data.Height * (int)LevelBuilder.Cell} M   PE-DIREITO {LevelBuilder.WallHeight:0.00} M";
            PixelFont.Draw(_pixels, _width, _height, Margin, 18, title.Replace(',', '.'), 2, Ink);

            DrawFloor(data);
            DrawPath(data);
            DrawLights(data);
            DrawWalls(data);
            DrawMarks(data);
            DrawSpawnAndExit(data);
            DrawLegend(data, Margin + levelWidth + Margin, TitleHeight);

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"level0-mapa-seed{seed}.png");

            var texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
            texture.SetPixels32(_pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            _pixels = null;

            Debug.Log("[BackRooms] Mapa 2D salvo em " + path);
            return path;
        }

        // ---------------------------------------------------------------- Camadas

        // Cada célula pintada conforme o desgaste: clara perto do início, escura longe.
        static void DrawFloor(LevelData d)
        {
            for (int z = 0; z < d.Height; z++)
            {
                for (int x = 0; x < d.Width; x++)
                {
                    Color32 color = Color32.Lerp(CleanFloor, WornFloor, LevelGenerator.WearAt(d, x, z));
                    Rect(x * LevelBuilder.Cell, z * LevelBuilder.Cell, LevelBuilder.Cell, LevelBuilder.Cell, color);
                }
            }

            // Grade das células (4 m), bem fraca.
            for (int x = 0; x <= d.Width; x++)
                Line(x * LevelBuilder.Cell, 0f, x * LevelBuilder.Cell, d.Height * LevelBuilder.Cell, 1, GridLine, 0.12f);
            for (int z = 0; z <= d.Height; z++)
                Line(0f, z * LevelBuilder.Cell, d.Width * LevelBuilder.Cell, z * LevelBuilder.Cell, 1, GridLine, 0.12f);
        }

        static void DrawPath(LevelData d)
        {
            for (int i = 0; i + 1 < d.PathCells.Count; i++)
            {
                Vector2 a = CellCenter(d, d.PathCells[i]);
                Vector2 b = CellCenter(d, d.PathCells[i + 1]);

                // Pontilhado.
                for (float t = 0f; t < 1f; t += 0.2f)
                {
                    Vector2 p = Vector2.Lerp(a, b, t);
                    Dot(p.x, p.y, 2, PathColor);
                }
            }
        }

        static void DrawLights(LevelData d)
        {
            for (int z = 0; z < d.Height; z++)
            {
                for (int x = 0; x < d.Width; x++)
                {
                    LightState state = d.Lights[x, z];
                    if (state == LightState.None) continue;

                    Color32 color = state == LightState.On ? LightOn : state == LightState.Flicker ? LightFlicker : LightDead;
                    float cx = (x + 0.5f) * LevelBuilder.Cell;
                    float cz = (z + 0.5f) * LevelBuilder.Cell;
                    Rect(cx - 0.35f, cz - 0.65f, 0.7f, 1.3f, Wall);
                    Rect(cx - 0.25f, cz - 0.55f, 0.5f, 1.1f, color);
                }
            }
        }

        static void DrawWalls(LevelData d)
        {
            float cell = LevelBuilder.Cell;

            for (int z = 0; z <= d.Height; z++)
            {
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.DoorSouth[x, z])
                    {
                        // Vão de porta: dois trechos de parede com a passagem no meio.
                        Rect(x * cell - 0.15f, z * cell - 0.15f, cell * 0.5f - 0.7f + 0.15f, 0.3f, Wall);
                        Rect((x + 0.5f) * cell + 0.7f, z * cell - 0.15f, cell * 0.5f - 0.7f + 0.15f, 0.3f, Wall);
                        continue;
                    }

                    if (!d.WallSouth[x, z]) continue;
                    if (d.AlcoveSouth[x, z] != 0) DrawAlcove((x + 0.5f) * cell, z * cell, true, d.AlcoveSouth[x, z] == 1 ? -1f : 1f);
                    else Rect(x * cell - 0.15f, z * cell - 0.15f, cell + 0.3f, 0.3f, Wall);
                }
            }

            for (int x = 0; x <= d.Width; x++)
            {
                for (int z = 0; z < d.Height; z++)
                {
                    if (d.DoorWest[x, z])
                    {
                        Rect(x * cell - 0.15f, z * cell - 0.15f, 0.3f, cell * 0.5f - 0.7f + 0.15f, Wall);
                        Rect(x * cell - 0.15f, (z + 0.5f) * cell + 0.7f, 0.3f, cell * 0.5f - 0.7f + 0.15f, Wall);
                        continue;
                    }

                    if (!d.WallWest[x, z]) continue;
                    if (d.AlcoveWest[x, z] != 0) DrawAlcove(x * cell, (z + 0.5f) * cell, false, d.AlcoveWest[x, z] == 1 ? -1f : 1f);
                    else Rect(x * cell - 0.15f, z * cell - 0.15f, 0.3f, cell + 0.3f, Wall);
                }
            }

            for (int z = 0; z <= d.Height; z++)
                for (int x = 0; x <= d.Width; x++)
                    if (d.Pillar[x, z]) Rect(x * cell - 0.4f, z * cell - 0.4f, 0.8f, 0.8f, Wall);
        }

        // Parede grossa: bloco de 1,4 m com o nicho por dentro e a abertura de um lado.
        static void DrawAlcove(float cx, float cz, bool alongX, float opening)
        {
            const float halfLength = 2.1f, halfThickness = 0.7f, cavityHalf = 1.2f, cavityDepth = 0.5f, doorHalf = 0.45f;

            if (alongX)
            {
                Rect(cx - halfLength, cz - halfThickness, halfLength * 2f, halfThickness * 2f, ThickWall);
                Rect(cx - cavityHalf, cz - cavityDepth, cavityHalf * 2f, cavityDepth * 2f, Hideout);
                Rect(cx - doorHalf, opening > 0f ? cz + cavityDepth : cz - halfThickness, doorHalf * 2f, halfThickness - cavityDepth, Hideout);
            }
            else
            {
                Rect(cx - halfThickness, cz - halfLength, halfThickness * 2f, halfLength * 2f, ThickWall);
                Rect(cx - cavityDepth, cz - cavityHalf, cavityDepth * 2f, cavityHalf * 2f, Hideout);
                Rect(opening > 0f ? cx + cavityDepth : cx - halfThickness, cz - doorHalf, halfThickness - cavityDepth, doorHalf * 2f, Hideout);
            }
        }

        // Sangue, palavras e setas, junto da parede em que estão. Sujeira comum não entra:
        // é muita coisa e já está representada pelo tom do piso.
        static void DrawMarks(LevelData d)
        {
            foreach (Decoration mark in d.Decorations)
            {
                if (mark.Kind == DecorationKind.FloorPool)
                {
                    Dot((mark.X + 0.5f) * LevelBuilder.Cell + mark.Along * 1.2f, (mark.Z + 0.5f) * LevelBuilder.Cell + mark.Height * 1.2f, 4, Blood);
                    continue;
                }

                if (mark.Kind != DecorationKind.Blood && mark.Kind != DecorationKind.Text && mark.Kind != DecorationKind.Arrow) continue;

                LevelBuilder.WallFace(mark.X, mark.Z, mark.Side, out Vector3 center, out Vector3 normal);
                Vector3 tangent = Mathf.Abs(normal.z) > 0.5f ? Vector3.right : Vector3.forward;
                Vector3 p = center + tangent * mark.Along + normal * 0.45f;

                if (mark.Kind == DecorationKind.Arrow)
                {
                    var direction = new Vector2(mark.DirX, mark.DirZ);
                    Vector2 from = new Vector2(p.x, p.z) - direction * 0.7f;
                    Vector2 to = new Vector2(p.x, p.z) + direction * 0.7f;
                    Vector2 side = new Vector2(-direction.y, direction.x);

                    Line(from.x, from.y, to.x, to.y, 2, Arrow, 1f);
                    Line(to.x, to.y, to.x - direction.x * 0.5f + side.x * 0.4f, to.y - direction.y * 0.5f + side.y * 0.4f, 2, Arrow, 1f);
                    Line(to.x, to.y, to.x - direction.x * 0.5f - side.x * 0.4f, to.y - direction.y * 0.5f - side.y * 0.4f, 2, Arrow, 1f);
                }
                else
                {
                    Dot(p.x, p.z, 3, mark.Kind == DecorationKind.Blood ? Blood : Word);
                }
            }
        }

        static void DrawSpawnAndExit(LevelData d)
        {
            float sx = d.SpawnCenterX * LevelBuilder.Cell;
            float sz = d.SpawnCenterZ * LevelBuilder.Cell;
            Dot(sx, sz, 9, Wall);
            Dot(sx, sz, 7, Spawn);
            Label(sx, sz - 1.7f, "INICIO", Spawn);

            LevelBuilder.WallFace(d.ExitX, d.ExitZ, d.ExitSide, out Vector3 center, out Vector3 normal);
            Vector3 tangent = Mathf.Abs(normal.z) > 0.5f ? Vector3.right : Vector3.forward;
            Vector3 a = center - tangent * 0.6f;
            Vector3 b = center + tangent * 0.6f + normal * 0.5f;
            Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(0.5f, Mathf.Abs(b.x - a.x)), Mathf.Max(0.5f, Mathf.Abs(b.z - a.z)), Exit);

            Vector3 labelAt = center + normal * 1.6f;
            Label(labelAt.x, labelAt.z, "SAIDA", Exit);
        }

        static void DrawLegend(LevelData d, int x, int y)
        {
            int row = y;

            void Item(Action<int, int> icon, string text)
            {
                icon(x + 12, row + 8);
                PixelFont.Draw(_pixels, _width, _height, x + 36, row + 1, text, 2, Ink);
                row += 28;
            }

            PixelFont.Draw(_pixels, _width, _height, x, row, "LEGENDA", 2, Ink);
            row += 30;

            Item((cx, cy) => Disc(cx, cy, 7, Spawn), "INICIO (SALA FECHADA)");
            Item((cx, cy) => PixelFont.Fill(_pixels, _width, _height, cx - 8, cy - 5, 16, 10, Exit), "SAIDA (PORTA EXIT)");
            Item((cx, cy) => { for (int i = -8; i <= 8; i += 5) Disc(cx + i, cy, 2, PathColor); }, "CAMINHO MAIS CURTO");
            Item((cx, cy) => PixelFont.Fill(_pixels, _width, _height, cx - 10, cy - 2, 20, 4, Ink), "PAREDE (0.2 M)");
            Item((cx, cy) => PixelFont.Fill(_pixels, _width, _height, cx - 5, cy - 5, 10, 10, Ink), "PILAR (0.8 M)");
            Item((cx, cy) =>
            {
                PixelFont.Fill(_pixels, _width, _height, cx - 10, cy - 7, 20, 14, ThickWall);
                PixelFont.Fill(_pixels, _width, _height, cx - 6, cy - 4, 12, 8, Hideout);
            }, "PAREDE GROSSA");
            PixelFont.Draw(_pixels, _width, _height, x + 36, row - 8, "COM ESCONDERIJO", 2, Ink);
            row += 16;

            Item((cx, cy) => PixelFont.Fill(_pixels, _width, _height, cx - 3, cy - 6, 6, 12, LightOn), "LUZ ACESA");
            Item((cx, cy) => PixelFont.Fill(_pixels, _width, _height, cx - 3, cy - 6, 6, 12, LightFlicker), "LUZ PISCANDO");
            Item((cx, cy) =>
            {
                PixelFont.Fill(_pixels, _width, _height, cx - 4, cy - 7, 8, 14, Ink);
                PixelFont.Fill(_pixels, _width, _height, cx - 3, cy - 6, 6, 12, LightDead);
            }, "LUZ QUEIMADA");
            Item((cx, cy) =>
            {
                PixelFont.Fill(_pixels, _width, _height, cx - 9, cy - 1, 16, 2, Ink);
                PixelFont.Fill(_pixels, _width, _height, cx + 4, cy - 4, 2, 8, Ink);
                PixelFont.Fill(_pixels, _width, _height, cx + 6, cy - 2, 2, 4, Ink);
            }, "SETA NA PAREDE");
            Item((cx, cy) => Disc(cx, cy, 4, new Color32(200, 30, 40, 255)), "SANGUE / POCA");
            Item((cx, cy) => Disc(cx, cy, 4, new Color32(70, 120, 240, 255)), "PALAVRA ESCRITA");

            row += 14;
            PixelFont.Draw(_pixels, _width, _height, x, row, "DESGASTE DO PISO", 2, Ink);
            row += 24;
            for (int i = 0; i < 200; i++)
                PixelFont.Fill(_pixels, _width, _height, x + i, row, 1, 16, Color32.Lerp(CleanFloor, WornFloor, i / 199f));
            row += 22;
            PixelFont.Draw(_pixels, _width, _height, x, row, "NOVO", 2, Ink);
            PixelFont.Draw(_pixels, _width, _height, x + 200 - PixelFont.Measure("GASTO", 2), row, "GASTO", 2, Ink);

            row += 40;
            int cellPixels = Mathf.RoundToInt(LevelBuilder.Cell * PixelsPerMeter);
            PixelFont.Fill(_pixels, _width, _height, x, row, cellPixels, 3, Ink);
            PixelFont.Fill(_pixels, _width, _height, x, row - 4, 2, 11, Ink);
            PixelFont.Fill(_pixels, _width, _height, x + cellPixels - 2, row - 4, 2, 11, Ink);
            PixelFont.Draw(_pixels, _width, _height, x + cellPixels + 10, row - 6, "4 M (1 CELULA)", 2, Ink);

            int lights = 0, hideouts = 0;
            foreach (LightState state in d.Lights) if (state == LightState.On || state == LightState.Flicker) lights++;
            foreach (byte alcove in d.AlcoveSouth) if (alcove != 0) hideouts++;
            foreach (byte alcove in d.AlcoveWest) if (alcove != 0) hideouts++;

            row += 34;
            PixelFont.Draw(_pixels, _width, _height, x, row, $"LUZES ACESAS: {lights}", 2, Ink);
            row += 22;
            PixelFont.Draw(_pixels, _width, _height, x, row, $"ESCONDERIJOS: {hideouts}", 2, Ink);
            row += 22;
            PixelFont.Draw(_pixels, _width, _height, x, row, $"CAMINHO: {(d.PathCells.Count - 1) * (int)LevelBuilder.Cell} M", 2, Ink);
            row += 34;
            PixelFont.Draw(_pixels, _width, _height, x, row, "NORTE (Z) PARA CIMA", 1, Ink);
            row += 12;
            PixelFont.Draw(_pixels, _width, _height, x, row, "CADA SEED GERA OUTRO MAPA", 1, Ink);
        }

        // ---------------------------------------------------------------- Desenho

        static Vector2 CellCenter(LevelData d, int index)
        {
            return new Vector2((index % d.Width + 0.5f) * LevelBuilder.Cell, (index / d.Width + 0.5f) * LevelBuilder.Cell);
        }

        // Converte metros do mundo (x para a direita, z para cima) em pixels da imagem.
        static int Px(float worldX) { return _originX + Mathf.RoundToInt(worldX * PixelsPerMeter); }
        static int Py(float worldZ) { return _originY + Mathf.RoundToInt((_levelDepth - worldZ) * PixelsPerMeter); }

        static void Rect(float worldX, float worldZ, float sizeX, float sizeZ, Color32 color)
        {
            int x0 = Px(worldX), x1 = Px(worldX + sizeX);
            int y0 = Py(worldZ + sizeZ), y1 = Py(worldZ);
            PixelFont.Fill(_pixels, _width, _height, x0, y0, Mathf.Max(1, x1 - x0), Mathf.Max(1, y1 - y0), color);
        }

        static void Dot(float worldX, float worldZ, int radius, Color32 color)
        {
            Disc(Px(worldX), Py(worldZ), radius, color);
        }

        static void Disc(int cx, int cy, int radius, Color32 color)
        {
            for (int j = -radius; j <= radius; j++)
                for (int i = -radius; i <= radius; i++)
                    if (i * i + j * j <= radius * radius) PixelFont.Fill(_pixels, _width, _height, cx + i, cy + j, 1, 1, color);
        }

        static void Line(float x0, float z0, float x1, float z1, int thickness, Color32 color, float opacity)
        {
            int ax = Px(x0), ay = Py(z0), bx = Px(x1), by = Py(z1);
            int steps = Mathf.Max(Mathf.Abs(bx - ax), Mathf.Abs(by - ay), 1);

            for (int i = 0; i <= steps; i++)
            {
                int x = Mathf.RoundToInt(Mathf.Lerp(ax, bx, i / (float)steps));
                int y = Mathf.RoundToInt(Mathf.Lerp(ay, by, i / (float)steps));

                for (int j = 0; j < thickness; j++)
                    for (int k = 0; k < thickness; k++)
                        Blend(x + k, y + j, color, opacity);
            }
        }

        static void Blend(int x, int y, Color32 color, float opacity)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height) return;
            int index = (_height - 1 - y) * _width + x;
            _pixels[index] = Color32.Lerp(_pixels[index], color, opacity);
        }

        // Texto com fundo escuro, centralizado no ponto dado.
        static void Label(float worldX, float worldZ, string text, Color32 color)
        {
            int w = PixelFont.Measure(text, 2);
            int x = Px(worldX) - w / 2;
            int y = Py(worldZ) - 7;
            PixelFont.Fill(_pixels, _width, _height, x - 3, y - 3, w + 6, 20, Wall);
            PixelFont.Draw(_pixels, _width, _height, x, y, text, 2, color);
        }
    }
}
