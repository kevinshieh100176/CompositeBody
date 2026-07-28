using Unity.Services.Multiplayer.Components;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.Services.Multiplayer.Editor.Components
{
    [CustomPropertyDrawer(typeof(CreateSessionOptions))]
    class CreateSessionOptionsSectionDrawer : PropertyDrawer
    {
        const string k_AdvancedFoldoutKey = "SessionConnector.CreateSessionOptions.Advanced";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement();

            container.Add(new PropertyField(property.FindPropertyRelative("m_MaxPlayers")));
            container.Add(new PropertyField(property.FindPropertyRelative("m_SessionName")));

            var advancedFoldout = new Foldout
            {
                text = "Advanced",
                value = UnityEditor.SessionState.GetBool(k_AdvancedFoldoutKey, false)
            };
            advancedFoldout.RegisterValueChangedCallback(evt =>
                UnityEditor.SessionState.SetBool(k_AdvancedFoldoutKey, evt.newValue));

            advancedFoldout.Add(new PropertyField(property.FindPropertyRelative("m_IsPrivate")));
            advancedFoldout.Add(new PropertyField(property.FindPropertyRelative("m_IsLocked")));
            advancedFoldout.Add(new PropertyField(property.FindPropertyRelative("m_Password")));

            container.Add(advancedFoldout);

            return container;
        }
    }
}
