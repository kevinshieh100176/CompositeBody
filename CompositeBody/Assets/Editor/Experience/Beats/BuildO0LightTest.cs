using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CompositeBody.Experience;
using CompositeBody.PointClouds;

namespace CompositeBody.Experience.EditorTools
{
    /// <summary>
    /// O-0's two 光圈, built and photographed: the grey plane, 真人 A and B as point clouds, a
    /// purple stage special over one and a yellow one over the other, and the low-density dust
    /// the script asks for.
    ///
    /// The 光圈 are CONES, not columns -- a lamp hung above each figure throwing a beam down onto
    /// them, the way a stage special works. A column is the right shape for light rising out of
    /// the floor; a cone is the right shape for light falling on a body, and it carries the two
    /// cues a column cannot: a visible source overhead, and a pool where the beam lands.
    ///
    /// Six views, and the last three are the ones that matter. A volumetric light is easy to get
    /// right from outside and easy to get wrong the moment someone walks into it -- front-face
    /// rendering vanishes, a near-plane clip tears a hole, an integral that assumed the camera
    /// was outside goes negative. O-0 asks the players to approach the figures and touch them,
    /// so inside the beam is not an edge case here, it is the beat. The view looking back up
    /// into the lamp is there for the phase function, which is invisible from any other angle.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Experience.EditorTools.BuildO0LightTest.Run
    ///   -outDir &lt;dir&gt; [-scene] [-width 1200] [-height 760]
    /// </summary>
    public static class BuildO0LightTest
    {
        const string k_ScenePath = "Assets/_Scenes/O0LightTest.unity";
        const string k_Cloud = "Assets/_models/PointCloud/Figure_A.ply";
        const string k_MatSpot = "Assets/Materials/VolumetricSpot.mat";
        const string k_MatFigure = "Assets/Materials/O0FigureLit.mat";

        // The fixture, in metres and degrees. 24 degrees across a 3.3 m throw gives a pool about
        // 1.4 m wide, which wraps a figure and still leaves a quarter of a metre of dark floor
        // between the two 真人 -- they stand 1.7 m apart, and at 30 degrees the pools merge into
        // one puddle and the beat loses the two separate colours it is about.
        //
        // The throw is exactly the lamp height, so the visible cone ends on the floor and the
        // proxy box is barely buried in it. The lamp's RANGE is half as far again, because a
        // URP light's range is where its falloff reaches zero: at 3.3 m of a 3.5 m range the
        // attenuation is about one percent, which is why the first version of this scene threw
        // a beam at a floor it could not light. Range and throw are different measurements.
        const float k_Throw = 3.3f;
        const float k_ConeAngle = 24f;
        const float k_LampHeight = 3.3f;
        const float k_LampRange = 5f;

        static readonly Color k_Purple = new(0.62f, 0.22f, 1.00f);
        static readonly Color k_Yellow = new(1.00f, 0.78f, 0.12f);

        // 「原先空無一物，完全灰色的空間」. Grey, and that is the correction: the first version of this
        // scene faded to 0.075, which reads as darkness rather than as an empty grey room. Dark
        // enough that additive beams still have somewhere to sit -- a bright room and a visible
        // shaft are in direct competition, which is why stage lighting happens in the dark.
        static readonly Color k_Air = new(0.145f, 0.148f, 0.160f);

