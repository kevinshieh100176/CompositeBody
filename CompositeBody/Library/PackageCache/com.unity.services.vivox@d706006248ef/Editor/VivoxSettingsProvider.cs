using System.Collections.Generic;
using System;
using Unity.Services.Core.Editor;
using UnityEditor;

using UnityEditor.UIElements;
using UnityEditor.CrashReporting;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Services.Vivox.Editor
{
    class VivoxSettingsProvider : EditorGameServiceSettingsProvider
    {
        const string k_PackageName = "com.unity.services.vivox";
        const string k_IsServiceEnabledKey = "isServiceEnabled";
        const string k_IsTestModeKey = "isTestMode";
        const string k_Title = "Vivox";
        
        VivoxSettingsProvider(string name, SettingsScope scope, IEnumerable<string> keywords = null)
            : base(name, scope, keywords) { }

        protected override IEditorGameService EditorGameService
            => EditorGameServiceRegistry.Instance.GetEditorGameService<VivoxIdentifier>();

        protected override string Title => k_Title;

        protected override string Description
            => "Deliver the best voice and text communications for your players with Unity's managed solution.";

        protected override VisualElement GenerateServiceDetailUI()
        {
            var containerAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath.Common);
            VisualElement containerUI = null;
            if (containerAsset != null)
            {
                containerUI = containerAsset.CloneTree().contentContainer;

                var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath.Common);
                if (styleSheet != null)
                {
                    containerUI.styleSheets.Add(styleSheet);
                    SetUpDashboardButton(containerUI);
                    SetUpDocumentationButton(containerUI);
                    SetUpCredentials(containerUI);
                    SetUpTestToggle(containerUI);
                    FetchCredentials(containerUI);
                    EditorGameServiceAnalyticsSender.SendProjectSettingsEvent();
#if !ENABLE_EDITOR_GAME_SERVICES
                    RemoveExternalLinkIcons(containerUI);
#endif
#if !AUTH_PACKAGE_PRESENT
                    SetUpAuthPackageWarning(containerUI);
#else
                    RemoveAuthPackageWarning(containerUI);
#endif
                }
            }
            return containerUI;
        }

        protected override VisualElement GenerateUnsupportedDetailUI()
        {
            return GenerateServiceDetailUI();
        }

        static void SetUpTestToggle(VisualElement parentElement)
        {
            var testModeToggle = parentElement.Q<Toggle>(UxmlNode.TestModeToggle);
            testModeToggle.value = VivoxSettings.Instance.IsTestMode;
            testModeToggle.RegisterValueChangedCallback((ChangeEvent<bool> testModeChange) => {
                var settings = VivoxSettings.Instance;
                settings.IsTestMode = testModeChange.newValue;
                if (!testModeChange.newValue)
                {
                    settings.TokenKey = "";
                    var KeyVar = parentElement.Q<TextElement>(UxmlNode.KeyVar);
                    KeyVar.text = UIStrings.TestKey;
                }
                else
                {
                    FetchCredentials(parentElement);
                }
                settings.Save();
            });
        }

        static void SetUpAuthPackageWarning(VisualElement parentElement)
        {
            var authPackageWarning = parentElement.Q<TextElement>(UxmlNode.AuthPackageWarning);
            authPackageWarning.text = "The Authentication Package has not been imported. For the easiest Vivox Token Vending experience, import the Unity Authentication Package. Otherwise, select the Documentation link and then refer to the Access Token Developer Guide.";
        }

        static void RemoveAuthPackageWarning(VisualElement parentElement)
        {
            var authPackageWarning = parentElement.Q<TextElement>(UxmlNode.AuthPackageWarning);
            authPackageWarning.text = "";
        }

        static void RemoveExternalLinkIcons(VisualElement parentElement)
        {
            var externalLinks = parentElement.Q<VisualElement>(UxmlNode.ExternalLinks + "Dash");
            externalLinks.visible = false;
            externalLinks = parentElement.Q<VisualElement>(UxmlNode.ExternalLinks + "Docs");
            externalLinks.visible = false;
        }

        static void SetUpDashboardButton(VisualElement parentElement)
        {
            var dashboardSdkButton = parentElement.Q(UxmlNode.Dashboard);
            if (dashboardSdkButton != null)
            {
                var clickable = new Clickable(() =>
                {
                    //EditorGameServiceAnalyticsSender.SendProjectSettingsDownloadUserReportingSDKEvent();
                    Application.OpenURL(URL.Dashboard);
                });
                dashboardSdkButton.AddManipulator(clickable);
            }
        }

        static void SetUpDocumentationButton(VisualElement parentElement)
        {
            var downloadSdkButton = parentElement.Q(UxmlNode.Documentation);
            if (downloadSdkButton != null)
            {
                var clickable = new Clickable(() =>
                {
                    //EditorGameServiceAnalyticsSender.SendProjectSettingsDownloadUserReportingSDKEvent();
                    Application.OpenURL(URL.Documentation);
                });
                downloadSdkButton.AddManipulator(clickable);
            }
        }

        static void SetUpCredentials(VisualElement parentElement)
        {
            var ServerVar = parentElement.Q<TextElement>(UxmlNode.ServerVar);
            var DomainVar = parentElement.Q<TextElement>(UxmlNode.DomainVar);
            var IssuerVar = parentElement.Q<TextElement>(UxmlNode.IssuerVar);
            var KeyVar = parentElement.Q<TextElement>(UxmlNode.KeyVar);

            ServerVar.text = VivoxSettings.Instance.Server;
            DomainVar.text = VivoxSettings.Instance.Domain;
            KeyVar.text = !string.IsNullOrEmpty(VivoxSettings.Instance.TokenKey) ? VivoxSettings.Instance.TokenKey : UIStrings.TestKey;
            IssuerVar.text = VivoxSettings.Instance.TokenIssuer;
            ServerVar.SetEnabled(false);
            DomainVar.SetEnabled(false);
            KeyVar.SetEnabled(false);
            IssuerVar.SetEnabled(false);
        }

        /// <summary>
        /// Shows and hides UI elements based on fetching value to indicate to the customer what is happening.
        /// </summary>
        /// <param name="parentElement"></param>
        /// <param name="fetching">Show fetching text ui and hide settings when fetching is true.  Show settings and hide fetching text when fetching is false.</param>
        private static void SetUIFetchingCreds(VisualElement parentElement, bool fetching = false)
        {
            var fetchCredsTextElement = parentElement.Q<TextElement>(UxmlNode.FetchCredsText);
            var configurationSettingsContainer = parentElement.Q<VisualElement>(UxmlNode.ConfigurationSettingsContainer);

            fetchCredsTextElement.style.display = fetching ? DisplayStyle.Flex : DisplayStyle.None;
            configurationSettingsContainer.style.display = fetching ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static void FetchCredentials(VisualElement parentElement)
        {
            if (VivoxApiClient.Instance.IsFetchingCreds)
            {
                SetUIFetchingCreds(parentElement, false);
                return;
            }

            var ServerVar = parentElement.Q<TextElement>(UxmlNode.ServerVar);
            var DomainVar = parentElement.Q<TextElement>(UxmlNode.DomainVar);
            var IssuerVar = parentElement.Q<TextElement>(UxmlNode.IssuerVar);
            var KeyVar = parentElement.Q<TextElement>(UxmlNode.KeyVar);
            SetUIFetchingCreds(parentElement,true);
            if (String.IsNullOrEmpty(CloudProjectSettings.projectId))
            {
                Debug.LogError("[Vivox]: ProjectId not set. You must link a Unity Dashboard project with Vivox Credentials before using the Vivox Services Page");
                return;
            }

            VivoxApiClient.Instance.GetAndSetVivoxCredentials(OnCredentialsFetched, HandleException);

            void OnCredentialsFetched(GetVivoxCredentialsResponse credResp)
            {
                if (VivoxSettings.Instance.IsTestMode)
                {
                    KeyVar.text = VivoxSettings.Instance.TokenKey;
                }
                else
                {
                    KeyVar.text = UIStrings.TestKey;
                }

                ServerVar.text = VivoxSettings.Instance.Server;
                DomainVar.text = VivoxSettings.Instance.Domain;
                IssuerVar.text = VivoxSettings.Instance.TokenIssuer;
                EditorGameServiceAnalyticsSender.SendCredentialPullEvent();
                SetUIFetchingCreds(parentElement, false);
            }

            void HandleException(Exception exception)
            {
                ServerVar.text = "";
                DomainVar.text = "";
                KeyVar.text = "";
                IssuerVar.text = "";
                Debug.LogError(exception);
                SetUIFetchingCreds(parentElement,false);
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new VivoxSettingsProvider(GenerateProjectSettingsPath(k_Title), SettingsScope.Project);
        }

        static class UIStrings
        {
            public const string TestKey = "Only pulled and saved when test mode is enabled";
        }

        static class UxmlPath
        {
            public const string Common = "Packages/com.unity.services.vivox/Editor/UXML/VivoxPackageEditor.uxml";
        }

        static class UssPath
        {
            public const string Common = "Packages/com.unity.services.vivox/Editor/StyleSheets/VivoxPackageEditor.uss";
        }

        static class UxmlNode
        {
            public const string Dashboard = "DashboardLinkButton";
            public const string Documentation = "DocumentationLinkButton";
            public const string CheckConnection = "ConnectionButton";
            public const string ServerVar = "ServerVar";
            public const string DomainVar = "DomainVar";
            public const string IssuerVar = "IssuerVar";
            public const string KeyVar = "KeyVar";
            public const string TestModeToggle = "TestToggle";
            public const string ExternalLinks = "ExternalLink";
            public const string AuthPackageWarning = "AuthPackageWarning";
            public const string FetchCredsText = "FetchCredsText";
            public const string ConfigurationSettingsContainer = "ConfigurationSettingsContainer";
        }

        static class URL
        {
            public const string Dashboard = "https://dashboard.unity3d.com/vivox";
            public const string Documentation = "https://docs.unity.com/ugs/en-us/manual/vivox-unity/manual/Unity/vivox-unity-first-steps";
        }
    }
}
