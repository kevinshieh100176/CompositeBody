using Unity.Netcode;
using UnityEngine;
using XRMultiplayer;

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
    public class AvatarFormationSync : NetworkBehaviour
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

        float m_SpawnedAt;

        /// <summary>True once this player has touched, on every machine.</summary>
        public bool formed => m_FormedAt.Value >= 0f;

        /// <summary>Shared-clock time of the touch, or <see cref="NotFormed"/>.</summary>
        public float formedAt => m_FormedAt.Value;

        public override void OnNetworkSpawn()
        {
            m_SpawnedAt = ExperienceClock.now;
        }

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

        void Update()
        {
            if (m_Timeout <= 0f || !IsSpawned || !IsOwner || formed) return;
            if (ExperienceClock.now - m_SpawnedAt < m_Timeout) return;

            Debug.LogWarning($"[FormationSync] no touch after {m_Timeout}s; forming anyway so " +
                             "the other player is not left waiting.", this);
            MarkFormed();
        }

        /// <summary>
        /// The sync for a given player, or null. On the player root rather than on the avatar's
        /// body, so it is found the same way from either side of the connection.
        /// </summary>
        public static AvatarFormationSync For(XRINetworkPlayer player)
        {
            return player != null ? player.GetComponent<AvatarFormationSync>() : null;
        }
    }
}
