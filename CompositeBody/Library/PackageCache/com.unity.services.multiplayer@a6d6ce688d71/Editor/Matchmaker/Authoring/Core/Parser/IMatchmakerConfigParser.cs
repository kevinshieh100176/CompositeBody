using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Parser
{
    interface IMatchmakerConfigParser
    {
        public const string QueueConfigExtension = ".mmq";
        public const string EnvironmentConfigExtension = ".mme";

        class ParsingResult
        {
            public List<MatchmakerConfigResource> failed { get; set; } = new();

            public List<MatchmakerConfigResource> parsed { get; set; } = new();
        }

        Task<ParsingResult> Parse(IReadOnlyList<string> filePaths, CancellationToken ct);

        /// <summary>
        /// Serialize matchmaker config to path if modified
        /// </summary>
        /// <param name="config">The config to serialize</param>
        /// <param name="path">The path of the existing config</param>
        /// <param name="ct"></param>
        /// <returns>A tuple with bool set to true if the file has been authored and a string for error</returns>
        Task<(bool, string)> SerializeToFile(IMatchmakerConfig config, string path, CancellationToken ct);

        IMatchmakerConfig Parse(string path);
    }
}
