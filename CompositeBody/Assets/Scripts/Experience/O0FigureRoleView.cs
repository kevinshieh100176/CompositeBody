using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Each player sees only their own 真人: Player 1 the purple one, Player 2 the yellow.
    ///
    /// Local presentation only, like every <see cref="LocalRolePresenter"/>. Both clients hold
    /// the same scene with both figures in it and each independently hides the one that is not
    /// theirs, so the asymmetry costs no network traffic and there is no authoritative "who sees
    /// what" to get out of step. Nothing about the beat's state differs between the two machines
    /// -- only what is drawn.
    ///
    /// The whole station goes, not just the point cloud. A 光圈 left burning over an empty patch
    /// of floor reads as a bug rather than as a light with nobody in it, and the music-box plate
    /// under an absent figure would be stranger still.
    ///
    /// Until the role is known -- in the editor with no session, or in the moments before the
    /// roles replicate -- BOTH are shown. Showing nothing would make a scene that is merely not
    /// connected look like a scene that is broken.
    /// </summary>
    public class O0FigureRoleView : LocalRolePresenter
    {
        [SerializeField, Tooltip("Which player this 真人 belongs to. The one who sees it.")]
        PlayerRole m_VisibleTo = PlayerRole.Player1;

        [SerializeField, Tooltip("What to hide. Normally the whole station -- figure, lamp, beam " +
                                 "and plate. Left empty, this object is used.")]
        GameObject m_Station;

        // What was last reported, so flipping the debug role view says something and a scene
        // sitting still does not fill the console.
        int m_Reported = -1;

        protected override void ApplyForRole(PlayerRole role, bool roleKnown)
        {
            GameObject target = m_Station != null ? m_Station : gameObject;

            // Never this object: deactivating the thing the component is on would stop it from
            // ever being told the role changed.
            if (target == gameObject)
            {
                Debug.LogError($"[FigureRoleView] '{name}' has itself as its station; it would " +
                               "deactivate the component that drives it. Point it at the station " +
                               "root instead.", this);
                return;
            }

            bool visible = !roleKnown || role == m_VisibleTo;
            if (target.activeSelf != visible) target.SetActive(visible);

            int state = roleKnown ? (int)role : -2;
            if (m_Reported != state)
            {
                m_Reported = state;
                Debug.Log($"[FigureRoleView] {target.name}: " +
                          (roleKnown
                              ? $"local role is {role}, belongs to {m_VisibleTo} -> {(visible ? "shown" : "hidden")}"
                              : "role not known yet, showing both"));
            }
        }
    }
}
