using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Folds the two old scenes into one MainScene, and lifts O-0's content out into a scene
    /// prefab.
    ///
    /// The piece used to run as a pair: CompositeBody_Main held the co-located room -- the
    /// session manager, the staff panel, the two spawn points and the calibration landmarks --
    /// and CompositeBody_Experience held the show, as one ExperienceDirector and a tree of beats.
    /// One MainScene with a prefab per beat means the room and the show are authored in the same
    /// place, and a beat can be opened and edited without loading the rest of the piece.
    ///
    /// THIS MERGES RATHER THAN REBUILDS, and that is the whole design of it. Both scenes are full
    /// of prefab instances, wired references and NetworkObjects whose hashes are derived from
    /// where they sit; re-creating any of that from script would silently drop whatever nobody
    /// thought to re-create. Opening both scenes and moving the objects across keeps every
    /// reference, because they are the same objects.
    ///
    /// Run once. After this the prefab is the source of truth for O-0 and is edited by hand --
    /// BuildO0Arrival stays in the project as the record of how the beat was first assembled,
    /// but running it again will not update what is in the prefab.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildMainScene.Run
    /// </summary>
    public static class BuildMainScene
    {
        const string k_RoomScene = "Assets/_Scenes/_Test/CompositeBody_Main.unity";
        const string k_ContentScene = "Assets/_Scenes/_Test/CompositeBody_Experience.unity";
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";
        const string k_O0Prefab = "Assets/_Scenes/O_0.prefab";

        public static void Run()
        {
            Debug.Log("[Main] Starting...");

            if (!ExtractBeatPrefab()) return;
            if (!Assemble()) return;
            if (!Verify()) return;

            Debug.Log("[Main] RESULT: PASS");
        }

        /// <summary>
        /// Save the already-built Beat_O0 out as Assets/_Scenes/O_0.prefab.
        ///
        /// Taken from the content scene rather than rebuilt from BuildO0Arrival, because the one
        /// in the scene is the one that has been rendered and checked: the cue is wired, the
        /// fixtures are tuned, the dust is assigned. Rebuilding would produce something that
        /// ought to be identical, which is not the same as being identical.
        ///
        /// The holder stays ACTIVE and its Content child stays inactive. That split is what lets
        /// a BeatController switch its own beat on -- a controller on a deactivated object could
        /// never run to switch itself back.
        /// </summary>
        static bool ExtractBeatPrefab()
        {
            Scene scene = EditorSceneManager.OpenScene(k_ContentScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[Main] RESULT: FAIL - could not open {k_ContentScene}.");
                return false;
            }

            GameObject holder = FindInScene(scene, "Beat_O0");
            if (holder == null)
            {
                Debug.LogError("[Main] RESULT: FAIL - no Beat_O0 in the content scene.");
                return false;
            }

            if (holder.GetComponent<O0ArrivalBeat>() == null)
            {
                Debug.LogError("[Main] RESULT: FAIL - Beat_O0 has no O0ArrivalBeat.");
                return false;
            }

            // The project's own naming for scene prefabs. Everything that refers to a beat does
            // so through the StoryBeat enum rather than through this name, so renaming is safe.
            holder.name = "O_0";

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(holder, k_O0Prefab, out bool ok);
            if (!ok || saved == null)
            {
                Debug.LogError($"[Main] RESULT: FAIL - could not save {k_O0Prefab}.");
                return false;
            }

            Transform content = saved.transform.childCount > 0 ? saved.transform.GetChild(0) : null;
            Debug.Log($"[Main] {k_O0Prefab}: {saved.transform.childCount} child(ren), " +
                      $"content '{(content != null ? content.name : "none")}' with " +
                      $"{(content != null ? content.childCount : 0)} layer(s).");
            return true;
        }

        /// <summary>
        /// Room scene + the show's rig, saved as MainScene, with O-0 as a prefab instance.
        /// </summary>
        static bool Assemble()
        {
            Scene room = EditorSceneManager.OpenScene(k_RoomScene, OpenSceneMode.Single);
            if (!room.IsValid())
            {
                Debug.LogError($"[Main] RESULT: FAIL - could not open {k_RoomScene}.");
                return false;
            }

            Scene content = EditorSceneManager.OpenScene(k_ContentScene, OpenSceneMode.Additive);
            if (!content.IsValid())
            {
                Debug.LogError($"[Main] RESULT: FAIL - could not open {k_ContentScene} additively.");
                return false;
            }

            GameObject show = FindInScene(content, "Experience");
            if (show == null)
            {
                Debug.LogError("[Main] RESULT: FAIL - no Experience root in the content scene.");
                return false;
            }

            // Moved, not copied. Instantiating a copy would renumber every local file id and so
            // break the NetworkObject hashes the director depends on to reach a client.
            SceneManager.MoveGameObjectToScene(show, room);
            Debug.Log($"[Main] moved '{show.name}' into the room scene.");

            if (!SwapBeatForPrefab(show)) return false;

            // Nothing was saved into the content scene, so it closes without writing. The old
            // scene stays on disk in _Test as it was.
            EditorSceneManager.CloseScene(content, true);

            EditorSceneManager.MarkAllScenesDirty();

            // First save: an object created or moved by script has no persistent GlobalObjectId
            // until the scene exists on disk, so the hash pass below has nothing to derive one
            // from. This is the same two-step BuildExperienceScene does, for the same reason.
            EditorSceneManager.SaveScene(room, k_MainScene);
            RegenerateNetworkObjectHashes();
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(room, k_MainScene);

            EnsureInBuildSettings(k_MainScene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Main] saved {k_MainScene}.");
            return true;
        }

        /// <summary>
        /// Replace the inline Beat_O0 with an instance of the prefab, in the same place.
        ///
        /// Without this MainScene would hold a second, unlinked copy of O-0: editing the prefab
        /// would change nothing in the show, which is the exact failure the prefab exists to
        /// prevent. The other beats are still inline placeholders and are left alone -- they
        /// become prefabs as each one is built.
        /// </summary>
        static bool SwapBeatForPrefab(GameObject show)
        {
            Transform beats = show.transform.Find("Beats");
            if (beats == null)
            {
                Debug.LogError("[Main] RESULT: FAIL - no Beats root under Experience.");
                return false;
            }

            Transform inline = beats.Find("O_0") ?? beats.Find("Beat_O0");
            if (inline == null)
            {
                Debug.LogError("[Main] RESULT: FAIL - no O-0 holder under Beats.");
                return false;
            }

            int siblingIndex = inline.GetSiblingIndex();
            Object.DestroyImmediate(inline.gameObject);

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(k_O0Prefab);
            if (asset == null)
            {
                Debug.LogError($"[Main] RESULT: FAIL - {k_O0Prefab} did not load.");
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, beats.gameObject.scene);
            instance.transform.SetParent(beats, false);
            instance.transform.SetSiblingIndex(siblingIndex);
            instance.name = "O_0";

            Debug.Log("[Main] O-0 is now a prefab instance under Beats.");
            return true;
        }

        static bool Verify()
        {
            Scene scene = EditorSceneManager.OpenScene(k_MainScene, OpenSceneMode.Single);

            var director = Object.FindFirstObjectByType<ExperienceDirector>();
            if (director == null)
            {
                Debug.LogError("[Main] RESULT: FAIL - MainScene has no ExperienceDirector.");
                return false;
            }
            Debug.Log("[Main] OK   ExperienceDirector");

            if (Object.FindFirstObjectByType<ScreenFade>(FindObjectsInactive.Include) == null)
            {
                Debug.LogError("[Main] RESULT: FAIL - MainScene has no ScreenFade.");
                return false;
            }
            Debug.Log("[Main] OK   ScreenFade");

            var beat = Object.FindFirstObjectByType<O0ArrivalBeat>(FindObjectsInactive.Include);
            if (beat == null)
            {
                Debug.LogError("[Main] RESULT: FAIL - MainScene has no O0ArrivalBeat.");
                return false;
            }

            // The point of the whole exercise: editing O_0.prefab has to change the show.
            if (PrefabUtility.GetPrefabInstanceStatus(beat.gameObject) != PrefabInstanceStatus.Connected)
            {
                Debug.LogError("[Main] RESULT: FAIL - O-0 in MainScene is not a connected prefab " +
                               "instance, so edits to the prefab would not reach the show.");
                return false;
            }
            Debug.Log("[Main] OK   O-0 is a connected prefab instance");

            // The thing from the room scene the show cannot run without. Searched by name across
            // the whole tree, not across roots: the spawns sit under the room's Environment, and
            // a root-only check reported zero for a scene that had both.
            int spawns = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                spawns += CountNamed(root.transform, "_Spawn");

            if (spawns < 2)
            {
                Debug.LogError($"[Main] RESULT: FAIL - MainScene has {spawns} spawn point(s); " +
                               "a co-located piece for two players needs both.");
                return false;
            }
            Debug.Log($"[Main] OK   spawns ({spawns})");

            int zeroHash = 0;
            foreach (var netObj in Object.FindObjectsByType<NetworkObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (netObj.PrefabIdHash == 0) zeroHash++;
            }
            if (zeroHash > 0)
            {
                Debug.LogError($"[Main] RESULT: FAIL - {zeroHash} NetworkObject(s) still hash to 0. " +
                               "NGO cannot match those between host and client, so the beat index " +
                               "would never reach the second headset and the show would run on " +
                               "the host only.");
                return false;
            }
            Debug.Log("[Main] OK   NetworkObject hashes");
            return true;
        }

        /// <summary>Objects at or under <paramref name="parent"/> whose name ends in the suffix.</summary>
        static int CountNamed(Transform parent, string suffix)
        {
            int n = parent.name.EndsWith(suffix) ? 1 : 0;
            foreach (Transform child in parent) n += CountNamed(child, suffix);
            return n;
        }

        static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                Transform found = FindDeep(root.transform, name);
                if (found != null) return found.gameObject;
            }
            return null;
        }

        static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Scene-placed NetworkObjects derive their hash from where they sit, and a moved object
        /// keeps a stale one. NGO matches host to client on that hash, so a stale value is a
        /// show that runs on one headset.
        /// </summary>
        static void RegenerateNetworkObjectHashes()
        {
            var onValidate = typeof(NetworkObject).GetMethod(
                "OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);

            if (onValidate == null)
            {
                Debug.LogError("[Main] Could not find NetworkObject.OnValidate; hashes may stay 0.");
                return;
            }

            int n = 0;
            foreach (var netObj in Object.FindObjectsByType<NetworkObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                onValidate.Invoke(netObj, null);
                EditorUtility.SetDirty(netObj);
                n++;
            }
            Debug.Log($"[Main] regenerated {n} NetworkObject hash(es).");
        }

        static void EnsureInBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path != path) continue;
                if (!scenes[i].enabled)
                {
                    scenes[i] = new EditorBuildSettingsScene(path, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                Debug.Log("[Main] already in build settings.");
                return;
            }

            // First, so a build boots into the show rather than into whichever test scene
            // happened to be at the top of the list.
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Main] added to build settings, first.");
        }
    }
}
