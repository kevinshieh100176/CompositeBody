using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Patches pinch calibration into the existing main scene, in place.
    ///
    /// Deliberately not part of <see cref="BuildBaseScene"/>: that rebuilds the scene from
    /// nothing, and this scene is already in use and has been tested. This only adds what is
    /// missing and leaves everything else alone, so it is safe to re-run.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.ConfigureCalibration.Run
    /// </summary>
    public static class ConfigureCalibration
    {
        public static void Run()
        {
            Debug.Log("[Calib] Starting...");

            var scene = EditorSceneManager.OpenScene(BuildBaseScene.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[Calib] RESULT: FAIL - could not open {BuildBaseScene.ScenePath}");
                return;
            }

            CalibrationPoint marker = EnsureMarker();
            if (marker == null)
            {
                Debug.LogError("[Calib] RESULT: FAIL - no calibration marker and could not create one.");
                return;
            }

            EnsurePinchCalibrator(marker);
            DisableVenueSpawnOffsets();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Calib] Saved {BuildBaseScene.ScenePath}");

            if (!Verify()) return;

            Debug.Log("[Calib] RESULT: PASS");
        }

        static CalibrationPoint EnsureMarker()
        {
            var existing = Object.FindFirstObjectByType<CalibrationPoint>();
            if (existing != null)
            {
                Debug.Log($"[Calib] Found existing marker '{existing.name}' at {existing.transform.position} " +
                          $"facing {existing.transform.eulerAngles.y:0.#}deg.");
                return existing;
            }

            var go = new GameObject("CalibrationMarker");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var marker = go.AddComponent<CalibrationPoint>();
            Debug.Log("[Calib] Created CalibrationMarker at the origin -- move it to match the taped marker " +
                      "in the room, and point its forward the way the player will face.");
            return marker;
        }

        static void EnsurePinchCalibrator(CalibrationPoint marker)
        {
            var existing = Object.FindFirstObjectByType<HandPinchCalibrator>();
            if (existing != null)
            {
                Debug.Log("[Calib] HandPinchCalibrator already present; leaving its settings alone.");
                return;
            }

            // On the marker itself rather than on the rig: the marker is the thing being lined
            // up with, and keeping them together means one object to move when the tape moves.
            var calibrator = marker.gameObject.AddComponent<HandPinchCalibrator>();
            var so = new SerializedObject(calibrator);
            so.FindProperty("m_Marker").objectReferenceValue = marker;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Added wired but switched off. The piece is hand-tracked throughout and a pinch is
            // already the grab gesture, so the headset's own floor and room setup stand in for
            // this until a shared origin is actually needed.
            calibrator.enabled = false;

            Debug.Log("[Calib] Added HandPinchCalibrator to the marker (right hand, held pinch), " +
                      "switched off -- enable the component to put the gesture back.");
        }

        /// <summary>
        /// Turns the per-role spawn offsets into desktop-only. With a headset attached they move
        /// each rig by a different amount away from an origin physical calibration had already
        /// made correct, which is what put the two players in the wrong places relative to the
        /// real room and to each other.
        /// </summary>
        static void DisableVenueSpawnOffsets()
        {
            var positioner = Object.FindFirstObjectByType<RoleSpawnPositioner>();
            if (positioner == null)
            {
                Debug.Log("[Calib] No RoleSpawnPositioner in the scene; nothing to gate.");
                return;
            }

            var so = new SerializedObject(positioner);
            var desktopOnly = so.FindProperty("m_DesktopOnly");
            if (desktopOnly == null)
            {
                Debug.LogError("[Calib] RoleSpawnPositioner has no m_DesktopOnly field.");
                return;
            }

            bool was = desktopOnly.boolValue;
            desktopOnly.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[Calib] RoleSpawnPositioner m_DesktopOnly {was} -> true " +
                      "(offsets now apply only without a headset).");
        }

        static bool Verify()
        {
            var marker = Object.FindFirstObjectByType<CalibrationPoint>();
            var calibrator = Object.FindFirstObjectByType<HandPinchCalibrator>();
            var positioner = Object.FindFirstObjectByType<RoleSpawnPositioner>();

            if (marker == null)
            {
                Debug.LogError("[Calib] RESULT: FAIL - no CalibrationPoint in the saved scene.");
                return false;
            }
            Debug.Log("[Calib] OK   CalibrationPoint present");

            if (calibrator == null)
            {
                Debug.LogError("[Calib] RESULT: FAIL - no HandPinchCalibrator in the saved scene.");
                return false;
            }

            var calibSo = new SerializedObject(calibrator);
            if (calibSo.FindProperty("m_Marker").objectReferenceValue == null)
            {
                Debug.LogError("[Calib] RESULT: FAIL - HandPinchCalibrator has no marker assigned.");
                return false;
            }
            // Reported rather than asserted either way. Whether the gesture should be live is a
            // decision about the piece, not something a scene can be wrong about -- but a log
            // that said only "wired" would read as "armed".
            Debug.Log($"[Calib] OK   HandPinchCalibrator wired to the marker " +
                      $"({(calibrator.enabled ? "ENABLED -- a held pinch will recalibrate" : "switched off")})");

            if (positioner != null)
            {
                var so = new SerializedObject(positioner);
                if (!so.FindProperty("m_DesktopOnly").boolValue)
                {
                    Debug.LogError("[Calib] RESULT: FAIL - RoleSpawnPositioner would still offset the rig in the venue.");
                    return false;
                }
                Debug.Log("[Calib] OK   RoleSpawnPositioner is desktop-only");
            }

            return true;
        }
    }
}
