using SoulLink.Core.Cards;
using static SoulLink.Core.Tests.TestCards;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests.Cards;

public class LinkRegistryTests
{
    [Fact]
    public void Cards_from_the_same_choice_link_together()
    {
        var links = new LinkRegistry();

        var group = links.Link([Ref(Ana, 1), Ref(Ben, 1), Ref(Cal, 1)]);

        Assert.NotNull(group);
        Assert.Equal(group, links.GroupOf(Ref(Ben, 1)));
        Assert.Equal(Ref(Cal, 1), links.LinkedCard(Ref(Ana, 1), Cal));
        Assert.Null(links.LinkedCard(Ref(Ana, 1), Dee));
    }

    [Fact]
    public void A_solo_card_stays_unlinked()
    {
        var links = new LinkRegistry();

        Assert.Null(links.Link([Ref(Ana, 1)]));
        Assert.Null(links.GroupOf(Ref(Ana, 1)));
        Assert.Empty(links.Groups);
    }

    [Fact]
    public void A_group_holds_at_most_one_card_per_player()
    {
        var links = new LinkRegistry();

        Assert.Throws<ArgumentException>(() => links.Link([Ref(Ana, 1), Ref(Ana, 2)]));
    }

    [Fact]
    public void A_card_is_in_at_most_one_group()
    {
        var links = new LinkRegistry();
        links.Link([Ref(Ana, 1), Ref(Ben, 1)]);

        Assert.Throws<ArgumentException>(() => links.Link([Ref(Ana, 1), Ref(Cal, 1)]));
    }

    [Fact]
    public void Transforming_keeps_the_link()
    {
        var links = new LinkRegistry();
        var group = links.Link([Ref(Ana, 1), Ref(Ben, 1)]);

        links.Replace(Ref(Ana, 1), Ref(Ana, 9));

        Assert.Null(links.GroupOf(Ref(Ana, 1)));
        Assert.Equal(group, links.GroupOf(Ref(Ana, 9)));
        Assert.Equal(Ref(Ana, 9), links.LinkedCard(Ref(Ben, 1), Ana));
    }

    [Fact]
    public void Removing_a_card_leaves_the_rest_of_its_group()
    {
        var links = new LinkRegistry();
        var group = links.Link([Ref(Ana, 1), Ref(Ben, 1)])!.Value;

        links.Remove(Ref(Ana, 1));

        Assert.Null(links.GroupOf(Ref(Ana, 1)));
        Assert.Equal(Ref(Ben, 1), links.MemberFor(group, Ben));
        Assert.Null(links.LinkedCard(Ref(Ben, 1), Ana));

        links.Remove(Ref(Ben, 1));
        Assert.Empty(links.Groups);
    }

    [Fact]
    public void Restoring_a_snapshot_keeps_group_ids()
    {
        var links = new LinkRegistry();
        links.Link([Ref(Ana, 1), Ref(Ben, 1)]);
        var second = links.Link([Ref(Ana, 2), Ref(Ben, 2)]);
        links.Remove(Ref(Ana, 1));
        links.Remove(Ref(Ben, 1));

        var restored = LinkRegistry.Restore(links.Snapshot());

        Assert.Equal(second, restored.GroupOf(Ref(Ben, 2)));
        Assert.Equal(new LinkGroupId(3), restored.Link([Ref(Ana, 3), Ref(Ben, 3)]));
    }

    [Fact]
    public void A_card_can_join_an_existing_group_once_per_player()
    {
        var links = new LinkRegistry();
        var group = links.Link([Ref(Ana, 1), Ref(Ben, 1)])!.Value;

        Assert.True(links.Add(group, Ref(Cal, 1)));
        Assert.False(links.Add(group, Ref(Cal, 2)));
        Assert.False(links.Add(new LinkGroupId(99), Ref(Dee, 1)));
        Assert.Throws<ArgumentException>(() => links.Add(group, Ref(Ana, 1)));
        Assert.Equal(group, links.GroupOf(Ref(Cal, 1)));
    }
}
