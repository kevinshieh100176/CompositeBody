using UnityEngine;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Footprints that light one after another around the centre and go out behind, over and
    /// over: a few steps of light travelling the circle, telling a player to walk it and which
    /// way round.
    ///
    /// A LOOP, NOT A SWEEP, and the difference is what the cue is for. A ring that filled itself
    /// in once and stayed lit said "here is a circle"; it stopped pointing anywhere the moment it
    /// was complete, and a player who looked down ten seconds late saw only a shape on the floor.
    /// A band of steps going round says "walk this way" for as long as it is up, and keeps saying
    /// it to whoever looks down whenever.
    ///
    /// EACH PRINT FADES OUT BEHIND THE BAND rather than settling to a dim value. The tail is what
    /// makes the band directional: the brightest print is the next one to stand on, and the ones
    /// going out are the ones already walked.
    ///
    /// ONE RING PER PLAYER, AT DIFFERENT RADII. Both players are in the same four-by-five metre
    /// room walking the same circle, and two people following one line of prints in the dark meet
    /// on it. Concentric rings in each player's 代表色 keep them apart and keep whose is whose
    /// obvious, which is the same job the colours do everywhere else in the piece.
    ///
    /// DRIVEN BY BEING SWITCHED ON. The timeline's activation track decides when; the loop runs
    /// itself from <see cref="ExperienceClock"/>, so both machines light the same print at the
    /// same moment without this being replicated.
    /// </summary>
    public class FootprintRing : MonoBehaviour, IDebugInteraction
    {
        [SerializeField, Tooltip("Whose ring this is. Decides the colour.")]
        PlayerRole m_Role = PlayerRole.Player1;

        [SerializeField, Tooltip("The prints, in walking order. Found among the children if empty.")]
        Renderer[] m_Prints = new Renderer[0];

        [SerializeField, Min(0.5f), Tooltip("Seconds for the band to travel all the way round. " +
                                            "This is a walking pace, not an animation speed: at " +
                                            "twelve prints and eight seconds a player keeping up " +
                                            "takes a step every two thirds of a second.")]
        float m_LapSeconds = 8f;

        [SerializeField, Min(0.01f), Tooltip("Seconds a print takes to come up as the band " +
                                             "reaches it. Short: a print that fades in slowly " +
                                             "reads as a stain rather than as a step.")]
        float m_LightUpSeconds = 0.2f;

        [SerializeField, Min(0f), Tooltip("Seconds a print stays at full before it starts going " +
                                          "out.")]
        float m_HoldSeconds = 0.5f;

        [SerializeField, Min(0.01f), Tooltip("Seconds a print takes to go out behind the band. " +
                                             "This is what sets the length of the tail: longer " +
                                             "leaves more of the circle lit at once. At 2.6 " +
                                             "about five of twelve prints are showing, which is " +
                                             "enough of the path to read as a circle while the " +
                                             "bright end still says which way round.")]
        float m_FadeOutSeconds = 2.6f;

        [SerializeField, ColorUsage(false, true), Tooltip("What the print emits, as an HDR " +
                                                          "colour. Multiplies the role colour " +
                                                          "below unless that is switched off, so " +
                                                          "white at intensity 2 is the 代表色 " +
                                                          "twice as bright. Use the intensity " +
                                                          "slider in the colour picker: it works " +
                                                          "in stops, and one stop is a doubling.")]
        Color m_Emission = Color.white;

        [SerializeField, Tooltip("Tint the emission above by the player's 代表色. Off uses the " +
                                 "emission colour as it is, for a ring that should not be a " +
                                 "role colour at all.")]
        bool m_UseRoleColor = true;

        [SerializeField, Range(0f, 1f), Tooltip("Scrub the loop by hand, 0 to 1 of a lap. " +
                                                "Ignores the clock.")]
        float m_Phase;

        [SerializeField, Tooltip("Use the slider above instead of the clock. For authoring.")]
        bool m_Scrub;

        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int k_EmissionColor = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock m_Block;
        float m_StartTime = -1f;
        int m_Lit;

        /// <summary>Where the band has got to, 0 to 1 of a lap.</summary>
        public float phase
        {
            get => m_Phase;
            set { m_Phase = Mathf.Repeat(value, 1f); Apply(m_Phase); }
        }

        /// <summary>
        /// Kept under its old name because the still renderer and the payoff harness drive the
        /// ring through it. It is the position of the band now rather than how much of the ring
        /// has been filled in, which for both of those is the same thing: a number to put the
        /// loop somewhere.
        /// </summary>
        public float reveal
        {
            get => phase;
            set => phase = value;
        }

        /// <summary>How many prints are showing at this instant.</summary>
        public int litCount => m_Lit;

        /// <summary>
        /// How hard the prints emit, as a plain multiplier on the authored emission colour.
        /// Above 1 is an HDR value. For the bloom toggle and the comparison renders; by hand,
        /// the colour field's own intensity slider is the better control.
        /// </summary>
        public float intensity
        {
            get => Mathf.Max(m_Emission.r, Mathf.Max(m_Emission.g, m_Emission.b));
            set
            {
                float was = intensity;
                float scale = was > 1e-4f ? Mathf.Max(value, 0.01f) / was : Mathf.Max(value, 0.01f);
                m_Emission = new Color(m_Emission.r * scale, m_Emission.g * scale,
                                       m_Emission.b * scale, 1f);
                Apply(m_Phase);
            }
        }

        /// <summary>The colour actually emitted, before the per-print fade.</summary>
        public Color emission
        {
            get => m_UseRoleColor ? RoleTinted() : m_Emission;
            set { m_Emission = value; m_UseRoleColor = false; Apply(m_Phase); }
        }

        Color RoleTinted()
        {
            Color role = PlayerRoleColors.For(m_Role);
            return new Color(role.r * m_Emission.r, role.g * m_Emission.g, role.b * m_Emission.b, 1f);
        }

        void Awake()
        {
            Resolve();
            InteractionDebug.Register(this);
        }

        void OnDestroy() => InteractionDebug.Unregister(this);

        void OnEnable()
        {
            // The clock, not Time.time, and stamped on enable rather than in Awake: the ring is
            // switched on by the timeline part way through the show, and the band has to start
            // from then rather than from whenever the prefab loaded.
            m_StartTime = ExperienceClock.now;
            Apply(0f);
        }

        void OnValidate()
        {
            if (!m_Scrub) return;
            Resolve();
            Apply(m_Phase);
        }

        void Resolve()
        {
            m_Block ??= new MaterialPropertyBlock();

            if (m_Prints == null || m_Prints.Length == 0)
            {
                // In child order, which the builder lays down in walking order. Sorting by angle
                // here would look clever and would silently disagree with the builder the first
                // time someone moved a print by hand.
                m_Prints = GetComponentsInChildren<Renderer>(true);
            }
        }

        void LateUpdate()
        {
            if (m_Scrub)
            {
                Apply(m_Phase);
                return;
            }

            if (m_StartTime < 0f) return;

            // Repeat, not clamp: the band goes round for as long as the ring is up.
            float laps = (ExperienceClock.now - m_StartTime) / Mathf.Max(m_LapSeconds, 0.01f);
            Apply(Mathf.Repeat(laps, 1f));
        }

        /// <summary>
        /// The whole ring as a function of one value, so a scrub, a still and the live show all
        /// take the same path through this.
        /// </summary>
        void Apply(float phase01)
        {
            m_Phase = Mathf.Repeat(phase01, 1f);
            m_Lit = 0;

            if (m_Prints == null || m_Prints.Length == 0) return;

            m_Block ??= new MaterialPropertyBlock();

            // THE EMITTED COLOUR IS AN HDR VALUE AND IS MEANT TO BE. The material is additive
            // and unlit, so what this writes IS what reaches the colour buffer -- there is no
            // lighting in between to attenuate it. Over 1 it is brighter than white, which is
            // the only thing bloom looks for and the whole reason the buffer is floating point.
            Color colour = emission;

            int count = m_Prints.Length;
            float secondsPerPrint = Mathf.Max(m_LapSeconds, 0.01f) / count;
            float head = m_Phase * count;

            for (int i = 0; i < count; i++)
            {
                Renderer print = m_Prints[i];
                if (print == null) continue;

                // HOW LONG AGO THE BAND PASSED THIS PRINT, wrapped into one lap. Wrapping is what
                // makes the loop seamless: the print the band has just left is a fraction of a
                // lap old, and the one in front of it is nearly a whole lap old rather than
                // negative.
                float age = Mathf.Repeat(head - i, count) * secondsPerPrint;

                float alpha = AlphaAt(age);
                bool visible = alpha > 0.004f;

                if (print.enabled != visible) print.enabled = visible;
                if (!visible) continue;

                m_Lit++;

                // A property block, so one material serves both rings in both colours and
                // neither writes a colour into the asset on disk.
                // The alpha carries the fade and the RGB carries the emission, because the
                // material is additive: what reaches the screen is colour * alpha. So the HDR
                // value rides on the colour and the fade stays a straight 0-to-1, rather than
                // the two fighting over one number.
                print.GetPropertyBlock(m_Block);
                m_Block.SetColor(k_BaseColor, new Color(colour.r, colour.g, colour.b, alpha));

                // Written as emission as well, so anything that reads the standard property --
                // a different shader on these quads, an emissive pass added later -- sees the
                // same value rather than a black surface. The unlit material ignores it, and a
                // property block write to a name the shader does not have costs nothing.
                m_Block.SetColor(k_EmissionColor, new Color(colour.r * alpha, colour.g * alpha,
                                                            colour.b * alpha, 1f));

                print.SetPropertyBlock(m_Block);
            }
        }

        /// <summary>
        /// One print's brightness, by how long ago the band reached it: up, hold, out, dark until
        /// the band comes round again.
        /// </summary>
        float AlphaAt(float age)
        {
            if (age < 0f) return 0f;
            if (age < m_LightUpSeconds) return Mathf.SmoothStep(0f, 1f, age / m_LightUpSeconds);

            float afterHold = age - m_LightUpSeconds - m_HoldSeconds;
            if (afterHold <= 0f) return 1f;
            if (afterHold >= m_FadeOutSeconds) return 0f;

            return Mathf.SmoothStep(1f, 0f, afterHold / m_FadeOutSeconds);
        }

        #region Debug panel

        public string debugId => $"footprints.{m_Role}";

        public string debugLabel => $"Footprints on the floor ({m_Role}) -- walk the circle";

        public DebugInteractionScope debugScope => DebugInteractionScope.LocalMachine;

        public string debugFireLabel => "Show";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;
            state.fired = isActiveAndEnabled;

            if (!isActiveAndEnabled)
                state.detail = "switched off -- the timeline's activation track brings these up";
            else if (m_Prints == null || m_Prints.Length == 0)
                state.detail = "no prints wired";
            else
                state.detail = $"{m_Lit} of {m_Prints.Length} lit at {intensity:0.00}x" +
                               (intensity > 1.01f ? " (HDR)" : "") +
                               $", {m_Phase * 100f:0}% round a {m_LapSeconds:0.#}s lap" +
                               (m_Scrub ? ", scrubbing by hand" : "");

            return state;
        }

        // NOT AN INTERACTION, STRICTLY -- nothing waits for anyone to walk it, the timeline just
        // puts it up. It gets a row because "are the prints up on both machines" is a question
        // worth being able to answer from one screen, and a fire button because that is faster
        // than scrubbing the show to find out what they look like.
        public bool DebugCanFire(DebugFireTarget target) =>
            target == DebugFireTarget.Local && !isActiveAndEnabled;

        public void DebugFire(DebugFireTarget target)
        {
            if (target != DebugFireTarget.Local) return;

            if (!gameObject.activeSelf) gameObject.SetActive(true);
            m_StartTime = ExperienceClock.now;
            Apply(0f);
        }

        public bool DebugCanReset() => isActiveAndEnabled;

        public void DebugReset()
        {
            m_Scrub = false;
            m_StartTime = ExperienceClock.now;
            Apply(0f);
        }

        #endregion
    }
}
