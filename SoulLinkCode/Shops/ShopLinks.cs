using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using SoulLink.Core;
using SoulLink.Core.Shops;
using SoulLink.Core.Sync;
using SoulLink.SoulLinkCode.Cards;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Shops;

/// <summary>
/// Links shop purchases slot by slot: each card, relic and potion slot and the card removal service, or each of the
/// Blitz merchant's wares. A click is a vote to buy or a first-click purchase; the mod buys from the local player's own
/// shop, at their own price, whatever the host decides they buy.
/// </summary>
/// <remarks>
/// The game keeps every player's stock in its merchant sync and makes each purchase on every peer, one purchase at a
/// time per player, so the mod buys through it, one owed purchase after another.
/// </remarks>
internal static class ShopLinks
{
    private static readonly AccessTools.FieldRef<MerchantSynchronizer, List<MerchantInventory>> InventoriesOf =
        AccessTools.FieldRefAccess<MerchantSynchronizer, List<MerchantInventory>>("_inventories");

    private static readonly AccessTools.FieldRef<MerchantInventory, bool> PurchaseInFlight =
        AccessTools.FieldRefAccess<MerchantInventory, bool>("_purchaseInFlight");

    /// <summary>Purchases done in the game, as "table|sequence", so each linked purchase is made once.</summary>
    private static readonly HashSet<string> Done = new(StringComparer.Ordinal);

    private static readonly HashSet<string> Entered = new(StringComparer.Ordinal);
    private static int _buying;
    private static (int Gold, int PotionSlots)? _lastReported;
    private static bool _scheduled;
    private static bool _checkingLater;

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

    /// <summary>The local player's stock in the shop they're in.</summary>
    public static MerchantInventory? Inventory() => OptionLinks.Me is { } me ? InventoryOf(me) : null;

    /// <summary>A player's stock in the shop the local player is in, or null outside a shop.</summary>
    public static MerchantInventory? InventoryOf(Player player)
    {
        if (Room() is null || RunManager.Instance?.MerchantSynchronizer is not { } merchant)
        {
            return null;
        }

        var inventories = InventoriesOf(merchant);
        var index = player.RunState.GetPlayerSlotIndex(player);
        return inventories is not null && index >= 0 && index < inventories.Count ? inventories[index] : null;
    }

    /// <summary>
    /// What the player can spend in their shop: gold, or at the Blitz merchant, where wares cost no gold, the choices
    /// they have left.
    /// </summary>
    private static int Budget(Player player, MerchantInventory inventory) =>
        inventory.IsBlitzShop ? Math.Max(0, inventory.BlitzChoicesRemaining) : player.Gold;

    /// <summary>A slot's price against <see cref="Budget"/>: a Blitz ware uses one choice.</summary>
    private static int PriceOf(MerchantEntry entry) => entry is BlitzMerchantEntry ? 1 : entry.Cost;

    public static string? TableId() => OptionLinks.Room is { } room ? $"{room}/shop" : null;

    /// <summary>The shop's linked state, once the local player has entered it.</summary>
    public static ShopSession? Shop(string table) =>
        OptionLinks.Session is { } session && session.State.Find<ShopTable>(table)?.Shop is { } shop && shop.IsIn(session.Me) ? shop : null;

    /// <summary>
    /// The slot an entry sits in: cards count across the character and colorless rows, then relics, potions, removal
    /// and the Blitz merchant's wares.
    /// </summary>
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

