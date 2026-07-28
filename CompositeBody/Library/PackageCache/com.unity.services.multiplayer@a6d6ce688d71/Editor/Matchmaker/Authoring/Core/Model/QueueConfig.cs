using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model
{
    [Serializable]
    class QueueConfig : IMatchmakerConfig
    {
        [IgnoreDataMember]
        public IMatchmakerConfig.ConfigType Type => IMatchmakerConfig.ConfigType.QueueConfig;

        [DataMember(Name = "$schema")]
        public string Schema = "https://ugs-config-schemas.unity3d.com/v1/matchmaker/matchmaker-queue.schema.json";

        [DataMember(IsRequired = true)] public QueueName Name { get; set; }

        [DataMember(IsRequired = true)] public bool Enabled { get; set; } = true;

        [DataMember(IsRequired = true)] public int MaxPlayersPerTicket { get; set; }

        [DataMember(IsRequired = false)] public BasePoolConfig DefaultPool { get; set; }

        [DataMember(IsRequired = false)] public List<FilteredPoolConfig> FilteredPools { get; set; } = new();

        public static QueueConfig GetDefault(QueueConfig obj = null)
        {
            var res = obj ?? new QueueConfig();
            res.Name = new QueueName("default-queue");
            res.Enabled = true;
            res.MaxPlayersPerTicket = 2;
            res.DefaultPool = new BasePoolConfig
            {
                Name = new PoolName("default-pool"),
                Enabled = true,
                TimeoutSeconds = 90,
                MatchLogic = new MatchLogicRulesConfig
                {
                    Name = "Rules",
                    MatchDefinition = new RuleBasedMatchDefinition()
                    {
                        teams = new List<RuleBasedTeamDefinition>()
                        {
                            new RuleBasedTeamDefinition()
                            {
                                name = "Team",
                                playerCount = new Range()
                                {
                                    min = 1,
                                    max = 2
                                },
                                teamCount = new Range()
                                {
                                    min = 2,
                                    max = 2
                                }
                            }
                        }
                    }
                },
                MatchHosting = new MatchIdConfig()
            };
            return res;
        }
    }

    class PoolConfig
    {
        internal PoolConfig() {}

        internal PoolConfig(PoolConfig poolConfig)
        {
            if (poolConfig == null) return;
            Enabled = poolConfig.Enabled;
            Name = poolConfig.Name;
            TimeoutSeconds = poolConfig.TimeoutSeconds;
            MatchLogic = poolConfig.MatchLogic;
            MatchHosting = poolConfig.MatchHosting;
        }

        [DataMember(IsRequired = true)] public PoolName Name { get; set; }

        [DataMember(IsRequired = true)] public bool Enabled { get; set; }

        [DataMember(IsRequired = true)] public int TimeoutSeconds { get; set; }

        [DataMember(IsRequired = true)]
        public MatchLogicRulesConfig MatchLogic { get; set; }

        [DataMember(IsRequired = true)] public IMatchHostingConfig MatchHosting { get; set; }
    }

    class BasePoolConfig : PoolConfig
    {
        internal BasePoolConfig() {}

        internal BasePoolConfig(PoolConfig poolConfig, List<PoolConfig> variants = null) : base(poolConfig)
        {
            Variants = variants ?? new();
        }

        [DataMember(IsRequired = false)] public List<PoolConfig> Variants { get; set; } = new();
    }

    class FilteredPoolConfig : BasePoolConfig
    {
        internal FilteredPoolConfig() {}

        internal FilteredPoolConfig(PoolConfig poolConfig, List<Filter> filters = null,
                                    List<PoolConfig> variants = null) : base(poolConfig, variants)
        {
            Filters = filters ?? new();
        }

        [DataMember(IsRequired = true)] public List<Filter> Filters { get; set; } = new();

        internal class Filter
        {
            [DataMember(IsRequired = false)] public string Attribute { get; set; }

            [DataMember(IsRequired = true)] public FilterOperator Operator { get; set; } = FilterOperator.Equal;

            [DataMember(IsRequired = true)] public FilterValue Value { get; set; }

            public enum FilterOperator
            {
                Equal,
                NotEqual,
                LessThan,
                GreaterThan,
                CommonExpressionLanguage
            }

            // FilterValue represents a filter value, which can be a string or a number.
            public class FilterValue : JsonObject
            {
                public FilterValue(string value)
                    : base(value) {}

                public FilterValue(int value)
                    : base(value.ToString()) {}

                public FilterValue(float value)
                    : base(value.ToString()) {}
            }
        }
    }

    class MatchLogicRulesConfig
    {
        [DataMember(IsRequired = true)] public RuleBasedMatchDefinition MatchDefinition { get; set; }

        [DataMember(IsRequired = true)] public string Name { get; set; }

        [DataMember(IsRequired = false)] public bool BackfillEnabled { get; set; }
    }

    interface IMatchHostingConfig
    {
        public MatchHostingType Type { get; set; }

        public enum MatchHostingType
        {
            Invalid = 0,
            Multiplay = 1,
            MatchId = 2,
            CloudCode = 3
        }
    }

    class MultiplayConfig : IMatchHostingConfig
    {
        [DataMember(IsRequired = true)]
        public IMatchHostingConfig.MatchHostingType Type { get; set; } = IMatchHostingConfig.MatchHostingType.Multiplay;

        [DataMember(IsRequired = true)] public string FleetName { get; set; }

        [DataMember(IsRequired = true)] public string BuildConfigurationName { get; set; }

        [DataMember(IsRequired = true)] public string DefaultQoSRegionName { get; set; }

        [DataMember(IsRequired = false)] public string ModuleName { get; set; }

        [DataMember(IsRequired = false)] public string AllocateFunctionName { get; set; }

        [DataMember(IsRequired = false)] public string PollFunctionName { get; set; }
    }

    class MatchIdConfig : IMatchHostingConfig
    {
        [DataMember(IsRequired = true)]
        public IMatchHostingConfig.MatchHostingType Type { get; set; } =
            IMatchHostingConfig.MatchHostingType.MatchId;
    }

    class CloudCodeConfig : IMatchHostingConfig
    {
        [DataMember(IsRequired = true)]
        public IMatchHostingConfig.MatchHostingType Type { get; set; } =
            IMatchHostingConfig.MatchHostingType.CloudCode;

        [DataMember(IsRequired = true)] public string ModuleName { get; set; }

        [DataMember(IsRequired = true)] public string AllocateFunctionName { get; set; }

        [DataMember(IsRequired = true)] public string PollFunctionName { get; set; }
    }
}
