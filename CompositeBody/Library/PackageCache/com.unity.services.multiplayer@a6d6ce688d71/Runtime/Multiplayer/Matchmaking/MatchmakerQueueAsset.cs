using UnityEngine;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Represents a Matchmaker queue configuration asset.<br/>
    /// This asset is created in the Unity Editor when importing a Matchmaker queue configuration from a .mmq file.<br/>
    /// It can be used to reference the queue in code from a MonoBehaviour or another asset so that changing the queue configuration from the mmq file will update it where it is used.
    /// </summary>
    public class MatchmakerQueueAsset : ScriptableObject
    {
        /// <summary>
        /// The name of the queue
        /// </summary>
        [field: SerializeField]
        public string Name { get; set; }

        /// <summary>
        /// The max players for the queue
        /// </summary>
        [field: SerializeField]
        public int MaxPlayers { get; set; }

        /// <summary>
        /// The timeout for the queue
        /// </summary>
        [field: SerializeField]
        public int TimeoutSeconds { get; set; }
    }
}
