using System;
using System.Collections.Generic;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// Event arguments for key collections with add and delete notifications.
    /// </summary>
    /// <typeparam name="TK">The key type.</typeparam>
    internal sealed class KeyEventArg<TK> : EventArgs
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="key">The key that uniquely identifies the item in the collection.</param>
        public KeyEventArg(TK key)
        {
            Key = key;
        }

        /// <summary>
        /// The key that uniquely identifies the item in the collection.
        /// </summary>
        public TK Key { get; set; }
    }

    /// <summary>
    /// Event arguments for key collections with value changed notifications.
    /// </summary>
    /// <typeparam name="TK">The key type.</typeparam>
    /// <typeparam name="TV">The value type.</typeparam>
    internal sealed class ValueEventArg<TK, TV> : EventArgs
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="key">The key that uniquely identifies the item in the collection.</param>
        /// <param name="value">A value that maps to the key.</param>
        /// <param name="name">The name of the changed property.</param>
        public ValueEventArg(TK key, TV value, string name)
        {
            Key = key;
            Value = value;
            PropertyName = name;
        }

        /// <summary>
        /// The key that uniquely identifies the item in the collection.
        /// </summary>
        public TK Key { get; set; }
        /// <summary>
        /// A value that maps to the key.
        /// </summary>
        public TV Value { get; set; }
        /// <summary>
        /// The name of the changed property.
        /// </summary>
        public string PropertyName { get; set; }
    }

    /// <summary>
    /// A read-only dictionary that raises an event whenever an item is added, removed, or modified.
    /// </summary>
    /// <typeparam name="TK">The key type.</typeparam>
    /// <typeparam name="T">The value type.</typeparam>
    internal interface IReadOnlyDictionary<TK, T> : IEnumerable<T>
    {
        /// <summary>
        /// Raised when an item is added.
        /// </summary>
        event EventHandler<KeyEventArg<TK>> AfterKeyAdded;
        /// <summary>
        /// Raised when an item is about to be removed.
        /// </summary>
        event EventHandler<KeyEventArg<TK>> BeforeKeyRemoved;
        /// <summary>
        /// Raised after a value has been updated.
        /// </summary>
        event EventHandler<ValueEventArg<TK, T>> AfterValueUpdated;

        /// <summary>
        /// All the keys.
        /// </summary>
        ICollection<TK> Keys { get; }
        /// <summary>
        /// The value for a particular key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The value for the key.</returns>
        T this[TK key] { get;  }

        /// <summary>
        /// Determines if a key is contained.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>True if the key is contained.</returns>
        bool ContainsKey(TK key);

        /// <summary>
        /// The number of items in the collection.
        /// </summary>
        int Count { get; }
    }
}
