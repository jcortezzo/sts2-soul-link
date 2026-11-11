using System.Diagnostics.CodeAnalysis;
using SoulLink.Core.Decisions;

namespace SoulLink.Core.Cards;

/// <summary>
/// A linked choice of cards from players' decks for the same action: everyone who Smiths at a campfire, everyone who
/// bought card removal, everyone who took the same event option. The first pick sets the target and everyone else
/// takes their matching card, as decided by the <see cref="ICardMatchingStrategy"/>.
/// </summary>
public sealed class CardChoiceDecision : LinkedChoice<CardId>
{
    private readonly Dictionary<PlayerId, SelectionScreen> _screens = new();

    /// <param name="matching">Linked cards or deck index, from the settings.</param>
    /// <param name="mode">Nice or mean, from the settings.</param>
    /// <param name="pickCount">How many cards each player picks.</param>
    public CardChoiceDecision(ICardMatchingStrategy matching, LinkMode mode, int pickCount = 1)
        : base(mode, pickCount)
    {
        ArgumentNullException.ThrowIfNull(matching);
        Matching = matching;
    }

    public ICardMatchingStrategy Matching { get; }

    /// <summary>Adds a player doing this action, with their selection screen.</summary>
    public void Join(SelectionScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        AddParticipant(screen.Owner);
        _screens[screen.Owner] = screen;
    }

    /// <summary>Replaces a player's screen, for example when their deck changed before they picked.</summary>
    public void UpdateScreen(SelectionScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        RequireParticipant(screen.Owner);
        _screens[screen.Owner] = screen;
    }

    public SelectionScreen ScreenOf(PlayerId player)
    {
        RequireParticipant(player);
        return _screens[player];
    }

    public CommitCheck CanCommit(PlayerId player, CardId card) => CanCommit(player, Selection.Of(card));

    public Commit<CardId> Commit(PlayerId player, CardId card) => Commit(player, Selection.Of(card));

    protected override IEnumerable<CardId> Options(PlayerId player) => _screens[player].Cards.Select(card => card.Card);

    protected override bool IsAvailable(PlayerId player, CardId option) =>
        _screens.TryGetValue(player, out var screen) && screen.IsEligible(option);

    protected override LinkKey Describe(PlayerId player, Selection<CardId> choice) => Matching.Describe(_screens[player], choice);

    protected override bool TryResolve(LinkKey link, PlayerId player, [MaybeNullWhen(false)] out Selection<CardId> choice) =>
        Matching.TryResolve(link, _screens[player], out choice);
}
