using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.DeploymentApi.Editor;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Fetch;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Parser;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Validation;
using DeploymentStatus = Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi.DeploymentStatus;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Deploy
{
    class MatchmakerDeployHandler : IMatchmakerDeployHandler
    {
        /// <summary>A status to represent an item that failed to check the remote.</summary>
        static readonly Unity.Services.DeploymentApi.Editor.DeploymentStatus FailedToConnect = new("Failed to connect", "Unable to reach the remote cloud configuration, check that your account is connected and the Cloud services is accessible.", SeverityLevel.Error);

        protected IConfigApiClient m_configApiClient;
        readonly IMatchmakerConfigParser m_configParser;
        readonly IDeepEqualityComparer m_deepEqualityComparer;

        public MatchmakerDeployHandler(IConfigApiClient configApiClient,
                                       IMatchmakerConfigParser configParser,
                                       IDeepEqualityComparer deepEqualityComparer)
        {
            m_configApiClient = configApiClient;
            m_configParser = configParser;
            m_deepEqualityComparer = deepEqualityComparer;
        }

        public async Task<DeployResult> DeployAsync(
            IReadOnlyList<string> localResourcePaths,
            MultiplayResources availableMultiplayResources,
            bool reconcile,
            bool dryRun,
            CancellationToken ct = default)
        {
            // Parsing
            var parseResult = await m_configParser.Parse(localResourcePaths, ct);
            if (parseResult.parsed.Count == 0 && !reconcile)
            {
                var result = new DeployResult();
                result.Failed.AddRange(parseResult.failed);
                return result;
            }

            var items = parseResult.parsed;

            var res = await DeployAsync(items, availableMultiplayResources, reconcile, dryRun, ct);
            res.Failed.AddRange(parseResult.failed);
            return res;
        }

        public async Task<DeployResult> DeployAsync(
            List<MatchmakerConfigResource> items,
            MultiplayResources availableMultiplayResources,
            bool reconcile,
            bool dryRun,
            CancellationToken ct)
        {
            DeployResult result = new DeployResult();
            bool configExist;
            EnvironmentConfig originalEnvironmentConfig;
            List<QueueConfig> originalQueues;
            try
            {
                (configExist, originalEnvironmentConfig) = await m_configApiClient.GetEnvironmentConfig(ct);
                // Error don't matter on list queue cause we want to fix the config by deploying
                originalQueues = (await m_configApiClient.ListQueues(ct)).Select(q => q.Item1).ToList();
            }
            catch (Exception e)
            {
                result.AbortMessage = e.Message;
                foreach (var item in items)
                {
                    item.Status = FailedToConnect;
                    result.Failed.Add(item);
                }
                return result;
            }

            // envConfigFile.Content should be EnvironmentConfig (handled by parser)
            // If no envConfig to deploy, take a default envConfig
            var envConfigFile = items.Find(p => p.Content is EnvironmentConfig);
            var queueConfigFiles = items.Where(p => p.Content is QueueConfig).ToList();
            var targetEnvConfig = envConfigFile?.Content as EnvironmentConfig ?? new EnvironmentConfig();

            var envConfigCreated = false;
            // Deploy envConfig first if it's the first deployment
            List<ErrorResponse> errors;
            if (!configExist)
            {
                var initConfig = new EnvironmentConfig
                {
                    // Don't deploy default queue name as part of original upsert since the reference isn't deployed yet
                    DefaultQueueName = new QueueName(),
                    // In the case users are not pushing any environment files, we still want to ensure the environment is enabled.
                    // Having it disabled will cause matchmake to fail and it's impossible to enable it later from the Cloud Dashboard once a queue is deployed.
                    Enabled = true,
                };
                errors = await m_configApiClient.UpsertEnvironmentConfig(initConfig, dryRun, ct);
                if (errors.Count != 0)
                {
                    if (envConfigFile != null)
                    {
                        envConfigFile.Status = DeploymentStatus.Get("Failed to upsert Environment Config", errors);
                        result.Failed.Add(envConfigFile);
                    }

                    return result;
                }

                envConfigCreated = true;
            }


            // Queues reconcile
            if (reconcile)
            {
                var targetQueueNames = queueConfigFiles.Select(q =>
                {
                    // q.Content should be QueueConfig (handled by parser)
                    var queueConfig = q.Content as QueueConfig;
                    return queueConfig?.Name?.ToString();
                }).ToList();

                foreach (var originalQueue in originalQueues)
                {
                    if (targetQueueNames.Contains(originalQueue.Name.ToString()))
                        continue;

                    var reconcileQueueResult = new MatchmakerConfigResource
                    {
                        Name = originalQueue.Name.ToString(),
                        Content = originalQueue,
                        Status = DeploymentStatus.Get("Deleted", "queue", dryRun, originalQueue.Name.ToString()),
                        Path = ""
                    };
                    await m_configApiClient.DeleteQueue(originalQueue.Name, dryRun, ct);
                    result.Deleted.Add(reconcileQueueResult);
                }
            }

            foreach (var queueConfigFile in queueConfigFiles)
            {
                // queueConfigFile.Content should be QueueConfig (handled by parser)
                var queueConfig = queueConfigFile.Content as QueueConfig;
                if (queueConfig == null)
                    continue;

                var originalQueue = originalQueues.Find(q => q.Name.Equals(queueConfig.Name));
                if (originalQueue != null && m_deepEqualityComparer.IsDeepEqual(queueConfig, originalQueue))
                {
                    queueConfigFile.Status = new DeploymentApi.Editor.DeploymentStatus("Deployed", $"Updated - Queue '{queueConfig.Name}' is up to date", SeverityLevel.Success);
                    queueConfigFile.Progress = 100f;
                    continue;
                }

                errors = await m_configApiClient.UpsertQueue(queueConfig, availableMultiplayResources, dryRun, ct);
                if (errors.Count != 0)
                {
                    queueConfigFile.Status = DeploymentStatus.Get($"Failed to update queue '{queueConfig.Name}'", errors);
                    result.Failed.Add(queueConfigFile);
                    continue;
                }

                if (originalQueue == null)
                {
                    queueConfigFile.Status = DeploymentStatus.Get("Created", "queue", dryRun, queueConfig.Name.ToString(), SeverityLevel.Success);
                    queueConfigFile.Progress = 100f;
                    result.Created.Add(queueConfigFile);
                }
                else
                {
                    var poolUpdateDetails =
                        PoolUpdateResult.GetPoolDeployResultFromQueues(originalQueue, queueConfig, m_deepEqualityComparer,
                            dryRun);
                    queueConfigFile.Status = DeploymentStatus.Get("Updated", "queue", dryRun, queueConfig.Name.ToString(),
                        SeverityLevel.Success, poolUpdateDetails);
                    queueConfigFile.Progress = 100f;
                    result.Updated.Add(queueConfigFile);
                }

                if (!dryRun)
                    result.Authored.Add(queueConfigFile);
            }

            if (envConfigFile == null || m_deepEqualityComparer.IsDeepEqual(targetEnvConfig, originalEnvironmentConfig))
            {
                if (envConfigFile != null)
                {
                    envConfigFile.Status =
                        new DeploymentApi.Editor.DeploymentStatus(
                            "Deployed",
                            "Updated - Environment Config is up-to-date",
                            SeverityLevel.Success);
                    envConfigFile.Progress = 100f;
                }

                return result;
            }

            errors = await m_configApiClient.UpsertEnvironmentConfig(targetEnvConfig, dryRun, ct);
            if (errors.Count != 0)
            {
                envConfigFile.Status = DeploymentStatus.Get("Failed to update EnvironmentConfig", errors);
                result.Failed.Add(envConfigFile);
            }
            else
            {
                if (envConfigCreated)
                {
                    envConfigFile.Status = DeploymentStatus.Get("Created", "environmentConfig", dryRun, severityLevel: SeverityLevel.Success);
                    envConfigFile.Progress = 100f;
                    result.Created.Add(envConfigFile);
                }
                else
                {
                    envConfigFile.Status = DeploymentStatus.Get("Updated", "environmentConfig", dryRun, severityLevel: SeverityLevel.Success);
                    envConfigFile.Progress = 100f;
                    result.Updated.Add(envConfigFile);
                }

                if (!dryRun)
                    result.Authored.Add(envConfigFile);
            }

            return result;
        }

    }
}
