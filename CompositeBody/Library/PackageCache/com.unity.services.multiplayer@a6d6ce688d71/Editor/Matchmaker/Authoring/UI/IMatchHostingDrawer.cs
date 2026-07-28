using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.UI
{
    /// <summary>
    /// Custom PropertyDrawer for IMatchHosting type.
    /// </summary>
    /// <remarks>
    /// We are using a custom property drawer so that we can track changes
    /// to the underlying IMatchHosting type and swap to the proper instance when needed.
    /// </remarks>
    [CustomPropertyDrawer(typeof(IMatchHosting))]
    class IMatchHostingDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new PropertyField(property);
            container.Bind(property.serializedObject);

            // Tacking changes on the whole serialized object
            // because tracking the property itself causes issues when it is changed to another underlying type.
            container.TrackSerializedObjectValue(property.serializedObject, UpdateUnderlyingType);

            void UpdateUnderlyingType(SerializedObject serializedObject)
            {
                // Managing the undo group.
                // UpdateUnderlyingType is called after the property change was pushed to the undo stack.
                // So we're assuming we are in the next group now, and will collapse the current changes
                // with the type change one.
                var undo = Undo.GetCurrentGroup() - 1;

                var referenceValue = (IMatchHosting)property.managedReferenceValue;
                var type = (IMatchHosting.MatchHostingType)property.FindPropertyRelative(referenceValue.GetTypeFieldName).intValue;
                switch (type)
                {
                    case IMatchHosting.MatchHostingType.MatchId:
                        if (property.managedReferenceValue is ClientHosting)
                        {
                            return;
                        }
                        property.managedReferenceValue = new ClientHosting()
                        {
                            Type = IMatchHosting.MatchHostingType.MatchId
                        };
                        break;
                    case IMatchHosting.MatchHostingType.Multiplay:
                        if (property.managedReferenceValue is MultiplayHosting)
                        {
                            return;
                        }
                        property.managedReferenceValue = new MultiplayHosting()
                        {
                            Type = IMatchHosting.MatchHostingType.Multiplay
                        };
                        break;
                    case IMatchHosting.MatchHostingType.CloudCode:
                        if (property.managedReferenceValue is CloudCodeHosting)
                        {
                            return;
                        }
                        property.managedReferenceValue = new CloudCodeHosting()
                        {
                            Type = IMatchHosting.MatchHostingType.CloudCode
                        };
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                // Note the early returns in the switch case, if we're not changing the type,
                // there is no need to apply the modified properties and collapse the undo group.
                property.serializedObject.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undo);
            }

            return container;
        }
    }
}
