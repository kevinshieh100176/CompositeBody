using UnityEditor;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi
{
    interface IProjectIdProvider
    {
        string ProjectId { get; }
    }

    class ProjectIdProvider : IProjectIdProvider
    {
        public string ProjectId => CloudProjectSettings.projectId;
    }
}
