using System;
using System.Collections.Generic;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// Malhas esculpidas em código para o Capitão, o inimigo do Level 1 (ver
    /// Docs/level1-mostruario.md): a cabeça — um rosto humano comprido e magro, de olhos
    /// arregalados e boca aberta —, a barba, o cabelo, o chapéu de três bicos e o casaco.
    ///
    /// A cabeça é uma esfera deformada: para cada direção a partir do centro calcula-se a
    /// distância até a casca. O rosto é desenhado em "coordenadas de rosto" (u, v) = (x/z, y/z)
    /// da direção, como se projetado num plano à frente: olhos, nariz, boca e queixo são
    /// calombos e covas nesse plano. Tudo o que vai colado na cabeça — barba, cabelo,
    /// sobrancelhas, vincos, lábios — é uma película que segue a mesma casca um pouco afastada,
    /// então encaixa sem fresta e sem esfera esticada.
    ///
    /// No espaço da cabeça, +Z é para onde o rosto aponta, +Y é para cima e +X é a direita de
    /// quem a veste (como no traje dos jogadores). Medidas antes da escala do corpo.
    /// </summary>
    public static class CaptainHeadMesh
    {
        // Meias medidas da cabeça: 0,16 m de largura, 0,24 m do queixo ao alto, 0,20 m de fundo.
        const float HalfWidth = 0.078f;
        const float HalfHeight = 0.118f;
        const float HalfDepth = 0.100f;

        // Resolução alta: o rosto é pequeno e a boca precisa de várias fileiras de vértices.
        const int Latitudes = 140;
        const int Longitudes = 136;

        /// <summary>Centro da boca em coordenadas de rosto (u, v).</summary>
        public static readonly Vector2 Mouth = new Vector2(0f, -0.70f);
        /// <summary>Meia largura e meia altura da boca aberta, em coordenadas de rosto.</summary>
        public static readonly Vector2 MouthSize = new Vector2(0.27f, 0.12f);

        /// <summary>
        /// Onde fica cada olho, em coordenadas de rosto. O rosto não é simétrico: o esquerdo
        /// (em -u) é mais baixo e mais afastado, caído, dentro de uma órbita inchada.
        /// </summary>
        public static readonly Vector2 EyeRight = new Vector2(0.42f, 0.07f);
        public static readonly Vector2 EyeLeft = new Vector2(-0.45f, 0.0f);

        /// <summary>A direção da cabeça que passa pelo ponto (u, v) do rosto.</summary>
        public static Vector3 FaceDirection(float u, float v)
        {
            return new Vector3(u, v, 1f).normalized;
        }

        // ---------------------------------------------------------------- Forma da cabeça

        /// <summary>Distância do centro da cabeça até a casca na direção dada (normalizada).</summary>
        public static float Radius(Vector3 d)
        {
            // Oval base, com o queixo mais estreito que o crânio.
            float halfWidth = HalfWidth * (1f - 0.24f * Ease(0.05f, -0.8f, d.y));
            float r = 1f / Mathf.Sqrt(Sq(d.x / halfWidth) + Sq(d.y / HalfHeight) + Sq(d.z / HalfDepth));

            // Crânio maior atrás e em cima; nuca descendo para o pescoço.
            r *= 1f + 0.09f * Ease(-0.1f, 0.6f, d.y) * Ease(0.3f, -0.6f, d.z);

            // Orelhas, para fora, um pouco atrás do meio.
            r *= 1f + 0.24f * Blob(d, new Vector3(1f, 0.02f, -0.12f), 0.17f);
            r *= 1f + 0.24f * Blob(d, new Vector3(-1f, 0.02f, -0.12f), 0.17f);

            // Queixo comprido, que a barba cobre.
            r *= 1f + 0.08f * Blob(d, new Vector3(0f, -0.8f, 0.6f), 0.26f);

            if (d.z > 0.12f)
            {
                float front = Ease(0.12f, 0.5f, d.z);
                float u = d.x / d.z, v = d.y / d.z;
                float au = Mathf.Abs(u);

                // Testa e arco das sobrancelhas.
                r *= 1f + 0.05f * front * Mathf.Exp(-Sq((v - 0.30f) / 0.10f)) * Ease(0.9f, 0.45f, au);

                // Órbitas fundas: os olhos arregalados ficam dentro delas.
                r *= 1f - 0.10f * front * Gauss(u - EyeRight.x, v - EyeRight.y, 0.19f, 0.15f);
                r *= 1f - 0.08f * front * Gauss(u - EyeLeft.x, v - EyeLeft.y, 0.20f, 0.15f);

                // Inchaço embaixo do olho caído e na maçã do rosto desse lado, de pancada.
                r *= 1f + 0.07f * front * Gauss(u + 0.50f, v + 0.13f, 0.20f, 0.12f);

                // Nariz comprido, quebrado: a ponta torta para a direita. Asas largas.
                float bend = 0.035f * Ease(0.15f, -0.32f, v);
                float ridge = Mathf.Exp(-Sq((u - bend) / 0.10f)) * Ease(0.24f, -0.26f, v) * Ease(-0.44f, -0.30f, v);
                r *= 1f + 0.30f * front * ridge;
                r *= 1f + 0.03f * front * Gauss(u - 0.01f, v - 0.08f, 0.05f, 0.05f);
                r *= 1f + 0.11f * front * Gauss(au - 0.16f, v + 0.33f, 0.08f, 0.07f);

                // Maçãs do rosto altas e bochechas chupadas embaixo delas.
                r *= 1f + 0.07f * front * Gauss(au - 0.64f, v + 0.02f, 0.2f, 0.14f);
                r *= 1f - 0.07f * front * Gauss(au - 0.56f, v + 0.50f, 0.2f, 0.2f);

                // Boca projetada para a frente e a cova da boca aberta, no meio dela.
                r *= 1f + 0.07f * front * Gauss(u, v - Mouth.y, 0.42f, 0.30f);
                float inside = Sq(u / MouthSize.x) + Sq((v - Mouth.y) / MouthSize.y);
                r *= 1f - 0.13f * front * Mathf.Exp(-inside * 1.4f);
            }

            // Pele enrugada: ondulação fina e irregular.
            r *= 1f + 0.006f * Mathf.Sin(d.x * 31f + d.y * 13f) * Mathf.Sin(d.z * 23f - d.y * 29f + 1.2f);

            return r;
        }

        /// <summary>Ponto da casca na direção dada, afastado dela <paramref name="lift"/> metros.</summary>
        public static Vector3 Surface(Vector3 direction, float lift = 0f)
        {
            Vector3 d = direction.normalized;
            return d * (Radius(d) + lift);
        }

        /// <summary>Ponto da casca no ponto (u, v) do rosto.</summary>
        public static Vector3 FacePoint(float u, float v, float lift = 0f)
        {
            return Surface(FaceDirection(u, v), lift);
        }

        public static Mesh BuildHead()
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int lat = 0; lat <= Latitudes; lat++)
            {
                // Do polo de baixo (pescoço) ao polo de cima.
                float phi = Mathf.Lerp(-90f, 90f, lat / (float)Latitudes) * Mathf.Deg2Rad;

                for (int lon = 0; lon <= Longitudes; lon++)
                {
                    float theta = lon / (float)Longitudes * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(phi) * Mathf.Sin(theta), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Cos(theta));
                    vertices.Add(Surface(d));
                    uvs.Add(new Vector2(lon / (float)Longitudes * 3f, lat / (float)Latitudes * 2f));
                }
            }

            Grid(triangles, 0, Latitudes, Longitudes, true);
            return Finish("Cabeca_Do_Capitao", vertices, uvs, triangles);
        }

        // ---------------------------------------------------------------- Barba e cabelo

        /// <summary>
        /// Uma película sobre a casca numa faixa de latitudes e longitudes, com espessura dada
        /// por direção. Onde a espessura é praticamente zero o triângulo não é desenhado: é
        /// assim que a barba tem a borda recortada e o buraco da boca.
        /// </summary>
        public static Mesh BuildPatch(string name, float latFrom, float latTo, float lonFrom, float lonTo,
                                      int rows, int columns, Func<Vector3, float> thickness)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var depth = new List<float>();
            var triangles = new List<int>();

            for (int r = 0; r <= rows; r++)
            {
                float phi = Mathf.Lerp(latFrom, latTo, r / (float)rows) * Mathf.Deg2Rad;
                for (int c = 0; c <= columns; c++)
                {
                    float theta = Mathf.Lerp(lonFrom, lonTo, c / (float)columns) * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Cos(phi) * Mathf.Sin(theta), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Cos(theta));
                    float h = Mathf.Max(0f, thickness(d));
                    depth.Add(h);
                    vertices.Add(Surface(d, 0.0005f + h));
                    uvs.Add(new Vector2(c / (float)columns * 3f, r / (float)rows * 3f));
                }
            }

            // Mesma ordem da cabeça (linhas para cima, colunas para +X na frente): face para fora.
            int row = columns + 1;
            const float empty = 0.0006f;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    int a = r * row + c, b = a + 1, d = a + row, e = d + 1;
                    if (depth[a] > empty || depth[b] > empty || depth[d] > empty)
                    {
                        triangles.Add(a); triangles.Add(b); triangles.Add(d);
                    }
                    if (depth[b] > empty || depth[e] > empty || depth[d] > empty)
                    {
                        triangles.Add(b); triangles.Add(e); triangles.Add(d);
                    }
                }
            }

            return Finish(name, vertices, uvs, triangles);
        }

        /// <summary>
        /// Barba preta curta, cheia: desce das costeletas pelo maxilar, cobre o queixo e a parte
        /// de baixo das bochechas e sobe num bigode, com o buraco da boca aberto no meio.
        /// </summary>
        public static Mesh BuildBeard()
        {
            return BuildPatch("Barba_Do_Capitao", -88f, 20f, -118f, 118f, 110, 120, d =>
            {
                // Linha de cima: baixa na frente, sobe até as costeletas, na frente da orelha.
                float side = Ease(0.3f, 0.95f, Mathf.Abs(d.x));
                float top = Mathf.Lerp(-0.40f, 0.03f, side);
                float mask = Ease(top, top - 0.07f, d.y);

                // Não passa da orelha para trás nem desce pela nuca.
                mask *= Ease(-0.32f, -0.1f, d.z);

                if (d.z > 0.12f)
                {
                    float u = d.x / d.z, v = d.y / d.z;

                    // Bigode: faixa logo acima da boca, ligada à barba nos cantos.
                    float mustache = Mathf.Exp(-Sq((v - (Mouth.y + MouthSize.y + 0.07f)) / 0.06f)) * Ease(0.42f, 0.3f, Mathf.Abs(u));
                    mask = Mathf.Max(mask, mustache);

                    // Buraco da boca, um pouco maior que ela.
                    float inside = Sq(u / (MouthSize.x + 0.03f)) + Sq((v - Mouth.y) / (MouthSize.y + 0.03f));
                    mask *= Ease(1f, 1.25f, inside);
                }

                float tufts = 1f + 0.18f * Mathf.Sin(d.x * 90f + d.y * 40f) * Mathf.Sin(d.z * 70f - d.x * 30f);
                return 0.0085f * mask * tufts;
            });
        }

        /// <summary>Cabelo curto e preto dos lados e atrás, aparecendo por baixo do chapéu.</summary>
        public static Mesh BuildHair()
        {
            return BuildPatch("Cabelo_Do_Capitao", -20f, 89f, -180f, 180f, 70, 140, d =>
            {
                float back = Ease(0.25f, -0.05f, d.z) * Ease(-0.3f, -0.08f, d.y);
                float top = Ease(0.38f, 0.55f, d.y);
                // Costeletas: descem na frente da orelha até encontrar a barba.
                float burns = Ease(0.85f, 0.95f, Mathf.Abs(d.x)) * Ease(-0.1f, 0.05f, d.y) * Ease(-0.2f, 0.05f, d.z);
                return 0.005f * Mathf.Max(back, Mathf.Max(top, burns));
            });
        }

        // ---------------------------------------------------------------- Películas coladas na cabeça

        /// <summary>
        /// Um disco que segue a casca: centro na direção dada, raio em "direção normalizada"
        /// (0,1 ≈ 1 cm na cabeça). A borda encosta na casca; o miolo sobe até <paramref name="lift"/>.
        /// </summary>
        public static Mesh BuildDisc(string name, Vector3 center, float radius, float lift, float squashY = 1f)
        {
            Frame(center, out Vector3 u, out Vector3 v);

            const int rings = 8, sides = 40;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int ring = 0; ring <= rings; ring++)
            {
                float t = ring / (float)rings;
                // A borda fica 1 mm acima da casca, para não piscar contra ela.
                float h = 0.001f + (lift - 0.001f) * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t * t * t));

                for (int side = 0; side <= sides; side++)
                {
                    float a = side / (float)sides * Mathf.PI * 2f;
                    Vector3 d = center + (u * Mathf.Cos(a) + v * Mathf.Sin(a) * squashY) * radius * Mathf.Max(t, 0.0001f);
                    vertices.Add(Surface(d, h));
                    uvs.Add(new Vector2(0.5f + 0.5f * t * Mathf.Cos(a), 0.5f + 0.5f * t * Mathf.Sin(a)));
                }
            }

            Grid(triangles, 0, rings, sides, false);
            return Finish(name, vertices, uvs, triangles);
        }

        /// <summary>
        /// Uma faixa que segue a casca ao longo de uma curva. <paramref name="curve"/> dá, para t de
        /// 0 a 1, a direção do meio da faixa e a direção "para o lado"; <paramref name="width"/> e
        /// <paramref name="lift"/> dão a meia largura e a espessura em cada (t, s), s de -1 a 1.
        /// Fechada nas pontas pela própria espessura, que vai a zero.
        /// </summary>
        public static Mesh BuildBand(string name, int steps, int across, Func<float, (Vector3 middle, Vector3 side)> curve,
                                     Func<float, float, float> width, Func<float, float, float> lift, bool loop = false, bool doubleSided = false)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                (Vector3 middle, Vector3 side) = curve(loop && i == steps ? 0f : t);

                for (int j = 0; j <= across; j++)
                {
                    float s = Mathf.Lerp(-1f, 1f, j / (float)across);
                    Vector3 d = middle.normalized + side * (s * width(t, s));
                    vertices.Add(Surface(d, 0.001f + Mathf.Max(0f, lift(t, s))));
                    uvs.Add(new Vector2(t * 4f, j / (float)across));
                }
            }

            Grid(triangles, 0, steps, across, false);
            if (doubleSided)
            {
                // O avesso precisa de vértices próprios, senão as normais dos dois lados se anulam.
                int count = vertices.Count;
                for (int k = 0; k < count; k++)
                {
                    vertices.Add(vertices[k] * 0.995f);
                    uvs.Add(uvs[k]);
                }
                Grid(triangles, count, steps, across, true);
            }
            return Finish(name, vertices, uvs, triangles);
        }

        // ---------------------------------------------------------------- Chapéu de três bicos

        /// <summary>
        /// Chapéu de três bicos: copa baixa e arredondada e aba dobrada para cima, que vista de
        /// cima é um triângulo de pontas redondas (uma ponta na frente, duas atrás). Com os dois
        /// lados da aba, para ver por dentro também. A base da copa fica em y = 0.
        /// </summary>
        public static Mesh BuildHatShell(float crownRadius, float crownHeight)
        {
            const int around = 120;
            const int crownRings = 12;
            const int brimRings = 10;

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // Copa: do topo (r = 0) até a base, um domo achatado.
            for (int ring = 0; ring <= crownRings; ring++)
            {
                float t = ring / (float)crownRings;
                float r = crownRadius * Mathf.Sin(t * Mathf.PI * 0.5f) * 1.02f;
                float y = crownHeight * Mathf.Cos(t * Mathf.PI * 0.5f) * (1f - 0.08f * t);

                for (int i = 0; i <= around; i++)
                {
                    float a = i / (float)around * Mathf.PI * 2f;
                    // Gasto: uma amassada funda atrás, à direita, e a copa toda um pouco torta.
                    float dent = Mathf.Exp(-Sq(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 130f) / 38f)) * Mathf.Sin(t * Mathf.PI);
                    float lump = 1f + 0.035f * Mathf.Sin(a * 3f + 0.7f) * t;
                    float ry = y * (1f - 0.2f * dent) * (1f + 0.04f * Mathf.Sin(a + 1.1f) * (1f - t));
                    float rr = r * (1f - 0.10f * dent) * lump;
                    vertices.Add(new Vector3(Mathf.Sin(a) * rr * 1.04f, ry, Mathf.Cos(a) * rr));
                    uvs.Add(new Vector2(i / (float)around * 4f, t));
                }
            }
            Grid(triangles, 0, crownRings, around, false);

            // Aba: da base da copa até o contorno de triângulo, subindo.
            int brimStart = vertices.Count;
            for (int ring = 0; ring <= brimRings; ring++)
            {
                float t = ring / (float)brimRings;
                for (int i = 0; i <= around; i++)
                {
                    float a = i / (float)around * Mathf.PI * 2f;
                    float outer = BrimOuter(a, crownRadius);
                    float r = Mathf.Lerp(crownRadius * 1.02f, outer, t);
                    // Dobra para cima: quase reta perto da copa, depois sobe forte; nas pontas sobe menos.
                    float rise = BrimRise(a) * Mathf.Pow(t, 1.7f);
                    vertices.Add(new Vector3(Mathf.Sin(a) * r * 1.04f, rise - 0.004f * t, Mathf.Cos(a) * r));
                    uvs.Add(new Vector2(i / (float)around * 4f, 1f + t));
                }
            }
            Grid(triangles, brimStart, brimRings, around, false);

            // O avesso da aba, 6 mm por dentro, com as faces viradas ao contrário.
            int backStart = vertices.Count;
            for (int k = brimStart; k < backStart; k++)
            {
                vertices.Add(vertices[k] + new Vector3(0f, -0.006f, 0f));
                uvs.Add(uvs[k]);
            }
            Grid(triangles, backStart, brimRings, around, true);

            return Finish("Chapeu_Do_Capitao", vertices, uvs, triangles);
        }

        /// <summary>Os pontos do contorno de fora da aba, para o debrum.</summary>
        public static Vector3 BrimEdge(float angle, float crownRadius)
        {
            float outer = BrimOuter(angle, crownRadius);
            return new Vector3(Mathf.Sin(angle) * outer * 1.04f, BrimRise(angle) - 0.004f, Mathf.Cos(angle) * outer);
        }

        // Altura da beirada da aba. Gasto: a aba é ondulada, caída de um lado, e onde está
        // rasgada a ponta tomba para fora.
        static float BrimRise(float angle)
        {
            float rise = 0.035f + 0.12f * (1f - 0.55f * BrimCorner(angle));
            rise *= 1f + 0.12f * Mathf.Sin(angle * 5f + 0.4f) - 0.22f * Mathf.Exp(-Sq(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, 205f) / 35f));
            return rise * (1f - 0.35f * Tear(angle));
        }

        // Contorno da aba vista de cima: triângulo arredondado, uma ponta para a frente (ângulo 0).
        // Gasto: beirada puída (irregular) e um rasgo em V na frente, à esquerda de quem veste.
        static float BrimOuter(float angle, float crownRadius)
        {
            // Puído: recortes curtos e irregulares, não ondas (ondas largas viravam coroa).
            float fray = 0.02f * Mathf.Sin(angle * 37f + 1.3f) * Mathf.Sin(angle * 11f) + 0.012f * Mathf.Sin(angle * 67f + 0.4f);
            float brim = 0.38f + 0.30f * BrimCorner(angle);
            return crownRadius * (1f + brim * (1f + fray * 2f) * (1f - 0.55f * Tear(angle)));
        }

        // O rasgo em V: 1 no fundo dele, 0 fora.
        static float Tear(float angle)
        {
            return Mathf.Exp(-Sq(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, -42f) / 4.5f));
        }

        // 1 nas três pontas (0°, 120°, 240°), 0 no meio dos lados.
        static float BrimCorner(float angle)
        {
            return Mathf.Pow(0.5f + 0.5f * Mathf.Cos(angle * 3f), 3f);
        }

        // ---------------------------------------------------------------- Casaco

        /// <summary>
        /// O tronco do casaco: um tubo da cintura (y = 0) aos ombros, de seção oval e peito
        /// estufado, mais largo nos ombros. Fechado em cima em volta do pescoço.
        /// </summary>
        public static Mesh BuildCoatTorso(float height, float waistWidth, float shoulderWidth, float depth)
        {
            const int rings = 24, around = 48;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int ring = 0; ring <= rings; ring++)
            {
                float t = ring / (float)rings;
                float y = t * height;
                float halfW = Mathf.Lerp(waistWidth, shoulderWidth, Ease(0f, 0.85f, t)) * 0.5f;
                float halfD = depth * 0.5f * (0.92f + 0.12f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.2f)));
                // Ombro caído e gola: os últimos anéis fecham em volta do pescoço.
                float close = Ease(0.86f, 1f, t);
                halfW = Mathf.Lerp(halfW, 0.09f, close);
                halfD = Mathf.Lerp(halfD, 0.08f, close);
                y -= 0.05f * Ease(0.8f, 0.95f, t) * (1f - close);

                for (int i = 0; i <= around; i++)
                {
                    float a = i / (float)around * Mathf.PI * 2f;
                    float x = Mathf.Sin(a), z = Mathf.Cos(a);
                    // Peito estufado na frente, costas retas.
                    float chest = 1f + 0.10f * Mathf.Max(0f, z) * Mathf.Sin(Mathf.PI * Ease(0.25f, 0.85f, t));
                    float squareness = Mathf.Pow(Mathf.Abs(x), 0.85f) * Mathf.Sign(x);
                    vertices.Add(new Vector3(squareness * halfW, y, z * halfD * chest));
                    uvs.Add(new Vector2(i / (float)around * 3f, t * 2f));
                }
            }

            Grid(triangles, 0, rings, around, true);
            return Finish("Casaco_Tronco", vertices, uvs, triangles);
        }

        /// <summary>
        /// A saia do casaco, da cintura (y = 0) para baixo, abrindo em sino e com uma abertura na
        /// frente e outra atrás (as abas). Tem os dois lados: dá para ver o forro.
        /// </summary>
        public static Mesh BuildCoatSkirt(float length, float waistWidth, float hemWidth, float depth)
        {
            const int rings = 16, around = 64;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // Duas abas: da lateral esquerda à direita passando pelos lados, deixando a frente
            // (±12°, abrindo para baixo) e o meio de trás (±6°) abertos.
            for (int flap = 0; flap < 2; flap++)
            {
                float from = flap == 0 ? 12f : -174f;
                float to = flap == 0 ? 174f : -12f;
                int start = vertices.Count;

                for (int ring = 0; ring <= rings; ring++)
                {
                    float t = ring / (float)rings;
                    float halfW = Mathf.Lerp(waistWidth, hemWidth, Mathf.Pow(t, 1.3f)) * 0.5f;
                    float halfD = Mathf.Lerp(depth, depth * 1.35f, Mathf.Pow(t, 1.3f)) * 0.5f;

                    for (int i = 0; i <= around; i++)
                    {
                        float deg = Mathf.Lerp(from, to, i / (float)around);
                        // A aba de frente se abre mais embaixo, como casaco desabotoado.
                        float open = Ease(0.1f, 1f, t) * 20f * (Mathf.Abs(Mathf.DeltaAngle(deg, 0f)) < 60f ? 1f : 0f);
                        float a = (deg + Mathf.Sign(deg) * open) * Mathf.Deg2Rad;
                        // Dobras do tecido.
                        float fold = 1f + 0.05f * t * Mathf.Sin(deg * 0.21f);
                        vertices.Add(new Vector3(Mathf.Sin(a) * halfW * fold, -t * length, Mathf.Cos(a) * halfD * fold));
                        uvs.Add(new Vector2(i / (float)around * 3f, t * 2f));
                    }
                }

                Grid(triangles, start, rings, around, false);

                int back = vertices.Count;
                for (int k = start; k < back; k++)
                {
                    Vector3 p = vertices[k];
                    Vector3 inward = new Vector3(p.x, 0f, p.z).normalized * 0.008f;
                    vertices.Add(p - inward);
                    uvs.Add(uvs[k]);
                }
                Grid(triangles, back, rings, around, true);
            }

            return Finish("Casaco_Saia", vertices, uvs, triangles);
        }

        // ---------------------------------------------------------------- Tubo

        /// <summary>
        /// Um tubo ao longo de um caminho (debrum do chapéu, a âncora, rachaduras em relevo).
        /// <paramref name="path"/> dá o ponto para t de 0 a 1; as pontas são fechadas.
        /// </summary>
        public static Mesh BuildTube(string name, Func<float, Vector3> path, int steps, float radius, bool loop = false)
        {
            const int around = 10;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            Vector3 previousNormal = Vector3.up;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 p = path(loop && i == steps ? 0f : t);
                Vector3 ahead = path(Mathf.Min(1f, t + 0.5f / steps)) - path(Mathf.Max(0f, t - 0.5f / steps));
                Vector3 tangent = ahead.sqrMagnitude > 1e-10f ? ahead.normalized : Vector3.forward;

                // Moldura que gira o mínimo de um passo ao outro, para o tubo não torcer.
                Vector3 normal = Vector3.Cross(tangent, Vector3.Cross(previousNormal, tangent));
                if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(tangent, Vector3.right);
                normal.Normalize();
                previousNormal = normal;
                Vector3 binormal = Vector3.Cross(tangent, normal);

                // Pontas afinam para fechar o tubo.
                float r = loop ? radius : radius * Mathf.Sqrt(Mathf.Clamp01(Mathf.Min(t, 1f - t) * steps * 0.5f + 0.15f));

                for (int j = 0; j <= around; j++)
                {
                    float a = j / (float)around * Mathf.PI * 2f;
                    vertices.Add(p + (normal * Mathf.Cos(a) + binormal * Mathf.Sin(a)) * r);
                    uvs.Add(new Vector2(t * 8f, j / (float)around));
                }
            }

            // A volta vai da normal para a binormal (tangente x normal): com esta ordem a face fica para fora.
            Grid(triangles, 0, steps, around, true);
            return Finish(name, vertices, uvs, triangles);
        }

        // ---------------------------------------------------------------- Malha

        static void Frame(Vector3 center, out Vector3 u, out Vector3 v)
        {
            Vector3 c = center.normalized;
            u = Vector3.Cross(Vector3.up, c).normalized;
            if (u.sqrMagnitude < 0.01f) u = Vector3.right;
            v = Vector3.Cross(c, u).normalized;
        }

        // Liga cada linha à seguinte com dois triângulos por quadrado. "flip" inverte a face.
        static void Grid(List<int> triangles, int start, int rows, int columns, bool flip)
        {
            int row = columns + 1;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    int a = start + r * row + c, b = a + 1, d = a + row, e = d + 1;
                    if (!flip)
                    {
                        triangles.Add(a); triangles.Add(d); triangles.Add(b);
                        triangles.Add(b); triangles.Add(d); triangles.Add(e);
                    }
                    else
                    {
                        triangles.Add(a); triangles.Add(b); triangles.Add(d);
                        triangles.Add(b); triangles.Add(e); triangles.Add(d);
                    }
                }
            }
        }

        static Mesh Finish(string name, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        static float Sq(float v) => v * v;

        static float Ease(float from, float to, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
        }

        // Calombo oval no plano do rosto, com meias larguras wx e wy.
        static float Gauss(float dx, float dy, float wx, float wy)
        {
            return Mathf.Exp(-Sq(dx / wx) - Sq(dy / wy));
        }

        // Calombo suave em volta de uma direção; largura em "distância entre direções".
        static float Blob(Vector3 d, Vector3 center, float width)
        {
            return Mathf.Exp(-(d - center.normalized).sqrMagnitude / (width * width));
        }
    }
}
