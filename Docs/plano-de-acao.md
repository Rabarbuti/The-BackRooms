# Plano de ação: até a entrega

O que falta para o jogo fechar como produto, em que ordem, com o que muda no código, os testes e o
critério de pronta de cada etapa. Reescrito em 19/11/2026 (o plano de 17/11/2026, que levou o
Capitão ao Level 1, está cumprido; o resumo dele ficou no `historico.md`).

As mecânicas citadas estão explicadas em `mecanicas.md` (códigos `PC.1`, `P0.3`...) e nos projetos
das fases (`level1-mostruario.md`, `level2-piscinas.md`, `level3-escritorio.md`, `plano-coop.md`).

**A meta da entrega:** toda fase tem **algo a fazer** (um objetivo que destranca a saída) e **algo a
temer** (uma ameaça própria), e o jogo abre, ensina, termina e se configura como um produto.

---

## Onde estamos (19/11/2026)

Antes deste plano (manhã de 19/11/2026):

| Fase | Ameaça | Objetivo |
|---|---|---|
| Level 0 — o labirinto | A entidade, buracos, apagão | **Não**: chegar à porta bastava |
| Level 1 — o Mostruário | O Capitão, totens, plástico-bolha, barricadas | Teclado na porta, código em três etiquetas |
| Level 2 — As Piscinas | **Nenhuma** | **Não** |
| Level 3 — O Escritório | Só clima (onda de luzes, elevador) | **Não** |

**Depois das etapas 0 a 5 (fim de 19/11/2026)**, todas testadas por máquina, nenhuma jogada à mão:

| Fase | Ameaça | Objetivo | Falta para a entrega |
|---|---|---|---|
| Level 0 | A entidade, buracos, apagão | **Disjuntores** (Etapa 1) | Jogar à mão |
| Level 1 | O Capitão e o resto | Teclado e etiquetas | Jogar à mão e ajustar números |
| Level 2 | **A maré e o ralo na vazante** (Etapa 2) | **Válvulas e drenagem** (Etapa 3) | Jogar à mão; maré e drenagem em dois editores |
| Level 3 | **A entidade do Level 0** (Etapa 4) | **Alarme de incêndio** (Etapa 4) | Jogar à mão; som ambiente |

Em todas: bateria da lanterna e pilhas, sons da porta e tela de opções (Etapa 5). Falta a Etapa 6
inteira e, da Etapa 5, o som ambiente do Escritório e a conferência de planta do Level 2 e do Level 3
em dois editores.

**Depois da rodada "jogando" (20–21/11/2026)**, o usuário jogou e pediu um inimigo próprio por fase
nova, menu de pausa, o jogo em inglês e um Level 2 menos fácil. Tudo testado por máquina, nada jogado
à mão depois:

| Fase | Ameaça | Objetivo | Falta para a entrega |
|---|---|---|---|
| Level 0 | A entidade, buracos, apagão | Disjuntores | Jogar à mão |
| Level 1 | O Capitão e o resto | Teclado e etiquetas (CLEARANCE) | Jogar à mão e ajustar números |
| Level 2 | **The Undertow**, a maré de 1,3 m com alarme, o ralo | Válvulas: **uma do outro lado da água funda** | Jogar à mão (maré pesada demais? Undertow justo?) |
| Level 3 | **The Janitor** (escuro, onda que persegue, interruptores) e **The Attendant** (cego, elevador) | Alarme de incêndio, que corta a energia | Jogar à mão; modelos do cenário |

Em todas: menu de pausa com as tarefas e as teclas (Esc), cartazes de dicas, texto do jogador em
inglês, menu inicial em silêncio (só a música). Feitos também: o som ambiente e os armários que batem
do Escritório, os botões dos elevadores e a conferência de planta, objetivo, pilhas e maré do Level 2
e do Level 3 em dois editores (`NetTests`).

**Depois de 21/11/2026 (tarde) e 23/11/2026:** o jogo ficou **só para um jogador** (o menu esconde Host,
Join e endereço; a rede continua no código) e **quem testa é o usuário, jogando**: as rodadas desde então
passaram só por compilação e pelas conferências dos geradores, não pelos testes em Play nem pelos de dois
editores. Em 23/11: sons de "pato de borracha" tirados, passos refeitos, Escritório de 16 x 14, um som por
monstro no Escritório, o atalho Ctrl + Shift + L que libera as fases (para a build do itch.io) e o
`GDD.md` e o `README.md` na raiz (a rubrica pede GDD atualizado e créditos dos assets). Ver
`historico.md`.

---

## Como trabalhar

