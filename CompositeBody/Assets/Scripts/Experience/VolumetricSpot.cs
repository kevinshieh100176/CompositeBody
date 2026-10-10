using System.Collections.Generic;
using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// One stage light: the visible cone of a spot, for O-0's 光圈 and anything later that wants
    /// a beam rather than a wash.
    ///
    /// Pairs with a real <see cref="Light"/> rather than replacing it. The Light is what actually
    /// falls on a figure and on the floor; this is the air in between. Drag the Light into
    /// <see cref="m_MatchLight"/> and the cone takes its angle, range, colour and aim every
    /// frame, so there is one place to point a fixture and the beam cannot drift out of
    /// agreement with the light it is supposed to be.
    ///
    /// Sizing is authored in metres and degrees and converted to the proxy's scale, because the
    /// shader inscribes the cone in a unit cube -- which also means the SCALE ENCODES THE ANGLE
    /// and the shader needs no angle parameter. Nobody should have to know that to hang a lamp.
    ///
    /// Phase comes from <see cref="ExperienceClock"/>, like <see cref="DistantSoundBed"/>, the
    /// point clouds and <see cref="VolumetricColumn"/>, so the haze drifts identically on both
    /// headsets. Two players standing either side of the same beam would otherwise watch it
    /// breathe out of step.
    ///
    /// Values go through a MaterialPropertyBlock so several fixtures can share one material
    /// asset without writing into it, which is the convention the membrane and point-cloud
    /// renderers already follow. The cost is that a renderer with a property block is not
    /// SRP-batcher compatible -- irrelevant at two fixtures, worth knowing at twenty.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public class VolumetricSpot : MonoBehaviour
    {
        [Header("Fixture")]
        [SerializeField, Tooltip("The real light this beam belongs to. When set, angle, range, " +
                                 "colour and aim are taken from it every frame and the fields " +
                                 "below are driven rather than authored.")]
        Light m_MatchLight;

        [SerializeField, Min(0.1f), Tooltip("Throw distance in metres, apex to the far end.")]
        float m_Range = 3.2f;

        [SerializeField, Range(1f, 160f), Tooltip("Full cone angle in degrees, same convention " +
                                                  "as Light.spotAngle.")]
        float m_ConeAngle = 34f;

        [Header("Colour")]
        [SerializeField, Tooltip("Take the colour from a player role instead of the field below.")]
        bool m_UseRoleColor;

        [SerializeField, Tooltip("Which role's colour, when the toggle above is on. A 真人's " +
                                 "光圈 is the colour of the player who faces them.")]
        PlayerRole m_Role = PlayerRole.Player1;

        [SerializeField]
        Color m_Color = new(0.62f, 0.22f, 1f, 1f);

        [SerializeField, Range(0f, 8f)]
        float m_Intensity = 1.8f;

        [SerializeField, Range(0f, 4f), Tooltip("Haze density per metre of beam.")]
        float m_Density = 1f;

        [Header("Scattering")]
        [SerializeField, Range(0f, 0.9f), Tooltip("Forward scattering. Higher flares the beam " +
                                                  "when you look back up it and fades it when " +
                                                  "you look across it.")]
        float m_Anisotropy = 0.65f;

        [SerializeField, Range(4, 32), Tooltip("Raymarch samples across the beam. Fixed cost " +
                                               "per covered pixel -- this is the frame-time dial.")]
        int m_Steps = 12;

        [Header("Occlusion")]
        [SerializeField, Tooltip("Integrate against scene depth instead of letting the depth " +
                                 "test do it. On is correct and is the default: a proxy box is " +
                                 "always slightly larger than the cone inside it, so where the " +
                                 "box is buried in the floor the depth test rejects the exit " +
                                 "fragment and cuts a hard wedge out of the beam above it. " +
                                 "REQUIRES a depth texture -- without one, turning this on makes " +
                                 "the beam draw over everything, because it also switches the " +
                                 "depth test off.")]
        bool m_SceneDepthOcclusion = true;

        [Header("State")]
        [SerializeField, Range(0f, 1f), Tooltip("0 is off, 1 is full. Fade it with FadeTo for a cue.")]
        float m_Reveal = 1f;

        static readonly int k_Color = Shader.PropertyToID("_Color");
        static readonly int k_Intensity = Shader.PropertyToID("_Intensity");
        static readonly int k_Density = Shader.PropertyToID("_Density");
        static readonly int k_Anisotropy = Shader.PropertyToID("_Anisotropy");
        static readonly int k_Steps = Shader.PropertyToID("_Steps");
        static readonly int k_Phase = Shader.PropertyToID("_Phase");
        static readonly int k_Reveal = Shader.PropertyToID("_Reveal");

        // The shader inscribes the cone in the unit cube at 90% of it, so the authored metres
        // describe the light rather than the box around it. See the shader header for why the
        // slack is not optional.
        const float k_Inset = 0.9f;

        MeshRenderer m_Renderer;
        MaterialPropertyBlock m_Block;

        float m_LastRange = -1f, m_LastAngle = -1f;
        float m_FadeFrom, m_FadeTo, m_FadeStart, m_FadeSeconds;
        float m_HazeScale = 1f;

        // A roster rather than a search. CompositeFogZone needs the live fixtures every frame to
        // tint the air around them, and FindObjectsByType in a LateUpdate would walk the whole
        // scene each time -- while still missing a fixture that a beat switched on since the
        // last refresh.
        static readonly List<VolumetricSpot> s_Active = new();

        /// <summary>Every enabled fixture, in registration order.</summary>
        public static IReadOnlyList<VolumetricSpot> active => s_Active;

        /// <summary>
        /// Multiplier on density, set by <see cref="CompositeFogZone"/>, not serialised.
        ///
        /// Separate from the authored density so the zone can move every beam with the room's
        /// haze without ever overwriting what each fixture was set to -- the authored value is
        /// the fixture's own character, this is how much smoke is in the air tonight.
        /// </summary>
        public float hazeScale
        {
            get => m_HazeScale;
            set { m_HazeScale = Mathf.Max(0f, value); }
        }

        /// <summary>The colour this beam actually renders, role override included.</summary>
        public Color effectiveColor => m_UseRoleColor ? PlayerRoleColors.For(m_Role) : m_Color;

        public float reveal
        {
            get => m_Reveal;
            set { m_Reveal = Mathf.Clamp01(value); m_FadeSeconds = 0f; Apply(); }
        }

        /// <summary>Throw distance in metres. Safe to animate; it rescales the proxy.</summary>
        public float range
        {
            get => m_Range;
            set { m_Range = Mathf.Max(0.1f, value); Resize(); Align(); }
        }

        /// <summary>Full cone angle in degrees, matching <see cref="Light.spotAngle"/>.</summary>
        public float coneAngle
        {
            get => m_ConeAngle;
            set { m_ConeAngle = Mathf.Clamp(value, 1f, 160f); Resize(); }
        }

        /// <summary>Whether the beam integrates against scene depth.</summary>
        public bool sceneDepthOcclusion
        {
            get => m_SceneDepthOcclusion;
            set { m_SceneDepthOcclusion = value; ApplyOcclusionMode(); }
        }

        void OnEnable()
        {
            if (!s_Active.Contains(this)) s_Active.Add(this);
            Resolve();
            Pull();
            Resize();
            Align();
            Apply();
        }

        void OnDisable() => s_Active.Remove(this);

        void OnValidate()
        {
            Resolve();
            Pull();
            Resize();
            Align();
            if (isActiveAndEnabled) Apply();
        }

        /// <summary>
        /// Recompute everything from the authored fields: pairing, size, aim, properties.
        ///
        /// Needed because an [ExecuteAlways] LateUpdate does not fire reliably outside play mode
        /// -- in batch mode there is no scene view to repaint and so nothing drives it at all.
        /// An editor tool that builds a fixture should call this before saving the scene, so what
        /// lands on disk is the transform the component would have computed for itself.
        /// </summary>
        public void Rebuild()
        {
            Resolve();
            Pull();
            Resize();
            Align();
            Apply();
        }

        void Resolve()
        {
            m_Block ??= new MaterialPropertyBlock();
            if (m_Renderer == null) m_Renderer = GetComponent<MeshRenderer>();
            ApplyOcclusionMode();
        }

        /// <summary>
        /// Keyword and ZTest move together, because either on its own is wrong.
        ///
        /// Without the depth clamp, ZTest LEqual is what occludes the beam, and it does it for
        /// free. With the clamp, the test has to be Always -- the whole point is to shade the air
        /// in front of nearer geometry, which LEqual has already thrown away by the time the
        /// fragment runs.
        ///
        /// Set on the shared material rather than a property block: keywords are not per
        /// renderer, and ZTest is pipeline state a block cannot reach. Fixtures that disagree
        /// about this need their own materials.
        /// </summary>
        void ApplyOcclusionMode()
        {
            if (m_Renderer == null) return;
            Material material = Application.isPlaying
                ? m_Renderer.material
                : m_Renderer.sharedMaterial;
            if (material == null) return;

            const string k_Keyword = "_SCENE_DEPTH_CLAMP";
            if (m_SceneDepthOcclusion)
            {
                material.EnableKeyword(k_Keyword);
                material.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            }
            else
            {
                material.DisableKeyword(k_Keyword);
                material.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            }
        }

        /// <summary>
        /// Take angle and colour from the paired light, when there is one.
        ///
        /// Angle and colour are unambiguous: the beam is exactly as wide as the light and exactly
        /// its colour, and copying them means there is one place to set each.
        ///
        /// RANGE IS NOT COPIED, and that is deliberate. A Light's range is where its falloff
        /// reaches zero, not how far it throws -- URP's attenuation is already near nothing at
        /// 90% of it, so a lamp 3.3 m above a floor needs a range of about 5 m to put any light
        /// on it at all. Copying that into the volume would run the visible cone nearly two
        /// metres through the floor, and since the far end of the proxy is then buried, a player
        /// inside the beam looking down at their feet would see it disappear: the proxy's back
        /// face is behind the floor, the depth test rejects it, and there is no fragment left to
        /// shade. Throw is a separate measurement from falloff. Author it.
        /// </summary>
        void Pull()
        {
            if (m_MatchLight == null) return;

            if (m_MatchLight.type == LightType.Spot)
                m_ConeAngle = Mathf.Clamp(m_MatchLight.spotAngle, 1f, 160f);

            if (!m_UseRoleColor) m_Color = m_MatchLight.color;
        }

        /// <summary>
        /// Sit the proxy on the paired light: apex at the light, beam down its forward.
        ///
        /// The shader's apex is at object z = -0.45, which after scaling lands half a range
        /// behind the proxy's centre -- so the box has to be pushed forward by half the throw
        /// for the cone to start at the lamp rather than behind it.
        /// </summary>
        void Align()
        {
            if (m_MatchLight == null) return;
            Transform lamp = m_MatchLight.transform;
            transform.SetPositionAndRotation(lamp.position + lamp.forward * (m_Range * 0.5f),
                                             lamp.rotation);
        }

        /// <summary>
        /// The proxy is scaled, not rebuilt. The shader reads a fixed object-space cone, so the
        /// only thing that has to match the authored metres and degrees is this transform.
        /// </summary>
        void Resize()
        {
            if (Mathf.Approximately(m_Range, m_LastRange) &&
                Mathf.Approximately(m_ConeAngle, m_LastAngle))
                return;

            m_LastRange = m_Range;
            m_LastAngle = m_ConeAngle;

            float halfAngle = Mathf.Clamp(m_ConeAngle, 1f, 160f) * 0.5f * Mathf.Deg2Rad;
            float baseRadius = m_Range * Mathf.Tan(halfAngle);

            transform.localScale = new Vector3(baseRadius * 2f / k_Inset,
                                               baseRadius * 2f / k_Inset,
                                               m_Range / k_Inset);

            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name != "Cube")
            {
                Debug.LogWarning($"[VolumetricSpot] '{name}' has a {filter.sharedMesh.name} " +
                                 "proxy; the shader expects a Cube. A cone mesh leaves holes in " +
                                 "the beam when seen from inside, because its own faces cull.",
                                 this);
            }
        }

        void LateUpdate()
        {
            Pull();
            Resize();
            Align();

            if (m_FadeSeconds > 0f)
            {
                float t = Mathf.Clamp01((ExperienceClock.now - m_FadeStart) / m_FadeSeconds);
                m_Reveal = Mathf.Lerp(m_FadeFrom, m_FadeTo, t * t * (3f - 2f * t));
                if (t >= 1f) m_FadeSeconds = 0f;
            }
            Apply();
        }

        /// <summary>Bring the beam up or take it down over <paramref name="seconds"/>.</summary>
        public void FadeTo(float target, float seconds)
        {
            m_FadeFrom = m_Reveal;
            m_FadeTo = Mathf.Clamp01(target);
            m_FadeStart = ExperienceClock.now;
            m_FadeSeconds = Mathf.Max(0.01f, seconds);
        }

        void Apply()
        {
            if (m_Renderer == null) return;
            m_Block ??= new MaterialPropertyBlock();

            m_Block.SetColor(k_Color, effectiveColor);
            m_Block.SetFloat(k_Intensity, m_Intensity);
            m_Block.SetFloat(k_Density, m_Density * m_HazeScale);
            m_Block.SetFloat(k_Anisotropy, m_Anisotropy);
            m_Block.SetFloat(k_Steps, m_Steps);
            m_Block.SetFloat(k_Phase, ExperienceClock.now);
            m_Block.SetFloat(k_Reveal, m_Reveal);
            m_Renderer.SetPropertyBlock(m_Block);

            // A beam at zero still rasterises every pixel of its proxy before discarding, and
            // these are large on screen. Switching the renderer off is the difference between a
            // faded cue and a free one.
            bool visible = m_Reveal > 0.002f;
            if (m_Renderer.enabled != visible) m_Renderer.enabled = visible;
        }

        void OnDrawGizmosSelected()
        {
            // The authored cone, not the proxy box -- so a mis-set angle is visible before the
            // shader is ever asked to render it.
            Gizmos.color = m_UseRoleColor ? PlayerRoleColors.For(m_Role) : m_Color;
            Vector3 apex = transform.position - transform.forward * (m_Range * 0.5f);
            float baseRadius = m_Range * Mathf.Tan(Mathf.Clamp(m_ConeAngle, 1f, 160f)
                                                   * 0.5f * Mathf.Deg2Rad);
            Vector3 centre = apex + transform.forward * m_Range;

            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                Vector3 rim = centre
                            + transform.right * (Mathf.Cos(a) * baseRadius)
                            + transform.up * (Mathf.Sin(a) * baseRadius);
                Gizmos.DrawLine(apex, rim);
            }
        }
    }
}
