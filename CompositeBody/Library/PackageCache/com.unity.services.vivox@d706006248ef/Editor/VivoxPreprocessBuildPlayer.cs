#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
#if UNITY_2018_1_OR_NEWER

using UnityEditor.Build.Reporting;
#endif
using Unity.Services.Vivox.Editor;
using UnityEngine;

class VivoxPreprocessBuildPlayer :
#if UNITY_2018_1_OR_NEWER
    IPreprocessBuildWithReport
# else
    IPreprocessBuild
#endif
{
    // Directory of the ChatChannelSample sample.
    private string vivoxSampleDirectory = Application.dataPath + "/Vivox/Examples/ChatChannelSample";
    // Path to our audio directory
    private string vivoxAudioDirectory = Application.dataPath + "/Vivox/Examples/ChatChannelSample/Audio";
    // Path to StreamingAssets
    private string vivoxStreamingAssetsPath = Application.dataPath + "/StreamingAssets/VivoxAssets";
    // Our audio file used
    private string vivoxAudioFile = "VivoxAudioForInjection.wav";

    public int callbackOrder { get { return 0; } }

#if UNITY_2018_1_OR_NEWER
    public void OnPreprocessBuild(BuildReport report)
    {
#if UNITY_EDITOR_OSX && UNITY_IOS
        CheckMicDescription();
#endif
        StreamingAssetsSetup();
#if AUTH_PACKAGE_PRESENT
        EditorGameServiceAnalyticsSender.SendVivoxBuildWithAuthenticationEvent();
#endif
    }

#endif

    public void OnPreprocessBuild(BuildTarget target, string path)
    {
#if UNITY_EDITOR_OSX && UNITY_IOS
        CheckMicDescription();
#endif
        StreamingAssetsSetup();
    }

    private void StreamingAssetsSetup()
    {
        if (Directory.Exists(vivoxAudioDirectory))
        {
            if (!Directory.Exists(vivoxStreamingAssetsPath))
            {
                Directory.CreateDirectory(vivoxStreamingAssetsPath);
                File.Copy(vivoxAudioDirectory + "/" + vivoxAudioFile, vivoxStreamingAssetsPath + "/" + vivoxAudioFile);
            }
            else
            {
                if (!File.Exists(vivoxStreamingAssetsPath + "/" + vivoxAudioFile))
                {
                    File.Copy(vivoxAudioDirectory + "/" + vivoxAudioFile, vivoxStreamingAssetsPath + "/" + vivoxAudioFile);
                }
            }
        }
    }

#if UNITY_EDITOR_OSX && UNITY_IOS
    private void CheckMicDescription()
    {
        if (string.IsNullOrEmpty(PlayerSettings.iOS.microphoneUsageDescription))
        {
            Debug.LogWarning("If this application requests Microphone Access you must add a description to the `Other Settings > Microphone Usage Description` in Player Settings");
        }
    }
#endif

#if !UNITY_2023_1_OR_NEWER
    [InitializeOnLoad]
    static class VivoxNativePluginSelector
    {
        static bool IsPluginActive(string path)
        {
            // If we're in a version of Unity that doesn't support the ARM64 standalone target anyway, let's exclude it from any builds.
            if (path.Contains("ARM64"))
            {
                return false;
            }

            return true;
        }

        static VivoxNativePluginSelector()
        {
            var pluginPaths = new[]
            {
                "Packages/com.unity.services.vivox/Plugins/ARM64/VivoxNative.dll",
                "Packages/com.unity.services.vivox/Plugins/ARM64/vivoxsdk.dll"
            };

            foreach (var pluginPath in pluginPaths)
            {

                var importer = PluginImporter.GetAtPath(pluginPath) as PluginImporter;
                if (importer != null) // sanity check
                {
                    importer.SetIncludeInBuildDelegate(IsPluginActive);
                }
            }
        }
    }
#endif 
}
#endif
