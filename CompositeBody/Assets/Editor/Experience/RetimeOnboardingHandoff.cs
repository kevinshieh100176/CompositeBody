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
    /// IT OWNS TWO CLIPS AND LEAVES EVERY OTHER ONE ALONE, which is the lesson of the sections
    /// that were authored after it. The timeline it was written against had one blackout and two
    /// activation clips, so "every clip on the Fade track" and "every activation clip that does
    /// not start at zero" named exactly the right things. It now carries three blackouts
    /// (Onboarding → S1, S1 → S2, S2 → S3) and three activation clips, and those two rules had
    /// become "sweep the lot onto 400s": the S2 → S3 blackout would have gone from 1176 to 395
    /// and S2's room would have been switched on three hundred seconds early, both reported as
    /// success. RetimeS1 hit the same wall and settled it the same way -- own the clip you are
    /// responsible for, name what you left behind. So the blackout is found by name and the
    /// activation clips by being the FIRST TWO in time; anything further down the timeline
    /// belongs to a later handover and is logged, not moved.
    ///
    /// AND THE ONBOARDING BLOCK IS FOUND BY PREFIX, not by a list of labels. This file used to
    /// hold the four names BuildMainTimeline gives the onboarding's Control tracks, which is a
    /// second copy of the running order and went stale the way second copies do: O-1 became
    /// 鬼的抓取 and O-2 鬼魂交流 when the sheet was re-read, and O-3's track was dropped from the
    /// timeline by hand. The list then matched nothing, so every run failed on the boundary and
    /// never reached the clips -- which is the one reason the sweep above never did its damage.
    /// Two bugs cancelling out is not a tool that works. Any Control track labelled O-something
    /// is an onboarding beat, the boundary is where the last of them ends, and renaming a beat
    /// or dropping one cannot put this out of step again.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.RetimeOnboardingHandoff.Run
    /// </summary>
    public static class RetimeOnboardingHandoff
    {
        const string k_Timeline = "Assets/_Timeline/Main.playable";

        /// <summary>
        /// What an onboarding beat's Control track is called at the front. Every other section's
        /// tracks start S1, S2 or S3, so this picks out the block and nothing else.
        /// </summary>
        const string k_OnboardingPrefix = "O-";

        /// <summary>The track the blackouts live on, and the one of them this tool owns.</summary>
        const string k_FadeTrack = "Fade";
        const string k_FadeClip = "黑場 Onboarding → S1";

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
            moved += Activations(timeline, boundary);
            moved += Fade(timeline, boundary);

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
                if (!track.name.StartsWith(k_OnboardingPrefix)) continue;

                found.Add(track.name);
                foreach (TimelineClip clip in track.GetClips())
                    boundary = System.Math.Max(boundary, clip.start + clip.duration);
            }

            if (found.Count > 0 && boundary > 0d)
            {
                Debug.Log($"[Retime] the onboarding is {found.Count} beat(s): " +
                          $"{string.Join(", ", found)}.");
                return true;
            }

            Debug.LogError("[Retime] RESULT: FAIL - no Control track labelled " +
                           $"'{k_OnboardingPrefix}...' carries a clip on the running order, so " +
                           "the boundary cannot be worked out and nothing was moved. The " +
                           "onboarding beats are the ones BuildMainTimeline labels O-0, O-1 and " +
                           "so on; if they have been renamed, this prefix is the one thing to fix.");
            return false;
        }

        /// <summary>
        /// Anchor the pair of activation clips that meet at the join: the one holding the
        /// onboarding room up, and the one holding up whatever comes after it.
        ///
        /// THE FIRST TWO IN TIME, AND ONLY THOSE TWO. Every activation clip is called "Active"
        /// -- Timeline names them, not the person -- so position is all there is to tell them
        /// apart by, and the old rule for the second one was "it does not start at zero". That
        /// was true of exactly one clip once. S2's room is held up by a third clip at 767s now,
        /// which that rule would have dragged to the onboarding boundary, switching S2's world on
        /// in the middle of S1. Anything after the second clip is another section's handover.
        /// </summary>
        static int Activations(TimelineAsset timeline, double boundary)
        {
            var clips = new List<TimelineClip>();

            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is not ActivationTrack) continue;
                clips.AddRange(track.GetClips());
            }

            clips.Sort((a, b) => a.start.CompareTo(b.start));

            if (clips.Count == 0)
            {
                Debug.LogWarning("[Retime] no activation clips on the timeline, so the room " +
                                 "handover was not retimed. The blackout still was.");
                return 0;
            }

            int moved = Anchor(clips[0], 0d, boundary, "holds the onboarding room up");

            if (clips.Count > 1)
                moved += Anchor(clips[1], boundary, clips[1].duration, "holds what comes after it up");

            for (int i = 2; i < clips.Count; i++)
            {
                Debug.Log($"[Retime] left the activation clip at {clips[i].start:F1}-" +
                          $"{clips[i].start + clips[i].duration:F1}s alone; it belongs to a " +
                          "later section's handover, not to this one.");
            }

            return moved;
        }

        /// <summary>
        /// Centre this tool's blackout on the join, and say which of the others it left where
        /// they were.
        ///
        /// The fade keeps its own length and stays centred on the join, which is what a blackout
        /// across a boundary is: half of it either side. BY NAME, because the track carries one
        /// blackout per section handover and a tool that moves somebody else's is the sort of
        /// damage that looks like nothing until the show runs.
        /// </summary>
        static int Fade(TimelineAsset timeline, double boundary)
        {
            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is not PlayableTrack || track.name != k_FadeTrack) continue;

                int moved = 0;
                bool found = false;

                foreach (TimelineClip clip in track.GetClips())
                {
                    if (clip.displayName == k_FadeClip)
                    {
                        found = true;
                        moved += Anchor(clip, boundary - clip.duration * 0.5d, clip.duration,
                                        "covers the join");
                        continue;
                    }

                    Debug.Log($"[Retime] left '{clip.displayName}' alone at {clip.start:F1}-" +
                              $"{clip.start + clip.duration:F1}s; that handover is not this " +
                              "tool's to place.");
                }

                if (!found)
                {
                    Debug.LogWarning($"[Retime] no clip called '{k_FadeClip}' on the " +
                                     $"'{k_FadeTrack}' track, so no blackout was retimed. " +
                                     "Nothing else on the track was moved in its place; rename " +
                                     "the clip, or fix the constant here, rather than letting " +
                                     "this guess.");
                }

                return moved;
            }

            Debug.LogWarning($"[Retime] no '{k_FadeTrack}' track on the timeline, so '{k_FadeClip}' " +
                             "was not retimed.");
            return 0;
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
