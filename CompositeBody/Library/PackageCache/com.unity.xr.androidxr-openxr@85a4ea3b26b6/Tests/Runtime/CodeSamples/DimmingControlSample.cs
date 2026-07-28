using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features.Android;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests
{
    class DimmingControlSample : MonoBehaviour
    {
        #region read_and_set_dimming_level
        [Range(0f, 1f)]
        float targetDimming = 0.5f;
        void ApplyDimming()
        {
            // Assumes Result<T> exposes `status` and `value`.
            var supportedResult = ARCameraFeature.TryGetSupportedDimmingLevels(Allocator.Temp);

            if (supportedResult.status.IsError())
            {
                Debug.LogWarning("Global dimming is not supported on this runtime.");
                return;
            }

            // Dimming level values are [0,1] (least to most) returned in ascending order
            var supportedLevels = supportedResult.value;

            if (supportedLevels.Length == 0)
            {
                Debug.LogWarning("The runtime returned no supported dimming levels.");
                return;
            }

            // Request a dimming value in [0,1] (least to most).
            // The runtime clamps and rounds to the closest supported level if needed.
            var setStatus = ARCameraFeature.TryRequestGlobalDimmingLevel(targetDimming);
            if (setStatus.IsError())
            {
                Debug.LogWarning("Failed to request dimming.");
                return;
            }
        }

        void GetDimming()
        {
            var currentResult = ARCameraFeature.TryGetGlobalDimmingLevel();
            if (currentResult.status.IsError())
            {
                Debug.LogWarning("Failed to read the current dimming level.");
                return;
            }

            Debug.Log($"Requested dimming: {targetDimming}, current dimming: {currentResult.value}");
        }
        #endregion
    }
}
