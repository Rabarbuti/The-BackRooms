using System;
using System.IO;
using BackRooms.Entities;
using BackRooms.World;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Fotos do Capitão, o inimigo do Level 1 (Docs/level1-mostruario.md), antes de ele entrar
    /// no jogo. Como o Level 1 ainda não existe, ele é fotografado dentro do Level 0, com a
    /// mesma câmera, lanterna e filtro de filmadora do jogo.
    ///
    /// Menu BackRooms > Capturar prévia do Capitão. As imagens vão para Logs/Previews.
    /// Os materiais dele ficam em Art/Level1/Capitao e são regravados a cada captura (as
    /// cores vivem aqui, no código).
    /// </summary>
    public static class CaptainPreview
    {
        const string ScenePath = "Assets/_Game/Scenes/Level0_Test.unity";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        const string ArtFolder = "Assets/_Game/Art/Level1/Capitao";

        [MenuItem("BackRooms/Capturar prévia do Capitão")]
        public static void CaptureFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string folder = Capture(DefaultFolder());
            if (folder != null) EditorUtility.RevealInFinder(folder);
        }

        /// <summary>Uso em linha de comando: captura e fecha o editor (0 = ok).</summary>
        public static void BatchCapture()
        {
            try
            {
                bool ok = Capture(DefaultFolder()) != null;
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

        public const string PrefabPath = "Assets/_Game/Prefabs/Captain.prefab";

        /// <summary>
        /// Grava o prefab de rede do Capitão (Prefabs/Captain.prefab), que o Level1Setup põe no
        /// diretor da fase. Como o da entidade: só componentes; o corpo é montado em código quando
        /// ele aparece. O colisor impede o jogador de atravessá-lo e o corpo rígido cinemático faz
        /// a caneta não riscar nele. O Netcode registra o prefab sozinho em
        /// DefaultNetworkPrefabs.asset.
        /// </summary>
        public static GameObject CreatePrefab()
        {
            var captain = new GameObject("Captain");

            var capsule = captain.AddComponent<CapsuleCollider>();
            capsule.radius = 0.38f;
            capsule.height = 2.7f;
            capsule.center = new Vector3(0f, 1.35f, 0f);

            var rigid = captain.AddComponent<Rigidbody>();
            rigid.isKinematic = true;
            rigid.useGravity = false;

            var body = captain.AddComponent<CaptainBody>();
            AssignMaterials(body);
            body.model = CharacterModels.Model("Capitao");
            body.modelMaterials = CharacterModels.Materials("Capitao");
            captain.AddComponent<CaptainAudio>();
            // A perna de pau marca o carpete (pista para separar do totem).
            captain.AddComponent<PegMarks>().markMaterial = PegMarkMaterial();

            // Rede: quem manda na posição é o host.
            captain.AddComponent<NetworkObject>();
            var networkTransform = captain.AddComponent<NetworkTransform>();
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleZ = false;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;
            networkTransform.Interpolate = true;

            // Manca devagar (1,7 m/s caçando; o jogador anda a 2,8): o perigo é chegar por trás.
            // No apagão, as arrancadas de 5,2 m/s (CaptainBrain).
            var brain = captain.AddComponent<CaptainBrain>();
            brain.roamSpeed = 1.2f;
            brain.searchSpeed = 1.5f;
            brain.investigateSpeed = 1.7f;
            brain.chaseSpeed = 1.7f;
            brain.acceleration = 14f;
            brain.sightRange = 22f;
            brain.eyeHeight = 2.45f;
            brain.bodyRadius = 0.4f;
            brain.catchRadius = 1.3f;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(captain, PrefabPath);
            UnityEngine.Object.DestroyImmediate(captain);
            Level0Setup.StampNetworkId(prefab);
            return prefab;
        }

        // Decalque da marca da perna de pau: um círculo escuro de carpete amassado, mais fundo no
        // meio, com a borda sumindo (alfa). Textura e material em Art/Level1/Capitao.
        static Material PegMarkMaterial()
        {
            EnsureFolder(ArtFolder);
            string texturePath = ArtFolder + "/MarcaPernaDePau.png";
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
                    float alpha = Mathf.Clamp01((1f - d) * 2.2f);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.62f) * 6f);
                    float shade = Mathf.Lerp(0.16f, 0.08f, ring);
                    texture.SetPixel(x, y, new Color(shade, shade * 0.85f, shade * 0.6f, alpha * 0.85f));
                }
            File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            if (importer != null && !importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            string path = ArtFolder + "/Decal_MarcaPernaDePau.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(Shader.Find("HDRP/Decal")) { name = "Decal_MarcaPernaDePau" };
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetFloat("_DecalBlend", 1f);
            material.SetFloat("_AffectAlbedo", 1f);
            material.SetFloat("_AffectNormal", 0f);
            material.SetFloat("_AffectMetal", 0f);
            material.SetFloat("_AffectAO", 0f);
            material.SetFloat("_AffectSmoothness", 0f);
            material.SetFloat("_MaskBlendSrc", 0f);
            HDMaterial.ValidateMaterial(material);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Monta o Capitão com os materiais dele (criados ou atualizados aqui).</summary>
        public static CaptainBody CreateFigure()
        {
            var figure = new GameObject("CapitaoDaFoto");
            var body = figure.AddComponent<CaptainBody>();
            AssignMaterials(body);
            body.model = CharacterModels.Model("Capitao");
            body.modelMaterials = CharacterModels.Materials("Capitao");
            body.Build();
            return body;
        }

        public static void AssignMaterials(CaptainBody body)
        {
            EnsureFolder(ArtFolder);

            body.skinMaterial = Lit("Capitao_Pele", Rgb(112, 72, 50), 0.38f);
            body.creaseMaterial = Lit("Capitao_Vinco", Rgb(56, 32, 22), 0.3f);
            body.hairMaterial = Lit("Capitao_Cabelo", Rgb(16, 14, 13), 0.2f);
            body.eyeMaterial = Lit("Capitao_Olho", Rgb(232, 226, 210), 0.8f);
            body.irisMaterial = Lit("Capitao_Iris", Rgb(74, 44, 24), 0.8f);
            body.pupilMaterial = Lit("Capitao_Pupila", Rgb(6, 6, 6), 0.9f);
            body.toothMaterial = Lit("Capitao_Dente", Rgb(214, 206, 180), 0.5f);
            body.mawMaterial = Lit("Capitao_Boca", Rgb(26, 8, 8), 0.5f);
            body.hatMaterial = Lit("Capitao_Chapeu", Rgb(66, 42, 24), 0.15f);
            body.ribbonMaterial = Lit("Capitao_Fita", Rgb(232, 230, 222), 0.3f);
            body.coatMaterial = Lit("Capitao_Casaco", Rgb(200, 176, 124), 0.15f);
            body.shirtMaterial = Lit("Capitao_Camisa", Rgb(150, 192, 160), 0.3f);
            body.sashMaterial = Lit("Capitao_Faixa", Rgb(124, 98, 118), 0.2f);
            body.beltMaterial = Lit("Capitao_Cinto", Rgb(20, 19, 19), 0.35f);
            body.brassMaterial = Lit("Capitao_Latao", Rgb(196, 156, 60), 0.6f, 0.7f);
            body.pantsMaterial = Lit("Capitao_Calca", Rgb(52, 50, 50), 0.15f);
            body.bootMaterial = Lit("Capitao_Bota", Rgb(80, 54, 34), 0.3f);
            body.woodMaterial = Lit("Capitao_Madeira", Rgb(118, 82, 50), 0.22f);
            body.bloodMaterial = Lit("Capitao_Sangue", Rgb(104, 28, 20), 0.45f);
            body.bruiseMaterial = Lit("Capitao_Hematoma", Rgb(66, 36, 48), 0.35f);
            body.patchMaterial = Lit("Capitao_Remendo", Rgb(40, 34, 30), 0.1f);
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

            GameObject rig = Level0Preview.CreateCameraRig(prefab, out Camera camera);
            Directory.CreateDirectory(folder);

            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            CaptainBody body = null;

            try
            {
                builder.Rebuild();

                var camcorder = UnityEngine.Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();
                if (camcorder != null) camcorder.Draw(true, 412.3f, 412.3f, 0.6f, false);

                Transform player = rig.transform;
                var torch = player.GetComponentInChildren<Light>(true);
                camera.targetTexture = target;

                if (!Level0Preview.FindStretch(builder, out Vector3 from, out Vector3 to))
                {
                    Debug.LogError("[BackRooms] Não achei um trecho reto para fotografar o Capitão.");
                    return null;
                }

                Vector3 look = (to - from).normalized;
                body = CreateFigure();
                Transform figure = body.transform;
                Quaternion level = camera.transform.localRotation;
                Quaternion facing = Quaternion.LookRotation(-look, Vector3.up);

                // Parado no fim do corredor, duro, de braços abertos, a 8 m.
                figure.SetPositionAndRotation(to, facing);
                body.Pose(0f, 0f, 1f, 0f, 0.3f);
                Level0Preview.ShootPair(player, camera, torch, target, readback, folder, "capitao_parado", from, look);

                // Mancando na direção da câmera, apoiado na perna de pau.
                body.Pose(0.75f, 1.7f, 0f, 0.15f, 0.3f);
                Level0Preview.ShootPair(player, camera, torch, target, readback, folder, "capitao_andando", from, look);

                // De perto, olhando para cima: ele tem 2,8 m com o chapéu.
                body.Pose(0f, 0f, 1f, 0f, 0.3f);
                camera.transform.localRotation = Quaternion.Euler(-16f, 0f, 0f);
                Level0Preview.ShootPair(player, camera, torch, target, readback, folder, "capitao_perto", to - look * 3.4f, look);

                // O rosto visto de baixo, como o jogador o vê.
                body.Pose(0f, 0f, 1f, 0f, 0.3f);
                camera.transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);
                Level0Preview.ShootPair(player, camera, torch, target, readback, folder, "capitao_rosto", to - look * 2.1f, look);

                // Retrato na altura dos olhos dele, para conferir o rosto e a roupa de perto
                // (a câmera sobe; no jogo ninguém o vê daqui).
                camera.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
                player.SetPositionAndRotation(to - look * 1.25f + Vector3.up * 0.82f, Quaternion.LookRotation(look, Vector3.up));
                if (torch != null)
                {
                    torch.transform.SetPositionAndRotation(camera.transform.position + Vector3.up * 0.3f, camera.transform.rotation);
                    torch.enabled = true;
                }
                Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "capitao_retrato.png"));
                figure.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, 35f, 0f) * -look, Vector3.up);
                Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "capitao_retrato_34.png"));

                // Close do rosto, de frente e de três quartos, para ver os machucados e o olho caído.
                Transform face = FindDeep(figure, "Cabeca_Centro");
                if (face != null)
                {
                    foreach (float turn in new[] { 0f, 30f })
                    {
                        figure.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, turn, 0f) * -look, Vector3.up);
                        Vector3 eye = face.position - look * 0.62f;
                        player.SetPositionAndRotation(eye - camera.transform.localPosition.y * Vector3.up, Quaternion.LookRotation(look, Vector3.up));
                        camera.transform.localRotation = Quaternion.identity;
                        // A lanterna fica atrás, a uns 2 m, como na mão de quem filma: colada no rosto estoura a imagem.
                        Vector3 lamp = face.position - look * 2.2f + Vector3.up * 0.3f;
                        if (torch != null) torch.transform.SetPositionAndRotation(lamp, Quaternion.LookRotation(face.position - lamp));
                        Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, turn == 0f ? "capitao_close.png" : "capitao_close_34.png"));
                    }
                }

                if (torch != null) torch.enabled = false;
                figure.rotation = facing;

                // Caçando: curvado, braços para a frente e a boca escancarada.
                figure.rotation = facing;
                body.Pose(0.25f, 4.2f, 0f, 1f, 0.3f);
                camera.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                Level0Preview.ShootPair(player, camera, torch, target, readback, folder, "capitao_boca", to - look * 2.6f, look);

                // De lado e de costas, para conferir a silhueta, a perna de pau e o casaco.
                body.Pose(0.75f, 1.7f, 0f, 0f, 0.3f);
                camera.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
                figure.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, look), Vector3.up);
                Level0Preview.ShootPair(player, camera, torch, target, readback, folder, "capitao_de_lado", to - look * 4.6f, look);

                figure.rotation = Quaternion.LookRotation(look, Vector3.up);
                body.Pose(0f, 0f, 1f, 0f, 0.3f);
                Level0Preview.ShootPair(player, camera, torch, target, readback, folder, "capitao_costas", to - look * 4.6f, look);

                camera.transform.localRotation = level;
                Debug.Log("[BackRooms] Prévia do Capitão salva em " + folder);
                return folder;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(readback);
                if (body != null) UnityEngine.Object.DestroyImmediate(body.gameObject);
                UnityEngine.Object.DestroyImmediate(rig);
                ShaderUtil.allowAsyncCompilation = asyncBefore;
                AssetDatabase.SaveAssets();

                // Reabre a cena do disco para descartar o mapa gerado só para a foto.
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        // ---------------------------------------------------------------- Materiais

        // Cria o material na primeira vez e regrava as cores sempre, para o ajuste ficar no código.
        static Material Lit(string name, Color color, float smoothness, float metallic = 0f)
        {
            string path = ArtFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;

            if (created)
            {
                Shader shader = Shader.Find("HDRP/Lit");
                if (shader == null)
                {
                    Debug.LogError("[BackRooms] Shader HDRP/Lit não encontrado.");
                    return null;
                }
                material = new Material(shader) { name = name };
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            HDMaterial.ValidateMaterial(material);

            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f);
        }
    }
}
