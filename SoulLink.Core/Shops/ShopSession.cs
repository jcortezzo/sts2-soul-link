using SoulLink.Core.Cards;

namespace SoulLink.Core.Shops;

/// <summary>
/// One shop visit by the party. Each slot is its own linked decision, and everyone buys from their own shop at their
/// own price.
/// </summary>
/// <remarks>
/// <para>
/// Nice: when anyone buys a slot on their own, everyone else who can buy that slot must buy it. The purchase is
/// owed until it's paid, and its price is set aside from the debtor's gold so later purchases can't make it
/// unaffordable: earlier purchases get first claim on gold.
/// </para>
/// <para>
/// Mean: a slot can only be bought on your own if everyone else can buy theirs too, so every purchase binds everyone.
/// </para>
/// <para>Not buying isn't a decision, so it binds no one.</para>
/// <para>
/// The Blitz merchant sells wares for no gold, but each visit allows only a few choices. The mod enters a player's
/// remaining choices as their gold and every ware at a price of one, so the same rules apply: an owed ware sets a
/// choice aside, and a player with no choices left can't buy.
/// </para>
/// <para>
/// Players act through <see cref="Choose"/>. With first click, a click buys the slot and pays what everyone else now
/// owes at once. With everyone agrees, a click is a vote, and the slot is bought once everyone who can buy it has
/// voted for it.
/// </para>
/// </remarks>
public sealed class ShopSession
{
    private readonly Dictionary<PlayerId, Shopper> _shoppers = new();
    private readonly List<PlayerId> _order = [];
    private readonly List<Purchase> _purchases = [];
    private readonly Dictionary<(PlayerId Player, ShopSlot Slot), int> _votes = new();
    private int _voteSequence;

    /// <param name="mode">Nice or mean, from the settings.</param>
    /// <param name="matching">How card removal targets are matched.</param>
    /// <param name="whoDecides">Whether everyone has to agree, or the first click decides.</param>
    public ShopSession(LinkMode mode, ICardMatchingStrategy matching, WhoDecides whoDecides = WhoDecides.EveryoneAgrees)
    {
        Mode = mode;
        WhoDecides = whoDecides;
        RemovalTarget = new CardChoiceDecision(matching, mode, 1, whoDecides);
    }

    public LinkMode Mode { get; }

    /// <summary>Whether a click is a vote that everyone has to agree on, or a purchase that decides at once.</summary>
    public WhoDecides WhoDecides { get; }

    /// <summary>The players in the shop, in the order they entered.</summary>
    public IReadOnlyList<PlayerId> Shoppers => _order;

    /// <summary>Every purchase in order.</summary>
    public IReadOnlyList<Purchase> Purchases => _purchases;

    /// <summary>
    /// Which card each removal buyer removes: the first buyer's card sets the target. Join each buyer's removal screen
    /// as soon as they buy or owe the removal, so mean mode can check every buyer's matching card.
    /// </summary>
    public CardChoiceDecision RemovalTarget { get; }

    /// <summary>Adds a player to the shop with their gold, free potion slots and stock.</summary>
    public void Enter(PlayerId player, int gold, int freePotionSlots, IEnumerable<ShopItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(gold);
        ArgumentOutOfRangeException.ThrowIfNegative(freePotionSlots);
        if (_shoppers.ContainsKey(player))
        {
            throw new ArgumentException($"{player} is already in the shop.", nameof(player));
        }

        var stock = new Dictionary<ShopSlot, ShopItem>();
        foreach (var item in items)
        {
            if (!stock.TryAdd(item.Slot, item))
            {
                throw new ArgumentException($"{item.Slot} is listed twice.", nameof(items));
            }
        }

        _shoppers[player] = new Shopper(gold, freePotionSlots, stock);
        _order.Add(player);
    }

    public int GoldOf(PlayerId player) => Get(player).Gold;

    /// <summary>The player's free potion slots, counting slots set aside for potions they owe.</summary>
    public int FreePotionSlotsOf(PlayerId player) => Get(player).FreePotionSlots;

    /// <summary>Whether the player has entered the shop.</summary>
    public bool IsIn(PlayerId player) => _shoppers.ContainsKey(player);

