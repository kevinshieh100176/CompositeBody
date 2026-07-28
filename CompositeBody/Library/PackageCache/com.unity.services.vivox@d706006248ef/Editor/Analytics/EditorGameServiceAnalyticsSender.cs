using System;
using Unity.Services.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Unity.Services.Vivox.Editor
{
    static class EditorGameServiceAnalyticsSender
    {
        static class AnalyticsComponent
        {
            public const string ProjectSettings = "Project Settings";
            public const string TopMenu = "Top Menu";
        }

        static class AnalyticsAction
        {
            public const string Configure = "Configure";
            public const string CredentialPull = "CredentialPull";
            public const string Build = "Build";
            public const string TestModeBuild = "TestModeBuild";
            public const string AuthenticationBuild = "AuthenticationBuild";
        }

        const int k_Version = 1;
        const string k_EventName = "editorgameserviceeditor";

        static IEditorGameServiceIdentifier s_Identifier;

        static IEditorGameServiceIdentifier Identifier
        {
            get
            {
                if (s_Identifier == null)
                {
                    s_Identifier = EditorGameServiceRegistry.Instance.GetEditorGameService<VivoxIdentifier>().Identifier;
                }
                return s_Identifier;
            }
        }

        internal static void SendProjectSettingsEvent()
        {
            SendEvent(AnalyticsComponent.ProjectSettings, AnalyticsAction.Configure);
        }

        internal static void SendTopMenuConfigureEvent()
        {
            SendEvent(AnalyticsComponent.TopMenu, AnalyticsAction.Configure);
        }

        internal static void SendCredentialPullEvent()
        {
            SendEvent(AnalyticsComponent.ProjectSettings, AnalyticsAction.CredentialPull);
        }

        internal static void SendVivoxBuildEvent()
        {
            SendEvent(AnalyticsComponent.ProjectSettings, AnalyticsAction.Build);
        }

        internal static void SendVivoxBuildWithAuthenticationEvent()
        {
            SendEvent(AnalyticsComponent.ProjectSettings, AnalyticsAction.AuthenticationBuild);
        }

        internal static void SendVivoxBuildWithTestModeEvent()
        {
            SendEvent(AnalyticsComponent.ProjectSettings, AnalyticsAction.TestModeBuild);
        }

        static void SendEvent(string component, string action)
        {
            EditorAnalytics.SendEventWithLimit(k_EventName, new EditorGameServiceEvent
            {
                action = action,
                component = component,
                package = Identifier.GetKey()
            }, k_Version);
        }

        /// <remarks>Lowercase is used here for compatibility with analytics.</remarks>
        [Serializable]
        public struct EditorGameServiceEvent
        {
            public string action;
            public string component;
            public string package;
        }
    }
}
