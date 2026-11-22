# The BackRooms

Jogo de terror 3D em co-op no estilo Backrooms, feito em Unity. Hoje existe o Level 0
jogável por IP, com interface de filmadora VHS, o primeiro item (caneta de riscar), buracos
com faixas de travessia, apagões periódicos, a primeira entidade, que caça os jogadores pelo
barulho, gravação da tela em vídeo (F9), música na tela inicial e passos que mudam com o piso e a
água (carpete, azulejo, água rasa, nado, debaixo d'água). **Cada fase tem um inimigo próprio** (o Level 3
tem dois), todos esculpidos no Blender e vestidos sobre juntas montadas em código (seção "Inimigos
esculpidos"): a entidade (Level 0), o Capitão (Level 1), o Undertow (Level 2), o Zelador e o
Ascensorista (Level 3). A segunda fase, o **Level 1 — O Mostruário**, tem
projeto, uma versão de teste jogável mobiliada com modelos feitos no Blender e o inimigo dela, o
Capitão, que só anda quando ninguém o vê iluminado (seção "Level 1: onde parou e como continuar"). A terceira, o
**Level 2 — As Piscinas** (as Poolrooms), tem projeto e uma versão de teste jogável: salas de
azulejo sorteadas pelo `PoolGenerator` (não o labirinto do Level 0), água em quatro profundidades,
luz só por claraboias e janelas, nado e fôlego, a maré com alarme e o ralo (`PoolTide`), as válvulas que
drenam (uma delas só se alcança nadando) e o **Undertow**, que muda de água pelos canos, caça pelo
barulho na água e, na maré alta, pelo mapa inteiro (menos a sala inicial) (`Docs/level2-piscinas.md`, cabeçalho "Já existe / Falta"). A quarta, o **Level 3 — O Escritório**, tem
projeto e uma versão de teste jogável: um andar de prédio vazio sorteado pelo `OfficeGenerator` (anel de
corredores de teto técnico, salas, saguão de elevadores, cafeteria, vão da escada com a saída), luz fria,
armários amarelos que batem, a onda de luzes que persegue os jogadores, interruptores de luz, botões de
elevador, o **Zelador** (só existe no escuro) e o **Ascensorista** (cego, chega de elevador e caça pelo
barulho), e o alarme de incêndio que destrava a saída e corta a energia; sem modelos do Blender do
cenário ainda (`Docs/level3-escritorio.md`, cabeçalho "Já existe / Falta"). **Toda fase tem objetivo** (a porta só destranca cumprido): o teclado do Level 1
(`ExitLock`) e, nas outras, o objetivo comum `SwitchObjective` (disjuntores, válvulas, alarmes; seção
"Objetivo comum"). Esc abre o menu de pausa, com as tarefas da fase. O plano até a entrega está em
`Docs/plano-de-acao.md`.

Conversa, comentários de código, documentação e mensagens de commit são em **português**. **O texto que
o jogador lê é em inglês** (menus, filmadora, tarefas, placas, etiquetas, nomes dos monstros; decisão de
20/11/2026): fica em constantes no topo da classe, para ser fácil de trocar, sem acento onde a fonte do
jogo (`PixelFont`) não tem.

## Ambiente

- Unity **6000.6.4f1**, HDRP 17.6 (não trocar de pipeline), Netcode for GameObjects 2.13.3.
  Até 11/11/2026 o projeto era 6000.3.26f1 / HDRP 17.3; a troca de versão é um commit só, se
  precisar voltar. Na primeira abertura nessa versão o log mostra exceções internas do HDRP
  (atlas de sombras, "Blitter is already initialized"); não vêm do jogo e não aparecem em Play.
- **Blender 5.3** em `C:\Program Files\Blender Foundation\Blender 5.3\`, usado só por linha de
  comando, sem janela, para gerar os modelos (`Tools/Blender`).
- A máquina tem **16 GB**: Unity e Blender juntos (ou dois Unity em par mais outro) matam rodadas por
  falta de memória. **Com mais de um agente (ou terminal) trabalhando, Unity e Blender só pela fila**
  (`Tools/unity-fila.ps1`, seção "Como testar"), nunca chamando `Unity.exe` direto.
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
  placas de curva no lugar das setas pintadas; também as barricadas, os totens, o plástico-bolha, o
  piso por zona e o objetivo). Do Level 1: `HideSpot` (guarda-roupa e mesa de esconder, tecla E;
  `HideSpotInput`), `BubbleWrap` (plástico-bolha: barulho e estalo), `Totem` (estátua do Capitão),
  `ExitLock` (porta trancada por teclado e etiquetas; o painel do código na tela é o `CodePanel`) e `FloorZones` (laminado e
  vinílico, para os passos). As Piscinas têm gerador e construtor próprios:
  `PoolGenerator` (planta por salas, C# puro, com `Validate`), `PoolData` (divisas, claraboias,
  salas, colunas, lâmpadas), `LevelBuilder.Pools.cs` (a geometria, parte do `LevelBuilder`) e
  `PoolWater` (ondulação, cáustica, debaixo d'água, anoitecer, salas escuras). O Escritório também:
  `OfficeGenerator` (planta, C# puro, com `Validate` e `Hash`), `OfficeData` (tipos de célula e de
  divisa, salas, vão da escada, elevadores), `LevelBuilder.Office.cs` (piso, tetos, divisas, luminárias)
  e `LevelBuilder.OfficeProps.cs` (mobília, elevadores, vão da escada e saída, placas), e `OfficeEvents`
  (`ElevatorDoors`, `OfficeElevators`, `OfficeLightWave` no lugar do apagão, `OfficeSounds`). Em todas:
  `SwitchObjective` (o objetivo comum, posto por `LevelBuilder.Objective.cs`), `BatteryPickups` (pilhas
  da lanterna), `LevelTasks` (`ITaskSource`: cada objetivo publica as próprias tarefas e teclas para o
  menu de pausa; `SwitchObjective`, `ExitLock` e `LightSwitches` implementam) e `WorldNoise` (barulho do
  mundo para os inimigos: `INoiseListener` + `WorldNoise.Emit(ponto, raio)`; armários, estalo do
  interruptor, estrondo do objetivo). Nas Piscinas, `PoolTide` (maré, alarme, ralo na vazante, drenagem)
  e `PoolAlarm` (as lâmpadas vermelhas de parede do alarme da maré, pela seed, depois de tudo). No
  Escritório, `OfficeLockers` (`LockerBangs`/`LockerDoor`: os armários que batem pelo relógio) e
  `ElevatorButtons` (`ElevatorControl`/`ElevatorButton`: abrir, fechar e chamar; o host decide).
- `Scripts/Player` — `PlayerController` (movimento, queda e captura), `Flashlight`, `Footsteps`
  (passos sintetizados por superfície: carpete sem água na fase; nas Piscinas, azulejo seco, água
  rasa, nado à tona, debaixo d'água e um espirro ao cruzar a linha d'água depressa; dos outros
  jogadores, calculado aqui pela altura deles e a água local; `NoiseOf` dá o barulho de cada
  superfície para um inimigo que ouça a água), `PlayerBody` (o traje que os outros veem: o modelo da
  Tripo vestido nas juntas, com as medidas dele; sem o modelo, a figura de código) e
  `ItemHand` (a mão em primeira pessoa).
- `Scripts/Audio` — `ShopMusic` (música de loja do Level 1, que se cala antes do apagão, e o
  jingle do Capitão), `FluorescentHum` (zumbido das luminárias), `OfficeAmbience` (o som ambiente do
  Escritório: ar-condicionado, difusores e reatores que calam onde a onda apaga) e `MenuMusic` (música da tela
  inicial, `Audio/Musica/Menu_Tema.mp3`: toca em laço no menu, some ao começar a jogar; um dono
  só, que sobrevive à troca de fase pela lista do menu sem recomeçar).
- `Scripts/Network` — `GameSession` (host, cliente, envio da seed e do relógio da sessão) e
  `PlayerNetwork` (o que cada jogador publica: olhar, agachar, lanterna, esconderijo, item na mão).
- `Scripts/Items` — `PenTool` (entrada e mira) e `DrawingBoard` (malhas dos riscos e sincronização).
- `Scripts/Entities` — a entidade do Level 0: `EntityDirector` (quando e onde aparece),
  `EntityBrain` (sentidos e decisão, só no host), `LevelNav` (caminho na grade do mapa),
  `EntityBody` (corpo de cipós e animação), `EntityHeadMesh` (cabeça esculpida como malha) e
  `EntityAudio`. O inimigo do Level 1, o Capitão: `CaptainBrain` (herda do `EntityBrain` e muda
  três coisas pelos ganchos `HoldStill`, `SpeedNow` e `IsLevel0Entity`: congela visto e iluminado,
  cansa quem o encara, arrancadas no apagão), `CaptainBody` (segura a pose congelado; e
  `CaptainHeadMesh`, do corpo antigo) e `CaptainAudio` (a perna de pau). O mesmo `EntityDirector`
  põe um ou outro, pelo prefab. O inimigo das Piscinas, o Undertow: `UndertowNav` (território na
  água: funda e poço; na maré alta também o raso e o seco alagado, nunca a sala inicial), `UndertowBrain`
  (só no host; ouve o barulho na água pelo `Footsteps` e pelo `WorldNoise`; muda de água pelo relógio,
  `RoamPaused`; confere o corpo inteiro contra a fase a cada passo, `Fits`), `UndertowDirector` (põe-no
  no covil, só o ponto de partida; `Paused`),
  `UndertowBody` e `UndertowAudio`. O Zelador do Level 3: `JanitorBrain` (herda do `EntityBrain` pelo
  gancho `CellOpen`: só pisa no escuro; `JanitorBrain.Waves.cs`: escolhe os trechos da onda perto dos
  jogadores, nasce na ponta, decide o interruptor), `JanitorBody`, `JanitorAudio`, `JanitorDirector` (põe
  o Zelador e os interruptores; corte de energia do alarme e luz de emergência; `JanitorDark`, o mapa do
  escuro) e `LightSwitches` (interruptores de luz, mira + E). O Ascensorista do Level 3:
  `AttendantBrain` (cego: ganchos `Scripted`, `HasSight`, `CatchNeedsFacing`, `OfficeRoomsShelter`,
  `HearMargin`, `Catch`; as fases da visita, `AttendantPhase`, vão pela rede e ligam a porta,
  `ElevatorDoors.HeldOpen`, e o painel com a seta em cada máquina), `AttendantBody` e `AttendantAudio`.
  No Level 3 o `EntityDirector` põe o Ascensorista (assim que alguém sai da cabine, sem as exigências de
  lugar da entidade: ele nasce e já se esconde num poço), e o `JanitorDirector`, o Zelador. Os corpos montam
  as juntas e a animação em código; a aparência é o modelo do Blender (`model` no componente; vazio,
  volta o corpo antigo de peças).
- `Scripts/Systems/LevelCatalog.cs` — as fases em ordem (`LevelCatalog`: nome e cena) e o
  progresso desta máquina (`LevelProgress`: zeradas + a próxima liberadas; tudo no modo
  desenvolvedor). Fase nova entra no catálogo e na lista de cenas da build.
- `Scripts/Systems/FigureKit.cs` — peças para montar figuras em código (junta, osso, volume). A
  entidade, o Capitão, o traje dos jogadores (`PlayerBody`) e a mão (`ItemHand`) usam.
- `Scripts/Systems/CharacterModel.cs` — lê o modelo de personagem do Blender (`.bytes`) e o veste
  nas juntas como `SkinnedMeshRenderer`.
- `Scripts/UI` — filmadora (`Camcorder`, `CamcorderPass`), gravação em vídeo (`ScreenRecorder`,
  `AviWriter`, `RecorderAudioTap`), o menu inicial e a tela de fim da fita (`ConnectionMenu`), o menu de
  pausa (`PauseMenu`, criado pelo `ConnectionMenu` num documento próprio; o jogo não para) e `MenuKit`
  (peças de UI e a tela de opções comuns aos dois menus).
- `Scripts/Editor` — gerador do Level 0 (`Level0Setup`, que também monta outras fases por
  `SceneOptions`), modelos GLB gerados por IA (`GlbReader`, `ImportedModels`), pintores de textura
  (`ProceduralPaint`, `StainPainter`, `EntityPainter`), o Level 1 (`Level1Setup`, `Level1Models`,
  `Level1PlayTest`, `CaptainPreview`), os inimigos esculpidos (`CharacterModels`: esqueleto para o
  Blender e materiais), o Level 2 (`Level2Setup`: materiais, perfil de Volume e conferência;
  `Level2Preview`: fotos; `Level2PlayTest`), o Level 3 (`Level3Setup`: materiais, texturas pintadas,
  perfil de Volume, cena, conferência e fotos; `Level3PlayTest`), os inimigos novos (`UndertowPreview`,
  `JanitorPreview`, `AttendantPreview`: prefab, materiais e fotos; `UndertowTest`, `JanitorTest`,
  `AttendantTest`), a sonda dos testes em dois editores (`NetProbe`) e as demais ferramentas de teste e
  prévia (menu **BackRooms**).
- `Tools/compila.ps1` — compila os scripts sem abrir o Unity (usa o compilador do editor e as
  referências da última compilação em `Library/Bee`). Serve para conferir erro de compilação em
  segundos e para vários agentes trabalharem ao mesmo tempo; não substitui os testes.
- `Tools/unity-fila.ps1` — a fila do Unity e do Blender: um mutex global deixa um de cada vez, espera o
  editor do usuário fechar e a árvore compilar, e roda o método (`-Metodo`; em par, `-MetodoCopia`, que
  espelha o projeto em `..\BR-rede` e abre os dois editores) ou o script do Blender (`-Blender`). Logs
  em `Logs/Fila/<nome>.log`.
- `Tools/Blender` — modelos feitos por script no Blender e exportados em FBX:
  `moveis_base.py` (biblioteca: peças, paleta, UV, exportação, conferência, foto rápida) e um
  script por lote em `lotes/` (`basicos`, `estofados`, `loja`, `detalhes`, `transito`). Os inimigos:
  `personagem_base.py` (escultura por SDF), `personagem_export.py` (do esculpido ao jogo) e
  `personagens/` (`capitao.py`, `entidade.py`, `afogado.py`, `zelador.py`, `ascensorista.py`,
  `traje.py`, `conferir_modelo.py`, `esqueleto_*.json`, `traje_medidas.json`). Os modelos da Tripo que
  não são móveis: `tripo/` (`alavanca.py`, o disjuntor do Level 0; `traje_fonte.py`; ver
  `Docs/modelos-blender.md`, seção 10).
- `Docs/` — `plano-coop.md` (estado atual e plano do jogo, arquitetura de rede; ler antes de
  mexer em rede ou em geração de fase), `historico.md` (registro cronológico de cada rodada),
  `level1-mostruario.md` (projeto do Level 1 e do Capitão), `level3-escritorio.md` (projeto do Level 3;
  o prompt das referências em `level3-escritorio-prompt.md`), `level2-piscinas.md` (projeto do Level 2, as
  Poolrooms: referências, salas, luz e texturas; prompts em `level2-prompts.md`), `plano-de-acao.md` (o que fazer a seguir, em
  que ordem, com testes e decisões em aberto), `mecanicas.md` (as
  mecânicas de cada fase em linguagem de jogador, do que existe ao planejado, e ideias novas não decididas,
  `P0.1`, `P1.2`...), `modelos-blender.md` (modelos do
  Blender ao Unity e como dividir lotes entre agentes). Os outros são referência de design.

## Teclas

WASD anda, Shift corre, Ctrl ou C agacha, Espaço pula, F lanterna, uma tecla por caneta (1 preta,
2 azul, 3 vermelha: puxa a caneta, troca de cor ou, se já é a da mão, guarda), botão esquerdo
risca, E abre a porta
de saída (ou solta, se já estava esperando os outros), mira + E pega pilhas, mira + segurar E arma um
disjuntor, gira uma válvula (a da água, boiando: Espaço e E juntos) ou puxa um alarme, mira + E num
interruptor de luz religa o trecho escuro (Escritório), mira + E num botão de elevador abre, fecha ou
chama, mira + E numa carteira das salas de aula do Escritório senta (E de novo, andar ou pular levanta), mira + E no teclado da porta do Level 1 abre o painel do código (números ou clique,
Backspace apaga, Enter confirma, Esc ou E fecha), R grava a tela, Esc abre o menu de pausa (tarefas, teclas e opções; o jogo não para; fechar
prende o mouse de novo; Start no controle). No menu: H hospeda, J entra; a lista "Level" troca de nível
(no editor todas ficam liberadas). Na água funda das
Piscinas (piscinas, raias, canais e poços: não dá pé), segurar Espaço sobe e fica à tona; sem tecla,
afunda devagar até o fundo; Ctrl ou C mergulha; da água funda sai-se por escada ou, à tona,
empurrando contra uma borda baixa (W, ou Espaço encostado e de frente): sobe nela.

## Conteúdo gerado: não editar à mão

A cena `Scenes/Level0_Test.unity`, os prefabs `Player` e `Entity`, o perfil de Volume e tudo em
`Art/Level0` (texturas e materiais) são **gerados** por `Level0Setup` (menu BackRooms > Montar
Level 0). Mudança nesses arquivos se faz no gerador, não no arquivo.

Exceção: `Art/Fontes` guarda os modelos **GLB originais** gerados por IA (Higgsfield). Eles não são
gerados pelo `Level0Setup`, que os lê com `GlbReader` (sem pacote de glTF) e assa em `Art/Level0`
a malha já encaixada e as texturas do HDRP (`ImportedModels`). Apagar um GLB volta à peça antiga.
`Art/Fontes/Texturas` guarda texturas geradas por IA já emendáveis, com o nome da textura que
substituem; o `Level0Setup.EnsureTexture` usa o arquivo de lá no lugar da pintura em código.

Outra exceção: `Audio/Musica/Menu_Tema.mp3` (música do menu) é um arquivo mandado pelo usuário, não
gerado; o `Level0Setup` só acerta a importação (Streaming, Vorbis 0,7, estéreo) e põe o objeto
`MusicaDoMenu` nas três cenas.

A cena `Scenes/Level1_Test.unity` é **gerada** por `Level1Setup` (menu BackRooms > Montar Level 1
(teste)), com o mesmo montador do Level 0 (`Level0Setup.SceneOptions`). O prefab
`Prefabs/Captain.prefab` também é **gerado**, por `CaptainPreview.CreatePrefab`, a cada montagem do
Level 1.

Tudo em `Art/Personagens` é **gerado**: `<Nome>.bytes`, `_Cor.png` e `_Normal.png` pelos scripts de
`Tools/Blender/personagens` com `--exportar`, e os materiais por `CharacterModels` (e, do Ascensorista,
os `Peca_*` e `Painel_*` pelo `AttendantPreview`). Os `esqueleto_*.json` de `Tools/Blender/personagens`
são gravados pelo Unity (`CharacterModels`); o `traje_medidas.json`, pelo `traje.py -- --medidas`. Os
prefabs dos inimigos novos também são **gerados**:
`Prefabs/Undertow.prefab` por `UndertowPreview.CreatePrefab` a cada montagem do Level 2;
`Prefabs/Janitor.prefab` (`JanitorPreview`) e `Prefabs/Attendant.prefab` (`AttendantPreview.CreatePrefab`)
a cada montagem do Level 3.

`Art/Fontes/Alavanca` (`Alavanca.glb` e `.json`, o disjuntor do Level 0) e `Art/Fontes/Traje`
(`Traje.glb`) também são **gerados**, pelos scripts de `Tools/Blender/tripo` a partir da exportação
da Tripo, que fica só na máquina de quem a fez (`Assets/TripoModels`, fora do Git). Para mudar,
exporte de novo e rode o script; o `Level0Setup` assa o disjuntor em `Art/Level0` (`Alavanca_*`).

A cena `Scenes/Level3_Test.unity` (O Escritório), os materiais e as texturas de `Art/Level3` (pintadas em
código) e o perfil `Settings/Level3_Volume.asset` são **gerados** por `Level3Setup` (menu BackRooms >
Montar Level 3 (teste)), que regrava tudo a cada montagem. A planta não fica na cena: sai do
`OfficeGenerator` pela seed, em Play.

A cena `Scenes/Level2_Test.unity` (As Piscinas), os materiais e texturas de `Art/Level2` e o perfil
`Settings/Level2_Volume.asset` são **gerados** por `Level2Setup` (menu BackRooms > Montar Level 2
(teste)), que regrava tudo a cada montagem. As texturas-fonte ficam em `Art/Fontes/Level2`,
recortadas das folhas do Higgsfield (não são geradas; o que deriva delas vai para `Art/Level2`):
A (`Tile_White`, `Mosaic_Pool`, `Coping` e `Tile_White_Worn`, este sem uso), B (`Water_Height`,
`Caustics`, `Window_Frosted`, `Glass_Block`), C (as manchas `Stain_*`) e D (as marcações `Mark_Depth`,
`Mark_NoDiving`, `Mark_DeepEnd`, `Mark_LaneEnd`); C e D têm transparência e viram decalques
(`Decal_Stain_*` e `Decal_Mark_*`, postos pela seed em `BuildPoolStains` e `BuildPoolMarks`). Na folha B, onde existe o arquivo-fonte o gerador usa; onde não existe, pinta em
código. Ainda pintados sempre: a segunda ondulação, a segunda rede da cáustica e o vidro das
claraboias. Também gerados ali: `Pool_Wet` (marca molhada da maré), `Pool_TideLine` (linha da maré alta)
e `Pool_Alarm` (lente das lâmpadas do alarme). A planta não fica na cena: sai do `PoolGenerator` pela
seed, em Play.

Os modelos em `Art/Level1/Modelos` (`.fbx` e `materiais_<lote>.json`) e as texturas em
`Art/Level1/Texturas` são **gerados** pelos lotes de `Tools/Blender`; os materiais em
`Art/Level1/Materiais`, por `Level1Models` a partir dos `materiais_*.json`; os de
`Art/Level1/Capitao`, pelo `CaptainPreview`; os de `Art/Level1/Fase` (plástico-bolha, pisos,
etiqueta, base e fibra de vidro do totem), pelo `Level1Setup`. Muda-se o script e roda de novo: o `.fbx` nunca se
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
- **Interagível novo de mira + E entra na mira comum** (`InteractAim`, `World/InteractAim.cs`, desde
  21/11/2026): implementa `IAimSource` (alvo mais perto do centro da tela num cone de 9°, com visada
  livre, ou o raio direto) e age só se `InteractAim.Winner(this)` devolve o alvo. Mira própria em cada
  classe fazia um E acionar duas coisas vizinhas; a porta de saída cede a vez (`InteractAim.Any`).
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
  tombada ou empilhada). A cruz é o meio da célula e um braço até cada lado **aberto**: rente a
  parede fechada não há passagem (até 17/11/2026 a faixa inteira valia, e mesa de jantar e sofá
  encostados nunca entravam). Nenhum móvel invade a sala inicial nem a saída: confere a caixa
  girada inteira (cadeira tombada gira pelo pé e atravessava a parede). `Level1Setup.BatchCheck` confere com uma cápsula entre todas as células.
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
  `AddCollider`) e na `PropLayer` (`Spawn`). Não criar um sem o outro. Nas Piscinas, o que é
  curvo (arco, coluna, abóbada, poço) usa `MeshCollider` da mesma malha; enfeite sem colisor
  (sanca, cáustica, corrimão) entra por nome na lista do `Level0ColliderCheck`.
- **Nas Piscinas, toda célula com piso que não é poço se alcança (a pé, ou nadando na água funda),
  de toda água funda se sai por escada ou pela borda, de todo poço se sai nadando para a funda e o caminho do
  início à saída nunca exige nadar** (`PoolGenerator.FootPath`, só seco e raso). A funda (piso a
  −2,0 m) não dá pé. Divisa aberta entre alturas diferentes sem `PoolStair` é borda: andando, desce-se
  e não se sobe (`LevelData.CanStep`); nadando à tona, sobe-se (`PlayerController.TryStartClimb`, topo
  até 45 cm acima da água: deque seco e piso raso, nunca parede). Toda região de água funda e poço
  tem escada ou borda própria (`PoolGenerator.EveryDeepRegionHasExit`). A garantia do grafo não prova
  que o jogador sai: mudou água, jogador, borda ou escada, rode o `Level2EscapeTest` (um softlock
  assim já chegou ao jogador). Regra nova de planta entra no `PoolGenerator.Validate`, que sorteia de novo
  até passar; coluna nunca fica a menos de (raio + 0,6 m) das linhas de centro das células.
- **A fase das Piscinas não tem luminária:** a luz vem de claraboias e janelas (luz de caixa
  inclinada, sempre para o mesmo lado, sombra de uma face em cache) e o perfil de Volume é o dela.
  Luz pontual com sombra custa seis faces; não usar lá.
- **Lista de cenas da build:** o Level 0 fica sempre em primeiro (é a cena que a build abre).
  `Level0Setup.AddSceneToBuild` garante isso; uma fase nova entra depois dele. Cada montagem tira a
  cena da lista e a põe no fim, então montar o Level 1 depois do Level 2 (ou o contrário) troca os
  dois de lugar em `ProjectSettings/EditorBuildSettings.asset`. Não faz mal (as fases abrem pelo nome
  da cena no `LevelCatalog`, nunca pelo índice), mas não é mudança para commit.
- **Objeto de rede posto por código usa o `NetworkManager` da sessão**, nunca o
  `NetworkManager.Singleton` (`NetworkObject.InstantiateAndSpawn(prefab, manager, ...)`): na troca
  de fase o da cena velha é destruído depois que o da nova ligou e deixa o `Singleton` vazio (o
  Capitão nunca aparecia depois de passar pela porta do Level 0).
- **Rede:** host autoritativo por IP direto (porta 7777), movimento com autoridade do dono.
  Estado que quem entra depois precisa ver (riscos, por exemplo) tem de ser reenviado na conexão,
  como faz `DrawingBoard.SendAll`.
- **O Level 0 e o Level 1 precisam da luz ambiente** (`Level0Setup.AmbientScale`, o céu em degradê do
  perfil): o mapa é gerado ao hospedar e não tem luz rebatida, então sem ela tudo o que nenhuma luminária
  alcança direto fica preto puro. Desligar o céu por desempenho não rende (medido em 20/11 na T2000: 0
  ms); a queda de 27 para 20 ms em 12/11 veio da oclusão de ambiente, que continua desligada.
- **Entrada nova de jogador ignora a tecla com `InputGate.Blocked`** (menu de pausa ou painel do código
  aberto, ou o quadro em que o painel fechou; o jogo não para: o jogador ainda cai, afunda e é pego, só
  não comanda).
- **No menu (sessão fora de `Playing`) o `AudioListener` fica mudo** (`GameOptions.MenuSilence`) e só a
  `MenuMusic` passa (`ignoreListenerVolume`): a fase é montada atrás do menu e os sons dela (zumbido dos
  pontos do objetivo, música da loja) tocavam no menu. Som novo não precisa saber do menu.
- **Barulho de evento para os inimigos passa por `WorldNoise.Emit`** (armários, estalo do interruptor,
  estrondo do objetivo), não por `EntityBrain.HearNoise` de um inimigo só: cada inimigo decide o que ouve
  (`INoiseListener`; o Undertow só o que cai na água dele, o Ascensorista fora do andar só a sirene).
- **Fase roteirizada de inimigo** (`EntityBrain.Scripted` verdadeiro) pula o `HoldStill`. O que a
  subclasse confere lá (fim de um estado com prazo) tem de ser conferido também no `Scripted`, ou trava:
  o Ascensorista ficava parado para sempre na cabine.
- **`PlayerController.GetTaken` dispara `Respawned`** logo depois de pôr o jogador na cabine do
  Ascensorista (é o que manda o salto pela rede). Quem assinar `Respawned` não pode supor que ele quer
  dizer "de volta ao início".
- **Objeto posto em cada máquina por raio ou física só conta a geometria fixa da fase** (nada de
  jogador, inimigo, porta que se mexe, pilha): quem entra depois monta com os outros já andando. Os
  interruptores fazem assim e o host publica um resumo das posições, que o cliente confere; os raios que
  põem os pontos do objetivo ignoram jogadores (`SwitchObjective.WallRay`).
- **Nas Piscinas, na maré alta (+1,3 m) o raso vira nado e o seco é onde se respira** (1,12 m de água;
  desde 21/11/2026 é território do Undertow, que caça no mapa inteiro menos a sala inicial). Maré acima de
  ~1,45 m acaba com onde respirar. Mudou maré, água, jogador, borda ou escada: rode também o
  `Level2HighTideTest`.
- **O corpo do Undertow é comprido (3,4 m, braços de 2,9 m de ponta a ponta): o meio dentro do território
  não basta.** Cada passo e giro confere os pontos do corpo contra os colisores da fase
  (`UndertowBrain.Fits`, `BodyProbes`), a altura sai do piso medido por raio sob o corpo todo (degraus,
  capa, deque do poço redondo) e o `UndertowBody.Fit` recolhe os braços onde eles bateriam. Até 21/11/2026
  só o meio era conferido e ele atravessava parede e escada; o `UndertowTest` confere o corpo o tempo todo.
  A conferência não pode recusar vão que o `UndertowNav` dá como aberto: os braços recolhidos (1,28 m)
  não passavam no vão de porta (1,4 m) nem rente a coluna perto da divisa, e ele travava nas portas. Desde
  21/11/2026 (à noite) ele nada para onde aponta, cruza cada vão pelo eixo, baixo, e no aperto se espreme
  (`SqueezeProbes`, 0,76 m).
- **Teste em dois editores compara as máquinas pela sonda (`NetProbe`)**: o host manda a hora da sessão
  e o estado, e o cliente compara no mesmo instante. Valor que cada máquina calcula pela própria hora
  (maré, apagão, onda) só prova algo junto com a conferência do relógio, e o relógio se mede pelo início
  da sessão em UTC, não na chegada da mensagem (depois de um quadro travado, as mensagens chegam todas
  juntas com o `ServerTime` velho).
- **Pela fila, chamada pelo bash ou por `powershell -File`, `-Extra '-x','y'` chegava ao Unity como um
  argumento só** e os testes rodavam com a seed padrão sem avisar. A fila normaliza desde 21/11/2026
  (`-Extra '-seed,7'` serve de qualquer shell); confira no log que a seed pedida foi a montada.

## Inimigos esculpidos

O traje dos jogadores segue o mesmo caminho a partir do passo 1, mas a forma vem da Tripo e o
esqueleto é que se ajusta a ela (`Docs/modelos-blender.md`, seção 10).

Os cinco inimigos são esculpidos por script no Blender (formas de distância com sinal: elipsoides,
cápsulas, vincos cavados) e vestidos sobre as juntas que os corpos montam e animam em código:
`EntityBody` (`entidade.py`), `CaptainBody` (`capitao.py`), `UndertowBody` (`afogado.py`, deitado, nadando
de bruços), `JanitorBody` (`zelador.py`) e `AttendantBody` (`ascensorista.py`). Os três novos (20–21/11/2026)
seguiram o molde do Capitão: cérebro herdando do `EntityBrain` por ganchos, corpo em código, som
sintetizado, prefab gerado pelo `<Nome>Preview`, teste próprio sozinho e em dois editores. O caminho (detalhes em `Docs/modelos-blender.md`, seção 9):

1. **Esqueleto:** BackRooms > Personagens > Exportar esqueletos (ou
   `CharacterModels.BatchDumpSkeletons`) monta cada corpo na **pose de encaixe** (a pose em que o
   `Build` termina: parada) e grava `Tools/Blender/personagens/esqueleto_<nome>.json`.
2. **Escultura e exportação:** `blender.exe -b --factory-startup -P Tools/Blender/personagens/<nome>.py -- --exportar`
   esculpe sobre esse esqueleto, reduz (~40 mil triângulos), abre o UV num mapa só, assa cor e
   relevo (2048) e calcula os pesos de cada junta. Sem `--exportar`, só fotos de prévia em
   `Logs/Blender/personagens` (`-- --rosto` no Capitão: só a cabeça, mais rápido).
3. **Conferência no Blender:** `conferir_modelo.py -- Capitao` (ou `Entidade`, `Afogado`, `Zelador`,
   `Ascensorista`) lê o `.bytes` de volta e fotografa com as faces de trás escondidas
   (`Logs/Blender/personagens/jogo_*.png`; modelo deitado ganha também a foto `_cima`).
4. **No Unity:** `CaptainPreview.BatchCapture`, `UndertowPreview.BatchCapture`, `JanitorPreview.BatchCapture`,
   `AttendantPreview.BatchCapture` e `Level0Preview.BatchSceneAndCapture` (que recria a cena e o prefab
   `Entity`) criam os materiais e fotografam dentro da fase (`Logs/Previews/<nome>/`).

**Mudou junta, comprimento de osso ou a pose parada** de um corpo? O modelo sai do lugar:
exporte o esqueleto de novo e rode o script com `--exportar`. Mudou só a animação (as rotações
fora da pose parada)? Nada a refazer. Os nomes das juntas são a ligação entre os dois lados (a
lista `BONES` de cada script): renomear junta quebra o modelo (o log avisa qual falta).

## Level 1: onde parou e como continuar

O projeto completo está em `Docs/level1-mostruario.md` (espaço, zonas, catálogo, como os objetos
são postos, o Capitão e as regras dele, objetivo, som, ordem de construção e decisões em aberto).

**Já existe:**
- **Cena de teste** `Scenes/Level1_Test.unity`: a planta do Level 0 em 20 x 20 células, pé-direito de
  3,4 m (`LevelBuilder.ceilingHeight`; o padrão continua 4,05), sem
  buracos, nichos nem entidade (o apagão continua). Para jogar: menu BackRooms > Abrir Level 1
  (teste), Play, H.
- **`PropLayer`:** mobília a planta pela seed. Cada bloco de 2 x 2 células tem um tema (jantar,
  sala de estar, loja, estoque, bagunça, vazio) e um bloco é a sala dos sofás. Móvel grande
  encosta em parede fechada, de frente para a célula; os menores vão nos quatro cantos; o
  desgaste (`LevelGenerator.WearAt`) tomba e empilha mais longe do início. As setas do caminho
  até a saída viram placas amarelas de curva pregadas na parede (`MountExitSigns`). Modelo novo
  só aparece na fase se entrar nas listas de algum tema (`WallItem`, `CornerItem`).
- **65 modelos** feitos no Blender em 9 lotes (lista em `Docs/modelos-blender.md`, seção 7).
- **Zonas** (um tema por bloco de 2 x 2, pela seed): jantar, sala de estar, loja, estoque, bagunça,
  quarto, escritório, cubículos, lavanderia, festa, verão, vazio, a sala dos sofás e o sorteio (o
  carro num beco sem saída fora do caminho, que vira obstáculo como uma barricada). Placas de
  papelão penduradas com o nome da zona e DO NOT ENTER nas barricadas. Longe do início,
  anomalias: mesa de cabeça para baixo no teto, móvel atravessando a parede pela metade,
  guarda-roupa de frente para a parede, batente de porta solto, cama no corredor e um conjunto
  repetido em três salas seguidas (a mesma mobília, idêntica até a cadeira torta).
- **O Capitão na fase** (`CaptainBrain`, regras em `level1-mostruario.md`, seção 5.4): aparece pelo
  `EntityDirector` quando alguém chega a 8 células da sala inicial; ouve e vê como a entidade;
  congela quando algum jogador o tem na tela, sem parede no meio, **e** ele está iluminado
  (luminária acesa a até 6,5 m ou facho de lanterna); quem o encara mais de 6 s vê o chuvisco
  crescer e, aos 9 s, deixa de valer como vigia; manca a 1,7 m/s e, no apagão, caçando, dá
  arrancadas de 5,2 m/s; pega como a entidade (NO SIGNAL). Som: "TOC… arrasta" da perna de pau e,
  perto, o jingle da loja numa caixinha (mais alto, rápido e desafinado de perto; mudo congelado).
  No apagão, com chance de 50%, troca de lugar com um totem que ninguém vê. As pistas para
  separá-lo de um totem: a cabeça vira devagar para o jogador mais perto (até 40°, mesmo
  congelado) e a perna de pau deixa marcas redondas no chão (`PegMarks`, decalques).
- **Abrigo e perigo** (`PropLayer`, pela seed): guarda-roupa e mesa de jantar de esconder (mira + E;
  na mesa, só agachado; o guarda-roupa esconde sempre, a mesa só fora do cone do inimigo, até 6 m;
  dentro, a tela vira a fresta da porta e o jogador não vigia o Capitão); totens (6 a 10: o corpo
  do Capitão na pose parada, em fibra de vidro, numa base preta com a placa amarela DISPLAY ONLY);
  plástico-bolha (barulho de corrida, mesmo agachado); barricadas (2,5% das células, entupidas do
  chão ao teto; nunca isolam nada: `LevelData.Barricade`, que o `LevelNav` evita).
- **Objetivo** (`ExitLock`): a porta de saída fica trancada por um teclado na parede ao lado; mira +
  E nele abre o painel do código na tela (`CodePanel`, UI Toolkit): digita-se pelos números (fileira
  de cima ou numérico) ou clicando, Backspace apaga, Enter confirma, Esc ou E fecha; o jogo não para
  (o jogador só não comanda: `InputGate.Blocked`). O código de 3 dígitos sai da seed e está em
  três etiquetas vermelhas de CLEARANCE, cada uma com o dígito e a posição (1/3, 2/3, 3/3), em
  blocos diferentes e longe umas das outras. Vermelho com CLEARANCE é só delas: as outras placas da
  loja são amarelas (DISPLAY ONLY) ou dizem outra coisa. Sem bilhete na sala inicial (tirado em
  21/11/2026). O cliente manda o código ao host (`GameSession`, mensagens nomeadas), que destranca e
  avisa todos, inclusive quem entra depois.
- **Som e piso:** música de loja baixinha (some antes do aviso do apagão); laminado nas vitrines de
  jantar e de estar e vinílico no estoque, com passos próprios (`Footsteps`: `Laminate`, `Vinyl`).
- **O corpo do Capitão** (`CaptainBody.Pose(fase, velocidade, parado, boca, tempo)`). Modelo esculpido no Blender (seção "Inimigos esculpidos"): mais sujo e
  assustador, de olhos injetados e mãos em garra; parado, estica os braços para a frente. A boca
  não abre mais com `mouth` (o modelo já tem a boca aberta). Desenho a partir de imagens do filme *Backrooms* (2026), a pedido; antes de
  publicar, ver a decisão em aberto 4 de `level1-mostruario.md` (direitos autorais).

**Próximos passos:** jogar à mão (sozinho e em grupo) e ajustar os números (velocidades do
Capitão, quantidade de barricadas, totens e plástico-bolha, distância das etiquetas). A saída
continua a porta corta-fogo com EXIT (decidido em 17/11/2026: sem a porta da doca do projeto).

**Modelos novos:** escreva um lote em `Tools/Blender/lotes/` (ou peça a agentes, um por lote,
com o modelo Sonnet; o roteiro do pedido está em `Docs/modelos-blender.md`, seção 6), rode-o
com `-- --fotos`, olhe as fotos em `Logs/Blender`, acrescente o móvel às listas da `PropLayer`
e rode `Level1Setup.BatchCheck` e `Level1PlayTest.BatchRun`.

## Como testar

No editor, pelo menu **BackRooms**: Abrir Level 0, Abrir Level 1 (teste), Testar Level 0
(fumaça), Testar Level 1 (Play), Testar Capitão (Level 1), Conferir Level 1, Abrir Level 2 (teste), Testar Level 2 (Play), Testar fuga da água funda,
Conferir Level 2, Fotografar Level 2, Abrir Level 3 (teste), Testar Level 3 (Play), Conferir Level 3, Conferir colisores do Level 0, Gerar mapa 2D,
Testar menu de pausa (quatro fases), Testar regras da entidade, Testar Undertow (Level 2), Testar fuga na
maré alta, Fotografar válvulas do Level 2, Testar Zelador (Level 3), Testar Ascensorista (Level 3), Testar
Zelador e Ascensorista juntos (Level 3), Testar som do Level 3, Testar elevadores do Level 3, Fotografar luz
do Level 0, Capturar prévia do Undertow, do Zelador e do Ascensorista,
Capturar prévia, Capturar prévia do Capitão, Móveis do Level 1 > Atualizar e fotografar,
Testar passos do Level 2, Exportar sons (entidade e passos) (WAV em `Logs/Sons`, para ouvir e
ajustar os sons sintetizados sem precisar jogar: os da entidade, `passo_carpete_*`,
`passo_azulejo_*`, `passo_agua_rasa_*`, `nado_bracada_*`, `debaixo_dagua_*`, `espirro_entrada_*`
os do Capitão, `capitao_toc`, `capitao_arrasta` e `capitao_jingle`, e os do Level 1,
`loja_musica`, `plastico_bolha_*`, `passo_laminado_*` e `passo_vinilico_*`; os da maré, `mare_sirene`,
`mare_buzina`, `mare_correnteza`, `mare_glub` e `ralo_gorgolejo`; os do Escritório, `escritorio_ar`,
`escritorio_reator`, `escritorio_difusor`, `armario_bate_*`, `elevador_botao`, `elevador_plim`,
`elevador_ronco`, `interruptor_estalo` e `reator_falhando`; e os dos inimigos novos, `undertow_*`,
`zelador_*` e `ascensorista_*`; em lote, `EntitySounds.BatchExport`).

Por linha de comando, com o **editor fechado** (dois editores não abrem o mesmo projeto). Com mais
de um agente trabalhando, **sempre pela fila** (um pedido de cada vez; espera o editor do usuário
fechar e a árvore compilar):

```
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\unity-fila.ps1 -Metodo BackRooms.EditorTools.Level0SmokeTest.BatchRun [-Extra '-seed,1998'] [-Nome apelido]
powershell ... -File Tools\unity-fila.ps1 -Metodo BackRooms.EditorTools.Level0SmokeTest.BatchHost -MetodoCopia BackRooms.EditorTools.Level0SmokeTest.BatchJoin
powershell ... -File Tools\unity-fila.ps1 -Blender Tools\Blender\personagens\zelador.py -ArgsBlender '--exportar'
```

Sozinho na máquina, direto:

```
"C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe" -batchmode -projectPath "<pasta>" -executeMethod BackRooms.EditorTools.Level0SmokeTest.BatchRun -logFile "<log>"
```

- `Level0SmokeTest.BatchRun` — sozinho: entra em Play, anda, corre, pula, risca encostado na
  parede (a mão não pode passar dela), troca as canetas pelas teclas, atravessa uma faixa, cai num buraco, vê um apagão, grava 3 s de vídeo e é pego pela entidade. Sai com 0 se
  passou, 3 se falhou. O resultado é a linha `[BackRooms] Teste de fumaça PASSOU/FALHOU: ...`
  do log.
- `Level0SmokeTest.BatchHost` + `BatchJoin` — host e cliente ao mesmo tempo. O segundo editor
  precisa de uma **cópia** do projeto em outra pasta (a fila espelha em `..\BR-rede`).
  `BatchHostDrawFirst` + `BatchJoinLate` testam quem entra depois. O apagão é cobrado pelas fases que
  as passadas do teste cobriram e conferido pela hora da sessão; o relógio do cliente é conferido pela
  sonda (`NetProbe`). `BatchJoin` aceita `-atraso N` (o cliente entra N s depois do host; pela fila,
  `-ExtraCopia '-atraso,N'`).
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
  pilha, a cadeira de cima, inclinada, barra o jogador antes da de baixo. O Capitão fica pausado
  (`EntityDirector.Paused`): ele tem teste próprio.
- `Level1CaptainTest.BatchRun` — as regras do Capitão em Play: põe jogador e Capitão a 8–14 m com
  visada livre, sob uma luminária boa, e confere que de costas ele anda; olhado com luz congela e
  o corpo segura a pose; no apagão forçado, olhado sem lanterna, anda em arrancadas; com a
  lanterna nele congela; sem lanterna, pega quem fica parado; com a luz de volta, encarado sem
  parar, volta a andar depois de uns 9 s. Linha `[BackRooms] Teste do Capitão`. Dois editores:
  `BatchHost` (o mesmo teste, esperando o cliente) + `BatchJoin` (na cópia; confere que o Capitão
  aparece, anda e congela com a pose segura no cliente). Desliga a troca com o totem.
- `Level1ShelterTest.BatchRun` — abrigo e perigo do Level 1 em Play: o guarda-roupa (E entra,
  esconde, o Capitão não pega nem é congelado de dentro, E sai pela frente), a mesa (em pé pede
  para agachar; o Capitão a 9 m não vê, a 3 m olhando vê e pega), o plástico-bolha (andando
  devagar no chão comum o Capitão a 6 células não ouve; em cima da folha, ouve), a barricada (não se
  entra) e a troca com um totem num apagão forçado. Linha `[BackRooms] Teste de abrigo do Level 1`.
  Dois editores: `BatchHost` + `BatchJoin` (o cliente vê o host sumir no guarda-roupa e o totem
  trocar de lugar).
- `Level1ObjectiveTest.BatchRun` — o objetivo: três etiquetas em blocos diferentes, nenhum bilhete,
  placa amarela nos totens, porta trancada não abre; o painel do código com teclado e mouse
  virtuais (mira + E abre; aberto não anda, não gira e o 1 não puxa a caneta; Backspace apaga; errado
  pisca vermelho e limpa; Esc fecha sem abrir a pausa; E fecha sem mexer na porta; o certo, por
  clique, numérico e fileira de cima, destranca e fecha) e a porta abre; fotos em
  `Logs/Previews/level1_placas/painel_*.png`. Linha `[BackRooms] Teste do objetivo do Level 1`. Dois
  editores: `BatchHost` + `BatchJoin` (o cliente digita no painel um código errado e o certo; o host
  destranca) e `BatchHostLate` + `BatchJoinLate` (o host destranca; o cliente entra depois e acha a
  porta destrancada).
- `Level1SoundTest.BatchRun` — som do Level 1 com teclado virtual: passos no laminado, no vinílico e
  no carpete; a música da loja toca e se cala antes do aviso do apagão; o jingle do Capitão alto e
  agudo a 3 m, baixo a 13 m. Linha `[BackRooms] Teste de som do Level 1`.
- `LevelSelectTest.BatchRun` — seletor de fase em Play: fase bloqueada sem progresso, troca
  para o Level 1 com uma sessão só, a porta do Level 1 zera e leva ao Level 2 já hospedando,
  nada num poço, a porta do Level 2 zera e leva ao Level 3 já hospedando, a porta do Level 3 (última
  fase) zera, encerra a fita e devolve ao menu, volta ao Level 0 sem
  restos da água, e a porta do Level 0 leva ao Level 1 já hospedando. Dois editores:
  `LevelSelectTest.BatchHostLevel1` + `BatchJoinFromLevel0` (o cliente tem de ser levado ao
  Level 1 e montar a mesma mobília) e `BatchHostDoor` + `BatchJoinDoor` (o cliente abre a porta
  e espera; quando o host abre, os dois passam para o Level 1 e o cliente reconecta). Confere
  também a música do menu (`MenuMusic`): um dono só depois de cada troca, nunca recomeça, calada
  em jogo e de volta (do mesmo ponto) no menu. Restaura o progresso salvo; linha
  `[BackRooms] Teste do seletor de fase`.
- `CharacterModels.BatchDumpSkeletons` — grava os esqueletos dos inimigos para o Blender
  (`Tools/Blender/personagens/esqueleto_*.json`).
- `Level0Preview.BatchCaptureHud` — só o painel da filmadora, em 1920 x 1080, de frente para a
  porta de saída (`Logs/Previews/painel_*.png`). Não recria a cena.
- `Level0Preview.BatchCaptureHandWall` — a mão em primeira pessoa riscando uma parede a 1,5 m,
  0,8 m, 0,4 m e 0,15 m, de frente e a 45°, as três canetas e o traje com a caneta colado na
  parede (`Logs/Previews/mao_parede/*.png`); escreve no log quanto cada uma passa da parede. Não
  recria a cena.
- `Level2Setup.BatchCheck` — recria a cena e os materiais das Piscinas e confere 5 seeds
  (`PoolGenerator.Validate` sem problemas, mesma seed = mesmo `Hash`, perfil de Volume da fase,
  água e cáustica na cena); fotos de cada tipo de sala em `Logs/Previews/level2_look_*.png` (com
  a filmadora) e `level2_limpa_*.png` (sem ela, para medir cor).
- `Level2Preview.BatchCapture` (`-seed N`, padrão 1998) — recria a cena e tira, em
  `Logs/Previews/level2_*.png`, a planta vista de cima, duas fotos por sala (da porta e do meio) e os
  closes: arco, arco largo, vigia, vigia acesa, janelão, frestas, tijolo de vidro, escada entrando na
  água, poço, claraboia, teto luminoso, até três manchas e debaixo d'água. Apaga antes todas as
  `level2_*.png` (inclusive as do `BatchCheck` e as dos closes). Lento fora do Play (uns 13 minutos).
- `Level2Preview.BatchCloseups` (`-seed N`, padrão 1998) — também recria a cena, mas tira só os
  closes do acabamento, em `Logs/Previews/level2_s{seed}_*.png`: tijolo de vidro e janelão (de frente
  e de lado), poço redondo (de cima e debaixo d'água), emenda do teto de vidro (aceso e apagado), um
  close de cada tipo de mancha e de marcação, as duas salas com mais decalques e duas vistas longas.
  Apaga só as fotos da mesma seed, não as outras. Escreve no log quantas manchas e marcações a seed
  tem e quais das seeds de conferência têm poço redondo, tijolo de vidro e janelão. Com `-so borda`,
  só a capa da borda; com `-so fundo`, só a água funda (piscina do deque, boiando e debaixo d'água,
  as escadas mais longas que saem dela, a raia funda e o poço redondo; `level2_s{seed}_fundo_*.png`).
- `Level2PlayTest.BatchRun` (`-seed N`) — as Piscinas em Play: nasce no seco, anda o caminho a pé
  até a saída (sem nadar) pelas escadas e arcos, fica à tona num poço segurando Espaço, afunda e se
  afoga sem tecla, sobe e sai nadando para a área funda; desce uma escada para a água funda e passa
  a nadar, atravessa à tona, afunda até o fundo (2 m), sobe e sai subindo a escada; do fundo, sem
  Espaço, anda até a escada e sobe os degraus andando; sobe na borda nadando à tona (para o deque
  seco e para o piso raso; do fundo só com W e à tona contra uma parede não sobe) e, andando no raso
  contra uma borda, não sobe no seco; por fim, o anoitecer.
- `Level2EscapeTest.BatchRun` (`-seed N` ou `-seeds A,B,C`; menu Testar fuga da água funda) — para
  cada região de água funda e poço, põe o jogador no fundo (na célula mais longe da escada, numa do
  meio e numa de poço) e, só com o teclado virtual, sobe à tona e nada até a escada mais próxima e,
  noutra tentativa, até a borda mais próxima; passa se todas saem (de pé no seco ou no raso). Fôlego
  sem fim e tempo de jogo acelerado (12 seeds em poucos minutos); cada tentativa em
  `Logs/Fuga/fuga.txt`, fotos de quem ficou preso em `Logs/Fuga`. Linha `[BackRooms] Teste de fuga`.
- `Level0ColliderCheck.BatchRunLevel2` — colisores contra a malha visível na cena das Piscinas.
- `Level2SoundTest.BatchRun` (`-seed N`) — passos por superfície nas Piscinas, em Play com teclado
  virtual: anda no azulejo seco e na água rasa, cai de 1,2 m na água (um espirro só), nada à tona
  num poço e numa célula funda comum, mergulha e desce uma escada do seco para a água andando (sem
  espirro) e correndo (um espirro); em cada lugar confere a superfície escolhida (`Footsteps.Surface`) e que o som
  dela tocou, e nenhum passo de carpete. Sai com 0 se passou, 3 se falhou; linha
  `[BackRooms] Teste de som do Level 2`. Ouvir de verdade é com gente: exporte os WAV (acima).
- `Level3Setup.BatchCheck` — recria a cena e os materiais do Escritório e confere 8 seeds
  (`OfficeGenerator.Validate` sem problemas, mesma seed = mesmo `Hash`, uma porta de saída, três
  elevadores, início na cabine, caminho até a escada e escada até a porta livres para uma cápsula do
  tamanho do jogador, chão sob cada ponto da escada); fotos em `Logs/Previews/level3_*.png` (cabine,
  saguão, corredor nos dois sentidos, salas, cafeteria, área de estar, porta da escada de perto e de
  longe, vão da escada, saída, placa). Sai com 0 se passou, 3 se falhou.
- `Level3MenuPhoto.BatchRun` — não é teste: o menu inicial com o Level 3 escolhido e a vista do
  Escritório atrás dele (`LevelBuilder.MenuView`), em `Logs/Previews/office/menu_level3.png`.
- `Level3PlayTest.BatchRun` — o Escritório em Play: nasce na cabine, a porta dela abre sozinha, anda o
  caminho e desce a escada até a porta da saída (passos de piso duro), espera a onda de luzes apagar um
  trecho e abre a porta: última fase, zera e acaba no menu. Restaura o progresso salvo; linha
  `[BackRooms] Teste do Level 3`.
- `ObjectiveTest.BatchRun` (Level 0), `BatchRunLevel2`, `BatchRunLevel3` — o objetivo comum em Play:
  pontos longe uns dos outros e do início, porta trancada (chacoalha), aviso da mira, segurar E faz
  barulho e arma, sozinho não expira, com dois jogadores contados (`SwitchObjective.PlayersForTest`) o
  ponto vence na janela e dois fora dela não cumprem, dois dentro cumprem, a porta abre com som. No
  Level 2 o jogador vai **nadando** até a válvula da água (e a gira boiando, se ela fica sobre a funda)
  e confere que a água desce e o alarme da maré cala; no Level 3, que a sirene chega ao Ascensorista
  (escondido num poço, ele chega mais cedo e anda para o acionador). Aceita `-seed N` (padrão 1998).
  Dois editores (Level 0): `BatchHost` + `BatchJoin` (cada um arma um ponto dentro da janela). Linha
  `[BackRooms] Teste do objetivo`.
- `Level2TideTest.BatchRun` (`-seed N`) — a maré com o relógio forçado (`PoolTide.ForceSeconds`): o
  alarme (sirene, buzina, lâmpadas, painel), o ronco e a agitação; sobe 1,3 m, o seco vadeia sem correr
  (~1/3 da corrida), de pé no raso sem tecla a cabeça fica debaixo d'água e segurando Espaço sobe; na
  vazante o ralo puxa quem nada sobre o poço até a grade, parada não puxa, longe dos poços não puxa; a
  marca molhada fica; válvulas abertas no meio de uma subida fazem a água descer na hora. Linha
  `[BackRooms] Teste da maré`. Os outros testes das Piscinas pausam a maré (`PoolTide.Paused`) e o
  Undertow (`UndertowDirector.Paused`).
- `Level2HighTideTest.BatchRun` (`-seed N` ou `-seeds A,B,C`; menu Testar fuga na maré alta) — a fuga na
  maré alta: maré forçada no alto, fôlego de verdade, Undertow pausado; de pé em cada raso do caminho a
  pé e nos rasos e fundos mais longe de um seco, só com o teclado virtual, chega-se de pé ao seco
  respirando. Linha `[BackRooms] Teste da maré alta`; detalhes em `Logs/MareAlta/mare_alta.txt`.
- `Level2ValveSurvey.BatchRun` (`-seeds`; padrão 1998, 7, 4242, 31337, 90210, 1, 42, 2026) — fora do
  Play: onde caem as válvulas, quanto se nada até a da água, a distância da rota ao covil e ao ralo,
  se entra na água por escada; fotos em `Logs/Previews/valvulas`. Linha `[BackRooms] Válvulas do Level 2`.
- `Level2Preview.BatchTide` (`-seed N`; sem ela, 1998 e 7) — fotos da maré do mesmo ponto em água baixa,
  alarme, maré alta e depois, em `Logs/Previews/mare`.
- `UndertowTest.BatchRun` — o Undertow em Play: nadar submerso não o acorda, o espirro acorda (com
  rugido), nadar à tona longe não acorda, quem sobe na borda escapa enquanto ele ronda, pega e puxa quem
  nada perto; na maré alta entra no raso, ruge e ronda, pega quem anda no seco alagado, perde quem fica
  parado e quieto e não entra na sala inicial; na vazante some pelos ralos; surge noutra água pelos canos,
  em cada água funda pelo barulho dela e muda de água pelo relógio; drenado, se arrasta e pega quem anda
  no fundo. O tempo todo: nunca fica fora do território, nunca entra na sala inicial e nenhuma parte do
  corpo entra em colisor fixo da fase (4 cm de tolerância). Linha `[BackRooms] Teste do Undertow`. Dois
  editores: `BatchHost` + `BatchJoin` (o cliente espirra perto do covil e nada, é pego e puxado, ouve o
  rugido; o corpo é conferido nas duas máquinas). `UndertowPreview.BatchCapture` (`-seed N`): fotos em
  `Logs/Previews/undertow/` (com `-fotoTag X`, as séries da braçada e da maré alta saem com o prefixo X_;
  com `-so bracada` ou `-so mare`, só elas). `Level2EscapeTest -undertow` roda com ele ativo e
  conta à parte as tentativas pegas.
- `NetTests.BatchHostLevel2` + `BatchJoinLevel2` (e `Level3`) — dois editores: planta, pontos do
  objetivo e pilhas iguais, e relógio igual; maré igual no mesmo instante (Level 2); pilhas pegas por um
  somem no outro; o cliente sai e volta (quem entra depois) e acha as pilhas sumidas; no Level 3, o
  cliente aperta um interruptor (RPC ao host), sai, o host apaga outro trecho e o cliente volta e acha o
  interruptor recarregando e a mesma onda do host; cumprido o objetivo, porta aberta e água baixa (2) ou
  sirene (3), também para quem entra depois. Linha `[BackRooms] Teste de rede (Level N, host|cliente)`.
- `EntityRulesTest.BatchRun` — as regras da entidade em Play no Level 0: campo de visão (colada nas
  costas e do lado não vê nem pega; 75° não vê, 55° vê; além do alcance não vê), cansaço na caça, pressa
  só sem ser vista e fora da caça, de frente a 1 m pega; depois, fora do Play, o abrigo na planta do
  Escritório (`LevelData.IsShelter`) em 8 seeds. Lê `_velocity` e `_chaseSince` do `EntityBrain` por
  reflexão (renomear quebra o teste, que avisa). `BatchRunLevel0` e `BatchRunShelter` rodam uma parte só.
  Dois editores: `BatchHost` + `BatchJoin` (a posição dela no cliente é a do host no mesmo instante).
  Linha `[BackRooms] Teste das regras da entidade`.
- `BatteryTest.BatchRun` — a lanterna gasta bateria; pilhas no mapa; mira + E pega, some e recarrega;
  não se pega duas vezes. Linha `[BackRooms] Teste das pilhas`.
- `JanitorTest.BatchRun` — o Zelador em Play: não entra na luz e para na beira, não pega na luz a 0,9 m,
  pega no escuro, some com a luz, aparece sozinho, hesita com a lanterna; a onda escolhe o trecho do
  jogador, pisca antes de apagar, ele nasce na ponta justa; o interruptor religa e ele some, o estalo
  chega ao Ascensorista, a recarga recusa; no alarme, andar escuro com emergência e sem interruptor, e a
  fuga até a escada. Linha `[BackRooms] Teste do Zelador`. Dois editores: `BatchHost` + `BatchJoin`.
  `JanitorPreview.BatchCapture`: fotos em `Logs/Previews/zelador`.
- `AttendantTest.BatchRun` — o Ascensorista em Play: chega pelo poço da seed (seta, porta, sai), ronda
  parando para escutar, volta e parte; agachado e parado a 4 m ele passa; correndo a uns 10 m ele vem e
  pega quem está agachado no caminho dele (respiração); quem é pego aparece na cabine dele com a porta
  fechando antes de cair o sinal; ele entra na salinha acesa; o alarme o chama (no andar e fora dele);
  trancado numa cabine de poço não é ouvido, mas é levado se ele chega por ela. Os armários ficam
  calados. Linha `[BackRooms] Teste do Ascensorista`. Dois editores: `BatchHost` + `BatchJoin`.
  `AttendantPreview.BatchCapture`: fotos em `Logs/Previews/ascensorista`.
- `Level3EnemiesTest.BatchRun` — o Zelador e o Ascensorista na mesma partida, sem pausar nada: pelo
  relógio a onda traz o Zelador e o Ascensorista chega de elevador; com os dois no andar, o jogador
  aperta o interruptor do trecho escuro, o Zelador sai dele e o estalo chega ao Ascensorista; o Zelador
  nunca fica em célula acesa e nenhum erro no console. Linha `[BackRooms] Teste dos dois inimigos do Level 3`.
- `Level3SoundTest.BatchRun` — o som do Escritório: calado no menu, o ambiente entra jogando, os reatores
  do trecho que a onda apaga calam e voltam; os armários batem na hora da seed, longe do jogador, e o
  barulho chega ao inimigo pela via genérica. Linha `[BackRooms] Teste de som do Level 3`.
- `Level3ElevatorTest.BatchRun` — os botões dos elevadores: fechar e abrir a cabine, a porta reabre com o
  jogador no vão, `HeldOpen` recusa fechar, chamar abre o poço e ele fecha sozinho, de dentro de um poço
  fechado "abrir" funciona. Dois editores: `BatchHost` + `BatchJoin` (quem entra depois acha a cabine
  fechada pelo host; o chamado do cliente abre o poço no host).
- `PauseMenuTest.BatchRun` (quatro fases; `BatchRunLevel0` só o Level 0) — no menu o ouvinte está mudo e
  só a música passa; o cartaz não diz "ao mesmo tempo" e lembra o Esc; com o menu aberto o jogador não
  anda, não gira nem liga a lanterna; as tarefas e as teclas aparecem (sozinho, com dois jogadores e com
  um ponto armado); "Quit to menu" volta ao menu; todo texto visível do menu inicial, do fim da fita e
  do menu de pausa está em inglês; o painel do menu some com o fim da fita na tela e volta com "Back to
  menu". Fotos em `Logs/Previews/pausa` e `Logs/Previews/ingles` (e `Logs/Previews/fecho/fim_da_fita.png`).
- `Level0LightPreview.BatchRun` — fotos e medida da luz do Level 0 onde ela costuma faltar (beco ao lado
  de luminária, canto de célula acesa e apagada, nicho, célula mais longe de luz), em várias seeds
  (`-luzSeeds 1998,7,4242`) e variantes montadas sem salvar nada (`-luzVariantes base,ambiente4,fade60,
  semsombra,performant,apagao...`); mede a % de pixels pretos (log `[BackRooms] luz0`, `medidas.txt`).
  Pasta `Logs/Previews/luz0/<-luzTag>`.
- `BuildTool.BatchWindows` — o jogo para Windows 64 bits com as fases da lista da build, em
  `Builds/Windows/TheBackRooms.exe` (fora do Git; ~40 min na primeira vez). Linha
  `[BackRooms] Build para Windows`.
- `GameOptionsCheck.BatchRun` — fora do Play: volume, mouse e qualidade presos aos limites, aplicados e
  guardados (`GameOptions`). Linha `[BackRooms] Conferência das opções`.
- `Level1Models.BatchUpdateAndCapture` — cria os materiais dos lotes, reimporta os modelos,
  escreve as medidas no log e salva fotos em `Logs/Previews/moveis_*.png` (todos, ou só os de
  `-moveis Nome1,Nome2`), além da vitrine de jantar, da loja e da sala dos sofás.
- `CaptainPreview.BatchCapture` — fotos do Capitão dentro do Level 0, em
  `Logs/Previews/capitao_*.png` (inclusive closes do rosto). Regrava os materiais dele.
- Modelos do Blender: `blender.exe -b --factory-startup -P Tools/Blender/lotes/<lote>.py -- --fotos`
  (fotos rápidas em `Logs/Blender`). Sozinho na máquina, vários lotes podem rodar ao mesmo tempo; com
  outros agentes, pela fila (`-Blender`), porque o Unity e o Blender juntos estouram os 16 GB.

Não passar `-quit`: os métodos encerram o editor sozinhos. Depois de rodar em lote, o editor
pode abrir numa cena vazia; o `Level0AutoUpdate` troca para a cena do jogo.

Mudança em jogador, item, fase ou rede só está pronta depois do teste de fumaça sozinho; mudança
em rede, depois do teste com dois editores; mudança no Level 2 (ou na água, no jogador, no
`LevelBuilder`, no `PoolGenerator` ou no `Level2Setup`), depois de `Level2Setup.BatchCheck`,
`Level2PlayTest.BatchRun`, `Level2EscapeTest.BatchRun` (nas seeds de conferência),
`Level2HighTideTest.BatchRun` (nas seeds de conferência), `Level0ColliderCheck.BatchRunLevel2`,
`UndertowTest.BatchRun` e `LevelSelectTest.BatchRun` (a
porta do Level 1 leva às Piscinas e a delas, ao Escritório); mudança no Level 3 (ou no `OfficeGenerator`,
no `LevelBuilder.Office*`, no `OfficeEvents` ou no `Level3Setup`), depois de `Level3Setup.BatchCheck`,
`Level3PlayTest.BatchRun`, `Level3SoundTest`, `Level3ElevatorTest` e `LevelSelectTest.BatchRun`; mudança no
Zelador, na onda de luzes, nos interruptores ou no `JanitorDark`, depois de `JanitorTest` (sozinho e em
dois editores), `Level3EnemiesTest` e `Level3PlayTest`; mudança no Ascensorista ou nos elevadores, depois
de `AttendantTest` (sozinho e em dois editores), `Level3EnemiesTest` e `Level3ElevatorTest`; mudança no
Undertow (ou na água, no `Footsteps` ou no `PlayerController` nadando), depois do `UndertowTest` sozinho e
em dois editores e dos testes das Piscinas; mudança em texto ou menu, depois do `PauseMenuTest`; mudança no Level 1 (ou no `LevelBuilder`, no gerador
ou nos modelos), depois de `Level1Setup.BatchCheck` e `Level1PlayTest.BatchRun`; mudança no
Capitão (ou no `EntityBrain`, que ele e os três inimigos novos herdam), depois de
`Level1CaptainTest.BatchRun`, `EntityRulesTest`, do teste de fumaça e dos testes dos inimigos novos; mudança no abrigo, no objetivo ou no som do Level 1 (`PropLayer`, `HideSpot`, `ExitLock`,
`ShopMusic`), depois de `Level1Setup.BatchCheck`, `Level1ShelterTest`, `Level1ObjectiveTest` e
`Level1SoundTest` (os dois primeiros também em dois editores); mudança nos
passos (`Footsteps`), depois do teste de fumaça e de `Level2SoundTest.BatchRun`; mudança no objetivo
comum (`SwitchObjective`), depois do `ObjectiveTest` nas três fases e em dois editores e do
`PauseMenuTest`; mudança na maré (`PoolTide`, `PoolWater`), depois do `Level2TideTest`, do
`Level2HighTideTest` e dos testes das Piscinas; mudança em rede do Level 2 ou do Level 3, depois do
`NetTests` da fase. Diga no resumo o
que foi testado por máquina e o que não foi testado à mão.

## Objetivo comum

`SwitchObjective` (`PC.1` de `Docs/mecanicas.md`): N pontos distantes armados dentro de uma janela de
tempo. O `LevelBuilder.objective` diz o tipo (`Breaker` no Level 0, posto sempre pelo `Level0Setup`;
`Valve` no Level 2 e `FireAlarm` no Level 3, pelo `SceneOptions.objective`). Regras:

- Os pontos (e as pilhas, `BatteryPickups`) saem de um `System.Random` próprio, **depois de tudo**: não
  mudam o hash da planta. Parede lisa e livre achada por raio a partir do meio da célula; os raios
  ignoram jogadores (quem entra depois monta igual).
- Não é "ao mesmo tempo": o primeiro ponto armado abre a janela e os outros (2 com até dois jogadores,
  3 com três ou quatro: `RequiredFor`; no Level 3, os 3 alarmes sempre, também sozinho) têm de vir
  dentro dela; sozinho não há janela.
- No Level 3 os alarmes ficam dentro de salas, um por sala, fora da vista da porta e do vidro e longe da
  cabine e da escada (`SwitchObjective.ChooseAlarms`; o log diz a sala de cada um); sem três salas que
  sirvam, voltam aos corredores.
- Nas Piscinas, a válvula 0 fica no seco (a pé) e a 1 só se alcança nadando (`WaterValveSpot`:
  `AcrossWater`, numa ilha ou na parede sobre a funda; `WellBottom`, no fundo de um poço, desligado;
  `LevelBuilder.waterValve`), por sorteio próprio fugindo da água do covil do Undertow (onde ele começa;
  depois ele muda de água).
- Quem segura E faz barulho de corrida (`PlayerNetwork.Working`, lido pelo `EntityBrain`); cumprido, o
  host solta o estrondo pelo mundo (`WorldNoise.Emit`, `CompleteNoiseRadius` 500 m) a partir do último
  ponto, e o evento `SwitchObjective.Completed` dispara uma vez em todas as máquinas (inclusive em quem
  entra depois): é o que corta a energia do Escritório.
- O cliente avisa o host (`BackRooms.SwitchArm`); o host arma pela hora de `SessionSeconds` e manda o
  estado inteiro a todos e a quem entra depois (`BackRooms.SwitchState`, guardado em
  `SwitchObjective.Pending` se chegar antes da montagem). Pilhas: `BackRooms.Pickup` / `BackRooms.Pickups`.
- `LevelExit.Locked` junta o `ExitLock` e o `SwitchObjective`. Nos testes que só querem passar pela
  porta, o host cumpre com `SwitchObjective.Instance.CompleteForTest()` (o `LevelSelectTest` faz isso).
- Peças em escala (cubos primitivos com o colisor deles): o `Level0ColliderCheck` copia a escala.
- Cada fase com objetivo comum ganha o cartaz de dicas na sala inicial (`SwitchObjective.PinNote`,
  em inglês: TIPS, quatro dicas e "ESC: TASKS"; "Placa" sem colisor). A instrução do objetivo fica no
  menu de pausa (`ITaskSource`: tarefas com o detalhe de onde ficam os pontos e da janela). A porta do Level 3 encerra a fita: `LevelProgress.EndTape` e a tela de fim
  no `ConnectionMenu`, com os tempos de `LevelProgress.RecordTime`.

## Git

- Repositório: `Rabarbuti/The-BackRooms`. A **`main` é a única branch que dura**: todos os
  níveis vivem nela (fase incompleta fica escondida do jogador pelo `LevelProgress`).
- **Branch por tarefa, curta** (horas ou poucos dias: `porta-saida`, `capitao-cerebro`), saindo
  da `main` atualizada e voltando por PR assim que os testes passam; depois do merge, apaga-se a
  branch. Nada de branch por fase: ela se afasta da `main` e o conflito cresce.
- **Puxe a `main` todo dia** (`git pull` na `main`, `git merge main` na sua branch) e avise no grupo
  antes de mexer em arquivo que todos tocam (`GameSession`, `Camcorder`, `LevelBuilder`, docs).
- **Conflito em conteúdo gerado** (cenas, prefabs, materiais, `Art/Level*`): não se resolve à mão;
  fique com qualquer lado e rode o gerador de novo. `Docs/historico.md` junta as duas entradas
  sozinho (`merge=union` no `.gitattributes`).
- O PR diz o que passou por máquina e o que não foi testado à mão.
- **PNG, FBX e MP3 vão direto no Git** (`binary` no `.gitattributes`): a conta está sem orçamento
  de LFS. O único MP3 é a música do menu (~1,7 MB). Os outros binários continuam em LFS; evite
  adicionar áudio, `.blend` ou vídeo sem combinar antes.
- Todo arquivo novo em `Assets/` vai com o `.meta` correspondente.
