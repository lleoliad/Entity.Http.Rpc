using Fantasy;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Entities.Http.Rpc;

public sealed class HttpRpcExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService? _problemDetailsService;
    private readonly HttpRpcOptions _options;

    public HttpRpcExceptionHandler(IProblemDetailsService? problemDetailsService, HttpRpcOptions options)
    {
        _problemDetailsService = problemDetailsService;
        _options = options;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;
        Log.Error($"[HTTP-{traceId}] Unhandled exception: {exception}");

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        if (!_options.ErrorHandling.UseProblemDetails || _problemDetailsService is null)
        {
            await httpContext.Response.WriteAsJsonAsync(new
            {
                title = "An unexpected error occurred.",
                status = StatusCodes.Status500InternalServerError,
                traceId,
                detail = _options.ErrorHandling.IncludeExceptionDetails ? exception.ToString() : null
            }, cancellationToken);

            return true;
        }

        var problem = new ProblemDetails
        {
            Title = "An unexpected error occurred.",
            Status = StatusCodes.Status500InternalServerError,
            Detail = _options.ErrorHandling.IncludeExceptionDetails ? exception.ToString() : null
        };

        problem.Extensions["traceId"] = traceId;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
