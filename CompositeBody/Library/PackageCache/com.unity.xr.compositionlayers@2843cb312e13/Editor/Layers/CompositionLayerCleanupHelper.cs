using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Unity.XR.CompositionLayers.UIInteraction
{
    class CompositionLayerCleanupHelper : IPreprocessBuildWithReport
    {
        public int callbackOrder => Int32.MaxValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            TagManagerController.RemoveAllLayersContaining(CanvasLayerController.CanvasLayerTagPrefix);
        }
    }
}
