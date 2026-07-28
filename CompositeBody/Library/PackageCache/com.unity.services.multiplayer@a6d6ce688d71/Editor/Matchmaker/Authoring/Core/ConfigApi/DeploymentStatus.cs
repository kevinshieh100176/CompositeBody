using System.Collections.Generic;
using System.Linq;
using Unity.Services.DeploymentApi.Editor;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi
{
    static class DeploymentStatus
    {
        internal static DeploymentApi.Editor.DeploymentStatus Get(string message, List<ErrorResponse> errorResponse, SeverityLevel severityLevel = SeverityLevel.Error, PoolUpdateResult poolUpdateResult = null)
        {
            var messageDetail = new string(message);
            if (errorResponse.Count > 0)
            {
                messageDetail += $" with errors: {string.Join(", ", errorResponse.Select( e => $"{e.ResultCode}: {e.Message}"))}";
            }
            if (poolUpdateResult != null && !string.IsNullOrEmpty(poolUpdateResult.ToString()))
            {
                messageDetail += $". {poolUpdateResult}";
            }
            return new DeploymentApi.Editor.DeploymentStatus(message, messageDetail, severityLevel);
        }

        internal static DeploymentApi.Editor.DeploymentStatus Get(string action, string itemType, bool dryRun, string itemName = "", SeverityLevel severityLevel = SeverityLevel.Info, PoolUpdateResult poolUpdateResult = null)
        {
            var messageDetails = $"The {itemType} {(string.IsNullOrEmpty(itemName) ?  "": $"{itemName} ")}" +
                $"{(dryRun ? "will be" : "has been")} {action.ToLower()}." +
                $"{(string.IsNullOrEmpty(poolUpdateResult?.ToString()) ? "" : $" {poolUpdateResult}")}";
            return new DeploymentApi.Editor.DeploymentStatus("Deployed", messageDetails, severityLevel);
        }
    }
}
