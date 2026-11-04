using System;
using System.Collections.Generic;
using UnityEngine;

namespace CompositeBody.Interaction.Home
{
    /// <summary>
    /// One wall of the big room and everything on it (Floor, Back, Front, Left, Right, Top), and how
    /// it comes back: each element -- the wall first, then its furniture one after another -- starts
    /// invisible, floated out from where it belongs along the group's outward axis, and moves home
    /// with an ease-in while it surfaces out of black, then wobbles once as it arrives.
    ///
    /// THE AUTHORED POSE IS THE ANSWER. Every element's world pose is read when the group wakes and
    /// is where it lands; nothing is serialized twice, so moving a chair in the scene needs no
    /// numbers changed. Poses are written in WORLD space, parent first, so furniture parented under
    /// its wall does not ride the wall's own flight.
    ///
    /// HOW IT SURFACES depends on the slot's shader:
    ///   CompositeBody/MeltingMemory (the walls)  -> _Holes driven 1 (all holes) -> authored, by MPB.
    ///   anything else (URP Lit furniture)       -> swapped to its builder-made reveal twin
    ///                                              (CompositeBody/InteractionReveal) and _Reveal
    ///                                              driven 0 -> 1, by MPB.
    /// Both clip in their shadow and depth passes, so shadows surface with the surface. At the end
    /// the renderer gets its ORIGINAL shared materials back and no property block at all -- the room
    /// after the reveal is the room as authored, batched and lit exactly as before.
    ///
    /// A PURE FUNCTION OF TIME since <see cref="Play"/>'s start stamp, on the shared clock: two
    /// headsets draw the same frame, and a late joiner fast-forwards.
    ///
    /// The target is a SCENE object while this component lives in a prefab, so the reference is a
    /// scene override written by the builder; <see cref="m_TargetPath"/> finds it if the override
    /// is lost.
    /// </summary>
    [DisallowMultipleComponent]
    public class HomeRevealGroup : MonoBehaviour
    {
        const string k_MeltingShader = "CompositeBody/MeltingMemory";
        static readonly int k_Reveal = Shader.PropertyToID("_Reveal");
        static readonly int k_Holes = Shader.PropertyToID("_Holes");

        [Header("What")]
        [SerializeField, Tooltip("ASCII name for logs (Floor, Back, Front, Left, Right, Top).")]
        string m_GroupName = "Group";

        [SerializeField, Tooltip("The wall (its children are its furniture). A scene override.")]
        Transform m_Target;

        [SerializeField, Tooltip("Hierarchy path used when the scene override is missing, e.g. " +
                                 "/Home/--- Environment ---/S3/Inner_Wall/Back_Wall.")]
        string m_TargetPath = "";

        [Header("Float in")]
        [SerializeField, Tooltip("World direction the group waits along, away from the room (unit). " +
                                 "Floor waits below, Top above. Set by the builder from the wall's " +
                                 "dominant axis.")]
        Vector3 m_FloatDirection = Vector3.up;

        [SerializeField, Min(0f), Tooltip("Metres out along the direction each element starts from.")]
        float m_FloatOffset = 1.2f;

        [SerializeField, Min(0.05f), Tooltip("Seconds each element takes to come home.")]
        float m_ElementTravel = 1.8f;

        [SerializeField, Min(0f), Tooltip("Seconds between one element starting and the next. Wall first, " +
                                          "then the furniture in hierarchy order.")]
        float m_Stagger = 0.3f;

        [SerializeField, Range(0.2f, 1f), Tooltip("Fraction of the travel by which the surface is " +
                                                  "fully there -- it is whole a little before it lands.")]
        float m_RevealPortion = 0.8f;

        [SerializeField, Tooltip("Optional travel curve (0..1 -> 0..1). Empty = cubic ease-in.")]
        AnimationCurve m_TravelCurve = new();

