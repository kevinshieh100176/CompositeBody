using System.Runtime.CompilerServices;

// The Matchmaker authoring assembly needs access to the core models and some utilities
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.JsonObject
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.Range
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.RangeRelaxation
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.RangeRelaxationType
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.AgeType
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.Rule
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.RuleType
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.RuleExternalData
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.RuleBasedMatchDefinition
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.MatchLogicRulesConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.IMatchHostingConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.MatchIdConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.IMatchmakerConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.ErrorResponse
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.MultiplayConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.EnvironmentConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.QueueConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.QueueName
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.BasePoolConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.PoolName
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.PoolConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.FilteredPoolConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.FilteredPoolConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.IO.IFileSystem
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi.IConfigApiClient
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Deploy.IMatchmakerDeployHandler
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Deploy.MatchmakerDeployHandler
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Parser.IMatchmakerConfigParser
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Fetch.IDeepEqualityComparer
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.MatchmakerConfigResource
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Editor.Matchmaker.Authoring")]

// Multiplayer center defines a set of presets that rely on the following APIs
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.Range;
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.QueueConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.QueueName
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.BasePoolConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.PoolName
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.MatchLogicRulesConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.RuleBasedMatchDefinition
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.RuleBasedTeamDefinition
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.IMatchHostingConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.MatchIdConfig
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Editor.MultiplayerCenter")]

[assembly: InternalsVisibleTo("Unity.Services.Cli.Matchmaker")]
[assembly: InternalsVisibleTo("Unity.Services.Cli.Matchmaker.UnitTest")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

// Expose the Core Authoring APIs towards test assemblies
#if UNITY_INCLUDE_TESTS
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.Range
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.QueueConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.EnvironmentConfig
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Tests.Editor.Matchmaker.Authoring")]
#endif

// Needed to enable record types
namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core
{
    static class IsExternalInit {}
}
