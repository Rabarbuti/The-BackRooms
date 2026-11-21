using System.Collections.Generic;
using BackRooms.Entities;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// Quem ouve barulho no mundo: um inimigo implementa e se registra em <see cref="WorldNoise"/>
    /// (no OnEnable/OnNetworkSpawn; sai no OnDisable/OnNetworkDespawn). É chamado em todas as
    /// máquinas; quem tem cérebro só no host decide lá (ignora no cliente).
    /// </summary>
    public interface INoiseListener
    {
        /// <summary>
        /// Barulho forte em <paramref name="at"/>, que se ouve até <paramref name="radius"/> metros
        /// (sem descontar paredes). O ouvinte confere a própria distância e decide se vai até lá.
        /// </summary>
        void HearWorldNoise(Vector3 at, float radius);
    }

    /// <summary>
    /// Barulho no mundo, sem depender de qual inimigo a fase tem: "aconteceu um barulho na posição X
    /// com alcance R". Quem faz barulho chama <see cref="Emit"/>; quem ouve implementa
    /// <see cref="INoiseListener"/> e se registra. Hoje usam: os armários que batem no Escritório
    /// (<see cref="LockerBangs"/>).
    ///
    /// Ponte provisória: a entidade do Level 0 (<see cref="EntityBrain"/>, e o Capitão, que herda
    /// dela) ainda não se registra. Enquanto ela não implementar a interface, o barulho chega nela
    /// por <see cref="EntityBrain.HearNoise"/> se estiver dentro do alcance.
    /// </summary>
    public static class WorldNoise
    {
        static readonly List<INoiseListener> Listeners = new List<INoiseListener>();

        /// <summary>Barulhos emitidos desde que o jogo abriu (para os testes).</summary>
        public static int Emitted { get; private set; }

        public static Vector3 LastAt { get; private set; }
        public static float LastRadius { get; private set; }

        /// <summary>Quantos ouvintes (registrados ou pela ponte) receberam o último barulho.</summary>
        public static int LastHeardBy { get; private set; }

        public static void Register(INoiseListener listener)
        {
            if (listener != null && !Listeners.Contains(listener)) Listeners.Add(listener);
        }

        public static void Unregister(INoiseListener listener) => Listeners.Remove(listener);

        public static void Emit(Vector3 at, float radius)
        {
            Emitted++;
            LastAt = at;
            LastRadius = radius;
            int heard = 0;

            Listeners.RemoveAll(l => l == null || (l is Object o && o == null));
            foreach (INoiseListener listener in Listeners.ToArray())
            {
                listener.HearWorldNoise(at, radius);
                heard++;
            }

            // Ponte para a entidade do Level 0 enquanto ela não for um INoiseListener.
            EntityBrain brain = EntityBrain.Instance;
            if (brain != null && !(brain is INoiseListener) && (brain.transform.position - at).magnitude <= radius)
            {
                brain.HearNoise(at);
                heard++;
            }

            LastHeardBy = heard;
        }
    }
}
