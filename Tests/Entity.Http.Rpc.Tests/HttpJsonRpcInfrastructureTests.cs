using System.Buffers.Binary;
using System.Text.Json;
using Entities.Http.Rpc;
using Fantasy;
using Fantasy.PacketParser;
using Xunit;

namespace Entity.Http.Rpc.Tests;

public sealed class HttpJsonRpcInfrastructureTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void HttpProtoPacketCodec_Should_Parse_Empty_Message_Header()
    {
        var packetBytes = new byte[Packet.OuterPacketHeadLength];
        BinaryPrimitives.WriteInt32LittleEndian(packetBytes, -1);
        BinaryPrimitives.WriteUInt32LittleEndian(packetBytes.AsSpan(Packet.PacketLength, sizeof(uint)), OuterOpcode.C2G_TestEmptyMessage);
        BinaryPrimitives.WriteUInt32LittleEndian(packetBytes.AsSpan(Packet.OuterPacketRpcIdLocation, sizeof(uint)), 42u);

        var packet = HttpProtoPacketCodec.Parse(packetBytes);

        Assert.Equal(OuterOpcode.C2G_TestEmptyMessage, packet.ProtocolCode);
        Assert.Equal(42u, packet.RpcId);
        Assert.Equal(packetBytes, packet.Body);
    }

    [Fact]
    public void HttpProtoPacketCodec_Should_Reject_Length_Mismatch()
    {
        var packetBytes = new byte[Packet.OuterPacketHeadLength];
        BinaryPrimitives.WriteInt32LittleEndian(packetBytes, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(packetBytes.AsSpan(Packet.PacketLength, sizeof(uint)), OuterOpcode.C2G_TestEmptyMessage);
        BinaryPrimitives.WriteUInt32LittleEndian(packetBytes.AsSpan(Packet.OuterPacketRpcIdLocation, sizeof(uint)), 1u);

        var exception = Assert.Throws<InvalidOperationException>(() => HttpProtoPacketCodec.Parse(packetBytes));

        Assert.Contains("length mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HttpJsonRpcRequestEnvelope_Should_Deserialize_CamelCase_Body()
    {
        const string json = """
                            {
                              "protocolCode": 268445457,
                              "rpcId": 9,
                              "messageName": "C2G_TestRequest",
                              "body": {
                                "tag": "hello",
                                "data": [1, 2]
                              }
                            }
                            """;

        var envelope = JsonSerializer.Deserialize<HttpJsonRpcRequestEnvelope>(json, WebJsonOptions);
        Assert.NotNull(envelope);
        Assert.Equal(OuterOpcode.C2G_TestRequest, envelope.ProtocolCode);
        Assert.Equal(9u, envelope.RpcId);
        Assert.Equal("C2G_TestRequest", envelope.MessageName);

        var message = envelope.Body.Deserialize(typeof(C2G_TestRequest), WebJsonOptions);
        var typedMessage = Assert.IsType<C2G_TestRequest>(message);

        try
        {
            Assert.Equal("hello", typedMessage.Tag);
            Assert.Equal([1, 2], typedMessage.Data);
        }
        finally
        {
            typedMessage.Dispose();
        }
    }
}
