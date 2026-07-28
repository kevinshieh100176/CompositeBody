using Unity.XR.CompositionLayers.Services;
using UnityEngine.XR.OpenXR.CompositionLayers;
using UnityEngine.XR.OpenXR.NativeTypes.Android;

namespace UnityEngine.XR.OpenXR.Features.Android
{
    /// <summary>
    /// A stub class because Android XR doesn't technically support a passthrough composition layer;
    /// instead, final composition is done over the passthrough camera as per XrEnvironmentBlendMode
    /// </summary>
    class AndroidOpenXRPassthroughLayer : OpenXRCustomLayerHandler<XrCompositionLayerPassthroughAndroid>
    {
        bool m_WasPassthroughEnabled = false;

        protected override bool CreateSwapchain(
            CompositionLayerManager.LayerInfo layerInfo,
            out SwapchainCreateInfo swapchainCreateInfo
        )
        {
            m_WasPassthroughEnabled = ARCameraFeature.GetPassthrough();
            ARCameraFeature.SetPassthrough(true);
            // Swapchain not needed for this layer
            swapchainCreateInfo = default;
            return false;
        }

        protected override bool CreateNativeLayer(
            CompositionLayerManager.LayerInfo layerInfo,
            SwapchainCreatedOutput swapchainOutput,
            out XrCompositionLayerPassthroughAndroid nativeLayer
        )
        {
            nativeLayer = default;
            return false;
        }

        protected override bool ModifyNativeLayer(
            CompositionLayerManager.LayerInfo layerInfo,
            ref XrCompositionLayerPassthroughAndroid nativeLayer
        )
        {
            return true;
        }

        public override void RemoveLayer(int removedLayerId)
        {
            ARCameraFeature.SetPassthrough(m_WasPassthroughEnabled);
            base.RemoveLayer(removedLayerId);
        }
    }
}
