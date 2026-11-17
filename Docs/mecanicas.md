# Mecânicas das fases

Visão geral do que se faz em cada fase, em linguagem de jogador, e ideias novas ainda não
decididas. Escrito em 17/11/2026.

Cada mecânica traz uma de três marcas:

- **Existe** — já está no jogo e dá para jogar.
- **Planejado** — está no projeto da fase, com detalhes no documento indicado, mas ainda não foi
  feito.
- **Proposta** — ideia nova, ainda não decidida. As propostas têm código (`P0.1`, `P1.2`...) para
  serem citadas em conversa e commit. Uma proposta aceita sai daqui e vai para o documento da fase,
  com a marca **Planejado**.

Toda mecânica precisa reforçar uma das quatro forças do jogo (`plano-coop.md`, seção 1): **ficar
junto** (sanidade), **se separar** (objetivos), **falar** (coordenação) ou **ficar calado**
(inimigo que ouve). Cada proposta diz qual delas reforça.

---

## 1. Em todas as fases

### Existe

- **Co-op de 1 a 4 por IP.** Um hospeda, os outros entram. Cada máquina monta o mesmo mapa a partir
  da seed que o host envia.
- **Movimento:** anda, corre (6 s de fôlego), agacha, pula.
- **Lanterna** (F), com bateria infinita.
- **Filmadora VHS:** a tela inteira é a imagem de uma fita, com REC, contador e hora. F9 grava um
  vídeo de verdade.
- **Caneta de riscar** em três cores (1, 2, 3). Risca parede, chão e teto; todos veem os riscos.
- **Não há morte.** Cair num buraco, ser pego ou se afogar dá NO SIGNAL e o jogador volta para a
  sala inicial.
- **Porta de saída** (E): todos precisam chegar nela. Quando todos esperam, a fase acaba e o grupo
  passa para a seguinte.

### Planejado (`plano-coop.md`, seção 2)

- **Objetivo de verdade em cada fase.** Hoje chegar à porta basta. A saída passa a exigir tarefas
  em lugares distantes, ao mesmo tempo, por pessoas diferentes (seção 2.3).
- **Sala de contenção:** quem é pego fica preso numa sala do mapa, que só um colega abre por fora.
  Todos presos = derrota (seção 2.2).
- **Voz por proximidade:** só se ouve quem está perto, e o inimigo ouve a voz (seção 2.1).
- **Sanidade:** cai sozinho e no escuro, sobe perto dos colegas; baixa, gera alucinações que só
  aquele jogador vê (seção 2.5).
- **Itens:** pilhas da lanterna, walkie-talkie, Almond Water (seção 2.4).

### Propostas

**PC.1 — Um componente de objetivo para as três fases.** Os três objetivos planejados têm a
mesma forma: *N pontos distantes que precisam ser ativados dentro de uma janela de tempo*
(disjuntores no Level 0, teclado e etiquetas no Level 1, válvulas no Level 2). Vale um componente
só, posto pela seed e conferido pelo host, com:

- quantos pontos, quanto tempo de janela e quanto tempo segurando E cada um leva;
- o barulho que cada ponto faz enquanto é ativado (o inimigo ouve);
- com 1 jogador, a janela desligada (cada ponto fica ativado), como os documentos já pedem.

*Força:* se separar. *Cuidado:* estado que quem entra depois precisa ver, como os riscos da caneta
(`DrawingBoard.SendAll`).

---

## 2. Level 0 — o labirinto amarelo

### Existe

- **Labirinto** de 80 x 80 m, de carpete e luz fluorescente. Fica mais sujo (sangue, frases nas
  paredes, lâmpadas ruins) quanto mais longe da sala inicial.
- **Buracos** de 7 m no caminho mais curto. Atravessa-se andando numa viga estreita (arriscado) ou
  dá-se a volta (mais longo, sempre existe).
- **Apagões:** as luzes piscam e apagam por 10 s, em todas as máquinas ao mesmo tempo. Sobram as
  lanternas.
