using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Puts the 黑場 that covers the S2 → S3 handover on the show timeline, and keeps it centred
    /// on that join.
    ///
    /// ONE MORE SECTION BOUNDARY, THE SAME TREATMENT AS THE OTHER TWO. Main.playable already
    /// carries a blackout where the Onboarding hands over to S1 and another where S1 hands over
    /// to S2, both on the hand-authored Fade track, both 10s, both straddling the join. S3 did
    /// not have one: S2-3 ran out and S3A-1 simply appeared. This authors the third.
    ///
    /// WHY A TOOL RATHER THAN A DRAG. The join is not a number, it is wherever S2's last beat
    /// now ends -- and that has moved twice this week, because S2 and S3 are being retimed in
    /// the Timeline window beat by beat. A blackout dragged into place by hand is correct until
    /// the next retime and then silently fires inside S2-3. So the boundary is read off the
    /// Control clips every run: re-run this after moving a beat and the blackout follows it.
    /// <see cref="RetimeOnboardingHandoff"/> made the same argument for the first boundary.
    ///
    /// IT STRADDLES THE JOIN, half either side, which is the rule the other two blackouts
    /// already follow: a fade that starts at the cut is still clear at the moment of the cut, so
    /// S3A-1 would appear in front of the audience and only then go dark. Centred, the darkest
    /// part of the hold sits on the boundary and S3A-1's first five seconds play under it.
    ///
    /// ONE CLIP, NOT THREE. The curve is the whole out-hold-in -- up over the first third, flat
    /// across the middle, down over the last -- for the reason <see cref="ScreenFadeBehaviour"/>
    /// gives: three abutting clips say the same thing and can develop a gap between them, and a
    /// gap in a blackout is a frame of the next section arriving early.
    ///
    /// AND IT DOES NOT TOUCH THE LIGHTING, which is the one way this differs from the S1 → S2
    /// blackout. That clip carries the switch from S1's bake to S2's, because a blackout is the
    /// only moment a whole lightmap array can be swapped unseen. There is no S3 look to switch
    /// to yet -- LightingDirector in MainScene knows Onboarding, S1 and S2 -- and naming one
    /// that does not exist costs a warning per run and leaves the lighting where it was. So the
    /// fields stay empty, S3 opens wearing S2's lighting, and when S3's bake lands the switch
    /// belongs on THIS clip: lightingLookBefore "S2", lightingLook "S3", switch at 0.5.
    ///
    /// BY NAME, AND ONLY BY NAME. The Fade track is somebody's hand work and carries every
    /// section's blackout; this moves the one clip called <see cref="k_FadeClip"/> and reports
    /// what it left alone. An earlier version of RetimeOnboardingHandoff swept every clip on the
    /// track onto its own boundary and reported success, which put another section's blackout
    /// 300s out of place -- the sort of damage that looks like nothing until the show runs.
    ///
    /// IDEMPOTENT. The clip is found before it is made and anchored to an absolute time rather
    /// than shifted by a delta, so a second run reports no change. A duration somebody has
    /// widened by hand is left alone: how long a blackout lasts is a decision, only where it
    /// sits is derivable.
    ///
    /// IT OPENS NO SCENE, deliberately. Every Build* tool in this folder opens MainScene Single,
    /// and the S2/S3 retime this blackout is for is unsaved work in that scene more often than
    /// not. Nothing here needs the scene: a ScreenFadeClip holds its own settings, so the whole
    /// edit is inside Main.playable.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildS2S3Blackout.Run
    /// </summary>
    public static class BuildS2S3Blackout
    {
        const string k_Timeline = "Assets/_Timeline/Main.playable";
        const string k_FadeTrack = "Fade";

        /// <summary>
        /// The clip this tool owns. The arrow is the same U+2192 the other two use, so the three
        /// blackouts read as one set on the track.
        /// </summary>
        const string k_FadeClip = "黑場 S2 → S3";

        /// <summary>What the sub-asset is called, matching the two already in the file.</summary>
        const string k_ClipAsset = "黑場";

        /// <summary>
        /// Ten seconds, which is what the other two handovers are. Only used when the clip is
        /// being created; after that its width is whoever authored it's business.
        /// </summary>
        const double k_Duration = 10d;

        /// <summary>
        /// How the sections are told apart: a Control track whose label starts with "S2" is S2's
        /// beat, "S3" is S3's. Labels and not a beat list, because S2 and S3 are hand-authored
        /// and a list here would be a second place to maintain the running order -- which is
        /// exactly how RetimeOnboardingHandoff's own list went stale.
        /// </summary>
        const string k_Leaving = "S2";
        const string k_Arriving = "S3";

        [MenuItem("Tools/Show/Build the S2 → S3 Blackout")]
        public static void Run()
        {
            Debug.Log("[S2S3] Starting...");

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (timeline == null)
            {
                Debug.LogError($"[S2S3] RESULT: FAIL - no TimelineAsset at {k_Timeline}.");
                return;
            }

            if (!TryFindBoundary(timeline, out double boundary)) return;

            PlayableTrack fadeTrack = FindFadeTrack(timeline);
            if (fadeTrack == null)
            {
                Debug.LogError($"[S2S3] RESULT: FAIL - no '{k_FadeTrack}' track on the timeline. " +
                               "The other two blackouts live on it; this tool will not author a " +
                               "second fade track beside it.");
                return;
            }

            TimelineClip clip = Find(fadeTrack, k_FadeClip);
            bool created = false;

            if (clip == null)
            {
                clip = fadeTrack.CreateClip<ScreenFadeClip>();
                clip.displayName = k_FadeClip;
                clip.duration = k_Duration;

                var asset = (ScreenFadeClip)clip.asset;
                asset.name = k_ClipAsset;
                asset.template.color = Color.black;
                asset.template.alpha = OutHoldIn();

                // Left empty on purpose; see the remarks. The switch point is still set, so the
                // day S3's bake lands only the two look names have to be filled in.
                asset.template.lightingLook = "";
                asset.template.lightingLookBefore = "";
                asset.template.lightingSwitchAt = 0.5f;

                created = true;
                Debug.Log($"[S2S3] created '{k_FadeClip}' on the '{k_FadeTrack}' track, " +
                          $"{k_Duration:F0}s, black, no lighting switch.");
            }

            double start = boundary - clip.duration * 0.5d;
            bool moved = Anchor(clip, start, boundary);

            Report(fadeTrack);

            if (!created && !moved)
            {
                Debug.Log("[S2S3] nothing to do; the blackout is already on the join.\n" +
                          "[S2S3] RESULT: PASS");
                return;
            }

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(k_Timeline, ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[S2S3] '{k_FadeClip}' covers {clip.start:F1}-{clip.start + clip.duration:F1}s, " +
                      $"darkest on {boundary:F1}s.\n[S2S3] RESULT: PASS");
        }

        /// <summary>
        /// Where S2 hands over to S3, read off the Control clips rather than typed in, so this
        /// cannot disagree with the running order it is following.
        ///
        /// NORMALLY THE TWO AGREE and the boundary is simply that second. When they do not --
        /// a retime in progress, a beat not yet pulled back -- the join is the middle of the gap
        /// and the numbers are both logged, because a blackout centred on one end of a 20s hole
        /// is worse than one centred in it and the hole is the thing to go and look at.
        /// </summary>
        static bool TryFindBoundary(TimelineAsset timeline, out double boundary)
        {
            boundary = 0d;

            double leaves = double.MinValue;
            double arrives = double.MaxValue;
            int left = 0;
            int arriving = 0;

            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is not ControlTrack) continue;

                bool isLeaving = track.name.StartsWith(k_Leaving);
                bool isArriving = track.name.StartsWith(k_Arriving);
                if (!isLeaving && !isArriving) continue;

                foreach (TimelineClip clip in track.GetClips())
                {
                    if (isLeaving)
                    {
                        leaves = System.Math.Max(leaves, clip.start + clip.duration);
                        left++;
                    }
                    else
                    {
                        arrives = System.Math.Min(arrives, clip.start);
                        arriving++;
                    }
                }
            }

            if (left == 0 || arriving == 0)
            {
                Debug.LogError($"[S2S3] RESULT: FAIL - found {left} '{k_Leaving}' beat(s) and " +
                               $"{arriving} '{k_Arriving}' beat(s) on the running order. Both " +
                               "sides of the join have to be there for it to be worked out; " +
                               "nothing was moved.");
                return false;
            }

            if (System.Math.Abs(arrives - leaves) < 0.1d)
            {
                boundary = leaves;
                Debug.Log($"[S2S3] {k_Leaving} ends and {k_Arriving} starts at {boundary:F1}s " +
                          $"({left} + {arriving} beats).");
                return true;
            }

            boundary = (leaves + arrives) * 0.5d;
            Debug.LogWarning($"[S2S3] {k_Leaving} ends at {leaves:F1}s but {k_Arriving} starts at " +
                             $"{arrives:F1}s -- {System.Math.Abs(arrives - leaves):F1}s apart. The " +
                             $"blackout is centred in the middle of that at {boundary:F1}s, which " +
                             "is the best this can do; the two beats are the thing to look at.");
            return true;
        }

        static PlayableTrack FindFadeTrack(TimelineAsset timeline)
        {
            foreach (TrackAsset track in timeline.GetRootTracks())
                if (track is PlayableTrack playable && track.name == k_FadeTrack) return playable;

            return null;
        }

        static TimelineClip Find(TrackAsset track, string displayName)
        {
            foreach (TimelineClip clip in track.GetClips())
                if (clip.displayName == displayName) return clip;

            return null;
        }

        /// <summary>
        /// Centre the clip on the join. A thousandth of a second of slack, so a clip already in
        /// place is not reported as moved and the asset is not dirtied for nothing.
        /// </summary>
        static bool Anchor(TimelineClip clip, double start, double boundary)
        {
            if (System.Math.Abs(clip.start - start) < 0.001d) return false;

            Debug.Log($"[S2S3] '{clip.displayName}': {clip.start:F1}-{clip.start + clip.duration:F1}s -> " +
                      $"{start:F1}-{start + clip.duration:F1}s, centred on {boundary:F1}.");
            clip.start = start;
            return true;
        }

        /// <summary>
        /// Say what else is on the Fade track and was not touched. The whole hazard with this
        /// track is a tool that moves a blackout it does not own, so each run states plainly
        /// which ones it kept its hands off.
        /// </summary>
        static void Report(TrackAsset fadeTrack)
        {
            foreach (TimelineClip clip in fadeTrack.GetClips())
            {
                if (clip.displayName == k_FadeClip) continue;

                Debug.Log($"[S2S3] left '{clip.displayName}' alone at " +
                          $"{clip.start:F1}-{clip.start + clip.duration:F1}s.");
            }
        }

        /// <summary>
        /// The blackout's shape: clear, dark across the middle third, clear again. The same four
        /// keys the other two handovers carry, so all three fades look and read alike.
        /// </summary>
        static AnimationCurve OutHoldIn() => new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(1f / 3f, 1f),
            new Keyframe(2f / 3f, 1f),
            new Keyframe(1f, 0f));
    }
}
