#if GAMEOBJECTS_NETCODE_AVAILABLE
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core.Scheduler.Internal;
using Unity.Services.DistributedAuthority;

/*
 * TODO: users might want the option to not have to wait until a client's NetworkManager is fully synchronized before the session creation task completes.
 * Currently clients are not considered fully connected to the NetworkManager until after they have finished synchronizing.
 * Synchronizing includes loading any loaded scenes. This is slow in large projects.
 */

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Manages the full lifecycle of a <see cref="Netcode.NetworkManager"/>
    /// session, from setup to start to stop. Should handle all
    /// situations where the <see cref="Netcode.NetworkManager"/>
    /// is manually started or stopped separately from the session.
    /// </summary>
    class NetworkManagerSession : IDisposable
    {
        const string k_EnclosingType = nameof(NetworkManagerSession);
        const string k_NetcodeNetworkManagerType = nameof(Netcode) + "." + nameof(Netcode.NetworkManager);
        const long k_InvalidActionId = 0;

        // Initial settings for any changeable state.
        bool m_IsTransportCached;
        NetworkTransport m_CachedTransport;
        NetworkRole m_NetworkRole;

#if GAMEOBJECTS_NETCODE_2_AVAILABLE
        bool m_IsDASettingsCached;
        bool m_CachedUseCMBService;
        NetworkTopologyTypes m_CachedTopologyType;
#endif

        readonly IActionScheduler m_ActionScheduler;
        static readonly TimeSpan k_ScheduledActionDelay = TimeSpan.FromSeconds(1);
        TaskCompletionSource<long?> m_ActionCompletionSource;
        long? m_StartNetworkManagerActionId;
        long? m_StopNetworkManagerActionId;

        internal NetworkManager NetworkManager { get; private set; }
        internal bool Disposed { get; private set; }

        public NetworkManagerSession([NotNull] IActionScheduler actionScheduler, [NotNull] NetworkManager manager,
                                     NetworkRole role)
        {
            NetworkManager = manager;
            m_ActionScheduler = actionScheduler;
            m_NetworkRole = role;
        }

        #region Getters and setters

        public void SetNetworkRole(NetworkRole newRole)
        {
            m_NetworkRole = newRole;
        }

        public UnityTransport GetUnityTransport()
        {
            return NetworkManager.NetworkConfig.NetworkTransport as UnityTransport;
        }

#if GAMEOBJECTS_NETCODE_2_AVAILABLE
        public DistributedAuthorityTransport GetDistributedAuthorityTransport()
        {
            return NetworkManager.NetworkConfig.NetworkTransport as DistributedAuthorityTransport;
        }

#endif
        public void SetTransport(UnityTransport transport)
        {
            if (Disposed)
            {
                return;
            }

            // If the transport was already cached,
            // ensure we don't override the cache.
            if (!m_IsTransportCached)
            {
                m_IsTransportCached = true;
                m_CachedTransport = NetworkManager.NetworkConfig.NetworkTransport;
            }

            NetworkManager.NetworkConfig.NetworkTransport = transport;
        }

#if GAMEOBJECTS_NETCODE_2_AVAILABLE
        public void ConfigureForDistributedAuthority()
        {
            if (Disposed)
            {
                return;
            }

            // If the settings were already cached,
            // ensure we don't override the cache.
            if (!m_IsDASettingsCached)
            {
                m_IsDASettingsCached = true;
                m_CachedUseCMBService = NetworkManager.NetworkConfig.UseCMBService;
                m_CachedTopologyType = NetworkManager.NetworkConfig.NetworkTopology;
            }

            NetworkManager.NetworkConfig.UseCMBService = true;
            NetworkManager.NetworkConfig.NetworkTopology = NetworkTopologyTypes.DistributedAuthority;
        }

#endif

        #endregion

        #region Session starting and stopping

        /// <summary>
        /// Starts the <see cref="Netcode.NetworkManager"/>.
        /// </summary>
        /// <remarks>
        /// The underlying <see cref="Netcode.NetworkManager"/> should handle timeouts internally,
        /// if the task defined by this method does not complete, the caller thread might hang indefinitely.
        /// </remarks>
        /// <returns>
        /// A <see cref="Task"/> that will be completed
        /// once the NetworkManager finishes connecting.
        /// </returns>
        /// <exception cref="SessionException">
        /// Thrown when the <see cref="Netcode.NetworkManager"/> fails to start.
        /// </exception>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            Logger.LogCallVerboseWithMessage(k_EnclosingType,
                $"Called for {NetworkManager.name}. Starting as {m_NetworkRole:G}.");

            if (Disposed)
            {
                Logger.LogCallWarning(k_EnclosingType, "Called after dispose.");
                return;
            }

            if (NetworkManager.IsListening)
            {
                Logger.LogCallWarning(k_EnclosingType, $"{k_NetcodeNetworkManagerType} is already connected.");
                return;
            }

            if (m_StartNetworkManagerActionId > k_InvalidActionId || m_ActionCompletionSource != null)
            {
                Logger.LogCallWarning(k_EnclosingType, $"{nameof(NetworkManagerSession)} is already started.");
                await m_ActionCompletionSource.Task;
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var finishedActionId = await SetupNetworkManagerLifecycleMonitoring(cancellationToken);

                if (finishedActionId != m_StartNetworkManagerActionId)
                {
                    Logger.LogCallVerboseWithMessage(k_EnclosingType,
                        $"The action with id {finishedActionId} has terminated however the expected id is {m_StartNetworkManagerActionId}.");
                }

                if (m_StartNetworkManagerActionId == null)
                {
                    // Safe to dispose as NetworkManager never started
                    throw new SessionException(
                        $"Failed to start {k_NetcodeNetworkManagerType} component as {m_NetworkRole:G}.",
                        SessionError.NetworkManagerStartFailed);
                }

                if (m_ActionCompletionSource == null || !m_ActionCompletionSource.Task.IsCompletedSuccessfully)
                {
                    throw new SessionException($"Failed to start {nameof(Netcode.NetworkManager)} component.",
                        SessionError.NetworkManagerStartFailed);
                }
            }
            // log but do not dispose, this should only happen if another action is being executed on this session
            catch (TaskCanceledException e)
            {
                Logger.LogCallVerboseWithMessage(k_EnclosingType,
                    $"{k_NetcodeNetworkManagerType} failed to start! {e}");
                m_StartNetworkManagerActionId = null;

                // StopAsync cancels the start task before it calls Shutdown(); continuations may run in between.
                // If we rethrow (or later dispose) while still listening, restoring transport/settings in Dispose is unsafe.
                if (m_StopNetworkManagerActionId > k_InvalidActionId
                    && NetworkManager.IsListening
                    && !NetworkManager.ShutdownInProgress)
                {
                    NetworkManager.Shutdown();
                }

                throw;
            }
            catch (Exception e)
            {
                // StopAsync cancels the start task before it calls Shutdown(); continuations may run in between.
                // If we rethrow (or later dispose) while still listening, restoring transport/settings in Dispose is unsafe.
                if (m_StopNetworkManagerActionId > k_InvalidActionId
                    && NetworkManager.IsListening
                    && !NetworkManager.ShutdownInProgress)
                {
                    NetworkManager.Shutdown();
                }


                Logger.LogCallVerboseWithMessage(k_EnclosingType,
                    $"{k_NetcodeNetworkManagerType} failed to start! {e}");
                m_ActionScheduler.CancelAction(m_StartNetworkManagerActionId ?? k_InvalidActionId);

                Dispose();
                if (e is not SessionException)
                {
                    throw new SessionException($"Failed to start {nameof(Netcode.NetworkManager)}: {e.Message}",
                        SessionError.NetworkManagerStartFailed);
                }

                throw;
            }
        }

        /// <summary>
        /// Stops the <see cref="Netcode.NetworkManager"/> if necessary.
        /// Not guaranteed to finish, make sure to handle timeout gracefully.
        /// </summary>
        /// <remarks>
        /// The underlying <see cref="Netcode.NetworkManager"/> should handle timeouts internally,
        /// if the task defined by this method does not complete, the caller thread might hang indefinitely.
        /// </remarks>
        /// <returns>
        /// A <see cref="Task"/> that resolves when the <see
        /// cref="Netcode.NetworkManager"/> has finished shutting down.
        /// </returns>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            Logger.LogCallVerboseWithMessage(k_EnclosingType, $"Called for {NetworkManager.name}.");

            if (Disposed)
            {
                Logger.LogCallWarning(k_EnclosingType, "Called after dispose.");
                return;
            }

            // The NetworkManager won't be subscribed to the correct callbacks if it was started outside StartAsync.
            if (m_StartNetworkManagerActionId == null)
            {
                Logger.LogCallWarning(k_EnclosingType,
                    $"{k_NetcodeNetworkManagerType} was started outside of the session. " +
                    $"Ensure {k_NetcodeNetworkManagerType}.{nameof(Netcode.NetworkManager.Shutdown)} is called manually.");
                return;
            }

            if (m_StopNetworkManagerActionId > k_InvalidActionId)
            {
                Logger.LogCallWarning(k_EnclosingType, "Called more than once.");
                await m_ActionCompletionSource.Task;
                return;
            }

            if (m_ActionCompletionSource != null)
            {
                // another action is in progress,
                m_ActionCompletionSource.TrySetCanceled();
                // most probably start!
                m_ActionScheduler.CancelAction(m_StartNetworkManagerActionId ?? k_InvalidActionId);

                m_ActionCompletionSource = null;
            }

            if (!NetworkManager.ShutdownInProgress)
            {
                m_ActionCompletionSource = new TaskCompletionSource<long?>();

                // Strat the shutdown process for the network manager.
                // The network manager will process the shutdown on following NetworkLoop update calls.
                NetworkManager.Shutdown();

                m_StopNetworkManagerActionId =
                    m_ActionScheduler.ScheduleAction(cancellationToken.ThrowIfCancellationRequested,
                        k_ScheduledActionDelay.TotalSeconds);

                // When the NetworkManager.Shutdown() completes, the completion
                // source will be set with the m_StopNetworkManagerActionId
                // so if it matches we know it was a normal shutdown,
                // if it doesn't we know something went wrong.
                var finishedActionId = await m_ActionCompletionSource.Task;
                if (finishedActionId != m_StopNetworkManagerActionId)
                {
                    Logger.LogCallVerboseWithMessage(k_EnclosingType,
                        $"The action with id {finishedActionId} has terminated however the expected id was {m_StopNetworkManagerActionId}.");
                }
            }
        }

        private Task<long?> SetupNetworkManagerLifecycleMonitoring(CancellationToken cancellationToken)
        {
            // the completion source will trigger either shutdown interrupted or completed or canceled
            m_ActionCompletionSource = new TaskCompletionSource<long?>();
            bool started;
            switch (m_NetworkRole)
            {
                case NetworkRole.Server:
                    m_StartNetworkManagerActionId = m_ActionScheduler.ScheduleAction(() =>
                    {
                        Logger.LogCallVerboseWithMessage(k_EnclosingType, "Starting server...");
                    });
                    NetworkManager.OnServerStarted -= OnServerStarted;
                    NetworkManager.OnServerStarted += OnServerStarted;
                    NetworkManager.OnServerStopped -= OnManagerStopped;
                    NetworkManager.OnServerStopped += OnManagerStopped;
                    started = NetworkManager.StartServer();
                    break;
                case NetworkRole.Host:
                    // We do not rely on the XXXStarted callbacks because of a bug.
                    ScheduleStartActionStateCheck(cancellationToken);
                    NetworkManager.OnServerStopped -= OnManagerStopped;
                    NetworkManager.OnServerStopped += OnManagerStopped;
                    started = NetworkManager.StartHost();
                    break;
                case NetworkRole.Client:
                    // We do not rely on the XXXStarted callbacks because of a bug.
                    ScheduleStartActionStateCheck(cancellationToken);
                    NetworkManager.OnClientStopped -= OnManagerStopped;
                    NetworkManager.OnClientStopped += OnManagerStopped;
                    started = NetworkManager.StartClient();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Network manager did not start correctly
            if (!started)
            {
                m_ActionScheduler.CancelAction(m_StartNetworkManagerActionId ?? k_InvalidActionId);
                return null;
            }

            return m_ActionCompletionSource.Task;
        }

        private void ScheduleStartActionStateCheck(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            m_StartNetworkManagerActionId = m_ActionScheduler
                .ScheduleAction(() => CheckNetworkManagerState(cancellationToken), k_ScheduledActionDelay.TotalSeconds);
        }

        private void CheckNetworkManagerState(CancellationToken cancellationToken)
        {
            if (m_StartNetworkManagerActionId > k_InvalidActionId
                && NetworkManager.IsListening
                && !NetworkManager.IsConnectedClient)
            {
                Logger.LogCallVerboseWithMessage(k_EnclosingType,
                    $"Waiting for client connection... {m_StartNetworkManagerActionId}.");

                ScheduleStartActionStateCheck(cancellationToken);
                return;
            }

            if (!NetworkManager.IsListening)
            {
                Logger.LogCallVerboseWithMessage(k_EnclosingType,
                    $"{k_NetcodeNetworkManagerType} has stopped listening, cancelling start task.");
                m_ActionScheduler.CancelAction(m_StartNetworkManagerActionId ?? k_InvalidActionId);
                m_StartNetworkManagerActionId = null;
                m_ActionCompletionSource.TrySetCanceled();
                return;
            }

            OnStartCompleted();
        }

        /// <summary>
        /// Finalizes the state of the <see cref="m_ActionCompletionSource"/>
        /// task. Called by callbacks once <see cref="NetworkManager"/>
        /// has started and is fully synchronized.
        /// </summary>
        private void OnStartCompleted()
        {
            Logger.LogCallVerbose(k_EnclosingType);

            if (m_StartNetworkManagerActionId == null)
            {
                // This should never happen. The callbacks are registered in
                // StartAsync so the completion token should always exist.
                Logger.LogCallError(k_EnclosingType,
                    $"{k_NetcodeNetworkManagerType} has started but the task was never created.");
                m_ActionCompletionSource?.TrySetCanceled();
                return;
            }

            m_ActionCompletionSource?.TrySetResult(m_StartNetworkManagerActionId);
        }

        /// <summary>
        /// Finalizes the state of the <see cref="m_ActionCompletionSource"/>
        /// task. Called by callbacks once <see
        /// cref="NetworkManager"/> has finished shutting down.
        /// </summary>
        private void OnStopCompleted()
        {
            if (m_StopNetworkManagerActionId == null)
            {
                if (m_StartNetworkManagerActionId != null && !NetworkManager.IsListening)
                {
                    m_ActionCompletionSource.TrySetException(new SessionException("Failed to start the network manager",
                        SessionError.NetworkManagerStartFailed));
                    return;
                }

                Logger.LogCallWarning(k_EnclosingType,
                    $"{k_NetcodeNetworkManagerType} has been shutdown outside of a session. " +
                    $"Do not call {k_NetcodeNetworkManagerType}.{nameof(Netcode.NetworkManager.Shutdown)} when using a session. Use ISession.LeaveAsync instead.");
                m_ActionCompletionSource?.TrySetCanceled();
                return;
            }

            m_ActionCompletionSource.TrySetResult(m_StopNetworkManagerActionId);
        }

        #endregion

        #region Callbacks

        private void DisposeCallbacks()
        {
            NetworkManager.OnServerStarted -= OnServerStarted;
            NetworkManager.OnClientStopped -= OnManagerStopped;
            NetworkManager.OnServerStopped -= OnManagerStopped;
        }

        private void OnServerStarted()
        {
            NetworkManager.OnServerStarted -= OnServerStarted;
            Logger.LogCallVerbose(k_EnclosingType);

            OnStartCompleted();
        }

        /// <summary>
        /// Callback that will be invoked whenever the <see
        /// cref="NetworkManager"/> is fully shut down.
        /// </summary>
        private void OnManagerStopped(bool isServer)
        {
            if (Disposed)
            {
                return;
            }

            Logger.LogCallVerbose(k_EnclosingType);

            // schedule for next frame
            m_ActionScheduler.ScheduleAction(ScheduleCompletion, UnityEngine.Time.deltaTime);

            return;

            // Wait for the current frame to finish processing
            // (allows the shutdown to completely finish).
            void ScheduleCompletion()
            {
                // Call dispose to clean up this NetworkManager session.
                Logger.LogCallVerboseWithMessage(k_EnclosingType, "Session stopped. Disposing...");
                Dispose();
                OnStopCompleted();
            }
        }

        #endregion

        public void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            if (NetworkManager.IsListening)
            {
                Logger.LogCallError(k_EnclosingType,
                    $"Cannot be disposed while {nameof(Netcode.NetworkManager)} is still listening. Cached settings can be lost!");
                return;
            }

            Logger.LogCallVerbose(k_EnclosingType);

            DisposeCallbacks();

            if (m_IsTransportCached)
            {
                NetworkManager.NetworkConfig.NetworkTransport = m_CachedTransport;
            }

#if GAMEOBJECTS_NETCODE_2_AVAILABLE
            if (m_IsDASettingsCached)
            {
                NetworkManager.NetworkConfig.UseCMBService = m_CachedUseCMBService;
                NetworkManager.NetworkConfig.NetworkTopology = m_CachedTopologyType;
            }
#endif
            Disposed = true;
        }
    }
}
#endif
