using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Marks the object the show moves the players around on.
    ///
    /// The XR rig is parented to this in the scene, so moving it carries whoever is wearing the
    /// headset. The avatars find it through here and parent themselves to it at spawn, which
    /// they cannot do in the scene because they do not exist until the session starts.
    ///
    /// A marker rather than a name lookup or a tag: there is exactly one of these, the thing it
    /// names is load-bearing, and a renamed object should break at the point of the rename
    /// rather than three beats into a show.
    /// </summary>
    [DisallowMultipleComponent]
    public class Playspace : MonoBehaviour
    {
        /// <summary>The playspace in the loaded scene, or null before it has woken.</summary>
        public static Playspace Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[Playspace] '{name}' is a second playspace; '{Instance.name}' " +
                                 "is already the one everything rides on. The avatars will " +
                                 "follow that one and this will do nothing.", this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