- **Uma branch curta por etapa**, saindo da ponta da `level3-escritorio` enquanto o PR 10 (o Level 3)
  não entra na `main`; depois, da `main`. Volta por PR assim que os testes passam.
- **Cada etapa termina com os testes que o `CLAUDE.md` pede** para o que ela mexe, mais um teste novo
  por máquina para cada mecânica nova. O PR diz o que passou por máquina e o que não foi testado à
  mão.
- **A geração continua determinística.** O que a etapa põe no mapa sai de um `System.Random` próprio
  (seed misturada a uma constante), depois de tudo: nenhuma etapa muda o hash da planta das seeds que
  já existem (`LevelGenerator`, `PoolGenerator.Hash`, `OfficeGenerator.Hash`, `PropLayer.Hash`).
- **O que acontece junto** (maré, ralo acordando na vazante, onda de luzes) é conta sobre
  `GameSession.SessionSeconds`; **o que um jogador causa** (armar um ponto do objetivo) vai ao host por
  mensagem nomeada e o host avisa todos, inclusive quem entra depois (o molde do `ExitLock`).
- Quando uma etapa entra: doc da fase, `mecanicas.md`, `historico.md` e `CLAUDE.md` atualizados.

---

## Etapa 0 — Fechar a junção da `main` no Level 3 (feita em 19/11/2026)

A `level3-escritorio` recebeu a `main` (Level 1 completo e lanterna; commit `879a2e9`). Antes de mexer
em qualquer coisa, a bateria inteira roda de novo, um editor de cada vez: os geradores das três fases
(`Level3Setup`, `Level1Setup`, `Level2Setup` `.BatchCheck`), os testes em Play de cada fase, o seletor
de fase e a fumaça do Level 0. Conteúdo regravado pelos geradores vai num commit próprio.

**Pronta quando:** a bateria passa (ou cada falha está explicada e separada do merge) e o texto do
PR 10 diz o resultado.

---

## Etapa 1 — Objetivo comum (`PC.1`) e os disjuntores do Level 0 (feita em 19/11/2026, menos jogar à mão)

Branch `objetivo-disjuntores`.

**O componente** (`SwitchObjective`, em `Scripts/World`): *N pontos distantes ativados dentro de uma
janela de tempo* (`mecanicas.md`, `PC.1`). Serve ao Level 0 (disjuntores), ao Level 2 (válvulas) e ao
Level 3 (acionadores do alarme de incêndio); muda só o tipo do ponto (aparência, som e textos).

1. **Pontos postos pela seed, sem mudar o mapa:** um `System.Random(seed ^ constante)` próprio escolhe,
   depois de tudo, paredes fechadas longe do início e longe umas das outras (distância andando pela
   grade, para uma pessoa só não conseguir armar dois dentro da janela correndo).
2. **Armar:** mirar o ponto (o padrão de mira de `PenTool.TryAim`) e segurar E por alguns segundos.
   Enquanto segura, o jogador faz barulho de corrida (`PlayerNetwork.Working` → o `EntityBrain` ouve).
3. **A janela:** o host guarda a hora em que cada ponto foi armado (`SessionSeconds`); o objetivo se
   cumpre quando há ao mesmo tempo tantos pontos armados quanto pede (2 com até dois jogadores, 3 com
   três ou quatro), cada um valendo pela janela. **Sozinho, a janela some**: ponto armado fica armado.
4. **Rede:** o cliente avisa o host ao terminar de armar; o host confere a distância, arma e manda o
   estado a todos (e a quem entra depois, junto com a seed, como os riscos e o `ExitLock`).
5. **A porta de saída fica trancada** até o objetivo se cumprir (`LevelExit.Locked`); a filmadora diz o
   que falta (`NO POWER`, `1/2`) e, mirando um ponto, o que a tecla faz e quanto falta da janela.
6. **Level 0:** quatro caixas de disjuntor cinza na parede, cada uma zumbindo de longe (a pista é o
   som), com uma lâmpada vermelha que fica verde enquanto o disjuntor vale. Cumprido o objetivo, as
   luminárias piscam e a porta destrava com um estalo.

**Testes:** novo `Level0ObjectiveTest` (porta trancada; mira mostra o aviso; segurar E arma; um ponto
só não destranca com dois jogadores contados; sozinho, dois destrancam; a porta abre; o mapa da seed
1998 tem o mesmo hash de antes); `Level0SmokeTest`; `LevelSelectTest` (cumpre o objetivo antes de abrir
a porta do Level 0); `Level0ColliderCheck`; dois editores (`Level0ObjectiveTest.BatchHost` +
`BatchJoin`: cada um arma um disjuntor dentro da janela e a porta destrava nos dois; quem entra depois
acha o estado).

