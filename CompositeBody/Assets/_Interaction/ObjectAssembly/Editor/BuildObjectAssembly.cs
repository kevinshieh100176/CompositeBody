using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using CompositeBody.Interaction.EditorTools;
using Object = UnityEngine.Object;

namespace CompositeBody.Interaction.ObjectAssembly.EditorTools
{
    /// <summary>
    /// Builds Assets/_Interaction/ObjectAssembly/ObjectAssembly_Demo.unity and
    /// Prefabs/ObjectAssembly.prefab from Lamp_2split.fbx, then verifies them.
    ///
    ///   Tools/ObjectAssembly/Build Demo   (refuses while any loaded scene has unsaved changes;
    ///                                      reopens what was open afterwards)
    ///   Tools/ObjectAssembly/Verify Demo  (with the demo scene active)
    ///
    /// THE HALVES ARE BAKED, NOT INSTANCED. The FBX's parts are a 100x-scaled, 270-degree-rotated
    /// root with the other part parented under it. BreathingView offsets its Visual child in the
    /// root's local space, so a scaled or tipped root would turn a 2.5 cm bob into metres sideways.
    /// Each part's mesh is therefore re-written into metres, upright, centred on its own bounds
    /// (Meshes/OA_Lamp_A.asset, OA_Lamp_B.asset): the root is unscaled and unrotated, the Visual
    /// child is identity, and the convex collider uses the same mesh.
    ///
    /// THE JOIN POINT is where the two parts meet in the FBX's assembled layout: the centroid of
    /// the vertex pairs closer than 4 mm (the closest pair if none are). Each half gets a JoinPoint
    /// child there, so both coincide when assembled and OATrail runs between them.
    ///
    /// OA_Phase0.playable is created once and then only topped up (a "Phase0 VO" AudioTrack, a
    /// "Lights Up" AnimationTrack, an OAAppearMarker), so a voice-over dropped into it later
    /// survives a rebuild.
    /// </summary>
    public static class BuildObjectAssembly
    {
        const string Tag = "OABuild";

        public const string Root = "Assets/_Interaction/ObjectAssembly";
        public const string ScenePath = Root + "/ObjectAssembly_Demo.unity";
        public const string PrefabPath = Root + "/Prefabs/ObjectAssembly.prefab";
        public const string FbxPath = "Assets/_models/Lamp_2split/Lamp_2split.fbx";
        const string MeshFolder = Root + "/Meshes";
        const string MaterialFolder = Root + "/Materials";
        const string TimelineFolder = Root + "/Timeline";
        public const string TimelinePath = TimelineFolder + "/OA_Phase0.playable";
        const string LightsClipPath = TimelineFolder + "/OA_LightsUp.anim";
        const string FloorMaterialPath = MaterialFolder + "/OA_Floor.mat";
        const string TrailMaterialPath = MaterialFolder + "/OATrailFlow.mat";

        public const string AudioTrackName = "Phase0 VO";
        public const string LightsTrackName = "Lights Up";
        const float AppearMarkerTime = 3.5f;
        const float LightsStart = 0.5f;
        const float LightsDuration = 3.5f;
        const float KeyIntensity = 3.2f;
        const float FillIntensity = 1.0f;
        const float ContactTolerance = 0.004f;

        static readonly Vector3 CornerP1 = new(-0.55f, 1.05f, -0.2f);
        static readonly Vector3 CornerP2 = new(0.55f, 1.05f, -0.2f);
        static readonly Vector3 DestinationPosition = new(0f, 1.25f, 0.65f);
        static readonly Pose SpawnP1 = new(new Vector3(-0.7f, 0f, -0.95f), Quaternion.identity);
        static readonly Pose SpawnP2 = new(new Vector3(0.7f, 0f, -0.95f), Quaternion.identity);
        static readonly Vector3 FollowOffset = new(0.24f, -0.3f, 0.42f);

        struct Part
        {
            public string name;
            public Mesh mesh;
            public Matrix4x4 world;
            public Material material;
        }

        // ---- menu ------------------------------------------------------------------------------

        [MenuItem("Tools/ObjectAssembly/Build Demo")]
        public static void BuildDemo()
        {
            if (InteractionBuildUtil.AnySceneDirty(Tag))
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - a loaded scene has unsaved changes; this builder " +
                               "replaces the open scene and never saves somebody else's work.");
                return;
            }

