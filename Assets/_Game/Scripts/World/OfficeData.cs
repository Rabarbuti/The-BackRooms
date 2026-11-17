using System.Collections.Generic;

namespace BackRooms.World
{
    /// <summary>O que ocupa uma célula do Escritório (Level 3).</summary>
    public enum OfficeCell : byte
    {
        /// <summary>Fora da planta (não sobra nenhuma hoje; reservado).</summary>
        Solid,
        Corridor,
        /// <summary>Saguão dos elevadores.</summary>
        Lobby,
        /// <summary>Cabine do elevador onde os jogadores nascem (a "sala inicial").</summary>
        Cab,
        /// <summary>Poço de elevador fechado: só a porta de inox aparece, do lado do saguão.</summary>
        Shaft,
        /// <summary>Sala de aula ou de reunião (área de 6 células ou mais).</summary>
        Classroom,
        /// <summary>Sala pequena: técnica, laboratório, depósito.</summary>
        Small,
        Cafe,
        /// <summary>Área de estar: sofá, pufes e plantas, aberta para o corredor.</summary>
        Lounge,
        /// <summary>Vão da escada: sem piso (vê-se os andares de cima e de baixo), com a escada até a saída.</summary>
        Atrium
    }

    /// <summary>
    /// O que há numa divisa entre duas células do Escritório. Mesmos índices de
    /// <see cref="LevelData.WallSouth"/> / <see cref="LevelData.WallWest"/>, que continuam valendo
    /// para andar: falso em <see cref="Open"/>, <see cref="Door"/>, <see cref="GlassDoor"/>,
    /// <see cref="CabDoor"/> e <see cref="StairTop"/>; verdadeiro nos outros.
    /// </summary>
    public enum OfficeEdge : byte
    {
        Open,
        Wall,
        /// <summary>Parede com porta de sala (aberta, encostada na parede de dentro).</summary>
        Door,
        /// <summary>Divisória de vidro fosco em esquadria branca.</summary>
        Glass,
        /// <summary>Divisória de vidro com um vão de porta.</summary>
        GlassDoor,
        /// <summary>Parede de fora com janela e persiana (só na borda do mapa).</summary>
        Window,
        /// <summary>Porta de elevador fechada (poço), vista do saguão.</summary>
        ElevatorDoor,
        /// <summary>Porta da cabine inicial: abre alguns segundos depois do início.</summary>
        CabDoor,
        /// <summary>Guarda-corpo do vão da escada.</summary>
        Railing,
        /// <summary>Entrada da escada: do corredor para o patamar de cima.</summary>
        StairTop
    }

    /// <summary>Uma sala (ou espaço) do Escritório: retângulo de células do mesmo tipo.</summary>
    public struct OfficeRoom
    {
        public OfficeCell Kind;
        public int X0;
        public int Z0;
        public int Width;
        public int Height;
        /// <summary>Luzes acesas (falso = luminárias queimadas; só a janela, se houver).</summary>
        public bool Lit;
        /// <summary>Sorteio estável para variar a mobília.</summary>
        public int Variant;
        /// <summary>Lado da sala que dá para a borda do mapa (janelas), ou <see cref="WallSide.Floor"/> se nenhum.</summary>
        public WallSide WindowSide;

        public bool Contains(int x, int z) => x >= X0 && x < X0 + Width && z >= Z0 && z < Z0 + Height;
    }

    /// <summary>
    /// O vão da escada e a saída. A escada encosta na parede de uma das pontas do vão
    /// (<see cref="EndSide"/>): sai do corredor (<see cref="HeadX"/>, <see cref="HeadZ"/>) pela
    /// célula (<see cref="TopX"/>, <see cref="TopZ"/>), desce um lance no sentido
    /// <see cref="Inward"/> (do corredor para a borda do mapa) até um patamar, onde fica a porta
    /// corta-fogo da saída, na parede da ponta.
    /// </summary>
    public struct OfficeAtrium
    {
        public int X0;
        public int Z0;
        public int Width;
        public int Height;
        public int TopX;
        public int TopZ;
        public int HeadX;
        public int HeadZ;
        /// <summary>Do corredor para dentro do vão.</summary>
        public WallSide Inward;
        /// <summary>Parede da ponta em que a escada encosta (e onde fica a porta da saída).</summary>
        public WallSide EndSide;

        public bool Contains(int x, int z) => x >= X0 && x < X0 + Width && z >= Z0 && z < Z0 + Height;
    }

    /// <summary>Uma porta de elevador do saguão: a da cabine inicial ou a de um poço fechado.</summary>
    public struct OfficeElevator
    {
        /// <summary>Célula do poço (ou da cabine).</summary>
        public int X;
        public int Z;
        /// <summary>Lado do poço por onde se entra (dá para o saguão).</summary>
        public WallSide Side;
        public bool IsCab;
    }
}
