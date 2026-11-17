using System.Collections.Generic;
using BackRooms.Net;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// Plástico-bolha no chão do Level 1 (level1-mostruario.md, seções 3.1 e 5.4): folhas postas
    /// pela <see cref="PropLayer"/>, sem colisor. Quem anda em cima faz barulho de corrida, mesmo
    /// agachado — o inimigo ouve (<c>EntityBrain</c> consulta <see cref="Under"/>) — e as bolhas
    /// estalam alto, em 3D, para todo mundo.
    ///
    /// As folhas não acabam (cada passo estoura bolhas novas): não há estado para mandar pela
    /// rede. Cada máquina toca o estalo de cada jogador pela posição dele.
    /// </summary>
    public sealed class BubbleWrap : MonoBehaviour
    {
        /// <summary>Áreas das folhas no chão, em XZ do mundo.</summary>
        static readonly List<Rect> Sheets = new List<Rect>();

        [Range(0f, 1f)] public float volume = 0.85f;
        [Tooltip("Distância até onde se ouve, em metros.")]
        public float range = 30f;
        [Tooltip("De quanto em quanto o pé estoura mais bolhas, em metros.")]
        public float stepMeters = 0.35f;

        readonly Dictionary<PlayerNetwork, Vector3> _lastPop = new Dictionary<PlayerNetwork, Vector3>();
        readonly List<AudioSource> _sources = new List<AudioSource>();
        AudioClip[] _pops;
        int _next;

        /// <summary>Quantas vezes estalou nesta máquina (para os testes).</summary>
        public static int Pops { get; private set; }

        /// <summary>Quantas folhas há no mapa.</summary>
        public static int Count => Sheets.Count;

        public static void Clear() => Sheets.Clear();

        public static void Add(Rect worldXZ) => Sheets.Add(worldXZ);

        /// <summary>O ponto (os pés de alguém) está em cima de uma folha?</summary>
        public static bool Under(Vector3 position)
        {
            for (int i = 0; i < Sheets.Count; i++)
                if (Sheets[i].Contains(new Vector2(position.x, position.z))) return true;
            return false;
        }

        void Awake()
        {
            _pops = new AudioClip[3];
            for (int i = 0; i < _pops.Length; i++) _pops[i] = CreatePops(i);

            for (int i = 0; i < 4; i++)
            {
                var child = new GameObject("Estalo");
                child.transform.SetParent(transform, false);
                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 2f;
                source.maxDistance = range;
                source.dopplerLevel = 0f;
                _sources.Add(source);
            }
        }

        void OnDestroy()
        {
            Sheets.Clear();
        }

        void Update()
        {
            if (Sheets.Count == 0) return;

            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                Vector3 at = player.transform.position;

                if (!Under(at))
                {
                    _lastPop.Remove(player);
                    continue;
                }

                // Pisou na folha agora: estala já. Depois, a cada passo de chão andado.
                if (_lastPop.TryGetValue(player, out Vector3 last))
                {
                    Vector3 moved = at - last;
                    moved.y = 0f;
                    if (moved.magnitude < stepMeters) continue;
                }

                _lastPop[player] = at;
                Play(at);
            }
        }

        void Play(Vector3 at)
        {
            AudioSource source = _sources[_next];
            _next = (_next + 1) % _sources.Count;

            source.transform.position = at + Vector3.up * 0.05f;
            source.pitch = Random.Range(0.92f, 1.1f);
            source.PlayOneShot(_pops[Random.Range(0, _pops.Length)], volume);
            Pops++;
        }

        static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        /// <summary>
        /// Uma pisada no plástico-bolha: 5 a 9 estouros secos em 0,3 s, cada um um clique de
        /// ruído agudo com um "toc" curto de plástico, em intervalos irregulares.
        /// </summary>
        public static AudioClip CreatePops(int variant)
        {
            int rate = Rate;
            int length = (int)(rate * 0.35f);
            var samples = new float[length];
            var rng = new System.Random(4111 + variant * 97);

            int count = 5 + rng.Next(5);
            for (int k = 0; k < count; k++)
            {
                int start = (int)(rate * (0.01f + 0.26f * (float)rng.NextDouble()));
                float loud = 0.5f + 0.5f * (float)rng.NextDouble();
                float tone = 1800f + 1600f * (float)rng.NextDouble();
                float high = 0f, prev = 0f;

                for (int i = 0; i < rate * 0.03f && start + i < length; i++)
                {
                    float t = i / (float)rate;
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    // Passa-alta simples: o estouro é seco e agudo.
                    high = 0.6f * (high + noise - prev);
                    prev = noise;
                    float snap = high * Mathf.Exp(-t * 520f) * 1.6f;
                    float body = Mathf.Sin(2f * Mathf.PI * tone * t) * Mathf.Exp(-t * 260f) * 0.5f;
                    samples[start + i] += (snap + body) * loud;
                }
            }

            float peak = 0f;
            foreach (float s in samples) peak = Mathf.Max(peak, Mathf.Abs(s));
            if (peak > 0f)
                for (int i = 0; i < length; i++) samples[i] *= 0.9f / peak;

            var clip = AudioClip.Create("PlasticoBolha_" + variant, length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
