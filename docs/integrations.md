# Protocol Compatibility

The client protocol and upstream provider protocol are separate choices. An agent can use Anthropic Messages while AgentRouter sends OpenAI Chat Completions to the selected provider. Provider and model names do not establish protocol compatibility.

## Client APIs

| Endpoint | Supported behavior |
| --- | --- |
| `POST /v1/messages` | Anthropic-style text, system instructions, function tools and results; JSON and SSE. |
| `POST /v1/responses` | Stateless text, function tools and results, and text reasoning output/history; JSON and SSE. See the limits below. |
| `GET /v1/models` | Configured enabled models. This does not discover models from providers. |

Inference uses the gateway token, not the upstream API key. See [agent setup](guide/agents.md).

## Upstream APIs

| Provider transport | Availability |
| --- | --- |
| OpenAI Chat Completions | Supported through the OpenAI SDK, including configured compatible endpoints. |
| Anthropic Messages | Supported through the native Anthropic SDK. |
| Azure OpenAI | Existing Azure adapter; deployment and endpoint must match your resource. |
| GitHub Models | Existing GitHub adapter; model access depends on your account. |
| Native OpenAI Responses or Gemini | Not implemented as upstream transports. |

OpenRouter, DeepSeek, Z.AI and other compatible services need an endpoint, protocol, and exact model ID that support your workload. Text success alone does not prove streaming or tool compatibility. Provider checks send a real request and may incur charges. See [providers](guide/providers.md).

## Responses And Codex Limits

Send full history with `store=false`. Stored continuations, `previous_response_id`, background responses, reasoning-effort controls, nonempty reasoning summaries, strict schema enforcement, multimodal content, and custom/grammar/hosted tools are not supported. Unsupported request fields are rejected.

Opaque reasoning history is provider-specific; do not treat it as portable continuation state. The experimental Codex launcher and this Responses subset do not establish support for every Codex workflow, including custom patch tools.

Tool execution stays in the client. AgentRouter does not run tools, rewrite local files, provide MCP aggregation, or synthesize agent execution traces. See [observability](guide/observability.md) for telemetry boundaries and [agent profiles](reference/agent-profiles.md) for launcher limits.
