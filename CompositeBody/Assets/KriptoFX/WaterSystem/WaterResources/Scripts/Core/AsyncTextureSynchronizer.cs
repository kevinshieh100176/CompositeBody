#pragma warning disable CS0168 // variable is declared but never used
#pragma warning disable CS0618 // member is obsolete (with message)
#pragma warning disable CS0649 // field is never assigned (but may be set in inspector)
#pragma warning disable CS0219 // variable is assigned but its value is never used
#pragma warning disable CS0414 // field is assigned but its value is never used

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace KWS
{
    internal enum ReadbackCode
    {
        UpToDate,
        Enqueued,
        Busy
    }

    public class AsyncTextureSynchronizer<T> where T : struct
    {
        // The pair of buffers that allows us to keep doing the async readback "permanently"
        NativeArray<T>[] m_InternalBuffers = new NativeArray<T>[2];

        // Tracker of the current "valid" buffer
        int m_CurrentBuffer = 1;


        // Is there any job on going right now?
        bool m_CurrentlyOnGoingJob = false;

        System.Action<AsyncGPUReadbackRequest> m_Callback;

        public Action DataUpdated;

        internal AsyncTextureSynchronizer()
        {
            m_Callback = OnReceive;
        }
        public void OnReceive(AsyncGPUReadbackRequest request)
        {
            if (!request.hasError)
                SwapCurrentBuffer();
            m_CurrentlyOnGoingJob = false;

            DataUpdated?.Invoke();
        }


        public NativeArray<T> CurrentBuffer()
        {
            return m_InternalBuffers[m_CurrentBuffer];
        }

        public bool IsCreated()
        {
            return m_InternalBuffers[m_CurrentBuffer].IsCreated;
        }


        public bool IsBusy()
        {
            return m_CurrentlyOnGoingJob;
        }

        void SwapCurrentBuffer()
        {
            m_CurrentBuffer = (m_CurrentBuffer + 1) % 2;
        }

        int NextBufferIndex()
        {
            return (m_CurrentBuffer + 1) % 2;
        }

        void ValidateNativeBuffer(ref NativeArray<T> buffer, int length)
        {
            if (!buffer.IsCreated || buffer.Length != length)
            {
                if (buffer.IsCreated)
                    buffer.Dispose();
                buffer = new NativeArray<T>(length, Allocator.Persistent);
            }
        }

        internal ReadbackCode EnqueueRequest(CommandBuffer cmd, ComputeBuffer buffer)
        {
            // A job is already going on, we need to wait before we do anything
            if (m_CurrentlyOnGoingJob)
                return ReadbackCode.Busy;

            // Ok we are now inside a read back job
            m_CurrentlyOnGoingJob = true;

            ValidateNativeBuffer(ref m_InternalBuffers[0], buffer.count);
            ValidateNativeBuffer(ref m_InternalBuffers[1], buffer.count);
            // Grab the next buffer
            NativeArray<T> nextBuffer = m_InternalBuffers[NextBufferIndex()];

#if UNITY_EDITOR
            // TODO: Remove this when the bug is fixed
            AtomicSafetyHandle ash = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(nextBuffer);
            AtomicSafetyHandle.CheckReadAndThrow(ash);
            AtomicSafetyHandle.CheckDeallocateAndThrow(ash);
#endif

            cmd.RequestAsyncReadbackIntoNativeArray(ref nextBuffer, buffer, m_Callback);

            // Notify that we enqueued
            return ReadbackCode.Enqueued;
        }

        internal void ReleaseATSResources()
        {
            // If a job is still ongoing, we need to wait that it is done before we free the resources
            if (m_CurrentlyOnGoingJob) AsyncGPUReadback.WaitAllRequests();
            if (m_InternalBuffers[0].IsCreated) m_InternalBuffers[0].Dispose();
            if (m_InternalBuffers[1].IsCreated) m_InternalBuffers[1].Dispose();
        }
    }
}