using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Authors the show's running order into Assets/_Timeline/Main.playable, and puts the driver
    /// that runs it in MainScene.
    ///
    /// ONE TIMELINE, EACH BEAT NESTED IN IT. Every beat gets a Control clip pointing at its holder
    /// under Beats, so O-0's own timeline plays as part of the master rather than alongside it.
    /// The running order becomes a thing you can see and drag, instead of a beat list in one
    /// inspector and four prefabs somewhere else.
    ///
    /// THE GATED BEATS ARE MARKERS, NOT DURATIONS. O-1, O-2 and O-3 end when both players have
    /// done the thing, which no clip length can express. Each one gets a <see cref="ShowHoldMarker"/>
    /// at the end of its clip naming the StoryProgressManager task to wait for; the driver stops
    /// the show there and starts it again when the task is reported complete. Moving a hold is
    /// dragging a marker.
    ///
    /// O-1 AND O-2 ARE EMPTY. Their scenes are a camera and a light -- there is no content and no
    /// sub-timeline to nest yet. Their clips are placeholders with an authored guess at a duration,
    /// wired so that dropping content into the holder is all that is needed later.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildMainTimeline.Run
    /// </summary>
    public static class BuildMainTimeline
    {
        const string k_Timeline = "Assets/_Timeline/Main.playable";
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";
        const string k_DriverName = "ShowTimeline";

        /// <summary>
        /// The running order, taken from 合成肉身_VR_Production_Timeline_v2 (1010), sheet
        /// "VR Timeline", column Duration_sec. 18 scenes, 2260s -- the 38:00 that sheet's own
        /// 欄位與規則 tab quotes as the total.
        ///
        /// DURATIONS ARE THE DOCUMENT'S, NOT THE CODE'S. When the two disagree the document wins:
        /// it is what the other departments are building to. O-0 is the one place that still
        /// disagrees on purpose -- the document gives the scene 60s while the cue authored in
        /// BuildO0Timeline is 32s long, so the cue holds its last frame for the remaining 28.
        ///
        /// Gates sit only on the Onboarding beats, which is where the sheet puts its one explicit
        /// 停頓點 ("互動需要做完兩個", after O-1) and where the 完成條件 column asks for both
        /// players. Everything after that is timed; drop a ShowHoldMarker on the marker track to
        /// make one of them wait.
        /// </summary>
        static readonly (string holder, string label, double duration, string gate)[] k_Order =
        {
            ("O_0",        "O-0 進入",            60d,  null),
            ("Beat_O1",    "O-1 鬼的抓取",         90d,  "beat.O1_Control"),
            ("Beat_O2",    "O-2 鬼魂交流",         120d, "beat.O2_Grasp"),
            ("Beat_O3",    "O-3 Onboarding結束",   20d,  "beat.O3_Communion"),

            ("Beat_S1-1",  "S1-1 記憶痕跡成形",     40d,  null),
            ("Beat_S1-2",  "S1-2 吊掛的家",        120d, null),

            ("Beat_S2-1",  "S2-1 門關上之後",       60d,  null),
            ("Beat_S2-2",  "S2-2 碎片：燈",        180d, null),
            ("Beat_S2-3",  "S2-3 碎片：杯子",       120d, null),
            ("Beat_S2-4",  "S2-4 隱藏的記憶",       180d, null),

            ("Beat_S3A-1", "S3A-1 把家道別",        40d,  null),
            ("Beat_S3A-2", "S3A-2 回到依附的人",     300d, null),
            ("Beat_S3A-3", "S3A-3 物件化膜",        90d,  null),
            ("Beat_S3A-4", "S3A-4 沒有說出口的話",   240d, null),

            ("Beat_S3B-1", "S3B-1 真人消失",        60d,  null),
            ("Beat_S3B-2", "S3B-2 一片片的記憶",     240d, null),
            ("Beat_S3B-3", "S3B-3 最後一次旋轉",     180d, null),
            ("Beat_S3B-4", "S3B-4 膜鬆開",          120d, null),
        };

        [MenuItem("Tools/Show/Build Main Timeline")]
        static void RunFromMenu() => Run();

        public static void Run()
        {
            Scene scene = EditorSceneManager.OpenScene(k_MainScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[MainTimeline] RESULT: FAIL - could not open {k_MainScene}.");
                return;
            }

            TimelineAsset timeline = LoadOrCreate();
            if (timeline == null) return;

            PlayableDirector director = WireDriver(scene);
            if (director == null) return;

            Clear(timeline);

            if (!BuildOrder(scene, timeline, director)) return;

            director.playableAsset = timeline;

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(director);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.ImportAsset(k_Timeline, ImportAssetOptions.ForceSynchronousImport);

            Verify();
        }

        static TimelineAsset LoadOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(created, k_Timeline);
            Debug.Log($"[MainTimeline] created {k_Timeline}.");
            return AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
        }

        /// <summary>
        /// Empties the asset so a rebuild replaces the running order instead of doubling it.
        ///
        /// The asset is reused rather than deleted and remade, because deleting it would give the
        /// new file a new GUID and the director in the scene would be left pointing at nothing.
        /// </summary>
        static void Clear(TimelineAsset timeline)
        {
            var tracks = new List<TrackAsset>(timeline.GetRootTracks());
            foreach (TrackAsset track in tracks) timeline.DeleteTrack(track);

            if (timeline.markerTrack != null)
            {
                var markers = new List<IMarker>(timeline.markerTrack.GetMarkers());
                foreach (IMarker marker in markers) timeline.markerTrack.DeleteMarker(marker);
            }

            // DeleteMarker detaches a marker from the track but leaves the ScriptableObject behind
            // as a sub-asset of the .playable, so every rebuild would leave another set of orphans
            // in the file -- three per run, invisible in the Timeline window and confusing to
            // anyone reading the asset. Destroy any that are still in there.
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(k_Timeline))
            {
                if (sub is ShowHoldMarker) Object.DestroyImmediate(sub, true);
            }
        }

        /// <summary>
        /// Finds or makes the object that runs the show.
        ///
        /// Parented under the ExperienceDirector, which already carries the scene's NetworkObject.
        /// The driver keeps the show's position in server-owned NetworkVariables, and a
        /// NetworkBehaviour with no NetworkObject above it is never spawned -- so it would silently
        /// fall back to local time and the two headsets would drift.
        /// </summary>
        static PlayableDirector WireDriver(Scene scene)
        {
            var experience = Object.FindFirstObjectByType<ExperienceDirector>(FindObjectsInactive.Include);
            if (experience == null)
            {
                Debug.LogError("[MainTimeline] RESULT: FAIL - MainScene has no ExperienceDirector " +
                               "to hang the show timeline off.");
                return null;
            }

            Transform holder = experience.transform.Find(k_DriverName);
            if (holder == null)
            {
                var go = new GameObject(k_DriverName);
                go.transform.SetParent(experience.transform, false);
                holder = go.transform;
                Debug.Log($"[MainTimeline] created {k_DriverName} under ExperienceDirector.");
            }

            var director = holder.GetComponent<PlayableDirector>();
            if (director == null) director = holder.gameObject.AddComponent<PlayableDirector>();

            // Manual and not played on awake: the driver sets the time from the shared clock.
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.extrapolationMode = DirectorWrapMode.Hold;

            if (holder.GetComponent<ShowTimelineDriver>() == null)
                holder.gameObject.AddComponent<ShowTimelineDriver>();

            return director;
        }

        static bool BuildOrder(Scene scene, TimelineAsset timeline, PlayableDirector director)
        {
            timeline.CreateMarkerTrack();
            MarkerTrack markers = timeline.markerTrack;

            double at = 0d;
            int nested = 0;
            int gates = 0;
            int created = 0;

            foreach ((string holderName, string label, double duration, string gate) in k_Order)
            {
                GameObject target = FindInScene(scene, holderName);
                if (target == null)
                {
                    // The document has 18 scenes; the scene was built with holders for the four
                    // Onboarding beats and the older StoryBeat sheet. Rather than fail, make the
                    // holder -- an empty object is exactly what a beat with no content yet is, and
                    // it gives the Control clip something to point at until there is content.
                    Transform beats = FindBeatsRoot(scene);
                    if (beats == null)
                    {
                        Debug.LogError("[MainTimeline] RESULT: FAIL - no 'Beats' object in MainScene " +
                                       $"to create '{holderName}' under.");
                        return false;
                    }

                    var made = new GameObject(holderName);
                    made.transform.SetParent(beats, false);
                    made.SetActive(false);
                    target = made;
                    created++;
                }

                var track = timeline.CreateTrack<ControlTrack>(null, label);
                TimelineClip clip = track.CreateClip<ControlPlayableAsset>();
                clip.start = at;
                clip.duration = duration;
                clip.displayName = label;

                var control = (ControlPlayableAsset)clip.asset;

                // updateDirector is what nests the beat's own timeline: the clip drives that
                // director's time instead of letting it run itself.
                control.updateDirector = true;
                control.updateParticle = false;
                control.active = true;
                control.postPlayback = ActivationControlPlayable.PostPlaybackState.Revert;

                // The sub-director lives on a child of the holder (O_0/Timeline), not on the
                // holder itself, so the search has to go down the hierarchy.
                control.searchHierarchy = true;

                // ExposedReference is resolved through the director, not stored in the asset --
                // the asset has no idea which scene it is being played in.
                ExposedReference<GameObject> reference = control.sourceGameObject;
                reference.exposedName = System.Guid.NewGuid().ToString();
                control.sourceGameObject = reference;
                director.SetReferenceValue(reference.exposedName, target);

                bool hasOwnTimeline = HasSubDirector(target);
                if (hasOwnTimeline) nested++;

                Debug.Log($"[MainTimeline] {label,-16} {at,5:F1} -> {at + duration,5:F1}s" +
                          $"   {(hasOwnTimeline ? "nested sub-timeline" : "placeholder, no sub-timeline yet")}");

                at += duration;

                if (!string.IsNullOrWhiteSpace(gate))
                {
                    var hold = markers.CreateMarker<ShowHoldMarker>(at);
                    hold.SetTaskId(gate);
                    gates++;
                    Debug.Log($"[MainTimeline] {"hold",-16} {at,5:F1}s        waits for '{gate}'");
                }
            }

            Debug.Log($"[MainTimeline] {k_Order.Length} beat(s), {nested} with a sub-timeline, " +
                      $"{created} holder(s) created, {gates} gate(s); " +
                      $"{at:F0}s = {(int)at / 60}m{(int)at % 60:00}s of running order.");
            return true;
        }

        /// <summary>The object the per-beat holders live under.</summary>
        static Transform FindBeatsRoot(Scene scene)
        {
            GameObject beats = FindInScene(scene, "Beats");
            return beats != null ? beats.transform : null;
        }

        /// <summary>Whether this beat brings its own timeline, or is still an empty holder.</summary>
        static bool HasSubDirector(GameObject holder)
        {
            foreach (var candidate in holder.GetComponentsInChildren<PlayableDirector>(true))
            {
                if (candidate.playableAsset != null) return true;
            }
            return false;
        }

        static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                GameObject found = FindIn(root.transform, name);
                if (found != null) return found;
            }
            return null;
        }

        static GameObject FindIn(Transform parent, string name)
        {
            if (parent.name == name) return parent.gameObject;
            for (int i = 0; i < parent.childCount; i++)
            {
                GameObject found = FindIn(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Re-opens the scene and checks the things that would make the show not run: a director
        /// left on a clock of its own, a Control clip whose target did not survive the save, a
        /// hold with no task on it.
        /// </summary>
        static void Verify()
        {
            Scene scene = EditorSceneManager.OpenScene(k_MainScene, OpenSceneMode.Single);

            var driver = Object.FindFirstObjectByType<ShowTimelineDriver>(FindObjectsInactive.Include);
            if (driver == null)
            {
                Debug.LogError("[MainTimeline] RESULT: FAIL - no ShowTimelineDriver in MainScene.");
                return;
            }

            var director = driver.GetComponent<PlayableDirector>();
            if (director == null || director.playableAsset == null)
            {
                Debug.LogError("[MainTimeline] RESULT: FAIL - the driver has no timeline to run.");
                return;
            }
            Debug.Log("[MainTimeline] OK   driver and timeline");

            if (director.timeUpdateMode != DirectorUpdateMode.Manual)
            {
                Debug.LogError("[MainTimeline] RESULT: FAIL - the show director is not Manual, so it " +
                               "would run on this machine's clock and the two headsets would drift.");
                return;
            }
            Debug.Log("[MainTimeline] OK   Manual update mode");

            if (driver.GetComponentInParent<Unity.Netcode.NetworkObject>() == null)
            {
                Debug.LogError("[MainTimeline] RESULT: FAIL - the driver has no NetworkObject above " +
                               "it, so it is never spawned and the hold would be decided locally.");
                return;
            }
            Debug.Log("[MainTimeline] OK   under a NetworkObject");

            var timeline = (TimelineAsset)director.playableAsset;

            int clips = 0;
            int unbound = 0;
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is not ControlTrack) continue;
                foreach (TimelineClip clip in track.GetClips())
                {
                    clips++;
                    var control = (ControlPlayableAsset)clip.asset;

                    // Resolved through the director itself, which is the exposed-property table.
                    // Going via director.playableGraph would mean building a graph first -- it has
                    // not played, so there is no graph to ask.
                    if (control.sourceGameObject.Resolve(director) == null)
                        unbound++;
                }
            }

            if (clips == 0)
            {
                Debug.LogError("[MainTimeline] RESULT: FAIL - no Control clips; nothing is nested.");
                return;
            }
            if (unbound > 0)
            {
                Debug.LogError($"[MainTimeline] RESULT: FAIL - {unbound} of {clips} Control clip(s) " +
                               "point at nothing. The exposed reference did not survive the save, " +
                               "so those beats would not be driven at all.");
                return;
            }
            Debug.Log($"[MainTimeline] OK   {clips} nested beat clip(s), all bound");

            int holds = 0;
            if (timeline.markerTrack != null)
            {
                foreach (IMarker marker in timeline.markerTrack.GetMarkers())
                {
                    if (marker is not ShowHoldMarker hold) continue;
                    holds++;
                    if (!hold.gates)
                        Debug.LogWarning($"[MainTimeline] a hold at {hold.time:F1}s has no task on it, " +
                                         "so it will not stop the show.");
                }
            }
            Debug.Log($"[MainTimeline] OK   {holds} hold(s)");

            Debug.Log($"[MainTimeline] RESULT: PASS - {timeline.duration:F1}s running order, " +
                      $"{clips} beat(s), {holds} gate(s).");
        }
    }
}
