# Level 2 — As Piscinas

Projeto da terceira fase. Escrito em 11/11/2026.

**Já existe:** uma versão de teste jogável (`Scenes/Level2_Test.unity`, menu BackRooms > Montar
Level 2 (teste)) com as etapas 1 a 6 da seção 8.1:
- **Planta:** a do Level 0 em 16 x 16 células, sem buracos, nichos nem entidade, com profundidade
  por célula (`LevelData.Depth`: seco +0,2 m, raso −0,4, fundo −1,2, poço −6, água em 0, tudo na
  grade do azulejo de 20 cm) e escadas de 3 ou 4 degraus de 20 cm em cada desnível
  (`LevelData.Stairs`).
- **Regras do gerador:** saída e sala inicial secas; seco nunca encosta em fundo; poço só encosta
  em fundo; tudo que não é poço continua alcançável a pé (`LevelGenerator.AllReachableOnFoot`).
- **Construção:** azulejo em tudo, pastilha no fundo, capa na borda das áreas fundas, paredes até
  7 m abaixo da água.
- **Luz:** claraboias grandes no lugar das luminárias, uma a mais a cada três células apagadas, e
  luz rebatida mais forte.
- **Água** (`PoolWater`): uma lâmina transparente ondulando e, com a cabeça debaixo d'água, névoa
  verde-água.
- **Jogador:** anda mais devagar na água (85% na canela, 55% no peito, sem correr), nada no poço
  (Espaço sobe, Ctrl/C mergulha), tem 20 s de fôlego submerso e, quando o fôlego acaba, se afoga e
  volta ao início, como na queda no buraco.
- **Filmadora:** `WATER` e a barra `AIR` com a cabeça debaixo d'água.
- **Caneta:** não risca debaixo d'água.

**Falta:** maré, ralo, casas de máquinas e drenagem, arcos, salões de 8 m, anomalias, manchas, som
e as texturas das folhas B, C e D (por ora a água e as claraboias são pintadas em código).

---

## 1. A ideia em uma frase

**Um complexo sem fim de salas de azulejo branco, cheias de água morna até a canela, ao peito ou
sem fundo, iluminado por um sol que não se vê.** Calmo, bonito e limpo demais. Não há monstro
caçando ninguém: o perigo é a própria água, que sobe e desce sozinha, e o que mora no fundo dos
poços escuros.

Quem sai pela porta da doca do Level 1 desce uma escada de azulejo e a água já está nos tornozelos.

### O que muda em relação aos níveis anteriores

| | Level 0 | Level 1 | Level 2 — As Piscinas |
|---|---|---|---|
| Sensação | Opressão, cansaço | Bagunça, desconfiança | **Calma bonita demais**, sonho |
| Chão | Carpete; buracos com faixa | Móveis e barricadas | **Água em quatro profundidades**, escadas que somem nela |
| Perigo | Entidade que ouve | Capitão que anda quando não é visto | **A maré** e **o ralo**; nenhum monstro perseguindo |
| Luz | Fluorescente, apagão | Fluorescente, apagão | **Claraboias de luz do dia** e cantos totalmente escuros; no "anoitecer" só sobram as luzes dentro d'água |
| Som | Zumbido | Música de loja | **Abafado**: o eco morre de repente, a voz não vai longe |
| Esconderijo | Nicho | Guarda-roupa | Não há do que se esconder — há onde **não afundar** |

Contra as quatro forças do plano (`plano-coop.md`, seção 1):
- **Ficar junto:** quem afunda numa área funda precisa de alguém na borda para achar a escada no
  escuro, e a voz abafada não alcança longe.
- **Se separar:** o objetivo (seção 6) exige duas casas de máquinas ao mesmo tempo, longe uma da
  outra.
- **Falar:** o som abafado obriga a chegar perto para combinar.
- **Ficar calado:** espirrar água acorda o ralo (seção 5).

---

## 2. Referências

### 2.1 A wiki — Level 37 "Sublimity"

