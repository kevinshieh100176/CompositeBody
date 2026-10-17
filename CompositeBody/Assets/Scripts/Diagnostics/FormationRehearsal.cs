using UnityEngine;
using UnityEngine.InputSystem;
using CompositeBody.Experience;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Plays <see cref="AvatarFormation"/> on demand, so the membrane growing in and the Ch36
    /// body arriving behind it can be watched on a monitor instead of by putting a headset on,
    /// joining a session and reaching out to touch a 真人.
    ///
    /// It drives the real component rather than reproducing it. A rehearsal rig that animated
    /// the films itself would be a second implementation of the thing under test, and would
    /// agree with the real one right up until the moment it mattered.
    ///
    /// Timed off <see cref="ExperienceClock"/> rather than Time.time, because that is what
    /// AvatarFormation reads. With no session the clock falls through to Time.time by itself, so
    /// the rehearsal needs no network and still exercises the timing path the show runs on.
    /// </summary>
    public class FormationRehearsal : MonoBehaviour
    {
        [SerializeField, Tooltip("The avatar to drive. Found in the scene if left empty.")]
        AvatarFormation m_Formation;

        [SerializeField, Tooltip("Seconds of nothing before the touch, so the unformed state is " +
                                 "visible rather than flashing past on load.")]
        float m_LeadIn = 1.5f;

        [SerializeField, Tooltip("Run it again automatically, for leaving the scene playing " +
                                 "while judging the timing.")]
        bool m_Loop;

        [SerializeField, Min(1f), Tooltip("Seconds from the touch to the restart, when looping. " +
                                          "Needs to outlast membrane + gap + body.")]
        float m_LoopSeconds = 40f;

        float m_Started = -1f;

        void Start()
        {
            if (m_Formation == null) m_Formation = FindFirstObjectByType<AvatarFormation>();
            if (m_Formation == null)
            {
                Debug.LogError("[Rehearsal] no AvatarFormation in the scene.");
                enabled = false;
                return;
            }

            Restart();
        }

        void Restart()
        {
            m_Formation.Unform();
            m_Started = ExperienceClock.now;
            Debug.Log($"[Rehearsal] unformed; touching in {m_LeadIn:F1}s.");
        }

        void Update()
        {
            if (m_Formation == null) return;

            float t = ExperienceClock.now - m_Started;

            if (!m_Formation.formed && t >= m_LeadIn) m_Formation.Form();
            if (m_Loop && t >= m_LeadIn + m_LoopSeconds) Restart();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.spaceKey.wasPressedThisFrame) m_Formation.Form();
            if (keyboard.rKey.wasPressedThisFrame) Restart();
            if (keyboard.lKey.wasPressedThisFrame) m_Loop = !m_Loop;
        }

        void OnGUI()
        {
            if (m_Formation == null) return;

            const float w = 300f, h = 24f, gap = 4f;
            float x = 14f, y = 14f;

            float t = ExperienceClock.now - m_Started;
            GUI.Box(new Rect(x - 6f, y - 6f, w + 12f, (h + gap) * 5f + 12f), GUIContent.none);

            GUI.Label(new Rect(x, y, w, h),
                      m_Formation.formed
                          ? $"forming   t+{Mathf.Max(t - m_LeadIn, 0f):F1}s"
                          : $"unformed  touch in {Mathf.Max(m_LeadIn - t, 0f):F1}s");
            y += h + gap;

            if (GUI.Button(new Rect(x, y, w, h), "Touch now  (Space)")) m_Formation.Form();
            y += h + gap;

            if (GUI.Button(new Rect(x, y, w, h), "Back to unformed  (R)")) Restart();
            y += h + gap;

            if (GUI.Button(new Rect(x, y, w, h), $"Loop: {(m_Loop ? "on" : "off")}  (L)"))
                m_Loop = !m_Loop;
            y += h + gap;

            GUI.Label(new Rect(x, y, w, h), "membrane grows, then the body fades in behind it");
        }
    }
}
