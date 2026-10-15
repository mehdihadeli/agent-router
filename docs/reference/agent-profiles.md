# Agent Profiles

Agent Router profiles are saved launch contracts. A profile chooses a gateway model route, identifies the local agent executable, and declares which opening surface is allowed. Profiles are isolated: creating or launching one does not rewrite global Claude, Codex, or OpenCode settings.

## Profile fields

| Field | Values | Meaning |
| --- | --- | --- |
| `Name` | Local name | Stable profile label and bounded agent attribution value. |
| `Kind` | `claude`, `codex`, `opencode`, `claude-design` | Selects the child configuration contract. |
| `Model` | `provider/native-model-id` | Default route. Empty uses the gateway default. |
| `Executable` | Native executable path or name | Shell wrappers ending in `.cmd` or `.bat` are rejected. |
| `EntryMode` | `cli`, `app`, `cli-and-app` | Declares the surfaces a future launcher may expose. |
| `Scope` | `launch`, `system` | `launch` is child-only. `system` records intent for a consented system adapter; it does not edit files today. |
| `TierModels` | `opus`, `sonnet`, `haiku` map | Optional Claude tier routes. Empty tiers inherit gateway routing. |
| `TelemetryEndpoint` | HTTPS or loopback HTTP root | Optional child-only native telemetry exporter. |
| `Environment` | `KEY=VALUE` map | Optional child-only environment overrides. Gateway-controlled credentials and endpoints are rejected. |
| `WorkingDirectory` | Absolute path | Optional working directory for the launched child process. |

## Supported contracts

### Claude Code

`claude` uses the Anthropic-compatible gateway surface. The launcher sets `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_MODEL`, and tier variables only in the child process. The inherited `ANTHROPIC_API_KEY` is removed. Claude Code remains responsible for files, commands, permissions, and tools.

### Codex

`codex` receives a child-only Responses provider through native `--config` arguments and `AR_AGENT_TOKEN`. Upstream secrets never appear in arguments. This uses the gateway's bounded Responses subset; stored continuations, multimodal input, and unsupported custom tools remain outside the contract.

### OpenCode

`opencode` receives an OpenAI-compatible gateway root under `/v1`, the gateway token as the child `OPENAI_API_KEY`, and the selected route as `OPENCODE_MODEL`. The profile does not claim desktop profile switching or invent an OpenCode configuration file format. A local OpenCode release must support this environment contract for the launcher to be useful.

### Claude Design

`claude-design` is app-only and launch-scoped. It can be registered and returned by the management API, but the CLI launcher rejects it with an explicit app-only error. A desktop adapter must be added only after its executable and protocol contract are verified.

## CLI examples

```powershell
ar profile add Review --kind claude --model work/gpt-4.1
ar profile add OpenCodeWork --kind opencode --model work/gpt-4.1 --executable opencode
ar profile add Design --kind claude-design --entry-mode app --executable claude-design
ar profile add Review --working-directory C:\src\review --env CLAUDE_CODE_MAX_OUTPUT_TOKENS=2048
ar profile list
ar profile edit Review --tier-sonnet work/gpt-4.1
ar profile edit Review --env CLAUDE_CODE_MAX_OUTPUT_TOKENS=4096 --working-directory C:\src\review
ar profile remove Review
ar profile launch Review -- --permission-mode plan
```

The gateway must already be running. `profile launch` accepts arguments after `--` as literal process arguments and returns the child exit code. It only accepts loopback HTTP(S) gateway roots.

## Management API

`GET /management/v1/profiles` returns kind, entry mode, scope, model, executable, tier routes, and telemetry endpoint without upstream credentials. `POST` and `PUT` accept `Kind`, `EntryMode`, `Scope`, `TierModels`, and `TelemetryEndpoint`; profile validation runs before mutation. The Angular Profiles view provides create, edit, delete, enable/disable, and all of these fields. Management authentication is separate from the inference token.
