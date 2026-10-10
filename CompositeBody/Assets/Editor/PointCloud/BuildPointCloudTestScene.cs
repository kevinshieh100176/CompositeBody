using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CompositeBody.PointClouds;

namespace CompositeBody.PointClouds.EditorTools
{
    /// <summary>
    /// Builds one scene holding every point-cloud look side by side, so they can be compared
    /// rather than remembered.
    ///
    /// Four stations, left to right: the scan in its own colour, the white lit version standing
    /// between the two 光區 lamps, the Fro approach variant, and the earlier sparse extraction so
    /// the density difference is visible next to the rest. All four read the same cloud except
    /// the last, and all four are driven by <see cref="PointCloudShowcase"/>, so pressing play
    /// cycles each through holding, stuttering, coming apart and forming again while the camera
    /// dollies past the Fro station.
    ///
    /// The builder sets the importer settings the scene depends on -- Quads topology, a point
    /// budget, estimated normals, measured density -- rather than assuming whoever opens it
    /// remembers to. Those settings live in the .ply's .meta and are shared by anything else
    /// referencing the same asset, so the choices are logged on every run.
    ///
    /// Quads rather than Points: the three shaders differ in how they shade a point's own area,
    /// which a one-pixel point cannot show. That costs four vertices per point, which is why
    /// there is a budget.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.PointClouds.EditorTools.BuildPointCloudTestScene.Run
    ///   [-maxPoints 250000] [-fullDensity]
    /// </summary>
    public static class BuildPointCloudTestScene
    {
        const string k_ScenePath = "Assets/_Scenes/PointCloudTest.unity";
        const string k_CloudDir = "Assets/_models/PointCloud";

        const string k_MainCloud = k_CloudDir + "/Figure_A.ply";
        const string k_SparseCloud = k_CloudDir + "/person_unity.ply";

        const string k_MatScan = "Assets/Materials/PointCloudScan.mat";
        const string k_MatLit = "Assets/Materials/PointCloudLit.mat";
        const string k_MatFro = "Assets/Materials/PointCloudFro.mat";
        const string k_MatSparse = "Assets/Materials/PointCloudSparse.mat";

        /// <summary>
        /// Points kept per cloud. 250k over a 1.7 m figure is about 1.6 mm apart, which still
        /// reads solid, and at four vertices each keeps the scene near a million per station
        /// instead of 2.7. Pass -fullDensity to compare at the real count.
        /// </summary>
        const int k_DefaultMaxPoints = 250_000;

        const float k_Spacing = 1.5f;

