using System.Collections.Generic;
using System.Collections.Specialized;
using Unity.Services.DeploymentApi.Editor;

namespace Unity.Services.Deployment.Editor.Interface.UI.Inspector
{
    /// <summary>
    /// Lookup table for <see cref="IDeploymentItem"/>s based on their path.<br />
    /// The class registers to the <see cref="Deployments"/> collection changes to update the lookup table
    /// with the list of all <see cref="IDeploymentItem"/>s sorted by their asset path.<br />
    /// </summary>
    class DeploymentItemsLookup
    {
        /// <summary>
        /// Lookup of <see cref="DeploymentItem"/> instance based on their path.<br />
        /// This is updated whenever the list of deployment items or providers changes.
        /// </summary>
        readonly Dictionary<string, List<IDeploymentItem>> _deploymentLookup = new();

        /// <summary>
        /// The deployments instance that contains the list of deployment providers and items.
        /// </summary>
        readonly Deployments _deployments;

        public DeploymentItemsLookup(Deployments deployments)
        {
            _deployments = deployments;
            _deployments.DeploymentProviders.CollectionChanged += OnDeploymentListChanged;
            RefreshLookup();
        }

        /// <summary>
        /// Output the list of <see cref="IDeploymentItem"/>s at the specified path.<br />
        /// If the path is not valid or no items are found, it will return false and a null list.
        /// </summary>
        /// <param name="path">An asset path.</param>
        /// <param name="items">List of <see cref="IDeploymentItem"/>s registered at that path.</param>
        /// <returns>True is at least one <see cref="IDeploymentItem"/> exists at the given path, false otherwise.</returns>
        public bool TryGetDeploymentItemsAtPath(string path, out List<IDeploymentItem> items)
        {
            if (string.IsNullOrEmpty(path))
            {
                items = null;
                return false;
            }

            return _deploymentLookup.TryGetValue(path, out items);
        }

        /// <summary>
        /// Update the lookup table with the current list of <see cref="IDeploymentItem"/>s.<br />
        /// This method will also register to the <see cref="DeploymentProvider.DeploymentItems.CollectionChanged"/> event on each provider.
        /// </summary>
        void RefreshLookup()
        {
            _deploymentLookup.Clear();
            foreach (var provider in _deployments.DeploymentProviders)
            {
                provider.DeploymentItems.CollectionChanged -= OnDeploymentListChanged;
                provider.DeploymentItems.CollectionChanged += OnDeploymentListChanged;
                foreach (var deploymentItem in provider.DeploymentItems)
                {
                    if (string.IsNullOrEmpty(deploymentItem.Path))
                        continue;

                    if (!_deploymentLookup.ContainsKey(deploymentItem.Path))
                    {
                        _deploymentLookup.Add(deploymentItem.Path, new List<IDeploymentItem>());
                    }
                    _deploymentLookup[deploymentItem.Path].Add(deploymentItem);
                }
            }
        }

        void OnDeploymentListChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshLookup();
        }
    }
}
