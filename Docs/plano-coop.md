# The BackRooms — estado atual e plano

Como ler os documentos do projeto:

- **Este arquivo** — o que o jogo tem hoje, o que falta e para onde vai. Leia antes de começar.
- **`CLAUDE.md`** (na raiz) — ambiente, onde fica cada coisa no código, regras que não podem ser
  quebradas e como testar.
- **`historico.md`** — o registro cronológico de cada rodada, com causas de defeitos e números de
  teste. Só quando precisar entender uma decisão antiga.
- **`level1-mostruario.md`** — projeto da segunda fase (a loja de móveis copiada, cheia de
  objetos) e do inimigo dela, o Capitão. Existe uma versão de teste jogável e o corpo do Capitão.
- **`modelos-blender.md`** — como os modelos são feitos no Blender por script e entram no Unity,
  e como dividir lotes de modelos entre agentes.
- `contexto-backrooms.md`, `prompt-fases-visual-fisica.md`, `referencia-your-happy-place.md` —
  referências de design do gênero.

Atualizado em 11/11/2026.

---

## Estado atual

Existe uma fase, o Level 0, jogável por 1 a 4 pessoas por IP, e uma versão de teste do Level 1
(seção "Level 1 de teste" abaixo). Não há ainda vitória nem derrota: a saída só registra que o
jogador chegou. Editor: Unity 6000.6.4f1 com HDRP 17.6 (até 11/11/2026, 6000.3.26f1 / HDRP 17.3).

### Fase

- **Mapa:** 20 x 20 células de 4 m (80 x 80 m), pé-direito de 4,05 m, gerado a partir de uma
  seed. `LevelGenerator` produz a planta (`LevelData`); `LevelBuilder` monta malhas e colisores.
  O tamanho é `LevelBuilder.DefaultSize`.
- **Sala inicial:** fechada, 2 x 2 células, um vão de porta, materiais limpos, perto de um canto
  sorteado (a 3–6 células das duas bordas mais próximas). A entidade não entra nela.
- **Saída:** porta corta-fogo com placa EXIT na célula mais distante do início, do outro lado do
  mapa. O modelo da porta foi gerado por IA (GLB em `Art/Fontes`, lido por `GlbReader` e encaixado
  por `ImportedModels`); sem o GLB volta a porta antiga.
- **Desgaste:** sujeira, sangue, palavras e lâmpadas defeituosas aumentam com a distância da
  sala inicial (`LevelGenerator.WearAt`).
- **Esconderijos:** 12% das paredes internas viram paredes grossas com um nicho de entrada
  baixa (só agachado). Quem está dentro não é visto nem pego.
- **Setas:** no máximo 6, espalhadas, apontando o caminho mais curto até a saída.
- **Luminárias:** calha de chapa com difusor de acrílico e duas lâmpadas tubulares; luz pontual
  com sombra em cache atrás do difusor. No máximo 165 acesas, por causa do limite de 1024 sombras
  configurado no HDRP.
- **Grelhas de ar:** uma a cada 12 células, no alto das paredes, fora da sala inicial
  (`LevelGenerator.PlaceVents`, malha em código).

### Buracos

- Um a cada 100 células (4 no mapa atual), retângulos de 2 x 2 a 3 x 4 células, 7 m de
  profundidade, abertos em cima do caminho mais curto, onde mais alongam o desvio (até 14
  células por buraco).
- **Faixas:** vigas de 0,4 m de largura atravessam cada buraco. O jogador tem 0,6 m.
- **Sempre há outro caminho:** um buraco nunca isola parte do mapa. `LevelData.PathCells` é o
  caminho direto (pelas faixas) e `SafePathCells` o seguro. Na seed 1998: 140 m contra 252 m.
- **Fumaça** cobre o fundo (`PitSmoke`). Quem desce 2,5 m vê NO SIGNAL e reaparece na sala
  inicial em 0,8 s. Não há morte.

### Apagão

`LightsOut`: o primeiro com 60 s de partida, 10 s de escuro, depois 30 s de luz entre um e
outro. As luminárias avisam piscando nos 10 s anteriores, e sem parar nos últimos 2 s. No escuro
some também a luz ambiente; sobram as lanternas e a placa da saída. Todas as máquinas apagam
juntas pelo relógio `GameSession.SessionSeconds`.

### Jogador

