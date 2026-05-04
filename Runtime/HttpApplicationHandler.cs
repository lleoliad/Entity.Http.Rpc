using Fantasy;
using Fantasy.Async;
using Fantasy.Event;
using Fantasy.Network.HTTP;
using MessagePack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Entities.Http.Rpc;

/// <summary>
/// Builds the ASP.NET Core middleware pipeline and maps the HTTP JSON/MessagePack/Proto RPC endpoints.
/// </summary>
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
                // Echo the trace id so API clients can correlate server-side logs with a specific response.
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

        app.MapPost("/http/json/rpc", context => HandleEncryptedRpcRequestAsync(context, () => HandleJsonRpcRequestAsync(context, null)));
        app.MapPost("/http/json/rpc/{messageName}", context =>
        {
            var routeMessageName = context.Request.RouteValues.TryGetValue("messageName", out var value) ? value?.ToString() : null;
            return HandleEncryptedRpcRequestAsync(context, () => HandleJsonRpcRequestAsync(context, routeMessageName));
        });

        app.MapPost("/http/messagepack/rpc", context => HandleEncryptedRpcRequestAsync(context, () => HandleMessagePackRpcRequestAsync(context, null)));
        app.MapPost("/http/messagepack/rpc/{messageName}", context =>
        {
            var routeMessageName = context.Request.RouteValues.TryGetValue("messageName", out var value) ? value?.ToString() : null;
            return HandleEncryptedRpcRequestAsync(context, () => HandleMessagePackRpcRequestAsync(context, routeMessageName));
        });

        app.MapPost("/http/memorypack/rpc", context => HandleEncryptedRpcRequestAsync(context, () => HandleMemoryPackRpcRequestAsync(context, null)));
        app.MapPost("/http/memorypack/rpc/{messageName}", context =>
        {
            var routeMessageName = context.Request.RouteValues.TryGetValue("messageName", out var value) ? value?.ToString() : null;
            return HandleEncryptedRpcRequestAsync(context, () => HandleMemoryPackRpcRequestAsync(context, routeMessageName));
        });

        app.MapPost("/http/proto/rpc", context => HandleEncryptedRpcRequestAsync(context, () => HandleProtoRpcRequestAsync(context, null)));
        app.MapPost("/http/proto/rpc/{messageName}", context =>
        {
            var routeMessageName = context.Request.RouteValues.TryGetValue("messageName", out var value) ? value?.ToString() : null;
            return HandleEncryptedRpcRequestAsync(context, () => HandleProtoRpcRequestAsync(context, routeMessageName));
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

    private static async Task HandleEncryptedRpcRequestAsync(HttpContext context, Func<Task> next)
    {
        var options = context.RequestServices.GetRequiredService<HttpRpcOptions>();
        var payloadProtector = context.RequestServices.GetRequiredService<HttpRpcPayloadProtector>();

        if (!payloadProtector.Enabled)
        {
            await next.Invoke();
            return;
        }

        var originalRequestBody = context.Request.Body;
        var originalResponseBody = context.Response.Body;

        try
        {
            using var encryptedRequest = new MemoryStream();
            await context.Request.Body.CopyToAsync(encryptedRequest, context.RequestAborted);

            if (!payloadProtector.TryUnprotect(encryptedRequest.ToArray(), out var requestBody))
            {
                context.Response.StatusCode = options.Encryption.DecryptionFailureStatusCode;
                await context.Response.WriteAsync("Encrypted HTTP RPC request body is invalid.", context.RequestAborted);
                return;
            }

            await using var decryptedRequest = new MemoryStream(requestBody, writable: false);
            await using var responseBuffer = new MemoryStream();
            context.Request.Body = decryptedRequest;
            context.Response.Body = responseBuffer;

            await next.Invoke();

            context.Response.Body = originalResponseBody;

            if (responseBuffer.Length == 0)
            {
                return;
            }

            var encryptedResponse = payloadProtector.Protect(responseBuffer.ToArray());
            context.Response.ContentType = options.Encryption.EncryptedContentType;
            context.Response.ContentLength = encryptedResponse.Length;
            await context.Response.Body.WriteAsync(encryptedResponse, context.RequestAborted);
        }
        finally
        {
            context.Request.Body = originalRequestBody;
            context.Response.Body = originalResponseBody;
        }
    }

    private async Task HandleJsonRpcRequestAsync(HttpContext context, string? routeMessageName)
    {
        var options = context.RequestServices.GetRequiredService<HttpRpcOptions>();
        var sessionRegistry = context.RequestServices.GetRequiredService<HttpProtoSessionRegistry>();
        var messageDispatcher = context.RequestServices.GetRequiredService<HttpJsonMessageDispatcher>();

        CancellationTokenSource? timeoutCts = null;
        var effectiveToken = context.RequestAborted;

        if (options.DispatchTimeoutSeconds > 0)
        {
            timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.DispatchTimeoutSeconds));
            effectiveToken = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, timeoutCts.Token).Token;
        }

        try
        {
            // JSON and Proto requests intentionally share the same session registry so handlers that rely
            // on the Fantasy Session abstraction behave the same no matter which wire format reached them.
            await using var sessionLease = await sessionRegistry.AcquireAsync(context, effectiveToken);
            var dispatchResult = await messageDispatcher.DispatchAsync(context, sessionLease, routeMessageName, effectiveToken);

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
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status504GatewayTimeout, "Server processing timeout.", null);
        }
        catch (Exception exception)
        {
            var detail = options.ErrorHandling.IncludeExceptionDetails ? exception.ToString() : null;
            await WriteJsonErrorAsync(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.", detail);
        }
        finally
        {
            timeoutCts?.Dispose();
        }

        await FTask.CompletedTask;
    }

    private async Task HandleMessagePackRpcRequestAsync(HttpContext context, string? routeMessageName)
    {
        var options = context.RequestServices.GetRequiredService<HttpRpcOptions>();
        var sessionRegistry = context.RequestServices.GetRequiredService<HttpProtoSessionRegistry>();
        var messageDispatcher = context.RequestServices.GetRequiredService<HttpMessagePackMessageDispatcher>();

        if (!options.MessagePack.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        CancellationTokenSource? timeoutCts = null;
        var effectiveToken = context.RequestAborted;

        if (options.DispatchTimeoutSeconds > 0)
        {
            timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.DispatchTimeoutSeconds));
            effectiveToken = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, timeoutCts.Token).Token;
        }

        try
        {
            await using var sessionLease = await sessionRegistry.AcquireAsync(context, effectiveToken);
            var dispatchResult = await messageDispatcher.DispatchAsync(context, sessionLease, routeMessageName, effectiveToken);

            if (!dispatchResult.HasResponse || dispatchResult.ResponseEnvelope is null)
            {
                context.Response.StatusCode = options.Proto.EmptyMessageStatusCode;
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = options.MessagePack.ContentType;
            await MessagePackSerializer.SerializeAsync(context.Response.Body, dispatchResult.ResponseEnvelope, HttpServicesHandler.ConfigureMessagePackOptions(options.MessagePack), context.RequestAborted);
        }
        catch (HttpProtoSessionException exception)
        {
            await WriteMessagePackErrorAsync(context, options, exception.StatusCode, exception.Message, null);
        }
        catch (InvalidOperationException exception)
        {
            await WriteMessagePackErrorAsync(context, options, StatusCodes.Status400BadRequest, exception.Message, null);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            await WriteMessagePackErrorAsync(context, options, StatusCodes.Status504GatewayTimeout, "Server processing timeout.", null);
        }
        catch (Exception exception)
        {
            var detail = options.ErrorHandling.IncludeExceptionDetails ? exception.ToString() : null;
            await WriteMessagePackErrorAsync(context, options, StatusCodes.Status500InternalServerError, "An unexpected error occurred.", detail);
        }
        finally
        {
            timeoutCts?.Dispose();
        }

        await FTask.CompletedTask;
    }

    private async Task HandleMemoryPackRpcRequestAsync(HttpContext context, string? routeMessageName)
    {
        var options = context.RequestServices.GetRequiredService<HttpRpcOptions>();
        var sessionRegistry = context.RequestServices.GetRequiredService<HttpProtoSessionRegistry>();
        var messageDispatcher = context.RequestServices.GetRequiredService<HttpMemoryPackMessageDispatcher>();

        if (!options.MemoryPack.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        CancellationTokenSource? timeoutCts = null;
        var effectiveToken = context.RequestAborted;

        if (options.DispatchTimeoutSeconds > 0)
        {
            timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.DispatchTimeoutSeconds));
            effectiveToken = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, timeoutCts.Token).Token;
        }

        try
        {
            await using var sessionLease = await sessionRegistry.AcquireAsync(context, effectiveToken);
            var dispatchResult = await messageDispatcher.DispatchAsync(context, sessionLease, routeMessageName, effectiveToken);

            if (!dispatchResult.HasResponse || dispatchResult.ResponseEnvelope is null)
            {
                context.Response.StatusCode = options.Proto.EmptyMessageStatusCode;
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = options.MemoryPack.ContentType;
            var responseBody = HttpMemoryPackMessageCodec.SerializeResponseEnvelope(dispatchResult.ResponseEnvelope);
            await context.Response.BodyWriter.WriteAsync(responseBody, context.RequestAborted);
        }
        catch (HttpProtoSessionException exception)
        {
            await WriteMemoryPackErrorAsync(context, options, exception.StatusCode, exception.Message, null);
        }
        catch (InvalidOperationException exception)
        {
            await WriteMemoryPackErrorAsync(context, options, StatusCodes.Status400BadRequest, exception.Message, null);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            await WriteMemoryPackErrorAsync(context, options, StatusCodes.Status504GatewayTimeout, "Server processing timeout.", null);
        }
        catch (Exception exception)
        {
            var detail = options.ErrorHandling.IncludeExceptionDetails ? exception.ToString() : null;
            await WriteMemoryPackErrorAsync(context, options, StatusCodes.Status500InternalServerError, "An unexpected error occurred.", detail);
        }
        finally
        {
            timeoutCts?.Dispose();
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

        CancellationTokenSource? timeoutCts = null;
        var effectiveToken = context.RequestAborted;

        if (options.DispatchTimeoutSeconds > 0)
        {
            timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.DispatchTimeoutSeconds));
            effectiveToken = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, timeoutCts.Token).Token;
        }

        try
        {
            await using var sessionLease = await sessionRegistry.AcquireAsync(context, effectiveToken);
            var dispatchResult = await messageDispatcher.DispatchAsync(context, sessionLease, routeMessageName, effectiveToken);

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
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await context.Response.WriteAsync("Server processing timeout.", context.RequestAborted);
        }
        catch (Exception exception)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync(exception.ToString(), context.RequestAborted);
        }
        finally
        {
            timeoutCts?.Dispose();
        }

        await FTask.CompletedTask;
    }

    private static Task WriteJsonErrorAsync(HttpContext context, int statusCode, string title, string? detail)
    {
        context.Response.StatusCode = statusCode;
        // Keep JSON error payloads minimal and stable so HTTP clients can consume them without depending
        // on ASP.NET Core ProblemDetails semantics.
        return context.Response.WriteAsJsonAsync(new
        {
            title,
            status = statusCode,
            traceId = context.TraceIdentifier,
            detail
        }, context.RequestAborted);
    }

    private static Task WriteMemoryPackErrorAsync(HttpContext context, HttpRpcOptions options, int statusCode, string title, string? detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = options.MemoryPack.ContentType;
        var body = HttpMemoryPackMessageCodec.SerializeErrorEnvelope(new HttpMemoryPackErrorEnvelope(title, statusCode, context.TraceIdentifier, detail));
        return context.Response.BodyWriter.WriteAsync(body, context.RequestAborted).AsTask();
    }

    private static Task WriteMessagePackErrorAsync(HttpContext context, HttpRpcOptions options, int statusCode, string title, string? detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = options.MessagePack.ContentType;

        return MessagePackSerializer.SerializeAsync(context.Response.Body, new HttpMessagePackErrorEnvelope
        {
            Title = title,
            Status = statusCode,
            TraceId = context.TraceIdentifier,
            Detail = detail
        }, HttpServicesHandler.ConfigureMessagePackOptions(options.MessagePack), context.RequestAborted);
    }
}