        [Header("Arrival wobble")]
        [SerializeField, Min(0f)] float m_WobbleDuration = 0.7f;
        [SerializeField, Min(0f), Tooltip("Metres of overshoot along the travel direction.")]
        float m_WobbleDistance = 0.03f;
        [SerializeField, Min(0f), Tooltip("Degrees of rock.")] float m_WobbleAngle = 1.2f;
        [SerializeField, Min(0.5f)] float m_WobbleFrequency = 3.5f;

        [Header("Reveal twins (builder)")]
        [SerializeField, Tooltip("Source materials that have a reveal twin.")]
        Material[] m_TwinSources = Array.Empty<Material>();

        [SerializeField, Tooltip("Their twins, same order.")]
        Material[] m_Twins = Array.Empty<Material>();

        enum Phase : byte { Hidden, Moving, Done }

        sealed class Element
        {
            public Transform transform;
            public Vector3 homePosition;
            public Quaternion homeRotation;
            public Renderer[] renderers;
            public Material[][] originals;
            public Material[][] reveals;
            public bool[] melting;   // has a MeltingMemory slot
            public float[] holes;    // authored _Holes of that slot (first one wins)
            public Phase phase;
        }

        readonly List<Element> m_Elements = new();
        MaterialPropertyBlock m_Block;
        bool m_Prepared;
        bool m_Playing;
        bool m_Done;
        double m_StartAt = InteractionPhaseClock.Unset;
        int m_MissingTwins;

        public string groupName => m_GroupName;
        public Transform target => m_Target;
        public string targetPath => m_TargetPath;
        public Vector3 floatDirection => m_FloatDirection;
        public float floatOffset { get => m_FloatOffset; set => m_FloatOffset = Mathf.Max(0f, value); }
        public bool prepared => m_Prepared;
        public bool playing => m_Playing && !m_Done;
        public bool done => m_Done;
        public bool started => m_Playing;
        public double startAt => m_StartAt;
        public int elementCount => m_Elements.Count;

        /// <summary>Seconds from start until the last element has settled.</summary>
        public float duration =>
            Mathf.Max(0, m_Elements.Count - 1) * m_Stagger + m_ElementTravel + m_WobbleDuration;

        /// <summary>Shared-clock time the group is finished, or +inf if not started.</summary>
        public double endAt => m_Playing ? m_StartAt + duration : double.PositiveInfinity;

        /// <summary>Renderers this group drives (for tests).</summary>
        public IEnumerable<Renderer> renderers
        {
            get
            {
                foreach (Element e in m_Elements)
                    foreach (Renderer r in e.renderers)
                        if (r != null) yield return r;
            }
        }

        /// <summary>Original shared materials of a renderer this group drives, or null.</summary>
        public Material[] OriginalMaterialsOf(Renderer renderer)
        {
            foreach (Element e in m_Elements)
                for (int i = 0; i < e.renderers.Length; i++)
                    if (e.renderers[i] == renderer) return e.originals[i];
            return null;
        }

        /// <summary>Largest distance/angle any element is from its authored pose (for tests).</summary>
        public void MaxPoseError(out float metres, out float degrees)
        {
            metres = 0f;
            degrees = 0f;
            foreach (Element e in m_Elements)
            {
                if (e.transform == null) continue;
                metres = Mathf.Max(metres, Vector3.Distance(e.transform.position, e.homePosition));
                degrees = Mathf.Max(degrees, Quaternion.Angle(e.transform.rotation, e.homeRotation));
            }
        }

        /// <summary>Configure from the builder.</summary>
        public void Configure(string groupName, Transform targetTransform, string path, Vector3 direction,
                              Material[] twinSources, Material[] twins)
        {
            m_GroupName = groupName;
            m_Target = targetTransform;
            m_TargetPath = path;
            m_FloatDirection = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.up;
            m_TwinSources = twinSources ?? Array.Empty<Material>();
            m_Twins = twins ?? Array.Empty<Material>();
        }

