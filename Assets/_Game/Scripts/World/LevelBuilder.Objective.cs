using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// O objetivo comum da fase (<see cref="SwitchObjective"/>): disjuntores, válvulas ou alarmes,
    /// postos por último, com um sorteio próprio, depois de a planta e o resto estarem prontos.
    /// </summary>
    public sealed partial class LevelBuilder
    {
        [Header("Objetivo")]
        [Tooltip("Pontos que destrancam a saída: disjuntores (Level 0), válvulas (Level 2), alarmes (Level 3). None = sem objetivo comum.")]
        public SwitchKind objective = SwitchKind.None;
        [Tooltip("Material das peças do objetivo (a cor é trocada por peça).")]
        public Material switchBodyMaterial;
        [Tooltip("Lâmpada das peças do objetivo (sem luz; a cor vem de um MaterialPropertyBlock).")]
        public Material switchLampMaterial;
        [Tooltip("Pilhas da lanterna espalhadas pelo chão (P0.3), com sorteio próprio.")]
        public bool batteriesEnabled;

        void BuildObjective()
        {
            if (Data == null) return;
            if (objective != SwitchKind.None)
            {
                GameObject root = CreateChild("Objetivo");
                var switches = root.AddComponent<SwitchObjective>();
                switches.Place(this, objective, switchBodyMaterial, switchLampMaterial);
            }

            // Pilhas da lanterna: depois do objetivo (desviam das peças dele), com outro sorteio.
            if (batteriesEnabled) CreateChild("Pilhas").AddComponent<BatteryPickups>().Place(this, switchBodyMaterial);
        }
    }
}
