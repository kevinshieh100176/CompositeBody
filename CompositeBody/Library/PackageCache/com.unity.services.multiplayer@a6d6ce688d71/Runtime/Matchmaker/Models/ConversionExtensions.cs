namespace Unity.Services.Matchmaker.Models
{
    static class ConversionExtensions
    {
        public static MatchProperties ToMatchProperties(this StoredMatchProperties matchProperties)
        {
            return new MatchProperties(matchProperties.Teams, matchProperties.Players, matchProperties.Region, matchProperties.BackfillTicketId);
        }
    }
}
