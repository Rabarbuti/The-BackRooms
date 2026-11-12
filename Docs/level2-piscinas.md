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

### 2.4 A pesquisa de imagens de 12/11/2026

A primeira versão de teste saiu como "o Level 0 com azulejo": o labirinto de corredores de 4 m, teto
escuro, painéis de luz no forro e água verde-acinzentada opaca. Antes de refazer, três agentes olharam
cerca de 130 imagens (as *Dream Pools* originais em jaredpike.art, as wikis, as capturas do POOLS, do
*Escape the Backrooms*, do *Dreamcore* e do *Anemoiapolis*) e mediram formas e cores. O que mudou o
projeto:

- **O erro não era o teto baixo.** O salão típico tem 3,0 a 3,5 m de pé-direito e é LARGO (altura para
  largura de 1:4 a 1:8). O erro era o teto **escuro**, a **grade uniforme de corredores** e a **falta
  de salões**. Poucos espaços chegam a 8–12 m, e por isso viram marco.
- **Não existe luminária aparente** em nenhuma das 27 imagens canônicas: a luz só entra por aberturas
  (claraboia, janelão de vidro fosco, fresta, vigia) e pela água.
- **Formas que se repetem:** coluna-tambor redonda e lisa (Ø 1,2 a 2,2 m, sem base nem capitel), arco
  pleno recortado na parede sem moldura, vigia redonda, encontro de parede com teto arredondado,
  parede grossa com vãos sem porta em série, túnel em abóbada, fenda alta e estreita com luz no topo,
  escada reta que continua para dentro d'água com corrimão de inox, piscina afundada num deque seco e
  poço redondo escuro.
- **A água entra de três jeitos:** piso todo alagado e raso com a parede entrando direto nela (60% das
  imagens), piscina afundada em deque (30%) e poço escuro (10%).
- **Não aparece:** porta com folha, rodapé, moldura, sujeira, mobília, sinalização, cano, rampa.
  Tobogã só existe em material de jogo e de fã.
- **Cor (medida na tela):** azulejo ao sol `#F5F3DA`, meia-sombra esverdeada `#A8BE9B`, água rasa
  `#60C0A4`, turquesa viva `#18BEA4`, funda `#0A9872`, janela estourada `#FEFDF5`, boca de túnel
  `#0E110A`. A cor da água vem do piso branco visto através dela, não de uma película colorida. O medo
  vem de poucos buracos escuros num quadro claro, não de escurecer a sala.
- **Estrutura de fase (jogos):** aglomerados de salas-assinatura ligados com laços; corredor apertado
  antes de salão; um "sol" só no mapa inteiro para orientar; nenhum trecho obrigatório nadando. O
  gerador de Roblox que fazia árvore de salas pequenas sem laço foi achado chato.

