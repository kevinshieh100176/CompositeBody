using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Builds the Windows PCVR player. The piece is delivered on PC with two tethered headsets
    /// and a staff machine, so StandaloneWindows64 is the only target that matters here.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildPcvrPlayer.Run
    ///   [-buildOut &lt;path to .exe&gt;] [-devBuild]
    ///
    /// Default output is &lt;repo&gt;/Builds/CompositeBody_PCVR/CompositeBody.exe, which the
    /// repository's .gitignore already excludes.
    /// </summary>
    public static class BuildPcvrPlayer
    {
        const string k_DefaultRelative = "../../Builds/CompositeBody_PCVR/CompositeBody.exe";

        public static void Run()
        {
            Debug.Log("[Build] Starting PCVR build...");

            string outPath = GetArg("-buildOut");
            if (string.IsNullOrEmpty(outPath))
                outPath = Path.GetFullPath(Path.Combine(Application.dataPath, k_DefaultRelative));

            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] RESULT: FAIL - no enabled scenes in Build Settings.");
                return;
            }

            for (int i = 0; i < scenes.Length; i++)
                Debug.Log($"[Build] scene {i}: {scenes[i]}");

            // The project also carries the Android/Quest packages, so the active target may well
            // be Android. BuildPlayer would switch anyway; doing it explicitly keeps the reason
            // for the reimport that follows visible in the log.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                Debug.Log($"[Build] Switching active target from {EditorUserBuildSettings.activeBuildTarget} to StandaloneWindows64.");
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            var options = BuildOptions.None;
            if (HasFlag("-devBuild"))
            {
                // Development build keeps the profiler and a readable stack trace, which is what
                // makes a failure in the venue diagnosable at all.
                options |= BuildOptions.Development | BuildOptions.AllowDebugging;
                Debug.Log("[Build] Development build requested.");
            }

            var buildOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = options
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            BuildSummary summary = report.summary;

            Debug.Log($"[Build] result={summary.result} " +
                      $"errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                      $"time={summary.totalTime} size={summary.totalSize / (1024 * 1024)}MB");

            if (summary.result != BuildResult.Succeeded)
            {
                foreach (BuildStep step in report.steps)
                {
                    foreach (BuildStepMessage message in step.messages)
                    {
                        if (message.type == LogType.Error || message.type == LogType.Exception)
                            Debug.LogError($"[Build] {step.name}: {message.content}");
                    }
                }

                Debug.LogError($"[Build] RESULT: FAIL - build {summary.result}.");
                return;
            }

            if (!File.Exists(outPath))
            {
                Debug.LogError($"[Build] RESULT: FAIL - build reported success but {outPath} does not exist.");
                return;
            }

            var info = new FileInfo(outPath);
            Debug.Log($"[Build] Wrote {outPath} ({info.Length / 1024}KB executable)");
            Debug.Log($"[Build] Data folder: {Path.ChangeExtension(outPath, null)}_Data");
            Debug.Log("[Build] RESULT: PASS");
        }

        static string[] EnabledScenes()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path)) scenes.Add(scene.path);
            }
            return scenes.ToArray();
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }

        static bool HasFlag(string name)
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
