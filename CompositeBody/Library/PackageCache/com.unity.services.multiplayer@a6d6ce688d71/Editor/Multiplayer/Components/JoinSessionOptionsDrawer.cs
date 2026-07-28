using Unity.Services.Multiplayer.Components;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Services.Multiplayer.Editor.Components
{
    [CustomPropertyDrawer(typeof(Multiplayer.Components.JoinSessionOptions))]
    class JoinSessionOptionsDrawer : PropertyDrawer
    {
        const string k_HiddenClass = "hidden";

        [SerializeField]
        StyleSheet m_MultiplayerComponentsStyleSheet;

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement(); 
            if (m_MultiplayerComponentsStyleSheet != null)
                container.styleSheets.Add(m_MultiplayerComponentsStyleSheet);
            var sessionIdField =
                new PropertyField(property.FindPropertyRelative("m_SessionId"), "Session ID");
            var sessionCodeField =
                new PropertyField(property.FindPropertyRelative("m_SessionCode"), "Session Code");
            var joinModeProp = property.FindPropertyRelative("m_JoinMode");
            container.Add(new PropertyField(joinModeProp, "Join Mode"));
            container.Add(sessionIdField);
            container.Add(sessionCodeField);
            container.Add(new PropertyField(property.FindPropertyRelative("m_Password")));

            void UpdateVisibility()
            {
                property.serializedObject.Update();
                UpdateOperationVisibility(joinModeProp, sessionIdField, sessionCodeField);
            }

            UpdateVisibility();
            container.TrackPropertyValue(property, _ => UpdateVisibility());
            return container;
        }


        static void UpdateOperationVisibility(SerializedProperty joinModeProp,
            VisualElement sessionIdField,
            VisualElement sessionCodeField)
        {
            joinModeProp.serializedObject.Update();
            var joinMode = (JoinSessionMode)joinModeProp.enumValueIndex;
            sessionIdField.EnableInClassList(k_HiddenClass, joinMode != JoinSessionMode.ById);
            sessionCodeField.EnableInClassList(k_HiddenClass, joinMode != JoinSessionMode.ByCode);
        }
    }
}