        for (var i = 0; i < inventory.BlitzEntries.Count; i++)
        {
            if (ReferenceEquals(inventory.BlitzEntries[i], entry))
            {
                return ShopSlot.Ware(i + 1);
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

        for (var i = 0; i < inventory.BlitzEntries.Count; i++)
        {
            yield return (ShopSlot.Ware(i + 1), inventory.BlitzEntries[i]);
        }
    }

    /// <summary>
    /// Slot peek in the shop: each teammate's item in the hovered slot, with their price. Every player's stock is
    /// known on every machine, so nothing needs sharing.
    /// </summary>
    public static IReadOnlyList<IHoverTip> Peek(NMerchantSlot node)
    {
        if (OptionLinks.Session is not { } session
            || OptionLinks.Me is not { } me
            || Inventory() is not { } mine
            || node.Entry is not { } entry
            || SlotOf(mine, entry) is not { } slot
            || slot == ShopSlot.CardRemoval)
        {
            return [];
        }

        var tips = new List<IHoverTip>();
        foreach (var teammate in me.RunState.Players.Where(player => !ReferenceEquals(player, me)).OrderBy(player => player.NetId))
        {
            if (InventoryOf(teammate) is { } inventory)
            {
                var theirs = Slots(inventory).Where(item => item.Slot == slot).Select(item => item.Entry).FirstOrDefault();
                tips.AddRange(Peek(OptionLinks.IdOf(teammate), theirs, entry));
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

        if (theirs is BlitzMerchantEntry ware)
        {
            var used = ware.IsStocked ? null : "\nThey can't choose it any more.";
            return [PeekTips.About(player, (PeekTips.Text(ware.Description) ?? "") + used, PeekTips.Text(ware.Title), ware.SmallIcon)];
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

        var items = Slots(inventory).Select(slot => new ShopItem(slot.Slot, PriceOf(slot.Entry), slot.Entry.IsStocked)).ToList();
        var budget = Budget(me, inventory);
        var args = ShopTable.EnterArgs(budget, FreePotionSlots(me), items);
        if (session.HasJoined(table, SyncActions.ShopEnter, args))
        {
            return [];
        }

        var reported = (budget, FreePotionSlots(me));
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
                // The game makes one purchase at a time, so while one is under way the owed purchase waits its turn.
                if (_buying > 0 || PurchaseInFlight(inventory))
                {
                    Schedule();
                    return false;
                }

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

    /// <summary>Shows who wants or bought each slot, and dims the slots the local player can't buy right now.</summary>
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

            // The game only turns an unaffordable price red. Dimmed: mean mode's greyed-out slots, and slots the player
            // can't pay for or has no potion slot for, counting gold and slots set aside for purchases they owe.
            var verdict = shop.CanBuy(session.Me, slot).Verdict;
            Dim.Set(node, !entry.EnoughGold || verdict is PurchaseVerdict.GreyedOut or PurchaseVerdict.NotEnoughGold or PurchaseVerdict.NoPotionSlot);
            var backers = shop.Shoppers
                .Where(player => shop.VotersFor(slot).Contains(player) || shop.Purchases.Any(purchase => purchase.Player == player && purchase.Slot == slot))
                .ToList();
            VoteIcons.Show(node, VoteIcons.BoundsOf(node, node.Hitbox), backers);
        }
    }

    /// <summary>
    /// Marks a purchase as the source of what follows: a card bought links with the cards the others bought from the
    /// same slot, and a card screen the purchase opens, like card removal's or a Blitz ware's, links with the screens of
    /// everyone who bought that slot and can't be backed out of.
    /// </summary>
    private static void MarkSource(string table, Purchase purchase)
    {
        CardLinksSync.GainingFrom(table, purchase.Slot.ToString(), purchase.Root);
        DeckChoices.Acting(
            table,
            purchase.Slot.ToString(),
            () => Shop(table) is { } shop
                ? shop.Purchases.Where(other => other.Slot == purchase.Slot).Select(other => other.Player).Distinct().ToList()
                : [],
            binding: true);
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

    /// <summary>
    /// Makes the purchases the host decided for the local player, one at a time and oldest first, then reports their
    /// gold or remaining choices back.
    /// </summary>
    private static void Pump()
    {
        if (OptionLinks.Session is not { } session || TableId() is not { } table || Shop(table) is not { } shop
            || Inventory() is not { } inventory || OptionLinks.Me is not { } me)
        {
            return;
        }

        // The game makes one purchase at a time, including one the player started themselves.
        if (_buying == 0 && PurchaseInFlight(inventory) && Undone(shop, table, session.Me).Any())
        {
            CheckLater();
        }
        else if (_buying == 0)
        {
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
                TaskHelper.RunSafely(Buy(entry));
                break;
            }
        }

        // Changes the game makes, like a purchase's real price or a ware that gives more choices, go back to the shop
        // once nothing is being bought.
        if (_buying == 0 && (Budget(me, inventory), FreePotionSlots(me)) is var now && now != _lastReported
            && (shop.GoldOf(session.Me) != now.Item1 || shop.FreePotionSlotsOf(session.Me) != now.Item2))
        {
            _lastReported = now;
            session.Submit(table, SyncActions.ShopGold, ShopTable.GoldArgs(now.Item1, now.Item2));
        }
    }

    /// <summary>Buys through the game's merchant sync, so every peer makes the same purchase.</summary>
    private static async Task Buy(MerchantEntry entry)
    {
        try
        {
            if (RunManager.Instance?.MerchantSynchronizer is not { } merchant)
            {
                MainFile.Logger.Warn("Soul Link couldn't reach the shop to make a linked purchase.");
                return;
            }

            // A linked card removal can't be backed out of: it was decided for everyone.
            if (entry is MerchantCardRemovalEntry removal)
            {
                removal.SetPurchaseUncancelable();
            }

            if (!await merchant.PurchaseLocalEntry(entry))
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

    /// <summary>Looks again shortly, while a purchase the player started is still under way.</summary>
    private static void CheckLater()
    {
        if (_checkingLater || Engine.GetMainLoop() is not SceneTree tree)
        {
            return;
        }

        _checkingLater = true;
        tree.CreateTimer(0.25).Timeout += () =>
        {
            _checkingLater = false;
            Schedule();
        };
    }
}
