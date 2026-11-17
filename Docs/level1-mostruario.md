# Level 1 — O Mostruário

Projeto da segunda fase. Escrito em 11/11/2026.

**Já existe:** uma versão de teste jogável (`Scenes/Level1_Test.unity`, menu BackRooms > Montar
Level 1 (teste)): a planta do Level 0 em 20 x 20 células, com pé-direito de 3,4 m, sem buracos nem nichos,
mobiliada pela `PropLayer` com os 65 modelos do Blender, incluindo a sala dos sofás e placas
de trânsito (PARE e STOP). No lugar das setas pintadas do Level 0, placas amarelas de curva
(losango com a seta dobrada) pregadas na parede apontam o caminho até a saída. A porta do Level 0
leva até aqui. **O Capitão está na fase** (desde 17/11/2026, `CaptainBrain`): aparece quando alguém
chega a 5 células da sala inicial, ouve, congela quando é visto e está iluminado, cansa quem o
encara, anda em arrancadas no apagão e pega como a entidade (seção 5.4, regras 1 a 4 e 6).
Também já existem (17/11/2026): totens com troca no apagão, guarda-roupa e mesa de esconder,
plástico-bolha, barricadas, o objetivo (teclado, etiquetas vermelhas e bilhete), a música da loja, o
jingle do Capitão e o piso por zona (laminado e vinílico) com passos próprios.
As zonas da seção 3.1 existem todas (lavanderia, festa, verão, quarto, escritório e cubículos
com os lotes de 17/11/2026; o sorteio é um carro num beco sem saída), com placas penduradas com o
nome da zona e anomalias longe do início (mesa no teto, móvel atravessando a parede, guarda-roupa
de frente para a parede, batente solto). A mobília sai igual no host e no cliente (conferido em dois
editores).
**Falta:** jogar à mão e ajustar. A saída continua a porta corta-fogo com EXIT, não a porta da
doca da seção 6 (decidido em 17/11/2026). O teto a 3,4 m não muda a iluminação das luminárias.

---

## 1. A ideia em uma frase

**As mesmas salas amarelas do Level 0, mas alguém mobiliou tudo — errado.** É o depósito e o
salão de exposição de uma loja de móveis que o Backrooms copiou de memória: conjuntos de sala
de jantar, escritório e quarto montados no meio do labirinto, móveis atravessando paredes,
cadeiras empilhadas até o teto e, em toda parte, o pirata da placa da loja.

Quem sai pela porta EXIT do Level 0 cai aqui, nos fundos da loja.

### O que muda em relação ao Level 0

| | Level 0 | Level 1 — O Mostruário |
|---|---|---|
| Espaço | Vazio, repetitivo | Mesma planta, **cheio de objetos** que tapam a visão |
| Perigo do chão | Buracos com faixas | **Barricadas** de móveis e plástico-bolha que estala |
| Esconderijo | Nicho na parede | **Guarda-roupas** e debaixo das mesas (agachado) |
| Inimigo | Ouve passos | Ouve passos **e só se mexe quando ninguém o vê** |
| Engano | — | **Totens** do pirata da loja espalhados, iguais a ele de longe |
| Apagão | Assusta | É quando ele **anda livre** |

Cada mudança reforça uma das quatro forças do plano (`plano-coop.md`, seção 1): os objetos
tapam a visão e forçam o grupo a **ficar junto** para vigiar; o objetivo exige se **separar**;
os totens obrigam a **falar** ("é o de verdade?"); o plástico-bolha e o inimigo que ouve pedem
para **ficar calado**.

---

## 2. Referências

### 2.1 O filme *Backrooms* (2026)

A loja de móveis é o ponto de partida do filme: a passagem para o Backrooms aparece no subsolo
de um showroom, as salas amarelas têm "móveis deformados", a direção de arte fala em pisos
desalinhados, arcos que se cruzam e **objetos atravessando paredes**, e o final acontece numa
cópia defeituosa da loja. O antagonista é uma cópia do dono da loja vestida com a fantasia de
pirata do mascote, alto demais, de perna de pau, que não fala.

