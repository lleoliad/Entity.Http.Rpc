using System.Net;

namespace Entities.Http.Rpc;

public static class HttpRpcOptionsValidator
{
    public static void ValidateOrThrow(HttpRpcOptions options)
    {
        var errors = Validate(options);

        if (errors.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException($"Invalid {HttpRpcOptions.SectionName} configuration:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");
    }

    public static IReadOnlyList<string> Validate(HttpRpcOptions? options)
    {
        var errors = new List<string>();

        if (options is null)
        {
            errors.Add("Configuration section is missing.");
            return errors;
        }

        ValidateProto(options.Proto, errors);
        ValidateCors(options.Cors, errors);
        ValidateAuth(options.Auth, errors);
        ValidateObservability(options.Observability, errors);
        ValidateHealthChecks(options.HealthChecks, errors);
        ValidateForwardedHeaders(options.ForwardedHeaders, errors);

        return errors;
    }

    private static void ValidateCors(HttpRpcCorsOptions options, ICollection<string> errors)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (options.AllowedOrigins.Length == 0)
        {
            errors.Add("Cors.AllowedOrigins must contain at least one origin when CORS is enabled.");
        }

        if (!options.AllowAnyMethod && options.AllowedMethods.Length == 0)
        {
            errors.Add("Cors.AllowedMethods must contain at least one method when Cors.AllowAnyMethod is disabled.");
        }

        if (!options.AllowAnyHeader && options.AllowedHeaders.Length == 0)
        {
            errors.Add("Cors.AllowedHeaders must contain at least one header when Cors.AllowAnyHeader is disabled.");
        }

        if (options.AllowCredentials && options.AllowedOrigins.Any(origin => origin == "*" || string.Equals(origin, "all", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Cors.AllowCredentials cannot be enabled when Cors.AllowedOrigins contains a wildcard.");
        }
    }

    private static void ValidateProto(HttpRpcProtoOptions options, ICollection<string> errors)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.SessionHeaderName))
        {
            errors.Add("Proto.SessionHeaderName is required when Proto.Enabled is enabled.");
        }

        if (options.SessionIdleTimeoutSeconds <= 0)
        {
            errors.Add("Proto.SessionIdleTimeoutSeconds must be greater than zero.");
        }

        if (options.SessionCleanupIntervalSeconds <= 0)
        {
            errors.Add("Proto.SessionCleanupIntervalSeconds must be greater than zero.");
        }

        if (options.InvalidSessionStatusCode is < 100 or > 999)
        {
            errors.Add("Proto.InvalidSessionStatusCode must be a valid HTTP status code.");
        }

        if (options.EmptyMessageStatusCode is < 100 or > 999)
        {
            errors.Add("Proto.EmptyMessageStatusCode must be a valid HTTP status code.");
        }
    }

    private static void ValidateAuth(HttpRpcAuthOptions options, ICollection<string> errors)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            errors.Add("Auth.SigningKey is required when authentication is enabled.");
        }
        else if (options.SigningKey.Length < 32)
        {
            errors.Add("Auth.SigningKey must be at least 32 characters long.");
        }

        if (options.ValidateIssuer && string.IsNullOrWhiteSpace(options.Issuer))
        {
            errors.Add("Auth.Issuer is required when Auth.ValidateIssuer is enabled.");
        }

        if (options.ValidateAudience && string.IsNullOrWhiteSpace(options.Audience))
        {
            errors.Add("Auth.Audience is required when Auth.ValidateAudience is enabled.");
        }

        if (options.ClockSkewSeconds < 0)
        {
            errors.Add("Auth.ClockSkewSeconds cannot be negative.");
        }
    }

    private static void ValidateObservability(HttpRpcObservabilityOptions options, ICollection<string> errors)
    {
        if (!options.IncludeTraceIdentifierResponseHeader)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.TraceIdentifierHeaderName))
        {
            errors.Add("Observability.TraceIdentifierHeaderName is required when response trace headers are enabled.");
        }
    }

    private static void ValidateHealthChecks(HttpRpcHealthCheckOptions options, ICollection<string> errors)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Path) || !options.Path.StartsWith('/'))
        {
            errors.Add("HealthChecks.Path must be an absolute path starting with '/'.");
        }
    }

    private static void ValidateForwardedHeaders(HttpRpcForwardedHeadersOptions options, ICollection<string> errors)
    {
        if (!options.Enabled)
        {
            return;
        }

        foreach (var proxy in options.KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                errors.Add($"ForwardedHeaders.KnownProxies contains an invalid IP address: {proxy}");
            }
        }

        foreach (var network in options.KnownNetworks)
        {
            var parts = network.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out _) || !int.TryParse(parts[1], out _))
            {
                errors.Add($"ForwardedHeaders.KnownNetworks contains an invalid CIDR entry: {network}");
            }
        }
    }
}
