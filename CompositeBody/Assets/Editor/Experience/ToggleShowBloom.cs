using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Switches the show's post-processing on and off: bloom, a tonemapper, and the camera flag
    /// that makes either of them run at all.
    ///
    /// THREE THINGS HAVE TO BE TRUE AND ONLY ONE OF THEM IS OBVIOUS. The pipeline already
    /// supports HDR -- URP-Performant has it on, and the colour buffer is floating point, so
    /// values above white have been surviving the render all along. What was missing is anything
    /// that looks at them: post-processing is off on every camera in the project, the default
    /// volume's bloom intensity is 0, and its tonemapper is set to None. Turn on only one and
    /// nothing changes, which is why this is a single switch rather than three settings to find.
    ///
    /// WHY IT IS A TOGGLE AND NOT A DECISION. A fullscreen bloom pass on a PCVR rig at 90 Hz per
    /// eye is real frame time, and the template shipped it off for that reason. This makes trying
    /// it a menu item and makes putting it back a menu item, so the cost can be measured in a
    /// headset rather than argued about.
    ///
    /// TONEMAPPING MATTERS MORE THAN THE BLOOM. Without one, every value over 1 clips flat to
    /// white: a bright glow and a very bright glow are the same white blob, and the room's
    /// colours go with them. Neutral rolls the highlights off instead, which is what makes an
    /// HDR value read as "bright" rather than as "clipped".
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.ToggleShowBloom.Enable
    /// </summary>
    public static class ToggleShowBloom
    {
        const string k_Profile = "Assets/DefaultVolumeProfile.asset";

        internal const string RendererPath =
            "Assets/VRMPAssets/Settings/URP-Performant-Renderer.asset";

        /// <summary>
        /// URP's own post-processing resources, by package path rather than by the hashed folder
        /// the package cache happens to be in this week.
        /// </summary>
        internal const string PackagePostProcessData =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        /// <summary>The rig the players actually look through.</summary>
        const string k_RigPrefab =
            "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRMPT_XR_Origin_Setup.prefab";

        /// <summary>
        /// Gentle. The room is nearly black and the sources are small and bright, which is the
        /// case bloom flatters most -- at 1.0 the orbits read as headlights.
        /// </summary>
        const float k_BloomIntensity = 0.75f;

        /// <summary>
        /// Just under 1, so only what is genuinely over white blooms. Lower and the dim haze in
        /// the room starts glowing, which reads as a dirty lens rather than as light.
        /// </summary>
        const float k_BloomThreshold = 0.9f;

        const float k_BloomScatter = 0.72f;

        const string k_OnboardingPrefab = "Assets/_Scenes/Onboarding.prefab";

        /// <summary>
        /// How far over white the cues are pushed when this is on.
        ///
        /// BLOOM ALONE DOES ALMOST NOTHING HERE, which is the part that surprises. It only picks
        /// up what is over its threshold, and at the authored brightnesses a role colour's
        /// strongest channel only just reaches 1 -- so the pass runs, costs its frame time, and
        /// changes almost no pixels. The sources have to be given HDR values for there to be
        /// anything for it to catch.
        ///
        /// 1.6 RATHER THAN MORE. Past about two, Neutral tonemapping desaturates the highlights
        /// towards white and the purple 代表色 turns pink -- which for a piece whose two players
        /// ARE two colours is a real loss, and not an obvious consequence of a brightness slider.
        /// </summary>
        const float k_HdrFactor = 1.6f;

        /// <summary>
        /// The prints get pushed harder than the lamps, because they are small.
        ///
        /// A FOOTPRINT IS A FEW HUNDRED PIXELS ON A FLOOR and a glow is a third of the screen,
        /// so the same multiplier reads as two different amounts of light. At 1.6 the prints
        /// barely separate from the floor; past about 3 the purple 代表色 drifts to magenta as
        /// the tonemapper desaturates the highlights -- the blue and red channels are both over
        /// white by then and compress together.
        /// </summary>
        const float k_PrintHdrFactor = 2.5f;

        /// <summary>The builders' authored values, restored when this is switched off.</summary>
        const float k_LampIntensity = 1.9f;
        const float k_GlowIntensity = 0.55f;

        [MenuItem("Tools/Show/Bloom and HDR/Enable")]
        public static void Enable() => Set(true);

        [MenuItem("Tools/Show/Bloom and HDR/Disable")]
        public static void Disable() => Set(false);

        static void Set(bool on)
        {
            Debug.Log($"[Bloom] {(on ? "Enabling" : "Disabling")}...");

            if (!SetRendererResources(on)) return;
            if (!SetProfile(on)) return;
            int cameras = SetCameras(on);
            SetSourceBrightness(on);

            AssetDatabase.SaveAssets();

            // THE FADE CURVE IS BUILT FROM THE LAMPS' VALUES, so changing those behind it leaves
            // O-2's animation track ramping to the old number and then jumping to the new one
            // the instant the clip ends. Re-authoring it is cheaper than remembering this.
            BuildO2Timeline.Run();

            Debug.Log($"[Bloom] post-processing {(on ? "ON" : "off")} for {cameras} camera(s); " +
                      $"bloom {(on ? k_BloomIntensity.ToString("0.00") : "0")}, " +
                      $"tonemapping {(on ? "Neutral" : "None")}.");
            Debug.Log("[Bloom] RESULT: PASS");
        }

        /// <summary>
        /// The switch nobody finds: URP decides whether post-processing exists at all from
        /// whether the RENDERER has a PostProcessData asset --
        /// <c>postProcessEnabled = data.postProcessData != null</c> in UniversalRenderer. This
        /// project's renderer has none, so every camera flag and every bloom value in the volume
        /// was moot: there was no pass to run them in. Turning bloom "on" without this changes
        /// nothing at all and looks like the effect simply doing nothing.
        ///
        /// Set through SerializedObject rather than the typed property, which has moved between
        /// public, internal and obsolete across URP versions.
        /// </summary>
        internal static bool SetRendererResources(bool on)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableObject>(RendererPath);
            if (renderer == null)
            {
                Debug.LogError($"[Bloom] RESULT: FAIL - no renderer asset at {RendererPath}.");
                return false;
            }

            Object data = on ? AssetDatabase.LoadAssetAtPath<Object>(PackagePostProcessData) : null;
            if (on && data == null)
            {
                Debug.LogError("[Bloom] RESULT: FAIL - could not load URP's PostProcessData from " +
                               $"{PackagePostProcessData}. Without it there is no post-processing " +
                               "pass to put bloom in.");
                return false;
            }

            var so = new SerializedObject(renderer);
            SerializedProperty slot = so.FindProperty("postProcessData");
            if (slot == null)
            {
                Debug.LogError("[Bloom] RESULT: FAIL - the renderer has no postProcessData field; " +
                               "this URP version decides post-processing some other way.");
                return false;
            }

            if (slot.objectReferenceValue != data)
            {
                slot.objectReferenceValue = data;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(renderer);

                Debug.Log($"[Bloom] renderer post-processing resources {(on ? "attached" : "removed")}.");
            }

            return true;
        }

        static bool SetProfile(bool on)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_Profile);
            if (profile == null)
            {
                Debug.LogError($"[Bloom] RESULT: FAIL - no volume profile at {k_Profile}. It is " +
                               "the default volume for the whole project, set in URP's global " +
                               "settings, so without it there is nothing to configure.");
                return false;
            }

            if (profile.TryGet(out Bloom bloom))
            {
                bloom.active = true;
                bloom.intensity.overrideState = true;
                bloom.intensity.value = on ? k_BloomIntensity : 0f;
                bloom.threshold.overrideState = true;
                bloom.threshold.value = k_BloomThreshold;
                bloom.scatter.overrideState = true;
                bloom.scatter.value = k_BloomScatter;

                // Off: the high quality path is a second set of blur taps for a difference
                // nobody sees through a headset lens.
                bloom.highQualityFiltering.overrideState = true;
                bloom.highQualityFiltering.value = false;
            }
            else
            {
                Debug.LogWarning("[Bloom] the profile has no Bloom override; nothing to turn up.");
            }

            if (profile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping.active = true;
                tonemapping.mode.overrideState = true;

                // Neutral rather than ACES: ACES is a film look with its own contrast and
                // saturation, and this piece has two role colours that have to stay themselves.
                tonemapping.mode.value = on ? TonemappingMode.Neutral : TonemappingMode.None;
            }
            else
            {
                Debug.LogWarning("[Bloom] the profile has no Tonemapping override; values over " +
                                 "white will clip flat whatever the bloom does.");
            }

            EditorUtility.SetDirty(profile);
            return true;
        }

        /// <summary>
        /// Give the cues something over white to bloom, and put them back afterwards.
        ///
        /// THE ORBIT LAMPS, THE AIR AROUND THEM AND THE PRINTS, which are the three things in
        /// O-2 made of light. Multipliers of the builders' values rather than new numbers, so
        /// the staging keeps its relative brightnesses.
        /// </summary>
        static void SetSourceBrightness(bool on)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(k_OnboardingPrefab);
            if (root == null)
            {
                Debug.LogWarning($"[Bloom] no prefab at {k_OnboardingPrefab}; the cues keep " +
                                 "their current brightness and bloom will have little to catch.");
                return;
            }

            try
            {
                float factor = on ? k_HdrFactor : 1f;

                Transform orbits = root.transform.Find("Content/Orbits");
                if (orbits != null)
                {
                    foreach (Light lamp in orbits.GetComponentsInChildren<Light>(true))
                        lamp.intensity = k_LampIntensity * factor;

                    foreach (var glow in orbits.GetComponentsInChildren<CompositeBody.Experience.VolumetricPoint>(true))
                        glow.intensity = k_GlowIntensity * factor;
                }

                Transform prints = root.transform.Find("Content/Footprints");
                if (prints != null)
                {
                    float printFactor = on ? k_PrintHdrFactor : 1f;
                    foreach (var ring in prints.GetComponentsInChildren<CompositeBody.Experience.FootprintRing>(true))
                        ring.intensity = printFactor;
                }

                PrefabUtility.SaveAsPrefabAsset(root, k_OnboardingPrefab);
                Debug.Log($"[Bloom] orbit lamps at {k_LampIntensity * factor:0.00}, glows at " +
                          $"{k_GlowIntensity * factor:0.00}, prints emitting at " +
                          $"{(on ? k_PrintHdrFactor : 1f):0.00}x.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The flag without which none of the above runs.
        ///
        /// BASE CAMERAS ONLY. An overlay camera draws on top of a base camera's result and has
        /// no post-processing of its own to do; turning the flag on there costs a pass that
        /// resolves nothing.
        /// </summary>
        static int SetCameras(bool on)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(k_RigPrefab);
            if (root == null)
            {
                Debug.LogWarning($"[Bloom] no rig prefab at {k_RigPrefab}; cameras left alone.");
                return 0;
            }

            int changed = 0;
            try
            {
                foreach (var data in root.GetComponentsInChildren<UniversalAdditionalCameraData>(true))
                {
                    if (data.renderType != CameraRenderType.Base) continue;
                    if (data.renderPostProcessing == on) continue;

                    data.renderPostProcessing = on;
                    changed++;
                    Debug.Log($"[Bloom] '{data.name}': post-processing {(on ? "on" : "off")}.");
                }

                if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, k_RigPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return changed;
        }
    }
}
