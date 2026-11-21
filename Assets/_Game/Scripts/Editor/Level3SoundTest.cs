using System.Collections.Generic;
using BackRooms.Audio;
using BackRooms.Entities;
using BackRooms.Net;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Teste em Play do som do Escritório (Level 3), com o relógio dos eventos forçado
    /// (<see cref="OfficeElevators.ForceSeconds"/>):
    /// - o ambiente (<see cref="OfficeAmbience"/>) não soa com o menu na tela e entra ao jogar; o sopro
    ///   e os reatores ficam baixos;
    /// - debaixo de um trecho que a onda de luzes apaga, as vozes de reator daquele trecho calam e
    ///   voltam quando as luzes voltam;
    /// - os armários batem (<see cref="LockerBangs"/>) na hora que o relógio manda, no corredor que a
    ///   conta escolhe para a seed (o mesmo pedido duas vezes), longe do jogador, 3 a 6 batidas, com as
    ///   portas soltas se abrindo; o barulho chega pela via genérica (<see cref="WorldNoise"/>) e a
    ///   entidade, se a fase tem, passa a investigar aquele ponto e anda para lá;
    /// - de volta ao menu, o ambiente cala.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level3SoundTest.BatchRun (sai com 0 se
    /// passou, 3 se falhou). Menu: BackRooms > Testar som do Level 3. Linha "[BackRooms] Teste de som
    /// do Level 3 PASSOU/FALHOU". Ouvir de verdade é com gente: exporte os WAV (EntitySounds).
    /// </summary>
    public static class Level3SoundTest
    {
        const string ActiveKey = "BackRooms.Level3SoundTest.Active";
        const string BatchKey = "BackRooms.Level3SoundTest.Batch";
        const string ExitCodeKey = "BackRooms.Level3SoundTest.ExitCode";
        const double Timeout = 150.0;
        const int Seed = 1998;

        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();
        static int _stage;
        static double _began, _stageStart, _lastTick;
        static PlayerNetwork _local;
        static LevelBuilder _level;
        static OfficeAmbience _ambience;
        static CeilingLight[] _run;
        static Vector3 _ear;
        static float _before;
        static LockerBangs.Plan _expected;
        static EntityBrain _entity;
        static Vector3 _noiseAt, _entityStart;
        static double _firstBangClock = -1.0;
        static float _maxAngle;
        static Probe _probe;
        static bool _finished;

        /// <summary>Ouvinte de teste: guarda o último barulho que chegou pela via genérica.</summary>
        sealed class Probe : INoiseListener
        {
            public int Heard;
            public Vector3 At;
            public float Radius;

            public void HearWorldNoise(Vector3 at, float radius)
            {
                Heard++;
                At = at;
                Radius = radius;
            }
        }

        [MenuItem("BackRooms/Testar som do Level 3")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(false);
        }

        public static void BatchRun() => Run(true);

        static void Run(bool batch)
        {
            if (!System.IO.File.Exists(Level3Setup.ScenePath)) Level3Setup.BuildScene();
            EditorSceneManager.OpenScene(Level3Setup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (SessionState.GetInt(ExitCodeKey, -1) >= 0)
            {
                EditorApplication.playModeStateChanged -= ExitAfterPlay;
                EditorApplication.playModeStateChanged += ExitAfterPlay;
                if (!EditorApplication.isPlayingOrWillChangePlaymode) ExitAfterPlay(PlayModeStateChange.EnteredEditMode);
            }

            if (!SessionState.GetBool(ActiveKey, false)) return;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (stackTrace != null && (stackTrace.Contains("UnityEditor.Search.") || stackTrace.Contains("Unity.AI."))) return;
            Errors.Add(message);
        }

        static double Now => EditorApplication.timeSinceStartup;

        static void Next()
        {
            _stage++;
            _stageStart = Now;
        }

        static bool StageTimedOut(double seconds) => Now - _stageStart > seconds;
        static double InStage => Now - _stageStart;

        static void Tick()
        {
            if (!EditorApplication.isPlaying || _finished) return;
            GameSession session = GameSession.Instance;
            if (session == null) return;

            if (_began <= 0.0) _began = _lastTick = Now;
            float dt = Mathf.Clamp((float)(Now - _lastTick), 0f, 0.1f);
            _lastTick = Now;

            if (Now - _began > Timeout)
            {
                Finish(false, $"tempo esgotado na etapa {_stage}");
                return;
            }

            switch (_stage)
            {
                case 0:
                {
                    // Menu na tela: se o ambiente já existe, está calado.
                    if (InStage < 1.5) break;
                    foreach (OfficeAmbience a in Object.FindObjectsByType<OfficeAmbience>(FindObjectsSortMode.None))
                        if (a.Wanted || a.Level > 0f) { Finish(false, $"com o menu na tela o ambiente quer soar (nível {a.Level:F2})"); return; }
                    Notes.Add("no menu o ambiente não soa");

                    LevelProgress.DevModeOverride = true;
                    EntityDirector.Paused = true;
                    session.fixedSeed = Seed;
                    session.Host(localOnly: true);
                    Next();
                    break;
                }

                case 1:
                {
                    if (session.Phase != SessionPhase.Playing) break;
                    _local = PlayerNetwork.Local;
                    _level = Object.FindAnyObjectByType<LevelBuilder>();
                    _ambience = OfficeAmbience.Instance;
                    if (_local == null || _level == null || _level.Data == null) break;
                    if (_ambience == null) { if (StageTimedOut(10)) Finish(false, "o Escritório não montou o som ambiente"); break; }
                    if (!_ambience.Wanted || _ambience.Level < 0.999f)
                    {
                        if (StageTimedOut(8)) Finish(false, $"jogando, o ambiente não entrou (quer={_ambience.Wanted}, nível={_ambience.Level:F2})");
                        break;
                    }
                    if (_ambience.AirNow <= 0f) { Finish(false, "o sopro do ar-condicionado está mudo jogando"); return; }
                    if (_ambience.AirNow > 0.1f || _ambience.ballastVolume > 0.15f || _ambience.ventVolume > 0.15f)
                    {
                        Finish(false, $"ambiente alto demais (sopro {_ambience.AirNow:F3}, reator {_ambience.ballastVolume:F2}, difusor {_ambience.ventVolume:F2})");
                        return;
                    }
                    Notes.Add($"jogando, o ambiente entrou baixo (sopro {_ambience.AirNow:F3}, {_ambience.LightCount} luminárias, {_ambience.VentCount} difusores)");

                    // Debaixo do meio do primeiro trecho da onda 0, antes dela.
                    OfficeLightWave wave = OfficeLightWave.Instance;
                    if (wave == null || wave.RunCount == 0) { Finish(false, "a fase não tem onda de luzes"); return; }
                    _run = wave.RunFor(0);
                    CeilingLight middle = _run[_run.Length / 2];
                    Vector3 under = middle.transform.position;
                    // A onda escolhida pelo host (perto do jogador) não é previsível: aqui ela segue o relógio.
                    OfficeLightWave.UseClock = true;
                    OfficeElevators.ForceSeconds = OfficeLightWave.FirstWave - 4.0;
                    _local.controller.Teleport(new Vector3(under.x, _level.transform.position.y + 0.05f, under.z), 0f);
                    Next();
                    break;
                }

                case 2:
                {
                    if (InStage < 2.0) break;
                    _ear = _ambience.ListenerPosition();
                    _before = _ambience.VolumeOn(_run);
                    if (_before < 0.5f) { Finish(false, $"debaixo do trecho aceso, os reatores dele quase não soam ({_before:F2}; vozes {_ambience.BallastVoicesOn()})"); return; }
                    // Trecho inteiro apagado (dentro do tempo escuro).
                    OfficeElevators.ForceSeconds = OfficeLightWave.FirstWave + OfficeLightWave.WarnSeconds + _run.Length * OfficeLightWave.StepSeconds + 1.0;
                    Next();
                    break;
                }

                case 3:
                {
                    if (InStage < 1.0) break;
                    int dark = 0;
                    foreach (CeilingLight light in _run) if (light != null && !light.IsLit) dark++;
                    if (dark < _run.Length - 1) { Finish(false, $"a onda forçada não apagou o trecho ({dark} de {_run.Length})"); return; }
                    float during = _ambience.VolumeOn(_run);
                    if (during > 0.05f || _ambience.AnyVoiceOnDarkLight())
                    {
                        Finish(false, $"com o trecho apagado os reatores dele ainda soam ({during:F2}, antes {_before:F2})");
                        return;
                    }
                    Notes.Add($"a onda apagou {dark} luminária(s) e os reatores do trecho calaram ({_before:F2} → {during:F2}; total ouvido {_ambience.BallastHeardAt(_ear):F2})");
                    OfficeElevators.ForceSeconds = OfficeLightWave.FirstWave + OfficeLightWave.Duration(_run.Length) + 3.0;
                    Next();
                    break;
                }

                case 4:
                {
                    if (InStage < 1.5) break;
                    float after = _ambience.VolumeOn(_run);
                    if (after < 0.5f) { Finish(false, $"as luzes voltaram e os reatores do trecho não ({after:F2})"); return; }
                    Notes.Add("as luzes voltaram e os reatores também");

                    // Armários: o jogador na cabine (abrigo), longe de tudo; o plano esperado do evento 0.
                    LevelData d = _level.Data;
                    Vector3 cab = _level.CellCenter(d.SpawnX, d.SpawnZ);
                    _local.controller.Teleport(new Vector3(cab.x, _level.transform.position.y + 0.05f, cab.z), 0f);
                    Next();
                    break;
                }

                case 5:
                {
                    if (InStage < 1.0) break;
                    LockerBangs bangs = LockerBangs.Instance;
                    if (bangs == null || bangs.RowCount == 0) { Finish(false, "a fase não tem armários que batem"); return; }
                    List<Vector3> players = LockerBangs.PlayerPositions();
                    _expected = bangs.Choose(0, players);
                    LockerBangs.Plan again = bangs.Choose(0, players);
                    if (_expected.Skipped) { Finish(false, $"o evento 0 não achou corredor longe do jogador ({bangs.RowCount} corredores)"); return; }
                    float nearest = float.MaxValue;
                    foreach (LockerDoor door in _expected.Doors) nearest = Mathf.Min(nearest, Flat(door.NoisePoint - players[0]));
                    if (again.Row != _expected.Row || again.Doors.Length != _expected.Doors.Length || again.Doors[0] != _expected.Doors[0])
                    {
                        Finish(false, "a mesma seed e as mesmas posições deram outro corredor");
                        return;
                    }
                    int count = _expected.Doors.Length;
                    if (count < LockerBangs.MinBangs || count > LockerBangs.MaxBangs) { Finish(false, $"{count} batidas (fora de 3 a 6)"); return; }
                    if (nearest < LockerBangs.FallbackDistance) { Finish(false, $"porta a {nearest:F1} m do jogador"); return; }
                    Notes.Add($"{bangs.RowCount} fileiras de armários; a escolhida fica a {nearest:F0} m do jogador");
                    _noiseAt = _expected.Doors[count - 1].NoisePoint;

                    _probe = new Probe();
                    WorldNoise.Register(_probe);

                    // A entidade (se a fase a tem), num corredor a 10–30 m do barulho, longe do jogador.
                    EntityDirector director = EntityDirector.Instance;
                    if (director != null)
                    {
                        Vector3 spot = EntitySpot(players[0]);
                        if (spot.x > -9000f)
                        {
                            _entity = director.SpawnAt(spot, _noiseAt - spot);
                            _entityStart = spot;
                        }
                    }

                    // O Ascensorista (o inimigo desta fase) só ouve no andar: chega agora por um poço.
                    if (_entity is AttendantBrain attendant) attendant.ForceArrival();
                    Next();
                    break;
                }

                case 6:
                {
                    if (_entity is AttendantBrain attendant && attendant.Phase != AttendantPhase.Out)
                    {
                        if (StageTimedOut(30)) Finish(false, $"o Ascensorista não saiu da cabine ({attendant.Phase})");
                        break;
                    }
                    if (_entity != null) Notes.Add($"inimigo da fase: {_entity.GetType().Name}, a {Flat(_entity.transform.position - _noiseAt):F0} m do ponto do barulho");
                    OfficeElevators.ForceSeconds = LockerBangs.Instance.StartOf(0) - 1.5;
                    Next();
                    break;
                }

                case 7:
                {
                    // O relógio anda no tempo real a partir de 1,5 s antes do evento.
                    LockerBangs bangs = LockerBangs.Instance;
                    OfficeElevators.ForceSeconds += dt;
                    if (bangs.BangsPlayed > 0 && _firstBangClock < 0.0) _firstBangClock = OfficeElevators.ForceSeconds;
                    if (bangs.Last != null && bangs.Last.Doors != null)
                        foreach (LockerDoor door in bangs.Last.Doors) _maxAngle = Mathf.Max(_maxAngle, door.Angle);
                    if (_entity != null && _entity.State == EntityState.Chase) { Finish(false, "a entidade caçou o jogador na cabine (abrigo)"); return; }

                    double end = _expected.Times[_expected.Times.Length - 1] + 1.0;
                    if (OfficeElevators.ForceSeconds < end) break;

                    LockerBangs.Plan played = bangs.Last;
                    if (played == null || played.Event != 0 || played.Row != _expected.Row) { Finish(false, $"o evento tocou em outro corredor ({played?.Row} em vez de {_expected.Row})"); return; }
                    if (bangs.BangsPlayed != _expected.Doors.Length) { Finish(false, $"{bangs.BangsPlayed} batidas tocadas de {_expected.Doors.Length}"); return; }
                    if (System.Math.Abs(_firstBangClock - _expected.Times[0]) > 0.25) { Finish(false, $"a primeira batida veio aos {_firstBangClock:F2} s; o relógio mandava {_expected.Times[0]:F2} s"); return; }
                    if (_maxAngle < 60f) { Finish(false, $"as portas soltas mal se mexeram ({_maxAngle:F0}°)"); return; }
                    if (bangs.NoisesSent != 1 || _probe.Heard != 1) { Finish(false, $"barulho na via genérica: mandados {bangs.NoisesSent}, ouvidos {_probe.Heard}"); return; }
                    if ((_probe.At - _noiseAt).magnitude > 0.01f || _probe.Radius < 30f) { Finish(false, $"o barulho chegou em {_probe.At} (raio {_probe.Radius}), não em {_noiseAt}"); return; }
                    Notes.Add($"os armários bateram {bangs.BangsPlayed} vezes aos {_expected.Times[0]:F1} s (a primeira a {_firstBangClock - _expected.Times[0]:+0.00;-0.00} s da hora), no corredor {played.Row} que a seed escolhe, a {Flat(_noiseAt - LockerBangs.PlayerPositions()[0]):F0} m do jogador; portas abriram até {_maxAngle:F0}°; o barulho chegou pela via genérica (raio {_probe.Radius:F0} m)");

                    if (_entity == null)
                    {
                        Notes.Add("a fase não tem inimigo pelo EntityDirector");
                        Next();
                        Next();
                        break;
                    }
                    float goal = Flat(_entity.Goal - _noiseAt);
                    if (_entity.State != EntityState.Investigate || goal > 3f)
                    {
                        Finish(false, $"o inimigo ({_entity.GetType().Name}) não foi investigar o barulho ({_entity.State}, alvo a {goal:F1} m dele, ele a {Flat(_entity.transform.position - _noiseAt):F0} m; {WorldNoise.LastHeardBy} ouvinte(s))");
                        return;
                    }
                    _before = Flat(_entity.transform.position - _noiseAt);
                    Next();
                    break;
                }

                case 8:
                {
                    float now = Flat(_entity.transform.position - _noiseAt);
                    if (_before - now < 2f)
                    {
                        if (StageTimedOut(10)) Finish(false, $"a entidade não andou para o barulho ({_before:F1} m → {now:F1} m, {_entity.State})");
                        break;
                    }
                    Notes.Add($"o inimigo ouviu pela via genérica, foi investigar o ponto ({_entity.State}) e andou {_before - now:F1} m para lá");
                    Next();
                    break;
                }

                case 9:
                    OfficeElevators.ForceSeconds = -1.0;
                    session.Leave();
                    Next();
                    break;

                case 10:
                {
                    OfficeAmbience a = OfficeAmbience.Instance;
                    if (session.Phase != SessionPhase.Menu || (a != null && (a.Wanted || a.Level > 0f)))
                    {
                        if (StageTimedOut(8)) Finish(false, $"de volta ao menu o ambiente não calou (fase {session.Phase}, quer={a?.Wanted}, nível={a?.Level:F2})");
                        break;
                    }
                    Notes.Add("de volta ao menu o ambiente calou");
                    Finish(true, string.Join("; ", Notes));
                    break;
                }
            }
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        // Corredor fora de abrigo a 10–30 m do barulho e a 15 m ou mais do jogador (o mais perto de 16 m do barulho).
        static Vector3 EntitySpot(Vector3 player)
        {
            LevelData d = _level.Data;
            Vector3 best = new Vector3(-9999f, 0f, 0f);
            float bestScore = float.MaxValue;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.Office[x, z] != OfficeCell.Corridor || d.IsShelter(x, z)) continue;
                    Vector3 c = _level.CellCenter(x, z);
                    float toNoise = Flat(c - _noiseAt);
                    if (toNoise < 10f || toNoise > 30f || Flat(c - player) < 15f) continue;
                    float score = Mathf.Abs(toNoise - 16f);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
            return best;
        }

        static void Finish(bool ok, string report)
        {
            if (_finished) return;
            _finished = true;
            OfficeElevators.ForceSeconds = -1.0;
            OfficeLightWave.UseClock = false;
            EntityDirector.Paused = false;
            if (_probe != null) WorldNoise.Unregister(_probe);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
            LevelProgress.DevModeOverride = null;

            if (Errors.Count > 0)
            {
                ok = false;
                report += $"; {Errors.Count} erro(s) no console, o primeiro: {Errors[0]}";
            }

            Debug.Log("[BackRooms] Teste de som do Level 3 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);

            if (SessionState.GetBool(BatchKey, false))
            {
                SessionState.SetInt(ExitCodeKey, ok ? 0 : 3);
                EditorApplication.playModeStateChanged -= ExitAfterPlay;
                EditorApplication.playModeStateChanged += ExitAfterPlay;
            }

            EditorApplication.ExitPlaymode();
        }

        static void ExitAfterPlay(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= ExitAfterPlay;
            int code = SessionState.GetInt(ExitCodeKey, 1);
            SessionState.EraseInt(ExitCodeKey);
            Level1Setup.ExitWithLevel0Open(code);
        }
    }
}
