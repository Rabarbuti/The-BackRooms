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

        /// <summary>Segundos de partida; sem sessão na cena (prévia, teste solto), o tempo da cena.</summary>
        public static double Clock()
        {
            GameSession session = GameSession.Instance;
            if (session == null) return Application.isPlaying ? Time.timeSinceLevelLoad : -1.0;
            return session.SessionSeconds;
        }

        void Update()
        {
            double t = Clock();
            int visiting = -1;
            bool open = false, lit = false;

            if (t >= FirstVisit && _shafts.Count > 0)
            {
                long k = (long)((t - FirstVisit) / VisitEvery);
                double into = t - FirstVisit - k * VisitEvery;
                visiting = (int)(Hash(_seed, (int)k) % (uint)_shafts.Count);
                lit = into < VisitOpen + 2.0;
                open = into >= 1.0 && into < VisitOpen;
            }

            foreach (ElevatorDoors door in _doors)
            {
                if (door.IsCab)
                {
                    door.SetWanted(t >= CabOpensAt);
                    continue;
                }

                bool chosen = _shafts.IndexOf(door) == visiting;
                door.SetCabLight(chosen && lit);
                door.SetWanted(chosen && open);
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
    /// A onda de luzes do Escritório, no lugar do apagão do Level 0: de tempos em tempos as
    /// luminárias de um trecho reto de corredor apagam uma a uma, em fila, com o estalo do
    /// reator; quando a fila termina, todas acendem de volta de uma vez. O trecho e o sentido
    /// saem da seed e do número da onda; o momento, do relógio da sessão.
    /// </summary>
    public sealed class OfficeLightWave : MonoBehaviour
    {
        public const float FirstWave = 45f;
        public const float WaveEvery = 60f;
        public const float StepSeconds = 0.45f;
        public const float HoldSeconds = 2.5f;

        readonly List<CeilingLight[]> _runs = new List<CeilingLight[]>();
        int _seed;
        long _event = -1;
        CeilingLight[] _active;
        bool[] _dark;
        bool _reverse;

        public static OfficeLightWave Instance { get; private set; }

        /// <summary>Quantos trechos de corredor a onda pode percorrer.</summary>
        public int RunCount => _runs.Count;

        /// <summary>Luminárias apagadas pela onda neste instante.</summary>
        public int DarkCount
        {
            get
            {
                if (_dark == null) return 0;
                int n = 0;
                foreach (bool d in _dark) if (d) n++;
                return n;
            }
        }

        public void Init(int seed, List<CeilingLight[]> runs)
        {
            _seed = seed;
            _runs.Clear();
            _runs.AddRange(runs);
        }

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            Release();
        }

        void Update()
        {
            double t = OfficeElevators.Clock();
            if (t < FirstWave || _runs.Count == 0)
            {
                Release();
                return;
            }

            long k = (long)((t - FirstWave) / WaveEvery);
            double into = t - FirstWave - k * WaveEvery;
            CeilingLight[] run = _runs[(int)(OfficeElevators.Hash(_seed + 17, (int)k) % (uint)_runs.Count)];
            double duration = run.Length * StepSeconds + HoldSeconds;
            if (into >= duration)
            {
                Release();
                return;
            }

            if (k != _event)
            {
                Release();
                _event = k;
                _active = run;
                _dark = new bool[run.Length];
                _reverse = (OfficeElevators.Hash(_seed + 29, (int)k) & 1u) == 1u;
            }

            for (int i = 0; i < _active.Length; i++)
            {
                CeilingLight light = _active[_reverse ? _active.Length - 1 - i : i];
                bool dark = into >= i * StepSeconds;
                if (dark && !_dark[i] && light != null && light.IsLit && Application.isPlaying)
                    AudioSource.PlayClipAtPoint(OfficeSounds.Clack, light.transform.position, 0.7f);
                _dark[i] = dark;
                if (light != null) light.SetForcedDark(dark);
            }
        }

        void Release()
        {
            if (_active != null)
                foreach (CeilingLight light in _active)
                    if (light != null) light.SetForcedDark(false);
            _active = null;
            _dark = null;
            _event = -1;
        }
    }

    /// <summary>Sons sintetizados do Escritório (sem arquivo): o plim do elevador, o motor e o estalo do reator.</summary>
    public static class OfficeSounds
    {
        const int Rate = 44100;
        static AudioClip _chime, _rumble, _clack;

        public static AudioClip Chime => _chime != null ? _chime : (_chime = Make("Elevador_Plim", 1.2f, ChimeSample));
        public static AudioClip Rumble => _rumble != null ? _rumble : (_rumble = Make("Elevador_Motor", 1.6f, RumbleSample()));
        public static AudioClip Clack => _clack != null ? _clack : (_clack = Make("Reator_Estalo", 0.3f, ClackSample()));

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
