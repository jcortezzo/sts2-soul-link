using SoulLink.Core.Campfires;
using SoulLink.Core.Cards;
using SoulLink.Core.Decisions;
using SoulLink.Core.Rewards;
using SoulLink.Core.Shops;
using static SoulLink.Core.Campfires.CampfireOptions;
using static SoulLink.Core.Tests.TestCards;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests;

/// <summary>
/// The worked examples from docs/rules.md, one test each, in the same order and wording. Examples 1 to 12 use first
/// click; the rest use everyone agrees.
/// </summary>
public class WorkedExamplesTests
{
    private static readonly SoulLinkSettings FirstClick = SoulLinkSettings.Default with { WhoDecides = WhoDecides.FirstClick };

    private static readonly OptionKey ExtraPower = CardRewardKeys.Extra("LASTING_CANDY");

    private static readonly CampfireOption[] AllOptions = [new(Rest), new(Mend), new(Smith), new(Dig), new(Cook), new(Lift)];

    private static CardRewardScreen CandyScreen(LinkMode mode)
    {
        var round = new CardRewardRound(mode, WhoDecides.FirstClick);
        round.Join(Ana, [new CardRewardOffer(3, ["LASTING_CANDY"])]);
        round.Join(Ben, [new CardRewardOffer(3)]);
        round.Join(Cal, [new CardRewardOffer(3)]);
        return round.Screen(1);
    }

    [Fact]
    public void Example_01_card_reward_nice()
    {
        // Ana has a Lasting Candy extra Power; Ben and Cal have three cards each.
        var screen = CandyScreen(LinkMode.Nice);

        // Ben takes slot 2 first, so Cal must take slot 2: first click takes it for him.
        var picks = screen.Choose(Ben, CardRewardKeys.Card(2));
        Assert.Equal([Ben, Cal], picks.Select(commit => commit.Player));
        Assert.Equal(Selection.Of(CardRewardKeys.Card(2)), screen.Decision.CommitOf(Cal)!.Choice);

        // Ana may take slot 2 or her extra Power, so she still chooses.
        Assert.False(screen.Decision.HasDecided(Ana));
        Assert.True(screen.CanCommit(Ana, CardRewardKeys.Card(2)).IsAllowed);
        Assert.True(screen.CanCommit(Ana, ExtraPower).IsAllowed);
        Assert.Equal(CommitCheck.MustFollow(Ben), screen.CanCommit(Ana, CardRewardKeys.Skip));
    }

    [Fact]
    public void Example_02_card_reward_mean()
    {
        var screen = CandyScreen(LinkMode.Mean);

        // Ana's extra Power is greyed out.
        Assert.Equal(CommitVerdict.GreyedOut, screen.CanCommit(Ana, ExtraPower).Verdict);

        // Ben takes slot 2, so Ana and Cal take slot 2.
        var picks = screen.Choose(Ben, CardRewardKeys.Card(2));
        Assert.Equal([Ben, Ana, Cal], picks.Select(commit => commit.Player));
        Assert.All(picks, commit => Assert.Equal(Selection.Of(CardRewardKeys.Card(2)), commit.Choice));
    }

    [Fact]
    public void Example_03_exclusive_option_first_nice()
    {
        var screen = CandyScreen(LinkMode.Nice);

        // Ana takes her extra Power first. Ben and Cal aren't bound by it.
        Assert.Equal([Ana], screen.Choose(Ana, ExtraPower).Select(commit => commit.Player));
        Assert.Equal(ChoiceState.Free, screen.Decision.StatusOf(Ben).State);
        Assert.Equal(ChoiceState.Free, screen.Decision.StatusOf(Cal).State);

        // Ben skips first, so Cal skips too.
        Assert.Equal([Ben, Cal], screen.Choose(Ben, CardRewardKeys.Skip).Select(commit => commit.Player));
        Assert.Equal(Selection.Of(CardRewardKeys.Skip), screen.Decision.CommitOf(Cal)!.Choice);
    }

