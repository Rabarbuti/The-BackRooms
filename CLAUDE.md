# The BackRooms

Jogo de terror 3D em co-op no estilo Backrooms, feito em Unity. Hoje existe o Level 0
jogável por IP, com interface de filmadora VHS, o primeiro item (caneta de riscar), buracos
com faixas de travessia, apagões periódicos, a primeira entidade, que caça os jogadores pelo
barulho, e gravação da tela em vídeo (F9).

Conversa, comentários de código, documentação e mensagens de commit são em **português**.

## Ambiente

- Unity **6000.6.4f1**, HDRP 17.6 (não trocar de pipeline), Netcode for GameObjects 2.13.3.
  Até 11/11/2026 o projeto era 6000.3.26f1 / HDRP 17.3; a troca de versão é um commit só, se
  precisar voltar.
- Só o **Input System novo**: nada de `UnityEngine.Input`. As ações são criadas em código
  (`new InputAction(...)`), como em `PenTool` e `PlayerController`.
- Interface em UI Toolkit; o efeito VHS é um Custom Pass (`CamcorderPass` + `CamcorderVHS.shader`).
- Sem `.asmdef`: tudo compila em `Assembly-CSharp`, e o que está em `Scripts/Editor` em
  `Assembly-CSharp-Editor`.

## Onde fica cada coisa

Todo o jogo está em `Assets/_Game`. Namespaces seguem a pasta: `BackRooms.World`, `.Player`,
`.Net`, `.Items`, `.Entities`, `.UI`, `.Audio` e `BackRooms.EditorTools` para `Scripts/Editor`.

- `Scripts/World` — `LevelGenerator` (planta a partir da seed) e `LevelBuilder` (malhas e colisores).
- `Scripts/Network` — `GameSession` (host, cliente, envio da seed) e `PlayerNetwork`.
- `Scripts/Items` — `PenTool` (entrada e mira) e `DrawingBoard` (malhas dos riscos e sincronização).
- `Scripts/Entities` — a entidade: `EntityDirector` (quando e onde aparece), `EntityBrain` (sentidos
  e decisão, só no host), `LevelNav` (caminho na grade do mapa), `EntityBody` (corpo de cipós e
  animação), `EntityHeadMesh` (cabeça esculpida como malha) e `EntityAudio`, tudo feito em código.
- `Scripts/Systems/FigureKit.cs` — peças para montar figuras em código (junta, osso, volume). A
  entidade, o traje dos jogadores (`PlayerBody`) e a mão em primeira pessoa (`ItemHand`) usam.
- `Scripts/UI` — filmadora (`Camcorder`, `CamcorderPass`) e gravação em vídeo (`ScreenRecorder`,
  `AviWriter`, `RecorderAudioTap`).
- `Scripts/Editor` — gerador do Level 0 e ferramentas de teste (menu **BackRooms**).
- `Docs/plano-coop.md` — plano do jogo, arquitetura de rede e registro das correções. Ler antes
  de mexer em rede ou em geração de fase. Os outros arquivos de `Docs/` são referência de design.

## Conteúdo gerado: não editar à mão

A cena `Scenes/Level0_Test.unity`, os prefabs `Player` e `Entity`, o perfil de Volume e tudo em
`Art/Level0` (texturas e materiais) são **gerados** por `Level0Setup` (menu BackRooms > Montar
Level 0). Mudança nesses arquivos se faz no gerador, não no arquivo.

Exceção: `Art/Fontes` guarda os modelos **GLB originais** gerados por IA (Higgsfield). Eles não são
gerados pelo `Level0Setup`, que os lê com `GlbReader` (sem pacote de glTF) e assa em `Art/Level0`
a malha já encaixada e as texturas do HDRP (`ImportedModels`). Apagar um GLB volta à peça antiga.

Ao mudar algo que só aparece depois de recriar (texturas, materiais, objetos da cena, valores
padrão do `LevelBuilder`), **aumente `Level0Setup.ContentVersion`**. O `Level0AutoUpdate` compara
com a versão gravada na cena e oferece recriar; sem isso o jogo roda código novo sobre conteúdo
velho. Mudança só de código não precisa.

