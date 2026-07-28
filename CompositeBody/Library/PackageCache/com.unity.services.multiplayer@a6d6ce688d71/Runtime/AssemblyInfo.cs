using System.Runtime.CompilerServices;

using UnityEngine.Scripting;

// prevent Il2CPP code stripping
[assembly: AlwaysLinkAssembly]

[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Server")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.IntegrationTests")]
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Tests")]
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Tests.PlayMode")]
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.Editor")]
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.EditorTests")]
[assembly: InternalsVisibleTo("Unity.Services.Lobbies.Tests")]
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.E2ETests")]
[assembly: InternalsVisibleTo("Unity.Services.Multiplayer.E2ETests.Common")]
