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
using CompositeBody.Multiplayer;
using Object = UnityEngine.Object;

namespace CompositeBody.Interaction.ObjectAssembly.EditorTools
{
    /// <summary>How a variant finds the point where its two parts meet.</summary>
    public enum OAJointRule
    {
        /// <summary>
        /// Centroid of part A's vertices within 4 mm of part B (the closest pair if none are).
        /// For parts that interpenetrate or nest along a shared axis -- the lamp's frame and shade.
        /// </summary>
        ContactCentroid = 0,

        /// <summary>
        /// Centre of the face of B's bounds that looks toward A's centre. For a part mounted on
        /// the face of the other -- the camera's lens on the body's front -- where the few
        /// vertices that happen to touch cluster at one edge of the seam rather than around it.
        /// </summary>
        FaceOfBTowardA = 1,
    }

    /// <summary>One ObjectAssembly object: where it comes from, what it is called, what it writes.</summary>
    public sealed class OAVariant
    {
        public string key;            // menu / log name: "Lamp", "Camera"
        public string fbxPath;
        public string rootName;       // scene root and prefab root: "OA_Lamp"
        public string scenePath;
        public string prefabPath;
        public string timelinePath;
        public string lightsClipPath;
        public string meshPathA;
        public string meshPathB;
        public string materialPath;   // the object's reveal material (a second one gets _B if the parts differ)
        public string debugId;
        public string debugLabel;
        public string phase1TaskId;
        public string phase1Label;
        public OAJointRule jointRule;

        /// <summary>
        /// Phase 0's line as each player hears it: 玩家A 跟玩家B 聽到不一樣的 VO. Two paths rather
        /// than one, because the two people in the room are told different things from the first
        /// sentence of the beat -- see the per-role dialogue note in the class remarks.
        ///
        /// Null means that recording does not exist yet. The deck, its speaker and its role gate
        /// are still built; <see cref="BuildObjectAssembly"/> names the gap in the log rather than
        /// leaving a silent beat to be discovered in the venue.
        /// </summary>
        public string phase0ClipA;

        public string phase0ClipB;

        public string tag => "OABuild/" + key;
    }

    /// <summary>
    /// Builds an ObjectAssembly variant: its demo scene and prefab from a two-part FBX, then
    /// verifies them. One build path for every variant in <see cref="Variants"/>; a variant is a
    /// row in that table, never a copy of this file.
    ///
    ///   Tools/ObjectAssembly/&lt;Variant&gt;/Build Demo   (refuses while any loaded scene has unsaved
    ///                                                 changes; reopens what was open afterwards)
    ///   Tools/ObjectAssembly/&lt;Variant&gt;/Verify Demo  (with that demo scene active)
    ///
    /// THE HALVES ARE BAKED, NOT INSTANCED. These FBXs are a scaled and/or 270-degree-rotated root
    /// with the other part parented under it. BreathingView offsets its Visual child in the root's
    /// local space, so a scaled or tipped root would turn a 2.5 cm bob into metres sideways. Each
    /// part's mesh is re-written into metres, upright, centred on its own bounds: the fragment root
    /// is unscaled and unrotated, the Visual child is identity, the convex collider uses that mesh.
    ///
    /// THE JOIN POINT follows the variant's <see cref="OAJointRule"/>. Each half gets a JoinPoint
    /// child there, so both coincide when assembled and OATrail runs between them.
    ///
    /// The Phase 0 timeline is created once and then only topped up (two role-gated AudioTracks, a
    /// "Lights Up" AnimationTrack, an OAAppearMarker), so a voice-over dropped into it later
    /// survives a rebuild.
    ///
    /// PHASE 0 IS PER ROLE, ON TWO DECKS. 「玩家a 跟玩家b 要可以聽到不一樣的 vo」 from the beat's
    /// first sentence, so there is no single shared Phase 0 line: "Phase0 VO A" and "Phase0 VO B"
    /// are two AudioTracks bound to two AudioSources, each standing behind a <see cref="RoleAudible"/>
    /// gate for one player. Timeline has no concept of a player, but every machine runs its own
    /// copy of the prefab with its own director, so two tracks on two sources are already two
    /// private decks on each headset and which one is heard is a purely local mute -- no network
    /// traffic, and one asset that is identical on both machines. See RoleAudible's remarks for
    /// why it mutes rather than stops, which matters here because the local role frequently
    /// resolves after the beat has started.
    ///
    /// A line BOTH players hear does not belong on these decks as two copies to keep in step: that
    /// is what Phase 2 is, and <see cref="OAController"/> plays it from the shared stamp through
    /// one ungated source.
    ///
    /// The single "Phase0 VO" track that came before this is migrated, not discarded: it is renamed
    /// to "Phase0 VO A" with whatever clip was on it, so nothing authored in the Timeline window
    /// is lost and the only new thing is B's deck.
    /// </summary>
    public static class BuildObjectAssembly
    {
        public const string Root = "Assets/_Interaction/ObjectAssembly";
        const string MeshFolder = Root + "/Meshes";
        const string MaterialFolder = Root + "/Materials";
        const string TimelineFolder = Root + "/Timeline";
        const string FloorMaterialPath = MaterialFolder + "/OA_Floor.mat";
        const string TrailMaterialPath = MaterialFolder + "/OATrailFlow.mat";

