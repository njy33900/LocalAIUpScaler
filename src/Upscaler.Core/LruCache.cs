namespace Upscaler.Core;

/// <summary>

/// Small bounded LRU cache. Callers serialize access.

/// </summary>
public sealed class LruCache<TKey, TValue>(int capacity)
where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> entries = new();
    private readonly LinkedList<(TKey Key, TValue Value)> order = new();

    public int Count => entries.Count;

    /// <summary>
    /// 캐시 적중 시 항목을 최근 사용 위치로 이동하고 값을 반환합니다.
    /// </summary>
    /// <param name="key">조회할 캐시 키입니다.</param>
    /// <param name="value">적중한 값이며, 누락 시 기본값입니다.</param>
    /// <returns>항목이 존재하면 true입니다.</returns>
    public bool TryGet(TKey key, out TValue value)
    {
        if (!entries.TryGetValue(key, out var node))
        {
            value = default!;

            return false;
        }

        order.Remove(node);
        order.AddFirst(node);
        value = node.Value.Value;

        return true;
    }

    /// <summary>
    /// 항목을 최근 사용 위치에 저장하고 용량을 초과하면 가장 오래 사용하지 않은 항목부터 제거합니다.
    /// </summary>
    public void Put(TKey key, TValue value)
    {
        if (entries.Remove(key, out var old))
        {
            order.Remove(old);
        }

        var node = order.AddFirst((key, value));
        entries.Add(key, node);

        while (entries.Count > Math.Max(1, capacity))
        {
            var last = order.Last!;
            entries.Remove(last.Value.Key);
            order.RemoveLast();
        }
    }

    /// <summary>
    /// 값 사전과 사용 순서 목록을 함께 비웁니다.
    /// </summary>
    public void Clear()
    {
        entries.Clear();
        order.Clear();
    }
}