        /// <summary>Twin for <paramref name="source"/> from the builder's table, or null.</summary>
        public Material TwinOf(Material source)
        {
            if (source == null) return null;
            for (int i = 0; i < m_TwinSources.Length && i < m_Twins.Length; i++)
                if (m_TwinSources[i] == source) return m_Twins[i];
            return null;
        }

        public static bool IsMelting(Material m) =>
            m != null && m.shader != null && (m.shader.name == k_MeltingShader || m.HasProperty(k_Holes));

        /// <summary>The target, from the override or the path fallback.</summary>
        public Transform ResolveTarget()
        {
            if (m_Target != null) return m_Target;
            if (string.IsNullOrEmpty(m_TargetPath)) return null;
            GameObject go = GameObject.Find(m_TargetPath);
            if (go == null) return null;
            m_Target = go.transform;
            Debug.LogWarning($"[Home] group '{m_GroupName}': target override missing; found " +
                             $"'{m_TargetPath}' by path.", this);
            return m_Target;
        }

        /// <summary>
        /// Read the authored poses and materials. Call once, before anything has moved the room
        /// (the assembly does it in Awake).
        /// </summary>
        public bool Prepare()
        {
            if (m_Prepared) return true;
            Transform t = ResolveTarget();
            if (t == null)
            {
                Debug.LogError($"[Home] group '{m_GroupName}' has no target ('{m_TargetPath}'). Re-run " +
                               "Tools > Home > Build Home Interaction.", this);
                return false;
            }

            m_Block ??= new MaterialPropertyBlock();
            m_Elements.Clear();
            m_MissingTwins = 0;

            // The wall first (its own renderer only), then each direct child with its subtree.
            AddElement(t, t.GetComponents<Renderer>());
            foreach (Transform child in t)
                AddElement(child, child.GetComponentsInChildren<Renderer>(true));

            if (m_MissingTwins > 0)
                Debug.LogWarning($"[Home] group '{m_GroupName}': {m_MissingTwins} material slot(s) have " +
                                 "no reveal twin and will appear without the dissolve. Re-run the builder.", this);

            m_Prepared = true;
            return true;
        }

        void AddElement(Transform t, Renderer[] rs)
        {
            var e = new Element
            {
                transform = t,
                renderers = rs,
                originals = new Material[rs.Length][],
                reveals = new Material[rs.Length][],
                melting = new bool[rs.Length],
                holes = new float[rs.Length],
                phase = Phase.Hidden,
            };
            t.GetPositionAndRotation(out e.homePosition, out e.homeRotation);

            for (int i = 0; i < rs.Length; i++)
            {
                Material[] originals = rs[i].sharedMaterials;
                var reveal = new Material[originals.Length];
                e.holes[i] = 0f;
                for (int s = 0; s < originals.Length; s++)
                {
                    Material m = originals[s];
                    if (m == null) continue;
                    if (IsMelting(m))
                    {
                        reveal[s] = m;
                        if (!e.melting[i]) e.holes[i] = m.GetFloat(k_Holes);
                        e.melting[i] = true;
                        continue;
                    }
                    Material twin = TwinOf(m);
                    if (twin == null) m_MissingTwins++;
                    reveal[s] = twin != null ? twin : m;
                }
                e.originals[i] = originals;
                e.reveals[i] = reveal;
            }
            m_Elements.Add(e);
        }

        /// <summary>Everything invisible and floated out; materials and blocks back to authored.</summary>
        public void Hide()
        {
            if (!Prepare()) return;
            m_Playing = false;
            m_Done = false;
            m_StartAt = InteractionPhaseClock.Unset;
            foreach (Element e in m_Elements)
            {
                Restore(e);
                SetPose(e, Offset(1f), Quaternion.identity);
                SetVisible(e, false);
                e.phase = Phase.Hidden;
            }
        }

        /// <summary>Everything home, as authored (an end state, or a group put back).</summary>
        public void ShowImmediately()
        {
            if (!Prepare()) return;
            foreach (Element e in m_Elements) Finish(e);
            m_Done = true;
        }