    /// <summary>
    /// Updates a player's gold from the game. Obligations the player can no longer pay are dropped, with earlier
    /// obligations keeping first claim on the gold.
    /// </summary>
    public void SetGold(PlayerId player, int gold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gold);
        var shopper = Get(player);
        shopper.Gold = gold;
        DropUnaffordable(shopper);
    }

    /// <summary>Updates a player's free potion slots from the game, dropping potion obligations that no longer fit.</summary>
    public void SetFreePotionSlots(PlayerId player, int freePotionSlots)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(freePotionSlots);
        var shopper = Get(player);
        shopper.FreePotionSlots = freePotionSlots;
        DropUnaffordable(shopper);
    }

    /// <summary>The purchases the player still owes, oldest first.</summary>
    public IReadOnlyList<Obligation> OwedBy(PlayerId player) => Get(player).Owed;

    /// <summary>Whether the player is done: they can leave once they owe nothing.</summary>
    public bool CanLeave(PlayerId player) => Get(player).Owed.Count == 0;

    public bool HasBought(PlayerId player, ShopSlot slot) => Get(player).Bought.Contains(slot);

    /// <summary>The slots the player has voted to buy, in vote order.</summary>
    public IReadOnlyList<ShopSlot> VotesOf(PlayerId player) =>
        _votes.Where(vote => vote.Key.Player == player).OrderBy(vote => vote.Value).Select(vote => vote.Key.Slot).ToList();

    /// <summary>The players who have voted to buy the slot, in vote order.</summary>
    public IReadOnlyList<PlayerId> VotersFor(ShopSlot slot) =>
        _votes.Where(vote => vote.Key.Slot == slot).OrderBy(vote => vote.Value).Select(vote => vote.Key.Player).ToList();

    /// <summary>
    /// A player's click on a slot. With first click, it buys the slot, then everyone who now owes it buys theirs. With
    /// everyone agrees, it becomes one of the player's votes, or takes the vote back if they already voted for the
    /// slot; the slot is bought once everyone who can buy it has voted for it.
    /// </summary>
    /// <returns>The purchases made, in order. Empty while a vote waits for the others.</returns>
    /// <exception cref="InvalidOperationException">The purchase isn't allowed; see <see cref="CanBuy"/>.</exception>
    public IReadOnlyList<Purchase> Choose(PlayerId player, ShopSlot slot)
    {
        var check = CanBuy(player, slot);
        if (!check.IsAllowed)
        {
            throw new InvalidOperationException($"{player} can't buy {slot}: {check}.");
        }

        if (WhoDecides == WhoDecides.FirstClick)
        {
            var purchases = new List<Purchase> { Buy(player, slot) };
            purchases.AddRange(Settle());
            return purchases;
        }

        if (!_votes.Remove((player, slot)))
        {
            _votes[(player, slot)] = ++_voteSequence;
        }

        return Settle();
    }

    /// <summary>
    /// Re-checks the shop after gold, potion slots or the party changed: first click pays any obligation that can be
    /// paid, and everyone agrees buys any slot that's now agreed.
    /// </summary>
    /// <returns>The purchases made, in order.</returns>
    public IReadOnlyList<Purchase> Settle()
    {
        var purchases = new List<Purchase>();
        while (true)
        {
            var made = WhoDecides == WhoDecides.FirstClick ? PayObligations() : BuyAgreedSlot();
            if (made.Count == 0)
            {
                return purchases;
            }

            purchases.AddRange(made);
        }
    }

    /// <summary>Checks whether the player may buy the slot now.</summary>
    public PurchaseCheck CanBuy(PlayerId player, ShopSlot slot)
    {
        if (!_shoppers.TryGetValue(player, out var shopper))
        {
            return new PurchaseCheck(PurchaseVerdict.NotInShop);
        }

        var own = Affordability(shopper, slot);
        if (own != PurchaseVerdict.Allowed)
        {
            return new PurchaseCheck(own);
        }

        if (Mode == LinkMode.Mean && !shopper.Owes(slot))
        {
            foreach (var other in _order)
            {
                var otherShopper = _shoppers[other];
                if (other != player && !IsSettled(otherShopper, slot) && Affordability(otherShopper, slot) != PurchaseVerdict.Allowed)
                {
                    return PurchaseCheck.GreyedOut(other);
                }
            }
        }

        return PurchaseCheck.Allowed;
    }

    /// <summary>Records a purchase and the obligations it creates.</summary>
    /// <exception cref="InvalidOperationException">The purchase isn't allowed; see <see cref="CanBuy"/>.</exception>
    public Purchase Buy(PlayerId player, ShopSlot slot)
    {
        var check = CanBuy(player, slot);
        if (!check.IsAllowed)
        {
            throw new InvalidOperationException($"{player} can't buy {slot}: {check}.");
        }

        var shopper = _shoppers[player];
        var item = shopper.Items[slot];
        var settled = shopper.Owed.Find(obligation => obligation.Slot == slot);
        if (settled is not null)
        {
            shopper.Owed.Remove(settled);
        }

        shopper.Gold -= item.Price;
        if (slot.Kind == ShopSlotKind.Potion)
        {
            shopper.FreePotionSlots--;
        }

        shopper.Bought.Add(slot);
        var sequence = _purchases.Count + 1;
        var created = new List<Obligation>();
        if (settled is null)
        {
            foreach (var other in _order)
            {
                var otherShopper = _shoppers[other];
                if (other != player && !IsSettled(otherShopper, slot) && Affordability(otherShopper, slot) == PurchaseVerdict.Allowed)
                {
                    var obligation = new Obligation(other, slot, otherShopper.Items[slot].Price, player, sequence);
                    otherShopper.Owed.Add(obligation);
                    created.Add(obligation);
                }
            }
        }

        var purchase = new Purchase(sequence, player, slot, item.Price, settled is not null, settled?.Sequence ?? sequence, created);
        _purchases.Add(purchase);
        return purchase;
    }

    private static bool IsSettled(Shopper shopper, ShopSlot slot) => shopper.Bought.Contains(slot) || shopper.Owes(slot);

    /// <summary>First click: buys everything players owe that they can pay now, oldest obligation first.</summary>
    private List<Purchase> PayObligations()
    {
        var purchases = new List<Purchase>();
        foreach (var obligation in _order.SelectMany(player => _shoppers[player].Owed).OrderBy(obligation => obligation.Sequence).ToList())
        {
            if (CanBuy(obligation.Debtor, obligation.Slot).IsAllowed)
            {
                purchases.Add(Buy(obligation.Debtor, obligation.Slot));
            }
        }

        return purchases;
    }

    /// <summary>
    /// Everyone agrees: buys the first slot, in vote order, that everyone who can buy it has voted for. The first
    /// voter's purchase leads, so the others pay what it makes them owe. Votes that stopped being possible, for
    /// example because the player spent their gold, are dropped.
    /// </summary>
    private List<Purchase> BuyAgreedSlot()
    {
        foreach (var ((voter, slot), _) in _votes.OrderBy(vote => vote.Value).ToList())
        {
            if (!CanBuy(voter, slot).IsAllowed)
            {
                _votes.Remove((voter, slot));
                continue;
            }

            var buyers = _order.Where(player => Affordability(_shoppers[player], slot) == PurchaseVerdict.Allowed).ToList();
            if (!buyers.TrueForAll(player => _votes.ContainsKey((player, slot))))
            {
                continue;
            }

            var purchases = new List<Purchase>();
            foreach (var buyer in buyers.OrderBy(player => _votes[(player, slot)]))
            {
                _votes.Remove((buyer, slot));
                purchases.Add(Buy(buyer, slot));
            }

            return purchases;
        }

        return [];
    }

    /// <summary>Whether the shopper could buy the slot, setting aside gold and potion slots for everything else they owe.</summary>
    private static PurchaseVerdict Affordability(Shopper shopper, ShopSlot slot)
    {
        if (!shopper.Items.TryGetValue(slot, out var item))
        {
            return PurchaseVerdict.NotOffered;
        }

        if (shopper.Bought.Contains(slot))
        {
            return PurchaseVerdict.AlreadyBought;
        }

        if (!item.InStock)
        {
            return PurchaseVerdict.SoldOut;
        }

        var reservedGold = 0;
        var reservedPotionSlots = 0;
        foreach (var obligation in shopper.Owed)
        {
            if (obligation.Slot != slot)
            {
                reservedGold += obligation.Price;
                reservedPotionSlots += obligation.Slot.Kind == ShopSlotKind.Potion ? 1 : 0;
            }
        }

        if (shopper.Gold - reservedGold < item.Price)
        {
            return PurchaseVerdict.NotEnoughGold;
        }

        if (slot.Kind == ShopSlotKind.Potion && shopper.FreePotionSlots - reservedPotionSlots < 1)
        {
            return PurchaseVerdict.NoPotionSlot;
        }

        return PurchaseVerdict.Allowed;
    }

    /// <summary>Keeps obligations in order while they still fit, so earlier purchases keep first claim.</summary>
    private static void DropUnaffordable(Shopper shopper)
    {
        var gold = shopper.Gold;
        var potionSlots = shopper.FreePotionSlots;
        var kept = new List<Obligation>();
        foreach (var obligation in shopper.Owed)
        {
            var isPotion = obligation.Slot.Kind == ShopSlotKind.Potion;
            if (obligation.Price <= gold && (!isPotion || potionSlots > 0))
            {
                gold -= obligation.Price;
                potionSlots -= isPotion ? 1 : 0;
                kept.Add(obligation);
            }
        }

        shopper.Owed.Clear();
        shopper.Owed.AddRange(kept);
    }

    private Shopper Get(PlayerId player) =>
        _shoppers.TryGetValue(player, out var shopper)
            ? shopper
            : throw new ArgumentException($"{player} isn't in the shop.", nameof(player));

    private sealed class Shopper(int gold, int freePotionSlots, Dictionary<ShopSlot, ShopItem> items)
    {
        public int Gold { get; set; } = gold;

        public int FreePotionSlots { get; set; } = freePotionSlots;

        public Dictionary<ShopSlot, ShopItem> Items { get; } = items;

        public HashSet<ShopSlot> Bought { get; } = [];

        public List<Obligation> Owed { get; } = [];

        public bool Owes(ShopSlot slot) => Owed.Exists(obligation => obligation.Slot == slot);
    }
}
