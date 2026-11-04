using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using CompositeBody.Experience;
using Object = UnityEngine.Object;

namespace CompositeBody.Interaction.EditorTools
{
    /// <summary>
    /// What the three "integrate into MainScene" steps share: finding the show's furniture,
    /// reading the playspace's authored pose, putting a module under an always-active parent, and
    /// editing the show timeline.
    ///
    /// EVERY STEP IS RE-RUNNABLE AND REUSES WHAT IS THERE. MainScene is rebuilt and re-baked by
    /// other people, so an integration you cannot re-run will rot. Re-running must also not change
    /// a module's network identity: an in-scene NetworkObject is matched across machines by
    /// GlobalObjectIdHash, which is derived from its persistent id, so destroying and
    /// re-instantiating a module would hand every fragment a new one. These helpers therefore
    /// find-and-update rather than delete-and-recreate.
    /// </summary>
    public static class MainSceneIntegrationUtil
    {
        public const string MainScenePath = "Assets/_Scenes/MainScene.unity";
        public const string BeatsPath = "Experience/Beats";
        public const string ShowTimelinePath = "Experience/ExperienceDirector/ShowTimeline";

        // ---- preconditions -------------------------------------------------------------------

        /// <summary>MainScene is the active scene and nothing has unsaved changes.</summary>
        public static bool RequireMainScene(string tag, out Scene scene)
        {
            scene = default;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError($"[{tag}] RESULT: FAIL - leave play mode first.");
                return false;
            }
            return InteractionBuildUtil.RequireCleanActiveScene(MainScenePath, tag, out scene);
        }

        // ---- the scene ------------------------------------------------------------------------

