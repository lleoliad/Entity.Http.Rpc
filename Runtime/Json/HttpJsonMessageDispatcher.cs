using System.Text.Json;
using Fantasy;
using Fantasy.Async;
using Fantasy.Network.Interface;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Entities.Http.Rpc;

/// <summary>
/// Adapts a JSON envelope into the corresponding Fantasy message invocation and converts request-style
/// replies back into JSON.
/// </summary>
public sealed class HttpJsonMessageDispatcher
{
    private readonly Scene _scene;
    private readonly HttpProtoReflectionBridge _reflectionBridge;
    private readonly JsonSerializerOptions _serializerOptions;

    public HttpJsonMessageDispatcher(Scene scene, HttpProtoReflectionBridge reflectionBridge, IOptions<JsonOptions> jsonOptions)
    {
        _scene = scene;
        _reflectionBridge = reflectionBridge;
        _serializerOptions = jsonOptions.Value.SerializerOptions;
    }

    public async Task<HttpJsonDispatchResult> DispatchAsync(HttpContext httpContext, HttpProtoSessionLease sessionLease, string? routeMessageName, CancellationToken cancellationToken)
    {
        var envelope = await JsonSerializer.DeserializeAsync<HttpJsonRpcRequestEnvelope>(httpContext.Request.Body, _serializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("HTTP json rpc request body is required.");

        var dispatcher = _reflectionBridge.GetMessageDispatcher(_scene);
        var messageType = _reflectionBridge.GetMessageType(dispatcher, envelope.ProtocolCode)
            ?? throw new InvalidOperationException($"Fantasy message type for protocolCode:{envelope.ProtocolCode} was not found.");

        EnsureMessageName(routeMessageName, "Route", messageType);
        EnsureMessageName(envelope.MessageName, "Body", messageType);

        var message = DeserializeBody(envelope.Body, messageType);
        var isRequest = typeof(IRequest).IsAssignableFrom(messageType);

        // Reuse the proto request context so downstream Fantasy code can keep writing responses through
        // Session.Send without caring whether the transport was JSON or raw binary.
        sessionLease.Network.BindRequest(sessionLease.RequestContext);

        await HttpProtoSceneDispatcher.RunAsync(_scene, () =>
            _reflectionBridge.DispatchMessageAsync(dispatcher, sessionLease.Network, sessionLease.Session, envelope.ProtocolCode, envelope.RpcId, message, messageType));

        if (!isRequest)
        {
            return HttpJsonDispatchResult.Empty;
        }

        if (!sessionLease.RequestContext.HasResponse)
        {
            throw new InvalidOperationException($"Fantasy request handler for protocolCode:{envelope.ProtocolCode} did not produce a response packet.");
        }

        var responsePacket = HttpProtoPacketCodec.FindResponsePacket(sessionLease.RequestContext.GetResponseBytes(), envelope.RpcId);
        var responseType = _reflectionBridge.GetMessageType(dispatcher, responsePacket.ProtocolCode)
            ?? throw new InvalidOperationException($"Fantasy message type for protocolCode:{responsePacket.ProtocolCode} was not found.");
        var responseMessage = HttpProtoPacketCodec.Deserialize(responsePacket, responseType);

        try
        {
            // The response is first materialized as a Fantasy packet by the pseudo network and then projected
            // back into JSON here, which guarantees both HTTP formats exercise the same server-side handlers.
            var responseElement = JsonSerializer.SerializeToElement(responseMessage, responseType, _serializerOptions);
            var responseEnvelope = new HttpJsonRpcResponseEnvelope(responsePacket.ProtocolCode, responsePacket.RpcId, responseType.Name, responseElement);
            return new HttpJsonDispatchResult(true, responseEnvelope);
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

    private object DeserializeBody(JsonElement body, Type messageType)
    {
        if (body.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Activator.CreateInstance(messageType)
                ?? throw new InvalidOperationException($"Failed to create Fantasy message {messageType.FullName} from an empty json body.");
        }

        var message = body.Deserialize(messageType, _serializerOptions);
        return message ?? throw new InvalidOperationException($"Failed to deserialize Fantasy message {messageType.FullName} from json body.");
    }
}

public readonly record struct HttpJsonDispatchResult(bool HasResponse, HttpJsonRpcResponseEnvelope? ResponseEnvelope)
{
    public static HttpJsonDispatchResult Empty => new(false, null);
}

/// <summary>
/// Wire format accepted by <c>/http/json/rpc</c>.
/// </summary>
internal sealed record HttpJsonRpcRequestEnvelope(uint ProtocolCode, uint RpcId, string? MessageName, JsonElement Body);

/// <summary>
/// Wire format returned by <c>/http/json/rpc</c> when a Fantasy request message produces a reply.
/// </summary>
public sealed record HttpJsonRpcResponseEnvelope(uint ProtocolCode, uint RpcId, string MessageName, JsonElement Body);