- **Movimento:** anda a 2,8 m/s, corre a 5,0 com 6 s de fôlego, agacha a 1,4, pula 0,8 m.
- **Lanterna** (F), por enquanto com bateria infinita. Passos sintetizados em código.
- **Traje** (`PlayerBody`): o que os outros veem é um explorador de traje de proteção amarelo,
  que anda, agacha, acompanha o olhar e mostra a caneta e a lanterna nas mãos.
- **Mão em primeira pessoa** (`ItemHand`) segurando o item em uso.

### Caneta

Único item. Tecla 1 pega e guarda, botão esquerdo risca paredes, chão e teto a até 2,2 m, R
troca a cor (preto, vermelho, azul). Todos começam com ela. Os riscos (`DrawingBoard`) aparecem
para todos e são enviados a quem entra depois. Limites: 300 pontos por traço, 1500 traços.

### Entidade

- **Aparência:** 3,3 m, corpo de cipós pretos torcidos, cabeça comprida esculpida como malha,
  sem olhos, de rosto fechado e boca só na frente (`EntityBody`, `EntityHeadMesh`).
- **Quando aparece:** não existe no começo. Surge quando alguém chega a 8 células de caminhada
  da sala inicial, com pelo menos 15 s de partida, numa célula entre esse jogador e a sala, a 4
  células ou mais de todos e fora da vista de todos (`EntityDirector`).
- **Ouvido:** o principal. O host estima o barulho pelo deslocamento de cada jogador: parado 0,
  agachado 25%, andando 55%, correndo 100%. O som viaja pelo labirinto, barrado por parede,
  até 8 células para quem corre.
- **Visão:** 26 m à frente num cone de 130°, ou 3,5 m em qualquer direção, com linha de visada.
- **Velocidade:** vagando 1,9 m/s, vasculhando 2,6, indo atrás de um barulho 3,4, perseguindo 4,6.
- **O que salva:** ficar parado ou agachado, entrar num esconderijo, atravessar um buraco pela
  faixa (ela não anda em faixa) ou voltar para a sala inicial.
- **Captura:** a menos de 1,25 m ela ruge, o jogador vê NO SIGNAL e reaparece na sala inicial
  em 1,6 s. **Provisório** — o plano (seção 2.2) é sala de contenção com resgate.
- **Avisos:** ronco grave em 3D e chuvisco na filmadora a menos de 16 m.
- **Como anda:** sem NavMesh. `LevelNav` calcula o caminho na grade do `LevelData`.
- No apagão ela não muda de comportamento e, sem olhos, quase some.

### Filmadora

- **Painel e efeito:** REC, contador, bateria, hora e data desenhados por `Camcorder`; o efeito
  de fita é um Custom Pass (`CamcorderPass` + `CamcorderVHS.shader`).
- **Gravação de verdade:** F9 grava o que o jogador vê, com som, num AVI de 854 x 480 a 20
  quadros por segundo, na pasta Vídeos do usuário, em "The BackRooms". Para sozinha aos 15 min.

### Fases e progresso

- **Seletor no menu:** lista as fases do `LevelCatalog` (hoje Level 0 e Level 1 de teste); a
  atual fica em destaque. Escolher outra fecha a sessão e carrega a cena dela
  (`GameSession.SwitchLevel`). Cada fase é uma cena com a própria sessão.
- **Progresso** (`LevelProgress`, nas preferências desta máquina): uma fase está zerada quando o
  jogador desta máquina chega à saída dela. Ficam liberadas as zeradas e a próxima. O aviso
  "LEVEL N ZERADO" aparece no alto da tela.
- **Modo desenvolvedor:** no editor, em build de desenvolvimento ou com `-brDev`, todas liberadas.

### Rede

- Host autoritativo por IP direto, porta 7777. Jogar sozinho é hospedar sem ninguém entrar.
  No menu, H hospeda e J entra; por linha de comando, `-brHost` e `-brJoin <endereço>`.
- O host envia a seed, o instante de início da sessão e a fase; cada máquina gera o mesmo mapa.
  Quem entra estando em outra fase troca sozinho para a do host e reconecta no mesmo endereço.
- Para jogar pela internet hoje é preciso abrir a porta 7777 no roteador de quem hospeda ou
  usar uma VPN de jogo.

---

## O que ainda não existe

