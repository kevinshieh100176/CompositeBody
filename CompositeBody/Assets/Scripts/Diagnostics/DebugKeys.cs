using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Keyboard shortcuts for the interactions that otherwise need a headset and a 真人 to touch.
    ///
    /// Testing the second machine has meant having a person on it wearing a headset, reaching out
    /// and touching something, every single time -- which is a lot of asking for a favour to
    /// find out whether a gate replicated. These fire the same interactions through the same
    /// path the real touch takes, so what is being checked is still the real thing.
    ///
    /// WORKS ON A CLIENT, which is the point. The interaction registry is per machine and so are
    /// these keys, so PC B can report its own half of a two-player gate without anyone being in
    /// the room.
    ///
    /// Bound by ID rather than by object reference. The interactions live across a scene, an
    /// additively loaded scene, a beat's content root and the player prefab, and only some of
    /// them exist at any moment -- an id looked up when the key is pressed finds whichever is
    /// running, and says so plainly when none is.
    /// </summary>
    public class DebugKeys : MonoBehaviour
    {
        [Serializable]
        public struct Binding
        {
            public Key key;

            [Tooltip("The interaction's debugId: 'o0.awakening' for touching the scan, " +
                     "'beat.O2_Grasp' for the object gate, 'calibration' to arm the calibrator.")]
            public string debugId;

            [Tooltip("Local fires this machine's half, which is what a client wants. Both walks " +
                     "a two-player gate alone and only the host can do it.")]
            public DebugFireTarget target;
        }

        [SerializeField, Tooltip("Press the key, fire the interaction. Add rows for anything " +
                                 "else with a debugId -- no code needed.")]
        Binding[] m_Bindings =
        {
            new() { key = Key.Digit1, debugId = "o0.awakening", target = DebugFireTarget.Local },
            new() { key = Key.Digit2, debugId = "beat.O2_Grasp", target = DebugFireTarget.Local },
            new() { key = Key.Digit3, debugId = "beat.O1_Control", target = DebugFireTarget.Local },
            new() { key = Key.Digit4, debugId = "beat.O3_Communion", target = DebugFireTarget.Local },
        };

        [SerializeField, Tooltip("Hold this to use the number row, so typing a seat number into " +
                                 "a field does not fire a beat. Set to None to drop the guard.")]
        Key m_Modifier = Key.LeftCtrl;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            var go = new GameObject("~DebugKeys");
            DontDestroyOnLoad(go);
            go.AddComponent<DebugKeys>();
        }

        void Start()
        {
            var lines = new System.Text.StringBuilder();
            foreach (Binding b in m_Bindings)
                lines.Append($"\n[DebugKeys]   {Chord(b.key)}  {b.debugId} ({b.target})");

            Debug.Log("[DebugKeys] ready." + lines);
        }

        string Chord(Key key) =>
            m_Modifier == Key.None ? key.ToString() : $"{m_Modifier}+{key}";

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (m_Modifier != Key.None && !keyboard[m_Modifier].isPressed) return;

            foreach (Binding binding in m_Bindings)
            {
                if (binding.key == Key.None || !keyboard[binding.key].wasPressedThisFrame) continue;
                Fire(binding);
            }
        }

        void Fire(Binding binding)
        {
            IDebugInteraction found = null;
            foreach (IDebugInteraction entry in InteractionDebug.entries)
            {
                if (entry != null && entry.debugId == binding.debugId) { found = entry; break; }
            }

            if (found == null)
            {
                // Named, and with what IS registered, because "nothing happened" on a key press
                // is indistinguishable from a key that did not register.
                var have = new System.Text.StringBuilder();
                foreach (IDebugInteraction entry in InteractionDebug.entries)
                    if (entry != null) have.Append($"\n[DebugKeys]   {entry.debugId}");

                Debug.LogWarning($"[DebugKeys] {Chord(binding.key)}: nothing registered as " +
                                 $"'{binding.debugId}' right now. It belongs to a beat that is " +
                                 "not running, or to content that has not loaded." +
                                 (have.Length > 0
                                     ? "\n[DebugKeys] what IS registered:" + have
                                     : "\n[DebugKeys] nothing is registered at all."));
                return;
            }

            if (!InteractionDebug.fireEnabled)
            {
                Debug.LogWarning($"[DebugKeys] {Chord(binding.key)}: firing is switched off in " +
                                 $"InteractionDebug, so '{binding.debugId}' was not fired.");
                return;
            }

            if (!found.DebugCanFire(binding.target))
            {
                Debug.LogWarning($"[DebugKeys] {Chord(binding.key)}: '{binding.debugId}' is " +
                                 $"registered but refused {binding.target} -- it has probably " +
                                 "already happened, or this machine is not allowed to fire it.");
                return;
            }

            found.DebugFire(binding.target);
            Debug.Log($"[DebugKeys] {Chord(binding.key)}: fired '{binding.debugId}' " +
                      $"({binding.target}) -- {found.debugLabel}");
        }
    }
}
