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

    /// <summary>
    /// Whether the operation is the player joining this table when they're in it already. Such a join changes nothing
    /// and is accepted, so a player can send their join again with a click whenever they haven't seen it arrive.
    /// </summary>
    bool IsRepeatJoin(SyncOp op);
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

    public IReadOnlyList<string> Execute(SyncOp op) => IsRepeatJoin(op) ? [] : Handle(op, replay: null);

    public void Replay(SyncOp op, IReadOnlyList<string> result)
    {
        if (!IsRepeatJoin(op))
        {
            Handle(op, result);
        }
    }

    public virtual bool IsRepeatJoin(SyncOp op) => false;

    /// <summary>Applies an operation.</summary>
    /// <param name="op">The operation.</param>
    /// <param name="replay">The host's result when a client replays the operation; null when the host executes it.</param>
    /// <returns>The result to send with the notice.</returns>
    protected abstract IReadOnlyList<string> Handle(SyncOp op, IReadOnlyList<string>? replay);

    protected static SyncRejectedException Unknown(SyncOp op) => new($"{op.Action} doesn't apply to this table.");

    /// <summary>
    /// Changes a decision's players or options, then settles it. The host sends the commits that follow from the
    /// change, such as a late player following an earlier pick; clients apply those instead of working them out.
    /// </summary>
    protected static IReadOnlyList<string> Settled<TAtom>(
        LinkedChoice<TAtom> decision, IReadOnlyList<string>? replay, Func<TAtom, string> format, Func<string, TAtom> parse, Action change)
        where TAtom : notnull
    {
        ArgumentNullException.ThrowIfNull(change);
        if (replay is not null)
        {
            change();
            decision.ApplyAll(DecodeAll(replay, parse));
            return replay;
        }

        var before = decision.Commits.Count;
        change();
        decision.Settle();
        return CommitCodec.Encode(decision.Commits.Skip(before), format).ToList();
    }

    /// <summary>
    /// A player's click: the host checks it and chooses, sending every commit that follows; a client records the same
    /// vote and applies the host's commits.
    /// </summary>
    /// <param name="decision">The decision.</param>
    /// <param name="op">The operation.</param>
    /// <param name="choice">What the player clicked.</param>
    /// <param name="replay">The host's result on a client, or null on the host.</param>
    /// <param name="format">Writes an option for the notice.</param>
    /// <param name="parse">Reads an option from the notice.</param>
    /// <param name="check">A stricter check than the decision's own, like a reroll the player owes.</param>
    protected static IReadOnlyList<string> ChooseOrApply<TAtom>(
        LinkedChoice<TAtom> decision,
        SyncOp op,
        Selection<TAtom> choice,
        IReadOnlyList<string>? replay,
        Func<TAtom, string> format,
        Func<string, TAtom> parse,
        Func<CommitCheck>? check = null)
        where TAtom : notnull
    {
        if (replay is not null)
        {
            decision.ApplyChoice(op.Player, choice, DecodeAll(replay, parse));
            return replay;
        }

        var verdict = check?.Invoke() ?? decision.CanCommit(op.Player, choice);
        if (!verdict.IsAllowed)
        {
            throw new SyncRejectedException(verdict.ToString());
        }

        var before = decision.Commits.Count;
        decision.Choose(op.Player, choice);
        return CommitCodec.Encode(decision.Commits.Skip(before), format).ToList();
    }

    internal static string FormatKey(OptionKey key) => key.Value;

    internal static OptionKey ParseKey(string text) =>
        text.Length > 0 ? new OptionKey(text) : throw new SyncRejectedException("An option key is empty.");

    internal static string FormatCard(CardId card) => SyncArgs.Number(card.Value);

    internal static CardId ParseCard(string text) =>
        long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var id)
            ? new CardId(id)
            : throw new SyncRejectedException("A card id isn't a number.");

    private static List<Commit<TAtom>> DecodeAll<TAtom>(IReadOnlyList<string> replay, Func<string, TAtom> parse)
        where TAtom : notnull
    {
        var reader = new ArgReader(replay);
        var commits = CommitCodec.Decode(reader, parse);
        reader.End();
        return commits;
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
}

/// <summary>A decision matched by option key: events, Ancients, relic rewards.</summary>
public sealed class OptionTable : SyncTable
{
    public OptionTable(string id, LinkMode mode, int pickCount, WhoDecides whoDecides = WhoDecides.EveryoneAgrees)
        : base(id)
    {
        Decision = new OptionDecision(mode, pickCount, whoDecides);
    }

