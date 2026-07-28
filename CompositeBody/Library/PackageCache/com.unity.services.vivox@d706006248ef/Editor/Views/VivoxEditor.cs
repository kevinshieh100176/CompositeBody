using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Collections.Generic;

using System;

namespace Unity.Services.Vivox.Editor
{
    internal class VivoxEditor : EditorWindow
    {
#if UNITY_2020_2_OR_NEWER
        [MenuItem("Services/Vivox/Configure", priority = 111)]
#endif
        static void ShowProjectSettings()
        {
            SettingsService.OpenProjectSettings("Project/Services/Vivox");
            EditorGameServiceAnalyticsSender.SendTopMenuConfigureEvent();
        }
    }
}
