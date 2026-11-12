using Godot;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using SoulLink.Core;
using SoulLink.Core.Shops;
using SoulLink.Core.Sync;
using SoulLink.SoulLinkCode.Cards;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Shops;

/// <summary>
/// Links shop purchases slot by slot: each card, relic and potion slot and the card removal service. A click is a vote
/// to buy or a first-click purchase; the mod buys from the local player's own shop, at their own price, whatever the
/// host decides they buy.
/// </summary>
internal static class ShopLinks
{
    /// <summary>Purchases done in the game, as "table|sequence", so each linked purchase is made once.</summary>
    private static readonly HashSet<string> Done = new(StringComparer.Ordinal);

    private static readonly HashSet<string> Entered = new(StringComparer.Ordinal);
    private static int _buying;
    private static (int Gold, int PotionSlots)? _lastReported;
    private static bool _scheduled;

    /// <summary>Forgets everything from the last run.</summary>
    public static void Reset()
    {
        Done.Clear();
        Entered.Clear();
        _buying = 0;
        _lastReported = null;
    }

    public static NMerchantRoom? Room() =>
        NMerchantRoom.Instance is { } room && GodotObject.IsInstanceValid(room) && room.IsInsideTree() ? room : null;

    public static MerchantInventory? Inventory() => Room()?.Room?.GetLocalInventory();

    public static string? TableId() => OptionLinks.Room is { } room ? $"{room}/shop" : null;

    /// <summary>The shop's linked state, once the local player has entered it.</summary>
    public static ShopSession? Shop(string table) =>
        OptionLinks.Session is { } session && session.State.Find<ShopTable>(table)?.Shop is { } shop && shop.IsIn(session.Me) ? shop : null;

    /// <summary>The slot an entry sits in: cards count across the character and colorless rows, then relics, potions and removal.</summary>
    public static ShopSlot? SlotOf(MerchantInventory inventory, MerchantEntry entry)
    {
        var cards = inventory.CharacterCardEntries.Concat(inventory.ColorlessCardEntries).ToList();
        for (var i = 0; i < cards.Count; i++)
        {
            if (ReferenceEquals(cards[i], entry))
            {
                return ShopSlot.Card(i + 1);
            }
        }

        for (var i = 0; i < inventory.RelicEntries.Count; i++)
        {
            if (ReferenceEquals(inventory.RelicEntries[i], entry))
            {
                return ShopSlot.Relic(i + 1);
            }
        }

        for (var i = 0; i < inventory.PotionEntries.Count; i++)
        {
            if (ReferenceEquals(inventory.PotionEntries[i], entry))
            {
                return ShopSlot.Potion(i + 1);
            }
        }

        return ReferenceEquals(inventory.CardRemovalEntry, entry) ? ShopSlot.CardRemoval : null;
    }

    /// <summary>Every slot in the local player's shop with its entry.</summary>
    public static IEnumerable<(ShopSlot Slot, MerchantEntry Entry)> Slots(MerchantInventory inventory)
    {
        var cards = inventory.CharacterCardEntries.Concat(inventory.ColorlessCardEntries).ToList();
        for (var i = 0; i < cards.Count; i++)
        {
            yield return (ShopSlot.Card(i + 1), cards[i]);
        }

        for (var i = 0; i < inventory.RelicEntries.Count; i++)
        {
            yield return (ShopSlot.Relic(i + 1), inventory.RelicEntries[i]);
        }

        for (var i = 0; i < inventory.PotionEntries.Count; i++)
        {
            yield return (ShopSlot.Potion(i + 1), inventory.PotionEntries[i]);
        }

        if (inventory.CardRemovalEntry is { } removal)
        {
            yield return (ShopSlot.CardRemoval, removal);
        }
    }

    /// <summary>
    /// Slot peek in the shop: each teammate's item in the hovered slot, with their price. Every player's stock is
    /// known on every machine, so nothing needs sharing.
    /// </summary>
    public static IReadOnlyList<IHoverTip> Peek(NMerchantSlot node)
    {
        if (OptionLinks.Session is not { } session
            || Room()?.Room is not { } room
            || room.GetLocalInventory() is not { } mine
            || node.Entry is not { } entry
            || SlotOf(mine, entry) is not { } slot
            || slot == ShopSlot.CardRemoval)
        {
            return [];
        }

        var tips = new List<IHoverTip>();
        foreach (var inventory in room.Inventories.Where(inventory => inventory.Player is not null).OrderBy(inventory => inventory.Player.NetId))
        {
            var player = OptionLinks.IdOf(inventory.Player);
            if (player != session.Me)
            {
                var theirs = Slots(inventory).Where(item => item.Slot == slot).Select(item => item.Entry).FirstOrDefault();
                tips.AddRange(Peek(player, theirs, entry));
            }
        }

        return tips;
    }

