using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Services.Matchmaker.Http;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.Matchmaker.Models;
using Logger = Unity.Services.Multiplayer.Logger;

namespace Unity.Services.Matchmaker.Overrides
{
    internal class ABRemoteConfig : IABRemoteConfig
    {
        const string StagingEnvironment = "staging";
        const string ConfigType = "matchmaker";
        const string StagingURL = "https://remote-config-stg.uca.cloud.unity3d.com/api/v1/settings";
        const string ProdURL = "https://config.unity3d.com/api/v1/settings";
        private string _projectId;
        private string _environmentId;

        private readonly IHttpClient _client;
        private readonly string _userId;
        private readonly string _url;
        private bool _needRefresh = true;

        public List<Override> Overrides { get; private set; }

        public string AssignmentId { get; private set; }

        public ABRemoteConfig(IHttpClient client, string installationId, string cloudEnvironment, string projectId, string environmentId)
        {
            Overrides = new List<Override>();

            _client = client;
            _userId = installationId;
            _projectId = projectId;
            _environmentId = environmentId;
            _url = cloudEnvironment == StagingEnvironment ? StagingURL : ProdURL;
        }

        public async Task RefreshGameOverridesAsync()
        {
            if (!_needRefresh)
            {
                return;
            }

            var requestData = new
            {
                projectId = _projectId,
                environmentId = _environmentId,
                userId = _userId,
                configType = ConfigType,
                attributes = new Dictionary<string, Dictionary<string, string>>
                {
                    ["unity"] = new Dictionary<string, string>
                    {
                        { "platform", Application.platform.ToString() }
                    },

                    ["app"] = new Dictionary<string, string>(),
                    ["user"] = new Dictionary<string, string>(),
                }
            };

            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestData));
            var headers = new Dictionary<string, string>();

            var response = await _client.MakeRequestAsync("POST", _url, body, headers, 10);
            if (response.IsHttpError || response.IsNetworkError)
            {
                Logger.LogError($"Error while fetching overrides: code={response.StatusCode}, message='{response.ErrorMessage}'");
                return;
            }

            var responseBodyStr = Encoding.UTF8.GetString(response.Data);
            var responseBody = JObject.Parse(responseBodyStr);
            if (responseBody["configs"] ? [ConfigType]?.Type != JTokenType.Object)
            {
                Logger.LogError($"Error while fetching overrides: '{ConfigType}' missing in {responseBodyStr}");
                return;
            }

            if (responseBody["metadata"] ? ["assignmentId"]?.Type != JTokenType.String)
            {
                Logger.LogError($"Error while fetching overrides: 'assignmentId' missing in {responseBodyStr}");
                return;
            }
            AssignmentId = responseBody["metadata"] ? ["assignmentId"].Value<string>();

            foreach (var jToken in responseBody["configs"][ConfigType])
            {
                var overrideInfo = (JProperty)jToken;
                if (overrideInfo == null) continue;
                var variantId = overrideInfo.Value["variantId"];
                if (variantId == null) continue;
                var overrideId = overrideInfo.Value["overrideId"];
                if (overrideId == null) continue;

                Overrides.Add(new Override(
                    overrideInfo.Name,
                    variantId.Value<string>(),
                    overrideId.Value<string>()));
            }

            _needRefresh = false;
        }
    }
}
