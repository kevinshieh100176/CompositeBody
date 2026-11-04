using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using XRMultiplayer;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Interaction.Diagnostics
{
    /// <summary>
    /// DEVELOPMENT ONLY. A desktop HUD for testing the interaction modules in the editor without a
    /// headset: start / join a session, start the show, fire or reset any interaction, and use
    /// module-specific buttons (<see cref="IDesktopTestActions"/>) -- next to XRI's XR Interaction
    /// Simulator, which drives the rig from mouse and keyboard.
    ///
    /// WHY IMGUI AND F-KEYS. The simulator reads the mouse too: a left click is "select" on the
    /// simulated controller and the right button turns the head, so clicks meant for screen-space
    /// UI (the staff panel's Host / Join) get eaten or double as a grab. IMGUI does not go through
    /// the EventSystem at all, and every network action also has a function key the simulator
    /// does not use. On top of that, while the pointer is over this window -- or while "mouse to
    /// UI" is locked with F1 -- the simulator's own mouse actions are switched off, so the click
    /// only reaches the UI (this window, and the uGUI staff panel too).
    ///
    /// MULTIPLAYER PLAY MODE. Virtual players share PlayerPrefs, so they would present the same
    /// player identity and the joiner would take the host's role. When this runs inside an MPPM
    /// clone (its project lives under Library/VP/) it joins with its own stable identity.
    /// </summary>
    [DisallowMultipleComponent]
    public class DesktopTestHud : MonoBehaviour
    {
        static readonly HashSet<string> k_SimulatorAssets = new()
        {
            "XR Interaction Simulator Controls",
            "XR Interaction Controller Controls",
            "XR Interaction Hand Controls",
        };

        [SerializeField] string m_JoinAddress = "127.0.0.1";
        [SerializeField] ushort m_JoinPort = 7777;
        [SerializeField, Tooltip("Join with an identity of its own instead of the PlayerPrefs one. " +
                                 "Switched on automatically inside a Multiplayer Play Mode clone.")]
        bool m_AlternateIdentity;
        [SerializeField] bool m_Collapsed;
        [SerializeField, Min(260f)] float m_Width = 420f;

        const int k_WindowId = 0x5E57;
        bool m_Dragged;
        GUIStyle m_Wrap;
        float m_Scale = 1f;
        Rect m_Window = new(12f, 12f, 420f, 10f);
        bool m_LockUi;      // F1
        bool m_HoverUi;     // pointer over this window
        bool m_MouseTakenFromSimulator;
        readonly List<InputAction> m_SimulatorMouseActions = new();
        string m_Status = "";
        string m_PortText;
        Vector2 m_Scroll;

        /// <summary>True while the simulator's mouse actions are off (pointer over the HUD, or F1).</summary>
        public bool mouseGivenToUi => m_MouseTakenFromSimulator;

        public bool alternateIdentity { get => m_AlternateIdentity; set => m_AlternateIdentity = value; }

        static bool insideMppmClone =>
            Application.dataPath.Replace('\\', '/').Contains("/Library/VP/");

        string alternateIdentityToken
        {
            get
            {
                string path = Application.dataPath.Replace('\\', '/');
                int vp = path.IndexOf("/Library/VP/", System.StringComparison.Ordinal);
                string clone = vp >= 0 ? path.Substring(vp + 12).Split('/')[0] : "desktop";
                return "desktop-test-" + clone;
            }
        }

        void Awake()
        {
            if (insideMppmClone) m_AlternateIdentity = true;
            m_PortText = m_JoinPort.ToString();
            m_Window.width = m_Width;
        }

        void OnDisable() => GiveMouseToSimulator();

        // ---- keys ------------------------------------------------------------------------------

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f1Key.wasPressedThisFrame) m_LockUi = !m_LockUi;
                if (kb.f5Key.wasPressedThisFrame) Host();
                if (kb.f6Key.wasPressedThisFrame) Join();
                if (kb.f7Key.wasPressedThisFrame) StartShow(false);
                if (kb.f8Key.wasPressedThisFrame) StartShow(true);
                if (kb.f9Key.wasPressedThisFrame) Leave();
                if (kb.f10Key.wasPressedThisFrame) m_Collapsed = !m_Collapsed;
            }

            bool wantUi = m_LockUi || m_HoverUi;
            if (wantUi && !m_MouseTakenFromSimulator) TakeMouseFromSimulator();
            else if (!wantUi && m_MouseTakenFromSimulator) GiveMouseToSimulator();

            // Module sections, re-found once a second rather than per OnGUI call (several a frame).
            if (Time.unscaledTime >= m_NextExtrasScan)
            {
                m_NextExtrasScan = Time.unscaledTime + 1f;
                m_Extras.Clear();
                foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
                    if (mb is IDesktopTestActions a) m_Extras.Add(a);
            }
        }

        readonly List<IDesktopTestActions> m_Extras = new();
        float m_NextExtrasScan;

        // ---- network ---------------------------------------------------------------------------

        /// <summary>Host on this machine (the same path the staff panel's Host button takes).</summary>
        public bool Host()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return Fail("no NetworkManager in the scene");
            if (nm.IsListening) return Fail("already in a session");
            if (XRINetworkGameManager.Instance == null) return Fail("no XRINetworkGameManager in the scene");
            bool ok = XRINetworkGameManager.Instance.HostLocalConnection();
            return ok ? Ok($"hosting on port {Port(nm)}") : Fail("StartHost failed -- see the console (port in use?)");
        }

        /// <summary>Join <see cref="m_JoinAddress"/>:<see cref="m_JoinPort"/>.</summary>
        public bool Join()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return Fail("no NetworkManager in the scene");
            if (nm.IsListening) return Fail("already in a session");
            if (!(nm.NetworkConfig.NetworkTransport is UnityTransport transport)) return Fail("no UnityTransport");
            if (XRINetworkGameManager.Instance == null) return Fail("no XRINetworkGameManager in the scene");
            if (ushort.TryParse(m_PortText, out ushort port)) m_JoinPort = port;

            if (m_AlternateIdentity)
                nm.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(alternateIdentityToken);

            transport.SetConnectionData(m_JoinAddress, m_JoinPort);
            bool ok = XRINetworkGameManager.Instance.JoinLocalConnection();
            return ok ? Ok($"joining {m_JoinAddress}:{m_JoinPort}" + (m_AlternateIdentity ? " (alternate identity)" : ""))
                      : Fail("StartClient failed -- see the console");
        }

        /// <summary>Host only: start the show. Refuses to start solo unless <paramref name="solo"/>.</summary>
        public bool StartShow(bool solo)
        {
            var nm = NetworkManager.Singleton;
            var session = GameSessionManager.Instance;
            if (nm == null || !nm.IsServer || session == null) return Fail("only the host can start");
            if (session.sessionState != SessionState.Lobby) return Fail("already started");
            int present = ConnectedRoles(session);
            if (!solo && present < 2)
                return Fail($"waiting for the second player ({present}/2 have a role) -- F8 starts solo");
            if (solo) session.allowSoloFromPanel = true;
            bool ok = session.StartExperience();
            return ok ? Ok(solo && present < 2 ? "started solo" : "started with both players") : Fail("StartExperience refused");
        }

        public void Leave()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) { Fail("not in a session"); return; }
            nm.Shutdown();
            Ok("left the session");
        }

        static int ConnectedRoles(GameSessionManager session)
        {
            int n = 0;
            foreach (var e in session.playerRoles)
                if (e.isConnected && e.role != PlayerRole.Unassigned) n++;
            return n;
        }

        static string Port(NetworkManager nm) =>
            nm.NetworkConfig.NetworkTransport is UnityTransport t ? t.ConnectionData.Port.ToString() : "?";

        bool Ok(string s) { m_Status = s; Debug.Log($"[DesktopTestHud] {s}"); return true; }
        bool Fail(string s) { m_Status = "! " + s; Debug.LogWarning($"[DesktopTestHud] {s}"); return false; }

        // ---- the simulator and the mouse ------------------------------------------------------

        void TakeMouseFromSimulator()
        {
            m_SimulatorMouseActions.Clear();
            foreach (InputAction action in InputSystem.ListEnabledActions())
            {
                InputActionAsset asset = action.actionMap?.asset;
                if (asset == null || !k_SimulatorAssets.Contains(asset.name)) continue;
                if (!UsesPointer(action)) continue;
                action.Disable();
                m_SimulatorMouseActions.Add(action);
            }
            m_MouseTakenFromSimulator = true;
        }

        void GiveMouseToSimulator()
        {
            foreach (InputAction action in m_SimulatorMouseActions)
                if (action != null) action.Enable();
            m_SimulatorMouseActions.Clear();
            m_MouseTakenFromSimulator = false;
        }

        static bool UsesPointer(InputAction action)
        {
            foreach (InputBinding b in action.bindings)
            {
                string p = b.effectivePath;
                if (string.IsNullOrEmpty(p)) continue;
                if (p.StartsWith("<Mouse>") || p.StartsWith("<Pointer>")) return true;
            }
            return false;
        }

        // ---- drawing ---------------------------------------------------------------------------

        void OnGUI()
        {
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            m_Scale = scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            m_Wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true };

            float width = m_Collapsed ? 240f : m_Width;
            m_Window.width = width;

            // Anchored top-right until dragged: the staff panel and the debug panel own the left
            // edge, and a HUD drawn over them makes both unreadable. Re-anchored every frame so a
            // Game view resize keeps it on screen.
            if (!m_Dragged)
            {
                m_Window.x = Screen.width / scale - width - 12f;
                m_Window.y = 12f;
            }

            Vector2 before = m_Window.position;
            m_Window = GUILayout.Window(k_WindowId, m_Window, DrawWindow,
                                        m_Collapsed ? "Desktop Test (F10)" : "Desktop Test  --  F10 collapse",
                                        GUILayout.Width(width));
            if (Event.current.type == EventType.MouseDrag && (m_Window.position - before).sqrMagnitude > 0.01f)
                m_Dragged = true;

            Vector2 mouse = Event.current.mousePosition; // already in the scaled GUI space
            m_HoverUi = m_Window.Contains(mouse);
        }

        void DrawWindow(int id)
        {
            // Module sections draw plain GUILayout labels; wrap them too, for this window only.
            bool wrapWas = GUI.skin.label.wordWrap;
            GUI.skin.label.wordWrap = true;
            try { DrawWindowContents(); }
            finally { GUI.skin.label.wordWrap = wrapWas; }
        }

        void DrawWindowContents()
        {
            if (m_Collapsed)
            {
                GUILayout.Label(NetworkLine(), m_Wrap);
                GUILayout.Label(m_MouseTakenFromSimulator ? "mouse -> UI" : "mouse -> simulator");
                GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
                return;
            }

            m_LockUi = GUILayout.Toggle(m_LockUi, " F1  Mouse to UI (lock)");
            GUILayout.Label(m_MouseTakenFromSimulator
                ? "Mouse: UI -- the simulator ignores it"
                : "Mouse: simulator (hover this window, or F1, to click UI)", m_Wrap);

            Section("Session");
            GUILayout.Label(NetworkLine(), m_Wrap);
            GUILayout.Label(SessionLine(), m_Wrap);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host (F5)")) Host();
            if (GUILayout.Button("Join (F6)")) Join();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Join", GUILayout.Width(34f));
            m_JoinAddress = GUILayout.TextField(m_JoinAddress, GUILayout.MinWidth(120f));
            m_PortText = GUILayout.TextField(m_PortText, GUILayout.Width(60f));
            GUILayout.EndHorizontal();
            m_AlternateIdentity = GUILayout.Toggle(m_AlternateIdentity,
                insideMppmClone ? " Own identity (MPPM clone)" : " Own identity (2nd editor)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start (F7)")) StartShow(false);
            if (GUILayout.Button("Solo (F8)")) StartShow(true);
            if (GUILayout.Button("Leave (F9)")) Leave();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(m_Status)) GUILayout.Label(m_Status, m_Wrap);

            // A fixed height: a GUILayout scroll view inside an auto-sized window otherwise
            // collapses to its scrollbar and hides everything in it.
            float scrollHeight = Mathf.Clamp(Screen.height / m_Scale * 0.42f, 160f, 520f);
            m_Scroll = GUILayout.BeginScrollView(m_Scroll, GUILayout.Height(scrollHeight));

            foreach (IDesktopTestActions actions in m_Extras)
            {
                if (actions is Object o && o == null) continue;
                Section(actions.desktopTestTitle);
                actions.OnDesktopTestGUI();
            }

            Section("Interactions (debug rows)");
            foreach (IDebugInteraction row in InteractionDebug.entries)
            {
                if (row == null) continue;
                DebugInteractionState state;
                try { state = row.DebugState(); } catch { continue; }
                GUILayout.Label($"{row.debugLabel}\n  {state.detail}", m_Wrap);
                GUILayout.BeginHorizontal();
                if (row.DebugCanFire(DebugFireTarget.Local) && GUILayout.Button(row.debugFireLabel)) row.DebugFire(DebugFireTarget.Local);
                if (row.DebugCanFire(DebugFireTarget.Both) && GUILayout.Button("Both")) row.DebugFire(DebugFireTarget.Both);
                if (row.DebugCanReset() && GUILayout.Button("Reset")) row.DebugReset();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            Section("Keys");
            GUILayout.Label(
                "Simulator: hold RMB + move = look, WASD = move, LMB = select / grab (point and click), " +
                "G grip, T trigger, Tab = FPS / device mode, [ and ] = left / right device, R = reset. " +
                "HUD: F1 mouse to UI, F5 host, F6 join, F7 start, F8 solo, F9 leave, F10 collapse, " +
                "F2 = desktop grab probe (nearest grabbable in view).", m_Wrap);

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        static void Section(string title)
        {
            GUILayout.Space(4f);
            GUILayout.Label($"-- {title} --");
        }

        static string NetworkLine()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return "Network: no NetworkManager";
            if (!nm.IsListening) return "Network: offline";
            if (nm.IsHost) return $"Network: HOST on {Port(nm)}, clients {nm.ConnectedClientsIds.Count}";
            return nm.IsConnectedClient ? $"Network: client #{nm.LocalClientId}" : "Network: connecting...";
        }

        static string SessionLine()
        {
            var session = GameSessionManager.Instance;
            if (session == null || !session.IsSpawned) return "Session: --";
            var sb = new StringBuilder($"Session: {session.sessionState}{(session.solo ? " (solo)" : "")}  roles:");
            foreach (var e in session.playerRoles)
                sb.Append($" {e.role}=#{e.clientId}{(e.isConnected ? "" : "(gone)")}");
            if (session.TryGetLocalRole(out PlayerRole me)) sb.Append($"  me={me}");
            return sb.ToString();
        }
    }
}
