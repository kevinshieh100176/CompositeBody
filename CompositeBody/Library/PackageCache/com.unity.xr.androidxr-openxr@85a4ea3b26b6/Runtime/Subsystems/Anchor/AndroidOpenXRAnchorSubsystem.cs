using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using UnityEngine.Android;
using UnityEngine.Scripting;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.OpenXR.Features.Android
{
    internal struct RetryableTaskData<T>
    {
        public long StartTime;
        public long TimeoutThreshold;
        public SerializableGuid Guid;
        public AwaitableCompletionSource<T> CompletionSource;
        public TrackableId TrackableId;
        public Allocator Allocator;
    }

    /// <summary>
    /// The Android-OpenXR implementation of the <see cref="XRAnchorSubsystem"/>.
    /// </summary>
    [Preserve]
    public sealed class AndroidOpenXRAnchorSubsystem : XRAnchorSubsystem
    {
        internal const string k_SubsystemId = "Android-Anchor";

        class AndroidOpenXRAnchorProvider : Provider
        {
            Allocator m_AllocatorForIds;

            IRetryableTaskProcessor<Result<SerializableGuid>> m_AnchorSavingProcessor = new SaveAnchorProcessor();
            IRetryableTaskProcessor<Result<XRAnchor>> m_AnchorLoadingProcessor = new LoadAnchorProcessor();
            IRetryableTaskProcessor<Result<NativeArray<SerializableGuid>>> m_LoadAllAnchorIdsProcessor = new LoadAllAnchorIdsProcessor();
            IRetryableTaskProcessor<XRResultStatus> m_EraseAnchorProcessor = new EraseAnchorProcessor();

            // This is currently being used as a time-out so if our async-under-the-covers
            // functions never complete, we stop trying after ten seconds
            const long k_TenSeconds = 10000000000;

            protected override bool TryInitialize()
            {
                if (OpenXRRuntime.IsExtensionEnabled(Constants.OpenXRExtensions.k_XR_ANDROID_trackables))
                {
                    NativeApi.Create();
                    return true;
                }

                return false;
            }

            public override void Start()
            {
#if UNITY_ANDROID
                if (!Permission.HasUserAuthorizedPermission(Constants.Permissions.k_SceneUnderstandingFinePermission)
                    && !Permission.HasUserAuthorizedPermission(Constants.Permissions.k_SceneUnderstandingCoarsePermission))
                {
                    Debug.LogWarning($"Placing anchors requires system permission {Constants.Permissions.k_SceneUnderstandingCoarsePermission}, but permission was not granted.");
                }
#endif

#if !UNITY_STANDALONE_WIN
                // AnchorProvider.Start() initializes the persistence handler, which is not supported by direct preview at the current time
                NativeApi.Start();
#endif // !UNITY_STANDALONE_WIN
            }

            public override void Stop()
            {
            }

            public override void Destroy() => NativeApi.Destroy();

            static void RetryPostponedTask<T>(IRetryableTaskProcessor<T> processor)
            {
                var node = processor.TasksData.First;

                while (node != null)
                {
                    var nextNode = node.Next;
                    var taskData = node.Value;
                    var elapsed = NativeApi.GetCurrentTime() - taskData.StartTime;
                    bool removeNode;

                    if (elapsed > taskData.TimeoutThreshold)
                    {
                        Debug.LogWarning($"{processor.GetType().Name}: Timeout after {elapsed / 1000000f:F2}ms");
                        processor.OnTimeOut(taskData);
                        removeNode = true;
                    }
                    else
                    {
                        try
                        {
                            removeNode = processor.TryNativeApiRequest(node, ref taskData);
                            node.Value = taskData; // re-write the copy back to the node if it happened to be modified inside a processor
                        }
                        catch (InvalidOperationException e)
                        {
                            Debug.LogWarning($"{processor.GetType().Name}: CompletionSource {taskData.CompletionSource} already completed\n{e}");
                            removeNode = true;
                        }
                        catch (NullReferenceException e)
                        {
                            Debug.LogWarning($"{processor.GetType().Name}: CompletionSource {taskData.CompletionSource} is reset, skipping\n{e}");
                            removeNode = true;
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"{processor.GetType().Name}: Unexpected error {e}");
                            removeNode = true;
                        }
                    }

                    if (removeNode && node.List == processor.TasksData)
                    {
                        processor.TasksData.Remove(node);
                    }

                    node = nextNode;
                }
            }

            public override unsafe TrackableChanges<XRAnchor> GetChanges(XRAnchor defaultAnchor, Allocator allocator)
            {
                RetryPostponedTask(m_AnchorSavingProcessor);
                RetryPostponedTask(m_AnchorLoadingProcessor);
                RetryPostponedTask(m_LoadAllAnchorIdsProcessor);
                RetryPostponedTask(m_EraseAnchorProcessor);

                NativeApi.GetChanges(
                    out var addedPtr, out var addedCount,
                    out var updatedPtr, out var updatedCount,
                    out var removedPtr, out var removedCount,
                    out var elementSize);
                try
                {
                    return new TrackableChanges<XRAnchor>(
                        addedPtr, addedCount,
                        updatedPtr, updatedCount,
                        removedPtr, removedCount,
                        defaultAnchor, elementSize,
                        allocator);
                }
                finally
                {
                    NativeApi.ClearChanges();
                }
            }

            public override bool TryAddAnchor(
                Pose pose,
                out XRAnchor anchor)
            {
                return NativeApi.TryAdd(in pose, out anchor);
            }

            public override bool TryAttachAnchor(
                TrackableId attachedToId,
                Pose pose,
                out XRAnchor anchor)
            {
                return NativeApi.TryAttach(attachedToId, in pose, out anchor);
            }

            public override bool TryRemoveAnchor(TrackableId anchorId)
            {
                return NativeApi.TryRemove(anchorId);
            }

            public override Awaitable<Result<SerializableGuid>> TrySaveAnchorAsync(
                TrackableId anchorId, CancellationToken cancellationToken = default)
            {
                AwaitableCompletionSource<Result<SerializableGuid>> saveAnchorCompletionSource = new();

                m_AnchorSavingProcessor.TasksData.AddLast(new RetryableTaskData<Result<SerializableGuid>>
                {
                    CompletionSource = saveAnchorCompletionSource,
                    StartTime = NativeApi.GetCurrentTime(),
                    TimeoutThreshold = k_TenSeconds,
                    Guid = new SerializableGuid(),
                    TrackableId = anchorId
                });

                RetryPostponedTask(m_AnchorSavingProcessor);

                return saveAnchorCompletionSource.Awaitable;
            }

            public override Awaitable<Result<XRAnchor>> TryLoadAnchorAsync(
                SerializableGuid savedAnchorGuid, CancellationToken cancellationToken = default)
            {
                AwaitableCompletionSource<Result<XRAnchor>> loadAnchorCompletionSource = new();

                m_AnchorLoadingProcessor.TasksData.AddLast(new RetryableTaskData<Result<XRAnchor>>
                {
                    CompletionSource = loadAnchorCompletionSource,
                    StartTime = NativeApi.GetCurrentTime(),
                    TimeoutThreshold = k_TenSeconds,
                    Guid = savedAnchorGuid
                });

                RetryPostponedTask(m_AnchorLoadingProcessor);

                return loadAnchorCompletionSource.Awaitable;
            }

            public override Awaitable<Result<NativeArray<SerializableGuid>>> TryGetSavedAnchorIdsAsync(
                Allocator allocator, CancellationToken cancellationToken = default)
            {
                AwaitableCompletionSource<Result<NativeArray<SerializableGuid>>> completionSource = new();

                m_LoadAllAnchorIdsProcessor.TasksData.AddLast(new RetryableTaskData<Result<NativeArray<SerializableGuid>>>
                {
                    CompletionSource = completionSource,
                    StartTime = NativeApi.GetCurrentTime(),
                    TimeoutThreshold = k_TenSeconds,
                    Allocator = allocator
                });

                RetryPostponedTask(m_LoadAllAnchorIdsProcessor);

                return completionSource.Awaitable;
            }

            public override Awaitable<XRResultStatus> TryEraseAnchorAsync(
                SerializableGuid anchorId, CancellationToken cancellationToken = default)
            {
                AwaitableCompletionSource<XRResultStatus> eraseAnchorCompletionSource = new();

                m_EraseAnchorProcessor.TasksData.AddLast(new RetryableTaskData<XRResultStatus>
                {
                    Guid = anchorId,
                    StartTime = NativeApi.GetCurrentTime(),
                    TimeoutThreshold = k_TenSeconds,
                    CompletionSource = eraseAnchorCompletionSource
                });

                return eraseAnchorCompletionSource.Awaitable;
            }

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            static void RegisterDescriptor()
            {
                XRAnchorSubsystemDescriptor.Register(new XRAnchorSubsystemDescriptor.Cinfo
                {
                    id = k_SubsystemId,
                    providerType = typeof(AndroidOpenXRAnchorProvider),
                    subsystemTypeOverride = typeof(AndroidOpenXRAnchorSubsystem),
                    supportsTrackableAttachments = true,
                    supportsSynchronousAdd = true,
                    supportsSaveAnchorDelegate = () => OpenXRRuntime.IsExtensionEnabled(Constants.OpenXRExtensions.k_XR_ANDROID_device_anchor_persistence),
                    supportsLoadAnchorDelegate = () => OpenXRRuntime.IsExtensionEnabled(Constants.OpenXRExtensions.k_XR_ANDROID_device_anchor_persistence),
                    supportsEraseAnchorDelegate = () => OpenXRRuntime.IsExtensionEnabled(Constants.OpenXRExtensions.k_XR_ANDROID_device_anchor_persistence),
                    supportsGetSavedAnchorIdsDelegate = () => OpenXRRuntime.IsExtensionEnabled(Constants.OpenXRExtensions.k_XR_ANDROID_device_anchor_persistence),
                    supportsAsyncCancellation = false,
                });
            }
        }
    }

    static class NativeApi
    {
        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_Create")]
        public static extern void Create();

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_Start")]
        public static extern void Start();

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_Destroy")]
        public static extern void Destroy();

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_GetChanges")]
        public static extern unsafe void GetChanges(
            out void* addedPtr, out int addedCount,
            out void* updatedPtr, out int updatedCount,
            out void* removedPtr, out int removedCount,
            out int elementSize);

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_TryAdd")]
        public static extern bool TryAdd(
            in Pose pose,
            out XRAnchor anchor);

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_TryAttach")]
        public static extern bool TryAttach(
            TrackableId trackableToAffix,
            in Pose pose,
            out XRAnchor anchor);

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_TryRemove")]
        public static extern bool TryRemove(TrackableId anchorId);

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_ClearChanges")]
        public static extern void ClearChanges();

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_TrySaveAnchor")]
        public static extern void TrySaveAnchor(TrackableId anchorId, ref SerializableGuid anchorGuid, ref XRResultStatus synchronousResultStatus);

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_TryLoadAnchor")]
        public static extern void TryLoadAnchor(SerializableGuid anchorId, ref XRAnchor anchor, ref XRResultStatus synchronousResultStatus);

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_FrameManager_GetCurrentTime")]
        public static extern long GetCurrentTime();

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_GetSavedIdCount")]
        public static extern uint GetSavedIdCount(ref XRResultStatus synchronousResultStatus);

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_GetSavedIds")]
        public static extern unsafe void GetSavedIds(int idCount, void* savedIds, ref XRResultStatus synchronousResultStatus);

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_TryEraseAnchor")]
        public static extern bool TryEraseAnchor(TrackableId anchorId, ref XRResultStatus synchronousResultStatus);

        [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Anchor_IsPersistedAnchor")]
        public static extern XRResultStatus IsPersistedAnchor(TrackableId anchorId);
    }
}

