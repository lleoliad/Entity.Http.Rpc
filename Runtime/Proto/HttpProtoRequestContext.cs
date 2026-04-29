using Fantasy;
using Fantasy.Serialize;

namespace Entities.Http.Rpc;

/// <summary>
/// Temporary per-request storage for Fantasy packets written by handlers while an HTTP request is active.
/// </summary>
public sealed class HttpProtoRequestContext
{
    private readonly List<MemoryStreamBuffer> _responseBuffers = [];

    public bool HasResponse => _responseBuffers.Count > 0;
    public int ResponseCount => _responseBuffers.Count;

    public ReadOnlyMemory<byte> GetResponseBytes()
    {
        if (_responseBuffers.Count == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (_responseBuffers.Count == 1)
        {
            var responseBuffer = _responseBuffers[0];
            return new ReadOnlyMemory<byte>(responseBuffer.GetBuffer(), 0, (int)responseBuffer.Position);
        }

        var length = _responseBuffers.Sum(buffer => (int)buffer.Position);
        var body = new byte[length];
        var offset = 0;

        foreach (var responseBuffer in _responseBuffers)
        {
            var count = (int)responseBuffer.Position;
            Buffer.BlockCopy(responseBuffer.GetBuffer(), 0, body, offset, count);
            offset += count;
        }

        return body;
    }

    public bool TryWriteResponse(MemoryStreamBuffer responseBuffer)
    {
        _responseBuffers.Add(responseBuffer);
        return true;
    }

    public void Clear()
    {
        foreach (var responseBuffer in _responseBuffers)
        {
            responseBuffer.Dispose();
        }

        _responseBuffers.Clear();
    }
}
