using Fantasy;
using Fantasy.Serialize;

namespace Entities.Http.Rpc;

/// <summary>
/// Temporary per-request storage for the first response packet written by a Fantasy handler.
/// </summary>
public sealed class HttpProtoRequestContext
{
    private MemoryStreamBuffer? _responseBuffer;

    public bool HasResponse => _responseBuffer is not null;

    public ReadOnlyMemory<byte> GetResponseBytes()
    {
        if (_responseBuffer is null)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        return new ReadOnlyMemory<byte>(_responseBuffer.GetBuffer(), 0, (int)_responseBuffer.Position);
    }

    public bool TryWriteResponse(MemoryStreamBuffer responseBuffer)
    {
        if (_responseBuffer is not null)
        {
            // HTTP RPC expects at most one reply packet per request. Additional writes are discarded so the
            // ASP.NET Core endpoint can keep a deterministic response contract.
            responseBuffer.Dispose();
            Log.Warning("[HTTP] Multiple proto responses were produced for the same HTTP request. The later response was discarded.");
            return false;
        }

        _responseBuffer = responseBuffer;
        return true;
    }

    public void Clear()
    {
        _responseBuffer?.Dispose();
        _responseBuffer = null;
    }
}
