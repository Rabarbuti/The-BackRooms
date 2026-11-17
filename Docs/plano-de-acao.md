# Plano de ação

O que fazer a seguir, em que ordem, e o que precisa ser decidido antes. Escrito em 17/11/2026.

As mecânicas citadas aqui estão explicadas em `mecanicas.md` (com os códigos `PC.1`, `P0.1`...) e
nos projetos das fases (`level1-mostruario.md`, `level2-piscinas.md`, `plano-coop.md`).

---

## Onde estamos

| Fase | Dá para jogar | Ameaça | Objetivo |
|---|---|---|---|
| Level 0 — o labirinto | Sim | A entidade (caça pelo barulho), buracos, apagão | Não: chegar à porta basta |
| Level 1 — o Mostruário | Sim, versão de teste mobiliada | O Capitão (anda só quando não é visto iluminado) | Não |
| Level 2 — As Piscinas | Sim, versão de teste com nado | **Nenhuma** (sem maré, sem ralo) | Não |

O que mais falta é **algo a fazer** em cada fase (objetivo) e **algo a temer** nas duas últimas.
O plano ataca isso primeiro, na ordem do que mais muda o jogo pelo menor esforço.

---

## Como trabalhar

- **Uma branch curta por etapa** (ou por parte de etapa), saindo da `main` atualizada e voltando
  por PR assim que os testes passam. Depois do merge, apaga-se a branch.
- **Cada etapa termina com os testes que o `CLAUDE.md` pede** para o que ela mexe, e o PR diz o
  que passou por máquina e o que não foi testado à mão.
- **Etapa pronta = jogada à mão pelo menos uma vez**, de preferência em dois jogadores. Os testes
  por máquina conferem regras; se a mecânica dá medo ou graça, só jogando.
- Quando uma etapa termina, o documento da fase passa a marcá-la como feita, e a proposta usada sai
  de `mecanicas.md`.

---

## Etapa 1 — O Capitão no Level 1

**Por que primeiro:** o Level 1 não tem ameaça nenhuma, e o Capitão é o que mais está pronto: o
corpo, a animação e o modelo já existem (`CaptainBody`), e o ouvido pode vir do `EntityBrain`.
Projeto: `level1-mostruario.md`, seção 5.

**Primeira rodada** (branch `capitao-cerebro`) — **feita em 17/11/2026** (`CaptainBrain`,
`CaptainAudio`, `Level1CaptainTest`; detalhes em `historico.md`):

1. **Pôr o Capitão na fase.** Um prefab de rede como o `Entity` (registrado pelo Netcode em
   `DefaultNetworkPrefabs.asset`, nunca por código) e um diretor que decide onde e quando ele
   aparece, no molde do `EntityDirector`. A cena do Level 1 é montada pelo `Level1Setup`.
2. **Ouvido:** a mesma conta de barulho do `EntityBrain` (parado, agachado, andando, correndo).
3. **Congela quando visto e iluminado.** O host confere, a cada quadro, se algum jogador o tem na
   tela com linha de visada **e** se ele está sob uma luminária acesa ou no facho de uma lanterna.
   Se sim, ele para onde está, mesmo no meio do passo. Os raios descartam o próprio jogador (o
   padrão de `PenTool.TryAim`).
4. **Apagão é a hora dele:** no escuro, sem lanterna nele, anda livre e em arrancadas (5,2 m/s por
   1,5 s); fora disso manca a 1,7 m/s.
5. **Encarar por mais de 6 s:** o chuvisco da filmadora cresce até tapar a imagem (decisão D2).
6. **Captura:** igual à da entidade (NO SIGNAL e volta para a sala inicial).
7. **Som:** "TOC… arrasta" da perna de pau, sintetizado em código como os sons da entidade, e
   exportável em WAV pelo menu para ouvir sem jogar.
8. **Só pisa onde o `LevelNav` deixa:** fora da sala inicial.

**Teste novo** (`Level1CaptainTest`): ele anda quando ninguém olha; congela quando é olhado com
luz; anda quando é olhado no escuro; congela no facho da lanterna durante o apagão; pega quem fica
parado na frente dele no escuro.

**Testes:** `Level1Setup.BatchCheck`, `Level1PlayTest`, `Level1CaptainTest`, fumaça do Level 0,
`LevelSelectTest` e dois editores (o Capitão é objeto de rede).

**Segunda rodada** (outra branch): totens e a troca de lugar no apagão, guarda-roupa como
esconderijo (as portas já são objetos com a origem na dobradiça), plástico-bolha.

---

## Etapa 2 — Objetivo comum, começando pelos disjuntores do Level 0

**Por que agora:** nenhuma fase tem objetivo, e os três objetivos planejados têm a mesma forma
(proposta `PC.1` de `mecanicas.md`): *N pontos distantes ativados dentro de uma janela de tempo*.
Um componente só serve às três fases.

**Primeira rodada** (branch `objetivo-disjuntores`):

1. **O ponto de objetivo:** um componente posto pela seed, ativado segurando E por alguns
   segundos, que faz barulho enquanto é ativado (o inimigo ouve).
2. **A regra da janela:** os pontos precisam estar ativos ao mesmo tempo, dentro de N segundos. O
   host confere. **Sozinho**, cada ponto fica ativo depois de ativado.
