using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace Unity.Services.Multiplayer.Editor
{
    /// <summary>
    /// SettingsProvider for Unity Services and Multiplayer.
    /// </summary>
    static class MultiplayerSettingsProvider
    {
        const string k_SettingsPath = "Project/Services/Multiplayer";
        const string k_DefineServices = "ENABLE_UNITY_SERVICES_VERBOSE_LOGGING";
        const string k_DefineMultiplayer = "ENABLE_UNITY_MULTIPLAYER_VERBOSE_LOGGING";

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider(k_SettingsPath, SettingsScope.Project)
            {
                label = "Multiplayer",
                keywords = new HashSet<string>(new[] { "Multiplayer", "Verbose", "Logging", "Services" }),
                guiHandler = OnGui
            };
        }

        static void OnGui(string searchContext)
        {
            var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
            var definesStr = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
            var defines = definesStr.Split(';').Where(s => !string.IsNullOrEmpty(s)).ToList();
            var hasServices = defines.Contains(k_DefineServices);
            var hasMultiplayer = defines.Contains(k_DefineMultiplayer);
            var verboseEnabled = hasServices || hasMultiplayer;

            EditorGUI.BeginChangeCheck();
            var newValue = EditorGUILayout.Toggle("Verbose Logging", verboseEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                if (newValue)
                {
                    if (!defines.Contains(k_DefineServices)) defines.Add(k_DefineServices);
                    if (!defines.Contains(k_DefineMultiplayer)) defines.Add(k_DefineMultiplayer);
                }
                else
                {
                    defines.RemoveAll(s => s == k_DefineServices || s == k_DefineMultiplayer);
                }
                PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, string.Join(";", defines));
            }

            if (verboseEnabled)
                EditorGUILayout.HelpBox("Verbose logging is enabled. Extra debug output may appear in the console.", MessageType.Info);
        }
    }
}
