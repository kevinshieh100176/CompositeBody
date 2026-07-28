using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.OpenXR.Features.Android
{
    internal class LoadAllAnchorIdsProcessor : IRetryableTaskProcessor<Result<NativeArray<SerializableGuid>>>
    {
        const int k_PersistentDataNotReady = -1000457003; //XR_ERROR_PERSISTED_DATA_NOT_READY_ANDROID

        public LinkedList<RetryableTaskData<Result<NativeArray<SerializableGuid>>>> TasksData { get; } = new();

        void IRetryableTaskProcessor<Result<NativeArray<SerializableGuid>>>.OnTimeOut(
            in RetryableTaskData<Result<NativeArray<SerializableGuid>>> taskData)
        {
            taskData.CompletionSource.SetResult(new Result<NativeArray<SerializableGuid>>(new XRResultStatus(XRResultStatus.StatusCode.UnknownError),
                new NativeArray<SerializableGuid>()));
        }

        bool IRetryableTaskProcessor<Result<NativeArray<SerializableGuid>>>.TryNativeApiRequest(
            LinkedListNode<RetryableTaskData<Result<NativeArray<SerializableGuid>>>> node,
            ref RetryableTaskData<Result<NativeArray<SerializableGuid>>> taskData)
        {
            var resultStatus = new XRResultStatus();
            var idCount = (int)NativeApi.GetSavedIdCount(ref resultStatus);

            if (resultStatus.nativeStatusCode == k_PersistentDataNotReady)
            {
                return false;
            }

            if (resultStatus.IsError())
            {
                Debug.LogWarning($"{GetType().Name}: Failed to get saved IDs count. StatusCode: {resultStatus.statusCode}, NativeStatusCode: {resultStatus.nativeStatusCode}");
                taskData.CompletionSource.SetResult(new Result<NativeArray<SerializableGuid>>(resultStatus, new NativeArray<SerializableGuid>(0, taskData.Allocator)));
                return true;
            }

            if (idCount == 0)
            {
                taskData.CompletionSource.SetResult(new Result<NativeArray<SerializableGuid>>(resultStatus, new NativeArray<SerializableGuid>(0, taskData.Allocator)));
                return true;
            }

            unsafe
            {
                var savedIds = new NativeArray<SerializableGuid>(idCount, taskData.Allocator);
                resultStatus = new XRResultStatus();
                NativeApi.GetSavedIds(idCount, savedIds.GetUnsafePtr(), ref resultStatus);

                if (resultStatus.nativeStatusCode == k_PersistentDataNotReady)
                {
                    return false;
                }

                if (resultStatus.IsError())
                {
                    Debug.LogWarning($"{GetType().Name}: Failed to get saved IDs. StatusCode: {resultStatus.statusCode}, NativeStatusCode: {resultStatus.nativeStatusCode}");
                    taskData.CompletionSource.SetResult(new Result<NativeArray<SerializableGuid>>(resultStatus, new NativeArray<SerializableGuid>(0, taskData.Allocator)));
                    return true;
                }

                taskData.CompletionSource.SetResult(new Result<NativeArray<SerializableGuid>>(resultStatus, savedIds));
                return true;
            }
        }
    }
}

