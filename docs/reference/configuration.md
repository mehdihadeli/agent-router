# Configuration Reference

## Choose A Configuration Mode

| Mode | Use it for | Apply changes |
| --- | --- | --- |
| Saved CLI configuration | Normal provider, route, and launch-profile setup. Stored in SQLite. | Restart `serve` or run `ar service reload`. |
| Versioned settings JSON | Conditional routing, numeric rewrites, and fallback. | Validate/preview first, then explicitly run `serve --settings-file PATH`. |
| Legacy host options | Existing flat JSON/environment configuration or library embedding. | Restart the host. |

These contracts are distinct. Saved providers use `BaseUrl`; versioned providers use `Endpoint`; legacy provider arrays use `BaseAddress`. Do not combine their examples into one document. For a first setup, follow [Install and start](../guide/quick-start.md).

## Initialize Saved Configuration

Use the CLI to create the normal saved configuration. `ar init` creates the user configuration directory and gateway token, and preserves an existing configuration.

```powershell
ar init --port 4600
ar provider add work --base-url https://api.openai.com/v1 --models gpt-4.1
ar route set work/gpt-4.1
ar profile add Review --model work/gpt-4.1
ar config show
```

`provider add` stores the endpoint and model IDs in `~/.agent-router/config.json`. Supply the API key through the provider's environment variable or the private interactive prompt; never put a secret in command arguments. The generated gateway token and provider credentials are encrypted separately. Replace `gpt-4.1` with a model available to your provider.

Run `ar serve` to use the saved configuration in the foreground. For a managed service, run `ar service start`; after changing saved providers or routes, run `ar service reload`. Use `ar config show` to inspect saved values with secrets masked or omitted.

Versioned settings are a separate contract. Create `settings.json` manually from the example below, then run `ar config validate --file settings.json` and `ar route preview --file settings.json` before explicitly activating it with `ar serve --settings-file settings.json`. Validation and preview do not create, save, or activate configuration.

## Naming Conventions

Configuration property names use **PascalCase** at every level: `SchemaVersion`, `Server.Port`, `Providers.work.Endpoint`, and `Execution.MaxAttempts`. Enum values are written as `OpenAi`, `Anthropic`, `ApiKey`, and `RoundRobin`. User-defined provider/profile IDs, model IDs, aliases, URLs, and secret/environment references retain their supplied spelling.

Saved JSON, configuration CLI output, and management DTO properties now use PascalCase. Older camelCase documents remain readable; legacy `api_key` and `round_robin` enum spellings remain readable too. Saving emits the new form. Scripts reading `config show`, validation/preview output, or management responses must update case-sensitive property lookups, for example `server.proxyPort` becomes `Server.ProxyPort`. OpenAPI publishes the new management property names.

This does not change external protocol fields such as Anthropic `max_tokens`, Responses `store`, Claude settings `env`, or Codex TOML keys. Those follow their owning protocol, not AgentRouter configuration conventions.

## Environment Overrides

Environment variable names mirror the loaded JSON path. Replace each nesting separator with `__`; configuration translates it to `:`. Names are case-insensitive. There is no mandatory application or `LiveProviders` wrapper. The same mapping applies to all recognized settings, including provider dictionaries and indexed lists. Overrides change loaded values, not the JSON file on disk.

| JSON path | Environment or dotenv key | Configuration consumer |
| --- | --- | --- |
| `RequestTimeoutSeconds` | `RequestTimeoutSeconds` | Active Messages inference execution deadline |
| `MaxProviderAttempts` | `MaxProviderAttempts` | Initial upstream attempt plus permitted shared retries |
| `Server.Port` | `Server__Port` | Versioned settings validation/preview and explicit activation |
| `Providers.compatible.Endpoint` | `Providers__compatible__Endpoint` | Versioned settings validation/preview and explicit activation |
| `Providers.compatible.Credentials[0].Environment` | `Providers__compatible__Credentials__0__Environment` | Versioned credential reference, not a raw API key |
| `Execution.MaxAttempts` | `Execution__MaxAttempts` | Versioned settings validation/preview and explicitly activated dispatch |
| `Server.ProxyPort` | `Server__ProxyPort` | Persisted inference configuration |
| `Providers.work.BaseUrl` | `Providers__work__BaseUrl` | Persisted inference configuration |
| `Providers.work.ApiKey` | `Providers__work__ApiKey` | Private, runtime-only upstream credential override |
| `Providers.work.Models[0]` | `Providers__work__Models__0` | Persisted inference configuration |
| `Routing.DefaultModel` | `Routing__DefaultModel` | Persisted inference configuration |

