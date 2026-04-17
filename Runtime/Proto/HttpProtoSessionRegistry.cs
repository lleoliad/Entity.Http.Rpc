using System.Collections.Concurrent;
using System.Net;
using Fantasy;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Network;
using Microsoft.AspNetCore.Http;

namespace Entities.Http.Rpc;

public sealed class HttpProtoSessionRegistry
{
    private readonly Scene _scene;
    private readonly HttpRpcOptions _options;
    private readonly HttpProtoReflectionBridge _reflectionBridge;
    private readonly ConcurrentDictionary<string, HttpProtoSessionEntry> _sessions = new(StringComparer.Ordinal);

    public HttpProtoSessionRegistry(Scene scene, HttpRpcOptions options, HttpProtoReflectionBridge reflectionBridge)
    {
        _scene = scene;
        _options = options;
        _reflectionBridge = reflectionBridge;
    }

    public async Task<HttpProtoSessionLease> AcquireAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var headerName = _options.Proto.SessionHeaderName;
        var sessionId = context.Request.Headers[headerName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            if (_options.Proto.RequireExistingSession)
            {
                throw new HttpProtoSessionException("HTTP proto session header is required.", _options.Proto.InvalidSessionStatusCode);
            }

            sessionId = Guid.NewGuid().ToString("N");
        }

        while (true)
        {
            if (!_sessions.TryGetValue(sessionId, out var entry))
            {
                var createdEntry = await CreateEntryAsync(sessionId, context);

                if (!_sessions.TryAdd(sessionId, createdEntry))
                {
                    await DisposeEntryAsync(createdEntry);
                    continue;
                }

                entry = createdEntry;
            }

            await entry.Gate.WaitAsync(cancellationToken);

            try
            {
                if (entry.IsDisposed)
                {
                    _sessions.TryRemove(sessionId, out _);
                    continue;
                }

                if (IsExpired(entry))
                {
                    await DisposeEntryAsync(entry);
                    _sessions.TryRemove(sessionId, out _);
                    throw new HttpProtoSessionException("HTTP proto session has expired.", _options.Proto.InvalidSessionStatusCode);
                }

                entry.LastAccessUtc = DateTime.UtcNow;
                context.Response.Headers[headerName] = sessionId;
                return new HttpProtoSessionLease(this, entry);
            }
            catch
            {
                if (!entry.IsDisposed && entry.Gate.CurrentCount == 0)
                {
                    entry.Gate.Release();
                }

                throw;
            }
        }
    }

    public async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        foreach (var pair in _sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = pair.Value;

            if (!IsExpired(entry))
            {
                continue;
            }

            if (!await entry.Gate.WaitAsync(0, cancellationToken))
            {
                continue;
            }

            try
            {
                if (!IsExpired(entry))
                {
                    continue;
                }

                await DisposeEntryAsync(entry);
                _sessions.TryRemove(pair.Key, out _);
            }
            finally
            {
                if (!entry.IsDisposed)
                {
                    entry.Gate.Release();
                }
            }
        }
    }

    internal void Release(HttpProtoSessionEntry entry)
    {
        entry.Network.ClearRequest();
        entry.RequestContext.Clear();
        entry.LastAccessUtc = DateTime.UtcNow;

        if (!entry.IsDisposed)
        {
            entry.Gate.Release();
        }
    }

    private Task<HttpProtoSessionEntry> CreateEntryAsync(string sessionId, HttpContext context)
    {
        var remoteEndPoint = new IPEndPoint(context.Connection.RemoteIpAddress ?? IPAddress.Loopback, context.Connection.RemotePort);
        return HttpProtoSceneDispatcher.RunAsync(_scene, () =>
        {
            var network = Entity.Create<HttpPseudoClientNetwork>(_scene, false, false);
            network.InitializeForHttp();
            network.SessionId = sessionId;

            var session = _reflectionBridge.CreateSession(network, remoteEndPoint);
            return FTask<HttpProtoSessionEntry>.FromResult(new HttpProtoSessionEntry(sessionId, network, session));
        });
    }

    private bool IsExpired(HttpProtoSessionEntry entry)
    {
        return DateTime.UtcNow - entry.LastAccessUtc > TimeSpan.FromSeconds(_options.Proto.SessionIdleTimeoutSeconds);
    }

    private Task DisposeEntryAsync(HttpProtoSessionEntry entry)
    {
        entry.IsDisposed = true;
        return HttpProtoSceneDispatcher.RunAsync(_scene, async () =>
        {
            try
            {
                if (!entry.Session.IsDisposed)
                {
                    entry.Session.Dispose();
                }
            }
            catch (Exception exception)
            {
                Log.Error($"[HTTP] Failed to dispose proto session:{entry.SessionId}. {exception}");
            }

            await FTask.CompletedTask;
        });
    }
}

public sealed class HttpProtoSessionLease : IAsyncDisposable
{
    private readonly HttpProtoSessionRegistry _registry;
    private readonly HttpProtoSessionEntry _entry;
    private bool _disposed;

    internal HttpProtoSessionLease(HttpProtoSessionRegistry registry, HttpProtoSessionEntry entry)
    {
        _registry = registry;
        _entry = entry;
    }

    public Session Session => _entry.Session;
    public HttpPseudoClientNetwork Network => _entry.Network;
    public HttpProtoRequestContext RequestContext => _entry.RequestContext;
    public string SessionId => _entry.SessionId;

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _registry.Release(_entry);
        return ValueTask.CompletedTask;
    }
}

internal sealed class HttpProtoSessionEntry
{
    public HttpProtoSessionEntry(string sessionId, HttpPseudoClientNetwork network, Session session)
    {
        SessionId = sessionId;
        Network = network;
        Session = session;
    }

    public string SessionId { get; }
    public HttpPseudoClientNetwork Network { get; }
    public Session Session { get; }
    public HttpProtoRequestContext RequestContext { get; } = new();
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public DateTime LastAccessUtc { get; set; } = DateTime.UtcNow;
    public bool IsDisposed { get; set; }
}

public sealed class HttpProtoSessionException : Exception
{
    public HttpProtoSessionException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
