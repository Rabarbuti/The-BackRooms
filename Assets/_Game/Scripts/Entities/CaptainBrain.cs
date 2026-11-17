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
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Captain == this) Captain = null;
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

                bool sees = lit && InSight(player);
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
