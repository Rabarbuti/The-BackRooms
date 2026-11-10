# Referência: Your Happy Place

Análise do jogo que está em `C:\Users\rabar\OurGAMES\YourHappyPlace`, para servir de referência de fases e de atmosfera para **The BackRooms**. Feita em 10/11/2026.

## Como esta análise foi feita (e seus limites)

- A pasta contém o jogo **compilado** (Unreal Engine 5.1, projeto interno chamado `VHSLiminal`), sem código-fonte.
- **Não executei o jogo.** Li três coisas: o arquivo de créditos, os registros de partidas antigas (quais mapas foram carregados) e o **índice de assets** do pacote — cerca de 1.700 nomes de arquivos (mapas, materiais, sons, modelos).
- Portanto tudo abaixo é **inferido de nomes**. Um nome como `Lvl9ChaseSequence` diz que existe uma perseguição na fase 9, mas não como ela funciona. Onde escrevo "provavelmente", é dedução minha.
- É um jogo de outra pessoa. Serve como referência de ideias e de estrutura; nenhum arquivo dele deve ser copiado para o nosso projeto.

Pela internet, o jogo aparece como um terror psicológico independente de espaços liminares, feito para uma game jam (página do desenvolvedor Dark_Knife15 no itch.io). Não confirmei mais detalhes lá.

---

## 1. Estrutura

- **13 fases** numeradas (`0`, `0_2`, `2` a `12`), mais `Entry` e `MainMenu`.
- A passagem de uma fase para outra é por **elevador**: há assets para chamar o elevador, botão, seta indicadora (ligada, com erro), viagem, batidas na porta (`ElevatorBanging`) e uma caixa com alavanca que o ativa (`ElevatorActivatorBox`, `ElActLever`). Há também gatilhos que carregam a fase seguinte ao atravessar um ponto (`LoadLevelTrigger`, `LoadLevelTimer`).
- Um jogador só, em primeira pessoa, sem armas (o personagem é o modelo padrão de primeira pessoa do Unreal).
- **Respawn por limites** (`BoundsRespawn`): quem sai do mapa volta, em vez de cair para sempre.
- Tem easter egg (`EasterEgg`, `EE_Chicken`).

## 2. Fases (tema inferido pelos materiais e objetos de cada mapa)

| Mapa | Tema provável | Pistas nos nomes |
|---|---|---|
| 0 | Bairro ao ar livre à beira de uma pedreira | casas (`TownHouse`, `M0House`), cerca, rio, estrada, penhasco de pedreira, escadas de concreto, escada de mão, porta de galpão, cano no terreno |
| 0_2 | Segunda parte da fase 0 | carregada logo depois da 0 nos registros |
| 3 | Área externa com postes | `M3LightPole`, grama, tetos próprios |
| 4 | Terreno aberto com sol falso | `Map4Terrain`, `FakeSunMap4`, nuvens, `Map4FadeBlack` |
| 5 | Prédio de escritório com estacionamento | tijolo, carpete, parede de escritório, mármore, mesa, placa de saída, `Map5ParkingLot` |
| 6 | Salão de pilares sob o céu | pilares, mármore, carpete vermelho, luzes de teto, colinas, céu |
| 7 | Piscinas | azulejos, água, ralo, luz subaquática, escorregadores (`Slide1` a `3`), válvula de drenagem |
| 8 | Salão / escritório com uma figura parada | lustre, carpete azul e vermelho, alambrado, `Map8StandingMan`, `StandingManCutscene`, `Map8Eye` |
| 9 | Loja / shopping com perseguição | carpete rosa e amarelo, papel de parede, grade de loja, escadas, `Map9WindowDistortion`, **`Lvl9ChaseSequence`** |
| 10 | Hotel | reboco de hotel, lobby, pilares, pátio circular, cortinas, plataforma, janelas, `Map10PowerTrigger` |
| 11 | Corredor final | tijolo, azulejo, `Map11End` |
| 12 | Não identifiquei | — |

Fora da numeração aparecem ambientes reutilizados: **shopping** (escada rolante, catracas, placa `MallSign`, música `MallMusic1`), **mercado** (prateleiras, geladeira com zumbido, carrinho de compras) e **Backrooms clássicas** (`BackroomsWallpaper1/2`, carpete, lâmpada fluorescente, `LightBuzz`, `LightFlicker`).

O que isso ensina: cada fase é **um lugar comum e reconhecível** (bairro, escritório, piscina, hotel, shopping), e não uma variação das salas amarelas. A mistura de interior e exterior — inclusive céu e sol falsos — é o que dá a sensação de sonho.

## 3. Ameaças e momentos roteirizados

