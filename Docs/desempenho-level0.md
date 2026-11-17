# Desempenho do Level 0 — o que foi medido e o que mudou

Registro da medição de 11/11/2026 e da mudança que saiu dela. Para medir de novo:
menu **BackRooms > Medir desempenho do Level 0** (ou, com o editor fechado,
`-batchmode -executeMethod BackRooms.EditorTools.Level0Benchmark.BatchRun -benchOut <pasta>`).
Leva uns 15 minutos e grava `desempenho.txt` e uma foto por configuração.

## Como a medição funciona

`Assets/_Game/Scripts/Editor/Level0Benchmark.cs` entra no Play, hospeda a fase com a seed 1998,
segura o monstro e o apagão, e anda com a câmera do jogador pelo caminho seguro inteiro, meio
metro por quadro (504 quadros), balançando o olhar para os lados. Cada quadro é desenhado em
1920 x 1080 e só é dado por terminado quando a placa de vídeo entrega um pixel de volta. A mesma
caminhada é repetida em cada configuração.

O número é **processador + placa somados, um atrás do outro**. Serve para comparar uma
configuração com a outra; não é a taxa de quadros do jogo (jogando, os dois trabalham ao mesmo
tempo).

Três coisas que deram errado antes de a medição prestar, para ninguém repetir:

- **Fora do Play não serve.** Com a fase montada no modo de edição, o tempo por quadro cresceu de
  1 s para 31 s ao longo da caminhada. Tem que ser dentro do Play.
- **Pular de um ponto para outro não serve.** Cada pulo faz dezenas de luminárias calcularem a
  sombra de uma vez, o que não acontece andando.
- **Em modo batch o Unity não desenha a câmera sozinho.** O teste chama `camera.Render()` numa
  textura. E duas medições ao mesmo tempo na mesma placa não valem: uma atrasa a outra.

## Resultado (antes da mudança)

RTX 4070 Laptop, perfil High Fidelity, 152 luminárias acesas, 426 marcas.

| Configuração | Média | p95 | Pior quadro | Contra a base |
|---|---|---|---|---|
| Primeira passada (sombras sendo calculadas) | 35,9 ms | 49,0 | 350 ms | — |
| Como estava, sem lanterna | 36,2 ms | 50,5 | 103 | −3% |
| **Como estava, com lanterna (base)** | **37,3 ms** | 47,8 | 136 | — |
| Luminárias só até 20 m (corte de distância do HDRP) | 33,2 ms | 41,5 | 139 | −11% |
| **Luminárias fora da névoa volumétrica** | **27,8 ms** | 36,8 | 137 | **−26%** |
| Sem névoa volumétrica nenhuma | 25,6 ms | 34,1 | 94 | −31% |
| Sem marcas (decalques) | 35,4 ms | 46,3 | 72 | −5% |
| 20 m + fora da névoa + marcas até 25 m | 28,4 ms | 37,5 | 201 | −24% |
| Luminárias só até 20 m (desligando as outras) | 34,4 ms | 45,0 | 88 | −8% |
| Só a lanterna, nenhuma luminária | 25,2 ms | 37,1 | 87 | −32% |

O que isso diz:

- **A luz direta das luminárias é quase de graça.** Apagar as 152 de uma vez dá o mesmo ganho que
  só tirá-las da névoa. O caro era cada luminária acender a névoa volumétrica.
- **Cortar por distância rende pouco** (8 a 11%) e escurece o fundo dos corredores. Desligar e
  religar ainda refaz a sombra de cada luminária que volta.
- **Iluminação pré-calculada (baked) não resolveria**: o que pesa não é a luz nas paredes, e o
  mapa é gerado ao hospedar, então não existe cena pronta para pré-calcular.
- **Uns 25 ms são custo fixo** que nenhuma dessas configurações mexe (perfil gráfico,
  pós-processamento, passada da filmadora, resolução). Ainda não foi medido por partes.

Comparando as fotos: tirar as luminárias da névoa é quase imperceptível; o corte em 20 m se nota
(o fim do corredor fica mais escuro); só a lanterna muda a cara do jogo.

## O que mudou

