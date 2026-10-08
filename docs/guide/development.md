# Development Guide

## Local commands

Build:

```bash
dotnet build claude-code-gateway.slnx --configuration Release
```

Test:

```bash
dotnet test --solution claude-code-gateway.slnx --configuration Release
```

Run with repo launcher:

```bash
./claude-code-gateway serve --port 4600
```

Pack tool locally:

```bash
dotnet pack src/ClaudeCodeGateway.Cli/ClaudeCodeGateway.Cli.csproj --configuration Release --output artifacts
```

## Configuration flow

The gateway loads configuration from:

1. Command-line arguments
2. Environment variables
3. `appsettings.json` and environment-specific variants
4. `~/claude-code-gateway/config.json`
5. `~/.claude/settings.json`
6. Local `.env`

Use `CCG_CREATE_USER_CONFIG=true` to auto-create the user config file when supported by the current setup.

## Shared projects

- `src/ClaudeCodeGateway.Core` keeps route, options, and provider contracts.
- `src/ClaudeCodeGateway.Providers.OpenAI` owns OpenAI-compatible provider registrations.
- `src/ClaudeCodeGateway.ServiceDefaults` owns health checks, OpenTelemetry, resilience, and service discovery defaults.
