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

    /// <summary>Messages that arrived before this peer's session started, applied once it does.</summary>
    private static readonly Queue<Action<SoulLinkSession>> EarlyMessages = new();

    private readonly INetGameService _net;
    private readonly SyncHost? _host;
    private readonly SyncReplica? _replica;
    private readonly List<PendingOp> _pending = [];

    private SoulLinkSession(INetGameService net, bool isHost)
    {
        _net = net;
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

    public bool IsHost => _host is not null;

    public SyncState State { get; }

    /// <summary>The run with the host's settings, once the host's start notice has arrived.</summary>
    public SoulLinkRun? Run => State.Run;

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
            _pending.Add(new PendingOp(op, done));
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

    private sealed record PendingOp(SyncOp Op, Action<bool>? Done);
}
