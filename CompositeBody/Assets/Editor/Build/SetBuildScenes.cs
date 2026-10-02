using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Sets Build Settings to exactly the scenes given, in the order given, and nothing else.
    ///
    /// Separate from the scene builders on purpose. Those each append themselves if missing,
    /// which is right for them but means none of them can be used to take a scene away. Which
    /// scenes ship is a deliberate decision, so it gets its own explicit step.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.SetBuildScenes.Run
    ///   -scenes "Assets/_Scenes/A.unity;Assets/_Scenes/B.unity"
    /// </summary>
    public static class SetBuildScenes
    {
        public static void Run()
        {
            string arg = GetArg("-scenes");
            if (string.IsNullOrEmpty(arg))
            {
                Debug.LogError("[BuildScenes] RESULT: FAIL - pass -scenes \"path1;path2\".");
                return;
            }

            string[] paths = arg.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var entries = new List<EditorBuildSettingsScene>();

            foreach (string raw in paths)
            {
                string path = raw.Trim().Replace('\\', '/');

                // Checked against disk rather than trusted: a typo would otherwise produce a
                // player with a scene list that silently cannot load its own first scene.
                if (!File.Exists(path))
                {
                    Debug.LogError($"[BuildScenes] RESULT: FAIL - no scene at '{path}'.");
                    return;
                }

                entries.Add(new EditorBuildSettingsScene(path, true));
            }

            string removed = Describe(EditorBuildSettings.scenes);
            EditorBuildSettings.scenes = entries.ToArray();
            AssetDatabase.SaveAssets();

            Debug.Log($"[BuildScenes] was: {removed}");
            for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
                Debug.Log($"[BuildScenes] now {i}: {EditorBuildSettings.scenes[i].path}");

            if (EditorBuildSettings.scenes.Length != entries.Count)
            {
                Debug.LogError("[BuildScenes] RESULT: FAIL - the list did not take.");
                return;
            }

            Debug.Log("[BuildScenes] RESULT: PASS");
        }

        static string Describe(EditorBuildSettingsScene[] scenes)
        {
            if (scenes == null || scenes.Length == 0) return "(empty)";

            var names = new List<string>(scenes.Length);
            foreach (var scene in scenes)
                names.Add(Path.GetFileNameWithoutExtension(scene.path) + (scene.enabled ? "" : " (off)"));
            return string.Join(", ", names);
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
    }
}
