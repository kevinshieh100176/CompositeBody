using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.DeploymentApi.Editor;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.SyncWithRemote;
using UnityEditor;
using DeploymentStatus = Unity.Services.DeploymentApi.Editor.DeploymentStatus;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Deployment
{
    class MatchmakerSyncWithRemoteCommand : Command<MatchmakerConfigResource>
    {
        readonly IMatchmakerSyncWithRemoteHandler m_SyncWithRemoteHandler;
        readonly IConfigApiClient m_Client;
        readonly Func<IEnvironmentProvider> m_EnvironmentProvider;
        readonly IProjectIdProvider m_ProjectIdProvider;

        public override string Name => L10n.Tr("Refresh Status");

        public MatchmakerSyncWithRemoteCommand(
            IMatchmakerSyncWithRemoteHandler moduleSyncWithRemoteHandler,
            IConfigApiClient client,
            Func<IEnvironmentProvider> environmentsApi,
            IProjectIdProvider projectIdProvider)
        {
            m_SyncWithRemoteHandler = moduleSyncWithRemoteHandler;
            m_Client = client;
            m_EnvironmentProvider = environmentsApi;
            m_ProjectIdProvider = projectIdProvider;
        }

        public override async Task ExecuteAsync(IEnumerable<MatchmakerConfigResource> items, CancellationToken cancellationToken = default)
        {
            var configResources = items.ToList();
            foreach (var i in configResources)
            {
                i.Progress = 0f;
                i.Status = new DeploymentStatus();
            }

            try
            {
                await m_Client.Initialize(m_ProjectIdProvider.ProjectId, m_EnvironmentProvider().Current, cancellationToken);
            }
            catch (Exception e)
            {
                foreach (var configResource in configResources)
                {
                    configResource.Status = new DeploymentStatus(
                        "Failed to connect",
                        e.Message,
                        SeverityLevel.Error);
                }
                return;
            }
            await m_SyncWithRemoteHandler.SyncAsync(configResources, m_Client, cancellationToken);
        }
    }
}