For `Server.Port`, use `Server__Port=4600`. For the versioned default route, use `Routing__DefaultModel=compatible/owner/native-model`. Lowest-to-highest precedence is JSON, project-root dotenv, then process environment. Versioned CLI commands additionally apply repeated `--set` arguments last, such as `--set Server:Port=4600` or `--set Server__Port=4600`. Overrides are not saved.

Only recognized paths are consumed. Versioned environment overlays ignore unrelated root keys and reject unknown fields inside known settings sections. Indexed overrides replace individual list fields, not the entire list.

Operational controls that do not correspond to JSON properties use these Agent Router names:

| Variable | Purpose |
| --- | --- |
| `AR_CONFIG_DIR` | Select the data directory before configuration and secret storage are opened |
| `AR_SECRET_KEY` | Inject the existing hex-encoded master encryption key |
| `AR_BIND_HOST` | Override the standalone inference bind interface |
| `AR_PANEL_ORIGINS` | Reserved panel-origin control; defining its name does not implement a browser UI |
| `AR_SETTINGS_FILE` | Select the read-only versioned settings file |
| `AR_LIVE_TESTS` | Process-only paid-test consent; never enabled by JSON or dotenv |

The former gateway-prefixed environment names are no longer accepted. Rename deployment/service variables before restarting. For an injected master key, keep exactly the same value under `AR_SECRET_KEY`; do not generate a replacement or delete encrypted storage. Use `AR_CONFIG_DIR` to keep any nondefault storage location. These bootstrap controls are read from the process environment; they do not require a synthetic wrapper around JSON-backed settings.

## Versioned Settings And Preview

Version-1 settings separate server options, provider protocols and credential references, routing rules, execution budgets, and agent metadata. See [routing](../guide/routing.md) for when to use them.

`config validate` and `route preview` remain read-only: they do not activate settings, resolve credentials or contact providers. Foreground `serve --settings-file PATH` explicitly activates this contract. Ordinary `serve`, managed inference and provider/profile management continue using persisted configuration. The two configuration contracts are not merged during explicit activation.

The default global file is `settings.json` inside the existing gateway data directory (platform paths below). Select another file with `AR_SETTINGS_FILE` or `--file`. No file is created implicitly, and a missing, malformed, unknown-version or invalid selected file fails rather than falling back to another configuration. Existing SQLite, encrypted secrets and legacy global JSON remain untouched.

Precedence, lowest to highest:

1. Contract defaults and the selected JSON file.
2. Project-root `.env` settings matching JSON paths, such as `Server__Port`.
3. Process environment settings matching those paths.
4. Repeatable `--set KEY=VALUE` overrides, which are never persisted.

Existing `AR__...` versioned setting names remain compatibility aliases. Direct JSON-path names take precedence over aliases if both are supplied; prefer direct names and do not mix the two forms for one setting. `AR_SETTINGS_FILE` selects a file rather than overriding a JSON property.

Root discovery starts at the current directory, or `--project-root`, and uses the nearest `.git` directory/worktree marker or `global.json`. Without a marker, the starting directory is the root. Only that root's optional `.env` is loaded, not every ancestor's file. Environment/file-path selection is separate from typed setting overrides. Dictionary keys and array indices follow .NET configuration paths; indexed overrides replace fields, not entire arrays. Remove list entries in JSON rather than assuming an overlay truncates a list. JSON placeholders are not interpolated by this loader; credentials use explicit references instead.

