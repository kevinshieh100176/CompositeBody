namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model
{
    interface IMatchmakerConfig
    {
        public ConfigType Type { get; }

        public enum ConfigType
        {
            Unspecified,
            EnvironmentConfig,
            QueueConfig
        }
    }
}