- **Entidade** com corpo próprio, animação, passos e corrida (`Entity`, `EntityFootsteps`, `EntitySprint`), mais morte (`Death`).
- **Sinais no jogador**: batimento cardíaco e respiração em loop (`HeartbeatLoop`, `BreathingLoop`) — provavelmente sobem quando a entidade está perto.
- **Figuras humanas** que não são a entidade: um "comprador" que anda com carrinho (`Shopper`, `AdvanceShopper`, `ShopperCutscene`) e um homem parado (`StandingMan2`). Provavelmente aparecem em cenas curtas, para inquietar sem atacar.
- **Momentos pontuais**: batidas (`KnockingEffect2`), porta batendo (`DoorSlam`), texto com falha (`GlitchTextTrigger`), anúncio no alto-falante (`PaAnnouncement`), janela estranha, espelho (`BehindTheMirror`), balões.
- Poucas ameaças para 13 fases: a maior parte do jogo é ambiente e som.

## 4. Som (a parte mais rica do pacote)

- **Camadas de fundo**: zumbido de fita (`VHS_Hum`), tom grave contínuo (`20HzLoop`), zumbido de lâmpada, zumbido de geladeira, ambiências de terror e de fábrica, coro etéreo (`ChoirEthereal`), vozes distorcidas ao longe (`DistortedTalking`, `TalkingAmbience`).
- **Vento no microfone** (`MicWind1/2`): o som de quem filma ao ar livre. Reforça a ideia de câmera na mão.
- **Música ambiente fora de lugar**: música de shopping, rádio de loja, Mozart, uma batida relaxante — tocando em lugares vazios.
- **Reverberação por ambiente**: zonas próprias para banheiro, casa, shopping, piscina (dentro e fora) e depósito.
- **Passos por superfície**: carpete, grama, água, madeira.
- **Áudio degradado**: efeitos de redução de bits e de taxa de amostragem (`BitDepth`, `SampleRate`), ou seja, o som também passa pela "fita".

## 5. Câmera e interface

- Um efeito mestre de VHS em tela cheia (`MasterVHSEffect`), com distorção de lente, sujeira na lente (`lensDirt`), grão de filme, rastro de luz (`LightSmear`) e nitidez artificial.
- **Indicador de gravação** e animação de início de gravação (`RecordingIndicator`, `RecordingStart`).
- Bordas escurecidas na interface (`UI_EdgeFade`).
- Fonte pequena de pixels (`MiniFont`), contagem regressiva e hora do dia (`Countdown`, `TimeOfDay`).
- Tremor de câmera ao andar e ao correr (`WalkCamShake`, `RunCamShake`).
- Menu com botões no estilo dos botões de uma filmadora, com som de clique (`CamButton1` a `5`, `Cam_Click`, `Cam_Hover`).

---

## 6. O que levar para The BackRooms

### Já feito a partir desta referência
- **Interface de filmadora**: painel com REC piscando, contador da fita, bateria, hora e data, mais efeito VHS em tela cheia com bordas escuras. Ver `Camcorder.cs`, `CamcorderPass.cs` e `Shaders/CamcorderVHS.shader`.
- **Respawn por limites**: já existe no `PlayerController` (rede de segurança).

### Próximos passos sugeridos
1. **Elevador como passagem de fase.** Hoje a saída é uma porta com placa EXIT. Um elevador que precisa ser chamado e ativado com alavanca encaixa no objetivo co-op do plano (ligar disjuntores) e vira a tela de carregamento natural entre fases.
2. **Sinais da entidade no jogador.** Batimento e respiração que sobem com a proximidade, e interferência na imagem — o `Camcorder` já tem o método `Glitch` para isso.
3. **Fases como lugares comuns.** Candidatas, na ordem em que o gerador atual mais ajuda: garagem (já no plano), escritório com baias, piscinas, hotel, shopping/mercado. Ambientes externos pedem outro tipo de gerador.
4. **Som por ambiente.** Passos por superfície, zonas de reverberação e música ambiente fora de lugar. O vento no microfone combina com a filmadora.
5. **Figuras que não atacam.** Uma silhueta parada no fim de um corredor que some quando alguém chega perto custa pouco e rende muito num co-op ("você viu aquilo?").
6. **Menu com cara de filmadora.** O menu atual é provisório; os botões e o painel podem seguir o mesmo visual do OSD.

### O que não copiar
- A progressão linear de 13 fases feitas à mão: não cabe no nosso escopo. Nosso caminho é poucas fases geradas por seed, cada uma com uma regra própria.
- Um jogador só: o nosso é co-op, então todo susto precisa funcionar com mais de uma pessoa olhando.

## Fontes

- Pasta local do jogo: `Credits.txt`, registros em `VHSLiminal/Saved/Logs` e o índice de `VHSLiminal/Content/Paks`.
- [Listagem no itch.io](https://itch.io/post/16948437)
- [Listagem no Lutris](https://lutris.net/games/your-happy-place/)