    private static IEnumerable<IHoverTip> Peek(PlayerId player, MerchantEntry? theirs, MerchantEntry mine)
    {
        if (theirs is null)
        {
            return [PeekTips.About(player, "Has nothing in this slot.")];
        }

        if (!theirs.IsStocked)
        {
            return [PeekTips.About(player, "This slot is sold out in their shop.")];
        }

        var price = theirs.EnoughGold ? $"Costs them {theirs.Cost} gold." : $"Costs them {theirs.Cost} gold, more than they have.";
        return theirs switch
        {
            MerchantCardEntry { CreationResult.Card: { } card } =>
                PeekTips.Card(player, card, (mine as MerchantCardEntry)?.CreationResult?.Card, "Has", "in this slot", price),
            MerchantRelicEntry { Model: { } relic } => HoverTipFactory.FromRelic(relic).Take(1).Select(tip => PeekTips.Retitled(player, tip, price)),
            MerchantPotionEntry { Model: { } potion } => [PeekTips.Retitled(player, HoverTipFactory.FromPotion(potion), price)],
            _ => [],
        };
    }

    /// <summary>Enters the shop once the local player's stock is ready, with their gold and free potion slots.</summary>
    public static void EnterCurrent() => OptionLinks.Submit(EnterOps());

    /// <summary>
    /// The shop's join: once when the stock is ready, and again with a click (<paramref name="again"/>) until this
    /// peer has seen the host accept it. The host ignores a repeat.
    /// </summary>
    private static IReadOnlyList<GatedOp> EnterOps(bool again = false)
    {
        if (OptionLinks.Session is not { } session || TableId() is not { } table || (!again && Entered.Contains(table))
            || Inventory() is not { } inventory || OptionLinks.Me is not { } me)
        {
            return [];
        }

        var items = Slots(inventory).Select(slot => new ShopItem(slot.Slot, slot.Entry.Cost, slot.Entry.IsStocked)).ToList();
        var args = ShopTable.EnterArgs(me.Gold, FreePotionSlots(me), items);
        if (session.HasJoined(table, SyncActions.ShopEnter, args))
        {
            return [];
        }

        var reported = (me.Gold, FreePotionSlots(me));
        return [new GatedOp(table, SyncActions.ShopEnter, args, Sent: () =>
        {
            Entered.Add(table);
            _lastReported = reported;
        })];
    }

    /// <summary>
    /// Handles a click on a shop slot: sends it to the host as a vote or a first-click purchase. A purchase the host
    /// already decided for the local player but the mod couldn't make goes straight to the game.
    /// </summary>
    /// <returns>True when the game should act on the click as it is.</returns>
    public static bool Click(NMerchantSlot node)
    {
        if (OptionLinks.Session is not { } session || TableId() is not { } table || Inventory() is not { } inventory
            || node.Entry is not { } entry || SlotOf(inventory, entry) is not { } slot)
        {
            return true;
        }

        if (Shop(table) is { } shop)
        {
            if (Undone(shop, table, session.Me).FirstOrDefault(purchase => purchase.Slot == slot) is { } owed)
            {
                Done.Add($"{table}|{owed.Sequence}");
                MarkSource(table, owed);
                return true;
            }

            // When the game would refuse the purchase anyway, let it, so it shows why.
            if (shop.CanBuy(session.Me, slot).Verdict is PurchaseVerdict.NotEnoughGold or PurchaseVerdict.SoldOut or PurchaseVerdict.NoPotionSlot
                && (!entry.IsStocked || !entry.EnoughGold || (slot.Kind == ShopSlotKind.Potion && OptionLinks.Me?.HasOpenPotionSlots == false)))
            {
                return true;
            }
        }

        ClickGate.Request(
        [
            .. EnterOps(again: true),
            new GatedOp(table, SyncActions.ShopBuy, ShopTable.SlotArgs(slot), state =>
                state.Find<ShopTable>(table)?.Shop is { } linked && linked.IsIn(session.Me) ? ToCommitCheck(linked.CanBuy(session.Me, slot)) : null),
        ]);
        return false;
    }

    /// <summary>Checks for purchases to make after the linked state or the shop changed. Once per frame at most.</summary>
    public static void Schedule()
    {
        if (_scheduled)
        {
            return;
        }

        _scheduled = true;
        Callable.From(Run).CallDeferred();
    }

