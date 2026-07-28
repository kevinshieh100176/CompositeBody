using System;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// The event arguments when an item is added to an IReadOnlyQueue.
    /// </summary>
    /// <typeparam name="T">The type of item in the queue.</typeparam>
    internal sealed class QueueItemAddedEventArgs<T> : EventArgs
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="item">The item added to a queue.</param>
        public QueueItemAddedEventArgs(T item)
        {
            Value = item;
        }

        /// <summary>
        /// The value.
        /// </summary>
        public T Value { get; }
    }

    /// <summary>
    /// A queue that raises an event when an item is added.
    /// </summary>
    /// <typeparam name="T">The type of item in the queue</typeparam>
    internal interface IReadOnlyQueue<T>
    {
        /// <summary>
        /// The event that is raised when an item is added.
        /// </summary>
        event EventHandler<QueueItemAddedEventArgs<T>> AfterItemAdded;

        /// <summary>
        /// Remove an item from the queue.
        /// </summary>
        /// <returns>The item. Null if the queue is empty.</returns>
        T Dequeue();
        /// <summary>
        /// Remove all the items from the queue.
        /// </summary>
        void Clear();
        /// <summary>
        /// The count of items in the queue.
        /// </summary>

        int Count { get; }
        /// <summary>
        /// Look at the head of the queue without dequeuing.
        /// </summary>
        /// <returns>The next item in the queue. Null if the queue is empty.</returns>
        T Peek();
    }
}