    public OptionDecision Decision { get; }

    public override bool IsRepeatJoin(SyncOp op) => op.Action == SyncActions.OptionsJoin && Decision.IsParticipant(op.Player);

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

                var joining = ReadOptions(args);
                return Settled(Decision, replay, FormatKey, ParseKey, () => Decision.Join(op.Player, joining));
            case SyncActions.OptionsUpdate:
                var updated = ReadOptions(args);
                return Settled(Decision, replay, FormatKey, ParseKey, () => Decision.UpdateOptions(op.Player, updated));
            case SyncActions.OptionsChoose:
                return ChooseOrApply(Decision, op, new Selection<OptionKey>(args.Rest().Select(ParseKey)), replay, FormatKey, ParseKey);
            case SyncActions.OptionsLeave:
                args.End();
                return Settled(Decision, replay, FormatKey, ParseKey, () => Decision.RemoveParticipant(op.Player));
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
    public CardTable(string id, ICardMatchingStrategy matching, LinkMode mode, int pickCount, WhoDecides whoDecides = WhoDecides.EveryoneAgrees)
        : base(id)
    {
        Decision = new CardChoiceDecision(matching, mode, pickCount, whoDecides);
    }

    public CardChoiceDecision Decision { get; }

    public override bool IsRepeatJoin(SyncOp op) => op.Action == SyncActions.CardsJoin && Decision.IsParticipant(op.Player);

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

                var screen = new SelectionScreen(op.Player, ReadScreen(args));
                return Settled(Decision, replay, FormatCard, ParseCard, () => Decision.Join(screen));
            case SyncActions.CardsChoose:
                return ChooseOrApply(Decision, op, ReadCards(args), replay, FormatCard, ParseCard);
            case SyncActions.CardsLeave:
                args.End();
                return Settled(Decision, replay, FormatCard, ParseCard, () => Decision.RemoveParticipant(op.Player));
            default:
                throw Unknown(op);
        }
    }
}

/// <summary>One card reward screen, with linked Driftwood rerolls.</summary>
public sealed class RewardTable : SyncTable
{
    public RewardTable(string id, int number, LinkMode mode, WhoDecides whoDecides = WhoDecides.EveryoneAgrees)
        : base(id)
    {
        Screen = new CardRewardScreen(number, mode, whoDecides);
    }

    public CardRewardScreen Screen { get; }

    public override bool IsRepeatJoin(SyncOp op) => op.Action == SyncActions.RewardJoin && Screen.Decision.IsParticipant(op.Player);

    /// <summary>Arguments for <see cref="SyncActions.RewardJoin"/>: the screen number, then the offer.</summary>
    public static IReadOnlyList<string> JoinArgs(int number, CardRewardOffer offer) => [SyncArgs.Number(number), .. OfferArgs(offer)];

    /// <summary>Arguments for <see cref="SyncActions.RewardReroll"/>: the new offer.</summary>
    public static IReadOnlyList<string> OfferArgs(CardRewardOffer offer) =>
        [SyncArgs.Number(offer.Cards), SyncArgs.Flag(offer.CanReroll), SyncArgs.Flag(offer.CanSacrifice), .. offer.Extras ?? []];

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

                var offer = ReadOffer(args);
                return Settled(Screen.Decision, replay, FormatKey, ParseKey, () => Screen.Join(op.Player, offer));
            case SyncActions.RewardChoose:
                var option = ParseKey(args.String());
                args.End();
                return ChooseOrApply(
                    Screen.Decision, op, Selection.Of(option), replay, FormatKey, ParseKey, () => Screen.CanCommit(op.Player, option));
            case SyncActions.RewardReroll:
                var rerolled = ReadOffer(args);
                return Settled(Screen.Decision, replay, FormatKey, ParseKey, () => Screen.Reroll(op.Player, rerolled));
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
        var canSacrifice = args.Bool();
        var extras = args.Rest();
        return new CardRewardOffer(cards, extras, canReroll, canSacrifice);
    }
}

/// <summary>
/// One shop visit by the party. Purchases depend only on what's synced, so clients work them out the same way the
/// host does; removal targets come from the host.
/// </summary>
public sealed class ShopTable : SyncTable
{
    public ShopTable(string id, LinkMode mode, ICardMatchingStrategy matching, WhoDecides whoDecides = WhoDecides.EveryoneAgrees)
        : base(id)
    {
        Shop = new ShopSession(mode, matching, whoDecides);
    }

