using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Authors O-0's cue into Assets/_Timeline/O_0.playable, and puts the director that plays it
    /// inside O_0.prefab.
    ///
    /// THE CUE: five seconds of the single white light, then the two 光圈 come up on 真人 A and B
    /// while the white light goes out. The overlap is deliberate -- the colours are already
    /// rising for two seconds before the white starts to fall, because with a clean hand-off the
    /// room dips dark in the middle of the change and reads as a glitch rather than as a cue.
    ///
    /// THE DIRECTOR GOES IN THE PREFAB, not in O_0.unity. The authoring scene is where the
    /// timeline is edited, but a director that only exists there would not exist in MainScene,
    /// and the show would open on a white light that never changes. Inside the prefab it travels
    /// with the beat.
    ///
    /// UPDATE MODE IS MANUAL, and that is the whole reason this is worth a tool rather than ten
    /// minutes of dragging. A PlayableDirector left on Game Time plays on each machine's own
    /// clock, and two people standing in the same room would watch the same cross-fade happen at
    /// different moments -- seconds apart, depending on when each headset started. O0ArrivalBeat
    /// drives director.time from ExperienceClock instead, so the Timeline is the authoring
    /// surface and the shared clock is the time source.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildO0Timeline.Run
    /// </summary>
    public static class BuildO0Timeline
    {
        const string k_Timeline = "Assets/_Timeline/O_0.playable";
        /// <summary>
        /// The prefab that is actually in the show.
        ///
        /// NOT O_0.prefab, WHICH IS ONLY THE SEED. BuildOnboarding copied it once into
        /// Onboarding.prefab and merged the other onboarding beats into that; MainScene has
        /// instanced Onboarding.prefab ever since. This builder went on wiring the seed, so
        /// everything it did to a prefab -- the Animator, the director, the bindings -- landed in
        /// an asset nothing plays. The timeline ASSET is shared, which is why the cue still
        /// worked and why nobody noticed.
        /// </summary>
        const string k_Prefab = "Assets/_Scenes/Onboarding.prefab";

        /// <summary>
        /// The seed, still wired so O_0.unity keeps previewing. It has no Beats group and no
        /// Interaction object, so the handover is skipped there rather than failing.
        /// </summary>
        const string k_SeedPrefab = "Assets/_Scenes/O_0.prefab";
        const string k_TrackName = "O-0 Cue";
        const string k_ClipName = "O_0_Cue";
        const string k_AudioTrackName = "Cue Tone";
        const string k_HandoverTrackName = "O-0 Handover";

        /// <summary>
        /// The body cue's own row, because it now starts while the 真人 are still dissolving.
        ///
        /// TWO CLIPS THAT OVERLAP ON ONE ANIMATION TRACK ARE A BLEND, not two things happening
        /// at once. Timeline crossfades them, and the first run with the cue moved under the
        /// dissolve read the 真人 at 0.04 where the curve says 0.13 and the cue at 0.00 where it
        /// has to cross a half -- so the 真人 half-left and the body was never told to arrive at
        /// all. Separate rows do not blend, and the two may now sit wherever the pacing wants
        /// them.
        /// </summary>
        const string k_BodyTrackName = "O-0 Body cue";

        /// <summary>
        /// The 真人's arrival, on a row of its own above the dissolve.
        ///
        /// A ROW OF ITS OWN BECAUSE IT HAS TO HOLD. The clip is seven seconds long and the 真人
        /// then stand there for eighty more, so the property has to keep reading 1 long after the
        /// clip has ended -- that is the clip's post-extrapolation, and extrapolation is a
        /// property of a row rather than of a moment, so the row can hold nothing else.
        /// </summary>
        const string k_ArriveTrackName = "O-0 真人 arrive";
        const string k_ScansInClipName = "O_0_ScansIn";
        const string k_ScansClipName = "O_0_ScansOut";
        const string k_BodyClipName = "O_0_BodyCue";

        /// <summary>
        /// Where the two components the handover drives sit, relative to the Animator on the
        /// prefab root. Animation paths are strings and fail silently when they are wrong, so
        /// they are named once and <see cref="Verify"/> drives the finished timeline to prove
        /// they still resolve.
        /// </summary>
        const string k_HandoverPath = "Beats";
        const string k_AwakeningPath = "Content/Interaction";

        const string k_TitleTex = "Assets/_Art/O0/Title_CompositeBody_PLACEHOLDER.png";
        const string k_LineTex = "Assets/_Art/O0/O0_OpeningLine.png";
        const string k_TitleMat = "Assets/Materials/O0Title.mat";
        const string k_LineMat = "Assets/Materials/O0Line.mat";
        const string k_ToneClip = "Assets/_Audio/Placeholder/O0_Cue_PLACEHOLDER.wav";

        /// <summary>
        /// THE TWO PRIVATE SELF-ACCOUNTS, a pair each. 「玩家a 跟 玩家b 會各自聽到 不同音檔」: A and B
        /// are never told the same thing in O-0, which is the first thing the piece says about
        /// the two of them standing in different rooms of the same memory.
        ///
        /// A ROLE IS A TRACK, NOT A BRANCH. Timeline has no idea a player exists -- but each
        /// machine runs its own copy of this prefab with its own director, so two AudioTracks
        /// bound to two AudioSources are already two independent decks on every headset, and
        /// which deck is heard is a purely local decision that <see cref="RoleAudible"/> makes.
        /// See that class for why it mutes rather than stops. Nothing here crosses the network
        /// and the asset is identical on both machines.
        /// </summary>
        const string k_LineA1 = "Assets/_Audio/O-A1_他離開以後.wav";
        const string k_LineB1 = "Assets/_Audio/O-B1_他搬走以後.wav";
        const string k_LineA2 = "Assets/_Audio/O-A2_有些記憶.wav";
        const string k_LineB2 = "Assets/_Audio/O-B2_有些記憶.wav";

        const string k_NarrationATrack = "O-0 自白 A (Player1)";
        const string k_NarrationBTrack = "O-0 自白 B (Player2)";
        const string k_SpeakerA = "Narration A";
        const string k_SpeakerB = "Narration B";

        /// <summary>
        /// Where the cards hang, in metres from the beat's origin, and how wide they are.
        ///
        /// 「出現一行字」 in the centre of the space: z = 0 is the middle of the 4 x 5 m room, and
        /// the beat's origin is the room's origin. They used to sit at 2.8 m, which was sized for
        /// a viewer standing near the middle -- with the players now starting at z = -2 and the
        /// scans at the far end, a card at 2.8 m would be nearly 5 m away and behind the figures.
        ///
        /// Sized for a 2 m read, which is the distance from z = -2 to the centre. The line is 18
        /// characters across at 1.25 m, so each glyph subtends about 2 degrees -- roughly twice
        /// what is comfortable to read, which is right for one held sentence in a dark room. The
        /// whole block is 35 degrees wide, so it is a single fixation and not a head turn.
        /// </summary>
        const float k_CardDistance = 0f;
        const float k_TitleWidth = 0.85f;
        const float k_LineWidth = 1.25f;
        const float k_EyeHeight = 1.55f;

        /// <summary>Where the players stand. Staging reference only -- nothing moves the rig.</summary>
        const float k_ViewerZ = -2f;

        // The whole opening, in seconds. Authored here and then editable by dragging in the
        // Timeline window -- these are starting positions, not a contract.
        //
        // 「觀眾一戴上VR，看到：合成肉身。體驗正式開始，出現提示音，出現一行字」. The title is the first
        // thing in the piece, so it arrives in the dark with nothing else in the room: the white
        // light does not exist yet, which is why every fixture's curve now starts at zero rather
        // than at full.
        const float k_TitleUp = 2f;       // title in:        0 -> 2
        const float k_TitleOutAt = 6f;    // title out:       6 -> 8
        const float k_TitleOut = 2f;
        const float k_ToneAt = 8f;        // 提示音, as the title clears
        const float k_LineInAt = 8.5f;    // the line in:     8.5 -> 10.5
        const float k_LineUp = 2f;
        const float k_LineOutAt = 16f;    // the line out:    16 -> 18
        const float k_LineOut = 2f;

        // The room arrives UNDER the last of the text rather than after it. A clean gap between
        // the words going and the light coming reads as two separate events; an overlap reads as
        // the words becoming the space, which is what the line is about.
        const float k_LightUpAt = 15f;
        const float k_LightUp = 3f;
        const float k_Open = k_LightUpAt + k_LightUp;   // 18: the white light is full

        const float k_Hold = 5f;          // the white light alone:  18 -> 23
        const float k_ColourUp = 4f;      // 光圈 rising:             23 -> 27
        const float k_CentreDelay = 2f;   // after the colours start
        const float k_CentreDown = 5f;    // white falling:          25 -> 30
        const float k_Tail = 2f;          // a little hold at the end so the clip is not clipped

        // THE HANDOVER, which used to be earned by touching a 真人 and is now authored here.
        //
        // 「玩家不需要去觸摸scan, 而是改在o_0 timeline上達成 scan dissolve, scan dissolve後 玩家身體
        // 按照原本計畫出現」. Two clips on one track, so the handover reads as one movement in the
        // window. Drag either clip to re-cut the pacing; stretch the first to make the scans
        // take longer.
        //
        // THEY NOW OVERLAP, WHERE THEY USED TO BE A SEQUENCE. The cue used to sit a second after
        // the last 真人 had gone; it now starts with the dissolve, because the beat has to end at
        // 140s and the formation needs thirty of them. Nothing is out of order in the picture:
        // AvatarFormation grows the membrane over the first 45% of its ramp and only then fades
        // the Ch36 body in, so what overlaps the dissolve is the film forming, and the body
        // itself still arrives several seconds after the room is empty. Retime warns if that
        // stops being true.
        //
        // The scans go after the cue above has finished, not during it. The 光圈 are the 真人's
        // own lights -- dissolving a figure while its colour is still rising reads as a fault in
        // the light rather than as the figure leaving.
        // AND IT NOW WAITS FOR THE FIRST PAIR OF LINES. 「播完後才接scan dissolve」 -- the scans
        // may not start going while either player is still being spoken to.
        //
        // THE TWO LINES ARE NOT THE SAME LENGTH, so "after they have played" is one moment and
        // not two: the later of the two ends it, and the shorter player waits out the difference
        // in a room that has not moved yet. There is no second answer available -- the directors
        // are scrubbed from one shared clock, so a dissolve that started when each player's own
        // line finished would happen at two different times in the same room.
        //
        // WHICH IS WHY THESE ARE READ OFF THE CLIPS RATHER THAN TYPED. Swap a wav and the beat
        // re-cuts itself on the next run instead of talking over its own dissolve.
        //
        // THE TWENTY AND THE NINETY ARE THE SCRIPT'S. Production timeline v4 writes this beat as
        // 重點句 20s, then O-0｜進入 / 第一段VO 90s -- two rows for one and the same thing -- so the
        // first pair starts at 20s and is budgeted 90, which A1 fills at 88.0s. The 30s it then
        // gives 轉場一｜紙箱 is the one number here that is NOT followed: see the O-0 row in
        // BuildMainTimeline for why the whole body arrives in this beat where the sheet grows
        // only hands, and why the second pair waits for it instead of playing over it.
        // WHEN THE 真人 ARRIVE, as a length rather than a time: they start with the first 自白
        // and finish as their own 光圈 finish rising.
        //
        // 「現在O_0的SCAN 一開始就在 改成跟第一段VO(A1/B1)一起出現」. Neither end of this is a number of
        // its own, which is the point: the start is k_FirstLinesAt, and the end is the cue's own
        // 光圈 ramp above. The halos ARE the figures' light, and a figure that materialised before
        // or after its own lamp read as two events rather than one arrival. Seven seconds, as
        // long as the cue stays where it is.
        const float k_ScansIn = k_Open + k_Hold + k_ColourUp - k_FirstLinesAt;

        const float k_ScansOut = 10f;     // how long the 真人 take to go
        const float k_FirstLinesAt = 20f; // 「過20秒後」, with the room up and the 光圈 rising
        const float k_LinesTail = 1f;     // a breath between the last word and the scans going

        /// <summary>
        /// WHERE THE SECOND PAIR SPEAKS, AND NOW THE ANCHOR OF THE WHOLE BACK HALF.
        ///
        /// It used to be derived -- body cue, plus the formation, plus a breath -- which put it
        /// at 152.5s and O-0 at 185s. 「我把A2 B2 的音檔往前移了 讓他在140S的時候播完」: the pair was
        /// dragged to here in the Timeline window instead, so that the beat ends at 140s where
        /// the production timeline says it does, and the body now arrives UNDER the second pair
        /// rather than before it. So this number is typed, read off what was dragged, and the
        /// handover behind it hangs off it -- see <see cref="Retime"/>.
        ///
        /// Drag the clips again and change this to match, or the next run puts them back.
        /// </summary>
        const float k_SecondLinesAt = 112f;

        /// <summary>
        /// The numbers the beat had before the lines existed, kept as the fallback. Used only
        /// when a narration clip cannot be loaded: the builder still authors a handover that
        /// works rather than collapsing every cue onto zero, and the log says which clip it
        /// could not find.
        /// </summary>
        const float k_ScansOutAtFallback = 34f;
        const float k_BodyAtFallback = 45f;

        // Filled by Retime() before anything is authored. Not const, because their values are
        // in the audio.
        static float s_ScansOutAt = k_ScansOutAtFallback;
        static float s_BodyAt = k_BodyAtFallback;
        static float s_SecondLinesAt = k_SecondLinesAt;

        // Only long enough to cross a half, which is what O0TouchAwakening latches on. It is a
        // ramp rather than a signal marker because this show's directors are scrubbed and
        // resynced from a shared clock, and a notification that was dropped or doubled would
        // leave one headset with a body and the other without. See ShowHoldMarker's remarks.
        const float k_BodyCue = 0.5f;

        /// <summary>
        /// AvatarFormation's own ramp, read off XRI_Network_Player_Avatar.prefab. Not used to
        /// drive anything -- the body arrives on its own clock once it is told to -- but the cue
        /// is now placed so that the ramp LANDS on the last word of the second pair, so the
        /// number has to be written down here too.
        ///
        /// THIRTY, WHERE IT WAS THIRTY-TWO. This and the prefab's m_TotalSeconds are one number
        /// kept in two places: change one and change the other, or the body arrives at a time
        /// nothing on the timeline agrees with. Two seconds came off so the cue lands with the
        /// dissolve rather than a second and a half before it -- 「把身體出現的時間縮短讓他在140S
        /// 完整跑完」. The shape is untouched: m_TotalSeconds stretches the authored curves rather
        /// than rewriting them, so the membrane still has the first 45% of the ramp and the Ch36
        /// body still fades in over the back half.
        /// </summary>
        const float k_BodySeconds = 30f;

        /// <summary>
        /// Where AvatarFormation's authored m_BodyCurve leaves zero: the point in the ramp at
        /// which the Ch36 body itself starts to show, once the membrane has grown. Off the same
        /// prefab as <see cref="k_BodySeconds"/>.
        ///
        /// 「我的理解是AVATAR的膜會先出現完身體才會出現」 -- it does, and this is the only reason that
        /// reading matters here: the cue may overlap the 真人 dissolve, but the BODY may not, and
        /// without this number there is no way to say which of the two the overlap is.
        /// </summary>
        const float k_MembraneShare = 0.5f;

        // Full values, matching what BuildO0Arrival authored.
        const float k_CentreLamp = 24f;
        const float k_ColourLamp = 6f;

        /// <summary>
        /// One player's deck: the row in the Timeline window, the speaker it plays out of, and
        /// the one role allowed to hear it.
        /// </summary>
        struct Deck
        {
            public PlayerRole role;
            public string label;
            public string trackName;
            public string speaker;
            public string first;
            public string second;
            public AudioTrack track;
        }

        /// <summary>
        /// Menu entry as well as the batch one, because the batch one cannot run while the editor
        /// has the project open -- and the editor is exactly where someone is standing when they
        /// want to see what this does.
        /// </summary>
        [MenuItem("Tools/O-0/Build Timeline")]
        static void RunFromMenu()
        {
            // Run() opens O_0.unity to bind the preview director, which discards whatever is
            // unsaved in the editor right now. Ask first, and abort on a no.
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Run();
        }

        public static void Run()
        {
            Debug.Log("[Timeline] Starting...");

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (timeline == null)
            {
                Debug.LogError($"[Timeline] RESULT: FAIL - no TimelineAsset at {k_Timeline}.");
                return;
            }

            AnimationClip clip = BuildClip(timeline);
            AnimationTrack track = BuildTrack(timeline, clip);
            if (track == null) return;

            AudioTrack audioTrack = BuildAudioTrack(timeline);
            if (audioTrack == null) return;

            MuteStrayAnimationTracks(timeline);

            // Before the handover, because where the scans go and when the body arrives are both
            // read off the length of the first pair of lines.
            Retime();

            Deck[] decks = BuildNarrationTracks(timeline);
            if (decks == null) return;

            AnimationTrack[] handover = BuildHandoverTracks(timeline);
            if (handover == null) return;

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(k_Timeline, ImportAssetOptions.ForceSynchronousImport);

            // The live prefab first, and then the seed -- so O_0.unity keeps previewing the cue
            // without the show depending on an asset nothing instances.
            if (!WirePrefab(timeline, track, audioTrack, handover, decks, k_Prefab, true)) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(k_SeedPrefab) != null)
                WirePrefab(timeline, track, audioTrack, handover, decks, k_SeedPrefab, false);

            // Flush the prefab before the scene is opened. WirePrefab has just added an Animator
            // to the prefab asset, and an instance already sitting in a scene does not grow one
            // until the asset is written and reimported -- so without this the binding below
            // looks for an Animator that does not exist yet and quietly gives up.
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(k_Prefab, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(k_SeedPrefab, ImportAssetOptions.ForceSynchronousImport);

            BindAuthoringScene();
            if (!Verify(decks)) return;

            float end = k_Open + k_Hold + k_CentreDelay + k_CentreDown + k_Tail;
            float colourAt = k_Open + k_Hold;
            Debug.Log($"[Timeline] title 0-{k_TitleOutAt + k_TitleOut}s, tone {k_ToneAt}s, " +
                      $"line {k_LineInAt}-{k_LineOutAt + k_LineOut}s, room full {k_Open}s, " +
                      $"halos up {colourAt}-{colourAt + k_ColourUp}s, white out " +
                      $"{colourAt + k_CentreDelay}-{colourAt + k_CentreDelay + k_CentreDown}s, " +
                      $"clip {end}s.\n" +
                      $"[Timeline] handover: 真人 in {k_FirstLinesAt}-" +
                      $"{k_FirstLinesAt + k_ScansIn}s, 真人 out " +
                      $"{s_ScansOutAt}-{s_ScansOutAt + k_ScansOut}s, " +
                      $"body cue {s_BodyAt:F2}s, body arrived by " +
                      $"{s_BodyAt + k_BodyCue * 0.5f + k_BodySeconds:F2}s.\n" +
                      $"[Timeline] 自白: pair 1 at {k_FirstLinesAt}s, pair 2 at " +
                      $"{s_SecondLinesAt}s -- O-0 runs {timeline.duration:F1}s, so its clip " +
                      "in Main.playable has to be at least that wide or the beat is cut off.\n" +
                      "[Timeline] RESULT: PASS");
        }

        /// <summary>
        /// One clip holding all six curves, as a sub-asset of the timeline.
        ///
        /// One clip rather than one per fixture: the whole point of the cue is that the two
        /// changes overlap, and that relationship is only visible if the keys sit on the same
        /// piece of paper.
        /// </summary>
        static AnimationClip BuildClip(TimelineAsset timeline)
        {
            // Reuse the existing sub-asset if this has been run before, so the Timeline window
            // does not accumulate orphans.
            AnimationClip clip = null;
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(k_Timeline))
            {
                if (sub is AnimationClip existing && existing.name == k_ClipName) clip = existing;
            }

            bool isNew = clip == null;
            if (isNew) clip = new AnimationClip { name = k_ClipName };
            clip.frameRate = 60f;
            clip.ClearCurves();

            float colourStart = k_Open + k_Hold;
            float colourEnd = colourStart + k_ColourUp;
            float centreStart = colourStart + k_CentreDelay;
            float centreEnd = centreStart + k_CentreDown;

            // The title, then the line. Both start at nothing, because the piece opens in black.
            SetCurve(clip, "Content/Opening/Title", typeof(CardFade), "m_Alpha",
                     (0f, 0f), (k_TitleUp, 1f), (k_TitleOutAt, 1f), (k_TitleOutAt + k_TitleOut, 0f));
            SetCurve(clip, "Content/Opening/Line", typeof(CardFade), "m_Alpha",
                     (0f, 0f), (k_LineInAt, 0f), (k_LineInAt + k_LineUp, 1f),
                     (k_LineOutAt, 1f), (k_LineOutAt + k_LineOut, 0f));

            // The white light the players arrive in: off while the words are up, then full, then
            // out. It used to start at full, which is what reading the beat as beginning with the
            // room rather than with the title would give you.
            SetCurve(clip, "Content/Centre/Lamp", typeof(Light), "m_Intensity",
                     (0f, 0f), (k_LightUpAt, 0f), (k_Open, k_CentreLamp),
                     (centreStart, k_CentreLamp), (centreEnd, 0f));
            SetCurve(clip, "Content/Centre/Beam", typeof(VolumetricSpot), "m_Reveal",
                     (0f, 0f), (k_LightUpAt, 0f), (k_Open, 1f),
                     (centreStart, 1f), (centreEnd, 0f));

            // The two 光圈: dark, then up. Lamp and haze move together -- a 光圈 whose air fades in
            // over a figure already lit in that colour arrives backwards.
            foreach (string figure in new[] { "Human A", "Human B" })
            {
                SetCurve(clip, $"Content/{figure}/Lamp", typeof(Light), "m_Intensity",
                         (0f, 0f), (colourStart, 0f), (colourEnd, k_ColourLamp));
                SetCurve(clip, $"Content/{figure}/Beam", typeof(VolumetricSpot), "m_Reveal",
                         (0f, 0f), (colourStart, 0f), (colourEnd, 1f));
            }

            if (isNew) AssetDatabase.AddObjectToAsset(clip, timeline);
            EditorUtility.SetDirty(clip);
            Debug.Log($"[Timeline] clip '{clip.name}': 8 curves.");
            return clip;
        }

        /// <summary>
        /// Keys with FLAT tangents, which is an ease in and out at every one of them.
        ///
        /// Linear would be wrong here in a way that is easy to miss on a monitor and obvious in a
        /// headset: a light that starts and stops rising instantly has a visible corner, and the
        /// eye reads the corner as the cue rather than the change.
        ///
        /// Tangents set to zero by hand rather than through AnimationCurve.SmoothTangents, which
        /// is a trap at exactly this shape. SmoothTangents averages the slopes of the NEIGHBOURS,
        /// so on a hold-then-fall -- 24 at t=0, 24 at t=7, 0 at t=12 -- the middle key inherits
        /// the slope of the whole span and the curve bulges ABOVE the hold on its way there. It
        /// measured 26.45 on a lamp authored at 24, and 1.10 on a reveal that saturates at 1.
        /// </summary>
        static void SetCurve(AnimationClip clip, string path, System.Type type, string property,
                             params (float t, float v)[] keys)
        {
            var frames = new Keyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                frames[i] = new Keyframe(keys[i].t, keys[i].v, 0f, 0f);
            clip.SetCurve(path, type, property, new AnimationCurve(frames));
        }

        static AnimationTrack BuildTrack(TimelineAsset timeline, AnimationClip clip)
        {
            AnimationTrack track = null;
            foreach (TrackAsset existing in timeline.GetOutputTracks())
            {
                if (existing is AnimationTrack a && a.name == k_TrackName) track = a;
            }

            if (track == null)
            {
                track = timeline.CreateTrack<AnimationTrack>(null, k_TrackName);
            }
            else
            {
                foreach (TimelineClip old in new System.Collections.Generic.List<TimelineClip>(track.GetClips()))
                    timeline.DeleteClip(old);
            }

            if (track == null)
            {
                Debug.LogError("[Timeline] RESULT: FAIL - could not create the animation track.");
                return null;
            }

            TimelineClip timelineClip = track.CreateClip(clip);
            timelineClip.start = 0d;
            timelineClip.duration = k_Open + k_Hold + k_CentreDelay + k_CentreDown + k_Tail;
            timelineClip.displayName = k_ClipName;

            Debug.Log($"[Timeline] track '{track.name}', clip 0-{timelineClip.duration}s.");
            return track;
        }

        /// <summary>
        /// Mute animation tracks this builder did not author, so two tracks are not writing
        /// m_Dissolve at once.
        ///
        /// THERE WAS ONE, AND IT WAS FIGHTING THIS. A track called "Animation Track" carried a
        /// recorded infinite clip with m_Dissolve on both 真人 -- 1 at twenty seconds and 0 at
        /// twenty-five, with both ends clamped, so the figures were FULLY DISSOLVED from the
        /// first frame of the show and came back five seconds later.
        ///
        /// MUTED, NOT DELETED, which is the correction. An earlier version of this deleted it
        /// and destroyed its clip, on the reasoning that the recording was broken and had been
        /// superseded. Both halves of that were true and neither is this tool's call to make:
        /// somebody sat down and recorded those curves, and a builder that quietly throws away
        /// hand work is a builder nobody can afford to run. Muted, it is still there, still
        /// visible in the Timeline window, and one click from being looked at again.
        ///
        /// By name, because that is what distinguishes authored from recorded here: a track added
        /// by dragging a component into the Timeline window is called "Animation Track".
        /// </summary>
        static void MuteStrayAnimationTracks(TimelineAsset timeline)
        {
            var mine = new System.Collections.Generic.HashSet<string>
            {
                k_TrackName, k_HandoverTrackName, k_BodyTrackName, k_ArriveTrackName
            };

            foreach (TrackAsset candidate in
                     new System.Collections.Generic.List<TrackAsset>(timeline.GetRootTracks()))
            {
                if (candidate is not AnimationTrack || mine.Contains(candidate.name)) continue;
                if (candidate.muted) continue;

                candidate.muted = true;
                EditorUtility.SetDirty(candidate);

                Debug.LogWarning($"[Timeline] MUTED the hand-recorded track '{candidate.name}' " +
                                 "-- the handover below writes m_Dissolve too, and two tracks " +
                                 "on it would layer over each other. Nothing was deleted: " +
                                 "unmute it in the Timeline window if it is the one you want.");
            }
        }

        /// <summary>
        /// The handover: the 真人 leaving, and then the player's own body being told to arrive.
        ///
        /// ONE CLIP ON EACH OF TWO TRACKS, each one draggable on its own. The first ramps
        /// <c>ScanHandover.m_Progress</c> from nothing to all of it; the second crosses
        /// <c>O0TouchAwakening.m_Cue</c> past a half, which is the latch that fires the plates,
        /// the hands and the body.
        ///
        /// THEY USED TO SHARE A TRACK, and that was right while the body waited for the room to
        /// empty: one row, two clips, a sequence you could read at a glance. The cue now starts
        /// inside the dissolve, and two clips that overlap on one animation track are blended
        /// rather than played -- see <see cref="k_BodyTrackName"/> for what that looked like.
        /// They are bound to the same Animator, which is not a blend: the two write different
        /// components, and a property only blends against another clip driving the same one.
        ///
        /// EACH CLIP IS EXACTLY AS LONG AS ITS CUE, which is only safe because both components
        /// latch. An animation track reverts the properties it drives to their authored values
        /// the moment its clip ends, so a dissolve clip that stopped at 44 s would walk the 真人
        /// straight back into the room; ScanHandover holds its end state and O0TouchAwakening
        /// latches on the first crossing, so neither can be undone by the track letting go. That
        /// is what lets these be two short clips you can drag and stretch rather than two long
        /// ones that have to reach the end of the beat.
        /// </summary>
        static AnimationTrack[] BuildHandoverTracks(TimelineAsset timeline)
        {
            AnimationTrack arriveTrack = Row(timeline, k_ArriveTrackName);
            AnimationTrack track = Row(timeline, k_HandoverTrackName);
            AnimationTrack bodyTrack = Row(timeline, k_BodyTrackName);
            if (arriveTrack == null || track == null || bodyTrack == null) return null;

            // THE CLIP HOLDS THEM IN THE ROOM ITSELF: it ramps over its first seven seconds and
            // then carries a flat 1 all the way to the end of the dissolve, so it is about eighty
            // seconds of clip doing seven seconds of work.
            //
            // WHY NOT EXTRAPOLATION, WHICH IS WHAT THIS WANTS. TimelineClip.preExtrapolationMode
            // and postExtrapolationMode are read-only in this version of com.unity.timeline --
            // the Timeline window sets them, and nothing public does. Reaching past that with
            // reflection to save eighty seconds of flat curve is not worth owning.
            //
            // WHY NOT A LATCH, which is what the dissolve does. ScanHandover's latch is play-mode
            // only on purpose, so that scrubbing shows the 真人 coming back; a latched arrival
            // would be stuck at 1 the moment anyone scrubbed past 27s and the authoring scene
            // would stop telling the truth about the top of the beat.
            //
            // WHAT STAYS DRAGGABLE is the thing worth dragging: the ramp is at the clip's head,
            // so moving the clip moves when they arrive, and stretching its end only lengthens
            // the hold. The curve is authored to the clip's own length, so a stretched clip is
            // re-cut on the next run rather than being replayed at a new speed.
            float arriveFor = s_ScansOutAt + k_ScansOut - k_FirstLinesAt;
            AnimationClip arrive = SubClip(timeline, k_ScansInClipName);
            SetCurve(arrive, k_HandoverPath, typeof(ScanHandover), "m_Arrival",
                     (0f, 0f), (k_ScansIn, 1f), (arriveFor, 1f));

            TimelineClip arriveClip = arriveTrack.CreateClip(arrive);
            arriveClip.start = k_FirstLinesAt;
            arriveClip.duration = arriveFor;
            arriveClip.displayName = "真人 arrive";

            // AND IT RUNS TO THE END OF THE DISSOLVE, not to its start. Were it to stop at 109s
            // the track would let go of m_Arrival the moment the dissolve began, the property
            // would fall back to the prefab's authored 0, and the 真人 would blink out whole
            // instead of dissolving over ten seconds. Ending with the dissolve means the one
            // frame where it lets go is a frame on which they are already gone -- and an empty
            // authored state is then the right thing for every beat after this one.

            AnimationClip scans = SubClip(timeline, k_ScansClipName);
            SetCurve(scans, k_HandoverPath, typeof(ScanHandover), "m_Progress",
                     (0f, 0f), (k_ScansOut, 1f));

            TimelineClip scansClip = track.CreateClip(scans);
            scansClip.start = s_ScansOutAt;
            scansClip.duration = k_ScansOut;
            scansClip.displayName = "真人 dissolve";

            AnimationClip body = SubClip(timeline, k_BodyClipName);
            SetCurve(body, k_AwakeningPath, typeof(O0TouchAwakening), "m_Cue",
                     (0f, 0f), (k_BodyCue, 1f));

            TimelineClip bodyClip = bodyTrack.CreateClip(body);
            bodyClip.start = s_BodyAt;
            bodyClip.duration = k_BodyCue;
            bodyClip.displayName = "Body arrives";

            Debug.Log($"[Timeline] track '{arriveTrack.name}': 真人 arrive " +
                      $"{k_FirstLinesAt}-{k_FirstLinesAt + k_ScansIn}s, then held to " +
                      $"{k_FirstLinesAt + arriveFor}s | " +
                      $"track '{track.name}': 真人 dissolve " +
                      $"{s_ScansOutAt}-{s_ScansOutAt + k_ScansOut}s | track " +
                      $"'{bodyTrack.name}': body cue at {s_BodyAt:F2}s.");
            return new[] { arriveTrack, track, bodyTrack };
        }

        /// <summary>
        /// One animation row, found by name and emptied, so a re-run replaces its clips instead
        /// of adding to them and the director's binding -- which keys off the track object --
        /// survives.
        /// </summary>
        static AnimationTrack Row(TimelineAsset timeline, string name)
        {
            AnimationTrack track = null;
            foreach (TrackAsset existing in timeline.GetOutputTracks())
            {
                if (existing is AnimationTrack a && a.name == name) track = a;
            }

            if (track == null) track = timeline.CreateTrack<AnimationTrack>(null, name);
            if (track == null)
            {
                Debug.LogError($"[Timeline] RESULT: FAIL - could not create the track '{name}'.");
                return null;
            }

            foreach (TimelineClip old in
                     new System.Collections.Generic.List<TimelineClip>(track.GetClips()))
                timeline.DeleteClip(old);

            return track;
        }

        /// <summary>
        /// An animation clip kept inside the timeline asset, reused across runs so the Timeline
        /// window does not accumulate orphans and so a clip someone has re-shaped by hand keeps
        /// its identity.
        /// </summary>
        static AnimationClip SubClip(TimelineAsset timeline, string name)
        {
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(k_Timeline))
            {
                if (sub is AnimationClip existing && existing.name == name)
                {
                    existing.ClearCurves();
                    existing.frameRate = 60f;
                    EditorUtility.SetDirty(existing);
                    return existing;
                }
            }

            var clip = new AnimationClip { name = name, frameRate = 60f };
            AssetDatabase.AddObjectToAsset(clip, timeline);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>
        /// Give the authoring scene's own director a binding, so the Timeline window actually
        /// scrubs something.
        ///
        /// O_0.unity has a loose Timeline object with a PlayableDirector pointing at this asset
        /// and no bindings at all -- which is the director anyone would click on, and which
        /// animates nothing when scrubbed. The timeline looks broken when it is not.
        ///
        /// Bound rather than deleted. The prefab's own director is Manual and is only driven by
        /// O0ArrivalBeat once the beat is active, and nothing activates a beat in an authoring
        /// scene -- so it never evaluates there and the two cannot fight. That leaves the scene's
        /// director free to be exactly what it looks like: the preview handle.
        /// </summary>
        static void BindAuthoringScene()
        {
            // ONBOARDING.UNITY, NOT O_0.UNITY. The beats were merged and the old authoring scene
            // went with them; this still opened it, and OpenScene THROWS on a path that is not
            // there rather than returning an invalid scene -- so the builder died here, after
            // writing everything, with "Scene file not found" and no RESULT line at all.
            const string k_Scene = "Assets/_Scenes/Onboarding.unity";

            if (!System.IO.File.Exists(k_Scene))
            {
                Debug.LogWarning($"[Timeline] no {k_Scene}; the preview director is not bound. " +
                                 "Run Tools/Onboarding/Build Onboarding to make the authoring " +
                                 "scene again.");
                return;
            }

            // Reloaded from the path rather than passed in. Run() reimports the timeline asset
            // before this point, and a ForceSynchronousImport can replace the loaded instance --
            // so a reference captured earlier compares false against the very asset it came
            // from. The director was found and rejected for being "a different timeline".
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            AnimationTrack track = null;
            AnimationTrack handover = null;
            AnimationTrack bodyCue = null;
            AnimationTrack arrive = null;
            foreach (TrackAsset candidate in timeline.GetOutputTracks())
            {
                if (candidate is not AnimationTrack a) continue;
                if (a.name == k_TrackName) track = a;
                else if (a.name == k_HandoverTrackName) handover = a;
                else if (a.name == k_BodyTrackName) bodyCue = a;
                else if (a.name == k_ArriveTrackName) arrive = a;
            }
            if (track == null)
            {
                Debug.LogWarning("[Timeline] preview director not bound: no animation track.");
                return;
            }

            UnityEngine.SceneManagement.Scene scene =
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    k_Scene, UnityEditor.SceneManagement.OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogWarning($"[Timeline] could not open {k_Scene}; preview director not bound.");
                return;
            }

            PlayableDirector loose = null;
            Animator animator = null;

            // Searched through the whole hierarchy, not across roots. The hand-made Timeline
            // object is parented UNDER the O-0 prefab instance, so a root-only search found
            // nothing and reported that the scene had no director in it at all.
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (var director in root.GetComponentsInChildren<PlayableDirector>(true))
                {
                    if (director.playableAsset == null) continue;
                    if (AssetDatabase.GetAssetPath(director.playableAsset) != k_Timeline) continue;
                    // Skip the prefab's own director: its bindings live inside the asset, and
                    // writing them again from the scene would only create a prefab override. The
                    // hand-made one is a scene object with no counterpart in any prefab.
                    //
                    // Not "skip anything already bound", which is what this said first -- on the
                    // second run the loose director was bound to the animation track, got skipped
                    // as finished, and never received the audio binding that had appeared since.
                    if (PrefabUtility.GetCorrespondingObjectFromSource(director) != null) continue;
                    loose = director;
                }

                var beat = root.GetComponentInChildren<O0ArrivalBeat>(true);
                if (beat != null) animator = beat.GetComponent<Animator>();
            }

            if (loose == null || animator == null)
            {
                // Say which half is missing. "Nothing to bind" was true and useless.
                Debug.LogWarning($"[Timeline] preview director not bound: " +
                                 $"{(loose == null ? "no PlayableDirector on a root pointing at this timeline" : "director found")}, " +
                                 $"{(animator == null ? "no Animator on an O0ArrivalBeat in the scene" : "animator found")}.");
                return;
            }

            // Renamed, because the prefab now contributes a child called Timeline as well and two
            // siblings with the same name under the same parent is a guessing game.
            if (loose.gameObject.name == "Timeline") loose.gameObject.name = "Timeline (Preview)";

            loose.SetGenericBinding(track, animator);

            // And the handover, or scrubbing past the cue in the authoring scene leaves the 真人
            // standing there and the track looks like it failed to build.
            if (handover != null) loose.SetGenericBinding(handover, animator);
            if (bodyCue != null) loose.SetGenericBinding(bodyCue, animator);
            if (arrive != null) loose.SetGenericBinding(arrive, animator);

            // The tone as well, or scrubbing past 8 s in the authoring scene is silent and the
            // audio track looks like it failed to build.
            AudioTrack audio = null;
            foreach (TrackAsset candidate in timeline.GetOutputTracks())
            {
                if (candidate is AudioTrack a && a.name == k_AudioTrackName) audio = a;
            }
            AudioSource speaker = animator.transform.Find("Content/Opening/Tone")?.GetComponent<AudioSource>();
            if (audio != null && speaker != null) loose.SetGenericBinding(audio, speaker);
            // Hold rather than loop, so scrubbing past the end leaves the room in the state the
            // beat actually ends in instead of snapping back to one white light.
            loose.extrapolationMode = DirectorWrapMode.Hold;

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, k_Scene);
            Debug.Log($"[Timeline] bound the preview director in {k_Scene} to O-0's Animator.");
        }

        /// <summary>
        /// Drive the finished timeline and read the fixtures back.
        ///
        /// A wired director and a populated track prove nothing on their own: a binding that
        /// resolves to the wrong Animator, or a curve path that no longer matches the hierarchy,
        /// leaves a timeline that looks right in the window and moves nothing. The only check
        /// worth having is whether the lights actually change.
        /// </summary>
        static bool Verify(Deck[] decks)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(k_Prefab);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                // The beat ships with its content switched off; BeatController turns it on. The
                // cue has to be checked against the state it actually runs in.
                Transform content = instance.transform.Find("Content");
                if (content != null) content.gameObject.SetActive(true);

                var director = instance.GetComponentInChildren<PlayableDirector>(true);
                Light centreLamp = Find<Light>(instance, "Content/Centre/Lamp");
                Light aLamp = Find<Light>(instance, "Content/Human A/Lamp");
                var centreBeam = Find<VolumetricSpot>(instance, "Content/Centre/Beam");
                var aBeam = Find<VolumetricSpot>(instance, "Content/Human A/Beam");
                var title = Find<CardFade>(instance, "Content/Opening/Title");
                var line = Find<CardFade>(instance, "Content/Opening/Line");

                if (director == null || centreLamp == null || aLamp == null ||
                    centreBeam == null || aBeam == null || title == null || line == null)
                {
                    Debug.LogError("[Timeline] RESULT: FAIL - could not find the director, the " +
                                   "fixtures or the opening cards inside the prefab instance.");
                    return false;
                }

                float end = k_Open + k_Hold + k_CentreDelay + k_CentreDown;
                float colourAt = k_Open + k_Hold;

                bool ok = true;
                foreach (float t in new[] { 0f, k_TitleUp, k_TitleOutAt + k_TitleOut,
                                            k_LineInAt + k_LineUp, k_Open, colourAt, end })
                {
                    director.time = t;
                    director.Evaluate();
                    Debug.Log($"[Timeline] t={t,5:F1}s  title {title.alpha:F2} line {line.alpha:F2}" +
                              $"   |   white {centreLamp.intensity,6:F2}/{centreBeam.reveal:F2}" +
                              $"   |   A {aLamp.intensity,5:F2}/{aBeam.reveal:F2}");

                    // Nothing may exceed what was authored. Interpolation that overshoots a hold
                    // is invisible in the Timeline window and visible in the room as a lamp that
                    // brightens before it goes out.
                    if (centreLamp.intensity > k_CentreLamp * 1.001f ||
                        aLamp.intensity > k_ColourLamp * 1.001f ||
                        centreBeam.reveal > 1.001f || aBeam.reveal > 1.001f ||
                        title.alpha > 1.001f || line.alpha > 1.001f)
                    {
                        Debug.LogError($"[Timeline] RESULT: FAIL - overshoot at t={t:F1}s.");
                        ok = false;
                    }
                }

                // 「觀眾一戴上VR，看到：合成肉身」 -- the piece opens on the title in black. Nothing
                // else may be lit at t=0, which is the assertion that would have caught the beat
                // still starting with the room up.
                director.time = 0d; director.Evaluate();
                if (centreLamp.intensity > 0.01f || aBeam.reveal > 0.01f ||
                    title.alpha > 0.01f || line.alpha > 0.01f)
                {
                    Debug.LogError("[Timeline] RESULT: FAIL - the beat does not open in black.");
                    ok = false;
                }

                director.time = k_TitleUp; director.Evaluate();
                if (title.alpha < 0.99f)
                {
                    Debug.LogError("[Timeline] RESULT: FAIL - the title never reaches full.");
                    ok = false;
                }

                director.time = k_LineInAt + k_LineUp; director.Evaluate();
                if (line.alpha < 0.99f)
                {
                    Debug.LogError("[Timeline] RESULT: FAIL - the line never reaches full.");
                    ok = false;
                }

                // The room is up and the words are gone at the same moment, which is the overlap
                // the opening is built around.
                director.time = k_Open; director.Evaluate();
                if (centreLamp.intensity < k_CentreLamp * 0.99f || line.alpha > 0.01f ||
                    aBeam.reveal > 0.01f)
                {
                    Debug.LogError("[Timeline] RESULT: FAIL - at the open the white light is not " +
                                   "full, or the line is still up, or a 光圈 is already lit.");
                    ok = false;
                }

                director.time = end; director.Evaluate();
                if (centreLamp.intensity > 0.01f || aBeam.reveal < 0.99f)
                {
                    Debug.LogError("[Timeline] RESULT: FAIL - at the end the white light has not " +
                                   "gone out, or the 光圈 have not come up.");
                    ok = false;
                }

                ok &= VerifyHandover(instance, director);
                ok &= VerifyNarration(director, decks);

                if (ok) Debug.Log("[Timeline] OK   the opening, the cue and the handover drive " +
                                  "everything.");
                return ok;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Put the two components the handover drives into the state the clips assume.
        ///
        /// THE SCAN CURVE IS FLATTENED TO A STRAIGHT RAMP. It was authored as 0 to 1 by 0.7 and
        /// held, which made sense while a second curve was fading an object up underneath it --
        /// the knee was the overlap. The object is O-1's now, so the knee is left over from an
        /// arrangement that no longer exists, and it would quietly make the dissolve finish at
        /// seven tenths of whatever length the clip is dragged to. A straight ramp is what makes
        /// "the clip is as long as the dissolve" true.
        ///
        /// AND THE TOUCH STANDS DOWN. The beat is fired from the timeline; leaving the hand poll
        /// armed as well would mean a player who reached out early got the arrival twice over --
        /// once from their hand, once from the cue. It is a checkbox, for the authoring scene.
        /// </summary>
        static bool PrepareHandover(Transform handoverOn, Transform awakeningOn)
        {
            var handover = handoverOn.GetComponent<ScanHandover>();
            var awakening = awakeningOn.GetComponent<O0TouchAwakening>();

            if (handover == null || awakening == null)
            {
                Debug.LogError("[Timeline] RESULT: FAIL - the prefab has no " +
                               $"{(handover == null ? $"ScanHandover on '{k_HandoverPath}'" : $"O0TouchAwakening on '{k_AwakeningPath}'")}.");
                return false;
            }

            var hso = new SerializedObject(handover);
            hso.FindProperty("m_Progress").floatValue = 0f;
            hso.FindProperty("m_ScanCurve").animationCurveValue = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

            // AUTHORED AT AN EMPTY ROOM. The timeline brings the 真人 in, so the prefab must not:
            // left at 1, they would be standing there in the editor, in any still taken without
            // a director, and for the first frame of play before the clip's extrapolation takes
            // hold. Straightened for the same reason as the curve above.
            hso.FindProperty("m_Arrival").floatValue = 0f;
            hso.FindProperty("m_ArrivalCurve").animationCurveValue = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));
            hso.ApplyModifiedPropertiesWithoutUndo();

            var aso = new SerializedObject(awakening);
            aso.FindProperty("m_Cue").floatValue = 0f;
            aso.FindProperty("m_RequireTouch").boolValue = false;
            aso.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[Timeline] handover prepared: both curves straightened, an empty room " +
                      "and the cue at 0, the touch poll off -- O-0's timeline brings the 真人 in, " +
                      "takes them away and fires the arrival.");
            return true;
        }

        /// <summary>
        /// Drive the handover and read the two values it writes back.
        ///
        /// The one check worth having, because an animation path is a string: the whole beat now
        /// depends on two of them, and a path that no longer matches the hierarchy leaves a
        /// timeline that looks right in the window, animates nothing, and opens the show on two
        /// 真人 that never leave and a player with no body.
        ///
        /// The cue is read off the serialized field rather than through a property, because
        /// nothing exposes it: it exists to be animated and latched in LateUpdate, which does
        /// not run while a director is merely evaluated.
        /// </summary>
        static bool VerifyHandover(GameObject instance, PlayableDirector director)
        {
            var handover = instance.GetComponentInChildren<ScanHandover>(true);
            var awakening = instance.GetComponentInChildren<O0TouchAwakening>(true);

            if (handover == null || awakening == null)
            {
                Debug.LogError("[Timeline] RESULT: FAIL - the prefab instance has no " +
                               $"{(handover == null ? "ScanHandover" : "O0TouchAwakening")}, so " +
                               "the handover curves have nothing to write to.");
                return false;
            }

            var so = new SerializedObject(awakening);
            SerializedProperty cue = so.FindProperty("m_Cue");
            if (cue == null)
            {
                Debug.LogError("[Timeline] RESULT: FAIL - O0TouchAwakening has no m_Cue field; " +
                               "the body would never be told to arrive.");
                return false;
            }

            bool ok = true;

            // THE ARRIVAL, at four times. The first is the one 「SCAN 一開始就在」 was about: an
            // empty room before the clip. The last is the one the long flat tail exists for -- a
            // clip that stopped at its ramp would leave the 真人 out of the room for the whole of
            // the 自白 they are supposed to be standing through, and nothing in the Timeline
            // window would show it.
            foreach ((float t, float expected) in new[]
                     {
                         (0f, 0f),
                         (k_FirstLinesAt, 0f),
                         (k_FirstLinesAt + k_ScansIn, 1f),
                         (s_ScansOutAt, 1f),
                     })
            {
                director.time = t;
                director.Evaluate();

                float read = handover.arrival;
                Debug.Log($"[Timeline] t={t,6:F2}s  真人 arrival {read:F2}");

                if (Mathf.Abs(read - expected) > 0.05f)
                {
                    Debug.LogError($"[Timeline] RESULT: FAIL - at t={t:F2}s the 真人 arrival reads " +
                                   $"{read:F2}, not {expected:F2}. " + (expected > 0.5f
                                       ? "The clip is not holding past its end, so the 真人 are " +
                                         "not in the room for their own 自白."
                                       : "Something has the 真人 in the room before they arrive."));
                    ok = false;
                }
            }

            // THE DISSOLVE IS ONLY CHECKED WHERE ITS CURVE IS PINNED. SetCurve smooths its
            // keys, so the ramp is an S and not a line: a quarter of the way along it reads 0.04
            // and not 0.125, which is right in the window and looks like a broken track in a
            // test. NaN means "sample it and print it, but do not claim to know the number" --
            // used at the two cue times, which are wherever the pacing has put them.
            //
            // IN TIME ORDER, and the cue's state is derived from the time rather than typed,
            // because the cue now fires inside the dissolve. Typing it was what asserted the
            // cue must be down at 114s -- true only while the body waited for the empty room.
            foreach ((float t, float progress) in new[]
                     {
                         (s_ScansOutAt, 0f),
                         (s_BodyAt, float.NaN),
                         (s_BodyAt + k_BodyCue, float.NaN),
                         (s_ScansOutAt + k_ScansOut * 0.5f, 0.5f),
                         (s_ScansOutAt + k_ScansOut, 1f),
                     })
            {
                director.time = t;
                director.Evaluate();

                // Update, because a SerializedObject caches: without it every read after the
                // first would report the value the field had before the director was moved.
                so.Update();

                float read = handover.progress;
                float cueRead = cue.floatValue;
                bool fired = t >= s_BodyAt + k_BodyCue - 0.001f;

                Debug.Log($"[Timeline] t={t,6:F2}s  真人 dissolve {read:F2}   |   body cue " +
                          $"{cueRead:F2}{(fired ? "  (should be up)" : "")}");

                if (!float.IsNaN(progress) && Mathf.Abs(read - progress) > 0.05f)
                {
                    Debug.LogError($"[Timeline] RESULT: FAIL - at t={t:F2}s the 真人 dissolve " +
                                   $"reads {read:F2}, not {progress:F2}.");
                    ok = false;
                }

                if (fired && cueRead < 0.5f)
                {
                    Debug.LogError($"[Timeline] RESULT: FAIL - at t={t:F2}s the body cue reads " +
                                   $"{cueRead:F2}; it has to cross a half or the body never arrives.");
                    ok = false;
                }

                if (!fired && cueRead >= 0.5f)
                {
                    Debug.LogError($"[Timeline] RESULT: FAIL - the body cue is already up at " +
                                   $"t={t:F2}s, before its own clip has crossed a half at " +
                                   $"{s_BodyAt + k_BodyCue:F2}s. Something else is driving m_Cue.");
                    ok = false;
                }
            }

            return ok;
        }

        // ------------------------------------------------------------------ the two 自白

        /// <summary>
        /// Load a line, and say so rather than throwing when it is not there. A missing wav is
        /// the normal state of this project halfway through a recording session, and a builder
        /// that refuses to run leaves the whole beat unauthored over one file.
        /// </summary>
        static AudioClip Line(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogWarning($"[Timeline] no audio clip at {path}. That line is left off the " +
                                 "timeline, and the pacing around it is a guess until it lands.");
            }

            return clip;
        }

        /// <summary>
        /// Cut the handover to the lines: where the scans start going, and when the body has to
        /// be told to arrive for it to finish on the player's last word.
        ///
        /// THE BEAT IS PINNED AT BOTH ENDS NOW. The first pair starts at 20s and the second pair
        /// ends at 140s, both of them typed; what is computed is the handover in between. The
        /// cue runs BACKWARDS from the end of the second pair, which is the one thing here that
        /// is easy to get wrong forwards: the body is not arrived when its cue fires. The cue is
        /// a half-second ramp that merely tells <see cref="O0TouchAwakening"/> to call Form(),
        /// latching when the ramp crosses a half, and AvatarFormation then takes its own
        /// <see cref="k_BodySeconds"/> to bring the body in. So the cue belongs a whole formation
        /// plus half a ramp BEFORE the last word, nowhere near it.
        ///
        /// 「出現完後撥放第二段」 was the old rule and is deliberately no longer it: the body arrives
        /// under the second pair and lands on its last word. What is still checked is the order
        /// inside the picture -- the Ch36 body may not start fading in while a 真人 is still
        /// standing there -- because that is the part an audience can see.
        /// </summary>
        static void Retime()
        {
            AudioClip a1 = Line(k_LineA1);
            AudioClip b1 = Line(k_LineB1);
            AudioClip a2 = Line(k_LineA2);
            AudioClip b2 = Line(k_LineB2);

            s_SecondLinesAt = k_SecondLinesAt;

            if (a1 == null || b1 == null)
            {
                s_ScansOutAt = k_ScansOutAtFallback;
                s_BodyAt = k_BodyAtFallback;
                Debug.LogWarning("[Timeline] the first pair of lines is incomplete, so the " +
                                 $"handover stays at its old fixed times ({s_ScansOutAt}s / " +
                                 $"{s_BodyAt}s) rather than being cut to audio that is not there.");
                return;
            }

            float lastWord = k_FirstLinesAt + Mathf.Max(a1.length, b1.length);
            s_ScansOutAt = lastWord + k_LinesTail;

            if (a2 == null || b2 == null)
            {
                // Nothing to run back from, so the body goes back to following the dissolve.
                s_BodyAt = s_ScansOutAt + k_ScansOut + 1f;
                Debug.LogWarning("[Timeline] the second pair of lines is incomplete, so the body " +
                                 $"cue follows the dissolve at {s_BodyAt:F1}s rather than being " +
                                 "cut to land on a last word that is not there yet.");
                return;
            }

            // BACKWARDS FROM THE LAST WORD. The half-ramp is the quarter-second between the cue
            // clip starting and m_Cue crossing a half, which is when Form() is actually called --
            // small, and the whole point of this line is that the body is not still arriving
            // after the room has gone quiet.
            float ends = s_SecondLinesAt + Mathf.Max(a2.length, b2.length);
            s_BodyAt = ends - k_BodySeconds - k_BodyCue * 0.5f;

            float bodyShows = s_BodyAt + k_BodyCue * 0.5f + k_BodySeconds * k_MembraneShare;
            float roomEmpty = s_ScansOutAt + k_ScansOut;

            if (s_SecondLinesAt < lastWord)
                Debug.LogError($"[Timeline] RESULT: FAIL - 自白 2 starts at {s_SecondLinesAt:F1}s " +
                               $"but 自白 1 runs to {lastWord:F1}s, so a player talks over " +
                               "themselves. Move k_SecondLinesAt later.");

            if (bodyShows < roomEmpty)
                Debug.LogWarning($"[Timeline] the Ch36 body starts showing at {bodyShows:F1}s but " +
                                 $"a 真人 is still there until {roomEmpty:F1}s -- the player gets " +
                                 "a body in a room that has not emptied. Move 自白 2 later " +
                                 "(k_SecondLinesAt) or shorten the formation.");

            Debug.Log($"[Timeline] 自白 1 from {k_FirstLinesAt}s (A {a1.length:F1}s, " +
                      $"B {b1.length:F1}s) -> last word at {lastWord:F1}s; 真人 dissolve " +
                      $"{s_ScansOutAt:F1}-{roomEmpty:F1}s; 自白 2 {s_SecondLinesAt:F1}-" +
                      $"{ends:F1}s; body cue {s_BodyAt:F2}s -> membrane first, Ch36 body from " +
                      $"{bodyShows:F1}s, arrived {ends:F1}s on the last word.");
        }

        /// <summary>
        /// One AudioTrack per player, two clips on each: the line before the scans go, and the
        /// line after the body has arrived.
        ///
        /// TWO CLIPS ON ONE TRACK RATHER THAN FOUR TRACKS, so a player's whole account is one row
        /// in the Timeline window and the silence between their two lines is a gap you can see
        /// and drag. It is also what keeps the role decision down to one AudioSource per player:
        /// a second track for the same player would want a second source, a second RoleAudible,
        /// and a second place to get the role wrong.
        ///
        /// Re-runnable: the tracks are found by name and only their clips replaced, so the asset
        /// keeps its track objects and the director's bindings survive. Bindings key off the
        /// track object and not its name -- renaming a track in the Timeline window cannot break
        /// them, but deleting and recreating one would.
        /// </summary>
        static Deck[] BuildNarrationTracks(TimelineAsset timeline)
        {
            var decks = new[]
            {
                new Deck
                {
                    role = PlayerRole.Player1, label = "A", trackName = k_NarrationATrack,
                    speaker = k_SpeakerA, first = k_LineA1, second = k_LineA2,
                },
                new Deck
                {
                    role = PlayerRole.Player2, label = "B", trackName = k_NarrationBTrack,
                    speaker = k_SpeakerB, first = k_LineB1, second = k_LineB2,
                },
            };

            for (int i = 0; i < decks.Length; i++)
            {
                AudioTrack track = null;
                foreach (TrackAsset existing in timeline.GetOutputTracks())
                {
                    if (existing is AudioTrack a && a.name == decks[i].trackName) track = a;
                }

                if (track == null)
                    track = timeline.CreateTrack<AudioTrack>(null, decks[i].trackName);

                if (track == null)
                {
                    Debug.LogError("[Timeline] RESULT: FAIL - could not create the track " +
                                   $"'{decks[i].trackName}'.");
                    return null;
                }

                foreach (TimelineClip old in
                         new System.Collections.Generic.List<TimelineClip>(track.GetClips()))
                    timeline.DeleteClip(old);

                AddLine(track, decks[i].first, k_FirstLinesAt, $"O-0-{decks[i].label}1");
                AddLine(track, decks[i].second, s_SecondLinesAt, $"O-0-{decks[i].label}2");

                decks[i].track = track;
            }

            return decks;
        }

        /// <summary>
        /// One line on a deck, as long as the wav is. The clip is sized to the audio rather than
        /// to a typed number, so a clip can never be shorter than the sentence inside it -- which
        /// cuts a line off mid-word and is close to invisible in the Timeline window.
        /// </summary>
        static void AddLine(AudioTrack track, string path, float at, string label)
        {
            AudioClip audio = Line(path);
            if (audio == null) return;

            TimelineClip clip = track.CreateClip<AudioPlayableAsset>();
            ((AudioPlayableAsset)clip.asset).clip = audio;
            clip.start = at;
            clip.duration = audio.length;
            clip.displayName = label;

            Debug.Log($"[Timeline] {track.name}: {label} ({audio.name}) " +
                      $"{at:F1}-{at + audio.length:F1}s.");
        }

        /// <summary>
        /// A speaker of its own for each player's lines, beside the cue tone.
        ///
        /// NOT THE TONE'S SOURCE. RoleAudible mutes a whole AudioSource, so sharing one with the
        /// 提示音 would silence the cue for whichever player the deck is not for. One source per
        /// deck is what makes "mute everything on this source" a safe thing to say.
        ///
        /// 2D, like the tone and like O-1's speaker: this is a player's own account of what
        /// happened and not a voice standing somewhere in the room, and a positioned one would
        /// arrive from a different direction for each of the two people in it.
        /// </summary>
        static AudioSource EnsureSpeaker(Transform opening, string name)
        {
            Transform speaker = opening.Find(name);
            if (speaker == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(opening, false);
                speaker = go.transform;
                Debug.Log($"[Timeline] created '{name}' in the prefab.");
            }

            var source = speaker.GetComponent<AudioSource>();
            if (source == null) source = speaker.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            return source;
        }

        /// <summary>
        /// The role gate. Written through SerializedObject rather than a public setter because
        /// the field is private and serialized, and a prefab edit has to go through the
        /// serialization system to survive the save.
        /// </summary>
        static void EnsureRoleAudible(AudioSource source, PlayerRole role, string label)
        {
            var presenter = source.GetComponent<RoleAudible>();
            if (presenter == null)
            {
                presenter = source.gameObject.AddComponent<RoleAudible>();
                Debug.Log($"[Timeline] added RoleAudible to '{source.name}'.");
            }

            var so = new SerializedObject(presenter);
            SerializedProperty roles = so.FindProperty("m_AudibleToRoles");
            roles.arraySize = 1;
            roles.GetArrayElementAtIndex(0).intValue = (int)role;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[Timeline] 自白 {label} -> '{source.name}', audible to {role} only.");
        }

        /// <summary>
        /// The two decks are private to different players, and neither one talks over the beat it
        /// is meant to sit between.
        ///
        /// BOTH DECKS PRIVATE TO THE SAME PLAYER is the failure that sounds like success on one
        /// headset: that player hears the whole opening and nobody notices the other stood in
        /// silence until there are two people in the room. The roles are written by index through
        /// a SerializedProperty, so this is checked rather than trusted.
        ///
        /// THE ORDER IS CHECKED TOO, because the clips are meant to be dragged. Dragging the
        /// first line later until it runs into the dissolve, or the second line somewhere the
        /// body no longer lands on its last word, are exactly the two mistakes this arrangement
        /// invites -- and in the room they read as a sound problem rather than as a timing one.
        ///
        /// The second check changed direction when the pair moved to 112s. The body is no longer
        /// required to have arrived BEFORE the line; it is required not to still be arriving
        /// AFTER it. A body that finishes forming into silence is the thing to catch now.
        /// </summary>
        static bool VerifyNarration(PlayableDirector director, Deck[] decks)
        {
            bool ok = true;
            var seen = new System.Collections.Generic.List<PlayerRole>();
            float arrived = s_BodyAt + k_BodyCue * 0.5f + k_BodySeconds;

            foreach (Deck deck in decks)
            {
                var source = director.GetGenericBinding(deck.track) as AudioSource;
                if (source == null)
                {
                    Debug.LogError($"[Timeline] RESULT: FAIL - '{deck.trackName}' is bound to no " +
                                   "AudioSource, so that player's lines would never be heard.");
                    ok = false;
                    continue;
                }

                var presenter = source.GetComponent<RoleAudible>();
                if (presenter == null)
                {
                    Debug.LogError($"[Timeline] RESULT: FAIL - '{source.name}' has no " +
                                   $"RoleAudible, so both players would hear 自白 {deck.label}.");
                    ok = false;
                    continue;
                }

                var so = new SerializedObject(presenter);
                SerializedProperty roles = so.FindProperty("m_AudibleToRoles");
                if (roles.arraySize != 1)
                {
                    Debug.LogError($"[Timeline] RESULT: FAIL - '{source.name}' is audible to " +
                                   $"{roles.arraySize} roles; one is the whole point of it.");
                    ok = false;
                    continue;
                }

                seen.Add((PlayerRole)roles.GetArrayElementAtIndex(0).intValue);

                var clips = new System.Collections.Generic.List<TimelineClip>(deck.track.GetClips());
                if (clips.Count != 2)
                {
                    Debug.LogError($"[Timeline] RESULT: FAIL - '{deck.trackName}' holds " +
                                   $"{clips.Count} clips, not the two lines {deck.label} is owed.");
                    ok = false;
                    continue;
                }

                double firstEnds = clips[0].start + clips[0].duration;
                if (firstEnds > s_ScansOutAt + 0.001)
                {
                    Debug.LogError($"[Timeline] RESULT: FAIL - 自白 {deck.label}1 runs to " +
                                   $"{firstEnds:F1}s but the 真人 start dissolving at " +
                                   $"{s_ScansOutAt:F1}s. 「播完後才接scan dissolve」.");
                    ok = false;
                }

                double secondEnds = clips[1].start + clips[1].duration;
                if (arrived > secondEnds + 0.001)
                {
                    Debug.LogError($"[Timeline] RESULT: FAIL - 自白 {deck.label}2 ends at " +
                                   $"{secondEnds:F1}s but the body is still arriving until " +
                                   $"{arrived:F1}s, so it finishes forming into a room that has " +
                                   "gone quiet. 「讓他在140S的時候播完」 -- the ramp lands on the " +
                                   "last word.");
                    ok = false;
                }

                Debug.Log($"[Timeline] 自白 {deck.label}: {clips[0].start:F1}-{firstEnds:F1}s " +
                          $"then {clips[1].start:F1}-{clips[1].start + clips[1].duration:F1}s, " +
                          $"out of '{source.name}'.");
            }

            if (seen.Count != 2 || seen[0] == seen[1])
            {
                Debug.LogError("[Timeline] RESULT: FAIL - the two decks resolved to " +
                               $"{string.Join(" and ", seen)}; they must be different players, " +
                               "or one of the two stands in silence through the whole opening.");
                ok = false;
            }

            return ok;
        }

        static T Find<T>(GameObject root, string path) where T : Component
        {
            Transform t = root.transform.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        /// <summary>
        /// Put the director inside the prefab, bound to the beat's own Animator.
        ///
        /// An AnimationTrack drives an Animator, so the prefab root needs one. It has no
        /// controller and never will -- Timeline builds its own playable graph and drives the
        /// Animator through that. An Animator with no controller animates nothing on its own,
        /// which is exactly what is wanted outside the cue.
        /// </summary>
        /// <summary>
        /// 「出現提示音」 as its own track, so the sound and the words can be slid against each
        /// other without touching the lighting.
        /// </summary>
        static AudioTrack BuildAudioTrack(TimelineAsset timeline)
        {
            var tone = AssetDatabase.LoadAssetAtPath<AudioClip>(k_ToneClip);
            if (tone == null)
            {
                Debug.LogError($"[Timeline] RESULT: FAIL - no audio clip at {k_ToneClip}.");
                return null;
            }

            AudioTrack track = null;
            foreach (TrackAsset existing in timeline.GetOutputTracks())
            {
                if (existing is AudioTrack a && a.name == k_AudioTrackName) track = a;
            }

            if (track == null)
            {
                track = timeline.CreateTrack<AudioTrack>(null, k_AudioTrackName);
            }
            else
            {
                foreach (TimelineClip old in new System.Collections.Generic.List<TimelineClip>(track.GetClips()))
                    timeline.DeleteClip(old);
            }

            TimelineClip clip = track.CreateClip<AudioPlayableAsset>();
            ((AudioPlayableAsset)clip.asset).clip = tone;
            clip.start = k_ToneAt;
            clip.duration = tone.length;
            clip.displayName = "Cue";

            Debug.Log($"[Timeline] audio track '{track.name}', {tone.name} at {k_ToneAt}s.");
            return track;
        }

        /// <summary>
        /// The title card, the line of text and the speaker that plays the tone.
        ///
        /// Both cards are plain quads with an unlit ADDITIVE material. For white type on black
        /// that is the right blend and alpha is the wrong one: an alpha-blended quad carries its
        /// own rectangle, writing black over black wherever the glyphs are not, which stays
        /// invisible right up until something passes behind it and then it is a floating box.
        /// Additive adds light where the glyphs are and nothing anywhere else, so the card has no
        /// edges -- and it takes the cards out of the transparent sort order argument with the
        /// beams and the point clouds entirely.
        /// </summary>
        static bool BuildOpening(GameObject root)
        {
            Transform content = root.transform.Find("Content");
            if (content == null)
            {
                Debug.LogError("[Timeline] RESULT: FAIL - the prefab has no Content.");
                return false;
            }

            Transform opening = content.Find("Opening");
            if (opening == null)
            {
                var go = new GameObject("Opening");
                go.transform.SetParent(content, false);
                opening = go.transform;
            }

            if (!BuildCard(opening, "Title", k_TitleTex, k_TitleMat, k_TitleWidth)) return false;
            if (!BuildCard(opening, "Line", k_LineTex, k_LineMat, k_LineWidth)) return false;

            Transform tone = opening.Find("Tone");
            if (tone == null)
            {
                var go = new GameObject("Tone");
                go.transform.SetParent(opening, false);
                tone = go.transform;
            }

            var source = tone.GetComponent<AudioSource>();
            if (source == null) source = tone.gameObject.AddComponent<AudioSource>();
            source.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(k_ToneClip);
            source.playOnAwake = false;
            // 2D. The tone marks the start of the experience rather than coming from somewhere in
            // the room, and a positioned cue would arrive from a different direction for each of
            // the two players standing in different places.
            source.spatialBlend = 0f;
            return true;
        }

        static bool BuildCard(Transform parent, string name, string texPath, string matPath,
                              float widthMetres)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                Debug.LogError($"[Timeline] RESULT: FAIL - no texture at {texPath}.");
                return false;
            }

            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(unlit) { name = System.IO.Path.GetFileNameWithoutExtension(matPath) };
                AssetDatabase.CreateAsset(mat, matPath);
            }

            // URP's Unlit does not switch to transparent from the shader alone -- the surface
            // mode, the blend factors, the depth write, the queue AND the keyword all have to
            // agree, and setting only some of them gives an opaque black rectangle.
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(mat);

            Transform card = parent.Find(name);
            if (card == null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = name;
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(parent, false);
                card = quad.transform;
            }

            // A Unity Quad's front face points along -Z, which is toward a viewer standing at
            // negative Z looking into the room. Identity rotation is already facing them.
            float aspect = tex.height / (float)tex.width;
            card.localPosition = new Vector3(0f, k_EyeHeight, k_CardDistance);
            card.localRotation = Quaternion.identity;
            card.localScale = new Vector3(widthMetres, widthMetres * aspect, 1f);

            var renderer = card.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (card.GetComponent<CardFade>() == null) card.gameObject.AddComponent<CardFade>();
            return true;
        }

        static bool WirePrefab(TimelineAsset timeline, AnimationTrack track, AudioTrack audioTrack,
                               AnimationTrack[] handover, Deck[] decks, string prefabPath,
                               bool requireHandover)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogError($"[Timeline] RESULT: FAIL - could not open {prefabPath}.");
                return false;
            }

            try
            {
                if (!BuildOpening(root)) return false;

                var animator = root.GetComponent<Animator>();
                if (animator == null) animator = root.AddComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                Transform holder = root.transform.Find("Timeline");
                if (holder == null)
                {
                    var go = new GameObject("Timeline");
                    go.transform.SetParent(root.transform, false);
                    holder = go.transform;
                }

                var director = holder.GetComponent<PlayableDirector>();
                if (director == null) director = holder.gameObject.AddComponent<PlayableDirector>();

                director.playableAsset = timeline;
                director.playOnAwake = false;
                // Manual, and driven from ExperienceClock by O0ArrivalBeat. See the class note.
                director.timeUpdateMode = DirectorUpdateMode.Manual;
                director.extrapolationMode = DirectorWrapMode.Hold;
                director.SetGenericBinding(track, animator);

                // The same Animator for the handover rows: every track's paths are written
                // relative to the prefab root, which is where it lives.
                foreach (AnimationTrack row in handover)
                    director.SetGenericBinding(row, animator);

                Transform handoverOn = root.transform.Find(k_HandoverPath);
                Transform awakeningOn = root.transform.Find(k_AwakeningPath);
                if (handoverOn == null || awakeningOn == null)
                {
                    if (requireHandover)
                    {
                        Debug.LogError($"[Timeline] RESULT: FAIL - {prefabPath} needs " +
                                       $"'{k_HandoverPath}' and '{k_AwakeningPath}'; the handover " +
                                       "curves address them by path and would animate nothing.");
                        return false;
                    }

                    Debug.Log($"[Timeline] {prefabPath} has no '{k_HandoverPath}' or " +
                              $"'{k_AwakeningPath}'; it is the seed, so the handover is left to " +
                              "the merged prefab.");
                }
                else if (!PrepareHandover(handoverOn, awakeningOn))
                {
                    return false;
                }

                Transform tone = root.transform.Find("Content/Opening/Tone");
                if (tone == null || tone.GetComponent<AudioSource>() == null)
                {
                    Debug.LogError("[Timeline] RESULT: FAIL - no Tone AudioSource to bind.");
                    return false;
                }
                director.SetGenericBinding(audioTrack, tone.GetComponent<AudioSource>());

                // A speaker of its own for each player, and the role gate on it. Both decks are
                // on both machines; only one of them is audible on each.
                foreach (Deck deck in decks)
                {
                    AudioSource speaker = EnsureSpeaker(tone.parent, deck.speaker);
                    director.SetGenericBinding(deck.track, speaker);
                    EnsureRoleAudible(speaker, deck.role, deck.label);
                }

                // Hand the beat its director, so the cue has something to drive.
                var beat = root.GetComponent<O0ArrivalBeat>();
                if (beat == null)
                {
                    Debug.LogError("[Timeline] RESULT: FAIL - the prefab root has no O0ArrivalBeat.");
                    return false;
                }

                var so = new SerializedObject(beat);
                so.FindProperty("m_Director").objectReferenceValue = director;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[Timeline] {prefabPath} wired: Animator on the root, " +
                          "PlayableDirector on Timeline, manual update mode.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
