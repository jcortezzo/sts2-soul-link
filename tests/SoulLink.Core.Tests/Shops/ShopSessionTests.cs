using SoulLink.Core.Cards;
using SoulLink.Core.Decisions;
using SoulLink.Core.Shops;
using static SoulLink.Core.Tests.TestCards;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests.Shops;

public class ShopSessionTests
{
    private static ShopSession NewShop(LinkMode mode, WhoDecides whoDecides = WhoDecides.FirstClick) =>
        new(mode, new DeckIndexMatching(), whoDecides);

    private static ShopItem[] Stock(int card1 = 50, int relic2 = 150, int potion1 = 50, int removal = 75) =>
    [
        new(ShopSlot.Card(1), card1),
        new(ShopSlot.Relic(2), relic2),
        new(ShopSlot.Potion(1), potion1),
        new(ShopSlot.Potion(2), potion1),
        new(ShopSlot.CardRemoval, removal),
    ];

    [Fact]
    public void A_purchase_obligates_everyone_who_can_buy_the_same_slot()
    {
        var shop = NewShop(LinkMode.Nice);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Ben, gold: 200, freePotionSlots: 2, Stock());
        shop.Enter(Cal, gold: 100, freePotionSlots: 2, Stock());

        var purchase = shop.Buy(Ana, ShopSlot.Relic(2));

