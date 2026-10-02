using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using CompositeBody.Avatar.Skin;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Wraps the networked player avatar in vacuum membrane.
    ///
    /// The template's avatar is a head and two hands and nothing else -- there is no torso, no
    /// rig, no body mesh. So the membrane goes onto what is actually there: a film is relaxed
    /// against each of the three meshes and added as a sibling skinned renderer sharing the same
    /// bones, which means it follows head turns and finger tracking for free.
    ///
    /// That is not a workaround. O-0 says the players have no definite body and can only faintly
    /// make out their own hands, so membrane over head and hands is the avatar the script asks
    /// for at this point. A full membrane-wrapped body is an S0-2 problem -- that is the beat
    /// where a body forms -- and it needs a torso and a three-point IK solution that this project
    /// does not have yet.
    ///
    /// Edits the shared player prefab, so it changes the avatar in every scene. Idempotent:
    /// re-running replaces the films rather than stacking more on.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.ConfigureMembraneAvatar.Probe
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.ConfigureMembraneAvatar.Run
    /// </summary>
    public static class ConfigureMembraneAvatar
    {
        const string k_PrefabPath = "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRI_Network_Player_Avatar.prefab";
        const string k_MeshDir = "Assets/_models/AvatarMembrane";
        const string k_MaterialPath = "Assets/Materials/MembraneAvatar.mat";
        const string k_FilmSuffix = "_MembraneFilm";

        /// <summary>
        /// Head and hands need different settings. A 20mm film is right on a torso and turns a
        /// hand into a mitten: the relaxation that usefully bridges an armpit also bridges the
        /// gaps between fingers, and the fingers are the only part of this avatar that moves
        /// expressively.
        /// </summary>
        struct FilmSettings
        {
            public float baseOffset;
            public float minOffset;
            public int smoothIterations;
            public float smoothLambda;
        }

        static readonly FilmSettings k_HeadSettings = new()
        {
            baseOffset = 0.016f, minOffset = 0.005f, smoothIterations = 420, smoothLambda = 0.55f
        };

        static readonly FilmSettings k_HandSettings = new()
        {
            baseOffset = 0.005f, minOffset = 0.0018f, smoothIterations = 90, smoothLambda = 0.35f
        };

        /// <summary>Read-only dump of what the avatar prefab actually contains.</summary>
        public static void Probe()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[MembraneAvatar] RESULT: FAIL - prefab not found at {k_PrefabPath}");
                return;
            }

            foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = smr.sharedMesh;
                Debug.Log($"[MembraneAvatar] SMR '{smr.name}' on '{smr.transform.parent?.name ?? "(root)"}' " +
                          $"mesh='{(mesh != null ? mesh.name : "null")}' " +
                          $"verts={(mesh != null ? mesh.vertexCount : 0)} " +
                          $"readable={(mesh != null && mesh.isReadable)} " +
                          $"bones={smr.bones.Length} root='{(smr.rootBone != null ? smr.rootBone.name : "null")}' " +
                          $"mats={smr.sharedMaterials.Length}");
            }

            foreach (var mr in prefab.GetComponentsInChildren<MeshRenderer>(true))
                Debug.Log($"[MembraneAvatar] MeshRenderer '{mr.name}' (not wrapped: no bones to skin a film to)");

            Debug.Log("[MembraneAvatar] RESULT: PASS");
        }

        public static void Run()
        {
            Debug.Log("[MembraneAvatar] Starting...");

            var shader = Shader.Find("CompositeBody/VacuumMembrane");
            if (shader == null)
            {
                Debug.LogError("[MembraneAvatar] RESULT: FAIL - shader 'CompositeBody/VacuumMembrane' not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    Debug.LogError($"[MembraneAvatar] Shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[MembraneAvatar] RESULT: FAIL - shader has compile errors.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[MembraneAvatar] RESULT: FAIL - prefab not found at {k_PrefabPath}");
                return;
            }

            Material filmMat = LoadOrCreateMaterial(shader);
            Directory.CreateDirectory(k_MeshDir);

            // Edited through a prefab contents scope rather than on the asset directly, so the
            // whole edit either lands and is saved or is thrown away intact.
            GameObject root = PrefabUtility.LoadPrefabContents(k_PrefabPath);
            int wrapped = 0;

            try
            {
                RemoveExistingFilms(root);

                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.name.EndsWith(k_FilmSuffix)) continue;

                    Mesh source = smr.sharedMesh;
                    if (source == null)
                    {
                        Debug.LogWarning($"[MembraneAvatar] '{smr.name}' has no mesh; skipped.");
                        continue;
                    }
                    // The prefab carries two hand rigs -- Quest and AndroidXR_Simplified -- and
                    // enables one at runtime by platform. This project ships PCVR against Quest
                    // over Link, so wrapping the AndroidXR set would be two more films that are
                    // never switched on.
                    string owner = smr.transform.parent != null ? smr.transform.parent.name : "(root)";
                    if (owner.Contains("AndroidXR"))
                    {
                        Debug.Log($"[MembraneAvatar] Skipping '{owner}/{smr.name}': AndroidXR rig, and the target is PCVR.");
                        continue;
                    }

                    bool isHand = smr.name.ToLowerInvariant().Contains("hand");
                    FilmSettings settings = isHand ? k_HandSettings : k_HeadSettings;

                    // Read/Write Enabled is off on these imported meshes, which blocks reading
                    // vertices at runtime but not here: the editor has the imported asset open.
                    Mesh film;
                    try
                    {
                        film = MembraneShellBuilder.Build(source, settings.baseOffset, settings.minOffset,
                                                          settings.smoothIterations, settings.smoothLambda);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[MembraneAvatar] RESULT: FAIL - could not relax a film against " +
                                       $"'{source.name}' ({smr.name}): {e.Message}");
                        return;
                    }

                    if (film == null || film.vertexCount == 0)
                    {
                        Debug.LogError($"[MembraneAvatar] RESULT: FAIL - the film built against '{source.name}' is empty.");
                        return;
                    }

                    // Named after the object that owns it: both hand rigs call their renderers
                    // 'LeftHand', so the mesh assets would overwrite each other.
                    film.name = $"{owner}_{smr.name}{k_FilmSuffix}";

                    string meshPath = $"{k_MeshDir}/{film.name}.asset";
                    if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
                    AssetDatabase.CreateAsset(film, meshPath);

                    var filmGO = new GameObject(film.name);

                    // A sibling, not a child: parented under the source renderer it would inherit
                    // that object's scale twice over, and the template enables and disables these
                    // objects per local/remote player, which a child would silently follow.
                    filmGO.transform.SetParent(smr.transform.parent, false);
                    filmGO.transform.SetLocalPositionAndRotation(smr.transform.localPosition, smr.transform.localRotation);
                    filmGO.transform.localScale = smr.transform.localScale;

                    var filmSmr = filmGO.AddComponent<SkinnedMeshRenderer>();
                    filmSmr.sharedMesh = film;
                    filmSmr.bones = smr.bones;
                    filmSmr.rootBone = smr.rootBone;
                    filmSmr.sharedMaterial = filmMat;

                    // Without this the film culls on its bind-pose bounds, so a head turn or a
                    // raised hand makes the membrane blink out while the mesh under it stays.
                    filmSmr.updateWhenOffscreen = true;

                    filmSmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    filmSmr.receiveShadows = false;

                    // Tied to the renderer it wraps, so it inherits the avatar's own local/remote
                    // hiding. Without this the head film renders for the local player, who is
                    // inside that head.
                    var link = filmGO.AddComponent<MembraneFilmLink>();
                    var linkSo = new SerializedObject(link);
                    linkSo.FindProperty("m_Source").objectReferenceValue = smr;
                    linkSo.FindProperty("m_Film").objectReferenceValue = filmSmr;
                    linkSo.ApplyModifiedPropertiesWithoutUndo();

                    wrapped++;
                    Debug.Log($"[MembraneAvatar] Wrapped '{owner}/{smr.name}' ({source.vertexCount} -> {film.vertexCount} verts, " +
                              $"{(isHand ? "hand" : "head")} settings, {smr.bones.Length} bones).");
                }

                if (wrapped == 0)
                {
                    Debug.LogError("[MembraneAvatar] RESULT: FAIL - nothing was wrapped.");
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[MembraneAvatar] Saved prefab with {wrapped} film(s).");

            if (!Verify(wrapped)) return;

            Debug.Log("[MembraneAvatar] RESULT: PASS");
        }

        static void RemoveExistingFilms(GameObject root)
        {
            var stale = new List<GameObject>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.EndsWith(k_FilmSuffix)) stale.Add(t.gameObject);
            }
            foreach (var go in stale)
            {
                Debug.Log($"[MembraneAvatar] Removing previous film '{go.name}'.");
                Object.DestroyImmediate(go);
            }
        }

        static Material LoadOrCreateMaterial(Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(shader) { name = "MembraneAvatar" };
                AssetDatabase.CreateAsset(mat, k_MaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            // Left neutral on purpose. The avatar is shared by both players, so a role colour
            // baked in here would put the same colour on both of them; tinting per role has to
            // happen at runtime from PlayerRoleColors, the way RoleBlobPresenter does it.
            mat.SetColor("_FilmColor", new Color(0.72f, 0.78f, 0.86f));
            mat.SetColor("_FrostColor", new Color(0.88f, 0.91f, 0.96f));
            mat.SetFloat("_ClearAlpha", 0.12f);
            mat.SetFloat("_FrostAlpha", 0.62f);
            mat.SetFloat("_ShowContact", 0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static bool Verify(int expected)
        {
            // Re-loaded from disk, so this checks what was actually saved rather than what the
            // in-memory copy happened to hold.
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath);
            if (saved == null)
            {
                Debug.LogError("[MembraneAvatar] RESULT: FAIL - could not reload the saved prefab.");
                return false;
            }

            int found = 0;
            foreach (var smr in saved.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr.name.EndsWith(k_FilmSuffix)) continue;
                found++;

                if (smr.sharedMesh == null || smr.sharedMesh.vertexCount == 0)
                {
                    Debug.LogError($"[MembraneAvatar] RESULT: FAIL - '{smr.name}' has no film mesh.");
                    return false;
                }
                if (smr.bones == null || smr.bones.Length == 0 || smr.rootBone == null)
                {
                    Debug.LogError($"[MembraneAvatar] RESULT: FAIL - '{smr.name}' is not bound to bones, so it " +
                                   "would not follow the head or the fingers.");
                    return false;
                }
                if (smr.sharedMaterial == null || smr.sharedMaterial.shader == null ||
                    smr.sharedMaterial.shader.name != "CompositeBody/VacuumMembrane")
                {
                    Debug.LogError($"[MembraneAvatar] RESULT: FAIL - '{smr.name}' is not using the membrane shader.");
                    return false;
                }
                if (!smr.updateWhenOffscreen)
                {
                    Debug.LogError($"[MembraneAvatar] RESULT: FAIL - '{smr.name}' would cull on bind-pose bounds.");
                    return false;
                }

                var link = smr.GetComponent<MembraneFilmLink>();
                if (link == null || new SerializedObject(link).FindProperty("m_Source").objectReferenceValue == null)
                {
                    Debug.LogError($"[MembraneAvatar] RESULT: FAIL - '{smr.name}' has no MembraneFilmLink back to the " +
                                   "renderer it wraps, so it would not follow the avatar's local/remote hiding.");
                    return false;
                }

                Debug.Log($"[MembraneAvatar] OK   {smr.name} ({smr.sharedMesh.vertexCount} verts, {smr.bones.Length} bones)");
            }

            if (found != expected)
            {
                Debug.LogError($"[MembraneAvatar] RESULT: FAIL - saved prefab has {found} film(s), expected {expected}.");
                return false;
            }

            return true;
        }
    }
}
