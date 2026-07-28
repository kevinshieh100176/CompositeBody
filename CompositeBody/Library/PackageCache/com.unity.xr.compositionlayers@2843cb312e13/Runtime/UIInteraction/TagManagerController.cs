#if UNITY_EDITOR
using UnityEditor;

namespace Unity.XR.CompositionLayers.UIInteraction
{
    /// <summary>
    /// Helper for creating a TagManager asset file for controlling Canvas Layers
    /// </summary>
    internal static class TagManagerController
    {
        // Index to start creating new layers (8 ignores the provided layers)
        const int ArrayStartIndex = 8;

        // TagManager asset (ProjectSettings/TagManager.asset)
        static SerializedObject s_TagManager;

        // Layer property in tagManager
        static SerializedProperty s_LayersProp;

        /// <summary>
        /// Initialize TagManager asset file
        /// </summary>
        static TagManagerController()
        {
            s_TagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            s_LayersProp = s_TagManager.FindProperty("layers");
        }

        /// <summary>
        /// Tries to add a Layer to tagManager
        /// </summary>
        /// <param name="layerName">Layer name to add</param>
        /// <returns>Whether or not the layer was added</returns>
        public static bool TryAddLayer(string layerName)
        {
            SerializedProperty firstEmptyLayer = null;

            s_TagManager.Update();

            // Check if the layerName already exists and try cache the first empty layer found
            bool found = false;
            for (int i = ArrayStartIndex; i < s_LayersProp.arraySize; i++)
            {
                SerializedProperty layer = s_LayersProp.GetArrayElementAtIndex(i);
                if (layer.stringValue.Equals(layerName))
                {
                    found = true;
                    break;
                }

                if (firstEmptyLayer == null && string.IsNullOrEmpty(layer.stringValue))
                    firstEmptyLayer = layer;
            }

            // if layerName was not found, add it to the first empty layer
            if (!found)
            {
                if (firstEmptyLayer != null)
                {
                    firstEmptyLayer.stringValue = layerName;
                    s_TagManager.ApplyModifiedPropertiesWithoutUndo();
                }
                else
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Removes layer from tagManager
        /// </summary>
        /// <param name="layerName">Layer name to remove</param>
        public static void RemoveLayer(string layerName)
        {
            s_TagManager.Update();

            if (string.IsNullOrEmpty(layerName))
                return;

            SerializedProperty layer = null;

            for (int i = 0; i < s_LayersProp.arraySize; i++)
            {
                layer = s_LayersProp.GetArrayElementAtIndex(i);
                if (layer.stringValue.Equals(layerName))
                    break;
            }

            if (layer == null)
                return;

            layer.stringValue = string.Empty;

            s_TagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void RemoveAllLayersContaining(string layerName)
        {
            for (int i = ArrayStartIndex; i < s_LayersProp.arraySize; i++)
            {
                SerializedProperty layer = s_LayersProp.GetArrayElementAtIndex(i);
                if (layer.stringValue.Contains(layerName))
                    layer.stringValue = string.Empty;
            }
            s_TagManager.ApplyModifiedPropertiesWithoutUndo();
        }

    }
}
#endif
