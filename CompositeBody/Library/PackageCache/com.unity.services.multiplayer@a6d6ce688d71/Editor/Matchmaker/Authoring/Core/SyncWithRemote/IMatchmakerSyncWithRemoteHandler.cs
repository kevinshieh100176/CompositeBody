using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.SyncWithRemote
{
    interface IMatchmakerSyncWithRemoteHandler
    {
        Task<SyncResult> SyncAsync(
            IEnumerable<MatchmakerConfigResource> items,
            IConfigApiClient client,
            CancellationToken ct = default);
    }
}
