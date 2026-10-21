using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CompositeBody.Experience;

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
        const string k_Prefab = "Assets/_Scenes/Onboarding.prefab";
        const string k_CueClip = "Assets/_Audio/S0-2.wav";

        /// <summary>The object inside the prefab that carries this beat's director.</summary>
        public const string DirectorName = "Timeline O_2";

        const string k_SpeakerName = "Cue S0-2";

        /// <summary>
        /// When each cue happens, in seconds from the start of the beat. The first is not zero:
        /// the show arrives here off a hold, and a clip starting on the same instant would have
        /// been showing while the players were still being waited for.
        /// </summary>
        const double k_OrbitsAt = 0.5d;
        const double k_FootprintsAt = 2.5d;
        const double k_CueAt = 4.5d;

        const double k_OrbitFade = 3.5d;

        /// <summary>O-2's authored length, from the running order in BuildMainTimeline.</summary>
        const double k_BeatSeconds = 120d;

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

                Clear(timeline);

                PlayableDirector director = EnsureDirector(root.transform, timeline);
                AudioSource speaker = EnsureSpeaker(content);

                BuildActivation(timeline, director, orbits.gameObject, "O-2: orbit lights",
                                k_OrbitsAt, "Orbits on");

                if (!BuildOrbitFade(timeline, director, orbits)) return;

                BuildActivation(timeline, director, prints.gameObject, "O-2: footprints",
                                k_FootprintsAt, "Footprints on");

                BuildCue(timeline, director, speaker, cue);

                // Off in the prefab, because nothing hides them before this beat runs.
                Hide(orbits.gameObject);
                Hide(prints.gameObject);

                EditorUtility.SetDirty(timeline);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(k_Timeline, ImportAssetOptions.ForceSynchronousImport);

                PrefabUtility.SaveAsPrefabAsset(root, k_Prefab);

                Debug.Log($"[O2Timeline] {k_Timeline}: orbits at {k_OrbitsAt}s, footprints at " +
                          $"{k_FootprintsAt}s, {cue.name} ({cue.length:F1}s) at {k_CueAt}s.");
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

        static void BuildCue(TimelineAsset timeline, PlayableDirector director,
                             AudioSource speaker, AudioClip cue)
        {
            var track = timeline.CreateTrack<AudioTrack>(null, "O-2: S0-2");
            TimelineClip clip = track.CreateClip<AudioPlayableAsset>();
            ((AudioPlayableAsset)clip.asset).clip = cue;
            clip.start = k_CueAt;
            clip.duration = cue.length;
            clip.displayName = cue.name;

            director.SetGenericBinding(track, speaker);
        }
    }
}
