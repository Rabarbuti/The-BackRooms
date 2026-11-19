using System;
using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using BackRooms.World;
using Unity.Netcode;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// O Capitão, o inimigo do Level 1 (Docs/level1-mostruario.md, seção 5.4). Herda da
    /// entidade do Level 0 o ouvido, a visão, o caminho e a captura, e muda três coisas:
    ///
    /// • <b>Não se mexe enquanto é visto.</b> Se algum jogador o tem na tela, com linha de
    ///   visada, e ele está iluminado (uma luminária acesa perto ou o facho de uma lanterna), ele
    ///   congela onde está, mesmo no meio de um passo, e não pega ninguém. O corpo segura a pose
    ///   (<see cref="CaptainBody"/> lê <see cref="Frozen"/>, que vai pela rede).
    /// • <b>Não dá para vigiar para sempre.</b> Quem o encara por mais de
    ///   <see cref="stareNoiseAfter"/> segundos vê o chuvisco da filmadora crescer; passados
    ///   <see cref="stareBlindAfter"/>, a imagem some e esse jogador deixa de valer como vigia
    ///   até desviar o olhar um tempo. Alguém precisa piscar.
    /// • <b>Apagão é a hora dele.</b> No escuro, caçando, anda em arrancadas
    ///   (<see cref="darkDashSpeed"/> por <see cref="dashSeconds"/>, depois
    ///   <see cref="dashRestSeconds"/> mancando). Fora isso manca devagar.
    ///
    /// • <b>Troca com um totem no apagão</b> (regra 5): no começo de um apagão, com chance
    ///   <see cref="swapChance"/>, se ninguém o vê, ele some e aparece no lugar de um totem que
    ///   ninguém está olhando, e o totem vai para onde ele estava. A posição de cada totem movido
    ///   vai pela rede (<see cref="TotemPose"/>), e quem entra depois recebe a lista.
    ///
    /// Só o host decide se ele está congelado. O quanto o jogador desta máquina o encarou
    /// (<see cref="LocalStare"/>, para o chuvisco) é contado em cada máquina com a mesma conta.
    /// </summary>
    public sealed class CaptainBrain : EntityBrain
    {
        /// <summary>O Capitão da sessão, em qualquer máquina. Nulo enquanto ele não apareceu.</summary>
        public static CaptainBrain Captain { get; private set; }

        [Header("Capitão: apagão")]
        [Tooltip("Velocidade das arrancadas no escuro, caçando (m/s).")]
        public float darkDashSpeed = 5.2f;
        public float dashSeconds = 1.5f;
        [Tooltip("Pausa mancando entre uma arrancada e outra, em segundos.")]
        public float dashRestSeconds = 1.0f;

        [Header("Capitão: ser visto")]
        [Tooltip("Até onde um jogador o vigia, em metros.")]
        public float watchRange = 30f;
        [Tooltip("Até onde a luz de uma luminária acesa o ilumina, em metros.")]
        public float lampReach = 6.5f;
        [Tooltip("Encarando por mais que isto (s), o chuvisco da filmadora começa a crescer.")]
        public float stareNoiseAfter = 6f;
        [Tooltip("Encarando por mais que isto (s), a imagem some e o jogador deixa de vigiá-lo.")]
        public float stareBlindAfter = 9f;
        [Tooltip("Quão depressa o cansaço de encarar passa olhando para outro lado (segundos por segundo).")]
        public float stareRecovery = 1.5f;

        [Header("Capitão: totens")]
        [Tooltip("Chance de trocar de lugar com um totem em cada apagão.")]
        [Range(0f, 1f)] public float swapChance = 0.5f;
        [Tooltip("Segundos de escuro antes da troca.")]
        public float swapDelay = 1.2f;
        [Tooltip("O totem escolhido fica a pelo menos esta distância de todo jogador, em metros.")]
        public float swapMinDistance = 8f;

        /// <summary>Uma troca de lugar: o totem de índice <see cref="Index"/> foi para outro ponto.</summary>
        public struct TotemPose : INetworkSerializable, IEquatable<TotemPose>
        {
            public ushort Index;
            public Vector3 Position;
            public float Yaw;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Index);
                serializer.SerializeValue(ref Position);
                serializer.SerializeValue(ref Yaw);
            }

            public bool Equals(TotemPose other) => Index == other.Index && Position == other.Position && Yaw == other.Yaw;
        }

        /// <summary>Quantas vezes trocou de lugar com um totem (só no host). Para os testes.</summary>
        public int Swaps { get; private set; }

        /// <summary>Para os testes: a próxima troca acontece (sem sorteio) no próximo apagão.</summary>
        public bool ForceNextSwap { get; set; }

        /// <summary>Congelado por estar sendo visto e iluminado. Vale em qualquer máquina.</summary>
        public bool Frozen => _frozen.Value;

        /// <summary>Há quantos segundos o jogador desta máquina o encara (com o desconto de quando desvia).</summary>
        public float LocalStare => PlayerNetwork.Local != null && _stare.TryGetValue(PlayerNetwork.Local, out float s) ? s : 0f;

        /// <summary>Quantas vezes congelou (só no host). Para os testes.</summary>
        public int Freezes { get; private set; }

        /// <summary>Maior velocidade que alcançou desde a última leitura (só no host). Para os testes.</summary>
        public float TopSpeed { get; set; }

        const float WatchInterval = 0.1f;
        // Proporção da tela suposta para a câmera de quem vigia: no host a câmera dos outros
        // jogadores fica desligada e não tem tamanho de tela de verdade.
        const float Aspect = 16f / 9f;
        // Pontos do corpo conferidos: cabeça, peito e joelho. Basta um à vista.
        static readonly float[] BodyHeights = { 2.35f, 1.5f, 0.6f };
        static readonly RaycastHit[] Hits = new RaycastHit[24];

        readonly NetworkVariable<bool> _frozen = new NetworkVariable<bool>();
        readonly NetworkList<TotemPose> _totems = new NetworkList<TotemPose>();
        bool _swapDarkSeen;
        float _swapAt = -1f;
        readonly Dictionary<PlayerNetwork, float> _stare = new Dictionary<PlayerNetwork, float>();
        readonly List<PlayerNetwork> _gone = new List<PlayerNetwork>();

        float _watchAt;
        float _watchedAt;
        bool _watched;
        bool _wasDark;
        float _darkSince;
        Vector3 _lastPosition;
        float _lastStep;

        protected override bool IsLevel0Entity => false;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Captain = this;
            _lastPosition = transform.position;

            // Quem entra depois recebe a lista inteira: põe os totens já trocados no lugar.
            _totems.OnListChanged += OnTotemsChanged;
            for (int i = 0; i < _totems.Count; i++) ApplyTotem(_totems[i]);
        }

        public override void OnNetworkDespawn()
        {
            _totems.OnListChanged -= OnTotemsChanged;
            base.OnNetworkDespawn();
            if (Captain == this) Captain = null;
        }

        void OnTotemsChanged(NetworkListEvent<TotemPose> change)
        {
            if (change.Type == NetworkListEvent<TotemPose>.EventType.Add ||
                change.Type == NetworkListEvent<TotemPose>.EventType.Value)
                ApplyTotem(change.Value);
        }

        static void ApplyTotem(TotemPose pose)
        {
            if (pose.Index < Totem.All.Count && Totem.All[pose.Index] != null)
                Totem.All[pose.Index].MoveTo(pose.Position, pose.Yaw);
        }

        // ---------------------------------------------------------------- Ser visto

        protected override bool HoldStill(float now)
        {
            if (now >= _watchAt)
            {
                float dt = _watchedAt > 0f ? now - _watchedAt : WatchInterval;
                _watchAt = now + WatchInterval;
                _watchedAt = now;
                _watched = UpdateWatchers(dt, all: true);

                if (_watched != _frozen.Value)
                {
                    _frozen.Value = _watched;
                    if (_watched) Freezes++;
                }
            }

            TrySwap(now);

            // Velocidade real, medida aqui mesmo (para o teste conferir as arrancadas). O passo
            // medido agora foi dado no quadro anterior, com o tempo daquele quadro.
            float step = _lastStep;
            _lastStep = Time.deltaTime;
            if (step > 0f)
            {
                Vector3 moved = transform.position - _lastPosition;
                moved.y = 0f;
                float speed = moved.magnitude / step;
                // Um salto grande é teletransporte (aparecer, ser recolocado), não corrida.
                if (speed < 12f) TopSpeed = Mathf.Max(TopSpeed, speed);
            }
            _lastPosition = transform.position;

            return _watched;
        }

        // Nas outras máquinas, só o cansaço de encarar do jogador local (para o chuvisco).
        void LateUpdate()
        {
            if (!IsSpawned || IsServer) return;
            if (Time.time < _watchAt) return;

            float now = Time.time;
            float dt = _watchedAt > 0f ? now - _watchedAt : WatchInterval;
            _watchAt = now + WatchInterval;
            _watchedAt = now;
            UpdateWatchers(dt, all: false);
        }

        // Atualiza o cansaço de encarar de cada jogador e diz se algum ainda o vigia.
        bool UpdateWatchers(float dt, bool all)
        {
            bool lit = IsLit();
            bool watched = false;

            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                if (!all && player != PlayerNetwork.Local) continue;

                // De dentro do guarda-roupa não se vigia ninguém (só se vê a fresta).
                bool sees = lit && player.Hiding != BackRooms.Player.HideKind.Wardrobe && InSight(player);
                _stare.TryGetValue(player, out float stare);
                stare = sees ? stare + dt : Mathf.Max(0f, stare - dt * stareRecovery);
                _stare[player] = stare;

                // Quem encarou demais está com a imagem tomada pelo chuvisco: não vale como vigia.
                if (sees && stare < stareBlindAfter) watched = true;
            }

            // Esquece quem saiu da sessão.
            _gone.Clear();
            foreach (PlayerNetwork player in _stare.Keys)
                if (player == null || !player.IsSpawned) _gone.Add(player);
            foreach (PlayerNetwork player in _gone) _stare.Remove(player);

            return watched;
        }

        /// <summary>
        /// Está iluminado agora? Por uma luminária acesa a até <see cref="lampReach"/> (fora do
        /// apagão) ou pelo facho da lanterna de qualquer jogador, sempre sem parede no meio.
        /// </summary>
        public bool IsLit()
        {
            Transform root = LevelRoot();
            if (root == null) return false;

            Vector3 chest = transform.position + Vector3.up * 1.5f;

            LightsOut power = LightsOut.Instance;
            if (power == null || power.Phase != PowerPhase.Dark)
            {
                float reach = lampReach * lampReach;
                foreach (CeilingLight lamp in CeilingLight.All)
                {
                    if (lamp == null || !lamp.IsLit) continue;
                    Vector3 from = lamp.LightPosition + Vector3.down * 0.3f;
                    if ((from - chest).sqrMagnitude > reach) continue;
                    if (!Blocked(from, chest, root)) return true;
                }
            }

            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                Flashlight torch = player.flashlight;
                if (torch == null || !torch.IsOn || torch.Beam == null) continue;

                Light beam = torch.Beam;
                Vector3 from = beam.transform.position;
                foreach (float height in BodyHeights)
                {
                    Vector3 point = transform.position + Vector3.up * height;
                    Vector3 to = point - from;
                    if (to.magnitude > beam.range) continue;
                    if (beam.type == LightType.Spot && Vector3.Angle(beam.transform.forward, to) > beam.spotAngle * 0.5f) continue;
                    if (!Blocked(from, point, root)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Ele está na tela de <paramref name="viewer"/>, sem parede (nem móvel) no meio? Não
        /// confere a luz.
        /// </summary>
        public bool InSight(PlayerNetwork viewer)
        {
            Camera camera = viewer != null ? viewer.playerCamera : null;
            Transform root = LevelRoot();
            if (camera == null || root == null) return false;

            Transform eye = camera.transform;
            float tanV = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.92f;
            float tanH = tanV * Aspect;

            foreach (float height in BodyHeights)
            {
                Vector3 point = transform.position + Vector3.up * height;
                Vector3 local = eye.InverseTransformPoint(point);
                if (local.z < 0.3f || local.z > watchRange) continue;
                if (Mathf.Abs(local.x) > local.z * tanH || Mathf.Abs(local.y) > local.z * tanV) continue;
                if (!Blocked(eye.position, point, root)) return true;
            }

            return false;
        }

        static Transform LevelRoot()
        {
            GameSession session = GameSession.Instance;
            return session != null && session.level != null ? session.level.transform : null;
        }

        // Parede, teto ou móvel no meio (tudo que é do nível). Jogadores e o próprio Capitão não tapam.
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

        // ---------------------------------------------------------------- Totens

        // Host: no começo de cada apagão sorteia se troca; passado swapDelay de escuro, troca com
        // um totem que ninguém está vendo e que está longe de todos.
        void TrySwap(float now)
        {
            LightsOut power = LightsOut.Instance;
            bool dark = power != null && power.Phase == PowerPhase.Dark;
            if (!dark)
            {
                _swapDarkSeen = false;
                _swapAt = -1f;
                return;
            }

            if (!_swapDarkSeen)
            {
                _swapDarkSeen = true;
                bool roll = ForceNextSwap || UnityEngine.Random.value < swapChance;
                _swapAt = roll && Totem.All.Count > 0 ? now + swapDelay : -1f;
            }

            if (_swapAt < 0f || now < _swapAt) return;
            if (_watched || State == EntityState.Feed) return; // tenta de novo no próximo quadro

            Totem chosen = null;
            float best = float.MaxValue;
            foreach (Totem totem in Totem.All)
            {
                if (totem == null) continue;
                Vector3 at = totem.transform.position;
                float nearest = float.MaxValue;
                bool seen = false;
                foreach (PlayerNetwork player in PlayerNetwork.All)
                {
                    if (player == null || !player.IsSpawned || !player.InGame) continue;
                    nearest = Mathf.Min(nearest, Vector3.Distance(player.transform.position, at));
                    if (PointInSight(player, at)) seen = true;
                }
                if (seen || nearest < swapMinDistance) continue;
                // O mais perto de algum jogador (sem passar do limite): a troca assusta onde há gente.
                if (nearest < best)
                {
                    best = nearest;
                    chosen = totem;
                }
            }

            _swapAt = -1f;
            if (chosen == null) return;

            // O totem vai para o meio da célula em que ele estava (nunca fecha passagem); ele, para
            // o canto do totem.
            Vector3 from = transform.position;
            Vector3 to = chosen.transform.position;
            Vector3 cellCenter = CellCenterOf(from);
            float yaw = Quaternion.LookRotation(Flat(to - from).sqrMagnitude > 0.01f ? Flat(to - from) : Vector3.forward).eulerAngles.y;

            var pose = new TotemPose { Index = (ushort)chosen.Index, Position = cellCenter, Yaw = yaw };
            ApplyTotem(pose);
            bool replaced = false;
            for (int i = 0; i < _totems.Count; i++)
            {
                if (_totems[i].Index != pose.Index) continue;
                _totems[i] = pose;
                replaced = true;
            }
            if (!replaced) _totems.Add(pose);

            Quaternion facing = chosen.transform.rotation;
            transform.SetPositionAndRotation(new Vector3(to.x, from.y, to.z), facing);
            if (TryGetComponent(out Unity.Netcode.Components.NetworkTransform moved))
                moved.Teleport(transform.position, transform.rotation, transform.localScale);
            _lastPosition = transform.position;
            ForgetPath();
            Swaps++;
        }

        static Vector3 CellCenterOf(Vector3 world)
        {
            GameSession session = GameSession.Instance;
            LevelBuilder level = session != null ? session.level : null;
            if (level == null) return world;
            Vector3 local = level.transform.InverseTransformPoint(world);
            int x = Mathf.FloorToInt(local.x / LevelBuilder.Cell), z = Mathf.FloorToInt(local.z / LevelBuilder.Cell);
            Vector3 center = level.CellCenter(x, z);
            return new Vector3(center.x, world.y, center.z);
        }

        // O ponto (pé de um totem) aparece na tela do jogador, sem parede no meio, a até 25 m?
        static bool PointInSight(PlayerNetwork viewer, Vector3 foot)
        {
            Camera camera = viewer.playerCamera;
            Transform root = LevelRoot();
            if (camera == null || root == null) return false;

            Transform eye = camera.transform;
            float tanV = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * Aspect;
            foreach (float height in BodyHeights)
            {
                Vector3 point = foot + Vector3.up * height;
                Vector3 local = eye.InverseTransformPoint(point);
                if (local.z < 0.3f || local.z > 25f) continue;
                if (Mathf.Abs(local.x) > local.z * tanH || Mathf.Abs(local.y) > local.z * tanV) continue;
                if (!BlockedExcept(eye.position, point, root, foot)) return true;
            }
            return false;
        }

        // Como Blocked, mas sem contar o próprio totem (o raio termina dentro da caixa dele).
        static bool BlockedExcept(Vector3 from, Vector3 to, Transform levelRoot, Vector3 foot)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return false;

            int count = Physics.RaycastNonAlloc(from, delta / distance, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = Hits[i].collider.transform;
                if (!hit.IsChildOf(levelRoot)) continue;
                if (hit.GetComponent<Totem>() != null && Flat(hit.position - foot).sqrMagnitude < 0.25f) continue;
                return true;
            }
            return false;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // ---------------------------------------------------------------- Andar

        protected override float SpeedNow()
        {
            float speed = base.SpeedNow();

            LightsOut power = LightsOut.Instance;
            bool dark = power != null && power.Phase == PowerPhase.Dark;
            if (dark && !_wasDark) _darkSince = Time.time;
            _wasDark = dark;

            // No escuro, caçando: arrancadas, com pausas mancando entre elas.
            bool hunting = State == EntityState.Chase || State == EntityState.Investigate;
            if (!dark || !hunting || speed <= 0f) return speed;

            float cycle = Mathf.Max(0.1f, dashSeconds + dashRestSeconds);
            float into = (Time.time - _darkSince) % cycle;
            return into < dashSeconds ? darkDashSpeed : speed;
        }
    }
}
