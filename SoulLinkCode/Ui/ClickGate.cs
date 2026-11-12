using Godot;
using SoulLink.Core.Decisions;
using SoulLink.Core.Sync;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>One operation a click sends to the host.</summary>
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
/// Sends a player's click on a linked option to the host instead of letting the game act on it. With everyone agrees
/// the click is a vote, and with first click it decides. Whatever the host then commits for the local player is done
/// in the game as an approved click, by the same code whoever's click caused it, so the game's own sync still
/// carries the actual choice.
/// </summary>
internal static class ClickGate
{
    /// <summary>How long to ignore further clicks while waiting for the host, in case an answer never arrives.</summary>
    private const ulong WaitLimitMsec = 5000;

    private static object? _approved;
    private static bool _waiting;
    private static ulong _waitingSince;

    /// <summary>True when the mod itself is doing this click, so the patch should let it through.</summary>
    public static bool TryConsumeApproval(object owner)
    {
        if (!ReferenceEquals(_approved, owner))
        {
            return false;
        }

        _approved = null;
        return true;
    }

    /// <summary>Does a click the mod approved, letting it through the gate patches.</summary>
    public static void RunApproved(GodotObject owner, Action click)
    {
        _approved = owner;
        try
        {
            click();
        }
        finally
        {
            _approved = null;
        }
    }

    /// <summary>
    /// Decides whether the game may act on a click now. Returns true outside linked runs or when there's nothing to
    /// send. Otherwise the game's own action is skipped and the operations go to the host, unless the local copy of
    /// the linked state already refuses them; <paramref name="answered"/> then runs with whether everything was
    /// accepted.
    /// </summary>
    public static bool Request(IReadOnlyList<GatedOp> ops, Action<bool>? answered = null)
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
                Views.RefreshAll();
                answered?.Invoke(false);
                return false;
            }
        }

        var remaining = ops.Count;
        var failed = false;
        _waiting = true;
        _waitingSince = Time.GetTicksMsec();
        foreach (var op in ops)
        {
            op.Sent?.Invoke();
            session.Submit(op.Table, op.Action, op.Args, accepted =>
            {
                failed |= !accepted;
                if (--remaining > 0)
                {
                    return;
                }

                _waiting = false;
                Views.RefreshAll();
                answered?.Invoke(!failed);
            });
        }

        return false;
    }
}
