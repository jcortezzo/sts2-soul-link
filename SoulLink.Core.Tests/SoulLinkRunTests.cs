using SoulLink.Core.Cards;
using SoulLink.Core.Decisions;
using SoulLink.Core.Shops;
using static SoulLink.Core.Tests.TestCards;
using static SoulLink.Core.Tests.TestOptions;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests;

public class SoulLinkRunTests
{
    [Theory]
    [InlineData(CardMatching.LinkedCards)]
    [InlineData(CardMatching.DeckIndex)]
    public void Decisions_use_the_run_settings(CardMatching matching)
    {
        var run = new SoulLinkRun(new SoulLinkSettings(matching, LinkMode.Mean));

        Assert.Equal(matching, run.Matching.Kind);
        Assert.Equal(LinkMode.Mean, run.NewOptionDecision().Mode);
        Assert.Equal(LinkMode.Mean, run.NewCardChoice().Mode);
        Assert.Equal(LinkMode.Mean, run.NewShop().Mode);
        Assert.Equal(2, run.NewOptionDecision(pickCount: 2).PickCount);
    }

    [Theory]
    [InlineData(WhoDecides.EveryoneAgrees)]
    [InlineData(WhoDecides.FirstClick)]
    public void Decisions_use_the_who_decides_setting(WhoDecides whoDecides)
    {
        var run = new SoulLinkRun(SoulLinkSettings.Default with { WhoDecides = whoDecides });

        Assert.Equal(whoDecides, run.NewOptionDecision().WhoDecides);
        Assert.Equal(whoDecides, run.NewCardChoice().WhoDecides);
        Assert.Equal(whoDecides, run.NewCardRewardRound().WhoDecides);
        Assert.Equal(whoDecides, run.NewShop().WhoDecides);
        Assert.Equal(whoDecides, run.NewCampfire().WhoDecides);
        Assert.Equal(whoDecides, run.NewCampfire().SmithTarget.WhoDecides);
    }

    [Fact]
    public void Everyone_agrees_is_the_default()
    {
        Assert.Equal(WhoDecides.EveryoneAgrees, SoulLinkSettings.Default.WhoDecides);
    }

    [Fact]
    public void Starter_decks_are_linked_at_the_start()
    {
        var run = new SoulLinkRun(SoulLinkSettings.Default);

        run.LinkStarterDecks(
        [
            new StarterDeck(Ana, [new StarterCard(Card(Ana, 1), "Strike")]),
            new StarterDeck(Ben, [new StarterCard(Card(Ben, 1), "Strike")]),
        ]);

        Assert.Equal(Ref(Ben, 1), run.Links.LinkedCard(Ref(Ana, 1), Ben));
    }

    [Fact]
    public void Cards_from_the_same_slot_and_lead_are_linked()
    {
        var run = new SoulLinkRun(SoulLinkSettings.Default);
        var decision = run.NewOptionDecision();
        decision.Join(Ana, CardReward(3, Candy));
        decision.Join(Ben, CardReward(3));
        decision.Join(Cal, CardReward(2));
        decision.Join(Dee, CardReward(2));
        decision.Commit(Ana, Slot(3));
        decision.Commit(Ben, Slot(3));
        decision.Commit(Cal, Slot(1));
        decision.Commit(Dee, Slot(1));

        var groups = run.LinkGainedCards(decision,
        [
            new GainedCard(Ana, Slot(3), Card(Ana, 20)),
            new GainedCard(Ben, Slot(3), Card(Ben, 20)),
            new GainedCard(Cal, Slot(1), Card(Cal, 20)),
            new GainedCard(Dee, Slot(1), Card(Dee, 20)),
        ]);

        Assert.Equal(2, groups.Count);
        Assert.Equal(Ref(Ben, 20), run.Links.LinkedCard(Ref(Ana, 20), Ben));
        Assert.Equal(Ref(Dee, 20), run.Links.LinkedCard(Ref(Cal, 20), Dee));
        Assert.Null(run.Links.LinkedCard(Ref(Ana, 20), Cal));
    }

