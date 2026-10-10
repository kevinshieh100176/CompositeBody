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
        const string k_Prefab = "Assets/_Scenes/O_0.prefab";
        const string k_TrackName = "O-0 Cue";
        const string k_ClipName = "O_0_Cue";
        const string k_AudioTrackName = "Cue Tone";

        const string k_TitleTex = "Assets/_Art/O0/Title_CompositeBody_PLACEHOLDER.png";
        const string k_LineTex = "Assets/_Art/O0/O0_OpeningLine.png";
        const string k_TitleMat = "Assets/Materials/O0Title.mat";
        const string k_LineMat = "Assets/Materials/O0Line.mat";
        const string k_ToneClip = "Assets/Audio/Placeholder/O0_Cue_PLACEHOLDER.wav";

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

        // Full values, matching what BuildO0Arrival authored.
        const float k_CentreLamp = 24f;
        const float k_ColourLamp = 6f;

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

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(k_Timeline, ImportAssetOptions.ForceSynchronousImport);

            if (!WirePrefab(timeline, track, audioTrack)) return;

            // Flush the prefab before the scene is opened. WirePrefab has just added an Animator
            // to the prefab asset, and an instance already sitting in a scene does not grow one
            // until the asset is written and reimported -- so without this the binding below
            // looks for an Animator that does not exist yet and quietly gives up.
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(k_Prefab, ImportAssetOptions.ForceSynchronousImport);

            BindAuthoringScene();
            if (!Verify()) return;

            float end = k_Open + k_Hold + k_CentreDelay + k_CentreDown + k_Tail;
            float colourAt = k_Open + k_Hold;
            Debug.Log($"[Timeline] title 0-{k_TitleOutAt + k_TitleOut}s, tone {k_ToneAt}s, " +
                      $"line {k_LineInAt}-{k_LineOutAt + k_LineOut}s, room full {k_Open}s, " +
                      $"halos up {colourAt}-{colourAt + k_ColourUp}s, white out " +
                      $"{colourAt + k_CentreDelay}-{colourAt + k_CentreDelay + k_CentreDown}s, " +
                      $"clip {end}s.\n[Timeline] RESULT: PASS");
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
            const string k_Scene = "Assets/_Scenes/O_0.unity";

            // Reloaded from the path rather than passed in. Run() reimports the timeline asset
            // before this point, and a ForceSynchronousImport can replace the loaded instance --
            // so a reference captured earlier compares false against the very asset it came
            // from. The director was found and rejected for being "a different timeline".
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            AnimationTrack track = null;
            foreach (TrackAsset candidate in timeline.GetOutputTracks())
            {
                if (candidate is AnimationTrack a && a.name == k_TrackName) track = a;
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
        static bool Verify()
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

                if (ok) Debug.Log("[Timeline] OK   the opening and the cue drive everything.");
                return ok;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
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

        static bool WirePrefab(TimelineAsset timeline, AnimationTrack track, AudioTrack audioTrack)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(k_Prefab);
            if (root == null)
            {
                Debug.LogError($"[Timeline] RESULT: FAIL - could not open {k_Prefab}.");
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

                Transform tone = root.transform.Find("Content/Opening/Tone");
                if (tone == null || tone.GetComponent<AudioSource>() == null)
                {
                    Debug.LogError("[Timeline] RESULT: FAIL - no Tone AudioSource to bind.");
                    return false;
                }
                director.SetGenericBinding(audioTrack, tone.GetComponent<AudioSource>());

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

                PrefabUtility.SaveAsPrefabAsset(root, k_Prefab);
                Debug.Log("[Timeline] prefab wired: Animator on the root, PlayableDirector on " +
                          "Timeline, manual update mode.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
