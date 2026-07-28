using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;

namespace Unity.Services.Vivox
{
    internal sealed class ReadWriteDictionary<TK, TI, T> : IReadOnlyDictionary<TK, TI>
        where T : class, TI
        where TI : IKeyedItemNotifyPropertyChanged<TK>
    {
        private readonly Dictionary<TK, TI> _items = new Dictionary<TK, TI>();

        public TI this[TK key]
        {
            get
            {
                return _items[key];
            }
            set
            {
                if (!_items.ContainsKey(key) && !_items.ContainsValue((T)value))
                {
                    _items[key] = (T)value;
                    value.PropertyChanged += Value_PropertyChanged;
                    AfterKeyAdded?.Invoke(this, new KeyEventArg<TK>(key));
                }
            }
        }

        private void Value_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            T item = (T)sender;
            AfterValueUpdated?.Invoke(this, new ValueEventArg<TK, TI>(item.Key, item, e.PropertyName));
        }

        public ICollection<TK> Keys => _items.Keys;

        public void Clear()
        {
            _items.Clear();
        }

        public bool ContainsKey(TK key)
        {
            return _items.ContainsKey(key);
        }

        public bool Remove(TK key)
        {
            if (_items.ContainsKey(key))
            {
                BeforeKeyRemoved?.Invoke(this, new KeyEventArg<TK>(key));
                T item = (T)_items[key];
                _items.Remove(key);
                item.PropertyChanged -= Value_PropertyChanged;
                return true;
            }
            return false;
        }

        public event EventHandler<KeyEventArg<TK>> AfterKeyAdded;
        public event EventHandler<ValueEventArg<TK, TI>> AfterValueUpdated;
        public event EventHandler<KeyEventArg<TK>> BeforeKeyRemoved;
        public int Count => _items.Count;

        public T At(TK key)
        {
            return (T)_items[key];
        }

        public bool ContainsValue(T item)
        {
            return _items.ContainsValue(item);
        }

        public IEnumerator<TI> GetEnumerator()
        {
            return _items.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
