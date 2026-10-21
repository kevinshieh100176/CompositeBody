using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Finds every interaction in the loaded scenes and puts it on the debug list, INCLUDING the
    /// ones on objects that are switched off.
    ///
    /// WHY THIS IS NEEDED AT ALL. An interaction registers itself in Awake, and Unity does not
    /// run Awake on a component whose object is inactive -- so anything that ships switched off,
    /// or that lives inside a beat's content root before that beat runs, has no row until the
    /// moment it is turned on. That is the worst possible moment for it to appear: the row is
    /// missing exactly while someone is asking "why has this not happened yet", and a list that
    /// omits an interaction is indistinguishable from one that was never built.
    ///
    /// The orbits and the footprints are both off until O-2 switches them on, and the calibrator
    /// ships disabled. All three would otherwise be invisible to the panel for most of the show.
    ///
    /// A SCAN, NOT A SERIALIZED LIST, because a list is a thing to forget: an interaction added
    /// to the prefab next month would have to be remembered here, which is the failure the
    /// registry exists to avoid. The scan runs on load and whenever a scene is added -- never per
    /// frame -- and registration is idempotent, so an interaction that later wakes up and
    /// registers itself changes nothing.
    /// </summary>
    [DefaultExecutionOrder(100)]   // after the interactions' own Awake, so theirs win the race
    public class InteractionDebugRoster : MonoBehaviour
    {
        [SerializeField, Tooltip("Scan every loaded scene. Off scans only this object's own " +
                                 "hierarchy, for a roster that belongs to one prefab.")]
        bool m_AllLoadedScenes = true;

        readonly List<IDebugInteraction> m_Found = new();

        void OnEnable()
        {
            Scan();
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;

            // Only what this roster added, and only the entries nobody else is keeping alive:
            // a component that registered itself is its own business.
            foreach (IDebugInteraction found in m_Found) InteractionDebug.Unregister(found);
            m_Found.Clear();
        }

        // The experience scene is loaded additively part way through the show and brings its own
        // content with it.
        void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Scan();

        /// <summary>
        /// Walk the hierarchy and register everything that implements the interface. Public so
        /// the panel can ask for a rescan when it is opened, for content spawned since.
        /// </summary>
        public void Scan()
        {
            int added = 0;

            if (m_AllLoadedScenes)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded) continue;

                    foreach (GameObject root in scene.GetRootGameObjects())
                        added += ScanHierarchy(root);
                }
            }
            else
            {
                added += ScanHierarchy(gameObject);
            }

            if (added > 0)
                Debug.Log($"[InteractionDebug] roster found {added} interaction(s) that had not " +
                          "registered themselves -- switched-off objects, mostly.", this);
        }

        int ScanHierarchy(GameObject root)
        {
            int added = 0;

            // true: inactive objects included, which is the entire point.
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is not IDebugInteraction interaction) continue;
                if (InteractionDebug.IsRegistered(interaction)) continue;

                InteractionDebug.Register(interaction);
                m_Found.Add(interaction);
                added++;
            }

            return added;
        }
    }
}
