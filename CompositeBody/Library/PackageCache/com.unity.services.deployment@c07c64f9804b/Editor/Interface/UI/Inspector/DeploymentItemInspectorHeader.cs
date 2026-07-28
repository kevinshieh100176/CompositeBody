using System;
using System.Collections.Generic;
using Unity.Services.DeploymentApi.Editor;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;

namespace Unity.Services.Deployment.Editor.Interface.UI.Inspector
{
    /// <summary>
    /// UnityEditor.Editor header drawer class that will display the <see cref="DeploymentStatus"/> of an asset
    /// if it is tracked by the <see cref="Deployments"/> API.
    /// </summary>
    static class DeploymentItemInspectorHeader
    {
        /// <summary>
        /// FilePath to DeploymentItems lookup table.
        /// </summary>
        /// <remarks>This content is loaded lazily on the first inspector header draw.</remarks>
        static DeploymentItemsLookup _deploymentItemsLookup = null;

        /// <summary>
        /// FilePath to Editor lookup table.
        /// </summary>
        static DeploymentItemEditorLookup _editorLookup = null;

        /// <summary>
        /// Caching for the status icons textures so they are not loaded for every render.
        /// </summary>
        /// <remarks>This content is loaded lazily on the first inspector header draw.</remarks>
        static readonly Dictionary<SeverityLevel, Texture> StatusIcons = new();

        /// <summary>
        /// Used to lazy initialize the <see cref="_deploymentItemsLookup"/>, <see cref="_editorLookup"/>
        /// and the <see cref="StatusIcons"/> on the first inspector header draw.
        /// </summary>
        static bool _initialized = false;

        /// <summary>
        /// Foldout state for the deployment items header in the inspector when multiple items are displayed.
        /// </summary>
        const string FoldoutStateKey = nameof(DeploymentItemInspectorHeader) + "." + nameof(FoldoutState);
        static bool _foldoutCache = EditorPrefs.GetBool(FoldoutStateKey, true);
        static bool FoldoutState
        {
            get => _foldoutCache;
            set
            {
                if (value != _foldoutCache)
                {
                    _foldoutCache = value;
                    EditorPrefs.SetBool(FoldoutStateKey, value);
                }
            }
        }

        /// <summary>
        /// Initialization system. Registering the drawing method to the Editor system and loading the cache.
        /// </summary>
        [InitializeOnLoadMethod]
        static void SetupDeploymentConfigInspectorHeader()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawEditorHeader;
        }

        /// <summary>
        /// Find the list of <see cref="IDeploymentItem"/>s for the current editor targets and draw them if any.
        /// </summary>
        /// <param name="editor">The editor header being drawn. <see cref="Editor.finishedDefaultHeaderGUI"/></param>
        static void DrawEditorHeader(UnityEditor.Editor editor)
        {
            if (editor == null || Deployments.Instance == null)
                return;

            try
            {
                if (!_initialized)
                {
                    StatusIcons.Add(SeverityLevel.Warning, AssetDatabase.LoadAssetAtPath<Texture>("Packages/com.unity.services.deployment/Editor/Interface/UI/Assets/Icons/Warning.png"));
                    StatusIcons.Add(SeverityLevel.Error, AssetDatabase.LoadAssetAtPath<Texture>("Packages/com.unity.services.deployment/Editor/Interface/UI/Assets/Icons/Error.png"));
                    StatusIcons.Add(SeverityLevel.Success, AssetDatabase.LoadAssetAtPath<Texture>("Packages/com.unity.services.deployment/Editor/Interface/UI/Assets/Icons/Success.png"));
                    _deploymentItemsLookup = new DeploymentItemsLookup(Deployments.Instance);
                    _editorLookup = new DeploymentItemEditorLookup(_deploymentItemsLookup);
                    _initialized = true;
                }

                _editorLookup.CleanupEditors();

                var deploymentItems = new List<IDeploymentItem>();
                var paths = new HashSet<string>();
                foreach (var target in editor.targets)
                {
                    if (target is AssetImporter assetImporter)
                    {
                        if (!string.IsNullOrEmpty(assetImporter.assetPath) && _deploymentItemsLookup.TryGetDeploymentItemsAtPath(assetImporter.assetPath, out var items))
                        {
                            paths.Add(assetImporter.assetPath);
                            deploymentItems.AddRange(items);
                        }
                    }
                    else if (target != null)
                    {
                        // If this is not an AssetImporter editor, only display the deployment status if the Importer Editor is hidden.
                        // Testing for the path to be a valid deployment item before checking the importer hide flags because it's faster.
                        var path = AssetDatabase.GetAssetPath(target);
                        if (!string.IsNullOrEmpty(path) && _deploymentItemsLookup.TryGetDeploymentItemsAtPath(path, out var items)
                            && (AssetImporter.GetAtPath(path)?.hideFlags & HideFlags.HideInInspector) == HideFlags.HideInInspector)
                        {
                            paths.Add(path);
                            deploymentItems.AddRange(items);
                        }
                    }
                }

                if (deploymentItems.Count > 0)
                {
                    _editorLookup.RegisterEditorForRedraw(editor, paths);
                    DrawDeploymentItemsHeader(deploymentItems);
                }
            }
            catch (Exception e)
            {
                // Logging error with a verbose log to avoid spamming the console.
                Shared.Logging.Logger.LogVerbose("Failed to draw a deployment item header with error: " + e.Message);
            }
        }

