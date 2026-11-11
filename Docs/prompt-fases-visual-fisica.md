# Fases: design gráfico e física — referência e prompt

Análise de três jogos da Steam para orientar a construção das fases de **The BackRooms** (Unity 6.3, HDRP, co-op). Feita em 10/11/2026.

## Como esta análise foi feita (e seus limites)

- **Visual:** vi as screenshots das três páginas da Steam (18 de Escape Together, 14 de Backrooms 2026, 7 de The Backrooms). Algumas não carregaram. Não assisti aos trailers.
- **Mecânicas:** texto das páginas, um guia da comunidade e a wiki de fãs de Escape Together, e as análises de usuários de The Backrooms.
- **Não joguei nenhum dos três.** Nenhuma página documenta física. Por isso este documento separa:
  - **Observado** — o que as imagens e os textos mostram.
  - **Proposta** — valores e técnicas que eu recomendo para reproduzir aquilo no Unity. São ponto de partida para ajuste, não medições dos jogos.
- **Backrooms (2026)** ainda não foi lançado: só existem screenshots e uma descrição curta.
- As cores em hexadecimal são aproximações tiradas das screenshots.

---

## 1. Os três jogos

### 1.1 Backrooms: Escape Together (Triiodide Studios)
Co-op 1–6, acesso antecipado desde 2022, 93% positivo. 11–12 níveis procedurais, ambientes "fotorrealistas feitos à mão", áudio espacializado, voz por proximidade.

**Observado — estrutura:**
- Cada nível tem **uma mecânica própria**, não só um cenário:

| Nível | Cenário | Entidade | Mecânica |
|---|---|---|---|
| 0 The Lobby | Salas amarelas | Bacteria, Smiler | Documentar com filmadora |
| 1 Habitable Zone | Garagem de concreto | Skin-Stealer | Religar geradores |
| 2 Pipe Dreams | Túneis de tubulação | Scratcher (caça por som) | Válvulas e relógio |
| 3 Electrical Station | Corredores de manutenção | Clump | Consertar (fusível, fio, fita, gasolina) |
| 4 Abandoned Office | Escritório com baias | Nenhuma | Código em 4 monitores |
| 6 Lights Out | Escuridão total | Collector | Navegar por LIDAR |
| 37 Sublimity | Poolrooms | Nenhuma | Exploração e válvulas |
| ! Run For Your Life | Corredor de hospital | Scratcher | Perseguição pura |
| Fun | Festa infantil | Partygoer | Minijogos com moedas e tickets |
| 232 | Supermercado infinito | Facelings | Economia de compras |
| The Hub | Base segura | Nenhuma | Seleção de rota |

- **Eventos globais de fase:** *Blackout* (todas as luzes apagam; lanterna vira essencial) e *Alarme* (a fase inteira fica vermelha, com som pulsante). Entidades mudam de comportamento durante o blackout.
- **Objetivos sorteados** no Level 0: dois por partida, de uma lista (achar fitas VHS, filmar a entidade, filmar durante blackout, filmar os buracos). Depois, voltar ao **elevador**, que leva à segunda parte.
- **Regras de entidade ligadas a luz e linha de visão:** Smiler recua com lanterna no rosto; Bacteria é despistada quebrando linha de visão e distância; Skin-Stealer é repelido por luz e os armários de suprimentos (onde a luz não apaga) são abrigo.
- **Buracos no chão** com vigas finas por onde se atravessa; no nível escuro, buracos mortais marcados em vermelho.
- **Navegação diegética:** linha listrada no chão, placas de saída, setas, desenhos de bonecos-palito na parede que ensinam regras.
- **Interação:** segurar tecla para ativar gerador, girar válvulas, trancar porta com painel ou fusível, esconder-se em armários, consumíveis em salas de suprimento, itens que podem ser arremessados.
- **Bloqueios invisíveis:** numa fase de perseguição, não dá para pular por cima das mesas.

**Observado — visual:**
- Jogadores usam **traje hazmat amarelo** com capuz e visor escuro. Esconde o rosto, é legível de longe e combina com a lore.
- Bacteria: figura preta, muito alta e fina, feita de fios emaranhados, quase encostando no teto.
- Interface mínima; menu de personagem/inventário com tinta amarela.

