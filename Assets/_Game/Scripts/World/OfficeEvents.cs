using System.Collections.Generic;
using BackRooms.Net;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    /// <summary>
    /// Porta de elevador do saguão do Escritório: duas folhas de inox que correm para dentro da
    /// parede (com os colisores), o "plim" ao abrir, o ronco do motor e, nos poços fechados, a
    /// luz da cabine vazia que só acende quando o elevador "chega". Quem decide abrir é o
    /// <see cref="OfficeElevators"/>.
    /// </summary>
    public sealed class ElevatorDoors : MonoBehaviour
    {
        const float OpenSeconds = 1.4f;

        Transform _left, _right;
        Vector3 _left0, _right0, _slide;
        bool _isCab;
        HDAdditionalLightData _light;
        MeshRenderer _spots;
        Material _on, _off;
        AudioSource _audio;
        float _open, _target;
        bool _lit;

        public bool IsCab => _isCab;

        /// <summary>0 = fechada, 1 = aberta.</summary>
        public float Openness => _open;

        public bool WantsOpen => _target > 0.5f;

        /// <summary>
        /// Mantida aberta por outro sistema (o inimigo que chega de elevador, enquanto está fora da
        /// cabine): nem o relógio nem o botão de fechar a fecham. Ligar e desligar em todas as
        /// máquinas (a porta anda em cada uma).
        /// </summary>
        public bool HeldOpen { get; set; }

        /// <summary>Luz da cabine acesa agora (só nos poços).</summary>
        public bool CabLit => _lit;

        public void Init(Transform left, Transform right, Vector3 slide, bool isCab, HDAdditionalLightData cabLight, MeshRenderer spots,
                         Material on, Material off)
        {
            _left = left;
            _right = right;
            _left0 = left.localPosition;
            _right0 = right.localPosition;
            _slide = slide;
            _isCab = isCab;
            _light = cabLight;
            _spots = spots;
            _on = on;
            _off = off;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.minDistance = 2f;
            _audio.maxDistance = 30f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
        }

        public void SetWanted(bool open)
        {
            if (open == WantsOpen) return;
            _target = open ? 1f : 0f;
            if (_audio == null || !Application.isPlaying) return;
            if (open) _audio.PlayOneShot(OfficeSounds.Chime, 0.8f);
            _audio.PlayOneShot(OfficeSounds.Rumble, 0.5f);
        }

        /// <summary>Luz da cabine (só nos poços; a da cabine inicial é uma luminária comum).</summary>
        public void SetCabLight(bool on)
        {
            if (_light == null || _lit == on) return;
            _lit = on;
            _light.lightDimmer = on ? 1f : 0f;
            if (_spots != null) _spots.sharedMaterial = on ? _on : _off;
        }

        /// <summary>Põe as folhas direto numa abertura, sem som (fotos fora do Play).</summary>
        public void Snap(float openness)
        {
            _open = _target = Mathf.Clamp01(openness);
            float eased = _open * _open * (3f - 2f * _open);
            _left.localPosition = _left0 - _slide * eased;
            _right.localPosition = _right0 + _slide * eased;
        }

        void Update()
        {
            if (Mathf.Approximately(_open, _target)) return;
            _open = Mathf.MoveTowards(_open, _target, Time.deltaTime / OpenSeconds);
            float eased = _open * _open * (3f - 2f * _open);
            _left.localPosition = _left0 - _slide * eased;
            _right.localPosition = _right0 + _slide * eased;
        }
    }

    /// <summary>
    /// Os elevadores do saguão. A porta da cabine onde os jogadores nascem abre alguns segundos
    /// depois do início da partida. De tempos em tempos um dos poços fechados "chega": a luz da
    /// cabine vazia acende, o plim toca, a porta abre, espera e fecha. Tudo pelo relógio da
    /// sessão (<see cref="GameSession.SessionSeconds"/>) e pela seed: igual em todas as máquinas,
    /// sem nada pela rede.
    /// </summary>
    public sealed class OfficeElevators : MonoBehaviour
    {
        public const float CabOpensAt = 3.5f;
        public const float FirstVisit = 75f;
        public const float VisitEvery = 85f;
        public const float VisitOpen = 8f;

        readonly List<ElevatorDoors> _doors = new List<ElevatorDoors>();
        readonly List<ElevatorDoors> _shafts = new List<ElevatorDoors>();
        int _seed;

        public static OfficeElevators Instance { get; private set; }
        public IReadOnlyList<ElevatorDoors> Doors => _doors;

        public void Init(int seed, List<ElevatorDoors> doors)
        {
            _seed = seed;
            _doors.Clear();
            _doors.AddRange(doors);
            _shafts.Clear();
            foreach (ElevatorDoors door in doors)
                if (!door.IsCab) _shafts.Add(door);
        }

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Testes: relógio forçado dos eventos do Escritório (negativo = o relógio da sessão).</summary>
        public static double ForceSeconds = -1.0;

        /// <summary>
        /// A fase tem o inimigo do elevador (o Ascensorista, <c>AttendantBrain</c>, liga em todas as
        /// máquinas): o relógio deixa de mandar elevadores vazios, porque todo plim passa a ser ele. A
        /// porta da cabine em que ele chega é dele (<see cref="ElevatorDoors.HeldOpen"/>).
        /// </summary>
        public static bool VisitsByEnemy;

        /// <summary>Segundos de partida; sem sessão na cena (prévia, teste solto), o tempo da cena.</summary>
        public static double Clock()
        {
            if (ForceSeconds >= 0.0) return ForceSeconds;
            GameSession session = GameSession.Instance;
            if (session == null) return Application.isPlaying ? Time.timeSinceLevelLoad : -1.0;
            return session.SessionSeconds;
        }

        void Update()
        {
            double t = Clock();
            int visiting = -1;
            bool open = false, lit = false;

            if (t >= FirstVisit && _shafts.Count > 0 && !VisitsByEnemy)
            {
                long k = (long)((t - FirstVisit) / VisitEvery);
                double into = t - FirstVisit - k * VisitEvery;
                visiting = (int)(Hash(_seed, (int)k) % (uint)_shafts.Count);
                lit = into < VisitOpen + 2.0;
                open = into >= 1.0 && into < VisitOpen;
            }

            foreach (ElevatorDoors door in _doors)
            {
                // Os botões (ElevatorControl) e a trava (HeldOpen) mandam por cima do relógio.
                if (door.IsCab)
                {
                    door.SetWanted(ElevatorControl.Resolve(door, t >= CabOpensAt, t, out _));
                    continue;
                }

                bool chosen = _shafts.IndexOf(door) == visiting;
                bool wanted = ElevatorControl.Resolve(door, chosen && open, t, out bool called);
                door.SetCabLight((chosen && lit) || called || wanted);
                door.SetWanted(wanted);
            }
        }

        public static uint Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA6Bu;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return h;
            }
        }
    }

    /// <summary>
    /// Um trecho apagado pela onda de luzes: qual trecho (<see cref="Run"/>, índice em
    /// <see cref="OfficeLightWave"/>), quando começa (<see cref="Start"/>, segundos de sessão), o
    /// sentido em que as luminárias apagam e, se alguém religou o interruptor do trecho, quando
    /// (<see cref="Cut"/>; sem interruptor, <see cref="Never"/>). Vai pela rede (o host escolhe).
    /// </summary>
    public struct WavePlan : Unity.Netcode.INetworkSerializable, System.IEquatable<WavePlan>
    {
        public const double Never = 1e12;

        public short Run;
        public bool Reverse;
        public double Start;
        public double Cut;

        public void NetworkSerialize<T>(Unity.Netcode.BufferSerializer<T> serializer) where T : Unity.Netcode.IReaderWriter
        {
            serializer.SerializeValue(ref Run);
            serializer.SerializeValue(ref Reverse);
            serializer.SerializeValue(ref Start);
            serializer.SerializeValue(ref Cut);
        }

        public bool Equals(WavePlan other) => Run == other.Run && Reverse == other.Reverse && Start == other.Start && Cut == other.Cut;
    }

    /// <summary>
    /// A onda de luzes do Escritório, no lugar do apagão do Level 0, e o caminho do Zelador
    /// (<c>JanitorBrain</c>), que só existe no escuro. Cada passagem (<see cref="WavePlan"/>) num
    /// trecho reto de corredor: as luminárias <b>piscam</b> por <see cref="WarnSeconds"/> (o reator
    /// falha no som: é o tempo de sair), apagam em cascata, ficam apagadas <see cref="HoldSeconds"/> e
    /// voltam uma a uma na mesma ordem; o interruptor do trecho (<c>LightSwitches</c>) religa tudo na
    /// hora.
    ///
    /// Quem escolhe os trechos é o host, perto de onde há jogador (<see cref="Planner"/>, posto pelo
    /// cérebro do Zelador e sincronizado pela rede); sem ele (prévia, Zelador desligado), a onda segue
    /// o relógio da sessão e a seed, como antes. Cada máquina faz a mesma conta sobre a hora da sessão;
    /// só o pisca-pisca do aviso é sorteado em cada uma.
    /// </summary>
    public sealed class OfficeLightWave : MonoBehaviour
    {
        // Até 20/11/2026: a cada 60 s desde os 45 s, um trecho só, 2,5 s de escuro e sem aviso (o
        // Zelador mal existia). Depois: escolhida perto dos jogadores, com aviso e escuro longo.
        public const float FirstWave = 25f;
        public const float WaveEvery = 40f;
        /// <summary>Segundos piscando antes de apagar.</summary>
        public const float WarnSeconds = 2.5f;
        /// <summary>Intervalo entre uma luminária e a seguinte apagando (a cascata).</summary>
        public const float StepSeconds = 0.12f;
        /// <summary>Segundos com o trecho inteiro apagado, depois da última luminária apagar.</summary>
        public const float HoldSeconds = 16f;
        /// <summary>Intervalo entre uma luminária e a seguinte voltando a acender.</summary>
        public const float RelightSeconds = 0.7f;
        /// <summary>Quantos trechos cada onda apaga, sem planejador (diferentes, se a fase tem mais de um).</summary>
        public const int RunsPerWave = 2;

        /// <summary>Para os testes: parada, a onda não apaga nada (e devolve o que apagou).</summary>
        public static bool Paused;

        /// <summary>
        /// Os trechos a apagar, escolhidos pelo host e iguais em todas as máquinas (o cérebro do
        /// Zelador põe e tira). Nulo: a onda segue o relógio e a seed.
        /// </summary>
        public static System.Func<IReadOnlyList<WavePlan>> Planner;

        /// <summary>
        /// Para os testes que forçam o relógio (<see cref="OfficeElevators.ForceSeconds"/>): ignora o
        /// <see cref="Planner"/> e segue o relógio e a seed (cada passagem começa pelo aviso).
        /// </summary>
        public static bool UseClock;

        readonly List<CeilingLight[]> _runs = new List<CeilingLight[]>();
        readonly List<WavePlan> _clockPlans = new List<WavePlan>();
        readonly HashSet<CeilingLight> _want = new HashSet<CeilingLight>();
        readonly HashSet<CeilingLight> _warn = new HashSet<CeilingLight>();
        readonly HashSet<CeilingLight> _touched = new HashSet<CeilingLight>();
        readonly HashSet<CeilingLight> _wasDark = new HashSet<CeilingLight>();
        readonly HashSet<CeilingLight> _wasWarn = new HashSet<CeilingLight>();
        readonly List<CeilingLight> _gone = new List<CeilingLight>();
        readonly Dictionary<CeilingLight, float> _blink = new Dictionary<CeilingLight, float>();
        readonly Dictionary<CeilingLight, int> _runOf = new Dictionary<CeilingLight, int>();
        int _seed;

        public static OfficeLightWave Instance { get; private set; }

        /// <summary>Quantos trechos de corredor a onda pode percorrer.</summary>
        public int RunCount => _runs.Count;

        /// <summary>Luminárias apagadas pela onda neste instante (fora as que só piscam).</summary>
        public int DarkCount => _want.Count;

        /// <summary>Luminárias piscando no aviso neste instante.</summary>
        public int WarningCount => _warn.Count;

        public void Init(int seed, List<CeilingLight[]> runs)
        {
            _seed = seed;
            _runs.Clear();
            _runs.AddRange(runs);
            _runOf.Clear();
            for (int r = 0; r < _runs.Count; r++)
                foreach (CeilingLight lamp in _runs[r])
                    if (lamp != null && !_runOf.ContainsKey(lamp)) _runOf[lamp] = r;
        }

        /// <summary>As luminárias do trecho <paramref name="run"/>, na ordem do corredor.</summary>
        public IReadOnlyList<CeilingLight> RunLamps(int run) => run >= 0 && run < _runs.Count ? _runs[run] : System.Array.Empty<CeilingLight>();

        /// <summary>O trecho da luminária (o primeiro, se for de cruzamento), ou -1.</summary>
        public int RunIndexOf(CeilingLight lamp) => lamp != null && _runOf.TryGetValue(lamp, out int r) ? r : -1;

        /// <summary>A luminária está piscando no aviso (ainda não apagou)? O Zelador a conta como acesa.</summary>
        public bool InWarning(CeilingLight lamp) => _warn.Contains(lamp) && !_want.Contains(lamp);

        /// <summary>O primeiro trecho que a onda número <paramref name="k"/> percorre, sem planejador.</summary>
        public CeilingLight[] RunFor(long k) => RunFor(k, 0);

        /// <summary>O trecho <paramref name="which"/> (0 ou 1) da onda <paramref name="k"/>, sem planejador.</summary>
        public CeilingLight[] RunFor(long k, int which)
        {
            int i = RunIndexFor(k, which);
            return i < 0 ? null : _runs[i];
        }

        int RunIndexFor(long k, int which)
        {
            if (_runs.Count == 0) return -1;
            int first = (int)(OfficeElevators.Hash(_seed + 17, (int)k) % (uint)_runs.Count);
            if (which == 0 || _runs.Count == 1) return first;
            int step = 1 + (int)(OfficeElevators.Hash(_seed + 41, (int)k) % (uint)(_runs.Count - 1));
            return (first + step) % _runs.Count;
        }

        /// <summary>O (primeiro) trecho que a onda está apagando agora (nulo fora de uma onda).</summary>
        public CeilingLight[] ActiveRun
        {
            get
            {
                foreach (CeilingLight lamp in _want)
                {
                    int r = RunIndexOf(lamp);
                    if (r >= 0) return _runs[r];
                }
                return null;
            }
        }

        // ---------------------------------------------------------------- Tempo de cada passagem

        /// <summary>Quando acaba o aviso (o trecho começa a apagar).</summary>
        public static double DarkStart(WavePlan p) => p.Start + WarnSeconds;

        /// <summary>Quando a luminária de ordem <paramref name="i"/> apaga.</summary>
        public static double LampDark(WavePlan p, int i) => DarkStart(p) + i * StepSeconds;

        /// <summary>Quando a luminária de ordem <paramref name="i"/> volta, num trecho de <paramref name="n"/>.</summary>
        public static double LampRelit(WavePlan p, int n, int i) => System.Math.Min(p.Cut, DarkStart(p) + n * StepSeconds + HoldSeconds + i * RelightSeconds);

        /// <summary>Quando a passagem termina (tudo aceso de volta).</summary>
        public double End(WavePlan p)
        {
            int n = p.Run >= 0 && p.Run < _runs.Count ? _runs[p.Run].Length : 0;
            return System.Math.Min(p.Cut, DarkStart(p) + n * StepSeconds + HoldSeconds + n * RelightSeconds);
        }

        /// <summary>Segundos que dura a passagem num trecho de <paramref name="length"/> luminárias (sem interruptor).</summary>
        public static double Duration(int length) => WarnSeconds + length * StepSeconds + HoldSeconds + length * RelightSeconds;

        /// <summary>A passagem está no aviso (piscando) na hora <paramref name="t"/>?</summary>
        public static bool Warning(WavePlan p, double t) => t >= p.Start && t < DarkStart(p) && t < p.Cut;

        /// <summary>A passagem tem luminária apagada na hora <paramref name="t"/>?</summary>
        public bool Dark(WavePlan p, double t) => t >= DarkStart(p) && t < End(p);

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            Release();
        }

        IReadOnlyList<WavePlan> ClockPlans(double t)
        {
            _clockPlans.Clear();
            if (t < FirstWave || _runs.Count == 0) return _clockPlans;
            long k = (long)((t - FirstWave) / WaveEvery);
            for (int r = 0; r < Mathf.Min(RunsPerWave, _runs.Count); r++)
                _clockPlans.Add(new WavePlan
                {
                    Run = (short)RunIndexFor(k, r),
                    Reverse = (OfficeElevators.Hash(_seed + 29 + r * 13, (int)k) & 1u) == 1u,
                    Start = FirstWave + k * (double)WaveEvery,
                    Cut = WavePlan.Never,
                });
            return _clockPlans;
        }

        void Update()
        {
            double t = OfficeElevators.Clock();
            if (Paused || _runs.Count == 0 || t < 0)
            {
                Release();
                return;
            }

            IReadOnlyList<WavePlan> plans = Planner != null && !UseClock ? Planner() : ClockPlans(t);

            // Dois trechos podem dividir a luminária do cruzamento: apagada se algum a quer apagada.
            _want.Clear();
            _warn.Clear();
            if (plans != null)
                foreach (WavePlan p in plans)
                {
                    if (p.Run < 0 || p.Run >= _runs.Count || t < p.Start || t >= p.Cut) continue;
                    CeilingLight[] run = _runs[p.Run];
                    if (Warning(p, t))
                    {
                        foreach (CeilingLight lamp in run) if (lamp != null) _warn.Add(lamp);
                        continue;
                    }
                    for (int i = 0; i < run.Length; i++)
                    {
                        CeilingLight lamp = run[p.Reverse ? run.Length - 1 - i : i];
                        // Apaga em cascata e volta na mesma ordem: o escuro anda pelo corredor e some na ponta.
                        if (lamp != null && t >= LampDark(p, i) && t < LampRelit(p, run.Length, i)) _want.Add(lamp);
                    }
                }

            foreach (CeilingLight lamp in _want) _touched.Add(lamp);
            foreach (CeilingLight lamp in _warn) _touched.Add(lamp);

            _gone.Clear();
            foreach (CeilingLight lamp in _touched)
            {
                if (lamp == null)
                {
                    _gone.Add(lamp);
                    continue;
                }
                bool dark = _want.Contains(lamp);
                bool warn = !dark && _warn.Contains(lamp);

                // Começou a piscar: o reator falha no som. Apagou: o estalo.
                if (warn && !_wasWarn.Contains(lamp) && Application.isPlaying)
                    AudioSource.PlayClipAtPoint(OfficeSounds.Sputter, lamp.transform.position, 0.45f);
                if (dark && !_wasDark.Contains(lamp) && Application.isPlaying)
                    AudioSource.PlayClipAtPoint(OfficeSounds.Clack, lamp.transform.position, 0.7f);

                if (warn)
                {
                    // Pisca: rajadas curtas de apagado (só visual: cada máquina sorteia as suas).
                    _blink.TryGetValue(lamp, out float next);
                    if (Time.time >= next)
                    {
                        bool off = !lamp.ForcedDark;
                        lamp.SetForcedDark(off);
                        _blink[lamp] = Time.time + (off ? Random.Range(0.04f, 0.16f) : Random.Range(0.05f, 0.35f));
                    }
                }
                else
                {
                    lamp.SetForcedDark(dark);
                    if (!dark) _gone.Add(lamp);
                }

                if (dark) _wasDark.Add(lamp); else _wasDark.Remove(lamp);
                if (warn) _wasWarn.Add(lamp); else _wasWarn.Remove(lamp);
            }
            foreach (CeilingLight lamp in _gone)
            {
                _touched.Remove(lamp);
                _blink.Remove(lamp);
            }
        }

        void Release()
        {
            foreach (CeilingLight lamp in _touched)
                if (lamp != null) lamp.SetForcedDark(false);
            _touched.Clear();
            _want.Clear();
            _warn.Clear();
            _wasDark.Clear();
            _wasWarn.Clear();
            _blink.Clear();
        }
    }

    /// <summary>Sons sintetizados do Escritório (sem arquivo): o plim do elevador, o motor e o estalo do reator.</summary>
    public static class OfficeSounds
    {
        const int Rate = 44100;
        static AudioClip _chime, _rumble, _clack, _sputter;

        public static AudioClip Chime => _chime != null ? _chime : (_chime = Make("Elevador_Plim", 1.2f, ChimeSample));
        public static AudioClip Rumble => _rumble != null ? _rumble : (_rumble = Make("Elevador_Motor", 1.6f, RumbleSample()));
        public static AudioClip Clack => _clack != null ? _clack : (_clack = Make("Reator_Estalo", 0.3f, ClackSample()));
        public static AudioClip Sputter => _sputter != null ? _sputter : (_sputter = Make("Reator_Falhando", 1.4f, SputterSample()));

        static AudioClip Make(string name, float seconds, System.Func<float, float> sample)
        {
            int count = (int)(seconds * Rate);
            var data = new float[count];
            float peak = 0f;
            for (int i = 0; i < count; i++)
            {
                data[i] = sample(i / (float)Rate);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            if (peak > 0f) for (int i = 0; i < count; i++) data[i] *= 0.8f / peak;
            for (int i = 0; i < Mathf.Min(count, 400); i++) data[count - 1 - i] *= i / 400f;

            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        // Dois toques (mi e dó), como o plim de elevador, com harmônicos de sino.
        static float ChimeSample(float t)
        {
            float Tone(float f, float start)
            {
                float u = t - start;
                if (u < 0f) return 0f;
                float env = Mathf.Min(1f, u / 0.004f) * Mathf.Exp(-u / 0.35f);
                return env * (Mathf.Sin(2f * Mathf.PI * f * u) + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 2.01f * u) + 0.12f * Mathf.Sin(2f * Mathf.PI * f * 3.02f * u));
            }
            return Tone(659.3f, 0f) + Tone(523.3f, 0.42f);
        }

        // Motor e porta correndo: ruído grave filtrado com um zumbido de 60 Hz.
        static System.Func<float, float> RumbleSample()
        {
            var rng = new System.Random(5);
            float low = 0f;
            float alpha = 1f - Mathf.Exp(-2f * Mathf.PI * 180f / Rate);
            return t =>
            {
                low += ((float)rng.NextDouble() * 2f - 1f - low) * alpha;
                float env = Mathf.Min(1f, t / 0.25f) * Mathf.Min(1f, (1.6f - t) / 0.5f);
                return env * (low * 3f + 0.15f * Mathf.Sin(2f * Mathf.PI * 60f * t));
            };
        }

        // Reator falhando (o aviso da onda): o zumbido de 120 Hz engasgando, com estalos e cortes.
        static System.Func<float, float> SputterSample()
        {
            var rng = new System.Random(13);
            float gate = 1f, until = 0f;
            return t =>
            {
                if (t >= until)
                {
                    gate = rng.NextDouble() < 0.45 ? 0f : 1f;
                    until = t + 0.02f + (float)rng.NextDouble() * 0.12f;
                }
                float hum = Mathf.Sin(2f * Mathf.PI * 120f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 240f * t + 0.3f) + 0.25f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 360f * t));
                float crackle = rng.NextDouble() < 0.004 ? ((float)rng.NextDouble() * 2f - 1f) * 3f : 0f;
                float env = Mathf.Min(1f, t / 0.05f) * Mathf.Min(1f, (1.4f - t) / 0.2f);
                return env * (hum * 0.35f * gate + crackle);
            };
        }

        // Estalo de reator desligando: um "tunc" grave e um clique seco.
        static System.Func<float, float> ClackSample()
        {
            var rng = new System.Random(9);
            return t =>
            {
                float thump = Mathf.Sin(2f * Mathf.PI * (110f + 60f * Mathf.Exp(-t / 0.02f)) * t) * Mathf.Exp(-t / 0.035f);
                float click = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t / 0.004f);
                return thump + click * 0.8f;
            };
        }
    }
}
