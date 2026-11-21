namespace BackRooms.UI
{
    /// <summary>
    /// O ponto central que diz se o jogador local pode comandar agora: falso com o menu de pausa
    /// aberto (<see cref="PauseMenu.IsOpen"/>) ou com o painel do código do Level 1 na tela
    /// (<see cref="CodePanel"/>). O jogo não para em nenhum dos dois: o jogador ainda cai, afunda e é
    /// pego, só não anda, não gira a câmera, não usa itens nem o E. <c>PlayerController</c>,
    /// <c>PenTool</c>, <c>Flashlight</c> e os usos de E leem <see cref="Blocked"/>.
    /// </summary>
    public static class InputGate
    {
        /// <summary>
        /// A entrada do jogador está tomada por uma tela. Vale também no quadro em que o painel do
        /// código fechou, para o Esc ou o E que o fechou não abrir o menu de pausa nem a porta.
        /// </summary>
        public static bool Blocked => PauseMenu.IsOpen || CodePanel.TakesInput;
    }
}
