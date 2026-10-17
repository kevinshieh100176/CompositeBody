using UnityEngine;
using UnityEngine.Splines;

namespace CompositeBody.Experience
{
    /// <summary>
    /// The authored half of a river: a spline, and curves describing what the water does along it.
    /// <c>BuildRiver</c> turns this into chunked meshes; nothing here generates geometry.
    ///
    /// WHY CURVES RATHER THAN PER-KNOT DATA. Unity's spline knots carry a position and tangents
    /// and nothing else, so width and flow have to live somewhere. AnimationCurves over the
    /// river's normalised length keep the whole profile visible and draggable in one inspector
    /// field, and they are the idiom AvatarFormation and ScanHandover already use for shaping.
    ///
    /// WHY THE WATER RUNS ON THE SHARED CLOCK. Two players stand in the same physical room. On
    /// <c>_Time</c> each headset would animate its own water from its own start, and two people
    /// looking at the same stretch of river would see different waves -- the exact failure
    /// ExperienceClock exists to prevent. The global is set here rather than per material so one
    /// river time covers every river and every pass of the shader.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SplineContainer))]
    public class RiverSpline : MonoBehaviour
    {
        [Header("Shape")]
        [SerializeField, Tooltip("Width in metres across the river's normalised length. The " +
                                 "horizontal key is 0 at the source and 1 at the mouth.")]
        AnimationCurve m_Width = AnimationCurve.Constant(0f, 1f, 3f);

        [SerializeField, Min(0.01f), Tooltip("Scales the whole width curve, for trying a river " +
                                             "wider or narrower without redrawing it.")]
        float m_WidthScale = 1f;

        [Header("Depth")]
        [SerializeField, Tooltip("Depth in metres along the river's length. There is no riverbed " +
                                 "in the scene, so this is what gives the water a body at all -- " +
                                 "the depth texture has nothing behind the surface to measure.")]
        AnimationCurve m_Depth = AnimationCurve.Constant(0f, 1f, 1.2f);

        [SerializeField, Tooltip("Cross-section, from one bank (0) through the channel (0.5) to " +
                                 "the other (1). Multiplies the depth above. A real river is " +
                                 "deepest along one line, not uniformly, and flattening this " +
                                 "curve is what makes a stretch read as a ford rather than a " +
                                 "channel.")]
        AnimationCurve m_DepthProfile = new(
            new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));

        [SerializeField, Min(0f), Tooltip("How far the bed extends beyond the water's edge, in " +
                                          "metres. Without a margin the bed and the waterline " +
                                          "end on the same vertex and the shore is a knife edge " +
                                          "with nothing behind it to see.")]
        float m_BankWidth = 0.9f;

        [SerializeField, Tooltip("How high the bed has risen by the outer edge of that margin, " +
                                 "relative to the water surface. This is the dry shore.")]
        float m_BankRise = 0.35f;

        [Header("Flow")]
        [SerializeField, Tooltip("Flow speed multiplier along the length. Baked into the mesh, " +
                                 "so a narrows can run faster than a pool.")]
        AnimationCurve m_Flow = AnimationCurve.Constant(0f, 1f, 1f);

        [SerializeField, Tooltip("How much surface foam, 0-1, along the length. Edge foam at the " +
                                 "banks is added by the shader on top of this.")]
        AnimationCurve m_Foam = AnimationCurve.Constant(0f, 1f, 0.15f);

        [Header("Build")]
        [SerializeField, Min(1f), Tooltip("Length of each chunk in metres. Chunks are what make " +
                                          "near and far work: each gets its own LODGroup, and a " +
                                          "chunk can be frustum-culled where one long mesh never " +
                                          "would be.")]
        float m_ChunkLength = 8f;

        [SerializeField, Range(2, 32), Tooltip("Vertices across the river, minus one. Held " +
                                               "CONSTANT across LOD levels on purpose -- varying " +
                                               "it would leave T-junctions at chunk seams where " +
                                               "two neighbours picked different levels.")]
        int m_LateralSegments = 8;

