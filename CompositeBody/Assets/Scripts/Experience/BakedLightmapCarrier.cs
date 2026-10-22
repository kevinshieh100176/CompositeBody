using System.Collections.Generic;
using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Lets a prefab keep the lighting it was baked with.
    ///
    /// THE PROBLEM. A renderer stores two things about its lightmap -- an INDEX and a patch of
    /// UV -- and both of them do travel into a prefab. What does not travel is the thing the
    /// index points AT: LightmapSettings.lightmaps belongs to the scene, loaded from its
    /// LightingData asset. Drop the prefab into a scene that was never baked and the renderer
    /// still asks for "lightmap 37" of an array that is empty. The bake is not lost, it is
    /// unaddressable.
    ///
    /// So this carries the array as well. Each carrier stores the lightmaps its own renderers
    /// use, numbered from zero within itself, and at runtime they are appended to whatever is
    /// already loaded and every index is rebased onto its new home.
    ///
    /// REBUILT WHOLE, NEVER PATCHED. Removing one carrier's entries would shift every index
    /// after them, so the naive version of this works until the second scene unloads and then
    /// silently relights everything with the wrong maps. Instead the global array is rebuilt
    /// from the set of active carriers each time that set changes, and all of them are
    /// reassigned. Nothing tracks an offset across time, so nothing can drift out of step, and
    /// a scene's worth of lighting can be unloaded without disturbing the others.
    ///
    /// The scene's own lightmaps, if it has any, stay first in the array and are never
    /// rewritten, so the renderers belonging to the scene keep working.
    ///
    /// WHAT THIS DOES NOT SOLVE. Every carrier that is active keeps its textures resident. Three
    /// scenes' worth of bake is three scenes' worth of VRAM, all at once, and S1_v3 alone is
    /// 64 lightmaps. Disabling a carrier releases its claim and rebuilds without it, so
    /// unloading is possible -- but it has to be done, it does not happen by itself the way it
    /// would with an additively loaded scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class BakedLightmapCarrier : MonoBehaviour
    {
        /// <summary>One renderer and where it looked in the bake, numbered from zero HERE.</summary>
        [System.Serializable]
        struct Entry
        {
            public Renderer renderer;
            public int index;
            public Vector4 scaleOffset;
        }

        /// <summary>
        /// One lightmap, as the three textures a LightmapData is made of. Stored as direct
        /// references: they are assets, so a prefab holds on to them the same way it holds a
        /// material, and nothing has to be found by name at load.
        /// </summary>
        [System.Serializable]
        struct Maps
        {
            public Texture2D color;
            public Texture2D direction;
            public Texture2D shadowMask;
        }

        [SerializeField, Tooltip("Filled in by 'Capture Baked Lightmaps' below, with the scene " +
                                 "that was baked open. Re-capture after any re-bake.")]
        Entry[] m_Entries = new Entry[0];

        [SerializeField] Maps[] m_Lightmaps = new Maps[0];

        /// <summary>Where this carrier's maps start in the global array, as of the last rebuild.</summary>
        int m_Offset;

        static readonly List<BakedLightmapCarrier> s_Active = new();

        /// <summary>
        /// Whatever the scene had when the first carrier joined, kept first in the array and
        /// handed back when the last one leaves.
        ///
        /// The scene's own renderers index into this, and they are not ours to rewrite -- if a
        /// baked scene is open and a carrier is added to it, moving the scene's maps would
        /// relight the scene itself.
        /// </summary>
        static LightmapData[] s_SceneBase;
        static bool s_BaseCaptured;

        void OnEnable()
        {
            if (m_Entries.Length == 0 || m_Lightmaps.Length == 0) return;

            if (!s_BaseCaptured)
            {
                s_SceneBase = LightmapSettings.lightmaps;
                s_BaseCaptured = true;
            }

            if (!s_EnvCaptured)
            {
                s_EnvBase = Environment.Read();
                s_EnvCaptured = true;
            }

            if (!s_Active.Contains(this)) s_Active.Add(this);
            Rebuild();
        }

        void OnDisable()
        {
            if (!s_Active.Remove(this)) return;

            // HAND THE ARRAY BACK when the last carrier goes. Holding on to a base captured at
            // some earlier moment is how this gets subtly wrong: the first carrier might have
            // enabled in a scene with no bake at all, and a scene loaded additively afterwards
            // brings its own lightmaps that we would then be treating as ours to append to.
            // While any carrier is active the array is ours; when none are, it is not.
            if (s_Active.Count == 0)
            {
                LightmapSettings.lightmaps = s_SceneBase ?? new LightmapData[0];
                s_SceneBase = null;
                s_BaseCaptured = false;

                if (s_EnvCaptured) s_EnvBase.Write();
                s_EnvCaptured = false;
                s_WarnedTwoEnvironments = false;
                return;
            }

            Rebuild();
        }

        /// <summary>
        /// Rebuild the global array from the scene's own maps plus every active carrier, then
        /// point all of their renderers at the result.
        /// </summary>
        static void Rebuild()
        {
            var all = new List<LightmapData>(s_SceneBase ?? new LightmapData[0]);

            foreach (BakedLightmapCarrier carrier in s_Active)
            {
                carrier.m_Offset = all.Count;

                foreach (Maps maps in carrier.m_Lightmaps)
                {
                    all.Add(new LightmapData
                    {
                        lightmapColor = maps.color,
                        lightmapDir = maps.direction,
                        shadowMask = maps.shadowMask,
                    });
                }
            }

            LightmapSettings.lightmaps = all.ToArray();

            foreach (BakedLightmapCarrier carrier in s_Active) carrier.Apply();

            ApplyEnvironment();
        }

        void Apply()
        {
            foreach (Entry entry in m_Entries)
            {
                if (entry.renderer == null) continue;

                entry.renderer.lightmapIndex = entry.index + m_Offset;
                entry.renderer.lightmapScaleOffset = entry.scaleOffset;
            }
        }

        // --- the environment ----------------------------------------------------------------

        [SerializeField, Tooltip("Also carry the scene's fog, ambient light and skybox.\n\n" +
                                 "These are ONE SET PER SCENE, not per object, so only one " +
                                 "carrier can own them at a time. Leave this off for props and " +
                                 "set it on the environment itself -- a chair should not have " +
                                 "an opinion about the fog.")]
        bool m_CarryEnvironment = true;

        /// <summary>
        /// A scene's lighting environment, which lives in RenderSettings and is global.
        ///
        /// The scenes differ sharply here -- S1_v3 sits in pale grey fog, MainScene in something
        /// almost black -- so geometry moved from one to the other without this arrives wearing
        /// the wrong weather, which reads as the model being wrong rather than the scene.
        /// </summary>
        [System.Serializable]
        struct Environment
        {
            public bool captured;

            public bool fog;
            public Color fogColor;
            public FogMode fogMode;
            public float fogDensity, fogStart, fogEnd;

            public UnityEngine.Rendering.AmbientMode ambientMode;
            public Color ambientSky, ambientEquator, ambientGround;
            public float ambientIntensity;
            public Material skybox;
            public float reflectionIntensity;

            /// <summary>
            /// The ambient probe itself, 27 floats, because the colour fields above do not
            /// reproduce it. With ambient mode set to Skybox and no skybox assigned -- which is
            /// how S1_v3 is set up -- the probe is the only place the ambient light exists.
            /// </summary>
            public float[] probe;

            public static Environment Read()
            {
                var e = new Environment
                {
                    captured = true,
                    fog = RenderSettings.fog,
                    fogColor = RenderSettings.fogColor,
                    fogMode = RenderSettings.fogMode,
                    fogDensity = RenderSettings.fogDensity,
                    fogStart = RenderSettings.fogStartDistance,
                    fogEnd = RenderSettings.fogEndDistance,
                    ambientMode = RenderSettings.ambientMode,
                    ambientSky = RenderSettings.ambientSkyColor,
                    ambientEquator = RenderSettings.ambientEquatorColor,
                    ambientGround = RenderSettings.ambientGroundColor,
                    ambientIntensity = RenderSettings.ambientIntensity,
                    skybox = RenderSettings.skybox,
                    reflectionIntensity = RenderSettings.reflectionIntensity,
                    probe = new float[27],
                };

                UnityEngine.Rendering.SphericalHarmonicsL2 sh = RenderSettings.ambientProbe;
                for (int rgb = 0; rgb < 3; rgb++)
                    for (int coefficient = 0; coefficient < 9; coefficient++)
                        e.probe[rgb * 9 + coefficient] = sh[rgb, coefficient];

                return e;
            }

            public void Write()
            {
                if (!captured) return;

                RenderSettings.fog = fog;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = fogStart;
                RenderSettings.fogEndDistance = fogEnd;

                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientSkyColor = ambientSky;
                RenderSettings.ambientEquatorColor = ambientEquator;
                RenderSettings.ambientGroundColor = ambientGround;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.skybox = skybox;
                RenderSettings.reflectionIntensity = reflectionIntensity;

                if (probe == null || probe.Length != 27) return;

                var sh = new UnityEngine.Rendering.SphericalHarmonicsL2();
                for (int rgb = 0; rgb < 3; rgb++)
                    for (int coefficient = 0; coefficient < 9; coefficient++)
                        sh[rgb, coefficient] = probe[rgb * 9 + coefficient];

                // Set last: assigning the ambient mode or the skybox above makes Unity
                // regenerate the probe, which would overwrite the one that was baked.
                RenderSettings.ambientProbe = sh;
            }
        }

        [SerializeField] Environment m_Environment;

        static Environment s_EnvBase;
        static bool s_EnvCaptured;
        static bool s_WarnedTwoEnvironments;

        /// <summary>
        /// Hand the environment to the carrier that most recently came on, and say so when more
        /// than one is asking.
        ///
        /// There is no right answer with two of them active -- the fog cannot be two colours --
        /// so rather than pick quietly and let it look like a bad bake, this names them. The
        /// show never has two environments up at once, so hearing this means something is
        /// enabled that should not be.
        /// </summary>
        static void ApplyEnvironment()
        {
            BakedLightmapCarrier owner = null;
            int asking = 0;

            foreach (BakedLightmapCarrier carrier in s_Active)
            {
                if (!carrier.m_CarryEnvironment || !carrier.m_Environment.captured) continue;

                owner = carrier;
                asking++;
            }

            if (asking > 1 && !s_WarnedTwoEnvironments)
            {
                s_WarnedTwoEnvironments = true;
                Debug.LogWarning($"[Lightmaps] {asking} carriers are each carrying an " +
                                 "environment, and the fog, ambient and skybox are one set per " +
                                 $"scene. '{owner.name}' wins because it came on last. Only one " +
                                 "environment should be enabled at a time, or the others should " +
                                 "have Carry Environment switched off.", owner);
            }

            if (owner != null) owner.m_Environment.Write();
            else if (s_EnvCaptured) s_EnvBase.Write();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Read the bake off the open scene and store it here.
        ///
        /// Run this with the baked scene open, on the object that is to become the prefab, and
        /// run it again after any re-bake: the indices and the UV patches both change, and a
        /// stale capture will light the model with somebody else's lightmap rather than fail.
        /// </summary>
        [ContextMenu("Capture Baked Lightmaps")]
        void Capture()
        {
            LightmapData[] scene = LightmapSettings.lightmaps;
            if (scene == null || scene.Length == 0)
            {
                Debug.LogWarning("[Lightmaps] This scene has no baked lightmaps loaded, so there " +
                                 "is nothing to capture. Open the scene that was baked, and " +
                                 "check that its lighting data is not still being generated.",
                                 this);
                return;
            }

            // Only the maps actually used are kept, renumbered from zero, so a prefab that
            // touches four of sixty-four lightmaps carries four.
            var used = new Dictionary<int, int>();
            var entries = new List<Entry>();

            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                int index = r.lightmapIndex;

                // 65534 and 65535 are Unity's "no lightmap" and "probe lit" markers, not indices.
                if (index < 0 || index >= 65534 || index >= scene.Length) continue;

                if (!used.TryGetValue(index, out int local))
                {
                    local = used.Count;
                    used[index] = local;
                }

                entries.Add(new Entry
                {
                    renderer = r,
                    index = local,
                    scaleOffset = r.lightmapScaleOffset,
                });
            }

            if (entries.Count == 0)
            {
                Debug.LogWarning("[Lightmaps] None of the renderers under this object are " +
                                 "lightmapped, so nothing was captured. They are probably not " +
                                 "marked Contribute GI, or the bake has not been run.", this);
                return;
            }

            var maps = new Maps[used.Count];
            foreach (KeyValuePair<int, int> pair in used)
            {
                LightmapData data = scene[pair.Key];
                maps[pair.Value] = new Maps
                {
                    color = data.lightmapColor,
                    direction = data.lightmapDir,
                    shadowMask = data.shadowMask,
                };
            }

            m_Entries = entries.ToArray();
            m_Lightmaps = maps;
            if (m_CarryEnvironment) m_Environment = Environment.Read();

            UnityEditor.EditorUtility.SetDirty(this);

            Debug.Log($"[Lightmaps] captured {m_Entries.Length} renderer(s) using " +
                      $"{m_Lightmaps.Length} of the scene's {scene.Length} lightmap(s). " +
                      "They are applied when this object is enabled in play mode; in the editor " +
                      "the prefab will look unlit until then, which is expected and not a " +
                      "failed capture.", this);
        }

        /// <summary>Say what is stored, without changing it.</summary>
        [ContextMenu("Report What Is Stored")]
        void ReportStored()
        {
            if (m_Entries.Length == 0)
            {
                Debug.Log("[Lightmaps] nothing captured yet.", this);
                return;
            }

            long bytes = 0;
            foreach (Maps m in m_Lightmaps)
            {
                if (m.color != null) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m.color);
                if (m.direction != null) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m.direction);
                if (m.shadowMask != null) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m.shadowMask);
            }

            int missing = 0;
            foreach (Entry e in m_Entries) if (e.renderer == null) missing++;

            Debug.Log($"[Lightmaps] {m_Entries.Length} renderer(s), {m_Lightmaps.Length} " +
                      $"lightmap(s), about {bytes / (1024f * 1024f):0.0} MB resident while this " +
                      "is enabled." +
                      (missing > 0
                          ? $"\n[Lightmaps] {missing} entr(ies) have lost their renderer and will " +
                            "be skipped. Re-capture."
                          : ""), this);
        }
#endif
    }
}
