using Unity.Netcode;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace XRMultiplayer
{
    /// <summary>
    /// Manages the network functionality for VR multiplayer.
    /// </summary>
    public class NetworkManagerVRMultiplayer : NetworkManager
    {
        [SerializeField, Tooltip("Set this to control how much logging is generated")]
        LogLevel m_LogLevel;

        [SerializeField, Tooltip("This should almost always be set to true")]
        bool m_RunInBackground = true;

        [SerializeField]
        NetworkConfig m_NetworkConfig;

        /// <summary>
        /// THIS SHADOWS NetworkManager.Awake, WHICH UNITY WILL THEREFORE NOT CALL.
        ///
        /// Unity sends Awake to the most-derived declaration only, and the base class's Awake is
        /// private, so declaring one here silently replaces it. That base Awake is where Netcode
        /// creates its logger, calls NetworkConfig.InitializePrefabs(), hooks sceneUnloaded and
        /// raises OnInstantiated -- none of which happened.
        ///
        /// It worked in the editor and only in the editor. NetworkManager.OnValidate also
        /// creates the logger as a fallback, and OnValidate runs in the editor and never in a
        /// player. So the editor papered over it and a build threw a NullReferenceException on
        /// the first line of StartHost(), which is Log.CaptureFunctionCall() -- pressing Host did
        /// nothing at all.
        ///
        /// The config is assigned BEFORE the base runs, because the base's Awake is what calls
        /// InitializePrefabs() and it has to see the real config rather than the empty one the
        /// base field holds on disk.
        /// </summary>
        void Awake()
        {
            LogLevel = m_LogLevel;
            RunInBackground = m_RunInBackground;
            NetworkConfig = m_NetworkConfig;
            Utils.s_LogLevel = LogLevel;

            InvokeBaseAwake();
        }

        /// <summary>
        /// Run the base class's private Awake. Reflection because it is private and there is no
        /// other way to reach it; once per NetworkManager, so the cost is irrelevant. If a future
        /// Netcode renames or removes it this says so loudly rather than failing the way it just
        /// did -- silently, and only in builds.
        /// </summary>
        void InvokeBaseAwake()
        {
            MethodInfo awake = typeof(NetworkManager).GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            if (awake == null)
            {
                Debug.LogError("[NetworkManagerVRMultiplayer] could not find NetworkManager.Awake " +
                               "to invoke. Netcode will be left half-initialised and hosting will " +
                               "throw. Check whether the base Awake was renamed in this version.",
                               this);
                return;
            }

            awake.Invoke(this, null);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(NetworkManagerVRMultiplayer))]
    class VRMutliplayerTemplateNetworkManagerEditor : Editor
    {
        /// <summary>
        /// This function is called when the inspector is drawn.
        /// </summary>
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (Application.isPlaying)
            {
                switch (XRINetworkGameManager.CurrentConnectionState.Value)
                {
                    case XRINetworkGameManager.ConnectionState.None:
                        GUILayout.Box("Authenticating");
                        break;
                    case XRINetworkGameManager.ConnectionState.Authenticating:
                        GUILayout.Box("Authenticating");
                        break;
                    case XRINetworkGameManager.ConnectionState.Authenticated:
                        if (GUILayout.Button("Connect"))
                        {
                            XRINetworkGameManager.Instance.QuickJoinLobby();
                        }
                        break;
                    case XRINetworkGameManager.ConnectionState.Connecting:
                        GUILayout.Box("Connecting");
                        break;
                    case XRINetworkGameManager.ConnectionState.Connected:
                        if (GUILayout.Button("Disconnect"))
                        {
                            XRINetworkGameManager.Instance.Disconnect();
                        }
                        break;
                }
            }
            else
            {
                GUILayout.Box("Game not running.");
            }
        }
    }
#endif
}