        /// <summary>
        /// IMGUI render of a list of <see cref="IDeploymentItem"/>s. <br />
        /// If multiple items are present, a foldout is used to show the status of each item with their name.
        /// </summary>
        /// <param name="items"></param>
        static void DrawDeploymentItemsHeader(List<IDeploymentItem> items)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                if (items.Count > 1)
                {
                    FoldoutState = EditorGUILayout.Foldout(FoldoutState,
                        EditorGUIUtility.TrTempContent("Deployment Status"));
                    if (FoldoutState)
                    {
                        using (new EditorGUI.IndentLevelScope())
                        {
                            foreach (var item in items)
                            {
                                DrawDeploymentItemLine(item.Name, item);
                            }
                        }
                    }
                }
                else
                {
                    DrawDeploymentItemLine("Deployment Status", items[0]);
                }
            }
        }

        /// <summary>
        /// Custom drawing of a single line in IMGUI to be able to control the space
        /// between the two label fields and insert an icon in-between.
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="item"></param>
        static void DrawDeploymentItemLine(string prefix, IDeploymentItem item)
        {
            if (item == null)
                return;

            // Getting a control Rect for exactly one line in the rendering.
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);

            // Calculating the width of the first label.
            var prefixContent = EditorGUIUtility.TrTempContent($"{prefix}: ");
            var size = GUI.skin.label.CalcSize(prefixContent);

            // Drawing the first label on the left.
            EditorGUI.LabelField(rect, prefixContent);

            // Moving the left side of the draw to after the text content.
            rect.xMin += size.x;

            var icon = StatusIcons.GetValueOrDefault(item.Status.MessageSeverity);
            if (icon != null)
            {
                // GUI calls doesn't take the indent size into account,
                // so we're saving it once here to be added to CalcSize and GUI.DrawTexture.
                var indentLevel = EditorGUI.IndentedRect(Rect.zero).xMin;
                // Forcing the texture size to be 8x8 pixels.
                var iconRect = new Rect(rect.xMin + indentLevel, rect.yMin + (rect.height - 8) / 2, 8, 8);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

                // Forcing spacing between the texture and the second label.
                rect.xMin += 11f;
            }

            // Drawing the file status itself in the leftover space.
            EditorGUI.LabelField(rect, GetDeploymentStatusContent(item));
        }

        /// <summary>
        /// Converts a <see cref="IDeploymentItem"/> status into a <see cref="GUIContent"/> for IMGUI display in the inspector.<br />
        /// If the current status message is empty, it will display "No status available" instead of an empty line.<br />
        /// </summary>
        /// <param name="item">A <see cref="DeploymentItem"/>.</param>
        /// <returns>A <see cref="GUIContent"/> instance using the <see cref="Message"/> as the text,
        /// the <see cref="IDeploymentItem.Status.MessageSeverity"/> for the icon,
        /// and the <see cref="IDeploymentItem.Status.MessageDetail"/> as a tooltip.</returns>
        static GUIContent GetDeploymentStatusContent(IDeploymentItem item)
        {
            var message = string.IsNullOrEmpty(item.Status.Message) ? "No status available" : item.Status.Message;
            return new GUIContent(message, null, item.Status.MessageDetail);
        }
    }
}
