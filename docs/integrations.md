# Integrations

The current integration boundary is:

```text
Claude Code or Anthropic client
        |
        v
AgentRouter.Api
        |
        v
Claude/OpenAI/Gemini translation layer
        |
        v
Microsoft.Extensions.AI
        |
        v
OpenAI-compatible provider
```

## Client side

Supported clients speak the Anthropic Messages API, including Claude Code and compatible Anthropic SDK clients.

## Provider side

Current provider registrations support:

- OpenAI-compatible endpoints.
- Azure OpenAI.
- GitHub Models.

The gateway translates requests and responses between Claude, OpenAI, and Gemini formats. SSE streaming and tool definitions/function calls are preserved across the translation boundary.

Provider registration belongs in `src/AgentRouter.Providers.OpenAI`. Protocol conversion belongs in `src/AgentRouter.Api`. Route and configuration policy belongs in `src/AgentRouter.Core` and `src/AgentRouter.Hosting`.

The `/v1/models` integration provides cached model discovery with on-demand refresh. Usage statistics are updated from events and can be queried for today, yesterday, this week, or this month.
