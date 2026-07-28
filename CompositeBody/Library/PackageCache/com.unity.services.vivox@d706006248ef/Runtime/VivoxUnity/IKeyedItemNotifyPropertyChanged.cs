using System.ComponentModel;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// An interface for notifying the consumer of changes to an element in a typed collection.
    /// </summary>
    /// <typeparam name="TK">The key type.</typeparam>
    internal interface IKeyedItemNotifyPropertyChanged<out TK> : INotifyPropertyChanged
    {
        /// <summary>
        /// The unique identifier for the element that raises the property changed event.
        /// </summary>
        TK Key { get; }
    }
}
