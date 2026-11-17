using System.Collections.Generic;

namespace BackRooms.World
{
    // Dados das Piscinas (Level 2), além dos que já estão em LevelData. Tudo puro (sem Unity),
    // para o PoolGenerator dar a mesma planta em todas as máquinas a partir da seed.
    //
    // A planta das Piscinas NÃO é o labirinto do Level 0: é um conjunto de SALAS retangulares
    // (salões altos, piscinas com deque, canais, galerias de arcos, túneis em abóbada) separadas
    // por paredes grossas, com maciços fechados entre elas. Dentro de uma sala não há parede.

    /// <summary>O que existe na divisa entre duas células vizinhas (ou entre uma célula e a borda do mapa).</summary>
    public enum PoolEdge : byte
    {
        /// <summary>Nada: as duas células são da mesma sala (ou a sala continua). Passa-se.</summary>
        Open,
        /// <summary>Parede cheia, do fundo até o teto mais alto dos dois lados.</summary>
        Wall,
        /// <summary>Parede com um arco pleno de passagem: vão de 2,4 m, fecho a 3,2 m do piso mais alto. Passa-se.</summary>
        Arch,
        /// <summary>Parede com um arco abatido largo: vão de 3,4 m. Passa-se.</summary>
        WideArch,
        /// <summary>Parede com uma abertura redonda (Ø 2 m, centro a 1,9 m do piso mais alto). Vê-se através; não se passa.</summary>
        Porthole,
        /// <summary>Parede com um janelão de vidro fosco aceso. Só contra maciço ou borda do mapa. Não se passa.</summary>
        Window,
        /// <summary>Parede de tijolo de vidro acesa (verde-água). Só contra maciço ou borda do mapa. Não se passa.</summary>
        GlassBlock,
        /// <summary>Parede com um vão reto sem porta nem moldura: 1,4 m de largura, 2,4 m de altura acima do piso mais alto. Passa-se.</summary>
        Doorway,
        /// <summary>Parede com duas frestas verticais acesas (0,5 x 2,4 m). Só contra maciço ou borda do mapa. Não se passa.</summary>
        Slits,
        /// <summary>Parede com uma vigia redonda acesa (Ø 1,8 m, vidro fosco). Só contra maciço ou borda do mapa. Não se passa.</summary>
        RoundWindow
    }

    /// <summary>Claraboia no teto de uma célula. A luz do dia entra por ela, inclinada.</summary>
    public enum PoolSky : byte
    {
        None,
        /// <summary>Quadrada, 2,4 x 2,4 m, no meio da célula.</summary>
        Square,
        /// <summary>Redonda, Ø 2,8 m, no meio da célula.</summary>
        Round,
        /// <summary>Rasgo de 3,6 x 1,2 m ao longo de X.</summary>
        SlotX,
        /// <summary>Rasgo de 1,2 x 3,6 m ao longo de Z.</summary>
        SlotZ,
        /// <summary>O teto inteiro da célula é vidro fosco aceso (células vizinhas formam um teto luminoso).</summary>
        Full
    }

    /// <summary>Tipo da sala. Só informa (mapa, testes, registro): o construtor lê os campos, não o tipo.</summary>
    public enum PoolRoomKind : byte
    {
        Start,
        Exit,
        /// <summary>Salão alto com colunas, água rasa ou funda.</summary>
        Hall,
        /// <summary>Piscina funda rebaixada com deque seco em volta.</summary>
        Basin,
        /// <summary>Canal: água funda no meio, passeio seco de um ou dos dois lados.</summary>
        Canal,
        /// <summary>Galeria: corredor largo com arcos em sequência.</summary>
        Gallery,
        /// <summary>Túnel estreito em abóbada.</summary>
        Tunnel,
        /// <summary>Sala do poço: escura, com o poço redondo no fundo.</summary>
        WellRoom,
        /// <summary>Escadaria: degraus largos descendo para dentro d'água.</summary>
        Terrace,
        /// <summary>Qualquer outra.</summary>
        Other,
        /// <summary>Átrio da claraboia: teto de 12 m, anel raso e ilha seca no meio.</summary>
        Atrium,
        /// <summary>Cisterna: escura, funda, com pilares em todo vértice.</summary>
        Cistern,
        /// <summary>Raia longa: raso, fundo, raso, com claraboia contínua. O eixo de vista do mapa.</summary>
        Lane,
        /// <summary>Corredor de janelas: frestas ou vigias acesas em série.</summary>
        WindowGallery,
        /// <summary>Fenda: corredor de uma célula de largura e teto de 8 ou 12 m, com rasgo de luz no topo.</summary>
        Slot
    }

    public sealed class PoolRoom
    {
        public int Id;
        public PoolRoomKind Kind;
        /// <summary>Retângulo da sala, em células.</summary>
        public int X0, Z0, Width, Height;
        /// <summary>Altura do teto plano (ou do arranque da abóbada), em metros acima da água (Y = 0).</summary>
        public float Ceiling;
        /// <summary>Teto em abóbada de berço no lugar do plano. Só em salas com 1 ou 2 células de largura.</summary>
        public bool Vault;
        /// <summary>A abóbada corre ao longo de X (senão, ao longo de Z).</summary>
        public bool VaultAlongX;
        /// <summary>Faixa de azulejo azul nas paredes (uma fiada na linha d'água e outra a 2 m).</summary>
        public bool Accent;
        /// <summary>Sala escura de propósito: sem claraboia nem janela, só a luz de dentro d'água.</summary>
        public bool Dark;

        public bool Contains(int x, int z) => x >= X0 && x < X0 + Width && z >= Z0 && z < Z0 + Height;
    }

    /// <summary>
    /// Coluna lisa, sem base nem capitel, do fundo ao teto. Posição em metros no espaço do nível
    /// (X = célula * 4). Nunca fica a menos de (raio + 0,6 m) das linhas de centro das células,
    /// para não fechar passagem: nos vértices da grade (tambor de raio até 0,9; esbelta de 0,45)
    /// ou a 1 m deles sobre uma linha da grade (raio até 0,4).
    /// </summary>
    public struct PoolColumn
    {
        public float X, Z;
        /// <summary>Raio da coluna redonda, ou meio lado da quadrada.</summary>
        public float Radius;
        public bool Round;
    }

    /// <summary>Luz de dentro d'água: um disco aceso na parede <see cref="Side"/> da célula, abaixo da linha d'água.</summary>
    public struct PoolLamp
    {
        public int X, Z;
        public WallSide Side;
        /// <summary>Altura do centro, em metros (negativa: abaixo da água).</summary>
        public float Y;
    }
}
