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
    /// NOT PART OF BuildMainTimeline, AND IT CANNOT BE. That builder authors the whole running
    /// order from one duration table and would have to be given every beat's real length to be
    /// run at all; S1 is the only section whose sheet has landed, and rebuilding from the table
    /// would quietly undo the hand work on the Fade, Activation and Animation tracks that is not
    /// in it. So this edits Main.playable in place, the way RetimeOnboardingHandoff does.
    ///
    /// THE SHEET'S TIMES, WITH ONE DELIBERATE DEPARTURE. The script gives 收黑 its own row at
    /// 06:40-06:50 and starts S1-1 at 06:50, which would take ten seconds off a beat whose end
    /// is fixed at 09:40. The decision taken is to give those ten seconds back to S1-1 at the
    /// front -- S1-1 runs 06:40-09:40, 180s, and its first ten seconds play under the blackout --
    /// so nothing after 09:40 has to move. Every other boundary below is the sheet's own.
    ///
    ///   收黑           0640 -> 0650    10s   centred on the join, so 400.0 is fully black
    ///   S1-1 窗內的世界  0640 -> 0940   180s   (sheet says 0650; the 10s is added at the front)
    ///   S1-2 門與草原    0940 -> 1010    30s
    ///   S1-3 黑白房子群  1010 -> 1054    44s
    ///   S1-4 紅色剖開房子 1054 -> 1147    53s
    ///   S1-5 霧氣之房左  1147 -> 1209    22s
    ///   S1-6 霧氣之房右  1209 -> 1247    38s
    ///
    /// 門與草原 HAS A LENGTH NOW, and the second pass of the sheet is where it got one. It was
    /// a track with no clip while its 時長 cell was empty -- a beat with a position and no
    /// duration -- and it is an ordinary 30s beat from here on. A zero in the table still means
    /// the empty-cell case, because the sheet has done it once and can do it again.
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
    /// EVERYTHING AFTER S1 HANGS OFF S1'S END, which is why this tool moves clips it did not
    /// author. The back half was chained to 11:49 when that was where S1 finished; the second
    /// sheet pushed S1 out to 12:47 and left the handover behind, so the S1 → S2 blackout fired
    /// in the middle of S1-5, S2's content switched on over S1-6, and S2-1 ran underneath both.
    /// A section boundary is a relationship, not a number typed twice.
    ///
    /// SO IT IS ANCHORED, NOT SHIFTED. The whole block after S1 is moved as one rigid piece, by
    /// the gap between where it currently starts and where S1 now ends -- durations, gaps and
    /// overlaps inside it are preserved exactly, nothing is re-chained, and once it is in place
    /// the gap is zero and a second run moves nothing. A delta would have been simpler to write
    /// and would double every time somebody ran this twice.
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

        /// <summary>
        /// The one clip on the Fade track this tool owns, by name.
        ///
        /// THE FADE TRACK CARRIES EVERY SECTION'S BLACKOUT, not just this one. It gained a
        /// 黑場 S1 → S2 while this tool was away, and an earlier version of this method retimed
        /// every clip it found on the track -- so the S1 → S2 blackout was dragged on top of
        /// this one at 395s, which is the sort of damage that looks like nothing until the show
        /// runs. Where S1 hands over to S2 is S2's boundary to decide, not this tool's, so it
        /// moves the one blackout it is responsible for and says what it left alone.
        /// </summary>
        const string k_FadeClip = "黑場 Onboarding → S1";

        /// <summary>
        /// The blackout at S1's other end. Its length is left as whoever authored it set it --
        /// how long a blackout lasts is a decision, and only where it sits is derivable.
        /// </summary>
        const string k_FadeOutClip = "黑場 S1 → S2";

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
        /// S1_2.playable IS NOT THE ONE THAT WAS THERE. The file that held that name carried the
        /// old S1-2 吊掛的家's two VO files out of _Audio/old -- 98s and 111s of them, which do
        /// not fit a 30s beat and were never for this one. Nothing referenced it, so it moved to
        /// _Timeline/old/ the way superseded audio goes to _Audio/old, and 門與草原 gets a clean
        /// asset at the name its beat is called. Inheriting a dead beat's voice-over by name
        /// would have been the quiet kind of wrong.
        /// </summary>
        static readonly (string id, string label, string holder, string sub, double start, double duration)[] k_S1 =
        {
            ("S1-1", "S1-1 窗內的世界",   "Beat_S1-1", "S1_1", 400d, 180d),
            ("S1-2", "S1-2 門與草原",     "Beat_S1-2", "S1_2", 580d,  30d),
            ("S1-3", "S1-3 黑白房子群",   "Beat_S1-3", "S1_3", 610d,  44d),
            ("S1-4", "S1-4 紅色剖開房子", "Beat_S1-4", "S1_4", 654d,  53d),
            ("S1-5", "S1-5 霧氣之房左",   "Beat_S1-5", "S1_5", 707d,  22d),
            ("S1-6", "S1-6 霧氣之房右",   "Beat_S1-6", "S1_6", 729d,  38d),
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

            Fade(timeline, k_FadeClip, boundary, k_FadeDuration);
            HoldS1Room(timeline, boundary, s1End);
            HandOverToS2(timeline, boundary, s1End);

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

        /// <summary>
        /// Centre one named blackout on a boundary, and leave every other clip on the Fade track
        /// exactly where it is. A <paramref name="length"/> of zero keeps the clip's own.
        ///
        /// BY NAME, AND ONLY BY NAME. The Fade track carries a blackout per section handover and
        /// an earlier version of this swept all of them onto S1's front door, which put the
        /// S1 → S2 blackout 300s out of place and reported success. A clip this cannot find is a
        /// warning, never a guess at which of the others was meant.
        /// </summary>
        static void Fade(TimelineAsset timeline, string clipName, double boundary, double length)
        {
            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is not PlayableTrack || track.name != k_FadeTrack) continue;

                foreach (TimelineClip clip in track.GetClips())
                {
                    if (clip.displayName != clipName) continue;

                    double width = length > 0d ? length : clip.duration;
                    double start = boundary - width * 0.5d;

                    if (System.Math.Abs(clip.start - start) < 0.001d &&
                        System.Math.Abs(clip.duration - width) < 0.001d)
                    {
                        Debug.Log($"[RetimeS1] 收黑 '{clipName}' is already centred on " +
                                  $"{boundary:F1}s at {clip.start:F1}-{clip.start + clip.duration:F1}s.");
                        return;
                    }

                    Debug.Log($"[RetimeS1] 收黑 '{clipName}': " +
                              $"{clip.start:F1}-{clip.start + clip.duration:F1}s -> " +
                              $"{start:F1}-{start + width:F1}s, centred on {boundary:F1}.");
                    clip.start = start;
                    clip.duration = width;
                    return;
                }

                Debug.LogWarning($"[RetimeS1] no clip called '{clipName}' on the '{k_FadeTrack}' " +
                                 "track, so that blackout was not retimed. Nothing else on the " +
                                 "track was moved in its place; rename the clip, or fix the " +
                                 "constant here, rather than letting this guess.");
                return;
            }

            Debug.LogWarning($"[RetimeS1] no '{k_FadeTrack}' track on the timeline, so '{clipName}' " +
                             "was not retimed.");
        }

        /// <summary>Whether a track is one of S1's own, by the ids in <see cref="k_S1"/>.</summary>
        static bool IsS1Track(TrackAsset track)
        {
            foreach ((string id, _, _, _, _, _) in k_S1)
                if (track.name.StartsWith(id + " ")) return true;
            return false;
        }

        /// <summary>
        /// Put everything after S1 back against S1's end, as one rigid block.
        ///
        /// The block is every Control clip that is neither one of S1's nor part of the onboarding
        /// in front of it, plus the activation clips and hold markers that sit with it. It moves
        /// by the gap between where it starts now and where S1 now ends, so a run that finds it
        /// already there moves nothing.
        /// </summary>
        static void HandOverToS2(TimelineAsset timeline, double s1Start, double s1End)
        {
            var block = new List<TimelineClip>();
            double anchor = double.MaxValue;

            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is not ControlTrack || IsS1Track(track)) continue;
                foreach (TimelineClip clip in track.GetClips())
                {
                    if (clip.start < s1Start) continue;   // the onboarding beats, in front of S1
                    block.Add(clip);
                    anchor = System.Math.Min(anchor, clip.start);
                }
            }

            if (block.Count == 0)
            {
                Debug.Log("[RetimeS1] nothing after S1 on the running order to hand over to.");
                return;
            }

            double delta = s1End - anchor;
            if (System.Math.Abs(delta) < 0.001d)
            {
                Debug.Log($"[RetimeS1] the {block.Count} beat(s) after S1 already start where S1 " +
                          $"ends, at {s1End:F1}s.");
            }
            else
            {
                foreach (TimelineClip clip in block) clip.start += delta;

                int rooms = 0;
                foreach (TrackAsset track in timeline.GetRootTracks())
                {
                    if (track is not ActivationTrack) continue;
                    foreach (TimelineClip clip in track.GetClips())
                    {
                        if (clip.start < anchor - 0.001d) continue;
                        clip.start += delta;
                        rooms++;
                    }
                }

                int holds = 0;
                if (timeline.markerTrack != null)
                {
                    foreach (IMarker marker in timeline.markerTrack.GetMarkers())
                    {
                        if (marker.time < anchor - 0.001d) continue;
                        marker.time += delta;
                        holds++;
                    }
                }

                Debug.Log($"[RetimeS1] moved the {block.Count} beat(s) after S1 by " +
                          $"{delta:+0.0;-0.0}s -- they started at {anchor:F1}s and S1 now ends at " +
                          $"{s1End:F1}s -- along with {rooms} activation clip(s) and {holds} hold " +
                          "marker(s). Durations and every gap inside the block are unchanged.");
            }

            Fade(timeline, k_FadeOutClip, s1End, 0d);
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
        /// The running order as it now stands, with every blackout in its place, so an overlap
        /// is on the record rather than a surprise in the Timeline window.
        /// </summary>
        static void Report(TimelineAsset timeline)
        {
            var rows = new List<(double start, double end, string name, bool beat)>();
            double reach = 0d;

            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                foreach (TimelineClip clip in track.GetClips())
                {
                    if (track is ControlTrack)
                    {
                        rows.Add((clip.start, clip.start + clip.duration, track.name, true));
                        reach = System.Math.Max(reach, clip.start + clip.duration);
                    }
                    else if (track is PlayableTrack && track.name == k_FadeTrack)
                    {
                        rows.Add((clip.start, clip.start + clip.duration, clip.displayName, false));
                    }
                }
            }

            rows.Sort((a, b) => a.start.CompareTo(b.start));

            // Only beats can overlap each other meaningfully; a blackout straddling a boundary is
            // the whole point of a blackout and must not be reported as a clash.
            double covered = 0d;
            foreach ((double start, double end, string name, bool beat) in rows)
            {
                string flag = beat && start < covered - 0.001d ? "   <- overlaps the beat above it" : "";
                Debug.Log($"[RetimeS1]   {start,6:F1} -> {end,6:F1}  {name}{flag}");
                if (beat) covered = System.Math.Max(covered, end);
            }

            Debug.Log($"[RetimeS1] the show now runs {reach:F0}s = " +
                      $"{(int)reach / 60}m{(int)reach % 60:00}s.");
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
