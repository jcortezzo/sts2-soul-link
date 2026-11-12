using SoulLink.Core.Decisions;
using SoulLink.Core.Rewards;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests.Rewards;

public class CardRewardRoundTests
{
    private static readonly CardRewardOffer Three = new(3);
    private static readonly CardRewardOffer ThreeWithDriftwood = new(3, CanReroll: true);
    private static readonly CardRewardOffer ThreeWithPaelsWing = new(3, CanSacrifice: true);

    [Fact]
    public void Extra_screens_are_linked_among_the_players_who_have_them()
    {
        var round = new CardRewardRound(LinkMode.Nice);
        round.Join(Ana, [Three, Three]);
        round.Join(Ben, [Three]);
        round.Join(Cal, [Three, Three]);

        Assert.Equal([Ana, Ben, Cal], round.Screen(1).Participants);
        Assert.Equal([Ana, Cal], round.Screen(2).Participants);
    }

    [Theory]
    [InlineData(LinkMode.Nice)]
    [InlineData(LinkMode.Mean)]
    public void A_screen_only_one_player_has_is_free(LinkMode mode)
    {
        var round = new CardRewardRound(mode);
        round.Join(Ana, [Three, new CardRewardOffer(3, ["LASTING_CANDY"])]);
        round.Join(Ben, [Three]);

        var screen = round.Screen(2);

        Assert.All(
            screen.Decision.OptionsOf(Ana),
            option => Assert.True(screen.CanCommit(Ana, option.Key).IsAllowed));
    }

    [Fact]
    public void Paels_Wing_sacrifices_bind_only_other_holders()
    {
        var round = new CardRewardRound(LinkMode.Nice);
        round.Join(Ana, [ThreeWithPaelsWing]);
        round.Join(Ben, [ThreeWithPaelsWing]);
        round.Join(Cal, [Three]);
        var screen = round.Screen(1);

        screen.Commit(Ana, CardRewardKeys.Sacrifice);

        Assert.Equal(CommitCheck.MustFollow(Ana), screen.CanCommit(Ben, CardRewardKeys.Card(1)));
        Assert.True(screen.CanCommit(Ben, CardRewardKeys.Sacrifice).IsAllowed);
        Assert.Equal(ChoiceState.Free, screen.Decision.StatusOf(Cal).State);
        Assert.True(screen.CanCommit(Cal, CardRewardKeys.Card(3)).IsAllowed);
    }

    [Theory]
    [InlineData(LinkMode.Nice)]
    [InlineData(LinkMode.Mean)]
    public void A_holder_can_sacrifice_instead_of_following_a_card_pick(LinkMode mode)
    {
        var round = new CardRewardRound(mode);
        round.Join(Ana, [ThreeWithPaelsWing]);
        round.Join(Ben, [ThreeWithPaelsWing]);
        round.Join(Cal, [Three]);
        var screen = round.Screen(1);
        screen.Commit(Cal, CardRewardKeys.Card(2));

        Assert.True(screen.CanCommit(Ana, CardRewardKeys.Sacrifice).IsAllowed);
        Assert.Equal(CommitKind.Escape, screen.Commit(Ana, CardRewardKeys.Sacrifice).Kind);
        Assert.Equal(CommitCheck.MustFollow(Ana), screen.CanCommit(Ben, CardRewardKeys.Card(2)));
    }

    [Fact]
    public void Other_Driftwood_holders_reroll_before_picking()
    {
        var round = new CardRewardRound(LinkMode.Nice);
        round.Join(Ana, [ThreeWithDriftwood]);
        round.Join(Ben, [Three]);
        round.Join(Cal, [ThreeWithDriftwood]);
        var screen = round.Screen(1);

        var owing = screen.Reroll(Ana, Three);

        Assert.Equal([Cal], owing);
        Assert.Equal(CommitCheck.MustReroll(Ana), screen.CanCommit(Cal, CardRewardKeys.Card(1)));
        Assert.True(screen.CanCommit(Ben, CardRewardKeys.Card(1)).IsAllowed);

        Assert.Empty(screen.Reroll(Cal, Three));
        Assert.Equal(0, screen.RerollsOwed(Cal));
        Assert.Equal(0, screen.RerollsOwed(Ana));
        Assert.True(screen.CanCommit(Cal, CardRewardKeys.Card(1)).IsAllowed);
    }

    [Fact]
    public void A_reroll_is_not_a_commit()
    {
        var round = new CardRewardRound(LinkMode.Nice);
        round.Join(Ana, [ThreeWithDriftwood]);
        round.Join(Ben, [Three]);
        round.Join(Cal, [ThreeWithDriftwood]);
        var screen = round.Screen(1);
        screen.Commit(Ben, CardRewardKeys.Card(2));

        screen.Reroll(Ana, Three);

        Assert.Equal(CommitCheck.MustFollow(Ben), screen.CanCommit(Ana, CardRewardKeys.Card(1)));
        Assert.True(screen.CanCommit(Ana, CardRewardKeys.Card(2)).IsAllowed);
    }

    [Fact]
    public void Players_who_picked_or_cannot_reroll_owe_nothing()
    {
        var round = new CardRewardRound(LinkMode.Nice);
        round.Join(Ana, [ThreeWithDriftwood]);
        round.Join(Ben, [ThreeWithDriftwood]);
        round.Join(Cal, [Three]);
        var screen = round.Screen(1);
        screen.Commit(Ben, CardRewardKeys.Card(1));
        screen.Commit(Ana, CardRewardKeys.Card(1));

        Assert.Throws<InvalidOperationException>(() => screen.Reroll(Ana, Three));
        Assert.Throws<InvalidOperationException>(() => screen.Reroll(Cal, Three));
        Assert.Equal(0, screen.RerollsOwed(Ben));
    }

    [Fact]
    public void Screen_numbers_start_at_one()
    {
        var round = new CardRewardRound(LinkMode.Nice);
        round.Join(Ana, [Three]);

        Assert.Equal(1, round.Screen(1).Number);
        Assert.Throws<ArgumentOutOfRangeException>(() => round.Screen(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => round.Screen(0));
    }
}
