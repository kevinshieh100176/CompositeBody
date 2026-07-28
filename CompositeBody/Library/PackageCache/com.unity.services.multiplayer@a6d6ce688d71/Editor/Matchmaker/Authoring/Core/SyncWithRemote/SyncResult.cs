using System.Collections.Generic;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.SyncWithRemote
{
    class SyncResult
    {
        public string AbortMessage { get; set; } = "";

        public List<MatchmakerConfigResource> UpToDate { get; } = new();

        public List<MatchmakerConfigResource> NotInSync { get; } = new();

        public List<MatchmakerConfigResource> Missing { get; } = new();

        public List<MatchmakerConfigResource> Failed { get; } = new();
    }
}
