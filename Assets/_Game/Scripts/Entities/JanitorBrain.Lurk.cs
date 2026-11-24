using BackRooms.Net;
using BackRooms.World;
using UnityEngine;

namespace BackRooms.Entities
{
    /// <summary>
    /// À espreita e caça (decisão de 23/11/2026, pedido do usuário jogando). Só o host decide, algumas
    /// vezes por segundo (<see cref="HuntInterval"/>), sobre o mapa do escuro (<see cref="JanitorDark"/>):
    ///
    /// • <b>Todos na luz:</b> ele fica na célula escura mais perto (a pé) de algum jogador, entre todas
    ///   as regiões escuras do andar, na beira dela, virado para o jogador. Dentro da mesma região vai
    ///   andando; quando outra região fica mais perto (uma célula ou mais, por
    ///   <see cref="JanitorBrain.regionSwitchSeconds"/>), afunda e sobe lá, a
    ///   <see cref="JanitorBrain.lurkMinCells"/> ou mais de todo jogador.
    /// • <b>Alguém pisou no escuro:</b> na região dele, caça na hora, sem precisar ver
    ///   (<see cref="Senses"/>), e não larga enquanto o jogador ficar no escuro; noutra região, afunda e
    ///   sobe nela depois de <see cref="JanitorBrain.huntSwitchSeconds"/>, a
    ///   <see cref="JanitorBrain.emergeMinCells"/> ou mais de todos e fora da vista. Voltando à luz, o
    ///   jogador deixa de ser sentido e ele volta à beira.
    /// </summary>
    public sealed partial class JanitorBrain
    {
        const float HuntInterval = 0.25f;
        const string HuntedKind = "no escuro do jogador";

        /// <summary>Host: à espreita (ninguém no escuro dele), na beira do escuro mais perto do jogador.</summary>
        public bool Lurking => Present && _lurking;

        bool _lurking = true;
        bool _mineFresh;
        PlayerNetwork _lure;
        PlayerNetwork _hunted;
        int _huntedX, _huntedZ;
        float _huntAt;
        float _switchSince = -1f;
        int[,] _mine;
        int[,] _theirs;

        protected override bool DrivesGoal => Present && _lurking;

        // Sente quem pisa numa célula escura da região em que ele está (a do mapa _mine).
        protected override bool Senses(PlayerNetwork player)
        {
            LevelNav nav = Nav;
            if (!Present || !_mineFresh || nav == null || player == null) return false;
            return nav.WorldToCell(player.transform.position, out int x, out int z) && nav.Standable(x, z) && _mine[x, z] >= 0;
        }

