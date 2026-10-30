using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using XRMultiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Hosts the show and reports what happened, from the command line, so a built player can be
    /// tested without a headset and without anyone pressing a button.
    ///
    /// WHY THIS EXISTS. Hosting threw a NullReferenceException in builds and only in builds --
    /// Netcode's own Awake was being shadowed, and the editor hid it because OnValidate repairs
    /// the same field. Play-mode tests could not see it by construction. The only thing that can
    /// is the player itself, run and asked whether it worked.
    ///
    /// Does nothing unless -autohost is on the command line, so it is inert in the build that
    /// goes in front of an audience. It exits with a non-zero code on failure, which is what
    /// makes it usable from a script.
    ///
    ///   CompositeBody.exe -autohost -batchmode -nographics -logFile &lt;log&gt;
    /// </summary>
    public static class BuildSmokeTest
    {
        const string k_Flag = "-autohost";
        const float k_Timeout = 40f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (!System.Environment.GetCommandLineArgs().Contains(k_Flag)) return;

            var go = new GameObject("~BuildSmokeTest");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        class Runner : MonoBehaviour
        {
            IEnumerator Start()
            {
                Debug.Log("[Smoke] started; waiting for the session managers.");

                float deadline = Time.realtimeSinceStartup + k_Timeout;
                while (XRINetworkGameManager.Instance == null || NetworkManager.Singleton == null)
                {
                    if (Time.realtimeSinceStartup > deadline) yield break ; // reported below
                    yield return null;
                }

                NetworkManager manager = NetworkManager.Singleton;
                Debug.Log($"[Smoke] managers up. transport=" +
                          $"{(manager.NetworkConfig?.NetworkTransport != null ? manager.NetworkConfig.NetworkTransport.GetType().Name : "NULL")}, " +
                          $"playerPrefab=" +
                          $"{(manager.NetworkConfig?.PlayerPrefab != null ? manager.NetworkConfig.PlayerPrefab.name : "NULL")}, " +
                          $"approval={manager.NetworkConfig?.ConnectionApproval}");

                // The same call the Host button makes, so a pass here means the button works.
                bool started;
                try
                {
                    started = XRINetworkGameManager.Instance.HostLocalConnection();
                }
                catch (System.Exception e)
                {
                    Fail($"HostLocalConnection threw: {e.GetType().Name}: {e.Message}");
                    yield break;
                }

                if (!started) { Fail("HostLocalConnection returned false."); yield break; }
                Debug.Log("[Smoke] host started.");

                while (Time.realtimeSinceStartup < deadline)
                {
                    if (manager.IsListening && manager.LocalClient is { PlayerObject: not null }) break;
                    yield return null;
                }

                if (!manager.IsListening) { Fail("the host never started listening."); yield break; }
                if (manager.LocalClient?.PlayerObject == null)
                {
                    Fail("hosted, but no player object was ever created.");
                    yield break;
                }

                GameObject avatar = manager.LocalClient.PlayerObject.gameObject;
                Debug.Log($"[Smoke] player spawned: '{avatar.name}'.");

                // The component the whole onboarding hangs off. Found by name because this file
                // must not depend on the beat's assembly layout.
                bool hasFormation = avatar.GetComponentsInChildren<Component>(true)
                    .Any(c => c != null && c.GetType().Name == "AvatarFormation");

                if (!hasFormation) { Fail("the avatar carries no AvatarFormation."); yield break; }

                Debug.Log("[Smoke] AvatarFormation present.");
                Debug.Log("[Smoke] RESULT: PASS");

                yield return new WaitForSecondsRealtime(1f);
                Application.Quit(0);
            }

            static void Fail(string why)
            {
                Debug.LogError($"[Smoke] RESULT: FAIL - {why}");
                Application.Quit(1);
            }
        }
    }
}
