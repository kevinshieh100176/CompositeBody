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
    /// AND THEY ARRIVE ON THE TIMELINE TOO, which is the other half of this component and was
    /// missing. 「現在O_0的SCAN 一開始就在 改成跟第一段VO(A1/B1)一起出現」: the 真人 used to be standing
    /// in the room from the first frame, so the opening had no one entering it -- the room simply
    /// already contained two people. <see cref="m_Arrival"/> is a second serialized field on a
    /// second clip, and the beat now reads as 空房間 -> 真人出現 -> 說話 -> 消失 instead of starting
    /// one step in.
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

        [SerializeField, Range(0f, 1f), Tooltip("WHETHER THEY ARE THERE AT ALL, 0 to 1. DRIVEN " +
                                                "BY O-0'S TIMELINE -- 0 is an empty room, 1 is " +
                                                "the 真人 standing in it waiting to be dissolved.")]
        float m_Arrival = 1f;

        [SerializeField, Tooltip("Arrival -> how much of the 真人 is there. Shaped here and not " +
                                 "on the clip, for the same reason as the curve above.")]
        AnimationCurve m_ArrivalCurve = new(new Keyframe(0f, 0f, 0f, 0f),
                                           new Keyframe(1f, 1f, 0f, 0f));

        bool m_Done;

        /// <summary>Where the exchange has got to, 0 to 1.</summary>
        public float progress => m_Progress;

        /// <summary>How much of the 真人 has arrived, 0 to 1. What O-0's first clip drives.</summary>
        public float arrival => m_Arrival;

        /// <summary>True once the 真人 are gone.</summary>
        public bool complete => m_Done || m_Progress >= 0.999f;

        /// <summary>
        /// Put the exchange at a given point, now, without the timeline. For the still renderer
        /// and for scrubbing: the same path LateUpdate takes, so a still shows what the show
        /// shows.
        /// </summary>
        public void SetProgressForEditor(float value) => Apply(value);

        /// <summary>
        /// Put the arrival at a given point, now, without the timeline. The still renderer needs
        /// it: the prefab is authored at an empty room, so anything that photographs the 真人
        /// outside play mode has to say that they are there first.
        /// </summary>
        public void SetArrivalForEditor(float value)
        {
            m_Arrival = Mathf.Clamp01(value);
            Apply(m_Progress);
        }

        // Apply(0f) is "nothing has dissolved", and with the arrival authored at 0 that is an
        // empty room rather than two 真人 standing in it. Before the arrival existed this line
        // was what put them on screen at the first frame.
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
            float here = Mathf.Clamp01(m_ArrivalCurve.Evaluate(Mathf.Clamp01(m_Arrival)));

            // THE TWO ENDS COMPOSE AS A MAXIMUM, not as a sum and not one replacing the other.
            // dissolve is how much of the figure is NOT there, so "has not arrived yet" and
            // "has already left" are the same picture and whichever is further along wins. It
            // also means the departure cannot be undone by an arrival clip still holding at 1,
            // which is exactly what it does hold at for the eighty seconds in between.
            float dissolve = Mathf.Max(1f - here, scan);

            foreach (Station station in m_Stations)
            {
                if (station?.figure != null) station.figure.dissolve = dissolve;
            }

            if (m_Done || m_Progress < 0.999f || !Application.isPlaying) return;
            m_Done = true;
            Debug.Log("[ScanHandover] the 真人 are gone.");
        }
    }
}
