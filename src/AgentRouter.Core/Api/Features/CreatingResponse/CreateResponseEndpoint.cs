using AgentRouter.Core.Api.Converters;
using AgentRouter.Core.Api.ErrorHandling.Exceptions;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Routes;
using Microsoft.Extensions.Options;

namespace AgentRouter.Core.Api.Features.CreatingResponse;

public static class CreateResponseEndpoint
{
    public static IEndpointRouteBuilder MapCreatingResponse(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost(
                "/v1/responses",
                async (
                    HttpContext context,
                    IServiceProvider services,
                    IOptions<ClaudeGatewayOptions> options,
                    CancellationToken cancellationToken
                ) =>
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken
                    );
                    deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));
                    try
                    {
                        if (!context.Request.HasJsonContentType())
                            throw new BadHttpRequestException(
                                "Content-Type must be application/json.",
                                StatusCodes.Status415UnsupportedMediaType
                            );
                        var request =
                            await context.Request.ReadFromJsonAsync<ResponsesRequest>(
                                deadline.Token
                            ) ?? throw new BadRequestException("Request body is required.");
                        var (messages, chatOptions) = ResponsesConverter.Convert(request);
                        var route = RouteModelFactory.ResolveRequestModel(
                            options.Value,
                            request.Model
                        );
                        var client = new GatewayInferenceClient(
                            services,
                            options.Value,
                            context,
                            request.Model
                        );
                        chatOptions.ModelId = route.Model;
                        if (request.Stream)
                            await ResponsesStreamWriter.WriteAsync(
                                context,
                                client.GetStreamingResponseAsync(
                                    messages,
                                    chatOptions,
                                    deadline.Token
                                ),
                                route.Model,
                                deadline.Token
                            );
                        else
                        {
                            var response = await client.GetResponseAsync(
                                messages,
                                chatOptions,
                                deadline.Token
                            );
                            await context.Response.WriteAsJsonAsync(
                                ResponsesConverter.ConvertResponse(
                                    response,
                                    response.ModelId ?? route.Model
                                ),
                                deadline.Token
                            );
                        }
                    }
                    catch (OperationCanceledException)
                        when (deadline.IsCancellationRequested
                            && !cancellationToken.IsCancellationRequested
                        )
                    {
                        if (context.Response.HasStarted)
                            context.Abort();
                        else
                            throw new TimeoutException("Inference request deadline exceeded.");
                    }
                }
            )
            .Accepts<ResponsesRequest>("application/json", "application/*+json");
        return endpoints;
    }
}
