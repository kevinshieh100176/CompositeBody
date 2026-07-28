using Unity.Services.Multiplayer.Components;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.Services.Multiplayer.Editor.Components
{
    [CustomEditor(typeof(SessionConnectorBehaviour))]
    class SessionConnectorBehaviorEditor : UnityEditor.Editor
    {
        const FoldoutState k_FoldoutDefaultState = FoldoutState.Opened;
        const string k_SessionConnectorPropertyName = "SessionConnector";
        const string k_UnityFoldoutInputClassName = "unity-foldout__input";
        const string k_FoldoutKeyPrefix = nameof(SessionConnectorBehaviorEditor) + "." + nameof(Foldout);

        static bool AsBool(FoldoutState state)
        {
            return state is FoldoutState.Opened;
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            var connectorProperty = serializedObject.FindProperty(k_SessionConnectorPropertyName);
            var connectorField = root.Query<PropertyField>()
                .Where(p => p.bindingPath == connectorProperty.propertyPath)
                .First();

            var parent = connectorField.parent;
            var index = parent.IndexOf(connectorField);
            connectorField.RemoveFromHierarchy();

            var placeholder = new VisualElement();
            parent.Insert(index, placeholder);

            UpdateConnectorDisplay(placeholder, connectorField, connectorProperty);

            connectorField.TrackPropertyValue(connectorProperty, _ =>
                UpdateConnectorDisplay(placeholder, connectorField, connectorProperty));

            return root;
        }

        void UpdateConnectorDisplay(VisualElement placeholder, PropertyField connectorField,
            SerializedProperty connectorProperty)
        {
            serializedObject.Update();
            placeholder.Clear();

            if (connectorProperty.objectReferenceValue == null)
            {
                placeholder.Add(connectorField);
            }
            else
            {
                var foldoutKey = $"{k_FoldoutKeyPrefix}.{GetUniqueId()}";
                var foldout = new Foldout
                {
                    value = UnityEditor.SessionState.GetBool(foldoutKey, AsBool(k_FoldoutDefaultState))
                };
                var toggle = foldout.Q<Toggle>();
                toggle.RegisterValueChangedCallback(evt =>
                    UnityEditor.SessionState.SetBool(foldoutKey, evt.newValue));

                var toggleInput = toggle
                    .Query<VisualElement>()
                    .Class(k_UnityFoldoutInputClassName)
                    .Build()
                    .First();
                toggleInput.Add(connectorField);

                var inspector = new InspectorElement(
                    new SerializedObject(connectorProperty.objectReferenceValue));
                var box = new Box();
                box.Add(inspector);
                foldout.Add(box);

                placeholder.Add(foldout);
            }
        }

        string GetUniqueId()
        {
#if UNITY_6000_4_OR_NEWER
            return target.GetEntityId().ToString();
#else
            return target.GetInstanceID().ToString();
#endif
        }

        enum FoldoutState
        {
            Closed = 0,
            Opened = 1
        }
    }
}
