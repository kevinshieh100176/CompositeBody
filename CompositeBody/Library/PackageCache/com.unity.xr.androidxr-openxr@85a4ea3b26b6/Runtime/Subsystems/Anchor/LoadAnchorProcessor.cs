using System.Collections.Generic;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.OpenXR.Features.Android
{
    internal class LoadAnchorProcessor : IRetryableTaskProcessor<Result<XRAnchor>>
    {
        HashSet<int> m_BreakingErrorCodes = new ()
        {
            -10,            // XR_ERROR_LIMIT_REACHED
            -1000457000     // XR_ERROR_ANCHOR_ID_NOT_FOUND_ANDROID
        };

        const int k_PersistentDataNotReady = -1000457003; //XR_ERROR_PERSISTED_DATA_NOT_READY_ANDROID

        public LinkedList<RetryableTaskData<Result<XRAnchor>>> TasksData { get; } = new();

        void IRetryableTaskProcessor<Result<XRAnchor>>.OnTimeOut(in RetryableTaskData<Result<XRAnchor>> taskData)
        {
            taskData.CompletionSource.SetResult(new Result<XRAnchor>(new XRResultStatus(XRResultStatus.StatusCode.UnknownError), new XRAnchor()));
        }

        bool IRetryableTaskProcessor<Result<XRAnchor>>.TryNativeApiRequest(LinkedListNode<RetryableTaskData<Result<XRAnchor>>> node,
            ref RetryableTaskData<Result<XRAnchor>> taskData)
        {
            var finishTask = false;
            var persistedAnchorResult = NativeApi.IsPersistedAnchor(taskData.Guid);
            var anchor = new XRAnchor();

            if (persistedAnchorResult.IsSuccess())
            {
                var resultStatus = new XRResultStatus();
                NativeApi.TryLoadAnchor(taskData.Guid, ref anchor, ref resultStatus);

                if (resultStatus.IsSuccess())
                {
                    finishTask = true;
                }
                else if (m_BreakingErrorCodes.Contains(resultStatus.nativeStatusCode))
                {
                    Debug.LogWarning($"{GetType().Name}: Failed to load anchor {taskData.Guid}. StatusCode: {resultStatus.statusCode}, NativeStatusCode: {resultStatus.nativeStatusCode}");
                    finishTask = true;
                }

                if (finishTask)
                {
                    taskData.CompletionSource.SetResult(new Result<XRAnchor>(resultStatus, anchor));
                }
            }
            else if(persistedAnchorResult.nativeStatusCode != k_PersistentDataNotReady)
            {
                Debug.LogWarning($"{GetType().Name}: Anchor {taskData.Guid} is not persisted.");
                taskData.CompletionSource.SetResult(new Result<XRAnchor>(new XRResultStatus(false), anchor));
                finishTask = true;
            }

            return finishTask;
        }
    }
}

