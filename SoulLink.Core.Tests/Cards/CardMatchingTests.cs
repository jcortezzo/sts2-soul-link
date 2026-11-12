using SoulLink.Core.Cards;
using SoulLink.Core.Decisions;
using static SoulLink.Core.Tests.TestCards;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests.Cards;

public class CardMatchingTests
{
    [Fact]
    public void Linked_cards_match_the_card_in_the_same_group()
    {
        var links = new LinkRegistry();
        links.Link([Ref(Ana, 3), Ref(Ben, 7)]);
        var matching = new LinkedCardsMatching(links);

        var link = matching.Describe(Screen(Ana, 10), Selection.Of(Card(Ana, 3)));

        Assert.True(matching.TryResolve(link, Screen(Ben, 10), out var matches));
        Assert.Equal(Selection.Of(Card(Ben, 7)), matches);
    }

    [Fact]
    public void An_unlinked_card_has_no_match()
    {
        var matching = new LinkedCardsMatching(new LinkRegistry());

        var link = matching.Describe(Screen(Ana, 10), Selection.Of(Card(Ana, 3)));

        Assert.False(matching.TryResolve(link, Screen(Ben, 10), out _));
    }

    [Fact]
    public void A_linked_card_missing_from_the_screen_has_no_match()
    {
        var links = new LinkRegistry();
        links.Link([Ref(Ana, 3), Ref(Ben, 7)]);
        var matching = new LinkedCardsMatching(links);

        var link = matching.Describe(Screen(Ana, 10), Selection.Of(Card(Ana, 3)));

        Assert.False(matching.TryResolve(link, Screen(Ben, 5), out _));
    }

    [Fact]
    public void Deck_index_matches_the_same_position()
    {
        var matching = new DeckIndexMatching();

        var link = matching.Describe(Screen(Ana, 20), Selection.Of(Card(Ana, 4)));

        Assert.True(matching.TryResolve(link, Screen(Ben, 20), out var matches));
        Assert.Equal(Selection.Of(Card(Ben, 4)), matches);
    }

    [Fact]
    public void Deck_index_uses_the_last_card_on_a_shorter_screen()
    {
        var matching = new DeckIndexMatching();

        var link = matching.Describe(Screen(Ana, 20), Selection.Of(Card(Ana, 17)));

        Assert.True(matching.TryResolve(link, Screen(Ben, 15), out var matches));
        Assert.Equal(Selection.Of(Card(Ben, 15)), matches);
    }

    [Fact]
    public void Deck_index_steps_back_for_several_picks_past_the_end()
    {
        var matching = new DeckIndexMatching();

        var link = matching.Describe(Screen(Ana, 20), Selection.Of(Card(Ana, 18), Card(Ana, 16)));

        Assert.True(matching.TryResolve(link, Screen(Ben, 15), out var matches));
        Assert.Equal(Selection.Of(Card(Ben, 14), Card(Ben, 15)), matches);
        Assert.False(matching.TryResolve(link, Screen(Cal, 1), out _));
    }

    [Fact]
    public void Deck_index_needs_picks_from_the_screen()
    {
        var matching = new DeckIndexMatching();

        Assert.Throws<ArgumentException>(() => matching.Describe(Screen(Ana, 5), Selection.Of(Card(Ana, 9))));
    }

    [Theory]
    [InlineData(CardMatching.LinkedCards, typeof(LinkedCardsMatching))]
    [InlineData(CardMatching.DeckIndex, typeof(DeckIndexMatching))]
    public void The_setting_picks_the_strategy(CardMatching setting, Type expected)
    {
        var strategy = CardMatchingStrategies.Create(setting, new LinkRegistry());

        Assert.IsType(expected, strategy);
        Assert.Equal(setting, strategy.Kind);
    }
}