- **Esconderijos:** nichos baixos na parede, só agachado. Lá dentro ninguém é visto nem pego.
- **A entidade:** 3,3 m de cipós pretos, sem olhos. Surge quando alguém se afasta da sala inicial
  e **caça pelo barulho** (correr faz muito, andar médio, agachar pouco, parado nada); também vê de
  perto e num cone à frente. Avisa com um ronco e chuvisco na filmadora. Salva-se quem se esconde,
  fica parado, atravessa um buraco pela viga (ela não anda nelas) ou volta à sala inicial (ela não
  entra).

### Planejado (`plano-coop.md`, seção 2.3)

- **Elevador sem energia.** Achar **disjuntores** que zumbem (a pista é o som), armar dois
  **quase ao mesmo tempo** em lugares diferentes e digitar num painel um **código que aparece em
  outro ponto do mapa**: um lê, outro digita.
- **Entidade:** aproveitar o apagão, reagir à lanterna, mais de uma, vigiar esconderijos
  (`plano-coop.md`, "O que ainda não existe").

### Propostas

**P0.1 — A entidade imita passos.** De vez em quando ela toca passos de carpete vindos de outra
direção, como se fosse um colega andando. O jogador não sabe se é amigo ou ela.

- *Força:* falar ("é você virando à esquerda?").
- *Reaproveita:* o `Footsteps` já sintetiza o passo de carpete de qualquer posição.
- *Cuidado:* raro e só quando o grupo está separado; senão vira ruído e ninguém liga.

**P0.2 — O labirinto muda no apagão.** Durante o escuro, algumas paredes trocam de lugar. Os
riscos da caneta viram a única prova ("eu risquei aqui e agora é parede").

- *Força:* falar e ficar junto (quem estava longe não viu a mudança).
- *Reaproveita:* o apagão e o relógio da sessão: cada máquina faz a mesma troca pela seed e pelo
  número do apagão, sem mandar nada pela rede.
- *Cuidado:* depois de cada troca, buraco nenhum pode isolar parte do mapa (a regra do
  `SafeDetour`) e ninguém pode ficar preso dentro de uma parede nova. As setas pintadas mentem
  depois da troca, o que pode ser bom.

**P0.3 — Lanterna com bateria e pilhas no chão.** Já está nos itens planejados; a proposta é
fazê-la cedo, porque é barata e deixa o apagão tenso de verdade.

- *Força:* ficar junto (dividir pilhas, andar perto de quem tem luz).

**P0.4 — Visão noturna na filmadora.** Uma tecla liga o modo noturno: imagem verde, enxerga no
escuro, gasta a bateria da filmadora depressa. No apagão, mostra a entidade, que hoje quase some.

- *Força:* falar (um jogador vira o "olho" do grupo e guia os outros).
- *Reaproveita:* o efeito VHS (`CamcorderVHS.shader`) e a bateria que o painel já desenha.
- *Cuidado:* depende de a bateria da filmadora existir de verdade (hoje é só desenho).

---

## 3. Level 1 — o Mostruário (loja de móveis)

Projeto completo em `level1-mostruario.md`.

### Existe

- O mesmo labirinto amarelo (20 x 20 células, teto mais baixo) e sem buracos, **cheio de móveis**: salas de
  jantar montadas, estantes, sofás, cadeiras tombadas e empilhadas. Os móveis tapam a visão mas
  nunca fecham uma passagem.
- **Placas amarelas** de curva na parede apontam o caminho até a saída.
- **Apagão**, como no Level 0.
- **O Capitão** (desde 17/11/2026; `level1-mostruario.md`, seção 5): homem de 2,6 m vestido de
  pirata, a mascote de uma loja de móveis. Aparece quando alguém se afasta da sala inicial.
  - **Ouve** e vê como a entidade do Level 0.
  - **Só se mexe quando ninguém olha.** Visto e iluminado (luminária acesa perto ou lanterna),
    congela no meio do passo e não pega ninguém.
  - **Não dá para vigiar para sempre:** encarado mais de 6 s, o chuvisco cresce; aos 9 s a imagem
    some e quem encarava deixa de segurá-lo. Alguém precisa desviar, e o grupo combina quem olha.
  - **Manca devagar** (mais devagar que quem anda); **no apagão anda livre**, em arrancadas.
  - Pega como a entidade (NO SIGNAL, volta à sala inicial).
  - Avisa com o som da perna de pau ("TOC… arrasta").
