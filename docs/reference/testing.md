# Testing Reference

Test projects:

- `tests/ClaudeCodeGateway.Api.Tests/ClaudeCodeGateway.Api.Tests.csproj`
- `tests/ClaudeCodeGateway.Cli.Tests/ClaudeCodeGateway.Cli.Tests.csproj`
- `tests/ClaudeCodeGateway.Core.Tests/ClaudeCodeGateway.Core.Tests.csproj`
- `tests/ClaudeCodeGateway.Hosting.Tests/ClaudeCodeGateway.Hosting.Tests.csproj`
- `tests/ClaudeCodeGateway.Providers.OpenAI.Tests/ClaudeCodeGateway.Providers.OpenAI.Tests.csproj`

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

- `tests/ClaudeCodeGateway.Api.Tests`: converter behavior, request validation, exception mapping, and hosted HTTP pipeline tests.
- `tests/ClaudeCodeGateway.Cli.Tests`: end-to-end CLI host wiring tests.
- `tests/ClaudeCodeGateway.Core.Tests`: routes, option validation, and core policy tests.
- `tests/ClaudeCodeGateway.Hosting.Tests`: configuration providers, option binding, and Claude settings writer tests.
- `tests/ClaudeCodeGateway.Providers.OpenAI.Tests`: provider registration contract tests.

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
dotnet test --solution claude-code-gateway.slnx --configuration Release
```
