using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Deploy;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Fetch;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Parser;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Model;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Deployment
{
    interface IEditorMatchmakerDeploymentHandler : IMatchmakerDeployHandler
    {
        public IConfigApiClient Client { get; set; }
    }

    class EditorMatchmakerDeploymentHandler : MatchmakerDeployHandler, IEditorMatchmakerDeploymentHandler
    {
        public IConfigApiClient Client { get { return m_configApiClient;} set { m_configApiClient = value; } }

        public EditorMatchmakerDeploymentHandler(
            IMatchmakerConfigParser configParser,
            IDeepEqualityComparer deepEqualityComparer) : base(null, configParser, deepEqualityComparer)
        {}
    }
}
