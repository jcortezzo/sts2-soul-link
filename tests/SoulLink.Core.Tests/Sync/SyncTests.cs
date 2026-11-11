using SoulLink.Core.Campfires;
using SoulLink.Core.Cards;
using SoulLink.Core.Decisions;
using SoulLink.Core.Rewards;
using SoulLink.Core.Shops;
using SoulLink.Core.Sync;
using static SoulLink.Core.Tests.TestCards;
using static SoulLink.Core.Tests.TestOptions;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests.Sync;

public class SyncTests
{
    private const string Event = "f3/event/1";

    private static IReadOnlyList<string> EventJoin => OptionTable.JoinArgs(1, Slots(3));

    [Fact]
    public void Starting_the_run_shares_the_hosts_settings()
    {
        var settings = new SoulLinkSettings(CardMatching.DeckIndex, LinkMode.Mean, SharedDeath: true);

        var network = new SyncTestNetwork(settings);

        Assert.Equal(settings, network.Agreed(state => state.Run!.Settings));
    }

    [Fact]
    public void Only_the_host_starts_the_run_and_players_only_act_for_themselves()
    {
        var network = new SyncTestNetwork();

        Assert.False(network.Send("run", Ben, SyncActions.RunStart, SyncState.StartArgs(SoulLinkSettings.Default)).IsAccepted);
        Assert.False(network.Host.Submit(new SyncOp(Event, Cal, SyncActions.OptionsJoin, EventJoin), Ben).IsAccepted);
    }

    [Fact]
    public void Every_peer_sees_the_same_bindings()
    {
        var network = new SyncTestNetwork();
        foreach (var player in new[] { Ana, Ben, Cal })
        {
            network.Send(Event, player, SyncActions.OptionsJoin, EventJoin);
        }

        Assert.True(network.Send(Event, Ben, SyncActions.OptionsCommit, Slot(2).Value).IsAccepted);

        var status = network.Agreed(state => state.Find<OptionTable>(Event)!.Decision.StatusOf(Cal));
        Assert.Equal(Selection.Of(Slot(2)), status.Binding!.Required);
    }

    [Fact]
    public void The_host_order_decides_the_first_click()
    {
        var network = new SyncTestNetwork();
        foreach (var player in new[] { Ana, Ben, Cal })
        {
            network.Send(Event, player, SyncActions.OptionsJoin, EventJoin);
        }

        var first = network.Send(Event, Cal, SyncActions.OptionsCommit, Slot(1).Value);
        var second = network.Send(Event, Ben, SyncActions.OptionsCommit, Slot(3).Value);

        Assert.True(first.IsAccepted);
        Assert.False(second.IsAccepted);
        Assert.Equal(CommitCheck.MustFollow(Cal).ToString(), second.Rejection!.Reason);
    }

    [Fact]
    public void Clients_hold_notices_that_arrive_early()
    {
        var host = new SyncHost(Ana);
        var notices = new List<SyncNotice>
        {
            host.Submit(new SyncOp("run", Ana, SyncActions.RunStart, SyncState.StartArgs(SoulLinkSettings.Default)), Ana).Notice!,
            host.Submit(new SyncOp(Event, Ana, SyncActions.OptionsJoin, EventJoin), Ana).Notice!,
            host.Submit(new SyncOp(Event, Ben, SyncActions.OptionsJoin, EventJoin), Ben).Notice!,
            host.Submit(new SyncOp(Event, Ana, SyncActions.OptionsCommit, [Slot(3).Value]), Ana).Notice!,
        };
        var replica = new SyncReplica();

        foreach (var notice in new[] { notices[3], notices[1], notices[0], notices[0], notices[2] })
        {
            replica.Receive(notice);
        }

        Assert.Equal(4, replica.Applied);
        Assert.Empty(replica.Errors);
        Assert.Equal(CommitCheck.MustFollow(Ana), replica.State.Find<OptionTable>(Event)!.Decision.CanCommit(Ben, Slot(1)));
    }

