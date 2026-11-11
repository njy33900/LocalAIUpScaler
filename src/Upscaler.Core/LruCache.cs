namespace Upscaler.Core;

/// <summary>Small bounded LRU cache. Callers serialize access.</summary>
public sealed class LruCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> entries = new();
    private readonly LinkedList<(TKey Key, TValue Value)> order = new();
    public int Count => entries.Count;
    public bool TryGet(TKey key, out TValue value)
    {
        if (!entries.TryGetValue(key, out var node)) { value = default!; return false; }
        order.Remove(node); order.AddFirst(node); value = node.Value.Value; return true;
    }
    public void Put(TKey key, TValue value)
    {
        if (entries.Remove(key, out var old)) order.Remove(old);
        var node = order.AddFirst((key, value)); entries.Add(key, node);
        while (entries.Count > Math.Max(1, capacity))
        {
            var last = order.Last!; entries.Remove(last.Value.Key); order.RemoveLast();
        }
    }
    public void Clear() { entries.Clear(); order.Clear(); }
}
