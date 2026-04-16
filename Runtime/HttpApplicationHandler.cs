using Fantasy;
using Fantasy.Async;
using Fantasy.Event;
using Fantasy.Network.HTTP;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Entities.Http.Rpc;

public sealed class HttpApplicationHandler : AsyncEventSystem<OnConfigureHttpApplication>
{
    protected override async FTask Handler(OnConfigureHttpApplication self)
    {
        var app = self.Application;
        var options = app.Services.GetRequiredService<HttpRpcOptions>();

        if (options.ForwardedHeaders.Enabled)
        {
            app.UseForwardedHeaders();
        }

        app.UseExceptionHandler();

        if (options.Cors.Enabled)
        {
            app.UseCors(HttpRpcOptions.CorsPolicyName);
        }

        app.Use(async (context, next) =>
        {
            if (string.IsNullOrWhiteSpace(context.TraceIdentifier))
            {
                context.TraceIdentifier = Guid.NewGuid().ToString("N");
            }

            if (options.Observability.IncludeTraceIdentifierResponseHeader)
            {
                context.Response.Headers[options.Observability.TraceIdentifierHeaderName] = context.TraceIdentifier;
            }

            await next.Invoke();
        });

        if (options.Observability.RequestLoggingEnabled)
        {
            app.Use(async (context, next) =>
            {
                var start = DateTime.UtcNow;
                var traceId = context.TraceIdentifier;

                try
                {
                    await next.Invoke();
                    LogRequestCompletion(context, traceId, start, options);
                }
                catch
                {
                    LogRequestCompletion(context, traceId, start, options);
                    throw;
                }
            });
        }

        if (options.Auth.Enabled)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        if (options.ResponseHeaders is { Enabled: true, Headers.Count: > 0 })
        {
            app.Use(async (context, next) =>
            {
                foreach (var header in options.ResponseHeaders.Headers)
                {
                    context.Response.Headers[header.Key] = header.Value;
                }

                await next.Invoke();
            });
        }

        if (options.HealthChecks.Enabled)
        {
            var endpoint = app.MapHealthChecks(options.HealthChecks.Path);
            if (options.HealthChecks.AllowAnonymous)
            {
                endpoint.AllowAnonymous();
            }
        }

        Log.Info($"[HTTP] HTTP RPC pipeline configured: Scene {self.Scene.SceneConfigId}, AuthEnabled={options.Auth.Enabled}, HealthChecksEnabled={options.HealthChecks.Enabled}");

        await FTask.CompletedTask;
    }

    private static void LogRequestCompletion(HttpContext context, string traceId, DateTime start, HttpRpcOptions options)
    {
        var duration = (DateTime.UtcNow - start).TotalMilliseconds;
        var ip = options.Observability.IncludeClientIp ? context.Connection.RemoteIpAddress?.ToString() : "hidden";
        Log.Info($"[HTTP-{traceId}] {context.Request.Method} {context.Request.Path} responded {context.Response.StatusCode} in {duration:F2}ms from {ip}");
    }
}