        /// <summary>
        /// Host, algumas vezes por segundo com ele no mapa: decide entre caçar (alguém no escuro dele),
        /// mudar de região (afunda; devolve verdadeiro) ou ir à beira do escuro mais perto do jogador.
        /// </summary>
        bool Hunt(LevelBuilder level, float now)
        {
            LevelNav nav = Nav;
            if (nav == null || !nav.WorldToCell(transform.position, out int cx, out int cz)) return false;
            if (_mine == null || _mine.GetLength(0) != nav.Data.Width || _mine.GetLength(1) != nav.Data.Height) _mine = nav.NewMap();
            nav.Distances(cx, cz, _mine);
            _mineFresh = true;
            if (!Survey(level))
            {
                _lurking = true;
                _lure = null;
                return false;
            }

            // Alguém no escuro desta região: caça (o Senses o dá como visto a cada escuta).
            if (_hunted != null && _mine[_huntedX, _huntedZ] >= 0)
            {
                _lurking = false;
                _switchSince = -1f;
                _lure = _hunted;
                return false;
            }

            _lurking = true;
            int here = NearestDark(_mine, out float hereCells, out PlayerNetwork hereLure);
            bool wantSwitch = _hunted != null;
            if (!wantSwitch)
            {
                int there = NearestDark(null, out float thereCells, out _);
                int w = nav.Data.Width;
                wantSwitch = there >= 0 && _mine[there % w, there / w] < 0 && (here < 0 || thereCells <= hereCells - 1f);
            }

            if (wantSwitch && !HoldEmerge)
            {
                if (_switchSince < 0f) _switchSince = now;
                float wait = _hunted != null ? huntSwitchSeconds : regionSwitchSeconds;
                // Só muda se o lugar novo é justo e serve: na região de quem está no escuro, ou (todos na
                // luz) uma célula ou mais mais perto do jogador do que a beira desta região.
                if (now - _switchSince >= wait && PickEmergeSpot(level, out Vector3 spot, out _, out int spotCells, out string kind) &&
                    nav.WorldToCell(spot, out int sx, out int sz) && _mine[sx, sz] < 0 &&
                    (_hunted != null ? kind == HuntedKind : here < 0 || spotCells + 1 <= hereCells))
                {
                    Vanish(level, now, _hunted != null ? "o jogador entrou noutro escuro" : "outro escuro ficou mais perto");
                    _nextEmerge = now + SinkSeconds + 0.1f;
                    return true;
                }
            }
            else _switchSince = -1f;

            // Vai à beira do escuro mais perto do jogador, sem sair da região.
            _lure = hereLure;
            if (here < 0 || hereLure == null || State == EntityState.Feed) return false;
            int width = nav.Data.Width;
            Vector3 goal = nav.ClampToCell(here % width, here / width, hereLure.transform.position, bodyRadius + 0.2f);
            if (State != EntityState.Investigate || !HasGoal || Flat(goal - Goal).magnitude > 0.3f)
                GoTo(goal, EntityState.Investigate, 60f);
            return false;
        }

        // Parado na beira, vira para o jogador que espreita.
        void FaceLure(float now)
        {
            if (!_lurking || _lure == null || !Arrived) return;
            Vector3 to = Flat(_lure.transform.position - transform.position);
            if (to.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to, Vector3.up), turnSpeed * 0.5f * Time.deltaTime);
        }

        /// <summary>
        /// Os passos de cada jogador (em _players/_playerMaps) e quem está numa célula escura que ele
        /// pisa (_hunted: de preferência na região dele, senão o mais perto). Falso sem jogador.
        /// </summary>
        bool Survey(LevelBuilder level)
        {
            _hunted = null;
            LevelNav nav = Nav;
            if (nav == null || !PlayerMaps(level)) return false;

            float best = float.MaxValue;
            foreach (PlayerNetwork player in _players)
            {
                Vector3 at = player.transform.position;
                if (at.y < -1f || !nav.WorldToCell(at, out int x, out int z) || !nav.Standable(x, z)) continue;
                bool mine = Present && _mineFresh && _mine[x, z] >= 0;
                float score = Flat(at - transform.position).magnitude + (mine ? 0f : 10000f);
                if (score >= best) continue;
                best = score;
                _hunted = player;
                _huntedX = x;
                _huntedZ = z;
            }
            return true;
        }

        /// <summary>
        /// A célula escura (que ele pisa) mais perto a pé de algum jogador; só entre as de
        /// <paramref name="within"/> (passos >= 0), se dado. <paramref name="cells"/> é a distância em
        /// células, com o desempate pela linha reta. -1 se não há.
        /// </summary>
        int NearestDark(int[,] within, out float cells, out PlayerNetwork lure)
        {
            cells = float.MaxValue;
            lure = null;
            LevelNav nav = Nav;
            LevelData data = nav.Data;
            int best = -1;
            for (int z = 0; z < data.Height; z++)
            {
                for (int x = 0; x < data.Width; x++)
                {
                    if (within != null && within[x, z] < 0) continue;
                    if (!nav.Standable(x, z)) continue;
                    Vector3 center = nav.CellCenter(x, z);
                    for (int p = 0; p < _players.Count; p++)
                    {
                        float d = CellsFrom(p, x, z, center);
                        if (d >= cells) continue;
                        cells = d;
                        lure = _players[p];
                        best = x + z * data.Width;
                    }
                }
            }
            return best;
        }