        [SerializeField, Tooltip("Metres between stations along the river, one entry per LOD " +
                                 "level. Only the lengthwise density changes between levels, " +
                                 "which is where a river's cost is: it is long, not wide.")]
        float[] m_LodSpacing = { 0.35f, 1.2f, 4f };

        [SerializeField, Tooltip("LODGroup transition heights, one per level, largest first.")]
        float[] m_LodScreenHeights = { 0.35f, 0.12f, 0.03f };

        [SerializeField, Tooltip("The water material. Left empty the chunks render magenta, " +
                                 "which this project has seen enough of.")]
        Material m_Material;

        [SerializeField, Tooltip("The riverbed material. The bed is what the water's depth is " +
                                 "measured against, so without it the water has nothing to be " +
                                 "deep over and reads as a flat sheet again.")]
        Material m_BedMaterial;

        static readonly int k_RiverTime = Shader.PropertyToID("_RiverTime");

        public AnimationCurve width => m_Width;
        public float widthScale => m_WidthScale;
        public AnimationCurve flow => m_Flow;
        public AnimationCurve foam => m_Foam;
        public float chunkLength => Mathf.Max(m_ChunkLength, 1f);
        public int lateralSegments => Mathf.Clamp(m_LateralSegments, 2, 32);
        public float[] lodSpacing => m_LodSpacing;
        public float[] lodScreenHeights => m_LodScreenHeights;
        public Material material => m_Material;
        public Material bedMaterial => m_BedMaterial;
        public float bankWidth => Mathf.Max(m_BankWidth, 0f);
        public float bankRise => m_BankRise;

        /// <summary>
        /// Height of the bed relative to the water surface at a point across the FULL bed span,
        /// where <paramref name="uBed"/> is 0 at one outer margin and 1 at the other. Negative
        /// under the water, positive on the dry shore.
        /// </summary>
        public float BedHeightAt(float t01, float uBed, float width)
        {
            float span = width + 2f * bankWidth;
            float offset = Mathf.Abs((Mathf.Clamp01(uBed) - 0.5f) * span);
            float halfWater = width * 0.5f;

            if (offset <= halfWater)
            {
                // Under the water: convert back to the water's own 0-1 so the bed is carved by
                // the same cross-section curve that colours the water. They cannot disagree.
                float uWater = Mathf.Clamp01((offset + halfWater) / Mathf.Max(width, 1e-4f));
                return -DepthAt(t01, uWater);
            }

            if (bankWidth <= 1e-4f) return 0f;

            float outward = Mathf.Clamp01((offset - halfWater) / bankWidth);
            return bankRise * outward;
        }

        public AnimationCurve depth => m_Depth;
        public AnimationCurve depthProfile => m_DepthProfile;

        /// <summary>Width in metres at a normalised position along the river.</summary>
        public float WidthAt(float t01)
        {
            return Mathf.Max(m_Width.Evaluate(Mathf.Clamp01(t01)) * m_WidthScale, 0.01f);
        }

        /// <summary>
        /// Depth in metres at a point on the surface: <paramref name="t01"/> along the river,
        /// <paramref name="u"/> across it. Evaluated per vertex rather than per station, because
        /// the cross-section is the half of it that you actually see.
        /// </summary>
        public float DepthAt(float t01, float u)
        {
            float along = m_Depth.Evaluate(Mathf.Clamp01(t01));
            float across = m_DepthProfile.Evaluate(Mathf.Clamp01(u));
            return Mathf.Max(along * across, 0f);
        }

        void OnEnable() => PushTime();
        void LateUpdate() => PushTime();

        /// <summary>
        /// Set every frame rather than once: ExperienceClock falls back to local time before a
        /// session exists and switches to server time when one does, and the water should follow
        /// that handover rather than freeze on whichever it saw first.
        /// </summary>
        void PushTime()
        {
            Shader.SetGlobalFloat(k_RiverTime, ExperienceClock.now);
        }
    }
}
