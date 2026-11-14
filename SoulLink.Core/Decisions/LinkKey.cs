namespace SoulLink.Core.Decisions;

/// <summary>
/// A player-independent description of a committed choice, like "slot 2" or "the cards in link group 7".
/// Every peer resolves it against each player's own options, so the host can send it to clients as is.
/// </summary>
public sealed class LinkKey : IEquatable<LinkKey>
{
    private readonly string[] _parts;

    public LinkKey(IEnumerable<string> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        _parts = parts.ToArray();
    }

    public IReadOnlyList<string> Parts => _parts;

    public bool Equals(LinkKey? other) => other is not null && _parts.AsSpan().SequenceEqual(other._parts);

    public override bool Equals(object? obj) => Equals(obj as LinkKey);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var part in _parts)
        {
            hash.Add(part, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => string.Join("|", _parts);
}
