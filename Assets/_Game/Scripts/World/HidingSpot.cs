using BackRooms.Player;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// Volume de esconderijo (o nicho dentro de uma parede grossa). Marca o jogador como
    /// escondido enquanto ele estiver dentro. A entidade vai consultar isso na etapa 4.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class HidingSpot : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out PlayerController player)) player.SetHidden(true);
        }

        void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent(out PlayerController player)) player.SetHidden(false);
        }
    }
}
