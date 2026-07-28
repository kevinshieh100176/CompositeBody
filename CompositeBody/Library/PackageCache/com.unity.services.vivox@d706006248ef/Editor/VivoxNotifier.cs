using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.Services.Vivox.Editor
{
    // This class is a singular place to put any checks that should happen when loading the Editor or during Domain Reload (which is triggered by code changes, or by actions such as importing a new package).
    // The InitializeOnLoad attribute for a static class ensures that the static constructor is called during Domain Reload.
    // Individual methods of this class are not called unless they have the InitializeOnLoadMethod attribute or are called from the Constructor.
    [InitializeOnLoad]
    static class VivoxNotifier
    {
        static VivoxNotifier()
        {
            // Call any methods that should run during Domain Reload.
            CheckForExistingVivoxAssets();
        }

        // If an Assets/Vivox folder exists, warn the user to delete that folder to remove conflicts caused by importing the new package.
        static void CheckForExistingVivoxAssets()
        {
            if (AssetDatabase.IsValidFolder("Assets/Vivox"))
            {
                Debug.LogError("An existing Vivox folder is present. When you are importing by using the package manager, delete existing Vivox assets to avoid conflicts.");
            }
        }
    }
}
