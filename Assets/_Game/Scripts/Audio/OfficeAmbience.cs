using System;
using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.World;
using UnityEngine;

namespace BackRooms.Audio
{
    /// <summary>
    /// Som ambiente do Escritório (Level 3), sintetizado na hora (sem arquivo de áudio):
    ///
    /// - **Ar-condicionado:** sopro grave e contínuo, 2D, mais forte nos corredores de teto técnico
    ///   (os dutos ficam à vista) e mais fraco nas salas; perto de um difusor do forro, um chiado de
    ///   ar saindo, 3D.
    /// - **Reatores das luminárias:** zumbido de 120 Hz em vozes 3D (poucas, reaproveitadas) postas nas
    ///   luminárias acesas mais perto de quem ouve. Luminária apagada (morta, piscando no escuro, a onda
    ///   de luzes do <see cref="OfficeLightWave"/>) cala na hora: o trecho apagado fica em silêncio.
    ///
    /// Baixo, de fundo: não cobre os passos do monstro nem o alarme. O volume geral do jogo
    /// (<see cref="GameOptions"/>) vale pelo <see cref="AudioListener.volume"/>. Como o zumbido das
    /// outras fases (<see cref="FluorescentHum"/>), só soa jogando: com o menu na tela, calado
    /// (<see cref="MenuMusic.MenuOnScreen"/>). Sem dispositivo de áudio (testes em lote) as contas
    /// continuam e os testes leem os números.
    /// </summary>
    public sealed class OfficeAmbience : MonoBehaviour
    {
        [Range(0f, 1f)] public float airVolume = 0.075f;
        [Tooltip("Fração do sopro dentro das salas (forro fechado), em relação ao corredor.")]
        [Range(0f, 1f)] public float roomAirFactor = 0.5f;
        [Range(0f, 1f)] public float ventVolume = 0.12f;
        [Range(0f, 1f)] public float ballastVolume = 0.1f;
        public float ballastRange = 9f;
        public float ventRange = 6f;
        public float fadeInSeconds = 1.5f;
        public float fadeOutSeconds = 1.5f;

        const int BallastVoices = 6;
        const int VentVoices = 2;
        const float ReassignEvery = 0.2f;
        /// <summary>Segundos para uma voz de reator calar (luminária apagando) e para entrar.</summary>
        const float BallastOff = 0.05f, BallastOn = 0.35f;

        public static OfficeAmbience Instance { get; private set; }

        /// <summary>Verdadeiro jogando; falso com o menu na tela.</summary>
        public bool Wanted { get; private set; }

        /// <summary>Nível do fade geral, de 0 (calado) a 1.</summary>
        public float Level { get; private set; }

        /// <summary>Volume do sopro 2D agora.</summary>
        public float AirNow { get; private set; }

        /// <summary>Quem ouve está num corredor (teto técnico)?</summary>
        public bool InCorridor { get; private set; }

        public int LightCount => _lights.Length;
        public int VentCount => _vents.Length;

        sealed class Voice
        {
            public AudioSource Source;
            public bool IsBallast;
            public CeilingLight Light;
            public int Vent = -1;
            public bool Moving;
            public int Target = -1;
            public float Volume;
        }

        CeilingLight[] _lights = Array.Empty<CeilingLight>();
        Vector3[] _vents = Array.Empty<Vector3>();
        Func<Vector3, bool> _inCorridor;
        AudioSource _air;
        readonly List<Voice> _ballasts = new List<Voice>();
        readonly List<Voice> _ventVoices = new List<Voice>();
        float _reassignAt;
        float _airLevel;
        AudioListener _listener;
        bool _started;

        public void Init(IEnumerable<CeilingLight> lights, IEnumerable<Vector3> vents, Func<Vector3, bool> inCorridor)
        {
            _lights = new List<CeilingLight>(lights).ToArray();
            _vents = new List<Vector3>(vents).ToArray();
            _inCorridor = inCorridor;
        }

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (!Application.isPlaying) return;
            _started = true;

