using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Checks the arithmetic in <see cref="CalibrationPoint.CalibrateAtPoint"/> against a
    /// synthetic rig, with no headset involved.
    ///
    /// This is the one part of pinch calibration that can be verified without hardware, and it
    /// is also the part most likely to be quietly wrong: the sign of the yaw delta, whether the
    /// rotation happens about the right pivot, and whether head pitch and roll leak into the
    /// origin. Every one of those produces a world that is merely offset rather than broken,
    /// which is indistinguishable from bad tracking from inside a headset.
    ///
    /// The reference point is parented under the rig before calibrating, because that is what
    /// the player's hand actually is -- a point rigidly attached to the thing being moved.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.VerifyCalibrationMath.Run
    /// </summary>
    public static class VerifyCalibrationMath
    {
        const float k_PositionTolerance = 0.002f;   // 2mm
        const float k_AngleTolerance = 0.05f;       // degrees

        static int s_Checks;
        static int s_Failures;

        public static void Run()
        {
            Debug.Log("[CalibMath] Starting...");
            s_Checks = 0;
            s_Failures = 0;

            // Deliberately awkward numbers: a case where the marker is at the origin facing
            // forward would pass even with the yaw sign inverted.
            Case("offset and rotated",
                camPos: new Vector3(2.4f, 1.62f, -3.1f),
                camEuler: new Vector3(11f, 47f, -6f),
                markerPos: new Vector3(-1.5f, 0f, 0.8f),
                markerYaw: 145f,
                handLocalOffset: new Vector3(0.18f, -0.25f, 0.42f));

            Case("marker behind the player",
                camPos: new Vector3(-5f, 1.70f, 6.2f),
                camEuler: new Vector3(-8f, -160f, 3f),
                markerPos: new Vector3(0.35f, 0f, -0.9f),
                markerYaw: -70f,
                handLocalOffset: new Vector3(-0.1f, -0.3f, 0.38f));

            Case("already aligned",
                camPos: new Vector3(0f, 1.65f, 0f),
                camEuler: new Vector3(0f, 0f, 0f),
                markerPos: Vector3.zero,
                markerYaw: 0f,
                handLocalOffset: new Vector3(0f, -0.2f, 0.4f));

            Debug.Log($"[CalibMath] {s_Checks} checks, {s_Failures} failed.");
            Debug.Log(s_Failures == 0 ? "[CalibMath] RESULT: PASS" : "[CalibMath] RESULT: FAIL");
        }

        static void Case(string label, Vector3 camPos, Vector3 camEuler, Vector3 markerPos, float markerYaw,
                         Vector3 handLocalOffset)
        {
            Debug.Log($"[CalibMath] --- {label} ---");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Rig: origin -> floor offset -> camera, the shape XROrigin expects.
            var originGO = new GameObject("XR Origin");
            var floorOffsetGO = new GameObject("Camera Offset");
            floorOffsetGO.transform.SetParent(originGO.transform, false);

            var camGO = new GameObject("Main Camera");
            camGO.transform.SetParent(floorOffsetGO.transform, false);
            var cam = camGO.AddComponent<Camera>();
            camGO.transform.SetPositionAndRotation(camPos, Quaternion.Euler(camEuler));

            var origin = originGO.AddComponent<XROrigin>();
            var originSo = new SerializedObject(origin);
            originSo.FindProperty("m_Camera").objectReferenceValue = cam;
            originSo.FindProperty("m_CameraFloorOffsetObject").objectReferenceValue = floorOffsetGO;
            originSo.ApplyModifiedPropertiesWithoutUndo();

            // The pinch: a point fixed relative to the rig, roughly where a hand would be.
            var handGO = new GameObject("PinchPoint");
            handGO.transform.SetParent(camGO.transform, false);
            handGO.transform.localPosition = handLocalOffset;
            Vector3 referenceBefore = handGO.transform.position;

            var markerGO = new GameObject("CalibrationMarker");
            markerGO.transform.SetPositionAndRotation(markerPos, Quaternion.Euler(0f, markerYaw, 0f));
            var marker = markerGO.AddComponent<CalibrationPoint>();

            float camPitchBefore = NormalizeAngle(camGO.transform.eulerAngles.x);
            float camRollBefore = NormalizeAngle(camGO.transform.eulerAngles.z);
            float camHeightBefore = camGO.transform.position.y;

            marker.CalibrateAtPoint(referenceBefore);

            // 1. The point the player was touching now sits on the marker, in plan.
            Vector3 referenceAfter = handGO.transform.position;
            float planError = Vector2.Distance(
                new Vector2(referenceAfter.x, referenceAfter.z),
                new Vector2(markerPos.x, markerPos.z));
            Check(planError <= k_PositionTolerance,
                $"pinch point lands on the marker (off by {planError * 1000f:0.#}mm)");

            // 2. The player now faces the way the marker faces.
            float yawError = Mathf.Abs(Mathf.DeltaAngle(camGO.transform.eulerAngles.y, markerYaw));
            Check(yawError <= k_AngleTolerance,
                $"camera yaw matches the marker (off by {yawError:0.###}deg)");

            // 3. Head tilt must not have been baked into the origin: a rig rotated in pitch or
            // roll would leave the horizon permanently crooked for that player.
            float pitchDrift = Mathf.Abs(Mathf.DeltaAngle(NormalizeAngle(camGO.transform.eulerAngles.x), camPitchBefore));
            float rollDrift = Mathf.Abs(Mathf.DeltaAngle(NormalizeAngle(camGO.transform.eulerAngles.z), camRollBefore));
            Check(pitchDrift <= k_AngleTolerance, $"head pitch unchanged (drifted {pitchDrift:0.###}deg)");
            Check(rollDrift <= k_AngleTolerance, $"head roll unchanged (drifted {rollDrift:0.###}deg)");
            Check(Mathf.Abs(originGO.transform.eulerAngles.x) <= k_AngleTolerance &&
                  Mathf.Abs(originGO.transform.eulerAngles.z) <= k_AngleTolerance,
                "origin is yaw-only");

            // 4. Height left to the headset's own floor estimate.
            float heightDrift = Mathf.Abs(camGO.transform.position.y - camHeightBefore);
            Check(heightDrift <= k_PositionTolerance, $"camera height unchanged (drifted {heightDrift * 1000f:0.#}mm)");

            // 5. Calibrating twice from the already-correct state must be a no-op, or repeated
            // staff re-calibration would walk the origin away a little each time.
            Vector3 settled = handGO.transform.position;
            marker.CalibrateAtPoint(settled);
            float idempotenceError = Vector3.Distance(handGO.transform.position, settled);
            Check(idempotenceError <= k_PositionTolerance,
                $"re-calibrating does not drift (moved {idempotenceError * 1000f:0.#}mm)");
        }

        static float NormalizeAngle(float degrees)
        {
            float a = Mathf.Repeat(degrees, 360f);
            return a > 180f ? a - 360f : a;
        }

        static void Check(bool condition, string description)
        {
            s_Checks++;
            if (condition)
            {
                Debug.Log($"[CalibMath] ok   {description}");
                return;
            }

            s_Failures++;
            Debug.LogError($"[CalibMath] FAILED: {description}");
        }
    }
}
