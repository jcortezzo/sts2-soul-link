namespace SoulLink.Core.Sync;

/// <summary>The host's answer to an operation: a notice to send to everyone, or a rejection for the sender.</summary>
public sealed record SyncOutcome(SyncNotice? Notice, SyncRejection? Rejection)
{
    public bool IsAccepted => Notice is not null;
}

/// <summary>
/// Runs on the host. Orders operations from every player, first come first served, applies them and numbers them,
/// so the host decides which commit came first.
/// </summary>
public sealed class SyncHost
{
    public SyncHost(PlayerId host, SyncState? state = null)
    {
        Host = host;
        State = state ?? new SyncState();
    }

    /// <summary>The host's own player.</summary>
    public PlayerId Host { get; }

    public SyncState State { get; }

    /// <summary>The sequence of the last accepted operation.</summary>
    public long Sequence { get; private set; }

    /// <summary>Checks and applies an operation a player sent.</summary>
    /// <param name="op">The operation.</param>
    /// <param name="sender">
    /// Who sent it. Players can only act for themselves, and only the host can start the run or pair the starting decks.
    /// </param>
    public SyncOutcome Submit(SyncOp op, PlayerId sender)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (op.Player != sender)
        {
            return Reject(op, "Players can only act for themselves.");
        }

        if (op.Action is SyncActions.RunStart or SyncActions.LinksStarters && sender != Host)
        {
            return Reject(op, "Only the host can start the run or pair the starting decks.");
        }

        IReadOnlyList<string> result;
        try
        {
            result = State.Execute(op);
        }
        catch (Exception ex) when (ex is SyncRejectedException or ArgumentException or InvalidOperationException)
        {
            return Reject(op, ex.Message);
        }

        var notice = new SyncNotice(++Sequence, op, result);
        State.Notify(notice);
        return new SyncOutcome(notice, null);
    }

    private static SyncOutcome Reject(SyncOp op, string reason) => new(null, new SyncRejection(op, reason));
}

/// <summary>
/// Runs on clients. Applies the host's notices in order, holding back any that arrive early, so the client's state
/// matches the host's.
/// </summary>
public sealed class SyncReplica
{
    private readonly SortedDictionary<long, SyncNotice> _early = new();
    private readonly List<string> _errors = [];

    public SyncReplica(SyncState? state = null)
    {
        State = state ?? new SyncState();
    }

    public SyncState State { get; }

    /// <summary>The sequence of the last applied notice.</summary>
    public long Applied { get; private set; }

    /// <summary>Notices that failed to apply, which means this client drifted from the host.</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>
    /// The notice this client is missing while later ones wait for it, or null when nothing is held back. Every
    /// update from the host after a missing one waits, so a notice that never arrives stalls the whole linked state.
    /// </summary>
    public long? Missing => _early.Count > 0 ? Applied + 1 : null;

    /// <summary>How many notices are held back waiting for <see cref="Missing"/>.</summary>
    public int HeldBack => _early.Count;

    /// <summary>Applies a notice, or holds it until the notices before it arrive. Duplicates are ignored.</summary>
    public void Receive(SyncNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (notice.Sequence <= Applied || !_early.TryAdd(notice.Sequence, notice))
        {
            return;
        }

        while (_early.Remove(Applied + 1, out var next))
        {
            Applied = next.Sequence;
            try
            {
                State.Replay(next.Op, next.Result);
                State.Notify(next);
            }
            catch (Exception ex) when (ex is SyncRejectedException or ArgumentException or InvalidOperationException)
            {
                _errors.Add($"{next.Op}: {ex.Message}");
            }
        }
    }
}
