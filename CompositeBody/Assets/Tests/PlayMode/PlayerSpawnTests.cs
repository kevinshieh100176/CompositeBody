using System.Collections;
using System.Reflection;
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

        /// <summary>
        /// The show must NOT start on its own.
        ///
        /// It used to: ShowTimelineDriver.OnNetworkSpawn set Held to false and ResumedAt to now,
        /// so show time began advancing the instant the server spawned it and the whole running
        /// order played itself with nobody pressing anything.
        ///
        /// Only half of the behaviour can be asserted from here, and it is the half that broke:
        /// StartExperience requires both players, so a single host cannot legally start the show
        /// and this test cannot prove that it resumes afterwards. It proves it does not start by
        /// itself, which is the regression.
        /// </summary>
        [UnityTest]
        public IEnumerator ShowDoesNotStartUntilStaffDo()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return LoadMainScene();
            yield return StartHostAndWait();

            Component driver = FindByTypeName("ShowTimelineDriver");
            Assert.IsNotNull(driver, "No ShowTimelineDriver in " + k_Scene + ".");

            PropertyInfo showTime = driver.GetType().GetProperty("showTime");
            Assert.IsNotNull(showTime, "ShowTimelineDriver has no showTime property.");

            // Long enough that a show running at wall-clock speed would be unmistakably past
            // zero, short enough not to slow the suite down.
            float deadline = Time.realtimeSinceStartup + 2f;
            float highest = 0f;
            while (Time.realtimeSinceStartup < deadline)
            {
                highest = Mathf.Max(highest, (float)showTime.GetValue(driver));
                yield return null;
            }

            Debug.Log($"[ShowTest] highest show time over 2s of lobby: {highest:0.000}s");

            Assert.Less(highest, 0.1f,
                        $"The show advanced to {highest:0.00}s while still in the lobby. It is " +
                        "starting itself again -- check the pre-start hold in ServerStep, and " +
                        "remember that setting Held alone does not work because GateAt(0) " +
                        "returns null and the resume path reads that as cleared.");
        }

        /// <summary>
        /// Pressing Play with no session must not run the show either.
        ///
        /// This is the case the first fix missed. With no session the driver is not spawned, so
        /// `shared` is false and ServerStep -- where the pre-start hold had been added -- never
        /// runs at all; LocalStep handled it instead and had no gate. The editor therefore went
        /// on playing the whole running order to itself, which is what the test above could not
        /// see, because it hosts.
        /// </summary>
        [UnityTest]
        public IEnumerator ShowDoesNotStartWithoutASession()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return LoadMainScene();

            // Deliberately no StartHost here.
            NetworkManager manager = NetworkManager.Singleton;
            Assert.IsFalse(manager != null && manager.IsListening,
                           "A session is already running; this test needs none.");

            Component driver = FindByTypeName("ShowTimelineDriver");
            Assert.IsNotNull(driver, "No ShowTimelineDriver in " + k_Scene + ".");

            PropertyInfo showTime = driver.GetType().GetProperty("showTime");
            Assert.IsNotNull(showTime, "ShowTimelineDriver has no showTime property.");

            float deadline = Time.realtimeSinceStartup + 2f;
            float highest = 0f;
            while (Time.realtimeSinceStartup < deadline)
            {
                highest = Mathf.Max(highest, (float)showTime.GetValue(driver));
                yield return null;
            }

            Debug.Log($"[ShowTest] highest show time over 2s with no session: {highest:0.000}s");

            Assert.Less(highest, 0.1f,
                        $"The show ran to {highest:0.00}s with no session. LocalStep is missing " +
                        "the pre-start hold -- gating ServerStep alone does not cover pressing " +
                        "Play in the editor.");
        }

        /// <summary>
        /// The spawned avatar carries the component that replicates the touch. Without it a
        /// player's body forms only on their own machine and the other person never sees it --
        /// and the failure is silent, because everything still works locally.
        /// </summary>
        [UnityTest]
        public IEnumerator SpawnedAvatarCarriesFormationSync()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return LoadMainScene();
            yield return StartHostAndWait();

            NetworkManager manager = NetworkManager.Singleton;
            Assert.IsNotNull(manager.LocalClient?.PlayerObject, "No player object spawned.");

            GameObject avatar = manager.LocalClient.PlayerObject.gameObject;

            Component sync = null;
            foreach (Component c in avatar.GetComponentsInChildren<Component>(true))
            {
                if (c != null && c.GetType().Name == "AvatarFormationSync") { sync = c; break; }
            }

            Assert.IsNotNull(sync,
                             $"'{avatar.name}' has no AvatarFormationSync, so touching a 真人 " +
                             "would never reach the other machine. Re-run " +
                             "Tools > Onboarding > Wire Formation Sync.");

            Debug.Log($"[ShowTest] PASS: AvatarFormationSync on '{sync.gameObject.name}'.");
        }

        [TearDown]
        public void TearDown()
        {
            // Each test starts its own host, so one left listening would make the next fail for
            // a reason that has nothing to do with what it is testing.
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening) manager.Shutdown();
        }

        static Component FindByTypeName(string typeName)
        {
            foreach (Component c in Object.FindObjectsByType<Component>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c != null && c.GetType().Name == typeName) return c;
            }
            return null;
        }

        static IEnumerator StartHostAndWait()
        {
            NetworkManager manager = NetworkManager.Singleton;
            Assert.IsNotNull(manager, "No NetworkManager in " + k_Scene + ".");
            Assert.IsTrue(manager.StartHost(), "StartHost() returned false.");

            float deadline = Time.realtimeSinceStartup + k_Timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (manager.IsListening && manager.LocalClient is { PlayerObject: not null }) break;
                yield return null;
            }

            Assert.IsTrue(manager.IsListening, "The host never started listening.");
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
