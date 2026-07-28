using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Matchmaker.Models;

namespace Unity.Services.Matchmaker.Overrides
{
    internal interface IABRemoteConfig
    {
        List<Override> Overrides { get; }
        string AssignmentId { get; }

        Task RefreshGameOverridesAsync();
    }
}
