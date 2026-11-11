using System;
using System.IO;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Renderiza imagens do Level 0 sem entrar em Play, para conferir o visual.
    /// Menu BackRooms > Capturar prévia do Level 0. As imagens vão para Logs/Previews.
    /// </summary>
    public static class Level0Preview
    {
        const string ScenePath = "Assets/_Game/Scenes/Level0_Test.unity";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";

        [MenuItem("BackRooms/Capturar prévia do Level 0")]
        public static void CaptureFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string folder = Capture(DefaultFolder());
            if (folder != null) EditorUtility.RevealInFinder(folder);
        }

        /// <summary>Uso em linha de comando: recria tudo (assets e cena), captura e fecha o editor.</summary>
        public static void BatchBuildAndCapture()
        {
            BatchBuild(regenerateAssets: true);
        }

        /// <summary>Uso em linha de comando: recria só a cena, mantendo texturas e materiais, captura e fecha.</summary>
        public static void BatchSceneAndCapture()
        {
            BatchBuild(regenerateAssets: false);
        }

        static void BatchBuild(bool regenerateAssets)
        {
            try
            {
                Level0Setup.BuildScene(regenerateAssets);
                string folder = ArgumentAfter("-previewOut") ?? DefaultFolder();
                bool ok = Capture(folder) != null;
                EditorApplication.Exit(ok ? 0 : 2);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static string DefaultFolder()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews"));
        }

        static string ArgumentAfter(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }

        static string Capture(string folder)
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError("[BackRooms] Cena não encontrada. Rode antes BackRooms > Montar Level 0.");
                return null;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var builder = UnityEngine.Object.FindFirstObjectByType<LevelBuilder>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (builder == null || prefab == null)
            {
                Debug.LogError("[BackRooms] A cena não tem LevelBuilder ou falta o prefab do jogador.");
                return null;
            }

            // No jogo o jogador é criado pela rede. Para a foto monta-se um suporte simples
            // com cópias da câmera e da lanterna do prefab (mesmos ajustes), sem os
            // componentes de rede. Instanciar o prefab de rede fora do Play, por script,
            // travou o editor em modo batch nos testes.
            GameObject playerObject = CreateCameraRig(prefab, out Camera camera);

            Directory.CreateDirectory(folder);

            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);

            try
            {
                builder.Rebuild();

                // Fora do Play o painel da filmadora não se atualiza sozinho: desenha um quadro
                // de exemplo (gravando há 3 min 27 s, bateria em 80%).
                var camcorder = UnityEngine.Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();
                if (camcorder != null) camcorder.Draw(true, 207.47f, 207.47f, 0.8f, false);

                Transform player = playerObject.transform;
                player.position = builder.SpawnCenter + Vector3.up * 0.05f;
                var torch = player.GetComponentInChildren<Light>(true);
                if (torch != null) torch.enabled = false;

                camera.targetTexture = target;

                // Quatro direções a partir do spawn, depois uma com a lanterna numa célula sem luz.
                for (int i = 0; i < 4; i++)
                {
                    if (player != null) player.rotation = Quaternion.Euler(0f, i * 90f, 0f);
                    Shoot(camera, target, readback, Path.Combine(folder, $"level0_spawn_{i * 90}.png"));
                }

                if (player != null && FindDarkCell(builder, out Vector3 dark))
                {
                    player.position = dark;
                    player.rotation = Quaternion.Euler(0f, 45f, 0f);
                    Shoot(camera, target, readback, Path.Combine(folder, "level0_escuro.png"));

                    if (torch != null)
                    {
                        torch.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                        torch.enabled = true;
                        Shoot(camera, target, readback, Path.Combine(folder, "level0_lanterna.png"));
                        torch.enabled = false;
                    }
                }

                if (player != null) ShootFeatures(builder, player, camera, torch, target, readback, folder);

                if (player != null) ShootPenSample(builder, player, camera, target, readback, folder);

                if (player != null) ShootPits(builder, player, camera, torch, target, readback, folder);

                if (player != null) ShootEntity(builder, player, camera, torch, target, readback, folder);

                if (player != null) ShootEntityFace(builder, player, camera, torch, target, readback, folder);

                if (player != null) ShootPlayerFigure(builder, prefab, player, camera, torch, target, readback, folder);

                if (player != null) ShootHand(builder, prefab, player, camera, target, readback, folder);

                if (player != null) ShootBlackout(builder, player, camera, torch, target, readback, folder);

                // A tela de quem acabou de cair num buraco.
                if (camcorder != null)
                {
                    camcorder.Draw(true, 207.47f, 207.47f, 0.8f, false, null, true);
                    Shoot(camera, target, readback, Path.Combine(folder, "sem_sinal.png"));
                    camcorder.Draw(true, 207.47f, 207.47f, 0.8f, false);
                }

                // Quatro vistas de uma área distante do início, onde o desgaste é total.
                if (player != null)
                {
                    int farX = Mathf.Clamp(builder.Data.SpawnX - 5, 0, builder.Data.Width - 1);
                    int farZ = Mathf.Clamp(builder.Data.SpawnZ - 4, 0, builder.Data.Height - 1);
                    player.position = builder.CellCenter(farX, farZ) + Vector3.up * 0.05f;

                    for (int i = 0; i < 4; i++)
                    {
                        player.rotation = Quaternion.Euler(0f, i * 90f, 0f);
                        Shoot(camera, target, readback, Path.Combine(folder, $"level0_longe_{i * 90}.png"));
                    }
                }

                Debug.Log("[BackRooms] Prévia salva em " + folder);
                return folder;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(readback);
                ShaderUtil.allowAsyncCompilation = asyncBefore;

                // Reabre a cena do disco para descartar o mapa gerado só para a foto.
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        // Risca uma seta, um X e um círculo na parede norte da sala inicial, um de cada cor,
        // e fotografa. Serve para conferir como a caneta aparece sem precisar jogar.
        static void ShootPenSample(LevelBuilder builder, Transform player, Camera camera,
                                   RenderTexture target, Texture2D readback, string folder)
        {
            var board = UnityEngine.Object.FindFirstObjectByType<BackRooms.Items.DrawingBoard>();
            if (board == null) return;

            LevelData data = builder.Data;
            float wallZ = (data.RoomZ0 + data.RoomHeight) * LevelBuilder.Cell - LevelBuilder.WallThickness * 0.5f;
            Vector3 origin = builder.transform.TransformPoint(new Vector3(data.SpawnCenterX * LevelBuilder.Cell - 2.6f, 1.5f, wallZ));
            Vector3 normal = Vector3.back;

            var points = new System.Collections.Generic.List<Vector3>();

            // Seta para a direita (preta).
            points.Add(origin + new Vector3(0f, 0.25f, 0f));
            for (int i = 1; i <= 30; i++) points.Add(origin + new Vector3(i * 0.02f, 0.25f + Mathf.Sin(i * 0.4f) * 0.004f, 0f));
            board.Append(9001, 0, normal, points);
            points.Clear();
            for (int i = 0; i <= 8; i++) points.Add(origin + new Vector3(0.6f - i * 0.02f, 0.25f + i * 0.015f, 0f));
            board.Append(9002, 0, normal, points);
            points.Clear();
            for (int i = 0; i <= 8; i++) points.Add(origin + new Vector3(0.6f - i * 0.02f, 0.25f - i * 0.015f, 0f));
            board.Append(9003, 0, normal, points);

            // X (vermelho).
            points.Clear();
            for (int i = 0; i <= 14; i++) points.Add(origin + new Vector3(0.95f + i * 0.02f, 0.4f - i * 0.02f, 0f));
            board.Append(9004, 1, normal, points);
            points.Clear();
            for (int i = 0; i <= 14; i++) points.Add(origin + new Vector3(0.95f + i * 0.02f, 0.12f + i * 0.02f, 0f));
            board.Append(9005, 1, normal, points);

            // Círculo (azul).
            points.Clear();
            for (int i = 0; i <= 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f;
                points.Add(origin + new Vector3(1.75f + Mathf.Cos(a) * 0.16f, 0.26f + Mathf.Sin(a) * 0.16f, 0f));
            }
            board.Append(9006, 2, normal, points);

            // Um risco no chão, em frente à parede.
            points.Clear();
            Vector3 floor = builder.transform.TransformPoint(new Vector3(data.SpawnCenterX * LevelBuilder.Cell - 2.4f, 0f, wallZ - 0.7f));
            for (int i = 0; i <= 50; i++) points.Add(floor + new Vector3(i * 0.03f, 0f, Mathf.Sin(i * 0.3f) * 0.05f));
            board.Append(9007, 0, Vector3.up, points);

            player.position = builder.transform.TransformPoint(new Vector3(data.SpawnCenterX * LevelBuilder.Cell - 1.7f, 0.05f, wallZ - 1.9f));
            player.rotation = Quaternion.identity;

            var camcorder = UnityEngine.Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();
            if (camcorder != null) camcorder.Draw(true, 207.47f, 207.47f, 0.8f, false, "PEN BLACK");

            Shoot(camera, target, readback, Path.Combine(folder, "caneta.png"));
        }

        static GameObject CreateCameraRig(GameObject prefab, out Camera camera)
        {
            var rig = new GameObject("SuporteDaPrevia");

            var pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(rig.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            camera = pivot.AddComponent<Camera>();
            CopyFromPrefab(prefab.GetComponentInChildren<Camera>(true), camera);
            CopyFromPrefab(prefab.GetComponentInChildren<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>(true),
                           GetOrAdd<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>(pivot));
            camera.enabled = false;

            var torch = new GameObject("Lanterna");
            torch.transform.SetParent(rig.transform, false);
            var torchLight = torch.AddComponent<Light>();
            CopyFromPrefab(prefab.GetComponentInChildren<Light>(true), torchLight);
            CopyFromPrefab(prefab.GetComponentInChildren<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>(true),
                           GetOrAdd<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>(torch));
            torchLight.enabled = false;

            return rig;
        }

        static void CopyFromPrefab(Component source, Component destination)
        {
            if (source != null && destination != null) EditorUtility.CopySerialized(source, destination);
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();
        }

        // Uma foto de cada tipo de elemento: marcas, esconderijo e saída. Cada uma sai
        // duas vezes — só com a luz do ambiente e com a lanterna acesa.
        static void ShootFeatures(LevelBuilder builder, Transform player, Camera camera, Light torch,
                                  RenderTexture target, Texture2D readback, string folder)
        {
            LevelData data = builder.Data;
            var done = new System.Collections.Generic.HashSet<DecorationKind>();

            foreach (Decoration mark in data.Decorations)
            {
                if (mark.Kind == DecorationKind.FloorPool || !done.Add(mark.Kind)) continue;

                LevelBuilder.WallFace(mark.X, mark.Z, mark.Side, out Vector3 center, out Vector3 normal);
                Vector3 tangent = Mathf.Abs(normal.z) > 0.5f ? Vector3.right : Vector3.forward;
                Vector3 from = builder.transform.TransformPoint(center + tangent * mark.Along + normal * 2.2f);
                ShootPair(player, camera, torch, target, readback, folder, "marca_" + mark.Kind.ToString().ToLowerInvariant(), from, -normal);
            }

            if (FindAlcove(data, out Vector3 origin, out Vector3 opening))
            {
                Vector3 from = builder.transform.TransformPoint(origin + opening * 2.8f + Vector3.Cross(Vector3.up, opening) * 1.2f);
                Vector3 look = (builder.transform.TransformPoint(origin) - from).normalized;
                ShootPair(player, camera, torch, target, readback, folder, "esconderijo", from, look);
            }

            LevelBuilder.WallFace(data.ExitX, data.ExitZ, data.ExitSide, out Vector3 exitCenter, out Vector3 exitNormal);
            ShootPair(player, camera, torch, target, readback, folder, "saida",
                      builder.transform.TransformPoint(exitCenter + exitNormal * 3f), -exitNormal);
        }

        static void ShootPair(Transform player, Camera camera, Light torch, RenderTexture target, Texture2D readback,
                              string folder, string name, Vector3 position, Vector3 lookDirection)
        {
            lookDirection.y = 0f;
            player.position = new Vector3(position.x, 0.05f, position.z);
            player.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);

            Shoot(camera, target, readback, Path.Combine(folder, name + ".png"));

            if (torch == null) return;
            torch.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
            torch.enabled = true;
            Shoot(camera, target, readback, Path.Combine(folder, name + "_lanterna.png"));
            torch.enabled = false;
        }

        // A entidade num trecho reto do caminho: parada a 8 m, correndo a 8 m e de perto. Fora
        // do Play ela não se move sozinha; o corpo é montado e posto em pose só para a foto.
        static void ShootEntity(LevelBuilder builder, Transform player, Camera camera, Light torch,
                                RenderTexture target, Texture2D readback, string folder)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Level0Setup.EntityPrefabPath);
            LevelData data = builder.Data;
            if (prefab == null || !prefab.TryGetComponent(out BackRooms.Entities.EntityBody source)) return;

            // Três células seguidas em linha reta, com luz acesa na do meio se houver.
            int found = -1;
            for (int i = 4; i + 2 < data.SafePathCells.Count; i++)
            {
                int a = data.SafePathCells[i], b = data.SafePathCells[i + 1], c = data.SafePathCells[i + 2];
                if (b - a != c - b) continue;

                if (found < 0) found = i;
                if (data.Lights[b % data.Width, b / data.Width] != LightState.On) continue;

                found = i;
                break;
            }

            if (found < 0) return;

            Vector3 from = builder.CellCenter(data.SafePathCells[found] % data.Width, data.SafePathCells[found] / data.Width);
            Vector3 to = builder.CellCenter(data.SafePathCells[found + 2] % data.Width, data.SafePathCells[found + 2] / data.Width);
            Vector3 look = (to - from).normalized;

            var figure = new GameObject("EntidadeDaFoto");
            var body = figure.AddComponent<BackRooms.Entities.EntityBody>();
            body.bodyMaterial = source.bodyMaterial;
            body.shellMaterial = source.shellMaterial;
            body.boneMaterial = source.boneMaterial;
            body.mawMaterial = source.mawMaterial;
            body.Build();

            Quaternion level = camera.transform.localRotation;

            try
            {
                figure.transform.SetPositionAndRotation(to, Quaternion.LookRotation(-look, Vector3.up));

                body.Pose(0.1f, 0f, 0f, 2f);
                ShootPair(player, camera, torch, target, readback, folder, "entidade_parada", from, look);

                body.Pose(0.2f, 4.6f, 1f, 2f);
                ShootPair(player, camera, torch, target, readback, folder, "entidade_correndo", from, look);

                // De perto, olhando um pouco para cima: ela tem 3 m.
                camera.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
                ShootPair(player, camera, torch, target, readback, folder, "entidade_perto", to - look * 3.2f, look);

                // De lado, para conferir a postura.
                camera.transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);
                figure.transform.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, look), Vector3.up);
                ShootPair(player, camera, torch, target, readback, folder, "entidade_de_lado", to - look * 4.5f, look);
            }
            finally
            {
                camera.transform.localRotation = level;
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        // Três células seguidas em linha reta no caminho seguro, com luz acesa na do meio se
        // houver: a câmera fica na primeira, olhando para a terceira (8 m).
        static bool FindStretch(LevelBuilder builder, out Vector3 from, out Vector3 to)
        {
            LevelData data = builder.Data;
            int found = -1;

            for (int i = 4; i + 2 < data.SafePathCells.Count; i++)
            {
                int a = data.SafePathCells[i], b = data.SafePathCells[i + 1], c = data.SafePathCells[i + 2];
                if (b - a != c - b) continue;

                if (found < 0) found = i;
                if (data.Lights[b % data.Width, b / data.Width] != LightState.On) continue;

                found = i;
                break;
            }

            from = to = Vector3.zero;
            if (found < 0) return false;

            from = builder.CellCenter(data.SafePathCells[found] % data.Width, data.SafePathCells[found] / data.Width);
            to = builder.CellCenter(data.SafePathCells[found + 2] % data.Width, data.SafePathCells[found + 2] / data.Width);
            return true;
        }

        static BackRooms.Entities.EntityBody CreateEntityFigure()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Level0Setup.EntityPrefabPath);
            if (prefab == null || !prefab.TryGetComponent(out BackRooms.Entities.EntityBody source)) return null;

            var figure = new GameObject("EntidadeDaFoto");
            var body = figure.AddComponent<BackRooms.Entities.EntityBody>();
            body.bodyMaterial = source.bodyMaterial;
            body.shellMaterial = source.shellMaterial;
            body.boneMaterial = source.boneMaterial;
            body.mawMaterial = source.mawMaterial;
            body.Build();
            return body;
        }

        // O rosto da entidade de perto, de baixo para cima (ela tem 3 m), parada e no bote.
        static void ShootEntityFace(LevelBuilder builder, Transform player, Camera camera, Light torch,
                                    RenderTexture target, Texture2D readback, string folder)
        {
            if (!FindStretch(builder, out Vector3 from, out Vector3 to)) return;

            BackRooms.Entities.EntityBody body = CreateEntityFigure();
            if (body == null) return;

            Vector3 look = (to - from).normalized;
            Quaternion level = camera.transform.localRotation;

            try
            {
                body.transform.SetPositionAndRotation(to, Quaternion.LookRotation(-look, Vector3.up));

                // De três quartos, que é como a cabeça comprida aparece melhor.
                body.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, 48f, 0f) * -look, Vector3.up);
                body.Pose(0.1f, 0f, 0.4f, 2f);
                camera.transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);
                ShootPair(player, camera, torch, target, readback, folder, "entidade_rosto", to - look * 2.2f, look);

                // Por trás e de lado: daqui não pode aparecer boca nenhuma.
                body.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, 142f, 0f) * -look, Vector3.up);
                body.Pose(0.1f, 0f, 1f, 2f);
                camera.transform.localRotation = Quaternion.Euler(-34f, 0f, 0f);
                ShootPair(player, camera, torch, target, readback, folder, "entidade_por_tras", to - look * 2.0f, look);

                body.transform.rotation = Quaternion.LookRotation(-look, Vector3.up);
                body.Pose(0.1f, 0f, 1f, 2.03f, 1f);
                camera.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                ShootPair(player, camera, torch, target, readback, folder, "entidade_ataque", to - look * 3.2f, look);
            }
            finally
            {
                camera.transform.localRotation = level;
                UnityEngine.Object.DestroyImmediate(body.gameObject);
            }
        }

        // O traje dos jogadores, como os outros o veem: parado de frente, andando com a
        // caneta e a lanterna, e agachado.
        static void ShootPlayerFigure(LevelBuilder builder, GameObject prefab, Transform player, Camera camera, Light torch,
                                      RenderTexture target, Texture2D readback, string folder)
        {
            var source = prefab.GetComponentInChildren<BackRooms.Player.PlayerBody>(true);
            if (source == null || !FindStretch(builder, out Vector3 from, out Vector3 to)) return;

            var figure = new GameObject("JogadorDaFoto");
            var body = figure.AddComponent<BackRooms.Player.PlayerBody>();
            body.suitMaterial = source.suitMaterial;
            body.rubberMaterial = source.rubberMaterial;
            body.visorMaterial = source.visorMaterial;
            body.gearMaterial = source.gearMaterial;
            body.Build();

            Vector3 look = (to - from).normalized;
            Vector3 spot = from + look * 2.6f;

            try
            {
                figure.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-look, Vector3.up));
                body.Pose(0.1f, 0f, 0f, 0f, 0f, 0f, 1f);
                ShootPair(player, camera, torch, target, readback, folder, "jogador_de_frente", from, look);

                figure.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, 55f, 0f) * -look, Vector3.up);
                body.Pose(0.2f, 2.8f, 0f, 8f, 1f, 1f, 1f);
                ShootPair(player, camera, torch, target, readback, folder, "jogador_andando", from, look);

                figure.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, -70f, 0f) * -look, Vector3.up);
                body.Pose(0.15f, 1.2f, 1f, 15f, 0f, 1f, 1f);
                ShootPair(player, camera, torch, target, readback, folder, "jogador_agachado", from, look);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        // A mão em primeira pessoa com a caneta, como o dono a vê, de frente para uma parede.
        static void ShootHand(LevelBuilder builder, GameObject prefab, Transform player, Camera camera,
                              RenderTexture target, Texture2D readback, string folder)
        {
            var source = prefab.GetComponentInChildren<BackRooms.Player.ItemHand>(true);
            if (source == null) return;

            var holder = new GameObject("MaoDaFoto");
            holder.transform.SetParent(camera.transform, false);
            holder.transform.localPosition = source.transform.localPosition;
            holder.transform.localRotation = source.transform.localRotation;

            var hand = holder.AddComponent<BackRooms.Player.ItemHand>();
            hand.suitMaterial = source.suitMaterial;
            hand.gloveMaterial = source.gloveMaterial;
            hand.penMaterial = source.penMaterial;
            hand.Build();

            try
            {
                // Na sala inicial, a 1,4 m da parede norte.
                player.position = builder.SpawnCenter + new Vector3(0f, 0.05f, LevelBuilder.Cell - 1.5f);
                player.rotation = Quaternion.identity;
                Shoot(camera, target, readback, Path.Combine(folder, "mao_com_caneta.png"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        // O apagão: todas as luminárias escuras e sem luz ambiente, com e sem a lanterna.
        static void ShootBlackout(LevelBuilder builder, Transform player, Camera camera, Light torch,
                                  RenderTexture target, Texture2D readback, string folder)
        {
            var lightsOut = UnityEngine.Object.FindFirstObjectByType<LightsOut>();
            if (lightsOut == null || !FindStretch(builder, out Vector3 from, out Vector3 to)) return;

            CeilingLight[] lights = builder.GetComponentsInChildren<CeilingLight>();
            BackRooms.Entities.EntityBody body = CreateEntityFigure();
            Vector3 look = (to - from).normalized;

            try
            {
                foreach (CeilingLight light in lights) light.SetPower(PowerPhase.Dark);
                lightsOut.SetDarkness(1f);

                ShootPair(player, camera, torch, target, readback, folder, "apagao", from, look);

                // E com a entidade no fim do corredor: só os olhos e a boca aparecem.
                if (body != null)
                {
                    body.transform.SetPositionAndRotation(to, Quaternion.LookRotation(-look, Vector3.up));
                    body.Pose(0.1f, 0f, 1f, 2f);
                    ShootPair(player, camera, torch, target, readback, folder, "apagao_com_entidade", from, look);
                }
            }
            finally
            {
                foreach (CeilingLight light in lights) light.SetPower(PowerPhase.Steady);
                lightsOut.SetDarkness(0f);
                if (body != null) UnityEngine.Object.DestroyImmediate(body.gameObject);
            }
        }

        // Dois buracos, cada um visto de três jeitos: da beirada, de cima da faixa olhando
        // para baixo e da beirada com a lanterna acesa.
        static void ShootPits(LevelBuilder builder, Transform player, Camera camera, Light torch,
                              RenderTexture target, Texture2D readback, string folder)
        {
            LevelData data = builder.Data;
            Quaternion level = camera.transform.localRotation;
            int shot = 0;

            foreach (PitArea area in data.Pits)
            {
                if (shot >= 2 || !FindPitEntry(data, area, out int x, out int z, out WallSide side)) continue;

                data.Neighbor(x, z, side, out int nx, out int nz);
                Vector3 inside = builder.CellCenter(x, z);
                Vector3 outside = builder.CellCenter(nx, nz);
                Vector3 look = (inside - outside).normalized;
                string name = "buraco_" + shot++;

                // Da beirada, um passo antes de a faixa começar.
                camera.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
                ShootPair(player, camera, torch, target, readback, folder, name, outside + look * 0.6f, look);

                // Em cima da faixa, olhando para o fundo.
                camera.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);
                player.position = new Vector3(inside.x, 0.05f, inside.z) - look * 1.2f;
                player.rotation = Quaternion.LookRotation(look, Vector3.up);
                Shoot(camera, target, readback, Path.Combine(folder, name + "_faixa.png"));
            }

            camera.transform.localRotation = level;
        }

        // Uma célula do buraco em que a faixa chega até a beirada, e o lado por onde ela sai.
        static bool FindPitEntry(LevelData data, PitArea area, out int x, out int z, out WallSide side)
        {
            for (z = area.Z0; z < area.Z0 + area.Height; z++)
                for (x = area.X0; x < area.X0 + area.Width; x++)
                    foreach (WallSide candidate in new[] { WallSide.South, WallSide.North, WallSide.West, WallSide.East })
                    {
                        if (!data.HasStrip(x, z, candidate)) continue;
                        if (!data.Neighbor(x, z, candidate, out int nx, out int nz) || data.Pit[nx, nz]) continue;

                        side = candidate;
                        return true;
                    }

            x = z = 0;
            side = WallSide.South;
            return false;
        }

        static bool FindAlcove(LevelData data, out Vector3 origin, out Vector3 opening)
        {
            for (int z = 1; z < data.Height; z++)
            {
                for (int x = 0; x < data.Width; x++)
                {
                    if (data.AlcoveSouth[x, z] == 0) continue;
                    origin = new Vector3((x + 0.5f) * LevelBuilder.Cell, 0f, z * LevelBuilder.Cell);
                    opening = data.AlcoveSouth[x, z] == 1 ? Vector3.back : Vector3.forward;
                    return true;
                }
            }

            for (int x = 1; x < data.Width; x++)
            {
                for (int z = 0; z < data.Height; z++)
                {
                    if (data.AlcoveWest[x, z] == 0) continue;
                    origin = new Vector3(x * LevelBuilder.Cell, 0f, (z + 0.5f) * LevelBuilder.Cell);
                    opening = data.AlcoveWest[x, z] == 1 ? Vector3.left : Vector3.right;
                    return true;
                }
            }

            origin = Vector3.zero;
            opening = Vector3.forward;
            return false;
        }

        static void Shoot(Camera camera, RenderTexture target, Texture2D readback, string path)
        {
            // Vários quadros para o antisserrilhamento temporal e a névoa estabilizarem.
            for (int i = 0; i < 16; i++) camera.Render();

            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            readback.Apply();
            File.WriteAllBytes(path, readback.EncodeToPNG());
        }

        // Procura a célula sem luminária mais distante de qualquer luz acesa.
        static bool FindDarkCell(LevelBuilder builder, out Vector3 position)
        {
            LevelData data = builder.Data;
            int bestX = -1, bestZ = -1, bestDistance = -1;

            for (int z = 0; z < data.Height; z++)
            {
                for (int x = 0; x < data.Width; x++)
                {
                    if (data.Lights[x, z] == LightState.On || data.Lights[x, z] == LightState.Flicker) continue;

                    int nearest = int.MaxValue;
                    for (int lz = 0; lz < data.Height; lz++)
                        for (int lx = 0; lx < data.Width; lx++)
                            if (data.Lights[lx, lz] == LightState.On || data.Lights[lx, lz] == LightState.Flicker)
                                nearest = Mathf.Min(nearest, Mathf.Abs(lx - x) + Mathf.Abs(lz - z));

                    if (nearest != int.MaxValue && nearest > bestDistance)
                    {
                        bestDistance = nearest;
                        bestX = x;
                        bestZ = z;
                    }
                }
            }

            position = bestX >= 0 ? builder.CellCenter(bestX, bestZ) : Vector3.zero;
            return bestX >= 0;
        }
    }
}
