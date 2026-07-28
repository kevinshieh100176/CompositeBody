using System;
using Unity.XR.CompositionLayers.Services;
using UnityEngine;

namespace Unity.XR.CompositionLayers.Layers.Editor
{
    static class CompositionLayerSceneValidation
    {
        static Camera s_CachedCamera;
        static int s_CachedCameraFrame = -1;

        internal static string GetCameraConfigWarning(CompositionLayer layer)
        {
            if (layer == null)
                return null;

            var layerData = layer.LayerData;
            if (layerData == null)
                return null;

            var isUnderlay = layer.Order < 0;
            var isPassthroughByType = CompositionLayerManager.PassthroughLayerType?.IsInstanceOfType(layerData) == true;
            var isPassthroughByName = layerData.GetType().Name.Contains("Passthrough", StringComparison.InvariantCultureIgnoreCase);
            var isPassthrough = isPassthroughByType || isPassthroughByName;

            if (!isUnderlay && !isPassthrough)
                return null;

            var camera = ResolveMainCamera();
            if (camera == null)
                return null;

            var layerKind = isPassthrough ? "passthrough" : "underlay";
            if (camera.clearFlags == CameraClearFlags.Skybox)
                return $"Cameras with clear flags set to Skybox may obscure this {layerKind} layer. Set Clear Flags to Solid Color with Background alpha 0.";
            if (camera.clearFlags == CameraClearFlags.SolidColor && camera.backgroundColor.a > 0f)
                return $"Camera Background alpha must be 0 for {layerKind} layers to render correctly. Adjust the Background color's alpha to 0.";

            return null;
        }

        static Camera ResolveMainCamera()
        {
            var frame = Time.frameCount;
            if (s_CachedCameraFrame == frame && s_CachedCamera != null)
                return s_CachedCamera;

            s_CachedCameraFrame = frame;
            s_CachedCamera = CompositionLayerManager.mainCameraCache;
            if (s_CachedCamera != null)
                return s_CachedCamera;

            s_CachedCamera = Camera.main;
            return s_CachedCamera;
        }
    }
}
