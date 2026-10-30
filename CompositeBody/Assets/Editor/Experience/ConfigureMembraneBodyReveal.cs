using System.IO;
using UnityEditor;
using UnityEngine;
using CompositeBody.Avatar.Skin;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Gives the player's membrane the same formation the room has: fading in and out, or growing
    /// outward so the film creeps over the body.
    ///
    /// Two things have to change on the avatar, and nothing else does.
    ///
    /// The film moves onto CompositeBody/VacuumMembraneAnimated, which is the only shader with a
    /// reveal in it. Its motion settings are all written to zero here, and with them at zero that
    /// shader's output is identical to the still one -- same vertex positions, same normals, same
    /// gap, same shading -- so a fully revealed avatar looks exactly as it does today. The
    /// breathing is available on the same material if it is ever wanted, but turning it on is a
    /// separate decision from being able to fade.
    ///
    /// Then a <see cref="MembraneReveal"/> drives it, growing from the feet upward. The origin is
    /// a point in mesh space rather than a Transform, which matters on a skinned film: the rest
    /// positions the growth is measured against are the bind pose and do not move, so an origin
    /// tracked from a scene Transform would wander through the body every time the body walked.
    ///
    /// This edits the shared player prefab, so it changes the avatar in every scene. Idempotent,
    /// and <see cref="Revert"/> puts the still material back and removes the component.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.ConfigureMembraneBodyReveal.Run
    /// </summary>
    public static class ConfigureMembraneBodyReveal
    {
        const string k_PrefabPath = "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRI_Network_Player_Avatar.prefab";
        const string k_StillMaterialPath = "Assets/Materials/MembraneAvatar.mat";
        const string k_RevealMaterialPath = "Assets/Materials/MembraneAvatarReveal.mat";

        /// <summary>
        /// How long the body takes to form, and to go. Shorter than the room's: a body arriving
        /// is a moment, where a room filling is a passage.
        /// </summary>
        const float k_GrowSeconds = 3.5f;
        const float k_FadeSeconds = 2.0f;

        public static void Run()
        {
            Debug.Log("[BodyReveal] Starting...");

            var animShader = Shader.Find("CompositeBody/VacuumMembraneAnimated");
            if (animShader == null)
            {
                Debug.LogError("[BodyReveal] RESULT: FAIL - shader 'CompositeBody/VacuumMembraneAnimated' not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(animShader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(animShader))
                    Debug.LogError($"[BodyReveal] Shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[BodyReveal] RESULT: FAIL - the animated membrane shader has compile errors.");
                return;
            }

            var still = AssetDatabase.LoadAssetAtPath<Material>(k_StillMaterialPath);
            if (still == null)
            {
                Debug.LogError($"[BodyReveal] RESULT: FAIL - the avatar's film material is not at {k_StillMaterialPath}. " +
                               "Run ConfigureMembraneBody first.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(k_PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[BodyReveal] RESULT: FAIL - prefab not found at {k_PrefabPath}");
                return;
            }

            try
            {
                SkinnedMeshRenderer film = FindFilm(root);
                if (film == null)
                {
                    Debug.LogError($"[BodyReveal] RESULT: FAIL - no film named '{ConfigureMembraneBody.FilmName}' " +
                                   "on the avatar. Run ConfigureMembraneBody first.");
                    return;
                }

                Material reveal = LoadOrCreateRevealMaterial(animShader, still);
                film.sharedMaterial = reveal;

                if (!Attach(film)) return;

                PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[BodyReveal] RESULT: PASS");
        }

        /// <summary>
        /// Puts the avatar back exactly as it was: the still material on the film and no reveal
        /// component. Worth having, because this edits the one prefab every scene's player comes
        /// from, and "try it and see" needs a way back that is not a git revert of a binary asset.
        /// </summary>
        public static void Revert()
        {
            Debug.Log("[BodyReveal] Reverting...");

            var still = AssetDatabase.LoadAssetAtPath<Material>(k_StillMaterialPath);
            if (still == null)
            {
                Debug.LogError($"[BodyReveal] RESULT: FAIL - the still material is gone from {k_StillMaterialPath}.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(k_PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[BodyReveal] RESULT: FAIL - prefab not found at {k_PrefabPath}");
                return;
            }

            try
            {
                SkinnedMeshRenderer film = FindFilm(root);
                if (film == null)
                {
                    Debug.LogError("[BodyReveal] RESULT: FAIL - no film on the avatar to revert.");
                    return;
                }

                film.sharedMaterial = still;

                // Whatever the reveal last wrote is still on the renderer; clearing the block is
                // what actually restores it, since the property it overrode lives on the material.
                film.SetPropertyBlock(null);
                film.forceRenderingOff = false;

                var existing = film.GetComponent<MembraneReveal>();
                if (existing != null) Object.DestroyImmediate(existing, true);

                PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[BodyReveal] RESULT: PASS - the avatar is back on the still material.");
        }

        static SkinnedMeshRenderer FindFilm(GameObject root)
        {
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name == ConfigureMembraneBody.FilmName) return smr;
            return null;
        }

        static bool Attach(SkinnedMeshRenderer film)
        {
            if (film.sharedMesh == null)
            {
                Debug.LogError("[BodyReveal] RESULT: FAIL - the film renderer has no mesh, so the growth " +
                               "radius cannot be measured.");
                return false;
            }

            var reveal = film.GetComponent<MembraneReveal>();
            if (reveal == null) reveal = film.gameObject.AddComponent<MembraneReveal>();

            // Up from the soles. The film's bind pose is the T-pose, so the bottom of its bounds
            // is the feet, and growing from there puts the front travelling up the body the way
            // it would if the sheet were being drawn on.
            Bounds b = film.sharedMesh.bounds;
            var origin = new Vector3(b.center.x, b.min.y, b.center.z);

            var so = new SerializedObject(reveal);
            so.FindProperty("m_Film").objectReferenceValue = film;
            so.FindProperty("m_GrowFrom").objectReferenceValue = null;
            so.FindProperty("m_GrowFromLocal").vector3Value = origin;
            so.FindProperty("m_AutoRadius").boolValue = true;
            so.FindProperty("m_GrowOnEnable").boolValue = false;
            so.FindProperty("m_FadeInSeconds").floatValue = k_GrowSeconds;
            so.FindProperty("m_FadeOutSeconds").floatValue = k_FadeSeconds;

            // Fully formed unless something asks otherwise. A player who spawns invisible because
            // nothing has called GrowIn yet is a worse default than one who simply cannot fade.
            so.FindProperty("m_Reveal").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[BodyReveal] Film '{film.name}': {film.sharedMesh.vertexCount:N0} verts, " +
                      $"bind-pose bounds {b.size}, growing from {origin} upward.");
            return true;
        }

        /// <summary>
        /// The avatar's film material with a reveal in it. Copied property by property off the
        /// still one so the look cannot drift, then every motion amplitude written to zero: this
        /// change is about being able to fade, not about making the player breathe. The shader
        /// scales its normal response by the breath amplitude, so zero here really is zero.
        /// </summary>
        public static Material LoadOrCreateRevealMaterial(Shader animShader, Material still)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_RevealMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(animShader) { name = "MembraneAvatarReveal" };
                AssetDatabase.CreateAsset(mat, k_RevealMaterialPath);
            }
            else if (mat.shader != animShader)
            {
                mat.shader = animShader;
            }

            int count = ShaderUtil.GetPropertyCount(animShader);
            for (int i = 0; i < count; i++)
            {
                string name = ShaderUtil.GetPropertyName(animShader, i);
                if (!still.HasProperty(name)) continue;

                switch (ShaderUtil.GetPropertyType(animShader, i))
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        mat.SetColor(name, still.GetColor(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        mat.SetFloat(name, still.GetFloat(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        mat.SetVector(name, still.GetVector(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        mat.SetTexture(name, still.GetTexture(name));
                        break;
                }
            }

            mat.SetFloat("_BreathAmount", 0f);
            mat.SetFloat("_RippleAmount", 0f);
            mat.SetFloat("_CreaseDrift", 0f);
            mat.SetFloat("_ManualTime", -1f);

            mat.SetFloat("_Reveal", 1f);
            mat.SetFloat("_GrowSoftness", 0.3f);
            mat.SetFloat("_GrowInflate", 1f);
            mat.SetFloat("_GrowEdgeFrost", 1f);

            // Origin and radius are MembraneReveal's to write; these are only what the material
            // shows when nothing is driving it.
            mat.SetFloat("_GrowRadius", 0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
