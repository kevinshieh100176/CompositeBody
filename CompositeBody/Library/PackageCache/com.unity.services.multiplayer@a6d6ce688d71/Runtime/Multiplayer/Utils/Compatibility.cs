using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.Services.Multiplayer
{
    static class Compatibility
    {
        static readonly WaitForEndOfFrame k_WaitForEndOfFrame = new();

        internal static Task WaitForEndOfFrameAsync(MonoBehaviour behaviour)
        {
            var tcs = new TaskCompletionSource<object>();
            behaviour.StartCoroutine(WaitForEndOfFrameCoroutine(tcs));
            return tcs.Task;

            static IEnumerator WaitForEndOfFrameCoroutine(TaskCompletionSource<object> tcs)
            {
                yield return k_WaitForEndOfFrame;
                tcs.SetResult(null);
            }
        }

        internal static async Task WaitForSecondsRealtimeAsync(TimeSpan duration)
        {
            var end = Time.realtimeSinceStartup + (float)duration.TotalSeconds;
            while (Time.realtimeSinceStartup < end)
                await Task.Yield();
        }
    }
}
