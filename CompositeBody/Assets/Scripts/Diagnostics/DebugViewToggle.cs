using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using CompositeBody.Multiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// On-screen switches for the debug scenery -- the play space outline, the debug environment,
    /// the stick locomotion.
    ///
    /// Drawn with IMGUI, which is the point rather than laziness: IMGUI renders to the desktop
    /// window, so the operator at the monitor gets buttons and the person in the headset does not
    /// get a panel floating in the piece. A world-space canvas would be visible to both, and a
    /// debug control that the audience can see is not a debug control.
    ///
    /// Targets are resolved by name when nothing is wired explicitly, because these objects get
    /// renamed as the room changes and a tool for finding problems should not become one.
    /// </summary>
    public class DebugViewToggle : MonoBehaviour
    {
        [SerializeField, Tooltip("What to switch. Left empty, the names below are looked up " +
                                 "instead when the scene starts.")]
        GameObject[] m_Targets = new GameObject[0];

        [SerializeField, Tooltip("Looked up by name if the list above is empty. Missing ones are " +
                                 "reported once and then ignored.")]
        string[] m_FallbackNames = { "PlaySpace_Debug", "Debug_Enviroment" };

        [SerializeField, Tooltip("Components to switch rather than whole objects -- the stick " +
                                 "locomotion, say, which lives on this same object.")]
        MonoBehaviour[] m_Behaviours = new MonoBehaviour[0];

        [Header("Panel")]
        [SerializeField, Tooltip("Draw the buttons at all. Off leaves the keyboard shortcuts.")]
        bool m_ShowPanel = true;

        [SerializeField, Tooltip("Which side of the screen the panel sits on. Right keeps it off " +
                                 "the staff panel, which builds itself down the left.")]
        Side m_Side = Side.Right;

        [SerializeField, Tooltip("Pixels in from the edge.")]
        Vector2 m_Margin = new(12f, 12f);

        [SerializeField, Tooltip("F1 toggles the first target, F2 the second, and so on. Handy " +
                                 "when the panel itself is in the way of what you are looking at.")]
        bool m_FunctionKeys = true;

        [SerializeField, Tooltip("Hide every target at startup, so a build does not open with the " +
                                 "debug scenery switched on in front of an audience.")]
        bool m_HiddenOnStart;

        [Header("Role")]
        [SerializeField, Tooltip("A row that renders this machine as Player A or Player B, for " +
                                 "checking the asymmetric views without a second headset.")]
        bool m_RoleSwitch = true;

        [SerializeField, Tooltip("F12 steps the role view. Off leaves the button.")]
        bool m_RoleKey = true;

        public enum Side { Left, Right }

        readonly List<GameObject> m_Resolved = new();
        readonly List<MonoBehaviour> m_ResolvedBehaviours = new();

        void Start()
        {
            m_Resolved.Clear();
            m_ResolvedBehaviours.Clear();

            foreach (GameObject target in m_Targets)
            {
                if (target != null) m_Resolved.Add(target);
            }

            if (m_Resolved.Count == 0)
            {
                foreach (string name in m_FallbackNames)
                {
                    // Inactive objects too: something switched off last run is exactly what
                    // someone is trying to switch back on.
                    GameObject found = FindIncludingInactive(name);
                    if (found != null) m_Resolved.Add(found);
                    else Debug.LogWarning($"[DebugView] no object named '{name}' in the scene.");
                }
            }

            foreach (MonoBehaviour behaviour in m_Behaviours)
            {
                if (behaviour != null) m_ResolvedBehaviours.Add(behaviour);
            }

            if (m_HiddenOnStart) SetAll(false);

            Debug.Log($"[DebugView] {m_Resolved.Count} object(s): " +
                      string.Join(", ", m_Resolved.ConvertAll(t => t.name)) +
                      (m_ResolvedBehaviours.Count > 0
                          ? $" | {m_ResolvedBehaviours.Count} component(s)"
                          : ""));
        }

        void Update()
        {
            // Input System rather than the legacy Input class. With the project set to the new
            // input handling only, UnityEngine.Input throws the moment it is touched -- and an
            // exception here would stop everything below it in the same Update from running.
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (m_FunctionKeys)
            {
                for (int i = 0; i < m_Resolved.Count && i < 8; i++)
                {
                    if (keyboard[Key.F1 + i].wasPressedThisFrame) Toggle(i);
                }
            }

            if (m_RoleSwitch && m_RoleKey && keyboard[Key.F12].wasPressedThisFrame) StepRoleView();
        }

        /// <summary>
        /// Own view -> Player A -> Player B -> before the roles arrive -> own view.
        ///
        /// Cycled rather than given four buttons: the panel sits over the game view and this is
        /// one row of it. The current state is written on the button, so a cycle is never a guess.
        /// </summary>
        public void StepRoleView()
        {
            PlayerRole? current = LocalRolePresenter.debugRole;
            PlayerRole? next;
            if (!current.HasValue) next = PlayerRole.Player1;
            else if (current.Value == PlayerRole.Player1) next = PlayerRole.Player2;
            else if (current.Value == PlayerRole.Player2) next = PlayerRole.Unassigned;
            else next = null;

            LocalRolePresenter.debugRole = next;
        }

        static string RoleLabel()
        {
            PlayerRole? role = LocalRolePresenter.debugRole;
            if (!role.HasValue) return "own view";
            if (role.Value == PlayerRole.Player1) return "Player A (purple)";
            if (role.Value == PlayerRole.Player2) return "Player B (yellow)";
            return "unassigned";
        }

        static Color RoleColour()
        {
            PlayerRole? role = LocalRolePresenter.debugRole;
            if (!role.HasValue) return new Color(0.35f, 0.35f, 0.38f);
            if (role.Value == PlayerRole.Player1) return new Color(0.62f, 0.35f, 1f);
            if (role.Value == PlayerRole.Player2) return new Color(0.95f, 0.85f, 0.35f);
            return new Color(0.5f, 0.5f, 0.55f);
        }

        void OnGUI()
        {
            if (!UiVisibility.visible) return;
            if (!m_ShowPanel) return;
            int rows = m_Resolved.Count + m_ResolvedBehaviours.Count;
            if (rows == 0 && !m_RoleSwitch) return;

            const float k_Width = 230f, k_Height = 26f, k_Gap = 4f;

            // Anchored to the edge every frame rather than to a stored pixel, so it stays put
            // when the game view is resized.
            float x = m_Side == Side.Right ? Screen.width - k_Width - m_Margin.x : m_Margin.x;
            float y = m_Margin.y;

            // One extra row for the all-at-once switch, which is what you actually want before
            // someone puts the headset on, and one for the input readout.
            string status = StatusLine();
            int extra = 1 + (string.IsNullOrEmpty(status) ? 0 : 1) + (m_RoleSwitch ? 1 : 0);
            float panelHeight = (k_Height + k_Gap) * (rows + extra) + 12f;

            GUI.Box(new Rect(x - 6f, y - 6f, k_Width + 12f, panelHeight), GUIContent.none);

            bool anythingOn = AnythingOn();
            Color previous = GUI.backgroundColor;

            GUI.backgroundColor = anythingOn ? new Color(0.95f, 0.55f, 0.35f) : new Color(0.35f, 0.75f, 0.45f);
            if (GUI.Button(new Rect(x, y, k_Width, k_Height),
                           anythingOn ? "Hide all debug" : "Show all debug"))
                SetAll(!anythingOn);
            GUI.backgroundColor = previous;
            y += k_Height + k_Gap;

            for (int i = 0; i < m_Resolved.Count; i++)
            {
                GameObject target = m_Resolved[i];
                if (target == null) continue;

                bool on = target.activeSelf;
                string key = m_FunctionKeys && i < 8 ? $"F{i + 1}  " : "";
                if (Row(new Rect(x, y, k_Width, k_Height), $"{key}{target.name}", on))
                    Toggle(i);
                y += k_Height + k_Gap;
            }

            foreach (MonoBehaviour behaviour in m_ResolvedBehaviours)
            {
                if (behaviour == null) continue;
                if (Row(new Rect(x, y, k_Width, k_Height),
                        behaviour.GetType().Name, behaviour.enabled))
                    behaviour.enabled = !behaviour.enabled;
                y += k_Height + k_Gap;
            }

            // The role view. Coloured by which player is being rendered rather than on/off,
            // because "on" would be meaningless here -- there is no neutral state, only whose
            // eyes you are borrowing.
            if (m_RoleSwitch)
            {
                Color was = GUI.backgroundColor;
                GUI.backgroundColor = RoleColour();
                string key = m_RoleKey ? "F12  " : "";
                if (GUI.Button(new Rect(x, y, k_Width, k_Height), $"{key}See as: {RoleLabel()}"))
                    StepRoleView();
                GUI.backgroundColor = was;
                y += k_Height + k_Gap;
            }

            // The live stick value. A number that moves means the binding is fine and something
            // else is wrong; a number that never moves says the opposite, and either answer is
            // better than pushing the stick harder.
            if (!string.IsNullOrEmpty(status))
                GUI.Label(new Rect(x + 4f, y + 4f, k_Width, k_Height), status);
        }

        string StatusLine()
        {
            foreach (MonoBehaviour behaviour in m_ResolvedBehaviours)
            {
                if (behaviour is DebugStickMove move) return move.statusLine;
            }
            return null;
        }

        static bool Row(Rect rect, string label, bool on)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = on ? new Color(0.3f, 0.8f, 1f) : new Color(0.35f, 0.35f, 0.38f);
            bool clicked = GUI.Button(rect, $"{label}  {(on ? "ON" : "off")}");
            GUI.backgroundColor = previous;
            return clicked;
        }

        bool AnythingOn()
        {
            foreach (GameObject target in m_Resolved)
            {
                if (target != null && target.activeSelf) return true;
            }
            foreach (MonoBehaviour behaviour in m_ResolvedBehaviours)
            {
                if (behaviour != null && behaviour.enabled) return true;
            }
            return false;
        }

        /// <summary>Everything at once, which is the state that matters before a show.</summary>
        public void SetAll(bool on)
        {
            foreach (GameObject target in m_Resolved)
            {
                if (target != null) target.SetActive(on);
            }
            foreach (MonoBehaviour behaviour in m_ResolvedBehaviours)
            {
                if (behaviour != null) behaviour.enabled = on;
            }
            Debug.Log($"[DebugView] all debug scenery {(on ? "on" : "off")}.");
        }

        /// <summary>Flip one target. Safe to call from a UI button or a staff script.</summary>
        public void Toggle(int index)
        {
            if (index < 0 || index >= m_Resolved.Count) return;
            GameObject target = m_Resolved[index];
            if (target == null) return;
            target.SetActive(!target.activeSelf);
            Debug.Log($"[DebugView] {target.name} {(target.activeSelf ? "on" : "off")}");
        }

        /// <summary>Flip one target by name, for anything that does not know the order.</summary>
        public void Toggle(string targetName)
        {
            for (int i = 0; i < m_Resolved.Count; i++)
            {
                if (m_Resolved[i] != null && m_Resolved[i].name == targetName) { Toggle(i); return; }
            }
        }

        static GameObject FindIncludingInactive(string name)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name == name) return t.gameObject;
            }
            return null;
        }
    }
}
