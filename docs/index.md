# Claude Code Gateway Docs

This repository follows a simple layout modeled after the GroundKit project:

- `src/ClaudeCodeGateway.Cli` contains the executable CLI entry point.
- `src/ClaudeCodeGateway.Server` contains the standalone web executable.
- `src/ClaudeCodeGateway.Api` contains the Anthropic protocol adapter.
- `src/ClaudeCodeGateway.Core` contains route, options, and provider contracts.
- `src/ClaudeCodeGateway.Hosting` contains configuration and host composition.
- `src/ClaudeCodeGateway.Providers.OpenAI` contains OpenAI-compatible provider registrations.
- `src/ClaudeCodeGateway.ServiceDefaults` contains service defaults for host concerns.
- Namespace families follow the same split: `ClaudeCodeGateway.Cli`, `ClaudeCodeGateway`, `ClaudeCodeGateway.ErrorHandling`, `ClaudeCodeGateway.Shared.*`, and `Microsoft.Extensions.Hosting` for `ServiceDefaults` extensions.
- `tests/ClaudeCodeGateway.Tests` contains integration and unit coverage for the gateway.
- `docs/` contains user and contributor documentation.

Start here:

- `features.md` for the main runtime capabilities.
- `integrations.md` for provider and client integration notes.
- `guide/development.md` for local setup and common commands.
- `guide/versioning.md` for preview, RC, and stable release procedures.
- `reference/cli.md` for command usage.
- `reference/configuration.md` for config sources and examples.
- `reference/testing.md` for test coverage notes.
