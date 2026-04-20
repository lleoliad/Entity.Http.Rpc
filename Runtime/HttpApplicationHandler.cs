using Fantasy;
using Fantasy.Async;
using Fantasy.Event;
using Fantasy.Network.HTTP;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

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

        app.MapPost("/http/json/rpc", context => HandleJsonRpcRequestAsync(context, null));
        app.MapPost("/http/json/rpc/{messageName}", context =>
        {
            var routeMessageName = context.Request.RouteValues.TryGetValue("messageName", out var value) ? value?.ToString() : null;
            return HandleJsonRpcRequestAsync(context, routeMessageName);
        });

        app.MapPost("/http/proto/rpc", context => HandleProtoRpcRequestAsync(context, null));
        app.MapPost("/http/proto/rpc/{messageName}", context =>
        {
            var routeMessageName = context.Request.RouteValues.TryGetValue("messageName", out var value) ? value?.ToString() : null;
            return HandleProtoRpcRequestAsync(context, routeMessageName);
        });

        Log.Info($"[HTTP] HTTP RPC pipeline configured: Scene {self.Scene.SceneConfigId}, AuthEnabled={options.Auth.Enabled}, HealthChecksEnabled={options.HealthChecks.Enabled}");

        await FTask.CompletedTask;
    }

    private static void LogRequestCompletion(HttpContext context, string traceId, DateTime start, HttpRpcOptions options)
    {
        var duration = (DateTime.UtcNow - start).TotalMilliseconds;
        var ip = options.Observability.IncludeClientIp ? context.Connection.RemoteIpAddress?.ToString() : "hidden";
        Log.Info($"[HTTP-{traceId}] {context.Request.Method} {context.Request.Path} responded {context.Response.StatusCode} in {duration:F2}ms from {ip}");
    }

    private async Task HandleJsonRpcRequestAsync(HttpContext context, string? routeMessageName)
    {
        var options = context.RequestServices.GetRequiredService<HttpRpcOptions>();
        var sessionRegistry = context.RequestServices.GetRequiredService<HttpProtoSessionRegistry>();
        var messageDispatcher = context.RequestServices.GetRequiredService<HttpJsonMessageDispatcher>();

        try
        {
            await using var sessionLease = await sessionRegistry.AcquireAsync(context, context.RequestAborted);
            var dispatchResult = await messageDispatcher.DispatchAsync(context, sessionLease, routeMessageName, context.RequestAborted);

            if (!dispatchResult.HasResponse || dispatchResult.ResponseEnvelope is null)
            {
                context.Response.StatusCode = options.Proto.EmptyMessageStatusCode;
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(dispatchResult.ResponseEnvelope, context.RequestAborted);
        }
        catch (HttpProtoSessionException exception)
        {
            await WriteJsonErrorAsync(context, exception.StatusCode, exception.Message, null);
        }
        catch (InvalidOperationException exception)
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest, exception.Message, null);
        }
        catch (Exception exception)
        {
            var detail = options.ErrorHandling.IncludeExceptionDetails ? exception.ToString() : null;
            await WriteJsonErrorAsync(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.", detail);
        }

        await FTask.CompletedTask;
    }

    private async Task HandleProtoRpcRequestAsync(HttpContext context, string? routeMessageName)
    {
        var options = context.RequestServices.GetRequiredService<HttpRpcOptions>();
        var sessionRegistry = context.RequestServices.GetRequiredService<HttpProtoSessionRegistry>();
        var messageDispatcher = context.RequestServices.GetRequiredService<HttpProtoMessageDispatcher>();

        if (!options.Proto.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        try
        {
            await using var sessionLease = await sessionRegistry.AcquireAsync(context, context.RequestAborted);
            var dispatchResult = await messageDispatcher.DispatchAsync(context, sessionLease, routeMessageName, context.RequestAborted);

            if (!dispatchResult.HasResponse)
            {
                context.Response.StatusCode = options.Proto.EmptyMessageStatusCode;
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/octet-stream";
            await context.Response.BodyWriter.WriteAsync(dispatchResult.ResponseBody, context.RequestAborted);
        }
        catch (HttpProtoSessionException exception)
        {
            context.Response.StatusCode = exception.StatusCode;
            await context.Response.WriteAsync(exception.Message, context.RequestAborted);
        }
        catch (InvalidOperationException exception)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(exception.Message, context.RequestAborted);
        }
        catch (Exception exception)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync(exception.ToString(), context.RequestAborted);
        }

        await FTask.CompletedTask;
    }

    private static Task WriteJsonErrorAsync(HttpContext context, int statusCode, string title, string? detail)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new
        {
            title,
            status = statusCode,
            traceId = context.TraceIdentifier,
            detail
        }, context.RequestAborted);
    }
}
