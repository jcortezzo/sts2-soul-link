using SoulLink.Core.Cards;
using SoulLink.Core.Decisions;
using SoulLink.Core.Shops;
using static SoulLink.Core.Tests.TestOptions;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests;

public class RunTrackerTests
{
    [Fact]
    public void Deaths_are_counted_and_the_run_continues_by_default()
    {
        var tracker = new RunTracker(SoulLinkSettings.Default);

        Assert.Equal(DeathOutcome.RunContinues, tracker.RecordDeath(Cal));
        tracker.RecordDeath(Cal);

        Assert.Equal(2, tracker.DeathsOf(Cal));
        Assert.Equal(0, tracker.DeathsOf(Ana));
    }

    [Fact]
    public void With_shared_death_any_death_ends_the_run()
    {
        var tracker = new RunTracker(SoulLinkSettings.Default with { SharedDeath = true });

        Assert.Equal(DeathOutcome.RunEnds, tracker.RecordDeath(Cal));
    }

    [Fact]
    public void Leads_in_shared_decisions_count_as_first_clicks()
    {
        var tracker = new RunTracker(SoulLinkSettings.Default);
        var shared = new OptionDecision(LinkMode.Nice);
        shared.Join(Ana, CardReward(3));
        shared.Join(Ben, CardReward(3));
        var solo = new OptionDecision(LinkMode.Nice);
        solo.Join(Ana, CardReward(3));

        tracker.RecordCommit(shared, shared.Commit(Ana, Slot(1)));
        tracker.RecordCommit(shared, shared.Commit(Ben, Slot(1)));
        tracker.RecordCommit(solo, solo.Commit(Ana, Slot(2)));

        Assert.Equal(1, tracker.FirstClicksOf(Ana));
        Assert.Equal(0, tracker.FirstClicksOf(Ben));
    }

    [Fact]
    public void Purchases_made_on_your_own_count_as_first_clicks()
    {
        var tracker = new RunTracker(SoulLinkSettings.Default);
        var shop = new ShopSession(LinkMode.Nice, new DeckIndexMatching());
        shop.Enter(Ana, 100, 2, [new(ShopSlot.Card(1), 50)]);
        shop.Enter(Ben, 100, 2, [new(ShopSlot.Card(1), 50)]);

        tracker.RecordPurchase(shop, shop.Buy(Ana, ShopSlot.Card(1)));
        tracker.RecordPurchase(shop, shop.Buy(Ben, ShopSlot.Card(1)));

        Assert.Equal(
            [new PlayerSummary(Ana, 1, 0), new PlayerSummary(Ben, 0, 0)],
            tracker.Summary([Ana, Ben]));
    }
}