        /// <summary>Start at <paramref name="sharedStart"/> on the shared clock (may be past or future).</summary>
        public void Play(double sharedStart)
        {
            if (!Prepare()) return;
            m_StartAt = sharedStart;
            m_Playing = true;
            m_Done = false;
        }

        /// <summary>Draw this frame. Cheap once done.</summary>
        public void Tick(double now)
        {
            if (!m_Playing || m_Done || !m_Prepared) return;

            bool allDone = true;
            for (int i = 0; i < m_Elements.Count; i++)
            {
                Element e = m_Elements[i];
                if (e.phase == Phase.Done) continue;
                allDone = false;

                float since = (float)(now - (m_StartAt + i * (double)m_Stagger));
                if (since < 0f)
                {
                    // Still waiting its turn -- but its parent may be moving, so hold the world pose.
                    SetPose(e, Offset(1f), Quaternion.identity);
                    continue;
                }

                if (since >= m_ElementTravel + m_WobbleDuration)
                {
                    Finish(e);
                    continue;
                }

                if (e.phase == Phase.Hidden)
                {
                    for (int r = 0; r < e.renderers.Length; r++)
                        if (e.renderers[r] != null) e.renderers[r].sharedMaterials = e.reveals[r];
                    SetVisible(e, true);
                    e.phase = Phase.Moving;
                }

                if (since < m_ElementTravel)
                {
                    float k = since / m_ElementTravel;
                    float eased = InteractionEasing.EaseIn(k, m_TravelCurve);
                    SetPose(e, Offset(1f - eased), Quaternion.identity);
                    SetReveal(e, Mathf.Clamp01(k / m_RevealPortion));
                }
                else
                {
                    float w = InteractionEasing.Wobble(since - m_ElementTravel, m_WobbleDuration, m_WobbleFrequency);
                    Vector3 axis = Vector3.Cross(m_FloatDirection, Vector3.up);
                    if (axis.sqrMagnitude < 1e-4f) axis = Vector3.right;
                    // Overshoot INTO the room (against the float direction), then settle.
                    SetPose(e, -m_FloatDirection * (w * m_WobbleDistance),
                            Quaternion.AngleAxis(w * m_WobbleAngle, axis.normalized));
                    SetReveal(e, 1f);
                }
            }

            if (allDone) m_Done = true;
        }

        Vector3 Offset(float amount01) => m_FloatDirection * (m_FloatOffset * amount01);

        static void SetPose(Element e, Vector3 offset, Quaternion turn)
        {
            if (e.transform == null) return;
            e.transform.SetPositionAndRotation(e.homePosition + offset, turn * e.homeRotation);
        }

        static void SetVisible(Element e, bool on)
        {
            foreach (Renderer r in e.renderers)
                if (r != null && r.forceRenderingOff == on) r.forceRenderingOff = !on;
        }

        void SetReveal(Element e, float reveal01)
        {
            for (int i = 0; i < e.renderers.Length; i++)
            {
                Renderer r = e.renderers[i];
                if (r == null) continue;
                m_Block.Clear();
                m_Block.SetFloat(k_Reveal, reveal01);
                if (e.melting[i]) m_Block.SetFloat(k_Holes, Mathf.Lerp(1f, e.holes[i], reveal01));
                r.SetPropertyBlock(m_Block);
            }
        }

        static void Restore(Element e)
        {
            for (int i = 0; i < e.renderers.Length; i++)
            {
                Renderer r = e.renderers[i];
                if (r == null) continue;
                r.SetPropertyBlock(null);
                r.sharedMaterials = e.originals[i];
            }
        }

        static void Finish(Element e)
        {
            if (e.transform != null) e.transform.SetPositionAndRotation(e.homePosition, e.homeRotation);
            Restore(e);
            SetVisible(e, true);
            e.phase = Phase.Done;
        }
    }
}
