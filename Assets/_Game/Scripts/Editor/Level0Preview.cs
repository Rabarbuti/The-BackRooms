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

        /// <summary>Uso em linha de comando: monta a cena, captura e fecha o editor.</summary>
        public static void BatchBuildAndCapture()
        {
            try
            {
                Level0Setup.BuildScene(regenerateAssets: true);
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
                Transform player = playerObject.transform;
                player.position = builder.CellCenter(builder.Data.SpawnX, builder.Data.SpawnZ) + Vector3.up * 0.05f;
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
