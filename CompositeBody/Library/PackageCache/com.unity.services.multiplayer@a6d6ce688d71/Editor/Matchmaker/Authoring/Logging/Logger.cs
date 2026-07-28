using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Logging
{
    class Logger : ILogger
    {
        const string k_Tag = "[MatchmakingAuthoring]";

        const string k_VerboseLoggingDefine = "ENABLE_UNITY_MATCHMAKER_AUTHORING_VERBOSE_LOGGING";

        public void LogError(object message) => Debug.unityLogger.LogError(k_Tag, message);

        public void LogWarning(object message) => Debug.unityLogger.LogWarning(k_Tag, message);

        public void LogInfo(object message) => Debug.unityLogger.Log(k_Tag, message);

        public void LogVerbose(object message) => LogVerboseInternal(message);

#if !ENABLE_UNITY_SERVICES_VERBOSE_LOGGING
        [Conditional(k_VerboseLoggingDefine)]
#endif
        void LogVerboseInternal(object message) => Debug.unityLogger.Log(k_Tag, message);
    }
}
