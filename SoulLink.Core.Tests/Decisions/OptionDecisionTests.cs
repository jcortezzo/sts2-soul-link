using SoulLink.Core.Decisions;
using static SoulLink.Core.Tests.TestOptions;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests.Decisions;

public class OptionDecisionTests
{
    private static OptionDecision ThreePlayers(LinkMode mode, int anaCards = 3, int benCards = 3, int calCards = 3)
    {
        var decision = new OptionDecision(mode);
        decision.Join(Ana, CardReward(anaCards));
        decision.Join(Ben, CardReward(benCards));
        decision.Join(Cal, CardReward(calCards));
        return decision;
    }

    [Fact]
    public void Everyone_is_free_before_anyone_commits()
    {
        var decision = ThreePlayers(LinkMode.Nice);

        Assert.All(decision.Participants, player => Assert.Equal(ChoiceState.Free, decision.StatusOf(player).State));
        Assert.True(decision.CanCommit(Ana, Slot(1)).IsAllowed);
        Assert.True(decision.CanCommit(Ben, OptionKey.Skip).IsAllowed);
    }

    [Fact]
    public void First_commit_binds_everyone_who_can_follow()
    {
        var decision = ThreePlayers(LinkMode.Nice);

        decision.Commit(Ben, Slot(2));

        var status = decision.StatusOf(Ana);
        Assert.Equal(ChoiceState.Bound, status.State);
        Assert.Equal(Selection.Of(Slot(2)), status.Binding!.Required);
        Assert.Equal(Ben, status.Binding.Source.Player);
        Assert.Equal(CommitCheck.MustFollow(Ben), decision.CanCommit(Ana, Slot(1)));
        Assert.Equal(CommitCheck.MustFollow(Ben), decision.CanCommit(Cal, OptionKey.Skip));
        Assert.True(decision.CanCommit(Cal, Slot(2)).IsAllowed);
    }

    [Fact]
    public void Following_records_a_follow_commit()
    {
        var decision = ThreePlayers(LinkMode.Nice);
        var lead = decision.Commit(Ben, Slot(2));

        var follow = decision.Commit(Cal, Slot(2));

        Assert.Equal(CommitKind.Lead, lead.Kind);
        Assert.Equal(CommitKind.Follow, follow.Kind);
        Assert.Equal(lead.Sequence, follow.Follows);
        Assert.Equal(lead.Sequence, follow.Root);
    }

    [Fact]
    public void A_player_who_cannot_follow_is_free()
    {
        var decision = ThreePlayers(LinkMode.Nice, calCards: 2);

        var lead = decision.Commit(Ana, Slot(3));

        Assert.Equal(ChoiceState.Bound, decision.StatusOf(Ben).State);
        Assert.Equal(ChoiceState.Free, decision.StatusOf(Cal).State);
        Assert.Equal([lead], decision.UnfollowableCommits(Cal));
        Assert.True(decision.CanCommit(Cal, Slot(1)).IsAllowed);
    }

    [Fact]
    public void The_first_free_player_binds_the_rest_who_can_follow_them()
    {
        var decision = new OptionDecision(LinkMode.Nice);
        decision.Join(Ana, CardReward(3));
        decision.Join(Ben, CardReward(2));
        decision.Join(Cal, CardReward(2));
        decision.Join(Dee, CardReward(3));

        decision.Commit(Ana, Slot(3));
        decision.Commit(Ben, Slot(1));

        Assert.Equal(Ana, decision.StatusOf(Dee).Binding!.Source.Player);
        Assert.Equal(Ben, decision.StatusOf(Cal).Binding!.Source.Player);
        Assert.Equal(Selection.Of(Slot(1)), decision.StatusOf(Cal).Binding!.Required);
    }

    [Fact]
    public void An_exclusive_extra_can_be_taken_instead_of_following()
    {
        var decision = new OptionDecision(LinkMode.Nice);
        decision.Join(Ana, CardReward(3, Candy));
        decision.Join(Ben, CardReward(3));
        decision.Commit(Ben, Slot(2));

        Assert.Equal(CommitCheck.MustFollow(Ben), decision.CanCommit(Ana, Slot(1)));
        Assert.True(decision.CanCommit(Ana, Candy).IsAllowed);
        Assert.Equal(CommitKind.Escape, decision.Commit(Ana, Candy).Kind);
    }

    [Fact]
    public void Taking_an_exclusive_extra_binds_other_holders_even_if_already_bound()
    {
        var decision = new OptionDecision(LinkMode.Nice);
        decision.Join(Ana, CardReward(3, Candy));
        decision.Join(Ben, CardReward(3));
        decision.Join(Cal, CardReward(3));
        decision.Join(Dee, CardReward(3, Candy));
        decision.Commit(Ben, Slot(2));

        decision.Commit(Ana, Candy);

        Assert.Equal(Selection.Of(Candy), decision.StatusOf(Dee).Binding!.Required);
        Assert.Equal(Ana, decision.StatusOf(Dee).Binding!.Source.Player);
        Assert.Equal(Selection.Of(Slot(2)), decision.StatusOf(Cal).Binding!.Required);
    }

