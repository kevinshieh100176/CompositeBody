using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// The real room's floor, drawn as an outline. DEBUG ONLY -- nothing in the show reads this,
    /// and deleting the object it sits on breaks nothing.
    ///
    /// The piece is co-located: two people walk around a 4 x 3 m space wearing headsets, and
    /// everything they can see is authored in metres against a floor that does not exist in the
    /// scene. Without something to check against, a 光圈 that looks correctly placed on a monitor
    /// turns out to be half a metre into a wall.
    ///
    /// An outline rather than a filled plane. A filled quad on the floor of a black void is a
    /// large, flat, bright surface in exactly the place the beat wants empty, and it changes how
    /// everything above it reads -- which would make the debug aid alter the thing it is there to
    /// let you judge. A 4 cm border is legible from standing height and nearly invisible from
    /// anywhere else.
    ///
    /// Generated rather than modelled so the numbers stay editable. When the real venue turns out
    /// to be 4.2 x 2.8, that is two fields, not a new mesh.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class PlaySpaceBounds : MonoBehaviour
    {
        [Header("The room, in metres")]
        [SerializeField, Min(0.5f)] float m_Width = 4f;
        [SerializeField, Min(0.5f)] float m_Depth = 5f;

        [Header("Drawing")]
        [SerializeField, Range(0.005f, 0.2f), Tooltip("Thickness of the outline.")]
        float m_LineWidth = 0.04f;

        [SerializeField, Range(0.001f, 0.05f), Tooltip("Height above the floor. Enough to beat " +
                                                       "z-fighting with a floor at y = 0, little " +
                                                       "enough not to read as a kerb.")]
        float m_Lift = 0.004f;

        [SerializeField, Tooltip("Draw a cross at the centre, which is where the piece assumes " +
                                 "the origin is.")]
        bool m_CentreMark = true;

        Mesh m_Mesh;
        float m_LastWidth = -1f, m_LastDepth = -1f, m_LastLine = -1f;
        bool m_LastCentre;

        /// <summary>The authored room size in metres, for anything that wants to check against it.</summary>
        public Vector2 size => new(m_Width, m_Depth);

        void OnEnable() => Rebuild();

        void OnValidate() => Rebuild();

        void Update()
        {
            // Cheap guard rather than rebuilding every frame: this is [ExecuteAlways], so it ticks
            // in the editor while someone drags the width field.
            if (!Mathf.Approximately(m_Width, m_LastWidth) ||
                !Mathf.Approximately(m_Depth, m_LastDepth) ||
                !Mathf.Approximately(m_LineWidth, m_LastLine) ||
                m_CentreMark != m_LastCentre)
                Rebuild();
        }

        void Rebuild()
        {
            m_LastWidth = m_Width;
            m_LastDepth = m_Depth;
            m_LastLine = m_LineWidth;
            m_LastCentre = m_CentreMark;

            var filter = GetComponent<MeshFilter>();
            if (filter == null) return;

            if (m_Mesh == null)
            {
                m_Mesh = new Mesh { name = "PlaySpaceBounds" };
                // Not saved with the scene: it is regenerated from the fields on load, and a
                // serialised copy would be one more thing that can disagree with them.
                m_Mesh.hideFlags = HideFlags.HideAndDontSave;
            }

            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();

            float hw = m_Width * 0.5f, hd = m_Depth * 0.5f, w = m_LineWidth;
            // Four bars rather than a mitred ring: the corners overlap, which costs eight
            // triangles and removes every chance of getting a mitre wrong at an odd aspect.
            AddBar(verts, tris, -hw, hd - w, hw, hd);            // far
            AddBar(verts, tris, -hw, -hd, hw, -hd + w);          // near
            AddBar(verts, tris, -hw, -hd, -hw + w, hd);          // left
            AddBar(verts, tris, hw - w, -hd, hw, hd);            // right

            if (m_CentreMark)
            {
                const float k_Arm = 0.15f;
                AddBar(verts, tris, -k_Arm, -w * 0.5f, k_Arm, w * 0.5f);
                AddBar(verts, tris, -w * 0.5f, -k_Arm, w * 0.5f, k_Arm);
            }

            m_Mesh.Clear();
            m_Mesh.SetVertices(verts);
            m_Mesh.SetTriangles(tris, 0);
            m_Mesh.RecalculateBounds();
            filter.sharedMesh = m_Mesh;
        }

        /// <summary>One flat quad on the XZ plane, lifted clear of the floor.</summary>
        void AddBar(System.Collections.Generic.List<Vector3> verts,
                    System.Collections.Generic.List<int> tris,
                    float x0, float z0, float x1, float z1)
        {
            int b = verts.Count;
            verts.Add(new Vector3(x0, m_Lift, z0));
            verts.Add(new Vector3(x1, m_Lift, z0));
            verts.Add(new Vector3(x1, m_Lift, z1));
            verts.Add(new Vector3(x0, m_Lift, z1));
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
        }

        void OnDrawGizmos()
        {
            // Also as a gizmo, so the room is visible in the scene view even with the renderer
            // switched off -- which is how it should sit whenever someone is judging the look.
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, m_Lift, 0f), new Vector3(m_Width, 0f, m_Depth));
        }
    }
}
