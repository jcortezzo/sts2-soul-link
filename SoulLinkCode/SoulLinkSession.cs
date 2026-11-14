using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using SoulLink.Core;
using SoulLink.Core.Sync;
using SoulLink.SoulLinkCode.Net;

namespace SoulLink.SoulLinkCode;

/// <summary>
/// Soul Link for the current co-op run. The host orders every player's operations and broadcasts numbered notices;
/// clients send requests to the host and replay its notices, so every peer sees the same locks.
/// </summary>
internal sealed class SoulLinkSession
{
    private const int MaxEarlyMessages = 256;

    /// <summary>How long a request may wait for the host's answer before it's logged as unanswered.</summary>
    private const ulong UnansweredMsec = 10000;

    /// <summary>Messages that arrived before this peer's session started, applied once it does.</summary>
    private static readonly Queue<Action<SoulLinkSession>> EarlyMessages = new();

    private readonly INetGameService _net;
    private readonly SyncHost? _host;
    private readonly SyncReplica? _replica;
    private readonly List<PendingOp> _pending = [];
    private long? _reportedGap;

    private SoulLinkSession(INetGameService net, bool isHost)
    {
        _net = net;
        StartedAtMsec = Godot.Time.GetTicksMsec();
        Me = new PlayerId(net.NetId);
        if (isHost)
        {
            _host = new SyncHost(Me);
            State = _host.State;
        }
        else
        {
            _replica = new SyncReplica();
            State = _replica.State;
        }

        State.Applied += OnApplied;
    }

    /// <summary>Raised after any operation is applied, so lock indicators can refresh.</summary>
    public event Action<SyncNotice>? Changed;

    /// <summary>Raised after any operation is applied in whichever session is current.</summary>
    public static event Action? StateChanged;

    /// <summary>The session for the current co-op run, or null outside co-op runs.</summary>
    public static SoulLinkSession? Current { get; private set; }

    /// <summary>The local player.</summary>
    public PlayerId Me { get; }

    /// <summary>When the session started, in engine ticks.</summary>
    public ulong StartedAtMsec { get; }

    public bool IsHost => _host is not null;

    public SyncState State { get; }

    /// <summary>The run with the host's settings, once the host's start notice has arrived.</summary>
    public SoulLinkRun? Run => State.Run;

    /// <summary>
    /// Whether this peer's copy of the linked state has the local player in the table the join is for. Until it does,
    /// a click on that table sends the join again with it, so a join that got lost can't leave the player out.
    /// </summary>
    public bool HasJoined(string table, string action, IReadOnlyList<string> args) =>
        State.IsJoined(new SyncOp(table, Me, action, args));

