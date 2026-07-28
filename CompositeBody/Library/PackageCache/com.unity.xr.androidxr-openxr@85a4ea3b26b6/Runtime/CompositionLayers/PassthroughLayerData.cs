using System;
using Unity.XR.CompositionLayers.Layers;

namespace UnityEngine.XR.OpenXR.Features.Android
{
    /// <summary>
    /// Subclass of <see cref="LayerData" /> that defines a passthrough layer in a scene.
    /// </summary>
    [Serializable]
    [CompositionLayerData(
        Provider = "Android XR OpenXR",
        Name = "Passthrough",
        IconPath = "",
        InspectorIcon = "",
        ListViewIcon = "",
        Description = "Passthrough Layer",
        SuggestedExtenstionTypes = new Type[] { }
    )]
    public class PassthroughLayerData : LayerData
    {
    }
}
