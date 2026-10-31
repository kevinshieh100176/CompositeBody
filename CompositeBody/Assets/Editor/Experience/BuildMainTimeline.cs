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
    /// THE THREE ONBOARDING BEATS ALL NEST THEIR OWN TIMELINE NOW -- O-0's cue and handover,
    /// O-1's dissolves, O-2's orbit lights -- and all three directors live in the onboarding
    /// prefab beside the content they drive. The S beats are still empty holders with an authored
    /// guess at a duration, wired so that dropping content in is all that is needed later.
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
        /// The running order, taken from 合成肉身_VR_Production_Timeline_v4, sheet "VR Timeline".
        /// Its own 時長 chain totals 2370s = 39:30; the 欄位與規則 tab still quotes 38:00, which is
        /// left over from v2 (1010) and is the number this table used to be built from.
        ///
        /// READ THE 時長 COLUMN, NOT THE TC COLUMNS. In v4 the 開始TC / 結束TC cells are stale from
        /// the 轉場一 row down -- they are 90s behind, because the 第一段VO row was inserted and the
        /// TCs were never recalculated, and they still chain to v3's old 0:05:10 boundary. 時長 is
        /// the maintained column and chains cleanly, so it is the one to trust.
        ///
        /// DURATIONS ARE THE DOCUMENT'S, NOT THE CODE'S. When the two disagree the document wins:
        /// it is what the other departments are building to. O-0 ran 45s over for a while and no
        /// longer does -- the second 自白 was moved under the body's arrival instead of after it,
        /// so the beat is the sheet's 140s again and every boundary below is v4's. What survives
        /// of that disagreement is the body itself rather than a number; the note on the O-0 row
        /// says what it was and why the row cannot be shortened further.
        ///
        /// THE NEW ONBOARDING LINES HAVE LANDED, so O-1 and O-2 move to v4's durations. This
        /// note used to say they were held back: the sheet gives O-1 three sub-beats
        /// (燈 / T-shirt 35 / 封箱 50) and O-2 90s, and all four of the VO files it asks for
        /// were missing, so widening O-1 would have bought 70s of dead air rather than a longer
        /// beat. All four are in the project now and their lengths fit the sub-beats they were
        /// written for, to within a second and a half:
        ///
        ///   燈        85s   O-AB1_睡前                  74.5s, both players hear it together
        ///   T-shirt   35s   O-AB2_搶衣服                 34.0s, likewise
        ///   封箱      50s   O-A3 / O-B3_有些東西可以放進箱子  37.0s each, ONE PER PLAYER
        ///
        /// 燈 IS 85s AND NOT THE 75s ITS 時長 CELL SAYS. That row is the one place in the
        /// Onboarding block where the two columns disagree -- its TC span (0220->0345) is 85s
        /// while 時長 reads 75 -- and 85 is the one to build to: the extra ten seconds are the
        /// cartons and the wall arriving before anyone is asked for anything, which is authored
        /// on O-1's own track at 1.5-12s and is time the beat really does spend. The other two
        /// rows' columns agree. So O-1 is 170s, not the 160 the 時長 chain adds up to, and every
        /// boundary below it sits 10s later than the 時長 column alone would put it.
        ///
        /// 封箱 IS A LINE WITH NOTHING UNDER IT. Sealing the carton is an interaction nobody has
        /// built and it is deliberately parked; its two recordings are wired so the running
        /// order is recorded, and O1GrabWaves simply stops after the jacket. That does not block
        /// the beat -- the gate is the boxes being filled, not the waves being finished.
        ///
        /// O-3 IS NOT TOUCHED. v4 replaces it with a 10s 收黑, which would drop the 停頓點 that
        /// waits for both players, and that row is not in the crop this was re-read from.
        ///
        /// Gates sit only on the Onboarding beats, which is where the sheet puts its one explicit
        /// 停頓點 ("互動需要做完兩個", after O-1) and where the 完成條件 column asks for both
        /// players. Everything after that is timed; drop a ShowHoldMarker on the marker track to
        /// make one of them wait.
        /// </summary>
        static readonly (string holder, string label, double duration, string gate, double holdLead)[] k_Order =
        {
            // O-0 POINTS AT A PATH, NOT A NAME, and the path is into live content. Every other
            // entry here names an empty holder under Beats; this one names the director that
            // actually owns the opening cue, inside the Onboarding prefab instance. Until now the
            // lookup found no object called "O_0", quietly made an empty holder and nested
            // nothing -- so the cue was driven by nobody at all: O0ArrivalBeat stands down
            // whenever a master driver exists, and the master was pointing at a stub.
            // A HUNDRED AND FORTY SECONDS, WHICH IS WHAT v4 SAYS. It was 185 for a while, and
            // the argument is worth keeping because the beat did not lose anything to get here.
            //
            // WHAT v4 ASKS FOR. Its three O-0 rows are 重點句 20s, O-0｜進入 / 第一段VO 90s (one
            // and the same beat, written as two rows) and 轉場一｜紙箱 30s -- 140s in all. The 30s
            // is the tight one: it has to hold 真人消失, the player's body arriving AND the 28s
            // second 自白, so those three have to overlap rather than queue.
            //
            // AND THEY DO OVERLAP NOW. The 185 was the same five events played strictly one
            // after another -- 20 + 88 + 10 + 32 + 28 -- with the second 自白 waiting until the
            // body was fully there. 「我把A2 B2 的音檔往前移了 讓他在140S的時候播完」: that pair was
            // dragged back to 112-140s, and the formation was shortened to 30s and pulled back
            // to 109.75s so the body forms UNDER it and lands on its last word. Every event is
            // still in the beat; two of them now happen at the same time.
            //
            // WHAT IS STILL NOT THE SHEET'S READING is the body itself. v4 only grows HANDS here
            // -- 「玩家雙手完整顯現 → 進O-1」, with the full membrane body held back to S2-1 -- and the
            // decision taken is the other one: the player gets a whole body in O-0. It fits in
            // the sheet's 140s, so this duration no longer records that disagreement and
            // BuildO0Timeline is the only place it is written down. Shortening this row further
            // to buy time elsewhere would cut the second 自白 off, not just tighten a transition.
            //
            //   20s      the room is up, and each player's first 自白 starts
            //  +88s      the longer of the two first lines (A 88.0s, B 83.5s) plays out
            //  -> 109s   a breath later the 真人 start to go, over 10s
            //  -> 109.8s the body is cued, and AvatarFormation's 30s ramp runs under the rest:
            //            membrane first, the Ch36 body itself from 125s, after the room is empty
            //  -> 112s   each player's second 自白, 28s of it, ending at 140s
            //  = 140s, with the body arriving on its last word.
            //
            // THE NUMBER LIVES IN TWO PLACES AND MUST AGREE. BuildO0Timeline computes the beat's
            // real length from the wavs and logs it; this is a typed duration that has to be at
            // least as wide, because a Control clip drives its sub-director for exactly as long
            // as the clip is -- too narrow and O-0 is cut off mid-sentence with a half-formed
            // body, which looks like a bug in the body rather than a number in a table.
            ("Onboarding/Timeline", "O-0 進入", 140d, null, 0d),

            // O-1 IS THREE SUB-BEATS IN ONE ROW, and one gate at the end of all three. v4 writes
            // 鬼的抓取1 燈 / 鬼的抓取2 T-shirt / 封箱 as separate rows, but they are not separate
            // beats: 「播完後下一個物件才會出現」 means each one begins when the players finished the
            // last, which is a chain O1GrabWaves runs at runtime and not three clips at fixed
            // times. The sheet's 時長 for the three is what this row is wide enough for; where
            // inside it each object actually appears is up to the players.
            //
            // AND THE 停頓點 IS THIS ROW'S HOLD. v4 puts 「停頓點（兩者VO都需要聽完）」 between O-1 and
            // O-2 as a zero-length row; that is this gate, which O1DropSequence will not clear
            // while either player still has a box to fill or the beat is still speaking.
            //
            // IT HOLDS NINE SECONDS BEFORE THE END, not at it. The objects, the cartons and the
            // wall of boxes dissolve out on O-1's own timeline once that gate clears -- so the
            // tail after the hold is exactly that dissolve, and O-2 starts in an empty room. The
            // lead is O-1's own number; see BuildO1Timeline.
            ("Onboarding/Timeline O_1", "O-1 鬼的抓取", 170d, "beat.O1_Control",
             BuildO1Timeline.k_OutLead),

            // 鬼魂交流 AND 90s, BOTH FROM v4. This row used to read 鬼的抓取 at 120s, and the note
            // here used to argue the label was the odd one out because the gate is called
            // beat.O2_Grasp. v4 settles it the other way: every 抓取 is in O-1 now, and O-2 is
            // 鬼魂交流. The gate keeps its id -- it is a key, not a description, and renaming it
            // would strand the flag on both machines mid-show.
            ("Onboarding/Timeline O_2", "O-2 鬼魂交流", 90d, "beat.O2_Grasp", 0d),
            ("Beat_O3",    "O-3 Onboarding結束",   20d,  "beat.O3_Communion", 0d),

            ("Beat_S1-1",  "S1-1 記憶痕跡成形",     40d,  null, 0d),
            ("Beat_S1-2",  "S1-2 吊掛的家",        120d, null, 0d),

            ("Beat_S2-1",  "S2-1 門關上之後",       60d,  null, 0d),
            ("Beat_S2-2",  "S2-2 碎片：燈",        180d, null, 0d),
            ("Beat_S2-3",  "S2-3 碎片：杯子",       120d, null, 0d),
            ("Beat_S2-4",  "S2-4 隱藏的記憶",       180d, null, 0d),

            ("Beat_S3A-1", "S3A-1 把家道別",        40d,  null, 0d),
            ("Beat_S3A-2", "S3A-2 回到依附的人",     300d, null, 0d),
            ("Beat_S3A-3", "S3A-3 物件化膜",        90d,  null, 0d),
            ("Beat_S3A-4", "S3A-4 沒有說出口的話",   240d, null, 0d),

            ("Beat_S3B-1", "S3B-1 真人消失",        60d,  null, 0d),
            ("Beat_S3B-2", "S3B-2 一片片的記憶",     240d, null, 0d),
            ("Beat_S3B-3", "S3B-3 最後一次旋轉",     180d, null, 0d),
            ("Beat_S3B-4", "S3B-4 膜鬆開",          120d, null, 0d),
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
        ///
        /// ONLY THE TRACKS THIS BUILDER AUTHORS. It used to delete every root track, which meant
        /// running it threw away anything anyone had put on the master timeline by hand -- and
        /// people had: an activation track holding the onboarding room up for its whole 290
        /// seconds, another doing the same for S1, and a Fade track with the 黑場 that covers the
        /// Onboarding -> S1 handover. All three went on one run, silently, and the running order
        /// that came back looked perfectly correct.
        ///
        /// So the rule is now the obvious one: this tool owns the Control tracks and its own hold
        /// markers, and nothing else on the asset is its business.
        /// </summary>
        static void Clear(TimelineAsset timeline)
        {
            var kept = new List<string>();

            foreach (TrackAsset track in new List<TrackAsset>(timeline.GetRootTracks()))
            {
                if (track is ControlTrack)
                {
                    timeline.DeleteTrack(track);
                    continue;
                }

                if (track is MarkerTrack) continue;
                kept.Add($"{track.name} ({track.GetType().Name})");
            }

            if (kept.Count > 0)
                Debug.Log($"[MainTimeline] left {kept.Count} hand-authored track(s) alone: " +
                          $"{string.Join(", ", kept)}.");

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

            // Unscaled and not played on awake: the driver plays it and pulls it onto the shared
            // clock. It used to be Manual, which is exact and silent -- a graph that is only
            // evaluated never advances the audio clock, so no AudioTrack in the show could
            // sound. See the remarks on ShowTimelineDriver.
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
            director.extrapolationMode = DirectorWrapMode.Hold;

            if (holder.GetComponent<ShowTimelineDriver>() == null)
                holder.gameObject.AddComponent<ShowTimelineDriver>();

            return director;
        }

        /// <summary>
        /// Remove what earlier versions of this builder left in the scene. The S0-2 speaker used
        /// to live under the show driver, because the cue used to be a track on this timeline;
        /// it is in the onboarding prefab now, beside the beat that plays it. An orphan here
        /// would be a second, silent AudioSource that looks like it should be doing something.
        /// </summary>
        static void RemoveStaleObjects(PlayableDirector director)
        {
            Transform speaker = director.transform.Find("Cue Speaker S0-2");
            if (speaker == null) return;

            Object.DestroyImmediate(speaker.gameObject);
            Debug.Log("[MainTimeline] removed the old 'Cue Speaker S0-2'; O-2's timeline owns " +
                      "that cue and its speaker now.");
        }

        static bool BuildOrder(Scene scene, TimelineAsset timeline, PlayableDirector director)
        {
            RemoveStaleObjects(director);

            timeline.CreateMarkerTrack();
            MarkerTrack markers = timeline.markerTrack;

            double at = 0d;
            int nested = 0;
            int gates = 0;
            int created = 0;

            foreach ((string holderName, string label, double duration, string gate, double holdLead) in k_Order)
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

                // A PATH MEANS LIVE CONTENT, AND LIVE CONTENT IS NOT ACTIVATED BY ITS CLIP. The
                // clip would switch its target on for its duration and, on Revert, off again
                // afterwards -- which for O-0's holder means switching off the object carrying
                // the whole onboarding room sixty seconds in. An empty placeholder holder has
                // nothing to lose, so it keeps the old behaviour.
                control.active = !holderName.Contains("/");
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
                    double holdAt = at - holdLead;
                    var hold = markers.CreateMarker<ShowHoldMarker>(holdAt);
                    hold.SetTaskId(gate);
                    gates++;
                    Debug.Log($"[MainTimeline] {"hold",-16} {holdAt,5:F1}s        waits for '{gate}'" +
                              (holdLead > 0d ? $"   ({holdLead:F0}s of beat left after it)" : ""));
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

        /// <summary>
        /// A name, or a slash path when a name is not enough. "Timeline" appears twice in the
        /// onboarding prefab -- the live director and a disabled preview one -- so the entry that
        /// wants the live one has to say which; a search by name picks whichever comes first in
        /// the file, which is not a decision anybody made.
        /// </summary>
        static GameObject FindInScene(Scene scene, string nameOrPath)
        {
            if (!nameOrPath.Contains("/"))
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    GameObject found = FindIn(root.transform, nameOrPath);
                    if (found != null) return found;
                }
                return null;
            }

            // THE FIRST SEGMENT IS FOUND ANYWHERE, not only among the scene roots. The onboarding
            // prefab is instanced under Beats rather than at the top of the scene, so a path
            // anchored at the roots found nothing and the builder quietly made an empty holder
            // called "Onboarding/Content/Orbits" -- slash and all.
            string[] parts = nameOrPath.Split('/');

            GameObject head = FindInScene(scene, parts[0]);
            if (head == null) return null;

            Transform at = head.transform;
            for (int i = 1; i < parts.Length && at != null; i++) at = at.Find(parts[i]);
            return at != null ? at.gameObject : null;
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

            if (director.timeUpdateMode == DirectorUpdateMode.Manual)
            {
                Debug.LogError("[MainTimeline] RESULT: FAIL - the show director is Manual, so its " +
                               "graph never advances and every AudioTrack in the show is silent. " +
                               "The driver plays it and corrects it against the shared clock.");
                return;
            }
            Debug.Log($"[MainTimeline] OK   {director.timeUpdateMode} update mode (played, " +
                      "corrected against the shared clock)");

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

            // EVERY TRACK'S BINDING, not just the Control clips. A binding is a reference to a
            // scene object, and anything that destroys and recreates that object -- rebuilding
            // the footprint rings, say -- leaves the track bound to nothing. Nothing complains:
            // the activation track simply stops driving, so the thing it was meant to hide is
            // visible from the first frame of the show, which looks like a staging decision
            // rather than a broken reference.
            int bound = 0, missing = 0;
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is ControlTrack || track is MarkerTrack) continue;

                // A PLAYABLE TRACK HAS NOTHING TO BIND, and demanding one failed the build over
                // a track that was working perfectly. The Fade track carries ScreenFadeClip,
                // whose behaviour reaches ScreenFade.Instance in ProcessFrame rather than being
                // handed an object -- so it has no generic binding, has never had one, and the
                // check above was reading "unbound" as "broken" for a whole class of track that
                // is never bound.
                if (track is PlayableTrack) continue;

                if (director.GetGenericBinding(track) != null) bound++;
                else
                {
                    missing++;
                    Debug.LogError($"[MainTimeline] track '{track.name}' is bound to nothing. " +
                                   "Whatever it drove has been recreated since the timeline was " +
                                   "built; re-run this builder to bind it again.");
                }
            }

            if (missing > 0)
            {
                Debug.LogError($"[MainTimeline] RESULT: FAIL - {missing} of {bound + missing} " +
                               "track(s) drive nothing.");
                return;
            }
            Debug.Log($"[MainTimeline] OK   {bound} payoff track(s), all bound");

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
