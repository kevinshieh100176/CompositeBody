using UnityEngine;

namespace CompositeBody.Avatar.Cloth
{
    /// <summary>
    /// Settings describing a cloth-shrouded standing figure: a body of revolution whose radius
    /// profile reads as head / neck / shoulders / draping body / pooled hem, with vertical folds
    /// running down it and an optional wind-swept train trailing off to one side.
    /// </summary>
    [System.Serializable]
    public class DrapedFigureSettings
    {
        [Header("Scale")]
        public float height = 2.0f;

        [Header("Tessellation")]
        [Range(16, 256)] public int radialSegments = 128;
        [Range(16, 256)] public int heightSegments = 160;

        [Header("Folds")]
        [Tooltip("Number of vertical folds running around the drape.")]
        public int foldCount = 13;
        [Tooltip("Fold depth as a fraction of total height.")]
        public float foldAmplitude = 0.022f;
        [Tooltip("How far the folds rotate as they descend, so they don't read as a rigid extrusion.")]
        public float foldTwist = 1.4f;
        [Tooltip("Secondary finer folds layered over the primary ones.")]
        public int detailFoldCount = 31;
        public float detailFoldAmplitude = 0.006f;

        [Header("Wind Sweep")]
        [Tooltip("How far the lower cloth is dragged sideways into a trailing train.")]
        public float sweepStrength = 0.22f;
        [Tooltip("Direction the train trails, in degrees around Y.")]
        public float sweepDirection = 195f;
        [Tooltip("Extra flare added to the hem on the trailing side.")]
        public float trainFlare = 0.55f;

        [Header("Billowing Train")]
        [Tooltip("How far the train streams out sideways, as a multiple of height.")]
        public float trainLength = 1.25f;
        [Tooltip("How strongly the train is flattened toward the ground, so it streams horizontally instead of hanging.")]
        [Range(0f, 1f)] public float trainFlatten = 0.78f;
        [Tooltip("Where down the figure the train begins peeling away.")]
        [Range(0.3f, 0.95f)] public float trainStart = 0.72f;
        [Tooltip("Vertical lift given to the streaming train so it lofts on the wind.")]
        public float trainLift = 0.05f;
        [Tooltip("How much the train fans out sideways as it streams, so it reads as a billowing sheet rather than a narrow trailing hem.")]
        public float trainSpread = 0.5f;
    }

    /// <summary>
    /// Builds the <see cref="DrapedFigureSettings"/> silhouette into a mesh. Kept static and
    /// engine-only so both the runtime component and editor preview tooling can share it.
    /// </summary>
    public static class DrapedFigureMesh
    {
        // Radius profile keyed by t (0 = crown of head, 1 = ground), as a fraction of height.
        // Traced off a shrouded-figure silhouette: head dome, neck pinch, shoulder break, then
        // a continuous widening drape down to a pooled hem.
        // The crown follows a true spherical arc (dense keys) so it domes over smoothly instead
        // of pinching to a point, then holds a narrow hood before breaking at the shoulders.
        static readonly Vector2[] k_ProfileKeys =
        {
            new Vector2(0.000f, 0.000f),
            new Vector2(0.010f, 0.026f),
            new Vector2(0.025f, 0.040f),
            new Vector2(0.050f, 0.052f),
            new Vector2(0.075f, 0.058f),
            new Vector2(0.105f, 0.061f),
            new Vector2(0.150f, 0.062f), // hood, held nearly straight
            new Vector2(0.190f, 0.060f),
            new Vector2(0.225f, 0.098f), // shoulder break
            new Vector2(0.270f, 0.124f),
            new Vector2(0.340f, 0.140f),
            new Vector2(0.460f, 0.156f),
            new Vector2(0.600f, 0.174f),
            new Vector2(0.740f, 0.198f),
            new Vector2(0.860f, 0.228f),
            new Vector2(0.940f, 0.258f),
            new Vector2(1.000f, 0.290f),
        };

        static AnimationCurve BuildProfileCurve()
        {
            var keys = new Keyframe[k_ProfileKeys.Length];
            for (int i = 0; i < k_ProfileKeys.Length; i++)
                keys[i] = new Keyframe(k_ProfileKeys[i].x, k_ProfileKeys[i].y);

            var curve = new AnimationCurve(keys);
            for (int i = 0; i < curve.length; i++)
                curve.SmoothTangents(i, 0f);
            return curve;
        }

        public static Mesh Build(DrapedFigureSettings s)
        {
            var profile = BuildProfileCurve();

            int radial = Mathf.Max(8, s.radialSegments);
            int rows = Mathf.Max(8, s.heightSegments);
            int cols = radial + 1; // duplicate seam column so UVs and tangents stay continuous

            int vertCount = cols * (rows + 1);
            var vertices = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];