            _air = gameObject.AddComponent<AudioSource>();
            _air.clip = CreateAir();
            _air.loop = true;
            _air.spatialBlend = 0f;
            _air.volume = 0f;
            _air.playOnAwake = false;
            _air.Play();

            AudioClip ballast = CreateBallast();
            for (int i = 0; i < BallastVoices; i++)
            {
                Voice v = NewVoice("Reator", ballast, ballastRange, i);
                v.IsBallast = true;
                _ballasts.Add(v);
            }
            AudioClip vent = CreateVent();
            for (int i = 0; i < VentVoices; i++) _ventVoices.Add(NewVoice("Difusor", vent, ventRange, i + 11));
        }

        Voice NewVoice(string name, AudioClip clip, float range, int index)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = range;
            source.dopplerLevel = 0f;
            source.spread = 60f;
            source.volume = 0f;
            // Cada voz começa num ponto do laço: não somam em fase.
            source.timeSamples = (int)(clip.samples * ((index * 0.37f) % 1f));
            source.Play();
            return new Voice { Source = source };
        }

        /// <summary>Onde quem ouve está: o AudioListener ligado (a câmera do jogador) ou o jogador local.</summary>
        public Vector3 ListenerPosition()
        {
            if (_listener == null || !_listener.isActiveAndEnabled)
            {
                _listener = null;
                foreach (AudioListener l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                    if (l.isActiveAndEnabled) { _listener = l; break; }
            }
            if (_listener != null) return _listener.transform.position;
            PlayerNetwork local = PlayerNetwork.Local;
            return local != null ? local.transform.position + Vector3.up * 1.6f : transform.position;
        }

        void Update()
        {
            if (!_started) return;

            bool? menu = MenuMusic.MenuOnScreen();
            if (menu.HasValue) Wanted = !menu.Value;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float rate = Wanted ? dt / Mathf.Max(0.01f, fadeInSeconds) : -dt / Mathf.Max(0.01f, fadeOutSeconds);
            Level = Mathf.Clamp01(Level + rate);
            float master = Level * Level;

            Vector3 ear = ListenerPosition();

            // Sopro: mais forte sob os dutos do corredor.
            InCorridor = _inCorridor != null && _inCorridor(ear);
            float airTarget = airVolume * (InCorridor ? 1f : roomAirFactor);
            _airLevel = Mathf.MoveTowards(_airLevel, airTarget, dt * airVolume);
            AirNow = master * _airLevel;
            if (_air != null) _air.volume = AirNow;

            if (Time.unscaledTime >= _reassignAt)
            {
                _reassignAt = Time.unscaledTime + ReassignEvery;
                AssignBallasts(ear);
                AssignVents(ear);
            }

            foreach (Voice v in _ballasts)
            {
                bool on = v.Light != null && v.Light.IsLit && !v.Moving;
                float step = dt / (on ? BallastOn : BallastOff);
                v.Volume = Mathf.MoveTowards(v.Volume, on ? 1f : 0f, step);
                MoveWhenSilent(v);
                v.Source.volume = v.Volume * ballastVolume * master;
            }

            foreach (Voice v in _ventVoices)
            {
                bool on = v.Vent >= 0 && !v.Moving;
                v.Volume = Mathf.MoveTowards(v.Volume, on ? 1f : 0f, dt / 0.6f);
                MoveWhenSilent(v);
                v.Source.volume = v.Volume * ventVolume * master;
            }
        }

        // Voz que trocou de lugar: espera calar, muda e volta a entrar (sem estalo).
        void MoveWhenSilent(Voice v)
        {
            if (!v.Moving || v.Volume > 0.001f) return;
            v.Moving = false;
            if (v.IsBallast)
            {
                v.Light = v.Target >= 0 ? _lights[v.Target] : null;
                if (v.Light != null)
                {
                    v.Source.transform.position = v.Light.transform.position;
                    // Cada reator com um tom um pouco diferente.
                    v.Source.pitch = 0.985f + 0.03f * (OfficeElevators.Hash(v.Target, 120) % 100u / 100f);
                }
            }
            else
            {
                v.Vent = v.Target;
                if (v.Vent >= 0) v.Source.transform.position = _vents[v.Vent];
            }
        }

        readonly List<int> _near = new List<int>();

        void AssignBallasts(Vector3 ear)
        {
            // As luminárias acesas mais perto, até o alcance.
            _near.Clear();
            for (int i = 0; i < _lights.Length; i++)
            {
                CeilingLight light = _lights[i];
                if (light == null || !light.IsLit) continue;
                if ((light.transform.position - ear).sqrMagnitude > ballastRange * ballastRange) continue;
                _near.Add(i);
            }
            _near.Sort((a, b) => (_lights[a].transform.position - ear).sqrMagnitude.CompareTo((_lights[b].transform.position - ear).sqrMagnitude));
            if (_near.Count > _ballasts.Count) _near.RemoveRange(_ballasts.Count, _near.Count - _ballasts.Count);
            Assign(_ballasts, _near, v => v.Light != null ? Array.IndexOf(_lights, v.Light) : -1);
        }

        void AssignVents(Vector3 ear)
        {
            _near.Clear();
            for (int i = 0; i < _vents.Length; i++)
                if ((_vents[i] - ear).sqrMagnitude <= ventRange * ventRange) _near.Add(i);
            _near.Sort((a, b) => (_vents[a] - ear).sqrMagnitude.CompareTo((_vents[b] - ear).sqrMagnitude));
            if (_near.Count > _ventVoices.Count) _near.RemoveRange(_ventVoices.Count, _near.Count - _ventVoices.Count);
            Assign(_ventVoices, _near, v => v.Vent);
        }

        static void Assign(List<Voice> voices, List<int> wanted, Func<Voice, int> current)
        {
            // Quem já está num dos lugares pedidos fica; os outros vão para os que sobraram.
            var free = new List<Voice>();
            var taken = new HashSet<int>();
            foreach (Voice v in voices)
            {
                int at = v.Moving ? v.Target : current(v);
                if (at >= 0 && wanted.Contains(at) && taken.Add(at)) continue;
                free.Add(v);
            }
            int next = 0;
            foreach (int place in wanted)
            {
                if (taken.Contains(place)) continue;
                if (next >= free.Count) break;
                Voice v = free[next++];
                v.Target = place;
                v.Moving = true;
            }
            for (; next < free.Count; next++)
            {
                Voice v = free[next];
                if (current(v) < 0 && !v.Moving) continue;
                v.Target = -1;
                v.Moving = true;
            }
        }

        // ---------------------------------------------------------------- Para os testes

        /// <summary>Quanto de reator se ouve em <paramref name="ear"/>: soma das vozes ligadas, pela distância (0 = nada).</summary>
        public float BallastHeardAt(Vector3 ear)
        {
            float sum = 0f;
            foreach (Voice v in _ballasts)
            {
                if (v.Light == null) continue;
                float d = (v.Source.transform.position - ear).magnitude;
                sum += v.Volume * Mathf.Clamp01(1f - Mathf.Max(0f, d - 1f) / (ballastRange - 1f));
            }
            return sum;
        }

        /// <summary>Soma dos volumes (0 a 1 cada) das vozes de reator postas nestas luminárias.</summary>
        public float VolumeOn(ICollection<CeilingLight> lights)
        {
            float sum = 0f;
            foreach (Voice v in _ballasts)
                if (v.Light != null && lights.Contains(v.Light)) sum += v.Volume;
            return sum;
        }

        /// <summary>Alguma voz de reator soa numa luminária apagada?</summary>
        public bool AnyVoiceOnDarkLight()
        {
            foreach (Voice v in _ballasts)
                if (v.Light != null && !v.Light.IsLit && v.Volume > 0.05f) return true;
            return false;
        }

        /// <summary>Vozes de reator soando agora (volume acima de 5%).</summary>
        public int BallastVoicesOn()
        {
            int n = 0;
            foreach (Voice v in _ballasts) if (v.Volume > 0.05f) n++;
            return n;
        }

        // ---------------------------------------------------------------- Síntese

        const int Rate = 44100;

        /// <summary>Reator de luminária: 120 Hz e harmônicos, com um pouco de 60 Hz e um batimento lento. Laço de 2 s.</summary>
        public static AudioClip CreateBallast()
        {
            const float seconds = 2f;
            int n = (int)(Rate * seconds);
            var data = new float[n];
            var rng = new System.Random(1201);
            var phases = new float[26];
            for (int h = 0; h < phases.Length; h++) phases[h] = (float)rng.NextDouble() * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float v = 0.3f * Mathf.Sin(2f * Mathf.PI * 60f * t);
                for (int h = 1; h <= phases.Length; h++)
                {
                    float f = 120f * h;
                    float a = 1f / Mathf.Pow(h, 1.35f);
                    if (f > 1800f && f < 3600f) a *= 1.6f;
                    v += a * Mathf.Sin(2f * Mathf.PI * f * t + phases[h - 1]);
                }
                // Batimento de meio hertz (um ciclo por laço): o reator "respira".
                data[i] = v * (1f - 0.15f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.5f * t)));
            }
            return Finish("Escritorio_Reator", data, 0.8f);
        }

        /// <summary>Ar-condicionado: ronco grave de ar nos dutos, com o giro do ventilador. Laço de 8 s.</summary>
        public static AudioClip CreateAir()
        {
            return Noise("Escritorio_Ar", 8f, 7311, (white, state) =>
            {
                // Ruído grave (dois polos a ~110 Hz) e um sopro médio fraco.
                state[0] += (white - state[0]) * 0.0155f;
                state[1] += (state[0] - state[1]) * 0.0155f;
                state[2] += (white - state[2]) * 0.09f;
                float mid = state[2] - state[1];
                return state[1] * 9f + mid * 0.25f;
            },
            // Ventilador: 47 Hz com um vaivém de 0,25 Hz (inteiros em 8 s: o laço fecha).
            t => Mathf.Sin(2f * Mathf.PI * 47f * t) * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.25f * t)) * 0.06f);
        }

        /// <summary>Difusor do forro: chiado de ar saindo pela grelha. Laço de 6 s.</summary>
        public static AudioClip CreateVent()
        {
            // Faixa de ~400 Hz a ~2,5 kHz.
            return Noise("Escritorio_Difusor", 6f, 9127, (white, state) =>
            {
                state[0] += (white - state[0]) * 0.3f;
                state[1] += (state[0] - state[1]) * 0.055f;
                return state[0] - state[1];
            }, null);
        }

        // Ruído filtrado em laço: gera um pedaço a mais e funde o fim no começo (sem emenda). A parte
        // periódica (inteira no laço) entra depois da fusão, para não dobrar na emenda.
        static AudioClip Noise(string name, float seconds, int seed, Func<float, float[], float> filter, Func<float, float> periodic)
        {
            int n = (int)(Rate * seconds);
            int fade = Rate;
            var raw = new float[n + fade];
            var rng = new System.Random(seed);
            var state = new float[4];
            // Aquece o filtro antes de gravar.
            for (int i = 0; i < Rate; i++) filter((float)rng.NextDouble() * 2f - 1f, state);
            for (int i = 0; i < raw.Length; i++)
                raw[i] = filter((float)rng.NextDouble() * 2f - 1f, state);

            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = raw[i];
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                data[i] = raw[i] * Mathf.Sqrt(w) + raw[n + i] * Mathf.Sqrt(1f - w);
            }
            if (periodic != null)
                for (int i = 0; i < n; i++) data[i] += periodic(i / (float)Rate);
            return Finish(name, data, 0.8f);
        }

        static AudioClip Finish(string name, float[] data, float peakTarget)
        {
            float peak = 0f;
            foreach (float v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 0f) for (int i = 0; i < data.Length; i++) data[i] *= peakTarget / peak;
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }
    }
}
