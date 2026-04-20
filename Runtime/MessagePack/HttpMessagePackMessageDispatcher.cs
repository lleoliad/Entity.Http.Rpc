using Fantasy;
using Fantasy.Async;
using Fantasy.Network.Interface;
using MessagePack;
using Microsoft.AspNetCore.Http;

namespace Entities.Http.Rpc;

/// <summary>
/// Adapts a MessagePack envelope into the corresponding Fantasy message invocation and converts
/// request-style replies back into MessagePack.
/// </summary>
public sealed class HttpMessagePackMessageDispatcher
{
    private readonly Scene _scene;
    private readonly HttpProtoReflectionBridge _reflectionBridge;
    private readonly MessagePackSerializerOptions _serializerOptions;

    public HttpMessagePackMessageDispatcher(Scene scene, HttpProtoReflectionBridge reflectionBridge, HttpRpcOptions options)
    {
        _scene = scene;
        _reflectionBridge = reflectionBridge;
        _serializerOptions = HttpServicesHandler.ConfigureMessagePackOptions(options.MessagePack);
    }

    public async Task<HttpMessagePackDispatchResult> DispatchAsync(HttpContext httpContext, HttpProtoSessionLease sessionLease, string? routeMessageName, CancellationToken cancellationToken)
    {
        var envelope = await MessagePackSerializer.DeserializeAsync<HttpMessagePackRpcRequestEnvelope>(httpContext.Request.Body, _serializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("HTTP messagepack rpc request body is required.");

        var dispatcher = _reflectionBridge.GetMessageDispatcher(_scene);
        var messageType = _reflectionBridge.GetMessageType(dispatcher, envelope.ProtocolCode)
            ?? throw new InvalidOperationException($"Fantasy message type for protocolCode:{envelope.ProtocolCode} was not found.");

        EnsureMessageName(routeMessageName, "Route", messageType);
        EnsureMessageName(envelope.MessageName, "Body", messageType);

        var message = DeserializeBody(envelope.Body, messageType, envelope.ProtocolCode);
        var isRequest = typeof(IRequest).IsAssignableFrom(messageType);

        // Reuse the proto request context so downstream Fantasy code can keep writing responses through
        // Session.Send without caring whether the transport was JSON, MessagePack, or raw binary.
        sessionLease.Network.BindRequest(sessionLease.RequestContext);

        await HttpProtoSceneDispatcher.RunAsync(_scene, () =>
            _reflectionBridge.DispatchAsync(dispatcher, sessionLease.Session, envelope.ProtocolCode, envelope.RpcId, message));

        if (!isRequest)
        {
            return HttpMessagePackDispatchResult.Empty;
        }

        if (!sessionLease.RequestContext.HasResponse)
        {
            throw new InvalidOperationException($"Fantasy request handler for protocolCode:{envelope.ProtocolCode} did not produce a response packet.");
        }

        var responseBody = sessionLease.RequestContext.GetResponseBytes().ToArray();
        var responsePacket = HttpProtoPacketCodec.Parse(responseBody);
        var responseType = _reflectionBridge.GetMessageType(dispatcher, responsePacket.ProtocolCode)
            ?? throw new InvalidOperationException($"Fantasy message type for protocolCode:{responsePacket.ProtocolCode} was not found.");
        var responseMessage = HttpProtoPacketCodec.Deserialize(responsePacket, responseType);

        try
        {
            var messageBody = MessagePackSerializer.Serialize(responseType, responseMessage, _serializerOptions, cancellationToken);
            var responseEnvelope = new HttpMessagePackRpcResponseEnvelope
            {
                ProtocolCode = responsePacket.ProtocolCode,
                RpcId = responsePacket.RpcId,
                MessageName = responseType.Name,
                Body = messageBody
            };

            return new HttpMessagePackDispatchResult(true, responseEnvelope);
        }
        finally
        {
            if (responseMessage is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private void EnsureMessageName(string? actualMessageName, string source, Type messageType)
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

    private object DeserializeBody(byte[]? body, Type messageType, uint protocolCode)
    {
        if (body is null || body.Length == 0)
        {
            return Activator.CreateInstance(messageType)
                ?? throw new InvalidOperationException($"Failed to create Fantasy message {messageType.FullName} from an empty messagepack body.");
        }

        var message = MessagePackSerializer.Deserialize(messageType, body, _serializerOptions);
        return message ?? throw new InvalidOperationException($"Failed to deserialize Fantasy message {messageType.FullName} from messagepack body for protocolCode:{protocolCode}.");
    }
}

public readonly record struct HttpMessagePackDispatchResult(bool HasResponse, HttpMessagePackRpcResponseEnvelope? ResponseEnvelope)
{
    public static HttpMessagePackDispatchResult Empty => new(false, null);
}

/// <summary>
/// Wire format accepted by <c>/http/messagepack/rpc</c>.
/// </summary>
[MessagePackObject]
public sealed class HttpMessagePackRpcRequestEnvelope
{
    [Key(0)]
    public uint ProtocolCode { get; init; }

    [Key(1)]
    public uint RpcId { get; init; }

    [Key(2)]
    public string? MessageName { get; init; }

    [Key(3)]
    public byte[]? Body { get; init; }
}

/// <summary>
/// Wire format returned by <c>/http/messagepack/rpc</c> when a Fantasy request message produces a reply.
/// </summary>
[MessagePackObject]
public sealed class HttpMessagePackRpcResponseEnvelope
{
    [Key(0)]
    public uint ProtocolCode { get; init; }

    [Key(1)]
    public uint RpcId { get; init; }

    [Key(2)]
    public string MessageName { get; init; } = string.Empty;

    [Key(3)]
    public byte[] Body { get; init; } = Array.Empty<byte>();
}

[MessagePackObject]
public sealed class HttpMessagePackErrorEnvelope
{
    [Key(0)]
    public string Title { get; init; } = string.Empty;

    [Key(1)]
    public int Status { get; init; }

    [Key(2)]
    public string TraceId { get; init; } = string.Empty;

    [Key(3)]
    public string? Detail { get; init; }
}
