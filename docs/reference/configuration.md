# Configuration Reference

## Persisted Configuration

Use the [CLI configuration commands](cli.md) for normal setup. Shared configuration contracts live in [src/AgentRouter.Core/Contracts/Configuration/GatewayConfigDocument.cs](../../src/AgentRouter.Core/Contracts/Configuration/GatewayConfigDocument.cs); [GatewayConfigurationService](../../src/AgentRouter.Core/Hosting/GatewayConfigurationService.cs) owns mutations, and [GatewayRuntimeConfiguration](../../src/AgentRouter.Core/Hosting/GatewayRuntimeConfiguration.cs) projects enabled providers and routes into runtime options. These services can be reused by future web and Electron management clients.

| Platform | Default data directory |
| --- | --- |
| Windows | `%APPDATA%\claude-code-gateway` (retained compatibility location) |
| macOS/Linux | `$XDG_CONFIG_HOME/claude-code-gateway`, or `~/.config/claude-code-gateway` (retained compatibility location) |
| Override | `CC_GATEWAY_CONFIG_DIR` |

The directory contains `gateway.sqlite` and `secret.key`. Provider credentials and the generated gateway token are encrypted separately from the configuration JSON. Protect the entire directory: possession of the database and key can allow decryption. Stop all gateway processes before taking a file-level backup, and retain SQLite companion files when present. Do not edit a live database.

Once saved configuration includes providers, it owns the provider list, default route, and tier routes. Enabled API-key providers must support OpenAI-compatible requests. IDs `azure` and `github` retain their existing Azure OpenAI and GitHub Models adapter behavior. Native Anthropic/Gemini upstream protocols and OAuth import are not implemented.

Recognized explicit `provider/model` requests take precedence over tier aliases. Unrecognized or missing client models use the configured tier route when applicable, otherwise the default route. Model discovery lists configured enabled models; it does not fetch or refresh upstream catalogs. Restart the host after provider or route changes.

## Legacy Host Options

Primary runtime options are defined in [src/AgentRouter.Core/ClaudeGatewayOptions.cs](../../src/AgentRouter.Core/ClaudeGatewayOptions.cs). When no saved providers exist, existing configuration sources remain available.

Namespace ownership:

- `AgentRouter.Shared.Configuration` owns configuration providers, binding helpers, and `ClaudeGatewayOptions`.
- `AgentRouter.Shared.Contracts` owns shared provider configuration models.
- `AgentRouter.Shared.Routes` owns route parsing and route-model resolution.

Important keys:

- `GatewayBaseAddress`
- `EnableTelemetry`
- `Providers`
- `RouteModel`
- `GatewayAuthToken` (legacy client authentication)
- `ApplyClaudeSettings` (default `false`; explicitly enables global Claude settings writes)

`Microsoft.Extensions.AI.IChatClient` handles upstream requests. Agent Framework is not required: the gateway translates and forwards tool calls and results, while the client agent executes tools.

ServiceDefaults exports logs, metrics, and traces when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured, without an Aspire AppHost. `EnableTelemetry` controls the AI wrapper. Sensitive AI payload capture defaults off; explicitly set `Telemetry:EnableSensitiveData=true` (or `Telemetry__EnableSensitiveData=true` in environment variables) only when the exporter and access policies permit recording prompt and tool content.

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
