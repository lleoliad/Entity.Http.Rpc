
# Repository Guidelines

## Project Structure & Module Organization

This repository is a small .NET library that adds HTTP JSON, MessagePack, MemoryPack, and Proto RPC support to the Entity/Fantasy server stack.

- `Runtime/`: production code. Core entry points are `HttpServicesHandler.cs` and `HttpApplicationHandler.cs`.
- `Runtime/Json/`: JSON RPC envelope handling and request/response projection.
- `Runtime/MessagePack/`: MessagePack RPC envelope handling and request/response projection.
- `Runtime/MemoryPack/`: MemoryPack RPC envelope handling and request/response projection.
- `Runtime/Proto/`: HTTP-to-Fantasy Proto bridge, session registry, dispatcher, and cleanup service.
- `Runtime/Controller/`: MVC controllers such as `HealthController.cs`.
- `Tests/Entity.Http.Rpc.Tests/`: xUnit test project for package-level behavior and validation rules.
- `Assets/`: package assets such as the NuGet icon.

Keep new runtime code under `Runtime/` and mirror tests under `Tests/Entity.Http.Rpc.Tests/`.

## Build, Test, and Development Commands

- `dotnet build Entity.Http.Rpc.csproj`: build the library for all target frameworks.
- `dotnet test Tests/Entity.Http.Rpc.Tests/Entity.Http.Rpc.Tests.csproj`: run the xUnit test suite.
- `dotnet pack Entity.Http.Rpc.csproj -c Release`: create the NuGet package in `../../nupkg`.

Run commands from the repository root unless a script or CI job requires otherwise.

## Coding Style & Naming Conventions

Use standard C# conventions already present in the codebase:

- 4-space indentation, UTF-8 text, and file-scoped namespaces.
- `PascalCase` for public types and methods, `camelCase` for locals and parameters.
- Keep files focused; one primary type per file is the current pattern.
- Prefer clear option/property names such as `SessionIdleTimeoutSeconds` over abbreviations.

There is no dedicated formatter config in this repo, so follow the existing style and SDK defaults.

## Testing Guidelines

Tests use xUnit. Name test files after the unit under test, for example `HttpRpcOptionsValidatorTests.cs`.

- Use `Method_Should_DoSomething_When_Condition` naming for test methods.
- Add tests for new option validation, pipeline behavior, and error handling paths.
- When adding a new wire format, mirror the existing JSON/Proto coverage style with infrastructure tests for envelope parsing/serialization and option validation.
- Keep tests deterministic and isolated; avoid network or external service dependencies.

## Fantasy Compatibility Notes

The HTTP Proto path is intended to preserve Fantasy networking semantics as much as possible.

- Treat Proto request/response bodies as Fantasy outer packet streams, not a single-packet-only RPC envelope.
- Keep route, addressable, roaming, ping, and response protocol kinds flowing through Fantasy's outer network scheduler instead of duplicating that dispatch logic.
- Preserve the pseudo `Session` abstraction and shared session registry across JSON, MessagePack, MemoryPack, and Proto transports.
- Do not discard additional packets written through `Session.Send`; roaming and forwarded messages may be emitted alongside the direct RPC response.
- When projecting JSON, MessagePack, or MemoryPack responses, select the packet matching the request `rpcId` from the captured packet stream.
- If framework compatibility requires deeper integration, prefer a narrow bridge around Fantasy packet/session APIs before modifying the Fantasy framework itself.

## Commit & Pull Request Guidelines

Recent commits use short imperative subjects, for example:

- `Add HTTP proto RPC pipeline`
- `Handle unexpected proto RPC errors`

Follow that format: concise, present tense, and focused on one change. For pull requests, include a short summary, note any configuration or API impact, and list the `dotnet test` coverage for the change.

## Security & Configuration Tips

Configuration binds from `Entity:HttpRpc`, including format-specific settings such as `Entity:HttpRpc:Json`, `Entity:HttpRpc:MessagePack`, `Entity:HttpRpc:MemoryPack`, and `Entity:HttpRpc:Proto`. Do not commit real JWT signing keys or production proxy settings. Keep `ErrorHandling.IncludeExceptionDetails` disabled outside local debugging.
