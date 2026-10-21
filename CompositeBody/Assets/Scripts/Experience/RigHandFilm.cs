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
        [SerializeField, Tooltip("Draw the membrane on the RIG's hands as well as on Ch36's.\n\n" +
                                 "Off, which is the case now that Ch36's own hands track: the " +
                                 "body wears the membrane and there is one pair of hands. On, " +
                                 "the rig's hands carry a film of their own -- which is a second " +
                                 "membrane hand floating at the real hand's position, since the " +
                                 "rig and the avatar are different object trees that only " +
                                 "roughly agree.")]
        bool m_DrawOnRig;

        MembraneReveal m_Reveal;

        /// <summary>Set when the film must be put away, cleared once that has been done.</summary>
        bool m_Suppress;

        void OnEnable()
        {
            m_Reveal = GetComponent<MembraneReveal>();

            // OFF BY DEFAULT, and off means invisible rather than merely unregistered: without
            // registering it would simply sit at whatever reveal value it was authored with --
            // and both rig films are authored m_Reveal: 1, a fully grown hand, which is what was
            // visible beside Ch36's before anyone had touched a 真人.
            //
            // THROUGH THE REVEAL, NOT THE RENDERER. The first attempt set forceRenderingOff here
            // and lost: MembraneReveal.Apply owns that flag -- "a separate flag that nothing else
            // in the project touches" is its own comment -- and rewrites it from m_Reveal, which
            // was 1, so the film came straight back. Setting the reveal to zero makes it hidden
            // through the mechanism that owns it, and nothing moves it afterwards because an
            // unregistered film is not in AvatarFormation's list.
            if (!m_DrawOnRig)
            {
                m_Suppress = true;
                Suppress();
                return;
            }

            m_Suppress = false;
            AvatarFormation.AddFilm(m_Reveal);
        }

        /// <summary>
        /// Again after everything has had its OnEnable, because Apply is a no-op until
        /// MembraneReveal has resolved its own renderer -- which it does in ITS OnEnable, and the
        /// order of two components on one object is not ours to choose.
        /// </summary>
        void LateUpdate()
        {
            if (!m_Suppress) return;
            m_Suppress = false;
            Suppress();
        }

        void Suppress()
        {
            if (m_Reveal != null) m_Reveal.SetRevealImmediate(0f);
        }

        void OnDisable()
        {
            if (!m_DrawOnRig) return;
            AvatarFormation.RemoveFilm(m_Reveal);
        }
    }
}
