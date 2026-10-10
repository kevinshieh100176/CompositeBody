using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CompositeBody.PointClouds.EditorTools
{
    /// <summary>
    /// Renders a point-cloud figure at a series of dissolve values, so the effect can be looked
    /// at without putting on a headset.
    ///
    /// Worth a harness rather than eyeballing it in the editor because the interesting failures
    /// are quiet. Turbulence driven from the wrong clock still shimmers. A dissolve whose
    /// ordering comes out of a moving position still fizzes, but crawls instead of coming
    /// apart. A depth pass that displaces points differently from the colour pass tears holes
    /// in the body. All three look fine in a single still and wrong in a sequence, so the
    /// sequence is what gets rendered.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.PointClouds.EditorTools.RenderPointCloudPreview.Run
    ///   -cloud "Assets/_models/PointCloud/Figure_A.ply" -outDir "C:/out" [-pointSize 0.004]
    /// </summary>
    public static class RenderPointCloudPreview
    {
        const int k_Width = 540;
        const int k_Height = 900;

        /// <summary>
        /// What gets rendered. Glitch frames come in pairs at the same amount and different
        /// phases, because the thing to check is that the corruption *moves between held
        /// frames* rather than sitting still -- a single still cannot show that, and a glitch
        /// that never steps is the most likely way for this to be quietly broken.
        /// </summary>
        static readonly (float dissolve, float glitch, float rate, float phase, string label)[] k_Frames =
        {
            (0.00f, 0.00f,  0f, 0.0f, "00_idle"),
            (0.00f, 0.00f,  0f, 2.7f, "01_idle_later"),
            (0.25f, 0.00f,  0f, 4.0f, "02_dissolve_25"),
            (0.50f, 0.00f,  0f, 5.0f, "03_dissolve_50"),
            (0.75f, 0.00f,  0f, 6.0f, "04_dissolve_75"),
            (1.00f, 0.00f,  0f, 7.0f, "05_gone"),

            // Frozen: the still misplaced cloud. A light pass and a heavy one, because the
            // 真人 have to stay recognisable as people while reading as damaged.
            (0.00f, 0.18f,  0f, 0.0f, "06_glitch_frozen_light"),
            (0.00f, 0.45f,  0f, 0.0f, "07_glitch_frozen_seed9"),

            // Live: same amount, stepping. Two phases that land on different held frames.
            (0.00f, 0.45f, 11f, 0.00f, "08_glitch_live_a"),
            (0.00f, 0.45f, 11f, 0.19f, "09_glitch_live_b"),

            // A burst, which is what GlitchBurst drives.
            (0.00f, 0.90f, 11f, 0.37f, "10_glitch_burst"),

            // Glitch and dissolve together, which is S4-1.
            (0.35f, 0.55f, 11f, 0.55f, "11_glitch_and_dissolve"),
        };

        /// <summary>
        /// Camera distances the Fro variant is rendered from.
        ///
        /// Distance *is* the parameter for that shader -- the disintegration is driven by how
        /// close the viewer is -- so the only honest way to preview it is to move the camera
        /// and keep everything else fixed.
        /// </summary>
        static readonly (float metres, string label)[] k_FroDistances =
        {
            (4.0f, "fro_0_far"),
            (2.6f, "fro_1_edge"),
            (1.8f, "fro_2_closing"),
            (1.2f, "fro_3_near"),
            (0.8f, "fro_4_close"),
            (0.5f, "fro_5_inside"),
        };

        public static void Run()
        {
            if (HasFlag("-fro")) { RunFro(); return; }
            if (HasFlag("-lit")) { RunLit(); return; }

            string cloudPath = GetArg("-cloud");
            string outDir = GetArg("-outDir");
            if (string.IsNullOrEmpty(cloudPath) || string.IsNullOrEmpty(outDir))
            {
                Debug.LogError("[CloudPreview] RESULT: FAIL - pass -cloud <asset> -outDir <folder>.");
                return;
            }

            float pointSize = 0.004f;
            string sizeArg = GetArg("-pointSize");
            if (!string.IsNullOrEmpty(sizeArg)) float.TryParse(sizeArg, out pointSize);

            Mesh mesh = LoadMesh(cloudPath.Replace('\\', '/'));
            if (mesh == null)
            {
                Debug.LogError($"[CloudPreview] RESULT: FAIL - no Mesh at {cloudPath}.");
                return;
            }

            var shader = Shader.Find("CompositeBody/PointCloud");
            if (shader == null)
            {
                Debug.LogError("[CloudPreview] RESULT: FAIL - CompositeBody/PointCloud not found.");
                return;
            }

            Directory.CreateDirectory(outDir);
            var material = new Material(shader);
            material.SetFloat("_PointSize", pointSize);
            material.SetFloat("_Turbulence", 0.004f);
            material.SetFloat("_TurbulenceScale", 6f);
            material.SetFloat("_TurbulenceSpeed", 0.9f);
            material.SetFloat("_DissolveRadius", Mathf.Max(0.5f, mesh.bounds.size.y));
            material.SetVector("_DissolveFrom", new Vector4(
                mesh.bounds.center.x,
                mesh.bounds.min.y + mesh.bounds.size.y * 0.72f,   // chest height
                mesh.bounds.center.z, 0f));
            material.SetVector("_DissolveDir", new Vector4(0f, 1f, 0f, 0f));
            material.SetFloat("_DissolveDrift", 0.6f);
            material.SetFloat("_DissolveSpread", 0.08f);
            material.SetFloat("_DissolveScatter", 0.35f);

            BuildScene(mesh, material, out Camera cam, out Renderer cloudRenderer);

            material.SetFloat("_GlitchSlabs", 48f);
            material.SetFloat("_GlitchShift", 0.05f);
            material.SetFloat("_GlitchDropout", 0.25f);
            material.SetFloat("_GlitchScatter", 0.18f);
            material.SetFloat("_GlitchChroma", 0.5f);

            int written = 0;
            foreach ((float dissolve, float glitch, float rate, float phase, string label) in k_Frames)
            {
                material.SetFloat("_Dissolve", dissolve);
                material.SetFloat("_Phase", phase);
                material.SetFloat("_Glitch", glitch);
                material.SetFloat("_GlitchRate", rate);
                material.SetFloat("_GlitchSeed", label.EndsWith("seed9") ? 9f : 3f);
                cloudRenderer.enabled = dissolve < 0.999f;

                string path = Path.Combine(outDir, $"cloud_{label}.png");
                if (Render(cam, path)) written++;
                Debug.Log($"[CloudPreview] dissolve {dissolve:0.00} glitch {glitch:0.00} " +
                          $"rate {rate:0} phase {phase:0.00} -> {path}");
            }

            if (written == k_Frames.Length)
                Debug.Log($"[CloudPreview] {written} frames, {mesh.vertexCount:N0} points.\n" +
                          "[CloudPreview] RESULT: PASS");
            else
                Debug.LogError($"[CloudPreview] only {written} of {k_Frames.Length} frames written.\n" +
                               "[CloudPreview] RESULT: FAIL");
        }

        static void RunFro()
        {
            string cloudPath = (GetArg("-cloud") ?? "").Replace('\\', '/');
            string outDir = GetArg("-outDir");
            if (string.IsNullOrEmpty(cloudPath) || string.IsNullOrEmpty(outDir))
            {
                Debug.LogError("[CloudPreview] RESULT: FAIL - pass -cloud <asset> -outDir <folder>.");
                return;
            }

            // The cascade is ordered by density, which only exists if the importer measured it.
            // Turning it on here rather than asking the operator to remember keeps the preview
            // honest about what the shader is actually reading.
            var importer = AssetImporter.GetAtPath(cloudPath) as PlyImporter;
            if (importer != null)
            {
                var so = new SerializedObject(importer);
                SerializedProperty density = so.FindProperty("m_ComputeDensity");
                if (density != null && !density.boolValue)
                {
                    density.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    importer.SaveAndReimport();
                    Debug.Log("[CloudPreview] enabled density baking on the source cloud");
                }
            }

            Mesh mesh = LoadMesh(cloudPath);
            var shader = Shader.Find("CompositeBody/PointCloudFro");
            if (mesh == null || shader == null)
            {
                Debug.LogError($"[CloudPreview] RESULT: FAIL - mesh {(mesh == null ? "missing" : "ok")}, " +
                               $"CompositeBody/PointCloudFro {(shader == null ? "missing" : "ok")}.");
                return;
            }

            bool hasDensity = mesh.uv2 != null && mesh.uv2.Length == mesh.vertexCount;
            Debug.Log($"[CloudPreview] {mesh.vertexCount:N0} points, density baked: {hasDensity}");

            Directory.CreateDirectory(outDir);
            var material = new Material(shader);
            material.SetFloat("_PointSize", 0.0045f);
            material.SetFloat("_Intensity", 1.4f);
            material.SetFloat("_ApproachNear", 0.45f);
            material.SetFloat("_ApproachFar", 2.6f);
            material.SetFloat("_FallDistance", 1.1f);
            material.SetFloat("_Burn", 2.6f);
            material.SetFloat("_Phase", 3.2f);

            BuildScene(mesh, material, out Camera cam, out Renderer cloudRenderer);
            cloudRenderer.enabled = true;

            Bounds b = mesh.bounds;
            Vector3 target = b.center + Vector3.up * b.size.y * 0.10f;

            int written = 0;
            foreach ((float metres, string label) in k_FroDistances)
            {
                cam.transform.position = target + new Vector3(0.22f, 0.10f, -metres);
                cam.transform.LookAt(target);

                string path = Path.Combine(outDir, $"cloud_{label}.png");
                if (Render(cam, path)) written++;
                Debug.Log($"[CloudPreview] camera at {metres:0.0} m -> {path}");
            }

            if (written == k_FroDistances.Length)
                Debug.Log($"[CloudPreview] {written} frames.\n[CloudPreview] RESULT: PASS");
            else
                Debug.LogError($"[CloudPreview] only {written} of {k_FroDistances.Length}.\n" +
                               "[CloudPreview] RESULT: FAIL");
        }

        /// <summary>
        /// The lit variant, under the lighting the script actually calls for.
        ///
        /// Rendered in three setups because the whole claim of a white cloud is that the light
        /// decides its colour: a neutral key to show the form the scan's own albedo could not
        /// carry, then the same figure in the purple 光區 and in the yellow one, with nothing
        /// changed but the lamp.
        /// </summary>
        static void RunLit()
        {
            string cloudPath = (GetArg("-cloud") ?? "").Replace('\\', '/');
            string outDir = GetArg("-outDir");
            if (string.IsNullOrEmpty(cloudPath) || string.IsNullOrEmpty(outDir))
            {
                Debug.LogError("[CloudPreview] RESULT: FAIL - pass -cloud <asset> -outDir <folder>.");
                return;
            }

            var importer = AssetImporter.GetAtPath(cloudPath) as PlyImporter;
            if (importer != null)
            {
                var so = new SerializedObject(importer);
                SerializedProperty estimate = so.FindProperty("m_EstimateNormals");
                if (estimate != null && !estimate.boolValue)
                {
                    estimate.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    importer.SaveAndReimport();
                    Debug.Log("[CloudPreview] enabled normal estimation on the source cloud");
                }
            }

            Mesh mesh = LoadMesh(cloudPath);
            var shader = Shader.Find("CompositeBody/PointCloudLit");
            if (mesh == null || shader == null)
            {
                Debug.LogError($"[CloudPreview] RESULT: FAIL - mesh {(mesh == null ? "missing" : "ok")}, " +
                               $"CompositeBody/PointCloudLit {(shader == null ? "missing" : "ok")}.");
                return;
            }

            Vector3[] normals = mesh.normals;
            bool hasNormals = normals != null && normals.Length == mesh.vertexCount;
            // A cloud whose normals were never estimated carries 0,1,0 everywhere, which lights
            // as a flat lid. Checking the spread tells us they are real before we trust a render.
            int distinct = 0;
            if (hasNormals)
            {
                var seen = new HashSet<Vector3Int>();
                int stride = Mathf.Max(1, normals.Length / 5000);
                for (int i = 0; i < normals.Length; i += stride)
                {
                    seen.Add(new Vector3Int(Mathf.RoundToInt(normals[i].x * 8f),
                                            Mathf.RoundToInt(normals[i].y * 8f),
                                            Mathf.RoundToInt(normals[i].z * 8f)));
                }
                distinct = seen.Count;
            }
            Debug.Log($"[CloudPreview] {mesh.vertexCount:N0} points, normals: {hasNormals}, " +
                      $"{distinct} distinct directions in a 5k sample");
            if (distinct < 20)
                Debug.LogError("[CloudPreview] normals look degenerate -- the cloud will light flat.");

            Directory.CreateDirectory(outDir);
            var material = new Material(shader);
            material.SetFloat("_PointSize", 0.0045f);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_ScanColorMix", 0f);
            material.SetFloat("_Wrap", 0.35f);
            material.SetFloat("_AmbientBoost", 0.6f);
            material.SetFloat("_Exposure", 1.1f);
            material.SetFloat("_Phase", 2.0f);

            BuildScene(mesh, material, out Camera cam, out Renderer cloudRenderer);
            cloudRenderer.enabled = true;
            Bounds b = mesh.bounds;
            Vector3 centre = b.center;

            var keyGO = new GameObject("Key");
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.6f;
            key.color = new Color(0.95f, 0.96f, 1f);
            keyGO.transform.rotation = Quaternion.Euler(38f, -142f, 0f);

            var zoneGO = new GameObject("Zone");
            var zone = zoneGO.AddComponent<Light>();
            zone.type = LightType.Point;
            zone.range = 6f;
            zone.intensity = 9f;
            zoneGO.transform.position = centre + new Vector3(-0.7f, 0.25f, -0.55f);
            zoneGO.SetActive(false);

            var frames = new (bool keyOn, bool zoneOn, Color zoneColor, string label)[]
            {
                (true,  false, Color.white,                       "lit_0_white_key"),
                (false, true,  new Color(0.62f, 0.22f, 1.00f),    "lit_1_purple_zone"),
                (false, true,  new Color(1.00f, 0.78f, 0.12f),    "lit_2_yellow_zone"),
                (true,  true,  new Color(0.62f, 0.22f, 1.00f),    "lit_3_key_plus_purple"),
            };

            int written = 0;
            foreach ((bool keyOn, bool zoneOn, Color zoneColor, string label) in frames)
            {
                keyGO.SetActive(keyOn);
                zoneGO.SetActive(zoneOn);
                zone.color = zoneColor;

                string path = Path.Combine(outDir, $"cloud_{label}.png");
                if (Render(cam, path)) written++;
                Debug.Log($"[CloudPreview] key {keyOn} zone {zoneOn} -> {path}");
            }

            if (written == frames.Length)
                Debug.Log($"[CloudPreview] {written} frames.\n[CloudPreview] RESULT: PASS");
            else
                Debug.LogError($"[CloudPreview] only {written} of {frames.Length}.\n" +
                               "[CloudPreview] RESULT: FAIL");
        }

        static bool HasFlag(string name)
        {
            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (a == name) return true;
            }
            return false;
        }

        static Mesh LoadMesh(string path)
        {
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is Mesh m) return m;
            }
            return null;
        }

        static void BuildScene(Mesh mesh, Material material, out Camera cam, out Renderer cloudRenderer)
        {
            var root = new GameObject("CloudPreview");

            var cloud = new GameObject("Cloud");
            cloud.transform.SetParent(root.transform);
            cloud.AddComponent<MeshFilter>().sharedMesh = mesh;
            cloudRenderer = cloud.AddComponent<MeshRenderer>();
            cloudRenderer.sharedMaterial = material;

            Bounds b = mesh.bounds;

            var camGO = new GameObject("PreviewCamera");
            camGO.transform.SetParent(root.transform);
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<UniversalAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Not black: a dark figure on black shows nothing, and the whole point of the
            // preview is to see where the points are.
            cam.backgroundColor = new Color(0.09f, 0.10f, 0.12f, 1f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;

            // Framed to leave headroom above, because the dissolve drifts upward and the
            // interesting part of the effect happens off the top of a tightly framed shot.
            float distance = b.size.y * 1.9f;
            Vector3 target = b.center + Vector3.up * b.size.y * 0.18f;
            camGO.transform.position = target + new Vector3(0.35f, 0.12f, -distance);
            camGO.transform.LookAt(target);
        }

        static bool Render(Camera cam, string outPath)
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

                var tex = new Texture2D(k_Width, k_Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, k_Width, k_Height), 0, 0);
                tex.Apply();

                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[CloudPreview] {outPath}: {e.Message}");
                return false;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = previous;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
