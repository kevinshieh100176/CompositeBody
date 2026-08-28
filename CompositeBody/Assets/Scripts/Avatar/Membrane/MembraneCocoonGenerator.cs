using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace CompositeBody.Avatar.Membrane
{
    /// <summary>
    /// Procedurally builds a spider-web-style cocoon around this transform (e.g. the player
    /// avatar): small silk panels wrapped around a pod/cocoon silhouette, individually breakable
    /// off their central anchor, PLUS a proper strand network of breakable spring joints
    /// connecting every panel to its ring/spoke neighbors -- so pulling one spot visibly tugs
    /// and strains the panels around it, not just that one isolated piece. Every joint (anchor
    /// and neighbor) has a matching visible thread (<see cref="WebStrand"/>) that sags like silk
    /// and snaps only once it's actually torn and pulled apart.
    /// </summary>
    public class MembraneCocoonGenerator : MonoBehaviour
    {
        [Header("Shape")]
        [SerializeField, Tooltip("Height of the cocoon, from the base of this transform upward.")]
        float m_Height = 1.8f;
        [SerializeField, Tooltip("Radius at the cocoon's widest point (mid-height).")]
        float m_MaxRadius = 0.45f;
        [SerializeField, Range(3, 40), Tooltip("Number of horizontal panel rows.")]
        int m_RingCount = 6;
        [SerializeField, Range(3, 48), Tooltip("Number of panels around each ring.")]
        int m_SpokeCount = 10;
        [SerializeField, Range(0.01f, 0.3f), Tooltip("Fraction of height excluded at top/bottom to avoid degenerate near-zero-radius panels.")]
        float m_PoleTaper = 0.08f;
        [SerializeField, Range(0.3f, 1f), Tooltip("How much of each grid cell the silk panel fills. Lower values leave visible web gaps between panels.")]
        float m_PanelFill = 0.7f;

        [Header("Anchor Physics (panel to body)")]
        [SerializeField] float m_PanelMass = 0.05f;
        [SerializeField, Tooltip("Spring stiffness holding a panel near its rest position against the body.")]
        float m_AnchorSpring = 300f;
        [SerializeField] float m_AnchorDamper = 8f;
        [SerializeField, Tooltip("How far a panel can stretch away from the body before the anchor spring resists hard. Small values keep panels snug but still give a bit of silky stretch when tugged.")]
        float m_AnchorMaxStretch = 0.08f;
        [SerializeField, Tooltip("Linear force (N) needed to tear a panel completely free of the cocoon.")]
        float m_BreakForce = 15f;
        [SerializeField, Tooltip("Torque (N*m) needed to tear a panel completely free of the cocoon.")]
        float m_BreakTorque = 15f;
        [SerializeField, Tooltip("Physical thickness of each panel's collider.")]
        float m_PanelThickness = 0.02f;

        [Header("Strand Physics (panel to panel)")]
        [SerializeField, Tooltip("Spring stiffness pulling neighboring panels back toward their rest distance.")]
        float m_StrandSpring = 150f;
        [SerializeField] float m_StrandDamper = 4f;
        [SerializeField, Tooltip("Force needed to snap a single strand between two neighboring panels. Usually lower than the anchor break force so pulling cascades outward through the web before a panel comes fully loose.")]
        float m_StrandBreakForce = 6f;
        [SerializeField, Tooltip("How far past rest length a strand can stretch before it snaps visually, once its joint has broken.")]
        float m_StrandSnapStretch = 3f;

        [Header("Look")]
        [SerializeField, Tooltip("Leave empty to use a translucent silk runtime default.")]
        Material m_PanelMaterial;
        [SerializeField, Tooltip("Leave empty to use a pale silk-thread runtime default.")]
        Material m_StrandMaterial;
        [SerializeField, Range(0.001f, 0.02f)] float m_StrandWidth = 0.004f;

        Rigidbody m_Anchor;
        Material m_RuntimePanelMaterial;
        Material m_RuntimeStrandMaterial;
        Transform m_StrandContainer;

        MembranePanel[,] m_PanelGrid;
        readonly List<MembranePanel> m_Panels = new();
        int m_TornCount;
        bool m_FullyOpenedFired;

        /// <summary>0-1 fraction of panels torn free of the anchor so far.</summary>
        public float tornFraction => m_Panels.Count == 0 ? 0f : (float)m_TornCount / m_Panels.Count;

        public IReadOnlyList<MembranePanel> panels => m_Panels;

        /// <summary>Fired once, the first time every panel has torn free.</summary>
        public event Action onCocoonFullyOpened;

        void Awake()
        {
            BuildAnchor();
            BuildPanels();
            ConnectNeighborStrands();
        }

        void BuildAnchor()
        {
            var anchorGO = new GameObject("MembraneAnchor");
            anchorGO.transform.SetParent(transform, false);
            m_Anchor = anchorGO.AddComponent<Rigidbody>();
            m_Anchor.isKinematic = true;

            var strandContainerGO = new GameObject("Strands");
            strandContainerGO.transform.SetParent(transform, false);
            m_StrandContainer = strandContainerGO.transform;
        }

        void BuildPanels()
        {
            var ring = new Vector3[m_RingCount + 1, m_SpokeCount];

            for (int r = 0; r <= m_RingCount; r++)
            {
                float t = Mathf.Lerp(m_PoleTaper, 1f - m_PoleTaper, (float)r / m_RingCount);
                float y = t * m_Height;
                float radius = m_MaxRadius * Mathf.Sin(t * Mathf.PI);

                for (int s = 0; s < m_SpokeCount; s++)
                {
                    float angle = (float)s / m_SpokeCount * Mathf.PI * 2f;
                    ring[r, s] = new Vector3(radius * Mathf.Cos(angle), y, radius * Mathf.Sin(angle));
                }
            }

            var overallCenter = new Vector3(0f, m_Height * 0.5f, 0f);
            m_PanelGrid = new MembranePanel[m_RingCount, m_SpokeCount];

            for (int r = 0; r < m_RingCount; r++)
            {
                for (int s = 0; s < m_SpokeCount; s++)
                {
                    int sNext = (s + 1) % m_SpokeCount;
                    m_PanelGrid[r, s] = CreatePanel(ring[r, s], ring[r, sNext], ring[r + 1, s], ring[r + 1, sNext], overallCenter, r, s);
                }
            }
        }

        MembranePanel CreatePanel(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 overallCenter, int ringIndex, int spokeIndex)
        {
            Vector3 centroid = (a + b + c + d) * 0.25f;

            // Shrink each corner toward the centroid so neighboring panels don't touch --
            // leaves a visible web gap that the connecting strand crosses.
            a = Vector3.Lerp(centroid, a, m_PanelFill);
            b = Vector3.Lerp(centroid, b, m_PanelFill);
            c = Vector3.Lerp(centroid, c, m_PanelFill);
            d = Vector3.Lerp(centroid, d, m_PanelFill);

            var go = new GameObject($"Panel_{ringIndex}_{spokeIndex}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = centroid;

            Vector3 outward = centroid - overallCenter;
            outward.y *= 0.35f; // bias outward direction toward horizontal so panels near the poles don't tilt oddly
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.forward;
            outward.Normalize();
            go.transform.localRotation = Quaternion.LookRotation(-outward, Vector3.up);

            Vector3 la = go.transform.InverseTransformPoint(transform.TransformPoint(a));
            Vector3 lb = go.transform.InverseTransformPoint(transform.TransformPoint(b));
            Vector3 lc = go.transform.InverseTransformPoint(transform.TransformPoint(c));
            Vector3 ld = go.transform.InverseTransformPoint(transform.TransformPoint(d));

            var mesh = new Mesh { name = "MembranePanelQuad" };
            mesh.vertices = new[] { la, lb, lc, ld };
            mesh.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(1, 0) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // The two possible quad diagonals can wind the normal either way depending on the
            // panel's local orientation; flip it if it's pointing into the cocoon instead of out.
            if (Vector3.Dot(mesh.normals[0], go.transform.InverseTransformDirection(outward)) < 0f)
            {
                mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
                mesh.RecalculateNormals();
            }

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = GetOrCreatePanelMaterial();

            var boxCollider = go.AddComponent<BoxCollider>();
            boxCollider.center = mesh.bounds.center;
            Vector3 size = mesh.bounds.size;
            boxCollider.size = new Vector3(Mathf.Max(size.x, 0.01f), Mathf.Max(size.y, 0.01f), m_PanelThickness);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = m_PanelMass;

            var joint = go.AddComponent<SpringJoint>();
            joint.connectedBody = m_Anchor;
            joint.autoConfigureConnectedAnchor = true;
            joint.spring = m_AnchorSpring;
            joint.damper = m_AnchorDamper;
            joint.minDistance = 0f;
            joint.maxDistance = m_AnchorMaxStretch;
            joint.breakForce = m_BreakForce;
            joint.breakTorque = m_BreakTorque;

            var grab = go.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.throwOnDetach = true;

            var panel = go.AddComponent<MembranePanel>();
            panel.SetAnchorJoint(joint);
            panel.onTorn += HandlePanelTorn;

            m_Panels.Add(panel);
            return panel;
        }

        /// <summary>
        /// Wires every panel to its "next spoke" and "next ring" neighbor with a breakable
        /// spring joint plus a matching visible strand, covering every grid edge exactly once.
        /// </summary>
        void ConnectNeighborStrands()
        {
            for (int r = 0; r < m_RingCount; r++)
            {
                for (int s = 0; s < m_SpokeCount; s++)
                {
                    MembranePanel current = m_PanelGrid[r, s];

                    int sNext = (s + 1) % m_SpokeCount;
                    CreateStrand(current, m_PanelGrid[r, sNext]);

                    if (r < m_RingCount - 1)
                        CreateStrand(current, m_PanelGrid[r + 1, s]);
                }
            }
        }

        void CreateStrand(MembranePanel from, MembranePanel to)
        {
            var joint = from.gameObject.AddComponent<SpringJoint>();
            joint.connectedBody = to.GetComponent<Rigidbody>();
            joint.autoConfigureConnectedAnchor = true;

            float restLength = Vector3.Distance(from.transform.position, to.transform.position);
            joint.spring = m_StrandSpring;
            joint.damper = m_StrandDamper;
            joint.minDistance = restLength * 0.9f;
            joint.maxDistance = restLength * 1.1f;
            joint.breakForce = m_StrandBreakForce;

            var strandGO = new GameObject($"Strand_{from.name}_to_{to.name}");
            strandGO.transform.SetParent(m_StrandContainer, false);

            var line = strandGO.AddComponent<LineRenderer>();
            line.sharedMaterial = GetOrCreateStrandMaterial();
            line.widthMultiplier = m_StrandWidth;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            strandGO.AddComponent<WebStrand>().Initialize(from.transform, to.transform, joint, m_StrandSnapStretch);
        }

        Material GetOrCreatePanelMaterial()
        {
            if (m_PanelMaterial != null) return m_PanelMaterial;
            if (m_RuntimePanelMaterial != null) return m_RuntimePanelMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            m_RuntimePanelMaterial = new Material(shader) { name = "MembraneSilk (Runtime Default)" };
            m_RuntimePanelMaterial.SetColor("_BaseColor", new Color(0.92f, 0.9f, 0.85f, 0.35f));

            // Standard URP Lit transparency setup (equivalent to switching Surface Type to
            // Transparent in the inspector), applied via script since this material is built at
            // runtime.
            m_RuntimePanelMaterial.SetFloat("_Surface", 1f);
            m_RuntimePanelMaterial.SetFloat("_Blend", 0f);
            m_RuntimePanelMaterial.SetOverrideTag("RenderType", "Transparent");
            m_RuntimePanelMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m_RuntimePanelMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m_RuntimePanelMaterial.SetInt("_ZWrite", 0);
            m_RuntimePanelMaterial.DisableKeyword("_ALPHATEST_ON");
            m_RuntimePanelMaterial.EnableKeyword("_ALPHABLEND_ON");
            m_RuntimePanelMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m_RuntimePanelMaterial.renderQueue = (int)RenderQueue.Transparent;

            return m_RuntimePanelMaterial;
        }

        Material GetOrCreateStrandMaterial()
        {
            if (m_StrandMaterial != null) return m_StrandMaterial;
            if (m_RuntimeStrandMaterial != null) return m_RuntimeStrandMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            m_RuntimeStrandMaterial = new Material(shader) { name = "SilkThread (Runtime Default)" };
            m_RuntimeStrandMaterial.SetColor("_BaseColor", new Color(0.95f, 0.95f, 0.92f, 0.8f));
            return m_RuntimeStrandMaterial;
        }

        void HandlePanelTorn(MembranePanel panel)
        {
            m_TornCount++;
            if (!m_FullyOpenedFired && m_TornCount >= m_Panels.Count)
            {
                m_FullyOpenedFired = true;
                onCocoonFullyOpened?.Invoke();
            }
        }
    }
}
