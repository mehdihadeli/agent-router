# claude-code-gateway

Anthropic (Claude Messages API) compatible gateway that proxies requests to **OpenAI-format** providers (OpenAI / Azure OpenAI / GitHub Models, etc.).

## Quick start (Claude Code)

Point Claude Code to this gateway:

```powershell
$env:ANTHROPIC_AUTH_TOKEN = "test"   # any non-empty value
$env:ANTHROPIC_BASE_URL   = "http://localhost:4600"

claude --model gpt-4.1
```

## Configuration

The gateway loads configuration from multiple sources.

### Configuration sources and priority

Higher items override lower items when the same key exists.

1. **Command-line args** (default ASP.NET Core)
2. **Environment variables** (default ASP.NET Core)
3. **appsettings.json / appsettings.{Environment}.json** (default ASP.NET Core)
4. **User config file**: `~/claude-code-gateway/config.json` (optional)
5. **Claude Code settings**: `~/.claude/settings.json` (optional)
6. **Local .env file**: `./.env` (optional)

Notes:
- `./.env` is meant for local dev convenience.
- Environment variables should use `__` for nesting, e.g. `AgentFrameworkOptions__ChatApiKey`.

### Environment variable interpolation inside JSON / .env

For string values inside the JSON config files and the local `.env`, the gateway supports:
- `$VAR_NAME`
- `${VAR_NAME}`

If the environment variable is not set, the placeholder is left unchanged.

### Creating the user config file automatically

If you want the gateway to create `~/claude-code-gateway/config.json` when it doesn’t exist, set:

```powershell
$env:CCG_CREATE_USER_CONFIG = "true"
```

## Supported configuration keys

Most settings live under the `AgentFrameworkOptions` section (see `src/Common/AIProviders/AgentFrameworkOptions.cs`).
Common keys:

- `AgentFrameworkOptions:ChatProviderType` (e.g. `OpenAI`, `Azure`, or `GitHubModels`)
- `AgentFrameworkOptions:ChatEndpoint`
- `AgentFrameworkOptions:ChatApiKey`
- `AgentFrameworkOptions:ChatDeploymentName` (required for Azure)
- `AgentFrameworkOptions:ChatModel`
- `AgentFrameworkOptions:Temperature`

## Example: ~/claude-code-gateway/config.json

This file is **flat** (no `ClaudeGatewayOptions` wrapper).

```json
{
  "GatewayBaseAddress": "http://localhost:4000",
  "DefaultProviderType": "Azure",
  "EnableTelemetry": false,
  "Providers": [
    {
      "ProviderType": "Azure",
      "BaseAddress": "$AZURE_OPENAI_ENDPOINT",
      "ApiKey": "$AZURE_OPENAI_API_KEY",
      "Models": ["gpt-4.1", "gpt-4o-mini"]
    }
  ]
}
```

## Example: environment variables (PowerShell)

```powershell
$env:AgentFrameworkOptions__ChatProviderType   = "Azure"
$env:AgentFrameworkOptions__ChatEndpoint       = "https://YOUR-RESOURCE.openai.azure.com/"
$env:AgentFrameworkOptions__ChatApiKey         = "..."
$env:AgentFrameworkOptions__ChatDeploymentName = "gpt-4.1"
$env:AgentFrameworkOptions__ChatModel          = "gpt-4.1"
$env:AgentFrameworkOptions__Temperature        = "0.2"
```

## Example: GitHub Models provider

```json
{
  "GatewayBaseAddress": "http://localhost:4000",
  "EnableTelemetry": false,
  "Providers": [
    {
      "ProviderType": "GitHubModels",
      "BaseAddress": "https://models.github.ai/inference/",
      "ApiKey": "$GITHUB_TOKEN",
      "Models": ["openai/gpt-4.1"]
    }
  ],
  "RouteModel": {
    "DefaultModel": "github/openai/gpt-4.1",
    "ThinkingModel": "github/openai/gpt-4.1",
    "SearchModel": "github/openai/gpt-4.1"
  }
}
```

Notes:
- GitHub Models is registered through the OpenAI-compatible client path.
- Provider route name is `github`, so route keys look like `github/<model-id>`.