    [Fact]
    public void Clients_keep_the_hosts_card_match_even_if_their_deck_changed_first()
    {
        var network = new SyncTestNetwork();
        foreach (var state in network.States)
        {
            state.Run!.Links.Link([Ref(Ana, 3), Ref(Ben, 3)]);
        }

        const string smith = "f5/upgrade";
        network.Send(smith, Ana, SyncActions.CardsJoin, CardTable.JoinArgs(1, Screen(Ana, 5).Cards));
        network.Send(smith, Ben, SyncActions.CardsJoin, CardTable.JoinArgs(1, Screen(Ben, 5).Cards));

        // Ben's client removes Ana's card before the host's notice arrives.
        var replica = network.Replicas[Ben];
        replica.State.Run!.Links.Remove(Ref(Ana, 3));
        var outcome = network.Host.Submit(new SyncOp(smith, Ana, SyncActions.CardsCommit, [SyncArgs.Number(Card(Ana, 3).Value)]), Ana);
        replica.Receive(outcome.Notice!);

        Assert.Equal(Selection.Of(Card(Ben, 3)), replica.State.Find<CardTable>(smith)!.Decision.StatusOf(Ben).Binding!.Required);
    }

    [Fact]
    public void Shops_sync_purchases_and_obligations()
    {
        var network = new SyncTestNetwork();
        const string shop = "f7/shop";
        ShopItem[] stock = [new(ShopSlot.Relic(2), 150), new(ShopSlot.CardRemoval, 75)];
        network.Send(shop, Ana, SyncActions.ShopEnter, ShopTable.EnterArgs(300, 2, stock));
        network.Send(shop, Ben, SyncActions.ShopEnter, ShopTable.EnterArgs(200, 2, stock));
        network.Send(shop, Cal, SyncActions.ShopEnter, ShopTable.EnterArgs(100, 2, stock));

        Assert.True(network.Send(shop, Ana, SyncActions.ShopBuy, ShopTable.SlotArgs(ShopSlot.Relic(2))).IsAccepted);
        Assert.False(network.Send(shop, Cal, SyncActions.ShopBuy, ShopTable.SlotArgs(ShopSlot.Relic(2))).IsAccepted);

        var owed = network.Agreed(state => state.Find<ShopTable>(shop)!.Shop.OwedBy(Ben).Single().Slot);
        Assert.Equal(ShopSlot.Relic(2), owed);
        network.Send(shop, Ben, SyncActions.ShopGold, ShopTable.GoldArgs(100, 2));
        Assert.Empty(network.Agreed(state => state.Find<ShopTable>(shop)!.Shop.OwedBy(Ben)));
    }

    [Fact]
    public void Removal_targets_sync_through_the_shop()
    {
        var network = new SyncTestNetwork(SoulLinkSettings.Default with { CardMatching = CardMatching.DeckIndex });
        const string shop = "f7/shop";
        ShopItem[] stock = [new(ShopSlot.CardRemoval, 75)];
        network.Send(shop, Ana, SyncActions.ShopEnter, ShopTable.EnterArgs(300, 2, stock));
        network.Send(shop, Ben, SyncActions.ShopEnter, ShopTable.EnterArgs(300, 2, stock));
        network.Send(shop, Ana, SyncActions.ShopBuy, ShopTable.SlotArgs(ShopSlot.CardRemoval));
        network.Send(shop, Ana, SyncActions.ShopRemovalJoin, ShopTable.RemovalScreenArgs(Screen(Ana, 12).Cards));
        network.Send(shop, Ben, SyncActions.ShopRemovalJoin, ShopTable.RemovalScreenArgs(Screen(Ben, 10).Cards));

        network.Send(shop, Ana, SyncActions.ShopRemovalCommit, SyncArgs.Number(Card(Ana, 11).Value));

        var required = network.Agreed(state => state.Find<ShopTable>(shop)!.Shop.RemovalTarget.StatusOf(Ben).Binding!.Required);
        Assert.Equal(Selection.Of(Card(Ben, 10)), required);
    }

    [Fact]
    public void Campfires_sync_categories_and_smith_targets()
    {
        var network = new SyncTestNetwork(SoulLinkSettings.Default with { CardMatching = CardMatching.DeckIndex });
        const string campfire = "f9/campfire";
        CampfireOption[] options = [new(CampfireOptions.Rest), new(CampfireOptions.Smith)];
        network.Send(campfire, Ana, SyncActions.CampfireJoin, CampfireTable.JoinArgs(true, options, Screen(Ana, 10).Cards));
        network.Send(campfire, Ben, SyncActions.CampfireJoin, CampfireTable.JoinArgs(false, options, Screen(Ben, 10).Cards));
        network.Send(campfire, Cal, SyncActions.CampfireJoin, CampfireTable.JoinArgs(false, options, null));

        network.Send(campfire, Ben, SyncActions.CampfireChoose, CampfireOptions.Smith);
        network.Send(campfire, Ben, SyncActions.CampfireSmith, SyncArgs.Number(Card(Ben, 4).Value));

        Assert.False(network.Send(campfire, Cal, SyncActions.CampfireChoose, CampfireOptions.Rest).IsAccepted);
        var required = network.Agreed(state => state.Find<CampfireTable>(campfire)!.Campfire.SmithTarget.StatusOf(Ana).Binding!.Required);
        Assert.Equal(Selection.Of(Card(Ana, 4)), required);
    }

