using UnityEngine;
using CompositeBody.PointClouds;

namespace CompositeBody.Experience
{
    /// <summary>
    /// The 真人 go, and the room is handed over to O-1.
    ///
    /// IT IS AUTHORED ON O-0'S TIMELINE NOW, not earned by a touch. The beat used to wait for a
    /// player to reach out and put a hand on a scan, then dissolve it once their own body had
    /// finished arriving. That made the whole opening hostage to one gesture: a player who did
    /// not reach out, or whose hand tracking dropped, stood in a room that never moved on. So
    /// <see cref="m_Progress"/> is a serialized field driven by an AnimationTrack on
    /// Assets/_Timeline/O_0.playable -- when the 真人 begin to go, and how long they take, are a
    /// clip you drag.
    ///
    /// THE ORDER IS THE OTHER WAY ROUND FROM BEFORE. The scan leaves FIRST and the player's own
    /// body arrives after it, which is what <c>O0TouchAwakening</c>'s own timeline cue fires. It
    /// used to be body-then-scan, because the scan was waiting on the body having arrived.
    ///
    /// THE OBJECTS ARE NOT HERE ANY MORE. This used to fade the coat and the mug up in the 真人's
    /// place on the same curve. They belong to O-1 -- they arrive with the boxes, on that beat's
    /// own timeline, through <see cref="PropPresence"/> -- so what is left here is one thing: how
    /// far the scans have gone.
    ///
    /// Local, like everything else in the beat. Each player's 真人 are their own; nothing here
    /// crosses the network, and it does not need to -- the timeline that drives it is already on
    /// the shared clock.
    /// </summary>
    public class ScanHandover : MonoBehaviour
    {
        [System.Serializable]
        public class Station
        {
            [Tooltip("The 真人 that dissolves.")]
            public PointCloudFigure figure;
        }

        [SerializeField] Station[] m_Stations = new Station[0];

        [SerializeField, Range(0f, 1f), Tooltip("THE WHOLE EXCHANGE, 0 to 1. DRIVEN BY O-0'S " +
                                                "TIMELINE -- drag it to see any moment of it " +
                                                "without pressing Play.")]
        float m_Progress;

        [SerializeField, Tooltip("Progress -> how far the 真人 has dissolved. Shaped here rather " +
                                 "than on the timeline clip, so the clip stays one straight ramp " +
                                 "that can be dragged and stretched without re-drawing a curve.")]
        AnimationCurve m_ScanCurve = new(new Keyframe(0f, 0f, 0f, 0f),
                                         new Keyframe(1f, 1f, 0f, 0f));

        bool m_Done;

        /// <summary>Where the exchange has got to, 0 to 1.</summary>
        public float progress => m_Progress;

        /// <summary>True once the 真人 are gone.</summary>
        public bool complete => m_Done || m_Progress >= 0.999f;

        /// <summary>
        /// Put the exchange at a given point, now, without the timeline. For the still renderer
        /// and for scrubbing: the same path LateUpdate takes, so a still shows what the show
        /// shows.
        /// </summary>
        public void SetProgressForEditor(float value) => Apply(value);

        void Awake() => Apply(0f);

        // LateUpdate, so the timeline has already written the field this frame -- and every
        // frame, not only when it changes: PointCloudFigure.Update pushes its own fields to the
        // material each frame anyway, and the dissolve has to be re-asserted or the figure's own
        // authored value shows through.
        //
        // ONCE GONE, GONE. An AnimationTrack stops writing the moment its clip ends and the
        // track blends the property back toward the value the scene was authored with -- so the
        // frame the dissolve clip finishes, the 真人 would walk back into the room. Latching the
        // end state is what keeps them out of it, and it is why the clip can be exactly as long
        // as the dissolve rather than having to run to the end of the beat.
        //
        // Play mode only, so scrubbing the timeline in the editor still shows them coming back
        // and the beat can be worked on in both directions.
        void LateUpdate() => Apply(m_Done ? 1f : m_Progress);

        void OnValidate()
        {
            if (isActiveAndEnabled) Apply(m_Progress);
        }

        void Apply(float progress)
        {
            m_Progress = Mathf.Clamp01(progress);
            float scan = Mathf.Clamp01(m_ScanCurve.Evaluate(m_Progress));

            foreach (Station station in m_Stations)
            {
                if (station?.figure != null) station.figure.dissolve = scan;
            }

            if (m_Done || m_Progress < 0.999f || !Application.isPlaying) return;
            m_Done = true;
            Debug.Log("[ScanHandover] the 真人 are gone.");
        }
    }
}
