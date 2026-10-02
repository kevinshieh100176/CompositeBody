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

            // A test build that boots straight into one scene, without reordering Build Settings
            // and so without changing what the real build starts on.
            string firstScene = GetArg("-firstScene");
            if (!string.IsNullOrEmpty(firstScene))
            {
                int index = System.Array.FindIndex(scenes,
                    s => string.Equals(s, firstScene, StringComparison.OrdinalIgnoreCase));

                if (index < 0)
                {
                    Debug.LogError($"[Build] RESULT: FAIL - -firstScene '{firstScene}' is not an enabled build scene.");
                    return;
                }

                (scenes[0], scenes[index]) = (scenes[index], scenes[0]);
                Debug.Log($"[Build] Booting into '{firstScene}' for this build only.");
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

            string outDir = Path.GetDirectoryName(outPath);

            if (HasFlag("-clean") && Directory.Exists(outDir))
            {
                Directory.Delete(outDir, true);
                Debug.Log($"[Build] Cleaned {outDir}.");
            }

            Directory.CreateDirectory(outDir);

            // Unity's post-build step writes this file with CreateNew and throws if it is
            // already there, so a rebuild over a previous build reports an error and silently
            // keeps the OLD bindings. Harmless while the file is empty, but it would quietly
            // ship stale input bindings the moment it is not.
            string bindings = Path.Combine(outDir, "RuntimeActionBindings.json");
            if (File.Exists(bindings))
            {
                File.Delete(bindings);
                Debug.Log("[Build] Removed the previous RuntimeActionBindings.json so it gets regenerated.");
            }

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

            // Reported whatever the result. A build can come back Succeeded with a non-zero
            // error count, and those errors are exactly the ones that go unnoticed: the player
            // runs, so nothing looks wrong until the thing that failed to be written matters.
            int reported = 0;
            foreach (BuildStep step in report.steps)
            {
                foreach (BuildStepMessage message in step.messages)
                {
                    if (message.type != LogType.Error && message.type != LogType.Exception) continue;
                    Debug.LogError($"[Build] {step.name}: {message.content}");
                    reported++;
                }
            }

            if (summary.totalErrors > 0 && reported == 0)
                Debug.LogError($"[Build] {summary.totalErrors} error(s) reported by the build but none " +
                               "carried a step message; check the editor log above this line.");

            if (summary.result != BuildResult.Succeeded)
            {
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

            // The player executable itself is a fixed Unity binary and is not rewritten when
            // only managed code changed, so its timestamp is not evidence the build is current.
            // The managed assembly is.
            string assembly = Path.Combine(Path.ChangeExtension(outPath, null) + "_Data", "Managed", "Assembly-CSharp.dll");
            if (File.Exists(assembly))
                Debug.Log($"[Build] Assembly-CSharp.dll written {File.GetLastWriteTime(assembly):HH:mm:ss} " +
                          "(this, not the .exe timestamp, says whether the build is current)");
            else
                Debug.LogWarning($"[Build] No Assembly-CSharp.dll at {assembly}; project code may not be in the player.");

            if (!File.Exists(bindings))
                Debug.LogWarning("[Build] RuntimeActionBindings.json was not regenerated.");

            if (summary.totalErrors > 0)
            {
                Debug.LogError($"[Build] RESULT: FAIL - build succeeded but reported {summary.totalErrors} error(s); " +
                               "see the [Build] error lines above.");
                return;
            }

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
