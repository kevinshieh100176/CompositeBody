using UnityEngine;

namespace CompositeBody.Avatar.Membrane
{
    /// <summary>
    /// The visible thread for one SpringJoint connection between two neighboring membrane
    /// panels. Follows both endpoints every frame with a slight gravity sag, so it reads as a
    /// silk strand rather than a rigid rod. Once its joint breaks, the strand keeps rendering
    /// (so you see it go slack) until the endpoints pull apart past a snap distance, at which
    /// point it destroys itself -- giving a genuine "thread tore free" moment instead of an
    /// instant disappearance.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class WebStrand : MonoBehaviour
    {
        Transform m_EndA;
        Transform m_EndB;
        Joint m_Joint;
        float m_RestLength;
        float m_SnapStretchMultiplier;
        LineRenderer m_Line;

        public void Initialize(Transform endA, Transform endB, Joint joint, float snapStretchMultiplier = 3f)
        {
            m_EndA = endA;
            m_EndB = endB;
            m_Joint = joint;
            m_SnapStretchMultiplier = snapStretchMultiplier;
            m_RestLength = Mathf.Max(Vector3.Distance(endA.position, endB.position), 0.001f);

            m_Line = GetComponent<LineRenderer>();
            m_Line.positionCount = 3;
            m_Line.useWorldSpace = true;
        }

        void LateUpdate()
        {
            if (m_EndA == null || m_EndB == null)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 a = m_EndA.position;
            Vector3 b = m_EndB.position;
            float currentLength = Vector3.Distance(a, b);

            bool jointBroken = m_Joint == null;
            if (jointBroken && currentLength > m_RestLength * m_SnapStretchMultiplier)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 mid = Vector3.Lerp(a, b, 0.5f);
            float sag = Mathf.Clamp(currentLength * 0.08f, 0f, 0.05f);
            mid -= Vector3.up * sag;

            m_Line.SetPosition(0, a);
            m_Line.SetPosition(1, mid);
            m_Line.SetPosition(2, b);
        }
    }
}
