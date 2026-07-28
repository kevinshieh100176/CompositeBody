using System;
using Unity.Services.Core.Editor.OrganizationHandler;
using Unity.Services.DeploymentApi.Editor;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi;
using UnityEditor;
using UnityEngine;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Deployment
{
    class MatchmakerDashboardQueueUrlResolver
    {
        private const string k_ServicesSettingsURI = "Project/Services";
        private const string k_EnvironmentsSettingsURI = "Project/Services/Environments";

        readonly IOrganizationHandler m_OrganizationHandler;
        readonly Func<IEnvironmentProvider> m_EnvironmentProviderFunc;
        readonly IProjectIdProvider m_ProjectIdProvider;

        private string OrganizationId => m_OrganizationHandler.Key;
        private string ProjectId => m_ProjectIdProvider.ProjectId;
        private string EnvironmentId => m_EnvironmentProviderFunc().Current;

        internal MatchmakerDashboardQueueUrlResolver(
            IOrganizationHandler organizationHandler,
            IProjectIdProvider projectIdProvider,
            Func<IEnvironmentProvider> environmentProviderFunc
        )
        {
            m_OrganizationHandler  = organizationHandler;
            m_ProjectIdProvider = projectIdProvider;
            m_EnvironmentProviderFunc = environmentProviderFunc;
        }

        public string GetQueueUrl()
        {
            if (string.IsNullOrEmpty(OrganizationId) || !Guid.TryParse(ProjectId, out var projGuid) || projGuid == Guid.Empty)
            {
                throw new UnlinkedCloudProjectException();
            }
            if (!Guid.TryParse(EnvironmentId, out var envGuid) || envGuid == Guid.Empty)
            {
                throw new UnsetEnvironmentException();
            }
            // Unity dashboard URLs and the internal matchmaker API use (and expose) queue IDs to reference queues.
            // However, the public matchmaker admin API (which is used in this SDK) uses the queue name instead of the
            // queue ID (it doesn't expose the ID at all), so there is no way for us to link directly to a queue in the
            // dashboard. Due to this, we link to the dashboard queues overview page instead of the specific queue
            // details page. The user can find the queue by browsing the list of queue names on this page.
            return $"https://cloud.unity.com/home/organizations/{OrganizationId}/projects/{ProjectId}/environments/{EnvironmentId}/matchmaker/queues";
        }

        public string GetQueueUrlOrOpenProjectCloudSettings()
        {
            try
            {
                return GetQueueUrl();
            }
            catch (UnlinkedCloudProjectException)
            {
                Debug.LogWarning("No cloud project is linked.");
                SettingsService.OpenProjectSettings(k_ServicesSettingsURI);
            }
            catch (UnsetEnvironmentException)
            {
                Debug.LogWarning("No cloud environment is set.");
                SettingsService.OpenProjectSettings(k_EnvironmentsSettingsURI);
            }

            return null;
        }

        public bool TryGetQueueUrl(out string url)
        {
            try
            {
                url = GetQueueUrl();
                return true;
            }
            catch (Exception)
            {
                url = string.Empty;
                return false;
            }
        }
    }

    class UnlinkedCloudProjectException : Exception {}

    class UnsetEnvironmentException : Exception {}
}
