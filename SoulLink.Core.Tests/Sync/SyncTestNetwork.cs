using SoulLink.Core.Sync;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests.Sync;

/// <summary>A host (Ana) and two clients (Ben and Cal) passing sync messages in memory.</summary>
internal sealed class SyncTestNetwork
{
    public SyncTestNetwork(SoulLinkSettings? settings = null)
    {
        Host = new SyncHost(Ana);
        Replicas = new Dictionary<PlayerId, SyncReplica> { [Ben] = new(), [Cal] = new() };
        Send(new SyncOp("run", Ana, SyncActions.RunStart, SyncState.StartArgs(settings ?? SoulLinkSettings.Default)));
    }

    public SyncHost Host { get; }

    public Dictionary<PlayerId, SyncReplica> Replicas { get; }

    /// <summary>Every peer's state, host first.</summary>
    public IEnumerable<SyncState> States => new[] { Host.State }.Concat(Replicas.Values.Select(replica => replica.State));

    /// <summary>Submits an operation from its player and delivers an accepted notice to every client.</summary>
    public SyncOutcome Send(SyncOp op)
    {
        var outcome = Host.Submit(op, op.Player);
        if (outcome.Notice is { } notice)
        {
            foreach (var replica in Replicas.Values)
            {
                replica.Receive(notice);
            }
        }

        return outcome;
    }

    public SyncOutcome Send(string table, PlayerId player, string action, params string[] args) =>
        Send(new SyncOp(table, player, action, args));

    public SyncOutcome Send(string table, PlayerId player, string action, IReadOnlyList<string> args) =>
        Send(new SyncOp(table, player, action, args));

    /// <summary>Asserts every peer agrees, then returns one of the values.</summary>
    public T Agreed<T>(Func<SyncState, T> read)
    {
        var values = States.Select(read).ToList();
        Assert.All(values, value => Assert.Equal(values[0], value));
        Assert.All(Replicas.Values, replica => Assert.Empty(replica.Errors));
        return values[0];
    }
}

/// <summary>An in-memory packet for codec tests.</summary>
internal sealed class MemoryPacket : IPacketWriter, IPacketReader
{
    private readonly List<object> _values = [];
    private int _next;

    public void WriteString(string value) => _values.Add(value);

    public void WriteULong(ulong value) => _values.Add(value);

    public void WriteLong(long value) => _values.Add(value);

    public void WriteInt(int value) => _values.Add(value);

    public string ReadString() => (string)_values[_next++];

    public ulong ReadULong() => (ulong)_values[_next++];

    public long ReadLong() => (long)_values[_next++];

    public int ReadInt() => (int)_values[_next++];
}
