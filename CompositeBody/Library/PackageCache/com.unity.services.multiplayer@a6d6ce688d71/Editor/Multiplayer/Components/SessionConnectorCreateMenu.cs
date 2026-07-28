using System.IO;
using Unity.Services.Multiplayer.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.Services.Multiplayer.Editor.Components
{
    static class SessionConnectorCreateMenu
    {
        const string k_MenuRoot = "Assets/Create/Services/Multiplayer/Session Connector/";

        static string GetTargetFolder()
        {
            if (Selection.activeObject != null)
            {
                var path = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (!string.IsNullOrEmpty(path))
                    return AssetDatabase.IsValidFolder(path) ? path : Path.GetDirectoryName(path);
            }
            return "Assets";
        }

        internal static void CreateSessionConnectorAsset(SessionConnectorType connectorType, string defaultFileName)
        {
            var asset = ScriptableObject.CreateInstance<SessionConnector>();
            var so = new SerializedObject(asset);
            so.FindProperty("m_ConnectorType").enumValueIndex = (int)connectorType;
            so.ApplyModifiedPropertiesWithoutUndo();

            var folder = GetTargetFolder();
            var path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, defaultFileName + ".asset"));
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        [MenuItem(k_MenuRoot + "Create Session", false, 0)]
        static void CreateSession() =>
            CreateSessionConnectorAsset(SessionConnectorType.Create, "Create Session");

        [MenuItem(k_MenuRoot + "Create or Join Session", false, 2)]
        static void CreateOrJoinSession() =>
            CreateSessionConnectorAsset(SessionConnectorType.CreateOrJoin, "Create or Join Session");
    }
}
