using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity.Services.Multiplayer
{
    internal class Logger
    {
        const string k_Tag = "[Multiplayer]";
        const string k_UnityAssertions = "UNITY_ASSERTIONS";
        const string k_VerboseLoggingDefine =
            "ENABLE_UNITY_MULTIPLAYER_VERBOSE_LOGGING";

        static void Log(LogType level, object message, UnityEngine.Object context)
        {
            Debug.unityLogger.Log(level, k_Tag, message, context);
        }

        public static void Log(object message)
        {
            Log(message, null);
        }

        public static void Log(object message, UnityEngine.Object context)
        {
            Log(LogType.Log, message, context);
        }

        public static void LogWarning(object message)
        {
            LogWarning(message, null);
        }

        public static void LogWarning(object message, UnityEngine.Object context)
        {
            Log(LogType.Warning, message, context);
        }

        public static void LogCallWarning(string enclosingType, string message, [CallerMemberName] string method = "")
        {
            LogCallWarning(enclosingType, message, null, method);
        }

        public static void LogCallWarning(string enclosingType, string message, UnityEngine.Object context, [CallerMemberName] string method = "")
        {
            LogWarning($"{enclosingType}.{method}: {message}", context);
        }

        public static void LogError(object message)
        {
            LogError(message, null);
        }

        public static void LogError(object message, UnityEngine.Object context)
        {
            Log(LogType.Error, message, context);
        }

        public static void LogCallError(string enclosingType, string message, [CallerMemberName] string method = "")
        {
            LogCallError(enclosingType, message, null, method);
        }

        public static void LogCallError(string enclosingType, string message, UnityEngine.Object context, [CallerMemberName] string method = "")
        {
            LogError($"{enclosingType}.{method}: {message}", context);
        }

        public static void LogException(Exception exception)
        {
            LogException(exception, null);
        }

        public static void LogException(Exception exception, UnityEngine.Object context)
        {
            Log(LogType.Exception, exception, context);
        }

        [Conditional(k_UnityAssertions)]
        public static void LogAssertion(object message)
        {
            LogAssertion(message, null);
        }

        [Conditional(k_UnityAssertions)]
        public static void LogAssertion(object message, UnityEngine.Object context)
        {
            Log(LogType.Assert, message, context);
        }

        [Conditional(k_VerboseLoggingDefine)]
        public static void LogVerbose(object message)
        {
            LogVerbose(message, null);
        }

        [Conditional(k_VerboseLoggingDefine)]
        public static void LogVerbose(object message, UnityEngine.Object context)
        {
            Log(message, context);
        }

        [Conditional(k_VerboseLoggingDefine)]
        public static void LogCallVerbose(string enclosingType, [CallerMemberName] string method = "")
        {
            LogCallVerbose(enclosingType, null, method);
        }

        [Conditional(k_VerboseLoggingDefine)]
        public static void LogCallVerbose(string enclosingType, UnityEngine.Object context, [CallerMemberName] string method = "")
        {
            LogVerbose($"{enclosingType}.{method}", context);
        }

        [Conditional(k_VerboseLoggingDefine)]
        public static void LogCallVerboseWithMessage(string enclosingType, string message,
            [CallerMemberName] string method = "")
        {
            LogCallVerboseWithMessage(enclosingType, message, null, method);
        }

        [Conditional(k_VerboseLoggingDefine)]
        public static void LogCallVerboseWithMessage(string enclosingType, string message, UnityEngine.Object context, [CallerMemberName] string method = "")
        {
            LogVerbose($"{enclosingType}.{method}: {message}", context);
        }
    }
}
