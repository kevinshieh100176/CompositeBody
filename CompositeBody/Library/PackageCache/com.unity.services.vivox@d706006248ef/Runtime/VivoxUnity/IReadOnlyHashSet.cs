using System;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// A read-only hash set that raises events when keys are added or removed.
    /// </summary>
    /// <typeparam name="T">The type of item in the hashset</typeparam>
    internal interface IReadOnlyHashSet<T>
    {
        /// <summary>
        /// Raised after adding a key.
        /// </summary>
        event EventHandler<KeyEventArg<T>> AfterKeyAdded;
        /// <summary>
        /// Raised prior to removing a key.
        /// </summary>
        event EventHandler<KeyEventArg<T>> BeforeKeyRemoved;

        /// <summary>
        /// Indicates whether a key is contained in the collection.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>True if the key is contained in the collection.</returns>
        bool Contains(T key);
        /// <summary>
        /// The number of items in the collection.
        /// </summary>
        int Count { get; }
    }
}
