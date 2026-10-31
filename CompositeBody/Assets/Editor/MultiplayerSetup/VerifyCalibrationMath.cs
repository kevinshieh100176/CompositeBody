using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Checks the arithmetic in <see cref="CalibrationPoint.CalibrateAtPoint"/> and
    /// <see cref="CalibrationPoint.CalibrateFromHeadsetRest"/> against a synthetic rig, with no
    /// headset involved.
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

            // The headset-in-a-cradle path. The cases that matter are the ones where the
            // headset is not upright, because that is where reading a yaw off euler angles
            // stops working and starts quietly returning something plausible.
            RestCase("headset upright in the cradle",
                restPos: new Vector3(-1.5f, 0.11f, 0.8f),
                restEuler: new Vector3(0f, 37f, 0f),
                yawError: 123f,
                originOffset: new Vector3(2.2f, 0f, -1.7f),
                extraTilt: 0f);

            RestCase("headset face-down, pitched ninety degrees",
                restPos: new Vector3(0.4f, 0.06f, -2.3f),
                restEuler: new Vector3(90f, -64f, 0f),
                yawError: -98f,
                originOffset: new Vector3(-3.4f, 0f, 0.9f),
                extraTilt: 0f);

            RestCase("headset nose-up in a sloped cradle",
                restPos: new Vector3(1.1f, 0.14f, 1.9f),
                restEuler: new Vector3(-45f, 200f, 5f),
                yawError: 61f,
                originOffset: new Vector3(0.6f, 0f, 4.2f),
                extraTilt: 0f);

            // A cradle holds a headset to a degree or two, not exactly. That residual must stay
            // where it is rather than being rotated into the origin and tilting the horizon.
            RestCase("headset a little off in the cradle",
                restPos: new Vector3(-0.7f, 0.1f, 0.25f),
                restEuler: new Vector3(0f, 15f, 0f),
                yawError: 40f,
                originOffset: new Vector3(1f, 0f, 1f),
                extraTilt: 3f);

            // The error this whole approach exists to remove: someone presses the key while the
            // headset is still on their head.
            RestCase("refused: still being worn",
                restPos: new Vector3(-1.5f, 0.11f, 0.8f),
                restEuler: new Vector3(0f, 37f, 0f),
                yawError: 123f,
                originOffset: new Vector3(2.2f, 0f, -1.7f),
                extraTilt: 35f,
                expectRefusal: true);

            NudgeCase();

            Debug.Log($"[CalibMath] {s_Checks} checks, {s_Failures} failed.");
            Debug.Log(s_Failures == 0 ? "[CalibMath] RESULT: PASS" : "[CalibMath] RESULT: FAIL");
        }

        /// <summary>
        /// One headset-at-rest case. The synthetic truth is that the headset really is in the
        /// cradle and the ORIGIN is what is wrong, so the measured camera pose is the rest pose
        /// seen through a yaw error and an offset -- which is exactly the error a second
        /// headset's independent tracking origin produces, and all of it is correctable.
        /// </summary>
        static void RestCase(string label, Vector3 restPos, Vector3 restEuler, float yawError,
                             Vector3 originOffset, float extraTilt, bool expectRefusal = false)
        {
            Debug.Log($"[CalibMath] --- {label} ---");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var originGO = new GameObject("XR Origin");
            var floorOffsetGO = new GameObject("Camera Offset");
            floorOffsetGO.transform.SetParent(originGO.transform, false);

            var camGO = new GameObject("Main Camera");
            camGO.transform.SetParent(floorOffsetGO.transform, false);
            var cam = camGO.AddComponent<Camera>();

            var origin = originGO.AddComponent<XROrigin>();
            var originSo = new SerializedObject(origin);
            originSo.FindProperty("m_Camera").objectReferenceValue = cam;
            originSo.FindProperty("m_CameraFloorOffsetObject").objectReferenceValue = floorOffsetGO;
            originSo.ApplyModifiedPropertiesWithoutUndo();

            Quaternion restRot = Quaternion.Euler(restEuler);

            var markerGO = new GameObject("CalibrationMarker");
            markerGO.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var marker = markerGO.AddComponent<CalibrationPoint>();

            var restGO = new GameObject("HeadsetRest");
            restGO.transform.SetParent(markerGO.transform, true);
            restGO.transform.SetPositionAndRotation(restPos, restRot);

            var markerSo = new SerializedObject(marker);
            markerSo.FindProperty("m_HeadsetRest").objectReferenceValue = restGO.transform;
            markerSo.ApplyModifiedPropertiesWithoutUndo();

            // Where the headset reports itself to be: the true rest pose, through this
            // headset's own wrong origin, plus however badly it is sitting in the cradle.
            Quaternion wrongYaw = Quaternion.Euler(0f, yawError, 0f);
            Quaternion measuredRot = wrongYaw * restRot * Quaternion.AngleAxis(extraTilt, Vector3.right);
            Vector3 measuredPos = wrongYaw * restPos + originOffset;
            camGO.transform.SetPositionAndRotation(measuredPos, measuredRot);

            bool calibrated = marker.CalibrateFromHeadsetRest(out string failure);

            if (expectRefusal)
            {
                Check(!calibrated, $"refused ({failure ?? "but it did not refuse"})");
                Check(Vector3.Distance(camGO.transform.position, measuredPos) <= k_PositionTolerance &&
                      Quaternion.Angle(camGO.transform.rotation, measuredRot) <= k_AngleTolerance,
                    "a refusal moves nothing");
                return;
            }

            if (!Check(calibrated, $"calibrated ({failure ?? "ok"})")) return;

            // 1. The headset now reports itself where the cradle actually is -- height included,
            // since the cradle's height is measured and both headsets are told the same one.
            float positionError = Vector3.Distance(camGO.transform.position, restPos);
            Check(positionError <= k_PositionTolerance,
                $"headset lands on the rest pose (off by {positionError * 1000f:0.#}mm)");

            // 2. The facing is right, and whatever slop there was in the cradle is still slop
            // rather than having been corrected away or doubled.
            float residual = Quaternion.Angle(camGO.transform.rotation, restRot);
            Check(Mathf.Abs(residual - extraTilt) <= 0.1f,
                $"facing matches the cradle to its own {extraTilt:0.#}deg of slop (left {residual:0.###}deg)");

            // 3. Yaw-only, which is the whole reason this does not simply snap the camera onto
            // the rest pose: a mis-measured cradle must not leave the horizon crooked.
            Check(Mathf.Abs(NormalizeAngle(originGO.transform.eulerAngles.x)) <= k_AngleTolerance &&
                  Mathf.Abs(NormalizeAngle(originGO.transform.eulerAngles.z)) <= k_AngleTolerance,
                "origin is yaw-only");

            // 4. Calibrating again from the settled state must not walk the origin along.
            Vector3 settledPos = camGO.transform.position;
            Quaternion settledRot = camGO.transform.rotation;
            marker.CalibrateFromHeadsetRest(out _);
            float drift = Vector3.Distance(camGO.transform.position, settledPos);
            Check(drift <= k_PositionTolerance && Quaternion.Angle(camGO.transform.rotation, settledRot) <= k_AngleTolerance,
                $"re-calibrating does not drift (moved {drift * 1000f:0.#}mm)");
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

        /// <summary>
        /// The staff rescue path: nudging an origin by hand from the debug panel, and putting it
        /// back.
        ///
        /// Worth a test precisely because of when it is used. Nobody presses these buttons on a
        /// quiet afternoon; they press them with an audience in the room and a performer standing
        /// in the wrong place, and "undo" has to mean undo.
        /// </summary>
        static void NudgeCase()
        {
            Debug.Log("[CalibMath] --- hand correction ---");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var originGO = new GameObject("XR Origin");
            var floorOffsetGO = new GameObject("Camera Offset");
            floorOffsetGO.transform.SetParent(originGO.transform, false);

            var camGO = new GameObject("Main Camera");
            camGO.transform.SetParent(floorOffsetGO.transform, false);
            var cam = camGO.AddComponent<Camera>();

            var origin = originGO.AddComponent<XROrigin>();
            var originSo = new SerializedObject(origin);
            originSo.FindProperty("m_Camera").objectReferenceValue = cam;
            originSo.FindProperty("m_CameraFloorOffsetObject").objectReferenceValue = floorOffsetGO;
            originSo.ApplyModifiedPropertiesWithoutUndo();

            // A marker at an awkward yaw, so "right" in the room and "right" in world space are
            // plainly different directions and a nudge done in the wrong frame cannot pass.
            const float markerYaw = 145f;
            var markerGO = new GameObject("CalibrationMarker");
            markerGO.transform.SetPositionAndRotation(new Vector3(-1.5f, 0f, 0.8f),
                                                      Quaternion.Euler(0f, markerYaw, 0f));
            var marker = markerGO.AddComponent<CalibrationPoint>();

            var restGO = new GameObject("HeadsetRest");
            restGO.transform.SetParent(markerGO.transform, false);
            restGO.transform.localPosition = new Vector3(0f, 0.11f, 0f);

            var markerSo = new SerializedObject(marker);
            markerSo.FindProperty("m_HeadsetRest").objectReferenceValue = restGO.transform;
            markerSo.ApplyModifiedPropertiesWithoutUndo();

            var calibrator = markerGO.AddComponent<HeadsetRestCalibrator>();
            var calibratorSo = new SerializedObject(calibrator);
            calibratorSo.FindProperty("m_Marker").objectReferenceValue = marker;
            calibratorSo.ApplyModifiedPropertiesWithoutUndo();

            // Start from a calibrated origin, which is the state staff are correcting.
            camGO.transform.SetPositionAndRotation(
                Quaternion.Euler(0f, 70f, 0f) * restGO.transform.position + new Vector3(2f, 0f, -1f),
                Quaternion.Euler(0f, 70f, 0f) * restGO.transform.rotation);
            if (!Check(marker.CalibrateFromHeadsetRest(out string failure),
                       $"calibrated before correcting ({failure ?? "ok"})")) return;

            Vector3 calibratedPosition = camGO.transform.position;
            Quaternion calibratedRotation = camGO.transform.rotation;

            Check(!calibrator.hasCorrection,
                  "nothing to undo before anything has been nudged");

            // 1. Sideways is the ROOM's sideways: five centimetres along the marker's own right,
            // not along world X.
            calibrator.Nudge(new Vector3(0.05f, 0f, 0f), 0f);
            Vector3 moved = camGO.transform.position - calibratedPosition;
            Vector3 markerRight = markerGO.transform.right;
            Check(Mathf.Abs(moved.magnitude - 0.05f) <= k_PositionTolerance,
                  $"a 5cm step moves 5cm (moved {moved.magnitude * 100f:0.##}cm)");
            Check(Vector3.Angle(moved, markerRight) <= 0.1f,
                  $"and it moves along the marker's right, not world right " +
                  $"(off by {Vector3.Angle(moved, markerRight):0.##}deg)");

            Check(calibrator.hasCorrection,
                  "there is now a correction to undo");

            // 2. Forward likewise, and height is plain world up.
            Vector3 beforeForward = camGO.transform.position;
            calibrator.Nudge(new Vector3(0f, 0f, 0.05f), 0f);
            Check(Vector3.Angle(camGO.transform.position - beforeForward, markerGO.transform.forward) <= 0.1f,
                  "forward follows the marker's forward");

            Vector3 beforeUp = camGO.transform.position;
            calibrator.Nudge(new Vector3(0f, 0.05f, 0f), 0f);
            Check(Vector3.Angle(camGO.transform.position - beforeUp, Vector3.up) <= 0.1f,
                  "up is world up");

            // 3. Turning pivots on the head, so a player in the right place who is facing wrong
            // can be fixed without being swung across the room.
            Vector3 beforeTurn = camGO.transform.position;
            float yawBefore = camGO.transform.eulerAngles.y;
            calibrator.Nudge(Vector3.zero, 2f);
            Check(Vector3.Distance(camGO.transform.position, beforeTurn) <= k_PositionTolerance,
                  "turning leaves the head where it was");
            Check(Mathf.Abs(Mathf.DeltaAngle(camGO.transform.eulerAngles.y, yawBefore + 2f)) <= k_AngleTolerance,
                  "and turns by the step it was given");

            // 4. The one that matters: back to calibrated, exactly, after a mixed sequence of
            // moves and turns that do not commute.
            calibrator.UndoCorrection();
            float positionBack = Vector3.Distance(camGO.transform.position, calibratedPosition);
            float rotationBack = Quaternion.Angle(camGO.transform.rotation, calibratedRotation);
            Check(positionBack <= k_PositionTolerance,
                  $"undo puts the origin back exactly (off by {positionBack * 1000f:0.##}mm)");
            Check(rotationBack <= k_AngleTolerance,
                  $"undo puts the facing back exactly (off by {rotationBack:0.###}deg)");
            Check(!calibrator.hasCorrection,
                  "and there is nothing left to undo");

            // 5. A fresh calibration is the new zero, so undo cannot reach back past it into a
            // pose from before the headset was last in the cradle.
            calibrator.Nudge(new Vector3(0.05f, 0f, 0f), 0f);
            marker.CalibrateFromHeadsetRest(out _);
            Check(!calibrator.hasCorrection,
                  "re-calibrating clears the hand correction");
        }

        static float NormalizeAngle(float degrees)
        {
            float a = Mathf.Repeat(degrees, 360f);
            return a > 180f ? a - 360f : a;
        }

        static bool Check(bool condition, string description)
        {
            s_Checks++;
            if (condition)
            {
                Debug.Log($"[CalibMath] ok   {description}");
                return true;
            }

            s_Failures++;
            Debug.LogError($"[CalibMath] FAILED: {description}");
            return false;
        }
    }
}
