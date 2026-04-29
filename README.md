# Entity.Http.Rpc

![.NET](https://img.shields.io/badge/.NET-8%2F9%2F10-512BD4)
![Entity](https://img.shields.io/badge/Entity-Fantasy%20RPC-0A7E8C)
![Transport](https://img.shields.io/badge/Transport-HTTP%20JSON%20%2B%20MessagePack%20%2B%20MemoryPack%20%2B%20Proto-1F6FEB)
![Auth](https://img.shields.io/badge/Auth-JWT%20Bearer-8B5CF6)
![License](https://img.shields.io/badge/License-MIT-green)

`Entity.Http.Rpc` adds HTTP-based RPC endpoints to the `Entity`/`Fantasy` server stack. In addition to the usual ASP.NET Core baseline features such as JSON options, JWT authentication, CORS, forwarded headers, exception handling, and health checks, the package now exposes JSON, MessagePack, MemoryPack, and Proto RPC endpoints that dispatch directly into Fantasy message handlers.

## Features

- HTTP JSON RPC endpoint backed by Fantasy protocol codes and message dispatch
- HTTP MessagePack RPC endpoint backed by Fantasy protocol codes and message dispatch
- HTTP MemoryPack RPC endpoint backed by Fantasy protocol codes and message dispatch
- HTTP Proto RPC endpoint for binary Fantasy outer packet streams
- Shared HTTP session bridge for JSON, MessagePack, MemoryPack, and Proto requests
- Optional AES-GCM body encryption for all HTTP RPC transports
- Configuration-driven JSON serialization behavior
- Configuration-driven MessagePack serialization behavior
- Configuration-driven MemoryPack serialization behavior
- Optional JWT Bearer authentication and authorization
- Unified exception handling and trace-aware error payloads
- Request tracing, response trace headers, and request logging
- Forwarded header support for reverse proxy and gateway deployments
- Built-in health check endpoint
- Supports `net8.0`, `net9.0`, and `net10.0`

## Installation

```bash
dotnet add package Entity.Http.Rpc
```

## Endpoints

The middleware registers these POST endpoints:

- `/http/json/rpc`
- `/http/json/rpc/{messageName}`
- `/http/messagepack/rpc`
- `/http/messagepack/rpc/{messageName}`
- `/http/memorypack/rpc`
- `/http/memorypack/rpc/{messageName}`
- `/http/proto/rpc`
- `/http/proto/rpc/{messageName}`

Health checks are available at `/health` by default and can be changed through configuration.

## JSON RPC Contract

JSON RPC uses a Fantasy-aware envelope. The request body must include `protocolCode` and may include `messageName`. The route `{messageName}` is optional, but if it is present it must match the Fantasy message type resolved from `protocolCode`.

Request example:

```json
{
  "protocolCode": 268445457,
  "rpcId": 1,
  "messageName": "C2G_TestRequest",
  "body": {
    "tag": "hello",
    "data": [1, 2, 3]
  }
}
```

Response example for `IRequest` messages:

```json
{
  "protocolCode": 402663185,
  "rpcId": 1,
  "messageName": "G2C_TestResponse",
  "body": {
    "errorCode": 0,
    "tag": "hello",
    "data": "AQID"
  }
}
```

Behavior notes:

- `protocolCode` is resolved through Fantasy's `MessageDispatcherComponent`.
- `body` is deserialized using the configured ASP.NET Core `JsonOptions`.
- If the resolved Fantasy message type does not implement `IRequest`, the endpoint returns `Proto.EmptyMessageStatusCode` and no JSON body.
- If the route `messageName` or body `messageName` does not match the resolved message type, the endpoint returns `400`.
- JSON RPC always reuses the Proto session bridge and response packet pipeline internally.

## MessagePack RPC Contract

MessagePack RPC uses the same envelope semantics as JSON RPC, but the request and response are MessagePack-encoded. The envelope shape is:

- `protocolCode`: `uint`
- `rpcId`: `uint`
- `messageName`: `string?`
- `body`: `bin`

Behavior notes:

- `MessagePack.Enabled = false` disables the MessagePack endpoint and returns `404` for MessagePack requests.
- `body` contains the MessagePack-encoded Fantasy message payload.
- `body` is deserialized using the configured MessagePack resolver and compression settings.
- If the resolved Fantasy message type does not implement `IRequest`, the endpoint returns `Proto.EmptyMessageStatusCode` and no MessagePack body.
- If the route `messageName` or body `messageName` does not match the resolved message type, the endpoint returns `400`.
- MessagePack RPC always reuses the Proto session bridge and response packet pipeline internally.

## MemoryPack RPC Contract

MemoryPack RPC uses the same envelope semantics as MessagePack RPC, but the request and response are MemoryPack-encoded. The envelope shape is:

- `protocolCode`: `uint`
- `rpcId`: `uint`
- `messageName`: `string?`
- `body`: `bin`

Behavior notes:

- `MemoryPack.Enabled = false` disables the MemoryPack endpoint and returns `404` for MemoryPack requests.
- `body` contains the MemoryPack-encoded Fantasy message payload.
- If the resolved Fantasy message type does not implement `IRequest`, the endpoint returns `Proto.EmptyMessageStatusCode` and no MemoryPack body.
- If the route `messageName` or body `messageName` does not match the resolved message type, the endpoint returns `400`.
- MemoryPack RPC always reuses the Proto session bridge and response packet pipeline internally.

## Proto RPC Contract

Proto RPC accepts one or more concatenated binary Fantasy outer packets on `/http/proto/rpc` and `/http/proto/rpc/{messageName}`.

Behavior notes:

- `Proto.Enabled = false` disables the Proto endpoint and returns `404` for Proto requests.
- `Proto.EmptyMessageStatusCode` is returned when the dispatched Fantasy message is not an `IRequest`.
- When `{messageName}` is present, it must match the Fantasy message type resolved from the packet protocol code.
- Proto responses are returned as a Fantasy outer packet stream, so a single HTTP response can carry the RPC reply plus any route/roaming forwarded messages produced while the request was handled.
- Route, addressable, roaming, ping, and response protocol kinds are dispatched through Fantasy's own outer network scheduler for maximum framework compatibility.

## Session Behavior

JSON, MessagePack, MemoryPack, and Proto RPC share the same HTTP session registry.

- Session IDs are carried in `Proto.SessionHeaderName`, which defaults to `X-Session-Id`.
- If the request does not provide the header and `Proto.RequireExistingSession = false`, the server creates a new session ID and writes it back to the response header.
- If `Proto.RequireExistingSession = true`, requests without the session header are rejected with `Proto.InvalidSessionStatusCode`.
- Sessions expire after `Proto.SessionIdleTimeoutSeconds`.
- Expired sessions are cleaned up every `Proto.SessionCleanupIntervalSeconds`.

## Error Behavior

Unhandled middleware-level exceptions are processed by `HttpRpcExceptionHandler`.

- If `Encryption.Enabled = true`, RPC response bodies are encrypted after format-specific error payloads are written.
- If request body decryption fails, the endpoint returns `Encryption.DecryptionFailureStatusCode` with a minimal plain-text error.
- If `ErrorHandling.UseProblemDetails = true`, non-RPC exceptions are written through ASP.NET Core `ProblemDetails`.
- If `ErrorHandling.UseProblemDetails = false`, errors are returned as JSON with `title`, `status`, `traceId`, and optional `detail`.
- JSON RPC validation and session errors are returned as JSON payloads.
- MessagePack RPC validation and session errors are returned as MessagePack payloads.
- MemoryPack RPC validation and session errors are returned as MemoryPack payloads.
- Proto RPC validation and session errors are returned as plain text.
- `ErrorHandling.IncludeExceptionDetails` controls whether exception details are included in error responses.

## Configuration

Add the `Entity:HttpRpc` section to the host application configuration:

```json
{
  "Entity": {
    "HttpRpc": {
      "Json": {
        "UseCamelCase": true,
        "WriteIndented": false,
        "IgnoreNullValues": true,
        "SerializeEnumsAsStrings": true
      },
      "MessagePack": {
        "Enabled": true,
        "ContentType": "application/x-msgpack",
        "UseContractlessResolver": true,
        "UseLz4BlockArrayCompression": false
      },
      "MemoryPack": {
        "Enabled": true,
        "ContentType": "application/x-memorypack"
      },
      "Proto": {
        "Enabled": true,
        "SessionHeaderName": "X-Session-Id",
        "SessionIdleTimeoutSeconds": 300,
        "SessionCleanupIntervalSeconds": 60,
        "RequireExistingSession": false,
        "InvalidSessionStatusCode": 401,
        "EmptyMessageStatusCode": 204
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
        "ValidateIssuer": true,
        "ValidateAudience": true,
        "ValidateLifetime": true,
        "ValidateIssuerSigningKey": true,
        "ClockSkewSeconds": 30
      },
      "ForwardedHeaders": {
        "Enabled": true,
        "ForwardXForwardedFor": true,
        "ForwardXForwardedProto": true,
        "KnownProxies": [
          "10.0.0.10"
        ],
        "KnownNetworks": []
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
      "Encryption": {
        "Enabled": false,
        "Algorithm": "AesGcm",
        "KeyBase64": "replace-with-base64-encoded-32-byte-key",
        "EncryptedContentType": "application/octet-stream",
        "DecryptionFailureStatusCode": 400
      },
      "ErrorHandling": {
        "UseProblemDetails": true,
        "IncludeExceptionDetails": false
      }
    }
  }
}
```

## Validation Rules

Startup validation fails when configuration is inconsistent. Important rules:

- `MessagePack.ContentType` is required when `MessagePack.Enabled = true`.
- `MemoryPack.ContentType` is required when `MemoryPack.Enabled = true`.
- `Proto.SessionHeaderName` is required when `Proto.Enabled = true`.
- `Proto.SessionIdleTimeoutSeconds` and `Proto.SessionCleanupIntervalSeconds` must be greater than zero.
- `Proto.InvalidSessionStatusCode` and `Proto.EmptyMessageStatusCode` must be valid HTTP status codes.
- `Cors.AllowedOrigins` must not be empty when CORS is enabled.
- If `Cors.AllowAnyMethod = false`, `Cors.AllowedMethods` must contain at least one value.
- If `Cors.AllowAnyHeader = false`, `Cors.AllowedHeaders` must contain at least one value.
- `Cors.AllowCredentials` cannot be used with wildcard origins.
- `Auth.SigningKey` is required and must be at least 32 characters when authentication is enabled.
- `Auth.Issuer` is required when `Auth.ValidateIssuer = true`.
- `Auth.Audience` is required when `Auth.ValidateAudience = true`.
- `Observability.TraceIdentifierHeaderName` is required when trace response headers are enabled.
- `HealthChecks.Path` must start with `/`.
- `ForwardedHeaders.KnownProxies` and `ForwardedHeaders.KnownNetworks` must contain valid IP/CIDR values.
- `Encryption.KeyBase64` is required when encryption is enabled and must decode to exactly 32 bytes.
- `Encryption.Algorithm` must be `AesGcm` when encryption is enabled.
- `Encryption.EncryptedContentType` is required when encryption is enabled.
- `Encryption.DecryptionFailureStatusCode` must be a valid HTTP status code.

## Default Behavior

- JSON RPC endpoints are always mapped.
- MessagePack RPC endpoints are mapped, but return `404` when `MessagePack.Enabled = false`.
- MemoryPack RPC endpoints are mapped, but return `404` when `MemoryPack.Enabled = false`.
- Proto RPC endpoints are mapped, but return `404` when `Proto.Enabled = false`.
- A request trace ID is always ensured and can be echoed in the configured response header.
- Request logging is enabled by default.
- Health checks are enabled by default at `/health`.
- The JSON serializer defaults to camelCase, ignores nulls, and serializes enums as strings.
- RPC body encryption is disabled by default.

## RPC Body Encryption

When `Encryption.Enabled = true`, all `/http/*/rpc` request bodies must be AES-GCM encrypted and every non-empty RPC response body is AES-GCM encrypted. This is a transport wrapper around the complete HTTP body, so the JSON, MessagePack, MemoryPack, and Proto contracts do not change after decryption.

The encrypted wire format is:

```text
version(1 byte = 1) + nonce(12 bytes) + tag(16 bytes) + ciphertext
```

The configured `Encryption.KeyBase64` value must be the same Base64-encoded 32-byte key on the server and Unity client. Generate a production key with a secure random source and store it outside the repository.

## Runtime Integration

The package integrates with the host framework through the `OnConfigureHttpServices` and `OnConfigureHttpApplication` lifecycle events. Existing host event registration does not need to change.

## Development

Common commands:

```bash
dotnet build Entity.Http.Rpc.csproj
dotnet test Tests/Entity.Http.Rpc.Tests/Entity.Http.Rpc.Tests.csproj
dotnet pack Entity.Http.Rpc.csproj -c Release
```

## Production Recommendations

- Store JWT signing keys in a secure configuration source rather than in the repository.
- Store RPC encryption keys in a secure configuration source and rotate them through deployment configuration.
- Enable `ForwardedHeaders` and restrict `KnownProxies` or `KnownNetworks` when running behind a reverse proxy.
- Keep `ErrorHandling.IncludeExceptionDetails = false` in production.
- Use explicit CORS origins instead of permissive settings.
- Treat `X-Session-Id` as an application session handle and pass it consistently for stateful JSON, MessagePack, or Proto RPC clients.

## Links

- Repository: <https://github.com/lleoliad/Entity.Http.Rpc>
