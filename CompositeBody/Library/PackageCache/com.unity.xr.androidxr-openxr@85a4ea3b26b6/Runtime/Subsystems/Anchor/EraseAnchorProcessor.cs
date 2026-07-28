using System.Collections.Generic;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.OpenXR.Features.Android
{
    internal class EraseAnchorProcessor : IRetryableTaskProcessor<XRResultStatus>
    {
        const int k_AnchorIDNotFound = -1000457000; //XR_ERROR_ANCHOR_ID_NOT_FOUND_ANDROID
        const int k_PersistentDataNotReady = -1000457003; //XR_ERROR_PERSISTED_DATA_NOT_READY_ANDROID
        public LinkedList<RetryableTaskData<XRResultStatus>> TasksData { get; } = new();

        void IRetryableTaskProcessor<XRResultStatus>.OnTimeOut(in RetryableTaskData<XRResultStatus> taskData)
        {
            taskData.CompletionSource.SetResult(new XRResultStatus(XRResultStatus.StatusCode.UnknownError));
        }

        bool IRetryableTaskProcessor<XRResultStatus>.TryNativeApiRequest(
            LinkedListNode<RetryableTaskData<XRResultStatus>> node,
            ref RetryableTaskData<XRResultStatus> taskData)
        {
            var resultStatus = new XRResultStatus();
            NativeApi.TryEraseAnchor(taskData.Guid, ref resultStatus);

            if (resultStatus.nativeStatusCode == k_PersistentDataNotReady)
            {
                return false;
            }

            if (resultStatus.IsError())
            {
                var msg = resultStatus.nativeStatusCode == k_AnchorIDNotFound
                    ? $"Anchor with id {taskData.Guid} not found."
                    : $"Failed to unpersist anchor {taskData.Guid}.";
                Debug.LogWarning($"{GetType().Name}: {msg} StatusCode: {resultStatus.statusCode}, NativeStatusCode: {resultStatus.nativeStatusCode}");
            }

            taskData.CompletionSource.SetResult(resultStatus);
            return true;
        }
    }
}

