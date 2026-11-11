using System;
using System.Collections.Generic;
using BackRooms.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Confere se o que é sólido para a física é o mesmo que aparece na tela. Para várias
    /// seeds, constrói o nível, dispara milhares de raios e compara onde cada um bate nos
    /// colisores (o que o jogo usa, por exemplo, para riscar com a caneta) com onde bate
    /// na malha desenhada. Uma diferença é um "colisor invisível" ou um buraco na colisão.
    /// Menu BackRooms > Conferir colisores do Level 0.
    /// </summary>
    public static class Level0ColliderCheck
    {
        const string ScenePath = "Assets/_Game/Scenes/Level0_Test.unity";
        const int VisualLayer = 30;
        const float Reach = 6f;
        const float Tolerance = 0.02f;

        [MenuItem("BackRooms/Conferir colisores do Level 0")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            int bad = Run(new[] { 1998, 7, 21, 404 }, 4000);
            EditorUtility.DisplayDialog("Level 0", bad == 0 ? "Colisores e malha batem em todos os raios testados." : $"{bad} raios bateram em lugares diferentes. Veja o Console.", "OK");
        }

        public static void BatchRun()
        {
            try
            {
                int bad = Run(new[] { 1998, 7, 21, 404, 1337, 90210, 5, 77777 }, 6000);
                EditorApplication.Exit(bad == 0 ? 0 : 3);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static int Run(int[] seeds, int raysPerSeed)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var builder = UnityEngine.Object.FindFirstObjectByType<LevelBuilder>();
            if (builder == null)
            {
                Debug.LogError("[BackRooms] A cena não tem LevelBuilder.");
                return -1;
            }

            int total = 0;

            foreach (int seed in seeds)
            {
                builder.seed = seed;
                builder.Rebuild();

                // Cópia da malha visível como colisores, numa camada separada.
                var visual = new List<GameObject>();
                foreach (MeshFilter filter in builder.GetComponentsInChildren<MeshFilter>())
                {
                    // Luminárias contam: a calha tem colisor do tamanho dela, e o difusor fica 5 mm
                    // para dentro do aro (sem calha, o painel fica 4 mm abaixo do forro).
                    if (filter.sharedMesh == null) continue;
                    // So a estrutura conta: rodape, porta e placa sao enfeites sem colisor, por projeto.
                    if (filter.name == "Rodapes" || filter.name == "Porta" || filter.name == "Placa") continue;
                    // A fumaca dos buracos aparece mas nao e solida, tambem por projeto.
                    if (filter.GetComponentInParent<PitSmoke>() != null) continue;

                    var go = new GameObject("Visual_" + filter.name) { layer = VisualLayer };
                    go.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                    go.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                    visual.Add(go);
                }

                Physics.SyncTransforms();

                int solidMask = ~(1 << VisualLayer);
                int visualMask = 1 << VisualLayer;
                var rng = new System.Random(seed);
                int bad = 0;
                int grazing = 0;
                var samples = new List<string>();

                for (int i = 0; i < raysPerSeed; i++)
                {
                    // Origem: altura dos olhos, em qualquer ponto de qualquer célula.
                    var origin = new Vector3(
                        (float)rng.NextDouble() * builder.Data.Width * LevelBuilder.Cell,
                        0.4f + (float)rng.NextDouble() * 1.6f,
                        (float)rng.NextDouble() * builder.Data.Height * LevelBuilder.Cell);

                    Vector3 direction = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 1.6f - 0.8f, (float)rng.NextDouble() * 2f - 1f).normalized;

                    // Origem dentro de uma parede não conta.
                    if (Physics.CheckSphere(origin, 0.05f, solidMask, QueryTriggerInteraction.Ignore)) continue;

                    bool hitSolid = Physics.Raycast(origin, direction, out RaycastHit solid, Reach, solidMask, QueryTriggerInteraction.Ignore);
                    bool hitVisual = Physics.Raycast(origin, direction, out RaycastHit seen, Reach, visualMask, QueryTriggerInteraction.Ignore);

                    bool same = hitSolid == hitVisual && (!hitSolid || Mathf.Abs(solid.distance - seen.distance) <= Tolerance);
                    if (same) continue;

                    // Raio que passa raspando numa quina pode acertar a malha e errar a caixa (ou
                    // o contrário) por uma fração de milímetro. Um defeito de verdade tem área:
                    // continua errado com o raio meio grau para qualquer lado. Só esse conta.
                    if (Agrees(origin, direction, solidMask, visualMask))
                    {
                        grazing++;
                        continue;
                    }

                    bad++;
                    if (samples.Count < 6)
                    {
                        string what = hitSolid
                            ? $"colisor a {solid.distance:F2} m em {solid.point} (normal {solid.normal}, objeto {solid.collider.name}, centro {DescribeBox(solid.collider)})"
                            : "nenhum colisor";
                        string shown = hitVisual ? $"malha a {seen.distance:F2} m ({seen.collider.name})" : "nenhuma malha";
                        samples.Add($"  origem {origin} direção {direction}: {what}; {shown}");
                    }
                }

                foreach (GameObject go in visual) UnityEngine.Object.DestroyImmediate(go);

                Debug.Log($"[BackRooms] Colisores seed {seed}: {bad} diferenças em {raysPerSeed} raios ({grazing} de raspão em quina, desconsideradas)." + (samples.Count > 0 ? "\n" + string.Join("\n", samples) : ""));
                total += bad;
            }

            builder.Clear();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return total;
        }

        // Verdadeiro se algum raio vizinho (meio grau para cima, baixo, esquerda ou direita)
        // bate no mesmo lugar nos colisores e na malha.
        static bool Agrees(Vector3 origin, Vector3 direction, int solidMask, int visualMask)
        {
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            right = right.sqrMagnitude < 0.0001f ? Vector3.right : right.normalized;
            Vector3 up = Vector3.Cross(direction, right).normalized;

            foreach (Vector3 axis in new[] { right, -right, up, -up })
            {
                Vector3 nudged = Quaternion.AngleAxis(0.5f, axis) * direction;
                bool hitSolid = Physics.Raycast(origin, nudged, out RaycastHit solid, Reach, solidMask, QueryTriggerInteraction.Ignore);
                bool hitVisual = Physics.Raycast(origin, nudged, out RaycastHit seen, Reach, visualMask, QueryTriggerInteraction.Ignore);

                if (hitSolid == hitVisual && (!hitSolid || Mathf.Abs(solid.distance - seen.distance) <= Tolerance)) return true;
            }

            return false;
        }

        static string DescribeBox(Collider collider)
        {
            return collider is BoxCollider box ? $"{box.center} tamanho {box.size}" : collider.GetType().Name;
        }
    }
}
