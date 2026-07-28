using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Fetch
{
    interface IMatchmakerFetchHandler
    {
        Task<FetchResult> FetchAsync(string rootDir, IReadOnlyList<string> filePaths, bool reconcile, bool dryRun, CancellationToken ct = default);
        Task<FetchResult> FetchAsync(IConfigApiClient clientApi, IReadOnlyList<string> filePaths, bool reconcile, bool dryRun, CancellationToken ct = default);
    }
}
