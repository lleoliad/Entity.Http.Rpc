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
        var packets = ParseMany(body);

        if (packets.Count != 1)
        {
            throw new InvalidOperationException($"HTTP proto body must contain exactly one Fantasy outer packet. Actual:{packets.Count}.");
        }

        return packets[0];
    }

    public static IReadOnlyList<HttpProtoPacket> ParseMany(ReadOnlyMemory<byte> body)
    {
        if (body.Length < Packet.OuterPacketHeadLength)
        {
            throw new InvalidOperationException("HTTP proto body is smaller than the Fantasy outer packet header.");
        }

        var packets = new List<HttpProtoPacket>();
        var span = body.Span;
        var offset = 0;

        while (offset < span.Length)
        {
            var remaining = span.Length - offset;

            if (remaining < Packet.OuterPacketHeadLength)
            {
                throw new InvalidOperationException("HTTP proto body contains a partial Fantasy outer packet header.");
            }

            var packetSpan = span.Slice(offset);
            var packetBodyLength = BinaryPrimitives.ReadInt32LittleEndian(packetSpan);

            if (packetBodyLength > ProgramDefine.MaxMessageSize)
            {
                throw new InvalidOperationException($"HTTP proto body exceeds Fantasy max message size:{ProgramDefine.MaxMessageSize}.");
            }

            var packetLength = packetBodyLength < 0
                ? Packet.OuterPacketHeadLength
                : Packet.OuterPacketHeadLength + packetBodyLength;

            if (remaining < packetLength)
            {
                throw new InvalidOperationException($"HTTP proto packet length mismatch. Expected:{packetLength} Actual:{remaining}.");
            }

            var protocolCode = BinaryPrimitives.ReadUInt32LittleEndian(packetSpan.Slice(Packet.PacketLength, sizeof(uint)));
            var rpcId = BinaryPrimitives.ReadUInt32LittleEndian(packetSpan.Slice(Packet.OuterPacketRpcIdLocation, sizeof(uint)));
            var packetBody = packetSpan.Slice(0, packetLength).ToArray();

            packets.Add(new HttpProtoPacket(protocolCode, rpcId, packetBody));
            offset += packetLength;
        }

        return packets;
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

    public static OuterPackInfo CreateOuterPackInfo(HttpPseudoClientNetwork network, HttpProtoPacket packet)
    {
        var packInfo = OuterPackInfo.Create(network);
        packInfo.ProtocolCode = packet.ProtocolCode;
        packInfo.RpcId = packet.RpcId;

        var memoryStream = packInfo.RentMemoryStream(MemoryStreamBufferSource.UnPack, packet.Body.Length);
        memoryStream.Write(packet.Body, 0, packet.Body.Length);
        return packInfo;
    }

    public static HttpProtoPacket FindResponsePacket(ReadOnlyMemory<byte> responseBody, uint rpcId)
    {
        var responsePackets = ParseMany(responseBody);

        if (rpcId != 0)
        {
            foreach (var responsePacket in responsePackets)
            {
                if (responsePacket.RpcId == rpcId)
                {
                    return responsePacket;
                }
            }
        }

        if (responsePackets.Count == 1)
        {
            return responsePackets[0];
        }

        throw new InvalidOperationException($"Fantasy request handler produced {responsePackets.Count} proto packets, but no response packet matched rpcId:{rpcId}.");
    }
}

internal readonly record struct HttpProtoPacket(uint ProtocolCode, uint RpcId, byte[] Body);