            var reopen = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && !string.IsNullOrEmpty(s.path) && s.path != ScenePath) reopen.Add(s.path);
            }

            try
            {
                Build();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[{Tag}] RESULT: FAIL - exception: {e.Message}");
            }
            finally
            {
                Reopen(reopen);
            }
        }

        [MenuItem("Tools/ObjectAssembly/Verify Demo")]
        public static void VerifyDemo()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - open {ScenePath} first.");
                return;
            }
            var failures = new List<string>();
            Verify(scene, failures);
            InteractionBuildUtil.Verdict(Tag, failures);
        }

        static void Reopen(List<string> paths)
        {
            if (paths.Count == 0) return;
            if (Application.isBatchMode) return;
            EditorSceneManager.OpenScene(paths[0], OpenSceneMode.Single);
            for (int i = 1; i < paths.Count; i++) EditorSceneManager.OpenScene(paths[i], OpenSceneMode.Additive);
            Debug.Log($"[{Tag}] reopened {string.Join(", ", paths)}.");
        }

        // ---- build -----------------------------------------------------------------------------

        static void Build()
        {
            var failures = new List<string>();

            if (!InteractionBuildUtil.RevealShaderOk(out string shaderProblem))
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - {InteractionBuildUtil.RevealShaderName} {shaderProblem}.");
                return;
            }

            // 1. The two parts.
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            List<Part> parts = fbx != null ? ReadParts(fbx) : new List<Part>();
            if (!InteractionBuildUtil.Check(Tag, parts.Count == 2,
                    $"{FbxPath} has exactly two mesh parts (found {parts.Count})", failures))
            {
                InteractionBuildUtil.Verdict(Tag, failures);
                return;
            }

            // 2. The join point, in the FBX's assembled layout (world space of a fresh instance).
            Vector3 joint = FindJoint(parts[0], parts[1], out int contacts, out float closest);
            Debug.Log($"[{Tag}] join point {joint:F4} from {contacts} contact pairs (closest {closest * 1000f:0.0} mm).");

            // 3. A fresh scene FIRST. Opening a scene in Single mode unloads every asset nothing
            // references, and an asset created a moment earlier is exactly that -- the director
            // was handed a dead TimelineAsset and saved a null. Assets are made after this.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.035f, 0.035f, 0.045f);
            RenderSettings.fog = false;

            // 4. Baked meshes, materials, timeline.
            Directory.CreateDirectory(MeshFolder);
            Directory.CreateDirectory(MaterialFolder);
            Directory.CreateDirectory(TimelineFolder);
            Directory.CreateDirectory(Root + "/Prefabs");

            var centres = new Vector3[2];
            var meshes = new Mesh[2];
            var materials = new Material[2];
            for (int i = 0; i < 2; i++)
            {
                centres[i] = WorldBoundsCentre(parts[i]);
                meshes[i] = BakeMesh(parts[i], centres[i], $"{MeshFolder}/OA_Lamp_{(i == 0 ? "A" : "B")}.asset");
                string matPath = $"{MaterialFolder}/OA_{Sanitize(parts[i].material != null ? parts[i].material.name : "Lamp")}.mat";
                materials[i] = InteractionBuildUtil.MakeFragmentMaterial(parts[i].material, matPath);
            }
            Material floorMat = MakeFloorMaterial();
            AnimationClip lightsClip = MakeLightsClip();
            TimelineAsset timeline = EnsureTimeline(lightsClip, out AudioTrack audioTrack, out AnimationTrack lightsTrack);
            AssetDatabase.SaveAssets();

            var environment = new GameObject("Environment");
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(environment.transform, false);
            floor.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            var root = new GameObject("ObjectAssembly");
            Transform cornerP1 = Child(root.transform, "Corner_P1", CornerP1).transform;
            Transform cornerP2 = Child(root.transform, "Corner_P2", CornerP2).transform;
            Transform destination = Child(root.transform, "Destination", DestinationPosition).transform;

            // Lights, dark until the Phase 0 timeline brings them up.
            GameObject lights = Child(root.transform, "Lights", Vector3.zero);
            Animator lightsAnimator = lights.AddComponent<Animator>();
            MakeLight(lights.transform, "Key", LightType.Spot, new Vector3(0f, 2.9f, -0.1f),
                      Quaternion.Euler(80f, 0f, 0f), new Color(1f, 0.92f, 0.8f), 6f);
            MakeLight(lights.transform, "Fill", LightType.Point, new Vector3(0f, 2.0f, -1.6f),
                      Quaternion.identity, new Color(0.72f, 0.8f, 1f), 5f);

            // Halves.
            var fragments = new OAFragment[2];
            var joinPoints = new Transform[2];
            var views = new BreathingView[2];
            Vector3[] corners = { CornerP1, CornerP2 };
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "Fragment_A" : "Fragment_B");
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(corners[i], Quaternion.identity);

                GameObject visual = Child(go.transform, "Visual", Vector3.zero);
                visual.transform.localPosition = Vector3.zero;
                visual.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var mr = visual.AddComponent<MeshRenderer>();
                mr.sharedMaterial = materials[i];
                views[i] = visual.AddComponent<BreathingView>();

                InteractionBuildUtil.ConfigureGrabbable(go, meshes[i]);
                fragments[i] = go.AddComponent<OAFragment>();

                GameObject jp = Child(go.transform, "JoinPoint", Vector3.zero);
                jp.transform.localPosition = joint - centres[i];
                joinPoints[i] = jp.transform;
            }

            // Trail.
            GameObject trailGo = Child(root.transform, "Trail", Vector3.zero);
            var trail = trailGo.AddComponent<OATrail>();
            var tso = new SerializedObject(trail);
            tso.FindProperty("m_Material").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(TrailMaterialPath);
            tso.FindProperty("m_Visibility").floatValue = 0f;
            tso.ApplyModifiedPropertiesWithoutUndo();
            trail.SetEnds(joinPoints[0], joinPoints[1]);

            // Controller: always active, its own NetworkObject (the root has none).
            GameObject ctrlGo = Child(root.transform, "Controller", Vector3.zero);
            var ctrlNet = ctrlGo.AddComponent<NetworkObject>();
            InteractionBuildUtil.SetNetworkObjectFlags(ctrlNet);
            var director = ctrlGo.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
            director.extrapolationMode = DirectorWrapMode.Hold;
            var controller = ctrlGo.AddComponent<OAController>();

            AudioSource phase0 = MakeVoice(ctrlGo.transform, "Phase0 VO");
            AudioSource phase1 = MakeVoice(ctrlGo.transform, "Phase1 VO");
            AudioSource phase2 = MakeVoice(ctrlGo.transform, "Phase2 VO");
            director.SetGenericBinding(audioTrack, phase0);
            director.SetGenericBinding(lightsTrack, lightsAnimator);

            for (int i = 0; i < 2; i++)
            {
                var layout = new Pose(centres[i] - joint, Quaternion.identity);
                fragments[i].Configure(controller, joinPoints[i], views[i], layout, FollowOffset);
                EditorUtility.SetDirty(fragments[i]);
            }
            controller.Configure(fragments[0], fragments[1], trail, cornerP1, cornerP2, destination, director, phase1, phase2);
            EditorUtility.SetDirty(controller);

            // Bootstrap (network manager, session, rig, staff + debug panels...).
            GameObject bootstrap = InteractionBuildUtil.BuildDemoBootstrap(scene, SpawnP1, SpawnP2, Tag);
            InteractionBuildUtil.Check(Tag, bootstrap != null, "demo bootstrap built", failures);

            // 5. Save: scene, prefab, hashes, scene again.
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                InteractionBuildUtil.Check(Tag, false, $"saved {ScenePath}", failures);
                InteractionBuildUtil.Verdict(Tag, failures);
                return;
            }

            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction, out bool prefabOk);
            InteractionBuildUtil.Check(Tag, prefabOk, $"saved {PrefabPath}", failures);

            InteractionBuildUtil.RegenerateNetworkObjectHashes(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // 6. Verify.
            Verify(scene, failures);
            InteractionBuildUtil.Verdict(Tag, failures);
        }

        // ---- parts, joint, meshes --------------------------------------------------------------

        static List<Part> ReadParts(GameObject fbx)
        {
            var parts = new List<Part>();
            var instance = (GameObject)Object.Instantiate(fbx);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                foreach (MeshFilter mf in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    parts.Add(new Part
                    {
                        name = mf.sharedMesh.name,
                        mesh = mf.sharedMesh,
                        world = mf.transform.localToWorldMatrix,
                        material = mr != null ? mr.sharedMaterial : null,
                    });
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
            return parts;
        }

        static Vector3[] WorldVertices(Part p)
        {
            Vector3[] v = p.mesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = p.world.MultiplyPoint3x4(v[i]);
            return v;
        }

        static Vector3 WorldBoundsCentre(Part p)
        {
            Vector3[] v = WorldVertices(p);
            var b = new Bounds(v[0], Vector3.zero);
            foreach (Vector3 x in v) b.Encapsulate(x);
            return b.center;
        }

        /// <summary>Centroid of the contact between two parts; the closest vertex pair if they never come within tolerance.</summary>
        static Vector3 FindJoint(Part a, Part b, out int contacts, out float closest)
        {
            Vector3[] va = WorldVertices(a);
            Vector3[] vb = WorldVertices(b);
            Vector3 sum = Vector3.zero;
            contacts = 0;
            closest = float.MaxValue;
            Vector3 closestMid = Vector3.zero;
            float tol2 = ContactTolerance * ContactTolerance;

            foreach (Vector3 p in va)
            {
                float best = float.MaxValue;
                Vector3 bestQ = p;
                foreach (Vector3 q in vb)
                {
                    float d = (p - q).sqrMagnitude;
                    if (d < best) { best = d; bestQ = q; }
                }
                float bestDistance = Mathf.Sqrt(best);
                if (bestDistance < closest)
                {
                    closest = bestDistance;
                    closestMid = (p + bestQ) * 0.5f;
                }
                if (best <= tol2)
                {
                    sum += (p + bestQ) * 0.5f;
                    contacts++;
                }
            }
            return contacts > 0 ? sum / contacts : closestMid;
        }

        /// <summary>
        /// The part's mesh rewritten into metres, upright, centred on <paramref name="centre"/>.
        /// Saved over an existing asset in place so its GUID (and every reference) survives a rebuild.
        /// </summary>
        static Mesh BakeMesh(Part p, Vector3 centre, string path)
        {
            Mesh baked = Object.Instantiate(p.mesh);
            Matrix4x4 m = p.world;
            Matrix4x4 normalMatrix = m.inverse.transpose;
            float handedness = m.determinant < 0f ? -1f : 1f;

            Vector3[] v = p.mesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]) - centre;
            baked.vertices = v;

            Vector3[] n = p.mesh.normals;
            if (n != null && n.Length == v.Length)
            {
                for (int i = 0; i < n.Length; i++) n[i] = normalMatrix.MultiplyVector(n[i]).normalized;
                baked.normals = n;
            }

            Vector4[] t = p.mesh.tangents;
            if (t != null && t.Length == v.Length)
            {
                for (int i = 0; i < t.Length; i++)
                {
                    Vector3 d = m.MultiplyVector(new Vector3(t[i].x, t[i].y, t[i].z)).normalized;
                    t[i] = new Vector4(d.x, d.y, d.z, t[i].w * handedness);
                }
                baked.tangents = t;
            }

            baked.RecalculateBounds();
            baked.name = Path.GetFileNameWithoutExtension(path);

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(baked, path);
                return baked;
            }
            EditorUtility.CopySerialized(baked, existing);
            existing.name = baked.name;
            Object.DestroyImmediate(baked);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ---- materials, lights, timeline -------------------------------------------------------

        static Material MakeFloorMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "OA_Floor" };
            mat.SetColor("_BaseColor", new Color(0.16f, 0.16f, 0.17f, 1f));
            mat.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(mat, FloorMaterialPath);
            return mat;
        }

        static Light MakeLight(Transform parent, string name, LightType type, Vector3 position, Quaternion rotation,
                               Color color, float range)
        {
            GameObject go = Child(parent, name, position);
            go.transform.rotation = rotation;
            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = 0f; // dark until the timeline
            light.range = range;
            if (type == LightType.Spot)
            {
                light.spotAngle = 80f;
                light.innerSpotAngle = 40f;
                light.shadows = LightShadows.Soft;
            }
            light.lightmapBakeType = LightmapBakeType.Realtime;
            return light;
        }

        static AnimationClip MakeLightsClip()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(LightsClipPath);
            bool created = clip == null;
            if (created) clip = new AnimationClip { name = "OA_LightsUp" };
            clip.ClearCurves();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Key", typeof(Light), "m_Intensity"),
                                            AnimationCurve.EaseInOut(0f, 0f, LightsDuration, KeyIntensity));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Fill", typeof(Light), "m_Intensity"),
                                            AnimationCurve.EaseInOut(0f, 0f, LightsDuration, FillIntensity));
            if (created) AssetDatabase.CreateAsset(clip, LightsClipPath);
            else EditorUtility.SetDirty(clip);
            return clip;
        }

        static TimelineAsset EnsureTimeline(AnimationClip lightsClip, out AudioTrack audioTrack, out AnimationTrack lightsTrack)
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                timeline.name = "OA_Phase0";
                AssetDatabase.CreateAsset(timeline, TimelinePath);
            }

            audioTrack = null;
            lightsTrack = null;
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (audioTrack == null && track is AudioTrack a && track.name == AudioTrackName) audioTrack = a;
                if (lightsTrack == null && track is AnimationTrack l && track.name == LightsTrackName) lightsTrack = l;
            }
            if (audioTrack == null) audioTrack = timeline.CreateTrack<AudioTrack>(null, AudioTrackName);
            if (lightsTrack == null) lightsTrack = timeline.CreateTrack<AnimationTrack>(null, LightsTrackName);

            TimelineClip lightsTimelineClip = null;
            foreach (TimelineClip c in lightsTrack.GetClips()) { lightsTimelineClip = c; break; }
            if (lightsTimelineClip == null)
            {
                lightsTimelineClip = lightsTrack.CreateClip(lightsClip);
                lightsTimelineClip.start = LightsStart;
                lightsTimelineClip.duration = LightsDuration;
                lightsTimelineClip.displayName = "Lights Up";
            }
            // Hold after the clip, or the lights fall back to dark the moment a longer VO outruns it.
            SetPostExtrapolation(lightsTimelineClip, TimelineClip.ClipExtrapolation.Hold);

            bool hasMarker = false;
            if (timeline.markerTrack != null)
                foreach (IMarker m in timeline.markerTrack.GetMarkers())
                    if (m is OAAppearMarker) hasMarker = true;
            if (!hasMarker)
            {
                timeline.CreateMarkerTrack();
                timeline.markerTrack.CreateMarker<OAAppearMarker>(AppearMarkerTime);
            }

            EditorUtility.SetDirty(timeline);
            return timeline;
        }

        static void SetPostExtrapolation(TimelineClip clip, TimelineClip.ClipExtrapolation mode)
        {
            PropertyInfo prop = typeof(TimelineClip).GetProperty("postExtrapolationMode",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo setter = prop?.GetSetMethod(true);
            if (setter != null) setter.Invoke(clip, new object[] { mode });
            else Debug.LogWarning($"[{Tag}] could not set post-extrapolation on '{clip.displayName}'.");
        }

        static AudioSource MakeVoice(Transform parent, string name)
        {
            GameObject go = Child(parent, name, Vector3.zero);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            return source;
        }

        static GameObject Child(Transform parent, string name, Vector3 worldPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPosition;
            return go;
        }

        static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        // ---- verify ----------------------------------------------------------------------------

        static void Verify(Scene scene, List<string> failures)
        {
            bool C(bool ok, string what) => InteractionBuildUtil.Check(Tag, ok, what, failures);

            GameObject root = null;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == "ObjectAssembly") root = go;
            if (!C(root != null, "scene has /ObjectAssembly")) return;

            C(root.GetComponent<NetworkObject>() == null, "root has no NetworkObject");
            C(PrefabUtility.IsPartOfPrefabInstance(root) && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null,
              "root is an instance of Prefabs/ObjectAssembly.prefab");

            foreach (NetworkObject n in root.GetComponentsInChildren<NetworkObject>(true))
            {
                Transform up = n.transform.parent;
                bool nested = false;
                while (up != null) { if (up.GetComponent<NetworkObject>() != null) nested = true; up = up.parent; }
                C(!nested, $"{n.name}: NetworkObject not nested under another");
            }

            var controller = root.GetComponentInChildren<OAController>(true);
            if (!C(controller != null, "has an OAController")) return;
            C(controller.GetComponent<NetworkObject>() != null, "OAController has its own NetworkObject");
            C(controller.gameObject.activeInHierarchy, "OAController is always active");

            var so = new SerializedObject(controller);
            foreach (string field in new[] { "m_FragmentA", "m_FragmentB", "m_Trail", "m_CornerP1", "m_CornerP2",
                                             "m_Destination", "m_Director", "m_Phase1Source", "m_Phase2Source" })
                C(so.FindProperty(field).objectReferenceValue != null, $"OAController.{field} wired");
            C(so.FindProperty("m_DebugId").stringValue == "oa.assembly", "debug id is 'oa.assembly'");
            C(Mathf.Approximately(so.FindProperty("m_JoinDistance").floatValue, 0.12f), "join distance 0.12 m");

            OAFragment a = controller.fragmentA, b = controller.fragmentB;
            if (!C(a != null && b != null && a != b, "two distinct fragments")) return;

            foreach (OAFragment f in new[] { a, b })
            {
                InteractionBuildUtil.VerifyGrabbable(Tag, f.gameObject, failures);
                C(f.controller == controller, $"{f.name}: controller wired");
                C(f.joinPoint != null && f.joinPoint.IsChildOf(f.transform), $"{f.name}: JoinPoint child");
                C(f.view != null && f.view.transform.parent == f.transform, $"{f.name}: BreathingView on the Visual child");
                C(f.transform.localScale == Vector3.one && f.transform.rotation == Quaternion.identity,
                  $"{f.name}: root unscaled and upright (BreathingView offsets are in root space)");
                if (f.view != null)
                {
                    Transform v = f.view.transform;
                    C(v.localPosition == Vector3.zero && v.localRotation == Quaternion.identity && v.localScale == Vector3.one,
                      $"{f.name}: Visual is identity");
                    var mr = v.GetComponent<MeshRenderer>();
                    Material m = mr != null ? mr.sharedMaterial : null;
                    C(InteractionBuildUtil.IsRevealMaterialOk(m) && m.IsKeywordEnabled("_EMISSION"),
                      $"{f.name}: reveal material with _ALPHATEST_ON and _EMISSION");
                }
            }

            // Both join points coincide in the assembled layout.
            if (a.joinPoint != null && b.joinPoint != null)
            {
                Vector3 ja = a.layout.position + a.layout.rotation * a.joinPoint.localPosition;
                Vector3 jb = b.layout.position + b.layout.rotation * b.joinPoint.localPosition;
                C(Vector3.Distance(ja, jb) < 0.001f && ja.magnitude < 0.001f,
                  $"join points coincide at the assembly origin ({Vector3.Distance(ja, jb) * 1000f:0.00} mm apart)");
            }

            C(Vector3.Distance(a.transform.position, controller.cornerP1.position) < 0.001f, "half A starts at Corner_P1");
            C(Vector3.Distance(b.transform.position, controller.cornerP2.position) < 0.001f, "half B starts at Corner_P2");

            // Trail.
            OATrail trail = controller.trail;
            if (C(trail != null, "trail present"))
            {
                C(trail.endA == a.joinPoint && trail.endB == b.joinPoint, "trail runs between the join points");
                C(trail.visibility == 0f, "trail starts hidden");
                C(new SerializedObject(trail).FindProperty("m_Material").objectReferenceValue != null, "trail material set");
            }

            // Timeline.
            var director = controller.GetComponent<PlayableDirector>();
            var timeline = director != null ? director.playableAsset as TimelineAsset : null;
            if (C(timeline != null && AssetDatabase.GetAssetPath(timeline) == TimelinePath, "director plays OA_Phase0.playable"))
            {
                C(!director.playOnAwake, "director does not play on awake");
                AudioTrack audio = null; AnimationTrack anim = null;
                foreach (TrackAsset t in timeline.GetOutputTracks())
                {
                    if (t is AudioTrack at && t.name == AudioTrackName) audio = at;
                    if (t is AnimationTrack an && t.name == LightsTrackName) anim = an;
                }
                C(audio != null && director.GetGenericBinding(audio) is AudioSource, "Phase0 VO track bound to an AudioSource");
                C(anim != null && director.GetGenericBinding(anim) is Animator, "Lights Up track bound to the Lights animator");
                if (anim != null)
                {
                    bool hold = false;
                    foreach (TimelineClip c in anim.GetClips()) hold = c.postExtrapolationMode == TimelineClip.ClipExtrapolation.Hold;
                    C(hold, "lights clip holds after it ends");
                }
                bool marker = false;
                if (timeline.markerTrack != null)
                    foreach (IMarker m in timeline.markerTrack.GetMarkers()) marker |= m is OAAppearMarker;
                C(marker, "timeline has an OAAppearMarker");
            }

            // Lights start dark.
            Transform lights = root.transform.Find("Lights");
            if (C(lights != null, "Lights present"))
            {
                bool dark = true;
                foreach (Light l in lights.GetComponentsInChildren<Light>(true)) dark &= l.intensity == 0f;
                C(dark, "lights start dark");
            }

            InteractionBuildUtil.VerifyHashes(Tag, scene, failures);
            InteractionBuildUtil.VerifyDemoBootstrap(Tag, scene, failures);
        }
    }
}
