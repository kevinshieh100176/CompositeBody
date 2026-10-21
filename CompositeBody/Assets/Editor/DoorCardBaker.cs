using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Bakes each AtmosphericHouse door prefab into a 12-triangle "card" box whose front face
// carries the door's relief (normals), mask (metallic/AO/smoothness) and glass, all in one atlas.
// Cards are for far-away decoration (e.g. doors on the walls), not doors the player walks up to.
public static class DoorCardBaker
{
    const string DoorsFolder = "Assets/AtmosphericHouse/Prefabs/Doors";
    const string ColorMaterialsFolder = "Assets/Materials/IX/DoorColors";
    const string OutFolder = "Assets/Materials/IX/DoorCards";
    const string PrefabOutFolder = "Assets/Prefabs/DoorCards";
    const int Tile = 512;
    const int Columns = 4;
    static readonly Vector3 BakeOrigin = new Vector3(0, -5000, 0);

    class Source
    {
        public bool IsGlass;
        public Material Material;
        public Vector3[] Vertices; // root space
        public Vector3[] Normals;  // root space
        public Vector2[] Uvs;
        public int[] Triangles;
    }

    [MenuItem("Tools/Bake Door Cards")]
    public static void Bake()
    {
        EnsureFolder(OutFolder);
        EnsureFolder(PrefabOutFolder);

        var prefabs = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { DoorsFolder }))
            prefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)));
        prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        int rows = Mathf.CeilToInt(prefabs.Count / (float)Columns);
        int atlasW = Columns * Tile, atlasH = rows * Tile;
        var albedo = new Color[atlasW * atlasH];
        var normal = new Color[atlasW * atlasH];
        var mask = new Color[atlasW * atlasH];
        var readable = new Dictionary<Texture, Texture2D>();
        var meshes = new Dictionary<string, Mesh>();

        try
        {
            for (int p = 0; p < prefabs.Count; p++)
            {
                EditorUtility.DisplayProgressBar("Bake Door Cards", prefabs[p].name, p / (float)prefabs.Count);
                var tileRect = new RectInt((p % Columns) * Tile, (p / Columns) * Tile, Tile, Tile);
                var bounds = BakeTile(prefabs[p], tileRect, atlasW, albedo, normal, mask, readable);
                meshes[prefabs[p].name] = BuildCardMesh(prefabs[p].name, bounds, tileRect, atlasW, atlasH);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            foreach (var t in readable.Values) Object.DestroyImmediate(t);
        }

        var normalTex = WriteAtlas("DoorCards_Normal", normal, atlasW, atlasH, false, true);
        var maskTex = WriteAtlas("DoorCards_Mask", mask, atlasW, atlasH, false, false);
        AssetDatabase.DeleteAsset($"{OutFolder}/DoorCards_Albedo.png"); // superseded by the per-color atlases

        // One material per door color, mirroring the original Doors_white_clean setup. The paint color is
        // baked into each color's albedo atlas (not _BaseColor) so the frosted glass stays untinted.
        var template = FindMaterial("Doors_white_clean");
        int prefabCount = 0;
        var colored = new Color[albedo.Length];
        foreach (var guid in AssetDatabase.FindAssets("Doors_white_clean_ t:Material", new[] { ColorMaterialsFolder }))
        {
            var colorMat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (!colorMat.name.StartsWith("Doors_white_clean_")) continue;
            string colorName = colorMat.name.Substring("Doors_white_clean_".Length);

            var paint = colorMat.GetColor("_BaseColor");
            for (int i = 0; i < albedo.Length; i++)
            {
                var c = albedo[i].a > 0.5f ? albedo[i] * paint : albedo[i]; // alpha 0 = glass, left untinted
                c.a = 1;
                colored[i] = c;
            }
            var albedoTex = WriteAtlas("DoorCards_Albedo_" + colorName, colored, atlasW, atlasH, true, false);

            var mat = new Material(template) { name = "DoorCards_" + colorName, enableInstancing = true };
            mat.SetTexture("_BaseMap", albedoTex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", normalTex);
            mat.SetTexture("_MetallicGlossMap", maskTex);
            mat.SetTexture("_OcclusionMap", maskTex);
            mat = SaveOrUpdate(mat, $"{OutFolder}/{mat.name}.mat");

            foreach (var kv in meshes)
            {
                var go = new GameObject(kv.Key + "_" + colorName);
                go.AddComponent<MeshFilter>().sharedMesh = kv.Value;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
                PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabOutFolder}/{go.name}.prefab");
                Object.DestroyImmediate(go);
                prefabCount++;
            }
        }

        AssetDatabase.SaveAssets();
        // Re-saved prefabs can keep stale in-memory references until reimported
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabOutFolder }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        Debug.Log($"Baked {meshes.Count} door cards into a {atlasW}x{atlasH} atlas and {prefabCount} prefabs.");
    }

    // Rasterizes the door as seen from the front (+Z of the prefab root), keeping the frontmost
    // surface per texel, and writes one atlas tile. Returns the door's bounds in root space.
    // (Physics raycasts against these non-readable meshes report phantom hits, so this is done by hand.)
    static Bounds BakeTile(GameObject prefab, RectInt tile, int atlasW,
        Color[] albedo, Color[] normal, Color[] mask, Dictionary<Texture, Texture2D> readable)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.transform.SetPositionAndRotation(BakeOrigin, Quaternion.identity);
        inst.transform.localScale = Vector3.one;

        var sources = new List<Source>();
        bool hasBounds = false;
        var bounds = new Bounds();
        try
        {
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(false))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (!r.enabled || mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                var toRoot = inst.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var src = new Source
                {
                    Material = r.sharedMaterial,
                    IsGlass = r.sharedMaterial != null && r.sharedMaterial.name.ToLower().Contains("glass"),
                    Triangles = mesh.triangles,
                    Uvs = mesh.uv,
                    Vertices = mesh.vertices,
                    Normals = mesh.normals,
                };
                for (int v = 0; v < src.Vertices.Length; v++)
                {
                    src.Vertices[v] = toRoot.MultiplyPoint3x4(src.Vertices[v]);
                    if (src.Normals.Length > 0) src.Normals[v] = toRoot.MultiplyVector(src.Normals[v]).normalized;
                }
                sources.Add(src);

                var rb = new Bounds(inst.transform.InverseTransformPoint(r.bounds.center), inst.transform.InverseTransformVector(r.bounds.size));
                if (!hasBounds) { bounds = rb; hasBounds = true; } else bounds.Encapsulate(rb);
            }
        }
        finally
        {
            Object.DestroyImmediate(inst);
        }

        int w = tile.width, h = tile.height;
        var depth = new float[w * h];
        var hitSrc = new Source[w * h];
        var hitTri = new int[w * h];
        var hitBary = new Vector3[w * h];
        for (int k = 0; k < depth.Length; k++) depth[k] = float.NegativeInfinity;

        float sx = w / bounds.size.x, sy = h / bounds.size.y;
        foreach (var src in sources)
        {
            var t = src.Triangles;
            for (int k = 0; k < t.Length; k += 3)
            {
                Vector3 a = src.Vertices[t[k]], b = src.Vertices[t[k + 1]], c = src.Vertices[t[k + 2]];
                // Texel space, sampled at texel centers
                Vector2 pa = new Vector2((a.x - bounds.min.x) * sx - 0.5f, (a.y - bounds.min.y) * sy - 0.5f);
                Vector2 pb = new Vector2((b.x - bounds.min.x) * sx - 0.5f, (b.y - bounds.min.y) * sy - 0.5f);
                Vector2 pc = new Vector2((c.x - bounds.min.x) * sx - 0.5f, (c.y - bounds.min.y) * sy - 0.5f);
                float area = (pb.x - pa.x) * (pc.y - pa.y) - (pc.x - pa.x) * (pb.y - pa.y);
                if (Mathf.Abs(area) < 1e-8f) continue; // edge-on to the camera

                int x0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(pa.x, pb.x, pc.x)));
                int x1 = Mathf.Min(w - 1, Mathf.FloorToInt(Mathf.Max(pa.x, pb.x, pc.x)));
                int y0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(pa.y, pb.y, pc.y)));
                int y1 = Mathf.Min(h - 1, Mathf.FloorToInt(Mathf.Max(pa.y, pb.y, pc.y)));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float wa = ((pb.x - x) * (pc.y - y) - (pc.x - x) * (pb.y - y)) / area;
                    float wb = ((pc.x - x) * (pa.y - y) - (pa.x - x) * (pc.y - y)) / area;
                    float wc = 1 - wa - wb;
                    if (wa < 0 || wb < 0 || wc < 0) continue;
                    float z = wa * a.z + wb * b.z + wc * c.z;
                    int p = y * w + x;
                    if (z <= depth[p]) continue;
                    depth[p] = z;
                    hitSrc[p] = src;
                    hitTri[p] = k;
                    hitBary[p] = new Vector3(wa, wb, wc);
                }
            }
        }

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int p = y * w + x;
            int i = (tile.y + y) * atlasW + tile.x + x;
            var src = hitSrc[p];
            if (src == null)
            {
                albedo[i] = Color.white;
                normal[i] = new Color(0.5f, 0.5f, 1, 1);
                mask[i] = new Color(0, 1, 0, 0.3f);
                continue;
            }

            int k = hitTri[p];
            var bc = hitBary[p];
            int ia = src.Triangles[k], ib = src.Triangles[k + 1], ic = src.Triangles[k + 2];

            if (src.IsGlass)
            {
                // Frosted glass keeps its own texture; alpha 0 marks it as "don't tint" for the per-color atlases
                var guv = src.Uvs.Length > 0 ? src.Uvs[ia] * bc.x + src.Uvs[ib] * bc.y + src.Uvs[ic] * bc.z : Vector2.zero;
                var baseTex = src.Material.GetTexture("_BaseMap");
                var c = (baseTex ? Sample(baseTex, guv, readable, false) : Color.white) * src.Material.GetColor("_BaseColor");
                c.a = 0;
                albedo[i] = c;
                var glassMask = src.Material.GetTexture("_MetallicGlossMap");
                mask[i] = glassMask ? Sample(glassMask, guv, readable, true) : new Color(0, 1, 0, 0.8f);
                normal[i] = new Color(0.5f, 0.5f, 1, 1);
                continue;
            }

            var n = src.Normals.Length > 0
                ? (src.Normals[ia] * bc.x + src.Normals[ib] * bc.y + src.Normals[ic] * bc.z).normalized
                : Vector3.forward;
            if (n.z < 0) n = -n; // back-facing triangles still describe the visible surface
            normal[i] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);

            albedo[i] = Color.white;
            var maskMap = src.Material != null ? src.Material.GetTexture("_MetallicGlossMap") : null;
            if (maskMap != null && src.Uvs.Length > 0)
            {
                var uv = src.Uvs[ia] * bc.x + src.Uvs[ib] * bc.y + src.Uvs[ic] * bc.z;
                mask[i] = Sample(maskMap, uv, readable, true);
            }
            else mask[i] = new Color(0, 1, 0, 0.3f);
        }
        return bounds;
    }

    static Color Sample(Texture tex, Vector2 uv, Dictionary<Texture, Texture2D> cache, bool linear)
    {
        if (!cache.TryGetValue(tex, out var copy))
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, linear);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            cache[tex] = copy;
        }
        return copy.GetPixelBilinear(uv.x, uv.y);
    }

    // Box matching the door's bounds; the front face maps to the door's atlas tile,
    // the back face mirrors it, and the thin sides reuse the tile's edge texels
    static Mesh BuildCardMesh(string name, Bounds b, RectInt tile, int atlasW, int atlasH)
    {
        float u0 = (tile.x + 0.5f) / atlasW, u1 = (tile.xMax - 0.5f) / atlasW;
        float v0 = (tile.y + 0.5f) / atlasH, v1 = (tile.yMax - 0.5f) / atlasH;
        Vector3 mn = b.min, mx = b.max;

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var norms = new List<Vector3>();
        var tans = new List<Vector4>();
        var tris = new List<int>();

        void Quad(Vector3 a, Vector3 bb, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 n, Vector4 t)
        {
            int s = verts.Count;
            verts.AddRange(new[] { a, bb, c, d });
            uvs.AddRange(new[] { ua, ub, uc, ud });
            for (int k = 0; k < 4; k++) { norms.Add(n); tans.Add(t); }
            // Unity treats clockwise (as seen from outside) as front-facing
            tris.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
        }

        // Front (+Z), U along +X, V along +Y
        Quad(new Vector3(mn.x, mn.y, mx.z), new Vector3(mn.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mx.x, mn.y, mx.z),
            new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0), Vector3.forward, new Vector4(1, 0, 0, 1));
        // Back (-Z), mirrored
        Quad(new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mn.x, mx.y, mn.z), new Vector3(mn.x, mn.y, mn.z),
            new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), new Vector2(u0, v0), Vector3.back, new Vector4(-1, 0, 0, 1));
        // Left (-X): left edge column of the tile
        Quad(new Vector3(mn.x, mn.y, mn.z), new Vector3(mn.x, mx.y, mn.z), new Vector3(mn.x, mx.y, mx.z), new Vector3(mn.x, mn.y, mx.z),
            new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u0, v1), new Vector2(u0, v0), Vector3.left, new Vector4(0, 0, 1, 1));
        // Right (+X): right edge column
        Quad(new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mx.x, mn.y, mn.z),
            new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u1, v1), new Vector2(u1, v0), Vector3.right, new Vector4(0, 0, -1, 1));
        // Top (+Y): top edge row
        Quad(new Vector3(mn.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mx.z),
            new Vector2(u0, v1), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v1), Vector3.up, new Vector4(1, 0, 0, 1));
        // Bottom (-Y): bottom edge row
        Quad(new Vector3(mn.x, mn.y, mn.z), new Vector3(mn.x, mn.y, mx.z), new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mn.y, mn.z),
            new Vector2(u0, v0), new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v0), Vector3.down, new Vector4(1, 0, 0, 1));

        var mesh = new Mesh { name = name + "_card" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(norms);
        mesh.SetTangents(tans);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        Unwrapping.GenerateSecondaryUVSet(mesh); // lightmap UVs

        return SaveOrUpdate(mesh, $"{OutFolder}/{mesh.name}.asset");
    }

    // Overwrites an existing asset in place so prefabs and scenes referencing it stay linked on re-bake
    static T SaveOrUpdate<T>(T fresh, string path) where T : Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(fresh, path);
            return fresh;
        }
        string name = existing.name;
        EditorUtility.CopySerialized(fresh, existing);
        existing.name = name;
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(fresh);
        return existing;
    }

    static Texture2D WriteAtlas(string name, Color[] pixels, int w, int h, bool sRGB, bool isNormal)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, !sRGB);
        tex.SetPixels(pixels);
        tex.Apply();
        var path = $"{OutFolder}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = sRGB;
        importer.maxTextureSize = Mathf.Max(w, h);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material FindMaterial(string name)
    {
        foreach (var guid in AssetDatabase.FindAssets(name + " t:Material"))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m.name == name) return m;
        }
        return null;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
