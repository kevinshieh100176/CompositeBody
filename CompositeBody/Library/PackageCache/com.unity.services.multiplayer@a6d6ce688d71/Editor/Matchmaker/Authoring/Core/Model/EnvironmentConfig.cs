using System;
using System.Runtime.Serialization;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model
{
    [Serializable]
    class EnvironmentConfig : IMatchmakerConfig
    {
        [IgnoreDataMember]
        public IMatchmakerConfig.ConfigType Type => IMatchmakerConfig.ConfigType.EnvironmentConfig;

        [DataMember(Name = "$schema")]
        public string Schema = "https://ugs-config-schemas.unity3d.com/v1/matchmaker/matchmaker-environment-config.schema.json";

        [DataMember(IsRequired = true)] public bool Enabled { get; set; }

        [DataMember(IsRequired = false)] public QueueName DefaultQueueName { get; set; } = new();

        public static EnvironmentConfig GetDefault()
        {
            return new EnvironmentConfig()
            {
                Enabled = true,
                DefaultQueueName = new QueueName("default-queue")
            };
        }
    }
}
