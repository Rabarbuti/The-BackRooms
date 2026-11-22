# Mecânicas das fases

Visão geral do que se faz em cada fase, em linguagem de jogador, e ideias novas ainda não
decididas. Escrito em 17/11/2026; atualizado em 21/11/2026 (menu de pausa, inglês, maré nova, os três
inimigos novos).

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
- **Lanterna** (F), com bateria: 4 min acesa (era 10 até 21/11/2026), em porcentagem no painel, piscando
  em vermelho abaixo de 20%. **Pilhas no chão** (desde 19/11/2026, `BatteryPickups`):
  de 4 a 9 cartelas por mapa, postas pela seed; mira + E pega e a lanterna de quem pegou volta cheia;
  quem chega primeiro leva (o host decide).
- **Filmadora VHS:** a tela inteira é a imagem de uma fita, com contador e hora. F9 grava um vídeo de
  verdade (AVI na pasta Vídeos\The BackRooms, com o efeito, o painel e o som): parado, o canto diz STBY;
  gravando, REC verde piscando com o tempo do clipe; ao parar, SAVED e o nome do arquivo. A tela
  **Recordings** (menu inicial e menu de pausa) lista os clipes, abre no reprodutor do sistema, abre a
  pasta e apaga com confirmação.
- **Caneta de riscar** em três cores (1, 2, 3). Risca parede, chão e teto; todos veem os riscos.
- **Não há morte.** Cair num buraco, ser pego ou se afogar dá NO SIGNAL e o jogador volta para a
  sala inicial.
- **Porta de saída** (E): todos precisam chegar nela. Quando todos esperam, a fase acaba e o grupo
  passa para a seguinte. Trancada, a maçaneta chacoalha; abrindo, range e bate.
- **Objetivo em toda fase** (desde 19/11/2026): a porta só destranca cumprido o objetivo da fase.
  Nas fases 0, 2 e 3 é o mesmo componente (`SwitchObjective`, antes a proposta `PC.1`): *pontos
  distantes armados dentro de uma janela de tempo*. Mira + segurar E; enquanto segura, o jogador faz
  barulho de corrida (o inimigo ouve). Não é "ao mesmo tempo": o primeiro ponto armado abre a janela e
  o segundo (com três ou quatro jogadores, os outros dois; no Escritório, sempre os três alarmes) tem de
  vir dentro dela: 10 s nos disjuntores, 5 s nas válvulas, 20 s nos alarmes. **Sozinho a janela some**
  (ponto armado fica armado). O painel diz o que falta.
- **Mira tolerante** (desde 21/11/2026): todo interagível de mira + E vale se está a até uns 9° do centro
  da tela (de perto, a 20 cm da mira), no alcance e sem parede no meio; com dois perto, ganha o mais perto
  do centro. Interruptores, botões de elevador, disjuntores, alarmes, a válvula do seco e o teclado do
  Level 1 ficam perto da altura dos olhos (1,35 a 1,45 m).
- **Menu de pausa** (Esc, desde 21/11/2026): o jogo **não para** (a fita continua rodando); o jogador só
  deixa de comandar. Mostra as **tarefas** da fase ao vivo (`[X]` feita, `[  ]` a fazer, com uma linha
  de detalhe: onde ficam os pontos e a regra da janela para o número de jogadores de agora), as
  **teclas** que valem nesta fase e os botões Continue, Options e Quit to menu.
- **Opções** (no menu inicial e no de pausa): volume, sensibilidade do mouse e qualidade (os três
  perfis do HDRP).
- **Cartaz de dicas** (TIPS) pregado na sala inicial das fases 0, 2 e 3: quatro dicas curtas e
  "ESC: TASKS". A instrução do objetivo está no menu de pausa.
- **Tudo o que o jogador lê é em inglês** (desde 21/11/2026): menus, filmadora, placas, etiquetas.
- **No menu inicial só a música se ouve:** a fase montada atrás dele fica muda.
- **Fim da fita:** a porta do Level 3 encerra a fita (END OF TAPE) com o tempo de cada fase.

### Planejado (`plano-coop.md`, seção 2; fora da entrega, ver `plano-de-acao.md`)