    [Fact]
    public void An_extra_everyone_has_is_linked_like_any_other_option()
    {
        var decision = new OptionDecision(LinkMode.Nice);
        decision.Join(Ana, CardReward(3, Candy));
        decision.Join(Ben, CardReward(3, Candy));
        decision.Commit(Ben, Slot(2));

        Assert.Equal(CommitCheck.MustFollow(Ben), decision.CanCommit(Ana, Candy));
    }

    [Fact]
    public void Mean_greys_out_options_someone_cannot_take()
    {
        var decision = new OptionDecision(LinkMode.Mean);
        decision.Join(Ana, CardReward(3, Candy));
        decision.Join(Ben, CardReward(3));
        decision.Join(Cal, CardReward(2));

        Assert.Equal(LinkMode.Mean, decision.EffectiveMode);
        Assert.Equal(CommitCheck.GreyedOut(Cal), decision.CanCommit(Ana, Slot(3)));
        Assert.Equal(CommitCheck.GreyedOut(Ben), decision.CanCommit(Ana, Candy));
        Assert.True(decision.CanCommit(Ana, Slot(2)).IsAllowed);
    }

    [Fact]
    public void Mean_never_greys_out_a_separate_option()
    {
        var sacrifice = OptionKey.Separate("PAELS_WING");
        var decision = new OptionDecision(LinkMode.Mean);
        decision.Join(Ana, CardReward(3).Append(new DecisionOption(sacrifice)));
        decision.Join(Ben, CardReward(3));

        Assert.True(decision.CanCommit(Ana, sacrifice).IsAllowed);
        Assert.Contains(new OptionView<OptionKey>(sacrifice, OptionState.Open), decision.ViewOptions(Ana));

        decision.Commit(Ana, sacrifice);
        Assert.Equal(ChoiceState.Free, decision.StatusOf(Ben).State);
    }

    [Fact]
    public void Mean_first_commit_binds_everyone()
    {
        var decision = ThreePlayers(LinkMode.Mean);

        decision.Commit(Cal, Slot(1));

        Assert.Equal(CommitCheck.MustFollow(Cal), decision.CanCommit(Ana, Slot(2)));
        Assert.Equal(CommitCheck.MustFollow(Cal), decision.CanCommit(Ben, OptionKey.Skip));
    }

    [Fact]
    public void Mean_falls_back_to_nice_when_no_option_works_for_everyone()
    {
        var decision = new OptionDecision(LinkMode.Mean);
        decision.Join(Ana, [new DecisionOption(Slot(1)), new DecisionOption(Slot(2), Available: false)]);
        decision.Join(Ben, [new DecisionOption(Slot(1), Available: false), new DecisionOption(Slot(2))]);

        Assert.Equal(LinkMode.Nice, decision.EffectiveMode);
        decision.Commit(Ana, Slot(1));
        Assert.Equal(ChoiceState.Free, decision.StatusOf(Ben).State);
        Assert.True(decision.CanCommit(Ben, Slot(2)).IsAllowed);
    }

    [Fact]
    public void The_first_commit_fixes_the_mode()
    {
        var decision = ThreePlayers(LinkMode.Mean);
        var commit = decision.Commit(Ana, Slot(1));

        decision.UpdateOptions(Ben, [new DecisionOption(Slot(1), Available: false), new DecisionOption(OptionKey.Skip)]);

        Assert.Equal(LinkMode.Mean, commit.Mode);
        Assert.Equal(LinkMode.Mean, decision.EffectiveMode);
        Assert.Equal(ChoiceState.Free, decision.StatusOf(Ben).State);
        Assert.True(decision.CanCommit(Ben, OptionKey.Skip).IsAllowed);
    }

    [Fact]
    public void Multi_pick_screens_match_the_whole_set_in_any_order()
    {
        var decision = new OptionDecision(LinkMode.Nice, pickCount: 2);
        decision.Join(Ana, Slots(8));
        decision.Join(Ben, Slots(8));

        decision.Commit(Ana, Selection.Of(Slot(2), Slot(5)));

        Assert.Equal(CommitCheck.MustFollow(Ana), decision.CanCommit(Ben, Selection.Of(Slot(2), Slot(6))));
        Assert.Equal(CommitCheck.WrongPickCount, decision.CanCommit(Ben, Slot(2)));
        Assert.True(decision.CanCommit(Ben, Selection.Of(Slot(5), Slot(2))).IsAllowed);
    }