        public static void Run()
        {
            Debug.Log("[CloudScene] Starting...");

            var scan = Shader.Find("CompositeBody/PointCloud");
            var lit = Shader.Find("CompositeBody/PointCloudLit");
            var fro = Shader.Find("CompositeBody/PointCloudFro");
            if (!Check(scan, "CompositeBody/PointCloud") ||
                !Check(lit, "CompositeBody/PointCloudLit") ||
                !Check(fro, "CompositeBody/PointCloudFro"))
                return;

            int maxPoints = k_DefaultMaxPoints;
            if (HasFlag("-fullDensity")) maxPoints = 0;
            string arg = GetArg("-maxPoints");
            if (!string.IsNullOrEmpty(arg) && int.TryParse(arg, out int parsed)) maxPoints = parsed;

            if (!File.Exists(k_MainCloud))
            {
                Debug.LogError($"[CloudScene] RESULT: FAIL - {k_MainCloud} is missing. " +
                               "Put a cleaned .ply there first.");
                return;
            }

            Mesh mainMesh = PrepareCloud(k_MainCloud, maxPoints);
            if (mainMesh == null)
            {
                Debug.LogError($"[CloudScene] RESULT: FAIL - {k_MainCloud} produced no mesh.");
                return;
            }
            Mesh sparseMesh = File.Exists(k_SparseCloud) ? PrepareCloud(k_SparseCloud, maxPoints) : null;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildEnvironment(mainMesh);

            var figures = new List<PointCloudFigure>();
            float x = -1.5f * k_Spacing;

            figures.Add(BuildStation(mainMesh, ScanMaterial(scan), "1 SCAN COLOUR",
                "opaque, the capture's own colour", new Vector3(x, 0f, 0f)));
            x += k_Spacing;

            PointCloudFigure litFigure = BuildStation(mainMesh, LitMaterial(lit), "2 WHITE / LIT",
                "white albedo, the lamps decide the colour", new Vector3(x, 0f, 0f));
            figures.Add(litFigure);
            BuildZoneLamps(new Vector3(x, 0f, 0f));
            x += k_Spacing;

            Vector3 froPosition = new(x, 0f, 0f);
            figures.Add(BuildStation(mainMesh, FroMaterial(fro), "3 FRO / APPROACH",
                "additive; falls apart as you near it", froPosition));
            x += k_Spacing;

            if (sparseMesh != null)
            {
                figures.Add(BuildStation(sparseMesh, SparseMaterial(lit), "4 SPARSE SCAN",
                    $"{sparseMesh.vertexCount / 4:N0} pts for comparison", new Vector3(x, 0f, 0f)));
            }

            Camera cam = BuildCamera();
            BuildShowcase(figures, cam, froPosition);

            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();

            long verts = 0;
            foreach (PointCloudFigure f in figures)
            {
                var mf = f.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) verts += mf.sharedMesh.vertexCount;
            }

            Debug.Log($"[CloudScene] {k_ScenePath}: {figures.Count} stations, {verts:N0} vertices total " +
                      $"({verts / 4:N0} points).");
            Debug.Log("[CloudScene] RESULT: PASS");
        }

        /// <summary>
        /// Forces the importer settings the scene needs and returns the resulting mesh.
        ///
        /// Normals because the lit shader has nothing to light without them, density because
        /// the Fro cascade orders itself by it, Quads because a one-pixel point cannot show
        /// what separates these three shaders.
        /// </summary>
        static Mesh PrepareCloud(string path, int maxPoints)
        {
            var importer = AssetImporter.GetAtPath(path) as PlyImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[CloudScene] {path} is not handled by PlyImporter; using it as-is.");
                return LoadMesh(path);
            }