        public static void Run()
        {
            Debug.Log("[O0Light] Starting...");

            var spotShader = Shader.Find("CompositeBody/VolumetricSpot");
            var figureShader = Shader.Find("CompositeBody/PointCloudLit");
            if (!Check(spotShader, "CompositeBody/VolumetricSpot")) return;
            if (!Check(figureShader, "CompositeBody/PointCloudLit")) return;

            Mesh figureMesh = LoadMesh(k_Cloud);
            if (figureMesh == null)
                Debug.LogWarning($"[O0Light] no mesh at {k_Cloud}; columns will stand empty.");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildVoid();

            Material spotMat = LoadOrCreate(k_MatSpot, spotShader, "VolumetricSpot");
            // Tuned down hard from the shader's defaults. At density 1 over a 3 m throw the
            // beams came out as flat opaque triangles: enough haze that x/(1+x) saturates along
            // every path through the cone, so the core and the edge arrive at the same colour
            // and the shape stops reading as light. Haze wants to be nearly subliminal -- you
            // should see the beam and still see through it.
            spotMat.SetFloat("_Intensity", 1.35f);
            // A soft radial gradient rather than a hard rim. Gain is for a sharp theatrical edge
            // and this beat wants a 光圈, a halo, not a followspot.
            spotMat.SetFloat("_EdgeGain", 1.6f);
            spotMat.SetFloat("_EdgeSoftness", 0.85f);
            // Low, and it was three times this before the room had air in it. Distance falloff
            // dims the beam downward; the height haze brightens it downward. Left at 1.8 the two
            // cancelled almost exactly and the shafts came out flat and faint along their whole
            // length. The gradient is the haze's job now -- this only takes the edge off the top.
            spotMat.SetFloat("_DistanceFalloff", 0.6f);
            // Density roughly doubled at the same time, because the authored value is now the
            // density at FLOOR level rather than everywhere: the haze factor is 1 at the base and
            // about 0.22 up at the lamp, so the same number buys a dimmer beam overall.
            spotMat.SetFloat("_Density", 0.95f);
            // The default 0.35 fades the last third of the beam away, which is right for a shaft
            // that ends in mid-air and wrong for one that lands on a floor -- it would delete
            // exactly the part that makes the cone read as touching the ground.
            spotMat.SetFloat("_RangeFade", 0.05f);
            // Zero. The haze does not need a fake pool at its far end: the lamp is a real spot
            // light pointed at a real floor, so the pool is the floor actually being lit. Adding
            // it here as well doubles it and cancels the distance falloff above.
            spotMat.SetFloat("_BaseGlow", 0f);
            spotMat.SetFloat("_Anisotropy", 0.65f);
            spotMat.SetFloat("_Steps", 12f);
            // Tighter than the 0.3 default: this is the distance over which the beam fades out
            // where it meets the floor, and at 30 cm it ate the edge of the pool it lands in.
            spotMat.SetFloat("_DepthSoftness", 0.15f);
            Save(spotMat, k_MatSpot);

            Material figureMat = LoadOrCreate(k_MatFigure, figureShader, "O0FigureLit");
            figureMat.SetColor("_BaseColor", Color.white);
            figureMat.SetFloat("_ScanColorMix", 0.2f);
            figureMat.SetFloat("_PointSize", 0.0045f);
            figureMat.SetFloat("_Exposure", 0.7f);
            figureMat.SetFloat("_AmbientBoost", 0.4f);
            figureMat.SetFloat("_Wrap", 0.4f);
            Save(figureMat, k_MatFigure);

            BuildStation("Human A", new Vector3(-0.85f, 0f, 0f), k_Purple,
                         figureMesh, figureMat, spotMat, 22f);
            BuildStation("Human B", new Vector3(0.85f, 0f, 0f), k_Yellow,
                         figureMesh, figureMat, spotMat, -22f);

            BuildDust();
            Camera cam = BuildCamera();

            // After the fixtures, not before: the zone collects the beams from VolumetricSpot's
            // roster, and in batch mode nothing else will drive it before the scene is written.
            CompositeFogZone zone = UnityEngine.Object.FindFirstObjectByType<CompositeFogZone>();
            if (zone != null) zone.Rebuild();
            Debug.Log($"[O0Light] air: {VolumetricSpot.active.Count} fixture(s) tinting the fog, " +
                      $"stock fog density {RenderSettings.fogDensity:F3} to match at floor level.");

            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();

            string outDir = GetArg("-outDir");
            if (string.IsNullOrEmpty(outDir))
            {
                Debug.Log($"[O0Light] {k_ScenePath} built.\n[O0Light] RESULT: PASS");
                return;
            }

            int width = 1200, height = 760;
            string w = GetArg("-width"), h = GetArg("-height");
            if (!string.IsNullOrEmpty(w)) int.TryParse(w, out width);
            if (!string.IsNullOrEmpty(h)) int.TryParse(h, out height);
            Directory.CreateDirectory(outDir);

            var views = new (Vector3 position, Vector3 look, string label)[]
            {
                (new Vector3(0f, 1.25f, -4.6f),      new Vector3(0f, 1.0f, 0f),      "o0_0_both"),
                (new Vector3(-2.1f, 1.5f, -2.6f),    new Vector3(-0.85f, 1.0f, 0f),  "o0_1_purple"),
                // Just outside the purple beam, then standing in the middle of it. The second is
                // the one that breaks naive implementations.
                (new Vector3(-0.85f, 1.45f, -1.25f), new Vector3(-0.85f, 1.0f, 0f),  "o0_2_at_the_edge"),
                (new Vector3(-0.85f, 1.45f, -0.28f), new Vector3(-0.85f, 1.05f, 0f), "o0_3_inside"),
                // Low, looking back up into the lamp. The phase function only shows itself from
                // here; from every other angle the beam is being viewed across it, and forward
                // scattering has nothing to do.
                (new Vector3(-0.85f, 0.35f, -0.95f), new Vector3(-0.85f, 3.3f, 0f),  "o0_4_up_the_beam"),
                // Inside the beam, off the axis, looking steeply down at the floor. This is the
                // one angle where back-face rendering can fail: the ray leaves through the
                // bottom of the proxy box, which is buried under the floor, and if the depth
                // test rejects that fragment there is nothing left to shade and the beam
                // vanishes where the player is looking. Worth a picture rather than an argument.
                (new Vector3(-0.58f, 1.6f, -0.30f),  new Vector3(-0.80f, 0f, 0.12f), "o0_5_looking_down"),
                // Low and across, almost lying on the floor. The height fog only shows its
                // gradient edge-on; from eye level a ground layer and a uniform room look the
                // same, which is how you end up tuning a parameter that is doing nothing.
                (new Vector3(2.9f, 0.3f, -2.6f),     new Vector3(-0.4f, 0.85f, 0.1f), "o0_6_haze"),
            };

            int written = 0;
            foreach ((Vector3 position, Vector3 look, string label) in views)
            {
                cam.transform.position = position;
                cam.transform.LookAt(look);
                string path = Path.Combine(outDir, $"{label}.png");
                if (Render(cam, path, width, height)) written++;
                Debug.Log($"[O0Light] camera {position} -> {path}");
            }

            // The wide shot again with the depth clamp switched OFF, which is the state this
            // project was in before the pipeline asset gained its depth texture. Kept as a
            // picture rather than a note because the artefact it shows is easy to mistake for
            // tuning: a straight dark wedge across the bottom of each beam, right where the haze
            // is thickest, from the proxy box's buried far face being depth-rejected. Something
            // that looks like a badly tuned falloff and is not one is worth being able to point
            // at later.
            var spots = UnityEngine.Object.FindObjectsByType<VolumetricSpot>(
                FindObjectsSortMode.InstanceID);
            foreach (VolumetricSpot s in spots) s.sceneDepthOcclusion = false;

            (Vector3 position, Vector3 look, string label) wide = views[0];
            cam.transform.position = wide.position;
            cam.transform.LookAt(wide.look);
            string depthPath = Path.Combine(outDir, "o0_7_no_depthclamp.png");
            if (Render(cam, depthPath, width, height)) written++;
            Debug.Log($"[O0Light] wide, depth clamp off (the wedge) -> {depthPath}");

            foreach (VolumetricSpot s in spots) s.sceneDepthOcclusion = true;
            // The occlusion mode writes a keyword and a ZTest onto the shared material, so put
            // the material back the way the scene expects rather than leaving the editor to
            // decide which of the two states it feels like persisting.
            AssetDatabase.SaveAssets();

            int expected = views.Length + 1;
            if (written == expected)
                Debug.Log($"[O0Light] {k_ScenePath}, {written} views.\n[O0Light] RESULT: PASS");
            else
                Debug.LogError($"[O0Light] only {written} of {expected}.\n[O0Light] RESULT: FAIL");
        }

