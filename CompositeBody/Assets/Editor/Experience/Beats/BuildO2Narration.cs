using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// O-2's 自白, moved off the beat's own timeline and onto Main -- and the reason is that a
    /// voice nested under a Control clip gets shredded.
    ///
    /// WHY IT CANNOT STAY ON O_2.playable. Timeline re-seeks an audio clip on any out-of-band
    /// evaluation: <c>ScheduleRuntimeClip.EvaluateAt</c> sets <c>forceSeek</c> when
    /// <c>evaluationType == Evaluate</c> and calls <c>AudioClipPlayable.Seek</c>. A Control clip
    /// reaches that path every time the parent graph's speed changes, because
    /// <c>DirectorControlPlayable.PrepareFrame</c> sets <c>m_SyncTime</c> from
    /// <c>DetectDiscontinuity</c> -- the frame's advance against <c>deltaTime * effectiveSpeed</c>,
    /// tolerance <c>DiscreteTime.tickValue</c> -- and <c>ProcessFrame</c> then calls
    /// <c>playableGraph.Evaluate()</c> on the nested graph. One speed write, one re-seek, one
    /// click.
    ///
    /// And ShowTimelineDriver writes a speed: its drift trim pulls the director back onto the
    /// shared clock. Quantising that trim (k_TrimStep) did not end this -- it moved the chatter to
    /// the rounding boundaries, at drift 0.031, 0.094, 0.156 and 0.219s, where a mean drift
    /// sitting on a boundary flips the step every frame because m_Drift carries a frame of Link's
    /// alternating frame time. Frame-rate re-seeks on a 90-second line is the crackle the venue
    /// heard, and it is audible on a CLIENT only: on one machine the target and the director
    /// advance by the same delta and the trim never fires.
    ///
    /// A line on Main is immune. The master's own clips are evaluated as Playback, so a speed
    /// change is a 0.5% rate change on the voice and nothing else -- inaudible, and no seek. This
    /// is the reason BuildS2Narration and BuildS3Narration already put S1's, S2's and S3's lines
    /// here, and O-2 was the one left behind. O_0.playable still has three audio tracks and the
    /// same exposure; this tool deliberately does not touch them.
    ///
    /// WHAT IS GIVEN UP. The line no longer scrubs with the beat in isolation, and -- the one that
    /// matters -- a hold authored INSIDE O-2 would no longer pause it, because a hold pauses the
    /// master and <c>EvaluateAt</c> only disables a clip on <c>frameData.timeHeld</c>. There is no
    /// gate inside 310-400s today (the onboarding's one is at 301s, and the next are at 772.5s
    /// and 986.5s, both outside a line). Author a gate in the middle of O-2 and this trade comes
    /// due: the voice would talk into the wait, which is what AmbientBed's remarks call the one
    /// thing the onboarding must not say.
    ///
    /// THE SOURCES SIT UNDER Beat_O2, NOT INSIDE THE ONBOARDING PREFAB. The pair this replaces --
    /// Onboarding/Content/Narration O-2 A and B -- live inside Content, and Content is bound to
    /// three of Main's activation tracks: it is switched off for most of the show, and Timeline
    /// schedules its clips against the DSP clock when the graph is built, so a deck that is
    /// disabled at that moment is silence with no symptom. Beat_O2 is active and is bound to no
    /// activation track, which is exactly where Beat_S1/S1 VO, Beat_S2/S2 VO and Beat_S3/S3 VO
    /// sit.
    ///
    /// IT DOES NOT SAVE, following the other two narration builders: MainScene usually has
    /// someone's authoring in it and neither discarding that nor writing it to disk is this
    /// tool's call. Nothing is on disk until you save the scene, which writes both timelines
    /// with it.
    ///
    /// Tools > O-2 > Build O-2 Narration
    /// </summary>
    public static class BuildO2Narration
    {
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";
        const string k_Timeline = "Assets/_Timeline/Main.playable";

        /// <summary>The beat's own timeline, which the two tracks are taken OFF.</summary>
        const string k_BeatTimeline = "Assets/_Timeline/O_2.playable";

        const string k_BeatRoot = "Experience/Beats/Beat_O2";
        const string k_VORoot = "O-2 VO";

        /// <summary>
        /// 自白 rather than 旁白, keeping the beat's own word: the onboarding's lines are the
        /// player's own account of the room, and the sections' are narration over it.
        /// </summary>
        const string k_TrackA = "O-2 自白 A (Player1)";

        const string k_TrackB = "O-2 自白 B (Player2)";
        const string k_SpeakerA = "O-2 VO A";
        const string k_SpeakerB = "O-2 VO B";

        /// <summary>The label this builder owns, so a re-run replaces its own clip and no other.</summary>
        const string k_Label = "O-2 自白";

        /// <summary>
        /// 「一邊播放 vo[你有沒有發現]」. These two are the files the beat plays today, carried over
        /// unchanged -- the move is about where the clip sits in the graph, not about the take.
        ///
        /// THEY ARE BYTE-IDENTICAL (same md5, 90.000s, 24-bit 48kHz, peak -4.47 dBFS, no clipped
        /// samples), so 玩家A and 玩家B hear the same recording here. That is the state S3 was in
        /// before its own two takes landed; the guard at the end of Run says so on every run.
        /// </summary>
        const string k_LineA = "Assets/_Audio/OA6_你有沒有發現.wav";

        const string k_LineB = "Assets/_Audio/OB6_你有沒有發現.wav";

        /// <summary>
        /// Seconds into the beat that the line starts. Zero, which is what the nested tracks were
        /// authored at and therefore what the room sounds like today. BuildO2Timeline's old
        /// single-deck cue was at 1.5s; moving the line is not the moment to re-cut it.
        /// </summary>
        const double k_LineAt = 0d;

        /// <summary>A player's deck: the track, the source under O-2 VO, and who may hear it.</summary>
        struct Deck
        {
            public string trackName;
            public string speakerName;
            public string line;
            public PlayerRole role;
            public string label;

            public AudioTrack track;
            public AudioSource source;
        }

        [MenuItem("Tools/O-2/Build O-2 Narration")]
        public static void Run()
        {
            Debug.Log("[O2VO] Starting...");

            // REUSED, NOT REOPENED. Opening MainScene Single here would discard whatever is in
            // front of whoever ran this.
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != k_MainScene)
            {
                Debug.LogError($"[O2VO] RESULT: FAIL - {k_MainScene} is not the open scene " +
                               $"('{scene.path}' is). Open it and run this again.");
                return;
            }

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (timeline == null)
            {
                Debug.LogError($"[O2VO] RESULT: FAIL - no timeline at {k_Timeline}.");
                return;
            }

            PlayableDirector show = FindShowDirector(timeline);
            if (show == null)
            {
                Debug.LogError("[O2VO] RESULT: FAIL - nothing in MainScene plays " +
                               $"{k_Timeline}, so there is no director to bind a track on.");
                return;
            }

            if (!BeatSpan(timeline, out double from, out double to))
            {
                Debug.LogError("[O2VO] RESULT: FAIL - no 'O-2' control track on Main, so where " +
                               "the beat starts and ends is not knowable from the running order.");
                return;
            }

            Transform voRoot = EnsureVORoot();
            if (voRoot == null) return;

            var decks = new[]
            {
                new Deck
                {
                    trackName = k_TrackA, speakerName = k_SpeakerA, line = k_LineA,
                    role = PlayerRole.Player1, label = "A",
                },
                new Deck
                {
                    trackName = k_TrackB, speakerName = k_SpeakerB, line = k_LineB,
                    role = PlayerRole.Player2, label = "B",
                },
            };

            for (int i = 0; i < decks.Length; i++)
            {
                decks[i].track = EnsureTrack(timeline, decks[i].trackName);
                if (decks[i].track == null) return;

                decks[i].source = EnsureSpeaker(voRoot, decks[i].speakerName);
                EnsureRoleAudible(decks[i].source, decks[i].role, decks[i].label);

                show.SetGenericBinding(decks[i].track, decks[i].source);

                if (!Author(timeline, decks[i], from, to)) return;
            }

            if (!Strip()) return;

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(show);
            EditorSceneManager.MarkSceneDirty(scene);

            if (!Verify(show, decks)) return;

            if (k_LineA == k_LineB || SameBytes(k_LineA, k_LineB))
            {
                Debug.LogWarning(
                    "[O2VO] ONE RECORDING ON BOTH DECKS: 玩家A and 玩家B hear the same audio in " +
                    "O-2. The decks and their role gates are right either way -- only k_LineA " +
                    "and k_LineB say who hears what -- so this is a placeholder to replace when " +
                    "the second take lands, not a wiring fault.");
            }

            Debug.Log("[O2VO] RESULT: PASS - and nothing is on disk yet: save MainScene " +
                      "(File > Save) and both timeline assets go with it.");
        }

        /// <summary>
        /// One clip per deck, across the beat, replacing only this builder's own.
        ///
        /// Replaced by label rather than by clearing the track, so a per-beat line added here by
        /// hand later is not taken out by a re-run.
        /// </summary>
        static bool Author(TimelineAsset timeline, Deck deck, double from, double to)
        {
            var audio = AssetDatabase.LoadAssetAtPath<AudioClip>(deck.line);
            if (audio == null)
            {
                Debug.LogError($"[O2VO] RESULT: FAIL - no AudioClip at {deck.line}.");
                return false;
            }

            int removed = 0;
            foreach (TimelineClip old in new List<TimelineClip>(deck.track.GetClips()))
            {
                if (old.displayName != k_Label) continue;
                timeline.DeleteClip(old);
                removed++;
            }

            double span = to - from - k_LineAt;

            TimelineClip clip = deck.track.CreateClip<AudioPlayableAsset>();
            ((AudioPlayableAsset)clip.asset).clip = audio;
            clip.start = from + k_LineAt;
            clip.clipIn = 0d;

            // THE BEAT'S LENGTH, NOT THE FILE'S. A clip longer than the beat would keep talking
            // through the blackout that ends the onboarding and into S1-1.
            clip.duration = span;
            clip.displayName = k_Label;

            string note = audio.length > span + 1e-3
                ? $" -- TRIMMED: the file is {audio.length:F1}s, so its last " +
                  $"{audio.length - span:F1}s never play"
                : audio.length < span - 1e-3
                    ? $" -- SHORT: the file is only {audio.length:F1}s, so the last " +
                      $"{span - audio.length:F1}s of the beat are silent on this deck"
                    : "";

            Debug.Log($"[O2VO] {deck.track.name}: {k_Label} ({audio.name}) " +
                      $"{clip.start:F1}-{clip.end:F1}s ({span:F1}s){note}" +
                      $"{(removed > 0 ? $", replacing {removed} earlier copy" : "")}.");
            return true;
        }

        /// <summary>
        /// Takes every AudioTrack off the beat's own timeline, which is the half of this tool that
        /// actually fixes the crackle: a copy left there would both play twice and keep being
        /// re-seeked.
        ///
        /// Reported rather than done quietly -- what comes off is the line the venue has been
        /// hearing, and the log is how you check that what went onto Main is the same clip at the
        /// same moment.
        /// </summary>
        static bool Strip()
        {
            var beat = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_BeatTimeline);
            if (beat == null)
            {
                Debug.LogError($"[O2VO] RESULT: FAIL - no timeline at {k_BeatTimeline}; the " +
                               "nested copy of the line cannot be checked, let alone removed.");
                return false;
            }

            var doomed = new List<AudioTrack>();
            foreach (TrackAsset track in beat.GetOutputTracks())
                if (track is AudioTrack audio) doomed.Add(audio);

            if (doomed.Count == 0)
            {
                Debug.Log($"[O2VO] {System.IO.Path.GetFileName(k_BeatTimeline)} already has no " +
                          "audio track; nothing to take off.");
                return true;
            }

            foreach (AudioTrack track in doomed)
            {
                foreach (TimelineClip clip in track.GetClips())
                {
                    var asset = clip.asset as AudioPlayableAsset;
                    AudioClip had = asset != null ? asset.clip : null;
                    Debug.Log($"[O2VO] taking '{track.name}' off " +
                              $"{System.IO.Path.GetFileName(k_BeatTimeline)}: " +
                              $"{(had != null ? had.name : "no clip")} at beat-local " +
                              $"{clip.start:F1}-{clip.end:F1}s.");
                }

                beat.DeleteTrack(track);
            }

            EditorUtility.SetDirty(beat);
            Debug.Log($"[O2VO] {doomed.Count} audio track(s) removed from " +
                      $"{System.IO.Path.GetFileName(k_BeatTimeline)}. The beat keeps its orbits, " +
                      "footprints, scans and boxes; only the voice moved.");
            return true;
        }

        /// <summary>
        /// Where O-2 begins and ends, read off the running order rather than typed here -- the
        /// beat has been re-cut once already (120s to 90s) and the clip has to follow it.
        /// </summary>
        static bool BeatSpan(TimelineAsset timeline, out double from, out double to)
        {
            from = double.MaxValue;
            to = 0d;

            foreach (TrackAsset t in timeline.GetOutputTracks())
            {
                if (t is not ControlTrack || !t.name.Trim().StartsWith("O-2")) continue;

                foreach (TimelineClip c in t.GetClips())
                {
                    if (c.start < from) from = c.start;
                    if (c.end > to) to = c.end;
                }
            }

            return from < double.MaxValue && to > from;
        }

        static Transform EnsureVORoot()
        {
            GameObject beat = Find(k_BeatRoot);
            if (beat == null)
            {
                Debug.LogError($"[O2VO] RESULT: FAIL - no '{k_BeatRoot}' in MainScene to hang " +
                               $"'{k_VORoot}' off.");
                return null;
            }

            Transform root = beat.transform.Find(k_VORoot);
            if (root == null)
            {
                var made = new GameObject(k_VORoot);
                made.transform.SetParent(beat.transform, false);
                root = made.transform;
                Debug.Log($"[O2VO] created '{k_BeatRoot}/{k_VORoot}'.");
            }

            // An inactive VO root is silence with no symptom, and the whole point of this parent
            // is that nothing switches it.
            if (!root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(true);
                Debug.Log($"[O2VO] switched '{k_VORoot}' back on; Timeline cannot sound through " +
                          "a disabled AudioSource.");
            }

            if (!root.gameObject.activeInHierarchy)
            {
                Debug.LogError($"[O2VO] RESULT: FAIL - '{k_VORoot}' is inside something that is " +
                               "switched off, so both decks would be silent. Check that " +
                               $"'{k_BeatRoot}' and its parents are active.");
                return null;
            }

            return root;
        }

        /// <summary>
        /// Found by name and reused, so the clips are replaced without the track object changing:
        /// bindings key off the track object, so deleting and recreating one would break them.
        /// </summary>
        static AudioTrack EnsureTrack(TimelineAsset timeline, string name)
        {
            foreach (TrackAsset existing in timeline.GetOutputTracks())
            {
                if (existing is AudioTrack audio && audio.name == name) return audio;
            }

            var track = timeline.CreateTrack<AudioTrack>(null, name);
            if (track == null)
                Debug.LogError($"[O2VO] RESULT: FAIL - could not create the track '{name}'.");
            else
                Debug.Log($"[O2VO] created the track '{name}'.");

            return track;
        }

        /// <summary>
        /// A 2D source of its own per player, like S1's, S2's and S3's. RoleAudible mutes a whole
        /// AudioSource, so one shared between the two decks would silence one player's line to
        /// hide the other's.
        /// </summary>
        static AudioSource EnsureSpeaker(Transform root, string name)
        {
            Transform holder = root.Find(name);
            if (holder == null)
            {
                var made = new GameObject(name);
                made.transform.SetParent(root, false);
                holder = made.transform;
                Debug.Log($"[O2VO] created '{name}'.");
            }

            holder.gameObject.SetActive(true);

            var source = holder.GetComponent<AudioSource>();
            if (source == null) source = holder.gameObject.AddComponent<AudioSource>();

            // playOnAwake would start whatever clip the inspector happens to be holding at show
            // start, five minutes before this deck is due.
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            EditorUtility.SetDirty(source);
            return source;
        }

        /// <summary>
        /// The role gate. Written through SerializedObject because the field is private and
        /// serialized, and only the serialization system makes the change survive a save.
        /// </summary>
        static void EnsureRoleAudible(AudioSource source, PlayerRole role, string label)
        {
            var presenter = source.GetComponent<RoleAudible>();
            if (presenter == null)
            {
                presenter = source.gameObject.AddComponent<RoleAudible>();
                Debug.Log($"[O2VO] added RoleAudible to '{source.name}'.");
            }

            var so = new SerializedObject(presenter);
            SerializedProperty roles = so.FindProperty("m_AudibleToRoles");
            roles.arraySize = 1;
            roles.GetArrayElementAtIndex(0).intValue = (int)role;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[O2VO] 自白 {label} -> '{source.name}', audible to {role} only.");
        }

        /// <summary>
        /// The two decks are bound, private to DIFFERENT players, and nothing audio is left on the
        /// beat's own timeline.
        ///
        /// Checked rather than trusted: both decks private to the same player is the failure that
        /// sounds like success on one headset, and a track left behind on O_2 is the failure this
        /// whole tool exists to remove.
        /// </summary>
        static bool Verify(PlayableDirector show, Deck[] decks)
        {
            var seen = new List<PlayerRole>();

            foreach (Deck deck in decks)
            {
                if (show.GetGenericBinding(deck.track) is not AudioSource bound)
                {
                    Debug.LogError($"[O2VO] RESULT: FAIL - '{deck.trackName}' is bound to no " +
                                   "AudioSource, so it makes no sound at all.");
                    return false;
                }

                if (!bound.gameObject.activeInHierarchy)
                {
                    Debug.LogError($"[O2VO] RESULT: FAIL - '{bound.name}' is switched off, so " +
                                   "Timeline has nothing to sound through.");
                    return false;
                }

                var presenter = bound.GetComponent<RoleAudible>();
                if (presenter == null)
                {
                    Debug.LogError($"[O2VO] RESULT: FAIL - '{bound.name}' has no RoleAudible, so " +
                                   "both players would hear this private deck.");
                    return false;
                }

                var so = new SerializedObject(presenter);
                SerializedProperty roles = so.FindProperty("m_AudibleToRoles");
                if (roles.arraySize != 1)
                {
                    Debug.LogError($"[O2VO] RESULT: FAIL - '{bound.name}' is audible to " +
                                   $"{roles.arraySize} roles; a private deck wants exactly one.");
                    return false;
                }

                var role = (PlayerRole)roles.GetArrayElementAtIndex(0).intValue;
                if (seen.Contains(role))
                {
                    Debug.LogError($"[O2VO] RESULT: FAIL - two decks are both audible to {role} " +
                                   "only, so the other player hears nothing for the whole beat.");
                    return false;
                }

                seen.Add(role);
            }

            var beat = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_BeatTimeline);
            if (beat != null)
            {
                foreach (TrackAsset t in beat.GetOutputTracks())
                {
                    if (t is not AudioTrack) continue;
                    Debug.LogError($"[O2VO] RESULT: FAIL - '{t.name}' is still on " +
                                   $"{System.IO.Path.GetFileName(k_BeatTimeline)}, so the line " +
                                   "plays twice and the nested copy still crackles.");
                    return false;
                }
            }

            Debug.Log($"[O2VO] verified: {decks.Length} decks on Main, one each for " +
                      string.Join(" and ", seen) + ", and no audio left on the beat.");
            return true;
        }

        /// <summary>Whether two assets are the same recording, so the placeholder warning is honest.</summary>
        static bool SameBytes(string a, string b)
        {
            try
            {
                byte[] left = System.IO.File.ReadAllBytes(a);
                byte[] right = System.IO.File.ReadAllBytes(b);
                if (left.Length != right.Length) return false;
                for (int i = 0; i < left.Length; i++)
                    if (left[i] != right[i]) return false;
                return true;
            }
            catch (System.IO.IOException)
            {
                return false;
            }
        }

        static PlayableDirector FindShowDirector(TimelineAsset timeline)
        {
            foreach (PlayableDirector d in Object.FindObjectsByType<PlayableDirector>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (d.playableAsset == timeline) return d;
            }

            return null;
        }

        /// <summary>GameObject.Find by path, through inactive objects too.</summary>
        static GameObject Find(string path)
        {
            string[] parts = path.Split('/');

            Transform cursor = null;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != parts[0]) continue;
                cursor = root.transform;
                break;
            }

            for (int i = 1; i < parts.Length && cursor != null; i++) cursor = cursor.Find(parts[i]);
            return cursor != null ? cursor.gameObject : null;
        }
    }
}
