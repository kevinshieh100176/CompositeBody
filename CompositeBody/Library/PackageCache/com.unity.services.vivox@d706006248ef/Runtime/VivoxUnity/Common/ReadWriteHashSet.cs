using System;
using System.Collections.Generic;

namespace Unity.Services.Vivox
{
    class ReadWriteHashSet<T> :
        IReadOnlyHashSet<T>
    {
        private readonly HashSet<T> _items = new HashSet<T>();
        public event EventHandler<KeyEventArg<T>> AfterKeyAdded;
        public event EventHandler<KeyEventArg<T>> BeforeKeyRemoved;

        public bool Contains(T key)
        {
            return _items.Contains(key);
        }

        public bool Add(T key)
        {
            bool added = _items.Add(key);
            if (added)
                AfterKeyAdded?.Invoke(this, new KeyEventArg<T>(key));
            return added;
        }

        public bool Remove(T key)
        {
            if (_items.Contains(key))
                BeforeKeyRemoved?.Invoke(this, new KeyEventArg<T>(key));
            return _items.Remove(key);
        }

        public int Count => _items.Count;

        public void Clear()
        {
            _items.Clear();
        }
    }
}
