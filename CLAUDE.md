# The BackRooms

Jogo de terror 3D em co-op no estilo Backrooms, feito em Unity. Hoje existe o Level 0
jogável por IP, com interface de filmadora VHS, o primeiro item (caneta de riscar), buracos
com faixas de travessia, apagões periódicos, a primeira entidade, que caça os jogadores pelo
barulho, e gravação da tela em vídeo (F9). A segunda fase, o **Level 1 — O Mostruário**, tem
projeto, uma versão de teste jogável mobiliada com modelos feitos no Blender e o corpo do
inimigo dela, o Capitão (seção "Level 1: onde parou e como continuar").

Conversa, comentários de código, documentação e mensagens de commit são em **português**.

## Ambiente

- Unity **6000.6.4f1**, HDRP 17.6 (não trocar de pipeline), Netcode for GameObjects 2.13.3.
  Até 11/11/2026 o projeto era 6000.3.26f1 / HDRP 17.3; a troca de versão é um commit só, se
  precisar voltar. Na primeira abertura nessa versão o log mostra exceções internas do HDRP
  (atlas de sombras, "Blitter is already initialized"); não vêm do jogo e não aparecem em Play.
- **Blender 5.3** em `C:\Program Files\Blender Foundation\Blender 5.3\`, usado só por linha de
  comando, sem janela, para gerar os modelos (`Tools/Blender`).
- Só o **Input System novo**: nada de `UnityEngine.Input`. As ações são criadas em código
  (`new InputAction(...)`), como em `PenTool` e `PlayerController`.
- Interface em UI Toolkit; o efeito VHS é um Custom Pass (`CamcorderPass` + `CamcorderVHS.shader`).
- Sem `.asmdef`: tudo compila em `Assembly-CSharp`, e o que está em `Scripts/Editor` em
  `Assembly-CSharp-Editor`.

## Onde fica cada coisa

Todo o jogo está em `Assets/_Game`. Namespaces seguem a pasta: `BackRooms.World`, `.Player`,
`.Net`, `.Items`, `.Entities`, `.UI`, `.Audio` e `BackRooms.EditorTools` para `Scripts/Editor`.

- `Scripts/World` — `LevelGenerator` (planta a partir da seed; buracos e nichos podem ser
  desligados) e `LevelBuilder` (malhas e colisores; as setas pintadas podem ser desligadas);
  buracos (`PitFall`, `PitSmoke`); luminárias e apagão (`CeilingLight`, `LightsOut`); esconderijo e
  saída (`HidingSpot`, `LevelExit`); `PropLayer` (os móveis do Level 1 espalhados pela seed e as
  placas de curva no lugar das setas pintadas).
- `Scripts/Player` — `PlayerController` (movimento, queda e captura), `Flashlight`, `Footsteps`,
  `PlayerBody` (o traje que os outros veem) e `ItemHand` (a mão em primeira pessoa).
- `Scripts/Network` — `GameSession` (host, cliente, envio da seed e do relógio da sessão) e
  `PlayerNetwork` (o que cada jogador publica: olhar, agachar, lanterna, esconderijo, item na mão).
- `Scripts/Items` — `PenTool` (entrada e mira) e `DrawingBoard` (malhas dos riscos e sincronização).
- `Scripts/Entities` — a entidade do Level 0: `EntityDirector` (quando e onde aparece),
  `EntityBrain` (sentidos e decisão, só no host), `LevelNav` (caminho na grade do mapa),
  `EntityBody` (corpo de cipós e animação), `EntityHeadMesh` (cabeça esculpida como malha) e
  `EntityAudio`, tudo feito em código. O inimigo do Level 1, o Capitão, por enquanto só corpo:
  `CaptainBody` e `CaptainHeadMesh`.
- `Scripts/Systems/LevelCatalog.cs` — as fases em ordem (`LevelCatalog`: nome e cena) e o
  progresso desta máquina (`LevelProgress`: zeradas + a próxima liberadas; tudo no modo
  desenvolvedor). Fase nova entra no catálogo e na lista de cenas da build.
- `Scripts/Systems/FigureKit.cs` — peças para montar figuras em código (junta, osso, volume). A
  entidade, o Capitão, o traje dos jogadores (`PlayerBody`) e a mão (`ItemHand`) usam.
- `Scripts/UI` — filmadora (`Camcorder`, `CamcorderPass`) e gravação em vídeo (`ScreenRecorder`,
  `AviWriter`, `RecorderAudioTap`).
- `Scripts/Editor` — gerador do Level 0 (`Level0Setup`, que também monta outras fases por
  `SceneOptions`), modelos GLB gerados por IA (`GlbReader`, `ImportedModels`), pintores de textura
  (`ProceduralPaint`, `StainPainter`, `EntityPainter`), o Level 1 (`Level1Setup`, `Level1Models`,
  `Level1PlayTest`, `CaptainPreview`) e as demais ferramentas de teste e prévia (menu **BackRooms**).
- `Tools/Blender` — modelos feitos por script no Blender e exportados em FBX:
  `moveis_base.py` (biblioteca: peças, paleta, UV, exportação, conferência, foto rápida) e um
  script por lote em `lotes/` (`basicos`, `estofados`, `loja`, `detalhes`, `transito`).
- `Docs/` — `plano-coop.md` (estado atual e plano do jogo, arquitetura de rede; ler antes de
  mexer em rede ou em geração de fase), `historico.md` (registro cronológico de cada rodada),
  `level1-mostruario.md` (projeto do Level 1 e do Capitão), `level2-piscinas.md` (projeto do Level 2, as
  Poolrooms, e as texturas a gerar; prompts em `level2-prompts.md`), `modelos-blender.md` (modelos do
  Blender ao Unity e como dividir lotes entre agentes). Os outros são referência de design.

## Teclas

WASD anda, Shift corre, Ctrl ou C agacha, Espaço pula, F lanterna, 1 pega e guarda a caneta,
botão esquerdo risca, R ou 3 passa para a cor seguinte e 2 volta para a anterior, E abre a porta
de saída (ou solta, se já estava esperando os outros), F9 grava a tela, Esc solta o mouse. No menu: H hospeda,
J entra; a lista "Fase" troca de nível (no editor todas ficam liberadas).

## Conteúdo gerado: não editar à mão

A cena `Scenes/Level0_Test.unity`, os prefabs `Player` e `Entity`, o perfil de Volume e tudo em
`Art/Level0` (texturas e materiais) são **gerados** por `Level0Setup` (menu BackRooms > Montar
Level 0). Mudança nesses arquivos se faz no gerador, não no arquivo.

Exceção: `Art/Fontes` guarda os modelos **GLB originais** gerados por IA (Higgsfield). Eles não são
gerados pelo `Level0Setup`, que os lê com `GlbReader` (sem pacote de glTF) e assa em `Art/Level0`
a malha já encaixada e as texturas do HDRP (`ImportedModels`). Apagar um GLB volta à peça antiga.
`Art/Fontes/Texturas` guarda texturas geradas por IA já emendáveis, com o nome da textura que
substituem; o `Level0Setup.EnsureTexture` usa o arquivo de lá no lugar da pintura em código.

A cena `Scenes/Level1_Test.unity` é **gerada** por `Level1Setup` (menu BackRooms > Montar Level 1
(teste)), com o mesmo montador do Level 0 (`Level0Setup.SceneOptions`).

A cena `Scenes/Level2_Test.unity` (As Piscinas) e os materiais e texturas de `Art/Level2` são
**gerados** por `Level2Setup` (menu BackRooms > Montar Level 2 (teste)). As texturas de azulejo,
pastilha e borda vêm de `Art/Fontes/Level2` (recortadas da Folha A do Higgsfield); a ondulação da
água e o vidro das claraboias são pintados em código até as folhas B, C e D chegarem.

Os modelos em `Art/Level1/Modelos` (`.fbx` e `materiais_<lote>.json`) e as texturas em
`Art/Level1/Texturas` são **gerados** pelos lotes de `Tools/Blender`; os materiais em
`Art/Level1/Materiais`, por `Level1Models` a partir dos `materiais_*.json`; os de
`Art/Level1/Capitao`, pelo `CaptainPreview`. Muda-se o script e roda de novo: o `.fbx` nunca se
edita, e `.blend` não entra no projeto.

Ao mudar algo que só aparece depois de recriar (texturas, materiais, objetos da cena, valores
padrão do `LevelBuilder`), **aumente `Level0Setup.ContentVersion`**. O `Level0AutoUpdate` compara
com a versão gravada na cena e oferece recriar; sem isso o jogo roda código novo sobre conteúdo
velho. Mudança só de código não precisa. A cena do Level 1 não tem versão: recrie-a
(`Level1Setup.BatchCheck` recria) sempre que mudar o `Level0Setup`, o `LevelBuilder` ou os modelos.

## Regras que já custaram um defeito

- **Raio saindo da câmera do jogador ignora o próprio jogador.** O colisor do
  `CharacterController` só acompanha o corpo no passo de física seguinte; pulando, correndo ou
  recuando, a câmera sai de dentro dele e um `Physics.Raycast` comum o acerta. Use o padrão de
  `PenTool.TryAim` (`RaycastNonAlloc` descartando `transform.root` igual ao do jogador) em todo
  item novo: interagir, pegar, mirar.
- **Geração de fase é determinística.** O host envia só a seed e cada máquina monta o mesmo
  labirinto, então `LevelGenerator`, `LevelBuilder` e `PropLayer` usam apenas
  `System.Random(seed)`, nunca `UnityEngine.Random`, tempo, ordem de dicionário nem
  `string.GetHashCode`. O teste de fumaça compara o hash do mapa entre host e cliente; a
  `PropLayer` tem o seu (`PropLayer.Hash`). Ligar ou desligar uma etapa do gerador muda o que vem
  depois dela: opções novas entram desligando etapas só na fase que pede, com o padrão igual ao
  Level 0.
- **Buraco nunca isola parte do mapa.** Toda célula com piso continua alcançável sem pisar em
  faixa, então sempre há um caminho seguro até a saída (`LevelData.SafePathCells`). Quem mexer
  em `LevelGenerator.DigPits` mantém a conferência de `SafeDetour`.
- **Móvel nunca fecha passagem.** A `PropLayer` deixa livre uma cruz de 1,2 m no meio de cada
  célula e recusa, ao criar, qualquer móvel com colisor que invada essa cruz (inclusive cadeira
  tombada ou empilhada). `Level1Setup.BatchCheck` confere com uma cápsula entre todas as células.
- **O que precisa acontecer junto em todas as máquinas usa `GameSession.SessionSeconds`**, o
  relógio do servidor, em vez de mandar o evento pela rede. É assim que o apagão (`LightsOut`)
  funciona: cada máquina faz a mesma conta.
- **Sem janela o Unity não desenha a câmera do jogador.** Teste por linha de comando que dependa
  da imagem (a gravação) precisa mandar desenhar: `camera.Render()` numa textura.
- **A entidade só pisa onde o `LevelNav` deixa:** células com piso, fora da sala inicial e dos
  buracos. Sala inicial como abrigo e faixa como rota de fuga dependem disso. Ela não usa NavMesh.
- **Prefab de rede não se registra por código.** O Netcode mantém `Assets/DefaultNetworkPrefabs.asset`
  com todo prefab que tem `NetworkObject`; registrar de novo dá erro de duplicata.
- **Material sem luz (HDRP/Unlit): a cor é o que aparece na tela, de 0 a 1**, não nits.
- **Figura em código: forma orgânica se esculpe como malha, não com esferas esticadas** (ficam
  com cara de balão). Muitas peças pequenas se fundem com `FigureKit.Bake` depois de montadas.
  Em malha esculpida, conta com `Mathf.Pow` de valor que pode sair negativo (seno no fim de uma
  volta) vira NaN e some a malha inteira: prenda com `Mathf.Max(0, ...)`.
- **Mudança de aparência de personagem ou modelo passa por imagem antes de entrar no jogo.** As
  fotos saem das ferramentas de prévia (`Level0Preview`, `CaptainPreview`, `Level1Models`),
  renderizadas dentro da fase. Personagem alto é visto de baixo: confira também a foto de baixo
  (a barba do Capitão tapava a boca só nela).
- **Modelos do Blender:** cada peça acerta as próprias faces ao nascer (recalcular as normais da
  malha inteira no fim virava faces do avesso onde peças se encostam); o chanfro recebe o material
  da peça (sem isso pega o material 0 da malha); uma cor não pode ter o mesmo nome em dois lotes
  com definições diferentes (o `run_lote` recusa). Detalhes em `Docs/modelos-blender.md`.
- **Colisor e malha visível nascem juntos** no `LevelBuilder` (`AddWall`, `AlcoveBox`,
  `AddCollider`) e na `PropLayer` (`Spawn`). Não criar um sem o outro.
- **Lista de cenas da build:** o Level 0 fica sempre em primeiro (é a cena que a build abre).
  `Level0Setup.AddSceneToBuild` garante isso; uma fase nova entra depois dele.
- **Rede:** host autoritativo por IP direto (porta 7777), movimento com autoridade do dono.
  Estado que quem entra depois precisa ver (riscos, por exemplo) tem de ser reenviado na conexão,
  como faz `DrawingBoard.SendAll`.

## Level 1: onde parou e como continuar

O projeto completo está em `Docs/level1-mostruario.md` (espaço, zonas, catálogo, como os objetos
são postos, o Capitão e as regras dele, objetivo, som, ordem de construção e decisões em aberto).

**Já existe:**
- **Cena de teste** `Scenes/Level1_Test.unity`: a planta do Level 0 em 10 x 10 células, sem
  buracos, nichos nem entidade (o apagão continua). Para jogar: menu BackRooms > Abrir Level 1
  (teste), Play, H.
- **`PropLayer`:** mobília a planta pela seed. Cada bloco de 2 x 2 células tem um tema (jantar,
  sala de estar, loja, estoque, bagunça, vazio) e um bloco é a sala dos sofás. Móvel grande
  encosta em parede fechada, de frente para a célula; os menores vão nos quatro cantos; o
  desgaste (`LevelGenerator.WearAt`) tomba e empilha mais longe do início. As setas do caminho
  até a saída viram placas amarelas de curva pregadas na parede (`MountExitSigns`). Modelo novo
  só aparece na fase se entrar nas listas de algum tema (`WallItem`, `CornerItem`).
- **43 modelos** feitos no Blender em 5 lotes (lista em `Docs/modelos-blender.md`, seção 7).
- **O Capitão:** só o corpo (`CaptainBody.Pose(fase, velocidade, parado, boca, tempo)`), ainda
  fora da fase. Desenho a partir de imagens do filme *Backrooms* (2026), a pedido; antes de
  publicar, ver a decisão em aberto 4 de `level1-mostruario.md` (direitos autorais).

**Próximos passos** (ordem de `level1-mostruario.md`, seção 8): passagem do Level 0 para o
Level 1 (o host envia o índice da fase junto com a seed); cérebro do Capitão (herda o ouvido do
`EntityBrain` e congela quando visto e iluminado); totens; guarda-roupa como `HidingSpot`
(as portas já são objetos filhos com a origem na dobradiça); plástico-bolha; etiquetas de
LIQUIDAÇÃO, teclado e porta da doca (objetivo); som; anomalias e barricadas. Conferir a mobília
entre host e cliente (dois editores) ainda não foi feito.

**Modelos novos:** escreva um lote em `Tools/Blender/lotes/` (ou peça a agentes, um por lote,
com o modelo Sonnet; o roteiro do pedido está em `Docs/modelos-blender.md`, seção 6), rode-o
com `-- --fotos`, olhe as fotos em `Logs/Blender`, acrescente o móvel às listas da `PropLayer`
e rode `Level1Setup.BatchCheck` e `Level1PlayTest.BatchRun`.

## Como testar

No editor, pelo menu **BackRooms**: Abrir Level 0, Abrir Level 1 (teste), Testar Level 0
(fumaça), Testar Level 1 (Play), Conferir Level 1, Abrir Level 2 (teste), Testar Level 2 (Play),
Conferir Level 2, Conferir colisores do Level 0, Gerar mapa 2D,
Capturar prévia, Capturar prévia do Capitão, Móveis do Level 1 > Atualizar e fotografar,
Exportar sons da entidade (WAV em `Logs/Sons`, para ouvir e ajustar os sons sintetizados sem
precisar jogar).

Por linha de comando, com o **editor fechado** (dois editores não abrem o mesmo projeto):

```
"C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe" -batchmode -projectPath "<pasta>" -executeMethod BackRooms.EditorTools.Level0SmokeTest.BatchRun -logFile "<log>"
```

- `Level0SmokeTest.BatchRun` — sozinho: entra em Play, anda, corre, pula, risca, atravessa uma
  faixa, cai num buraco, vê um apagão, grava 3 s de vídeo e é pego pela entidade. Sai com 0 se
  passou, 3 se falhou. O resultado é a linha `[BackRooms] Teste de fumaça PASSOU/FALHOU: ...`
  do log.
- `Level0SmokeTest.BatchHost` + `BatchJoin` — host e cliente ao mesmo tempo. O segundo editor
  precisa de uma **cópia** do projeto em outra pasta. `BatchHostDrawFirst` + `BatchJoinLate`
  testam quem entra depois.
- `Level0ColliderCheck.BatchRun` — colisores contra a malha visível, em 8 seeds.
- `Level0Preview.BatchSceneAndCapture` — recria a cena (necessário depois de mexer no
  `Level0Setup`) e salva imagens da fase, do monstro, do traje e da mão em `Logs/Previews`.
- `Level1Setup.BatchCheck` — atualiza os modelos, recria a cena do Level 1 e confere 5 seeds
  (passagens livres, sala inicial vazia, mesma seed = mesma mobília); fotos em
  `Logs/Previews/level1_*.png`. Sai com 0 se passou, 3 se falhou.
- `Level1PlayTest.BatchRun` — o Level 1 em Play: hospeda, confere que nasce na sala inicial com
  a mobília montada, anda o caminho seguro inteiro até a saída sem travar e anda contra um móvel
  sem atravessá-lo. Sai com 0 se passou, 3 se falhou; linha `[BackRooms] Teste do Level 1`.
  "Encostou" se mede pela colisão que o `CharacterController` relata, não por distância: numa
  pilha, a cadeira de cima, inclinada, barra o jogador antes da de baixo.
- `LevelSelectTest.BatchRun` — seletor de fase em Play: fase bloqueada sem progresso, troca
  para o Level 1 com uma sessão só, a porta do Level 1 zera e leva ao Level 2 já hospedando,
  nada num poço, a porta do Level 2 (última fase) zera e devolve ao menu, volta ao Level 0 sem
  restos da água, e a porta do Level 0 leva ao Level 1 já hospedando. Dois editores:
  `LevelSelectTest.BatchHostLevel1` + `BatchJoinFromLevel0` (o cliente tem de ser levado ao
  Level 1 e montar a mesma mobília) e `BatchHostDoor` + `BatchJoinDoor` (o cliente abre a porta
  e espera; quando o host abre, os dois passam para o Level 1 e o cliente reconecta). Restaura o
  progresso salvo; linha `[BackRooms] Teste do seletor de fase`.
- `Level0Preview.BatchCaptureHud` — só o painel da filmadora, em 1920 x 1080, de frente para a
  porta de saída (`Logs/Previews/painel_*.png`). Não recria a cena.
- `Level2Setup.BatchCheck` — recria a cena das Piscinas e confere 5 seeds (saída e sala inicial
  secas, seco nunca encosta em fundo, poço só encosta em fundo, tudo sem poço alcançável a pé,
  uma escada por desnível, mesma seed = mesma planta); fotos em `Logs/Previews/level2_*.png`.
- `Level2PlayTest.BatchRun` — as Piscinas em Play: nasce no seco, anda o caminho a pé até a saída
  pelas escadas, boia num poço, mergulha até afogar e voltar, e sai nadando para a área funda.
- `Level0ColliderCheck.BatchRunLevel2` — colisores contra a malha visível na cena das Piscinas.
- `Level1Models.BatchUpdateAndCapture` — cria os materiais dos lotes, reimporta os modelos,
  escreve as medidas no log e salva fotos em `Logs/Previews/moveis_*.png` (todos, ou só os de
  `-moveis Nome1,Nome2`), além da vitrine de jantar, da loja e da sala dos sofás.
- `CaptainPreview.BatchCapture` — fotos do Capitão dentro do Level 0, em
  `Logs/Previews/capitao_*.png` (inclusive closes do rosto). Regrava os materiais dele.
- Modelos do Blender: `blender.exe -b --factory-startup -P Tools/Blender/lotes/<lote>.py -- --fotos`
  (fotos rápidas em `Logs/Blender`). Vários lotes podem rodar ao mesmo tempo; o Unity, não.

Não passar `-quit`: os métodos encerram o editor sozinhos. Depois de rodar em lote, o editor
pode abrir numa cena vazia; o `Level0AutoUpdate` troca para a cena do jogo.

Mudança em jogador, item, fase ou rede só está pronta depois do teste de fumaça sozinho; mudança
em rede, depois do teste com dois editores; mudança no Level 2 (ou na água, no jogador, no
`LevelBuilder` ou no gerador), depois de `Level2Setup.BatchCheck`, `Level2PlayTest.BatchRun` e
`Level0ColliderCheck.BatchRunLevel2`; mudança no Level 1 (ou no `LevelBuilder`, no gerador
ou nos modelos), depois de `Level1Setup.BatchCheck` e `Level1PlayTest.BatchRun`. Diga no resumo o
que foi testado por máquina e o que não foi testado à mão.

## Git

- Repositório: `Rabarbuti/The-BackRooms`. Trabalho em branch, nunca direto na `main`: o Level 1
  está na branch `level1` (saiu da `level0-coop`, que é a do Level 0).
- **PNG e FBX vão direto no Git** (`binary` no `.gitattributes`): a conta está sem orçamento de
  LFS. Os outros binários continuam em LFS; evite adicionar áudio, `.blend` ou vídeo sem
  combinar antes.
- Todo arquivo novo em `Assets/` vai com o `.meta` correspondente.