- **Objetivo da fase:** disjuntores, código, elevador, vitória e derrota (seção 2.3).
- **Sala de contenção e resgate** de quem é capturado (seção 2.2).
- **Entrar por código de sala** (Relay da Unity; falta ativar no painel da Unity Cloud).
- **Voz por proximidade** e a entidade reagindo à voz (seção 2.1).
- **Sanidade** e alucinações (seção 2.5).
- **Itens:** pegar a caneta no cenário, tinta que acaba, borracha, pilhas, walkie-talkie, Almond
  Water. Mão em primeira pessoa só existe para a caneta.
- **Entidade:** aproveitar o apagão, reagir à lanterna, mais de uma, vigiar esconderijos.
- **Buracos:** som de queda, faixa que quebra ou balança.
- **Opções:** gráficos, resolução e pasta da gravação.
- **Mais níveis.** O segundo está projetado em `level1-mostruario.md`. Existe uma versão de
  teste jogável (`Scenes/Level1_Test.unity`: mapa pequeno mobiliado, sem entidade) e o corpo do
  inimigo (`CaptainBody`); já dá para escolher a fase no menu, mas sair por uma porta ainda não
  leva à seguinte, e o objetivo de cada fase (seção 2.3) ainda não existe: hoje chegar à saída
  basta para zerar.

---

## 1. Conceito

**1 a 4 jogadores caem juntos no Level 0 e precisam achar a saída antes que o lugar os separe.**

O jogo inteiro se apoia em uma tensão de quatro forças:

| Força | Empurra o grupo para | Como aparece no jogo |
|---|---|---|
| Sanidade | **ficar junto** | Sozinho, a sanidade cai rápido; perto de um colega, ela se recupera |
| Objetivos | **se separar** | A saída exige ações em pontos distantes ao mesmo tempo |
| Coordenação | **falar** | Códigos, direções e avisos só passam por voz |
| Entidade | **ficar calado** | Ela ouve passos, objetos e a voz real dos jogadores |

Toda mecânica nova deve reforçar uma dessas forças. Se não reforça, não entra.

---

## 2. O que muda por ser co-op

### 2.1 Voz por proximidade
- Voz 3D: quem está longe não é ouvido. Paredes abafam.
- O volume da voz de cada jogador gera **ruído** que a entidade investiga.
- **Walkie-talkie** (item, bateria limitada): fala com o grupo todo à distância, mas o aparelho de quem recebe também faz barulho.

### 2.2 Captura em vez de morte
Quando a entidade pega um jogador, ele não morre — some e reaparece trancado em uma **sala de contenção** em outro ponto do labirinto.
- O capturado continua jogando: pode gritar por socorro (voz por proximidade) e ouvir quem se aproxima.
- A porta só abre **por fora**, por um colega.
- Todos capturados = derrota; o nível recomeça com outro layout.

Isso evita o maior problema do co-op de terror: o jogador morto que fica assistindo.

### 2.3 Objetivos que exigem mais de uma pessoa
A saída do Level 0 é um elevador sem energia. Para ligá-lo:
1. Achar os **disjuntores** espalhados (quantidade = jogadores + 1, mínimo 2). Cada um emite um zumbido elétrico audível de longe — é a pista, não é caça às cegas.
2. Dois disjuntores precisam ser armados **dentro de uma janela de tempo curta**, por pessoas diferentes em lugares diferentes.
3. Um painel mostra um código que só aparece em **outro** ponto do mapa: um lê, outro digita.

Com 1 jogador, as regras de simultaneidade são desligadas (o jogo continua jogável sozinho, mas é pensado para grupo).

### 2.4 Recursos compartilhados
- Lanterna com bateria; pilhas são poucas e ficam no chão para qualquer um pegar.
- Almond Water restaura sanidade; pode ser entregue a um colega.
- Spray de tinta para marcar paredes — as marcas são vistas por todos.

### 2.5 Sanidade em grupo
- Cai com: estar sozinho, escuro, proximidade da entidade.
- Sobe com: estar perto de um colega, área iluminada, Almond Water.
- Baixa sanidade gera alucinações **só para aquele jogador** (passos falsos, um colega que não está lá, voz distorcida). Os outros não veem — o que gera desconfiança e conversa.

---

## 3. Arquitetura de rede

- **Modelo:** um jogador hospeda e os outros entram (listen server), sem servidor dedicado.
  Como é PvE entre amigos, trapaça não é preocupação: a prioridade é a resposta do controle.
- **O mapa não trafega:** só a seed. Cada máquina gera o mesmo labirinto, e por isso a geração
  usa apenas `System.Random(seed)`. A gerência de cenas do Netcode está desligada.