    /// <summary>Shows who wants or bought each slot, and dims slots mean mode greys out.</summary>
    public static void Refresh()
    {
        if (Room()?.Inventory is not { } rug || !GodotObject.IsInstanceValid(rug))
        {
            return;
        }

        if (OptionLinks.Session is not { } session || TableId() is not { } table || Shop(table) is not { } shop || rug.Inventory is not { } inventory)
        {
            return;
        }

        foreach (var node in rug.GetAllSlots())
        {
            if (!GodotObject.IsInstanceValid(node) || node.Entry is not { } entry || SlotOf(inventory, entry) is not { } slot)
            {
                continue;
            }

            Dim.Set(node, shop.CanBuy(session.Me, slot).Verdict == PurchaseVerdict.GreyedOut);
            var backers = shop.Shoppers
                .Where(player => shop.VotersFor(slot).Contains(player) || shop.Purchases.Any(purchase => purchase.Player == player && purchase.Slot == slot))
                .ToList();
            VoteIcons.Show(node, VoteIcons.BoundsOf(node, node.Hitbox), backers);
        }
    }

    /// <summary>
    /// Marks a purchase as the source of what follows: a card bought links with the cards the others bought from the
    /// same slot, and card removal's card screen links with the screens of everyone who bought removal.
    /// </summary>
    private static void MarkSource(string table, Purchase purchase)
    {
        CardLinksSync.GainingFrom(table, purchase.Slot.ToString(), purchase.Root);
        DeckChoices.Acting(table, purchase.Slot.ToString(), () =>
            Shop(table) is { } shop
                ? shop.Purchases.Where(other => other.Slot == purchase.Slot).Select(other => other.Player).Distinct().ToList()
                : []);
    }


    private static SoulLink.Core.Decisions.CommitCheck? ToCommitCheck(PurchaseCheck check) =>
        check.Verdict switch
        {
            PurchaseVerdict.Allowed => SoulLink.Core.Decisions.CommitCheck.Allowed,
            PurchaseVerdict.GreyedOut when check.Because is { } blocker => SoulLink.Core.Decisions.CommitCheck.GreyedOut(blocker),
            _ => SoulLink.Core.Decisions.CommitCheck.Unavailable,
        };

    private static int FreePotionSlots(Player player) => Math.Max(0, player.MaxPotionCount - player.Potions.Count());

    private static IEnumerable<Purchase> Undone(ShopSession shop, string table, PlayerId me) =>
        shop.Purchases.Where(purchase => purchase.Player == me && !Done.Contains($"{table}|{purchase.Sequence}"));

    private static void Run()
    {
        _scheduled = false;
        try
        {
            EnterCurrent();
            Pump();
            Refresh();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Soul Link couldn't make a linked purchase: {ex}");
        }
    }

    /// <summary>Makes the purchases the host decided for the local player, then reports their gold back.</summary>
    private static void Pump()
    {
        if (OptionLinks.Session is not { } session || TableId() is not { } table || Shop(table) is not { } shop
            || Inventory() is not { } inventory || OptionLinks.Me is not { } me)
        {
            return;
        }

        foreach (var purchase in Undone(shop, table, session.Me).ToList())
        {
            Done.Add($"{table}|{purchase.Sequence}");
            var entry = Slots(inventory).FirstOrDefault(slot => slot.Slot == purchase.Slot).Entry;
            if (entry is null)
            {
                MainFile.Logger.Warn($"Soul Link couldn't find {purchase.Slot} in your shop.");
                continue;
            }

            MainFile.Logger.Info($"Soul Link is buying {purchase.Slot}.");
            MarkSource(table, purchase);
            _buying++;
            TaskHelper.RunSafely(Buy(entry, inventory));
        }

        // Gold changes the game makes, like a purchase's real price, go back to the shop once nothing is being bought.
        if (_buying == 0 && (me.Gold, FreePotionSlots(me)) is var now && now != _lastReported
            && (shop.GoldOf(session.Me) != now.Item1 || shop.FreePotionSlotsOf(session.Me) != now.Item2))
        {
            _lastReported = now;
            session.Submit(table, SyncActions.ShopGold, ShopTable.GoldArgs(now.Item1, now.Item2));
        }
    }

    private static async Task Buy(MerchantEntry entry, MerchantInventory inventory)
    {
        try
        {
            // A linked card removal can't be backed out of: it was decided for everyone.
            var bought = entry is MerchantCardRemovalEntry removal
                ? await removal.OnTryPurchaseWrapper(inventory, cancelable: false)
                : await entry.OnTryPurchaseWrapper(inventory);
            if (!bought)
            {
                MainFile.Logger.Warn("Soul Link couldn't make a linked purchase in your shop.");
            }
        }
        finally
        {
            _buying--;
            Schedule();
        }
    }
}
