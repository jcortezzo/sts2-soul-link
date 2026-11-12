using SoulLink.Core.Cards;

namespace SoulLink.Core.Sync;

/// <summary>
/// The linked state every peer keeps: the run and its open tables. The host changes it through
/// <see cref="SyncHost"/> and clients through <see cref="SyncReplica"/>, so it only ever changes by applying
/// operations in the host's order.
/// </summary>
public sealed class SyncState
{
    private readonly Dictionary<string, ISyncTable> _tables = new(StringComparer.Ordinal);
    private readonly HashSet<string> _closed = new(StringComparer.Ordinal);

    /// <summary>Raised after each operation is applied, on the host and on clients.</summary>
    public event Action<SyncNotice>? Applied;

    /// <summary>The run, once the host has started it with its settings.</summary>
    public SoulLinkRun? Run { get; private set; }

    public IReadOnlyCollection<string> TableIds => _tables.Keys;

    /// <summary>Arguments for <see cref="SyncActions.LinksStarters"/>.</summary>
    public static IReadOnlyList<string> StarterArgs(IReadOnlyList<StarterDeck> decks)
    {
        ArgumentNullException.ThrowIfNull(decks);
        var args = new List<string> { SyncArgs.Number(decks.Count) };
        foreach (var deck in decks)
        {
            args.Add(SyncArgs.Player(deck.Player));
            args.Add(SyncArgs.Number(deck.Cards.Count));
            foreach (var card in deck.Cards)
            {
                args.Add(SyncArgs.Number(card.Card.Value));
                args.Add(card.Name);
            }
        }

        return args;
    }

    /// <summary>Arguments for <see cref="SyncActions.LinksGained"/>.</summary>
    public static IReadOnlyList<string> GainedArgs(string source, IEnumerable<CardId> cards) =>
        [source, .. cards.Select(card => SyncArgs.Number(card.Value))];

    /// <summary>Arguments for <see cref="SyncActions.RunStart"/>.</summary>
    public static IReadOnlyList<string> StartArgs(SoulLinkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return [settings.CardMatching.ToString(), settings.Mode.ToString(), SyncArgs.Flag(settings.SharedDeath), settings.WhoDecides.ToString()];
    }

    /// <summary>The open table with this id, if it exists and has this type.</summary>
    public TTable? Find<TTable>(string id)
        where TTable : class, ISyncTable =>
        _tables.GetValueOrDefault(id) as TTable;

    /// <summary>
    /// Forgets a table, for example when the party leaves the room. Later operations on it are refused on the host
    /// and skipped on clients. Every peer can close tables on their own.
    /// </summary>
    public void Close(string id)
    {
        _tables.Remove(id);
        _closed.Add(id);
    }

    /// <summary>Closes every open table, for example when entering a new room.</summary>
    public void CloseAll()
    {
        foreach (var id in _tables.Keys.ToList())
        {
            Close(id);
        }
    }

    internal IReadOnlyList<string> Execute(SyncOp op) => Handle(op, replay: null);

    internal void Replay(SyncOp op, IReadOnlyList<string> result) => Handle(op, result);

    internal void Notify(SyncNotice notice) => Applied?.Invoke(notice);

    private IReadOnlyList<string> Handle(SyncOp op, IReadOnlyList<string>? replay)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (op.Action == SyncActions.RunStart)
        {
            Start(op);
            return [];
        }

        if (Run is null)
        {
            throw new SyncRejectedException("The run hasn't started.");
        }

        switch (op.Action)
        {
            case SyncActions.LinksStarters:
                Run.LinkStarterDecks(ReadDecks(new ArgReader(op.Args)));
                return [];
            case SyncActions.LinksGained:
                var gained = new ArgReader(op.Args);
                var source = gained.String();
                Run.Gains.Gain(source, op.Player, gained.Rest().Select(SyncTable.ParseCard).ToList());
                return [];
        }

        if (_closed.Contains(op.Table))
        {
            // The host refuses new operations on a closed table, but a client that closed it first skips the
            // operations the host had already accepted.
            return replay ?? throw new SyncRejectedException("That decision is over.");
        }

        var isNew = !_tables.TryGetValue(op.Table, out var table);
        table ??= Create(op, Run) ?? throw new SyncRejectedException($"{op.Table} doesn't exist yet.");
        IReadOnlyList<string> result;
        if (replay is null)
        {
            result = table.Execute(op);
        }
        else
        {
            table.Replay(op, replay);
            result = replay;
        }

        // A table only exists once an operation on it succeeds, so a refused join leaves nothing behind.
        if (isNew)
        {
            _tables[op.Table] = table;
        }

        return result;
    }

    private static ISyncTable? Create(SyncOp op, SoulLinkRun run)
    {
        var mode = run.Settings.Mode;
        var whoDecides = run.Settings.WhoDecides;
        return op.Action switch
        {
            SyncActions.OptionsJoin => new OptionTable(op.Table, mode, FirstNumber(op), whoDecides),
            SyncActions.CardsJoin => new CardTable(op.Table, run.Matching, mode, FirstNumber(op), whoDecides),
            SyncActions.RewardJoin => new RewardTable(op.Table, FirstNumber(op), mode, whoDecides),
            SyncActions.ShopEnter => new ShopTable(op.Table, mode, run.Matching, whoDecides),
            SyncActions.CampfireJoin => new CampfireTable(op.Table, mode, run.Matching, whoDecides),
            _ => null,
        };
    }

    private static int FirstNumber(SyncOp op)
    {
        var number = new ArgReader(op.Args).Int();
        return number >= 1 ? number : throw new SyncRejectedException("The first argument must be at least 1.");
    }

    private static List<StarterDeck> ReadDecks(ArgReader args)
    {
        var decks = new List<StarterDeck>();
        var players = args.Count();
        for (var i = 0; i < players; i++)
        {
            var player = args.Player();
            var count = args.Count();
            var cards = new List<StarterCard>(count);
            for (var j = 0; j < count; j++)
            {
                cards.Add(new StarterCard(new CardId(args.Long()), args.String()));
            }

            decks.Add(new StarterDeck(player, cards));
        }

        args.End();
        return decks;
    }

    private void Start(SyncOp op)
    {
        var args = new ArgReader(op.Args);
        var settings = new SoulLinkSettings(args.Enum<CardMatching>(), args.Enum<LinkMode>(), args.Bool(), args.Enum<WhoDecides>());
        args.End();
        Run = new SoulLinkRun(settings);
        _tables.Clear();
        _closed.Clear();
    }
}
