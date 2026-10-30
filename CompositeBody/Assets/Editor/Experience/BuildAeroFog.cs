using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Puts AERO's volumetric fog into the onboarding: the renderer feature on URP-Performant's
    /// renderer, and the controller in the Onboarding scene.
    ///
    /// THIS ADDS THE PROJECT'S FIRST RENDERER FEATURE, which is worth saying out loud because it
    /// is not free. URP-Performant has run with an empty feature list and m_MSAA 4; a fullscreen
    /// pass forces an intermediate colour target and an MSAA resolve every frame, per eye, at
    /// 90 Hz. That cost is the reason the 光圈 were built as per-light cone proxies instead of a
    /// fullscreen effect in the first place. It is being paid here deliberately.
    ///
    /// The fog material is AERO's own, copied into the project's Materials folder rather than
    /// used in place -- tuning the fog for this piece should not edit a file that updating the
    /// package will overwrite.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildAeroFog.Run
    /// </summary>
    public static class BuildAeroFog
    {
        const string k_Renderer = "Assets/VRMPAssets/Settings/URP-Performant-Renderer.asset";
        const string k_Scene = "Assets/_Scenes/Onboarding.unity";
        const string k_AeroMaterial =
            "Assets/Mirza/AERO - Volumetric Fog and Mist/Materials/Volumetric Fog PC (Gradients) 2.mat";
        const string k_OurMaterial = "Assets/Materials/OnboardingFog.mat";

        [MenuItem("Tools/Onboarding/Add AERO Fog")]
        public static void Run()
        {
            Debug.Log("[AeroFog] Starting...");

            Material fog = CopyMaterial();
            if (fog == null) return;

            if (!AddFeature(fog)) return;
            AddController(fog);

            AssetDatabase.SaveAssets();
            Debug.Log("[AeroFog] RESULT: PASS");
        }

        /// <summary>
        /// Our own copy of AERO's fog material, so the piece's settings survive a package update.
        /// </summary>
        static Material CopyMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(k_OurMaterial);
            if (existing != null)
            {
                Debug.Log($"[AeroFog] {k_OurMaterial} already exists; left as it is.");
                return existing;
            }

            var source = AssetDatabase.LoadAssetAtPath<Material>(k_AeroMaterial);
            if (source == null)
            {
                Debug.LogError($"[AeroFog] RESULT: FAIL - no AERO material at {k_AeroMaterial}. " +
                               "Is the package imported?");
                return null;
            }

            if (!AssetDatabase.CopyAsset(k_AeroMaterial, k_OurMaterial))
            {
                Debug.LogError($"[AeroFog] RESULT: FAIL - could not copy to {k_OurMaterial}.");
                return null;
            }

            AssetDatabase.ImportAsset(k_OurMaterial, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[AeroFog] copied the fog material to {k_OurMaterial}.");
            return AssetDatabase.LoadAssetAtPath<Material>(k_OurMaterial);
        }

        static bool AddFeature(Material fog)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(k_Renderer);
            if (renderer == null)
            {
                Debug.LogError($"[AeroFog] RESULT: FAIL - no renderer at {k_Renderer}.");
                return false;
            }

            // Found by type NAME, not by referencing AERO's type: this editor assembly must not
            // fail to compile when the package is absent, which it has been for most of this
            // project's life.
            System.Type featureType = System.AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return System.Type.EmptyTypes; } })
                .FirstOrDefault(t => t.Name == "FullscreenCustomLightingFeature");

            if (featureType == null)
            {
                Debug.LogError("[AeroFog] RESULT: FAIL - FullscreenCustomLightingFeature not " +
                               "found. AERO's scripts are not in the project.");
                return false;
            }

            foreach (ScriptableRendererFeature existing in renderer.rendererFeatures)
            {
                if (existing != null && existing.GetType() == featureType)
                {
                    Debug.Log("[AeroFog] the fog feature is already on the renderer.");
                    return true;
                }
            }

            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
            feature.name = "AERO Volumetric Fog";

            var so = new SerializedObject(feature);
            SerializedProperty material = so.FindProperty("material");
            if (material != null) material.objectReferenceValue = fog;
            so.ApplyModifiedPropertiesWithoutUndo();

            renderer.rendererFeatures.Add(feature);
            AssetDatabase.AddObjectToAsset(feature, renderer);

            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssetIfDirty(renderer);
            AssetDatabase.ImportAsset(k_Renderer, ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[AeroFog] added the feature to {Path.GetFileName(k_Renderer)} " +
                      "(the project's first renderer feature -- this forces an intermediate " +
                      "target and an MSAA 4 resolve per eye).");
            return true;
        }

        static void AddController(Material fog)
        {
            if (!File.Exists(k_Scene))
            {
                Debug.LogWarning($"[AeroFog] no scene at {k_Scene}; the feature is on the " +
                                 "renderer but nothing drives it.");
                return;
            }

            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.OpenScene(k_Scene, OpenSceneMode.Single);

            System.Type controllerType = System.AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return System.Type.EmptyTypes; } })
                .FirstOrDefault(t => t.Name == "VolumetricFogController");

            if (controllerType == null)
            {
                Debug.LogWarning("[AeroFog] VolumetricFogController not found; the fog will use " +
                                 "the material's authored values with nothing driving them.");
                return;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren(controllerType, true) != null)
                {
                    Debug.Log("[AeroFog] a fog controller is already in the scene.");
                    return;
                }
            }

            var go = new GameObject("AERO Fog");
            Component controller = go.AddComponent(controllerType);

            var so = new SerializedObject(controller);
            SerializedProperty material = so.FindProperty("material");
            if (material != null) material.objectReferenceValue = fog;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, k_Scene);
            Debug.Log($"[AeroFog] added 'AERO Fog' to {Path.GetFileName(k_Scene)}.");
        }
    }
}
