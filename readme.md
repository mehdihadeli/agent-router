# AgentRouter

[![BuildTestPublish](https://img.shields.io/github/actions/workflow/status/mehdihadeli/agent-router/build-and-publish.yml?branch=main&style=for-the-badge&label=BuildTestPublish)](https://github.com/mehdihadeli/agent-router/actions/workflows/build-and-publish.yml)
[![License](https://img.shields.io/github/license/mehdihadeli/agent-router?style=for-the-badge)](https://github.com/mehdihadeli/agent-router)
[![.NET](https://img.shields.io/badge/.NET%2010-512bd4?style=for-the-badge)](https://dotnet.microsoft.com/platform/dotnet)
[![AgentRouter](https://img.shields.io/nuget/v/AgentRouter?style=for-the-badge&color=blue&label=AgentRouter)](https://www.nuget.org/packages/AgentRouter)

AgentRouter is a local model gateway that lets coding agents use different AI providers through one consistent endpoint. Use Claude Code with OpenAI-compatible models, use experimental Codex profiles with Anthropic or other configured providers, and connect OpenCode or other compatible clients to the provider model that fits the task. Configure providers, select models with defaults, profiles, tiers, rules, and fallback candidates, then launch supported agents against the route you choose.

The `ar` CLI manages the complete local workflow: initialize configuration and encrypted credentials, add providers, preview and validate routes, run inference in the foreground or as a background service, launch agent profiles, and inspect health and request telemetry. AgentRouter translates supported requests and responses, including streaming, function tools, tool results, and selected multimodal content, while the agent remains responsible for executing tools and editing files.

## Features at a glance

| Capability | What it provides | Reference |
| --- | --- | --- |
| Provider connections | OpenAI-compatible Chat Completions and native Anthropic upstreams, including supported custom endpoints. | [Provider guide](docs/guide/providers.md) |
| Streaming and tools | Forward streaming responses, tool definitions, function calls, tool results, and supported multimodal content. | [Protocol compatibility](docs/integrations.md) |
| Model routing | Default routes, Claude tiers, conditional rules, numeric rewrites, aliases, and ordered fallback. | [Routing guide](docs/guide/routing.md) |
| Agent profiles | Launch Claude Code and supported Codex, OpenCode, and Claude Design profiles without changing global agent settings. | [Agent guide](docs/guide/agents.md) |
| Background operation | Run inference as a managed service with status, pause, resume, reload, and stop controls. | [CLI reference](docs/reference/cli.md) |
| Request protection | Bound attempts and deadlines, honor `Retry-After`, and apply circuit breakers and concurrency limits. | [Execution settings](docs/reference/configuration.md#provider-attempt-budget) |
| Visibility | Retain sanitized request summaries or export logs, traces, and metrics through OpenTelemetry. | [Observability guide](docs/guide/observability.md) |
| Local credentials | Encrypt provider keys, require an inference token, and keep management authorization separate. | [Security configuration](docs/reference/configuration.md#environment-overrides) |

AgentRouter forwards model requests. The agent still executes tools; AgentRouter does not run commands or edit files for the model.

## Install

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), which includes the ASP.NET Core runtime used by AgentRouter. Install Claude Code separately if you plan to launch it.

Install the AgentRouter .NET tool from NuGet.org:

```shell
dotnet tool install --global AgentRouter
ar --help
```

For a prerelease, add `--prerelease`; to select a specific release, add `--version VERSION`. If `ar` is not found after installation, ensure the .NET tools directory is on `PATH` and reopen your terminal.

### Open the management UI

The published .NET tool includes the Angular management UI. Consumers do not need Node.js, npm, or a separate frontend package.

```shell
ar ui
```

This starts or reuses the local management service, opens the UI in your browser, and prints a short-lived pairing code. Enter the code in the browser within two minutes. To open the address yourself, use:

```shell
ar ui --no-open
```

Use the UI to configure providers and routes, manage profiles, control the gateway, and inspect request observability. Node.js and npm are only needed when developing AgentRouter or building the package from source.

## Quick start

Create your configuration, add a provider, and save a Claude Code launch profile:

```shell
ar init --port 4600
ar provider add work --base-url https://api.openai.com/v1 --models gpt-4.1
ar route set work/gpt-4.1
ar profile add Review --model work/gpt-4.1
ar serve
```

`provider add` reads `OPENAI_API_KEY` or prompts privately in an interactive terminal. Supply the key privately before setup; never include it in command arguments. Replace `gpt-4.1` with a model your account can access. `work` and `Review` are names you choose.

Leave `serve` running. In another terminal, check the gateway and launch your profile:

```shell
ar status --url http://127.0.0.1:4600
ar profile launch Review --url http://127.0.0.1:4600
```

The agent receives gateway settings only in its child process. Your global Claude settings remain unchanged. Using the agent sends upstream requests that may incur provider charges. Press Ctrl+C in the gateway terminal when finished.

Running `ar` alone shows help without starting anything. Use `ar <command> --help` for command options and examples.

## CLI workflow

Use `ar` for setup, inference, service lifecycle, profiles, and diagnostics. The main workflows are:

| Workflow | Start here | Purpose |
| --- | --- | --- |
| Setup | `ar init` | Create local configuration and the gateway token. |
| Provider | `ar provider add`, `ar provider list`, `ar provider check` | Register an upstream, inspect models, and optionally test a real request. |
| Saved routing | `ar route set` | Select the default model or a Claude tier. |
| Profiles | `ar profile add`, `ar profile list`, `ar profile launch` | Save and launch an isolated agent configuration. |
| Foreground inference | `ar serve`, `ar status` | Run a short local session in the current terminal. |
| Background service | `ar service start`, `ar service status` | Keep management and inference running after the terminal closes. |
| Apply changes | `ar service reload` | Validate saved changes and restart inference without stopping management. |
| Versioned routing | `ar config validate`, `ar route preview`, `ar serve --settings-file` | Validate, preview, and explicitly activate conditional settings. |
| Inspection | `ar config show`, `ar observability` | Inspect masked configuration and retained request health. |

Use `ar service pause` and `ar service resume` to stop or restart inference while keeping management available. Use `ar service reload` after changing saved providers or routes. Configuration validation and route preview are read-only and do not contact providers. See the [CLI reference](docs/reference/cli.md) for command options and lifecycle details.

## Configuration modes

AgentRouter has three separate configuration contracts:

| Mode | Best for | Main commands | Reference |
| --- | --- | --- | --- |
| Saved CLI configuration | Normal providers, routes, profiles, and credentials under `~/.agent-router`. | `ar init`, `ar provider add`, `ar route set`, `ar profile add`, `ar service reload` | [Configuration reference](docs/reference/configuration.md#initialize-saved-configuration) |
| Versioned settings JSON | Conditional routing, numeric rewrites, aliases, fallback candidates, and explicit previews. | `ar config validate`, `ar route preview`, `ar serve --settings-file PATH` | [Versioned settings](docs/reference/configuration.md#versioned-settings-and-preview) |
| Legacy host options | Existing flat JSON/environment configuration and library embedding. | Host-specific configuration | [Legacy host options](docs/reference/configuration.md#legacy-host-options) |

Configuration properties use PascalCase. Nested environment overrides use `__`, for example `Execution__MaxAttempts` and `Providers__work__BaseUrl`. Environment overrides affect runtime values only; they do not rewrite saved configuration. See the [configuration reference](docs/reference/configuration.md) for precedence, storage, validation, and migration rules.

## Providers and models

A provider has its own ID, endpoint, protocol, model list, and credential. Choose the provider type that matches the upstream API:

| Provider type | Protocol | Endpoint shape | Credential | Notes |
| --- | --- | --- | --- | --- |
| OpenAI-compatible | `OpenAi` | Provider-documented Chat Completions base, commonly ending in `/v1` | `OPENAI_API_KEY` by default | Works with OpenAI-compatible services when their model behavior matches the supported protocol. |
| Native Anthropic | `Anthropic` | Anthropic API base, without `/v1/messages` | `ANTHROPIC_API_KEY` | Uses native Anthropic upstream semantics. |
| Custom compatible service | `OpenAi` or `Anthropic` | Service-documented base URL | Provider-specific environment variable | A compatible label alone does not guarantee tools, streaming, or reasoning support. |

Model selectors use `PROVIDER/MODEL`. Native model IDs keep their original case and slashes; the provider prefix is not sent upstream. The gateway does not discover upstream models; `/v1/models` lists enabled configured models.

To add a native Anthropic provider, supply its key through `ANTHROPIC_API_KEY` and replace `YOUR_MODEL_ID` with an available model:

```shell
ar provider add anthropic --protocol Anthropic --base-url https://api.anthropic.com --models YOUR_MODEL_ID --api-key-env ANTHROPIC_API_KEY
ar provider list
```

Route targets use `PROVIDER/MODEL`. Model IDs retain their original case and slashes: a model named `owner/native-model` under provider `work` becomes `work/owner/native-model`. The gateway does not fetch upstream model catalogs; `/v1/models` lists configured enabled models.

OpenRouter, DeepSeek, Z.AI, and other custom services need a supported protocol and model behavior. Azure OpenAI and GitHub Models have existing adapter paths described in the [provider guide](docs/guide/providers.md).

`ar provider check PROVIDER/MODEL` sends a real inference request and may incur charges. Successful registration or local gateway status alone does not prove upstream access.

## Routing and fallback

| Routing need | Configuration | Behavior |
| --- | --- | --- |
| Default model | `ar route set work/gpt-4.1` | Sends requests to the selected provider/model. |
| Claude tier | `ar route set work/gpt-4.1-mini --tier Haiku` | Maps `Default`, `Opus`, `Sonnet`, or `Haiku` to a model. |
| Conditional rule | Versioned `Routing.Rules` | Matches agent, task, subagent, workflow, requested model, or text. |
| Numeric rewrite | Versioned `Rewrite` | Supports `MaxOutputTokens` and `Temperature` only. |
| Fallback | Versioned `FallbackModels` | Tries ordered candidates before repeating the selected model. |
| Preview | `ar route preview --file settings.json` | Evaluates a candidate without saving, activating, or contacting providers. |

Choose a default route or assign a Claude tier:

```shell
ar route set work/gpt-4.1
ar route set work/gpt-4.1-mini --tier Haiku
```

Configure every referenced model on its provider first. Supported tiers are `Default`, `Opus`, `Sonnet`, and `Haiku`. A recognized explicit provider/model takes precedence over tier selection. Apply saved changes by restarting foreground `serve` or running `ar service reload` for a managed gateway.

For conditional rules and fallback, create a versioned file using the [configuration example](docs/reference/configuration.md#versioned-settings-and-preview). Validate and preview it before activation:

```shell
ar config validate --file settings.json
ar route preview --file settings.json --task review --subagent worker
ar serve --settings-file settings.json
```

Validation and preview do not call providers, save changes, or activate settings. The file is a separate configuration mode, not an overlay on saved providers. Activation requires one enabled environment credential per enabled provider and a gateway token environment reference. Managed inference does not accept this settings-file switch yet.

Rules can match agent, task, subagent, workflow, requested model, or literal text, and rewrite `MaxOutputTokens` or `Temperature`. Explicit canonical models and aliases win over rules, so a profile pinned to a model bypasses conditional selection. See [routing](docs/guide/routing.md) for precedence and fallback details.

## Agent profiles

Profiles save a model and native executable choice without changing global agent settings. Forward agent arguments after `--`:

```shell
ar profile list
ar profile launch Review -- --permission-mode plan
```

The gateway must already be running. Launch supports local loopback gateway URLs, not remote gateways. Install the agent separately; use `--executable` when its native executable is not discoverable.

Experimental Codex profiles use `--kind codex`. They rely on a stateless Responses subset supporting text, function tools, and text reasoning output/history. Full Codex sessions, custom patch tools, stored continuations, and reasoning-effort controls are not certified or supported as a complete workflow. See [agents](docs/guide/agents.md) before using this mode.

## Background operation

Stop any foreground gateway using the same port before starting the managed service:

```shell
ar service start
ar service status
ar profile launch Review
```

Check that status reports `Inference: Running`. Management availability alone does not mean inference is ready, and readiness does not verify provider access.

| Command | Effect |
| --- | --- |
| `ar service pause` | Drain active requests and stop inference, leaving management running. |
| `ar service resume` | Start inference again from saved settings. |
| `ar service reload` | Validate saved configuration, drain requests, and restart inference. |
| `ar service stop` | Stop the managed process and its inference listener. |

Reload can briefly interrupt availability; it is not seamless hot reload. Service commands do not control a standalone `ar serve` process. Management provides authenticated configuration APIs and OpenAPI, but no browser UI. See the [CLI reference](docs/reference/cli.md) for management-only and foreground service modes.

## Retries and deadlines

Inference has an execution deadline and a bounded attempt budget. Legacy hosting defaults to one attempt; explicit retries accept a total of 1..8 attempts, including the first. Versioned settings can also select ordered fallback models. Delays and attempts consume the same deadline, and gateway-registered SDK retries are disabled to avoid multiplying requests.

Transient connection failures, HTTP 408, 429, and 5xx may retry before output. Retry-After, exponential backoff, per-provider/model circuit breakers, and concurrency limits protect upstreams. After the first provider stream update is exposed, the gateway never retries or switches providers; a later failure interrupts that stream. Retries may incur additional charges.

See [execution settings](docs/reference/configuration.md#provider-attempt-budget) for limits and configuration names.

## Configuration and security

Settings and encrypted credentials live under `~/.agent-router` by default. Set `AR_CONFIG_DIR` before starting AgentRouter to use another directory. Protect this directory and its backups: it contains both encrypted credentials and local encryption-key material.

Use `ar config show` to inspect saved configuration with secrets masked or omitted. Configuration property names use PascalCase. Nested environment variables use `__`, such as `Server__ProxyPort`; existing camelCase documents remain readable. Provider IDs and native model IDs are not renamed. Environment overrides do not overwrite saved configuration.

Inference listens on loopback by default and requires a generated gateway token. Management uses a separate credential; an inference token cannot authorize configuration changes. `ar config token` prints a sensitive credential for manual client setup. Never paste that output into logs, URLs, screenshots, or support tickets.

The profile launcher leaves global agent configuration unchanged. Remote exposure requires deliberate access controls and TLS; it does not enable remote profile launching. See [configuration](docs/reference/configuration.md) for environment precedence, storage, and migration guidance.

## Request history and telemetry

Local request summaries and OpenTelemetry export are separate opt-in workflows. Before starting inference, set `Observability__Enabled=true` to retain summaries of provider, model, latency, outcome, and reported token usage. Unknown usage remains unknown, and cost is not calculated. Prompt/response bodies are not stored in summaries.

The authenticated management API exposes recent history at `GET /management/v1/requests?limit=100`. OpenTelemetry can export logs, traces, and metrics to your own collector using `OTEL_EXPORTER_OTLP_ENDPOINT` and the appropriate protocol setting. Sensitive AI tracing is off by default.

Gateway telemetry describes model traffic, not every local agent action. There is no built-in dashboard or full agent-event ingestion service. Follow [observability](docs/guide/observability.md) for collector setup, retention, and privacy limits.

## Compatibility limits

Anthropic Messages is supported as client ingress. OpenAI Responses ingress is a stateless subset; OpenAI Chat Completions is an upstream protocol, not an implemented client ingress. Unsupported protocol semantics are rejected rather than silently dropped.

Gemini, credential rotation, priced usage accounting, and Electron are not implemented. Compatibility depends on the selected provider, model, and requested features. Consult [protocol compatibility](docs/integrations.md), the [CLI guide](docs/guide/cli.md), and the [web UI guide](docs/guide/ui.md) before relying on a workflow.

## Update or uninstall

Stop a running managed service before updating the tool:

```shell
ar service stop
dotnet tool update --global AgentRouter
```

Use your release's package source or version options when needed. To uninstall:

```shell
dotnet tool uninstall --global AgentRouter
```

Saved configuration is stored separately from the tool installation. Uninstalling the tool does not remove that data; protect or remove it separately according to your retention needs.

## Documentation

| Task | Guide |
| --- | --- |
| First setup and running modes | [Install and start](docs/guide/quick-start.md) |
| Connect a provider and verify access | [Providers](docs/guide/providers.md) |
| Launch Claude Code or try Codex | [Agents](docs/guide/agents.md) |
| Select models, rules, and fallback | [Routing](docs/guide/routing.md) |
| Configure JSON and environment variables | [Configuration](docs/reference/configuration.md) |
| Inspect requests and export telemetry | [Observability](docs/guide/observability.md) |

## License

AgentRouter is licensed under the [MIT License](LICENSE).
