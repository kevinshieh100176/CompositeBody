#if UNITY_EDITOR
namespace UnityEditor.XR.CompositionLayers.Editor.ProjectValidation
{
    /// <summary>
    /// Validation rule generator for shared validation rules between multiple features.
    /// </summary>
    static class CompositionLayerBuildValidationRuleFactory
    {
#if UNITY_RENDER_PIPELINES_UNIVERSAL
        internal static CompositionLayerBuildValidationRule CreateHDRRuleForURP(BuildTargetGroup buildTargetGroup)
        {
            return new CompositionLayerBuildValidationRule
            {
                Message = "Disable HDR on Universal Render Pipeline Asset to enable composition layer support.",
                CheckPredicate = () =>
                {
                    foreach (var urpAsset in CompositionLayerProjectValidation.GetURPAssetsForBuildTarget(buildTargetGroup))
                        if (urpAsset != null && urpAsset.supportsHDR)
                            return false;

                    return true;
                },
                FixIt = () => CompositionLayerProjectValidation.DisableHDROnURPAssets(buildTargetGroup),
                FixItAutomatic = true,
                FixItMessage = "Open Graphics Settings to disable HDR on Universal Render Pipeline Asset.",
                Error = false,
                buildTargetGroup = buildTargetGroup
            };
        }

        internal static CompositionLayerBuildValidationRule CreateAlphaProcessingRuleForURP(BuildTargetGroup buildTargetGroup)
        {
            return new CompositionLayerBuildValidationRule
            {
                Message = "Enable **Post-processing** > **Alpha processing** on your Universal Render Pipeline asset if your project uses transparent composition layers (Quad, Cylinder, Equirect, Cubemap, or Projection Eye Rig). If your project only uses Passthrough, you can leave it disabled for better performance.",
                CheckPredicate = () =>
                {
#if UNITY_2023_1_OR_NEWER
                    foreach (var urpAsset in CompositionLayerProjectValidation.GetURPAssetsForBuildTarget(buildTargetGroup))
                        if (urpAsset != null && !urpAsset.allowPostProcessAlphaOutput)
                            return false;

                    return true;
#else
                    return true;
#endif
                },
                FixIt = () => CompositionLayerProjectValidation.OpenFirstURPAssetWithAlphaOutputOff(buildTargetGroup),
                FixItAutomatic = false,
                FixItMessage = "Open the Universal Render Pipeline Asset to enable/disable the Alpha Processing setting under Post Processing.",
                Error = false,
                buildTargetGroup = buildTargetGroup
            };
        }
#endif
    }
}
#endif