        // Distância do jogador p à célula: passos a pé (sem caminho, a linha reta) mais um centésimo
        // da linha reta, para desempatar sempre do mesmo jeito.
        float CellsFrom(int p, int x, int z, Vector3 center)
        {
            int steps = _playerMaps[p][x, z];
            float straight = Flat(center - _players[p].transform.position).magnitude / LevelBuilder.Cell;
            return (steps >= 0 ? steps : straight) + straight * 0.01f;
        }

        /// <summary>
        /// Onde aparecer agora (depois de <see cref="Survey"/>). Alguém no escuro: na região escura dele,
        /// a <see cref="emergeMinCells"/> ou mais de todos e fora da vista de todos (vai caçar na hora);
        /// sem lugar justo lá, ou todos na luz: no escuro mais perto de alguém, a
        /// <see cref="lurkMinCells"/> ou mais (à espreita; ver subir do chão no escuro é o susto).
        /// </summary>
        bool PickEmergeSpot(LevelBuilder level, out Vector3 spot, out PlayerNetwork lure, out int cells, out string kind)
        {
            kind = "";
            LevelNav nav = Nav;
            if (nav == null)
            {
                spot = Vector3.zero;
                lure = null;
                cells = -1;
                return false;
            }
            if (_hunted != null)
            {
                if (_theirs == null || _theirs.GetLength(0) != nav.Data.Width || _theirs.GetLength(1) != nav.Data.Height) _theirs = nav.NewMap();
                nav.Distances(_huntedX, _huntedZ, _theirs);
                if (FairCell(level, _theirs, emergeMinCells, true, out spot, out lure, out cells))
                {
                    kind = HuntedKind;
                    return true;
                }
            }
            bool someoneInDark = _hunted != null;
            if (FairCell(level, null, someoneInDark ? emergeMinCells : lurkMinCells, someoneInDark, out spot, out lure, out cells))
            {
                kind = someoneInDark ? "no escuro" : "à espreita";
                return true;
            }
            return false;
        }

        // A célula escura mais perto de algum jogador entre as que ficam a minCells ou mais (a pé, e
        // 3/4 disso em linha reta) de todos, e fora da vista de todos se pedido.
        bool FairCell(LevelBuilder level, int[,] within, int minCells, bool unseen, out Vector3 spot, out PlayerNetwork lure, out int cells)
        {
            spot = Vector3.zero;
            lure = null;
            cells = -1;
            LevelNav nav = Nav;
            LevelData data = nav.Data;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int z = 0; z < data.Height; z++)
            {
                for (int x = 0; x < data.Width; x++)
                {
                    if (within != null && within[x, z] < 0) continue;
                    if (!nav.Standable(x, z)) continue;

                    Vector3 center = nav.CellCenter(x, z);
                    float nearest = float.MaxValue;
                    PlayerNetwork near = null;
                    bool ok = true;
                    for (int p = 0; p < _players.Count && ok; p++)
                    {
                        float d = CellsFrom(p, x, z, center);
                        float straight = Flat(center - _players[p].transform.position).magnitude / LevelBuilder.Cell;
                        if (d < minCells || straight < minCells * 0.75f) ok = false;
                        else if (d < nearest)
                        {
                            nearest = d;
                            near = _players[p];
                        }
                    }
                    if (!ok || nearest >= bestScore) continue;
                    if (unseen && SeenByAnyone(center, level.transform)) continue;
                    bestScore = nearest;
                    best = x + z * data.Width;
                    lure = near;
                }
            }
            if (best < 0) return false;

            spot = nav.CellCenter(best % data.Width, best / data.Width);
            spot.y = level.transform.position.y;
            cells = Mathf.FloorToInt(bestScore);
            return true;
        }

        bool SeenByAnyone(Vector3 foot, Transform levelRoot)
        {
            foreach (PlayerNetwork player in _players)
                if (Seen(player, foot, levelRoot)) return true;
            return false;
        }
    }
}