    [Fact]
    public void Card_rewards_sync_picks_and_rerolls()
    {
        var network = new SyncTestNetwork();
        const string reward = "f2/reward/1";
        network.Send(reward, Ana, SyncActions.RewardJoin, RewardTable.JoinArgs(1, new CardRewardOffer(3, CanReroll: true)));
        network.Send(reward, Ben, SyncActions.RewardJoin, RewardTable.JoinArgs(1, new CardRewardOffer(3, ["LASTING_CANDY"], CanReroll: true)));

        network.Send(reward, Ana, SyncActions.RewardReroll, RewardTable.OfferArgs(new CardRewardOffer(3)));

        Assert.False(network.Send(reward, Ben, SyncActions.RewardCommit, Candy.Value).IsAccepted);
        network.Send(reward, Ben, SyncActions.RewardReroll, RewardTable.OfferArgs(new CardRewardOffer(3, ["LASTING_CANDY"])));
        Assert.True(network.Send(reward, Ben, SyncActions.RewardCommit, Candy.Value).IsAccepted);
        Assert.Equal(ChoiceState.Free, network.Agreed(state => state.Find<RewardTable>(reward)!.Screen.Decision.StatusOf(Ana).State));
    }

    [Fact]
    public void A_refused_join_leaves_no_table()
    {
        var network = new SyncTestNetwork();

        Assert.False(network.Send(Event, Ana, SyncActions.OptionsJoin, OptionTable.JoinArgs(0, Slots(3))).IsAccepted);
        Assert.False(network.Send(Event, Ana, SyncActions.OptionsCommit, Slot(1).Value).IsAccepted);
        Assert.Empty(network.Host.State.TableIds);
    }

    [Fact]
    public void Closed_tables_refuse_new_operations_and_clients_skip_old_ones()
    {
        var network = new SyncTestNetwork();
        network.Send(Event, Ana, SyncActions.OptionsJoin, EventJoin);
        network.Replicas[Ben].State.Close(Event);

        Assert.True(network.Send(Event, Ben, SyncActions.OptionsJoin, EventJoin).IsAccepted);
        Assert.Empty(network.Replicas[Ben].Errors);

        network.Host.State.Close(Event);
        Assert.False(network.Send(Event, Cal, SyncActions.OptionsJoin, EventJoin).IsAccepted);
    }

    [Fact]
    public void Malformed_operations_are_refused()
    {
        var network = new SyncTestNetwork();
        network.Send(Event, Ana, SyncActions.OptionsJoin, EventJoin);

        Assert.False(network.Send(Event, Ana, SyncActions.OptionsCommit, Slot(1).Value, Slot(1).Value).IsAccepted);
        Assert.False(network.Send(Event, Ana, SyncActions.ShopBuy, "Relic", "2").IsAccepted);
        Assert.False(network.Send(Event, Ben, SyncActions.OptionsJoin, "1", "slot:1", "maybe").IsAccepted);
        Assert.False(network.Send("f3/nothing", Ana, "options.dance").IsAccepted);
    }

    [Fact]
    public void Messages_round_trip_through_packets()
    {
        var op = new SyncOp("f2/reward/1", Ben, SyncActions.RewardCommit, ["slot:2"]);
        var notice = new SyncNotice(42, op, ["1", "Lead", "", "0", "Nice", "slot:2"]);
        var rejection = new SyncRejection(op, "MustFollow (Player 1)");
        var packet = new MemoryPacket();

        SyncCodec.WriteOp(packet, op);
        SyncCodec.WriteNotice(packet, notice);
        SyncCodec.WriteRejection(packet, rejection);

        Assert.Equal(op, SyncCodec.ReadOp(packet));
        Assert.Equal(notice, SyncCodec.ReadNotice(packet));
        Assert.Equal(rejection, SyncCodec.ReadRejection(packet));
    }

    [Fact]
    public void Corrupt_list_lengths_are_rejected()
    {
        var packet = new MemoryPacket();
        packet.WriteString(Event);
        packet.WriteULong(Ana.Value);
        packet.WriteString(SyncActions.OptionsCommit);
        packet.WriteInt(-1);

        Assert.Throws<InvalidDataException>(() => SyncCodec.ReadOp(packet));
    }
}
