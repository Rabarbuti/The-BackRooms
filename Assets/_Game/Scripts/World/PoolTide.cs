using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.Player;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// A água das Piscinas que se move (level2-piscinas.md, seções 4.3, 5 e 6), igual em todas as
    /// máquinas e sem nada pela rede além do que já vai:
    ///
    /// - <b>A maré</b>: pelo relógio da sessão (<see cref="GameSession.SessionSeconds"/>), um ciclo de
    ///   <see cref="Cycle"/> s: parada, depois <see cref="RiseSeconds"/> s subindo até
    ///   <see cref="Amplitude"/> m e <see cref="FallSeconds"/> s descendo (a vazante). A primeira subida
    ///   começa aos <see cref="FirstRise"/> s. 10 s antes, um "glub" grave e longe e os poços borbulham.
    /// - <b>O ralo</b>: na vazante a água sai pelos ralos dos poços. Quem nada num poço, ou na água funda
    ///   vizinha, é puxado para a grade, mais forte quanto mais perto; chegar à grade é afogar. As
    ///   lâmpadas de dentro d'água piscam enquanto puxa. Cada máquina puxa o próprio jogador (o
    ///   movimento é do dono), e todas fazem a mesma conta.
    /// - <b>A drenagem</b>: cumprido o objetivo das válvulas (<see cref="SwitchObjective"/>, tipo
    ///   <see cref="SwitchKind.Valve"/>), a água do mapa desce <see cref="DrainDepth"/> m em
    ///   <see cref="DrainSeconds"/> s a partir da hora em que o host o cumpriu, e a maré para.
    ///
    /// Tudo vira uma altura só, aplicada em <see cref="PoolWater.Offset"/>: o jogador, os passos, a
    /// filmadora e a caneta já leem <see cref="PoolWater.SurfaceY"/>.
    /// </summary>
    public sealed class PoolTide : MonoBehaviour
    {
        /// <summary>Para os testes que dependem da água parada (fuga da funda, passos, Play): sem maré.</summary>
        public static bool Paused;

        /// <summary>Para os testes: força a hora da maré (segundos de sessão); negativo = o relógio.</summary>
        public static double ForceSeconds = -1.0;

        public const float Cycle = 90f;
        public const float FirstRise = 90f;
        public const float RiseSeconds = 15f;
        public const float FallSeconds = 15f;
        public const float WarningSeconds = 10f;
        public const float Amplitude = 0.9f;
        public const float DrainDepth = 2f;
        public const float DrainSeconds = 30f;

        [Tooltip("Velocidade máxima com que o ralo puxa para baixo, em m/s, junto da grade (quem segura Espaço sobe a 1,6: escapa devagar).")]
        public float pullSpeed = 1.5f;
        [Tooltip("Alcance do ralo além do poço, em metros (a água funda vizinha).")]
        public float pullReach = 6f;

        /// <summary>O ralo está puxando agora (vazante, com água).</summary>
        public bool Pulling { get; private set; }

        /// <summary>Quanto o ralo puxa o jogador desta máquina agora, de 0 a 1 (testes e painel).</summary>
        public float LocalPull { get; private set; }

        /// <summary>Altura da água acima da de repouso agora, em metros (negativa drenando).</summary>
        public float Offset { get; private set; }

        public static PoolTide Current { get; private set; }

        PoolWater _water;
        LevelBuilder _level;
        readonly List<Vector3> _grates = new List<Vector3>();
        readonly List<Light> _lamps = new List<Light>();
        readonly List<float> _lampIntensity = new List<float>();
        AudioSource _ambient;
        AudioClip _glub, _gurgle;
        int _warnedCycle = -1;
        bool _drainAnnounced;

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        // Monta no primeiro quadro, com a fase inteira pronta (as lâmpadas nascem depois da água).
        void Start()
        {
            PoolWater water = GetComponent<PoolWater>();
            LevelBuilder level = GetComponentInParent<LevelBuilder>();
            if (water != null && level != null && level.Data != null && level.Data.HasPools) Init(level, water);
        }

        /// <summary>Monta a partir da planta: um ralo no fundo de cada célula de poço.</summary>
        void Init(LevelBuilder level, PoolWater water)
        {
            _level = level;
            _water = water;
            LevelData data = level.Data;

            // Uma grade por célula de poço (o redondo e o de célula inteira têm o ralo no meio).
            for (int z = 0; z < data.Height; z++)
                for (int x = 0; x < data.Width; x++)
                    if (!data.IsSolid(x, z) && data.Depth[x, z] == PoolDepth.Well)
                        _grates.Add(level.CellCenter(x, z));

            // As lâmpadas de dentro d'água perto de um ralo: piscam enquanto ele puxa.
            foreach (Light light in level.GetComponentsInChildren<Light>(true))
            {
                if (!light.gameObject.name.StartsWith("Lampada")) continue;
                bool near = false;
                foreach (Vector3 grate in _grates)
                    if (new Vector2(grate.x - light.transform.position.x, grate.z - light.transform.position.z).magnitude < 10f) near = true;
                if (!near) continue;
                _lamps.Add(light);
                _lampIntensity.Add(light.intensity);
            }

            _ambient = gameObject.AddComponent<AudioSource>();
            _ambient.spatialBlend = 0f;
            _ambient.playOnAwake = false;
            _ambient.volume = 0.6f;
            BuildClips();
        }

        void Update()
        {
            if (_water == null || _level == null) return;
            double now = Seconds();

            float tide = Paused ? 0f : TideAt(now);
            float drain = DrainAt(now);
            Offset = tide * (1f - drain) - DrainDepth * drain;
            _water.Offset = Offset;
            _water.Drained01 = drain;

            // A vazante: a água sai pelos ralos (parados com o mapa drenado).
            Pulling = !Paused && drain <= 0f && Falling(now);
            UpdateLamps();
            UpdateSounds(now, drain);
            PullLocal();
        }

        // ---------------------------------------------------------------- Contas (iguais em toda máquina)

        static double Seconds()
        {
            if (ForceSeconds >= 0.0) return ForceSeconds;
            GameSession session = GameSession.Instance;
            double s = session != null ? session.SessionSeconds : -1.0;
            return s;
        }

        /// <summary>Altura da maré aos <paramref name="seconds"/> de sessão, em metros (0 parada).</summary>
        public static float TideAt(double seconds)
        {
            if (seconds < FirstRise) return 0f;
            float t = (float)((seconds - FirstRise) % Cycle);
            if (t < RiseSeconds) return Amplitude * Smooth(t / RiseSeconds);
            if (t < RiseSeconds + FallSeconds) return Amplitude * Smooth(1f - (t - RiseSeconds) / FallSeconds);
            return 0f;
        }

        /// <summary>Verdadeiro na vazante (a maré descendo).</summary>
        public static bool Falling(double seconds)
        {
            if (seconds < FirstRise) return false;
            float t = (float)((seconds - FirstRise) % Cycle);
            return t >= RiseSeconds && t < RiseSeconds + FallSeconds;
        }

        /// <summary>Segundos até a próxima subida (para o aviso), ou negativo subindo/descendo.</summary>
        public static float UntilRise(double seconds)
        {
            if (seconds < FirstRise) return (float)(FirstRise - seconds);
            float t = (float)((seconds - FirstRise) % Cycle);
            return t < RiseSeconds + FallSeconds ? -1f : Cycle - t;
        }

        /// <summary>Quanto já drenou, de 0 a 1, pela hora em que o objetivo das válvulas se cumpriu.</summary>
        public static float DrainAt(double seconds)
        {
            SwitchObjective objective = SwitchObjective.Instance;
            if (objective == null || objective.Kind != SwitchKind.Valve || !objective.Complete || objective.CompletedAt < 0.0) return 0f;
            if (seconds < 0.0) return 1f;
            return Smooth((float)((seconds - objective.CompletedAt) / DrainSeconds));
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // ---------------------------------------------------------------- O ralo

        // O jogador desta máquina, nadando perto de um ralo na vazante: puxado para a grade.
        void PullLocal()
        {
            LocalPull = 0f;
            PlayerNetwork local = PlayerNetwork.Local;
            if (local == null || !local.InGame) return;
            PlayerController body = local.controller;
            body.WaterCurrent = Vector3.zero;
            if (!Pulling || body.IsReturning || !body.IsSwimming || _grates.Count == 0) return;

            Vector3 feet = body.transform.position;
            Vector3 best = Vector3.zero;
            float bestDistance = float.MaxValue;
            foreach (Vector3 grate in _grates)
            {
                float flat = new Vector2(grate.x - feet.x, grate.z - feet.z).magnitude;
                if (flat < bestDistance) { bestDistance = flat; best = grate; }
            }

            float reach = LevelBuilder.Cell * 0.5f + pullReach;
            if (bestDistance > reach) return;

            // Mais forte perto: puxa para o meio do poço e para baixo.
            float strength = Mathf.Clamp01(1f - bestDistance / reach);
            strength = 0.35f + 0.65f * strength;
            Vector3 toward = new Vector3(best.x - feet.x, 0f, best.z - feet.z);
            if (toward.sqrMagnitude > 0.01f) toward.Normalize();
            body.WaterCurrent = toward * (pullSpeed * 0.6f * strength) + Vector3.down * (pullSpeed * strength);
            LocalPull = strength;

            // Na grade: afogado (a mesma volta ao início da queda no buraco).
            if (bestDistance < 1.4f && feet.y < best.y + 0.9f) body.Drown();
        }

        void UpdateLamps()
        {
            for (int i = 0; i < _lamps.Count; i++)
            {
                if (_lamps[i] == null) continue;
                float flicker = Pulling ? (Mathf.PerlinNoise(Time.time * 9f, i * 1.7f) > 0.45f ? 1f : 0.08f) : 1f;
                _lamps[i].intensity = _lampIntensity[i] * flicker;
            }
        }

        // ---------------------------------------------------------------- Som

        void UpdateSounds(double now, float drain)
        {
            if (_ambient == null || now < 0.0) return;

            // Aviso: um glub grave e longe, 10 s antes de a maré subir.
            float until = UntilRise(now);
            int cycle = now < FirstRise ? 0 : 1 + (int)((now - FirstRise) / Cycle);
            if (!Paused && drain <= 0f && until > 0f && until <= WarningSeconds && _warnedCycle != cycle)
            {
                _warnedCycle = cycle;
                _ambient.PlayOneShot(_glub, 0.9f);
            }

            // A vazante e a drenagem: gorgolejo constante dos ralos.
            bool gurgling = Pulling || drain > 0f && drain < 1f;
            if (gurgling && !_ambient.isPlaying)
            {
                _ambient.clip = _gurgle;
                _ambient.loop = true;
                _ambient.Play();
            }
            else if (!gurgling && _ambient.isPlaying) _ambient.Stop();
            _ambient.volume = drain > 0f ? 0.7f : 0.35f + 0.4f * LocalPull;

            if (drain > 0f && !_drainAnnounced)
            {
                _drainAnnounced = true;
                _ambient.PlayOneShot(_glub, 1f);
            }
        }

        void BuildClips()
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            var rng = new System.Random(3131);

            int n = (int)(rate * 2.2f);
            var glub = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                // Três bolhas graves descendo de tom, abafadas, com um ronco embaixo.
                float v = 0f;
                for (int b = 0; b < 3; b++)
                {
                    float start = b * 0.38f;
                    if (t < start) continue;
                    float u = t - start;
                    v += Mathf.Sin(2f * Mathf.PI * (95f - 30f * u) * u) * Mathf.Exp(-u * 5f) * (1f - b * 0.25f);
                }
                v += Mathf.Sin(2f * Mathf.PI * 38f * t) * 0.35f * Mathf.Clamp01(t * 2f) * Mathf.Clamp01((2.2f - t) * 1.5f);
                glub[i] = v * 0.6f;
            }
            _glub = AudioClip.Create("Mare_Glub", n, 1, rate, false);
            _glub.SetData(glub, 0);

            int g = rate * 3;
            var gurgle = new float[g];
            float low = 0f;
            for (int i = 0; i < g; i++)
            {
                float t = i / (float)rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.04f;
                float bubble = rng.NextDouble() < 0.0009 ? 1f : 0f;
                gurgle[i] = low * 1.6f + Mathf.Sin(2f * Mathf.PI * 160f * t) * bubble;
            }
            // Laço sem estalo: as pontas se cruzam.
            int fade = rate / 10;
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                gurgle[i] = gurgle[i] * w + gurgle[g - fade + i] * (1f - w);
            }
            _gurgle = AudioClip.Create("Ralo_Gorgolejo", g - fade, 1, rate, false);
            var trimmed = new float[g - fade];
            System.Array.Copy(gurgle, trimmed, g - fade);
            _gurgle.SetData(trimmed, 0);
        }
    }
}
