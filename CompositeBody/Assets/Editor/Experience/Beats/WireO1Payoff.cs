using UnityEditor;
using UnityEngine;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Prepares the Onboarding prefab for O-1: a cue and a gate report on each grabbable object,
    /// the footprint rings, and the orbit lights handed over to the timeline.
    ///
    /// THE ORBITS STOP OWNING THEIR OWN GATE. They used to be held back by LightOrbitGate, which
    /// waited for both players to touch a 真人 and then faded them in. The cue now belongs to the
    /// show's timeline: an activation track decides when and an animation track does the fade, so
    /// the component goes.
    ///
    /// AND NOTHING ELSE OWNS THEM EITHER. A <see cref="BeatContent"/> was added later to hold the
    /// two lights back to O3_Communion, which gave the group two masters: the activation track
    /// switched the parent on inside O-2 while the BeatContent kept the children off until the
    /// beat after it, so the authored fade ran on lights nobody could see. One owner, and it is
    /// the timeline -- so the component is stripped here, the same way LightOrbitGate was.
    ///
    /// WHICH MEANS THE GROUP SHIPS SWITCHED OFF. An activation track does force its target
    /// inactive everywhere it has no clip -- but only while its director is being EVALUATED, and
    /// these tracks live on the beat's own timeline, which the master only drives during O-2's
    /// clip. Before that nothing is hiding them, so whatever the prefab says is what the room
    /// shows: left on, they were up from the first frame of the show. Off is therefore the
    /// authored state, and it costs nothing in the panel because
    /// <see cref="CompositeBody.Diagnostics.InteractionDebugRoster"/> registers interactions on
    /// inactive objects for exactly this case. The one real cost is that the orbit paths cannot
    /// be seen while they are shaped by hand: switch the group on in the scene view and off again
    /// before saving.
    ///
    /// An Animator is added to the group because an Animation track has to bind to one; it has no
    /// controller and animates nothing on its own.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.WireO1Payoff.Run
    /// </summary>
    public static class WireO1Payoff
    {
        const string k_PrefabPath = "Assets/_Scenes/Onboarding.prefab";
        const string k_CueA = "Assets/_Audio/S0-1-A.wav";
        const string k_CueB = "Assets/_Audio/S0-1-B.wav";

        [MenuItem("Tools/O-1/Wire Grab Cues and Payoff")]
        public static void Run()
        {
            Debug.Log("[O1Payoff] Starting...");

            GameObject root = PrefabUtility.LoadPrefabContents(k_PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[O1Payoff] RESULT: FAIL - could not open {k_PrefabPath}.");
                return;
            }

            try
            {
                Transform content = root.transform.Find("Content");
                if (content == null)
                {
                    Debug.LogError("[O1Payoff] RESULT: FAIL - no Content in the prefab.");
                    return;
                }

                // The stations are under Beats/O_1 Grab, not Content -- the grab beat has its own
                // holder in the merged prefab -- so the search starts at the root.
                if (!WireStation(root.transform, "Coat (Player A)", PlayerRole.Player1, k_CueA)) return;
                if (!WireStation(root.transform, "Mug (Player B)", PlayerRole.Player2, k_CueB)) return;

                if (!HandOrbitsToTimeline(content)) return;

                if (!BuildFootprints.BuildInto(content)) return;

                PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
                Debug.Log("[O1Payoff] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The orbit hand-over on its own, without re-wiring the stations or rebuilding the
        /// prints. Separate because this is the part that has had to be re-applied after a
        /// component was added to the group by hand, and the rest of <see cref="Run"/> needs the
        /// cue clips present to get as far as it.
        ///
        /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
        ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.WireO1Payoff.HandOrbitsOnly
        /// </summary>
        [MenuItem("Tools/O-1/Hand the Orbits to the Timeline")]
        public static void HandOrbitsOnly()
        {
            Debug.Log("[O1Payoff] Handing the orbits to the timeline...");

            GameObject root = PrefabUtility.LoadPrefabContents(k_PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[O1Payoff] RESULT: FAIL - could not open {k_PrefabPath}.");
                return;
            }

            try
            {
                Transform content = root.transform.Find("Content");
                if (content == null)
                {
                    Debug.LogError("[O1Payoff] RESULT: FAIL - no Content in the prefab.");
                    return;
                }

                if (!HandOrbitsToTimeline(content)) return;

                PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
                Debug.Log("[O1Payoff] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static bool WireStation(Transform root, string name, PlayerRole owner, string cuePath)
        {
            Transform station = FindDeep(root, name);
            if (station == null)
            {
                Debug.LogError($"[O1Payoff] RESULT: FAIL - no '{name}' in the prefab. Run " +
                               "Tools/O-1/Build Grab Interaction first.");
                return false;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(cuePath);
            if (clip == null)
            {
                Debug.LogError($"[O1Payoff] RESULT: FAIL - no clip at {cuePath}.");
                return false;
            }

            var existing = station.GetComponent<O1GrabStation>();
            if (existing != null)
            {
                Debug.Log($"[O1Payoff] '{name}' already has a grab station; left as it is.");
                return true;
            }

            // The AudioSource is made here rather than left to the component's own Awake, so the
            // settings are visible in the prefab instead of appearing at runtime.
            var source = station.GetComponent<AudioSource>();
            if (source == null) source = station.gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.maxDistance = 12f;

            var grab = station.gameObject.AddComponent<O1GrabStation>();
            var so = new SerializedObject(grab);
            so.FindProperty("m_Owner").intValue = (int)owner;
            so.FindProperty("m_Cue").objectReferenceValue = clip;
            so.FindProperty("m_Source").objectReferenceValue = source;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[O1Payoff] '{name}': {owner} grabs it, cue {clip.name} " +
                      $"({clip.length:F1}s), reports 'beat.O1_Control'.");
            return true;
        }

        static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindDeep(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        static bool HandOrbitsToTimeline(Transform content)
        {
            Transform orbits = content.Find("Orbits");
            if (orbits == null)
            {
                Debug.LogError("[O1Payoff] RESULT: FAIL - no Content/Orbits in the prefab.");
                return false;
            }

            // LightOrbitGate BY TYPE NAME, not by type. This script has to keep compiling after
            // LightOrbitGate is deleted from the project, and it has to run BEFORE the delete --
            // a component whose script is gone leaves a missing-script entry in the prefab that
            // is far more annoying to remove than the component was.
            //
            // BeatContent by type, because that one is still in the project and is meant to stay:
            // it is the right tool for content that belongs to a beat, and wrong for these two
            // lights, whose moment is a point inside a beat that the timeline already names.
            int removed = 0;
            foreach (Component component in orbits.GetComponents<Component>())
            {
                if (component == null) continue;
                if (component.GetType().Name != "LightOrbitGate" && component is not BeatContent) continue;
                Debug.Log($"[O1Payoff] removed {component.GetType().Name} from the Orbits group; " +
                          "the timeline is the only thing that switches these lights now.");
                Object.DestroyImmediate(component, true);
                removed++;
            }

            if (orbits.GetComponent<Animator>() == null)
            {
                orbits.gameObject.AddComponent<Animator>();
                Debug.Log("[O1Payoff] added an Animator to the Orbits group for the fade track.");
            }

            // OFF IN THE PREFAB. The activation track that switches these on lives on O-2's own
            // timeline, and the master only drives that director during O-2's clip -- so nothing
            // hides them before the beat, and the authored state is what the room shows until
            // then. See the remarks on this class; the debug rows survive it.
            Hide(orbits.gameObject, "Orbits");

            Transform footprints = content.Find("Footprints");
            if (footprints != null) Hide(footprints.gameObject, "Footprints");

            return true;
        }

        static void Hide(GameObject group, string label)
        {
            if (!group.activeSelf) return;
            group.SetActive(false);
            Debug.Log($"[O1Payoff] switched the {label} group OFF; its activation track is what " +
                      "brings it in, and nothing is driving that track before the beat.");
        }
    }
}
