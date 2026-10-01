namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Every beat of the experience, in running order. This enum is the canonical beat sheet:
    /// the summaries below are the script's own, so the running order lives in version control
    /// next to the code that plays it rather than only in the design document.
    ///
    /// Values are spaced by 10 because they are serialized into scenes by number. A beat
    /// inserted later takes a gap value and nothing already authored has to be renumbered.
    ///
    /// The written script ends at <see cref="S0_4_TheyLeave"/>; <see cref="End"/> is the
    /// terminal hold, and the insertion point for S1 onward if it is ever written.
    /// </summary>
    public enum StoryBeat
    {
        None = 0,

        /// <summary>
        /// O-0 進入. Sea/water/frame void between a room and an ocean. No body, only faint
        /// hands. Each player sees the other as a blurred silhouette in their own role colour.
        /// Low ambience; distant water, furniture, muffled voices.
        /// </summary>
        O0_Arrival = 10,

        /// <summary>
        /// O-1 鬼的控制. A lamp rests on the water, then trembles. Making a fist strips its
        /// gravity: it floats, spins, judders, briefly flies.
        /// </summary>
        O1_Control = 20,

        /// <summary>
        /// O-2 鬼的抓取. Cup, keys, clothes, book. The player's own colour seeps outward from
        /// inside exactly one of them. Hands pass through everything else; the owned object
        /// brightens on approach and can be moved, turned, dropped, or handed over.
        /// </summary>
        O2_Grasp = 30,

        /// <summary>
        /// O-3 鬼魂交流. A pale light zone between the players. Closing in makes particles
        /// cross and colours mix; orbiting each other resolves the muffled audio into
        /// 「我想跟你說，我真的很在乎你。」 /「我知道。」 Then the zone dims.
        /// </summary>
        O3_Communion = 40,

        /// <summary>
        /// O-4 Onboarding 結束. Teaching props lose their glow, prompts fade, role colours
        /// stay. The first unmistakable sound: a key turning. Black.
        /// </summary>
        O4_OnboardingEnd = 50,

        /// <summary>
        /// S0-1 聲音先於空間. Full black, hands barely visible. Cues arrive from separate
        /// directions: key in lock, tap running, laughter, an argument cutting through, tape
        /// peeling, a box dragged over the floor, a door opened and shut.
        /// </summary>
        S0_1_SoundBeforeSpace = 60,

        /// <summary>
        /// S0-2 生活痕跡形成身體. Each sound leaves particles where it happened -- water cool,
        /// laughter warm, the argument splintering -- which drift to the player and build hands,
        /// then arms, chest, silhouette. The body is particles, colour, text fragments and a
        /// translucent membrane. The two players absorb different sounds, so their bodies differ.
        /// </summary>
        S0_2_TracesFormBody = 70,

        /// <summary>
        /// S0-3 空屋形成. Walls, window, door frame and floor surface out of the dark. The
        /// furniture is already gone: dust, pale patches on the walls, pressure marks on the
        /// floor, a few things left in the corners.
        /// </summary>
        S0_3_EmptyRoomForms = 80,

        /// <summary>
        /// S0-4 真正的兩個人離開. The actual couple carry out the last boxes, faces unreadable,
        /// not speaking. Hands pass through them. The last one looks back, pauses, shuts the
        /// door. 「砰。」 Silence.
        /// </summary>
        S0_4_TheyLeave = 90,

        /// <summary>Terminal hold after the script ends. Staff restart from here.</summary>
        End = 100
    }

    /// <summary>Ordering, labels and gate-id conventions for <see cref="StoryBeat"/>.</summary>
    public static class StoryBeats
    {
        /// <summary>Running order. The director walks this array; the enum values are not assumed contiguous.</summary>
        public static readonly StoryBeat[] Ordered =
        {
            StoryBeat.O0_Arrival,
            StoryBeat.O1_Control,
            StoryBeat.O2_Grasp,
            StoryBeat.O3_Communion,
            StoryBeat.O4_OnboardingEnd,
            StoryBeat.S0_1_SoundBeforeSpace,
            StoryBeat.S0_2_TracesFormBody,
            StoryBeat.S0_3_EmptyRoomForms,
            StoryBeat.S0_4_TheyLeave,
            StoryBeat.End
        };

        /// <summary>
        /// Staff-panel label. ASCII on purpose: the panel draws with Unity's built-in legacy
        /// font, which has no CJK glyphs, so the script's own titles would render as blanks.
        /// </summary>
        public static string DisplayName(StoryBeat beat) => beat switch
        {
            StoryBeat.O0_Arrival => "O-0 Arrival",
            StoryBeat.O1_Control => "O-1 Ghost Control",
            StoryBeat.O2_Grasp => "O-2 Ghost Grasp",
            StoryBeat.O3_Communion => "O-3 Ghost Communion",
            StoryBeat.O4_OnboardingEnd => "O-4 Onboarding End",
            StoryBeat.S0_1_SoundBeforeSpace => "S0-1 Sound Before Space",
            StoryBeat.S0_2_TracesFormBody => "S0-2 Traces Form Body",
            StoryBeat.S0_3_EmptyRoomForms => "S0-3 Empty Room Forms",
            StoryBeat.S0_4_TheyLeave => "S0-4 They Leave",
            StoryBeat.End => "End",
            _ => "(none)"
        };

        /// <summary>Short code for the staff panel's jump buttons.</summary>
        public static string ShortCode(StoryBeat beat) => beat switch
        {
            StoryBeat.O0_Arrival => "O0",
            StoryBeat.O1_Control => "O1",
            StoryBeat.O2_Grasp => "O2",
            StoryBeat.O3_Communion => "O3",
            StoryBeat.O4_OnboardingEnd => "O4",
            StoryBeat.S0_1_SoundBeforeSpace => "S1",
            StoryBeat.S0_2_TracesFormBody => "S2",
            StoryBeat.S0_3_EmptyRoomForms => "S3",
            StoryBeat.S0_4_TheyLeave => "S4",
            StoryBeat.End => "End",
            _ => "-"
        };

        /// <summary>
        /// Conventional <see cref="StoryProgressManager"/> task id gating a beat. Derived rather
        /// than hand-typed so the director, the content scripts and the scene builder cannot
        /// drift apart over a typo in a string that nothing would otherwise validate.
        /// </summary>
        public static string GateTaskId(StoryBeat beat) => $"beat.{beat}";

        public static int IndexOf(StoryBeat beat) => System.Array.IndexOf(Ordered, beat);
    }
}
