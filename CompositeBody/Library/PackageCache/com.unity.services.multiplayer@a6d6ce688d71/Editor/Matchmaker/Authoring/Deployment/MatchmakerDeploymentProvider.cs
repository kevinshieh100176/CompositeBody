using Unity.Services.DeploymentApi.Editor;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Model;
using UnityEditor;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Deployment
{
    class MatchmakerDeploymentProvider : DeploymentProvider
    {
        public override string Service => L10n.Tr("Matchmaker");

        public override Command DeployCommand { get; }

        public override Command SyncItemsWithRemoteCommand { get; }

        public MatchmakerDeploymentProvider(
            MatchmakerDeployCommand matchmakerDeployCommand,
            MatchmakerSyncWithRemoteCommand matchmakerSyncWithRemoteCommand,
            MatchmakerFetchFromRemoteCommand matchmakerFetchFromRemoteCommand,
            ObservableMatchmakerQueueAssets observableMatchmakerQueueAssets)
            : base(observableMatchmakerQueueAssets.DeploymentItems)
        {
            DeployCommand = matchmakerDeployCommand;
            SyncItemsWithRemoteCommand = matchmakerSyncWithRemoteCommand;
            Commands.Add(matchmakerSyncWithRemoteCommand);
            Commands.Add(matchmakerFetchFromRemoteCommand);
        }
    }
}
