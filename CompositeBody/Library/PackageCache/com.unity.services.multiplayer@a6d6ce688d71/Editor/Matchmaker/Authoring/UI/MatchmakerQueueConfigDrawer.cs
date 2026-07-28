using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Deployment;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.UI
{
    [CustomPropertyDrawer(typeof(MatchmakerQueueData))]
    class MatchmakerQueueConfigDrawer : PropertyDrawer
    {
        [SerializeField]
        VisualTreeAsset m_MatchmakerInspectorUxml;

        private const string k_LinkLabelIdentifier = "DashboardLinkLabel";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (m_MatchmakerInspectorUxml == null)
            {
                // Fallback to default PropertyField if UXML file is not found
                Debug.LogWarning("MatchmakerQueueInspector's UXML not found, using default PropertyField");
                return new PropertyField(property);
            }

            var container = m_MatchmakerInspectorUxml.Instantiate();
            container.Bind(property.serializedObject);

            var linkLabel = container.Q<Label>(k_LinkLabelIdentifier);
            if (linkLabel != null)
            {
                linkLabel.AddManipulator(new Clickable(DashboardLinkOnClick));
            }
            return container;
        }

        void DashboardLinkOnClick(EventBase clickEvent)
        {
            if (clickEvent.target is Label { name : k_LinkLabelIdentifier})
            {
                var url = MatchmakerAuthoringServices.Instance.GetService<MatchmakerDashboardQueueUrlResolver>()
                    .GetQueueUrlOrOpenProjectCloudSettings();
                if (url != null)
                {
                    Application.OpenURL(url);
                }
            }
        }
    }
}
