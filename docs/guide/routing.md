# Routing And Fallback

Use saved routes for ordinary CLI operation. Use a versioned settings file when you need conditional routing, numeric rewrites, or an explicit fallback chain.

## Defaults And Claude Tiers

```powershell
ar route set work/gpt-4.1
ar route set work/gpt-4.1-mini --tier Haiku
ar service reload
```

A recognized explicit provider/model wins over tier selection. Otherwise a configured Claude tier applies when recognized, then the default route. Native model IDs retain their original case and slashes. Restart foreground `serve` to apply saved changes if you are not using the managed service.

## Conditional Settings

The [configuration reference](../reference/configuration.md#versioned-settings-and-preview) contains a complete versioned file. It uses `SchemaVersion`, `Server`, `Providers`, `Routing`, `Execution`, and `Agents` sections.

Validate and inspect a candidate without contacting providers:

```powershell
ar config validate --file settings.json
ar route preview --file settings.json --task review --subagent worker
```

Preview neither saves nor activates settings. Activate them explicitly in a foreground gateway:

```powershell
ar serve --settings-file settings.json
```

This mode does not merge saved providers into the file. Each enabled provider needs exactly one enabled environment credential, and `Server.TokenEnvironment` must name a nonempty gateway-token variable. Managed inference does not accept this activation switch yet.

## Selection Order

Versioned routing selects an explicit canonical provider/model or alias first, then the first matching rule, then a named agent model, then the default. Every condition supplied in a rule must match. Conditions include agent, task, subagent, workflow, requested model, and literal case-insensitive text.

Clients can supply `X-AR-Agent`, `X-AR-Task`, `X-AR-Subagent`, and `X-AR-Workflow`. Each value is limited to 128 characters without control characters. These are client-reported labels, not authenticated identities. AgentRouter does not automatically inject subagent model tags or tool descriptions.

Canonical model selection bypasses conditional rules. A profile pinned to a canonical model therefore does not automatically use task rules. Only validated `MaxOutputTokens` and `Temperature` rewrites are supported; original client options are not mutated.

## Retry And Fallback

`Execution.MaxAttempts` includes the initial attempt and accepts 1..8. `Execution.DeadlineSeconds` shares one deadline across attempts, delays, and streaming. Ordered, deduplicated candidates are tried before remaining attempts repeat the last candidate. With no fallback, retries use the selected model.

Connection failures without an HTTP status, HTTP 408, 429, and 5xx may retry. Other HTTP errors do not. Backoff starts at 200 ms with exponential jitter and a 5-second ceiling; valid Retry-After headers override that delay and consume the original deadline. SDK retries are disabled in gateway composition. No hedging occurs.

After the first provider stream update is exposed, no retry or provider switching is allowed. A later failure interrupts that stream. Retrying inference can incur extra charges even when the earlier attempt did not finish.

Per-provider/model concurrency and circuit-breaker defaults are described in the [attempt budget reference](../reference/configuration.md#provider-attempt-budget). Credential rotation and token/cost admission are not implemented.