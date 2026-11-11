using SoulLink.Core.Campfires;
using SoulLink.Core.Cards;
using SoulLink.Core.Decisions;
using SoulLink.Core.Rewards;
using SoulLink.Core.Shops;

namespace SoulLink.Core.Sync;

/// <summary>Linked state that operations act on: one decision, card reward screen, shop or campfire.</summary>
public interface ISyncTable
{
    string Id { get; }

    /// <summary>On the host: checks the operation, applies it and returns the result to send with the notice.</summary>
    /// <exception cref="SyncRejectedException">The operation isn't allowed.</exception>
    IReadOnlyList<string> Execute(SyncOp op);

    /// <summary>On a client: applies an operation the host accepted, with the host's result.</summary>
    void Replay(SyncOp op, IReadOnlyList<string> result);
}

/// <summary>Shared handling for tables, so the host and clients run the same code.</summary>
public abstract class SyncTable : ISyncTable
{
    protected SyncTable(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; }

    public IReadOnlyList<string> Execute(SyncOp op) => Handle(op, replay: null);

    public void Replay(SyncOp op, IReadOnlyList<string> result) => Handle(op, result);

    /// <summary>Applies an operation.</summary>
    /// <param name="op">The operation.</param>
    /// <param name="replay">The host's result when a client replays the operation; null when the host executes it.</param>
    /// <returns>The result to send with the notice.</returns>
    protected abstract IReadOnlyList<string> Handle(SyncOp op, IReadOnlyList<string>? replay);

    protected static SyncRejectedException Unknown(SyncOp op) => new($"{op.Action} doesn't apply to this table.");

    /// <summary>Commits on the host, or applies the host's exact commit on a client.</summary>
    protected static IReadOnlyList<string> CommitOrApply<TAtom>(
        LinkedChoice<TAtom> decision, SyncOp op, Selection<TAtom> choice, IReadOnlyList<string>? replay)
        where TAtom : notnull
    {
        if (replay is not null)
        {
            decision.Apply(CommitCodec.Decode(op.Player, choice, replay));
            return replay;
        }

        var check = decision.CanCommit(op.Player, choice);
        if (!check.IsAllowed)
        {
            throw new SyncRejectedException(check.ToString());
        }

        return CommitCodec.Encode(decision.Commit(op.Player, choice));
    }

    internal static IEnumerable<string> ScreenArgs(IEnumerable<ScreenCard> screen) =>
        screen.SelectMany(card => new[] { SyncArgs.Number(card.Card.Value), SyncArgs.Flag(card.Eligible) });

    internal static List<ScreenCard> ReadScreen(ArgReader args)
    {
        var cards = new List<ScreenCard>();
        while (args.HasMore)
        {
            cards.Add(new ScreenCard(new CardId(args.Long()), args.Bool()));
        }

        return cards;
    }

    internal static Selection<CardId> ReadCards(ArgReader args) => new(args.Rest().Select(ParseCard));

    internal static string Key(ArgReader args)
    {
        var key = args.String();
        return key.Length > 0 ? key : throw new SyncRejectedException("An option key is empty.");
    }

    private static CardId ParseCard(string text) =>
        long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var id)
            ? new CardId(id)
            : throw new SyncRejectedException("A card id isn't a number.");
}

/// <summary>A decision matched by option key: events, Ancients, relic rewards, chests.</summary>
public sealed class OptionTable : SyncTable
{
    public OptionTable(string id, LinkMode mode, int pickCount)
        : base(id)
    {
        Decision = new OptionDecision(mode, pickCount);
    }

    public OptionDecision Decision { get; }

    /// <summary>Arguments for <see cref="SyncActions.OptionsJoin"/>.</summary>
    public static IReadOnlyList<string> JoinArgs(int pickCount, IEnumerable<DecisionOption> options) =>
        [SyncArgs.Number(pickCount), .. OptionArgs(options)];

    /// <summary>Arguments for <see cref="SyncActions.OptionsUpdate"/>.</summary>
    public static IReadOnlyList<string> OptionArgs(IEnumerable<DecisionOption> options) =>
        options.SelectMany(option => new[] { option.Key.Value, SyncArgs.Flag(option.Available) }).ToList();

    protected override IReadOnlyList<string> Handle(SyncOp op, IReadOnlyList<string>? replay)
    {
        var args = new ArgReader(op.Args);
        switch (op.Action)
        {
            case SyncActions.OptionsJoin:
                if (args.Int() != Decision.PickCount)
                {
                    throw new SyncRejectedException("The pick count doesn't match.");
                }

                Decision.Join(op.Player, ReadOptions(args));
                return [];
            case SyncActions.OptionsUpdate:
                Decision.UpdateOptions(op.Player, ReadOptions(args));
                return [];
            case SyncActions.OptionsCommit:
                return CommitOrApply(Decision, op, new Selection<OptionKey>(args.Rest().Select(key => new OptionKey(key))), replay);
            case SyncActions.OptionsLeave:
                args.End();
                Decision.RemoveParticipant(op.Player);
                return [];
            default:
                throw Unknown(op);
        }
    }

