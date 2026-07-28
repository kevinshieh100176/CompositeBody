using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Services.Multiplayer.Editor.Components
{
    [CustomPropertyDrawer(typeof(Multiplayer.Components.SessionNetworkSettings))]
    class SessionNetworkSettingsDrawer : PropertyDrawer
    {
        const string k_HiddenClass = "hidden";

        [SerializeField]
        StyleSheet m_MultiplayerComponentsStyleSheet;

        bool m_NetworkOptionsFoldout = true;
        const string k_SessionNetworkSettingsTooltip =
            "Network type and hosting options for create and create-or-join.";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var foldout = new Foldout { text = "Network Options", value = m_NetworkOptionsFoldout };
            foldout.Q<Toggle>().tooltip = k_SessionNetworkSettingsTooltip;
            if (m_MultiplayerComponentsStyleSheet != null)
                foldout.styleSheets.Add(m_MultiplayerComponentsStyleSheet);
            foldout.RegisterValueChangedCallback(evt => m_NetworkOptionsFoldout = evt.newValue);

            var createNetworkOnSessionCreationProp = property.FindPropertyRelative("m_CreateNetworkOnSessionCreation");
            foldout.Add(new PropertyField(createNetworkOnSessionCreationProp, "Create Network On Session Creation"));

            var networkTypeProp = property.FindPropertyRelative("m_Network");
            foldout.Add(new PropertyField(networkTypeProp));

            var directContainer = new VisualElement();
            var directIPPort = property.FindPropertyRelative("m_DirectIPPort");
            directContainer.Add(new PropertyField(directIPPort.FindPropertyRelative("m_Ip"), "IP"));
            directContainer.Add(new PropertyField(directIPPort.FindPropertyRelative("m_Port")));
            directContainer.Add(new PropertyField(directIPPort.FindPropertyRelative("m_ListenIp"), "Listen IP"));
            foldout.Add(directContainer);

            var relayContainer = new VisualElement();
            var relayOptions = property.FindPropertyRelative("m_RelayOptions");
            relayContainer.Add(new PropertyField(relayOptions.FindPropertyRelative("m_Protocol")));
            relayContainer.Add(new PropertyField(relayOptions.FindPropertyRelative("m_Region")));
            relayContainer.Add(new PropertyField(relayOptions.FindPropertyRelative("m_PreserveRegion")));
            foldout.Add(relayContainer);

            UpdateNetworkTypeVisibility(directContainer, relayContainer, networkTypeProp);
            foldout.TrackPropertyValue(networkTypeProp, p =>
            {
                p.serializedObject.Update();
                UpdateNetworkTypeVisibility(directContainer, relayContainer, p);
            });

            return foldout;
        }

        static void UpdateNetworkTypeVisibility(VisualElement directContainer, VisualElement relayContainer,
            SerializedProperty networkTypeProp)
        {
            networkTypeProp.serializedObject.Update();
            var isDirect = networkTypeProp.enumValueIndex ==
                (int)Multiplayer.Components.SessionNetworkSettings.NetworkType.Direct;
            var isRelay = networkTypeProp.enumValueIndex ==
                (int)Multiplayer.Components.SessionNetworkSettings.NetworkType.Relay;
            directContainer.EnableInClassList(k_HiddenClass, !isDirect);
            relayContainer.EnableInClassList(k_HiddenClass, !isRelay);
        }
    }
}
