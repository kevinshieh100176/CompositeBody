using UnityEngine;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// The two players' 代表色 -- the colour that marks what belongs to each of them, and the
    /// one thing the script keeps across every beat ("玩家 A、B 身上的代表色仍然保留").
    ///
    /// Defined once here because the same two colours have to agree across the blurred
    /// silhouette in O-0, the colour seeping out of an owned object in O-2, the mixing light
    /// zone in O-3 and the per-role particles in S0-2. Two hues that sit opposite each other
    /// also means O-3's mix reads as a third colour rather than as a muddier version of either.
    /// </summary>
    public static class PlayerRoleColors
    {
        /// <summary>Player A, 提分手者／內疚方. Cool and withdrawing.</summary>
        public static readonly Color Player1 = new(0.42f, 0.84f, 0.78f);

        /// <summary>Player B, 被分手者／不甘方. Warm and holding on.</summary>
        public static readonly Color Player2 = new(0.93f, 0.63f, 0.29f);

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
