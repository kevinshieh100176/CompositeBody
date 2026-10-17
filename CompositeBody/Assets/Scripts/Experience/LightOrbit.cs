using System.Collections.Generic;
using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Carries a light around a closed path through a handful of control points.
    ///
    /// THE CONTROL POINTS ARE CHILD TRANSFORMS, not a serialized array of vectors and not a
    /// Spline asset. Unity's Splines package is not in this project, and adding one to draw two
    /// loops would be a dependency bought for very little -- while child transforms are already
    /// draggable in the scene view, snap, can be parented and animated, and show their own
    /// handles without a custom editor. Shaping the path is the point; this is the shortest road
    /// to shaping it by hand.
    ///
    /// Catmull-Rom through the points, so the curve passes through each one rather than being
    /// pulled toward it -- when a point is moved to clear the 真人's head, the path goes where it
    /// was put.
    ///
    /// SPEED IS CONSTANT ALONG THE CURVE, which needs saying because it costs a lookup table.
    /// Walking the curve by its parameter instead would make the light rush through the tight
    /// corners and dawdle on the long outside arcs -- and since the whole intent here is a path
    /// that is NOT symmetrical, those segments differ in length a lot. Uneven speed would read as
    /// the light stuttering rather than as the path being irregular.
    /// </summary>
    [ExecuteAlways]
    public class LightOrbit : MonoBehaviour
    {
        [SerializeField, Tooltip("The thing carried around the path. A child, usually the light.")]
        Transform m_Mover;

        [SerializeField, Tooltip("Control points, in order. Found among the children if empty -- " +
                                 "any child that is not the mover.")]
        Transform[] m_Points = new Transform[0];

        [SerializeField, Min(0.5f), Tooltip("Seconds for one lap.")]
        float m_Seconds = 48f;

        [SerializeField, Tooltip("Where on the path it starts, 0 to 1. Two lights on two paths " +
                                 "want different values or they set off together.")]
        [Range(0f, 1f)] float m_Phase;

        [SerializeField, Tooltip("Go the other way round.")]
        bool m_Reverse;

        [SerializeField, Tooltip("Turn the mover to face the way it is going. Off leaves its " +
                                 "rotation alone, which is what a point light wants.")]
        bool m_FaceAlongPath;

        [SerializeField, Min(16), Tooltip("Samples in the arc-length table. More is smoother " +
                                          "speed and costs only memory, once.")]
        int m_Resolution = 256;

        [SerializeField, Tooltip("Draw the path in the scene view.")]
        bool m_ShowPath = true;

        readonly List<float> m_Arc = new();      // cumulative length at each sample
        Vector3[] m_Cache;                       // the sampled curve, in local space
        float m_Length;

        /// <summary>Total length of the loop, in metres. 0 until there are enough points.</summary>
        public float length => m_Length;

        /// <summary>
        /// Put the mover at a point on the loop, now, without the clock. For the still renderer
        /// and for scrubbing in the editor, where ExperienceClock does not advance.
        /// </summary>
        public void PreviewAt(float t01)
        {
            if (m_Cache == null) Rebuild();
            if (m_Mover == null || m_Length <= 0f) return;
            m_Mover.position = transform.TransformPoint(EvaluateLocal(Mathf.Repeat(t01, 1f)));
        }

        void OnEnable() => Rebuild();
        void OnValidate() => Rebuild();

        void LateUpdate()
        {
            if (m_Mover == null || m_Length <= 0f) return;

            // ExperienceClock rather than Time.time, so the two lights stay in step with each
            // other and with the rest of the show across a session.
            float t = ExperienceClock.now / Mathf.Max(m_Seconds, 0.01f) + m_Phase;
            if (m_Reverse) t = -t;

            Vector3 here = EvaluateLocal(Mathf.Repeat(t, 1f));
            m_Mover.position = transform.TransformPoint(here);

            if (m_FaceAlongPath)
            {
                Vector3 ahead = EvaluateLocal(Mathf.Repeat(t + 0.002f, 1f));
                Vector3 dir = transform.TransformPoint(ahead) - m_Mover.position;
                if (dir.sqrMagnitude > 1e-8f) m_Mover.rotation = Quaternion.LookRotation(dir);
            }
        }

        /// <summary>
        /// Resample the curve and measure it. Called whenever anything that defines the shape
        /// changes, which in the editor is every time a control point is dragged.
        /// </summary>
        public void Rebuild()
        {
            if (m_Points == null || m_Points.Length == 0) CollectPoints();

            m_Length = 0f;
            m_Arc.Clear();
            m_Cache = null;

            int count = CountValid();
            if (count < 3) return;      // a loop needs three points to be a loop

            int samples = Mathf.Max(m_Resolution, count * 8);
            m_Cache = new Vector3[samples + 1];

            for (int i = 0; i <= samples; i++)
                m_Cache[i] = CatmullRom((float)i / samples);

            m_Arc.Add(0f);
            for (int i = 1; i <= samples; i++)
            {
                m_Length += Vector3.Distance(m_Cache[i - 1], m_Cache[i]);
                m_Arc.Add(m_Length);
            }
        }

        void CollectPoints()
        {
            var found = new List<Transform>();
            foreach (Transform child in transform)
            {
                if (child == m_Mover) continue;
                found.Add(child);
            }
            m_Points = found.ToArray();
        }

        int CountValid()
        {
            int n = 0;
            foreach (Transform p in m_Points)
            {
                if (p != null) n++;
            }
            return n;
        }

        /// <summary>Position at normalised DISTANCE along the loop, not at curve parameter.</summary>
        Vector3 EvaluateLocal(float distance01)
        {
            if (m_Cache == null || m_Arc.Count < 2) return Vector3.zero;

            float target = Mathf.Clamp01(distance01) * m_Length;

            // Binary search the arc table: at 256 samples a linear scan would be fine, but this
            // runs every frame for every light and costs nothing to do properly.
            int lo = 0, hi = m_Arc.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (m_Arc[mid] <= target) lo = mid; else hi = mid;
            }

            float span = m_Arc[hi] - m_Arc[lo];
            float f = span > 1e-6f ? (target - m_Arc[lo]) / span : 0f;
            return Vector3.Lerp(m_Cache[lo], m_Cache[hi], f);
        }

        /// <summary>Closed Catmull-Rom through the control points, in this object's local space.</summary>
        Vector3 CatmullRom(float t01)
        {
            int n = m_Points.Length;
            float scaled = Mathf.Repeat(t01, 1f) * n;
            int i = Mathf.FloorToInt(scaled);
            float t = scaled - i;

            Vector3 p0 = Local(i - 1), p1 = Local(i), p2 = Local(i + 1), p3 = Local(i + 2);

            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) +
                           (-p0 + p2) * t +
                           (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                           (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        Vector3 Local(int index)
        {
            int n = m_Points.Length;
            if (n == 0) return Vector3.zero;
            Transform p = m_Points[((index % n) + n) % n];
            return p != null ? transform.InverseTransformPoint(p.position) : Vector3.zero;
        }

        void OnDrawGizmos()
        {
            if (!m_ShowPath) return;
            if (m_Cache == null) Rebuild();
            if (m_Cache == null) return;

            Gizmos.color = new Color(0.6f, 0.7f, 1f, 0.75f);
            for (int i = 1; i < m_Cache.Length; i++)
            {
                Gizmos.DrawLine(transform.TransformPoint(m_Cache[i - 1]),
                                transform.TransformPoint(m_Cache[i]));
            }

            Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
            foreach (Transform p in m_Points)
            {
                if (p != null) Gizmos.DrawWireSphere(p.position, 0.06f);
            }
        }
    }
}
