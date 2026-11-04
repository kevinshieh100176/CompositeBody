using CompositeBody.Experience;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Read S2_v1's bake off S2_v1.unity and store it in S2_v1.prefab, so the S2 look in
    /// MainScene is lit by the room that was actually baked.
    ///
    /// WHY THIS IS A TOOL RATHER THAN A SETTING. MainScene does not bake S2. It cannot: a bake
    /// is one lightmap set per scene, and the show walks through several rooms that were lit
    /// separately, so each section carries its own bake in a <see cref="BakedLightmapCarrier"/>
    /// and LightingDirector switches which one the room is standing in. The carrier stores the
    /// lightmap TEXTURES as asset references plus, per renderer, the index and the patch of UV
    /// it was baked into -- because none of that travels in a prefab on its own. In Unity 6 a
    /// renderer's lightmap index is not even serialised on the component; it is handed out by
    /// the scene's LightingData asset when the scene loads. Which is exactly why it has to be
    /// read with the baked scene OPEN, and why it has to be read AGAIN after every re-bake.
    ///
    /// AND WHY FORGETTING IS SO QUIET. A re-bake writes new lightmap assets with new GUIDs and
    /// deletes the old ones. The carrier's stored references then resolve to null -- not to an
    /// error. The carrier checks that it has entries and maps, which it does, so it dutifully
    /// appends three empty lightmaps to the global array and points twenty-nine of S2's
    /// renderers at them. The room comes up with its baked light simply gone, wearing whatever
    /// ambient the last capture happened to catch, and nothing in the console says so. That is
    /// the state this repairs, and it is the state the show falls back into every time S2_v1 is
    /// baked again.
    ///
    /// WHAT IT TAKES WITH IT. The lightmaps, the per-renderer indices and UV patches, and the
    /// environment -- fog, ambient mode and colours, skybox, reflection intensity, and the
    /// baked ambient probe itself. S2's lights are Point and Spot lights inside the prefab, so
    /// they reach MainScene on their own; three of them are Baked and exist only in the
    /// lightmaps this captures.
    ///
    /// IT WILL NOT DISCARD ANYONE'S WORK. Capturing means opening S2_v1.unity, which replaces
    /// whatever is open, and what is normally open here is MainScene -- possibly holding an
    /// afternoon of hand placement, possibly holding a "Preview Look In Editor" that must NOT
    /// be saved. Neither call is this tool's to make, so it makes neither: it refuses while any
    /// open scene is dirty, and puts the scene that was open back when it is done.
    ///
    /// Unity.exe -batchmode -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.CaptureS2Lighting.Run
    /// </summary>
    public static class CaptureS2Lighting
    {
        const string k_Scene = "Assets/_Scenes/IX_Scene/S2_v1.unity";
        const string k_Prefab = "Assets/_Scenes/IX_Scene/S2_v1.prefab";

        [MenuItem("Tools/Show/Capture S2 Lighting From S2_v1")]
        static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run();
        }

        public static void Run()
        {
            Debug.Log("[S2 Lighting] Starting...");

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (!open.isDirty) continue;

                Debug.LogError($"[S2 Lighting] RESULT: FAIL - '{open.path}' has unsaved changes, " +
                               $"and opening {k_Scene} would throw them away without asking. " +
                               "Save it, or revert it, and run this again.");
                return;
            }

            // Where to come back to. Read before the open, and used on the failure paths below
            // as well: a tool that leaves someone looking at S2_v1 when they were working in
            // MainScene has still cost them their place.
            string returnTo = SceneManager.GetActiveScene().path;

            Scene scene = EditorSceneManager.OpenScene(k_Scene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[S2 Lighting] RESULT: FAIL - could not open {k_Scene}.");
                Return(returnTo);
                return;
            }

            // THE BAKE HAS TO BE LOADED, not merely sitting on disk. An empty array here is the
            // scene opening before its LightingData asset has been imported, or the bake having
            // been cleared -- and capturing from it would store nothing while looking like a
            // success.
            int sceneMaps = LightmapSettings.lightmaps.Length;
            if (sceneMaps == 0)
            {
                Debug.LogError($"[S2 Lighting] RESULT: FAIL - {k_Scene} has no baked lightmaps " +
                               "loaded, so there is nothing to capture. Bake it, or let the " +
                               "import of its lighting data finish, and run this again.");
                Return(returnTo);
                return;
            }

            BakedLightmapCarrier carrier = FindCarrier(scene, out int found);
            if (carrier == null)
            {
                Debug.LogError($"[S2 Lighting] RESULT: FAIL - no {k_Prefab} instance with a " +
                               $"BakedLightmapCarrier in {k_Scene}.");
                Return(returnTo);
                return;
            }

            if (found > 1)
            {
                Debug.LogWarning($"[S2 Lighting] {found} carriers in {k_Scene} belong to " +
                                 $"{k_Prefab}; captured the last one found, on " +
                                 $"'{carrier.gameObject.name}'. Only one object should carry " +
                                 "the section's bake.");
            }

            // Said before the capture overwrites it, because a run that reports three missing
            // textures beforehand and none afterwards is the whole story of what it fixed.
            Describe("before", carrier);

            carrier.CaptureFromOpenScene();

            int missing = Describe("after", carrier);
            if (missing > 0)
            {
                Debug.LogError($"[S2 Lighting] RESULT: FAIL - {missing} of the captured lightmap " +
                               "slots came back empty, which should not be possible from a " +
                               "loaded bake. Nothing was applied to the prefab.");
                Return(returnTo);
                return;
            }

            // ONTO THE PREFAB, which is the only copy MainScene reads. Capturing on the instance
            // and stopping there would leave S2_v1.unity looking right and the show unchanged --
            // the same trap SyncO0Prefab exists for.
            PrefabUtility.ApplyObjectOverride(carrier, k_Prefab, InteractionMode.AutomatedAction);
            AssetDatabase.SaveAssets();

            // Read back from the ASSET, not from the instance that was just captured: the
            // question is what MainScene will load, and the apply is the step that can silently
            // not have happened.
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(k_Prefab);
            BakedLightmapCarrier stored = asset != null
                ? asset.GetComponentInChildren<BakedLightmapCarrier>(true)
                : null;

            if (stored == null)
            {
                Debug.LogError($"[S2 Lighting] RESULT: FAIL - {k_Prefab} has no carrier to read " +
                               "back, so the capture went nowhere.");
                Return(returnTo);
                return;
            }

            int storedMissing = Describe("stored in the prefab", stored);

            // S2_v1.unity is left dirty by the capture and the apply, and is deliberately not
            // saved: nothing in the scene itself changed, and reopening is what discards it.
            Return(returnTo);

            if (storedMissing > 0)
            {
                Debug.LogError($"[S2 Lighting] RESULT: FAIL - {k_Prefab} still has " +
                               $"{storedMissing} empty lightmap slot(s) after the apply.");
                return;
            }

            Debug.Log($"[S2 Lighting] captured from {sceneMaps} lightmap(s) in {k_Scene} and " +
                      $"applied to {k_Prefab}. MainScene's S2 look reads it from there, so " +
                      "MainScene itself does not change." +
                      "\n[S2 Lighting] RESULT: PASS");
        }

        /// <summary>The carrier belonging to the S2_v1 prefab instance, wherever it sits.</summary>
        static BakedLightmapCarrier FindCarrier(Scene scene, out int found)
        {
            BakedLightmapCarrier carrier = null;
            found = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (BakedLightmapCarrier candidate in
                         root.GetComponentsInChildren<BakedLightmapCarrier>(true))
                {
                    if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate.gameObject)
                        != k_Prefab) continue;

                    carrier = candidate;
                    found++;
                }
            }

            return carrier;
        }

        /// <summary>
        /// Say what a carrier is holding, and return how many of its lightmap slots are empty.
        ///
        /// Through SerializedObject because the fields are private and serialised, which is the
        /// right way round -- a carrier's stored bake is not an API, and this is the one place
        /// that has to look at it from outside.
        /// </summary>
        static int Describe(string when, BakedLightmapCarrier carrier)
        {
            var so = new SerializedObject(carrier);
            SerializedProperty entries = so.FindProperty("m_Entries");
            SerializedProperty maps = so.FindProperty("m_Lightmaps");
            SerializedProperty env = so.FindProperty("m_Environment");

            var sb = new System.Text.StringBuilder();
            sb.Append($"[S2 Lighting] {when}: {entries.arraySize} renderer(s), ")
              .Append(maps.arraySize).Append(" lightmap(s)");

            int missing = 0;
            for (int i = 0; i < maps.arraySize; i++)
            {
                Object color = maps.GetArrayElementAtIndex(i)
                                   .FindPropertyRelative("color").objectReferenceValue;

                sb.Append("\n  [").Append(i).Append("] ")
                  .Append(color == null ? "MISSING" : AssetDatabase.GetAssetPath(color));

                if (color == null) missing++;
            }

            Object skybox = env.FindPropertyRelative("skybox").objectReferenceValue;
            sb.Append("\n  environment: captured=")
              .Append(env.FindPropertyRelative("captured").boolValue)
              .Append("  fog=").Append(env.FindPropertyRelative("fog").boolValue)
              .Append("  ambientMode=")
              .Append((UnityEngine.Rendering.AmbientMode)
                      env.FindPropertyRelative("ambientMode").intValue)
              .Append("  ambientIntensity=")
              .Append(env.FindPropertyRelative("ambientIntensity").floatValue)
              .Append("  skybox=").Append(skybox == null ? "(none)" : skybox.name)
              .Append("  reflectionIntensity=")
              .Append(env.FindPropertyRelative("reflectionIntensity").floatValue);

            Debug.Log(sb.ToString());
            return missing;
        }

        static void Return(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath) || scenePath == k_Scene) return;

            // OpenScene does not prompt, which is the point: the dirt here is this tool's own
            // and the scene being returned to was verified clean on the way in.
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }
    }
}
