using UnityEngine;

namespace CompositeBody.Avatar.Vfx
{
    /// <summary>
    /// Sheds particles off the whole body surface and leaves them behind in the world.
    ///
    /// Emission is driven by the shape module reading the avatar's <see cref="SkinnedMeshRenderer"/>
    /// directly, so every triangle of the deformed body is a spawn site and the cloud follows the
    /// animation for free -- no per-frame mesh bake, no sample set to keep in sync with the rig.
    ///
    /// Simulation is in <b>world</b> space: a particle is born at whatever world position the
    /// skin occupied at that instant and then stops caring about the body entirely, so walking
    /// draws a standing trail through the room rather than dragging the cloud along.
    ///
    /// Requires Read/Write Enabled on the body mesh. Unity's skinned-mesh shape reads vertex data
    /// on the CPU, and without it the shape silently emits nothing at all.
    /// </summary>
    [ExecuteAlways] // so the effect can be dialled in, and previewed by tooling, without Play mode
    public class BodyShedVfx : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField, Tooltip("Body whose surface spawns the particles.")]
        SkinnedMeshRenderer m_Body;

        [Header("Systems")]
        [SerializeField] ParticleSystem m_Motes;
        [SerializeField] ParticleSystem m_Streaks;

        [Header("Output")]
        [SerializeField, Range(0f, 4f), Tooltip("Scales both systems' emission. Ramp this from gameplay to bring the effect up and down.")]
        float m_Intensity = 1f;

        [SerializeField, Tooltip("Fine points shed per second across the whole body.")]
        float m_MotesPerSecond = 9000f;
        [SerializeField, Tooltip("Extra fine points per metre the body travels, so movement sheds harder than standing still.")]
        float m_MotesPerMetre = 6000f;

        [SerializeField, Tooltip("Streaks thrown off per second across the whole body.")]
        float m_StreaksPerSecond = 120f;
        [SerializeField, Tooltip("Extra streaks per metre the body travels.")]
        float m_StreaksPerMetre = 160f;

        public float intensity
        {
            get => m_Intensity;
            set { m_Intensity = Mathf.Max(0f, value); ApplyRates(); }
        }

        void OnEnable() => ApplyRates();
        void OnValidate() => ApplyRates();

        void ApplyRates()
        {
            SetRates(m_Motes, m_MotesPerSecond * m_Intensity, m_MotesPerMetre * m_Intensity);
            SetRates(m_Streaks, m_StreaksPerSecond * m_Intensity, m_StreaksPerMetre * m_Intensity);
        }

        static void SetRates(ParticleSystem system, float perSecond, float perMetre)
        {
            if (system == null) return;
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = perSecond;
            emission.rateOverDistance = perMetre;
        }

        /// <summary>
        /// Applies the particle-system settings the effect expects. Exposed so the same setup
        /// runs from tooling and from the inspector, instead of being a list of module values
        /// somebody has to reproduce by hand.
        /// </summary>
        [ContextMenu("Configure Particle Systems")]
        public void ConfigureSystems()
        {
            ConfigureMotes(m_Motes, m_Body);
            ConfigureStreaks(m_Streaks, m_Body);
            ApplyRates();
        }

        public static void ConfigureMotes(ParticleSystem system, SkinnedMeshRenderer body)
        {
            if (system == null) return;
            ConfigureShared(system, body, lifetime: 5.0f, maxParticles: 40000);

            var main = system.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.026f);
            // Drifts off the skin rather than being fired from it, so the body stays wrapped in
            // a haze instead of venting steam.
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.14f);
            main.gravityModifier = 0.01f;

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.09f;
            noise.frequency = 0.55f;
            noise.scrollSpeed = 0.12f;
            noise.damping = true;

            system.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
        }

        public static void ConfigureStreaks(ParticleSystem system, SkinnedMeshRenderer body)
        {
            if (system == null) return;
            ConfigureShared(system, body, lifetime: 2.6f, maxParticles: 4000);

            var main = system.main;
            main.startSize = 0.010f; // the streak's width; its length comes from speed
            // Fired outward along the surface normal, which is what throws the long radiating
            // lines rather than a second, faster haze.
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 4.2f);
            main.gravityModifier = 0f;

            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.05f; // bleeds speed off so a streak shortens as it dies
            limit.limit = 6f;

            var shape = system.shape;
            shape.randomDirectionAmount = 0.25f; // fan them off the normal a little

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 1.1f;
            renderer.lengthScale = 1f;
            renderer.cameraVelocityScale = 0f;
        }

        static void ConfigureShared(ParticleSystem system, SkinnedMeshRenderer body,
                                    float lifetime, int maxParticles)
        {
            var main = system.main;

            // The whole point: a particle is placed in the world when it spawns and left there.
            // In Local space it would ride along inside the avatar's transform and the body
            // would never actually leave anything behind it.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = lifetime;
            main.maxParticles = maxParticles;
            main.startColor = Color.white;
            main.playOnAwake = true;

            var emission = system.emission;
            emission.enabled = true;

            // Every triangle of the deformed body is a spawn site, and the direction each
            // particle leaves along is that triangle's normal.
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
            shape.skinnedMeshRenderer = body;
            shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            shape.useMeshMaterialIndex = false;
            shape.normalOffset = 0.008f; // just clear of the skin, so nothing spawns inside it

            var colour = system.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.10f),
                    new GradientAlphaKey(0.7f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.3f));

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sortMode = ParticleSystemSortMode.None; // additive: draw order does not matter
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.alignment = ParticleSystemRenderSpace.View;
        }
    }
}
