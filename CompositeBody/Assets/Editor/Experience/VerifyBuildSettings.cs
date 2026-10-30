using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Checks that Build Settings would build the show, and only the show.
    ///
    /// WHY THIS IS WORTH A CHECK. Unity's scene list is edited by hand, by accident, and by any
    /// package that decides it wants a scene in there. Get it wrong and the build still succeeds
    /// -- it just ships the wrong first scene, or drags a test scene along, and you find out in
    /// the headset. The project also carries two scenes that must never be in a build:
    /// MembraneCombineTest, and CompositeBody_Experience, which is the legacy beat spine that
    /// used to be loaded on top of MainScene and produced two of everything.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.VerifyBuildSettings.Run
    /// </summary>
    public static class VerifyBuildSettings
    {
        const string k_Main = "Assets/_Scenes/MainScene.unity";

        [MenuItem("Tools/Verify/Build Settings")]
        public static void Run()
        {
            Debug.Log("[BuildSettings] Starting...");

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            var report = new StringBuilder();
            int problems = 0;

            report.AppendLine($"[BuildSettings] {scenes.Length} scene(s) listed:");
            foreach (EditorBuildSettingsScene s in scenes)
                report.AppendLine($"[BuildSettings]   [{(s.enabled ? "ON " : "off")}] {s.path}");

            EditorBuildSettingsScene[] enabled = scenes.Where(s => s.enabled).ToArray();

            if (enabled.Length == 0)
            {
                report.AppendLine("[BuildSettings] FAIL no scene is enabled; a build would have " +
                                  "nothing to load.");
                problems++;
            }
            else
            {
                if (enabled[0].path != k_Main)
                {
                    report.AppendLine($"[BuildSettings] FAIL the first enabled scene is " +
                                      $"'{enabled[0].path}', not {k_Main}. That is what a built " +
                                      "player opens on launch.");
                    problems++;
                }

                if (enabled.Length > 1)
                {
                    report.AppendLine($"[BuildSettings] WARN {enabled.Length} scenes are enabled. " +
                                      "The show runs from MainScene alone; anything else is " +
                                      "weight in the build and a scene that can be loaded by " +
                                      "accident.");
                    foreach (EditorBuildSettingsScene s in enabled.Skip(1))
                        report.AppendLine($"[BuildSettings]     also enabled: {s.path}");
                }
            }

            // Named rather than pattern-matched on "_Test", because the one that actually caused
            // trouble does not live under that folder by name alone -- it was loaded additively
            // by the session manager and produced a second set of scans.
            foreach (EditorBuildSettingsScene s in enabled)
            {
                if (!s.path.Contains("_Test/")) continue;
                report.AppendLine($"[BuildSettings] FAIL a test scene is enabled: {s.path}");
                problems++;
            }

            report.Append(problems == 0
                ? "[BuildSettings] RESULT: PASS"
                : $"[BuildSettings] {problems} problem(s).\n[BuildSettings] RESULT: FAIL");

            if (problems == 0) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
        }
    }
}
