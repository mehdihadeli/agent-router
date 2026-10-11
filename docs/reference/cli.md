# CLI Reference

The gateway is available as a repo-local launcher and as a packaged .NET tool.

Product name: **Agent Router**. Install package `AgentRouter`; installed command is `ar`. Repo-local launchers are `ar.ps1`, `ar.cmd`, and `ar`; full-name `agent-router` launchers remain available. Existing `CC_GATEWAY_*` variables, the `claude-code-gateway` data directory, legacy configuration paths, and inference identity header are retained for compatibility. No credentials or existing configuration are moved by this rename.

Configuration commands persist providers, routes, and Claude Code profiles in SQLite. Secrets are stored separately using AES-GCM. The proxy defaults to `http://127.0.0.1:49250` and requires its generated client token.

## Commands

| Command | Purpose |
| --- | --- |
| `init [--port <PORT>]` | Initialize local storage and client token; preserve existing configuration. |
| `provider add <ID> --base-url <URL> --models <CSV> [--protocol OpenAi\|Anthropic] [--api-key-env <NAME>]` | Add a named API-key provider. Reads `OPENAI_API_KEY` by default, or prompts privately when input is interactive. |
| `provider list` | List configured providers and models without credentials. |
| `provider remove <ID> [--if-base-url <URL>]` | Remove an unreferenced provider and its encrypted credentials. Optional URL guard refuses mismatches. |
| `provider use <PROVIDER/MODEL> [--tier <TIER>]` | Set `Default`, `Opus`, `Sonnet`, or `Haiku` route. |
| `provider check <PROVIDER/MODEL>` | Send a small real upstream request with a 30-second timeout; may incur charges. |
| `profile add <NAME> [--model <PROVIDER/MODEL>] [--executable <PATH>]` | Save a Claude Code launch profile; executable defaults to `claude`. Use a native executable, not `.cmd` or `.bat`. |
| `profile list` | List saved profiles. |
| `config show` | Print JSON with secrets masked or omitted. |
| `config token [--managed]` | Print sensitive inference token, or a verified running service's separate management token. |
| `serve [-p\|--port <PORT>] [-u\|--url <URL>]` | Run in foreground; aliases `s`, `run`; default command when none is specified. |
| `start [--management-port <PORT>] [--no-gateway]` | Start or reuse verified background service; management and optional inference use separate listeners. |
| `stop` | Gracefully stop only verified managed service; clean stale state without terminating an unrelated process. |
| `web [--management-port <PORT>] [--no-gateway]` | Run management API and optional inference in foreground. Angular UI and browser opening are not included yet. |
| `status [--url <URL>] [--managed]` | Check inference, or verify managed instance and report actual listener URLs. Exit `0` when available, `1` otherwise. |
| `launch <PROFILE> [--url <URL>] -- <AGENT ARGUMENTS>` | Launch an enabled Claude Code profile against an already running local gateway. |

`serve --port` binds `127.0.0.1`. `--url` takes precedence and accepts an absolute HTTP(S) listener URL. Ports must be between `1` and `65535`. An HTTPS listener needs an appropriate ASP.NET Core certificate configuration.

Restart inference after changing providers or routes: restart foreground `serve`, or run `stop` then `start`. There is no hot configuration reload yet. Reusing `start` reports the existing service and does not apply new flags or saved configuration.

## Managed Lifecycle

Management binds only `127.0.0.1`, defaults to port `49251`, and does not inherit ambient Kestrel listener configuration. `--management-port 0` selects a free port and prints the actual URL. Fixed-port conflicts fail rather than silently moving inference or management. `--no-gateway` starts management with no configured providers; omit it only after configuring inference.

```powershell
./agent-router.ps1 start --no-gateway --management-port 0
./agent-router.ps1 status --managed
./agent-router.ps1 stop
```

Startup waits at most 30 seconds for authenticated readiness. A shared file lock serializes start/stop/status. Runtime identity includes instance ID, PID, process start time, and version. State in `service.json` contains no token; per-instance management tokens use the existing encrypted secret store. Persistent empty `service.lock` and `config.lock` files are not indicators that a process is running.

`stop` requests graceful shutdown and waits up to 10 seconds; it never falls back to killing an arbitrary PID. Startup failure terminates only the child started by that invocation. Dead or reused-PID state is cleaned on inspection. Malformed state or an unauthenticated live identity fails closed and needs operator investigation, not an automatic process kill. A foreground `web` process cannot replace an already-running managed instance.

