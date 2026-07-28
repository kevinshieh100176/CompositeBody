using Unity.Services.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Unity.Services.Vivox.Editor
{
    [InitializeOnLoad]
    class VivoxService : IEditorGameService
    {
        const string VivoxServiceRanOnce = "VivoxServiceRanOnce";
        const string VivoxProjectIdFetched = "VivoxProjectIdFetched";
        public VivoxService()
        {
#if !ENABLE_EDITOR_GAME_SERVICES
            if (!CheckIfProjectIdUpdated())
            {
                return;
            }
#endif

        }

        [InitializeOnLoadMethod]
        static void InternalGetCredentials()
        {
            VivoxApiClient.Instance.GetAndSetVivoxCredentials();
        }

        static bool CheckIfProjectIdUpdated()
        {
            // If the project id is null or not the same as the project we have looked up already then update it, set VivoxServiceRanOnce to false since the project changed and return true
            if (SessionState.GetString(VivoxProjectIdFetched, "NOT_SET_YET") != CloudProjectSettings.projectId)
            {
                SessionState.SetString(VivoxProjectIdFetched, CloudProjectSettings.projectId);
                SessionState.SetBool(VivoxServiceRanOnce, false);
                return true;
            }
            return false;
        }

        public string Name => "Vivox Service";

        public IEditorGameServiceIdentifier Identifier { get; } = new VivoxIdentifier();

        public bool RequiresCoppaCompliance => false;

        public bool HasDashboard => true;

        /// <summary>
        /// Getter for the formatted dashboard url
        /// If <see cref="HasDashboard"/> is false, this field only need return null or empty string
        /// </summary>
        /// <returns>The formatted URL</returns>
        public string GetFormattedDashboardUrl()
        {
#if ENABLE_EDITOR_GAME_SERVICES
            return $"https://dashboard.unity3d.com/organizations/{CloudProjectSettings.organizationKey}/projects/{CloudProjectSettings.projectId}/vivox/overview";
#else
            return "https://dashboard.unity3d.com/vivox/";
#endif
        }

        public IEditorGameServiceEnabler Enabler => null;
    }
}
