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

    private static CampfireSession NewCampfire(LinkMode mode) => new(mode, new DeckIndexMatching());

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
        var campfire = new CampfireSession(LinkMode.Mean, new LinkedCardsMatching(links));
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
    [InlineData("REST", CampfireCategory.Recover)]
    [InlineData("mend", CampfireCategory.Recover)]
    [InlineData("SMITH", CampfireCategory.Improve)]
    [InlineData("LIFT", CampfireCategory.Improve)]
    [InlineData("SOMETHING_A_RELIC_ADDS", CampfireCategory.Improve)]
    public void Options_belong_to_categories(string optionId, CampfireCategory expected)
    {
        Assert.Equal(expected, CategoryOf(optionId));
    }
}
