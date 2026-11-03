using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using XRMultiplayer;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Desktop-only (mouse/keyboard) control surface for PCVR staff: host or join the LAN
    /// session, see connection/role status, and press Start once both players are in. Built
    /// procedurally so it needs zero manual scene/prefab wiring -- drop this component on any
    /// scene GameObject and it builds its own Screen Space Overlay canvas at runtime, which only
    /// renders to the desktop mirror window, never into the headset.
    ///
    /// THREE ZONES, AND ONE RULE: NOTHING ABOVE THE START BUTTON MAY CHANGE HEIGHT. The show
    /// card holds only what staff touch during a run -- host/join, then Start -- and every line
    /// in it reserves its space whether or not it has anything to say. Under it is the
    /// diagnostics dump, in a drawer of fixed height. The operator cards (beat, calibration,
    /// TouchDesigner) sit in a second column beside them. Staff reach for Start under pressure,
    /// and a button that drifts down the window as a log grows is a button that gets missed.
    ///
    /// WHICH IS WHY EVERY LAYOUT GROUP HERE SETS childControlHeight. It used to be off, while
    /// <see cref="CreateText"/> gave each label a one-line rect and let it overflow: the
    /// diagnostics block -- six to fifteen lines, and growing with the scene event log -- was
    /// handed one line of layout space and drew the rest straight over the Start button below
    /// it. Text wraps horizontally now for the same reason, instead of running off the column.
    /// </summary>
    [RequireComponent(typeof(LanSessionBroadcaster), typeof(LanSessionDiscovery))]
    public class StaffControlPanel : MonoBehaviour
    {
        const int k_GamePort = 7777;
        const string k_DefaultSessionName = "CompositeBody Session";

        // The canvas scales against 1280x800 and the player window is 2048x1280 -- an exact
        // 1.6x, so these are reference pixels that land on whole device pixels.
        const float k_ColumnWidth = 340f;
        const float k_DrawerHeight = 200f;

        static readonly Color k_CardBg = new(0.08f, 0.08f, 0.09f, 0.88f);
        static readonly Color k_DrawerBg = new(0.04f, 0.05f, 0.04f, 0.92f);
        static readonly Color k_ButtonBg = new(0.20f, 0.20f, 0.22f, 0.95f);
        static readonly Color k_HostBg = new(0.14f, 0.32f, 0.52f, 0.98f);
        static readonly Color k_StartBg = new(0.14f, 0.40f, 0.28f, 0.98f);
        static readonly Color k_Heading = new(0.72f, 0.74f, 0.80f);
        static readonly Color k_Muted = new(0.62f, 0.64f, 0.70f);
        static readonly Color k_Warn = new(0.98f, 0.78f, 0.42f);
        static readonly Color k_Diag = new(0.55f, 0.72f, 0.58f);
        static readonly Color k_Info = new(0.62f, 0.84f, 0.98f);
        static readonly Color k_Error = new(0.98f, 0.70f, 0.55f);

        [SerializeField, Tooltip("Collapses and reopens the diagnostics drawer. It starts open; " +
                                 "close it before anyone looks at the mirror window. F9 because " +
                                 "F2 is the scrubber, F3 the interaction list and F4 the " +
                                 "calibration panel.")]
        Key m_DiagnosticsKey = Key.F9;

        LanSessionBroadcaster m_Broadcaster;
        LanSessionDiscovery m_Discovery;

        GameObject m_PreConnectPanel;
        GameObject m_ConnectedPanel;
        Text m_StatusText;
        Text m_RoleStatusText;
        Text m_BlockedReasonText;
        Button m_SoloButton;
        Text m_MessageText;
        Text m_DiagText;
        Button m_StartButton;
        Transform m_SessionListParent;
        InputField m_IpInput;
        GameObject m_BeatPanel;
        Text m_BeatText;
        GameObject m_CalibrationPanel;
        Text m_CalibrationText;
        HandPinchCalibrator m_PinchCalibrator;
        GameObject m_TouchDesignerPanel;
        InputField m_TdHostInput;
        Text m_TdHostText;
        TouchDesignerSender m_TdSender;
        GameObject m_DrawerBody;
        Text m_DrawerLabel;
        bool m_DrawerOpen = true;

        readonly List<GameObject> m_SessionRows = new();

        void Awake()
        {
            m_Broadcaster = GetComponent<LanSessionBroadcaster>();
            m_Discovery = GetComponent<LanSessionDiscovery>();
            BuildUI();
        }

        void Start()
        {
            EnsureEventSystem();

            m_Discovery.onSessionsUpdated += HandleSessionsUpdated;
            m_Discovery.StartListening();

            // Resolved here and not in Awake: the sender opens its socket in its own OnEnable, so
            // asking it where it is pointed is only meaningful once every Awake/OnEnable has run.
            m_TdSender = FindAnyObjectByType<TouchDesignerSender>();
            if (m_TouchDesignerPanel != null)
                m_TouchDesignerPanel.SetActive(m_TdSender != null);

            if (m_TdSender != null)
            {
                m_TdHostInput.text = m_TdSender.activeHost ?? string.Empty;
                ShowTdStatus();
            }
        }

        void Update()
        {
            HandleDrawerKey();

            bool listening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

            m_PreConnectPanel.SetActive(!listening);
            m_ConnectedPanel.SetActive(listening);

            if (listening) RefreshConnectedPanel();

            // Outside the listening check on purpose: the drawer is most wanted before a
            // connection exists, when what staff need to know is why hosting or joining failed.
            RefreshDiagnostics();
        }

        void OnDestroy()
        {
            if (m_Discovery != null) m_Discovery.onSessionsUpdated -= HandleSessionsUpdated;
        }

        void RefreshConnectedPanel()
        {
            bool isServer = NetworkManager.Singleton.IsServer;
            var session = GameSessionManager.Instance;

            if (session == null)
            {
                m_StatusText.text = "Connected.\nWaiting on the session manager...";
                m_RoleStatusText.text = "";
                m_BlockedReasonText.text = "";
                m_StartButton.gameObject.SetActive(false);
                m_SoloButton.gameObject.SetActive(false);
                return;
            }

            // Two lines in both branches, because the status sits above the Start button and a
            // line that comes and goes would move the button with it.
            m_StatusText.text = isServer
                ? $"Hosting {XRINetworkGameManager.Instance.GetLocalIPAddress()}:{k_GamePort}\n" +
                  $"{session.connectedPlayerCount}/{GameSessionManager.MaxPlayers} players  ·  {session.sessionState}"
                : $"Connected as client\n{session.sessionState}";

            m_RoleStatusText.text = session.TryGetLocalRole(out PlayerRole localRole)
                ? $"Local role: {localRole}"
                : "Local role: not assigned yet";

            m_StartButton.gameObject.SetActive(isServer);
            m_SoloButton.gameObject.SetActive(isServer);

            if (isServer)
            {
                // StartExperience is the authority on whether a solo run is allowed, so the
                // button asks it rather than re-deciding here -- two places deciding the same
                // thing is how a button comes to be greyed out for a start that would succeed.
                bool canStart = session.sessionState == SessionState.Lobby && session.CanStart();
                m_StartButton.interactable = canStart;
                m_BlockedReasonText.text = canStart ? "" : BlockedReason(session);
                RefreshSoloButton(session);
            }
            else
            {
                m_BlockedReasonText.text = "";
            }

            RefreshBeatPanel(isServer);
            RefreshCalibrationPanel();
        }

        /// <summary>
        /// WHY START IS OFF, FOR THE SLOT KEPT EMPTY UNDER THE BUTTON. A disabled button refuses
        /// in silence: the click does not reach OnStartClicked, so even its "waiting on players"
        /// line never runs, and the panel looks broken rather than strict. The usual cause is
        /// simply that a BUILD will not start a one-person show unless it has been told to --
        /// correct behaviour for the venue, and impossible to guess from a greyed-out button.
        /// So the reason names the switch that fixes it, which sits directly underneath.
        /// </summary>
        static string BlockedReason(GameSessionManager session)
        {
            if (session.sessionState != SessionState.Lobby)
                return $"Start is off: the session is {session.sessionState}, not Lobby.";

            if (session.connectedPlayerCount < 1)
                return "Start is off: no player has spawned yet.";

            return $"Start is off: {session.connectedPlayerCount} of {GameSessionManager.MaxPlayers} " +
                   "players. Turn Solo on below to run it single-handed.";
        }

        /// <summary>
        /// Flips the host's willingness to start one-handed. Host-local: soloAllowed is only
        /// ever read on the machine that starts the show, and what the clients need -- whether
        /// their gates relax -- is replicated by StartExperience at the moment it starts.
        /// </summary>
        void OnSoloClicked()
        {
            var session = GameSessionManager.Instance;
            if (session == null) return;
            session.allowSoloFromPanel = !session.allowSoloFromPanel;
        }

        /// <summary>
        /// The switch reports the effective answer rather than its own flag. In the editor,
        /// with -solo on the command line, or with the inspector flag set, solo is already on
        /// from somewhere else and this switch cannot turn it off -- so it says where it came
        /// from and goes dead, instead of reading "off" beside a Start that accepts one player.
        /// </summary>
        void RefreshSoloButton(GameSessionManager session)
        {
            bool forcedElsewhere = session.soloAllowed && !session.allowSoloFromPanel;
            m_SoloButton.interactable = !forcedElsewhere && session.sessionState == SessionState.Lobby;

            Text label = ButtonLabel(m_SoloButton);
            label.text = forcedElsewhere
                ? "Solo: on (editor or -solo)"
                : session.allowSoloFromPanel
                    ? "Solo: ON -- one player will start"
                    : "Solo: off";

            // Amber whenever a one-person start is possible, however it was switched on: this
            // is a debug state, and the venue should be able to see that it is in one.
            label.color = session.soloAllowed ? k_Warn : Color.white;
        }

        /// <summary>
        /// Only while the drawer is open, because <see cref="GameSessionManager.BuildDiagnostics"/>
        /// walks the role list and the scene event log into a fresh string, and this runs every
        /// frame. Closed, the work is skipped rather than done and thrown away.
        /// </summary>
        void RefreshDiagnostics()
        {
            if (!m_DrawerOpen || m_DiagText == null) return;

            var session = GameSessionManager.Instance;
            m_DiagText.text = session != null
                ? session.BuildDiagnostics()
                : "No GameSessionManager in the scene yet.";
        }

        void HandleDrawerKey()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || m_DiagnosticsKey == Key.None) return;
            if (!keyboard[m_DiagnosticsKey].wasPressedThisFrame) return;
            if (TypingIntoAField()) return;

            SetDrawerOpen(!m_DrawerOpen);
        }

        /// <summary>
        /// A field with the caret in it eats the key: this panel takes a TouchDesigner host and
        /// an IP address by hand, and an F4 typed into one of those is still a keystroke.
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

        void SetDrawerOpen(bool open)
        {
            m_DrawerOpen = open;
            if (m_DrawerBody != null) m_DrawerBody.SetActive(open);
            if (m_DrawerLabel != null)
                m_DrawerLabel.text = open ? "[-]  Diagnostics" : "[+]  Diagnostics";
        }

        /// <summary>
        /// A line that is visible in both states, which is the whole point of it. The host and
        /// join failures used to be written to the connected panel's status text -- a panel that
        /// is inactive precisely when hosting has just failed, so the message went nowhere.
        /// </summary>
        void Notify(string message, bool isError = false)
        {
            if (m_MessageText == null) return;
            m_MessageText.color = isError ? k_Error : k_Info;
            m_MessageText.text = message;
        }

        /// <summary>
        /// Calibration is per-headset and entirely local, so this panel reports and arms only
        /// the machine it is running on. In the venue each PC has its own mirror window, so
        /// whoever is standing with a player arms that player's headset.
        /// </summary>
        void RefreshCalibrationPanel()
        {
            if (m_PinchCalibrator == null) m_PinchCalibrator = FindAnyObjectByType<HandPinchCalibrator>();

            // A disabled calibrator counts as absent. Arm() only sets a flag that the component's
            // own Update acts on, so a switched-off one would give staff a button that reports
            // "armed" and then never calibrates anything.
            bool available = m_PinchCalibrator != null && m_PinchCalibrator.enabled;
            m_CalibrationPanel.SetActive(available);
            if (!available) return;

            string state = m_PinchCalibrator.hasCalibrated ? "calibrated" : "NOT calibrated";
            string armed = m_PinchCalibrator.isArmed ? "armed" : "not armed";

            float progress = m_PinchCalibrator.holdProgress;
            string holding = progress > 0f ? $"  holding {progress * 100f:0}%" : string.Empty;

            m_CalibrationText.text = $"This headset: {state} ({armed}){holding}\n" +
                                     "Pinch right index+thumb on the marker and hold.";
        }

        void OnArmCalibrationClicked()
        {
            if (m_PinchCalibrator != null) m_PinchCalibrator.Arm();
        }

        /// <summary>
        /// The beat controls only exist on the host, and only once the experience scene has
        /// loaded -- the director lives in that scene, so there is nothing to drive until staff
        /// have pressed Start.
        /// </summary>
        void RefreshBeatPanel(bool isServer)
        {
            var director = ExperienceDirector.Instance;
            bool available = isServer && director != null;

            m_BeatPanel.SetActive(available);
            if (!available) return;

            BeatDefinition def = director.DefinitionFor(director.currentBeat);

            string endsOn;
            if (!string.IsNullOrEmpty(def.gateTaskId))
                endsOn = $"both players report '{def.gateTaskId}'";
            else if (def.autoAdvanceSeconds > 0f)
                endsOn = $"timer {director.timeInBeat:0}s / {def.autoAdvanceSeconds:0}s";
            else
                endsOn = "staff only";

            m_BeatText.text = $"{director.beatIndex + 1}/{director.beatCount}  {StoryBeats.DisplayName(director.currentBeat)}\n" +
                              $"ends on: {endsOn}";
        }

        void OnBeatPrevClicked() => ExperienceDirector.Instance?.GoBack();
        void OnBeatNextClicked() => ExperienceDirector.Instance?.Advance();
        void OnBeatRestartClicked() => ExperienceDirector.Instance?.Restart();
        void OnBeatJumpClicked(StoryBeat beat) => ExperienceDirector.Instance?.JumpTo(beat);

        void HandleSessionsUpdated(IReadOnlyList<LanSessionInfo> sessions)
        {
            foreach (var row in m_SessionRows) Destroy(row);
            m_SessionRows.Clear();

            foreach (var session in sessions)
            {
                if (session.state != SessionState.Lobby) continue; // don't offer joins mid-experience
                m_SessionRows.Add(CreateSessionRow(session));
            }
        }

        void OnHostClicked()
        {
            if (XRINetworkGameManager.Instance == null)
            {
                Notify("No XRINetworkGameManager found in the scene.", true);
                return;
            }

            Utils.Log($"[StaffPanel] HOST clicked. localIP={XRINetworkGameManager.Instance.GetLocalIPAddress()} port={k_GamePort}");

            if (XRINetworkGameManager.Instance.HostLocalConnection())
            {
                m_Discovery.StopListening();
                m_Broadcaster.StartBroadcasting(k_DefaultSessionName, k_GamePort);
                Notify("Hosting. Broadcasting this session on the LAN.");
                Utils.Log("[StaffPanel] StartHost returned true; broadcasting session.");
            }
            else
            {
                Notify("Failed to host -- see the log.", true);
                Utils.LogError("[StaffPanel] StartHost returned FALSE.");
            }
        }

        void OnJoinClicked(LanSessionInfo session)
        {
            var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as Unity.Netcode.Transports.UTP.UnityTransport;
            if (transport == null)
            {
                Notify("No UnityTransport configured.", true);
                return;
            }

            transport.SetConnectionData(session.hostAddress, (ushort)session.port);
            Utils.Log($"[StaffPanel] JOIN clicked -> {session.hostAddress}:{session.port} " +
                      $"(payload {NetworkManager.Singleton.NetworkConfig.ConnectionData?.Length ?? 0} bytes, " +
                      $"approval={NetworkManager.Singleton.NetworkConfig.ConnectionApproval})");

            if (XRINetworkGameManager.Instance != null && XRINetworkGameManager.Instance.JoinLocalConnection())
            {
                m_Discovery.StopListening();
                Notify($"Joining {session.hostAddress}:{session.port}...");
                Utils.Log("[StaffPanel] StartClient returned true; awaiting approval + scene sync.");
                StartCoroutine(ReportConnectionOutcome());
            }
            else
            {
                Notify($"Failed to join {session.sessionName}.", true);
                Utils.LogError("[StaffPanel] StartClient returned FALSE.");
            }
        }

        /// <summary>
        /// Logs the connection outcome a few seconds after StartClient, so a failure that
        /// happens silently after the click still leaves a record in the player log -- and now
        /// also says so on the panel, where whoever clicked is still looking.
        /// </summary>
        System.Collections.IEnumerator ReportConnectionOutcome()
        {
            for (int second = 1; second <= 12; second++)
            {
                yield return new WaitForSeconds(1f);

                var nm = NetworkManager.Singleton;
                if (nm == null)
                {
                    Notify("NetworkManager disappeared while connecting.", true);
                    Utils.LogError("[StaffPanel] NetworkManager disappeared while connecting.");
                    yield break;
                }

                if (nm.IsConnectedClient)
                {
                    Notify($"Connected after ~{second}s.");
                    Utils.Log($"[StaffPanel] CONNECTED after ~{second}s. localId={nm.LocalClientId}");
                    yield break;
                }

                if (!nm.IsListening)
                {
                    Notify($"Connection dropped after ~{second}s: '{nm.DisconnectReason}'.", true);
                    Utils.LogError($"[StaffPanel] Connection dropped after ~{second}s " +
                                   $"(disconnect reason: '{nm.DisconnectReason}').");
                    yield break;
                }

                Utils.Log($"[StaffPanel] t+{second}s still connecting: listening={nm.IsListening} " +
                          $"connected={nm.IsConnectedClient} localId={nm.LocalClientId}");
            }

            Notify("Still not connected after 12s -- approval or scene sync never completed.", true);
            Utils.LogError("[StaffPanel] Still not connected after 12s -- approval or scene sync never completed.");
        }

        void OnJoinByIpClicked()
        {
            string ip = m_IpInput != null ? m_IpInput.text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(ip))
            {
                Notify("Enter the host PC's IP address first.", true);
                return;
            }

            OnJoinClicked(new LanSessionInfo
            {
                sessionName = ip,
                hostAddress = ip,
                port = k_GamePort,
                state = SessionState.Lobby
            });
        }

        void OnStartClicked()
        {
            if (GameSessionManager.Instance == null) return;
            if (!GameSessionManager.Instance.StartExperience())
                Notify("Cannot start yet -- waiting on players.", true);
        }

        void OnSetTdHostClicked()
        {
            if (m_TdSender == null)
            {
                m_TdHostText.text = "No TouchDesignerSender in this scene.";
                return;
            }

            bool ok = m_TdSender.SetHost(m_TdHostInput.text, out string message);

            if (ok)
            {
                // Read back from the sender rather than echoing what was typed, so a trimmed
                // address shows as the thing actually in use.
                m_TdHostInput.text = m_TdSender.activeHost;
            }

            m_TdHostText.color = ok ? k_Info : k_Error;
            m_TdHostText.text = message;
        }

        /// <summary>Where the stream is pointed before anyone has typed anything.</summary>
        void ShowTdStatus()
        {
            // activeHost stays null until the sender has opened a socket once, which it does in
            // its own OnEnable -- so null here means the component is sitting in the scene
            // disabled, and claiming an address would be worse than saying nothing is going out.
            m_TdHostText.text = string.IsNullOrEmpty(m_TdSender.activeHost)
                ? "Sender is disabled -- nothing is being sent."
                : $"Sending to {m_TdSender.activeHost}:{m_TdSender.port}.";
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem (Staff Panel Fallback)");
            go.AddComponent<EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        #region UI construction

        void BuildUI()
        {
            var canvasGO = gameObject;
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);

            // Matched on width, said out loud: the player window is 2048 wide, so the whole
            // panel draws at exactly 1.6x and a 14pt line lands near 22 real pixels.
            scaler.matchWidthOrHeight = 0f;

            canvasGO.AddComponent<GraphicRaycaster>();

            // TWO COLUMNS, BECAUSE ONE DOES NOT FIT. Matching on width means the reference
            // height stays 800 whatever the window size, and the show card, three operator
            // cards and the drawer come to a little over a thousand between them. Stacked in one
            // column the bottom cards hang off the window, where no mouse can reach them; side
            // by side each column runs to about five hundred.
            Transform show = CreateColumn(canvasGO.transform, "ShowColumn", 16f);
            Transform operators = CreateColumn(canvasGO.transform, "OperatorColumn", 16f + k_ColumnWidth + 12f);

            CreateText(show, "Staff Control Panel", 20, FontStyle.Bold);
            BuildShowCard(show);
            BuildDiagnosticsDrawer(show);

            BuildBeatPanel(operators);
            BuildCalibrationPanel(operators);
            BuildTouchDesignerPanel(operators);

            m_PreConnectPanel.SetActive(true);
            m_ConnectedPanel.SetActive(false);
            SetDrawerOpen(m_DrawerOpen);
        }

        /// <summary>
        /// The only card staff touch while the piece is running. Every line in it reserves its
        /// height, so Start sits at the same pixel in every state the session can be in.
        /// </summary>
        void BuildShowCard(Transform parent)
        {
            var card = CreateCard(parent, "ShowCard");

            m_PreConnectPanel = CreatePanel(card.transform, "PreConnectPanel", new Vector2(k_ColumnWidth, 0));
            Stack(m_PreConnectPanel, 0, 6);

            CreateText(m_PreConnectPanel.transform, "Connect", 16, FontStyle.Bold, k_Heading);
            CreateButton(m_PreConnectPanel.transform, "Host Session", OnHostClicked,
                         new Vector2(0, 44), k_HostBg, 16);

            // Manual fallback: UDP broadcast is commonly blocked by firewalls, VPN adapters and
            // managed switches, and without this the panel would offer no way to connect at all.
            CreateText(m_PreConnectPanel.transform, "Or join by host IP:", 13, FontStyle.Italic, k_Muted);
            m_IpInput = CreateInputField(m_PreConnectPanel.transform, "192.168.1.10");
            CreateButton(m_PreConnectPanel.transform, "Join by IP", OnJoinByIpClicked);

            CreateText(m_PreConnectPanel.transform, "Nearby sessions:", 13, FontStyle.Italic, k_Muted);

            var listGO = CreatePanel(m_PreConnectPanel.transform, "SessionList", new Vector2(k_ColumnWidth, 0));
            Stack(listGO, 0, 4);
            m_SessionListParent = listGO.transform;

            m_ConnectedPanel = CreatePanel(card.transform, "ConnectedPanel", new Vector2(k_ColumnWidth, 0));
            Stack(m_ConnectedPanel, 0, 6);

            CreateText(m_ConnectedPanel.transform, "Show", 16, FontStyle.Bold, k_Heading);
            m_StatusText = CreateText(m_ConnectedPanel.transform, "", 14, FontStyle.Normal, Color.white, 2);
            m_RoleStatusText = CreateText(m_ConnectedPanel.transform, "", 13, FontStyle.Normal, k_Muted, 1);

            m_StartButton = CreateButton(m_ConnectedPanel.transform, "Start Experience", OnStartClicked,
                                         new Vector2(0, 48), k_StartBg, 17);

            // Two lines, reserved whether or not there is a reason to show, so the button above
            // does not shift when one appears.
            m_BlockedReasonText = CreateText(m_ConnectedPanel.transform, "", 13, FontStyle.Normal, k_Warn, 2);

            // Directly under the reason, because the commonest reason is the one this fixes.
            // Below Start rather than above it, so adding it moved nothing.
            m_SoloButton = CreateButton(m_ConnectedPanel.transform, "Solo: off", OnSoloClicked,
                                        new Vector2(0, 30));

            // On the card rather than inside either sub-panel: a message about a host that
            // failed has to outlive the panel swap that the failure did not cause.
            m_MessageText = CreateText(card.transform, "", 13, FontStyle.Normal, k_Info, 1);
        }

        void BuildTouchDesignerPanel(Transform parent)
        {
            m_TouchDesignerPanel = CreateCard(parent, "TouchDesignerPanel");

            CreateText(m_TouchDesignerPanel.transform, "TouchDesigner", 16, FontStyle.Bold, k_Heading);
            m_TdHostText = CreateText(m_TouchDesignerPanel.transform, "", 13, FontStyle.Normal, k_Info, 1);

            m_TdHostInput = CreateInputField(m_TouchDesignerPanel.transform, string.Empty, "TdHostInput");
            CreateButton(m_TouchDesignerPanel.transform, "Set TD Host", OnSetTdHostClicked);

            // Hidden until Start() has had a chance to find a sender: a control for something
            // that is not in the scene is worse than no control, because it invites someone to
            // type an address and believe it took effect.
            m_TouchDesignerPanel.SetActive(false);
        }

        void BuildCalibrationPanel(Transform parent)
        {
            m_CalibrationPanel = CreateCard(parent, "CalibrationPanel");

            CreateText(m_CalibrationPanel.transform, "Calibration", 16, FontStyle.Bold, k_Heading);
            m_CalibrationText = CreateText(m_CalibrationPanel.transform, "", 13, FontStyle.Normal, k_Info, 2);

            // Needed because a pinch is only accepted in the lobby by default: once the piece is
            // running, a player whose tracking has drifted can only be realigned if staff let
            // them pinch again.
            CreateButton(m_CalibrationPanel.transform, "Arm Re-calibration", OnArmCalibrationClicked);

            m_CalibrationPanel.SetActive(false);
        }

        /// <summary>
        /// Beat transport for staff. Skip exists because a two-player gate is a hang risk in
        /// front of an audience, and jump exists because rehearsing a late beat otherwise means
        /// replaying the whole piece to reach it.
        /// </summary>
        void BuildBeatPanel(Transform parent)
        {
            m_BeatPanel = CreateCard(parent, "BeatPanel");

            CreateText(m_BeatPanel.transform, "Beat", 16, FontStyle.Bold, k_Heading);
            m_BeatText = CreateText(m_BeatPanel.transform, "", 14, FontStyle.Normal,
                                    new Color(0.95f, 0.85f, 0.55f), 2);

            var transport = CreateRow(m_BeatPanel.transform, "Transport", 30);
            CreateButton(transport, "< Prev", OnBeatPrevClicked, new Vector2(96, 30));
            CreateButton(transport, "Next >", OnBeatNextClicked, new Vector2(96, 30));
            CreateButton(transport, "Restart", OnBeatRestartClicked, new Vector2(96, 30));

            // A grid and not a row: ten beats at a width that fits ten across would leave labels
            // too narrow to read, and the eleventh beat added to the sheet would run off the
            // column instead of starting a second line.
            var jumps = CreatePanel(m_BeatPanel.transform, "Jump", new Vector2(k_ColumnWidth, 0));
            var grid = jumps.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(58, 26);
            grid.spacing = new Vector2(4, 4);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;

            foreach (var beat in StoryBeats.Ordered)
            {
                // Captured per iteration on purpose; one shared variable would make every
                // button jump to the last beat in the list.
                StoryBeat target = beat;
                CreateButton(jumps.transform, StoryBeats.ShortCode(target), () => OnBeatJumpClicked(target),
                             new Vector2(58, 26), null, 12);
            }

            m_BeatPanel.SetActive(false);
        }

        /// <summary>
        /// The diagnostics dump, in a box of fixed height that clips and scrolls. It is the
        /// fixed height plus the mask that make it safe to leave open: the text inside can grow
        /// without limit -- BuildDiagnostics appends the whole scene event log -- and still
        /// cannot reach a pixel belonging to anything above it.
        /// </summary>
        void BuildDiagnosticsDrawer(Transform parent)
        {
            var card = CreateCard(parent, "DiagnosticsDrawer");

            var header = CreateButton(card.transform, "[-]  Diagnostics", () => SetDrawerOpen(!m_DrawerOpen),
                                      new Vector2(0, 28), k_ButtonBg, 14, FontStyle.Bold);
            m_DrawerLabel = ButtonLabel(header);
            m_DrawerLabel.alignment = TextAnchor.MiddleLeft;
            m_DrawerLabel.color = k_Heading;

            m_DrawerBody = CreatePanel(card.transform, "DrawerBody", new Vector2(k_ColumnWidth, k_DrawerHeight));
            m_DrawerBody.AddComponent<Image>().color = k_DrawerBg;

            var bodyLayout = m_DrawerBody.AddComponent<LayoutElement>();
            bodyLayout.minHeight = k_DrawerHeight;
            bodyLayout.preferredHeight = k_DrawerHeight;

            var scroll = m_DrawerBody.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            var viewport = CreatePanel(m_DrawerBody.transform, "Viewport", Vector2.zero);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(8, 8);
            viewportRect.offsetMax = new Vector2(-8, -8);
            viewport.AddComponent<RectMask2D>();

            var content = CreatePanel(viewport.transform, "Content", Vector2.zero);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            Stack(content, 0, 0);

            // Needed here, and only here: the content's parent is a viewport rather than a
            // layout group, so nothing else would give it the height the text asks for.
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            m_DiagText = CreateText(content.transform, "", 12, FontStyle.Normal, k_Diag);

            scroll.viewport = viewportRect;
            scroll.content = contentRect;
        }

        static Transform CreateRow(Transform parent, string name, float height)
        {
            var row = CreatePanel(parent, name, new Vector2(k_ColumnWidth, height));
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var element = row.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            return row.transform;
        }

        GameObject CreateSessionRow(LanSessionInfo session)
        {
            Transform row = CreateRow(m_SessionListParent, $"Session_{session.hostAddress}", 30);

            Text label = CreateText(row, $"{session.sessionName} ({session.currentPlayers}/{session.maxPlayers})",
                                    13, FontStyle.Normal);
            label.GetComponent<LayoutElement>().preferredWidth = 200;

            CreateButton(row, "Join", () => OnJoinClicked(session), new Vector2(84, 28));

            return row.gameObject;
        }

        /// <summary>
        /// One top-anchored stack of cards. Two of these side by side still clear the
        /// interaction list on the right edge, which is 520 reference pixels wide -- the second
        /// column ends at 708 and the list starts at 744.
        /// </summary>
        static Transform CreateColumn(Transform parent, string name, float x)
        {
            var column = CreatePanel(parent, name, new Vector2(k_ColumnWidth, 0));
            Stack(column, 0, 10);

            // The only ContentSizeFitters in the panel, one per column. Everything below them is
            // sized by its parent, and a fitter on a child of a layout group is how a card ends
            // up fighting the group that is already sizing it.
            column.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var rect = column.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -16);
            return column.transform;
        }

        /// <summary>A group with a background of its own, sized by whoever owns it.</summary>
        static GameObject CreateCard(Transform parent, string name)
        {
            var card = CreatePanel(parent, name, new Vector2(k_ColumnWidth, 0));
            card.AddComponent<Image>().color = k_CardBg;
            Stack(card, 12, 6);
            return card;
        }

        /// <summary>
        /// The one layout configuration this panel uses, in one place. childControlHeight is the
        /// line that matters: with it off, a label that wraps to five lines is given one line of
        /// space and draws the other four over its siblings.
        /// </summary>
        static VerticalLayoutGroup Stack(GameObject go, int padding, float spacing)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        static GameObject CreatePanel(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            return go;
        }

        /// <summary>
        /// <paramref name="reserveLines"/> keeps space for text that is not there yet, so a line
        /// appearing cannot push what is under it. Used for the status, the blocked-start reason
        /// and the message line -- everything around the Start button.
        /// </summary>
        static Text CreateText(Transform parent, string content, int fontSize, FontStyle style,
                               Color? color = null, int reserveLines = 0)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color ?? Color.white;
            text.text = content;

            // Wrap, not overflow: the hosting line and the scene event log are both longer than
            // the column, and used to run off the side of it into the middle of the window.
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var element = go.AddComponent<LayoutElement>();
            if (reserveLines > 0) element.minHeight = reserveLines * (fontSize + 5);

            return text;
        }

        static InputField CreateInputField(Transform parent, string placeholder, string name = "IpInput")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(280, 32);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);

            var element = go.AddComponent<LayoutElement>();
            element.minHeight = 32;
            element.preferredHeight = 32;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 14;
            text.color = Color.white;
            text.supportRichText = false;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6, 2);
            textRect.offsetMax = new Vector2(-6, -2);

            var input = go.AddComponent<InputField>();
            input.textComponent = text;
            input.text = placeholder;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        /// <summary>
        /// A zero width in <paramref name="size"/> means "as wide as the card", which is what a
        /// primary action wants; a real width is honoured inside a row or a grid.
        /// </summary>
        static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick,
                                   Vector2? size = null, Color? background = null, int fontSize = 14,
                                   FontStyle style = FontStyle.Normal)
        {
            Vector2 wanted = size ?? new Vector2(0, 34);

            var go = new GameObject($"Button_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = background ?? k_ButtonBg;

            var button = go.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            // Set by hand because Selectable only wires this up in Reset, which is editor-only:
            // a button built at runtime has no target graphic, so interactable = false changed
            // nothing on screen and a refusing button looked exactly like a willing one. The
            // tint multiplies the CanvasRenderer colour, so the background colour above
            // survives and simply dims when the button is disabled.
            button.targetGraphic = image;

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = wanted;

            var element = go.AddComponent<LayoutElement>();
            element.minHeight = wanted.y;
            element.preferredHeight = wanted.y;
            if (wanted.x > 0f) element.preferredWidth = wanted.x;

            var textGO = new GameObject("Label", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;

            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8, 0);
            textRect.offsetMax = new Vector2(-8, 0);

            return button;
        }

        static Text ButtonLabel(Button button) => button.GetComponentInChildren<Text>();

        #endregion
    }
}
