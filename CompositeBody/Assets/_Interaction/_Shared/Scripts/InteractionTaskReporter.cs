using UnityEngine;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Interaction
{
    /// <summary>
    /// Turns an interaction's completion UnityEvent into a StoryProgressManager task, so a
    /// <c>ShowHoldMarker</c> on the show timeline can wait for it.
    ///
    /// WHY THIS EXISTS. The modules in <c>Assets/_Interaction/</c> announce that they are finished
    /// with a plain UnityEvent (<c>HomeAssembly.onCompleted</c>, <c>OAController.onCompleted</c>),
    /// which is right for them: a module should not know what the show does with the news. The
    /// show, for its part, gates on task ids. This is the one-line adapter between the two, and it
    /// is a component rather than a method on either side so the wiring is visible in the
    /// inspector and survives a rebuild of the module.
    ///
    /// REPORTS THIS MACHINE'S OWN ROLE. <c>ReportTaskCompleteRpc</c> is <c>SendTo.Server</c> and
    /// the server attributes it to the sender's role, so every machine calls it for itself and
    /// <c>StoryProgressManager</c> completes the task once each role present has reported. That is
    /// also why the completion events fire on every machine rather than only the server. A solo
    /// run is handled there too -- <c>IsTaskComplete</c> asks only the roles actually connected.
    ///
    /// IDEMPOTENT ON PURPOSE. <c>HomeAssembly</c> re-fires <c>onCompleted</c> on a machine that
    /// disconnects and rejoins, and setting a role's flag true twice is the same as setting it
    /// once, so a repeat report is harmless. <see cref="m_Once"/> stops the duplicate RPC anyway,
    /// because a second one is noise in the log that reads like a bug.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionTaskReporter : MonoBehaviour
    {
        [SerializeField, Tooltip("The StoryProgressManager task this reports, e.g. s2.lamp.done. " +
                                 "ASCII, unique across the show, and the same id the ShowHoldMarker " +
                                 "that waits for it carries.")]
        string m_TaskId = "";

        [SerializeField, Tooltip("Debug-panel heading for the gate row. ASCII -- the panel's font " +
                                 "has no CJK glyphs.")]
        string m_GateLabel = "";

        [SerializeField, Tooltip("Report at most once per run of this scene.")]
        bool m_Once = true;

        bool m_Reported;

        public string taskId => m_TaskId;
        public string gateLabel => m_GateLabel;
        public bool reported => m_Reported;

        /// <summary>Builder hook.</summary>
        public void Configure(string taskId, string gateLabel)
        {
            m_TaskId = taskId;
            m_GateLabel = gateLabel;
        }

        void OnEnable()
        {
            // So the gate has a named row in the debug panel before anyone has reported it --
            // otherwise the row only appears once the task exists in the progress list, which is
            // exactly when it has stopped being the interesting thing to look at.
            if (!string.IsNullOrWhiteSpace(m_TaskId))
                InteractionDebug.RegisterTaskGate(m_TaskId, string.IsNullOrWhiteSpace(m_GateLabel) ? m_TaskId : m_GateLabel);
        }

        /// <summary>Wire this to the module's completion event.</summary>
        public void Report()
        {
            if (string.IsNullOrWhiteSpace(m_TaskId))
            {
                Debug.LogWarning($"[TaskReporter] '{name}' has no task id, so nothing was reported.", this);
                return;
            }
            if (m_Once && m_Reported) return;

            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress == null)
            {
                // Not an error: a module can be exercised in its demo scene with no session, and
                // there is no gate to open there.
                Debug.Log($"[TaskReporter] no StoryProgressManager, so '{m_TaskId}' was not reported.", this);
                return;
            }

            m_Reported = true;
            progress.ReportTaskCompleteRpc(m_TaskId);
            Debug.Log($"[TaskReporter] reported '{m_TaskId}'.", this);
        }

        /// <summary>Lets a re-run of the beat report again.</summary>
        public void ResetReported() => m_Reported = false;
    }
}