```json
{
  "SchemaVersion": 1,
  "Server": {
    "Host": "127.0.0.1",
    "Port": 49250,
    "TokenEnvironment": "AR_GATEWAY_TOKEN"
  },
  "Providers": {
    "compatible": {
      "Protocol": "OpenAi",
      "Endpoint": "https://your-compatible-provider.example/v1",
      "Models": [
        "owner/native-model",
        "small-model"
      ],
      "Credentials": [
        {
          "Id": "primary",
          "Environment": "COMPATIBLE_API_KEY"
        },
        {
          "Id": "reserve",
          "Environment": "COMPATIBLE_RESERVE_KEY",
          "Enabled": false
        }
      ]
    },
    "reasoning": {
      "Protocol": "Anthropic",
      "Endpoint": "https://api.anthropic.com",
      "Models": [
        "your-anthropic-model-id"
      ],
      "Credentials": [
        {
          "Id": "primary",
          "Environment": "ANTHROPIC_API_KEY"
        }
      ]
    }
  },
  "Routing": {
    "DefaultModel": "compatible/owner/native-model",
    "Aliases": {
      "quick": "compatible/small-model"
    },
    "Rules": [
      {
        "Id": "review-worker",
        "Task": "review",
        "Subagent": "worker",
        "Model": "reasoning/your-anthropic-model-id",
        "Rewrite": {
          "MaxOutputTokens": 1024,
          "Temperature": 0.2
        },
        "FallbackModels": [
          "compatible/owner/native-model"
        ]
      }
    ]
  },
  "Execution": {
    "MaxAttempts": 3,
    "DeadlineSeconds": 60,
    "CredentialCooldownSeconds": 30
  },
  "Agents": {
    "review": {
      "Kind": "codex",
      "Model": "reasoning/your-anthropic-model-id"
    }
  }
}
```

Properties and validation:

| Section | Meaning and limits |
| --- | --- |
| `Server` | Loopback host only; port 1..65535. Activation requires `TokenEnvironment` naming a nonempty gateway token source, never a raw token. |
| `Providers` | Stable simple provider IDs, separate `OpenAi`/`Anthropic` protocol and case-preserved endpoint/native model IDs. Endpoints require HTTPS or loopback HTTP; user info, query strings and fragments are rejected. No automatic model discovery. |
| `Credentials` | Unique stable IDs, `Enabled`, and exactly one `Environment` variable name or encrypted `Secret` ID. No raw `ApiKey` field. Preview validates references structurally without checking their values/existence. Secret-ID lifecycle and rotation remain pending. |
| `Routing` | Default, aliases and ordered first-match rules. Conditions: `Agent`, `Task`, `Subagent`, `Workflow`, `RequestedModel`, and literal case-insensitive `ContainsText`. All supplied conditions must match. Labels are preview inputs, not proof of authenticated agent identity. |
| `Rewrite` | Only `MaxOutputTokens` (1..131072) and finite `Temperature` (0..2). Applied to cloned live options after explicit activation. No arbitrary JSON, authentication/header, tool-schema or tool-ID rewrites. |
| `Execution` | `MaxAttempts` 1..8; `DeadlineSeconds` 1..600; ordered `FallbackModels`. Activation applies the attempt/deadline caps. `CredentialCooldownSeconds` is validated but credential rotation/cooldown execution is not implemented. |
| `Agents` | Named `claude`, `codex`, `opencode`, or `claude-design` routing metadata, optional canonical `Model` and `Executable`. CLI launch still reads persisted profiles, not this dictionary. |

Preview precedence: explicit canonical provider/model or configured alias, then first matching rule, then a named agent's model, then default. A noncanonical requested name must match an alias or a rule; an unknown explicit model never silently becomes a default-provider charge. Provider IDs match case-insensitively; native model IDs match exactly and retain slashes. Rule and global fallback candidates are validated, deduplicated and reported with budgets; preview does not reserve keys or execute attempts.

```powershell
ar config validate --file ./settings.json --set Server:Port=4600
ar route preview --file ./settings.json --task review --subagent worker
ar route preview --file ./settings.json --model compatible/owner/native-model --set Execution:MaxAttempts=2 --set Execution:DeadlineSeconds=15
```

Non-secret root `.env` examples for these read-only commands:

```dotenv
AR_SETTINGS_FILE=./settings.json
Server__Port=4600
Execution__MaxAttempts=2
Execution__DeadlineSeconds=30
```

Do not place secret values in `--set`, issue reports, or URLs. Supply API keys privately through environment references or encrypted storage.

### Explicit Live Activation

```powershell
ar serve --settings-file ./settings.json
```

Activation requires one enabled environment credential per enabled provider. Multiple enabled credentials and encrypted secret references are rejected rather than silently ignored. Resolved secrets stay in memory. Settings activation does not edit global agent configuration or persisted providers. `AR_SETTINGS_FILE` alone does not opt into activation. Managed inference does not yet accept this settings-file switch.

