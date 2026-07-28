#if !UNITY_2021_1_OR_NEWER && PLATFORM_IOS
#define NEEDS_MANUAL_PRIVACY_MANIFEST_ADDITION
#endif

using UnityEditor;
using UnityEditor.Callbacks;
#if PLATFORM_IOS || PLATFORM_VISIONOS
using System.IO;
using UnityEditor.iOS.Xcode;
#endif

internal class BuildPostProcessor
{
    [PostProcessBuildAttribute(1)]
    internal static void OnPostProcessBuild(BuildTarget target, string path)
    {
#if NEEDS_MANUAL_PRIVACY_MANIFEST_ADDITION
        AddPrivacyManifest(target, path);
#endif
#if PLATFORM_IOS
        if (target == BuildTarget.iOS)
        {
            PostProcessIosBuild(path);
        }
#elif UNITY_2022_3_OR_NEWER && PLATFORM_VISIONOS
        if (target == BuildTarget.VisionOS)
        {
            PostProcessVisionOsBuild(path);
        }
#endif
    }

    internal static void PostProcessIosBuild(string path)
    {
#if PLATFORM_IOS
        string simulatorSuffix = PlayerSettings.iOS.sdkVersion == iOSSdkVersion.SimulatorSDK ? "-simulator" : "";
        string vivoxNativeLib = $"libVivoxNative{simulatorSuffix}.a";
        string sdkLibName = $"libvivoxsdk{simulatorSuffix}.a";
        string staticLibrarySrcPath = $"Packages/com.unity.services.vivox/Plugins/iOS/";
        string relativeLibPathInXcodeProj = $"Libraries/com.unity.services.vivox/Plugins/iOS/";

        PBXProject project = new PBXProject();
        string sPath = PBXProject.GetPBXProjectPath(path);
        project.ReadFromFile(sPath);

        string vivoxNativeLibSrcPath = $"{staticLibrarySrcPath}{vivoxNativeLib}";
        string vivoxNativeLibPathInXcodeProj = $"{relativeLibPathInXcodeProj}{vivoxNativeLib}";
        AddFileToXcodeProject(project, path, vivoxNativeLibSrcPath, vivoxNativeLibPathInXcodeProj);

        string sdkLibSrcPath = $"{staticLibrarySrcPath}{sdkLibName}";
        string sdkLibPathInXcodeProj = $"{relativeLibPathInXcodeProj}{sdkLibName}";
        AddFileToXcodeProject(project, path, sdkLibSrcPath, sdkLibPathInXcodeProj);

        // Since we don't ask Unity to add our libraries, we need to add the search path manually
        string unityFrameworkTarget = project.GetUnityFrameworkTargetGuid();
        project.AddBuildProperty(unityFrameworkTarget, "LIBRARY_SEARCH_PATHS", "$(PROJECT_DIR)/" + relativeLibPathInXcodeProj);
        File.WriteAllText(PBXProject.GetPBXProjectPath(path), project.WriteToString());
#endif
    }

    internal static void PostProcessVisionOsBuild(string path)
    {
#if PLATFORM_VISIONOS
        string libName = PlayerSettings.VisionOS.sdkVersion == VisionOSSdkVersion.Simulator ? "libvivoxsdk-simulator.a" : "libvivoxsdk.a";
        string packageName = "com.unity.services.vivox";
        string staticLibrarySrcPath = $"Packages/{packageName}/Plugins/visionOS/{libName}";
        if (!File.Exists(staticLibrarySrcPath))
        {
            packageName = "com.unity.services.vivox-visionos";
            staticLibrarySrcPath = $"Packages/{packageName}/Plugins/visionOS/{libName}";
        }
        string relativeLibPathInXcodeProj = $"Libraries/ARM64/Packages/{packageName}/Plugins/visionOS/{libName}";

        PBXProject project = new PBXProject();
        string sPath = path + "/Unity-VisionOS.xcodeproj/project.pbxproj";
        project.ReadFromFile(sPath);

        AddFileToXcodeProject(project, path, staticLibrarySrcPath, relativeLibPathInXcodeProj);

        File.WriteAllText(sPath, project.WriteToString());
#endif
    }

#if NEEDS_MANUAL_PRIVACY_MANIFEST_ADDITION
    internal static void AddPrivacyManifest(BuildTarget target, string path)
    {
        string privacyManifestPath = "Packages/com.unity.services.vivox/Plugins/iOS/PrivacyInfo.xcprivacy";
        string relativeLibPathInXcodeProj = $"Libraries/com.unity.services.vivox/Plugins/iOS/PrivacyInfo.xcprivacy";
        PBXProject project = new PBXProject();
        string sPath = PBXProject.GetPBXProjectPath(path);
        if (!File.Exists(sPath))
        {
            return;
        }
        project.ReadFromFile(sPath);
        AddFileToXcodeProject(project, path, privacyManifestPath, relativeLibPathInXcodeProj);
        File.WriteAllText(sPath, project.WriteToString());
    }
#endif

#if PLATFORM_IOS || PLATFORM_VISIONOS
    internal static void AddFileToXcodeProject(PBXProject project, string projectPath, string srcPath, string relativePathInXcodeProj)
    {
        string xcodeLibPath = projectPath + "/" + relativePathInXcodeProj;
        string xcodeLibDirectory = Path.GetDirectoryName(xcodeLibPath);
        if (!Directory.Exists(xcodeLibDirectory))
        {
            Directory.CreateDirectory(xcodeLibDirectory);
        }

        UnityEngine.Debug.Log($"Copying Vivox library from: {srcPath} to {xcodeLibPath}");
        CopyAndReplaceFile(srcPath, xcodeLibPath);
        string target = project.GetUnityFrameworkTargetGuid();
        string fileGuid = project.AddFile(relativePathInXcodeProj, relativePathInXcodeProj);
        project.AddFileToBuild(target, fileGuid);
    }

    internal static void CopyAndReplaceFile(string srcPath, string dstPath)
    {
        if (File.Exists(dstPath))
            File.Delete(dstPath);
        File.Copy(srcPath, dstPath);
    }
#endif
}