        static GameObject BuildStation(string name, Vector3 position, Color colour,
                                       Mesh figureMesh, Material figureMat, Material spotMat,
                                       float yaw)
        {
            var root = new GameObject(name);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            if (figureMesh != null)
            {
                var figure = new GameObject("Figure");
                figure.transform.SetParent(root.transform, false);
                figure.AddComponent<MeshFilter>().sharedMesh = figureMesh;
                var renderer = figure.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = figureMat;
                renderer.localBounds = Grow(figureMesh.bounds, 2.5f);
                figure.AddComponent<PointCloudFigure>();
            }

            // A real lamp as well as the volume, and this one comes first because the volume
            // reads its cone. The light is what actually falls on the figure and on the floor;
            // the volume is only the air in between, and without the light a white point cloud
            // inside a glow stays grey.
            //
            // A spot rather than the point light this used to use. A point light had to be cut
            // to a 1.5 m range to stop it crossing the 1.7 m between the two 真人 and dressing
            // each of them in the other's colour. A 24-degree cone cannot reach the other figure
            // at any range, so the lamp is free to throw the whole 3.5 m to the floor.
            var lampGO = new GameObject("Lamp");
            lampGO.transform.SetParent(root.transform, false);
            lampGO.transform.localPosition = new Vector3(0f, k_LampHeight, 0f);
            // Straight down. The root only yaws, so +90 about local X aims the lamp at the floor
            // whichever way the figure inside it is turned.
            lampGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var lamp = lampGO.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.color = colour;
            lamp.intensity = 11f;
            lamp.range = k_LampRange;
            lamp.spotAngle = k_ConeAngle;
            lamp.innerSpotAngle = k_ConeAngle * 0.45f;
            // No shadows. 真人 is a point cloud -- hundreds of thousands of discrete points with
            // no watertight surface -- so a shadow map of it is a cloud of holes and the beam
            // shadow it casts reads as noise rather than as a body. Shafts genuinely broken by
            // an occluder need a depth capture from the lamp's own view, which is its own piece
            // of work and not this one.
            lamp.shadows = LightShadows.None;

            // A cube, not a cone. The shader solves the cone from the ray, so the proxy only has
            // to generate fragments wherever the beam might be visible; a cone mesh is closer to
            // the shape and worse at the job, because its own faces cull from the inside and
            // stepping into the beam would punch wedge-shaped holes through it where no back
            // face existed to shade. The collider is not wanted either -- a light should not be
            // something to bump into.
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "Beam";
            UnityEngine.Object.DestroyImmediate(beam.GetComponent<Collider>());
            beam.transform.SetParent(root.transform, false);
            beam.GetComponent<MeshRenderer>().sharedMaterial = spotMat;

            var spot = beam.AddComponent<VolumetricSpot>();
            var so = new SerializedObject(spot);
            so.FindProperty("m_MatchLight").objectReferenceValue = lamp;
            so.FindProperty("m_Range").floatValue = k_Throw;
            so.FindProperty("m_ConeAngle").floatValue = k_ConeAngle;
            so.FindProperty("m_Color").colorValue = colour;
            // These override the material: the component pushes them through a property block
            // every frame, so leaving them at the shader defaults would quietly undo the tuning
            // above the moment the scene runs.
            so.FindProperty("m_Intensity").floatValue = 1.35f;
            so.FindProperty("m_Density").floatValue = 0.95f;
            so.FindProperty("m_Anisotropy").floatValue = 0.65f;
            so.FindProperty("m_Steps").intValue = 12;
            // On, and the pipeline asset now carries m_RequireDepthTexture to pay for it. The
            // proxy box is a ninth longer than the cone inside it, so its far face is buried
            // under the floor, and with plain depth testing every ray leaving through that face
            // is rejected -- which cut a hard straight wedge out of the bottom of both beams,
            // exactly where the haze is thickest and the light matters most.
            so.FindProperty("m_SceneDepthOcclusion").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Batch mode has no scene view, so an [ExecuteAlways] LateUpdate is not guaranteed to
            // run before the scene is written out. Drive it once by hand, so what lands on disk
            // is the geometry the component would have computed for itself.
            spot.Rebuild();

            return root;
        }

