using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using CompositeBody.Multiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// The interaction list, on the desktop mirror: every interaction in the piece, whether it
    /// has gone off, which players set it off, how long before a timeout sets it off anyway, and
    /// a button per interaction to set it off from here.
    ///
    /// A SCREEN SPACE OVERLAY, WHICH IS WHY IT IS NOT IN THE HEADSET. The same arrangement
    /// StaffControlPanel uses: an overlay canvas renders to the mirror window only, so a panel
    /// covered in buttons cannot end up floating in a player's view. It is also why the buttons
    /// are mouse-driven rather than hand-driven -- the person debugging is at the keyboard.
    ///
    /// BUILT PROCEDURALLY, like the staff panel, so it needs no prefab and no scene wiring: drop
    /// the component on any object and the panel exists. A debug tool that needs a prefab kept
    /// in step with a growing list of interactions is a debug tool that rots.
    ///
    /// ROWS COME FROM <see cref="InteractionDebug"/>, NOT FROM A LIST HERE, so an interaction
    /// added to the piece next month appears without this file being touched. The only thing
    /// this file knows how to find on its own is the set of task ids, because a task gate has no
    /// component to register itself: those are read off the beat sheet and off whatever the
    /// server already has flags for.
    ///
    /// Hidden by default and toggled with a key, and its buttons are dead unless the build was
    /// told to enable them -- see <see cref="InteractionDebug.fireEnabled"/> for why that split.
    /// </summary>
    public class InteractionDebugPanel : MonoBehaviour
    {
        [SerializeField, Tooltip("Shows and hides the panel.")]
        Key m_ToggleKey = Key.F3;

        [SerializeField, Tooltip("Open as soon as the scene starts. Off: the key opens it.")]
        bool m_VisibleOnStart;

        [SerializeField, Min(0.05f), Tooltip("Seconds between refreshes. The rows ask every " +
                                             "interaction for its state, so this is not free -- " +
                                             "but at 5/second a countdown still reads smoothly.")]
        float m_RefreshInterval = 0.2f;

        [SerializeField, Min(200f), Tooltip("Panel width in reference pixels.")]
        float m_Width = 520f;

        const float k_Padding = 12f;
        const float k_Spacing = 6f;

        static readonly Color k_Fired = new(0.55f, 0.95f, 0.6f);
        static readonly Color k_Waiting = new(0.98f, 0.85f, 0.45f);
        static readonly Color k_Dim = new(0.62f, 0.64f, 0.70f);

        class Row
        {
            public IDebugInteraction interaction;
            public GameObject root;
            public Text status;
            public Text detail;
            public Button fire, player1, player2, both, reset;
            public Text fireLabel;

            // Only on rows whose interaction can be hand-corrected, which is almost none of them.
        }

        readonly List<Row> m_Rows = new();
        readonly StringBuilder m_Builder = new();

        GameObject m_PanelRoot;
        RectTransform m_Viewport;
        RectTransform m_Content;
        Transform m_RowsParent;
        Text m_Header;
        Text m_Footer;
        Text m_Unmatched;

        bool m_Dirty = true;
        float m_NextRefresh;

        /// <summary>
        /// Open or close it from code. Exists so the play-mode harness can open the panel without
        /// a keystroke, and so anything else that wants to offer a way in -- a line on the staff
        /// panel, say -- does not have to synthesise one.
        /// </summary>
        public bool visible
        {
            get => m_PanelRoot != null && m_PanelRoot.activeSelf;
            set { if (m_PanelRoot != null) m_PanelRoot.SetActive(value); }
        }

        /// <summary>Rows currently drawn, headings included. For the harness.</summary>
        public int rowCount => m_Rows.Count;

        /// <summary>How tall the list is, and how much of it the window can show.</summary>
        public float contentHeight => m_Content != null ? m_Content.rect.height : 0f;

        public float viewportHeight => m_Viewport != null ? m_Viewport.rect.height : 0f;

        /// <summary>
        /// Whether the panel is inside the window rather than hanging off the bottom of it. The
        /// list being taller than the window is fine -- it scrolls, and the footer says so -- but
        /// the panel itself extending past the edge is not, because those rows cannot be reached.
        /// </summary>
        public bool fitsOnScreen
        {
            get
            {
                var canvasRect = transform as RectTransform;
                var rootRect = m_PanelRoot != null ? m_PanelRoot.GetComponent<RectTransform>() : null;
                if (canvasRect == null || rootRect == null) return true;
                return rootRect.rect.height <= canvasRect.rect.height + 0.5f;
            }
        }

        void Awake()
        {
            BuildUI();
            m_PanelRoot.SetActive(m_VisibleOnStart);
        }

        void OnEnable()
        {
            InteractionDebug.onEntriesChanged += HandleEntriesChanged;
            EnsureEventSystem();
        }

        void OnDisable()
        {
            InteractionDebug.onEntriesChanged -= HandleEntriesChanged;
        }

        void HandleEntriesChanged() => m_Dirty = true;

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current[m_ToggleKey].wasPressedThisFrame)
                m_PanelRoot.SetActive(!m_PanelRoot.activeSelf);

            if (!m_PanelRoot.activeSelf) return;

            Scroll();

            if (Time.unscaledTime < m_NextRefresh) return;
            m_NextRefresh = Time.unscaledTime + m_RefreshInterval;

            FitToWindow();
            DiscoverTaskGates();
            if (m_Dirty) RebuildRows();
            Refresh();
        }

        /// <summary>
        /// Keep the panel inside the window, whatever shape the window is.
        ///
        /// IT USED TO BE 740 REFERENCE PIXELS TALL, FULL STOP. The canvas scaler matches the
        /// reference WIDTH, so the canvas's height in reference units is whatever the window's
        /// aspect makes it -- on a wide or short window that is well under 740, and the bottom of
        /// the panel simply hung off the bottom of the screen. The rows down there could not be
        /// scrolled to either, because the content was SHORTER than the viewport and the scroll
        /// clamp correctly refused to move. The rows existed, were correct, and could not be seen
        /// or reached: the exact appearance of an interaction missing from the list.
        /// </summary>
        void FitToWindow()
        {
            var canvasRect = transform as RectTransform;
            if (canvasRect == null || m_PanelRoot == null) return;

            var rootRect = m_PanelRoot.GetComponent<RectTransform>();

            const float margin = 16f;
            float available = Mathf.Max(canvasRect.rect.height - margin * 2f, 120f);
            if (!Mathf.Approximately(rootRect.sizeDelta.y, available))
                rootRect.sizeDelta = new Vector2(m_Width, available);

            // The viewport takes whatever the header and footer leave. Measured rather than
            // assumed, so a header that grows to two lines does not push the list off the end.
            float used = m_Header.rectTransform.rect.height +
                         m_Footer.rectTransform.rect.height +
                         k_Padding * 2f + k_Spacing * 2f;

            float viewport = Mathf.Max(available - used, 80f);
            if (!Mathf.Approximately(m_Viewport.sizeDelta.y, viewport))
                m_Viewport.sizeDelta = new Vector2(m_Width - k_Padding * 2f, viewport);
        }

        /// <summary>
        /// Wheel scrolling, because the list outgrows the screen once every beat has its
        /// interactions. A RectMask2D on the viewport and a shifted content object rather than a
        /// ScrollRect: there is no dragging to support, and this is three lines against a
        /// viewport, a scrollbar and two more RectTransforms to build by hand.
        /// </summary>
        void Scroll()
        {
            if (Mouse.current == null) return;

            float wheel = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Approximately(wheel, 0f)) return;

            float overflow = Mathf.Max(m_Content.rect.height - m_Viewport.rect.height, 0f);
            Vector2 position = m_Content.anchoredPosition;
            position.y = Mathf.Clamp(position.y - wheel * 0.5f, 0f, overflow);
            m_Content.anchoredPosition = position;
        }

        /// <summary>
        /// Task gates have no component to register them, so they are found: every gate named on
        /// the beat sheet, plus every task the server already holds flags for. The second half
        /// matters -- it means a task reported by something nobody has told this panel about
        /// still shows up, as its bare id, rather than being invisible.
        /// </summary>
        void DiscoverTaskGates()
        {
            ExperienceDirector director = ExperienceDirector.Instance;
            if (director != null)
            {
                foreach (StoryBeat beat in StoryBeats.Ordered)
                {
                    BeatDefinition def = director.DefinitionFor(beat);
                    if (string.IsNullOrEmpty(def.gateTaskId)) continue;

                    InteractionDebug.RegisterTaskGate(def.gateTaskId,
                        $"{StoryBeats.ShortCode(beat)} gate -- {StoryBeats.DisplayName(beat)}");
                }
            }

            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress == null) return;

            foreach (var task in progress.tasks)
                InteractionDebug.RegisterTaskGate(task.taskId.ToString(), null);
        }

        void RebuildRows()
        {
            m_Dirty = false;

            foreach (Row row in m_Rows) Destroy(row.root);
            m_Rows.Clear();

            // Grouped, and in a fixed order, so a row does not move under the mouse between one
            // refresh and the next: gates first because they are what the show waits on.
            foreach (DebugInteractionScope scope in new[]
                     {
                         DebugInteractionScope.Gate,
                         DebugInteractionScope.Player,
                         DebugInteractionScope.LocalMachine,
                     })
            {
                bool heading = false;

                foreach (IDebugInteraction interaction in InteractionDebug.entries)
                {
                    if (interaction.debugScope != scope) continue;

                    if (!heading)
                    {
                        heading = true;
                        var header = CreateText(m_RowsParent, HeadingFor(scope), 14, FontStyle.Bold, 500);
                        header.color = new Color(0.75f, 0.82f, 1f);
                        m_Rows.Add(new Row { root = header.gameObject });
                    }

                    m_Rows.Add(BuildRow(interaction));
                }
            }

            if (m_Rows.Count == 0)
            {
                var empty = CreateText(m_RowsParent,
                    "Nothing registered. Interactions appear here once their scene is loaded.",
                    13, FontStyle.Italic, 500);
                empty.color = k_Dim;
                m_Rows.Add(new Row { root = empty.gameObject });
            }

            // Kept last so it stays at the bottom of the list as rows come and go.
            m_Unmatched.transform.SetAsLastSibling();
        }

        static string HeadingFor(DebugInteractionScope scope) => scope switch
        {
            DebugInteractionScope.Gate => "GATES  (both players, server-authoritative)",
            DebugInteractionScope.Player => "PER PLAYER  (replicated)",
            _ => "LOCAL TO A MACHINE  (mirrored for display only)",
        };

        Row BuildRow(IDebugInteraction interaction)
        {
            var row = new Row { interaction = interaction };

            row.root = CreatePanel(m_RowsParent, $"Row_{interaction.debugId}", new Vector2(m_Width - 24f, 0));
            var layout = row.root.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 1;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            row.root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            row.status = CreateText(row.root.transform, "", 13, FontStyle.Bold, m_Width - 30f);
            row.detail = CreateText(row.root.transform, "", 11, FontStyle.Normal, m_Width - 30f);
            row.detail.color = k_Dim;

            Transform buttons = CreateRow(row.root.transform, "Buttons");
            row.fire = CreateButton(buttons, interaction.debugFireLabel,
                                    () => Fire(interaction, DebugFireTarget.Local), new Vector2(104, 22));
            row.fireLabel = row.fire.GetComponentInChildren<Text>();
            row.player1 = CreateButton(buttons, "as P1",
                                       () => Fire(interaction, DebugFireTarget.Player1), new Vector2(58, 22));
            row.player2 = CreateButton(buttons, "as P2",
                                       () => Fire(interaction, DebugFireTarget.Player2), new Vector2(58, 22));
            row.both = CreateButton(buttons, "both",
                                    () => Fire(interaction, DebugFireTarget.Both), new Vector2(54, 22));
            row.reset = CreateButton(buttons, "reset",
                                     () => Reset(interaction), new Vector2(54, 22));

            return row;
        }

        void Fire(IDebugInteraction interaction, DebugFireTarget target)
        {
            if (!InteractionDebug.fireEnabled || !interaction.DebugCanFire(target)) return;

            Debug.Log($"[InteractionDebug] '{interaction.debugId}' fired from the panel as {target}.");
            interaction.DebugFire(target);
            m_NextRefresh = 0f;
        }

        void Reset(IDebugInteraction interaction)
        {
            if (!InteractionDebug.fireEnabled || !interaction.DebugCanReset()) return;

            Debug.Log($"[InteractionDebug] '{interaction.debugId}' reset from the panel.");
            interaction.DebugReset();
            m_NextRefresh = 0f;
        }

        void Refresh()
        {
            RefreshHeader();

            foreach (Row row in m_Rows)
            {
                if (row.interaction == null) continue;   // a heading
                RefreshRow(row);
            }

            RefreshUnmatched();
        }

        void RefreshHeader()
        {
            NetworkManager nm = NetworkManager.Singleton;
            string where = nm == null || !nm.IsListening
                ? "no session"
                : nm.IsServer ? $"HOST (client {nm.LocalClientId})" : $"client {nm.LocalClientId}";

            PlayerRole role = InteractionDebug.LocalRole();
            GameSessionManager session = GameSessionManager.Instance;
            string state = session != null ? session.sessionState.ToString() : "-";

            m_Header.text = $"Interaction Debug   [{m_ToggleKey}]\n" +
                            $"{where}   role: {role}   session: {state}";

            string buttons = InteractionDebug.fireEnabled
                ? "Buttons live. 'as P1/P2/both' write flags server-side or ask the other machine; " +
                  "the local button takes the real path."
                : $"Buttons DISABLED in this build. Relaunch with {InteractionDebug.FireArgument} to enable them.";

            // NAME THE HIDDEN ROWS. A list that quietly ends at the bottom of the window is how
            // an interaction comes to look as though it was never built.
            float hidden = m_Content.rect.height - m_Viewport.rect.height;
            if (hidden > 1f)
            {
                float above = m_Content.anchoredPosition.y;
                float below = hidden - above;
                buttons = $"SCROLL (mouse wheel) -- {(above > 1f ? $"{above:0}px above, " : "")}" +
                          $"{Mathf.Max(below, 0f):0}px below.\n" + buttons;
            }

            m_Footer.text = buttons;
            m_Footer.color = InteractionDebug.fireEnabled ? k_Dim : k_Waiting;
        }

        void RefreshRow(Row row)
        {
            DebugInteractionState state = row.interaction.DebugState();

            m_Builder.Clear();
            m_Builder.Append(state.fired ? "[FIRED]  " : "[     ]  ");
            m_Builder.Append(row.interaction.debugLabel);

            if (row.interaction.debugScope != DebugInteractionScope.LocalMachine)
            {
                m_Builder.Append("    P1:").Append(state.player1 ? "yes" : "--");
                m_Builder.Append("  P2:").Append(state.player2 ? "yes" : "--");
            }

            if (state.secondsToTimeout >= 0f)
                m_Builder.Append($"    timeout in {state.secondsToTimeout:0}s");

            row.status.text = m_Builder.ToString();
            row.status.color = state.fired ? k_Fired : k_Waiting;

            m_Builder.Clear();
            m_Builder.Append(string.IsNullOrEmpty(state.detail) ? row.interaction.debugId : state.detail);

            // The other machine's copy of a local-only interaction, so one screen shows both
            // halves of a piece whose interactions are deliberately not replicated.
            if (row.interaction.debugScope == DebugInteractionScope.LocalMachine)
                AppendRemoteLines(row.interaction.debugId);

            row.detail.text = m_Builder.ToString();

            SetButton(row.fire, row.interaction.DebugCanFire(DebugFireTarget.Local));
            SetButton(row.player1, row.interaction.DebugCanFire(DebugFireTarget.Player1));
            SetButton(row.player2, row.interaction.DebugCanFire(DebugFireTarget.Player2));
            SetButton(row.both, row.interaction.DebugCanFire(DebugFireTarget.Both));
            SetButton(row.reset, row.interaction.DebugCanReset());

            if (row.fireLabel != null) row.fireLabel.text = row.interaction.debugFireLabel;

        }

        void AppendRemoteLines(string debugId)
        {
            InteractionDebugRelay relay = InteractionDebugRelay.Instance;
            if (relay == null || !relay.IsSpawned) return;

            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

            foreach (var machine in relay.machines)
            {
                if (machine.clientId == me || !machine.id.ToString().Equals(debugId)) continue;

                PlayerRole role = InteractionDebugRelay.RoleOf(machine.clientId);
                string who = role != PlayerRole.Unassigned ? role.ToString() : $"client {machine.clientId}";

                m_Builder.Append($"\n{who}: {(machine.fired ? "FIRED" : "not fired")}");

                string detail = machine.detail.ToString();
                if (!string.IsNullOrEmpty(detail)) m_Builder.Append($" -- {detail}");
                if (machine.secondsToTimeout >= 0f)
                    m_Builder.Append($" (timeout in {machine.secondsToTimeout:0}s)");
            }
        }

        /// <summary>
        /// Rows the other machine has and this one does not. Should be empty -- both machines run
        /// the same build -- so it is shown rather than dropped: a mismatch here means the two
        /// are NOT on the same build, which is worth knowing before anything else is believed.
        /// </summary>
        void RefreshUnmatched()
        {
            InteractionDebugRelay relay = InteractionDebugRelay.Instance;
            if (relay == null || !relay.IsSpawned)
            {
                m_Unmatched.gameObject.SetActive(false);
                return;
            }

            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

            m_Builder.Clear();
            foreach (var machine in relay.machines)
            {
                if (machine.clientId == me) continue;

                string id = machine.id.ToString();
                if (IsRegistered(id)) continue;

                PlayerRole role = InteractionDebugRelay.RoleOf(machine.clientId);
                m_Builder.Append($"\n{id} ({role}): {(machine.fired ? "FIRED" : "not fired")}");
            }

            bool any = m_Builder.Length > 0;
            m_Unmatched.gameObject.SetActive(any);
            if (any)
                m_Unmatched.text = "ONLY ON THE OTHER MACHINE -- the two builds do not match:" + m_Builder;
        }

        static bool IsRegistered(string debugId)
        {
            foreach (IDebugInteraction interaction in InteractionDebug.entries)
            {
                if (interaction.debugId == debugId) return true;
            }
            return false;
        }

        static void SetButton(Button button, bool usable)
        {
            if (button == null) return;

            bool live = usable && InteractionDebug.fireEnabled;
            if (button.interactable != live) button.interactable = live;

            var image = button.GetComponent<Image>();
            if (image != null)
                image.color = live ? new Color(0.22f, 0.3f, 0.24f, 0.95f)
                                   : new Color(0.16f, 0.16f, 0.16f, 0.6f);
        }

        #region UI construction

        void BuildUI()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above the staff panel's 100: when both are open the one with the buttons being
            // pressed has to be the one on top.
            canvas.sortingOrder = 110;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);

            gameObject.AddComponent<GraphicRaycaster>();

            m_PanelRoot = CreatePanel(transform, "InteractionDebugRoot", new Vector2(m_Width, 0));
            var background = m_PanelRoot.AddComponent<Image>();
            background.color = new Color(0.04f, 0.05f, 0.07f, 0.92f);

            var rootLayout = m_PanelRoot.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset((int)k_Padding, (int)k_Padding, 10, 10);
            rootLayout.spacing = (int)k_Spacing;
            rootLayout.childControlHeight = false;
            rootLayout.childForceExpandHeight = false;

            // Anchored top-RIGHT, because the staff panel owns the top-left and the two must not
            // sit on top of each other in the venue.
            var rootRect = m_PanelRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1, 1);
            rootRect.anchorMax = new Vector2(1, 1);
            rootRect.pivot = new Vector2(1, 1);
            rootRect.anchoredPosition = new Vector2(-16, -16);
            rootRect.sizeDelta = new Vector2(m_Width, 740);

            m_Header = CreateText(m_PanelRoot.transform, "Interaction Debug", 16, FontStyle.Bold, m_Width - 24f);

            // The clipped window the rows scroll inside.
            var viewportGO = CreatePanel(m_PanelRoot.transform, "Viewport", new Vector2(m_Width - 24f, 640));
            viewportGO.AddComponent<RectMask2D>();
            m_Viewport = viewportGO.GetComponent<RectTransform>();

            var contentGO = CreatePanel(m_Viewport, "Content", new Vector2(m_Width - 24f, 0));
            m_Content = contentGO.GetComponent<RectTransform>();
            m_Content.anchorMin = new Vector2(0, 1);
            m_Content.anchorMax = new Vector2(0, 1);
            m_Content.pivot = new Vector2(0, 1);
            m_Content.anchoredPosition = Vector2.zero;

            var contentLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 5;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandHeight = false;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            m_RowsParent = contentGO.transform;

            m_Unmatched = CreateText(m_RowsParent, "", 11, FontStyle.Bold, m_Width - 30f);
            m_Unmatched.color = new Color(1f, 0.5f, 0.45f);
            m_Unmatched.gameObject.SetActive(false);

            m_Footer = CreateText(m_PanelRoot.transform, "", 11, FontStyle.Italic, m_Width - 24f);
            m_Footer.color = k_Dim;
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem (Interaction Debug Fallback)");
            go.AddComponent<EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        static GameObject CreatePanel(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = size;
            return go;
        }

        static Transform CreateRow(Transform parent, string name)
        {
            GameObject row = CreatePanel(parent, name, new Vector2(480, 24));
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            return row.transform;
        }

        static Text CreateText(Transform parent, string content, int fontSize, FontStyle style, float width)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = Color.white;
            text.text = content;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            // Height follows the text, so a two-line detail does not overlap the row below it.
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, fontSize + 4);
            return text;
        }

        static Button CreateButton(Transform parent, string label,
                                   UnityEngine.Events.UnityAction onClick, Vector2 size)
        {
            var go = new GameObject($"Button_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.22f, 0.3f, 0.24f, 0.95f);

            var button = go.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            go.GetComponent<RectTransform>().sizeDelta = size;

            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(go.transform, false);

            var text = labelGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 12;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;
            text.supportRichText = false;

            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return button;
        }

        #endregion
    }
}
