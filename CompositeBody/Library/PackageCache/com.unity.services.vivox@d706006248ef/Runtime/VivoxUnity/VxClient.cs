using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

#if (UNITY_ANDROID && !UNITY_EDITOR) || __ANDROID__
using UnityEngine;
#endif

namespace Unity.Services.Vivox
{
    internal class VxClient : IDisposable
    {
#if ((UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR) || __IOS__
        [DllImport("__Internal")]
        private static extern void PrepareForVivox();
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void vx_initialize();
        [DllImport("__Internal")]
        private static extern void vx_uninitialize();
#endif
        private static VxClient _instance;
        private readonly Dictionary<string, AsyncResult<vx_resp_base_t>> _pendingRequests = new Dictionary<string, AsyncResult<vx_resp_base_t>>();
        private long _nextRequestId = 1;
        private int _startCount = 0;
        /// <summary>
        /// A three letter appId that should be set to UNI. Caution: Do not set this value. Contact your Vivox representative for more information.
        /// </summary>
        public const string appId = "UNI"; // Caution: Do not change this value.
        public string defaultRealm;
        public bool IsQuitting;

        internal VxTokenGen tokenGen = new VxTokenGen();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetClientNoDomainReload()
        {
            VxClient.Instance.IsQuitting = false;
        }

        internal static bool PlatformNotSupported
        {
            get
            {
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX

                VivoxLogger.LogError("Failed to make your request, this is not a supported platform.");
                return true;
#else
                return false;
#endif
            }
        }
        private VxClient() {}

        public static VxClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new VxClient();
                }
                return _instance;
            }
        }

        public bool Started { get { return _startCount > 0; } }

        public delegate void HandleEventMessage(vx_evt_base_t eventMessage);
        public event HandleEventMessage EventMessageReceived;

        /// <summary>
        /// This method is used to start VxClient.
        /// Most implementations should use <see cref="Client"/> instead of <see cref="VxClient"/>
        /// </summary>
        /// <param name="config">Optional: config to set on initialize.</param>
        public void Start(VivoxConfigurationOptions config = null)
        {
#if ((UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR) || __IOS__
            if (config != null && !config.SkipPrepareForVivox)
            {
                PrepareForVivox();
            }
#endif
            InternalStart(config?.InternalVivoxConfig);
        }

        /// <summary>
        /// Note: It is recommended that you use Start(VivoxConfig config) instead of this method.
        /// This method is used to start VxClient.
        /// Most implementation should use <see cref="Client"/> instead of <see cref="VxClient"/>
        /// </summary>
        /// <param name="config">Config to set on initialize.</param>
        public void Start(vx_sdk_config_t config)
        {
            // For this start method, we will force the PrepareForVivox as
            // we plan to deprecate this method in the future.
#if ((UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR) || __IOS__
            PrepareForVivox();
#endif
            InternalStart(config);
        }

        private void InternalStart(vx_sdk_config_t config = null)
        {
            if (PlatformNotSupported) return;

            if (_startCount > 0)
            {
                ++_startCount;
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            vx_initialize();
            ++_startCount;
            return;
#else
            /// Initialize the VivoxNative module before returning the Client object.
#if (UNITY_ANDROID && !UNITY_EDITOR) || __ANDROID__
            // Initialize the VivoxNative module
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            AndroidJavaObject appContext = activity.Call<AndroidJavaObject>("getApplicationContext");
            AndroidJavaClass pluginClass = new AndroidJavaClass("com.vivox.vivoxnative.VivoxNative");
            pluginClass.CallStatic("init", appContext);
#endif

            config = config == null ? new vx_sdk_config_t() : config;
            int status = VivoxCoreInstance.Initialize(config);
            if (status != 0)
                throw new VivoxApiException(status);

            MessagePump.Instance.MainLoopRun += InstanceOnMainLoopRun;
            ++_startCount;

            //Start the Unity interop class that will set up the needed message pump.
#if UNITY_5_3_OR_NEWER
            // We only want to start the message pump if we are actually in the context of a running application, whether that is in the editor or in a build.
#if UNITY_EDITOR
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                VxUnityInterop.Instance.StartVivoxUnity();
            }
#else
            VxUnityInterop.Instance.StartVivoxUnity();
#endif
#endif
#endif
        }

        private void InstanceOnMainLoopRun(ref bool didWork)
        {
            if (!Started)
                return;
            for (;;)
            {
                VivoxCoreInstance.send_all_logs();
                vx_message_base_t m = Helper.NextMessage();

                if (m == null)
                    break;
                didWork = true;
                if (m.type == vx_message_type.msg_event)
                {
                    EventMessageReceived?.Invoke((vx_evt_base_t)m);
                }
                else if (m.type == vx_message_type.msg_response)
                {
                    var r = (vx_resp_base_t)m;
                    string key = r.request.cookie;
                    AsyncResult<vx_resp_base_t> result = null;
                    lock (_pendingRequests)
                    {
                        if (_pendingRequests.ContainsKey(key))
                        {
                            result = _pendingRequests[key];
                            _pendingRequests.Remove(key);
                        }
                    }
                    result?.SetComplete(r);
                }
            }
        }

        public void Stop()
        {
            if (_startCount <= 0)
                return;
            --_startCount;
            if (_startCount != 0)
                return;

#if UNITY_WEBGL && !UNITY_EDITOR
            vx_uninitialize();
            ++_startCount;
            return;
#else

            MessagePump.Instance.MainLoopRun -= InstanceOnMainLoopRun;
#endif
        }

        public void Cleanup()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            vx_uninitialize();
            ++_startCount;