    private static List<DecisionOption> ReadOptions(ArgReader args)
    {
        var options = new List<DecisionOption>();
        while (args.HasMore)
        {
            options.Add(new DecisionOption(new OptionKey(Key(args)), args.Bool()));
        }

        return options;
    }
}

/// <summary>A choice of cards from players' decks for the same action.</summary>
public sealed class CardTable : SyncTable
{
    public CardTable(string id, ICardMatchingStrategy matching, LinkMode mode, int pickCount)
        : base(id)
    {
        Decision = new CardChoiceDecision(matching, mode, pickCount);
    }

    public CardChoiceDecision Decision { get; }

    /// <summary>Arguments for <see cref="SyncActions.CardsJoin"/>: pick count, then the screen in order.</summary>
    public static IReadOnlyList<string> JoinArgs(int pickCount, IEnumerable<ScreenCard> screen) =>
        [SyncArgs.Number(pickCount), .. ScreenArgs(screen)];

    protected override IReadOnlyList<string> Handle(SyncOp op, IReadOnlyList<string>? replay)
    {
        var args = new ArgReader(op.Args);
        switch (op.Action)
        {
            case SyncActions.CardsJoin:
                if (args.Int() != Decision.PickCount)
                {
                    throw new SyncRejectedException("The pick count doesn't match.");
                }

                Decision.Join(new SelectionScreen(op.Player, ReadScreen(args)));
                return [];
            case SyncActions.CardsCommit:
                return CommitOrApply(Decision, op, ReadCards(args), replay);
            case SyncActions.CardsLeave:
                args.End();
                Decision.RemoveParticipant(op.Player);
                return [];
            default:
                throw Unknown(op);
        }
    }
}

/// <summary>One card reward screen, with linked Driftwood rerolls.</summary>
public sealed class RewardTable : SyncTable
{
    public RewardTable(string id, int number, LinkMode mode)
        : base(id)
    {
        Screen = new CardRewardScreen(number, mode);
    }

    public CardRewardScreen Screen { get; }

    /// <summary>Arguments for <see cref="SyncActions.RewardJoin"/>: the screen number, then the offer.</summary>
    public static IReadOnlyList<string> JoinArgs(int number, CardRewardOffer offer) => [SyncArgs.Number(number), .. OfferArgs(offer)];

    /// <summary>Arguments for <see cref="SyncActions.RewardReroll"/>: the new offer.</summary>
    public static IReadOnlyList<string> OfferArgs(CardRewardOffer offer) =>
        [SyncArgs.Number(offer.Cards), SyncArgs.Flag(offer.CanReroll), .. offer.Extras ?? []];

    protected override IReadOnlyList<string> Handle(SyncOp op, IReadOnlyList<string>? replay)
    {
        var args = new ArgReader(op.Args);
        switch (op.Action)
        {
            case SyncActions.RewardJoin:
                if (args.Int() != Screen.Number)
                {
                    throw new SyncRejectedException("The screen number doesn't match.");
                }

                Screen.Join(op.Player, ReadOffer(args));
                return [];
            case SyncActions.RewardCommit:
                var option = new OptionKey(Key(args));
                args.End();
                if (replay is not null)
                {
                    Screen.Decision.Apply(CommitCodec.Decode(op.Player, Selection.Of(option), replay));
                    return replay;
                }

                var check = Screen.CanCommit(op.Player, option);
                return check.IsAllowed
                    ? CommitCodec.Encode(Screen.Commit(op.Player, option))
                    : throw new SyncRejectedException(check.ToString());
            case SyncActions.RewardReroll:
                Screen.Reroll(op.Player, ReadOffer(args));
                return [];
            default:
                throw Unknown(op);
        }
    }

    private static CardRewardOffer ReadOffer(ArgReader args)
    {
        var cards = args.Int();
        if (cards < 0)
        {
            throw new SyncRejectedException("The card count is negative.");
        }

        var canReroll = args.Bool();
        var extras = args.Rest();
        return new CardRewardOffer(cards, extras, canReroll);
    }
}

/// <summary>One shop visit by the party.</summary>
public sealed class ShopTable : SyncTable
{
    public ShopTable(string id, LinkMode mode, ICardMatchingStrategy matching)
        : base(id)
    {
        Shop = new ShopSession(mode, matching);
    }

    public ShopSession Shop { get; }

    /// <summary>Arguments for <see cref="SyncActions.ShopEnter"/>.</summary>
    public static IReadOnlyList<string> EnterArgs(int gold, int freePotionSlots, IEnumerable<ShopItem> items) =>
    [
        SyncArgs.Number(gold),
        SyncArgs.Number(freePotionSlots),
        .. items.SelectMany(item => new[]
        {
            item.Slot.Kind.ToString(), SyncArgs.Number(item.Slot.Number), SyncArgs.Number(item.Price), SyncArgs.Flag(item.InStock),
        }),
    ];

