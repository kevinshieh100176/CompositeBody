using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using CompositeBody.Avatar.Body;
using CompositeBody.Avatar.Cloth.EditorTools;
using CompositeBody.Avatar.Skin;
using XRMultiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Makes the Ch36 membrane figure the player's avatar.
    ///
    /// Until now the players were a head and two hands with membrane relaxed over them, because
    /// that is all the template's avatar contained and all that O-0 asked for. The figure that
    /// the membrane look was actually developed against is Ch36 -- it is what stands in
    /// MembraneCombineTest, and what the film settings were tuned on -- so this puts the same
    /// figure on the player instead of beside them.
    ///
    /// Three decisions worth knowing about:
    /// - The body is driven from the avatar's existing networked head and hands rather than from
    ///   anything new on the wire. See <see cref="MembraneBodyRig"/>.
    /// - The template's head and visor renderers are switched off rather than deleted. The
    ///   template's own scripts hold references to them -- the mouth blendshape, the shirt
    ///   colour, the local-player material swap -- and a deleted renderer turns those into null
    ///   reference exceptions on spawn.
    /// - The tracked hand visuals stay, and keep their films. They have live, networked finger
    ///   tracking that a body solved from three points cannot match, so the body collapses its
    ///   own hands and lets them do the hands. Their skin is re-materialled to the body's, so
    ///   that a hand and a torso read as one creature under the film.
    ///
    /// Edits the shared player prefab, so it changes the avatar in every scene, not only in the
    /// sample scene it was asked for. Idempotent: re-running replaces the body rather than
    /// adding a second one.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.ConfigureMembraneBody.Run
    /// </summary>
    public static class ConfigureMembraneBody
    {
        const string k_PrefabPath = "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRI_Network_Player_Avatar.prefab";
        const string k_MeshDir = "Assets/_models/AvatarMembrane";
        const string k_FilmMeshPath = k_MeshDir + "/MembraneBody_Ch36_MembraneFilm.asset";
        const string k_FilmMaterialPath = "Assets/Materials/MembraneAvatar.mat";
        const string k_BodyMaterialPath = "Assets/Materials/MembraneAvatarBody.mat";

        /// <summary>The object the whole body hangs under, and the handle this script finds it by
        /// again on a re-run.</summary>
        public const string BodyRootName = "MembraneBody";

        /// <summary>The template's host indicator, which this avatar does not wear.</summary>
        const string k_CrownName = "Host_Crown";

        public const string FilmName = "MembraneBody_Ch36_MembraneFilm";

        // The settings the membrane look was tuned at in MembraneCombineTest. A film is a shape,
        // not a shader effect, so keeping these in step with that scene is what keeps the player
        // and the figures standing in front of them looking like the same material.
        const float k_BaseOffset = 0.020f;
        const float k_MinOffset = 0.006f;
        const int k_SmoothIterations = 900;
        const float k_SmoothLambda = 0.60f;

        /// <summary>Mixamo's prefix on every bone in this rig.</summary>
        const string k_BonePrefix = "mixamorig1:";

        /// <summary>Read-only dump of what is on the avatar now, and what the rig resolved.</summary>
        public static void Probe()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[MembraneBody] RESULT: FAIL - prefab not found at {k_PrefabPath}");
                return;
            }

            Transform body = prefab.transform.Find(BodyRootName);
            Debug.Log($"[MembraneBody] body root: {(body != null ? "present" : "absent")}");

            foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Debug.Log($"[MembraneBody] SMR '{smr.name}' mesh='{(smr.sharedMesh != null ? smr.sharedMesh.name : "null")}' " +
                          $"verts={(smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0)} " +
                          $"bones={smr.bones.Length} enabled={smr.enabled} " +
                          $"mat='{(smr.sharedMaterial != null ? smr.sharedMaterial.name : "null")}'");
            }

            var rig = prefab.GetComponentInChildren<MembraneBodyRig>(true);
            Debug.Log(rig != null
                ? $"[MembraneBody] rig present, bones resolved={rig.bonesResolved}"
                : "[MembraneBody] no MembraneBodyRig on the avatar.");

            Debug.Log("[MembraneBody] RESULT: PASS");
        }

        public static void Run()
        {
            Debug.Log("[MembraneBody] Starting...");

            var membraneShader = Shader.Find("CompositeBody/VacuumMembrane");
            if (membraneShader == null)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - shader 'CompositeBody/VacuumMembrane' not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(membraneShader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(membraneShader))
                    Debug.LogError($"[MembraneBody] Shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[MembraneBody] RESULT: FAIL - the membrane shader has compile errors.");
                return;
            }

            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - URP/Lit not found.");
                return;
            }

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ProbeAvatarRig.AvatarFbxPath);
            if (fbx == null)
            {
                Debug.LogError($"[MembraneBody] RESULT: FAIL - avatar FBX not found at {ProbeAvatarRig.AvatarFbxPath}");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath) == null)
            {
                Debug.LogError($"[MembraneBody] RESULT: FAIL - prefab not found at {k_PrefabPath}");
                return;
            }

            Mesh film = LoadOrBuildFilm(fbx);
            if (film == null) return;

            Material filmMat = LoadFilmMaterial(membraneShader);
            Material bodyMat = LoadOrCreateBodyMaterial(litShader);

            if (!Attach(fbx, film, filmMat, bodyMat)) return;
            if (!Verify(film)) return;

            Debug.Log("[MembraneBody] RESULT: PASS");
        }

        /// <summary>
        /// Relaxes a film against the Ch36 mesh, or reuses the one on disk. The relaxation is 900
        /// passes over 16k vertices, and its result depends only on the mesh and the four
        /// settings above, so rebuilding it on every run would be a minute spent reproducing a
        /// file that already exists.
        /// </summary>
        static Mesh LoadOrBuildFilm(GameObject fbx)
        {
            var bodySmr = fbx.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (bodySmr == null || bodySmr.sharedMesh == null)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - the Ch36 FBX has no skinned mesh.");
                return null;
            }

            Mesh source = bodySmr.sharedMesh;

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(k_FilmMeshPath);
            if (existing != null && existing.vertexCount == source.vertexCount)
            {
                Debug.Log($"[MembraneBody] Reusing the film at {k_FilmMeshPath} ({existing.vertexCount} verts).");
                return existing;
            }

            Debug.Log($"[MembraneBody] Relaxing a film against '{source.name}' ({source.vertexCount} verts)...");

            Mesh built;
            try
            {
                built = MembraneShellBuilder.Build(source, k_BaseOffset, k_MinOffset,
                                                   k_SmoothIterations, k_SmoothLambda);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MembraneBody] RESULT: FAIL - could not relax the film: {e.Message}");
                return null;
            }

            if (built == null || built.vertexCount == 0)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - the relaxed film is empty.");
                return null;
            }

            built.name = FilmName;

            Directory.CreateDirectory(k_MeshDir);
            if (existing != null) AssetDatabase.DeleteAsset(k_FilmMeshPath);
            AssetDatabase.CreateAsset(built, k_FilmMeshPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[MembraneBody] Saved the film to {k_FilmMeshPath} ({built.vertexCount} verts).");
            return built;
        }

        /// <summary>
        /// The same film material the head and hands already use. One material for the whole
        /// creature, because the membrane is one sheet and two materials would be two things to
        /// keep in step.
        /// </summary>
        static Material LoadFilmMaterial(Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_FilmMaterialPath);
            if (mat != null) return mat;

            Debug.Log($"[MembraneBody] No film material at {k_FilmMaterialPath}; creating one.");
            mat = new Material(shader) { name = "MembraneAvatar" };
            AssetDatabase.CreateAsset(mat, k_FilmMaterialPath);
            return mat;
        }

        /// <summary>
        /// The body under the film: dark and matte, at the same values as the figures in
        /// MembraneCombineTest. The film's whole read is the difference between where it touches
        /// skin and where it spans air, and a bright body underneath flattens that out.
        /// </summary>
        static Material LoadOrCreateBodyMaterial(Shader litShader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_BodyMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(litShader) { name = "MembraneAvatarBody" };
                AssetDatabase.CreateAsset(mat, k_BodyMaterialPath);
            }
            else if (mat.shader != litShader)
            {
                mat.shader = litShader;
            }

            mat.SetColor("_BaseColor", new Color(0.085f, 0.082f, 0.088f));
            mat.SetFloat("_Smoothness", 0.14f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static bool Attach(GameObject fbx, Mesh film, Material filmMat, Material bodyMat)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(k_PrefabPath);

            try
            {
                // --- the three poses the body is solved from ---
                Transform headTarget = root.transform.Find("Head");
                Transform leftTarget = root.transform.Find("Left Hand");
                Transform rightTarget = root.transform.Find("Right Hand");

                if (headTarget == null || leftTarget == null || rightTarget == null)
                {
                    Debug.LogError("[MembraneBody] RESULT: FAIL - the avatar has no Head/Left Hand/Right Hand " +
                                   "transforms, which are the poses the body is solved from.");
                    return false;
                }

                // --- the body ---
                Transform stale = root.transform.Find(BodyRootName);
                if (stale != null)
                {
                    Debug.Log("[MembraneBody] Removing the previous body.");
                    Object.DestroyImmediate(stale.gameObject);
                }

                // A plain clone rather than a nested prefab instance: the body is edited here --
                // materials, an added film, a dozen wired bones -- and every one of those would
                // otherwise be a prefab override on an imported model.
                var body = (GameObject)Object.Instantiate(fbx);
                body.name = BodyRootName;
                body.transform.SetParent(root.transform, false);
                body.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                body.transform.localScale = Vector3.one;

                var bodySmr = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (bodySmr == null || bodySmr.sharedMesh == null)
                {
                    Debug.LogError("[MembraneBody] RESULT: FAIL - the cloned body has no skinned mesh.");
                    return false;
                }

                var bodyMats = new Material[bodySmr.sharedMesh.subMeshCount];
                for (int i = 0; i < bodyMats.Length; i++) bodyMats[i] = bodyMat;
                bodySmr.sharedMaterials = bodyMats;

                // The body is inside a film, so it is lit by whatever reaches it through the
                // film. Its own shadow would be cast onto the inside of that film.
                bodySmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bodySmr.updateWhenOffscreen = true;

                // --- the film over it ---
                var filmGO = new GameObject(FilmName);
                filmGO.transform.SetParent(body.transform, false);

                var filmSmr = filmGO.AddComponent<SkinnedMeshRenderer>();
                filmSmr.sharedMesh = film;
                filmSmr.bones = bodySmr.bones;
                filmSmr.rootBone = bodySmr.rootBone;
                filmSmr.sharedMaterial = filmMat;

                // A skinned membrane that is not told to recompute its bounds culls on its
                // bind-pose ones, which in a headset reads as the body blinking out as you turn.
                filmSmr.updateWhenOffscreen = true;
                filmSmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                filmSmr.receiveShadows = false;

                var link = filmGO.AddComponent<MembraneFilmLink>();
                var linkSo = new SerializedObject(link);
                linkSo.FindProperty("m_Source").objectReferenceValue = bodySmr;
                linkSo.FindProperty("m_Film").objectReferenceValue = filmSmr;
                linkSo.ApplyModifiedPropertiesWithoutUndo();

                // --- the solve ---
                if (!WireRig(body, bodySmr, headTarget, leftTarget, rightTarget)) return false;

                // --- what the body now replaces ---
                HideTemplateHead(root);
                HideFloatingUi(root);
                MatchHandsToBody(root, bodyMat);

                PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[MembraneBody] Saved the avatar prefab.");
            return true;
        }

        static bool WireRig(GameObject body, SkinnedMeshRenderer bodySmr,
                            Transform head, Transform left, Transform right)
        {
            var rig = body.AddComponent<MembraneBodyRig>();
            var so = new SerializedObject(rig);

            so.FindProperty("m_HeadTarget").objectReferenceValue = head;
            so.FindProperty("m_LeftHandTarget").objectReferenceValue = left;
            so.FindProperty("m_RightHandTarget").objectReferenceValue = right;

            // Bound by name against the renderer's own bone list rather than by walking the
            // hierarchy: these are the bones the mesh is actually skinned to, so a name that does
            // not appear here would be a bone the body does not move.
            var bones = new Dictionary<string, Transform>();
            foreach (var bone in bodySmr.bones)
            {
                if (bone == null) continue;
                string name = bone.name.StartsWith(k_BonePrefix) ? bone.name.Substring(k_BonePrefix.Length) : bone.name;
                bones[name] = bone;
            }

            // Mixamo's names: 'Arm' is the upper arm and 'Shoulder' is the clavicle.
            var wanted = new (string property, string bone)[]
            {
                ("m_Hips", "Hips"),
                ("m_Chest", "Spine2"),
                ("m_Neck", "Neck"),
                ("m_HeadBone", "Head"),
                ("m_LeftShoulder", "LeftShoulder"),
                ("m_RightShoulder", "RightShoulder"),
                ("m_LeftUpperArm", "LeftArm"),
                ("m_LeftForeArm", "LeftForeArm"),
                ("m_LeftHandBone", "LeftHand"),
                ("m_RightUpperArm", "RightArm"),
                ("m_RightForeArm", "RightForeArm"),
                ("m_RightHandBone", "RightHand"),
                ("m_LeftUpperLeg", "LeftUpLeg"),
                ("m_LeftLowerLeg", "LeftLeg"),
                ("m_LeftFoot", "LeftFoot"),
                ("m_RightUpperLeg", "RightUpLeg"),
                ("m_RightLowerLeg", "RightLeg"),
                ("m_RightFoot", "RightFoot"),
            };

            var missing = new List<string>();
            foreach (var (property, bone) in wanted)
            {
                if (!bones.TryGetValue(bone, out Transform t))
                {
                    missing.Add(bone);
                    continue;
                }
                so.FindProperty(property).objectReferenceValue = t;
            }

            if (missing.Count > 0)
            {
                Debug.LogError($"[MembraneBody] RESULT: FAIL - the rig is missing bones: {string.Join(", ", missing)}. " +
                               "A body with an unbound chain would hold that limb in its bind pose.");
                return false;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[MembraneBody] Wired the rig to {wanted.Length} bones and 3 tracked targets.");
            return true;
        }

        /// <summary>
        /// Switches off the head the template came with. Ch36 has its own, in the right place and
        /// under the same film.
        ///
        /// Switched off rather than deleted, and the objects left where they are: XRAvatarVisuals
        /// drives a mouth blendshape and the player's shirt colour through these renderers, and
        /// the local-player material swap reassigns their materials on spawn. All of that is
        /// harmless on a disabled renderer and a null reference without one. The film already
        /// over the template head follows it down on its own -- that is what MembraneFilmLink is
        /// for -- so it does not have to be found and removed here.
        /// </summary>
        static void HideTemplateHead(GameObject root)
        {
            string[] names = { "Head", "HMD" };
            int hidden = 0;

            Transform visuals = root.transform.Find("Avatar_Head");
            if (visuals == null)
            {
                Debug.LogWarning("[MembraneBody] No 'Avatar_Head' on the avatar, so there is no template head to hide.");
                return;
            }

            foreach (var renderer in visuals.GetComponentsInChildren<Renderer>(true))
            {
                foreach (string name in names)
                {
                    if (renderer.name != name) continue;
                    renderer.enabled = false;
                    hidden++;
                    Debug.Log($"[MembraneBody] Switched off the template's '{renderer.name}'.");
                }
            }

            if (hidden == 0)
                Debug.LogWarning("[MembraneBody] Found no template head renderers to switch off.");
        }

        /// <summary>
        /// Takes the crown and the name tag off the top of the player's head.
        ///
        /// Both are social-hub furniture the template ships with: a gold crown marking the session
        /// host, and a floating card with a name, initials, voice particles and a mute button.
        /// Neither belongs on a figure that is meant to read as a body in a dark room, and a
        /// label hovering over a membrane avatar is the one thing in the frame that announces it
        /// is a multiplayer template.
        ///
        /// Switched off rather than deleted, like the head. XRINetworkPlayer dereferences the name
        /// tag with no null check when a player spawns -- it reparents it to a world canvas if the
        /// scene has one, and calls SetupNameTag on it otherwise -- so a deleted tag is a null
        /// reference exception on every spawn. The crown is held by XRAvatarVisuals in three
        /// places, including the local player's material swap.
        /// </summary>
        static void HideFloatingUi(GameObject root)
        {
            // The template's own switch for the crown, which also deactivates it on spawn, plus
            // the renderer, so it cannot show in the frames before that runs.
            var visuals = root.GetComponent<XRAvatarVisuals>();
            if (visuals != null)
            {
                var so = new SerializedObject(visuals);
                so.FindProperty("m_ShowHostVisuals").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[MembraneBody] Turned off the host crown in XRAvatarVisuals.");
            }
            else
            {
                Debug.LogWarning("[MembraneBody] No XRAvatarVisuals on the avatar; the crown may come back on spawn.");
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name != k_CrownName) continue;
                renderer.enabled = false;
                Debug.Log($"[MembraneBody] Switched off '{renderer.name}'.");
            }

            // The canvas component rather than the object it is on. Deactivating the object would
            // hide the tag just as well and then hide it from GetComponentInParent too, which is
            // how the player script finds the canvas to reparent -- and that call is not guarded.
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                canvas.enabled = false;

                // The voice particles hanging off the name tag are particle systems, not UI, so a
                // switched-off canvas does not stop them: they would still puff over the player's
                // head whenever somebody spoke. Everything under the canvas that draws goes off,
                // whatever kind of renderer it turns out to be.
                int renderers = 0;
                foreach (var renderer in canvas.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled) continue;
                    renderer.enabled = false;
                    renderers++;
                }

                Debug.Log($"[MembraneBody] Switched off the '{canvas.name}' canvas, the name tag on it, " +
                          $"and {renderers} renderer(s) under it.");
            }
        }

        /// <summary>
        /// Puts the body's material on the tracked hands, which are staying. They are the one
        /// part of the avatar that is not Ch36, and under a translucent film a skin-coloured hand
        /// on a near-black body reads as two creatures rather than one.
        /// </summary>
        static void MatchHandsToBody(GameObject root, Material bodyMat)
        {
            int matched = 0;

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.name != "LeftHand" && smr.name != "RightHand") continue;
                if (smr.sharedMesh == null) continue;

                var mats = new Material[Mathf.Max(1, smr.sharedMesh.subMeshCount)];
                for (int i = 0; i < mats.Length; i++) mats[i] = bodyMat;
                smr.sharedMaterials = mats;
                matched++;
            }

            Debug.Log($"[MembraneBody] Put the body material on {matched} hand renderer(s).");
        }

        static bool Verify(Mesh film)
        {
            // Re-loaded from disk, so this checks what was saved rather than what the in-memory
            // copy happened to hold.
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath);
            if (saved == null)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - could not reload the saved prefab.");
                return false;
            }

            Transform body = saved.transform.Find(BodyRootName);
            if (body == null)
            {
                Debug.LogError($"[MembraneBody] RESULT: FAIL - no '{BodyRootName}' on the saved avatar.");
                return false;
            }

            var rig = body.GetComponent<MembraneBodyRig>();
            if (rig == null)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - the body has no MembraneBodyRig, so nothing would pose it.");
                return false;
            }
            if (!rig.bonesResolved)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - the rig did not resolve its bones.");
                return false;
            }
            if (rig.leftHandTarget == null || rig.rightHandTarget == null)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - the rig has no hand targets, so the arms would not move.");
                return false;
            }

            SkinnedMeshRenderer bodySmr = null, filmSmr = null;
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.name == FilmName) filmSmr = smr;
                else if (bodySmr == null) bodySmr = smr;
            }

            if (bodySmr == null)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - no body renderer under the body root.");
                return false;
            }
            if (filmSmr == null)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - no film renderer under the body root.");
                return false;
            }

            if (filmSmr.sharedMesh == null || filmSmr.sharedMesh.vertexCount != film.vertexCount)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - the film renderer is not using the film mesh.");
                return false;
            }
            if (filmSmr.bones == null || filmSmr.bones.Length != bodySmr.bones.Length || filmSmr.rootBone == null)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - the film is not skinned to the body's bones, " +
                               "so it would hang still while the body moved.");
                return false;
            }
            if (!filmSmr.updateWhenOffscreen)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - the film would cull on its bind-pose bounds.");
                return false;
            }
            if (filmSmr.sharedMaterial == null || filmSmr.sharedMaterial.shader == null ||
                filmSmr.sharedMaterial.shader.name != "CompositeBody/VacuumMembrane")
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - the film is not using the membrane shader.");
                return false;
            }

            var link = filmSmr.GetComponent<MembraneFilmLink>();
            if (link == null || link.source == null)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - the film has no MembraneFilmLink back to the body.");
                return false;
            }

            // The point of the whole edit: the template head must be off, or the player wears two
            // heads in the same place.
            Transform visuals = saved.transform.Find("Avatar_Head");
            if (visuals != null)
            {
                foreach (var renderer in visuals.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name != "Head" && renderer.name != "HMD") continue;
                    if (!renderer.enabled) continue;

                    Debug.LogError($"[MembraneBody] RESULT: FAIL - the template's '{renderer.name}' is still on.");
                    return false;
                }
            }

            // Nothing floating over the head: no crown, no name card.
            foreach (var renderer in saved.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name != k_CrownName || !renderer.enabled) continue;

                Debug.LogError("[MembraneBody] RESULT: FAIL - the host crown is still on.");
                return false;
            }

            var savedVisuals = saved.GetComponent<XRAvatarVisuals>();
            if (savedVisuals != null &&
                new SerializedObject(savedVisuals).FindProperty("m_ShowHostVisuals").boolValue)
            {
                Debug.LogError("[MembraneBody] RESULT: FAIL - XRAvatarVisuals would switch the crown back on.");
                return false;
            }

            foreach (var canvas in saved.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.enabled)
                {
                    Debug.LogError($"[MembraneBody] RESULT: FAIL - the '{canvas.name}' canvas is still drawing.");
                    return false;
                }

                foreach (var renderer in canvas.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled) continue;

                    Debug.LogError($"[MembraneBody] RESULT: FAIL - '{renderer.name}' under the " +
                                   $"'{canvas.name}' canvas is still drawing.");
                    return false;
                }
            }

            Debug.Log($"[MembraneBody] OK   body {bodySmr.sharedMesh.vertexCount} verts, " +
                      $"film {filmSmr.sharedMesh.vertexCount} verts, {filmSmr.bones.Length} bones, " +
                      "crown and name tag off.");
            return true;
        }
    }
}
