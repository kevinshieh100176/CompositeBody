using System.Collections.Generic;
using Unity.Services.Core.Analytics.Internal;
using UnityEngine;

namespace Unity.Services.Matchmaker.Overrides
{
    internal class ABAnalytics : IABAnalytics
    {
        private readonly string _projectId;
        private readonly string _environmentId;
        private readonly IAnalyticsStandardEventComponent _analyticsService;

        public ABAnalytics(string projectId, string environmentId, IAnalyticsStandardEventComponent analyticsService)
        {
            _projectId = projectId;
            _environmentId = environmentId;
            _analyticsService = analyticsService;
        }

        public void SubmitUserAssignmentConfirmedEvent(string rcVariantID, string rcAssignmentID)
        {
            var evt = new Dictionary<string, object>
            {
                ["sdkMethod"] = "Matchmaker.ABTests.ABAnalytics.userAssignmentConfirmed",
                ["projectID"] = _projectId,
                ["rcVariantID"] = rcVariantID,
                ["rcAssignmentID"] = rcAssignmentID,
                ["rcEnvironmentID"] = _environmentId,
                ["assignmentSource"] = "matchmaker"
            };

            _analyticsService?.Record("userAssignmentConfirmed", evt, 1, "com.unity.services.multiplayer");
        }
    }
}