- **Eventos com hora marcada** (o apagão) não trafegam: cada máquina faz a conta sobre o relógio
  do servidor.

| Coisa | Quem manda | Como chega aos outros |
|---|---|---|
| Planta do labirinto | Host sorteia a seed | Um inteiro; cada um gera o mapa |
| Posição do jogador | O próprio jogador | `NetworkTransform` com autoridade do dono |
| Olhar, agachar, lanterna, esconderijo, item na mão | O próprio jogador | `NetworkVariable` |
| Riscos de caneta | Quem risca, validado pelo host | RPC; quem entra depois recebe todos |
| Entidade (posição, estado) | Host | `NetworkTransform` e `NetworkVariable` |
| Barulho dos jogadores | Host estima pelo deslocamento | Não trafega |
| Captura | Host decide | RPC para o dono do jogador |
| Apagão | Ninguém | Conta local sobre o relógio do servidor |

Pacotes: Netcode for GameObjects 2.13.3, Multiplayer Services 2.3.3 (para o Relay, ainda sem
uso), Multiplayer Play Mode 2.0.2 e AI Navigation 2.0.15 (sem uso: a entidade anda pela grade).
Vivox entra só com a voz.

---

## 4. Renderização (HDRP)

- **Decidido em 10/11/2026: o jogo fica em HDRP.** Para PCs modestos a saída é o perfil
  "Performant", não trocar de pipeline. Os três perfis do template foram mantidos.
- **Luz:** mapa procedural não permite lightmap. As luminárias são luzes pontuais com sombra em
  cache; piscam pelo dimmer, sem refazer a sombra. A luz ambiente vem de um céu em degradê.
- **Névoa volumétrica** existe nos perfis High Fidelity e Balanced; o Performant não tem. O que
  depende dela (a fumaça dos buracos) tem versão que funciona sem.
- **Figuras** (entidade, traje, mão, Capitão) são montadas em código. Modelos importados: a porta
  da saída (GLB gerado por IA, `Art/Fontes`) e os móveis do Level 1 (FBX feitos por script no
  Blender, `Tools/Blender`).
- **Texturas do Level 0** (papel de parede, carpete, forro, concreto): geradas por IA e já
  emendáveis, em `Art/Fontes/Texturas`; sem o arquivo, o `Level0Setup` volta a pintá-las em código.

---

## 5. Etapas

| # | Etapa | Situação |
|---|---|---|
| 0 | Fundação: commit inicial, pastas, pacotes | Feita |
| 1 | Sala de teste: jogador em primeira pessoa, Level 0, luz, zumbido | Feita |
| 2 | Rede: 2+ jogadores se veem, lanterna sincronizada | Feita por IP; falta código de sala |
| 3 | Labirinto por seed, igual em todas as máquinas | Feita (sem NavMesh) |
| 4 | Entidade: IA no host, ruído, esconderijos, captura e resgate | Feita, menos contenção e resgate |
| 5 | Voz: Vivox posicional, voz gera ruído, walkie-talkie | Não começada |
| 6 | Loop completo: disjuntores, código, elevador, sanidade, vitória e derrota | Não começada |
| 7 | Acabamento: VHS, menu, sala de espera, opções gráficas, build | VHS e gravação feitos |
| 8+ | Mais níveis, cada um com regra própria | Não começada |

Fora do plano original, já existem: caneta, buracos com faixas, apagão, traje dos jogadores e
gravação em vídeo.

---

## 6. Decisões em aberto

1. **Captura** — hoje o capturado volta para a sala inicial. Manter, ou fazer a sala de
   contenção com resgate do plano?
2. **Velocidade da entidade** — ela é mais lenta que a corrida do jogador (4,6 contra 5,0), então
   dá para fugir enquanto há fôlego. Mais rápida obrigaria a se esconder sempre.
3. **Sala inicial como abrigo** — a entidade ignora quem está nela, o que permite ficar parado lá.
4. **Entidade no apagão** — sem olhos, quase some no escuro. Devolver algum brilho?
5. **Distribuição** — só entre amigos (o Relay da Unity basta) ou publicar na Steam depois.
6. **Arte** — decidido em 11/11/2026: modelos novos no **Blender, por script**
   (`Docs/modelos-blender.md`), exportados em FBX. Os móveis do Level 1 já vêm por esse
   caminho; figuras (entidade, Capitão, traje) seguem em código até alguém passar para lá.
