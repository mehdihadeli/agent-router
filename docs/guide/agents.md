# Connect Agents

Start the gateway before launching an agent. AgentRouter forwards model requests; the agent keeps responsibility for local files, commands, and tools.

Agent Router profiles are local launch contracts. They select a gateway route and describe which
surface may open the agent. They do not install agents, modify global agent settings, or execute
agent tools on the gateway.

## Profile kinds and surfaces

| Kind | Supported surface | Child configuration | Notes |
| --- | --- | --- | --- |
| `claude` | CLI, or `cli-and-app` metadata | Anthropic Messages URL, token, default and tier models | Claude Code CLI is the verified local launcher. |
| `codex` | CLI | Responses provider arguments and child-only token | Responses support is bounded; see [protocol limits](../integrations.md#responses-and-codex-limits). |
| `opencode` | CLI | OpenAI-compatible URL, token, model, and `X_AR_AGENT` context | Uses the local OpenAI-compatible contract; OpenCode desktop switching is not automated. |
| `claude-design` | App metadata only | Anthropic-compatible route metadata | App launch requires a future desktop adapter; `profile launch` rejects this kind. |

`EntryMode` is `cli`, `app`, or `cli-and-app`. `Scope` is `launch` (child-only and the default)
or `system` (a saved intent for a future consented system adapter). The current CLI launcher
rejects `app` profiles rather than modifying global files or guessing a desktop protocol.

## Claude Code

The Angular Profiles view provides the same profile configuration fields as the CLI, including
agent kind, route, executable, entry mode, scope, Claude tier routes, enabled state, and optional
native telemetry endpoint. It can create, edit, and delete profiles. It does not launch a local
agent process from the browser.

Install Claude Code separately, then create a profile for a configured model:

```powershell
ar profile add Review --model work/gpt-4.1
ar profile list
ar profile launch Review -- --permission-mode plan
```

Create an OpenCode profile with the same isolated child contract:

```powershell
ar profile add OpenCodeWork --kind opencode --model work/gpt-4.1 --executable opencode
ar profile launch OpenCodeWork
```

Register an app-only Claude Design profile for management clients:

```powershell
ar profile add Design --kind claude-design --entry-mode app --executable claude-design
```

Arguments after `--` are passed to the agent as separate arguments. A profile does not install the agent or start the gateway automatically. Use `--url http://127.0.0.1:4600` when selecting a specific local listener.

Launch sets `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN`, and model/tier overrides in the child process. Inherited `ANTHROPIC_API_KEY` is removed from that child. Parent variables and global Claude settings are not modified. The command returns the agent's exit code.

Use a native executable with `--executable` if discovery does not find your installation. Shell wrappers are rejected. Gateway launch URLs must be loopback HTTP(S) roots without credentials, paths, queries, or fragments.

Claude Code profiles can carry `opus`, `sonnet`, and `haiku` tier selectors in the management
contract. Empty tiers inherit the gateway's configured tier route. Profile names are forwarded as
bounded `X-AR-Agent` context; this is attribution, not an authentication or authorization claim.

## Experimental Codex

```powershell
ar profile add CodexTrial --kind codex --model work/gpt-4.1
ar profile launch CodexTrial
```

The launcher supplies a custom Responses provider through child-only configuration and an environment token. It does not edit global Codex files. Existing user/project settings still load; this is not a separate `CODEX_HOME`.

The gateway supports a stateless Responses subset. Stored continuations, custom/freeform patch tools, multimodal input, and other unsupported semantics may prevent ordinary Codex tasks from working. Read [protocol limits](../integrations.md#responses-and-codex-limits) before relying on it. Full Codex session compatibility is not certified.

## Other Clients

Clients can connect directly if they use the supported Messages or Responses subset. Use the configured gateway URL and inference token. `ar config token` prints that sensitive token for manual setup; do not paste its output into tickets, logs, or URLs. It cannot authorize management operations.

## Optional Agent Telemetry

`profile add` accepts `--telemetry-endpoint` for child-only native exporter configuration. This is separate from gateway telemetry. Delivery depends on the installed agent version and has not been verified end to end. Prepare collector access controls, redaction, and retention first; disabling prompt logging does not prove every native tool-event field is redacted. See [observability](observability.md).