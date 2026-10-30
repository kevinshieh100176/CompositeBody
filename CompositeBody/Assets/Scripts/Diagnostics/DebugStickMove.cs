using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Fly the rig around with the right thumbstick. Debugging only.
    ///
    /// Binds its own InputAction in code rather than going through XRI's locomotion providers and
    /// the Default Input Actions asset. The rig already carries a full XRI locomotion stack --
    /// grab move, teleport, snap turn -- and adding a second move provider to it means two things
    /// writing the origin's position on the same frame. A private action touches none of that and
    /// can be switched off without leaving anything half-configured behind.
    ///
    /// SEVERAL BINDINGS, NOT ONE. The Input System's XR layouts do not agree on what the stick is
    /// called: the Oculus layout calls it "thumbstick", OpenVR exposes both that and
    /// "primary2DAxis", and a controller that comes up on the generic XRController layout may
    /// only have the latter. One path that happens to be wrong for the device in the room looks
    /// exactly like a broken script. Keyboard and gamepad are bound as well, because most of the
    /// time this gets used there is no headset on at all.
    ///
    /// It reports what it actually resolved on enable, so "it does not move" can be answered by
    /// reading the console instead of by guessing.
    ///
    /// THIS BREAKS THE ROOM, on purpose. The piece is co-located: the virtual floor is the real
    /// floor, and the play space outline is where the real walls are. Moving the rig slides that
    /// correspondence apart, so a player who walks after using this is walking relative to the
    /// wrong room. Recentre puts it back.
    /// </summary>
    public class DebugStickMove : MonoBehaviour
    {
        [SerializeField, Tooltip("Metres per second at full deflection.")]
        float m_Speed = 1.5f;

        [SerializeField, Range(0f, 0.6f), Tooltip("Stick travel ignored before moving. Thumbsticks " +
                                                  "rest off-centre and the rig would creep.")]
        float m_Deadzone = 0.18f;

        [SerializeField, Tooltip("Move in the direction the head is facing. Off moves along the " +
                                 "rig's own axes, which is steadier when lining something up.")]
        bool m_RelativeToHead = true;

        [SerializeField, Tooltip("Right grip plus stick moves up and down. Q and E on the keyboard.")]
        bool m_AllowVertical = true;

        readonly System.Collections.Generic.List<MonoBehaviour> m_Suspended = new();
        Vector3 m_Home;
        bool m_HasHome;
        bool m_Moved;
        bool m_RebindPending;
        bool m_ReportedXrFallback;
        UnityEngine.XR.InputDevice m_XrRight;

        InputAction m_Move;
        InputAction m_Lift;
        InputAction m_Recentre;
        XROrigin m_Origin;
        Transform m_Rig;

        /// <summary>One line for the debug panel: what is bound, and what it is reading now.</summary>
        public string statusLine { get; private set; } = "stick: (not started)";

        void OnEnable()
        {
            // Built here, not in an asset: a binding in code cannot be lost by someone opening
            // the Default Input Actions and resaving it.
            m_Move = new InputAction("Debug Move", InputActionType.Value, expectedControlType: "Vector2");
            m_Move.AddBinding("<XRController>{RightHand}/thumbstick");
            m_Move.AddBinding("<XRController>{RightHand}/primary2DAxis");
            m_Move.AddBinding("<XRController>{RightHand}/Primary2DAxis");
            // Usage rather than name, and the OpenXR layout by its own name. The Quest comes up
            // as OculusTouchController under OpenXR, and which spelling of the stick that layout
            // exposes has moved between package versions.
            m_Move.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
            m_Move.AddBinding("<OculusTouchController>{RightHand}/thumbstick");
            m_Move.AddBinding("<Gamepad>/rightStick");
            m_Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            m_Move.Enable();

            m_Lift = new InputAction("Debug Lift", InputActionType.Value, expectedControlType: "Axis");
            m_Lift.AddBinding("<XRController>{RightHand}/grip");
            m_Lift.AddBinding("<XRController>{RightHand}/gripPressed");
            m_Lift.Enable();

            m_Recentre = new InputAction("Debug Recentre", InputActionType.Button);
            m_Recentre.AddBinding("<Keyboard>/f4");
            m_Recentre.Enable();

            Report();
            SuspendTurnProviders(true);

            // The controllers are not there yet when a scene starts. OpenXR brings the session
            // up well after the first Update, so binding once in OnEnable resolves the keyboard
            // and nothing else -- which is exactly what the console said when this did not work:
            // "bound to: Keyboard/w, Keyboard/s, Keyboard/a, Keyboard/d", logged a full second
            // before the Oculus Touch profile was registered.
            InputSystem.onDeviceChange += HandleDeviceChange;
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= HandleDeviceChange;
            Dispose(ref m_Move);
            Dispose(ref m_Lift);
            Dispose(ref m_Recentre);
            SuspendTurnProviders(false);
            statusLine = "stick: (disabled)";
        }

        /// <summary>
        /// Note that a device arrived or left. The rebind itself happens in the next Update
        /// rather than here: this runs inside the Input System's own device handling, and
        /// reaching back in to disable and re-enable an action from there is asking for
        /// re-entrancy. A frame of delay is invisible.
        /// </summary>
        void HandleDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change != InputDeviceChange.Added && change != InputDeviceChange.Removed &&
                change != InputDeviceChange.Reconnected && change != InputDeviceChange.Disconnected)
                return;

            m_RebindPending = true;
        }

        /// <summary>
        /// Disabled and re-enabled rather than left to re-resolve itself: an enabled action is
        /// supposed to pick up a new device on its own, and a debug tool is the wrong place to
        /// depend on that. Happens perhaps twice a session.
        /// </summary>
        void RebindNow()
        {
            m_RebindPending = false;
            Rebind(ref m_Move);
            Rebind(ref m_Lift);
            Report();
        }

        static void Rebind(ref InputAction action)
        {
            if (action == null) return;
            action.Disable();
            action.Enable();
        }

        /// <summary>
        /// Switch XRI's turn providers off while this is driving, and back on afterwards.
        ///
        /// The rig binds snap turn to the right thumbstick, so pushing it to move also snapped
        /// the view a few degrees at a time -- which makes steering nearly impossible, because
        /// every correction turns the direction you are correcting in. Suspended rather than
        /// deleted: turning is a real part of the piece and this component is a debug aid that
        /// gets switched off again.
        ///
        /// Found by type NAME rather than by referencing XRI's types. The locomotion namespaces
        /// moved between XRI 2 and 3, and a debug tool should not be the thing that stops the
        /// project compiling after a package upgrade.
        /// </summary>
        void SuspendTurnProviders(bool suspend)
        {
            if (suspend) m_Suspended.Clear();

            if (suspend)
            {
                foreach (var behaviour in FindObjectsByType<MonoBehaviour>(
                             FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (behaviour == null || !behaviour.enabled) continue;
                    string type = behaviour.GetType().Name;
                    if (!type.EndsWith("TurnProvider")) continue;

                    behaviour.enabled = false;
                    m_Suspended.Add(behaviour);
                }

                if (m_Suspended.Count > 0)
                    Debug.Log($"[DebugMove] suspended {m_Suspended.Count} turn provider(s) so the " +
                              "stick only moves: " +
                              string.Join(", ", m_Suspended.ConvertAll(b => b.GetType().Name)));
                return;
            }

            foreach (MonoBehaviour behaviour in m_Suspended)
            {
                if (behaviour != null) behaviour.enabled = true;
            }
            if (m_Suspended.Count > 0) Debug.Log("[DebugMove] turn providers restored.");
            m_Suspended.Clear();
        }

        static void Dispose(ref InputAction action)
        {
            if (action == null) return;
            action.Disable();
            action.Dispose();
            action = null;
        }

        /// <summary>
        /// Say what the bindings resolved to. Zero controls means the device is not there or is
        /// on a layout none of the paths match -- which is the answer, not a mystery.
        /// </summary>
        void Report()
        {
            var sb = new StringBuilder();
            foreach (InputControl control in m_Move.controls)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(control.device.name).Append('/').Append(control.name);
            }

            if (m_Move.controls.Count == 0)
            {
                Debug.LogWarning("[DebugMove] no controls resolved. No right XR controller, no " +
                                 "gamepad and no keyboard were found. WASD will start working as " +
                                 "soon as a keyboard is detected; a controller has to be tracking.");
            }
            else
            {
                Debug.Log($"[DebugMove] bound to: {sb}");
            }
        }

        /// <summary>
        /// The right controller's stick straight from the XR subsystem, bypassing the Input
        /// System's layouts and binding paths entirely. The device handle is a struct that goes
        /// invalid when the controller sleeps, so it is re-fetched rather than cached forever.
        /// </summary>
        bool TryReadXrStick(out Vector2 value)
        {
            value = Vector2.zero;

            if (!m_XrRight.isValid)
                m_XrRight = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (!m_XrRight.isValid) return false;

            if (!m_XrRight.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out value))
                return false;

            if (!m_ReportedXrFallback && value.sqrMagnitude > 1e-6f)
            {
                m_ReportedXrFallback = true;
                Debug.Log($"[DebugMove] reading '{m_XrRight.name}' directly through UnityEngine.XR; " +
                          "the Input System binding did not resolve this controller.");
            }
            return true;
        }

        void Update()
        {
            if (m_RebindPending) RebindNow();

            if (!Resolve()) { statusLine = "stick: no XROrigin"; return; }

            if (m_Recentre != null && m_Recentre.WasPressedThisFrame() && m_HasHome)
            {
                m_Rig.position = m_Home;
                m_Moved = false;
                Debug.Log("[DebugMove] rig recentred; the virtual floor matches the real one again.");
            }

            Vector2 stick = m_Move != null ? m_Move.ReadValue<Vector2>() : Vector2.zero;
            string source = m_Move != null && m_Move.controls.Count > 0 ? "stick" : null;

            // Second, independent route to the same stick. The Input System has to match a
            // binding path against a device layout, and that is one more thing to be wrong:
            // which layout the Quest comes up as, and whether its stick is called thumbstick or
            // primary2DAxis, has moved between package versions. UnityEngine.XR asks the XR
            // subsystem for the right hand's primary 2D axis by feature, with no layout and no
            // path in between, so it works whenever OpenXR reports a controller at all.
            //
            // Used only when the binding gave nothing, so the normal path stays the normal path.
            if (stick.sqrMagnitude < 1e-6f && TryReadXrStick(out Vector2 direct))
            {
                stick = direct;
                source = "stick(xr)";
            }

            // Shown whether or not it is moving, so a stick that is being read but ignored by the
            // deadzone looks different from one that is not being read at all.
            statusLine = source != null
                ? $"{source}  x {stick.x:F2}   y {stick.y:F2}"
                : "stick: nothing bound";

            if (stick.sqrMagnitude < m_Deadzone * m_Deadzone) return;

            // Rescaled past the deadzone so the first movement is slow rather than a jump to
            // deadzone speed.
            float magnitude = Mathf.InverseLerp(m_Deadzone, 1f, stick.magnitude);
            Vector2 dir = stick.normalized * magnitude;

            // Same two routes as the stick, for the same reason.
            float grip = m_Lift != null ? m_Lift.ReadValue<float>() : 0f;
            if (grip <= 0.5f && m_XrRight.isValid &&
                m_XrRight.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float xrGrip))
                grip = xrGrip;

            bool lifting = m_AllowVertical && grip > 0.5f;
            Vector3 delta;

            if (lifting)
            {
                delta = Vector3.up * (dir.y * m_Speed * Time.deltaTime);
            }
            else
            {
                Vector3 forward, right;
                if (m_RelativeToHead && m_Origin != null && m_Origin.Camera != null)
                {
                    Transform head = m_Origin.Camera.transform;
                    // Flattened: looking at the floor and pushing forward should still move you
                    // along it, not into it.
                    forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                    right = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
                    if (forward.sqrMagnitude < 1e-4f) forward = m_Rig.forward;
                }
                else
                {
                    forward = Vector3.ProjectOnPlane(m_Rig.forward, Vector3.up).normalized;
                    right = Vector3.ProjectOnPlane(m_Rig.right, Vector3.up).normalized;
                }

                delta = (forward * dir.y + right * dir.x) * (m_Speed * Time.deltaTime);
            }

            m_Rig.position += delta;

            if (!m_Moved)
            {
                m_Moved = true;
                Debug.Log("[DebugMove] rig moved. The play space outline no longer matches the " +
                          "real room until F4 recentres it.");
            }
        }

        bool Resolve()
        {
            if (m_Rig != null) return true;

            m_Origin = FindFirstObjectByType<XROrigin>();
            m_Rig = m_Origin != null ? m_Origin.transform : null;
            if (m_Rig == null) return false;

            m_Home = m_Rig.position;
            m_HasHome = true;
            return true;
        }
    }
}
