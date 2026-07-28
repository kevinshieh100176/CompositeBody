using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Editor")]
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Tests.Editor.Matchmaker.Authoring")]
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Matchmaker.Authoring")]
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Editor.Matchmaker.Authoring")]
[assembly: InternalsVisibleTo("Unity.Services.Cli.Matchmaker")]
[assembly: InternalsVisibleTo("Unity.Services.Cli.Matchmaker.UnitTest")]
[assembly: InternalsVisibleTo("InternalsVisible.DynamicProxyGenAssembly2")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

// Needed to enable record types
namespace Unity.Services.Multiplayer.Editor.Shared
{
    static class IsExternalInit {}
}
