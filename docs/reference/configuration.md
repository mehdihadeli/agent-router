# Configuration Reference

Primary gateway options are defined in `src/ClaudeCodeGateway.Core/ClaudeGatewayOptions.cs`.

Namespace ownership:

- `ClaudeCodeGateway.Shared.Configuration` owns configuration providers, binding helpers, and `ClaudeGatewayOptions`.
- `ClaudeCodeGateway.Shared.Contracts` owns shared provider configuration models.
- `ClaudeCodeGateway.Shared.Routes` owns route parsing and route-model resolution.

Important keys:

- `GatewayBaseAddress`
- `EnableTelemetry`
- `Providers`
- `RouteModel`
- `AgentFrameworkOptions:ChatProviderType`
- `AgentFrameworkOptions:ChatEndpoint`
- `AgentFrameworkOptions:ChatApiKey`
- `AgentFrameworkOptions:ChatDeploymentName`
- `AgentFrameworkOptions:ChatModel`
- `AgentFrameworkOptions:Temperature`

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
      "Models": ["gpt-4.1"]
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