        static Bounds Grow(Bounds b, float m) { b.Expand(m); return b; }

        /// <summary>The script's 「原先空無一物，完全灰色的空間，只有一個平面」.</summary>
        static void BuildVoid()
        {
            // The air, and the thing the beams are visible in. RenderSettings fog is not set
            // here any more -- the zone drives it, so the stock-shader floor stays in agreement
            // with everything standing on it. See CompositeFogZone.MatchStockFog.
            var air = new GameObject("Air").AddComponent<CompositeFogZone>();
            var aso = new SerializedObject(air);
            aso.FindProperty("m_Color").colorValue = k_Air;
            aso.FindProperty("m_Density").floatValue = 0.085f;
            // 2.2 m of falloff against a 3.3 m lamp: the air is thick where the figures stand
            // and thin where the fixtures hang, so the beams come out of clear space and land in
            // something. A uniform room gives shafts with no gradient along them at all.
            aso.FindProperty("m_FalloffHeight").floatValue = 2.2f;
            aso.FindProperty("m_GlowStrength").floatValue = 0.4f;
            aso.FindProperty("m_GlowRadius").floatValue = 0.75f;
            aso.ApplyModifiedPropertiesWithoutUndo();

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.200f, 0.205f, 0.220f);
            RenderSettings.ambientEquatorColor = new Color(0.150f, 0.153f, 0.165f);
            RenderSettings.ambientGroundColor = new Color(0.080f, 0.082f, 0.090f);

