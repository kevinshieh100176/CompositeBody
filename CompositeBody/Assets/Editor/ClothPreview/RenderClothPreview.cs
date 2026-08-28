using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using CompositeBody.Avatar.Cloth;

namespace CompositeBody.Avatar.Cloth.EditorTools
{
    /// <summary>
    /// Batch-mode preview renderer for the cloth shader. Builds a throwaway scene framed to match
    /// the art reference (low camera looking slightly up, low horizon, pale sky over dry field),
    /// renders it offscreen and writes a PNG so the result can be compared against the reference
    /// image directly.
    ///
    /// Run with (note: no -nographics, rendering needs a graphics device):
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; -clothOut &lt;file.png&gt;
    ///   -executeMethod CompositeBody.Avatar.Cloth.EditorTools.RenderClothPreview.Run
    /// </summary>
    public static class RenderClothPreview
    {
        const string k_ShaderName = "CompositeBody/ClothDrape";
        const string k_MaterialPath = "Assets/Materials/ClothDrape.mat";

        const int k_Width = 696;
        const int k_Height = 819;

        public static void Run()
        {
            try
            {
                string outPath = GetArg("-clothOut") ?? Path.Combine(Directory.GetCurrentDirectory(), "ClothPreview.png");
                Debug.Log($"[ClothPreview] Rendering to: {outPath}");

                var shader = Shader.Find(k_ShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[ClothPreview] RESULT: FAIL - shader '{k_ShaderName}' not found.");
                    return;
                }
                if (ShaderUtil.ShaderHasError(shader))
                {
                    foreach (var m in ShaderUtil.GetShaderMessages(shader))
                        Debug.LogError($"[ClothPreview] Shader {m.severity} line {m.line}: {m.message}");
                    Debug.LogError("[ClothPreview] RESULT: FAIL - shader has compile errors.");
                    return;
                }
                Debug.Log("[ClothPreview] Shader compiled with no errors.");

                var material = LoadOrCreateMaterial(shader);

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                BuildScene(material, out Camera cam);

                RenderToFile(cam, outPath);

                Debug.Log("[ClothPreview] RESULT: PASS - image written.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClothPreview] RESULT: FAIL - {e}");
            }
        }

        static Material LoadOrCreateMaterial(Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(shader) { name = "ClothDrape" };
                AssetDatabase.CreateAsset(mat, k_MaterialPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[ClothPreview] Created material at {k_MaterialPath}");
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }
            return mat;
        }

        static void BuildScene(Material clothMaterial, out Camera cam)
        {
            // --- Figure -------------------------------------------------------------------
            var figureGO = new GameObject("DrapedFigure");
            var settings = new DrapedFigureSettings();
            figureGO.AddComponent<MeshFilter>().sharedMesh = DrapedFigureMesh.Build(settings);
            var figureRenderer = figureGO.AddComponent<MeshRenderer>();
            figureRenderer.sharedMaterial = clothMaterial;

            // --- Ground -------------------------------------------------------------------
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(60f, 1f, 60f);
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMat.SetColor("_BaseColor", Color.white);
            groundMat.SetFloat("_Smoothness", 0.04f);
            groundMat.SetTexture("_BaseMap", BuildFieldTexture(new Color(0.78f, 0.67f, 0.34f), new Color(0.38f, 0.31f, 0.15f)));
            groundMat.SetTextureScale("_BaseMap", new Vector2(38f, 38f));
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

            // Darker tilled soil in the immediate foreground, as in the reference.
            var soil = GameObject.CreatePrimitive(PrimitiveType.Plane);
            soil.name = "ForegroundSoil";
            // Kept as a narrow strip very close to the camera so it reads as foreground soil at
            // the bottom edge, rather than a band cutting across the middle of the field.
            soil.transform.localScale = new Vector3(9f, 1f, 0.14f);
            soil.transform.position = new Vector3(0f, 0.004f, -4.35f);
            var soilMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            soilMat.SetColor("_BaseColor", Color.white);
            soilMat.SetFloat("_Smoothness", 0.02f);
            soilMat.SetTexture("_BaseMap", BuildFieldTexture(new Color(0.24f, 0.19f, 0.13f), new Color(0.11f, 0.09f, 0.06f)));
            soilMat.SetTextureScale("_BaseMap", new Vector2(14f, 3f));
            soil.GetComponent<MeshRenderer>().sharedMaterial = soilMat;

            // --- Light --------------------------------------------------------------------
            var lightGO = new GameObject("Sun");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.Soft;
            lightGO.transform.rotation = Quaternion.Euler(32f, 28f, 0f);

            // --- Environment lighting -----------------------------------------------------
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.70f, 0.88f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.55f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.42f, 0.36f, 0.22f);
            RenderSettings.fog = false;

            // --- Camera -------------------------------------------------------------------
            // Placed low and pitched slightly up so the horizon sits in the lower quarter of
            // frame and the figure reads as looming, matching the reference composition.
            // Procedural sky gives the horizon-to-zenith gradient the reference has, which a
            // flat background colour can't reproduce.
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var skyMat = new Material(skyShader);
                skyMat.SetFloat("_SunSize", 0.02f);
                skyMat.SetFloat("_SunSizeConvergence", 5f);
                skyMat.SetFloat("_AtmosphereThickness", 0.72f);
                skyMat.SetColor("_SkyTint", new Color(0.42f, 0.58f, 0.80f));
                skyMat.SetColor("_GroundColor", new Color(0.58f, 0.52f, 0.36f));
                skyMat.SetFloat("_Exposure", 1.25f);
                RenderSettings.skybox = skyMat;
            }

            var camGO = new GameObject("PreviewCamera");
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<UniversalAdditionalCameraData>();
            cam.clearFlags = skyShader != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.60f, 0.75f, 0.87f);
            cam.fieldOfView = 26.2f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f;
            camGO.transform.position = new Vector3(0f, 0.40f, -5.0f);
            camGO.transform.rotation = Quaternion.Euler(-7.42f, 0f, 0f);
        }

        /// <summary>
        /// Builds a small tiling noise texture so the ground reads as dry grass / soil rather
        /// than a flat colour plane.
        /// </summary>
        static Texture2D BuildFieldTexture(Color light, Color dark)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat };
            var rng = new System.Random(20260822);

            var noise = new float[size, size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    noise[x, y] = (float)rng.NextDouble();

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Streak the noise vertically so it reads as blades rather than static.
                    float v = 0f;
                    for (int k = -3; k <= 3; k++)
                        v += noise[x, ((y + k) % size + size) % size];
                    v /= 7f;

                    float fine = noise[x, y] * 0.35f;
                    float t = Mathf.Clamp01(v * 0.75f + fine);
                    pixels[y * size + x] = Color.Lerp(dark, light, t);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Renders a camera offscreen to a PNG. Shared with the cloth scene builder.</summary>
        public static void RenderCameraToFile(Camera cam, string outPath) => RenderToFile(cam, outPath);

        static void RenderToFile(Camera cam, string outPath)
        {
            var rt = new RenderTexture(k_Width, k_Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 8
            };
            rt.Create();

            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(k_Width, k_Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, k_Width, k_Height), 0, 0);
            tex.Apply();

            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;

            byte[] png = tex.EncodeToPNG();
            string dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(outPath, png);

            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            Debug.Log($"[ClothPreview] Wrote {png.Length} bytes to {outPath}");
        }

        static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }
    }
}