Windows subprocess integration tests verify detached output handles, foreground ownership, concurrent fresh starts, listener conflicts, and both-listener shutdown. Linux/macOS process lifecycle verification remains pending.

## Management API

Every endpoint requires `Authorization: Bearer <management-token>`. Get that sensitive credential explicitly with `config token --managed`; never include it in URLs, screenshots, or diagnostic logs. Inference tokens cannot authorize management, and management tokens cannot authorize inference. There is no unauthenticated discovery endpoint. Unexpected Host or cross-origin Origin headers are rejected; permissive CORS and cookie authentication are not enabled.

| Method and path | Behavior |
| --- | --- |
| `GET /management/v1/identity` | Authenticated readiness and process identity. |
| `GET /management/v1/config` | Dedicated configuration DTO: ports, active provider, safe provider/profile/route data, and credential-presence flags; no stored credential values. |
| `GET /management/v1/providers` | Provider response DTOs with `hasApiKey`, never API-key values or suffixes. |
| `POST /management/v1/providers/{id}` | Create enabled provider from `baseUrl`, `models`, `apiKey`, and optional `compatibility`. |
| `PUT /management/v1/providers/{id}` | Update endpoint/models/protocol/enabled state; omitted or masked `apiKey` preserves existing key. |
| `DELETE /management/v1/providers/{id}` | Delete only unreferenced provider and its encrypted credentials. |
| `GET /management/v1/profiles` | List isolated Claude CLI profile DTOs. |
| `POST /management/v1/profiles/{name}` | Create enabled profile from optional `model` and `executable`. |
| `PUT /management/v1/profiles/{name}` | Update model, executable, and enabled state. |
| `DELETE /management/v1/profiles/{name}` | Remove saved profile. |
| `PUT /management/v1/routes/{tier}` | Assign `model` selector to Default/Opus/Sonnet/Haiku tier. |
| `POST /management/v1/shutdown` | Stop authenticated managed instance and its optional inference listener. |
| `GET /management/v1/openapi/management-v1.json` | Authenticated OpenAPI document with request/response schemas. |

Creation returns `201`, updates/deletes `204`, malformed configuration `400`, missing resources `404`, and duplicates/dependency conflicts `409`. Management bodies are limited to 1 MiB. Provider deletion and removal of referenced models are refused until routes/profiles are reassigned. CLI and management mutations share a cross-process configuration lock; direct storage consumers must coordinate separately. Optimistic concurrency and one transaction spanning configuration and secrets remain future work.

`restartRequired` documents the restart-to-apply policy; it does not measure differences against the running inference snapshot. Angular UI, automatic browser opening, management-driven agent launch, and in-place inference restart remain pending.

## Examples

```powershell
./agent-router.ps1 init --port 4600
# Set OPENAI_API_KEY privately, or let provider add prompt for it.
./agent-router.ps1 provider add work --base-url https://api.openai.com/v1 --models gpt-4.1,gpt-4.1-mini
./agent-router.ps1 provider use work/gpt-4.1
./agent-router.ps1 provider use work/gpt-4.1-mini --tier Haiku
./agent-router.ps1 profile add Review --model work/gpt-4.1
./agent-router.ps1 serve
```

In another terminal:

```powershell
./agent-router.ps1 status
./agent-router.ps1 launch Review -- --permission-mode plan
```

On macOS/Linux use `bash ./ar`; packaged installations use `ar`.

## Launch Isolation

`launch` sets `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN`, model, and configured tier overrides only on the child process. It removes inherited `ANTHROPIC_API_KEY` from that process, preserves individual arguments, and returns the agent exit code. Parent environment and global Claude settings remain unchanged.

Only absolute loopback HTTP(S) gateway URLs without credentials, paths, queries, or fragments are accepted for profile launches. Remote profile launching, Codex/OpenCode profiles, and desktop app launching are not implemented.

## Security

`config token` output is a credential: do not paste it into logs, screenshots, or tickets. It is separate from upstream provider keys. Clients authenticate using `x-api-key` or `Authorization: Bearer`; an arbitrary non-empty token is not accepted by the configured gateway.

Use loopback listeners for local work. Remote listeners require firewall/private-network controls and TLS; they do not make `launch` support remote gateways. Protect the storage directory and backups, which contain encrypted credentials and their local key material.
