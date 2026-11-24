using BackRooms.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace BackRooms.World
{
    /// <summary>
    /// O cartaz de dicas da sala inicial de cada fase: um papel pregado na parede, deixado por alguém
    /// que passou por ali, com dicas curtas sobre o monstro da fase (desde 23/11/2026; antes eram
    /// dicas gerais) e a lembrança do Esc. É a única instrução dentro do mundo: o que fazer e as teclas
    /// estão no menu de pausa. Nas fases com objetivo comum quem prega é o <see cref="SwitchObjective"/>;
    /// no Level 1, o <see cref="LevelBuilder"/> (<c>BuildObjective</c>). Sem colisor ("Placa" é
    /// enfeite para o Level0ColliderCheck), sem sorteio: não muda nenhum hash.
    /// </summary>
    public sealed class StartNote : MonoBehaviour
    {
        // ---------------------------------------------------------------- Textos (o jogador lê)
        // PixelFont: só A-Z, 0-9, espaço e : . , / - = ( ) % _ (sem apóstrofo, "!" nem "?").
        // Até 21 letras por linha e 8 linhas. No Level 1, nenhum dígito (as etiquetas vermelhas
        // CLEARANCE são o código da porta; o cartaz não pode parecer uma delas).

        /// <summary>Level 0: a entidade, que caça pelo barulho e vê só para a frente.</summary>
        public static readonly string[] EntityTips =
        {
            "IT HEARS YOU RUN", "CROUCH = SILENCE", "IT SEES ONLY AHEAD", "HIDE IN WALL NICHES",
            "IT NEVER WALKS PLANKS", "THIS ROOM IS SAFE", "FOLLOW THE HUM", "ESC: TASKS"
        };

        /// <summary>Level 1: o Capitão, que só anda sem ser visto com luz.</summary>
        public static readonly string[] CaptainTips =
        {
            "HE MOVES WHEN UNSEEN", "LOOK AT HIM IN LIGHT", "DARK: FLASHLIGHT (F)", "DO NOT STARE TOO LONG",
            "STATUES NEVER TURN", "HIS PEG LEAVES RINGS", "HIDE IN A WARDROBE", "ESC: TASKS"
        };

        /// <summary>Level 2: o Undertow, que vive na água funda e ouve a água.</summary>
        public static readonly string[] UndertowTips =
        {
            "IT LIVES IN THE DEEP", "SPLASHES WAKE IT", "UNDERWATER IS QUIET", "CLIMB OUT TO ESCAPE",
            "BE STILL: IT FORGETS", "ALARM: RUN BACK HERE", "HOLD SPACE TO FLOAT", "ESC: TASKS"
        };

        /// <summary>Level 3: o Zelador (só no escuro) e o Ascensorista (cego, de elevador).</summary>
        public static readonly string[] OfficeTips =
        {
            "STAY IN THE LIGHT", "SOMEONE MOPS THE DARK", "DARK: HIT THE SWITCH", "ELEVATOR MAN IS BLIND",
            "BLINKING ARROW = HIM", "CROUCH LOW. NEVER RUN", "STEP OUT OF HIS WAY", "ESC: TASKS"
        };

        /// <summary>Os cartazes das quatro fases, com o nome de cada uma (para as fotos de conferência).</summary>
        public static (string level, string[] lines)[] All => new[]
        {
            ("level0", EntityTips), ("level1", CaptainTips), ("level2", UndertowTips), ("level3", OfficeTips)
        };

        /// <summary>O cartaz da fase aberta, em qualquer máquina; nulo onde não coube ou fora de jogo.</summary>
        public static StartNote Current { get; private set; }

        /// <summary>As linhas deste cartaz (cópia).</summary>
        public string[] Lines => (string[])_lines.Clone();

        string[] _lines = new string[0];

        const int Width = 128;
        const int LineStep = 11;
        const int Margin = 6;
        const float QuadWidth = 0.48f;

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// <summary>
        /// O desenho do cartaz: as linhas centradas, tinta azul no papel. 128 pontos de largura; a
        /// altura cresce com as linhas (5 linhas: 64, como o cartaz antigo; 8 linhas: 96).
        /// </summary>
        public static Texture2D Texture(string[] lines)
        {
            int w = Width, h = HeightFor(lines.Length);
            var pixels = new Color32[w * h];
            var paper = new Color32(236, 228, 196, 255);
            var ink = new Color32(30, 34, 90, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = paper;
            int top = (h - (lines.Length * LineStep - (LineStep - PixelFont.GlyphHeight))) / 2;
            for (int i = 0; i < lines.Length; i++)
                PixelFont.Draw(pixels, w, h, (w - PixelFont.Measure(lines[i], 1)) / 2, top + i * LineStep, lines[i], 1, ink);
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "BilheteEnsino" };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        static int HeightFor(int lines) => Mathf.Max(64, lines * LineStep - (LineStep - PixelFont.GlyphHeight) + 2 * Margin);

        /// <summary>
        /// Prega o cartaz na primeira parede lisa em volta do meio da sala inicial (ordem fixa: igual
        /// em todas as máquinas; os raios ignoram jogadores). Devolve a raiz (nome <paramref name="name"/>)
        /// ou nulo se nenhuma parede serviu.
        /// </summary>
        public static StartNote Pin(LevelBuilder level, Transform parent, string name, string[] lines, Material template)
        {
            LevelData data = level.Data;
            if (data == null) return null;
            Physics.SyncTransforms();
            // Uma parede fechada de alguma célula da sala inicial, em ordem fixa (z, x, lado).
            for (int z = data.RoomZ0; z < data.RoomZ0 + Mathf.Max(1, data.RoomHeight); z++)
            for (int x = data.RoomX0; x < data.RoomX0 + Mathf.Max(1, data.RoomWidth); x++)
            foreach (WallSide wall in SwitchObjective.Sides)
            {
                if (x < 0 || z < 0 || x >= data.Width || z >= data.Height || !data.HasWall(x, z, wall)) continue;
                Vector3 dir = level.transform.TransformDirection(SwitchObjective.SideDirection(wall));
                Vector3 center = level.CellCenter(x, z) + Vector3.up * 1.4f;
                if (!SwitchObjective.WallRay(center, dir, LevelBuilder.Cell * 0.5f + 0.4f, out RaycastHit hit)) continue;
                if (Vector3.Dot(hit.normal, -dir) < 0.97f) continue;
                Vector3 side = Vector3.Cross(Vector3.up, hit.normal).normalized;
                bool flat = true;
                for (int s = -1; s <= 1; s += 2)
                    if (!SwitchObjective.WallRay(center + side * (0.3f * s), dir, hit.distance + 0.3f, out RaycastHit other) ||
                        Mathf.Abs(other.distance - hit.distance) > 0.03f) flat = false;
                if (!flat) continue;

                Texture2D texture = Texture(lines);

                var root = new GameObject(name);
                root.transform.SetParent(parent, false);
                root.transform.SetPositionAndRotation(hit.point + hit.normal * 0.012f, Quaternion.LookRotation(-hit.normal, Vector3.up));
                var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
                card.name = "Placa";
                Collider c = card.GetComponent<Collider>();
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
                card.transform.SetParent(root.transform, false);
                // O mesmo tamanho de ponto em qualquer altura (3,75 mm por ponto).
                card.transform.localScale = new Vector3(QuadWidth, QuadWidth * texture.height / texture.width, 1f);
                Material material = SwitchObjective.Tinted(template, Color.white, 0.2f);
                material.name = "BilheteEnsino";
                material.SetTexture("_BaseColorMap", texture);
                var renderer = card.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;

                var note = root.AddComponent<StartNote>();
                note._lines = (string[])lines.Clone();
                Current = note;
                return note;
            }
            return null;
        }
    }
}
