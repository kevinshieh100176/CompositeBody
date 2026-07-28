using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;
using UnityEngine.XR.OpenXR.Features;

namespace UnityEditor.XR.OpenXR.Features.Android
{
    [MovedFrom(false, null, null, "AndroidXRSettingsInitializer")]
    [FilePath("ProjectSettings/AndroidXRSettingsManager.asset", FilePathAttribute.Location.ProjectFolder)]
    class AndroidXRSettingsManager : ScriptableSingleton<AndroidXRSettingsManager>
    {
        static readonly string k_ProjectSettingsPath = Path.Combine("ProjectSettings", "AndroidXRSettingsManager.asset");
        static readonly string k_LegacyAssetDirectoryPath = Path.Combine("Assets", "XR", "AndroidXR");
        static readonly string k_LegacyAssetInitializerPath = Path.Combine(k_LegacyAssetDirectoryPath, "AndroidXRSettingsInitializer");

        static readonly string k_SessionStateLegacyInitializerFileCheckKey = "AndroidXRLegacySettingsInitializerFileChecked";

        [SerializeField, HideInInspector, FormerlySerializedAs("isInitialized")]
        bool hasInitializedXRSettings;

        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            if (!SessionState.GetBool(k_SessionStateLegacyInitializerFileCheckKey, false))
            {
                if (AssetDatabase.AssetPathExists(k_LegacyAssetInitializerPath))
                {
                    MigrateFromLegacyInitializer();
                    instance.Save(true);
                }

                SessionState.SetBool(k_SessionStateLegacyInitializerFileCheckKey, true);
            }

            EditorApplication.delayCall += OnEditorReloadedDelayed;
        }

        static void OnEditorReloadedDelayed()
        {
#if UNITY_ANDROID_XR
            if (!instance.hasInitializedXRSettings)
                instance.InitializeXRSettings();
#else
            if (instance.hasInitializedXRSettings)
            {
                instance.hasInitializedXRSettings = false;
                instance.Save(true);
            }
#endif
        }

        static void MigrateFromLegacyInitializer()
        {
            Debug.Log("Migrating Android XR Settings asset file to project settings.");

            File.Copy(k_LegacyAssetInitializerPath, k_ProjectSettingsPath, true);
            AssetDatabase.DeleteAsset(k_LegacyAssetInitializerPath);

            var assetDirectoryIsEmpty = !Directory.EnumerateFileSystemEntries(k_LegacyAssetDirectoryPath)
                .Any(entry =>
                {
                    var fileName = Path.GetFileName(entry);
                    return fileName != ".DS_Store" && fileName != "Thumbs.db";
                });

            if (assetDirectoryIsEmpty)
            {
                AssetDatabase.DeleteAsset(k_LegacyAssetDirectoryPath);
            }
        }

        void InitializeXRSettings()
        {
            var androidXrFeatureSet = OpenXRFeatureSetManager.GetFeatureSetWithId(
                BuildTargetGroup.Android,
                AndroidFeatureSet.featureSetId
            );

            if (androidXrFeatureSet != null)
                androidXrFeatureSet.isEnabled = true;
            else
                Debug.LogWarning("Android XR feature set could not be enabled in OpenXR settings.");

            var foveatedRenderingFeature = FeatureHelpers.GetFeatureWithIdForActiveBuildTarget(FoveatedRenderingFeature.featureId);

            if (foveatedRenderingFeature != null)
                foveatedRenderingFeature.enabled = true;
            else
                Debug.LogWarning("Foveated Rendering feature could not be enabled in OpenXR settings.");

            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(BuildTargetGroup.Android);

            hasInitializedXRSettings = true;
            Save(true);
        }
    }
}
