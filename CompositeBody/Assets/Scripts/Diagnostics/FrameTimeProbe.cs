using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Says how long frames are actually taking, split into CPU and GPU.
    ///
    /// WHY SPLIT THEM. The question this exists to answer is whether the piece can afford the
    /// baked IX scenes -- 64 lightmaps in S1_v3 alone, with SSAO newly switched on -- and an
    /// average frame rate cannot answer it. Lightmaps cost GPU time and memory bandwidth;
    /// scripts and the netcode cost CPU. A number that mixes them says "it is slow" and nothing
    /// about what to do. These are reported apart so the next decision is obvious.
    ///
    /// WHY PERCENTILES AND NOT AN AVERAGE. In VR the frames that matter are the slow ones. A
    /// mean of 9 ms hides a 95th percentile of 14 ms, and it is the 14 that drops a frame, judders
    /// the image and is felt in the head rather than seen on a graph. The worst frame in each
    /// window is reported too, because a single 30 ms spike when a beat starts is a real fault
    /// even if every other frame is comfortable.
    ///
    ///   CompositeBody.exe -frametime
    ///
    /// Always on in the editor, so a scene can be measured by pressing play on it -- which is
    /// how to price a scene that is not in the build yet.
    ///
    /// Frame Timing Stats must be enabled in Player Settings or the GPU figure comes back empty;
    /// this says so rather than reporting zero, because a zero reads as "free".
    /// </summary>
    public static class FrameTimeProbe
    {
        const string k_Flag = "-frametime";

        /// <summary>Seconds between reports. Long enough to be a sample, short enough to watch.</summary>
        const float k_Window = 10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            bool asked = System.Environment.GetCommandLineArgs().Contains(k_Flag);
            if (!asked && !Application.isEditor) return;

            var go = new GameObject("~FrameTimeProbe");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        class Runner : MonoBehaviour
        {
            readonly List<float> m_Cpu = new();
            readonly List<float> m_Gpu = new();
            readonly List<float> m_Main = new();
            readonly List<float> m_Wall = new();

            readonly FrameTiming[] m_Timings = new FrameTiming[1];
            float m_Next;
            bool m_WarnedNoStats;

            void Start()
            {
                m_Next = Time.unscaledTime + k_Window;

                Debug.Log($"[FrameTime] measuring every {k_Window:0}s. " +
                          $"XR: {(XRSettings.enabled ? XRSettings.loadedDeviceName : "off")}, " +
                          $"eye texture {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}, " +
                          $"viewport scale {XRSettings.renderViewportScale:0.00}. " +
                          $"Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}.");
            }

            void Update()
            {
                // Wall clock, which is what the player experiences, and the only figure that
                // needs no settings turned on to be true.
                m_Wall.Add(Time.unscaledDeltaTime * 1000f);

                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, m_Timings) > 0)
                {
                    m_Cpu.Add((float)m_Timings[0].cpuFrameTime);
                    m_Gpu.Add((float)m_Timings[0].gpuFrameTime);
                    m_Main.Add((float)m_Timings[0].cpuMainThreadFrameTime);
                }

                if (Time.unscaledTime < m_Next) return;
                m_Next = Time.unscaledTime + k_Window;
                Report();
            }

            void Report()
            {
                if (m_Wall.Count == 0) return;

                string line = $"[FrameTime] over {m_Wall.Count} frames, " +
                              $"scene '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'" +
                              $"\n[FrameTime]   wall  {Describe(m_Wall)}";

                if (m_Gpu.Count > 0)
                {
                    line += $"\n[FrameTime]   cpu   {Describe(m_Cpu)}" +
                            $"\n[FrameTime]   main  {Describe(m_Main)}" +
                            $"\n[FrameTime]   gpu   {Describe(m_Gpu)}";
                }
                else if (!m_WarnedNoStats)
                {
                    m_WarnedNoStats = true;
                    line += "\n[FrameTime]   no CPU/GPU split: Frame Timing Stats is off in " +
                            "Player Settings, so only the wall clock above is real. Reporting " +
                            "zero for the GPU would read as free, which is the opposite of " +
                            "unknown.";
                }

                // The budget, so the numbers mean something without a second window open. A
                // headset at 90 Hz gives 11.1 ms per frame for everything, both eyes included.
                float refresh = XRSettings.enabled ? ActiveRefreshRate() : 0f;
                if (refresh > 1f)
                    line += $"\n[FrameTime]   budget {1000f / refresh:0.0} ms at {refresh:0} Hz. " +
                            "Anything over that in the 95th column is dropping frames.";

                Debug.Log(line);

                m_Wall.Clear(); m_Cpu.Clear(); m_Gpu.Clear(); m_Main.Clear();
            }

            /// <summary>Median, 95th and worst, in that order -- the three that decide anything.</summary>
            static string Describe(List<float> samples)
            {
                if (samples.Count == 0) return "no samples";

                samples.Sort();
                float median = samples[samples.Count / 2];
                float p95 = samples[Mathf.Min(samples.Count - 1, (int)(samples.Count * 0.95f))];
                float worst = samples[samples.Count - 1];

                return $"median {median:0.00} ms   95th {p95:0.00} ms   worst {worst:0.00} ms";
            }

            static float ActiveRefreshRate()
            {
                var displays = new List<XRDisplaySubsystem>();
                SubsystemManager.GetSubsystems(displays);

                foreach (XRDisplaySubsystem display in displays)
                {
                    if (!display.running) continue;
                    if (display.TryGetDisplayRefreshRate(out float rate) && rate > 1f) return rate;
                }
                return 0f;
            }
        }
    }
}
