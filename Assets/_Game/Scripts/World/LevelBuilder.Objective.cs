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
        [Tooltip("Disjuntor da Tripo (ImportedModels.EnsureLever): a caixa, com o centro em Y = 0, o fundo em Z = 0 e a frente " +
                 "para -Z. Vazio = a caixa de peças primitivas.")]
        public Mesh switchBreakerBox;
        [Tooltip("A alça do disjuntor da Tripo, com a origem no eixo de giro; girada 0° é desligada (pendurada).")]
        public Mesh switchBreakerHandle;
        public Material switchBreakerMaterial;
        [Tooltip("Eixo de giro da alça, no espaço da caixa do modelo.")]
        public Vector3 switchBreakerPivot;
        [Tooltip("Pilhas da lanterna espalhadas pelo chão (P0.3), com sorteio próprio.")]
        public bool batteriesEnabled;
        [Tooltip("Válvulas (Level 2): onde fica a segunda. AcrossWater = numa ilha ou na parede sobre a funda (só se chega nadando); WellBottom = no fundo de um poço que não é o covil (o passo seguinte, se ainda ficar fácil); Dry = as duas no seco, como até 21/11/2026.")]
        public WaterValveSpot waterValve = WaterValveSpot.AcrossWater;

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
