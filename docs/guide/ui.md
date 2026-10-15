# Web UI Guide

AgentRouter includes a local Angular management UI for configuration, lifecycle control, profiles,
and request observability. It is served by the management process and uses a short-lived browser
pairing flow rather than placing a permanent management token in a URL.

## Start The UI

```powershell
ar ui
```

The command starts or reuses local management, prints a loopback UI address and a pairing code,
and opens the browser. To open it manually:

```powershell
ar ui --no-open
```

Enter the pairing code in the browser within two minutes. Keep the code private: it grants
management access. The UI is intended for local management; remote exposure requires explicit
network and authentication controls.

## Overview

The Overview page shows:

- Gateway state and listener address.
- Start, pause, and restart controls.
- Provider, model, profile, and retained-request counts.
- A 24-hour observability band with request count, success rate, errors, average and p95 latency,
  input/output tokens, top route, last activity, and known cost.
- Configured providers and model routes.

The observability band uses the same sanitized summary contract as `ar observability`.

## Providers

The Providers page can:

- Add an OpenAI-compatible or Anthropic-compatible provider.
- Set endpoint, model list, protocol, and enabled state.
- Update providers while preserving an existing key when the key field is omitted or masked.
- Remove unreferenced providers.
- Show key presence without revealing key values.

Provider connectivity checks remain explicit CLI operations and are not triggered automatically by
opening or editing the page.

## Routing

The Routing page edits the default, Opus, Sonnet, and Haiku model selectors. Saved changes are
shown as requiring an inference restart. Use the restart action after reviewing the change.

## Profiles

The Profiles page provides create, edit, and delete operations for:

- Claude Code, Codex, OpenCode, and Claude Design profile kinds.
- Model route and native executable.
- CLI, app, or CLI-and-app entry mode.
- Launch or system scope metadata.
- Claude Opus, Sonnet, and Haiku tier routes.
- Enabled state and optional native telemetry endpoint.

Profiles are isolated launch contracts. The UI does not modify global agent settings. Claude Design
is currently app metadata and does not have a verified desktop launcher.

## Requests And Observability

The Requests page lists the newest retained sanitized summaries with timestamp, provider/model,
outcome, latency, and reported input/output tokens. The retention store is bounded and separate
from OTLP export.

OpenTelemetry traces, metrics, and sanitized logs are exported when configured with
`OTEL_EXPORTER_OTLP_ENDPOINT`. The browser does not query an OTLP backend directly. It reads the
management API's bounded summary and request endpoints, so credentials and prompt/response bodies
remain outside browser state.

## Lifecycle And Safety

- Start and restart require configured enabled models.
- Pause stops inference while keeping management available.
- Restart drains active requests before replacing inference.
- Configuration mutations use the management credential, not the inference token.
- Browser pairing is short-lived; disconnect ends the browser session.
- Saved secrets are never returned to the UI.
- The UI does not launch local agent processes; use `ar profile launch` for that workflow.

For command-level setup and service recovery, see the [CLI guide](cli.md) and
[CLI reference](../reference/cli.md). For telemetry configuration and interpretation, see
[observability](observability.md).
