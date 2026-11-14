namespace SoulLink.Core.Decisions;

/// <summary>
/// What a player picks in one decision: usually one option, or several for "choose N" screens.
/// Two selections are equal when they hold the same items in any order.
/// </summary>
public sealed class Selection<T> : IEquatable<Selection<T>>
    where T : notnull
{
    private readonly T[] _items;
    private readonly HashSet<T> _set;

    public Selection(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
        _set = new HashSet<T>(_items);
        if (_set.Count != _items.Length)
        {
            throw new ArgumentException("A selection can't hold the same item twice.", nameof(items));
        }
    }

    /// <summary>The picked items in the order they were given.</summary>
    public IReadOnlyList<T> Items => _items;

    public int Count => _items.Length;

    public bool Contains(T item) => _set.Contains(item);

    public bool Equals(Selection<T>? other) => other is not null && _set.SetEquals(other._set);

    public override bool Equals(object? obj) => Equals(obj as Selection<T>);

    public override int GetHashCode()
    {
        // Order-independent, so equal sets hash the same.
        var hash = 0;
        foreach (var item in _set)
        {
            hash ^= EqualityComparer<T>.Default.GetHashCode(item) * 397;
        }

        return hash ^ _set.Count;
    }

    public override string ToString() => string.Join(" + ", _items);
}

/// <summary>Creates selections with type inference.</summary>
public static class Selection
{
    public static Selection<T> Of<T>(params T[] items)
        where T : notnull => new(items);
}