### 1.2 Backrooms (2026) (Phantom Games)
Co-op 1–6, não lançado. "Ultra-realista", níveis clássicos e originais, **rotas escondidas**, interface mínima. A descrição destaca espaços onde "direção, escala e familiaridade não são confiáveis".

**Observado — visual (é o mais forte dos três em composição):**
- **Muito mais escuro.** Level 0 com poucas luminárias; a maior parte da sala fica em penumbra e os pilares projetam sombras longas e duras a partir de uma única fonte.
- **Perspectiva de um ponto e simetria:** corredores retos com luminárias repetindo até o ponto de fuga; arcos concêntricos nas Poolrooms diminuindo ao infinito.
- **Recursão como sinal de anomalia:** uma porta brilhando em vermelho que mostra dentro dela a mesma porta repetida, com "THIS WAY" pichado ao lado.
- **Escala exagerada:** salões enormes de azulejo com escadarias largas descendo para a água.
- **Luz estourada:** "janelas" retangulares totalmente brancas, sem nada visível do outro lado.
- Variações de Poolrooms: creme com água verde; quase preta com lustres em forma de losango refletidos na água; azul escura com claraboias ovais.
- Uma imagem mostra só o **círculo da lanterna** revelando pilares de azulejo no escuro total.

### 1.3 The Backrooms (Ionized Games)
Single-player, acesso antecipado desde 2022, 61% positivo (misto). Filmadora na mão, mapa aleatório, "uma dúzia de salas únicas", criaturas.

**Observado — visual:**
- Level 0 mais **claro e esverdeado**: carpete verde-oliva, papel de parede com padrão visível, rodapé amarelo, luminárias grandes e brilhantes.
- **Faixa vermelha pintada** na parede, perto do chão — linha-guia.
- **Acampamento de pesquisa:** tenda branca em forma de domo num salão vazio, com tripés, refletores e equipamento (estilo dos vídeos de Kane Pixels).
- **Escritório dos anos 90:** baias, monitores CRT bege, carpete vinho, plantas em vaso, pilares, luminárias embutidas.
- Hangar azulado com trajes amarelos pendurados; tanque industrial enferrujado com névoa; piscina de água verde dentro de uma sala amarela.

**Observado — o que os jogadores reclamam (não repetir):**
- Monstros aparecem **demais** — "não passo um minuto sem encontrar um" — e a tensão vira correria.
- Monstro que não mata em um golpe perde o medo.
- **Cair através do chão** e sair do mapa.
- Sem save: morrer ou fechar o jogo recomeça tudo.
- Elogios: efeito de filmadora, ambiência "exaustiva como deve ser", mapa aleatório.

---

## 2. Design gráfico das fases

### 2.1 Level 0 — as salas amarelas
Três interpretações do mesmo lugar:

| | Escape Together | Backrooms (2026) | The Backrooms |
|---|---|---|---|
| Clima | Amarelo uniforme, bem iluminado | Mostarda escuro, penumbra | Verde-amarelado, claro |
| Luminárias | Frequentes | Raras | Frequentes e grandes |
| Sombras | Suaves | Longas e duras | Suaves |
| Efeito | Monotonia | Ameaça | Estranheza de escritório |

**Observado — elementos comuns:**
- **Parede:** papel de parede amarelo-pálido com padrão pequeno e repetido (geométrico ou arabesco), levemente sujo, com manchas de umidade. Algumas paredes lisas, cor creme, para variar.
- **Piso:** carpete de pelo curto, bege-amarelado a oliva, com manchas escuras.
- **Teto:** forro modular de placas acústicas quadradas com perfis metálicos visíveis, amarelado.
- **Luminárias:** painéis retangulares embutidos no forro, brancos e estourados (com bloom). O amarelo da cena vem do **rebatimento** da luz branca nas superfícies amarelas, não de luz amarela.
- **Rodapé** fino, mesma família de cor da parede ou amarelo mais forte.
- **Arquitetura:** paredes soltas que não fecham salas, pilares quadrados grossos, vãos sem porta, corredores que desembocam em salões. Sem janelas, quase sem móveis.
- **Pontos de referência raros:** porta escura com placa EXIT vermelha, faixa vermelha na parede, pichação, buraco no chão, tenda de pesquisa.