    public ShopSession Shop { get; }

    public override bool IsRepeatJoin(SyncOp op) =>
        (op.Action == SyncActions.ShopEnter && Shop.IsIn(op.Player))
        || (op.Action == SyncActions.ShopRemovalJoin && Shop.RemovalTarget.IsParticipant(op.Player));

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

                Shop.Choose(op.Player, slot);
                return [];
            case SyncActions.ShopGold:
                var newGold = args.Int();
                var newPotionSlots = args.Int();
                args.End();
                Shop.SetGold(op.Player, newGold);
                Shop.SetFreePotionSlots(op.Player, newPotionSlots);
                Shop.Settle();
                return [];
            case SyncActions.ShopRemovalJoin:
                var removalScreen = new SelectionScreen(op.Player, ReadScreen(args));
                return Settled(Shop.RemovalTarget, replay, FormatCard, ParseCard, () => Shop.RemovalTarget.Join(removalScreen));
            case SyncActions.ShopRemovalChoose:
                return ChooseOrApply(Shop.RemovalTarget, op, ReadCards(args), replay, FormatCard, ParseCard);
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

/// <summary>
/// One campfire visit by the party. Every notice carries the host's category commits, then its Smith target commits.
/// </summary>
public sealed class CampfireTable : SyncTable
{
    public CampfireTable(string id, LinkMode mode, ICardMatchingStrategy matching, WhoDecides whoDecides = WhoDecides.EveryoneAgrees)
        : base(id)
    {
        Campfire = new CampfireSession(mode, matching, whoDecides);
    }

    public CampfireSession Campfire { get; }

    public override bool IsRepeatJoin(SyncOp op) => op.Action == SyncActions.CampfireJoin && Campfire.IsAt(op.Player);

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
                var upgradeScreen = screen.Count > 0 ? new SelectionScreen(op.Player, screen) : null;
                if (replay is not null)
                {
                    Campfire.Join(op.Player, options, hasTent, upgradeScreen);
                    var (joinCategories, joinTargets) = Decode(replay);
                    Campfire.ApplySettle(joinCategories, joinTargets);
                    return replay;
                }

                return Record(() =>
                {
                    Campfire.Join(op.Player, options, hasTent, upgradeScreen);
                    Campfire.Settle();
                });
            case SyncActions.CampfireChoose:
                var optionId = Key(args);
                args.End();
                if (replay is not null)
                {
                    var (categories, targets) = Decode(replay);
                    Campfire.ApplyChoose(op.Player, optionId, categories, targets);
                    return replay;
                }

                if (Campfire.CanChoose(op.Player, optionId) is { IsAllowed: false } check)
                {
                    throw new SyncRejectedException(check.ToString());
                }

                return Record(() => Campfire.Choose(op.Player, optionId));
            case SyncActions.CampfireSmith:
                var cards = ReadCards(args);
                if (replay is not null)
                {
                    var (_, smithCommits) = Decode(replay);
                    Campfire.SmithTarget.ApplyChoice(op.Player, cards, smithCommits);
                    return replay;
                }

                var target = cards.Count == 1 ? cards.Items[0] : throw new SyncRejectedException(CommitCheck.WrongPickCount.ToString());
                if (Campfire.CanChooseTarget(op.Player, target) is { IsAllowed: false } targetCheck)
                {
                    throw new SyncRejectedException(targetCheck.ToString());
                }

                return Record(() => Campfire.ChooseTarget(op.Player, target));
            default:
                throw Unknown(op);
        }
    }

    /// <summary>On the host: makes a change and returns the category and Smith target commits it made.</summary>
    private IReadOnlyList<string> Record(Action change)
    {
        var categoriesBefore = Campfire.Category.Commits.Count;
        var targetsBefore = Campfire.SmithTarget.Commits.Count;
        change();
        return
        [
            .. CommitCodec.Encode(Campfire.Category.Commits.Skip(categoriesBefore), FormatKey),
            .. CommitCodec.Encode(Campfire.SmithTarget.Commits.Skip(targetsBefore), FormatCard),
        ];
    }

    private static (List<Commit<OptionKey>> Categories, List<Commit<CardId>> Targets) Decode(IReadOnlyList<string> replay)
    {
        var reader = new ArgReader(replay);
        var categories = CommitCodec.Decode(reader, ParseKey);
        var targets = CommitCodec.Decode(reader, ParseCard);
        reader.End();
        return (categories, targets);
    }
}