        /// <summary>Phase 0's two private decks, and the speakers they are bound to.</summary>
        public const string AudioTrackNameA = "Phase0 VO A";

        public const string AudioTrackNameB = "Phase0 VO B";

        /// <summary>
        /// What the one shared deck was called before phase 0 went per role. Still looked for, so
        /// a rebuild migrates it into A instead of leaving an orphan track sounding over B.
        /// </summary>
        const string LegacyAudioTrackName = "Phase0 VO";

        public const string LightsTrackName = "Lights Up";

        public static readonly OAVariant Lamp = new()
        {
            key = "Lamp",
            fbxPath = "Assets/_models/Lamp_2split/Lamp_2split.fbx",
            rootName = "OA_Lamp",
            scenePath = Root + "/OA_Lamp_Demo.unity",
            prefabPath = Root + "/Prefabs/OA_Lamp.prefab",
            timelinePath = TimelineFolder + "/OA_Lamp_Phase0.playable",
            lightsClipPath = TimelineFolder + "/OA_Lamp_LightsUp.anim",
            meshPathA = MeshFolder + "/OA_Lamp_A.asset",
            meshPathB = MeshFolder + "/OA_Lamp_B.asset",
            materialPath = MaterialFolder + "/OA_Lamp_URP.mat",
            debugId = "oa.lamp",
            debugLabel = "OA lamp -- claim, link, join, float",
            phase1TaskId = "oa.lamp.phase1",
            phase1Label = "OA lamp phase1 -- each player heard their line",
            jointRule = OAJointRule.ContactCentroid,

            // S2-2's phase 0 has not been recorded per player yet, so both decks carry S2_1_B --
            // the clip the single shared deck already held. The structure is real and the audio is
            // a placeholder; swap these two lines when the pair lands and re-run. Keeping the
            // placeholder rather than emptying the decks is deliberate: the lamp's "Lights Up" and
            // its OAAppearMarker both sit at 15.52s because that is where this clip ends, and an
            // empty deck would silently shorten the beat to the 3.5s fallback.
            phase0ClipA = "Assets/_Audio/S2_1_B.wav",
            phase0ClipB = "Assets/_Audio/S2_1_B.wav",
        };

        public static readonly OAVariant Camera = new()
        {
            key = "Camera",
            fbxPath = "Assets/_models/Camera_2split/Camera_2split.fbx",
            rootName = "OA_Camera",
            scenePath = Root + "/OA_Camera_Demo.unity",
            prefabPath = Root + "/Prefabs/OA_Camera.prefab",
            timelinePath = TimelineFolder + "/OA_Camera_Phase0.playable",
            lightsClipPath = TimelineFolder + "/OA_Camera_LightsUp.anim",
            meshPathA = MeshFolder + "/OA_Camera_A.asset",
            meshPathB = MeshFolder + "/OA_Camera_B.asset",
            materialPath = MaterialFolder + "/OA_Camera_URP.mat",
            debugId = "oa.camera",
            debugLabel = "OA camera -- claim, link, join, float",
            phase1TaskId = "oa.camera.phase1",
            phase1Label = "OA camera phase1 -- each player heard their line",
            jointRule = OAJointRule.FaceOfBTowardA,

            // S2-3's phase 0 has no recording at all, per player or shared -- the shared deck was
            // empty before this too. Both decks are built and gated and stay empty until the pair
            // exists; the run reports it by name. S2-3-AB.wav is NOT a stand-in: it is 191s of the
            // 隱藏的記憶 narration already playing on Main's 共同 deck at 853s, and putting it here
            // would play it twice.
            phase0ClipA = null,
            phase0ClipB = null,
        };

        public static readonly OAVariant[] Variants = { Lamp, Camera };

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

        // ---- menus -----------------------------------------------------------------------------

        [MenuItem("Tools/ObjectAssembly/Lamp/Build Demo")] public static void BuildLamp() => BuildDemo(Lamp);
        [MenuItem("Tools/ObjectAssembly/Lamp/Verify Demo")] public static void VerifyLamp() => VerifyDemo(Lamp);
        [MenuItem("Tools/ObjectAssembly/Lamp/Upgrade Phase 0 Voices")] public static void UpgradeLamp() => UpgradePhase0Voices(Lamp);
        [MenuItem("Tools/ObjectAssembly/Camera/Build Demo")] public static void BuildCamera() => BuildDemo(Camera);
        [MenuItem("Tools/ObjectAssembly/Camera/Verify Demo")] public static void VerifyCamera() => VerifyDemo(Camera);
        [MenuItem("Tools/ObjectAssembly/Camera/Upgrade Phase 0 Voices")] public static void UpgradeCamera() => UpgradePhase0Voices(Camera);

