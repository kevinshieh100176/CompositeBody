using UnityEditor;
using UnityEngine;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Takes AERO's demo preset down to a thin haze for a 4x5 m black room.
    ///
    /// The material was copied from "Volumetric Fog PC (Gradients) 2", which is tuned for an
    /// outdoor scene with a sky, a horizon and a sun. Most of what makes that preset look good
    /// outdoors is what makes it look wrong here.
    ///
    /// THE BIGGEST SINGLE FIX IS MAX DISTANCE, not density. The preset marches 12 steps across
    /// 50 m. The room is five metres deep, so every sample but the first lands outside it -- the
    /// fog in the space you can actually walk through is decided by roughly one sample. That is
    /// the same mistake this project already learned once: VolumetricSpot's header records it as
    /// "4 steps across 20 m of range is one sample every five metres, and in a five-metre room
    /// the dither meant to hide that becomes the image". Pulling the range in costs nothing and
    /// buys back the entire sample budget.
    ///
    /// THE SECOND IS AMBIENT. Ambient light lifts the fog everywhere, which outdoors reads as sky
    /// bouncing around and in a black void reads as a grey veil over the blacks -- the void stops
    /// being a void. 合成肉身 is almost entirely black with a few coloured sources; the haze has to
    /// be something the lights reveal, not something that glows on its own.
    ///
    /// Re-runnable, and it only writes the values listed here -- anything tuned by hand that is
    /// not in this list is left alone.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.TuneFogToHaze.Run
    /// </summary>
    public static class TuneFogToHaze
    {
        const string k_Material = "Assets/Materials/OnboardingFog.mat";

        [MenuItem("Tools/Onboarding/Tune Fog To Haze")]
        public static void Run()
        {
            Debug.Log("[Haze] Starting...");

            var fog = AssetDatabase.LoadAssetAtPath<Material>(k_Material);
            if (fog == null)
            {
                Debug.LogError($"[Haze] RESULT: FAIL - no material at {k_Material}.");
                return;
            }

            // --- range and sampling ---------------------------------------------------------
            // 8 m covers the room and a little beyond, so the 12 steps land one every 0.67 m
            // instead of one every 4 m.
            Set(fog, "_Max_Distance", 8f);
            Set(fog, "_Steps", 12f);

            // --- how much air there is ------------------------------------------------------
            // Haze is a suggestion of air, not weather. Low, and uniform through the standing
            // volume rather than pooling at the floor.
            Set(fog, "_Density", 0.014f);
            Set(fog, "_Height", 4.0f);          // above head height, so it does not thin out at
            Set(fog, "_Height_Falloff", 0.35f); // the height the 光圈 and the orbits occupy

            // --- what the air does to light -------------------------------------------------
            // Forward scattering is what makes a light look like it is in air at all. Kept
            // strong, because it is the entire effect once density is this low.
            Set(fog, "_Anisotropy", 0.65f);
            Set(fog, "_Anisotropy_Blend", 0.5f);

            // --- the grey veil --------------------------------------------------------------
            // Ambient is what stops a black room being black. Nearly off: the haze should only
            // exist where a light finds it.
            Set(fog, "_Ambient_Light", 0.25f);
            Set(fog, "_Ambient_Light_Scale", 0.15f);

            // --- outdoor decoration, off ----------------------------------------------------
            // Height gradients and masks shape fog against a horizon. There is no horizon here,
            // and every one of them is a texture fetch per step.
            Set(fog, "_Enable_Height_Gradient", 0f);
            Set(fog, "_Enable_Height_Gradient_Texture", 0f);
            Set(fog, "_Enable_Height_Gradient_LUT", 0f);
            Set(fog, "_Enable_Height_Mask", 0f);
            Set(fog, "_Enable_Height_Mask_Texture", 0f);

            // The blur pass smears the scene through the fog, which is a thick-fog look and the
            // opposite of haze. It also samples a second fullscreen texture, which is both cost
            // and one more thing that has to be stereo-correct.
            Set(fog, "_Enable_Blur_Texture", 0f);
            Set(fog, "_Blur_Texture_Blend", 0f);

            // --- self-shadowing -------------------------------------------------------------
            // Fog shadowing itself is for thick fog. At this density it costs steps to show
            // nothing, so the step counts come down rather than the feature being torn out.
            Set(fog, "_Self_Shadow_Steps", 2f);
            Set(fog, "_Main_Light_Self_Shadow_Steps", 2f);
            Set(fog, "_Additional_Light_Self_Shadow_Steps", 3f);

            // --- keywords -------------------------------------------------------------------
            // The lights are the point: scattering and additional lights stay on. Ambient is
            // dimmed by value above rather than disabled, so it can be dialled back up.
            Enable(fog, "_ENABLE_ADDITIONAL_LIGHTS", true);
            Enable(fog, "_ENABLE_AMBIENT_LIGHT", true);
            Set(fog, "_Enable_Scattering", 1f);

            EditorUtility.SetDirty(fog);
            AssetDatabase.SaveAssetIfDirty(fog);

            Debug.Log($"[Haze] {k_Material}\n" +
                      "  range 50 -> 8 m, so 12 steps fall inside the room instead of past it\n" +
                      "  density 0.02 -> 0.014, ambient 2 -> 0.25 (the grey veil over the blacks)\n" +
                      "  height gradients, masks and the blur pass switched off\n" +
                      "[Haze] RESULT: PASS");
        }

        static void Set(Material m, string name, float value)
        {
            if (!m.HasProperty(name))
            {
                Debug.LogWarning($"[Haze] no property '{name}' on this material; skipped.");
                return;
            }
            m.SetFloat(name, value);
        }

        static void Enable(Material m, string keyword, bool on)
        {
            if (on) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }
    }
}
