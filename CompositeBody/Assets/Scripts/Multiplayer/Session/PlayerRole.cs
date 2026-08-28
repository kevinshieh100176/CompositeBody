namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Fixed two-player role set for the experience. Story/gameplay logic (task gating,
    /// per-player visibility) keys off this rather than the ephemeral NGO client id.
    /// </summary>
    public enum PlayerRole
    {
        Unassigned = 0,
        Player1 = 1,
        Player2 = 2
    }
}
