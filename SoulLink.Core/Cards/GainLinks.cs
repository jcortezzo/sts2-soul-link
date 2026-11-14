namespace SoulLink.Core.Cards;

/// <summary>
/// Links cards as players report gaining them from the same source: the same option of the same linked decision,
/// decided by the same lead. Players report at different times, so the first card of a source waits until a second
/// player's card arrives, and later players join the group that formed.
/// </summary>
/// <remarks>
/// When a source gives several cards, the n-th card of each player links with the n-th card of the others. Every peer
/// applies the same reports in the same order, so the groups match across peers.
/// </remarks>
public sealed class GainLinks
{
    private readonly LinkRegistry _links;
    private readonly Dictionary<string, List<Slot>> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Source, PlayerId Player), int> _reported = new();

    public GainLinks(LinkRegistry links)
    {
        ArgumentNullException.ThrowIfNull(links);
        _links = links;
    }

    /// <summary>A key for a source that every player who gained from it builds the same way.</summary>
    /// <param name="table">The linked decision's table.</param>
    /// <param name="option">The option taken, like a card reward slot or a shop slot.</param>
    /// <param name="root">The sequence of the lead commit or purchase that decided it.</param>
    public static string SourceKey(string table, string option, int root) => $"{table}|{option}|{root}";

    /// <summary>Records the cards a player gained from a source, linking them with the other players' cards from it.</summary>
    /// <returns>The groups the cards joined or formed.</returns>
    public IReadOnlyList<LinkGroupId> Gain(string source, PlayerId player, IReadOnlyList<CardId> cards)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(cards);
        if (!_sources.TryGetValue(source, out var slots))
        {
            slots = [];
            _sources[source] = slots;
        }

        var groups = new List<LinkGroupId>();
        var first = _reported.GetValueOrDefault((source, player));
        _reported[(source, player)] = first + cards.Count;
        for (var i = first; i < first + cards.Count; i++)
        {
            while (slots.Count <= i)
            {
                slots.Add(new Slot());
            }

            var card = new CardRef(player, cards[i - first]);
            if (_links.GroupOf(card) is not null)
            {
                continue;
            }

            var slot = slots[i];
            if (slot.Group is { } group)
            {
                if (_links.Add(group, card))
                {
                    groups.Add(group);
                }
            }
            else if (slot.Waiting is { } waiting && waiting.Owner != player && _links.GroupOf(waiting) is null)
            {
                slot.Group = _links.Link([waiting, card]);
                slot.Waiting = null;
                if (slot.Group is { } formed)
                {
                    groups.Add(formed);
                }
            }
            else
            {
                slot.Waiting = card;
            }
        }

        return groups;
    }

    private sealed class Slot
    {
        public LinkGroupId? Group { get; set; }

        public CardRef? Waiting { get; set; }
    }
}
