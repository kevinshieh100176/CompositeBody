using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// Provide access to the Vivox system.
    /// Note: An application should have only one Client object.
    /// </summary>
    internal sealed class Client : IDisposable
    {
        private static readonly Lazy<Client> s_instance = new Lazy<Client>(() => new Client());

        public static Client Instance => s_instance.Value;
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern bool vx_is_initialized();
#endif
        #region Member Variables
#if UNITY_WEBGL && !UNITY_EDITOR
        private readonly ReadWriteDictionary<AccountId, ILoginSession, LoginSessionWeb> _loginSessions = new ReadWriteDictionary<AccountId, ILoginSession, LoginSessionWeb>();
        private readonly AudioInputDevicesWeb _inputDevices = new AudioInputDevicesWeb(VxClient.Instance);
        private readonly AudioOutputDevicesWeb _outputDevices = new AudioOutputDevicesWeb(VxClient.Instance);
#else
        private readonly ReadWriteDictionary<AccountId, ILoginSession, LoginSession> _loginSessions = new ReadWriteDictionary<AccountId, ILoginSession, LoginSession>();
        private readonly AudioInputDevices _inputDevices = new AudioInputDevices(VxClient.Instance);
        private readonly AudioOutputDevices _outputDevices = new AudioOutputDevices(VxClient.Instance);
#endif

        private Uri _serverUri;
        private static int _nextHandle;
        private string _connectorHandle;
        private readonly Queue<IAsyncResult> _pendingConnectorCreateRequests = new Queue<IAsyncResult>();
        private bool _ttsIsInitialized;

        private uint _ttsManagerId;
        internal uint TTSManagerId => _ttsManagerId;
        internal static readonly int requestTimeout = 30000;
        internal static readonly int historyQueryRequestTimeout = 60000;

        public static VxTokenGen tokenGen
        {
            get { return VxClient.Instance.tokenGen; }
            set { VxClient.Instance.tokenGen = value; }
        }

        /// <summary>
        /// The domain that the server determines during client connector creation. Otherwise, this is NULL.
        /// </summary>
        public static string defaultRealm
        {
            get { return VxClient.Instance.defaultRealm; }
            private set { VxClient.Instance.defaultRealm = value; }
        }
        #endregion

        #region Helpers

        void CheckInitialized()
        {
            if (!Initialized || VxClient.PlatformNotSupported)
                return;
        }

        #endregion

        /// <summary>
        /// Initialize this Client instance.
        /// Note: If this Client instance is already initialized, then this does nothing.
        /// <param name="config">Optional: config to set on initialize.</param>
        /// </summary>
        public void Initialize(VivoxConfigurationOptions config = null)
        {
            if (Initialized || VxClient.PlatformNotSupported)
                return;


            VxClient.Instance.Start(config);

            // Refresh audio devices to ensure they are up-to-date when the client is initialized.
            AudioInputDevices.BeginRefresh(null);
            AudioOutputDevices.BeginRefresh(null);
        }

        /// <summary>
        /// Initialize this Client instance.
        /// Note: If this Client instance is already initialized, then this does nothing.
        /// <param name="config">Optional: config to set on initialize.</param>
        /// </summary>
        public async Task InitializeAsync(string serverUri, VivoxConfigurationOptions config = null)
        {
            if (Initialized || VxClient.PlatformNotSupported)
                return;

            _serverUri = new Uri(serverUri);

            VxClient.Instance.Start(config);

            // Refresh audio devices to ensure they are up-to-date when the client is initialized.
            await AudioInputDevices.RefreshDevicesAsync(null);
            await AudioOutputDevices.RefreshDevicesAsync(null);
        }

        internal IAsyncResult BeginGetConnectorHandle(AsyncCallback callback)
        {
            if (_serverUri == null)
            {
                throw new NullReferenceException($"{nameof(_serverUri)} is Null. If not using Unity Game Services, use BeginGetConnectorHandle(Uri server, AsyncCallback callback)");
            }

            return BeginGetConnectorHandle(_serverUri, callback);
        }

        internal IAsyncResult BeginGetConnectorHandle(Uri server, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            CheckInitialized();

            var result = new AsyncResult<string>(callback);
            if (!string.IsNullOrEmpty(_connectorHandle))
            {
                result.SetCompletedSynchronously(_connectorHandle);
                return result;
            }

            _pendingConnectorCreateRequests.Enqueue(result);
            if (_pendingConnectorCreateRequests.Count > 1)
            {
                return result;
            }

            var request = new vx_req_connector_create_t();
            var response = new vx_resp_connector_create_t();
            request.acct_mgmt_server = server.ToString();
            string connectorHandle = $"C{_nextHandle++}";
            request.connector_handle = connectorHandle;
            VxClient.Instance.BeginIssueRequest(request, ar =>
            {
                try
                {
                    response = VxClient.Instance.EndIssueRequest(ar);
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    _connectorHandle = null;
                    while (_pendingConnectorCreateRequests.Count > 0)
                    {
                        ((AsyncResult<string>)(_pendingConnectorCreateRequests).Dequeue()).SetComplete(e);
                    }

                    throw;
                }
                _connectorHandle = connectorHandle;
                if (!string.IsNullOrEmpty(response.default_realm))
                {
                    defaultRealm = response.default_realm;
                }
                while (_pendingConnectorCreateRequests.Count > 0)
                {
                    ((AsyncResult<string>)(_pendingConnectorCreateRequests).Dequeue()).SetComplete(_connectorHandle);
                }
            });
            return result;
        }

        internal string EndGetConnectorHandle(IAsyncResult result)
        {
            return ((AsyncResult<string>)result).Result;
        }

        internal void RemoveLoginSession(AccountId accountId)
        {
            _loginSessions.Remove(accountId);
        }

        internal void AddLoginSession(AccountId accountId, LoginSession session)
        {
            if (!_loginSessions.ContainsKey(accountId))
            {
                _loginSessions[accountId] = session;
            }
        }

        /// <summary>
        /// Uninitialize this Client instance.
        /// Note: If this Client instance is not initialized, then this does nothing.
        /// </summary>
        public void Uninitialize()
        {
            if (Initialized && !VxClient.PlatformNotSupported)
            {
                VxClient.Instance.Stop();
                VxClient.Instance.Cleanup();
                _loginSessions.Clear();
                _connectorHandle = null;
#if !UNITY_WEBGL
                TTSShutdown();
                _inputDevices.Clear();
                _outputDevices.Clear();
#endif
            }
        }

        /// <summary>
        /// The internal version of the low-level vivoxsdk library.
        /// </summary>
        public static string InternalVersion => VxClient.GetVersion();


        public ILoginSession GetLoginSession(AccountId accountId)
        {
            // Call the core method, indicating the service instance is not applicable/optional.
            return GetOrCreateLoginSession(accountId, null, isServiceInstanceOptional: true);
        }

        public ILoginSession GetLoginSession(AccountId accountId, IVivoxServiceInternal vivoxServiceInstance)
        {
            return GetOrCreateLoginSession(accountId, vivoxServiceInstance, isServiceInstanceOptional: false);
        }

        /// <summary>
        /// The single source of truth for getting or creating login sessions.
        /// This handles all checks, caching, and platform-specific creation logic.
        /// </summary>
        /// <param name="accountId">The account to get a session for.</param>
        /// <param name="vivoxServiceInstance">The service instance, which may be null.</param>
        /// <param name="isServiceInstanceOptional">A flag to differentiate between the two public overloads.
        /// If false, a null vivoxServiceInstance will cause an exception on non-WebGL platforms.</param>
        private ILoginSession GetOrCreateLoginSession(AccountId accountId, IVivoxServiceInternal vivoxServiceInstance, bool isServiceInstanceOptional)
        {
            if (AccountId.IsNullOrEmpty(accountId))
            {
                throw new ArgumentNullException(nameof(accountId));
            }

            CheckInitialized();

            if (_loginSessions.ContainsKey(accountId))
            {
                return _loginSessions[accountId];
            }

            ILoginSession newLoginSession;
#if UNITY_WEBGL && !UNITY_EDITOR
            newLoginSession = new LoginSessionWeb(this, accountId);
#else
            if (vivoxServiceInstance == null)
            {
                if (isServiceInstanceOptional)
                {
                    // This is the valid case from GetLoginSession(accountId).
                    // Use the constructor that doesn't require a service instance.
                    newLoginSession = new LoginSession(this, accountId);
                }
                else
                {
                    // The caller used the overload expecting a service instance but provided null.
                    throw new ArgumentNullException(nameof(vivoxServiceInstance), "A non-null vivoxServiceInstance is required when calling this method overload on this platform.");
                }
            }
            else
            {
                newLoginSession = new LoginSession(this, accountId, vivoxServiceInstance);
            }
#endif

            _loginSessions[accountId] = newLoginSession;
            return newLoginSession;
        }

        /// <summary>
        /// Specifies whether the client is initialized: True if initialized, false if uninitialized.
        /// Note: The state of this is managed by the Core SDK; the wrapper is forwarding the information.
        /// </summary>
        public bool Initialized
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return Convert.ToBoolean(vx_is_initialized());
#endif
                if (VxClient.PlatformNotSupported) return false;
                return Convert.ToBoolean(VivoxCoreInstancePINVOKE.vx_is_initialized());
            }
        }

        /// <summary>
        /// All of the Login sessions associated with this Client instance.
        /// </summary>
        public IReadOnlyDictionary<AccountId, ILoginSession> LoginSessions => _loginSessions;
        /// <summary>
        /// The audio input devices associated with this Client instance.
        /// </summary>
        public IAudioDevices AudioInputDevices => _inputDevices;
        /// <summary>
        /// The audio output devices associated with this Client instance.
        /// </summary>
        public IAudioDevices AudioOutputDevices => _outputDevices;

        /// <summary>
        /// Indicates whether Vivox's software echo cancellation feature is enabled.
        /// Note: This is completely independent of any hardware-provided acoustic echo cancellation that might be available for a device.
        /// </summary>
        public bool IsAudioEchoCancellationEnabled
        {
            get
            {
                if (VxClient.PlatformNotSupported) return false;
                return VivoxCoreInstance.IsAudioEchoCancellationEnabled();
            }
        }

        /// <summary>
        /// Turn Vivox's audio echo cancellation feature on or off.
        /// </summary>
        /// <param name="onOff">True for on, False for off.</param>
        public void SetAudioEchoCancellation(bool onOff)
        {
            CheckInitialized();

            if (IsAudioEchoCancellationEnabled != onOff)
            {
                VivoxCoreInstance.vx_set_vivox_aec_enabled(Convert.ToInt32(onOff));
            }
        }

        /// <summary>
        /// Get the current value of the Voice Processing IO mode for iOS.
        /// 2 (default): Will always use the Voice Processing Audio Unit, no matter the audio setup.
        /// 1: Will only use the Voice Processing Audio Unit when the speaker phone is in use.
        /// 0: Will never use the Voice Processing Audio Unit.
        /// </summary>
        /// <returns>Current Voice Processing IO mode for IOS, -1 on error.</returns>
        public int IosVoiceProcessingIOMode
        {
            get
            {
                if (VxClient.PlatformNotSupported) return -1;
                int mode;
                VivoxCoreInstance.vx_get_ios_voice_processing_io_mode(out mode);
                return mode;
            }
        }

        /// <summary>
        /// Set the value of the Voice Processing IO mode for iOS.
        /// 2 (default): Will always use the Voice Processing Audio Unit, no matter the audio setup.
        /// 1: Will only use the Voice Processing Audio Unit when the speaker phone is in use.
        /// 0: Will never use the Voice Processing Audio Unit.
        /// </summary>
        /// <param name="mode">2 (default), 1, or 0.</param>
        public void SetIosVoiceProcessingIOMode(int mode)
        {
            CheckInitialized();
            VivoxCoreInstance.vx_set_ios_voice_processing_io_mode(mode);
        }

        void IDisposable.Dispose()
        {
            Uninitialize();
        }

        public static void Run(Func<bool> done)
        {
            MessagePump.Instance.RunUntil(done);
        }

        public static bool Run(WaitHandle handle, TimeSpan until)
        {
            var finishTime = DateTime.Now + until;
            MessagePump.Instance.RunUntil(() => MessagePump.IsDone(handle, finishTime));
            if (handle != null)
            {
                return handle.WaitOne(0);
            }

            return false;
        }

        /// <summary>
        /// Process all asynchronous messages.
        /// This must be called periodically by the application at a frequency of no less than every 100ms.
        /// </summary>
        public static void RunOnce()
        {
            MessagePump.Instance.RunUntil(() => MessagePump.IsDone(null, DateTime.Now));
        }

        internal bool TTSInitialize()
        {
            if (!_ttsIsInitialized && !VxClient.PlatformNotSupported)
            {
                // NB: When there is more than one tts_engine type available, we will need to make a public TTSInitialize() method.
                vx_tts_status status = VivoxCoreInstance.vx_tts_initialize(vx_tts_engine_type.tts_engine_vivox_default, out _ttsManagerId);
                if (vx_tts_status.tts_status_success == status)
                    _ttsIsInitialized = true;
            }
            return _ttsIsInitialized;
        }

        internal void TTSShutdown()
        {
            if (_ttsIsInitialized)
            {
                var status = VivoxCoreInstance.vx_tts_shutdown();
                foreach (ILoginSession session in _loginSessions)
                {
                    ((TextToSpeech)session.TTS).CleanupTTS();
                }
                _ttsIsInitialized = false;
            }
        }
    }
}
