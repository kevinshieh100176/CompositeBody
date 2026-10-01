using UnityEngine;
using UnityEngine.InputSystem;
using XRMultiplayer;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Stand-in content for a beat that hasn't been built yet. It shows a labelled marker, logs
    /// its entry and exit, and -- on a beat that has a gate -- lets whoever is testing report
    /// the local player's half of it from the keyboard.
    ///
    /// This exists so the running order is walkable end to end before any of the real content,
    /// audio or VFX is in: the beat chain, the two-player gates, the staff overrides and the
    /// replication of all three can be verified on two machines against markers, and then each
    /// marker swapped for the real thing one beat at a time without the spine changing.
    /// </summary>
    public class PlaceholderBeat : BeatController
    {
        [SerializeField, TextArea(2, 6), Tooltip("The script's own description of this beat. Shown on the marker.")]
        string m_Synopsis;

        [SerializeField, Tooltip("Reports this machine's half of the beat's gate. A desk-test stand-in for whatever the real interaction turns out to be; harmless on a beat with no gate.")]
        Key m_ReportGateKey = Key.Space;

        [SerializeField, Tooltip("Marker label, filled in by the scene builder.")]
        TextMesh m_Label;

        public string synopsis => m_Synopsis;

        protected override void OnEnterBeat()
        {
            Utils.Log($"[PlaceholderBeat] ENTER {StoryBeats.DisplayName(beat)}");
            RefreshLabel();
        }

        protected override void OnExitBeat()
        {
            Utils.Log($"[PlaceholderBeat] EXIT  {StoryBeats.DisplayName(beat)}");
        }

        void Update()
        {
            if (!isActiveBeat) return;

            if (Keyboard.current != null && Keyboard.current[m_ReportGateKey].wasPressedThisFrame)
            {
                var director = ExperienceDirector.Instance;
                if (director == null) return;

                if (string.IsNullOrEmpty(director.currentGateTaskId))
                {
                    Utils.Log($"[PlaceholderBeat] {StoryBeats.DisplayName(beat)} ends on its own timer; nothing to report.");
                    return;
                }

                Utils.Log($"[PlaceholderBeat] Reporting local half of '{director.currentGateTaskId}'.");
                director.ReportLocalGateComplete();
            }
        }

        void RefreshLabel()
        {
            if (m_Label == null) return;

            var director = ExperienceDirector.Instance;
            string exit = "(staff only)";
            if (director != null)
            {
                BeatDefinition def = director.DefinitionFor(beat);
                if (!string.IsNullOrEmpty(def.gateTaskId)) exit = $"waits for BOTH players  [{m_ReportGateKey}]";
                else if (def.autoAdvanceSeconds > 0f) exit = $"holds {def.autoAdvanceSeconds:0}s";
            }

            m_Label.text = $"{StoryBeats.DisplayName(beat)}\n{exit}\n\n{m_Synopsis}";
        }
    }
}