- **Sem objetivo:** anda-se até a porta.

### Planejado (`level1-mostruario.md`)

- **O jingle de loja desafinado** perto do Capitão, e a música da loja (seção 7).
- **Totens** (seção 5.4): 6 a 10 estátuas iguais a ele de longe. O totem tem base preta e placa de
  OFERTA; o de verdade, sangue no casaco e olhos que seguem. No apagão ele pode trocar de lugar
  com um totem.
- **Esconderijos:** guarda-roupa, e debaixo da mesa agachado.
- **Plástico-bolha:** estala alto e chama o Capitão. **Barricadas** de móveis: ele não passa.
- **Objetivo** (seção 6): a porta da doca tem um teclado. O código está em **três etiquetas
  vermelhas de LIQUIDAÇÃO**, longe uma da outra, cada uma com um dígito. Para achá-las o grupo se
  separa, e quem se separa deixa de vigiar o Capitão.

### Propostas

**P1.1 — A loja se rearruma.** Não é só o Capitão que se mexe quando ninguém olha: alguns móveis
também trocam de lugar. O jogador aprende que tudo mexe, e os totens ficam mais traiçoeiros.

- *Força:* falar ("aquela estante estava aqui?").
- *Reaproveita:* a regra "visto e iluminado" do Capitão e a `PropLayer`.
- *Cuidado:* a cruz livre de cada célula (móvel nunca fecha passagem) tem de valer depois de cada
  troca, e as duas máquinas precisam trocar igual.

**P1.2 — Sala de segurança com monitores.** Uma salinha com telas mostrando câmeras da loja. Um
jogador fica lá vendo onde está o Capitão e guia os outros pela voz.

- *Força:* falar e se separar (quem vigia as telas fica sozinho e longe).
- *Cuidado:* cada câmera custa um desenho a mais no HDRP. O seguro é imagem pequena e uma câmera
  atualizada por vez, poucas vezes por segundo.