        [MenuItem("Tools/ObjectAssembly/Upgrade Phase 0 Voices (all variants)")]
        public static void UpgradeAllPhase0Voices()
        {
            foreach (OAVariant v in Variants) UpgradePhase0Voices(v);
        }

        /// <summary>
        /// Gives an EXISTING prefab and timeline phase 0's two role-gated decks, in place.
        ///
        /// WHY THIS IS NOT JUST "Build Demo" AGAIN. The full build writes the prefab from a fresh
        /// hierarchy through SaveAsPrefabAssetAndConnect, which hands every object in it a new
        /// local file id. MainScene carries these two prefabs as instances with per-beat overrides
        /// on their controllers -- m_BeatRoot, m_Phase1ClipP1/P2, m_Phase2Clip, the onCompleted
        /// call -- and every one of those overrides is addressed BY that file id. Rebuilding the
        /// prefab to add two speakers would therefore silently drop the whole S2 integration, and
        /// the first sign of it would be a beat that never starts in the venue. This touches only
        /// what it has to and leaves every id alone, so the instances inherit the two new speakers
        /// and keep their overrides.
        ///
        /// Re-runnable: it finds the decks and speakers by name, migrates the single shared deck
        /// if that is still what is there, and leaves any clip already authored on a deck as it is.
        /// It does not open or touch any scene.
        /// </summary>
        public static void UpgradePhase0Voices(OAVariant v)
        {
            string tag = v.tag;
            var failures = new List<string>();

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(v.timelinePath);
            if (timeline == null)
            {
                Debug.LogError($"[{tag}] RESULT: FAIL - no timeline at {v.timelinePath}. " +
                               "Run Build Demo first; there is nothing to upgrade.");
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(v.prefabPath);
            if (contents == null)
            {
                Debug.LogError($"[{tag}] RESULT: FAIL - no prefab at {v.prefabPath}.");
                return;
            }

            try
            {
                EnsurePhase0Tracks(timeline, v, out AudioTrack voiceA, out AudioTrack voiceB);

                var director = contents.GetComponentInChildren<PlayableDirector>(true);
                if (!InteractionBuildUtil.Check(tag, director != null, "prefab has a PlayableDirector", failures))
                {
                    InteractionBuildUtil.Verdict(tag, failures);
                    return;
                }

                Transform holder = director.transform;

                // The old shared speaker becomes A, keeping its file id -- so the binding that
                // already points at it survives and A's deck needs no rebinding at all.
                Transform legacy = holder.Find(LegacyAudioTrackName);
                if (legacy != null && holder.Find(AudioTrackNameA) == null)
                {
                    legacy.name = AudioTrackNameA;
                    Debug.Log($"[{tag}] renamed the shared speaker '{LegacyAudioTrackName}' to " +
                              $"'{AudioTrackNameA}'.");
                }

                AudioSource sourceA = EnsureSpeaker(holder, AudioTrackNameA, tag);
                AudioSource sourceB = EnsureSpeaker(holder, AudioTrackNameB, tag);
                GateVoice(sourceA, PlayerRole.Player1, tag);
                GateVoice(sourceB, PlayerRole.Player2, tag);

                director.SetGenericBinding(voiceA, sourceA);
                director.SetGenericBinding(voiceB, sourceB);
                EditorUtility.SetDirty(director);

                InteractionBuildUtil.Check(tag, director.GetGenericBinding(voiceA) as AudioSource == sourceA,
                                           $"{AudioTrackNameA} bound to '{sourceA.name}'", failures);
                InteractionBuildUtil.Check(tag, director.GetGenericBinding(voiceB) as AudioSource == sourceB,
                                           $"{AudioTrackNameB} bound to '{sourceB.name}'", failures);
                InteractionBuildUtil.Check(tag, GatedRole(sourceA) == PlayerRole.Player1,
                                           "deck A audible to Player1 only", failures);
                InteractionBuildUtil.Check(tag, GatedRole(sourceB) == PlayerRole.Player2,
                                           "deck B audible to Player2 only", failures);

                PrefabUtility.SaveAsPrefabAsset(contents, v.prefabPath, out bool prefabOk);
                InteractionBuildUtil.Check(tag, prefabOk, $"saved {v.prefabPath}", failures);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();

            float appear = AppearMarkerOf(timeline);
            foreach (TrackAsset t in timeline.GetOutputTracks())
            {
                if (t is AudioTrack at && (at.name == AudioTrackNameA || at.name == AudioTrackNameB))
                    WarnIfVoiceOutrunsAppear(tag, at.name, at, appear);
            }

            InteractionBuildUtil.Verdict(tag, failures);
        }

        /// <summary>
        /// One phase-0 speaker, found by name or made. playOnAwake stays off: the inspector's
        /// leftover clip would otherwise sound the moment the prefab wakes, long before the beat.
        /// </summary>
        static AudioSource EnsureSpeaker(Transform parent, string name, string tag)
        {
            Transform holder = parent.Find(name);
            if (holder == null)
            {
                var made = new GameObject(name);
                made.transform.SetParent(parent, false);
                holder = made.transform;
                Debug.Log($"[{tag}] created speaker '{name}'.");
            }

            holder.gameObject.SetActive(true);
            var source = holder.GetComponent<AudioSource>();
            if (source == null) source = holder.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            return source;
        }

        public static OAVariant VariantForScene(string scenePath)
        {
            foreach (OAVariant v in Variants) if (v.scenePath == scenePath) return v;
            return null;
        }

        public static void BuildDemo(OAVariant variant)
        {
            if (InteractionBuildUtil.AnySceneDirty(variant.tag))
            {
                Debug.LogError($"[{variant.tag}] RESULT: FAIL - a loaded scene has unsaved changes; this builder " +
                               "replaces the open scene and never saves somebody else's work.");
                return;
            }

            var reopen = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && !string.IsNullOrEmpty(s.path) && s.path != variant.scenePath) reopen.Add(s.path);
            }

            try
            {
                Build(variant);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[{variant.tag}] RESULT: FAIL - exception: {e.Message}");
            }
            finally
            {
                Reopen(reopen, variant.tag);
            }
        }

