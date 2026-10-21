using Unity.Netcode;
using UnityEngine;
using XRMultiplayer;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Tells the other machine that this player has touched a 真人, so their body can grow there
    /// too. Sits on the player root, beside <see cref="XRINetworkPlayer"/>.
    ///
    /// WHY IT SENDS A TIME AND NOT A YES. The body forms over half a minute. A bare flag would
    /// start that ramp whenever the packet happened to land, so a dropped tick, a late join or a
    /// slow link would leave the two machines disagreeing about how grown the body is -- and the
    /// disagreement would persist for the whole thirty-two seconds rather than correcting. The
    /// moment of the touch is a fact both machines can hold, and <see cref="AvatarFormation"/>
    /// already stamps its start against <see cref="ExperienceClock"/>, which is shared. So the
    /// owner sends that stamp, everyone derives progress from it, and a late packet changes when
    /// the body starts appearing here -- not how far along it is.
    ///
    /// OWNER-WRITTEN, because the player who touched is the only one who knows they did. The
    /// role a player is SHOWN in is still looked up server-side through GameSessionManager, so
    /// nothing a client sends decides how it is coloured.
    ///
    /// THE TIMEOUT IS A REQUIREMENT, not a nicety. The production timeline states it as a rule
    /// for every gated beat: 不可讓一位玩家卡住另一位. If one player never touches anything, their
    /// partner must not be left looking at a light where a person should be for the rest of the
    /// show.
    /// </summary>
    [DisallowMultipleComponent]
    public class AvatarFormationSync : NetworkBehaviour, IDebugInteraction
    {
        /// <summary>Sentinel for "has not touched yet". Shared clock time is never negative.</summary>
        public const float NotFormed = -1f;

        [SerializeField, Min(0f), Tooltip("Seconds after spawning before this player is treated " +
                                          "as formed whether they touched anything or not. 0 " +
                                          "disables it. The timeline requires a fallback on " +
                                          "every gate so one player cannot strand the other.")]
        float m_Timeout = 180f;

        readonly NetworkVariable<float> m_FormedAt = new(
            NotFormed,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        float m_StartedAt = -1f;

        /// <summary>True once this player has touched, on every machine.</summary>
        public bool formed => m_FormedAt.Value >= 0f;

        /// <summary>Shared-clock time of the touch, or <see cref="NotFormed"/>.</summary>
        public float formedAt => m_FormedAt.Value;

        public override void OnNetworkSpawn()
        {
            m_StartedAt = -1f;
            InteractionDebug.Register(this);
        }

        public override void OnNetworkDespawn() => InteractionDebug.Unregister(this);

        /// <summary>
        /// Record the touch. Only the owner may, and only once -- a second call would restart
        /// everyone else's ramp from the beginning.
        /// </summary>
        public void MarkFormed()
        {
            if (!IsSpawned || !IsOwner || formed) return;
            m_FormedAt.Value = ExperienceClock.now;
            Debug.Log($"[FormationSync] touch sent at {m_FormedAt.Value:0.00}s.", this);
        }

        /// <summary>
        /// Seconds before the fallback forms this player anyway, or NEGATIVE when no clock is
        /// running -- already formed, no timeout configured, or the show not started.
        ///
        /// Readable on every machine, not only the owner's, which is why the stamp below is kept
        /// everywhere rather than under the IsOwner test where it started. The two machines agree
        /// to within a frame without anything being replicated: the show starting is one
        /// replicated event and <see cref="ExperienceClock"/> is shared, so each machine stamps
        /// the same moment. A countdown only the owner could see would be invisible on the one
        /// screen anybody is watching it from.
        /// </summary>
        public float secondsUntilTimeout
        {
            get
            {
                if (m_Timeout <= 0f || formed || m_StartedAt < 0f) return -1f;
                return Mathf.Max(m_Timeout - (ExperienceClock.now - m_StartedAt), 0f);
            }
        }

        void Update()
        {
            if (m_Timeout <= 0f || !IsSpawned || formed) return;

            // COUNTED FROM THE START OF THE SHOW, NOT FROM SPAWN. Measured from spawn it fired
            // while a machine sat in the lobby -- so a host left idle formed itself, and the
            // player who joined later was met by a fully grown membrane standing at the origin
            // before anyone had touched anything. The fallback is meant to protect a player
            // whose partner will not touch during the beat, which cannot begin before the beat
            // does.
            if (!ExperienceStarted()) { m_StartedAt = -1f; return; }

            if (m_StartedAt < 0f) m_StartedAt = ExperienceClock.now;

            // THE STAMP IS KEPT ON EVERY MACHINE, THE FORMING IS STILL THE OWNER'S. Only the
            // owner may write the NetworkVariable, so only the owner may act on the deadline;
            // everyone else tracks it purely so the debug panel can show the same countdown the
            // owner is running.
            if (!IsOwner) return;
            if (ExperienceClock.now - m_StartedAt < m_Timeout) return;

            Debug.LogWarning($"[FormationSync] no touch {m_Timeout}s after the show started; " +
                             "forming anyway so the other player is not left waiting.", this);
            MarkFormed();
        }

        /// <summary>
        /// With no session manager at all -- a desk test -- the show is treated as running, so a
        /// single machine still reaches the fallback rather than waiting forever.
        /// </summary>
        static bool ExperienceStarted()
        {
            GameSessionManager session = GameSessionManager.Instance;
            return session == null || session.sessionState != SessionState.Lobby;
        }

        /// <summary>
        /// The sync for a given player, or null. On the player root rather than on the avatar's
        /// body, so it is found the same way from either side of the connection.
        /// </summary>
        public static AvatarFormationSync For(XRINetworkPlayer player)
        {
            return player != null ? player.GetComponent<AvatarFormationSync>() : null;
        }

        #region Debug panel

        // ONE ROW PER PLAYER, KEYED ON THE OWNER'S CLIENT ID. Every machine holds a sync for
        // every player, so the id has to name the player rather than the component -- otherwise
        // two rows on one screen would both claim to be "the formation" and a fire aimed at one
        // machine could land on the other. The client id is the same number on both machines,
        // which is what lets a button pressed here resolve to the right row over there.
        public string debugId => $"player.formation.{(IsSpawned ? OwnerClientId : 0)}";

        public string debugLabel
        {
            get
            {
                PlayerRole role = IsSpawned
                    ? InteractionDebugRelay.RoleOf(OwnerClientId)
                    : PlayerRole.Unassigned;
                string who = role != PlayerRole.Unassigned ? role.ToString() : $"client {(IsSpawned ? OwnerClientId : 0)}";
                // ASCII ONLY IN A UI STRING: the panel draws with LegacyRuntime.ttf, which has no CJK
                // glyphs, so "真人" would be two empty boxes on the one screen that has to be
                // readable at a glance.
                return $"Touched the scan figure -- body forming ({who}){(IsOwner ? ", this machine" : "")}";
            }
        }

        public DebugInteractionScope debugScope => DebugInteractionScope.Player;

        public string debugFireLabel => "Form";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;
            state.fired = formed;
            state.secondsToTimeout = secondsUntilTimeout;

            PlayerRole role = IsSpawned ? InteractionDebugRelay.RoleOf(OwnerClientId) : PlayerRole.Unassigned;
            state.player1 = formed && role == PlayerRole.Player1;
            state.player2 = formed && role == PlayerRole.Player2;

            if (formed) state.detail = $"touched at {formedAt:0.0}s on the shared clock";
            else if (state.secondsToTimeout >= 0f) state.detail = "waiting for a touch; fallback armed";
            else if (m_Timeout <= 0f) state.detail = "waiting for a touch; no fallback configured";
            else state.detail = "waiting for a touch; fallback starts when the show does";

            return state;
        }

        public bool DebugCanFire(DebugFireTarget target)
        {
            if (!IsSpawned || formed) return false;

            // Local is this machine's own player and goes through Form(), the real path. The
            // role targets mean "make that player's machine do it", which only the server may
            // ask for -- and only of the machine the row actually belongs to.
            if (target == DebugFireTarget.Local) return IsOwner;
            if (target == DebugFireTarget.Both) return false;

            PlayerRole role = InteractionDebugRelay.RoleOf(OwnerClientId);
            return InteractionDebugRelay.canForce &&
                   ((target == DebugFireTarget.Player1 && role == PlayerRole.Player1) ||
                    (target == DebugFireTarget.Player2 && role == PlayerRole.Player2));
        }

        public void DebugFire(DebugFireTarget target)
        {
            if (!DebugCanFire(target)) return;

            if (target == DebugFireTarget.Local)
            {
                // Form() rather than MarkFormed(): the touch has to grow the body on this
                // machine as well as tell the other one, and Form is what the real touch calls.
                if (AvatarFormation.local != null) AvatarFormation.local.Form();
                else MarkFormed();
                return;
            }

            InteractionDebugRelay.Instance?.FireOnClient(OwnerClientId, debugId);
        }

        // NO RESET. The flag is written once by design -- a second write would restart the other
        // machine's thirty-second ramp from the beginning -- and clearing it would not put a
        // grown body away, because nothing in AvatarFormation listens for a formed player
        // becoming unformed. A reset button here would look like it worked and would not.
        public bool DebugCanReset() => false;

        public void DebugReset() { }

        #endregion
    }
}
