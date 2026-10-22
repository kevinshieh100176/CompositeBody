using UnityEngine;
using UnityEngine.XR;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Reports which XR compile symbols this build actually has, and what the runtime stereo mode
    /// is, once at startup.
    ///
    /// WHY THIS EXISTS. The KriptoFX water turned one eye grey and the other black. Its stereo
    /// detection is written like this:
    ///
    ///     #if ENABLE_VR
    ///         return XRSettings.stereoRenderingMode == SinglePassInstanced;
    ///     #else
    ///         return false;
    ///     #endif
    ///
    /// so without the symbol it does not report "unknown", it reports NOT STEREO -- and then
    /// allocates single-slice render targets while the camera is drawing into a two-slice array.
    /// One eye gets the water and the other gets nothing, which is exactly what it looked like.
    ///
    /// A missing compile symbol is invisible: the code simply is not there, nothing warns, and
    /// the asset behaves as though it had decided something. This prints the symbols so that
    /// kind of fault can be read off a log instead of inferred from a rendering artefact.
    ///
    /// Always on, one line, at startup. It costs a string and answers a question that has
    /// already cost an evening.
    /// </summary>
    public static class StereoDefineProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Report()
        {
            const string vr =
#if ENABLE_VR
                "ENABLE_VR ON";
#else
                "ENABLE_VR *OFF*";
#endif

            const string vrModule =
#if ENABLE_VR_MODULE
                "ENABLE_VR_MODULE ON";
#else
                "ENABLE_VR_MODULE *OFF*";
#endif

            const string xrModule =
#if ENABLE_XR_MODULE
                "ENABLE_XR_MODULE ON";
#else
                "ENABLE_XR_MODULE *OFF*";
#endif

            // ONLY TRUE OF THIS ASSEMBLY. versionDefines are declared per assembly definition,
            // so a symbol that is off here can be on in a package that asks for it -- KWS
            // maps com.unity.modules.vr to ENABLE_VR_MODULE in its own asmdef, and this
            // file, which has no asmdef at all, is in Assembly-CSharp and asks for
            // nothing. Reading this line as though it described the whole project is a
            // mistake that was made once already, in the hour this was written.
            Debug.Log($"[Stereo] in Assembly-CSharp: {vr}, {vrModule}, {xrModule}." +
                      $"\n[Stereo] runtime: XR {(XRSettings.enabled ? "enabled" : "disabled")}, " +
                      $"device '{XRSettings.loadedDeviceName}', " +
                      $"mode {XRSettings.stereoRenderingMode}." +
                      "\n[Stereo] Any symbol marked *OFF* means code guarded by it was never " +
                      "compiled, and an effect that tests it falls through to its 'not stereo' " +
                      "branch rather than saying it does not know. For what a PACKAGE was " +
                      "compiled with, read its asmdef, not this line.");
        }
    }
}
