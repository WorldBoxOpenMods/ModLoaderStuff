using System.Collections;
namespace NeoModLoader.utils.Collections;
/// <summary>
/// a list of slots which can contain null or a variable
/// </summary>
/// <remarks>thread safe</remarks>
public sealed class SlotList<T> : IDictionary<int, T>, ICollection<T>
{
    private readonly Dictionary<int, T>   _data     = new();
    private readonly Dictionary<int, int> _position = new(); 
    private readonly Stack<int>           _free     = new();

    private int[] _active = new int[8];  
    private int   _count  = 0;
    private int   _next   = 0;           

    private readonly object _lock = new();
    
    public T GetRandom()
    {
        var snap = Volatile.Read(ref _active);
        int c    = Volatile.Read(ref _count);

        if (c == 0) throw new InvalidOperationException("Collection is empty.");

        int candidate = snap[Randy.randomInt(0, c)];

        lock (_lock)
        {
            if (_data.TryGetValue(candidate, out var value))
                return value;

            // candidate was removed in the race window — fall back to a safe read
            return _count == 0 ? throw new InvalidOperationException("Collection is empty.") : _data[_active[Randy.randomInt(0, _count)]];
        }
    }

    public T? Get(int index)
    {
        lock (_lock)
        {
            if (_data.TryGetValue(index, out var value))
            {
                return value;
            }
        }
        return default;
    }
    public int Add(T value)
    {
        lock (_lock)
        {
            int index = _free.Count > 0 ? _free.Pop() : _next++;
            InsertInternal(index, value);
            return index;
        }
    }
    public void Set(int index, T value)
    {
        lock (_lock)
        {
            if (_data.ContainsKey(index))
            {
                _data[index] = value;
            }
            else
            {
                if (index >= _next) _next = index + 1;

                InsertInternal(index, value);
            }
        }
    }

    public bool Remove(int index)
    {
        lock (_lock)
        {
            if (!_data.ContainsKey(index)) return false;
            RemoveInternal(index);
            return true;
        }
    }
    private void InsertInternal(int index, T value)
    {
        _data[index] = value;
        EnsureCapacity(_count + 1);

        _active[_count]  = index;
        _position[index] = _count;
        _count++;
    }

    private void RemoveInternal(int index)
    {
        int pos       = _position[index];
        int lastIndex = _active[_count - 1];

        _active[pos]        = lastIndex;
        _position[lastIndex] = pos;
        _count--;

        _data.Remove(index);
        _position.Remove(index);
        _free.Push(index);
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _active.Length) return;

        int newSize = _active.Length;
        while (newSize < required) newSize *= 2;

        var next = new int[newSize];
        Array.Copy(_active, next, _count);
        Volatile.Write(ref _active, next);
    }
    public T this[int key]
    {
        get => Get(key);
        set => Set(key, value);
    }
    /// <summary>
    /// removes the first instance of the item
    /// </summary>
    public bool Remove(T item)
    {
        lock (_lock)
        {
            foreach (var pair in _data)
            {
                if (Equals(pair.Value, item))
                {
                    RemoveInternal(pair.Key);
                    return true;
                }
            }
        }
        return false;
    }

    public int IndexOf(T item)
    {
        lock (_lock)
        {
            foreach (var pair in _data)
            {
                if (Equals(pair.Value, item))
                {
                    return pair.Key;
                }
            }
        }
        return -1;
    }

    public int Count { get { lock (_lock) return _count; } }

    public bool IsReadOnly => false;

    public ICollection<int> Keys
    {
        get { lock (_lock) return _data.Keys.ToList(); }
    }

    public ICollection<T> Values
    {
        get { lock (_lock) return _data.Values.ToList(); }
    }

    public void Add(int key, T value) => Set(key, value);

    void ICollection<KeyValuePair<int, T>>.Add(KeyValuePair<int, T> item)
        => Set(item.Key, item.Value);

    public bool ContainsKey(int key)
    {
        lock (_lock) return _data.ContainsKey(key);
    }

    public bool Contains(KeyValuePair<int, T> item)
    {
        lock (_lock)
            return _data.TryGetValue(item.Key, out var v) &&
                   Equals(v, item.Value);
    }

    public bool TryGetValue(int key, out T value)
    {
        lock (_lock) return _data.TryGetValue(key, out value);
    }
    public bool Remove(KeyValuePair<int, T> item)
    {
        lock (_lock)
        {
            if (!_data.TryGetValue(item.Key, out var v)) return false;
            if (!Equals(v, item.Value)) return false;

            RemoveInternal(item.Key); 
            return true;
        }
    }

    public void CopyTo(KeyValuePair<int, T>[] array, int arrayIndex)
    {
        lock (_lock)
            foreach (var kv in _data)
                array[arrayIndex++] = kv;
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        List<T> snapshot;
        lock (_lock) snapshot = _data.Values.ToList();
        return snapshot.GetEnumerator();
    }

    public IEnumerator<KeyValuePair<int, T>> GetEnumerator()
    {
        List<KeyValuePair<int, T>> snapshot;
        lock (_lock) snapshot = _data.ToList();
        return snapshot.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void ICollection<T>.Add(T item)
    {
        Add(item);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _data.Clear();
            _position.Clear();
            _free.Clear();
            _active = new int[8];
            _count  = 0;
            _next   = 0;
        }
    }

    public bool Contains(T item)
    {
        lock (_lock)
        {
            return _data.Values.Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_lock)
            foreach (var kv in _data)
                array[arrayIndex++] = kv.Value;
    }
}