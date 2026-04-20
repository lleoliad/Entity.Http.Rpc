using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fantasy;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.PacketParser;
using Fantasy.Serialize;

namespace Entities.Http.Rpc;

/// <summary>
/// Minimal <see cref="AClientNetwork"/> implementation that lets Fantasy believe it is talking to a
/// client session while ASP.NET Core captures the serialized reply in-memory.
/// </summary>
public sealed class HttpPseudoClientNetwork : AClientNetwork
{
    private HttpProtoRequestContext? _currentRequestContext;

    public string SessionId { get; set; } = string.Empty;

    public void InitializeForHttp()
    {
        Initialize(NetworkType.Client, NetworkProtocolType.TCP, NetworkTarget.Outer);
    }

    public void BindRequest(HttpProtoRequestContext requestContext)
    {
        _currentRequestContext = requestContext;
    }

    public void ClearRequest()
    {
        _currentRequestContext = null;
    }

    public override Session Connect(string remoteAddress, Action onConnectComplete, Action onConnectFail, Action onConnectDisconnect, bool isHttps, int connectTimeout = 5000)
    {
        throw new NotSupportedException("HTTP pseudo network does not support Connect.");
    }

    public override void Send(uint rpcId, long address, MemoryStreamBuffer? memoryStream, IMessage? message, Type messageType)
    {
        try
        {
            if (_currentRequestContext is null)
            {
                memoryStream?.Dispose();
                message?.Dispose();
                Log.Warning($"[HTTP] Session:{SessionId} attempted to send without an active HTTP request context.");
                return;
            }

            // If the handler already prepared a packet buffer, reuse it; otherwise serialize the response
            // exactly the same way the real network pipeline would.
            var packet = memoryStream ?? PackMessage(rpcId, message!, messageType);
            _currentRequestContext.TryWriteResponse(packet);
        }
        catch (Exception exception)
        {
            memoryStream?.Dispose();
            message?.Dispose();
            Log.Error($"[HTTP] Failed to write proto response for session:{SessionId}. {exception}");
        }
    }

    public override void RemoveChannel(uint channelId)
    {
    }

    private MemoryStreamBuffer PackMessage(uint rpcId, IMessage message, Type messageType)
    {
        var memoryStream = MemoryStreamBufferPool.RentMemoryStream(MemoryStreamBufferSource.Pack);
        memoryStream.Seek(Packet.OuterPacketHeadLength, SeekOrigin.Begin);

        var opCode = message.OpCode();
        OpCodeIdStruct opCodeIdStruct = opCode;
        var memoryStreamLength = 0;

        if (SerializerManager.TrySerialize(opCodeIdStruct.OpCodeProtocolType, messageType, message, memoryStream, out var error))
        {
            memoryStreamLength = (int)memoryStream.Position;
        }
        else
        {
            message.Dispose();
            throw new InvalidOperationException($"Failed to serialize {messageType.FullName}: {error}");
        }

        var packetBodyCount = memoryStreamLength - Packet.OuterPacketHeadLength;

        if (packetBodyCount == 0)
        {
            // Fantasy encodes an empty body as -1 in the outer packet header.
            packetBodyCount = -1;
        }

        if (packetBodyCount > ProgramDefine.MaxMessageSize)
        {
            message.Dispose();
            throw new InvalidOperationException($"Message content exceeds {ProgramDefine.MaxMessageSize} bytes.");
        }

        var buffer = memoryStream.GetBuffer();
        ref var bufferRef = ref MemoryMarshal.GetArrayDataReference(buffer);
        Unsafe.WriteUnaligned(ref bufferRef, packetBodyCount);
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref bufferRef, Packet.PacketLength), opCode);
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref bufferRef, Packet.OuterPacketRpcIdLocation), rpcId);

        message.Dispose();
        return memoryStream;
    }
}