**Proposta — paleta inicial:**
- Papel de parede: `#C8B560` (base), padrão em `#B9A650`
- Carpete: `#A6955A` (variante oliva `#8F9A5A`)
- Forro: `#CFC79A`
- Rodapé: `#B8A040`
- Luz do painel: branco levemente frio, 4500–5000 K
- Placa EXIT / faixa-guia: `#C8201E`

**Proposta — medidas (grade modular):**
- Célula da grade: 4 m × 4 m. Pé-direito: 2,7 m.
- Parede: 0,2 m de espessura. Rodapé: 0,1 m.
- Placa de forro: 0,6 × 0,6 m. Luminária: 0,6 × 1,2 m.
- Vão de passagem: 1,0–2,0 m de largura × 2,1 m de altura.
- Pilar: 0,6 a 1,0 m de lado.
- Uma luminária a cada 1–3 células, com distribuição irregular (algumas células ficam escuras).

### 2.2 Outras fases (resumo visual)
- **Garagem / Habitable Zone:** concreto cinza, pilares, piso molhado com poças refletindo, uma luz dura por vão, gerador amarelo e cones. Contraste alto.
- **Tubulações / Pipe Dreams:** túnel estreito e curvo, canos grossos correndo pelas paredes, luz pontual quente, muito escuro, vapor.
- **Escritório:** baias repetidas, CRTs, carpete vinho ou cinza, luminárias embutidas; versão escura com janelas foscas emitindo luz azulada.
- **Poolrooms:** azulejo pequeno branco ou creme em tudo (piso, parede, teto, pilar), água turquesa ou verde, rasa, muito reflexiva; arcos, pilares cilíndricos, escadas entrando na água; luz branca estourada vinda de aberturas.
- **Level Fun:** cores saturadas — piso de EVA colorido, xadrez vermelho/azul/branco, castelos infláveis, mesa de festa amarela, balões, cerquinha branca — sob teto escuro e iluminação de festa. O contraste entre alegria e escuridão é o efeito.
- **Lights Out:** preto total; o mundo só aparece onde a lanterna ou o LIDAR alcança.
- **Supermercado:** prateleiras bege vazias em fileiras, piso vinílico, luminárias tubulares.
- **Alarme (evento):** a mesma fase tingida de vermelho.

### 2.3 Princípios que valem para todas
1. **Um material domina cada fase** (papel amarelo, azulejo, concreto). A repetição do material é a identidade.
2. **A luz é o desenho:** poças de luz separadas por penumbra guiam e ameaçam. Áreas escuras precisam existir.
3. **Fonte de luz estourada, ambiente subexposto.**
4. **Repetição até o ponto de fuga** — alinhar luminárias, pilares e arcos em eixos longos.
5. **Escala errada** — teto baixo demais ou salão grande demais.
6. **Quase nenhum objeto.** Cada objeto solitário vira ponto de referência ou pista.
7. **Uma anomalia por área** (cor diferente, recursão, buraco) para quebrar a monotonia e marcar o caminho.

---

## 3. Física e sensação de jogo

Tudo nesta seção é **proposta**, salvo onde indicado.

### 3.1 Corpo do jogador
- Cápsula: 1,8 m de altura, 0,3 m de raio. Olhos a 1,65 m. Agachado: 1,1 m (olhos a 0,95 m).
- Velocidades: andar 2,8 m/s, correr 5,0 m/s, agachado 1,4 m/s.
- Fôlego: 6 s de corrida; recupera após 2 s parado, em 4 s. Sem fôlego, respiração ofegante audível (e que gera ruído).
- Aceleração e freio curtos (0,15 s) — sem deslizar, mas sem ser instantâneo.
- Gravidade −20 m/s². Pulo de 0,8 m — o suficiente para degraus, não para atravessar obstáculos altos.
- Degrau máximo 0,3 m; rampa máxima 45°.
- Balanço de câmera: 2–3 cm, sincronizado com os passos. Deve poder ser desligado nas opções.
- Câmera com leve atraso e tremor de mão (efeito filmadora), sem afetar a mira da interação.

