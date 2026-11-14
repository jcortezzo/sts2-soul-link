namespace SoulLink.Core.Shops;

public enum ShopSlotKind
{
    Card,
    Relic,
    Potion,
    CardRemoval,

    /// <summary>One of the Blitz merchant's wares, like a card removal or a card reward, counted in shop order.</summary>
    Ware,
}

/// <summary>
/// A position in the shop layout. Every player's shop has the same slots with different stock and prices, and the
/// same slot is the same linked decision.
/// </summary>
public readonly record struct ShopSlot(ShopSlotKind Kind, int Number)
{
    /// <summary>The card removal service. There's one per shop.</summary>
    public static ShopSlot CardRemoval { get; } = new(ShopSlotKind.CardRemoval, 1);

    /// <summary>A card slot, counted from 1.</summary>
    public static ShopSlot Card(int number) => Numbered(ShopSlotKind.Card, number);

    /// <summary>A relic slot, counted from 1.</summary>
    public static ShopSlot Relic(int number) => Numbered(ShopSlotKind.Relic, number);

    /// <summary>A potion slot, counted from 1.</summary>
    public static ShopSlot Potion(int number) => Numbered(ShopSlotKind.Potion, number);

    /// <summary>A Blitz merchant ware, counted from 1.</summary>
    public static ShopSlot Ware(int number) => Numbered(ShopSlotKind.Ware, number);

    public override string ToString() => Kind == ShopSlotKind.CardRemoval ? "card removal" : $"{Kind} {Number}";

    private static ShopSlot Numbered(ShopSlotKind kind, int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        return new ShopSlot(kind, number);
    }
}

/// <summary>What one player's shop has in a slot, at that player's price.</summary>
public readonly record struct ShopItem(ShopSlot Slot, int Price, bool InStock = true);

/// <summary>Why a player can or can't buy a slot.</summary>
public enum PurchaseVerdict
{
    Allowed,
    NotInShop,
    NotOffered,
    SoldOut,
    AlreadyBought,

    /// <summary>Not enough gold, counting gold set aside for purchases the player owes.</summary>
    NotEnoughGold,

    /// <summary>No free potion slot, counting slots set aside for potions the player owes.</summary>
    NoPotionSlot,

    /// <summary>Mean mode: another player can't buy the same slot.</summary>
    GreyedOut,
}

/// <param name="Verdict">Allowed, or why not.</param>
/// <param name="Because">For <see cref="PurchaseVerdict.GreyedOut"/>, a player who can't buy the slot.</param>
public readonly record struct PurchaseCheck(PurchaseVerdict Verdict, PlayerId? Because = null)
{
    public static PurchaseCheck Allowed { get; } = new(PurchaseVerdict.Allowed);

    public bool IsAllowed => Verdict == PurchaseVerdict.Allowed;

    public static PurchaseCheck GreyedOut(PlayerId blocker) => new(PurchaseVerdict.GreyedOut, blocker);

    public override string ToString() => Because is { } player ? $"{Verdict} ({player})" : Verdict.ToString();
}

/// <summary>A purchase a player owes because another player bought the same slot.</summary>
/// <param name="Debtor">Who has to buy.</param>
/// <param name="Slot">What they have to buy.</param>
/// <param name="Price">The debtor's price for it, set aside from their gold.</param>
/// <param name="Because">Whose purchase created the obligation.</param>
/// <param name="Sequence">The sequence of that purchase. Earlier obligations get first claim on gold.</param>
public sealed record Obligation(PlayerId Debtor, ShopSlot Slot, int Price, PlayerId Because, int Sequence);

/// <summary>A completed purchase.</summary>
/// <param name="Sequence">1-based order of purchases in this shop.</param>
/// <param name="SettledObligation">True when the player was paying off an obligation, which binds no one.</param>
/// <param name="Root">
/// The sequence of the purchase that decided this one: the purchase that created the obligation, or this purchase.
/// Cards bought with the same slot and root link together.
/// </param>
/// <param name="NewObligations">Purchases other players now owe.</param>
public sealed record Purchase(
    int Sequence,
    PlayerId Player,
    ShopSlot Slot,
    int Price,
    bool SettledObligation,
    int Root,
    IReadOnlyList<Obligation> NewObligations);
