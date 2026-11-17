using System.Collections.Generic;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>Piso de uma zona do Level 1 (level1-mostruario.md, seção 3).</summary>
    public enum FloorKind : byte
    {
        /// <summary>O carpete amarelo de sempre.</summary>
        Carpet,
        /// <summary>Laminado de madeira, debaixo das vitrines de sala de jantar e de estar.</summary>
        Laminate,
        /// <summary>Piso vinílico cinza, no estoque.</summary>
        Vinyl
    }

    /// <summary>
    /// Onde o piso não é carpete: a <see cref="PropLayer"/> cobre algumas células com laminado ou
    /// vinílico e registra a área aqui. Os passos (<c>Footsteps</c>) consultam
    /// <see cref="At"/> para escolher o som. Só leitura em jogo; nada vai pela rede (sai da seed).
    /// </summary>
    public static class FloorZones
    {
        static readonly List<(Rect area, FloorKind kind)> Zones = new List<(Rect, FloorKind)>();

        public static int Count => Zones.Count;

        public static void Clear() => Zones.Clear();

        public static void Add(Rect worldXZ, FloorKind kind) => Zones.Add((worldXZ, kind));

        /// <summary>O piso sob o ponto (os pés de alguém).</summary>
        public static FloorKind At(Vector3 position)
        {
            var p = new Vector2(position.x, position.z);
            for (int i = 0; i < Zones.Count; i++)
                if (Zones[i].area.Contains(p)) return Zones[i].kind;
            return FloorKind.Carpet;
        }

        // Sem recarregar o domínio ao entrar em Play, as zonas sobrariam da partida anterior.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Zones.Clear();
    }
}