- **Sala de contenção:** quem é pego fica preso numa sala do mapa, que só um colega abre por fora.
  Todos presos = derrota (seção 2.2).
- **Voz por proximidade:** só se ouve quem está perto, e o inimigo ouve a voz (seção 2.1).
- **Sanidade:** cai sozinho e no escuro, sobe perto dos colegas; baixa, gera alucinações que só
  aquele jogador vê (seção 2.5).
- **Itens:** walkie-talkie, Almond Water (seção 2.4).

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
- **Disjuntores** (desde 19/11/2026): a porta está sem energia (`NO POWER`). Quatro caixas cinza na
  parede, longe do início e umas das outras, cada uma **zumbindo de longe** (a pista é o som), com
  uma lâmpada vermelha. Segurar E por 3 s arma uma (a lâmpada fica verde por 10 s); dois armados
  dentro dos 10 s religam a energia (três com três ou quatro jogadores). Armar faz barulho, e o
  estrondo da energia voltando chama a entidade para o último disjuntor.

### Planejado (`plano-coop.md`, seção 2.3)

- **Painel de código** (fora da entrega): digitar um código que aparece em outro ponto do mapa.
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

### Também existe (desde 17/11/2026)

- **Totens:** 6 a 10 estátuas iguais ao Capitão de longe, numa base preta com a placa amarela DISPLAY ONLY e
  com brilho de fibra de vidro. No apagão ele pode trocar de lugar com um totem que ninguém vê.
  De perto, o Capitão de verdade vira a cabeça para quem está perto e a perna de pau dele deixa
  marcas no chão.
- **Esconderijos:** guarda-roupa (E; esconde sempre; dentro só se vê pela fresta) e debaixo da mesa
  de jantar (agachado + E; ele vê quem está no cone dele a até 6 m).
- **Plástico-bolha:** estala alto e conta como corrida para o ouvido dele. **Barricadas** de móveis
  do chão ao teto: ninguém passa, e nunca isolam nada.
- **Objetivo:** a porta de saída tem um teclado; mira + E nele abre o painel do código na tela
  (digita-se pelo teclado ou clicando; o jogo não para enquanto isso). O código está em **três
  etiquetas vermelhas de CLEARANCE** (liquidação), longe umas das outras, cada uma com o dígito e a
  posição (1/3, 2/3, 3/3). Só elas são vermelhas com CLEARANCE: as outras placas de preço são
  amarelas (DISPLAY ONLY). Sem bilhete na sala inicial (tirado em 21/11/2026: ficava fácil demais).
- **Som:** jingle de caixinha perto do Capitão, música de loja que some antes do apagão e passos
  diferentes no laminado e no vinílico.

### Planejado (`level1-mostruario.md`)

- Nada do projeto além de pequenos acabamentos (ver `level1-mostruario.md`, "Falta"). Também já
  existem as zonas quarto, escritório, cubículos, lavanderia, festa, verão e o sorteio (o carro), as
  placas penduradas com o nome da zona e as anomalias longe do início.

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
- **O caminho até a saída nunca exige nadar**, mas o objetivo exige (uma das válvulas, abaixo).
- **Luz só de claraboias e janelas.** No lugar do apagão, o **anoitecer**: a luz esquenta, cai até
  a noite e volta.
- **A maré** (`PoolTide`, refeita em 21/11/2026), em todas as máquinas juntas:
  - aos 20 s de partida (e a cada 90 s) toca um **alarme**: sirene na fase inteira, lâmpadas vermelhas
    de parede piscando (uma ou duas por sala), uma buzina de piscina de ondas e o aviso na filmadora
    (`TIDE COMING`);
  - a água **sobe 1,3 m em 20 s**, fica **alta 15 s** e **desce em 20 s** (a vazante); enquanto anda,
    ronco de água correndo e superfície agitada;
  - na maré alta o **raso passa da cabeça** (1,72 m de água: ou se nada ou se espera no seco) e o **seco
    fica com água pelo peito** (1,12 m: vadeia-se devagar e respira-se; mas desde 21/11/2026 não é abrigo
    do Undertow, que na maré alta caça no mapa inteiro);
  - **com água acima do joelho não se corre**;
  - a água deixa marca molhada nas paredes e no chão, que seca em ~50 s, e há uma linha de sujeira na
    altura da maré alta (dá para saber até onde ela chega).
