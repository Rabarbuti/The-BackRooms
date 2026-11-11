# Modelos no Blender para o Unity

Como os modelos 3D do jogo são feitos no Blender e entram no projeto. Escrito em 11/11/2026,
a partir do guia de modelagem para Unity enviado na conversa (fluxo, formatos, importação,
orçamento de polígonos, armadilhas), adaptado ao que este projeto já faz.

Hoje existem por este caminho os móveis do Level 1, em quatro lotes (seção 7). A entidade do Level 0, o Capitão,
o traje e a mão continuam montados em código (`FigureKit`); passar o Capitão para o Blender é
o próximo candidato (seção 6).

---

## 1. A ideia: modelo também é conteúdo gerado

O resto do jogo (fase, texturas, materiais, figuras) sai de código, e é isso que deixa tudo
refazível e revisável no Git. Os modelos seguem a mesma regra:

- **O modelo é um script Python** em `Tools/Blender/`, rodado pelo Blender sem janela. O
  script é a fonte; o `.fbx` é o resultado.
- **Não se edita o `.fbx` nem se salva `.blend`.** Para mudar um móvel, muda-se o script e roda
  de novo. Quem quiser esculpir à mão no Blender pode, mas o resultado precisa voltar como
  script ou como um `.blend` combinado antes (os `.blend` iriam para o LFS, que está sem
  orçamento).
- **Tudo determinístico:** o acaso de cada móvel (livros, caixas da pilha) sai de
  `random.Random(semente)`. Rodar duas vezes dá o mesmo arquivo.

## 2. Como rodar

