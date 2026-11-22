using System.Collections.Generic;
using BackRooms.Net;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// Uma porta de armário amarelo do Escritório solta da malha juntada (uma por módulo de 3 x 3),
    /// numa dobradiça, para poder bater. Fechada é igual às outras.
    /// </summary>
    public sealed class LockerDoor : MonoBehaviour
    {
        Quaternion _closed;
        float _sign = 1f;
        float _angle;

        /// <summary>Célula do corredor onde o armário está.</summary>
        public int X { get; private set; }
        public int Z { get; private set; }

        /// <summary>Para onde a porta dá (o corredor), no mundo.</summary>
        public Vector3 Facing => transform.forward;

        /// <summary>Meio da folha, no mundo.</summary>
        public Vector3 Center { get; private set; }

        /// <summary>Onde o barulho acontece: no corredor, um metro à frente da porta.</summary>
        public Vector3 NoisePoint => new Vector3(Center.x, transform.position.y, Center.z) + Facing * 1f;

        public float Angle => _angle;

        public void Init(int x, int z, Vector3 leafCenterLocal)
        {
            X = x;
            Z = z;
            _closed = transform.localRotation;
            Center = transform.TransformPoint(leafCenterLocal);
            // A dobradiça gira para o lado que leva a ponta solta para o corredor.
            Vector3 free = Center - transform.position;
            _sign = Vector3.Dot(Quaternion.AngleAxis(30f, Vector3.up) * free, Facing) > 0f ? 1f : -1f;
        }

        public void SetAngle(float degrees)
        {
            if (Mathf.Abs(degrees - _angle) < 0.01f) return;
            _angle = degrees;
            transform.localRotation = _closed * Quaternion.AngleAxis(_sign * degrees, Vector3.up);
        }
    }

    /// <summary>
    /// Os armários que batem (ideia 5 de level3-escritorio.md, seção 6): de tempos em tempos, uma fila
    /// de 3 a 6 portas de armário batendo ao longo de um corredor longe dos jogadores, vindo na
    /// direção do mais perto. Pelo relógio da sessão (<see cref="OfficeElevators.Clock"/>) e pela seed,
    /// sem nada pela rede: cada máquina faz a mesma conta. O corredor é o primeiro, numa ordem sorteada
    /// pela seed e pelo número do evento, que fica a pelo menos <see cref="MinDistance"/> de todos os
    /// jogadores (sem nenhum, o mais longe, se a <see cref="FallbackDistance"/> ou mais); escolhido uma vez, no começo do evento (perto do limite, um cliente pode raramente
    /// escolher outro; o barulho que conta é o do host). Sem nenhum longe o bastante, o evento não
    /// acontece. Corredor aqui é uma fileira reta de células de corredor (uma só já serve: uma parede
    /// de armários tem 3 módulos) com 3 portas soltas ou mais. A última batida é um barulho no mundo (<see cref="WorldNoise"/>): o inimigo que ouve vai até lá.
    /// </summary>
    public sealed class LockerBangs : MonoBehaviour
    {
        public const float FirstBang = 110f;
        public const float BangEvery = 95f;
        public const float JitterSeconds = 25f;
        public const float MinDistance = 16f;
        /// <summary>Sem corredor a <see cref="MinDistance"/>, vale o mais longe se estiver a pelo menos isto.</summary>
        public const float FallbackDistance = 10f;
        public const float NoiseRadius = 44f;
        public const int MinBangs = 3;
        public const int MaxBangs = 6;
        /// <summary>Batida que chega mais atrasada que isto (relógio que pulou) não toca.</summary>
        const float LateSeconds = 0.5f;

        /// <summary>Um corredor reto com armários: as portas soltas na ordem do corredor.</summary>
        public sealed class Row
        {
            public LockerDoor[] Doors;
        }

        /// <summary>O que um evento faz: corredor, portas na ordem das batidas e a hora de cada uma.</summary>
        public sealed class Plan
        {
            public long Event;
            public int Row = -1;
            public LockerDoor[] Doors;
            public double[] Times;
            public bool[] Played;
            public bool NoiseSent;
            public bool Skipped => Row < 0;
            public double End => Times != null && Times.Length > 0 ? Times[Times.Length - 1] + 1.0 : 0.0;
        }

        readonly List<Row> _rows = new List<Row>();
        int _seed;
        Plan _plan;
        AudioSource[] _voices;
        int _nextVoice;
        AudioClip[] _clips;

        public static LockerBangs Instance { get; private set; }

        public int RowCount => _rows.Count;
        public int DoorCount { get; private set; }

        /// <summary>O último evento escolhido (ou pulado), para os testes.</summary>
        public Plan Last => _plan;

        /// <summary>Batidas tocadas e barulhos mandados desde que a fase montou.</summary>
        public int BangsPlayed { get; private set; }
        public int NoisesSent { get; private set; }

        public void Init(int seed, List<Row> rows)
        {
            _seed = seed;
            _rows.Clear();
            _rows.AddRange(rows);
            DoorCount = 0;
            foreach (Row row in rows) DoorCount += row.Doors.Length;
        }

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Quando o evento <paramref name="k"/> começa (segundos de partida).</summary>
        public double StartOf(long k)
        {
            double jitter = OfficeElevators.Hash(_seed + 53, (int)k) % 1000u / 1000.0 * JitterSeconds;
            return FirstBang + k * (double)BangEvery + jitter;
        }

        /// <summary>
        /// O plano do evento <paramref name="k"/> com os jogadores em <paramref name="players"/>: o
        /// mesmo em todas as máquinas para as mesmas posições.
        /// </summary>
        public Plan Choose(long k, IList<Vector3> players)
        {
            var plan = new Plan { Event = k };
            if (_rows.Count == 0) return plan;

            // Ordem dos corredores sorteada pela seed e pelo evento.
            var order = new List<int>(_rows.Count);
            var keys = new uint[_rows.Count];
            for (int i = 0; i < _rows.Count; i++)
            {
                order.Add(i);
                keys[i] = OfficeElevators.Hash(_seed + 61, (int)k * 4099 + i);
            }
            order.Sort((a, b) => keys[a] != keys[b] ? keys[a].CompareTo(keys[b]) : a.CompareTo(b));

            float farthest = -1f;
            int far = -1;
            foreach (int r in order)
            {
                float distance = Nearest(_rows[r].Doors, players);
                if (distance > farthest + 0.01f)
                {
                    farthest = distance;
                    far = r;
                }
                if (distance < MinDistance) continue;
                plan.Row = r;
                break;
            }
            // Mapa apertado: o corredor mais longe, se não estiver em cima de ninguém.
            if (plan.Row < 0 && farthest >= FallbackDistance) plan.Row = far;
            if (plan.Row < 0) return plan;

            LockerDoor[] doors = _rows[plan.Row].Doors;
            int count = Mathf.Min(doors.Length, MinBangs + (int)(OfficeElevators.Hash(_seed + 67, (int)k) % (uint)(MaxBangs - MinBangs + 1)));
            int window = Mathf.Min(doors.Length, count * 2);
            int offset = (int)(OfficeElevators.Hash(_seed + 73, (int)k) % (uint)(doors.Length - window + 1));
            var chosen = new LockerDoor[count];
            for (int j = 0; j < count; j++)
                chosen[j] = doors[offset + (count > 1 ? Mathf.RoundToInt(j * (window - 1) / (float)(count - 1)) : 0)];

            // Vem na direção do jogador mais perto: começa pela ponta mais longe dele.
            if (players.Count > 0 && count > 1)
            {
                float first = float.MaxValue, last = float.MaxValue;
                foreach (Vector3 p in players)
                {
                    first = Mathf.Min(first, Flat(chosen[0].NoisePoint - p));
                    last = Mathf.Min(last, Flat(chosen[count - 1].NoisePoint - p));
                }
                if (first < last) System.Array.Reverse(chosen);
            }

            plan.Doors = chosen;
            plan.Times = new double[count];
            plan.Played = new bool[count];
            double t = StartOf(k);
            for (int j = 0; j < count; j++)
            {
                plan.Times[j] = t;
                t += 0.45 + OfficeElevators.Hash(_seed + 71, (int)k * 16 + j) % 100u / 100.0 * 0.5;
            }
            return plan;
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        static float Nearest(LockerDoor[] doors, IList<Vector3> players)
        {
            float best = float.MaxValue;
            foreach (Vector3 p in players)
                foreach (LockerDoor door in doors)
                    best = Mathf.Min(best, Flat(door.NoisePoint - p));
            return best;
        }

        /// <summary>Onde estão os jogadores na partida (todas as máquinas veem as mesmas posições, com atraso).</summary>
        public static List<Vector3> PlayerPositions()
        {
            var list = new List<Vector3>();
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.InGame) list.Add(player.transform.position);
            return list;
        }

        void Update()
        {
            double t = OfficeElevators.Clock();
            if (t < FirstBang || _rows.Count == 0)
            {
                Release();
                return;
            }

            // O evento em curso: o último que já começou.
            long k = (long)((t - FirstBang) / BangEvery);
            if (k > 0 && t < StartOf(k)) k--;
            if (t < StartOf(k))
            {
                Release();
                return;
            }

            if (_plan == null || _plan.Event != k)
            {
                Release();
                _plan = Choose(k, PlayerPositions());
            }
            if (_plan.Skipped) return;

            for (int j = 0; j < _plan.Doors.Length; j++)
            {
                LockerDoor door = _plan.Doors[j];
                double u = t - _plan.Times[j];
                if (door != null) door.SetAngle(SwingAngle((float)u));
                if (_plan.Played[j] || u < 0.0) continue;
                _plan.Played[j] = true;
                if (u > LateSeconds || door == null) continue;

                BangsPlayed++;
                Play(door.Center + door.Facing * 0.3f, j);
                if (j == _plan.Doors.Length - 1 && !_plan.NoiseSent)
                {
                    _plan.NoiseSent = true;
                    NoisesSent++;
                    WorldNoise.Emit(door.NoisePoint, NoiseRadius);
                }
            }
        }

        /// <summary>
        /// Ângulo da porta em graus, pelo tempo até a batida (<paramref name="u"/> = 0 é a batida):
        /// abre de repente, fica um instante escancarada, bate e quica.
        /// </summary>
        public static float SwingAngle(float u)
        {
            const float max = 105f;
            if (u < -0.55f || u > 0.3f) return 0f;
            if (u < -0.38f)
            {
                float a = (u + 0.55f) / 0.17f;
                return max * a * a * (3f - 2f * a);
            }
            if (u < -0.1f) return max - 4f * Mathf.Sin((u + 0.38f) * 30f);
            if (u < 0f)
            {
                float a = (u + 0.1f) / 0.1f;
                return max * (1f - a * a);
            }
            return 9f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, u / 0.14f)) * (u < 0.14f ? 1f : 0f);
        }

        void Release()
        {
            if (_plan != null && _plan.Doors != null)
                foreach (LockerDoor door in _plan.Doors)
                    if (door != null) door.SetAngle(0f);
            _plan = null;
        }

        void Play(Vector3 at, int index)
        {
            if (!Application.isPlaying) return;
            if (_voices == null)
            {
                _voices = new AudioSource[2];
                for (int i = 0; i < _voices.Length; i++)
                {
                    var go = new GameObject("BatidaDeArmario");
                    go.transform.SetParent(transform, false);
                    AudioSource source = go.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.spatialBlend = 1f;
                    source.rolloffMode = AudioRolloffMode.Logarithmic;
                    source.minDistance = 4f;
                    source.maxDistance = 70f;
                    source.dopplerLevel = 0f;
                    _voices[i] = source;
                }
                _clips = new AudioClip[3];
                for (int i = 0; i < _clips.Length; i++) _clips[i] = CreateBang(i);
            }

            AudioSource voice = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            voice.transform.position = at;
            voice.pitch = 0.94f + 0.12f * (OfficeElevators.Hash(_seed + 79, index) % 100u / 100f);
            voice.PlayOneShot(_clips[index % _clips.Length], 1f);
        }

        /// <summary>
        /// Porta de armário de chapa fina batendo, sintetizada (sem arquivo): o trinco, a pancada com os
        /// modos de uma chapa de aço (frequências que não são harmônicas) e o chacoalhar da fileira.
        /// <paramref name="variation"/> muda as frequências e o quique.
        /// </summary>
        public static AudioClip CreateBang(int variation)
        {
            const int rate = 44100;
            const float seconds = 0.9f;
            int n = (int)(rate * seconds);
            var data = new float[n];
            var rng = new System.Random(4100 + variation);
            float shift = 1f + 0.07f * (variation - 1);
            float[] modes = { 142f, 337f, 611f, 948f, 1387f, 1906f, 2650f };
            float[] decay = { 0.32f, 0.22f, 0.16f, 0.12f, 0.09f, 0.07f, 0.05f };
            float[] gain = { 0.9f, 0.75f, 0.6f, 0.45f, 0.35f, 0.25f, 0.18f };
            float[] phase = new float[modes.Length];
            for (int m = 0; m < modes.Length; m++) phase[m] = (float)rng.NextDouble() * 6.283f;

            const float latch = 0.0f, slam = 0.012f;
            float rattle = 0.07f + 0.03f * variation;
            float low = 0f;
            float alpha = 1f - Mathf.Exp(-2f * Mathf.PI * 2500f / rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float noise = (float)rng.NextDouble() * 2f - 1f;
                low += (noise - low) * alpha;
                float v = 0f;

                // O trinco: clique seco.
                if (t >= latch) v += noise * 0.35f * Mathf.Exp(-(t - latch) / 0.002f);

                // A pancada: modos da chapa e um baque grave do corpo do armário.
                float u = t - slam;
                if (u >= 0f)
                {
                    for (int m = 0; m < modes.Length; m++)
                        v += gain[m] * Mathf.Sin(2f * Mathf.PI * modes[m] * shift * u + phase[m]) * Mathf.Exp(-u / decay[m]);
                    v += Mathf.Sin(2f * Mathf.PI * (75f + 40f * Mathf.Exp(-u / 0.03f)) * u) * 1.1f * Mathf.Exp(-u / 0.06f);
                    v += low * 1.3f * Mathf.Exp(-u / 0.012f);
                }

                // As portas vizinhas chacoalhando: duas batidinhas mais fracas.
                for (int b = 0; b < 2; b++)
                {
                    float r = t - rattle - b * 0.045f;
                    if (r < 0f) continue;
                    float e = (b == 0 ? 0.35f : 0.18f) * Mathf.Exp(-r / 0.03f);
                    v += e * (Mathf.Sin(2f * Mathf.PI * 1210f * shift * r) + low * 1.5f);
                }
                data[i] = v;
            }

            float peak = 0f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            if (peak > 0f) for (int i = 0; i < n; i++) data[i] *= 0.9f / peak;
            for (int i = 0; i < 400; i++) data[n - 1 - i] *= i / 400f;

            var clip = AudioClip.Create("Armario_Bate_" + variation, n, 1, rate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }
    }
}
