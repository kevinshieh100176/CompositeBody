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
    /// AND THE GROUP IS LEFT SWITCHED ON IN THE PREFAB, which looks wrong and is not. An
    /// activation track drives its target for the WHOLE timeline, not only during its clip:
    /// everywhere it has no clip the target is forced inactive, including before the first one.
    /// So the show hides these itself on its first evaluated frame, and leaving them on in the
    /// prefab costs nothing at runtime while buying two things that matter -- the paths can be
    /// seen while they are shaped by hand, and the components' Awake runs, so the footprints and
    /// the orbits have rows in the debug panel before they have appeared rather than popping
    /// into the list when they fire.
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

            // BY TYPE NAME, not by type. This script has to keep compiling after LightOrbitGate
            // is deleted from the project, and it has to run BEFORE the delete -- a component
            // whose script is gone leaves a missing-script entry in the prefab that is far more
            // annoying to remove than the component was.
            int removed = 0;
            foreach (Component component in orbits.GetComponents<Component>())
            {
                if (component == null || component.GetType().Name != "LightOrbitGate") continue;
                Object.DestroyImmediate(component, true);
                removed++;
            }

            if (removed > 0)
                Debug.Log($"[O1Payoff] removed {removed} LightOrbitGate from the Orbits group; " +
                          "the timeline owns the cue now.");

            if (orbits.GetComponent<Animator>() == null)
            {
                orbits.gameObject.AddComponent<Animator>();
                Debug.Log("[O1Payoff] added an Animator to the Orbits group for the fade track.");
            }

            // Switched ON, deliberately: the activation track hides it at runtime, and an
            // object that is off in the prefab is one whose Awake never runs -- so its debug row
            // would not exist until the moment it appeared.
            if (!orbits.gameObject.activeSelf)
            {
                orbits.gameObject.SetActive(true);
                Debug.Log("[O1Payoff] left the Orbits group ON; the activation track hides it " +
                          "until the payoff.");
            }

            Transform footprints = content.Find("Footprints");
            if (footprints != null && !footprints.gameObject.activeSelf)
            {
                footprints.gameObject.SetActive(true);
                Debug.Log("[O1Payoff] left the Footprints group ON for the same reason.");
            }

            return true;
        }
    }
}
