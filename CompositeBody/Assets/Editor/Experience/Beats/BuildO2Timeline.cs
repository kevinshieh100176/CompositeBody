using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CompositeBody.Experience;
using CompositeBody.PointClouds;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// O-2's own timeline: the orbit lights come up, the footprints arrive two seconds later, and
    /// S0-2 starts two seconds after that.
    ///
    /// A SUB-TIMELINE, LIKE O-0'S, rather than tracks on the master. These three cues belong to
    /// this beat, and a beat that owns its own timeline can be opened, scrubbed and re-cut on its
    /// own -- which is the whole reason the show nests its beats instead of authoring one
    /// 2260-second asset. The master now carries running order and holds, and nothing else.
    ///
    /// THE DIRECTOR AND ITS BINDINGS LIVE IN THE PREFAB. A binding is a reference to a scene
    /// object, so the director that drives these tracks has to sit in the same prefab as the
    /// orbits and the prints -- then the whole beat travels as one asset and no scene has to be
    /// repaired after the prefab is edited. It hangs off the onboarding root beside the O-0 one.
    ///
    /// WHY THE ORBITS AND PRINTS ARE SWITCHED OFF IN THE PREFAB AGAIN. An activation track only
    /// drives its target while its director is being evaluated, and this director is only driven
    /// during O-2 -- so before the beat begins there is nothing to hide them, and whatever the
    /// prefab says is what the room shows. On the master they could be left on, because that
    /// timeline is evaluated from the first frame of the show. The cost is that the group is
    /// invisible while its paths are shaped by hand: switch it on in the scene view and off
    /// before saving. Their debug rows survive it, because InteractionDebugRoster registers
    /// components on inactive objects too.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildO2Timeline.Run
    /// </summary>
    public static class BuildO2Timeline
    {
        const string k_Timeline = "Assets/_Timeline/O_2.playable";
        const string k_FadeClip = "Assets/_Timeline/O2_OrbitFade.anim";
        const string k_GhostClip = "Assets/_Timeline/O2_Ghosts.anim";
        const string k_Prefab = "Assets/_Scenes/Onboarding.prefab";

        /// <summary>
        /// 「一邊播放 vo[O-AB3_你有沒有發現]」 -- 85.5 seconds over a 90-second beat, so the line is
        /// very nearly the whole of O-2 and the four scans are paced to it rather than to the
        /// beat's length.
        ///
        /// IT REPLACES S0-2.wav, WHICH IS NOT IN THE PROJECT ANY MORE. This builder has been
        /// failing on the missing file -- returning before it wrote anything -- so the O_2
        /// timeline on disk is whatever it was last built as, which is why its duration still
        /// said 120s. The same failure WireO1Payoff had, for the same reason: a running order
        /// moved on and the builder kept asking for the file it used to name.
        ///
        /// NOT O-2AB_你有沒有發現.wav, which is a 165-second cut of the same line and would be
        /// most of a minute longer than the beat it has to fit inside.
        /// </summary>
        const string k_CueClip = "Assets/_Audio/O-AB3_你有沒有發現.wav";

        /// <summary>The object inside the prefab that carries this beat's director.</summary>
        public const string DirectorName = "Timeline O_2";

        const string k_SpeakerName = "Cue O-2";

        /// <summary>What that speaker was called while it played S0-2; renamed in place.</summary>
        const string k_OldSpeakerName = "Cue S0-2";

        /// <summary>The group of four scans, built by <see cref="BuildO2Ghosts"/>.</summary>
        const string k_GhostGroup = "O_2 Ghosts";

        /// <summary>
        /// O-1's group, which this beat inherits and empties. 「到o-2 箱子(box &amp;
        /// Showcase_CB_boxes)要fade out, scan人物都要fade in」 -- the cartons, the wall of boxes
        /// and whatever is still in them are taken away HERE rather than at the end of O-1, so
        /// the room changes hands over the top of the first scan forming instead of standing
        /// empty between the two beats.
        /// </summary>
        const string k_GrabGroup = "O_1 Grab";

        const string k_BoxesClip = "Assets/_Timeline/O2_BoxesOut.anim";

        /// <summary>
        /// When each cue happens, in seconds from the start of the beat. The first is not zero:
        /// the show arrives here off a hold, and a clip starting on the same instant would have
        /// been showing while the players were still being waited for.
        /// </summary>
        const double k_OrbitsAt = 0.5d;
        const double k_FootprintsAt = 2.5d;
        const double k_CueAt = 1.5d;

        const double k_OrbitFade = 3.5d;

        /// <summary>
        /// THE FOUR SCANS' PACING, and the only four numbers here worth an opinion.
        ///
        /// A slot is one scan's whole turn -- form, stand, go -- and the next one starts when it
        /// is over. 4 x 21 = 84 seconds, which lands the last of them gone at 84s with the line
        /// still running to 87s and the beat ending at 90s: three seconds of the room empty
        /// again before O-3, which is the shape the dissolve-outs elsewhere in the show have.
        ///
        /// THEY DO NOT OVERLAP. A slot is longer than what happens inside it (2.5 + 14 + 2.5 =
        /// 19 of 21), so there is a second and a half of nobody between one scan and the next.
        /// Two point clouds in the same position do not read as two ghosts; they read as one
        /// cloud that has gone wrong. A crossfade is these numbers, not a different structure.
        /// </summary>
        const double k_GhostsAt = 2.0d;
        const double k_GhostSlot = 21.0d;
        const double k_GhostFade = 2.5d;
        const double k_GhostHold = 14.0d;

        /// <summary>
        /// O-1's boxes going, timed to cross the first scan arriving: they start half a second
        /// in and are gone at 5.0s, while Ghost 1 forms from 2.0 to 4.5s. The overlap is the
        /// point -- 「箱子要fade out, scan人物都要fade in」 is one exchange, not two cues, and a
        /// room that emptied first and filled afterwards would read as two.
        /// </summary>
        const double k_BoxesOutAt = 0.5d;
        const double k_BoxesOut = 4.5d;

        /// <summary>
        /// O-2's authored length, from the running order in BuildMainTimeline. v4 writes the
        /// beat as 0510 -> 0640, which is 90s, and the 120 this used to say was the old cut.
        /// </summary>
        const double k_BeatSeconds = 90d;

        [MenuItem("Tools/O-2/Build O-2 Timeline")]
        public static void Run()
        {
            Debug.Log("[O2Timeline] Starting...");

            var cue = AssetDatabase.LoadAssetAtPath<AudioClip>(k_CueClip);
            if (cue == null)
            {
                Debug.LogError($"[O2Timeline] RESULT: FAIL - no audio clip at {k_CueClip}.");
                return;
            }

            TimelineAsset timeline = LoadOrCreate();
            if (timeline == null) return;

            GameObject root = PrefabUtility.LoadPrefabContents(k_Prefab);
            if (root == null)
            {
                Debug.LogError($"[O2Timeline] RESULT: FAIL - could not open {k_Prefab}.");
                return;
            }

            try
            {
                Transform content = root.transform.Find("Content");
                Transform orbits = content != null ? content.Find("Orbits") : null;
                Transform prints = content != null ? content.Find("Footprints") : null;

                if (orbits == null || prints == null)
                {
                    Debug.LogError("[O2Timeline] RESULT: FAIL - the prefab needs Content/Orbits " +
                                   "and Content/Footprints. Run Tools/O-1/Wire Grab Cues and " +
                                   "Payoff first.");
                    return;
                }

                Transform beats = root.transform.Find("Beats");
                Transform ghosts = beats != null ? beats.Find(k_GhostGroup) : null;
                if (ghosts == null)
                {
                    Debug.LogError($"[O2Timeline] RESULT: FAIL - no Beats/{k_GhostGroup} in the " +
                                   "prefab, so there are no scans to show. Run Tools/" +
                                   "Onboarding/Build Onboarding first.");
                    return;
                }

                Clear(timeline);

                PlayableDirector director = EnsureDirector(root.transform, timeline);
                AudioSource speaker = EnsureSpeaker(content);

                BuildActivation(timeline, director, orbits.gameObject, "O-2: orbit lights",
                                k_OrbitsAt, "Orbits on");

                if (!BuildOrbitFade(timeline, director, orbits)) return;

                BuildActivation(timeline, director, prints.gameObject, "O-2: footprints",
                                k_FootprintsAt, "Footprints on");

                // FROM ZERO, unlike the orbits and the prints. Their curves write m_Reveal from
                // the first frame of the beat and an inactive group would not take it -- and a
                // clip starting at k_GhostsAt - k_GhostFade would start at -0.5s, which clamps
                // to 0 at the front and runs 0.5s PAST the beat at the back, making the asset
                // longer than the Control clip that drives it. Nothing is visible at 0 anyway:
                // four scans at reveal 0 draw nothing, because PointCloudFigure switches its
                // own renderer off when there is nothing to see.
                BuildActivation(timeline, director, ghosts.gameObject, "O-2: the scans",
                                0d, "Scans in the room");

                if (!BuildGhosts(timeline, director, ghosts)) return;

                Transform grab = beats.Find(k_GrabGroup);
                if (grab == null)
                {
                    Debug.LogError($"[O2Timeline] RESULT: FAIL - no Beats/{k_GrabGroup} in the " +
                                   "prefab, so O-1's boxes would never leave the room. Run " +
                                   "Tools/Onboarding/Build Onboarding first.");
                    return;
                }

                if (!BuildBoxesOut(timeline, director, grab)) return;

                BuildCue(timeline, director, speaker, cue);

                // Off in the prefab, because nothing hides them before this beat runs.
                Hide(orbits.gameObject);
                Hide(prints.gameObject);
                Hide(ghosts.gameObject);

                EditorUtility.SetDirty(timeline);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(k_Timeline, ImportAssetOptions.ForceSynchronousImport);

                PrefabUtility.SaveAsPrefabAsset(root, k_Prefab);

                Debug.Log($"[O2Timeline] {k_Timeline}: a {k_BeatSeconds}s beat -- orbits at " +
                          $"{k_OrbitsAt}s, footprints at {k_FootprintsAt}s, {cue.name} " +
                          $"({cue.length:F1}s) at {k_CueAt}s running to " +
                          $"{k_CueAt + cue.length:F1}s.");

                if (k_CueAt + cue.length > k_BeatSeconds)
                    Debug.LogWarning($"[O2Timeline] {cue.name} runs {k_CueAt + cue.length:F1}s " +
                                     $"into a {k_BeatSeconds}s beat, so the master's Control " +
                                     "clip stops driving this director before the line is over " +
                                     "and the last of it is never heard.");

                Debug.Log("[O2Timeline] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void Hide(GameObject go)
        {
            if (!go.activeSelf) return;
            go.SetActive(false);
            Debug.Log($"[O2Timeline] switched '{go.name}' off; the activation track brings it in.");
        }

        static TimelineAsset LoadOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(created, k_Timeline);
            Debug.Log($"[O2Timeline] created {k_Timeline}.");
            return AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
        }

        /// <summary>
        /// Emptied and rebuilt rather than replaced, so the asset keeps its GUID and the director
        /// pointing at it survives.
        /// </summary>
        static void Clear(TimelineAsset timeline)
        {
            foreach (TrackAsset track in new List<TrackAsset>(timeline.GetRootTracks()))
                timeline.DeleteTrack(track);
        }

        static PlayableDirector EnsureDirector(Transform root, TimelineAsset timeline)
        {
            Transform holder = root.Find(DirectorName);
            if (holder == null)
            {
                var go = new GameObject(DirectorName);
                go.transform.SetParent(root, false);
                holder = go.transform;
                Debug.Log($"[O2Timeline] created '{DirectorName}' in the prefab.");
            }

            var director = holder.GetComponent<PlayableDirector>();
            if (director == null) director = holder.gameObject.AddComponent<PlayableDirector>();

            director.playableAsset = timeline;

            // Driven by the master's Control clip, never by itself.
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.extrapolationMode = DirectorWrapMode.Hold;

            return director;
        }

        /// <summary>
        /// The speaker for S0-2, inside the prefab so the audio track can bind to it. 2D: the cue
        /// is the room's own sound rather than a thing in it.
        /// </summary>
        static AudioSource EnsureSpeaker(Transform content)
        {
            Transform speaker = content.Find(k_SpeakerName);

            // RENAMED, NOT REPLACED. This object was 'Cue S0-2' while it played that clip; a
            // second one beside it would leave an orphan AudioSource in the prefab that nothing
            // drives and nobody deletes, which is how a scene ends up with three speakers.
            if (speaker == null)
            {
                Transform old = content.Find(k_OldSpeakerName);
                if (old != null)
                {
                    old.name = k_SpeakerName;
                    speaker = old;
                    Debug.Log($"[O2Timeline] renamed '{k_OldSpeakerName}' to '{k_SpeakerName}'; " +
                              "the beat's line is O-AB3 now.");
                }
            }

            if (speaker == null)
            {
                var go = new GameObject(k_SpeakerName);
                go.transform.SetParent(content, false);
                speaker = go.transform;
                Debug.Log($"[O2Timeline] created '{k_SpeakerName}' in the prefab.");
            }

            var source = speaker.GetComponent<AudioSource>();
            if (source == null) source = speaker.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            return source;
        }

        /// <summary>
        /// One activation clip, running to the end of the beat.
        ///
        /// TO THE END, because an activation track forces its target inactive everywhere it has
        /// no clip -- not only before the first one. A clip that stopped when the cue was over
        /// would switch the lights back off a few seconds after bringing them up.
        /// </summary>
        static void BuildActivation(TimelineAsset timeline, PlayableDirector director,
                                    GameObject target, string trackName, double at, string clipName)
        {
            var track = timeline.CreateTrack<ActivationTrack>(null, trackName);
            TimelineClip clip = track.CreateDefaultClip();
            clip.start = at;
            clip.duration = k_BeatSeconds - at;
            clip.displayName = clipName;

            // Active, so the lights and the prints stay as the beat left them once this director
            // stops being driven at the end of O-2 rather than snapping off at the boundary.
            track.postPlaybackState = ActivationTrack.PostPlaybackState.Active;
            director.SetGenericBinding(track, target);
        }

        /// <summary>
        /// The lights' arrival, as curves: two lamp intensities and the two glows around them,
        /// from nothing to their authored values.
        ///
        /// ONLY FOUR CURVES, AND DELIBERATELY NOT THE TRAILS OR THE SPARKS. Both arrive correctly
        /// on their own -- a TrailRenderer switched on has no history to draw and grows as the
        /// light moves, and the sparks emit over DISTANCE, so they appear at the rate the light
        /// travels. Animating them would mean authoring a fade for something that already fades,
        /// against property paths that fail silently when they are wrong.
        /// </summary>
        static bool BuildOrbitFade(TimelineAsset timeline, PlayableDirector director, Transform orbits)
        {
            var animator = orbits.GetComponent<Animator>();
            if (animator == null) animator = orbits.gameObject.AddComponent<Animator>();

            var clip = new AnimationClip { name = "O2_OrbitFade", frameRate = 60f };

            foreach (string orbit in new[] { "Orbit Purple", "Orbit Yellow" })
            {
                Transform mover = orbits.Find($"{orbit}/Light");
                if (mover == null)
                {
                    Debug.LogError($"[O2Timeline] RESULT: FAIL - no '{orbit}/Light' under the " +
                                   "Orbits group; the fade would animate nothing.");
                    return false;
                }

                var lamp = mover.GetComponent<Light>();
                float intensity = lamp != null ? lamp.intensity : 1.9f;

                // Read off the object rather than assumed, so a light someone has dimmed by hand
                // fades up to the value they chose instead of back to the builder's.
                clip.SetCurve($"{orbit}/Light", typeof(Light), "m_Intensity", Ramp(intensity));

                Transform glow = mover.Find("Glow");
                var volume = glow != null ? glow.GetComponent<VolumetricPoint>() : null;
                if (volume != null)
                    clip.SetCurve($"{orbit}/Light/Glow", typeof(VolumetricPoint), "m_Reveal",
                                  Ramp(volume.reveal));
            }

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(k_FadeClip);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
                clip = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(clip, k_FadeClip);
            }
            EditorUtility.SetDirty(clip);

            var track = timeline.CreateTrack<AnimationTrack>(null, "O-2: orbit fade");
            TimelineClip timelineClip = track.CreateClip(clip);
            timelineClip.start = k_OrbitsAt;
            timelineClip.duration = k_OrbitFade;
            timelineClip.displayName = "Lights up";

            director.SetGenericBinding(track, animator);
            return true;
        }

        static AnimationCurve Ramp(float target) =>
            AnimationCurve.EaseInOut(0f, 0f, (float)k_OrbitFade, target);

        /// <summary>
        /// The four scans forming and going, as eight curves on one clip that spans the beat.
        ///
        /// ONE FLOAT, RUN BOTH WAYS. 「我這四個想要都是dissolve in」 -- so a scan arrives by its
        /// dissolve going 1 -> 0 and leaves by the same float going 0 -> 1, and m_Reveal is
        /// pinned at 1 in the prefab and never animated. Each scan is one curve, not two.
        ///
        /// WHICH IS NOT WHAT REVEAL WOULD HAVE GIVEN. Reveal is a hard spatial wavefront with
        /// no per-point term -- it draws the body on from an origin like a wipe. Dissolve mixes
        /// that order with a per-point hash, drifts every point along m_DissolveDir and shrinks
        /// it on the way; reversed, that is a scatter of points falling in and settling into a
        /// person. Coming together and coming apart are then the same gesture in two
        /// directions, which is the right symmetry for a beat about one.
        ///
        /// ONE CLIP FROM 0 TO THE END OF THE BEAT, so a key's time IS its time in O-2 and the
        /// whole succession can be read in one glance at the curve editor. The alternative --
        /// a clip per scan, each starting where that scan does -- hides the gaps between them
        /// in the clip offsets, and the gaps are the thing most likely to want adjusting.
        ///
        /// AND THE CURVES HOLD AT BOTH ENDS. A key at 0 and a key at the end mean a director
        /// evaluated at any moment of the beat writes a definite value: no scan can be left
        /// half-formed by a scrub, and the group's authored state cannot leak through.
        /// </summary>
        static bool BuildGhosts(TimelineAsset timeline, PlayableDirector director, Transform ghosts)
        {
            var animator = ghosts.GetComponent<Animator>();
            if (animator == null) animator = ghosts.gameObject.AddComponent<Animator>();

            var clip = new AnimationClip { name = "O2_Ghosts", frameRate = 60f };
            var shown = new List<string>();

            for (int i = 0; i < BuildO2Ghosts.Ghosts.Length; i++)
            {
                BuildO2Ghosts.Ghost ghost = BuildO2Ghosts.Ghosts[i];

                if (ghosts.Find(ghost.FigurePath) == null)
                {
                    Debug.LogError($"[O2Timeline] RESULT: FAIL - no '{ghost.FigurePath}' under " +
                                   $"{k_GhostGroup}; the curves would animate nothing. Run " +
                                   "Tools/O-2/Build the Ghost Scans first.");
                    return false;
                }

                double formsAt = k_GhostsAt + i * k_GhostSlot;
                double goesAt = formsAt + k_GhostFade + k_GhostHold;

                // Gone, together, gone. The keys at 0 and at the end of the beat are what make
                // a scrub to any moment write a definite value, so no scan can be left half
                // assembled by dragging the playhead.
                clip.SetCurve(ghost.FigurePath, typeof(PointCloudFigure), "m_Dissolve",
                    new AnimationCurve(
                        new Keyframe(0f, 1f, 0f, 0f),
                        new Keyframe((float)formsAt, 1f, 0f, 0f),
                        new Keyframe((float)(formsAt + k_GhostFade), 0f, 0f, 0f),
                        new Keyframe((float)goesAt, 0f, 0f, 0f),
                        new Keyframe((float)(goesAt + k_GhostFade), 1f, 0f, 0f),
                        new Keyframe((float)k_BeatSeconds, 1f, 0f, 0f)));

                shown.Add($"{ghost.name} ({ghost.capture}) {formsAt:F1}-" +
                          $"{formsAt + k_GhostFade:F1}s in, " +
                          $"{goesAt:F1}-{goesAt + k_GhostFade:F1}s out");
            }

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(k_GhostClip);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
                clip = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(clip, k_GhostClip);
            }
            EditorUtility.SetDirty(clip);

            var track = timeline.CreateTrack<AnimationTrack>(null, "O-2: the scans, forming and going");
            TimelineClip timelineClip = track.CreateClip(clip);
            timelineClip.start = 0d;
            timelineClip.duration = k_BeatSeconds;
            timelineClip.displayName = "Four scans";

            director.SetGenericBinding(track, animator);

            Debug.Log($"[O2Timeline] {shown.Count} scan(s) at the centre of the play space -- " +
                      $"{string.Join("; ", shown)}. One at a time, never two.");
            return true;
        }

        /// <summary>
        /// Everything O-1 left standing, taken away as this beat opens.
        ///
        /// EVERY PropPresence UNDER THE GROUP, not a list of names. The beat shows a wall of
        /// boxes, two cartons and however many objects the running order is up to; naming them
        /// here would mean a third object added to O-1 is one nobody remembered to clear away,
        /// and it would be left hanging in the room through the whole of O-2 before anyone
        /// noticed. What O-1 can show is exactly what carries a PropPresence, so that is the
        /// list.
        ///
        /// THE JACKETS ARE WHY THIS MATTERS. They were never on O-1's own track at all --
        /// <see cref="CompositeBody.Experience.O1GrabWaves"/> owns them, because when they
        /// arrive depends on the players -- so a dissolve-out authored in O-1 could not have
        /// taken them. One still in a player's hand at the end of the beat simply stayed. Here
        /// there is no conflict: O1GrabWaves stops writing a presence the moment its fade is
        /// finished, and O-1's director is not being evaluated during O-2, so this track is the
        /// only thing touching these floats.
        ///
        /// AND THE ONE CASE IT GETS WRONG, written down because it is the kind of thing that is
        /// found on a stage rather than in a diff: the curve starts at 1, so anything that
        /// never arrived is SHOWN for half a second and then dissolved. That cannot happen on a
        /// run that finishes -- beat.O1_Control needs every box filled, which needs every
        /// object to have been in a hand -- but it can on the 180-second fallback, where a
        /// player who never put their lamp away never got a jacket. The fix is not a different
        /// curve: a curve that started at 0 would snap away the jacket of every player who DID
        /// get one. It would take a component that fades from wherever a float already is, and
        /// that is not worth owning for a case that only happens after the show has already
        /// visibly failed.
        /// </summary>
        static bool BuildBoxesOut(TimelineAsset timeline, PlayableDirector director, Transform grab)
        {
            var animator = grab.GetComponent<Animator>();
            if (animator == null) animator = grab.gameObject.AddComponent<Animator>();

            var presences = grab.GetComponentsInChildren<PropPresence>(true);
            if (presences.Length == 0)
            {
                Debug.LogError($"[O2Timeline] RESULT: FAIL - nothing under {k_GrabGroup} has a " +
                               "PropPresence, so O-1's boxes have no way to leave. Run " +
                               "Tools/O-1/Build the O-1 Timeline first.");
                return false;
            }

            var clip = new AnimationClip { name = "O2_BoxesOut", frameRate = 60f };
            var names = new List<string>();

            foreach (PropPresence presence in presences)
            {
                string path = PathUnder(grab, presence.transform);
                clip.SetCurve(path, typeof(PropPresence), "m_Presence", new AnimationCurve(
                    new Keyframe(0f, 1f, 0f, 0f),
                    new Keyframe((float)k_BoxesOutAt, 1f, 0f, 0f),
                    new Keyframe((float)(k_BoxesOutAt + k_BoxesOut), 0f, 0f, 0f),
                    new Keyframe((float)k_BeatSeconds, 0f, 0f, 0f)));
                names.Add(path);
            }

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(k_BoxesClip);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
                clip = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(clip, k_BoxesClip);
            }
            EditorUtility.SetDirty(clip);

            var track = timeline.CreateTrack<AnimationTrack>(null, "O-2: O-1's boxes, going");
            TimelineClip timelineClip = track.CreateClip(clip);
            timelineClip.start = 0d;
            timelineClip.duration = k_BeatSeconds;
            timelineClip.displayName = "Boxes out";

            director.SetGenericBinding(track, animator);

            Debug.Log($"[O2Timeline] {names.Count} thing(s) of O-1's dissolve out " +
                      $"{k_BoxesOutAt}-{k_BoxesOutAt + k_BoxesOut}s, across Ghost 1 forming at " +
                      $"{k_GhostsAt}-{k_GhostsAt + k_GhostFade}s -- {string.Join(", ", names)}.");
            return true;
        }

        /// <summary>A transform's path relative to an ancestor, for an animation curve.</summary>
        static string PathUnder(Transform root, Transform target)
        {
            string path = target.name;
            for (Transform t = target.parent; t != null && t != root; t = t.parent)
                path = t.name + "/" + path;
            return path;
        }

        static void BuildCue(TimelineAsset timeline, PlayableDirector director,
                             AudioSource speaker, AudioClip cue)
        {
            var track = timeline.CreateTrack<AudioTrack>(null, "O-2: the line");
            TimelineClip clip = track.CreateClip<AudioPlayableAsset>();
            ((AudioPlayableAsset)clip.asset).clip = cue;
            clip.start = k_CueAt;
            clip.duration = cue.length;
            clip.displayName = cue.name;

            director.SetGenericBinding(track, speaker);
        }
    }
}
