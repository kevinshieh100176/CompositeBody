using System.Collections.Generic;

namespace UnityEngine.XR.OpenXR.Features.Android
{
    internal interface IRetryableTaskProcessor<T>
    {
        LinkedList<RetryableTaskData<T>> TasksData { get; }
        void OnTimeOut(in RetryableTaskData<T> taskData);
        bool TryNativeApiRequest(LinkedListNode<RetryableTaskData<T>> node, ref RetryableTaskData<T> taskData);
    }
}

