using System;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Parser;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.IO;
using UnityEditor.AssetImporters;
using UnityEngine;
// The FileSystem has to be from IO.FileSystem and not the one from Core.IO.FileSystem because the async breaks the import process.
using FileSystem = Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.IO.FileSystem;
using Logger = Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Logging.Logger;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Model
{
    [HelpURL("https://docs.unity3d.com/Packages/com.unity.services.multiplayer@1.0/manual/Matchmaker/Authoring/index.html")]
    [ScriptedImporter(3, new[]
    {
        IMatchmakerConfigParser.QueueConfigExtension,
        IMatchmakerConfigParser.EnvironmentConfigExtension
    })]
    class MatchmakerConfigAssetImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<MatchmakerAsset>();
            asset.Path = assetPath;
            ctx.AddObjectToAsset("MainAsset", asset);

            try
            {
                // We only load the content to populate the runtime asset.
                // QueueConfig is not serializable so there is no point saving it as part of the asset.
                var content = new MatchmakerConfigLoader(new FileSystem()).Parse(ctx.assetPath);
                if (content is QueueConfig config)
                {
                    var settings = ScriptableObject.CreateInstance<MatchmakerQueueAsset>();
                    settings.hideFlags = HideFlags.NotEditable;
                    settings.Name = config.Name.ToString();
                    settings.MaxPlayers = config.MaxPlayersPerTicket;
                    settings.TimeoutSeconds = config.DefaultPool.TimeoutSeconds;
                    settings.name = settings.Name;
                    ctx.AddObjectToAsset("MatchmakerQueueAsset", settings);
                }
            }
            catch (Exception e)
            {
                ctx.LogImportError($"Failed to read Matchmaker config content: {e.Message}");
            }

            var logger = new Logger();
            logger.LogVerbose($"[{nameof(MatchmakerConfigAssetImporter)}][{GetHashCode()}] Imported asset: {ctx.assetPath}");
        }
    }
}
