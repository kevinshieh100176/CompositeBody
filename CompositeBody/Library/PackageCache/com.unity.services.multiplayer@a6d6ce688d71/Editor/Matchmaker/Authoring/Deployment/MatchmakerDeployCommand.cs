using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.DeploymentApi.Editor;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;
using UnityEditor;
using UnityEngine;
using DeploymentStatus = Unity.Services.DeploymentApi.Editor.DeploymentStatus;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Deployment
{
    class MatchmakerDeployCommand : Command<MatchmakerConfigResource>
    {
        readonly IEditorMatchmakerDeploymentHandler m_DeploymentHandler;
        readonly IConfigApiClient m_Client;
        readonly Func<IEnvironmentProvider> m_EnvironmentProvider;
        readonly IProjectIdProvider m_ProjectIdProvider;

        public override string Name => L10n.Tr("Deploy");

        public MatchmakerDeployCommand(
            IEditorMatchmakerDeploymentHandler moduleDeploymentHandler,
            IConfigApiClient client,
            Func<IEnvironmentProvider> environmentsApi,
            IProjectIdProvider projectIdProvider)
        {
            m_DeploymentHandler = moduleDeploymentHandler;
            m_Client = client;
            m_EnvironmentProvider = environmentsApi;
            m_ProjectIdProvider = projectIdProvider;
        }

        public override async Task ExecuteAsync(IEnumerable<MatchmakerConfigResource> items, CancellationToken cancellationToken = default)
        {
            var itemList = items.ToList();

            OnPreDeploy(itemList);

            // Matchmaker requires deploy-time client initialization due to its
            // dependency to GSH which requires populating the GSH info
            await m_Client.Initialize(m_ProjectIdProvider.ProjectId, m_EnvironmentProvider().Current, cancellationToken);
            m_DeploymentHandler.Client = m_Client;
            var result = await m_DeploymentHandler.DeployAsync(
                itemList,
                m_Client.GetRemoteMultiplayResources(),
                false,
                false,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.AbortMessage))
            {
                Debug.LogError(result.AbortMessage);
            }
        }

        static void OnPreDeploy(IReadOnlyList<MatchmakerConfigResource> items)
        {
            foreach (var i in items)
            {
                i.Progress = 0f;
                i.Status = new DeploymentStatus();
            }
        }
    }
}