Fontes principais: [Dream Pools — Jared Pike](https://www.jaredpike.art/projects/dream-pools/),
[designboom](https://www.designboom.com/art/diving-3d-artist-jared-pike-imaginary-liminal-interiors-pool-dreamscapes-02-18-2024/),
[Level 37 — Backrooms Wiki](http://backrooms-wiki.wikidot.com/level-37),
[POOLS na Steam](https://store.steampowered.com/app/2663530/POOLS/),
[Level 37 — Escape the Backrooms](https://escapethebackrooms.fandom.com/wiki/Level_37),
[UploadVR — POOLS](https://www.uploadvr.com/pools-vr-review/).

### 2.5 O que tiramos de cada um

- **Da wiki:** pilares demais, escadas que somem na água, cantos escuros, som abafado, área funda
  perigosa, saída pelo fundo.
- **Do Pike:** o vocabulário de formas e a luz — sol entrando por aberturas que não se veem, teto
  claro, água turquesa transparente, poucos buracos pretos.
- **Do POOLS:** água que desacelera, eco por sala, corredor apertado antes de salão, e a variante
  escura (água negra espelhada) para a sala do poço e para o anoitecer.
- **Nosso:** a maré sincronizada, o ralo, a filmadora sofrendo debaixo d'água e o objetivo de
  esvaziar a piscina.

**Não copiamos nenhuma imagem** — usamos o vocabulário e montamos as nossas salas pela seed.

---

## 3. O espaço

**A planta é um conjunto de salas, não o labirinto do Level 0.** O `PoolGenerator` espalha salas
retangulares na grade de 4 m (16 x 16 células, 64 x 64 m), deixa **maciços** fechados entre elas (15 a
30% do mapa, o que dá contorno irregular) e liga tudo com laços. Dentro de uma sala não há parede:
há colunas, desníveis e água.

- **Tudo é azulejo branco de 20 x 20 cm**, piso, parede e teto. O teto é claro. Fundo das áreas
  fundas em pastilha azul-clara.
- **Pé-direito de 3,6 m na maioria das salas** e de 4,8 m nos salões maiores; 8 e 12 m só nos marcos
  (no máximo três ou quatro por mapa). Túnel em abóbada com arranque a 2,4 m.
- **Paredes de 0,4 m**, com a espessura aparecendo em todo vão. Passagens recortadas sem moldura:
  arco pleno (vão de 2,4 m), arco abatido largo (3,4 m), vão reto sem porta (1,4 x 2,4 m) e vão total.
  Salões que se encostam ligam-se por uma série de arcos. Vigia redonda (Ø 2 m) deixa ver a sala ao
  lado sem deixar passar.
- **Colunas-tambor** redondas e lisas nos vértices da grade (raio de 0,6 a 0,9 m), esbeltas (0,45 m)
  ou pilares quadrados em galeria e cisterna. Nunca fecham passagem: ficam a pelo menos (raio + 0,6 m)
  das linhas de centro das células.
- **Sanca:** o encontro de toda parede com o teto plano é um quarto de círculo de 0,4 m.
- **Nenhuma luminária.** A luz vem de claraboias (quadrada, redonda, rasgo ou o teto inteiro da
  célula), de janelões, frestas e vigias de vidro fosco aceso, de paredes de tijolo de vidro e de
  lâmpadas dentro d'água. **O sol é um só:** a luz do dia viaja para +X e +Z no mapa inteiro, então as
  janelas ficam sempre nas paredes sul e oeste e toda mancha de sol cai para o mesmo lado. Serve de
  bússola.
- **Sala inicial = patamar de chegada:** seco, com escada de borda a borda descendo para a água.

### 3.1 Profundidades

Cada célula tem um piso numa de quatro alturas (a água fica em Y = 0):

| Profundidade | Piso | Andar | Outros |
|---|---|---|---|
| **Seco** | +0,2 m | normal | patamares, deques, ilhas |
| **Rasa** (canela) | −0,4 m | 85% | a maior parte da área molhada |
| **Funda** (peito) | −1,2 m | 55%, não corre | raias, piscinas, canais |
| **Poço** | −6 m | **nada**, cai devagar | fôlego (seção 4.2), escuro, o ralo |

**Escada ou borda.** Numa divisa aberta entre duas alturas, ou há escada (degraus de 20 x 30 cm, da
largura do vão, e aí se anda nos dois sentidos) ou é uma **borda** com capa: cai-se na água por ela,
mas não se sobe. A piscina afundada tem borda em volta e escada só em um ou dois lugares, como uma
piscina de verdade. Regra do gerador, no espírito de "buraco nunca isola": **toda célula com piso que
não é poço continua alcançável a pé** (mesma altura ou escada), e **de todo poço se sai nadando** — por
uma célula funda vizinha ou por uma escada que desce até 1,6 m abaixo da água, com corrimão.

O **poço redondo** é um furo de 3,2 m de diâmetro no fundo de uma área funda; o ralo mora no fundo
dele.

### 3.2 Salas

Sorteadas pela seed, giradas e espelhadas; nenhum tipo aparece mais de duas vezes, fora túnel e
galeria.

| Sala | Células | Teto | Água | Para que serve |
|---|---|---|---|---|
| Patamar de chegada | 3 x 3 | 3,6 | duas fileiras secas, uma rasa, escada larga | início e respiro |
| Salão de pilares | 4 x 4 a 6 x 6 | 3,6 ou 4,8 | raso; às vezes uma raia funda cortando | o espaço dominante; perde-se a direção |
| Átrio da claraboia | 3 x 3 | 12 | anel raso, ilha seca no meio | marco e ponto de encontro |
| Piscina com deque | 5 x 5 ou 6 x 5 | 4,8 | deque seco, fundo dentro, escadas | o lugar "normal", e por isso estranho |
| Canal | 3 x 4–6 | 3,6 | seco, fundo, seco | gargalo; vira nado na maré alta |
| Raia longa | 3 x 8–10 | 3,6 ou 4,8 | raso, fundo, raso, claraboia contínua | o eixo de vista de 30 a 40 m |
| Galeria / corredor de janelas | 1–2 x 4–8 | 3,6 | raso | frestas ou vigias acesas em série |
| Túnel em abóbada | 1 x 2–4 | abóbada | raso ou seco | compressão antes de um salão |
| Fenda | 1 x 3–5 | 8 ou 12 | raso | corredor estreito e altíssimo, luz no topo |
| Escadaria afogada | 2–4 x 3–4 | 8 ou 12 | seco, raso, fundo, poço | risco; os degraus somem na água |
| Sala do grande poço | 5 x 5 | 12 | anel raso, anel fundo, poço no centro | o maior marco, longe do início, escura |
| Cisterna | 4 x 4 | 3,6 | funda, pilares em todo vértice | escura; a variante "água negra" |
| Sala da saída | 2 x 2 | 3,6 | seca | a porta |

Composição: patamar perto de um canto; grande poço e saída do lado oposto; depois os salões; depois o
resto. Salas ocupam pelo menos 55% das células e corredores de uma célula de largura no máximo 15%.
Depois da árvore mínima de ligações entram mais 30% de ligações para formar laços. Pelo menos três
salas de teto alto em quadrantes diferentes. O caminho a pé do início à saída tem pelo menos 60% de
seco e raso e nunca mais de três células fundas seguidas.

Ainda sem lugar na grade (ficam para depois): corredor curvo em S, escada em caracol, mezanino, teto
inclinado, sala octogonal, tobogã e as casas de máquinas do objetivo.

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

- **Nenhuma luminária.** Claraboias e janelas de vidro fosco, brancas, estouradas, nunca mostrando o
  que há do outro lado.
- **Uma luz de caixa por claraboia** (raios paralelos, inclinados 28 graus, sempre para o mesmo lado):
  a mancha de sol cai deslocada no piso e sobe na parede, e as colunas fazem sombra comprida. Sombra
  em cache, uma face por luz (o Level 0 gasta seis por luminária).
- **A luz ambiente dá a meia-sombra esverdeada**; o perfil de Volume é próprio da fase (o do Level 0 é
  amarelo): céu creme com base ciano, névoa clara e leve, exposição fixa.
- **Poucos buracos escuros de propósito:** boca de túnel, sala do poço e cisterna (sem claraboia, só
  as lâmpadas de dentro d'água). É a receita "água negra espelhada".
- **Luzes dentro d'água** nas paredes das piscinas e dos poços, verde-azuladas, sem sombra.
- **Cáustica** somada sobre pisos e paredes submersos.
- **Anoitecer** (o apagão desta fase): o vidro das claraboias e janelas apaga pelo mesmo relógio do
  apagão do Level 0; sobram as luzes de dentro d'água e as lanternas.

---

## 8. Como construir

- **`PoolGenerator`** (C# puro, só `System.Random(seed)`): salas, maciços, divisas, profundidades,
  escadas, colunas, claraboias e lâmpadas, com `Validate` conferindo todas as regras e sorteando de
  novo até passar. Os dados ficam em `LevelData` (campos das Piscinas) e em `PoolData.cs`.
- **`LevelBuilder.Pools.cs`** (parte do `LevelBuilder`): pisos e tetos por célula, paredes grossas com
  arcos, vãos, vigias e janelas, abóbadas, colunas, sanca, escadas, bordas, poço redondo, claraboias
  com a luz, lâmpadas, água e a malha da cáustica. O que é curvo usa `MeshCollider` da mesma malha.
- **Água:** `HDRP/Lit` transparente com refração (a cor vem do fundo visto através dela), sem o Water
  System do HDRP. Física no `PlayerController`, pela altura do jogador em relação à superfície.
- **Montagem da cena** pelo `Level0Setup.SceneOptions`, com perfil de Volume próprio
  (`Level2Setup`), mais `LevelCatalog` (índice 2) e a lista de cenas da build.
- **Modelos** (pelo caminho do Blender, `modelos-blender.md`), para depois: escadinha de metal,
  cadeira de salva-vidas, grade de ralo, válvula e bombas, plaquinha de profundidade, boia esquecida.

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
