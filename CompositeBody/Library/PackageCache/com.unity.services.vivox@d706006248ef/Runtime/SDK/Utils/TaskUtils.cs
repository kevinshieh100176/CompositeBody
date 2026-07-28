// This would now be a static helper method, not an extension method.
using System.Runtime.CompilerServices;
using System;
using System.Threading.Tasks;

namespace Unity.Services.Vivox
{
    internal static class TaskUtils
    {
        /// <summary>
        /// Safely executes a task-returning function in a "fire and forget" style, guaranteeing that any resulting exception is logged instead of being lost.
        /// This method protects against both synchronous exceptions thrown when creating the task and asynchronous exceptions from the task itself.
        /// It is the robust alternative to unhandled calls like `_ = MyAsyncMethod()`.
        /// </summary>
        /// <param name="taskFunc">A lambda or method group that returns the Task to be executed. The function itself is invoked within the safety of this method's try-catch block.</param>
        /// <param name="caller">The name of the calling method, automatically populated to improve error log context.</param>
        internal static async void FireAndForgetSafe(Func<Task> taskFunc, [CallerMemberName] string caller = "")
        {
            try
            {
                await taskFunc();
            }
            catch (Exception e)
            {
                VivoxLogger.LogException(new Exception($"Exception caught in task from {caller}.", e));
            }
        }
    }
}
