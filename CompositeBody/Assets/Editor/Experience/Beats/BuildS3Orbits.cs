using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// O-2's two orbit lights and its two rings of footprints again, in S3: up for the whole of
    /// the section and centred on wherever the playspace has got to by then.
    ///
    /// COPIES OF THE O-2 OBJECTS RATHER THAN A SECOND BUILD OF THEM. 「o-2的orbit light 跟 腳印」
    /// is a request for those lights and those prints, and both were shaped by hand after their
    /// builders made them -- the orbit paths especially, which is why
    /// <see cref="BuildLightOrbits"/> refuses to overwrite them. Rebuilding from the same numbers
    /// would give S3 the pre-shaping version and then quietly disagree with O-2 forever. So this
    /// instantiates what is in the show.
    ///
    /// They land in MainScene as plain objects, not as a prefab instance: the originals are
    /// children of the Onboarding prefab instance, and a sub-object of a prefab instance cannot
    /// be lifted out with its link intact. Nothing is lost by it -- the two sets are separate
    /// staging decisions from here on, which is what re-shaping one without the other means.
    ///
    /// THE CENTRE IS READ OFF THE SHOW, NOT TYPED IN. 「中心位置擺在那個時間play space的中央」:
    /// the playspace does not stay where MainScene authors it, it is carried to each section by
    /// an Animation track on Main -- so the centre is that track's curve sampled at the first
    /// frame of S3, which at the time of writing is (0, -21.36, 100.69). Typing the number in
    /// would read correctly and be wrong the first time anybody re-keys the move.
    ///
    /// THE HEIGHT IS THE FLOOR'S, NOT THE PLAYSPACE'S, and they are 4 cm apart. The playspace
    /// sits at y -21.36 while the plane S3 actually shows is at -21.40, so prints hung at the
    /// playspace's own height would float a visible 5 cm over the floor they are drawn on. Only
    /// the two horizontal numbers come from the playspace.
    ///
    /// ONE ACTIVATION CLIP ACROSS THE WHOLE SECTION. An activation track forces its target
    /// inactive everywhere it has no clip, so a single clip from the first frame of S3 to the end
    /// of the show is both the cue and the thing that keeps the group hidden for the 1181 seconds
    /// before it. It starts inside the S2 -> S3 blackout, like the wall and light cues either
    /// side of it, so nothing is seen arriving.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildS3Orbits.Run
    /// </summary>
    public static class BuildS3Orbits
    {
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";
        const string k_TimelineAsset = "Assets/_Timeline/Main.playable";

        /// <summary>Where the copies live, beside the other beats' content.</summary>
        const string k_BeatsPath = "Experience/Beats";
        const string k_GroupName = "S3 Orbits + Footprints";

        /// <summary>
        /// The O-2 originals. Inside the Onboarding prefab instance, which is where
        /// BuildLightOrbits and BuildFootprints put them.
        /// </summary>
        const string k_OrbitsSource = "Experience/Beats/Onboarding/Content/Orbits";
        const string k_PrintsSource = "Experience/Beats/Onboarding/Content/Footprints";

        /// <summary>
        /// The plane S3 stands on. NOT Inner_Wall/Floor, which is the S2 room's floor and is
        /// switched off at 1181 by its own activation track -- S3 reuses the S2 set with the
        /// inner walls taken away, so from then on the floor underfoot is the outer plane.
        /// </summary>
        const string k_FloorPath =
            "Experience/Beats/Beat_S2/Content/S2_v1/--- Environment ---/S3/OuterWall/Plane";

        const string k_TrackName = "S3 Orbit + 腳印（黑場中開）";
        const string k_ClipName = "S3 全程";

        /// <summary>
        /// What the debug panel calls this pair of rings. Without it both pairs register as
        /// footprints.Player1 and footprints.Player2, and a key binding or a remote fire can only
        /// ever reach the first -- InteractionDebug warns about exactly that, which is how
        /// FootprintRing came to have the field.
        /// </summary>
        const string k_Where = "S3";

        /// <summary>
        /// The prints are drawn 1.2 cm over whatever their group sits on, so the group goes on the
        /// floor itself. In the onboarding room their group is lifted 22 cm instead, to clear that
        /// room's water surface at 0.19 -- S3 has no water (its look leaves the water field empty
        /// and the set's own Water System is off), so the same lift would be 22 cm of nothing.
        /// </summary>
        const float k_PrintGroupLift = 0f;

        [MenuItem("Tools/S-3/Build S3 Orbits and Footprints")]
        public static void Run() => Build(replace: false);

        /// <summary>
        /// Throw the S3 copies away and take them from O-2 again. A separate menu item, and not
        /// the default, for the same reason the orbit and footprint builders split: once these
        /// paths are re-shaped for a room far larger than the onboarding one, that shaping is the
        /// work.
        /// </summary>
        [MenuItem("Tools/S-3/Rebuild S3 Orbits and Footprints (discards hand placement)")]
        public static void Rebuild() => Build(replace: true);

        static void Build(bool replace)
        {
            Debug.Log($"[S3Orbits] Starting ({(replace ? "rebuild" : "create if missing")})...");

            // THIS BUILDER DOES NOT OPEN THE SCENE. Every other tool here opens MainScene Single
            // and would throw away whatever is in front of whoever ran it; and since this one has
            // to save the scene at the end, it cannot be allowed to save someone else's
            // half-finished work either.
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != k_MainScene)
            {
                Debug.LogError($"[S3Orbits] RESULT: FAIL - {k_MainScene} is not the open scene " +
                               $"('{scene.path}' is). Open it and run this again.");
                return;
            }

            if (scene.isDirty)
            {
                Debug.LogError("[S3Orbits] RESULT: FAIL - MainScene has unsaved changes and this " +
                               "builder saves the scene. Save or revert first.");
                return;
            }

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_TimelineAsset);
            if (timeline == null)
            {
                Debug.LogError($"[S3Orbits] RESULT: FAIL - no timeline at {k_TimelineAsset}.");
                return;
            }

            PlayableDirector director = FindShowDirector(timeline);
            if (director == null)
            {
                Debug.LogError("[S3Orbits] RESULT: FAIL - nothing in MainScene plays " +
                               $"{k_TimelineAsset}, so there is no director to bind a track on.");
                return;
            }

            if (!TryS3Span(timeline, director, out double from, out double to, out double lastBeatEnd))
            {
                Debug.LogError("[S3Orbits] RESULT: FAIL - no S3-* control tracks on Main, so " +
                               "where S3 starts is not knowable from the running order.");
                return;
            }

            if (!TryPlayspaceAt(director, timeline, from, out Vector3 centre))
            {
                Debug.LogError("[S3Orbits] RESULT: FAIL - could not read the playspace's position " +
                               $"at {from:F0}s. Main needs an Animation track bound to the " +
                               "Playspace's Animator with the recorded move on it.");
                return;
            }

            if (!TryFloorY(out float floorY))
            {
                floorY = centre.y;
                Debug.LogWarning($"[S3Orbits] no floor at {k_FloorPath}; falling back to the " +
                                 $"playspace's own height, {floorY:F3}. The prints will be a few " +
                                 "centimetres off whatever S3 actually stands on -- check them.");
            }

            GameObject beatsGO = Find(k_BeatsPath);
            if (beatsGO == null)
            {
                Debug.LogError($"[S3Orbits] RESULT: FAIL - no {k_BeatsPath} in MainScene.");
                return;
            }

            Transform beats = beatsGO.transform;
            Transform group = beats.Find(k_GroupName);

            if (group != null && replace)
            {
                Object.DestroyImmediate(group.gameObject);
                group = null;
                Debug.Log($"[S3Orbits] removed the existing '{k_GroupName}' for a rebuild.");
            }

            if (group == null)
            {
                var made = new GameObject(k_GroupName);
                made.transform.SetParent(beats, false);
                group = made.transform;
                group.position = new Vector3(centre.x, floorY, centre.z);
                Debug.Log($"[S3Orbits] '{k_GroupName}' created at {group.position:F3} -- " +
                          $"playspace xz at {from:F0}s, S3's own floor height.");
            }
            else
            {
                Debug.Log($"[S3Orbits] '{k_GroupName}' already present at {group.position:F3}, " +
                          $"left where it is. The playspace is at {centre:F3} and the floor at " +
                          $"{floorY:F3} -- Rebuild moves it back.");
            }

            // LEFT ON, like the originals. The activation track below is what hides the group,
            // and because it is on the master -- evaluated from the first frame of the show -- it
            // does so before anything is ever seen. A group switched off here instead would be
            // invisible to author, and its FootprintRings' Awake would never run to register
            // their debug rows.
            group.gameObject.SetActive(true);

            if (!CopyInto(group, k_OrbitsSource, "Orbits", 0f)) return;
            if (!CopyInto(group, k_PrintsSource, "Footprints", k_PrintGroupLift)) return;

            EnsureTrack(timeline, director, group.gameObject, from, to);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[S3Orbits] up from {from:F0}s to {to:F0}s ({to - from:F0}s). The S3 beats " +
                      $"themselves run out at {lastBeatEnd:F0}s; the clip goes to the end of the " +
                      "show, which is what the section's own wall and light cues do.");
            Debug.Log("[S3Orbits] RESULT: PASS");
        }

        /// <summary>
        /// Copy one of O-2's groups in, if it is not there already.
        ///
        /// Instantiate rather than a component-by-component rebuild, because it is the only thing
        /// that remaps the serialized references INSIDE the copy: FootprintRing's print array and
        /// LightOrbit's mover and control points all point at children, and a copy whose arrays
        /// still pointed at the O-2 originals would drive the onboarding room from S3.
        /// </summary>
        static bool CopyInto(Transform group, string sourcePath, string name, float lift)
        {
            Transform existing = group.Find(name);
            if (existing != null)
            {
                Debug.Log($"[S3Orbits] {name}: already present, left as it is.");
                Retag(existing.gameObject);
                return true;
            }

            GameObject source = Find(sourcePath);
            if (source == null)
            {
                Debug.LogError($"[S3Orbits] RESULT: FAIL - nothing at {sourcePath} to copy. Run " +
                               "Tools/Onboarding/Build Light Orbits and Tools/O-1/Build " +
                               "Footprints first.");
                return false;
            }

            GameObject copy = Object.Instantiate(source, group, false);
            copy.name = name;
            copy.transform.localPosition = new Vector3(0f, lift, 0f);
            copy.SetActive(true);

            Retag(copy);

            foreach (LightOrbit orbit in copy.GetComponentsInChildren<LightOrbit>(true))
                orbit.Rebuild();

            Debug.Log($"[S3Orbits] {name}: copied from {sourcePath} to local " +
                      $"{copy.transform.localPosition:F3}.");
            return true;
        }

        /// <summary>
        /// Give the copied rings their own debug ids. Idempotent, and run over an existing group
        /// too, so a group built before FootprintRing had the field gets it on the next run.
        /// </summary>
        static void Retag(GameObject root)
        {
            foreach (FootprintRing ring in root.GetComponentsInChildren<FootprintRing>(true))
            {
                var so = new SerializedObject(ring);
                SerializedProperty where = so.FindProperty("m_Where");
                if (where == null || where.stringValue == k_Where) continue;

                where.stringValue = k_Where;
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[S3Orbits] {ring.name}: debug id is now '{ring.debugId}'.");
            }
        }

        /// <summary>
        /// One activation clip spanning the section, on a track of its own, rewritten every run.
        ///
        /// Rewritten rather than added to: two clips on one activation track would leave a gap
        /// where the group is forced off, and running this builder twice is not meant to be a way
        /// of discovering that.
        /// </summary>
        static void EnsureTrack(TimelineAsset timeline, PlayableDirector director,
                                GameObject target, double from, double to)
        {
            ActivationTrack track = null;
            foreach (TrackAsset t in timeline.GetOutputTracks())
            {
                if (t is ActivationTrack found && found.name == k_TrackName)
                {
                    track = found;
                    break;
                }
            }

            bool made = track == null;
            if (made) track = timeline.CreateTrack<ActivationTrack>(null, k_TrackName);

            foreach (TimelineClip old in new List<TimelineClip>(track.GetClips()))
                track.DeleteClip(old);

            TimelineClip clip = track.CreateDefaultClip();
            clip.start = from;
            clip.duration = to - from;
            clip.displayName = k_ClipName;

            // Active, so the group stays up at the very end rather than blinking off on the last
            // frame of the show.
            track.postPlaybackState = ActivationTrack.PostPlaybackState.Active;

            director.SetGenericBinding(track, target);
            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(director);

            Debug.Log($"[S3Orbits] activation track '{k_TrackName}' " +
                      $"{(made ? "created" : "rewritten")}, {from:F0} -> {to:F0}s, bound to " +
                      $"{target.name}.");
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

        /// <summary>
        /// Where S3 begins and ends, taken from the running order on the timeline rather than
        /// from a table.
        ///
        /// The start is the first S3 control clip. The end is taken from THE SECTION'S OWN
        /// CUES -- the activation bands that begin on that same instant, which today is the outer
        /// directional coming up inside the blackout, ending at 1438, the very instant the S2 set
        /// S3 borrows is switched off again. That is the end of S3 whatever the beats happen to
        /// add up to: they run out at 1357 because S3's script is not finished, so following them
        /// would put the lights out eighty seconds before the room goes.
        ///
        /// NOT timeline.duration, WHICH IS WHATEVER THE LONGEST CLIP SAYS. There is an unbound
        /// activation band on Main running to 1900 -- somebody's work in progress, pointing at
        /// nothing -- and reading the duration made this track 462 seconds longer than the
        /// section it is meant to cover. An unbound track drives no object, so it is not evidence
        /// of how long anything lasts, and the scan below skips those.
        /// </summary>
        static bool TryS3Span(TimelineAsset timeline, PlayableDirector director, out double from,
                              out double to, out double lastBeatEnd)
        {
            from = double.MaxValue;
            lastBeatEnd = 0d;
            to = 0d;

            foreach (TrackAsset t in timeline.GetOutputTracks())
            {
                if (t is not ControlTrack || !t.name.StartsWith("S3-")) continue;

                foreach (TimelineClip c in t.GetClips())
                {
                    if (c.start < from) from = c.start;
                    if (c.end > lastBeatEnd) lastBeatEnd = c.end;
                }
            }

            if (from >= double.MaxValue) return false;

            to = lastBeatEnd;
            foreach (TrackAsset t in timeline.GetOutputTracks())
            {
                if (t is not ActivationTrack track || track.name == k_TrackName) continue;
                if (director.GetGenericBinding(track) == null) continue;

                foreach (TimelineClip c in track.GetClips())
                {
                    if (System.Math.Abs(c.start - from) > 0.001d) continue;
                    if (c.end > to) to = c.end;
                }
            }

            return to > from;
        }

        /// <summary>
        /// The playspace's position at a show time, from the Animation track that carries it.
        ///
        /// An infinite (recorded) clip, so its curve times are show seconds and
        /// AnimationCurve.Evaluate clamps past the last key -- which is the whole point here: the
        /// move stops being keyed at 715s, in S1, and every section after that reads as the held
        /// value.
        /// </summary>
        static bool TryPlayspaceAt(PlayableDirector director, TimelineAsset timeline, double when,
                                   out Vector3 where)
        {
            where = Vector3.zero;

            foreach (TrackAsset t in timeline.GetOutputTracks())
            {
                if (t is not AnimationTrack track) continue;
                if (director.GetGenericBinding(track) is not Animator animator) continue;
                if (animator.GetComponent<Playspace>() == null) continue;

                AnimationClip clip = track.infiniteClip;
                if (clip == null) continue;

                int got = 0;
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(Transform) || !string.IsNullOrEmpty(binding.path))
                        continue;

                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.length == 0) continue;

                    float at = curve.Evaluate((float)when);
                    switch (binding.propertyName)
                    {
                        case "m_LocalPosition.x": where.x = at; got++; break;
                        case "m_LocalPosition.y": where.y = at; got++; break;
                        case "m_LocalPosition.z": where.z = at; got++; break;
                    }
                }

                if (got == 0) continue;

                // The curve is a LOCAL position. The playspace is a root object today, so this
                // changes nothing -- it is here so that parenting it tomorrow does not silently
                // move the rings somewhere else.
                Transform parent = animator.transform.parent;
                if (parent != null) where = parent.TransformPoint(where);

                Debug.Log($"[S3Orbits] playspace at {when:F0}s is {where:F3}, from " +
                          $"'{track.name}' on Main ({got} of 3 position curves).");
                return true;
            }

            return false;
        }

        /// <summary>What S3 stands on, as a world height.</summary>
        static bool TryFloorY(out float y)
        {
            y = 0f;

            GameObject floor = Find(k_FloorPath);
            Renderer renderer = floor != null ? floor.GetComponent<Renderer>() : null;
            if (renderer == null) return false;

            y = renderer.bounds.max.y;
            Debug.Log($"[S3Orbits] S3's floor is '{floor.name}' at y {y:F3}.");
            return true;
        }

        /// <summary>
        /// GameObject.Find by path, but through inactive objects too -- which it has to be: the S2
        /// set that carries S3's floor is driven by an activation track and is as likely as not
        /// switched off while this runs.
        /// </summary>
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
