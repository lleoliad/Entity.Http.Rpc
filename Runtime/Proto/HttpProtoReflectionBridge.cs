using System.Net;
using System.Reflection;
using Fantasy;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Entities.Http.Rpc;

public sealed class HttpProtoReflectionBridge
{
    private static readonly MethodInfo SessionCreateMethod = typeof(Session).GetMethod(
        "Create",
        BindingFlags.Static | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(AClientNetwork), typeof(IPEndPoint)],
        modifiers: null)!;

    private static readonly PropertyInfo SceneMessageDispatcherProperty = typeof(Scene).GetProperty(
        "MessageDispatcherComponent",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo MessageDispatcherGetOpCodeTypeMethod = typeof(MessageDispatcherComponent).GetMethod(
        "GetOpCodeType",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo MessageHandlerDictionaryField = typeof(MessageDispatcherComponent).GetField(
        "_messageHandlerDictionary",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    public Session CreateSession(HttpPseudoClientNetwork network, IPEndPoint remoteEndPoint)
    {
        return (Session)SessionCreateMethod.Invoke(null, [network, remoteEndPoint])!;
    }

    public MessageDispatcherComponent GetMessageDispatcher(Scene scene)
    {
        return (MessageDispatcherComponent)SceneMessageDispatcherProperty.GetValue(scene)!;
    }

    public Type? GetMessageType(MessageDispatcherComponent dispatcher, uint protocolCode)
    {
        return (Type?)MessageDispatcherGetOpCodeTypeMethod.Invoke(dispatcher, [protocolCode]);
    }

    public async FTask DispatchAsync(MessageDispatcherComponent dispatcher, Session session, uint protocolCode, uint rpcId, object message)
    {
        var messageHandlerDictionary = MessageHandlerDictionaryField.GetValue(dispatcher)
            ?? throw new InvalidOperationException("Fantasy message handler dictionary is not initialized.");

        var tryGetValueMethod = messageHandlerDictionary.GetType().GetMethod("TryGetValue")
            ?? throw new InvalidOperationException("Fantasy message handler dictionary does not expose TryGetValue.");

        var arguments = new object?[] { protocolCode, null };
        var resolved = (bool)tryGetValueMethod.Invoke(messageHandlerDictionary, arguments)!;

        if (!resolved || arguments[1] is not Func<Session, uint, object, FTask> handler)
        {
            throw new InvalidOperationException($"Fantasy handler for protocolCode:{protocolCode} was not found.");
        }

        await handler(session, rpcId, message);
    }
}
