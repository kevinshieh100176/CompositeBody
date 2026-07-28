using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Core.Editor.Environments;
using Unity.Services.Deployment.Editor.Shared.EditorUtils;
using Unity.Services.Deployment.Editor.Shared.Infrastructure.Collections;
using Unity.Services.Deployment.Editor.Tracking;
using Unity.Services.DeploymentApi.Editor;
using Logger = Unity.Services.Deployment.Editor.Shared.Logging.Logger;

namespace Unity.Services.Deployment.Editor.SyncWithRemote
{
    sealed class DeploymentItemSyncWithRemote : IDisposable
    {
        public int SyncDelayMs { get; set; } = 100;
        public Task SyncTask => m_SyncTask;

        readonly ObservableCollection<DeploymentProvider> m_Providers;

        HashSet<DeploymentProvider> m_ToSync = new();
        Task m_SyncTask;

        public DeploymentItemSyncWithRemote(
            ObservableCollection<DeploymentProvider> providers,
            IDeploymentItemTracker tracker,
            IEnvironmentsApi environmentsApi)
        {
            m_Providers = providers;

            m_Providers.CollectionChanged += ProvidersOnCollectionChanged;
            m_Providers.ForEach(TriggerSync);
            tracker.ItemAdded += TriggerSync;
            tracker.ItemChanged += TriggerSync;
            tracker.ItemDeleted += TriggerSync;

            environmentsApi.PropertyChanged += EnvironmentsApiOnPropertyChanged;
        }

        void EnvironmentsApiOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IEnvironmentsApi.ActiveEnvironmentId))
            {
                m_Providers.ForEach(TriggerSync);
            }
        }

        void ProvidersOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    e.NewItems.Cast<DeploymentProvider>().ForEach(TriggerSync);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    e.NewItems.Cast<DeploymentProvider>().ForEach(TriggerSync);
                    e.OldItems.Cast<DeploymentProvider>().ForEach(m_ToSync.Remove);
                    break;
                case NotifyCollectionChangedAction.Reset:
                    m_Providers.ForEach(TriggerSync);
                    break;
                case NotifyCollectionChangedAction.Move:
                    break;
                case NotifyCollectionChangedAction.Remove:
                    e.OldItems.Cast<DeploymentProvider>().ForEach(m_ToSync.Remove);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        void TriggerSync(IDeploymentItem item)
        {
            var provider = m_Providers.FirstOrDefault(p => p.DeploymentItems.Contains(item));
            if (provider != null)
            {
                TriggerSync(provider);
            }
        }

        void TriggerSync(DeploymentProvider provider)
        {
            m_ToSync.Add(provider);
            if (m_SyncTask == null || m_SyncTask.IsCompleted)
            {
                m_SyncTask = Sync.SafeAsync(SyncWithRemote);
            }
        }

        async Task SyncWithRemote()
        {
            Logger.LogVerbose("[Sync] Job Triggered");
            do
            {
                // Voluntary throttle to wait for other possible providers to be added before triggering the sync.
                await Task.Delay(SyncDelayMs);

                Logger.LogVerbose("[Sync] Job Started");
                var syncTasks = m_ToSync
                    .Where(p => p.SyncItemsWithRemoteCommand != null)
                    .Select(p => p.SyncItemsWithRemoteCommand.ExecuteAsync(p.DeploymentItems))
                    .ToList();
                m_ToSync.Clear();

                await Task.WhenAll(syncTasks);
                Logger.LogVerbose("[Sync] Job Completed");
            }
            // Continue until there are no more items to sync.
            // This can happen if new items to sync are added while the sync is in progress.
            while (m_ToSync.Count > 0);
            Logger.LogVerbose("[Sync] Job Closing");
        }

        public void Dispose()
        {
            if (m_SyncTask != null && m_SyncTask.IsCompleted)
            {
                m_SyncTask?.Dispose();
            }
        }
    }
}