            float sweepRad = s.sweepDirection * Mathf.Deg2Rad;
            var sweepDir = new Vector3(Mathf.Cos(sweepRad), 0f, Mathf.Sin(sweepRad));
            var sweepPerp = Vector3.Cross(Vector3.up, sweepDir).normalized;

            for (int r = 0; r <= rows; r++)
            {
                float t = (float)r / rows;                 // 0 at crown, 1 at ground
                float y = (1f - t) * s.height;             // world Y, figure stands on y = 0
                float baseRadius = profile.Evaluate(t) * s.height;

                // Folds fade in below the head so the hood stays smooth, matching how a shroud
                // pulls taut over the skull but breaks into folds once it hangs free.
                float foldMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.16f, 0.42f, t));

                // Two separate wind effects: a gentle lean of the whole lower drape, and a
                // train that streams out near-horizontally. The train is applied per-vertex
                // below rather than per-row, because only the cloth on the trailing side should
                // peel away -- flattening the whole ring lifts the figure off the ground.
                float sweepMask = Mathf.Pow(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, t)), 1.4f);
                float trainMask = Mathf.Pow(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.trainStart, 1f, t)), 1.15f);

                Vector3 sweepOffset = sweepDir * (s.sweepStrength * sweepMask * s.height);

                for (int c = 0; c < cols; c++)
                {
                    float u = (float)c / radial;
                    float theta = u * Mathf.PI * 2f;

                    float fold = Mathf.Sin(theta * s.foldCount + t * s.foldTwist * Mathf.PI * 2f);

                    // Layer in harmonics that don't divide evenly into foldCount, so fold widths
                    // vary the way real gathered cloth does instead of reading as even ribbing.
                    fold += 0.55f * Mathf.Sin(theta * (s.foldCount * 0.61f) + 1.7f + t * 0.9f);
                    fold += 0.30f * Mathf.Sin(theta * (s.foldCount * 1.43f) + 4.1f - t * 1.3f);
                    fold *= 0.62f;

                    float detail = Mathf.Sin(theta * s.detailFoldCount - t * s.foldTwist * Mathf.PI);

                    float radius = baseRadius
                                   + fold * s.foldAmplitude * s.height * foldMask
                                   + detail * s.detailFoldAmplitude * s.height * foldMask;

                    var dir = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));

                    // Hem flares extra where it faces the trailing direction.
                    float facingTrain = Mathf.Max(0f, Vector3.Dot(dir, sweepDir));
                    radius += facingTrain * s.trainFlare * sweepMask * baseRadius;

                    // Only cloth facing downwind streams into the train; everything else keeps
                    // its height, so the hem stays planted on the ground all the way around.
                    float trainWeight = trainMask * Mathf.Pow(facingTrain, 1.3f);
                    float vy = y * (1f - s.trainFlatten * trainWeight)
                               + s.trainLift * s.height * trainWeight * Mathf.Sin(trainWeight * Mathf.PI);
                    Vector3 trainOffset = sweepDir * (s.trainLength * trainWeight * s.height);

                    // Fan the streaming cloth out perpendicular to the wind so the train reads
                    // as a broad billowing sheet instead of a narrow tail.
                    float sideways = Vector3.Dot(dir, sweepPerp);
                    trainOffset += sweepPerp * (sideways * s.trainSpread * trainWeight * s.height);

                    Vector3 pos = dir * radius + new Vector3(0f, vy, 0f) + sweepOffset + trainOffset;

                    int idx = r * cols + c;
                    vertices[idx] = pos;
                    uvs[idx] = new Vector2(u, 1f - t);
                }
            }

            var triangles = new int[rows * radial * 6];
            int ti = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < radial; c++)
                {
                    int a = r * cols + c;
                    int b = a + 1;
                    int cIdx = (r + 1) * cols + c;
                    int d = cIdx + 1;

                    // Wound so normals face outward, not into the body of revolution.
                    triangles[ti++] = a; triangles[ti++] = b; triangles[ti++] = cIdx;
                    triangles[ti++] = b; triangles[ti++] = d; triangles[ti++] = cIdx;
                }
            }

            var mesh = new Mesh { name = "DrapedFigure" };
            mesh.indexFormat = vertCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents(); // U runs around the body, so tangents follow the folds
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Drop-in component that builds the draped figure at runtime and renders it with whatever
    /// cloth material is assigned.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class DrapedFigure : MonoBehaviour
    {
        [SerializeField] DrapedFigureSettings m_Settings = new();
        [SerializeField] Material m_ClothMaterial;

        public DrapedFigureSettings settings => m_Settings;

        void Awake() => Rebuild();

        public void Rebuild()
        {
            GetComponent<MeshFilter>().sharedMesh = DrapedFigureMesh.Build(m_Settings);
            if (m_ClothMaterial != null)
                GetComponent<MeshRenderer>().sharedMaterial = m_ClothMaterial;
        }
    }
}
