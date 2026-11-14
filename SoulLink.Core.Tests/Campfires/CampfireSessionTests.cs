using SoulLink.Core.Campfires;
using SoulLink.Core.Cards;
using SoulLink.Core.Decisions;
using static SoulLink.Core.Campfires.CampfireOptions;
using static SoulLink.Core.Tests.TestCards;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests.Campfires;

public class CampfireSessionTests
{
    private static readonly CampfireOption[] Usual = [new(Rest), new(Smith), new(Dig)];

    private static CampfireSession NewCampfire(LinkMode mode, WhoDecides whoDecides = WhoDecides.FirstClick) =>
        new(mode, new DeckIndexMatching(), whoDecides);

    [Fact]
    public void The_first_choice_sets_the_category()
    {
        var campfire = NewCampfire(LinkMode.Nice);
        campfire.Join(Ben, Usual, hasMiniatureTent: false);
        campfire.Join(Cal, Usual, hasMiniatureTent: false);

        campfire.Choose(Ben, Smith);

        Assert.Equal(CommitCheck.MustFollow(Ben), campfire.CanChoose(Cal, Rest));
        Assert.True(campfire.CanChoose(Cal, Dig).IsAllowed);
        Assert.True(campfire.CanChoose(Cal, Smith).IsAllowed);
    }

    [Fact]
    public void Miniature_Tent_holders_can_do_everything_and_set_nothing()
    {
        var campfire = NewCampfire(LinkMode.Nice);
        campfire.Join(Ana, Usual, hasMiniatureTent: true);
        campfire.Join(Ben, Usual, hasMiniatureTent: false);

        campfire.Choose(Ana, Rest);
        campfire.Choose(Ana, Dig);

        Assert.Equal([Rest, Dig], campfire.ChosenBy(Ana));
        Assert.Equal(CommitCheck.AlreadyDecided, campfire.CanChoose(Ana, Rest));
        Assert.True(campfire.CanChoose(Ben, Smith).IsAllowed);
        Assert.DoesNotContain(Ana, campfire.Category.Participants);
    }

    [Fact]
    public void A_player_who_cannot_do_the_category_chooses_freely_in_nice_mode()
    {
        var campfire = NewCampfire(LinkMode.Nice);
        campfire.Join(Ben, Usual, hasMiniatureTent: false);
        campfire.Join(Cal, [new(Rest), new(Smith, Available: false)], hasMiniatureTent: false);

        campfire.Choose(Ben, Dig);

        Assert.True(campfire.CanChoose(Cal, Rest).IsAllowed);
    }

    [Fact]
    public void Mean_greys_out_a_category_someone_cannot_do()
    {
        var campfire = NewCampfire(LinkMode.Mean);
        campfire.Join(Ana, Usual, hasMiniatureTent: false);
        campfire.Join(Cal, [new(Rest, Available: false), new(Smith)], hasMiniatureTent: false);

        Assert.Equal(CommitCheck.GreyedOut(Cal), campfire.CanChoose(Ana, Rest));
        Assert.True(campfire.CanChoose(Ana, Dig).IsAllowed);
    }

    [Fact]
    public void Each_player_without_a_tent_chooses_once()
    {
        var campfire = NewCampfire(LinkMode.Nice);
        campfire.Join(Ben, Usual, hasMiniatureTent: false);
        campfire.Choose(Ben, Rest);

        Assert.Equal(CommitCheck.AlreadyDecided, campfire.CanChoose(Ben, Dig));
        Assert.Throws<InvalidOperationException>(() => campfire.Choose(Ben, Dig));
    }

    [Fact]
    public void Everyone_who_smiths_follows_the_first_smith_target()
    {
        var campfire = NewCampfire(LinkMode.Nice);
        campfire.Join(Ana, Usual, hasMiniatureTent: true, Screen(Ana, 10));
        campfire.Join(Ben, Usual, hasMiniatureTent: false, Screen(Ben, 10));
        campfire.Join(Cal, Usual, hasMiniatureTent: false, Screen(Cal, 10));

        campfire.Choose(Ben, Smith);
        campfire.SmithTarget.Commit(Ben, Card(Ben, 6));

        Assert.Equal(Selection.Of(Card(Ana, 6)), campfire.SmithTarget.StatusOf(Ana).Binding!.Required);
        Assert.Equal(Selection.Of(Card(Cal, 6)), campfire.SmithTarget.StatusOf(Cal).Binding!.Required);
    }

