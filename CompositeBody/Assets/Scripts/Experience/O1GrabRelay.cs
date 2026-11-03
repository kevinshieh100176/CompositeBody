using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Carries "this player has their O-1 object in hand right now" across the network, so the
    /// host can put both players into the TouchDesigner stream.
    ///
    /// WHY THIS HAS TO EXIST. The O-1 objects are deliberately local: RoleVisibility switches off
    /// the other player's renderers AND its colliders, so a grab can only ever happen on the
    /// machine that owns the object, and nothing about it crosses the network. Nothing in the show
    /// needed it to until now -- the beat's 「兩人必須都抓住物件」 moment is reported as a task, and
    /// a task latches. A latch cannot go back down when the object is put down, and a live flag is
    /// what TD asked for.
    ///
    /// WHY NOT <see cref="CompositeBody.Diagnostics.InteractionDebugRelay"/>, which already mirrors
    /// local-only interaction states and is tantalisingly close: it mirrors them for display. It
    /// reports four times a second, it carries the latched 'fired' flag rather than a live one, and
    /// it says of itself that it adds no gameplay path. Borrowing it would make what the show sends
    /// out depend on the debug system staying switched on.
    ///
    /// SERVER-OWNED, CLIENT-REPORTED. Each machine works out its own answer -- it is the only one
    /// that can -- and the server owns the two flags, deriving which role a report belongs to from
    /// the sender's own client id rather than taking its word for it. That is the same line
    /// InteractionDebugRelay draws: a client must not be able to write the other player's half.
    ///
    /// ON CHANGE ONLY. A grab is a once-a-minute event. An RPC every frame for a bool that rarely
    /// moves is the kind of traffic that stays invisible until the venue's network is the thing
    /// that is struggling.
    /// </summary>
    [DisallowMultipleComponent]
    public class O1GrabRelay : NetworkBehaviour
    {
        public static O1GrabRelay Instance { get; private set; }

        readonly NetworkVariable<bool> m_Player1Holding =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<bool> m_Player2Holding =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [SerializeField, Min(0.02f), Tooltip("Seconds between checks of this machine's own grab " +
                                             "state. Not every frame: nothing downstream reacts " +
                                             "faster than a packet, and the check walks a list.")]
        float m_PollInterval = 0.1f;

        [SerializeField, Min(0.25f), Tooltip("Seconds between rescans for the O-1 objects. They " +
                                             "arrive with the onboarding content rather than with " +
                                             "the scene, so one scan at startup would find none.")]
        float m_RescanInterval = 1f;

        readonly List<O1GrabStation> m_Stations = new();
        float m_NextPoll;
        float m_NextRescan;
        bool m_LastSent;
        bool m_HasSent;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[O1GrabRelay] a second relay on '{name}'; keeping the first.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            // A client that drops while holding would otherwise leave its flag stuck at 1 for the
            // rest of the show, which reads downstream as a player standing there holding a coat.
            if (IsServer && NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        void HandleClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;
            Write(RoleOf(clientId), false);
        }

        /// <summary>
        /// Whether that player has their object in hand. False for a role nobody is playing, and
        /// false on every machine before the session is up, which is the same thing the rest of
        /// the stream does with a player who is not there.
        /// </summary>
        public bool IsHolding(PlayerRole role)
        {
            if (!IsSpawned) return false;

            return role switch
            {
                PlayerRole.Player1 => m_Player1Holding.Value,
                PlayerRole.Player2 => m_Player2Holding.Value,
                _ => false
            };
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (Time.unscaledTime >= m_NextRescan)
            {
                m_NextRescan = Time.unscaledTime + m_RescanInterval;
                Rescan();
            }

            if (Time.unscaledTime < m_NextPoll) return;
            m_NextPoll = Time.unscaledTime + m_PollInterval;

            bool holding = HoldingHere();
            if (m_HasSent && holding == m_LastSent) return;

            m_HasSent = true;
            m_LastSent = holding;
            ReportHoldingRpc(holding);
        }

        /// <summary>
        /// Whether anything is in a hand on THIS machine. It does not have to ask which role that
        /// is: the other player's objects have no colliders here, so nothing of theirs can be
        /// selected on this machine in the first place.
        /// </summary>
        bool HoldingHere()
        {
            for (int i = 0; i < m_Stations.Count; i++)
            {
                O1GrabStation station = m_Stations[i];
                if (station != null && station.held) return true;
            }
            return false;
        }

        void Rescan()
        {
            m_Stations.Clear();
            m_Stations.AddRange(FindObjectsByType<O1GrabStation>(FindObjectsSortMode.None));
        }

        /// <summary>
        /// The role is taken from the sender's client id, not from the message. A client that
        /// wanted to report the other player's hand would have to be the other player.
        /// </summary>
        [Rpc(SendTo.Server)]
        void ReportHoldingRpc(bool holding, RpcParams rpcParams = default)
        {
            Write(RoleOf(rpcParams.Receive.SenderClientId), holding);
        }

        void Write(PlayerRole role, bool holding)
        {
            if (role == PlayerRole.Player1) m_Player1Holding.Value = holding;
            else if (role == PlayerRole.Player2) m_Player2Holding.Value = holding;
        }

        /// <summary>
        /// The session's own role table. With no role assigned -- a desk test with one machine --
        /// the reporter stands in as Player 1, which is the same fallback the rest of the
        /// experience makes so that a single-machine run still produces a stream.
        /// </summary>
        static PlayerRole RoleOf(ulong clientId)
        {
            GameSessionManager session = GameSessionManager.Instance;
            if (session != null &&
                session.TryGetRoleForClient(clientId, out PlayerRole role) &&
                role != PlayerRole.Unassigned)
                return role;

            return PlayerRole.Player1;
        }
    }
}