**Depois, se sobrar tempo antes da entrega:** o painel de código do Level 0 (um lê o código num ponto
do mapa, outro digita), reaproveitando o `ExitLock`.

**Pronta quando:** os testes passam e alguém jogou o Level 0 sozinho até a porta.

---

## Etapa 2 — A maré e o ralo: a ameaça das Piscinas (feita em 19/11/2026, menos o ralo que acorda com barulho e jogar à mão)

Branch `piscinas-mare`. Projeto: `level2-piscinas.md`, seções 4.3 e 5.

1. **A água se move.** A altura vem de uma conta só (`PoolWater.LevelAt(segundos)`), igual em todas as
   máquinas pelo `SessionSeconds`: ciclo de 90 s, 60 s parada, 15 s subindo até +0,9 m e 15 s descendo.
   O jogador, os passos, a filmadora e a caneta já leem `PoolWater.SurfaceY`, que é a altura do objeto
   da água: basta movê-lo. A lâmina passa a cobrir também o seco (escondida sob o piso enquanto a
   água está baixa), para a maré alta aparecer onde inunda.
2. **Aviso:** 10 s antes de subir, um "glub" grave e longe e a água borbulhando nos poços.
3. **O ralo** (seção 5): na **vazante** (os 15 s em que a maré desce) a água sai pelos ralos dos poços,
   e quem nada num poço ou numa funda vizinha é puxado para a grade, mais forte quanto mais perto;
   chegar à grade é afogar. Antes, as lâmpadas do poço piscam e a superfície gira. Além da vazante,
   **barulho perto de um poço acorda o ralo dele** por 8 s: o host ouve (a conta do `EntityBrain`) e
   avisa todos com a hora do despertar; cada máquina puxa o próprio jogador.
4. **A planta não muda** (nenhuma regra nova no `PoolGenerator`; decisão D3 abaixo).

*Feito:* tudo menos o despertar por barulho (o ralo puxa só na vazante; o despertar pelo host fica para
depois da entrega). O puxão é de 1,5 m/s para baixo, que quem segura Espaço (1,6 m/s) vence devagar.

**Testes:** novo `Level2TideTest` (a altura segue o relógio; o seco inunda na maré alta; no raso, de
pé, a cabeça fica fora d'água; na vazante, nadando sobre um poço, é puxado e se afoga; longe, não;
barulho perto do poço acorda o ralo); `Level2Setup.BatchCheck`, `Level2PlayTest`, `Level2EscapeTest`
(seeds de conferência; com a maré parada, como hoje), `Level0ColliderCheck.BatchRunLevel2`,
`Level2SoundTest`, `LevelSelectTest`.

**Pronta quando:** os testes passam e alguém atravessou uma maré inteira jogando.

---

## Etapa 3 — Válvulas e drenagem: o objetivo das Piscinas (feita em 19/11/2026, menos dois editores e jogar à mão)

Branch `piscinas-drenagem`. Projeto: `level2-piscinas.md`, seção 6.

1. **Duas válvulas** (o `SwitchObjective` da Etapa 1, tipo válvula): rodas vermelhas na parede de
   células secas, a pelo menos 8 células uma da outra e fora das salas de poço, postas pela seed sem
   mudar a planta (as casas de máquinas como salas próprias do gerador ficam para depois: mudariam o
   hash de todas as seeds). Girar leva 6 s e faz barulho (acorda os ralos por perto). Janela de 5 s.
2. **Drenagem:** cumprido o objetivo, a água do mapa todo desce 2 m em 30 s (a mesma altura da
   maré, somada) e a maré para; a saída destranca. A porta continua na sala seca da saída (a porta no
   fundo do grande poço fica para quando o gerador puder mudar).
3. Quem entra depois de drenado acha a água baixa (o estado vai com a seed).

**Testes:** o `ObjectiveTest.BatchRunLevel2` faz o papel do `Level2DrainTest` planejado (porta
trancada; as duas válvulas drenam; a água desce; a porta abre), mais os da Etapa 2. Falta o de dois
editores (o cliente vendo a água descer).

**Pronta quando:** os testes passam e alguém drenou jogando.

---

## Etapa 4 — Alarme de incêndio e a entidade: o Level 3 (feita em 19/11/2026; armários em 20/11; a entidade trocada por dois inimigos próprios em 21/11; falta jogar à mão)

Branch `escritorio-alarme`. Projeto: `level3-escritorio.md`, seções 3 e 6.

