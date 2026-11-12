using SoulLink.Core.Cards;
using SoulLink.Core.Decisions;
using static SoulLink.Core.Tests.TestCards;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests.Cards;

public class CardChoiceDecisionTests
{
    private readonly LinkRegistry _links = new();

    public CardChoiceDecisionTests()
    {
        // Each player's card n is linked with everyone else's card n.
        for (var n = 1; n <= 10; n++)
        {
            _links.Link([Ref(Ana, n), Ref(Ben, n), Ref(Cal, n)]);
        }
    }

    private CardChoiceDecision Linked(LinkMode mode, int pickCount = 1) =>
        new(new LinkedCardsMatching(_links), mode, pickCount);

    private static SelectionScreen ScreenWithout(PlayerId owner, int count, int missing) =>
        SelectionScreen.Of(owner, Deck(owner, count).Where(card => card != Card(owner, missing)));

    [Fact]
    public void Everyone_takes_their_card_linked_to_the_first_pick()
    {
        var decision = Linked(LinkMode.Nice);
        decision.Join(Screen(Ana, 10));
        decision.Join(Screen(Ben, 10));

        decision.Commit(Ana, Card(Ana, 4));

        Assert.Equal(Selection.Of(Card(Ben, 4)), decision.StatusOf(Ben).Binding!.Required);
        Assert.Equal(CommitCheck.MustFollow(Ana), decision.CanCommit(Ben, Card(Ben, 5)));
    }

    [Fact]
    public void A_player_whose_linked_card_cannot_take_the_action_picks_freely_in_nice_mode()
    {
        var decision = Linked(LinkMode.Nice);
        decision.Join(Screen(Ana, 10));
        decision.Join(ScreenWithout(Ben, 10, missing: 1));
        decision.Join(new SelectionScreen(Cal, Deck(Cal, 10).Select(card => new ScreenCard(card, Eligible: card != Card(Cal, 1)))));

        decision.Commit(Ana, Card(Ana, 1));

        Assert.Equal(ChoiceState.Free, decision.StatusOf(Ben).State);
        Assert.Equal(ChoiceState.Free, decision.StatusOf(Cal).State);
        Assert.True(decision.CanCommit(Ben, Card(Ben, 6)).IsAllowed);
    }

    [Fact]
    public void Mean_mode_locks_groups_someone_cannot_follow()
    {
        var decision = Linked(LinkMode.Mean);
        decision.Join(Screen(Ana, 10));
        decision.Join(ScreenWithout(Ben, 10, missing: 1));

        Assert.Equal(CommitCheck.GreyedOut(Ben), decision.CanCommit(Ana, Card(Ana, 1)));
        Assert.True(decision.CanCommit(Ana, Card(Ana, 2)).IsAllowed);
    }

    [Fact]
    public void Mean_mode_locks_unlinked_cards()
    {
        var decision = Linked(LinkMode.Mean);
        decision.Join(Screen(Ana, 12));
        decision.Join(Screen(Ben, 12));

        Assert.Equal(CommitCheck.GreyedOut(Ben), decision.CanCommit(Ana, Card(Ana, 11)));
    }

    [Fact]
    public void Deck_index_follows_the_same_position_or_the_last_card()
    {
        var decision = new CardChoiceDecision(new DeckIndexMatching(), LinkMode.Nice);
        decision.Join(Screen(Ana, 20));
        decision.Join(Screen(Ben, 15));

        decision.Commit(Ana, Card(Ana, 17));

        Assert.Equal(Selection.Of(Card(Ben, 15)), decision.StatusOf(Ben).Binding!.Required);
    }

    [Fact]
    public void Followers_do_not_bind_players_who_cannot_follow_the_leader()
    {
        // Ana's card 17 maps to Cal's last card, which is ineligible, so Cal is free. Ben follows with his card 15,
        // which Cal could match, but a follow binds no one.
        var decision = new CardChoiceDecision(new DeckIndexMatching(), LinkMode.Nice);
        decision.Join(Screen(Ana, 20));
        decision.Join(Screen(Ben, 15));
        decision.Join(new SelectionScreen(Cal, Deck(Cal, 16).Select(card => new ScreenCard(card, Eligible: card != Card(Cal, 16)))));

        decision.Commit(Ana, Card(Ana, 17));
        decision.Commit(Ben, Card(Ben, 15));

        Assert.Equal(ChoiceState.Free, decision.StatusOf(Cal).State);
    }

    [Fact]
    public void Multi_card_picks_match_as_a_set()
    {
        var decision = Linked(LinkMode.Nice, pickCount: 2);
        decision.Join(Screen(Ana, 10));
        decision.Join(Screen(Ben, 10));

        decision.Commit(Ana, Selection.Of(Card(Ana, 2), Card(Ana, 8)));

        Assert.Equal(Selection.Of(Card(Ben, 8), Card(Ben, 2)), decision.StatusOf(Ben).Binding!.Required);
    }

