using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// O jogo para Windows (64 bits), com as fases da lista da build (o Level 0 primeiro; ver
    /// <c>Level0Setup.AddSceneToBuild</c>). Sai em <c>Builds/Windows/TheBackRooms.exe</c> (pasta fora
    /// do Git). Menu BackRooms > Gerar build para Windows. Linha de comando: -executeMethod
    /// BackRooms.EditorTools.BuildTool.BatchWindows (sai com 0 se gerou, 3 se falhou); linha
    /// "[BackRooms] Build para Windows".
    /// </summary>
    public static class BuildTool
    {
        public const string Output = "Builds/Windows/TheBackRooms.exe";

        [MenuItem("BackRooms/Gerar build para Windows")]
        public static void BuildFromMenu()
        {
            string problem = Build(out string summary);
            EditorUtility.DisplayDialog("Build", problem ?? summary, "OK");
        }

        public static void BatchWindows()
        {
            string problem = Build(out string summary);
            Debug.Log("[BackRooms] Build para Windows " + (problem == null ? "PASSOU: " + summary : "FALHOU: " + problem));
            EditorApplication.Exit(problem == null ? 0 : 3);
        }

        static string Build(out string summary)
        {
            summary = "";
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                // Só as fases (a lista também tem a cena de exemplo do template do HDRP).
                if (scene.enabled && File.Exists(scene.path) && scene.path.StartsWith("Assets/_Game/Scenes/")) scenes.Add(scene.path);
            if (scenes.Count == 0) return "a lista de cenas da build está vazia";
            if (!scenes[0].EndsWith("Level0_Test.unity")) return $"a primeira cena da build é {scenes[0]} (tem de ser o Level 0)";

            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary result = report.summary;
            if (result.result != BuildResult.Succeeded)
                return $"{result.result}, {result.totalErrors} erro(s)";
            summary = $"{scenes.Count} cenas, {result.totalSize / (1024f * 1024f):F0} MB em {result.totalTime.TotalMinutes:F1} min, {Path.GetFullPath(Output)}";
            return null;
        }
    }
}
