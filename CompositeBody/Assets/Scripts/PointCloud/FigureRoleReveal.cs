using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.PointClouds
{
    /// <summary>
    /// A 真人 that only one of the two players ever sees, whose coming and going is authored on a
    /// <see cref="FigurePresenceTrack"/> seen by both.
    ///
    /// S1-4 asks for exactly that: 「玩家a繼續看到a5 但玩家b看到 figure_b5」. The cue itself is not
    /// asymmetric -- both figures stand in the same place, gather at the same second and leave at
    /// the same second, and the whole of the difference is WHICH BODY each headset draws. So the
    /// presence curve stays one authored shape per figure and the role decides whether it is
    /// rendered at all, which is also what keeps the two cues re-timeable as one thing: moving
    /// the clip moves both players' figure.
    ///
    /// WHY NOT <see cref="FigureRoleDissolveTrack"/>, which is how S3-4 shows each player the
    /// other one's 真人. That track IS the asymmetry -- two columns of cues on one clip -- and it
    /// is the right tool when the two players' schedules differ. Here they do not. It would also
    /// cost S1-4 the thing the presence track exists for: a role track is plain clips with no
    /// mixer, so nothing answers for the figure before its clip, and both bodies would have to
    /// ship with an authored Dissolve of 1 plus an activation band to stay out of the room for
    /// the eleven minutes before the cue. That is precisely the pair of figures somebody is
    /// still placing by eye, and an invisible one is not placeable.
    ///
    /// WHY REVEAL AND NOT A DISABLED RENDERER. <see cref="RoleVisibility"/> is the general answer
    /// to "only Player1 sees this", and on a point cloud it loses: <see cref="PointCloudFigure"/>
    /// writes <c>renderer.enabled</c> itself every frame from its own reveal and dissolve, so a
    /// role gate that switched the renderer off would be overwritten on the next frame -- and so
    /// would one that forced the dissolve to 1, because the presence track writes that same float
    /// every frame from the authored curve. _Reveal is the one channel nothing else on these
    /// figures touches, and <see cref="PointCloudFigure"/> already folds it into the same
    /// renderer-off test, so this gate costs no draw call and cannot be fought: whatever the
    /// presence track does with the dissolve, reveal 0 keeps the cloud switched off. The same
    /// call <see cref="CompositeBody.Experience.RoleBlobPresenter"/> makes for its presence light.
    ///
    /// Local presentation only, like every <see cref="LocalRolePresenter"/>: both clients hold
    /// both figures and each hides the one that is not theirs, so the asymmetry costs no network
    /// traffic, cannot get out of step, and answers to
    /// <see cref="LocalRolePresenter.debugRole"/> -- one headset can be told to render as the
    /// other player and the pair swaps on the spot.
    ///
    /// Until the role is known -- in the editor with no session, or in the moments before the
    /// roles replicate -- BOTH are shown, the same call
    /// <see cref="CompositeBody.Experience.O0FigureRoleView"/> and
    /// <see cref="FigureRoleDissolveBehaviour"/> make. Showing nothing would make a scene that is
    /// merely not connected look like a scene that is broken.
    /// </summary>
    public class FigureRoleReveal : LocalRolePresenter
    {
        [SerializeField, Tooltip("The player who sees this 真人. Player1 is 玩家A (湖水綠), " +
                                 "Player2 is 玩家B. Every other machine draws nothing here.")]
        PlayerRole m_VisibleTo = PlayerRole.Player1;

        [SerializeField, Tooltip("The cloud to gate. Found on this object if left empty.")]
        PointCloudFigure m_Figure;

        /// <summary>
        /// The reveal this figure was authored with, put back when it is this machine's to draw.
        ///
        /// Read once rather than assumed to be 1: a figure whose arrival is authored as a partial
        /// reveal is still that figure for the player it belongs to, and a gate that handed it
        /// back at 1 would quietly be the only thing in the show setting its look.
        /// </summary>
        float m_Authored = 1f;

        // What was last reported, so flipping the debug role says something and a scene sitting
        // still does not fill the console.
        int m_Reported = -1;

        void Awake()
        {
            if (m_Figure == null) m_Figure = GetComponent<PointCloudFigure>();
            if (m_Figure != null) m_Authored = m_Figure.reveal;
        }

        protected override void ApplyForRole(PlayerRole role, bool roleKnown)
        {
            if (m_Figure == null)
            {
                // Once, not every role change: a missing binding is a setup error, and this runs
                // again every time the roles list moves.
                if (m_Reported != -3)
                {
                    m_Reported = -3;
                    Debug.LogError($"[FigureRoleReveal] '{name}' has no PointCloudFigure to gate, " +
                                   "so both players will see whatever is here.", this);
                }

                return;
            }

            bool mine = !roleKnown || role == m_VisibleTo;
            m_Figure.reveal = mine ? m_Authored : 0f;

            int state = roleKnown ? (int)role : -2;
            if (m_Reported == state) return;

            m_Reported = state;
            Debug.Log($"[FigureRoleReveal] {m_Figure.name}: " +
                      (roleKnown
                          ? $"local role is {role}, belongs to {m_VisibleTo} -> " +
                            $"{(mine ? "drawn" : "not drawn")}"
                          : "role not known yet, showing both"));
        }
    }
}
