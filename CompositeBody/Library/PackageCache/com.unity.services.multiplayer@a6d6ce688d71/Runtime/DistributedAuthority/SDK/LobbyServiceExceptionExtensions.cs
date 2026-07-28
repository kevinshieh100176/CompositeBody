using Unity.Services.Lobbies;

namespace Unity.Services.DistributedAuthority
{
    internal static class LobbyServiceExceptionExtensions
    {
        const string k_AlreadyMemberDetail = "already a member";

        public static bool IsAlreadyMemberError(this LobbyServiceException lex)
        {
            // TODO: Explore how we can get more granular access while still being able to test this.
            return lex.Reason == LobbyExceptionReason.LobbyConflict
                && lex.Message.Contains(k_AlreadyMemberDetail);
        }
    }
}
