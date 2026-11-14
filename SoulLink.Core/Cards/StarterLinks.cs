namespace SoulLink.Core.Cards;

/// <summary>A card in a starting deck, with the name it pairs by (for example "Strike" for every character's Strike).</summary>
public readonly record struct StarterCard(CardId Card, string Name);

/// <summary>A player's starting deck in deck order.</summary>
public sealed record StarterDeck(PlayerId Player, IReadOnlyList<StarterCard> Cards);

/// <summary>Pairs starting decks into link groups at the start of a run.</summary>
public static class StarterLinks
{
    /// <summary>
    /// Pairs starter cards by name in deck order: the n-th card of a name every deck has links with the n-th card of
    /// that name in the other decks (Strike with Strike, Defend with Defend). Then each character's other starter cards
    /// pair in deck order. Cards with no counterpart stay unlinked.
    /// </summary>
    /// <returns>The groups to link, each with at least two cards and at most one per player.</returns>
    public static IReadOnlyList<IReadOnlyList<CardRef>> Pair(IReadOnlyList<StarterDeck> decks)
    {
        ArgumentNullException.ThrowIfNull(decks);
        if (decks.Select(deck => deck.Player).Distinct().Count() != decks.Count)
        {
            throw new ArgumentException("Each player can have only one starting deck.", nameof(decks));
        }

        if (decks.Count < 2)
        {
            return [];
        }

        var sharedNames = decks
            .Select(deck => deck.Cards.Select(card => card.Name).ToHashSet(StringComparer.Ordinal))
            .Aggregate((shared, names) =>
            {
                shared.IntersectWith(names);
                return shared;
            });

        var groups = new List<IReadOnlyList<CardRef>>();
        var namesInOrder = decks.SelectMany(deck => deck.Cards).Select(card => card.Name).Distinct(StringComparer.Ordinal);
        foreach (var name in namesInOrder.Where(sharedNames.Contains))
        {
            AddByPosition(groups, decks, card => string.Equals(card.Name, name, StringComparison.Ordinal));
        }

        AddByPosition(groups, decks, card => !sharedNames.Contains(card.Name));
        return groups;
    }

    /// <summary>Links the n-th matching card of every deck that has one, for each n.</summary>
    private static void AddByPosition(List<IReadOnlyList<CardRef>> groups, IReadOnlyList<StarterDeck> decks, Func<StarterCard, bool> matches)
    {
        var perDeck = decks.Select(deck => deck.Cards.Where(matches).ToList()).ToList();
        var longest = perDeck.Max(cards => cards.Count);
        for (var position = 0; position < longest; position++)
        {
            var group = new List<CardRef>();
            for (var i = 0; i < decks.Count; i++)
            {
                if (position < perDeck[i].Count)
                {
                    group.Add(new CardRef(decks[i].Player, perDeck[i][position].Card));
                }
            }

            if (group.Count >= 2)
            {
                groups.Add(group);
            }
        }
    }
}