    [Fact]
    public void Mean_smith_targets_must_work_for_everyone_who_could_smith()
    {
        var links = new LinkRegistry();
        links.Link([Ref(Ben, 1), Ref(Cal, 1)]);
        links.Link([Ref(Ben, 2), Ref(Cal, 2)]);
        var campfire = new CampfireSession(LinkMode.Mean, new LinkedCardsMatching(links), WhoDecides.FirstClick);
        campfire.Join(Ben, Usual, hasMiniatureTent: false, Screen(Ben, 2));
        campfire.Join(Cal, Usual, hasMiniatureTent: false, SelectionScreen.Of(Cal, [Card(Cal, 2)]));

        campfire.Choose(Ben, Smith);

        Assert.Equal(CommitCheck.GreyedOut(Cal), campfire.SmithTarget.CanCommit(Ben, Card(Ben, 1)));

        campfire.Choose(Cal, Dig);

        Assert.DoesNotContain(Cal, campfire.SmithTarget.Participants);
        Assert.True(campfire.SmithTarget.CanCommit(Ben, Card(Ben, 1)).IsAllowed);
    }

    [Fact]
    public void Option_views_follow_the_category()
    {
        var campfire = NewCampfire(LinkMode.Nice);
        campfire.Join(Ben, Usual, hasMiniatureTent: false);
        campfire.Join(Cal, [new(Rest), new(Smith), new(Dig, Available: false)], hasMiniatureTent: false);

        campfire.Choose(Ben, Smith);

        Assert.Equal(
            [
                new OptionView<string>(Rest, OptionState.Locked, Ben),
                new OptionView<string>(Smith, OptionState.Open),
                new OptionView<string>(Dig, OptionState.Unavailable),
            ],
            campfire.ViewOptions(Cal));
        Assert.Equal(OptionState.Chosen, campfire.ViewOptions(Ben)[1].State);
        Assert.Equal(OptionState.NotChosen, campfire.ViewOptions(Ben)[0].State);
    }

    [Theory]
    [InlineData("HEAL", CampfireCategory.Recover)]
    [InlineData("REST", CampfireCategory.Recover)]
    [InlineData("mend", CampfireCategory.Recover)]
    [InlineData("RESTORE", CampfireCategory.Recover)]
    [InlineData("EVOLVE", CampfireCategory.Improve)]
    [InlineData("DIG", CampfireCategory.Improve)]
    [InlineData("CLONE", CampfireCategory.Improve)]
    [InlineData("SMITH", CampfireCategory.Improve)]
    [InlineData("LIFT", CampfireCategory.Improve)]
    [InlineData("SOMETHING_A_RELIC_ADDS", CampfireCategory.Improve)]
    public void Options_belong_to_categories(string optionId, CampfireCategory expected)
    {
        Assert.Equal(expected, CategoryOf(optionId));
    }

    [Fact]
    public void Campers_are_listed_in_the_order_they_arrive()
    {
        var campfire = NewCampfire(LinkMode.Nice, WhoDecides.EveryoneAgrees);
        campfire.Join(Cal, [new(Rest)], hasMiniatureTent: false);
        campfire.Join(Ana, [new(Rest)], hasMiniatureTent: true);

        Assert.Equal([Cal, Ana], campfire.Campers);
        Assert.True(campfire.IsAt(Ana));
        Assert.False(campfire.IsAt(Ben));
    }

    [Fact]
    public void Everyone_agrees_does_everyones_option_once_they_pick_the_same_category()
    {
        var campfire = NewCampfire(LinkMode.Nice, WhoDecides.EveryoneAgrees);
        campfire.Join(Ben, Usual, hasMiniatureTent: false);
        campfire.Join(Cal, Usual, hasMiniatureTent: false);

        Assert.Empty(campfire.Choose(Ben, Smith));
        Assert.Empty(campfire.Choose(Cal, Rest));
        Assert.Equal(Smith, campfire.PendingOf(Ben));
        Assert.Empty(campfire.ChosenBy(Ben));

        Assert.Equal([new CampfireChoice(Ben, Smith), new CampfireChoice(Cal, Dig)], campfire.Choose(Cal, Dig));
        Assert.Null(campfire.PendingOf(Cal));
    }

    [Fact]
    public void Clicking_the_same_option_again_takes_it_back()
    {
        var campfire = NewCampfire(LinkMode.Nice, WhoDecides.EveryoneAgrees);
        campfire.Join(Ben, Usual, hasMiniatureTent: false);
        campfire.Join(Cal, Usual, hasMiniatureTent: false);
        campfire.Choose(Ben, Rest);

        campfire.Choose(Ben, Rest);

        Assert.Null(campfire.PendingOf(Ben));
        Assert.Null(campfire.Category.VoteOf(Ben));
        Assert.Empty(campfire.Choose(Cal, Rest));
    }

