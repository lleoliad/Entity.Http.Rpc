using Fantasy;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Microsoft.AspNetCore.Http;

namespace Entities.Http.Rpc;

/// <summary>
/// Handles the raw binary HTTP endpoint by parsing Fantasy outer packets and invoking the scene dispatcher.
/// </summary>
public sealed class HttpProtoMessageDispatcher
{
    private readonly Scene _scene;
    private readonly HttpProtoReflectionBridge _reflectionBridge;

    public HttpProtoMessageDispatcher(Scene scene, HttpProtoReflectionBridge reflectionBridge)
    {
        _scene = scene;
        _reflectionBridge = reflectionBridge;
    }

    public async Task<HttpProtoDispatchResult> DispatchAsync(HttpContext httpContext, HttpProtoSessionLease sessionLease, string? routeMessageName, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(httpContext.Request, cancellationToken);
        var packet = HttpProtoPacketCodec.Parse(body);
        var dispatcher = _reflectionBridge.GetMessageDispatcher(_scene);
        var messageType = _reflectionBridge.GetMessageType(dispatcher, packet.ProtocolCode)
            ?? throw new InvalidOperationException($"Fantasy message type for protocolCode:{packet.ProtocolCode} was not found.");

        if (!string.IsNullOrWhiteSpace(routeMessageName) &&
            !string.Equals(routeMessageName, messageType.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Route message name '{routeMessageName}' does not match protocol message '{messageType.Name}'.");
        }

        var message = HttpProtoPacketCodec.Deserialize(packet, messageType);
        var isRequest = typeof(IRequest).IsAssignableFrom(messageType);

        // The pseudo network captures anything the Fantasy handler writes back through Session.Send.
        sessionLease.Network.BindRequest(sessionLease.RequestContext);

        await HttpProtoSceneDispatcher.RunAsync(_scene, () => DispatchOnSceneAsync(dispatcher, sessionLease.Session, packet.ProtocolCode, packet.RpcId, message));

        if (!isRequest)
        {
            return HttpProtoDispatchResult.Empty;
        }

        if (!sessionLease.RequestContext.HasResponse)
        {
            throw new InvalidOperationException($"Fantasy request handler for protocolCode:{packet.ProtocolCode} did not produce a response packet.");
        }

        return new HttpProtoDispatchResult(true, sessionLease.RequestContext.GetResponseBytes());
    }

    private async FTask DispatchOnSceneAsync(MessageDispatcherComponent dispatcher, Session session, uint protocolCode, uint rpcId, object message)
    {
        await _reflectionBridge.DispatchAsync(dispatcher, session, protocolCode, rpcId, message);
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await request.Body.CopyToAsync(memoryStream, cancellationToken);
        return memoryStream.ToArray();
    }
}

public readonly record struct HttpProtoDispatchResult(bool HasResponse, ReadOnlyMemory<byte> ResponseBody)
{
    public static HttpProtoDispatchResult Empty => new(false, ReadOnlyMemory<byte>.Empty);
}
