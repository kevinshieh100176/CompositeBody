using UnityEngine;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Silences this AudioSource on any client whose local role isn't in
    /// <see cref="m_AudibleToRoles"/>. The audio half of <see cref="RoleVisibility"/>: "only
    /// Player1 hears track A".
    ///
    /// WHY THIS IS ALL IT TAKES. Timeline has no concept of a player, but an AudioTrack binds to
    /// an AudioSource in the *scene*, and each machine runs its own copy of the scene with its own
    /// PlayableDirector. So two tracks bound to two sources are already two independent decks on
    /// each headset; which of them is heard is a purely local decision, and that is exactly what
    /// every <see cref="LocalRolePresenter"/> in the project is for. No network traffic, nothing
    /// to keep in sync, and the show asset stays identical on both machines.
    ///
    /// IT MUTES RATHER THAN STOPS. Timeline schedules its audio clips against the DSP clock when
    /// the graph is built -- see the note in ShowTimelineDriver about a director that is evaluated
    /// but never played. Stopping the source, or disabling it, takes the clip off that schedule,
    /// and re-enabling it will not put it back where the show has got to; the track would come
    /// back in the wrong place or not at all. Muting leaves both decks running in lockstep and can
    /// be flipped on any frame, which matters twice here: the local role often resolves after the
    /// show has already started, and the debug override has to be able to change what you hear
    /// mid-clip or it is lying about what the other player gets.
    ///
    /// Do NOT reach for the track's own volume in the timeline asset instead. That is asset-side,
    /// shared by both machines, and would silence the track for whoever is meant to hear it.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class RoleAudible : LocalRolePresenter
    {
        [SerializeField, Tooltip("Roles that hear this source. One role is the point of the " +
                                 "component; both is the same as not having it.")]
        PlayerRole[] m_AudibleToRoles = { PlayerRole.Player1 };

        [SerializeField, Tooltip("Log every time this source goes silent or comes back. On by " +
                                 "default: 'player B heard nothing' is otherwise invisible in " +
                                 "the venue, where there is no second pair of ears to ask.")]
        bool m_LogChanges = true;

        AudioSource m_Source;
        bool m_Applied;

        void Awake() => m_Source = GetComponent<AudioSource>();

        protected override void ApplyForRole(PlayerRole role, bool roleKnown)
        {
            // Unknown role mutes. The alternative is hearing A and B at once, which is worse
            // than silence and reads as a bad mix rather than as a role that has not arrived.
            bool audible = roleKnown && System.Array.IndexOf(m_AudibleToRoles, role) >= 0;

            if (m_LogChanges && (!m_Applied || m_Source.mute == audible))
            {
                Debug.Log($"[RoleView] {name} {(audible ? "audible" : "muted")} " +
                          $"(local role {(roleKnown ? role.ToString() : "unresolved")}).");
            }

            m_Source.mute = !audible;
            m_Applied = true;
        }
    }
}
