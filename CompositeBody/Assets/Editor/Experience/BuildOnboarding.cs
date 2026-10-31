using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Experience;
using CompositeBody.PointClouds;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Merges the opening beats into one scene and one prefab: Onboarding.
    ///
    /// O-0 through O-3 share a room. They are the same black void, the same slab, the same two
    /// 光圈 and the same two 真人 standing in them -- what changes between them is what the
    /// player is asked to do. Four prefabs of that would be four copies of one room to keep in
    /// step, and the copies had already started to drift: O_1.unity and O_2.unity were
    /// byte-identical empty scenes, and there was no O_3 at all.
    ///
    /// SO THE SHARED ROOM IS AUTHORED ONCE, under Content, and each beat contributes only what
    /// is its own, under Beats. O-1 is two props and a rule about who can see them; it needs no
    /// room, which is the point.
    ///
    /// CONTENT DOES NOT MOVE, and that is not cosmetic. The O-0 timeline animates by PATH --
    /// "Content/Human A/Lamp", "Content/Opening/Title" -- resolved relative to the object the
    /// track is bound to, which is the prefab root. Inserting a level above Content, or renaming
    /// it, silently breaks every one of those curves: the timeline still plays, and nothing
    /// moves. So the merge renames the root and adds a sibling, and leaves the inside alone.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildOnboarding.Run
    /// </summary>
    public static class BuildOnboarding
    {
        const string k_SourcePrefab = "Assets/_Scenes/O_0.prefab";
        const string k_PrefabPath = "Assets/_Scenes/Onboarding.prefab";
        const string k_ScenePath = "Assets/_Scenes/Onboarding.unity";
        const string k_PlaySpacePrefab = "Assets/_Scenes/PlaySpace_Debug.prefab";

        [MenuItem("Tools/Onboarding/Build Onboarding")]
        public static void Run()
        {
            Debug.Log("[Onboarding] Starting...");

            if (AssetDatabase.LoadAssetAtPath<GameObject>(k_SourcePrefab) == null)
            {
                Debug.LogError($"[Onboarding] RESULT: FAIL - no source prefab at {k_SourcePrefab}.");
                return;
            }

            // SEEDED FROM O_0 ONCE, then edited in place forever after. The merged prefab is
            // hand-staged -- props are positioned, values are tuned -- and re-copying O_0 over
            // the top on every run would throw all of that away. Copying is only ever how the
            // file comes into existence; delete Onboarding.prefab to start again from O_0.
            bool seeded = AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath) != null;
            if (!seeded)
            {
                if (!AssetDatabase.CopyAsset(k_SourcePrefab, k_PrefabPath))
                {
                    Debug.LogError($"[Onboarding] RESULT: FAIL - could not copy to {k_PrefabPath}.");
                    return;
                }
                AssetDatabase.ImportAsset(k_PrefabPath, ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"[Onboarding] seeded {k_PrefabPath} from {k_SourcePrefab}.");
            }
            else
            {
                Debug.Log($"[Onboarding] {k_PrefabPath} already exists; editing it in place and " +
                          "leaving anything staged by hand alone.");
            }

            GameObject root = PrefabUtility.LoadPrefabContents(k_PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[Onboarding] RESULT: FAIL - could not open {k_PrefabPath}.");
                return;
            }

            bool ok;
            try
            {
                root.name = "Onboarding";

                Transform beats = root.transform.Find("Beats");
                if (beats == null)
                {
                    var go = new GameObject("Beats");
                    go.transform.SetParent(root.transform, false);
                    beats = go.transform;
                }

                // Reused, not rebuilt. The props are staged by hand once they exist, and
                // destroying the group to recreate it would throw that away every time this ran.
                Transform grabGroup = beats.Find("O_1 Grab");
                if (grabGroup == null)
                {
                    var go = new GameObject("O_1 Grab");
                    go.transform.SetParent(beats, false);
                    grabGroup = go.transform;
                }

                // O-2's four scans, the same way and for the same reason: reused, not rebuilt.
                Transform ghostGroup = beats.Find("O_2 Ghosts");
                if (ghostGroup == null)
                {
                    var go = new GameObject("O_2 Ghosts");
                    go.transform.SetParent(beats, false);
                    ghostGroup = go.transform;
                }

                ok = BuildO1Grab.BuildInto(grabGroup) && BuildO2Ghosts.BuildInto(ghostGroup);
                if (ok)
                {
                    WireHandover(root, beats, grabGroup);
                    PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (!ok)
            {
                if (!seeded) AssetDatabase.DeleteAsset(k_PrefabPath);
                Debug.LogError("[Onboarding] RESULT: FAIL - the O-1 props could not be built, so " +
                               "the merged prefab was discarded rather than saved incomplete.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Object>(k_ScenePath) == null) BuildScene();
            else Debug.Log($"[Onboarding] {k_ScenePath} already exists; left as it is.");
            AssetDatabase.SaveAssets();

            Debug.Log($"[Onboarding] {k_PrefabPath} and {k_ScenePath}\n" +
                      "  Content/ unchanged, so the O-0 timeline's paths still resolve.\n" +
                      "  Beats/O_1 Grab/ holds the two lamps and their boxes.\n" +
                      "  Beats/O_2 Ghosts/ holds the four scans, unformed, at the centre.\n" +
                      "  O_0.prefab is left in place; MainScene still points at it.\n" +
                      "[Onboarding] RESULT: PASS");
        }

        /// <summary>
        /// Hand the two 真人 to ScanHandover, which is what dissolves them.
        ///
        /// IT NO LONGER PAIRS THEM WITH A PROP. Each figure used to be paired with the object
        /// that took its place, and both halves ran off one curve. The objects belong to O-1 now
        /// -- they arrive with the boxes, on that beat's own timeline, through PropPresence -- so
        /// the only thing this has to know is which clouds go.
        ///
        /// Found by ROLE rather than by order in the hierarchy, from the view component that
        /// already says so, so a future reshuffle of the Content hierarchy cannot quietly leave
        /// one of them behind.
        /// </summary>
        static void WireHandover(GameObject root, Transform beats, Transform grabGroup)
        {
            var handover = beats.GetComponent<ScanHandover>();
            if (handover == null) handover = beats.gameObject.AddComponent<ScanHandover>();

            // Which figure belongs to which role, from the view component that already says so.
            var figureFor = new System.Collections.Generic.Dictionary<PlayerRole, PointCloudFigure>();
            foreach (var view in root.GetComponentsInChildren<O0FigureRoleView>(true))
            {
                var so = new SerializedObject(view);
                var role = (PlayerRole)so.FindProperty("m_VisibleTo").intValue;
                var station = so.FindProperty("m_Station").objectReferenceValue as GameObject;
                if (station == null) continue;

                var figure = station.GetComponentInChildren<PointCloudFigure>(true);
                if (figure != null) figureFor[role] = figure;
            }

            var figures = new System.Collections.Generic.List<PointCloudFigure>();
            foreach (PlayerRole role in new[] { PlayerRole.Player1, PlayerRole.Player2 })
            {
                if (figureFor.TryGetValue(role, out PointCloudFigure figure)) figures.Add(figure);
                else
                    Debug.LogWarning($"[Onboarding] no 真人 found for {role}; that player's scan " +
                                     "will not dissolve when O-0's timeline asks it to.");
            }

            var hso = new SerializedObject(handover);
            SerializedProperty list = hso.FindProperty("m_Stations");
            list.arraySize = figures.Count;
            for (int i = 0; i < figures.Count; i++)
            {
                list.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("figure").objectReferenceValue = figures[i];
            }
            hso.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[Onboarding] ScanHandover wired with {figures.Count} 真人.");
        }

        /// <summary>
        /// The authoring scene: the merged prefab, the play-space outline, and a camera to look
        /// at them with. Deliberately not a copy of MainScene -- this is for working on the beat,
        /// and a scene with the whole network stack in it is slower to open and easier to break.
        /// </summary>
        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath);
            if (prefab != null) PrefabUtility.InstantiatePrefab(prefab);
            else Debug.LogWarning("[Onboarding] merged prefab missing when building the scene.");

            var playSpace = AssetDatabase.LoadAssetAtPath<GameObject>(k_PlaySpacePrefab);
            if (playSpace != null) PrefabUtility.InstantiatePrefab(playSpace);

            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f;
            camGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

            // Standing where the players start, looking down the room at the 真人.
            camGO.transform.position = new Vector3(0f, 1.6f, -2f);
            camGO.transform.rotation = Quaternion.identity;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.05f, 0.05f, 0.07f);
            RenderSettings.ambientEquatorColor = new Color(0.04f, 0.04f, 0.05f);
            RenderSettings.ambientGroundColor = new Color(0.02f, 0.02f, 0.03f);
            RenderSettings.fog = false;

            Directory.CreateDirectory(Path.GetDirectoryName(k_ScenePath));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, k_ScenePath);
        }
    }
}
