using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using SoulLink.Core;
using SoulLink.Core.Decisions;
using SoulLink.Core.Sync;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Linking;

/// <summary>
/// What every screen linked by option key shares: joining its table, sending clicks, and reading the local copy of
/// its decision.
/// </summary>
internal static class OptionLinks
{
    private static readonly Func<RunManager, RunState?> StateGetter =
        AccessTools.MethodDelegate<Func<RunManager, RunState?>>(AccessTools.PropertyGetter(typeof(RunManager), "State"));

    private static readonly HashSet<string> Joined = new(StringComparer.Ordinal);

    /// <summary>The session, once the host's settings have arrived.</summary>
    public static SoulLinkSession? Session => SoulLinkSession.Current is { Run: not null } session ? session : null;

    /// <summary>The current run's state, or null between runs.</summary>
    public static RunState? RunState => RunManager.Instance is { } manager ? StateGetter(manager) : null;

    /// <summary>The local player, or null between runs.</summary>
    public static Player? Me => RunState is { } state ? LocalContext.GetMe(state) : null;

    /// <summary>The room the party is in, the same text on every peer, for naming tables.</summary>
    public static string? Room => RunState?.RunLocation.ToString();

    /// <summary>Forgets which tables were joined in the last run.</summary>
    public static void Reset() => Joined.Clear();

    /// <summary>The game's player for a linked player, for portraits.</summary>
    public static Player? PlayerOf(PlayerId player) => RunState?.GetPlayer(player.Value);

    public static PlayerId IdOf(Player player) => new(player.NetId);

    /// <summary>
    /// The join for a table, with the local player's options in display order. A screen joins once when it opens; a
    /// click (<paramref name="again"/>) sends the join again until this peer has seen the host accept it, so a join
    /// that got lost on the way can't leave the player out of the decision. The host ignores a join it already has.
    /// </summary>
    public static IReadOnlyList<GatedOp> JoinOps(string table, IReadOnlyList<DecisionOption> options, int pickCount = 1, bool again = false)
    {
        var args = OptionTable.JoinArgs(pickCount, options);
        if ((!again && Joined.Contains(table)) || Session is not { } session || session.HasJoined(table, SyncActions.OptionsJoin, args))
        {
            return [];
        }

        return [new GatedOp(table, SyncActions.OptionsJoin, args, Sent: () => Joined.Add(table))];
    }

    /// <summary>Sends operations that need no answer, like joining a table when its screen opens.</summary>
    public static void Submit(IEnumerable<GatedOp> ops)
    {
        if (Session is not { } session)
        {
            return;
        }

        foreach (var op in ops)
        {
            op.Sent?.Invoke();
            session.Submit(op.Table, op.Action, op.Args);
        }
    }

    /// <summary>The operation for a click on options, checked against the local linked state.</summary>
    public static GatedOp Choose(string table, params OptionKey[] keys) =>
        new(table, SyncActions.OptionsChoose, keys.Select(key => key.Value).ToList(), state =>
            state.Find<OptionTable>(table)?.Decision.CanCommit(SoulLinkSession.Current?.Me ?? default, new Selection<OptionKey>(keys)));

    /// <summary>A table's decision, once the local player has joined it.</summary>
    public static OptionDecision? DecisionOf(string table) =>
        Session is { } session
        && session.State.Find<OptionTable>(table)?.Decision is { } decision
        && decision.IsParticipant(session.Me)
            ? decision
            : null;

    /// <summary>The local player's commit on a table, or null.</summary>
    public static Commit<OptionKey>? CommitOf(string table) =>
        Session is { } session ? DecisionOf(table)?.CommitOf(session.Me) : null;

    /// <summary>The players whose vote or choice includes the option, in the order they joined, for portraits.</summary>
    public static IReadOnlyList<PlayerId> Backers(OptionDecision decision, OptionKey key) =>
        decision.Participants
            .Where(player => decision.VoteOf(player)?.Contains(key) == true || decision.CommitOf(player)?.Choice.Contains(key) == true)
            .ToList();

    /// <summary>Whether the local player can't take the option now: someone else's choice locks it, mean mode greys it out, or they chose another.</summary>
    public static bool IsBlocked(OptionDecision decision, OptionKey key) =>
        Session is { } session
        && decision.ViewOptions(session.Me).FirstOrDefault(view => view.Option == key) is { } view
        && view.State is OptionState.Locked or OptionState.GreyedOut or OptionState.NotChosen;
}
