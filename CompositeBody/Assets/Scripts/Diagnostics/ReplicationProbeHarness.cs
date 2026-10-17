using System.Collections;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using XRMultiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Runs one process as a host and another as a client, so client-to-server pose replication
    /// can be reproduced on a single machine.
    ///
    /// WHY. Every test so far had the host own the only player, which means the head moved
    /// because LateUpdate ran locally -- replication was never exercised at all. The bug being
    /// chased is specifically a CLIENT owner's pose failing to reach the server, and no
    /// single-process test can see it. Two processes can, and then the question stops being
    /// "what might be wrong" and becomes "which of these two logs disagrees".
    ///
    /// The client walks its camera in a circle, because a head that never moves proves nothing:
    /// an avatar frozen at the origin and an avatar correctly reporting a stationary origin look
    /// identical.
    ///
    ///   CompositeBody.exe -probehost   -posedebug -batchmode -nographics -logFile host.log
    ///   CompositeBody.exe -probeclient -posedebug -batchmode -nographics -logFile client.log
    ///
    /// Inert without those flags.
    /// </summary>
    public static class ReplicationProbeHarness
    {
        const string k_Host = "-probehost";
        const string k_Client = "-probeclient";
        const float k_Lifetime = 25f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            bool host = args.Contains(k_Host);
            bool client = args.Contains(k_Client);
            if (!host && !client) return;

            var go = new GameObject("~ReplicationProbeHarness");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().isHost = host;
        }

        class Runner : MonoBehaviour
        {
            public bool isHost;

            IEnumerator Start()
            {
                string role = isHost ? "HOST" : "CLIENT";
                Debug.Log($"[Harness] {role} starting.");

                float deadline = Time.realtimeSinceStartup + 40f;
                while (XRINetworkGameManager.Instance == null || NetworkManager.Singleton == null)
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Debug.LogError($"[Harness] {role} RESULT: FAIL - managers never came up.");
                        Application.Quit(1);
                        yield break;
                    }
                    yield return null;
                }

                if (isHost)
                {
                    if (!XRINetworkGameManager.Instance.HostLocalConnection())
                    {
                        Debug.LogError("[Harness] HOST RESULT: FAIL - HostLocalConnection false.");
                        Application.Quit(1);
                        yield break;
                    }
                    Debug.Log("[Harness] HOST listening.");
                }
                else
                {
                    // Loopback, and whatever port the transport is already configured with, so
                    // this follows the project's own setting rather than inventing one.
                    var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport
                        as UnityTransport;
                    if (transport == null)
                    {
                        Debug.LogError("[Harness] CLIENT RESULT: FAIL - no UnityTransport.");
                        Application.Quit(1);
                        yield break;
                    }

                    ushort port = transport.ConnectionData.Port;
                    transport.SetConnectionData("127.0.0.1", port);
                    Debug.Log($"[Harness] CLIENT joining 127.0.0.1:{port}");

                    if (!XRINetworkGameManager.Instance.JoinLocalConnection())
                    {
                        Debug.LogError("[Harness] CLIENT RESULT: FAIL - JoinLocalConnection false.");
                        Application.Quit(1);
                        yield break;
                    }

                    StartCoroutine(WalkTheCamera());
                }

                yield return new WaitForSecondsRealtime(k_Lifetime);
                Debug.Log($"[Harness] {role} RESULT: PASS (ran {k_Lifetime}s)");
                Application.Quit(0);
            }

            /// <summary>
            /// Moves the rig camera in a circle, standing in for a person wearing the headset.
            /// XRINetworkPlayer copies this onto `head` every LateUpdate, and `head` is what
            /// replicates -- so this is the input the whole chain is supposed to carry.
            /// </summary>
            static IEnumerator WalkTheCamera()
            {
                Camera rig = Camera.main != null
                    ? Camera.main
                    : Object.FindFirstObjectByType<Camera>();

                if (rig == null)
                {
                    Debug.LogError("[Harness] CLIENT has no camera to move.");
                    yield break;
                }

                // The hands come from the modality manager's controller transforms, not from the
                // camera, so walking the camera alone leaves them static -- which looks exactly
                // like hands that fail to replicate. Both have to move for this to prove
                // anything.
                var modality = Object.FindFirstObjectByType<
                    UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager>();

                Transform left = modality != null && modality.leftController != null
                    ? modality.leftController.transform : null;
                Transform right = modality != null && modality.rightController != null
                    ? modality.rightController.transform : null;

                Debug.Log($"[Harness] CLIENT walking camera '{rig.name}'" +
                          $", leftController={(left != null ? left.name : "NONE")}" +
                          $", rightController={(right != null ? right.name : "NONE")}");

                float t = 0f;
                while (true)
                {
                    t += Time.unscaledDeltaTime;
                    var centre = new Vector3(Mathf.Sin(t) * 2f, 1.6f, Mathf.Cos(t) * 2f);
                    rig.transform.position = centre;

                    // Offset to either side and bobbing, so a hand that merely copies the head
                    // is distinguishable from one carrying its own pose.
                    if (left != null)
                        left.position = centre + new Vector3(-0.25f, Mathf.Sin(t * 3f) * 0.2f, 0f);
                    if (right != null)
                        right.position = centre + new Vector3(0.25f, Mathf.Cos(t * 3f) * 0.2f, 0f);

                    yield return null;
                }
            }
        }
    }
}