    [Fact]
    public void Multi_pick_cards_link_slot_by_slot()
    {
        var run = new SoulLinkRun(SoulLinkSettings.Default);
        var cheese = run.NewOptionDecision(pickCount: 2);
        cheese.Join(Ana, Slots(8));
        cheese.Join(Ben, Slots(8));
        cheese.Commit(Ana, Selection.Of(Slot(2), Slot(5)));
        cheese.Commit(Ben, Selection.Of(Slot(2), Slot(5)));

        run.LinkGainedCards(cheese,
        [
            new GainedCard(Ana, Slot(2), Card(Ana, 30)),
            new GainedCard(Ana, Slot(5), Card(Ana, 31)),
            new GainedCard(Ben, Slot(5), Card(Ben, 31)),
            new GainedCard(Ben, Slot(2), Card(Ben, 30)),
        ]);

        Assert.Equal(Ref(Ben, 30), run.Links.LinkedCard(Ref(Ana, 30), Ben));
        Assert.Equal(Ref(Ben, 31), run.Links.LinkedCard(Ref(Ana, 31), Ben));
    }

    [Fact]
    public void A_card_only_one_player_gained_stays_unlinked()
    {
        var run = new SoulLinkRun(SoulLinkSettings.Default);
        var decision = run.NewOptionDecision();
        decision.Join(Ana, CardReward(3, Candy));
        decision.Join(Ben, CardReward(3));
        decision.Commit(Ana, Candy);
        decision.Commit(Ben, OptionKey.Skip);

        Assert.Empty(run.LinkGainedCards(decision, [new GainedCard(Ana, Candy, Card(Ana, 20))]));
        Assert.Null(run.Links.GroupOf(Ref(Ana, 20)));
    }

    [Fact]
    public void Cards_bought_because_of_the_same_purchase_are_linked()
    {
        var run = new SoulLinkRun(SoulLinkSettings.Default);
        var shop = run.NewShop();
        shop.Enter(Ana, 100, 2, [new(ShopSlot.Card(1), 50)]);
        shop.Enter(Ben, 100, 2, [new(ShopSlot.Card(1), 50)]);
        shop.Enter(Cal, 10, 2, [new(ShopSlot.Card(1), 50)]);
        var lead = shop.Buy(Ana, ShopSlot.Card(1));
        var follow = shop.Buy(Ben, ShopSlot.Card(1));
        shop.SetGold(Cal, 60);
        var late = shop.Buy(Cal, ShopSlot.Card(1));

        run.LinkPurchasedCards(
        [
            new PurchasedCard(lead, Card(Ana, 40)),
            new PurchasedCard(follow, Card(Ben, 40)),
            new PurchasedCard(late, Card(Cal, 40)),
        ]);

        Assert.Equal(Ref(Ben, 40), run.Links.LinkedCard(Ref(Ana, 40), Ben));
        Assert.Null(run.Links.GroupOf(Ref(Cal, 40)));
    }

    [Fact]
    public void Copies_from_a_linked_duplicate_form_a_new_group()
    {
        var run = new SoulLinkRun(SoulLinkSettings.Default);
        run.Links.Link([Ref(Ana, 1), Ref(Ben, 1)]);
        var duplicate = run.NewCardChoice();
        duplicate.Join(Screen(Ana, 5));
        duplicate.Join(Screen(Ben, 5));
        duplicate.Commit(Ana, Card(Ana, 1));
        duplicate.Commit(Ben, Card(Ben, 1));

        var groups = run.LinkCopies(duplicate, [Ref(Ana, 50), Ref(Ben, 50)]);

        Assert.Equal(run.Links.GroupOf(Ref(Ana, 50)), Assert.Single(groups));
        Assert.NotEqual(run.Links.GroupOf(Ref(Ana, 1)), run.Links.GroupOf(Ref(Ana, 50)));
    }
}
