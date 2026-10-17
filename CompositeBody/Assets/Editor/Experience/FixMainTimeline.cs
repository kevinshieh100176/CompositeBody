using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Makes the master timeline agree with the merged Onboarding prefab.
    ///
    /// The show spine was authored when O-0 to O-3 were four beats with four objects, so it has
    /// four Control clips in a row: the real beat for the first sixty seconds, then three empty
    /// placeholders for the next two hundred and thirty. A Control clip switches its target on
    /// for its own duration and off outside it, so the onboarding appeared, ran for a minute,
    /// and then disappeared for the rest of the section.
    ///
    /// ONE CLIP, NOT FOUR POINTING AT THE SAME OBJECT. Repointing all four would look tidier in
    /// the timeline window and would be worse: consecutive Control clips on one target
    /// deactivate and reactivate it at every boundary, and everything in this beat does its
    /// setup in Awake and OnEnable -- the formation unforms itself, the rig films re-register,
    /// the handover restarts. The player would watch their own body reset at sixty seconds.
    ///
    /// The tracks themselves are left in place, empty. They are the script's own beat names and
    /// are worth keeping as labels even when nothing is switched from them.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.FixMainTimeline.Run
    /// </summary>
    public static class FixMainTimeline
    {
        const string k_TimelinePath = "Assets/_Timeline/Main.playable";

        /// <summary>The track whose clip survives, and the ones whose clips are removed.</summary>
        const string k_KeepPrefix = "O-0";
        static readonly string[] k_ClearPrefixes = { "O-1", "O-2", "O-3" };

        [MenuItem("Tools/Onboarding/Fix Main Timeline")]
        public static void Run()
        {
            Debug.Log("[MainTimeline] Starting...");

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_TimelinePath);
            if (timeline == null)
            {
                Debug.LogError($"[MainTimeline] RESULT: FAIL - no timeline at {k_TimelinePath}.");
                return;
            }

            TrackAsset keep = null;
            var clear = new List<TrackAsset>();

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track.name.StartsWith(k_KeepPrefix)) keep = track;
                else if (k_ClearPrefixes.Any(p => track.name.StartsWith(p))) clear.Add(track);
            }

            if (keep == null)
            {
                Debug.LogError($"[MainTimeline] RESULT: FAIL - no track starting '{k_KeepPrefix}'.");
                return;
            }

            TimelineClip anchor = keep.GetClips().FirstOrDefault();
            if (anchor == null)
            {
                Debug.LogError($"[MainTimeline] RESULT: FAIL - '{keep.name}' has no clip to extend.");
                return;
            }

            // The onboarding runs from where O-0 starts to wherever the last of the others ends,
            // measured rather than assumed: those numbers are the show's, not this script's, and
            // they will move again as the piece is cut.
            double end = anchor.start + anchor.duration;
            int removed = 0;

            foreach (TrackAsset track in clear)
            {
                foreach (TimelineClip clip in track.GetClips().ToList())
                {
                    end = System.Math.Max(end, clip.start + clip.duration);
                    track.DeleteClip(clip);
                    removed++;
                }
            }

            double was = anchor.duration;
            anchor.duration = end - anchor.start;

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(k_TimelinePath, ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[MainTimeline] '{keep.name}' clip {anchor.start:F0}s, " +
                      $"duration {was:F0}s -> {anchor.duration:F0}s (ends {end:F0}s).\n" +
                      $"  {removed} placeholder clip(s) removed from: " +
                      string.Join(", ", clear.Select(t => t.name)) + "\n" +
                      "  Those tracks are kept as labels; the onboarding is now switched on once " +
                      "and stays on.\n" +
                      "[MainTimeline] RESULT: PASS");
        }
    }
}
