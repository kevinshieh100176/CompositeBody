using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Makes S1-2's two audio tracks private to one player each: A is heard only by Player1, B
    /// only by Player2.
    ///
    /// The scene already had the two AudioSources, and the S1_2 director already bound one to
    /// each AudioTrack -- so the asymmetry was authored and then heard by everybody. All this
    /// adds is a <see cref="RoleAudible"/> on each bound source, which mutes it on the machine
    /// that is not that player. See RoleAudible for why muting is the only safe mechanism here.
    ///
    /// It also renames the tracks from Unity's defaults ("Audio Track", "Audio Track (1)") to
    /// the clip names, so the timeline surface says who hears what. The bindings key off the
    /// track object, not its name, so renaming cannot break them.
    ///
    /// Re-runnable and additive, like the other Configure tools: it fixes up what it finds and
    /// leaves a scene that is already in use alone.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.ConfigureS12RoleAudio.Run
    /// </summary>
    public static class ConfigureS12RoleAudio
    {
        public const string ScenePath = "Assets/_Scenes/MainScene.unity";
        public const string TimelineName = "S1_2";

        public static void Run()
        {
            Debug.Log("[S12Audio] Starting...");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[S12Audio] RESULT: FAIL - could not open {ScenePath}");
                return;
            }

            PlayableDirector director = Object
                .FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(d => d.playableAsset != null && d.playableAsset.name == TimelineName);

            if (director == null)
            {
                Debug.LogError($"[S12Audio] RESULT: FAIL - no PlayableDirector in {ScenePath} " +
                               $"playing {TimelineName}.playable.");
                return;
            }

            var timeline = (TimelineAsset)director.playableAsset;
            AudioTrack[] tracks = timeline.GetOutputTracks().OfType<AudioTrack>().ToArray();
            if (tracks.Length != 2)
            {
                Debug.LogError($"[S12Audio] RESULT: FAIL - expected 2 AudioTracks in " +
                               $"{TimelineName}, found {tracks.Length}. Nothing changed.");
                return;
            }

            int wired = 0;
            foreach (AudioTrack track in tracks)
            {
                // The track names are Unity's defaults, so which player a track belongs to is
                // carried by its clip's display name -- "S1-2-A" / "S1-2-B".
                TimelineClip clip = track.GetClips().FirstOrDefault();
                string label = clip != null ? clip.displayName : string.Empty;

                PlayerRole role;
                if (label.EndsWith("-A")) role = PlayerRole.Player1;
                else if (label.EndsWith("-B")) role = PlayerRole.Player2;
                else
                {
                    Debug.LogError($"[S12Audio] RESULT: FAIL - track '{track.name}' has clip " +
                                   $"'{label}', which ends in neither -A nor -B, so there is no " +
                                   "saying who should hear it. Nothing further changed.");
                    return;
                }

                var source = director.GetGenericBinding(track) as AudioSource;
                if (source == null)
                {
                    Debug.LogError($"[S12Audio] RESULT: FAIL - track '{track.name}' ({label}) is " +
                                   "bound to no AudioSource, so there is nothing to make private.");
                    return;
                }

                EnsureRoleAudible(source, role, label);
                RenameTrack(timeline, track, label, role);
                wired++;
            }

            EditorUtility.SetDirty(timeline);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[S12Audio] Saved {ScenePath} and {TimelineName}.playable ({wired} tracks).");

            if (!Verify(director, tracks)) return;

            Debug.Log("[S12Audio] RESULT: PASS");
        }

        static void EnsureRoleAudible(AudioSource source, PlayerRole role, string label)
        {
            var presenter = source.GetComponent<RoleAudible>();
            if (presenter == null)
            {
                presenter = source.gameObject.AddComponent<RoleAudible>();
                Debug.Log($"[S12Audio] Added RoleAudible to '{source.name}' for {label}.");
            }

            var so = new SerializedObject(presenter);
            SerializedProperty roles = so.FindProperty("m_AudibleToRoles");
            roles.arraySize = 1;
            roles.GetArrayElementAtIndex(0).intValue = (int)role;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[S12Audio] {label} -> '{source.name}', audible to {role} only.");
        }

        static void RenameTrack(TimelineAsset timeline, AudioTrack track, string label, PlayerRole role)
        {
            string wanted = $"{label} ({role})";
            if (track.name == wanted) return;

            string was = track.name;
            track.name = wanted;
            EditorUtility.SetDirty(track);
            EditorUtility.SetDirty(timeline);
            Debug.Log($"[S12Audio] Renamed track '{was}' -> '{wanted}'.");
        }

        static bool Verify(PlayableDirector director, AudioTrack[] tracks)
        {
            var seen = new List<PlayerRole>();

            foreach (AudioTrack track in tracks)
            {
                var source = director.GetGenericBinding(track) as AudioSource;
                var presenter = source != null ? source.GetComponent<RoleAudible>() : null;
                if (presenter == null)
                {
                    Debug.LogError($"[S12Audio] RESULT: FAIL - '{track.name}' has no RoleAudible " +
                                   "on its bound source after the run.");
                    return false;
                }

                var so = new SerializedObject(presenter);
                SerializedProperty roles = so.FindProperty("m_AudibleToRoles");
                if (roles.arraySize != 1)
                {
                    Debug.LogError($"[S12Audio] RESULT: FAIL - '{track.name}' is audible to " +
                                   $"{roles.arraySize} roles; one is the point of the component.");
                    return false;
                }

                seen.Add((PlayerRole)roles.GetArrayElementAtIndex(0).intValue);
            }

            // Both tracks private to the same player is the failure that sounds like success on
            // one headset: that player hears the whole beat, and nobody notices the other is
            // silent until there are two people in the room.
            if (seen.Count != 2 || seen[0] == seen[1])
            {
                Debug.LogError($"[S12Audio] RESULT: FAIL - the two tracks resolved to " +
                               $"{string.Join(" and ", seen)}; they must be different players.");
                return false;
            }

            Debug.Log($"[S12Audio] Verified: one track each for {seen[0]} and {seen[1]}.");
            return true;
        }
    }
}