Em `LevelBuilder.CreateCeilingLight`, a luminária do teto passa a ter
`affectsVolumetric = lampsLightFog`, e `lampsLightFog` nasce **desligado**. A névoa continua
existindo e a lanterna continua desenhando o facho nela. Para voltar ao que era, ligue
**Lamps Light Fog** no `LevelBuilder` da cena.

Vale para qualquer fase montada pelo `LevelBuilder` (Level 0 e Level 1).

Efeito colateral bom: no HDRP, uma luz com o dimmer em zero só sai da lista de luzes se também
não afetar a névoa. Agora a luminária apagada pelo apagão sai de verdade da conta.

## Conferido com a mudança aplicada

A mesma medição, rodada de novo com a mudança no código (a "base" agora é o jogo novo):

| Configuração | Média | p95 | Pior quadro |
|---|---|---|---|
| **Atual, com lanterna** | **28,9 ms** | 39,2 | 237 |
| Como era (luminárias acendendo a névoa) | 38,6 ms | 52,4 | 234 |
| Sem névoa volumétrica nenhuma | 26,0 ms | 35,6 | 168 |
| Luminárias só até 20 m (corte do HDRP) | 27,5 ms | 33,7 | 140 |
| Sem marcas (decalques) | 24,7 ms | 29,4 | 60 |
| Só a lanterna | 27,9 ms | 50,1 | 325 |

Um quarto a menos por quadro, igual ao previsto. Com as luminárias fora da névoa, o que sobra
para ganhar com elas é pouco: cortar em 20 m ou apagar todas fica dentro da variação de uma
rodada para outra (uns 2 ms; os piores quadros variam bem mais que isso). As marcas custaram
2 ms numa rodada e 4 ms na outra: passam a ser a maior fatia das que foram medidas, mas pequena.

O smoke test solo passou com a mudança (0 erros; apagão, gravação, monstro e buracos conferidos).
O de dois editores não foi rodado: a mudança não mexe em rede.

## O que ficou de fora

- **Engasgo da primeira passada** (pior quadro de 350 ms): cada luminária calcula a sombra em
  cache na primeira vez que entra no alcance, sem limite por quadro. Daria para calcular todas ao
  carregar a fase.
- **O custo fixo de ~25 ms**: medir perfil gráfico, pós-processamento, passada da filmadora e
  resolução, um de cada vez.

## Rodada de 12/11/2026: distâncias, malhas juntas e vazamento de luz

- **Vazamento de luz.** A sombra das luminárias sumia a 50 m e a luz só a 70 m: nesse intervalo a
  luminária iluminava sem sombra, atravessando as paredes. Agora a luz some a 45 m
  (`LevelBuilder.lightFadeDistance`) e a sombra vai 20% além. A resolução baixa da sombra (64
  pontos por face, o que cabe no atlas para 152 luminárias) foi testada e não é a causa: numa
  célula sem luminária, sombra de 256 pontos muda 4% da luz que chega, e as fotos saem iguais.
- **Calhas e grelhas** viram uma malha cada (eram um objeto por luminária e dois por grelha).
  Marcar como "static" não tem efeito num mapa montado ao hospedar; juntar as malhas é o
  equivalente. As luzes já tinham a sombra em cache, que é o que "estático" daria a elas.
- **Marcas** somem a 25 m (`decalDrawDistance`), as setas da saída a 40 m (`arrowDrawDistance`).
- **Visão** até 85 m, não menos: 35 de 40 seeds têm um corredor reto de mais de 60 m (o maior, 80 m).
- **Medido:** nenhuma diferença fora da variação entre rodadas. Com as distâncias antigas (luz 70 m,
  marcas 60 m, visão 150 m) a caminhada deu 20,4 ms; com as novas, entre 19,5 e 24,3 ms conforme
  a passada. O quadro caiu de 27 para cerca de 20 ms desde a rodada anterior por causa dos ajustes
  do volume (oclusão de ambiente e céu desligados). O que sobra de caro são as luminárias: só com
  a lanterna o quadro custa 12 ms.

- **Luz a 25 m (12/11/2026, segunda mudança do dia).** `lightFadeDistance` caiu de 45 para 25 m, e
  com isso a sombra some a 30 m. O fundo dos corredores fica mais escuro a partir de 25 m. Vale para
  os três níveis (o Level 2 usa o mesmo campo nas claraboias e nos janelões). Não foi medido nem
  testado.
