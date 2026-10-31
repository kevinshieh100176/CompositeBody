using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Patches headset-at-rest calibration into the main scene, in place.
    ///
    /// The venue procedure this sets up: put the headset in the cradle on the taped marker,
    /// press the key on that headset's own PC, pick it up and put it on. Both machines do it
    /// separately and end up agreeing, because both have been told where the same physical
    /// object is.
    ///
    /// Re-runnable, and deliberately additive like <see cref="ConfigureCalibration"/>: it adds
    /// what is missing and leaves the rest of a scene that is already in use alone.
    ///
    /// NOTE ON THE SCENE PATH. <see cref="BuildBaseScene.ScenePath"/> still points at
    /// Assets/_Scenes/CompositeBody_Main.unity, which no longer exists -- the live scene is
    /// MainScene.unity, which is where the CalibrationMarker actually is. This targets the live
    /// one rather than repointing that constant, which has other callers.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.ConfigureHeadsetRestCalibration.Run
    /// </summary>
    public static class ConfigureHeadsetRestCalibration
    {
        public const string ScenePath = "Assets/_Scenes/MainScene.unity";

        /// <summary>
        /// Placeholder height of the headset's tracking centre above the marker, in metres.
        /// A guess at a headset standing on a low block, and no substitute for measuring the
        /// cradle that gets built -- every millimetre of error here is a millimetre both
        /// players' worlds are wrong by.
        /// </summary>
        const float k_PlaceholderRestHeight = 0.11f;

        public static void Run()
        {
            Debug.Log("[RestCalib] Starting...");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[RestCalib] RESULT: FAIL - could not open {ScenePath}");
                return;
            }

            var marker = Object.FindAnyObjectByType<CalibrationPoint>();
            if (marker == null)
            {
                Debug.LogError("[RestCalib] RESULT: FAIL - no CalibrationPoint in the scene. Run " +
                               "ConfigureCalibration first, or place the marker by hand.");
                return;
            }

            Transform rest = EnsureRestPose(marker);
            EnsureCalibrator(marker);
            SilenceOldTestKey(marker);
            SetPanelKey();
            EnsureCalibrationPanel();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RestCalib] Saved {ScenePath}");

            if (!Verify(rest)) return;

            Debug.Log("[RestCalib] RESULT: PASS");
        }

        static Transform EnsureRestPose(CalibrationPoint marker)
        {
            var so = new SerializedObject(marker);
            SerializedProperty property = so.FindProperty("m_HeadsetRest");

            if (property.objectReferenceValue is Transform existing)
            {
                Debug.Log($"[RestCalib] Rest pose already set to '{existing.name}' at " +
                          $"{existing.position}; leaving it alone.");
                return existing;
            }

            Transform found = marker.transform.Find("HeadsetRest");
            if (found == null)
            {
                var go = new GameObject("HeadsetRest");
                go.transform.SetParent(marker.transform, false);
                go.transform.localPosition = new Vector3(0f, k_PlaceholderRestHeight, 0f);
                go.transform.localRotation = Quaternion.identity;
                found = go.transform;

                Debug.LogWarning("[RestCalib] Created HeadsetRest at a PLACEHOLDER pose: " +
                                 $"{k_PlaceholderRestHeight:0.000}m above the marker, upright, facing the " +
                                 "marker's forward. Measure the real cradle and move it -- this number " +
                                 "is a guess, and both players' worlds are wrong by whatever it is out by.");
            }

            property.objectReferenceValue = found;
            so.ApplyModifiedPropertiesWithoutUndo();
            return found;
        }

        static void EnsureCalibrator(CalibrationPoint marker)
        {
            var existing = Object.FindAnyObjectByType<HeadsetRestCalibrator>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Debug.Log("[RestCalib] HeadsetRestCalibrator already present; leaving its settings alone.");
                return;
            }

            // On the marker, next to the pinch calibrator, for the same reason that one is: the
            // marker is the thing being lined up with, so the tape and everything that refers to
            // it move together.
            var calibrator = marker.gameObject.AddComponent<HeadsetRestCalibrator>();
            var so = new SerializedObject(calibrator);
            so.FindProperty("m_Marker").objectReferenceValue = marker;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Added ENABLED, unlike the pinch calibrator. A stray key press cannot recalibrate
            // a headset that is on someone's head: the tilt will not match the cradle and the
            // calibration is refused. The pinch has no such tell, which is why it ships off.
            calibrator.enabled = true;

            Debug.Log("[RestCalib] Added HeadsetRestCalibrator to the marker, enabled, key C.");
        }

        /// <summary>
        /// Takes the old stand-on-the-marker test key out of service, so one key does not mean
        /// two different calibrations. The pinch calibrator keeps its own gesture.
        /// </summary>
        static void SilenceOldTestKey(CalibrationPoint marker)
        {
            var so = new SerializedObject(marker);
            SerializedProperty key = so.FindProperty("m_TestCalibrateKey");
            if (key == null)
            {
                Debug.LogWarning("[RestCalib] CalibrationPoint has no m_TestCalibrateKey field to silence.");
                return;
            }

            // intValue, not enumValueIndex: the latter is a position in the enum's value list,
            // which only happens to equal the value for Key.None.
            if (key.intValue == (int)Key.None)
            {
                Debug.Log("[RestCalib] CalibrationPoint's own test key is already off.");
                return;
            }

            key.intValue = (int)Key.None;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[RestCalib] CalibrationPoint's stand-on-the-marker test key switched off; " +
                      "C now belongs to the headset-at-rest calibrator.");
        }

        /// <summary>
        /// Puts the debug panel on F3, which is how staff reach these controls at all.
        ///
        /// Done here, in the scene, because the serialized value is what runs: changing the
        /// field's default in <see cref="InteractionDebugPanel"/> does nothing to a panel that
        /// is already in a scene, and this scene has one. Part of this setup rather than a
        /// separate script because a calibration nobody can open the panel for is not set up.
        /// </summary>
        static void SetPanelKey()
        {
            var panel = Object.FindAnyObjectByType<InteractionDebugPanel>(FindObjectsInactive.Include);
            if (panel == null)
            {
                Debug.LogWarning("[RestCalib] No InteractionDebugPanel in the scene, so there is " +
                                 "nothing to put on F3 and no way to reach the correction buttons.");
                return;
            }

            var so = new SerializedObject(panel);
            SerializedProperty key = so.FindProperty("m_ToggleKey");
            if (key == null)
            {
                Debug.LogWarning("[RestCalib] InteractionDebugPanel has no m_ToggleKey field.");
                return;
            }

            if (key.intValue == (int)Key.F3)
            {
                Debug.Log("[RestCalib] Debug panel is already on F3.");
                return;
            }

            var was = (Key)key.intValue;
            key.intValue = (int)Key.F3;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[RestCalib] Debug panel toggle {was} -> F3.");
        }

        /// <summary>
        /// The calibration panel, on its own object. Staff reach for this one under pressure, so
        /// it gets its own key rather than a row inside the interaction list.
        /// </summary>
        static void EnsureCalibrationPanel()
        {
            var existing = Object.FindAnyObjectByType<CalibrationPanel>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Debug.Log($"[RestCalib] CalibrationPanel already present on '{existing.name}' " +
                          $"({existing.toggleKey}); leaving its settings alone.");
                return;
            }

            var go = new GameObject("CalibrationPanel");
            var panel = go.AddComponent<CalibrationPanel>();
            Debug.Log($"[RestCalib] Added CalibrationPanel on its own object, key {panel.toggleKey}.");
        }

        static bool Verify(Transform rest)
        {
            var marker = Object.FindAnyObjectByType<CalibrationPoint>();
            var calibrator = Object.FindAnyObjectByType<HeadsetRestCalibrator>(FindObjectsInactive.Include);

            if (marker == null)
            {
                Debug.LogError("[RestCalib] RESULT: FAIL - no CalibrationPoint in the saved scene.");
                return false;
            }

            if (marker.headsetRest == marker.transform)
            {
                Debug.LogError("[RestCalib] RESULT: FAIL - the marker still has no rest pose, so a " +
                               "calibration would line the headset up with the floor tape itself.");
                return false;
            }
            Debug.Log($"[RestCalib] OK   rest pose '{marker.headsetRest.name}' at {marker.headsetRest.position}");

            if (calibrator == null)
            {
                Debug.LogError("[RestCalib] RESULT: FAIL - no HeadsetRestCalibrator in the saved scene.");
                return false;
            }

            var so = new SerializedObject(calibrator);
            if (so.FindProperty("m_Marker").objectReferenceValue == null)
            {
                Debug.LogError("[RestCalib] RESULT: FAIL - HeadsetRestCalibrator has no marker assigned.");
                return false;
            }
            Debug.Log($"[RestCalib] OK   HeadsetRestCalibrator wired to the marker " +
                      $"({(calibrator.enabled ? "enabled" : "SWITCHED OFF -- the key will do nothing")})");

            var panel = Object.FindAnyObjectByType<InteractionDebugPanel>(FindObjectsInactive.Include);
            if (panel == null)
            {
                Debug.LogError("[RestCalib] RESULT: FAIL - no InteractionDebugPanel, so the " +
                               "correction buttons cannot be reached.");
                return false;
            }

            var panelSo = new SerializedObject(panel);
            if (panelSo.FindProperty("m_ToggleKey").intValue != (int)Key.F3)
            {
                Debug.LogError("[RestCalib] RESULT: FAIL - the debug panel is not on F3.");
                return false;
            }
            Debug.Log("[RestCalib] OK   debug panel opens on F3");

            var calibrationPanel = Object.FindAnyObjectByType<CalibrationPanel>(FindObjectsInactive.Include);
            if (calibrationPanel == null)
            {
                Debug.LogError("[RestCalib] RESULT: FAIL - no CalibrationPanel, so staff have no " +
                               "dedicated way to calibrate or correct this headset.");
                return false;
            }
            Debug.Log($"[RestCalib] OK   calibration panel opens on {calibrationPanel.toggleKey}");

            if (rest != null && Vector3.Distance(rest.localPosition, new Vector3(0f, k_PlaceholderRestHeight, 0f)) < 0.0001f)
            {
                // Reported, not failed: a scene that has not been measured yet is a normal state
                // to save, and the number is only wrong once someone takes it into a venue.
                Debug.LogWarning("[RestCalib] NOTE the rest pose is still the placeholder. Measure the " +
                                 "cradle before the next venue day.");
            }

            return true;
        }
    }
}