    [Fact]
    public void Example_04_extra_screens()
    {
        // Ana has Prayer Wheel and Cal's The Hunt killed an enemy, so both get a second card reward.
        var round = new CardRewardRound(LinkMode.Nice, WhoDecides.FirstClick);
        round.Join(Ana, [new CardRewardOffer(3), new CardRewardOffer(3)]);
        round.Join(Ben, [new CardRewardOffer(3)]);
        round.Join(Cal, [new CardRewardOffer(3), new CardRewardOffer(3)]);

        // Screen 1 is linked for all three; screen 2 for Ana and Cal only.
        Assert.Equal([Ana, Ben, Cal], round.Screen(1).Participants);
        Assert.Equal([Ana, Cal], round.Screen(2).Participants);

        Assert.Equal([Cal, Ana], round.Screen(2).Choose(Cal, CardRewardKeys.Card(3)).Select(commit => commit.Player));
        Assert.True(round.Screen(1).CanCommit(Ana, CardRewardKeys.Card(1)).IsAllowed);
    }

    [Fact]
    public void Example_05_shop_nice()
    {
        var shop = new ShopSession(LinkMode.Nice, new DeckIndexMatching(), WhoDecides.FirstClick);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, [new(ShopSlot.Relic(2), 150), new(ShopSlot.Card(1), 50)]);
        shop.Enter(Ben, gold: 220, freePotionSlots: 2, [new(ShopSlot.Relic(2), 160), new(ShopSlot.Card(1), 70)]);
        shop.Enter(Cal, gold: 100, freePotionSlots: 2, [new(ShopSlot.Relic(2), 170), new(ShopSlot.Card(1), 45)]);

        // Ana buys relic slot 2. Ben can afford his relic slot 2, so he must buy it; Cal can't, so he's unaffected.
        var relic = shop.Choose(Ana, ShopSlot.Relic(2));
        Assert.Equal([Ana, Ben], relic.Select(purchase => purchase.Player));