    [Fact]
    public void The_target_holds_after_the_leaders_card_leaves_the_deck()
    {
        var decision = Linked(LinkMode.Nice);
        decision.Join(Screen(Ana, 10));
        decision.Join(Screen(Ben, 10));
        decision.Commit(Ana, Card(Ana, 3));

        // Ana's removal goes through before Ben picks.
        _links.Remove(Ref(Ana, 3));

        Assert.Equal(Selection.Of(Card(Ben, 3)), decision.StatusOf(Ben).Binding!.Required);
    }

    [Fact]
    public void Players_see_the_card_each_teammate_would_take()
    {
        var decision = Linked(LinkMode.Nice);
        decision.Join(Screen(Ana, 12));
        decision.Join(ScreenWithout(Ben, 10, missing: 3));

        Assert.Equal(Selection.Of(Card(Ben, 4)), decision.MatchFor(Ana, Selection.Of(Card(Ana, 4)), Ben));
        Assert.Null(decision.MatchFor(Ana, Selection.Of(Card(Ana, 3)), Ben));
        Assert.Null(decision.MatchFor(Ana, Selection.Of(Card(Ana, 11)), Ben));
        Assert.Null(decision.MatchFor(Ana, Selection.Of(Card(Ana, 4)), Cal));

        var byIndex = new CardChoiceDecision(new DeckIndexMatching(), LinkMode.Nice);
        byIndex.Join(Screen(Ana, 12));
        byIndex.Join(Screen(Ben, 10));
        Assert.Equal(Selection.Of(Card(Ben, 10)), byIndex.MatchFor(Ana, Selection.Of(Card(Ana, 12)), Ben));
    }

    [Fact]
    public void Selections_before_confirming_show_to_teammates_as_a_pick_would_match()
    {
        var decision = new CardChoiceDecision(new DeckIndexMatching(), LinkMode.Nice, pickCount: 2);
        decision.Join(Screen(Ana, 6));
        decision.Join(Screen(Ben, 4));

        decision.Mark(Ana, [Card(Ana, 2), Card(Ana, 6)]);
        decision.Mark(Ben, [Card(Ben, 4)]);

        // Ana's sixth card is past the end of Ben's screen, so it shows on his last card, as her pick would.
        Assert.Equal([Ana], decision.MarkersFor(Ben, Card(Ben, 2)));
        Assert.Equal([Ana], decision.MarkersFor(Ben, Card(Ben, 4)));
        Assert.Empty(decision.MarkersFor(Ben, Card(Ben, 1)));
        Assert.Equal([Ben], decision.MarkersFor(Ana, Card(Ana, 4)));
        Assert.Empty(decision.MarkersFor(Ana, Card(Ana, 6)));
        Assert.Equal([Card(Ben, 4)], decision.MarksOf(Ben));
    }

    [Fact]
    public void Selections_never_decide_even_when_they_line_up()
    {
        var decision = new CardChoiceDecision(new DeckIndexMatching(), LinkMode.Nice, pickCount: 2);
        decision.Join(Screen(Ana, 5));
        decision.Join(Screen(Ben, 5));

        decision.Mark(Ana, [Card(Ana, 1), Card(Ana, 2)]);
        decision.Mark(Ben, [Card(Ben, 1), Card(Ben, 2)]);

        Assert.Empty(decision.Settle());
        Assert.Empty(decision.Commits);
    }

    [Fact]
    public void Changing_the_selection_after_confirming_takes_the_vote_back()
    {
        var decision = new CardChoiceDecision(new DeckIndexMatching(), LinkMode.Nice, pickCount: 2);
        decision.Join(Screen(Ana, 5));
        decision.Join(Screen(Ben, 5));
        var anas = Selection.Of(Card(Ana, 1), Card(Ana, 2));

        decision.Mark(Ana, anas.Items);
        decision.Choose(Ana, anas);
        decision.Mark(Ana, [Card(Ana, 2), Card(Ana, 1)]);
        Assert.Equal(anas, decision.VoteOf(Ana));

        decision.Mark(Ana, [Card(Ana, 1)]);
        Assert.Null(decision.VoteOf(Ana));

        // Ben confirming what Ana had decides nothing until she confirms it again.
        Assert.Empty(decision.Choose(Ben, Selection.Of(Card(Ben, 1), Card(Ben, 2))));
        decision.Mark(Ana, anas.Items);
        Assert.Equal([Ben, Ana], decision.Choose(Ana, anas).Select(commit => commit.Player));
        Assert.Empty(decision.MarksOf(Ana));
    }

    [Fact]
    public void Clearing_the_selection_takes_the_vote_back()
    {
        var decision = new CardChoiceDecision(new DeckIndexMatching(), LinkMode.Nice, pickCount: 2);
        decision.Join(Screen(Ana, 5));
        decision.Join(Screen(Ben, 5));
        decision.Choose(Ana, Selection.Of(Card(Ana, 1), Card(Ana, 2)));

        decision.Mark(Ana, []);

        Assert.Null(decision.VoteOf(Ana));
        Assert.Empty(decision.MarkersFor(Ben, Card(Ben, 1)));
    }
}
