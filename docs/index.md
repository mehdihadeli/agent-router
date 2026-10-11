# Agent Router Docs

The runtime has three production projects:

- `src/AgentRouter.Cli` contains the executable CLI entry point.
- `src/AgentRouter.Core` contains protocol adapters, routing, provider integration, configuration, storage, and host composition.
- `src/AgentRouter.ServiceDefaults` contains service defaults for host concerns.
- Namespace families follow the same split: `AgentRouter.Cli`, `AgentRouter`, `AgentRouter.ErrorHandling`, `AgentRouter.Shared.*`, and `Microsoft.Extensions.Hosting` for `ServiceDefaults` extensions.
- Core.Tests and Cli.Tests mirror source ownership with Unit and Integration folders; ServiceDefaults has no test project.
- `docs/` contains user and contributor documentation.

Start here:

- [Reference feature coverage](reference/feature-parity.md) for supported behavior, missing capabilities, and implementation priorities.

- `features.md` for the main runtime capabilities.
- `integrations.md` for provider and client integration notes.
- `guide/development.md` for local setup and common commands.
- `guide/versioning.md` for preview, RC, and stable release procedures.
- `reference/cli.md` for command usage.
- `reference/configuration.md` for config sources and examples.
- `reference/testing.md` for test coverage notes.
