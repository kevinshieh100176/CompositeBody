using Unity.XR.CoreUtils.Editor;

namespace UnityEditor.XR.Interaction.Toolkit.Samples
{
    /// <summary>
    /// Non-blocking stand-in for <see cref="PackageVersionUtility"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="PackageVersionUtility.GetPackageVersion"/> fills its cache with
    /// <c>Client.List</c> followed by <c>while (!request.IsCompleted) Thread.Sleep(50)</c> on the
    /// main thread. The request only reports completion from the editor tick that loop is blocking,
    /// so the first Project Validation check after a domain reload can wedge the editor for good.
    /// The registered package list is already resolved in memory, so read it directly instead.
    /// </remarks>
    static class SamplePackageVersions
    {
        public static PackageVersion GetPackageVersion(string packageName)
        {
            var info = global::UnityEditor.PackageManager.PackageInfo.FindForPackageName(packageName);
            return info != null ? new PackageVersion(info.version) : default;
        }

        public static bool IsPackageInstalled(string packageName)
        {
            return GetPackageVersion(packageName) != default;
        }
    }
}
