# Entity.Http.Rpc

`Entity.Http.Rpc` is an HTTP infrastructure package for the `Entity` service framework. It provides production-ready JSON serialization, CORS, optional JWT authentication, unified exception handling, request logging, forwarded header support, and health checks.

## Features

- Configuration-driven HTTP baseline capabilities
- Optional JWT Bearer authentication and authorization
- Unified exception handling with `ProblemDetails`
- Request tracing and structured logging
- Forwarded header support for reverse proxy and gateway deployments
- Built-in health check endpoint
- Supports `net8.0`, `net9.0`, and `net10.0`

## Installation

```bash
dotnet add package Entity.Http.Rpc
```

## Configuration

Add the `Entity:HttpRpc` section to the host application configuration:

```json
{
  "Entity": {
    "HttpRpc": {
      "Json": {
        "UseCamelCase": true,
        "IgnoreNullValues": true,
        "SerializeEnumsAsStrings": true
      },
      "Cors": {
        "Enabled": true,
        "AllowedOrigins": [
          "https://api.example.com",
          "https://admin.example.com"
        ],
        "AllowAnyMethod": true,
        "AllowAnyHeader": true,
        "AllowCredentials": false
      },
      "Auth": {
        "Enabled": true,
        "SigningKey": "replace-with-a-secret-that-is-at-least-32-characters",
        "Issuer": "entity-platform",
        "Audience": "entity-clients",
        "ClockSkewSeconds": 30
      },
      "ForwardedHeaders": {
        "Enabled": true,
        "KnownProxies": [ "10.0.0.10" ]
      },
      "HealthChecks": {
        "Enabled": true,
        "Path": "/health",
        "AllowAnonymous": true
      },
      "Observability": {
        "RequestLoggingEnabled": true,
        "IncludeClientIp": true,
        "IncludeTraceIdentifierResponseHeader": true,
        "TraceIdentifierHeaderName": "X-Request-Id"
      },
      "ResponseHeaders": {
        "Enabled": true,
        "Headers": {
          "X-Service-Name": "entity-http-rpc"
        }
      },
      "ErrorHandling": {
        "UseProblemDetails": true,
        "IncludeExceptionDetails": false
      }
    }
  }
}
```

## Default Behavior

- When `Auth.Enabled` is not enabled, JWT is not registered and authentication is not required.
- Once authentication is enabled, missing critical settings such as `SigningKey`, `Issuer`, or `Audience` will cause startup to fail.
- When CORS is enabled, `AllowedOrigins` must be configured explicitly.
- When `ForwardedHeaders` is enabled, trusted proxies can be restricted through `KnownProxies` and `KnownNetworks`.
- Health checks are mapped to `/health` by default.

## Runtime Integration

The package continues to integrate with the host framework through the `OnConfigureHttpServices` and `OnConfigureHttpApplication` lifecycle events. Existing host event registration does not need to change.

## Production Recommendations

- Store JWT signing keys in a secure configuration source rather than in the repository.
- Enable `ForwardedHeaders` and configure trusted proxies when running behind a reverse proxy.
- Keep `ErrorHandling.IncludeExceptionDetails = false` in production.
- Minimize the set of exposed CORS origins as much as possible.

## Links

- Repository: <https://github.com/lleoliad/Entity.Http.Rpc>
