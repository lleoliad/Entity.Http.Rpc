using System.Buffers.Binary;
using System.Text.Json;
using Entities.Http.Rpc;
using Fantasy;
using Fantasy.PacketParser;
using MessagePack;
using Xunit;

namespace Entity.Http.Rpc.Tests;

public sealed class HttpJsonRpcInfrastructureTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly MessagePackSerializerOptions WebMessagePackOptions = HttpServicesHandler.ConfigureMessagePackOptions(new HttpRpcMessagePackOptions());

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

    [Fact]
    public void HttpMessagePackRpcRequestEnvelope_Should_Deserialize_Body()
    {
        var request = new C2G_TestRequest
        {
            Tag = "hello",
            Data = [1, 2]
        };

        try
        {
            var envelopeBytes = MessagePackSerializer.Serialize(new HttpMessagePackRpcRequestEnvelope
            {
                ProtocolCode = OuterOpcode.C2G_TestRequest,
                RpcId = 9u,
                MessageName = "C2G_TestRequest",
                Body = MessagePackSerializer.Serialize(typeof(C2G_TestRequest), request, WebMessagePackOptions)
            }, WebMessagePackOptions);

            var envelope = MessagePackSerializer.Deserialize<HttpMessagePackRpcRequestEnvelope>(envelopeBytes, WebMessagePackOptions);

            Assert.NotNull(envelope);
            Assert.Equal(OuterOpcode.C2G_TestRequest, envelope.ProtocolCode);
            Assert.Equal(9u, envelope.RpcId);
            Assert.Equal("C2G_TestRequest", envelope.MessageName);

            var message = MessagePackSerializer.Deserialize(typeof(C2G_TestRequest), envelope.Body!, WebMessagePackOptions);
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
        finally
        {
            request.Dispose();
        }
    }

    [Fact]
    public void HttpMemoryPackRpcRequestEnvelope_Should_Deserialize_Body()
    {
        var response = new G2C_TestMemoryPackResponse
        {
            ErrorCode = 7,
            Info = new TestMemoryPackInfo
            {
                A = "memorypack"
            }
        };

        try
        {
            var envelopeBytes = HttpMemoryPackMessageCodec.SerializeRequestEnvelope(new HttpMemoryPackRpcRequestEnvelope(
                OuterOpcode.G2C_TestMemoryPackResponse,
                11u,
                "G2C_TestMemoryPackResponse",
                MemoryPack.MemoryPackSerializer.Serialize(typeof(G2C_TestMemoryPackResponse), response)));

            var envelope = HttpMemoryPackMessageCodec.DeserializeRequestEnvelope(envelopeBytes);

            Assert.NotNull(envelope);
            Assert.Equal(OuterOpcode.G2C_TestMemoryPackResponse, envelope.ProtocolCode);
            Assert.Equal(11u, envelope.RpcId);
            Assert.Equal("G2C_TestMemoryPackResponse", envelope.MessageName);

            var message = HttpMemoryPackMessageCodec.DeserializeBody(envelope.Body, typeof(G2C_TestMemoryPackResponse), envelope.ProtocolCode);
            var typedMessage = Assert.IsType<G2C_TestMemoryPackResponse>(message);

            try
            {
                Assert.Equal(7u, typedMessage.ErrorCode);
                Assert.NotNull(typedMessage.Info);
                Assert.Equal("memorypack", typedMessage.Info.A);
            }
            finally
            {
                typedMessage.Dispose();
            }
        }
        finally
        {
            response.Dispose();
        }
    }
}
