using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// One key that takes every piece of UI off the screen, and puts it back.
    ///
    /// The panels have grown a key each -- F1 for the interaction list, F2 for the scrubber,
    /// F1..F8 for the debug scenery, F12 for the role view -- and clearing the screen before a
    /// screenshot, a video capture or someone walking up to the monitor has meant remembering
    /// all of them and which of them were open. This is the master switch: press it, the screen
    /// is the piece; press it again, every panel is exactly as it was.
    ///
    /// IT HIDES, IT DOES NOT CLOSE. Each panel keeps its own open/closed state while the master
    /// switch is down, so un-hiding restores what was on screen rather than a default layout.
    /// For the same reason the panels keep running their Update: the scrubber still answers F2,
    /// the rehearsal still loops, and switching back shows the state they have reached rather
    /// than the state they were frozen in.
    ///
    /// BOTH KINDS OF UI, which is why it takes two mechanisms. The canvas panels are switched at
    /// their Canvas component, which stops them rendering and stops their raycaster answering;
    /// the IMGUI panels cannot be switched from outside at all, so they read
    /// <see cref="visible"/> at the top of their OnGUI. A new IMGUI panel that forgets that line
    /// will not hide -- there is no way to make that automatic short of disabling the component,
    /// which would stop its Update too.
    ///
    /// Canvases that were already switched off are left alone and are not switched on by the
    /// restore: the switch remembers what it touched, so it cannot hand back a screen with more
    /// on it than there was before.
    /// </summary>
    public class UiVisibility : MonoBehaviour
    {
        [SerializeField, Tooltip("Hides and shows every panel at once. Backquote (the ` key " +
                                 "left of 1) because the per-panel keys have taken the function " +
                                 "row and the debug firing has taken the letters.")]
        Key m_ToggleKey = Key.Backquote;

        [SerializeField, Min(0.1f), Tooltip("While hidden, how often to look for UI that has " +
                                            "appeared since -- a panel that builds itself, or a " +
                                            "scene loaded additively. Hiding would be a lie if " +
                                            "the next canvas to arrive drew itself anyway.")]
        float m_RescanInterval = 0.5f;

        /// <summary>
        /// False while the master switch is down. IMGUI panels check this at the top of OnGUI;
        /// canvas panels need not, they are switched at the Canvas.
        /// </summary>
        public static bool visible { get; private set; } = true;

        /// <summary>Raised on every change, for anything that has to do more than not draw.</summary>
        public static event System.Action<bool> changed;

        // What this switch turned off, so the restore puts back that and nothing else.
        static readonly List<Canvas> s_Hidden = new();

        float m_NextRescan;

        /// <summary>
        /// Makes itself at startup, like <see cref="DebugKeys"/> and <see cref="ShowScrubber"/>:
        /// a switch for clearing the screen is wanted in every scene and in every build, and one
        /// that lives in a scene is one more thing to wire and to find missing on the night.
        ///
        /// Unless one is already in the scene. The key below can only be changed in an inspector
        /// on an instance that exists before play, so putting the component on an object is how
        /// you rebind it -- and that has to not end in two switches fighting over one key.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (FindAnyObjectByType<UiVisibility>(FindObjectsInactive.Include) != null) return;

            var go = new GameObject("~UiVisibility");
            DontDestroyOnLoad(go);
            go.AddComponent<UiVisibility>();
        }

        void Start()
        {
            // Static state outlives a domain reload that keeps play mode running, so a scene
            // entered with the switch down would have no way back.
            visible = true;
            s_Hidden.Clear();

            Debug.Log($"[UiVisibility] ready -- {m_ToggleKey} hides every panel at once.");
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && m_ToggleKey != Key.None &&
                keyboard[m_ToggleKey].wasPressedThisFrame && !TypingIntoAField())
                Toggle();

            if (!visible && Time.unscaledTime >= m_NextRescan)
            {
                m_NextRescan = Time.unscaledTime + m_RescanInterval;
                HideCanvases();
            }
        }

        /// <summary>
        /// A field with the caret in it eats the key. The staff panel takes a TouchDesigner host
        /// and an IP address by hand, and a backquote typed into one of those should land in the
        /// field rather than hide the field.
        /// </summary>
        static bool TypingIntoAField()
        {
            GameObject selected = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject
                : null;
            if (selected == null) return false;

            var field = selected.GetComponent<InputField>();
            return field != null && field.isFocused;
        }

        public static void Toggle() => SetVisible(!visible);

        /// <summary>Safe to call from a button, a staff script or a test.</summary>
        public static void SetVisible(bool on)
        {
            if (on == visible) return;
            visible = on;

            if (on) RestoreCanvases();
            else HideCanvases();

            Debug.Log($"[UiVisibility] UI {(on ? "shown" : "hidden")}.");
            changed?.Invoke(on);
        }

        /// <summary>
        /// Every canvas that is currently drawing, including the world-space ones -- "all UI"
        /// means the headset too, not only the mirror window.
        ///
        /// Inactive objects are skipped: a canvas on an object that is switched off is not on
        /// screen, and switching its component off would leave it dark when its owner switches
        /// the object back on.
        /// </summary>
        static void HideCanvases()
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (Canvas canvas in canvases)
            {
                if (canvas == null || !canvas.enabled) continue;

                // Nested canvases ride on their root; switching only the root is enough and
                // keeps the restore list short.
                if (!canvas.isRootCanvas) continue;

                canvas.enabled = false;
                s_Hidden.Add(canvas);
            }
        }

        static void RestoreCanvases()
        {
            foreach (Canvas canvas in s_Hidden)
            {
                if (canvas != null) canvas.enabled = true;
            }
            s_Hidden.Clear();
        }
    }
}
