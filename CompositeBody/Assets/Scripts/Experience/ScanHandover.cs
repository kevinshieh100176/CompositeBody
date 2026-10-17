using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using CompositeBody.PointClouds;

namespace CompositeBody.Experience
{
    /// <summary>
    /// The 真人 goes, and leaves something behind in its place.
    ///
    /// Runs once the player's own body has finished arriving -- not when they touched the 真人,
    /// which is half a minute earlier. The scan dissolves, and while it is dissolving the object
    /// fades up where the scan was standing. One leaves and one takes its place, in the same
    /// spot, so the object reads as what the 真人 turned into rather than as a prop that appeared
    /// nearby.
    ///
    /// THE PROPS' TRANSFORMS ARE NOT TOUCHED, by this or by the builder on a re-run. Where an
    /// object sits is a staging decision and it is made by hand; this only decides when it is
    /// there. An earlier version placed each prop at its 真人's bounds centre when the handover
    /// started, which quietly overrode whatever had been authored.
    ///
    /// One 0-to-1 value with a curve each, like <see cref="AvatarFormation"/>: the overlap
    /// between the scan leaving and the object arriving is the whole effect, so it is authored
    /// as two curves over one timeline rather than as two durations and a delay that have to be
    /// reasoned about to see whether they overlap at all.
    ///
    /// Local, like everything else in the beat. Each player's 真人 and each player's object are
    /// their own; nothing here crosses the network.
    /// </summary>
    public class ScanHandover : MonoBehaviour
    {
        [System.Serializable]
        public class Station
        {
            [Tooltip("The 真人 that dissolves.")]
            public PointCloudFigure figure;

            [Tooltip("The object that takes its place. Moved to the figure when the handover starts.")]
            public Transform prop;

            [Tooltip("Renderers to fade up. Found under the prop if left empty.")]
            public Renderer[] propRenderers;

            [Tooltip("Grabbing is switched on only once the object is substantially there. " +
                     "Reaching for something still half transparent gets a grab that feels like " +
                     "a bug.")]
            public XRGrabInteractable grab;
        }

        [SerializeField] Station[] m_Stations = new Station[0];

        [Header("Timing")]
        [SerializeField, Range(0f, 1f), Tooltip("The whole handover, 0 to 1. Drag it to see any " +
                                                "moment of the exchange without pressing Play.")]
        float m_Progress;

        [SerializeField, Tooltip("Ignore the clock and use the slider above.")]
        bool m_Scrub;

        [SerializeField, Min(0.1f), Tooltip("Seconds for the whole exchange.")]
        float m_TotalSeconds = 12f;

        [SerializeField, Min(0f), Tooltip("Pause after the body has finished arriving. A beat of " +
                                          "stillness before the 真人 begins to go.")]
        float m_StartDelay = 2f;

        [SerializeField, Tooltip("Progress -> how far the 真人 has dissolved.")]
        AnimationCurve m_ScanCurve = new(new Keyframe(0f, 0f, 0f, 0f),
                                         new Keyframe(0.7f, 1f, 0f, 0f),
                                         new Keyframe(1f, 1f, 0f, 0f));

        [SerializeField, Tooltip("Progress -> how present the object is. Starts at the same " +
                                 "moment as the scan curve, so the object begins arriving on " +
                                 "the frame the 真人 begins to go: one exchange, not two events.")]
        AnimationCurve m_PropCurve = new(new Keyframe(0f, 0f, 0f, 0f),
                                         new Keyframe(0.8f, 1f, 0f, 0f),
                                         new Keyframe(1f, 1f, 0f, 0f));

        [SerializeField, Range(0f, 1f), Tooltip("Presence above which the object can be grabbed.")]
        float m_GrabbableAt = 0.6f;

        [Header("Fade")]
        [SerializeField, Tooltip("Colour property whose alpha is the object's presence.")]
        string m_ColorProperty = "_BaseColor";

        int m_ColorId;
        MaterialPropertyBlock m_Block;
        bool m_Running, m_Done;
        float m_StartTime;

        /// <summary>Where the exchange has got to, 0 to 1.</summary>
        public float progress => m_Progress;

        /// <summary>
        /// Put the exchange at a given point, now, without the clock. For the still renderer and
        /// for scrubbing: the same path LateUpdate takes, so a still shows what the show shows.
        /// </summary>
        public void SetProgressForEditor(float value)
        {
            Resolve();
            Apply(value);
        }

