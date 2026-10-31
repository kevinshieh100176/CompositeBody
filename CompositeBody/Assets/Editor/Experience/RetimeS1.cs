using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using CompositeBody.Experience;
using Object = UnityEngine.Object;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Puts S1 on the running order the new S1 script gives it: six beats instead of two, with
    /// the names and the boundaries off that sheet.
    ///
    /// NOT PART OF BuildMainTimeline, AND IT CANNOT BE. That builder chains every beat end to
    /// end from one duration table, which is the right model for a show whose sections are
    /// authored in order -- and it is exactly the model S1 has now outgrown. S1 runs to 11:49
    /// while S2-1 still starts at 09:20, because S2's own times have not been re-cut yet; the
    /// two sections overlap on purpose and a chaining builder has no way to say that. So this
    /// edits Main.playable in place, the way RetimeOnboardingHandoff does, and touches nothing
    /// outside S1 and the two clips that straddle its edges.
    ///
    /// THE SHEET'S TIMES, WITH ONE DELIBERATE DEPARTURE. The script gives 收黑 its own row at
    /// 06:40-06:50 and starts S1-1 at 06:50, which would take ten seconds off a beat whose end
    /// is fixed at 09:30. The decision taken is to give those ten seconds back to S1-1 at the
    /// front -- S1-1 runs 06:40-09:30, 170s, and its first ten seconds play under the blackout --
    /// so nothing after 09:30 has to move. Every other boundary below is the sheet's own.
    ///
    ///   收黑           0640 -> 0650    10s   centred on the join, so 400.0 is fully black
    ///   S1-1 窗內的世界  0640 -> 0930   170s   (sheet says 0650; the 10s is added at the front)
    ///   S1-2 門與草原    0930 -> 0930     -    the sheet's 時長 cell is empty; no clip yet
    ///   S1-3 黑白房子群  0930 -> 1006    36s
    ///   S1-4 紅色剖開房子 1006 -> 1049    43s
    ///   S1-5 霧氣之房左  1049 -> 1111    22s
    ///   S1-6 霧氣之房右  1111 -> 1149    38s
    ///
    /// S1-2 IS A TRACK WITH NOTHING ON IT, which is what a beat with no agreed length is. The
    /// sheet chains S1-3 off 09:30 regardless, so the beat has a position and no duration; a
    /// placeholder clip would have to invent one, and a one-frame clip would be a beat that
    /// silently never plays. The track, the holder and the sub-timeline are all here, so
    /// deciding the length later is dragging a clip onto a track that is already wired.
    ///
    /// THE BLACKOUT STRADDLES THE JOIN rather than sitting after it. The sheet writes 收黑 as a
    /// row of its own running 06:40-06:50, but a fade placed to start at the cut is clear at the
    /// moment the cut happens -- S1-1 would appear in front of the audience and only then go
    /// dark. RetimeOnboardingHandoff already decided this for the 6s version and the rule holds
    /// at 10: half either side, so 400.0 is the blackest frame.
    ///
    /// AND THE S1 ROOM HAS TO STAY UP FOR ALL OF IT. The activation clip holding Beat_S1/Content
    /// was cut to end at 580 when S1 was two beats long; left alone it would switch the S1 world
    /// off in the middle of S1-4. It is widened to cover the whole section.
    ///
    /// IDEMPOTENT. Every clip is anchored to an absolute time from the table rather than shifted
    /// by a delta, and holders, tracks and sub-timelines are found before they are made, so a
    /// second run reports no change.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.RetimeS1.Run
    /// </summary>
    public static class RetimeS1
    {
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";
        const string k_Timeline = "Assets/_Timeline/Main.playable";
        const string k_TimelineFolder = "Assets/_Timeline";
        const string k_FadeTrack = "Fade";

        /// <summary>How long the blackout is, from the 收黑 row. It is centred on S1's start.</summary>
        const double k_FadeDuration = 10d;

        /// <summary>
        /// S1's running order, in seconds off the show clock.
        ///
        /// <c>holder</c> is the empty object under Beats the Control clip points at, <c>sub</c>
        /// the beat's own timeline asset under <see cref="k_TimelineFolder"/>, and a
        /// <c>duration</c> of zero means the beat is on the order but has no length yet, so its
        /// track is left without a clip.
        ///
        /// 門與草原 GETS NO TIMELINE ASSET YET, and the existing S1_2.playable is not it. That
        /// file carries the old S1-2 吊掛的家's two VO files out of _Audio/old, and the beat
        /// replacing it is a different beat; inheriting a dead beat's audio by name would be a
        /// worse kind of wrong than having nothing. A beat with no agreed length has nothing to
        /// author into a timeline anyway, so its holder and track wait and the asset is made
        /// when the length lands.
        /// </summary>
        static readonly (string id, string label, string holder, string sub, double start, double duration)[] k_S1 =
        {
            ("S1-1", "S1-1 窗內的世界",   "Beat_S1-1", "S1_1", 400d, 170d),
            ("S1-2", "S1-2 門與草原",     "Beat_S1-2", null,   570d,   0d),
            ("S1-3", "S1-3 黑白房子群",   "Beat_S1-3", "S1_3", 570d,  36d),
            ("S1-4", "S1-4 紅色剖開房子", "Beat_S1-4", "S1_4", 606d,  43d),
            ("S1-5", "S1-5 霧氣之房左",   "Beat_S1-5", "S1_5", 649d,  22d),
            ("S1-6", "S1-6 霧氣之房右",   "Beat_S1-6", "S1_6", 671d,  38d),
        };

        [MenuItem("Tools/Show/Retime S1 to the new script")]
        public static void Run()
        {
            Debug.Log("[RetimeS1] Starting...");

            Scene scene = EditorSceneManager.OpenScene(k_MainScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[RetimeS1] RESULT: FAIL - could not open {k_MainScene}.");
                return;
            }

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (timeline == null)
            {
                Debug.LogError($"[RetimeS1] RESULT: FAIL - no TimelineAsset at {k_Timeline}.");
                return;
            }

            var driver = Object.FindFirstObjectByType<ShowTimelineDriver>(FindObjectsInactive.Include);
            PlayableDirector director = driver != null ? driver.GetComponent<PlayableDirector>() : null;
            if (director == null)
            {
                Debug.LogError("[RetimeS1] RESULT: FAIL - MainScene has no ShowTimelineDriver " +
                               "with a PlayableDirector, so the Control clips have nothing to " +
                               "resolve their targets through.");
                return;
            }

            Transform beats = FindBeatsRoot(scene);
            if (beats == null)
            {
                Debug.LogError("[RetimeS1] RESULT: FAIL - no 'Beats' object in MainScene.");
                return;
            }

            var madeTracks = new List<TrackAsset>();
            TrackAsset anchor = null;

            foreach ((string id, string label, string holderName, string sub, double start, double duration) in k_S1)
            {
                GameObject holder = Holder(scene, beats, holderName);
                if (sub != null) WireSubDirector(holder, SubTimeline(sub));

                bool made = false;
                ControlTrack track = FindTrack(timeline, id);
                if (track == null)
                {
                    track = timeline.CreateTrack<ControlTrack>(null, label);
                    madeTracks.Add(track);
                    made = true;
                }
                else if (track.name != label)
                {
                    Debug.Log($"[RetimeS1] renamed track '{track.name}' -> '{label}'.");
                    track.name = label;
                }

                if (!made) anchor = track;

                if (duration <= 0d)
                {
                    StripClips(track, director);
                    Debug.Log($"[RetimeS1] {label,-18} {start,6:F1}s          no clip: the sheet " +
                              "gives this beat no length yet. Its track and holder are here; drop " +
                              "a clip on the track once the length is agreed.");
                    continue;
                }

                TimelineClip clip = FirstClip(track) ?? track.CreateClip<ControlPlayableAsset>();
                bool movedClip = System.Math.Abs(clip.start - start) > 0.001d ||
                                 System.Math.Abs(clip.duration - duration) > 0.001d;
                clip.start = start;
                clip.duration = duration;
                clip.displayName = label;

                Bind(clip, director, holder);

                Debug.Log($"[RetimeS1] {label,-18} {start,6:F1} -> {start + duration,6:F1}s" +
                          $"   {(made ? "new track" : movedClip ? "retimed" : "already there")}" +
                          $", nests {sub}.playable");
            }

            if (madeTracks.Count > 0) PlaceAfter(timeline, madeTracks, anchor);

            double boundary = k_S1[0].start;
            double s1End = 0d;
            foreach (var beat in k_S1) s1End = System.Math.Max(s1End, beat.start + beat.duration);

            Fade(timeline, boundary);
            HoldS1Room(timeline, boundary, s1End);

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(director);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.ImportAsset(k_Timeline, ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[RetimeS1] S1 runs {boundary:F0}-{s1End:F0}s " +
                      $"({(int)boundary / 60}:{(int)boundary % 60:00} - {(int)s1End / 60}:{(int)s1End % 60:00}), " +
                      $"{k_S1.Length} beats.");
            Report(timeline);

            Debug.Log("[RetimeS1] RESULT: PASS");
        }

        /// <summary>
        /// The track a beat already has, found by its id rather than its label -- every one of
        /// these beats is being renamed, so the label is the one thing that cannot be matched on.
        /// </summary>
        static ControlTrack FindTrack(TimelineAsset timeline, string id)
        {
            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is ControlTrack control && track.name.StartsWith(id + " ")) return control;
            }
            return null;
        }

        static TimelineClip FirstClip(TrackAsset track)
        {
            foreach (TimelineClip clip in track.GetClips()) return clip;
            return null;
        }

        /// <summary>
        /// Takes a track back to having no clips, and releases the exposed reference each one
        /// held -- an unreferenced entry left in the director's table is a GUID pointing at an
        /// object nothing can reach, and they accumulate.
        /// </summary>
        static void StripClips(TrackAsset track, PlayableDirector director)
        {
            foreach (TimelineClip clip in new List<TimelineClip>(track.GetClips()))
            {
                if (clip.asset is ControlPlayableAsset control)
                {
                    PropertyName exposed = control.sourceGameObject.exposedName;
                    if (!PropertyName.IsNullOrEmpty(exposed)) director.ClearReferenceValue(exposed);
                }

                Debug.Log($"[RetimeS1] removed '{clip.displayName}' " +
                          $"({clip.start:F1}-{clip.start + clip.duration:F1}s) from '{track.name}'.");
                track.DeleteClip(clip);
            }
        }

        /// <summary>
        /// Point a Control clip at its holder, keeping whatever exposedName it already had so a
        /// re-run does not strand the old entry in the director's reference table.
        /// </summary>
        static void Bind(TimelineClip clip, PlayableDirector director, GameObject holder)
        {
            var control = (ControlPlayableAsset)clip.asset;

            control.updateDirector = true;
            control.updateParticle = false;
            control.searchHierarchy = true;
            control.active = true;
            control.postPlayback = ActivationControlPlayable.PostPlaybackState.Revert;

            ExposedReference<GameObject> reference = control.sourceGameObject;
            if (PropertyName.IsNullOrEmpty(reference.exposedName))
            {
                reference.exposedName = System.Guid.NewGuid().ToString();
                control.sourceGameObject = reference;
            }

            director.SetReferenceValue(reference.exposedName, holder);
        }

        static Transform FindBeatsRoot(Scene scene)
        {
            GameObject beats = FindInScene(scene, "Beats");
            return beats != null ? beats.transform : null;
        }

        /// <summary>The empty object the Control clip switches on, made if it is not there yet.</summary>
        static GameObject Holder(Scene scene, Transform beats, string name)
        {
            GameObject found = FindInScene(scene, name);
            if (found != null) return found;

            var made = new GameObject(name);
            made.transform.SetParent(beats, false);
            made.SetActive(false);
            Debug.Log($"[RetimeS1] created holder '{name}' under Beats.");
            return made;
        }

        /// <summary>The beat's own timeline, made empty if it is not there yet.</summary>
        static TimelineAsset SubTimeline(string name)
        {
            string path = $"{k_TimelineFolder}/{name}.playable";

            var existing = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<TimelineAsset>();
            created.editorSettings.frameRate = 60d;
            AssetDatabase.CreateAsset(created, path);
            Debug.Log($"[RetimeS1] created {path}.");
            return AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
        }

        /// <summary>
        /// Give the holder a director running the beat's own timeline.
        ///
        /// Manual and not played on awake, like every other sub-director in the show: the
        /// Control clip on Main.playable writes this director's time, and one that ran on its
        /// own clock as well would fight it.
        /// </summary>
        static void WireSubDirector(GameObject holder, TimelineAsset subTimeline)
        {
            var director = holder.GetComponent<PlayableDirector>();
            if (director == null)
            {
                director = holder.AddComponent<PlayableDirector>();
                Debug.Log($"[RetimeS1] added a PlayableDirector to '{holder.name}'.");
            }

            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.extrapolationMode = DirectorWrapMode.Hold;

            if (director.playableAsset != subTimeline) director.playableAsset = subTimeline;

            EditorUtility.SetDirty(director);
        }

        /// <summary>The blackout: the sheet's length, centred on the join.</summary>
        static void Fade(TimelineAsset timeline, double boundary)
        {
            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is not PlayableTrack || track.name != k_FadeTrack) continue;

                foreach (TimelineClip clip in track.GetClips())
                {
                    double start = boundary - k_FadeDuration * 0.5d;
                    Debug.Log($"[RetimeS1] 收黑 '{clip.displayName}': " +
                              $"{clip.start:F1}-{clip.start + clip.duration:F1}s -> " +
                              $"{start:F1}-{start + k_FadeDuration:F1}s, centred on {boundary:F1}.");
                    clip.start = start;
                    clip.duration = k_FadeDuration;
                }
                return;
            }

            Debug.LogWarning($"[RetimeS1] no '{k_FadeTrack}' track on the timeline; the blackout " +
                             "across the Onboarding -> S1 join was not retimed.");
        }

        /// <summary>
        /// Hold the S1 world up for the whole section. The activation clip that starts at the
        /// join is the one -- the other one holds the onboarding room up from zero -- and it is
        /// told apart by where it starts, because Timeline names every activation clip "Active".
        /// </summary>
        static void HoldS1Room(TimelineAsset timeline, double boundary, double s1End)
        {
            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is not ActivationTrack) continue;

                foreach (TimelineClip clip in track.GetClips())
                {
                    if (clip.start < 1d) continue;

                    double duration = s1End - boundary;
                    if (System.Math.Abs(clip.start - boundary) < 0.1d &&
                        System.Math.Abs(clip.duration - duration) < 0.1d)
                    {
                        Debug.Log($"[RetimeS1] the S1 room is already held {clip.start:F1}-" +
                                  $"{clip.start + clip.duration:F1}s.");
                        return;
                    }

                    Debug.Log($"[RetimeS1] the S1 room is held " +
                              $"{clip.start:F1}-{clip.start + clip.duration:F1}s -> " +
                              $"{boundary:F1}-{boundary + duration:F1}s, so it is still up for S1-6.");
                    clip.start = boundary;
                    clip.duration = duration;
                    return;
                }
            }
        }

        /// <summary>
        /// Move freshly made tracks up next to the ones they belong with. Timeline appends new
        /// tracks to the end of the asset, which would leave S1-3..S1-6 sitting under S3B-4 and
        /// the running order unreadable in the window.
        /// </summary>
        static void PlaceAfter(TimelineAsset timeline, List<TrackAsset> tracks, TrackAsset anchor)
        {
            var so = new SerializedObject(timeline);
            SerializedProperty list = so.FindProperty("m_Tracks");
            if (list == null || !list.isArray) return;

            var order = new List<Object>();
            for (int i = 0; i < list.arraySize; i++)
                order.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);

            foreach (TrackAsset track in tracks) order.Remove(track);

            int at = anchor != null ? order.IndexOf(anchor) : -1;
            if (at < 0) at = order.Count - 1;
            order.InsertRange(at + 1, tracks);

            for (int i = 0; i < order.Count && i < list.arraySize; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = order[i];

            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[RetimeS1] moved {tracks.Count} new track(s) up under " +
                      $"'{(anchor != null ? anchor.name : "the end")}'.");
        }

        /// <summary>
        /// The running order as it now stands, so the overlap with S2 is on the record rather
        /// than a surprise in the Timeline window. S2's own times are not this tool's business.
        /// </summary>
        static void Report(TimelineAsset timeline)
        {
            var rows = new List<(double start, double end, string name)>();
            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is not ControlTrack) continue;
                foreach (TimelineClip clip in track.GetClips())
                    rows.Add((clip.start, clip.start + clip.duration, track.name));
            }
            rows.Sort((a, b) => a.start.CompareTo(b.start));

            double reach = 0d;
            foreach ((double start, double end, string name) in rows)
            {
                string flag = start < reach - 0.001d ? "   <- overlaps what is above it" : "";
                Debug.Log($"[RetimeS1]   {start,6:F1} -> {end,6:F1}  {name}{flag}");
                reach = System.Math.Max(reach, end);
            }
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
    }
}