O Blender 5.3 está instalado em `C:\Program Files\Blender Foundation\Blender 5.3\`.

1. Gerar um lote (o Unity pode estar aberto; ele reimporta ao voltar ao foco):

   ```
   "C:\Program Files\Blender Foundation\Blender 5.3\blender.exe" -b --factory-startup -P Tools/Blender/lotes/basicos.py -- --fotos
   ```

   Grava um `.fbx` por móvel e o `materiais_<lote>.json` em `Assets/_Game/Art/Level1/Modelos`,
   as texturas do lote em `Art/Level1/Texturas`, e imprime triângulos e medidas de cada um. Sai
   com erro (código 3) se algum passar do orçamento ou não encostar no chão. Com `-- --fotos`,
   salva uma foto rápida de cada móvel em `Logs/Blender/<Nome>.png` (Workbench, de 3/4 pela
   frente), para conferir forma e proporção sem abrir o Unity.

2. Atualizar materiais e conferir em imagem: menu **BackRooms > Móveis do Level 1 >
   Atualizar e fotografar**, ou com o editor fechado:

   ```
   "C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe" -batchmode -projectPath "<pasta>" -executeMethod BackRooms.EditorTools.Level1Models.BatchUpdateAndCapture -logFile "<log>"
   ```

   Cria os materiais, reimporta os modelos, escreve no log as medidas de cada um e salva fotos
   em `Logs/Previews/moveis_*.png`: cada móvel da pasta sozinho (ou só os de
   `-moveis Nome1,Nome2`), uma vitrine de jantar, uma sala com pilha jogada de cadeiras e a
   sala dos sofás.

## 3. O fluxo do guia, e o que fazemos em cada etapa

| Etapa do guia | Aqui |
|---|---|
| Referência e conceito | Catálogo e medidas em `level1-mostruario.md` (3.2); imagens do MECCHA CHAMELEON |
| Bloqueio (block out) | Direto no script, em caixas e cilindros com as medidas reais |
| Alta resolução / escultura | Não usamos em móveis: forma reta, o chanfro basta. Para figuras orgânicas, ver seção 6 |
| Retopologia | Não precisa: a malha já nasce leve (feita para o jogo) |
| UV | Projeção pela face dominante, **1 m = 1 unidade de UV**: densidade de textura igual em todos os móveis |
| Baking (normal, AO) | Ainda não. Quando os móveis ganharem textura, o script assa no próprio Blender |
| Textura PBR | Por enquanto só cor, rugosidade e metal (paleta no script). Textura pintada é o próximo passo |
| Rig e pesos | Não para móveis. Peça que se mexe é objeto filho com a origem na dobradiça (portas do guarda-roupa) |
| Exportar FBX | Pelo script, com as opções da seção 4 |
| Importar no Unity | `Level1ModelImporter` acerta tudo sozinho (seção 5) |

## 4. Regras de modelagem e exportação

- **Escala real em metros**, unidade do Blender = 1 m. Nunca corrigir escala no Unity.
- **Origem no chão, no meio** do móvel. A **frente fica em -Y no Blender**, que vira **+Z no
  Unity** (o `forward` do objeto): girar o móvel no jogo é girar em Y.
- **Uma malha por móvel**, com vários materiais (menos chamadas de desenho). Exceção: o que se
  mexe vira objeto filho.
- **Chanfro nas quinas:** 1 segmento nas peças duras, 2 nas estofadas. Sem chanfro, a luz não
  marca a borda e o móvel parece de papelão.
- **Polígonos com mais de 4 lados são triangulados** antes de exportar (sem isso o FBX sai sem
  tangentes).
- **Orçamento: até 3.000 triângulos por móvel** (o guia sugere 200–2.000 para objetos de
  cenário em celular; em PC sobra, mas o Level 1 terá centenas de móveis). Hoje vão de 80
  (caixa) a 1.256 (sofá, estante). O script avisa quem passar.
- **Materiais com nome** (`Madeira_Escura`, `Tecido_Azul`...). A cor de verdade fica na paleta
  do script; o Unity cria um HDRP Lit com o mesmo nome.
- **Exportação FBX:** `apply_scale_options = FBX_SCALE_UNITS`, `axis_forward = -Z`,
  `axis_up = Y`, `bake_space_transform` ligado (o objeto chega sem rotação de -90°), sem
  animação, sem folhas de osso, texturas não embutidas.

## 5. Do lado do Unity

- **`Level1ModelImporter`** (em `Scripts/Editor/Level1Models.cs`) roda em todo `.fbx` de
  `Art/Level1/Modelos`: escala 1 com a escala do arquivo, sem conversão de eixo, sem animação,
  câmera, luz ou blend shapes, normais do arquivo, tangentes calculadas (MikkTSpace), sem
  compressão nem leitura pela CPU, sem colisor.
- **Materiais:** o guia avisa que material embutido no FBX não vira bem o shader do pipeline.
  `Level1Models.UpdateModels` cria ou atualiza os HDRP Lit em `Art/Level1/Materiais` a partir
  dos `materiais_*.json` (com a textura, se houver) e grava no `.meta` de cada modelo a troca "material do FBX com esse nome
  → material do projeto". Reimportar não perde a troca.
- **Colisores** não vêm do modelo. A camada de objetos do Level 1 (`PropLayer`, ainda a fazer)
  cria a caixa de colisão no mesmo passo em que põe o móvel, como manda a regra "colisor e
  malha nascem juntos".
- **Git:** `.fbx` vai direto no Git (`binary` no `.gitattributes`), como os PNG. O `.blend`
  continua marcado para LFS e não é usado.

## 6. Lotes e agentes: como fazer muitos móveis

Cada lote é um script em `Tools/Blender/lotes/` que só define os seus móveis e chama
`run_lote("<lote>", [funções], PALETA)`. Tudo o mais (peças, chanfro, UV, paleta comum,
exportação, conferência e foto) vem de `Tools/Blender/moveis_base.py`. Por isso lotes não se
atrapalham e dá para dividir o trabalho:

- **Um agente por lote**, com um modelo mais simples e barato (Sonnet). Cada um escreve só o
  arquivo do seu lote, roda o Blender com `--fotos`, abre as próprias fotos, corrige e entrega
  a lista com triângulos e medidas. Os lotes rodam ao mesmo tempo sem conflito: nomes de
  móvel, de material e de textura são únicos por lote, e `run_lote` recusa redefinir uma cor
  que já existe.
- **Quem coordena** (o agente principal) escreve o pedido de cada lote — nomes, medidas,
  referência descrita em palavras, orçamento —, revisa as fotos rápidas, e depois roda uma vez
  só o Unity (`Level1Models.BatchUpdateAndCapture`): dois editores não abrem o mesmo projeto.
- **O pedido de um lote** precisa ter: o nome do arquivo e do lote, cada móvel com nome, medidas
  e o que o faz reconhecível, as cores novas, o orçamento de triângulos, o comando para rodar e
  a ordem de abrir as fotos e iterar. Os pedidos dos lotes atuais estão resumidos na seção 7.
- **Limites:** o Workbench mostra a cor média de um material com textura, não a textura; listras
  e veios só aparecem na foto do Unity. A foto rápida descarta a face de trás, como o Unity:
  uma face do avesso aparece como buraco já no Blender.
- **Faces para fora:** cada peça acerta a direção das suas faces ao nascer (`_finish_piece`).
  Recalcular a malha inteira no fim, com as peças já encostadas, virava faces do avesso (o
  encosto das reclináveis saía preto no Unity). Não recalcular a malha toda num lote. Peça que se mexe (porta) continua sendo filho com origem
  na dobradiça.

## 7. Lotes existentes

| Lote | Móveis | Quem fez |
|---|---|---|
| `basicos` | Cadeira, MesaDeJantar, Sofa, Poltrona, Estante, GuardaRoupa, EstanteDeAco, EstanteDeAcoCheia, CaixaDePapelao, PilhaDeCaixas, PlacaOferta, PlacaLiquidacao | Agente principal |
| `estofados` | SofaMarromListrado, NamoradeiraMarromListrada, PoltronaMarromListrada (veludo marrom listrado, como os da sala de sofás do filme), PoltronaReclinavelBege, PoltronaReclinavelAzul | Agente (Sonnet) |
| `loja` | EstanteAbertaMadeira, TorreExpositora, BalcaoDeCaixa, MonitorCRT, MesaSimples, PlacaPenduradaOferta, PlacaPenduradaLiquida | Agente (Sonnet) |
| `detalhes` | CadeiraEncostoRipas, CadeiraDobravel, Comoda, CabideiroDePe, VasoAlto, MaquinaDeLavar, CilindroAzul | Agente (Sonnet) |
| `transito` | PlacaPare, PlacaStop (inglês), PlacaSetas, PlacaAnimal, cada uma também com poste (`...Poste`); PlacaCurvaDireita e PlacaCurvaEsquerda (losango amarelo com seta dobrada; também com poste) | Agente (Sonnet); STOP e placas de curva pelo agente principal |

Todos entram no jogo pela `PropLayer` (Level 1 de teste), achados pelo nome do FBX. Móvel novo só
aparece na fase se a `PropLayer` souber onde pô-lo (as listas de cada tema em `PropLayer.cs`).

## 8. Próximos passos

1. **Texturas dos móveis:** veio da madeira, trama do tecido e papelão pintados ou assados no
   Blender (com a UV de 1 m já pronta), exportados como PNG ao lado do FBX.
2. **LOD:** quando o `PropLayer` existir e a fase encher, gerar LOD1 (≈50%) e LOD2 (≈25%) no
   script para os móveis grandes e montar o `LOD Group` no Unity.
3. **O Capitão no Blender:** hoje é montado em código com cápsulas e malhas esculpidas
   (`CaptainBody`). No Blender ele viraria uma malha única com esqueleto e pesos, com
   deformação de verdade nas juntas. A animação pode continuar em código, girando os ossos.
   Pelo guia: escultura → retopologia → UV → rig → FBX com `Rig: Generic`.
4. **Mais móveis do catálogo:** cômoda, cabideiro, vaso, balcão, máquina de lavar, placas de
   trânsito, carro, espreguiçadeira, divisória de cubículo.
