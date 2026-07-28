using System.Runtime.InteropServices;

namespace UnityEngine.XR.OpenXR.Features.Android
{
    static class XrFuncTableUtils
    {
        internal static void ClearCachedFunc(string methodName)
        {
            NativeApi.ClearCachedFunc(methodName);
        }

        static class NativeApi
        {
            [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_XrFuncTable_ClearCachedFunc")]
            public static extern void ClearCachedFunc(string methodName);
        }
    }
}
