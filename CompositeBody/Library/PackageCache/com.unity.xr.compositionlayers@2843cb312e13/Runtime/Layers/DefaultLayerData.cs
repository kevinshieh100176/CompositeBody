using System;
using Unity.XR.CompositionLayers.Services;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Unity.XR.CompositionLayers.Layers
{
    /// <summary>
    /// Subclass of <see cref="LayerData" /> that represents the default base
    /// rendered layer for composition layer ordering. This is an implicit
    /// layer that Unity will render to the display of the target XR device.
    ///
    /// The intention of this layer is to provide a default "invisible" layer
    /// to act as the 0th layer which separates underlay layers from overlay layers.
    /// </summary>
    [CompositionLayerData(
        Provider = "Unity",
        Name = "Default Scene",
        IconPath = CompositionLayerConstants.IconPath,
        InspectorIcon = "",
        ListViewIcon = "",
        Description = "Represents the default base composition layer. This is an implicit layer which separates the overlay layers from the underlays.",
        SuggestedExtenstionTypes = new Type[] { }
     )]
    [Serializable]
    [CompositionLayersHelpURL(typeof(DefaultLayerData))]
    public class DefaultLayerData : LayerData
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultLayerData"/> class.
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        public DefaultLayerData() { }

        /// <summary>
        /// Validates the <see cref="DefaultLayerData"/> to ensure the correct layer is used by the <see cref="CompositionLayerManager"/>.
        /// </summary>
        /// <remarks>
        /// If the <see cref="CompositionLayerManager"/> is not present, this layer is valid as long as it exists in the scene and is not hidden.
        /// If the <see cref="CompositionLayerManager"/> is present, this layer is valid if it is the <see cref="CompositionLayerManager.DefaultSceneCompositionLayer"/>.
        /// </remarks>
        /// <param name="layer">The composition layer to validate.</param>
        /// <returns>
        /// <see langword="true"/> if the layer is valid for use by the <see cref="CompositionLayerManager"/>. Otherwise, <see langword="false"/>.
        /// </returns>
        protected internal override bool Validate(CompositionLayer layer)
        {
            var layerManager = CompositionLayerManager.Instance;

            // Since a user created Default Layer will exist BEFORE the manager, it is valid as long as it is not hidden.
            if (layerManager == null && !layer.gameObject.hideFlags.HasFlag(UnityEngine.HideFlags.HideAndDontSave))
                return true;
            // If the manager exists but no default scene layer has been set yet, allow this layer to become it.
            if (layerManager != null && layerManager.DefaultSceneCompositionLayer == null)
                return true;
            // If the layer exists AFTER the manager, it can only be valid if it is the default scene layer set by the manager.
            if (layerManager != null && layerManager.DefaultSceneCompositionLayer == layer)
                return true;

            // If the validation checks fail, we can destroy this layer and log a warning.
            // This can happen if a user force duplicates the default layer, or if a new scene is loaded with a default layer already in it
            Debug.LogWarning($"A Default Scene Layer already exists in the scene, destroying duplicate layer.");
            UnityObjectUtils.Destroy(layer.gameObject);
            return false;
        }
    }
}