### 3.2 Colisão e mundo
- Peças modulares com **colisores primitivos** (caixas), não colisores de malha. Paredes com 0,2 m de espessura real.
- Piso como colisor contínuo e espesso (0,5 m), não um plano fino.
- **Plano de segurança** abaixo do mapa: quem cair é reposicionado no último ponto válido. Isso responde direto à reclamação de "cair através do chão".
- Bloqueadores invisíveis onde o jogador não deve passar (por cima de mesas, em frestas).

### 3.3 Objetos físicos
- Itens pequenos (fita VHS, garrafa, pilha, cone) são corpos rígidos: podem ser pegos, soltos e arremessados (6–8 m/s).
- Detecção de colisão contínua nos arremessáveis, para não atravessarem paredes.
- **Impacto gera ruído:** a intensidade do som e do evento de ruído (para a IA) é proporcional à velocidade do impacto. Arremessar vira ferramenta de distração.
- Em rede: o host simula; ao pegar, o objeto vira cinemático e segue a mão do dono.

### 3.4 Portas, armários e mecanismos
- Portas animadas (não físicas), com estado controlado pelo host; trancáveis. Bloqueiam a navegação da entidade quando fechadas.
- Armários como esconderijo: entrar troca a câmera para uma fresta; a entidade tem uma chance de checar armários próximos de onde ouviu ruído.
- Interações de **segurar** (gerador, válvula) com barra de progresso curta — deixam o jogador vulnerável e fazem barulho.

### 3.5 Luz como sistema de jogo
- Cada luminária tem estado: ligada, piscando, queimada.
- Controlador de fase (no host) dispara eventos sincronizados:
  - **Blackout:** apaga todas as luminárias e os emissivos por 30–90 s.
  - **Alarme:** troca a cor para vermelho, pulsando, com sirene.
- Lanterna: luz spot com sombra, alcance ~25 m, cone interno 20° / externo 45°, com leve atraso ao seguir a câmera e bateria que acaba.
- A lanterna é **vista pelos outros jogadores e pelas entidades** — é ferramenta e risco.

### 3.6 Perigos de cenário
- **Buracos no chão:** queda = captura. Vigas de 0,3 m permitem atravessar andando devagar.
- **Água (Poolrooms):** rasa (0,3–1,0 m). Reduz a velocidade em até 40% conforme a profundidade; passos na água são mais altos (mais ruído). Superfície reflexiva com ondulação ao andar.

### 3.7 Regras de ritmo (das reclamações)
- Encontro com entidade deve ser **raro**: minutos de silêncio entre um e outro.
- Entidade que alcança o jogador resolve em **um contato** (no nosso jogo: captura).
- Entidade nunca aparece sem aviso sonoro e nunca nasce à vista.
- Progresso salvo por fase concluída.

---

## 4. Como reproduzir o visual no HDRP

- **Exposição fixa ou com faixa estreita.** Exposição automática total clareia as áreas escuras e mata a penumbra.
- **Luminária = malha emissiva forte + luz em tempo real sem sombra.** O bloom faz o painel estourar.
- **Rebatimento amarelo:** mapa procedural não permite lightmap. Usar luz ambiente constante amarelada bem fraca + Screen Space Global Illumination nos perfis de qualidade média e alta + oclusão de ambiente.
- **Névoa volumétrica** leve e amarelada para dar profundidade e esconder o fim dos corredores (30–40 m).
- **Reflexos:** Screen Space Reflections para piso molhado e azulejo.
- **Materiais:** papel de parede com rugosidade alta (0,8) e relevo sutil; carpete com rugosidade 0,95 e normal fibroso; azulejo com rugosidade baixa (0,15) e rejunte no normal.
- **Variação sem novos assets:** decalques (manchas, mofo, pichação, setas) aplicados por seed.
- **Pós-processamento (filmadora):** grão de filme, aberração cromática leve, vinheta, distorção de lente sutil, leve desfoque nas bordas, gradação de cor puxando sombras para verde-amarelo, tonemapping ACES. Carimbo "REC" e data são interface, não pós.
- **Desempenho:** sombra só na lanterna e em poucas luzes-chave; fade de luz por distância; instancing nas peças repetidas.

