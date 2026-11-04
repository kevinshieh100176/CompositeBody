using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using CompositeBody.Experience;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CompositeBody.Interaction.ObjectAssembly
{
    /// <summary>
    /// The glowing trail between the two halves of the ObjectAssembly object.
    ///
    /// A few LineRenderer strands, each a curve from <see cref="endA"/> to <see cref="endB"/>
    /// with a gentle sag, a loose braid around the chord and drifting noise on top, so the
    /// light floats and flows instead of hanging as a straight wire. Every displacement is
    /// shaped by sin(pi s), zero at both ends, so the trail stays attached to each half however
    /// hard it churns in the middle. The material (CompositeBody/OATrailFlow) carries the flow
    /// that travels out of both halves toward each other.
    ///
    /// The controller drives two things: <see cref="visibility"/> (fade in after the Phase 1
    /// voice-over, out when the halves join) and, optionally, <see cref="intensityOverride"/>.
    /// Otherwise <see cref="intensity"/> comes from the distance between the ends, so the closer
    /// the players bring the halves the wider, brighter, faster and more turbulent it gets.
    ///
    /// Shape and flow are pure functions of <see cref="ExperienceClock"/> and the two end
    /// positions, so both headsets draw the same trail with nothing replicated. Speed changes
    /// crossfade between layers at fixed speeds rather than scaling time -- time is the shared
    /// clock, thousands of seconds in, and multiplying it by a changing speed would make the
    /// phase leap the instant a player moved.
    ///
    /// The strands are children created by this component with HideFlags.DontSave: rebuilt on
    /// load, never written into a scene or prefab, never a diff in a saved file.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class OATrail : MonoBehaviour
    {
        public const string defaultMaterialPath = "Assets/_Interaction/ObjectAssembly/Materials/OATrailFlow.mat";
        const string k_ShaderName = "CompositeBody/OATrailFlow";
        const string k_StrandName = "OATrail Strand";

        [Header("Ends")]
        [SerializeField, Tooltip("End A -- later each half's JoinPoint child. UV.x is 0 here.")]
        Transform m_EndA;
        [SerializeField, Tooltip("End B. UV.x is 1 here.")]
        Transform m_EndB;

        [Header("State")]
        [SerializeField, Range(0f, 1f), Tooltip("0 hides the trail outright (renderers off, no " +
                                                "geometry work). Driven by the controller.")]
        float m_Visibility;

        [Header("Distance to Intensity")]
        [SerializeField, Min(0f), Tooltip("At or beyond this distance (metres) intensity is 0.")]
        float m_FarDistance = 1.6f;
        [SerializeField, Min(0f), Tooltip("At or inside this distance (metres) intensity is 1.")]
        float m_NearDistance = 0.12f;
        [SerializeField, Tooltip("Shapes the 0..1 remap of distance before smoothing.")]
        AnimationCurve m_IntensityResponse = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField, Min(0f), Tooltip("Smoothing time in seconds. 0 follows the distance exactly.")]
        float m_IntensitySmoothTime = 0.25f;

        [Header("Strands")]
        [SerializeField, Tooltip("Shared material. Never written at runtime; per-strand values " +
                                 "go through a MaterialPropertyBlock.")]
        Material m_Material;
        [SerializeField, Range(1, 4)] int m_StrandCount = 3;
        [SerializeField, Range(8, 64)] int m_PointsPerStrand = 32;
        [SerializeField, Tooltip("Width along the trail, 0 at A to 1 at B, times the width below.")]
        AnimationCurve m_WidthProfile = new(new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.6f));
        [SerializeField, Range(0.1f, 1f), Tooltip("Width of strands 2+ relative to the main one.")]
        float m_SecondaryWidth = 0.55f;
        [SerializeField, Range(0f, 1f), Tooltip("Brightness of strands 2+ relative to the main one.")]
        float m_SecondaryBrightness = 0.7f;

        [Header("Response (far -> near)")]
        [SerializeField, Min(0f), Tooltip("Main strand width in metres at intensity 0.")]
        float m_WidthFar = 0.02f;
        [SerializeField, Min(0f), Tooltip("Main strand width in metres at intensity 1.")]
        float m_WidthNear = 0.042f;
        [SerializeField, Range(0f, 2f)] float m_BrightnessFar = 0.6f;
        [SerializeField, Range(0f, 2f)] float m_BrightnessNear = 1f;
        [SerializeField, Min(0f), Tooltip("Noise displacement in metres at intensity 0.")]
        float m_TurbulenceFar = 0.018f;
        [SerializeField, Min(0f), Tooltip("Noise displacement in metres at intensity 1.")]
        float m_TurbulenceNear = 0.032f;
        [SerializeField, Min(0.01f), Tooltip("Wiggles per metre at intensity 0.")]
        float m_TurbulenceFrequencyFar = 1.4f;
        [SerializeField, Min(0.01f), Tooltip("Wiggles per metre at intensity 1.")]
        float m_TurbulenceFrequencyNear = 2.6f;
        [SerializeField, Range(0.02f, 0.5f), Tooltip("Displacement never exceeds this fraction " +
                                                     "of the distance, so a short trail does not " +
                                                     "thrash.")]
        float m_TurbulenceMaxRatio = 0.18f;

        [Header("Motion")]
        [SerializeField, Min(0f), Tooltip("How fast the wiggles travel away from each end, in " +
                                          "noise cells per second, at intensity 0.")]
        float m_FlowSpeed = 0.35f;
        [SerializeField, Min(1f), Tooltip("Flow speed multiplier at intensity 1 (crossfaded, " +
                                          "not scaled, so it never jumps).")]
        float m_FlowSpeedBoost = 2.8f;
        [SerializeField, Min(0f), Tooltip("How fast the shape itself morphs, independent of flow.")]
        float m_Evolve = 0.12f;
        [SerializeField, Range(0f, 0.3f), Tooltip("Sag as a fraction of the distance, at intensity 0.")]
        float m_SagFar = 0.07f;
        [SerializeField, Range(0f, 0.3f), Tooltip("Sag at intensity 1 -- tauter as the halves close.")]
        float m_SagNear = 0.02f;
        [SerializeField, Min(0f), Tooltip("Slow vertical float of the whole trail, metres.")]
        float m_FloatAmplitude = 0.012f;
        [SerializeField, Min(0.1f)] float m_FloatPeriod = 5.5f;
        [SerializeField, Min(0f), Tooltip("Radius the strands braid around the chord at intensity 0, metres.")]
        float m_BraidRadiusFar = 0.03f;
        [SerializeField, Min(0f), Tooltip("Braid radius at intensity 1 -- the strands gather.")]
        float m_BraidRadiusNear = 0.012f;
        [SerializeField, Range(0f, 4f), Tooltip("Braid turns along the trail.")]
        float m_BraidTwists = 1.25f;
        [SerializeField, Tooltip("Braid rotation, radians per second.")]
        float m_BraidSpin = 0.35f;

        static readonly int k_Intensity = Shader.PropertyToID("_Intensity");
        static readonly int k_Visibility = Shader.PropertyToID("_Visibility");
        static readonly int k_Brightness = Shader.PropertyToID("_Brightness");
        static readonly int k_TrailTime = Shader.PropertyToID("_TrailTime");
        static readonly int k_TrailLength = Shader.PropertyToID("_TrailLength");
        static readonly int k_StrandSeed = Shader.PropertyToID("_StrandSeed");

        [System.NonSerialized] LineRenderer[] m_Lines;
        [System.NonSerialized] Vector3[] m_Points;
        [System.NonSerialized] MaterialPropertyBlock m_Block;
        [System.NonSerialized] Material m_FallbackMaterial;

        float m_Intensity;
        float m_IntensityVelocity;
        float m_Distance = float.PositiveInfinity;
        bool m_Rendering;

        // ------------------------------------------------------------------ public API

        /// <summary>0..1. 0 turns the strands' renderers off and skips all geometry work.</summary>
        public float visibility
        {
            get => m_Visibility;
            set => m_Visibility = Mathf.Clamp01(value);
        }

        /// <summary>
        /// 0..1, smoothed. From the distance between the ends (far = 0, near = 1) unless
        /// <see cref="intensityOverride"/> is set. Updated whether or not the trail is visible.
        /// </summary>
        public float intensity => m_Intensity;

        /// <summary>When set, replaces the distance-driven target (still smoothed). Null to release.</summary>
        public float? intensityOverride { get; set; }

        /// <summary>Current distance between the ends in metres; +Infinity when an end is missing.</summary>
        public float distance => m_Distance;

        public Transform endA => m_EndA;
        public Transform endB => m_EndB;

        public void SetEnds(Transform a, Transform b)
        {
            m_EndA = a;
            m_EndB = b;
        }

        /// <summary>
        /// Snap intensity to its target and redraw now. For tooling, captures and the first frame
        /// after a teleport -- an [ExecuteAlways] LateUpdate does not fire reliably in edit mode.
        /// </summary>
        public void Rebuild()
        {
            UpdateIntensity(0f, true);
            EnsureStrands();
            Draw(ExperienceClock.now);
        }

        // ------------------------------------------------------------------ lifecycle

        void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
            SetRendering(false);
        }

        void OnDestroy()
        {
            if (m_Lines != null)
            {
                foreach (var line in m_Lines)
                    if (line != null) DestroyLater(line.gameObject);
                m_Lines = null;
            }

            if (m_FallbackMaterial != null)
            {
                DestroyLater(m_FallbackMaterial);
                m_FallbackMaterial = null;
            }
        }