Live requests use the same evaluator and precedence as preview. `X-AR-Agent`, `X-AR-Task`, `X-AR-Subagent` and `X-AR-Workflow` supply optional context, each limited to 128 characters without control characters. These values are client-reported, not authenticated agent identities or authorization scopes. Explicit canonical selectors and aliases win over conditional rules; launchers choosing canonical models therefore bypass those rules.

The executor tries deduplicated rule/global candidates in order within the shared deadline and `Execution.MaxAttempts`. Once candidates are exhausted, any remaining attempts retry the last candidate. Without fallbacks, retries use the selected model. All gateway-registered SDK clients have retries disabled, so attempts do not multiply. Transport failures, HTTP 408, 429 and 5xx may retry before output; other HTTP errors do not. Streaming retry/fallback stops after the first exposed provider update, even before text becomes visible. Shared backoff, Retry-After and provider/model protection are described below. Credential rotation, cost admission and a per-attempt usage ledger remain pending.

### Responses Subset

`POST /v1/responses` shares inference authentication, the 16 MiB body limit, routing, rewrites and execution policy with Messages. It accepts text or text-message history, instructions, client function declarations/calls/results, tool choice and numeric generation options. It emits JSON or sequenced text/function SSE lifecycle events with reported usage and incomplete/failed termination. Tools execute in the client.

Use full history and `store=false`. Text reasoning output/history is supported, but reasoning-effort controls and nonempty summaries are not. Stored/background responses, `previous_response_id`, custom/grammar/hosted tools, strict schema enforcement and multimodal content are unsupported. Unknown fields are rejected. See [protocol compatibility](../integrations.md); this does not certify a complete Codex session.

### Request Summaries And Telemetry

Set `Observability__Enabled=true` to enable the bounded background summary writer. It stores provider, selected model, requested selector, duration, outcome, dispatch count, configured SDK attempt limit and nullable reported input/output usage in SQLite. Cost remains unknown; pricing/provenance and failed-attempt usage accounting are not implemented. Queue capacity is 1024; full queues and storage failures drop summaries without blocking inference. Retention is at most 10000 rows and 30 days, cleaned on writes; reads exclude expired rows.

`GET /management/v1/requests?limit=100` requires the management token. The limit is clamped to 1..1000, newest first. Use the same `AR_CONFIG_DIR` for standalone inference and management. Explicit custom embedded/management storage-path propagation into child inference still needs integration coverage. There are no prompt bodies, agent-event records, search filters or pagination cursors in this initial summary API.

