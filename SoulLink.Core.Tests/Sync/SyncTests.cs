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

    private static SoulLinkSettings FirstClick => SoulLinkSettings.Default with { WhoDecides = WhoDecides.FirstClick };

    [Fact]
    public void Starting_the_run_shares_the_hosts_settings()
    {
        var settings = new SoulLinkSettings(CardMatching.DeckIndex, LinkMode.Mean, SharedDeath: true, WhoDecides.FirstClick);

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
    public void Every_peer_sees_the_picks_a_first_click_makes()
    {
        var network = new SyncTestNetwork(FirstClick);
        foreach (var player in new[] { Ana, Ben, Cal })
        {
            network.Send(Event, player, SyncActions.OptionsJoin, EventJoin);
        }

        Assert.True(network.Send(Event, Ben, SyncActions.OptionsChoose, Slot(2).Value).IsAccepted);

        Assert.Equal([Ben, Ana, Cal], network.Agreed(state => state.Find<OptionTable>(Event)!.Decision.Commits.Select(commit => commit.Player).ToList()));
        Assert.Equal(Selection.Of(Slot(2)), network.Agreed(state => state.Find<OptionTable>(Event)!.Decision.CommitOf(Cal)!.Choice));
    }

    [Fact]
    public void The_host_order_decides_the_first_click()
    {
        var network = new SyncTestNetwork(FirstClick);
        foreach (var player in new[] { Ana, Ben, Cal })
        {
            network.Send(Event, player, SyncActions.OptionsJoin, EventJoin);
        }

        var first = network.Send(Event, Cal, SyncActions.OptionsChoose, Slot(1).Value);
        var second = network.Send(Event, Ben, SyncActions.OptionsChoose, Slot(3).Value);

        Assert.True(first.IsAccepted);
        Assert.False(second.IsAccepted);
        Assert.Equal(CommitCheck.AlreadyDecided.ToString(), second.Rejection!.Reason);
    }

    [Fact]
    public void Votes_sync_and_an_agreement_commits_for_everyone()
    {
        var network = new SyncTestNetwork();
        foreach (var player in new[] { Ana, Ben, Cal })
        {
            network.Send(Event, player, SyncActions.OptionsJoin, EventJoin);
        }

        network.Send(Event, Ana, SyncActions.OptionsChoose, Slot(2).Value);
        network.Send(Event, Ben, SyncActions.OptionsChoose, Slot(2).Value);

        Assert.Equal(Selection.Of(Slot(2)), network.Agreed(state => state.Find<OptionTable>(Event)!.Decision.VoteOf(Ana)));
        Assert.Empty(network.Agreed(state => state.Find<OptionTable>(Event)!.Decision.Commits));

        network.Send(Event, Cal, SyncActions.OptionsChoose, Slot(2).Value);

        Assert.True(network.Agreed(state => state.Find<OptionTable>(Event)!.Decision.IsComplete));
        Assert.Equal(Ana, network.Agreed(state => state.Find<OptionTable>(Event)!.Decision.Commits[0].Player));
    }

    [Fact]
    public void A_late_player_follows_a_first_click_when_they_join()
    {
        var network = new SyncTestNetwork(FirstClick);
        network.Send(Event, Ana, SyncActions.OptionsJoin, EventJoin);
        network.Send(Event, Ana, SyncActions.OptionsChoose, Slot(3).Value);

        network.Send(Event, Ben, SyncActions.OptionsJoin, EventJoin);

        Assert.Equal(Selection.Of(Slot(3)), network.Agreed(state => state.Find<OptionTable>(Event)!.Decision.CommitOf(Ben)?.Choice));
    }

    [Fact]
    public void A_player_leaving_can_complete_an_agreement()
    {
        var network = new SyncTestNetwork();
        foreach (var player in new[] { Ana, Ben, Cal })
        {
            network.Send(Event, player, SyncActions.OptionsJoin, EventJoin);
        }

        network.Send(Event, Ana, SyncActions.OptionsChoose, Slot(1).Value);
        network.Send(Event, Ben, SyncActions.OptionsChoose, Slot(1).Value);
        network.Send(Event, Cal, SyncActions.OptionsLeave, []);

        Assert.True(network.Agreed(state => state.Find<OptionTable>(Event)!.Decision.IsComplete));
    }

    [Fact]
    public void Clients_hold_notices_that_arrive_early()
    {
        var host = new SyncHost(Ana);
        var notices = new List<SyncNotice>
        {
            host.Submit(new SyncOp("run", Ana, SyncActions.RunStart, SyncState.StartArgs(FirstClick)), Ana).Notice!,
            host.Submit(new SyncOp(Event, Ana, SyncActions.OptionsJoin, EventJoin), Ana).Notice!,
            host.Submit(new SyncOp(Event, Ben, SyncActions.OptionsJoin, EventJoin), Ben).Notice!,
            host.Submit(new SyncOp(Event, Ana, SyncActions.OptionsChoose, [Slot(3).Value]), Ana).Notice!,
        };
        var replica = new SyncReplica();

        foreach (var notice in new[] { notices[3], notices[1], notices[0], notices[0], notices[2] })
        {
            replica.Receive(notice);
        }

        Assert.Equal(4, replica.Applied);
        Assert.Empty(replica.Errors);
        Assert.Equal(Selection.Of(Slot(3)), replica.State.Find<OptionTable>(Event)!.Decision.CommitOf(Ben)!.Choice);
    }

    [Fact]
    public void Clients_keep_the_hosts_card_match_even_if_their_deck_changed_first()
    {
        var network = new SyncTestNetwork(FirstClick);
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
        var outcome = network.Host.Submit(new SyncOp(smith, Ana, SyncActions.CardsChoose, [SyncArgs.Number(Card(Ana, 3).Value)]), Ana);
        replica.Receive(outcome.Notice!);

        Assert.Empty(replica.Errors);
        Assert.Equal(Selection.Of(Card(Ben, 3)), replica.State.Find<CardTable>(smith)!.Decision.CommitOf(Ben)!.Choice);
    }

    [Fact]
    public void A_first_click_purchase_buys_for_everyone_who_can()
    {
        var network = new SyncTestNetwork(FirstClick);
        const string shop = "f7/shop";
        ShopItem[] stock = [new(ShopSlot.Relic(2), 150), new(ShopSlot.CardRemoval, 75)];
        network.Send(shop, Ana, SyncActions.ShopEnter, ShopTable.EnterArgs(300, 2, stock));
        network.Send(shop, Ben, SyncActions.ShopEnter, ShopTable.EnterArgs(200, 2, stock));
        network.Send(shop, Cal, SyncActions.ShopEnter, ShopTable.EnterArgs(100, 2, stock));

        Assert.True(network.Send(shop, Ana, SyncActions.ShopBuy, ShopTable.SlotArgs(ShopSlot.Relic(2))).IsAccepted);
        Assert.False(network.Send(shop, Cal, SyncActions.ShopBuy, ShopTable.SlotArgs(ShopSlot.Relic(2))).IsAccepted);

        Assert.True(network.Agreed(state => state.Find<ShopTable>(shop)!.Shop.HasBought(Ben, ShopSlot.Relic(2))));
        Assert.Equal(50, network.Agreed(state => state.Find<ShopTable>(shop)!.Shop.GoldOf(Ben)));
        Assert.Empty(network.Agreed(state => state.Find<ShopTable>(shop)!.Shop.OwedBy(Ben)));
    }

    [Fact]
    public void Shop_votes_sync_and_a_player_who_runs_out_of_gold_stops_blocking()
    {
        var network = new SyncTestNetwork();
        const string shop = "f7/shop";
        ShopItem[] stock = [new(ShopSlot.Relic(2), 150)];
        network.Send(shop, Ana, SyncActions.ShopEnter, ShopTable.EnterArgs(300, 2, stock));
        network.Send(shop, Ben, SyncActions.ShopEnter, ShopTable.EnterArgs(200, 2, stock));
        network.Send(shop, Cal, SyncActions.ShopEnter, ShopTable.EnterArgs(200, 2, stock));

        network.Send(shop, Ana, SyncActions.ShopBuy, ShopTable.SlotArgs(ShopSlot.Relic(2)));
        network.Send(shop, Ben, SyncActions.ShopBuy, ShopTable.SlotArgs(ShopSlot.Relic(2)));
        Assert.Equal([Ana, Ben], network.Agreed(state => state.Find<ShopTable>(shop)!.Shop.VotersFor(ShopSlot.Relic(2)).ToList()));

        network.Send(shop, Cal, SyncActions.ShopGold, ShopTable.GoldArgs(100, 2));

        Assert.True(network.Agreed(state => state.Find<ShopTable>(shop)!.Shop.HasBought(Ana, ShopSlot.Relic(2))));
        Assert.True(network.Agreed(state => state.Find<ShopTable>(shop)!.Shop.HasBought(Ben, ShopSlot.Relic(2))));
        Assert.Empty(network.Agreed(state => state.Find<ShopTable>(shop)!.Shop.VotersFor(ShopSlot.Relic(2))));
    }

    [Fact]
    public void Removal_targets_sync_through_the_shop()
    {
        var network = new SyncTestNetwork(FirstClick with { CardMatching = CardMatching.DeckIndex });
        const string shop = "f7/shop";
        ShopItem[] stock = [new(ShopSlot.CardRemoval, 75)];
        network.Send(shop, Ana, SyncActions.ShopEnter, ShopTable.EnterArgs(300, 2, stock));
        network.Send(shop, Ben, SyncActions.ShopEnter, ShopTable.EnterArgs(300, 2, stock));
        network.Send(shop, Ana, SyncActions.ShopBuy, ShopTable.SlotArgs(ShopSlot.CardRemoval));
        network.Send(shop, Ana, SyncActions.ShopRemovalJoin, ShopTable.RemovalScreenArgs(Screen(Ana, 12).Cards));
        network.Send(shop, Ana, SyncActions.ShopRemovalChoose, SyncArgs.Number(Card(Ana, 11).Value));

        // Ben bought removal along with Ana, and follows her target as soon as his screen joins.
        Assert.True(network.Agreed(state => state.Find<ShopTable>(shop)!.Shop.HasBought(Ben, ShopSlot.CardRemoval)));
        network.Send(shop, Ben, SyncActions.ShopRemovalJoin, ShopTable.RemovalScreenArgs(Screen(Ben, 10).Cards));

        var target = network.Agreed(state => state.Find<ShopTable>(shop)!.Shop.RemovalTarget.CommitOf(Ben)?.Choice);
        Assert.Equal(Selection.Of(Card(Ben, 10)), target);
    }

    [Fact]
    public void Campfires_sync_categories_and_smith_targets_with_first_click()
    {
        var network = new SyncTestNetwork(FirstClick with { CardMatching = CardMatching.DeckIndex });
        const string campfire = "f9/campfire";
        CampfireOption[] options = [new(CampfireOptions.Rest), new(CampfireOptions.Smith)];
        network.Send(campfire, Ana, SyncActions.CampfireJoin, CampfireTable.JoinArgs(true, options, Screen(Ana, 10).Cards));
        network.Send(campfire, Ben, SyncActions.CampfireJoin, CampfireTable.JoinArgs(false, options, Screen(Ben, 10).Cards));
        network.Send(campfire, Cal, SyncActions.CampfireJoin, CampfireTable.JoinArgs(false, options, null));

        network.Send(campfire, Ben, SyncActions.CampfireChoose, CampfireOptions.Smith);
        network.Send(campfire, Ben, SyncActions.CampfireSmith, SyncArgs.Number(Card(Ben, 4).Value));

        Assert.False(network.Send(campfire, Cal, SyncActions.CampfireChoose, CampfireOptions.Rest).IsAccepted);
        Assert.Null(network.Agreed(state => state.Find<CampfireTable>(campfire)!.Campfire.SmithTarget.CommitOf(Ana)));

        // Ana has Miniature Tent, so her target is only picked once she chooses Smith.
        network.Send(campfire, Ana, SyncActions.CampfireChoose, CampfireOptions.Smith);

        var target = network.Agreed(state => state.Find<CampfireTable>(campfire)!.Campfire.SmithTarget.CommitOf(Ana)?.Choice);
        Assert.Equal(Selection.Of(Card(Ana, 4)), target);
    }

    [Fact]
    public void Campfire_clicks_wait_until_everyone_picks_the_same_category()
    {
        var network = new SyncTestNetwork(SoulLinkSettings.Default with { CardMatching = CardMatching.DeckIndex });
        const string campfire = "f9/campfire";
        CampfireOption[] options = [new(CampfireOptions.Rest), new(CampfireOptions.Smith), new(CampfireOptions.Dig)];
        network.Send(campfire, Ben, SyncActions.CampfireJoin, CampfireTable.JoinArgs(false, options, Screen(Ben, 10).Cards));
        network.Send(campfire, Cal, SyncActions.CampfireJoin, CampfireTable.JoinArgs(false, options, Screen(Cal, 10).Cards));

        network.Send(campfire, Ben, SyncActions.CampfireChoose, CampfireOptions.Smith);
        network.Send(campfire, Cal, SyncActions.CampfireChoose, CampfireOptions.Rest);
        Assert.Equal(CampfireOptions.Rest, network.Agreed(state => state.Find<CampfireTable>(campfire)!.Campfire.PendingOf(Cal)));

        network.Send(campfire, Cal, SyncActions.CampfireChoose, CampfireOptions.Dig);

        Assert.Equal([CampfireOptions.Smith], network.Agreed(state => state.Find<CampfireTable>(campfire)!.Campfire.ChosenBy(Ben).ToList()));
        Assert.Equal([CampfireOptions.Dig], network.Agreed(state => state.Find<CampfireTable>(campfire)!.Campfire.ChosenBy(Cal).ToList()));
        Assert.DoesNotContain(Cal, network.Agreed(state => state.Find<CampfireTable>(campfire)!.Campfire.SmithTarget.Participants.ToList()));
    }

    [Fact]
    public void Card_rewards_sync_picks_and_rerolls()
    {
        var network = new SyncTestNetwork();
        const string reward = "f2/reward/1";
        network.Send(reward, Ana, SyncActions.RewardJoin, RewardTable.JoinArgs(1, new CardRewardOffer(3, CanReroll: true)));
        network.Send(reward, Ben, SyncActions.RewardJoin, RewardTable.JoinArgs(1, new CardRewardOffer(3, ["LASTING_CANDY"], CanReroll: true)));

        network.Send(reward, Ana, SyncActions.RewardReroll, RewardTable.OfferArgs(new CardRewardOffer(3)));

        Assert.False(network.Send(reward, Ben, SyncActions.RewardChoose, Candy.Value).IsAccepted);
        network.Send(reward, Ben, SyncActions.RewardReroll, RewardTable.OfferArgs(new CardRewardOffer(3, ["LASTING_CANDY"])));
        Assert.True(network.Send(reward, Ben, SyncActions.RewardChoose, Candy.Value).IsAccepted);
        Assert.Equal(Selection.Of(Candy), network.Agreed(state => state.Find<RewardTable>(reward)!.Screen.Decision.CommitOf(Ben)?.Choice));
        Assert.Equal(ChoiceState.Free, network.Agreed(state => state.Find<RewardTable>(reward)!.Screen.Decision.StatusOf(Ana).State));
    }

    [Fact]
    public void A_refused_join_leaves_no_table()
    {
        var network = new SyncTestNetwork();

        Assert.False(network.Send(Event, Ana, SyncActions.OptionsJoin, OptionTable.JoinArgs(0, Slots(3))).IsAccepted);
        Assert.False(network.Send(Event, Ana, SyncActions.OptionsChoose, Slot(1).Value).IsAccepted);
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

        Assert.False(network.Send(Event, Ana, SyncActions.OptionsChoose, Slot(1).Value, Slot(1).Value).IsAccepted);
        Assert.False(network.Send(Event, Ana, SyncActions.ShopBuy, "Relic", "2").IsAccepted);
        Assert.False(network.Send(Event, Ben, SyncActions.OptionsJoin, "1", "slot:1", "maybe").IsAccepted);
        Assert.False(network.Send("f3/nothing", Ana, "options.dance").IsAccepted);
    }

    [Fact]
    public void Messages_round_trip_through_packets()
    {
        var op = new SyncOp("f2/reward/1", Ben, SyncActions.RewardChoose, ["slot:2"]);
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
        packet.WriteString(SyncActions.OptionsChoose);
        packet.WriteInt(-1);

        Assert.Throws<InvalidDataException>(() => SyncCodec.ReadOp(packet));
    }

    [Fact]
    public void The_host_pairs_the_starting_decks_for_every_peer()
    {
        var network = new SyncTestNetwork();
        StarterDeck[] decks =
        [
            new(Ana, [new(Card(Ana, 1), "Strike"), new(Card(Ana, 2), "Defend")]),
            new(Ben, [new(Card(Ben, 1), "Defend"), new(Card(Ben, 2), "Strike")]),
        ];

        Assert.False(network.Send("links", Ben, SyncActions.LinksStarters, SyncState.StarterArgs(decks)).IsAccepted);
        Assert.True(network.Send("links", Ana, SyncActions.LinksStarters, SyncState.StarterArgs(decks)).IsAccepted);

        Assert.Equal(Ref(Ben, 2), network.Agreed(state => state.Run!.Links.LinkedCard(Ref(Ana, 1), Ben)));
        Assert.Equal(Ref(Ben, 1), network.Agreed(state => state.Run!.Links.LinkedCard(Ref(Ana, 2), Ben)));
    }

    [Fact]
    public void Gained_cards_link_the_same_way_on_every_peer()
    {
        var network = new SyncTestNetwork();
        var source = GainLinks.SourceKey("f2/rewards-1/card-1", "slot:2", root: 1);

        network.Send("links", Ben, SyncActions.LinksGained, SyncState.GainedArgs(source, [Card(Ben, 20)]));
        network.Send("links", Cal, SyncActions.LinksGained, SyncState.GainedArgs(source, [Card(Cal, 20)]));

        Assert.Equal(Ref(Cal, 20), network.Agreed(state => state.Run!.Links.LinkedCard(Ref(Ben, 20), Cal)));
        Assert.Equal(1, network.Agreed(state => state.Run!.Links.GroupOf(Ref(Ben, 20))!.Value.Value));
    }
}
