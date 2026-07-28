using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.DeploymentApi.Editor;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.IO;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Parser;
using DeploymentStatus = Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi.DeploymentStatus;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Fetch
{
    class MatchmakerFetchHandler : IMatchmakerFetchHandler
    {
        readonly IConfigApiClient m_configApiClient;
        readonly IFileSystem m_fileSystem;
        readonly IMatchmakerConfigParser m_configParser;
        readonly IDeepEqualityComparer m_deepEqualityComparer;

        public MatchmakerFetchHandler(IConfigApiClient configApiClient,
                                      IMatchmakerConfigParser configParser,
                                      IFileSystem fileSystem,
                                      IDeepEqualityComparer deepEqualityComparer)
        {
            m_configApiClient = configApiClient;
            m_configParser = configParser;
            m_fileSystem = fileSystem;
            m_deepEqualityComparer = deepEqualityComparer;
        }

        public Task<FetchResult> FetchAsync(
            IConfigApiClient client,
            IReadOnlyList<string> filePaths,
            bool reconcile,
            bool dryRun,
            CancellationToken ct = default)
        {
            return FetchAsync(client, null, filePaths, reconcile, dryRun, true, ct);
        }

        public Task<FetchResult> FetchAsync(
            string rootDir,
            IReadOnlyList<string> filePaths,
            bool reconcile,
            bool dryRun,
            CancellationToken ct = default)
        {
            return FetchAsync(m_configApiClient, rootDir, filePaths, reconcile, dryRun, false, ct);
        }


        public async Task<FetchResult> FetchAsync(
            IConfigApiClient client,
            string rootDir,
            IReadOnlyList<string> filePaths,
            bool reconcile,
            bool dryRun,
            bool updateOnly,
            CancellationToken ct = default)
        {
            var result = new FetchResult();

            if (!updateOnly && string.IsNullOrEmpty(rootDir))
            {
                result.AbortMessage = "Root directory is empty";
                return result;
            }

            var parseResult = await m_configParser.Parse(filePaths, ct);

            result.Failed.AddRange(parseResult.failed);
            if (parseResult.parsed.Count == 0 && !reconcile)
                return result;

            var(configExist, remoteConfig) = await client.GetEnvironmentConfig(ct);
            var remoteQueueConfigs = await client.ListQueues(ct);

            if (!configExist)
            {
                result.AbortMessage = "No matchmaker config found for this environment.";
                return result;
            }

            // Updating/Deleting existing files
            foreach (var configFile in parseResult.parsed)
            {
                IMatchmakerConfig remoteConfigFile = null;
                var queueWarnings = new List<ErrorResponse>();
                switch (configFile.Content)
                {
                    case QueueConfig localQueueConfig:
                        (remoteConfigFile, queueWarnings) = remoteQueueConfigs.Find(q => q.Item1.Name.Equals(localQueueConfig.Name));
                        break;
                    case EnvironmentConfig:
                        remoteConfigFile = remoteConfig;
                        break;
                }

                if (remoteConfigFile == null)
                {
                    if (!updateOnly)
                    {
                        if (!dryRun)
                        {
                            try
                            {
                                m_fileSystem.Delete(configFile.Path);
                                result.Authored.Add(configFile);
                            }
                            catch (IOException ex)
                            {
                                var fsException = new FileSystemException(configFile.Path, FileSystemException.Action.Delete, ex.Message);
                                configFile.Status = new DeploymentApi.Editor.DeploymentStatus("Failed to delete file", fsException.ToString(), SeverityLevel.Error);
                                result.Failed.Add(configFile);
                                continue;
                            }
                            catch (SystemException ex)
                            {
                                configFile.Status = new DeploymentApi.Editor.DeploymentStatus("Failed to delete file", $"Could not access {configFile.Path} : {ex.Message}" , SeverityLevel.Error);
                                result.Failed.Add(configFile);
                                continue;
                            }
                        }

                        configFile.Status = DeploymentStatus.Get("deleted", "queue", dryRun, configFile.Name);
                        result.Deleted.Add(configFile);
                    }
                }
                else
                {
                    if (!m_deepEqualityComparer.IsDeepEqual(configFile.Content, remoteConfigFile))
                    {
                        var poolUpdateResult = new PoolUpdateResult();
                        if (remoteConfigFile is QueueConfig queueConfig)
                        {
                            if (configFile.Content is QueueConfig originalQueueConfig)
                            {
                                poolUpdateResult = PoolUpdateResult.GetPoolDeployResultFromQueues(originalQueueConfig, queueConfig, m_deepEqualityComparer, dryRun);
                            }

                            if (queueWarnings.Count > 0)
                            {
                                configFile.Status = DeploymentStatus.Get($"Updated queue {queueConfig.Name}" , queueWarnings,  SeverityLevel.Warning, poolUpdateResult);
                            }
                            else
                            {
                                configFile.Status = DeploymentStatus.Get("Updated", "queue", dryRun, queueConfig.Name.ToString(), SeverityLevel.Info, poolUpdateResult);
                            }
                        }
                        else
                        {
                            configFile.Status = DeploymentStatus.Get("Updated", "environmentConfig", dryRun);
                        }

                        configFile.Content = remoteConfigFile;
                        result.Updated.Add(configFile);

                        if (!dryRun)
                        {
                            var(authored, error) =
                                await m_configParser.SerializeToFile(configFile.Content, configFile.Path, ct);
                            if (!string.IsNullOrEmpty(error))
                            {
                                configFile.Status = new DeploymentApi.Editor.DeploymentStatus(
                                    $"Failed to write in file {configFile.Path}", error, SeverityLevel.Error);
                                result.Failed.Add(configFile);
                            }
                            else if (authored)
                                result.Authored.Add(configFile);
                        }
                    }
                }
            }

            if (!reconcile || updateOnly)
                return result;

            MatchmakerConfigResource newConfigFile;
            foreach (var(remoteQueueConfigFile, queueConfigWarnings) in remoteQueueConfigs)
            {
                var localQueueConfigFile = parseResult.parsed.Where(
                    localConfigFile => localConfigFile.Content is QueueConfig localQueueConfig &&
                    localQueueConfig.Name.Equals(remoteQueueConfigFile.Name));

                // Create missing queue config files
                if (!localQueueConfigFile.Any())
                {
                    newConfigFile = new MatchmakerConfigResource
                    {
                        Name = remoteQueueConfigFile.Name.ToString(),
                        Path = Path.Combine(rootDir, $"{remoteQueueConfigFile.Name}{IMatchmakerConfigParser.QueueConfigExtension}"),
                        Content = remoteQueueConfigFile
                    };
                    if (!dryRun)
                    {
                        var(authored, error) =
                            await m_configParser.SerializeToFile(remoteQueueConfigFile, newConfigFile.Path, ct);
                        if (!string.IsNullOrEmpty(error))
                        {
                            newConfigFile.Status = new DeploymentApi.Editor.DeploymentStatus($"Failed to create file {newConfigFile.Path}", error, SeverityLevel.Error);
                            result.Failed.Add(newConfigFile);
                            continue;
                        }
                        if (authored)
                            result.Authored.Add(newConfigFile);
                    }

                    if (newConfigFile.Content is QueueConfig queueConfig)
                    {
                        if (queueConfigWarnings.Count > 0)
                        {
                            newConfigFile.Status = DeploymentStatus.Get($"Created queue {queueConfig.Name}", queueConfigWarnings, SeverityLevel.Warning);
                        }
                        else
                        {
                            newConfigFile.Status = DeploymentStatus.Get($"Created", "queue", dryRun,  queueConfig.Name.ToString());
                        }
                    }

                    result.Created.Add(newConfigFile);
                }
            }

            // Creating new env config file if none exists
            var localEnvConfigFile = parseResult.parsed.Find(localConfigFile => localConfigFile.Content is EnvironmentConfig);
            if (localEnvConfigFile != null)
                return result;

            newConfigFile = new MatchmakerConfigResource
            {
                Name = "EnvironmentConfig",
                Path = Path.Combine(rootDir, $"EnvironmentConfig{IMatchmakerConfigParser.EnvironmentConfigExtension}"),
                Content = remoteConfig
            };
            if (!dryRun)
            {
                var(authored, error) =
                    await m_configParser.SerializeToFile(remoteConfig, newConfigFile.Path, ct);
                if (!string.IsNullOrEmpty(error))
                {
                    newConfigFile.Status = new DeploymentApi.Editor.DeploymentStatus($"Failed to create file {newConfigFile.Path}", error, SeverityLevel.Error);
                    result.Failed.Add(newConfigFile);
                    return result;
                }
                if (authored)
                    result.Authored.Add(newConfigFile);
            }

            newConfigFile.Status =  DeploymentStatus.Get($"Created", "environmentConfig", dryRun);
            result.Created.Add(newConfigFile);
            return result;
        }
    }
}