ServiceDefaults subscribes to `AgentRouter.Gateway`: inference spans, request/attempt counters and duration histogram, plus sanitized request-completion logs. Configure `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` for an HTTP OTLP collector. Gateway telemetry does not represent unobserved local tool execution. Opt-in native agent export is described in [launch isolation](cli.md#launch-isolation).

## Persisted Configuration

Use the [CLI configuration commands](cli.md) for normal setup. Provider, route, and profile mutations share validation with management APIs. `config show` reports saved state with secrets masked or omitted; it does not disclose runtime-only secret overrides.

| Platform | Default data directory |
| --- | --- |
| Windows | `%USERPROFILE%\.agent-router` |
| macOS/Linux | `$HOME/.agent-router` (`~/.agent-router`) |
| Override | `AR_CONFIG_DIR` |

The default uses the user profile directory on every platform, not AppData or `XDG_CONFIG_HOME`. Explicit directory arguments take precedence over `AR_CONFIG_DIR`, which takes precedence over this default. Global versioned settings, legacy user JSON, SQLite, encrypted secrets, provider logos, logs and service state share this root. Project-local dotenv files remain project-local; this change does not relocate them or Claude's own settings.

Existing files are not automatically migrated, deleted or read from the former default locations. To keep using an existing data directory, set `AR_CONFIG_DIR` explicitly. To migrate, stop all gateway processes first, then move the complete data directory contents into `.agent-router`, keeping the database and master key together. Move any legacy global user JSON from its former `~/claude-code-gateway` folder separately if needed. Never move or copy a live SQLite database; retain companion files when present. The global user JSON now follows the shared directory override too. Do not initialize a replacement configuration if your intent is to retain saved providers and secrets.

The directory contains `config.json`, `gateway.sqlite`, and `secret.key`. Providers, routes, profiles, and non-secret runtime settings are stored in `config.json`; provider credentials and the generated gateway token are encrypted separately. SQLite retains request history, runtime state, and the configuration version marker. Protect the entire directory: possession of the database and key can allow decryption. Stop all gateway processes before taking a file-level backup, and retain SQLite companion files when present. Do not edit a live database.

The SQLite location is configurable in `config.json`:

```json
{
  "Storage": {
    "DatabasePath": "runtime/gateway.sqlite"
  }
}
```

Relative paths are resolved from the AgentRouter data directory. The default is `gateway.sqlite` in that directory. `Storage__DatabasePath` and the programmatic `StorageOptions.DatabasePath` override the JSON value; the programmatic option has highest precedence.

Once saved configuration includes providers, it supplies the provider list, default route, and tier routes. Runtime composition overlays matching configuration paths onto an in-memory copy before projection and validation. Project dotenv overrides JSON, process environment overrides dotenv, and supported command arguments override environment. For example, `Providers__work__BaseUrl` overrides the saved `Providers.work.BaseUrl`; `Routing__DefaultModel` overrides the saved default model. Saved documents and encrypted secrets are not overwritten by these runtime overrides. Explicit host/listener arguments still take priority. CLI configuration reads and management reads continue showing saved state, not environment-only secrets. Binding an existing policy field does not implement a currently unsupported policy.

Enabled API-key providers support OpenAI-compatible or native Anthropic requests. IDs `azure` and `github` retain their existing Azure OpenAI and GitHub Models adapter behavior. Gemini upstream protocols and OAuth import are not implemented. Managed inference requires saved enabled providers/models; standalone `serve` retains legacy fallback configuration.

Recognized explicit `provider/model` requests take precedence over tier aliases. Unrecognized or missing client models use the configured tier route when applicable, otherwise the default route. Model discovery lists configured enabled models; it does not fetch or refresh upstream catalogs. Apply provider or route changes with `ar service reload` for managed inference, or restart standalone `serve`. Managed restart validates replacement configuration before draining the old host; management stays available on inference failures.

## Inference Request Limits

Inference request bodies under `/v1` have a fixed **16 MiB (16,777,216 bytes)** limit. The limit counts body bytes after HTTP transfer framing is removed, not chunk headers. It is enforced while reading, without an additional full-body buffer. Bodies exactly at the limit are accepted if otherwise valid; oversized authenticated message requests return HTTP 413 with an Anthropic `invalid_request_error` before calling a provider or committing SSE. Authentication is checked first when configured.

Message requests require `application/json` or an `application/*+json` content type. Malformed or missing JSON returns typed HTTP 400; unsupported content types return typed HTTP 415. This limit is not currently a configuration option. Existing server/reverse-proxy limits can reject requests earlier. Management retains its separate 1 MiB limit. Provider/model concurrency is bounded as described below; token/cost admission remains unimplemented.

### Execution Deadline

`RequestTimeoutSeconds` bounds active `/v1/messages` execution from body parsing through upstream completion or SSE writing. The default is **120 seconds**; accepted values are **1..3600 seconds**. Invalid values reject host startup. It is a root host option, so its equivalent environment/dotenv key is also `RequestTimeoutSeconds`:

```dotenv
RequestTimeoutSeconds=90
```

The setting applies to ordinary standalone and managed inference without changing saved configuration. Explicit schema-1 activation instead takes its deadline from `Execution__DeadlineSeconds`. Live tests separately use `LiveTests__DeadlineSeconds`.

The same linked cancellation token covers upstream SDK execution, shared retry delays and streaming enumeration; each retry does not receive a fresh gateway deadline. Expiry before response output returns HTTP 504 with the existing Anthropic `overloaded_error` timeout envelope. Expiry after SSE output starts aborts the connection without emitting a successful `message_stop`, changing status, replaying the request or switching providers. Caller disconnect/cancellation still cancels upstream work and is not relabeled as a gateway deadline. Completed non-streaming JSON response serialization and server/reverse-proxy connection limits are separate from this execution timer.

The gateway relies on cancellation-aware SDK transports; this is not a sandbox for uncooperative custom clients. Credential reservation and token/cost admission remain pending. Explicit schema-1 activation adds the bounded fallback policy described above.

### Provider Attempt Budget

`MaxProviderAttempts` is the maximum number of shared inference attempts, **including the initial request**, in legacy hosting. It defaults to **1** (no retries) and accepts **1..8**. Explicit schema-1 activation instead uses `Execution__MaxAttempts` across the selected model and its fallback chain. Invalid values reject composition before serving requests.

This root runtime host option uses the same name in JSON, dotenv and process environment. For example:

```dotenv
RequestTimeoutSeconds=90
MaxProviderAttempts=3
```

Three means one initial attempt and at most two retries under the same deadline. `Microsoft.Extensions.Resilience` owns the retry pipeline for both hosting modes; SDK retries are disabled in gateway composition. The standalone public SDK registration overloads retain their explicit SDK retry arguments. Do not layer an additional retry policy around a custom gateway client. Preview alone never activates model switching.

**Behavior change:** Legacy attempts now use the same transient classifier and backoff as schema-1 routing, rather than SDK-specific retry eligibility. Only connection failures without an HTTP status, HTTP 408, 429 and 5xx are retried. Other HTTP errors, caller cancellation and deadline expiry are not. Set a higher budget only when repeated inference requests and possible extra charges are acceptable; an interrupted operation may already have incurred usage.

Retries use exponential backoff starting at 200 ms, jitter and a 5-second backoff ceiling. Valid `retry-after-ms` or `Retry-After` seconds/HTTP-date headers override the backoff, including delays longer than five seconds. Waiting consumes the original deadline; a server delay exceeding that deadline does not trigger an early retry. No hedging or parallel speculative inference is enabled.

Each configured provider/model has in-process protection shared across requests: at most **64 concurrent calls**, no admission queue, and a circuit breaker with a **50% transient-failure ratio**, **10-call minimum throughput**, **30-second sampling window** and **15-second break duration**. Limits are currently fixed, not settings fields. A stream holds its admission lease until completion or disposal. Breaker accounting covers JSON execution and stream priming, not failures after streaming commitment. Capacity/open-circuit rejections return sanitized HTTP 503, or advance the bounded fallback chain when attempts remain; they do not count as upstream dispatches. Host restart resets this state.

Caller cancellation and the full-stream deadline remain linked after priming. Neither retry nor provider switching occurs after the first exposed update. Polly pipeline logs are disabled to avoid exposing SDK exception bodies; existing sanitized gateway summaries remain available. Neither mode has a per-attempt usage ledger, credential rotation or cost admission. Caps cover registered SDK clients, not arbitrary custom-client side effects or independently configured redirects. Supplied clients must honor cancellation and avoid hidden retries.

## Legacy Host Options

Primary runtime options are defined in [`ClaudeGatewayOptions.cs`](https://github.com/mehdihadeli/agent-router/blob/main/src/AgentRouter.Core/ClaudeGatewayOptions.cs). When no saved providers exist, existing configuration sources remain available.

Namespace ownership:

- `AgentRouter.Core.Hosting` owns configuration providers and binding helpers.
- `AgentRouter.Core` owns `ClaudeGatewayOptions`.
- `AgentRouter.Core.Contracts` owns shared provider configuration models.
- `AgentRouter.Core.Routes` owns route parsing and route-model resolution.

Important keys:

- `GatewayBaseAddress`
- `EnableTelemetry`
- `Providers`
- `RouteModel`
- `GatewayAuthToken` (legacy client authentication)
- `ApplyClaudeSettings` (default `false`; explicitly enables global Claude settings writes)

`Microsoft.Extensions.AI.IChatClient` handles upstream requests. Agent Framework is not required: the gateway translates and forwards tool calls and results, while the client agent executes tools.

ServiceDefaults exports logs, metrics, and traces when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured, without an Aspire AppHost. `EnableTelemetry` controls the AI wrapper. Sensitive AI payload capture defaults off; explicitly set `Telemetry:EnableSensitiveData=true` (or `Telemetry__EnableSensitiveData=true` in environment variables) only when the exporter and access policies permit recording prompt and tool content.

Example user config:

```json
{
  "GatewayBaseAddress": "http://localhost:4000",
  "EnableTelemetry": false,
  "Providers": [
    {
      "ProviderType": "Azure",
      "BaseAddress": "$AZURE_OPENAI_ENDPOINT",
      "ApiKey": "$AZURE_OPENAI_API_KEY",
      "Models": [
        "gpt-4.1"
      ]
    }
  ],
  "RouteModel": {
    "DefaultModel": "azure/gpt-4.1",
    "ThinkingModel": "azure/gpt-4.1",
    "SearchModel": "azure/gpt-4.1"
  }
}
```

Environment placeholders support `$NAME` and `${NAME}` forms.
