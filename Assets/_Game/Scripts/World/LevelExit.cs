using System;
using BackRooms.Player;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// Gatilho na porta de saída. Por enquanto só avisa que o jogador chegou; o que
    /// acontece depois (elevador, próxima fase) entra na etapa 6.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class LevelExit : MonoBehaviour
    {
        public static event Action<PlayerController> Reached;

        void OnTriggerEnter(Collider other)
        {
            if (!other.TryGetComponent(out PlayerController player)) return;

            Debug.Log("[BackRooms] Saída encontrada.");
            Reached?.Invoke(player);
        }
    }
}
