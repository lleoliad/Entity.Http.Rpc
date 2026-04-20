using System.Buffers.Binary;
using System.IO;
using Fantasy;
using Fantasy.Network;
using Fantasy.PacketParser;
using Fantasy.Serialize;

namespace Entities.Http.Rpc;

/// <summary>
/// Parses and deserializes Fantasy outer packets received through HTTP.
/// </summary>
internal static class HttpProtoPacketCodec
{
    public static HttpProtoPacket Parse(byte[] body)
    {
        if (body.Length < Packet.OuterPacketHeadLength)
        {
            throw new InvalidOperationException("HTTP proto body is smaller than the Fantasy outer packet header.");
        }

        var span = body.AsSpan();
        var packetBodyLength = BinaryPrimitives.ReadInt32LittleEndian(span);

        if (packetBodyLength > ProgramDefine.MaxMessageSize)
        {
            throw new InvalidOperationException($"HTTP proto body exceeds Fantasy max message size:{ProgramDefine.MaxMessageSize}.");
        }

        var protocolCode = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(Packet.PacketLength, sizeof(uint)));
        var rpcId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(Packet.OuterPacketRpcIdLocation, sizeof(uint)));
        var expectedLength = packetBodyLength < 0 ? Packet.OuterPacketHeadLength : Packet.OuterPacketHeadLength + packetBodyLength;

        // Fantasy uses -1 to represent "header only" messages, so HTTP validation mirrors that convention.
        if (body.Length != expectedLength)
        {
            throw new InvalidOperationException($"HTTP proto packet length mismatch. Expected:{expectedLength} Actual:{body.Length}.");
        }

        return new HttpProtoPacket(protocolCode, rpcId, body);
    }

    public static object Deserialize(HttpProtoPacket packet, Type messageType)
    {
        var memoryStream = new MemoryStreamBuffer(packet.Body);
        memoryStream.Seek(Packet.OuterPacketHeadLength, SeekOrigin.Begin);

        OpCodeIdStruct opCodeIdStruct = packet.ProtocolCode;

        if (SerializerManager.TryDeserialize(opCodeIdStruct.OpCodeProtocolType, messageType, memoryStream, out var message, out var error))
        {
            return message!;
        }

        throw new InvalidOperationException($"Failed to deserialize Fantasy message {messageType.FullName}: {error}");
    }
}

internal readonly record struct HttpProtoPacket(uint ProtocolCode, uint RpcId, byte[] Body);
