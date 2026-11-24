# Level 3 — O Escritório

Projeto da quarta fase. Escrito em 17/11/2026 a partir do prompt `level3-escritorio-prompt.md` e das
referências (13 fotos e 3 vídeos de um prédio real, que **não entram no repositório**: mostram o nome
de uma instituição, cartazes e pessoas).

**Já existe:** uma versão de teste jogável (`Scenes/Level3_Test.unity`, menu BackRooms > Montar
Level 3 (teste)), na lista de fases depois das Piscinas (a porta do Level 2 leva a ela; é a última):
- **Planta** (`OfficeGenerator`, C# puro, só `System.Random(seed)`, com `Validate` e `Hash`): 16 x 14
  células de 4 m (14 x 12 até 23/11/2026; ver "Andar maior" abaixo); anel de corredores recuado 2 ou 3 células da fachada, um transversal norte-sul (às
  vezes também um leste-oeste); saguão de 3 x 2 com a fileira dos elevadores (poço, cabine inicial,
  poço) no miolo; salas de aula e salas pequenas no resto do miolo e na faixa da fachada; cafeteria
  perto do saguão, área de estar e o vão da escada (3 células, longe do saguão) na faixa da fachada.
  Regras da seção 2.3 no `Validate` (tudo alcançável da cabine, toda sala com porta para o corredor,
  caminho de 10 células ou mais, vão a 6 células ou mais do saguão; desde 21/11/2026 a entrada da
  escada fica no corredor mais longe a pé da cabine, ou quase, e fora da vista do saguão); reprovou,
  sorteia de novo.
- **Construção** (`LevelBuilder.Office.cs` e `LevelBuilder.OfficeProps.cs`): piso de porcelanato
  cinza (madeira nas salas de aula), teto técnico nos corredores (laje escura, duto galvanizado,
  eletrocalha, conduítes, cano vermelho), forro branco nas salas, ripas escuras no saguão, na
  cafeteria e na área de estar; divisórias de vidro fosco em esquadria branca, portas brancas abertas
  encostadas na parede, janelas com persiana (luz de fora entrando) na fachada; armários amarelos,
  quadros elétricos, extintores e câmeras domo; mesas e cadeiras de tela em fileiras; salas técnicas,
  depósitos e laboratórios; cafeteria com balcão de OSB, banquetas, geladeiras e painel de senha;
  sofá curvo, pufes e plantas; cabines de inox com corrimão e painel; o vão da escada com degraus de
  granito (fita preta, ponteiras amarelas), viga grafite, guarda-corpos, lance de baixo fechado por
  grade, os outros andares como cascas, claraboia; a porta corta-fogo da saída no patamar, com a
  placa verde. Desde 21/11/2026 o vão fica atrás de parede: do corredor só se vê uma porta igual às
  das salas, com a placa de saída rente à parede em cima e uma plaquinha de escada ao lado; placa
  pendurada (com seta) só na última curva do caminho, se fica a até 3 células da escada.
  Tudo em código, sem modelo do Blender ainda.
- **Luz:** luminárias tubulares penduradas nos corredores (5200 K), embutidas nas salas (4300 K),
  pendentes no saguão (3600 K); spots com sombra em cache de uma face só; trechos de corredor
  apagados e salas escuras; Volume próprio (`Settings/Level3_Volume.asset`): céu frio fraco, névoa
  cinza-azulada, cor dessaturada.
- **Tensão:** a porta da cabine abre sozinha 3,5 s depois do início; a **onda de luzes**
  (`OfficeLightWave`, no lugar do apagão) que **persegue** (desde 21/11/2026): a cada ~40 s, a partir dos
  25 s, o host escolhe o trecho de corredor onde está um jogador (`WavePlan` pela rede), as luminárias
  piscam 2,5 s, apagam em cascata, ficam 16 s apagadas e voltam uma a uma; o **elevador** (`OfficeElevators`)
  não chega mais vazio: todo plim é o Ascensorista (`VisitsByEnemy`). Sons sintetizados (`OfficeSounds`).
- **Som ambiente** (20/11/2026, `OfficeAmbience`): ar-condicionado, difusores e reatores que calam onde a
  onda apaga (a cena não tem mais o zumbido 2D). **Armários que batem** (`LockerBangs`, ideia 5 da seção
  6): uma fileira de portas bate num corredor longe dos jogadores, vindo na direção deles, pelo relógio;
  o barulho vai aos inimigos por `WorldNoise`. **Botões dos elevadores** (`ElevatorControl`): abrir e
  fechar na cabine, chamar no saguão; o host decide; a porta reabre com alguém no vão.
- **Passos de piso duro** (`LevelBuilder.HardFloorActive` → `Footsteps` usa os do azulejo seco).
- **Objetivo** (19/11/2026): a porta corta-fogo fica trancada até o **alarme de incêndio** disparar
  (`SwitchObjective`, três acionadores vermelhos longe uns dos outros: puxar leva 2 s. Até 21/11/2026
  ficavam em paredes de corredor e bastavam dois, o segundo dentro de 8 s; desde então **cada um fica
  dentro de uma sala**, fora da vista da porta, e **valem os três**, dentro de 20 s do primeiro em
  grupo; sozinho, sem janela). Disparado, a sirene toca
  nos acionadores e a porta destrava; desde 21/11/2026 a sirene chama o Ascensorista e **a energia do
  andar cai** (só as salinhas-abrigo ficam acesas, luzes de emergência a cada 3 células nas rotas até a
  escada, interruptores sem energia).
- **Dois inimigos próprios** (21/11/2026; a entidade do Level 0 saiu; decisão 4): **The Janitor** (o
  Zelador, `JanitorBrain`: só existe no escuro, nasce na ponta do trecho que a onda apagou, some quando a
  luz volta; desde 23/11/2026 espreita na beira do escuro mais perto do jogador e caça na hora quem pisa no
  escuro, `JanitorBrain.Lurk.cs`; o esfregão se recolhe rente à parede, `JanitorBody.Fit`; interruptores de luz, `LightSwitches`, religam o trecho com estalo alto e recarregam 25 s) e
  **The Attendant** (o Ascensorista, `AttendantBrain`: cego, chega de elevador, caça pelo barulho, ouve a
  respiração de perto, entra em qualquer sala e leva quem pega para a cabine). Modelos esculpidos no
  Blender (`zelador.py`, `ascensorista.py`). Regras em `mecanicas.md`, seção 5.
- **Texto do jogador em inglês** (21/11/2026): avisos da filmadora (`E - LIGHTS ON`, `E - CALL
  ELEVATOR`...), cartaz de dicas (TIPS / HIDE IN A SMALL ROOM / WITH THE LIGHT ON / CROUCH TO STAY QUIET /
  ESC: TASKS), no menu "Level 3 — The Office".

- **Andar maior** (23/11/2026, pedido "expanda um pouco, sem mudar a experiência"): de 14 x 12 para
  16 x 14 células (64 x 56 m), com o mesmo gerador. Em 508 seeds: 158 → 214 células andáveis (+35%),
  17,7 → 24,1 salas (+36%), 34 → 47 células de corredor (+36%, a mesma parte do andar), caminho da cabine
  à escada de 13,4 → 17,0 células. A altura 14 deixa o miolo sempre fundo o bastante para o transversal
  leste-oeste (sorteado em metade das plantas): o andar maior ganha voltas, não corredores mais longos.
  O que dependia do tamanho e foi acertado: trechos de luminárias queimadas proporcionais ao corredor
  (`DeadStretchCells`, um a cada 23 células) e a 9 células da cabine (eram 8); trecho da onda de luzes
  de no máximo 10 luminárias (`LevelBuilder.MaxWaveRun`: o lado de 12 do anel vira dois, e o andar ganha
  trechos e interruptores na proporção: nas 8 seeds da conferência, de 5,25 para 7,0 trechos e de 5,0 para
  6,9 interruptores por planta); pilhas uma a cada ~33 células
  (6 em vez de 4); alarmes a 7 passos uns dos outros (eram 6), a 6 da cabine (5) e a 5 da escada (4);
  o Ascensorista fica 85 s no andar (75); sombra das luminárias com até 80% do atlas em cache (60%),
  para seguir em 128 px com até ~200 luminárias acesas; `lightFadeDistance` de 30 para 28 m, porque o
  que custa por quadro são as luzes a menos dessa distância da câmera (o HDRP descarta as outras do laço
  de luz), e com 30 m o andar maior pegava ~6% mais delas em média (13% no pior lugar); com 28 m fica
  abaixo do antigo. Objetos desenhados: 78 → 82. O que não mudou por ser relativo ao jogador:
  onde o Zelador nasce, a distância dos armários que batem, a ronda perto do jogador.

**Falta:** jogar à mão (os números dos dois inimigos e da onda são de partida); os modelos do Blender
do cenário (as peças da seção 4 hoje são caixas e tubos em código); as ideias 3 e 4 da seção 6
(cadeiras viradas, telas que ligam). A planta, o objetivo e o interruptor entre host e cliente estão
conferidos em dois editores (`NetTests`).

**Decisões tomadas em 17/11/2026** (seção 9): viga da escada grafite; OSB mantido, mas dessaturado;
saída pela porta corta-fogo no patamar da escada; por enquanto sem inimigo, como as Piscinas (depois:
a entidade do Level 0 em 19/11/2026 e, desde 21/11/2026, o Zelador e o Ascensorista).

### O que os vídeos mostram

| Vídeo | Duração | Percurso |
|---|---|---|
| 1 | 25 s | Corredor de teto técnico (luminárias tubulares atravessadas, eletrocalhas, conduítes, cano vermelho) → porta branca com visor → sala de aula larga, piso de madeira, forro branco, mesas brancas em fileiras, janelas no fundo → volta ao corredor: divisórias de vidro fosco em esquadria branca de um lado, quadros elétricos, gabinetes baixos de OSB e prateleiras abertas brancas do outro → sala técnica pequena |
| 2 | 7 s | Saguão dos elevadores: portas de inox lado a lado, totem de andares, teto de ripas escuras com pendentes pretos, mesas quadradas brancas com cadeiras de arame, banco estofado comprido junto a um janelão com guarda-corpo de tubos e a cidade lá fora |
| 3 | 7 s | Dentro da cabine: portas de inox abrindo para o saguão; a cabine por dentro, inox escovado nas quatro paredes, corrimão, piso de granito, spots no teto |

**Proporções medidas a olho** (pessoas e portas de 2,1 m como régua): corredor de 2,8 a 3,2 m livres;
laje a ~3,8 m nos corredores, com dutos e eletrocalhas entre 3,0 e 3,6 m e as luminárias penduradas a
~2,8 m; forro das salas a ~2,9 m; ripas do saguão a ~3,2 m; porta de elevador de ~1,1 x 2,2 m; cabine
grande (de maca), ~2,2 x 2,6 m por dentro.

---

## 1. A fase em dois parágrafos

**O Escritório é o andar de um prédio comercial e universitário que acabou de fechar e nunca mais vai
abrir.** Tudo está ligado: luminárias, ar-condicionado, geladeiras, telas. Não há ninguém. Os
corredores são retos, compridos e iguais, e a única cor forte são os armários amarelos que se repetem
pelo caminho, como se marcassem um trajeto que não leva a lugar nenhum. A luz é fria e uniforme, o
som é o sopro do ar-condicionado e o zumbido dos reatores; de vez em quando, o "plim" de um elevador
chegando a um andar onde ninguém chamou.

O que a diferencia das outras fases: o Level 0 é um labirinto de cômodos iguais, o Level 1 é uma loja
mobiliada e o Level 2 é água e luz natural. O Escritório é **arquitetura de verdade**, com função:
corredor, sala, saguão, cafeteria, escada. Por isso dá para se orientar (os espaços são diferentes) e
ao mesmo tempo se perder (cada tipo se repete demais). O medo vem da ordem: cadeiras alinhadas que
mudam de lugar, uma fila de luzes que apaga em sequência, um vão de escada de onde se vê andares
iguais em cima e embaixo, sem fim.

---

## 2. A planta em regras (grade de células de 4 m)

Gerador próprio, como o das Piscinas: um `OfficeGenerator` em C# puro, só `System.Random(seed)`, com
`Validate` que sorteia de novo até passar e um `Hash` para comparar host e cliente.

### 2.1 Tamanho e estrutura

- **16 x 14 células** (64 x 56 m; era 14 x 12, 56 x 48 m, até 23/11/2026). O Level 1 tem 10 x 10;
  esta fase aguenta mais porque é feita de peças repetidas.
- **Estrutura de prédio, não de labirinto:** um **anel de corredores** em volta de um miolo, mais 1 ou 2
  corredores transversais que cortam o miolo. Os corredores ficam em linhas e colunas inteiras da grade
  e dividem o resto em **quarteirões** retangulares. Cada quarteirão vira uma ou mais salas.
- Corredor tem **1 célula de largura**: 4 m de eixo a eixo, com armários (0,45 m) num lado e divisória
  de vidro no outro, sobrando ~3 m livres, como nas fotos.

### 2.2 Tipos de espaço

| Espaço | Tamanho (células) | Quantos por mapa | Onde e como liga |
|---|---|---|---|
| **Cabine do elevador (início)** | 1 (meia célula útil: 2,2 x 2,6 m) | 1 | Na parede de fundo do saguão; única saída é a porta para ele |
| **Saguão dos elevadores** | 3 x 2 | 1 (ou 2, ver seção 6) | Encosta no anel; 3 ou 4 portas de elevador numa parede longa; abre para o corredor em 1 ou 2 lados |
| **Corredor** | 1 de largura, trechos de 3 a 12 | o anel + 1 ou 2 transversais | Liga tudo; ~40% das células |
| **Sala de aula / reunião** | 3 x 2 ou 4 x 3 | 4 a 6 | Dentro dos quarteirões; 1 ou 2 portas para o corredor; janelas no fundo só se a sala encosta na borda do mapa |
| **Sala pequena (técnica, laboratório)** | 1 x 1 ou 2 x 1 | 3 a 5 | Sobras dos quarteirões; 1 porta; metade delas escura |
| **Cafeteria** | 3 x 3, aberta para o corredor | 1 | A 2 ou 3 células do saguão (é o segundo marco depois do início) |
| **Área de estar** | 2 x 2, sem parede para o corredor | 1 ou 2 | Num cruzamento do anel com um transversal |
| **Átrio da escada** | 2 x 3 (vão aberto) | 1 | Longe do início (metade oposta do mapa), com guarda-corpo em volta e acesso pelo patamar |
| **Saída** | 1 | 1 | Ver seção 3 |

### 2.3 Regras de ligação (entram no `Validate`)

1. Todo espaço com piso é alcançável a pé a partir da cabine.
2. Toda sala tem pelo menos uma porta para um corredor (nunca só para outra sala).
3. Do saguão até a saída, o caminho mais curto passa por **pelo menos 12 células de corredor** e
   **não passa** pela cafeteria nem pela área de estar (elas são desvios e esconderijos).
4. Nenhum trecho reto de corredor passa de 12 células sem um cruzamento ou uma porta (corredor
   comprido demais cansa em vez de assustar).
5. O átrio fica a pelo menos 6 células de distância (pela grade) do saguão.
6. Móvel nunca fecha passagem: mesma cruz livre de 1,2 m no meio da célula da `PropLayer`.

---

## 3. A escada: andar único ou vários?

| Opção | Prós | Contras |
|---|---|---|
| **A. Vários andares jogáveis** | É o que o prédio real é; o vão faz sentido; perseguição em altura | Grade 3D: `LevelData`, `LevelNav`, entidade, buracos e mapa 2D são todos de um andar só. Luzes e geometria multiplicadas pelo número de andares. Muito trabalho antes de ter graça |
| **B. Escada só cenário** | Barato; o vão vira vista (andares iguais em cima e embaixo, sem fim) | Promete um lugar que não se pode ir; o jogador tenta descer e bate numa barreira invisível |
| **C. Um andar jogável, e a escada é a saída** | Mantém a grade 2D; o vão é cenário **e** objetivo: a saída fica no fim da escada, um lance abaixo | Só um lance de escada de verdade; precisa de um volume abaixo do piso só naquela região |

**Recomendação: C.** O andar inteiro é jogado em 2D, como as outras fases. No átrio, a escada desce
um lance até um patamar intermediário, onde está a **porta corta-fogo da saída de emergência** (a
`LevelExit`), com a placa verde de saída acesa em cima. Os lances que continuam para cima e para baixo
do patamar são cenário: terminam em grade de enrolar fechada (para cima) e no escuro (para baixo). Os
outros andares vistos pelo vão são cascas: borda da laje, guarda-corpo, uma fileira de luminárias e
neblina, sem nada dentro.

Isso também resolve a sinalização: no Level 1 as setas viraram placas de curva; aqui, as **placas de
saída de emergência** (só pictograma, sem texto) pregadas nas paredes apontam o caminho seguro até o
átrio.

---

## 4. Peças 3D, da mais importante para a menos

Medidas em metros (largura x profundidade x altura). Materiais na paleta da seção 5.1. As peças de
arquitetura (paredes, pisos, forro) saem do construtor em código; a lista abaixo é o que vai para os
lotes do Blender.

| # | Peça | Medidas | Material |
|---|---|---|---|
| 1 | **Luminária tubular pendente** | 2,4 x 0,12 (diâmetro), cabos de 0,8 | Difusor branco leitoso (emissivo), tampas de alumínio |
| 2 | **Bloco de armários amarelos** (módulo de 3 x 3 portas, com fechadura e número em relevo sem texto legível) | 1,2 x 0,45 x 1,35 | Portas amarelas laminadas, caixa cinza-claro, rodapé cinza-escuro |
| 3 | **Teto técnico** (módulo por célula: reto, canto, T, cruz): eletrocalha perfurada, 4 a 6 conduítes, duto galvanizado retangular, cano de incêndio, tirantes | 4 x 4, entre 3,0 e 3,6 de altura | Aço galvanizado, alumínio, cano vermelho-escuro fosco |
| 4 | **Divisória de vidro em esquadria** (módulo, com e sem porta de giro) | 1,0 x 0,08 x 2,8 | Vidro fosco cinza-esverdeado, perfil branco de 5 cm |
| 5 | **Porta de sala** com visor vertical estreito | 0,9 x 0,05 x 2,1 + batente | Branca, maçaneta de inox |
| 6 | **Porta de elevador com batente** | 1,1 x 0,2 x 2,2 (folhas de 0,55) | Inox escovado |
| 7 | **Cabine do elevador** (paredes, corrimão, painel de botões, spots) | 2,2 x 2,6 x 2,5 por dentro | Inox escovado, piso de granito cinza, botões com luz âmbar |
| 8 | **Mesa comprida branca** (encaixa em fileira de até 4) | 1,6 x 0,7 x 0,75 | Tampo branco, pés de aço branco |
| 9 | **Cadeira de tela com rodinhas** | 0,6 x 0,6 x 0,95 | Tela preta, assento grafite, base preta |
| 10 | **Escada metálica**: lance de 10 degraus, patamar, viga lateral, guarda-corpo de tubos com tela | lance de 1,4 de largura; degrau 0,28 x 0,17 | Granito cinza, fita preta, ponteiras amarelas, inox, tela metálica, viga (cor: decisão 1) |
| 11 | **Quadro elétrico, câmera domo, extintor, lixeira dupla, placa de saída** | 0,6 x 0,2 x 0,8; domo 0,15; etc. | Cinza, branco, vermelho fosco do extintor, placa verde emissiva |
| 12 | **Teto de ripas escuras + pendente em meia-esfera** (saguão) | ripas 0,05 x 0,25 a cada 0,15; pendente 0,6 de diâmetro | Grafite fosco; pendente preto por fora, branco por dentro |
| 13 | **Gabinete baixo e prateleira aberta** (corredor) | 1,2 x 0,5 x 0,9; 1,2 x 0,4 x 2,0 | OSB (decisão 2) e branco |
| 14 | **Cafeteria**: balcão, banqueta alta, mesa redonda, cadeira de madeira curvada, geladeira vitrine, painel de senha | balcão 4,0 x 0,9 x 1,1; geladeira 0,7 x 0,7 x 2,0; painel 0,4 x 0,15 | OSB, inox, vidro, LED vermelho |
| 15 | **Tela de parede e projetor de teto** | tela 1,25 x 0,72; projetor 0,4 x 0,3 | Preto; a tela usa o material de estática (seção 6) |
| 16 | **Janela com persiana** | módulo 1,4 x 1,8 | Caixilho cinza, persiana branca, vidro com o `DaylightPanel` |
| 17 | **Sofá curvo, pufe grande, vaso com planta** | sofá 3,6 de arco; pufe 1,2 x 0,45; vaso 0,4 x 0,4 x 1,2 | Cinza (sofá e pufe), vaso de concreto, planta verde dessaturada |

---

## 5. Iluminação

### 5.1 Paleta

Cinza das paredes (#8C8C88), concreto do piso em placas grandes (#7A7873), inox escovado, teto escuro
(#2A2B2D). **Armários amarelos** (#E8B21C) são a única cor forte. Ignoradas: o vermelho e a madeira dos
armários reais, os sofás amarelo e vermelho e os pisos verde, roxo e vermelho.

### 5.2 Luz de cada espaço

Intensidade relativa: 1,0 = corredor.

| Espaço | Tipo | Cor | Intensidade | Observação |
|---|---|---|---|---|
| **Corredor** | Luminária tubular a cada 4 m, atravessada | Branco frio, ~5000 K | 1,0 | Spot largo para baixo (como no Level 0) com o tubo emissivo; sombra só nas 4 mais próximas |
| **Sala de aula** | Linhas embutidas no forro, 2 por célula | Branco neutro, ~4000 K | 1,2 (o lugar mais claro) | Metade das salas acesas, metade com só a janela |
| **Saguão** | Pendentes em meia-esfera, cone fechado | Neutro-quente, ~3500 K | 0,7 | O teto de ripas fica escuro acima das luzes |
| **Cafeteria** | Linha luminosa sobre o balcão + spots de trilho | ~3500 K | 0,8 | Geladeiras e painel de senha emissivos fazem o resto |
| **Cabine do elevador** | 4 spots embutidos | Neutro | 1,1 | Pequena e clara: o refúgio do começo |
| **Átrio da escada** | Luz natural fria vindo de cima (`DaylightPanel`) | Branco-azulado | 0,9 em cima, 0 embaixo | Escurece andar por andar para baixo |
| **Área de estar** | 1 pendente | ~3000 K | 0,5 | Penumbra, bom esconderijo |

### 5.3 Áreas escuras

- **Salas pequenas:** metade apagadas.
- **Trechos de corredor:** de 3 a 5 luminárias queimadas seguidas, longe do início.
- **O fundo do vão da escada.**
- **Uma cabine de elevador parada** com a porta entreaberta e a luz de dentro piscando.
- **O apagão** (`LightsOut`) continua, pelo relógio da sessão. Aqui ele vem em onda (seção 6).

---

## 6. Ideias de tensão

1. **A onda de luzes.** As luminárias de um corredor apagam uma a uma, em fila, vindo na direção do
   jogador, a ~1 por segundo. Antes de chegar nele, param e acendem de volta, todas de uma vez. Pelo
   `GameSession.SessionSeconds`, igual em todas as máquinas. Substitui o apagão comum desta fase.
2. **O elevador que chega sozinho.** "Plim": uma das portas do saguão (ou de um segundo saguão no
   meio do mapa) abre para uma cabine vazia e iluminada, espera e fecha. Quem entra antes de fechar
   sai no **outro saguão** do mapa: um atalho ou uma armadilha, dependendo de onde está a coisa que
   caça.
3. **Cadeiras fora do lugar.** Ao voltar a uma sala de aula já vista, todas as cadeiras estão viradas
   para a porta. Troca só quando ninguém está olhando para a sala (a câmera de nenhum jogador vê a
   sala), decidida pela seed e pelo relógio da sessão.
4. **As telas ligam.** Telas e projetores de uma sala acendem com estática quando alguém entra. Às
   vezes, em vez de estática, mostram em baixa resolução a imagem de uma câmera domo do corredor:
   o jogador se vê de costas, da sala onde acabou de estar.
5. **Os armários batem.** Uma sequência de portas de armário batendo ao longo de um corredor, longe,
   chegando mais perto. É só som, mas o inimigo da fase (quando existir) ouve o mesmo barulho. *Feito em
   20/11/2026* (`LockerBangs`); o Ascensorista vai até lá.

---

## 7. O que deixar de fora para rodar bem

- **Teto técnico em módulos fundidos:** uma malha por tipo de célula (reto, canto, T, cruz), com
  instanciamento. Nada de cano ou conduíte como objeto separado.
- **Armários em blocos de 1,2 m numa malha só**, portas inclusas. Só os poucos que abrem (se um dia
  forem esconderijo) têm porta separada.
- **Luzes sem sombra**, exceto as 4 mais próximas do jogador (o padrão do Level 0), e o mesmo fade a
  25 m das outras fases. Nada de luz de área em tempo real.
- **Vidro sem refração:** o vidro fosco das divisórias é opaco-translúcido, sem SSR nem várias camadas
  de transparência umas atrás das outras.
- **Inox sem reflexo em tempo real:** smoothness alto e uma reflection probe assada por cabine e uma
  no saguão.
- **Andares do vão como cascas:** borda de laje, guarda-corpo e uma fileira de luminárias emissivas
  sem luz real, apagados pela neblina.
- **Janelas sem cidade:** vidro com `DaylightPanel` (branco frio), nunca a vista real das fotos.
- **Cadeiras e mesas limitadas por sala** (uma sala de 4 x 3 com 40 cadeiras pesa): fileiras com
  buracos e LOD simples.
- **Nenhum texto legível, logotipo, cartaz ou pessoa** das fotos.

---

## 8. Ordem de construção

1. **Responder a seção 9.**
2. `OfficeGenerator` + `Validate` + `Hash`, com teste da planta em várias seeds e o mapa 2D, sem
   geometria.
3. Construção em código (`LevelBuilder.Office.cs`): paredes, piso, forro, teto técnico provisório,
   portas como vão, luminárias. Cena `Level3_Test` por um `Level3Setup`, entrada no `LevelCatalog` e na
   build (escondida pelo `LevelProgress`).
4. Teste em Play no estilo do `Level1PlayTest`: nasce na cabine, anda o caminho até a saída.
5. Lote do Blender `escritorio.py` com as peças 1 a 9; depois 10 a 17.
6. Mobília pela `PropLayer` (ou uma camada própria): fileiras nas salas, cafeteria, área de estar.
7. Luz, Volume próprio, sons (ar-condicionado, reatores, elevador).
8. Tensão da seção 6, na ordem 1, 2, 5, 3, 4.

---

## 9. Decisões em aberto

1. **Verde-água da viga da escada:** o prompt pede a viga verde-água, mas também diz que a única cor
   forte são os armários amarelos. Sugestão: viga **grafite** e a única outra cor é o verde da placa
   de saída (que é luz, e é o guia).
2. **OSB dos gabinetes e do balcão da cafeteria:** é madeira clara, quase uma cor. Manter
   dessaturado, ou trocar por laminado cinza?
3. **Saída:** porta corta-fogo no patamar da escada (recomendado, seção 3) ou um elevador "de saída"
   que só chega num momento certo?
4. **Inimigo:** a fase tem um próprio, usa a entidade do Level 0 ou começa sem nenhum (como as
   Piscinas)? **Decidido em 19/11/2026 (para revisão): a entidade do Level 0** (D6 de
   `plano-de-acao.md`), com o alarme de incêndio como objetivo (D7). **Decidido de novo em 20/11/2026,
   pelo usuário: dois inimigos próprios**, o Zelador e o Ascensorista.
5. **Referências:** chegaram 13 fotos, não 14, e a ordem de envio não bate com a tabela do prompt
   (lá, 1, 2 e 5 são escadas; aqui a 1 é a área de estar). Este projeto cita as fotos pelo que
   mostram, não pelo número.