1. **Objetivo:** a porta corta-fogo da escada só destrava com o **alarme de incêndio** disparado.
   Acionadores manuais vermelhos nos corredores (o `SwitchObjective`, tipo alarme), longe uns dos
   outros; armar os que a regra pede dentro da janela dispara a sirene e as luzes de emergência, e a
   porta da escada destrava.
2. **Ameaça:** a entidade do Level 0 passa a morar no Escritório (decisão 4 de `level3-escritorio.md`:
   a opção "usa a entidade do Level 0"; o `LevelNav` já anda pela grade do Escritório). Ela ouve o
   piso duro e, quando o alarme dispara, corre para o acionador que o disparou: a fuga até a escada é o
   clímax da fase.
3. **Armários batem** (ideia 5 da seção 6): uma sequência de portas batendo num corredor longe, pelo
   relógio da sessão; o inimigo ouve o mesmo barulho e vai até lá. *Feito em 20/11/2026* (`LockerBangs`,
   pela via genérica `WorldNoise`).

*Em 21/11/2026* a entidade do Level 0 saiu do Escritório: o Zelador (só no escuro; a onda de luzes o
traz, os interruptores o tiram) e o Ascensorista (cego, chega de elevador). D6 abaixo.

**Testes:** novo `Level3ObjectiveTest` (porta trancada; armar dispara o alarme; a porta abre; a
entidade aparece e caça), `Level3Setup.BatchCheck`, `Level3PlayTest` (cumpre o objetivo antes de abrir),
`LevelSelectTest`, fumaça do Level 0 (o `EntityBrain` é o mesmo).

**Pronta quando:** os testes passam e alguém zerou o Level 3 jogando.

---

## Etapa 5 — As pequenas, para todas as fases (pilhas, sons da porta e opções feitos em 19/11/2026)

Cada uma é uma branch curta própria, em qualquer ordem:

- **`P0.3` — Lanterna com bateria e pilhas no chão.** `Flashlight.infiniteBattery` desligado nas
  fases; pilhas postas pela seed (`System.Random` próprio), pegas com E por quem chega primeiro (o
  host decide e avisa todos; quem entra depois recebe as que já sumiram). A filmadora já desenha a
  bateria. Teste: a bateria cai acesa; pegar uma pilha recarrega; a pilha some nas duas máquinas.
- **Som e animação da porta de saída:** trancada (chacoalha), destravando (estalo), abrindo (a folha
  gira ao passar de fase).
- **Som ambiente do Escritório:** ar-condicionado e reatores, sintetizados como o `FluorescentHum`.
  *Feito em 20/11/2026* (`OfficeAmbience`).
- **Tela de opções** no menu: volume geral e da música, sensibilidade do mouse, qualidade (os três
  perfis do HDRP), pasta da gravação. Guardadas em `PlayerPrefs`.
- **Conferência de planta entre host e cliente** no Level 2 e no Level 3 (dois editores, como o
  `Level1Setup` já faz para a mobília). *Feito em 20–21/11/2026* (`NetTests`: planta, objetivo, pilhas,
  maré, quem entra depois e, no Level 3, o interruptor apertado pelo cliente).

---

## Etapa 6 — Fechar como produto (fim, bilhetes e build feitos em 19/11/2026)

- **Fim do jogo:** passar a porta do Level 3 mostra um encerramento ("fim da fita") com o tempo de
  cada fase, e volta ao menu. *Feito:* `LevelProgress.RecordTime`/`EndTape` e a tela no `ConnectionMenu`.
- **Bilhete de ensino** em cada sala inicial (o que fazer nesta fase, sem texto em excesso), no molde
  do bilhete do Level 1: disjuntores, válvulas, alarme, e `P2.5` (marcar onde dá pé com a caneta).
  *Feito:* `SwitchObjective.PinNote`. Em 21/11/2026 virou cartaz de dicas em inglês (TIPS; nas
  Piscinas, "MARK THE SHALLOWS") e a instrução foi para o menu de pausa (Esc).
- **Build para Windows** e uma partida inteira jogada nela, sozinho e em dois. *Feito o build*
  (`BuildTool.BatchWindows`, `Builds/Windows/TheBackRooms.exe`, ~40 min na primeira vez); falta jogar nela.
- **Desempenho** medido nas quatro fases (`Level0Benchmark` e o equivalente das outras). *Feito
  numa placa integrada (Iris Xe):* tabela em `desempenho-level0.md`; falta medir numa placa dedicada
  e no perfil "Performant".
