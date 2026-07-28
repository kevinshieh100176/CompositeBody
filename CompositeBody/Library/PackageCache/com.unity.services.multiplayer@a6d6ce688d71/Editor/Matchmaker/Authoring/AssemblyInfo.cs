using System.Runtime.CompilerServices;

// Exposed Authoring APIs required by the Multiplayer Editor assembly
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Matchmaker.Api.IMatchmakerAdminApi
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.ProjectIdProvider
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Matchmaker.Model.EnvironmentConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Matchmaker.Model.QueueConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.MatchmakerAuthoringServices
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Model.MatchmakerAsset
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Editor")]

// Exposed Authoring APIs required by the Multiplayer Editor MultiplayerCenter assembly
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Model.MatchmakerAsset
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Editor.MultiplayerCenter")]

// Exposing Authoring API classes towards unity-gaming-services-cli
[assembly: InternalsVisibleTo("Unity.Services.Cli.Matchmaker")]

// Exposing Authoring APIs towards test assemblies
#if UNITY_INCLUDE_TESTS
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Shared.ApiResponse
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Shared.ApiErrorType
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Shared.ApiOperation
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Shared.ApiConfiguration
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Matchmaker.Api.IMatchmakerAdminApi
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Matchmaker.Model.QueueConfig
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Matchmaker.Model.ProblemDetails
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Matchmaker.Model.ProblemDetailsDetailsInner
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.MatchmakerAdminClient
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Tests.Editor.Matchmaker.Authoring")]

// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Matchmaker.Api.IMatchmakerAdminApi
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Shared.IApiConfiguration
// Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.IProjectIdProvider
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.EditorTests")]

[assembly: InternalsVisibleTo("Unity.Services.Cli.Matchmaker.UnitTest")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
#endif

// Needed to enable record types
namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring
{
    static class IsExternalInit {}
}