        /// <summary>True once the 真人 are gone and the objects are fully there.</summary>
        public bool complete => m_Done;

        void Awake()
        {
            Resolve();
            Apply(0f);
        }

        /// <summary>
        /// Find each station's renderers and grab handle. Separate from Awake because the editor
        /// paths -- scrubbing, the still renderer -- need it too, and Awake does not run outside
        /// play mode: without this, a scrub left both objects fully visible at progress 0 and
        /// the stills showed an exchange that had not started as one already finished.
        /// </summary>
        void Resolve()
        {
            if (m_ColorId == 0) m_ColorId = Shader.PropertyToID(m_ColorProperty);
            m_Block ??= new MaterialPropertyBlock();

            foreach (Station station in m_Stations)
            {
                if (station == null) continue;
                if (station.propRenderers == null || station.propRenderers.Length == 0)
                    station.propRenderers = station.prop != null
                        ? station.prop.GetComponentsInChildren<Renderer>(true)
                        : new Renderer[0];

                if (station.grab == null && station.prop != null)
                    station.grab = station.prop.GetComponentInChildren<XRGrabInteractable>(true);
            }
        }

        void LateUpdate()
        {
            if (m_Scrub)
            {
                Apply(m_Progress);
                return;
            }

            // Not a return once finished: the O-0 cue animates m_Dissolve on both figures and
            // its track clamps the last key forever, so the frame this stops writing, the
            // timeline puts the 真人 back. Holding the end state is what keeps them gone.
            if (m_Done) { Apply(1f); return; }

            if (!m_Running)
            {
                // Waits on the arrival having FINISHED, not on the touch. AvatarFormation.formed
                // goes true the moment a 真人 is touched; arrived waits for the body to be there.
                AvatarFormation formation = AvatarFormation.local;
                if (formation == null || !formation.arrived) return;

                m_Running = true;
                m_StartTime = ExperienceClock.now + m_StartDelay;
                Debug.Log($"[ScanHandover] body has arrived; the 真人 begin to go in " +
                          $"{m_StartDelay:F1}s, over {m_TotalSeconds:F1}s.");
            }

            float t = (ExperienceClock.now - m_StartTime) / Mathf.Max(m_TotalSeconds, 0.01f);
            if (t < 0f) return;

            Apply(t);

            if (t >= 1f)
            {
                m_Done = true;
                Debug.Log("[ScanHandover] the 真人 are gone and the objects are here.");
            }
        }

        void OnValidate()
        {
            if (!m_Scrub || !isActiveAndEnabled) return;
            Resolve();
            Apply(m_Progress);
        }


        void Apply(float progress)
        {
            m_Progress = Mathf.Clamp01(progress);

            float scan = Mathf.Clamp01(m_ScanCurve.Evaluate(m_Progress));
            float prop = Mathf.Clamp01(m_PropCurve.Evaluate(m_Progress));

            foreach (Station station in m_Stations)
            {
                if (station == null) continue;

                if (station.figure != null) station.figure.dissolve = scan;

                SetPresence(station, prop);

                if (station.grab != null)
                {
                    bool grabbable = prop >= m_GrabbableAt;
                    if (station.grab.enabled != grabbable) station.grab.enabled = grabbable;
                }
            }
        }

        /// <summary>
        /// Alpha, not the dither the body uses. The dither hides what is NEARER than its
        /// threshold, which is exactly right for a body you are standing inside and exactly
        /// wrong for something you pick up and bring to your face -- it would vanish as you
        /// raised it. These materials are built transparent for the same reason.
        /// </summary>
        void SetPresence(Station station, float presence)
        {
            if (station.propRenderers == null) return;
            m_Block ??= new MaterialPropertyBlock();

            foreach (Renderer r in station.propRenderers)
            {
                if (r == null) continue;

                Color colour = r.sharedMaterial != null && r.sharedMaterial.HasProperty(m_ColorId)
                    ? r.sharedMaterial.GetColor(m_ColorId)
                    : Color.white;
                colour.a = presence;

                r.GetPropertyBlock(m_Block);
                m_Block.SetColor(m_ColorId, colour);
                r.SetPropertyBlock(m_Block);

                // Nothing to draw at zero, and these are not cheap meshes.
                bool hidden = presence <= 0.002f;
                if (r.forceRenderingOff != hidden) r.forceRenderingOff = hidden;
            }
        }
    }
}