Daqui tiramos o **arquétipo** — o dono da loja copiado na fantasia de pirata, alto demais,
perna de pau, mudo, loja copiada errado. A aparência segue de perto as imagens do filme
(seção 5.2); o nome e a história da loja são nossos. Ver a decisão em aberto 4 (seção 9).

Fontes: [Wikipedia — Backrooms (film)](https://en.wikipedia.org/wiki/Backrooms_(film)),
[ScreenRant — Pirate Clark explained](https://screenrant.com/backrooms-movie-pirate-clark-explained/).

### 2.2 MECCHA CHAMELEON — a fase "Backroom"

O jogo é de Unreal; os mapas estão empacotados, então o que segue foi lido da lista de arquivos
instalados (`Manifest_UFSFiles_Win64.txt`), não da fase aberta. Mesmo assim mostra bem o método:

- **Paredes modulares** (`stage/Backroom/walls/Back_room1` a `Back_room6`) com um material de
  papel de parede próprio. A fase é montada com seis peças de parede repetidas.
- **Objetos numa camada à parte.** A fase existe como uma *Data Layer* (`DL_BackRoom`) por cima
  do mapa principal: os objetos são ligados como um conjunto, separados da arquitetura.
- **Objetos agrupados por tema**, cada tema numa pasta (`stage/small/backroom/...`):
  - `officeset` — gaveteiros (`Cabinet1`, `Cabinet2`), computador (`PC`), teclado, cadeiras.
  - `Woodenfurniture` — cadeira de madeira, dois baús (`Chest1`, `Chest2`) e um conjunto
    fundido numa malha só (`Combined_...`).
  - `sign` — cinco placas.
  - `poolroom` — espreguiçadeira (`Summer_Bed`), bola de praia, maiôs, cerca e luzes de
    piscina e sete peças de escorregador.
  - `car` — **uma viatura de polícia inteira** dentro das salas amarelas.
- O resto do jogo usa as mesmas famílias em outras fases: estantes de depósito, caixas de
  papelão, lixeiras, cabos, extintor, balde, roupa lavada, luminárias fluorescentes, pôsteres,
  quadros, sofás, malas, tapetes.

**O que as imagens da fase mostram** (capturas enviadas em 11/11/2026):

- **Pilha jogada:** cadeiras de madeira tombadas umas sobre as outras, cabideiros, uma poltrona,
  um estrado de cama em pé, mesinhas e vasos, tudo girado ao acaso, como entulho de mudança.
- **Revestimento:** um pilar com placas de trânsito (PARE, proibido estacionar, 30 km/h,
  triângulos de perigo) cobrindo todas as faces, e outras placas caídas pelo chão.
- **Fileira:** máquinas de lavar vermelhas lado a lado ao longo de uma parede, como lavanderia.
- **Conjunto arrumado:** mesas de jantar com cadeiras sob uma luz forte; sala de estar com
  poltronas vermelha e azul, sofá, cômoda, vaso alto e um palete; festa com bandeirinhas no
  vão da porta, cadeiras dobráveis e mesa de bolos.
- **Objeto sozinho e fora de lugar:** a viatura parada no fim do corredor, um vaso azul sobre a
  mureta, garrafas grandes demais encostadas na parede, um balcão de recepção baixo.
- **Cubículos:** vista de cima, uma grade de salas quadradas de paredes altas, cada uma com um
  computador, um gaveteiro ou uma mesa — um escritório em labirinto.
- **O meio do corredor fica quase sempre livre.** A bagunça se encosta em parede, canto e
  pilar; o caminho principal continua legível. Piso claro com manchas, como o nosso.

**O que levamos para o nosso jogo:**

1. Arquitetura e objetos separados. No nosso caso: `LevelBuilder` continua fazendo paredes, e
   uma **camada de objetos** nova lê a mesma planta e mobília por cima.
2. Objetos em **conjuntos temáticos**, não soltos um a um. Um conjunto é uma "vitrine": mesa +
   cadeiras + luminária + placa de preço.
3. Conjuntos **fundidos numa malha só** depois de montados (como o `Combined_` deles e o nosso
   `FigureKit.Bake`), para mapas com centenas de objetos não pesarem.
4. **Um objeto absurdo e enorme** de vez em quando (a viatura deles). O nosso é o carro do
   sorteio da loja, sozinho numa sala.
5. **Um canto de outro lugar** (a piscina deles). O nosso é a vitrine de verão: espreguiçadeiras,
   guarda-sol e bola de praia sobre o carpete molhado.
6. **Cinco jeitos de pôr objetos**, não um: pilha jogada, revestimento, fileira, conjunto
   arrumado e objeto sozinho (seção 4.1). A mistura é o que dá cara de "lugar copiado errado".
7. **Objetos de outros lugares no meio da loja:** placas de trânsito, máquinas de lavar, festa.
   Viram zonas próprias (seção 3.1).

---

## 3. O espaço

- **Mesma grade do Level 0:** 20 x 20 células de 4 m, mesmo `LevelGenerator` com outras regras
  (sem buracos nem nichos; mais salas grandes, porque vitrine pede espaço).
- **Pé-direito 3,4 m** em vez de 4,05: o teto mais baixo deixa os móveis mais opressivos.
- **Placas penduradas** do teto, de papelão: "OFERTA", "LIQUIDAÇÃO", "SALA DE JANTAR",
  "ESCRITÓRIO", "QUARTO", "VERÃO", "PROIBIDO ENTRAR" — algumas com o rosto do pirata.
- **Piso por zona:** carpete amarelo nos corredores, laminado de madeira debaixo das vitrines de
  sala e quarto, piso vinílico cinza no estoque. Ajuda a se orientar sem mapa.
- **Sala inicial = fundos da loja:** estoque limpo, prateleiras com caixas, a porta por onde se
  entrou trancada atrás. A entidade não entra (mesma regra do Level 0).

### 3.1 Zonas

Cada região fechada por paredes recebe um tema, sorteado pela seed com peso:

| Zona | Peso | O que tem | Para que serve no jogo |
|---|---|---|---|
| Sala de jantar | 3 | Mesa comprida, 6–8 cadeiras, aparador, lustre | Esconder debaixo da mesa |
| Sala de estar | 3 | Sofás, poltronas, mesa de centro, estante, TV de tubo | Bloqueia visão em altura de cintura |
| Quarto | 2 | Cama, criado-mudo, **guarda-roupa**, tapete | Guarda-roupa = esconderijo |
| Escritório | 2 | Mesas, cadeiras giratórias, gaveteiros, computador, teclado | Corredores estreitos entre mesas |
| Estoque | 3 | Estantes de aço, caixas, carrinho, **plástico-bolha** | Barulho no chão; caixas tapam visão |
| Cubículos | 1,5 | Uma divisória alta por célula, com mesa, computador e gaveteiro | Labirinto dentro do labirinto; visão curta |
| Lavanderia | 1 | Fileira de máquinas de lavar contra a parede, cesto, roupa no chão | Máquinas tapam a visão; uma liga sozinha e faz barulho |
| Festa | 0,7 | Bandeirinhas no vão, cadeiras dobráveis, mesa de bolos, balões | Balão estoura se encostado: barulho |
| Verão | 0,5 | Espreguiçadeiras, guarda-sol, bola de praia, cerca, poça | Estranhamento; sem cobertura |
| Sorteio | 0,3 | Um carro inteiro, faixa "CONCORRA", cordas de fila | Marco de orientação |
| Sala dos sofás | 0,5 | Sala entupida de sofás e poltronas marrons listrados, de parede a parede, empilhados, de pé e tombados (cena do filme) | Só se passa por cima ou espremido; o Capitão some entre eles |
| Vazia | 2 | Nada — como o Level 0 | Respiro e contraste |

As duas últimas aparecem no máximo uma vez por mapa.

### 3.2 Catálogo de objetos (primeira leva)

Todos feitos em código, de caixas e cilindros (móvel é forma reta; aqui o `MeshBuilder` basta,
sem esculpir). Medidas reais, para o jogador reconhecer de longe.

| Objeto | Tamanho (L x A x P) | Junto à parede? | Colisor | Bloqueia visão |
|---|---|---|---|---|
| Cadeira de madeira | 0,45 x 0,9 x 0,5 | não | sim | não |
| Mesa de jantar | 2,0 x 0,76 x 0,9 | não | sim (agachar embaixo) | não |
| Aparador | 1,6 x 0,85 x 0,45 | sim | sim | não |
| Sofá | 2,1 x 0,85 x 0,9 | não | sim | parcial |
| Poltrona | 0,9 x 0,9 x 0,85 | não | sim | parcial |
| Estante | 1,0 x 2,0 x 0,35 | sim | sim | **sim** |
| TV de tubo no rack | 0,7 x 1,1 x 0,5 | sim | sim | não |
| Cama de casal | 1,4 x 0,55 x 2,0 | sim (cabeceira) | sim | não |
| Guarda-roupa | 1,2 x 2,1 x 0,6 | sim | sim, com porta | **sim** |
| Mesa de escritório | 1,4 x 0,75 x 0,7 | não | sim | não |
| Cadeira giratória | 0,6 x 1,0 x 0,6 | não | sim | não |
| Gaveteiro | 0,4 x 0,7 x 0,5 | não | sim | não |
| Estante de aço | 1,8 x 2,2 x 0,5 | sim | sim | **sim** |
| Pilha de caixas | 1,0 x 0,4–1,8 x 0,8 | não | sim | conforme a altura |
| Espreguiçadeira | 0,6 x 0,4 x 1,9 | não | sim | não |
| Guarda-sol | Ø 2,0 x 2,2 | não | só o mastro | não |
| Carro | 4,3 x 1,45 x 1,8 | não | sim | **sim** |
| Totem do pirata | 0,9 x 2,8 x 0,6 | não | sim | sim |
| Placa de preço | 0,6 x 0,4 | sobre os móveis | não | não |
| Plástico-bolha | 1,5 x 0,02 x 1,0 | chão | não (gatilho) | não |
| Cabideiro | 0,5 x 1,8 x 0,5 | não | só o mastro | não |
| Palete de madeira | 1,2 x 0,15 x 0,8 | não | sim | não |
| Cômoda | 0,9 x 0,8 x 0,45 | sim | sim | não |
| Vaso alto | Ø 0,35 x 0,9 | não | sim | não |
| Balcão de recepção | 2,4 x 1,05 x 0,6 | não | sim | parcial |
| Máquina de lavar | 0,6 x 0,85 x 0,6 | sim (em fileira) | sim | não |
| Placa de trânsito | 0,6 x 0,6 (ou com poste 2,2) | em pilar ou caída | só com poste | não |
| Mesa de bolos e cadeira dobrável | 1,6 x 0,75 x 0,8 / 0,45 x 0,8 x 0,45 | não | sim | não |
| Bandeirinhas | largura do vão | no alto do vão | não | não |
| Divisória de cubículo | 4,0 x 1,9 x 0,08 | contorno da célula | sim | **sim** |

---

## 4. Como os objetos são postos (a camada de objetos)

O equivalente nosso à Data Layer do MECCHA CHAMELEON: um componente `PropLayer` que roda depois
do `LevelBuilder`, lê o mesmo `LevelData` e mobília. Regras:

1. **Determinístico.** Só `System.Random(seed ^ 0x4D4F5645)` ("MOVE"). Nada de
   `UnityEngine.Random`, tempo ou ordem de dicionário: todas as máquinas mobíliam igual, e o
   teste de fumaça compara um hash dos objetos entre host e cliente, como já faz com o mapa.
2. **Sub-grade de 0,5 m.** Cada célula de 4 m vira 8 x 8 casas. Antes de pôr qualquer coisa,
   reserva-se uma **cruz livre de 1,0 m de largura** ligando o centro da célula a cada lado
   aberto. Assim o `LevelNav` continua funcionando célula a célula, o jogador (0,6 m) sempre
   passa e a entidade nunca fica presa num sofá.
3. **Conjunto antes de objeto.** Primeiro escolhe-se a "vitrine" (mesa + cadeiras + lustre +
   placa) e a sua âncora; depois os objetos em volta dela, girados para a âncora. Objeto alto
   (estante, guarda-roupa, estante de aço) só em casa encostada em parede, de costas para ela.
4. **Desgaste pela distância** (`LevelGenerator.WearAt`, o mesmo do Level 0). Perto do início,
   vitrines arrumadas. Longe, as **anomalias**:
   - móvel atravessando a parede pela metade (o colisor é cortado na face da parede);
   - cadeiras empilhadas até o teto;
   - mesa posta de cabeça para baixo **no teto**;
   - batente de porta solto no meio da sala, sem parede;
   - o mesmo conjunto repetido em três salas seguidas, idêntico até a cadeira torta;
   - cama no corredor, guarda-roupa de frente para a parede.
5. **Barricadas.** Algumas células ficam entupidas de móveis do chão ao teto e viram "sem piso"
   para o `LevelNav`, como os buracos do Level 0. Valem as mesmas regras: nunca isolam parte do
   mapa (`SafeDetour`) e sempre há caminho até a saída.
6. **Colisor e malha nascem juntos**, como no `LevelBuilder`: cada objeto cria a caixa de
   colisão no mesmo passo em que cria a malha.
7. **Fundir por célula.** Terminada a célula, as malhas são fundidas por material (o
   `FigureKit.Bake` já faz isso para figuras). Meta: até 1500 objetos no mapa, menos de 400
   malhas desenhadas. Só objetos com mais de 1,2 m projetam sombra.
### 4.1 Os cinco jeitos de pôr objetos

Tirados das imagens do MECCHA CHAMELEON. Cada zona usa uma mistura, e o desgaste puxa a
mistura para os jeitos mais caóticos.

| Jeito | Como o código põe | Onde | Peso perto do início → longe |
|---|---|---|---|
| **Conjunto arrumado** | Âncora (mesa, sofá, cama) + objetos em volta, virados para ela, alinhados à grade | Meio de sala grande, fora da cruz livre | 70% → 20% |
| **Fileira** | O mesmo objeto repetido lado a lado, de costas para a parede, com 0–2 faltando | Parede comprida e lisa | 15% → 15% |
| **Pilha jogada** | 4–12 objetos pequenos e médios, cada um com giro e tombo ao acaso, empilhados por altura | Canto de sala e beco sem saída | 5% → 40% |
| **Revestimento** | Placas planas coladas nas faces de um pilar ou numa parede, sobrepostas, algumas no chão | Pilares e pontas de parede | 5% → 15% |
| **Objeto sozinho** | Um objeto grande ou fora de contexto, sozinho, alinhado ao corredor | Fim de corredor reto, para ser visto de longe | 5% → 10% |

A pilha jogada é a única com giro livre; para não depender de física (que não é
determinística entre máquinas), cada objeto da pilha é posto por conta: sorteia-se o giro,
calcula-se a altura do que já está embaixo naquela casa e assenta-se por cima. O colisor da
pilha é uma caixa só, do tamanho dela.

8. **Nada trafega pela rede.** Os objetos são estáticos e saem da seed. O que muda durante a
   partida (porta de guarda-roupa, plástico-bolha estourado) é estado pequeno, enviado a quem
   entra depois como faz `DrawingBoard.SendAll`.

---

## 5. O inimigo: O Capitão

### 5.1 Quem é

A loja copiada pelo Backrooms se chamava **Móveis Ancoradouro**. O dono gravava os comerciais
vestido de pirata — o **Capitão Ancoradouro** —, e a mesma figura, pintada em fibra de vidro,
ficava na porta da loja com uma placa de "OFERTA".

O Backrooms copiou o dono **com a fantasia**, e não sabe onde termina uma e começa o outro. O
Capitão não tira a roupa, não fala e não pisca: é a lembrança de um comercial, esticada até
caber no corredor.

### 5.2 Aparência (esculpida no Blender: `Tools/Blender/personagens/capitao.py`)

Desenhado a partir das imagens de referência do filme enviadas em 11/11/2026: homem alto e
magro em fantasia de pirata gasta, de rosto assustado. Em 12/11/2026, a pedido, ficou mais
assustador e menos "brinquedo": cores apagadas e encardidas, pele acinzentada, órbitas fundas e
escuras, olhos amarelados e injetados, careta larga de dentes podres, sangue seco do nariz e da
boca, casaco rasgado e furado, mãos em garra; parado, estica os braços para a frente.

- **2,6 m até o alto da cabeça, 2,8 m com o chapéu.** Magro, de pescoço comprido, braços
  afastados do corpo e duros.
- **Rosto humano esculpido:** pele escura, magra, cheia de **vincos fundos** (testa, olheiras,
  bochechas, entre as sobrancelhas). Nariz comprido. **Olhos arregalados**, o branco todo à
  mostra, íris pequena e pupila menor ainda, encarando quem está na frente. Sobrancelhas
  erguidas, cara de espanto que não passa. **Boca aberta** com os dentes de cima à mostra.
- **Rosto torto e machucado:** o olho esquerdo é **caído** — mais baixo, meio coberto pela
  pálpebra inchada, olhando para fora —, com **hematoma roxo** em volta e a maçã do rosto
  inchada. Sobrancelha esquerda baixa e quebrada, a direita bem erguida. **Nariz quebrado**, com
  a ponta torta e um corte na ponte. **Corte na testa** com sangue seco escorrido, três
  arranhões na bochecha direita, hematomas na têmpora e no queixo, **lábio partido**.
- **Barba preta curta**, cheia no queixo e no maxilar, subindo pelas costeletas, com bigode.
  Cabelo curto preto por baixo do chapéu.
- **Chapéu de três bicos** de feltro marrom gasto, com uma fita branca do lado: aba puída e
  ondulada, caída atrás, um **rasgo em V** na frente, a copa **amassada** e torta, um remendo de
  pano escuro e um furo.
- **Casaco comprido sem mangas, cor de areia**, aberto embaixo, com vista de botões de latão e
  uma **mancha de sangue** escorrendo da gola pela frente.
- **Camisa verde-menta** de mangas largas e gola pontuda.
- **Faixa lilás** amarrada na cintura, com as pontas penduradas até o joelho, e **cinto preto
  largo** de fivela de latão.
- **Mãos nuas**, de dedos compridos, abertos e esticados.
- Calça escura, **bota marrom na direita e perna de pau na esquerda**.

Por que funciona com o filtro VHS: o casaco claro e as mangas verdes destacam a silhueta
comprida no amarelo das salas; de perto, o que fica é o **branco dos olhos** e a boca aberta.

### 5.3 Como se mexe

- **Manca.** A perna de pau finca dura; o corpo dá um tranco para o lado dela a cada passo.
  Isso vira som: **"TOC… arrasta. TOC… arrasta."** — o aviso dele, como o ronco do Level 0.
- **Parado, fica duro:** em pé, um pouco torto para o lado da perna de pau, braços abertos e
  dedos esticados, cabeça inclinada, olhando fixo — como quem posa para um comercial.
- **Correndo,** curva o tronco e leva os braços para a frente, para agarrar; a boca escancara.
- **Não fala.** Perto, ouve-se o **jingle da loja** saindo de lugar nenhum, desafinado. Quanto
  mais perto, mais rápido e mais errado.

### 5.4 Regras

Todas as regras estão feitas (`CaptainBrain`, que herda do `EntityBrain`; testes em
`Level1CaptainTest` e `Level1ShelterTest`). Da regra 5, os "olhos seguem" são a cabeça dele, que
vira devagar para o jogador mais perto (até 40°, a até 10 m, mesmo congelado; os olhos fazem
parte da malha do rosto), e a perna de pau marca o carpete com decalques redondos a cada passo
dela (`PegMarks`, as 60 mais novas ficam). O totem não vira a cabeça nem anda. Os números ficam no prefab (`CaptainPreview.CreatePrefab`) e no `CaptainBrain`.

1. **Ouve como a entidade do Level 0** — mesmo cálculo de barulho do `EntityBrain`, com o
   plástico-bolha valendo como corrida (quando existir). Também vê como ela: 22 m à frente, olho a
   2,45 m.
2. **Não se mexe enquanto é visto.** Se algum jogador o tem no campo de visão **e ele está
   iluminado** (luminária acesa ou lanterna), ele congela, duro. Pode estar no meio de um passo.
   *Como está feito:* o host confere, a cada 0,1 s, a cabeça, o peito e o joelho dele na tela de
   cada jogador (16:9, até 30 m, sem parede nem móvel no meio) e a luz no peito: uma luminária
   acesa a até 6,5 m (fora do apagão) ou o facho de uma lanterna. Congelado, não anda, não vira e
   não pega ninguém; o corpo segura a pose do instante, inclusive a respiração (`CaptainBody`
   lê o estado, que vai pela rede).
3. **Mas não dá para vigiar para sempre.** Encarar o Capitão pela filmadora por mais de 6 s
   enche a tela de chuvisco e derruba a sanidade (quando existir). Alguém precisa piscar —
   e é nessa hora que o grupo tem de combinar quem olha.
   *Como está feito* (decisão D2 de `plano-de-acao.md`, sem sanidade): o tempo encarando conta
   enquanto ele está à vista e iluminado e cai 1,5 s por segundo olhando para outro lado. Dos 6 s
   aos 9 s o chuvisco cresce até tapar a imagem; a partir dos 9 s aquele jogador **deixa de valer
   como vigia** e o Capitão volta a andar, se ninguém mais o olha.
4. **Apagão é a hora dele.** No escuro, sem lanterna nele, anda livre e em arrancadas de 5,2
   m/s por 1,5 s. Fora isso manca a 1,7 m/s.
   *Como está feito:* caçando (investigando um barulho ou perseguindo), as arrancadas se repetem
   com 1 s mancando entre elas. Vagando, anda a 1,2 m/s; vasculhando, a 1,5.
5. **Totens.** Há 6 a 10 estátuas de fibra de vidro do Capitão Ancoradouro espalhadas pela
   loja, do mesmo tamanho e na mesma pose dura. De longe são iguais. As pistas para separar, ensinadas por um bilhete na sala inicial:
   - o totem fica numa **base preta** e segura a placa de OFERTA;
   - o Capitão **não tem base**, tem a **mancha de sangue** no casaco, os **olhos seguem** quem
     passa e a perna de pau **marca o carpete**.
   Durante um apagão ele pode **trocar de lugar com um totem**: quando a luz volta, há um totem
   onde ele estava, e ele está onde o totem estava.
6. **Captura:** ele agarra com as duas mãos. Igual ao Level 0 por enquanto (NO SIGNAL e volta para a sala
   inicial); depois, sala de contenção (`plano-coop.md`, 2.2) — aqui, um provador trancado.
7. **Fraquezas:** não passa por barricada nem entra em guarda-roupa; esconder-se debaixo de mesa
   só funciona agachado e fora do cone de visão dele.

---

## 6. Objetivo da fase

**Fechar o caixa.** A saída é a porta da doca de carga, trancada por um teclado. O código está
nas **etiquetas de LIQUIDAÇÃO**: entre todas as placas de preço, três são vermelhas, cada uma
com um dígito e a sua ordem ("1º", "2º", "3º"). Uma pessoa lê, outra digita. Com 1 jogador,
o código aparece também numa nota na sala inicial, embaralhado.

As etiquetas vermelhas são sorteadas pela seed sempre em zonas diferentes, nunca a menos de
6 células umas das outras: o grupo precisa se separar — e quem fica sozinho perde quem vigiava
o Capitão.

---

## 7. Som

- Zumbido fluorescente mais grave que no Level 0 e, por cima, uma **música de loja** baixinha,
  vinda do teto, que some e volta. Quando some de vez, é aviso de apagão.
- Passos mudam com o piso: carpete abafado, laminado oco, vinílico estalado.
- Plástico-bolha: estalos altos, ouvidos por toda a zona — e pelo Capitão.
- O Capitão: "TOC… arrasta", rangido de fibra de vidro quando ele vira a cabeça, caixinha de
  música. Nunca voz.

---

## 8. Ordem de construção

| # | Etapa | Depende de |
|---|---|---|
| 1 | Corpo do Capitão + prévia em imagem | — (**feito**: `CaptainBody`, menu Capturar prévia do Capitão; fotos em `Logs/Previews/capitao_*.png`) |
| 2 | `PropLayer` com os objetos e a cruz livre | — (**feito**, com 37 modelos do Blender) |
| 3 | Zonas e conjuntos; hash de objetos no teste de fumaça | 2 (**feito em parte**: temas por bloco 2 x 2, sala dos sofás, hash conferido por `Level1Setup.BatchCheck`; falta conferir entre host e cliente) |
| 4 | Troca de fase: a porta EXIT leva ao Level 1; o host envia índice da fase + seed | — (**feito**: `LevelExit`, `GameSession.OpenExit`) |
| 5 | Cérebro do Capitão: herda o ouvido do `EntityBrain`, regra de "visto e iluminado" | 1, 4 (**feito** em 17/11/2026: `CaptainBrain`, `Level1CaptainTest`) |
| 6 | Totens e troca no apagão | 3, 5 (**feito**: `Totem`, `CaptainBrain.TrySwap`) |
| 7 | Guarda-roupa como `HidingSpot`, plástico-bolha | 3 (**feito**: `HideSpot` com a tecla E, também a mesa; `BubbleWrap`) |
| 8 | Etiquetas de LIQUIDAÇÃO, teclado, porta da doca | 3 (**feito**: `ExitLock`; a porta continua a corta-fogo com EXIT) |
| 9 | Anomalias, barricadas, carro, vitrine de verão | 3 (**feito**: barricadas, sorteio com o carro, zona de verão e as anomalias; só a mesa no teto e o batente vieram como variação, o resto como o projeto descreve) |
| 10 | Som: jingle desafinado, perna de pau, música de loja | 5 (**feito**: `CaptainAudio`, `ShopMusic`; piso por zona com passos próprios) |

Os passos 2 e 4 não dependem um do outro e podem andar em paralelo.

## 9. Decisões em aberto

1. **Nome da fase.** "Level 1" no estilo da wiki, ou só "O Mostruário"?
2. **Encarar a filmadora** (regra 5.4.3) depende da sanidade, que ainda não existe. Sem ela, o
   castigo por encarar pode ser só o chuvisco crescer até tapar a imagem.
3. **Uma entidade ou duas?** A entidade de cipós do Level 0 aparecer aqui também, rara, deixaria
   a regra do Capitão mais tensa — mas complica ensinar.
4. **Aparência do Capitão e direitos autorais.** Ele foi desenhado bem perto do personagem do
   filme *Backrooms* (2026), a pedido. Para jogar entre amigos não há problema; antes de publicar
   (Steam, decisão 5 do plano), vale afastar o desenho — trocar cores da roupa, o rosto e o
   chapéu — para ele ser claramente nosso.
5. **Ideias novas** para esta fase, ainda não decididas (a loja que se rearruma, sala de
   segurança com monitores, alto-falante da loja, empurrar móveis para barricar):
   `mecanicas.md`, seção 3, propostas `P1.1` a `P1.4`.
