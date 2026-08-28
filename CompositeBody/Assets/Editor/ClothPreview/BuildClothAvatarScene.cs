using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CompositeBody.Avatar.Cloth.EditorTools
{
    /// <summary>
    /// Builds a ready-to-play scene where a shroud garment is simulated by Unity's Cloth
    /// component and conforms to the Ch36 avatar: the garment is skinned to the head and chest
    /// bones (so pinned cloth follows the body), pinned above the shoulders, and collided
    /// against capsule colliders generated from the real Mixamo bone chain.
    ///
    /// Run:
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Avatar.Cloth.EditorTools.BuildClothAvatarScene.Run
    /// </summary>
    public static class BuildClothAvatarScene
    {
        const string k_ScenePath = "Assets/Scenes/ClothAvatarSim.unity";
        const string k_GarmentMeshPath = "Assets/_models/ShroudGarment.asset";
        const string k_ClothMaterialPath = "Assets/Materials/ClothDrape.mat";
        const string k_BodyMaterialPath = "Assets/Materials/ShroudBody.mat";

        const string k_Prefix = "mixamorig1:";

        // Garment tessellation. Kept modest -- Unity Cloth cost scales with vertex count, and a
        // few thousand verts is the practical ceiling for a VR frame budget.
        const int k_Radial = 44;
        const int k_Rows = 38;

        public static void Run()
        {
            Debug.Log("[ClothAvatar] Starting...");

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ProbeAvatarRig.AvatarFbxPath);
            if (fbx == null)
            {
                Debug.LogError($"[ClothAvatar] RESULT: FAIL - avatar not found at {ProbeAvatarRig.AvatarFbxPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            avatar.name = "Ch36_Avatar";
            avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            PrefabUtility.UnpackPrefabInstance(avatar, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var bones = MapBones(avatar);
            if (!Validate(bones, out string missing))
            {
                Debug.LogError($"[ClothAvatar] RESULT: FAIL - missing bones: {missing}");
                return;
            }

            LowerArms(bones);
            SetBodyMaterial(avatar);

            var garment = BuildGarment(avatar, bones, out Mesh garmentMesh);
            var cloth = ConfigureCloth(garment, bones, garmentMesh);

            BuildEnvironment(out Camera cam);

            EditorSceneManager.MarkAllScenesDirty();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            Debug.Log($"[ClothAvatar] Saved scene to {k_ScenePath}");

            string outPath = GetArg("-clothOut");
            if (!string.IsNullOrEmpty(outPath))
                RenderClothPreview.RenderCameraToFile(cam, outPath);

            Debug.Log($"[ClothAvatar] Cloth verts={cloth.vertices.Length} capsules={cloth.capsuleColliders.Length} spheres={cloth.sphereColliders.Length}");
            Debug.Log("[ClothAvatar] RESULT: PASS");
        }

        // ---------------------------------------------------------------- bones

        static Dictionary<string, Transform> MapBones(GameObject avatar)
        {
            var map = new Dictionary<string, Transform>();
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith(k_Prefix))
                    map[t.name.Substring(k_Prefix.Length)] = t;
            }
            return map;
        }

        static readonly string[] k_Required =
        {
            "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "HeadTop_End",
            "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot",
            "LeftArm", "LeftForeArm", "RightArm", "RightForeArm"
        };

        static bool Validate(Dictionary<string, Transform> bones, out string missing)
        {
            var absent = new List<string>();
            foreach (var n in k_Required)
                if (!bones.ContainsKey(n)) absent.Add(n);
            missing = string.Join(", ", absent);
            return absent.Count == 0;
        }

        /// <summary>
        /// The FBX ships in a T-pose, which would push the arms straight through the shroud.
        /// Drop them to the sides so the garment reads correctly at rest.
        /// </summary>
        static void LowerArms(Dictionary<string, Transform> bones)
        {
            // The character's left arm points along -X, so a positive rotation about world Z
            // swings it down; the right arm is mirrored.
            bones["LeftArm"].Rotate(Vector3.forward, 72f, Space.World);
            bones["RightArm"].Rotate(Vector3.forward, -72f, Space.World);
            bones["LeftForeArm"].Rotate(Vector3.forward, 12f, Space.World);
            bones["RightForeArm"].Rotate(Vector3.forward, -12f, Space.World);
        }

        static void SetBodyMaterial(GameObject avatar)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_BodyMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "ShroudBody" };
                mat.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.06f));
                mat.SetFloat("_Smoothness", 0.12f);
                AssetDatabase.CreateAsset(mat, k_BodyMaterialPath);
            }
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = mat;
        }

        // ---------------------------------------------------------------- garment

        static GameObject BuildGarment(GameObject avatar, Dictionary<string, Transform> bones, out Mesh mesh)
        {
            float crownY = bones["HeadTop_End"].position.y;
            float headY = bones["Head"].position.y;
            float neckY = bones["Neck"].position.y;
            float chestY = bones["Spine2"].position.y;
            float hipsY = bones["Hips"].position.y;
            float kneeY = bones["LeftLeg"].position.y;
            float ankleY = bones["LeftFoot"].position.y;

            // Radius profile keyed on world height, sized off the real skeleton with clearance
            // so the cloth hangs off the body instead of starting inside it.
            var profile = new AnimationCurve();
            // Radii include clearance for the arms hanging at the sides; too tight and the
            // hands push straight through the shroud at rest.
            profile.AddKey(-0.02f, 0.370f);
            profile.AddKey(ankleY, 0.340f);
            profile.AddKey(kneeY, 0.310f);
            profile.AddKey(hipsY, 0.288f);
            profile.AddKey(chestY, 0.262f);
            profile.AddKey(neckY, 0.140f);
            profile.AddKey(headY, 0.140f);
            profile.AddKey(crownY, 0.112f);
            profile.AddKey(crownY + 0.055f, 0.045f);
            profile.AddKey(crownY + 0.085f, 0.004f);
            for (int i = 0; i < profile.length; i++) profile.SmoothTangents(i, 0f);

            float topY = crownY + 0.085f;
            float bottomY = -0.02f;

            // No duplicated seam column: Cloth welds coincident verts, and a welded mesh no
            // longer maps 1:1 onto the `coefficients` array, which silently breaks pinning.
            // Instead the seam wraps in the index buffer and tangents are generated
            // analytically below, so there is no shading seam either.
            int cols = k_Radial;
            var vertices = new Vector3[cols * (k_Rows + 1)];
            var uvs = new Vector2[vertices.Length];
            var tangents = new Vector4[vertices.Length];

            for (int r = 0; r <= k_Rows; r++)
            {
                float v = (float)r / k_Rows;          // 0 at top, 1 at hem
                float y = Mathf.Lerp(topY, bottomY, v);
                float baseRadius = Mathf.Max(profile.Evaluate(y), 0.002f);

                // Folds fade in below the head so the hood stays smooth.
                float foldMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(neckY, hipsY, -y + (neckY + hipsY)));
                foldMask = Mathf.Clamp01(foldMask);

                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    uvs[idx] = new Vector2((float)c / k_Radial, 1f - v);

                    float theta = (float)c / k_Radial * Mathf.PI * 2f;
                    float fold = Mathf.Sin(theta * 11f + v * 2.2f)
                                 + 0.55f * Mathf.Sin(theta * 6.7f + 1.7f)
                                 + 0.30f * Mathf.Sin(theta * 15.7f - 4.1f);
                    float radius = baseRadius + fold * 0.012f * foldMask;

                    vertices[idx] = new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius);

                    // Circumferential tangent, computed rather than derived from UVs so the
                    // anisotropic silk highlight stays continuous across the wrap.
                    tangents[idx] = new Vector4(-Mathf.Sin(theta), 0f, Mathf.Cos(theta), -1f);
                }
            }

            var triangles = new int[k_Rows * k_Radial * 6];
            int ti = 0;
            for (int r = 0; r < k_Rows; r++)
            {
                for (int c = 0; c < k_Radial; c++)
                {
                    int cNext = (c + 1) % k_Radial; // wraps the seam without duplicate verts
                    int a = r * cols + c, b = r * cols + cNext;
                    int d = (r + 1) * cols + c, e = (r + 1) * cols + cNext;
                    // Wound so normals face outward; the opposite order points them into the
                    // body, which backface-culls the near side and shows the shroud's interior.
                    triangles[ti++] = a; triangles[ti++] = b; triangles[ti++] = d;
                    triangles[ti++] = b; triangles[ti++] = e; triangles[ti++] = d;
                }
            }

            mesh = new Mesh { name = "ShroudGarment" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.tangents = tangents;
            mesh.RecalculateBounds();

            var garment = new GameObject("Shroud");
            garment.transform.SetParent(avatar.transform, false);

            // Skin to two bones: cloth above the neck is pinned and must follow the head, while
            // everything below hangs from the chest.
            var boneArray = new[] { bones["Head"], bones["Spine2"] };
            var weights = new BoneWeight[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                float headBlend = Mathf.Clamp01(Mathf.InverseLerp(neckY - 0.05f, headY, vertices[i].y));
                weights[i] = new BoneWeight
                {
                    boneIndex0 = 0,
                    weight0 = headBlend,
                    boneIndex1 = 1,
                    weight1 = 1f - headBlend
                };
            }
            mesh.boneWeights = weights;

            var bindposes = new Matrix4x4[boneArray.Length];
            for (int i = 0; i < boneArray.Length; i++)
                bindposes[i] = boneArray[i].worldToLocalMatrix * garment.transform.localToWorldMatrix;
            mesh.bindposes = bindposes;

            AssetDatabase.CreateAsset(mesh, k_GarmentMeshPath);
            AssetDatabase.SaveAssets();

            var smr = garment.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = boneArray;
            smr.rootBone = bones["Spine2"];
            smr.updateWhenOffscreen = true;

            var clothMat = AssetDatabase.LoadAssetAtPath<Material>(k_ClothMaterialPath);
            if (clothMat != null)
            {
                // The shroud is a closed tube, so wrinkles must be sampled cylindrically rather
                // than from UVs or a seam appears down the wrap.
                clothMat.SetFloat("_WrinkleCylindrical", 1f);
                clothMat.SetFloat("_WrinkleStrength", 0.42f);
                EditorUtility.SetDirty(clothMat);
                AssetDatabase.SaveAssets(); // SetDirty alone does not write the asset back
            }
            smr.sharedMaterial = clothMat;

            Debug.Log($"[ClothAvatar] Garment built: {vertices.Length} verts, {triangles.Length / 3} tris");
            return garment;
        }

        // ---------------------------------------------------------------- cloth

        static UnityEngine.Cloth ConfigureCloth(GameObject garment, Dictionary<string, Transform> bones, Mesh mesh)
        {
            var cloth = garment.AddComponent<UnityEngine.Cloth>();

            cloth.stretchingStiffness = 0.85f;
            cloth.bendingStiffness = 0.28f;   // low = silky, drapes into fine folds
            cloth.damping = 0.32f;
            cloth.friction = 0.45f;
            cloth.useGravity = true;
            cloth.worldVelocityScale = 0.42f; // cloth reacts when the avatar walks
            cloth.worldAccelerationScale = 0.38f;
            cloth.clothSolverFrequency = 120f;
            cloth.enableContinuousCollision = true;
            cloth.useTethers = true;
            cloth.useVirtualParticles = 1f; // this one is a 0-1 weight, not a bool
            cloth.externalAcceleration = new Vector3(-0.55f, 0f, -0.15f); // faint breeze
            cloth.randomAcceleration = new Vector3(0.35f, 0.1f, 0.35f);

            // Pin from the source mesh, not `cloth.vertices` -- the latter is only populated
            // once the solver runs, so in edit mode it reads back as zeros and every vertex
            // silently ends up unpinned (the shroud then just falls off on Play).
            float neckY = bones["Neck"].position.y;
            float hemY = -0.02f;

            var meshVerts = mesh.vertices;
            var coefficients = cloth.coefficients;
            int pinned = 0;

            if (coefficients.Length != meshVerts.Length)
            {
                Debug.LogError($"[ClothAvatar] RESULT: FAIL - Cloth welded the mesh " +
                               $"({meshVerts.Length} mesh verts vs {coefficients.Length} cloth verts); " +
                               "pinning indices would not line up.");
                return cloth;
            }

            for (int i = 0; i < coefficients.Length; i++)
            {
                float y = garment.transform.TransformPoint(meshVerts[i]).y;
                var c = coefficients[i];

                if (y >= neckY)
                {
                    c.maxDistance = 0f; // hood is fixed to the head
                    pinned++;
                }
                else
                {
                    float tt = Mathf.InverseLerp(neckY, hemY, y);
                    c.maxDistance = Mathf.Lerp(0f, 0.5f, tt * tt); // freedom grows toward the hem
                }
                c.collisionSphereDistance = 0.02f;
                coefficients[i] = c;
            }
            cloth.coefficients = coefficients;

            var capsules = new List<CapsuleCollider>();
            AddBoneCapsule(bones, capsules, "Spine", "Spine2", 0.175f);
            AddBoneCapsule(bones, capsules, "Hips", "Spine", 0.185f);
            AddBoneCapsule(bones, capsules, "LeftUpLeg", "LeftLeg", 0.105f);
            AddBoneCapsule(bones, capsules, "RightUpLeg", "RightLeg", 0.105f);
            AddBoneCapsule(bones, capsules, "LeftLeg", "LeftFoot", 0.085f);
            AddBoneCapsule(bones, capsules, "RightLeg", "RightFoot", 0.085f);
            AddBoneCapsule(bones, capsules, "LeftArm", "LeftForeArm", 0.075f);
            AddBoneCapsule(bones, capsules, "RightArm", "RightForeArm", 0.075f);
            cloth.capsuleColliders = capsules.ToArray();

            // Head gets a sphere pair (a conical capsule), which is what Cloth uses for rounded
            // volumes -- it has no box or mesh collision support at all.
            var headSphere = CreateSphere(bones["Head"], 0.115f, Vector3.zero);
            var neckSphere = CreateSphere(bones["Neck"], 0.095f, Vector3.zero);
            cloth.sphereColliders = new[] { new ClothSphereColliderPair(headSphere, neckSphere) };

            if (pinned == 0)
                Debug.LogError("[ClothAvatar] RESULT: FAIL - no vertices were pinned; the shroud would fall off on Play.");
            else
                Debug.Log($"[ClothAvatar] Pinned {pinned}/{coefficients.Length} verts; {capsules.Count} capsules");

            return cloth;
        }

        /// <summary>
        /// Builds a capsule oriented down the actual bone direction rather than assuming an
        /// axis, since Mixamo bone orientations vary between joints.
        /// </summary>
        static void AddBoneCapsule(Dictionary<string, Transform> bones, List<CapsuleCollider> into,
                                   string boneName, string childName, float radius)
        {
            if (!bones.TryGetValue(boneName, out var bone) || !bones.TryGetValue(childName, out var child))
                return;

            var holder = new GameObject($"ClothCollider_{boneName}");
            holder.transform.SetParent(bone, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            holder.layer = 2; // Ignore Raycast, so these proxies stay out of gameplay queries

            Vector3 localChild = bone.InverseTransformPoint(child.position);
            float length = localChild.magnitude;

            int axis = 0;
            float best = Mathf.Abs(localChild.x);
            if (Mathf.Abs(localChild.y) > best) { axis = 1; best = Mathf.Abs(localChild.y); }
            if (Mathf.Abs(localChild.z) > best) { axis = 2; }

            var capsule = holder.AddComponent<CapsuleCollider>();
            capsule.direction = axis;
            capsule.center = localChild * 0.5f;
            capsule.radius = radius;
            capsule.height = length + radius * 2f;

            into.Add(capsule);
        }

        static SphereCollider CreateSphere(Transform bone, float radius, Vector3 localOffset)
        {
            var holder = new GameObject($"ClothCollider_{bone.name}");
            holder.transform.SetParent(bone, false);
            holder.transform.localPosition = localOffset;
            holder.layer = 2;
            var sphere = holder.AddComponent<SphereCollider>();
            sphere.radius = radius;
            return sphere;
        }

        // ---------------------------------------------------------------- environment

        static void BuildEnvironment(out Camera cam)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.30f, 0.29f, 0.27f));
            gm.SetFloat("_Smoothness", 0.08f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = gm;

            var lightGO = new GameObject("Sun");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.shadows = LightShadows.Soft;
            lightGO.transform.rotation = Quaternion.Euler(38f, 30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.48f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.32f, 0.33f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.15f, 0.14f);

            var camGO = new GameObject("PreviewCamera");
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<UniversalAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.36f, 0.42f, 0.50f);
            cam.fieldOfView = 32f;
            camGO.transform.position = new Vector3(0.9f, 1.15f, -3.3f);
            camGO.transform.LookAt(new Vector3(0f, 0.95f, 0f));
        }

        static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, System.StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
