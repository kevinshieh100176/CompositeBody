using UnityEngine;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// The two players' 代表色 -- the colour that marks what belongs to each of them, and the
    /// one thing the script keeps across every beat ("玩家 A、B 身上的代表色仍然保留").
    ///
    /// Defined once here because the same two colours have to agree across the blurred
    /// silhouette in O-0, the colour seeping out of an owned object in O-2, the mixing light
    /// zone in O-3 and the per-role particles in S0-2.
    ///
    /// THESE ARE O-0'S 光圈 COLOURS. The two lamps standing over the 真人 were authored directly
    /// on the lights as purple and yellow and did not read from here, so this file drifted to a
    /// teal and orange that nothing in the built scene used -- two sources of truth for the one
    /// thing the script says must never vary ("玩家 A、B 身上的代表色仍然保留"). The beams are what
    /// the audience has already been looking at, so the beams won, and every authored copy of the
    /// pair (Onboarding.prefab, the test scenes, the editor builders) was rewritten with this file
    /// rather than left to drift again.
    ///
    /// NOW A MELANCHOLY PAIR: A 湖水綠, B 藍色, both cool and both held under full value. What
    /// this gives up is deliberate and worth writing down, because the old pair was chosen for it:
    ///   * The hues are neighbours, not opposites, so O-3's shared zone mixes to a steel blue-cyan
    ///     -- a shade of the same family rather than a third colour the audience reads as new.
    ///   * Neither player is warm any more, so "A withdrawing / B holding on" is no longer carried
    ///     by temperature. The two are told apart by hue distance (~170° vs ~220°) and by B sitting
    ///     visibly darker and denser than A, which is what has to survive on a point cloud.
    ///   * Both sit near 0.8 value instead of 1.0, so the 光圈 run about a fifth dimmer than the
    ///     authored intensities assumed. The light intensities themselves were left alone.
    /// </summary>
    public static class PlayerRoleColors
    {
        /// <summary>Player A, 提分手者／內疚方. 湖水綠 -- cool, washed out, withdrawing.</summary>
        public static readonly Color Player1 = new(0.30f, 0.78f, 0.70f);

        /// <summary>Player B, 被分手者／不甘方. 藍色 -- deeper and heavier, sinking rather than warm.</summary>
        public static readonly Color Player2 = new(0.24f, 0.42f, 0.85f);

        public static Color For(PlayerRole role) => role switch
        {
            PlayerRole.Player1 => Player1,
            PlayerRole.Player2 => Player2,
            _ => new Color(0.55f, 0.55f, 0.58f)
        };

        /// <summary>The colour the two of them make together, for O-3's shared light zone.</summary>
        public static Color Mixed => (Player1 + Player2) * 0.5f;
    }
}
