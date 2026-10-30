using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Stands up a sample river in 1_1 and renders it, in one batch run.
    ///
    /// THE EXISTING CAMERA IS NOT TOUCHED. 1_1's camera sits at eye height, slightly pitched
    /// down -- someone placed it. The stills are shot from a temporary camera created and
    /// destroyed here instead, so judging the look never moves anything that was authored.
    ///
    /// LIGHTS ARE THE POINT, not decoration. This shader is roughly nine parts specular to one
    /// part diffuse, so in an unlit scene the river comes out near-black and reads as broken
    /// rather than subtle. The two point lights are the piece's own 代表色 -- purple for Player 1,
    /// yellow for Player 2, straight off PlayerRoleColors -- so the sample shows the lighting the
    /// river will actually live under rather than a studio setup that flatters it.
    ///
    /// Ambient is pushed almost to black for the same reason: 合成肉身 is a black void, and a
    /// default skybox ambient would wash the water with light it will never have in the piece.
    ///
    /// Idempotent. Re-running rebuilds the mesh and re-renders, but leaves the material, the
    /// camera and any hand-moved knots alone.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildSampleRiver.Run
    /// </summary>
    public static class BuildSampleRiver
    {
        const string k_Scene = "Assets/_Scenes/1_1.unity";
        const string k_LightRoot = "River Sample Lights";
        const string k_OutDir = "Assets/_Renders/River";
        const int k_Width = 1000, k_Height = 640;

        [MenuItem("Tools/River/3 - Build Sample River In 1_1")]
        public static void Run()
        {
            Debug.Log("[Sample] Starting...");

            // Textures, import settings and the material first: the river cannot be built without
            // a material, and BuildRiver refuses rather than baking magenta chunks.
            SetupRiver.Run();

            if (!File.Exists(k_Scene))
            {
                Debug.LogError($"[Sample] RESULT: FAIL - no scene at {k_Scene}.");
                return;
            }

            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.OpenScene(k_Scene, OpenSceneMode.Single);

            SetupLighting();
            EnsureLights();

            RiverSpline river = EnsureRiver();
            if (river == null)
            {
                Debug.LogError("[Sample] RESULT: FAIL - could not create the river.");
                return;
            }

            if (!BuildRiver.Build(river))
            {
                Debug.LogError("[Sample] RESULT: FAIL - the bake failed; see above.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, k_Scene);

            VerifyRiver.Run();
            RenderStills(river);

            Debug.Log("[Sample] RESULT: PASS");
        }

        /// <summary>
        /// Near-black flat ambient and no skybox contribution, which is the lighting this piece
        /// actually has. The water should be something the lights find, not something that glows.
        /// </summary>
        static void SetupLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.015f, 0.018f, 0.024f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
        }

        static void EnsureLights()
        {
            if (GameObject.Find(k_LightRoot) != null) return;

            var root = new GameObject(k_LightRoot);

            // Placed along the river rather than aimed at it, so the glints travel as the surface
            // moves under them instead of sitting in one spot.
            AddPoint(root, "Purple", new Vector3(-1.6f, 1.1f, 0.5f),
                     PlayerRoleColors.For(PlayerRole.Player1), 9f, 14f);
            AddPoint(root, "Yellow", new Vector3(2.1f, 1.4f, 7.5f),
                     PlayerRoleColors.For(PlayerRole.Player2), 11f, 18f);

            // One weak directional so the far stretch is not pitch black and the chunking can be
            // judged. Sample-scene convenience; the piece itself has no sun.
            var sun = new GameObject("Weak Key");
            sun.transform.SetParent(root.transform, false);
            sun.transform.rotation = Quaternion.Euler(28f, 18f, 0f);
            Light sunLight = sun.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.color = new Color(0.76f, 0.83f, 1f);
            sunLight.intensity = 0.14f;
            sunLight.shadows = LightShadows.None;

            Debug.Log("[Sample] added the sample lights (two 代表色 points and a weak key).");
        }

        static void AddPoint(GameObject parent, string name, Vector3 position, Color color,
                             float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = position;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;

            // The orbit lights in the onboarding run without shadows, and a shadowed point light
            // costs a cubemap render per frame per light.
            light.shadows = LightShadows.None;
        }

        static RiverSpline EnsureRiver()
        {
            var existing = Object.FindFirstObjectByType<RiverSpline>();
            if (existing != null)
            {
                // A river authored before the bed existed has an empty bed slot, and BuildRiver
                // now refuses without one. Fill it rather than making the person find out from
                // an error.
                SetupRiver.AssignBedMaterial(existing);

                Debug.Log($"[Sample] '{existing.name}' is already in the scene; " +
                          "its spline is left as authored.");
                return existing;
            }

            SetupRiver.CreateInScene();
            return Object.FindFirstObjectByType<RiverSpline>();
        }

        /// <summary>
        /// Renders the same two views twice: once with the realism terms switched off, once with
        /// them on.
        ///
        /// This is an honest A/B rather than a remembered one, and it is possible because every
        /// one of those terms is a parameter with a neutral value. Setting Fresnel strength to 0,
        /// diffuse to full and the distant normal floor to 1 reproduces the previous shader
        /// exactly, so the two images differ by the change and nothing else -- same mesh, same
        /// lights, same camera, same frame of the clock.
        /// </summary>
        [MenuItem("Tools/River/4 - Render Before And After")]
        public static void RenderComparison()
        {
            Debug.Log("[Compare] Starting...");

            EditorSceneManager.OpenScene(k_Scene, OpenSceneMode.Single);

            var river = Object.FindFirstObjectByType<RiverSpline>();
            if (river == null || river.material == null)
            {
                Debug.LogError("[Compare] RESULT: FAIL - no built river in the scene.");
                return;
            }

            Material fog = river.material;

            // Captured so the asset is left exactly as found; nothing here is saved.
            float specWas = fog.GetFloat("_SpecIntensity");
            float fresnelWas = fog.GetFloat("_FresnelStrength");
            float diffuseWas = fog.GetFloat("_DiffuseIntensity");
            float floorWas = fog.GetFloat("_NormalFadeFloor");

            Directory.CreateDirectory(k_OutDir);

            var rig = new GameObject("~RiverCompareCamera");
            Camera camera = rig.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 300f;
            camera.fieldOfView = 62f;

            // Two views on purpose. LOOKING DOWN is where Fresnel does its work and is also what
            // a standing player actually does with water at their feet -- the eye-level shot is
            // almost entirely grazing surface, where reflectance is high everywhere and the term
            // has little left to redistribute. Judging this change on the grazing view alone
            // would show only an overall dimming and miss the point of it.
            var downPos = new Vector3(0.7f, 1.62f, -1.2f);
            var downRot = new Vector3(54f, 12f, 0f);
            var alongPos = new Vector3(-0.6f, 1.55f, -6.5f);
            var alongRot = new Vector3(6f, 8f, 0f);

            Apply(fog, 3.5f, 0f, 1f, 1f);
            Shoot(camera, "before_looking_down", downPos, downRot);
            Shoot(camera, "before_eye_level", alongPos, alongRot);

            Apply(fog, specWas, fresnelWas, diffuseWas, floorWas);
            Shoot(camera, "after_looking_down", downPos, downRot);
            Shoot(camera, "after_eye_level", alongPos, alongRot);

            Object.DestroyImmediate(rig);

            Debug.Log($"[Compare] before: spec 3.5, fresnel off, diffuse full, no distance " +
                      $"flattening\n[Compare] after:  spec {specWas}, fresnel {fresnelWas}, " +
                      $"diffuse {diffuseWas}, distant normals {floorWas}\n" +
                      "[Compare] RESULT: PASS");
        }

        static void Apply(Material m, float spec, float fresnel, float diffuse, float normalFloor)
        {
            m.SetFloat("_SpecIntensity", spec);
            m.SetFloat("_FresnelStrength", fresnel);
            m.SetFloat("_DiffuseIntensity", diffuse);
            m.SetFloat("_NormalFadeFloor", normalFloor);
        }

        // --- stills -----------------------------------------------------------------------------

        static void RenderStills(RiverSpline river)
        {
            Directory.CreateDirectory(k_OutDir);

            var rig = new GameObject("~RiverStillCamera");
            Camera camera = rig.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 300f;
            camera.fieldOfView = 62f;

            int written = 0;
            written += Shoot(camera, "01_eye_level_downstream",
                             new Vector3(-0.6f, 1.55f, -6.5f), new Vector3(6f, 8f, 0f));
            written += Shoot(camera, "02_close_to_the_surface",
                             new Vector3(0.4f, 0.28f, -1.0f), new Vector3(3f, 22f, 0f));
            written += Shoot(camera, "03_high_wide_for_lod",
                             new Vector3(-5.0f, 7.5f, -9.0f), new Vector3(24f, 28f, 0f));

            // Looking down at the water in front of you, which is what a standing player does and
            // the only view where the cross-section is legible at all. From a grazing angle the
            // channel and the margins project onto almost the same few pixels.
            written += Shoot(camera, "04_looking_down_at_the_channel",
                             new Vector3(0.7f, 1.62f, -1.2f), new Vector3(54f, 12f, 0f));

            Object.DestroyImmediate(rig);

            Debug.Log($"[Sample] wrote {written} still(s) to {k_OutDir}.");
        }

        static int Shoot(Camera camera, string name, Vector3 position, Vector3 euler)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));

            var rt = new RenderTexture(k_Width, k_Height, 24, RenderTextureFormat.ARGB32)
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
            rt.Release();
            Object.DestroyImmediate(rt);

            string path = Path.Combine(k_OutDir, name + ".png");
            File.WriteAllBytes(path, shot.EncodeToPNG());
            Object.DestroyImmediate(shot);

            return 1;
        }
    }
}
