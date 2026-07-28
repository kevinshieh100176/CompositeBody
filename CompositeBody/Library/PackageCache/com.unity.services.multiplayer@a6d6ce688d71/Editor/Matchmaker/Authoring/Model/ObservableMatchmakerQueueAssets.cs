using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Unity.Services.DeploymentApi.Editor;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Parser;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.IO;
using Unity.Services.Multiplayer.Editor.Shared.Assets;
using UnityEditor;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Model
{
    /// <summary>
    /// This class serves to track creation and deletion of Matchmaker queue assets
    /// in the project and maintains the list of associated deployment items.
    /// </summary>
    sealed class ObservableMatchmakerQueueAssets : IDisposable
    {
        readonly IMatchmakerConfigParser m_ResourceLoader;

        readonly AssetPostprocessorProxy m_AssetPostprocessor;
        readonly Dictionary<string, MatchmakerConfigResource> m_PathsToDeploymentItems = new Dictionary<string, MatchmakerConfigResource>();
        public ObservableCollection<IDeploymentItem> DeploymentItems { get; } = new ObservableCollection<IDeploymentItem>();

        public ObservableMatchmakerQueueAssets(IMatchmakerConfigParser resourceLoader)
        {
            m_ResourceLoader = resourceLoader;
            m_AssetPostprocessor = new AssetPostprocessorProxy();
            m_AssetPostprocessor.AllAssetsPostprocessed += OnAllAssetsPostprocessed;
            // Do no try to find all files if the AssetDatabase is already updating, wait for the postprocessor.
            // It can happen when a project is open in safe mode and compiles for the first time.
            if (!EditorApplication.isUpdating)
            {
                ProcessAllAssets();
            }
        }

        void OnAllAssetsPostprocessed(object sender, PostProcessEventArgs e)
        {
            if (e.DidDomainReload)
            {
                ProcessAllAssets();
            }
            else
            {
                foreach (var assetPath in e.ImportedAssetPaths)
                {
                    AssetAdded(assetPath);
                }

                foreach (var assetPath in e.DeletedAssetPaths)
                {
                    AssetRemoved(assetPath);
                }

                for (int i = 0; i < e.MovedAssetPaths.Length; i++)
                {
                    AssetMoved(e.MovedFromAssetPaths[i], e.MovedAssetPaths[i]);
                }
            }
        }

        void AssetMoved(string movedFromAssetPath, string movedAssetPath)
        {
            if (!movedFromAssetPath.EndsWith(".mmq") && !movedFromAssetPath.EndsWith(".mme"))
            {
                // Old path was not a matchmaker asset, treat it as a possible addition
                AssetAdded(movedAssetPath);
                return;
            }
            if (!movedAssetPath.EndsWith(".mmq") && !movedAssetPath.EndsWith(".mme"))
            {
                // The new path is not a matchmaker asset, treat it as a possible removal
                AssetRemoved(movedFromAssetPath);
                return;
            }

            if (!m_PathsToDeploymentItems.Remove(movedFromAssetPath, out var deploymentItem))
            {
                // Should never happen, but for some reason the old path is not tracked. Treat it as an addition.
                AssetAdded(movedAssetPath);
                return;
            }
            deploymentItem.Path = movedAssetPath;
            deploymentItem.Name = Path.GetFileNameWithoutExtension(movedAssetPath);
            m_PathsToDeploymentItems.Add(movedAssetPath, deploymentItem);
            UpdateAssetContent(movedAssetPath, deploymentItem);
        }

        void AssetRemoved(string assetPath)
        {
            if (!assetPath.EndsWith(".mmq") && !assetPath.EndsWith(".mme"))
                return;

            if (m_PathsToDeploymentItems.TryGetValue(assetPath, out var deploymentItem))
            {
                DeploymentItems.Remove(deploymentItem);
                m_PathsToDeploymentItems.Remove(assetPath);
            }
        }

        void AssetAdded(string assetPath)
        {
            if (!assetPath.EndsWith(".mmq") && !assetPath.EndsWith(".mme"))
                return;

            if (!m_PathsToDeploymentItems.TryGetValue(assetPath, out var deploymentItem))
            {
                deploymentItem = new MatchmakerConfigResource();
                deploymentItem.Name = Path.GetFileNameWithoutExtension(assetPath);
                deploymentItem.Path = assetPath;
                deploymentItem.Type =
                    Path.GetExtension(assetPath) == IMatchmakerConfigParser.EnvironmentConfigExtension ? "Environment Config" : "Queue Config";

                m_PathsToDeploymentItems.Add(assetPath, deploymentItem);
                DeploymentItems.Add(deploymentItem);
            }

            UpdateAssetContent(assetPath, deploymentItem);
        }

        void UpdateAssetContent(string assetPath, MatchmakerConfigResource deploymentItem)
        {
            ClearSerializationErrorStates(deploymentItem);
            try
            {
                deploymentItem.Content = m_ResourceLoader.Parse(assetPath);
            }
            catch (MyServiceDeserializationException e)
            {
                deploymentItem.States.Add(
                    new AssetState(e.ErrorMessage, e.Details, SeverityLevel.Error));
            }
        }

        void ProcessAllAssets()
        {
            DeploymentItems.Clear();
            m_PathsToDeploymentItems.Clear();
            var allAssets = AssetDatabase.FindAssets("glob:\"**.(mmq|mme)\"");
            foreach (var guid in allAssets)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                AssetAdded(path);
            }
        }

        public void Dispose()
        {
            m_AssetPostprocessor.AllAssetsPostprocessed -= OnAllAssetsPostprocessed;
        }

        static void ClearSerializationErrorStates(MatchmakerConfigResource resource)
        {
            for (int i = resource.States.Count - 1; i >= 0; --i)
            {
                if (MatchmakerConfigLoader.IsDeserializationError(resource.States[i].Description))
                {
                    resource.States.RemoveAt(i);
                }
            }
        }
    }
}
