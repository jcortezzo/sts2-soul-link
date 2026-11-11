namespace SoulLink.Core.Cards;

/// <summary>One card on a selection screen, and whether the action can take it (for example, not already upgraded).</summary>
public readonly record struct ScreenCard(CardId Card, bool Eligible = true);

/// <summary>
/// The cards a player sees when choosing from their deck, in the order the game shows them: left to right, then top
/// to bottom. Deck index matching counts positions on this screen.
/// </summary>
public sealed class SelectionScreen
{
    private readonly ScreenCard[] _cards;
    private readonly Dictionary<CardId, int> _positions = new();

    public SelectionScreen(PlayerId owner, IEnumerable<ScreenCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        Owner = owner;
        _cards = cards.ToArray();
        for (var i = 0; i < _cards.Length; i++)
        {
            if (!_positions.TryAdd(_cards[i].Card, i))
            {
                throw new ArgumentException($"{_cards[i].Card} appears twice on the screen.", nameof(cards));
            }
        }
    }

    public PlayerId Owner { get; }

    public IReadOnlyList<ScreenCard> Cards => _cards;

    /// <summary>A screen where every card is eligible.</summary>
    public static SelectionScreen Of(PlayerId owner, IEnumerable<CardId> cards) =>
        new(owner, cards.Select(card => new ScreenCard(card)));

    /// <summary>The card's 0-based position, or -1 if it isn't on the screen.</summary>
    public int IndexOf(CardId card) => _positions.TryGetValue(card, out var position) ? position : -1;

    public bool Contains(CardId card) => _positions.ContainsKey(card);

    public bool IsEligible(CardId card) => _positions.TryGetValue(card, out var position) && _cards[position].Eligible;
}
