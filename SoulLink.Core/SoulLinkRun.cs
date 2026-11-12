using SoulLink.Core.Campfires;
using SoulLink.Core.Cards;
using SoulLink.Core.Decisions;
using SoulLink.Core.Rewards;
using SoulLink.Core.Shops;

namespace SoulLink.Core;

/// <summary>A card a player gained from an option in a linked decision.</summary>
public readonly record struct GainedCard(PlayerId Player, OptionKey Option, CardId Card);

/// <summary>A card a player bought in a shop.</summary>
public readonly record struct PurchasedCard(Purchase Purchase, CardId Card);

/// <summary>
/// The rules engine's state for one run: the settings, card links and run stats. Creates linked decisions with the
/// run's settings and links the cards they give.
/// </summary>
public sealed class SoulLinkRun
{
    public SoulLinkRun(SoulLinkSettings settings, LinkRegistry? links = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        Links = links ?? new LinkRegistry();
        Gains = new GainLinks(Links);
        Matching = CardMatchingStrategies.Create(settings.CardMatching, Links);
        Tracker = new RunTracker(settings);
    }

    public SoulLinkSettings Settings { get; }

    public LinkRegistry Links { get; }

    /// <summary>Links cards players report gaining from the same linked choice.</summary>
    public GainLinks Gains { get; }

    /// <summary>The card matching strategy for the run's setting.</summary>
    public ICardMatchingStrategy Matching { get; }

    public RunTracker Tracker { get; }

    /// <summary>A decision matched by option: events, Ancients, relic rewards, chests.</summary>
    public OptionDecision NewOptionDecision(int pickCount = 1) => new(Settings.Mode, pickCount, Settings.WhoDecides);

    /// <summary>A choice of cards from players' decks for the same action.</summary>
    public CardChoiceDecision NewCardChoice(int pickCount = 1) => new(Matching, Settings.Mode, pickCount, Settings.WhoDecides);

    public CardRewardRound NewCardRewardRound() => new(Settings.Mode, Settings.WhoDecides);

    public ShopSession NewShop() => new(Settings.Mode, Matching, Settings.WhoDecides);

    public CampfireSession NewCampfire() => new(Settings.Mode, Matching, Settings.WhoDecides);

    /// <summary>Pairs the starting decks at the start of the run.</summary>
    public void LinkStarterDecks(IReadOnlyList<StarterDeck> decks)
    {
        foreach (var group in StarterLinks.Pair(decks))
        {
            Links.Link(group);
        }
    }

    /// <summary>
    /// Links cards gained from the same option of a decision by players who share the same lead: the same card reward
    /// slot, event pick or Ancient choice. A card only one player gained stays unlinked.
    /// </summary>
    /// <returns>The new link groups.</returns>
    public IReadOnlyList<LinkGroupId> LinkGainedCards(OptionDecision decision, IEnumerable<GainedCard> gained)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(gained);
        return LinkGroups(gained
            .GroupBy(card => (Root: RootOf(decision, card.Player), card.Option))
            .Select(group => group.Select(card => new CardRef(card.Player, card.Card))));
    }

    /// <summary>Links cards bought from the same shop slot because of the same purchase.</summary>
    /// <returns>The new link groups.</returns>
    public IReadOnlyList<LinkGroupId> LinkPurchasedCards(IEnumerable<PurchasedCard> purchased)
    {
        ArgumentNullException.ThrowIfNull(purchased);
        return LinkGroups(purchased
            .GroupBy(card => (card.Purchase.Slot, card.Purchase.Root))
            .Select(group => group.Select(card => new CardRef(card.Purchase.Player, card.Card))));
    }

    /// <summary>Links copies made by a linked duplicate action among players who share the same lead.</summary>
    /// <returns>The new link groups.</returns>
    public IReadOnlyList<LinkGroupId> LinkCopies(CardChoiceDecision decision, IEnumerable<CardRef> copies)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(copies);
        return LinkGroups(copies.GroupBy(copy => RootOf(decision, copy.Owner)));
    }

    private static int RootOf<TAtom>(LinkedChoice<TAtom> decision, PlayerId player)
        where TAtom : notnull =>
        decision.CommitOf(player)?.Root
        ?? throw new ArgumentException($"{player} hasn't committed in this decision.", nameof(player));

    private List<LinkGroupId> LinkGroups(IEnumerable<IEnumerable<CardRef>> groups)
    {
        var ids = new List<LinkGroupId>();
        foreach (var group in groups)
        {
            if (Links.Link(group) is { } id)
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