            var keyGO = new GameObject("Key Light");
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 0.55f;          // low: the void is meant to be unformed, not lit
            key.color = new Color(0.92f, 0.93f, 0.97f);
            keyGO.transform.rotation = Quaternion.Euler(50f, -150f, 0f);

            var floorShader = Shader.Find("Universal Render Pipeline/Lit");
            if (floorShader == null) return;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Grey Plane";
            floor.transform.localScale = Vector3.one * 4f;
            var mat = new Material(floorShader) { name = "O0GreyPlane" };
            mat.SetColor("_BaseColor", new Color(0.20f, 0.20f, 0.21f));
            mat.SetFloat("_Smoothness", 0.05f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>「遠處漂浮著少量灰塵與微弱粒子」 -- enough to catch the light, not a snowstorm.</summary>
        static void BuildDust()
        {
            var moteShader = Shader.Find("CompositeBody/TraceParticle");
            if (moteShader == null) return;

            var go = new GameObject("Dust");
            go.transform.position = new Vector3(0f, 1.2f, 0f);
            var system = go.AddComponent<ParticleSystem>();

            var main = system.main;
            main.startLifetime = 14f;
            main.startSpeed = 0.02f;
            main.startSize = 0.012f;
            main.maxParticles = 420;
            main.startColor = new Color(0.8f, 0.82f, 0.9f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;

            var emission = system.emission;
            emission.rateOverTime = 30f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(7f, 2.6f, 5f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(moteShader) { name = "O0Dust" };
            mat.SetColor("_Color", new Color(0.85f, 0.87f, 0.95f, 1f));
            mat.SetFloat("_Intensity", 0.8f);
            renderer.sharedMaterial = mat;
        }

        static Camera BuildCamera()
        {
            var go = new GameObject("Preview Camera");
            var cam = go.AddComponent<Camera>();
            go.AddComponent<UniversalAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // The same grey the fog saturates to. Anything else and the far distance ends on a
            // visible seam where the fogged floor stops and the background starts.
            cam.backgroundColor = k_Air;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            go.tag = "MainCamera";
            go.transform.position = new Vector3(0f, 1.25f, -4.6f);
            go.transform.LookAt(new Vector3(0f, 1f, 0f));
            return cam;
        }

        static bool Render(Camera cam, string outPath, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
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
                Debug.LogError($"[O0Light] {outPath}: {e.Message}");
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

        static Mesh LoadMesh(string path)
        {
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is Mesh m) return m;
            }
            return null;
        }

        static Material LoadOrCreate(string path, Shader shader, string name)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.shader != shader) existing.shader = shader;
                return existing;
            }
            return new Material(shader) { name = name };
        }

        static void Save(Material mat, string path)
        {
            if (!AssetDatabase.Contains(mat)) AssetDatabase.CreateAsset(mat, path);
            EditorUtility.SetDirty(mat);
        }

        static bool Check(Shader shader, string name)
        {
            if (shader == null)
            {
                Debug.LogError($"[O0Light] RESULT: FAIL - shader '{name}' not found.");
                return false;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                Debug.LogError($"[O0Light] RESULT: FAIL - '{name}' has compile errors.");
                return false;
            }
            return true;
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
