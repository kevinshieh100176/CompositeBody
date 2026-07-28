using System.Collections;
using System.Collections.Generic;
using Unity.Services.Vivox.AudioTaps;
using UnityEditor;
using UnityEngine;

namespace Unity.Services.Vivox.Editor
{
    internal static class VivoxCreateObjectMenu
    {
        // Menu item strings
        internal const string CaptureSinkTapMenuItemName = "GameObject/Audio/Vivox Capture Sink Tap";
        internal const string CaptureSourceTapMenuItemName = "GameObject/Audio/Vivox Capture Source Tap";
        internal const string CaptureSourceAndSinkTapMenuItemName = "GameObject/Audio/Vivox Capture Source and Sink Tap";
        internal const string ChannelAudioMenuItemName = "GameObject/Audio/Vivox Channel Audio Tap";
        internal const string ParticipantTapMenuItemName = "GameObject/Audio/Vivox Participant Tap";

        // GameObject names
        internal const string CaptureSinkTapGameObjectName = "Vivox Capture Sink Tap";
        internal const string CaptureSourceTapGameObjectName = "Vivox Capture Source Tap";
        internal const string CaptureSourceAndSinkTapGameObjectName = "Vivox Capture Source and Sink Tap";
        internal const string ChannelAudioGameObjectName = "Vivox Channel Audio Tap";
        internal const string ParticipantTapGameObjectName = "Vivox Participant Tap";

        private static void CreateGameObjectWithComponent<T>(MenuCommand menuCommand, string gameObjectName) where T : Component
        {
            var gameObject = new GameObject(gameObjectName);
            gameObject.AddComponent<T>();

            SetupCreatedGameObject(menuCommand, gameObject);
        }

        private static void SetupCreatedGameObject(MenuCommand menuCommand, GameObject gameObject)
        {
            GameObjectUtility.SetParentAndAlign(gameObject, menuCommand.context as GameObject); // no-op if this was not a context-click
            Undo.RegisterCreatedObjectUndo(gameObject, $"Create {gameObject.name}");
            Selection.activeGameObject = gameObject;
        }

#if VIVOX_ENABLE_CAPTURE_SINK_TAP
        [MenuItem(CaptureSinkTapMenuItemName)]
        private static void CreateCaptureSinkTap(MenuCommand menuCommand)
        {
            CreateGameObjectWithComponent<VivoxCaptureSinkTap>(menuCommand, CaptureSinkTapGameObjectName);
        }
#endif

        [MenuItem(CaptureSourceTapMenuItemName)]
        private static void CreateCaptureSourceTap(MenuCommand menuCommand)
        {
            CreateGameObjectWithComponent<VivoxCaptureSourceTap>(menuCommand, CaptureSourceTapGameObjectName);
        }

#if VIVOX_ENABLE_CAPTURE_SINK_TAP
        [MenuItem(CaptureSourceAndSinkTapMenuItemName)]
        private static void CreateCaptureSourceAndSinkTap(MenuCommand menuCommand)
        {
            var gameObject = new GameObject(CaptureSourceAndSinkTapGameObjectName);
            gameObject.AddComponent<VivoxCaptureSourceTap>();
            gameObject.AddComponent<VivoxCaptureSinkTap>();

            SetupCreatedGameObject(menuCommand, gameObject);
        }
#endif

        [MenuItem(ChannelAudioMenuItemName)]
        private static void CreateChannelAudioTap(MenuCommand menuCommand)
        {
            CreateGameObjectWithComponent<VivoxChannelAudioTap>(menuCommand, ChannelAudioGameObjectName);
        }

        [MenuItem(ParticipantTapMenuItemName)]
        private static void CreateParticipantTap(MenuCommand menuCommand)
        {
            CreateGameObjectWithComponent<VivoxParticipantTap>(menuCommand, ParticipantTapGameObjectName);
        }
    }
}
