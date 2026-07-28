using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi
{
    interface IConfigApiClient
    {
        Task UpdateToken();
        Task Initialize(string projectId, string environmentId, CancellationToken ct = default);
        Task<(bool, EnvironmentConfig)> GetEnvironmentConfig(CancellationToken ct = default);
        Task<List<ErrorResponse>> UpsertEnvironmentConfig(EnvironmentConfig environmentConfig, bool dryRun, CancellationToken ct = default);
        Task<List<(QueueConfig, List<ErrorResponse>)>> ListQueues(CancellationToken ct = default);
        Task<List<ErrorResponse>> UpsertQueue(QueueConfig queueConfig, MultiplayResources availableMultiplayResources, bool dryRun, CancellationToken ct = default);
        Task DeleteQueue(QueueName queueName, bool dryRun, CancellationToken ct = default);
        MultiplayResources GetRemoteMultiplayResources();
    }
}
