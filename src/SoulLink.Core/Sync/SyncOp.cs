namespace SoulLink.Core.Sync;

/// <summary>
/// An operation on a synced table: a player joining a decision, committing, buying and so on. Players send
/// operations to the host, which orders them, so every peer applies the same operations in the same order.
/// </summary>
/// <param name="Table">The table id, built the same way on every peer, for example <c>f12/reward/1</c>.</param>
/// <param name="Player">The player the operation is for. Players may only submit operations for themselves.</param>
/// <param name="Action">What to do, from <see cref="SyncActions"/>.</param>
/// <param name="Args">The action's arguments as text.</param>
public sealed record SyncOp(string Table, PlayerId Player, string Action, IReadOnlyList<string> Args)
{
    public bool Equals(SyncOp? other) =>
        other is not null
        && string.Equals(Table, other.Table, StringComparison.Ordinal)
        && Player == other.Player
        && string.Equals(Action, other.Action, StringComparison.Ordinal)
        && Args.SequenceEqual(other.Args, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Table, Player, Action, Args.Count);

    public override string ToString() => $"{Table} {Action} by {Player} [{string.Join(", ", Args)}]";
}

/// <summary>An operation the host accepted, with its place in the global order.</summary>
/// <param name="Sequence">1-based position in the host's order. Clients apply notices in this order.</param>
/// <param name="Op">The accepted operation.</param>
/// <param name="Result">
/// What the host worked out while applying it, such as a commit's link, so clients replay exactly what the host did.
/// </param>
public sealed record SyncNotice(long Sequence, SyncOp Op, IReadOnlyList<string> Result)
{
    public bool Equals(SyncNotice? other) =>
        other is not null && Sequence == other.Sequence && Op.Equals(other.Op) && Result.SequenceEqual(other.Result, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Sequence, Op);
}

/// <summary>An operation the host refused, and why. Only the player who submitted it needs to act on it.</summary>
public sealed record SyncRejection(SyncOp Op, string Reason);

/// <summary>Thrown by a table when the host should refuse an operation.</summary>
public sealed class SyncRejectedException : Exception
{
    public SyncRejectedException()
    {
    }

    public SyncRejectedException(string message)
        : base(message)
    {
    }

    public SyncRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The actions tables understand. The part before the dot names the kind of table. A choose action is a player's
/// click: with first click it commits at once, and with everyone agrees it's a vote. The host's notice for any action
/// on a linked decision carries the commits that followed from it, for every player.
/// </summary>
public static class SyncActions
{
    /// <summary>Host only: starts the run with the host's settings. Args: card matching, mode, shared death, who decides.</summary>
    public const string RunStart = "run.start";

    /// <summary>Joins an option decision. Args: pick count, then key and availability pairs.</summary>
    public const string OptionsJoin = "options.join";

    /// <summary>Replaces the player's options. Args: key and availability pairs.</summary>
    public const string OptionsUpdate = "options.update";

    /// <summary>Clicks options. Args: the keys.</summary>
    public const string OptionsChoose = "options.choose";

    /// <summary>Leaves an option decision.</summary>
    public const string OptionsLeave = "options.leave";

    /// <summary>Joins a card choice. Args: pick count, then card id and eligibility pairs in screen order.</summary>
    public const string CardsJoin = "cards.join";

    /// <summary>Clicks cards. Args: the card ids.</summary>
    public const string CardsChoose = "cards.choose";

    /// <summary>Leaves a card choice.</summary>
    public const string CardsLeave = "cards.leave";

    /// <summary>Joins a card reward screen. Args: screen number, card count, can reroll, can sacrifice, then extra sources.</summary>
    public const string RewardJoin = "reward.join";

    /// <summary>Clicks a card reward option. Args: the option key.</summary>
    public const string RewardChoose = "reward.choose";

    /// <summary>Rerolls a card reward screen. Args: the new offer, as for <see cref="RewardJoin"/>.</summary>
    public const string RewardReroll = "reward.reroll";

    /// <summary>Enters a shop. Args: gold, free potion slots, then slot kind, number, price and stock for each item.</summary>
    public const string ShopEnter = "shop.enter";

    /// <summary>Clicks a slot to buy it. Args: slot kind and number.</summary>
    public const string ShopBuy = "shop.buy";

    /// <summary>Updates gold and free potion slots from the game. Args: gold, free potion slots.</summary>
    public const string ShopGold = "shop.gold";

    /// <summary>Joins the card removal target choice. Args: card id and eligibility pairs in screen order.</summary>
    public const string ShopRemovalJoin = "shop.removal-join";

    /// <summary>Clicks the card to remove. Args: the card id.</summary>
    public const string ShopRemovalChoose = "shop.removal-choose";

    /// <summary>
    /// Arrives at a campfire. Args: has Miniature Tent, option count, option id and availability pairs, then card id
    /// and eligibility pairs for the upgrade screen.
    /// </summary>
    public const string CampfireJoin = "campfire.join";

    /// <summary>Clicks a campfire option. Args: the option id.</summary>
    public const string CampfireChoose = "campfire.choose";

    /// <summary>Clicks the card to Smith. Args: the card id.</summary>
    public const string CampfireSmith = "campfire.smith";
}
