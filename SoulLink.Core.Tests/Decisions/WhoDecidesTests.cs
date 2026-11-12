using SoulLink.Core.Decisions;
using static SoulLink.Core.Tests.TestOptions;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests.Decisions;

/// <summary>How a click decides a linked choice: the first click picks for everyone, or everyone has to agree.</summary>
public class WhoDecidesTests
{
    private static OptionDecision NewDecision(WhoDecides whoDecides, LinkMode mode = LinkMode.Nice, params (PlayerId Player, DecisionOption[] Options)[] players)
    {
        var decision = new OptionDecision(mode, 1, whoDecides);
        foreach (var (player, options) in players)
        {
            decision.Join(player, options);
        }

        return decision;
    }

    private static OptionDecision ThreeOfUs(WhoDecides whoDecides, LinkMode mode = LinkMode.Nice) =>
        NewDecision(whoDecides, mode, (Ana, CardReward(3)), (Ben, CardReward(3)), (Cal, CardReward(3)));

    private static IEnumerable<PlayerId> Players(IEnumerable<Commit<OptionKey>> commits) => commits.Select(commit => commit.Player);

    [Fact]
    public void First_click_picks_the_same_option_for_everyone_who_follows()
    {
        var decision = ThreeOfUs(WhoDecides.FirstClick);

        var commits = decision.Choose(Ben, Slot(2));

        Assert.Equal([Ben, Ana, Cal], Players(commits));
        Assert.Equal([CommitKind.Lead, CommitKind.Follow, CommitKind.Follow], commits.Select(commit => commit.Kind));
        Assert.True(decision.IsComplete);
    }

    [Fact]
    public void First_click_leaves_players_who_could_take_something_else_to_choose()
    {
        var decision = NewDecision(WhoDecides.FirstClick, LinkMode.Nice, (Ana, CardReward(3, Candy)), (Ben, CardReward(3)), (Cal, CardReward(2)));

        var commits = decision.Choose(Ben, Slot(3));

        // Ana could take her extra card instead, and Cal has no slot 3, so both still choose.
        Assert.Equal([Ben], Players(commits));
        Assert.Equal(ChoiceState.Bound, decision.StatusOf(Ana).State);
        Assert.Equal(ChoiceState.Free, decision.StatusOf(Cal).State);
    }

    [Fact]
    public void First_click_picks_for_held_players_once_they_can_act()
    {
        var decision = ThreeOfUs(WhoDecides.FirstClick);
        var held = true;
        decision.IsHeld = player => held && player == Cal;

        Assert.Equal([Ana, Ben], Players(decision.Choose(Ana, Slot(1))));

        held = false;
        Assert.Equal([Cal], Players(decision.Settle()));
    }

    [Fact]
    public void First_click_picks_for_a_late_player_when_settled()
    {
        var decision = NewDecision(WhoDecides.FirstClick, LinkMode.Nice, (Ana, CardReward(3)));
        decision.Choose(Ana, OptionKey.Skip);

        decision.Join(Ben, CardReward(3));

        Assert.Equal([Ben], Players(decision.Settle()));
        Assert.Equal(Selection.Of(OptionKey.Skip), decision.CommitOf(Ben)!.Choice);
    }

    [Fact]
    public void Nothing_is_picked_automatically_when_a_decision_does_not_follow_automatically()
    {
        var decision = new OptionDecision(LinkMode.Nice, 1, WhoDecides.FirstClick) { FollowsAutomatically = false };
        decision.Join(Ana, Slots(2));
        decision.Join(Ben, Slots(2));

        Assert.Equal([Ana], Players(decision.Choose(Ana, Slot(1))));
        Assert.Equal(ChoiceState.Bound, decision.StatusOf(Ben).State);
    }

    [Fact]
    public void Everyone_agrees_waits_until_everyone_who_can_take_it_has_voted()
    {
        var decision = ThreeOfUs(WhoDecides.EveryoneAgrees);

        Assert.Empty(decision.Choose(Cal, Slot(2)));
        Assert.Empty(decision.Choose(Ana, Slot(2)));
        Assert.Equal(Selection.Of(Slot(2)), decision.VoteOf(Ana));
        Assert.Equal([Cal, Ana], decision.VotersFor(Ben, Slot(2)));

        var commits = decision.Choose(Ben, Slot(2));

        // The first voter leads.
        Assert.Equal([Cal, Ana, Ben], Players(commits));
        Assert.Equal(CommitKind.Lead, commits[0].Kind);
        Assert.Empty(decision.Votes);
    }

    [Fact]
    public void Clicking_the_same_option_again_takes_the_vote_back()
    {
        var decision = ThreeOfUs(WhoDecides.EveryoneAgrees);
        decision.Choose(Ana, Slot(1));

        decision.Choose(Ana, Slot(1));

        Assert.Null(decision.VoteOf(Ana));
    }