        public static void VerifyDemo(OAVariant variant)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != variant.scenePath)
            {
                Debug.LogError($"[{variant.tag}] RESULT: FAIL - open {variant.scenePath} first.");
                return;
            }
            var failures = new List<string>();
            Verify(variant, scene, failures);
            InteractionBuildUtil.Verdict(variant.tag, failures);
        }

        static void Reopen(List<string> paths, string tag)
        {
            if (paths.Count == 0 || Application.isBatchMode) return;
            EditorSceneManager.OpenScene(paths[0], OpenSceneMode.Single);
            for (int i = 1; i < paths.Count; i++) EditorSceneManager.OpenScene(paths[i], OpenSceneMode.Additive);
            Debug.Log($"[{tag}] reopened {string.Join(", ", paths)}.");
        }

        // ---- build -----------------------------------------------------------------------------

        static void Build(OAVariant v)
        {
            string tag = v.tag;
            var failures = new List<string>();

            if (!InteractionBuildUtil.RevealShaderOk(out string shaderProblem))
            {
                Debug.LogError($"[{tag}] RESULT: FAIL - {InteractionBuildUtil.RevealShaderName} {shaderProblem}.");
                return;
            }

            // 1. The two parts.
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(v.fbxPath);
            List<Part> parts = fbx != null ? ReadParts(fbx) : new List<Part>();
            if (!InteractionBuildUtil.Check(tag, parts.Count == 2,
                    $"{v.fbxPath} has exactly two mesh parts (found {parts.Count}: " +
                    $"{string.Join(", ", parts.ConvertAll(p => p.name))})", failures))
            {
                InteractionBuildUtil.Verdict(tag, failures);
                return;
            }

            // 2. The join point, in the FBX's assembled layout (world space of a fresh instance).
            Vector3 joint = FindJoint(v.jointRule, parts[0], parts[1], out string how);
            Debug.Log($"[{tag}] parts {parts[0].name} + {parts[1].name}; join point {joint:F4} ({how}).");

            // 3. A fresh scene FIRST. Opening a scene in Single mode unloads every asset nothing
            // references, and an asset created a moment earlier is exactly that -- the director
            // was once handed a dead TimelineAsset and saved a null. Assets are made after this.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.035f, 0.035f, 0.045f);
            RenderSettings.fog = false;

            // 4. Baked meshes, materials, timeline.
            Directory.CreateDirectory(MeshFolder);
            Directory.CreateDirectory(MaterialFolder);
            Directory.CreateDirectory(TimelineFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(v.prefabPath) ?? Root);

            var centres = new Vector3[2];
            var meshes = new Mesh[2];
            var materials = new Material[2];
            string[] meshPaths = { v.meshPathA, v.meshPathB };
            for (int i = 0; i < 2; i++)
            {
                centres[i] = WorldBoundsCentre(parts[i]);
                meshes[i] = BakeMesh(parts[i], centres[i], meshPaths[i]);
            }
            materials[0] = InteractionBuildUtil.MakeFragmentMaterial(parts[0].material, v.materialPath);
            materials[1] = parts[1].material == parts[0].material
                ? materials[0]
                : InteractionBuildUtil.MakeFragmentMaterial(parts[1].material, v.materialPath.Replace(".mat", "_B.mat"));
            Material floorMat = MakeFloorMaterial();
            AnimationClip lightsClip = MakeLightsClip(v.lightsClipPath);
            TimelineAsset timeline = EnsureTimeline(v, lightsClip, out AudioTrack voiceTrackA,
                                                    out AudioTrack voiceTrackB, out AnimationTrack lightsTrack);
            AssetDatabase.SaveAssets();

            var environment = new GameObject("Environment");
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(environment.transform, false);
            floor.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            var root = new GameObject(v.rootName);
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
                visual.AddComponent<MeshRenderer>().sharedMaterial = materials[i];
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

            // Phase 0 is two private decks, one per player, each muted on the other headset.
            AudioSource phase0A = MakeVoice(ctrlGo.transform, AudioTrackNameA);
            AudioSource phase0B = MakeVoice(ctrlGo.transform, AudioTrackNameB);
            GateVoice(phase0A, PlayerRole.Player1, tag);
            GateVoice(phase0B, PlayerRole.Player2, tag);

            AudioSource phase1 = MakeVoice(ctrlGo.transform, "Phase1 VO");
            AudioSource phase2 = MakeVoice(ctrlGo.transform, "Phase2 VO");
            director.SetGenericBinding(voiceTrackA, phase0A);
            director.SetGenericBinding(voiceTrackB, phase0B);
            director.SetGenericBinding(lightsTrack, lightsAnimator);

            for (int i = 0; i < 2; i++)
            {
                var layout = new Pose(centres[i] - joint, Quaternion.identity);
                fragments[i].Configure(controller, joinPoints[i], views[i], layout, FollowOffset);
                EditorUtility.SetDirty(fragments[i]);
            }
            controller.Configure(fragments[0], fragments[1], trail, cornerP1, cornerP2, destination, director, phase1, phase2);
            var cso = new SerializedObject(controller);
            cso.FindProperty("m_DebugId").stringValue = v.debugId;
            cso.FindProperty("m_DebugLabel").stringValue = v.debugLabel;
            cso.FindProperty("m_Phase1TaskId").stringValue = v.phase1TaskId;
            cso.FindProperty("m_Phase1GateLabel").stringValue = v.phase1Label;
            cso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);

            // Bootstrap (network manager, session, rig, staff + debug panels...).
            GameObject bootstrap = InteractionBuildUtil.BuildDemoBootstrap(scene, SpawnP1, SpawnP2, tag);
            InteractionBuildUtil.Check(tag, bootstrap != null, "demo bootstrap built", failures);

            // 5. Save: scene, prefab, hashes, scene again.
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, v.scenePath))
            {
                InteractionBuildUtil.Check(tag, false, $"saved {v.scenePath}", failures);
                InteractionBuildUtil.Verdict(tag, failures);
                return;
            }

            PrefabUtility.SaveAsPrefabAssetAndConnect(root, v.prefabPath, InteractionMode.AutomatedAction, out bool prefabOk);
            InteractionBuildUtil.Check(tag, prefabOk, $"saved {v.prefabPath}", failures);

            InteractionBuildUtil.RegenerateNetworkObjectHashes(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // 6. Verify.
            Verify(v, scene, failures);
            InteractionBuildUtil.Verdict(tag, failures);
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
                // A skinned part would not bake this way; count it so the two-part check fails loudly.
                foreach (SkinnedMeshRenderer smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    parts.Add(new Part { name = (smr.sharedMesh != null ? smr.sharedMesh.name : smr.name) + " (skinned)" });
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

        static Bounds WorldBounds(Part p)
        {
            Vector3[] v = WorldVertices(p);
            var b = new Bounds(v[0], Vector3.zero);
            foreach (Vector3 x in v) b.Encapsulate(x);
            return b;
        }

        static Vector3 WorldBoundsCentre(Part p) => WorldBounds(p).center;

        static Vector3 FindJoint(OAJointRule rule, Part a, Part b, out string how)
        {
            if (rule == OAJointRule.FaceOfBTowardA)
            {
                Bounds ba = WorldBounds(a), bb = WorldBounds(b);
                Vector3 toA = ba.center - bb.center;
                int axis = 0;
                float best = -1f;
                for (int i = 0; i < 3; i++)
                {
                    float r = Mathf.Abs(toA[i]) / Mathf.Max(bb.extents[i], 1e-5f);
                    if (r > best) { best = r; axis = i; }
                }
                Vector3 face = bb.center;
                face[axis] = toA[axis] >= 0f ? bb.max[axis] : bb.min[axis];
                how = $"face of {b.name}'s bounds toward {a.name}, axis {"xyz"[axis]}";
                return face;
            }

            Vector3 centroid = ContactCentroid(a, b, out int contacts, out float closest);
            how = $"contact centroid of {contacts} vertex pairs, closest {closest * 1000f:0.0} mm";
            return centroid;
        }

        /// <summary>Centroid of the contact between two parts; the closest vertex pair if they never come within tolerance.</summary>
        static Vector3 ContactCentroid(Part a, Part b, out int contacts, out float closest)
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

        static AnimationClip MakeLightsClip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool created = clip == null;
            if (created) clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(path) };
            clip.ClearCurves();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Key", typeof(Light), "m_Intensity"),
                                            AnimationCurve.EaseInOut(0f, 0f, LightsDuration, KeyIntensity));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Fill", typeof(Light), "m_Intensity"),
                                            AnimationCurve.EaseInOut(0f, 0f, LightsDuration, FillIntensity));
            if (created) AssetDatabase.CreateAsset(clip, path);
            else EditorUtility.SetDirty(clip);
            return clip;
        }

        static TimelineAsset EnsureTimeline(OAVariant v, AnimationClip lightsClip, out AudioTrack voiceA,
                                            out AudioTrack voiceB, out AnimationTrack lightsTrack)
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(v.timelinePath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                timeline.name = Path.GetFileNameWithoutExtension(v.timelinePath);
                AssetDatabase.CreateAsset(timeline, v.timelinePath);
            }

            EnsurePhase0Tracks(timeline, v, out voiceA, out voiceB);

            lightsTrack = null;
            foreach (TrackAsset track in timeline.GetOutputTracks())
                if (lightsTrack == null && track is AnimationTrack l && track.name == LightsTrackName) lightsTrack = l;
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
            SetPostExtrapolation(lightsTimelineClip, TimelineClip.ClipExtrapolation.Hold, v.tag);

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

        /// <summary>
        /// Phase 0's two decks on the asset, migrating the single shared one if that is what is
        /// there. Shared by the full build and by the in-place upgrade, so there is exactly one
        /// definition of what a phase-0 deck is.
        /// </summary>
        static void EnsurePhase0Tracks(TimelineAsset timeline, OAVariant v,
                                       out AudioTrack voiceA, out AudioTrack voiceB)
        {
            voiceA = null;
            voiceB = null;
            AudioTrack legacy = null;
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (!(track is AudioTrack a)) continue;
                if (voiceA == null && a.name == AudioTrackNameA) voiceA = a;
                else if (voiceB == null && a.name == AudioTrackNameB) voiceB = a;
                else if (legacy == null && a.name == LegacyAudioTrackName) legacy = a;
            }

            // Migrate the one shared deck into A rather than adding two beside it. A renamed track
            // is the SAME object, so the director's existing binding to the old "Phase0 VO" speaker
            // still points at it, and whatever clip was authored on it stays exactly where it was.
            if (voiceA == null && legacy != null)
            {
                legacy.name = AudioTrackNameA;
                voiceA = legacy;
                Debug.Log($"[{v.tag}] migrated the shared '{LegacyAudioTrackName}' track to " +
                          $"'{AudioTrackNameA}'; its clip and its binding are untouched.");
            }

            if (voiceA == null) voiceA = timeline.CreateTrack<AudioTrack>(null, AudioTrackNameA);
            if (voiceB == null) voiceB = timeline.CreateTrack<AudioTrack>(null, AudioTrackNameB);

            EnsureVoiceClip(voiceA, v.phase0ClipA, "A", v.tag);
            EnsureVoiceClip(voiceB, v.phase0ClipB, "B", v.tag);
            EditorUtility.SetDirty(timeline);
        }

        static void SetPostExtrapolation(TimelineClip clip, TimelineClip.ClipExtrapolation mode, string tag)
        {
            PropertyInfo prop = typeof(TimelineClip).GetProperty("postExtrapolationMode",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo setter = prop?.GetSetMethod(true);
            if (setter != null) setter.Invoke(clip, new object[] { mode });
            else Debug.LogWarning($"[{tag}] could not set post-extrapolation on '{clip.displayName}'.");
        }

        /// <summary>
        /// Tops one phase-0 deck up from the variant table, and says what it did.
        ///
        /// A deck that already holds a clip is left EXACTLY as it is -- not replaced, not resized.
        /// The lamp's line was dragged to 0 and its lights were timed to where it ends; a builder
        /// that rewrote the clip on every run would quietly undo that the first time somebody
        /// nudged it in the Timeline window.
        ///
        /// A null path is a recording that does not exist yet, not an error: the deck, the speaker
        /// and the role gate are built anyway so the pair can be dropped in later, and the gap is
        /// logged by name. A placeholder nobody is told about is a placeholder that ships.
        /// </summary>
        static void EnsureVoiceClip(AudioTrack track, string clipPath, string label, string tag)
        {
            foreach (TimelineClip existing in track.GetClips())
            {
                Debug.Log($"[{tag}] phase 0 deck {label} already holds '{existing.displayName}' " +
                          $"({existing.start:0.00}-{existing.end:0.00}s); left as authored.");
                return;
            }

            if (string.IsNullOrEmpty(clipPath))
            {
                Debug.LogWarning($"[{tag}] phase 0 deck {label} is EMPTY: no recording for this " +
                                 "player yet. The track, its speaker and its role gate are built, " +
                                 "so dropping the clip in is all that is left -- but until then " +
                                 $"player {label} hears nothing in phase 0.");
                return;
            }

            var audio = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            if (audio == null)
            {
                Debug.LogWarning($"[{tag}] phase 0 deck {label}: nothing at '{clipPath}', so the " +
                                 "deck is empty. Check the path in the variant table.");
                return;
            }

            TimelineClip clip = track.CreateClip<AudioPlayableAsset>();
            ((AudioPlayableAsset)clip.asset).clip = audio;
            clip.start = 0d;
            clip.duration = audio.length;          // sized to the wav: never cut a line mid-word
            clip.displayName = audio.name;
            Debug.Log($"[{tag}] phase 0 deck {label}: '{audio.name}' 0.00-{audio.length:0.00}s.");
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

        /// <summary>
        /// The role gate on one phase-0 deck: this speaker is heard by this player and muted on
        /// the other headset. Written through a SerializedProperty because the field is private --
        /// and read back the same way by <see cref="Verify"/>, since two decks gated to the SAME
        /// player is the failure that sounds perfect on one headset.
        /// </summary>
        static void GateVoice(AudioSource source, PlayerRole role, string tag)
        {
            var gate = source.GetComponent<RoleAudible>();
            if (gate == null) gate = source.gameObject.AddComponent<RoleAudible>();

            var so = new SerializedObject(gate);
            SerializedProperty roles = so.FindProperty("m_AudibleToRoles");
            roles.arraySize = 1;
            roles.GetArrayElementAtIndex(0).intValue = (int)role;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[{tag}] '{source.name}' is audible to {role} only.");
        }

        /// <summary>The single role a phase-0 speaker is gated to, or null if it is not gated.</summary>
        static PlayerRole? GatedRole(AudioSource source)
        {
            var gate = source != null ? source.GetComponent<RoleAudible>() : null;
            if (gate == null) return null;

            SerializedProperty roles = new SerializedObject(gate).FindProperty("m_AudibleToRoles");
            if (roles == null || roles.arraySize != 1) return null;
            return (PlayerRole)roles.GetArrayElementAtIndex(0).intValue;
        }

        static string Describe(PlayerRole? role) =>
            role.HasValue ? role.Value.ToString() : "ungated or gated to more than one role";

        /// <summary>When the halves appear, from the asset, or the controller's fallback.</summary>
        static float AppearMarkerOf(TimelineAsset timeline)
        {
            if (timeline != null && timeline.markerTrack != null)
                foreach (IMarker m in timeline.markerTrack.GetMarkers())
                    if (m is OAAppearMarker) return (float)m.time;
            return AppearMarkerTime;
        }

        /// <summary>
        /// Says so when a deck is still talking as the halves appear.
        ///
        /// The two players' lines are DIFFERENT recordings and so different lengths, but there is
        /// only one OAAppearMarker and it is on the shared clock -- it has to be, or the two
        /// headsets would show the halves at different moments. So one player can be mid-sentence
        /// when the beat moves on, and the only way to know is to be told.
        /// </summary>
        static void WarnIfVoiceOutrunsAppear(string tag, string deck, AudioTrack track, float appear)
        {
            if (track == null) return;
            foreach (TimelineClip clip in track.GetClips())
            {
                if (clip.end <= appear + 0.01d) continue;
                Debug.LogWarning($"[{tag}] {deck}: '{clip.displayName}' runs to {clip.end:0.00}s but " +
                                 $"the halves appear at {appear:0.00}s, so that player is still " +
                                 "being talked to when the beat moves on. Move the OAAppearMarker " +
                                 "to the longer of the two lines, or trim the line.");
            }
        }

        static GameObject Child(Transform parent, string name, Vector3 worldPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPosition;
            return go;
        }

        // ---- verify ----------------------------------------------------------------------------

        static void Verify(OAVariant v, Scene scene, List<string> failures)
        {
            string tag = v.tag;
            bool C(bool ok, string what) => InteractionBuildUtil.Check(tag, ok, what, failures);

            GameObject root = null;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == v.rootName) root = go;
            if (!C(root != null, $"scene has /{v.rootName}")) return;

            C(root.GetComponent<NetworkObject>() == null, "root has no NetworkObject");
            C(PrefabUtility.IsPartOfPrefabInstance(root) &&
              PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == v.prefabPath,
              $"root is an instance of {v.prefabPath}");

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
            C(controller.debugId == v.debugId, $"debug id is '{v.debugId}' (is '{controller.debugId}')");
            C(controller.phase1TaskId == v.phase1TaskId, $"Phase 1 task id is '{v.phase1TaskId}' (is '{controller.phase1TaskId}')");
            C(controller.debugLabel == v.debugLabel, "debug label set");
            foreach (OAVariant other in Variants)
            {
                if (other == v) continue;
                C(other.debugId != v.debugId && other.phase1TaskId != v.phase1TaskId,
                  $"ids differ from the {other.key} variant's");
            }
            C(Mathf.Approximately(so.FindProperty("m_JoinDistance").floatValue, 0.12f), "join distance 0.12 m");

            OAFragment a = controller.fragmentA, b = controller.fragmentB;
            if (!C(a != null && b != null && a != b, "two distinct fragments")) return;

            foreach (OAFragment f in new[] { a, b })
            {
                InteractionBuildUtil.VerifyGrabbable(tag, f.gameObject, failures);
                C(f.controller == controller, $"{f.name}: controller wired");
                C(f.joinPoint != null && f.joinPoint.IsChildOf(f.transform), $"{f.name}: JoinPoint child");
                C(f.view != null && f.view.transform.parent == f.transform, $"{f.name}: BreathingView on the Visual child");
                C(f.transform.localScale == Vector3.one && f.transform.rotation == Quaternion.identity,
                  $"{f.name}: root unscaled and upright (BreathingView offsets are in root space)");
                if (f.view != null)
                {
                    Transform vt = f.view.transform;
                    C(vt.localPosition == Vector3.zero && vt.localRotation == Quaternion.identity && vt.localScale == Vector3.one,
                      $"{f.name}: Visual is identity");
                    var mf = vt.GetComponent<MeshFilter>();
                    string expectMesh = f == a ? v.meshPathA : v.meshPathB;
                    C(mf != null && mf.sharedMesh != null && AssetDatabase.GetAssetPath(mf.sharedMesh) == expectMesh,
                      $"{f.name}: mesh is {Path.GetFileName(expectMesh)}");
                    var mr = vt.GetComponent<MeshRenderer>();
                    Material m = mr != null ? mr.sharedMaterial : null;
                    C(InteractionBuildUtil.IsRevealMaterialOk(m) && m.IsKeywordEnabled("_EMISSION"),
                      $"{f.name}: reveal material with _ALPHATEST_ON and _EMISSION");
                    C(m != null && AssetDatabase.GetAssetPath(m).StartsWith(v.materialPath.Replace(".mat", "")),
                      $"{f.name}: material is {Path.GetFileName(v.materialPath)}");
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
            if (C(timeline != null && AssetDatabase.GetAssetPath(timeline) == v.timelinePath,
                  $"director plays {Path.GetFileName(v.timelinePath)}"))
            {
                C(!director.playOnAwake, "director does not play on awake");
                AudioTrack audioA = null; AudioTrack audioB = null; AnimationTrack anim = null;
                bool legacyAudio = false;
                foreach (TrackAsset t in timeline.GetOutputTracks())
                {
                    if (t is AudioTrack at && t.name == AudioTrackNameA) audioA = at;
                    if (t is AudioTrack bt && t.name == AudioTrackNameB) audioB = bt;
                    if (t is AudioTrack lt && lt.name == LegacyAudioTrackName) legacyAudio = true;
                    if (t is AnimationTrack an && t.name == LightsTrackName) anim = an;
                }

                var sourceA = audioA != null ? director.GetGenericBinding(audioA) as AudioSource : null;
                var sourceB = audioB != null ? director.GetGenericBinding(audioB) as AudioSource : null;
                C(sourceA != null, $"{AudioTrackNameA} bound to an AudioSource");
                C(sourceB != null, $"{AudioTrackNameB} bound to an AudioSource");
                C(!legacyAudio, $"no leftover shared '{LegacyAudioTrackName}' track sounding over the two decks");

                // Both decks gated to the same player is the failure that sounds perfect on one
                // headset: that player hears phase 0 twice and the other stands in silence, and
                // nothing on the first headset shows it. So read the roles back, do not trust them.
                PlayerRole? gateA = GatedRole(sourceA);
                PlayerRole? gateB = GatedRole(sourceB);
                C(gateA == PlayerRole.Player1, $"{AudioTrackNameA} is audible to Player1 only (is {Describe(gateA)})");
                C(gateB == PlayerRole.Player2, $"{AudioTrackNameB} is audible to Player2 only (is {Describe(gateB)})");
                C(sourceA != null && sourceB != null && sourceA != sourceB,
                  "the two phase-0 decks are bound to DIFFERENT speakers");

                // A deck longer than the beat's own opening talks over the halves appearing. Not a
                // failure -- it is a cueing decision -- but it has to be said out loud, because the
                // two lines are different lengths by design and only one appear time exists.
                float appear = AppearMarkerOf(timeline);
                WarnIfVoiceOutrunsAppear(v.tag, AudioTrackNameA, audioA, appear);
                WarnIfVoiceOutrunsAppear(v.tag, AudioTrackNameB, audioB, appear);

                C(anim != null && director.GetGenericBinding(anim) is Animator, "Lights Up track bound to the Lights animator");
                if (anim != null)
                {
                    bool hold = false;
                    string clipPath = null;
                    foreach (TimelineClip c in anim.GetClips())
                    {
                        hold = c.postExtrapolationMode == TimelineClip.ClipExtrapolation.Hold;
                        if (c.asset is AnimationPlayableAsset apa && apa.clip != null) clipPath = AssetDatabase.GetAssetPath(apa.clip);
                    }
                    C(hold, "lights clip holds after it ends");
                    C(clipPath == v.lightsClipPath, $"lights clip is {Path.GetFileName(v.lightsClipPath)} (is {clipPath})");
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

            InteractionBuildUtil.VerifyHashes(tag, scene, failures);
            InteractionBuildUtil.VerifyDemoBootstrap(tag, scene, failures);
        }
    }
}
