using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.DeploymentApi.Editor;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Fetch;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;
using UnityEditor;
using UnityEngine;
using DeploymentStatus = Unity.Services.DeploymentApi.Editor.DeploymentStatus;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Deployment
{
    class MatchmakerFetchFromRemoteCommand : Command<MatchmakerConfigResource>
    {
        readonly IMatchmakerFetchHandler m_FetchHandler;
        readonly IConfigApiClient m_Client;
        readonly Func<IEnvironmentProvider> m_EnvironmentProvider;
        readonly IProjectIdProvider m_ProjectIdProvider;

        public override string Name => L10n.Tr("Fetch from Remote");

        public MatchmakerFetchFromRemoteCommand(
            IMatchmakerFetchHandler fetchHandler,
            IConfigApiClient client,
            Func<IEnvironmentProvider> environmentsApi,
            IProjectIdProvider projectIdProvider)
        {
            m_FetchHandler = fetchHandler;
            m_Client = client;
            m_EnvironmentProvider = environmentsApi;
            m_ProjectIdProvider = projectIdProvider;
        }

        public override async Task ExecuteAsync(IEnumerable<MatchmakerConfigResource> items,
            CancellationToken cancellationToken = default)
        {
            var configResources = items.ToList();
            var assetPaths = new List<string>(configResources.Count);
            foreach (var i in configResources)
            {
                i.Progress = 0f;
                i.Status = new DeploymentStatus();
                assetPaths.Add(i.Path);
            }

            try
            {
                await m_Client.Initialize(m_ProjectIdProvider.ProjectId, m_EnvironmentProvider().Current,
                    cancellationToken);
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

            if (EditorUtility.DisplayDialog("Fetching Remote Configurations",
                    "This will overwrite your local configurations with the remote versions. Do you want to continue?",
                    "Yes", "No"))
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    var result = await m_FetchHandler.FetchAsync(m_Client, assetPaths, true, false, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(result.AbortMessage))
                    {
                        Debug.LogError(result.AbortMessage);
                        foreach (var configResource in configResources)
                        {
                            configResource.Status = new DeploymentStatus(
                                "Failed to fetch",
                                result.AbortMessage,
                                SeverityLevel.Error);
                        }

                        return;
                    }

                    foreach (var configResource in result.Failed)
                    {
                        if (configResource.Status.MessageSeverity != SeverityLevel.Error)
                        {
                            configResource.Status = new DeploymentStatus(
                                "Failed to fetch",
                                configResource.Name,
                                SeverityLevel.Error);
                        }
                    }

                    foreach (var asset in result.Updated)
                    {
                        AssetDatabase.ImportAsset(asset.Path);
                    }
                }
                catch (Exception e)
                {
                    foreach (var configResource in configResources)
                    {
                        configResource.Status = new DeploymentStatus(
                            "Failed to fetch",
                            e.Message,
                            SeverityLevel.Error);
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }
        }
    }
}
