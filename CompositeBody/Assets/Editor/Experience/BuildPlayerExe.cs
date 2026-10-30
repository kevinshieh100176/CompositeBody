using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Builds a Windows PCVR executable of MainScene.
    ///
    /// ONE SCENE, not the project's build-settings list. That list also carries two test scenes,
    /// and shipping them drags their assets into the player for no reason -- and, worse, makes a
    /// stale test scene something that can load in front of an audience. The show is MainScene.
    ///
    /// Built as a DEVELOPMENT build by default. This exists for testing in the headset, where
    /// the thing that saves an afternoon is Player.log: without it, a problem in a build can only
    /// be reproduced by guessing. Pass false to BuildAt for a clean build when the piece is
    /// actually being shown.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildPlayerExe.Run
    /// </summary>
    public static class BuildPlayerExe
    {
        const string k_Scene = "Assets/_Scenes/MainScene.unity";

        /// <summary>Outside Assets, or Unity would import the player it just built.</summary>
        const string k_OutDir = "Builds/Windows";
        const string k_ExeName = "CompositeBody.exe";

        [MenuItem("Tools/Build/Windows PCVR (MainScene)")]
        public static void Run() => BuildAt(k_OutDir, development: true);

        [MenuItem("Tools/Build/Windows PCVR (MainScene, release)")]
        public static void RunRelease() => BuildAt(k_OutDir + "-Release", development: false);

        public static void BuildAt(string directory, bool development)
        {
            Debug.Log("[Build] Starting...");

            if (!File.Exists(k_Scene))
            {
                Debug.LogError($"[Build] RESULT: FAIL - no scene at {k_Scene}.");
                return;
            }

            // Absolute, because BuildPipeline resolves a relative path against the project folder
            // and the difference is only visible when the build lands somewhere unexpected.
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string outDir = Path.GetFullPath(Path.Combine(projectRoot, directory));
            Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { k_Scene },
                locationPathName = Path.Combine(outDir, k_ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None
            };

            Debug.Log($"[Build] {(development ? "development" : "release")} build of {k_Scene}\n" +
                      $"[Build] -> {options.locationPathName}");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                // Only the errors, and only the first few: a failed Unity build log is mostly
                // noise and the first error is nearly always the one that matters.
                var errors = report.steps
                    .SelectMany(step => step.messages)
                    .Where(m => m.type is LogType.Error or LogType.Exception)
                    .Take(10)
                    .ToArray();

                foreach (var m in errors) Debug.LogError($"[Build] {m.content}");

                Debug.LogError($"[Build] RESULT: FAIL - {summary.result}, " +
                               $"{summary.totalErrors} error(s).");
                return;
            }

            double mb = summary.totalSize / (1024.0 * 1024.0);
            Debug.Log($"[Build] {k_ExeName}  {mb:F1} MB  in {summary.totalTime.TotalSeconds:F0}s\n" +
                      $"[Build] folder: {outDir}\n" +
                      (development
                          ? "[Build] development build -- Player.log is written to " +
                            "%USERPROFILE%\\AppData\\LocalLow\\<company>\\<product>\\Player.log\n"
                          : "") +
                      "[Build] RESULT: PASS");
        }
    }
}
