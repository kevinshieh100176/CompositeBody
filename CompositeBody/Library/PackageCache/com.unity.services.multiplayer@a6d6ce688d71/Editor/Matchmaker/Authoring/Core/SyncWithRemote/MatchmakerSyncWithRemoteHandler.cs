using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.DeploymentApi.Editor;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Fetch;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;
using DeploymentStatus = Unity.Services.DeploymentApi.Editor.DeploymentStatus;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.SyncWithRemote
{
    class MatchmakerSyncWithRemoteHandler : IMatchmakerSyncWithRemoteHandler
    {
        /// <summary> A status to represent an item that is up to date with the remote.</summary>
        static readonly DeploymentStatus UpToDate = new DeploymentStatus("Up to date", string.Empty, SeverityLevel.Success);
        /// <summary> A status to represent an item that was modified locally.</summary>
        static readonly DeploymentStatus ModifiedLocally = new DeploymentStatus("Modified locally, deploy to update", string.Empty, SeverityLevel.Warning);
        /// <summary> A status to represent an item that is missing from the remote.</summary>
        static readonly DeploymentStatus NoRemote = new DeploymentStatus("No remote", "This configuration could not be found on the cloud project with the current environment. Deploy to create it.", SeverityLevel.Warning);
        /// <summary>A status to represent an item that failed to check the remote.</summary>
        static readonly DeploymentStatus FailedToConnect = new DeploymentStatus("Failed to connect", "Unable to reach the remote cloud configuration, check that your account is connected and the Cloud services is accessible.", SeverityLevel.Error);
        /// <summary>A status to represent an invalid remote configuration. </summary>
        static readonly DeploymentStatus InvalidRemote = new DeploymentStatus("Invalid remote file", "This configuration exists on the cloud project but is invalid. Deploy your local copy or open the cloud dashboard to fix it.", SeverityLevel.Error);

        readonly IDeepEqualityComparer m_deepEqualityComparer;

        public MatchmakerSyncWithRemoteHandler(IDeepEqualityComparer deepEqualityComparer)
        {
            m_deepEqualityComparer = deepEqualityComparer;
        }

        public async Task<SyncResult> SyncAsync(
            IEnumerable<MatchmakerConfigResource> files,
            IConfigApiClient client,
            CancellationToken ct = default)
        {
            var result = new SyncResult();

            bool configExist;
            EnvironmentConfig remoteConfig;
            List<(QueueConfig, List<ErrorResponse>)> remoteQueueConfigs;
            try
            {
                (configExist, remoteConfig) = await client.GetEnvironmentConfig(ct);
                remoteQueueConfigs = await client.ListQueues(ct);
            }
            catch (Exception)
            {
                foreach (var configFile in files)
                {
                    configFile.Status = FailedToConnect;
                    result.Failed.Add(configFile);
                }
                return result;
            }

            // Checking existing files state
            foreach (var configFile in files)
            {
                IMatchmakerConfig remoteConfigFile = null;
                List<ErrorResponse> errorResponses = null;
                switch (configFile.Content)
                {
                    case QueueConfig localQueueConfig:
                        (remoteConfigFile, errorResponses) = remoteQueueConfigs.Find(q => q.Item1.Name.Equals(localQueueConfig.Name));
                        break;
                    case EnvironmentConfig:
                        remoteConfigFile = configExist ? remoteConfig : null;
                        break;
                }

                if (remoteConfigFile == null)
                {
                    configFile.Status = NoRemote;
                    result.Missing.Add(configFile);
                }
                else
                {
                    if (errorResponses != null && errorResponses.Count > 0)
                    {
                        configFile.Status = InvalidRemote;
                        result.Failed.Add(configFile);
                    }
                    else if (!m_deepEqualityComparer.IsDeepEqual(configFile.Content, remoteConfigFile))
                    {
                        configFile.Status = ModifiedLocally;
                        result.NotInSync.Add(configFile);
                    }
                    else
                    {
                        configFile.Status = UpToDate;
                        result.UpToDate.Add(configFile);
                    }
                }
            }

            return result;
        }
    }
}
