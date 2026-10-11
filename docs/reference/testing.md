# Testing Reference

Test projects:

- `tests/AgentRouter.Cli.Tests/AgentRouter.Cli.Tests.csproj`
- `tests/AgentRouter.Core.Tests/AgentRouter.Core.Tests.csproj`

Test projects mirror the Core and CLI source projects. ServiceDefaults has no dedicated test project.

Layout:

- Each test project uses `Unit/` and `Integration/` folders.
- `Unit/` contains fast, isolated tests.
- `Integration/` contains hosted pipeline and end-to-end tests.
- `tests/Directory.Build.props` centralizes shared test configuration, Microsoft Testing Platform settings, and runner extensions.

Build configuration:

- `Directory.Build.props` at the repository root defines the common .NET 10 build defaults.
- `src/Directory.Build.props` imports the root defaults for source projects.
- `tests/Directory.Build.props` imports the root defaults and adds test-specific analyzer and runner settings.
- `Directory.Packages.props` at the repository root owns package versions through central package management.

Current project split:

- `tests/AgentRouter.Core.Tests`: routes, adapters, provider transports, configuration, and storage. Feature folders sit under `Unit/` and `Integration/`; shared storage fixture lives under `Support/`.
- `tests/AgentRouter.Cli.Tests`: command validation under `Unit/`, subprocess configuration and hosted gateway workflows under `Integration/`.

Core HTTP integration fixtures reference the CLI executable only as the marker required by WebApplicationFactory. They still build the gateway library host directly. Existing test namespaces are preserved so test filters remain compatible.

Test stack:

- xUnit v3 via `xunit.v3`
- Microsoft Testing Platform-compatible executable test projects on .NET 10

Covered areas:

- Anthropic/OpenAI converter behavior
- Route model creation
- Configuration providers and binding
- Claude settings file synchronization
- CLI host end-to-end behavior
- End-to-end gateway tool wiring
- Error handling responses

Run all tests:

```bash
dotnet test --solution agent-router.slnx --configuration Release
```

Run without paid-provider tests:

```bash
dotnet test --solution agent-router.slnx --filter-not-class AgentRouter.Tests.EndToEndGatewayToolWiringTests --filter-not-class AgentRouter.Providers.OpenAI.Tests.LiveProviderTests
```