- **Decisão 4 de `level1-mostruario.md`** (o desenho do Capitão a partir do filme) resolvida antes
  de publicar fora do grupo.

---

## Fora da entrega

| O quê | Por quê fica de fora |
|---|---|
| Co-op no menu (desde 21/11/2026) | O jogo é entregue para um jogador; a rede por IP continua no código e testada em dois editores até 21/11, mas o menu não a mostra. |
| Voz por proximidade e walkie-talkie (Vivox) | Depende de conta e serviço da Unity, custo por uso e credenciais no projeto; o grupo joga com voz de fora (Discord). As mecânicas não dependem dela. |
| Entrar por código de sala (Relay) | Precisa ativar o projeto no painel da Unity Cloud (ação de quem tem a conta). O IP direto basta para jogar entre amigos; volta ao plano se o jogo for publicado. |
| Sanidade e alucinações | Sistema grande, que mexe em todas as fases e ainda não tem desenho fechado (D2 já trocou o efeito por chuvisco). As ameaças de cada fase já cumprem o papel. |
| Sala de contenção e resgate | Muda a regra de captura de todas as fases e pede uma sala nova em cada gerador. Hoje quem é pego volta ao início, o que funciona sozinho e em grupo. Primeira candidata depois da entrega. |
| Painel de código do Level 0, casas de máquinas e porta no fundo do poço (Level 2), túneis submersos, modelos do Blender do Escritório e das Piscinas | Mudam a planta (hash) ou são acabamento visual: o jogo fecha sem eles. |

---

## Decisões em aberto

Cada uma traz uma recomendação. Quem decidir anota a resposta aqui e no documento da fase.

| # | Decisão | Situação |
|---|---|---|
| D1 | Começar pelo Capitão ou pelo objetivo? | **Decidido (17/11/2026): Capitão.** Feito. |
| D2 | O que acontece ao encarar o Capitão por 6 s, sem sanidade? | **Decidido (17/11/2026): só o chuvisco.** Feito. |
| D3 | Na maré alta até o seco fica com água. Como sobra um refúgio? | **Decidido (19/11/2026), para revisão: sem refúgio novo por ora.** A recomendação era (a), patamares altos de +1,2 m com escada, mas ela muda o `PoolGenerator` e a planta de todas as seeds. **Números desde 21/11/2026** (maré de 1,3 m): na maré alta o raso fica com 1,72 m de água (de pé, a cabeça afunda: nada-se, no território do Undertow) e o seco com 1,12 m (vadeia-se sem nadar e nunca é território dele): o seco é o refúgio. Maré acima de ~1,45 m acabaria com ele. `Level2HighTideTest`: 113 de 113 largados de pé na maré alta chegam ao seco respirando. Se jogando faltar abrigo, (a) volta. |
| D4 | Quais propostas de `mecanicas.md` entram? | `P0.3` (pilhas) na Etapa 5; `P2.5` como bilhete de ensino na Etapa 6. As outras ficam para depois da entrega. |
| D5 | A entidade do Level 0 aparece também no Level 1? | **Não** (uma regra por fase). |
| D6 | Inimigo do Level 3 (decisão 4 de `level3-escritorio.md`)? | **Decidido de novo (20/11/2026, pelo usuário, depois de jogar): dois inimigos próprios**, o Zelador (The Janitor, só no escuro) e o Ascensorista (The Attendant, cego, do elevador). A entidade do Level 0 saiu do Escritório. O Level 2 ganhou o seu, o Undertow. |
| D7 | Objetivo do Level 3? | **Decidido (19/11/2026), para revisão: alarme de incêndio** destrava a porta corta-fogo (combina com a saída já decidida, a porta corta-fogo no patamar). |
| D8 | Válvulas do Level 2 sem casas de máquinas? | **Decidido (19/11/2026), para revisão:** válvulas sem sala própria, e a saída continua a sala seca: as casas de máquinas e a porta no fundo do poço mudariam o hash de todas as seeds. **Desde 21/11/2026** a segunda válvula exige nadar (numa ilha ou na parede sobre a funda); a válvula no fundo de um poço (`WellBottom`) está pronta e desligada. |
| D9 | O estrondo do objetivo cumprido nas Piscinas cai na água quando a última válvula armada é a da parede sobre a funda (seeds 31337, 90210, 2026) e chama o Undertow para quem ainda boia ali. Drama no fim ou injusto? | **Em aberto (21/11/2026):** ficou como está; decidir jogando. Nas seeds 90210 e 2026 essa válvula fica também perto de um ralo (4 e 6 m), que puxa na vazante. |
