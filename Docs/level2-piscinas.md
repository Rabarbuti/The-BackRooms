# Level 2 — As Piscinas

Projeto da terceira fase. Escrito em 11/11/2026; espaço, luz e água refeitos em 12/11/2026, depois
que a primeira versão de teste (o labirinto do Level 0 com azulejo e água no chão) foi rejeitada.

**Já existe:** uma versão de teste jogável (`Scenes/Level2_Test.unity`, menu BackRooms > Montar
Level 2 (teste)) com as etapas 1 a 7 da seção 8.1:
- **Planta por salas** (`PoolGenerator`, C# puro, só `System.Random(seed)`): 16 x 16 células de 4 m,
  com patamar de chegada, salões de pilares, átrio da claraboia, piscina com deque, canal, raia longa,
  galeria, corredor de janelas, túnel em abóbada, fenda, escadaria afogada, salas de poço, cisterna
  e sala da saída (seção 3.2), maciços fechados entre elas (15 a 30% do mapa) e ligações com laços.
  `PoolGenerator.Validate` confere todas as regras da seção 3 e, se reprovar, sorteia de novo.
- **Profundidade por célula** (`LevelData.Depth`: seco +0,2 m, raso −0,4, fundo −1,2, poço −6, água em
  0) e **escadas explícitas** com largura (`LevelData.Stairs`); desnível sem escada é borda: desce-se,
  não se sobe (`LevelData.CanStep`). Tudo que não é poço continua alcançável a pé e de todo poço se
  sai nadando.
- **Construção** (`LevelBuilder.Pools.cs`): paredes de 0,4 m com arcos, vãos e vigias recortados sem
  moldura, colunas-tambor, sanca, abóbada de berço, teto claro, escadas, bordas com capa, corrimãos,
  poço redondo e água só nas células molhadas.
- **Luz:** nenhuma luminária. Claraboias com luz de caixa inclinada (o mesmo "sol" no mapa todo, sombra
  de uma face em cache), janelões, frestas, vigias acesas e tijolo de vidro com spot, lâmpadas dentro
  d'água sem sombra, salas escuras de propósito e o anoitecer no apagão (seção 7).
- **Visual:** perfil de Volume próprio (`Settings/Level2_Volume.asset`); água `HDRP/Lit` transparente
  com refração e cáustica animada (`PoolWater`); texturas das folhas A, B, C e D (seção 10), com
  poucas manchas e marcações pintadas postas pela seed como decalques.
- **Jogador:** anda mais devagar na água (85% na canela, 55% no peito, sem correr), nada no poço
  (Espaço sobe, Ctrl/C mergulha), tem 20 s de fôlego submerso e, quando o fôlego acaba, se afoga e
  volta ao início, como na queda no buraco. Debaixo d'água a imagem fica ciano.
- **Filmadora:** `WATER` e a barra `AIR` com a cabeça debaixo d'água.
- **Caneta:** não risca debaixo d'água.
- **Desempenho** (seed 1998, 1920 x 1080, Quadro T2000): mediana de 37 ms por quadro sem lanterna e
  40 ms com, contra ~106 ms da primeira versão; 45 luzes com sombra de uma face em cache.

**Falta:** a maré, o ralo e os túneis submersos, as casas de máquinas e a drenagem (o objetivo), o
som e as anomalias; do vocabulário de formas, corredor curvo em S, escada em caracol, mezanino, teto
inclinado, tobogã e claraboia em sala de abóbada; os modelos do Blender (escadinha de metal, grade
de ralo, válvula). No visual, a cáustica ilumina até o fundo do poço, que devia ser escuro (seções 4.1
e 7). Não foi conferida a planta entre host e cliente (dois editores) nem jogada à mão.

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

- **Tudo é azulejo branco pequeno** (uns 13 cm, como nas referências, que medem 11 a 17), piso,
  parede e teto. O teto é claro. Fundo das áreas fundas em pastilha verde-água pálida. Alturas e
  degraus andam de 20 em 20 cm.
- **Pé-direito de 3,6 m na maioria das salas** e de 4,8 m nos salões maiores, no patamar e na piscina;
  8 e 12 m só nos marcos (de três a quatro por mapa, em pelo menos três quadrantes). Túnel em abóbada
  com arranque a 2,4 m. O teto é sempre um destes: 3,6, 4,8, 8 ou 12 m.
- **Paredes de 0,4 m**, com a espessura aparecendo em todo vão. Passagens recortadas sem moldura:
  arco pleno (vão de 2,4 m), arco abatido largo (3,4 m) e vão reto sem porta (1,4 x 2,4 m). Entre duas
  salas há sempre parede; dentro de uma sala, nenhuma. Salões que se encostam ligam-se por pelo menos
  dois vãos. Vigia redonda (Ø 2 m) deixa ver a sala ao lado sem deixar passar.
- **Colunas-tambor** redondas e lisas nos vértices da grade (raio de 0,6 a 0,9 m), esbeltas (0,45 m)
  ou pilares quadrados em galeria, raia e cisterna. Nunca fecham passagem: ficam a pelo menos
  (raio + 0,6 m) das linhas de centro das células.
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

O **poço redondo** é um furo de 3,2 m de diâmetro no fundo de uma área funda, com uma grade de ralo
no fundo; o ralo (seção 5) mora ali. Só fica redondo o poço sem escada cujas vizinhas abertas são
todas fundas; os outros ocupam a célula inteira.

### 3.2 Salas

Sorteadas pela seed, giradas e espelhadas; nenhum tipo aparece mais de duas vezes, fora túnel, fenda
e galeria (fenda, na prática, uma por mapa). O tipo (`PoolRoomKind`) só informa: o construtor lê as
células, não o tipo. Os tamanhos maiores só entram em mapas de 20 ou 22 células de lado. Átrio,
escadaria e sala de poço de canto caem para 4,8 m quando o mapa já tem salas altas demais.

| Sala (`PoolRoomKind`) | Células | Teto | Água | Para que serve |
|---|---|---|---|---|
| Patamar de chegada (`Start`) | 3 x 3 | 4,8 | duas fileiras secas, uma rasa, escada de borda a borda em cada célula dela | início e respiro; claraboia redonda no meio |
| Salão de pilares (`Hall`) | 4 x 4 a 6 x 6 | 3,6 ou 4,8 | raso; às vezes uma raia funda cortando | o espaço dominante; perde-se a direção |
| Átrio da claraboia (`Atrium`) | 3 x 3 | 12 | anel raso, ilha seca no meio, 2 a 4 escadas | marco e ponto de encontro |
| Piscina com deque (`Basin`) | 5 x 5 ou 5 x 6 | 4,8 | deque seco, fundo dentro, 1 ou 2 escadas do mesmo lado; às vezes um poço | o lugar "normal", e por isso estranho |
| Canal (`Canal`) | 3 x 4–6 | 3,6 ou 4,8 | passeio seco (ou raso), fundo no meio | gargalo; vira nado na maré alta |
| Raia longa (`Lane`) | 3 x 8–10 | 3,6 ou 4,8 | raso, fundo, raso, claraboia contínua, colunas em fila | o eixo de vista de 30 a 40 m |
| Galeria (`Gallery`) | 2 x 4–6 | 3,6 | raso ou seco, pilares quadrados na linha do meio | corredor largo de arcos |
| Corredor de janelas (`WindowGallery`) | 1–2 x 4–8 | 3,6 | raso | frestas ou vigias acesas em série na parede de fora |
| Túnel em abóbada (`Tunnel`) | 1 x 2–4 | abóbada | raso ou seco | compressão antes de um salão; escuro por dentro |
| Fenda (`Slot`) | 1 x 3–5 | 8 ou 12 | raso ou seco | corredor estreito e altíssimo, rasgo de luz no topo |
| Escadaria afogada (`Terrace`) | 2–4 x 3–4 | 8 ou 12 | seco, raso, fundo, às vezes poço | risco; os degraus somem na água |
| Sala do grande poço (`WellRoom`) | 5 x 5 (6 x 6) | 12 | anel raso, anel fundo, poço no centro (1 ou 4 células) | o maior marco, longe do início; escura em metade das vezes |
| Sala de poço de canto (`WellRoom`) | 3 x 3 | 8 ou 4,8 | fileira rasa, duas fundas, um poço | beco de risco; escura em metade das vezes |
| Cisterna (`Cistern`) | 4 x 4 | 3,6 | funda com cantos rasos, pilares em todo vértice | sempre escura; a variante "água negra" |
| Sala da saída (`Exit`) | 2 x 2 ou 2 x 3 | 3,6 | seca | a porta, numa parede contra maciço; provisória até existir a drenagem (seção 6) |

Composição: patamar perto de um canto; grande poço e saída do lado oposto (a pelo menos 45% da
diagonal do mapa); depois os salões; depois o resto; galerias até sobrar o vazio desejado. Salas
ocupam pelo menos 55% das células, salões e salas de 3 ou mais células de largura pelo menos 60% do
piso, salas estreitas no máximo 40% das salas e corredores de uma célula de largura no máximo 15%
das células. Depois da árvore mínima de ligações entram mais 30% de ligações para formar laços;
túnel e fenda ligam exatamente duas salas e nunca dois seguidos; beco só na saída, nas salas de poço
e na cisterna. De 70 a 85% das divisas na borda das salas são parede cega. Orçamentos: 30 a 70
claraboias, 3 a 8 células de poço, até 24 lâmpadas e até 48 janelas. O caminho a pé do início à saída
tem pelo menos 60% de seco e raso e nunca mais de três células fundas seguidas.

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

- **Água própria, não o Water System do HDRP** (decisão 3, seção 9): uma lâmina `HDRP/Lit`
  transparente com refração, só sobre as células molhadas. A cor vem do fundo visto através dela (o
  alfa é baixo), não de uma película colorida.
- **Duas ondulações** rolando para lados diferentes (mapa de normais de 1,25 m e detalhe de 0,5 m) e
  uma malha de **cáustica** aditiva sobre pisos e paredes submersos, com duas redes de linhas;
  animadas pelo `PoolWater`.
- A água é azul-esverdeada e clara no raso; no poço o fundo deve sumir num azul-escuro quase preto
  (hoje a cáustica ainda o clareia; seção 7).
- Uma **ondulação constante** sem causa (wiki); mais forte perto dos ralos ainda falta.

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
  quem estava num patamar baixo fica ilhado. O seco (+0,2 m) também fica com 0,7 m de água: se ele
  continua sendo refúgio é a decisão 5 da seção 9.
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

As casas de máquinas entram como salas do `PoolGenerator`, a pelo menos 8 células uma da outra e
fora das salas de poço; a porta de saída, no fundo da sala do grande poço (já a mais distante do
início). Até lá, a saída é a sala seca da seção 3.2.

---

## 7. Luz

- **Nenhuma luminária.** Claraboias e janelas de vidro fosco, brancas, estouradas, nunca mostrando o
  que há do outro lado.
- **Uma luz de caixa por claraboia** (raios paralelos, inclinados 35 graus, rumo 35 graus, sempre para
  o mesmo lado; 700 lux, 4800 K, sol de fim de tarde): a mancha de sol cai deslocada no piso e sobe na
  parede, e as colunas fazem sombra comprida. Nas faixas de teto de vidro, uma luz por fileira de
  células. Sombra em cache, uma face por luz (o Level 0 gasta seis por luminária). Os fachos acendem
  a névoa.
- **Janelões, frestas, vigias acesas e tijolo de vidro** têm um spot com sombra entrando na sala, um
  pouco para baixo; até três aberturas vizinhas da mesma parede dividem um.
- **A luz ambiente dá a meia-sombra esverdeada**; o perfil de Volume é próprio da fase
  (`Settings/Level2_Volume.asset`; o do Level 0 é amarelo): céu em degradê creme em cima e verde-água
  embaixo, névoa clara e leve que quase só aparece nos fachos, exposição fixa (EV 5,5), sem o zumbido
  de lâmpada.
- **Poucos buracos escuros de propósito:** a cisterna (sempre) e as salas de poço (metade das vezes)
  são salas escuras, sem claraboia nem janela, só com as lâmpadas de dentro d'água; dentro delas a luz
  ambiente cai (Volume local do `PoolWater`) e uma névoa preta faz a porta parecer um buraco visto de
  fora. Os túneis têm a mesma névoa, mais fraca. É a receita "água negra espelhada".
- **Luzes dentro d'água** nas paredes das áreas fundas e dos poços, verde-azuladas, sem sombra.
- **Cáustica** somada sobre pisos e paredes submersos; some no anoitecer e cresce para quem está
  debaixo d'água. Hoje ela vai até o fundo do poço com a mesma força e o clareia; falta fazê-la
  sumir com a profundidade, para o poço ficar escuro.
- **Alcance:** as luzes das claraboias e das janelas deixam de iluminar a 45 m da câmera e a sombra
  vai 20% além, até 54 m (`lightFadeDistance`, a regra do Level 0: luz sem sombra atravessaria as
  paredes); as lâmpadas submersas, sem sombra, somem a 60 m.
- **Anoitecer** (o apagão desta fase): pelo mesmo relógio do apagão do Level 0, o vidro das claraboias
  fica azul-noite, o das janelas apaga e o céu vira azul-marinho; sobram as luzes de dentro d'água e
  as lanternas.

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
- **Conferência e fotos:** `Level2Setup.BatchCheck` (5 seeds), `Level2PlayTest.BatchRun` (em Play),
  `Level2Preview.BatchCapture` (planta, salas e closes, `Logs/Previews/level2_*.png`) e
  `Level2Preview.BatchCloseups` (`-seed N`: só os closes do acabamento, como tijolo de vidro, janelão,
  poço redondo, teto de vidro, manchas e marcações, em `Logs/Previews/level2_s{seed}_*.png`, sem apagar
  as outras fotos).
- **Modelos** (pelo caminho do Blender, `modelos-blender.md`), para depois: escadinha de metal,
  cadeira de salva-vidas, grade de ralo, válvula e bombas, plaquinha de profundidade, boia esquecida.

### 8.1 Ordem

| # | Etapa | Estado | Depende de |
|---|---|---|---|
| 1 | Texturas (seção 10) recortadas e integradas | feito (folhas A a D) | — |
| 2 | Gerador por salas (`PoolGenerator`, `Validate`, `Hash`, mapa em texto) | feito | — |
| 3 | Construtor: paredes grossas, arcos, vigias, colunas, abóbadas, escadas, bordas, poço redondo; prévia em imagem | feito | 1, 2 |
| 4 | Água própria (refração, ondulação, cáustica, debaixo d'água) e medida de desempenho | feito | 3 |
| 5 | Física da água: lentidão, nado, fôlego, filmadora molhada | feito | 4 |
| 6 | Cena `Level2_Test`, `LevelCatalog`, testes (`Level2Setup.BatchCheck`, em Play) | feito | 3 |
| 7 | Luz: claraboias, janelas, salas escuras, lâmpadas, anoitecer, perfil de Volume | feito | 3 |
| 8 | Planta igual em host e cliente (dois editores) | falta | 6 |
| 9 | Decisão do refúgio na maré (seção 9, decisão 5) e maré sincronizada | falta | 5 |
| 10 | Ralo e túneis submersos | falta | 5 |
| 11 | Casas de máquinas, válvulas, drenagem e porta do fundo | falta | 9 |
| 12 | Som (abafado, eco por sala) | falta | 5 |
| 13 | Formas que faltam: corredor curvo em S, escada em caracol, mezanino, teto inclinado, tobogã, claraboia em abóbada | falta | 3 |
| 14 | Modelos do Blender (escadinha de metal, grade de ralo, válvula) | falta | — |
| 15 | Anomalias (seção 3.3) | falta | 3 |

8, 13 e 14 andam em paralelo com o resto.

---

## 9. Decisões em aberto

1. **Nome.** "Level 2 — As Piscinas" (nossa ordem) ou "Level 37" como na wiki?
2. **Inimigo de verdade?** A proposta é **nenhum** — o ralo e a maré bastam, e a calma é o que
   assusta depois de dois níveis de monstro. Alternativa: algo que só aparece como sombra embaixo
   d'água e segue quem espirra.
3. ~~HDRP Water ou água própria?~~ **Decidido em 12/11/2026: água própria** (`HDRP/Lit`
   transparente com refração, cáustica numa malha à parte). Dá a cor certa pelo fundo visto através
   dela e custa pouco: na seed 1998, tirar a refração poupa 3,3 ms por quadro e a cáustica, 2,4.
4. **Voz abafada** só vale quando existir voz por proximidade (Vivox, etapa 5 do plano).
5. **A maré deixa um refúgio seco?** O seco fica a +0,2 m e a maré sobe 0,9 m: na maré alta ele fica
   com 0,7 m de água e deixa de ser refúgio. Saídas (da pesquisa de level design): (a) uma altura nova,
   "alto" a +1,2 m, para patamares-refúgio, só encostando em seco e com escada (regras novas no
   `Validate`); (b) maré por região, com fases defasadas, para sempre haver uma região vizinha baixa.
   Nos dois casos, todo ponto do caminho a pé a no máximo três células de um refúgio; e canal, raia e
   cisterna, que viram nado, precisam de um laço mais longo e mais seco quando estão no caminho.

---

## 10. Texturas (Higgsfield)

Todas em **folhas 2 x 2** (deu certo no Level 0): uma imagem 2K, um material por quarto, recortado
e emendado depois em Python, com a cor média conferida contra a cena. O azulejo e a pastilha têm o
número exato de peças por quarto para o rejunte ser recortado no período certo.

As quatro folhas já foram geradas e recortadas. Cada quarto fica em `Art/Fontes/Level2/<nome>.png`;
o `Level2Setup` lê dali e grava o que deriva (recolorido, máscaras, materiais) em `Art/Level2`.

| Folha | Quarto | Arquivo | O que virou |
|---|---|---|---|
| A — superfícies | sup. esq. | `Tile_White` (recortado em 6 x 6 azulejos) | piso, parede e teto; deriva `Tile_Albedo` (rejunte mais escuro), `Tile_Height`, `Tile_Mask` e `Tile_Accent` (faixa azul e parede do poço). Posto a 0,8 m por repetição: azulejo de uns 13 cm |
| | sup. dir. | `Mosaic_Pool` (28 x 28 pastilhas) | fundo das áreas fundas e dos poços, como `Mosaic_Pale` (mais pálida e mais verde: quem tinge é a água), a 0,6 m |
| | inf. esq. | `Tile_White_Worn` | **sem uso**: as referências não têm sujeira (`poolWornTileMaterial` vazio) |
| | inf. dir. | `Coping` | capa arredondada da borda |
| B — água e luz | sup. esq. | `Water_Height` | mapa de normais da ondulação grande (1,25 m) |
| | sup. dir. | `Caustics` | a rede principal da cáustica (2 m) |
| | inf. esq. | `Window_Frosted` | vidro aceso de janelões, frestas e vigias acesas |
| | inf. dir. | `Glass_Block` | parede de tijolo de vidro; o brilho (`Glass_Block_Glow`) sai dela, só no vidro |
| C — manchas (fundo transparente) | sup. esq. | `Stain_Waterline` | limo na linha d'água |
| | sup. dir. | `Stain_Limescale` | escorrido de calcário do alto da parede |
| | inf. esq. | `Stain_GroutMold` | mofo no rejunte e no teto das salas sem claraboia |
| | inf. dir. | `Stain_Rust` | ferrugem embaixo das lâmpadas submersas |
| D — marcações (fundo transparente) | sup. esq. | `Mark_Depth` "1.2 M" | no deque junto à área funda ou na parede dela |
| | sup. dir. | `Mark_NoDiving` | em parede de sala com água rasa |
| | inf. esq. | `Mark_DeepEnd` | em parede de sala com poço |
| | inf. dir. | `Mark_LaneEnd` | o T no fundo, nas pontas das raias |

**Manchas** (folha C): decalques `HDRP/Decal` (`Art/Level2/Decal_Stain_*.mat`, campo
`poolStainDecals`), postos por `LevelBuilder.BuildPoolStains`, chamado no fim de `BuildPools`, só com
`System.Random` da seed. No máximo uma a cada 10 células com piso e 10 no mapa (`CellsPerStain`,
`MaxStains`); só em célula com desgaste (`LevelGenerator.WearAt`) de 0,5 ou mais, nunca na sala
inicial nem na da saída. Primeiro a ferrugem, até 3 (`MaxRust`), escorrendo de baixo de uma lâmpada
submersa até no máximo o piso; depois, em rodízio, o limo na linha d'água (parede de célula molhada
que não é poço), o calcário colado no começo da sanca (salas com claraboia, fora das abóbadas) e o
mofo sobre o rejunte, na parede ou no teto, só onde não há sol (sala escura ou célula sem
claraboia). Parede só cheia (nada sobre arco, vão, vigia, janela ou a porta de saída), uma por face,
e nenhuma encosta em escada, coluna, lâmpada ou outro decalque.

**Marcações** (folha D): decalques também (`Art/Level2/Decal_Mark_*.mat`, campo `poolMarkDecals`, na
ordem 0 "1.2 M", 1 NO DIVING, 2 DEEP END, 3 T de fim de raia), postos por `BuildPoolMarks` depois das
manchas e desviando delas e das escadas, colunas e lâmpadas:
- **"1.2 M"**, 1 ou 2 por piscina, raia ou canal (no máximo 6 no mapa): no piso do deque seco, com o
  pé do desenho a 0,35 m da borda de uma célula funda aberta para ele (nunca de poço redondo), ou na
  parede cheia de uma célula funda, logo acima da água (e da faixa azul, se a sala tem).
- **NO DIVING**, no máximo 4: em parede cheia de célula seca ou rasa, a 1,6 m, em patamar, escadaria,
  salão ou piscina que tenha água rasa junto de deque ou escada (no patamar de chegada sempre que
  couber; nos outros, 70% das vezes). É o único decalque que pode cair na sala inicial.
- **DEEP END**: em toda sala que tem célula de poço, numa parede cheia a 1,5 m, sorteada entre as três
  mais perto do poço.
- **T de raia**, no fundo das áreas fundas: na fileira funda mais longa de cada piscina, raia ou canal,
  um em cada ponta, a 0,6 m da borda (até 2,4 m, se a escada ocupa a ponta).

Na seed 1998 são 10 manchas e 14 marcações; na seed 7, 10 e 15.

**Ainda pintado em código** no `Level2Setup` (em `Art/Level2`): a segunda ondulação (`Water_Detail`,
0,5 m), a segunda rede da cáustica (`Caustics_Detail`) e o vidro das claraboias (`Skylight_Glow`, aceso
e azul-noite). Se `Water_Height`, `Caustics`, `Window_Frosted` ou `Glass_Block` sumirem da pasta de
fontes, o gerador volta a pintá-los.

As folhas C e D precisam de **fundo transparente**, que só o GPT Image faz. Os prompts ficam em
`Docs/level2-prompts.md`, para gerar de novo.
