using System;
using System.Collections.Generic;
using System.IO;
using BackRooms.Entities;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Onde caem as válvulas das Piscinas (<see cref="SwitchObjective"/>, tipo válvula) em várias
    /// seeds, fora do Play: monta a cena do Level 2 em cada seed e escreve, por seed, a válvula do
    /// seco (a quantas células do início, a pé) e a da água (ilha, parede sobre a funda ou poço;
    /// quantas células se nada no mínimo, em metros e em segundos de nado calmo a 1,26 m/s; a
    /// distância da rota ao covil do Undertow, que ouve nado à tona a ~11 m; e a do ralo mais perto).
    /// Fotos em Logs/Previews/valvulas: de onde se gira a válvula da água, da margem olhando para ela
    /// e a do seco.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level2ValveSurvey.BatchRun (-seeds A,B,C;
    /// padrão as 5 de conferência e mais 3). Não muda nada; sai com 0 se toda seed tem as duas
    /// válvulas e a da água exige nadar, 3 se não. Linha "[BackRooms] Válvulas do Level 2".
    /// </summary>
    public static class Level2ValveSurvey
    {
        static readonly int[] DefaultSeeds = { 1998, 7, 4242, 31337, 90210, 1, 42, 2026 };
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        const float SwimSpeed = 1.26f;   // 45% da caminhada (2,8 m/s)
        const float SurfaceHearing = 11f; // nado à tona, ouvido pelo Undertow (relato do afogado)

        [MenuItem("BackRooms/Fotografar válvulas do Level 2")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            bool ok = Survey(DefaultSeeds, out string report);
            Debug.Log("[BackRooms] Válvulas do Level 2 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
        }

        public static void BatchRun()
        {
            try
            {
                var seeds = new List<int>();
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++)
                {
                    if (args[i] == "-seed" && int.TryParse(args[i + 1], out int one)) seeds.Add(one);
                    if (args[i] == "-seeds")
                        foreach (string part in args[i + 1].Split(','))
                            if (int.TryParse(part.Trim(), out int s)) seeds.Add(s);
                }
                if (seeds.Count == 0) seeds.AddRange(DefaultSeeds);
                bool ok = Survey(seeds.ToArray(), out string report);
                Debug.Log("[BackRooms] Válvulas do Level 2 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
                Level1Setup.ExitWithLevel0Open(ok ? 0 : 3);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Level1Setup.ExitWithLevel0Open(1);
            }
        }

        static bool Survey(int[] seeds, out string report)
        {
            EditorSceneManager.OpenScene(Level2Setup.ScenePath, OpenSceneMode.Single);
            var builder = Object.FindFirstObjectByType<LevelBuilder>();
            if (builder == null || !builder.poolsEnabled) { report = "a cena do Level 2 não tem LevelBuilder com as Piscinas"; return false; }

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews", "valvulas"));
            Directory.CreateDirectory(folder);
            foreach (string old in Directory.GetFiles(folder, "*.png")) File.Delete(old);

            var problems = new List<string>();
            var lines = new List<string>();
            int seedBefore = builder.seed;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Camera camera = null;
            GameObject rig = null;
            if (prefab != null) rig = Level0Preview.CreateCameraRig(prefab, out camera);
            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            int photos = 0;

            try
            {
                if (camera != null) camera.targetTexture = target;
                foreach (int seed in seeds)
                {
                    builder.seed = seed;
                    builder.Rebuild();
                    Physics.SyncTransforms();
                    LevelData d = builder.Data;
                    var objective = builder.GetComponentInChildren<SwitchObjective>(true);
                    if (objective == null || objective.Count < 2) { problems.Add($"seed {seed}: {(objective == null ? 0 : objective.Count)} válvula(s)"); continue; }

                    UndertowDirector.FindLair(builder, out Vector3 lair);
                    var wells = new List<Vector3>();
                    for (int z = 0; z < d.Height; z++)
                        for (int x = 0; x < d.Width; x++)
                            if (!d.IsSolid(x, z) && d.Depth[x, z] == PoolDepth.Well) wells.Add(builder.CellCenter(x, z));

                    var parts = new List<string>();
                    for (int i = 0; i < objective.Count; i++)
                    {
                        Vector2Int c = objective.PointCell(i);
                        Vector3 p = objective.PointPosition(i);
                        string where = $"{objective.PointSpot(i)} ({c.x},{c.y}) {d.Depth[c.x, c.y]}, roda a {p.y:F2} m";
                        int swim = objective.PointSwim(i);
                        if (swim > 0)
                        {
                            float lairGap = float.MaxValue;
                            foreach (Vector2Int a in objective.PointApproach(i))
                            {
                                if (d.Depth[a.x, a.y] != PoolDepth.Deep && d.Depth[a.x, a.y] != PoolDepth.Well) continue;
                                Vector3 ac = builder.CellCenter(a.x, a.y);
                                lairGap = Mathf.Min(lairGap, new Vector2(ac.x - lair.x, ac.z - lair.z).magnitude);
                            }
                            float drain = float.MaxValue;
                            foreach (Vector3 w in wells) drain = Mathf.Min(drain, new Vector2(w.x - p.x, w.z - p.z).magnitude);
                            float meters = swim * LevelBuilder.Cell;
                            // Como se entra na água: escada (sem espirro andando) ou borda (pula: espirro, ouvido pelos canos).
                            IReadOnlyList<Vector2Int> route = objective.PointApproach(i);
                            string entry = "?";
                            for (int k = 1; k < route.Count; k++)
                            {
                                bool wet = d.Depth[route[k].x, route[k].y] == PoolDepth.Deep || d.Depth[route[k].x, route[k].y] == PoolDepth.Well;
                                if (!wet) continue;
                                WallSide side = SideTo(route[k - 1], route[k]);
                                entry = d.StairAt(route[k - 1].x, route[k - 1].y, side, out _) ? "entra por escada" : "entra pulando da borda";
                                break;
                            }
                            where += ", " + entry;
                            where += $", nado mínimo {swim} célula(s) ({meters:F0} m, ~{meters / SwimSpeed:F0} s calmo), rota a {lairGap:F0} m do covil" +
                                     (lairGap <= SurfaceHearing ? " (ouve o nado à tona)" : "") + $", ralo a {drain:F0} m";
                        }
                        else where += ", a pé";
                        parts.Add(where);
                    }

                    // A da água tem de exigir nadar; a do seco, não.
                    if (objective.WaterSpot == WaterValveSpot.Dry) problems.Add($"seed {seed}: as duas válvulas ficaram no seco");
                    else
                    {
                        if (objective.PointSwim(1) < 1) problems.Add($"seed {seed}: a válvula da água se alcança a pé");
                        if (objective.PointSwim(0) != 0) problems.Add($"seed {seed}: a válvula do seco exige nadar");
                    }
                    Vector2Int a0 = objective.PointCell(0), a1 = objective.PointCell(1);
                    if (Mathf.Abs(a0.x - a1.x) + Mathf.Abs(a0.y - a1.y) < 3) problems.Add($"seed {seed}: as válvulas estão a menos de 3 células");

                    lines.Add($"seed {seed} ({objective.WaterSpot}): " + string.Join("; ", parts));
                    if (camera != null) photos += Photos(builder, objective, rig, camera, target, readback, folder, seed);
                }
            }
            finally
            {
                builder.seed = seedBefore;
                builder.Rebuild();
                if (camera != null) camera.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                if (rig != null) Object.DestroyImmediate(rig);
                ShaderUtil.allowAsyncCompilation = asyncBefore;
            }

            foreach (string line in lines) Debug.Log("[BackRooms] Válvulas " + line);
            report = $"{seeds.Length} seeds, {photos} fotos em {folder}" + (problems.Count > 0 ? "; PROBLEMAS: " + string.Join("; ", problems) : "") +
                     " | " + string.Join(" | ", lines);
            return problems.Count == 0;
        }

        static WallSide SideTo(Vector2Int from, Vector2Int to) =>
            to.x > from.x ? WallSide.East : to.x < from.x ? WallSide.West : to.y > from.y ? WallSide.North : WallSide.South;

        // De onde se gira a válvula da água (à tona ou de pé na ilha), da margem olhando para ela e a do seco.
        static int Photos(LevelBuilder builder, SwitchObjective objective, GameObject rig, Camera camera, RenderTexture target, Texture2D readback, string folder, int seed)
        {
            var water = builder.GetComponentInChildren<PoolWater>();
            if (water != null) water.Prepare();
            float surface = water != null ? water.SurfaceY : builder.transform.position.y + LevelData.WaterLevel;
            int shots = 0;

            void Shot(string name, Vector3 eye, Vector3 at)
            {
                Vector3 look = at - eye;
                Vector3 flat = new Vector3(look.x, 0f, look.z);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
                float eyeHeight = camera.transform.position.y - rig.transform.position.y;
                rig.transform.SetPositionAndRotation(eye - Vector3.up * eyeHeight, Quaternion.LookRotation(flat.normalized, Vector3.up));
                camera.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(look.y, flat.magnitude) * Mathf.Rad2Deg, 0f, 0f);
                if (water != null)
                {
                    water.LocalUnderwater = eye.y < surface;
                    water.Snap(eye);
                }
                Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, $"valvula_s{seed}_{name}.png"));
                shots++;
            }

            for (int i = 0; i < objective.Count; i++)
            {
                Vector3 p = objective.PointPosition(i), f = objective.PointFacing(i);
                Vector3 front = p + f * 0.9f;
                // Quem gira: à tona (olhos 25 cm acima da água) ou de pé (1,65 m acima do piso).
                float eyeY = objective.PointInWater(i) ? surface + 0.25f : objective.PointFloor(i) + 1.65f;
                Shot(i == 0 ? "seco" : "agua_girando", new Vector3(front.x, eyeY, front.z), p);
                if (i == 0) continue;

                // Da margem: a última célula a pé da rota, olhando para a válvula por cima da água.
                IReadOnlyList<Vector2Int> route = objective.PointApproach(i);
                if (route.Count > 0)
                {
                    Vector3 shore = builder.CellCenter(route[0].x, route[0].y);
                    Shot("agua_da_margem", shore + Vector3.up * 1.65f, p);
                }
                // De longe, à tona no meio da travessia (se a rota tem mais de uma célula d'água).
                if (route.Count > 2)
                {
                    Vector2Int mid = route[route.Count / 2];
                    Vector3 m = builder.CellCenter(mid.x, mid.y);
                    Shot("agua_nadando", new Vector3(m.x, surface + 0.25f, m.z), p);
                }
            }
            if (water != null) water.LocalUnderwater = false;
            return shots;
        }
    }
}
