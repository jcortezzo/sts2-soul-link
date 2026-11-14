namespace SoulLink.Core.Sync;

/// <summary>Writes primitive values to a network packet. The mod adapts the game's packet writer to this.</summary>
public interface IPacketWriter
{
    void WriteString(string value);

    void WriteULong(ulong value);

    void WriteLong(long value);

    void WriteInt(int value);
}

/// <summary>Reads primitive values from a network packet, in the order they were written.</summary>
public interface IPacketReader
{
    string ReadString();

    ulong ReadULong();

    long ReadLong();

    int ReadInt();
}

/// <summary>Writes and reads sync messages.</summary>
public static class SyncCodec
{
    /// <summary>The most items a list may hold, so a corrupt packet can't allocate huge lists.</summary>
    public const int MaxListLength = 4096;

    public static void WriteOp(IPacketWriter writer, SyncOp op)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(op);
        writer.WriteString(op.Table);
        writer.WriteULong(op.Player.Value);
        writer.WriteString(op.Action);
        WriteList(writer, op.Args);
    }

    public static SyncOp ReadOp(IPacketReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var table = reader.ReadString();
        var player = new PlayerId(reader.ReadULong());
        var action = reader.ReadString();
        return new SyncOp(table, player, action, ReadList(reader));
    }

    public static void WriteNotice(IPacketWriter writer, SyncNotice notice)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(notice);
        writer.WriteLong(notice.Sequence);
        WriteOp(writer, notice.Op);
        WriteList(writer, notice.Result);
    }

    public static SyncNotice ReadNotice(IPacketReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var sequence = reader.ReadLong();
        var op = ReadOp(reader);
        return new SyncNotice(sequence, op, ReadList(reader));
    }

    public static void WriteRejection(IPacketWriter writer, SyncRejection rejection)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(rejection);
        WriteOp(writer, rejection.Op);
        writer.WriteString(rejection.Reason);
    }

    public static SyncRejection ReadRejection(IPacketReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var op = ReadOp(reader);
        return new SyncRejection(op, reader.ReadString());
    }

    private static void WriteList(IPacketWriter writer, IReadOnlyList<string> items)
    {
        if (items.Count > MaxListLength)
        {
            throw new ArgumentException($"Lists can hold at most {MaxListLength} items.", nameof(items));
        }

        writer.WriteInt(items.Count);
        foreach (var item in items)
        {
            writer.WriteString(item);
        }
    }

    private static List<string> ReadList(IPacketReader reader)
    {
        var count = reader.ReadInt();
        if (count is < 0 or > MaxListLength)
        {
            throw new InvalidDataException($"A list length of {count} is out of range.");
        }

        var items = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            items.Add(reader.ReadString());
        }

        return items;
    }
}
