using System.ComponentModel.DataAnnotations;

namespace Entities.Http.Rpc;

/// <summary>
/// Root options object bound from the <c>Entity:HttpRpc</c> configuration section.
/// </summary>
public sealed class HttpRpcOptions
{
    public const string SectionName = "Entity:HttpRpc";
    public const string CorsPolicyName = "EntityHttpRpcCors";

    public HttpRpcJsonOptions Json { get; set; } = new();
    public HttpRpcProtoOptions Proto { get; set; } = new();
    public HttpRpcCorsOptions Cors { get; set; } = new();
    public HttpRpcAuthOptions Auth { get; set; } = new();
    public HttpRpcObservabilityOptions Observability { get; set; } = new();
    public HttpRpcForwardedHeadersOptions ForwardedHeaders { get; set; } = new();
    public HttpRpcHealthCheckOptions HealthChecks { get; set; } = new();
    public HttpRpcResponseHeadersOptions ResponseHeaders { get; set; } = new();
    public HttpRpcErrorHandlingOptions ErrorHandling { get; set; } = new();
}

/// <summary>
/// Controls JSON payload formatting for HTTP JSON RPC requests and responses.
/// </summary>
public sealed class HttpRpcJsonOptions
{
    public bool UseCamelCase { get; set; } = true;
    public bool WriteIndented { get; set; }
    public bool IgnoreNullValues { get; set; } = true;
    public bool SerializeEnumsAsStrings { get; set; } = true;
}

/// <summary>
/// Configures the HTTP endpoint that forwards raw Fantasy outer packets.
/// </summary>
public sealed class HttpRpcProtoOptions
{
    public bool Enabled { get; set; } = true;
    public string SessionHeaderName { get; set; } = "X-Session-Id";
    public int SessionIdleTimeoutSeconds { get; set; } = 300;
    public int SessionCleanupIntervalSeconds { get; set; } = 60;
    public bool RequireExistingSession { get; set; }
    public int InvalidSessionStatusCode { get; set; } = 401;
    public int EmptyMessageStatusCode { get; set; } = 204;
}

/// <summary>
/// Controls the CORS policy applied to the HTTP RPC endpoints.
/// </summary>
public sealed class HttpRpcCorsOptions
{
    public bool Enabled { get; set; }
    public bool AllowAnyMethod { get; set; } = true;
    public bool AllowAnyHeader { get; set; } = true;
    public bool AllowCredentials { get; set; }
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    public string[] AllowedMethods { get; set; } = Array.Empty<string>();
    public string[] AllowedHeaders { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Configures JWT authentication for all HTTP RPC routes.
/// </summary>
public sealed class HttpRpcAuthOptions
{
    public bool Enabled { get; set; }

    [MinLength(32)]
    public string? SigningKey { get; set; }

    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public bool ValidateIssuer { get; set; } = true;
    public bool ValidateAudience { get; set; } = true;
    public bool ValidateLifetime { get; set; } = true;
    public bool ValidateIssuerSigningKey { get; set; } = true;
    public int ClockSkewSeconds { get; set; }
    public string[] DefaultSchemes { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Controls request logging and trace header behavior.
/// </summary>
public sealed class HttpRpcObservabilityOptions
{
    public bool RequestLoggingEnabled { get; set; } = true;
    public bool IncludeClientIp { get; set; } = true;
    public bool IncludeTraceIdentifierResponseHeader { get; set; } = true;
    public string TraceIdentifierHeaderName { get; set; } = "X-Request-Id";
}

/// <summary>
/// Defines which reverse-proxy headers are trusted before ASP.NET Core rewrites request metadata.
/// </summary>
public sealed class HttpRpcForwardedHeadersOptions
{
    public bool Enabled { get; set; }
    public bool ForwardXForwardedFor { get; set; } = true;
    public bool ForwardXForwardedProto { get; set; } = true;

    public string[] KnownProxies { get; set; } = Array.Empty<string>();
    public string[] KnownNetworks { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Configures the optional ASP.NET Core health-check endpoint.
/// </summary>
public sealed class HttpRpcHealthCheckOptions
{
    public bool Enabled { get; set; } = true;
    public string Path { get; set; } = "/health";
    public bool AllowAnonymous { get; set; } = true;
}

/// <summary>
/// Adds static response headers to every HTTP RPC response.
/// </summary>
public sealed class HttpRpcResponseHeadersOptions
{
    public bool Enabled { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Controls how unexpected HTTP pipeline failures are rendered back to clients.
/// </summary>
public sealed class HttpRpcErrorHandlingOptions
{
    public bool IncludeExceptionDetails { get; set; }
    public bool UseProblemDetails { get; set; } = true;
}
