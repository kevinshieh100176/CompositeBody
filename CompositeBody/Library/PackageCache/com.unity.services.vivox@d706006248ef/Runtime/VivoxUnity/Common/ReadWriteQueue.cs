using System;
using System.Collections.Generic;

namespace Unity.Services.Vivox
{
    internal sealed class ReadWriteQueue<T> : IReadOnlyQueue<T>
    {
        private readonly List<T> _items = new List<T>();
        public event EventHandler<QueueItemAddedEventArgs<T>> AfterItemAdded;
        public T Dequeue()
        {
            if (_items.Count == 0)
                return default(T);
            var item = _items[0];
            _items.RemoveAt(0);
            return item;
        }

        public void Clear()
        {
            _items.Clear();
        }

        public int Count => _items.Count;

        public T Peek()
        {
            if (_items.Count == 0)
                return default(T);
            return _items[0];
        }

        public void Enqueue(T item)
        {
            _items.Add(item);
            AfterItemAdded?.Invoke(this, new QueueItemAddedEventArgs<T>(item));
        }

        public bool Contains(T item)
        {
            foreach (var i in _items)
            {
                if (i.Equals(item))
                    return true;
            }
            return false;
        }

        public int RemoveAll(T item)
        {
            return _items.RemoveAll(i => i.Equals(item));
        }
    }
}
