using System.Collections.Generic;
using System.ComponentModel;
using Unity.Services.DeploymentApi.Editor;
using UnityEditor;
#if UNITY_6000_3_OR_NEWER
using UnityEngine;
#endif

namespace Unity.Services.Deployment.Editor.Interface.UI.Inspector
{
    /// <summary>
    /// Lookup table for <see cref="UnityEditor.Editor"/> instances that need to be redrawn based on a <see cref="IDeploymentItem"/> paths.<br />
    /// The class registers to the <see cref="IDeploymentItem.PropertyChanged"/> event to trigger a redraw of the editor when the status of a deployment item changes.
    /// </summary>
    class DeploymentItemEditorLookup
    {
#if UNITY_6000_3_OR_NEWER
        readonly Dictionary<string, HashSet<EntityId>> _editorLookup = new();
#else
        readonly Dictionary<string, HashSet<int>> _editorLookup = new();
#endif
        readonly DeploymentItemsLookup _deploymentItemsLookup;

        public DeploymentItemEditorLookup(DeploymentItemsLookup deploymentItemsLookup)
        {
            _deploymentItemsLookup = deploymentItemsLookup;
        }

        /// <summary>
        /// Registers the given <see cref="UnityEditor.Editor"/> for redraw when the status of any <see cref="IDeploymentItem"/> at the specified file paths changes.<br />
        /// </summary>
        /// <param name="editor">The editor instance to be redraw on <see cref="DeploymentItem"/> changes.</param>
        /// <param name="filePaths">The paths currently displayed by the editor.</param>
        public void RegisterEditorForRedraw(UnityEditor.Editor editor, HashSet<string> filePaths)
        {
            foreach (var filePath in filePaths)
            {
                if (!_editorLookup.TryGetValue(filePath, out var editors))
                {
#if UNITY_6000_3_OR_NEWER
                    editors = new HashSet<EntityId>();
#else
                    editors = new HashSet<int>();
#endif
                    _editorLookup.Add(filePath, editors);

                    if (_deploymentItemsLookup.TryGetDeploymentItemsAtPath(filePath, out var items))
                    {
                        foreach (var item in items)
                        {
                            if (item != null)
                            {
                                item.PropertyChanged += TriggerEditorRedraw;
                            }
                        }
                    }
                }

#if UNITY_6000_3_OR_NEWER
                editors.Add(editor.GetEntityId());
#else
                editors.Add(editor.GetInstanceID());
#endif
            }
        }

        /// <summary>
        /// Cleans up the editor lookup table by removing any editors that are no longer valid instances
        /// and unregister the <see cref="IDeploymentItem.PropertyChanged"/> event
        /// for paths that no longer have any editors drawing them.
        /// </summary>
        public void CleanupEditors()
        {
            List<string> keysToRemove = new List<string>();
            foreach (var pathToEditor in _editorLookup)
            {
                // If the path is not a valid deployment item anymore, it can be safely removed.
                if (!_deploymentItemsLookup.TryGetDeploymentItemsAtPath(pathToEditor.Key, out var deploymentItems))
                {
                    keysToRemove.Add(pathToEditor.Key);
                    continue;
                }

                // Remove any editor that is no longer valid.
#if UNITY_6000_3_OR_NEWER
                pathToEditor.Value.RemoveWhere(editorId => EditorUtility.EntityIdToObject(editorId) == null);
#else
                pathToEditor.Value.RemoveWhere(editorId => EditorUtility.InstanceIDToObject(editorId) == null);
#endif
                // If there are no more editors for this path, we can remove it and unregister its callback.
                if (pathToEditor.Value.Count == 0)
                {
                    foreach (var deploymentItem in deploymentItems)
                    {
                        if (deploymentItem != null)
                        {
                            deploymentItem.PropertyChanged -= TriggerEditorRedraw;
                        }
                    }
                    keysToRemove.Add(pathToEditor.Key);
                }
            }
            foreach (var key in keysToRemove)
            {
                _editorLookup.Remove(key);
            }
        }

        /// <summary>
        /// Triggers an Editor redraw for all editors registered to the <see cref="IDeploymentItem"/>.
        /// </summary>
        /// <remarks>
        /// This method is registered to the <see cref="IDeploymentItem.PropertyChanged"/> event.
        /// </remarks>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void TriggerEditorRedraw(object sender, PropertyChangedEventArgs e)
        {
            if (sender is IDeploymentItem deploymentItem
                && !string.IsNullOrEmpty(deploymentItem.Path)
                && _editorLookup.TryGetValue(deploymentItem.Path, out var editorList))
            {
                foreach (var editorId in editorList)
                {
#if UNITY_6000_3_OR_NEWER
                    var editorObject = EditorUtility.EntityIdToObject(editorId) as UnityEditor.Editor;
#else
                    var editorObject = EditorUtility.InstanceIDToObject(editorId) as UnityEditor.Editor;
#endif
                    if (editorObject != null)
                        editorObject.Repaint();
                }
            }
        }
    }
}
