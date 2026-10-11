# Reference Feature Coverage

Review date: 2026-10-12. This is a requirements and implementation-gap review, not a claim of feature parity or a line-by-line audit of the reference repository. The attached CLI implementation/README, representative core implementations, and relevant English documentation were compared with current gateway runtime paths and tests. Reference behavior informs requirements; implementation remains native .NET and Angular, not a port of its JavaScript architecture.

Statuses: **Supported** means a concrete runtime path exists; **Partial** means only a subset exists; **Missing** means no corresponding runtime path was found. DTO fields, installed SDKs, successful compilation, and passing unrelated tests do not establish support.

## CLI Coverage

| Reference behavior | Status | Current behavior and remaining requirement |
| --- | --- | --- |
| Foreground serve | Supported | `serve`, `s`, and `run` remain inference-only; `web` provides separate foreground management and optional inference. |
| `web` alias | Partial | Foreground management command exists; Angular/browser-open behavior and exact alias semantics are not implemented. |
| Background `start` | Supported | Serialized startup, bounded authenticated readiness, non-secret state, encrypted management token, owned-child failure cleanup; Windows subprocess tests pass. |
| `ui` start/reuse/open | Missing | Requires Angular assets and authenticated management API; browser-open failure must leave running service usable. |
| `stop` | Supported | Authenticated graceful shutdown after instance/PID/start-time/version verification; no PID-only kill fallback. |
| Management host/port | Partial | Loopback-only `--management-port`, including dynamic port zero; remote host selection remains intentionally unavailable. `serve --port` keeps inference meaning. |
| Management-only mode | Supported | `start`/`web --no-gateway` work with zero providers. |
| Open/no-open | Missing | Headless operation must not require browser launch. |
| Separate management credential | Supported | Separate per-instance encrypted token; explicit sensitive retrieval via `config token --managed`; tests verify mutual credential rejection. |
| Port fallback | Partial | Dynamic management port selection and actual URL reporting exist; fixed-port conflicts fail safely, with no next-port retry. |
| Existing-service reuse | Supported | Authenticated identity/readiness and OS start-time verification; reuse does not silently apply new flags/configuration. |
| Startup lock/stale state recovery | Partial | Concurrent fresh starts, failed startup, stale/reused PID, and forged state tested. Malformed state deliberately fails closed; crash/abandoned-lock and cross-platform fixtures remain to expand. |
| Profile by name or ID | Partial | `launch <name>` matches enabled Claude profiles case-insensitively; no stable profile ID or top-level shorthand. |
| CLI/app surface selection | Missing | Current launcher is Claude CLI only; `.cmd`/`.bat` launchers rejected. App surfaces need installed-app detection and explicit argument restrictions. |
| Agent argument forwarding/exit status | Supported | Child `ArgumentList` preserves arguments; launcher returns exit status and handles cancellation. |
| Shared temporary service leases | Missing | Requires process identity-aware leases, crash cleanup, ownership transfer, and shutdown only after last managed session exits. |
| Config preservation on uninstall | Partial | Configuration is outside tool install directory; validate package upgrade/uninstall lifecycle explicitly. |
| Remote management | Missing | Not enabled by inference bind settings; requires TLS, restricted origins/hosts, authentication, and documented trust boundary. |

Existing gateway-only `status`, `init`, `config show/token`, provider commands, and profile commands are additional explicit CLI management surfaces. Keep them as reusable alternatives to UI-only workflows.

## Core Coverage

