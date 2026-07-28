using System.IO;
using Newtonsoft.Json;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Parser;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.IO;
using Unity.Services.Multiplayer.Editor.Shared.Assets;
using UnityEditor;
using UnityEngine;
using PathIO = System.IO.Path;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Model
{
    [HelpURL("https://docs.unity3d.com/Packages/com.unity.services.multiplayer@1.0/manual/Matchmaker/Authoring/index.html")]
    class MatchmakerAsset : ScriptableObject, IPath, ISerializationCallbackReceiver
    {
        const string k_DefaultFileName = "Matchmaker";
        string m_Path;

        public string Name { get; set; }

        public string Path { get => m_Path; set => SetPath(value); }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { /* Not needed */ }

        void ISerializationCallbackReceiver.OnAfterDeserialize() { Name = System.IO.Path.GetFileName(Path); }

        void SetPath(string path)
        {
            var fileName = PathIO.GetFileName(path);
            Name = fileName;
            m_Path = path;
        }

        [MenuItem("Assets/Create/Services/Matchmaker Environment Config", false, 81)]
        public static void CreateConfigEnv()
        {
            var fileName = $"{k_DefaultFileName}Environment{IMatchmakerConfigParser.EnvironmentConfigExtension}";
            var content = JsonConvert.SerializeObject(EnvironmentConfig.GetDefault(), MatchmakerConfigLoader.GetSerializationSettings());
#if UNITY_6000_4_OR_NEWER
            ProjectWindowUtil.CreateAssetWithTextContent(fileName, content);
#else
            ProjectWindowUtil.CreateAssetWithContent(fileName, content);
#endif
        }

        [MenuItem("Assets/Create/Services/Matchmaker Queue Config", false, 81)]
        public static void CreateConfig()
        {
            var fileName = $"{k_DefaultFileName}Queue{IMatchmakerConfigParser.QueueConfigExtension}";
            var content = JsonConvert.SerializeObject(QueueConfig.GetDefault(), MatchmakerConfigLoader.GetSerializationSettings());
#if UNITY_6000_4_OR_NEWER
            ProjectWindowUtil.CreateAssetWithTextContent(fileName, content);
#else
            ProjectWindowUtil.CreateAssetWithContent(fileName, content);
#endif
        }

        public static void CreateQueueConfig(string queueName, bool focus = false)
        {
            var queueConfig = QueueConfig.GetDefault();
            queueConfig.Name = new QueueName(queueName);

            SaveQueueConfig(queueConfig, focus);
        }

        public static void SaveQueueConfig(QueueConfig config, bool focus = false)
        {
            var filepath = EditorUtility.SaveFilePanelInProject(
                "Queue Config save dialog",
                config.Name.ToString(),
                // extensions are currently defined with a leading . character which would cause issues with the save dialog
                IMatchmakerConfigParser.QueueConfigExtension.TrimStart('.'),
                "Choose a location in your project to save the matchmaker queue configuration.");

            // save was cancelled
            if (filepath.Length == 0)
            {
                return;
            }

            var content = JsonConvert.SerializeObject(config, MatchmakerConfigLoader.GetSerializationSettings());

            File.WriteAllText(filepath, content);
            AssetDatabase.ImportAsset(filepath, ImportAssetOptions.ForceSynchronousImport);

            if (focus)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(filepath);
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }
    }
}