**P1.3 — O alto-falante da loja.** Anúncios em horários do relógio da sessão ("cliente no
corredor sete"). Às vezes avisam onde o Capitão está; às vezes entregam ao Capitão onde está um
jogador.

- *Força:* ficar calado e falar.
- *Reaproveita:* o relógio `SessionSeconds` (todas as máquinas ouvem o mesmo anúncio na mesma
  hora). Combina com a música de loja que o projeto já pede (seção 7).

**P1.4 — Empurrar móveis para barricar.** O Capitão não passa por barricada (já planejado); a
proposta é que os jogadores **façam** a barricada empurrando móveis. Móvel grande, só dois
empurrando juntos.

- *Força:* ficar junto.
- *Cuidado:* posição de móvel vira estado de rede (quem entra depois precisa ver) e não pode fechar
  a última passagem até a saída.

---

## 4. Level 2 — As Piscinas

Projeto completo em `level2-piscinas.md`.

### Existe

- **Salas de azulejo branco** de vários tipos (salões de colunas, piscina com deque, raia, canal,
  túnel em abóbada, poços, cisterna), ligadas com laços.
- **Água em quatro alturas:**

  | Altura | O que acontece |
  |---|---|
  | Seco | Anda-se normal. |
  | Raso | Água na canela; anda-se mais devagar. |
  | Funda (2 m) | Não dá pé: nada-se. |
  | Poço | Muito fundo e escuro. |

- **Nado:** sem tecla afunda devagar; segurando Espaço sobe e boia; Ctrl ou C mergulha. Debaixo
  d'água o fôlego dura 20 s; acabou, afoga e volta ao início. Da funda sai-se por escada ou subindo
  na borda.
- **O caminho até a saída nunca exige nadar.**
- **Luz só de claraboias e janelas.** No lugar do apagão, o **anoitecer**: a luz esquenta, cai até
  a noite e volta.
- **Sem inimigo e sem objetivo.**

### Planejado (`level2-piscinas.md`)

A ideia é **não ter monstro perseguindo**: o perigo é a água.

- **A maré** (seção 4.3): a cada 90 s a água sobe quase 1 m e depois desce, em todas as máquinas
  juntas. O raso vira nado, quem estava num lugar baixo fica ilhado. Os ralos borbulham antes, de
  aviso. *Em aberto* (decisão 5): na maré alta até o seco fica com água; falta decidir se haverá
  patamares altos de refúgio ou maré defasada por região.
- **O ralo** (seção 5): uma grade escura no fundo de cada poço. Barulho perto **acorda** o ralo,
  que por 8 s puxa para baixo quem nada no poço; chegar à grade é afogar. Antes, as luzes do poço
  piscam e a água gira. Nunca se vê o que há atrás, no máximo uma mão branca por meio segundo.
- **Túneis submersos** nos poços: atalho curto e arriscado entre partes distantes.
- **Objetivo: esvaziar a piscina** (seção 6). A saída é uma porta no fundo do maior poço. Dois
  jogadores abrem **válvulas em casas de máquinas** em pontas opostas, quase ao mesmo tempo; girar
  faz barulho e acorda os ralos. Drenado, a água desce no mapa todo e a porta aparece.

### Propostas

**P2.1 — Puxar um colega da água.** Quem está na borda segura quem está se afogando (perto e
segurando E) e o tira da água.

- *Força:* ficar junto. É o resgate que a fase não tem.
- *Reaproveita:* a subida pela borda (`PlayerController.TryStartClimb`), feita por outro jogador.
- *Cuidado:* mexe no jogador de outra máquina, que tem autoridade sobre o próprio movimento: o
  pedido vai pela rede e o dono executa.

**P2.2 — Ondas que denunciam.** Cada espirro forma ondas que se espalham pela água e balançam o que
boia. O barulho fica visível, e o ralo "sente" a onda em vez de ouvir.

- *Força:* ficar calado, numa fase sem monstro.
- *Reaproveita:* a conta de barulho por superfície (`Footsteps.NoiseOf`) e a ondulação da
  `PoolWater`.

**P2.3 — Eco do azulejo.** O barulho vai mais longe nas salas grandes de azulejo (o alcance
multiplicado por um fator da sala). O mesmo passo é seguro num túnel e perigoso no salão.

- *Força:* ficar calado.
- *Custo:* baixo; só faz sentido quando houver alguém ouvindo (o ralo).

**P2.4 — Mergulho em dupla.** Uma válvula ou chave fica no fundo de um poço escuro. Um mergulha;
outro ilumina de cima com a lanterna e conta o fôlego dele em voz alta.

- *Força:* falar e ficar junto.
- *Reaproveita:* a lanterna, que já ilumina dentro d'água, e o fôlego.

**P2.5 — Marcar onde dá pé.** A caneta não risca debaixo d'água; os jogadores marcam nas paredes
onde é seguro. Com a maré (planejado), essas marcas ficam erradas quando a água sobe.

- *Força:* falar.
- *Custo:* nenhum: já funciona hoje. A proposta é ensinar isso ao jogador (um bilhete na sala
  inicial, marcas de exploradores anteriores).

---

## 5. Por onde começar

Sugestão de ordem, do que mais muda o jogo pelo menor esforço. O detalhe de cada etapa (passos,
testes, decisões) está em `plano-de-acao.md`.

1. **O cérebro do Capitão** (Level 1). O corpo está pronto e o ouvido vem do `EntityBrain`; falta a
   regra "visto e iluminado". É o que dá identidade à fase.
2. **O componente de objetivo (PC.1), começando pelos disjuntores do Level 0.** Dá sentido às três
   fases e é a base do teclado do Level 1 e das válvulas do Level 2.
3. **A maré** (Level 2), depois de decidir o refúgio (decisão 5 de `level2-piscinas.md`). É o
   evento global da fase, feito como o apagão.
