using UnityEditor;
using UnityEngine;

namespace CompositeBody.Avatar.Cloth.EditorTools
{
    /// <summary>
    /// Read-only diagnostic: dumps the character FBX's skinned meshes, bone names and bounds so
    /// the cloth setup can bind to real bones instead of guessed names.
    /// </summary>
    public static class ProbeAvatarRig
    {
        public const string AvatarFbxPath = "Assets/_models/Ch36_nonPBR (1).fbx";

        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarFbxPath);
            if (prefab == null)
            {
                Debug.LogError($"[ProbeRig] FAIL - could not load {AvatarFbxPath}");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var smrs = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Debug.Log($"[ProbeRig] SkinnedMeshRenderers: {smrs.Length}");

                foreach (var smr in smrs)
                {
                    Debug.Log($"[ProbeRig] SMR '{smr.name}' mesh='{(smr.sharedMesh ? smr.sharedMesh.name : "null")}' " +
                              $"verts={(smr.sharedMesh ? smr.sharedMesh.vertexCount : 0)} bones={smr.bones.Length} " +
                              $"root='{(smr.rootBone ? smr.rootBone.name : "null")}'");
                }

                var all = instance.GetComponentsInChildren<Transform>(true);
                Debug.Log($"[ProbeRig] Total transforms: {all.Length}");
                foreach (var t in all)
                    Debug.Log($"[ProbeRig] BONE {t.name}");

                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    foreach (var r in renderers) b.Encapsulate(r.bounds);
                    Debug.Log($"[ProbeRig] WorldBounds center={b.center} size={b.size}");
                }

                Debug.Log("[ProbeRig] RESULT: PASS");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
