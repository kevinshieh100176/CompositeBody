using UnityEngine;

namespace CompositeBody.Avatar.Skin
{
    /// <summary>
    /// Keeps a membrane film shown exactly when the renderer it wraps is shown.
    ///
    /// The film is a separate object from the body part it covers, which is what lets it skin
    /// independently -- but it also means nothing else in the project knows it exists. The
    /// networked avatar hides parts of itself depending on whether it belongs to the local
    /// player or a remote one: you are not meant to see your own head from the inside, and your
    /// own hands come from the local rig rather than from your avatar. A film that ignored that
    /// would hang in front of the local player's face.
    ///
    /// Mirroring the source renderer rather than reimplementing the rule means the film follows
    /// whatever policy the avatar applies, including any the template changes later.
    /// </summary>
    public class MembraneFilmLink : MonoBehaviour
    {
        [SerializeField, Tooltip("The body renderer this film wraps. The film is shown only while this is.")]
        Renderer m_Source;

        [SerializeField, Tooltip("The film's own renderer. Found on this object if left empty.")]
        Renderer m_Film;

        /// <summary>The body renderer this film tracks. Exposed so tooling can check the pairing
        /// without having to guess it back from object names.</summary>
        public Renderer source => m_Source;

        public Renderer film => m_Film;

        void Awake()
        {
            if (m_Film == null) m_Film = GetComponent<Renderer>();
        }

        void LateUpdate()
        {
            if (m_Film == null) return;

            if (m_Source == null)
            {
                // Better to show the film than to hide the body silently: a missing link reads
                // as an authoring mistake rather than as an invisible player.
                m_Film.enabled = true;
                return;
            }

            m_Film.enabled = m_Source.enabled && m_Source.gameObject.activeInHierarchy;
        }
    }
}
