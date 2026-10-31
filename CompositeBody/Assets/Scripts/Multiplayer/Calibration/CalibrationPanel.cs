using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// The calibration panel: one key, and everything staff need to line this headset up or
    /// rescue it.
    ///
    /// ITS OWN PANEL, NOT A ROW IN THE INTERACTION LIST. The controls started life as an extra
    /// row on <c>InteractionDebugPanel</c>, which was the cheap place to put them and the wrong
    /// one. That list is a dozen interactions deep, it scrolls, calibration sits in its bottom
    /// section, and the moment someone reaches for it is the moment a performer is standing in
    /// the wrong place with an audience watching. A tool used under that kind of pressure has to
    /// be one key away and contain nothing else.
    ///
    /// THE INTERACTION ROW STAYS, though, and is not a duplicate of this: every interaction in
    /// the piece registers one, and that row is also where the OTHER machine's calibration state
    /// appears, mirrored over the debug relay. This panel is about the headset in front of you;
    /// the row is how one screen sees both.
    ///
    /// NO DEBUG GATE. The interaction panel's buttons are dead in a venue build unless it was
    /// launched with <c>-debuginteractions</c>, because firing a story gate by misclick during a
    /// performance must not be one click away. These buttons are the opposite case: they exist
    /// precisely to be used in the venue, with the audience in. What guards them instead is that
    /// the panel is hidden behind a key, the steps are a centimetre, and
    /// <see cref="HeadsetRestCalibrator"/> refuses a calibration whose tilt does not match the
    /// cradle.
    ///
    /// THIS MACHINE ONLY. Each PC calibrates and corrects its own headset; nothing here reaches
    /// across the network. The person who can see whether a correction helped is the one at that
    /// PC watching that headset's mirror.
    ///
    /// BUILT PROCEDURALLY, like the staff and interaction panels: drop the component on an
    /// object and the panel exists, with no prefab to keep in step. A screen-space overlay, so
    /// it renders to the desktop mirror and cannot end up floating in a player's view, and its
    /// Canvas is found by <c>UiVisibility</c> like every other panel, so backquote clears it off
    /// the screen with the rest.
    /// </summary>
    public class CalibrationPanel : MonoBehaviour
    {
        [SerializeField, Tooltip("Shows and hides the panel. F4 by default: F1 and F2 belong to the debug scenery toggle, F2 to the show scrubber and F3 to the interaction list.")]
        Key m_ToggleKey = Key.F4;

        [SerializeField, Tooltip("Open as soon as the scene starts. Off: the key opens it.")]
        bool m_VisibleOnStart;

        [SerializeField, Min(0.05f), Tooltip("Seconds between refreshes. Faster than the interaction list because the settling indicator is what staff watch while waiting for a capture.")]
        float m_RefreshInterval = 0.15f;

        [SerializeField, Min(240f), Tooltip("Panel width in reference pixels.")]
        float m_Width = 360f;

        [Header("Correction steps")]
        [SerializeField, Min(0.001f), Tooltip("Small step, in metres.")]
        float m_FineMetres = 0.01f;

        [SerializeField, Min(0.001f), Tooltip("Large step, in metres. For an alignment that is visibly out rather than slightly off.")]
        float m_CoarseMetres = 0.05f;

        [SerializeField, Min(0.01f), Tooltip("Small step, in degrees.")]
        float m_FineDegrees = 0.5f;

        [SerializeField, Min(0.01f), Tooltip("Large step, in degrees.")]
        float m_CoarseDegrees = 2f;

        static readonly Color k_Good = new(0.55f, 0.95f, 0.6f);
        static readonly Color k_Waiting = new(0.98f, 0.85f, 0.45f);
        static readonly Color k_Bad = new(0.98f, 0.5f, 0.45f);
        static readonly Color k_Dim = new(0.62f, 0.64f, 0.70f);

        HeadsetRestCalibrator m_Calibrator;

        GameObject m_Root;
        Text m_Status;
        Text m_Headset;
        Text m_Correction;
        Button m_Calibrate, m_Cancel, m_Step, m_Undo;
        Text m_StepLabel;
        readonly System.Collections.Generic.List<Button> m_Steps = new();

        bool m_Coarse;
        float m_NextRefresh;

        /// <summary>Open or close it from code, for a harness or another panel.</summary>
        public bool visible
        {
            get => m_Root != null && m_Root.activeSelf;
            set { if (m_Root != null) m_Root.SetActive(value); }
        }

        /// <summary>The key this panel answers to, so a log line can name it.</summary>
        public Key toggleKey => m_ToggleKey;

        void Awake()
        {
            BuildUI();
            m_Root.SetActive(m_VisibleOnStart);
        }

        void Update()
        {
            if (Keyboard.current != null && m_ToggleKey != Key.None &&
                Keyboard.current[m_ToggleKey].wasPressedThisFrame)
            {
                m_Root.SetActive(!m_Root.activeSelf);
            }

            if (!m_Root.activeSelf) return;

            if (Time.unscaledTime < m_NextRefresh) return;
            m_NextRefresh = Time.unscaledTime + m_RefreshInterval;

            Refresh();
        }

        void Refresh()
        {
            if (m_Calibrator == null) m_Calibrator = FindAnyObjectByType<HeadsetRestCalibrator>();

            if (m_Calibrator == null)
            {
                m_Status.text = "no HeadsetRestCalibrator in the scene";
                m_Status.color = k_Bad;
                m_Headset.text = "nothing to calibrate from here";
                m_Correction.text = "";
                SetButton(m_Calibrate, false);
                SetButton(m_Cancel, false);
                SetButton(m_Undo, false);
                foreach (Button step in m_Steps) SetButton(step, false);
                return;
            }

            m_Status.text = m_Calibrator.statusDetail;
            m_Status.color = m_Calibrator.isArmed ? k_Waiting
                           : m_Calibrator.hasCalibrated ? k_Good
                           : k_Dim;

            // The headset line is what staff watch while a capture is armed: it says why nothing
            // has happened yet, which is the question the panel exists to answer.
            if (!m_Calibrator.isTracking)
            {
                m_Headset.text = "headset NOT tracking -- if it slept when it came off, " +
                                 "disable the proximity sensor";
                m_Headset.color = k_Bad;
            }
            else if (m_Calibrator.isArmed)
            {
                m_Headset.text = $"armed: settling {m_Calibrator.settleProgress * 100f:0}%" +
                                 $"   gives up in {m_Calibrator.armSecondsRemaining:0}s";
                m_Headset.color = k_Waiting;
            }
            else
            {
                m_Headset.text = $"headset tracking   ({m_Calibrator.calibrateKey} also calibrates)";
                m_Headset.color = k_Dim;
            }

            string correction = m_Calibrator.correctionSummary;
            m_Correction.text = string.IsNullOrEmpty(correction)
                ? "no hand correction"
                : "hand-corrected " + correction;
            m_Correction.color = string.IsNullOrEmpty(correction) ? k_Dim : k_Waiting;

            SetButton(m_Calibrate, !m_Calibrator.isArmed && m_Calibrator.isActiveAndEnabled);
            SetButton(m_Cancel, m_Calibrator.isArmed);
            SetButton(m_Undo, m_Calibrator.hasCorrection);
            SetButton(m_Step, true);
            foreach (Button step in m_Steps) SetButton(step, m_Calibrator.canNudge);

            if (m_StepLabel != null) m_StepLabel.text = StepLabel();
        }

        void Calibrate()
        {
            if (m_Calibrator == null) return;
            m_Calibrator.Arm();
            m_NextRefresh = 0f;
        }

        void Cancel()
        {
            if (m_Calibrator == null) return;
            m_Calibrator.Cancel();
            m_NextRefresh = 0f;
        }

        void Nudge(Vector3 direction, float turn)
        {
            if (m_Calibrator == null) return;

            float metres = m_Coarse ? m_CoarseMetres : m_FineMetres;
            float degrees = m_Coarse ? m_CoarseDegrees : m_FineDegrees;

            m_Calibrator.Nudge(direction * metres, turn * degrees);
            m_NextRefresh = 0f;
        }

        void Undo()
        {
            if (m_Calibrator == null) return;
            m_Calibrator.UndoCorrection();
            m_NextRefresh = 0f;
        }

        void ToggleStep()
        {
            m_Coarse = !m_Coarse;
            m_NextRefresh = 0f;
        }

        string StepLabel() =>
            m_Coarse
                ? $"{m_CoarseMetres * 100f:0.#}cm {m_CoarseDegrees:0.##}deg"
                : $"{m_FineMetres * 100f:0.#}cm {m_FineDegrees:0.##}deg";

        #region UI

        void BuildUI()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above the staff panel's 100 and the interaction list's 110: this is the one a
            // person is deliberately looking at when it is open.
            canvas.sortingOrder = 120;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);

            gameObject.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            m_Root = CreateBox(transform, "CalibrationPanelRoot", new Vector2(m_Width, 0));
            m_Root.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.94f);

            var layout = m_Root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 12);
            layout.spacing = 6;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            m_Root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Bottom-left: the staff panel owns the top-left and the interaction list the
            // top-right, and all three are open at once often enough to matter.
            var rect = m_Root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(0, 0);
            rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = new Vector2(16, 16);
            rect.sizeDelta = new Vector2(m_Width, 0);

            Text title = CreateText(m_Root.transform, $"CALIBRATION   [{m_ToggleKey}]", 16, FontStyle.Bold);
            title.color = new Color(0.75f, 0.82f, 1f);

            Text scope = CreateText(m_Root.transform, "this machine's headset only", 11, FontStyle.Italic);
            scope.color = k_Dim;

            m_Status = CreateText(m_Root.transform, "", 13, FontStyle.Bold);
            m_Headset = CreateText(m_Root.transform, "", 11, FontStyle.Normal);

            Transform capture = CreateStrip(m_Root.transform, "Capture");
            m_Calibrate = CreateButton(capture, "Calibrate", Calibrate, new Vector2(104, 26));
            m_Cancel = CreateButton(capture, "cancel", Cancel, new Vector2(66, 26));

            Text heading = CreateText(m_Root.transform, "CORRECT BY HAND   (room directions)", 11, FontStyle.Bold);
            heading.color = new Color(0.75f, 0.82f, 1f);

            Transform move = CreateStrip(m_Root.transform, "Move");
            m_Step = CreateButton(move, StepLabel(), ToggleStep, new Vector2(84, 22));
            m_StepLabel = m_Step.GetComponentInChildren<Text>();
            m_Steps.Add(CreateButton(move, "L", () => Nudge(Vector3.left, 0f), new Vector2(28, 22)));
            m_Steps.Add(CreateButton(move, "R", () => Nudge(Vector3.right, 0f), new Vector2(28, 22)));
            m_Steps.Add(CreateButton(move, "F", () => Nudge(Vector3.forward, 0f), new Vector2(28, 22)));
            m_Steps.Add(CreateButton(move, "B", () => Nudge(Vector3.back, 0f), new Vector2(28, 22)));

            Transform turn = CreateStrip(m_Root.transform, "Turn");
            m_Steps.Add(CreateButton(turn, "Up", () => Nudge(Vector3.up, 0f), new Vector2(34, 22)));
            m_Steps.Add(CreateButton(turn, "Dn", () => Nudge(Vector3.down, 0f), new Vector2(34, 22)));
            m_Steps.Add(CreateButton(turn, "CCW", () => Nudge(Vector3.zero, -1f), new Vector2(40, 22)));
            m_Steps.Add(CreateButton(turn, "CW", () => Nudge(Vector3.zero, 1f), new Vector2(38, 22)));
            m_Undo = CreateButton(turn, "undo", Undo, new Vector2(50, 22));

            m_Correction = CreateText(m_Root.transform, "", 11, FontStyle.Normal);
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem (Calibration Panel Fallback)");
            go.AddComponent<EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        static GameObject CreateBox(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = size;
            return go;
        }

        static Transform CreateStrip(Transform parent, string name)
        {
            GameObject row = CreateBox(parent, name, new Vector2(320, 26));
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            return row.transform;
        }

        Text CreateText(Transform parent, string content, int fontSize, FontStyle style)
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

            // Height follows the text, so the two-line "not tracking" message does not land on
            // top of the buttons under it.
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(m_Width - 24f, fontSize + 4);
            return text;
        }

        static Button CreateButton(Transform parent, string label,
                                   UnityEngine.Events.UnityAction onClick, Vector2 size)
        {
            var go = new GameObject($"Button_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            go.AddComponent<Image>().color = new Color(0.22f, 0.3f, 0.24f, 0.95f);

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

        static void SetButton(Button button, bool usable)
        {
            if (button == null) return;

            if (button.interactable != usable) button.interactable = usable;

            var image = button.GetComponent<Image>();
            if (image != null)
                image.color = usable ? new Color(0.22f, 0.3f, 0.24f, 0.95f)
                                     : new Color(0.16f, 0.16f, 0.16f, 0.6f);
        }

        #endregion
    }
}