    [Fact]
    public void A_player_who_joins_late_is_bound_by_earlier_commits()
    {
        var decision = new OptionDecision(LinkMode.Nice);
        decision.Join(Ana, CardReward(3));
        decision.Join(Ben, CardReward(3));
        decision.Commit(Ana, Slot(1));

        decision.Join(Cal, CardReward(3));

        Assert.Equal(CommitCheck.MustFollow(Ana), decision.CanCommit(Cal, Slot(2)));
    }

    [Fact]
    public void Losing_the_required_option_frees_the_player()
    {
        var decision = ThreePlayers(LinkMode.Nice);
        decision.Commit(Ana, Slot(2));

        decision.UpdateOptions(Ben, [new DecisionOption(Slot(1)), new DecisionOption(Slot(2), Available: false)]);

        Assert.Equal(ChoiceState.Free, decision.StatusOf(Ben).State);
    }

    [Fact]
    public void A_leader_who_leaves_still_binds_the_others()
    {
        var decision = ThreePlayers(LinkMode.Nice);
        decision.Commit(Ana, Slot(2));

        decision.RemoveParticipant(Ana);

        Assert.False(decision.IsParticipant(Ana));
        Assert.Equal(CommitCheck.MustFollow(Ana), decision.CanCommit(Ben, Slot(1)));
    }

    [Fact]
    public void Commit_rejects_choices_that_are_not_allowed()
    {
        var decision = ThreePlayers(LinkMode.Nice);
        decision.Commit(Ana, Slot(2));

        Assert.Throws<InvalidOperationException>(() => decision.Commit(Ben, Slot(1)));
        Assert.Throws<InvalidOperationException>(() => decision.Commit(Ana, Slot(2)));
        Assert.Equal(CommitCheck.AlreadyDecided, decision.CanCommit(Ana, Slot(2)));
        Assert.Equal(CommitCheck.NotParticipant, decision.CanCommit(Dee, Slot(2)));
        Assert.Equal(CommitCheck.Unavailable, decision.CanCommit(Ben, Slot(9)));
    }

    [Fact]
    public void Clients_replay_host_commits_in_order()
    {
        var host = ThreePlayers(LinkMode.Nice);
        var client = ThreePlayers(LinkMode.Nice);
        var lead = host.Commit(Ana, Slot(2));
        var follow = host.Commit(Ben, Slot(2));

        Assert.Throws<InvalidOperationException>(() => client.Apply(follow));
        client.Apply(lead);
        client.Apply(follow);

        Assert.Equal(host.StatusOf(Cal), client.StatusOf(Cal));
        Assert.Equal(host.Commits, client.Commits);
    }

    [Fact]
    public void Option_views_show_what_each_player_may_pick()
    {
        var decision = new OptionDecision(LinkMode.Nice);
        decision.Join(Ana, CardReward(2, Candy));
        decision.Join(Ben, CardReward(3));
        decision.Commit(Ben, Slot(2));

        Assert.Equal(
            [
                new OptionView<OptionKey>(Slot(1), OptionState.Locked, Ben),
                new OptionView<OptionKey>(Slot(2), OptionState.Required, Ben),
                new OptionView<OptionKey>(Candy, OptionState.Escape, Ben),
                new OptionView<OptionKey>(OptionKey.Skip, OptionState.Locked, Ben),
            ],
            decision.ViewOptions(Ana));
        Assert.Contains(new OptionView<OptionKey>(Slot(2), OptionState.Chosen), decision.ViewOptions(Ben));
    }

    [Fact]
    public void Option_views_grey_out_in_mean_mode()
    {
        var decision = new OptionDecision(LinkMode.Mean);
        decision.Join(Ana, CardReward(3));
        decision.Join(Ben, CardReward(2));

        var views = decision.ViewOptions(Ana);

        Assert.Equal(new OptionView<OptionKey>(Slot(3), OptionState.GreyedOut, Ben), views[2]);
        Assert.Equal(new OptionView<OptionKey>(Slot(1), OptionState.Open), views[0]);
    }

    [Fact]
    public void Peek_shows_every_players_option_in_a_slot()
    {
        var decision = ThreePlayers(LinkMode.Nice, calCards: 2);

        var peek = decision.Peek(Slot(3));

        Assert.Equal(new DecisionOption(Slot(3)), peek.Single(entry => entry.Key == Ben).Value);
        Assert.Null(peek.Single(entry => entry.Key == Cal).Value);
    }

    [Fact]
    public void Each_option_key_can_appear_once_per_player()
    {
        var decision = new OptionDecision(LinkMode.Nice);

        Assert.Throws<ArgumentException>(() => decision.Join(Ana, [new DecisionOption(Slot(1)), new DecisionOption(Slot(1))]));
    }
}
