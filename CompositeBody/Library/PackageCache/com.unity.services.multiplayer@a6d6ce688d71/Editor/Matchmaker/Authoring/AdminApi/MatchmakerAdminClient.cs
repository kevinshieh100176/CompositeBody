using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Matchmaker.Api;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi;
using UnityEngine;
using IAccessTokens = Unity.Services.Core.Editor.IAccessTokens;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Shared;
using Unity.Services.Multiplayer.Editor.Shared.Clients;
using CoreModel = Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;
using GeneratedModel = Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi.Matchmaker.Model;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi
{
    class MatchmakerAdminClient : IConfigApiClient
    {
        readonly IMatchmakerAdminApi m_Client;
        readonly IAccessTokens m_Tokens;
        readonly CoreModel.MultiplayResources m_MultiplayResources;
        string m_EnvironmentId;
        string m_ProjectId;

        public MatchmakerAdminClient(
            IMatchmakerAdminApi mClient,
            IAccessTokens tokenProvider)
        {
            m_Client = mClient;
            m_Tokens = tokenProvider;
        }

        public async Task Initialize(
            string projectId,
            string environmentId,
            CancellationToken ct = default)
        {
            m_EnvironmentId = environmentId;
            m_ProjectId = projectId;
            await UpdateToken();
        }

        public async Task UpdateToken()
        {
            string token = await m_Tokens.GetServicesGatewayTokenAsync();
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("No token exists, Editor is likely logged out");

            var adminApiHeaders = new AdminApiHeaders<MatchmakerAdminClient>(token);

            var headers = adminApiHeaders.ToDictionary();
            if (m_Client.Configuration.DefaultHeaders == null)
            {
                m_Client.Configuration.DefaultHeaders = headers;
            }
            else
            {
                foreach (var header in headers)
                    m_Client.Configuration.DefaultHeaders[header.Key] = header.Value;
            }
        }

        public async Task<(bool, CoreModel.EnvironmentConfig)> GetEnvironmentConfig(CancellationToken ct = default)
        {
            var response = await m_Client.GetEnvironmentConfig(
                m_ProjectId,
                m_EnvironmentId,
                ct);

            if (!response.IsSuccessful)
            {
                if (response.StatusCode == (int)HttpStatusCode.NotFound)
                {
                    return (false, new CoreModel.EnvironmentConfig());
                }
                EnsureSuccessful(response);
            }

            return (true, new CoreModel.EnvironmentConfig
            {
                DefaultQueueName = new CoreModel.QueueName(response.Data.DefaultQueueName ?? ""),
                Enabled = response.Data.Enabled
            });
        }

        public async Task<List<CoreModel.ErrorResponse>> UpsertEnvironmentConfig(
            CoreModel.EnvironmentConfig environmentConfig,
            bool dryRun,
            CancellationToken ct = default)
        {
            var response = await m_Client.UpdateEnvironmentConfig(
                m_ProjectId,
                m_EnvironmentId,
                dryRun,
                new Matchmaker.Model.EnvironmentConfig(environmentConfig.Enabled, environmentConfig.DefaultQueueName.ToString()),
                ct);

            var upsertEnvironmentConfig = GetOrThrowErrorResponse(response);
            if (upsertEnvironmentConfig != null)
            {
                return upsertEnvironmentConfig;
            }

            return new List<CoreModel.ErrorResponse>();
        }

        public async Task<List<(CoreModel.QueueConfig, List<CoreModel.ErrorResponse>)>> ListQueues(
            CancellationToken ct = default)
        {
            var response = await m_Client.ListQueues(
                m_ProjectId,
                m_EnvironmentId,
                ct);

            if (!response.IsSuccessful)
            {
                if (response.StatusCode == (int)HttpStatusCode.NotFound)
                {
                    return new List<(CoreModel.QueueConfig, List<CoreModel.ErrorResponse>)>();
                }
                EnsureSuccessful(response);
            }

            var result = new List<(CoreModel.QueueConfig, List<CoreModel.ErrorResponse>)>(response.Data.Count);
            foreach (var queueConfig in response.Data)
            {
                var coreQueue = ModelGeneratedToCore.FromGeneratedQueueConfig(queueConfig, m_MultiplayResources);
                result.Add(coreQueue);
            }

            return result;
        }

        public async Task<List<CoreModel.ErrorResponse>> UpsertQueue(
            CoreModel.QueueConfig queueConfig,
            CoreModel.MultiplayResources availableMultiplayResources,
            bool dryRun,
            CancellationToken ct = default)
        {
            var(genQueue, errors) = ModelCoreToGenerated.FromCoreQueueConfig(
                queueConfig,
                availableMultiplayResources,
                dryRun);

            if (errors.Count > 0)
                return errors;

            var response = await m_Client.UpsertQueueConfig(
                m_ProjectId,
                m_EnvironmentId,
                queueConfig.Name.ToString(),
                dryRun,
                genQueue,
                ct);

            var upsertQueueResponse = GetOrThrowErrorResponse(response);
            if (upsertQueueResponse != null)
            {
                return upsertQueueResponse;
            }

            return new List<CoreModel.ErrorResponse>();
        }

        public async Task DeleteQueue(
            CoreModel.QueueName queueName,
            bool dryRun,
            CancellationToken ct = default)
        {
            var response = await m_Client.DeleteQueue(
                m_ProjectId,
                m_EnvironmentId,
                queueName.ToString(),
                dryRun,
                ct);
            EnsureSuccessful(response);
        }

        public CoreModel.MultiplayResources GetRemoteMultiplayResources()
        {
            return m_MultiplayResources;
        }

        static List<CoreModel.ErrorResponse> GetOrThrowErrorResponse(ApiResponse response)
        {
            if (response.StatusCode == (int)HttpStatusCode.BadRequest)
            {
                var problem = JsonConvert.DeserializeObject<GeneratedModel.ProblemDetails>(response.Content, new JsonSerializerSettings()
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore
                });

                if (problem == null)
                {
                    return new List<CoreModel.ErrorResponse>
                    {
                        new()
                        {
                            Message = $"{response.ErrorText}: {response.Content}",
                            ResultCode = "-1"
                        }
                    };
                }

                return problem.Details.Select(
                    d =>
                        new CoreModel.ErrorResponse { ResultCode = d.ResultCode, Message = d.Message })
                    .ToList();
            }

            EnsureSuccessful(response);
            return null;
        }

        // This class allows us to add the JsonObjectSpecializedConverter to the Core JsonObject class
        [JsonConverter(typeof(JsonObjectSpecializedConverter))]
        public class JsonObjectSpecialized : CoreModel.JsonObject
        {
            public JsonObjectSpecialized(string value) : base(value)
            {
            }
        }

        static void EnsureSuccessful(ApiResponse response)
        {
            if (!response.IsSuccessful)
            {
                throw new MatchmakerClientException(response.ErrorText)
                {
                    StatusCode = response.StatusCode, ErrorType = response.ErrorType.ToString()
                };
            }
        }
    }
}
