using UnityEngine;
using CompositeBody.Avatar.Skin;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Joins a membrane film on the XR rig to the player's own formation, so the hands the player
    /// actually looks at arrive on the touch with the rest of the body rather than being there
    /// from the start.
    ///
    /// 「玩家沒有明確的身體，和真人觸碰後，才出現自己的雙手，只能隱約看見自己的雙手。」 The hands the
    /// player sees are the XR hand visuals on the RIG, not the ones on their networked avatar --
    /// the avatar's hands are what the other player sees of them. Those two live in different
    /// object trees, which is why <see cref="AvatarFormation"/> cannot simply find these by
    /// searching its own children and why this exists.
    ///
    /// Registration rather than a reference in either direction: the rig is alive before any
    /// avatar spawns and stays alive after one despawns, so neither can hold the other and be
    /// sure it is still there. The film announces itself and is taken to whatever state the
    /// formation is already in.
    /// </summary>
    [RequireComponent(typeof(MembraneReveal))]
    public class RigHandFilm : MonoBehaviour
    {
        MembraneReveal m_Reveal;

        void OnEnable()
        {
            m_Reveal = GetComponent<MembraneReveal>();
            AvatarFormation.AddFilm(m_Reveal);
        }

        void OnDisable()
        {
            AvatarFormation.RemoveFilm(m_Reveal);
        }
    }
}
