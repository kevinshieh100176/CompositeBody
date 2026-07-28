#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_RENDER_PIPELINES_UNIVERSAL
using UnityEngine.Rendering.Universal;
using Unity.XR.CompositionLayers.Emulation;
#endif

namespace UnityEditor.XR.CompositionLayers.Editor.ProjectValidation
{
    public static class CompositionLayerProjectValidation
    {
        private static readonly CompositionLayerBuildValidationRule[] BuiltinValidationRules =
        {
            new CompositionLayerBuildValidationRule
            {
                Message = "OpenXR Plugin 1.18.0-pre.1 or later is recommended for full feature support. Minimum version required is 1.12.0-exp.1 for the composition layers runtime.",
                CheckPredicate = () =>
                {
#if UNITY_XR_OPENXR_COMPLAYER
                    return true;
#else
                    return false;
#endif
                },
                FixIt = OpenPackageManager,
                FixItAutomatic = false,
                FixItMessage = "Open Package Manager to install proper OpenXR package version.",
                Error = false,
                buildTargetGroup = BuildTargetGroup.Android,
            },
#if UNITY_RENDER_PIPELINES_UNIVERSAL
            CompositionLayerBuildValidationRuleFactory.CreateHDRRuleForURP(BuildTargetGroup.Android),
            CompositionLayerBuildValidationRuleFactory.CreateAlphaProcessingRuleForURP(BuildTargetGroup.Android),
            CompositionLayerBuildValidationRuleFactory.CreateHDRRuleForURP(BuildTargetGroup.Standalone),
            CompositionLayerBuildValidationRuleFactory.CreateAlphaProcessingRuleForURP(BuildTargetGroup.Standalone),
#endif
        };

        private static readonly List<CompositionLayerBuildValidationRule> CachedValidationList = new List<CompositionLayerBuildValidationRule>(BuiltinValidationRules.Length);


        /// <summary>
        /// Opens Graphics Settings Window
        /// </summary>
        private static void OpenGraphicsSettings() => SettingsService.OpenProjectSettings("Project/Graphics");

        /// <summary>
        /// Retrieves All Validation Issues for a given build target group.
        /// </summary>
        /// <param name="issues">List of validation issues to populate. List is cleared before populating.</param>
        /// <param name="buildTarget">Build target group to check for validation issues</param>
        internal static void GetAllValidationIssues(List<CompositionLayerBuildValidationRule> issues, BuildTargetGroup buildTargetGroup)
        {
            issues.Clear();
            issues.AddRange(BuiltinValidationRules.Where(s => s.buildTargetGroup == buildTargetGroup || s.buildTargetGroup == BuildTargetGroup.Unknown));
        }

        /// <summary>
        /// Gathers and evaluates validation issues and adds them to a list if they fail their predicate check.
        /// </summary>
        /// <param name="issues">List of validation issues to populate. List is cleared before populating.</param>
        /// <param name="buildTarget">Build target group to check for validation issues</param>
        internal static void GetCurrentValidationIssues(List<CompositionLayerBuildValidationRule> issues, BuildTargetGroup buildTargetGroup)
        {
            CachedValidationList.Clear();
            CachedValidationList.AddRange(BuiltinValidationRules.Where(s => s.buildTargetGroup == buildTargetGroup || s.buildTargetGroup == BuildTargetGroup.Unknown));

            issues.Clear();
            foreach (var validation in CachedValidationList)
            {
                if (!validation.CheckPredicate?.Invoke() ?? false)
                {
                    issues.Add(validation);
                }
            }
        }

        /// <summary>
        /// Open the Package Manager window to install OpenXR package
        /// </summary>
        private static void OpenPackageManager() => UnityEditor.PackageManager.UI.Window.Open("com.unity.xr.openxr");

#if UNITY_RENDER_PIPELINES_UNIVERSAL
        internal static void DisableHDROnURPAssets(BuildTargetGroup buildTargetGroup)
        {
            foreach (var urpAsset in GetURPAssetsForBuildTarget(buildTargetGroup))
            {
                if (urpAsset != null)
                {
                    urpAsset.supportsHDR = false;
                    EditorUtility.SetDirty(urpAsset);
                    AssetDatabase.SaveAssets();
                }
            }
        }

        internal static void OpenFirstURPAssetWithAlphaOutputOff(BuildTargetGroup buildTargetGroup)
        {
#if UNITY_2023_1_OR_NEWER
            foreach (var urpAsset in GetURPAssetsForBuildTarget(buildTargetGroup))
            {

                if (urpAsset != null && !urpAsset.allowPostProcessAlphaOutput)
                {
                    Selection.activeObject = urpAsset;
                    EditorGUIUtility.PingObject(urpAsset);
                    return;
                }
            }
#endif
        }

        internal static HashSet<UniversalRenderPipelineAsset> GetURPAssetsForBuildTarget(BuildTargetGroup buildTargetGroup)
        {
            QualitySettings.GetRenderPipelineAssetsForPlatform<UniversalRenderPipelineAsset>(buildTargetGroup.ToString(), out var renderPipelineAssets);
            return renderPipelineAssets;
        }
#endif
    }
}
#endif //UNITY_EDITOR