            var so = new SerializedObject(importer);
            bool changed = false;
            changed |= SetEnum(so, "m_Topology", (int)PlyImporter.Topology.Quads);
            changed |= SetBool(so, "m_EstimateNormals", true);
            changed |= SetBool(so, "m_ComputeDensity", true);
            changed |= SetInt(so, "m_MaxPoints", Mathf.Max(0, maxPoints));

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                importer.SaveAndReimport();
                Debug.Log($"[CloudScene] {Path.GetFileName(path)}: set Quads, normals, density, " +
                          $"max points {(maxPoints > 0 ? maxPoints.ToString("N0") : "all")}");
            }
            return LoadMesh(path);
        }

        static bool SetEnum(SerializedObject so, string name, int value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null || p.enumValueIndex == value) return false;
            p.enumValueIndex = value;
            return true;
        }

        static bool SetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null || p.boolValue == value) return false;
            p.boolValue = value;
            return true;
        }

        static bool SetInt(SerializedObject so, string name, int value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null || p.intValue == value) return false;
            p.intValue = value;
            return true;
        }

        static Mesh LoadMesh(string path)
        {
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is Mesh m) return m;
            }
            return null;
        }

        static PointCloudFigure BuildStation(Mesh mesh, Material material, string title,
                                             string subtitle, Vector3 position)
        {
            var root = new GameObject(title.Replace(' ', '_'));
            root.transform.position = position;
            // Turned off square. The scan is a standing figure captured facing along X, so
            // head-on it is a profile and the form reads as a silhouette; three-quarters puts
            // the shoulder and the near arm in view, which is what the shading is for.
            root.transform.rotation = Quaternion.Euler(0f, 34f, 0f);

            var cloud = new GameObject("Cloud");
            cloud.transform.SetParent(root.transform, false);
            cloud.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = cloud.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            // A cloud whose bounds are its undisplaced mesh gets culled the moment a dissolve
            // carries its points outside them, which looks like the effect cutting out.
            renderer.localBounds = Grow(mesh.bounds, 2.5f);

            var figure = root.AddComponent<PointCloudFigure>();
            var fso = new SerializedObject(figure);
            fso.FindProperty("m_Cloud").objectReferenceValue = renderer;
            fso.FindProperty("m_DissolveFrom").vector3Value =
                new Vector3(0f, mesh.bounds.min.y + mesh.bounds.size.y * 0.72f, 0f);
            fso.FindProperty("m_DissolveRadius").floatValue = Mathf.Max(0.6f, mesh.bounds.size.y);
            fso.FindProperty("m_RevealRadius").floatValue = Mathf.Max(0.6f, mesh.bounds.size.y);
            fso.ApplyModifiedPropertiesWithoutUndo();

            Label(root.transform, title, subtitle, mesh.bounds.max.y + 0.22f);
            return figure;
        }

        static Bounds Grow(Bounds b, float metres)
        {
            b.Expand(metres);
            return b;
        }

        /// <summary>
        /// ASCII labels, drawn with the built-in font.
        ///
        /// Deliberately not the script's own Chinese titles: the legacy font carries no CJK
        /// glyphs and renders them as blanks, the same trap StoryBeats.DisplayName documents
        /// for the staff panel. A label nobody can read is worse than an English one.
        /// </summary>
        static void Label(Transform parent, string title, string subtitle, float height)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            // Identity, not flipped. The camera sits on -Z looking toward +Z, and a TextMesh
            // faces its own +Z, so an unrotated label is already the right way round; the 180
            // this started with turned every caption into mirror writing. Set in world space
            // so the station's yaw does not carry into it.
            go.transform.rotation = Quaternion.identity;

            var text = go.AddComponent<TextMesh>();
            text.text = $"{title}\n{subtitle}";
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 48;
            text.characterSize = 0.014f;
            text.color = new Color(0.72f, 0.75f, 0.82f);
        }

        static void BuildEnvironment(Mesh mesh)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.035f, 0.040f, 0.050f);
            RenderSettings.fogDensity = 0.028f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.085f, 0.095f, 0.115f);
            RenderSettings.ambientEquatorColor = new Color(0.050f, 0.055f, 0.068f);
            RenderSettings.ambientGroundColor = new Color(0.016f, 0.018f, 0.024f);

            var keyGO = new GameObject("Key Light");
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.5f;
            key.color = new Color(0.94f, 0.96f, 1f);
            keyGO.transform.rotation = Quaternion.Euler(38f, -142f, 0f);

            // A floor, because a figure with feet at y=0 floating in black has no scale and no
            // up. Lit, so it also shows the zone lamps spilling.
            var floorShader = Shader.Find("Universal Render Pipeline/Lit");
            if (floorShader == null) return;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 2.2f;
            var floorMat = new Material(floorShader) { name = "Floor" };
            floorMat.SetColor("_BaseColor", new Color(0.055f, 0.060f, 0.072f));
            floorMat.SetFloat("_Smoothness", 0.22f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
        }

        /// <summary>The two 光區 lamps, so the white station shows what the zones do to it.</summary>
        static void BuildZoneLamps(Vector3 centre)
        {
            var purpleGO = new GameObject("Zone Purple");
            var purple = purpleGO.AddComponent<Light>();
            purple.type = LightType.Point;
            purple.color = new Color(0.62f, 0.22f, 1.00f);
            // Dim, and with a range that stops short of the next station. At the first
            // settings these two blew the white figure to flat white and spilled far enough
            // right to light the sparse station gold, so the comparison was between one
            // overexposed figure and one wrongly coloured one.
            purple.intensity = 4.5f;
            purple.range = 2.1f;
            purpleGO.transform.position = centre + new Vector3(-0.55f, 0.9f, -0.45f);

            var yellowGO = new GameObject("Zone Yellow");
            var yellow = yellowGO.AddComponent<Light>();
            yellow.type = LightType.Point;
            yellow.color = new Color(1.00f, 0.78f, 0.12f);
            yellow.intensity = 4.5f;
            yellow.range = 2.1f;
            yellowGO.transform.position = centre + new Vector3(0.55f, 0.9f, -0.45f);
        }

        static Camera BuildCamera()
        {
            var go = new GameObject("Preview Camera");
            var cam = go.AddComponent<Camera>();
            go.AddComponent<UniversalAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.035f, 0.040f, 0.050f);
            cam.fieldOfView = 48f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            go.tag = "MainCamera";

            go.transform.position = new Vector3(0f, 1.05f, -4.4f);
            go.transform.LookAt(new Vector3(0f, 0.85f, 0f));
            return cam;
        }

        static void BuildShowcase(List<PointCloudFigure> figures, Camera cam, Vector3 froPosition)
        {
            var go = new GameObject("Showcase");
            var showcase = go.AddComponent<PointCloudShowcase>();

            var dolly = new GameObject("Dolly Target");
            dolly.transform.position = froPosition + new Vector3(0f, 0.9f, 0f);

            var so = new SerializedObject(showcase);
            SerializedProperty list = so.FindProperty("m_Figures");
            list.arraySize = figures.Count;
            for (int i = 0; i < figures.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = figures[i];

            so.FindProperty("m_Camera").objectReferenceValue = cam.transform;
            so.FindProperty("m_DollyTarget").objectReferenceValue = dolly.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material ScanMaterial(Shader shader)
        {
            Material mat = LoadOrCreate(k_MatScan, shader);
            mat.SetFloat("_PointSize", 0.0045f);
            mat.SetFloat("_Intensity", 1.3f);
            mat.SetFloat("_Turbulence", 0.003f);
            mat.SetFloat("_GlitchRate", 11f);
            return Save(mat);
        }

        static Material LitMaterial(Shader shader)
        {
            Material mat = LoadOrCreate(k_MatLit, shader);
            mat.SetFloat("_PointSize", 0.0045f);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_ScanColorMix", 0f);
            mat.SetFloat("_Wrap", 0.35f);
            mat.SetFloat("_AmbientBoost", 0.35f);
            // Under a key plus two zone lamps, white albedo at exposure 1.1 clips and the
            // zone colour survives only at the silhouette. The point of this station is that
            // the lamps tint it, so it has to sit below white.
            mat.SetFloat("_Exposure", 0.75f);
            mat.SetFloat("_Turbulence", 0.003f);
            mat.SetFloat("_GlitchRate", 11f);
            return Save(mat);
        }

        static Material FroMaterial(Shader shader)
        {
            Material mat = LoadOrCreate(k_MatFro, shader);
            mat.SetFloat("_PointSize", 0.0045f);
            mat.SetFloat("_Intensity", 1.4f);
            mat.SetFloat("_ApproachNear", 0.45f);
            mat.SetFloat("_ApproachFar", 2.6f);
            mat.SetFloat("_FallDistance", 1.1f);
            mat.SetFloat("_Burn", 2.6f);
            return Save(mat);
        }

        static Material SparseMaterial(Shader litShader)
        {
            Material mat = LoadOrCreate(k_MatSparse, litShader);
            // Bigger points, because the comparison should be about spacing rather than about
            // one station being dimmer than the rest.
            // Larger than the dense stations on purpose. At 23k points over a 1.7 m figure
            // the gaps are most of what you see, and at the dense station's point size the
            // silhouette never closes -- it reads as speckle rather than as a sparse person,
            // which is the wrong thing to learn from the comparison.
            mat.SetFloat("_PointSize", 0.011f);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_ScanColorMix", 0f);
            mat.SetFloat("_Wrap", 0.45f);
            mat.SetFloat("_AmbientBoost", 0.75f);
            mat.SetFloat("_Exposure", 1.15f);
            return Save(mat);
        }

        static Material LoadOrCreate(string path, Shader shader)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.shader != shader) existing.shader = shader;
                return existing;
            }
            return new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
        }

        static Material Save(Material mat)
        {
            if (!AssetDatabase.Contains(mat))
            {
                string path = $"Assets/Materials/{mat.name}.mat";
                AssetDatabase.CreateAsset(mat, path);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static bool Check(Shader shader, string name)
        {
            if (shader == null)
            {
                Debug.LogError($"[CloudScene] RESULT: FAIL - shader '{name}' not found.");
                return false;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                Debug.LogError($"[CloudScene] RESULT: FAIL - '{name}' has compile errors.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Opens the built scene and renders a frame from its camera, at a few points in the
        /// showcase cycle.
        ///
        /// A scene that builds without errors can still be empty, mis-framed or entirely black,
        /// and none of that shows up in a log line saying PASS. Rendering it is the only check
        /// that answers the question the scene exists to answer.
        ///
        /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
        ///   -executeMethod CompositeBody.PointClouds.EditorTools.BuildPointCloudTestScene.Shoot
        ///   -outDir &lt;dir&gt; [-width 1280] [-height 520]
        /// </summary>
        public static void Shoot()
        {
            string outDir = GetArg("-outDir");
            if (string.IsNullOrEmpty(outDir))
            {
                Debug.LogError("[CloudScene] RESULT: FAIL - pass -outDir <folder>.");
                return;
            }

            int width = 1280, height = 520;
            string w = GetArg("-width"), h = GetArg("-height");
            if (!string.IsNullOrEmpty(w)) int.TryParse(w, out width);
            if (!string.IsNullOrEmpty(h)) int.TryParse(h, out height);

            EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(outDir);

            var cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
            var showcase = UnityEngine.Object.FindFirstObjectByType<PointCloudShowcase>();
            var figures = UnityEngine.Object.FindObjectsByType<PointCloudFigure>(
                FindObjectsSortMode.InstanceID);

            if (cam == null || figures.Length == 0)
            {
                Debug.LogError($"[CloudScene] RESULT: FAIL - camera {(cam == null ? "missing" : "ok")}, " +
                               $"{figures.Length} figures.");
                return;
            }
            Debug.Log($"[CloudScene] opened: {figures.Length} figures, showcase " +
                      $"{(showcase == null ? "missing" : "present")}");

            // Driven by hand rather than by the showcase: batch mode never ticks Update, so the
            // states have to be set here for the stills to show anything but the idle pose.
            var states = new (float dissolve, float glitch, string label)[]
            {
                (0.00f, 0.00f, "scene_0_idle"),
                (0.00f, 0.45f, "scene_1_glitch"),
                (0.40f, 0.00f, "scene_2_dissolve"),
            };

            int written = 0;
            foreach ((float dissolve, float glitch, string label) in states)
            {
                foreach (PointCloudFigure f in figures)
                {
                    f.dissolve = dissolve;
                    f.glitch = glitch;
                }

                string path = Path.Combine(outDir, $"{label}.png");
                if (RenderTo(cam, path, width, height)) written++;
                Debug.Log($"[CloudScene] dissolve {dissolve:0.00} glitch {glitch:0.00} -> {path}");
            }

            if (written == states.Length)
                Debug.Log($"[CloudScene] {written} frames.\n[CloudScene] RESULT: PASS");
            else
                Debug.LogError($"[CloudScene] only {written} of {states.Length}.\n" +
                               "[CloudScene] RESULT: FAIL");
        }

        static bool RenderTo(Camera cam, string outPath, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1
            };
            RenderTexture previous = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;

                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[CloudScene] {outPath}: {e.Message}");
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

        static bool HasFlag(string name)
        {
            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (a == name) return true;
            }
            return false;
        }
    }
}
