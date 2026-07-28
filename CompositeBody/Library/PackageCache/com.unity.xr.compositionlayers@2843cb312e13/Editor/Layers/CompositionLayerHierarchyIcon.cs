using UnityEditor;
using UnityEngine;

namespace Unity.XR.CompositionLayers.Layers.Editor
{
    [InitializeOnLoad]
    static class CompositionLayerHierarchyIcon
    {
        const float k_IconSize = 16f;
        const float k_IconPadding = 2f;

        static GUIContent s_WarningIcon;

        static CompositionLayerHierarchyIcon()
        {
#if UNITY_6000_4_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI -= OnHierarchyItemGUI;
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyItemGUI;
#else
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyItemGUI;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
#endif
        }

#if UNITY_6000_4_OR_NEWER
        static void OnHierarchyItemGUI(EntityId entityId, Rect selectionRect)
        {
            DrawIcon(EditorUtility.EntityIdToObject(entityId) as GameObject, selectionRect);
        }
#else
        static void OnHierarchyItemGUI(int instanceID, Rect selectionRect)
        {
            DrawIcon(EditorUtility.InstanceIDToObject(instanceID) as GameObject, selectionRect);
        }
#endif

        static void DrawIcon(GameObject go, Rect selectionRect)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            if (go == null)
                return;

            var layer = go.GetComponent<CompositionLayer>();
            if (layer == null)
                return;

            var warning = CompositionLayerSceneValidation.GetCameraConfigWarning(layer);
            if (warning == null)
                return;

            if (s_WarningIcon == null)
                s_WarningIcon = new GUIContent(EditorGUIUtility.IconContent("console.warnicon.sml"));
            s_WarningIcon.tooltip = warning;

            var iconRect = new Rect(
                selectionRect.xMax - k_IconSize - k_IconPadding,
                selectionRect.y,
                k_IconSize,
                selectionRect.height);

            GUI.Label(iconRect, s_WarningIcon);
        }
    }
}
