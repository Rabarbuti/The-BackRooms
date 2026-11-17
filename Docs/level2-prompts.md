# Level 2 — prompts das texturas (Higgsfield)

**As quatro folhas já foram geradas e recortadas (12/11/2026).** Cada quarto está em
`Assets/_Game/Art/Fontes/Level2/<nome>.png`: A `Tile_White`, `Mosaic_Pool`, `Tile_White_Worn`,
`Coping`; B `Water_Height`, `Caustics`, `Window_Frosted`, `Glass_Block`; C `Stain_Waterline`,
`Stain_Limescale`, `Stain_GroutMold`, `Stain_Rust`; D `Mark_Depth`, `Mark_NoDiving`, `Mark_DeepEnd`,
`Mark_LaneEnd`. Os prompts abaixo ficam para gerar de novo alguma delas.

Para colar no chat do Claude (ou no Cowork, ou no ChatGPT) com o Higgsfield conectado. No Claude
Code o ilimitado do teste é bloqueado. O que cada folha vira está em `level2-piscinas.md`,
seção 10.

Copie o bloco inteiro abaixo:

````
Use o Higgsfield conectado para gerar 4 imagens com as gerações ILIMITADAS do meu teste grátis.
Não gaste créditos: em toda chamada passe use_unlim: true. Se o ilimitado não estiver disponível
para o modelo, pare e me avise em vez de cobrar.

Ferramenta: generate_image, uma chamada por vez (o ilimitado aceita um job por vez; se der 429,
espere o anterior terminar e tente de novo, sem cobrar crédito).

Parâmetros de todas: model "gpt_image_2_6", aspect_ratio "1:1", resolution "2k",
quality "medium", output_format "png".
Nas folhas C e D, acrescente background "transparent".

Não edite, não faça upscale e não recorte. No fim, me dê o link direto de download de cada PNG
em resolução original, com o nome da folha (A, B, C, D).

--- FOLHA A (superfícies) ---
A 2x2 texture sheet for a video game, four separate square material swatches, each filling exactly one quarter of the image, separated by perfectly straight edges with no gap, no border and no labels. All swatches are flat orthographic views, evenly lit, no shadows, no reflections, no perspective, no text, no watermark, each designed to tile seamlessly.
Top-left: glossy white square ceramic pool tiles, exactly 8 by 8 tiles across the swatch, perfectly aligned grid, thin light grey grout lines all of equal width, very subtle tone variation from tile to tile, clean and new.
Top-right: tiny square pool mosaic tiles, exactly 32 by 32 tiles across the swatch, mostly pale aqua blue with scattered white and light turquoise tiles, thin white grout, perfectly aligned grid.
Bottom-left: the same white square tiles, exactly 8 by 8 across, but aged: yellowed grout, faint limescale haze, a few hairline cracks, slightly dull glaze.
Bottom-right: off-white cast stone pool edge coping seen from straight above, a smooth band running horizontally across the whole swatch with a softly rounded front edge at the bottom, subtle wear.

--- FOLHA B (água e luz) ---
A 2x2 texture sheet for a video game, four separate square swatches, each filling exactly one quarter of the image, separated by perfectly straight edges with no gap, no border and no labels. No text, no watermark, each designed to tile seamlessly where it is a surface.
Top-left: grayscale height map of a calm water surface, gentle overlapping ripples and soft interference patterns, medium grey on average, no lighting, no color, seamless.
Top-right: underwater light caustics pattern, bright white wavy interlocking network on pure black, sharp but soft-edged lines, seamless.
Bottom-left: a frosted glass window pane glowing with soft warm late-afternoon daylight from behind, faint condensation and a few drip streaks, nothing visible through the glass, thin white painted metal frame along all four edges, front view.
Bottom-right: a wall of square frosted glass blocks, exactly 6 by 6 blocks, thin white mortar lines, soft pale aqua light glowing through from behind, nothing visible through them, front view.

--- FOLHA C (manchas, fundo transparente) ---
A 2x2 sheet of four separate grime decals for a video game on a fully transparent background. Each decal sits inside its own quarter with transparent space around it. No wall, no tiles, no background color, no text, no watermark. Soft, natural, slightly irregular edges that fade out.
Top-left: a horizontal waterline stain band spanning the full width of its quarter: a thin dark green-brown line of algae and limescale with fainter residue fading upward above it.
Top-right: vertical streaks of white and beige limescale running down from the top of its quarter, thin drips of different lengths.
Bottom-left: a patch of black mold that follows a square grid pattern, as if growing only inside tile grout lines, with an irregular outline.
Bottom-right: an orange-brown rust stain spreading and dripping downward from a small point near the top, as under a metal drain or ladder bolt.

--- FOLHA D (marcações, fundo transparente) ---
A 2x2 sheet of four separate swimming pool markings for a video game on a fully transparent background. Each marking is centered in its own quarter with transparent space around it. Flat black paint as painted on pool tiles, crisp edges with slight wear and a few chips. No wall, no tiles, no background color, no watermark.
Top-left: the depth marking "1.2 M" in a bold condensed sans-serif typeface.
Top-right: the words "NO DIVING" in a bold condensed sans-serif typeface, with a simple pictogram of a diving person crossed out above it.
Bottom-left: the words "DEEP END" in a bold condensed sans-serif typeface.
Bottom-right: a pool lane end marker in dark navy blue: a thick vertical stripe ending at the top in a short horizontal cross bar, like a T.
````

Se sobrar tempo no teste, gere **uma segunda versão das folhas A e B**: são as que mais aparecem
e as mais difíceis de emendar.
