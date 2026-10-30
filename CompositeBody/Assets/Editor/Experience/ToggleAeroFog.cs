using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Switches AERO's fullscreen fog pass on and off without unpicking the setup.
    ///
    /// WHY THE RENDERER FEATURE AND NOT THE SCENE OBJECT. Disabling the 'AERO Fog' controller in
    /// the Onboarding scene would stop the fog being driven but leave the pass in the pipeline,
    /// and the pass is where the cost is: a fullscreen effect forces URP to render to an
    /// intermediate colour target and resolve MSAA 4 every frame, per eye, at 90 Hz. That is the
    /// cost BuildAeroFog's header says is being paid deliberately, and the feature's m_Active is
    /// the only switch that stops paying it. With this the only feature on the renderer, turning
    /// it off returns URP-Performant to the empty feature list it ran with for most of this
    /// project's life.
    ///
    /// WHY DISABLE RATHER THAN REMOVE. The feature holds the reference to OnboardingFog.mat, and
    /// that material carries hand-tuned values. Removing the feature and adding it back would
    /// mean re-pointing it; m_Active costs nothing to flip and loses nothing.
    ///
    /// The controller object is left alone on purpose -- with no pass it has nothing to drive, and
    /// leaving it in place means turning the fog back on is this one switch again rather than two.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.ToggleAeroFog.Disable
    /// </summary>
    public static class ToggleAeroFog
    {
        const string k_Renderer = "Assets/VRMPAssets/Settings/URP-Performant-Renderer.asset";
        const string k_FeatureType = "FullscreenCustomLightingFeature";

        [MenuItem("Tools/Onboarding/AERO Fog/Disable")]
        public static void Disable() => Set(false);

        [MenuItem("Tools/Onboarding/AERO Fog/Enable")]
        public static void Enable() => Set(true);

        static void Set(bool active)
        {
            string what = active ? "Enable" : "Disable";
            Debug.Log($"[AeroToggle] {what}: starting...");

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(k_Renderer);
            if (renderer == null)
            {
                Debug.LogError($"[AeroToggle] RESULT: FAIL - no renderer at {k_Renderer}.");
                return;
            }

            // Found by type NAME rather than by referencing AERO's type, so this editor assembly
            // still compiles when the package is absent -- which it has been, more than once.
            ScriptableRendererFeature feature = renderer.rendererFeatures
                .FirstOrDefault(f => f != null && f.GetType().Name == k_FeatureType);

            if (feature == null)
            {
                Debug.LogError($"[AeroToggle] RESULT: FAIL - no {k_FeatureType} on the renderer. " +
                               "Run BuildAeroFog first, or AERO is not in the project.");
                return;
            }

            if (feature.isActive == active)
            {
                Debug.Log($"[AeroToggle] already {(active ? "on" : "off")}; nothing to do.\n" +
                          "[AeroToggle] RESULT: PASS");
                return;
            }

            feature.SetActive(active);

            // SetActive writes the instance; the serialized field is what survives a reload, so
            // the sub-asset is marked dirty and the whole renderer asset re-imported.
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(k_Renderer, ImportAssetOptions.ForceSynchronousImport);

            int remaining = renderer.rendererFeatures.Count(f => f != null && f.isActive);
            Debug.Log($"[AeroToggle] '{feature.name}' is now {(active ? "active" : "inactive")}. " +
                      $"{remaining} active renderer feature(s) left.\n" +
                      (active
                          ? "[AeroToggle] the fullscreen pass is back, and with it the " +
                            "intermediate target and the MSAA 4 resolve per eye.\n"
                          : "[AeroToggle] no fullscreen pass, so URP can skip the intermediate " +
                            "target and the MSAA resolve again. The 光圈 and the orbit glows are " +
                            "unaffected -- those are per-light proxies, not a fullscreen effect.\n") +
                      "[AeroToggle] RESULT: PASS");
        }
    }
}
