# Level 3 — O Escritório: prompt da ideia da fase

Prompt para colar numa IA (Claude, ChatGPT ou outra) junto com as referências, para ela entender
e detalhar a ideia da fase. Escrito em 17/11/2026. A fase ainda não existe no jogo: isto é só o
ponto de partida.

**Referências:** 14 fotos e 3 vídeos de um prédio real, que **não estão no repositório** (peça a
quem escreveu este arquivo). O prompt cita as fotos por número, nesta ordem de envio:

| Nº | O que mostra |
|---|---|
| 1, 2, 5 | Escada metálica em vão aberto, vista de baixo e do patamar |
| 3 | Sala com fileiras de mesas brancas e cadeiras pretas |
| 4 | Corredor com luminárias tubulares penduradas e teto técnico aparente |
| 6 | Saguão dos elevadores com sofá, pufes e pendentes pretos |
| 7 | Escada vista de cima, com piso tátil |
| 8, 9 | Interior da cabine do elevador, em inox |
| 10 | Cafeteria com balcão de OSB e mesa redonda |
| 11 | Corredor comprido com armários |
| 12 | Teto do corredor: dutos, eletrocalhas e luminárias |
| 13, 14 | Área de estar com pufes e sofá curvo |

Os vídeos mostram o percurso a pé entre esses espaços.

**Cuidados:**

- As fotos mostram o nome de uma instituição real, cartazes e pessoas. O prompt pede para não
  copiar nada disso: a fase é um lugar genérico e vazio.
- Nas fotos os armários são vermelhos e de madeira clara. A decisão para a fase é **só armários
  amarelos** como cor forte; o resto fica em cinza, inox e concreto.

## O prompt

```text
Você vai me ajudar a projetar uma fase nova de um jogo de terror cooperativo em 3D no estilo
Backrooms, feito em Unity 6 com HDRP. O jogo já tem três fases (corredores amarelos, um
mostruário de móveis e piscinas). Cada fase é montada por código a partir de uma seed: uma
grade de células vira corredores, salas, luzes e colisores. O jogador filma tudo com uma
filmadora VHS, tem uma lanterna, e passa de fase abrindo uma porta de saída.

Vou enviar 14 fotos e 3 vídeos de um prédio real. Use-os como referência de arquitetura,
materiais e luz. NÃO copie logotipos, nomes, cartazes, placas com texto real nem pessoas: a
fase é um lugar genérico e vazio, sem nenhuma marca.

IDEIA DA FASE
"O Escritório": um andar de prédio comercial/universitário moderno, vazio e silencioso,
grande demais e repetido demais para ser real. Corredores longos, teto técnico aparente,
luz fria. A sensação é de expediente encerrado, com tudo ligado e ninguém ali.

PALETA
Quase sem cor: cinza das paredes, concreto, aço inox escovado, piso cinza de placas grandes,
teto escuro. A ÚNICA cor forte são os armários (lockers) amarelos. As fotos mostram armários
vermelhos e de madeira clara, sofás coloridos e pisos verde e roxo: ignore essas cores e use
só o amarelo nos armários.

ELEMENTOS, COM A FOTO DE REFERÊNCIA
1. Início no elevador (fotos 8 e 9): o jogador nasce dentro de uma cabine de aço inox
   escovado, com painel de botões e corrimão. As portas abrem para o saguão.
2. Saguão dos elevadores (fotos 6 e 13): portas de elevador de inox lado a lado, teto de
   ripas escuras com pendentes pretos em meia-esfera, lixeiras, placa suspensa de direções
   (sem texto legível).
3. Corredores (fotos 4, 11 e 12): longos e retos, com luminárias tubulares brancas
   penduradas atravessadas, em fila, uma a cada poucos metros. Teto sem forro, com dutos
   galvanizados, eletrocalhas e canos vermelhos de incêndio à mostra. Um lado com armários
   até a altura do peito, o outro com portas de vidro em esquadria branca. Quadros
   elétricos, câmeras de segurança e placas de saída de emergência nas paredes.
4. Escadas (fotos 1, 2, 5 e 7): escada metálica em vão aberto, com viga lateral
   verde-água, degraus de granito cinza com fita antiderrapante preta e ponteiras amarelas,
   guarda-corpo de tubos horizontais de inox com tela metálica embaixo, patamares
   intermediários e piso tátil no topo. Do vão dá para ver os andares de cima e de baixo.
5. Salas de aula/reunião (foto 3): salas largas, com fileiras de mesas brancas compridas e
   muitas cadeiras pretas de encosto de tela com rodinhas, piso de madeira, forro branco
   com grelhas de ar-condicionado, quadro branco, projetor e telas na parede (desligadas ou
   com estática), janelas com persianas no fundo.
6. Cafeteria (foto 10): balcão comprido de madeira OSB com banquetas altas, mesa redonda
   branca com cadeiras de madeira curvada, painel de senha em LED vermelho, geladeiras e
   vitrines, teto com dutos redondos aparentes.
7. Áreas de estar (fotos 6, 13 e 14): sofá curvo comprido, pufes grandes cinza, plantas em
   vaso. Use com parcimônia e em tons de cinza.

Os vídeos mostram o percurso a pé entre esses espaços. Use-os para entender como eles se
ligam, as proporções (largura dos corredores, pé-direito, tamanho do vão da escada) e o som
ambiente.

O QUE EU PRECISO DE VOCÊ
1. Uma descrição da fase em até dois parágrafos: o clima e o que a diferencia das outras.
2. A planta em regras, pensada para geração por código numa grade de células de 4 m: quais
   tipos de sala existem, tamanho de cada uma em células, como se ligam, quantas de cada
   por mapa e onde ficam o elevador inicial e a saída.
3. Como usar a escada: a fase tem mais de um andar ou a escada é só cenário? Diga os prós e
   contras de cada opção e recomende uma.
4. Lista de peças 3D a modelar, da mais importante para a menos, com medidas aproximadas em
   metros e o material de cada uma.
5. Iluminação: tipo, cor e intensidade relativa das luzes de cada espaço, e onde ficam as
   áreas escuras.
6. Três a cinco ideias de tensão ou perigo que combinem com o lugar (por exemplo: luzes do
   corredor apagando em sequência, elevador que chega sozinho, cadeiras fora do lugar).
7. O que deixar de fora para a fase rodar bem: ela precisa de poucas luzes com sombra e de
   peças repetidas.

Responda em português, de forma direta, e pergunte antes de supor o que não estiver claro
nas fotos.
```
