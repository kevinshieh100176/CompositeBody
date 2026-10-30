using Unity.XR.CoreUtils;
using UnityEngine;
using CompositeBody.Experience;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Notices when the player is moved a long way in one frame, and says what did it.
    ///
    /// THE QUESTION IT ANSWERS is which of two things happened, because they look identical from
    /// inside the headset and have opposite fixes:
    ///
    ///   The PLAYSPACE moved and carried the rig. The rig is parented to it so that the show can
    ///   take the players somewhere, so this is the mechanism working -- and if the destination
    ///   is wrong, the thing to correct is whatever animates the playspace.
    ///
    ///   The RIG ITSELF was written. Something called SetPositionAndRotation or added to
    ///   .position on the XR Origin -- CalibrationPoint and RoleSpawnPositioner both do, and so
    ///   does any locomotion provider. Then the playspace is innocent.
    ///
    /// Telling them apart takes one number: the rig's LOCAL position. If the playspace moved, the
    /// local position did not change. If something wrote the rig, it did.
    ///
    /// Reports the show time as well, so a jump can be read against the timeline and matched to
    /// whatever clip is running then.
    /// </summary>
    public class TeleportWatch : MonoBehaviour
    {
        [SerializeField, Min(0.05f), Tooltip("Metres in one frame that count as a jump rather " +
                                             "than someone walking.")]
        float m_Threshold = 0.5f;

        XROrigin m_Rig;
        Transform m_Avatar;
        Vector3 m_LastWorld, m_LastLocal, m_LastPlayspace;
        Vector3 m_LastAvatarWorld, m_LastAvatarLocal;
        bool m_Primed;

        /// <summary>
        /// The local player's avatar root, which is a different object from the rig and can move
        /// independently of it. That is the whole reason this watches both: the rig is what the
        /// player sees through, the avatar is what the other player sees, and when they part
        /// company the headset shows nothing wrong at all.
        /// </summary>
        Transform FindAvatar()
        {
            foreach (var p in FindObjectsByType<XRMultiplayer.XRINetworkPlayer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (p.IsOwner) return p.transform;
            }
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            var go = new GameObject("~TeleportWatch");
            DontDestroyOnLoad(go);
            go.AddComponent<TeleportWatch>();
        }

        void LateUpdate()
        {
            if (m_Rig == null)
            {
                m_Rig = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
                if (m_Rig == null) return;
                m_Primed = false;
            }

            Transform rig = m_Rig.transform;
            Transform playspace = Playspace.Instance != null ? Playspace.Instance.transform : null;

            Vector3 world = rig.position;
            Vector3 local = rig.localPosition;
            Vector3 space = playspace != null ? playspace.position : Vector3.zero;

            if (!m_Primed)
            {
                m_Primed = true;
                m_LastWorld = world; m_LastLocal = local; m_LastPlayspace = space;
                return;
            }

            float movedWorld = Vector3.Distance(world, m_LastWorld);
            if (movedWorld >= m_Threshold)
            {
                float movedLocal = Vector3.Distance(local, m_LastLocal);
                float movedSpace = Vector3.Distance(space, m_LastPlayspace);

                // Which one it was, stated rather than left to be worked out from three vectors.
                string blame = movedLocal < m_Threshold * 0.5f
                    ? "THE PLAYSPACE MOVED and carried the rig. The rig's own local position " +
                      "barely changed, so nothing wrote to the rig -- look at whatever animates " +
                      "the playspace at this show time."
                    : "SOMETHING WROTE THE RIG. Its local position changed by about as much as " +
                      "its world position, so this is not the playspace carrying it -- look for " +
                      "a SetPositionAndRotation or a .position += on the XR Origin.";

                float show = ShowTimelineDriver.Instance != null
                    ? ShowTimelineDriver.Instance.showTime
                    : -1f;

                Debug.LogWarning(
                    $"[TeleportWatch] the player moved {movedWorld:0.00} m in one frame at show " +
                    $"{(show >= 0f ? $"{(int)show / 60:00}:{(int)show % 60:00} ({show:0.0}s)" : "time unknown")}." +
                    $"\n[TeleportWatch]   rig world  {m_LastWorld:F2} -> {world:F2}   ({movedWorld:0.00} m)" +
                    $"\n[TeleportWatch]   rig local  {m_LastLocal:F2} -> {local:F2}   ({movedLocal:0.00} m)" +
                    $"\n[TeleportWatch]   playspace  {m_LastPlayspace:F2} -> {space:F2}   ({movedSpace:0.00} m)" +
                    $"\n[TeleportWatch]   {blame}");
            }

            m_LastWorld = world; m_LastLocal = local; m_LastPlayspace = space;

            WatchAvatar(playspace);
        }

        void WatchAvatar(Transform playspace)
        {
            if (m_Avatar == null) { m_Avatar = FindAvatar(); if (m_Avatar == null) return; }

            Vector3 world = m_Avatar.position;
            Vector3 local = m_Avatar.localPosition;

            if (m_LastAvatarWorld == Vector3.zero && m_LastAvatarLocal == Vector3.zero)
            {
                m_LastAvatarWorld = world; m_LastAvatarLocal = local;
                return;
            }

            float moved = Vector3.Distance(world, m_LastAvatarWorld);
            if (moved >= m_Threshold)
            {
                string parent = m_Avatar.parent == null ? "(scene root)" : m_Avatar.parent.name;
                bool onPlayspace = playspace != null && m_Avatar.parent == playspace;

                float show = ShowTimelineDriver.Instance != null
                    ? ShowTimelineDriver.Instance.showTime : -1f;

                Debug.LogWarning(
                    $"[TeleportWatch] the AVATAR moved {moved:0.00} m in one frame at show " +
                    $"{(show >= 0f ? $"{(int)show / 60:00}:{(int)show % 60:00} ({show:0.0}s)" : "time unknown")}." +
                    $"\n[TeleportWatch]   avatar world {m_LastAvatarWorld:F2} -> {world:F2}" +
                    $"\n[TeleportWatch]   avatar local {m_LastAvatarLocal:F2} -> {local:F2}" +
                    $"\n[TeleportWatch]   parent '{parent}'" +
                    (onPlayspace
                        ? ", which IS the playspace, so PlayspaceRider did its job and something " +
                          "else moved the avatar inside it."
                        : ", which is NOT the playspace. PlayspaceRider never parented this " +
                          "avatar, so its head and hands are replicating local coordinates " +
                          "against the wrong frame -- that alone will put it somewhere else " +
                          "entirely."), this);
            }

            m_LastAvatarWorld = world; m_LastAvatarLocal = local;
        }
    }
}
