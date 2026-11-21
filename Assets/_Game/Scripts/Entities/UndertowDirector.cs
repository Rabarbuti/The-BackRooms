using BackRooms.Net;
using BackRooms.World;
using Unity.Netcode;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// Põe o Undertow (<see cref="UndertowBrain"/>) nas Piscinas. Roda só no host. Não é o
    /// <see cref="EntityDirector"/>: ela não surge atrás de ninguém; está lá desde o começo,
    /// dormindo no fundo do covil, e é o barulho na água que a acorda.
    ///
    /// O covil: um poço (o mais longe da sala inicial), ou, se a planta não tem poço, a célula de
    /// água funda mais longe da sala inicial.
    ///
    /// Os testes que nadam e não são dela a desligam com <see cref="Paused"/> (ou com o
    /// <see cref="EntityDirector.Paused"/>, que vale para todo inimigo).
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public sealed class UndertowDirector : MonoBehaviour
    {
        public static UndertowDirector Instance { get; private set; }

        /// <summary>Para os testes: enquanto verdadeiro, ela não aparece.</summary>
        public static bool Paused;

        public GameSession session;
        public GameObject prefab;

        [Tooltip("Segundos de partida antes de ela estar no covil.")]
        public float spawnAfter = 3f;

        /// <summary>O Undertow desta sessão (só no host).</summary>
        public UndertowBrain Creature { get; private set; }

        float _playingSince = -1f;

        void Awake()
        {
            Instance = this;
            if (session == null) session = GetComponent<GameSession>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (session == null || session.Phase != SessionPhase.Playing || !session.LevelReady)
            {
                _playingSince = -1f;
                Creature = null;
                return;
            }

            if (_playingSince < 0f) _playingSince = Time.time;
            if (!session.IsHosting || Paused || EntityDirector.Paused || prefab == null || Creature != null) return;
            if (Time.time - _playingSince < spawnAfter) return;

            LevelBuilder level = session.level;
            if (level == null || level.Data == null || !level.Data.HasPools) return;
            if (FindLair(level, out Vector3 lair)) SpawnAt(lair, true);
        }

        /// <summary>
        /// Host: põe o Undertow em <paramref name="position"/> (dormindo ali, que vira o covil, se
        /// <paramref name="asleep"/>). Se já existe, só muda de lugar. Para os testes também.
        /// </summary>
        public UndertowBrain SpawnAt(Vector3 position, bool asleep)
        {
            if (Creature == null)
            {
                // Pelo NetworkManager desta sessão, não pelo Singleton (ver EntityDirector.SpawnAt).
                NetworkObject spawned = NetworkObject.InstantiateAndSpawn(prefab, GetComponent<NetworkManager>(),
                    NetworkManager.ServerClientId, destroyWithScene: true, position: position, rotation: Quaternion.identity);
                Creature = spawned != null ? spawned.GetComponent<UndertowBrain>() : null;
                if (Creature == null) return null;
            }

            if (asleep) Creature.SleepAt(position);
            else Creature.Place(position, Creature.transform.rotation);
            return Creature;
        }

        /// <summary>O covil: o poço mais longe da sala inicial; sem poço, a água funda mais longe dela.</summary>
        public static bool FindLair(LevelBuilder level, out Vector3 lair)
        {
            LevelData data = level.Data;
            lair = Vector3.zero;
            Vector3 start = Vector3.zero;
            int starts = 0;
            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++)
                    if (data.InRoom(x, z)) { start += level.CellCenter(x, z); starts++; }
            if (starts > 0) start /= starts;

            float best = -1f;
            bool wellFound = false;
            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++)
                {
                    if (data.IsSolid(x, z)) continue;
                    PoolDepth depth = data.Depth[x, z];
                    bool well = depth == PoolDepth.Well;
                    if (!well && depth != PoolDepth.Deep) continue;
                    if (wellFound && !well) continue;
                    Vector3 c = level.CellCenter(x, z);
                    float d = new Vector2(c.x - start.x, c.z - start.z).magnitude;
                    if (well && !wellFound) { wellFound = true; best = -1f; }
                    if (d <= best) continue;
                    best = d;
                    lair = c;
                }
            return best >= 0f;
        }
    }
}
