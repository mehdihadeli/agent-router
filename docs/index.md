# AgentRouter Documentation

AgentRouter connects coding agents to models through a gateway you run locally. Configure a provider once, choose routes and launch profiles, and keep upstream credentials out of agent settings. The command is `ar`.

## Get Started

1. [Install and start](guide/quick-start.md): configure your first model and run the gateway.
2. [Add providers](guide/providers.md): choose a protocol, supply credentials privately, and check connectivity.
3. [Connect an agent](guide/agents.md): configure isolated Claude, Codex, OpenCode, and Claude Design profiles.
4. [Open the web UI](guide/ui.md): pair a browser with local management and configure the workspace.
5. [Inspect requests](guide/observability.md): use panel and CLI metrics or export telemetry.

## Configure Your Gateway

| Topic | What you can do |
| --- | --- |
| [Routing](guide/routing.md) | Select defaults and Claude tiers; preview conditional rules; activate bounded fallback. |
| [Configuration](reference/configuration.md) | Understand saved settings, versioned JSON, PascalCase names, environment overrides, and storage. |
| [CLI](reference/cli.md) | Find commands and options for setup, profiles, service lifecycle, and management. |
| [CLI guide](guide/cli.md) | Follow complete terminal workflows for setup, operations, profiles, and observability. |
| [Web UI](guide/ui.md) | Pair a local browser session and use Overview, Providers, Routing, Profiles, and Requests. |
| [Agent profiles](reference/agent-profiles.md) | Configure profile kinds, launch surfaces, routing, and support boundaries. |
| [Troubleshooting](guide/troubleshooting.md) | Diagnose authentication errors, missing models, timeouts, and service startup failures. |
| [Protocol compatibility](integrations.md) | Check supported ingress and upstream protocols before connecting a client. |

<!-- Contributor documentation is maintained under docs/dev/. -->
