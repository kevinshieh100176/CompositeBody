using UnityEngine;

namespace CompositeBody.Interaction.Home
{
    /// <summary>
    /// An oriented box, nothing more: the transform is its centre and orientation, <see cref="size"/>
    /// its extent in metres (unscaled -- the transform's own scale is ignored so a zone reads the
    /// same whatever it is parented under).
    ///
    /// PURE MATHS, NO COLLIDER. A trigger collider here would sit in front of every fragment and
    /// eat the hand's ray and poke casts -- the assembly zone is exactly where the hand is when it
    /// matters most. Containment is asked for, not reported.
    ///
    /// Home uses two: A, where the fragments are scattered, and B, the assembly zone around the
    /// mini-house (A minus an inflated B is the scatter region).
    /// </summary>
    [DisallowMultipleComponent]
    public class HomeZone : MonoBehaviour
    {
        [SerializeField, Tooltip("Full extent in metres, along the zone's own axes.")]
        Vector3 m_Size = Vector3.one;

        [SerializeField] Color m_GizmoColor = new(0.3f, 0.8f, 1f, 0.8f);

        public Vector3 size { get => m_Size; set => m_Size = Vector3.Max(value, Vector3.zero); }

        public Vector3 center => transform.position;

        public Quaternion rotation => transform.rotation;

        /// <summary>Is <paramref name="world"/> inside the box grown by <paramref name="margin"/> metres on every side?</summary>
        public bool Contains(Vector3 world, float margin = 0f)
        {
            Vector3 local = Quaternion.Inverse(transform.rotation) * (world - transform.position);
            Vector3 half = m_Size * 0.5f + Vector3.one * margin;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>A uniformly random point inside the box (shrunk by <paramref name="inset"/> per side).</summary>
        public Vector3 RandomPoint(System.Random rng, float inset = 0f)
        {
            Vector3 half = Vector3.Max(m_Size * 0.5f - Vector3.one * inset, Vector3.zero);
            var local = new Vector3(
                ((float)rng.NextDouble() * 2f - 1f) * half.x,
                ((float)rng.NextDouble() * 2f - 1f) * half.y,
                ((float)rng.NextDouble() * 2f - 1f) * half.z);
            return transform.position + transform.rotation * local;
        }

        /// <summary>Does the whole of <paramref name="other"/> (grown by <paramref name="margin"/>) fit in this one?</summary>
        public bool Encloses(HomeZone other, float margin = 0f)
        {
            if (other == null) return false;
            Vector3 h = other.size * 0.5f + Vector3.one * margin;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                if (!Contains(other.center + other.rotation * corner)) return false;
            }
            return true;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = m_GizmoColor;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, m_Size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
