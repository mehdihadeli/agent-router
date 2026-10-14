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

## Git hooks

Install the repository-local tools and Git hooks once after cloning:

```bash
dotnet tool restore
dotnet tool run husky -- run --group setup
```

Pre-commit checks staged C# files with CSharpier and verifies error-level style
and analyzer diagnostics against `agent-router.slnx`. These checks do not format
or stage files automatically. Fix reported formatting with
`dotnet tool run csharpier -- format <files>`, review the changes, then stage them.

Pre-push always builds the solution in Release and runs the deterministic
Microsoft.Testing.Platform tests using that build. It excludes
`EndToEndGatewayToolWiringTests`, `LiveProviderTests`,
`CodexProviderCompatibilityTests`, and `ErrorHandlingIntegrationTests` because
they require real agents, paid providers, or external network access. Hooks do
not authorize live tests, even when `AR_LIVE_TESTS` is set.

Run each hook group manually:

```bash
dotnet tool run husky -- run --group pre-commit
dotnet tool run husky -- run --group pre-push
```

The Docker-backed Gitleaks scan is an explicit, separate check:

```bash
dotnet tool run husky -- run --group security
```

It requires a running Docker daemon, may download the scanner image, and scans
the working directory with redacted output. It is not part of the default push
hook. Always select a group or task name; unfiltered `husky run` runs setup and
security tasks too. CI builds remain available via `--group ci` after restore.

## Configuration flow

The gateway loads configuration from:

1. Command-line arguments
2. Environment variables
3. `appsettings.json` and environment-specific variants
4. `~/.agent-router/config.json` (user profile directory on Windows and Linux; `AR_CONFIG_DIR` overrides the shared data directory)
5. `~/.claude/settings.json`
6. Local `.env`

Use `CCG_CREATE_USER_CONFIG=true` to auto-create the user config file when supported by the current setup.

## Shared projects

- `src/AgentRouter.Core` owns the gateway library, including API, routing, provider integrations, storage, and hosting folders.
- `src/AgentRouter.Cli` owns the executable and commands.
- `src/AgentRouter.ServiceDefaults` owns health checks, OpenTelemetry, resilience, and service discovery defaults.