    /// <summary>Arguments for <see cref="SyncActions.ShopBuy"/>.</summary>
    public static IReadOnlyList<string> SlotArgs(ShopSlot slot) => [slot.Kind.ToString(), SyncArgs.Number(slot.Number)];

    /// <summary>Arguments for <see cref="SyncActions.ShopGold"/>.</summary>
    public static IReadOnlyList<string> GoldArgs(int gold, int freePotionSlots) =>
        [SyncArgs.Number(gold), SyncArgs.Number(freePotionSlots)];

    /// <summary>Arguments for <see cref="SyncActions.ShopRemovalJoin"/>: the removal screen in order.</summary>
    public static IReadOnlyList<string> RemovalScreenArgs(IEnumerable<ScreenCard> screen) => ScreenArgs(screen).ToList();

    protected override IReadOnlyList<string> Handle(SyncOp op, IReadOnlyList<string>? replay)
    {
        var args = new ArgReader(op.Args);
        switch (op.Action)
        {
            case SyncActions.ShopEnter:
                var gold = args.Int();
                var potionSlots = args.Int();
                var items = new List<ShopItem>();
                while (args.HasMore)
                {
                    items.Add(new ShopItem(ReadSlot(args), args.Int(), args.Bool()));
                }

                Shop.Enter(op.Player, gold, potionSlots, items);
                return [];
            case SyncActions.ShopBuy:
                var slot = ReadSlot(args);
                args.End();
                if (replay is null && Shop.CanBuy(op.Player, slot) is { IsAllowed: false } check)
                {
                    throw new SyncRejectedException(check.ToString());
                }

                Shop.Buy(op.Player, slot);
                return [];
            case SyncActions.ShopGold:
                var newGold = args.Int();
                var newPotionSlots = args.Int();
                args.End();
                Shop.SetGold(op.Player, newGold);
                Shop.SetFreePotionSlots(op.Player, newPotionSlots);
                return [];
            case SyncActions.ShopRemovalJoin:
                Shop.RemovalTarget.Join(new SelectionScreen(op.Player, ReadScreen(args)));
                return [];
            case SyncActions.ShopRemovalCommit:
                return CommitOrApply(Shop.RemovalTarget, op, ReadCards(args), replay);
            default:
                throw Unknown(op);
        }
    }

    private static ShopSlot ReadSlot(ArgReader args)
    {
        var kind = args.Enum<ShopSlotKind>();
        var number = args.Int();
        return number >= 1 ? new ShopSlot(kind, number) : throw new SyncRejectedException("A slot number is below 1.");
    }
}

/// <summary>One campfire visit by the party.</summary>
public sealed class CampfireTable : SyncTable
{
    public CampfireTable(string id, LinkMode mode, ICardMatchingStrategy matching)
        : base(id)
    {
        Campfire = new CampfireSession(mode, matching);
    }

    public CampfireSession Campfire { get; }

    /// <summary>Arguments for <see cref="SyncActions.CampfireJoin"/>.</summary>
    public static IReadOnlyList<string> JoinArgs(bool hasMiniatureTent, IReadOnlyCollection<CampfireOption> options, IEnumerable<ScreenCard>? upgradeScreen) =>
    [
        SyncArgs.Flag(hasMiniatureTent),
        SyncArgs.Number(options.Count),
        .. options.SelectMany(option => new[] { option.Id, SyncArgs.Flag(option.Available) }),
        .. ScreenArgs(upgradeScreen ?? []),
    ];

    protected override IReadOnlyList<string> Handle(SyncOp op, IReadOnlyList<string>? replay)
    {
        var args = new ArgReader(op.Args);
        switch (op.Action)
        {
            case SyncActions.CampfireJoin:
                var hasTent = args.Bool();
                var count = args.Int();
                if (count < 0)
                {
                    throw new SyncRejectedException("The option count is negative.");
                }

                var options = new List<CampfireOption>();
                for (var i = 0; i < count; i++)
                {
                    options.Add(new CampfireOption(Key(args), args.Bool()));
                }

                var screen = ReadScreen(args);
                Campfire.Join(op.Player, options, hasTent, screen.Count > 0 ? new SelectionScreen(op.Player, screen) : null);
                return [];
            case SyncActions.CampfireChoose:
                var optionId = Key(args);
                args.End();
                if (replay is null && Campfire.CanChoose(op.Player, optionId) is { IsAllowed: false } check)
                {
                    throw new SyncRejectedException(check.ToString());
                }

                Campfire.Choose(op.Player, optionId);
                return [];
            case SyncActions.CampfireSmith:
                return CommitOrApply(Campfire.SmithTarget, op, ReadCards(args), replay);
            default:
                throw Unknown(op);
        }
    }
}