## Regras que já custaram um defeito

- **Raio saindo da câmera do jogador ignora o próprio jogador.** O colisor do
  `CharacterController` só acompanha o corpo no passo de física seguinte; pulando, correndo ou
  recuando, a câmera sai de dentro dele e um `Physics.Raycast` comum o acerta. Use o padrão de
  `PenTool.TryAim` (`RaycastNonAlloc` descartando `transform.root` igual ao do jogador) em todo
  item novo: interagir, pegar, mirar.
- **Geração de fase é determinística.** O host envia só a seed e cada máquina monta o mesmo
  labirinto, então `LevelGenerator` e `LevelBuilder` usam apenas `System.Random(seed)`, nunca
  `UnityEngine.Random`, tempo ou ordem de dicionário. O teste de fumaça compara o hash do mapa
  entre host e cliente.
- **Buraco nunca isola parte do mapa.** Toda célula com piso continua alcançável sem pisar em
  faixa, então sempre há um caminho seguro até a saída (`LevelData.SafePathCells`). Quem mexer
  em `LevelGenerator.DigPits` mantém a conferência de `SafeDetour`.
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
- **Mudança de aparência de personagem passa por imagem antes de entrar no jogo.** As fotos saem
  da ferramenta de prévia (`Level0Preview`), renderizadas dentro da fase.
- **Colisor e malha visível nascem juntos** no `LevelBuilder` (`AddWall`, `AlcoveBox`,
  `AddCollider`). Não criar um sem o outro.
- **Rede:** host autoritativo por IP direto (porta 7777), movimento com autoridade do dono.
  Estado que quem entra depois precisa ver (riscos, por exemplo) tem de ser reenviado na conexão,
  como faz `DrawingBoard.SendAll`.

## Como testar

No editor, pelo menu **BackRooms**: Testar Level 0 (fumaça), Conferir colisores do Level 0,
Gerar mapa 2D, Capturar prévia, Exportar sons da entidade (WAV em `Logs/Sons`, para ouvir e
ajustar os sons sintetizados sem precisar jogar).

Por linha de comando, com o **editor fechado** (dois editores não abrem o mesmo projeto):

```
"C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe" -batchmode -projectPath "<pasta>" -executeMethod BackRooms.EditorTools.Level0SmokeTest.BatchRun -logFile "<log>"
```

- `Level0SmokeTest.BatchRun` — sozinho: entra em Play, anda, corre, pula, risca, atravessa uma
  faixa, cai num buraco e é pego pela entidade. Sai com 0 se passou, 3 se falhou. O resultado é a linha `[BackRooms] Teste de fumaça PASSOU/FALHOU: ...` do log.
- `Level0SmokeTest.BatchHost` + `BatchJoin` — host e cliente ao mesmo tempo. O segundo editor
  precisa de uma **cópia** do projeto em outra pasta. `BatchHostDrawFirst` + `BatchJoinLate`
  testam quem entra depois.
- `Level0ColliderCheck.BatchRun` — colisores contra a malha visível, em 8 seeds.

Não passar `-quit`: os métodos encerram o editor sozinhos. Depois de rodar em lote, o editor
pode abrir numa cena vazia; o `Level0AutoUpdate` troca para a cena do jogo.

Mudança em jogador, item, fase ou rede só está pronta depois do teste de fumaça sozinho; mudança
em rede, depois do teste com dois editores. Diga no resumo o que foi testado por máquina e o que
não foi testado à mão.

## Git

- Repositório: `Rabarbuti/The-BackRooms`. Trabalho em branch (hoje `level0-coop`), nunca direto na `main`.
- **PNG vai direto no Git** (`*.png binary` no `.gitattributes`): a conta está sem orçamento de
  LFS. Os outros binários continuam em LFS; evite adicionar áudio, modelos ou vídeo grandes sem
  combinar antes.
- Todo arquivo novo em `Assets/` vai com o `.meta` correspondente.
