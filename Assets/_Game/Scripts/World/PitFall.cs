using BackRooms.Player;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// Volume no fundo de um buraco. Quem entra nele já passou do ponto de se segurar: o
    /// jogador é dado como caído e volta para a sala inicial (ver PlayerController.FallIntoPit).
    /// Só reage ao jogador desta máquina; os outros decidem a própria queda nas máquinas deles.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class PitFall : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out PlayerController player) && player.enabled) player.FallIntoPit();
        }
    }
}