#if UNITY_EDITOR
        void Reset()
        {
            m_Material = AssetDatabase.LoadAssetAtPath<Material>(defaultMaterialPath);
        }
#endif

        void OnValidate()
        {
            m_FarDistance = Mathf.Max(m_FarDistance, m_NearDistance + 0.01f);
            m_Visibility = Mathf.Clamp01(m_Visibility);
        }

        void LateUpdate()
        {
            // Edit mode updates only when something in the scene changes, so a smoothed value
            // would lag behind a dragged end until the next repaint. Snap it there instead.
            UpdateIntensity(Time.deltaTime, !Application.isPlaying);
            if (ShouldDraw()) EnsureStrands();
            Draw(ExperienceClock.now);
        }

        // XR poses (and a grabbed half with them) are written again just before rendering. Drawing
        // only in LateUpdate would leave the ends of the trail one frame behind a moving hand.
        void OnBeforeRender()
        {
            if (!isActiveAndEnabled || m_Lines == null) return;
            Draw(ExperienceClock.now);
        }

        // ------------------------------------------------------------------ intensity

        void UpdateIntensity(float dt, bool snap)
        {
            bool haveEnds = m_EndA != null && m_EndB != null;
            m_Distance = haveEnds ? Vector3.Distance(m_EndA.position, m_EndB.position) : float.PositiveInfinity;

            float target;
            if (intensityOverride.HasValue) target = Mathf.Clamp01(intensityOverride.Value);
            else if (!haveEnds) target = 0f;
            else
            {
                float raw = Mathf.InverseLerp(m_FarDistance, m_NearDistance, m_Distance);
                target = m_IntensityResponse != null && m_IntensityResponse.length > 0
                    ? Mathf.Clamp01(m_IntensityResponse.Evaluate(raw))
                    : raw;
            }

            if (snap || m_IntensitySmoothTime <= 0f)
            {
                m_Intensity = target;
                m_IntensityVelocity = 0f;
            }
            else if (dt > 0f)
            {
                m_Intensity = Mathf.Clamp01(Mathf.SmoothDamp(m_Intensity, target, ref m_IntensityVelocity,
                                                              m_IntensitySmoothTime, Mathf.Infinity, dt));
            }
        }

        // ------------------------------------------------------------------ strands

        bool ShouldDraw() => m_Visibility > 0.001f && m_EndA != null && m_EndB != null;

        void EnsureStrands()
        {
            int count = Mathf.Clamp(m_StrandCount, 1, 4);
            if (m_Lines != null && m_Lines.Length == count && System.Array.TrueForAll(m_Lines, l => l != null))
            {
                ApplyMaterial();
                return;
            }

            // Reuse strands that survived a domain reload (they are scene objects; only our
            // references to them were lost), drop any extras, create the rest.
            var found = new List<LineRenderer>();
            var extras = new List<GameObject>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (!child.name.StartsWith(k_StrandName)) continue;
                var line = child.GetComponent<LineRenderer>();
                if (line != null && found.Count < count) found.Add(line);
                else extras.Add(child.gameObject);
            }
            foreach (var go in extras) DestroyNow(go);

            while (found.Count < count)
            {
                var go = new GameObject(k_StrandName);
                go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                found.Add(go.AddComponent<LineRenderer>());
            }

            m_Lines = found.ToArray();
            for (int i = 0; i < m_Lines.Length; i++)
            {
                m_Lines[i].gameObject.name = $"{k_StrandName} {i}";
                ConfigureLine(m_Lines[i]);
            }
            m_Rendering = true; // force SetRendering to push the state to the new lines
            SetRendering(false);
            ApplyMaterial();
        }

        void ConfigureLine(LineRenderer line)
        {
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.loop = false;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            line.allowOcclusionWhenDynamic = false;
            line.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) },
            };
            line.widthCurve = m_WidthProfile != null && m_WidthProfile.length > 0
                ? m_WidthProfile
                : AnimationCurve.Constant(0f, 1f, 1f);
        }

        void ApplyMaterial()
        {
            var mat = ResolveMaterial();
            foreach (var line in m_Lines)
                if (line.sharedMaterial != mat) line.sharedMaterial = mat;
        }

        Material ResolveMaterial()
        {
            if (m_Material != null) return m_Material;
            if (m_FallbackMaterial == null)
            {
                var shader = Shader.Find(k_ShaderName);
                if (shader == null) return null;
                m_FallbackMaterial = new Material(shader) { name = "OATrailFlow (fallback)", hideFlags = HideFlags.DontSave };
                Debug.LogWarning($"[OATrail] {name}: no material assigned, using a default {k_ShaderName} material.", this);
            }
            return m_FallbackMaterial;
        }

        void SetRendering(bool on)
        {
            if (m_Lines == null || on == m_Rendering) return;
            m_Rendering = on;
            foreach (var line in m_Lines)
                if (line != null) line.enabled = on;
        }

        // ------------------------------------------------------------------ drawing

        void Draw(float t)
        {
            if (m_Lines == null) return;
            if (!ShouldDraw())
            {
                SetRendering(false);
                return;
            }

            int n = Mathf.Clamp(m_PointsPerStrand, 8, 64);
            if (m_Points == null || m_Points.Length != n) m_Points = new Vector3[n];
            m_Block ??= new MaterialPropertyBlock();

            Vector3 a = m_EndA.position;
            Vector3 b = m_EndB.position;
            Vector3 chord = b - a;
            float len = chord.magnitude;
            Vector3 dir = len > 1e-5f ? chord / len : Vector3.right;

            // A frame around the chord: "up" is world up with the chord taken out of it, so the
            // sag always hangs toward the floor.
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, dir);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.forward, dir);
            up.Normalize();
            Vector3 side = Vector3.Cross(dir, up);

            float k = m_Intensity;
            float sag = len * Mathf.Lerp(m_SagFar, m_SagNear, k);
            float turb = Mathf.Min(Mathf.Lerp(m_TurbulenceFar, m_TurbulenceNear, k), len * m_TurbulenceMaxRatio);
            float braid = Mathf.Min(Mathf.Lerp(m_BraidRadiusFar, m_BraidRadiusNear, k), len * 0.12f);
            float floatAmp = Mathf.Min(m_FloatAmplitude, len * 0.1f);
            float freq = Mathf.Lerp(m_TurbulenceFrequencyFar, m_TurbulenceFrequencyNear, k) * len;

            // Fixed-speed layers, crossfaded by intensity and normalised by their RMS weight.
            // The +offsets keep the Perlin coordinates positive from the first second.
            float slow = t * m_FlowSpeed + 101.3f;
            float fast = t * m_FlowSpeed * m_FlowSpeedBoost + 307.9f;
            float wSlow = 1f - k, wFast = k;
            float layerNorm = 1f / Mathf.Max(Mathf.Sqrt(wSlow * wSlow + wFast * wFast), 1e-3f);
            float evolve = t * m_Evolve;

            int count = m_Lines.Length;
            float width = Mathf.Lerp(m_WidthFar, m_WidthNear, k);
            float brightness = Mathf.Lerp(m_BrightnessFar, m_BrightnessNear, k);

            for (int i = 0; i < count; i++)
            {
                float seed = 13.7f * i + 5.3f;
                float phase = i * (Mathf.PI * 2f / count);
                float bob = Mathf.Sin(t * (Mathf.PI * 2f / m_FloatPeriod) + phase * 0.35f) * floatAmp;
                float arc = 0f;

                for (int p = 0; p < n; p++)
                {
                    float s = p / (n - 1f);
                    float env = Mathf.Sin(Mathf.PI * s);

                    float nx = Wiggle(s, freq, slow, fast, wSlow, wFast, layerNorm, seed + evolve);
                    float ny = Wiggle(s, freq, slow, fast, wSlow, wFast, layerNorm, seed + 31.1f + evolve * 0.87f);

                    float theta = phase + s * m_BraidTwists * Mathf.PI * 2f + t * m_BraidSpin;
                    float ox = Mathf.Cos(theta) * braid + nx * turb;
                    float oy = Mathf.Sin(theta) * braid + ny * turb + bob;

                    Vector3 pos = a + chord * s
                                  + (side * ox + up * oy) * env
                                  - up * (sag * 4f * s * (1f - s));
                    if (p > 0) arc += Vector3.Distance(m_Points[p - 1], pos);
                    m_Points[p] = pos;
                }

                // The ends are exact: the trail starts and stops on the join points.
                m_Points[0] = a;
                m_Points[n - 1] = b;

                var line = m_Lines[i];
                line.positionCount = n;
                line.SetPositions(m_Points);
                line.widthMultiplier = width * (i == 0 ? 1f : m_SecondaryWidth);

                line.GetPropertyBlock(m_Block);
                m_Block.SetFloat(k_Intensity, k);
                m_Block.SetFloat(k_Visibility, m_Visibility);
                m_Block.SetFloat(k_Brightness, brightness * (i == 0 ? 1f : m_SecondaryBrightness));
                m_Block.SetFloat(k_TrailTime, t);
                m_Block.SetFloat(k_TrailLength, Mathf.Max(arc, 1e-3f));
                m_Block.SetFloat(k_StrandSeed, seed);
                line.SetPropertyBlock(m_Block);
            }

            SetRendering(true);
        }

        /// <summary>
        /// Signed noise in roughly -1..1 at position s along the trail. Two flows, one anchored at
        /// each end and travelling away from it, blended by which end is nearer -- the geometric
        /// echo of the shader's two currents meeting in the middle.
        /// </summary>
        static float Wiggle(float s, float freq, float slow, float fast, float wSlow, float wFast,
                            float layerNorm, float seed)
        {
            float fromA = s * freq;
            float fromB = (1f - s) * freq;

            float a = Layer(fromA, slow, fast, wSlow, wFast, layerNorm, seed);
            float b = Layer(fromB, slow, fast, wSlow, wFast, layerNorm, seed + 57.3f);

            float wa = 1f - s, wb = s;
            return (a * wa + b * wb) / Mathf.Max(Mathf.Sqrt(wa * wa + wb * wb), 1e-3f);
        }

        // scroll - fromEnd: a feature at a fixed coordinate moves to larger fromEnd as time
        // passes, i.e. away from its own end.
        static float Layer(float fromEnd, float slow, float fast, float wSlow, float wFast, float layerNorm, float seed)
        {
            float v = 0f;
            if (wSlow > 0f) v += (Mathf.PerlinNoise(slow - fromEnd, seed) * 2f - 1f) * wSlow;
            if (wFast > 0f) v += (Mathf.PerlinNoise(fast - fromEnd * 1.31f, seed + 7.7f) * 2f - 1f) * wFast;
            return v * layerNorm;
        }

        // ------------------------------------------------------------------ util

        static void DestroyNow(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        // Teardown runs while Unity may already be destroying the hierarchy (scene close, the
        // parent object being deleted), where DestroyImmediate logs "Destroying object multiple
        // times". Defer it in edit mode; whatever the scene already took is null by then.
        static void DestroyLater(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
            {
                Destroy(obj);
                return;
            }
#if UNITY_EDITOR
            EditorApplication.delayCall += () =>
            {
                if (obj != null) DestroyImmediate(obj);
            };
#else
            DestroyImmediate(obj);
#endif
        }

        void OnDrawGizmosSelected()
        {
            if (m_EndA == null || m_EndB == null) return;
            Gizmos.color = Color.Lerp(new Color(0.4f, 0.3f, 0.2f), new Color(1f, 0.8f, 0.4f), m_Intensity);
            Gizmos.DrawLine(m_EndA.position, m_EndB.position);
            Gizmos.DrawWireSphere(m_EndA.position, 0.01f);
            Gizmos.DrawWireSphere(m_EndB.position, 0.01f);
        }
    }
}
