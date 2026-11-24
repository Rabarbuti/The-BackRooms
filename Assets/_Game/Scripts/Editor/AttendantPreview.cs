using System;
using System.IO;
using BackRooms.Entities;
using BackRooms.World;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// O Ascensorista ("The Attendant"), o inimigo do elevador do Level 3: o prefab de rede e as
    /// fotos dentro da fase.
    ///
    /// <see cref="CreatePrefab"/> grava Prefabs/Attendant.prefab (só componentes; o corpo se monta em
    /// código quando ele aparece), que o <see cref="Level3Setup"/> põe no <see cref="EntityDirector"/>
    /// da fase. Os materiais (do corpo provisório e do painel em cima das portas) ficam em
    /// Art/Personagens/Ascensorista e são regravados a cada montagem; os do modelo do Blender, pelo
    /// <see cref="CharacterModels"/>.
    ///
    /// Menu BackRooms > Capturar prévia do Ascensorista; linha de comando
    /// <see cref="BatchCapture"/> (-seed N). Fotos em Logs/Previews/ascensorista.
    /// </summary>
    public static class AttendantPreview
    {
        public const string PrefabPath = "Assets/_Game/Prefabs/Attendant.prefab";
        public const string ModelName = "Ascensorista";
        const string ArtFolder = CharacterModels.ArtFolder + "/" + ModelName;
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";

        /// <summary>Grava o prefab de rede do Ascensorista. O Netcode o registra sozinho em DefaultNetworkPrefabs.asset.</summary>
        public static GameObject CreatePrefab()
        {
            var root = new GameObject("Attendant");

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.radius = 0.34f;
            capsule.height = 2.0f;
            capsule.center = new Vector3(0f, 1.0f, 0f);

            var rigid = root.AddComponent<Rigidbody>();
            rigid.isKinematic = true;
            rigid.useGravity = false;

            var body = root.AddComponent<AttendantBody>();
            AssignMaterials(body);
            root.AddComponent<AttendantAudio>();

            root.AddComponent<NetworkObject>();
            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleZ = false;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;
            networkTransform.Interpolate = true;

            // Ronda devagar (o jogador anda a 2,8 m/s); ouviu, vai a 3,4 (só correndo se foge dele, e
            // correr faz barulho). Cego: sem visão.
            var brain = root.AddComponent<AttendantBrain>();
            brain.roamSpeed = 1.2f;
            brain.searchSpeed = 1.6f;
            brain.investigateSpeed = 3.4f;
            brain.chaseSpeed = 3.4f;
            brain.acceleration = 10f;
            brain.hearCells = 9f;
            brain.sightRange = 0f;
            brain.eyeHeight = 1.9f;
            brain.bodyRadius = 0.36f;
            brain.catchRadius = 1.0f;
            brain.feedSeconds = 3f;
            brain.searchSeconds = 6f;
            brain.indicatorOn = Unlit("Painel_Aceso", new Color(1f, 0.62f, 0.18f));
            brain.indicatorOff = Lit("Painel_Apagado", Rgb(22, 20, 18), 0.6f);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            Level0Setup.StampNetworkId(prefab);
            return prefab;
        }

        public static void AssignMaterials(AttendantBody body)
        {
            EnsureFolder(ArtFolder);
            body.uniformMaterial = Lit("Peca_Uniforme", Rgb(78, 22, 28), 0.25f);
            body.skinMaterial = Lit("Peca_Pele", Rgb(150, 142, 132), 0.35f);
            body.gauzeMaterial = Lit("Peca_Gaze", Rgb(200, 190, 168), 0.15f);
            body.brassMaterial = Lit("Peca_Latao", Rgb(170, 132, 58), 0.6f, 0.8f);
            body.capMaterial = Lit("Peca_Quepe", Rgb(28, 20, 22), 0.4f);
            body.caneMaterial = Lit("Peca_Bengala", Rgb(222, 220, 212), 0.5f);
            body.redMaterial = Lit("Peca_Vermelho", Rgb(120, 18, 16), 0.45f);
            body.model = CharacterModels.Model(ModelName);
            body.modelMaterials = CharacterModels.Materials(ModelName);
        }

        public static AttendantBody CreateFigure()
        {
            var figure = new GameObject("AscensoristaDaFoto");
            var body = figure.AddComponent<AttendantBody>();
            AssignMaterials(body);
            body.Build();
            return body;
        }

        // ---------------------------------------------------------------- Fotos

        [MenuItem("BackRooms/Capturar prévia do Ascensorista")]
        public static void CaptureFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string folder = Capture(1998);
            if (folder != null) EditorUtility.RevealInFinder(folder);
        }

        /// <summary>Linha de comando (-seed N, padrão 1998): fotos dentro do Level 3. Sai com 0 se tirou.</summary>
        public static void BatchCapture()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-seed");
                int seed = index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out int s) ? s : 1998;
                bool ok = Capture(seed) != null;
                Level1Setup.ExitWithLevel0Open(ok ? 0 : 2);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Level1Setup.ExitWithLevel0Open(1);
            }
        }

        static string Capture(int seed)
        {
            if (!File.Exists(Level3Setup.ScenePath)) Level3Setup.BuildScene();
            EditorSceneManager.OpenScene(Level3Setup.ScenePath, OpenSceneMode.Single);

            var builder = UnityEngine.Object.FindAnyObjectByType<LevelBuilder>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (builder == null || prefab == null)
            {
                Debug.LogError("[BackRooms] Prévia do Ascensorista: a cena do Level 3 não tem LevelBuilder ou falta o prefab do jogador.");
                return null;
            }

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews", "ascensorista"));
            Directory.CreateDirectory(folder);
            foreach (string old in Directory.GetFiles(folder, "*.png")) File.Delete(old);

            GameObject rig = Level0Preview.CreateCameraRig(prefab, out Camera camera);
            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            AttendantBody body = null;
            int shots = 0;

            try
            {
                builder.seed = seed;
                builder.Rebuild();
                foreach (HDAdditionalLightData light in builder.GetComponentsInChildren<HDAdditionalLightData>(true))
                    light.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
                camera.targetTexture = target;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 150f);
                Light torch = rig.GetComponentInChildren<Light>(true);

                LevelData d = builder.Data;
                OfficeElevator shaft = d.Elevators.Find(e => !e.IsCab);
                Vector3 toLobby = Side(shaft.Side);
                Vector3 cab = builder.CellCenter(shaft.X, shaft.Z);
                Vector3 front = cab + toLobby * LevelBuilder.Cell;
                ElevatorDoors door = null;
                foreach (ElevatorDoors candidate in UnityEngine.Object.FindObjectsByType<ElevatorDoors>())
                    if (!candidate.IsCab && (Flat(candidate.transform.position - (cab + toLobby * 2f))).magnitude < 0.6f) door = candidate;

                body = CreateFigure();
                Transform figure = body.transform;

                void Shot(string name, Vector3 eye, Vector3 at, float pitch, bool lamp)
                {
                    Vector3 flat = Flat(at - eye);
                    rig.transform.SetPositionAndRotation(new Vector3(eye.x, 0.05f, eye.z), Quaternion.LookRotation(flat.normalized, Vector3.up));
                    camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                    if (torch != null)
                    {
                        torch.enabled = lamp;
                        torch.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                    }
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, name + ".png"));
                    shots++;
                }

                // 1. A chegada: a porta do poço aberta, ele parado dentro da cabine, escutando.
                if (door != null) { door.Snap(1f); door.SetCabLight(true); }
                figure.SetPositionAndRotation(cab, Quaternion.LookRotation(toLobby, Vector3.up));
                body.Pose(0f, 0f, 1f, 0.8f, 0.3f);
                Shot("chegada", front + toLobby * 3.5f, cab, -6f, false);
                Shot("chegada_lanterna", front + toLobby * 3.5f, cab, -6f, true);

                // 2. No saguão, de frente, de lado, de costas e de baixo (como o jogador agachado o vê).
                figure.SetPositionAndRotation(front, Quaternion.LookRotation(toLobby, Vector3.up));
                body.Pose(0f, 0f, 1f, 0f, 0.3f);
                Shot("frente", front + toLobby * 4.2f, front, -10f, false);
                Shot("frente_lanterna", front + toLobby * 4.2f, front, -10f, true);
                Shot("frente_perto", front + toLobby * 1.7f, front, -22f, true);
                Shot("de_baixo", front + toLobby * 1.3f, front, -38f, true);
                figure.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, toLobby), Vector3.up);
                body.Pose(0.3f, 1.1f, 0f, 0f, 0.3f);
                Shot("lado_andando", front + toLobby * 4.2f, front, -10f, true);
                figure.rotation = Quaternion.LookRotation(-toLobby, Vector3.up);
                body.Pose(0f, 0f, 1f, 0f, 0.3f);
                Shot("costas", front + toLobby * 4.2f, front, -10f, true);
                figure.rotation = Quaternion.LookRotation(toLobby, Vector3.up);
                body.Pose(0f, 0f, 1f, 1f, 0.3f);
                Shot("escutando", front + toLobby * 2.6f, front, -16f, true);

                // 3. Correndo atrás de um barulho, curvado, braços para a frente.
                body.Pose(0.25f, 3.4f, 0f, 0f, 0.3f);
                Shot("correndo", front + toLobby * 3.2f, front, -8f, true);

                // 3b. Levando alguém pelo pescoço: o braço esquerdo erguido à frente, de lado e de frente.
                figure.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, toLobby), Vector3.up);
                body.Pose(0.3f, 1.1f, 0f, 0f, 0.3f, 1f);
                Shot("levando_lado", front + toLobby * 4.2f, front, -10f, true);
                figure.rotation = Quaternion.LookRotation(toLobby, Vector3.up);
                Shot("levando_frente", front + toLobby * 3.2f, front, -12f, true);

                // 4. No fim do corredor mais comprido, no claro e (com as luzes apagadas) só na lanterna.
                if (Level0Preview.FindStretch(builder, out Vector3 from, out Vector3 to))
                {
                    Vector3 look = Flat(to - from).normalized;
                    figure.SetPositionAndRotation(new Vector3(to.x, 0f, to.z), Quaternion.LookRotation(-look, Vector3.up));
                    body.Pose(0f, 0f, 1f, 0.6f, 0.3f);
                    Shot("corredor", from, to, -2f, false);
                    foreach (CeilingLight lamp in CeilingLight.All) if (lamp != null) lamp.SetForcedDark(true);
                    Shot("corredor_escuro_lanterna", to - look * 7f, to, -6f, true);
                    foreach (CeilingLight lamp in CeilingLight.All) if (lamp != null) lamp.SetForcedDark(false);
                }

                // 5. Close da cabeça (a gaze nos olhos tem de ler de longe).
                Transform head = FindDeep(figure, "Cabeca");
                if (head != null)
                {
                    figure.SetPositionAndRotation(front, Quaternion.LookRotation(toLobby, Vector3.up));
                    body.Pose(0f, 0f, 1f, 0f, 0.3f);
                    Vector3 face = head.position + Vector3.up * 0.12f;
                    rig.transform.SetPositionAndRotation(face + toLobby * 0.75f - Vector3.up * camera.transform.localPosition.y, Quaternion.LookRotation(-toLobby, Vector3.up));
                    camera.transform.localRotation = Quaternion.identity;
                    if (torch != null)
                    {
                        Vector3 lamp = face + toLobby * 2.2f + Vector3.up * 0.4f;
                        torch.transform.SetPositionAndRotation(lamp, Quaternion.LookRotation(face - lamp));
                        torch.enabled = true;
                    }
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "rosto.png"));
                    shots++;
                }

                if (door != null) { door.Snap(0f); door.SetCabLight(false); }
                Debug.Log($"[BackRooms] Prévia do Ascensorista: {shots} fotos em {folder} (modelo do Blender: {(body.model != null ? "sim" : "não, corpo provisório")}).");
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
                EditorSceneManager.OpenScene(Level3Setup.ScenePath, OpenSceneMode.Single);
            }
        }

        static Vector3 Side(WallSide side)
        {
            switch (side)
            {
                case WallSide.North: return Vector3.forward;
                case WallSide.South: return Vector3.back;
                case WallSide.East: return Vector3.right;
                default: return Vector3.left;
            }
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
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

        static Material Lit(string name, Color color, float smoothness, float metallic = 0f)
        {
            string path = ArtFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(Shader.Find("HDRP/Lit")) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            HDMaterial.ValidateMaterial(material);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        // Sem luz: a cor é a da tela (0 a 1). A seta acesa do painel.
        static Material Unlit(string name, Color color)
        {
            EnsureFolder(ArtFolder);
            string path = ArtFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(Shader.Find("HDRP/Unlit")) { name = name };
            material.SetColor("_UnlitColor", color);
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

        static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);
    }
}
