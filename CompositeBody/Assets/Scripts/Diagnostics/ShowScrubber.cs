using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CompositeBody.Experience;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// A bar across the bottom of the host's screen for putting the show at any second.
    ///
    /// Rehearsing the back half by sitting through the front half is the single most expensive
    /// thing about working on a 38-minute piece, and it is expensive every time. This is the
    /// cheap way out: drag, or press the beat you want.
    ///
    /// HOST ONLY, AND NOT BECAUSE OF ETIQUETTE. ShowTimelineDriver.ScrubTo returns immediately on
    /// a client -- show time is two server-owned numbers and the clients derive it -- so a
    /// scrubber on the other headset would move nothing and look broken. It draws nothing there
    /// rather than offering a control that cannot work.
    ///
    /// IMGUI on purpose. A staff tool that lives in a prefab has to be kept in a scene, wired,
    /// and merged; one that lives in a file is none of those. It is drawn only while the panel is
    /// open, so it costs nothing in a show.
    ///
    /// The jump buttons come from the timeline itself rather than a list kept here, so a clip
    /// renamed or moved is right in this bar the moment it is right on the timeline. That matters
    /// more than it sounds: a hand-kept list of beats would be another thing to be quietly wrong,
    /// and this week has had enough of those.
    /// </summary>
    public class ShowScrubber : MonoBehaviour
    {
        [SerializeField, Tooltip("Shows and hides the bar.")]
        Key m_ToggleKey = Key.F2;

        [SerializeField, Tooltip("Hidden until the key is pressed, so a show never starts with " +
                                 "a staff bar across it.")]
        bool m_VisibleOnStart;

        [SerializeField, Min(40f), Tooltip("Height of the bar in pixels.")]
        float m_Height = 150f;

        bool m_Visible;
        float m_Dragging = -1f;

        readonly List<(double start, string name)> m_Jumps = new();
        double m_JumpsFrom = -1.0;

        /// <summary>
        /// Makes itself, rather than living on an object in MainScene.
        ///
        /// A staff tool in a scene is one more thing to wire, to merge, and to find missing on
        /// the night. This way it is in every run of the project and costs a hidden GameObject.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            var go = new GameObject("~ShowScrubber");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ShowScrubber>();
        }

        void Start() => m_Visible = m_VisibleOnStart;

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current[m_ToggleKey].wasPressedThisFrame)
                m_Visible = !m_Visible;
        }

        ShowTimelineDriver Driver => ShowTimelineDriver.Instance;

        /// <summary>The show's full length, from the timeline the driver is running.</summary>
        double Duration
        {
            get
            {
                PlayableDirector director = Driver != null
                    ? Driver.GetComponent<PlayableDirector>()
                    : null;
                return director != null ? director.duration : 0.0;
            }
        }

        void OnGUI()
        {
            if (!UiVisibility.visible) return;
            if (!m_Visible) return;

            ShowTimelineDriver driver = Driver;
            if (driver == null)
            {
                Label("No ShowTimelineDriver in the scene; nothing to scrub.");
                return;
            }

            // Scrubbing is a server decision, so only the machine that can make it gets the bar.
            var network = Unity.Netcode.NetworkManager.Singleton;
            if (network != null && network.IsListening && !network.IsServer)
            {
                Label("Not the host. The show's time belongs to the server, so this scrubber " +
                      "would move nothing here.");
                return;
            }

            double duration = Duration;
            if (duration <= 0.0)
            {
                Label("The show timeline has no duration yet; it has probably not bound.");
                return;
            }

            GUILayout.BeginArea(new Rect(8, Screen.height - m_Height - 8, Screen.width - 16, m_Height),
                                GUI.skin.box);

            float now = driver.showTime;
            GUILayout.Label($"Show {Clock(now)} / {Clock((float)duration)}" +
                            (driver.isPlaying ? "" : "   PAUSED") +
                            (string.IsNullOrEmpty(driver.waitingOn)
                                ? ""
                                : $"   HELD, waiting on {driver.waitingOn}"));

            // The slider reads back from the clock every frame, so letting go anywhere leaves the
            // show where the handle was rather than where the last repaint thought it was.
            float wanted = GUILayout.HorizontalSlider(m_Dragging >= 0f ? m_Dragging : now,
                                                      0f, (float)duration,
                                                      GUILayout.Height(24));

            if (!Mathf.Approximately(wanted, now)) m_Dragging = wanted;

            if (m_Dragging >= 0f && Event.current.type == EventType.MouseUp)
            {
                driver.ScrubTo(m_Dragging);
                m_Dragging = -1f;
            }

            DrawJumps(driver, duration);

            GUILayout.EndArea();
        }

        void DrawJumps(ShowTimelineDriver driver, double duration)
        {
            RefreshJumps(duration);
            if (m_Jumps.Count == 0) return;

            int perRow = Mathf.Max(1, (Screen.width - 32) / 150);

            for (int i = 0; i < m_Jumps.Count; i += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + perRow, m_Jumps.Count); j++)
                {
                    (double start, string name) = m_Jumps[j];
                    if (GUILayout.Button($"{name}\n{Clock((float)start)}", GUILayout.Height(34)))
                        driver.ScrubTo((float)start);
                }
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// Read the clips off the timeline, once per asset rather than per frame -- OnGUI runs
        /// several times a frame and walking every track each time would be visible in the very
        /// frame times this is meant to help measure.
        /// </summary>
        void RefreshJumps(double duration)
        {
            if (System.Math.Abs(m_JumpsFrom - duration) < 0.001) return;
            m_JumpsFrom = duration;
            m_Jumps.Clear();

            PlayableDirector director = Driver != null ? Driver.GetComponent<PlayableDirector>() : null;
            if (director == null || director.playableAsset is not TimelineAsset timeline) return;

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                foreach (TimelineClip clip in track.GetClips())
                {
                    string name = string.IsNullOrWhiteSpace(clip.displayName)
                        ? track.name
                        : clip.displayName;

                    // "Active" clips are a track's own on/off band, not a beat anyone wants to
                    // jump to, and they outnumber the beats.
                    if (name == "Active") continue;

                    m_Jumps.Add((clip.start, name));
                }
            }

            m_Jumps.Sort((a, b) => a.start.CompareTo(b.start));
        }

        static string Clock(float seconds)
        {
            int whole = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{whole / 60:00}:{whole % 60:00}";
        }

        void Label(string text)
        {
            GUILayout.BeginArea(new Rect(8, Screen.height - 40, Screen.width - 16, 32), GUI.skin.box);
            GUILayout.Label(text);
            GUILayout.EndArea();
        }
    }
}
