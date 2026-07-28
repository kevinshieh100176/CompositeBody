namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Logging
{
    interface ILogger
    {
        void LogError(object message);
        void LogWarning(object message);
        void LogInfo(object message);
        void LogVerbose(object message);
    }
}
