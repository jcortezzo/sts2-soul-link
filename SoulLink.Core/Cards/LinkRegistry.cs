namespace SoulLink.Core.Cards;

/// <summary>
/// Tracks which cards are linked across players for the linked cards setting. A link group holds at most one card
/// per player; a card in no group, or alone in its group, has nothing to match.
/// </summary>
/// <remarks>
/// Every peer applies the same changes in the same order, so group ids match across peers and can be sent over the
/// network.
/// </remarks>
public sealed class LinkRegistry
{
    private readonly Dictionary<CardRef, LinkGroupId> _groupOf = new();
    private readonly Dictionary<LinkGroupId, List<CardRef>> _members = new();
    private int _lastId;

    /// <summary>All current groups.</summary>
    public IReadOnlyCollection<LinkGroupId> Groups => _members.Keys;

    /// <summary>
    /// Links cards gained from the same linked choice, like the same card reward slot or shop slot.
    /// </summary>
    /// <returns>The new group, or null when fewer than two cards were given: a solo card stays unlinked.</returns>
    /// <exception cref="ArgumentException">Two cards belong to the same player, or a card is already linked.</exception>
    public LinkGroupId? Link(IEnumerable<CardRef> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var list = cards.ToList();
        if (list.Select(card => card.Owner).Distinct().Count() != list.Count)
        {
            throw new ArgumentException("A link group holds at most one card per player.", nameof(cards));
        }

        foreach (var card in list)
        {
            if (_groupOf.ContainsKey(card))
            {
                throw new ArgumentException($"{card} is already linked.", nameof(cards));
            }
        }

        if (list.Count < 2)
        {
            return null;
        }

        var id = new LinkGroupId(++_lastId);
        Add(id, list);
        return id;
    }

    /// <summary>Adds a card to an existing group, for a player who gained theirs after the group formed.</summary>
    /// <returns>False when the group doesn't exist or already has a card of that player.</returns>
    /// <exception cref="ArgumentException">The card is already linked.</exception>
    public bool Add(LinkGroupId group, CardRef card)
    {
        if (_groupOf.ContainsKey(card))
        {
            throw new ArgumentException($"{card} is already linked.", nameof(card));
        }

        if (!_members.TryGetValue(group, out var members) || members.Exists(member => member.Owner == card.Owner))
        {
            return false;
        }

        members.Add(card);
        _groupOf[card] = group;
        return true;
    }

    /// <summary>The card's group, or null if it's unlinked.</summary>
    public LinkGroupId? GroupOf(CardRef card) => _groupOf.TryGetValue(card, out var id) ? id : null;

    /// <summary>The cards in a group, or an empty list if the group doesn't exist.</summary>
    public IReadOnlyList<CardRef> MembersOf(LinkGroupId group) =>
        _members.TryGetValue(group, out var members) ? members : [];

    /// <summary>The player's card in the group, or null if they have none.</summary>
    public CardRef? MemberFor(LinkGroupId group, PlayerId player)
    {
        foreach (var member in MembersOf(group))
        {
            if (member.Owner == player)
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>The other player's card linked to this one, or null.</summary>
    public CardRef? LinkedCard(CardRef card, PlayerId player) =>
        GroupOf(card) is { } group ? MemberFor(group, player) : null;

    /// <summary>Transforming keeps the link: the new card takes the old card's place in its group.</summary>
    public void Replace(CardRef old, CardRef replacement)
    {
        if (old.Owner != replacement.Owner)
        {
            throw new ArgumentException("A transformed card stays in the same deck.", nameof(replacement));
        }

        if (_groupOf.ContainsKey(replacement))
        {
            throw new ArgumentException($"{replacement} is already linked.", nameof(replacement));
        }

        if (!_groupOf.Remove(old, out var id))
        {
            return;
        }

        var members = _members[id];
        members[members.IndexOf(old)] = replacement;
        _groupOf[replacement] = id;
    }

    /// <summary>
    /// Removing a card takes it out of its group. The rest of the group stays, even a single card, so players who
    /// haven't removed their linked card yet still find it.
    /// </summary>
    public void Remove(CardRef card)
    {
        if (!_groupOf.Remove(card, out var id))
        {
            return;
        }

        var members = _members[id];
        members.Remove(card);
        if (members.Count == 0)
        {
            _members.Remove(id);
        }
    }

    /// <summary>All groups and their cards, for saving with the run.</summary>
    public IReadOnlyList<LinkGroupSnapshot> Snapshot() =>
        _members
            .OrderBy(entry => entry.Key.Value)
            .Select(entry => new LinkGroupSnapshot(entry.Key, entry.Value.ToList()))
            .ToList();

    /// <summary>Rebuilds a registry from a saved snapshot, keeping the group ids.</summary>
    public static LinkRegistry Restore(IEnumerable<LinkGroupSnapshot> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var registry = new LinkRegistry();
        foreach (var group in groups)
        {
            if (registry._members.ContainsKey(group.Id) || group.Cards.Count == 0)
            {
                throw new ArgumentException($"{group.Id} is duplicated or empty.", nameof(groups));
            }

            registry.Add(group.Id, group.Cards.ToList());
            registry._lastId = Math.Max(registry._lastId, group.Id.Value);
        }

        return registry;
    }

    private void Add(LinkGroupId id, List<CardRef> cards)
    {
        _members[id] = cards;
        foreach (var card in cards)
        {
            _groupOf[card] = id;
        }
    }
}

/// <summary>One link group as saved with the run.</summary>
public sealed record LinkGroupSnapshot(LinkGroupId Id, IReadOnlyList<CardRef> Cards);
