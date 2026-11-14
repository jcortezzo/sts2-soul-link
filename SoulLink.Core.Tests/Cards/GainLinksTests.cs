using SoulLink.Core.Cards;
using static SoulLink.Core.Tests.TestCards;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests.Cards;

public class GainLinksTests
{
    private static readonly string SlotTwo = GainLinks.SourceKey("f2/rewards-1/card-1", "slot:2", root: 1);

    [Fact]
    public void Cards_from_the_same_source_link_as_players_report_them()
    {
        var links = new LinkRegistry();
        var gains = new GainLinks(links);

        Assert.Empty(gains.Gain(SlotTwo, Ana, [Card(Ana, 20)]));
        Assert.Null(links.GroupOf(Ref(Ana, 20)));

        var formed = Assert.Single(gains.Gain(SlotTwo, Ben, [Card(Ben, 20)]));
        Assert.Equal(formed, links.GroupOf(Ref(Ana, 20)));

        gains.Gain(SlotTwo, Cal, [Card(Cal, 20)]);
        Assert.Equal([Ref(Ana, 20), Ref(Ben, 20), Ref(Cal, 20)], links.MembersOf(formed));
    }

    [Fact]
    public void Different_leads_or_options_make_different_groups()
    {
        var links = new LinkRegistry();
        var gains = new GainLinks(links);

        gains.Gain(SlotTwo, Ana, [Card(Ana, 20)]);
        gains.Gain(GainLinks.SourceKey("f2/rewards-1/card-1", "slot:2", root: 2), Ben, [Card(Ben, 20)]);
        gains.Gain(GainLinks.SourceKey("f2/rewards-1/card-1", "slot:1", root: 1), Cal, [Card(Cal, 20)]);

        Assert.Empty(links.Groups);
    }

    [Fact]
    public void Several_cards_from_one_source_link_in_order_even_when_reported_one_by_one()
    {
        var links = new LinkRegistry();
        var gains = new GainLinks(links);
        var cheese = GainLinks.SourceKey("f5/event/INITIAL/1", "slot:1", root: 1);

        gains.Gain(cheese, Ana, [Card(Ana, 21)]);
        gains.Gain(cheese, Ana, [Card(Ana, 22)]);
        gains.Gain(cheese, Ben, [Card(Ben, 21), Card(Ben, 22)]);

        Assert.Equal(Ref(Ben, 21), links.LinkedCard(Ref(Ana, 21), Ben));
        Assert.Equal(Ref(Ben, 22), links.LinkedCard(Ref(Ana, 22), Ben));
    }

    [Fact]
    public void A_card_that_is_already_linked_is_left_alone()
    {
        var links = new LinkRegistry();
        links.Link([Ref(Ana, 1), Ref(Ben, 1)]);
        var gains = new GainLinks(links);

        gains.Gain(SlotTwo, Ana, [Card(Ana, 1)]);
        gains.Gain(SlotTwo, Cal, [Card(Cal, 20)]);

        Assert.Equal(2, links.MembersOf(links.GroupOf(Ref(Ana, 1))!.Value).Count);
        Assert.Null(links.GroupOf(Ref(Cal, 20)));
    }
}