| Reference capability | Status | Current implementation or gap |
| --- | --- | --- |
| Anthropic Messages ingress, JSON/SSE, tool round-trip | Supported | HTTP adapter converts requests/results through `IChatClient`; client agent executes tools. |
| OpenAI Chat/Responses and Gemini Generate/Interactions ingress | Missing | An OpenAI-compatible upstream is not an OpenAI-compatible inbound API. Add dedicated adapters and conformance tests. |
| OpenAI Chat and Anthropic upstreams | Supported | Explicit API-key protocol selection, official SDK-backed keyed clients, fake-upstream transport/stream/tool tests. Live upstream checks remain opt-in. |
| Azure OpenAI and GitHub Models | Partial | Adapter registration exists; Azure-specific transport/deployment/auth behavior needs focused verification beyond generic OpenAI tests. |
| Responses/Gemini upstreams | Missing | Chat abstraction alone does not promise protocol-specific sessions, background jobs, or interactions semantics. |
| Provider presets, probing, catalog refresh | Missing | Manual endpoint/model setup and explicit charged connection check exist; no automatic discovery. |
| Provider/model CRUD | Supported | Typed management provider create/read/update/delete, manual model lists, enable/disable, dependency conflicts, write-only credentials, and CLI guarded removal; discovery remains separate. |
| Local agent authorization import/OAuth refresh | Missing | Use explicit consent and refresh coordination; do not scan/export credentials silently. |
| Credential scheduling/pools | Missing | Need priority/weight/limit selection, cooldown, outcome accounting, and safe concurrent updates. |
| Account quota/balance connectors | Missing | Model usage telemetry does not represent provider subscription balance or quotas. |
| Client keys, expiry, revocation, local limits | Missing | Single persisted gateway token exists; no multiple scoped client keys or request/token/image quotas. |
| Explicit/default/tier routing | Supported | Recognized `provider/model` and Opus/Sonnet/Haiku route resolution. |
| Ordered condition rules and rewrites | Missing | Need typed rules, deterministic precedence, validated target models, and dry-run diagnostics. |
| Claude billing cleanup/subagent tags | Missing | Need bounded tag extraction/removal and opt-in model-description instruction injection; ordinary tier aliases are not subagent routing. |
| Retry/failover/model chains | Missing | Retry count, failure classes, cancellation, deadlines, and no retry after committed stream output must be defined. SDK retries/resilience defaults do not establish this feature. |
| Script routing and plugins | Missing | Trusted-code execution is a distinct boundary; prefer typed policies first. Do not claim .NET in-process loading is a sandbox. |
| Multi-agent profiles | Partial | Isolated Claude CLI launch only; Codex, OpenCode, Grok, Kimi, Pi, Kilo, ZCode, WorkBuddy, and desktop surfaces need individual adapters and tests. |
| Configuration persistence/secrets | Partial | SQLite, AES-GCM, layered configuration, typed versioned management API, serialized cross-process mutations, and atomic first-run master-key publication. Import/export, optimistic concurrency, config/secret transaction atomicity, and legacy import parity remain. |
| Runtime reconfiguration | Missing | Saved providers/routes currently require host restart. Need validated snapshots and orderly in-flight request draining. |
| Standard telemetry | Partial | ServiceDefaults logs/metrics/traces and AI subscriptions exist without AppHost; validate actual OTLP export and gateway semantic correlation. |
| Request logs and agent observability | Missing | No searchable request/body store, retention/sampling, cost pricing service, agent timelines, or telemetry query UI. Gateway cannot observe unreported local agent actions. |
| Dashboard/usage reports/share cards | Missing | Need backend aggregation/query contracts as well as Angular views. |
| Context/session archives | Missing | Session DTOs do not establish capture, discovery, retention, or replay. |
| MCP ToolHub | Missing | Tool forwarding is not MCP discovery/invocation; requires transport lifecycle, resolver, auth, timeout, and recursion safeguards. |
| Fusion vision/search/tool augmentation | Missing | Requires explicit orchestration and capability routing; keep separate from pure proxy tool forwarding. |
| Image/video media jobs | Missing | Need job states, queue limits, cancellation/recovery, protected artifacts, and cleanup. |
| AgentClaw/bot relay | Missing | Needs per-platform auth, agent-native sessions, permission handoff, queues, outbox, and managed app lifecycle. |
| MITM/system proxy/network capture | Missing | Separate opt-in certificate/network trust changes; not necessary for direct gateway clients. |
| Provider deeplink/manifest import | Missing | Validate untrusted manifests, constrain fetching, preview changes, and confirm secrets before persistence. |
| Angular browser management | Missing | Authenticated management backend and OpenAPI now exist; Angular workspace/client/session bootstrap do not. |
| Electron/tray/browser integration | Missing | Phase two; includes notifications, app instances, updates, login startup, and explicitly consented browser state access. |
| Docker/publishing | Partial | .NET tool/library packaging verified; no equivalent container deployment, persistence, and browser administration workflow. |

