using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Deploy
{
    interface IMatchmakerDeployHandler
    {
        Task<DeployResult> DeployAsync(
            IReadOnlyList<string> filePaths,
            MultiplayResources availableMultiplayResources,
            bool reconcile,
            bool dryRun,
            CancellationToken ct = default);

        Task<DeployResult> DeployAsync(
            List<MatchmakerConfigResource> items,
            MultiplayResources availableMultiplayResources,
            bool reconcile,
            bool dryRun,
            CancellationToken ct = default);
    }
}