No Backrooms comunitário, as Poolrooms são o **Level 37**, um complexo de salas e corredores
ligados sem lógica, levemente submersos numa água morna que ondula sem nada a mover. Tudo é o
mesmo azulejo branco, "praticamente indestrutível"; a única cor é o azul-esverdeado da água.
Pilares saem da água em número grande demais para ter função. Muitas áreas de água aberta não têm
borda para subir. A luz vem de ângulos irregulares e deixa alguns cantos totalmente escuros.
Algumas escadas descem direto para poços fundos; entrar nas áreas fundas é desaconselhado, pelo
risco de afogamento no escuro. O som é "severamente alterado": cai de repente, não ecoa direito,
a fala parece abafada. Classificado como seguro, sem entidades registradas; saídas por túneis
escuros e por mergulho.

Fonte: [Backrooms Pantheon — Level 37 "Sublimity"](https://backrooms-pantheon.wikidot.com/level-37)
(texto CC BY-SA). Ver também [Level 37 na Backrooms Wiki](https://backrooms.fandom.com/wiki/Level_37).

### 2.2 As Dream Pools de Jared Pike

A imagem que todo mundo associa às Poolrooms vem da série *Dream Pools*, do artista 3D Jared
Pike (desde 2020): azulejo branco "das piscinas da infância", sol quente entrando por um lado e
**cantos de propósito sem luz**, nostalgia e estranhamento juntos. Corredores longos de azulejo
quadrado, água clorada até a barriga, janelas que não abrem e sempre embaçadas, nenhuma pista de
época nem de que horas são.

**Não copiamos nenhuma imagem dele** — usamos o vocabulário (azulejo, arco, claraboia, escada que
entra na água, sol contra sombra) e fazemos as nossas salas.

Fontes: [designboom — Jared Pike](https://www.designboom.com/art/diving-3d-artist-jared-pike-imaginary-liminal-interiors-pool-dreamscapes-02-18-2024/),
[LONER — A descent into the Dream Pools](https://www.lonerofficial.com/post/jared-pike-a-descent-into-the-dream-pools).

### 2.3 POOLS (Tensori, 2024)

Walking simulator puro nas Poolrooms, seis capítulos, sem monstro e sem música: o som da água e o
eco que muda de sala para sala carregam tudo. Andar na água é mais lento. Os críticos elogiam
os corredores apertados alternando com salões enormes e um capítulo que inclina a visão; a crítica
é ser curto. (Mais em `contexto-backrooms.md`, seção 2.5.)

Fontes: [GameSpew](https://www.gamespew.com/2024/04/pools-review-a-liminal-hidden-gem/),
[Finger Guns](https://fingerguns.net/games/2024/05/21/pools-review-pc-the-backroom-buoys/).

### 2.4 O que tiramos de cada um

- **Da wiki:** pilares demais, escadas que somem na água, cantos escuros, som abafado, área funda
  perigosa, saída pelo fundo.
- **Do Pike:** sol quente por claraboias e janelas embaçadas, arcos e salões, beleza calma.
- **Do POOLS:** água que desacelera, eco por sala, alternância corredor apertado / salão.
- **Nosso:** a maré sincronizada, o ralo, a filmadora sofrendo debaixo d'água e o objetivo de
  esvaziar a piscina.

---

## 3. O espaço

- **Grade de 4 m, como o Level 0**, mas com **altura de piso por célula** e um **nível de água por
  região**. 16 x 16 células (64 x 64 m): menor que o Level 0, porque andar na água é lento.
- **Pé-direito 4 m nos corredores e 8 m nos salões** (blocos de 2 x 2 ou 3 x 3 células sem paredes
  internas, cheios de pilares).
- **Tudo é azulejo branco de 20 x 20 cm** com rejunte cinza-claro: piso, parede e teto. 4 m são
  exatamente 20 azulejos, então o rejunte fecha nas quinas das células. Fundo das piscinas em
  **pastilha** pequena azul-clara; bordas com **capa arredondada** de pedra clara.
- **Passagens em arco** em vez de vão reto. Pilares redondos ou quadrados de azulejo, muitos.
- **Sala inicial = patamar seco** no alto de uma escada larga, limpa, com uma claraboia acesa.
  Quem sai do patamar já desce para a água.
- **Desgaste pela distância** (`LevelGenerator.WearAt`, como no Level 0): perto do início, branco
  impecável; longe, rejunte amarelado, calcário, limo na linha d'água, azulejo faltando.

### 3.1 Profundidades

Cada célula tem um piso numa de quatro alturas em relação à superfície da água da região:

| Profundidade | Água | Andar | Barulho de passo | Outros |
|---|---|---|---|---|
| **Seco** | — | normal | baixo | patamares, bordas, escadas |
| **Rasa** (canela, 0,3 m) | sim | 85% | alto (espirra) | a maior parte do mapa |
| **Funda** (peito, 1,2 m) | sim | 55%, não corre | médio (empurra água) | lanterna e caneta ainda funcionam |
| **Poço** (sem fundo visível, 6 m) | sim | **nada** (40%), cai devagar | baixo | fôlego (seção 4.2), escuro, o ralo |

Escadas ligam alturas vizinhas. **Escada para dentro do poço** existe (wiki), sempre com um
corrimão de metal para achar a saída no escuro.

### 3.2 Zonas

Sorteadas por bloco de células, como os temas do Level 1:

| Zona | Peso | O que tem | Para que serve |
|---|---|---|---|
| Corredor de azulejo | 4 | Corredor de 1 célula, água rasa, arcos repetidos até o ponto de fuga | Perspectiva infinita; barulho de passo |
| Salão de pilares | 3 | Bloco de 3 x 3, pé-direito 8 m, pilares a cada 2 m, água funda | Perde-se a direção; a luz vem de um lado só |
| Piscina | 2 | Raias pintadas no fundo, borda com capa, escadinha de metal, plaquinhas de profundidade | O lugar "normal" — e por isso estranho |
| Escadaria | 2 | Escada larga descendo para dentro d'água até sumir | Liga alturas; leva aos poços |
| Poço | 1 | Célula sem fundo visível, escura, ralo no fundo | Perigo e atalho (túnel submerso) |
| Tobogã | 0,5 | Tobogã de azulejo saindo da parede e caindo na água | Atalho de mão única |
| Claraboia | 1 | Sala com claraboia redonda ou janela embaçada estourando de luz | Marco de orientação |
| Bloco de vidro | 0,7 | Parede de tijolo de vidro com luz verde-água atrás | Marco; não dá para ver através |
| Casa de máquinas | — | Sempre 2 a 3 no mapa (seção 6): bombas, canos, válvula grande | Objetivo |

### 3.3 Anomalias (raras)

- Uma escada que sobe para o teto e continua dentro dele.
- Uma porta de azulejo na parede, sem maçaneta, com água escorrendo pela fresta.
- Plaquinhas de profundidade erradas: "-2 M", "∞", "1.2 M" num lugar seco.
- Uma raia pintada no fundo que continua pelas paredes e pelo teto.

---

## 4. A água

### 4.1 Como se vê

- **Primeira tentativa: o sistema de água do HDRP** (Water Surface do tipo *Pool*), que já faz
  ondulação, reflexo, cáustica no fundo e visão subaquática. Precisa ser ligado no asset do HDRP e
  medido: são muitas superfícies e 165 luzes com sombra.
- **Reserva, se pesar demais:** um plano transparente por região com mapa de normais de ondulação
  rolando e um decalque de **cáustica** animado projetado no fundo (texturas nas folhas da seção 10).
- A água é azul-esverdeada e clara no raso; no poço o fundo some num azul-escuro quase preto.
- Uma **ondulação constante** sem causa (wiki), mais forte perto dos ralos.

### 4.2 Como se joga

- **Lentidão e barulho** pela profundidade (seção 3.1).
- **Fôlego:** com a cabeça debaixo d'água o jogador tem 20 s. O painel da filmadora mostra
  `AIR` e uma barra; nos últimos 5 s a imagem escurece. Acabou o fôlego = **afogado**, a mesma
  regra da queda no buraco e da captura: NO SIGNAL e volta ao patamar inicial. Não há morte.
- **Filmadora molhada:** debaixo d'água a fita sofre — chuvisco forte, cor lavada, som abafado
  e o aviso `WATER`. Ao sair, a imagem demora uns segundos para limpar.
- **A caneta não risca debaixo d'água.** Risca azulejo seco e molhado acima da linha d'água.

### 4.3 A maré

O nível da água de cada região **sobe e desce sozinho**, junto em todas as máquinas, pelo relógio
`GameSession.SessionSeconds` (a mesma regra do apagão). Ciclo de 90 s: 60 s parada, 15 s subindo
até +0,9 m, 15 s descendo.

- Na maré alta, o raso vira fundo e o fundo vira poço: corredores que eram passagem viram nado, e
  quem estava num patamar baixo fica ilhado.
- **Aviso:** 10 s antes, os ralos borbulham e a água começa a puxar levemente para eles; o som
  da sala muda (um "glub" grave e longe).
- Substitui o **apagão** do Level 0 como o evento global da fase. O apagão continua existindo,
  mas aqui é o **anoitecer** (seção 7).

---

## 5. O perigo: o ralo

Não há monstro andando atrás de ninguém — a wiki e o POOLS acertaram nisso, e é o que separa esta
fase das outras duas. Há **o ralo**: os poços têm, no fundo, uma grade grande e escura. Ninguém
sabe o que tem atrás dela.

- **Ouve espirro:** passos na água rasa e quem corre na funda fazem barulho (a mesma conta de
  ruído do `EntityBrain`). Barulho perto de um poço "acorda" o ralo daquele poço.
- **Acordado, ele puxa:** por 8 s a água do poço e das células vizinhas gira e arrasta para a
  grade, mais forte quanto mais perto. Quem está nadando no poço é puxado para baixo; quem está na
  borda, não. Ser puxado até a grade = afogado (seção 4.2).
- **Sinal:** antes de puxar, as luzes de dentro d'água daquele poço piscam e a superfície forma um
  redemoinho. Nunca se vê nada atrás da grade — no máximo, uma vez, uma mão branca de dedos
  compridos segurando a grade por dentro, por meio segundo, se a lanterna bater nela.
- **Atalho:** quando o ralo dorme, os poços escondem **túneis submersos** que ligam regiões
  distantes — o caminho mais curto, e o mais arriscado.

Fica em aberto um inimigo de verdade (decisão 2, seção 9).

---

## 6. Objetivo da fase

**Esvaziar a piscina.** A saída é uma porta no **fundo do maior poço**, só alcançável sem água.
Para drenar, o grupo precisa abrir as **válvulas de duas casas de máquinas** ao mesmo tempo
(janela de 5 s), em pontas opostas do mapa (plano, seção 2.3). Cada válvula leva 6 s girando e faz
barulho — acorda os ralos por perto.

Drenado, o nível da água de todo o mapa desce 2 m em 30 s: os poços viram buracos secos com
escadas, as escadas que sumiam na água aparecem inteiras, e no fundo do poço grande a porta fica à
vista. Com 1 jogador, a regra de simultaneidade é desligada (as válvulas ficam abertas).

As casas de máquinas são sorteadas pela seed a pelo menos 8 células uma da outra e fora das áreas
de poço; a porta de saída, no poço mais distante do início.

---

## 7. Luz

- **Claraboias e janelas embaçadas** brancas, estourando de luz, nunca mostrando o que tem do
  outro lado. Luz quente de "fim de tarde" entrando de lado, sombras longas e duras de pilar.
- **Cantos totalmente escuros** de propósito: a luz não chega em todo lugar (wiki e Pike).
- **Luzes dentro d'água** nas paredes das piscinas e dos poços, brancas-azuladas.
- **Nada de lâmpada fluorescente.** É o que mais separa visualmente do Level 0 e do Level 1.
- **Anoitecer** (o apagão desta fase): as claraboias escurecem para azul-marinho em 10 s e ficam
  apagadas pelo mesmo tempo do apagão do Level 0; sobram as luzes de dentro d'água, que deixam as
  piscinas brilhando no escuro, e as lanternas.

---

## 8. Como construir

- **`LevelGenerator` com mais duas camadas:** altura de piso por célula (seco, raso, fundo, poço)
  e regiões de água com nível próprio. Regra nova, no espírito de "buraco nunca isola": **toda
  célula seca ou rasa continua alcançável sem nadar** na maré baixa — sempre há caminho a pé até
  as duas casas de máquinas e até a borda do poço da saída.
- **`LevelBuilder`:** parede com vão em **arco** (malha + colisor juntos), escadas por célula,
  borda com capa arredondada, pilares, teto alto nos salões. Mesmo `MeshBuilder`, UV por metro
  para o rejunte fechar.
- **Água:** primeiro o HDRP Water; medir. Física: um volume de água por região (gatilho) que o
  `PlayerController` consulta para lentidão, nado e fôlego.
- **Modelos** (pelo caminho do Blender, `modelos-blender.md`): escadinha de metal de piscina,
  corrimão, cadeira de salva-vidas, grade de ralo, válvula grande e bombas da casa de máquinas,
  tobogã de azulejo, plaquinha de profundidade, boia esquecida.
- **Montagem da cena** pelo `Level0Setup.SceneOptions`, como o Level 1, mais `LevelCatalog`
  (índice 2) e lista de cenas da build.

### 8.1 Ordem

| # | Etapa | Depende de |
|---|---|---|
| 1 | Texturas (seção 10) emendadas e integradas | — |
| 2 | Gerador: alturas, regiões de água, caminho seco garantido; mapa 2D para conferir | — |
| 3 | Construtor: azulejo, arcos, escadas, bordas, pilares, salões; prévia em imagem | 1, 2 |
| 4 | Água visível (HDRP Water ou reserva) e medida de desempenho | 3 |
| 5 | Física da água: lentidão, nado, fôlego, filmadora molhada | 4 |
| 6 | Cena `Level2_Test`, `LevelCatalog`, testes (`Level2Setup.BatchCheck`, em Play) | 3 |
| 7 | Maré sincronizada | 5 |
| 8 | Ralo e túneis submersos | 5 |
| 9 | Casas de máquinas, válvulas, drenagem e porta do fundo | 7 |
| 10 | Luz (claraboias, cantos escuros, luz subaquática, anoitecer) | 3 |
| 11 | Som | 5 |

1 e 2 andam em paralelo.

---

## 9. Decisões em aberto

1. **Nome.** "Level 2 — As Piscinas" (nossa ordem) ou "Level 37" como na wiki?
2. **Inimigo de verdade?** A proposta é **nenhum** — o ralo e a maré bastam, e a calma é o que
   assusta depois de dois níveis de monstro. Alternativa: algo que só aparece como sombra embaixo
   d'água e segue quem espirra.
3. **HDRP Water ou água própria?** Depende da medida da etapa 4.
4. **Voz abafada** só vale quando existir voz por proximidade (Vivox, etapa 5 do plano).

---

## 10. Texturas (Higgsfield)

Todas em **folhas 2 x 2** (deu certo no Level 0): uma imagem 2K, um material por quarto, recortado
e emendado depois em Python, com a cor média conferida contra a cena. O azulejo e a pastilha têm o
número exato de peças por quarto para o rejunte ser recortado no período certo.

| Folha | Quarto | Vira | Onde |
|---|---|---|---|
| A — superfícies | sup. esq. | `Tile_White` (8 x 8 azulejos = 1,6 m) | piso, parede, teto |
| | sup. dir. | `Mosaic_Pool` (32 x 32 pastilhas) | fundo de piscina e poço |
| | inf. esq. | `Tile_White_Worn` | o mesmo azulejo longe do início |
| | inf. dir. | `Coping` | capa arredondada da borda |
| B — água e luz | sup. esq. | `Water_Height` (vira normais) | ondulação (reserva da água) |
| | sup. dir. | `Caustics` | cáustica projetada no fundo |
| | inf. esq. | `Window_Frosted` (emissiva) | janelas e claraboias |
| | inf. dir. | `Glass_Block` (emissiva) | parede de tijolo de vidro |
| C — manchas (fundo transparente) | sup. esq. | `Stain_Waterline` | limo na linha d'água |
| | sup. dir. | `Stain_Limescale` | escorrido de calcário |
| | inf. esq. | `Stain_GroutMold` | mofo no rejunte |
| | inf. dir. | `Stain_Rust` | ferrugem embaixo de ralo e escada |
| D — marcações (fundo transparente) | sup. esq. | `Mark_Depth` "1.2 M" | borda das piscinas |
| | sup. dir. | `Mark_NoDiving` | borda |
| | inf. esq. | `Mark_DeepEnd` | parede do poço |
| | inf. dir. | `Mark_LaneEnd` | fundo das piscinas |

As folhas C e D precisam de **fundo transparente**, que só o GPT Image faz. Os prompts prontos
estão na mensagem que acompanhou este projeto; ficam também em `Docs/level2-prompts.md`.
