using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Puts the hand-authored clips that straddle the Onboarding → S1 boundary back where that
    /// boundary actually is.
    ///
    /// THREE CLIPS ON MAIN.PLAYABLE ARE NOT THIS PROJECT'S BUILDERS' WORK. Somebody sat down in
    /// the Timeline window and added them:
    ///
    ///   - an Activation track holding Onboarding/Content up for the whole onboarding block,
    ///   - another holding Beat_S1/Content up for the stretch after it,
    ///   - a Fade track carrying the 黑場 ScreenFadeClip across the join.
    ///
    /// All three are anchored to ONE NUMBER -- the second the onboarding ends -- and that number
    /// moves whenever a beat's duration changes in BuildMainTimeline's running order. It moved
    /// from 290 to 310 when O-0 grew to contain the player's body arriving, and nothing told the
    /// clips: the blackout would have landed in the middle of O-3 instead of at the end of it,
    /// and the onboarding room would have switched off twenty seconds early.
    ///
    /// NOT PART OF THE BUILD, AND DELIBERATELY SO. This runs when you ask it to, because these
    /// are hand decisions and a builder that re-positioned them on every run would be the same
    /// mistake in the other direction. Run it after changing a beat's duration.
    ///
    /// IDEMPOTENT. It anchors rather than shifts: the boundary is recomputed from the running
    /// order each time, so running it twice does nothing the second time and there is no delta
    /// to accumulate. Durations are left exactly as they were -- how long a blackout lasts is
    /// not a thing this can work out.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.RetimeOnboardingHandoff.Run
    /// </summary>
    public static class RetimeOnboardingHandoff
    {
        const string k_Timeline = "Assets/_Timeline/Main.playable";

        /// <summary>
        /// The beats that make up the onboarding, by the label BuildMainTimeline gives their
        /// Control tracks. The boundary is where the last of them ends.
        /// </summary>
        static readonly string[] k_Onboarding =
        {
            "O-0 進入", "O-1 鬼的控制", "O-2 鬼的抓取", "O-3 Onboarding結束",
        };

        /// <summary>What the clips are called, and what each one is anchored to.</summary>
        const string k_OnboardingHold = "Active";
        const string k_FadeTrack = "Fade";

        [MenuItem("Tools/Show/Re-anchor the Onboarding Handoff")]
        public static void Run()
        {
            Debug.Log("[Retime] Starting...");

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (timeline == null)
            {
                Debug.LogError($"[Retime] RESULT: FAIL - no TimelineAsset at {k_Timeline}.");
                return;
            }

            if (!TryFindBoundary(timeline, out double boundary)) return;

            Debug.Log($"[Retime] the onboarding ends at {boundary:F1}s.");

            int moved = 0;

            // The two activation clips: one runs up to the boundary from zero, the other starts
            // at it. Told apart by where they already are rather than by name, because both are
            // called "Active" -- an activation clip is named by Timeline, not by the person.
            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is ActivationTrack)
                {
                    foreach (TimelineClip clip in track.GetClips())
                    {
                        if (clip.start < 1d) moved += Anchor(clip, 0d, boundary - 0d, "holds the onboarding room up");
                        else moved += Anchor(clip, boundary, clip.duration, "holds what comes after it up");
                    }
                    continue;
                }

                // The fade keeps its own length and stays centred on the join, which is what a
                // blackout across a boundary is: half of it either side.
                if (track is PlayableTrack && track.name == k_FadeTrack)
                {
                    foreach (TimelineClip clip in track.GetClips())
                        moved += Anchor(clip, boundary - clip.duration * 0.5d, clip.duration,
                                        "covers the join");
                }
            }

            if (moved == 0)
            {
                Debug.Log("[Retime] nothing to move; everything is already anchored.\n" +
                          "[Retime] RESULT: PASS");
                return;
            }

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(k_Timeline, ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[Retime] moved {moved} clip(s).\n[Retime] RESULT: PASS");
        }

        /// <summary>
        /// Where the onboarding ends, read off the Control clips rather than from a constant, so
        /// this cannot disagree with the running order it is supposed to be following.
        /// </summary>
        static bool TryFindBoundary(TimelineAsset timeline, out double boundary)
        {
            boundary = 0d;
            var found = new List<string>();

            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is not ControlTrack) continue;
                if (System.Array.IndexOf(k_Onboarding, track.name) < 0) continue;

                found.Add(track.name);
                foreach (TimelineClip clip in track.GetClips())
                    boundary = System.Math.Max(boundary, clip.start + clip.duration);
            }

            if (found.Count == k_Onboarding.Length) return true;

            Debug.LogError($"[Retime] RESULT: FAIL - found {found.Count} of the " +
                           $"{k_Onboarding.Length} onboarding beats on the running order " +
                           $"({string.Join(", ", found)}). The labels in this file and the ones " +
                           "in BuildMainTimeline's running order have to match, or the boundary " +
                           "cannot be worked out.");
            return false;
        }

        /// <summary>
        /// Move one clip, and say so. A tenth of a second of slack, because these were placed by
        /// dragging and a clip that is already where it should be must not be reported as moved.
        /// </summary>
        static int Anchor(TimelineClip clip, double start, double duration, string what)
        {
            const double k_Slack = 0.1d;

            if (System.Math.Abs(clip.start - start) < k_Slack &&
                System.Math.Abs(clip.duration - duration) < k_Slack)
            {
                Debug.Log($"[Retime] '{clip.displayName}' already {what}: " +
                          $"{clip.start:F1}-{clip.start + clip.duration:F1}s.");
                return 0;
            }

            Debug.Log($"[Retime] '{clip.displayName}' {what}: " +
                      $"{clip.start:F1}-{clip.start + clip.duration:F1}s -> " +
                      $"{start:F1}-{start + duration:F1}s.");

            clip.start = start;
            clip.duration = duration;
            return 1;
        }
    }
}
