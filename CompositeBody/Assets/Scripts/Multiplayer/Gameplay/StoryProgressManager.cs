using System;
using Unity.Collections;
using Unity.Netcode;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Generic, server-authoritative task-gating primitive: a story beat identified by
    /// <c>taskId</c> unlocks only once both Player1 and Player2 have separately reported it
    /// complete. Content scripts call <see cref="ReportTaskCompleteRpc"/> and subscribe to
    /// <see cref="onTaskBeatUnlocked"/>; this class doesn't know what any task actually does.
    /// </summary>
    public class StoryProgressManager : NetworkBehaviour
    {
        public static StoryProgressManager Instance { get; private set; }

        readonly NetworkList<TaskProgress> m_Tasks = new();
        public NetworkList<TaskProgress> tasks => m_Tasks;

        /// <summary>Fired on every client (including host) once a task's flags are both true.</summary>
        public event Action<string> onTaskBeatUnlocked;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Call from client gameplay code when the local player finishes their half of a task.</summary>
        [Rpc(SendTo.Server)]
        public void ReportTaskCompleteRpc(FixedString64Bytes taskId, RpcParams rpcParams = default)
        {
            if (GameSessionManager.Instance == null) return;
            if (!TryGetRoleForClient(rpcParams.Receive.SenderClientId, out PlayerRole role)) return;

            int index = FindTaskIndex(taskId);
            TaskProgress progress = index >= 0 ? m_Tasks[index] : new TaskProgress { taskId = taskId };

            if (role == PlayerRole.Player1) progress.player1Done = true;
            else if (role == PlayerRole.Player2) progress.player2Done = true;

            if (index >= 0) m_Tasks[index] = progress;
            else m_Tasks.Add(progress);

            if (progress.player1Done && progress.player2Done)
            {
                onTaskBeatUnlocked?.Invoke(taskId.ToString());
                NotifyBeatUnlockedRpc(taskId);
            }
        }

        [Rpc(SendTo.Everyone)]
        void NotifyBeatUnlockedRpc(FixedString64Bytes taskId)
        {
            // Server already raised the event locally above; this forwards it to remote clients.
            if (IsServer) return;
            onTaskBeatUnlocked?.Invoke(taskId.ToString());
        }

        bool TryGetRoleForClient(ulong clientId, out PlayerRole role)
        {
            foreach (var entry in GameSessionManager.Instance.playerRoles)
            {
                if (entry.clientId == clientId)
                {
                    role = entry.role;
                    return true;
                }
            }
            role = PlayerRole.Unassigned;
            return false;
        }

        int FindTaskIndex(FixedString64Bytes taskId)
        {
            for (int i = 0; i < m_Tasks.Count; i++)
            {
                if (m_Tasks[i].taskId.Equals(taskId)) return i;
            }
            return -1;
        }

        public bool IsTaskComplete(string taskId)
        {
            int index = FindTaskIndex(taskId);
            if (index < 0) return false;
            var t = m_Tasks[index];
            return t.player1Done && t.player2Done;
        }
    }

    /// <summary>
    /// Row in the replicated task list. Needs INetworkSerializeByMemcpy for the same reason as
    /// PlayerRoleEntry: without a generated serializer, replicating the list throws and the
    /// client's synchronization silently never completes. FixedString64Bytes and the bools are
    /// all blittable, so memcpy serialization is valid.
    /// </summary>
    public struct TaskProgress : IEquatable<TaskProgress>, INetworkSerializeByMemcpy
    {
        public FixedString64Bytes taskId;
        public bool player1Done;
        public bool player2Done;

        public bool Equals(TaskProgress other) =>
            taskId.Equals(other.taskId) && player1Done == other.player1Done && player2Done == other.player2Done;

        public override bool Equals(object obj) => obj is TaskProgress other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(taskId, player1Done, player2Done);
    }
}
