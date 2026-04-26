using Fantasy;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.PacketParser;
using MemoryPack;
using Microsoft.AspNetCore.Http;

namespace Entities.Http.Rpc;

/// <summary>
/// Adapts a MemoryPack envelope into the corresponding Fantasy message invocation and converts
/// request-style replies back into MemoryPack.
/// </summary>
public sealed class HttpMemoryPackMessageDispatcher
{
    private readonly Scene _scene;
    private readonly HttpProtoReflectionBridge _reflectionBridge;

    public HttpMemoryPackMessageDispatcher(Scene scene, HttpProtoReflectionBridge reflectionBridge)
    {
        _scene = scene;
        _reflectionBridge = reflectionBridge;
    }

    public async Task<HttpMemoryPackDispatchResult> DispatchAsync(HttpContext httpContext, HttpProtoSessionLease sessionLease, string? routeMessageName, CancellationToken cancellationToken)
    {
        var requestBody = await ReadBodyAsync(httpContext.Request, cancellationToken);
        var envelope = HttpMemoryPackMessageCodec.DeserializeRequestEnvelope(requestBody)
            ?? throw new InvalidOperationException("HTTP memorypack rpc request body is required.");

        var dispatcher = _reflectionBridge.GetMessageDispatcher(_scene);
        var messageType = _reflectionBridge.GetMessageType(dispatcher, envelope.ProtocolCode)
            ?? throw new InvalidOperationException($"Fantasy message type for protocolCode:{envelope.ProtocolCode} was not found.");

        EnsureMessageName(routeMessageName, "Route", messageType);
        EnsureMessageName(envelope.MessageName, "Body", messageType);

        var message = HttpMemoryPackMessageCodec.DeserializeBody(envelope.Body, messageType, envelope.ProtocolCode);
        var isRequest = typeof(IRequest).IsAssignableFrom(messageType);

        // Reuse the proto request context so downstream Fantasy code can keep writing responses through
        // Session.Send without caring whether the transport was JSON, MessagePack, MemoryPack, or raw binary.
        sessionLease.Network.BindRequest(sessionLease.RequestContext);

        await HttpProtoSceneDispatcher.RunAsync(_scene, () =>
            _reflectionBridge.DispatchAsync(dispatcher, sessionLease.Session, envelope.ProtocolCode, envelope.RpcId, message));

        if (!isRequest)
        {
            return HttpMemoryPackDispatchResult.Empty;
        }

        if (!sessionLease.RequestContext.HasResponse)
        {
            throw new InvalidOperationException($"Fantasy request handler for protocolCode:{envelope.ProtocolCode} did not produce a response packet.");
        }

        var responseBody = sessionLease.RequestContext.GetResponseBytes().ToArray();
        var responsePacket = HttpProtoPacketCodec.Parse(responseBody);
        var responseType = _reflectionBridge.GetMessageType(dispatcher, responsePacket.ProtocolCode)
            ?? throw new InvalidOperationException($"Fantasy message type for protocolCode:{responsePacket.ProtocolCode} was not found.");
        var responseMessage = DeserializeResponsePacket(responsePacket, responseType);

        try
        {
            var messageBody = MemoryPackSerializer.Serialize(responseType, responseMessage);
            var responseEnvelope = new HttpMemoryPackRpcResponseEnvelope(
                responsePacket.ProtocolCode,
                responsePacket.RpcId,
                responseType.Name,
                messageBody);

            return new HttpMemoryPackDispatchResult(true, responseEnvelope);
        }
        finally
        {
            if (responseMessage is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static object DeserializeResponsePacket(HttpProtoPacket packet, Type responseType)
    {
        OpCodeIdStruct opCodeIdStruct = packet.ProtocolCode;

        if (opCodeIdStruct.OpCodeProtocolType == OpCodeProtocolType.MemoryPack)
        {
            return HttpMemoryPackMessageCodec.DeserializeBody(
                packet.Body.AsSpan(Packet.OuterPacketHeadLength).ToArray(),
                responseType,
                packet.ProtocolCode);
        }

        return HttpProtoPacketCodec.Deserialize(packet, responseType);
    }

    private static void EnsureMessageName(string? actualMessageName, string source, Type messageType)
    {
        if (string.IsNullOrWhiteSpace(actualMessageName))
        {
            return;
        }

        if (!string.Equals(actualMessageName, messageType.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{source} message name '{actualMessageName}' does not match protocol message '{messageType.Name}'.");
        }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await request.Body.CopyToAsync(memoryStream, cancellationToken);
        return memoryStream.ToArray();
    }
}

internal static class HttpMemoryPackMessageCodec
{
    public static HttpMemoryPackRpcRequestEnvelope? DeserializeRequestEnvelope(byte[] body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        var envelope = MemoryPackSerializer.Deserialize<(uint ProtocolCode, uint RpcId, string? MessageName, byte[]? Body)>(body);
        return new HttpMemoryPackRpcRequestEnvelope(envelope.ProtocolCode, envelope.RpcId, envelope.MessageName, envelope.Body);
    }

    public static byte[] SerializeRequestEnvelope(HttpMemoryPackRpcRequestEnvelope envelope)
    {
        return MemoryPackSerializer.Serialize((envelope.ProtocolCode, envelope.RpcId, envelope.MessageName, envelope.Body));
    }

    public static byte[] SerializeResponseEnvelope(HttpMemoryPackRpcResponseEnvelope envelope)
    {
        return MemoryPackSerializer.Serialize((envelope.ProtocolCode, envelope.RpcId, envelope.MessageName, envelope.Body));
    }

    public static byte[] SerializeErrorEnvelope(HttpMemoryPackErrorEnvelope envelope)
    {
        return MemoryPackSerializer.Serialize((envelope.Title, envelope.Status, envelope.TraceId, envelope.Detail));
    }

    public static object DeserializeBody(byte[]? body, Type messageType, uint protocolCode)
    {
        if (body is null || body.Length == 0)
        {
            return Activator.CreateInstance(messageType)
                ?? throw new InvalidOperationException($"Failed to create Fantasy message {messageType.FullName} from an empty memorypack body.");
        }

        var message = MemoryPackSerializer.Deserialize(messageType, body);
        return message ?? throw new InvalidOperationException($"Failed to deserialize Fantasy message {messageType.FullName} from memorypack body for protocolCode:{protocolCode}.");
    }
}

public readonly record struct HttpMemoryPackDispatchResult(bool HasResponse, HttpMemoryPackRpcResponseEnvelope? ResponseEnvelope)
{
    public static HttpMemoryPackDispatchResult Empty => new(false, null);
}

/// <summary>
/// Wire format accepted by <c>/http/memorypack/rpc</c>.
/// </summary>
public sealed record HttpMemoryPackRpcRequestEnvelope(uint ProtocolCode, uint RpcId, string? MessageName, byte[]? Body);

/// <summary>
/// Wire format returned by <c>/http/memorypack/rpc</c> when a Fantasy request message produces a reply.
/// </summary>
public sealed record HttpMemoryPackRpcResponseEnvelope(uint ProtocolCode, uint RpcId, string MessageName, byte[] Body);

public sealed record HttpMemoryPackErrorEnvelope(string Title, int Status, string TraceId, string? Detail);
