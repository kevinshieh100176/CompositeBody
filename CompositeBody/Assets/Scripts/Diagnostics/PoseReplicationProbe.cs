using System.Text;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using XRMultiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Reports, once a second on every machine, what each player's head is doing and who is
    /// allowed to move it.
    ///
    /// WHY THIS EXISTS. An avatar standing at the world origin on the other machine has at least
    /// four possible causes that look identical from the outside: the owner never found its XR
    /// rig, so nothing is being sent; the owner is sending but is not allowed to, so the server
    /// discards it; the pose arrives and something local overwrites it; or the pose arrives and
    /// only the visible body fails to follow. Reading the project cannot separate those, and
    /// neither can a single-process test -- when the host owns the only player, replication is
    /// never exercised at all.
    ///
    /// So this prints the four facts that do separate them, from both ends at once:
    ///   owner      -- is this machine allowed to move this avatar
    ///   commit     -- does its NetworkTransform believe it may send
    ///   headLocal  -- where the head is in the avatar's own space
    ///   headWorld  -- where it ends up
    ///
    /// Inert unless -posedebug is on the command line, so it costs nothing in the venue.
    ///
    ///   CompositeBody.exe -posedebug
    /// </summary>
    public class PoseReplicationProbe : MonoBehaviour
    {
        const string k_Flag = "-posedebug";
        const float k_Interval = 1f;

        float m_Next;
        readonly StringBuilder m_Line = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            bool wanted = false;
            foreach (string arg in System.Environment.GetCommandLineArgs())
            {
                if (arg == k_Flag) { wanted = true; break; }
            }

            if (!wanted) return;

            var go = new GameObject("~PoseReplicationProbe");
            go.AddComponent<PoseReplicationProbe>();
            DontDestroyOnLoad(go);
            Debug.Log("[PoseProbe] installed; reporting every second.");
        }

        void Update()
        {
            if (Time.unscaledTime < m_Next) return;
            m_Next = Time.unscaledTime + k_Interval;

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                Debug.Log("[PoseProbe] no session.");
                return;
            }

            m_Line.Clear();
            m_Line.Append($"[PoseProbe] thisMachine={(nm.IsServer ? "SERVER" : "CLIENT")} " +
                          $"localClientId={nm.LocalClientId}");

            XRINetworkPlayer[] players =
                Object.FindObjectsByType<XRINetworkPlayer>(FindObjectsInactive.Include,
                                                           FindObjectsSortMode.None);

            if (players.Length == 0) m_Line.Append("\n  (no players found)");

            foreach (XRINetworkPlayer player in players)
            {
                NetworkObject netObject = player.GetComponent<NetworkObject>();
                Transform head = player.head;

                m_Line.Append($"\n  player owner={(netObject != null ? netObject.OwnerClientId : 0)}" +
                              $" isOwner={(netObject != null && netObject.IsOwner)}" +
                              $" spawned={(netObject != null && netObject.IsSpawned)}");

                Report("head", head);
                Report("leftHand", player.leftHand);
                Report("rightHand", player.rightHand);
            }

            Debug.Log(m_Line.ToString());
        }

        /// <summary>
        /// One limb. All three are reported, because the head and the hands get their poses from
        /// different components and have failed independently -- reporting only the head is how
        /// the hands went unnoticed once the head was fixed.
        /// </summary>
        void Report(string label, Transform limb)
        {
            if (limb == null)
            {
                m_Line.Append($"\n      {label,-10} <UNASSIGNED>");
                return;
            }

            m_Line.Append($"\n      {label,-10} world={Fmt(limb.position)}" +
                          $" active={limb.gameObject.activeInHierarchy}");

            // Whether this machine's NetworkTransform thinks it may send. On the owner this must
            // be true or nothing is transmitted; on the other machine it must be false, or both
            // ends are writing and whichever ran last wins.
            var netTransform = limb.GetComponent<NetworkTransform>();
            if (netTransform == null)
            {
                m_Line.Append("  <NO NetworkTransform>");
                return;
            }

            m_Line.Append($" commit={netTransform.CanCommitToTransform}" +
                          $" serverAuth={netTransform.IsServerAuthoritative()}");
        }

        static string Fmt(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";
    }
}
