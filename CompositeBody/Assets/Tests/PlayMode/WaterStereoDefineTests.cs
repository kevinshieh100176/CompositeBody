using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace CompositeBody.Tests
{
    /// <summary>
    /// Guards the one line that decides whether the river renders to both eyes.
    ///
    /// THE FAULT THIS CATCHES. The KriptoFX water sets up its render targets from a flag:
    ///
    ///     #if ENABLE_VR_MODULE &amp;&amp; ENABLE_VR &amp;&amp; ENABLE_XR_MODULE
    ///         KWS_CoreUtils.SinglePassStereoEnabled = KWS_CoreUtils.IsSinglePassStereoActive();
    ///     #endif
    ///
    /// ENABLE_VR_MODULE comes from a versionDefines entry in KWS's own asmdef, which asks for
    /// com.unity.modules.vr -- a built-in module that NO LONGER EXISTS in Unity 6. So the flag
    /// was never assigned, the water allocated single-slice targets while the camera drew into a
    /// two-slice array, and one eye came back grey and the other black. Deactivating the river
    /// made it normal, which made it look like a water bug rather than a build-configuration one.
    ///
    /// The asmdef is now pointed at com.unity.modules.xr, which does exist, so the symbol is
    /// defined and the flag is set.
    ///
    /// WHY A TEST AND NOT A COMMENT. That asmdef is a vendor file. The next KWS update will
    /// overwrite it, the symbol will go undefined again, and an eye will go black with nothing
    /// to connect it to an asset update weeks earlier. This fails instead, by name.
    /// </summary>
    public class WaterStereoDefineTests
    {
        const string k_Asmdef =
            "KriptoFX/WaterSystem/WaterResources/Scripts/KWS_asmdef.asmdef";

        const string k_Define = "ENABLE_VR_MODULE";

        [Test]
        public void WaterStereoSymbolComesFromAPackageThatExists()
        {
            string path = Path.Combine(Application.dataPath, k_Asmdef);
            if (!File.Exists(path))
            {
                Assert.Ignore($"KWS is not in this project ({k_Asmdef}), so there is nothing " +
                              "to guard. If the river is in the show, this is a real failure.");
                return;
            }

            string asmdef = File.ReadAllText(path);
            StringAssert.Contains(k_Define, asmdef,
                $"KWS's asmdef no longer defines {k_Define}. Its stereo setup is guarded by " +
                "that symbol, so without it the water renders to one eye and the other goes " +
                "black. An asset update has probably overwritten the file.");

            // The package the symbol hangs off must be one the project actually has, or the
            // define never fires. This is exactly how the fault presented: the entry named
            // com.unity.modules.vr, which Unity 6 removed, so it silently never matched.
            string manifest = File.ReadAllText(
                Path.Combine(Application.dataPath, "..", "Packages", "manifest.json"));

            foreach (string package in PackagesDefining(asmdef, k_Define))
            {
                StringAssert.Contains($"\"{package}\"", manifest,
                    $"KWS's asmdef defines {k_Define} from '{package}', which is not in " +
                    "Packages/manifest.json, so the symbol is never defined and the river " +
                    "renders to one eye. com.unity.modules.vr was removed in Unity 6; " +
                    "com.unity.modules.xr is the one that exists.");
                return;
            }

            Assert.Fail($"KWS's asmdef mentions {k_Define} but no versionDefines entry maps a " +
                        "package to it, so the symbol cannot be defined.");
        }

        /// <summary>
        /// The package names whose versionDefines entry produces <paramref name="define"/>.
        ///
        /// Read with string work rather than a JSON parser because the editor assemblies a test
        /// can reach do not carry one, and the shape being looked for -- a "name" and a "define"
        /// inside one object -- is small enough that the parsing is not where the risk is.
        /// </summary>
        static System.Collections.Generic.IEnumerable<string> PackagesDefining(
            string asmdef, string define)
        {
            foreach (string chunk in asmdef.Split('{'))
            {
                if (!chunk.Contains(define)) continue;

                int at = chunk.IndexOf("\"name\"", System.StringComparison.Ordinal);
                if (at < 0) continue;

                int open = chunk.IndexOf('"', chunk.IndexOf(':', at) + 1);
                int close = chunk.IndexOf('"', open + 1);
                if (open < 0 || close < 0) continue;

                yield return chunk.Substring(open + 1, close - open - 1);
            }
        }
    }
}
