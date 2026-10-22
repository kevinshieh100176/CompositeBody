using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ScatterFurniture
{
    const string FurnitureFolder = "Assets/AtmosphericHouse/Prefabs/Props/Furniture";
    const string DoorsFolder = "Assets/AtmosphericHouse/Prefabs/Doors";
    const string PlaneName = "Plane.008";
    const int Count = 75;
    const float EdgeMargin = 0.9f;
    const float GiantChance = 0f;
    static readonly Vector2 GiantScale = new Vector2(3f, 12f);
    const float BottomEdgeBand = 0.05f;
    const float BuryDepth = 0.02f;

    [MenuItem("Tools/Scatter Furniture on Plane.008")]
    static void ScatterFurnitureMenu() => Scatter(FurnitureFolder, "Scattered_Furniture");

    [MenuItem("Tools/Scatter Doors on Plane.008")]
    static void ScatterDoorsMenu() => Scatter(DoorsFolder, "Scattered_Doors");

    static void Scatter(string prefabFolder, string groupName)
    {
        var plane = GameObject.Find("Base_Model_V3/" + PlaneName);
        if (plane == null || plane.GetComponent<Renderer>() == null)
        {
            Debug.LogError("Plane.008 not found under Base_Model_V3.");
            return;
        }

        var b = plane.GetComponent<Renderer>().bounds;
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabFolder });
        if (guids.Length == 0) return;

        // Plane.008 is the hilly ground mesh with no collider; add a temporary one to raycast against
        var collider = plane.AddComponent<MeshCollider>();
        Physics.SyncTransforms();

        var group = new GameObject(groupName);
        Undo.RegisterCreatedObjectUndo(group, "Scatter " + groupName);

        for (int i = 0; i < Count; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[Random.Range(0, guids.Length)]);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, plane.scene);
            Undo.RegisterCreatedObjectUndo(inst, "Scatter " + groupName);

            float x = b.center.x + Random.Range(-b.extents.x, b.extents.x) * EdgeMargin;
            float z = b.center.z + Random.Range(-b.extents.z, b.extents.z) * EdgeMargin;
            inst.transform.SetPositionAndRotation(
                new Vector3(x, b.max.y, z),
                Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            inst.transform.localScale = Vector3.one * (Random.value < GiantChance
                ? Random.Range(GiantScale.x, GiantScale.y)
                : 1f);

            BuryBottomEdge(inst.transform, collider);
            inst.transform.SetParent(group.transform, true);
        }

        Object.DestroyImmediate(collider);

        EditorSceneManager.MarkSceneDirty(plane.scene);
        Debug.Log($"Scattered {Count} prefabs from {prefabFolder} on {PlaneName}.");
    }

    // Lowers the object until every vertex along its bottom edge is below the ground,
    // so nothing floats where the ground slopes away
    static void BuryBottomEdge(Transform target, MeshCollider ground)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        float minY = float.MaxValue;
        foreach (var mf in target.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var m = mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices)
            {
                var w = m.MultiplyPoint3x4(v);
                verts.Add(w);
                minY = Mathf.Min(minY, w.y);
            }
        }

        float drop = float.MaxValue;
        foreach (var w in verts)
        {
            if (w.y > minY + BottomEdgeBand) continue;
            if (ground.Raycast(new Ray(new Vector3(w.x, w.y + 100f, w.z), Vector3.down), out var hit, 300f))
                drop = Mathf.Min(drop, hit.point.y - w.y);
        }
        if (drop != float.MaxValue)
            target.position += Vector3.up * (drop - BuryDepth);
    }
}
