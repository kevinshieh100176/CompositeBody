using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Services.Core.Editor;
using Unity.Services.Vivox.Serialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Unity.Services.Vivox.Editor
{
    class VivoxApiClient
    {
        static VivoxApiClient s_Instance;

        internal bool IsFetchingCreds;
        internal bool IsEditorFirstPass = true;

        internal static VivoxApiClient Instance
        {
            get
            {
                if (s_Instance is null)
                {
                    s_Instance = new VivoxApiClient();
                }

                return s_Instance;
            }
        }

        class VivoxApiConfig
        {
            public string credentials { get; set; } = $"https://services.unity.com/api/vivox/v1/projects";


            public string services { get; set; } = "https://services.unity.com/api/auth/v1/genesis-token-exchange/unity";
        }

        CdnConfiguredEndpoint<VivoxApiConfig> m_ClientConfig;

        public VivoxApiClient()
        {
            IsFetchingCreds = false;
            m_ClientConfig = new CdnConfiguredEndpoint<VivoxApiConfig>();
#if ENABLE_EDITOR_GAME_SERVICES
            CloudProjectSettingsEventManager.instance.projectStateChanged += OnProjectBindChanged;
#endif
        }

        ~VivoxApiClient()
        {
#if ENABLE_EDITOR_GAME_SERVICES
            CloudProjectSettingsEventManager.instance.projectStateChanged -= OnProjectBindChanged;
#endif
        }

#if ENABLE_EDITOR_GAME_SERVICES
        private void OnProjectBindChanged()
        {
            GetAndSetVivoxCredentials(null, Debug.LogError);
        }

#endif

        /// <summary>
        /// Central place to get the gateway token for the current project and then fetch your projects Vivox Credentials.
        /// Credentials will then be saved in <see cref="VivoxSettings"/> automatically.  Any exceptions will be logged in this method as well.
        /// </summary>
        /// <param name="onCredentialsFetched">Optional: onCredentialsFetched action that can be used to action successful credentials being set</param>
        /// <param name="onException">Optional: onException when the fetch fails.  No logs will be generate by this method so the calling code must handle error logs for the Editor UI.</param>
        internal void GetAndSetVivoxCredentials(Action<GetVivoxCredentialsResponse> onCredentialsFetched = null, Action<Exception> onException = null)
        {
            if (IsEditorFirstPass)
            {
                IsEditorFirstPass = false;
                // For some reason the first editor domain reload always has an CloudProjectSettings.projectId.
                // If we detect that, ignore it. CloudProjectSettings.projectId will be populate in the next domain reload if a project is linked.
                if (String.IsNullOrEmpty(CloudProjectSettings.projectId))
                {
                    return;
                }
            }

            // If CloudProjectSettings.projectId is empty at this point, it's almost certainly because there isn't a cloud project linked to the current Unity project.
            // In this case, let's wipe the credentials.
            if (String.IsNullOrEmpty(CloudProjectSettings.projectId))
            {
                VivoxSettings.Instance.TokenKey = string.Empty;
                VivoxSettings.Instance.TokenIssuer = string.Empty;
                VivoxSettings.Instance.Server = string.Empty;
                VivoxSettings.Instance.Domain = string.Empty;
                VivoxSettings.Instance.Save();

                return;
            }

            if (IsFetchingCreds)
            {
                return;
            }

            IsFetchingCreds = true;
            Instance.GetGatewayToken(OnGatewayTokenFetched, Debug.LogError);

            void OnGatewayTokenFetched(TokenExchangeResponse gateResp)
            {
                Instance.GetCredentials(gateResp.Token, OnCredentialsFetched, HandleException);

                void OnCredentialsFetched(GetVivoxCredentialsResponse credResp)
                {
                    // If we get an empty credential response for some reason, don't overwrite the existing creds.
                    // They should only be cleared if the project linkage has changed.
                    var receivedEmptyCredentialResponse = string.IsNullOrEmpty(credResp.Credentials.Issuer) || string.IsNullOrEmpty(credResp.Credentials.Environment.ServerUri) || string.IsNullOrEmpty(credResp.Credentials.Environment.Domain);
                    if (!receivedEmptyCredentialResponse)
                    {
                        VivoxSettings.Instance.TokenKey = VivoxSettings.Instance.IsTestMode && !string.IsNullOrEmpty(credResp.Credentials.Key) ? credResp.Credentials.Key : string.Empty;
                        VivoxSettings.Instance.TokenIssuer = credResp.Credentials.Issuer;
                        VivoxSettings.Instance.Server = credResp.Credentials.Environment.ServerUri;
                        VivoxSettings.Instance.Domain = credResp.Credentials.Environment.Domain;
                        VivoxSettings.Instance.Save();
                    }
                    EditorGameServiceAnalyticsSender.SendCredentialPullEvent();
                    IsFetchingCreds = false;
                    onCredentialsFetched?.Invoke(credResp);
                }

                void HandleException(Exception exception)
                {
                    if (exception.Message == "Object reference not set to an instance of an object")
                    {
                        var localException = new Exception(
                            "[Vivox]: Failed to pull Credentials from the Unity Dashboard. Ensure you are online and the associated project has Vivox enabled with Vivox Credentials generated on the Unity Dashboard.");
                        onException?.Invoke(localException);
                        return;
                    }
                    IsFetchingCreds = false;
                    onException?.Invoke(exception);
                }
            }
        }

        internal void GetCredentials(string token, Action<GetVivoxCredentialsResponse> onSuccess, Action<Exception> onError)
        {
            CreateJsonGetRequest(GetEndPointUrl, onSuccess, onError, token);

            string GetEndPointUrl(VivoxApiConfig config)
            {
                return $"{config.credentials}/{CloudProjectSettings.projectId}/credentials";
            }
        }

        internal void GetGatewayToken(Action<TokenExchangeResponse> onSuccess, Action<Exception> onError)
        {
            var request = new TokenExchangeRequest();
            request.Token = CloudProjectSettings.accessToken;
            CreateJsonPostRequest(GetEndPointUrl, request, onSuccess, onError, CloudProjectSettings.accessToken);

            string GetEndPointUrl(VivoxApiConfig config)
            {
                return $"{config.services}";
            }
        }

        void CreateJsonPostRequest<TRequestType, TResponseType>(
            Func<VivoxApiConfig, string> endpointConstructor, TRequestType request,
            Action<TResponseType> onSuccess, Action<Exception> onError, string token)
        {
            m_ClientConfig.GetConfiguration(OnGetConfigurationCompleted);

            void OnGetConfigurationCompleted(VivoxApiConfig configuration)
            {
                try
                {
                    var url = endpointConstructor(configuration);
                    var payload = IsolatedJsonConvert.SerializeObject(request);
                    var uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                    var postRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
                    {
                        downloadHandler = new DownloadHandlerBuffer(),
                        uploadHandler = uploadHandler
                    };
                    postRequest.SetRequestHeader("Content-Type", "application/json;charset=utf-8");
                    Authorize(postRequest, token);
                    postRequest.SendWebRequest().completed += CreateJsonResponseHandler(onSuccess, onError);
                }
                catch (Exception reason)
                {
                    IsFetchingCreds = false;
                    onError?.Invoke(reason);
                }
            }
        }

        void CreateJsonGetRequest<T>(
            Func<VivoxApiConfig, string> endpointConstructor, Action<T> onSuccess, Action<Exception> onError, string token)
        {
            m_ClientConfig.GetConfiguration(OnGetConfigurationCompleted);

            void OnGetConfigurationCompleted(VivoxApiConfig configuration)
            {
                try
                {
                    var url = endpointConstructor(configuration);
                    var getRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET)
                    {
                        downloadHandler = new DownloadHandlerBuffer()
                    };
                    Authorize(getRequest, token);
                    getRequest.SendWebRequest().completed += CreateJsonResponseHandler(onSuccess, onError);
                }
                catch (Exception reason)
                {
                    IsFetchingCreds = false;
                    onError?.Invoke(reason);
                }
            }
        }

        static Action<AsyncOperation> CreateJsonResponseHandler<T>(Action<T> onSuccess, Action<Exception> onError)
        {
            return JsonResponseHandler;

            void JsonResponseHandler(AsyncOperation unityOperation)
            {
                var callbackWebRequest = ((UnityWebRequestAsyncOperation)unityOperation).webRequest;
                if (WebRequestSucceeded(callbackWebRequest))
                {
                    try
                    {
                        var deserializedObject = IsolatedJsonConvert.DeserializeObject<T>(
                            callbackWebRequest.downloadHandler.text);
                        onSuccess?.Invoke(deserializedObject);
                    }
                    catch (Exception deserializeError)
                    {
                        onError?.Invoke(deserializeError);
                    }
                }
                else
                {
                    onError?.Invoke(new Exception(callbackWebRequest.error));
                }

                callbackWebRequest.Dispose();
            }
        }

        static bool WebRequestSucceeded(UnityWebRequest request)
        {
#if UNITY_2020_2_OR_NEWER
            return request.result == UnityWebRequest.Result.Success;
#else
            return request.isDone && !request.isHttpError && !request.isNetworkError;
#endif
        }

        static void Authorize(UnityWebRequest request, string token)
        {
            request.SetRequestHeader("AUTHORIZATION", $"Bearer {token}");
        }
    }
}
