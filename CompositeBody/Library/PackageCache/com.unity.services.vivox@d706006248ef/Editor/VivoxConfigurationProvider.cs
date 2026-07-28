using Unity.Services.Core.Configuration.Editor;
using UnityEditor.Build;
using UnityEditor;

namespace Unity.Services.Vivox.Editor
{
    /// <summary>
    /// Provider for settings related to Vivox that we want available at runtime.
    /// </summary>
    class VivoxConfigurationProvider : IConfigurationProvider
    {
        // Temporarily disable telemetry (until 1.3.2 is available) to prevent a crash when building for Switch platform
        const string k_TelemetryDisabledKey = "com.unity.services.core.telemetry-disabled";

        int IOrderedCallback.callbackOrder { get; }

        /// <summary>
        /// Adds your configuration values to the given <paramref name="builder"/>.
        /// This method is called on fresh instances created by reflection to be sure you can
        /// reach the settings you want available at runtime directly from a new instance.
        /// </summary>
        /// <param name="builder">
        /// The builder used to create the runtime configuration data.
        /// Use it to set configuration values.
        /// </param>
        public void OnBuildingConfiguration(ConfigurationBuilder builder)
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Switch)
            {
                builder.SetBool(k_TelemetryDisabledKey, true);
            }

            if (VivoxSettings.Instance.IsTestMode)
            {
                builder.SetString(VivoxServiceInternal.k_TokenKey, VivoxSettings.Instance.TokenKey);
            }
            else
            {
                builder.SetString(VivoxServiceInternal.k_TokenKey, string.Empty);
            }
            builder.SetString(VivoxServiceInternal.k_ServerKey, VivoxSettings.Instance.Server ?? string.Empty);
            builder.SetString(VivoxServiceInternal.k_DomainKey, VivoxSettings.Instance.Domain ?? string.Empty);
            builder.SetString(VivoxServiceInternal.k_IssuerKey, VivoxSettings.Instance.TokenIssuer ?? string.Empty);
            builder.SetBool(VivoxServiceInternal.k_EnvironmentCustomKey, false);
            builder.SetBool(VivoxServiceInternal.k_TestModeKey, VivoxSettings.Instance.IsTestMode);
        }
    }
}
