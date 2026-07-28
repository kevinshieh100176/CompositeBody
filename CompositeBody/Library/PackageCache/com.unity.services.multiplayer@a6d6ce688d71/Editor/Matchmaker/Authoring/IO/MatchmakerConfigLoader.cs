using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Fetch;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.IO;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Parser;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.IO
{
    class MatchmakerConfigLoader : IMatchmakerConfigParser, IDeepEqualityComparer
    {
        const string k_FailedToDeserializeMessage = "Failed to Deserialize";
        readonly IFileSystem m_FileSystem;
        static JsonSerializerSettings s_SerializerSettings = new JsonSerializerSettings()
        {
            Converters =
            {
                new DataMemberEnumConverter(),
                new ResourceNameConverter(),
                new MatchHostingConfigTypeConverted(),
                new FilterValueConverter(), // Needs to be before JsonObjectSpecializedConverter, since FilterValue extends JsonObject
                new JsonObjectSpecializedConverter()
            },
            Formatting = Formatting.Indented,
            ContractResolver = new CustomContractResolver()
        };

        public MatchmakerConfigLoader(IFileSystem fileSystem)
        {
            m_FileSystem = fileSystem;
        }

        public Task<IMatchmakerConfigParser.ParsingResult> Parse(
            IReadOnlyList<string> filePaths,
            CancellationToken ct)
        {
            var result = new IMatchmakerConfigParser.ParsingResult();

            foreach (var path in filePaths)
            {
                var resource = new MatchmakerConfigResource();
                resource.Name = Path.GetFileNameWithoutExtension(path);
                resource.Path = path;
                resource.Type = Path.GetExtension(path) == IMatchmakerConfigParser.QueueConfigExtension
                    ? "Queue Config"
                    : "Environment Config";
                try
                {
                    var config = Parse(path);
                    resource.Content = config;
                    result.parsed.Add(resource);
                }
                catch (MyServiceDeserializationException)
                {
                    result.failed.Add(resource);
                }
            }

            return Task.FromResult(result);
        }

        public async Task<(bool, string)> SerializeToFile(
            IMatchmakerConfig config,
            string path,
            CancellationToken ct)
        {
            try
            {
                string json = JsonConvert.SerializeObject(config, Formatting.Indented, s_SerializerSettings);
                await m_FileSystem.WriteAllText(path, json, ct);
            }
            catch (Exception e)
            {
                return (false, e.Message);
            }
            return (true, string.Empty);
        }

        public IMatchmakerConfig Parse(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path), "cannot deserialize a config with an empty path.");
            }

            try
            {
                var fileContent = m_FileSystem.ReadAllText(path, CancellationToken.None).Result;

                var ext = Path.GetExtension(path);
                if (ext == IMatchmakerConfigParser.QueueConfigExtension)
                    return JsonConvert.DeserializeObject<QueueConfig>(fileContent, s_SerializerSettings);
                return JsonConvert.DeserializeObject<EnvironmentConfig>(fileContent, s_SerializerSettings);
            }
            catch (Exception e)
                when(e is SerializationException
                    or JsonSerializationException
                    or JsonReaderException)
                {
                    throw new MyServiceDeserializationException(
                        k_FailedToDeserializeMessage,
                        e.Message,
                        e);
                }
                catch (Exception e)
                {
                    throw new MyServiceDeserializationException(
                        k_FailedToDeserializeMessage,
                        e.Message,
                        e);
                }
        }

        public bool IsDeepEqual<T>(T source, T target)
        {
            if (source == null || target == null)
            {
                return source == null && target == null;
            }

            var sourceJson = JsonConvert.SerializeObject(source, s_SerializerSettings);
            var targetJson = JsonConvert.SerializeObject(target, s_SerializerSettings);
            return sourceJson == targetJson;
        }

        public static bool IsDeserializationError(string description)
        {
            return description != null && description.Contains(k_FailedToDeserializeMessage);
        }

        public static JsonSerializerSettings GetSerializationSettings()
        {
            return s_SerializerSettings;
        }
    }

    class MyServiceDeserializationException : Exception
    {
        public string ErrorMessage;
        public string Details;

        public MyServiceDeserializationException(string message, string details, Exception exception)
            : base(message, exception)
        {
            ErrorMessage = message;
            Details = details;
        }
    }
}
