using AgentRouter.Core.Api.ErrorHandling.Exceptions;
using AgentRouter.Core.Api.Models.Anthropic;
using AgentRouter.Core.Api.Validation;
using AgentRouter.Core.Constants;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Routes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentRouter.Core.Api.Features.CreatingMessage;

public static class CreateMessageEndpoint
{
    public static IEndpointRouteBuilder MapCreatingMessage(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost(
                "/v1/messages",
                async (
                    HttpContext httpContext,
                    IServiceProvider sp,
                    IOptions<ClaudeGatewayOptions> gatewayOptions,
                    CancellationToken ct
                ) =>
                {
                    using var execution = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    execution.CancelAfter(
                        TimeSpan.FromSeconds(gatewayOptions.Value.RequestTimeoutSeconds)
                    );
                    try
                    {
                        if (!httpContext.Request.HasJsonContentType())
                            throw new BadHttpRequestException(
                                "Content-Type must be application/json.",
                                StatusCodes.Status415UnsupportedMediaType
                            );
                        var anthropicRequest =
                            await httpContext.Request.ReadFromJsonAsync<AnthropicRequest>(
                                execution.Token
                            ) ?? throw new BadRequestException("Request body is required.");
                        AnthropicRequestValidator.ThrowIfInvalid(anthropicRequest);

                        var selectedModel = RouteModelFactory.ResolveRequestModel(
                            gatewayOptions.Value,
                            anthropicRequest.Model
                        );
                        var chatClient = new GatewayInferenceClient(
                            sp,
                            gatewayOptions.Value,
                            httpContext,
                            anthropicRequest.Model
                        );

                        // The provider must receive the bare model name (e.g. "deepseek-flash"),
                        // not the gateway route ("openai/deepseek-flash").
                        var modelId = selectedModel.Model;

                        if (anthropicRequest.Stream is true)
                        {
                            return await AnthropicStreamingRequestHandler.HandleAsync(
                                chatClient,
                                anthropicRequest,
                                modelId,
                                httpContext,
                                execution.Token
                            );
                        }

                        var result = await AnthropicNonStreamingRequestHandler.HandleAsync(
                            chatClient,
                            anthropicRequest,
                            modelId,
                            execution.Token
                        );
                        await result.ExecuteAsync(httpContext);
                        return Results.Empty;
                    }
                    catch (OperationCanceledException)
                        when (execution.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        if (httpContext.Response.HasStarted)
                        {
                            httpContext.Abort();
                            return Results.Empty;
                        }
                        throw new TimeoutException("Inference request deadline exceeded.");
                    }
                }
            )
            .Accepts<AnthropicRequest>("application/json", "application/*+json");

        return builder;
    }

    /// <summary>
    /// Resolves the keyed <see cref="IChatClient"/> for the given model string.
    /// Falls back to the "default" key, then to an unkeyed registration.
    /// </summary>
    internal static IChatClient ResolveChatClient(
        IServiceProvider sp,
        ClaudeGatewayOptions gatewayOptions,
        string model
    )
    {
        var serviceKey = RouteModelFactory.ResolveServiceKey(gatewayOptions, model);

        return sp.GetKeyedService<IChatClient>(serviceKey)
            ?? sp.GetKeyedService<IChatClient>(RouteModelKeys.Default)
            ?? sp.GetService<IChatClient>()
            ?? throw new InvalidOperationException(
                $"No IChatClient registered for model '{model}' (key '{serviceKey}'). "
                    + "Check your provider/model configuration."
            );
    }
}
