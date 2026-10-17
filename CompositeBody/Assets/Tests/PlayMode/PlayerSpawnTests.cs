using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CompositeBody.Tests
{
    /// <summary>
    /// Does a player actually spawn when the show is hosted?
    ///
    /// Everything in the onboarding hangs off this one fact. The membrane, the Ch36 body, the
    /// touch, the 真人 dissolving and the objects taking their place all begin with an avatar
    /// existing and registering itself as the local one -- so when none of them happen, the
    /// useful question is not "which of the five is broken" but "did the player spawn at all".
    /// Reading the config cannot answer it: the config has looked correct more than once while
    /// nothing spawned. Only starting a host answers it.
    ///
    /// Runs in the editor's play mode, headless, with no headset. XR never initialises, which is
    /// fine: the spawn path is networking, not tracking.
    ///
    /// Unity.exe -batchmode -projectPath &lt;path&gt; -runTests -testPlatform PlayMode
    ///   -testResults &lt;results.xml&gt; -logFile &lt;log&gt;
    /// </summary>
    public class PlayerSpawnTests
    {
        const string k_Scene = "MainScene";
        const float k_Timeout = 20f;

        [SetUp]
        public void SetUp()
        {
            // The scene logs errors of its own -- XRI runs out of interactor registration slots,
            // among others -- and the test framework fails a test on any unexpected error. Those
            // are real and worth fixing, but they are not this test's question, and letting them
            // fail it means never finding out whether a player spawns.
            LogAssert.ignoreFailingMessages = true;
        }

        [UnityTest]
        public IEnumerator HostingTheShowSpawnsAPlayer()
        {
            // Set here as well as in SetUp: the scene logs its first error during load, which is
            // before SetUp's value has been seen in practice.
            LogAssert.ignoreFailingMessages = true;
            Debug.Log("[SpawnTest] loading " + k_Scene + " ...");

            yield return LoadMainScene();
            Debug.Log("[SpawnTest] scene loaded.");

            NetworkManager manager = NetworkManager.Singleton;
            Assert.IsNotNull(manager, "No NetworkManager in " + k_Scene + ".");

            // The three things that have to be true before a spawn is even possible, reported
            // separately so a failure names which one rather than just "no player".
            Assert.IsNotNull(manager.NetworkConfig, "NetworkConfig is null.");
            // NOTE ON WHICH FIELD THIS IS. The project subclasses NetworkManager as
            // NetworkManagerVRMultiplayer, which carries its own serialized `m_NetworkConfig`
            // and copies it over the base class's `NetworkConfig` in Awake. So the base field is
            // empty on disk BY DESIGN and only becomes real at runtime -- which is why this test
            // reads it after the scene has loaded rather than inspecting the prefab, and why a
            // static check of the prefab reports a missing player prefab that is not missing.
            Assert.IsNotNull(manager.NetworkConfig.NetworkTransport,
                             "No NetworkTransport at runtime -- the host cannot start. Check " +
                             "m_NetworkConfig on NetworkManagerVRMultiplayer, not NetworkConfig.");
            Assert.IsNotNull(manager.NetworkConfig.PlayerPrefab,
                             "No PlayerPrefab at runtime, so there is nothing to spawn. The live " +
                             "value comes from m_NetworkConfig on NetworkManagerVRMultiplayer; " +
                             "the base NetworkConfig block in the prefab is unused.");

            Debug.Log($"[SpawnTest] transport={manager.NetworkConfig.NetworkTransport.GetType().Name}, " +
                      $"playerPrefab={manager.NetworkConfig.PlayerPrefab.name}, " +
                      $"connectionApproval={manager.NetworkConfig.ConnectionApproval}");

            // Netcode's own initialisation, which only NetworkManager.Awake does. The editor
            // hides its absence: OnValidate creates the logger as a fallback and OnValidate does
            // not exist in a player, so a NetworkManager subclass that shadows Awake works in
            // play mode and throws on the first line of StartHost in a build. Asserted through a
            // public surface that Awake is responsible for, so it fails here rather than there.
            Assert.IsTrue(manager.NetworkConfig.Prefabs is { NetworkPrefabOverrideLinks: not null },
                          "NetworkConfig.InitializePrefabs() never ran, which means " +
                          "NetworkManager.Awake() never ran -- something is shadowing it. In the " +
                          "editor this still appears to work; in a build, hosting throws.");

            Assert.IsTrue(manager.StartHost(), "StartHost() returned false.");

            float deadline = Time.realtimeSinceStartup + k_Timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (manager.IsListening && manager.LocalClient is { PlayerObject: not null }) break;
                yield return null;
            }

            Assert.IsTrue(manager.IsListening, "The host never started listening.");
            Assert.IsNotNull(manager.LocalClient,
                             "No LocalClient after hosting -- the connection was never approved.");
            Assert.IsNotNull(manager.LocalClient.PlayerObject,
                             "The host connected but no player object was created. With " +
                             "ConnectionApproval on, that is the approval callback not setting " +
                             "CreatePlayerObject; with it off, it is NGO never calling the " +
                             "callback at all.");

            GameObject avatar = manager.LocalClient.PlayerObject.gameObject;
            Debug.Log($"[SpawnTest] PASS: spawned '{avatar.name}' at {avatar.transform.position}.");

            // Second level: the avatar is the one the beat expects, carrying the component the
            // touch calls Form() through. Found by name because a test assembly cannot reference
            // Assembly-CSharp, which is where that component lives.
            Component formation = null;
            foreach (Component c in avatar.GetComponentsInChildren<Component>(true))
            {
                if (c != null && c.GetType().Name == "AvatarFormation") { formation = c; break; }
            }
            Assert.IsNotNull(formation,
                             $"'{avatar.name}' spawned but carries no AvatarFormation, so nothing " +
                             "will form when a 真人 is touched.");

            Debug.Log("[SpawnTest] AvatarFormation present on the spawned avatar.");

            manager.Shutdown();
            yield return null;
        }

        static IEnumerator LoadMainScene()
        {
            if (SceneManager.GetActiveScene().name != k_Scene)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync(k_Scene, LoadSceneMode.Single);
                while (load is { isDone: false }) yield return null;
            }

            // A few frames so Awake/OnEnable/Start have all run before anything is asserted.
            for (int i = 0; i < 5; i++) yield return null;
        }
    }
}
