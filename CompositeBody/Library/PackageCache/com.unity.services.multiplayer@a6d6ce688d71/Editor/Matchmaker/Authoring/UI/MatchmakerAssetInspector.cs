using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Parser;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Deployment;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.IO;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Model;
using Unity.Services.Multiplayer.Editor.Shared.Analytics;
using Unity.Services.Multiplayer.Editor.Shared.UI.DeploymentConfigInspectorFooter;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.UI
{
    [CustomEditor(typeof(MatchmakerConfigAssetImporter))]
    [CanEditMultipleObjects]
    class MatchmakerQueueInspector : AssetImporterEditor
    {
        static JsonSerializerSettings SerializerSettings => MatchmakerConfigLoader.GetSerializationSettings();
        [SerializeField]
        VisualTreeAsset m_MatchmakerEnvironmentUxml;

        Dictionary<int, QueueConfig> m_Configs = new();

        /// <summary>
        /// This declaration allows the AssetEditorInspector to create a custom serialized object
        /// containing our own scriptable object type which displays the inspector.
        /// Using this allows the base editor importer class to handle apply/revert comparison for us.
        /// </summary>
        protected override Type extraDataType => typeof(MatchMakerQueueInspectorConfig);

        /// <summary>
        /// This is automatically called by the base AssetImporterEditor class and is used to initialize the extra data
        /// </summary>
        /// <param name="extraData"></param>
        /// <param name="targetIndex"></param>
        protected override void InitializeExtraDataInstance(Object extraData, int targetIndex)
        {
            var path = AssetDatabase.GetAssetPath(targets[targetIndex]);
            // No need to load the configuration for MatchmakerEnvironment files
            if (path.EndsWith(IMatchmakerConfigParser.EnvironmentConfigExtension))
                return;

            var configuration = LoadConfiguration(AssetDatabase.GetAssetPath(targets[targetIndex]));
            m_Configs.Add(targetIndex, configuration);
            if (extraData is MatchMakerQueueInspectorConfig config)
            {
                config.Initialize(configuration);
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            string matchmakerFilePath = AssetDatabase.GetAssetPath(target);
            bool isRunningOnUnityVersionThatDoesNotSupportThisCustomInspector = false;
#if !UNITY_6000_0_OR_NEWER
            isRunningOnUnityVersionThatDoesNotSupportThisCustomInspector = true;
#endif
            if (isRunningOnUnityVersionThatDoesNotSupportThisCustomInspector
                || matchmakerFilePath.EndsWith(IMatchmakerConfigParser.EnvironmentConfigExtension)) //this is the MatchmakerEnvironment file, which does not have the same structure as the MatchmakerQueue one
            {
                m_MatchmakerEnvironmentUxml.CloneTree(root);
                ShowResourceBody(root);
            }
            else
            {
                AddMatchmakerConfigInspector(root);
            }
            SetupConfigFooter(root);
            root.Add(new IMGUIContainer(ApplyRevertGUI));
            return root;
        }

        protected override void Apply()
        {
            serializedObject.ApplyModifiedProperties();
            extraDataSerializedObject?.ApplyModifiedProperties();

            var allJson = new Dictionary<string, string>(targets.Length);
            List<Exception> writingExceptions = new List<Exception>(targets.Length);
            for (int i = 0; i < targets.Length; i++)
            {
                try
                {
                    var matchmakerFilePath = AssetDatabase.GetAssetPath(targets[i]);
                    if (m_Configs.TryGetValue(i, out var config))
                    {
                        if (extraDataTargets[i] is MatchMakerQueueInspectorConfig editedConfig)
                        {
                            string editedJson = editedConfig.ToJson(SerializerSettings);

                            // Use a deep merge approach to preserve fields in the config that aren't in the inspector's model
                            string currentJson = JsonConvert.SerializeObject(config, SerializerSettings);

                            // Create a merged JSON by deserializing current, then overwriting with edited values
                            JObject currentObj = JObject.Parse(currentJson);
                            JObject editedObj = JObject.Parse(editedJson);

                            // Merge the edited object into the current object, overwriting only the fields that exist in edited
                            currentObj.Merge(editedObj,
                                new JsonMergeSettings
                                {
                                    MergeArrayHandling = MergeArrayHandling.Replace,
                                    PropertyNameComparison = StringComparison.InvariantCultureIgnoreCase,
                                    MergeNullValueHandling = MergeNullValueHandling.Ignore,
                                });

                            // Convert back to our config object
                            m_Configs[i] =
                                currentObj.ToObject<QueueConfig>(JsonSerializer.Create(SerializerSettings));
                            string json =
                                JsonConvert.SerializeObject(m_Configs[i], Formatting.Indented, SerializerSettings);
                            allJson.Add(matchmakerFilePath, json);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        "Unable to save the configuration file back to its json format with the current values.",
                        assetTargets[i]);
                    writingExceptions.Add(e);
                }
            }

            if (writingExceptions.Count > 0)
            {
                throw new AggregateException(writingExceptions);
            }
            foreach (var newFile in allJson)
            {
                File.WriteAllText(newFile.Key, newFile.Value);
            }

            base.Apply();
        }

        void SetupConfigFooter(VisualElement myInspector)
        {
            // If the UXML file was imported before the deployment API was available,
            // the footer type would not be part of the visual tree.
            // This is why we are creating the element here instead of trying to load it from the UXML.
            var deploymentConfigInspectorFooter = new DeploymentConfigInspectorFooter();

            // Set up the Dashboard link button only if we can successfully resolve the URL (project is linked & environment is set)
            if (MatchmakerAuthoringServices.Instance.GetService<MatchmakerDashboardQueueUrlResolver>()
                .TryGetQueueUrl(out _))
            {
                deploymentConfigInspectorFooter.DashboardLinkUrlGetter = () =>
                    // This will log an error if the project is not linked (null return value), but this can ONLY
                    // ever happen if the user unlinks the cloud project, keeps the inspector open, and then clicks the
                    // "Go to Dashboard" button. They will still get sent to the settings window to re-link a project.
                    // Once they close or switch the inspector to another asset, the button will go away since there is
                    // no valid dashboard link to send them to.
                    Task.FromResult(MatchmakerAuthoringServices.Instance
                        .GetService<MatchmakerDashboardQueueUrlResolver>().GetQueueUrlOrOpenProjectCloudSettings());
            }

            deploymentConfigInspectorFooter.BindGUI(
                AssetDatabase.GetAssetPath(target),
                MatchmakerAuthoringServices.Instance.GetService<ICommonAnalytics>(),
                "matchmaker");
            myInspector.Add(deploymentConfigInspectorFooter);
        }

        //--------------------------
        //Methods to maintain backward-compatibility with the MatchMakerEnvironment file, which used the same custom inspector of MatchmakerQueue
        void ShowResourceBody(VisualElement myInspector)
        {
            var body = myInspector.Q<TextField>();
            if (targets.Length == 1)
            {
                body.visible = true;
                body.value = ReadResourceBody(targets[0]);
            }
            else
            {
                body.visible = false;
            }
        }

        static string ReadResourceBody(UnityEngine.Object resource)
        {
            const int k_MaxLines = 75;
            var path = AssetDatabase.GetAssetPath(resource);
            var lines = File.ReadLines(path).Take(k_MaxLines).ToList();
            if (lines.Count == k_MaxLines)
            {
                lines.Add("...");
            }
            return string.Join(Environment.NewLine, lines);
        }

        //--------------------------
        static QueueConfig LoadConfiguration(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    return new MatchmakerConfigLoader(new FileSystem()).Parse(path) as QueueConfig;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error loading matchmaker queue data: {e.Message}");
                }
            }
            return QueueConfig.GetDefault();
        }

        void AddMatchmakerConfigInspector(VisualElement root)
        {
            var inspector = new PropertyField
            {
                bindingPath = nameof(MatchMakerQueueInspectorConfig.Data)
            };
            inspector.Unbind();
            inspector.Bind(extraDataSerializedObject);

            root.Add(inspector);
        }
    }
}
