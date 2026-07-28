using System.Collections.Generic;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Deploy
{
    class DeployResult
    {
        public string AbortMessage { get; set; } = "";

        public List<MatchmakerConfigResource> Created { get; } = new();

        public List<MatchmakerConfigResource> Updated { get; } = new();

        public List<MatchmakerConfigResource> Deleted { get; } = new();

        public List<MatchmakerConfigResource> Authored { get; } = new();

        public List<MatchmakerConfigResource> Failed { get; } = new();
    }
}