        Assert.Equal([new Obligation(Ben, ShopSlot.Relic(2), 150, Ana, 1)], purchase.NewObligations);
        Assert.Empty(shop.OwedBy(Cal));
        Assert.False(shop.CanLeave(Ben));
    }

    [Fact]
    public void Gold_owed_for_earlier_purchases_is_set_aside()
    {
        var shop = NewShop(LinkMode.Nice);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Ben, gold: 200, freePotionSlots: 2, Stock(card1: 75));
        shop.Buy(Ana, ShopSlot.Relic(2));

        Assert.Equal(PurchaseVerdict.NotEnoughGold, shop.CanBuy(Ben, ShopSlot.Card(1)).Verdict);
        Assert.Empty(shop.Buy(Ana, ShopSlot.Card(1)).NewObligations);

        var settled = shop.Buy(Ben, ShopSlot.Relic(2));
        Assert.True(settled.SettledObligation);
        Assert.Empty(settled.NewObligations);
        Assert.True(shop.CanLeave(Ben));
        Assert.Equal(50, shop.GoldOf(Ben));
    }

    [Fact]
    public void Mean_greys_out_a_slot_someone_cannot_buy()
    {
        var shop = NewShop(LinkMode.Mean);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Ben, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Cal, gold: 100, freePotionSlots: 2, Stock(relic2: 160));

        Assert.Equal(PurchaseCheck.GreyedOut(Cal), shop.CanBuy(Ana, ShopSlot.Relic(2)));
        Assert.True(shop.CanBuy(Ana, ShopSlot.Card(1)).IsAllowed);
    }

    [Fact]
    public void Mean_purchases_bind_everyone()
    {
        var shop = NewShop(LinkMode.Mean);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Ben, gold: 300, freePotionSlots: 2, Stock());

        shop.Buy(Ana, ShopSlot.Card(1));

        Assert.Equal(ShopSlot.Card(1), Assert.Single(shop.OwedBy(Ben)).Slot);
        Assert.True(shop.CanBuy(Ben, ShopSlot.Card(1)).IsAllowed);
    }

    [Fact]
    public void Potion_slots_owed_are_set_aside()
    {
        var shop = NewShop(LinkMode.Nice);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Ben, gold: 300, freePotionSlots: 1, Stock());
        shop.Enter(Cal, gold: 300, freePotionSlots: 0, Stock());

        shop.Buy(Ana, ShopSlot.Potion(1));

        Assert.Single(shop.OwedBy(Ben));
        Assert.Empty(shop.OwedBy(Cal));
        Assert.Equal(PurchaseVerdict.NoPotionSlot, shop.CanBuy(Ben, ShopSlot.Potion(2)).Verdict);
    }

    [Fact]
    public void Each_slot_can_be_bought_once_and_only_if_offered()
    {
        var shop = NewShop(LinkMode.Nice);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, [new(ShopSlot.Card(1), 50), new(ShopSlot.Card(2), 50, InStock: false)]);
        shop.Buy(Ana, ShopSlot.Card(1));

        Assert.Equal(PurchaseVerdict.AlreadyBought, shop.CanBuy(Ana, ShopSlot.Card(1)).Verdict);
        Assert.Equal(PurchaseVerdict.SoldOut, shop.CanBuy(Ana, ShopSlot.Card(2)).Verdict);
        Assert.Equal(PurchaseVerdict.NotOffered, shop.CanBuy(Ana, ShopSlot.Relic(1)).Verdict);
        Assert.Equal(PurchaseVerdict.NotInShop, shop.CanBuy(Ben, ShopSlot.Card(1)).Verdict);
        Assert.Throws<InvalidOperationException>(() => shop.Buy(Ana, ShopSlot.Card(1)));
    }

    [Fact]
    public void Losing_gold_drops_obligations_that_no_longer_fit()
    {
        var shop = NewShop(LinkMode.Nice);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Ben, gold: 300, freePotionSlots: 2, Stock());
        shop.Buy(Ana, ShopSlot.Relic(2));
        shop.Buy(Ana, ShopSlot.Card(1));

        shop.SetGold(Ben, 160);

        Assert.Equal(ShopSlot.Relic(2), Assert.Single(shop.OwedBy(Ben)).Slot);
    }

    [Fact]
    public void Removal_buyers_remove_their_matching_cards()
    {
        var shop = NewShop(LinkMode.Nice);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Ben, gold: 300, freePotionSlots: 2, Stock());
        var purchase = shop.Buy(Ana, ShopSlot.CardRemoval);
        shop.RemovalTarget.Join(Screen(Ana, 12));
        foreach (var obligation in purchase.NewObligations)
        {
            shop.RemovalTarget.Join(Screen(obligation.Debtor, 10));
        }

        shop.RemovalTarget.Commit(Ana, Card(Ana, 11));

        Assert.Equal(Selection.Of(Card(Ben, 10)), shop.RemovalTarget.StatusOf(Ben).Binding!.Required);
    }

    [Fact]
    public void First_click_buys_the_slot_for_everyone_who_can()
    {
        var shop = NewShop(LinkMode.Nice);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Ben, gold: 200, freePotionSlots: 2, Stock());
        shop.Enter(Cal, gold: 100, freePotionSlots: 2, Stock());

        var purchases = shop.Choose(Ana, ShopSlot.Relic(2));

        Assert.Equal([Ana, Ben], purchases.Select(purchase => purchase.Player));
        Assert.Equal(purchases[0].Sequence, purchases[1].Root);
        Assert.Empty(shop.OwedBy(Ben));
        Assert.False(shop.HasBought(Cal, ShopSlot.Relic(2)));
    }

    [Fact]
    public void Everyone_agrees_buys_a_slot_once_everyone_who_can_buy_it_votes()
    {
        var shop = NewShop(LinkMode.Nice, WhoDecides.EveryoneAgrees);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Ben, gold: 200, freePotionSlots: 2, Stock());
        shop.Enter(Cal, gold: 100, freePotionSlots: 2, Stock());

        Assert.Empty(shop.Choose(Ben, ShopSlot.Relic(2)));
        Assert.Equal([ShopSlot.Relic(2)], shop.VotesOf(Ben));

        // Cal can't afford it, so only Ana has to agree. Ben voted first, so his purchase leads.
        var purchases = shop.Choose(Ana, ShopSlot.Relic(2));
        Assert.Equal([Ben, Ana], purchases.Select(purchase => purchase.Player));
        Assert.Equal(purchases[0].Sequence, purchases[1].Root);
        Assert.Empty(shop.VotersFor(ShopSlot.Relic(2)));
    }

    [Fact]
    public void Clicking_a_slot_again_takes_the_vote_back()
    {
        var shop = NewShop(LinkMode.Nice, WhoDecides.EveryoneAgrees);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Ben, gold: 300, freePotionSlots: 2, Stock());
        shop.Choose(Ana, ShopSlot.Card(1));

        shop.Choose(Ana, ShopSlot.Card(1));

        Assert.Empty(shop.VotesOf(Ana));
        Assert.Empty(shop.Choose(Ben, ShopSlot.Card(1)));
    }

    [Fact]
    public void Votes_a_player_can_no_longer_pay_for_are_dropped()
    {
        var shop = NewShop(LinkMode.Nice, WhoDecides.EveryoneAgrees);
        shop.Enter(Ana, gold: 100, freePotionSlots: 2, Stock(card1: 60, potion1: 50));
        shop.Enter(Ben, gold: 300, freePotionSlots: 2, Stock());
        shop.Choose(Ana, ShopSlot.Card(1));
        shop.Choose(Ana, ShopSlot.Potion(1));

        shop.Choose(Ben, ShopSlot.Card(1));

        // Ana has 40 gold left, too little for her 50 gold potion.
        Assert.True(shop.HasBought(Ana, ShopSlot.Card(1)));
        Assert.Empty(shop.VotesOf(Ana));
    }

    [Fact]
    public void A_player_who_can_no_longer_buy_stops_holding_up_the_others()
    {
        var shop = NewShop(LinkMode.Nice, WhoDecides.EveryoneAgrees);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Ben, gold: 300, freePotionSlots: 2, Stock());
        shop.Choose(Ana, ShopSlot.Relic(2));

        shop.SetGold(Ben, 100);

        Assert.Equal([Ana], shop.Settle().Select(purchase => purchase.Player));
    }

    [Fact]
    public void Mean_mode_refuses_votes_for_greyed_out_slots()
    {
        var shop = NewShop(LinkMode.Mean, WhoDecides.EveryoneAgrees);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, Stock());
        shop.Enter(Ben, gold: 100, freePotionSlots: 2, Stock());

        Assert.Throws<InvalidOperationException>(() => shop.Choose(Ana, ShopSlot.Relic(2)));
        Assert.Empty(shop.VotesOf(Ana));
    }
}
