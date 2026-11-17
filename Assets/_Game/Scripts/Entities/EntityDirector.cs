using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.World;
using Unity.Netcode;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// Decide quando e onde a entidade aparece. Roda só no host. Serve também ao Capitão do
    /// Level 1: o prefab é outro (com <see cref="CaptainBrain"/>, que herda de
    /// <see cref="EntityBrain"/>) e a regra de quando e onde é a mesma.
    ///
    /// Ela não existe no começo da partida. Surge quando algum jogador já se afastou bastante
    /// da sala inicial, e surge atrás dele: numa célula do caminho entre a sala e esse jogador,
    /// longe o bastante de todos e sem que ninguém a esteja vendo. Daí segue para onde ele
    /// estava — quem olhar para trás vai vê-la vindo do lado de onde saiu.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public sealed class EntityDirector : MonoBehaviour
    {
        public static EntityDirector Instance { get; private set; }

        /// <summary>Para os testes automáticos: enquanto verdadeiro, a entidade não aparece.</summary>
        public static bool Paused;

        public GameSession session;
        public GameObject entityPrefab;

        [Tooltip("Desligado, a fase fica sem entidade.")]
        public bool entityEnabled = true;

        [Header("Quando aparece")]
        [Tooltip("Distância de caminhada da sala inicial, em células de 4 m, que algum jogador precisa alcançar.")]
        public int wakeCells = 8;
        [Tooltip("Tempo mínimo de partida, em segundos.")]
        public float minSeconds = 15f;

        [Header("Onde aparece")]
        [Tooltip("Distância mínima de caminhada até qualquer jogador, em células.")]
        public int minCellsFromPlayers = 4;

        /// <summary>A entidade desta sessão, depois que apareceu (só no host).</summary>
        public EntityBrain Entity { get; private set; }

        /// <summary>Por que a última tentativa de fazê-la aparecer não deu certo. Para diagnóstico.</summary>
        public string LastRefusal { get; private set; } = "";

        static readonly RaycastHit[] Hits = new RaycastHit[24];

        readonly List<Vector3> _path = new List<Vector3>();
        readonly List<PlayerNetwork> _players = new List<PlayerNetwork>();

        LevelNav _nav;
        int[,] _fromDoor;
        int[,] _fromPlayer;
        int _doorX, _doorZ;
        bool _hasDoor;
        float _playingSince = -1f;
        float _nextTry;
        int _tooClose;
        int _inSight;

        void Awake()
        {
            Instance = this;
            if (session == null) session = GetComponent<GameSession>();

            // O prefab da entidade não é registrado aqui: o Netcode mantém sozinho a lista
            // Assets/DefaultNetworkPrefabs.asset com todo prefab que tem NetworkObject, e o
            // NetworkManager da cena usa essa lista. Registrar de novo dá erro de duplicata.
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
                _nav = null;
                Entity = null;
                return;
            }

            if (_playingSince < 0f) _playingSince = Time.time;

            if (!session.IsHosting || !entityEnabled || Paused || entityPrefab == null || Entity != null) return;
            if (Time.time - _playingSince < minSeconds || Time.time < _nextTry) return;

            _nextTry = Time.time + 1f;
            TrySpawn();
        }

        /// <summary>
        /// Host: põe a entidade no mapa agora, se houver um lugar que sirva. Devolve falso se
        /// ninguém se afastou o bastante ou se todo o caminho está à vista de alguém.
        /// </summary>
        public bool TrySpawn()
        {
            LevelBuilder level = session.level;
            if (level == null || level.Data == null) return false;

            if (_nav == null || _nav.Data != level.Data)
            {
                _nav = new LevelNav(level);
                _fromDoor = _nav.NewMap();
                _fromPlayer = _nav.NewMap();
                _hasDoor = _nav.DoorCell(out _doorX, out _doorZ);
                if (_hasDoor) _nav.Distances(_doorX, _doorZ, _fromDoor);
            }

            if (!_hasDoor) return false;

            // Quem foi mais longe é quem a chama.
            _players.Clear();
            PlayerNetwork lead = null;
            int leadX = 0, leadZ = 0, farthest = -1;

            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                _players.Add(player);

                if (!_nav.WorldToCell(player.transform.position, out int px, out int pz) || _fromDoor[px, pz] <= farthest) continue;

                farthest = _fromDoor[px, pz];
                lead = player;
                leadX = px;
                leadZ = pz;
            }

            if (lead == null || farthest < wakeCells)
            {
                LastRefusal = $"ninguém a {wakeCells} células da sala (o mais longe está a {farthest})";
                return false;
            }

            if (!_nav.Path(_doorX, _doorZ, leadX, leadZ, _path))
            {
                LastRefusal = "sem caminho da porta até o jogador";
                return false;
            }

            // O caminho da porta até o jogador, com a célula da porta no começo.
            _path.Insert(0, _nav.CellCenter(_doorX, _doorZ));

            // Entre as células desse caminho que servem, a mais próxima do meio.
            int middle = _path.Count / 2;
            int best = -1;
            _tooClose = 0;
            _inSight = 0;

            for (int i = 0; i < _path.Count - 1; i++)
            {
                if (best >= 0 && Mathf.Abs(i - middle) >= Mathf.Abs(best - middle)) continue;
                if (Suitable(_path[i], level.transform)) best = i;
            }

            if (best < 0)
            {
                LastRefusal = $"nenhuma das {_path.Count - 1} células do caminho serve: {_tooClose} perto demais de alguém, {_inSight} à vista de alguém";
                return false;
            }

            LastRefusal = "";
            Vector3 position = _path[best];
            SpawnAt(position, _path[best + 1] - position);
            Entity.Alert(lead.transform.position);
            return true;
        }

        /// <summary>
        /// Host: põe a entidade (ou o Capitão) num ponto escolhido, olhando para
        /// <paramref name="facing"/>, sem conferir nada. Para os testes; o jogo usa
        /// <see cref="TrySpawn"/>. Se já existe, só muda de lugar.
        /// </summary>
        public EntityBrain SpawnAt(Vector3 position, Vector3 facing)
        {
            facing.y = 0f;
            Quaternion rotation = Quaternion.LookRotation(facing.sqrMagnitude > 0.01f ? facing : Vector3.forward, Vector3.up);

            if (Entity != null)
            {
                Entity.transform.SetPositionAndRotation(position, rotation);
                if (Entity.TryGetComponent(out Unity.Netcode.Components.NetworkTransform moved))
                    moved.Teleport(position, rotation, Entity.transform.localScale);
                return Entity;
            }

            GameObject spawned = Instantiate(entityPrefab, position, rotation);
            spawned.GetComponent<NetworkObject>().Spawn(true);
            Entity = spawned.GetComponent<EntityBrain>();
            return Entity;
        }

        // A célula serve se está longe de todos, andando, e fora da vista de todos.
        bool Suitable(Vector3 position, Transform levelRoot)
        {
            if (!_nav.WorldToCell(position, out int x, out int z)) return false;

            _nav.Distances(x, z, _fromPlayer);
            Vector3 head = position + Vector3.up * 2.4f;

            foreach (PlayerNetwork player in _players)
            {
                Vector3 at = player.transform.position;

                // Quem está na sala inicial está a salvo dela, então a distância não importa
                // (só não pode vê-la surgir). Quem está em cima de uma faixa fica fora do
                // alcance dela: para esse, vale a distância em linha reta.
                bool onMap = _nav.WorldToCell(at, out int px, out int pz);
                bool inRoom = onMap && _nav.Data.InRoom(px, pz);
                int steps = onMap ? _fromPlayer[px, pz] : -1;
                float straight = Vector3.Distance(position, at) / LevelBuilder.Cell;

                if (!inRoom && (steps >= 0 ? steps < minCellsFromPlayers : straight < minCellsFromPlayers))
                {
                    _tooClose++;
                    return false;
                }

                if (!Blocked(head, at + Vector3.up * 1.5f, levelRoot))
                {
                    _inSight++;
                    return false;
                }
            }

            return true;
        }

        static bool Blocked(Vector3 from, Vector3 to, Transform levelRoot)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return false;

            int count = Physics.RaycastNonAlloc(from, delta / distance, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Hits[i].collider.transform.IsChildOf(levelRoot)) return true;

            return false;
        }
    }
}
