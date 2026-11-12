using SoulLink.Core.Decisions;
using SoulLink.Core.Shops;

namespace SoulLink.Core;

/// <summary>What a death means for the run.</summary>
public enum DeathOutcome
{
    /// <summary>Normal co-op: the player revives after combat if anyone survives.</summary>
    RunContinues,

    /// <summary>Shared death is on, so the run ends for everyone.</summary>
    RunEnds,
}

/// <summary>One player's line in the end-of-run summary.</summary>
public sealed record PlayerSummary(PlayerId Player, int FirstClicks, int Deaths);

/// <summary>Applies shared death and counts first clicks and deaths for the end-of-run summary.</summary>
public sealed class RunTracker
{
    private readonly Dictionary<PlayerId, int> _deaths = new();
    private readonly Dictionary<PlayerId, int> _firstClicks = new();

    public RunTracker(SoulLinkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
    }

    public SoulLinkSettings Settings { get; }

    /// <summary>Counts a death. With shared death on, any player's death ends the run for everyone.</summary>
    public DeathOutcome RecordDeath(PlayerId player)
    {
        _deaths[player] = DeathsOf(player) + 1;
        return Settings.SharedDeath ? DeathOutcome.RunEnds : DeathOutcome.RunContinues;
    }

    /// <summary>Counts a lead in a decision shared with other players as a first click.</summary>
    public void RecordCommit<TAtom>(LinkedChoice<TAtom> decision, Commit<TAtom> commit)
        where TAtom : notnull
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.Kind == CommitKind.Lead && decision.Participants.Count > 1)
        {
            AddFirstClick(commit.Player);
        }
    }

    /// <summary>Counts a purchase made on the player's own, rather than owed, as a first click.</summary>
    public void RecordPurchase(ShopSession shop, Purchase purchase)
    {
        ArgumentNullException.ThrowIfNull(shop);
        ArgumentNullException.ThrowIfNull(purchase);
        if (!purchase.SettledObligation && shop.Shoppers.Count > 1)
        {
            AddFirstClick(purchase.Player);
        }
    }

    public int DeathsOf(PlayerId player) => _deaths.GetValueOrDefault(player);

    public int FirstClicksOf(PlayerId player) => _firstClicks.GetValueOrDefault(player);

    /// <summary>The end-of-run summary for these players, in the given order.</summary>
    public IReadOnlyList<PlayerSummary> Summary(IEnumerable<PlayerId> players) =>
        players.Select(player => new PlayerSummary(player, FirstClicksOf(player), DeathsOf(player))).ToList();

    private void AddFirstClick(PlayerId player) => _firstClicks[player] = FirstClicksOf(player) + 1;
}
