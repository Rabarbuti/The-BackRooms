using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Fotos do Level 2 (As Piscinas) para conferir a forma: planta vista de cima sem os tetos,
    /// duas fotos na altura dos olhos por sala (da porta para dentro e do meio para a parede mais
    /// longa) e closes de arco, vigia, escada entrando na água, poço redondo, claraboia e debaixo
    /// d'água. Saem em Logs/Previews/level2_*.png, em 1280 x 720.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.Level2Preview.BatchCapture
    /// [-seed N] (padrão 1998). Monta a cena (Level2Setup.BuildScene), reconstrói com a seed e
    /// escreve "[BackRooms] Fotos do Level 2: ..." no log. Sai com 0 se tirou as fotos.
    /// </summary>
    public static class Level2Preview
    {
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        const float Eye = 1.65f;

        [MenuItem("BackRooms/Fotografar Level 2")]
        public static void CaptureFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string report = Capture(1998, out _);
            Debug.Log("[BackRooms] Fotos do Level 2: " + report);
        }

        public static void BatchCapture()
        {
            int code = 1;
            try
            {
                string report = Capture(SeedArgument(1998), out bool ok);
                Debug.Log("[BackRooms] Fotos do Level 2: " + report);
                code = ok ? 0 : 3;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            Level1Setup.ExitWithLevel0Open(code);
        }

        /// <summary>
        /// Linha de comando, modo enxuto: -executeMethod BackRooms.EditorTools.Level2Preview.BatchCloseups
        /// [-seed N]. Só os closes do acabamento (tijolo de vidro, janelão, poço redondo, teto de
        /// vidro Full, manchas, marcações, uma sala com elas e a vista longa de uma raia), em
        /// Logs/Previews/level2_s{seed}_*.png, sem apagar as outras fotos. Também procura, nas seeds
        /// de conferência, quais têm poço redondo, tijolo de vidro e janelão.
        /// </summary>
        public static void BatchCloseups()
        {
            int code = 1;
            try
            {
                string report = Closeups(SeedArgument(1998), out bool ok);
                Debug.Log("[BackRooms] Closes do Level 2: " + report);
                code = ok ? 0 : 3;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            Level1Setup.ExitWithLevel0Open(code);
        }

        static readonly int[] ScanSeeds = { 1998, 7, 4242, 31337, 90210, 5, 21 };

        static string Closeups(int seed, out bool ok)
        {
            ok = false;
            WallSide[] sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };

            // Que seed tem o quê (só a planta: rápido).
            var scan = new List<string>();
            foreach (int s in ScanSeeds)
            {
                LevelData plan = LevelGenerator.Generate(s, 16, 16, false, false, true);
                var wells = new List<string>();
                int glass = 0, window = 0, full = 0;
                for (int z = 0; z < plan.Height; z++)
                    for (int x = 0; x < plan.Width; x++)
                    {
                        if (LevelBuilder.IsRoundWell(plan, x, z)) wells.Add($"({x},{z}) {plan.Rooms[plan.Room[x, z]].Kind}");
                        if (plan.IsSolid(x, z)) continue;
                        if (plan.Sky[x, z] == PoolSky.Full) full++;
                        foreach (WallSide side in sides)
                        {
                            if (plan.EdgeAt(x, z, side) == PoolEdge.GlassBlock) glass++;
                            if (plan.EdgeAt(x, z, side) == PoolEdge.Window) window++;
                        }
                    }
                scan.Add($"seed {s}: poço redondo {(wells.Count > 0 ? string.Join(" ", wells) : "nenhum")}, tijolo de vidro {glass}, janelão {window}, células Full {full}");
            }
            Debug.Log("[BackRooms] Procura nas seeds:\n" + string.Join("\n", scan));

            Level2Setup.BuildScene();
            EditorSceneManager.OpenScene(Level2Setup.ScenePath, OpenSceneMode.Single);
            var builder = Object.FindFirstObjectByType<LevelBuilder>();
            if (builder == null || !builder.poolsEnabled) return "a cena não tem LevelBuilder com as Piscinas ligadas";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) return "sem o prefab do jogador";

            builder.seed = seed;
            builder.Rebuild();
            LevelData d = builder.Data;
            WakeWater(builder);
            foreach (HDAdditionalLightData light in builder.GetComponentsInChildren<HDAdditionalLightData>(true))
                light.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
            Debug.Log($"[BackRooms] Seed {seed}: {builder.PoolStats}\nManchas e marcações: {builder.PoolDecalLog}");
            string ascii = PlanText(d);
            if (!string.IsNullOrEmpty(ascii)) Debug.Log($"[BackRooms] Planta das Piscinas, seed {seed}:\n{ascii}");

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews"));
            Directory.CreateDirectory(folder);
            foreach (string old in Directory.GetFiles(folder, $"level2_s{seed}_*.png")) File.Delete(old);

            GameObject rig = Level0Preview.CreateCameraRig(prefab, out Camera camera);
            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var names = new List<string>();
            var missing = new List<string>();
            var camcorder = Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();
            var water = builder.GetComponentInChildren<PoolWater>();
            Volume under = null;
            if (water != null)
                foreach (Volume v in water.GetComponentsInChildren<Volume>(true))
                    if (v.gameObject.name == "DebaixoDagua") under = v;
            float underBefore = under != null ? under.weight : 0f;

            try
            {
                if (camcorder != null) camcorder.Draw(true, 61.4f, 61.4f, 0.95f, false);
                camera.targetTexture = target;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 150f);

                void Shot(string name, Vector3 foot, Vector3 look, float pitch, bool underwater = false)
                {
                    look.y = 0f;
                    if (look.sqrMagnitude < 1e-6f) look = Vector3.forward;
                    if (under != null) under.weight = underwater ? 1f : underBefore;
                    if (camcorder != null) camcorder.Draw(true, 61.4f, 61.4f, 0.95f, false, underwater: underwater, air: underwater ? 0.6f : 1f);
                    rig.transform.SetPositionAndRotation(builder.transform.TransformPoint(foot), Quaternion.LookRotation(look.normalized, Vector3.up));
                    camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                    string file = $"level2_s{seed}_{name}.png";
                    Shoot(camera, target, readback, Path.Combine(folder, file));
                    names.Add(file);
                }

                // Olhando de onde está o pé para um ponto (pitch para baixo positivo).
                float PitchTo(Vector3 foot, Vector3 point)
                {
                    Vector3 flat = point - foot;
                    flat.y = 0f;
                    return Mathf.Clamp(Mathf.Atan2(foot.y + Eye - point.y, Mathf.Max(0.3f, flat.magnitude)) * Mathf.Rad2Deg, -70f, 70f);
                }

                (int x, int z) CellOf(Vector3 p) => (Mathf.Clamp(Mathf.FloorToInt(p.x / LevelBuilder.Cell), 0, d.Width - 1),
                                                     Mathf.Clamp(Mathf.FloorToInt(p.z / LevelBuilder.Cell), 0, d.Height - 1));

                // Tijolo de vidro e janelão, de frente, a 3 m.
                foreach (PoolEdge kind in new[] { PoolEdge.GlassBlock, PoolEdge.Window })
                {
                    string name = kind == PoolEdge.Window ? "janelao" : "tijolo_de_vidro";
                    bool found = false;
                    for (int z = 0; z < d.Height && !found; z++)
                        for (int x = 0; x < d.Width && !found; x++)
                        {
                            if (d.IsSolid(x, z)) continue;
                            foreach (WallSide side in sides)
                            {
                                if (d.EdgeAt(x, z, side) != kind) continue;
                                Vector3 dir = Dir(side);
                                Vector3 foot = CellCenter(d, x, z) + dir * (LevelBuilder.Cell * 0.5f - 3.2f);
                                foot.y = StandHeight(d, x, z);
                                Shot(name, foot, dir, -6f);
                                Vector3 oblique = Vector3.Cross(Vector3.up, dir) * 1.6f;
                                Shot(name + "_obliquo", foot + oblique, dir - oblique * 0.35f, -2f);
                                found = true;
                                break;
                            }
                        }
                    if (!found) missing.Add(name);
                }

                // Poço redondo: da superfície, na funda ao lado, e de dentro d'água, olhando o furo.
                bool well = false;
                for (int z = 0; z < d.Height && !well; z++)
                    for (int x = 0; x < d.Width && !well; x++)
                    {
                        if (!LevelBuilder.IsRoundWell(d, x, z)) continue;
                        foreach (WallSide side in sides)
                        {
                            if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz) || d.Depth[nx, nz] != PoolDepth.Deep) continue;
                            Vector3 dir = Dir(side);
                            Vector3 center = CellCenter(d, x, z);
                            Vector3 foot = center + dir * 3f;
                            foot.y = LevelData.DepthFloor(PoolDepth.Deep);
                            Shot("poco_redondo", foot, -dir, 45f);
                            Vector3 diver = center + dir * 2.4f;
                            diver.y = -0.5f - Eye;
                            Shot("poco_redondo_debaixo", diver, -dir, 50f, underwater: true);
                            well = true;
                            break;
                        }
                    }
                if (!well) missing.Add("poço redondo");

                // Degrau na linha d'água: o primeiro degrau de uma escada que desce do seco para a
                // água, de frente (rente à água, de dentro dela) e de cima (do seco). Sem a filmadora:
                // o chuvisco esconderia duas faces piscando no mesmo plano.
                CustomPassVolume[] camcorderPasses = Object.FindObjectsByType<CustomPassVolume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                void CleanShot(string name, Vector3 foot, Vector3 look, float pitch)
                {
                    foreach (CustomPassVolume pass in camcorderPasses) pass.enabled = false;
                    try { Shot(name, foot, look, pitch); }
                    finally { foreach (CustomPassVolume pass in camcorderPasses) pass.enabled = true; }
                }
                bool waterStep = false;
                float surface = water != null ? water.SurfaceY : 0f;
                foreach (PoolStair stair in d.Stairs)
                {
                    if (!d.Neighbor(stair.X, stair.Z, stair.Side, out int ux, out int uz) || d.IsSolid(ux, uz) || d.Depth[ux, uz] != PoolDepth.Dry) continue;
                    PoolDepth lower = d.Depth[stair.X, stair.Z];
                    if (lower != PoolDepth.Shallow && lower != PoolDepth.Deep) continue;
                    Vector3 toUpper = Dir(stair.Side);
                    // O primeiro degrau ocupa os 30 cm junto da divisa (ou da face da parede, num arco).
                    Vector3 step = CellCenter(d, stair.X, stair.Z) + toUpper * (LevelBuilder.Cell * 0.5f - 0.25f);
                    step.y = LevelData.DepthFloor(PoolDepth.Dry) - LevelData.StepRise;
                    Vector3 front = step - toUpper * 2.2f;
                    front.y = surface + 0.35f - Eye;
                    CleanShot("degrau_na_agua_frente", front, toUpper, PitchTo(front, step));
                    Vector3 above = step + toUpper * 1.1f;
                    above.y = LevelData.DepthFloor(PoolDepth.Dry);
                    CleanShot("degrau_na_agua_cima", above, -toUpper, PitchTo(above, step));
                    waterStep = true;
                    break;
                }
                if (!waterStep) missing.Add("escada do seco para a água");

                // Teto de vidro Full: a emenda entre duas células vizinhas da mesma sala, e a faixa toda de uma ponta.
                bool roof = false;
                for (int z = 0; z < d.Height && !roof; z++)
                    for (int x = 0; x + 1 < d.Width && !roof; x++)
                    {
                        if (d.IsSolid(x, z) || d.IsSolid(x + 1, z) || d.Sky[x, z] != PoolSky.Full || d.Sky[x + 1, z] != PoolSky.Full) continue;
                        if (d.Room[x, z] != d.Room[x + 1, z] || d.Rooms[d.Room[x, z]].Vault) continue;
                        float seam = (x + 1) * LevelBuilder.Cell;
                        Vector3 foot = new Vector3(seam - 2.6f, StandHeight(d, x, z), (z + 0.5f) * LevelBuilder.Cell);
                        Shot("teto_full_emenda", foot, Vector3.right, -62f);
                        int first = x;
                        while (first > 0 && d.Sky[first - 1, z] == PoolSky.Full && d.Room[first - 1, z] == d.Room[x, z]) first--;
                        Vector3 end = CellCenter(d, first, z) + Vector3.left * 1.4f;
                        end.y = StandHeight(d, first, z);
                        Shot("teto_full_faixa", end, Vector3.right, -38f);

                        // Aceso, o vidro estoura e não mostra emenda nenhuma: apagado (vidro azul-noite
                        // com a textura de vidro fosco), uma repetição por célula apareceria.
                        var lightsOut = Object.FindFirstObjectByType<LightsOut>();
                        if (lightsOut != null)
                        {
                            foreach (CeilingLight lamp in CeilingLight.All) lamp.SetPower(PowerPhase.Dark);
                            lightsOut.SetDarkness(1f);
                            if (water != null) water.ForceNight = 1f;
                            Shot("teto_full_emenda_apagado", foot, Vector3.right, -62f);
                            if (water != null) water.ForceNight = -1f;
                            lightsOut.SetDarkness(0f);
                            foreach (CeilingLight lamp in CeilingLight.All) lamp.SetPower(PowerPhase.Steady);
                        }
                        roof = true;
                    }
                if (!roof) missing.Add("teto Full com duas células");

                // Decalques: um close de cada material, de frente (parede) ou de trás do desenho (piso).
                var seen = new HashSet<string>();
                var perRoom = new Dictionary<int, int>();
                foreach (string group in new[] { "Manchas", "Marcacoes" })
                {
                    Transform parent = builder.transform.Find(group);
                    if (parent == null) { missing.Add(group); continue; }
                    foreach (DecalProjector decal in parent.GetComponentsInChildren<DecalProjector>())
                    {
                        Vector3 local = builder.transform.InverseTransformPoint(decal.transform.position);
                        Vector3 into = builder.transform.InverseTransformDirection(decal.transform.forward);
                        Vector3 up = builder.transform.InverseTransformDirection(decal.transform.up);
                        var (cx, cz) = CellOf(local - (Mathf.Abs(into.y) < 0.5f ? into * 0.5f : Vector3.zero));
                        int room = d.Room[cx, cz];
                        if (room >= 0) perRoom[room] = (perRoom.TryGetValue(room, out int n) ? n : 0) + 1;

                        string material = decal.material != null ? decal.material.name.Replace("Decal_", "").ToLowerInvariant() : "decal";
                        if (!seen.Add(material)) continue;

                        if (into.y > 0.5f)
                        {
                            // No teto: de baixo.
                            Vector3 foot = local - up * 2.4f;
                            var (fx, fz) = CellOf(foot);
                            foot.y = StandHeight(d, fx, fz);
                            Shot("close_" + material, foot, up, PitchTo(foot, local));
                        }
                        else if (into.y < -0.5f)
                        {
                            // No piso: de quem lê o desenho (atrás do pé dele).
                            Vector3 foot = local - up * 1.9f;
                            var (fx, fz) = CellOf(foot);
                            foot.y = StandHeight(d, fx, fz);
                            Shot("close_" + material, foot, up, PitchTo(foot, local));
                            // A mesma, de longe: a marcação na sala.
                            Vector3 back = local - up * 5f;
                            var (bx, bz) = CellOf(back);
                            if (!d.IsSolid(bx, bz) && d.Room[bx, bz] == room)
                            {
                                back.y = StandHeight(d, bx, bz);
                                Shot("sala_" + material, back, up, PitchTo(back, local) * 0.6f);
                            }
                            if (local.y < -0.1f)
                            {
                                Vector3 diver = local - up * 2.2f;
                                diver.y = -0.4f - Eye;
                                Shot("close_" + material + "_debaixo", diver, up, PitchTo(diver, local), underwater: true);
                            }
                        }
                        else
                        {
                            // De frente, a 2,4 m; o que está alto, de mais longe (no máximo 45° para cima).
                            Vector3 foot = local - into * 2.4f;
                            var (fx, fz) = CellOf(foot);
                            float rise = local.y - (StandHeight(d, fx, fz) + Eye);
                            if (rise > 2.4f)
                            {
                                foot = local - into * Mathf.Min(rise, 7f);
                                (fx, fz) = CellOf(foot);
                            }
                            foot.y = StandHeight(d, fx, fz);
                            if (local.y < -0.3f)
                            {
                                // Debaixo d'água: de frente, nadando na altura dela.
                                foot = local - into * 2f;
                                foot.y = local.y - Eye;
                                Shot("close_" + material, foot, into, 0f, underwater: true);
                            }
                            else Shot("close_" + material, foot, into, PitchTo(foot, local));
                        }
                    }
                }

                // As duas salas com mais decalques, em contexto: da passagem para o meio.
                var rooms = new List<int>(perRoom.Keys);
                rooms.Sort((a, b) => perRoom[b] != perRoom[a] ? perRoom[b].CompareTo(perRoom[a]) : a.CompareTo(b));
                for (int k = 0; k < Mathf.Min(2, rooms.Count); k++)
                {
                    PoolRoom room = d.Rooms[rooms[k]];
                    string kind = room.Kind.ToString().ToLowerInvariant();
                    Vector3 middle = new Vector3((room.X0 + room.Width * 0.5f) * LevelBuilder.Cell, 0f, (room.Z0 + room.Height * 0.5f) * LevelBuilder.Cell);
                    middle.y = StandHeight(d, Mathf.Min(room.X0 + room.Width / 2, d.Width - 1), Mathf.Min(room.Z0 + room.Height / 2, d.Height - 1));
                    if (FindDoor(d, room, out int dx, out int dz, out WallSide side))
                    {
                        Vector3 dir = Dir(side);
                        Vector3 foot = CellCenter(d, dx, dz) + dir * (LevelBuilder.Cell * 0.5f - 0.7f);
                        foot.y = StandHeight(d, dx, dz);
                        Vector3 look = middle - foot;
                        if (new Vector2(look.x, look.z).sqrMagnitude < 1f) look = -dir;
                        Shot($"contexto_sala{room.Id:00}_{kind}", foot, look, 10f);
                    }
                    Vector3 toWall = room.Width >= room.Height ? Vector3.forward : Vector3.right;
                    Shot($"contexto_sala{room.Id:00}_{kind}_meio", middle - toWall * (Mathf.Min(room.Width, room.Height) * LevelBuilder.Cell * 0.35f), toWall, 6f);
                }

                // Vista longa (a luz das claraboias some longe sem atravessar parede): ao longo da
                // sala mais comprida, de uma ponta, e do início para o lado mais longe do mapa.
                PoolRoom longest = null;
                foreach (PoolRoom room in d.Rooms)
                    if (longest == null || Mathf.Max(room.Width, room.Height) > Mathf.Max(longest.Width, longest.Height)) longest = room;
                if (longest != null)
                {
                    bool alongX = longest.Width >= longest.Height;
                    int x0 = alongX ? longest.X0 : longest.X0 + longest.Width / 2, z0 = alongX ? longest.Z0 + longest.Height / 2 : longest.Z0;
                    Vector3 foot = CellCenter(d, x0, z0) - (alongX ? Vector3.right : Vector3.forward) * 1.6f;
                    foot.y = StandHeight(d, x0, z0);
                    Shot($"vista_longa_{longest.Kind.ToString().ToLowerInvariant()}", foot, alongX ? Vector3.right : Vector3.forward, 3f);
                }
                Vector3 spawn = builder.transform.InverseTransformPoint(builder.SpawnCenter);
                Shot("vista_do_inicio", spawn, new Vector3(d.Width, 0f, d.Height) * (LevelBuilder.Cell * 0.5f) - spawn, 2f);

                ok = names.Count > 0;
            }
            finally
            {
                if (under != null) under.weight = underBefore;
                camera.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(rig);
                ShaderUtil.allowAsyncCompilation = asyncBefore;
            }

            return $"seed {seed}, {names.Count} fotos em {folder}: {string.Join(", ", names)}" +
                   (missing.Count > 0 ? $"; não há nesta seed: {string.Join(", ", missing)}" : "");
        }

        static int SeedArgument(int fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-seed" && int.TryParse(args[i + 1], out int seed)) return seed;
            return fallback;
        }

        static string Capture(int seed, out bool ok)
        {
            ok = false;
            Level2Setup.BuildScene();
            EditorSceneManager.OpenScene(Level2Setup.ScenePath, OpenSceneMode.Single);

            var builder = Object.FindFirstObjectByType<LevelBuilder>();
            if (builder == null || !builder.poolsEnabled) return "a cena não tem LevelBuilder com as Piscinas ligadas";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) return "sem o prefab do jogador";

            builder.seed = seed;
            builder.Rebuild();
            LevelData d = builder.Data;
            WakeWater(builder);

            // Fora do Play o quadro não avança (Time.frameCount parado) e as sombras "em cache" de
            // toda luz já vista voltam a ser desenhadas a cada Render: as fotos ficavam cada vez mais
            // lentas. Na prévia, a sombra é refeita a cada quadro só para as luzes à vista.
            foreach (HDAdditionalLightData light in builder.GetComponentsInChildren<HDAdditionalLightData>(true))
                light.shadowUpdateMode = ShadowUpdateMode.EveryFrame;

            string ascii = PlanText(d);
            if (!string.IsNullOrEmpty(ascii)) Debug.Log($"[BackRooms] Planta das Piscinas, seed {seed}:\n{ascii}");

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews"));
            Directory.CreateDirectory(folder);
            foreach (string old in Directory.GetFiles(folder, "level2_*.png")) File.Delete(old);

            GameObject rig = Level0Preview.CreateCameraRig(prefab, out Camera camera);
            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var names = new List<string>();
            var times = new List<string>();

            try
            {
                var camcorder = Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();
                if (camcorder != null) camcorder.Draw(true, 61.4f, 61.4f, 0.95f, false);
                camera.targetTexture = target;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 150f);

                void Shot(string name, Vector3 foot, Vector3 look, float pitch)
                {
                    look.y = 0f;
                    if (look.sqrMagnitude < 1e-6f) look = Vector3.forward;
                    rig.transform.SetPositionAndRotation(builder.transform.TransformPoint(foot), Quaternion.LookRotation(look.normalized, Vector3.up));
                    camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    Shoot(camera, target, readback, Path.Combine(folder, name));
                    names.Add(name);
                    times.Add($"{name.Replace("level2_", "").Replace(".png", "")} {clock.Elapsed.TotalSeconds:F1}s");
                    Debug.Log($"[BackRooms] Foto {name}: {clock.Elapsed.TotalSeconds:F1} s");
                }

                ShootPlan(builder, rig, camera, target, readback, folder, names);

                // Duas por sala: de uma passagem olhando para dentro e do meio para a parede mais longa.
                foreach (PoolRoom room in d.Rooms)
                {
                    string kind = room.Kind.ToString().ToLowerInvariant();
                    Vector3 middle = new Vector3((room.X0 + room.Width * 0.5f) * LevelBuilder.Cell, 0f, (room.Z0 + room.Height * 0.5f) * LevelBuilder.Cell);
                    middle.y = StandHeight(d, Mathf.Min(room.X0 + room.Width / 2, d.Width - 1), Mathf.Min(room.Z0 + room.Height / 2, d.Height - 1));

                    if (FindDoor(d, room, out int dx, out int dz, out WallSide side))
                    {
                        Vector3 dir = Dir(side);
                        Vector3 foot = CellCenter(d, dx, dz) + dir * (LevelBuilder.Cell * 0.5f - 0.7f);
                        foot.y = StandHeight(d, dx, dz);
                        Vector3 look = middle - foot;
                        if (new Vector2(look.x, look.z).sqrMagnitude < 1f) look = -dir;
                        Shot($"level2_sala{room.Id:00}_{kind}_porta.png", foot, look, 4f);
                    }

                    Vector3 toWall = room.Width >= room.Height ? Vector3.forward : Vector3.right;
                    Vector3 from = middle - toWall * (Mathf.Min(room.Width, room.Height) * LevelBuilder.Cell * 0.35f);
                    Shot($"level2_sala{room.Id:00}_{kind}_meio.png", from, toWall, 2f);
                }

                ShootCloses(builder, d, Shot, camcorder);
                ok = names.Count > 0;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(rig);
                ShaderUtil.allowAsyncCompilation = asyncBefore;
            }

            return $"seed {seed}, {names.Count} fotos em {folder}; {builder.PoolStats}; tempos: {string.Join(", ", times)}";
        }

        // Como Level0Preview.Shoot, com menos quadros (a névoa e o antisserrilhamento já assentam em 8).
        static void Shoot(Camera camera, RenderTexture target, Texture2D readback, string path)
        {
            for (int i = 0; i < 8; i++) camera.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            readback.Apply();
            File.WriteAllBytes(path, readback.EncodeToPNG());
        }

        // Fora do Play o PoolWater não roda o Start: cria agora a luz rebatida (como no jogo).
        static void WakeWater(LevelBuilder builder)
        {
            var water = builder.GetComponentInChildren<PoolWater>();
            if (water == null || water.GetComponentsInChildren<Volume>(true).Length > 0) return;
            MethodInfo start = typeof(PoolWater).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            start?.Invoke(water, null);
        }

        // A planta em texto, se o gerador souber desenhá-la (PoolGenerator.Ascii).
        static string PlanText(LevelData d)
        {
            MethodInfo ascii = typeof(PoolGenerator).GetMethod("Ascii", BindingFlags.Static | BindingFlags.Public);
            if (ascii == null || ascii.GetParameters().Length != 1) return null;
            return ascii.Invoke(null, new object[] { d }) as string;
        }

        static Vector3 Dir(WallSide side)
        {
            switch (side)
            {
                case WallSide.South: return Vector3.back;
                case WallSide.North: return Vector3.forward;
                case WallSide.West: return Vector3.left;
                default: return Vector3.right;
            }
        }

        static Vector3 CellCenter(LevelData d, int x, int z) => new Vector3((x + 0.5f) * LevelBuilder.Cell, d.FloorHeight(x, z), (z + 0.5f) * LevelBuilder.Cell);

        // Onde os pés ficam: no poço, boiando; no fundo, de pé (olhos a 0,4 m acima da água).
        static float StandHeight(LevelData d, int x, int z)
        {
            if (d.IsSolid(x, z)) return 0.2f;
            return Mathf.Max(d.FloorHeight(x, z), LevelData.DepthFloor(PoolDepth.Deep));
        }

        static bool Passable(PoolEdge edge) => edge == PoolEdge.Arch || edge == PoolEdge.WideArch || edge == PoolEdge.Doorway || edge == PoolEdge.Open;

        // Uma célula da borda da sala com passagem para fora dela.
        static bool FindDoor(LevelData d, PoolRoom room, out int x, out int z, out WallSide side)
        {
            WallSide[] sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };
            for (int cz = room.Z0; cz < room.Z0 + room.Height; cz++)
                for (int cx = room.X0; cx < room.X0 + room.Width; cx++)
                    foreach (WallSide s in sides)
                    {
                        if (!d.Neighbor(cx, cz, s, out int nx, out int nz) || room.Contains(nx, nz) || d.IsSolid(nx, nz)) continue;
                        if (!Passable(d.EdgeAt(cx, cz, s))) continue;
                        x = cx; z = cz; side = s;
                        return true;
                    }
            x = z = 0;
            side = WallSide.South;
            return false;
        }

        static void ShootPlan(LevelBuilder builder, GameObject rig, Camera camera, RenderTexture target, Texture2D readback, string folder, List<string> names)
        {
            LevelData d = builder.Data;
            var hidden = new List<Renderer>();
            foreach (Renderer r in builder.GetComponentsInChildren<Renderer>())
            {
                string n = r.gameObject.name;
                if (n == "Piscinas_Teto" || n == "Sanca" || n.StartsWith("Claraboia") || n == "Agua")
                {
                    if (r.enabled) hidden.Add(r);
                    r.enabled = false;
                }
            }

            var sunObject = new GameObject("SolDaPlanta");
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            if (!sunObject.TryGetComponent(out HDAdditionalLightData hd)) hd = sunObject.AddComponent<HDAdditionalLightData>();
            HDAdditionalLightData.InitDefaultHDAdditionalLightData(hd);
            sun.intensity = 60f;
            sun.shadows = LightShadows.None;
            sunObject.transform.rotation = Quaternion.Euler(70f, 30f, 0f);

            bool ortho = camera.orthographic;
            float size = camera.orthographicSize;
            try
            {
                float w = d.Width * LevelBuilder.Cell, h = d.Height * LevelBuilder.Cell;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(h * 0.5f, w * 0.5f * 720f / 1280f) * 1.04f;
                rig.transform.SetPositionAndRotation(builder.transform.TransformPoint(new Vector3(w * 0.5f, 16f - 1.65f, h * 0.5f)), Quaternion.identity);
                camera.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Shoot(camera, target, readback, Path.Combine(folder, "level2_planta.png"));
                names.Add("level2_planta.png");
            }
            finally
            {
                camera.orthographic = ortho;
                camera.orthographicSize = size;
                Object.DestroyImmediate(sunObject);
                foreach (Renderer r in hidden) r.enabled = true;
            }
        }

        static void ShootCloses(LevelBuilder builder, LevelData d, Action<string, Vector3, Vector3, float> shot, BackRooms.UI.Camcorder camcorder)
        {
            WallSide[] sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };
            bool arch = false, wideArch = false, porthole = false, roundWindow = false, window = false, glassBlock = false, slits = false;

            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.IsSolid(x, z)) continue;
                    foreach (WallSide side in sides)
                    {
                        PoolEdge edge = d.EdgeAt(x, z, side);
                        string name = null;
                        if (edge == PoolEdge.Arch && !arch) { arch = true; name = "arco"; }
                        else if (edge == PoolEdge.WideArch && !wideArch) { wideArch = true; name = "arco_largo"; }
                        else if (edge == PoolEdge.Porthole && !porthole) { porthole = true; name = "vigia"; }
                        else if (edge == PoolEdge.RoundWindow && !roundWindow) { roundWindow = true; name = "vigia_acesa"; }
                        else if (edge == PoolEdge.Window && !window) { window = true; name = "janelao"; }
                        else if (edge == PoolEdge.GlassBlock && !glassBlock) { glassBlock = true; name = "tijolo_de_vidro"; }
                        else if (edge == PoolEdge.Slits && !slits) { slits = true; name = "frestas"; }
                        if (name == null) continue;

                        Vector3 dir = Dir(side);
                        Vector3 foot = CellCenter(d, x, z) + dir * (LevelBuilder.Cell * 0.5f - 3f);
                        foot.y = StandHeight(d, x, z);
                        shot($"level2_close_{name}.png", foot, dir, -4f);
                    }
                }

            // Uma escada que entra na água funda, vista de cima dela.
            foreach (PoolStair stair in d.Stairs)
            {
                PoolDepth lower = d.Depth[stair.X, stair.Z];
                if (lower != PoolDepth.Deep && lower != PoolDepth.Well) continue;
                d.Neighbor(stair.X, stair.Z, stair.Side, out int ux, out int uz);
                Vector3 toLower = -Dir(stair.Side);
                Vector3 foot = CellCenter(d, ux, uz) - toLower * 0.6f;
                foot.y = StandHeight(d, ux, uz);
                Vector3 side = Vector3.Cross(Vector3.up, toLower) * 1.2f;
                shot("level2_close_escada.png", foot + side, toLower - side * 0.3f, 24f);
                break;
            }

            // O poço redondo, visto da borda (o poço com vizinhas só fundas e sem escada).
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.IsSolid(x, z) || d.Depth[x, z] != PoolDepth.Well) continue;
                    foreach (WallSide side in sides)
                    {
                        if (!d.IsOpen(x, z, side) || !d.Neighbor(x, z, side, out int nx, out int nz) || d.Depth[nx, nz] != PoolDepth.Deep) continue;
                        Vector3 dir = Dir(side);
                        Vector3 foot = CellCenter(d, x, z) + dir * 2.6f;
                        foot.y = LevelData.DepthFloor(PoolDepth.Deep);
                        shot("level2_close_poco.png", foot, -dir, 48f);
                        goto WellDone;
                    }
                }
            WellDone:

            // Uma claraboia (não Full) vista de baixo, do lado oposto ao sol.
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    PoolSky sky = d.Sky[x, z];
                    if (d.IsSolid(x, z) || sky == PoolSky.None || sky == PoolSky.Full) continue;
                    float heading = builder.poolSunHeading * Mathf.Deg2Rad;
                    var toSun = -new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
                    Vector3 foot = CellCenter(d, x, z) - toSun * 2.2f;
                    foot.y = StandHeight(d, x, z);
                    shot($"level2_close_claraboia_{sky.ToString().ToLowerInvariant()}.png", foot, toSun, -55f);
                    goto SkyDone;
                }
            SkyDone:

            // Uma faixa de teto Full, se houver.
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.IsSolid(x, z) || d.Sky[x, z] != PoolSky.Full) continue;
                    Vector3 foot = CellCenter(d, x, z);
                    foot.y = StandHeight(d, x, z);
                    shot("level2_close_teto_luminoso.png", foot + Vector3.back * 3f, Vector3.forward, -35f);
                    goto FullDone;
                }
            FullDone:

            // Até três manchas (decalques), de frente, a 2,4 m.
            Transform stains = builder.transform.Find("Manchas");
            if (stains != null)
            {
                int shots = 0;
                foreach (DecalProjector decal in stains.GetComponentsInChildren<DecalProjector>())
                {
                    if (shots >= 3) break;
                    Vector3 local = builder.transform.InverseTransformPoint(decal.transform.position);
                    Vector3 into = builder.transform.InverseTransformDirection(decal.transform.forward);
                    if (Mathf.Abs(into.y) > 0.5f) continue; // mofo no teto: fica para a foto da sala
                    int cx = Mathf.Clamp(Mathf.FloorToInt((local.x - into.x * 0.5f) / LevelBuilder.Cell), 0, d.Width - 1);
                    int cz = Mathf.Clamp(Mathf.FloorToInt((local.z - into.z * 0.5f) / LevelBuilder.Cell), 0, d.Height - 1);
                    Vector3 foot = local - into * 2.4f;
                    foot.y = StandHeight(d, cx, cz);
                    float pitch = -Mathf.Atan2(local.y - (foot.y + Eye), 2.4f) * Mathf.Rad2Deg;
                    string material = decal.material != null ? decal.material.name.ToLowerInvariant().Replace(' ', '_') : "mancha";
                    shot($"level2_close_mancha_{shots}_{material}.png", foot, into, Mathf.Clamp(pitch, -60f, 60f));
                    shots++;
                }
            }

            // Debaixo d'água, numa área funda, olhando ao longo da sala.
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.IsSolid(x, z) || d.Depth[x, z] != PoolDepth.Deep) continue;
                    var water = builder.GetComponentInChildren<PoolWater>();
                    Volume under = null;
                    if (water != null)
                        foreach (Volume v in water.GetComponentsInChildren<Volume>(true))
                            if (v.gameObject.name == "DebaixoDagua") under = v;
                    float before = under != null ? under.weight : 0f;
                    if (under != null) under.weight = 1f;
                    if (camcorder != null) camcorder.Draw(true, 61.4f, 61.4f, 0.95f, false, underwater: true, air: 0.6f);

                    Vector3 foot = CellCenter(d, x, z);
                    foot.y = -0.6f - Eye;
                    PoolRoom room = d.Rooms[d.Room[x, z]];
                    Vector3 look = new Vector3((room.X0 + room.Width * 0.5f) * LevelBuilder.Cell, 0f, (room.Z0 + room.Height * 0.5f) * LevelBuilder.Cell) - foot;
                    shot("level2_debaixo_dagua.png", foot, look.sqrMagnitude > 1f ? look : Vector3.forward, 8f);

                    if (under != null) under.weight = before;
                    if (camcorder != null) camcorder.Draw(true, 61.4f, 61.4f, 0.95f, false);
                    return;
                }
        }
    }
}
