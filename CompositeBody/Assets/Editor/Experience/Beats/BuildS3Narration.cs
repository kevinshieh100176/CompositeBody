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
    /// S3's 旁白: one recording across the whole section, on a private deck per player.
    ///
    /// 「timeline上s3整段要播S1B_20261101」, then 「這段分開來 a聽a的 b聽b的 但現在還沒有a的vo
    /// 之後會補 現在先都聽同一段」, and on 2026-11-05 the two recordings landed: 「幫我把s3的音檔
    /// 改成這兩個 分別是a跟b聽到到的」.
    ///
    /// So the structure and the content now agree -- two private decks, each behind its own
    /// role gate, each with its own recording, because S3 is the section where the two players
    /// are told different things. 玩家A hears S3A_20261105 and 玩家B hears S3B_20261105. The
    /// guard that warned when both decks pointed at one file is still at the end of Run(),
    /// silent now: it costs nothing and a placeholder nobody notices is a placeholder that
    /// ships.
    ///
    /// ONE CLIP ACROSS THE SECTION, not one per beat. 「整段」 is the instruction: this is a
    /// continuous account running under all six S3 beats rather than a line cued to any one of
    /// them, so it is one clip on each deck, starting on S3's own first frame and ending on its
    /// last. The two numbers come off Main's S3-* control tracks, so a re-cut S3 re-times it.
    ///
    /// AND IT IS TRIMMED TO THE SECTION, NOT FITTED. Both recordings are 256 seconds and S3 is
    /// 255, so one second comes off the end of each -- these were rendered to the section, unlike
    /// the S1 file they replaced, which was 357 and lost 102. Cut rather than stretched either
    /// way: stretching means a playback speed other than 1 on a voice, and the drift trim
    /// already moves this show's clock (ShowTimelineDriver), where a nested voice re-seeks on a
    /// speed change -- at frame rate that is a stutter, not a slow reading. The run reports how
    /// much is being left off.
    ///
    /// WHY THE TRACKS GO ON Main. Same reason S1's and S2's do -- see BuildS2Narration's
    /// remarks -- the running order is the one place a line's position is readable against every
    /// other beat, and BuildMainTimeline leaves hand-authored tracks alone.
    ///
    /// AND THE SOURCES SIT BESIDE Content, NOT INSIDE IT. Beat_S3 itself is not activation-
    /// driven today, but Content is the part every section's activation bands reach for, and an
    /// AudioSource switched off is silence with no symptom: the tracks are there, the clips are
    /// there, and nothing comes out of either headset. 'S3 VO' is a sibling of Content, exactly
    /// like Beat_S1/S1 VO and Beat_S2/S2 VO.
    ///
    /// IT DOES NOT SAVE, following BuildS2Narration rather than the Build* tools that open a
    /// scene over whatever you had and save on the way out. MainScene usually has someone's
    /// unsaved authoring in it and neither discarding that nor writing it to disk is this
    /// tool's call. Nothing is on disk until you save the scene, which writes the timeline with
    /// it.
    ///
    /// Tools > S-3 > Build S3 Narration
    /// </summary>
    public static class BuildS3Narration
    {
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";
        const string k_Timeline = "Assets/_Timeline/Main.playable";

        const string k_S3Root = "Experience/Beats/Beat_S3";
        const string k_VORoot = "S3 VO";

        const string k_TrackA = "S3 旁白 A (Player1)";
        const string k_TrackB = "S3 旁白 B (Player2)";
        const string k_SpeakerA = "S3 VO A";
        const string k_SpeakerB = "S3 VO B";

        /// <summary>What each deck plays, and the label on its clip.</summary>
        const string k_Label = "S3 整段";

        /// <summary>
        /// 玩家A 聽 A 的，玩家B 聽 B 的 -- 2026-11-05 收到的兩個音檔。Both are 256 s, 24-bit
        /// 48 kHz stereo, imported Streaming/Vorbis like every other long line in here. Their
        /// first 71 seconds are the same audio and they diverge after that, which is what the
        /// section is: a shared opening and then two accounts of it.
        /// </summary>
        const string k_LineA = "Assets/_Audio/S3A_20261105.wav";

        const string k_LineB = "Assets/_Audio/S3B_20261105.wav";

        /// <summary>A player's deck: the track, the source under S3 VO, and who may hear it.</summary>
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

        [MenuItem("Tools/S-3/Build S3 Narration")]
        public static void Run()
        {
            Debug.Log("[S3VO] Starting...");

            // REUSED, NOT REOPENED. Opening MainScene Single here would discard whatever is in
            // front of whoever ran this, which is what BuildS2Narration refuses to do too.
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != k_MainScene)
            {
                Debug.LogError($"[S3VO] RESULT: FAIL - {k_MainScene} is not the open scene " +
                               $"('{scene.path}' is). Open it and run this again.");
                return;
            }

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (timeline == null)
            {
                Debug.LogError($"[S3VO] RESULT: FAIL - no timeline at {k_Timeline}.");
                return;
            }

            PlayableDirector show = FindShowDirector(timeline);
            if (show == null)
            {
                Debug.LogError("[S3VO] RESULT: FAIL - nothing in MainScene plays " +
                               $"{k_Timeline}, so there is no director to bind a track on.");
                return;
            }

            if (!S3Span(timeline, out double from, out double to))
            {
                Debug.LogError("[S3VO] RESULT: FAIL - no S3-* control tracks on Main, so where " +
                               "S3 starts and ends is not knowable from the running order.");
                return;
            }

            Transform voRoot = EnsureVORoot(scene);
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

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(show);
            EditorSceneManager.MarkSceneDirty(scene);

            if (!Verify(show, decks)) return;

            if (k_LineA == k_LineB)
            {
                Debug.LogWarning(
                    "[S3VO] ONE FILE ON BOTH DECKS: every player hears " +
                    $"{System.IO.Path.GetFileName(k_LineA)}. S3 is a section where the two are " +
                    "told different things, so this is either a placeholder or a mistake. The " +
                    "decks and their role gates are right either way -- only k_LineA and " +
                    "k_LineB say who hears what.");
            }

            Debug.Log("[S3VO] RESULT: PASS - and nothing is on disk yet: save MainScene " +
                      "(File > Save) and the timeline asset goes with it.");
        }

        /// <summary>
        /// One clip per deck, across the whole section, replacing only this builder's own.
        ///
        /// Replaced by label rather than by clearing the track: these two rows are where S3's
        /// per-beat lines will go as they are recorded, and a re-run of this tool must not take
        /// them with it.
        /// </summary>
        static bool Author(TimelineAsset timeline, Deck deck, double from, double to)
        {
            var audio = AssetDatabase.LoadAssetAtPath<AudioClip>(deck.line);
            if (audio == null)
            {
                Debug.LogError($"[S3VO] RESULT: FAIL - no AudioClip at {deck.line}.");
                return false;
            }

            int removed = 0;
            foreach (TimelineClip old in new List<TimelineClip>(deck.track.GetClips()))
            {
                if (old.displayName != k_Label) continue;
                timeline.DeleteClip(old);
                removed++;
            }

            double span = to - from;

            TimelineClip clip = deck.track.CreateClip<AudioPlayableAsset>();
            ((AudioPlayableAsset)clip.asset).clip = audio;
            clip.start = from;
            clip.clipIn = 0d;

            // THE SECTION'S LENGTH, NOT THE FILE'S. A clip longer than the section would keep
            // talking over the fade to white and into whatever follows S3.
            clip.duration = span;
            clip.displayName = k_Label;

            string trimmed = audio.length > span
                ? $" -- TRIMMED: the file is {audio.length:F0}s, so its last " +
                  $"{audio.length - span:F0}s never play"
                : audio.length < span
                    ? $" -- SHORT: the file is only {audio.length:F0}s, so the last " +
                      $"{span - audio.length:F0}s of S3 are silent on this deck"
                    : "";

            Debug.Log($"[S3VO] {deck.track.name}: {k_Label} ({audio.name}) " +
                      $"{from:F0}-{to:F0}s ({span:F0}s){trimmed}" +
                      $"{(removed > 0 ? $", replacing {removed} earlier copy" : "")}.");
            return true;
        }

        /// <summary>
        /// Where S3 begins and ends, from the running order rather than a table -- the same scan
        /// BuildS3Figures uses, and for the same reason: these beats have been re-cut twice.
        /// 'S3-3' is stored as 'S3-3 ' with a trailing space, so names are trimmed.
        /// </summary>
        static bool S3Span(TimelineAsset timeline, out double from, out double to)
        {
            from = double.MaxValue;
            to = 0d;

            foreach (TrackAsset t in timeline.GetOutputTracks())
            {
                if (t is not ControlTrack || !t.name.Trim().StartsWith("S3-")) continue;

                foreach (TimelineClip c in t.GetClips())
                {
                    if (c.start < from) from = c.start;
                    if (c.end > to) to = c.end;
                }
            }

            return from < double.MaxValue && to > from;
        }

        static Transform EnsureVORoot(Scene scene)
        {
            GameObject s3 = Find(k_S3Root);
            if (s3 == null)
            {
                Debug.LogError($"[S3VO] RESULT: FAIL - no '{k_S3Root}' in MainScene to hang " +
                               $"'{k_VORoot}' off.");
                return null;
            }

            Transform root = s3.transform.Find(k_VORoot);
            if (root == null)
            {
                var made = new GameObject(k_VORoot);
                made.transform.SetParent(s3.transform, false);
                root = made.transform;
                Debug.Log($"[S3VO] created '{k_S3Root}/{k_VORoot}'.");
            }

            // An inactive VO root is silence with no symptom.
            if (!root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(true);
                Debug.Log($"[S3VO] switched '{k_VORoot}' back on; Timeline cannot sound through " +
                          "a disabled AudioSource.");
            }

            return root;
        }

        /// <summary>
        /// Found by name and reused, so the clips are replaced without the track object changing
        /// -- bindings key off the track object, so deleting and recreating one would break them.
        /// </summary>
        static AudioTrack EnsureTrack(TimelineAsset timeline, string name)
        {
            foreach (TrackAsset existing in timeline.GetOutputTracks())
            {
                if (existing is AudioTrack audio && audio.name == name) return audio;
            }

            var track = timeline.CreateTrack<AudioTrack>(null, name);
            if (track == null)
                Debug.LogError($"[S3VO] RESULT: FAIL - could not create the track '{name}'.");
            else
                Debug.Log($"[S3VO] created the track '{name}'.");

            return track;
        }

        /// <summary>
        /// A 2D source of its own per player, like S1's and S2's. RoleAudible mutes a whole
        /// AudioSource, so one shared between the two decks would silence one player's line to
        /// hide the other's. 2D because this is the player's own account and not a voice standing
        /// somewhere in the room.
        /// </summary>
        static AudioSource EnsureSpeaker(Transform root, string name)
        {
            Transform holder = root.Find(name);
            if (holder == null)
            {
                var made = new GameObject(name);
                made.transform.SetParent(root, false);
                holder = made.transform;
                Debug.Log($"[S3VO] created '{name}'.");
            }

            holder.gameObject.SetActive(true);

            var source = holder.GetComponent<AudioSource>();
            if (source == null) source = holder.gameObject.AddComponent<AudioSource>();

            // playOnAwake would start whatever clip the inspector happens to be holding at show
            // start, nearly twenty minutes before this deck is due.
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
                Debug.Log($"[S3VO] added RoleAudible to '{source.name}'.");
            }

            var so = new SerializedObject(presenter);
            SerializedProperty roles = so.FindProperty("m_AudibleToRoles");
            roles.arraySize = 1;
            roles.GetArrayElementAtIndex(0).intValue = (int)role;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[S3VO] 旁白 {label} -> '{source.name}', audible to {role} only.");
        }

        /// <summary>
        /// The two decks are bound, and private to DIFFERENT players.
        ///
        /// Both decks private to the same player is the failure that sounds like success on one
        /// headset: that player hears the whole section and nobody notices the other stood in
        /// silence until there are two people in the room. Checked rather than trusted.
        /// </summary>
        static bool Verify(PlayableDirector show, Deck[] decks)
        {
            var seen = new List<PlayerRole>();

            foreach (Deck deck in decks)
            {
                if (show.GetGenericBinding(deck.track) is not AudioSource bound)
                {
                    Debug.LogError($"[S3VO] RESULT: FAIL - '{deck.trackName}' is bound to no " +
                                   "AudioSource, so it makes no sound at all.");
                    return false;
                }

                var presenter = bound.GetComponent<RoleAudible>();
                if (presenter == null)
                {
                    Debug.LogError($"[S3VO] RESULT: FAIL - '{bound.name}' has no RoleAudible, so " +
                                   "both players would hear this private deck.");
                    return false;
                }

                var so = new SerializedObject(presenter);
                SerializedProperty roles = so.FindProperty("m_AudibleToRoles");
                if (roles.arraySize != 1)
                {
                    Debug.LogError($"[S3VO] RESULT: FAIL - '{bound.name}' is audible to " +
                                   $"{roles.arraySize} roles; a private deck wants exactly one.");
                    return false;
                }

                var role = (PlayerRole)roles.GetArrayElementAtIndex(0).intValue;
                if (seen.Contains(role))
                {
                    Debug.LogError($"[S3VO] RESULT: FAIL - two decks are both audible to {role} " +
                                   "only, so the other player hears nothing for the whole of S3.");
                    return false;
                }

                seen.Add(role);
            }

            Debug.Log($"[S3VO] verified: {decks.Length} decks, one each for " +
                      string.Join(" and ", seen));
            return true;
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
