using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CompositeBody.PointClouds.EditorTools
{
    /// <summary>
    /// Checks that the idle motion actually reaches the shader by the route the scene uses.
    ///
    /// <see cref="PointCloudFigure"/> does not write to the material. It writes to a
    /// <see cref="MaterialPropertyBlock"/> on the renderer, because the material is a shared
    /// asset and driving it directly would commit the current frame's phase to the project.
    /// That is the right call, and it is also a route with a failure mode no still frame can
    /// show: if a property block stops reaching the shader -- a batcher that does not read one,
    /// a property the shader never declared, a name that drifted -- the figure keeps rendering
    /// at whatever the material says and simply stands still. Nothing throws, nothing logs, and
    /// the effect looks like it was authored too subtly.
    ///
    /// So render the same cloud at two phases twice over: once with the phase set on the
    /// material, which is the control, and once through a property block, which is what the
    /// scene actually does. Both must move. If only the control moves, the drive is broken and
    /// no amount of amplitude will fix it.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.PointClouds.EditorTools.VerifyFigureDrive.Run
    ///   -cloud "Assets/_models/Pointcloud/Figure_A5.ply"
    ///   [-material "Assets/Materials/O0FigureLit.mat"] [-outDir "C:/out"]
    /// </summary>
    public static class VerifyFigureDrive
    {
        const int k_Width = 420;
        const int k_Height = 700;

        // Two phases far enough apart that the slowest term in the flow has turned over, and
        // close enough that this is still the same idle motion rather than a different pose.
        const float k_PhaseA = 0f;
        const float k_PhaseB = 2.7f;

        // A point that moved shifts a pixel by far more than this; compression and float noise
        // do not reach it.
        const int k_ChannelThreshold = 8;

        // Below this the cloud has not meaningfully moved. The figure covers roughly a third of
        // the frame and the interior of a point cloud barely changes when it drifts a
        // centimetre -- it is the silhouette that moves -- so the bar is deliberately low. The
        // failure this is looking for is zero, not small.
        const float k_MinMovedFraction = 0.01f;

        public static void Run()
        {
            string cloudPath = (GetArg("-cloud") ?? "Assets/_models/Pointcloud/Figure_A5.ply")
                .Replace('\\', '/');
            string materialPath = (GetArg("-material") ?? "Assets/Materials/O0FigureLit.mat")
                .Replace('\\', '/');
            string outDir = GetArg("-outDir");

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(cloudPath);
            var source = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mesh == null || source == null)
            {
                Debug.LogError($"[FigureDrive] RESULT: FAIL - mesh at {cloudPath} " +
                               $"{(mesh == null ? "missing" : "ok")}, material at {materialPath} " +
                               $"{(source == null ? "missing" : "ok")}.");
                return;
            }

            var material = new Material(source);
            Debug.Log($"[FigureDrive] {mesh.vertexCount:N0} points, shader {material.shader.name}, " +
                      $"_Turbulence {material.GetFloat("_Turbulence"):0.000} m, " +
                      $"_TurbulenceDrift {material.GetFloat("_TurbulenceDrift"):0.00}");

            BuildScene(mesh, material, out Camera cam, out Renderer cloudRenderer);

            // Control: the phase on the material, which is the route the preview harness uses
            // and the one already known to work.
            cloudRenderer.SetPropertyBlock(null);
            material.SetFloat("_Phase", k_PhaseA);
            Color32[] matA = Grab(cam);
            material.SetFloat("_Phase", k_PhaseB);
            Color32[] matB = Grab(cam);
            float viaMaterial = MovedFraction(matA, matB, out int matCovered);

            // The real route: everything PointCloudFigure writes, written the way it writes it.
            material.SetFloat("_Phase", k_PhaseA);
            var block = new MaterialPropertyBlock();
            Color32[] mpbA = GrabWithBlock(cam, cloudRenderer, block, material, k_PhaseA);
            Color32[] mpbB = GrabWithBlock(cam, cloudRenderer, block, material, k_PhaseB);
            float viaBlock = MovedFraction(mpbA, mpbB, out int mpbCovered);

            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
                Save(matA, Path.Combine(outDir, "material_phase_a.png"));
                Save(matB, Path.Combine(outDir, "material_phase_b.png"));
                Save(mpbA, Path.Combine(outDir, "block_phase_a.png"));
                Save(mpbB, Path.Combine(outDir, "block_phase_b.png"));
                Debug.Log($"[FigureDrive] frames written to {outDir}");
            }

            Object.DestroyImmediate(cam.transform.root.gameObject);

            Debug.Log($"[FigureDrive] phase on the material:       " +
                      $"{100f * viaMaterial:0.00}% of {matCovered:N0} covered pixels moved");
            Debug.Log($"[FigureDrive] phase through a property block: " +
                      $"{100f * viaBlock:0.00}% of {mpbCovered:N0} covered pixels moved");

            bool materialMoves = viaMaterial >= k_MinMovedFraction;
            bool blockMoves = viaBlock >= k_MinMovedFraction;

            if (materialMoves && blockMoves)
            {
                Debug.Log("[FigureDrive] both routes animate.\n[FigureDrive] RESULT: PASS");
            }
            else if (materialMoves)
            {
                Debug.LogError(
                    "[FigureDrive] the shader animates when the phase is on the material but NOT " +
                    "through a property block, which is the route PointCloudFigure uses. The " +
                    "figure will stand still in the scene however large the amplitude is.\n" +
                    "[FigureDrive] RESULT: FAIL");
            }
            else
            {
                Debug.LogError(
                    "[FigureDrive] the shader does not animate on either route, so the idle " +
                    "motion is off in the shader or the material rather than in the drive.\n" +
                    "[FigureDrive] RESULT: FAIL");
            }
        }

        /// <summary>Everything <see cref="PointCloudFigure.Apply"/> writes, written the same way.</summary>
        static Color32[] GrabWithBlock(Camera cam, Renderer renderer, MaterialPropertyBlock block,
                                       Material material, float phase)
        {
            block.SetFloat("_Phase", phase);
            block.SetFloat("_Turbulence", material.GetFloat("_Turbulence"));
            block.SetFloat("_TurbulenceDrift", material.GetFloat("_TurbulenceDrift"));
            block.SetFloat("_TurbulenceScale", material.GetFloat("_TurbulenceScale"));
            block.SetFloat("_TurbulenceSpeed", material.GetFloat("_TurbulenceSpeed"));
            renderer.SetPropertyBlock(block);
            return Grab(cam);
        }

        static float MovedFraction(Color32[] a, Color32[] b, out int covered)
        {
            covered = 0;
            int moved = 0;
            for (int i = 0; i < a.Length; i++)
            {
                // "Covered" is anywhere either frame has the figure rather than the backdrop.
                bool ink = Brightness(a[i]) > 40 || Brightness(b[i]) > 40;
                if (!ink) continue;
                covered++;
                int d = Mathf.Max(Mathf.Abs(a[i].r - b[i].r),
                        Mathf.Max(Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b)));
                if (d > k_ChannelThreshold) moved++;
            }
            return covered == 0 ? 0f : (float)moved / covered;
        }

        static int Brightness(Color32 c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

        static Color32[] Grab(Camera cam)
        {
            var rt = new RenderTexture(k_Width, k_Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1
            };
            RenderTexture previous = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(k_Width, k_Height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, k_Width, k_Height), 0, 0);
                tex.Apply();
                Color32[] pixels = tex.GetPixels32();
                Object.DestroyImmediate(tex);
                return pixels;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = previous;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static void Save(Color32[] pixels, string path)
        {
            var tex = new Texture2D(k_Width, k_Height, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void BuildScene(Mesh mesh, Material material, out Camera cam, out Renderer cloudRenderer)
        {
            var root = new GameObject("FigureDrive");

            var cloud = new GameObject("Cloud");
            cloud.transform.SetParent(root.transform);
            cloud.AddComponent<MeshFilter>().sharedMesh = mesh;
            cloudRenderer = cloud.AddComponent<MeshRenderer>();
            cloudRenderer.sharedMaterial = material;

            // The lit shader needs something to light it, or every frame is the same black
            // silhouette and nothing can be seen to move.
            var lightGO = new GameObject("Key");
            lightGO.transform.SetParent(root.transform);
            var key = lightGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 2.2f;
            lightGO.transform.rotation = Quaternion.Euler(28f, 150f, 0f);

            Bounds b = mesh.bounds;
            var camGO = new GameObject("DriveCamera");
            camGO.transform.SetParent(root.transform);
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<UniversalAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.10f, 0.12f, 1f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;

            // Closer than the dissolve preview frames it. A centimetre of drift is a couple of
            // pixels at full-figure framing, and this has to measure it.
            float distance = b.size.y * 1.35f;
            Vector3 target = b.center + Vector3.up * b.size.y * 0.12f;
            camGO.transform.position = target + new Vector3(0.25f, 0.08f, -distance);
            camGO.transform.LookAt(target);
        }

        static string GetArg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