    /// <summary>A player's name as the platform shows it, for the indicators.</summary>
    public string NameOf(PlayerId player)
    {
        try
        {
            var name = PlatformUtil.GetPlayerNameRaw(_net.Platform, player.Value);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (Exception ex)
        {
            // Names are only for the indicators, so a platform that can't name a player shouldn't break anything.
            MainFile.Logger.Warn($"Soul Link couldn't look up a player's name: {ex.Message}");
        }

        return "Teammate";
    }

    /// <summary>Starts a session for a new or loaded run. Single-player runs have no session.</summary>
    public static void Start(INetGameService net)
    {
        ArgumentNullException.ThrowIfNull(net);
        Current = null;
        if (!net.Type.IsMultiplayer())
        {
            EarlyMessages.Clear();
            return;
        }

        var session = new SoulLinkSession(net, IsHostService(net));
        Current = session;
        MainFile.Logger.Info($"Soul Link session started as {(session.IsHost ? "host" : "client")} ({net.Type}).");

        // The host starts the run before handling requests that arrived early, so they aren't refused.
        if (session.IsHost)
        {
            session.Submit("run", SyncActions.RunStart, SyncState.StartArgs(SoulLinkConfig.ToSettings()));
        }

        while (EarlyMessages.TryDequeue(out var deliver))
        {
            deliver(session);
        }
    }

    /// <summary>Ends the session when the run is cleaned up.</summary>
    public static void Stop()
    {
        Current = null;
        EarlyMessages.Clear();
    }

    /// <summary>Sends an operation for the local player.</summary>
    /// <param name="table">The table id.</param>
    /// <param name="action">The action, from <see cref="SyncActions"/>.</param>
    /// <param name="args">The action's arguments.</param>
    /// <param name="done">Called with true once the host accepts it and it's applied here, or false if refused.</param>
    public void Submit(string table, string action, IReadOnlyList<string> args, Action<bool>? done = null)
    {
        var op = new SyncOp(table, Me, action, args);
        if (_host is null)
        {
            ReportUnanswered();
            _pending.Add(new PendingOp(op, done, Godot.Time.GetTicksMsec()));
            CustomMessageWrapper.Send(new SoulLinkRequestMessage { Op = op }, _net);
            return;
        }

        var outcome = _host.Submit(op, Me);
        if (outcome.Notice is { } notice)
        {
            CustomMessageWrapper.Send(new SoulLinkNoticeMessage { Notice = notice }, _net);
        }
        else
        {
            MainFile.Logger.Info($"Refused {op}: {outcome.Rejection?.Reason}");
        }

        done?.Invoke(outcome.IsAccepted);
    }

    internal static void ReceiveRequest(SyncOp op, ulong senderId) => Deliver(session => session.OnRequest(op, new PlayerId(senderId)));

    internal static void ReceiveNotice(SyncNotice notice) => Deliver(session => session.OnNotice(notice));

    internal static void ReceiveRejection(SyncRejection rejection) => Deliver(session => session.OnRejection(rejection));

    private static bool IsHostService(INetGameService net) => net.Type == NetGameType.Host;

    private static void Deliver(Action<SoulLinkSession> action)
    {
        if (Current is { } session)
        {
            action(session);
        }
        else if (EarlyMessages.Count < MaxEarlyMessages)
        {
            EarlyMessages.Enqueue(action);
        }
    }

    private void OnRequest(SyncOp op, PlayerId sender)
    {
        if (_host is null)
        {
            MainFile.Logger.Warn($"Ignoring a request sent to a client: {op}");
            return;
        }

        var outcome = _host.Submit(op, sender);
        if (outcome.Notice is { } notice)
        {
            CustomMessageWrapper.Send(new SoulLinkNoticeMessage { Notice = notice }, _net);
        }
        else if (outcome.Rejection is { } rejection)
        {
            MainFile.Logger.Info($"Soul Link refused {op} from {NameOf(sender)}: {rejection.Reason}");
            CustomMessageWrapper.Send(new SoulLinkRejectionMessage { Rejection = rejection }, _net);
        }
    }

    private void OnNotice(SyncNotice notice)
    {
        if (_replica is null)
        {
            return;
        }

        var errors = _replica.Errors.Count;
        _replica.Receive(notice);
        for (var i = errors; i < _replica.Errors.Count; i++)
        {
            MainFile.Logger.Error($"Soul Link drifted from the host: {_replica.Errors[i]}");
        }

        ReportGap();
    }

    /// <summary>
    /// Logs when an update from the host goes missing, because every later update waits for it and nothing linked
    /// changes on this peer until it arrives, and logs again once it does.
    /// </summary>
    private void ReportGap()
    {
        if (_replica is null)
        {
            return;
        }

        var missing = _replica.Missing;
        if (missing == _reportedGap)
        {
            return;
        }

        if (missing is { } sequence)
        {
            MainFile.Logger.Warn($"Soul Link is missing update {sequence} from the host; {_replica.HeldBack} later update(s) wait for it.");
        }
        else
        {
            MainFile.Logger.Info($"Soul Link got the missing update {_reportedGap} from the host and caught up.");
        }

        _reportedGap = missing;
    }

    /// <summary>
    /// Logs requests the host hasn't answered for a while. An answer always comes back, accepted or refused, so a
    /// request still waiting means it or its answer got lost on the way.
    /// </summary>
    private void ReportUnanswered()
    {
        var now = Godot.Time.GetTicksMsec();
        foreach (var pending in _pending.Where(pending => !pending.Reported && now - pending.SentAtMsec > UnansweredMsec))
        {
            pending.Reported = true;
            MainFile.Logger.Warn($"Soul Link has had no answer from the host for {(now - pending.SentAtMsec) / 1000}s: {pending.Op}");
        }
    }

    private void OnRejection(SyncRejection rejection)
    {
        if (rejection.Op.Player != Me)
        {
            return;
        }

        MainFile.Logger.Info($"Host refused {rejection.Op}: {rejection.Reason}");
        TakePending(rejection.Op)?.Invoke(false);
    }

    private void OnApplied(SyncNotice notice)
    {
        if (notice.Op.Action == SyncActions.RunStart && Run is { } run)
        {
            MainFile.Logger.Info($"Soul Link rules for this run: {run.Settings}");
        }

        if (_replica is not null && notice.Op.Player == Me)
        {
            TakePending(notice.Op)?.Invoke(true);
        }

        Changed?.Invoke(notice);
        StateChanged?.Invoke();
    }

    private Action<bool>? TakePending(SyncOp op)
    {
        var index = _pending.FindIndex(pending => pending.Op.Equals(op));
        if (index < 0)
        {
            return null;
        }

        var done = _pending[index].Done;
        _pending.RemoveAt(index);
        return done;
    }

    private sealed class PendingOp(SyncOp op, Action<bool>? done, ulong sentAtMsec)
    {
        public SyncOp Op { get; } = op;

        public Action<bool>? Done { get; } = done;

        public ulong SentAtMsec { get; } = sentAtMsec;

        /// <summary>Whether the missing answer was logged already.</summary>
        public bool Reported { get; set; }
    }
}
