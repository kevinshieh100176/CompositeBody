using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRMultiplayer;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Server-authoritative session brain. Lives on a single scene-placed NetworkObject
    /// (not per-player), so it does not require touching the existing avatar prefab.
    ///
    /// Responsibilities:
    /// - Turns on Netcode connection approval and decides who gets which <see cref="PlayerRole"/>.
    /// - Keeps a server-only ledger keyed by <see cref="PlayerIdentity"/> guid (not OwnerClientId)
    ///   so a dropped headset can reconnect and reclaim its role instead of being treated as new.
    /// - Replicates a small <see cref="PlayerRoleEntry"/> list so every client (including host)
    ///   can look up its own role locally.
    /// - Gates the Lobby -> InProgress transition, which only staff (via the host machine) can flip.
    /// </summary>
    public class GameSessionManager : NetworkBehaviour
    {
        public const int MaxPlayers = 2;

        public static GameSessionManager Instance { get; private set; }

        [Header("Experience Scene")]
        [SerializeField, Tooltip("Scene loaded for everyone when staff press Start. Must be in Build Settings.")]
        string m_ExperienceSceneName = "CompositeBody_Experience";

        [SerializeField, Tooltip("Lobby geometry hidden once the experience begins. Optional.")]
        GameObject m_LobbyEnvironmentRoot;

        readonly NetworkList<PlayerRoleEntry> m_PlayerRoles = new();
        public NetworkList<PlayerRoleEntry> playerRoles => m_PlayerRoles;

        readonly NetworkVariable<SessionState> m_SessionState =
            new(SessionState.Lobby, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public SessionState sessionState => m_SessionState.Value;

        // Server-only. Never replicated -- survives a client's connection dropping.
        readonly Dictionary<string, PlayerRole> m_GuidToRole = new();
        readonly Dictionary<ulong, string> m_ClientIdToGuid = new();

        public event Action<SessionState> onSessionStateChanged;

        public int connectedPlayerCount
        {
            get
            {
                int count = 0;
                foreach (var entry in m_PlayerRoles)
                {
                    if (entry.isConnected) count++;
                }
                return count;
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Utils.LogWarning("[GameSessionManager] Duplicate instance found, destroying.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            if (NetworkManager.Singleton == null)
            {
                Utils.LogError("[GameSessionManager] No NetworkManager present in scene.");
                return;
            }

            // Must be set before StartHost()/StartClient() is called from the staff UI; both
            // happen well after Start() runs for every scene object, so this is safe.
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;

            // The identity token travels in NetworkConfig.ConnectionData and arrives as
            // ConnectionApprovalRequest.Payload. Without this every client is rejected for a
            // missing token. Note this is unrelated to UnityTransport.ConnectionData, which
            // carries the IP and port.
            NetworkManager.Singleton.NetworkConfig.ConnectionData = PlayerIdentity.BuildConnectionPayload();
            NetworkManager.Singleton.ConnectionApprovalCallback += HandleConnectionApproval;
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            NetworkManager.Singleton.OnServerStarted += HandleServerStarted;

            m_SessionState.OnValueChanged += HandleSessionStateChanged;

            // Scene synchronization gates IsConnectedClient, so its events are traced below.
            // NetworkManager.SceneManager does not exist until the manager starts, and hooking
            // it off a start callback proved unreliable, so Update polls until it attaches.
            // Otherwise "no scene events logged" is ambiguous: it cannot be distinguished from
            // "never subscribed", which is exactly the ambiguity that stalled this diagnosis.
            TrySubscribeSceneEvents();
        }

        void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.ConnectionApprovalCallback -= HandleConnectionApproval;
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
                NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
            }
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Also seeds the host, for the case where the server was already running by the time
        /// this component started. <see cref="HandleServerStarted"/> is only reachable from an
        /// event subscribed in <see cref="Start"/>, so a host brought up before that frame would
        /// never publish its own role -- and the symptom is the staff panel reporting "role not
        /// assigned" forever, with nothing in the log to say why.
        ///
        /// Seeding from here rather than from Start because the role list is a NetworkList:
        /// it cannot be written until the object is spawned, which is exactly what this is.
        /// Running twice is harmless -- the guid already maps to a role and the entry is
        /// overwritten in place.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            if (IsServer) HandleServerStarted();
        }

        /// <summary>Host doesn't go through its own approval callback, so seed it here.</summary>
        void HandleServerStarted()
        {
            if (!IsServer) return;

            string guid = PlayerIdentity.localGuid;
            if (!m_GuidToRole.TryGetValue(guid, out PlayerRole role))
            {
                role = PlayerRole.Player1;
                m_GuidToRole[guid] = role;
            }

            ulong localId = NetworkManager.Singleton.LocalClientId;
            m_ClientIdToGuid[localId] = guid;
            UpsertRoleEntry(localId, role, true);
        }

        void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            string guid = PlayerIdentity.ReadFromConnectionPayload(request.Payload);
            if (string.IsNullOrEmpty(guid))
            {
                response.Approved = false;
                response.Reason = "Missing player identity token.";
                return;
            }

            if (!m_GuidToRole.TryGetValue(guid, out PlayerRole role))
            {
                role = GetNextFreeRole();
                if (role == PlayerRole.Unassigned)
                {
                    response.Approved = false;
                    response.Reason = "Session already has two players.";
                    return;
                }
                m_GuidToRole[guid] = role;
            }

            m_ClientIdToGuid[request.ClientNetworkId] = guid;

            response.Approved = true;
            response.CreatePlayerObject = true;

            // The role entry is published in HandleClientConnected, not here. Approval runs
            // before the client has synchronized, so a NetworkList write at this point is not
            // reliably delivered to the very client being approved -- which is why the client
            // kept reporting "role not assigned" while the host saw both entries.
            Utils.Log($"[GameSessionManager] Approved guid {guid.Substring(0, 8)}... as {role} (clientId {request.ClientNetworkId}).");
        }

        /// <summary>
        /// Server-side. Fires once the client is fully connected and (with scene management on)
        /// synchronized, so this is the earliest point a NetworkList write reliably reaches it.
        /// </summary>
        void HandleClientConnected(ulong clientId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            if (!m_ClientIdToGuid.TryGetValue(clientId, out string guid))
            {
                // The host never runs through approval, so it is seeded in HandleServerStarted.
                if (clientId == NetworkManager.Singleton.LocalClientId) return;
                Utils.LogWarning($"[GameSessionManager] Connected client {clientId} has no identity mapping.");
                return;
            }

            if (!m_GuidToRole.TryGetValue(guid, out PlayerRole role)) return;

            UpsertRoleEntry(clientId, role, true);
            Utils.Log($"[GameSessionManager] Published {role} for clientId {clientId}. Roles now: {m_PlayerRoles.Count}");
        }

        void HandleClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;
            if (!m_ClientIdToGuid.TryGetValue(clientId, out string guid)) return;

            m_ClientIdToGuid.Remove(clientId);

            if (m_GuidToRole.TryGetValue(guid, out PlayerRole role))
            {
                SetRoleConnected(role, false);
            }
        }

        PlayerRole GetNextFreeRole()
        {
            bool p1Taken = m_GuidToRole.ContainsValue(PlayerRole.Player1);
            bool p2Taken = m_GuidToRole.ContainsValue(PlayerRole.Player2);
            if (!p1Taken) return PlayerRole.Player1;
            if (!p2Taken) return PlayerRole.Player2;
            return PlayerRole.Unassigned;
        }

        void UpsertRoleEntry(ulong clientId, PlayerRole role, bool isConnected)
        {
            for (int i = 0; i < m_PlayerRoles.Count; i++)
            {
                if (m_PlayerRoles[i].role == role)
                {
                    m_PlayerRoles[i] = new PlayerRoleEntry { clientId = clientId, role = role, isConnected = isConnected };
                    return;
                }
            }
            m_PlayerRoles.Add(new PlayerRoleEntry { clientId = clientId, role = role, isConnected = isConnected });
        }

        void SetRoleConnected(PlayerRole role, bool isConnected)
        {
            for (int i = 0; i < m_PlayerRoles.Count; i++)
            {
                if (m_PlayerRoles[i].role == role)
                {
                    var entry = m_PlayerRoles[i];
                    entry.isConnected = isConnected;
                    m_PlayerRoles[i] = entry;
                    return;
                }
            }
        }

        readonly List<string> m_SceneEventLog = new();

        bool m_SceneEventsSubscribed;

        void Update()
        {
            if (!m_SceneEventsSubscribed) TrySubscribeSceneEvents();
        }

        void TrySubscribeSceneEvents()
        {
            if (m_SceneEventsSubscribed) return;

            var sceneManager = NetworkManager.Singleton?.SceneManager;
            if (sceneManager == null) return;

            sceneManager.OnSceneEvent += HandleSceneEvent;
            m_SceneEventsSubscribed = true;
            Utils.Log("[GameSessionManager] Subscribed to NGO scene events.");
        }

        void HandleSceneEvent(SceneEvent sceneEvent)
        {
            string line = $"{sceneEvent.SceneEventType} '{sceneEvent.SceneName}' client={sceneEvent.ClientId}";
            m_SceneEventLog.Add(line);
            if (m_SceneEventLog.Count > 6) m_SceneEventLog.RemoveAt(0);
            Utils.Log($"[GameSessionManager] SceneEvent {line}");
        }

        /// <summary>
        /// Human-readable state dump. Surfaced on the staff panel because a built player has no
        /// console, and every failure so far has been invisible without one.
        /// </summary>
        public string BuildDiagnostics()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return "NetworkManager: none";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"server={nm.IsServer} client={nm.IsClient} connected={nm.IsConnectedClient} localId={nm.LocalClientId}");

            // If this is false on a client, no replicated state can arrive: the in-scene
            // NetworkObject never synchronized.
            sb.AppendLine($"sessionObjSpawned={IsSpawned}  roles={m_PlayerRoles.Count}");

            if (nm.IsServer)
                sb.AppendLine($"ngoConnectedClients={nm.ConnectedClientsIds.Count}");

            foreach (var entry in m_PlayerRoles)
                sb.AppendLine($"  [{entry.role}] clientId={entry.clientId} connected={entry.isConnected}");

            bool hasPlayerObj = nm.LocalClient != null && nm.LocalClient.PlayerObject != null;
            sb.AppendLine($"localPlayerObject={hasPlayerObj}");
            sb.AppendLine($"activeScene={SceneManager.GetActiveScene().name} loaded={SceneManager.sceneCount}");

            sb.Append($"sceneEventsSubscribed={m_SceneEventsSubscribed} events: ");
            if (m_SceneEventLog.Count == 0)
                sb.Append("(none)");
            else
                foreach (var line in m_SceneEventLog) sb.Append($"\n  {line}");

            return sb.ToString();
        }

        /// <summary>Looks up the local machine's own role from the replicated list.</summary>
        public bool TryGetLocalRole(out PlayerRole role)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                role = PlayerRole.Unassigned;
                return false;
            }

            ulong localId = NetworkManager.Singleton.LocalClientId;
            foreach (var entry in m_PlayerRoles)
            {
                if (entry.clientId == localId && entry.isConnected)
                {
                    role = entry.role;
                    return true;
                }
            }
            role = PlayerRole.Unassigned;
            return false;
        }

        /// <summary>
        /// Looks up the role held by a given connected client. Server-side callers use this to
        /// check that the sender of an RPC really is the player it is acting as, rather than
        /// trusting a role the message carried with it.
        /// </summary>
        public bool TryGetRoleForClient(ulong clientId, out PlayerRole role)
        {
            foreach (var entry in m_PlayerRoles)
            {
                if (entry.clientId == clientId && entry.isConnected)
                {
                    role = entry.role;
                    return true;
                }
            }
            role = PlayerRole.Unassigned;
            return false;
        }

        /// <summary>Swaps the two players' roles. Only allowed while still in the lobby.</summary>
        [Rpc(SendTo.Server)]
        public void RequestRoleSwapRpc()
        {
            if (m_SessionState.Value != SessionState.Lobby) return;
            if (m_PlayerRoles.Count < 2) return;

            var a = m_PlayerRoles[0];
            var b = m_PlayerRoles[1];
            (a.role, b.role) = (b.role, a.role);
            m_PlayerRoles[0] = a;
            m_PlayerRoles[1] = b;

            foreach (var kvp in m_ClientIdToGuid)
            {
                if (kvp.Key == a.clientId) m_GuidToRole[kvp.Value] = a.role;
                else if (kvp.Key == b.clientId) m_GuidToRole[kvp.Value] = b.role;
            }
        }

        /// <summary>Called locally on the host machine by the staff control panel.</summary>
        public bool StartExperience()
        {
            if (!IsServer) return false;
            if (m_SessionState.Value != SessionState.Lobby) return false;
            if (connectedPlayerCount < MaxPlayers) return false;

            m_SessionState.Value = SessionState.InProgress;

            if (!string.IsNullOrEmpty(m_ExperienceSceneName))
            {
                // Loaded through NetworkManager.SceneManager so every client loads it in step.
                // Additive on purpose: a Single load would unload the scene this manager lives
                // in and destroy the session (roles and story progress) mid-experience.
                var status = NetworkManager.SceneManager.LoadScene(m_ExperienceSceneName, LoadSceneMode.Additive);
                if (status != SceneEventProgressStatus.Started)
                    Utils.LogError($"[GameSessionManager] Could not load '{m_ExperienceSceneName}': {status}. " +
                                   "Check it is added to Build Settings.");
            }

            return true;
        }

        /// <summary>Runs on every client, so the lobby hides in step with the scene load.</summary>
        void HandleSessionStateChanged(SessionState previous, SessionState current)
        {
            if (current == SessionState.InProgress && m_LobbyEnvironmentRoot != null)
                m_LobbyEnvironmentRoot.SetActive(false);

            onSessionStateChanged?.Invoke(current);
        }

        /// <summary>
        /// Staff-only escape hatch: frees a role slot that's reserved by a guid that is not
        /// currently connected, so a new headset can take that spot instead of waiting forever
        /// for the original one to come back.
        /// </summary>
        public bool ReleaseAbandonedRole(PlayerRole role)
        {
            if (!IsServer) return false;
            if (m_SessionState.Value != SessionState.Lobby) return false;

            foreach (var entry in m_PlayerRoles)
            {
                if (entry.role == role && entry.isConnected) return false;
            }

            string guidToRemove = null;
            foreach (var kvp in m_GuidToRole)
            {
                if (kvp.Value == role)
                {
                    guidToRemove = kvp.Key;
                    break;
                }
            }
            if (guidToRemove == null) return false;

            m_GuidToRole.Remove(guidToRemove);
            for (int i = 0; i < m_PlayerRoles.Count; i++)
            {
                if (m_PlayerRoles[i].role == role)
                {
                    m_PlayerRoles.RemoveAt(i);
                    break;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Row in the replicated role list.
    ///
    /// INetworkSerializeByMemcpy is required, not optional: without it Netcode has no generated
    /// serializer for this type, and every attempt to replicate the list throws
    /// "Serialization has not been generated for type ...". That failure is silent from the
    /// outside -- local writes still succeed, so the host looks correct, while the client's
    /// synchronization payload can never be built and the client hangs half-connected forever.
    /// All fields are blittable (ulong, enum, bool), so memcpy serialization is valid here.
    /// </summary>
    public struct PlayerRoleEntry : IEquatable<PlayerRoleEntry>, INetworkSerializeByMemcpy
    {
        public ulong clientId;
        public PlayerRole role;
        public bool isConnected;

        public bool Equals(PlayerRoleEntry other) =>
            clientId == other.clientId && role == other.role && isConnected == other.isConnected;

        public override bool Equals(object obj) => obj is PlayerRoleEntry other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(clientId, role, isConnected);
    }
}
