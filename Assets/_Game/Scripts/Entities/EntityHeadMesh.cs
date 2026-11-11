using System.Collections.Generic;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// A cabeça da entidade, esculpida como malha em código: crânio comprido e sem olhos,
    /// mandíbula e dente. Montar a cabeça com esferas esticadas dava cara de balão.
    ///
    /// O crânio é uma "casca torneada": anda-se do focinho até a ponta de trás e, a cada
    /// passo, desenha-se um contorno fechado. O contorno não é um oval simples — tem uma
    /// crista no alto, um sulco de cada lado dela, as têmporas cavadas, o lado de baixo
    /// achatado e, da metade para trás, degraus como placas sobrepostas.
    ///
    /// A mandíbula encaixa por baixo do crânio sem fresta e leva um enchimento que tapa os
    /// lados e o fundo da boca quando ela abre: a boca só aparece na frente.
    /// </summary>
    public static class EntityHeadMesh
    {
        const int Rings = 110;
        const int Sides = 44;

        // ---------------------------------------------------------------- Forma do crânio

        // O contorno na posição s (0 = ponta do focinho, 1 = ponta de trás): onde fica o centro,
        // a meia largura e a meia altura para cima e para baixo. No espaço da cabeça, +Z é
        // para onde o rosto aponta e +Y é para cima.
        static void Section(float s, out Vector3 center, out float width, out float top, out float bottom)
        {
            float z = Mathf.Lerp(0.33f, -1.16f, s);
            float y = 0.045f + 0.17f * Mathf.Sin(Mathf.PI * Mathf.Pow(s, 0.72f)) - 0.07f * s;
            center = new Vector3(0f, y, z);

            width = 0.138f * Ease(-0.2f, 0.2f, s) * (1f - 0.9f * Ease(0.42f, 1.02f, s));
            float height = 0.124f * Ease(-0.3f, 0.2f, s) * (1f - 0.9f * Ease(0.38f, 1.02f, s));
            top = height;
            bottom = height * 0.5f;
        }

        // Distância do centro até a casca na direção dada (0° = direita, 90° = cima).
        static float Radius(float s, float degrees, float width, float top, float bottom)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(radians)), n = Mathf.Sin(radians);
            float height = n >= 0f ? top : bottom;

            // Oval um pouco quadrado.
            const float power = 2.5f;
            float r = 1f / Mathf.Pow(Mathf.Pow(c / width, power) + Mathf.Pow(Mathf.Abs(n) / height, power), 1f / power);

            // Crista no alto, do meio da testa até perto da ponta, com um sulco de cada lado.
            float crest = Ease(0.07f, 0.28f, s) * (1f - Ease(0.84f, 1f, s));
            r *= 1f + 0.19f * crest * Bump(degrees - 90f, 12f);
            r *= 1f - 0.085f * crest * (Bump(degrees - 57f, 9f) + Bump(degrees - 123f, 9f));

            // Têmporas cavadas: onde estariam os olhos há só uma depressão comprida.
            float temple = Ease(0.03f, 0.15f, s) * (1f - Ease(0.3f, 0.58f, s));
            r *= 1f - 0.16f * temple * (Bump(degrees - 16f, 15f) + Bump(degrees - 164f, 15f));

            // Maçãs do rosto, logo abaixo das têmporas.
            float cheek = Ease(0.05f, 0.14f, s) * (1f - Ease(0.2f, 0.34f, s));
            r *= 1f + 0.07f * cheek * (Bump(degrees + 14f, 12f) + Bump(degrees - 194f, 12f));

            // Da metade para trás, placas sobrepostas: cada uma engrossa e termina num degrau.
            float plates = Ease(0.3f, 0.4f, s) * (n > -0.25f ? 1f : 0.25f);
            r *= 1f + 0.06f * plates * (Mathf.Repeat(s * 12f, 1f) - 0.5f);

            // Irregularidade, para os dois lados não ficarem idênticos.
            r *= 1f + 0.022f * Mathf.Sin(degrees * 0.047f + s * 31f) * Mathf.Sin(s * 17f + 1.3f);

            return r;
        }

        /// <summary>Um ponto da superfície do crânio, um pouco afastado dela (para apoiar cipós e dentes).</summary>
        public static Vector3 SurfacePoint(float s, float degrees, float lift = 0f)
        {
            Section(s, out Vector3 center, out float width, out float top, out float bottom);
            float r = Radius(s, degrees, width, top, bottom) + lift;
            float radians = degrees * Mathf.Deg2Rad;
            return center + new Vector3(Mathf.Cos(radians) * r, Mathf.Sin(radians) * r, 0f);
        }

        /// <summary>A beirada de baixo do focinho na posição s: onde nascem os dentes de cima.</summary>
        public static Vector3 UpperGum(float s, float across)
        {
            Section(s, out Vector3 center, out float width, out _, out float bottom);
            return center + new Vector3(across * width * 0.84f, -bottom * 0.88f, 0f);
        }

        public static Mesh BuildSkull()
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int ring = 0; ring <= Rings; ring++)
            {
                float s = ring / (float)Rings;
                Section(s, out Vector3 center, out float width, out float top, out float bottom);

                for (int side = 0; side <= Sides; side++)
                {
                    float degrees = 360f * side / Sides;
                    float radians = degrees * Mathf.Deg2Rad;
                    float r = Radius(s, degrees, width, top, bottom);

                    vertices.Add(center + new Vector3(Mathf.Cos(radians) * r, Mathf.Sin(radians) * r, 0f));
                    uvs.Add(new Vector2(s * 5f, side / (float)Sides * 2f));
                }
            }

            Stitch(triangles, Rings, Sides);

            // Tampas: um ponto no meio de cada extremidade.
            Cap(vertices, uvs, triangles, 0, true);
            Cap(vertices, uvs, triangles, Rings, false);

            return Finish("Cranio", vertices, uvs, triangles);
        }

        // ---------------------------------------------------------------- Mandíbula

        const float JawLength = 0.318f;

        // Altura do lado de baixo do crânio, na linha do meio, na coordenada z da cabeça.
        static float Underside(float z)
        {
            Section(Mathf.InverseLerp(0.33f, -1.16f, z), out Vector3 center, out _, out _, out float bottom);
            return center.y - bottom;
        }

        /// <summary>Onde a mandíbula gira: encostada no lado de baixo do crânio, no fundo da boca.</summary>
        public static Vector3 JawHinge => new Vector3(0f, Underside(0f), 0f);

        // A mandíbula na posição t (0 = dobradiça, 1 = queixo), no espaço dela (origem na
        // dobradiça). Fechada, a face de cima acompanha o lado de baixo do crânio, sem fresta.
        //
        // Da dobradiça até a metade ela tem um enchimento que sobe para dentro do crânio:
        // fechada, ele fica escondido lá dentro; aberta, é ele que aparece no lugar da fresta.
        // Assim os lados e o fundo da boca ficam sempre tapados, e a boca só abre na frente.
        static void JawSection(float t, out Vector3 center, out float width, out float below, out float above)
        {
            float z = JawLength * t;
            float lip = Underside(z) - Underside(0f) - 0.003f;
            float thickness = Mathf.Lerp(0.052f, 0.03f, t) * (1f - 0.7f * Ease(0.93f, 1f, t));
            float filler = Mathf.Min(z * 0.8f, 0.15f) * (1f - Ease(0.5f, 0.66f, t));

            width = Mathf.Lerp(0.094f, 0.043f, Mathf.Pow(t, 1.25f)) * (0.55f + 0.45f * Ease(0f, 0.12f, t));
            center = new Vector3(0f, lip - thickness * 0.5f, z);
            below = thickness * 0.65f;
            above = thickness * 0.5f + filler;
        }

        /// <summary>A beirada de cima da mandíbula: onde nascem os dentes de baixo.</summary>
        public static Vector3 LowerGum(float t, float across)
        {
            JawSection(t, out Vector3 center, out float width, out _, out float above);
            return center + new Vector3(across * width * 0.8f, above * 0.75f, 0f);
        }

        /// <summary>O meio da face de cima da mandíbula, onde fica a língua.</summary>
        public static Vector3 JawTop(float t)
        {
            JawSection(t, out Vector3 center, out _, out _, out float above);
            return center + new Vector3(0f, above, 0f);
        }

        public static Mesh BuildJaw()
        {
            const int rings = 34, sides = 24;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // A malha é percorrida do queixo para a dobradiça, para ficar no mesmo sentido do crânio.
            for (int ring = 0; ring <= rings; ring++)
            {
                float t = 1f - ring / (float)rings;
                JawSection(t, out Vector3 center, out float width, out float below, out float above);

                for (int side = 0; side <= sides; side++)
                {
                    float radians = Mathf.PI * 2f * side / sides;
                    float c = Mathf.Cos(radians), n = Mathf.Sin(radians);

                    // Quase retangular: paredes laterais em pé, para taparem os lados da boca.
                    const float power = 3.4f;
                    float tall = n >= 0f ? above : below;
                    float r = 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(c) / width, power) + Mathf.Pow(Mathf.Abs(n) / tall, power), 1f / power);

                    vertices.Add(center + new Vector3(c * r, n * r, 0f));
                    uvs.Add(new Vector2(t * 1.4f, side / (float)sides));
                }
            }

            Stitch(triangles, rings, sides);
            Cap(vertices, uvs, triangles, 0, true, sides);
            Cap(vertices, uvs, triangles, rings, false, sides);

            return Finish("Mandibula", vertices, uvs, triangles);
        }

        // ---------------------------------------------------------------- Dente

        /// <summary>Cone de base 1 e altura 1, com a ponta para baixo. Cada dente é uma cópia esticada.</summary>
        public static Mesh BuildFang()
        {
            const int sides = 9;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int side = 0; side <= sides; side++)
            {
                float radians = Mathf.PI * 2f * side / sides;
                // Um pouco curvo: a ponta sai do eixo.
                vertices.Add(new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)));
                vertices.Add(new Vector3(Mathf.Cos(radians) * 0.42f + 0.16f, -0.55f, Mathf.Sin(radians) * 0.42f));
                uvs.Add(new Vector2(side / (float)sides, 0f));
                uvs.Add(new Vector2(side / (float)sides, 0.55f));
            }

            int tip = vertices.Count;
            vertices.Add(new Vector3(0.36f, -1f, 0f));
            uvs.Add(new Vector2(0.5f, 1f));

            for (int side = 0; side < sides; side++)
            {
                int a = side * 2, b = a + 1, c = a + 2, d = a + 3;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
                triangles.Add(b); triangles.Add(d); triangles.Add(tip);
            }

            return Finish("Dente", vertices, uvs, triangles);
        }

        // ---------------------------------------------------------------- Malha

        // Liga cada contorno ao seguinte com dois triângulos por lado, com a face para fora.
        static void Stitch(List<int> triangles, int rings, int sides)
        {
            int row = sides + 1;

            for (int ring = 0; ring < rings; ring++)
            {
                for (int side = 0; side < sides; side++)
                {
                    int a = ring * row + side, b = a + 1, c = a + row, d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }
        }

        static void Cap(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, int ring, bool front, int sides = Sides)
        {
            int row = sides + 1;
            int first = ring * row;

            Vector3 middle = Vector3.zero;
            for (int side = 0; side < sides; side++) middle += vertices[first + side];
            middle /= sides;

            int center = vertices.Count;
            vertices.Add(middle + new Vector3(0f, 0f, front ? 0.012f : -0.012f));
            uvs.Add(uvs[first]);

            for (int side = 0; side < sides; side++)
            {
                int a = first + side, b = a + 1;
                if (front) { triangles.Add(center); triangles.Add(a); triangles.Add(b); }
                else { triangles.Add(center); triangles.Add(b); triangles.Add(a); }
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

        static float Ease(float from, float to, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
        }

        // Calombo suave centrado em zero, com a largura dada em graus. Vale para qualquer volta.
        static float Bump(float degrees, float widthDegrees)
        {
            float d = Mathf.DeltaAngle(0f, degrees) / widthDegrees;
            return Mathf.Exp(-d * d);
        }
    }
}