    [Fact]
    public void A_new_vote_replaces_the_old_one()
    {
        var decision = ThreeOfUs(WhoDecides.EveryoneAgrees);
        decision.Choose(Ana, Slot(1));
        decision.Choose(Ben, Slot(3));
        decision.Choose(Cal, Slot(3));

        var commits = decision.Choose(Ana, Slot(3));

        Assert.Equal([Ben, Cal, Ana], Players(commits));
    }

    [Fact]
    public void Players_who_cannot_take_a_choice_do_not_block_it_in_nice_mode()
    {
        var decision = NewDecision(WhoDecides.EveryoneAgrees, LinkMode.Nice, (Ana, CardReward(3)), (Ben, CardReward(3)), (Cal, CardReward(2)));

        decision.Choose(Ana, Slot(3));
        var commits = decision.Choose(Ben, Slot(3));

        Assert.Equal([Ana, Ben], Players(commits));
        Assert.Equal(ChoiceState.Free, decision.StatusOf(Cal).State);
    }

    [Fact]
    public void An_extra_card_only_one_player_has_is_taken_at_once()
    {
        var decision = NewDecision(WhoDecides.EveryoneAgrees, LinkMode.Nice, (Ana, CardReward(3, Candy)), (Ben, CardReward(3)), (Cal, CardReward(3)));

        Assert.Equal([Ana], Players(decision.Choose(Ana, Candy)));

        decision.Choose(Ben, Slot(1));
        Assert.Equal([Ben, Cal], Players(decision.Choose(Cal, Slot(1))));
    }

    [Fact]
    public void An_extra_card_waits_for_the_other_holders_until_they_pick()
    {
        var decision = NewDecision(WhoDecides.EveryoneAgrees, LinkMode.Nice, (Ana, CardReward(3, Candy)), (Ben, CardReward(3, Candy)), (Cal, CardReward(3)));

        // Ben could take the extra card too, so Ana's vote waits for him.
        Assert.Empty(decision.Choose(Ana, Candy));
        Assert.Empty(decision.Choose(Ben, Slot(1)));

        // Ben and Cal agree on slot 1 without Ana, who wants her extra card instead, and then nothing holds her up.
        Assert.Equal([Ben, Cal, Ana], Players(decision.Choose(Cal, Slot(1))));
        Assert.Equal(Selection.Of(Candy), decision.CommitOf(Ana)!.Choice);
    }

    [Fact]
    public void Held_players_hold_up_an_agreement_until_they_vote()
    {
        var decision = ThreeOfUs(WhoDecides.EveryoneAgrees);
        var held = true;
        decision.IsHeld = player => held && player == Cal;

        decision.Choose(Ana, Slot(1));
        Assert.Empty(decision.Choose(Ben, Slot(1)));

        held = false;
        Assert.Empty(decision.Settle());
        Assert.Equal([Ana, Ben, Cal], Players(decision.Choose(Cal, Slot(1))));
    }

    [Fact]
    public void Prospects_do_not_hold_up_an_agreement()
    {
        var decision = ThreeOfUs(WhoDecides.EveryoneAgrees);
        decision.IsProspect = player => player == Cal;

        decision.Choose(Ana, Slot(1));

        Assert.Equal([Ana, Ben], Players(decision.Choose(Ben, Slot(1))));
        Assert.False(decision.HasDecided(Cal));
    }

    [Fact]
    public void Mean_mode_refuses_votes_for_greyed_out_options()
    {
        var decision = NewDecision(WhoDecides.EveryoneAgrees, LinkMode.Mean, (Ana, CardReward(3)), (Ben, CardReward(2)));

        Assert.Throws<InvalidOperationException>(() => decision.Choose(Ana, Slot(3)));
        Assert.Null(decision.VoteOf(Ana));
    }

    [Fact]
    public void Votes_that_stop_being_possible_are_dropped()
    {
        var decision = NewDecision(WhoDecides.EveryoneAgrees, LinkMode.Nice, (Ana, CardReward(3)), (Ben, CardReward(3)));
        decision.Choose(Ana, Slot(3));

        decision.UpdateOptions(Ana, CardReward(2));
        decision.Settle();

        Assert.Null(decision.VoteOf(Ana));
    }

    [Fact]
    public void A_client_replays_votes_and_the_hosts_commits()
    {
        var host = ThreeOfUs(WhoDecides.EveryoneAgrees);
        var client = ThreeOfUs(WhoDecides.EveryoneAgrees);

        foreach (var player in new[] { Ana, Ben, Cal })
        {
            var commits = host.Choose(player, Slot(2));
            client.ApplyChoice(player, Selection.Of(Slot(2)), commits);
        }

        Assert.Equal(host.Commits, client.Commits);
        Assert.Empty(client.Votes);
    }
}
