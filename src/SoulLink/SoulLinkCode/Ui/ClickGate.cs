using Godot;
using SoulLink.Core.Decisions;
using SoulLink.Core.Sync;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>One operation a click needs the host to accept before the game acts on it.</summary>
/// <param name="Table">The table id.</param>
/// <param name="Action">The action, from <see cref="SyncActions"/>.</param>
/// <param name="Args">The action's arguments.</param>
/// <param name="Check">The local verdict, from this peer's copy of the linked state. Null skips the local check.</param>
/// <param name="Sent">Runs when the operation is sent to the host.</param>
internal sealed record GatedOp(
    string Table,
    string Action,
    IReadOnlyList<string> Args,
    Func<SyncState, CommitCheck?>? Check = null,
    Action? Sent = null);

/// <summary>
/// Holds a player's click on a linked option until the host accepts it, then replays the click. The game's own sync
/// carries the actual choice, so the mod never changes game state itself.
/// </summary>
internal static class ClickGate
{
    /// <summary>How long to wait for the host before accepting clicks again, in case an answer never arrives.</summary>
    private const ulong WaitLimitMsec = 5000;

    private static object? _approved;
    private static bool _waiting;
    private static ulong _waitingSince;

    /// <summary>True when a click is replaying after the host accepted it, so the patch should let it through.</summary>
    public static bool TryConsumeApproval(object owner)
    {
        if (!ReferenceEquals(_approved, owner))
        {
            return false;
        }

        _approved = null;
        return true;
    }

    /// <summary>
    /// Decides whether a click may go ahead. Returns true when the game should act now: outside linked runs, or when
    /// the host accepted at once (on the host). Otherwise the click is blocked or waits for the host, and
    /// <paramref name="replay"/> runs once every operation is accepted.
    /// </summary>
    public static bool Request(GodotObject owner, IReadOnlyList<GatedOp> ops, Action replay)
    {
        var session = SoulLinkSession.Current;
        if (session?.Run is null || ops.Count == 0)
        {
            return true;
        }

        if (_waiting && Time.GetTicksMsec() - _waitingSince < WaitLimitMsec)
        {
            return false;
        }

        foreach (var op in ops)
        {
            if (op.Check?.Invoke(session.State) is { IsAllowed: false } check && check.Verdict != CommitVerdict.NotParticipant)
            {
                MainFile.Logger.Info($"Soul Link blocked {op.Action} on {op.Table}: {check}");
                LockView.Refresh();
                return false;
            }
        }

        var remaining = ops.Count;
        var failed = false;
        var synchronous = true;
        _waiting = true;
        _waitingSince = Time.GetTicksMsec();
        foreach (var op in ops)
        {
            op.Sent?.Invoke();
            session.Submit(op.Table, op.Action, op.Args, accepted =>
            {
                failed |= !accepted;
                if (--remaining > 0 || synchronous)
                {
                    return;
                }

                _waiting = false;
                Finish(owner, failed, replay);
            });
        }

        synchronous = false;
        if (remaining > 0)
        {
            return false;
        }

        // Every operation was answered during Submit, which only happens on the host.
        _waiting = false;
        if (failed)
        {
            LockView.Refresh();
        }

        return !failed;
    }

    private static void Finish(GodotObject owner, bool failed, Action replay)
    {
        if (failed || !GodotObject.IsInstanceValid(owner))
        {
            LockView.Refresh();
            return;
        }

        _approved = owner;
        try
        {
            replay();
        }
        finally
        {
            _approved = null;
        }
    }
}