3. **Estado pela rede**, reenviado a quem entra depois (como `DrawingBoard.SendAll`).
4. **A porta de saída fica trancada** até o objetivo ser cumprido; o painel da filmadora diz o
   que falta.
5. **Level 0:** disjuntores na parede que zumbem de longe (a pista é o som). Quantidade =
   jogadores + 1, no mínimo 2 (`plano-coop.md`, seção 2.3).
6. **A seed não pode mudar o mapa que já existe.** Os pontos são sorteados por um
   `System.Random` próprio, depois de tudo, e o teste de fumaça confere que o hash do mapa da
   seed 1998 continua o mesmo.

**Teste:** o teste de fumaça ganha "a porta não abre antes; arma os disjuntores; a porta abre", e
o `LevelSelectTest` passa a armar os disjuntores antes de abrir a porta. Dois editores: cada um
arma um disjuntor dentro da janela; fora da janela, não vale.

**Depois, com o mesmo componente:**

- **Painel de código do Level 0:** um jogador lê o código num ponto do mapa, outro digita no
  painel (`plano-coop.md`, seção 2.3).
- **Level 1:** o teclado da porta da doca e as três etiquetas vermelhas de LIQUIDAÇÃO
  (`level1-mostruario.md`, seção 6).
- **Level 2:** as válvulas das casas de máquinas (`level2-piscinas.md`, seção 6). Depende da
  Etapa 3 para a drenagem.

---

## Etapa 3 — A maré nas Piscinas

**Por que depois:** é o evento global da fase (no lugar do apagão), mas depende de uma decisão de
desenho (D3) e mexe no que mais quebrou até hoje: sair da água funda.
Projeto: `level2-piscinas.md`, seção 4.3.

**Antes de começar:** decidir D3 (o refúgio seco).

**O que muda no código:**

1. **O nível da água varia com o relógio da sessão** (`GameSession.SessionSeconds`), igual em
   todas as máquinas: 60 s parada, 15 s subindo até +0,9 m, 15 s descendo. Hoje o jogador, os
   passos e a filmadora já leem a altura de `PoolWater.SurfaceY`; o que é fixo é a superfície
   (`LevelData.WaterLevel`), a malha da água (só nas células molhadas) e as regras do gerador.
2. **Água onde hoje é seco:** na maré alta o seco e o raso ficam cobertos, então a malha da água
   tem de existir também sobre eles.
3. **O caminho a pé continua sem nado na maré alta** (regra nova no `PoolGenerator.Validate`).
4. **Aviso:** 10 s antes, ralos borbulhando e um "glub" grave.

**Testes:** todos os das Piscinas (`Level2Setup.BatchCheck`, `Level2PlayTest`, `Level2EscapeTest`
nas seeds de conferência, `Level0ColliderCheck.BatchRunLevel2`, `Level2SoundTest`,
`LevelSelectTest`). O `Level2EscapeTest` precisa passar também na maré alta e com a água subindo.

**Depois:** o ralo e os túneis submersos (seção 5), e a drenagem com as válvulas da Etapa 2.

---

## Pequenas, para encaixar entre as etapas

Baratas, independentes, boas para quem tiver um dia livre:

- **`P0.3` — Lanterna com bateria e pilhas no chão.** Deixa o apagão tenso de verdade.
- **`P2.5` — Marcar onde dá pé.** Já funciona (a caneta não risca debaixo d'água); falta ensinar:
  um bilhete na sala inicial e marcas de exploradores anteriores.
- **Conferir a mobília do Level 1 entre host e cliente** (dois editores), pendente desde que a
  `PropLayer` existe.
- **Som da porta de saída** (abrir, trancada).

---

## Decisões em aberto

Cada uma traz uma recomendação. Quem decidir anota a resposta aqui e no documento da fase.

| # | Decisão | Recomendação |
|---|---|---|
| D1 | Começar pela Etapa 1 (Capitão) ou pela Etapa 2 (objetivo)? | **Decidido (17/11/2026): Etapa 1.** |
| D2 | Encarar o Capitão por 6 s derruba a sanidade, que ainda não existe. O que acontece até lá? | **Decidido (17/11/2026): só o chuvisco**, crescendo dos 6 s aos 9 s até tapar a imagem; a partir daí quem encara deixa de segurá-lo. |
| D3 | Na maré alta até o seco fica com água. Como sobra um refúgio? | **(a) Patamares altos** (+1,2 m) com escada: a maré continua uma só para o mapa todo e `PoolWater.SurfaceY` segue servindo. A (b), maré por região, exige várias superfícies de água. |
| D4 | Quais propostas de `mecanicas.md` entram no plano? | Já: `P0.3` e `P2.5` (baratas). Depois da Etapa 1: `P1.3` (alto-falante). As outras esperam a fase ter ameaça e objetivo. |
| D5 | A entidade do Level 0 aparece também no Level 1, rara? | **Não por enquanto:** ensinar uma regra por fase (decisão 3 de `level1-mostruario.md`). |

---

## Fora deste plano, por enquanto

Ficam para quando as três fases tiverem ameaça e objetivo:

- Sala de contenção e resgate (`plano-coop.md`, seção 2.2).
- Voz por proximidade e walkie-talkie (seção 2.1; depende do Vivox).
- Sanidade e alucinações (seção 2.5).
- Entrar por código de sala (Relay).
- Mudar o desenho do Capitão antes de publicar (decisão 4 de `level1-mostruario.md`).
