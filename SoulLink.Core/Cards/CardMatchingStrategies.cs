using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using SoulLink.Core.Decisions;

namespace SoulLink.Core.Cards;

/// <summary>
/// Decides which of a player's cards matches another player's pick, for upgrades, removals and other deck choices.
/// One implementation per <see cref="CardMatching"/> setting.
/// </summary>
public interface ICardMatchingStrategy
{
    CardMatching Kind { get; }

    /// <summary>Describes a player's picks in terms every player can resolve against their own screen.</summary>
    LinkKey Describe(SelectionScreen screen, Selection<CardId> picks);

    /// <summary>
    /// Finds the cards on this screen that match a described pick. Returns false when the player has no matching card.
    /// Eligibility is checked by the decision, so a matching card that can't take the action still resolves.
    /// </summary>
    bool TryResolve(LinkKey link, SelectionScreen screen, [MaybeNullWhen(false)] out Selection<CardId> matches);
}

public static class CardMatchingStrategies
{
    /// <summary>Creates the strategy for a setting.</summary>
    public static ICardMatchingStrategy Create(CardMatching kind, LinkRegistry links) => kind switch
    {
        CardMatching.LinkedCards => new LinkedCardsMatching(links),
        CardMatching.DeckIndex => new DeckIndexMatching(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown card matching."),
    };
}

/// <summary>Your matching card is the one linked to theirs. Unlinked cards have no match.</summary>
public sealed class LinkedCardsMatching : ICardMatchingStrategy
{
    private const string Unlinked = "-";
    private readonly LinkRegistry _links;

    public LinkedCardsMatching(LinkRegistry links)
    {
        ArgumentNullException.ThrowIfNull(links);
        _links = links;
    }

    public CardMatching Kind => CardMatching.LinkedCards;

    public LinkKey Describe(SelectionScreen screen, Selection<CardId> picks) =>
        new(picks.Items
            .Select(card => _links.GroupOf(new CardRef(screen.Owner, card)) is { } group
                ? group.Value.ToString(CultureInfo.InvariantCulture)
                : Unlinked)
            .OrderBy(part => part, StringComparer.Ordinal));

    public bool TryResolve(LinkKey link, SelectionScreen screen, [MaybeNullWhen(false)] out Selection<CardId> matches)
    {
        var cards = new List<CardId>(link.Parts.Count);
        foreach (var part in link.Parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || _links.MemberFor(new LinkGroupId(id), screen.Owner) is not { } member
                || !screen.Contains(member.Card))
            {
                matches = null;
                return false;
            }

            cards.Add(member.Card);
        }

        matches = new Selection<CardId>(cards);
        return true;
    }
}

/// <summary>
/// Your matching card is at the same position on your selection screen. If your screen is shorter, your last card
/// counts, and for multi-card picks the next card back.
/// </summary>
public sealed class DeckIndexMatching : ICardMatchingStrategy
{
    public CardMatching Kind => CardMatching.DeckIndex;

    public LinkKey Describe(SelectionScreen screen, Selection<CardId> picks)
    {
        var positions = picks.Items.Select(screen.IndexOf).ToList();
        if (positions.Contains(-1))
        {
            throw new ArgumentException("Every pick must be on the screen.", nameof(picks));
        }

        positions.Sort();
        return new LinkKey(positions.Select(position => position.ToString(CultureInfo.InvariantCulture)));
    }

    public bool TryResolve(LinkKey link, SelectionScreen screen, [MaybeNullWhen(false)] out Selection<CardId> matches)
    {
        var taken = new HashSet<int>();
        var cards = new List<CardId>(link.Parts.Count);
        foreach (var part in link.Parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var position))
            {
                matches = null;
                return false;
            }

            var target = Math.Min(position, screen.Cards.Count - 1);
            while (target >= 0 && taken.Contains(target))
            {
                target--;
            }

            if (target < 0)
            {
                matches = null;
                return false;
            }

            taken.Add(target);
            cards.Add(screen.Cards[target].Card);
        }

        matches = new Selection<CardId>(cards);
        return true;
    }
}
