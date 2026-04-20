using Microsoft.Extensions.Hosting;

namespace Entities.Http.Rpc;

/// <summary>
/// Periodically removes idle pseudo sessions that were created for HTTP proto/json traffic.
/// </summary>
public sealed class HttpProtoSessionCleanupService : BackgroundService
{
    private readonly HttpRpcOptions _options;
    private readonly HttpProtoSessionRegistry _sessionRegistry;

    public HttpProtoSessionCleanupService(HttpRpcOptions options, HttpProtoSessionRegistry sessionRegistry)
    {
        _options = options;
        _sessionRegistry = sessionRegistry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Proto.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.Proto.SessionCleanupIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await _sessionRegistry.CleanupExpiredSessionsAsync(stoppingToken);
        }
    }
}
