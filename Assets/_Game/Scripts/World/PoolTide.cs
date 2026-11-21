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
    ///   <see cref="Amplitude"/> m, <see cref="HighSeconds"/> s de maré alta e <see cref="FallSeconds"/> s
    ///   descendo (a vazante). A primeira subida começa aos <see cref="FirstRise"/> s.
    /// - <b>O alarme</b>: <see cref="WarningSeconds"/> s antes e durante a subida, a sirene (e, no começo,
    ///   a buzina de piscina de ondas e um glub) toca na fase inteira e as lâmpadas vermelhas
    ///   (<see cref="PoolAlarm"/>) piscam; param quando a água para de subir e, drenada a fase, nunca
    ///   mais. Enquanto a água anda, o ronco de água correndo e a superfície agitada
    ///   (<see cref="PoolWater.Agitation"/>); o painel da filmadora mostra a fase (<see cref="HudText"/>);
    ///   nas paredes fica a marca molhada (<see cref="PoolWater.WetTop"/>).
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

        // O ciclo (medido jogando, 20/11/2026: zerava-se a fase antes da primeira maré, que vinha aos
        // 90 s): a primeira subida aos 30 s (o aviso aos 20, ainda perto do início), 20 s subindo,
        // 15 s de maré alta parada, 20 s descendo (a vazante, com o ralo) e 35 s de água baixa.
        public const float Cycle = 90f;
        public const float FirstRise = 30f;
        public const float RiseSeconds = 20f;
        public const float HighSeconds = 15f;
        public const float FallSeconds = 20f;
        public const float WarningSeconds = 10f;
        // 1,3 m (era 1,0 até 21/11/2026: de pé no raso os olhos ficavam 20 cm acima da água e
        // andava-se pelo fundo; a maré não fechava nada). Agora, na maré alta, o raso fica com 1,75 m
        // de água: a cabeça de quem está de pé fica debaixo d'água, então ou se nada à tona (território
        // do Undertow, UndertowNav.EnterColumn) ou se espera no seco, que fica com 1,15 m: vadeia-se
        // devagar, sem nadar, e nunca é território dele. Acima de ~1,45 m o seco também viraria nado e
        // deixaria de haver refúgio.
        public const float Amplitude = 1.3f;
        public const float DrainDepth = 2f;
        public const float DrainSeconds = 30f;

        /// <summary>Hora (segundos de sessão) em que a primeira maré chega ao alto.</summary>
        public const float FirstPeak = FirstRise + RiseSeconds;
        /// <summary>Hora em que começa a primeira vazante.</summary>
        public const float FirstEbb = FirstRise + RiseSeconds + HighSeconds;

        // Textos do painel da filmadora (a língua do jogo pode mudar: tudo aqui).
        public const string TextComing = "TIDE COMING";
        public const string TextRising = "TIDE RISING";
        public const string TextHigh = "HIGH TIDE";
        public const string TextFalling = "TIDE FALLING";
        public const string TextPull = "DRAIN PULLING";
        public const string TextDraining = "DRAINING";

        /// <summary>
        /// O aviso da maré para o painel da filmadora agora, ou nulo (água baixa, maré parada, sem
        /// Piscinas). <see cref="HudBlink"/> diz se pisca.
        /// </summary>
        public static string HudText { get; private set; }
        public static bool HudBlink { get; private set; }

        [Tooltip("Velocidade máxima com que o ralo puxa para baixo, em m/s, junto da grade (quem segura Espaço sobe a 1,6: escapa devagar).")]
        public float pullSpeed = 1.5f;
        [Tooltip("Alcance do ralo além do poço, em metros (a água funda vizinha). Era 6 até 20/11/2026: só 12 de 48 células fundas da seed 1998 ficavam ao alcance.")]
        public float pullReach = 10f;

        /// <summary>O ralo está puxando agora (vazante, com água).</summary>
        public bool Pulling { get; private set; }

        /// <summary>Quanto o ralo puxa o jogador desta máquina agora, de 0 a 1 (testes e painel).</summary>
        public float LocalPull { get; private set; }

        /// <summary>Altura da água acima da de repouso agora, em metros (negativa drenando).</summary>
        public float Offset { get; private set; }

        /// <summary>A fase da maré agora.</summary>
        public TidePhase Phase { get; private set; }

        /// <summary>Quantos avisos sonoros (buzina antes da subida, glub da vazante) já tocaram (testes).</summary>
        public int WarningsPlayed { get; private set; }

        /// <summary>O alarme (sirene e lâmpadas vermelhas) está ligado agora.</summary>
        public bool AlarmOn { get; private set; }

        /// <summary>Volume da sirene agora (testes).</summary>
        public float SirenVolume => _siren != null ? _siren.volume : 0f;

        /// <summary>Volume do ronco de água correndo agora (testes).</summary>
        public float FlowVolume => _flow != null ? _flow.volume : 0f;

        public static PoolTide Current { get; private set; }

        PoolWater _water;
        LevelBuilder _level;
        readonly List<Vector3> _grates = new List<Vector3>();
        readonly List<Light> _lamps = new List<Light>();
        readonly List<float> _lampIntensity = new List<float>();
        AudioSource _ambient, _flow, _siren, _alert;
        AudioClip _glub, _gurgle, _horn, _rush;
        PoolAlarm _alarm;
        int _warnedCycle = -1, _ebbCycle = -1;
        bool _drainAnnounced;

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this)
            {
                Current = null;
                HudText = null;
            }
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

            // Sons sem lugar (2D): ouvem-se em qualquer ponto da fase, como o anoitecer.
            _ambient = gameObject.AddComponent<AudioSource>();
            _ambient.spatialBlend = 0f;
            _ambient.playOnAwake = false;
            _ambient.volume = 0.6f;
            // Os avisos (buzina, glub) numa fonte só deles, em volume cheio: o volume do gorgolejo,
            // que muda a cada quadro, multiplicava também o one-shot e a buzina saía a ~1/3.
            _alert = gameObject.AddComponent<AudioSource>();
            _alert.spatialBlend = 0f;
            _alert.playOnAwake = false;
            _alert.volume = 1f;
            _flow = gameObject.AddComponent<AudioSource>();
            _flow.spatialBlend = 0f;
            _flow.playOnAwake = false;
            _flow.loop = true;
            _flow.volume = 0f;
            _glub = CreateGlub();
            _gurgle = CreateGurgle();
            _horn = CreateHorn();
            _rush = CreateRush();
            _flow.clip = _rush;
            _siren = gameObject.AddComponent<AudioSource>();
            _siren.spatialBlend = 0f;
            _siren.playOnAwake = false;
            _siren.loop = true;
            _siren.volume = 0f;
            _siren.clip = CreateSiren();

            _alarm = level.GetComponentInChildren<PoolAlarm>(true);
        }

        void Update()
        {
            if (_water == null || _level == null) return;
            double now = Seconds();

            // Drenando, a maré congela na altura da hora em que as válvulas se abriram e a água desce
            // dali (senão, abertas no meio de uma subida, a subida cancelava a descida e parecia que
            // nada tinha acontecido).
            float drain = DrainAt(now);
            double drainStart = DrainStart();
            float tide = Paused ? 0f : drainStart >= 0.0 && now >= drainStart ? TideAt(drainStart) : TideAt(now);
            Offset = tide * (1f - drain) - DrainDepth * drain;
            _water.Offset = Offset;
            _water.Drained01 = drain;
            Phase = Paused || now < 0.0 ? TidePhase.Low : drain > 0f ? TidePhase.Draining : PhaseAt(now);

            // O alarme: do aviso até a água parar de subir; drenada a fase, nunca mais.
            AlarmOn = Phase == TidePhase.Warning || Phase == TidePhase.Rising;
            if (_alarm != null) _alarm.Set(AlarmOn, now);

            // A superfície se agita enquanto a água anda (mais no meio da subida, que é mais rápida).
            float rate = Paused ? 0f : Mathf.Abs(RateAt(now)) * (1f - drain);
            if (drain > 0f && drain < 1f) rate = Mathf.Max(rate, DrainDepth / DrainSeconds);
            _water.Agitation = Mathf.Clamp01(rate / MaxRate);

            // A vazante: a água sai pelos ralos (parados com o mapa drenado).
            Pulling = !Paused && drain <= 0f && Falling(now);
            UpdateLamps();
            UpdateSounds(now, drain, rate);
            PullLocal();
            UpdateHud();
        }

        // Velocidade máxima da maré (meio da subida), em m/s: a smoothstep chega a 1,5 vezes a média.
        static float MaxRate => Amplitude * 1.5f / Mathf.Min(RiseSeconds, FallSeconds);

        // ---------------------------------------------------------------- Contas (iguais em toda máquina)

        static double Seconds()
        {
            if (ForceSeconds >= 0.0) return ForceSeconds;
            GameSession session = GameSession.Instance;
            double s = session != null ? session.SessionSeconds : -1.0;
            return s;
        }

        // Segundos dentro do ciclo atual (0 = começo da subida); negativo antes da primeira.
        static float CycleTime(double seconds) => seconds < FirstRise ? -1f : (float)((seconds - FirstRise) % Cycle);

        /// <summary>Altura da maré aos <paramref name="seconds"/> de sessão, em metros (0 parada).</summary>
        public static float TideAt(double seconds)
        {
            float t = CycleTime(seconds);
            if (t < 0f) return 0f;
            if (t < RiseSeconds) return Amplitude * Smooth(t / RiseSeconds);
            t -= RiseSeconds;
            if (t < HighSeconds) return Amplitude;
            t -= HighSeconds;
            if (t < FallSeconds) return Amplitude * Smooth(1f - t / FallSeconds);
            return 0f;
        }

        /// <summary>Quanto a maré sobe (positivo) ou desce (negativo) por segundo aos <paramref name="seconds"/>, em m/s.</summary>
        public static float RateAt(double seconds)
        {
            float t = CycleTime(seconds);
            if (t < 0f) return 0f;
            if (t < RiseSeconds) return Amplitude * SmoothSlope(t / RiseSeconds) / RiseSeconds;
            t -= RiseSeconds + HighSeconds;
            if (t >= 0f && t < FallSeconds) return -Amplitude * SmoothSlope(t / FallSeconds) / FallSeconds;
            return 0f;
        }

        /// <summary>Verdadeiro na vazante (a maré descendo).</summary>
        public static bool Falling(double seconds)
        {
            float t = CycleTime(seconds);
            return t >= RiseSeconds + HighSeconds && t < RiseSeconds + HighSeconds + FallSeconds;
        }

        /// <summary>Segundos até a próxima subida (para o aviso), ou negativo subindo, alta ou descendo.</summary>
        public static float UntilRise(double seconds)
        {
            if (seconds < FirstRise) return (float)(FirstRise - seconds);
            float t = CycleTime(seconds);
            return t < RiseSeconds + HighSeconds + FallSeconds ? -1f : Cycle - t;
        }

        /// <summary>A fase da maré aos <paramref name="seconds"/> de sessão (sem contar a drenagem).</summary>
        public static TidePhase PhaseAt(double seconds)
        {
            if (seconds < 0.0) return TidePhase.Low;
            float until = UntilRise(seconds);
            if (until > 0f) return until <= WarningSeconds ? TidePhase.Warning : TidePhase.Low;
            float t = CycleTime(seconds);
            if (t < RiseSeconds) return TidePhase.Rising;
            if (t < RiseSeconds + HighSeconds) return TidePhase.High;
            return TidePhase.Falling;
        }

        /// <summary>Quanto já drenou, de 0 a 1, pela hora em que o objetivo das válvulas se cumpriu.</summary>
        /// <remarks>
        /// A curva já sai descendo (rápida no começo, devagar no fim): aberta a segunda válvula, a
        /// água desce ~0,5 m nos primeiros 3 s. Até 20/11/2026 era uma rampa suave nas duas pontas
        /// e nos primeiros segundos quase nada se via.
        /// </remarks>
        public static float DrainAt(double seconds)
        {
            double start = DrainStart();
            if (start < 0.0) return 0f;
            if (seconds < 0.0) return 1f;
            float t = Mathf.Clamp01((float)((seconds - start) / DrainSeconds));
            return 1f - (1f - t) * (1f - t);
        }

        /// <summary>Para os testes: força a hora (segundos de sessão) em que a drenagem começou; negativo = a do objetivo.</summary>
        public static double ForceDrainStart = -1.0;

        /// <summary>Hora (segundos de sessão) em que as válvulas se abriram, ou negativo se ainda não.</summary>
        public static double DrainStart()
        {
            SwitchObjective objective = SwitchObjective.Instance;
            if (objective == null || objective.Kind != SwitchKind.Valve || !objective.Complete || objective.CompletedAt < 0.0) return -1.0;
            return ForceDrainStart >= 0.0 ? ForceDrainStart : objective.CompletedAt;
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // Derivada da Smooth (por unidade de t).
        static float SmoothSlope(float t)
        {
            t = Mathf.Clamp01(t);
            return 6f * t * (1f - t);
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

        void UpdateSounds(double now, float drain, float rate)
        {
            if (_ambient == null || now < 0.0) return;

            // Aviso: a buzina de piscina de ondas, longe, e o glub, 10 s antes de a maré subir.
            float until = UntilRise(now);
            int cycle = now < FirstRise ? 0 : 1 + (int)((now - FirstRise) / Cycle);
            if (!Paused && drain <= 0f && until > 0f && until <= WarningSeconds && _warnedCycle != cycle)
            {
                _warnedCycle = cycle;
                WarningsPlayed++;
                _alert.PlayOneShot(_horn, 1f);
                _alert.PlayOneShot(_glub, 0.8f);
            }

            // Começo da vazante: os ralos abrem com um glub (e depois gorgolejam).
            if (Pulling && _ebbCycle != cycle)
            {
                _ebbCycle = cycle;
                WarningsPlayed++;
                _alert.PlayOneShot(_glub, 1f);
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

            // Enquanto a água anda: o ronco de água correndo, mais forte quanto mais rápida, um
            // pouco mais agudo subindo e mais grave descendo.
            float target = rate > 0.002f ? 0.25f + 0.55f * Mathf.Clamp01(rate / MaxRate) : 0f;
            _flow.volume = Mathf.MoveTowards(_flow.volume, target, Time.deltaTime * 0.5f);
            _flow.pitch = Phase == TidePhase.Rising ? 1.06f : 0.9f;
            if (_flow.volume > 0.001f && !_flow.isPlaying) _flow.Play();
            else if (_flow.volume <= 0.001f && _flow.isPlaying) _flow.Stop();

            // A sirene: entra de uma vez e some em 1,5 s quando a água para de subir.
            if (AlarmOn) _siren.volume = SirenLoudness;
            else _siren.volume = Mathf.MoveTowards(_siren.volume, 0f, Time.deltaTime * SirenLoudness / 1.5f);
            if (_siren.volume > 0.001f && !_siren.isPlaying) _siren.Play();
            else if (_siren.volume <= 0.001f && _siren.isPlaying) _siren.Stop();

            if (drain > 0f && !_drainAnnounced)
            {
                _drainAnnounced = true;
                _alert.PlayOneShot(_glub, 1f);
            }
        }

        // ---------------------------------------------------------------- Painel

        // O que a filmadora mostra: a maré chegando, subindo, alta, descendo (o ralo puxando, para
        // quem está sendo puxado) e a drenagem.
        void UpdateHud()
        {
            string text = null;
            bool blink = true;
            switch (Phase)
            {
                case TidePhase.Warning: text = TextComing; break;
                case TidePhase.Rising: text = TextRising; break;
                case TidePhase.High: text = TextHigh; blink = false; break;
                case TidePhase.Falling: text = LocalPull > 0f ? TextPull : TextFalling; break;
                case TidePhase.Draining: text = _water.Drained01 < 1f ? TextDraining : null; blink = false; break;
            }
            HudText = text;
            HudBlink = blink;
        }

        // ---------------------------------------------------------------- Sons sintetizados (também exportados em WAV)

        static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        // Volume da sirene (2D): alta, mas abaixo dos sustos da entidade.
        const float SirenLoudness = 0.5f;

        /// <summary>
        /// A sirene de alarme (laço de 4,8 s): o lamento industrial que sobe e desce de 480 a 960 Hz
        /// a cada 2,4 s, onda quase quadrada (harmônicos ímpares) com uma segunda sirene um pouco
        /// desafinada atrás, eco de salão de azulejo e o agudo abafado pela distância.
        /// </summary>
        public static AudioClip CreateSiren()
        {
            int rate = Rate;
            const float period = 2.4f;
            int fade = rate / 10;
            int n = (int)(rate * period * 2f) + fade;
            var dry = new float[n];
            float phaseA = 0f, phaseB = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                // Triângulo suavizado: sobe e desce sem parar no alto nem embaixo.
                float u = (t / period) % 1f;
                float sweep = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * u);
                float f = Mathf.Lerp(480f, 960f, sweep);
                phaseA += 2f * Mathf.PI * f / rate;
                phaseB += 2f * Mathf.PI * f * 1.03f / rate;
                float v = 0f;
                for (int k = 1; k <= 7; k += 2) v += (Mathf.Sin(phaseA * k) + 0.5f * Mathf.Sin(phaseB * k)) / k;
                dry[i] = v;
            }

            var wet = new float[n];
            int d1 = (int)(rate * 0.23f), d2 = (int)(rate * 0.53f);
            float low = 0f, peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float v = dry[i] * 0.7f;
                if (i >= d1) v += wet[i - d1] * 0.35f;
                if (i >= d2) v += wet[i - d2] * 0.2f;
                wet[i] = v;
                low += (v - low) * 0.3f; // ~2,5 kHz
                dry[i] = low;
                peak = Mathf.Max(peak, Mathf.Abs(low));
            }
            float gain = peak > 0f ? 0.75f / peak : 1f;
            for (int i = 0; i < n; i++) dry[i] *= gain;
            return Looped("Mare_Sirene", dry, rate);
        }

        /// <summary>Três bolhas graves descendo de tom, abafadas, com um ronco embaixo (2,2 s).</summary>
        public static AudioClip CreateGlub()
        {
            int rate = Rate;
            int n = (int)(rate * 2.2f);
            var glub = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float v = 0f;
                for (int b = 0; b < 3; b++)
                {
                    float start = b * 0.38f;
                    if (t < start) continue;
                    float u = t - start;
                    // A bolha tem um "bloop" mais agudo por cima: o grave sozinho some em alto-falante pequeno.
                    v += Mathf.Sin(2f * Mathf.PI * (95f - 30f * u) * u) * Mathf.Exp(-u * 5f) * (1f - b * 0.25f);
                    v += 0.45f * Mathf.Sin(2f * Mathf.PI * (420f - 260f * u) * u) * Mathf.Exp(-u * 9f) * (1f - b * 0.25f);
                }
                v += Mathf.Sin(2f * Mathf.PI * 38f * t) * 0.35f * Mathf.Clamp01(t * 2f) * Mathf.Clamp01((2.2f - t) * 1.5f);
                glub[i] = v * 0.55f;
            }
            AudioClip clip = AudioClip.Create("Mare_Glub", n, 1, rate, false);
            clip.SetData(glub, 0);
            return clip;
        }

        /// <summary>
        /// A buzina de piscina de ondas, longe e ecoando no azulejo: dois toques de 1,4 s de duas notas
        /// ásperas (dente de serra) que desafinam juntas, com eco e o agudo abafado pela distância (5 s).
        /// Fica na faixa média (150 Hz a 2 kHz): ouve-se em fone e em alto-falante de notebook.
        /// </summary>
        public static AudioClip CreateHorn()
        {
            int rate = Rate;
            int n = (int)(rate * 5.2f);
            var dry = new float[n];
            float phaseA = 0f, phaseB = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = 0f;
                foreach (float start in new[] { 0f, 1.8f })
                {
                    float u = t - start;
                    if (u < 0f || u > 1.65f) continue;
                    env = Mathf.Max(env, Mathf.Clamp01(u / 0.07f) * Mathf.Clamp01((1.65f - u) / 0.25f));
                }
                if (env <= 0f) { dry[i] = 0f; continue; }
                // O fim de cada toque cai um pouco de tom, como buzina de ar perdendo pressão.
                float u2 = t < 1.8f ? t : t - 1.8f;
                float droop = 1f - 0.035f * Mathf.Clamp01((u2 - 1.3f) / 0.35f);
                phaseA += 2f * Mathf.PI * 156f * droop / rate;
                phaseB += 2f * Mathf.PI * 186f * droop / rate;
                float v = 0f;
                for (int k = 1; k <= 12; k++) v += (Mathf.Sin(phaseA * k) + 0.8f * Mathf.Sin(phaseB * k)) / k;
                dry[i] = v * env;
            }

            // Eco de salão de azulejo e distância: duas reflexões e um passa-baixa.
            var wet = new float[n];
            int d1 = (int)(rate * 0.29f), d2 = (int)(rate * 0.67f);
            float low = 0f, peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float v = dry[i] * 0.7f;
                if (i >= d1) v += wet[i - d1] * 0.42f;
                if (i >= d2) v += wet[i - d2] * 0.22f;
                wet[i] = v;
                low += (v - low) * 0.22f; // ~1,7 kHz a 44,1 kHz
                peak = Mathf.Max(peak, Mathf.Abs(low));
                dry[i] = low;
            }
            float gain = peak > 0f ? 0.8f / peak : 1f;
            for (int i = 0; i < n; i++) dry[i] *= gain;
            AudioClip clip = AudioClip.Create("Mare_Buzina", n, 1, rate, false);
            clip.SetData(dry, 0);
            return clip;
        }

        /// <summary>
        /// Água correndo (laço de 4 s): chiado na faixa média que vai e volta devagar, com bolhas
        /// estourando. Toca enquanto a maré anda.
        /// </summary>
        public static AudioClip CreateRush()
        {
            int rate = Rate;
            var rng = new System.Random(4242);
            int n = rate * 4 + rate / 5;
            var data = new float[n];
            float lowA = 0f, lowA2 = 0f, lowB = 0f;
            float bubble = 0f, bubbleFreq = 400f, bubblePhase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                // Passa-baixa de duas etapas (~1 kHz; uma só deixava chiado, centroide em 7,5 kHz).
                lowA += (noise - lowA) * 0.13f;
                lowA2 += (lowA - lowA2) * 0.13f;
                lowB += (noise - lowB) * 0.025f;  // ~180 Hz: o que sobra tirando este é a faixa média
                float band = lowA2 - lowB;
                float swell = 0.65f + 0.2f * Mathf.Sin(2f * Mathf.PI * 0.5f * t) + 0.15f * Mathf.Sin(2f * Mathf.PI * 1.25f * t + 1f);
                if (rng.NextDouble() < 7.0 / rate)
                {
                    bubble = 0.5f + 0.5f * (float)rng.NextDouble();
                    bubbleFreq = 300f + 600f * (float)rng.NextDouble();
                }
                bubblePhase += 2f * Mathf.PI * bubbleFreq / rate;
                bubbleFreq *= 1.00004f;
                bubble *= 1f - 35f / rate;
                data[i] = band * 2.2f * swell + Mathf.Sin(bubblePhase) * bubble * 0.25f;
            }
            float peak = 0f;
            foreach (float v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 0f) for (int i = 0; i < n; i++) data[i] *= 0.7f / peak;
            return Looped("Mare_Correnteza", data, rate);
        }

        /// <summary>Gorgolejo constante dos ralos (laço de 3 s).</summary>
        public static AudioClip CreateGurgle()
        {
            int rate = Rate;
            var rng = new System.Random(3131);
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
            return Looped("Ralo_Gorgolejo", gurgle, rate);
        }

        // Laço sem estalo: o último décimo de segundo se cruza com o começo.
        static AudioClip Looped(string name, float[] samples, int rate)
        {
            int fade = rate / 10, n = samples.Length - fade;
            var trimmed = new float[n];
            System.Array.Copy(samples, trimmed, n);
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                trimmed[i] = samples[i] * w + samples[n + i] * (1f - w);
            }
            AudioClip clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(trimmed, 0);
            return clip;
        }
    }

    /// <summary>Em que ponto do ciclo a maré está (<see cref="PoolTide.Phase"/>).</summary>
    public enum TidePhase
    {
        /// <summary>Água baixa, parada.</summary>
        Low,
        /// <summary>Os últimos segundos antes de subir (a buzina já tocou).</summary>
        Warning,
        Rising,
        /// <summary>Maré alta, parada.</summary>
        High,
        /// <summary>A vazante: descendo, com o ralo puxando.</summary>
        Falling,
        /// <summary>As válvulas foram abertas: a água desce de vez e a maré para.</summary>
        Draining,
    }
}
