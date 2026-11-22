using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
    /// O Undertow, o inimigo da água funda das Piscinas: o prefab de rede (gravado a cada montagem do
    /// Level 2, por <see cref="Level2Setup"/>), os materiais do corpo de código e as fotos de
    /// conferência dentro da fase, como o jogador o vê: de cima, do deque, através da ondulação (de
    /// dia e à noite), debaixo d'água de frente e de lado, de baixo contra a superfície, dormindo
    /// no poço, pegando, o rosto de perto e, para conferir o desenho, fora d'água no deque.
    ///
    /// Menu BackRooms > Capturar prévia do Undertow. Linha de comando: <see cref="BatchCapture"/>
    /// (-seed N, padrão 1998). Fotos em Logs/Previews/undertow/.
    /// </summary>
    public static class UndertowPreview
    {
        public const string PrefabPath = "Assets/_Game/Prefabs/Undertow.prefab";
        const string ArtFolder = CharacterModels.ArtFolder + "/Afogado";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        const string ModelName = "Afogado";

        /// <summary>
        /// Grava o prefab de rede (Prefabs/Undertow.prefab): só componentes, o corpo se monta em
        /// código quando ele aparece. Sem colisor: um colisor sólido rondando a borda barraria quem
        /// sobe nela para fugir. O Netcode registra o prefab sozinho em DefaultNetworkPrefabs.asset.
        /// </summary>
        public static GameObject CreatePrefab()
        {
            var root = new GameObject("Undertow");

            var body = root.AddComponent<UndertowBody>();
            AssignMaterials(body);
            body.model = CharacterModels.Model(ModelName);
            body.modelMaterials = CharacterModels.Materials(ModelName);

            // Rede: quem manda na posição é o host. Só o giro de lado; a inclinação o corpo calcula.
            root.AddComponent<NetworkObject>();
            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleZ = false;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;
            networkTransform.Interpolate = true;

            root.AddComponent<UndertowBrain>();
            root.AddComponent<UndertowAudio>();

            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            Level0Setup.StampNetworkId(prefab);
            return prefab;
        }

        static void AssignMaterials(UndertowBody body)
        {
            EnsureFolder(ArtFolder);
            body.paleMaterial = Lit(ModelName + "_CorpoClaro", new Color(0.62f, 0.66f, 0.6f), 0.62f);
            body.backMaterial = Lit(ModelName + "_CorpoEscuro", new Color(0.08f, 0.095f, 0.085f), 0.5f);
            body.bubbleMaterial = Bubble();
        }

        static Material Lit(string name, Color color, float smoothness)
        {
            string path = $"{ArtFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(Shader.Find("HDRP/Lit")) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            HDMaterial.ValidateMaterial(material);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        // Bolha: branca, quase transparente, bem lisa.
        static Material Bubble()
        {
            string path = $"{ArtFolder}/{ModelName}_Bolha.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(Shader.Find("HDRP/Lit")) { name = ModelName + "_Bolha" };
            material.SetFloat("_SurfaceType", 1f);
            material.SetFloat("_BlendMode", 0f);
            material.SetColor("_BaseColor", new Color(0.85f, 0.95f, 0.95f, 0.35f));
            material.SetFloat("_Smoothness", 0.95f);
            HDMaterial.ValidateMaterial(material);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- Fotos

        [MenuItem("BackRooms/Capturar prévia do Undertow")]
        public static void CaptureFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string report = Capture(SeedArgument(1998), out bool ok);
            Debug.Log("[BackRooms] Prévia do Undertow " + (ok ? "pronta" : "FALHOU") + ": " + report);
        }

        /// <summary>Linha de comando: fotografa e fecha o editor (0 = ok).</summary>
        public static void BatchCapture()
        {
            int code = 1;
            try
            {
                string report = Capture(SeedArgument(1998), out bool ok);
                Debug.Log("[BackRooms] Prévia do Undertow " + (ok ? "pronta" : "FALHOU") + ": " + report);
                code = ok ? 0 : 2;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            Level1Setup.ExitWithLevel0Open(code);
        }

        static string Capture(int seed, out bool ok)
        {
            ok = false;
            if (!File.Exists(Level2Setup.ScenePath)) Level2Setup.BuildScene();
            EditorSceneManager.OpenScene(Level2Setup.ScenePath, OpenSceneMode.Single);
            var builder = UnityEngine.Object.FindFirstObjectByType<LevelBuilder>();
            if (builder == null || !builder.poolsEnabled) return "a cena não tem LevelBuilder com as Piscinas";
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null) return "sem o prefab do jogador";

            builder.seed = seed;
            builder.Rebuild();
            LevelData d = builder.Data;
            WakeWater(builder);
            foreach (HDAdditionalLightData light in builder.GetComponentsInChildren<HDAdditionalLightData>(true))
                light.shadowUpdateMode = ShadowUpdateMode.EveryFrame;

            if (!FindDeck(d, out int px, out int pz, out WallSide deckSide)) return "nenhuma água funda com deque seco ao lado nesta seed";
            bool hasWell = FindWell(d, out int wx, out int wz);

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Previews", "undertow"));
            Directory.CreateDirectory(folder);
            // -fotoTag X: as séries da braçada e da maré saem com o prefixo X_ (antes_, depois_), e a
            // limpeza não apaga as de "antes" (para comparar com as de depois de mudar o modelo).
            // -so bracada | mare: só aquela série.
            string tag = TextArgument("-fotoTag", "");
            string prefix = tag.Length > 0 ? tag + "_" : "";
            string only = TextArgument("-so", "");
            foreach (string old in Directory.GetFiles(folder, "*.png"))
                if (!Path.GetFileName(old).StartsWith("antes_") || tag == "antes") File.Delete(old);

            // O corpo, sem rede: só o que se vê.
            var creature = new GameObject("Undertow_Previa");
            var body = creature.AddComponent<UndertowBody>();
            AssignMaterials(body);
            body.model = CharacterModels.Model(ModelName);
            body.modelMaterials = CharacterModels.Materials(ModelName);
            body.Build();

            GameObject rig = Level0Preview.CreateCameraRig(playerPrefab, out Camera camera);
            Light torch = rig.GetComponentInChildren<Light>(true);
            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var camcorder = UnityEngine.Object.FindFirstObjectByType<BackRooms.UI.Camcorder>();
            var water = builder.GetComponentInChildren<PoolWater>();
            var lightsOut = UnityEngine.Object.FindFirstObjectByType<LightsOut>();
            var names = new List<string>();

            try
            {
                camera.targetTexture = target;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 150f);
                float surface = water != null ? water.SurfaceY : LevelData.WaterLevel;

                void Shot(string name, Vector3 eye, Vector3 look, bool underwater, bool lamp = false)
                {
                    Vector3 flat = look - eye;
                    flat.y = 0f;
                    if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward * 0.01f;
                    float pitch = Mathf.Atan2(eye.y - look.y, flat.magnitude) * Mathf.Rad2Deg;
                    rig.transform.SetPositionAndRotation(eye - Vector3.up * 1.65f, Quaternion.LookRotation(flat.normalized, Vector3.up));
                    camera.transform.localRotation = Quaternion.Euler(Mathf.Clamp(pitch, -85f, 85f), 0f, 0f);
                    if (torch != null) torch.enabled = lamp;
                    if (water != null)
                    {
                        water.LocalUnderwater = underwater;
                        water.Snap(eye);
                    }
                    if (camcorder != null) camcorder.Draw(true, 61.4f, 61.4f, 0.95f, false, underwater: underwater, air: underwater ? 0.6f : 1f);
                    string file = name + ".png";
                    Shoot(camera, target, readback, Path.Combine(folder, file));
                    names.Add(file);
                }

                void PlaceStroke(Vector3 at, Vector3 forward, float speed, float sleep, float hunt, float grab, float jaw, float phase, float stroke)
                {
                    forward.y = 0f;
                    creature.transform.SetPositionAndRotation(at, Quaternion.LookRotation(forward.normalized, Vector3.up));
                    body.Pose(phase, speed, sleep, hunt, grab, jaw, stroke);
                    Physics.SyncTransforms();
                    body.Fit(builder);
                }

                void Place(Vector3 at, Vector3 forward, float speed, float sleep, float hunt, float grab, float jaw, float phase)
                    => PlaceStroke(at, forward, speed, sleep, hunt, grab, jaw, phase, phase * 0.7f);

                // A célula funda com o deque seco ao lado: ela passando a meia água, o jogador no deque.
                Vector3 pool = builder.CellCenter(px, pz);
                Vector3 toDeck = Dir(deckSide);
                Vector3 along = Vector3.Cross(Vector3.up, toDeck);
                Vector3 swimAt = new Vector3(pool.x, surface - 1.1f, pool.z);
                Vector3 deckEye = new Vector3(pool.x, 0.2f + 1.65f, pool.z) + toDeck * (LevelBuilder.Cell * 0.5f + 0.6f);

                if (only == "bracada" || only == "")
                    StrokeSeries(PlaceStroke, Shot, pool, toDeck, along, surface, prefix);
                if (only == "mare" || only == "")
                    TideSeries(PlaceStroke, Shot, builder, d, water, prefix);
                if (only != "")
                {
                    ok = true;
                    return $"seed {seed}: {names.Count} fotos ({only}) em {folder}; modelo " + (body.model != null ? "do Blender" : "de código");
                }

                Place(swimAt, along, 1.8f, 0f, 0f, 0f, 0.15f, 0.3f);
                Shot("cima_deque", deckEye, swimAt, false);
                Place(swimAt, along, 3f, 0f, 1f, 0f, 1f, 0.55f);
                Shot("cima_deque_cacando", deckEye, swimAt, false);

                if (water != null && lightsOut != null)
                {
                    water.ForceSeconds = lightsOut.firstDarkAt + lightsOut.darkSeconds * 0.5f;
                    Place(swimAt, along, 1.8f, 0f, 0f, 0f, 0.15f, 0.3f);
                    Shot("cima_deque_noite", deckEye, swimAt, false);
                    Shot("cima_deque_noite_lanterna", deckEye, swimAt, false, lamp: true);
                    water.ForceSeconds = -1.0;
                }

                // Debaixo d'água: de frente (vindo caçar, boca aberta) e de lado.
                Vector3 head = swimAt + along * 1.25f;
                Place(swimAt, along, 3f, 0f, 1f, 0f, 1f, 0.2f);
                Shot("debaixo_frente", head + along * 3.2f + Vector3.up * 0.2f, head, true);
                Place(swimAt, along, 1.8f, 0f, 0f, 0f, 0.2f, 0.4f);
                Shot("debaixo_lado", swimAt - toDeck * 0.2f + toDeck * -3.4f + Vector3.up * 0.1f, swimAt, true);
                Shot("debaixo_lado_lanterna", swimAt + toDeck * -3.4f + Vector3.up * 0.1f, swimAt, true, lamp: true);

                // De baixo: deitado no fundo, olhando para cima, ela passando contra a superfície.
                Vector3 above = new Vector3(pool.x, surface - 0.7f, pool.z);
                Place(above, along, 1.8f, 0f, 0f, 0f, 0.15f, 0.7f);
                Shot("de_baixo", new Vector3(pool.x, pool.y + 0.35f, pool.z) - along * 2.4f, above, true);

                // Pegando: de perto, de frente.
                Place(swimAt, along, 0f, 0f, 0f, 1f, 1f, 0.1f);
                Shot("pegando", head + along * 1.4f + Vector3.up * 0.1f, head, true);

                // O rosto de perto.
                Place(swimAt, along, 0f, 0f, 0f, 0f, 0.6f, 0.1f);
                Shot("rosto", head + along * 0.75f + Vector3.up * 0.05f, head + Vector3.up * 0.02f, true);

                // Dormindo no fundo do poço, visto da borda e debaixo d'água.
                if (hasWell)
                {
                    Vector3 well = builder.CellCenter(wx, wz);
                    Vector3 sleepAt = well + Vector3.up * 0.35f;
                    Place(sleepAt, Vector3.forward, 0f, 1f, 0f, 0f, 0.1f, 0.2f);
                    Shot("poco_dormindo_debaixo", well + new Vector3(1.6f, 3.2f, -1.6f), sleepAt, true);
                    Shot("poco_dormindo_debaixo_lanterna", well + new Vector3(1.6f, 3.2f, -1.6f), sleepAt, true, lamp: true);
                }

                // Fora d'água, no deque seco, bem iluminado: só para conferir o desenho.
                Vector3 deckFloor = new Vector3(pool.x, 0.2f + 0.35f, pool.z) + toDeck * (LevelBuilder.Cell * 0.5f + 1.8f);
                Place(deckFloor, along, 0f, 0f, 0f, 0f, 0.5f, 0.1f);
                Shot("seco_lado", deckFloor - toDeck * 3.6f + Vector3.up * 1.0f, deckFloor, false, lamp: true);
                Shot("seco_frente", deckFloor + along * 3.6f + Vector3.up * 0.9f, deckFloor + along * 0.6f, false, lamp: true);
                Shot("seco_cima", deckFloor + Vector3.up * 4.2f + toDeck * 1.0f, deckFloor, false, lamp: true);

                ok = true;
                return $"seed {seed}: {names.Count} fotos em {folder} (água funda ({px},{pz}) com deque a {deckSide}" +
                       (hasWell ? $", poço ({wx},{wz})" : ", sem poço") + "; modelo " + (body.model != null ? "do Blender" : "de código") + ")";
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(rig);
                UnityEngine.Object.DestroyImmediate(creature);
                if (water != null) { water.LocalUnderwater = false; water.Offset = 0f; }
                ShaderUtil.allowAsyncCompilation = asyncBefore;
            }
        }

        delegate void Poser(Vector3 at, Vector3 forward, float speed, float sleep, float hunt, float grab, float jaw, float phase, float stroke);
        delegate void Shooter(string name, Vector3 eye, Vector3 look, bool underwater, bool lamp = false);

        /// <summary>
        /// A braçada em fases, para conferir os braços presos ao ombro: fora d'água no deque seco
        /// (de cima e de lado, com lanterna) e de baixo, debaixo d'água, contra a superfície. Fases:
        /// deslizando (braços para trás), quatro pontos da braçada de peito caçando e pegando.
        /// </summary>
        static void StrokeSeries(Poser place, Shooter shot, Vector3 pool, Vector3 toDeck, Vector3 along, float surface, string prefix)
        {
            Vector3 deck = new Vector3(pool.x, 0.2f + 0.45f, pool.z) + toDeck * (LevelBuilder.Cell * 0.5f + 1.8f);
            Vector3 above = new Vector3(pool.x, surface - 0.7f, pool.z);
            var phases = new (string name, float speed, float hunt, float grab, float stroke)[]
            {
                ("deslize", 1.8f, 0f, 0f, 0.1f),
                ("f0", 3f, 1f, 0f, 0f), ("f25", 3f, 1f, 0f, 0.25f), ("f50", 3f, 1f, 0f, 0.5f), ("f75", 3f, 1f, 0f, 0.75f),
                ("pegando", 0f, 0f, 1f, 0.1f),
            };
            foreach (var p in phases)
            {
                string n = prefix + "bracada_" + p.name;
                place(deck, along, p.speed, 0f, p.hunt, p.grab, 0.5f, 0.3f, p.stroke);
                shot(n + "_cima", deck + Vector3.up * 3.4f + along * 0.4f + toDeck * 0.3f, deck + along * 0.4f, false, true);
                shot(n + "_lado", deck - toDeck * 3.2f + Vector3.up * 0.9f + along * 0.5f, deck + along * 0.5f, false, true);
                place(above, along, p.speed, 0f, p.hunt, p.grab, 0.5f, 0.3f, p.stroke);
                shot(n + "_baixo", new Vector3(pool.x, pool.y + 0.35f, pool.z) - along * 0.6f, above + along * 0.4f, true, true);
            }
        }

        /// <summary>
        /// Maré alta (água +1,3 m): ele caçando no raso (1,72 m de água) e no seco alagado (1,12 m),
        /// como o jogador o vê de pé ali, de cima e debaixo d'água, para conferir que o corpo não
        /// atravessa o chão.
        /// </summary>
        static void TideSeries(Poser place, Shooter shot, LevelBuilder builder, LevelData d, PoolWater water, string prefix)
        {
            if (water == null) return;
            water.Offset = PoolTide.Amplitude;
            float surface = water.SurfaceY;
            foreach (PoolDepth depth in new[] { PoolDepth.Shallow, PoolDepth.Dry })
            {
                if (!FindOpenRun(d, depth, out int x, out int z, out WallSide side)) continue;
                string n = prefix + "mare_" + (depth == PoolDepth.Dry ? "seco" : "raso");
                Vector3 c = builder.CellCenter(x, z);
                Vector3 along = Dir(side);
                Vector3 across = Vector3.Cross(Vector3.up, along);
                float y = Mathf.Clamp(c.y + 0.6f, c.y + 0.4f, surface - 0.45f);
                Vector3 at = new Vector3(c.x, y, c.z);
                place(at, along, 2.4f, 0f, 1f, 0f, 1f, 0.3f, 0.3f);
                Vector3 eye = new Vector3(c.x, c.y + 1.65f, c.z) + along * 4.6f + across * 0.6f;
                shot(n + "_cacando", eye, at + along * 1.2f, eye.y < surface, true);
                shot(n + "_cima", at + Vector3.up * 2.6f - along * 0.2f + across * 0.4f, at, false, true);
                // De lado, debaixo d'água, da célula de trás (aberta), meio de lado: a 1 m do piso.
                Vector3 sideEye = new Vector3(c.x, c.y + 0.5f, c.z) - along * 3.8f + across * 1.4f;
                shot(n + "_lado", sideEye, at + along * 0.2f, true, true);
                place(at, along, 2.4f, 0f, 1f, 0f, 1f, 0.3f, 0.75f);
                shot(n + "_lado_bracada", sideEye, at + along * 0.2f, true, true);
            }
            water.Offset = 0f;
        }

        // Uma célula daquela profundidade, fora da sala inicial, com a vizinha da frente e a de trás
        // abertas e da mesma profundidade (para o corpo comprido caber na foto).
        static bool FindOpenRun(LevelData d, PoolDepth depth, out int x, out int z, out WallSide side)
        {
            WallSide[] sides = { WallSide.North, WallSide.East, WallSide.South, WallSide.West };
            for (z = 0; z < d.Height; z++)
                for (x = 0; x < d.Width; x++)
                {
                    if (d.IsSolid(x, z) || d.Depth[x, z] != depth || d.InRoom(x, z)) continue;
                    foreach (WallSide s in sides)
                    {
                        WallSide o = s == WallSide.North ? WallSide.South : s == WallSide.South ? WallSide.North : s == WallSide.East ? WallSide.West : WallSide.East;
                        if (!d.IsOpen(x, z, s) || !d.Neighbor(x, z, s, out int ax, out int az) || d.Depth[ax, az] != depth) continue;
                        if (!d.IsOpen(x, z, o) || !d.Neighbor(x, z, o, out int bx, out int bz) || d.Depth[bx, bz] != depth) continue;
                        side = s;
                        return true;
                    }
                }
            x = z = -1;
            side = WallSide.North;
            return false;
        }

        static string TextArgument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }

        static bool FindDeck(LevelData d, out int x, out int z, out WallSide side)
        {
            WallSide[] sides = { WallSide.South, WallSide.North, WallSide.West, WallSide.East };
            for (z = 0; z < d.Height; z++)
                for (x = 0; x < d.Width; x++)
                {
                    if (d.IsSolid(x, z) || d.Depth[x, z] != PoolDepth.Deep) continue;
                    foreach (WallSide s in sides)
                    {
                        if (!d.IsOpen(x, z, s) || !d.Neighbor(x, z, s, out int nx, out int nz)) continue;
                        if (d.IsSolid(nx, nz) || d.Depth[nx, nz] != PoolDepth.Dry) continue;
                        // Do outro lado também água funda, para ela caber inteira na foto de lado.
                        WallSide opposite = s == WallSide.North ? WallSide.South : s == WallSide.South ? WallSide.North : s == WallSide.East ? WallSide.West : WallSide.East;
                        if (!d.IsOpen(x, z, opposite) || !d.Neighbor(x, z, opposite, out int ox, out int oz) || d.Depth[ox, oz] != PoolDepth.Deep) continue;
                        side = s;
                        return true;
                    }
                }
            x = z = -1;
            side = WallSide.South;
            return false;
        }

        static bool FindWell(LevelData d, out int x, out int z)
        {
            for (z = 0; z < d.Height; z++)
                for (x = 0; x < d.Width; x++)
                    if (!d.IsSolid(x, z) && d.Depth[x, z] == PoolDepth.Well) return true;
            x = z = -1;
            return false;
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

        static int SeedArgument(int fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-seed" && int.TryParse(args[i + 1], out int seed)) return seed;
            return fallback;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
