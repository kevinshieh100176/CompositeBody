using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// The same view of O-2's cues -- the orbit lights and the footprint rings -- rendered with
    /// post-processing off, with bloom, and with the sources pushed into HDR, so the three can be
    /// compared side by side before anyone commits a fullscreen pass to a 90 Hz rig.
    ///
    /// IT RENDERS THE REAL THINGS AND THE REAL PROFILE. The volume profile it edits is the
    /// project's default volume, the one URP's global settings point at, so these are the values
    /// the show would actually run with rather than a demonstration set up to look good. Whatever
    /// it changes it puts back, so the project is left in the state it was found in.
    ///
    /// WHY HDR NEEDS ITS OWN PASS. Bloom only picks up what is over its threshold, and at a
    /// brightness of 1 a role colour's strongest channel only just reaches it: the prints barely
    /// glow and the glow that does appear is a single-channel fringe. Pushing the sources over
    /// white is what the floating-point buffer was for, and it is the difference between bloom
    /// doing something and bloom doing nothing.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.RenderBloomComparison.Run
    /// </summary>
    public static class RenderBloomComparison
    {
        const string k_Prefab = "Assets/_Scenes/Onboarding.prefab";
        const string k_Profile = "Assets/DefaultVolumeProfile.asset";
        const string k_OutDir = "Assets/_Renders/Bloom";
        const int k_Width = 900, k_Height = 700;

        /// <summary>How far round its lap the footprint band is, for every shot.</summary>
        const float k_Phase = 0.25f;

        /// <summary>Where the orbit lights are put, as a fraction of their loop.</summary>
        const float k_OrbitAt = 0.18f;

        /// <summary>
        /// One shot of whatever the project is set to right now -- no overrides, nothing
        /// restored afterwards. What the show would actually look like, as opposed to what the
        /// comparison set demonstrates.
        /// </summary>
        [MenuItem("Tools/Show/Bloom and HDR/Render Current Settings")]
        public static void RenderCurrent()
        {
            Debug.Log("[BloomRender] rendering the project as it is set right now...");
            Directory.CreateDirectory(k_OutDir);

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(k_Prefab);
            if (asset == null)
            {
                Debug.LogError($"[BloomRender] RESULT: FAIL - no prefab at {k_Prefab}.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                Transform content = instance.transform.Find("Content");
                Transform orbits = content.Find("Orbits");
                Transform prints = content.Find("Footprints");

                orbits.gameObject.SetActive(true);
                prints.gameObject.SetActive(true);
                Isolate(instance.transform, orbits);
                Isolate(instance.transform, prints);

                PlaceOrbits(orbits);
                foreach (FootprintRing ring in prints.GetComponentsInChildren<FootprintRing>(true))
                    ring.phase = k_Phase;

                GameObject floor = BuildFloor();
                Camera camera = BuildCamera();

                // The rig's own camera flag is what decides this in the show, so the render
                // camera copies it rather than forcing it either way.
                SetPostProcessing(camera, PostProcessingIsOn());

                bool ok = Shoot(camera, "bloom_current");

                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(camera.gameObject);
                Debug.Log("[BloomRender] RESULT: " + (ok ? "PASS" : "FAIL"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// The footprints at a ladder of emission values, with the project's bloom settings, so
        /// one of them can be picked by eye rather than by argument.
        ///
        /// ONLY THE PRINTS CHANGE between the shots. The orbits keep whatever they are set to,
        /// because a comparison where two things move at once answers neither question.
        /// </summary>
        [MenuItem("Tools/Show/Bloom and HDR/Render Footprint Emission Ladder")]
        public static void RenderEmissionLadder()
        {
            Debug.Log("[BloomRender] emission ladder...");
            Directory.CreateDirectory(k_OutDir);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_Profile);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(k_Prefab);
            if (profile == null || asset == null)
            {
                Debug.LogError("[BloomRender] RESULT: FAIL - need the volume profile and the prefab.");
                return;
            }

            profile.TryGet(out Bloom bloom);
            profile.TryGet(out Tonemapping tonemapping);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            int written = 0;

            try
            {
                Transform content = instance.transform.Find("Content");
                Transform orbits = content.Find("Orbits");
                Transform prints = content.Find("Footprints");

                orbits.gameObject.SetActive(true);
                prints.gameObject.SetActive(true);
                Isolate(instance.transform, orbits);
                Isolate(instance.transform, prints);

                PlaceOrbits(orbits);
                var rings = prints.GetComponentsInChildren<FootprintRing>(true);
                foreach (FootprintRing ring in rings) ring.phase = k_Phase;

                GameObject floor = BuildFloor();
                Camera camera = BuildCamera();
                LookAtPrints(camera);
                SetPostProcessing(camera, PostProcessingIsOn());

                foreach (float factor in new[] { 1f, 2f, 3f, 5f })
                {
                    foreach (FootprintRing ring in rings) ring.intensity = factor;
                    written += Shoot(camera, $"footprint_emission_{factor:0}x") ? 1 : 0;
                }

                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(camera.gameObject);

                Debug.Log($"[BloomRender] wrote {written} image(s) to {k_OutDir}");
                Debug.Log("[BloomRender] RESULT: " + (written == 4 ? "PASS" : "FAIL"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Low and close, so the prints fill the frame rather than the orbits.</summary>
        static void LookAtPrints(Camera cam)
        {
            cam.fieldOfView = 55f;
            cam.transform.position = new Vector3(0.35f, 1.05f, -2.5f);
            cam.transform.LookAt(new Vector3(0f, 0f, -0.55f));
        }

        /// <summary>Whether the rig's base camera is set to run post-processing.</summary>
        static bool PostProcessingIsOn()
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRMPT_XR_Origin_Setup.prefab");
            if (rig == null) return false;

            foreach (var data in rig.GetComponentsInChildren<UniversalAdditionalCameraData>(true))
            {
                if (data.renderType == CameraRenderType.Base && data.renderPostProcessing)
                    return true;
            }
            return false;
        }

        [MenuItem("Tools/Show/Bloom and HDR/Render Comparison")]
        public static void Run()
        {
            Debug.Log("[BloomRender] Starting...");
            Directory.CreateDirectory(k_OutDir);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_Profile);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(k_Prefab);
            if (profile == null || asset == null)
            {
                Debug.LogError("[BloomRender] RESULT: FAIL - need both the default volume " +
                               $"profile and {k_Prefab}.");
                return;
            }

            profile.TryGet(out Bloom bloom);
            profile.TryGet(out Tonemapping tonemapping);

            // Remembered so the project is handed back exactly as it was.
            float wasIntensity = bloom != null ? bloom.intensity.value : 0f;
            float wasThreshold = bloom != null ? bloom.threshold.value : 0.9f;
            TonemappingMode wasMode = tonemapping != null ? tonemapping.mode.value : TonemappingMode.None;

            // The renderer needs post-processing resources before any of this can run; without
            // them URP has no pass to put bloom in and every shot below would come out identical.
            var rendererAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ToggleShowBloom.RendererPath);
            var rendererSo = rendererAsset != null ? new SerializedObject(rendererAsset) : null;
            SerializedProperty ppSlot = rendererSo?.FindProperty("postProcessData");
            Object wasPostProcessData = ppSlot != null ? ppSlot.objectReferenceValue : null;

            ToggleShowBloom.SetRendererResources(true);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            int written = 0;

            try
            {
                Transform content = instance.transform.Find("Content");
                Transform orbits = content != null ? content.Find("Orbits") : null;
                Transform prints = content != null ? content.Find("Footprints") : null;

                if (orbits == null || prints == null)
                {
                    Debug.LogError("[BloomRender] RESULT: FAIL - the prefab needs Content/Orbits " +
                                   "and Content/Footprints.");
                    return;
                }

                // O-2 switches these on; nothing is driving that here.
                orbits.gameObject.SetActive(true);
                prints.gameObject.SetActive(true);

                // Everything else in the room goes, so what is being compared is these cues and
                // not the rest of the beat's lighting.
                Isolate(instance.transform, orbits);
                Isolate(instance.transform, prints);

                PlaceOrbits(orbits);
                var rings = prints.GetComponentsInChildren<FootprintRing>(true);
                foreach (FootprintRing ring in rings) ring.phase = k_Phase;

                GameObject floor = BuildFloor();
                Camera camera = BuildCamera();

                // 1. What the show looks like today.
                Apply(bloom, tonemapping, 0f, TonemappingMode.None);
                SetPostProcessing(camera, false);
                written += Shoot(camera, "bloom_1_off") ? 1 : 0;

                // 2. Bloom on, sources unchanged. The honest first question: does turning it on
                //    do anything at all to what is already there?
                Apply(bloom, tonemapping, 0.75f, TonemappingMode.Neutral);
                SetPostProcessing(camera, true);
                written += Shoot(camera, "bloom_2_on") ? 1 : 0;

                // 3. A modest push over white, which is where the role colours still survive.
                Apply(bloom, tonemapping, 0.6f, TonemappingMode.Neutral);
                SetHdr(orbits, rings, 1.6f);
                written += Shoot(camera, "bloom_3_hdr_mild") ? 1 : 0;

                // 4. More.
                Apply(bloom, tonemapping, 0.75f, TonemappingMode.Neutral);
                SetHdr(orbits, rings, 2.5f);
                written += Shoot(camera, "bloom_4_hdr_more") ? 1 : 0;

                // 5. Too much, on purpose. Worth having in the set: it is where the highlights
                //    desaturate and 代表色 purple turns pink, which is the cost nobody expects
                //    from a brightness slider.
                Apply(bloom, tonemapping, 1.3f, TonemappingMode.Neutral);
                SetHdr(orbits, rings, 4f);
                written += Shoot(camera, "bloom_5_hdr_too_much") ? 1 : 0;

                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(camera.gameObject);

                Debug.Log($"[BloomRender] wrote {written} image(s) to {k_OutDir}");
                Debug.Log("[BloomRender] RESULT: " + (written == 5 ? "PASS" : "FAIL"));
            }
            finally
            {
                Object.DestroyImmediate(instance);

                // And the renderer, so trying this out does not quietly leave a fullscreen pass
                // switched on in the project.
                if (ppSlot != null)
                {
                    ppSlot.objectReferenceValue = wasPostProcessData;
                    rendererSo.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(rendererAsset);
                }

                // Put the profile back, whatever happened above.
                Apply(bloom, tonemapping, wasIntensity, wasMode);
                if (bloom != null) bloom.threshold.value = wasThreshold;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                Debug.Log($"[BloomRender] profile restored: bloom {wasIntensity:0.00}, tonemapping {wasMode}.");
            }
        }

        static void Apply(Bloom bloom, Tonemapping tonemapping, float intensity, TonemappingMode mode)
        {
            if (bloom != null)
            {
                bloom.active = true;
                bloom.intensity.overrideState = true;
                bloom.intensity.value = intensity;
                bloom.threshold.overrideState = true;
                bloom.threshold.value = 0.9f;
            }

            if (tonemapping != null)
            {
                tonemapping.active = true;
                tonemapping.mode.overrideState = true;
                tonemapping.mode.value = mode;
            }
        }

        static void SetPostProcessing(Camera camera, bool on)
        {
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = on;
        }

        /// <summary>
        /// Push the sources over white: the lamps, the air around them and the prints.
        ///
        /// MULTIPLIERS, NOT FIXED VALUES, so the staging keeps its relative brightnesses -- the
        /// point is to see what headroom buys, not to re-light the beat.
        /// </summary>
        static void SetHdr(Transform orbits, FootprintRing[] rings, float factor)
        {
            foreach (Light lamp in orbits.GetComponentsInChildren<Light>(true))
                lamp.intensity = 1.9f * factor;

            foreach (VolumetricPoint glow in orbits.GetComponentsInChildren<VolumetricPoint>(true))
                glow.intensity = 0.55f * factor;

            foreach (FootprintRing ring in rings) ring.intensity = factor;
        }

        static void PlaceOrbits(Transform orbits)
        {
            // Driven by ExperienceClock at runtime, which does not advance here, so the movers
            // are put somewhere by hand -- the same entry point the stills renderer uses.
            foreach (LightOrbit orbit in orbits.GetComponentsInChildren<LightOrbit>(true))
                orbit.PreviewAt(k_OrbitAt);
        }

        static void Isolate(Transform root, Transform keep)
        {
            for (Transform at = keep; at != null && at != root; at = at.parent)
            {
                Transform parent = at.parent;
                if (parent == null) break;

                foreach (Transform sibling in parent)
                {
                    // Anything already kept stays kept: this is called once per thing to keep.
                    if (sibling == at) continue;
                    if (sibling.name is "Orbits" or "Footprints") continue;
                    sibling.gameObject.SetActive(false);
                }
            }
        }

        static GameObject BuildFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "~RenderFloor";
            floor.transform.localScale = Vector3.one * 1.2f;
            floor.transform.position = new Vector3(0f, -0.002f, 0f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var material = new Material(lit) { name = "~RenderFloor" };
                material.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.058f));
                material.SetFloat("_Smoothness", 0.25f);
                floor.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
            return floor;
        }

        /// <summary>
        /// Standing in the room looking across it, with both orbit paths and both rings in frame.
        /// </summary>
        static Camera BuildCamera()
        {
            var go = new GameObject("~BloomCamera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.008f, 0.008f, 0.012f);
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = 62f;
            go.transform.position = new Vector3(0.1f, 1.55f, -3.2f);
            go.transform.LookAt(new Vector3(0f, 0.9f, 0.2f));

            // HDR on the camera as well, or the render target is 8-bit and nothing over white
            // survives long enough to bloom.
            cam.allowHDR = true;
            return cam;
        }

        static bool Shoot(Camera camera, string name)
        {
            var rt = new RenderTexture(k_Width, k_Height, 24, RenderTextureFormat.DefaultHDR)
            {
                antiAliasing = 4
            };

            camera.targetTexture = rt;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var shot = new Texture2D(k_Width, k_Height, TextureFormat.RGBA32, false);
            shot.ReadPixels(new Rect(0, 0, k_Width, k_Height), 0, 0);
            shot.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;

            string path = Path.Combine(k_OutDir, name + ".png");
            File.WriteAllBytes(path, shot.EncodeToPNG());

            Object.DestroyImmediate(shot);
            rt.Release();
            Object.DestroyImmediate(rt);

            Debug.Log($"[BloomRender] {path}");
            return File.Exists(path);
        }
    }
}