## Beyond the Reference

These are product requirements, not proof of superiority. Existing foundations include layered .NET configuration, official SDK adapters, explicit CLI setup, secret separation, scoped management authentication, credential-free service URLs, and typed versioned OpenAPI. Generated Angular types and the other additions below remain pending.

- Versioned OpenAPI management API with generated Angular client types; structured errors, optimistic concurrency, and separately scoped management/inference credentials.
- Credential-free browser URLs and sanitized diagnostics; opt-in payload capture in every environment, explicit redaction/retention controls, and safe backup/restore.
- Cancellation-aware request budgets and stream-safe failover; no duplicate visible output or hidden unbounded paid attempts.
- Deterministic dry-run routing and protocol conformance fixtures; both fake transport tests and explicitly enabled paid live tests.
- Cross-platform process identity/recovery tests plus migration/upgrade contracts; stable profile IDs without breaking existing CLI commands.

Do not invent a custom telemetry exporter, agent engine, JavaScript compatibility runtime, or another technical-layer project solely for parity. Continue with Core, CLI, ServiceDefaults, Core.Tests, and Cli.Tests; add Angular first and Electron later.

## Implementation Sequence and Acceptance

1. **Management and lifecycle foundation: implemented.** Separate authentication, typed provider/profile CRUD and routing mutations, management-only startup, instance identity, CLI start/stop/reuse, API-only foreground `web`, and authenticated OpenAPI are present. Windows tests cover credential separation, secret-free responses, concurrent fresh starts, listener conflicts, failed-child cleanup, forged/stale state, foreground ownership, and both-listener shutdown. Full deterministic suite: 191 passed; paid/live suites excluded. Cross-platform/process-crash fixtures and installed-tool lifecycle validation remain.
2. **Angular management and complete CLI operations: next.** Add same-origin UI, `ui`, browser open/no-open, packaged assets, session bootstrap without credential URLs, and config import/export. Reuse current `web` and actual URL reporting. Verify desktop/mobile workflows, headless startup, browser-open failure, clean-install setup with no providers, and migration without credential loss.
3. **Gateway policy and interoperability.** Add client-key expiry/revocation/limits, provider editing/discovery, additional ingress/upstream protocols, rules, and bounded failover. Verify stream/tool semantics, multiple provider isolation, disabled/expired keys, concurrent quota reservations, cancellation, non-retriable failures, and no retry after stream commit.
4. **Operational and agent features.** Add request summaries/querying, usage aggregation, redacted optional bodies, archive limits, broader agent CLI adapters, and shared-session leases. Validate OTLP export with a receiver, retention/sampling boundaries, process cleanup, parent-environment isolation, lease crash recovery, and no exposure of upstream credentials to clients.
5. **Advanced capabilities and desktop.** Implement Angular-backed Electron shell, then individually scoped MCP/Fusion/media/plugins/bot/proxy requirements. Each capability needs an explicit threat model, opt-in permission boundary, cross-platform fixture, lifecycle/cancellation tests, and its own supported-feature contract before being advertised.

No completion percentage is assigned: the source folders are not equally weighted features, and the review did not execute every reference flow. Current passing tests validate implemented behavior only. Matching the full reference surface and adding product improvements spans multiple milestones, not one patch.
