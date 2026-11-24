using System.Collections.Generic;
using BackRooms.Net;
using BackRooms.World;
using Unity.Netcode;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// A onda de luzes que persegue e os interruptores (decisão de 21/11/2026). Só o host decide:
    ///
    /// • <b>A onda persegue:</b> a cada <see cref="OfficeLightWave.WaveEvery"/> s (com folga), o host
    ///   escolhe o trecho de corredor em que um jogador está (ou o mais perto dele), revezando entre os
    ///   jogadores, e põe a passagem em <see cref="_plans"/> com a hora de início. A lista vai pela rede
    ///   (quem entra depois recebe) e cada máquina apaga as mesmas luminárias na mesma hora.
    /// • <b>Ele nasce perto:</b> quando o trecho apaga, o Zelador (escondido, ou tirado de onde estava)
    ///   sobe do chão na ponta do trecho mais perto do jogador, nunca a menos de <see cref="fairCells"/>
    ///   células de caminhada de ninguém.
    /// • <b>Interruptores:</b> um por trecho (<see cref="LightSwitches"/>). Religar acende o trecho na hora
    ///   (o Zelador dali some); o estalo é alto e chama quem ouve (<see cref="WorldNoise"/>); depois o
    ///   interruptor recarrega por <see cref="switchRechargeSeconds"/>. Com o alarme disparado, não
    ///   funcionam: a fuga final é no escuro.
    /// </summary>
    public sealed partial class JanitorBrain
    {
        [Header("Zelador: onda e interruptores")]
        [Tooltip("Nasce na ponta do trecho a pelo menos isto de caminhada de todo jogador, em células de 4 m.")]
        public int fairCells = 3;
        [Tooltip("Segundos até o interruptor funcionar de novo depois de usado.")]
        public float switchRechargeSeconds = 25f;
        [Tooltip("Até onde se ouve o estalo do interruptor, em metros.")]
        public float switchNoiseRadius = 45f;
        [Tooltip("Folga sorteada no intervalo entre ondas, em segundos (para não virar relógio).")]
        public float waveJitter = 6f;

        /// <summary>Contadores e últimos resultados (host), para os testes.</summary>
        public int WavesPlanned { get; private set; }
        public int SwitchFlips { get; private set; }
        /// <summary>Pedidos de interruptor que chegaram ao host (aceitos ou recusados).</summary>
        public int FlipRequests { get; private set; }
        public string LastSwitchRefusal { get; private set; } = "";
        public int LastWaveRun { get; private set; } = -1;
        /// <summary>Como foi a última aparição: "na onda" (ponta do trecho), "no escuro do jogador" (na região escura de quem pisou no escuro), "no escuro" (outro escuro, alguém no escuro) ou "à espreita" (todos na luz).</summary>
        public string LastEmergeKind { get; private set; } = "";
        /// <summary>Distância de caminhada (células) da última aparição até o jogador mais perto.</summary>
        public int LastEmergeCells { get; private set; } = -1;
        /// <summary>Diagnóstico: o que aconteceu com o último trecho que apagou (host).</summary>
        public string WaveLog { get; private set; } = "";

        readonly NetworkList<WavePlan> _plans = new NetworkList<WavePlan>();
        readonly NetworkList<double> _switchReady = new NetworkList<double>();
        readonly NetworkVariable<int> _switchHash = new NetworkVariable<int>();

        /// <summary>O resumo das posições dos interruptores no host (o cliente compara com os dele).</summary>
        public int HostSwitchHash => _switchHash.Value;
        readonly List<WavePlan> _planCache = new List<WavePlan>();
        readonly HashSet<long> _darkHandled = new HashSet<long>();
        readonly List<Vector2Int> _runCells = new List<Vector2Int>();
        double _nextWaveAt = OfficeLightWave.FirstWave;
        int _waveTurn;
        int _pendingRun = -1;
        string _spotWhy = "";
        float _pendingFullAt;
        float _pendingSince;

        System.Func<IReadOnlyList<WavePlan>> _planner;

        void WavesSpawn()
        {
            _plans.OnListChanged += OnPlansChanged;
            _switchReady.OnListChanged += OnSwitchChanged;
            RefreshPlans();
            _planner = () => _planCache;
            OfficeLightWave.Planner = _planner;
        }

        void WavesDespawn()
        {
            _plans.OnListChanged -= OnPlansChanged;
            _switchReady.OnListChanged -= OnSwitchChanged;
            if (OfficeLightWave.Planner == _planner) OfficeLightWave.Planner = null;
        }

        void OnPlansChanged(NetworkListEvent<WavePlan> change) => RefreshPlans();

        void RefreshPlans()
        {
            _planCache.Clear();
            foreach (WavePlan plan in _plans) _planCache.Add(plan);
        }

        // Em todas as máquinas: o interruptor foi usado (a hora de pronto avançou): estalo e barulho.
        void OnSwitchChanged(NetworkListEvent<double> change)
        {
            if (change.Type != NetworkListEvent<double>.EventType.Value || change.Value <= change.PreviousValue) return;
            LightSwitches.Instance?.Flipped(change.Index, switchNoiseRadius);
        }

        static double Clock() => OfficeElevators.Clock();

        static bool AlarmOn()
        {
            SwitchObjective objective = SwitchObjective.Instance;
            return objective != null && objective.Kind == SwitchKind.FireAlarm && objective.Complete;
        }

        /// <summary>As passagens da onda agora (em qualquer máquina).</summary>
        public IReadOnlyList<WavePlan> Plans => _planCache;

        /// <summary>Hora da sessão em que o interruptor do trecho volta a funcionar (em qualquer máquina).</summary>
        public double SwitchReadyAt(int run) => run >= 0 && run < _switchReady.Count ? _switchReady[run] : 0.0;

        /// <summary>O trecho está piscando ou apagado pela onda agora (em qualquer máquina)?</summary>
        public bool RunActive(int run, double t)
        {
            OfficeLightWave wave = OfficeLightWave.Instance;
            if (wave == null) return false;
            foreach (WavePlan plan in _planCache)
                if (plan.Run == run && t >= plan.Start && t < wave.End(plan)) return true;
            return false;
        }

        // ---------------------------------------------------------------- Host: escolher os trechos

        void PlanWaves(LevelBuilder level, float now, bool paused)
        {
            OfficeLightWave wave = OfficeLightWave.Instance;
            if (wave == null) return;
            while (_switchReady.Count < wave.RunCount) _switchReady.Add(0.0);

            if (LightSwitches.Instance != null && _switchHash.Value != LightSwitches.Instance.Hash)
                _switchHash.Value = LightSwitches.Instance.Hash;

            double t = Clock();
            for (int i = _plans.Count - 1; i >= 0; i--)
                if (t > wave.End(_plans[i]) + 1.0) _plans.RemoveAt(i);

            // Um trecho acabou de apagar: o Zelador vai para lá (se a ponta servir).
            foreach (WavePlan plan in _planCache)
            {
                long key = (long)(plan.Start * 1000.0) * 64 + plan.Run;
                if (!wave.Dark(plan, t) || _darkHandled.Contains(key)) continue;
                _darkHandled.Add(key);
                _pendingRun = plan.Run;
                _pendingSince = now;
                // A cascata leva um instante: só se desiste da ponta depois do trecho inteiro apagado.
                _pendingFullAt = now + wave.RunLamps(plan.Run).Count * OfficeLightWave.StepSeconds + 0.3f;
                WaveLog = $"trecho {plan.Run} apagou (presente={Present})";
            }

            // A onda é do mundo: segue mesmo com os inimigos pausados (só o Zelador não aparece).
            if (OfficeLightWave.Paused || AlarmOn() || t < _nextWaveAt) return;
            _nextWaveAt = t + OfficeLightWave.WaveEvery + Random.Range(-waveJitter, waveJitter);
            PlanAround(t, wave);
        }

        // Um trecho perto de cada jogador (até dois), revezando quem é o "alvo".
        void PlanAround(double t, OfficeLightWave wave)
        {
            _players.Clear();
            foreach (PlayerNetwork player in PlayerNetwork.All)
            {
                if (player == null || !player.IsSpawned || !player.InGame) continue;
                if (JanitorDark.Cell(player.transform.position, out int px, out int pz) && JanitorDark.Level != null &&
                    JanitorDark.Level.Data.IsShelter(px, pz)) continue;
                _players.Add(player);
            }

            if (_players.Count == 0)
            {
                if (wave.RunCount > 0) StartWave(Random.Range(0, wave.RunCount));
                return;
            }

            int count = Mathf.Min(2, _players.Count);
            var used = new HashSet<int>();
            for (int k = 0; k < count; k++)
            {
                PlayerNetwork target = _players[(_waveTurn + k) % _players.Count];
                int run = RunNear(target.transform.position, wave, used, t);
                if (run < 0) continue;
                used.Add(run);
                StartWave(run);
            }
            _waveTurn++;
        }

        // O trecho com luminária mais perto do ponto (o do próprio jogador, se ele está num), fora os
        // usados e os que já estão passando.
        int RunNear(Vector3 at, OfficeLightWave wave, HashSet<int> used, double t)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int r = 0; r < wave.RunCount; r++)
            {
                if (used.Contains(r) || RunActive(r, t)) continue;
                foreach (CeilingLight lamp in wave.RunLamps(r))
                {
                    if (lamp == null) continue;
                    Vector3 d = lamp.transform.position - at;
                    d.y = 0f;
                    if (d.magnitude >= bestDistance) continue;
                    bestDistance = d.magnitude;
                    best = r;
                }
            }
            return best;
        }

        /// <summary>Host: começa uma passagem da onda no trecho dado, agora (aviso, escuro, volta).</summary>
        public void StartWave(int run)
        {
            if (!IsServer || OfficeLightWave.Instance == null || run < 0 || run >= OfficeLightWave.Instance.RunCount) return;
            _plans.Add(new WavePlan { Run = (short)run, Reverse = Random.value < 0.5f, Start = Clock(), Cut = WavePlan.Never });
            RefreshPlans();
            WavesPlanned++;
            LastWaveRun = run;
        }

        /// <summary>Host, para os testes: começa a passagem no trecho perto do jogador; devolve o trecho.</summary>
        public int StartWaveNear(PlayerNetwork player)
        {
            OfficeLightWave wave = OfficeLightWave.Instance;
            if (!IsServer || wave == null || player == null) return -1;
            int run = RunNear(player.transform.position, wave, new HashSet<int>(), Clock());
            StartWave(run);
            return run;
        }

        /// <summary>Host, para os testes: a próxima onda automática fica para esta hora da sessão.</summary>
        public void ScheduleWave(double at) => _nextWaveAt = at;

        // ---------------------------------------------------------------- Host: nascer na ponta

        /// <summary>As células (com piso) do trecho da onda.</summary>
        public void RunCells(int run, List<Vector2Int> cells)
        {
            cells.Clear();
            OfficeLightWave wave = OfficeLightWave.Instance;
            if (wave == null) return;
            foreach (CeilingLight lamp in wave.RunLamps(run))
                if (lamp != null && JanitorDark.Cell(lamp.transform.position, out int x, out int z))
                {
                    var c = new Vector2Int(x, z);
                    if (!cells.Contains(c)) cells.Add(c);
                }
        }

        // Chamado a cada quadro (host) com o trecho que acabou de apagar: tira o Zelador de outro lugar
        // e o faz subir na ponta. Devolve verdadeiro se mexeu nele neste quadro.
        bool HandlePendingWave(LevelBuilder level, float now, bool paused)
        {
            if (_pendingRun < 0) return false;
            if (paused || HoldEmerge || now - _pendingSince > 8f)
            {
                WaveLog += paused ? "; pausado" : HoldEmerge ? "; segurado" : $"; desistiu em 8 s ({_spotWhy})";
                _pendingRun = -1;
                return false;
            }

            if (Present)
            {
                RunCells(_pendingRun, _runCells);
                if (JanitorDark.Cell(transform.position, out int cx, out int cz) && _runCells.Contains(new Vector2Int(cx, cz)))
                {
                    _pendingRun = -1;
                    return false;
                }
                // Está noutro escuro: afunda lá e sobe aqui, se a ponta do trecho servir (senão fica onde está).
                if (!FindRunSpot(level, _pendingRun, out _, out _, out _))
                {
                    if (now < _pendingFullAt) return false;
                    WaveLog += $"; estava noutro escuro e a ponta não serve ({_spotWhy})";
                    _pendingRun = -1;
                    return false;
                }
                WaveLog += "; afundou noutro escuro";
                Vanish(level, now, "a onda o chamou");
                _nextEmerge = now + SinkSeconds + 0.1f;
                return true;
            }

            if (now < _nextEmerge && now < _pendingSince + SinkSeconds + 0.2f) return true;
            if (TryEmergeInRun(level, _pendingRun))
            {
                WaveLog += "; subiu na ponta";
                _pendingRun = -1;
            }
            return true;
        }

        // A célula escura do trecho mais perto (andando) do jogador mais perto, sem ficar a menos de
        // fairCells de nenhum jogador. Vista não importa: no escuro, vê-lo subir do chão é o susto.
        bool TryEmergeInRun(LevelBuilder level, int run)
        {
            if (!FindRunSpot(level, run, out Vector3 spot, out PlayerNetwork bestTarget, out int bestSteps)) return false;
            Vector3 look = bestTarget != null ? bestTarget.transform.position - spot : Vector3.forward;
            ManifestAt(spot, look);
            LastEmergeKind = "na onda";
            LastEmergeCells = bestSteps;
            if (bestTarget != null) Alert(bestTarget.transform.position);
            return true;
        }

        /// <summary>
        /// A célula do trecho onde ele sobe: escura, a <see cref="fairCells"/> ou mais de caminhada de
        /// TODO jogador e, entre essas, a mais perto de quem está fora de abrigo (o alvo).
        /// </summary>
        bool FindRunSpot(LevelBuilder level, int run, out Vector3 spot, out PlayerNetwork bestTarget, out int bestSteps)
        {
            spot = Vector3.zero;
            bestTarget = null;
            bestSteps = int.MaxValue;
            LevelData data = level.Data;
            if (_free == null || _free.Data != data) _free = new LevelNav(level);
            RunCells(run, _runCells);
            _spotWhy = $"{_runCells.Count} células";
            if (_runCells.Count == 0 || !PlayerMaps(level)) return false;

            int best = -1, notDark = 0, unfair = 0, noTarget = 0;
            for (int i = 0; i < _runCells.Count; i++)
            {
                Vector2Int c = _runCells[i];
                if (!JanitorDark.IsDark(c.x, c.y) || data.IsShelter(c.x, c.y) || data.Pit[c.x, c.y] || data.IsBarricade(c.x, c.y))
                {
                    notDark++;
                    continue;
                }
                Vector3 center = _free.CellCenter(c.x, c.y);
                int nearest = int.MaxValue;
                PlayerNetwork target = null;
                bool ok = true;
                for (int p = 0; p < _players.Count && ok; p++)
                {
                    int steps = _playerMaps[p][c.x, c.y];
                    float straight = Vector3.Distance(Flat(center), Flat(_players[p].transform.position)) / LevelBuilder.Cell;
                    int cells = steps >= 0 ? steps : Mathf.CeilToInt(straight);
                    if (cells < fairCells || straight < fairCells * 0.75f) ok = false;
                    // O alvo é quem está fora de abrigo (na cabine ou numa salinha ele não chega).
                    if (JanitorDark.Cell(_players[p].transform.position, out int px, out int pz) && data.IsShelter(px, pz)) continue;
                    if (cells < nearest)
                    {
                        nearest = cells;
                        target = _players[p];
                    }
                }
                if (!ok) unfair++;
                else if (target == null) noTarget++;
                if (!ok || nearest >= bestSteps) continue;
                bestSteps = nearest;
                best = i;
                bestTarget = target;
            }
            _spotWhy = $"{_runCells.Count} células: {notDark} não escuras, {unfair} perto demais de alguém, {noTarget} sem alvo fora de abrigo, {_players.Count} jogador(es)";
            if (best < 0 || bestTarget == null) return false;

            spot = _free.CellCenter(_runCells[best].x, _runCells[best].y);
            spot.y = level.transform.position.y;
            return true;
        }

        // Distâncias a pé de cada jogador em jogo (em _players/_playerMaps). Falso sem jogador.
        bool PlayerMaps(LevelBuilder level)
        {
            LevelData data = level.Data;
            if (_free == null || _free.Data != data) _free = new LevelNav(level);
            _players.Clear();
            foreach (PlayerNetwork player in PlayerNetwork.All)
                if (player != null && player.IsSpawned && player.InGame) _players.Add(player);
            if (_players.Count == 0) return false;

            while (_playerMaps.Count < _players.Count) _playerMaps.Add(_free.NewMap());
            for (int p = 0; p < _players.Count; p++)
            {
                int[,] map = _playerMaps[p];
                if (_free.WorldToCell(_players[p].transform.position, out int px, out int pz) && WalkFrom(px, pz, map)) continue;
                for (int z = 0; z < data.Height; z++)
                    for (int x = 0; x < data.Width; x++) map[x, z] = -1;
            }
            return true;
        }

        // ---------------------------------------------------------------- Interruptores

        /// <summary>
        /// Em qualquer máquina: o jogador desta máquina usou o interruptor do trecho. No host decide na
        /// hora; no cliente pede ao host.
        /// </summary>
        public void RequestFlip(int run, Vector3 who)
        {
            if (!IsSpawned) return;
            if (IsServer) HostFlip(run, who);
            else FlipSwitchRpc(run, who);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void FlipSwitchRpc(int run, Vector3 who) => HostFlip(run, who);

        /// <summary>
        /// Host: religa o trecho se pode (perto do interruptor, sem alarme, carregado e com o trecho
        /// piscando ou escuro). Devolve se religou; o motivo da recusa fica em <see cref="LastSwitchRefusal"/>.
        /// </summary>
        public bool HostFlip(int run, Vector3 who)
        {
            if (!IsServer) return false;
            FlipRequests++;
            LightSwitches switches = LightSwitches.Instance;
            OfficeLightWave wave = OfficeLightWave.Instance;
            double t = Clock();

            if (switches == null || wave == null || !switches.Has(run)) return Refuse("sem interruptor");
            if (Vector3.Distance(who, switches.PositionOf(run)) > switches.reach + 1.5f) return Refuse("longe do interruptor");
            if (AlarmOn()) return Refuse("sem energia (alarme)");
            if (t < SwitchReadyAt(run)) return Refuse("recarregando");
            if (!RunActive(run, t)) return Refuse("o trecho já está aceso");

            for (int i = 0; i < _plans.Count; i++)
            {
                WavePlan plan = _plans[i];
                if (plan.Run != run || t < plan.Start || t >= wave.End(plan)) continue;
                plan.Cut = t;
                _plans[i] = plan;
            }
            RefreshPlans();
            while (_switchReady.Count <= run) _switchReady.Add(0.0);
            _switchReady[run] = t + switchRechargeSeconds;
            SwitchFlips++;
            LastSwitchRefusal = "";
            if (_pendingRun == run) _pendingRun = -1;
            // Ele estava no trecho: a luz volta sobre ele e ele some (o HoldStill confere a cada quadro).
            return true;
        }

        bool Refuse(string why)
        {
            LastSwitchRefusal = why;
            return false;
        }
    }
}