        // Cal then buys card slot 1, so Ana and Ben must buy theirs if they can still afford them. Ana can; Ben has
        // 60 gold left for a 70 gold card.
        var card = shop.Choose(Cal, ShopSlot.Card(1));
        Assert.Equal([Cal, Ana], card.Select(purchase => purchase.Player));
    }

    [Fact]
    public void Example_06_shop_mean()
    {
        var shop = new ShopSession(LinkMode.Mean, new DeckIndexMatching(), WhoDecides.FirstClick);
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, [new(ShopSlot.Relic(2), 150)]);
        shop.Enter(Ben, gold: 300, freePotionSlots: 2, [new(ShopSlot.Relic(2), 160)]);
        shop.Enter(Cal, gold: 100, freePotionSlots: 2, [new(ShopSlot.Relic(2), 170)]);

        // Cal can't afford his relic slot 2, so relic slot 2 is greyed out for everyone.
        Assert.Equal(PurchaseCheck.GreyedOut(Cal), shop.CanBuy(Ana, ShopSlot.Relic(2)));
        Assert.Equal(PurchaseCheck.GreyedOut(Cal), shop.CanBuy(Ben, ShopSlot.Relic(2)));
        Assert.Equal(PurchaseVerdict.NotEnoughGold, shop.CanBuy(Cal, ShopSlot.Relic(2)).Verdict);
    }

    /// <summary>Three Ironclads with linked starter decks: card 1 is each player's first Strike (starter group 1).</summary>
    private static SoulLinkRun IroncladRun(LinkMode mode)
    {
        var run = new SoulLinkRun(FirstClick with { CardMatching = CardMatching.LinkedCards, Mode = mode });
        string[] names = ["Strike", "Strike", "Strike", "Strike", "Strike", "Defend", "Defend", "Defend", "Defend", "Bash"];
        run.LinkStarterDecks(
            new[] { Ana, Ben, Cal }
                .Select(player => new StarterDeck(player, names.Select((name, i) => new StarterCard(Card(player, i + 1), name)).ToList()))
                .ToList());
        return run;
    }

    /// <summary>Ben's first Strike is already upgraded, so it isn't on his upgrade screen.</summary>
    private static SelectionScreen BensUpgradeScreen() => SelectionScreen.Of(Ben, Deck(Ben, 10).Skip(1));

    [Fact]
    public void Example_07_smith_linked_cards_nice()
    {
        var campfire = IroncladRun(LinkMode.Nice).NewCampfire();
        campfire.Join(Ana, AllOptions, hasMiniatureTent: false, Screen(Ana, 10));
        campfire.Join(Ben, AllOptions, hasMiniatureTent: false, BensUpgradeScreen());
        campfire.Join(Cal, AllOptions, hasMiniatureTent: false, Screen(Cal, 10));

        // Ana smiths a Strike from starter group 1.
        campfire.Choose(Ana, Smith);
        campfire.SmithTarget.Commit(Ana, Card(Ana, 1));

        // Ben's linked Strike is already upgraded, so Ben upgrades any card he likes.
        Assert.Equal(ChoiceState.Free, campfire.SmithTarget.StatusOf(Ben).State);
        Assert.True(campfire.SmithTarget.CanCommit(Ben, Card(Ben, 7)).IsAllowed);

        // Cal upgrades his linked Strike.
        Assert.Equal(Selection.Of(Card(Cal, 1)), campfire.SmithTarget.StatusOf(Cal).Binding!.Required);
    }

    [Fact]
    public void Example_08_smith_linked_cards_mean()
    {
        var campfire = IroncladRun(LinkMode.Mean).NewCampfire();
        campfire.Join(Ana, AllOptions, hasMiniatureTent: false, Screen(Ana, 10));
        campfire.Join(Ben, AllOptions, hasMiniatureTent: false, BensUpgradeScreen());
        campfire.Join(Cal, AllOptions, hasMiniatureTent: false, Screen(Cal, 10));
        campfire.Choose(Ana, Smith);

        // Starter group 1 is greyed out for Ana, because Ben's Strike can't be upgraded again.
        Assert.Equal(CommitCheck.GreyedOut(Ben), campfire.SmithTarget.CanCommit(Ana, Card(Ana, 1)));
        Assert.True(campfire.SmithTarget.CanCommit(Ana, Card(Ana, 2)).IsAllowed);
    }

    [Fact]
    public void Example_09_smith_deck_index()
    {
        var run = new SoulLinkRun(FirstClick with { CardMatching = CardMatching.DeckIndex });
        var smith = run.NewCardChoice();
        smith.Join(Screen(Ana, 20));
        smith.Join(Screen(Ben, 15));

        // Ana upgrades card 17 on her upgrade screen. Ben's screen shows 15 cards, so he upgrades card 15.
        smith.Commit(Ana, Card(Ana, 17));
        Assert.Equal(Selection.Of(Card(Ben, 15)), smith.StatusOf(Ben).Binding!.Required);
    }

    [Fact]
    public void Example_10_campfire_with_miniature_tent()
    {
        // Ana has Miniature Tent.
        var campfire = new SoulLinkRun(FirstClick with { CardMatching = CardMatching.DeckIndex }).NewCampfire();
        campfire.Join(Ana, AllOptions, hasMiniatureTent: true, Screen(Ana, 10));
        campfire.Join(Ben, AllOptions, hasMiniatureTent: false, Screen(Ben, 10));
        campfire.Join(Cal, AllOptions, hasMiniatureTent: false, Screen(Cal, 10));

        // Ben smiths first, so Cal must pick Smith, Dig, Cook or Lift.
        campfire.Choose(Ben, Smith);
        campfire.SmithTarget.Commit(Ben, Card(Ben, 4));
        Assert.Equal(CommitCheck.MustFollow(Ben), campfire.CanChoose(Cal, Rest));
        Assert.Equal(CommitCheck.MustFollow(Ben), campfire.CanChoose(Cal, Mend));
        Assert.All([Smith, Dig, Cook, Lift], option => Assert.True(campfire.CanChoose(Cal, option).IsAllowed));

        // Ana can do every option.
        Assert.All(AllOptions, option => Assert.True(campfire.CanChoose(Ana, option.Id).IsAllowed));

        // If she or Cal smith, their targets match Ben's.
        Assert.Equal(Selection.Of(Card(Ana, 4)), campfire.SmithTarget.StatusOf(Ana).Binding!.Required);
        Assert.Equal(Selection.Of(Card(Cal, 4)), campfire.SmithTarget.StatusOf(Cal).Binding!.Required);
    }

    [Fact]
    public void Example_11_chest()
    {
        var chest = new SoulLinkRun(FirstClick).NewOptionDecision();
        foreach (var player in new[] { Ana, Ben, Cal })
        {
            chest.Join(player, TestOptions.Slots(3));
        }

        // Ana picks relic 1, so Ben and Cal must pick relic 1.
        var picks = chest.Choose(Ana, OptionKey.Slot(1));
        Assert.Equal([Ana, Ben, Cal], picks.Select(commit => commit.Player));
        Assert.All(picks, commit => Assert.Equal(Selection.Of(OptionKey.Slot(1)), commit.Choice));
    }

    [Fact]
    public void Example_12_shared_death_on()
    {
        var run = new SoulLinkRun(FirstClick with { SharedDeath = true });

        // Cal dies in an elite fight. The run ends for everyone, even if Ana and Ben win the fight.
        Assert.Equal(DeathOutcome.RunEnds, run.Tracker.RecordDeath(Cal));
    }

    [Fact]
    public void Example_13_card_reward_everyone_agrees()
    {
        var round = new SoulLinkRun(SoulLinkSettings.Default).NewCardRewardRound();
        foreach (var player in new[] { Ana, Ben, Cal })
        {
            round.Join(player, [new CardRewardOffer(3)]);
        }

        var screen = round.Screen(1);

        // Ana and Ben click slot 2 and Cal clicks Skip, so nothing is taken yet.
        Assert.Empty(screen.Choose(Ana, CardRewardKeys.Card(2)));
        Assert.Empty(screen.Choose(Ben, CardRewardKeys.Card(2)));
        Assert.Empty(screen.Choose(Cal, CardRewardKeys.Skip));

        // When Cal switches to slot 2, all three take slot 2.
        var picks = screen.Choose(Cal, CardRewardKeys.Card(2));
        Assert.Equal([Ana, Ben, Cal], picks.Select(commit => commit.Player));
    }

    [Fact]
    public void Example_14_shop_everyone_agrees()
    {
        var shop = new SoulLinkRun(SoulLinkSettings.Default).NewShop();
        shop.Enter(Ana, gold: 300, freePotionSlots: 2, [new(ShopSlot.Relic(2), 150)]);
        shop.Enter(Ben, gold: 220, freePotionSlots: 2, [new(ShopSlot.Relic(2), 160)]);
        shop.Enter(Cal, gold: 100, freePotionSlots: 2, [new(ShopSlot.Relic(2), 170)]);

        // Ana and Ben click relic slot 2. Cal can't afford his, so he isn't waited for, and Ana and Ben buy theirs.
        Assert.Empty(shop.Choose(Ana, ShopSlot.Relic(2)));
        var bought = shop.Choose(Ben, ShopSlot.Relic(2));
        Assert.Equal([Ana, Ben], bought.Select(purchase => purchase.Player));
    }

    [Fact]
    public void Example_15_campfire_everyone_agrees()
    {
        var campfire = new SoulLinkRun(SoulLinkSettings.Default).NewCampfire();
        campfire.Join(Ana, AllOptions, hasMiniatureTent: false, Screen(Ana, 10));
        campfire.Join(Ben, AllOptions, hasMiniatureTent: false, Screen(Ben, 10));
        campfire.Join(Cal, AllOptions, hasMiniatureTent: true, Screen(Cal, 10));

        // Cal has Miniature Tent and rests right away.
        Assert.Equal([new CampfireChoice(Cal, Rest)], campfire.Choose(Cal, Rest));

        // Ana clicks Rest and Ben clicks Smith, so neither happens yet.
        Assert.Empty(campfire.Choose(Ana, Rest));
        Assert.Empty(campfire.Choose(Ben, Smith));

        // When Ana switches to Dig, both picked Improve: Ana digs and Ben smiths.
        Assert.Equal([new CampfireChoice(Ben, Smith), new CampfireChoice(Ana, Dig)], campfire.Choose(Ana, Dig));
    }
}