        public static Transform FindPath(Scene scene, string path)
        {
            string[] parts = path.TrimStart('/').Split('/');
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != parts[0]) continue;
                Transform t = root.transform;
                for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
                if (t != null) return t;
            }
            return null;
        }

        public static string PathOf(Transform t)
        {
            string p = t.name;
            for (Transform c = t.parent; c != null; c = c.parent) p = c.name + "/" + p;
            return "/" + p;
        }

        /// <summary>
        /// <c>/Experience/Beats</c> -- the always-active parent the modules live under. NOT a
        /// <c>Beat_*</c> root: those are switched off by the show timeline, and Netcode spawns
        /// in-scene NetworkObjects once, at synchronization, from what is active then. A module
        /// parented under an inactive beat root would never spawn at all.
        /// </summary>
        public static Transform Beats(Scene scene, string tag)
        {
            Transform beats = FindPath(scene, BeatsPath);
            if (beats == null)
            {
                Debug.LogError($"[{tag}] RESULT: FAIL - '/{BeatsPath}' is missing.");
                return null;
            }
            if (!beats.gameObject.activeInHierarchy)
            {
                Debug.LogError($"[{tag}] RESULT: FAIL - '/{BeatsPath}' is not active; modules under it would never spawn.");
                return null;
            }
            return beats;
        }

        public static PlayableDirector ShowDirector(Scene scene, string tag)
        {
            Transform t = FindPath(scene, ShowTimelinePath);
            var dir = t != null ? t.GetComponent<PlayableDirector>() : null;
            if (dir == null) Debug.LogError($"[{tag}] RESULT: FAIL - no PlayableDirector at '/{ShowTimelinePath}'.");
            return dir;
        }

        public static TimelineAsset ShowTimeline(Scene scene, string tag, out PlayableDirector director)
        {
            director = ShowDirector(scene, tag);
            var tl = director != null ? director.playableAsset as TimelineAsset : null;
            if (director != null && tl == null)
                Debug.LogError($"[{tag}] RESULT: FAIL - the show director has no TimelineAsset.");
            return tl;
        }

        /// <summary>
        /// The code a beat track's name starts with: the leading run of ASCII code characters, so
        /// 'S2-2 碎片：燈' and 'S2-3｜隱藏的記憶' both give their code.
        ///
        /// NOT a split on the first space, which is what <c>ShowTimelineDriver.CodeOf</c> does.
        /// The S2-3 track separates its code from its title with a FULLWIDTH vertical bar (U+FF5C,
        /// '｜') and no space, so splitting on ' ' hands back the whole CJK string as the code --
        /// which reads as "there is no S2-3 track" rather than as a parse that went wrong.
        /// </summary>
        public static string CodeOf(string trackName)
        {
            string trimmed = (trackName ?? string.Empty).Trim();
            int n = 0;
            while (n < trimmed.Length)
            {
                char c = trimmed[n];
                bool codeChar = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                                c == '-' || c == '_' || c == '.';
                if (!codeChar) break;
                n++;
            }
            return trimmed.Substring(0, n);
        }

        /// <summary>Start and end of a scene code (S2-2) on the show timeline, read off the asset.</summary>
        public static bool TryGetSceneWindow(TimelineAsset tl, string code, out double start, out double end)
        {
            start = end = 0;
            foreach (TrackAsset track in tl.GetOutputTracks())
            {
                if (track is not ControlTrack) continue;
                if (!string.Equals(CodeOf(track.name), code, System.StringComparison.OrdinalIgnoreCase)) continue;
                foreach (TimelineClip clip in track.GetClips())
                {
                    start = clip.start;
                    end = clip.end;
                    return true;
                }
            }
            return false;
        }

        // ---- the playspace --------------------------------------------------------------------

        /// <summary>
        /// Where the players are standing at <paramref name="showTime"/>, read from the show
        /// timeline's own Playspace animation.
        ///
        /// READ, NOT HARD-CODED. The playspace is the object the show moves the players around on
        /// (see <see cref="Playspace"/>), and S1 walks them from the world origin into the S3 room
        /// over 580-715s. Every module in S2 is authored around a root with the players just
        /// behind it, so the root pose IS this pose -- and if somebody re-records that move, these
        /// steps put the modules back in the right place on the next run instead of leaving them
        /// in a room the players no longer stand in.
        ///
        /// The move is a RECORDED clip, which is why this looks at <c>infiniteClip</c> first: a
        /// recorded track has no TimelineClips at all, so <c>GetClips()</c> comes back empty and
        /// reads exactly like "nothing animates the playspace".
        /// </summary>
        public static bool TryPlayspacePose(Scene scene, float showTime, string tag, out Pose pose)
        {
            pose = Pose.identity;
            Playspace playspace = Object.FindObjectsByType<Playspace>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                        .FirstOrDefault(p => p.gameObject.scene == scene);
            if (playspace == null)
            {
                Debug.LogError($"[{tag}] RESULT: FAIL - no Playspace in MainScene.");
                return false;
            }

            Transform t = playspace.transform;
            pose = new Pose(t.position, t.rotation);

            TimelineAsset tl = ShowTimeline(scene, tag, out PlayableDirector dir);
            if (tl == null) return false;

            var animator = playspace.GetComponent<Animator>();
            AnimationClip curves = null;
            foreach (TrackAsset track in tl.GetOutputTracks())
            {
                if (track is not AnimationTrack at) continue;
                var bound = dir.GetGenericBinding(at) as Object;
                bool isPlayspace = bound != null &&
                                   (bound == (Object)animator || bound == (Object)playspace.gameObject || bound == (Object)t);
                if (!isPlayspace) continue;

                curves = at.infiniteClip;
                if (curves == null)
                    foreach (TimelineClip c in at.GetClips())
                        if (c.animationClip != null) { curves = c.animationClip; break; }
                if (curves != null) break;
            }

            if (curves == null)
            {
                Debug.LogWarning($"[{tag}] the show timeline has no Playspace animation; taking its " +
                                 $"authored pose {Fmt(pose.position)} as where the players stand.");
                return true;
            }

            Vector3 p = pose.position;
            Vector3 euler = pose.rotation.eulerAngles;
            bool anyRot = false;
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(curves))
            {
                AnimationCurve c = AnimationUtility.GetEditorCurve(curves, b);
                if (c == null || c.length == 0) continue;
                float v = c.Evaluate(showTime);
                switch (b.propertyName)
                {
                    case "m_LocalPosition.x": p.x = v; break;
                    case "m_LocalPosition.y": p.y = v; break;
                    case "m_LocalPosition.z": p.z = v; break;
                    case "localEulerAnglesRaw.x": euler.x = v; anyRot = true; break;
                    case "localEulerAnglesRaw.y": euler.y = v; anyRot = true; break;
                    case "localEulerAnglesRaw.z": euler.z = v; anyRot = true; break;
                }
            }

            // The curves sit on the playspace itself, which is a scene root, so local == world.
            pose = new Pose(p, anyRot ? Quaternion.Euler(euler) : pose.rotation);
            Debug.Log($"[{tag}] playspace at {showTime:0}s of the show: {Fmt(pose.position)} " +
                      $"(from the recorded clip '{curves.name}', {curves.length:0}s long).");
            return true;
        }

        // ---- module instances -----------------------------------------------------------------

        /// <summary>
        /// The one instance of <paramref name="prefabPath"/> under <paramref name="parent"/>,
        /// instantiated if it is not there yet. Extra instances are an authoring mistake that
        /// would spawn two of every fragment, so they are reported rather than quietly tolerated.
        /// </summary>
        public static GameObject FindOrInstantiate(Scene scene, string prefabPath, Transform parent, string tag)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null)
            {
                Debug.LogError($"[{tag}] RESULT: FAIL - no prefab at '{prefabPath}'.");
                return null;
            }

            var found = new List<GameObject>();
            foreach (Transform child in parent)
            {
                GameObject go = child.gameObject;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == prefabPath) found.Add(go);
            }

            if (found.Count > 1)
            {
                Debug.LogError($"[{tag}] RESULT: FAIL - {found.Count} instances of '{prefabPath}' under " +
                               $"'{PathOf(parent)}'. Delete the spares; two would spawn two of every fragment.");
                return null;
            }

            if (found.Count == 1)
            {
                Debug.Log($"[{tag}] reusing the existing '{found[0].name}' (keeps its NetworkObject hashes).");
                return found[0];
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            instance.transform.SetParent(parent, true);
            instance.name = asset.name;
            Debug.Log($"[{tag}] instantiated '{prefabPath}' under '{PathOf(parent)}'.");
            return instance;
        }

        /// <summary>
        /// An always-inactive marker object the show switches on to arm a module. The module's own
        /// root cannot be this object (it has to stay active so its NetworkObjects spawn), so the
        /// arming signal is a separate, empty GameObject -- which is exactly what the existing
        /// <c>Beat_*</c> roots already are.
        /// </summary>
        public static GameObject EnsureBeatRoot(Transform parent, string name, string tag)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                if (existing.gameObject.activeSelf)
                {
                    existing.gameObject.SetActive(false);
                    Debug.Log($"[{tag}] '{name}' was left active; switched it off so the show owns its state.");
                }
                return existing.gameObject;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            Debug.Log($"[{tag}] created the arming root '{PathOf(go.transform)}' (inactive until the show switches it on).");
            return go;
        }

        // ---- serialized writes ----------------------------------------------------------------

        /// <summary>
        /// Writes a serialized field on a component that is part of a prefab instance, and records
        /// it. A value written straight onto an instance is only kept as an override if it is
        /// recorded; otherwise the next scene load reverts it to the prefab value, which is a
        /// failure that only shows up after a reload.
        /// </summary>
        public static void SetField(Component c, string property, System.Action<SerializedProperty> write)
        {
            var so = new SerializedObject(c);
            SerializedProperty p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogWarning($"[InteractionIntegrate] '{c.GetType().Name}' has no serialized '{property}'.", c);
                return;
            }
            write(p);
            so.ApplyModifiedPropertiesWithoutUndo();
            if (PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            EditorUtility.SetDirty(c);
        }

        public static void SetRef(Component c, string property, Object value) =>
            SetField(c, property, p => p.objectReferenceValue = value);

        public static void SetFloat(Component c, string property, float value) =>
            SetField(c, property, p => p.floatValue = value);

        public static void SetString(Component c, string property, string value) =>
            SetField(c, property, p => p.stringValue = value);

        /// <summary>
        /// Adds a persistent listener calling <paramref name="method"/> on <paramref name="target"/>
        /// to a UnityEvent field, unless it is already there. Persistent (serialized) rather than
        /// subscribed in code so the wiring is visible in the inspector, survives a domain reload,
        /// and is what somebody opening MainScene can see and change.
        /// </summary>
        public static bool WireEvent(Component owner, string eventProperty, Object target, string method, string tag)
        {
            var so = new SerializedObject(owner);
            SerializedProperty calls = so.FindProperty(eventProperty + ".m_PersistentCalls.m_Calls");
            if (calls == null)
            {
                Debug.LogError($"[{tag}] '{owner.GetType().Name}' has no UnityEvent '{eventProperty}'.", owner);
                return false;
            }

            for (int i = 0; i < calls.arraySize; i++)
            {
                SerializedProperty e = calls.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("m_Target").objectReferenceValue == target &&
                    e.FindPropertyRelative("m_MethodName").stringValue == method)
                    return false; // already wired
            }

            int n = calls.arraySize;
            calls.InsertArrayElementAtIndex(n);
            SerializedProperty call = calls.GetArrayElementAtIndex(n);
            call.FindPropertyRelative("m_Target").objectReferenceValue = target;
            SerializedProperty asmName = call.FindPropertyRelative("m_TargetAssemblyTypeName");
            if (asmName != null)
                asmName.stringValue = target.GetType().FullName + ", " + target.GetType().Assembly.GetName().Name;
            call.FindPropertyRelative("m_MethodName").stringValue = method;
            call.FindPropertyRelative("m_Mode").intValue = 1;      // EventDefined: call it with no argument
            call.FindPropertyRelative("m_CallState").intValue = 2; // RuntimeOnly
            so.ApplyModifiedPropertiesWithoutUndo();
            if (PrefabUtility.IsPartOfPrefabInstance(owner)) PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
            EditorUtility.SetDirty(owner);
            Debug.Log($"[{tag}] wired {owner.GetType().Name}.{eventProperty} -> {target.name}.{method}().");
            return true;
        }

        public static bool HasEventCall(Component owner, string eventProperty, Object target, string method)
        {
            SerializedProperty calls = new SerializedObject(owner)
                .FindProperty(eventProperty + ".m_PersistentCalls.m_Calls");
            if (calls == null) return false;
            for (int i = 0; i < calls.arraySize; i++)
            {
                SerializedProperty e = calls.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("m_Target").objectReferenceValue == target &&
                    e.FindPropertyRelative("m_MethodName").stringValue == method) return true;
            }
            return false;
        }

        // ---- the show timeline ----------------------------------------------------------------

        /// <summary>
        /// A <see cref="ShowHoldMarker"/> at <paramref name="time"/> for <paramref name="taskId"/>,
        /// moved rather than duplicated if one for that task is already on the timeline.
        ///
        /// Goes on the timeline own marker track because that is the only place
        /// <c>ShowTimelineDriver.CollectHolds</c> reads: a marker on any other track is collected
        /// by nothing and the show runs straight past the gate.
        /// </summary>
        public static ShowHoldMarker EnsureHoldMarker(TimelineAsset tl, double time, string taskId, string tag)
        {
            MarkerTrack track = tl.markerTrack;
            if (track == null)
            {
                tl.CreateMarkerTrack();
                track = tl.markerTrack;
            }
            if (track == null)
            {
                Debug.LogError($"[{tag}] RESULT: FAIL - the show timeline has no marker track.");
                return null;
            }

            foreach (IMarker m in track.GetMarkers().ToList())
            {
                if (m is not ShowHoldMarker hold || hold.taskId != taskId) continue;
                if (System.Math.Abs(hold.time - time) > 0.001)
                {
                    hold.time = time;
                    EditorUtility.SetDirty(tl);
                    Debug.Log($"[{tag}] moved the hold for '{taskId}' to {time:0.00}s.");
                }
                return hold;
            }

            var marker = track.CreateMarker<ShowHoldMarker>(time);
            marker.SetTaskId(taskId);
            EditorUtility.SetDirty(tl);
            Debug.Log($"[{tag}] added a ShowHoldMarker at {time:0.00}s for '{taskId}'.");
            return marker;
        }

        /// <summary>
        /// An <see cref="ActivationTrack"/> that switches <paramref name="bound"/> on for
        /// [start, end). Used rather than another Control clip because
        /// <c>ShowTimelineDriver.CollectScenes</c> builds the running order -- and the scene
        /// numbers it sends over OSC -- from ControlTracks: a new one would make this a scene of
        /// its own, and it is a cue inside S2-3, not a new scene.
        /// </summary>
        public static ActivationTrack EnsureActivation(PlayableDirector dir, TimelineAsset tl, string trackName,
                                                       GameObject bound, double start, double end, string tag)
        {
            ActivationTrack track = tl.GetOutputTracks().OfType<ActivationTrack>()
                                      .FirstOrDefault(t => t.name == trackName);
            if (track == null)
            {
                track = tl.CreateTrack<ActivationTrack>(null, trackName);
                Debug.Log($"[{tag}] created the ActivationTrack '{trackName}'.");
            }
            track.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;

            TimelineClip clip = track.GetClips().FirstOrDefault();
            if (clip == null) clip = track.CreateDefaultClip();
            clip.start = start;
            clip.duration = System.Math.Max(0.1, end - start);
            clip.displayName = trackName;

            dir.SetGenericBinding(track, bound);
            EditorUtility.SetDirty(tl);
            EditorUtility.SetDirty(dir);
            Debug.Log($"[{tag}] '{trackName}' switches '{bound.name}' on for {start:0.00}-{end:0.00}s.");
            return track;
        }

        // ---- saving ---------------------------------------------------------------------------

        /// <summary>
        /// Save, regenerate the NetworkObject hashes, record them on the instances, save again.
        ///
        /// In that order because a hash is derived from a persistent id, and a freshly created
        /// object has none until the scene has been saved once -- run any earlier, every new
        /// fragment keeps hash 0 and never synchronizes. The recording step is just as
        /// load-bearing: the hashes are written onto prefab INSTANCES, and an instance value that
        /// is not recorded reverts to the prefab value on the next load.
        /// </summary>
        public static bool SaveWithHashes(Scene scene, string tag, IEnumerable<GameObject> instances)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError($"[{tag}] RESULT: FAIL - could not save MainScene.");
                return false;
            }

            InteractionBuildUtil.RegenerateNetworkObjectHashes(scene);
            foreach (GameObject go in instances)
            {
                if (go == null) continue;
                foreach (NetworkObject netObj in go.GetComponentsInChildren<NetworkObject>(true))
                    if (PrefabUtility.IsPartOfPrefabInstance(netObj))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(netObj);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return EditorSceneManager.SaveScene(scene);
        }

        public static string Fmt(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";
    }
}
