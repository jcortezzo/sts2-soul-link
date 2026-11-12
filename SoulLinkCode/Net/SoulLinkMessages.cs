using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using SoulLink.Core.Sync;

namespace SoulLink.SoulLinkCode.Net;

/// <summary>A player's operation, sent to the host to be ordered. Only the host handles it.</summary>
public sealed class SoulLinkRequestMessage : ICustomMessage
{
    public SyncOp? Op { get; set; }

    public bool ShouldBroadcast => false;

    public void Serialize(PacketWriter writer) => SyncCodec.WriteOp(new GamePacketWriter(writer), Require(Op));

    public void Deserialize(PacketReader reader) => Op = SyncCodec.ReadOp(new GamePacketReader(reader));

    public void HandleMessage(ulong senderId) => SoulLinkSession.ReceiveRequest(Require(Op), senderId);

    internal static T Require<T>(T? value)
        where T : class =>
        value ?? throw new InvalidOperationException("The message has no content.");
}

/// <summary>An operation the host accepted, sent from the host to every client.</summary>
public sealed class SoulLinkNoticeMessage : ICustomMessage
{
    public SyncNotice? Notice { get; set; }

    public bool ShouldBroadcast => false;

    public void Serialize(PacketWriter writer) =>
        SyncCodec.WriteNotice(new GamePacketWriter(writer), SoulLinkRequestMessage.Require(Notice));

    public void Deserialize(PacketReader reader) => Notice = SyncCodec.ReadNotice(new GamePacketReader(reader));

    public void HandleMessage(ulong senderId) => SoulLinkSession.ReceiveNotice(SoulLinkRequestMessage.Require(Notice));
}

/// <summary>An operation the host refused, sent from the host to every client. Only its sender acts on it.</summary>
public sealed class SoulLinkRejectionMessage : ICustomMessage
{
    public SyncRejection? Rejection { get; set; }

    public bool ShouldBroadcast => false;

    public void Serialize(PacketWriter writer) =>
        SyncCodec.WriteRejection(new GamePacketWriter(writer), SoulLinkRequestMessage.Require(Rejection));

    public void Deserialize(PacketReader reader) => Rejection = SyncCodec.ReadRejection(new GamePacketReader(reader));

    public void HandleMessage(ulong senderId) => SoulLinkSession.ReceiveRejection(SoulLinkRequestMessage.Require(Rejection));
}

/// <summary>Adapts the game's packet writer to the rules engine's codec.</summary>
internal sealed class GamePacketWriter(PacketWriter writer) : IPacketWriter
{
    public void WriteString(string value) => writer.WriteString(value);

    public void WriteULong(ulong value) => writer.WriteULong(value);

    public void WriteLong(long value) => writer.WriteLong(value);

    public void WriteInt(int value) => writer.WriteInt(value);
}

/// <summary>Adapts the game's packet reader to the rules engine's codec.</summary>
internal sealed class GamePacketReader(PacketReader reader) : IPacketReader
{
    public string ReadString() => reader.ReadString();

    public ulong ReadULong() => reader.ReadULong();

    public long ReadLong() => reader.ReadLong();

    public int ReadInt() => reader.ReadInt();
}
