namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Fetch
{
    interface IDeepEqualityComparer
    {
        bool IsDeepEqual<T>(T source, T target);
    }
}