#else
            VivoxCoreInstance.Uninitialize();
            tokenGen = new VxTokenGen();
            lock (_pendingRequests)
            {
                _pendingRequests.Clear();
            }
#endif
            _startCount = 0;
        }

        public IAsyncResult BeginIssueRequest(vx_req_base_t request, AsyncCallback callback)
        {
            if (PlatformNotSupported) return null;
            if (request == null)
                throw new ArgumentNullException();
            if (!Started)
                throw new InvalidOperationException();
            string requestId = $"{_nextRequestId++}";
            request.cookie = requestId;
            var result = new AsyncResult<vx_resp_base_t>(callback) { AsyncState = requestId };
            lock (_pendingRequests)
            {
                _pendingRequests[requestId] = result;
            }
            var status = VivoxCoreInstance.IssueRequest(request);
            if (status != 0)
            {
                lock (_pendingRequests)
                {
                    _pendingRequests.Remove(requestId);
                }
                throw new VivoxApiException(status);
            }
            return result;
        }

        public vx_resp_base_t EndIssueRequest(IAsyncResult result)
        {
            if (result == null)
                throw new ArgumentNullException();
            if (!result.IsCompleted)
                throw new InvalidOperationException();
            var tresult = result as AsyncResult<vx_resp_base_t>;
            if (tresult == null)
                throw new InvalidCastException();
            if (tresult.Result.return_code == 1)
            {
                throw new VivoxApiException(tresult.Result.status_code, tresult.Result.request.cookie);
            }
            return tresult.Result;
        }

        #region IDisposable Support

        bool disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                // Free any other managed objects here.
                //
            }

            // Free any unmanaged objects here.
            VivoxCoreInstance.Uninitialize();

            disposed = true;
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        ~VxClient()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }

        #endregion
        public static string GetRandomUserId(string prefix)
        {
            return Helper.GetRandomUserId(prefix);
        }

        public static string GetRandomUserIdEx(string prefix, string issuer)
        {
            return Helper.GetRandomUserIdEx(prefix, issuer);
        }

        public static string GetRandomChannelUri(string prefix, string realm)
        {
            return Helper.GetRandomChannelUri(prefix, realm);
        }

        public static string GetVersion()
        {
            return VivoxCoreInstance.vx_get_sdk_version_info();
        }
    }
}
