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
    /// S2's 旁白, one private deck per player: 「玩家a 跟玩家b會各自聽到不同的vo」.
    ///
    /// S2-1 門關上之後 is the first line on those decks. The two players hear different
    /// recordings of it -- and A's recording does not exist yet, so BOTH decks are pointed at
    /// S2_1_B for now. That is deliberate and it is reported: the run ends with a warning naming
    /// the file both players are hearing, because a placeholder nobody notices is a placeholder
    /// that ships.
    ///
    /// WHY THE TRACKS GO ON Main AND NOT ON S2_1.playable. The S1 block already does it this way
    /// -- 'S1 旁白 共同 (SP)' / 'S1 旁白 A (Player1)' / 'S1 旁白 B (Player2)' are tracks on the
    /// master, bound to three sources under Beat_S1/S1 VO -- and the running order is the one
    /// place where a line's position is readable against every other beat. Nesting the narration
    /// in each beat's own .playable would put S2's lines in four different assets, timed against
    /// four different zeroes, which is how a line ends up a beat behind and nobody can see it.
    /// BuildMainTimeline is safe to re-run over this: it owns the Control tracks and its own
    /// hold markers and explicitly leaves every hand-authored track alone.
    ///
    /// ONE ROW PER PLAYER FOR THE WHOLE BLOCK, not one row per beat. S2's later lines
    /// (S2_2_A / S2_2_B, S2-5-A / S2-5-B) belong as further clips on these same two tracks, so a
    /// player's whole account of S2 is one row you can read and drag -- the same argument
    /// BuildO0Timeline makes for O-0's two 自白. It is also what keeps the role decision down to
    /// one AudioSource per player: a second track for the same player wants a second source, a
    /// second RoleAudible, and a second place to get the role wrong. Add to <see cref="k_Lines"/>.
    ///
    /// AND A THIRD ROW FOR THE LINES BOTH PLAYERS HEAR. The -AB stems -- S2-2-AB, S2-3-AB,
    /// S2-6-AB -- are one recording for the room, not a private line that happens to be the same
    /// on both decks. That distinction is the whole reason they do not go on the two private
    /// rows: a line the room hears would then exist as two clips to keep in step every time the
    /// cue moves, each one standing behind a role gate that can only silence it. 'S2 旁白 共同
    /// (SP)' is the same third row S1 already has -- 'S1 旁白 共同 (SP)' bound to Beat_S1/S1 VO/
    /// S1 VO SP, one source and no RoleAudible on it -- and it is the row you read when you want
    /// to know what both people in the room are being told. Add to <see cref="k_SharedLines"/>.
    ///
    /// AND THE SOURCES SIT BESIDE Content, NOT INSIDE IT. Main's third ActivationTrack drives
    /// Beat_S2/Content for the whole S2 block; an AudioSource under there is disabled when the
    /// show's graph is built and Timeline has nothing to sound through. 'S2 VO' is a sibling of
    /// Content and stays up, exactly like Beat_S1/S1 VO. Which player hears which deck is then a
    /// purely local decision that <see cref="RoleAudible"/> makes by muting -- see its remarks
    /// for why muting and not stopping, and why this costs no network traffic at all.
    ///
    /// Re-runnable and additive: it finds its tracks and sources by name, replaces only the
    /// clips it authored itself, and leaves everything else in the scene and the asset alone.
    ///
    /// IT DOES NOT SAVE. Every sibling Build* tool opens MainScene over whatever you had open
    /// and saves on the way out; this one reuses MainScene when it is already the active scene
    /// and leaves it dirty, because that scene usually has someone's unsaved authoring in it and
    /// neither discarding that nor writing it to disk is this tool's call. Nothing is on disk
    /// until you save the scene, which writes the timeline asset with it.
    ///
    /// Tools > Show > Build S2 Narration
    /// </summary>
    public static class BuildS2Narration
    {
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";
        const string k_Timeline = "Assets/_Timeline/Main.playable";

        const string k_S2Root = "Beat_S2";
        const string k_VORoot = "S2 VO";

        const string k_TrackA = "S2 旁白 A (Player1)";
        const string k_TrackB = "S2 旁白 B (Player2)";
        const string k_TrackSP = "S2 旁白 共同 (SP)";
        const string k_SpeakerA = "S2 VO A";
        const string k_SpeakerB = "S2 VO B";
        const string k_SpeakerSP = "S2 VO SP";

        /// <summary>
        /// 玩家A 的音檔還沒有，所以兩個人都先用 S2_1_B。Change this one line when A's own
        /// recording lands and re-run; nothing else in here knows the difference.
        /// </summary>
        const string k_S21A = "Assets/_Audio/S2_1_B.wav";

        const string k_S21B = "Assets/_Audio/S2_1_B.wav";

        /// <summary>
        /// S2-3｜隱藏的記憶's line, 191s of it. One recording for the room, so it rides the 共同
        /// row and not the two private decks -- see the class remarks.
        /// </summary>
        const string k_S23AB = "Assets/_Audio/S2-3-AB.wav";

        /// <summary>
        /// Seconds after the beat starts. NOT a taste decision: Main's ActivationTrack brings
        /// Beat_S2/Content up on the beat's own first frame, and a line that starts on that frame
        /// starts while the block is still arriving. A second later it also lands as the
        /// 黑場 S1 → S2 (762-772s) comes back up, so the picture returns on the first word.
        ///
        /// It is one number and the clip is draggable; move either if the room says otherwise.
        /// </summary>
        const double k_Lead = 1.0d;

        /// <summary>
        /// One row per line: which beat it belongs to, what the clips are called, and what each
        /// player hears. The beat is NAMED rather than timed -- see <see cref="BeatSlot"/>.
        /// </summary>
        static readonly (string beat, string label, string lineA, string lineB)[] k_Lines =
        {
            ("S2-1", "S2-1", k_S21A, k_S21B),
        };

        /// <summary>
        /// The lines both players hear, each cued at an ABSOLUTE time on the running order rather
        /// than at an offset from its beat. That is the difference between this table and
        /// <see cref="k_Lines"/>, and it is not a style choice: 共同 lines are called by the clock
        /// everyone in the building is reading -- 14:13 -- while a private deck's lines are
        /// described relative to the block they sit in.
        ///
        /// 14:13 is also exactly where S2-3's own row begins, so this line opens the beat instead
        /// of following <see cref="k_Lead"/> behind it. <see cref="AuthorSharedLines"/> checks
        /// that against the running order on every run and says so, because the two numbers
        /// agreeing today is not the same as them being the same number.
        /// </summary>
        static readonly (string beat, string label, string line, double at)[] k_SharedLines =
        {
            ("S2-3", "S2-3", k_S23AB, 14 * 60 + 13),
        };

        sealed class Deck
        {
            /// <summary>Null on the 共同 row: no role, no gate, both headsets.</summary>
            public PlayerRole? role;
            public string label, trackName, speaker;
            public AudioTrack track;
            public AudioSource source;
        }

        [MenuItem("Tools/Show/Build S2 Narration")]
        static void RunFromMenu() => Run();

        public static void Run()
        {
            Scene scene = OpenOrReuse();
            if (!scene.IsValid()) return;

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (timeline == null)
            {
                Debug.LogError($"[S2VO] RESULT: FAIL - no timeline at {k_Timeline}.");
                return;
            }

            PlayableDirector show = FindShowDirector(timeline);
            if (show == null)
            {
                Debug.LogError("[S2VO] RESULT: FAIL - nothing in MainScene is playing " +
                               $"{k_Timeline}, so there is no director to bind the tracks on. " +
                               "Run BuildMainTimeline first.");
                return;
            }

            Transform voRoot = EnsureVORoot(scene);
            if (voRoot == null) return;

            var decks = new[]
            {
                new Deck { role = PlayerRole.Player1, label = "A", trackName = k_TrackA, speaker = k_SpeakerA },
                new Deck { role = PlayerRole.Player2, label = "B", trackName = k_TrackB, speaker = k_SpeakerB },
                new Deck { role = null, label = "AB", trackName = k_TrackSP, speaker = k_SpeakerSP },
            };

            foreach (Deck deck in decks)
            {
                deck.track = EnsureTrack(timeline, deck.trackName);
                if (deck.track == null) return;

                deck.source = EnsureSpeaker(voRoot, deck.speaker);

                if (deck.role.HasValue)
                    EnsureRoleAudible(deck.source, deck.role.Value, deck.label);
                else if (!EnsureUngated(deck.source))
                    return;

                show.SetGenericBinding(deck.track, deck.source);
            }

            if (!AuthorLines(timeline, decks)) return;
            if (!AuthorSharedLines(timeline, System.Array.Find(decks, d => !d.role.HasValue)))
                return;

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(show);
            EditorSceneManager.MarkSceneDirty(scene);

            if (!Verify(show, decks)) return;

            Debug.Log("[S2VO] RESULT: PASS - and nothing is on disk yet: save MainScene " +
                      "(File > Save) and the timeline asset goes with it.");
        }

        /// <summary>
        /// MainScene, without throwing away what is open. Reopening it Single is what every
        /// sibling tool does and it discards the active scene's unsaved changes without asking;
        /// when the scene we want is already the one open there is no reason to.
        /// </summary>
        static Scene OpenOrReuse()
        {
            Scene active = EditorSceneManager.GetActiveScene();
            if (active.IsValid() && active.path == k_MainScene)
            {
                Debug.Log($"[S2VO] using the open {k_MainScene}" +
                          (active.isDirty ? " (dirty - its unsaved changes are kept)." : "."));
                return active;
            }

            Scene opened = EditorSceneManager.OpenScene(k_MainScene, OpenSceneMode.Single);
            if (!opened.IsValid())
                Debug.LogError($"[S2VO] RESULT: FAIL - could not open {k_MainScene}.");
            return opened;
        }

        static PlayableDirector FindShowDirector(TimelineAsset timeline)
        {
            foreach (PlayableDirector candidate in
                     Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include))
            {
                if (candidate.playableAsset == timeline) return candidate;
            }
            return null;
        }

        /// <summary>
        /// Beat_S2/S2 VO -- beside Content, never under it. See the class remarks.
        /// </summary>
        static Transform EnsureVORoot(Scene scene)
        {
            GameObject s2 = Find(scene, k_S2Root);
            if (s2 == null)
            {
                Debug.LogError($"[S2VO] RESULT: FAIL - no '{k_S2Root}' in MainScene to hang " +
                               $"'{k_VORoot}' off. The S2 block's own root is where the " +
                               "narration belongs; making one here would hide a bigger problem.");
                return null;
            }

            Transform root = s2.transform.Find(k_VORoot);
            if (root == null)
            {
                var made = new GameObject(k_VORoot);
                made.transform.SetParent(s2.transform, false);
                root = made.transform;
                Debug.Log($"[S2VO] created '{k_S2Root}/{k_VORoot}'.");
            }

            // An inactive VO root is silence with no symptom: the tracks are there, the clips are
            // there, and nothing comes out of either headset.
            if (!root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(true);
                Debug.Log($"[S2VO] switched '{k_VORoot}' back on; Timeline cannot sound through " +
                          "a disabled AudioSource.");
            }

            return root;
        }

        /// <summary>
        /// Found by name and reused, so the clips are replaced without the track object changing.
        /// Bindings key off the track object rather than its name: renaming a track in the
        /// Timeline window cannot break them, but deleting and recreating one would.
        /// </summary>
        static AudioTrack EnsureTrack(TimelineAsset timeline, string name)
        {
            foreach (TrackAsset existing in timeline.GetOutputTracks())
            {
                if (existing is AudioTrack audio && audio.name == name) return audio;
            }

            var track = timeline.CreateTrack<AudioTrack>(null, name);
            if (track == null)
                Debug.LogError($"[S2VO] RESULT: FAIL - could not create the track '{name}'.");
            else
                Debug.Log($"[S2VO] created the track '{name}'.");

            return track;
        }

        /// <summary>
        /// A 2D source of its own per player, like S1's and like O-0's. RoleAudible mutes a whole
        /// AudioSource, so one shared between the two decks would silence one player's line to
        /// hide the other's. 2D because this is the player's own account and not a voice standing
        /// somewhere in the room -- positioned, it would arrive from a different direction for
        /// each of the two people in it.
        /// </summary>
        static AudioSource EnsureSpeaker(Transform root, string name)
        {
            Transform holder = root.Find(name);
            if (holder == null)
            {
                var made = new GameObject(name);
                made.transform.SetParent(root, false);
                holder = made.transform;
                Debug.Log($"[S2VO] created '{name}'.");
            }

            holder.gameObject.SetActive(true);

            var source = holder.GetComponent<AudioSource>();
            if (source == null) source = holder.gameObject.AddComponent<AudioSource>();

            // playOnAwake would start whatever clip the inspector happens to be holding at show
            // start, 767 seconds before this deck is due.
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
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
                Debug.Log($"[S2VO] added RoleAudible to '{source.name}'.");
            }

            var so = new SerializedObject(presenter);
            SerializedProperty roles = so.FindProperty("m_AudibleToRoles");
            roles.arraySize = 1;
            roles.GetArrayElementAtIndex(0).intValue = (int)role;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[S2VO] 旁白 {label} -> '{source.name}', audible to {role} only.");
        }

        static bool AuthorLines(TimelineAsset timeline, Deck[] decks)
        {
            foreach ((string beat, string label, string lineA, string lineB) in k_Lines)
            {
                if (!BeatSlot(timeline, beat, out double start, out double end)) return false;

                double at = start + k_Lead;

                foreach (Deck deck in decks)
                {
                    if (!deck.role.HasValue) continue;

                    string clipLabel = $"{label}-{deck.label}";
                    string path = deck.role == PlayerRole.Player1 ? lineA : lineB;

                    var audio = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (audio == null)
                    {
                        Debug.LogError($"[S2VO] RESULT: FAIL - no AudioClip at {path}, so " +
                                       $"{clipLabel} has nothing to play.");
                        return false;
                    }

                    Replace(timeline, deck.track, clipLabel, audio, at);

                    // The running order gives the beat a width and the line has to live inside
                    // it. Overrunning does not truncate the line -- an AudioTrack on the master
                    // is not bounded by the Control clip beside it -- it carries the line into
                    // the next beat, over whatever that beat is saying.
                    double over = at + audio.length - end;
                    if (over > 0d)
                    {
                        Debug.LogWarning($"[S2VO] {clipLabel} runs {over:F1}s past the end of " +
                                         $"{beat} ({end:F1}s) and into the next beat. Either " +
                                         "that row is too narrow or the lead is too long.");
                    }
                }

                if (lineA == lineB)
                {
                    Debug.LogWarning($"[S2VO] {label}: BOTH PLAYERS HEAR " +
                                     $"{System.IO.Path.GetFileName(lineA)}. 玩家A 的音檔還沒有，" +
                                     "所以先用同一個檔案 -- the two decks are wired and private " +
                                     "already, and pointing k_S21A at A's own recording is the " +
                                     "whole of what is left to do.");
                }
            }

            return true;
        }

        /// <summary>
        /// The lines for the room, cued by the clock. One clip per entry on the single 共同 row,
        /// replacing any earlier copy of itself exactly as the private decks do.
        ///
        /// It reads the beat's slot off the running order even though it does not take its timing
        /// from it, and reports three ways the cue and the running order can disagree: a line that
        /// starts before its beat (it plays over the beat before, whose picture is still up), one
        /// that runs past the end of it (an AudioTrack on the master is not bounded by the Control
        /// clip beside it, so the line carries into the next beat and over whatever that one is
        /// saying), and one that lands on the beat's very first frame. The last of those is not a
        /// fault -- it is what 14:13 asks for and the line opens the beat -- but it is the one
        /// number in here that a moved row would silently change the meaning of, so it is said
        /// out loud rather than assumed.
        /// </summary>
        static bool AuthorSharedLines(TimelineAsset timeline, Deck deck)
        {
            if (deck == null)
            {
                Debug.LogError("[S2VO] RESULT: FAIL - no 共同 deck, so the -AB lines have no row " +
                               "to sit on.");
                return false;
            }

            foreach ((string beat, string label, string line, double at) in k_SharedLines)
            {
                if (!BeatSlot(timeline, beat, out double start, out double end)) return false;

                string clipLabel = $"{label}-{deck.label}";

                var audio = AssetDatabase.LoadAssetAtPath<AudioClip>(line);
                if (audio == null)
                {
                    Debug.LogError($"[S2VO] RESULT: FAIL - no AudioClip at {line}, so " +
                                   $"{clipLabel} has nothing to play.");
                    return false;
                }

                Replace(timeline, deck.track, clipLabel, audio, at);
                Debug.Log($"[S2VO] {clipLabel} is cued at {Clock(at)} ({at:F0}s), " +
                          $"{audio.length:F1}s long, out of both headsets.");

                if (at < start)
                {
                    Debug.LogWarning($"[S2VO] {clipLabel} starts {start - at:F1}s BEFORE {beat} " +
                                     $"does ({Clock(start)}), so it speaks over the beat in " +
                                     "front of it. Either the cue or that row is wrong.");
                }
                else if (System.Math.Abs(at - start) < 0.5d)
                {
                    Debug.Log($"[S2VO] {clipLabel} lands on {beat}'s first frame " +
                              $"({Clock(start)}) rather than {k_Lead:F1}s behind it, which is " +
                              "what the running order asks for: the line opens the beat. Move " +
                              $"{beat} and this cue stays at {Clock(at)}.");
                }

                double over = at + audio.length - end;
                if (over > 0d)
                {
                    Debug.LogWarning($"[S2VO] {clipLabel} runs {over:F1}s past the end of " +
                                     $"{beat} ({Clock(end)}) and into the next beat.");
                }
            }

            return true;
        }

        /// <summary>
        /// The 共同 row's source must carry NO role gate. RoleAudible mutes the whole AudioSource,
        /// so one on here does not make the line a little more private -- it takes a line written
        /// for the room away from one of the two people in it, and the other player's headset
        /// sounds perfectly correct while it happens. Reported rather than removed: a component
        /// somebody added on purpose is not this tool's to delete.
        /// </summary>
        static bool EnsureUngated(AudioSource source)
        {
            if (source.GetComponent<RoleAudible>() != null)
            {
                Debug.LogError($"[S2VO] RESULT: FAIL - '{source.name}' carries a RoleAudible. " +
                               "The 共同 lines are heard by both players; gated, one of them " +
                               "stands in silence and nothing on the other headset shows it. " +
                               "Remove the component and re-run.");
                return false;
            }

            Debug.Log($"[S2VO] 旁白 共同 -> '{source.name}', ungated: both players hear it.");
            return true;
        }

        /// <summary>
        /// Seconds as the running order says them, so a log line can be read against the call
        /// sheet without arithmetic.
        /// </summary>
        static string Clock(double seconds)
        {
            System.TimeSpan span = System.TimeSpan.FromSeconds(seconds);
            return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
        }

        /// <summary>
        /// Where a beat starts and ends, read off the running order rather than typed here. That
        /// row is what the other departments are building to and it moves; a second copy of its
        /// start in this file is a number that would quietly stop agreeing with it.
        /// </summary>
        static bool BeatSlot(TimelineAsset timeline, string beat, out double start, out double end)
        {
            start = end = 0d;

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (!(track is ControlTrack)) continue;

                foreach (TimelineClip clip in track.GetClips())
                {
                    if (!clip.displayName.StartsWith(beat)) continue;

                    start = clip.start;
                    end = clip.end;
                    Debug.Log($"[S2VO] {beat} is '{clip.displayName}', {start:F1}-{end:F1}s in " +
                              "the running order.");
                    return true;
                }
            }

            Debug.LogError($"[S2VO] RESULT: FAIL - no Control clip named '{beat}...' on " +
                           $"{k_Timeline}, so there is no saying when the line is due.");
            return false;
        }

        /// <summary>
        /// One line, as long as the wav is, replacing any earlier copy of itself.
        ///
        /// Sized to the audio and not to a typed number, so a clip can never be shorter than the
        /// sentence inside it -- which cuts a line off mid-word and is close to invisible in the
        /// Timeline window. Matched by display name, so re-running replaces this line and leaves
        /// every other clip on the deck where it is.
        /// </summary>
        static void Replace(TimelineAsset timeline, AudioTrack track, string label,
                            AudioClip audio, double at)
        {
            foreach (TimelineClip old in new List<TimelineClip>(track.GetClips()))
            {
                if (old.displayName == label) timeline.DeleteClip(old);
            }

            TimelineClip clip = track.CreateClip<AudioPlayableAsset>();
            ((AudioPlayableAsset)clip.asset).clip = audio;
            clip.start = at;
            clip.duration = audio.length;
            clip.displayName = label;

            Debug.Log($"[S2VO] {track.name}: {label} ({audio.name}) " +
                      $"{at:F1}-{at + audio.length:F1}s.");
        }

        /// <summary>
        /// The two decks are bound, audible, and private to DIFFERENT players.
        ///
        /// Both decks private to the same player is the failure that sounds like success on one
        /// headset: that player hears the whole beat and nobody notices the other stood in
        /// silence until there are two people in the room. The roles are written by index through
        /// a SerializedProperty, so this is checked rather than trusted.
        /// </summary>
        static bool Verify(PlayableDirector show, Deck[] decks)
        {
            var seen = new List<PlayerRole>();

            foreach (Deck deck in decks)
            {
                var bound = show.GetGenericBinding(deck.track) as AudioSource;
                if (bound == null)
                {
                    Debug.LogError($"[S2VO] RESULT: FAIL - '{deck.trackName}' is bound to no " +
                                   "AudioSource, so that player's lines would never be heard.");
                    return false;
                }

                if (!bound.gameObject.activeInHierarchy)
                {
                    Debug.LogError($"[S2VO] RESULT: FAIL - '{bound.name}' is not active in the " +
                                   "hierarchy; Timeline cannot sound through a disabled source.");
                    return false;
                }

                // The one deck that is SUPPOSED to be ungated, and the one check that keeps a
                // 共同 line from quietly becoming one player's private line.
                if (!deck.role.HasValue)
                {
                    if (bound.GetComponent<RoleAudible>() != null)
                    {
                        Debug.LogError($"[S2VO] RESULT: FAIL - '{bound.name}' carries a " +
                                       "RoleAudible, so the 共同 lines would reach one headset.");
                        return false;
                    }

                    Debug.Log($"[S2VO] verified: '{bound.name}' is ungated, both players hear.");
                    continue;
                }

                var presenter = bound.GetComponent<RoleAudible>();
                if (presenter == null)
                {
                    Debug.LogError($"[S2VO] RESULT: FAIL - '{bound.name}' has no RoleAudible, " +
                                   $"so both players would hear 旁白 {deck.label}.");
                    return false;
                }

                var so = new SerializedObject(presenter);
                SerializedProperty roles = so.FindProperty("m_AudibleToRoles");
                if (roles.arraySize != 1)
                {
                    Debug.LogError($"[S2VO] RESULT: FAIL - '{bound.name}' is audible to " +
                                   $"{roles.arraySize} roles; one is the whole point of it.");
                    return false;
                }

                seen.Add((PlayerRole)roles.GetArrayElementAtIndex(0).intValue);
            }

            if (seen.Count != 2 || seen[0] == seen[1])
            {
                Debug.LogError("[S2VO] RESULT: FAIL - the two decks resolved to " +
                               $"{string.Join(" and ", seen)}; they must be different players.");
                return false;
            }

            Debug.Log($"[S2VO] verified: one deck each for {seen[0]} and {seen[1]}.");
            return true;
        }

        static GameObject Find(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                GameObject hit = Find(root.transform, name);
                if (hit != null) return hit;
            }
            return null;
        }

        static GameObject Find(Transform from, string name)
        {
            if (from.name == name) return from.gameObject;

            foreach (Transform child in from)
            {
                GameObject hit = Find(child, name);
                if (hit != null) return hit;
            }

            return null;
        }
    }
}
