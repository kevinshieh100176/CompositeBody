using Unity.Services.Multiplayer.Components;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Services.Multiplayer.Editor.Components
{
    [CustomEditor(typeof(SessionConnector))]
    class SessionConnectorEditor : UnityEditor.Editor
    {
        const string k_HiddenClass = "hidden";
        const string k_ErrorStateClass = "state__error";

        [SerializeField] StyleSheet m_MultiplayerComponentsStyleSheet;

        static bool s_OperationFoldout = true;
        static bool s_AdvancedFoldout;

        const string k_NoSessionWarning =
            "A Multiplayer Session is required. Assign one to configure and run this operation.";

        const string k_OperationFoldoutTooltip =
            "Connector mode, session id for create-or-join, and options used when creating or joining.";

        const string k_CreateSessionOptionsTooltip =
            "Session name, max players, privacy, password, and related create settings.";

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            if (m_MultiplayerComponentsStyleSheet != null)
                root.styleSheets.Add(m_MultiplayerComponentsStyleSheet);

            var so = serializedObject;

            var sessionProp = so.FindProperty("m_MultiplayerSession");

            var warningBox = new HelpBox(k_NoSessionWarning, HelpBoxMessageType.Warning);

            root.Add(new PropertyField(sessionProp));
            root.Add(warningBox);

            var content = new VisualElement();

            var operationFoldout = new Foldout { text = "Operation", value = s_OperationFoldout };
            operationFoldout.Q<Toggle>().tooltip = k_OperationFoldoutTooltip;
            operationFoldout.RegisterValueChangedCallback(evt => s_OperationFoldout = evt.newValue);

            var operationProp = so.FindProperty("m_ConnectorType");
            operationFoldout.Add(new PropertyField(operationProp));

            var createOrJoinSessionIdProp = so.FindProperty("m_CreateOrJoinSessionId");
            var createOrJoinSessionIdField = new PropertyField(createOrJoinSessionIdProp, "Session ID");
            operationFoldout.Add(createOrJoinSessionIdField);

            var createSessionOptionsProp = so.FindProperty("m_CreateSessionOptions");
            var createSessionFields = new PropertyField(createSessionOptionsProp);
            createSessionFields.tooltip = k_CreateSessionOptionsTooltip;
            operationFoldout.Add(createSessionFields);

            var joinSessionOptionsProp = so.FindProperty("m_JoinSessionOptions");
            var joinSessionFields = new PropertyField(joinSessionOptionsProp);
            joinSessionFields.AddToClassList(k_HiddenClass);
            operationFoldout.Add(joinSessionFields);

            content.Add(operationFoldout);

            var networkOptionsField = new PropertyField(so.FindProperty("m_SessionNetworkSettings"));
            content.Add(networkOptionsField);

            var eventsField = new PropertyField(so.FindProperty("m_Events"));
            content.Add(eventsField);

            root.Add(content);

            root.Bind(so);

            UpdateVisibility();
            root.TrackPropertyValue(sessionProp, _ => UpdateVisibility());
            root.TrackPropertyValue(operationProp, _ => UpdateVisibility());
            root.TrackPropertyValue(createOrJoinSessionIdProp, _ => UpdateVisibility());

            return root;

            void UpdateVisibility()
            {
                so.Update();
                var hasSession = sessionProp.objectReferenceValue != null;
                warningBox.EnableInClassList(k_HiddenClass, hasSession);
                content.EnableInClassList(k_HiddenClass, !hasSession);
                if (hasSession)
                {
                    UpdateOperationVisibility(
                        createSessionFields,
                        createOrJoinSessionIdField,
                        networkOptionsField,
                        operationProp);
                }
            }
        }

        static void UpdateOperationVisibility(
            VisualElement createSessionFields,
            VisualElement createOrJoinSessionIdField,
            VisualElement networkOptionsField,
            SerializedProperty operationProp)
        {
            var operation = (SessionConnectorType)operationProp.enumValueIndex;
            var isCreate = operation == SessionConnectorType.Create;
            var isCreateOrJoin = operation == SessionConnectorType.CreateOrJoin;

            var showCreateAndNetwork = isCreate || isCreateOrJoin;
            createSessionFields.EnableInClassList(k_HiddenClass, !showCreateAndNetwork);
            networkOptionsField.EnableInClassList(k_HiddenClass, !showCreateAndNetwork);
            createOrJoinSessionIdField.EnableInClassList(k_HiddenClass, !isCreateOrJoin);

            CheckErrorState(createOrJoinSessionIdField,
                isCreateOrJoin,
                operationProp.serializedObject
                    .FindProperty("m_CreateOrJoinSessionId"),
                "Must define an ID!");
        }

        static void CheckErrorState(VisualElement target, bool checkEnabled,
            SerializedProperty operationProp, string errorMessage)
        {
            var isErrorState = checkEnabled
                               && string.IsNullOrWhiteSpace(operationProp.serializedObject
                                   .FindProperty("m_CreateOrJoinSessionId")
                                   .stringValue);

            target.EnableInClassList(k_ErrorStateClass, isErrorState);

            var textElement = target.Q<TextElement>();
            if (textElement == null)
            {
                return;
            }

            textElement.tooltip = isErrorState ?  errorMessage : string.Empty;
        }
    }
}
