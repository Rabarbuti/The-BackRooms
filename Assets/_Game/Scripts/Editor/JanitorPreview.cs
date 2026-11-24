using System;
using System.Collections.Generic;
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
    /// O Zelador, o inimigo do escuro do Level 3: o prefab de rede (Prefabs/Janitor.prefab, gravado a
    /// cada montagem do Level 3 pelo <see cref="Level3Setup"/>), os materiais das peças de código e as
    /// fotos dentro do Escritório (Logs/Previews/zelador): de frente na luz, no escuro com a lanterna,
    /// de lado, de costas, de baixo e o rosto; e rente à parede, antes e depois do encaixe do esfregão
    /// (<see cref="JanitorBody.Fit"/>; o log diz o que entrava na parede em cada uma).
    ///
    /// Menu BackRooms > Capturar prévia do Zelador. Precisa da cena do Level 3 montada.
    /// </summary>
    public static class JanitorPreview
    {
        public const string PrefabPath = "Assets/_Game/Prefabs/Janitor.prefab";
        const string ArtFolder = CharacterModels.ArtFolder + "/Zelador";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        public const string ModelName = "Zelador";

        [MenuItem("BackRooms/Capturar prévia do Zelador")]
        public static void CaptureFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string folder = Capture(Folder());
            if (folder != null) EditorUtility.RevealInFinder(folder);
        }

        /// <summary>Linha de comando: fotografa e fecha (0 = ok).</summary>
        public static void BatchCapture()
        {
            try
            {
                bool ok = Capture(Folder()) != null;
                Level1Setup.ExitWithLevel0Open(ok ? 0 : 2);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Level1Setup.ExitWithLevel0Open(1);
            }
        }

        static string Folder() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews", "zelador"));

        /// <summary>
        /// Grava o prefab de rede do Zelador. Só componentes: o corpo é montado em código quando ele
        /// aparece. O Netcode registra o prefab sozinho em DefaultNetworkPrefabs.asset.
        /// </summary>
        public static GameObject CreatePrefab()
        {
            var janitor = new GameObject("Janitor");

            var capsule = janitor.AddComponent<CapsuleCollider>();
            capsule.radius = 0.34f;
            capsule.height = 2.1f;
            capsule.center = new Vector3(0f, 1.05f, 0f);

            var rigid = janitor.AddComponent<Rigidbody>();
            rigid.isKinematic = true;
            rigid.useGravity = false;

            var body = janitor.AddComponent<JanitorBody>();
            AssignMaterials(body);
            body.model = CharacterModels.Model(ModelName);
            body.modelMaterials = CharacterModels.Materials(ModelName);
            janitor.AddComponent<JanitorAudio>();

            janitor.AddComponent<NetworkObject>();
            var networkTransform = janitor.AddComponent<NetworkTransform>();
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleZ = false;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;
            networkTransform.Interpolate = true;

            // Anda devagar rondando (arrastando o esfregão); caçando corre quase como o jogador e
            // cansa (o jogador corre 5,0 por 6 s, depois anda a 2,8). Enxerga bem no escuro, ouve mal.
            var brain = janitor.AddComponent<JanitorBrain>();
            brain.roamSpeed = 1.4f;
            brain.searchSpeed = 2.0f;
            brain.investigateSpeed = 2.6f;
            brain.chaseSpeed = 4.4f;
            brain.chaseBurstSeconds = 5f;
            brain.chaseTiredSeconds = 4f;
            brain.chaseTiredFactor = 0.55f;
            brain.unseenSpeedFactor = 1.3f;
            brain.acceleration = 10f;
            brain.sightRange = 20f;
            brain.sightHalfAngle = 60f;
            brain.hearCells = 4f;
            brain.eyeHeight = 1.9f;
            brain.bodyRadius = 0.38f;
            brain.catchRadius = 1.3f;
            // À espreita na beira do escuro mais perto do jogador; caça na hora quem pisa no escuro.
            brain.emergeMinCells = 3;
            brain.lurkMinCells = 2;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(janitor, PrefabPath);
            UnityEngine.Object.DestroyImmediate(janitor);
            Level0Setup.StampNetworkId(prefab);
            return prefab;
        }

        public static void AssignMaterials(JanitorBody body)
        {
            EnsureFolder(ArtFolder);
            body.skinMaterial = Lit("ZeladorCodigo_Pele", Rgb(122, 120, 108), 0.55f);
            body.coverallMaterial = Lit("ZeladorCodigo_Macacao", Rgb(72, 82, 72), 0.12f);
            body.wetMaterial = Lit("ZeladorCodigo_Molhado", Rgb(38, 42, 36), 0.75f);
            body.rubberMaterial = Lit("ZeladorCodigo_Borracha", Rgb(24, 24, 22), 0.5f);
            body.capMaterial = Lit("ZeladorCodigo_Gorro", Rgb(46, 40, 36), 0.08f);
            body.metalMaterial = Lit("ZeladorCodigo_Metal", Rgb(170, 160, 128), 0.65f, 0.9f);
            body.handleMaterial = Lit("ZeladorCodigo_Cabo", Rgb(120, 110, 70), 0.4f, 0.3f);
            body.mawMaterial = Lit("ZeladorCodigo_Boca", Rgb(20, 8, 8), 0.5f);
            body.toothMaterial = Lit("ZeladorCodigo_Dente", Rgb(196, 186, 150), 0.45f);
        }

        /// <summary>Monta o Zelador com os materiais (e o modelo, se existir) para a foto.</summary>
        public static JanitorBody CreateFigure()
        {
            var figure = new GameObject("ZeladorDaFoto");
            var body = figure.AddComponent<JanitorBody>();
            AssignMaterials(body);
            body.model = CharacterModels.Model(ModelName);
            body.modelMaterials = CharacterModels.Materials(ModelName);
            body.Build();
            return body;
        }

        // ---------------------------------------------------------------- Fotos

        static string Capture(string folder)
        {
            if (!File.Exists(Level3Setup.ScenePath))
            {
                Debug.LogError("[BackRooms] Cena do Level 3 não encontrada. Rode antes BackRooms > Montar Level 3 (teste).");
                return null;
            }

            EditorSceneManager.OpenScene(Level3Setup.ScenePath, OpenSceneMode.Single);
            var builder = UnityEngine.Object.FindFirstObjectByType<LevelBuilder>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (builder == null || prefab == null)
            {
                Debug.LogError("[BackRooms] A cena não tem LevelBuilder ou falta o prefab do jogador.");
                return null;
            }

            Directory.CreateDirectory(folder);
            foreach (string old in Directory.GetFiles(folder, "*.png")) File.Delete(old);

            GameObject rig = Level0Preview.CreateCameraRig(prefab, out Camera camera);
            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            JanitorBody body = null;
            var taken = new List<string>();

            try
            {
                builder.seed = 1998;
                builder.Rebuild();
                foreach (HDAdditionalLightData light in builder.GetComponentsInChildren<HDAdditionalLightData>(true))
                    light.shadowUpdateMode = ShadowUpdateMode.EveryFrame;

                var camcorder = UnityEngine.Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();
                if (camcorder != null) camcorder.Draw(true, 412.3f, 412.3f, 0.6f, false);

                camera.targetTexture = target;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 150f);
                Transform player = rig.transform;
                var torch = player.GetComponentInChildren<Light>(true);

                if (!LongestCorridor(builder, out Vector3 start, out Vector3 dir, out int length))
                {
                    Debug.LogError("[BackRooms] Não achei corredor reto para fotografar o Zelador.");
                    return null;
                }

                body = CreateFigure();
                Transform figure = body.transform;
                Vector3 spot = start + dir * Mathf.Min(length * LevelBuilder.Cell, 9f);
                Vector3 from = spot - dir * 8f;
                Quaternion facing = Quaternion.LookRotation(-dir, Vector3.up);
                figure.SetPositionAndRotation(spot, facing);

                void Pair(string name, Vector3 at, float pitch)
                {
                    camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                    Level0Preview.ShootPair(player, camera, torch, target, readback, folder, name, at, dir);
                    taken.Add(name);
                }

                // Na luz do corredor: parado a 8 m, andando, caçando.
                body.Pose(0f, 0f, 1f, 0f, 0.3f);
                Pair("zelador_parado", from, 2f);
                body.Pose(0.3f, 1.4f, 0f, 0f, 0.3f);
                Pair("zelador_andando", from, 2f);
                body.Pose(0.3f, 4.4f, 0f, 0f, 0.3f);
                Pair("zelador_cacando", spot - dir * 4.5f, -4f);

                // De perto, de baixo (como o jogador agachado o vê) e o rosto.
                body.Pose(0f, 0f, 1f, 0f, 0.3f);
                Pair("zelador_perto", spot - dir * 2.6f, -14f);
                player.SetPositionAndRotation(spot - dir * 1.7f + Vector3.down * 0.9f, Quaternion.LookRotation(dir, Vector3.up));
                camera.transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);
                Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "zelador_de_baixo.png"));
                taken.Add("zelador_de_baixo");
                player.SetPositionAndRotation(spot - dir * 1.2f + Vector3.up * 0.15f, Quaternion.LookRotation(dir, Vector3.up));
                camera.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
                if (torch != null)
                {
                    torch.transform.SetPositionAndRotation(camera.transform.position - dir * 1.5f + Vector3.up * 0.3f, camera.transform.rotation);
                    torch.enabled = true;
                }
                Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "zelador_rosto.png"));
                taken.Add("zelador_rosto");
                if (torch != null) torch.enabled = false;

                // A lanterna: hesita, o braço na frente do rosto.
                body.Pose(0f, 0f, 1f, 1f, 0.3f);
                Pair("zelador_hesita", spot - dir * 4f, -6f);

                // De lado e de costas: a silhueta, o esfregão e as chaves.
                body.Pose(0.3f, 1.4f, 0f, 0f, 0.3f);
                figure.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir), Vector3.up);
                Pair("zelador_de_lado", spot - dir * 4.5f, -4f);
                figure.rotation = Quaternion.LookRotation(dir, Vector3.up);
                Pair("zelador_costas", spot - dir * 4.5f, -4f);

                // Rente à parede (o esfregão entrava nela): andando com a parede à direita e parado de
                // costas para ela, antes (a pose crua) e depois do encaixe (JanitorBody.Fit).
                Vector3 wallSide = Vector3.Cross(Vector3.up, dir);
                Physics.SyncTransforms();
                if (Physics.Raycast(spot + Vector3.up, wallSide, out RaycastHit wall, 6f, ~0, QueryTriggerInteraction.Ignore))
                {
                    Vector3 rente = wall.point - wallSide * 0.42f;
                    rente.y = spot.y;
                    var where = new List<string>();
                    void WallPair(string name, Quaternion turn, float phase, float speed, float still, Vector3 eye)
                    {
                        figure.SetPositionAndRotation(rente, turn);
                        body.Unfit();
                        body.Pose(phase, speed, still, 0f, 0.3f);
                        Physics.SyncTransforms();
                        body.Penetrations(builder, where);
                        string before = where.Count == 0 ? "nada" : string.Join(", ", where);
                        camera.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
                        Level0Preview.ShootPair(player, camera, null, target, readback, folder, name + "_antes", eye, rente - eye);
                        body.Fit(builder);
                        body.Penetrations(builder, where);
                        string after = where.Count == 0 ? "nada" : string.Join(", ", where);
                        Level0Preview.ShootPair(player, camera, null, target, readback, folder, name + "_depois", eye, rente - eye);
                        taken.Add(name + "_antes");
                        taken.Add(name + "_depois");
                        Debug.Log($"[BackRooms] Zelador rente à parede ({name}): antes entrava na fase: {before}; depois: {after} (esfregão recolhido {body.MopFold:F2} de 2)");
                    }
                    WallPair("zelador_parede_andando", Quaternion.LookRotation(dir, Vector3.up), 0.3f, 1.4f, 0f, rente - dir * 3.2f - wallSide * 1.3f);
                    WallPair("zelador_parede_costas", Quaternion.LookRotation(-wallSide, Vector3.up), 0f, 0f, 1f, rente + dir * 3.2f - wallSide * 1.1f);
                    body.Unfit();
                    figure.position = spot;
                }
                else Debug.LogWarning("[BackRooms] Sem parede ao lado do corredor: sem foto do esfregão rente à parede.");

                // No escuro, como o jogador vai vê-lo: o corredor apagado, só a lanterna.
                figure.rotation = facing;
                body.Pose(0f, 0f, 1f, 0f, 0.3f);
                foreach (CeilingLight lamp in CeilingLight.All) if (lamp != null) lamp.SetForcedDark(true);
                Pair("zelador_escuro_8m", from, 2f);
                Pair("zelador_escuro_4m", spot - dir * 4f, -4f);
                foreach (CeilingLight lamp in CeilingLight.All) if (lamp != null) lamp.SetForcedDark(false);

                // O interruptor de luz do trecho (aceso; e no escuro, com a lanterna) e o trecho piscando.
                var wave = builder.GetComponentInChildren<OfficeLightWave>();
                LightSwitches switches = LightSwitches.Build(builder, wave);
                int run = -1;
                if (switches != null)
                    for (int r = 0; r < wave.RunCount && run < 0; r++) if (switches.Has(r)) run = r;
                if (run >= 0)
                {
                    figure.position = Vector3.down * 50f;
                    Vector3 at = switches.PositionOf(run), normal = switches.NormalOf(run);
                    Vector3 side = Vector3.Cross(Vector3.up, normal);
                    Vector3 stand = at + normal * 1.3f + side * 0.5f;
                    stand.y = builder.transform.position.y;
                    void ShootAt(string name, Vector3 point)
                    {
                        Vector3 flat = point - stand;
                        flat.y = 0f;
                        player.SetPositionAndRotation(stand, Quaternion.LookRotation(flat.normalized, Vector3.up));
                        Vector3 d = point - camera.transform.position;
                        camera.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, 0f, 0f);
                        Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, name + ".png"));
                        taken.Add(name);
                    }
                    ShootAt("zelador_interruptor", at);
                    foreach (CeilingLight lamp in wave.RunLamps(run)) if (lamp != null) lamp.SetForcedDark(true);
                    if (torch != null)
                    {
                        // Lanterna de quem está a uns 3 m (colada na parede estoura a imagem).
                        Vector3 lamp = at + normal * 3f + Vector3.up * 0.3f;
                        torch.transform.SetPositionAndRotation(lamp, Quaternion.LookRotation(at - lamp));
                        torch.enabled = true;
                    }
                    ShootAt("zelador_interruptor_escuro", at);
                    if (torch != null) torch.enabled = false;

                    // O trecho piscando no aviso: metade das luminárias apagada num instante do pisca-pisca,
                    // visto de uma ponta; e o Zelador na outra ponta, quando o trecho apaga.
                    IReadOnlyList<CeilingLight> lamps = wave.RunLamps(run);
                    for (int i = 0; i < lamps.Count; i++) if (lamps[i] != null) lamps[i].SetForcedDark(i % 3 == 1);
                    Vector3 a0 = lamps[0].transform.position, a1 = lamps[lamps.Count - 1].transform.position;
                    a0.y = a1.y = builder.transform.position.y;
                    Vector3 along = (a1 - a0).normalized;
                    player.SetPositionAndRotation(a0 - along * 0.5f, Quaternion.LookRotation(along, Vector3.up));
                    camera.transform.localRotation = Quaternion.Euler(-4f, 0f, 0f);
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "zelador_trecho_piscando.png"));
                    taken.Add("zelador_trecho_piscando");

                    foreach (CeilingLight lamp in lamps) if (lamp != null) lamp.SetForcedDark(true);
                    figure.SetPositionAndRotation(a1 - along * 2f, Quaternion.LookRotation(-along, Vector3.up));
                    body.Pose(0f, 0f, 1f, 0f, 0.3f);
                    if (torch != null)
                    {
                        torch.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                        torch.enabled = true;
                    }
                    Level0Preview.Shoot(camera, target, readback, Path.Combine(folder, "zelador_ponta_do_trecho.png"));
                    taken.Add("zelador_ponta_do_trecho");
                    if (torch != null) torch.enabled = false;
                    foreach (CeilingLight lamp in lamps) if (lamp != null) lamp.SetForcedDark(false);
                    Debug.Log($"[BackRooms] Interruptores na seed 1998: {switches.Count} de {wave.RunCount} trechos");
                }
                else Debug.LogWarning("[BackRooms] Nenhum interruptor posto: sem foto do interruptor.");

                Debug.Log($"[BackRooms] Prévia do Zelador ({(body.model != null ? "modelo do Blender" : "peças de código")}): {taken.Count} fotos em {folder}");
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

        // O trecho reto de corredor mais comprido: a célula da ponta, o sentido e quantas células segue.
        internal static bool LongestCorridor(LevelBuilder builder, out Vector3 start, out Vector3 dir, out int length)
        {
            LevelData d = builder.Data;
            start = Vector3.zero;
            dir = Vector3.forward;
            length = 0;
            for (int z = 0; z < d.Height; z++)
                for (int x = 0; x < d.Width; x++)
                    foreach (WallSide s in new[] { WallSide.East, WallSide.North })
                    {
                        int n = 0, cx = x, cz = z;
                        while (d.Office[cx, cz] == OfficeCell.Corridor && d.OfficeEdgeAt(cx, cz, s) == OfficeEdge.Open &&
                               d.Neighbor(cx, cz, s, out int nx, out int nz) && d.Office[nx, nz] == OfficeCell.Corridor)
                        {
                            n++;
                            cx = nx;
                            cz = nz;
                        }
                        if (n <= length) continue;
                        length = n;
                        start = builder.CellCenter(x, z);
                        dir = Level3Setup.Side(s);
                    }
            return length >= 3;
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
