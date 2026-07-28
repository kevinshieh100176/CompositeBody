namespace Unity.Services.Matchmaker.Overrides
{
    internal interface IABAnalytics
    {
        void SubmitUserAssignmentConfirmedEvent(string rcVariantID, string rcAssignmentID);
    }
}
