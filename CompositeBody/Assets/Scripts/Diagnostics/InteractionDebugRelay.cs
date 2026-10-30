using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// The network half of the debug panel: it carries each machine's local-only interaction
    /// states to the others, and it carries a press of a button on one machine to the machine
    /// that can act on it.
    ///
    /// WHY THIS EXISTS AT ALL. Half the interactions in the piece are deliberately local --
    /// touching a 真人 forms your own hands, calibrating moves your own origin -- and nothing
    /// about them is replicated, correctly. That leaves a debug list that can only ever describe
    /// the machine it is running on, which in a two-player piece is the half of the problem you
    /// are not looking at. So this mirrors those states for display, on change only.
    ///
    /// IT IS A SEPARATE COMPONENT FROM EVERYTHING IT DRIVES, and it adds no gameplay path: the
    /// only thing it can make a client do is run an interaction that client already implements,
    /// through that interaction's own code. There is no "set this state" wire into the show.
    ///
    /// SITS ON THE SESSION MANAGER OBJECT, which already has the NetworkObject and already
    /// outlives the additively loaded experience scene -- the states being mirrored belong to the
    /// session, not to a beat.
    ///
    /// Forcing is gated twice: <see cref="InteractionDebug.fireEnabled"/> for the build, and
    /// server-side authority for the flags themselves, so a client cannot write another player's
    /// half of a gate however its panel is configured.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionDebugRelay : NetworkBehaviour
    {
        public static InteractionDebugRelay Instance { get; private set; }

        /// <summary>
        /// Whether this machine may write flags it was not told about by their owner. Server and
        /// debug-enabled: the two conditions are separate on purpose, since a client build with
        /// the switch on still must not be able to fake the other player's half.
        /// </summary>
        public static bool canForce =>
            Instance != null && Instance.IsSpawned && Instance.IsServer && InteractionDebug.fireEnabled;

        /// <summary>One machine's view of one of its own local-only interactions.</summary>
        public struct MachineState : IEquatable<MachineState>, INetworkSerializeByMemcpy
        {
            public ulong clientId;
            public FixedString64Bytes id;
            public bool fired;
            public float secondsToTimeout;
            public FixedString64Bytes detail;

            public bool Equals(MachineState other) =>
                clientId == other.clientId && id.Equals(other.id) && fired == other.fired &&
                Mathf.Approximately(secondsToTimeout, other.secondsToTimeout) &&
                detail.Equals(other.detail);

            public override bool Equals(object obj) => obj is MachineState other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(clientId, id, fired, detail);
        }

        readonly NetworkList<MachineState> m_Machines = new();

        /// <summary>Every machine's local-only interaction states, this one's included.</summary>
        public NetworkList<MachineState> machines => m_Machines;

        // What this machine last sent, so a row that has not changed costs nothing.
        readonly Dictionary<string, MachineState> m_LastSent = new();

        [SerializeField, Min(0.05f), Tooltip("Seconds between checks for a local state worth " +
                                            "sending. Not every frame: the panel refreshes at " +
                                            "its own rate and nothing here is gameplay.")]
        float m_ReportInterval = 0.25f;

        float m_NextReport;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
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
            m_LastSent.Clear();

            // A machine that drops takes its rows with it, so the panel does not keep showing a
            // player who is not there as "not fired" -- which reads as a player who is stuck.
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnect;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnect;
        }

        void HandleClientDisconnect(ulong clientId)
        {
            for (int i = m_Machines.Count - 1; i >= 0; i--)
            {
                if (m_Machines[i].clientId == clientId) m_Machines.RemoveAt(i);
            }
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (Time.unscaledTime < m_NextReport) return;
            m_NextReport = Time.unscaledTime + m_ReportInterval;

            ReportLocalStates();
        }

        /// <summary>
        /// Send this machine's local-only rows, the changed ones only. Player- and gate-scope
        /// rows are left alone: those are replicated by the show itself, and mirroring them here
        /// would mean the panel could show a disagreement between two copies of one fact.
        /// </summary>
        void ReportLocalStates()
        {
            ulong me = NetworkManager.LocalClientId;

            foreach (IDebugInteraction interaction in InteractionDebug.entries)
            {
                if (interaction.debugScope != DebugInteractionScope.LocalMachine) continue;

                DebugInteractionState state = interaction.DebugState();
                var row = new MachineState
                {
                    clientId = me,
                    id = Truncate(interaction.debugId, 64),
                    fired = state.fired,
                    secondsToTimeout = state.secondsToTimeout,
                    detail = Truncate(state.detail, 64),
                };

                if (m_LastSent.TryGetValue(interaction.debugId, out MachineState last) &&
                    last.Equals(row) &&
                    Mathf.Abs(last.secondsToTimeout - row.secondsToTimeout) < 1f)
                {
                    continue;
                }

                m_LastSent[interaction.debugId] = row;

                // The server writes the list directly; a client has to ask it to. Same row
                // either way, so the host's own machine appears in the list like anyone else's
                // and a client's panel can show the host.
                if (IsServer) Store(row);
                else ReportStateRpc(row.id, row.fired, row.secondsToTimeout, row.detail);
            }
        }

        [Rpc(SendTo.Server)]
        void ReportStateRpc(FixedString64Bytes id, bool fired, float secondsToTimeout,
                            FixedString64Bytes detail, RpcParams rpcParams = default)
        {
            Store(new MachineState
            {
                clientId = rpcParams.Receive.SenderClientId,
                id = id,
                fired = fired,
                secondsToTimeout = secondsToTimeout,
                detail = detail,
            });
        }

        void Store(MachineState row)
        {
            for (int i = 0; i < m_Machines.Count; i++)
            {
                if (m_Machines[i].clientId != row.clientId || !m_Machines[i].id.Equals(row.id)) continue;
                m_Machines[i] = row;
                return;
            }
            m_Machines.Add(row);
        }

        /// <summary>
        /// Ask one machine to run one of its own interactions, as if its player had. Server only,
        /// and the client decides for itself whether it can -- the message names an interaction,
        /// it does not carry a state to apply.
        /// </summary>
        public void FireOnClient(ulong clientId, string debugId)
        {
            if (!canForce || string.IsNullOrWhiteSpace(debugId)) return;

            if (clientId == NetworkManager.LocalClientId)
            {
                FireLocally(debugId);
                return;
            }

            Debug.Log($"[InteractionDebug] asking client {clientId} to fire '{debugId}'.");
            FireLocalRpc(Truncate(debugId, 64), RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void FireLocalRpc(FixedString64Bytes debugId, RpcParams rpcParams)
        {
            FireLocally(debugId.ToString());
        }

        static void FireLocally(string debugId)
        {
            foreach (IDebugInteraction interaction in InteractionDebug.entries)
            {
                if (interaction.debugId != debugId) continue;

                if (!interaction.DebugCanFire(DebugFireTarget.Local))
                {
                    Debug.LogWarning($"[InteractionDebug] asked to fire '{debugId}' here, but it " +
                                     "cannot be fired on this machine right now.");
                    return;
                }

                Debug.Log($"[InteractionDebug] firing '{debugId}' on this machine, by request.");
                interaction.DebugFire(DebugFireTarget.Local);
                return;
            }

            Debug.LogWarning($"[InteractionDebug] asked to fire '{debugId}', which is not " +
                             "registered on this machine. Are both machines on the same build?");
        }

        /// <summary>The client holding a role, or false if nobody does.</summary>
        public static bool TryGetClientForRole(PlayerRole role, out ulong clientId)
        {
            clientId = 0;
            GameSessionManager session = GameSessionManager.Instance;
            if (session == null) return false;

            foreach (var entry in session.playerRoles)
            {
                if (entry.role != role || !entry.isConnected) continue;
                clientId = entry.clientId;
                return true;
            }
            return false;
        }

        /// <summary>The role a client holds, for labelling a mirrored row.</summary>
        public static PlayerRole RoleOf(ulong clientId)
        {
            GameSessionManager session = GameSessionManager.Instance;
            if (session == null) return PlayerRole.Unassigned;

            foreach (var entry in session.playerRoles)
            {
                if (entry.clientId == clientId) return entry.role;
            }
            return PlayerRole.Unassigned;
        }

        static FixedString64Bytes Truncate(string value, int bytes)
        {
            if (string.IsNullOrEmpty(value)) return default;

            // FixedString64Bytes throws rather than truncating, and a detail line long enough to
            // overflow it is a cosmetic problem that must not become an exception every refresh.
            // 56 rather than 64 because the type's payload is smaller than its name, and a
            // multi-byte character can take four bytes of whatever is left.
            int limit = Mathf.Min(bytes, 56);

            var result = new FixedString64Bytes();
            foreach (char c in value)
            {
                if (result.Length + 4 >= limit) break;
                result.Append(c);
            }
            return result;
        }
    }
}