---

## 5. Prompt pronto para colar

```text
Contexto: jogo "The BackRooms", Unity 6.3 (6000.3), HDRP 17, co-op 1–4 com Netcode for
GameObjects, host autoritativo. Leia Docs/contexto-backrooms.md, Docs/plano-coop.md e
Docs/prompt-fases-visual-fisica.md antes de começar.

Tarefa: construir o Level 0 ("The Lobby") como fase jogável.

VISUAL
- Kit modular em grade de 4 m, pé-direito 2,7 m: parede (0,2 m), parede com vão, pilar
  quadrado, piso, forro com placas de 0,6 m, forro com luminária embutida de 0,6 x 1,2 m,
  rodapé de 0,1 m.
- Materiais: papel de parede amarelo-pálido (#C8B560) com padrão pequeno repetido e
  manchas; carpete de pelo curto bege-amarelado (#A6955A); forro acústico amarelado
  (#CFC79A). Um único material dominante por fase.
- Luz: painéis emissivos brancos (4500–5000 K) estourados com bloom; o amarelo vem do
  rebatimento. Distribuição irregular: uma luminária a cada 1–3 células, deixando áreas
  em penumbra. Algumas piscam, algumas estão queimadas.
- Composição: corredores longos com luminárias alinhadas até o ponto de fuga; salões com
  floresta de pilares; paredes soltas que não fecham salas; sem janelas, quase sem móveis.
- Pontos de referência raros: porta escura com placa EXIT vermelha, faixa vermelha na
  parede, pichação com seta, buraco no chão.
- HDRP: exposição fixa, névoa volumétrica leve amarelada, SSGI + SSAO, sombra apenas na
  lanterna. Pós de filmadora: grão, aberração cromática leve, vinheta, distorção sutil.
- Decalques de mancha e mofo distribuídos pela seed.

FÍSICA
- Jogador: cápsula 1,8 m x 0,3 m, olhos a 1,65 m; andar 2,8 m/s, correr 5,0 m/s,
  agachar 1,4 m/s; fôlego de 6 s; gravidade -20 m/s²; pulo de 0,8 m; balanço de câmera
  de 2–3 cm, desligável.
- Colisores primitivos em todas as peças; piso espesso; plano de segurança abaixo do
  mapa que reposiciona quem cair.
- Itens pequenos são corpos rígidos arremessáveis com detecção contínua; o impacto gera
  som e evento de ruído proporcional à velocidade.
- Lanterna spot com sombra, ~25 m, com atraso leve e bateria.
- Buracos no chão com vigas de 0,3 m; cair = captura.

SISTEMAS DE FASE
- Geração procedural determinística por seed (System.Random), igual em todos os clientes.
- Controlador de fase no host com eventos sincronizados: Blackout (apaga tudo por
  30–90 s) e Alarme (luz vermelha pulsante + sirene).
- Luminárias com estados ligada / piscando / queimada.
- Navegação diegética: faixa no chão ou na parede, placas, desenhos que ensinam regras.
- Encontros com entidade raros, sempre anunciados por som, nunca nascendo à vista.

RESTRIÇÕES
- Sem armas. Interface mínima. Nada que exija wiki externa para entender.
- Áreas escuras são obrigatórias; não iluminar tudo por igual.
- Entregar em etapas testáveis e dizer o que foi e o que não foi verificado.
```

---

## Fontes

- [Steam — Backrooms: Escape Together](https://store.steampowered.com/app/2141730/Backrooms_Escape_Together/)
- [Steam — Backrooms (2026)](https://store.steampowered.com/app/4211820/Backrooms_2026/)
- [Steam — The Backrooms (Ionized Games)](https://store.steampowered.com/app/1825160/The_Backrooms/)
- [Guia da comunidade — A Complete Guide to Backrooms: Escape Together](https://steamcommunity.com/sharedfiles/filedetails/?id=3481118821)
- [Wiki de fãs — Backrooms: Escape Together Levels](https://backroomsescapetogether.wiki/levels)
- [Análises de usuários — The Backrooms](https://steamcommunity.com/app/1825160/reviews/?browsefilter=toprated&filterLanguage=english)
