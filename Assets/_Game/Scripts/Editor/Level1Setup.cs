using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// O Level 1 (Docs/level1-mostruario.md): a planta do Level 0 — 20 x 20 células, pé-direito
    /// de 3,4 m, sem buracos e sem nichos — mobiliada pela <see cref="PropLayer"/> com todos os
    /// modelos do Blender de Art/Level1/Modelos, e com o Capitão no lugar da entidade (o prefab
    /// Prefabs/Captain.prefab é gravado aqui, por <see cref="CaptainPreview.CreatePrefab"/>).
    ///
    /// Menu BackRooms > Montar Level 1 (teste). A cena fica em Scenes/Level1_Test.unity; para
    /// jogar, abra a cena, aperte Play e hospede (H). Ela é gerada: mudança se faz aqui, no
    /// Level0Setup ou na PropLayer, nunca na cena.
    ///
    /// Menu BackRooms > Conferir Level 1 (e Level1Setup.BatchCheck pela linha de comando):
    /// monta a cena e confere várias seeds — dá para passar entre todas as células abertas, a
    /// sala inicial fica vazia, a mesma seed dá a mesma mobília — e tira fotos em
    /// Logs/Previews/level1_*.png.
    /// </summary>
    public static class Level1Setup
    {
        public const string ScenePath = "Assets/_Game/Scenes/Level1_Test.unity";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        // O tamanho do Level 0 (20 x 20 células, 80 x 80 m) e o pé-direito baixo do projeto
        // (level1-mostruario.md, seção 3): 3,4 m deixam os móveis mais opressivos.
        const int Size = 20;
        const float Ceiling = 3.4f;
        static readonly int[] CheckSeeds = { 1998, 7, 4242, 31337, 90210 };

        [MenuItem("BackRooms/Abrir Level 1 (teste)", priority = 0)]
        public static void OpenFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) BuildScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[BackRooms] Level 1 aberto. Aperte Play e depois H para hospedar.");
        }

        [MenuItem("BackRooms/Abrir Level 0", priority = 0)]
        public static void OpenLevel0FromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Level0AutoUpdate.OpenGameScene();
        }

        [MenuItem("BackRooms/Montar Level 1 (teste)")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene();
        }

        public static void BuildScene()
        {
            GameObject[] models = LoadModels();
            GameObject captain = CaptainPreview.CreatePrefab();
            Level0Setup.BuildScene(false, new Level0Setup.SceneOptions
            {
                scenePath = ScenePath,
                levelName = "Level1",
                size = Size,
                ceilingHeight = Ceiling,
                pits = false,
                alcoves = false,
                // O inimigo daqui é o Capitão, com o mesmo diretor da entidade.
                // Ele acorda quando alguém chega a 8 células da sala inicial, como a entidade.
                entity = true,
                enemyPrefab = captain,
                enemyWakeCells = 8,
                paintedArrows = false,
                decorate = level =>
                {
                    var props = level.AddComponent<PropLayer>();
                    props.models = models;
                    // Totens: a estátua é o próprio modelo do Capitão, assado na pose parada.
                    props.totemModel = CharacterModels.Model("Capitao");
                    props.totemMaterials = CharacterModels.Materials("Capitao");
                    props.totemBaseMaterial = FaseMaterial("Totem_Base", new Color(0.035f, 0.035f, 0.04f), 0.7f, null);
                    props.bubbleWrapMaterial = FaseMaterial("PlasticoBolha", new Color(0.86f, 0.9f, 0.93f), 0.88f, BubbleTexture());
                    // Objetivo: porta trancada por teclado, código nas etiquetas vermelhas.
                    props.exitCode = true;
                    props.labelMaterial = FaseMaterial("Etiqueta", Color.white, 0.25f, null);
                },
            });
            Debug.Log($"[BackRooms] Level 1 de teste montado com {models.Length} modelos: abra {ScenePath}, aperte Play e hospede (H).");
        }

        // ---------------------------------------------------------------- Materiais da fase

        // Materiais e texturas da própria fase (não dos móveis do Blender): Art/Level1/Fase.
        // Gerados aqui a cada montagem.
        const string FaseFolder = "Assets/_Game/Art/Level1/Fase";

        static Material FaseMaterial(string name, Color color, float smoothness, Texture2D albedo)
        {
            if (!AssetDatabase.IsValidFolder(FaseFolder)) AssetDatabase.CreateFolder("Assets/_Game/Art/Level1", "Fase");
            string path = FaseFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(Shader.Find("HDRP/Lit")) { name = name };

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetTexture("_BaseColorMap", albedo);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(material);

            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        // Plástico-bolha: bolhas redondas em fileiras desencontradas, com brilho no alto de cada
        // uma e a película entre elas mais apagada. Emenda nas bordas (o quadrado repete).
        static Texture2D BubbleTexture()
        {
            string path = FaseFolder + "/PlasticoBolha.png";
            if (!AssetDatabase.IsValidFolder(FaseFolder)) AssetDatabase.CreateFolder("Assets/_Game/Art/Level1", "Fase");

            const int size = 256, rows = 8;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float pitch = size / (float)rows;
            float radius = pitch * 0.38f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float best = float.MaxValue;
                    Vector2 offset = Vector2.zero;
                    for (int r = -1; r <= rows; r++)
                    {
                        float cy = (r + 0.5f) * pitch;
                        float shift = (r & 1) == 0 ? 0f : pitch * 0.5f;
                        for (int c = -1; c <= rows; c++)
                        {
                            float cx = (c + 0.5f) * pitch + shift;
                            var d = new Vector2(x - cx, y - cy);
                            if (d.sqrMagnitude < best) { best = d.sqrMagnitude; offset = d; }
                        }
                    }
                    float dist = Mathf.Sqrt(best) / radius;
                    float film = 0.72f;
                    float value = film;
                    if (dist < 1f)
                    {
                        // Bolha: mais clara no meio, aro escuro na borda, brilho em cima e à esquerda.
                        float dome = Mathf.Sqrt(Mathf.Max(0f, 1f - dist * dist));
                        value = Mathf.Lerp(0.62f, 0.9f, dome);
                        if (dist > 0.82f) value -= 0.12f;
                        Vector2 glint = offset / radius - new Vector2(-0.35f, 0.35f);
                        if (glint.magnitude < 0.22f) value = 1f;
                    }
                    texture.SetPixel(x, y, new Color(value * 0.96f, value, value * 1.02f, 1f));
                }
            }

            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static GameObject[] LoadModels()
        {
            return AssetDatabase.FindAssets("t:Model", new[] { Level1Models.ModelFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(m => m != null)
                .ToArray();
        }

        // ---------------------------------------------------------------- Conferência

        [MenuItem("BackRooms/Conferir Level 1")]
        public static void CheckFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            bool ok = Check(out string report);
            EditorUtility.DisplayDialog("Level 1", (ok ? "PASSOU\n\n" : "FALHOU\n\n") + report, "OK");
        }

        /// <summary>Linha de comando: atualiza modelos, monta, confere e fotografa. Sai com 0 se passou, 3 se falhou.</summary>
        public static void BatchCheck()
        {
            try
            {
                Level1Models.UpdateModels();
                bool ok = Check(out string report);
                Debug.Log("[BackRooms] Conferência do Level 1 " + (ok ? "PASSOU" : "FALHOU") + ": " + report);
                ExitWithLevel0Open(ok ? 0 : 3);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ExitWithLevel0Open(1);
            }
        }

        /// <summary>
        /// Fecha o editor deixando o Level 0 aberto. O editor reabre na última cena aberta, e quem
        /// abre o projeto depois de um teste por linha de comando espera cair no jogo, não no
        /// Level 1 de teste.
        /// </summary>
        public static void ExitWithLevel0Open(int code)
        {
            try { Level0AutoUpdate.OpenGameScene(); }
            catch (Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(code);
        }

        static bool Check(out string report)
        {
            BuildScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var builder = UnityEngine.Object.FindFirstObjectByType<LevelBuilder>();
            var props = builder != null ? builder.GetComponent<PropLayer>() : null;
            if (props == null)
            {
                report = "a cena não tem LevelBuilder com PropLayer";
                return false;
            }

            var problems = new List<string>();
            var lines = new List<string>();

            try
            {
                foreach (int seed in CheckSeeds)
                {
                    builder.seed = seed;
                    builder.Rebuild();
                    int count = props.Count, hash = props.Hash;

                    builder.Rebuild();
                    if (props.Hash != hash || props.Count != count)
                        problems.Add($"seed {seed}: a mesma seed deu mobília diferente ({count} x {props.Count} móveis)");

                    Transform root = builder.transform.Find("Moveis");
                    var roomItems = new List<string>();
                    int inRoom = CountInSpawnRoom(builder, root, roomItems);
                    if (inRoom > 0) problems.Add($"seed {seed}: {inRoom} móveis dentro da sala inicial ({string.Join(", ", roomItems)})");

                    List<string> blocked = BlockedPassages(builder, root);
                    if (blocked.Count > 0)
                        problems.Add($"seed {seed}: {blocked.Count} passagens bloqueadas por móveis ({string.Join(", ", blocked.Take(5))})");

                    if (count < Size * Size / 2) problems.Add($"seed {seed}: só {count} móveis");

                    // Abrigo e perigo: tem de haver de tudo, e as barricadas não podem isolar nada.
                    if (props.Totems < props.totemsMin) problems.Add($"seed {seed}: só {props.Totems} totens");
                    if (props.Barricades < 1) problems.Add($"seed {seed}: nenhuma barricada");
                    if (props.BubbleSheets < 1) problems.Add($"seed {seed}: nenhum plástico-bolha");
                    if (props.HideSpots < 3) problems.Add($"seed {seed}: só {props.HideSpots} esconderijos em móvel");
                    string isolated = Isolated(d: builder.Data);
                    if (isolated != null) problems.Add($"seed {seed}: a barricada isolou {isolated}");

                    lines.Add($"seed {seed}: {count} móveis, sala dos sofás no bloco {props.SofaRoomBlock}, {blocked.Count} passagens bloqueadas, " +
                              $"{props.Barricades} barricadas, {props.Totems} totens, {props.BubbleSheets} plásticos-bolha, {props.HideSpots} esconderijos " +
                              $"({HideCount(root, BackRooms.Player.HideKind.Wardrobe)} guarda-roupas, {HideCount(root, BackRooms.Player.HideKind.Table)} mesas)");
                }

                builder.seed = CheckSeeds[0];
                builder.Rebuild();
                lines.Add("modelos usados (seed " + CheckSeeds[0] + "): " + string.Join(", ", UsedModels(builder.transform.Find("Moveis"))));
                lines.Add("fotos: " + Capture(builder, props));
            }
            finally
            {
                // Reabre a cena do disco: o mapa gerado aqui só serviu para conferir.
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            report = string.Join("\n", lines) + (problems.Count > 0 ? "\nPROBLEMAS:\n" + string.Join("\n", problems) : "");
            return problems.Count == 0;
        }

        static int CountInSpawnRoom(LevelBuilder builder, Transform root) => CountInSpawnRoom(builder, root, null);

        static int CountInSpawnRoom(LevelBuilder builder, Transform root, List<string> names)
        {
            LevelData d = builder.Data;
            int count = 0;
            foreach (Transform item in root)
            {
                Vector3 p = item.localPosition;
                int x = Mathf.FloorToInt(p.x / LevelBuilder.Cell), z = Mathf.FloorToInt(p.z / LevelBuilder.Cell);
                if (x >= 0 && z >= 0 && x < d.Width && z < d.Height && d.InRoom(x, z))
                {
                    count++;
                    names?.Add($"{item.name} em ({x},{z})");
                }
            }
            return count;
        }

        // Para cada par de células vizinhas sem parede entre elas, uma cápsula do tamanho do
        // jogador (0,28 m de raio) vai de um centro ao outro; bater num móvel é passagem fechada.
        static int HideCount(Transform root, BackRooms.Player.HideKind kind)
        {
            int count = 0;
            foreach (HideSpot spot in root.GetComponentsInChildren<HideSpot>())
                if (spot.kind == kind) count++;
            return count;
        }

        // Alguma célula com piso, fora das barricadas, ficou sem caminho até a sala inicial? Devolve
        // a primeira, ou nulo se não.
        static string Isolated(LevelData d)
        {
            var seen = new bool[d.Width, d.Height];
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(d.SpawnX, d.SpawnZ));
            seen[d.SpawnX, d.SpawnZ] = true;
            WallSide[] sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };
            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                foreach (WallSide side in sides)
                {
                    if (!d.CanWalk(c.x, c.y, side, false) || !d.Neighbor(c.x, c.y, side, out int nx, out int nz)) continue;
                    if (seen[nx, nz] || d.IsBarricade(nx, nz)) continue;
                    seen[nx, nz] = true;
                    queue.Enqueue(new Vector2Int(nx, nz));
                }
            }
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    if (!d.Pit[x, z] && !d.IsBarricade(x, z) && !seen[x, z]) return $"({x},{z})";
            return null;
        }

        static List<string> BlockedPassages(LevelBuilder builder, Transform root)
        {
            Physics.SyncTransforms();
            LevelData d = builder.Data;
            var blocked = new List<string>();

            for (int z = 0; z < d.Height; z++)
            {
                for (int x = 0; x < d.Width; x++)
                {
                    TryPassage(builder, root, d, x, z, x + 1, z, WallSide.East, blocked);
                    TryPassage(builder, root, d, x, z, x, z + 1, WallSide.North, blocked);
                }
            }
            return blocked;
        }

        static void TryPassage(LevelBuilder builder, Transform root, LevelData d, int x, int z, int nx, int nz, WallSide side, List<string> blocked)
        {
            if (nx >= d.Width || nz >= d.Height) return;
            if (!d.CanWalk(x, z, side, false)) return;
            // A barricada fecha a passagem de propósito; o que se confere é que nada fica isolado.
            if (d.IsBarricade(x, z) || d.IsBarricade(nx, nz)) return;

            Vector3 a = builder.CellCenter(x, z), b = builder.CellCenter(nx, nz);
            Vector3 low = Vector3.up * 0.35f, high = Vector3.up * 1.5f;
            Vector3 direction = b - a;
            RaycastHit[] hits = Physics.CapsuleCastAll(a + low, a + high, 0.28f, direction.normalized, direction.magnitude);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(root))
                {
                    blocked.Add($"({x},{z})-({nx},{nz}) por {hit.collider.name}");
                    return;
                }
            }
        }

        static IEnumerable<string> UsedModels(Transform root)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (Transform item in root)
                counts[item.name] = counts.TryGetValue(item.name, out int n) ? n + 1 : 1;
            return counts.Select(kv => $"{kv.Key} {kv.Value}");
        }

        // ---------------------------------------------------------------- Fotos

        static string Capture(LevelBuilder builder, PropLayer props)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) return "sem o prefab do jogador";

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews"));
            Directory.CreateDirectory(folder);
            GameObject rig = Level0Preview.CreateCameraRig(prefab, out Camera camera);

            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            int shots = 0;

            try
            {
                var camcorder = UnityEngine.Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();
                if (camcorder != null) camcorder.Draw(true, 61.4f, 61.4f, 0.95f, false);
                camera.targetTexture = target;

                LevelData d = builder.Data;
                Transform root = builder.transform.Find("Moveis");

                // Células com mais coisa, vistas da célula vizinha aberta.
                var perCell = new Dictionary<Vector2Int, int>();
                foreach (Transform item in root)
                {
                    var cell = new Vector2Int(Mathf.FloorToInt(item.localPosition.x / LevelBuilder.Cell), Mathf.FloorToInt(item.localPosition.z / LevelBuilder.Cell));
                    perCell[cell] = perCell.TryGetValue(cell, out int n) ? n + 1 : 1;
                }

                var targets = new List<Vector2Int>();
                Vector2Int sofa = props.SofaRoomBlock;
                if (sofa.x >= 0) targets.Add(new Vector2Int(sofa.x * 2, sofa.y * 2));
                targets.AddRange(perCell.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.x * 1000 + kv.Key.y)
                                         .Select(kv => kv.Key).Where(c => !targets.Contains(c)).Take(5));

                WallSide[] sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };
                foreach (Vector2Int cell in targets)
                {
                    foreach (WallSide side in sides)
                    {
                        if (!d.CanWalk(cell.x, cell.y, side, false) || !d.Neighbor(cell.x, cell.y, side, out int nx, out int nz)) continue;

                        Vector3 eye = builder.CellCenter(nx, nz);
                        Vector3 look = builder.CellCenter(cell.x, cell.y) - eye;
                        rig.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look.normalized, Vector3.up));
                        camera.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
                        string name = shots == 0 && sofa.x >= 0 ? "level1_sala_dos_sofas.png" : $"level1_vista_{shots}.png";
                        Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, name));
                        shots++;
                        break;
                    }
                }

                // As placas de saída pregadas no lugar das setas pintadas, vistas do meio da célula.
                int signs = 0;
                foreach (Decoration mark in d.Decorations)
                {
                    if (mark.Kind != DecorationKind.Arrow || signs >= 2) continue;
                    LevelBuilder.WallFace(mark.X, mark.Z, mark.Side, out Vector3 face, out Vector3 normal);
                    Vector3 tangent = Mathf.Abs(normal.z) > 0.5f ? Vector3.right : Vector3.forward;
                    Vector3 sign = builder.transform.TransformPoint(face + tangent * mark.Along);
                    Vector3 eye = sign + normal * 2.6f;
                    rig.transform.SetPositionAndRotation(new Vector3(eye.x, 0f, eye.z), Quaternion.LookRotation(-normal, Vector3.up));
                    camera.transform.localRotation = Quaternion.identity;
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, $"level1_placa_saida_{signs}.png"));
                    signs++;
                    shots++;
                }

                // Um totem (de frente e de baixo, que é como se vê um personagem alto), uma
                // barricada, um plástico-bolha e um guarda-roupa.
                Totem firstTotem = root.GetComponentInChildren<Totem>();
                if (firstTotem != null)
                {
                    Transform t = firstTotem.transform;
                    Vector3 eye = t.position + t.forward * 3.2f;
                    rig.transform.SetPositionAndRotation(new Vector3(eye.x, 0f, eye.z), Quaternion.LookRotation(-t.forward, Vector3.up));
                    camera.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "level1_totem.png"));
                    eye = t.position + t.forward * 1.4f;
                    rig.transform.SetPositionAndRotation(new Vector3(eye.x, 0f, eye.z), Quaternion.LookRotation(-t.forward, Vector3.up));
                    camera.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "level1_totem_de_baixo.png"));
                    shots += 2;
                }

                Transform barricade = root.Find("Barricada");
                if (barricade != null)
                {
                    Vector3 at = barricade.position;
                    foreach (WallSide side in sides)
                    {
                        Vector3 local = builder.transform.InverseTransformPoint(at);
                        int bx = Mathf.FloorToInt(local.x / LevelBuilder.Cell), bz = Mathf.FloorToInt(local.z / LevelBuilder.Cell);
                        if (!d.CanWalk(bx, bz, side, false) || !d.Neighbor(bx, bz, side, out int nx, out int nz)) continue;
                        Vector3 eye = builder.CellCenter(nx, nz);
                        rig.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((builder.CellCenter(bx, bz) - eye).normalized, Vector3.up));
                        camera.transform.localRotation = Quaternion.Euler(-5f, 0f, 0f);
                        Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "level1_barricada.png"));
                        shots++;
                        break;
                    }
                }

                Transform wrap = root.Find("PlasticoBolha");
                if (wrap != null)
                {
                    Vector3 eye = wrap.position + new Vector3(1.6f, 0f, 1.6f);
                    rig.transform.SetPositionAndRotation(new Vector3(eye.x, 0f, eye.z), Quaternion.LookRotation(new Vector3(-1f, 0f, -1f), Vector3.up));
                    camera.transform.localRotation = Quaternion.Euler(40f, 0f, 0f);
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "level1_plastico_bolha.png"));
                    shots++;
                }

                foreach (HideSpot spot in root.GetComponentsInChildren<HideSpot>())
                {
                    if (spot.kind != BackRooms.Player.HideKind.Wardrobe) continue;
                    Vector3 eye = spot.ExitWorld + spot.transform.forward * 0.8f;
                    rig.transform.SetPositionAndRotation(new Vector3(eye.x, 0f, eye.z), Quaternion.LookRotation(-spot.transform.forward, Vector3.up));
                    camera.transform.localRotation = Quaternion.Euler(5f, 0f, 0f);
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "level1_guarda_roupa.png"));
                    shots++;
                    break;
                }

                // Da porta da sala inicial para fora.
                rig.transform.SetPositionAndRotation(builder.SpawnCenter, Quaternion.identity);
                for (int i = 0; i < 4; i++)
                {
                    rig.transform.rotation = Quaternion.Euler(0f, i * 90f, 0f);
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, $"level1_inicio_{i * 90}.png"));
                    shots++;
                }
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(rig);
                ShaderUtil.allowAsyncCompilation = asyncBefore;
            }

            return $"{shots} em {folder}";
        }
    }
}