- **O ralo**: na **vazante** a água sai pelos ralos dos poços e puxa para a grade quem nada até 10 m
  além do poço; chegar à grade é afogar. As lâmpadas perto do poço piscam e os ralos gorgolejam.
- **The Undertow** (desde 21/11/2026), o inimigo da água funda: um afogado esticado de 3,4 m.
  - **Maré baixa: está em qualquer água em que dá para afundar** (piscinas, raias, canais, poços) e
    **nunca sai dela**; seco e raso são abrigo. Começa no poço mais longe da sala inicial, mas **não tem
    casa**: parado sem ouvir nada, a cada 16–30 s muda de água pelos canos, de preferência para uma a
    ~14 m de algum jogador, nunca a menos de 10 m de ninguém nem numa água onde alguém nada. Surge com
    bolhas e um gorgolejo do ralo, sem rugido: nunca se sabe em qual água ele está.
  - **Maré alta: persegue pelo mapa inteiro.** Quando o seco alaga (1,12 m de água), ele **ruge** e passa
    a rondar os jogadores a 8–18 m (nunca a menos de 7 m), vindo pelo barulho. Fora da água funda é mais
    lento: vem a 1,6 m/s, caça a 2,2 (quem vadeia anda a 1,54, sem correr) e dá um **bote** a 3 m/s nos
    últimos 2,5 m. **A sala inicial continua abrigo.** Quando a maré começa a descer, ele some pelos ralos
    e volta a uma água funda.
  - **É cego: caça pelo barulho na água.** Pular ou cair na água (espirro) chama de 24 m; correr
    chapinhando, até 24 m; andar vadeando, ~13 m; andar agachado na água, ~6 m; nadar à tona, ~11 m;
    nadar submerso, ~3 m; parado, nada. De outra água, pelos canos, só espirro e corrida.
  - **Só pega quem ele ouve:** quem ele ouviu nos últimos 2 s (ou quem esbarra nele, a 0,6 m). **Parado e
    quieto, ele perde a pista:** vem até perto de onde ouviu o último barulho, para a 1,6 m, escuta 4 s e
    vai embora. Ao ouvir o rugido, o certo é parar (ou sair da linha dele devagar, agachado).
  - **Os canos ligam as piscinas:** um espirro noutra água o chama; ele afunda, some e reaparece lá
    (num poço dela, se houver), rugindo.
  - **Avisos:** um **rugido** animal, rasgado, que se ouve de longe (abafado para quem está com a cabeça
    debaixo d'água), ao ouvir e acordar, ao dar o bote e quando a maré alaga o seco; nadando, um
    **rosnado** de garganta com a água revolvendo, que cresce e sobe de tom quanto mais perto; a sombra
    de braços abertos passando no fundo; as bolhas.
  - **Pega quem está com água acima da cintura no território dele** e puxa para o fundo (NO SIGNAL).
    **Quem sai da água escapa** (na maré baixa): ele ronda a borda por onde o jogador saiu por 8 s e vai
    descansar ali mesmo, na água funda.
  - **Drenada a fase**, arrasta-se pelo fundo das piscinas vazias a 0,9 m/s (o jogador anda a 2,8) e
    ainda pega quem fica no fundo; nos poços, ainda com água, nada normal.
  - Números: vai ao barulho a 2,3 m/s e caça a 3,3 (o jogador nada a 1,26).
- **Objetivo: drenar** (`SwitchObjective`, válvulas). Duas rodas vermelhas: **uma no seco, a pé; a
  outra do outro lado da água funda** (é preciso nadar até ela): numa ilha ou, quando a seed não tem
  ilha, na parede sobre a água, girada boiando (Espaço e E juntos). A roda fica sempre acima da maré
  alta. Girar leva 6 s; a segunda dentro de 5 s da primeira (sozinho, basta abrir as duas). Abertas, a
  água do mapa desce 2 m em 30 s (aberta no meio de uma subida, a maré congela e a água desce na hora),
  o alarme cala e a saída destranca.

### Planejado (`level2-piscinas.md`; fora da entrega)

- **O ralo que acorda com barulho** (seção 5), a mão branca atrás da grade e os **túneis submersos**.
- **Casas de máquinas** como salas e a porta no fundo do maior poço (mudam a planta).
- **Válvula no fundo de um poço** (`WaterValveSpot.WellBottom`, mergulho; é a `P2.4`): pronta no
  código e desligada.

### Propostas

**P2.1 — Puxar um colega da água.** Quem está na borda segura quem está se afogando (perto e
segurando E) e o tira da água.

- *Força:* ficar junto. É o resgate que a fase não tem.
- *Reaproveita:* a subida pela borda (`PlayerController.TryStartClimb`), feita por outro jogador.
- *Cuidado:* mexe no jogador de outra máquina, que tem autoridade sobre o próprio movimento: o
  pedido vai pela rede e o dono executa.

**P2.2 — Ondas que denunciam.** Cada espirro forma ondas que se espalham pela água e balançam o que
boia. O barulho fica visível, e o ralo (ou o Undertow) "sente" a onda em vez de ouvir.

- *Força:* ficar calado.
- *Reaproveita:* a conta de barulho por superfície (`Footsteps.NoiseOf`) e a ondulação da
  `PoolWater`.

**P2.3 — Eco do azulejo.** O barulho vai mais longe nas salas grandes de azulejo (o alcance
multiplicado por um fator da sala). O mesmo passo é seguro num túnel e perigoso no salão.

- *Força:* ficar calado.
- *Custo:* baixo; agora há quem ouça (o Undertow).

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

## 5. Level 3 — O Escritório

Projeto completo em `level3-escritorio.md`.

### Existe

- O andar de um prédio vazio: anel de corredores de teto técnico, salas de aula, saguão de
  elevadores, cafeteria, área de estar e o vão da escada com a saída (a porta corta-fogo no patamar).
- **A saída se procura** (desde 21/11/2026): a escada fica do lado oposto do andar ao elevador de
  chegada, fora da vista do saguão, e o vão fica atrás de parede: do corredor só se vê uma porta como
  as das salas, com a placa EXIT pequena rente à parede e uma plaquinha de escada. Placa pendurada só
  na última curva antes da escada.
- **A entidade do Level 0 saiu daqui** (21/11/2026). A fase tem dois inimigos próprios: **a luz salva
  de um (o Zelador); o silêncio salva do outro (o Ascensorista)**. A salinha acesa protege de um e não
  do outro.
- **A onda de luzes persegue** (desde 21/11/2026): a cada ~40 s (a partir dos 25 s) o trecho reto de
  corredor onde está um jogador (revezando entre eles) **pisca 2,5 s** (o reator falha no som: é a hora
  de sair), apaga em cascata, fica **16 s** apagado e volta uma luminária por vez. O zumbido some onde as
  luzes apagam: dá para ouvir o escuro chegando.
- **The Janitor (o Zelador)**: só existe no escuro.
  - **Nunca pisa numa célula acesa.** Quem está na luz está a salvo, mesmo a um passo: ele não vê, não
    ouve e não pega. As salinhas acesas são abrigo.
  - **Nasce perto:** quando o trecho apaga, sobe do chão (com um gorgolejo) na ponta mais perto do
    jogador que ficou dentro, nunca a menos de 3 células (12 m) de caminhada. Também aparece sozinho em
    qualquer escuro, longe de todos e fora da vista.
  - Enxerga bem no escuro (cone de 60°, 20 m), ouve mal (só corrida, a 4 células). Persegue só pelo
    escuro e para na beira da luz. Pega quem está no escuro ao alcance (NO SIGNAL).
  - **A luz voltando sobre ele o faz afundar e sumir.** A lanterna não o detém: o facho o faz hesitar
    0,6 s (no máximo a cada 4 s).
  - Pistas (som refeito em 21/11/2026): parado, o esfregão molhado em vaivém e, de tempos em tempos, o
    pano batendo no balde e torcido; rondando, botas de borracha pesadas, chaves chacoalhando baixo e o
    rodízio do balde guinchando no ritmo da velocidade, às vezes um resmungo; vindo, o guincho acelera e
    o balde bate. Arrancar ou parar chacoalha a água do balde.
  - Números: ronda a 1,4 m/s, caça a 4,4 por 5 s e cansa (2,4 por 4 s); jogador anda 2,8 e corre 5,0.
- **Interruptores de luz:** um por trecho de corredor da onda (placa branca com alavanca e luzinha).
  Mira + E com o trecho piscando ou escuro **religa o trecho na hora** e o Zelador dali some. O estalo é
  **alto** (45 m: chama o Ascensorista). Depois recarrega 25 s (a mira diz RESETTING).
- **The Attendant (o Ascensorista)**: cego, chega de elevador.
  - **Chega de elevador:** a seta âmbar em cima da porta de um poço pisca 5 s e a cabine ronca; plim, a
    porta abre e ele escuta parado na porta 2,4 s antes de sair. Primeira chegada aos 60 s; depois,
    30 a 50 s depois de partir. Nunca chega por um poço com jogador na frente.
  - **Só caça pelo barulho:** correndo ouve-se de longe (9 células), andando de perto (~3), agachado
    andando só na mesma célula, parado nada. **A 1,5 m, sem parede no meio, ouve a respiração**: ficar
    quieto só salva fora do caminho dele.
  - **Para para escutar** (2,5 s, estalando a boca) a cada destino novo e a cada 7 s andando: hora de
    não se mexer. Ouviu, vai a 3,4 m/s.
  - **Entra em qualquer sala**; só a cabine do início é abrigo.
  - **Quem ele pega é levado para a cabine dele:** a porta fecha na cara do jogador e só então cai o
    sinal. Os outros veem a porta daquele elevador fechar e reabrir vazia.
  - Fica uns 75 s no andar (a porta da cabine dele fica aberta e vazia: pista de que ele está fora) e
    parte; quem estiver dentro da cabine dele vai junto.
  - **Trancar-se numa cabine de poço** (botão de fechar) protege até a vez daquela cabine: se a
    chegada dele cair nela, a seta pisca lá fora 5 s antes.
  - Pistas: a bengala batendo no piso duro (de longe), o arrasto da bengala correndo, os estalos da
    boca, a respiração chiada de perto, o ronco da cabine e o plim.
- **Botões dos elevadores:** abrir e fechar no painel de cada cabine, chamar no saguão (mira + E). Um
  poço chamado acende, abre 1,5 s depois e fica 8 s aberto. A porta nunca prende: com alguém no vão,
  reabre. Os elevadores não chegam mais vazios: todo plim é o Ascensorista.
- **Armários que batem:** de tempos em tempos, uma fileira de portas de armário bate num corredor longe
  de todos, vindo na direção do jogador mais perto. O Ascensorista ouve e vai até lá.
- **Som ambiente:** ar-condicionado, difusores e o zumbido dos reatores, que cala onde as luzes apagam.
- **Objetivo: o alarme de incêndio** (`SwitchObjective`, alarmes): três acionadores vermelhos, **cada um
  dentro de uma sala** (de aula, de reunião, pequena ou a cafeteria), numa parede que não se vê da porta:
  é preciso entrar e olhar em volta (piscam vermelho e zumbem). Longe da cabine e da escada. **Precisa
  dos três** (desde 21/11/2026); puxar leva 2 s; sozinho ficam armados, em grupo os três dentro de 20 s. Disparado, a sirene toca e a
  porta corta-fogo destrava; a sirene chama o Ascensorista (se está fora do andar, chega em até 6 s) e
  **a energia do andar cai**: todas as luminárias apagam menos as das salinhas-abrigo, acendem luzes de
  emergência fracas a cada 3 células nas rotas até a escada (nunca mais de dois escuros seguidos) e os
  interruptores param (NO POWER). A fuga até a escada é de ilha em ilha de luz; o vão da escada é
  seguro.

### Planejado (`level3-escritorio.md`, seção 6)

- Cadeiras viradas para a porta, telas que ligam; modelos do Blender do cenário.

---

## 6. Por onde começar

A ordem até a entrega, com testes e critério de pronta, está em `plano-de-acao.md` (reescrito em
19/11/2026). Das propostas acima, nenhuma entra na entrega além de `P0.3` (feita) e `P2.5` (como
dica no cartaz das Piscinas, "MARK THE SHALLOWS"); a `P2.4` está pronta no código e desligada; as
outras ficam para depois.
