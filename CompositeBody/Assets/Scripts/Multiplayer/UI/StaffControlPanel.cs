using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XRMultiplayer;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Desktop-only (mouse/keyboard) control surface for PCVR staff: host or join the LAN
    /// session, see connection/role status, and press Start once both players are in. Built
    /// procedurally so it needs zero manual scene/prefab wiring -- drop this component on any
    /// scene GameObject and it builds its own Screen Space Overlay canvas at runtime, which only
    /// renders to the desktop mirror window, never into the headset.
    /// </summary>
    [RequireComponent(typeof(LanSessionBroadcaster), typeof(LanSessionDiscovery))]
    public class StaffControlPanel : MonoBehaviour
    {
        const int k_GamePort = 7777;
        const string k_DefaultSessionName = "CompositeBody Session";

        LanSessionBroadcaster m_Broadcaster;
        LanSessionDiscovery m_Discovery;

        GameObject m_PreConnectPanel;
        GameObject m_ConnectedPanel;
        Text m_StatusText;
        Text m_RoleStatusText;
        Text m_DiagText;
        Button m_StartButton;
        Transform m_SessionListParent;
        InputField m_IpInput;
        GameObject m_BeatPanel;
        Text m_BeatText;

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
        }

        void Update()
        {
            bool listening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

            m_PreConnectPanel.SetActive(!listening);
            m_ConnectedPanel.SetActive(listening);

            if (listening) RefreshConnectedPanel();
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
                m_StatusText.text = "Connected. Waiting on session manager...";
                m_RoleStatusText.text = "";
                m_StartButton.gameObject.SetActive(false);
                return;
            }

            m_StatusText.text = isServer
                ? $"Hosting on {XRINetworkGameManager.Instance.GetLocalIPAddress()}:{k_GamePort}\n" +
                  $"{session.connectedPlayerCount}/{GameSessionManager.MaxPlayers} players connected. State: {session.sessionState}"
                : $"Connected as client. State: {session.sessionState}";

            string roleLine = session.TryGetLocalRole(out PlayerRole localRole)
                ? $"Local role: {localRole}"
                : "Local role: not assigned yet";
            m_RoleStatusText.text = roleLine;

            if (m_DiagText != null) m_DiagText.text = session.BuildDiagnostics();

            m_StartButton.gameObject.SetActive(isServer);
            if (isServer)
            {
                bool canStart = session.sessionState == SessionState.Lobby && session.connectedPlayerCount >= GameSessionManager.MaxPlayers;
                m_StartButton.interactable = canStart;
            }

            RefreshBeatPanel(isServer);
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
                m_StatusText.text = "No XRINetworkGameManager found in scene.";
                return;
            }

            Utils.Log($"[StaffPanel] HOST clicked. localIP={XRINetworkGameManager.Instance.GetLocalIPAddress()} port={k_GamePort}");

            if (XRINetworkGameManager.Instance.HostLocalConnection())
            {
                m_Discovery.StopListening();
                m_Broadcaster.StartBroadcasting(k_DefaultSessionName, k_GamePort);
                Utils.Log("[StaffPanel] StartHost returned true; broadcasting session.");
            }
            else
            {
                m_StatusText.text = "Failed to host -- see log.";
                Utils.LogError("[StaffPanel] StartHost returned FALSE.");
            }
        }

        void OnJoinClicked(LanSessionInfo session)
        {
            var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as Unity.Netcode.Transports.UTP.UnityTransport;
            if (transport == null)
            {
                m_StatusText.text = "No UnityTransport configured.";
                return;
            }

            transport.SetConnectionData(session.hostAddress, (ushort)session.port);
            Utils.Log($"[StaffPanel] JOIN clicked -> {session.hostAddress}:{session.port} " +
                      $"(payload {NetworkManager.Singleton.NetworkConfig.ConnectionData?.Length ?? 0} bytes, " +
                      $"approval={NetworkManager.Singleton.NetworkConfig.ConnectionApproval})");

            if (XRINetworkGameManager.Instance != null && XRINetworkGameManager.Instance.JoinLocalConnection())
            {
                m_Discovery.StopListening();
                Utils.Log("[StaffPanel] StartClient returned true; awaiting approval + scene sync.");
                StartCoroutine(ReportConnectionOutcome());
            }
            else
            {
                m_StatusText.text = $"Failed to join {session.sessionName}.";
                Utils.LogError("[StaffPanel] StartClient returned FALSE.");
            }
        }

        /// <summary>
        /// Logs the connection outcome a few seconds after StartClient, so a failure that
        /// happens silently after the click still leaves a record in the player log.
        /// </summary>
        System.Collections.IEnumerator ReportConnectionOutcome()
        {
            for (int second = 1; second <= 12; second++)
            {
                yield return new WaitForSeconds(1f);

                var nm = NetworkManager.Singleton;
                if (nm == null)
                {
                    Utils.LogError("[StaffPanel] NetworkManager disappeared while connecting.");
                    yield break;
                }

                if (nm.IsConnectedClient)
                {
                    Utils.Log($"[StaffPanel] CONNECTED after ~{second}s. localId={nm.LocalClientId}");
                    yield break;
                }

                if (!nm.IsListening)
                {
                    Utils.LogError($"[StaffPanel] Connection dropped after ~{second}s " +
                                   $"(disconnect reason: '{nm.DisconnectReason}').");
                    yield break;
                }

                Utils.Log($"[StaffPanel] t+{second}s still connecting: listening={nm.IsListening} " +
                          $"connected={nm.IsConnectedClient} localId={nm.LocalClientId}");
            }

            Utils.LogError("[StaffPanel] Still not connected after 12s -- approval or scene sync never completed.");
        }

        void OnJoinByIpClicked()
        {
            string ip = m_IpInput != null ? m_IpInput.text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(ip))
            {
                m_StatusText.text = "Enter the host PC's IP address first.";
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
                m_StatusText.text = "Cannot start yet -- waiting on players.";
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;

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

            canvasGO.AddComponent<GraphicRaycaster>();

            var root = CreatePanel(canvasGO.transform, "StaffPanelRoot", new Vector2(360, 0));
            var rootLayout = root.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(16, 16, 16, 16);
            rootLayout.spacing = 8;
            rootLayout.childControlHeight = false;
            rootLayout.childForceExpandHeight = false;
            var rootFitter = root.AddComponent<ContentSizeFitter>();
            rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0, 1);
            rootRect.anchorMax = new Vector2(0, 1);
            rootRect.pivot = new Vector2(0, 1);
            rootRect.anchoredPosition = new Vector2(16, -16);

            CreateText(root.transform, "Staff Control Panel", 20, FontStyle.Bold);

            // Pre-connect panel
            m_PreConnectPanel = CreatePanel(root.transform, "PreConnectPanel", new Vector2(360, 0));
            var preLayout = m_PreConnectPanel.AddComponent<VerticalLayoutGroup>();
            preLayout.spacing = 6;
            preLayout.childControlHeight = false;
            preLayout.childForceExpandHeight = false;
            m_PreConnectPanel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateButton(m_PreConnectPanel.transform, "Host Session", OnHostClicked);

            // Manual fallback: UDP broadcast is commonly blocked by firewalls, VPN adapters and
            // managed switches, and without this the panel would offer no way to connect at all.
            CreateText(m_PreConnectPanel.transform, "Or join by host IP:", 14, FontStyle.Italic);
            m_IpInput = CreateInputField(m_PreConnectPanel.transform, "192.168.1.10");
            CreateButton(m_PreConnectPanel.transform, "Join by IP", OnJoinByIpClicked);

            CreateText(m_PreConnectPanel.transform, "Nearby sessions:", 14, FontStyle.Italic);

            var listGO = CreatePanel(m_PreConnectPanel.transform, "SessionList", new Vector2(360, 0));
            var listLayout = listGO.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 4;
            listLayout.childControlHeight = false;
            listLayout.childForceExpandHeight = false;
            listGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            m_SessionListParent = listGO.transform;

            // Connected panel
            m_ConnectedPanel = CreatePanel(root.transform, "ConnectedPanel", new Vector2(360, 0));
            var connLayout = m_ConnectedPanel.AddComponent<VerticalLayoutGroup>();
            connLayout.spacing = 6;
            connLayout.childControlHeight = false;
            connLayout.childForceExpandHeight = false;
            m_ConnectedPanel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            m_StatusText = CreateText(m_ConnectedPanel.transform, "", 14, FontStyle.Normal);
            m_RoleStatusText = CreateText(m_ConnectedPanel.transform, "", 14, FontStyle.Normal);
            m_DiagText = CreateText(m_ConnectedPanel.transform, "", 12, FontStyle.Normal);
            m_DiagText.color = new Color(0.65f, 0.85f, 0.65f);
            m_StartButton = CreateButton(m_ConnectedPanel.transform, "Start Experience", OnStartClicked);

            BuildBeatPanel(m_ConnectedPanel.transform);

            m_PreConnectPanel.SetActive(true);
            m_ConnectedPanel.SetActive(false);
        }

        /// <summary>
        /// Beat transport for staff. Skip exists because a two-player gate is a hang risk in
        /// front of an audience, and jump exists because rehearsing a late beat otherwise means
        /// replaying the whole piece to reach it.
        /// </summary>
        void BuildBeatPanel(Transform parent)
        {
            m_BeatPanel = CreatePanel(parent, "BeatPanel", new Vector2(360, 0));
            var layout = m_BeatPanel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            m_BeatPanel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateText(m_BeatPanel.transform, "Beat", 16, FontStyle.Bold);
            m_BeatText = CreateText(m_BeatPanel.transform, "", 14, FontStyle.Normal);
            m_BeatText.color = new Color(0.95f, 0.85f, 0.55f);

            var transport = CreateRow(m_BeatPanel.transform, "Transport");
            CreateButton(transport, "< Prev", OnBeatPrevClicked, new Vector2(80, 28));
            CreateButton(transport, "Next >", OnBeatNextClicked, new Vector2(80, 28));
            CreateButton(transport, "Restart", OnBeatRestartClicked, new Vector2(80, 28));

            var jumps = CreateRow(m_BeatPanel.transform, "Jump");
            foreach (var beat in StoryBeats.Ordered)
            {
                // Captured per iteration on purpose; one shared variable would make every
                // button jump to the last beat in the list.
                StoryBeat target = beat;
                CreateButton(jumps, StoryBeats.ShortCode(target), () => OnBeatJumpClicked(target), new Vector2(30, 24));
            }

            m_BeatPanel.SetActive(false);
        }

        static Transform CreateRow(Transform parent, string name)
        {
            var row = CreatePanel(parent, name, new Vector2(340, 30));
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            return row.transform;
        }

        GameObject CreateSessionRow(LanSessionInfo session)
        {
            var row = CreatePanel(m_SessionListParent, $"Session_{session.hostAddress}", new Vector2(360, 32));
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;

            CreateText(row.transform, $"{session.sessionName} ({session.currentPlayers}/{session.maxPlayers})", 14, FontStyle.Normal);
            CreateButton(row.transform, "Join", () => OnJoinClicked(session), new Vector2(80, 28));

            return row;
        }

        static GameObject CreatePanel(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            return go;
        }

        static Text CreateText(Transform parent, string content, int fontSize, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = Color.white;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(340, fontSize + 8);
            return text;
        }

        static InputField CreateInputField(Transform parent, string placeholder)
        {
            var go = new GameObject("IpInput", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(280, 30);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);

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

        static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Vector2? size = null)
        {
            var go = new GameObject($"Button_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            var button = go.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size ?? new Vector2(200, 32);

            var textGO = new GameObject("Label", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;

            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            return button;
        }

        #endregion
    }
}
