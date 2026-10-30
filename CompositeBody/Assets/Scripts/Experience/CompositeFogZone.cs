using System.Collections.Generic;
using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// The air in the room: height fog, and the colour the fixtures put into it.
    ///
    /// One of these per scene. It drives the globals that <c>CompositeFog.hlsl</c> reads, so
    /// every hand-written shader in the show fogs itself the same way at no extra pass and with
    /// no depth texture. See that file for why this is not a fullscreen effect.
    ///
    /// The point of it is the pairing. A stage beam is only visible because there is haze in the
    /// room, so the haze and the beams are one physical thing and should be one dial: raise
    /// <see cref="m_Haze"/> and the air thickens AND the cones brighten together. Each
    /// <see cref="VolumetricSpot"/> also registers as a light the fog can pick up, which is what
    /// gives a 真人 a 光圈 in the air beyond the edge of the cone itself -- stock Unity fog has no
    /// light interaction at all, and that absence is most of why an unlit room reads as flat.
    ///
    /// Stock <see cref="RenderSettings"/> fog is kept in step rather than switched off, because
    /// the URP Lit shaders in a scene can only receive that one. The conversion is in
    /// <see cref="MatchStockFog"/>, and the disagreement that remains is bounded: stock fog has
    /// no height term, so it is only exactly right at floor level -- which, for a floor, is
    /// where it is.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class CompositeFogZone : MonoBehaviour
    {
        [Header("Air")]
        [SerializeField, Tooltip("The colour of the air itself, before any fixture tints it.")]
        Color m_Color = new(0.165f, 0.168f, 0.182f, 1f);

        [SerializeField, Range(0f, 3f), Tooltip("Master dial. Scales the fog density and, when " +
                                                "the toggle below is on, the beams with it -- a " +
                                                "hazer feeds both.")]
        float m_Haze = 1f;

        [SerializeField, Range(0f, 1f), Tooltip("Density per metre at floor height.")]
        float m_Density = 0.085f;

        [Header("Height")]
        [SerializeField, Tooltip("World Y the fog is densest at. Normally the floor.")]
        float m_FloorHeight;

        [SerializeField, Min(0.05f), Tooltip("Metres over which the density falls by 1/e. Small " +
                                             "is a shallow ground layer; large is a uniform room.")]
        float m_FalloffHeight = 2.4f;

        [SerializeField, Range(0f, 1f), Tooltip("Ceiling on how opaque the air can ever get. " +
                                                "Below 1 the far distance stays readable instead " +
                                                "of going to a flat wall of colour.")]
        float m_MaxOpacity = 0.93f;

        [Header("Fixtures")]
        [SerializeField, Tooltip("Let the stage lights tint the air around them. This is the " +
                                 "part stock fog cannot do.")]
        bool m_CollectSpots = true;

        [SerializeField, Range(0f, 2f), Tooltip("How far each beam's glow reaches into the air, " +
                                                "as a multiple of its throw.")]
        float m_GlowRadius = 0.75f;

        [SerializeField, Range(0f, 2f), Tooltip("How strongly. Keep it low -- O-0 asks for " +
                                                "「完全灰色的空間」, and fog that takes the lamp's " +
                                                "colour everywhere stops being grey.")]
        float m_GlowStrength = 0.4f;

        [SerializeField, Tooltip("Scale every beam's density by Haze, so one dial moves the air " +
                                 "and the light in it together.")]
        bool m_DriveBeamDensity = true;

        [Header("Stock fog")]
        [SerializeField, Tooltip("Keep RenderSettings fog in agreement, for the stock URP " +
                                 "shaders that can only receive that one.")]
        bool m_MatchStockFog = true;

        // Four is the array size CompositeFog.hlsl declares. More fixtures than this in one
        // frame is a real possibility later in the show, and the honest answer then is to pick
        // the four nearest the camera rather than to widen the loop every shader pays for.
        const int k_MaxLights = 4;

        static readonly int k_ColorId = Shader.PropertyToID("_CompositeFogColor");
        static readonly int k_ParamsId = Shader.PropertyToID("_CompositeFogParams");
        static readonly int k_LightPosId = Shader.PropertyToID("_CompositeFogLightPos");
        static readonly int k_LightColorId = Shader.PropertyToID("_CompositeFogLightColor");
        static readonly int k_LightCountId = Shader.PropertyToID("_CompositeFogLightCount");

        readonly Vector4[] m_LightPos = new Vector4[k_MaxLights];
        readonly Vector4[] m_LightColor = new Vector4[k_MaxLights];

        /// <summary>Master haze. Animate this for a cue; the beams follow.</summary>
        public float haze
        {
            get => m_Haze;
            set { m_Haze = Mathf.Max(0f, value); Apply(); }
        }

        void OnEnable() => Apply();
        void OnValidate() { if (isActiveAndEnabled) Apply(); }
        void LateUpdate() => Apply();

        /// <summary>
        /// Push the globals now, rather than waiting for a LateUpdate that may never come.
        ///
        /// Same reason as <see cref="VolumetricSpot.Rebuild"/>: in batch mode there is no scene
        /// view to repaint, so [ExecuteAlways] does not run. An editor tool should call this
        /// AFTER building the fixtures, so they are on the roster when the lights are collected.
        /// </summary>
        public void Rebuild() => Apply();

        void OnDisable()
        {
            // Globals outlive the scene that set them. Without this, loading a scene with no
            // zone in it would leave every shader fogging against whatever the last one said.
            Shader.SetGlobalColor(k_ColorId, new Color(0f, 0f, 0f, 0f));
            Shader.SetGlobalFloat(k_LightCountId, 0f);
            foreach (VolumetricSpot spot in VolumetricSpot.active) spot.hazeScale = 1f;
        }

        void Apply()
        {
            float density = m_Density * m_Haze;

            Shader.SetGlobalColor(k_ColorId, new Color(m_Color.r, m_Color.g, m_Color.b,
                                                       m_Haze > 0f ? 1f : 0f));
            Shader.SetGlobalVector(k_ParamsId, new Vector4(density,
                                                           1f / Mathf.Max(m_FalloffHeight, 0.05f),
                                                           m_FloorHeight,
                                                           m_MaxOpacity));
            CollectLights();
            if (m_MatchStockFog) MatchStockFog(density);
        }

        /// <summary>
        /// Hand the shaders the fixtures whose glow they should pick up.
        ///
        /// The spots keep a static roster rather than being searched for: FindObjectsByType in
        /// LateUpdate would walk the scene every frame, and a beat that switches a fixture on
        /// halfway through would otherwise not appear until something forced a refresh.
        /// </summary>
        void CollectLights()
        {
            int count = 0;

            if (m_CollectSpots && m_GlowStrength > 0f)
            {
                IReadOnlyList<VolumetricSpot> spots = VolumetricSpot.active;
                for (int i = 0; i < spots.Count && count < k_MaxLights; i++)
                {
                    VolumetricSpot spot = spots[i];
                    if (spot == null || spot.reveal <= 0.002f) continue;

                    // The component's own position is the middle of the beam, which is where a
                    // halo in the air belongs -- at the lamp it would be a point of light above
                    // the figure rather than a glow around them.
                    Color colour = spot.effectiveColor;
                    m_LightPos[count] = new Vector4(spot.transform.position.x,
                                                    spot.transform.position.y,
                                                    spot.transform.position.z,
                                                    Mathf.Max(spot.range * m_GlowRadius, 0.01f));
                    m_LightColor[count] = new Vector4(colour.r, colour.g, colour.b,
                                                      m_GlowStrength * spot.reveal * m_Haze);
                    count++;
                }
            }

            for (int i = count; i < k_MaxLights; i++)
            {
                m_LightPos[i] = Vector4.zero;
                m_LightColor[i] = Vector4.zero;
            }

            Shader.SetGlobalVectorArray(k_LightPosId, m_LightPos);
            Shader.SetGlobalVectorArray(k_LightColorId, m_LightColor);
            Shader.SetGlobalFloat(k_LightCountId, count);

            if (!m_DriveBeamDensity) return;
            foreach (VolumetricSpot spot in VolumetricSpot.active)
            {
                if (spot != null) spot.hazeScale = m_Haze;
            }
        }

        /// <summary>
        /// Put RenderSettings fog where it agrees with the height fog at floor level.
        ///
        /// The two curves cannot be made equal -- this one is 1 - exp(-rho L) along a level ray
        /// and Unity's exponential-squared is 1 - exp(-(d L)^2) -- so they are matched at the
        /// distance where each reaches half opacity, which is the distance the eye reads as "how
        /// foggy is it". Solving both for that distance gives d = rho / sqrt(ln 2), the constant
        /// below. Matched by eye instead, the stock-shader floor always ends up visibly clearer
        /// or murkier than everything standing on it.
        /// </summary>
        void MatchStockFog(float density)
        {
            const float k_HalfOpacityMatch = 1.2011224f;   // 1 / sqrt(ln 2)

            RenderSettings.fog = density > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = m_Color;
            RenderSettings.fogDensity = density * k_HalfOpacityMatch;
        }

        void OnDrawGizmosSelected()
        {
            // The fog's base plane and the height one 1/e above it, so the layer is something
            // you can see and aim rather than two numbers in a panel.
            Gizmos.color = new Color(m_Color.r, m_Color.g, m_Color.b, 0.6f);
            var centre = new Vector3(transform.position.x, m_FloorHeight, transform.position.z);
            Gizmos.DrawWireCube(centre, new Vector3(8f, 0.001f, 8f));
            centre.y = m_FloorHeight + m_FalloffHeight;
            Gizmos.DrawWireCube(centre, new Vector3(8f, 0.001f, 8f));
        }
    }
}
