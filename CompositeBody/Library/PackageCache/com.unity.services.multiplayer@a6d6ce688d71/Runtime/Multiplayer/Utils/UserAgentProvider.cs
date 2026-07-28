using Unity.Services.Core.Configuration.Internal;
using UnityEngine;

namespace Unity.Services.Multiplayer
{
    internal class UserAgentProvider : IUserAgentProvider
    {
        const string k_PackageName = "com.unity.services.multiplayer";

        public string UserAgent { get; }

        public UserAgentProvider(IProjectConfiguration projectConfiguration)
        {
            var packageVersion = projectConfiguration?.GetString($"{k_PackageName}.version", "unknown") ?? "unknown";
            UserAgent = $"UnityPlayer/{Application.unityVersion} ({k_PackageName}/{packageVersion})";
        }
    }
}
