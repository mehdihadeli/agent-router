# Development Guide

## Local commands

Build:

```bash
dotnet build agent-router.slnx --configuration Release
```

Test:

```bash
dotnet test --solution agent-router.slnx --configuration Release
```

Run with repo launcher:

```bash
./agent-router serve --port 4600
```

Pack tool locally:

```bash
dotnet pack src/AgentRouter.Cli/AgentRouter.Cli.csproj --configuration Release --output artifacts
```

## Configuration flow

The gateway loads configuration from:

1. Command-line arguments
2. Environment variables
3. `appsettings.json` and environment-specific variants
4. `~/agent-router/config.json`
5. `~/.claude/settings.json`
6. Local `.env`

Use `CCG_CREATE_USER_CONFIG=true` to auto-create the user config file when supported by the current setup.

## Shared projects

- `src/AgentRouter.Core` owns the gateway library, including API, routing, provider integrations, storage, and hosting folders.
- `src/AgentRouter.Cli` owns the executable and commands.
- `src/AgentRouter.ServiceDefaults` owns health checks, OpenTelemetry, resilience, and service discovery defaults.