    [Fact]
    public void Switching_options_within_a_category_keeps_the_vote()
    {
        var campfire = NewCampfire(LinkMode.Nice, WhoDecides.EveryoneAgrees);
        campfire.Join(Ben, Usual, hasMiniatureTent: false);
        campfire.Join(Cal, Usual, hasMiniatureTent: false);
        campfire.Choose(Ben, Smith);
        campfire.Choose(Ben, Dig);

        Assert.Equal(Dig, campfire.PendingOf(Ben));
        Assert.Equal([new CampfireChoice(Ben, Dig), new CampfireChoice(Cal, Smith)], campfire.Choose(Cal, Smith));
    }

    [Fact]
    public void Smith_targets_need_Smith_first()
    {
        var campfire = NewCampfire(LinkMode.Nice, WhoDecides.EveryoneAgrees);
        campfire.Join(Ana, Usual, hasMiniatureTent: true, Screen(Ana, 10));

        Assert.Equal(CommitCheck.Unavailable, campfire.CanChooseTarget(Ana, Card(Ana, 3)));
        campfire.Choose(Ana, Smith);
        Assert.Equal([Ana], campfire.ChooseTarget(Ana, Card(Ana, 3)).Select(commit => commit.Player));
    }

    [Fact]
    public void Smiths_agree_on_targets_without_waiting_for_players_who_could_smith_later()
    {
        var campfire = NewCampfire(LinkMode.Nice, WhoDecides.EveryoneAgrees);
        campfire.Join(Ana, Usual, hasMiniatureTent: true, Screen(Ana, 10));
        campfire.Join(Ben, Usual, hasMiniatureTent: false, Screen(Ben, 10));
        campfire.Join(Cal, Usual, hasMiniatureTent: false, Screen(Cal, 10));
        campfire.Choose(Ben, Smith);
        campfire.Choose(Cal, Smith);

        Assert.Empty(campfire.ChooseTarget(Ben, Card(Ben, 4)));
        var targets = campfire.ChooseTarget(Cal, Card(Cal, 4));

        // Ana has Miniature Tent and hasn't chosen Smith, so the others don't wait for her.
        Assert.Equal([Ben, Cal], targets.Select(commit => commit.Player));
    }

    [Fact]
    public void First_click_picks_the_smith_target_for_players_who_smith_later()
    {
        var campfire = NewCampfire(LinkMode.Nice);
        campfire.Join(Ben, Usual, hasMiniatureTent: false, Screen(Ben, 10));
        campfire.Join(Cal, Usual, hasMiniatureTent: false, Screen(Cal, 10));
        campfire.Choose(Ben, Smith);
        Assert.Equal([Ben], campfire.ChooseTarget(Ben, Card(Ben, 4)).Select(commit => commit.Player));

        campfire.Choose(Cal, Smith);

        Assert.Equal(Selection.Of(Card(Cal, 4)), campfire.SmithTarget.CommitOf(Cal)!.Choice);
    }

    [Fact]
    public void A_client_replays_campfire_clicks_with_the_hosts_commits()
    {
        var host = NewCampfire(LinkMode.Nice, WhoDecides.EveryoneAgrees);
        var client = NewCampfire(LinkMode.Nice, WhoDecides.EveryoneAgrees);
        foreach (var campfire in new[] { host, client })
        {
            campfire.Join(Ben, Usual, hasMiniatureTent: false, Screen(Ben, 10));
            campfire.Join(Cal, Usual, hasMiniatureTent: false, Screen(Cal, 10));
        }

        foreach (var (player, option) in new[] { (Ben, Smith), (Cal, Rest), (Cal, Dig) })
        {
            var categories = host.Category.Commits.Count;
            var targets = host.SmithTarget.Commits.Count;
            var done = host.Choose(player, option);
            var replayed = client.ApplyChoose(
                player, option, host.Category.Commits.Skip(categories).ToList(), host.SmithTarget.Commits.Skip(targets).ToList());
            Assert.Equal(done, replayed);
        }

        Assert.Equal(host.ChosenBy(Cal), client.ChosenBy(Cal));
        Assert.Equal(host.Category.Commits, client.Category.Commits);
        Assert.Equal(host.SmithTarget.Participants, client.SmithTarget.Participants);
    }
}
